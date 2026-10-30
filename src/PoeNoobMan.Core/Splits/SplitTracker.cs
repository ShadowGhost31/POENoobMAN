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
    int Deaths);

public sealed record SplitRow(SplitDef Def, double? Current, double? Last, double? Best, double? SumOfBest, bool Recorded);

/// <summary>Таймер забігу зі сплітами, як у спідранерів. Логіка без інтерфейсу.</summary>
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

    public void SetDefs(IReadOnlyList<SplitDef> defs) => _defs = defs;

    public void Load(RunRecord? current) => Current = current;

    public void SetHistory(IReadOnlyList<RunRecord> history)
    {
        _pb = new();
        _last = new();
        _bestSegment = new();
        _sumOfBest = new();

        var pb = history.Where(r => r.TotalSeconds is not null).MinBy(r => r.TotalSeconds);
        if (pb is not null) _pb = Cumulative(pb);

        var last = history.Where(r => r.Splits.Any(s => s.Seconds is not null)).MaxBy(r => r.StartedAt);
        if (last is not null) _last = Cumulative(last);

        foreach (var run in history)
            foreach (var (id, seg) in Segments(run))
                if (!_bestSegment.TryGetValue(id, out var best) || seg < best) _bestSegment[id] = seg;

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
        };
    }

    /// <summary>
    /// Фіксує спліт, якщо подія відповідає наступному або одному з пізніших сплітів.
    /// Проміжні спліти тоді позначаються пропущеними.
    /// </summary>
    public bool OnTrigger(SplitTrigger t, int level)
    {
        if (Current is not { Finished: false } run) return false;
        for (var i = run.Splits.Count; i < _defs.Count; i++)
        {
            if (!Matches(_defs[i].On, t)) continue;
            while (run.Splits.Count < i) run.Splits.Add(new SplitTime { Id = _defs[run.Splits.Count].Id, Level = level, Deaths = run.Deaths });
            Record(run, t.Time, level);
            return true;
        }
        return false;
    }

    public bool ManualSplit(DateTime now, int level)
    {
        if (Current is not { Finished: false } run || run.Splits.Count >= _defs.Count) return false;
        Record(run, now, level);
        return true;
    }

    public bool Undo()
    {
        if (Current is not { } run || run.Splits.Count == 0) return false;
        run.Splits.RemoveAt(run.Splits.Count - 1);
        while (run.Splits.Count > 0 && run.Splits[^1].Seconds is null) run.Splits.RemoveAt(run.Splits.Count - 1);
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

    public TimerSnapshot Snapshot(DateTime now, Comparison comparison)
    {
        if (Current is not { } run)
            return new TimerSnapshot(false, false, TimeSpan.Zero, null, null, null, false, _defs.FirstOrDefault()?.Label, CompareAt(comparison, 0), TimerTone.Neutral, 0);

        var elapsed = (run.Finished && run.EndedAt is { } end ? end : now) - run.StartedAt;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

        var lastIndex = run.Splits.FindLastIndex(s => s.Seconds is not null);
        string? lastLabel = null;
        TimeSpan? lastTime = null, lastDelta = null;
        var gold = false;
        if (lastIndex >= 0 && lastIndex < _defs.Count)
        {
            var split = run.Splits[lastIndex];
            lastLabel = _defs[lastIndex].Label;
            lastTime = TimeSpan.FromSeconds(split.Seconds!.Value);
            if (CompareAt(comparison, lastIndex) is { } cmp) lastDelta = lastTime - cmp;
            gold = SegmentOf(run, lastIndex) is { } seg && _bestSegment.TryGetValue(split.Id, out var best) && seg < best;
        }

        var nextIndex = run.Splits.Count;
        var nextLabel = !run.Finished && nextIndex < _defs.Count ? _defs[nextIndex].Label : null;
        var nextCompare = nextLabel is null ? null : CompareAt(comparison, nextIndex);

        var tone = TimerTone.Neutral;
        if (nextCompare is { } nc && elapsed > nc) tone = TimerTone.Behind;
        else if (lastDelta is { } d) tone = d <= TimeSpan.Zero ? TimerTone.Ahead : TimerTone.Behind;

        return new TimerSnapshot(true, run.Finished, elapsed, lastLabel, lastTime, lastDelta, gold, nextLabel, nextCompare, tone, run.Deaths);
    }

    public IReadOnlyList<SplitRow> Rows()
    {
        var rows = new List<SplitRow>();
        for (var i = 0; i < _defs.Count; i++)
        {
            var d = _defs[i];
            var recorded = Current is { } run && i < run.Splits.Count;
            rows.Add(new SplitRow(d, recorded ? Current!.Splits[i].Seconds : null,
                _last.TryGetValue(d.Id, out var l) ? l : null,
                _pb.TryGetValue(d.Id, out var p) ? p : null,
                _sumOfBest.TryGetValue(d.Id, out var s) ? s : null,
                recorded));
        }
        return rows;
    }

    private TimeSpan? CompareAt(Comparison comparison, int index)
    {
        if (index < 0 || index >= _defs.Count) return null;
        var source = comparison switch
        {
            Comparison.LastRun => _last,
            Comparison.SumOfBest => _sumOfBest,
            _ => _pb,
        };
        return source.TryGetValue(_defs[index].Id, out var s) ? TimeSpan.FromSeconds(s) : null;
    }

    private void Record(RunRecord run, DateTime at, int level)
    {
        var seconds = Math.Max(0, (at - run.StartedAt).TotalSeconds);
        run.Splits.Add(new SplitTime { Id = _defs[run.Splits.Count].Id, Seconds = seconds, Level = level, Deaths = run.Deaths });
        if (run.Splits.Count == _defs.Count)
        {
            run.Finished = true;
            run.EndedAt = run.StartedAt.AddSeconds(seconds);
        }
    }

    private static bool Matches(Condition on, SplitTrigger t) => t.Kind switch
    {
        TriggerKind.Step => on.Step is { } s && s == t.Value,
        TriggerKind.Level => on.Level is { } l && t.Level >= l,
        TriggerKind.Area => on.Area is { } a && ProgressEngine.AreaMatches(a, t.Value, t.AreaName),
        _ => false,
    };

    private static Dictionary<string, double> Cumulative(RunRecord run) =>
        run.Splits.Where(s => s.Seconds is not null).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().Seconds!.Value);

    private static double? SegmentOf(RunRecord run, int index)
    {
        if (run.Splits[index].Seconds is not { } t) return null;
        if (index == 0) return t;
        return run.Splits[index - 1].Seconds is { } prev ? t - prev : null;
    }

    private static IEnumerable<(string Id, double Segment)> Segments(RunRecord run)
    {
        for (var i = 0; i < run.Splits.Count; i++)
            if (SegmentOf(run, i) is { } seg)
                yield return (run.Splits[i].Id, seg);
    }
}
