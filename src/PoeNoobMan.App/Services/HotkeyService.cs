using System.Windows.Interop;
using PoeNoobMan.App.Native;

namespace PoeNoobMan.App.Services;

/// <summary>
/// Глобальні хоткеї через RegisterHotKey. Якщо клавішу вже зайняла інша програма, реєстрація не вдається —
/// так ми й дізнаємося про конфлікт.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;

    public HotkeyService()
    {
        _source = new HwndSource(new HwndSourceParameters("PoeNoobManHotkeys") { ParentWindow = Win32.HWND_MESSAGE, Width = 0, Height = 0 });
        _source.AddHook(WndProc);
    }

    public bool TryRegister(Hotkey hotkey, Action handler)
    {
        var id = _nextId++;
        if (!Win32.RegisterHotKey(_source.Handle, id, hotkey.NativeModifiers | Win32.MOD_NOREPEAT, hotkey.VirtualKey)) return false;
        _handlers[id] = handler;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys) Win32.UnregisterHotKey(_source.Handle, id);
        _handlers.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            handled = true;
            handler();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.Dispose();
    }
}
