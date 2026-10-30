namespace PoeNoobMan.Core.Log;

public abstract record LogEvent(DateTime Time);

/// <summary>"You have entered X." з ID зони з попереднього рядка "Generating level N area ...".</summary>
public sealed record AreaEntered(DateTime Time, string AreaName, string? AreaId, int? AreaLevel) : LogEvent(Time)
{
    /// <summary>Номер акту з ID зони: "1_3_town" → 3, "2_8_1" → 8.</summary>
    public int? Act => AreaIds.ActOf(AreaId);
}

public sealed record LevelUp(DateTime Time, string Character, string CharacterClass, int Level) : LogEvent(Time);

public sealed record CharacterSlain(DateTime Time, string Character) : LogEvent(Time);

/// <summary>"You have received a Passive Skill Point." — нагорода за квест.</summary>
public sealed record PassivePointsReceived(DateTime Time, int Count) : LogEvent(Time);

/// <summary>"Successfully allocated/unallocated passive skill id: ..., name: X".</summary>
public sealed record PassiveAllocated(DateTime Time, string Id, string Name, bool Allocated) : LogEvent(Time)
{
    /// <summary>Нода асценду (id "AscendancyDeadeye12" тощо) — її беруть після проходження лабіринту.</summary>
    public bool IsAscendancy => Id.StartsWith("Ascendancy", StringComparison.OrdinalIgnoreCase);
}

/// <summary>"[LOADING SCREEN] (зона) Duration = N seconds" — скільки тривало завантаження.</summary>
public sealed record LoadingScreen(DateTime Time, double Seconds) : LogEvent(Time);

/// <summary>Рядок з відповіді на команду /passives: "N Passive Skill Points from quests".</summary>
public sealed record QuestPointsReported(DateTime Time, int Count) : LogEvent(Time);

/// <summary>
/// "***** LOG FILE OPENING *****" — гра запустилася. <see cref="PreviousLineTime"/> — час останнього рядка
/// перед цим, тобто приблизно коли гру було закрито.
/// </summary>
public sealed record GameStarted(DateTime Time, DateTime? PreviousLineTime) : LogEvent(Time);

public static class AreaIds
{
    public static int? ActOf(string? areaId)
    {
        if (areaId is null) return null;
        var parts = areaId.Split('_');
        if (parts.Length < 2 || (parts[0] != "1" && parts[0] != "2")) return null;
        return int.TryParse(parts[1], out var act) && act is >= 1 and <= 11 ? act : null;
    }

    /// <summary>Місто або схованка: тут вендори, сюди повертаються між зонами.</summary>
    public static bool IsTown(string? areaId, string? areaName) =>
        (areaId is not null && (areaId.EndsWith("_town", StringComparison.OrdinalIgnoreCase)
                                || areaId.StartsWith("Hideout", StringComparison.OrdinalIgnoreCase)))
        || (areaName?.Contains("Hideout", StringComparison.OrdinalIgnoreCase) ?? false);
}
