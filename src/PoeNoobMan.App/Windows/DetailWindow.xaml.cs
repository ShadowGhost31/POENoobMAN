using System.Windows;
using System.Windows.Controls;
using PoeNoobMan.App.Services;
using PoeNoobMan.App.ViewModels;
using PoeNoobMan.Core.Guides;

namespace PoeNoobMan.App.Windows;

/// <summary>Детальне меню (F9): відкривається праворуч від вікна гри і не забирає фокус.</summary>
public partial class DetailWindow : OverlayWindowBase
{
    private readonly AppController _app;
    private GuideView? _builtFor;
    private List<StepItem> _steps = [];
    private List<NodeItem> _nodes = [];

    public DetailWindow(AppController app)
    {
        _app = app;
        ClickThrough = false;
        InitializeComponent();
    }

    public void Render()
    {
        Scale.ScaleX = Scale.ScaleY = _app.Settings.Overlay.Scale;
        Root.Background = Panel(Math.Max(_app.Settings.Overlay.Opacity, 0.92));

        if (_app.Engine is not { } engine || _app.View is not { } view)
        {
            Title1.Text = "Гайд не обрано";
            Title2.Text = "Оберіть білд у вікні налаштувань";
            StepsList.ItemsSource = null;
            TreeList.ItemsSource = null;
            return;
        }

        if (!ReferenceEquals(_builtFor, view))
        {
            _builtFor = view;
            _steps = view.Steps.Select(s => new StepItem(s)).ToList();
            _nodes = view.Nodes.Select(n => new NodeItem(n)).ToList();
            StepsList.ItemsSource = _steps;
            TreeList.ItemsSource = _nodes;
        }

        var g = view.Guide;
        Title1.Text = $"{g.Title} · v{g.Version}";
        var options = g.Options.Select(o => o.Choices.FirstOrDefault(c => c.Id == view.Options.GetValueOrDefault(o.Id))?.Label).OfType<string>();
        Title2.Text = string.Join(" · ", options);
        LevelText.Text = engine.State.Level.ToString();
        PointsText.Text = engine.Points.ToString();

        var snapshot = engine.GetSteps();
        var current = snapshot.Current.Select(s => s.Id).ToHashSet();
        foreach (var item in _steps)
        {
            item.IsDone = engine.State.DoneSteps.Contains(item.Id);
            item.IsCurrent = current.Contains(item.Id);
            item.IsBlocker = snapshot.Blocker?.Id == item.Id;
        }

        var nodes = engine.GetNodes();
        var now = nodes.Now.Select(n => n.Id).ToHashSet();
        foreach (var item in _nodes)
        {
            item.IsTaken = engine.State.TakenNodes.Contains(item.Id);
            item.IsNow = now.Contains(item.Id);
            item.IsNext = nodes.Next?.Id == item.Id;
        }

        if (nodes.Now.FirstOrDefault() is { } n)
        {
            NodeText.Text = $"Взяти зараз: {(n.Refund ? "зняти " : "")}{n.Name}";
            TakeNodeButton.Visibility = Visibility.Visible;
        }
        else
        {
            NodeText.Text = nodes.Next is { } next ? $"Наступна нода: {next.Name} — {next.DescribeCondition()}" : "Усі ноди взято";
            TakeNodeButton.Visibility = Visibility.Collapsed;
        }

        RenderRun();
    }

    private void RenderRun()
    {
        if (_app.Tracker is not { } tracker) return;
        var cmp = _app.Settings.Timer.Comparison;
        RunList.ItemsSource = tracker.Rows().Select(r =>
        {
            var compare = cmp switch
            {
                Core.Splits.Comparison.LastRun => r.Last,
                Core.Splits.Comparison.SumOfBest => r.SumOfBest,
                _ => r.Best,
            };
            var delta = r.Current is { } c && compare is { } b ? c - b : (double?)null;
            return new SplitRowItem
            {
                Label = r.Def.Label,
                Current = r.Recorded ? TimeFormat.Clock(r.Current) : "",
                Delta = delta is { } d ? TimeFormat.Delta(TimeSpan.FromSeconds(Math.Round(d))) : "",
                Best = TimeFormat.Clock(r.Best),
                Last = TimeFormat.Clock(r.Last),
                Ahead = delta < 0,
                Behind = delta > 0,
            };
        }).Prepend(new SplitRowItem { Label = "Спліт", Current = "Зараз", Delta = "Δ", Best = "PB", Last = "Мин." }).ToList();
    }

    /// <summary>Праворуч від гри, а якщо там немає місця — біля правого краю всередині вікна гри.</summary>
    public void ShowBeside(GameWindowInfo game)
    {
        var c = game.Client;
        var maxHeightDip = c.Height * 0.84 / Math.Max(DpiScale, 0.5);
        var listHeight = Math.Max(160, maxHeightDip / Math.Max(_app.Settings.Overlay.Scale, 0.5) - 240);
        StepsScroll.MaxHeight = TreeScroll.MaxHeight = RunScroll.MaxHeight = listHeight;
        StepsScroll.Height = TreeScroll.Height = RunScroll.Height = listHeight;

        var opening = !IsVisible;
        if (opening) Show();
        UpdateLayout();
        var size = PixelBounds();
        var work = Native.Win32.WorkAreaOf(game.Hwnd);
        var x = c.Right + 8 + size.Width <= work.Right ? c.Right + 8 : c.Right - size.Width - 16;
        var y = c.Top + (int)(c.Height * 0.08);
        ShowAt(x, y);
        if (opening) ScrollToCurrent();
    }

    private void ScrollToCurrent()
    {
        var index = _steps.FindIndex(s => s.IsCurrent || s.IsBlocker);
        if (index < 0) return;
        if (StepsList.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement el)
            el.BringIntoView();
    }

    private void OnClose(object sender, RoutedEventArgs e) => _app.CloseDetail();
    private void OnLevelDown(object sender, RoutedEventArgs e) => _app.ChangeLevel(-1);
    private void OnLevelUp(object sender, RoutedEventArgs e) => _app.ChangeLevel(1);
    private void OnPointsDown(object sender, RoutedEventArgs e) => _app.ChangePoints(-1);
    private void OnPointsUp(object sender, RoutedEventArgs e) => _app.ChangePoints(1);
    private void OnTakeNode(object sender, RoutedEventArgs e) => _app.MarkNode();
    private void OnTimerSplit(object sender, RoutedEventArgs e) => _app.TimerSplit();
    private void OnTimerUndo(object sender, RoutedEventArgs e) => _app.TimerUndo();
    private void OnTimerReset(object sender, RoutedEventArgs e) => _app.TimerReset();

    private void OnStepClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: string id } cb) _app.SetStepDone(id, cb.IsChecked == true);
    }

    private void OnNodeClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: string id } cb) _app.SetNodeTaken(id, cb.IsChecked == true);
    }

    private void OnResetAsk(object sender, RoutedEventArgs e)
    {
        ResetButton.Visibility = Visibility.Collapsed;
        ResetConfirm.Visibility = Visibility.Visible;
    }

    private void OnResetYes(object sender, RoutedEventArgs e)
    {
        _app.ResetProgress();
        OnResetNo(sender, e);
    }

    private void OnResetNo(object sender, RoutedEventArgs e)
    {
        ResetConfirm.Visibility = Visibility.Collapsed;
        ResetButton.Visibility = Visibility.Visible;
    }
}
