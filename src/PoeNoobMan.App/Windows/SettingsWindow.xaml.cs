using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PoeNoobMan.App.Services;
using PoeNoobMan.App.ViewModels;
using PoeNoobMan.Core.Guides;
using PoeNoobMan.Core.Splits;
using PoeNoobMan.Core.Storage;

namespace PoeNoobMan.App.Windows;

public partial class SettingsWindow : Window
{
    private readonly AppController _app;
    private AppSettings _edit;
    private List<OptionItem> _options = [];
    private List<HotkeyItem> _hotkeys = [];
    private bool _loading;

    public SettingsWindow(AppController app)
    {
        _app = app;
        _edit = app.CloneSettings();
        InitializeComponent();
        VersionText.Text = $"Версія: {UpdateService.CurrentVersion}";

        app.GameChanged += _ => RenderGameStatus();
        app.GuidesChanged += () => FillGuides(GuideBox.SelectedItem is GuideEntry g ? g.Id : _edit.GuideId);
        Loaded += (_, _) =>
        {
            FillFrom(_edit);
            RenderOverlayButton();
            RenderGameStatus();
            if (_app.Settings.CheckUpdatesOnStart && !string.IsNullOrWhiteSpace(_app.Settings.UpdateSource)) _ = CheckUpdatesAsync(silent: true);
        };
    }

    // ---------------- Заповнення ----------------

    private void FillFrom(AppSettings s)
    {
        _loading = true;
        LogPath.Text = s.ClientLogPath;
        RenderLogStatus();
        Character.ItemsSource = _app.SeenCharacters.ToList();
        Character.Text = s.CharacterName;

        FillGuides(s.GuideId);

        ScaleSlider.Value = s.Overlay.Scale;
        OpacitySlider.Value = s.Overlay.Opacity;
        HintsSlider.Value = s.Overlay.MaxHints;
        HintsX.Text = Percent(s.Overlay.HintsX);
        HintsY.Text = Percent(s.Overlay.HintsY);
        TimerX.Text = Percent(s.Timer.X);
        TimerY.Text = Percent(s.Timer.Y);

        TimerEnabled.IsChecked = s.Timer.Enabled;
        TimerAutoStart.IsChecked = s.Timer.AutoStart;
        TimerShowNext.IsChecked = s.Timer.ShowNext;
        TimerSameOptions.IsChecked = s.Timer.SameOptionsOnly;
        ComparisonBox.SelectedValue = s.Timer.Comparison.ToString();

        var h = s.Hotkeys;
        _hotkeys =
        [
            new() { Key = nameof(h.DetailMenu), Label = "Детальне меню", Value = h.DetailMenu },
            new() { Key = nameof(h.MarkStep), Label = "Відмітити крок", Value = h.MarkStep },
            new() { Key = nameof(h.UndoStep), Label = "Скасувати відмітку", Value = h.UndoStep },
            new() { Key = nameof(h.MarkNode), Label = "Відмітити ноду", Value = h.MarkNode },
            new() { Key = nameof(h.CopyRegex), Label = "Копіювати regex вендора", Value = h.CopyRegex },
            new() { Key = nameof(h.TimerSplit), Label = "Таймер: старт / спліт", Value = h.TimerSplit },
            new() { Key = nameof(h.TimerUndo), Label = "Таймер: скасувати спліт", Value = h.TimerUndo },
            new() { Key = nameof(h.TimerReset), Label = "Таймер: скинути", Value = h.TimerReset },
        ];
        HotkeysList.ItemsSource = _hotkeys;

        UpdateSource.Text = s.UpdateSource;
        CheckUpdatesOnStart.IsChecked = s.CheckUpdatesOnStart;
        _loading = false;
    }

    private void FillGuides(string? selectId)
    {
        var wasLoading = _loading;
        _loading = true;
        var latest = _app.Guides.GuideIds.Select(id => _app.Guides.Versions(id)[0]).ToList();
        GuideBox.ItemsSource = latest;
        GuideBox.SelectedItem = latest.FirstOrDefault(g => g.Id == selectId) ?? latest.FirstOrDefault();
        _loading = wasLoading;
        FillVersionsAndOptions();

        var broken = _app.Guides.Broken.Select(b => $"{Path.GetFileName(b.Path)}: {string.Join("; ", b.Errors)}").ToList();
        GuideErrors.Text = broken.Count == 0 ? "" : "Файли з помилками:\n" + string.Join("\n", broken);
        if (latest.Count == 0) GuideErrors.Text = "Гайдів не знайдено. " + GuideErrors.Text;
    }

