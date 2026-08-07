#if !ANDROID
using Avalonia;
using System;

namespace AINote;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.CaptureDir = GetArgValue(args, "--capture-ui");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static string? GetArgValue(string[] args, string name)
    {
        foreach (var a in args)
        {
            if (a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return a[(name.Length + 1)..];
        }
        return null;
    }
}
#endif
