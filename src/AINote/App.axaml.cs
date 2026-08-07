using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
            Services.LightTitleBar.Apply(desktop.MainWindow);
#if !ANDROID
            SetupTrayIcon(desktop, vm);
#endif

            if (!string.IsNullOrEmpty(CaptureDir))
            {
                Directory.CreateDirectory(CaptureDir);
                desktop.MainWindow.Opened += async (_, _) =>
                {
                    await Task.Delay(700);
                    Capture(desktop.MainWindow, Path.Combine(CaptureDir, "main.png"));
                    vm.AddNoteOpen = true;
                    await Task.Delay(400);
                    Capture(desktop.MainWindow, Path.Combine(CaptureDir, "add-note.png"));
                    vm.AddNoteOpen = false;
                    vm.SettingsOpen = true;
                    await Task.Delay(500);
                    Capture(desktop.MainWindow, Path.Combine(CaptureDir, "settings.png"));
                    vm.SettingsOpen = false;
                    vm.ShowToast("已保存笔记");
                    await Task.Delay(250);
                    Capture(desktop.MainWindow, Path.Combine(CaptureDir, "toast.png"));
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

#if !ANDROID
    private static void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop, MainWindowViewModel vm)
    {
        if (!OperatingSystem.IsWindows()) return;
        var window = desktop.MainWindow;
        if (window is null) return;

        var showItem = new NativeMenuItem("打开主窗口");
        showItem.Click += (_, _) => ShowMainWindow(window);

        var addItem = new NativeMenuItem("新建笔记");
        addItem.Click += (_, _) =>
        {
            ShowMainWindow(window);
            vm.OpenAddNoteCommand.Execute(null);
        };

        var hideItem = new NativeMenuItem("隐藏到托盘");
        hideItem.Click += (_, _) => window.Hide();

        var exitItem = new NativeMenuItem("退出");
        exitItem.Click += (_, _) => desktop.Shutdown();

        var menu = new NativeMenu();
        menu.Items.Add(showItem);
        menu.Items.Add(addItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(hideItem);
        menu.Items.Add(exitItem);

        using var stream = AssetLoader.Open(new Uri("avares://AINote/Assets/app.png"));
        var trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(new Bitmap(stream)),
            ToolTipText = "AI 记事本",
            Menu = menu,
            IsVisible = true
        };
        trayIcon.Clicked += (_, _) => ShowMainWindow(window);

        TrayIcon.SetIcons(Application.Current!, new TrayIcons { trayIcon });
    }

    private static void ShowMainWindow(Window window)
    {
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
    }
#endif

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