    private void FillVersionsAndOptions()
    {
        if (GuideBox.SelectedItem is not GuideEntry entry)
        {
            VersionBox.ItemsSource = null;
            OptionsList.ItemsSource = null;
            return;
        }

        var versions = _app.Guides.Versions(entry.Id);
        VersionBox.ItemsSource = versions;
        VersionBox.SelectedItem = versions.FirstOrDefault(v => entry.Id == _edit.GuideId && v.Version == _edit.GuideVersion) ?? versions[0];

        var guide = ((GuideEntry)VersionBox.SelectedItem).Guide!;
        var selected = GuideView.Create(guide, _edit.OptionsFor(guide.Id)).Options;
        _options = guide.Options.Select(o => new OptionItem
        {
            Id = o.Id,
            Label = o.Label,
            Choices = o.Choices,
            Selected = selected.GetValueOrDefault(o.Id, o.Choices.FirstOrDefault()?.Id ?? ""),
        }).ToList();
        OptionsList.ItemsSource = _options;
    }

    private void OnGuideChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) FillVersionsAndOptions();
    }

    // ---------------- Збереження ----------------

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var s = _app.CloneSettings();
        s.ClientLogPath = LogPath.Text.Trim();
        s.CharacterName = Character.Text.Trim();

        if (VersionBox.SelectedItem is GuideEntry ver)
        {
            s.GuideId = ver.Id;
            s.GuideVersion = ver.Version;
            s.GuideOptions[ver.Id] = _options.ToDictionary(o => o.Id, o => o.Selected);
        }

        s.Overlay.Scale = ScaleSlider.Value;
        s.Overlay.Opacity = OpacitySlider.Value;
        s.Overlay.MaxHints = (int)HintsSlider.Value;
        var errors = new List<string>();
        s.Overlay.HintsX = ParsePercent(HintsX.Text, s.Overlay.HintsX, "Підказки X", errors);
        s.Overlay.HintsY = ParsePercent(HintsY.Text, s.Overlay.HintsY, "Підказки Y", errors);
        s.Timer.X = ParsePercent(TimerX.Text, s.Timer.X, "Таймер X", errors);
        s.Timer.Y = ParsePercent(TimerY.Text, s.Timer.Y, "Таймер Y", errors);

        s.Timer.Enabled = TimerEnabled.IsChecked == true;
        s.Timer.AutoStart = TimerAutoStart.IsChecked == true;
        s.Timer.ShowNext = TimerShowNext.IsChecked == true;
        s.Timer.SameOptionsOnly = TimerSameOptions.IsChecked == true;
        if (Enum.TryParse<Comparison>(ComparisonBox.SelectedValue as string, out var cmp)) s.Timer.Comparison = cmp;

        foreach (var hk in _hotkeys)
            typeof(HotkeySettings).GetProperty(hk.Key)!.SetValue(s.Hotkeys, hk.Value);

        s.UpdateSource = UpdateSource.Text.Trim();
        s.CheckUpdatesOnStart = CheckUpdatesOnStart.IsChecked == true;

        if (errors.Count == 0) errors.AddRange(_app.SaveSettings(s));
        if (errors.Count > 0)
        {
            ShowStatus(string.Join("\n", errors), error: true);
            return;
        }

        _edit = _app.CloneSettings();
        RenderLogStatus();
        ShowStatus($"Збережено о {DateTime.Now:HH:mm:ss}", error: false);
    }

    private void ShowStatus(string text, bool error)
    {
        SaveStatus.Text = text;
        SaveStatus.Foreground = error ? new SolidColorBrush(Color.FromRgb(0xA3, 0x2D, 0x2D)) : new SolidColorBrush(Color.FromRgb(0x3B, 0x6D, 0x11));
    }

    private static string Percent(double fraction) => Math.Round(fraction * 100, 1).ToString(CultureInfo.InvariantCulture);

    private static double ParsePercent(string text, double fallback, string label, List<string> errors)
    {
        if (double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v is >= 0 and <= 100)
            return v / 100;
        errors.Add($"{label}: потрібне число від 0 до 100");
        return fallback;
    }

    // ---------------- Оверлей ----------------

    private void OnToggleOverlay(object sender, RoutedEventArgs e)
    {
        var errors = _app.SetOverlayEnabled(!_app.OverlayEnabled);
        OverlayErrors.Text = errors.Count == 0 ? "" : string.Join("\n", errors);
        RenderOverlayButton();
    }

    private void RenderOverlayButton() =>
        OverlayButton.Content = _app.OverlayEnabled ? "Вимкнути оверлей" : "Увімкнути оверлей і прив'язати до вікна PoE";

    private void RenderGameStatus()
    {
        var found = _app.Game.Current.Found;
        GameDot.Fill = found ? new SolidColorBrush(Color.FromRgb(0x63, 0x99, 0x22)) : new SolidColorBrush(Color.FromRgb(0xE2, 0x4B, 0x4A));
        GameStatus.Text = found ? "Вікно гри знайдено" : "Вікно гри не знайдено";
    }

    // ---------------- Лог ----------------

    private void RenderLogStatus()
    {
        var path = LogPath.Text.Trim();
        var ok = path.Length > 0 && File.Exists(path);
        LogStatus.Text = ok ? "Файл знайдено" : "Файл не знайдено";
        LogStatus.Foreground = ok ? Brushes.DarkGreen : Brushes.Firebrick;
    }

    private void OnBrowseLog(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Client.txt|Client.txt|Текстові файли|*.txt", FileName = "Client.txt" };
        if (File.Exists(LogPath.Text)) dlg.InitialDirectory = Path.GetDirectoryName(LogPath.Text);
        if (dlg.ShowDialog(this) == true)
        {
            LogPath.Text = dlg.FileName;
            RenderLogStatus();
        }
    }

    private void OnDetectLog(object sender, RoutedEventArgs e)
    {
        if (_app.DetectClientLog() is { } path) LogPath.Text = path;
        RenderLogStatus();
        if (!File.Exists(LogPath.Text)) LogStatus.Text = "Не вдалося знайти автоматично — запустіть гру або вкажіть файл вручну";
    }

    private void OnOpenGuides(object sender, RoutedEventArgs e) => GameWindowTracker.OpenFolder(AppPaths.GuidesDir);
    private void OnOpenData(object sender, RoutedEventArgs e) => GameWindowTracker.OpenFolder(AppPaths.Root);

    // ---------------- Хоткеї ----------------

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: HotkeyItem item }) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Back or Key.Delete)
        {
            item.Value = "";
            return;
        }
        if (key is Key.Tab or Key.Escape || Hotkey.IsModifierKey(key)) return;
        item.Value = new Hotkey(Keyboard.Modifiers, key).ToString();
    }

    private void OnHotkeyFocus(object sender, KeyboardFocusChangedEventArgs e) => _app.SuspendHotkeys(true);
    private void OnHotkeyBlur(object sender, KeyboardFocusChangedEventArgs e) => _app.SuspendHotkeys(false);

    // ---------------- Оновлення ----------------

    private async void OnUpdate(object sender, RoutedEventArgs e)
    {
        if (_app.Updates.PendingVersion is not null)
        {
            UpdateButton.IsEnabled = false;
            try
            {
                await _app.Updates.DownloadAndRestartAsync(p => Dispatcher.BeginInvoke(() => UpdateStatus.Text = $"Завантаження… {p}%"));
            }
            catch (Exception ex)
            {
                UpdateStatus.Text = ex.Message;
                UpdateButton.IsEnabled = true;
            }
            return;
        }
        await CheckUpdatesAsync(silent: false);
    }

    private async Task CheckUpdatesAsync(bool silent)
    {
        var source = string.IsNullOrWhiteSpace(UpdateSource.Text) ? _app.Settings.UpdateSource : UpdateSource.Text.Trim();
        UpdateButton.IsEnabled = false;
        UpdateStatus.Text = "Перевіряю…";
        try
        {
            var version = await _app.Updates.CheckAsync(source);
            if (version is null) UpdateStatus.Text = "У вас остання версія";
            else
            {
                UpdateStatus.Text = $"Доступна версія {version}";
                UpdateButton.Content = $"Встановити {version}";
            }
        }
        catch (Exception ex)
        {
            UpdateStatus.Text = silent ? "" : ex.Message;
            AppLog.Write($"Оновлення: {ex.Message}");
        }
        finally
        {
            UpdateButton.IsEnabled = true;
        }
    }
}
