using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using FFPorter.Desktop.Controls;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;
using FFPorter.Desktop.Theme;

namespace FFPorter.Desktop;

public partial class SettingsWindow : Window
{
    private readonly Func<Task<bool>>? _check;
    private readonly CommunityPanel? _community;
    private readonly DispatcherTimer _retry = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly string _storageTip;
    private CommunityMap? _files;
    private bool _found, _probing, _closed, _retrying, _ready;
    private bool? _answers;
    private DateTime _typed;

    private static AppSettings Settings => AppSettings.Current;
    private static Edition Edition => Edition.Current;

    public SettingsWindow(Window? owner, Func<Task<bool>>? check = null, CommunityPanel? community = null)
    {
        InitializeComponent();
        ThemeManager.StyleTitleBar(this);
        if (owner != null && new WindowInteropHelper(owner).Handle != IntPtr.Zero)
            Owner = owner;
        else
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _check = check;
        _community = community;
        AsyncSwitch.Tag = "Runs steps side by side. Uses more memory.";
        AsyncSwitch.ToolTip = "Sound banks, shaders, streamed data and movies are converted side by side instead of one after another. The converted files are the same.";
        MultiConversionSwitch.Tag = $"Converts up to {MainWindow.JobsAtOnce()} items at once. Needs Parallel.";
        MultiConversionSwitch.ToolTip = "Another conversion only starts while there is enough free memory.";
        MultiCoreSwitch.Tag = "Uses every processor core inside each step.";
        MultiCoreSwitch.ToolTip = "Off keeps each step on one core: slower, but the PC stays free for other things. The converted files are the same.";
        GpuVideoSwitch.Tag = "Encodes map videos on the graphics card.";
        GpuVideoSwitch.ToolTip = "Uses the NVIDIA or AMD video encoder when there is one, and the processor otherwise.";
        DeleteOriginalsSwitch.Tag = "Deletes the PC files after a clean conversion.";
        DeleteOriginalsSwitch.ToolTip = "Zones, xpaks and sound banks are deleted for good, and the folder once nothing else is in it. "
            + $"Nothing is deleted if anything from the same folder failed. Your {Edition.GameFolderName} are never deleted.";
        DeleteCacheSwitch.Tag = "Clears saved work after each run. Slower next time.";
        DeleteCacheSwitch.ToolTip = "Compiled shaders, zone reads and converted sound are rebuilt next time. Converted files, reports and settings are kept.";
        SendSwitch.Tag = "Sends conversions and downloads when ready.";
        SendSwitch.ToolTip = "Converted maps and mods are sent when a run finishes, and stay on this PC. Community downloads are sent as soon as they are in, "
            + "and their copy on this PC is only deleted once the console has confirmed every file. "
            + "What is already on the console is only replaced after asking. "
            + "Maps go into usermaps and mods into mods, inside the folder chosen under Send to. "
            + "A file that is being sent is always finished first, even if you cancel or close the app.";
        ConsoleBox.ToolTip = "Your console's IP address on your network, as its FTP server shows it. Port 2121 is used unless you add another, like 192.168.1.50:21.";
        ConsoleBox.Text = Settings.ConsoleAddress ?? "";
        _storageTip = $"Where maps and mods go on the console: its own storage ({ConsoleFtp.Root}), a USB drive (/mnt/usb0-7) or extended storage (/mnt/ext0-7). "
            + "Only drives that can be written to are listed: a small test file is written to each and deleted again when Settings opens. "
            + $"The plugin and its own files always stay in {ConsoleFtp.Root}.";
        FilesAbout.ToolTip = "The plugin, menu scripts and shared zone the console needs for custom maps, from the latest release. "
            + "The plugin is always the newest one published, also when it came out after the rest; a newer plugin is fetched and sent on its own. "
            + $"They are downloaded into your output folder. Send to console puts them in {ConsoleFtp.Root}; with Auto-FTP on that happens by itself.";
        foreach (string? storage in ConsoleFtp.Drives.Prepend<string?>(null))
            StorageBox.Items.Add(new ComboBoxItem { Content = ConsoleFtp.StorageName(storage), Tag = storage, IsSelected = storage == Settings.ConsoleStorage });
        ListDrives([]);
        foreach (double? scale in Scales)
            ScaleBox.Items.Add(new ComboBoxItem { Content = scale is double fixedScale ? $"{fixedScale:P0}" : "Auto", Tag = scale, IsSelected = scale == Settings.InterfaceScale });
        if (ScaleBox.SelectedIndex < 0)
            ScaleBox.SelectedIndex = 0;
        ShowScale();
        ThemeManager.ScaleChanged += ShowScale;
        CheckButton.Visibility = check != null ? Visibility.Visible : Visibility.Collapsed;
        ShowBuild();
        ShowSettings();
        _ = ProbeAsync();
        _ = FindFilesAsync();
        _retry.Tick += async (_, _) =>
        {
            if (IsVisible)
                await RetryAsync();
        };
        _retry.Start();
        _ready = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _retry.Stop();
        ThemeManager.ScaleChanged -= ShowScale;
        if (_files != null)
            _files.PropertyChanged -= FilesChanged;
        base.OnClosed(e);
    }

