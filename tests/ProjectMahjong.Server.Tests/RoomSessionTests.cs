using ProjectMahjong.Contracts;
using ProjectMahjong.Game;
using ProjectMahjong.Server.Rooms;

namespace ProjectMahjong.Server.Tests;

public sealed class RoomSessionTests
{
    [Fact]
    public async Task JoinAsync_AllowsExactlyFourPlayers()
    {
        using var room = new RoomSession("ABC234", "房主", new FuzhouRuleOptions());

        Assert.NotNull((await room.JoinAsync("玩家二")).Session);
        Assert.NotNull((await room.JoinAsync("玩家三")).Session);
        Assert.NotNull((await room.JoinAsync("玩家四")).Session);
        var fifth = await room.JoinAsync("玩家五");

        Assert.Null(fifth.Session);
        Assert.Equal("房间已经坐满四人。", fifth.Error);
    }

    [Fact]
    public async Task StartGame_RequiresEveryoneReadyAndKeepsHandsPrivate()
    {
        using var room = new RoomSession("ABC234", "东家", new FuzhouRuleOptions());
        var host = await room.GetHostSessionAsync();
        var south = (await room.JoinAsync("南家")).Session!;
        var west = (await room.JoinAsync("西家")).Session!;
        var north = (await room.JoinAsync("北家")).Session!;

        foreach (var playerId in new[] { host.PlayerId, south.PlayerId, west.PlayerId, north.PlayerId })
        {
            Assert.True((await room.SetReadyAsync(playerId, true)).Accepted);
        }

        Assert.True((await room.StartGameAsync(host.PlayerId)).Accepted);
        var eastSnapshot = await room.GetSnapshotAsync(host.PlayerId);
        var southSnapshot = await room.GetSnapshotAsync(south.PlayerId);

        Assert.NotNull(eastSnapshot);
        Assert.NotNull(southSnapshot);
        Assert.Equal(RoomStage.Playing, eastSnapshot.Stage);
        Assert.Equal(17, eastSnapshot.MyHand.Count);
        Assert.Equal(16, southSnapshot.MyHand.Count);
        Assert.All(eastSnapshot.Players.Where(player => player.PlayerId != host.PlayerId), player =>
            Assert.True(player.ConcealedTileCount >= 16));
    }

    [Fact]
    public async Task RejoinAsync_RejectsAnInvalidRecoveryToken()
    {
        using var room = new RoomSession("ABC234", "东家", new FuzhouRuleOptions());
        var host = await room.GetHostSessionAsync();

        var result = await room.RejoinAsync(host.PlayerId, "00112233445566778899AABBCCDDEEFF");

        Assert.Null(result.Session);
        Assert.Equal("恢复码无效，无法找回座位。", result.Error);
    }

