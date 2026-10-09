using ProjectMahjong.Contracts;
using ProjectMahjong.Game;

namespace ProjectMahjong.Game.Tests;

public sealed class TileSetTests
{
    [Fact]
    public void Create_ReturnsACompleteUniqueFuzhouSet()
    {
        var tiles = TileSet.Create();

        Assert.Equal(144, tiles.Count);
        Assert.Equal(144, tiles.Select(tile => tile.Id).Distinct().Count());
        Assert.Equal(108, tiles.Count(tile => tile.Type.IsSuited));
        Assert.Equal(28, tiles.Count(tile => tile.Type.Suit is TileSuit.Wind or TileSuit.Dragon));
        Assert.Equal(8, tiles.Count(tile => tile.Type.Suit == TileSuit.Flower));
    }
}

