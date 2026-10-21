using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace CommunityUploader;

public sealed class MapRow : INotifyPropertyChanged
{
    private bool _selected, _canSelect = true;
    private string _status = "", _tone = "";

    public string Name { get; init; } = "";
    public string Title { get; init; } = "";
    public string Kind { get; init; } = "Map";
    public string Folder { get; init; } = "";
    public long Bytes { get; init; }
    public bool Picture { get; init; }
    public bool Listed { get; set; }
    public Action? Changed { get; set; }
    public string SizeText => Bytes > 0 ? $"{Bytes / 1073741824.0:0.00} GB" : "";

    public string Tag
    {
        get
        {
            if (Kind == "Mod")
                return "MOD";
            string lead = Name.Length > 3 && Name[2] == '_' ? Name[..2].ToUpperInvariant() : "";
            return lead is "ZM" or "MP" or "CP" ? lead : "MAP";
        }
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            Raise();
            Changed?.Invoke();
        }
    }

    public bool CanSelect { get => _canSelect; set { _canSelect = value; Raise(); } }
    public string Status { get => _status; private set { _status = value; Raise(); } }
    public string Tone { get => _tone; private set { _tone = value; Raise(); } }

    public void Set(string status, string tone = "")
    {
        Status = status;
        Tone = tone;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class MainWindow : Window
{
    private const string DefaultFolder = @"E:\BO3-Customs";
    private const string AppName = "PS4 .FF Porter - Black Ops III Community Uploader";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Map Uploader");
    private static readonly Regex ValidName = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    private readonly ObservableCollection<MapRow> _rows = [], _published = [];
    private readonly string _settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS4 FF Porter Community Uploader", "settings.json");
    private readonly Stopwatch _clock = new();
    private CancellationTokenSource? _cancel;
    private MapRow? _current;
    private long _doneBytes, _totalBytes, _rateBytes;
    private double _speed, _rateAt;
    private bool _running, _stopAfter, _canStopAfter, _listRead;

    public bool Quiet { get; set; }
    public bool CheckOnly { get; set; }
    public string WorkFolder { get; set; } = "";
    public string StartFolder { get; set; } = "";
    public string? TestKeyId { get; set; }
    public string? TestKey { get; set; }
    public string LastProblem { get; private set; } = "";

    private sealed record Saved(string? Folder, string? KeyId, string? Key);

    public MainWindow()
    {
        InitializeComponent();
        Rows.ItemsSource = _rows;
        PublishedRows.ItemsSource = _published;
        if (Keys.KeyId.Length > 0 && Keys.Key.Length > 0)
            KeyRow.Visibility = Visibility.Collapsed;
    }

    private bool ShowingPublished => PublishedTab.IsChecked == true;

    private ObservableCollection<MapRow> Shown => ShowingPublished ? _published : _rows;

    private string Work => WorkFolder.Length > 0 ? WorkFolder : Path.Combine(Path.GetDirectoryName(_settings)!, "work");

    public IReadOnlyList<MapRow> LocalRows => _rows;

    public IReadOnlyList<MapRow> PublishedList => _published;

    public async Task StartAsync()
    {
        FolderBox.Text = DefaultFolder;
        if (!Quiet)
            Load();
        if (StartFolder.Length > 0)
            FolderBox.Text = StartFolder;
        await ScanAsync();
    }

    public void ShowPublished()
    {
        PublishedTab.IsChecked = true;
        ShowView();
    }

    private static string Gb(long bytes) => $"{bytes / 1073741824.0:0.0} GB";

    private static string Amount(long bytes) => bytes >= 1L << 30 ? $"{bytes / 1073741824.0:0.00} GB" : $"{bytes / 1048576.0:0} MB";

    private void Say(string text) => StatusText.Text = text;

    private void Log(string text)
    {
        LogBox.AppendText(text + "\n");
        LogBox.ScrollToEnd();
        try
        {
            Directory.CreateDirectory(Work);
            File.AppendAllText(Path.Combine(Work, "upload.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\n");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_settings) || JsonSerializer.Deserialize<Saved>(File.ReadAllText(_settings)) is not { } saved)
                return;
            if (!string.IsNullOrWhiteSpace(saved.Folder))
                FolderBox.Text = saved.Folder;
            if (saved.Key is { Length: > 0 } stored)
            {
                KeyBox.Password = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser));
                KeyIdBox.Text = saved.KeyId ?? "";
                RememberBox.IsChecked = true;
            }
        }
        catch (Exception error) when (error is IOException or JsonException or CryptographicException or FormatException or UnauthorizedAccessException)
        {
        }
    }

    private void Save()
    {
        if (Quiet)
            return;
        try
        {
            bool remember = RememberBox.IsChecked == true && KeyBox.Password.Length > 0;
            string? key = remember ? Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(KeyBox.Password), Entropy, DataProtectionScope.CurrentUser)) : null;
            Directory.CreateDirectory(Path.GetDirectoryName(_settings)!);
            File.WriteAllText(_settings, JsonSerializer.Serialize(new Saved(FolderBox.Text.Trim(), remember ? KeyIdBox.Text.Trim() : null, key)));
        }
        catch (Exception error) when (error is IOException or CryptographicException or UnauthorizedAccessException)
        {
        }
    }

    private static MapRow? Describe(string path, string? expected)
    {
        string name = Path.GetFileName(path);
        bool mod = Publisher.ModZones.Any(zone => File.Exists(Path.Combine(path, zone)));
        bool map = !mod && File.Exists(Path.Combine(path, name + ".ff"));
        string? kind = mod ? "Mod" : map ? "Map" : expected;
        if (kind == null)
            return null;
        long bytes = Publisher.Files(path).Sum(file => new FileInfo(file).Length);
        string title = "";
        try
        {
            string workshop = Path.Combine(path, "workshop.json");
            if (File.Exists(workshop))
            {
                byte[] data = File.ReadAllBytes(workshop);
                if (JsonNode.Parse(data.AsSpan(Publisher.HasMark(data) ? 3 : 0)) is JsonObject about)
                {
                    foreach (KeyValuePair<string, JsonNode?> pair in about)
                    {
                        if (pair.Key.Equals("Title", StringComparison.OrdinalIgnoreCase) && pair.Value is JsonValue value && value.TryGetValue(out string? text))
                            title = text ?? "";
                    }
                }
            }
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
        }
        var row = new MapRow { Name = name, Title = title, Kind = kind, Folder = path, Bytes = bytes, Picture = File.Exists(Path.Combine(path, "previewimage.png")) };
        if (!ValidName.IsMatch(name))
        {
            row.CanSelect = false;
            row.Set("Name not accepted", "bad");
        }
        else if (!mod && !map)
        {
            row.CanSelect = false;
            row.Set(kind == "Mod" ? "No mod zone in it" : $"No {name}.ff", "bad");
        }
        return row;
    }

    private static List<MapRow> Read(string folder)
    {
        var rows = new List<MapRow>();
        string own = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        string? expected = own.Equals("usermaps", StringComparison.OrdinalIgnoreCase) ? "Map" : own.Equals("mods", StringComparison.OrdinalIgnoreCase) ? "Mod" : null;
        foreach (string path in Directory.GetDirectories(folder))
        {
            string name = Path.GetFileName(path);
            string? inside = name.Equals("usermaps", StringComparison.OrdinalIgnoreCase) ? "Map" : name.Equals("mods", StringComparison.OrdinalIgnoreCase) ? "Mod" : null;
            if (inside == null)
            {
                if (Describe(path, expected) is { } row)
                    rows.Add(row);
                continue;
            }
            foreach (string child in Directory.GetDirectories(path))
            {
                if (Describe(child, inside) is { } row)
                    rows.Add(row);
            }
        }
        return [.. rows.OrderBy(row => row.Kind == "Mod" ? 1 : 0).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task ScanAsync()
    {
        string folder = FolderBox.Text.Trim();
        _rows.Clear();
        _published.Clear();
        ShowView();
        Say("Reading…");
        List<MapRow> found = Directory.Exists(folder) ? await Task.Run(() => Read(folder)) : [];
        (JsonObject? list, _, bool missing) = await Publisher.ReadListAsync(CancellationToken.None);
        _listRead = list != null || missing;
        List<Listed> listed = list != null ? Publisher.Entries(list) : [];
        var names = new HashSet<string>(listed.Select(entry => entry.Name), StringComparer.OrdinalIgnoreCase);
        foreach (MapRow row in found)
        {
            row.Changed = Summary;
            if (row.CanSelect)
            {
                row.Listed = names.Contains(row.Name);
                if (row.Listed)
                {
                    row.Set("In the list", "good");
                }
                else
                {
                    row.Set(row.Picture || row.Kind == "Mod" ? "Ready" : "Ready · no picture");
                    row.Selected = true;
                }
            }
            _rows.Add(row);
        }
        foreach (Listed entry in listed)
        {
            var row = new MapRow { Name = entry.Name, Title = entry.Title, Kind = entry.Kind, Bytes = entry.Size, Changed = Summary };
            row.Set("Published");
            _published.Add(row);
        }
        ShowView();
        int maps = found.Count(row => row.Kind == "Map"), mods = found.Count - maps;
        Say(!Directory.Exists(folder) ? "That folder does not exist."
            : !_listRead ? "The list could not be read, so nothing is marked as already there."
            : $"{maps} map{(maps == 1 ? "" : "s")} and {mods} mod{(mods == 1 ? "" : "s")} found. Tick what to upload.");
    }

    private void ShowView()
    {
        bool published = ShowingPublished;
        LocalBar.Visibility = Rows.Visibility = UploadButton.Visibility = published ? Visibility.Collapsed : Visibility.Visible;
        PublishedBar.Visibility = PublishedRows.Visibility = PublishedActions.Visibility = published ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = published ? (_listRead ? "Nothing is published yet." : "The list could not be read.") : "No maps or mods in this folder.";
        EmptyText.Visibility = Shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Summary();
    }

    private void ViewClick(object sender, RoutedEventArgs e) => ShowView();

    private void Summary()
    {
        MapRow[] usable = [.. Shown.Where(row => row.CanSelect)];
        MapRow[] chosen = [.. usable.Where(row => row.Selected)];
        long bytes = chosen.Sum(row => row.Bytes);
        SummaryText.Text = Shown.Count == 0 ? "" : $"{chosen.Length} of {usable.Length} selected" + (bytes > 0 ? $" · {Gb(bytes)}" : "");
        AllBox.IsChecked = usable.Length > 0 && chosen.Length == usable.Length;
        UploadButton.IsEnabled = UnlistButton.IsEnabled = DeleteButton.IsEnabled = !_running && chosen.Length > 0;
    }

    private void AllClick(object sender, RoutedEventArgs e)
    {
        bool all = AllBox.IsChecked == true;
        foreach (MapRow row in Shown.Where(row => row.CanSelect))
            row.Selected = all;
        Summary();
    }

    private async void BrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "A folder of maps or mods, or the one that holds usermaps and mods" };
        if (Directory.Exists(FolderBox.Text.Trim()))
            dialog.InitialDirectory = FolderBox.Text.Trim();
        if (dialog.ShowDialog(this) != true)
            return;
        FolderBox.Text = dialog.FolderName;
        await ScanAsync();
    }

    private async void RefreshClick(object sender, RoutedEventArgs e) => await ScanAsync();

    private bool Key(out string id, out string key)
    {
        id = TestKeyId ?? (Keys.KeyId.Length > 0 ? Keys.KeyId : KeyIdBox.Text.Trim());
        key = TestKey ?? (Keys.Key.Length > 0 ? Keys.Key : KeyBox.Password);
        if (CheckOnly || (id.Length > 0 && key.Length > 0))
            return true;
        string missing = id.Length == 0 && key.Length == 0 ? "the key ID and the key" : id.Length == 0 ? "the key ID" : "the key";
        Say($"Enter {missing} first.");
        if (!Quiet)
        {
            MessageBox.Show(this, $"Enter {missing} first.\n\nBoth boxes are just above the progress bar. They are your storage key ID and application key.", AppName,
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        if (id.Length == 0)
            KeyIdBox.Focus();
        else
            KeyBox.Focus();
        return false;
    }

    private static string Many(MapRow[] rows)
    {
        int maps = rows.Count(row => row.Kind == "Map"), mods = rows.Length - maps;
        var parts = new List<string>();
        if (maps > 0)
            parts.Add(maps == 1 ? "1 map" : $"{maps} maps");
        if (mods > 0)
            parts.Add(mods == 1 ? "1 mod" : $"{mods} mods");
        return string.Join(" and ", parts);
    }

    private async void UploadClick(object sender, RoutedEventArgs e)
    {
        MapRow[] chosen = [.. _rows.Where(row => row.Selected && row.CanSelect)];
        if (chosen.Length == 0 || _running || !Key(out string id, out string key))
            return;
        if (MessageBox.Show(this, $"Upload {Many(chosen)} ({Gb(chosen.Sum(row => row.Bytes))})?\n\nThey show up in everyone's Community tab right away.", AppName,
            MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }
        Save();
        await UploadAsync(chosen, id, key);
    }

    private async void UnlistClick(object sender, RoutedEventArgs e)
    {
        MapRow[] chosen = [.. _published.Where(row => row.Selected)];
        if (chosen.Length == 0 || _running || !Key(out string id, out string key))
            return;
        bool delete = ReferenceEquals(sender, DeleteButton);
        string question = delete
            ? $"Delete {Many(chosen)} from the server?\n\nThey come off the Community list and their files are deleted. This cannot be undone."
            : $"Take {Many(chosen)} off the Community list?\n\nThey disappear from everyone's Community tab. Their files stay on the server.";
        if (MessageBox.Show(this, question, AppName, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK)
            return;
        Save();
        await RemoveAsync(chosen, id, key, delete);
    }

    public async Task<bool> UploadOnlyAsync(string name)
    {
        foreach (MapRow row in _rows)
            row.Selected = row.CanSelect && row.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
        MapRow[] chosen = [.. _rows.Where(row => row.Selected)];
        if (chosen.Length == 0 || !Key(out string id, out string key))
            return false;
        return await UploadAsync(chosen, id, key);
    }

    public async Task<bool> RemoveOnlyAsync(string name, bool delete)
    {
        ShowPublished();
        foreach (MapRow row in _published)
            row.Selected = row.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
        MapRow[] chosen = [.. _published.Where(row => row.Selected)];
        if (chosen.Length == 0 || !Key(out string id, out string key))
            return false;
        return await RemoveAsync(chosen, id, key, delete, false);
    }

    private void Begin(MapRow[] chosen, bool stopAfter)
    {
        _cancel = new CancellationTokenSource();
        _running = true;
        _stopAfter = false;
        _canStopAfter = stopAfter;
        _doneBytes = 0;
        _totalBytes = chosen.Sum(row => row.Bytes);
        _current = null;
        LastProblem = "";
        foreach (MapRow row in chosen)
            row.Set("Waiting");
        Lock(true);
        Overall.Value = 0;
        LogBox.Clear();
        Say("Starting…");
    }

    private bool End(MapRow[] chosen, int done, string word, bool stopped, string problem)
    {
        _cancel?.Dispose();
        _cancel = null;
        _running = false;
        foreach (MapRow row in chosen.Where(row => row.Status == "Waiting"))
            row.Set("Not done", "warn");
        _current?.Set(stopped ? "Stopped" : "Failed", stopped ? "warn" : "bad");
        _current = null;
        Lock(false);
        bool all = !stopped && problem.Length == 0 && done == chosen.Length;
        LastProblem = problem;
        if (problem.Length > 0)
        {
            Log("Stopped: " + problem);
            Say(problem);
        }
        else if (stopped)
        {
            Say($"Stopped. {done} {word}.");
        }
        else if (!all)
        {
            Say($"Stopped after the one in progress. {done} {word}.");
        }
        else
        {
            Say($"Done. {done} {word}.");
            Overall.Value = 1;
        }
        Summary();
        return all;
    }

    private void Step(MapRow row, int number, int count, string text, double fraction, long sent)
    {
        if (!ReferenceEquals(_current, row))
            return;
        row.Set(text, "run");
        string amount = "";
        if (sent >= 0)
        {
            double now = _clock.Elapsed.TotalSeconds;
            if (sent < _rateBytes || _rateAt == 0)
            {
                _rateBytes = sent;
                _rateAt = now;
            }
            else if (now - _rateAt >= 1)
            {
                double rate = (sent - _rateBytes) / (now - _rateAt);
                _speed = _speed > 0 ? _speed * 0.6 + rate * 0.4 : rate;
                _rateBytes = sent;
                _rateAt = now;
            }
            amount = $" · {Amount(sent)} of {Amount(row.Bytes)}" + (_speed > 0 ? $" · {_speed / 1048576.0:0.0} MB/s" : "");
        }
        Say($"{number} of {count} · {row.Name} · {text}{amount}");
        Overall.Value = _totalBytes > 0 ? Math.Clamp((_doneBytes + row.Bytes * fraction) / _totalBytes, 0, 1) : 0;
    }

    private async Task<bool> UploadAsync(MapRow[] chosen, string id, string key)
    {
        Begin(chosen, true);
        _clock.Restart();
        CancellationToken token = _cancel!.Token;
        string word = CheckOnly ? "checked" : "uploaded", problem = "";
        bool stopped = false;
        int done = 0;
        Storage? storage = null;
        try
        {
            if (!CheckOnly)
            {
                Say("Signing in…");
                storage = await Storage.ConnectAsync(id, key, token);
                Say("Checking the server…");
                await storage.CheckAsync(token);
            }
            for (int index = 0; index < chosen.Length && !_stopAfter; index++)
            {
                MapRow row = chosen[index];
                int number = index + 1;
                _current = row;
                _speed = _rateAt = 0;
                _rateBytes = 0;
                Log($"{row.Name}: starting ({row.SizeText})");
                double last = -1;
                string shown = "";
                void Report(string text, double fraction, long sent)
                {
                    double now = _clock.Elapsed.TotalSeconds;
                    if (text == shown && now - last < 0.1 && fraction < 1)
                        return;
                    last = now;
                    shown = text;
                    Dispatcher.BeginInvoke(() => Step(row, number, chosen.Length, text, fraction, sent));
                }
                await Publisher.UploadAsync(storage, row.Name, row.Kind, row.Folder, Work, Report, token);
                _current = null;
                row.Set(CheckOnly ? "Checked" : "Uploaded", "good");
                row.Listed = !CheckOnly;
                row.Selected = false;
                _doneBytes += row.Bytes;
                done++;
                Overall.Value = _totalBytes > 0 ? Math.Clamp((double)_doneBytes / _totalBytes, 0, 1) : 0;
                Log($"{row.Name}: {word}");
            }
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }
        catch (Exception error) when (error is IOException or HttpRequestException or UnauthorizedAccessException)
        {
            problem = error.Message;
        }
        finally
        {
            storage?.Dispose();
        }
        return End(chosen, done, word, stopped, problem);
    }

    private async Task<bool> RemoveAsync(MapRow[] chosen, string id, string key, bool delete, bool rescan = true)
    {
        Begin(chosen, false);
        CancellationToken token = _cancel!.Token;
        string word = CheckOnly ? "checked" : delete ? "deleted" : "taken off the list", problem = "";
        bool stopped = false;
        int done = 0;
        Storage? storage = null;
        try
        {
            if (!CheckOnly)
            {
                Say("Signing in…");
                storage = await Storage.ConnectAsync(id, key, token);
            }
            await Publisher.RemoveAsync(storage, [.. chosen.Select(row => row.Name)], delete, Work,
                (index, name) =>
                {
                    _current = chosen[index];
                    _current.Set(delete ? "Deleting" : "Taking off the list", "run");
                    Say($"{index + 1} of {chosen.Length} · {name} · {(delete ? "Deleting files" : "Taking off the list")}");
                },
                index =>
                {
                    chosen[index].Set(CheckOnly ? "Checked" : delete ? "Deleted" : "Off the list", "good");
                    chosen[index].Selected = false;
                    _current = null;
                    done++;
                    Overall.Value = (double)done / chosen.Length;
                },
                Log, token);
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }
        catch (Exception error) when (error is IOException or HttpRequestException or UnauthorizedAccessException)
        {
            problem = error.Message;
        }
        finally
        {
            storage?.Dispose();
        }
        bool all = End(chosen, done, word, stopped, problem);
        if (all && !CheckOnly && rescan)
            await ScanAsync();
        return all;
    }

    private void Lock(bool running)
    {
        FolderBox.IsEnabled = BrowseButton.IsEnabled = RefreshButton.IsEnabled = ReloadButton.IsEnabled = !running;
        Rows.IsEnabled = PublishedRows.IsEnabled = AllBox.IsEnabled = LocalTab.IsEnabled = PublishedTab.IsEnabled = !running;
        KeyIdBox.IsEnabled = KeyBox.IsEnabled = RememberBox.IsEnabled = !running;
        StopButton.IsEnabled = running;
        StopButton.Content = "Stop";
        Summary();
    }

    private void StopClick(object sender, RoutedEventArgs e)
    {
        if (!_running)
            return;
        if (!_stopAfter && _canStopAfter)
        {
            _stopAfter = true;
            StopButton.Content = "Stop now";
            Say("Stops after this one. Press Stop now to cut it off.");
            return;
        }
        if (MessageBox.Show(this, "Stop right now?\n\nWhat is in progress is left unfinished. Parts already sent stay on the server, unlisted.", AppName,
            MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
        {
            _cancel?.Cancel();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr handle = new WindowInteropHelper(this).Handle;
        int dark = 1;
        if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
        if (TryFindResource("Color.Caption") is Color caption)
        {
            int colour = caption.R | caption.G << 8 | caption.B << 16;
            DwmSetWindowAttribute(handle, 35, ref colour, sizeof(int));
            DwmSetWindowAttribute(handle, 34, ref colour, sizeof(int));
        }
        if (TryFindResource("Color.CaptionText") is Color text)
        {
            int colour = text.R | text.G << 8 | text.B << 16;
            DwmSetWindowAttribute(handle, 36, ref colour, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_running && !Quiet)
        {
            if (MessageBox.Show(this, "Work is in progress. Close and stop it?", AppName, MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                e.Cancel = true;
                return;
            }
            _cancel?.Cancel();
        }
        Save();
        base.OnClosing(e);
    }

    public void Render(string png)
    {
        var content = (FrameworkElement)Content;
        Content = null;
        var host = new Border { Background = (Brush)FindResource("Bg.App"), Child = content, Width = Width, Height = Height };
        host.Resources = Resources;
        host.Measure(new Size(Width, Height));
        host.Arrange(new Rect(0, 0, Width, Height));
        host.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(png)!);
        using FileStream stream = File.Create(png);
        encoder.Save(stream);
    }
}
