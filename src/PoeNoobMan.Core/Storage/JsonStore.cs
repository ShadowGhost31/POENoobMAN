using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoeNoobMan.Core.Storage;

public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Читає файл; якщо його немає або він пошкоджений, повертає новий об'єкт (пошкоджений файл зберігається як .bak).</summary>
    public static T Load<T>(string path) where T : new()
    {
        if (!File.Exists(path)) return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch (JsonException)
        {
            File.Copy(path, path + ".bak", overwrite: true);
            return new T();
        }
    }

    public static T? LoadOrNull<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Атомарний запис: спершу в .tmp, потім заміна.</summary>
    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
