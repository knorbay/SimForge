using Avalonia;
using System;
using System.IO;

namespace SimForge;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "simforge-crash.log");
            File.WriteAllText(logPath, eventArgs.ExceptionObject.ToString());
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new AvaloniaNativePlatformOptions
            {
                RenderingMode = [AvaloniaNativeRenderingMode.Software]
            })
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
