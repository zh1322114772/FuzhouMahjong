using ProjectMahjong.Contracts;
using ProjectMahjong.Game;

namespace ProjectMahjong.Game.Tests;

public sealed class FuzhouGameTests
{
    [Fact]
    public void StartNextHand_DealsSixteenTilesAndReplacesAllBonusTiles()
    {
        var game = CreateGame();

        var result = game.StartNextHand();

        Assert.True(result.Accepted);
        Assert.Equal(17, game.State.Players[Seat.East].Hand.Count);
        Assert.All(game.State.Players.Where(pair => pair.Key != Seat.East), pair =>
            Assert.Equal(16, pair.Value.Hand.Count));
        Assert.All(game.State.Players.Values.SelectMany(player => player.Hand), tile =>
            Assert.True(tile.Type.IsSuited));
        Assert.NotNull(game.State.GoldIndicator);

        var accountedTiles = game.State.Wall.Count
            + game.State.Players.Values.Sum(player => player.Hand.Count + player.Flowers.Count)
            + 1;
        Assert.Equal(144, accountedTiles);
    }

    [Fact]
    public void Peng_HasPriorityOverChi()
    {
        var game = CreateGame();
        Assert.True(game.StartNextHand().Accepted);
        PrepareEmptyTurn(game);

        var discarded = NewTile(1, TileSuit.Characters, 5);
        game.State.Players[Seat.East].Hand.Add(discarded);
        game.State.Players[Seat.South].Hand.AddRange([
            NewTile(2, TileSuit.Characters, 4),
            NewTile(3, TileSuit.Characters, 6)
        ]);
        game.State.Players[Seat.West].Hand.AddRange([
            NewTile(4, TileSuit.Characters, 5),
            NewTile(5, TileSuit.Characters, 5)
        ]);

        Assert.True(game.Discard(Seat.East, discarded.Id).Accepted);
        Assert.True(game.SubmitClaim(Seat.South, new ClaimChoice(ClaimKind.Chi, "m5-4")).Accepted);
        Assert.True(game.SubmitClaim(Seat.West, new ClaimChoice(ClaimKind.Peng)).Accepted);

        Assert.Equal(Seat.West, game.State.CurrentTurn);
        Assert.Equal(MeldKind.Peng, Assert.Single(game.State.Players[Seat.West].Melds).Kind);
        Assert.Empty(game.State.Players[Seat.South].Melds);
    }

    [Fact]
    public void SelfDrawnWin_UpdatesAllFourScores()
    {
        var game = CreateGame();
        Assert.True(game.StartNextHand().Accepted);
        PrepareEmptyTurn(game);
        game.State.GoldIndicator = NewTile(100, TileSuit.Characters, 9);
        game.State.Players[Seat.East].Hand.AddRange(WinningHand());

        var result = game.DeclareSelfDrawnWin(Seat.East);

        Assert.True(result.Accepted);
        Assert.Equal(109, game.State.Players[Seat.East].Score);
        Assert.All(game.State.Players.Where(pair => pair.Key != Seat.East), pair =>
            Assert.Equal(97, pair.Value.Score));
        Assert.Equal(HandPhase.Ended, game.State.Phase);
        Assert.Equal(Seat.South, game.State.Dealer);
    }

    [Fact]
    public void WinnerDeterminesWhoStartsTheNextHand()
    {
        var game = CreateGame();
        Assert.True(game.StartNextHand().Accepted);
        PrepareEmptyTurn(game);
        game.State.CurrentTurn = Seat.West;
        game.State.GoldIndicator = NewTile(100, TileSuit.Characters, 9);
        game.State.Players[Seat.West].Hand.AddRange(WinningHand());

        Assert.True(game.DeclareSelfDrawnWin(Seat.West).Accepted);
        Assert.Equal(Seat.North, game.State.Dealer);

        Assert.True(game.StartNextHand().Accepted);
        Assert.Equal(Seat.North, game.State.CurrentTurn);
    }

    [Fact]
    public void DrawnTile_IsTrackedUntilThatPlayerDiscards()
    {
        var game = CreateGame();
        Assert.True(game.StartNextHand().Accepted);
        PrepareEmptyTurn(game);

        var discard = NewTile(501, TileSuit.Characters, 1);
        var drawn = NewTile(502, TileSuit.Dots, 9);
        game.State.Players[Seat.East].Hand.Add(discard);
        game.State.Wall.Clear();
        game.State.Wall.Add(drawn);

        Assert.True(game.Discard(Seat.East, discard.Id).Accepted);
        Assert.Equal(Seat.South, game.State.CurrentTurn);
        Assert.Equal(drawn.Id, game.State.Players[Seat.South].DrawnTileId);

        Assert.True(game.Discard(Seat.South, drawn.Id).Accepted);
        Assert.Null(game.State.Players[Seat.South].DrawnTileId);
    }

    private static FuzhouGame CreateGame()
    {
        var players = Enum.GetValues<Seat>()
            .Select(seat => new GamePlayer(Guid.NewGuid(), seat.ToString(), seat))
            .ToArray();
        return new FuzhouGame(players, Seat.East, new FuzhouRuleOptions(), new UnshuffledRandomSource());
    }

    private static void PrepareEmptyTurn(FuzhouGame game)
    {
        foreach (var player in game.State.Players.Values)
        {
            player.Hand.Clear();
            player.Flowers.Clear();
            player.Melds.Clear();
            player.Discards.Clear();
            player.DrawnTileId = null;
        }

        game.State.CurrentTurn = Seat.East;
        game.State.Phase = HandPhase.AwaitingDiscard;
        game.State.GoldIndicator = NewTile(999, TileSuit.Dots, 9);
    }

    private static IEnumerable<Tile> WinningHand()
    {
        var types = new[]
        {
            (TileSuit.Characters, 1), (TileSuit.Characters, 2), (TileSuit.Characters, 3),
            (TileSuit.Characters, 4), (TileSuit.Characters, 5), (TileSuit.Characters, 6),
            (TileSuit.Dots, 1), (TileSuit.Dots, 2), (TileSuit.Dots, 3),
            (TileSuit.Dots, 4), (TileSuit.Dots, 5), (TileSuit.Dots, 6),
            (TileSuit.Bamboo, 2), (TileSuit.Bamboo, 3), (TileSuit.Bamboo, 4),
            (TileSuit.Bamboo, 8), (TileSuit.Bamboo, 8)
        };
        return types.Select((type, index) => NewTile(200 + index, type.Item1, type.Item2));
    }

    private static Tile NewTile(int id, TileSuit suit, int rank) => new(id, new TileType(suit, rank));
}

