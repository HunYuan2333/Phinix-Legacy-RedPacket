namespace Phinix.LegacyRedPacketExtension
{
    // Read facts and ephemeral view identities only. No packet, Thing, relay, ledger or mutation capability.
    public sealed class RedPacketPresentationActionContext
    {
        public const string PointId="builtin.legacy-redpacket.presentation.actions";
        public RedPacketPresentationActionContext(string viewId,long revision,string packetId,string itemRowId,int availableCount,int draftCount,int packetCount,string packetType,bool online,bool claimPending,int pendingInventorySends,string projectionKind="main",bool sendIntentReady=false,bool claimIntentReady=false)
        {ViewId=viewId;Revision=revision;PacketId=packetId;ItemRowId=itemRowId;AvailableCount=availableCount;DraftCount=draftCount;PacketCount=packetCount;PacketType=packetType;Online=online;ClaimPending=claimPending;PendingInventorySends=pendingInventorySends;ProjectionKind=projectionKind;SendIntentReady=sendIntentReady;ClaimIntentReady=claimIntentReady;}
        // Owner UI preflight only; these facts do not grant business authority or confirm a result.
        public bool SendIntentReady {get;} public bool ClaimIntentReady {get;}
        public string ProjectionKind {get;}
        public string ViewId {get;} public long Revision {get;} public string PacketId {get;} public string ItemRowId {get;}
        public int AvailableCount {get;} public int DraftCount {get;} public int PacketCount {get;} public string PacketType {get;}
        public bool Online {get;} public bool ClaimPending {get;} public int PendingInventorySends {get;}
    }
}
