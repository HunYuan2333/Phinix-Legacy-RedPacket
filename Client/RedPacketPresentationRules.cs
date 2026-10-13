using System;
using System.Globalization;
namespace Phinix.LegacyRedPacketExtension.Client
{
    internal static class RedPacketPresentationRules
    {
        internal static bool Quantity(string value,int maximum,out int parsed)
        {return int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out parsed)&&parsed>0&&parsed<=maximum;}
        internal static bool Packets(string value,int selected,out int parsed)=>Quantity(value,Math.Min(selected,RedPacketLimits.MaxClaimDetailsPerPacket),out parsed);
        internal static bool Claimable(RedPacket packet,string viewer,bool online,bool pending,DateTime now)
        {return online&&!string.IsNullOrEmpty(viewer)&&packet!=null&&!packet.Expired&&packet.ExpiresAtUtc>now&&packet.RemainingCount>0&&packet.RemainingPackets>0&&!packet.IsSender(viewer)&&!packet.HasClaimed(viewer)&&!pending&&(!packet.StateRequired||packet.StateReady);}
    }
}
