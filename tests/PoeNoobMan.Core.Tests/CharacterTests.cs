using PoeNoobMan.Core.Guides;
using PoeNoobMan.Core.Log;
using PoeNoobMan.Core.Progress;
using PoeNoobMan.Core.Splits;
using PoeNoobMan.Core.Tracking;

namespace PoeNoobMan.Core.Tests;

/// <summary>Новий персонаж і перемикання персонажів — за реальними рядками Client.txt.</summary>
public class CharacterTests
{
    private static readonly DateTime T0 = new(2026, 10, 30, 23, 0, 0);

    private static TrackingSession Session(string guideId, Dictionary<string, string>? options = null)
    {
        var repo = new GuideRepository(TestData.GuidesDir);
        repo.Reload();
        var view = GuideView.Create(repo.Find(guideId, null)!.Guide!, options);
        var saved = new Dictionary<string, ProgressState>();
        return new TrackingSession(new ProgressEngine(view, new ProgressState()), new SplitTracker(view.Splits, []))
        {
            LoadProgress = name => saved.TryGetValue(name, out var s) ? s : new ProgressState(),
            SaveProgress = (name, s) => saved[name] = s,
        };
    }

    private static void Play(TrackingSession s, params string[] lines)
    {
        var parser = new LogLineParser();
        var t = T0;
        foreach (var line in lines)
        {
            t = t.AddSeconds(30);
            var stamp = t.ToString("yyyy/MM/dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            var ev = parser.Parse($"{stamp} 1 a [INFO Client 1] {line}");
            if (ev is not null) s.Handle(ev);
        }
    }

    private const string Strand = "Generating level 1 area \"1_1_1\" with seed 1";
    private const string Town = "Generating level 13 area \"1_1_town\" with seed 1";
    private const string Coast = "Generating level 2 area \"1_1_2\" with seed 1";
    private const string Tutorial = "Requesting to complete all tutorials due to the client setting";

    [Fact]
    public void New_character_restarts_guide_and_timer()
    {
        // Реальна послідовність з логу 30.10: персонаж уже пройшов Act 1, потім створено нового
        var s = Session("rain-of-arrows-deadeye");
        Play(s, Strand, ": You have entered The Twilight Strand.", Tutorial, ": Old (Ranger) is now level 2",
            Town, ": You have entered Lioneye's Watch.", Coast, ": You have entered The Coast.");
        Assert.Contains("a1_strand", s.Engine.State.DoneSteps);
        Assert.Equal("Old", s.ActiveCharacter);

        Play(s, "[SCENE] Set Source [(unknown)]", Strand, ": You have entered The Twilight Strand.", Tutorial);
        Assert.Null(s.ActiveCharacter);
        Assert.Empty(s.Engine.State.DoneSteps);
        Assert.Contains("a1_strand", s.Engine.GetSteps().Current.Select(x => x.Id));
        Assert.True(s.Tracker.IsRunning);
        Assert.Empty(s.Tracker.Current!.Splits);

        Play(s, ": New (Ranger) is now level 2", Town, ": You have entered Lioneye's Watch.");
        Assert.Equal("New", s.ActiveCharacter);
        Assert.Contains("a1_strand", s.Engine.State.DoneSteps);
        Assert.Equal(["hillock"], s.Tracker.Current!.Splits.Select(x => x.Id));
    }

    [Fact]
    public void Mule_then_main_character_keeps_the_guide_and_timer()
    {
        var s = Session("arma-crema-scion", new Dictionary<string, string> { ["mule"] = "yes" });
        Play(s, Strand, ": You have entered The Twilight Strand.", Tutorial, ": Mule (Witch) is now level 2",
            Town, ": You have entered Lioneye's Watch.");
        Assert.Contains("mule_create", s.Engine.State.DoneSteps);
        var runStart = s.Tracker.Current!.StartedAt;

        Play(s, "[SCENE] Set Source [(unknown)]", Strand, ": You have entered The Twilight Strand.", Tutorial,
            ": Main (Scion) is now level 2", Town, ": You have entered Lioneye's Watch.");
        Assert.Equal("Main", s.ActiveCharacter);
        Assert.Contains("mule_create", s.Engine.State.DoneSteps);
        Assert.Contains("a1_strand", s.Engine.State.DoneSteps);
        Assert.Equal(runStart, s.Tracker.Current!.StartedAt);   // час мула входить у забіг
    }

    [Fact]
    public void Logging_into_another_character_switches_progress()
    {
        var s = Session("rain-of-arrows-deadeye");
        Play(s, Strand, ": You have entered The Twilight Strand.", Tutorial, ": First (Ranger) is now level 2",
            Town, ": You have entered Lioneye's Watch.");
        Play(s, Strand, ": You have entered The Twilight Strand.", Tutorial, ": Second (Ranger) is now level 2");
        Assert.Equal("Second", s.ActiveCharacter);
        Assert.DoesNotContain("a1_strand", s.Engine.State.DoneSteps);

        // Рівень гравця групи не перемикає прогрес
        Play(s, ": Friend has joined the area.", ": Friend (Witch) is now level 30");
        Assert.Equal("Second", s.ActiveCharacter);

        // Вийшли на вибір персонажа і зайшли першим — його прогрес повертається з першого ж рядка про рівень
        Play(s, "[SCENE] Set Source [(unknown)]", ": First (Ranger) is now level 3");
        Assert.Equal("First", s.ActiveCharacter);
        Assert.Contains("a1_strand", s.Engine.State.DoneSteps);
        Assert.Equal(3, s.Engine.State.Level);
    }
}