    private static readonly double?[] Scales = [null, 0.8, 0.9, 1, 1.1, 1.25, 1.5];

    private void ShowScale() => ScaleText.Text = Settings.InterfaceScale == null
        ? $"Fits your screen. Now {ThemeManager.Scale:P0}."
        : "Stays this size on every screen.";

    private void ScaleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || ScaleBox.SelectedItem is not ComboBoxItem item)
            return;
        Settings.SetInterfaceScale(item.Tag as double?);
        ThemeManager.Rescale();
        ShowScale();
    }

    private async Task RetryAsync()
    {
        if (_probing || _retrying || _closed || _answers == true || DateTime.UtcNow - _typed < TimeSpan.FromSeconds(3)
            || !ConsoleFtp.TryAddress(Settings.ConsoleAddress, out string host, out int port))
        {
            return;
        }
        _retrying = true;
        try
        {
            if (await Task.Run(() => ConsoleFtp.AnswersAsync(host, port)) && !_probing && !_closed)
                await ProbeAsync();
        }
        finally
        {
            _retrying = false;
        }
    }

    private async Task ProbeAsync()
    {
        if (!ConsoleFtp.TryAddress(Settings.ConsoleAddress, out string host, out int port))
            return;
        _probing = true;
        ShowSettings();
        ShowConsoleResult("Checking the drives…", "Text.Secondary");
        try
        {
            IReadOnlyList<string> usable = await Task.Run(() => ConsoleFtp.UsableAsync(host, port, ConsoleFtp.Drives, true, CancellationToken.None));
            _answers = true;
            if (!_closed)
                ShowDrives(usable, "");
        }
        catch (IOException error)
        {
            _answers = false;
            if (!_closed)
                ShowConsoleResult(char.ToUpperInvariant(error.Message[0]) + error.Message[1..] + ".", "Status.Warning");
        }
        finally
        {
            _probing = false;
            if (!_closed)
                ShowSettings();
        }
    }

    private void ListDrives(IReadOnlyList<string> usable)
    {
        foreach (ComboBoxItem item in StorageBox.Items)
        {
            item.IsEnabled = item.Tag is not string drive || usable.Contains(drive);
            item.Visibility = item.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ShowDrives(IReadOnlyList<string> usable, string found)
    {
        ListDrives(usable);
        if (Settings.ConsoleStorage is { } chosen && !usable.Contains(chosen))
        {
            StorageBox.SelectedIndex = 0;
            ShowConsoleResult($"{ConsoleFtp.StorageName(chosen)} is not usable. Back on console storage.", "Status.Warning");
            return;
        }
        string drives = usable.Count > 0 ? $"Drives: {string.Join(", ", usable.Select(ConsoleFtp.StorageName))}." : "No usable drives.";
        ShowConsoleResult(found + drives, found.Length > 0 ? "Status.Success" : "Text.Secondary");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        string address = ConsoleBox.Text.Trim();
        if (address.Length > 0 && !ConsoleFtp.TryAddress(address, out _, out _))
        {
            ShowConsoleResult("Not an address. Use an IP like 192.168.1.50 or 192.168.1.50:21.", "Status.Error");
            ConsoleBox.Focus();
            ConsoleBox.SelectAll();
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    private void ShowBuild()
    {
        ShowState(AppUpdate.Summary);
        BuildText.Text = AppUpdate.Built is { } built ? $"Built {built.ToLocalTime():d MMM yyyy, HH:mm}" : "";
    }

    private void ShowState(string text)
    {
        StateText.Text = text;
        StateText.ToolTip = text.Length > 0 ? text : null;
        StateText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void CheckClick(object sender, RoutedEventArgs e)
    {
        if (_found)
        {
            Close();
            return;
        }
        if (_check == null)
            return;
        CheckButton.IsEnabled = false;
        ShowState("Checking…");
        try
        {
            _found = await _check();
        }
        finally
        {
            CheckButton.IsEnabled = true;
            CheckButton.Content = _found ? "Get the update" : "Check for updates";
            ShowBuild();
        }
    }

    private void ConsoleChanged(object sender, TextChangedEventArgs e)
    {
        string address = ConsoleBox.Text.Trim();
        ConsoleHint.Visibility = ConsoleBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ConsoleResult.Visibility = Visibility.Collapsed;
        _answers = null;
        if (_ready)
            _typed = DateTime.UtcNow;
        ListDrives([]);
        if (ConsoleFtp.TryAddress(address, out _, out _))
        {
            Settings.SetConsoleAddress(address);
        }
        else if (address.Length == 0)
        {
            Settings.SetConsoleAddress(null);
            Settings.SetSendToConsole(false);
        }
        _community?.ShowConsoleFiles();
        ShowSettings();
    }

    private async void ConsoleTestClick(object sender, RoutedEventArgs e)
    {
        if (_probing)
            return;
        if (!ConsoleFtp.TryAddress(ConsoleBox.Text, out string host, out int port))
        {
            ShowConsoleResult("Enter the console's IP first, like 192.168.1.50.", "Status.Error");
            return;
        }
        _probing = true;
        ShowSettings();
        ShowConsoleResult($"Connecting to {host}:{port}…", "Text.Secondary");
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            ConsoleLook look = await ConsoleFtp.TestAsync(host, port, Settings.ConsoleRoot, limit.Token);
            _answers = true;
            if (!_closed)
                ShowDrives(look.Drives, look.Found ? "Connected. BO3-Customs found. " : "Connected. No BO3-Customs there yet. ");
        }
        catch (OperationCanceledException)
        {
            _answers = false;
            ShowConsoleResult("The console did not answer in time.", "Status.Error");
        }
        catch (IOException error)
        {
            _answers = false;
            ShowConsoleResult(char.ToUpperInvariant(error.Message[0]) + error.Message[1..] + ".", "Status.Error");
        }
        finally
        {
            _probing = false;
            if (!_closed)
                ShowSettings();
        }
    }

    private void ShowConsoleResult(string text, string colour)
    {
        ConsoleResult.Text = text;
        ConsoleResult.ToolTip = text;
        ConsoleResult.SetResourceReference(TextBlock.ForegroundProperty, colour);
        ConsoleResult.Visibility = Visibility.Visible;
    }

    private void StorageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StorageBox.SelectedItem is not ComboBoxItem chosen)
            return;
        Settings.SetConsoleStorage(chosen.Tag as string);
        StorageText.Text = Settings.ConsoleRoot;
        ConsoleResult.Visibility = Visibility.Collapsed;
    }

    private async Task FindFilesAsync()
    {
        if (_community == null || !Edition.HasConsoleFiles)
            return;
        FilesRow.Visibility = Visibility.Visible;
        if (_community.ConsoleFiles is { } known)
            Watch(known);
        else
            ShowFilesNote("Checking the latest release…", "Text.Secondary", false);
        CommunityMap? files;
        try
        {
            files = await _community.FindConsoleFilesAsync();
        }
        catch (IOException error)
        {
            if (!_closed)
                ShowFilesNote(char.ToUpperInvariant(error.Message[0]) + error.Message[1..] + ".", "Status.Warning", true);
            return;
        }
        if (_closed)
            return;
        if (files != null)
            Watch(files);
        else if (_files == null)
            ShowFilesNote("No console files in the releases yet.", "Text.Secondary", false);
    }

    private void Watch(CommunityMap files)
    {
        if (!ReferenceEquals(_files, files))
        {
            if (_files != null)
                _files.PropertyChanged -= FilesChanged;
            _files = files;
            files.PropertyChanged += FilesChanged;
        }
        ShowFiles();
    }

    private void FilesChanged(object? sender, PropertyChangedEventArgs e) => ShowFiles();

    private void ShowFilesNote(string text, string colour, bool retry)
    {
        FilesText.Text = text;
        FilesText.ToolTip = text;
        FilesText.SetResourceReference(TextBlock.ForegroundProperty, colour);
        FilesStart.Content = "Retry";
        FilesStart.Visibility = Shown(retry);
        FilesPause.Visibility = FilesCancel.Visibility = FilesMore.Visibility = FilesProgress.Visibility = Visibility.Collapsed;
    }

    private void ShowFiles()
    {
        if (_files is not { } files)
            return;
        bool noted = !files.IsRunning && files.HasNote;
        string text = files.IsRunning ? Brief(files.StageText) : noted ? files.Note : $"Plugin, menu scripts and shared zone · {files.SizeText}";
        string full = files.IsRunning ? files.StageText : text;
        FilesVersion.Text = files.VersionText;
        FilesText.Text = text;
        FilesText.ToolTip = full.Length > 0 ? full : null;
        FilesText.SetResourceReference(TextBlock.ForegroundProperty, !noted ? "Text.Secondary" : files.NoteState switch
        {
            RunStates.Failed => "Status.Error",
            RunStates.Cancelled => "Status.Warning",
            RunStates.Done => "Status.Success",
            _ => "Text.Secondary",
        });
        FilesStart.Content = files.ActionText;
        FilesStart.Visibility = Shown(files.CanStart);
        FilesPause.Content = files.PauseGlyph;
        FilesPause.ToolTip = files.PauseText;
        FilesPause.Visibility = Shown(files.CanPause);
        FilesCancel.Visibility = Shown(files.CanCancel);
        FilesMore.Visibility = Shown(files.HasMenu);
        FilesOpenItem.Visibility = Shown(files.CanOpen);
        FilesResendItem.Visibility = Shown(files.CanResend);
        FilesDeleteItem.Visibility = Shown(files.CanDelete);
        FilesProgress.Value = files.Progress;
        FilesProgress.Visibility = Shown(files.IsRunning);
    }

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    private static string Brief(string stage)
    {
        const string sending = DownloadProgress.Sending + " · ";
        if (!stage.StartsWith(sending, StringComparison.Ordinal))
            return stage;
        string rest = stage[sending.Length..];
        int next = rest.IndexOf(" · ", StringComparison.Ordinal);
        return "Sending · " + (next < 0 ? rest : rest[(next + 3)..]);
    }

    private void FilesStartClick(object sender, RoutedEventArgs e)
    {
        if (_files is { } files)
            _community?.Start(files, this);
        else
            _ = FindFilesAsync();
    }

    private void FilesPauseClick(object sender, RoutedEventArgs e)
    {
        if (_files is { } files)
            _community?.Pause(files);
    }

    private void FilesCancelClick(object sender, RoutedEventArgs e)
    {
        if (_files is { } files)
            _community?.Cancel(files);
    }

    private void FilesMoreClick(object sender, RoutedEventArgs e)
    {
        if (FilesMore.ContextMenu is not { } menu)
            return;
        menu.PlacementTarget = FilesMore;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void FilesOpenClick(object sender, RoutedEventArgs e)
    {
        if (_files is { } files)
            _community?.Open(files);
    }

    private void FilesResendClick(object sender, RoutedEventArgs e)
    {
        if (_files is { } files)
            _community?.Resend(files);
    }

    private void FilesDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_files is { } files)
            _community?.Delete(files, this);
    }

    private void SendToggled(object sender, RoutedEventArgs e)
    {
        bool on = SendSwitch.IsChecked == true;
        if (on != Settings.SendToConsole)
            Settings.SetSendToConsole(on);
    }

    private void ShowSettings()
    {
        AsyncSwitch.IsChecked = Settings.Async;
        MultiConversionSwitch.IsChecked = Settings.MultiConversion;
        MultiConversionSwitch.IsEnabled = Settings.Async;
        GpuVideoSwitch.IsChecked = Settings.GpuVideo;
        MultiCoreSwitch.IsChecked = Settings.MultiCore;
        DeleteOriginalsSwitch.IsChecked = Settings.DeleteOriginals;
        DeleteCacheSwitch.IsChecked = Settings.DeleteCache;
        SendSwitch.IsEnabled = Settings.ConsoleAddress != null;
        StorageBox.IsEnabled = Settings.ConsoleAddress != null && !_probing && _answers == true;
        StorageBox.ToolTip = StorageBox.IsEnabled || Settings.ConsoleAddress == null || _probing ? _storageTip : "Locked until the console answers. Test tries again.";
        ConsoleTest.IsEnabled = !_probing;
        StorageText.Text = Settings.ConsoleRoot;
        SendSwitch.IsChecked = Settings.SendToConsole;
    }

    private void AsyncToggled(object sender, RoutedEventArgs e)
    {
        bool on = AsyncSwitch.IsChecked == true;
        if (on == Settings.Async)
            return;
        Settings.SetAsync(on);
        if (!on)
            Settings.SetMultiConversion(false);
        ShowSettings();
    }

    private void GpuVideoToggled(object sender, RoutedEventArgs e) => Settings.SetGpuVideo(GpuVideoSwitch.IsChecked == true);

    private void MultiCoreToggled(object sender, RoutedEventArgs e) => Settings.SetMultiCore(MultiCoreSwitch.IsChecked == true);

    private void MultiConversionToggled(object sender, RoutedEventArgs e)
    {
        bool on = MultiConversionSwitch.IsChecked == true;
        if (!Settings.Async || on == Settings.MultiConversion)
            return;
        Settings.SetMultiConversion(on);
    }

    private void DeleteOriginalsToggled(object sender, RoutedEventArgs e)
    {
        bool on = DeleteOriginalsSwitch.IsChecked == true;
        if (on == Settings.DeleteOriginals)
            return;
        if (on && !MessageDialog.Confirm(this, DialogKind.Warning, "Delete PC originals after converting?",
            "They are deleted for good after each clean conversion.", "Delete originals", danger: true))
        {
            DeleteOriginalsSwitch.IsChecked = false;
            return;
        }
        Settings.SetDeleteOriginals(on);
    }

    private void DeleteCacheToggled(object sender, RoutedEventArgs e) => Settings.SetDeleteCache(DeleteCacheSwitch.IsChecked == true);
}
