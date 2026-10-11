namespace Phinix.LegacyRedPacketExtension.Client
{
    internal interface IRedPacketAnnouncementSender
    {
        bool TrySend(string text);
    }
}
