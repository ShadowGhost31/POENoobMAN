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

public static class AreaIds
{
    public static int? ActOf(string? areaId)
    {
        if (areaId is null) return null;
        var parts = areaId.Split('_');
        if (parts.Length < 2 || (parts[0] != "1" && parts[0] != "2")) return null;
        return int.TryParse(parts[1], out var act) && act is >= 1 and <= 11 ? act : null;
    }
}
