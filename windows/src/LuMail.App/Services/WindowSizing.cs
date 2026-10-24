using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace LuMail.App.Services;

/// <summary>
/// Fenstergröße in logischen Pixeln (DPI-bewusst), begrenzt auf den Arbeitsbereich des Bildschirms und zentriert –
/// so ragt auf 1366×768-Bildschirmen nichts (z. B. Dialog-Schaltflächen) über den Rand.
/// </summary>
public static class WindowSizing
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    public static void ResizeAndCenter(Window window, int logicalWidth, int logicalHeight)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var dpi = GetDpiForWindow(hwnd);
            var scale = dpi > 0 ? dpi / 96.0 : 1.0;
            var area = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            var width = Math.Min((int)(logicalWidth * scale), area.Width - 24);
            var height = Math.Min((int)(logicalHeight * scale), area.Height - 24);
            window.AppWindow.Resize(new SizeInt32 { Width = width, Height = height });
            window.AppWindow.Move(new PointInt32
            {
                X = area.X + Math.Max(0, (area.Width - width) / 2),
                Y = area.Y + Math.Max(0, (area.Height - height) / 2),
            });
        }
        catch (Exception)
        {
            window.AppWindow.Resize(new SizeInt32 { Width = logicalWidth, Height = logicalHeight });
        }
    }
}
