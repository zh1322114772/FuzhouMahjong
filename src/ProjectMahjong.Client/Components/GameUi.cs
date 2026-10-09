using ProjectMahjong.Contracts;

namespace ProjectMahjong.Client.Components;

/// <summary>Small display helpers shared by the table components.</summary>
public static class GameUi
{
    public static string SeatName(Seat seat) => seat switch
    {
        Seat.East => "东",
        Seat.South => "南",
        Seat.West => "西",
        _ => "北"
    };

    public static bool IsGold(TileDto tile, TileDto? goldTile) =>
        goldTile is not null && tile.Code == goldTile.Code;

    public static string MeldName(MeldKind kind) => kind switch
    {
        MeldKind.Chi => "吃",
        MeldKind.Peng => "碰",
        MeldKind.ExposedKong => "明杠",
        _ => "暗杠"
    };
}
