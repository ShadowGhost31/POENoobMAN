// Відтворює Client.txt через ту саму логіку, що й застосунок, і показує, що і коли спрацювало.
//
//   dotnet run --project tools/PoeNoobMan.Replay -- --log "<шлях>\Client.txt" --guide guides/arma-crema-scion@3.29.1.yaml
//       --char ІмяПерсонажа [--opt mule=no] [--from 2026/07/24] [--to 2026/08/30] [--timeline]
//
// Без --char рядки про рівень приймаються від будь-кого (як у застосунку з порожнім іменем).

using System.Globalization;
using System.Text;
using PoeNoobMan.Core.Guides;
using PoeNoobMan.Core.Log;
using PoeNoobMan.Core.Progress;
using PoeNoobMan.Core.Splits;
using PoeNoobMan.Core.Tracking;

Console.OutputEncoding = Encoding.UTF8;

string? logPath = null, guidePath = null, character = null;
DateTime? from = null, to = null;
var options = new Dictionary<string, string>();
var showTimeline = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--log": logPath = args[++i]; break;
        case "--guide": guidePath = args[++i]; break;
        case "--char": character = args[++i]; break;
        case "--opt":
            var kv = args[++i].Split('=', 2);
            options[kv[0]] = kv[1];
            break;
        case "--from": from = DateTime.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--to": to = DateTime.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--timeline": showTimeline = true; break;
        default:
            Console.Error.WriteLine($"Невідомий аргумент {args[i]}");
            return 2;
    }
}
if (logPath is null || guidePath is null)
{
    Console.Error.WriteLine("Потрібні --log і --guide");
    return 2;
}

var load = GuideLoader.LoadFile(guidePath);
if (!load.IsValid)
{
    Console.Error.WriteLine($"Гайд з помилками: {string.Join("; ", load.Errors)}");
    return 1;
}

var view = GuideView.Create(load.Guide!, options);
var engine = new ProgressEngine(view, new ProgressState());
var tracker = new SplitTracker(view.Splits, []);
var session = new TrackingSession(engine, tracker) { CharacterName = character ?? "" };

var timeline = new List<TimelineEntry>();
// Що показував би оверлей у момент кожного спліту (після індексу запису в timeline)
var overlayAt = new Dictionary<int, string>();
session.Timeline += e =>
{
    timeline.Add(e);
    if (e.Kind != TimelineKind.Split) return;
    var st = engine.GetSteps();
    var lines = st.Current.Take(4).Select(s => $"A{s.Act} {s.Text}").ToList();
    if (lines.Count < 4 && st.Blocker is { } b) lines.Add($"далі ({b.When}): {b.Text}");
    overlayAt[timeline.Count - 1] = string.Join("\n", lines.Select(l => $"                    │ {l}"));
};

var parser = new LogLineParser();
var firstVisit = new Dictionary<string, DateTime>();
var runStarted = (DateTime?)null;
var events = 0;
using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
using (var reader = new StreamReader(fs, Encoding.UTF8))
{
    while (reader.ReadLine() is { } line)
    {
        if (parser.Parse(line) is not { } ev) continue;
        if (from is { } f && ev.Time < f) continue;
        if (to is { } t && ev.Time > t) break;
        events++;
        session.Handle(ev);
        if (tracker.Current is { } run && runStarted is null) runStarted = run.StartedAt;
        if (ev is AreaEntered a && a.AreaId is { } id && runStarted is not null && !firstVisit.ContainsKey(id)) firstVisit[id] = a.Time;
    }
}

if (showTimeline)
{
    for (var i = 0; i < timeline.Count; i++)
    {
        var e = timeline[i];
        if (e.Kind == TimelineKind.Death) continue;
        var mark = e.Kind switch
        {
            TimelineKind.Area => "   зона ",
            TimelineKind.Level => "  рівень ",
            TimelineKind.StepUnlocked => " + крок  ",
            TimelineKind.StepDone => " ✓ крок  ",
            TimelineKind.Split => " ⏱ спліт ",
            TimelineKind.RunStarted => " ▶ старт ",
            TimelineKind.Points => " ★ очки  ",
            TimelineKind.Pause => " ⏸ пауза ",
            _ => "         ",
        };
        Console.WriteLine($"{e.Time:MM-dd HH:mm:ss}{mark}{e.Text}");
        if (overlayAt.TryGetValue(i, out var overlay)) Console.WriteLine(overlay);
    }
    Console.WriteLine();
}

var final = engine.GetSteps();
Console.WriteLine("Оверлей зараз:");
foreach (var s in final.Current.Take(6)) Console.WriteLine($"  A{s.Act} {s.Id}: {s.Text}");
if (final.Blocker is { } fb) Console.WriteLine($"  далі ({fb.When}): {fb.Id}");
Console.WriteLine($"  пропущено кроків: {final.Passed.Count}; видимих: {final.Current.Count}");
Console.WriteLine();

