using ProjectMahjong.Contracts;

namespace ProjectMahjong.Game;

public sealed record EngineActionResult(bool Accepted, string? Error)
{
    public static EngineActionResult Success() => new(true, null);

    public static EngineActionResult Reject(string error) => new(false, error);
}

public sealed record TurnPossibilities(
    bool CanHu,
    IReadOnlyList<Tile> ConcealedKongs,
    IReadOnlyList<Tile> AddedKongs);

public sealed class FuzhouGame
{
    private readonly FuzhouRuleOptions _options;
    private readonly IRandomSource _random;

    public FuzhouGame(
        IReadOnlyList<GamePlayer> players,
        Seat dealer,
        FuzhouRuleOptions options,
        IRandomSource? random = null)
    {
        if (players.Count != 4 || players.Select(player => player.Seat).Distinct().Count() != 4)
        {
            throw new ArgumentException("A game requires exactly four unique seats.", nameof(players));
        }

        _options = options;
        _random = random ?? new SystemRandomSource();
        State = new GameState(players, dealer, options);
    }

    public GameState State { get; }

    public bool IsMatchFinished => State.HandNumber >= _options.TotalHands && State.Phase == HandPhase.Ended;

    public EngineActionResult StartNextHand()
    {
        if (State.Phase is not HandPhase.NotStarted and not HandPhase.Ended)
        {
            return EngineActionResult.Reject("当前牌局尚未结束。");
        }

        if (IsMatchFinished)
        {
            return EngineActionResult.Reject("本场比赛已经结束。");
        }

        foreach (var player in State.Players.Values)
        {
            player.Hand.Clear();
            player.Flowers.Clear();
            player.Melds.Clear();
            player.Discards.Clear();
            player.DrawnTileId = null;
            player.HasDiscardedGold = false;
        }

        State.Wall.Clear();
        State.Wall.AddRange(TileSet.Create());
        _random.Shuffle(State.Wall);
        State.GoldIndicator = null;
        State.PendingClaims = null;
        State.LastDiscard = null;
        State.LastDiscardedBy = null;
        State.LastHandScore = null;
        State.HandNumber++;

        foreach (var seat in SeatsInOrderFrom(State.Dealer))
        {
            var tileCount = seat == State.Dealer ? 17 : 16;
            for (var index = 0; index < tileCount; index++)
            {
                State.Players[seat].Hand.Add(DrawFront());
            }
        }

        foreach (var seat in SeatsInOrderFrom(State.Dealer))
        {
            ReplaceAllBonusTiles(State.Players[seat]);
            State.Players[seat].DrawnTileId = null;
        }

        OpenGoldIndicator();
        SortAllHands();
        State.CurrentTurn = State.Dealer;
        State.Phase = HandPhase.AwaitingDiscard;
        return EngineActionResult.Success();
    }

    public EngineActionResult ResetMatch()
    {
        if (State.Phase != HandPhase.Ended)
        {
            return EngineActionResult.Reject("只能在一场比赛结束后重新开始。");
        }

        foreach (var player in State.Players.Values)
        {
            player.Score = _options.StartingScore;
        }

        State.HandNumber = 0;
        State.Dealer = Seat.East;
        State.Phase = HandPhase.NotStarted;
        return StartNextHand();
    }

    public EngineActionResult Discard(Seat seat, int tileId)
    {
        if (State.Phase != HandPhase.AwaitingDiscard || State.CurrentTurn != seat)
        {
            return EngineActionResult.Reject("现在不能出牌。");
        }

        var player = State.Players[seat];
        var tile = player.Hand.SingleOrDefault(candidate => candidate.Id == tileId);
        if (tile is null)
        {
            return EngineActionResult.Reject("手牌中找不到这张牌。");
        }

        player.Hand.Remove(tile);
        player.DrawnTileId = null;
        SortHand(player);
        player.Discards.Add(tile);
        player.HasDiscardedGold |= State.GoldType is not null && tile.Type == State.GoldType.Value;
        State.LastDiscard = tile;
        State.LastDiscardedBy = seat;

        var pending = new PendingClaims(tile, seat);
        foreach (var otherSeat in OtherSeatsAfter(seat))
        {
            var possibilities = BuildClaimPossibilities(otherSeat, pending);
            if (possibilities.HasAny)
            {
                pending.Possibilities.Add(otherSeat, possibilities);
            }
        }

        if (pending.Possibilities.Count == 0)
        {
            AdvanceAfterUnclaimedDiscard(seat);
            return EngineActionResult.Success();
        }

        State.PendingClaims = pending;
        State.Phase = HandPhase.AwaitingClaims;
        return EngineActionResult.Success();
    }

