using System;
using System.Reflection;
using PhinixClient.Framework;
using Phinix.InventoryExtension;
using Utils;
using Utils.Framework;
using Verse;

namespace Phinix.LegacyRedPacketExtension.Client
{
    // Temporary client rollout boundary. Business/UI only sees IRedPacketAnnouncementSender.
    internal static class RedPacketClientAdapters
    {
        internal static IRedPacketAnnouncementSender CreateAnnouncement(ExtensionHostContext host)
        {
            // Read the runtime constant via reflection: compiling against 1.9 must not inline its value.
            var versionField = typeof(IClientSessionContext).Assembly.GetType("PhinixClient.Framework.ClientAbstractionsCompatibility", true).GetField("Version");
            bool currentClient = new Version((string)versionField.GetRawConstantValue()).Major >= 2;
            if (!currentClient)
                return host.TryGetService<IFrameworkClientTransport>(out var previous) ? new PreviousAnnouncement(previous) : null;
            Type senderType = null, intentType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "ChatExtension") continue;
                senderType = assembly.GetType("Phinix.ChatExtension.Client.IChatMessageSender", false);
                intentType = assembly.GetType("Phinix.ChatExtension.Client.ChatMessageIntent", false);
                break;
            }
            if (senderType != null)
            {
                if (intentType == null) throw new InvalidOperationException("Incomplete current chat API.");
                object[] arguments = { null };
                var resolve = typeof(ExtensionHostContext).GetMethod("TryResolveApi").MakeGenericMethod(senderType);
                if (!(bool)resolve.Invoke(host, arguments)) return null; // Current Chat is disabled: no old-route fallback.
                var constructor = intentType.GetConstructor(new[] { typeof(string), typeof(System.Collections.Generic.IEnumerable<string>), typeof(string), typeof(string) });
                var send = senderType.GetMethod("TrySend", new[] { intentType });
                if (constructor == null || send == null || send.ReturnType != typeof(bool)) throw new InvalidOperationException("Unsupported current chat API shape.");
                return new CurrentAnnouncement(arguments[0], constructor, send);
            }
            // A missing or disabled current Chat must not route through an old sender.
            return null;
        }
        private sealed class CurrentAnnouncement : IRedPacketAnnouncementSender
        {
            private readonly object sender;
            private readonly ConstructorInfo intent;
            private readonly MethodInfo send;
            internal CurrentAnnouncement(object sender, ConstructorInfo intent, MethodInfo send)
            { this.sender = sender; this.intent = intent; this.send = send; }
            public bool TrySend(string text)
            {
                try { return (bool)send.Invoke(sender, new[] { intent.Invoke(new object[] { text, null, null, null }) }); }
                catch (TargetInvocationException error) { throw new InvalidOperationException("Chat announcement failed.", error.InnerException ?? error); }
            }
        }
        private sealed class PreviousAnnouncement : IRedPacketAnnouncementSender
        {
            private readonly IFrameworkClientTransport transport;
            internal PreviousAnnouncement(IFrameworkClientTransport transport) { this.transport = transport; }
            public bool TrySend(string text) => transport.TryHandleOutgoingMessage(text);
        }
    }

    internal partial class RedPacketInventoryCodec
    {
        // The previous base interface calls this; the current host calls DeliverVerified directly.
        public void Deliver(Thing thing, Map map, IntVec3 dropSpot)
        {
            var result = DeliverVerified(thing, map, dropSpot);
            if (result == null || result.Status != InventoryDeliveryStatus.Delivered)
                throw new InvalidOperationException(result?.Reason ?? "Delivery status is uncertain.");
        }
    }
}
