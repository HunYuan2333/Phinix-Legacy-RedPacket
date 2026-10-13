using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Utils;
using Verse;
namespace Phinix.LegacyRedPacketExtension.Client
{
    // Plugin projection, drafts and intent adapter. All controls/layout/actions/confirmation are public Host models.
    internal sealed class RedPacketPresentation : IDisposable
    {
        private const string MainId="redpacket-main";
        private readonly RedPacketTab tab;
        private readonly RedPacketStateMachine state;
        private readonly RedPacketHostPresentation host;
        private readonly IClientSettingsContext settingsContext;
        private readonly RedPacketSettings settings;
        private readonly Action<string,LogLevel> log;
        private object view,result,language;
        private long revision,themeVersion=-1,pointVersion=-1,second=-1;
        private int badgeVersion=-1,sendCount=-1,itemPage,itemPages=1,packetPage,packetPages=1,itemEpoch;
        private bool dirty=true,disposed,shownOnline;
        private string search="",quantity="0",packetCount="1",selectedPacketId,shownUuid,shownPacketState,shownTimeState,notice="",claimNotice="";
        private RedPacket[] packetsCache=Array.Empty<RedPacket>();private int packetsCacheVersion=-1;
        private RedPacketType type=RedPacketType.Normal;
        private RedPacketAvailableItem selectedItem;
        private Game game;
        private readonly Dictionary<string,RedPacketAvailableItem> itemRows=new Dictionary<string,RedPacketAvailableItem>();
        private readonly Dictionary<string,string> packetRows=new Dictionary<string,string>();
        private readonly HashSet<RedPacketHostWindow> windows=new HashSet<RedPacketHostWindow>();
        internal RedPacketPresentation(RedPacketTab tab,RedPacketStateMachine state,RedPacketHostPresentation host,IClientSettingsContext context,RedPacketSettings settings,Action<string,LogLevel> log)
        {this.tab=tab;this.state=state;this.host=host;settingsContext=context;this.settings=settings;this.log=log;}
        internal static string L(string key,params NamedArgument[] arguments)=>("Phinix_legacyRedpacket_"+key).Localize(arguments);
        private static string Safe(string text){string value=TextHelper.StripRichText(text??"");return value.Length>4096?value.Substring(0,4096):value;}
        private static string RowId(string value)
        {using(var hash=SHA256.Create())return "packet."+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
        internal void Open()
        {
            if(disposed||view!=null&&!host.Closed(view))return;
            host.Sources[MainId]=_=>Actions();
            try {view=host.Open(MainId);dirty=true;Update();}catch {Close();throw;}
        }
        internal void Close(){var old=view;view=null;host.Sources.Remove(MainId);(old as IDisposable)?.Dispose();dirty=true;}
        internal void InvalidateItems(){selectedItem=null;quantity="0";itemPage=0;itemEpoch++;dirty=true;}
        internal void Draw(Rect rect){if(view!=null&&!disposed)host.Draw(view,rect);}
        private RedPacket[] PacketSnapshot()
        {if(packetsCacheVersion!=state.BadgeVersion){packetsCache=state.GetPacketsSnapshot();packetsCacheVersion=state.BadgeVersion;}return packetsCache;}
        private RedPacket Picked()=>PacketSnapshot().FirstOrDefault(p=>p.Id==selectedPacketId);
        private string TimeState()=>Math.Max(0,(int)Math.Ceiling((tab.PresentationCooldown-DateTime.UtcNow).TotalSeconds))+"|"+string.Join("|",PacketSnapshot().Where(p=>state.ShouldDisplayInList(p,DateTime.UtcNow,tab.PresentationUuid)).Select(p=>p.Id));
        private string PacketState(RedPacket p)
        {return p==null?"":p.Id+"|"+p.RemainingCount+"|"+p.RemainingPackets+"|"+p.Expired+"|"+(p.ExpiresAtUtc<=DateTime.UtcNow)+"|"+p.StateReady+"|"+p.HasClaimed(tab.PresentationUuid)+"|"+state.IsPendingClaim(p.Id);}
        private string SendBlock()
        {
            if(!tab.PresentationOnline)return L("pleaseLogInPlaceholder");
            if(tab.PresentationItemsLoading)return L("uiItemsGrouping");
            if(Current.Game==null)return L("uiGameRequired");
            if(DateTime.UtcNow<tab.PresentationCooldown)return L("sendCooldownMessage",(int)Math.Ceiling((tab.PresentationCooldown-DateTime.UtcNow).TotalSeconds));
            if(selectedItem==null||!tab.PresentationHasItem(selectedItem)||!RedPacketPresentationRules.Quantity(quantity,selectedItem.Count,out int count))return L("errorSelectItem");
            if(!RedPacketPresentationRules.Packets(packetCount,count,out _))return L("uiPacketCountInvalid",Math.Min(count,RedPacketLimits.MaxClaimDetailsPerPacket));
            return null;
        }
        private void Preset(int value)
        {
            if(selectedItem==null||!tab.PresentationHasItem(selectedItem))return;
            int count=Math.Max(0,Math.Min(selectedItem.Count,value));quantity=count.ToString(CultureInfo.InvariantCulture);
            tab.PresentationQuantity(selectedItem,count);dirty=true;
        }
        private IEnumerable<HostAction> Actions()
        {
            var actions=new List<HostAction>();
            bool online=tab.PresentationOnline&&Current.Game!=null;
            actions.Add(new HostAction("refresh",L("refreshButton"),()=>{tab.PresentationRefresh();dirty=true;},online?null:L("pleaseLogInPlaceholder")));
            actions.Add(new HostAction("settings",L("uiSettings"),OpenSettings));
            actions.Add(new HostAction("normal",L("typeNormal"),()=>{type=RedPacketType.Normal;dirty=true;}){Selected=type==RedPacketType.Normal});
            actions.Add(new HostAction("lucky",L("typeLucky"),()=>{type=RedPacketType.Lucky;dirty=true;}){Selected=type==RedPacketType.Lucky});
            string title=L("sendButton");
            bool special=state.IsSpecialPacketDefinition(selectedItem?.ThingDef?.defName);
            actions.Add(new HostAction("send",title,()=>{bool submitted=tab.PresentationSend(packetCount,type);notice=L(submitted?"uiSendSubmitted":"uiSendRejected");quantity="0";packetCount="1";dirty=true;},SendBlock())
            {Kind=special?"Danger":"Primary",ConfirmTitle=title,ConfirmText=L("uiSendConfirm",Safe(selectedItem?.Label),quantity,packetCount,L(type==RedPacketType.Normal?"typeNormal":"typeLucky"))+(special?"\n\n"+L("uiSpecialRisk"):""),ConfirmAccept=title,ConfirmCancel=L("uiKeepDraft")});
            if(selectedItem!=null)
            {
                actions.Add(new HostAction("all",L("uiAll"),()=>Preset(selectedItem.Count)));
                actions.Add(new HostAction("half",L("uiHalf"),()=>Preset(selectedItem.Count/2)));
                actions.Add(new HostAction("none",L("uiClear"),()=>Preset(0)));
            }
            if(itemPage>0)actions.Add(new HostAction("items.previous",L("uiPrevious"),()=>{itemPage--;dirty=true;}));
            if(itemPage+1<itemPages)actions.Add(new HostAction("items.next",L("uiNext"),()=>{itemPage++;dirty=true;}));
            if(packetPage>0)actions.Add(new HostAction("packets.previous",L("uiPrevious"),()=>{packetPage--;dirty=true;}));
            if(packetPage+1<packetPages)actions.Add(new HostAction("packets.next",L("uiNext"),()=>{packetPage++;dirty=true;}));
            var packet=Picked();
            if(packet!=null)
            {
                string blocked=ClaimBlock(packet);
                actions.Add(new HostAction("claim",L("claimButton"),()=>{state.RequestClaim(packet);claimNotice=L("uiClaimSubmitted");dirty=true;},blocked){Kind="Primary"});
                actions.Add(new HostAction("detail",L("detailButton"),()=>OpenDetail(packet.Id)));
            }
            return actions;
        }
        private string ClaimBlock(RedPacket packet)
        {
            if(!tab.PresentationOnline)return L("pleaseLogInPlaceholder");
            if(Current.Game==null)return L("uiGameRequired");
            if(state.IsPendingClaim(packet.Id))return L("pendingLabel");
            if(packet.IsSender(tab.PresentationUuid))return L("uiOwnPacket");
            if(packet.HasClaimed(tab.PresentationUuid))return L("claimedLabel");
            if(packet.Expired||packet.ExpiresAtUtc<=DateTime.UtcNow)return L("expiredLabel");
            if(packet.RemainingCount<=0||packet.RemainingPackets<=0)return L("emptyLabel");
            if(packet.StateRequired&&!packet.StateReady)return L("statePending");
            if(RedPacketStateMachine.IsUnknownTemplate(packet.Template))return L("uiUnknownItem");
            if(RedPacketStateMachine.IsMinifiedTemplate(packet.Template))return L("minifiedClaimReject");
            return RedPacketPresentationRules.Claimable(packet,tab.PresentationUuid,tab.PresentationOnline,state.IsPendingClaim(packet.Id),DateTime.UtcNow)?null:L("uiUnavailable");
        }
        private string Status(RedPacket packet)
        {
            if(state.IsPendingClaim(packet.Id))return L("pendingLabel");
            if(packet.Expired||packet.ExpiresAtUtc<=DateTime.UtcNow)return L("expiredLabel");
            if(packet.RemainingCount<=0||packet.RemainingPackets<=0)return L("emptyLabel");
            if(packet.HasClaimed(tab.PresentationUuid))return L("claimedLabel");
            if(packet.StateRequired&&!packet.StateReady)return L("statePending");
            if(RedPacketStateMachine.IsUnknownTemplate(packet.Template)||RedPacketStateMachine.IsMinifiedTemplate(packet.Template))return L("uiUnavailable");
            return packet.IsSender(tab.PresentationUuid)?L("uiSentByYou"):L("uiAvailable");
        }
        internal void Update()
        {
            if(disposed||view==null||host.Closed(view))return;
            if(!ReferenceEquals(game,Current.Game)){tab.ClearItems();selectedPacketId=null;notice="";claimNotice="";dirty=true;}
            if(tab.PresentationOnline&&Current.Game!=null&&tab.PresentationItemsDirty)tab.PresentationRefresh();
            tab.AdvanceItemGrouping();
            // Inputs belong to the item rendered by this revision; switching rows must not retarget its fields.
            var renderedItem=selectedItem;
            while(host.Input(view,out var input))
            {
                if(input.Revision!=revision)continue;
                if(input.Id=="search"){search=input.Value??"";itemPage=0;tab.PresentationSearch(search);}
                else if(input.Id=="items"&&itemRows.TryGetValue(input.Value,out var item)&&tab.PresentationHasItem(item))
                {if(selectedItem!=item){selectedItem=item;quantity="0";packetCount="1";tab.PresentationQuantity(item,0);}}
                else if(input.Id=="quantity"&&selectedItem!=null&&ReferenceEquals(renderedItem,selectedItem))
                {quantity=input.Value;if(RedPacketPresentationRules.Quantity(quantity,selectedItem.Count,out int count))tab.PresentationQuantity(selectedItem,count);}
                else if(input.Id=="packet-count"&&ReferenceEquals(renderedItem,selectedItem))packetCount=input.Value;
                else if(input.Id=="packets"&&packetRows.TryGetValue(input.Value,out string id)){selectedPacketId=id;claimNotice="";}
                dirty=true;
            }
            long tick=DateTime.UtcNow.Ticks/TimeSpan.TicksPerSecond;
            var picked=Picked();string pickedState=PacketState(picked);
            string timeState=second==tick&&badgeVersion==state.BadgeVersion?shownTimeState:TimeState();
            second=tick;
            if(!dirty&&timeState==shownTimeState&&badgeVersion==state.BadgeVersion&&sendCount==state.PendingInventorySendCount&&themeVersion==host.ThemeVersion&&pointVersion==host.PointVersion&&ReferenceEquals(language,RedPacketLocalization.LanguageToken)&&ReferenceEquals(result,host.LastResult(view))&&shownOnline==tab.PresentationOnline&&shownUuid==tab.PresentationUuid&&shownPacketState==pickedState)return;
            var packets=PacketSnapshot().Where(p=>state.ShouldDisplayInList(p,DateTime.UtcNow,tab.PresentationUuid)).ToArray();
            if(!packets.Any(p=>p.Id==selectedPacketId)){selectedPacketId=null;picked=null;pickedState="";}
            if(selectedItem!=null&&!tab.PresentationHasItem(selectedItem)){selectedItem=null;quantity="0";}
            itemPages=Math.Max(1,(tab.PresentationItems.Count+99)/100);itemPage=Math.Min(itemPage,itemPages-1);
            packetPages=Math.Max(1,(packets.Length+99)/100);packetPage=Math.Min(packetPage,packetPages-1);
            itemRows.Clear();packetRows.Clear();string selectedItemRow=null;
            var items=tab.PresentationItems.Skip(itemPage*100).Take(100).Select((item,index)=>
            {
                string id="item."+itemEpoch+"."+(itemPage*100+index);itemRows[id]=item;if(ReferenceEquals(item,selectedItem))selectedItemRow=id;
                return new HostRow(id,Safe(item.Label),L("uiAvailableCount",item.Count)){Badge=L(item.IsInventory?"inventorySource":"uiMapSource"),BadgeColor=item.IsInventory?"ui.accent":"ui.text.secondary",Icon=item.ThingDef?.uiIcon};
            }).ToArray();
            var packetModels=packets.Skip(packetPage*100).Take(100).Select(packet=>
            {
                string id=RowId(packet.Id);packetRows[id]=packet.Id;
                string label=packet.Template==null?L("uiUnknownItem"):DefDatabase<ThingDef>.GetNamedSilentFail(packet.Template.DefName)?.label??packet.Template.DefName;
                return new HostRow(id,Safe(packet.SenderDisplayName)+" · "+Safe(label),L("uiPacketRemaining",packet.RemainingCount,packet.TotalCount,packet.RemainingPackets,packet.TotalPackets)+"\n"+packet.CreatedAtUtc.ToLocalTime().ToString("HH:mm",CultureInfo.InvariantCulture))
                    {Badge=Status(packet),BadgeColor=state.IsPendingClaim(packet.Id)||packet.StateRequired&&!packet.StateReady?"ui.warning":packet.HasClaimed(tab.PresentationUuid)?"ui.success":packet.Expired||packet.ExpiresAtUtc<=DateTime.UtcNow||packet.RemainingCount<=0||packet.RemainingPackets<=0||RedPacketStateMachine.IsUnknownTemplate(packet.Template)||RedPacketStateMachine.IsMinifiedTemplate(packet.Template)?"ui.text.secondary":"ui.accent"};
            }).ToArray();
            long ticket=++revision,tv=host.ThemeVersion,pv=host.PointVersion;int bv=state.BadgeVersion,pc=state.PendingInventorySendCount;
            object lang=RedPacketLocalization.LanguageToken;Game expectedGame=Current.Game;string uuid=tab.PresentationUuid;bool online=tab.PresentationOnline;var expectedItem=selectedItem;int available=selectedItem?.Count??0;
            Func<bool> current=()=>!disposed&&view!=null&&!host.Closed(view)&&!dirty&&ticket==revision&&(!online||expectedGame==null||!tab.PresentationItemsDirty)&&ReferenceEquals(expectedGame,Current.Game)&&uuid==tab.PresentationUuid&&online==tab.PresentationOnline&&tv==host.ThemeVersion&&pv==host.PointVersion&&bv==state.BadgeVersion&&pc==state.PendingInventorySendCount&&ReferenceEquals(lang,RedPacketLocalization.LanguageToken)&&ReferenceEquals(expectedItem,selectedItem)&&(selectedItem==null||tab.PresentationHasItem(selectedItem)&&selectedItem.Count==available)&&pickedState==PacketState(Picked());
            if(!RedPacketPresentationRules.Packets(packetCount,selectedItem?.Selected??0,out int portions))portions=0;
            if(!RedPacketPresentationRules.Quantity(quantity,available,out int draftCount))draftCount=0;
            var context=new RedPacketPresentationActionContext(MainId,ticket,selectedPacketId,selectedItemRow,available,draftCount,portions,type.ToString(),online,picked!=null&&state.IsPendingClaim(picked.Id),pc,"main",SendBlock()==null,picked!=null&&ClaimBlock(picked)==null);
            var actions=Actions().ToArray();using(var batch=host.Collect(context,current,actions))
            {
                Func<HostBinding,bool> own=a=>a.OwnerId==host.OwnerId;
                var sections=new List<object>{
                    host.Section("browse",L("sendTitle"),L("uiPage",itemPage+1,itemPages),batch.Bindings.Where(a=>own(a)&&(a.LocalId=="refresh"||a.LocalId.StartsWith("items."))),new[]{host.Field("search",L("searchLabel"),search)}),
                    host.FieldColumns(host.Section("draft",selectedItem==null?L("uiChooseItem"):Safe(selectedItem.Label),"",batch.Bindings.Where(a=>own(a)&&new[]{"normal","lucky","all","half","none","send"}.Contains(a.LocalId)),new[]{host.Field("quantity",L("uiQuantity"),quantity,true,selectedItem!=null&&!RedPacketPresentationRules.Quantity(quantity,available,out _)?L("uiQuantityInvalid",available):null,selectedItem==null?L("uiChooseItem"):null),host.Field("packet-count",L("packetCountLabel"),packetCount,true,(selectedItem?.Selected??0)>0&&!RedPacketPresentationRules.Packets(packetCount,selectedItem.Selected,out _)?L("uiPacketCountInvalid",Math.Min(selectedItem.Selected,RedPacketLimits.MaxClaimDetailsPerPacket)):null,selectedItem==null?L("uiChooseItem"):null)},status:notice+(pc>0?"\n"+L("inventorySendPending",pc):""),tone:pc>0?"Pending":"Neutral",plain:true,surfaceKey:"redpacket.panel")),
                    host.Section("available","","",list:host.List("items",items,L(tab.PresentationItemsLoading?"uiItemsGrouping":"uiNoItems"),selectedItemRow)),
                    host.Section("packet-tools",L("listTitle"),picked==null?L("uiChoosePacket"):Safe(picked.SenderDisplayName)+" · "+Status(picked),batch.Bindings.Where(a=>!own(a)||new[]{"claim","detail","settings"}.Contains(a.LocalId)||a.LocalId.StartsWith("packets.")),status:claimNotice),
                    host.Section("received","",L("uiClaimAuthorityNote"),list:host.List("packets",packetModels,L("uiNoPackets"),picked==null?null:RowId(picked.Id)))
                };
                var draft=sections[1];sections.RemoveAt(1);sections.Insert(2,draft);
                dirty=false;try {host.Publish(view,MainId,L("tab"),ticket,sections,batch,current,3,L("sendTitle"),L("listTitle"),1);}catch{dirty=true;throw;}
            }
            shownTimeState=timeState;second=tick;badgeVersion=bv;sendCount=pc;themeVersion=tv;pointVersion=pv;language=lang;game=expectedGame;shownUuid=uuid;shownOnline=online;shownPacketState=pickedState;result=host.LastResult(view);
        }
        internal void DrawSettingsLauncher(Rect rect){if(!disposed&&host.Button(rect,L("uiSettings")))OpenSettings();}
        private void OpenSettings()
        {
            var window=NewWindow("settings",L("uiSettings"),null);
            if(window==null)return;
            Func<string> signature=()=>settings.EnableNotifications+"|"+settings.EnableChatAnnouncement+"|"+settings.SuppressUnknownPacketNotification;
            window.Configure(()=>
            {
                string stamp=signature();
                var actions=new[]{Setting("notifications","settingNotifications","settingNotificationsDesc",RedPacketSettings.KeyNotifications,settings.EnableNotifications),Setting("announcement","settingChatAnnouncement","settingChatAnnouncementDesc",RedPacketSettings.KeyChatAnnouncement,settings.EnableChatAnnouncement),Setting("unknown","settingSuppressUnknownNotify","settingSuppressUnknownNotifyDesc",RedPacketSettings.KeySuppressUnknown,settings.SuppressUnknownPacketNotification)};
                return new WindowProjection{Actions=actions,Current=()=>signature()==stamp,Build=batch=>new[]{host.Section("notifications",L("settingNotifications"),L("settingNotificationsDesc"),batch.Bindings.Where(b=>b.OwnerId==host.OwnerId&&b.LocalId=="notifications")),host.Section("announcement",L("settingChatAnnouncement"),L("settingChatAnnouncementDesc"),batch.Bindings.Where(b=>b.OwnerId==host.OwnerId&&b.LocalId=="announcement")),host.Section("unknown",L("settingSuppressUnknownNotify"),L("settingSuppressUnknownNotifyDesc"),batch.Bindings.Where(b=>b.OwnerId==host.OwnerId&&b.LocalId=="unknown")),host.Section("contributions","",L("uiSettingsNote"),batch.Bindings.Where(b=>b.OwnerId!=host.OwnerId))}};
            });
            Show(window);
        }
        private HostAction Setting(string id,string labelKey,string helpKey,string key,bool value)
        {return new HostAction(id,(value?L("uiOn"):L("uiOff"))+" · "+L(labelKey),()=>settings.Save(settingsContext,key,!value)){Selected=value,ConfirmText=L(helpKey)};}
        private void OpenDetail(string packetId)
        {
            if(!state.TryGetPacketDetailSnapshot(packetId,out var snapshot)){claimNotice=L("detailUnavailable");dirty=true;return;}
            var window=NewWindow("detail",L("detailSenderTitle",Safe(snapshot.SenderName)),packetId);
            if(window==null)return;
            window.Configure(()=>
            {
                var def=DefDatabase<ThingDef>.GetNamedSilentFail(snapshot.ItemDefName);
                var actions=def==null?Array.Empty<HostAction>():new[]{new HostAction("item-info",L("uiItemInfo"),()=>Find.WindowStack.Add(new Dialog_InfoCard(def)))};
                var claims=snapshot.Claims.Select((claim,index)=>new HostRow("claim."+index,Safe(claim.Name),L("detailClaimAmount",claim.Amount)){Badge=snapshot.IsFullyClaimed&&claim.Uuid==snapshot.LuckiestUuid?L("detailBestTag"):null,BadgeColor="redpacket.accent"}).ToArray();
                return new WindowProjection{Actions=actions,Current=()=>true,Build=batch=>new[]{host.Section("overview",L("detailSenderTitle",Safe(snapshot.SenderName)),Safe(snapshot.ItemLabel)+"\n"+L("detailPacketAmountLine",snapshot.RemainingPackets,snapshot.TotalPackets)+"\n"+L("detailRemainingItemLine",snapshot.RemainingCount,snapshot.TotalCount),batch.Bindings,status:L("uiDetailSnapshotNote"),plain:false,surfaceKey:"redpacket.panel"),host.Section("claims",L("uiClaims"),"",list:host.List("claims",claims,L("detailNoClaims"),readOnly:true))}};
            });
            Show(window);
        }
        private RedPacketHostWindow NewWindow(string kind,string title,string packet)
        {
            if(disposed||windows.Count>=8)return null;
            return new RedPacketHostWindow(host,kind+"."+Guid.NewGuid().ToString("N"),title,packet,()=>tab.PresentationUuid,()=>tab.PresentationOnline,()=>{dirty=true;},closed=>windows.Remove(closed),log);
        }
        private void Show(RedPacketHostWindow window){windows.Add(window);try{Find.WindowStack.Add(window);}catch{window.Dispose();throw;}}
        public void Dispose(){if(disposed)return;disposed=true;var errors=new List<Exception>();try{Close();}catch(Exception e){errors.Add(e);}foreach(var window in windows.ToArray())try{window.Close();}catch(Exception e){errors.Add(e);}try{host.Dispose();}catch(Exception e){errors.Add(e);}windows.Clear();if(errors.Count>0)throw new AggregateException(errors);}
    }
    internal sealed class WindowProjection
    {internal HostAction[] Actions;internal Func<bool> Current;internal Func<HostBatch,IEnumerable<object>> Build;}
    internal sealed class RedPacketHostWindow : Window,IDisposable
    {
        private readonly RedPacketHostPresentation host;private readonly string id,title,packet;private readonly Func<string> uuid;private readonly Func<bool> online;private readonly Action changed;private readonly Action<RedPacketHostWindow> removed;private readonly Action<string,LogLevel> log;
        private object view,result,language;private Func<WindowProjection> build;private long revision,tv=-1,pv=-1;private bool closed,dirty=true;private Func<bool> shownCurrent;
        private Game game;private string owner;
        internal RedPacketHostWindow(RedPacketHostPresentation host,string id,string title,string packet,Func<string> uuid,Func<bool> online,Action changed,Action<RedPacketHostWindow> removed,Action<string,LogLevel> log)
        {this.host=host;this.id=id;this.title=title;this.packet=packet;this.uuid=uuid;this.online=online;this.changed=changed;this.removed=removed;this.log=log;doCloseX=true;closeOnAccept=false;closeOnCancel=true;resizeable=true;draggable=true;}
        internal void Configure(Func<WindowProjection> build){this.build=build;}
        public override Vector2 InitialSize=>new Vector2(Mathf.Min(640,UiScreenSafeArea.Current.width),Mathf.Min(620,UiScreenSafeArea.Current.height));
        public override void PreOpen(){base.PreOpen();game=Current.Game;owner=uuid();try{view=host.Open(id);Refresh();}catch{Dispose();throw;}}
        public override void WindowUpdate()
        {base.WindowUpdate();windowRect=UiScreenSafeArea.ClampWindow(windowRect,new Vector2(320,280));if(closed||view==null||host.Closed(view)||!ReferenceEquals(game,Current.Game)||owner!=uuid()){Close();return;}try{Refresh();}catch(Exception e){log?.Invoke("Red packet view failed: "+e,LogLevel.ERROR);Close();}}
        public override void DoWindowContents(Rect rect){if(!closed&&view!=null)host.Draw(view,rect);}
        private void Refresh()
        {
            if(!dirty&&shownCurrent?.Invoke()==true&&tv==host.ThemeVersion&&pv==host.PointVersion&&ReferenceEquals(language,RedPacketLocalization.LanguageToken)&&ReferenceEquals(result,host.LastResult(view)))return;
            var projection=build();
            foreach(var a in projection.Actions){Action execute=a.Execute;a.Execute=()=>{execute();dirty=true;changed();};}
            host.Sources[id]=_=>projection.Actions;
            long ticket=++revision,theme=host.ThemeVersion,points=host.PointVersion;object lang=RedPacketLocalization.LanguageToken;bool expectedOnline=online();
            Func<bool> current=()=>!closed&&!dirty&&ticket==revision&&view!=null&&!host.Closed(view)&&ReferenceEquals(game,Current.Game)&&owner==uuid()&&expectedOnline==online()&&theme==host.ThemeVersion&&points==host.PointVersion&&ReferenceEquals(lang,RedPacketLocalization.LanguageToken)&&projection.Current();
            var context=new RedPacketPresentationActionContext(id,ticket,packet,null,0,0,0,null,expectedOnline,false,0,packet==null?"settings":"detail-snapshot");
            using(var batch=host.Collect(context,current,projection.Actions))
            {var sections=projection.Build(batch);dirty=false;try{host.Publish(view,id,title,ticket,sections,batch,current);}catch{dirty=true;throw;}}
            tv=theme;pv=points;language=lang;result=host.LastResult(view);shownCurrent=projection.Current;
        }
        public override void PostClose(){try{Dispose();}finally{base.PostClose();}}
        public void Dispose(){if(closed)return;closed=true;host.Sources.Remove(id);var old=view;view=null;try{(old as IDisposable)?.Dispose();}finally{removed(this);}}
    }
}
