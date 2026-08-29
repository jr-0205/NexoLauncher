using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NexoLauncher.App;

internal static class NativeWindowTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    public static void ApplyDarkTitleBar(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var enabled = 1;
            var handle = new WindowInteropHelper(window).Handle;
            _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int valueSize);
}