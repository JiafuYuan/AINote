using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace AINote.Services;

/// <summary>
/// Windows：把系统标题栏背景/文字颜色与应用浅色主题统一。
/// </summary>
public static class LightTitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35; // Windows 11 22H2+
    private const int DWMWA_TEXT_COLOR = 36;    // Windows 11 22H2+

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (window.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero) return;

        int light = 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref light, sizeof(int));

        // COLORREF 顺序为 0x00BBGGRR
        int caption = 0x00FFFFFF;
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));

        int text = 0x00333130; // #303133
        DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
    }
}
