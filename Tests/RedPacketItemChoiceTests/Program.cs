using System;
using System.Linq;
using System.Collections.Generic;
using Phinix.LegacyRedPacketExtension.Client;
namespace Verse { public class ThingDef {} public class ThingStyleDef {} }
namespace Phinix.LegacyRedPacketExtension.Client
{
    internal sealed class RedPacketAvailableItem
    {
        public Verse.ThingDef ThingDef, StuffDef;
        public Verse.ThingStyleDef StyleDef;
        public int DisplayQuality=-1,Count;
        public bool IsIndividual;
        public static RedPacketAvailableItem CombinePhysical(IReadOnlyList<RedPacketAvailableItem> items)=>new RedPacketAvailableItem{Count=items.Sum(i=>i.Count)};
        public bool IsInventory;
        public string DisplayLabel;
    }
}
internal static class Program
{
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static void Main()
    {
        var def=new Verse.ThingDef();
        var fuel=Enumerable.Range(0,120).Select(i=>new RedPacketAvailableItem{ThingDef=def,Count=750,DisplayLabel="化合燃料"}).ToList();
        var groups=RedPacketItemChoice.Build(fuel);
        Check(groups.Count==1,"120 physically restricted stacks occupy one display row");
        Check(groups[0].TotalCount==90000&&groups[0].SourceCount==120,"all stacks contribute their actual count");
        Check(groups[0].CreateSelection().Count==90000,"A grouped map item builds one total-quantity send selection");
        Check(groups[0].Single==null,"a combined row never masquerades as one sendable stack");
        Check(groups[0].Contains(fuel[119])&&groups[0].Representative==fuel[0],"source identity and first-seen order retained");
        Check(fuel.All(f=>f.Count==750),"grouping cannot consume or modify physical sources");
        fuel.Add(new RedPacketAvailableItem{ThingDef=def,Count=100,IsInventory=true,DisplayLabel="化合燃料"});
        groups=RedPacketItemChoice.Build(fuel);
        Check(groups.Count==1&&groups[0].TotalCount==90100&&groups[0].HasInventory&&groups[0].HasMap,"same variant across inventory and map occupies one row with both source flags");
        var otherDef=new RedPacketAvailableItem{ThingDef=new Verse.ThingDef(),Count=5,DisplayLabel="化合燃料"};fuel.Add(otherDef);
        Check(RedPacketItemChoice.Build(fuel).Count==2,"equal labels cannot merge different Def identities");
        var stuff=new Verse.ThingDef();
        fuel.Add(new RedPacketAvailableItem{ThingDef=def,StuffDef=stuff,Count=7});
        fuel.Add(new RedPacketAvailableItem{ThingDef=def,DisplayQuality=2,Count=3});
        fuel.Add(new RedPacketAvailableItem{ThingDef=def,StyleDef=new Verse.ThingStyleDef(),Count=4});
        groups=RedPacketItemChoice.Build(fuel);
        Check(groups.Count==5,"material, quality and style variants remain distinguishable");
        Check(groups[1].Single==otherDef,"a single original group keeps its existing send binding");
        fuel.Add(new RedPacketAvailableItem{ThingDef=def,Count=0});fuel.Add(new RedPacketAvailableItem{Count=5});
        Check(RedPacketItemChoice.Build(fuel).Count==5,"empty sources and missing definitions are excluded");
        var gear=new[]{new RedPacketAvailableItem{ThingDef=def,Count=1,IsIndividual=true},new RedPacketAvailableItem{ThingDef=def,Count=1,IsIndividual=true}};
        Check(RedPacketItemChoice.Build(gear).Count==2,"Weapons and armor stay individual even with identical definitions and quality");
        var huge=RedPacketItemChoice.Build(Enumerable.Range(0,3).Select(_=>new RedPacketAvailableItem{ThingDef=def,Count=int.MaxValue}))[0];
        Check(huge.TotalCount==3L*int.MaxValue&&huge.DraftLimit==int.MaxValue,"total count is 64-bit while the unchanged quantity contract stays bounded");
        Check(RedPacketItemChoice.Build(Array.Empty<RedPacketAvailableItem>()).Count==0,"empty inventory has no placeholder duplicate");
        Console.WriteLine("PASS "+checks+" item-choice grouping checks (source-linked display model; no game execution)");
    }
}
