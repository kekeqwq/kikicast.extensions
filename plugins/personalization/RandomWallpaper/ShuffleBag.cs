namespace RandomWallpaper;

public sealed record ShuffleState(List<string> Known, List<string> Remaining);
public sealed record Selection(string? Path, ShuffleState State);
public static class ShuffleBag
{
    public static Selection Select(IReadOnlyList<string> candidates, ShuffleState? saved, IReadOnlySet<string> excluded, Action<string[]> shuffle)
    {
        var all = candidates.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var present = all.ToHashSet(StringComparer.OrdinalIgnoreCase); var known = saved?.Known.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var remaining = (saved?.Remaining ?? []).Where(present.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var additions = all.Where(x => !known.Contains(x)).ToArray(); shuffle(additions); remaining.AddRange(additions);
        var index = remaining.FindIndex(x => !excluded.Contains(x));
        if (index < 0) { var fresh = all.ToArray(); shuffle(fresh); remaining = fresh.ToList(); index = remaining.FindIndex(x => !excluded.Contains(x)); }
        if (index < 0) return new(null, new(all.ToList(), remaining));
        var selected = remaining[index]; remaining.RemoveAt(index); return new(selected, new(all.ToList(), remaining));
    }
}
