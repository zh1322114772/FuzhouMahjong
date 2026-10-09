namespace ProjectMahjong.Game;

/// <summary>
/// The intentionally small set of house rules supported by the first version.
/// Keep rule switches here instead of scattering them through the engine.
/// </summary>
public sealed record FuzhouRuleOptions
{
    public int StartingScore { get; init; } = 100;

    public int TotalHands { get; init; } = 4;

    public int BaseHandPoints { get; init; } = 3;

    public int ClaimTimeoutSeconds { get; init; } = 20;

    public int EmptyRoomLifetimeMinutes { get; init; } = 30;
}

