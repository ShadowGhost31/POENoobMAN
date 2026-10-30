namespace PoeNoobMan.Core.Guides;

/// <summary>Гайд, відфільтрований за обраними опціями (гілками і шляхами).</summary>
public sealed class GuideView
{
    private GuideView(Guide guide, IReadOnlyDictionary<string, string> options)
    {
        Guide = guide;
        Options = options;
        Steps = guide.Steps.Where(s => Matches(s.If, options)).ToList();
        Nodes = guide.Tree.Where(n => Matches(n.If, options)).ToList();
        Splits = guide.Splits.Where(s => Matches(s.If, options)).ToList();
    }

    public Guide Guide { get; }
    public IReadOnlyDictionary<string, string> Options { get; }
    public IReadOnlyList<GuideStep> Steps { get; }
    public IReadOnlyList<TreeNode> Nodes { get; }
    public IReadOnlyList<SplitDef> Splits { get; }

    /// <summary>Обрані опції; для відсутніх або невідомих береться default або перший варіант.</summary>
    public static GuideView Create(Guide guide, IReadOnlyDictionary<string, string>? selected)
    {
        var resolved = new Dictionary<string, string>();
        foreach (var o in guide.Options)
        {
            if (selected is not null && selected.TryGetValue(o.Id, out var v) && o.Choices.Any(c => c.Id == v))
                resolved[o.Id] = v;
            else if ((o.Default ?? o.Choices.FirstOrDefault()?.Id) is { } d)
                resolved[o.Id] = d;
        }
        return new GuideView(guide, resolved);
    }

    public static bool Matches(Dictionary<string, string>? filter, IReadOnlyDictionary<string, string> options)
    {
        if (filter is null) return true;
        foreach (var (key, value) in filter)
        {
            if (!options.TryGetValue(key, out var chosen)) return false;
            if (!value.Split('|', StringSplitOptions.TrimEntries).Contains(chosen)) return false;
        }
        return true;
    }
}
