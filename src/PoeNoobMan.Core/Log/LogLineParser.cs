using System.Globalization;
using System.Text.RegularExpressions;

namespace PoeNoobMan.Core.Log;

/// <summary>
/// Розбирає рядки Client.txt англійського клієнта. Має стан: ID зони з рядка "Generating level"
/// запам'ятовується і додається до наступного рядка про вхід у зону.
/// </summary>
public sealed partial class LogLineParser
{
    private string? _pendingAreaId;
    private int? _pendingAreaLevel;
    private DateTime? _lastTime;

    [GeneratedRegex(@"\] : You have received (a|an|\d+) Passive Skill Points?\.\s*$")]
    private static partial Regex PassiveRegex();

    [GeneratedRegex(@"\] : (\d+) Passive Skill Points? from quests")]
    private static partial Regex QuestPointsRegex();

    [GeneratedRegex(@"\] Successfully (allocated|unallocated) passive skill id: ([^,]*), name: (.+?)\s*$")]
    private static partial Regex AllocatedRegex();

    [GeneratedRegex(@"\] : ([^ ]+) has joined the area\.\s*$")]
    private static partial Regex JoinedRegex();

    [GeneratedRegex(@"\[LOADING SCREEN\] \(.*\) Duration = ([\d.]+) seconds")]
    private static partial Regex LoadingRegex();

    [GeneratedRegex(@"^(\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2})")]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"Generating level (\d+) area ""([^""]+)""")]
    private static partial Regex GeneratingRegex();

    [GeneratedRegex(@"\] : You have entered (.+?)\.\s*$")]
    private static partial Regex EnteredRegex();

    [GeneratedRegex(@"\] : ([^ ]+) \(([^)]+)\) is now level (\d+)\s*$")]
    private static partial Regex LevelRegex();

    [GeneratedRegex(@"\] : ([^ ]+) has been slain\.\s*$")]
    private static partial Regex SlainRegex();

    public LogEvent? Parse(string line)
    {
        var ts = TimestampRegex().Match(line);
        if (!ts.Success) return null;
        var time = DateTime.ParseExact(ts.Groups[1].Value, "yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
        var previous = _lastTime;
        _lastTime = time;

        if (line.Contains("***** LOG FILE OPENING *****", StringComparison.Ordinal))
        {
            _pendingAreaId = null;
            _pendingAreaLevel = null;
            return new GameStarted(time, previous);
        }

        if (line.Contains("Requesting to complete all tutorials", StringComparison.Ordinal))
            return new CharacterCreated(time);
        if (line.Contains("[SCENE] Set Source [(unknown)]", StringComparison.Ordinal))
            return new CharacterSelect(time);

        var m = GeneratingRegex().Match(line);
        if (m.Success)
        {
            _pendingAreaLevel = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            _pendingAreaId = m.Groups[2].Value;
            return null;
        }

        m = EnteredRegex().Match(line);
        if (m.Success)
        {
            var ev = new AreaEntered(time, m.Groups[1].Value, _pendingAreaId, _pendingAreaLevel);
            _pendingAreaId = null;
            _pendingAreaLevel = null;
            return ev;
        }

        m = LevelRegex().Match(line);
        if (m.Success)
            return new LevelUp(time, m.Groups[1].Value, m.Groups[2].Value, int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));

        m = SlainRegex().Match(line);
        if (m.Success)
            return new CharacterSlain(time, m.Groups[1].Value);

        m = JoinedRegex().Match(line);
        if (m.Success)
            return new PlayerJoined(time, m.Groups[1].Value);

        m = PassiveRegex().Match(line);
        if (m.Success)
        {
            var count = int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 1;
            return new PassivePointsReceived(time, count);
        }

        m = QuestPointsRegex().Match(line);
        if (m.Success)
            return new QuestPointsReported(time, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));

        m = LoadingRegex().Match(line);
        if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var load))
            return new LoadingScreen(time, load);

        m = AllocatedRegex().Match(line);
        if (m.Success)
            return new PassiveAllocated(time, m.Groups[2].Value, m.Groups[3].Value, m.Groups[1].Value == "allocated");

        return null;
    }
}
