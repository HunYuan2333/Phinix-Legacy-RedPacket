using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using PhinixClient;
using PhinixClient.Framework;
using Phinix.LegacyRedPacketExtension;
using UnityEngine;
using Utils.Framework;
internal static class CurrentProbe
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private sealed class ThemeRegistry : IClientThemeRegistry, IClientThemeRegistration
    {
        internal int Colors, Commits, Releases;
        internal ClientRgba Registered;
        public IClientThemeRegistration RegisterModule(IPhinixExtensionModule module, string keyNamespace=null, string ownerLabel=null) => this;
        public void RegisterColor(string key, ClientRgba value, string label=null, string fallbackKey=null, string group=null, string description=null) { Colors++; Registered=value; }
        public void RegisterFloat(string key,float value,float minimum=0,float maximum=1,string label=null,string group=null,string description=null) { }
        public void Commit() { Commits++; }
        public void Dispose() { Releases++; }
    }
    private static void RunThemeBinding(Assembly candidate, ExtensionHostContext host, Module module)
    {
        var registry=new ThemeRegistry();host.AddService<IClientThemeRegistry>(registry);
        var definitionType=candidate.GetTypes().Single(t=>t.Name=="PluginColor");
        var definition=Activator.CreateInstance(definitionType,Private,null,new object[]{"probe.color",new Color(.7f,.6f,.5f,1),"Probe",null},null);
        var definitions=Array.CreateInstance(definitionType,1);definitions.SetValue(definition,0);
        var adapter=candidate.GetTypes().Single(t=>t.Name=="PluginThemeAdapter");
        var source=adapter.GetMethod("Create",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{host,module,"probe","Probe",definitions,new Dictionary<string,Color>{{"probe.color",Color.white}}});
        Program.Check(registry.Colors==1 && registry.Commits==1 && registry.Registered.Red==.7f,"Actual plugin theme adapter registers through the neutral contract assembly");
        var palette=source.GetType().GetMethod("Capture",BindingFlags.Instance|BindingFlags.Public).Invoke(source,null);
        var color=(Color)palette.GetType().GetProperty("Item",Private).GetValue(palette,new object[]{"probe.color"});
        Program.Check(color.r==.2f && color.g==.3f && color.b==.4f,"Actual plugin binds neutral reader and shared game color bridge across assemblies");
        ((IDisposable)source).Dispose();((IDisposable)source).Dispose();
        Program.Check(registry.Releases==1,"Actual split theme binding releases the owned registration once");
    }

    private static object Call(object obj,string name,params object[] args)=>obj.GetType().GetMethod(name,Private).Invoke(obj,args);
    private static object Get(object obj,string name)=>obj.GetType().GetField(name,Private).GetValue(obj);
    private static void Set(object obj,string name,object value)=>obj.GetType().GetField(name,Private).SetValue(obj,value);
    public static void Run(Assembly candidate)
    {
        CheckExpiryRevision(candidate);
        CheckAutomaticMapChoice(candidate,"Chemfuel",750,750,1000);
        CheckAutomaticMapChoice(candidate,"ChunkMarble",1,1,2);
        var provider=new Provider();int opens=0,updates=0,closes=0;
        var wrapper=new ClientPresentationMainTab(provider,()=>opens++,()=>updates++,()=>closes++,"packets");
        wrapper.ViewUpdate();Program.Check(updates==0,"Hidden provider receives no owner update");
        wrapper.ViewOpened();wrapper.ViewOpened();wrapper.ViewUpdate();wrapper.ViewClosed();wrapper.ViewClosed();
        Program.Check(opens==1&&updates==1&&closes==1,"Provider lifecycle open/close is paired and idempotent");
        wrapper.ViewOpened();wrapper.ViewClosed();Program.Check(opens==2&&closes==2,"Provider can reopen");
        Program.Check(wrapper.TabLabel==provider.TabLabel&&wrapper.TabOrder==provider.TabOrder&&wrapper.LayoutHints.MinimumContentSize.x==provider.LayoutHints.MinimumContentSize.x&&wrapper.NavigationId=="packets","Public wrapper preserves navigation and responsive hints");
        int failedCleanup=0;var bad=new ClientPresentationMainTab(provider,()=>throw new InvalidOperationException(),()=>{},()=>failedCleanup++,"failed");
        try{bad.ViewOpened();}catch(InvalidOperationException){}bad.ViewClosed();Program.Check(failedCleanup==1,"Failed open releases the partially opened owner exactly once");
        var host=new ExtensionHostContext();var services=new Services();var module=new Module();
        host.AddService<IClientPresentationService>(services);host.AddService<IClientExtensionPointService>(services);host.AddService<IClientTheme>(services);
        RunThemeBinding(candidate, host, module);
        Type bridgeType=candidate.GetType("Phinix.LegacyRedPacketExtension.Client.RedPacketHostPresentation",true);
        var bridge=Activator.CreateInstance(bridgeType,Private,null,new object[]{host,module,typeof(IClientPresentationService).Assembly},null);
        Program.Check(services.Definitions==1&&services.PointId==RedPacketPresentationActionContext.PointId,"Adapter defines the ordinary public point once");
        Program.Check((long)bridgeType.GetProperty("ThemeVersion",Private).GetValue(bridge)==7,"Adapter reads borrowed Host theme version");
        var actionType=candidate.GetType("Phinix.LegacyRedPacketExtension.Client.HostAction",true);int invoked=0;
        var action=Activator.CreateInstance(actionType,Private,null,new object[]{"send","Send",(Action)(()=>invoked++),null},null);
        Set(action,"Kind","Primary");Set(action,"Selected",true);Set(action,"ConfirmTitle","Confirm");Set(action,"ConfirmText","Submission is not business confirmation");Set(action,"ConfirmAccept","Send");Set(action,"ConfirmCancel","Keep");
        var actionArray=Array.CreateInstance(actionType,1);actionArray.SetValue(action,0);
        Type factoryType=typeof(Func<,>).MakeGenericType(typeof(RedPacketPresentationActionContext),typeof(IEnumerable<>).MakeGenericType(actionType));
        var parameter=Expression.Parameter(typeof(RedPacketPresentationActionContext));
        var callback=Expression.Lambda(factoryType,Expression.Convert(Expression.Constant(actionArray),factoryType.GetMethod("Invoke").ReturnType),parameter).Compile();
        ((IDictionary)Get(bridge,"Sources"))["view"]=callback;
        bool current=true;var context=new RedPacketPresentationActionContext("view",1,"packet","item",200,100,5,"Normal",true,false,1);
        object batch=Call(bridge,"Collect",context,(Func<bool>)(()=>current),actionArray);
        var bindings=(IList)Get(batch,"Bindings");Program.Check(bindings.Count==1,"Real Host action binding is collected");
        var binding=(ClientPresentationAction)Get(bindings[0],"Model");
        Program.Check(binding.Kind==ClientButtonKind.Primary&&binding.IsSelected&&binding.Confirmation.Text.Contains("not business"),"Public selected/primary/confirmation metadata is retained");
        binding.Action.TryExecute();Program.Check(invoked==1,"Invocation uses the one public registry action");current=false;binding.Action.TryExecute();Program.Check(invoked==1,"Registry receives the exact stale-context guard");current=true;
        var field=(ClientPresentationTextField)Call(bridge,"Field","quantity","Quantity","200",true,"0–100",null,null);
        Program.Check(field.InputKind==ClientPresentationTextInputKind.Integer&&field.ValidationText=="0–100","Adapter constructs the public integer field with owner validation");
        Type rowType=candidate.GetType("Phinix.LegacyRedPacketExtension.Client.HostRow",true);
        object row=Activator.CreateInstance(rowType,Private,null,new object[]{"one","Claim","100"},null);Set(row,"Badge","Best");Set(row,"BadgeColor","redpacket.accent");
        var rowArray=Array.CreateInstance(rowType,1);rowArray.SetValue(row,0);
        var list=(ClientPresentationList)Call(bridge,"List","claims",rowArray,"No claims",null,true,true);
        Program.Check(list.CompactRows,"Red packet rows opt into the same public compact list layout");
        Program.Check(list.IsReadOnly&&list.Rows[0].Badges[0].ColorKey=="redpacket.accent","Readonly claim projection uses real Host list and theme badge");
        Program.Check(list.HoverColorKey=="redpacket.highlight"&&list.SelectionColorKey=="redpacket.accent","Registered plugin list theme roles survive readonly conversion");
        object section=Call(bridge,"Section","draft","Draft","Projection only",bindings,new object[]{field},list,"Waiting","Pending",true,null);
        section=Call(bridge,"FieldColumns",section);
        section=Call(bridge,"InlineToolbar",section);
        Program.Check(((ClientPresentationSection)section).InlineToolbar,"Red packet toolbars bind the public inline toolbar capability");
        Program.Check(((ClientPresentationSection)section).FieldColumns==2,"Adapter binds the same public responsive field-column hint");
        object view=Call(bridge,"Open","view");
        Call(bridge,"Publish",view,"view","Red packets",1L,new[]{section},batch,(Func<bool>)(()=>current),0,null,null,0);
        Program.Check(services.View.Page.Sections[0].Status.Tone==ClientPresentationTone.Pending&&Get(batch,"Owned")==null,"Publication transfers the one owned action set and the actual public page");
        services.View.Inputs.Enqueue(new ClientPresentationInput(1,"quantity",ClientPresentationInputKind.TextChanged,"100"));
        var method=bridgeType.GetMethod("Input",Private);object[] inputArgs={view,null};
        Program.Check((bool)method.Invoke(bridge,inputArgs)&&(string)Get(inputArgs[1],"Value")=="100"&&(long)Get(inputArgs[1],"Revision")==1,"Revisioned Host input is preserved by the adapter");
        CheckImeInputBatch(candidate,bridge);
        ((IDisposable)view).Dispose();Program.Check((bool)Call(bridge,"Closed",view)&&services.SetsDisposed==1,"Closing the view releases its transferred set");
        var orphan=new RedPacketPresentationActionContext("gone",2,null,null,0,0,0,null,false,false,0);
        object empty=Call(bridge,"Collect",orphan,(Func<bool>)(()=>true),Array.CreateInstance(actionType,0));Program.Check(((IList)Get(empty,"Bindings")).Count==0,"Closed/foreign view identity grants no official operation");((IDisposable)empty).Dispose();
        ((IDisposable)bridge).Dispose();((IDisposable)bridge).Dispose();Program.Check(services.ScopeDisposals==1,"Module-owned ordinary contribution scope is released once");
        var actualModule=(IActivatablePhinixExtensionModule)Activator.CreateInstance(candidate.GetType("Phinix.LegacyRedPacketExtension.Client.BuiltInLegacyRedPacketClientExtension",true));
        var sourceToken=new Token(true);var codecToken=new Token(false);
        actualModule.GetType().GetField("inventorySourceRegistration",Private).SetValue(actualModule,sourceToken);
        actualModule.GetType().GetField("inventoryCodecRegistration",Private).SetValue(actualModule,codecToken);
        bool cleanupFailed=false;try{actualModule.Shutdown(host);}catch(AggregateException){cleanupFailed=true;}
        Program.Check(cleanupFailed&&sourceToken.Calls==1&&codecToken.Calls==1,"Actual module continues cleanup after a source lease failure");
        actualModule.Shutdown(host);Program.Check(sourceToken.Calls==1&&codecToken.Calls==1,"Actual module repeated shutdown does not release old tokens twice");

    }
    private static void CheckImeInputBatch(Assembly candidate,object bridge)
    {
        var type=candidate.GetType("Phinix.LegacyRedPacketExtension.Client.RedPacketPresentation",true);
        var owner=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
        var view=(View)Call(bridge,"Open","ime");
        Set(owner,"host",bridge);Set(owner,"view",view);Set(owner,"revision",7L);Set(owner,"dirty",false);
        view.Publish(new ClientPresentationPage("ime","",7,new[]{new ClientPresentationSection("search","","",fields:new[]{new ClientPresentationTextField("search","","")})}),null,()=>!(bool)Get(owner,"dirty"));
        Action enqueue=()=>{foreach(string text in new[]{"医","医疗","医疗包"})view.Inputs.Enqueue(new ClientPresentationInput(7,"search",ClientPresentationInputKind.TextChanged,text));};
        enqueue();int oldCount=0;
        while(true){object[] args={view,null};if(!(bool)bridge.GetType().GetMethod("Input",Private).Invoke(bridge,args))break;oldCount++;Set(owner,"dirty",true);}
        Program.Check(oldCount==1&&view.Inputs.Count==0,"Per-event owner invalidation reproduces first-character-only candidate submission");
        Set(owner,"dirty",false);enqueue();
        var batch=(IList)Call(owner,"ReadInputBatch");string final="";
        foreach(var input in batch){final=(string)Get(input,"Value");Set(owner,"dirty",true);}
        Program.Check(batch.Count==3&&final=="医疗包"&&view.Inputs.Count==0,"Actual owner input batching preserves every IME draft before applying invalidation");
        enqueue();Program.Check(((IList)Call(owner,"ReadInputBatch")).Count==0,"Already stale page still rejects queued edits");
        Set(owner,"dirty",false);view.Inputs.Enqueue(new ClientPresentationInput(6,"search",ClientPresentationInputKind.TextChanged,"stale"));enqueue();
        batch=(IList)Call(owner,"ReadInputBatch");Program.Check(batch.Count==3&&batch.Cast<object>().All(i=>(long)Get(i,"Revision")==7),"Batch retains rendered revision ownership");
        view.Dispose();
    }
    private sealed class OnlineSession : IClientSessionContext
    {public bool Authenticated=>true;public bool LoggedIn=>true;public string SessionId=>"test";public string Uuid=>"viewer";}
    private static void CheckAutomaticMapChoice(Assembly candidate,string definitionName,int stackLimit,int eachCount,int selectedCount)
    {
        // Real owner/selection classes and game metadata, without native Scribe or world delivery.
        var def=(Verse.ThingDef)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Verse.ThingDef));
        def.defName=definitionName;def.stackLimit=stackLimit;def.category=Verse.ThingCategory.Item;
        var first=(Verse.Thing)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Verse.Thing));
        var second=(Verse.Thing)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Verse.Thing));
        first.thingIDNumber=101;second.thingIDNumber=102;
        first.def=def;first.stackCount=eachCount;second.def=def;second.stackCount=eachCount;
        Type itemType=candidate.GetType("Phinix.LegacyRedPacketExtension.Client.RedPacketAvailableItem",true);
        var from=itemType.GetMethod("FromPhysical",BindingFlags.Public|BindingFlags.Static);
        var one=from.Invoke(null,new object[]{new Phinix.TradeExtension.Client.StackedThings(new[]{first})});
        var two=from.Invoke(null,new object[]{new Phinix.TradeExtension.Client.StackedThings(new[]{second})});
        var items=Array.CreateInstance(itemType,2);items.SetValue(one,0);items.SetValue(two,1);
        Type choiceType=candidate.GetType("Phinix.LegacyRedPacketExtension.Client.RedPacketItemChoice",true);
        var choices=(IList)choiceType.GetMethod("Build",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{items});
        Program.Check(choices.Count==1,"Real "+definitionName+" sources occupy one item choice");
        var tab=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(candidate.GetType("Phinix.LegacyRedPacketExtension.Client.RedPacketTab",true));
        var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));list.Add(one);list.Add(two);
        Set(tab,"availableItems",list);Set(tab,"filteredItems",Activator.CreateInstance(list.GetType()));
        var selection=Call(tab,"PresentationSelectChoice",choices[0]);
        Program.Check(selection!=null&&(int)itemType.GetProperty("Count").GetValue(selection)==2*eachCount,"The real tab binds the entire map choice instead of its first pile");
        Call(tab,"PresentationQuantity",selection,selectedCount);
        Program.Check((int)itemType.GetProperty("Selected").GetValue(selection)==selectedCount,"A quantity larger than a single pile reaches the send owner");
        Program.Check((bool)Call(tab,"PresentationHasItem",selection),"A registered composite selection is accepted by freshness checks");
        tab.GetType().GetMethod("ClearItems",BindingFlags.Instance|BindingFlags.Public).Invoke(tab,null);
        Program.Check(!(bool)Call(tab,"PresentationHasItem",selection),"Refreshing the owner invalidates the composite selection");
        Program.Check(first.stackCount==eachCount&&second.stackCount==eachCount,"Browsing and clearing a combined selection never extracts physical items");
    }

    private static void CheckExpiryRevision(Assembly candidate)
    {
        var type=candidate.GetType("Phinix.LegacyRedPacketExtension.Client.RedPacketStateMachine",true);
        var state=Activator.CreateInstance(type,new object[]{new OnlineSession(),null,null,null,null,null,null});
        const BindingFlags statics=BindingFlags.Static|BindingFlags.NonPublic;
        var packets=(IDictionary)type.GetField("Packets",statics).GetValue(null);
        var next=type.GetField("nextExpiryCheckUtc",statics);
        var update=type.GetMethod("UpdateExpiry",Private);
        Func<int> version=()=> (int)type.GetProperty("BadgeVersion").GetValue(state);
        Action poll=()=>{next.SetValue(null,DateTime.MinValue);update.Invoke(state,null);};
        packets.Clear();
        try
        {
            var pending=(IDictionary)type.GetField("PendingClaims",statics).GetValue(null);
            var nextClaim=type.GetField("nextClaimTimeoutCheckUtc",statics);
            var checkClaims=type.GetMethod("CheckClaimTimeouts",Private);
            pending.Clear();
            for(int i=0;i<5;i++){nextClaim.SetValue(null,DateTime.MinValue);checkClaims.Invoke(state,null);poll();}
            Program.Check(version()==0,"Both idle timer paths preserve the published revision and confirmation");
            pending.Add("waiting",DateTime.UtcNow);
            nextClaim.SetValue(null,DateTime.MinValue);checkClaims.Invoke(state,null);
            Program.Check(version()==0&&pending.Contains("waiting"),"An unexpired claim wait does not invalidate the page");pending.Clear();nextClaim.SetValue(null,DateTime.MinValue);
            for(int i=0;i<5;i++)poll();
            Program.Check(version()==0,"Idle expiry polls do not invalidate a published page or its confirmation");
            var packetType=candidate.GetType("Phinix.LegacyRedPacketExtension.Client.RedPacket",true);
            var packet=Activator.CreateInstance(packetType);
            packetType.GetField("Id").SetValue(packet,"expiry");
            packetType.GetField("SenderUuid").SetValue(packet,"sender");
            packetType.GetField("ExpiresAtUtc").SetValue(packet,DateTime.UtcNow.AddMinutes(1));
            packets.Add("expiry",packet);poll();
            Program.Check(version()==0,"An unchanged live packet does not increment the projection revision");
            packetType.GetField("ExpiresAtUtc").SetValue(packet,DateTime.UtcNow.AddMinutes(-1));poll();
            Program.Check(version()==1&&(bool)packetType.GetField("Expired").GetValue(packet),"Real expiry changes the packet and invalidates old actions once");
            poll();Program.Check(version()==1,"Already expired packets do not repeatedly invalidate confirmation");
        }
        finally {packets.Clear();next.SetValue(null,DateTime.MinValue);}
    }
    private sealed class Token : IDisposable
    {private readonly bool fail;public int Calls;public Token(bool fail){this.fail=fail;}public void Dispose(){Calls++;if(fail)throw new InvalidOperationException("release");}}
    private sealed class Module : IPhinixExtensionModule
    {public string ExtensionId=>"builtin.legacy-redpacket";public void Register(IExtensionBuilder b){} }
    private sealed class Provider : IMainTabProvider,IResponsiveMainTabProvider
    {public string TabLabel=>"Packets";public float TabOrder=>1100;public UiLayoutHints LayoutHints {get;}=new UiLayoutHints(new Vector2(320,300),new Vector2(900,600),true);public void Draw(Rect rect){} }
    private sealed class Services : IClientPresentationService,IClientExtensionPointService,IClientTheme
    {
        public int Definitions,ScopeDisposals,SetsDisposed;public string PointId;public View View;
        private Delegate factory;
        public long Revision=>3;public long Version=>7;public event Action Changed {add{}remove{}}
        public IClientPresentationSession Open(IPhinixExtensionModule owner,string id)=>View=new View(this,id);
        public IClientExtensionPointScope ForModule(IPhinixExtensionModule module)=>new Scope(this);
        public IReadOnlyList<ClientExtensionPointInfo> CapturePoints()=>Array.Empty<ClientExtensionPointInfo>();
        private sealed class Scope : IClientExtensionPointScope
        {private readonly Services s;private bool disposed;public Scope(Services s){this.s=s;}public IClientActionPoint<T> DefineActions<T>(string id)where T:class{s.PointId=id;s.Definitions++;return new Point<T>(s);}public void ContributeActions<T>(string id,Func<T,IEnumerable<ClientActionDefinition>> f,float order=0)where T:class{s.factory=f;}public void Dispose(){if(!disposed){disposed=true;s.ScopeDisposals++;}}}
        private sealed class Point<T> : IClientActionPoint<T>where T:class
        {private readonly Services s;public Point(Services s){this.s=s;}public IClientActionSet Collect(T context,Func<bool> current=null)=>new Set(s,((Func<T,IEnumerable<ClientActionDefinition>>)s.factory)(context).Select(d=>(IClientContributedAction)new ActionBinding(d,current)).ToArray());}
        private sealed class Set : IClientActionSet
        {private readonly Services s;private bool disposed;public Set(Services s,IClientContributedAction[] actions){this.s=s;Actions=actions;}public IReadOnlyList<IClientContributedAction> Actions {get;}public void Dispose(){if(!disposed){disposed=true;s.SetsDisposed++;}}}
        private sealed class ActionBinding : IClientContributedAction
        {private readonly ClientActionDefinition d;private readonly Func<bool> current;public ActionBinding(ClientActionDefinition d,Func<bool> current){this.d=d;this.current=current;}public string OwnerId=>"builtin.legacy-redpacket";public string Id=>d.Id;public string Label=>d.Label;public string DisabledReason=>d.DisabledReason;public ClientActionExecutionResult TryExecute(){if(!current()||DisabledReason!=null)return new ClientActionExecutionResult(false,false,"stale");d.Execute();return new ClientActionExecutionResult(true,true,null);}}
        public ClientRgba PrimaryText=>new ClientRgba(1,1,1);public ClientRgba SecondaryText=>new ClientRgba(1,1,1);public ClientRgba Background=>new ClientRgba(0,0,0,0);public ClientRgba Surface=>new ClientRgba(0,0,0,0);public ClientRgba Separator=>new ClientRgba(0,0,0,0);public ClientRgba HoverHighlight=>new ClientRgba(0,0,0,0);public ClientRgba Pending=>new ClientRgba(1,1,1);public ClientRgba Error=>new ClientRgba(1,1,1);public ClientRgba Success=>new ClientRgba(1,1,1);public ClientRgba Warning=>new ClientRgba(1,1,1);
        public ClientRgba GetColor(string key)=>new ClientRgba(1,1,1);public bool TryGetColor(string key,out ClientRgba c){c=new ClientRgba(1,1,1);return true;}public float GetFloat(string key,float fallback=0)=>fallback;
        public ClientThemeSnapshot Capture()=>new ClientThemeSnapshot(Version,new Dictionary<string,ClientRgba>{{"probe.color",new ClientRgba(.2f,.3f,.4f,1)}},new Dictionary<string,float>());
    }
    private sealed class View : IClientPresentationSession
    {
        private readonly Services s;private IClientActionSet set;private Func<bool> current;public ClientPresentationPage Page;public readonly Queue<ClientPresentationInput> Inputs=new Queue<ClientPresentationInput>();
        public View(Services s,string id){this.s=s;PageId=id;}public string OwnerId=>"builtin.legacy-redpacket";public string PageId {get;}public bool IsClosed {get;private set;}public string FailureCode=>null;public ClientActionExecutionResult LastActionResult=>null;
        public void Publish(ClientPresentationPage page,IClientActionSet owned,Func<bool> current){set?.Dispose();set=owned;Page=page;this.current=current;}
        public void Draw(ClientPresentationBounds rect){}public float MeasureComposerHeight(float width)=>0;
        public bool TryDequeueInput(out ClientPresentationInput input){input=null;if(current!=null&&!current()){Inputs.Clear();return false;}input=Inputs.Count==0?null:Inputs.Dequeue();return input!=null;}
        public string ComposerDraft=>"";public string ComposerControlName=>"";public bool ComposerFocused=>false;public void SetComposerDraft(string value){}public void RequestComposerFocus(){}public void InvokeAction(string id){}public IReadOnlyList<ClientPresentationMenuItem> CreateActionMenuItems()=>new List<ClientPresentationMenuItem>();public void ScrollToRow(string feed,string row){}public void ScrollToEnd(){}
        public void Dispose(){if(IsClosed)return;IsClosed=true;set?.Dispose();set=null;}
    }
}
