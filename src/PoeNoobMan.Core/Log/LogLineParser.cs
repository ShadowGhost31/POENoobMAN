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

        return null;
    }
}
