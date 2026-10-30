namespace PoeNoobMan.Core.Progress;

/// <summary>Прогрес одного персонажа за одним гайдом. Кроки і ноди зберігаються за id, тому переживають нові версії гайда.</summary>
public sealed class ProgressState
{
    public int Level { get; set; } = 1;

    /// <summary>Ручна поправка до розрахованих очок дерева.</summary>
    public int PointsAdjust { get; set; }

    /// <summary>Очки за квести з рядків логу «You have received a Passive Skill Point».</summary>
    public int QuestPoints { get; set; }

    /// <summary>QuestPoints отримано з /passives, тобто це точне число, а не лише прирости з моменту запуску.</summary>
    public bool QuestPointsExact { get; set; }

    public string? CurrentAreaId { get; set; }
    public string? CurrentAreaName { get; set; }

    /// <summary>Відвідані зони (ID, а якщо ID невідомий — назва) у порядку першого входу.</summary>
    public List<string> VisitedAreas { get; set; } = [];

    public HashSet<string> DoneSteps { get; set; } = [];

    /// <summary>Кроки, чию умову по зоні вже виконано.</summary>
    public HashSet<string> UnlockedSteps { get; set; } = [];

    /// <summary>Порядок, у якому кроки відкривались: останній відкритий крок маршруту — це зона, де гравець зараз.</summary>
    public List<string> UnlockOrder { get; set; } = [];

    /// <summary>Кроки, з яких користувач зняв відмітку: автоматика їх більше не відмічає.</summary>
    public HashSet<string> SuppressedAuto { get; set; } = [];

    public HashSet<string> TakenNodes { get; set; } = [];

    /// <summary>Взято очок асценду (по 2 за Normal, Cruel і Merciless Labyrinth).</summary>
    public int AscendancyPoints { get; set; }

    /// <summary>Назви пасивок, взятих за логом (без урахування регістру).</summary>
    public HashSet<string> AllocatedPassives { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Порядок відмічених кроків, для скасування.</summary>
    public List<string> History { get; set; } = [];
}
