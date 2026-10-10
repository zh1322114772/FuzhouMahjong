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

    /// <summary>Time allowed for either a turn action or a discard claim.</summary>
    public int ActionTimeoutSeconds { get; init; } = 60;

    public int EmptyRoomLifetimeMinutes { get; init; } = 30;
}

