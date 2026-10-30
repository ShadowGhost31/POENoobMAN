using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using PoeNoobMan.App.Services;
using PoeNoobMan.App.Windows;
using PoeNoobMan.Core.Guides;
using PoeNoobMan.Core.Log;
using PoeNoobMan.Core.Progress;
using PoeNoobMan.Core.Splits;
using PoeNoobMan.Core.Storage;
using PoeNoobMan.Core.Tracking;

namespace PoeNoobMan.App;

/// <summary>Зв'язує лог, гайд, прогрес, таймер, хоткеї і вікна оверлею.</summary>
public sealed class AppController : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly ProgressStore _progressStore = new(AppPaths.ProgressDir);
    private readonly RunStore _runStore = new(AppPaths.RunsDir);
    private readonly HotkeyService _hotkeys = new();
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _guidesDebounce;
    private readonly LogState _logState;
    private FileSystemWatcher? _guidesWatcher;
    private LogWatcher? _logWatcher;

    private OverlayWindow? _overlay;
    private TimerWindow? _timer;
    private DetailWindow? _detail;
    private bool _detailOpen;
    private bool _hotkeysSuspended;
    private string? _flash;
    private DateTime _flashUntil;

    public AppController(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Settings = JsonStore.Load<AppSettings>(AppPaths.Settings);
        _logState = JsonStore.Load<LogState>(AppPaths.LogState);

        Directory.CreateDirectory(AppPaths.GuidesDir);
        Guides = new GuideRepository(Path.Combine(AppContext.BaseDirectory, "Guides"), AppPaths.GuidesDir);
        Guides.Reload();

        Game = new GameWindowTracker(dispatcher);
        Game.Changed += g =>
        {
            // Поки гри немає, час забігу не йде (той самий проміжок видно і з логу при наступному запуску гри)
            if (Tracker is { IsRunning: true } t)
            {
                if (g.Found) t.EndPause(DateTime.Now);
                else t.BeginPause(DateTime.Now);
                SaveRun();
            }
            UpdateWindows();
        };

        _clock = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal, (_, _) => OnClock(), dispatcher);
        _guidesDebounce = new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Normal, (_, _) => OnGuidesFolderChanged(), dispatcher);
    }

    public AppSettings Settings { get; private set; }
    public GuideRepository Guides { get; }
    public GameWindowTracker Game { get; }
    public UpdateService Updates { get; } = new();

    public GuideEntry? GuideEntry { get; private set; }
    public GuideView? View { get; private set; }
    public ProgressEngine? Engine { get; private set; }
    public SplitTracker? Tracker { get; private set; }

    public bool OverlayEnabled { get; private set; }
    public IReadOnlyList<string> SeenCharacters => _logState.SeenCharacters;

    /// <summary>Змінився прогрес, гайд або стан таймера.</summary>
    public event Action? Changed;

    /// <summary>У папці гайдів з'явився, змінився або зник файл.</summary>
    public event Action? GuidesChanged;

    public event Action<GameWindowInfo>? GameChanged;

    public void Start()
    {
        _overlay = new OverlayWindow(this);
        _timer = new TimerWindow(this);
        _detail = new DetailWindow(this);

        LoadGuide();
        RestartLogWatcher();
        WatchGuidesFolder();
        Game.Changed += g => GameChanged?.Invoke(g);
        Game.Start();
        _clock.Start();

        if (string.IsNullOrWhiteSpace(Settings.ClientLogPath) && Game.FindClientLog() is { } found)
        {
            Settings.ClientLogPath = found;
            SaveSettingsFile();
            RestartLogWatcher();
        }
    }

    // ---------------- Налаштування ----------------

    public AppSettings CloneSettings() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(Settings, JsonStore.Options), JsonStore.Options)!;

    /// <summary>Застосовує і зберігає налаштування. Якщо хоткеї конфліктують, нічого не зберігає.</summary>
    /// <returns>Список помилок.</returns>
    public IReadOnlyList<string> SaveSettings(AppSettings edited)
    {
        var errors = TestHotkeys(edited.Hotkeys);
        if (errors.Count > 0)
        {
            if (OverlayEnabled) RegisterHotkeys();
            return errors;
        }

        var old = Settings;
        SaveProgress();
        Settings = edited;
        SaveSettingsFile();

        if (!string.Equals(old.ClientLogPath, edited.ClientLogPath, StringComparison.OrdinalIgnoreCase)) RestartLogWatcher();
        LoadGuide();
        if (OverlayEnabled) RegisterHotkeys();
        UpdateWindows();
        return [];
    }

    private void SaveSettingsFile() => JsonStore.Save(AppPaths.Settings, Settings);

    public string? DetectClientLog() => Game.FindClientLog();

    // ---------------- Оверлей і хоткеї ----------------

    /// <returns>Помилки реєстрації хоткеїв.</returns>
    public IReadOnlyList<string> SetOverlayEnabled(bool enabled)
    {
        OverlayEnabled = enabled;
        IReadOnlyList<string> errors = [];
        if (enabled) errors = RegisterHotkeys();
        else
        {
            _hotkeys.UnregisterAll();
            _detailOpen = false;
        }
        UpdateWindows();
        return errors;
    }

    /// <summary>Поки користувач вводить хоткей у налаштуваннях, наші хоткеї не мають його перехоплювати.</summary>
    public void SuspendHotkeys(bool suspend)
    {
        _hotkeysSuspended = suspend;
        if (suspend) _hotkeys.UnregisterAll();
        else if (OverlayEnabled) RegisterHotkeys();
    }

    private IEnumerable<(string Label, string Value, Action Handler)> HotkeyBindings(HotkeySettings h) =>
    [
        ("Детальне меню", h.DetailMenu, ToggleDetail),
        ("Відмітити крок", h.MarkStep, MarkStep),
        ("Скасувати відмітку", h.UndoStep, UndoStep),
        ("Відмітити ноду", h.MarkNode, MarkNode),
        ("Копіювати regex", h.CopyRegex, CopyRegex),
        ("Таймер: старт / спліт", h.TimerSplit, TimerSplit),
        ("Таймер: скасувати спліт", h.TimerUndo, TimerUndo),
        ("Таймер: скинути", h.TimerReset, TimerReset),
    ];

    private IReadOnlyList<string> RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        if (_hotkeysSuspended) return [];
        var errors = new List<string>();
        foreach (var (label, value, handler) in HotkeyBindings(Settings.Hotkeys))
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (!Hotkey.TryParse(value, out var hk)) errors.Add($"{label}: не вдалося розібрати «{value}»");
            else if (!_hotkeys.TryRegister(hk, handler)) errors.Add($"{label}: {hk} зайнята іншою програмою");
        }
        return errors;
    }

    /// <summary>Перевіряє, чи можна зареєструвати всі клавіші, і знімає тестову реєстрацію.</summary>
    private List<string> TestHotkeys(HotkeySettings h)
    {
        _hotkeys.UnregisterAll();
        var errors = new List<string>();
        var seen = new Dictionary<Hotkey, string>();
        foreach (var (label, value, _) in HotkeyBindings(h))
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (!Hotkey.TryParse(value, out var hk))
            {
                errors.Add($"{label}: не вдалося розібрати «{value}»");
                continue;
            }
            if (seen.TryGetValue(hk, out var other))
            {
                errors.Add($"{label}: {hk} уже призначено на «{other}»");
                continue;
            }
            seen[hk] = label;
            if (!_hotkeys.TryRegister(hk, () => { })) errors.Add($"{label}: {hk} зайнята іншою програмою");
        }
        _hotkeys.UnregisterAll();
        return errors;
    }

    public void ToggleDetail()
    {
        _detailOpen = !_detailOpen;
        UpdateWindows();
    }

    public void CloseDetail()
    {
        _detailOpen = false;
        UpdateWindows();
    }

    private void UpdateWindows()
    {
        if (_overlay is null || _timer is null || _detail is null) return;
        var g = Game.Current;
        var show = OverlayEnabled && g.Found && !g.Minimized && g.Foreground;
        if (!show)
        {
            _overlay.Hide();
            _timer.Hide();
            _detail.Hide();
            return;
        }

        var c = g.Client;
        _overlay.Render();
        _overlay.ShowAt(c.Left + (int)(Settings.Overlay.HintsX * c.Width), c.Top + (int)(Settings.Overlay.HintsY * c.Height));

        if (Settings.Timer.Enabled && Tracker is not null)
        {
            _timer.Render();
            _timer.ShowAt(c.Left + (int)(Settings.Timer.X * c.Width), c.Top + (int)(Settings.Timer.Y * c.Height));
        }
        else _timer.Hide();

        if (_detailOpen)
        {
            _detail.Render();
            _detail.ShowBeside(g);
        }
        else _detail.Hide();
    }

    private void OnClock()
    {
        if (_timer is { IsVisible: true }) _timer.Render();
        if (_flash is not null && DateTime.Now > _flashUntil)
        {
            _flash = null;
            _overlay?.Render();
        }
    }

    /// <summary>Коротке повідомлення в оверлеї (кілька секунд).</summary>
    public string? Flash => _flash;

    private void ShowFlash(string text)
    {
        _flash = text;
        _flashUntil = DateTime.Now.AddSeconds(2.5);
        _overlay?.Render();
    }

    // ---------------- Гайд і прогрес ----------------

    public void LoadGuide()
    {
        GuideEntry = Guides.Find(Settings.GuideId, Settings.GuideVersion) ?? Guides.Entries.FirstOrDefault();
        if (GuideEntry?.Guide is not { } guide)
        {
            View = null;
            Engine = null;
            Tracker = null;
            Session = null;
            RaiseChanged();
            return;
        }

        Settings.GuideId = guide.Id;
        Settings.GuideVersion = guide.Version;
        View = GuideView.Create(guide, Settings.OptionsFor(guide.Id));
        Engine = new ProgressEngine(View, _progressStore.Load(guide.Id, Settings.CharacterName));
        Engine.RestoreCurrentArea();
        Engine.Recompute();

        var current = _runStore.LoadCurrent();
        Tracker = new SplitTracker(View.Splits, LoadHistory(guide.Id)) { RemoveLoads = Settings.Timer.RemoveLoads };
        if (current?.GuideId == guide.Id) Tracker.Load(current);

        Session = new TrackingSession(Engine, Tracker)
        {
            CharacterName = Settings.CharacterName,
            TimerEnabled = Settings.Timer.Enabled,
            AutoStart = Settings.Timer.AutoStart,
            StartRun = StartRun,
        };
        Session.CharacterSeen += name =>
        {
            if (!_logState.SeenCharacters.Contains(name)) _logState.SeenCharacters.Add(name);
        };
        RaiseChanged();
    }

    public TrackingSession? Session { get; private set; }

    private List<RunRecord> LoadHistory(string guideId)
    {
        var all = _runStore.LoadHistory(guideId);
        if (!Settings.Timer.SameOptionsOnly || View is null) return all;
        return all.Where(r => View.Options.All(o => r.Options.TryGetValue(o.Key, out var v) && v == o.Value)).ToList();
    }

    private void SaveProgress()
    {
        if (Engine is null) return;
        try
        {
            _progressStore.Save(View!.Guide.Id, Settings.CharacterName, Engine.State);
        }
        catch (IOException ex)
        {
            AppLog.Write($"Не вдалося зберегти прогрес: {ex.Message}");
        }
    }

    private void SaveRun()
    {
        try
        {
            _runStore.SaveCurrent(Tracker?.Current);
        }
        catch (IOException ex)
        {
            AppLog.Write($"Не вдалося зберегти забіг: {ex.Message}");
        }
    }

    private void RaiseChanged()
    {
        Changed?.Invoke();
        _overlay?.Render();
        if (_detail is { IsVisible: true }) _detail.Render();
        if (_timer is { IsVisible: true }) _timer.Render();
    }

    private void AfterChange(IReadOnlyList<string> doneSteps, DateTime time)
    {
        Session?.StepsDone(doneSteps, time);
        SaveProgress();
        SaveRun();
        RaiseChanged();
    }

    public void MarkStep()
    {
        if (Engine is null) return;
        var done = Engine.MarkFirstCurrent();
        if (done.Count == 0) ShowFlash("Немає поточних кроків");
        AfterChange(done, DateTime.Now);
    }

    public void UndoStep()
    {
        if (Engine?.UndoLast() is { } id) ShowFlash($"Знято відмітку: {View!.Steps.First(s => s.Id == id).Text}");
        AfterChange([], DateTime.Now);
    }

    public void SetStepDone(string id, bool done)
    {
        if (Engine is null) return;
        AfterChange(Engine.SetStepDone(id, done), DateTime.Now);
    }

    public void MarkNode()
    {
        if (Engine is null) return;
        if (Engine.MarkFirstNode() is null) ShowFlash("Немає нод, які час брати");
        AfterChange([], DateTime.Now);
    }

    public void SetNodeTaken(string id, bool taken)
    {
        Engine?.SetNodeTaken(id, taken);
        AfterChange([], DateTime.Now);
    }

    public void ChangeLevel(int delta)
    {
        if (Engine is null) return;
        var level = Engine.State.Level + delta;
        var done = Engine.SetLevel(level);
        Tracker?.OnTrigger(new SplitTrigger(TriggerKind.Level, DateTime.Now, Level: Engine.State.Level), Engine.State.Level);
        AfterChange(done, DateTime.Now);
    }

    public void ChangePoints(int delta)
    {
        if (Engine is null) return;
        AfterChange(Engine.AdjustPoints(delta), DateTime.Now);
    }

    public void ResetProgress()
    {
        Engine?.Reset();
        AfterChange([], DateTime.Now);
    }

    public void CopyRegex()
    {
        if (Engine is null) return;
        var steps = Engine.GetSteps();
        var source = steps.Current.Append(steps.Blocker).OfType<GuideStep>().FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Regex));
        if (source is null)
        {
            ShowFlash("У поточних кроках немає regex");
            return;
        }
        try
        {
            Clipboard.SetText(source.Regex!);
            ShowFlash("Regex скопійовано — Ctrl+F у вендора, Ctrl+V");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            ShowFlash("Буфер обміну зайнятий, спробуйте ще раз");
        }
    }

    // ---------------- Таймер ----------------

    public void TimerSplit()
    {
        if (Tracker is null || Engine is null) return;
        if (!Tracker.IsRunning) StartRun(DateTime.Now);
        else Tracker.ManualSplit(DateTime.Now, Engine.State.Level);
        SaveRun();
        RaiseChanged();
    }

    public void TimerUndo()
    {
        Tracker?.Undo();
        SaveRun();
        RaiseChanged();
    }

    public void TimerReset()
    {
        if (Tracker is null || View is null) return;
        if (Tracker.Reset() is { } old) _runStore.Archive(old);
        Tracker.SetHistory(LoadHistory(View.Guide.Id));
        SaveRun();
        ShowFlash("Таймер скинуто");
        RaiseChanged();
    }

    private void StartRun(DateTime at)
    {
        if (Tracker is null || View is null) return;
        if (Tracker.Reset() is { } old) _runStore.Archive(old);
        Tracker.SetHistory(LoadHistory(View.Guide.Id));
        Tracker.Start(at, View.Guide.Id, View.Guide.Version, Settings.CharacterName, View.Options);
    }

    // ---------------- Лог ----------------

    private void RestartLogWatcher()
    {
        _logWatcher?.Dispose();
        _logWatcher = null;
        var path = Settings.ClientLogPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        var offset = string.Equals(_logState.Path, path, StringComparison.OrdinalIgnoreCase) ? _logState.Offset : null;
        _logState.Path = path;
        _logWatcher = new LogWatcher(path, offset);
        _logWatcher.EventsRead += (events, newOffset) => _dispatcher.BeginInvoke(() => OnLogEvents(events, newOffset));
        _logWatcher.Error += ex => AppLog.Write($"Client.txt: {ex.Message}");
        _logWatcher.Start(TimeSpan.FromMilliseconds(500));
    }

    public bool LogFound => _logWatcher is not null;

    private void OnLogEvents(IReadOnlyList<LogEvent> events, long offset)
    {
        foreach (var ev in events)
        {
            if (Session is not null) Session.Handle(ev);
            else if (ev is LevelUp l && !_logState.SeenCharacters.Contains(l.Character)) _logState.SeenCharacters.Add(l.Character);
        }

        _logState.Offset = offset;
        try
        {
            JsonStore.Save(AppPaths.LogState, _logState);
        }
        catch (IOException ex)
        {
            AppLog.Write($"Не вдалося зберегти стан логу: {ex.Message}");
        }

        if (events.Count > 0)
        {
            SaveProgress();
            SaveRun();
            RaiseChanged();
        }
    }

    // ---------------- Папка гайдів ----------------

    private void WatchGuidesFolder()
    {
        _guidesWatcher = new FileSystemWatcher(AppPaths.GuidesDir) { IncludeSubdirectories = false, EnableRaisingEvents = true };
        FileSystemEventHandler restart = (_, _) => _dispatcher.BeginInvoke(() =>
        {
            _guidesDebounce.Stop();
            _guidesDebounce.Start();
        });
        _guidesWatcher.Created += restart;
        _guidesWatcher.Changed += restart;
        _guidesWatcher.Deleted += restart;
        _guidesWatcher.Renamed += (s, e) => restart(s, e);
    }

    private void OnGuidesFolderChanged()
    {
        _guidesDebounce.Stop();
        SaveProgress();
        Guides.Reload();
        LoadGuide();
        GuidesChanged?.Invoke();
    }

    public void Dispose()
    {
        SaveProgress();
        SaveRun();
        _clock.Stop();
        Game.Stop();
        _logWatcher?.Dispose();
        _guidesWatcher?.Dispose();
        _hotkeys.Dispose();
    }
}
