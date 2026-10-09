namespace ProjectMahjong.Game;

public interface IRandomSource
{
    void Shuffle<T>(IList<T> items);
}

public sealed class SystemRandomSource : IRandomSource
{
    public void Shuffle<T>(IList<T> items)
    {
        for (var index = items.Count - 1; index > 0; index--)
        {
            var swapIndex = Random.Shared.Next(index + 1);
            (items[index], items[swapIndex]) = (items[swapIndex], items[index]);
        }
    }
}

public sealed class UnshuffledRandomSource : IRandomSource
{
    public void Shuffle<T>(IList<T> items)
    {
        // Useful for deterministic tests and examples.
    }
}

