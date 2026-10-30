namespace PoeNoobMan.Core.Storage;

/// <summary>Розташування файлів у ~/.poe-helper.</summary>
public static class AppPaths
{
    public static string Root { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".poe-helper");

    public static string Settings => Path.Combine(Root, "settings.json");
    public static string LogState => Path.Combine(Root, "log-state.json");
    public static string GuidesDir => Path.Combine(Root, "guides");
    public static string ProgressDir => Path.Combine(Root, "progress");
    public static string RunsDir => Path.Combine(Root, "runs");
    public static string AppLog => Path.Combine(Root, "app.log");

    public static string SafeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "_default";
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
