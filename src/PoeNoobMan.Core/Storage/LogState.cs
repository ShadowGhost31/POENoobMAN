namespace PoeNoobMan.Core.Storage;

/// <summary>До якого місця вже прочитано Client.txt.</summary>
public sealed class LogState
{
    public string Path { get; set; } = "";
    public long? Offset { get; set; }

    /// <summary>Імена персонажів, які зустрічались у рядках про рівень.</summary>
    public List<string> SeenCharacters { get; set; } = [];

    /// <summary>Персонаж, яким грали останнім (за логом); прогрес завантажується для нього.</summary>
    public string? ActiveCharacter { get; set; }
}
