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
            try
            {
                var logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SimForge",
                    "Logs");
                Directory.CreateDirectory(logDirectory);
                File.WriteAllText(
                    Path.Combine(logDirectory, "simforge-crash.log"),
                    eventArgs.ExceptionObject.ToString());
            }
            catch
            {
                // Crash logging must never hide the original failure.
            }
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
            .WithInterFont()
            .LogToTrace();
}
