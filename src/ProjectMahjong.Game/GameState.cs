using ProjectMahjong.Contracts;

namespace ProjectMahjong.Game;

public sealed record GamePlayer(Guid Id, string DisplayName, Seat Seat);

public sealed class PlayerState(GamePlayer player, int startingScore)
{
    public GamePlayer Player { get; } = player;

    public List<Tile> Hand { get; } = [];

    public List<Tile> Flowers { get; } = [];

    public List<Meld> Melds { get; } = [];

    public List<Tile> Discards { get; } = [];

    /// <summary>
    /// The tile most recently drawn from the wall. The client renders this tile
    /// separately at the far right until the player discards.
    /// </summary>
    public int? DrawnTileId { get; set; }

    public int Score { get; set; } = startingScore;

    public bool HasDiscardedGold { get; set; }
}

public sealed record Meld(MeldKind Kind, IReadOnlyList<Tile> Tiles, Seat? FromSeat);

public enum HandPhase
{
    NotStarted,
    AwaitingDiscard,
    AwaitingClaims,
    Ended
}

public enum ClaimKind
{
    Pass,
    Chi,
    Peng,
    Kong,
    Hu
}

public sealed record ClaimChoice(ClaimKind Kind, string? ChiOptionId = null);

public sealed class PendingClaims(Tile discard, Seat discardedBy)
{
    public Tile Discard { get; } = discard;

    public Seat DiscardedBy { get; } = discardedBy;

    public Dictionary<Seat, ClaimPossibilities> Possibilities { get; } = [];

    public Dictionary<Seat, ClaimChoice> Choices { get; } = [];
}

public sealed record ChiChoice(string Id, IReadOnlyList<int> TileIds);

public sealed record ClaimPossibilities(
    bool CanHu,
    bool CanPeng,
    bool CanKong,
    IReadOnlyList<ChiChoice> ChiChoices)
{
    public bool HasAny => CanHu || CanPeng || CanKong || ChiChoices.Count > 0;
}

public sealed record HandScore(
    bool WasDraw,
    Seat? Winner,
    int Points,
    string Summary,
    IReadOnlyList<string> Items,
    IReadOnlyDictionary<Seat, int> Payments);

public sealed class GameState(
    IReadOnlyList<GamePlayer> players,
    Seat dealer,
    FuzhouRuleOptions options)
{
    public Dictionary<Seat, PlayerState> Players { get; } = players.ToDictionary(
        player => player.Seat,
        player => new PlayerState(player, options.StartingScore));

    public List<Tile> Wall { get; } = [];

    public Tile? GoldIndicator { get; set; }

    public TileType? GoldType => GoldIndicator?.Type;

    public Seat Dealer { get; set; } = dealer;

    public Seat CurrentTurn { get; set; } = dealer;

    public int HandNumber { get; set; }

    public HandPhase Phase { get; set; } = HandPhase.NotStarted;

    public PendingClaims? PendingClaims { get; set; }

    public Tile? LastDiscard { get; set; }

    public Seat? LastDiscardedBy { get; set; }

    public HandScore? LastHandScore { get; set; }
}

