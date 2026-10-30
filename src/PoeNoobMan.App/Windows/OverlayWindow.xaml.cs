using System.Windows;
using System.Windows.Documents;
using PoeNoobMan.App.ViewModels;
using PoeNoobMan.Core.Log;

namespace PoeNoobMan.App.Windows;

/// <summary>Рядок зони/рівня/очок, до N поточних кроків і наступна нода дерева.</summary>
public partial class OverlayWindow : OverlayWindowBase
{
    private readonly AppController _app;

    public OverlayWindow(AppController app)
    {
        _app = app;
        ClickThrough = true;
        InitializeComponent();
    }

    public void Render()
    {
        var s = _app.Settings.Overlay;
        Scale.ScaleX = Scale.ScaleY = s.Scale;
        Root.Background = Panel(s.Opacity);
        FlashLine.Text = _app.Flash ?? "";
        FlashLine.Visibility = _app.Flash is null ? Visibility.Collapsed : Visibility.Visible;

        if (_app.Engine is not { } engine)
        {
            Header.Text = "PoE Noob Man";
            Hints.ItemsSource = new[] { new HintItem { Act = "!", Text = "Оберіть гайд у налаштуваннях", IsWaiting = true } };
            NodeLine.Visibility = Visibility.Collapsed;
            return;
        }

        var st = engine.State;
        var zone = st.CurrentAreaName ?? "—";
        var act = AreaIds.ActOf(st.CurrentAreaId);
        Header.Text = $"{zone}{(act is { } a ? $" (A{a})" : "")}  ·  Рів. {st.Level}  ·  Очки {engine.Points}";

        var steps = engine.GetSteps();
        var hints = steps.Current.Take(Math.Max(1, s.MaxHints))
            .Select(x => new HintItem { Act = $"A{x.Act}", Text = x.Text })
            .ToList();
        if (hints.Count < s.MaxHints && steps.Blocker is { } b)
            hints.Add(new HintItem { Act = $"A{b.Act}", Text = $"Далі ({b.When}): {b.Text}", IsWaiting = true });
        if (hints.Count == 0)
            hints.Add(new HintItem { Act = "✓", Text = "Усі кроки гайда виконано", IsWaiting = true });
        Hints.ItemsSource = hints;

        var nodes = engine.GetNodes();
        NodeLine.Inlines.Clear();
        if (nodes.Now.FirstOrDefault() is { } now)
        {
            NodeLine.Inlines.Add(new Run("Взяти зараз: ") { Foreground = (System.Windows.Media.Brush)FindResource("OverlayAccent") });
            NodeLine.Inlines.Add(new Run(now.Refund ? $"зняти {now.Name}" : now.Name) { Foreground = (System.Windows.Media.Brush)FindResource("OverlayText") });
        }
        else if (nodes.Next is { } next)
        {
            NodeLine.Inlines.Add(new Run("Нода: ") { Foreground = (System.Windows.Media.Brush)FindResource("OverlayMuted") });
            NodeLine.Inlines.Add(new Run(next.Name) { Foreground = (System.Windows.Media.Brush)FindResource("OverlayText") });
            NodeLine.Inlines.Add(new Run($" — {next.When}") { Foreground = (System.Windows.Media.Brush)FindResource("OverlayDim") });
        }
        NodeLine.Visibility = NodeLine.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
