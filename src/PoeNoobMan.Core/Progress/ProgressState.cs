namespace PoeNoobMan.Core.Progress;

/// <summary>Прогрес одного персонажа за одним гайдом. Кроки і ноди зберігаються за id, тому переживають нові версії гайда.</summary>
public sealed class ProgressState
{
    public int Level { get; set; } = 1;

    /// <summary>Ручна поправка до розрахованих очок дерева.</summary>
    public int PointsAdjust { get; set; }

    public string? CurrentAreaId { get; set; }
    public string? CurrentAreaName { get; set; }

    /// <summary>Відвідані зони (ID, а якщо ID невідомий — назва) у порядку першого входу.</summary>
    public List<string> VisitedAreas { get; set; } = [];

    public HashSet<string> DoneSteps { get; set; } = [];

    /// <summary>Кроки, чию умову по зоні вже виконано.</summary>
    public HashSet<string> UnlockedSteps { get; set; } = [];

    /// <summary>Кроки, з яких користувач зняв відмітку: автоматика їх більше не відмічає.</summary>
    public HashSet<string> SuppressedAuto { get; set; } = [];

    public HashSet<string> TakenNodes { get; set; } = [];

    /// <summary>Порядок відмічених кроків, для скасування.</summary>
    public List<string> History { get; set; } = [];
}
