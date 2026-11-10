using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Runtime.Serialization;
using System.Text;
using Phinix.InventoryExtension;
using PhinixClient.Trade;
using RimWorld;
using Utils.Framework;
using Verse;

namespace Phinix.LegacyRedPacketExtension.Client
{
    [DataContract]
    internal sealed class RedPacketInventoryPayload
    {
        [DataMember]
        public string DefName { get; set; }
        [DataMember]
        public string StuffDefName { get; set; }
        [DataMember]
        public int HitPoints { get; set; }
        [DataMember]
        public int Quality { get; set; }
        [DataMember]
        public int StackCount { get; set; }
        [DataMember]
        public int DeliveryStackCount { get; set; }
        [DataMember]
        public string StateCodecId { get; set; }
        [DataMember]
        public byte[] StatePayload { get; set; }
        [DataMember]
        public RedPacketInventoryPayload InnerItem { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public bool RepeatedStacks { get; set; }

        public static RedPacketInventoryPayload FromSnapshot(TradeItemSnapshot snapshot, int deliveryStackCount)
        {
            if (snapshot == null) return null;
            return new RedPacketInventoryPayload
            {
                DefName = snapshot.DefName,
                StuffDefName = snapshot.StuffDefName,
                HitPoints = snapshot.HitPoints,
                Quality = (int)snapshot.Quality,
                StackCount = snapshot.StackCount,
                DeliveryStackCount = deliveryStackCount,
                StateCodecId = snapshot.StateCodecId,
                StatePayload = snapshot.StatePayload,
                InnerItem = snapshot.InnerItem == null ? null : FromSnapshot(snapshot.InnerItem, snapshot.InnerItem.StackCount)
            };
        }

        public TradeItemSnapshot ToSnapshot(int count)
        {
            int restoredCount = StatePayload != null && StatePayload.Length > 0 ? StackCount : count;
            return new TradeItemSnapshot(DefName, restoredCount, HitPoints, (TradeItemQuality)Quality,
                StuffDefName, InnerItem?.ToSnapshot(1), StateCodecId, StatePayload);
        }
    }

    internal partial class RedPacketInventoryCodec : IInventoryCodec, IInventoryItemPresentationCodec
    {
        public string CodecId => "legacy-redpacket.template";
        public int Version => 1;

        public InventoryItemPresentation PresentItem(InventoryEntry entry)
        {
            RedPacketInventoryPayload payload = Decode(entry.Payload);
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(payload.DefName);
            if (def == null || payload.InnerItem != null) return null;
            string label = def.LabelCap.ToString();
            ThingDef stuff = string.IsNullOrEmpty(payload.StuffDefName) ? null :
                DefDatabase<ThingDef>.GetNamedSilentFail(payload.StuffDefName);
            if (stuff != null) label += " · " + stuff.LabelCap;
            if (payload.Quality > 0 && payload.Quality <= 7)
                label += " (" + ((QualityCategory)(payload.Quality - 1)).GetLabel() + ")";
            string stuffName = payload.StuffDefName ?? string.Empty;
            return new InventoryItemPresentation
            {
                Label = label,
                IconDefName = payload.DefName,
                GroupKey = payload.DefName.Length + ":" + payload.DefName + stuffName.Length + ":" + stuffName + ":" + payload.Quality,
                ItemsPerUnit = payload.StatePayload != null && payload.StatePayload.Length > 0 ? payload.DeliveryStackCount : 1
            };
        }

        public bool CanStore(InventoryItem item)
        { return TryValidate(item, out _); }

        internal bool TryValidate(InventoryItem item, out string reason)
        {
            reason = null;
            if (item == null || item.CodecId != CodecId || item.CodecVersion != Version ||
                item.Quantity < 1 || item.Payload == null)
            { reason = "Invalid red packet codec, version or item quantity."; return false; }
            try
            {
                RedPacketInventoryPayload payload = Decode(item.Payload);
                if (payload.InnerItem != null) { reason = "Minified items cannot be stored as red packet rewards."; return false; }
                if (DefDatabase<ThingDef>.GetNamedSilentFail(payload.DefName) == null)
                { reason = "Item definition is unavailable: " + payload.DefName; return false; }
                if (!string.IsNullOrEmpty(payload.StuffDefName) && DefDatabase<ThingDef>.GetNamedSilentFail(payload.StuffDefName) == null)
                { reason = "Material definition is unavailable: " + payload.StuffDefName; return false; }
                if (!CanRestore(payload, item.Quantity))
                { reason = "Red packet state or stack bounds are invalid: item=" + payload.DefName + ", units=" + item.Quantity + ", deliveryStack=" + payload.DeliveryStackCount + ", sourceStack=" + payload.StackCount + ", stateCodec=" + payload.StateCodecId; return false; }
                return true;
            }
            catch (Exception error) { reason = "Red packet payload could not be decoded: " + error.GetType().Name; return false; }
        }

        private static bool CanRestore(RedPacketInventoryPayload payload, long quantity)
        {
            if (payload == null || quantity < 1) return false;
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(payload.DefName);
            if (def == null || def.defName == "UnknownItem" ||
                (!string.IsNullOrEmpty(payload.StuffDefName) &&
                 DefDatabase<ThingDef>.GetNamedSilentFail(payload.StuffDefName) == null)) return false;
            if (payload.StatePayload != null && payload.StatePayload.Length > 0 &&
                ((quantity != 1 && !payload.RepeatedStacks) || (payload.RepeatedStacks && quantity > int.MaxValue / Math.Max(1, payload.DeliveryStackCount)) || payload.StackCount < 1 || payload.DeliveryStackCount < 1 ||
                 payload.DeliveryStackCount > payload.StackCount || payload.DeliveryStackCount > Math.Max(1, def.stackLimit) || !string.Equals(payload.StateCodecId,
                    StatefulTradeItemProtocol.ScribeCodecId, StringComparison.OrdinalIgnoreCase))) return false;
            return true;
        }

