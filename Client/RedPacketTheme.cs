using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;
using Utils.Framework;

namespace Phinix.LegacyRedPacketExtension.Client
{
    // Presentation consumes one palette on both supported hosts; no version checks here.
    internal static class RedPacketTheme
    {
        private static readonly PluginColor[] definitions = {
            new PluginColor("redpacket.panel", new Color(.12f,.12f,.12f,.16f), "Panel / 面板背景", "ui.surface"),
            new PluginColor("redpacket.highlight", new Color(1,1,1,.1f), "Highlight / 悬停强调", "ui.hover"),
            new PluginColor("redpacket.accent", new Color(.95f,.72f,.3f), "Accent / 强调色", "ui.accent")
        };
        private static Dictionary<string, Color> Defaults()
        {
            var result = new Dictionary<string, Color>(StringComparer.Ordinal) {
                { "ui.text.primary", Color.white }, { "ui.text.secondary", new Color(.65f,.65f,.65f) },
                { "ui.surface", new Color(.12f,.12f,.12f,.16f) }, { "ui.separator", new Color(1,1,1,.15f) },
                { "ui.hover", new Color(1,1,1,.1f) }, { "ui.accent", new Color(.95f,.72f,.3f) },
                { "ui.warning", new Color(1,.84f,.2f) }, { "ui.error", new Color(.9f,.3f,.3f) },
                { "ui.button.background", new Color(.2f,.18f,.14f,1) }, { "ui.button.hover", new Color(1,1,1,.1f) },
                { "ui.button.pressed", new Color(.4f,.3f,.16f,1) },
                { "ui.control.disabled", new Color(.15f,.15f,.15f,.8f) },
                { "ui.input.background", Color.white }, { "ui.input.focus", new Color(.95f,.72f,.3f) }
            };
            foreach (var definition in definitions) result[definition.Key] = definition.Default;
            return result;
        }
        private static readonly PluginPalette fallback = new PluginPalette(Defaults());
        private static IPluginThemeSource source;
        private static PluginPalette drawing;
        internal static PluginPalette Current => drawing ?? source?.Capture() ?? fallback;
        internal static void Start(ExtensionHostContext host, IPhinixExtensionModule module)
        { Stop(); source = PluginThemeAdapter.Create(host,module,"redpacket","红包 / Red packets",definitions,Defaults()); }
        internal static void Stop() { var owned=source; source=null; owned?.Dispose(); }
        internal static Scope BeginDraw() => new Scope(true);
        internal struct Scope : IDisposable
        {
            private readonly PluginPalette previous;
            private readonly Color color, background, content;
            private readonly bool enabled, wrap;
            private readonly GameFont font;
            private readonly TextAnchor anchor;
            internal Scope(bool capture)
            {
                previous=drawing; drawing=Current;
                color=GUI.color; background=GUI.backgroundColor; content=GUI.contentColor;
                enabled=GUI.enabled; wrap=Text.WordWrap; font=Text.Font; anchor=Text.Anchor;
                GUI.color=drawing["ui.text.primary"];
            }
            public void Dispose()
            {
                drawing=previous; GUI.color=color; GUI.backgroundColor=background; GUI.contentColor=content;
                GUI.enabled=enabled; Text.WordWrap=wrap; Text.Font=font; Text.Anchor=anchor;
            }
        }
        internal static void Surface(Rect rect)
        { using (BeginDraw()) { GUI.color=Color.white; Widgets.DrawBoxSolid(rect,Current["redpacket.panel"]); } }
        internal static void Highlight(Rect rect)
        { using (BeginDraw()) { GUI.color=Color.white; Widgets.DrawBoxSolid(rect,Current["redpacket.highlight"]); } }
        internal static bool Button(Rect rect, string text, bool active = true)
        {
            using (BeginDraw())
            {
                GUI.enabled=GUI.enabled && active; bool enabled=GUI.enabled;
                GUI.color=Color.white; Widgets.DrawBoxSolid(rect,Current["ui.button.background"]);
                if(!enabled || Mouse.IsOver(rect)) Widgets.DrawBoxSolid(rect,Current[!enabled?"ui.control.disabled":Input.GetMouseButton(0)?"ui.button.pressed":"ui.button.hover"]);
                GUI.color=Current[enabled?"ui.text.primary":"ui.text.secondary"];
                return Widgets.ButtonText(rect,text,drawBackground:false,active:enabled) && enabled;
            }
        }
        internal static string TextField(Rect rect, string value, int maxLength=int.MaxValue, Regex validator=null, string controlName=null)
        {
            using (BeginDraw())
            {
                if(controlName!=null) GUI.SetNextControlName(controlName);
                GUI.backgroundColor=Current["ui.input.background"];
                GUI.color=Current[GUI.enabled?"ui.text.primary":"ui.text.secondary"];
                string result=Widgets.TextField(rect,value,maxLength,validator);
                if(controlName!=null && GUI.enabled && GUI.GetNameOfFocusedControl()==controlName)
                { GUI.color=Current["ui.input.focus"]; Widgets.DrawBox(rect); }
                return result;
            }
        }
        internal static void Checkbox(Rect rect, string label, ref bool value)
        { using (BeginDraw()) Widgets.CheckboxLabeled(rect,label,ref value,disabled:!GUI.enabled); }
    }
}
