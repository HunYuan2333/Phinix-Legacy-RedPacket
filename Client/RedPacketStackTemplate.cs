using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Phinix.TradeExtension.Client;
using Utils;
using System.Xml;
using PhinixClient.Trade;
using RimWorld;
using Verse;

namespace Phinix.LegacyRedPacketExtension.Client
{
    /// <summary>One wire template for equivalent stacks; original Things remain in sender custody.</summary>
    internal static class RedPacketStackTemplate
    {
        private const int MaxStacks = 1000;
        private const int MaxXmlBytes = 16 * 1024 * 1024;

        internal static TradeItemSnapshot Capture(IList<Thing> things, int expectedCount,
            Func<Thing, TradeItemSnapshot> serialize = null, Func<Thing, Thing, bool> canStack = null)
        {
            serialize = serialize ?? TradeItemConverter.ConvertThingFromVerse;
            canStack = canStack ?? ((a, b) => a.CanStackWith(b));
            if (things == null || things.Count < 1 || things.Count > MaxStacks || expectedCount < 1 ||
                things.Any(t => t == null || t.Destroyed || t.stackCount < 1) ||
                things.Distinct().Count() != things.Count ||
                things.Sum(t => (long)t.stackCount) != expectedCount)
                throw new InvalidOperationException("The selected stack count changed while creating the red packet.");
            Thing first = things[0];
            if (things.Count == 1) return CaptureCount(first, expectedCount, serialize);

            // CanStackWith alone is insufficient: mods may average or accumulate state when merging.
            // Known per-unit components require identical complete state; unknown implementations stay single-stack.
            if (things.Any(t => !IsKnownPerUnitStack(t))) throw Incompatible("UnsupportedComponent");
            string state = null;
            foreach (Thing thing in things)
            {
                if (!ReferenceEquals(first, thing) && (!canStack(first, thing) || !canStack(thing, first)))
                    throw Incompatible("StackabilityDifferent");
                TradeItemSnapshot snapshot = serialize(thing);
                if (snapshot == null || snapshot.StackCount != thing.stackCount) throw Incompatible("SnapshotInvalid");
                string candidate = ComparableState(snapshot);
                if (state != null && !string.Equals(state, candidate, StringComparison.Ordinal)) throw Incompatible();
                state = candidate;
            }
            // No absorption/destruction: rollback and subsequent claims still own the original physical stacks.
            return CaptureCount(first, expectedCount, serialize);
        }