        public string GetAggregationKey(InventoryItem item)
        {
            RedPacketInventoryPayload payload = Decode(item.Payload);
            // RepeatedStacks represents identical complete legal stacks. Each unit
            // still materializes one whole stack; legacy singular state stays indivisible.
            if (payload.StatePayload != null && payload.StatePayload.Length > 0 && !payload.RepeatedStacks) return null;
            using (SHA256 sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(item.Payload));
        }

        public int MaximumStackCount(InventoryEntry entry)
        {
            RedPacketInventoryPayload payload = Decode(entry.Payload);
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(payload.DefName);
            return payload.StatePayload != null && payload.StatePayload.Length > 0 ? 1 : Math.Max(1, def?.stackLimit ?? 1);
        }

        public Thing Materialize(InventoryEntry entry, int count)
        {
            RedPacketInventoryPayload payload = Decode(entry.Payload);
            if (!CanRestore(payload, entry.Quantity)) throw new InvalidOperationException("Red packet state or current stack limit cannot be restored safely.");
            if (count < 1 || count > MaximumStackCount(entry)) throw new ArgumentOutOfRangeException(nameof(count));
            Thing thing = payload.StatePayload != null && payload.StatePayload.Length > 0
                ? TradeItemConverter.ConvertStatefulStackFromSnapshot(payload.ToSnapshot(payload.StackCount), payload.DeliveryStackCount)
                : TradeItemConverter.ConvertThingFromSnapshot(payload.ToSnapshot(count));
            int expectedCount = payload.StatePayload != null && payload.StatePayload.Length > 0 ? payload.DeliveryStackCount : count;
            if (thing == null || thing.def == null || thing.def.defName == "UnknownItem" || thing.stackCount != expectedCount)
                throw new InvalidOperationException("Red packet payload could not be restored without loss.");
            return thing;
        }

        public virtual InventoryDeliveryResult DeliverVerified(Thing thing, Map map, IntVec3 dropSpot)
        {
            return InventoryDropDelivery.DeliverThings(new[] { thing }, map, dropSpot);
        }

        internal static byte[] Encode(TradeItemSnapshot snapshot, int deliveryStackCount, bool repeatedStacks = false)
        {
            var payload = RedPacketInventoryPayload.FromSnapshot(snapshot, deliveryStackCount);
            payload.RepeatedStacks = repeatedStacks;
            return Encoding.UTF8.GetBytes(FrameworkSerialization.SerializePayload(payload));
        }

        internal static IList<InventoryItem> BuildItems(TradeItemSnapshot template, int amount, string label, int stackLimit, bool compact = true)
        {
            if (template == null || amount < 1 || stackLimit < 1)
                throw new InvalidOperationException("Invalid red packet inventory quantity or template.");
            bool stateful = template.StatePayload != null && template.StatePayload.Length > 0;
            List<InventoryItem> items = new List<InventoryItem>();
            int remaining = amount;
            while (remaining > 0)
            {
                int count = stateful ? Math.Min(remaining, Math.Max(1, stackLimit)) : remaining;
                int repetitions = stateful && compact ? remaining / count : 1;
                bool repeated = repetitions > 1;
                items.Add(new InventoryItem
                {
                    CodecId = "legacy-redpacket.template", CodecVersion = 1,
                    Payload = Encode(template, count, repeated), Quantity = stateful ? repetitions : count,
                    Label = count > 1 ? label + " × " + count : label
                });
                remaining -= checked(count * repetitions);
                if (items.Count > 1000) throw new InvalidOperationException("Red packet inventory delivery exceeds its stack limit.");
            }
            return items;
        }

        private static RedPacketInventoryPayload Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) throw new InvalidOperationException("Empty red packet payload.");
            RedPacketInventoryPayload payload = FrameworkSerialization.DeserializePayload<RedPacketInventoryPayload>(Encoding.UTF8.GetString(bytes));
            if (payload == null || string.IsNullOrEmpty(payload.DefName)) throw new InvalidOperationException("Invalid red packet payload.");
            return payload;
        }
    }

    internal sealed class RedPacketInventorySourcePresenter : IInventorySourcePresenter
    {
        public string SourceId => "legacy-redpacket";

        public InventorySourcePresentation Present(InventoryEntry entry)
        {
            string name = string.IsNullOrWhiteSpace(entry?.OriginDisplayName)
                ? entry?.OriginUserId : entry.OriginDisplayName;
            if (string.IsNullOrWhiteSpace(name)) name = "Phinix_inventory_originUnknown".Localize().ToString();
            InventorySourcePresentation presentation = new InventorySourcePresentation
            {
                Summary = entry?.OriginKind == "redpacket-return"
                    ? "Phinix_legacyRedpacket_inventoryReturnSource".Localize().ToString()
                    : "Phinix_inventory_originRedPacket".Localize(name).ToString()
            };
            if (!string.IsNullOrWhiteSpace(entry?.OriginId))
                presentation.Details.Add("Phinix_inventory_originEventId".Localize(entry.OriginId).ToString());
            if (!string.IsNullOrWhiteSpace(entry?.OriginUserId))
                presentation.Details.Add("Phinix_inventory_originUserId".Localize(entry.OriginUserId).ToString());
            return presentation;
        }
    }

}
