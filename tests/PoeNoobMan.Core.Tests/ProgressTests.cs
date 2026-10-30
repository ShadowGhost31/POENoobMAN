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
    public void Mule_town_visit_does_not_complete_scion_step()
    {
        var e = Engine();
        Assert.Equal(["mule", "start"], CurrentIds(e));
        Assert.Equal("l2", e.GetSteps().Blocker!.Id);

        Assert.Equal(["mule"], e.OnAreaEntered("1_1_town", "Lioneye's Watch"));
        // крок мула закрився, а такий самий крок Скіона лишився, хоча мул досі в місті
        Assert.DoesNotContain("start", e.State.DoneSteps);
        e.SetLevel(1);
        Assert.DoesNotContain("start", e.State.DoneSteps);

        Assert.Equal(["start"], e.OnAreaEntered("1_1_town", "Lioneye's Watch"));
    }

    [Fact]
    public void Steps_stop_at_first_unmet_condition_and_unlock_in_order()
    {
        var e = Engine();
        e.SetStepDone("mule", true);
        e.SetStepDone("start", true);
        Assert.Empty(CurrentIds(e));
        Assert.Equal("l2", e.GetSteps().Blocker!.Id);

        // вхід у зону з кроку попереду запам'ятовується, але список стоїть на рівні
        e.OnAreaEntered("1_1_2", "The Coast");
        Assert.Empty(CurrentIds(e));

        e.SetLevel(2);
        Assert.Equal(["l2", "coast", "manual"], CurrentIds(e));
        Assert.Equal("a6", e.GetSteps().Blocker!.Id);
    }

    [Fact]
    public void Area_far_ahead_is_not_unlocked()
    {
        var e = Engine();
        e.OnAreaEntered("2_6_2", "The Coast");
        Assert.DoesNotContain("a6", e.State.UnlockedSteps);
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
        var arma = repo.Find("arma-crema-scion", null);
        Assert.NotNull(arma);
        Assert.Equal(22, arma!.Guide!.Steps.Sum(s => s.Points));
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
