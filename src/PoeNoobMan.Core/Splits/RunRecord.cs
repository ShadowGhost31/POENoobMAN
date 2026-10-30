namespace PoeNoobMan.Core.Splits;

public enum Comparison
{
    PersonalBest,
    LastRun,
    SumOfBest,
}

public sealed class RunRecord
{
    public string Id { get; set; } = "";
    public string GuideId { get; set; } = "";
    public string GuideVersion { get; set; } = "";
    public string Character { get; set; } = "";
    public Dictionary<string, string> Options { get; set; } = new();
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public bool Finished { get; set; }
    public int Deaths { get; set; }

    /// <summary>По одному запису на кожен спліт гайда до поточного; пропущені мають Seconds = null.</summary>
    public List<SplitTime> Splits { get; set; } = [];

    public double? TotalSeconds => Finished && Splits.LastOrDefault()?.Seconds is { } s ? s : null;

    public double? SecondsOf(string splitId) => Splits.FirstOrDefault(s => s.Id == splitId)?.Seconds;
}

public sealed class SplitTime
{
    public string Id { get; set; } = "";
    public double? Seconds { get; set; }
    public int Level { get; set; }
    public int Deaths { get; set; }
}

public enum TriggerKind
{
    Step,
    Level,
    Area,
}

/// <param name="Value">ID кроку або ID зони.</param>
public readonly record struct SplitTrigger(TriggerKind Kind, DateTime Time, string? Value = null, string? AreaName = null, int Level = 0);
