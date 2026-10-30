using System.IO;
using System.Diagnostics;
using System.Windows.Threading;
using PoeNoobMan.App.Native;

namespace PoeNoobMan.App.Services;

/// <param name="Client">Клієнтська область гри у фізичних пікселях екрана.</param>
/// <param name="Foreground">Активне вікно — гра або одне з вікон цього застосунку.</param>
public sealed record GameWindowInfo(bool Found, IntPtr Hwnd, Win32.RECT Client, bool Minimized, bool Foreground)
{
    public static readonly GameWindowInfo NotFound = new(false, IntPtr.Zero, default, false, false);
}

/// <summary>Шукає вікно PoE за класом POEWindowClass і стежить за його позицією та фокусом.</summary>
public sealed class GameWindowTracker
{
    private const string WindowClass = "POEWindowClass";
    private static readonly uint OwnPid = (uint)Environment.ProcessId;

    private readonly DispatcherTimer _timer;

    public GameWindowTracker(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal, (_, _) => Poll(), dispatcher);
    }

    public GameWindowInfo Current { get; private set; } = GameWindowInfo.NotFound;

    public event Action<GameWindowInfo>? Changed;

    public void Start()
    {
        Poll();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    /// <summary>Шлях до PathOfExile*.exe, якщо гра запущена.</summary>
    public string? GameExePath() => Current.Found ? Win32.ProcessPathOfWindow(Current.Hwnd) : null;

    private void Poll()
    {
        var info = Read();
        if (info == Current) return;
        Current = info;
        Changed?.Invoke(info);
    }

    private static GameWindowInfo Read()
    {
        var hwnd = Win32.FindWindow(WindowClass, null);
        if (hwnd == IntPtr.Zero) return GameWindowInfo.NotFound;

        var minimized = Win32.IsIconic(hwnd);
        Win32.GetClientRect(hwnd, out var rc);
        var origin = new Win32.POINT();
        Win32.ClientToScreen(hwnd, ref origin);
        var client = new Win32.RECT { Left = origin.X, Top = origin.Y, Right = origin.X + rc.Width, Bottom = origin.Y + rc.Height };

        var fg = Win32.GetForegroundWindow();
        var foreground = fg == hwnd;
        if (!foreground && fg != IntPtr.Zero)
        {
            Win32.GetWindowThreadProcessId(fg, out var pid);
            foreground = pid == OwnPid;
        }

        return new GameWindowInfo(true, hwnd, client, minimized || rc.Width == 0, foreground);
    }

    /// <summary>Стандартні місця Client.txt, якщо гра не запущена.</summary>
    public static IEnumerable<string> KnownLogLocations()
    {
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        ];
        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)))
        {
            yield return Path.Combine(root, "Grinding Gear Games", "Path of Exile", "logs", "Client.txt");
            yield return Path.Combine(root, "Steam", "steamapps", "common", "Path of Exile", "logs", "Client.txt");
        }
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            yield return Path.Combine(drive.Name, "SteamLibrary", "steamapps", "common", "Path of Exile", "logs", "Client.txt");
            yield return Path.Combine(drive.Name, "Path of Exile", "logs", "Client.txt");
        }
    }

    public string? FindClientLog()
    {
        if (GameExePath() is { } exe)
        {
            var path = Path.Combine(Path.GetDirectoryName(exe)!, "logs", "Client.txt");
            if (File.Exists(path)) return path;
        }
        return KnownLogLocations().Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    public static void OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
