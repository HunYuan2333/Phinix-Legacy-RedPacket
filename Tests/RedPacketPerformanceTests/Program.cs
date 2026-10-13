using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Verse;
using PhinixClient.Trade;
using Phinix.TradeExtension.Client;
using Phinix.LegacyRedPacketExtension.Client;
internal static class Program
{
    private sealed class Stateless : ThingComp {}
    private sealed class Stateful : ThingComp {public int Charge=1;}
    private sealed class Fixture
    {
        public string Name; public Thing[] Items; public bool Send;
    }
    private sealed class Sample
    {
        public double Milliseconds,MaxSliceMs;public long AllocatedBytes;public int Serialized,Groups,Warnings,Slices;
    }
    private sealed class Result
    {
        public string Scenario,Pipeline; public int Sources,Rounds,Serialized,Groups,Warnings;
        public double MedianMs,P95Ms,MinMs,MaxMs,MedianAllocatedMiB,MedianMaxSliceMs,WorstSliceMs; public int Slices;
    }
    private static int serialized;
    private static TradeItemSnapshot Serialize(Thing thing)
    {
        serialized++;
        string xml="<saveable><def>"+thing.def.defName+"</def><id>source</id><map>2</map><pos>(1,2,3)</pos><stackCount>"+thing.stackCount+"</stackCount>"+thing.State+"</saveable>";
        byte[] payload;
        using(var stream=new MemoryStream())
        {
            using(var gzip=new GZipStream(stream,CompressionMode.Compress,true))
            {var bytes=Encoding.UTF8.GetBytes(xml);gzip.Write(bytes,0,bytes.Length);}
            payload=stream.ToArray();
        }
        return new TradeItemSnapshot(thing.def.defName,thing.stackCount,thing.HitPoints,TradeItemQuality.None,null,null,StatefulTradeItemProtocol.ScribeCodecId,payload);
    }
    // Measurement-only eager comparator, not a runtime renderer/business path.
    // Same 1000 inspections and 4 Mi-character budget, without cheap candidate elimination.
    private static List<StackedThings> Eager(Thing[] items)
    {
        var output=new List<StackedThings>();var index=new Dictionary<string,List<Thing>>();
        int inspected=0,budget=4*1024*1024;
        using(var hash=SHA256.Create())
        foreach(var thing in items)
        {
            string key=null;
            if(RedPacketStackTemplate.IsKnownPerUnitStack(thing)&&inspected<1000&&budget>0)
            {
                inspected++;
                string state=RedPacketStackTemplate.ComparableState(Serialize(thing));budget-=state.Length;
                if(state.Length<=128*1024&&budget>=0)key=Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(state)));
            }
            if(key==null){output.Add(new StackedThings(new[]{thing}));continue;}
            if(!index.TryGetValue(key,out var bucket))index[key]=bucket=new List<Thing>();
            if(bucket.Count>0&&(!thing.CanStackWith(bucket[0])||!bucket[0].CanStackWith(thing))){output.Add(new StackedThings(new[]{thing}));continue;}
            bucket.Add(thing);
        }
        output.AddRange(index.Values.Select(bucket=>new StackedThings(bucket)));return output;
    }
    private static Sample Run(Fixture fixture,string pipeline)
    {
        serialized=0;int warnings=0,groups=0,slices=0;double maxSlice=0;
        long allocated=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
        List<StackedThings> result=null;
        if(fixture.Send)
        {
            var selection=RedPacketStackTemplate.PreparePhysical(new StackedThings(fixture.Items),fixture.Items.Length,Serialize);
            if(selection.Template.StackCount!=fixture.Items.Length||selection.Sources.Things.Count!=fixture.Items.Length)throw new Exception("Send quantity/source mismatch");groups=1;
        }
        else if(pipeline=="sliced-index")
        {
            using(var job=RedPacketStackTemplate.BeginGrouping(new[]{new StackedThings(fixture.Items)},(_,level)=>warnings++,Serialize))
            {
                while(!job.Completed)
                {
                    long start=Stopwatch.GetTimestamp();job.Advance(2,2048);slices++;
                    maxSlice=Math.Max(maxSlice,(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency);
                }
                result=job.Results.ToList();
            }
        }
        else if(pipeline=="coarse-index")
            result=RedPacketStackTemplate.GroupPhysical(new[]{new StackedThings(fixture.Items)},(_,level)=>warnings++,Serialize).ToList();
        else result=Eager(fixture.Items);
        watch.Stop();long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
        // Source conservation checks are deliberately outside timing/allocation measurement.
        if(result!=null)
        {
            groups=result.Count;var sources=result.SelectMany(g=>g.Things).ToArray();
            if(sources.Length!=fixture.Items.Length||sources.Distinct().Count()!=fixture.Items.Length||sources.Sum(t=>(long)t.stackCount)!=fixture.Items.Sum(t=>(long)t.stackCount))throw new Exception("Grouping lost or duplicated sources");
        }
        return new Sample{Milliseconds=watch.Elapsed.TotalMilliseconds,AllocatedBytes=bytes,Serialized=serialized,Groups=groups,Warnings=warnings,MaxSliceMs=maxSlice,Slices=slices};
    }
    private static string State(int bytes)
    {
        var random=new Random(117);var text=new StringBuilder("<health>100</health><components>");
        while(text.Length<bytes){text.Append("<value>");for(int i=0;i<40;i++)text.Append((char)('a'+random.Next(26)));text.Append("</value>");}
        return text.Append("</components>").ToString();
    }
    private static Fixture Build(string name,int count,int bytes,int definitions=1,int component=0,bool send=false)
    {
        var defs=Enumerable.Range(0,definitions).Select(i=>new ThingDef{defName="Item"+i}).ToArray();string state=State(bytes);
        var items=new Thing[count];for(int i=0;i<count;i++)
        {
            Thing thing=component==0?new Thing():new ThingWithComps();thing.def=defs[i%definitions];thing.State=state;thing.stackCount=1;
            if(component>0)((ThingWithComps)thing).AllComps.Add(component==1?(object)new Stateless():new Stateful());items[i]=thing;
        }
        return new Fixture{Name=name,Items=items,Send=send};
    }
    private static Result Measure(Fixture fixture,string pipeline,int rounds)
    {
        for(int warm=0;warm<2;warm++)Run(fixture,pipeline);
        var samples=new List<Sample>();
        for(int i=0;i<rounds;i++)
        {GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();samples.Add(Run(fixture,pipeline));}
        var times=samples.Select(s=>s.Milliseconds).OrderBy(x=>x).ToArray();var allocations=samples.Select(s=>s.AllocatedBytes).OrderBy(x=>x).ToArray();
        if(samples.Any(s=>s.Serialized!=samples[0].Serialized||s.Groups!=samples[0].Groups||s.Warnings!=samples[0].Warnings))throw new Exception("Nondeterministic counters");
        return new Result{Scenario=fixture.Name,Pipeline=pipeline,Sources=fixture.Items.Length,Rounds=rounds,Serialized=samples[0].Serialized,Groups=samples[0].Groups,Warnings=samples[0].Warnings,Slices=samples[rounds/2].Slices,MedianMaxSliceMs=samples.Select(s=>s.MaxSliceMs).OrderBy(x=>x).ElementAt(rounds/2),WorstSliceMs=samples.Max(s=>s.MaxSliceMs),MedianMs=times[rounds/2],P95Ms=times[Math.Min(rounds-1,(int)Math.Ceiling(rounds*.95)-1)],MinMs=times[0],MaxMs=times[rounds-1],MedianAllocatedMiB=allocations[rounds/2]/1048576d};
    }
    private static void Main(string[] args)
    {
        int rounds=args.Length>1?int.Parse(args[1]):9;
        var fixtures=new[]{Build("simple-same-500",500,256),Build("simple-same-1000",1000,256),Build("simple-same-10000",10000,256),
            Build("unique-def-10000",10000,256,10000),Build("unique-def-50000",50000,256,50000),Build("mixed-10000-100-defs",10000,256,100),
            Build("complex-8KiB-1000",1000,8192),Build("complex-32KiB-1000",1000,32768),Build("oversized-160KiB-200",200,160*1024),
            Build("stateless-comp-5000",5000,256,1,1),Build("unknown-stateful-10000",10000,256,1,2),Build("send-recheck-500",500,256,1,0,true),Build("send-recheck-1000",1000,256,1,0,true)};
        var results=new List<Result>();foreach(var fixture in fixtures)
            foreach(string pipeline in fixture.Send?new[]{"send-recheck"}:new[]{"coarse-index","sliced-index","eager-reference"})
            {var result=Measure(fixture,pipeline,rounds);results.Add(result);Console.WriteLine($"{result.Scenario,-29} {pipeline,-16} median={result.MedianMs,8:F2}ms p95={result.P95Ms,8:F2}ms alloc={result.MedianAllocatedMiB,8:F2}MiB serialized={result.Serialized} groups={result.Groups} slices={result.Slices} maxSlice={result.WorstSliceMs:F2}ms");}
        if(args.Length>0)File.WriteAllText(args[0],JsonSerializer.Serialize(new{Runtime=RuntimeInformation.FrameworkDescription,OS=RuntimeInformation.OSDescription,Processors=Environment.ProcessorCount,Build="Release",Rounds=rounds,TieredCompilation=Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),Notes="White-box linked production index with Verse stubs and synthetic gzip/XML serializer. No real Scribe, maps, Unity, relay or delivery. Fixture construction and conservation assertions excluded. GC before each measured round. Sample p95 is not a production SLA.",Results=results},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
    }
}
