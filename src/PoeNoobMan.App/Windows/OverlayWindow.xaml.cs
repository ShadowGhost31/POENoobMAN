using System.Windows;
using PoeNoobMan.App.ViewModels;
using PoeNoobMan.Core.Log;

namespace PoeNoobMan.App.Windows;

/// <summary>
/// Акт і зона з рівнем; блок «зараз»: що зробити в зоні і куди далі; підказки білда з кольоровими мітками;
/// наступна нода дерева.
/// </summary>
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
            ActChip.Visibility = Visibility.Collapsed;
            ZoneName.Text = "PoE Noob Man";
            Stats.Text = "";
            RouteBox.Visibility = Visibility.Visible;
            RouteTitle.Text = "Оберіть білд у налаштуваннях";
            RouteExtra.ItemsSource = null;
            RouteNext.Text = "";
            Hints.ItemsSource = null;
            return;
        }

        var st = engine.State;
        var act = AreaIds.ActOf(st.CurrentAreaId);
        ActChip.Visibility = act is null ? Visibility.Collapsed : Visibility.Visible;
        ActText.Text = act is { } a ? (a == 11 ? "ЕПІЛОГ" : $"A{a}") : "";
        ZoneName.Text = st.CurrentAreaName ?? "Зайдіть у гру";
        Stats.Text = $"рів {st.Level} · {engine.Points} оч";

        var (route, hints) = HintBuilder.Build(engine, Math.Max(1, s.MaxHints));
        if (route is not null)
        {
            RouteBox.Visibility = Visibility.Visible;
            RouteTitle.Text = route.Title;
            RouteExtra.ItemsSource = route.Extra;
            RouteNext.Text = route.Next is { } next ? $"→ {next}" : "";
            RouteNext.Visibility = route.Next is null ? Visibility.Collapsed : Visibility.Visible;
        }
        else RouteBox.Visibility = Visibility.Collapsed;

        var nodes = engine.GetNodes();
        if (nodes.Now.FirstOrDefault() is { } now)
            hints.Add(HintBuilder.Node("ДЕРЕВО", now.Refund ? $"зняти: {now.Name}" : now.Name, now: true));
        else if (nodes.Next is { } nextNode)
            hints.Add(HintBuilder.Node("ДЕРЕВО", $"{nextNode.Name} — {nextNode.DescribeCondition()}", now: false));

        if (route is null && hints.Count == 0)
            hints.Add(HintBuilder.Node("✓", "Усі кроки гайда виконано", now: false));
        Hints.ItemsSource = hints;
    }
}
