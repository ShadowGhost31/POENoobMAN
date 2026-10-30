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
    private AreaEntered? _lastArea;
    private bool _sawCharacterSelect;
    private readonly HashSet<string> _otherPlayers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Прогрес поточного персонажа; змінюється, коли створено нового персонажа або зайшли іншим.</summary>
    public ProgressEngine Engine { get; private set; } = engine;
    public SplitTracker Tracker { get; } = tracker;

    /// <summary>
    /// Ім'я персонажа з налаштувань. Порожнє — автоматичний режим: персонаж визначається з логу
    /// (новий персонаж — з рядка про туторіали, ім'я — з першого рядка про рівень).
    /// </summary>
    public string CharacterName { get; set; } = "";

    /// <summary>Персонаж, яким зараз грають (за логом); null — щойно створений, ім'я ще невідоме.</summary>
    public string? ActiveCharacter { get; set; }

    /// <summary>Під яким ім'ям зберігати прогрес.</summary>
    public string ProgressKey => !string.IsNullOrWhiteSpace(CharacterName) ? CharacterName : ActiveCharacter ?? "";

    /// <summary>Завантажити збережений прогрес персонажа (у відтворенні — новий порожній).</summary>
    public Func<string, ProgressState>? LoadProgress { get; set; }

    /// <summary>Зберегти прогрес персонажа перед перемиканням на іншого.</summary>
    public Action<string, ProgressState>? SaveProgress { get; set; }

    /// <summary>Перемкнули прогрес (новий персонаж або інший персонаж).</summary>
    public event Action? ProgressSwitched;

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
        !string.IsNullOrWhiteSpace(CharacterName)
            ? string.Equals(name, CharacterName, StringComparison.OrdinalIgnoreCase)
            : ActiveCharacter is null || string.Equals(name, ActiveCharacter, StringComparison.OrdinalIgnoreCase);

    public void Handle(LogEvent ev)
    {
        switch (ev)
        {
            case AreaEntered a:
                _lastArea = a;
                OnArea(a);
                break;

            case CharacterCreated c:
                OnCharacterCreated(c);
                break;

            case CharacterSelect:
                _sawCharacterSelect = true;
                break;

            case PlayerJoined pj:
                _otherPlayers.Add(pj.Name);
                break;

            case LevelUp l:
                CharacterSeen?.Invoke(l.Character);
                IdentifyCharacter(l);
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

    /// <summary>
    /// Новий персонаж: гайд і таймер починаються з нуля. Виняток — мул: якщо до маршруту кампанії ще не дійшли
    /// (виконано лише кроки перед маршрутом, як-от кроки мула), прогрес і забіг тривають.
    /// </summary>
    private void OnCharacterCreated(CharacterCreated c)
    {
        var previous = ActiveCharacter;
        ActiveCharacter = null;
        if (!Engine.HasRouteProgress)
        {
            Emit(c.Time, TimelineKind.RunStarted, previous is null ? "Новий персонаж" : $"Новий персонаж (після {previous}) — продовжуємо гайд");
            ProgressSwitched?.Invoke();
            return;
        }

        if (previous is not null) SaveProgress?.Invoke(previous, Engine.State);
        Engine = new ProgressEngine(Engine.View, new ProgressState());
        Emit(c.Time, TimelineKind.RunStarted, "Новий персонаж — гайд і таймер з початку");

        // Вхід на Twilight Strand уже був (рядок про туторіали йде після нього): повторюємо його для нового прогресу
        if (_lastArea is { } strand)
        {
            if (TimerEnabled && AutoStart) StartNewRun(strand.Time);
            Engine.OnAreaEntered(strand.AreaId, strand.AreaName);
        }
        ProgressSwitched?.Invoke();
    }

    /// <summary>
    /// Рядок про рівень називає персонажа: щойно створений отримує ім'я, інший — перемикає прогрес.
    /// Гра пише в лог і рівні гравців групи, тому чужі імена (з «has joined the area») ігноруються,
    /// а перемикання на іншого персонажа можливе лише після виходу на екран вибору персонажа.
    /// </summary>
    private void IdentifyCharacter(LevelUp l)
    {
        if (!string.IsNullOrWhiteSpace(CharacterName) || _otherPlayers.Contains(l.Character)) return;
        if (ActiveCharacter is null)
        {
            ActiveCharacter = l.Character;
            _sawCharacterSelect = false;
            ProgressSwitched?.Invoke();
            return;
        }
        if (string.Equals(ActiveCharacter, l.Character, StringComparison.OrdinalIgnoreCase))
        {
            _sawCharacterSelect = false;
            return;
        }
        if (!_sawCharacterSelect) return;
        _sawCharacterSelect = false;

        SaveProgress?.Invoke(ActiveCharacter, Engine.State);
        ActiveCharacter = l.Character;
        Engine = new ProgressEngine(Engine.View, LoadProgress?.Invoke(l.Character) ?? new ProgressState());
        Engine.RestoreCurrentArea();
        Emit(l.Time, TimelineKind.RunStarted, $"Інший персонаж: {l.Character}");
        ProgressSwitched?.Invoke();
    }

    private void StartNewRun(DateTime at)
    {
        if (StartRun is { } start) start(at);
        else
        {
            Tracker.Reset();
            Tracker.Start(at, View.Guide.Id, View.Guide.Version, ProgressKey, View.Options);
        }
    }

    private void OnArea(AreaEntered a)
    {
        Emit(a.Time, TimelineKind.Area, a.AreaId is null ? a.AreaName : $"{a.AreaName} [{a.AreaId}]", a.AreaId);

        if (TimerEnabled && AutoStart && !Tracker.IsRunning && View.Guide.TimerStart?.Area is { } startArea
            && ProgressEngine.AreaMatches(startArea, a.AreaId, a.AreaName))
        {
            StartNewRun(a.Time);
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
