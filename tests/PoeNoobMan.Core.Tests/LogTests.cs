using PoeNoobMan.Core.Log;

namespace PoeNoobMan.Core.Tests;

public class LogTests
{
    [Fact]
    public void Parses_area_with_id_from_generating_line()
    {
        var p = new LogLineParser();
        Assert.Null(p.Parse("""2026/10/30 20:30:47 607745859 1186a8a3 [DEBUG Client 10580] Generating level 2 area "1_1_2" with seed 1957617735"""));
        var ev = Assert.IsType<AreaEntered>(p.Parse("2026/10/30 20:30:48 607746765 cffb065b [INFO Client 10580] : You have entered The Coast."));
        Assert.Equal("The Coast", ev.AreaName);
        Assert.Equal("1_1_2", ev.AreaId);
        Assert.Equal(2, ev.AreaLevel);
        Assert.Equal(1, ev.Act);
        Assert.Equal(new DateTime(2026, 10, 30, 20, 30, 48), ev.Time);
    }

    [Fact]
    public void Parses_level_up_and_death()
    {
        var p = new LogLineParser();
        var lvl = Assert.IsType<LevelUp>(p.Parse("2026/10/30 20:34:11 607950546 cffb065b [INFO Client 10580] : Hero_One (Ascendant) is now level 13"));
        Assert.Equal(("Hero_One", "Ascendant", 13), (lvl.Character, lvl.CharacterClass, lvl.Level));
        var slain = Assert.IsType<CharacterSlain>(p.Parse("2026/10/30 20:33:32 607911046 cffb065b [INFO Client 10580] : Hero_One has been slain."));
        Assert.Equal("Hero_One", slain.Character);
    }

    [Fact]
    public void Parses_passive_points_and_game_start()
    {
        var p = new LogLineParser();
        p.Parse("2026/08/24 22:56:00 1 a [INFO Client 1] : You have entered The Feeding Trough.");
        Assert.Equal(1, Assert.IsType<PassivePointsReceived>(p.Parse("2026/08/24 22:57:00 1 a [INFO Client 1] : You have received a Passive Skill Point.")).Count);
        Assert.Equal(2, Assert.IsType<PassivePointsReceived>(p.Parse("2026/08/24 23:08:19 1 a [INFO Client 1] : You have received 2 Passive Skill Points.")).Count);
        Assert.Equal(24, Assert.IsType<QuestPointsReported>(p.Parse("2026/08/24 23:08:19 1 a [INFO Client 1] : 24 Passive Skill Points from quests:")).Count);
        Assert.Null(p.Parse("2026/08/24 23:08:19 1 a [INFO Client 1] : You have received 2 Passive Respec Points."));
        var start = Assert.IsType<GameStarted>(p.Parse("2026/08/25 18:00:00 ***** LOG FILE OPENING *****"));
        Assert.Equal(new DateTime(2026, 8, 24, 23, 8, 19), start.PreviousLineTime);
    }

    [Fact]
    public void Ignores_chat_lines()
    {
        var p = new LogLineParser();
        Assert.Null(p.Parse("2026/10/30 20:34:11 607950546 cffb065b [INFO Client 10580] #Someone: You have entered The Coast."));
        Assert.Null(p.Parse("2026/10/30 20:34:11 607950546 cffb065b [INFO Client 10580] @From Someone: x (y) is now level 3"));
    }

    [Theory]
    [InlineData("1_1_town", 1)]
    [InlineData("2_8_7_1_", 8)]
    [InlineData("2_10_town", 10)]
    [InlineData("HideoutBaths", null)]
    [InlineData("1_Labyrinth_boss_1", null)]
    public void Act_from_area_id(string id, int? act) => Assert.Equal(act, AreaIds.ActOf(id));

    [Fact]
    public void Area_display_names()
    {
        Assert.Equal("The Tidal Island (A1)", AreaNames.Display("1_1_2a"));
        Assert.Equal("Karui Shores (A11)", AreaNames.Display("2_11_endgame_town"));
        Assert.Equal("Somewhere", AreaNames.Display("Somewhere"));
    }

    [Fact]
    public void Every_area_in_bundled_guides_has_a_name()
    {
        var repo = new Guides.GuideRepository(TestData.GuidesDir);
        repo.Reload();
        var conditions = repo.Entries.SelectMany(e => e.Guide!.Steps.SelectMany(s => new[] { s.When, s.Done })
            .Concat(e.Guide.Splits.Select(sp => sp.On)).Append(e.Guide.TimerStart));
        var unknown = conditions.Select(c => c?.Area).OfType<string>().Where(a => AreaNames.NameOf(a) is null).Distinct();
        Assert.Empty(unknown);
    }

    [Fact]
    public void Watcher_reads_only_new_complete_lines_and_handles_truncation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"poe-log-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "2026/10/30 20:00:00 1 a [INFO Client 1] : You have entered Old Zone.\n");
            using var w = new LogWatcher(path, null);
            Assert.Empty(w.Poll()); // перший запуск — з кінця файлу

            File.AppendAllText(path, "2026/10/30 20:00:01 1 a [INFO Client 1] : You have entered New Zone.\n2026/10/30 20:00:02 1 a [INFO Client 1] : You have ent");
            var ev = Assert.Single(w.Poll());
            Assert.Equal("New Zone", ((AreaEntered)ev).AreaName);

            File.AppendAllText(path, "ered Third Zone.\n");
            Assert.Equal("Third Zone", ((AreaEntered)Assert.Single(w.Poll())).AreaName);

            File.WriteAllText(path, "2026/10/30 21:00:00 1 a [INFO Client 1] : You have entered After Truncate.\n");
            Assert.Equal("After Truncate", ((AreaEntered)Assert.Single(w.Poll())).AreaName);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