Console.WriteLine($"Подій у лозі: {events}. Рівень: {engine.State.Level}. Очки: {engine.Points} (за квести {engine.QuestPoints}).");
Console.WriteLine($"Смертей у забігу: {tracker.Current?.Deaths ?? 0}.");
Console.WriteLine();

// Кроки з умовою по зоні: чи відкрилися, і чи вчасно (при першому вході в зону)
var unlockTime = timeline.Where(e => e.Kind == TimelineKind.StepUnlocked && e.Id is not null)
    .GroupBy(e => e.Id!).ToDictionary(g => g.Key, g => g.First().Time);
Console.WriteLine("Кроки з умовою по зоні (показано лише проблеми):");
var problems = 0;
var okCount = 0;
var notVisited = new List<string>();
foreach (var s in view.Steps.Where(s => s.When?.Area is not null))
{
    var area = s.When!.Area!;
    var visited = firstVisit.TryGetValue(area, out var v) ? v : (DateTime?)null;
    var unlocked = unlockTime.TryGetValue(s.Id, out var u) ? u : (DateTime?)null;
    string? verdict = null;
    if (unlocked is null && visited is null) notVisited.Add($"{s.Id} ({AreaNames.Display(area)})");
    else if (unlocked is null) verdict = $"НЕ ВІДКРИВСЯ, хоча зону відвідано {visited:MM-dd HH:mm}";
    else if (visited is { } vv && (unlocked.Value - vv) > TimeSpan.FromSeconds(1)) verdict = $"відкрився ПІЗНО: {unlocked:MM-dd HH:mm}, перший вхід {vv:MM-dd HH:mm}";
    if (verdict is null) { okCount++; continue; }
    problems++;
    Console.WriteLine($"  A{s.Act,-2} {s.Id,-22} {AreaNames.Display(area),-38} {verdict}");
}
Console.WriteLine($"  вчасно відкрились: {okCount}; зону не відвідано: {notVisited.Count}{(notVisited.Count > 0 ? " — " + string.Join(", ", notVisited) : "")}");
Console.WriteLine();

// Як закрились усі кроки гайда
var doneAt = timeline.Where(e => e.Kind == TimelineKind.StepDone && e.Id is not null).GroupBy(e => e.Id!).ToDictionary(g => g.Key, g => g.First().Time);
var routeSteps = view.Steps.Where(s => s.When?.Area is not null && s.Done?.Area is not null).ToList();
var quests = view.Steps.Where(s => s.Points > 0).ToList();
var hints = view.Steps.Where(s => s.Done is null && s.Points == 0).ToList();
Console.WriteLine("Автоматичне закриття кроків:");
Console.WriteLine($"  кроки маршруту: закрито {routeSteps.Count(s => doneAt.ContainsKey(s.Id))} з {routeSteps.Count}");
foreach (var s in routeSteps.Where(s => !doneAt.ContainsKey(s.Id) && unlockTime.ContainsKey(s.Id)))
    Console.WriteLine($"    не закрито (у {AreaNames.Display(s.Done!.Area!)} після цієї зони не входили): {s.Id}");
Console.WriteLine($"  квести з очками: закрито {quests.Count(s => doneAt.ContainsKey(s.Id))} з {quests.Count}");
Console.WriteLine($"  підказки (геми, вендори): закрито {hints.Count(s => doneAt.ContainsKey(s.Id))} з {hints.Count}, решта прихована як пройдена: {hints.Count(s => !doneAt.ContainsKey(s.Id) && final.Passed.Contains(s.Id))}");
var other = view.Steps.Except(routeSteps).Except(quests).Except(hints).ToList();
foreach (var s in other) Console.WriteLine($"  {s.Id}: {(doneAt.TryGetValue(s.Id, out var dt) ? $"закрито {dt:MM-dd HH:mm}" : "не закрито")}");
Console.WriteLine();

Console.WriteLine("Спліти:");
foreach (var row in tracker.Rows())
{
    var t = row.Current is { } c ? TimeSpan.FromSeconds(c).ToString(@"h\:mm\:ss") : "—";
    var lvl = tracker.Current?.Splits.FirstOrDefault(x => x.Id == row.Def.Id)?.Level;
    Console.WriteLine($"  {row.Def.Label,-28} {t,10}{(lvl is { } l ? $"  рів. {l}" : "")}");
}
Console.WriteLine();
Console.WriteLine(problems == 0 ? "Проблем з відкриттям кроків не знайдено." : $"Проблем: {problems}.");
return problems == 0 ? 0 : 3;
