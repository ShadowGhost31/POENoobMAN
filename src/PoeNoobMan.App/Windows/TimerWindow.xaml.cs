using System.Windows;
using System.Windows.Media;
using PoeNoobMan.App.ViewModels;
using PoeNoobMan.Core.Splits;

namespace PoeNoobMan.App.Windows;

/// <summary>Компактний таймер: остання подія з часом і різницею, поточний час, наступна подія.</summary>
public partial class TimerWindow : OverlayWindowBase
{
    private readonly AppController _app;

    public TimerWindow(AppController app)
    {
        _app = app;
        ClickThrough = true;
        InitializeComponent();
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    public void Render()
    {
        Scale.ScaleX = Scale.ScaleY = _app.Settings.Overlay.Scale;
        Root.Background = Panel(Math.Max(_app.Settings.Overlay.Opacity, 0.6));
        if (_app.Tracker is not { } tracker) return;

        var t = _app.Settings.Timer;
        var s = tracker.Snapshot(DateTime.Now, t.Comparison);

        if (!s.HasRun)
        {
            LastLabel.Text = t.AutoStart && _app.View?.Guide.TimerStart is not null
                ? $"Автостарт або {_app.Settings.Hotkeys.TimerSplit}"
                : $"{_app.Settings.Hotkeys.TimerSplit} — старт";
            LastTime.Text = "";
            Delta.Text = "";
            Big.Text = "0:00:00";
            Big.Foreground = Res("OverlayDim");
        }
        else
        {
            LastLabel.Text = s.Finished ? $"Фініш · {s.LastLabel}" : s.LastLabel ?? "Старт";
            LastTime.Text = s.LastTime is { } lt ? TimeFormat.Clock(lt) : "";
            Delta.Text = s.LastDelta is { } d ? TimeFormat.Delta(d) : "";
            Delta.Foreground = s.LastGold ? Res("Gold") : s.LastDelta < TimeSpan.Zero ? Res("Ahead") : Res("Behind");
            Big.Text = TimeFormat.Clock(s.Elapsed);
            Big.Foreground = s.Tone switch
            {
                TimerTone.Ahead => Res("Ahead"),
                TimerTone.Behind => Res("Behind"),
                _ => Res("OverlayText"),
            };
        }

        var showNext = t.ShowNext && s.NextLabel is not null && !s.Finished;
        NextRow.Visibility = showNext ? Visibility.Visible : Visibility.Collapsed;
        if (showNext)
        {
            NextLabel.Text = $"Далі: {s.NextLabel}";
            var cmpName = t.Comparison switch
            {
                Comparison.LastRun => "мин.",
                Comparison.SumOfBest => "SoB",
                _ => "PB",
            };
            NextCompare.Text = s.NextCompare is { } nc ? $"{cmpName} {TimeFormat.Clock(nc)}" : "";
        }
    }
}
