using System.Windows.Media;
using PoeNoobMan.Core.Guides;
using PoeNoobMan.Core.Log;
using PoeNoobMan.Core.Progress;

namespace PoeNoobMan.App.ViewModels;

/// <summary>Рядок підказки з кольоровою міткою типу: ГЕМИ, КУПИТИ, КРАФТ, НАГОРОДА, ЛАБ, ПОРАДА, ДАЛІ.</summary>
public sealed class HintItem
{
    public required string Chip { get; init; }
    public required Brush ChipBackground { get; init; }
    public required Brush ChipForeground { get; init; }
    public required string Title { get; init; }
    public IReadOnlyList<string> Extra { get; init; } = [];
    public bool HasExtra => Extra.Count > 0;
    public bool IsWaiting { get; init; }

    /// <summary>Для простих списків (вікно налаштувань).</summary>
    public string Act => Chip;
    public string Text => Extra.Count == 0 ? Title : $"{Title} · {string.Join(" · ", Extra)}";
}

/// <summary>Поточний крок маршруту: що зробити в зоні і куди далі.</summary>
public sealed record RouteHint(string Zone, string Title, IReadOnlyList<string> Extra, string? Next);

public static class HintBuilder
{
    private static SolidColorBrush B(uint rgb)
    {
        var b = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        b.Freeze();
        return b;
    }

    private static readonly Dictionary<StepKind, (string Label, Brush Bg, Brush Fg)> Styles = new()
    {
        [StepKind.Gems] = ("ГЕМИ", B(0x0C447C), B(0xB5D4F4)),
        [StepKind.Buy] = ("КУПИТИ", B(0x633806), B(0xFAC775)),
        [StepKind.Craft] = ("КРАФТ", B(0x712B13), B(0xF5C4B3)),
        [StepKind.Reward] = ("НАГОРОДА", B(0x27500A), B(0xC0DD97)),
        [StepKind.Lab] = ("ЛАБ", B(0x3C3489), B(0xCECBF6)),
        [StepKind.Tip] = ("ПОРАДА", B(0x444441), B(0xD3D1C7)),
        [StepKind.Route] = ("ЗОНА", B(0x444441), B(0xD3D1C7)),
    };

    private static readonly Brush WaitBg = B(0x2C2C2A);
    private static readonly Brush WaitFg = B(0x888780);

    public static HintItem Hint(GuideStep s)
    {
        var (label, bg, fg) = Styles[s.KindOf()];
        if (s.KindOf() == StepKind.Gems && s.When?.Level is { } lvl) label = $"{label} {lvl}";
        var lines = s.Lines;
        return new HintItem
        {
            Chip = label,
            ChipBackground = bg,
            ChipForeground = fg,
            Title = lines.Count > 0 ? lines[0] : s.Text,
            Extra = lines.Skip(1).ToList(),
        };
    }

    public static HintItem Waiting(GuideStep s)
    {
        var lines = s.Lines;
        return new HintItem
        {
            Chip = "ДАЛІ",
            ChipBackground = WaitBg,
            ChipForeground = WaitFg,
            Title = $"{s.When}: {(lines.Count > 0 ? lines[0] : s.Text)}",
            IsWaiting = true,
        };
    }

    public static HintItem Node(string chip, string text, bool now) => new()
    {
        Chip = chip,
        ChipBackground = now ? Styles[StepKind.Buy].Bg : WaitBg,
        ChipForeground = now ? Styles[StepKind.Buy].Fg : WaitFg,
        Title = text,
        IsWaiting = !now,
    };

    /// <summary>Поточний крок маршруту, підказки білда (до <paramref name="max"/>) і рядок «далі».</summary>
    public static (RouteHint? Route, List<HintItem> Hints) Build(ProgressEngine engine, int max)
    {
        var steps = engine.GetSteps();
        RouteHint? route = null;
        var hints = new List<HintItem>();
        foreach (var s in steps.Current)
        {
            if (s.KindOf() == StepKind.Route)
            {
                if (route is not null) continue;
                var lines = s.Lines;
                route = new RouteHint(
                    AreaNames.NameOf(s.When!.Area!) ?? s.When.Area!,
                    lines.Count > 0 ? lines[0] : s.Text,
                    lines.Skip(1).ToList(),
                    s.Done?.Area is { } next ? AreaNames.NameOf(next) ?? next : null);
                continue;
            }
            if (hints.Count < max) hints.Add(Hint(s));
        }
        if (hints.Count < max && steps.Blocker is { } b) hints.Add(Waiting(b));
        return (route, hints);
    }
}
