using PoeNoobMan.Core.Guides;
using PoeNoobMan.Core.Progress;

namespace PoeNoobMan.Core.Splits;

public enum TimerTone
{
    Neutral,
    Ahead,
    Behind,
}

public sealed record TimerSnapshot(
    bool HasRun,
    bool Finished,
    TimeSpan Elapsed,
    string? LastLabel,
    TimeSpan? LastTime,
    TimeSpan? LastDelta,
    bool LastGold,
    string? NextLabel,
    TimeSpan? NextCompare,
    TimerTone Tone,
    int Deaths,
    bool Paused = false);

public sealed record SplitRow(SplitDef Def, double? Current, double? Last, double? Best, double? SumOfBest, bool Recorded);

/// <summary>
/// Таймер забігу зі сплітами, як у спідранерів. Кожен спліт фіксується, коли вперше стається його подія,
/// незалежно від порядку: якщо рівень 12 набрано вже в Act 2, спліт «Рівень 12» усе одно зафіксується.
/// Забіг завершується останнім сплітом гайда.
/// </summary>
public sealed class SplitTracker
{
    private IReadOnlyList<SplitDef> _defs;
    private Dictionary<string, double> _pb = new();
    private Dictionary<string, double> _last = new();
    private Dictionary<string, double> _bestSegment = new();
    private Dictionary<string, double> _sumOfBest = new();

    public SplitTracker(IReadOnlyList<SplitDef> defs, IReadOnlyList<RunRecord> history)
    {
        _defs = defs;
        SetHistory(history);
    }

    public IReadOnlyList<SplitDef> Defs => _defs;
    public RunRecord? Current { get; private set; }
    public bool IsRunning => Current is { Finished: false };

    public void Load(RunRecord? current)
    {
        // Старі записи могли містити пропущені спліти без часу
        current?.Splits.RemoveAll(s => s.Seconds is null);
        Current = current;
    }

    public void SetHistory(IReadOnlyList<RunRecord> history)
    {
        _pb = new();
        _last = new();
        _bestSegment = new();
        _sumOfBest = new();

        var pb = history.Where(r => r.TotalSeconds is not null).MinBy(r => r.TotalSeconds);
        if (pb is not null) _pb = Times(pb);

        var last = history.Where(r => r.Splits.Any(s => s.Seconds is not null)).MaxBy(r => r.StartedAt);
        if (last is not null) _last = Times(last);

        foreach (var run in history)
        {
            var times = Times(run);
            for (var i = 0; i < _defs.Count; i++)
                if (SegmentOf(times, i) is { } seg && (!_bestSegment.TryGetValue(_defs[i].Id, out var best) || seg < best))
                    _bestSegment[_defs[i].Id] = seg;
        }

        double sum = 0;
        foreach (var d in _defs)
        {
            if (!_bestSegment.TryGetValue(d.Id, out var seg)) break;
            sum += seg;
            _sumOfBest[d.Id] = sum;
        }
    }

    public void Start(DateTime at, string guideId, string guideVersion, string character, IReadOnlyDictionary<string, string> options)
    {
        Current = new RunRecord
        {
            Id = at.ToString("yyyy-MM-dd_HH-mm-ss"),
            GuideId = guideId,
            GuideVersion = guideVersion,
            Character = character,
            Options = new Dictionary<string, string>(options),
            StartedAt = at,
            LoadsRemoved = RemoveLoads,
        };
    }

    /// <summary>Фіксує всі ще не зафіксовані спліти, яким відповідає подія.</summary>
    public bool OnTrigger(SplitTrigger t, int level)
    {
        if (Current is not { Finished: false } run || t.Time < run.StartedAt) return false;
        var any = false;
        foreach (var def in _defs)
        {
            if (run.Finished) break;
            if (IsRecorded(run, def.Id) || !Matches(def.On, t)) continue;
            Record(run, def, t.Time, level);
            any = true;
        }
        return any;
    }

    /// <summary>Ручний спліт: перший ще не зафіксований спліт за порядком гайда.</summary>
    public bool ManualSplit(DateTime now, int level)
    {
        if (Current is not { Finished: false } run) return false;
        var next = _defs.FirstOrDefault(d => !IsRecorded(run, d.Id));
        if (next is null) return false;
        Record(run, next, now, level);
        return true;
    }

    public bool Undo()
    {
        if (Current is not { } run || run.Splits.Count == 0) return false;
        run.Splits.RemoveAt(run.Splits.Count - 1);
        run.Finished = false;
        run.EndedAt = null;
        return true;
    }

    /// <summary>Скидає поточний забіг і повертає його для архіву.</summary>
    public RunRecord? Reset()
    {
        var run = Current;
        Current = null;
        return run;
    }

    public void AddDeath()
    {
        if (IsRunning) Current!.Deaths++;
    }

    /// <summary>Чи віднімати завантаження в нових забігах.</summary>
    public bool RemoveLoads { get; set; } = true;

    public void AddLoad(double seconds)
    {
        if (Current is { Finished: false } run && !run.IsPaused && seconds is > 0 and < 600) run.LoadSeconds += seconds;
    }

