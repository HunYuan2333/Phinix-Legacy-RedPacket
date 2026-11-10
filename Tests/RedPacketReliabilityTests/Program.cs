using System;
using System.IO;
using System.Linq;
using System.Text;
using Phinix.LegacyRedPacketExtension.Client;
using PhinixClient.Trade;
using Utils.Framework;

internal static partial class Program
{
    private static int checks;
    private static void Check(bool value) { if (!value) throw new Exception("Assertion failed at check " + (checks + 1)); checks++; }
    private static void Main()
    {
        var template = new TradeItemSnapshot("WoodLog", 999999, 80, (TradeItemQuality)0,
            null, null, StatefulTradeItemProtocol.ScribeCodecId, new byte[] { 1, 2, 3 });
        var def = (Verse.ThingDef)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Verse.ThingDef));
        def.defName = "WoodLog"; def.stackLimit = 75;
        Verse.DefDatabase<Verse.ThingDef>.Add(def);
        var codec = new RedPacketInventoryCodec();
        foreach (int limit in new[] { 1, 75, 137, 999999 })
        foreach (int amount in new[] { 1, 75000, 75075, 999999 })
        {
            var items = RedPacketInventoryCodec.BuildItems(template, amount, "Wood", limit);
            Check(items.Count <= 2);
            long total = 0;
            foreach (var item in items)
            {
                var payload = FrameworkSerialization.DeserializePayload<RedPacketInventoryPayload>(Encoding.UTF8.GetString(item.Payload));
                Check(payload.StatePayload.SequenceEqual(template.StatePayload));
                Check(payload.StackCount == template.StackCount);
                Check(payload.DeliveryStackCount <= limit);
                total += item.Quantity * payload.DeliveryStackCount;
            }
            Check(total == amount);
            if (limit <= def.stackLimit)
                foreach (var item in items) Check(codec.CanStore(item));
        }
        var compact = RedPacketInventoryCodec.BuildItems(template, 999999, "Wood", 75);
        Check(compact.Count == 2 && compact.All(item => codec.TryValidate(item, out _)));
        var missing = new TradeItemSnapshot("MissingModItem", 1, 80, (TradeItemQuality)0, null, null, null, null);
        Check(!codec.TryValidate(RedPacketInventoryCodec.BuildItems(missing, 1, "missing", 75)[0], out var missingReason) && missingReason.Contains("MissingModItem"));
        var invalid = RedPacketInventoryCodec.BuildItems(template, 75, "Wood", 75)[0]; invalid.Payload = new byte[] { 0 };
        Check(!codec.TryValidate(invalid, out var invalidReason) && invalidReason.Contains("decoded"));
        var oversized = RedPacketInventoryCodec.BuildItems(template, 137, "Wood", 137)[0];
        Check(!codec.TryValidate(oversized, out var boundsReason) && boundsReason.Contains("stack bounds"));
        Check(!Encoding.UTF8.GetString(RedPacketInventoryCodec.Encode(template, 75)).Contains("RepeatedStacks"));
        Check(RedPacketInventoryCodec.BuildItems(template, 75000, "Wood", 75, false).Count == 1000);
        bool exceeded = false;
        try { RedPacketInventoryCodec.BuildItems(template, 75075, "Wood", 75, false); }
        catch (InvalidOperationException) { exceeded = true; }
        Check(exceeded);
        string directory = Path.Combine(Path.GetTempPath(), "redpacket-retry-" + Guid.NewGuid().ToString("N"));
        try
        {
            Func<string,string> path = name => Path.Combine(directory, name);
            var now = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);
            var retry = new RedPacketReceiveRetry(path);
            for (int i = 1; i <= 5; i++)
            {
                Check(retry.Begin("claim:packet:user", "user", now, true));
                retry.Fail("dependency unavailable", false, false);
                Check(retry.Current.Attempts == i);
                Check(!retry.Begin("claim:packet:user", "user", now, true));
                now = retry.Current.NextUtc;
                retry = new RedPacketReceiveRetry(path);
            }
            Check(!retry.Begin("claim:packet:user", "user", now, true));
            Check(retry.Current.Paused && retry.Current.Attempts == 5);
            retry.Retry();
            Check(retry.Begin("claim:packet:user", "user", now, true));
            retry.Fail("fixed capacity", true, false);
            Check(!retry.Begin("claim:packet:user", "user", now.AddDays(1), true));
            retry.Retry();
            Check(retry.Begin("claim:packet:user", "user", now.AddDays(1)));
            // A process crash after reserving a direct delivery must fail closed.
            retry = new RedPacketReceiveRetry(path);
            Check(!retry.Begin("claim:packet:user", "user", now.AddDays(2)));
            Check(retry.Current.Uncertain);
            retry.Retry();
            Check(retry.Current.Paused);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
#if SQLITE_TEST
        SqliteDepositTest();
#endif
        Console.WriteLine("PASS " + checks + ": compact counts/state, codec validation, historical encoding/boundary, persistent backoff, pause, explicit retry, uncertain crash.");
    }
}
