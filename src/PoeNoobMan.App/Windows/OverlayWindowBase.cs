using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using PoeNoobMan.App.Native;

namespace PoeNoobMan.App.Windows;

/// <summary>
/// Прозоре вікно поверх гри, яке ніколи не забирає фокус. Якщо <see cref="ClickThrough"/>,
/// миша проходить крізь нього в гру.
/// </summary>
public class OverlayWindowBase : Window
{
    public OverlayWindowBase()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;
    }

    protected bool ClickThrough { get; set; } = true;

    public IntPtr Handle { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Handle = new WindowInteropHelper(this).Handle;
        var ex = Win32.GetWindowLong(Handle, Win32.GWL_EXSTYLE) | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOPMOST;
        if (ClickThrough) ex |= Win32.WS_EX_TRANSPARENT | Win32.WS_EX_LAYERED;
        Win32.SetWindowLong(Handle, Win32.GWL_EXSTYLE, ex);
        HwndSource.FromHwnd(Handle)?.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Win32.WM_MOUSEACTIVATE) return IntPtr.Zero;
        handled = true;
        return new IntPtr(Win32.MA_NOACTIVATE);
    }

    /// <summary>Показує вікно без активації і ставить лівий верхній кут у точку (фізичні пікселі).</summary>
    public void ShowAt(int x, int y)
    {
        if (!IsVisible) Show();
        Win32.SetWindowPos(Handle, Win32.HWND_TOPMOST, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    public Win32.RECT PixelBounds()
    {
        Win32.GetWindowRect(Handle, out var r);
        return r;
    }

    protected double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleY;

    protected static SolidColorBrush Panel(double opacity) =>
        new(Color.FromArgb((byte)Math.Clamp(opacity * 255, 0, 255), 0x14, 0x14, 0x14));
}
