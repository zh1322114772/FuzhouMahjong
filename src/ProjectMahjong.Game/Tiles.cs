using ProjectMahjong.Contracts;
using System.Globalization;

namespace ProjectMahjong.Game;

public readonly record struct TileType(TileSuit Suit, int Rank)
{
    public bool IsSuited => Suit is TileSuit.Characters or TileSuit.Dots or TileSuit.Bamboo;

    public bool IsBonus => !IsSuited;

    public string Code => Suit switch
    {
        TileSuit.Characters => $"m{Rank}",
        TileSuit.Dots => $"p{Rank}",
        TileSuit.Bamboo => $"s{Rank}",
        TileSuit.Wind => $"w{Rank}",
        TileSuit.Dragon => $"d{Rank}",
        TileSuit.Flower => $"f{Rank}",
        _ => throw new ArgumentOutOfRangeException()
    };

    public string Label => Suit switch
    {
        TileSuit.Characters => $"{ChineseNumber(Rank)}万",
        TileSuit.Dots => $"{ChineseNumber(Rank)}筒",
        TileSuit.Bamboo => $"{ChineseNumber(Rank)}条",
        TileSuit.Wind => Rank switch { 1 => "东风", 2 => "南风", 3 => "西风", _ => "北风" },
        TileSuit.Dragon => Rank switch { 1 => "红中", 2 => "发财", _ => "白板" },
        TileSuit.Flower => Rank switch
        {
            1 => "梅", 2 => "兰", 3 => "竹", 4 => "菊",
            5 => "春", 6 => "夏", 7 => "秋", _ => "冬"
        },
        _ => throw new ArgumentOutOfRangeException()
    };

    private static string ChineseNumber(int value) =>
        value switch
        {
            1 => "一", 2 => "二", 3 => "三", 4 => "四", 5 => "五",
            6 => "六", 7 => "七", 8 => "八", 9 => "九", _ => value.ToString(CultureInfo.InvariantCulture)
        };
}

public sealed record Tile(int Id, TileType Type)
{
    public TileDto ToDto() => new(Id, Type.Suit, Type.Rank, Type.Code, Type.Label);
}

public static class TileSet
{
    public static List<Tile> Create()
    {
        var tiles = new List<Tile>(144);
        var id = 1;

        foreach (var suit in new[] { TileSuit.Characters, TileSuit.Dots, TileSuit.Bamboo })
        {
            for (var rank = 1; rank <= 9; rank++)
            {
                for (var copy = 0; copy < 4; copy++)
                {
                    tiles.Add(new Tile(id++, new TileType(suit, rank)));
                }
            }
        }

        AddCopies(tiles, TileSuit.Wind, 4, 4, ref id);
        AddCopies(tiles, TileSuit.Dragon, 3, 4, ref id);
        AddCopies(tiles, TileSuit.Flower, 8, 1, ref id);
        return tiles;
    }

    public static int SortKey(Tile tile) => ((int)tile.Type.Suit * 10) + tile.Type.Rank;

    private static void AddCopies(
        ICollection<Tile> tiles,
        TileSuit suit,
        int rankCount,
        int copies,
        ref int id)
    {
        for (var rank = 1; rank <= rankCount; rank++)
        {
            for (var copy = 0; copy < copies; copy++)
            {
                tiles.Add(new Tile(id++, new TileType(suit, rank)));
            }
        }
    }
}

