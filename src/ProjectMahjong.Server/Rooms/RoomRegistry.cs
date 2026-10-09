using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using ProjectMahjong.Contracts;
using ProjectMahjong.Game;
using ProjectMahjong.Server.Hubs;

namespace ProjectMahjong.Server.Rooms;

public sealed class RoomRegistry(
    IHubContext<GameHub, IGameClient> hubContext,
    IOptions<FuzhouRuleOptions> options,
    ILogger<RoomRegistry> logger)
{
    private const string RoomCodeCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly ConcurrentDictionary<string, RoomSession> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PlayerLocation> _connections = [];
    private readonly FuzhouRuleOptions _options = options.Value;

    public async Task<SessionResponseDto> CreateRoomAsync(string displayName)
    {
        var room = CreateUniqueRoom(displayName);
        var session = await room.GetHostSessionAsync();
        var snapshot = await room.GetSnapshotAsync(session.PlayerId)
            ?? throw new InvalidOperationException("The host snapshot was not created.");
        logger.LogInformation("Room {RoomCode} created", room.Code);
        return new SessionResponseDto(session, snapshot);
    }

    public async Task<(SessionResponseDto? Response, string? Error)> JoinRoomAsync(
        string roomCode,
        string displayName)
    {
        if (!_rooms.TryGetValue(NormalizeCode(roomCode), out var room))
        {
            return (null, "找不到这个房间，请检查房间号。");
        }

        var (session, error) = await room.JoinAsync(displayName);
        if (session is null)
        {
            return (null, error);
        }

        var snapshot = await room.GetSnapshotAsync(session.PlayerId)
            ?? throw new InvalidOperationException("The joined player snapshot was not created.");
        await BroadcastAsync(room);
        return (new SessionResponseDto(session, snapshot), null);
    }

    public async Task<(SessionResponseDto? Response, string? Error)> RejoinRoomAsync(
        string roomCode,
        Guid playerId,
        string reconnectToken)
    {
        if (!_rooms.TryGetValue(NormalizeCode(roomCode), out var room))
        {
            return (null, "房间已不存在，服务器可能已经重启。");
        }

        var (session, error) = await room.RejoinAsync(playerId, reconnectToken);
        if (session is null)
        {
            return (null, error);
        }

        var snapshot = await room.GetSnapshotAsync(playerId)
            ?? throw new InvalidOperationException("The rejoined player snapshot was not created.");
        return (new SessionResponseDto(session, snapshot), null);
    }

    public async Task<string?> ConnectAsync(ResumeRequestDto request, string connectionId)
    {
        if (!_rooms.TryGetValue(NormalizeCode(request.RoomCode), out var room))
        {
            return "房间已不存在，服务器可能已经重启。";
        }

        var error = await room.ConnectAsync(request.PlayerId, request.ReconnectToken, connectionId);
        if (error is not null)
        {
            return error;
        }

        _connections[connectionId] = new PlayerLocation(room.Code, request.PlayerId);
        await BroadcastAsync(room);
        return null;
    }

    public async Task DisconnectAsync(string connectionId)
    {
        if (!_connections.TryRemove(connectionId, out var location) ||
            !_rooms.TryGetValue(location.RoomCode, out var room))
        {
            return;
        }

        if (await room.DisconnectAsync(location.PlayerId, connectionId))
        {
            await BroadcastAsync(room);
        }
    }

    public Task<CommandReplyDto> SetReadyAsync(string connectionId, bool ready) =>
        ExecuteAsync(connectionId, (room, playerId) => room.SetReadyAsync(playerId, ready));

    public Task<CommandReplyDto> StartGameAsync(string connectionId) =>
        ExecuteAsync(connectionId, (room, playerId) => room.StartGameAsync(playerId));

    public Task<CommandReplyDto> StartNextHandAsync(string connectionId) =>
        ExecuteAsync(connectionId, (room, playerId) => room.StartNextHandAsync(playerId));

    public Task<CommandReplyDto> StartRematchAsync(string connectionId) =>
        ExecuteAsync(connectionId, (room, playerId) => room.StartRematchAsync(playerId));

    public Task<CommandReplyDto> DiscardAsync(
        string connectionId,
        int tileId,
        Guid commandId,
        long expectedRevision) =>
        ExecuteAsync(connectionId, (room, playerId) =>
            room.DiscardAsync(playerId, tileId, commandId, expectedRevision));

    public Task<CommandReplyDto> SelfDrawnWinAsync(
        string connectionId,
        Guid commandId,
        long expectedRevision) =>
        ExecuteAsync(connectionId, (room, playerId) =>
            room.SelfDrawnWinAsync(playerId, commandId, expectedRevision));

    public Task<CommandReplyDto> ConcealedKongAsync(
        string connectionId,
        int tileId,
        Guid commandId,
        long expectedRevision) =>
        ExecuteAsync(connectionId, (room, playerId) =>
            room.ConcealedKongAsync(playerId, tileId, commandId, expectedRevision));

    public Task<CommandReplyDto> AddedKongAsync(
        string connectionId,
        int tileId,
        Guid commandId,
        long expectedRevision) =>
        ExecuteAsync(connectionId, (room, playerId) =>
            room.AddedKongAsync(playerId, tileId, commandId, expectedRevision));

    public Task<CommandReplyDto> ClaimAsync(
        string connectionId,
        ClaimChoice choice,
        Guid commandId,
        long expectedRevision) =>
        ExecuteAsync(connectionId, (room, playerId) =>
            room.ClaimAsync(playerId, choice, commandId, expectedRevision));

    public async Task CleanupExpiredRoomsAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-_options.EmptyRoomLifetimeMinutes);
        foreach (var pair in _rooms)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await pair.Value.IsExpiredAsync(cutoff) && _rooms.TryRemove(pair.Key, out var removedRoom))
            {
                removedRoom.Dispose();
                logger.LogInformation("Expired room {RoomCode} removed", pair.Key);
            }
        }
    }

    private async Task<CommandReplyDto> ExecuteAsync(
        string connectionId,
        Func<RoomSession, Guid, Task<RoomMutation>> action)
    {
        if (!_connections.TryGetValue(connectionId, out var location) ||
            !_rooms.TryGetValue(location.RoomCode, out var room))
        {
            return CommandReplyDto.Rejected("尚未连接到房间。", 0);
        }

        var mutation = await action(room, location.PlayerId);
        if (!mutation.Accepted)
        {
            return CommandReplyDto.Rejected(mutation.Error ?? "操作失败。", mutation.Revision);
        }

        var botClaimTileId = await room.RunBotsAsync();
        await BroadcastAsync(room);
        if (botClaimTileId is int tileId)
        {
            ScheduleClaimTimeout(room, tileId);
        }

        return CommandReplyDto.Success(mutation.Revision);
    }

    private void ScheduleClaimTimeout(RoomSession room, int claimTileId)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(_options.ClaimTimeoutSeconds));
            if (await room.ResolveClaimTimeoutAsync(claimTileId))
            {
                var nextClaimTileId = await room.RunBotsAsync();
                await BroadcastAsync(room);
                if (nextClaimTileId is int tileId)
                {
                    ScheduleClaimTimeout(room, tileId);
                }
            }
        });
    }

    private async Task BroadcastAsync(RoomSession room)
    {
        var deliveries = await room.GetDeliveriesAsync();
        await Task.WhenAll(deliveries.Select(delivery =>
            hubContext.Clients.Client(delivery.ConnectionId).SnapshotUpdated(delivery.Snapshot)));
    }

    private RoomSession CreateUniqueRoom(string hostName)
    {
        while (true)
        {
            var code = CreateRoomCode();
            var room = new RoomSession(code, hostName, _options);
            if (_rooms.TryAdd(code, room))
            {
                return room;
            }
        }
    }

    private static string CreateRoomCode()
    {
        Span<char> code = stackalloc char[6];
        for (var index = 0; index < code.Length; index++)
        {
            code[index] = RoomCodeCharacters[Random.Shared.Next(RoomCodeCharacters.Length)];
        }

        return code.ToString();
    }

    private static string NormalizeCode(string roomCode) => roomCode.Trim().ToUpperInvariant();

    private sealed record PlayerLocation(string RoomCode, Guid PlayerId);
}

