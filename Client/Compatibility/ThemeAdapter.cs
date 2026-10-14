using System;
using System.Collections.Generic;
using System.Reflection;
using PhinixClient.Framework;
using UnityEngine;
using Utils.Framework;

namespace Phinix.LegacyRedPacketExtension.Client
{
    internal interface IPluginThemeSource : IDisposable
    {
        PluginPalette Capture();
    }
    internal sealed class PluginPalette
    {
        private readonly Dictionary<string, Color> colors;
        internal PluginPalette(Dictionary<string, Color> colors) { this.colors = new Dictionary<string, Color>(colors, StringComparer.Ordinal); }
        internal Color this[string key] => colors[key];
    }
    internal sealed class PluginColor
    {
        internal readonly string Key, Label, Fallback;
        internal readonly Color Default;
        internal PluginColor(string key, Color value, string label, string fallback)
        { Key = key; Default = value; Label = label; Fallback = fallback; }
    }

    // All runtime type binding stays here so the binary can reference the 1.9 SDK.
    internal static class PluginThemeAdapter
    {
        internal static IPluginThemeSource Create(ExtensionHostContext host, IPhinixExtensionModule owner,
            string keyNamespace, string ownerLabel, PluginColor[] definitions, Dictionary<string, Color> defaults)
        {
            var sdk = typeof(IClientTheme).Assembly;
            Type serviceType = sdk.GetType("PhinixClient.Framework.IClientTheme", false);
            Type registryType = sdk.GetType("PhinixClient.Framework.IClientThemeRegistry", false);
            Type readerType = sdk.GetType("PhinixClient.Framework.IClientThemeReader", false);
            if (serviceType == null && registryType == null && readerType == null)
            {
                var compatibility = typeof(IClientSessionContext).Assembly.GetType("PhinixClient.Framework.ClientAbstractionsCompatibility", true);
                string version = (string)compatibility.GetField("Version").GetRawConstantValue();
                if (new Version(version).Major >= 2) throw new InvalidOperationException("Current theme API is missing.");
                return new DefaultSource(defaults);
            }
            if (serviceType == null || registryType == null || readerType == null)
                throw new InvalidOperationException("Incomplete theme API.");
            object service = Resolve(host, serviceType), registry = Resolve(host, registryType);
            if (service == null || registry == null) return new DefaultSource(defaults); // Optional capability unavailable.
            var register = registryType.GetMethod("RegisterModule", new[] { typeof(IPhinixExtensionModule), typeof(string), typeof(string) });
            var capture = serviceType.GetMethod("Capture", Type.EmptyTypes);
            var versionProperty = readerType.GetProperty("Version");
            var rgba = sdk.GetType("PhinixClient.Framework.ClientRgba", true);
            var read = readerType.GetMethod("TryGetColor", new[] { typeof(string), rgba.MakeByRefType() });
            var convert = typeof(ClientColorValues).GetMethod("ToUnityColor", new[] { rgba });
            if (register == null || capture == null || versionProperty == null || read == null || convert == null)
                throw new InvalidOperationException("Unsupported theme API shape.");
            object registration = Invoke(register, registry, owner, keyNamespace, ownerLabel);
            var lease = registration as IDisposable;
            if (lease == null) throw new InvalidOperationException("Theme registration has no lifetime.");
            try
            {
                Type registrationType = sdk.GetType("PhinixClient.Framework.IClientThemeRegistration", true);
                var rgbaConstructor = rgba.GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) });
                var color = registrationType.GetMethod("RegisterColor", new[] { typeof(string), rgba, typeof(string), typeof(string), typeof(string), typeof(string) });
                var commit = registrationType.GetMethod("Commit", Type.EmptyTypes);
                if (rgbaConstructor == null || color == null || commit == null) throw new InvalidOperationException("Unsupported theme registration shape.");
                foreach (var definition in definitions)
                    Invoke(color, registration, definition.Key, rgbaConstructor.Invoke(new object[] { definition.Default.r, definition.Default.g, definition.Default.b, definition.Default.a }), definition.Label, definition.Fallback, ownerLabel, definition.Label);
                Invoke(commit, registration);
                var result = new CurrentSource(service, capture, versionProperty, read, convert, lease, defaults);
                result.Capture(); // Check the complete palette before activation returns.
                return result;
            }
            catch { lease.Dispose(); throw; }
        }
        private static object Resolve(ExtensionHostContext host, Type type)
        {
            object[] arguments = { null };
            var method = typeof(ExtensionHostContext).GetMethod("TryGetService").MakeGenericMethod(type);
            return (bool)Invoke(method, host, arguments) ? arguments[0] : null;
        }
        private static object Invoke(MethodInfo method, object target, params object[] arguments)
        {
            try { return method.Invoke(target, arguments); }
            catch (TargetInvocationException error)
            { throw new InvalidOperationException("Theme adapter invocation failed.", error.InnerException ?? error); }
        }
        private sealed class DefaultSource : IPluginThemeSource
        {
            private readonly PluginPalette palette;
            internal DefaultSource(Dictionary<string, Color> defaults) { palette = new PluginPalette(defaults); }
            public PluginPalette Capture() => palette;
            public void Dispose() { }
        }
        private sealed class CurrentSource : IPluginThemeSource
        {
            private object service;
            private readonly MethodInfo capture, read, convert;
            private readonly PropertyInfo version;
            private IDisposable lease;
            private readonly Dictionary<string, Color> defaults;
            private long cachedVersion = long.MinValue;
            private PluginPalette palette;
            internal CurrentSource(object service, MethodInfo capture, PropertyInfo version, MethodInfo read, MethodInfo convert, IDisposable lease, Dictionary<string, Color> defaults)
            { this.service=service; this.capture=capture; this.version=version; this.read=read; this.convert=convert; this.lease=lease; this.defaults=new Dictionary<string, Color>(defaults); }
            public PluginPalette Capture()
            {
                if (service == null) throw new ObjectDisposedException("Plugin theme");
                object snapshot = Invoke(capture, service);
                long revision = (long)version.GetValue(snapshot, null);
                if (palette != null && cachedVersion == revision) return palette;
                var values = new Dictionary<string, Color>(defaults, StringComparer.Ordinal);
                foreach (string key in defaults.Keys)
                {
                    object[] arguments = { key, null };
                    if ((bool)Invoke(read, snapshot, arguments)) values[key] = (Color)Invoke(convert, null, arguments[1]);
                }
                palette = new PluginPalette(values); cachedVersion = revision;
                return palette;
            }
            public void Dispose()
            { var owned=lease; lease=null; service=null; palette=null; owned?.Dispose(); }
        }
    }
}
