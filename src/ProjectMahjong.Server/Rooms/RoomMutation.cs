namespace ProjectMahjong.Server.Rooms;

public sealed record RoomMutation(bool Accepted, string? Error, long Revision, int? ClaimTileId = null)
{
    public static RoomMutation Success(long revision, int? claimTileId = null) =>
        new(true, null, revision, claimTileId);

    public static RoomMutation Reject(string error, long revision) =>
        new(false, error, revision);
}

public sealed record SnapshotDelivery(string ConnectionId, ProjectMahjong.Contracts.RoomSnapshotDto Snapshot);

