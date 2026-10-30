using System.Windows.Input;
using PoeNoobMan.App.Native;

namespace PoeNoobMan.App.Services;

/// <summary>Комбінація клавіш у вигляді "Ctrl+Shift+F10".</summary>
public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    private static readonly HashSet<Key> ModifierOnly =
    [
        Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift, Key.LeftAlt, Key.RightAlt, Key.LWin, Key.RWin, Key.System,
    ];

    public static bool IsModifierKey(Key key) => ModifierOnly.Contains(key);

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public uint NativeModifiers =>
        (Modifiers.HasFlag(ModifierKeys.Control) ? Win32.MOD_CONTROL : 0)
        | (Modifiers.HasFlag(ModifierKeys.Shift) ? Win32.MOD_SHIFT : 0)
        | (Modifiers.HasFlag(ModifierKeys.Alt) ? Win32.MOD_ALT : 0)
        | (Modifiers.HasFlag(ModifierKeys.Windows) ? Win32.MOD_WIN : 0);

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = ModifierKeys.None;
        Key? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModifierKeys.Control; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "win": mods |= ModifierKeys.Windows; break;
                default:
                    if (key is not null || !Enum.TryParse<Key>(part, true, out var k) || IsModifierKey(k)) return false;
                    key = k;
                    break;
            }
        }
        if (key is null) return false;
        hotkey = new Hotkey(mods, key.Value);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }
}
