#if SQLITE_TEST
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using PhinixClient.Storage;
using Phinix.InventoryExtension;
using Phinix.InventoryExtension.Client;
using Phinix.LegacyRedPacketExtension.Client;
using PhinixClient.Trade;
using Utils.Framework;

internal static partial class Program
{
    private static void SqliteDepositTest()
    {
        string directory=Path.Combine(Path.GetTempPath(),"redpacket-sqlite-"+Guid.NewGuid().ToString("N"));
        try
        {
            using(var service=new ClientDatabaseService(owner=>Path.Combine(directory,owner)))
            {
                var codec=new RedPacketInventoryCodec();var codecs=new Dictionary<string,IInventoryCodec>{{codec.CodecId,codec}};
                var template=new TradeItemSnapshot("WoodLog",999999,80,(TradeItemQuality)0,null,null,StatefulTradeItemProtocol.ScribeCodecId,new byte[]{1,2,3});
                var deposit=new InventoryDeposit{DepositId="synthetic-redpacket-claim",Source="legacy-redpacket",Items=RedPacketInventoryCodec.BuildItems(template,999999,"Wood",75)};
                using(var store=new InventoryDatabaseStore(service))
                {
                    var ledger=new InventoryLedger();ledger.Restore(store.Load("game-a"));
                    var result=ledger.PrepareDeposit(deposit,codecs,out var next);
                    if(result.Status!=InventoryDepositStatus.Committed)throw new Exception("Real Inventory rejected reward: " + result.Reason);
                    Check(result.Status==InventoryDepositStatus.Committed&&store.TryAppend(next));ledger.Publish(next);
                    Check(ledger.Snapshot().Count==2&&ActualUnits(ledger)==999999);
                    Check(ledger.PrepareDeposit(deposit,codecs,out _).Status==InventoryDepositStatus.AlreadyCommitted);
                    deposit.Items[0].Quantity++;Check(ledger.PrepareDeposit(deposit,codecs,out _).Status==InventoryDepositStatus.Conflict);deposit.Items[0].Quantity--;
                }
                using(var store=new InventoryDatabaseStore(service))
                {
                    var ledger=new InventoryLedger();ledger.Restore(store.Load("game-b"));
                    Check(ActualUnits(ledger)==999999&&ledger.PrepareDeposit(deposit,codecs,out _).Status==InventoryDepositStatus.AlreadyCommitted);
                    var entry=ledger.Snapshot().Single(e=>e.Quantity>1);
                    var preview=(Verse.Thing)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Verse.Thing));
                    preview.def=Verse.DefDatabase<Verse.ThingDef>.GetNamedSilentFail("WoodLog");preview.def.category=Verse.ThingCategory.Item;preview.def.thingClass=typeof(Verse.Thing);preview.def.label="Wood";preview.stackCount=75;
                    var selection=RedPacketAvailableItem.FromInventory(entry,preview);
                    Check(selection.Count==999975);selection.Selected=76;
                    Check(selection.ReservationQuantity==2&&selection.MaximumReservationUnits==1000);
                    var reserved=ledger.PrepareReservation(new InventoryReservationRequest{OperationId="partial-packet",OwnerExtensionId="builtin.legacy-redpacket",Purpose="redpacket.send",Lines=new[]{new InventoryReservationLine{EntryId=entry.EntryId,Quantity=selection.ReservationQuantity}}},out var next);
                    Check(reserved.Succeeded&&store.TryAppend(next));ledger.Publish(next);
                    var committed=ledger.PrepareReservationResolution(reserved.Reservation.ReservationId,InventoryReservationResolution.Commit,"synthetic relay acceptance",out next);
                    Check(committed.Succeeded&&store.TryAppend(next));ledger.Publish(next);
                    var returned=new InventoryDeposit{DepositId="redpacket-send-return:unselected:partial-packet",Source="legacy-redpacket",Items=RedPacketInventoryCodec.BuildItems(template,74,"Wood",75,false)};
                    Check(ledger.PrepareDeposit(returned,codecs,out next).Succeeded&&store.TryAppend(next));ledger.Publish(next);
                    Check(ActualUnits(ledger)==999999-76&&ledger.PrepareDeposit(returned,codecs,out _).Status==InventoryDepositStatus.AlreadyCommitted);
                    var singular=ledger.Snapshot().Single(e=>e.AggregationKey==null&&FrameworkSerialization.DeserializePayload<RedPacketInventoryPayload>(System.Text.Encoding.UTF8.GetString(e.Payload)).DeliveryStackCount==74);
                    preview.stackCount=74;var oldSelection=RedPacketAvailableItem.FromInventory(singular,preview);oldSelection.Selected=1;
                    Check(oldSelection.Count==74&&oldSelection.ReservationQuantity==1);
                    var plain=entry.Clone();plain.CodecId="foreign.codec";plain.Quantity=100;preview.stackCount=75;
                    var plainSelection=RedPacketAvailableItem.FromInventory(plain,preview);plainSelection.Selected=76;
                    Check(plainSelection.Count==100&&plainSelection.ReservationQuantity==76);
                }
                foreach(var state in new[]{InventoryReservationState.Reserved,InventoryReservationState.Uncertain,InventoryReservationState.Committed})
                    Check(RedPacketStateMachine.ShouldTrackInventoryReservation(new InventoryReservation{OwnerExtensionId="builtin.legacy-redpacket",State=state}));
                foreach(var state in new[]{InventoryReservationState.Restored,InventoryReservationState.Abandoned})
                    Check(!RedPacketStateMachine.ShouldTrackInventoryReservation(new InventoryReservation{OwnerExtensionId="builtin.legacy-redpacket",State=state}));
                Check(!RedPacketStateMachine.ShouldTrackInventoryReservation(new InventoryReservation{OwnerExtensionId="other",State=InventoryReservationState.Reserved}));
            }
        }
        finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    private static long ActualUnits(InventoryLedger ledger)=>ledger.Snapshot().Sum(entry=>entry.Quantity*FrameworkSerialization.DeserializePayload<RedPacketInventoryPayload>(System.Text.Encoding.UTF8.GetString(entry.Payload)).DeliveryStackCount);
}
#endif
