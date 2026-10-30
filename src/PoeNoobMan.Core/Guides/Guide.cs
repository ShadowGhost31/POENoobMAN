namespace PoeNoobMan.Core.Guides;

public sealed class Guide
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Patch { get; set; }
    public string? Source { get; set; }
    public List<GuideOption> Options { get; set; } = [];
    public List<GuideStep> Steps { get; set; } = [];
    public List<TreeNode> Tree { get; set; } = [];
    public List<SplitDef> Splits { get; set; } = [];

    /// <summary>Подія, на яку таймер стартує сам (якщо в налаштуваннях увімкнено автостарт).</summary>
    public Condition? TimerStart { get; set; }
}

public sealed class GuideOption
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Default { get; set; }
    public List<OptionChoice> Choices { get; set; } = [];
}

public sealed class OptionChoice
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
}

/// <summary>Усі задані поля мають виконуватись одночасно.</summary>
public sealed class Condition
{
    public int? Level { get; set; }
    public int? Points { get; set; }

    /// <summary>ID зони ("1_1_town") або її назва ("Lioneye's Watch").</summary>
    public string? Area { get; set; }

    /// <summary>ID кроку, який має бути виконаний.</summary>
    public string? Step { get; set; }

    public bool IsEmpty => Level is null && Points is null && Area is null && Step is null;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Level is { } l) parts.Add($"з {l} рівня");
        if (Points is { } p) parts.Add($"при {p} очках");
        if (Area is { } a) parts.Add($"у зоні {a}");
        if (Step is { } s) parts.Add($"після кроку {s}");
        return parts.Count == 0 ? "вручну" : string.Join(", ", parts);
    }
}

public sealed class GuideStep
{
    public string Id { get; set; } = "";
    public int Act { get; set; }
    public string Text { get; set; } = "";
    public string? Note { get; set; }

    /// <summary>Коли крок з'являється. Без умови з'являється одразу за попереднім.</summary>
    public Condition? When { get; set; }

    /// <summary>Коли крок відмічається сам. Без умови відмічається вручну.</summary>
    public Condition? Done { get; set; }

    /// <summary>Фільтр за опціями: optionId → choiceId (кілька варіантів через "|").</summary>
    public Dictionary<string, string>? If { get; set; }

    /// <summary>Скільки очок дерева дає крок (нагорода за квест).</summary>
    public int Points { get; set; }

    public string? Regex { get; set; }
    public bool Optional { get; set; }
}

public sealed class TreeNode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Note { get; set; }
    public Condition? When { get; set; }
    public Dictionary<string, string>? If { get; set; }
    public bool Refund { get; set; }
    public bool Optional { get; set; }
}

public sealed class SplitDef
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public Condition On { get; set; } = new();
    public Dictionary<string, string>? If { get; set; }
}
