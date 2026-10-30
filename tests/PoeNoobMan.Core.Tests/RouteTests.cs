using PoeNoobMan.Core.Guides;
using PoeNoobMan.Core.Progress;

namespace PoeNoobMan.Core.Tests;

/// <summary>Повністю автоматичне відстеження на справжньому маршруті кампанії і гайді Arma Crema.</summary>
public class RouteTests
{
    private static ProgressEngine Arma(string mule = "yes")
    {
        var repo = new GuideRepository(TestData.GuidesDir);
        repo.Reload();
        var guide = repo.Find("arma-crema-scion", null)!.Guide!;
        return new ProgressEngine(GuideView.Create(guide, new Dictionary<string, string> { ["mule"] = mule }), new ProgressState());
    }

    private static string[] Current(ProgressEngine e) => e.GetSteps().Current.Select(s => s.Id).ToArray();

    [Fact]
    public void Mule_then_scion_start_is_tracked_without_manual_marks()
    {
        var e = Arma();
        Assert.Equal(["mule_create", "mule_tarkleigh_sc", "mule_nessa", "mule_stash"], Current(e));

        // Мул: пляж → місто (у лозі вхід у місто буває двічі поспіль)
        e.OnAreaEntered("1_1_1", "The Twilight Strand");
        Assert.Equal(["mule_create"], e.OnAreaEntered("1_1_town", "Lioneye's Watch"));
        Assert.Empty(e.OnAreaEntered("1_1_town", "Lioneye's Watch"));
        Assert.DoesNotContain("a1_strand", e.State.DoneSteps);

        // Скіон стартує на пляжі: підказки мула, які були в місті, закриваються самі
        var closed = e.OnAreaEntered("1_1_1", "The Twilight Strand");
        Assert.Equal(["mule_tarkleigh_sc", "mule_nessa", "mule_stash"], closed);
        Assert.Equal(["a1_strand"], Current(e));

        // Hillock убитий — Скіон у місті
        e.SetLevel(2);
        Assert.Contains("a1_strand", e.OnAreaEntered("1_1_town", "Lioneye's Watch"));
        Assert.Equal(["a1_town", "a1_l2"], Current(e));

        // Вийшов з міста на The Coast: підказка рівня 2 закрилась, крок міста закрився, видно крок The Coast
        var done = e.OnAreaEntered("1_1_2", "The Coast");
        Assert.Contains("a1_l2", done);
        Assert.Contains("a1_town", done);
        Assert.Equal(["a1_coast"], Current(e));
    }

    [Fact]
    public void Quest_reward_closes_step_and_future_act_steps_stay_hidden()
    {
        var e = Arma("no");
        foreach (var (id, name) in new[] { ("1_1_1", "The Twilight Strand"), ("1_1_town", "Lioneye's Watch"), ("1_1_2", "The Coast"),
                     ("1_1_3", "The Mud Flats"), ("1_1_4_1", "The Submerged Passage"), ("1_1_4_0", "The Flooded Depths") })
            e.OnAreaEntered(id, name);

        // Рівень 16 уже є, але крок Act 2 не показується в Act 1
        e.SetLevel(16);
        Assert.DoesNotContain("a2_l16", Current(e));
        Assert.Contains("q1_dweller", Current(e));

        Assert.Contains("q1_dweller", e.OnPassivePointsReceived(1));
        Assert.Equal(16, e.Points);    // 15 за рівень + 1 за квест
    }

    [Fact]
    public void Branching_act_follows_the_zone_player_is_in()
    {
        // Act 8: гравець іде на Quay раніше, ніж у Lunaris — показується крок Quay, а не Lunaris
        var e = Arma("no");
        e.OnAreaEntered("2_8_3", "The Grand Promenade");
        e.OnAreaEntered("2_8_8", "The Quay");
        Assert.Contains("a8_quay", Current(e));
        Assert.DoesNotContain("a8_promenade", Current(e));

        e.OnAreaEntered("2_8_6", "The Lunaris Concourse");
        Assert.Contains("a8_lunconc", Current(e));
        Assert.DoesNotContain("a8_quay", Current(e));
    }

    [Fact]
    public void Lab_and_ascendancy_close_from_passive_allocation()
    {
        var e = Arma("no");
        e.OnAreaEntered("1_3_15", "The Imperial Gardens");
        e.OnAreaEntered("1_3_18_1", "The Sceptre of God");      // крок садів закрився → з'являється лабіринт
        Assert.Contains("lab_normal", Current(e));
        e.OnPassiveAllocated("Ascendant Deadeye", true, ascendancy: true);
        Assert.DoesNotContain("lab_normal", e.State.DoneSteps);
        Assert.Contains("lab_normal", e.OnPassiveAllocated("Ascendant Assassin", true, ascendancy: true));
    }

    [Fact]
    public void Ascendancy_node_waits_for_lab_and_current_zone_is_restored_after_guide_change()
    {
        var repo = new GuideRepository(TestData.GuidesDir);
        repo.Reload();
        var state = new ProgressState { CurrentAreaId = "1_1_6", CurrentAreaName = "The Climb" };
        state.VisitedAreas.Add("1_1_6");
        var e = new ProgressEngine(GuideView.Create(repo.Find("rain-of-arrows-deadeye", null)!.Guide!, null), state);

        Assert.Empty(e.GetNodes().Now);
        Assert.Equal("gathering_winds", e.GetNodes().Next!.Id);
        e.OnPassiveAllocated("Something", true, ascendancy: true);
        e.OnPassiveAllocated("Gathering Winds", true, ascendancy: true);
        Assert.Contains("gathering_winds", e.State.TakenNodes);

        Assert.DoesNotContain("a1_climb", Current(e));
        e.RestoreCurrentArea();
        Assert.Contains("a1_climb", Current(e));
    }

    [Fact]
    public void Tree_node_is_taken_from_allocation_log()
    {
        var e = Arma("no");
        e.OnPassiveAllocated("Practical Application", true);
        e.OnPassiveAllocated("Heart and Soul", true);
        Assert.DoesNotContain("heart_and_soul", e.State.TakenNodes);
        e.OnPassiveAllocated("Fire Walker", true);
        Assert.Contains("heart_and_soul", e.State.TakenNodes);
    }
}
