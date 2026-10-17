using System;
using System.Collections.Generic;
using Verse;

namespace Phinix.LegacyRedPacketExtension.Client
{
    // Display aggregation only. Original custody, state and send boundaries stay intact.
    internal sealed class RedPacketItemChoice
    {
        private readonly List<RedPacketAvailableItem> sources = new List<RedPacketAvailableItem>();
        internal RedPacketAvailableItem Representative => sources[0];
        internal RedPacketAvailableItem Single => sources.Count == 1 ? sources[0] : null;
        internal long TotalCount { get; private set; }
        internal int DraftLimit => (int)Math.Min(int.MaxValue, TotalCount);
        internal bool HasMap { get; private set; }
        internal bool HasInventory { get; private set; }
        internal int SourceCount => sources.Count;
        internal string Label => Representative.DisplayLabel;
        internal bool Contains(RedPacketAvailableItem item) => sources.Contains(item);
        internal IReadOnlyList<RedPacketAvailableItem> Sources => sources;
        // Map-source aggregation is wired to the existing physical transaction.
        // Inventory records keep their existing reservation ownership boundary.
        internal long MapCount { get { long count=0; foreach(var source in sources) if(!source.IsInventory) count+=source.Count; return count; } }
        internal RedPacketAvailableItem CreateSelection()
        {
            if (Single != null) return Single;
            var map = sources.FindAll(source => !source.IsInventory);
            if (map.Count > 0) return RedPacketAvailableItem.CombinePhysical(map);
            return null;
        }

        internal static List<RedPacketItemChoice> Build(IEnumerable<RedPacketAvailableItem> items)
        {
            var result = new List<RedPacketItemChoice>();
            var index = new Dictionary<Tuple<ThingDef, ThingDef, ThingStyleDef, int>, RedPacketItemChoice>();
            foreach (var item in items)
            {
                int count = item.Count;
                if (count < 1 || item.ThingDef == null) continue;
                var key = Tuple.Create(item.ThingDef, item.StuffDef, item.StyleDef, item.DisplayQuality);
                RedPacketItemChoice group;
                if (item.IsIndividual || !index.TryGetValue(key, out group))
                {
                    group = new RedPacketItemChoice(); if (!item.IsIndividual) index.Add(key, group); result.Add(group);
                }
                group.sources.Add(item); group.TotalCount = checked(group.TotalCount + count);
                if (item.IsInventory) group.HasInventory = true; else group.HasMap = true;
            }
            return result;
        }
    }
}
