using System;using System.IO;using System.IO.Compression;using System.Text;using System.Linq;using System.Collections.Generic;
using Verse;using PhinixClient.Trade;using Phinix.TradeExtension.Client;using Phinix.LegacyRedPacketExtension.Client;
class Program {
 static int passed;
 static void Check(bool ok){if(!ok)throw new Exception("Assertion failed");}
 static void Case(string name,Action test){test();passed++;Console.WriteLine("PASS "+name);}
 static void Throws(Action a){try{a();}catch(InvalidOperationException){return;}throw new Exception("Expected rejection");}
 static byte[] Zip(string xml){using(var o=new MemoryStream()){using(var z=new GZipStream(o,CompressionMode.Compress,true)){var b=Encoding.UTF8.GetBytes(xml);z.Write(b,0,b.Length);}return o.ToArray();}}
 static string Xml(TradeItemSnapshot s){using(var i=new MemoryStream(s.StatePayload))using(var z=new GZipStream(i,CompressionMode.Decompress))using(var r=new StreamReader(z))return r.ReadToEnd();}
 static TradeItemSnapshot Serialize(Thing t)=>new TradeItemSnapshot(t.def.defName,t.stackCount,100,TradeItemQuality.None,null,null,StatefulTradeItemProtocol.ScribeCodecId,Zip("<saveable><def>Steel</def><id>source</id><map>2</map><pos>(1,2,3)</pos><stackCount>"+t.stackCount+"</stackCount>"+t.State+"</saveable>"));
 static Thing Thing(int count=75,string state=null){var t=new Thing{stackCount=count};if(state!=null)t.State=state;return t;}
 static RedPacketSummaryQueue.Notice Notice(string key="packet:completed",string owner="sender")=>new RedPacketSummaryQueue.Notice{Key=key,Owner=owner,Title="Summary",Text="Done"};
 static void Main(){
  Case("200 steel preflight preserves intact state and all original counts",()=>{
   var things=new[]{Thing(),Thing(),Thing()};var plan=RedPacketStackTemplate.PreparePhysical(new StackedThings(things),200,Serialize);
   Check(plan.Sources.Things.Count==3&&plan.Template.StackCount==200);Check(things.All(t=>t.stackCount==75));
   var xml=Xml(plan.Template);Check(xml.Contains("<map>-1</map>")&&xml.Contains("<beenRevealed>true</beenRevealed>")&&xml.Contains("<stackCount>200</stackCount>"));
   // Same failure shape as native SplitOff: the partial entity has its own reveal history.
   var partial=Thing(50,things[2].State.Replace("true","false"));Throws(()=>RedPacketStackTemplate.Capture(new[]{things[0],things[1],partial},200,Serialize));
   Check(Xml(plan.Template).Contains("<beenRevealed>true</beenRevealed>"));
  });
  Case("prefer one sufficient stack",()=>{var a=Thing(50);var b=Thing(250);var p=RedPacketStackTemplate.PreparePhysical(new StackedThings(new[]{a,b}),200,Serialize);Check(p.Sources.Things.Count==1&&ReferenceEquals(p.Sources.Things[0],b)&&b.stackCount==250);});
  Case("source order conserves quantity",()=>{foreach(var counts in new[]{new[]{50,75,100},new[]{100,50,75}}){var ts=counts.Select(c=>Thing(c)).ToArray();Check(RedPacketStackTemplate.PreparePhysical(new StackedThings(ts),200,Serialize).Template.StackCount==200);Check(ts.Select(t=>t.stackCount).SequenceEqual(counts));}});
  Case("real transferable state mismatch rejects before mutation",()=>{var a=Thing();var b=Thing(75,a.State.Replace("100","80"));Throws(()=>RedPacketStackTemplate.PreparePhysical(new StackedThings(new[]{a,b}),100,Serialize));Check(a.stackCount==75&&b.stackCount==75);});
  Case("tick state remains authoritative",()=>{var a=Thing();var b=Thing(75,a.State.Replace("<tickDelta>0","<tickDelta>5"));Throws(()=>RedPacketStackTemplate.Capture(new[]{a,b},150,Serialize));});
  Case("reveal and quest state are not globally ignored",()=>{var a=Thing();var b=Thing(75,a.State+"<questTags><li>quest</li></questTags>");Throws(()=>RedPacketStackTemplate.Capture(new[]{a,b},150,Serialize));});
  Case("equivalent inventory materialization still aggregates",()=>{var ts=new[]{Thing(),Thing()};Check(RedPacketStackTemplate.Capture(ts,150,Serialize).StackCount==150);Check(ts.All(t=>t.stackCount==75));});
  Case("serializer failure restores temporary quantity",()=>{var t=Thing();Throws(()=>RedPacketStackTemplate.CaptureCount(t,200,_=>throw new InvalidOperationException("serializer")));Check(t.stackCount==75);});
  Case("unknown components stay single-source only",()=>{var a=new ThingWithComps();a.AllComps.Add(new object());var b=new ThingWithComps();b.AllComps.Add(new object());Check(RedPacketStackTemplate.Capture(new[]{a},75,Serialize).StackCount==75);Throws(()=>RedPacketStackTemplate.Capture(new Thing[]{a,b},150,Serialize));Check(RedPacketStackTemplate.GroupPhysical(new[]{new StackedThings(new Thing[]{a,b})},null,Serialize).Count()==2);});
  Case("UI grouping follows complete state",()=>{var a=Thing();var b=Thing();var c=Thing(75,a.State.Replace("100","80"));var groups=RedPacketStackTemplate.GroupPhysical(new[]{new StackedThings(new[]{a,b,c})},null,Serialize).ToArray();Check(groups.Length==2&&groups.Any(g=>g.Things.Count==2));});
  Case("duplicate and insufficient sources reject",()=>{var a=Thing();Throws(()=>RedPacketStackTemplate.PreparePhysical(new StackedThings(new[]{a,a}),100,Serialize));Throws(()=>RedPacketStackTemplate.PreparePhysical(new StackedThings(new[]{a}),100,Serialize));});
  Case("delivery count bounds retained",()=>{Check(RedPacketStackTemplate.DeliveryCounts(200,75).SequenceEqual(new[]{75,75,50}));Throws(()=>RedPacketStackTemplate.DeliveryCounts(1001,1));});
  Case("notification waits for context then delivers once",()=>{var q=new RedPacketSummaryQueue();q.Enqueue(Notice());int n=0;q.Drain("sender",false,_=>n++,null);Check(n==0&&q.Count==1);q.Drain("sender",true,_=>n++,null);q.Enqueue(Notice());q.Drain("sender",true,_=>n++,null);Check(n==1&&q.Count==0);});
  Case("completion and expiry deduplicate independently",()=>{var q=new RedPacketSummaryQueue();q.Enqueue(Notice());q.Enqueue(Notice("packet:expired"));int n=0;q.Drain("sender",true,_=>n++,null);Check(n==2);});
  Case("uncertain letter hook failure never repeats event or letter",()=>{var q=new RedPacketSummaryQueue();q.Enqueue(Notice());int attempted=0,failed=0;q.Drain("sender",true,_=>{attempted++;throw new NullReferenceException();},_=>failed++);q.Enqueue(Notice());q.Drain("sender",true,_=>attempted++,null);Check(attempted==1&&failed==1&&q.Count==0);});
  Case("notification scope reset drops old owner data",()=>{var q=new RedPacketSummaryQueue();q.Enqueue(Notice());q.Drain("another-user",false,_=>throw new Exception(),null);Check(q.Count==0);q.Clear();Check(q.Enqueue(Notice())&&q.Count==1);q.Clear();Check(q.Count==0);});
  Case("notification queue bounded and stores value copies",()=>{var q=new RedPacketSummaryQueue();for(int i=0;i<64;i++)Check(q.Enqueue(Notice(i.ToString())));Check(!q.Enqueue(Notice("overflow")));var value=Notice();q.Clear();q.Enqueue(value);value.Text="changed";q.Drain("sender",true,n=>Check(n.Text=="Done"),null);});
  Console.WriteLine(passed+" repair cases passed (algorithm seams; game acceptance remains required).");
 }
}
