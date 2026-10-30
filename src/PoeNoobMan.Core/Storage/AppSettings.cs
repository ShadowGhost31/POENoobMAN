using PoeNoobMan.Core.Splits;

namespace PoeNoobMan.Core.Storage;

public sealed class AppSettings
{
    public string ClientLogPath { get; set; } = "";
    public string CharacterName { get; set; } = "";
    public string? GuideId { get; set; }
    public string? GuideVersion { get; set; }

    /// <summary>guideId → (optionId → choiceId).</summary>
    public Dictionary<string, Dictionary<string, string>> GuideOptions { get; set; } = new();

    public HotkeySettings Hotkeys { get; set; } = new();
    public OverlaySettings Overlay { get; set; } = new();
    public TimerSettings Timer { get; set; } = new();

    /// <summary>Посилання на GitHub-репозиторій, URL або локальна папка з релізами Velopack.</summary>
    public string UpdateSource { get; set; } = "";
    public bool CheckUpdatesOnStart { get; set; } = true;

    public Dictionary<string, string> OptionsFor(string guideId) =>
        GuideOptions.TryGetValue(guideId, out var o) ? o : GuideOptions[guideId] = new();
}

public sealed class HotkeySettings
{
    public string DetailMenu { get; set; } = "F9";
    public string MarkStep { get; set; } = "F10";
    public string UndoStep { get; set; } = "Shift+F10";
    public string MarkNode { get; set; } = "Ctrl+F10";
    public string CopyRegex { get; set; } = "F7";
    public string TimerSplit { get; set; } = "F8";
    public string TimerUndo { get; set; } = "Ctrl+F8";
    public string TimerReset { get; set; } = "Shift+F8";
}

public sealed class OverlaySettings
{
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 0.8;
    public int MaxHints { get; set; } = 4;

    /// <summary>Позиція підказок як частка від розміру вікна гри (0..1).</summary>
    public double HintsX { get; set; } = 0.01;
    public double HintsY { get; set; } = 0.30;
}

public sealed class TimerSettings
{
    public bool Enabled { get; set; } = true;
    public bool ShowNext { get; set; } = true;
    public bool AutoStart { get; set; } = true;
    public bool SameOptionsOnly { get; set; } = true;
    public Comparison Comparison { get; set; } = Comparison.PersonalBest;
    public double X { get; set; } = 0.60;
    public double Y { get; set; } = 0.01;
}
