namespace ProjectMahjong.Contracts;

public sealed record CreateRoomRequest(string DisplayName);

public sealed record JoinRoomRequest(string DisplayName);

public sealed record RejoinRoomRequest(Guid PlayerId, string ReconnectToken);

public sealed record PlayerSessionDto(
    string RoomCode,
    Guid PlayerId,
    string DisplayName,
    string ReconnectToken);

public sealed record SessionResponseDto(PlayerSessionDto Session, RoomSnapshotDto Snapshot);

public sealed record ApiErrorDto(string Error);

public sealed record CommandReplyDto(bool Accepted, string? Error, long Revision)
{
    public static CommandReplyDto Success(long revision) => new(true, null, revision);

    public static CommandReplyDto Rejected(string error, long revision) =>
        new(false, error, revision);
}

public sealed record ResumeRequestDto(
    string RoomCode,
    Guid PlayerId,
    string ReconnectToken);

