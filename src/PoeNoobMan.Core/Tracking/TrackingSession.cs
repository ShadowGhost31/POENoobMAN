using PoeNoobMan.Core.Guides;
using PoeNoobMan.Core.Log;
using PoeNoobMan.Core.Progress;
using PoeNoobMan.Core.Splits;

namespace PoeNoobMan.Core.Tracking;

public enum TimelineKind
{
    Area,
    Level,
    Death,
    StepUnlocked,
    StepDone,
    Split,
    RunStarted,
    Points,
    Pause,
}

public sealed record TimelineEntry(DateTime Time, TimelineKind Kind, string Text, string? Id = null);

/// <summary>
/// Перетворює події Client.txt на зміни прогресу і таймера. Однаково використовується застосунком
/// і відтворенням логу, тому відтворення перевіряє саме ту логіку, що працює в грі.
/// </summary>
public sealed class TrackingSession(ProgressEngine engine, SplitTracker tracker)
{
    public ProgressEngine Engine { get; } = engine;
    public SplitTracker Tracker { get; } = tracker;

    /// <summary>Ім'я персонажа; порожнє — приймати рядки про рівень від будь-кого.</summary>
    public string CharacterName { get; set; } = "";

    public bool TimerEnabled { get; set; } = true;
    public bool AutoStart { get; set; } = true;

    /// <summary>Як стартувати забіг (застосунок спершу архівує попередній).</summary>
    public Action<DateTime>? StartRun { get; set; }

    /// <summary>Детальний журнал змін; потрібен для відтворення і діагностики.</summary>
    public event Action<TimelineEntry>? Timeline;

    /// <summary>Персонаж, від якого прийшов рядок про рівень (для списку в налаштуваннях).</summary>
    public event Action<string>? CharacterSeen;

    private GuideView View => Engine.View;

    public bool IsOurCharacter(string name) =>
        string.IsNullOrWhiteSpace(CharacterName) || string.Equals(name, CharacterName, StringComparison.OrdinalIgnoreCase);

    public void Handle(LogEvent ev)
    {
        switch (ev)
        {
            case AreaEntered a:
                OnArea(a);
                break;

            case LevelUp l:
                CharacterSeen?.Invoke(l.Character);
                if (!IsOurCharacter(l.Character)) break;
                Emit(l.Time, TimelineKind.Level, $"{l.Character} → рівень {l.Level}");
                var unlockedBefore = Snapshot();
                var done = Engine.SetLevel(l.Level);
                ReportUnlocked(l.Time, unlockedBefore);
                Split(new SplitTrigger(TriggerKind.Level, l.Time, Level: l.Level), l.Level);
                StepsDone(done, l.Time);
                break;

            case CharacterSlain s when IsOurCharacter(s.Character):
                Emit(s.Time, TimelineKind.Death, $"{s.Character} загинув");
                Tracker.AddDeath();
                break;

            case PassivePointsReceived p:
                var byQuest = Engine.OnPassivePointsReceived(p.Count);
                Emit(p.Time, TimelineKind.Points, $"+{p.Count} очко за квест (усього за квести {Engine.QuestPoints})");
                StepsDone(byQuest, p.Time);
                break;

            case PassiveAllocated pa:
                var takenBefore = Engine.State.TakenNodes.Count;
                var byPassive = Engine.OnPassiveAllocated(pa.Name, pa.Allocated, pa.IsAscendancy);
                if (pa.IsAscendancy || Engine.State.TakenNodes.Count != takenBefore)
                    Emit(pa.Time, TimelineKind.Points, $"{(pa.Allocated ? "взято" : "знято")}: {pa.Name}{(pa.IsAscendancy ? $" (асценд, {Engine.State.AscendancyPoints})" : "")}");
                StepsDone(byPassive, pa.Time);
                break;

            case LoadingScreen ls:
                Tracker.AddLoad(ls.Seconds);
                break;

            case QuestPointsReported q:
                Engine.OnQuestPointsReported(q.Count);
                Emit(q.Time, TimelineKind.Points, $"/passives: {q.Count} очок за квести");
                break;

            case GameStarted g when g.PreviousLineTime is { } closed && Tracker.IsRunning:
                Tracker.AddPause(closed, g.Time);
                Emit(g.Time, TimelineKind.Pause, $"Гра була закрита {g.Time - closed:d\\.hh\\:mm\\:ss} — не враховано в часі забігу");
                break;
        }
    }

    private void OnArea(AreaEntered a)
    {
        Emit(a.Time, TimelineKind.Area, a.AreaId is null ? a.AreaName : $"{a.AreaName} [{a.AreaId}]", a.AreaId);

        if (TimerEnabled && AutoStart && !Tracker.IsRunning && View.Guide.TimerStart?.Area is { } startArea
            && ProgressEngine.AreaMatches(startArea, a.AreaId, a.AreaName))
        {
            if (StartRun is { } start) start(a.Time);
            else Tracker.Start(a.Time, View.Guide.Id, View.Guide.Version, CharacterName, View.Options);
            Emit(a.Time, TimelineKind.RunStarted, "Старт забігу");
        }

        var unlockedBefore = Snapshot();
        var done = Engine.OnAreaEntered(a.AreaId, a.AreaName);
        ReportUnlocked(a.Time, unlockedBefore);
        Split(new SplitTrigger(TriggerKind.Area, a.Time, a.AreaId, a.AreaName), Engine.State.Level);
        StepsDone(done, a.Time);
    }

    /// <summary>Ручна відмітка кроків (F10, галочки) теж може зафіксувати спліт.</summary>
    public void StepsDone(IReadOnlyList<string> ids, DateTime time)
    {
        foreach (var id in ids)
        {
            Emit(time, TimelineKind.StepDone, StepText(id), id);
            Split(new SplitTrigger(TriggerKind.Step, time, id), Engine.State.Level);
        }
    }

    private void Split(SplitTrigger t, int level)
    {
        if (!TimerEnabled) return;
        var before = Tracker.Current?.Splits.Count ?? 0;
        if (!Tracker.OnTrigger(t, level) || Tracker.Current is not { } run) return;
        foreach (var s in run.Splits.Skip(before))
        {
            var label = Tracker.Defs.FirstOrDefault(d => d.Id == s.Id)?.Label ?? s.Id;
            Emit(t.Time, TimelineKind.Split, $"{label} — {TimeSpan.FromSeconds(s.Seconds ?? 0):h\\:mm\\:ss}", s.Id);
        }
    }

    private HashSet<string>? Snapshot() => Timeline is null ? null : [.. Engine.State.UnlockedSteps];

    private void ReportUnlocked(DateTime time, HashSet<string>? before)
    {
        if (before is null) return;
        foreach (var id in Engine.State.UnlockedSteps.Where(id => !before.Contains(id)))
            Emit(time, TimelineKind.StepUnlocked, StepText(id), id);
    }

    private string StepText(string id) => View.Steps.FirstOrDefault(s => s.Id == id)?.Text ?? id;

    private void Emit(DateTime time, TimelineKind kind, string text, string? id = null) =>
        Timeline?.Invoke(new TimelineEntry(time, kind, text, id));
}
