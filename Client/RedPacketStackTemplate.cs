using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
            if (things.Any(t => !IsKnownPerUnitStack(t))) throw Incompatible();
            string state = null;
            foreach (Thing thing in things)
            {
                if (!ReferenceEquals(first, thing) && (!canStack(first, thing) || !canStack(thing, first)))
                    throw Incompatible();
                TradeItemSnapshot snapshot = serialize(thing);
                if (snapshot.StackCount != thing.stackCount) throw Incompatible();
                string candidate = ComparableState(snapshot);
                if (state != null && !string.Equals(state, candidate, StringComparison.Ordinal)) throw Incompatible();
                state = candidate;
            }
            // No absorption/destruction: rollback and subsequent claims still own the original physical stacks.
            return CaptureCount(first, expectedCount, serialize);
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

        private static bool IsKnownPerUnitStack(Thing thing)
        {
            if (thing.GetType() == typeof(Thing)) return true;
            if (thing.GetType() != typeof(ThingWithComps)) return false;
            var comps = ((ThingWithComps)thing).AllComps;
            return comps == null || comps.All(comp => comp != null &&
                (comp.GetType() == typeof(CompForbiddable) || comp.GetType() == typeof(CompQuality) ||
                 comp.GetType() == typeof(CompColorable) || comp.GetType() == typeof(CompRottable) ||
                 comp.GetType() == typeof(CompIngredients)));
        }

        private static InvalidOperationException Incompatible()
        { return new InvalidOperationException("Phinix_legacyRedpacket_incompatibleStacks".Localize().ToString()); }

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

        internal static string ComparableState(TradeItemSnapshot snapshot)
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
}
