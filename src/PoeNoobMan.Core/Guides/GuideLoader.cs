using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PoeNoobMan.Core.Guides;

public sealed record GuideLoadResult(string Path, Guide? Guide, IReadOnlyList<string> Errors)
{
    public bool IsValid => Guide is not null && Errors.Count == 0;
}

public static class GuideLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static Guide Parse(string yaml) =>
        Deserializer.Deserialize<Guide?>(yaml) ?? throw new InvalidDataException("Порожній файл гайда");

    /// <param name="routeDirs">Де шукати маршрути, крім папки routes/ поруч із гайдом.</param>
    public static GuideLoadResult LoadFile(string path, params string[] routeDirs)
    {
        try
        {
            var guide = Parse(File.ReadAllText(path));
            var errors = new List<string>();
            if (guide.Route is { } routeName)
            {
                var dirs = new[] { Path.Combine(Path.GetDirectoryName(path)!, "routes") }.Concat(routeDirs);
                var routePath = dirs.Select(d => Path.Combine(d, routeName + ".yaml")).FirstOrDefault(File.Exists);
                if (routePath is null) return new GuideLoadResult(path, null, [$"не знайдено маршрут routes/{routeName}.yaml"]);
                guide = MergeRoute(Parse(File.ReadAllText(routePath)), guide, errors);
            }
            errors.AddRange(GuideValidator.Validate(guide));
            return new GuideLoadResult(path, guide, errors);
        }
        catch (YamlException ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            return new GuideLoadResult(path, null, [$"{Path.GetFileName(path)}, рядок {ex.Start.Line}: {message}"]);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new GuideLoadResult(path, null, [ex.Message]);
        }
    }

    /// <summary><c>after: "^"</c> — вставити крок на самий початок, перед маршрутом (наприклад, кроки мула).</summary>
    public const string StartAnchor = "^";

    /// <summary>Кроки маршруту по порядку; кожен крок гайда — одразу після свого <c>after</c> (у порядку файла гайда).</summary>
    public static Guide MergeRoute(Guide route, Guide guide, List<string> errors)
    {
        var routeIds = route.Steps.Select(s => s.Id).Append(StartAnchor).ToHashSet();
        var byAnchor = new Dictionary<string, List<GuideStep>>();
        foreach (var s in guide.Steps)
        {
            if (s.After is null || !routeIds.Contains(s.After))
            {
                errors.Add(s.After is null
                    ? $"крок {s.Id}: у гайді з маршрутом потрібне поле after"
                    : $"крок {s.Id}: after посилається на невідомий крок маршруту '{s.After}'");
                continue;
            }
            if (!byAnchor.TryGetValue(s.After, out var list)) byAnchor[s.After] = list = [];
            list.Add(s);
        }

        var steps = new List<GuideStep>();
        if (byAnchor.TryGetValue(StartAnchor, out var first)) steps.AddRange(first);
        foreach (var r in route.Steps)
        {
            steps.Add(r);
            if (byAnchor.TryGetValue(r.Id, out var extra)) steps.AddRange(extra);
        }

        guide.Steps = steps;
        guide.Options = [.. route.Options, .. guide.Options];
        if (guide.Splits.Count == 0) guide.Splits = route.Splits;
        guide.TimerStart ??= route.TimerStart;
        return guide;
    }
}