    [Fact]
    public async Task Discard_IsVisibleAsTheLastDiscardToEveryPlayer()
    {
        using var room = new RoomSession("ABC234", "东家", new FuzhouRuleOptions());
        var east = await room.GetHostSessionAsync();
        var south = (await room.JoinAsync("南家")).Session!;
        var west = (await room.JoinAsync("西家")).Session!;
        var north = (await room.JoinAsync("北家")).Session!;

        foreach (var player in new[] { east, south, west, north })
        {
            await room.SetReadyAsync(player.PlayerId, true);
        }

        await room.StartGameAsync(east.PlayerId);
        var eastBefore = (await room.GetSnapshotAsync(east.PlayerId))!;
        var discarded = eastBefore.MyHand[0];

        Assert.True((await room.DiscardAsync(
            east.PlayerId,
            discarded.Id,
            Guid.NewGuid(),
            eastBefore.Revision)).Accepted);

        foreach (var player in new[] { east, south, west, north })
        {
            var snapshot = (await room.GetSnapshotAsync(player.PlayerId))!;
            Assert.Equal(discarded.Id, snapshot.LastDiscard?.Id);
            Assert.Equal(Seat.East, snapshot.LastDiscardFrom);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task StartGame_FillsEmptySeatsWithBots(int humanCount)
    {
        using var room = new RoomSession("ABC234", "房主", new FuzhouRuleOptions());
        var humans = new List<PlayerSessionDto> { await room.GetHostSessionAsync() };

        for (var index = 1; index < humanCount; index++)
        {
            humans.Add((await room.JoinAsync($"玩家{index + 1}")).Session!);
        }

        foreach (var human in humans)
        {
            Assert.True((await room.SetReadyAsync(human.PlayerId, true)).Accepted);
        }

        Assert.True((await room.StartGameAsync(humans[0].PlayerId)).Accepted);
        var snapshot = await room.GetSnapshotAsync(humans[0].PlayerId);

        Assert.NotNull(snapshot);
        Assert.Equal(RoomStage.Playing, snapshot.Stage);
        Assert.Equal(4, snapshot.Players.Count);
        Assert.Equal(4 - humanCount, snapshot.Players.Count(player => player.IsBot));
        Assert.All(snapshot.Players.Where(player => player.IsBot), bot =>
        {
            Assert.True(bot.IsConnected);
            Assert.True(bot.IsReady);
            Assert.StartsWith("电脑·", bot.DisplayName);
        });
    }

    [Fact]
    public async Task RunBotsAsync_StopsWhenTheHumanNeedsToAct()
    {
        using var room = new RoomSession("ABC234", "房主", new FuzhouRuleOptions());
        var host = await room.GetHostSessionAsync();
        await room.SetReadyAsync(host.PlayerId, true);
        await room.StartGameAsync(host.PlayerId);
        var before = (await room.GetSnapshotAsync(host.PlayerId))!;
        var discard = before.MyHand.First(tile => tile.Code != before.GoldTile?.Code);

        var mutation = await room.DiscardAsync(host.PlayerId, discard.Id, Guid.NewGuid(), before.Revision);
        Assert.True(mutation.Accepted);

        await room.RunBotsAsync();
        var after = (await room.GetSnapshotAsync(host.PlayerId))!;

        Assert.True(after.Revision > before.Revision);
        Assert.True(
            after.Stage is RoomStage.Playing or RoomStage.WaitingForClaims or RoomStage.HandEnded,
            $"Unexpected stage after bot turns: {after.Stage}");
        if (after.Stage is RoomStage.Playing)
        {
            Assert.Equal(Seat.East, after.CurrentTurn);
        }
        else if (after.Stage is RoomStage.WaitingForClaims)
        {
            Assert.True(after.AvailableActions.CanPass);
        }
    }

    [Fact]
    public async Task EveryActionReceivesADeadline()
    {
        using var room = new RoomSession("123456", "房主", new FuzhouRuleOptions
        {
            ActionTimeoutSeconds = 60
        });
        var host = await room.GetHostSessionAsync();
        await room.SetReadyAsync(host.PlayerId, true);

        Assert.True((await room.StartGameAsync(host.PlayerId)).Accepted);
        var snapshot = (await room.GetSnapshotAsync(host.PlayerId))!;

        Assert.NotNull(snapshot.ActionDeadlineUtc);
        Assert.InRange(
            snapshot.ActionDeadlineUtc.Value - DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(55),
            TimeSpan.FromSeconds(61));
    }

    [Fact]
    public async Task DisconnectedHumanIsTemporarilyAutomated()
    {
        using var room = new RoomSession("123456", "东家", new FuzhouRuleOptions());
        var east = await room.GetHostSessionAsync();
        var south = (await room.JoinAsync("南家")).Session!;
        await room.ConnectAsync(east.PlayerId, east.ReconnectToken, "east-connection");
        await room.ConnectAsync(south.PlayerId, south.ReconnectToken, "south-connection");
        await room.SetReadyAsync(east.PlayerId, true);
        await room.SetReadyAsync(south.PlayerId, true);
        await room.StartGameAsync(east.PlayerId);
        var before = (await room.GetSnapshotAsync(south.PlayerId))!;

        Assert.True(await room.DisconnectAsync(east.PlayerId, "east-connection"));
        await room.RunBotsAsync();
        var after = (await room.GetSnapshotAsync(south.PlayerId))!;

        Assert.False(after.Players.Single(player => player.PlayerId == east.PlayerId).IsConnected);
        Assert.True(after.Revision > before.Revision);
        Assert.True(
            after.CurrentTurn != Seat.East || after.PendingDiscardFrom == Seat.East,
            "Disconnected East should either finish the turn or be waiting for claims on its automatic discard.");
    }
}
