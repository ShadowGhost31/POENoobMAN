using System.Text;

namespace PoeNoobMan.Core.Log;

/// <summary>
/// Читає лише нові рядки Client.txt. Без збереженого зміщення починає з кінця файлу.
/// Якщо файл став коротшим за зміщення (гра його очистила), читає з початку.
/// </summary>
public sealed class LogWatcher : IDisposable
{
    private const int MaxChunk = 8 * 1024 * 1024;

    private readonly LogLineParser _parser = new();
    private readonly object _lock = new();
    private Timer? _timer;

    public LogWatcher(string path, long? offset)
    {
        Path = path;
        Offset = offset;
    }

    public string Path { get; }
    public long? Offset { get; private set; }

    /// <summary>Викликається з фонового потоку.</summary>
    public event Action<IReadOnlyList<LogEvent>, long>? EventsRead;
    public event Action<Exception>? Error;

    public void Start(TimeSpan interval)
    {
        _timer ??= new Timer(_ => Tick(), null, TimeSpan.Zero, interval);
    }

    private void Tick()
    {
        try
        {
            var before = Offset;
            var events = Poll();
            if (events.Count > 0 || before != Offset) EventsRead?.Invoke(events, Offset ?? 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error?.Invoke(ex);
        }
    }

    public IReadOnlyList<LogEvent> Poll()
    {
        lock (_lock)
        {
            if (!File.Exists(Path)) return [];
            using var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = fs.Length;
            if (Offset is null)
            {
                Offset = length;
                return [];
            }
            if (length < Offset) Offset = 0;
            if (length == Offset) return [];

            var toRead = (int)Math.Min(length - Offset.Value, MaxChunk);
            var buffer = new byte[toRead];
            fs.Seek(Offset.Value, SeekOrigin.Begin);
            var read = 0;
            while (read < toRead)
            {
                var n = fs.Read(buffer, read, toRead - read);
                if (n == 0) break;
                read += n;
            }

            var lastNewline = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
            if (lastNewline < 0)
            {
                // Рядок ще дописується. Якщо шматок повний, а переносу так і немає, пропускаємо його.
                if (read == MaxChunk) Offset += read;
                return [];
            }

            var consumed = lastNewline + 1;
            Offset += consumed;
            var text = Encoding.UTF8.GetString(buffer, 0, consumed);
            var events = new List<LogEvent>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                if (_parser.Parse(line) is { } ev) events.Add(ev);
            }
            return events;
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
