using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FFPorter.Desktop.Theme;

public static class ThemeManager
{
    public const double SmallestScale = 0.75, LargestScale = 1.75;

    private const double DesignWidth = 1920, DesignHeight = 1040, Ease = 0.85, Step = 0.05, Fill = 0.96, Floor = 0.9;

    private static readonly string Component = $"/{typeof(ThemeManager).Assembly.GetName().Name};component/Theme/";
    private static readonly Lazy<ImageSource?> Icon = new(LoadIcon);
    private static readonly ConditionalWeakTable<Window, Sizes> Bases = new();
    private static Window? _main;
    private static Func<double?> _chosen = () => null;
    private static IntPtr _monitor;
    private static bool _moving;

    private sealed record Sizes(double Width, double Height, double MinWidth, double MinHeight);

    public static double Scale { get; private set; } = 1;

    public static event Action? ScaleChanged;

    public static void Initialize(Application application)
    {
        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(Component + "Palette.Dark.xaml", UriKind.Relative) });
        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(Component + "Controls.xaml", UriKind.Relative) });
    }

    private static ImageSource? LoadIcon()
    {
        try
        {
            return BitmapFrame.Create(new Uri($"pack://application:,,,{Component}app.ico", UriKind.Absolute));
        }
        catch (Exception error) when (error is IOException or NotSupportedException or FileFormatException)
        {
            return null;
        }
    }

    public static double AutoScale(double workWidth, double workHeight)
    {
        double fit = Math.Min(workWidth / DesignWidth, workHeight / DesignHeight);
        double eased = 1 + (fit - 1) * Ease;
        return Math.Clamp(Math.Round(eased / Step) * Step, SmallestScale, LargestScale);
    }

    public static void FollowScreen(Window window, Func<double?> chosen)
    {
        _main = window;
        _chosen = chosen;
        Bases.AddOrUpdate(window, new Sizes(window.Width, window.Height, window.MinWidth, window.MinHeight));
        window.SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(window) is HwndSource source)
                source.AddHook(Hook);
            Refit(true);
        };
        window.DpiChanged += (_, _) => window.Dispatcher.BeginInvoke(() =>
        {
            if (!_moving)
                Refit(false);
        });
        window.LocationChanged += (_, _) =>
        {
            if (!_moving && Moved())
                Refit(false);
        };
    }

    public static void Rescale() => Refit(false);

    private static IntPtr Hook(IntPtr handle, int message, IntPtr wide, IntPtr data, ref bool handled)
    {
        if (message == EnterSizeMove)
            _moving = true;
        else if (message == ExitSizeMove)
        {
            _moving = false;
            if (Moved() || Math.Abs(Scale - Wanted()) > 0.0001)
                Refit(false);
        }
        else if (message == DisplayChange)
            _main?.Dispatcher.BeginInvoke(() => Refit(false));
        return IntPtr.Zero;
    }

    private static bool Moved()
    {
        IntPtr handle = _main == null ? IntPtr.Zero : new WindowInteropHelper(_main).Handle;
        return handle != IntPtr.Zero && MonitorFromWindow(handle, NearestMonitor) != _monitor;
    }

    private static bool Screen(out Area work, out double dpi)
    {
        work = default;
        dpi = 1;
        if (_main == null)
            return false;
        IntPtr handle = new WindowInteropHelper(_main).Handle;
        if (handle == IntPtr.Zero)
            return false;
        _monitor = MonitorFromWindow(handle, NearestMonitor);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(_monitor, ref info))
            return false;
        work = info.Work;
        dpi = VisualTreeHelper.GetDpi(_main).DpiScaleX;
        return work.Right > work.Left && work.Bottom > work.Top && dpi > 0;
    }

    private static double Wanted() => Screen(out Area work, out double dpi)
        ? _chosen() is double chosen ? Math.Clamp(chosen, SmallestScale, LargestScale) : AutoScale((work.Right - work.Left) / dpi, (work.Bottom - work.Top) / dpi)
        : Scale;

    private static void Refit(bool first)
    {
        if (_main == null || !Screen(out Area work, out double dpi))
            return;
        double previous = Scale;
        Scale = Wanted();
        if (first || Math.Abs(Scale - previous) > 0.0001)
        {
            var transform = new ScaleTransform(Scale, Scale);
            transform.Freeze();
            Application.Current.Resources["Ui.Scale"] = transform;
            foreach (Window open in Application.Current.Windows)
                Resize(open);
        }
        Place(_main, work, dpi, first, previous);
        if (Math.Abs(Scale - previous) > 0.0001)
            ScaleChanged?.Invoke();
    }

    private static void Place(Window window, Area work, double dpi, bool first, double previous)
    {
        if (!Bases.TryGetValue(window, out Sizes? sizes))
            return;
        double workWidth = (work.Right - work.Left) / dpi, workHeight = (work.Bottom - work.Top) / dpi;
        window.MinWidth = Math.Min(sizes.MinWidth * Scale, workWidth * Floor);
        window.MinHeight = Math.Min(sizes.MinHeight * Scale, workHeight * Floor);
        if (window.WindowState != WindowState.Normal || !first && Math.Abs(Scale - previous) <= 0.0001)
            return;
        IntPtr handle = new WindowInteropHelper(window).Handle;
        double width = sizes.Width * Scale, height = sizes.Height * Scale;
        int centreX = (work.Left + work.Right) / 2, centreY = (work.Top + work.Bottom) / 2;
        if (!first && GetWindowRect(handle, out Area now))
        {
            width = (now.Right - now.Left) / dpi / previous * Scale;
            height = (now.Bottom - now.Top) / dpi / previous * Scale;
            centreX = (now.Left + now.Right) / 2;
            centreY = (now.Top + now.Bottom) / 2;
        }
        int wide = (int)Math.Round(Math.Min(width, workWidth * Fill) * dpi), tall = (int)Math.Round(Math.Min(height, workHeight * Fill) * dpi);
        int left = Math.Clamp(centreX - wide / 2, work.Left, Math.Max(work.Left, work.Right - wide));
        int top = Math.Clamp(centreY - tall / 2, work.Top, Math.Max(work.Top, work.Bottom - tall));
        SetWindowPos(handle, IntPtr.Zero, left, top, wide, tall, NoZOrder | NoActivate);
    }

    private static void Resize(Window window)
    {
        if (!Bases.TryGetValue(window, out Sizes? sizes))
            return;
        TextOptions.SetTextFormattingMode(window, Math.Abs(Scale - 1) < 0.0001 ? TextFormattingMode.Display : TextFormattingMode.Ideal);
        if (ReferenceEquals(window, _main))
            return;
        window.MinWidth = sizes.MinWidth * Scale;
        window.MinHeight = sizes.MinHeight * Scale;
        if (!double.IsNaN(sizes.Width))
            window.Width = sizes.Width * Scale;
        if (!double.IsNaN(sizes.Height))
            window.Height = sizes.Height * Scale;
    }

    public static void StyleTitleBar(Window window)
    {
        window.Icon ??= Icon.Value;
        if (!Bases.TryGetValue(window, out _))
        {
            Bases.Add(window, new Sizes(window.Width, window.Height, window.MinWidth, window.MinHeight));
            Resize(window);
        }
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            window.SourceInitialized -= OnSourceInitialized;
            window.SourceInitialized += OnSourceInitialized;
            return;
        }
        int dark = 1;
        if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, UseImmersiveDarkModeBefore20H1, ref dark, sizeof(int));
        if (Application.Current.TryFindResource("Color.Caption") is Color caption)
        {
            int colour = caption.R | caption.G << 8 | caption.B << 16;
            DwmSetWindowAttribute(handle, CaptionColor, ref colour, sizeof(int));
            DwmSetWindowAttribute(handle, BorderColor, ref colour, sizeof(int));
        }
        if (Application.Current.TryFindResource("Color.CaptionText") is Color text)
        {
            int colour = text.R | text.G << 8 | text.B << 16;
            DwmSetWindowAttribute(handle, TextColor, ref colour, sizeof(int));
        }
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.SourceInitialized -= OnSourceInitialized;
            StyleTitleBar(window);
        }
    }

    private const int UseImmersiveDarkModeBefore20H1 = 19, UseImmersiveDarkMode = 20, BorderColor = 34, CaptionColor = 35, TextColor = 36;
    private const int EnterSizeMove = 0x0231, ExitSizeMove = 0x0232, DisplayChange = 0x007E;
    private const uint NearestMonitor = 2, NoZOrder = 0x0004, NoActivate = 0x0010;

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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Area area);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
