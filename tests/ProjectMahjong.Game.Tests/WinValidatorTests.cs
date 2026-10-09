using ProjectMahjong.Contracts;
using ProjectMahjong.Game;

namespace ProjectMahjong.Game.Tests;

public sealed class WinValidatorTests
{
    [Fact]
    public void CanWin_AcceptsFiveMeldsAndAPair()
    {
        var hand = Tiles(
            "m1", "m2", "m3",
            "m4", "m5", "m6",
            "p1", "p2", "p3",
            "p7", "p8", "p9",
            "s5", "s5", "s5",
            "s9", "s9");

        Assert.True(WinValidator.CanWin(hand, exposedMeldCount: 0, goldType: null));
    }

    [Fact]
    public void CanWin_UsesGoldAsAWildcard()
    {
        var hand = Tiles(
            "m1", "m2", "g",
            "m4", "m5", "m6",
            "p1", "p2", "p3",
            "p7", "p8", "p9",
            "s5", "s5", "s5",
            "s9", "s9");
        var goldType = new TileType(TileSuit.Characters, 9);

        Assert.True(WinValidator.CanWin(hand, exposedMeldCount: 0, goldType));
    }

    [Fact]
    public void CanWin_UsesGoldBeforeTheFirstNaturalTileInASequence()
    {
        var hand = Tiles(
            "g", "m2", "m3",
            "m4", "m5", "m6",
            "p1", "p2", "p3",
            "p7", "p8", "p9",
            "s5", "s5", "s5",
            "s8", "s8");
        var goldType = new TileType(TileSuit.Characters, 9);

        Assert.True(WinValidator.CanWin(hand, exposedMeldCount: 0, goldType));
    }

    [Fact]
    public void CanWin_AcceptsThreeGoldsSpecialWin()
    {
        var hand = Tiles(
            "g", "g", "g",
            "m1", "m4", "m7",
            "p1", "p4", "p7",
            "s1", "s4", "s7",
            "m2", "p2", "s2",
            "m3", "p3");
        var goldType = new TileType(TileSuit.Characters, 9);

        Assert.True(WinValidator.CanWin(hand, exposedMeldCount: 0, goldType));
    }

    [Fact]
    public void CanWin_RejectsAnIncompleteShape()
    {
        var hand = Tiles(
            "m1", "m2", "m4",
            "m4", "m5", "m7",
            "p1", "p2", "p4",
            "p7", "p8", "p9",
            "s5", "s5", "s5",
            "s8", "s9");

        Assert.False(WinValidator.CanWin(hand, exposedMeldCount: 0, goldType: null));
    }

    private static Tile[] Tiles(params string[] codes)
    {
        var goldType = new TileType(TileSuit.Characters, 9);
        return codes.Select((code, index) => new Tile(index + 1, code == "g" ? goldType : Parse(code))).ToArray();
    }

    private static TileType Parse(string code)
    {
        var suit = code[0] switch
        {
            'm' => TileSuit.Characters,
            'p' => TileSuit.Dots,
            's' => TileSuit.Bamboo,
            _ => throw new ArgumentOutOfRangeException(nameof(code))
        };
        return new TileType(suit, int.Parse(code[1..], System.Globalization.CultureInfo.InvariantCulture));
    }
}

