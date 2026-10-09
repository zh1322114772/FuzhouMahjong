using Microsoft.AspNetCore.SignalR;
using ProjectMahjong.Contracts;
using ProjectMahjong.Game;
using ProjectMahjong.Server.Rooms;

namespace ProjectMahjong.Server.Hubs;

public sealed class GameHub(RoomRegistry rooms) : Hub<IGameClient>
{
    public async Task<CommandReplyDto> Resume(ResumeRequestDto request)
    {
        var error = await rooms.ConnectAsync(request, Context.ConnectionId);
        return error is null
            ? CommandReplyDto.Success(0)
            : CommandReplyDto.Rejected(error, 0);
    }

    public Task<CommandReplyDto> SetReady(bool ready) => rooms.SetReadyAsync(Context.ConnectionId, ready);
    public Task<CommandReplyDto> StartGame() => rooms.StartGameAsync(Context.ConnectionId);
    public Task<CommandReplyDto> StartNextHand() => rooms.StartNextHandAsync(Context.ConnectionId);
    public Task<CommandReplyDto> StartRematch() => rooms.StartRematchAsync(Context.ConnectionId);

    public Task<CommandReplyDto> Discard(int tileId, Guid commandId, long expectedRevision) =>
        rooms.DiscardAsync(Context.ConnectionId, tileId, commandId, expectedRevision);

    public Task<CommandReplyDto> SelfDrawnWin(Guid commandId, long expectedRevision) =>
        rooms.SelfDrawnWinAsync(Context.ConnectionId, commandId, expectedRevision);

    public Task<CommandReplyDto> ConcealedKong(int tileId, Guid commandId, long expectedRevision) =>
        rooms.ConcealedKongAsync(Context.ConnectionId, tileId, commandId, expectedRevision);

    public Task<CommandReplyDto> AddedKong(int tileId, Guid commandId, long expectedRevision) =>
        rooms.AddedKongAsync(Context.ConnectionId, tileId, commandId, expectedRevision);

    public Task<CommandReplyDto> Pass(Guid commandId, long expectedRevision) =>
        rooms.ClaimAsync(Context.ConnectionId, new ClaimChoice(ClaimKind.Pass), commandId, expectedRevision);

    public Task<CommandReplyDto> ClaimHu(Guid commandId, long expectedRevision) =>
        rooms.ClaimAsync(Context.ConnectionId, new ClaimChoice(ClaimKind.Hu), commandId, expectedRevision);

    public Task<CommandReplyDto> ClaimPeng(Guid commandId, long expectedRevision) =>
        rooms.ClaimAsync(Context.ConnectionId, new ClaimChoice(ClaimKind.Peng), commandId, expectedRevision);

    public Task<CommandReplyDto> ClaimKong(Guid commandId, long expectedRevision) =>
        rooms.ClaimAsync(Context.ConnectionId, new ClaimChoice(ClaimKind.Kong), commandId, expectedRevision);

    public Task<CommandReplyDto> ClaimChi(string optionId, Guid commandId, long expectedRevision) =>
        rooms.ClaimAsync(
            Context.ConnectionId,
            new ClaimChoice(ClaimKind.Chi, optionId),
            commandId,
            expectedRevision);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await rooms.DisconnectAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}

