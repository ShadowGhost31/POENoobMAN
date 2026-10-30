using PoeNoobMan.Core.Guides;

namespace PoeNoobMan.Core.Tests;

internal static class TestData
{
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PoeNoobMan.slnx"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Не знайдено корінь репозиторію");
        }
    }

    public static string GuidesDir => Path.Combine(RepoRoot, "guides");

    public static Guide Parse(string yaml) => GuideLoader.Parse(yaml);

    public const string MiniGuide = """
        id: mini
        version: "1.0"
        title: Mini
        options:
          - id: move
            label: Move
            default: a
            choices:
              - { id: a, label: A }
              - { id: b, label: B }
        steps:
          - id: mule
            act: 1
            text: mule enters town
            done: { area: "1_1_town" }
          - id: start
            act: 1
            text: scion enters town
            done: { area: "1_1_town" }
          - id: l2
            act: 1
            when: { level: 2 }
            text: level 2
          - id: only_b
            act: 1
            if: { move: b }
            text: only b
          - id: coast
            act: 1
            when: { area: "1_1_2" }
            points: 1
            text: coast quest
          - id: manual
            act: 1
            text: manual
          - id: a6
            act: 6
            when: { area: "2_6_2" }
            text: act 6 coast
        tree:
          - id: n1
            name: first
          - id: n2
            name: at 3 points
            when: { points: 3 }
        splits:
          - { id: s_town, label: Town, on: { step: start } }
          - { id: s_l5, label: Level 5, on: { level: 5 } }
          - { id: s_end, label: End, on: { area: "1_2_town" } }
        """;
}
