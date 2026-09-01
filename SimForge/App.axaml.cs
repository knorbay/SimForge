using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;

namespace SimForge;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;

            if (Array.Exists(desktop.Args ?? [], argument => argument == "--smoke-test"))
            {
                mainWindow.Opened += (_, _) =>
                {
                    Console.WriteLine("SimForge smoke test: main window opened.");
                    Console.Out.Flush();
                    Dispatcher.UIThread.Post(() => desktop.Shutdown(), DispatcherPriority.Background);
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
