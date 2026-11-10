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
                }
            }
        }
        finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    private static long ActualUnits(InventoryLedger ledger)=>ledger.Snapshot().Sum(entry=>entry.Quantity*FrameworkSerialization.DeserializePayload<RedPacketInventoryPayload>(System.Text.Encoding.UTF8.GetString(entry.Payload)).DeliveryStackCount);
}
#endif
