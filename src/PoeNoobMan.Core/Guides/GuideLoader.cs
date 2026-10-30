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

    public static GuideLoadResult LoadFile(string path)
    {
        try
        {
            var guide = Parse(File.ReadAllText(path));
            return new GuideLoadResult(path, guide, GuideValidator.Validate(guide));
        }
        catch (YamlException ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            return new GuideLoadResult(path, null, [$"рядок {ex.Start.Line}: {message}"]);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new GuideLoadResult(path, null, [ex.Message]);
        }
    }
}
