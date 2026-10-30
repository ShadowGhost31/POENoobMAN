using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace PoeNoobMan.App.Services;

/// <summary>
/// Оновлення через Velopack. Джерело — посилання на GitHub-репозиторій (релізи),
/// HTTP-адреса або локальна/мережева папка з результатом `vpk pack`.
/// </summary>
public sealed class UpdateService
{
    private UpdateInfo? _pending;
    private string? _pendingSource;

    public static string CurrentVersion
    {
        get
        {
            try
            {
                var mgr = new UpdateManager("https://localhost.invalid");
                if (mgr.IsInstalled && mgr.CurrentVersion is { } v) return v.ToString();
            }
            catch (Exception)
            {
                // не встановлено через Setup — беремо версію збірки
            }
            return Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }

    public string? PendingVersion => _pending?.TargetFullRelease.Version.ToString();

    private static UpdateManager Create(string source) =>
        source.Contains("github.com", StringComparison.OrdinalIgnoreCase)
            ? new UpdateManager(new GithubSource(source, null, false))
            : new UpdateManager(source);

    /// <returns>Нова версія або null, якщо оновлень немає.</returns>
    public async Task<string?> CheckAsync(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new InvalidOperationException("Не задано джерело оновлень");
        var mgr = Create(source);
        if (!mgr.IsInstalled) throw new InvalidOperationException("Оновлення працюють лише у версії, встановленій через Setup");
        _pending = await mgr.CheckForUpdatesAsync();
        _pendingSource = source;
        return PendingVersion;
    }

    public async Task DownloadAndRestartAsync(Action<int>? progress)
    {
        if (_pending is null || _pendingSource is null) throw new InvalidOperationException("Спершу перевірте оновлення");
        var mgr = Create(_pendingSource);
        await mgr.DownloadUpdatesAsync(_pending, progress);
        mgr.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }
}
