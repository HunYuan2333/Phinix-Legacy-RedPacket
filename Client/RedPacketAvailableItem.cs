using System;
using System.Collections.Generic;
using System.Linq;
using Phinix.InventoryExtension;
using Phinix.TradeExtension.Client;
using RimWorld;
using Verse;

namespace Phinix.LegacyRedPacketExtension.Client
{
    internal sealed class RedPacketAvailableItem : IDisposable
    {
        private readonly StackedThings physical;
        private readonly Thing preview;
        private int selected;
        internal IReadOnlyList<RedPacketAvailableItem> CompositeSources { get; private set; }
        internal bool IsComposite => CompositeSources != null;
        internal bool IsIndividual => !RedPacketStackTemplate.CanAutomaticallyGroup(ThingDef);
        internal IEnumerable<Thing> PhysicalThings => IsInventory ? Enumerable.Empty<Thing>() : physical.Things;

        private RedPacketAvailableItem(StackedThings physical, InventoryEntry entry, Thing preview)
        {
            this.physical = physical;
            Entry = entry;
            this.preview = preview;
        }

        public InventoryEntry Entry { get; }
        public bool IsInventory => Entry != null;
        private bool Combinable => !string.IsNullOrEmpty(Entry?.AggregationKey);
        // An opaque entry can represent one complete stack (e.g. 1000 rice),
        // whereas an aggregatable entry counts individual items.
        public int Count => IsInventory
            ? (int)Math.Min(int.MaxValue, Combinable ? Entry.Quantity : preview.stackCount)
            : (int)Math.Min(int.MaxValue, physical.Things.Sum(t => (long)Math.Max(0, t.stackCount)));
        public long ReservationQuantity => Combinable ? Selected : Entry.Quantity;
        public string Label => IsInventory
            ? preview.LabelCapNoCount + "  [" + "Phinix_legacyRedpacket_inventorySource".Localize().ToString() + "]"
            : physical.Label + ((physical as RedPacketStackTemplate.PhysicalGroup)?.RestrictionKey is string key
                ? "  [" + key.Localize().ToString() + "]" : string.Empty);
        // List identity is independent of the sender's physical-stack restriction.
        public string DisplayLabel => IsInventory ? preview.LabelCapNoCount : physical.Label;
        public int DisplayQuality
        {
            get
            {
                Thing thing = IsInventory ? preview : physical.Things.FirstOrDefault();
                return thing != null && thing.TryGetQuality(out QualityCategory quality) ? (int)quality : -1;
            }
        }
        public ThingDef ThingDef => IsInventory ? preview.def : physical.ThingDef;
        public ThingDef StuffDef => IsInventory ? preview.Stuff : physical.StuffDef;
        public ThingStyleDef StyleDef => IsInventory ? preview.StyleDef : physical.StyleDef;
        public int Selected
        {
            get => IsInventory ? selected : physical.Selected;
            set
            {
                int count = Math.Max(0, Math.Min(value, Count));
                if (IsInventory) selected = count;
                else physical.Selected = count;
            }
        }

        public static RedPacketAvailableItem FromPhysical(StackedThings stack)
        {
            return new RedPacketAvailableItem(stack, null, null);
        }

        internal static RedPacketAvailableItem CombinePhysical(IReadOnlyList<RedPacketAvailableItem> sources)
        {
            if (sources == null || sources.Count == 0 || sources.Any(s => s.IsInventory))
                throw new InvalidOperationException("A map selection was expected.");
            var things = sources.SelectMany(s => s.PhysicalThings).ToArray();
            if (things.Length == 0 || things.Distinct().Count() != things.Length)
                throw new InvalidOperationException("The map item selection has changed.");
            return new RedPacketAvailableItem(new StackedThings(things), null, null) { CompositeSources = sources.ToArray() };
        }

        public static RedPacketAvailableItem FromInventory(InventoryEntry entry, Thing preview)
        {
            if (entry == null || preview == null || preview.def == null || preview.stackCount < 1 ||
                preview is MinifiedThing || preview.def.category != ThingCategory.Item || preview.def.IsCorpse ||
                (string.IsNullOrEmpty(entry.AggregationKey) && entry.Quantity != 1))
                throw new InvalidOperationException("This inventory entry cannot be sent in a red packet.");
            return new RedPacketAvailableItem(null, entry.Clone(), preview);
        }

        internal RedPacketStackTemplate.PhysicalSelection PreparePhysicalSelection(int count)
        {
            if (IsInventory) throw new InvalidOperationException("A physical selection was expected.");
            return RedPacketStackTemplate.PreparePhysical(physical, count);
        }

        public IEnumerable<PoppedThing> PopSelectedPhysical()
        {
            return IsInventory ? Enumerable.Empty<PoppedThing>() : physical.PopSelectedWithOrigins();
        }

        public void Dispose() { if (IsInventory) Discard(new[] { preview }); }

        internal static void Discard(IEnumerable<Thing> things)
        {
            foreach (Thing thing in things ?? Enumerable.Empty<Thing>())
                if (thing != null && !thing.Destroyed && !thing.Spawned && thing.ParentHolder == null)
                    thing.Destroy(DestroyMode.Vanish);
        }
    }
}
