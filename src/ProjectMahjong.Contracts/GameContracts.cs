namespace ProjectMahjong.Contracts;

public enum Seat
{
    East = 0,
    South = 1,
    West = 2,
    North = 3
}

public enum TileSuit
{
    Characters,
    Dots,
    Bamboo,
    Wind,
    Dragon,
    Flower
}

public enum MeldKind
{
    Chi,
    Peng,
    ExposedKong,
    ConcealedKong
}

public enum RoomStage
{
    Lobby,
    Playing,
    WaitingForClaims,
    HandEnded,
    MatchEnded
}

public sealed record TileDto(int Id, TileSuit Suit, int Rank, string Code, string Label);

public sealed record MeldDto(MeldKind Kind, IReadOnlyList<TileDto> Tiles, Seat? FromSeat);

public sealed record SeatSnapshotDto(
    Guid PlayerId,
    string DisplayName,
    Seat Seat,
    bool IsHost,
    bool IsBot,
    bool IsConnected,
    bool IsReady,
    int Score,
    int ConcealedTileCount,
    IReadOnlyList<TileDto> Flowers,
    IReadOnlyList<MeldDto> Melds,
    IReadOnlyList<TileDto> Discards);

public sealed record ChiOptionDto(string Id, IReadOnlyList<TileDto> TilesFromHand);

public sealed record AvailableActionsDto(
    bool CanDiscard,
    bool CanPass,
    bool CanHu,
    bool CanPeng,
    bool CanExposedKong,
    IReadOnlyList<ChiOptionDto> ChiOptions,
    IReadOnlyList<TileDto> ConcealedKongOptions,
    IReadOnlyList<TileDto> AddedKongOptions)
{
    public static AvailableActionsDto None { get; } = new(
        false, false, false, false, false, [], [], []);
}

public sealed record ScorePaymentDto(string PlayerName, int Amount);

public sealed record HandResultDto(
    bool WasDraw,
    string Summary,
    string? WinnerName,
    int HandPoints,
    IReadOnlyList<string> ScoreItems,
    IReadOnlyList<ScorePaymentDto> Payments);

public sealed record RoomSnapshotDto(
    string RoomCode,
    long Revision,
    RoomStage Stage,
    Guid CurrentPlayerId,
    Seat? CurrentTurn,
    Seat Dealer,
    int HandNumber,
    int TotalHands,
    int TilesRemaining,
    TileDto? GoldTile,
    TileDto? PendingDiscard,
    Seat? PendingDiscardFrom,
    TileDto? LastDiscard,
    Seat? LastDiscardFrom,
    DateTimeOffset? ActionDeadlineUtc,
    IReadOnlyList<SeatSnapshotDto> Players,
    IReadOnlyList<TileDto> MyHand,
    int? MyDrawnTileId,
    AvailableActionsDto AvailableActions,
    HandResultDto? HandResult,
    string StatusMessage);