    /// <summary>Гра закрилася (вікно зникло): час не рахується до <see cref="EndPause"/>.</summary>
    public void BeginPause(DateTime at)
    {
        if (Current is { Finished: false } run && !run.IsPaused) run.Pauses.Add(new PauseInterval { From = at });
    }

    public void EndPause(DateTime at)
    {
        if (Current?.Pauses.LastOrDefault(p => p.To is null) is { } open) open.To = at < open.From ? open.From : at;
    }

    /// <summary>Проміжок, коли гра була закрита, відомий заднім числом (з логу при наступному запуску гри).</summary>
    public void AddPause(DateTime from, DateTime to)
    {
        if (Current is { Finished: false } run && to > from && to > run.StartedAt)
            run.Pauses.Add(new PauseInterval { From = from, To = to });
    }

    public TimerSnapshot Snapshot(DateTime now, Comparison comparison)
    {
        if (Current is not { } run)
            return new TimerSnapshot(false, false, TimeSpan.Zero, null, null, null, false, _defs.FirstOrDefault()?.Label, CompareFor(comparison, _defs.FirstOrDefault()), TimerTone.Neutral, 0);

        var elapsed = TimeSpan.FromSeconds(Math.Floor(run.ActiveSeconds(run.Finished && run.EndedAt is { } end ? end : now)));

        var times = Times(run);
        string? lastLabel = null;
        TimeSpan? lastTime = null, lastDelta = null;
        var gold = false;
        if (run.Splits.LastOrDefault(s => s.Seconds is not null) is { } last && IndexOf(last.Id) is { } lastIndex)
        {
            var def = _defs[lastIndex];
            lastLabel = def.Label;
            lastTime = TimeSpan.FromSeconds(last.Seconds!.Value);
            if (CompareFor(comparison, def) is { } cmp) lastDelta = lastTime - cmp;
            gold = SegmentOf(times, lastIndex) is { } seg && _bestSegment.TryGetValue(def.Id, out var best) && seg < best;
        }

        var next = run.Finished ? null : _defs.FirstOrDefault(d => !times.ContainsKey(d.Id));
        var nextCompare = next is null ? null : CompareFor(comparison, next);

        var tone = TimerTone.Neutral;
        if (nextCompare is { } nc && elapsed > nc) tone = TimerTone.Behind;
        else if (lastDelta is { } d) tone = d <= TimeSpan.Zero ? TimerTone.Ahead : TimerTone.Behind;

        return new TimerSnapshot(true, run.Finished, elapsed, lastLabel, lastTime, lastDelta, gold, next?.Label, nextCompare, tone, run.Deaths, run.IsPaused);
    }

    public IReadOnlyList<SplitRow> Rows()
    {
        var times = Current is { } run ? Times(run) : new Dictionary<string, double>();
        return _defs.Select(d => new SplitRow(d,
            times.TryGetValue(d.Id, out var c) ? c : null,
            _last.TryGetValue(d.Id, out var l) ? l : null,
            _pb.TryGetValue(d.Id, out var p) ? p : null,
            _sumOfBest.TryGetValue(d.Id, out var s) ? s : null,
            times.ContainsKey(d.Id))).ToList();
    }

    private TimeSpan? CompareFor(Comparison comparison, SplitDef? def)
    {
        if (def is null) return null;
        var source = comparison switch
        {
            Comparison.LastRun => _last,
            Comparison.SumOfBest => _sumOfBest,
            _ => _pb,
        };
        return source.TryGetValue(def.Id, out var s) ? TimeSpan.FromSeconds(s) : null;
    }

    private void Record(RunRecord run, SplitDef def, DateTime at, int level)
    {
        var seconds = run.ActiveSeconds(at);
        run.Splits.Add(new SplitTime { Id = def.Id, Seconds = seconds, Level = level, Deaths = run.Deaths });
        if (ReferenceEquals(def, _defs[^1]))
        {
            run.Finished = true;
            run.EndedAt = at;
            EndPause(at);
        }
    }

    private static bool IsRecorded(RunRecord run, string id) => run.Splits.Any(s => s.Id == id && s.Seconds is not null);

    private int? IndexOf(string id)
    {
        for (var i = 0; i < _defs.Count; i++)
            if (_defs[i].Id == id) return i;
        return null;
    }

    private static bool Matches(Condition on, SplitTrigger t) => t.Kind switch
    {
        TriggerKind.Step => on.Step is { } s && s == t.Value,
        TriggerKind.Level => on.Level is { } l && t.Level >= l,
        TriggerKind.Area => on.Area is { } a && ProgressEngine.AreaMatches(a, t.Value, t.AreaName),
        _ => false,
    };

    private static Dictionary<string, double> Times(RunRecord run) =>
        run.Splits.Where(s => s.Seconds is not null).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().Seconds!.Value);

    /// <summary>Сегмент — час від попереднього спліту гайда; якщо попередній не зафіксовано або події йшли не по порядку, сегмента немає.</summary>
    private double? SegmentOf(Dictionary<string, double> times, int index)
    {
        if (!times.TryGetValue(_defs[index].Id, out var t)) return null;
        if (index == 0) return t;
        if (!times.TryGetValue(_defs[index - 1].Id, out var prev)) return null;
        return t >= prev ? t - prev : null;
    }
}
