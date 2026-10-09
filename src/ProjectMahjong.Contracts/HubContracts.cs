namespace ProjectMahjong.Contracts;

public interface IGameClient
{
    Task SnapshotUpdated(RoomSnapshotDto snapshot);

    Task Notice(string message);
}

public static class GameHubRoutes
{
    public const string Path = "/hubs/game";
}
