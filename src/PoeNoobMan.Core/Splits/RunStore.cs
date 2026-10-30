using PoeNoobMan.Core.Storage;

namespace PoeNoobMan.Core.Splits;

/// <summary>runs/current.json — поточний забіг; runs/&lt;guideId&gt;/&lt;id&gt;.json — архів.</summary>
public sealed class RunStore(string dir)
{
    private string CurrentPath => Path.Combine(dir, "current.json");

    public RunRecord? LoadCurrent() => JsonStore.LoadOrNull<RunRecord>(CurrentPath);

    public void SaveCurrent(RunRecord? run)
    {
        if (run is null)
        {
            if (File.Exists(CurrentPath)) File.Delete(CurrentPath);
            return;
        }
        JsonStore.Save(CurrentPath, run);
    }

    /// <summary>Зберігає забіг в архів, якщо в ньому є хоч один спліт.</summary>
    public void Archive(RunRecord run)
    {
        if (!run.Splits.Any(s => s.Seconds is not null)) return;
        JsonStore.Save(Path.Combine(dir, AppPaths.SafeFileName(run.GuideId), AppPaths.SafeFileName(run.Id) + ".json"), run);
    }

    public List<RunRecord> LoadHistory(string guideId)
    {
        var folder = Path.Combine(dir, AppPaths.SafeFileName(guideId));
        if (!Directory.Exists(folder)) return [];
        return Directory.EnumerateFiles(folder, "*.json")
            .Select(JsonStore.LoadOrNull<RunRecord>)
            .OfType<RunRecord>()
            .OrderBy(r => r.StartedAt)
            .ToList();
    }
}
