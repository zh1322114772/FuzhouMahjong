namespace ProjectMahjong.Server.Rooms;

public sealed record RoomMutation(bool Accepted, string? Error, long Revision)
{
    public static RoomMutation Success(long revision) => new(true, null, revision);

    public static RoomMutation Reject(string error, long revision) =>
        new(false, error, revision);
}

public sealed record ActionDeadline(long Id, DateTimeOffset DueAtUtc);

public sealed record SnapshotDelivery(string ConnectionId, ProjectMahjong.Contracts.RoomSnapshotDto Snapshot);

