namespace ProjectMahjong.Server.Rooms;

public sealed class RoomCleanupService(
    RoomRegistry rooms,
    ILogger<RoomCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await rooms.CleanupExpiredRoomsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Room cleanup service stopped");
        }
    }
}
