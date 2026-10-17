// Algorithm test seams only. These do not replace RimWorld acceptance or compile references.
using System;using System.Collections.Generic;using System.Linq;
namespace Utils { public enum LogLevel { INFO, WARNING, ERROR } }
namespace Verse {
 public enum ThingCategory { Item,Building,Pawn }
 public class ThingDef { public ThingCategory category=ThingCategory.Item; public bool IsWeapon,IsApparel,IsCorpse,tradeNeverStack; public string defName="Steel";public int stackLimit=75; }
 public class Thing { public bool Destroyed; public ThingDef Stuff;public int HitPoints=100; public int stackCount=75;public ThingDef def=new ThingDef();public string State="<beenRevealed>true</beenRevealed><tickDelta>0</tickDelta><health>100</health>";public bool StackAllowed=true;public virtual bool CanStackWith(Thing other)=>StackAllowed&&other.def.defName==def.defName; }
 public class Medicine:ThingWithComps {}
 public class FoodState:ThingComp { public float PoisonPct; }
 public class VerbTracker { private List<object> verbs; public void Initialize(){verbs=new List<object>();} }
 public class CompEquippable:ThingComp { public VerbTracker VerbTracker {get;}=new VerbTracker(); }
 public class ThingComp {public virtual void PostExposeData(){} public virtual void PreAbsorbStack(Thing t,int n){} public virtual void PostSplitOff(Thing t){} public virtual bool AllowStackWith(Thing t)=>true;}
 public class ThingWithComps:Thing { public List<object> AllComps=new List<object>(); public override bool CanStackWith(Thing other){if(!base.CanStackWith(other))return false;foreach(var q in AllComps.OfType<RimWorld.CompQuality>())if(!((other as ThingWithComps)?.AllComps.OfType<RimWorld.CompQuality>().Any(o=>o.Quality==q.Quality)??false))return false;return true;} }
}
namespace RimWorld { public class CompForbiddable{} public class CompQuality{public int Quality;} public class CompColorable{} public class CompRottable{} public class CompIngredients{} }
namespace PhinixClient.Trade {
 public enum TradeItemQuality { None }
 public class TradeItemSnapshot {
  public string DefName,StuffDefName,StateCodecId;public int StackCount,HitPoints;public TradeItemQuality Quality;public TradeItemSnapshot InnerItem;public byte[] StatePayload;
  public TradeItemSnapshot(string def,int count,int hp,TradeItemQuality quality,string stuff,TradeItemSnapshot inner,string codec,byte[] payload){DefName=def;StackCount=count;HitPoints=hp;Quality=quality;StuffDefName=stuff;InnerItem=inner;StateCodecId=codec;StatePayload=payload;}
 }
 public static class StatefulTradeItemProtocol {public const string ScribeCodecId="core.item.scribe-v1";public const int MaxStatePayloadBytes=256*1024;}
}
namespace Phinix.TradeExtension.Client {
 public class StackedThings { public List<Verse.Thing> Things;public int Selected;public StackedThings(IEnumerable<Verse.Thing> things){Things=things.ToList();} }
 public static class TradeItemConverter { public static PhinixClient.Trade.TradeItemSnapshot ConvertThingFromVerse(Verse.Thing thing)=>throw new NotSupportedException("Inject a serializer in console tests."); }
}
namespace Phinix.LegacyRedPacketExtension.Client {public static class TestLocalization { public static string Localize(this string text)=>text;} }