    public EngineActionResult DeclareSelfDrawnWin(Seat seat)
    {
        if (State.Phase != HandPhase.AwaitingDiscard || State.CurrentTurn != seat)
        {
            return EngineActionResult.Reject("现在不能和牌。");
        }

        var player = State.Players[seat];
        if (!WinValidator.CanWin(player.Hand, player.Melds.Count, State.GoldType))
        {
            return EngineActionResult.Reject("当前手牌不能和牌。");
        }

        FinishWin(seat, selfDrawn: true, discardedBy: null, winningDiscard: null);
        return EngineActionResult.Success();
    }

    public EngineActionResult DeclareConcealedKong(Seat seat, int tileId)
    {
        if (!CanActOnOwnTurn(seat))
        {
            return EngineActionResult.Reject("现在不能杠牌。");
        }

        var player = State.Players[seat];
        var selected = player.Hand.SingleOrDefault(tile => tile.Id == tileId);
        if (selected is null || IsGold(selected))
        {
            return EngineActionResult.Reject("这张牌不能暗杠。");
        }

        var tiles = player.Hand.Where(tile => tile.Type == selected.Type).Take(4).ToArray();
        if (tiles.Length != 4)
        {
            return EngineActionResult.Reject("手中没有四张相同的牌。");
        }

        foreach (var tile in tiles)
        {
            player.Hand.Remove(tile);
        }

        player.Melds.Add(new Meld(MeldKind.ConcealedKong, tiles, null));
        DrawReplacementFor(player);
        SortHand(player);
        return EngineActionResult.Success();
    }

    public EngineActionResult DeclareAddedKong(Seat seat, int tileId)
    {
        if (!CanActOnOwnTurn(seat))
        {
            return EngineActionResult.Reject("现在不能补杠。");
        }

        var player = State.Players[seat];
        var selected = player.Hand.SingleOrDefault(tile => tile.Id == tileId);
        var peng = selected is null
            ? null
            : player.Melds.SingleOrDefault(meld =>
                meld.Kind == MeldKind.Peng && meld.Tiles[0].Type == selected.Type);

        if (selected is null || peng is null || IsGold(selected))
        {
            return EngineActionResult.Reject("这张牌不能补杠。");
        }

        player.Hand.Remove(selected);
        var meldIndex = player.Melds.IndexOf(peng);
        player.Melds[meldIndex] = new Meld(
            MeldKind.ExposedKong,
            peng.Tiles.Append(selected).ToArray(),
            peng.FromSeat);

        DrawReplacementFor(player);
        SortHand(player);
        return EngineActionResult.Success();
    }

    public EngineActionResult SubmitClaim(Seat seat, ClaimChoice choice)
    {
        if (State.Phase != HandPhase.AwaitingClaims || State.PendingClaims is null)
        {
            return EngineActionResult.Reject("当前没有等待处理的弃牌。");
        }

        if (!State.PendingClaims.Possibilities.TryGetValue(seat, out var possibilities))
        {
            return EngineActionResult.Reject("你不能响应这张牌。");
        }

        if (State.PendingClaims.Choices.ContainsKey(seat))
        {
            return EngineActionResult.Reject("你已经做出选择。");
        }

        if (!IsValidChoice(choice, possibilities))
        {
            return EngineActionResult.Reject("这个操作当前不可用。");
        }

        State.PendingClaims.Choices.Add(seat, choice);
        if (State.PendingClaims.Choices.Count == State.PendingClaims.Possibilities.Count)
        {
            ResolvePendingClaims();
        }

        return EngineActionResult.Success();
    }

    public void ResolveClaimTimeout()
    {
        if (State.Phase != HandPhase.AwaitingClaims || State.PendingClaims is null)
        {
            return;
        }

        foreach (var seat in State.PendingClaims.Possibilities.Keys)
        {
            State.PendingClaims.Choices.TryAdd(seat, new ClaimChoice(ClaimKind.Pass));
        }

        ResolvePendingClaims();
    }

