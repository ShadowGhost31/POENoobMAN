using System.IO;
using PoeNoobMan.Core.Storage;

namespace PoeNoobMan.App.Services;

public static class AppLog
{
    private static readonly object Lock = new();

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(AppPaths.Root);
                File.AppendAllText(AppPaths.AppLog, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // журнал не має ламати застосунок
        }
    }
}
