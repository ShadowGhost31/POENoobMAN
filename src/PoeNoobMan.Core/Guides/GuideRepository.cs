namespace PoeNoobMan.Core.Guides;

public sealed record GuideEntry(string Id, string Version, string Title, GuideLoadResult Result)
{
    public Guide? Guide => Result.IsValid ? Result.Guide : null;
}

/// <summary>
/// Збирає гайди з кількох папок (*.yaml, *.yml). Пізніші папки перекривають ранні,
/// якщо збігаються id і версія. Кожен новий файл — нова версія у списку.
/// </summary>
public sealed class GuideRepository
{
    private readonly string[] _dirs;
    private List<GuideEntry> _entries = [];
    private List<GuideLoadResult> _broken = [];

    public GuideRepository(params string[] dirs) => _dirs = dirs;

    public IReadOnlyList<GuideEntry> Entries => _entries;

    /// <summary>Файли, які не вдалося прочитати або які не пройшли перевірку.</summary>
    public IReadOnlyList<GuideLoadResult> Broken => _broken;

    public void Reload()
    {
        var byKey = new Dictionary<(string, string), GuideEntry>();
        var broken = new List<GuideLoadResult>();
        foreach (var dir in _dirs.Where(Directory.Exists))
        {
            var files = Directory.EnumerateFiles(dir, "*.yaml").Concat(Directory.EnumerateFiles(dir, "*.yml"));
            foreach (var file in files.Order(StringComparer.OrdinalIgnoreCase))
            {
                var result = GuideLoader.LoadFile(file);
                if (!result.IsValid)
                {
                    broken.Add(result);
                    continue;
                }
                var g = result.Guide!;
                byKey[(g.Id, g.Version)] = new GuideEntry(g.Id, g.Version, string.IsNullOrWhiteSpace(g.Title) ? g.Id : g.Title, result);
            }
        }
        _entries = byKey.Values.OrderBy(e => e.Title).ThenByDescending(e => e.Version, VersionComparer.Instance).ToList();
        _broken = broken;
    }

    public IReadOnlyList<string> GuideIds => _entries.Select(e => e.Id).Distinct().ToList();

    public IReadOnlyList<GuideEntry> Versions(string id) =>
        _entries.Where(e => e.Id == id).OrderByDescending(e => e.Version, VersionComparer.Instance).ToList();

    public GuideEntry? Find(string? id, string? version)
    {
        if (id is null) return null;
        var versions = Versions(id);
        return versions.FirstOrDefault(e => e.Version == version) ?? versions.FirstOrDefault();
    }
}

/// <summary>Порівнює "3.29.10" і "3.29.2" як числа, решту — як рядки.</summary>
public sealed class VersionComparer : IComparer<string>
{
    public static readonly VersionComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        var a = (x ?? "").Split('.', '-');
        var b = (y ?? "").Split('.', '-');
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var pa = i < a.Length ? a[i] : "0";
            var pb = i < b.Length ? b[i] : "0";
            var c = int.TryParse(pa, out var na) && int.TryParse(pb, out var nb)
                ? na.CompareTo(nb)
                : string.Compare(pa, pb, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
        }
        return 0;
    }
}
