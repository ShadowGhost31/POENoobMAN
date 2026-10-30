using PoeNoobMan.Core.Guides;
using PoeNoobMan.Core.Splits;

namespace PoeNoobMan.Core.Tests;

public class SplitTests
{
    private static readonly DateTime T0 = new(2026, 10, 30, 18, 0, 0);
    private static IReadOnlyList<SplitDef> Defs => TestData.Parse(TestData.MiniGuide).Splits;
    private static readonly Dictionary<string, string> NoOptions = new();

    private static RunRecord Run(params double[] seconds)
    {
        var t = new SplitTracker(Defs, []);
        t.Start(T0, "mini", "1.0", "Hero", NoOptions);
        foreach (var s in seconds) t.ManualSplit(T0.AddSeconds(s), 1);
        return t.Current!;
    }

    [Fact]
    public void Records_triggers_in_order_and_finishes()
    {
        var t = new SplitTracker(Defs, []);
        t.Start(T0, "mini", "1.0", "Hero", NoOptions);
        Assert.False(t.OnTrigger(new SplitTrigger(TriggerKind.Level, T0.AddSeconds(5), Level: 4), 4));
        Assert.True(t.OnTrigger(new SplitTrigger(TriggerKind.Step, T0.AddSeconds(10), "start"), 1));
        Assert.True(t.OnTrigger(new SplitTrigger(TriggerKind.Level, T0.AddSeconds(60), Level: 5), 5));
        Assert.True(t.OnTrigger(new SplitTrigger(TriggerKind.Area, T0.AddSeconds(100), "1_2_town", "The Forest Encampment"), 6));
        Assert.True(t.Current!.Finished);
        Assert.Equal(100, t.Current.TotalSeconds);
        Assert.Equal(TimeSpan.FromSeconds(100), t.Snapshot(T0.AddHours(1), Comparison.PersonalBest).Elapsed);
    }

    [Fact]
    public void Out_of_order_events_still_record_each_split()
    {
        // Кінець акту раніше за рівень 5: рівневий спліт не втрачається, а фіксується пізніше
        var defs = TestData.Parse(TestData.MiniGuide.Replace("{ id: s_end, label: End, on: { area: \"1_2_town\" } }",
            "{ id: s_mid, label: Mid, on: { area: \"1_2_town\" } }\n  - { id: s_end, label: End, on: { area: \"1_3_town\" } }")).Splits;
        var t = new SplitTracker(defs, []);
        t.Start(T0, "mini", "1.0", "Hero", NoOptions);
        Assert.True(t.OnTrigger(new SplitTrigger(TriggerKind.Area, T0.AddSeconds(50), "1_2_town"), 3));
        Assert.True(t.OnTrigger(new SplitTrigger(TriggerKind.Level, T0.AddSeconds(70), Level: 5), 5));
        var rows = t.Rows();
        Assert.Equal([null, 70.0, 50.0, null], rows.Select(r => r.Current));
        Assert.Equal("Level 5", t.Snapshot(T0.AddSeconds(80), Comparison.PersonalBest).LastLabel);
        Assert.Equal("Town", t.Snapshot(T0.AddSeconds(80), Comparison.PersonalBest).NextLabel);
    }

    [Fact]
    public void Paused_time_is_not_counted()
    {
        var t = new SplitTracker(Defs, []);
        t.Start(T0, "mini", "1.0", "Hero", NoOptions);
        t.AddPause(T0.AddSeconds(100), T0.AddSeconds(1100));        // гра закрита 1000 с (з логу)
        t.BeginPause(T0.AddSeconds(1000));                          // вікно зникло всередині того самого проміжку
        t.EndPause(T0.AddSeconds(1200));
        t.OnTrigger(new SplitTrigger(TriggerKind.Step, T0.AddSeconds(1300), "start"), 2);
        Assert.Equal(200, t.Current!.Splits[0].Seconds);             // 1300 − (100..1200 = 1100)
        Assert.Equal(TimeSpan.FromSeconds(300), t.Snapshot(T0.AddSeconds(1400), Comparison.PersonalBest).Elapsed);
        t.BeginPause(T0.AddSeconds(1400));
        var s = t.Snapshot(T0.AddSeconds(5000), Comparison.PersonalBest);
        Assert.True(s.Paused);
        Assert.Equal(TimeSpan.FromSeconds(300), s.Elapsed);
    }

    [Fact]
    public void Delta_tone_and_gold_against_pb()
    {
        var pb = Run(10, 60, 100);
        var slow = Run(20, 90, 150);
        slow.StartedAt = T0.AddDays(1);
        var t = new SplitTracker(Defs, [pb, slow]);
        t.Start(T0, "mini", "1.0", "Hero", NoOptions);
        t.ManualSplit(T0.AddSeconds(8), 1);

        var s = t.Snapshot(T0.AddSeconds(30), Comparison.PersonalBest);
        Assert.Equal("Town", s.LastLabel);
        Assert.Equal(TimeSpan.FromSeconds(-2), s.LastDelta);
        Assert.True(s.LastGold);
        Assert.Equal(TimerTone.Ahead, s.Tone);
        Assert.Equal("Level 5", s.NextLabel);
        Assert.Equal(TimeSpan.FromSeconds(60), s.NextCompare);

        Assert.Equal(TimerTone.Behind, t.Snapshot(T0.AddSeconds(61), Comparison.PersonalBest).Tone);
        Assert.Equal(TimeSpan.FromSeconds(-12), t.Snapshot(T0.AddSeconds(30), Comparison.LastRun).LastDelta);
    }

    [Fact]
    public void Sum_of_best_uses_best_segments()
    {
        var a = Run(10, 80, 100);  // сегменти 10, 70, 20
        var b = Run(20, 50, 90);   // сегменти 20, 30, 40
        var t = new SplitTracker(Defs, [a, b]);
        var rows = t.Rows();
        Assert.Equal([10.0, 40.0, 60.0], rows.Select(r => r.SumOfBest));
        Assert.Equal([20.0, 50.0, 90.0], rows.Select(r => r.Best));
    }

    [Fact]
    public void Undo_removes_last_split_and_unfinishes()
    {
        var t = new SplitTracker(Defs, []);
        t.Start(T0, "mini", "1.0", "Hero", NoOptions);
        t.OnTrigger(new SplitTrigger(TriggerKind.Step, T0.AddSeconds(10), "start"), 1);
        t.OnTrigger(new SplitTrigger(TriggerKind.Area, T0.AddSeconds(50), "1_2_town"), 3);
        Assert.True(t.Current!.Finished);
        Assert.True(t.Undo());
        Assert.Equal(["s_town"], t.Current.Splits.Select(s => s.Id));
        Assert.False(t.Current.Finished);
    }
}
