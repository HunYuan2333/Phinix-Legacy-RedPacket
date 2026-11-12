using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using Phinix.InventoryExtension;
using Phinix.LegacyRedPacketExtension.Client;
using Utils;
using Verse;

internal sealed class TrackedPreview : Thing
{
    internal int Calls;
    internal bool Throw;
    public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
    { Calls++; if (Throw) throw new InvalidOperationException("synthetic preview release failure"); }
}
internal static class Program
{
    private const BindingFlags Flags = BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
    private static Game Game()
    {
        var game=(Game)FormatterServices.GetUninitializedObject(typeof(Game));
        typeof(Game).GetField("maps",Flags).SetValue(game,new List<Map>{(Map)FormatterServices.GetUninitializedObject(typeof(Map))});
        return game;
    }
    private static ThingDef Def()
    {
        var def=(ThingDef)FormatterServices.GetUninitializedObject(typeof(ThingDef));
        def.defName="PreviewFixture";def.label="Preview fixture";def.category=ThingCategory.Item;def.thingClass=typeof(Thing);def.stackLimit=75;def.destroyable=true;return def;
    }
    private static T Preview<T>(int state=-1) where T:Thing,new()
    {
        var thing=new T{def=Def(),stackCount=1};
        var map=typeof(Thing).GetField("mapIndexOrState",Flags);map.SetValue(thing,Convert.ChangeType(state,map.FieldType));return thing;
    }
    private static RedPacketAvailableItem Item(Thing thing)=>RedPacketAvailableItem.FromInventory(new InventoryEntry{EntryId=Guid.NewGuid().ToString("N"),CodecId="foreign.fixture",CodecVersion=1,Quantity=1,Payload=new byte[]{1},Label="Preview",Source="fixture"},thing);
    private static void Set(object obj,string field,object value)=>obj.GetType().GetField(field,Flags).SetValue(obj,value);
    private static void BlockNetwork()
    {
        typeof(RedPacketRelay).GetField("nextPollUtc",Flags).SetValue(null,DateTime.MaxValue);
        typeof(RedPacketRelay).GetField("nextSendUtc",Flags).SetValue(null,DateTime.MaxValue);
    }
    public static int Main(string[] args)
    {
        var old=Current.Game;
        try
        {
            bool expectBug=args.Length>0&&args[0]=="--expect-bug";
            var logs=new List<string>();var relay=new RedPacketRelay((m,l)=>logs.Add(m),(a,b,c)=>{throw new Exception("Test must never send");});
            var machine=new RedPacketStateMachine(null,null,null,null,new RedPacketSettings(),relay,(m,l)=>logs.Add(m));
            var tab=new RedPacketTab(null,null,null,new RedPacketSettings(),machine,(m,l)=>logs.Add(m));
            Current.Game=Game();Set(machine,"inventoryGame",Current.Game);
            var raw=Preview<Thing>();Console.WriteLine("Fixture destroyed="+raw.Destroyed+" spawned="+raw.Spawned+" holder="+(raw.ParentHolder!=null));
            var first=Item(raw);var second=Item(Preview<Thing>());
            tab.PresentationAllItems.Add(first);tab.PresentationAllItems.Add(second);
            Set(tab,"filteredItems",new List<RedPacketAvailableItem>{first,second});Set(tab,"selectedStack",first);
            machine.Cleared+=tab.ClearItems;machine.Cleared+=BlockNetwork;
            Current.Game=null;BlockNetwork();
            try {typeof(RedPacketStateMachine).GetMethod("Tick",Flags).Invoke(machine,null);}
            catch(TargetInvocationException error)
            {
                if(expectBug&&error.InnerException is NullReferenceException){Console.WriteLine("REPRODUCED original main-menu preview cleanup NullReferenceException: "+error.InnerException.StackTrace);return 0;}
                throw;
            }
            if(expectBug)throw new Exception("Old binary did not reproduce the expected failure");
            Check(tab.PresentationAllItems.Count==0&&tab.PresentationItems.Count==0,"main-menu Tick clears both preview lists");
            Check(tab.GetType().GetField("selectedStack",Flags).GetValue(tab)==null,"old selection is detached");
            for(int i=0;i<5;i++)typeof(RedPacketStateMachine).GetMethod("Tick",Flags).Invoke(machine,null);
            Check(logs.Count==0,"repeated main-menu ticks do not report callback failures");
            Check(typeof(RedPacketAvailableItem).GetField("preview",Flags).GetValue(first)==null,"obsolete preview reference is released");
            Current.Game=Game();var tracked=Preview<TrackedPreview>();var item=Item(tracked);item.Dispose();item.Dispose();
            Check(tracked.Calls==1,"same-game preview is destroyed exactly once");
            tracked=Preview<TrackedPreview>();item=Item(tracked);Current.Game=Game();item.Dispose();Check(tracked.Calls==0,"switching games does not destroy an old-world preview in the new world");
            tracked=Preview<TrackedPreview>(0);item=Item(tracked);item.Dispose();Check(tracked.Calls==0,"spawned world objects are not destroyed by preview cleanup");
            tracked=Preview<TrackedPreview>(-2);item=Item(tracked);item.Dispose();Check(tracked.Calls==0,"already destroyed previews remain untouched");
            tab.ClearItems();tab.ClearItems();Check(tab.PresentationAllItems.Count==0,"empty cleanup is idempotent");
            var bad=Preview<TrackedPreview>();bad.Throw=true;var good=Preview<TrackedPreview>();
            var badItem=Item(bad);tab.PresentationAllItems.Add(badItem);tab.PresentationAllItems.Add(Item(good));Set(tab,"pendingItems",new List<RedPacketAvailableItem>{Item(Preview<TrackedPreview>())});
            tab.ClearItems();tab.ClearItems();
            Check(bad.Calls==1&&good.Calls==1&&tab.PresentationAllItems.Count==0,"one faulty preview cannot retain the list or block other releases");
            Check(logs.Count==1&&logs[0].Contains("synthetic preview release failure")&&logs[0].Contains("Destroy"),"release failure has one full diagnostic without repeated cleanup");
            Check(tab.GetType().GetField("pendingItems",Flags).GetValue(tab)==null,"pending grouping previews are detached");
            Console.WriteLine("PREVIEW LIFETIME PASS "+checks+"; actual plugin/game DLLs, synthetic objects, no network or inventory mutation");return 0;
        }
        finally{Current.Game=old;}
    }
}