    public TurnPossibilities GetTurnPossibilities(Seat seat)
    {
        if (!CanActOnOwnTurn(seat))
        {
            return new TurnPossibilities(false, [], []);
        }

        var player = State.Players[seat];
        var concealedKongs = player.Hand
            .Where(tile => !IsGold(tile))
            .GroupBy(tile => tile.Type)
            .Where(group => group.Count() == 4)
            .Select(group => group.First())
            .ToArray();

        var pengTypes = player.Melds
            .Where(meld => meld.Kind == MeldKind.Peng)
            .Select(meld => meld.Tiles[0].Type)
            .ToHashSet();

        var addedKongs = player.Hand
            .Where(tile => !IsGold(tile) && pengTypes.Contains(tile.Type))
            .ToArray();

        return new TurnPossibilities(
            WinValidator.CanWin(player.Hand, player.Melds.Count, State.GoldType),
            concealedKongs,
            addedKongs);
    }

    public ClaimPossibilities? GetClaimPossibilities(Seat seat)
    {
        if (State.Phase != HandPhase.AwaitingClaims || State.PendingClaims is null ||
            State.PendingClaims.Choices.ContainsKey(seat))
        {
            return null;
        }

        return State.PendingClaims.Possibilities.GetValueOrDefault(seat);
    }

    private void ResolvePendingClaims()
    {
        var pending = State.PendingClaims ?? throw new InvalidOperationException("No claims are pending.");
        var selectedClaims = pending.Choices
            .Where(pair => pair.Value.Kind != ClaimKind.Pass)
            .OrderBy(pair => ClaimPriority(pair.Value.Kind))
            .ThenBy(pair => SeatDistance(pending.DiscardedBy, pair.Key))
            .ToArray();

        if (selectedClaims.Length == 0)
        {
            AdvanceAfterUnclaimedDiscard(pending.DiscardedBy);
            return;
        }

        var selected = selectedClaims[0];
        var claimant = State.Players[selected.Key];
        claimant.DrawnTileId = null;

        if (selected.Value.Kind == ClaimKind.Hu)
        {
            RemoveClaimedDiscardFromTable(pending);
            FinishWin(selected.Key, selfDrawn: false, pending.DiscardedBy, pending.Discard);
            return;
        }

        RemoveClaimedDiscardFromTable(pending);
        switch (selected.Value.Kind)
        {
            case ClaimKind.Peng:
            {
                var fromHand = RemoveTilesOfType(claimant, pending.Discard.Type, 2);
                claimant.Melds.Add(new Meld(
                    MeldKind.Peng,
                    fromHand.Append(pending.Discard).ToArray(),
                    pending.DiscardedBy));
                break;
            }
            case ClaimKind.Kong:
            {
                var fromHand = RemoveTilesOfType(claimant, pending.Discard.Type, 3);
                claimant.Melds.Add(new Meld(
                    MeldKind.ExposedKong,
                    fromHand.Append(pending.Discard).ToArray(),
                    pending.DiscardedBy));
                DrawReplacementFor(claimant);
                break;
            }
            case ClaimKind.Chi:
            {
                var option = State.PendingClaims!.Possibilities[selected.Key].ChiChoices
                    .Single(choice => choice.Id == selected.Value.ChiOptionId);
                var fromHand = option.TileIds
                    .Select(id => claimant.Hand.Single(tile => tile.Id == id))
                    .ToArray();

                foreach (var tile in fromHand)
                {
                    claimant.Hand.Remove(tile);
                }

                claimant.Melds.Add(new Meld(
                    MeldKind.Chi,
                    fromHand.Append(pending.Discard).OrderBy(TileSet.SortKey).ToArray(),
                    pending.DiscardedBy));
                break;
            }
            default:
                throw new InvalidOperationException("Unsupported resolved claim.");
        }

        State.PendingClaims = null;
        State.CurrentTurn = selected.Key;
        State.Phase = HandPhase.AwaitingDiscard;
        SortHand(claimant);
    }

    private ClaimPossibilities BuildClaimPossibilities(Seat seat, PendingClaims pending)
    {
        var player = State.Players[seat];
        var matchingCount = player.Hand.Count(tile => tile.Type == pending.Discard.Type && !IsGold(tile));
        var canUseDiscardForMeld = !IsGold(pending.Discard);
        var candidateHand = player.Hand.Append(pending.Discard).ToArray();
        var canHu = !player.HasDiscardedGold &&
                    WinValidator.CanWin(candidateHand, player.Melds.Count, State.GoldType);

        return new ClaimPossibilities(
            canHu,
            canUseDiscardForMeld && matchingCount >= 2,
            canUseDiscardForMeld && matchingCount >= 3,
            seat == NextSeat(pending.DiscardedBy) && canUseDiscardForMeld
                ? BuildChiChoices(player, pending.Discard)
                : []);
    }

