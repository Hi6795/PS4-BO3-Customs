using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using FFPorter.Desktop;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Theme;

internal static class ScaleChecks
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private const double WindowWidth = 1380, WindowHeight = 880, SmallestWidth = 1080, SmallestHeight = 680, SettingsWidth = 1020;

    public static void Run()
    {
        Console.WriteLine("== scale");
        (double Width, double Height, double Scale)[] screens =
        [
            (1280, 680, 0.75), (1366, 728, 0.75), (1536, 824, 0.8), (1600, 860, 0.85), (1920, 1040, 1), (2560, 1400, 1.3), (3440, 1400, 1.3), (3840, 2120, 1.75),
        ];
        foreach ((double width, double height, double scale) in screens)
            Console.WriteLine($"   {width}x{height}: {ThemeManager.AutoScale(width, height):P0} (window {WindowWidth * scale:0}x{WindowHeight * scale:0})");
        Program.Check(screens.All(screen => Math.Abs(ThemeManager.AutoScale(screen.Width, screen.Height) - screen.Scale) < 0.001),
            "the interface size follows the screen: 75% on a 720p laptop, 100% at 1080p, 130% at 1440p, 175% on a 4K screen at 100%");
        Program.Check(screens.All(screen => SmallestWidth * screen.Scale <= screen.Width * 0.9 && SmallestHeight * screen.Scale <= screen.Height * 0.9),
            "at that size the window's smallest layout fits every one of those screens");

        AppSettings settings = AppSettings.Current;
        FieldInfo chosen = typeof(AppSettings).GetField("_interfaceScale", Private)!;
        object? before = chosen.GetValue(settings);
        try
        {
            chosen.SetValue(settings, 1.25);
            var window = new MainWindow();
            IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
            double dpi = VisualTreeHelper.GetDpi(window).DpiScaleX;
            (double width, double height) = Size(handle, dpi);
            (double workWidth, double workHeight) = Work(handle, dpi);
            Console.WriteLine($"   chosen 125%: scale {ThemeManager.Scale}, window {width:0}x{height:0} on a {workWidth:0}x{workHeight:0} work area");
            Program.Check(Math.Abs(ThemeManager.Scale - 1.25) < 0.001 && Application.Current.Resources["Ui.Scale"] is ScaleTransform { ScaleX: 1.25, ScaleY: 1.25 },
                "a chosen interface size scales the content of every window");
            Program.Check(Math.Abs(width - Math.Min(WindowWidth * 1.25, workWidth * 0.96)) <= 2 && Math.Abs(height - Math.Min(WindowHeight * 1.25, workHeight * 0.96)) <= 2
                && width <= workWidth && height <= workHeight, "the main window opens at that size and never larger than the screen");
            SettingsWindow dialog = Program.Settled(new SettingsWindow(null));
            Program.Check(Math.Abs(dialog.Width - SettingsWidth * 1.25) < 0.5, "a dialog opened afterwards has the same scale");

            chosen.SetValue(settings, 0.9);
            ThemeManager.Rescale();
            (double smaller, _) = Size(handle, dpi);
            Console.WriteLine($"   chosen 90%: scale {ThemeManager.Scale}, window {smaller:0} wide, settings {dialog.Width:0} wide");
            // A small work area can clamp the original window and enforce a
            // minimum width after rescaling; the native window honors both.
            double minimumWidth = Math.Min(SmallestWidth * 0.9, workWidth * 0.9);
            double expectedWidth = Math.Max(minimumWidth, Math.Min(width / 1.25 * 0.9, workWidth * 0.96));
            Program.Check(Math.Abs(ThemeManager.Scale - 0.9) < 0.001 && Math.Abs(dialog.Width - SettingsWidth * 0.9) < 0.5 && Math.Abs(smaller - expectedWidth) <= 3,
                "changing the size rescales the windows that are open");
        }
        finally
        {
            chosen.SetValue(settings, 1.0);
            ThemeManager.Rescale();
            chosen.SetValue(settings, before);
        }
        Program.Check(Math.Abs(ThemeManager.Scale - 1) < 0.001 && Application.Current.Resources["Ui.Scale"] is ScaleTransform { ScaleX: 1 }, "the size goes back to 100%");
    }

    private static (double Width, double Height) Size(IntPtr handle, double dpi)
    {
        GetWindowRect(handle, out Area area);
        return ((area.Right - area.Left) / dpi, (area.Bottom - area.Top) / dpi);
    }

    private static (double Width, double Height) Work(IntPtr handle, double dpi)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromWindow(handle, 2), ref info);
        return ((info.Work.Right - info.Work.Left) / dpi, (info.Work.Bottom - info.Work.Top) / dpi);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Area
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Area Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Area area);
}
