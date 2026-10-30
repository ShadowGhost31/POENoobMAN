namespace PoeNoobMan.Core.Guides;

public sealed class Guide
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Patch { get; set; }
    public string? Source { get; set; }

    /// <summary>
    /// Спільний маршрут кампанії з папки routes/ (наприклад "campaign@3.29"). Його кроки йдуть першими,
    /// кроки гайда вставляються після кроку, вказаного в <see cref="GuideStep.After"/>.
    /// Якщо в гайді немає сплітів чи старту таймера, беруться з маршруту.
    /// </summary>
    public string? Route { get; set; }

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

    /// <summary>Пасивку з такою назвою взято (рядок "Successfully allocated passive skill ... name: X").</summary>
    public string? Passive { get; set; }

    /// <summary>Взято щонайменше стільки очок асценду: 2 — Normal Lab, 4 — Cruel, 6 — Merciless.</summary>
    public int? Ascendancy { get; set; }

    public bool IsEmpty => Level is null && Points is null && Area is null && Step is null && Passive is null && Ascendancy is null;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Level is { } l) parts.Add($"з {l} рівня");
        if (Points is { } p) parts.Add($"при {p} очках");
        if (Area is { } a) parts.Add($"у зоні {Log.AreaNames.Display(a)}");
        if (Step is not null) parts.Add("після попереднього етапу");
        if (Passive is { } n) parts.Add($"після {n}");
        if (Ascendancy is { } asc) parts.Add(asc switch { <= 2 => "після Normal Lab", <= 4 => "після Cruel Lab", <= 6 => "після Merciless Lab", _ => $"{asc} очок асценду" });
        return parts.Count == 0 ? "вручну" : string.Join(", ", parts);
    }
}

public sealed class GuideStep
{
    public string Id { get; set; } = "";
    public int Act { get; set; }
    public string Text { get; set; } = "";
    public string? Note { get; set; }

    /// <summary>Коли крок з'являється. Без умови — щойно гравець дійшов до його акту.</summary>
    public Condition? When { get; set; }

    /// <summary>
    /// Коли крок закривається сам. Без умови крок — підказка: закривається, коли її показали в місті
    /// і гравець вийшов з міста (купив, що треба, і пішов далі).
    /// </summary>
    public Condition? Done { get; set; }

    /// <summary>Фільтр за опціями: optionId → choiceId (кілька варіантів через "|").</summary>
    public Dictionary<string, string>? If { get; set; }

    /// <summary>Скільки очок дерева дає крок (нагорода за квест); закривається рядком про отримане очко.</summary>
    public int Points { get; set; }

    /// <summary>Для кроків гайда з маршрутом: id кроку маршруту, після якого вставити цей крок.</summary>
    public string? After { get; set; }

    /// <summary>Тип підказки для мітки в оверлеї: gems, buy, craft, tip (для кроків маршруту, квестів і лабіринтів — сам).</summary>
    public string? Kind { get; set; }

    public string? Regex { get; set; }
    public bool Optional { get; set; }

    /// <summary>Пункти тексту: « · » розділяє короткі рядки (геми, покупки).</summary>
    public IReadOnlyList<string> Lines => Text.Split(" · ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public StepKind KindOf() =>
        When?.Area is not null && Done?.Area is not null ? StepKind.Route
        : Points > 0 ? StepKind.Reward
        : Done?.Ascendancy is not null ? StepKind.Lab
        : Kind?.ToLowerInvariant() switch
        {
            "gems" => StepKind.Gems,
            "buy" => StepKind.Buy,
            "craft" => StepKind.Craft,
            "flask" => StepKind.Buy,
            _ => StepKind.Tip,
        };

    public static readonly string[] KnownKinds = ["gems", "buy", "craft", "flask", "tip"];
}

public enum StepKind
{
    Route,
    Reward,
    Lab,
    Gems,
    Buy,
    Craft,
    Tip,
}

public sealed class TreeNode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Note { get; set; }
    public Condition? When { get; set; }

    /// <summary>Як показувати умову замість автоматичного тексту, наприклад "після Normal Lab".</summary>
    public string? Hint { get; set; }

    /// <summary>
    /// Назви пасивок так, як їх пише гра в Client.txt ("Heart and Soul", "Fire Mastery"). Нода відмічається сама,
    /// коли взято всі (для refund — коли всі зняли). Без списку нода відмічається вручну.
    /// </summary>
    public List<string>? Passives { get; set; }

    public Dictionary<string, string>? If { get; set; }
    public bool Refund { get; set; }
    public bool Optional { get; set; }

    public string DescribeCondition() => Hint ?? When?.ToString() ?? "вручну";
}

public sealed class SplitDef
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public Condition On { get; set; } = new();
    public Dictionary<string, string>? If { get; set; }
}
