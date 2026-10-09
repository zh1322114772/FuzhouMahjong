using ProjectMahjong.Contracts;

namespace ProjectMahjong.Game;

public static class WinValidator
{
    public static bool CanWin(
        IReadOnlyCollection<Tile> concealedTiles,
        int exposedMeldCount,
        TileType? goldType)
    {
        var requiredMelds = 5 - exposedMeldCount;
        if (requiredMelds < 0 || concealedTiles.Count != (requiredMelds * 3) + 2)
        {
            return false;
        }

        var goldCount = goldType is null
            ? 0
            : concealedTiles.Count(tile => tile.Type == goldType.Value);

        // 三金倒: three or more Gold tiles form an immediate winning hand.
        if (goldCount >= 3)
        {
            return true;
        }

        var counts = new int[27];
        foreach (var tile in concealedTiles)
        {
            if (goldType is not null && tile.Type == goldType.Value)
            {
                continue;
            }

            if (!tile.Type.IsSuited)
            {
                return false;
            }

            counts[ToIndex(tile.Type)]++;
        }

        // Try a natural pair.
        for (var index = 0; index < counts.Length; index++)
        {
            if (counts[index] >= 2)
            {
                counts[index] -= 2;
                if (CanFormMelds(counts, goldCount, requiredMelds))
                {
                    return true;
                }

                counts[index] += 2;
            }
        }

        // Try one natural tile plus one Gold as the pair.
        if (goldCount >= 1)
        {
            for (var index = 0; index < counts.Length; index++)
            {
                if (counts[index] == 0)
                {
                    continue;
                }

                counts[index]--;
                if (CanFormMelds(counts, goldCount - 1, requiredMelds))
                {
                    return true;
                }

                counts[index]++;
            }
        }

        // Try two Gold tiles as the pair.
        return goldCount >= 2 && CanFormMelds(counts, goldCount - 2, requiredMelds);
    }

    private static bool CanFormMelds(int[] counts, int goldCount, int meldsRemaining)
    {
        if (meldsRemaining == 0)
        {
            return counts.All(count => count == 0) && goldCount == 0;
        }

        var first = Array.FindIndex(counts, count => count > 0);
        if (first < 0)
        {
            return goldCount == meldsRemaining * 3;
        }

        // Try a triplet containing the first remaining tile.
        var naturalTripletTiles = Math.Min(3, counts[first]);
        var goldsForTriplet = 3 - naturalTripletTiles;
        if (goldsForTriplet <= goldCount)
        {
            counts[first] -= naturalTripletTiles;
            if (CanFormMelds(counts, goldCount - goldsForTriplet, meldsRemaining - 1))
            {
                counts[first] += naturalTripletTiles;
                return true;
            }

            counts[first] += naturalTripletTiles;
        }

        // Try every sequence position that can contain the first tile. This matters
        // when Gold fills a lower tile, for example Gold + 2 + 3.
        var rank = (first % 9) + 1;
        var suitStart = first - (rank - 1);
        for (var startRank = Math.Max(1, rank - 2); startRank <= Math.Min(7, rank); startRank++)
        {
            var used = new List<int>(3);
            var goldsForSequence = 0;

            for (var sequenceRank = startRank; sequenceRank <= startRank + 2; sequenceRank++)
            {
                var index = suitStart + sequenceRank - 1;
                if (counts[index] > 0)
                {
                    counts[index]--;
                    used.Add(index);
                }
                else
                {
                    goldsForSequence++;
                }
            }

            var sequenceWorks = goldsForSequence <= goldCount &&
                                CanFormMelds(counts, goldCount - goldsForSequence, meldsRemaining - 1);

            foreach (var index in used)
            {
                counts[index]++;
            }

            if (sequenceWorks)
            {
                return true;
            }
        }

        return false;
    }

    private static int ToIndex(TileType type)
    {
        var suitOffset = type.Suit switch
        {
            TileSuit.Characters => 0,
            TileSuit.Dots => 9,
            TileSuit.Bamboo => 18,
            _ => throw new ArgumentException("Only suited tiles belong in a winning hand.", nameof(type))
        };

        return suitOffset + type.Rank - 1;
    }
}

