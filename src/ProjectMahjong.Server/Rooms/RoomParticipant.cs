using ProjectMahjong.Contracts;

namespace ProjectMahjong.Server.Rooms;

public sealed class RoomParticipant(
    Guid id,
    string displayName,
    Seat seat,
    string reconnectToken,
    bool isHost,
    bool isBot,
    int startingScore)
{
    public Guid Id { get; } = id;

    public string DisplayName { get; } = displayName;

    public Seat Seat { get; } = seat;

    public string ReconnectToken { get; } = reconnectToken;

    public bool IsHost { get; } = isHost;

    public bool IsBot { get; } = isBot;

    public bool IsReady { get; set; }

    public string? ConnectionId { get; set; }

    public int StartingScore { get; } = startingScore;

    public bool IsConnected => IsBot || ConnectionId is not null;

    public PlayerSessionDto ToSession(string roomCode) =>
        new(roomCode, Id, DisplayName, ReconnectToken);
}

