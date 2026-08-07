using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using AINote.ViewModels;
using AINote.Views;

namespace AINote;

public partial class App : Avalonia.Application
{
    public static string? CaptureDir;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow { DataContext = vm };

            if (!string.IsNullOrEmpty(CaptureDir))
            {
                Directory.CreateDirectory(CaptureDir);
                desktop.MainWindow.Opened += async (_, _) =>
                {
                    await Task.Delay(700);
                    Capture(desktop.MainWindow, Path.Combine(CaptureDir, "main.png"));
                    vm.SettingsOpen = true;
                    await Task.Delay(500);
                    Capture(desktop.MainWindow, Path.Combine(CaptureDir, "settings.png"));
                    Environment.Exit(0);
                };
            }
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var vm = new MainWindowViewModel();
            singleView.MainView = new MainView { DataContext = vm };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void Capture(Window window, string path)
    {
        var w = Math.Max(1, (int)window.ClientSize.Width);
        var h = Math.Max(1, (int)window.ClientSize.Height);
        using var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        rtb.Render(window);
#pragma warning disable CS0618
        rtb.Save(path);
#pragma warning restore CS0618
    }
}
