using System.Text.Json.Serialization;

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

    /// <summary>Зафіксовані спліти в порядку фіксації. Seconds — ігровий час без пауз.</summary>
    public List<SplitTime> Splits { get; set; } = [];

    /// <summary>Проміжки, коли гра була закрита; у час забігу не входять.</summary>
    public List<PauseInterval> Pauses { get; set; } = [];

    /// <summary>Сумарний час екранів завантаження за логом.</summary>
    public double LoadSeconds { get; set; }

    /// <summary>Час забігу рахується без завантажень (як load-removed у спідранерів).</summary>
    public bool LoadsRemoved { get; set; } = true;

    [JsonIgnore]
    public double? TotalSeconds => Finished && Splits.LastOrDefault()?.Seconds is { } s ? s : null;

    [JsonIgnore]
    public bool IsPaused => Pauses.Any(p => p.To is null);

    /// <summary>Скільки секунд пауз було від старту до моменту <paramref name="at"/> (перетини рахуються один раз).</summary>
    public double PausedSeconds(DateTime at)
    {
        var spans = Pauses
            .Select(p => (From: p.From < StartedAt ? StartedAt : p.From, To: p.To is { } t && t < at ? t : at))
            .Where(p => p.To > p.From)
            .OrderBy(p => p.From)
            .ToList();
        double total = 0;
        DateTime? curFrom = null, curTo = null;
        foreach (var (f, t) in spans)
        {
            if (curTo is { } ct && f <= ct)
            {
                if (t > ct) curTo = t;
                continue;
            }
            if (curFrom is { } cf) total += (curTo!.Value - cf).TotalSeconds;
            curFrom = f;
            curTo = t;
        }
        if (curFrom is { } lf) total += (curTo!.Value - lf).TotalSeconds;
        return total;
    }

    /// <summary>
    /// Ігровий час від старту до моменту <paramref name="at"/>: без пауз і, якщо <see cref="LoadsRemoved"/>,
    /// без завантажень (події обробляються по порядку, тому LoadSeconds на момент фіксації спліту точний).
    /// </summary>
    public double ActiveSeconds(DateTime at) =>
        Math.Max(0, (at - StartedAt).TotalSeconds - PausedSeconds(at) - (LoadsRemoved ? LoadSeconds : 0));

    public double? SecondsOf(string splitId) => Splits.FirstOrDefault(s => s.Id == splitId)?.Seconds;
}

public sealed class SplitTime
{
    public string Id { get; set; } = "";
    public double? Seconds { get; set; }
    public int Level { get; set; }
    public int Deaths { get; set; }
}

public sealed class PauseInterval
{
    public DateTime From { get; set; }

    /// <summary>null — пауза ще триває.</summary>
    public DateTime? To { get; set; }
}

public enum TriggerKind
{
    Step,
    Level,
    Area,
}

/// <param name="Value">ID кроку або ID зони.</param>
public readonly record struct SplitTrigger(TriggerKind Kind, DateTime Time, string? Value = null, string? AreaName = null, int Level = 0);
