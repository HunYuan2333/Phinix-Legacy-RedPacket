using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Utils.Framework;
namespace Phinix.LegacyRedPacketExtension.Client
{
    // The single-package 1.9 boundary. All NEW SDK type names, binding and model conversion live here.
    // These are public Host models/actions/sessions, not a renderer or a private action registry.
    internal static class RedPacketPresentationAdapter
    {
        internal static IMainTabProvider Wrap(RedPacketTab tab)
        {
            Type wrapper=typeof(IClientSessionContext).Assembly.GetType("PhinixClient.Framework.ClientPresentationMainTab",false);
            if(wrapper==null){if(typeof(IClientSessionContext).Assembly.GetType("PhinixClient.Framework.IClientPresentationService",false)!=null)throw new InvalidOperationException("CurrentPresentationProviderAdapterMissing");return tab;}
            return (IMainTabProvider)Activator.CreateInstance(wrapper,new object[]{tab,(Action)tab.ViewOpened,(Action)tab.ViewUpdate,(Action)tab.ViewClosed,"redpackets"});
        }
        internal static RedPacketHostPresentation Create(ExtensionHostContext host,IPhinixExtensionModule owner)
        {
            var sdk=typeof(IClientSessionContext).Assembly;
            Type renderer=sdk.GetType("PhinixClient.Framework.IClientPresentationService",false);
            if(renderer==null)
            {
                string version=(string)sdk.GetType("PhinixClient.Framework.ClientAbstractionsCompatibility",true).GetField("Version").GetRawConstantValue();
                if(new Version(version).Major>=2)throw new InvalidOperationException("CurrentPresentationApiMissing");
                return null;
            }
            return new RedPacketHostPresentation(host,owner,sdk);
        }
    }
    internal sealed class HostAction
    {
        internal string Id,Label,Disabled,Kind="Normal";internal Action Execute;internal bool Selected;
        internal string ConfirmTitle,ConfirmText,ConfirmAccept,ConfirmCancel;
        internal HostAction(string id,string label,Action execute,string disabled=null){Id=id;Label=label;Execute=execute;Disabled=disabled;}
    }
    internal sealed class HostRow
    {
        internal string Id,Label,Detail,Disabled,Color;
        internal string Badge,BadgeColor;internal Texture2D Icon;
        internal HostRow(string id,string label,string detail=null){Id=id;Label=label;Detail=detail;}
    }
    internal sealed class HostInput {internal long Revision;internal string Id,Kind,Value;}
    internal sealed class HostBinding {internal string Id,OwnerId,LocalId;internal object Model;}
    internal sealed class HostBatch : IDisposable
    {
        internal object Owned;internal List<HostBinding> Bindings;
        public void Dispose(){var old=Owned;Owned=null;(old as IDisposable)?.Dispose();}
    }
    internal sealed class RedPacketHostPresentation : IDisposable
    {
        private readonly Assembly sdk;
        private readonly IPhinixExtensionModule owner;
        private readonly object renderer,points,scope,point,theme;
        private readonly Type scopeType,sessionType,pointType;
        internal readonly Dictionary<string,Func<RedPacketPresentationActionContext,IEnumerable<HostAction>>> Sources=new Dictionary<string,Func<RedPacketPresentationActionContext,IEnumerable<HostAction>>>();
        private bool disposed;
        internal string OwnerId=>owner.ExtensionId;
        internal long PointVersion=>(long)Get(points,"Revision");
        internal long ThemeVersion=>(long)Get(theme,"Version");
        internal RedPacketHostPresentation(ExtensionHostContext host,IPhinixExtensionModule owner,Assembly sdk)
        {
            this.owner=owner;this.sdk=sdk;
            renderer=Resolve(host,Type("IClientPresentationService"));points=Resolve(host,Type("IClientExtensionPointService"));theme=Resolve(host,Type("IClientTheme"));
            scopeType=Type("IClientExtensionPointScope");sessionType=Type("IClientPresentationSession");pointType=Type("IClientActionPoint`1").MakeGenericType(typeof(RedPacketPresentationActionContext));
            scope=Call(Type("IClientExtensionPointService").GetMethod("ForModule"),points,owner);
            try
            {
                point=Call(scopeType.GetMethod("DefineActions").MakeGenericMethod(typeof(RedPacketPresentationActionContext)),scope,RedPacketPresentationActionContext.PointId);
                var factory=GetType().GetMethod("Definitions",BindingFlags.Instance|BindingFlags.NonPublic);
                var context=Expression.Parameter(typeof(RedPacketPresentationActionContext),"context");
                var returnType=typeof(IEnumerable<>).MakeGenericType(Type("ClientActionDefinition"));
                var delegateType=typeof(Func<,>).MakeGenericType(typeof(RedPacketPresentationActionContext),returnType);
                var callback=Expression.Lambda(delegateType,Expression.Convert(Expression.Call(Expression.Constant(this),factory,context),returnType),context).Compile();
                Call(scopeType.GetMethod("ContributeActions").MakeGenericMethod(typeof(RedPacketPresentationActionContext)),scope,RedPacketPresentationActionContext.PointId,callback,0f);
            }
            catch {(scope as IDisposable)?.Dispose();throw;}
        }
        private object Definitions(RedPacketPresentationActionContext context)
        {
            var actions=!disposed&&Sources.TryGetValue(context.ViewId,out var source)?source(context):Enumerable.Empty<HostAction>();
            return ArrayOf("ClientActionDefinition",actions.Select(a=>New("ClientActionDefinition",new Dictionary<string,object>{{"id",a.Id},{"label",a.Label},{"execute",a.Execute},{"disabledReason",a.Disabled}})));
        }
        internal bool Button(Rect rect,string label)
        {
            object palette=Call(Type("IClientTheme").GetMethod("Capture"),theme);
            var method=Type("ClientWidgets").GetMethods().Single(m=>m.Name=="Button");
            var values=new Dictionary<string,object>{{"rect",rect},{"text",label},{"theme",palette}};
            return (bool)Call(method,null,method.GetParameters().Select(p=>values.TryGetValue(p.Name,out object value)?value:p.IsOptional?p.DefaultValue:throw new InvalidOperationException("UnsupportedPublicButtonShape")).ToArray());
        }
        internal object Open(string pageId)=>Call(Type("IClientPresentationService").GetMethod("Open"),renderer,owner,pageId);
        internal bool Closed(object view)=>view==null||(bool)Get(view,"IsClosed");
        internal object LastResult(object view)=>Get(view,"LastActionResult");
        internal void Draw(object view,Rect rect)=>Call(sessionType.GetMethod("Draw"),view,rect);
        internal bool Input(object view,out HostInput input)
        {
            object[] args={null};bool read=(bool)Call(sessionType.GetMethod("TryDequeueInput"),view,args);
            input=read?new HostInput{Revision=(long)Get(args[0],"Revision"),Id=(string)Get(args[0],"Id"),Kind=Get(args[0],"Kind").ToString(),Value=(string)Get(args[0],"Value")}:null;return read;
        }
        internal HostBatch Collect(RedPacketPresentationActionContext context,Func<bool> current,IEnumerable<HostAction> official)
        {
            var policies=official.ToDictionary(a=>a.Id,StringComparer.Ordinal);
            object owned=Call(pointType.GetMethod("Collect"),point,context,current);
            try
            {
                var bindings=new List<HostBinding>();
                foreach(object action in (IEnumerable)Get(owned,"Actions"))
                {
                    string id=(string)Get(action,"Id"),ownerId=(string)Get(action,"OwnerId");HostAction policy=null;
                    if(ownerId==OwnerId)policies.TryGetValue(id,out policy);
                    object confirmation=policy?.ConfirmTitle==null?null:New("ClientPresentationConfirmation",new Dictionary<string,object>{{"title",policy.ConfirmTitle},{"text",policy.ConfirmText},{"acceptLabel",policy.ConfirmAccept},{"cancelLabel",policy.ConfirmCancel}});
                    string fullId=ownerId+"."+id;
                    object binding=New("ClientPresentationAction",new Dictionary<string,object>{{"id",fullId},{"action",action},{"kind",Enum.Parse(Type("ClientButtonKind"),policy?.Kind??"Normal")},{"confirmation",confirmation}});
                    if(policy?.Selected==true)binding=Call(binding.GetType().GetMethod("WithSelection"),binding,true);
                    bindings.Add(new HostBinding{Id=fullId,OwnerId=ownerId,LocalId=id,Model=binding});
                }
                return new HostBatch{Owned=owned,Bindings=bindings};
            }
            catch {(owned as IDisposable)?.Dispose();throw;}
        }
        internal object Field(string id,string label,string value,bool integer=false,string error=null,string disabled=null)
        {
            if(integer)return Call(Type("ClientPresentationTextField").GetMethod("Integer"),null,id,label,value,error,disabled);
            return New("ClientPresentationTextField",new Dictionary<string,object>{{"id",id},{"label",label},{"value",value},{"maxLength",256},{"validationText",error},{"disabledReason",disabled}});
        }
        internal object List(string id,IEnumerable<HostRow> rows,string empty,string selected=null,bool readOnly=false)
        {
            var models=rows.Select(r=>
            {
                object row=New("ClientPresentationRow",new Dictionary<string,object>{{"id",r.Id},{"label",r.Label},{"detail",r.Detail},{"disabledReason",r.Disabled},{"colorKey",r.Color}});
                if(r.Badge!=null)
                {
                    object badge=New("ClientPresentationBadge",new Dictionary<string,object>{{"id","source"},{"label",r.Badge},{"icon",r.Icon}});
                    if(r.BadgeColor!=null)badge=Call(badge.GetType().GetMethod("WithColor"),badge,r.BadgeColor);
                    row=Call(row.GetType().GetMethod("WithBadges"),row,ArrayOf("ClientPresentationBadge",new[]{badge}));
                }
                return row;
            });
            object list=New("ClientPresentationList",new Dictionary<string,object>{{"id",id},{"rows",ArrayOf("ClientPresentationRow",models)},{"emptyText",empty},{"selectedId",selected}});
            list=Call(list.GetType().GetMethod("WithColors"),list,"redpacket.highlight","redpacket.accent");
            return readOnly?Call(list.GetType().GetMethod("WithReadOnly"),list):list;
        }
        internal object Section(string id,string title,string summary,IEnumerable<HostBinding> actions=null,IEnumerable<object> fields=null,object list=null,string status=null,string tone="Neutral",bool plain=true,string surfaceKey=null)
        {
            object state=status==null?null:New("ClientPresentationStatus",new Dictionary<string,object>{{"text",status},{"tone",Enum.Parse(Type("ClientPresentationTone"),tone)}});
            object section=New("ClientPresentationSection",new Dictionary<string,object>{{"id",id},{"title",title},{"summary",summary},{"actions",actions==null?null:ArrayOf("ClientPresentationAction",actions.Select(a=>a.Model))},{"fields",fields==null?null:ArrayOf("ClientPresentationTextField",fields)},{"list",list},{"status",state},{"surfaceKey",surfaceKey}});
            if(plain)section=Call(section.GetType().GetMethod("WithPlainLayout"),section);
            return Call(section.GetType().GetMethod("WithWrappedActions"),section);
        }
        internal object FieldColumns(object section)=>Call(section.GetType().GetMethod("WithFieldColumns"),section,2,180f);
        internal void Publish(object view,string id,string title,long revision,IEnumerable<object> sections,HostBatch batch,Func<bool> current,int primarySections=0,string primaryTitle=null,string secondaryTitle=null,int primaryFooterSections=0)
        {
            object page=New("ClientPresentationPage",new Dictionary<string,object>{{"id",id},{"title",title},{"revision",revision},{"sections",ArrayOf("ClientPresentationSection",sections)}});
            if(primarySections>0)
            {
                object split=New("ClientPresentationSplitView",new Dictionary<string,object>{{"primarySectionCount",primarySections},{"primaryTitle",primaryTitle},{"secondaryTitle",secondaryTitle},{"primaryFraction",.5f},{"minimumWidth",760f}});
                split=Call(split.GetType().GetMethod("WithFixedHeaders"),split,0,1,1,null);
                split=Call(split.GetType().GetMethod("WithFixedFooters"),split,primaryFooterSections,0);
                page=Call(page.GetType().GetMethod("WithSplitView"),page,split);
            }
            Call(sessionType.GetMethod("Publish"),view,page,batch?.Owned,current);
            if(batch!=null)batch.Owned=null; // successful transfer only
        }
        private Type Type(string name)=>sdk.GetType("PhinixClient.Framework."+name,true);
        private object New(string name,Dictionary<string,object> supplied)
        {
            var constructor=Type(name).GetConstructors().FirstOrDefault(c=>supplied.Keys.All(k=>c.GetParameters().Any(p=>p.Name==k))&&c.GetParameters().All(p=>supplied.ContainsKey(p.Name)||p.IsOptional));
            if(constructor==null)throw new InvalidOperationException("UnsupportedPublicPresentationShape: "+name);
            return constructor.Invoke(constructor.GetParameters().Select(p=>supplied.TryGetValue(p.Name,out object value)?value:p.DefaultValue).ToArray());
        }
        private Array ArrayOf(string name,IEnumerable<object> values){var list=values.ToArray();var array=Array.CreateInstance(Type(name),list.Length);for(int i=0;i<list.Length;i++)array.SetValue(list[i],i);return array;}
        private static object Resolve(ExtensionHostContext host,Type type)
        {object[] args={null};if(!(bool)Call(typeof(ExtensionHostContext).GetMethod("TryGetService").MakeGenericMethod(type),host,args))throw new InvalidOperationException("RequiredPresentationServiceUnavailable: "+type.Name);return args[0];}
        private static object Get(object value,string name)
        {
            var property=value.GetType().GetProperty(name)??value.GetType().GetInterfaces().Select(i=>i.GetProperty(name)).FirstOrDefault(p=>p!=null);
            if(property==null)throw new InvalidOperationException("UnsupportedPublicPresentationProperty: "+name);
            return property.GetValue(value,null);
        }
        private static object Call(MethodInfo method,object target,params object[] args)
        {if(method==null)throw new InvalidOperationException("UnsupportedPublicPresentationMethod");try{return method.Invoke(target,args);}catch(TargetInvocationException error){throw new InvalidOperationException("PresentationBindingFailed",error.InnerException??error);}}
        public void Dispose(){if(disposed)return;disposed=true;Sources.Clear();(scope as IDisposable)?.Dispose();}
    }
}
