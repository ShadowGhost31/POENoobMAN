using PoeNoobMan.Core.Guides;
using PoeNoobMan.Core.Progress;

namespace PoeNoobMan.Core.Tests;

public class ProgressTests
{
    private static ProgressEngine Engine(string? move = null)
    {
        var guide = TestData.Parse(TestData.MiniGuide);
        var opts = move is null ? null : new Dictionary<string, string> { ["move"] = move };
        return new ProgressEngine(GuideView.Create(guide, opts), new ProgressState());
    }

    private static string[] CurrentIds(ProgressEngine e) => e.GetSteps().Current.Select(s => s.Id).ToArray();

    [Fact]
    public void Options_filter_steps()
    {
        Assert.DoesNotContain(Engine().View.Steps, s => s.Id == "only_b");
        Assert.Contains(Engine("b").View.Steps, s => s.Id == "only_b");
    }

    [Fact]
    public void Area_name_far_ahead_is_not_unlocked_but_unique_id_is()
    {
        var byName = TestData.Parse(TestData.MiniGuide.Replace("when: { area: \"2_6_2\" }", "when: { area: \"The Coast\" }"));
        var e = new ProgressEngine(GuideView.Create(byName, null), new ProgressState());
        e.OnAreaEntered(null, "The Coast");
        // назва повторюється в A1 і A6: невідомий акт — не відкриваємо крок A6 з A1
        Assert.DoesNotContain("a6", e.State.UnlockedSteps);

        var e2 = Engine();
        e2.OnAreaEntered("2_6_2", "The Coast");
        // ID унікальний — гравець випереджає гайд, крок відкривається
        Assert.Contains("a6", e2.State.UnlockedSteps);
    }

    [Fact]
    public void Repeated_entry_into_same_zone_is_ignored()
    {
        // Реальний випадок з логу: "You have entered Lioneye's Watch" двічі поспіль при вході в гру
        var e = Engine();
        Assert.Equal(["mule"], e.OnAreaEntered("1_1_town", "Lioneye's Watch"));
        Assert.Empty(e.OnAreaEntered("1_1_town", "Lioneye's Watch"));
        Assert.DoesNotContain("start", e.State.DoneSteps);
        e.OnAreaEntered("1_1_1", "The Twilight Strand");
        Assert.Equal(["start"], e.OnAreaEntered("1_1_town", "Lioneye's Watch"));
    }

    [Fact]
    public void Passive_point_from_log_closes_visible_quest_step_and_counts()
    {
        var e = Engine();
        e.SetStepDone("mule", true);
        e.SetStepDone("start", true);
        e.SetLevel(2);
        e.OnAreaEntered("1_1_2", "The Coast");
        Assert.Contains("coast", e.GetSteps().Current.Select(s => s.Id));
        Assert.Contains("coast", e.OnPassivePointsReceived(1));
        Assert.Equal(2, e.Points);   // рівень 2 → 1 + 1 квест

        // /passives дає точне число і має пріоритет
        e.OnQuestPointsReported(5);
        Assert.Equal(6, e.Points);
        e.OnPassivePointsReceived(2);
        Assert.Equal(8, e.Points);
    }

    [Fact]
    public void Points_from_marked_steps_are_kept_when_log_starts_later()
    {
        var e = Engine();
        e.SetStepDone("coast", true);          // відмічено вручну до запуску застосунку
        e.SetLevel(3);
        Assert.Equal(3, e.Points);
        e.OnPassivePointsReceived(1);          // наступний квест з логу
        Assert.Equal(4, e.Points);
    }

    [Fact]
    public void Points_are_level_plus_quests_plus_adjust()
    {
        var e = Engine();
        e.SetLevel(3);
        Assert.Equal(2, e.Points);
        e.SetStepDone("coast", true);
        Assert.Equal(3, e.Points);
        e.AdjustPoints(-1);
        Assert.Equal(2, e.Points);
    }

    [Fact]
    public void Mark_and_undo()
    {
        var e = Engine();
        Assert.Equal("mule", e.MarkFirstCurrent()[0]);
        Assert.Equal("mule", e.UndoLast());
        Assert.DoesNotContain("mule", e.State.DoneSteps);
        // знятий вручну крок з умовою done автоматика більше не відмічає
        e.OnAreaEntered("1_1_town", "Lioneye's Watch");
        Assert.DoesNotContain("mule", e.State.DoneSteps);
    }

    [Fact]
    public void Nodes_now_and_next()
    {
        var e = Engine();
        var n = e.GetNodes();
        Assert.Equal(["n1"], n.Now.Select(x => x.Id));
        Assert.Equal("n2", n.Next!.Id);
        Assert.Equal("n1", e.MarkFirstNode());
        e.SetLevel(4);
        Assert.Equal(["n2"], e.GetNodes().Now.Select(x => x.Id));
    }

    [Fact]
    public void Bundled_guides_are_valid()
    {
        var repo = new GuideRepository(TestData.GuidesDir);
        repo.Reload();
        Assert.Empty(repo.Broken.SelectMany(b => b.Errors.Select(err => $"{Path.GetFileName(b.Path)}: {err}")));
        foreach (var id in new[] { "arma-crema-scion", "rain-of-arrows-deadeye" })
        {
            var entry = repo.Find(id, null);
            Assert.NotNull(entry);
            // 23 очки за квести кампанії (An End to Hunger дає 2), без бандитів
            Assert.Equal(23, entry!.Guide!.Steps.Sum(s => s.Points));
        }
    }

    [Theory]
    [InlineData("rain-of-arrows-deadeye", "ascendancy", "focal_point", "far_shot")]
    [InlineData("rain-of-arrows-deadeye", "ascendancy", "occupying_force", "occupying_force")]
    public void Guide_options_filter_tree(string guideId, string option, string choice, string expectedNode)
    {
        var repo = new GuideRepository(TestData.GuidesDir);
        repo.Reload();
        var view = GuideView.Create(repo.Find(guideId, null)!.Guide!, new Dictionary<string, string> { [option] = choice });
        Assert.Contains(view.Nodes, n => n.Id == expectedNode);
    }

    [Fact]
    public void Validator_reports_bad_option_reference()
    {
        var g = TestData.Parse(TestData.MiniGuide.Replace("if: { move: b }", "if: { movment: b }"));
        Assert.Contains(GuideValidator.Validate(g), e => e.Contains("movment"));
    }

    [Fact]
    public void Version_comparer_is_numeric()
    {
        Assert.True(VersionComparer.Instance.Compare("3.29.10", "3.29.2") > 0);
    }
}
