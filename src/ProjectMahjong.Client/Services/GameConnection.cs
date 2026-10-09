using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using ProjectMahjong.Contracts;

namespace ProjectMahjong.Client.Services;

public enum GameConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting
}

public sealed class GameConnection(NavigationManager navigation) : IAsyncDisposable
{
    private HubConnection? _hub;
    private PlayerSessionDto? _session;

    public event Action? Changed;

    public RoomSnapshotDto? Snapshot { get; private set; }

    public GameConnectionStatus Status { get; private set; } = GameConnectionStatus.Disconnected;

    public string? Error { get; private set; }

    public async Task StartAsync(PlayerSessionDto session)
    {
        await StopAsync();
        _session = session;
        SetStatus(GameConnectionStatus.Connecting);

        _hub = new HubConnectionBuilder()
            .WithUrl(navigation.ToAbsoluteUri(GameHubRoutes.Path))
            .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)])
            .WithStatefulReconnect()
            .Build();

        _hub.On<RoomSnapshotDto>(nameof(IGameClient.SnapshotUpdated), snapshot =>
        {
            if (Snapshot is null || snapshot.Revision >= Snapshot.Revision)
            {
                Snapshot = snapshot;
                Error = null;
                NotifyChanged();
            }
        });

        _hub.On<string>(nameof(IGameClient.Notice), message =>
        {
            Error = message;
            NotifyChanged();
        });

        _hub.Reconnecting += _ =>
        {
            SetStatus(GameConnectionStatus.Reconnecting);
            return Task.CompletedTask;
        };

        _hub.Reconnected += async _ =>
        {
            SetStatus(GameConnectionStatus.Connected);
            await ResumeAsync();
        };

        _hub.Closed += _ =>
        {
            SetStatus(GameConnectionStatus.Disconnected);
            return Task.CompletedTask;
        };

        try
        {
            await _hub.StartAsync();
            SetStatus(GameConnectionStatus.Connected);
            await ResumeAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            Error = "连接牌桌失败，请刷新页面重试。";
            SetStatus(GameConnectionStatus.Disconnected);
        }
    }

    public Task<CommandReplyDto> SetReadyAsync(bool ready) => InvokeAsync("SetReady", ready);
    public Task<CommandReplyDto> StartGameAsync() => InvokeAsync("StartGame");
    public Task<CommandReplyDto> StartNextHandAsync() => InvokeAsync("StartNextHand");
    public Task<CommandReplyDto> StartRematchAsync() => InvokeAsync("StartRematch");
    public Task<CommandReplyDto> DiscardAsync(int tileId) => InvokeGameCommandAsync("Discard", tileId);
    public Task<CommandReplyDto> SelfDrawnWinAsync() => InvokeGameCommandAsync("SelfDrawnWin");
    public Task<CommandReplyDto> ConcealedKongAsync(int tileId) => InvokeGameCommandAsync("ConcealedKong", tileId);
    public Task<CommandReplyDto> AddedKongAsync(int tileId) => InvokeGameCommandAsync("AddedKong", tileId);
    public Task<CommandReplyDto> PassAsync() => InvokeGameCommandAsync("Pass");
    public Task<CommandReplyDto> ClaimHuAsync() => InvokeGameCommandAsync("ClaimHu");
    public Task<CommandReplyDto> ClaimPengAsync() => InvokeGameCommandAsync("ClaimPeng");
    public Task<CommandReplyDto> ClaimKongAsync() => InvokeGameCommandAsync("ClaimKong");
    public Task<CommandReplyDto> ClaimChiAsync(string optionId) => InvokeGameCommandAsync("ClaimChi", optionId);

    public async Task StopAsync()
    {
        if (_hub is null)
        {
            return;
        }

        await _hub.DisposeAsync();
        _hub = null;
        Snapshot = null;
        SetStatus(GameConnectionStatus.Disconnected);
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task ResumeAsync()
    {
        if (_session is null)
        {
            return;
        }

        var reply = await InvokeAsync(
            "Resume",
            new ResumeRequestDto(_session.RoomCode, _session.PlayerId, _session.ReconnectToken));
        if (!reply.Accepted)
        {
            Error = reply.Error;
            NotifyChanged();
        }
    }

    private Task<CommandReplyDto> InvokeGameCommandAsync(string method, params object?[] arguments)
    {
        if (Snapshot is null)
        {
            return Task.FromResult(CommandReplyDto.Rejected("牌桌尚未准备好。", 0));
        }

        var commandArguments = arguments
            .Concat([Guid.NewGuid(), Snapshot.Revision])
            .ToArray();
        return InvokeAsync(method, commandArguments);
    }

    private async Task<CommandReplyDto> InvokeAsync(string method, params object?[] arguments)
    {
        if (_hub?.State != HubConnectionState.Connected)
        {
            return CommandReplyDto.Rejected("网络正在重连，请稍候。", Snapshot?.Revision ?? 0);
        }

        try
        {
            var reply = await _hub.InvokeCoreAsync<CommandReplyDto>(method, arguments);
            if (!reply.Accepted)
            {
                Error = reply.Error;
                NotifyChanged();
            }

            return reply;
        }
        catch (Exception exception) when (exception is HubException or InvalidOperationException)
        {
            Error = "操作没有送达服务器，请稍后重试。";
            NotifyChanged();
            return CommandReplyDto.Rejected(Error, Snapshot?.Revision ?? 0);
        }
    }

    private void SetStatus(GameConnectionStatus status)
    {
        Status = status;
        NotifyChanged();
    }

    private void NotifyChanged() => Changed?.Invoke();
}
