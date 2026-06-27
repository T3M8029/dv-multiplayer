using DV.Logic.Job;

namespace Multiplayer.Networking.Packets.Clientbound.Jobs;

internal class ClientboundTaskUpdatePacket
{
    public ushort JobNetId { get; set; }
    public ushort TaskNetId { get; set; }
    public TaskState NewState { get; set; }
    public float TaskStartTime { get; set; }
    public float TaskFinishTime { get; set; }
    public bool TaskStateUpdate { get; set; }
    public ushort CarNetID { get; set; }
    public bool ReplaceCar { get; set; }
    public ushort DestTrackId { get; set; }
    public bool ReplaceDestTrack { get; set; }
}
