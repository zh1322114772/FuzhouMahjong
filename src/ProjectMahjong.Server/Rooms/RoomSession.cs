using System.Security.Cryptography;
using ProjectMahjong.Contracts;
using ProjectMahjong.Game;

namespace ProjectMahjong.Server.Rooms;

public sealed class RoomSession : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly FuzhouRuleOptions _options;
    private readonly Dictionary<Guid, RoomParticipant> _participants = [];
    private readonly HashSet<Guid> _processedCommands = [];
    private FuzhouGame? _game;
    private int? _claimDeadlineTileId;
    private DateTimeOffset? _claimDeadlineUtc;

    public RoomSession(string code, string hostName, FuzhouRuleOptions options)
    {
        Code = code;
        _options = options;
        var host = CreateParticipant(hostName, Seat.East, isHost: true, isBot: false);
        _participants.Add(host.Id, host);
        LastActivityUtc = DateTimeOffset.UtcNow;
    }

    public string Code { get; }

    public long Revision { get; private set; }

    public DateTimeOffset LastActivityUtc { get; private set; }

    public async Task<PlayerSessionDto> GetHostSessionAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return _participants.Values.Single(participant => participant.IsHost).ToSession(Code);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(PlayerSessionDto? Session, string? Error)> JoinAsync(string displayName)
    {
        await _gate.WaitAsync();
        try
        {
            if (_game is not null)
            {
                return (null, "牌局已经开始，不能加入新玩家。");
            }

            if (_participants.Count >= 4)
            {
                return (null, "房间已经坐满四人。");
            }

            if (_participants.Values.Any(participant =>
                    string.Equals(participant.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)))
            {
                return (null, "房间内已经有人使用这个名字。");
            }

            var seat = Enum.GetValues<Seat>().First(candidate =>
                _participants.Values.All(participant => participant.Seat != candidate));
            var participant = CreateParticipant(displayName, seat, isHost: false, isBot: false);
            _participants.Add(participant.Id, participant);
            Touch();
            Revision++;
            return (participant.ToSession(Code), null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(PlayerSessionDto? Session, string? Error)> RejoinAsync(
        Guid playerId,
        string reconnectToken)
    {
        await _gate.WaitAsync();
        try
        {
            if (!TryAuthenticate(playerId, reconnectToken, out var participant))
            {
                return (null, "恢复码无效，无法找回座位。");
            }

            Touch();
            return (participant!.ToSession(Code), null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> ConnectAsync(
        Guid playerId,
        string reconnectToken,
        string connectionId)
    {
        await _gate.WaitAsync();
        try
        {
            if (!TryAuthenticate(playerId, reconnectToken, out var participant))
            {
                return "身份验证失败，请重新加入房间。";
            }

            participant!.ConnectionId = connectionId;
            Touch();
            Revision++;
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DisconnectAsync(Guid playerId, string connectionId)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_participants.TryGetValue(playerId, out var participant) ||
                participant.ConnectionId != connectionId)
            {
                return false;
            }

            participant.ConnectionId = null;
            Touch();
            Revision++;
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<RoomMutation> SetReadyAsync(Guid playerId, bool ready) => MutateAsync(() =>
    {
        if (_game is not null)
        {
            return EngineActionResult.Reject("牌局开始后不能改变准备状态。");
        }

        _participants[playerId].IsReady = ready;
        return EngineActionResult.Success();
    });

    public Task<RoomMutation> StartGameAsync(Guid playerId) => MutateAsync(() =>
    {
        var participant = _participants[playerId];
        if (_game is not null)
        {
            return EngineActionResult.Reject("比赛已经开始。");
        }

        if (!participant.IsHost)
        {
            return EngineActionResult.Reject("只有房主可以开始游戏。");
        }

        if (_participants.Values.Where(player => !player.IsBot).Any(player => !player.IsReady))
        {
            return EngineActionResult.Reject("所有玩家准备后才能开始。");
        }

        FillEmptySeatsWithBots();

        var gamePlayers = _participants.Values
            .OrderBy(player => player.Seat)
            .Select(player => new GamePlayer(player.Id, player.DisplayName, player.Seat))
            .ToArray();
        _game = new FuzhouGame(gamePlayers, Seat.East, _options);
        return _game.StartNextHand();
    });

    public Task<RoomMutation> StartNextHandAsync(Guid playerId) => MutateAsync(() =>
    {
        if (!_participants[playerId].IsHost)
        {
            return EngineActionResult.Reject("只有房主可以开始下一局。");
        }

        if (_game is null)
        {
            return EngineActionResult.Reject("比赛尚未开始。");
        }

        return _game.StartNextHand();
    });

    public Task<RoomMutation> StartRematchAsync(Guid playerId) => MutateAsync(() =>
    {
        if (!_participants[playerId].IsHost)
        {
            return EngineActionResult.Reject("只有房主可以开始再来一场。");
        }

        if (_game is null || !_game.IsMatchFinished)
        {
            return EngineActionResult.Reject("本场比赛尚未结束。");
        }

        return _game.ResetMatch();
    });

    public Task<RoomMutation> DiscardAsync(
        Guid playerId,
        int tileId,
        Guid commandId,
        long expectedRevision) =>
        ApplyGameCommandAsync(playerId, commandId, expectedRevision,
            (game, seat) => game.Discard(seat, tileId));

    public Task<RoomMutation> SelfDrawnWinAsync(
        Guid playerId,
        Guid commandId,
        long expectedRevision) =>
        ApplyGameCommandAsync(playerId, commandId, expectedRevision,
            (game, seat) => game.DeclareSelfDrawnWin(seat));

    public Task<RoomMutation> ConcealedKongAsync(
        Guid playerId,
        int tileId,
        Guid commandId,
        long expectedRevision) =>
        ApplyGameCommandAsync(playerId, commandId, expectedRevision,
            (game, seat) => game.DeclareConcealedKong(seat, tileId));

    public Task<RoomMutation> AddedKongAsync(
        Guid playerId,
        int tileId,
        Guid commandId,
        long expectedRevision) =>
        ApplyGameCommandAsync(playerId, commandId, expectedRevision,
            (game, seat) => game.DeclareAddedKong(seat, tileId));

    public Task<RoomMutation> ClaimAsync(
        Guid playerId,
        ClaimChoice choice,
        Guid commandId,
        long expectedRevision) =>
        ApplyGameCommandAsync(
            playerId,
            commandId,
            expectedRevision,
            (game, seat) => game.SubmitClaim(seat, choice),
            allowStaleRevision: true);

    public async Task<bool> ResolveClaimTimeoutAsync(int claimTileId)
    {
        await _gate.WaitAsync();
        try
        {
            if (_game?.State.PendingClaims?.Discard.Id != claimTileId)
            {
                return false;
            }

            _game.ResolveClaimTimeout();
            SyncClaimDeadline();
            Touch();
            Revision++;
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int?> RunBotsAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_game is null)
            {
                return null;
            }

            var changed = false;
            for (var step = 0; step < 200; step++)
            {
                if (_game.State.Phase is HandPhase.Ended or HandPhase.NotStarted)
                {
                    break;
                }

                if (_game.State.Phase == HandPhase.AwaitingClaims)
                {
                    var waitingBots = _game.State.PendingClaims!.Possibilities.Keys
                        .Where(seat => _participants.Values.Single(player => player.Seat == seat).IsBot)
                        .Where(seat => !_game.State.PendingClaims.Choices.ContainsKey(seat))
                        .ToArray();

                    foreach (var botSeat in waitingBots)
                    {
                        var possibilities = _game.GetClaimPossibilities(botSeat);
                        if (possibilities is not null)
                        {
                            _game.SubmitClaim(botSeat, ChooseBotClaim(possibilities));
                            changed = true;
                        }
                    }

                    if (_game.State.Phase == HandPhase.AwaitingClaims)
                    {
                        break;
                    }

                    continue;
                }

                var currentPlayer = _participants.Values.Single(player => player.Seat == _game.State.CurrentTurn);
                if (!currentPlayer.IsBot)
                {
                    break;
                }

                PlayBotTurn(currentPlayer.Seat);
                changed = true;
            }

            if (changed)
            {
                Touch();
                Revision++;
            }

            SyncClaimDeadline();
            return _game.State.PendingClaims?.Discard.Id;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RoomSnapshotDto?> GetSnapshotAsync(Guid playerId)
    {
        await _gate.WaitAsync();
        try
        {
            return _participants.ContainsKey(playerId) ? BuildSnapshot(playerId) : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<SnapshotDelivery>> GetDeliveriesAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return _participants.Values
                .Where(participant => participant.ConnectionId is not null)
                .Select(participant => new SnapshotDelivery(
                    participant.ConnectionId!,
                    BuildSnapshot(participant.Id)))
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> IsExpiredAsync(DateTimeOffset cutoff)
    {
        await _gate.WaitAsync();
        try
        {
            return LastActivityUtc < cutoff && _participants.Values
                .Where(player => !player.IsBot)
                .All(player => !player.IsConnected);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<RoomMutation> ApplyGameCommandAsync(
        Guid playerId,
        Guid commandId,
        long expectedRevision,
        Func<FuzhouGame, Seat, EngineActionResult> action,
        bool allowStaleRevision = false)
    {
        await _gate.WaitAsync();
        try
        {
            if (_processedCommands.Contains(commandId))
            {
                return RoomMutation.Success(Revision);
            }

            if (!allowStaleRevision && expectedRevision != Revision)
            {
                return RoomMutation.Reject("牌桌状态已经更新，请重试。", Revision);
            }

            if (_game is null)
            {
                return RoomMutation.Reject("比赛尚未开始。", Revision);
            }

            var participant = _participants[playerId];
            var result = action(_game, participant.Seat);
            if (!result.Accepted)
            {
                return RoomMutation.Reject(result.Error ?? "操作失败。", Revision);
            }

            _processedCommands.Add(commandId);
            TrimProcessedCommands();
            SyncClaimDeadline();
            Touch();
            Revision++;
            var claimTileId = result.StartClaimTimer ? _game.State.PendingClaims?.Discard.Id : null;
            return RoomMutation.Success(Revision, claimTileId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<RoomMutation> MutateAsync(Func<EngineActionResult> action)
    {
        await _gate.WaitAsync();
        try
        {
            var result = action();
            if (!result.Accepted)
            {
                return RoomMutation.Reject(result.Error ?? "操作失败。", Revision);
            }

            SyncClaimDeadline();
            Touch();
            Revision++;
            return RoomMutation.Success(Revision);
        }
        finally
        {
            _gate.Release();
        }
    }

    private RoomSnapshotDto BuildSnapshot(Guid playerId)
    {
        var viewer = _participants[playerId];
        var stage = GetRoomStage();
        var seats = _participants.Values
            .OrderBy(participant => participant.Seat)
            .Select(participant => BuildSeatSnapshot(participant))
            .ToArray();

        if (_game is null)
        {
            return new RoomSnapshotDto(
                Code,
                Revision,
                RoomStage.Lobby,
                viewer.Id,
                null,
                Seat.East,
                0,
                _options.TotalHands,
                0,
                null,
                null,
                null,
                null,
                null,
                null,
                seats,
                [],
                null,
                AvailableActionsDto.None,
                null,
                _participants.Count < 4 ? "等待其他玩家加入" : "请所有玩家准备");
        }

        var viewerState = _game.State.Players[viewer.Seat];
        var pending = _game.State.PendingClaims;
        return new RoomSnapshotDto(
            Code,
            Revision,
            stage,
            viewer.Id,
            _game.State.Phase == HandPhase.Ended ? null : _game.State.CurrentTurn,
            _game.State.Dealer,
            _game.State.HandNumber,
            _options.TotalHands,
            _game.State.Wall.Count,
            _game.State.GoldIndicator?.ToDto(),
            pending?.Discard.ToDto(),
            pending?.DiscardedBy,
            _game.State.LastDiscard?.ToDto(),
            _game.State.LastDiscardedBy,
            _claimDeadlineUtc,
            seats,
            viewerState.Hand.Select(tile => tile.ToDto()).ToArray(),
            viewerState.DrawnTileId,
            BuildAvailableActions(viewer),
            BuildHandResult(),
            BuildStatusMessage(viewer));
    }

    private SeatSnapshotDto BuildSeatSnapshot(RoomParticipant participant)
    {
        var playerState = _game?.State.Players.GetValueOrDefault(participant.Seat);
        return new SeatSnapshotDto(
            participant.Id,
            participant.DisplayName,
            participant.Seat,
            participant.IsHost,
            participant.IsBot,
            participant.IsConnected,
            participant.IsReady,
            playerState?.Score ?? participant.StartingScore,
            playerState?.Hand.Count ?? 0,
            playerState?.Flowers.Select(tile => tile.ToDto()).ToArray() ?? [],
            playerState?.Melds.Select(meld => new MeldDto(
                meld.Kind,
                meld.Tiles.Select(tile => tile.ToDto()).ToArray(),
                meld.FromSeat)).ToArray() ?? [],
            playerState?.Discards.Select(tile => tile.ToDto()).ToArray() ?? []);
    }

    private AvailableActionsDto BuildAvailableActions(RoomParticipant viewer)
    {
        if (_game is null)
        {
            return AvailableActionsDto.None;
        }

        var turn = _game.GetTurnPossibilities(viewer.Seat);
        var claim = _game.GetClaimPossibilities(viewer.Seat);
        var viewerHand = _game.State.Players[viewer.Seat].Hand;
        var chiOptions = claim?.ChiChoices.Select(option => new ChiOptionDto(
            option.Id,
            option.TileIds
                .Select(id => viewerHand.Single(tile => tile.Id == id).ToDto())
                .ToArray())).ToArray() ?? [];

        return new AvailableActionsDto(
            _game.State.Phase == HandPhase.AwaitingDiscard && _game.State.CurrentTurn == viewer.Seat,
            claim is not null,
            turn.CanHu || claim?.CanHu == true,
            claim?.CanPeng == true,
            claim?.CanKong == true,
            chiOptions,
            turn.ConcealedKongs.Select(tile => tile.ToDto()).ToArray(),
            turn.AddedKongs.Select(tile => tile.ToDto()).ToArray());
    }

    private HandResultDto? BuildHandResult()
    {
        var result = _game?.State.LastHandScore;
        if (result is null)
        {
            return null;
        }

        var payments = result.Payments
            .Where(pair => pair.Value != 0)
            .Select(pair => new ScorePaymentDto(
                _participants.Values.Single(player => player.Seat == pair.Key).DisplayName,
                pair.Value))
            .ToArray();

        var winnerName = result.Winner is null
            ? null
            : _participants.Values.Single(player => player.Seat == result.Winner).DisplayName;

        return new HandResultDto(
            result.WasDraw,
            result.Summary,
            winnerName,
            result.Points,
            result.Items,
            payments);
    }

    private RoomStage GetRoomStage()
    {
        if (_game is null)
        {
            return RoomStage.Lobby;
        }

        if (_game.State.Phase == HandPhase.Ended)
        {
            return _game.IsMatchFinished ? RoomStage.MatchEnded : RoomStage.HandEnded;
        }

        return _game.State.Phase == HandPhase.AwaitingClaims
            ? RoomStage.WaitingForClaims
            : RoomStage.Playing;
    }

    private string BuildStatusMessage(RoomParticipant viewer)
    {
        if (_game is null)
        {
            return "等待牌局开始";
        }

        if (_game.State.Phase == HandPhase.Ended)
        {
            return _game.IsMatchFinished ? "本场比赛结束" : "本局结束";
        }

        if (_game.State.Phase == HandPhase.AwaitingClaims)
        {
            return _game.GetClaimPossibilities(viewer.Seat) is null
                ? "等待其他玩家响应弃牌"
                : "请选择吃、碰、杠、和或过";
        }

        return _game.State.CurrentTurn == viewer.Seat ? "轮到你出牌" : "等待其他玩家出牌";
    }

    private bool TryAuthenticate(
        Guid playerId,
        string reconnectToken,
        out RoomParticipant? participant)
    {
        if (!_participants.TryGetValue(playerId, out participant))
        {
            return false;
        }

        var expected = Convert.FromHexString(participant.ReconnectToken);
        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(reconnectToken);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    private RoomParticipant CreateParticipant(string name, Seat seat, bool isHost, bool isBot) => new(
        Guid.NewGuid(),
        name,
        seat,
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
        isHost,
        isBot,
        _options.StartingScore);

    private void FillEmptySeatsWithBots()
    {
        foreach (var seat in Enum.GetValues<Seat>())
        {
            if (_participants.Values.Any(player => player.Seat == seat))
            {
                continue;
            }

            var bot = CreateParticipant($"电脑·{SeatName(seat)}", seat, isHost: false, isBot: true);
            bot.IsReady = true;
            _participants.Add(bot.Id, bot);
        }
    }

    private void PlayBotTurn(Seat seat)
    {
        var possibilities = _game!.GetTurnPossibilities(seat);
        if (possibilities.CanHu)
        {
            _game.DeclareSelfDrawnWin(seat);
            return;
        }

        if (possibilities.ConcealedKongs.Count > 0)
        {
            _game.DeclareConcealedKong(seat, possibilities.ConcealedKongs[0].Id);
            return;
        }

        if (possibilities.AddedKongs.Count > 0)
        {
            _game.DeclareAddedKong(seat, possibilities.AddedKongs[0].Id);
            return;
        }

        var hand = _game.State.Players[seat].Hand;
        var discard = hand.LastOrDefault(tile => _game.State.GoldType is null || tile.Type != _game.State.GoldType.Value)
            ?? hand[^1];
        _game.Discard(seat, discard.Id);
    }

    private static ClaimChoice ChooseBotClaim(ClaimPossibilities possibilities)
    {
        if (possibilities.CanHu) return new ClaimChoice(ClaimKind.Hu);
        if (possibilities.CanKong) return new ClaimChoice(ClaimKind.Kong);
        if (possibilities.CanPeng) return new ClaimChoice(ClaimKind.Peng);
        if (possibilities.ChiChoices.Count > 0) return new ClaimChoice(ClaimKind.Chi, possibilities.ChiChoices[0].Id);
        return new ClaimChoice(ClaimKind.Pass);
    }

    private static string SeatName(Seat seat) => seat switch
    {
        Seat.East => "东",
        Seat.South => "南",
        Seat.West => "西",
        _ => "北"
    };

    private void SyncClaimDeadline()
    {
        var pendingTileId = _game?.State.PendingClaims?.Discard.Id;
        if (pendingTileId is null)
        {
            _claimDeadlineTileId = null;
            _claimDeadlineUtc = null;
            return;
        }

        if (_claimDeadlineTileId != pendingTileId)
        {
            _claimDeadlineTileId = pendingTileId;
            _claimDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(_options.ClaimTimeoutSeconds);
        }
    }

    private void Touch() => LastActivityUtc = DateTimeOffset.UtcNow;

    private void TrimProcessedCommands()
    {
        if (_processedCommands.Count > 1_000)
        {
            _processedCommands.Clear();
        }
    }

    public void Dispose() => _gate.Dispose();
}

