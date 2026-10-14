using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using System.Diagnostics;
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
            // Known per-unit and provably stateless components require identical complete state; unclassified state stays single-stack.
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
            if (stack == null || count < 1 || stack.Things == null || stack.Things.Count == 0 || stack.Things.Count > MaxStacks ||
                stack.Things.Any(t => t == null || t.Destroyed || t.stackCount < 1) ||
                stack.Things.Distinct().Count() != stack.Things.Count)
                throw Incompatible("SelectionChanged");
            // One packet keeps the existing wire template. Choose the complete
            // state represented by most units, then exhaust that state before
            // supplementing from other compatible sources. Never absorb Things.
            var states=new List<ExactGroup>();
            var coarse=new CoarseKey(stack.Things[0]);
            canStack=canStack??((a,b)=>a.CanStackWith(b));
            foreach(var thing in stack.Things)
            {
                if(!coarse.Equals(new CoarseKey(thing)) || (!ReferenceEquals(thing,stack.Things[0]) && !GameCanGroup(stack.Things[0],thing,canStack)))throw Incompatible("StackabilityDifferent");
                var snapshot=serialize(thing);
                if(snapshot==null || snapshot.StackCount!=thing.stackCount)throw Incompatible("SnapshotInvalid");
                string state=ComparableState(snapshot);
                var group=states.FirstOrDefault(g=>g.State==state);
                if(group==null){group=new ExactGroup{State=state};states.Add(group);}
                group.Things.Add(thing);
            }
            var ordered=states.OrderByDescending(g=>g.Things.Sum(t=>(long)t.stackCount)).ToArray();
            var sources=new List<Thing>();long available=0;
            foreach(var thing in ordered.SelectMany(g=>g.Things.OrderByDescending(t=>t.stackCount)))
            {
                sources.Add(thing);available+=thing.stackCount;
                if(available>=count)break;
            }
            if(available<count)throw Incompatible("SelectionChanged");
            for(int i=0;i<sources.Count;i++)for(int j=0;j<i;j++)
                if(!GameCanGroup(sources[i],sources[j],canStack))throw Incompatible("StackabilityDifferent");
            TradeItemSnapshot template=CaptureCount(ordered[0].Things[0],count,serialize);
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

        private sealed class CoarseKey : IEquatable<CoarseKey>
        {
            private readonly ThingDef definition,stuff;
            internal CoarseKey(Thing thing){definition=thing.def;stuff=thing.Stuff;}
            public bool Equals(CoarseKey other)=>other!=null&&ReferenceEquals(definition,other.definition)&&ReferenceEquals(stuff,other.stuff);
            public override bool Equals(object other)=>Equals(other as CoarseKey);
            public override int GetHashCode(){unchecked{return (definition?.GetHashCode()??0)*31+(stuff?.GetHashCode()??0);}}
        }
        private static bool GameCanGroup(Thing a,Thing b,Func<Thing,Thing,bool> canStack=null)
        {
            // CanStackWith alone does not enforce stackLimit (weapons can have
            // matching quality but still have a capacity of one).
            if(a.def.stackLimit<=1 || b.def.stackLimit<=1)return false;
            return canStack==null?a.CanStackWith(b)&&b.CanStackWith(a):canStack(a,b)&&canStack(b,a);
        }
        private sealed class ExactGroup
        {
            internal string State;
            internal readonly List<Thing> Things=new List<Thing>();
        }

        // Refresh-local index: cheap necessary conditions, then fingerprint, then exact state.
        // Never cache mutable item snapshots across refreshes. Send still captures intact sources again.
        internal static IEnumerable<StackedThings> GroupPhysical(IEnumerable<StackedThings> stacks,
            Action<string, LogLevel> log, Func<Thing, TradeItemSnapshot> serialize = null, Func<string,string> fingerprint = null)
        {
            return GroupPhysicalSteps(stacks,log,serialize,fingerprint).Where(group=>group!=null);
        }
        internal static GroupingJob BeginGrouping(IEnumerable<StackedThings> stacks, Action<string,LogLevel> log,
            Func<Thing,TradeItemSnapshot> serialize=null)=>new GroupingJob(GroupPhysicalSteps(stacks,log,serialize,null));
        internal sealed class GroupingJob : IDisposable
        {
            private IEnumerator<StackedThings> work;
            private readonly List<StackedThings> results=new List<StackedThings>();
            internal bool Completed { get; private set; }
            internal IReadOnlyList<StackedThings> Results=>Completed?results:throw new InvalidOperationException("GroupingIncomplete");
            internal GroupingJob(IEnumerable<StackedThings> steps){work=steps.GetEnumerator();}
            // Cooperative main-thread slice; one serializer call cannot be preempted.
            internal bool Advance(double milliseconds=2,int maximumSteps=2048,Action<StackedThings> ready=null)
            {
                if(milliseconds<=0||double.IsNaN(milliseconds)||double.IsInfinity(milliseconds)||maximumSteps<1)throw new ArgumentOutOfRangeException();
                if(work==null)return Completed;
                long start=Stopwatch.GetTimestamp();int steps=0;
                try
                {
                    do
                    {
                        if(!work.MoveNext()){Completed=true;work.Dispose();work=null;return true;}
                        if(work.Current!=null){results.Add(work.Current);ready?.Invoke(work.Current);}
                        steps++;
                    }while(steps<maximumSteps&&(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency<milliseconds);
                    return false;
                }
                catch {Dispose();throw;}
            }
            public void Dispose(){var old=work;work=null;old?.Dispose();if(!Completed)results.Clear();}
        }
        private static IEnumerable<StackedThings> GroupPhysicalSteps(IEnumerable<StackedThings> stacks,
            Action<string,LogLevel> log,Func<Thing,TradeItemSnapshot> serialize,Func<string,string> fingerprint)
        {
            serialize=serialize??TradeItemConverter.ConvertThingFromVerse;
            int inspected=0,failures=0,characterBudget=4*1024*1024;
            string firstFailure=null;
            var buckets=new Dictionary<CoarseKey,List<Thing>>();
            foreach(var stack in stacks)
                foreach(var thing in stack.Things)
                {
                    yield return null;
                    if(thing==null||thing.Destroyed||thing.def==null||thing.stackCount<1)continue;
                    if(thing.def.stackLimit<=1){yield return new PhysicalGroup(new[]{thing},null);continue;}
                    var key=new CoarseKey(thing);
                    if(!buckets.TryGetValue(key,out var bucket))buckets[key]=bucket=new List<Thing>();
                    bucket.Add(thing);
                }
            using(var hash=SHA256.Create())
            foreach(var bucket in buckets.Values)
            {
                // A singleton has no merge candidate and needs no save/parse/hash work.
                if(bucket.Count==1){yield return new PhysicalGroup(bucket,null);continue;}
                var index=new Dictionary<string,List<ExactGroup>>(StringComparer.Ordinal);
                foreach(var thing in bucket)
                {
                    yield return null;
                    string state=null,key=null;
                    if(inspected<MaxStacks&&characterBudget>0)
                    {
                        inspected++;
                        try
                        {
                            var snapshot=serialize(thing);
                            if(snapshot==null||snapshot.StackCount!=thing.stackCount)throw Incompatible("SnapshotInvalid");
                            state=ComparableState(snapshot,128*1024);characterBudget-=state.Length;
                            if(state.Length<=128*1024&&characterBudget>=0)
                                key=fingerprint==null?Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(state))):fingerprint(state);
                        }
                        catch(Exception exception)
                        {
                            // Failed/oversized decoding also consumes work. Otherwise every
                            // rejected source bypasses the aggregate inspection budget.
                            characterBudget=Math.Max(0,characterBudget-128*1024);
                            failures++;firstFailure=firstFailure??exception.GetType().Name;
                        }
                    }
                    if(key==null){yield return new PhysicalGroup(new[]{thing},"Phinix_legacyRedpacket_singleStackOnly");continue;}
                    if(!index.TryGetValue(key,out var candidates))index[key]=candidates=new List<ExactGroup>();
                    ExactGroup group=null;
                    foreach(var candidate in candidates)
                    {
                        if(!string.Equals(candidate.State,state,StringComparison.Ordinal))continue;
                        bool allowed=true;
                        foreach(var source in candidate.Things)
                        {yield return null;if(!GameCanGroup(thing,source)){allowed=false;break;}}
                        if(allowed){group=candidate;break;}
                    }
                    if(group==null){group=new ExactGroup{State=state};candidates.Add(group);}
                    group.Things.Add(thing);
                    yield return null;
                }
                var compatible=new List<List<ExactGroup>>();
                foreach(var stateGroup in index.Values.SelectMany(groups=>groups))
                {
                    List<ExactGroup> destination=null;
                    foreach(var groups in compatible)
                    {
                        bool allowed=true;
                        foreach(var prior in groups)
                        {
                            foreach(var a in prior.Things)
                            {
                                foreach(var b in stateGroup.Things)
                                {yield return null;if(!GameCanGroup(a,b)){allowed=false;break;}}
                                if(!allowed)break;
                            }
                            if(!allowed)break;
                        }
                        if(allowed){destination=groups;break;}
                    }
                    if(destination==null){destination=new List<ExactGroup>();compatible.Add(destination);}
                    destination.Add(stateGroup);
                }
                foreach(var groups in compatible)
                {
                    var ordered=groups.OrderByDescending(g=>g.Things.Sum(t=>(long)t.stackCount));
                    yield return new PhysicalGroup(ordered.SelectMany(g=>g.Things),groups.Count>1?"Phinix_legacyRedpacket_dominantTemplate":null);
                }
            }
            if(failures>0)log?.Invoke("[RedPacket] Stack inspection limited failures="+failures+" reason="+firstFailure+"; affected stacks remain individually selectable.",LogLevel.WARNING);
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
                 comp.GetType() == typeof(CompIngredients) || IsUnusedEquippable(comp) || IsStatelessComponent(comp)));
        }

        // Base resource items such as WoodLog have CompEquippable. Its lazy
        // verb tracker is not transferable per-unit combat state until verbs
        // have been initialized. Never call AllVerbs here: that mutates it.
        // Verified in both game DLLs; absent/changed private storage fails closed.
        private static readonly FieldInfo equippableVerbs = typeof(VerbTracker).GetField("verbs", BindingFlags.Instance | BindingFlags.NonPublic);
        private static bool IsUnusedEquippable(object component)
        {
            if(component.GetType()!=typeof(CompEquippable) || equippableVerbs==null)return false;
            var tracker=((CompEquippable)component).VerbTracker;
            return tracker!=null && equippableVerbs.GetValue(tracker)==null;
        }

        private static readonly Dictionary<Type, bool> statelessComponents = new Dictionary<Type, bool>();
        private static bool IsStatelessComponent(object component)
        {
            Type type = component.GetType();
            lock (statelessComponents)
            {
                if (statelessComponents.TryGetValue(type, out bool result)) return result;
                result = typeof(ThingComp).IsAssignableFrom(type);
                for (Type current = type; result && current != typeof(ThingComp); current = current.BaseType)
                    result = current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Length == 0;
                foreach (string method in new[] { "PostExposeData", "PreAbsorbStack", "PostSplitOff", "AllowStackWith" })
                    result &= type.GetMethod(method, BindingFlags.Public | BindingFlags.Instance)?.DeclaringType == typeof(ThingComp);
                statelessComponents[type] = result;
                return result;
            }
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

        private static XmlDocument ReadDocument(TradeItemSnapshot snapshot, int maximumXmlBytes = MaxXmlBytes)
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
                    if (output.Length + count > maximumXmlBytes) throw Incompatible();
                    output.Write(buffer, 0, count);
                }
                output.Position = 0;
                using (var reader = XmlReader.Create(output, new XmlReaderSettings {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = maximumXmlBytes }))
                    document.Load(reader);
            }
            XmlElement root = document.DocumentElement;
            if (root == null || root.Name != "saveable") throw Incompatible();
            return document;
        }

        internal static string ComparableState(TradeItemSnapshot snapshot, int maximumXmlBytes = MaxXmlBytes)
        {
            XmlDocument document = ReadDocument(snapshot, maximumXmlBytes);
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
