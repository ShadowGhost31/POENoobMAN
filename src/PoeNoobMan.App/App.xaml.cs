using System.IO;
using System.Windows;
using System.Windows.Threading;
using PoeNoobMan.App.Services;
using PoeNoobMan.App.Windows;
using PoeNoobMan.Core.Storage;

namespace PoeNoobMan.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Розробка: PoeNoobMan.exe --preview <папка> — PNG оверлею без показу вікон
        if (e.Args is ["--preview", var previewDir, ..])
        {
            using var preview = new AppController(Dispatcher);
            preview.RenderPreview(previewDir);
            Shutdown();
            return;
        }

        _singleInstance = new Mutex(true, "PoeNoobMan.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("PoE Noob Man уже запущено.", "PoE Noob Man", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandled;
        Directory.CreateDirectory(AppPaths.Root);

        _controller = new AppController(Dispatcher);
        _controller.Start();
        var settings = new SettingsWindow(_controller);
        MainWindow = settings;
        settings.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write(e.Exception.ToString());
        MessageBox.Show($"Помилка: {e.Exception.Message}\n\nДеталі в {AppPaths.AppLog}", "PoE Noob Man",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