    private IReadOnlyList<ChiChoice> BuildChiChoices(PlayerState player, Tile discard)
    {
        if (!discard.Type.IsSuited)
        {
            return [];
        }

        var choices = new List<ChiChoice>();
        for (var startRank = discard.Type.Rank - 2; startRank <= discard.Type.Rank; startRank++)
        {
            if (startRank < 1 || startRank > 7)
            {
                continue;
            }

            var requiredTypes = Enumerable.Range(startRank, 3)
                .Where(rank => rank != discard.Type.Rank)
                .Select(rank => new TileType(discard.Type.Suit, rank))
                .ToArray();

            var selectedTiles = requiredTypes
                .Select(type => player.Hand.FirstOrDefault(tile => tile.Type == type && !IsGold(tile)))
                .ToArray();

            if (selectedTiles.All(tile => tile is not null))
            {
                choices.Add(new ChiChoice(
                    $"{discard.Type.Code}-{startRank}",
                    selectedTiles.Select(tile => tile!.Id).ToArray()));
            }
        }

        return choices;
    }

    private void AdvanceAfterUnclaimedDiscard(Seat discardedBy)
    {
        State.PendingClaims = null;
        State.CurrentTurn = NextSeat(discardedBy);
        var player = State.Players[State.CurrentTurn];

        if (!TryDrawFor(player, fromBack: false))
        {
            FinishDraw();
            return;
        }

        SortHand(player);
        State.Phase = HandPhase.AwaitingDiscard;
    }

    private bool TryDrawFor(PlayerState player, bool fromBack)
    {
        player.DrawnTileId = null;
        while (State.Wall.Count > 0)
        {
            var tile = fromBack ? DrawBack() : DrawFront();
            if (tile.Type.IsBonus)
            {
                player.Flowers.Add(tile);
                fromBack = true;
                continue;
            }

            player.Hand.Add(tile);
            player.DrawnTileId = tile.Id;
            return true;
        }

        return false;
    }

    private void DrawReplacementFor(PlayerState player)
    {
        if (!TryDrawFor(player, fromBack: true))
        {
            FinishDraw();
        }
    }

    private void ReplaceAllBonusTiles(PlayerState player)
    {
        while (true)
        {
            var bonus = player.Hand.FirstOrDefault(tile => tile.Type.IsBonus);
            if (bonus is null)
            {
                return;
            }

            player.Hand.Remove(bonus);
            player.Flowers.Add(bonus);
            if (!TryDrawFor(player, fromBack: true))
            {
                throw new InvalidOperationException("The wall ran out while replacing initial flowers.");
            }
        }
    }

    private void OpenGoldIndicator()
    {
        while (State.Wall.Count > 0)
        {
            var candidate = DrawBack();
            if (candidate.Type.IsBonus)
            {
                State.Players[State.Dealer].Flowers.Add(candidate);
                continue;
            }

            State.GoldIndicator = candidate;
            return;
        }

        throw new InvalidOperationException("The wall did not contain a suited Gold indicator.");
    }

    private void FinishWin(Seat winnerSeat, bool selfDrawn, Seat? discardedBy, Tile? winningDiscard)
    {
        var winner = State.Players[winnerSeat];
        var goldCount = winner.Hand.Count(IsGold) + (winningDiscard is not null && IsGold(winningDiscard) ? 1 : 0);
        var exposedKongs = winner.Melds.Count(meld => meld.Kind == MeldKind.ExposedKong);
        var concealedKongs = winner.Melds.Count(meld => meld.Kind == MeldKind.ConcealedKong);

        var items = new List<string>
        {
            $"底分 {_options.BaseHandPoints}",
            $"花牌 {winner.Flowers.Count}",
            $"金牌 {goldCount}"
        };

        var points = _options.BaseHandPoints + winner.Flowers.Count + goldCount;
        if (exposedKongs > 0)
        {
            items.Add($"明杠 {exposedKongs}");
            points += exposedKongs;
        }

        if (concealedKongs > 0)
        {
            items.Add($"暗杠 {concealedKongs} × 2");
            points += concealedKongs * 2;
        }

        if (goldCount >= 3)
        {
            items.Add("三金倒");
            points *= 2;
        }

        var payments = State.Players.Keys.ToDictionary(seat => seat, _ => 0);
        foreach (var loserSeat in State.Players.Keys.Where(seat => seat != winnerSeat))
        {
            var payment = !selfDrawn && loserSeat == discardedBy ? points * 2 : points;
            State.Players[loserSeat].Score -= payment;
            payments[loserSeat] = -payment;
            payments[winnerSeat] += payment;
        }

        winner.Score += payments[winnerSeat];
        var summary = selfDrawn
            ? $"{winner.Player.DisplayName} 自摸"
            : $"{winner.Player.DisplayName} 和牌";

        State.LastHandScore = new HandScore(false, winnerSeat, points, summary, items, payments);
        FinishHand(winnerSeat);
    }

