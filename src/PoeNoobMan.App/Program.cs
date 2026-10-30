using Velopack;

namespace PoeNoobMan.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Обробляє хуки встановлення/оновлення/видалення від Velopack і виходить, якщо це був такий запуск.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