        // Inspect intact sources before RimWorld SplitOff creates a new entity with
        // its own reveal/placement history. Do not erase transferable fields to make it match.
        internal static PhysicalSelection PreparePhysical(StackedThings stack, int count,
            Func<Thing, TradeItemSnapshot> serialize = null, Func<Thing, Thing, bool> canStack = null)
        {
            serialize = serialize ?? TradeItemConverter.ConvertThingFromVerse;
            if (stack == null || count < 1 || stack.Things == null || stack.Things.Count > MaxStacks ||
                stack.Things.Any(t => t == null || t.Destroyed || t.stackCount < 1) ||
                stack.Things.Distinct().Count() != stack.Things.Count)
                throw Incompatible("SelectionChanged");
            var sources = new List<Thing>();
            Thing sufficient = stack.Things.FirstOrDefault(t => t.stackCount >= count);
            if (sufficient != null) sources.Add(sufficient);
            else
            {
                long available = 0;
                foreach (Thing thing in stack.Things)
                {
                    sources.Add(thing); available += thing.stackCount;
                    if (available >= count) break;
                }
                if (available < count) throw Incompatible("SelectionChanged");
            }
            if (sources.Count == 0) throw Incompatible("SelectionChanged");
            // Capture validates every intact source with the inventory's same state policy.
            long total = sources.Sum(t => (long)t.stackCount);
            if (total > int.MaxValue) throw Incompatible("SelectionChanged");
            Capture(sources, (int)total, serialize, canStack);
            TradeItemSnapshot template = CaptureCount(sources[0], count, serialize);
            XmlDocument document = ReadDocument(template);
            // An intact source may be spawned. A wire template must never claim a sender map slot.
            foreach (XmlNode node in document.DocumentElement.ChildNodes.Cast<XmlNode>().Where(n => n.Name == "map"))
                node.InnerText = "-1";
            byte[] payload;
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(document.OuterXml);
                    gzip.Write(bytes, 0, bytes.Length);
                }
                payload = output.ToArray();
            }
            if (payload.Length > StatefulTradeItemProtocol.MaxStatePayloadBytes) throw Incompatible("SnapshotInvalid");
            template = new TradeItemSnapshot(template.DefName, count, template.HitPoints, template.Quality,
                template.StuffDefName, template.InnerItem, template.StateCodecId, payload);
            return new PhysicalSelection(new StackedThings(sources) { Selected = count }, template);
        }

        internal sealed class PhysicalSelection
        {
            internal PhysicalSelection(StackedThings sources, TradeItemSnapshot template)
            { Sources = sources; Template = template; }
            internal StackedThings Sources { get; }
            internal TradeItemSnapshot Template { get; }
        }

        internal sealed class PhysicalGroup : StackedThings
        {
            internal PhysicalGroup(IEnumerable<Thing> things, string restrictionKey) : base(things)
            { RestrictionKey = restrictionKey; }
            internal string RestrictionKey { get; }
        }

        // Group once during the UI refresh, using the same complete-state policy as send.
        // Unknown components stay individually selectable; no per-mod exception list.
        internal static IEnumerable<StackedThings> GroupPhysical(IEnumerable<StackedThings> stacks,
            Action<string, LogLevel> log, Func<Thing, TradeItemSnapshot> serialize = null)
        {
            serialize = serialize ?? TradeItemConverter.ConvertThingFromVerse;
            int inspected = 0, failures = 0, characterBudget = 4 * 1024 * 1024;
            string firstFailure = null;
            foreach (StackedThings stack in stacks)
            {
                var groups = new Dictionary<string, List<Thing>>(StringComparer.Ordinal);
                foreach (Thing thing in stack.Things)
                {
                    string key = null;
                    if (IsKnownPerUnitStack(thing) && inspected < MaxStacks && characterBudget > 0)
                    {
                        inspected++;
                        try
                        {
                            string state = ComparableState(serialize(thing));
                            characterBudget -= state.Length;
                            if (state.Length <= 128 * 1024 && characterBudget >= 0)
                                using (var hash = SHA256.Create())
                                    key = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(state)));
                        }
                        catch (Exception exception)
                        {
                            failures++; firstFailure = firstFailure ?? exception.GetType().Name;
                        }
                    }
                    if (key == null) { yield return new PhysicalGroup(new[] { thing }, "Phinix_legacyRedpacket_singleStackOnly"); continue; }
                    if (!groups.TryGetValue(key, out List<Thing> group)) groups[key] = group = new List<Thing>();
                    if (group.Count > 0 && (!thing.CanStackWith(group[0]) || !group[0].CanStackWith(thing)))
                    { yield return new PhysicalGroup(new[] { thing }, "Phinix_legacyRedpacket_singleStackOnly"); continue; }
                    group.Add(thing);
                }
                foreach (List<Thing> group in groups.Values)
                    yield return new PhysicalGroup(group, groups.Count > 1 ? "Phinix_legacyRedpacket_stateGroup" : null);
            }
            if (failures > 0) log?.Invoke("[RedPacket] Stack inspection limited failures=" + failures +
                " reason=" + firstFailure + "; affected stacks remain individually selectable.", LogLevel.WARNING);
        }

        internal static TradeItemSnapshot CaptureCount(Thing thing, int count, Func<Thing, TradeItemSnapshot> serialize = null)
        {
            if (thing == null || count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            serialize = serialize ?? TradeItemConverter.ConvertThingFromVerse;
            int original = thing.stackCount;
            try
            {
                thing.stackCount = count;
                TradeItemSnapshot snapshot = serialize(thing);
                if (snapshot == null || snapshot.StackCount != count) throw Incompatible();
                return snapshot;
            }
            finally { thing.stackCount = original; }
        }

        internal static bool IsKnownPerUnitStack(Thing thing)
        {
            if (thing.GetType() == typeof(Thing)) return true;
            if (thing.GetType() != typeof(ThingWithComps)) return false;
            var comps = ((ThingWithComps)thing).AllComps;
            return comps == null || comps.All(comp => comp != null &&
                (comp.GetType() == typeof(CompForbiddable) || comp.GetType() == typeof(CompQuality) ||
                 comp.GetType() == typeof(CompColorable) || comp.GetType() == typeof(CompRottable) ||
                 comp.GetType() == typeof(CompIngredients)));
        }

        private static RedPacketSelectionException Incompatible(string reason = "StateDifferent")
        { return new RedPacketSelectionException(reason); }

        internal static IList<int> DeliveryCounts(int count, int stackLimit)
        {
            if (count < 1 || stackLimit < 1 || ((long)count - 1) / stackLimit + 1 > MaxStacks)
                throw new InvalidOperationException("Red packet delivery exceeds its stack limit.");
            var parts = new List<int>();
            while (count > 0)
            {
                int part = Math.Min(stackLimit, count);
                parts.Add(part); count -= part;
            }
            return parts;
        }

        private static XmlDocument ReadDocument(TradeItemSnapshot snapshot)
        {
            if (snapshot == null || snapshot.StateCodecId != StatefulTradeItemProtocol.ScribeCodecId ||
                snapshot.StatePayload == null || snapshot.StatePayload.Length < 1 ||
                snapshot.StatePayload.Length > StatefulTradeItemProtocol.MaxStatePayloadBytes)
                throw Incompatible();
            var document = new XmlDocument { XmlResolver = null };
            using (var input = new MemoryStream(snapshot.StatePayload, false))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                byte[] buffer = new byte[8192]; int count;
                while ((count = gzip.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + count > MaxXmlBytes) throw Incompatible();
                    output.Write(buffer, 0, count);
                }
                output.Position = 0;
                using (var reader = XmlReader.Create(output, new XmlReaderSettings {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxXmlBytes }))
                    document.Load(reader);
            }
            XmlElement root = document.DocumentElement;
            if (root == null || root.Name != "saveable") throw Incompatible();
            return document;
        }

        internal static string ComparableState(TradeItemSnapshot snapshot)
        {
            XmlDocument document = ReadDocument(snapshot);
            XmlElement root = document.DocumentElement;
            // Only top-level identity, quantity and transfer placement are excluded.
            // Keep tickDelta, quest tags, graphics, health, stuff and every nested component field.
            foreach (string name in new[] { "id", "stackCount", "map", "pos", "rot", "spawnedTick", "despawnedTick" })
            {
                var nodes = root.ChildNodes.Cast<XmlNode>().Where(n => n.Name == name).ToArray();
                foreach (XmlNode node in nodes) root.RemoveChild(node);
            }
            return root.OuterXml;
        }
    }
    internal sealed class RedPacketSelectionException : InvalidOperationException
    {
        internal RedPacketSelectionException(string reason)
            : base("Phinix_legacyRedpacket_incompatibleStacks".Localize().ToString()) { Reason = reason; }
        internal string Reason { get; }
    }

}