    private void FinishDraw()
    {
        State.LastHandScore = new HandScore(
            true,
            null,
            0,
            "牌墙已摸完，本局流局",
            [],
            State.Players.Keys.ToDictionary(seat => seat, _ => 0));
        FinishHand(winner: null);
    }

    private void FinishHand(Seat? winner)
    {
        State.PendingClaims = null;
        State.Phase = HandPhase.Ended;

        // The next player counterclockwise from the winner opens the next hand.
        // After a draw, rotate from the current dealer so every seat progresses.
        State.Dealer = NextSeat(winner ?? State.Dealer);
    }

    private bool CanActOnOwnTurn(Seat seat) =>
        State.Phase == HandPhase.AwaitingDiscard && State.CurrentTurn == seat;

    private bool IsGold(Tile tile) => State.GoldType is not null && tile.Type == State.GoldType.Value;

    private static bool IsValidChoice(ClaimChoice choice, ClaimPossibilities possibilities) =>
        choice.Kind switch
        {
            ClaimKind.Pass => true,
            ClaimKind.Hu => possibilities.CanHu,
            ClaimKind.Peng => possibilities.CanPeng,
            ClaimKind.Kong => possibilities.CanKong,
            ClaimKind.Chi => possibilities.ChiChoices.Any(option => option.Id == choice.ChiOptionId),
            _ => false
        };

    private static int ClaimPriority(ClaimKind kind) => kind switch
    {
        ClaimKind.Hu => 0,
        ClaimKind.Kong => 1,
        ClaimKind.Peng => 1,
        ClaimKind.Chi => 2,
        _ => 3
    };

    private static int SeatDistance(Seat from, Seat to) => ((int)to - (int)from + 4) % 4;

    private static Seat NextSeat(Seat seat) => (Seat)(((int)seat + 1) % 4);

    private static IEnumerable<Seat> SeatsInOrderFrom(Seat first)
    {
        for (var offset = 0; offset < 4; offset++)
        {
            yield return (Seat)(((int)first + offset) % 4);
        }
    }

    private static IEnumerable<Seat> OtherSeatsAfter(Seat first) =>
        SeatsInOrderFrom(NextSeat(first)).Take(3);

    private Tile DrawFront()
    {
        var tile = State.Wall[0];
        State.Wall.RemoveAt(0);
        return tile;
    }

    private Tile DrawBack()
    {
        var index = State.Wall.Count - 1;
        var tile = State.Wall[index];
        State.Wall.RemoveAt(index);
        return tile;
    }

    private static IReadOnlyList<Tile> RemoveTilesOfType(PlayerState player, TileType type, int count)
    {
        var tiles = player.Hand.Where(tile => tile.Type == type).Take(count).ToArray();
        if (tiles.Length != count)
        {
            throw new InvalidOperationException("The validated claim no longer has the required tiles.");
        }

        foreach (var tile in tiles)
        {
            player.Hand.Remove(tile);
        }

        return tiles;
    }

    private void RemoveClaimedDiscardFromTable(PendingClaims pending)
    {
        State.Players[pending.DiscardedBy].Discards.Remove(pending.Discard);
    }

    private static void SortHand(PlayerState player) => player.Hand.Sort(
        (left, right) => TileSet.SortKey(left).CompareTo(TileSet.SortKey(right)));

    private void SortAllHands()
    {
        foreach (var player in State.Players.Values)
        {
            SortHand(player);
        }
    }
}

