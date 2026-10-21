using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;

namespace FFPorter.Desktop.Controls;

public partial class ConsolePanel : UserControl
{
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(2);

    private readonly ObservableCollection<ConsoleItem> _items = [];
    private readonly Dictionary<string, ConsoleDrive> _drives = new(StringComparer.Ordinal);
    private readonly ICollectionView _view;
    private CancellationTokenSource _session = new();
    private DateTime _scanned;
    private string _address = "", _kind = "", _status = "";
    private bool _scanning, _stale = true, _listed;
    private int _removing, _expected, _measured;

    private static AppSettings Settings => AppSettings.Current;

    public ConsolePanel()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ConsoleItem.Drive)));
        _view.Filter = Matches;
        ItemList.ItemsSource = _view;
        foreach (RadioButton filter in (RadioButton[])[AllFilter, ZombiesFilter, MultiplayerFilter, ModsFilter])
            filter.Checked += FilterChanged;
        ShowEmpty("On your console", "The list loads when this tab opens.");
    }

    public Action<string>? Log { get; set; }

    public Action? Idle { get; set; }

    public Action? StatusChanged { get; set; }

    public Action<string>? Removed { get; set; }

    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value)
                return;
            _status = value;
            StatusChanged?.Invoke();
        }
    }

    public bool IsRemoving => _removing > 0;

    public void Activate() => Read(false);

    public void Changed()
    {
        _stale = true;
        if (Visibility == Visibility.Visible)
            Read(false);
    }

    public void CancelAll() => _session.Cancel();

    private void Read(bool again)
    {
        if (!ConsoleFtp.TryAddress(Settings.ConsoleAddress, out string host, out int port))
        {
            _session.Cancel();
            _session = new CancellationTokenSource();
            _scanning = false;
            RefreshButton.IsEnabled = true;
            _items.Clear();
            _address = "";
            _listed = false;
            _stale = true;
            ShowCounts();
            ShowEmpty("No console set", "Add your console's IP in Settings.");
            Status = "";
            return;
        }
        string address = $"{host}:{port}";
        bool other = address != _address;
        if (other)
        {
            _items.Clear();
            _listed = false;
            ShowCounts();
        }
        else if (_scanning)
        {
            return;
        }
        if (other || again || _stale || DateTime.UtcNow - _scanned > Fresh)
            _ = ScanAsync(host, port);
        else
            ShowList();
    }

    private async Task ScanAsync(string host, int port)
    {
        _session.Cancel();
        CancellationTokenSource session = _session = new CancellationTokenSource();
        _address = $"{host}:{port}";
        _scanning = true;
        RefreshButton.IsEnabled = false;
        if (_items.Count == 0)
            ShowEmpty("Reading the console", "");
        Status = ConsoleFtp.Busy ? "Waiting for the console to be free" : "Reading the console";
        var found = new Progress<IReadOnlyList<ConsoleEntry>>(entries =>
        {
            if (ReferenceEquals(session, _session))
                Found(entries);
        });
        var measured = new Progress<ConsoleSize>(size =>
        {
            if (ReferenceEquals(session, _session))
                Measured(size);
        });
        try
        {
            await Task.Run(() => ConsoleFtp.InstalledAsync(host, port, found, measured, session.Token));
            if (!ReferenceEquals(session, _session))
                return;
            _scanned = DateTime.UtcNow;
            _stale = false;
            ShowStatus();
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException error)
        {
            if (!ReferenceEquals(session, _session))
                return;
            Write($"The console could not be read: {error.Message}");
            if (_items.Count == 0)
                ShowEmpty("Could not read the console", Sentence(error.Message) + ".");
            Status = "The console could not be read";
        }
        finally
        {
            if (ReferenceEquals(session, _session))
            {
                _scanning = false;
                RefreshButton.IsEnabled = true;
            }
        }
    }

    private void Found(IReadOnlyList<ConsoleEntry> entries)
    {
        var folders = new HashSet<string>(entries.Select(entry => entry.Folder), StringComparer.Ordinal);
        foreach (ConsoleItem gone in _items.Where(item => !folders.Contains(item.Folder)).ToList())
            _items.Remove(gone);
        foreach (ConsoleEntry entry in entries)
        {
            if (_items.Any(item => item.Folder == entry.Folder))
                continue;
            var item = new ConsoleItem { Name = entry.Name, Folder = entry.Folder, IsMod = entry.IsMod, Drive = Drive(entry.Storage) };
            int index = 0;
            while (index < _items.Count && Compare(_items[index], item) <= 0)
                index++;
            _items.Insert(index, item);
        }
        _listed = true;
        _expected = entries.Count;
        _measured = 0;
        ShowList();
        ShowTotals();
        if (entries.Count > 0)
            Status = $"Reading sizes · 0 of {entries.Count}";
    }

    private void Measured(ConsoleSize size)
    {
        _measured++;
        _items.FirstOrDefault(item => item.Folder == size.Entry.Folder)?.Measure(size);
        ShowTotals();
        Status = $"Reading sizes · {_measured} of {_expected}";
    }

    private ConsoleDrive Drive(string? storage)
    {
        if (!_drives.TryGetValue(storage ?? "", out ConsoleDrive? drive))
            _drives[storage ?? ""] = drive = new ConsoleDrive(storage);
        return drive;
    }

    private static int Compare(ConsoleItem first, ConsoleItem second) =>
        first.Drive.Order != second.Drive.Order ? first.Drive.Order.CompareTo(second.Drive.Order)
        : first.IsMod != second.IsMod ? first.IsMod.CompareTo(second.IsMod)
        : string.Compare(first.Name, second.Name, StringComparison.OrdinalIgnoreCase);

    private void RefreshClick(object sender, RoutedEventArgs e) => Read(true);

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _view.Refresh();
        if (_listed)
            ShowList();
    }

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        _kind = (sender as FrameworkElement)?.Tag as string ?? "";
        _view.Refresh();
        if (_listed)
            ShowList();
    }

    private bool Matches(object entry) => entry is ConsoleItem item && (_kind.Length == 0 || item.Kind == _kind) && Sought(item);

    private bool Sought(ConsoleItem item)
    {
        string query = SearchBox.Text.Trim();
        return query.Length == 0 || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Folder.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private async void RemoveClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ConsoleItem { CanRemove: true } item || !ConsoleFtp.TryAddress(Settings.ConsoleAddress, out string host, out int port))
            return;
        if (!MessageDialog.Confirm(Window.GetWindow(this), DialogKind.Warning, $"Remove {item.Name} from the console?",
            $"{item.Folder}\nIt is deleted from the console for good.", "Remove", danger: true))
        {
            return;
        }
        await RemoveAsync(item, host, port);
    }

    private async Task RemoveAsync(ConsoleItem item, string host, int port)
    {
        item.Problem = "";
        item.IsRemoving = true;
        _removing++;
        ShowStatus();
        Write($"\n[{DateTime.Now:T}] Removing {item.Name} from the console at {host}:{port}: {item.Folder}");
        try
        {
            await Task.Run(() => ConsoleFtp.RemoveAsync(host, port, item.Folder, CancellationToken.None));
            _items.Remove(item);
            Write($"{item.Name} was removed from the console");
            Removed?.Invoke(item.Folder);
        }
        catch (IOException error)
        {
            item.Problem = $"Not removed: {error.Message}";
            Write($"{item.Name} was not removed from the console: {error.Message}");
        }
        finally
        {
            item.IsRemoving = false;
            _removing--;
            ShowList();
            ShowTotals();
            if (!_scanning)
                ShowStatus();
            if (_removing == 0)
                Idle?.Invoke();
        }
    }

    private void ShowStatus()
    {
        if (_removing > 0)
        {
            Status = _removing == 1 ? "Removing from the console" : $"Removing {_removing} from the console";
            return;
        }
        int mods = _items.Count(item => item.IsMod), maps = _items.Count - mods;
        Status = _items.Count == 0 ? (_listed ? "Nothing on the console yet" : "")
            : $"{Counted(maps, mods)} on the console · {Job.SizeText(_items.Sum(item => item.Size))}";
    }

    private void ShowTotals()
    {
        foreach (IGrouping<ConsoleDrive, ConsoleItem> drive in _items.GroupBy(item => item.Drive))
        {
            int mods = drive.Count(item => item.IsMod), maps = drive.Count() - mods;
            string size = Job.SizeText(drive.Sum(item => item.Size));
            drive.Key.Summary = $"{Counted(maps, mods)} · {(drive.All(item => item.IsMeasured) ? size : drive.Any(item => item.IsMeasured) ? size + "…" : "…")}";
        }
    }

    private static string Counted(int maps, int mods)
    {
        var parts = new List<string>();
        if (maps > 0 || mods == 0)
            parts.Add($"{maps} map{(maps == 1 ? "" : "s")}");
        if (mods > 0)
            parts.Add($"{mods} mod{(mods == 1 ? "" : "s")}");
        return string.Join(" and ", parts);
    }

    private void ShowList()
    {
        ShowCounts();
        if (_view.Cast<object>().Any())
        {
            Empty.Visibility = Visibility.Collapsed;
            ListScroll.Visibility = Visibility.Visible;
        }
        else if (_items.Count == 0)
        {
            ShowEmpty("Nothing installed yet", "Send a map from Convert or Community.");
        }
        else if (SearchBox.Text.Trim().Length > 0)
        {
            ShowEmpty("Nothing matches your search", "");
        }
        else
        {
            ShowEmpty(_kind switch
            {
                CommunityKinds.Zombies => "No Zombies maps on the console",
                CommunityKinds.Multiplayer => "No Multiplayer maps on the console",
                CommunityKinds.Mod => "No mods on the console",
                _ => "Nothing installed yet",
            }, "");
        }
    }

    private void ShowCounts()
    {
        ConsoleItem[] found = [.. _items.Where(Sought)];
        string Count(string kind) => _listed ? "  " + found.Count(item => kind.Length == 0 || item.Kind == kind) : "";
        AllCount.Text = Count("");
        ZombiesCount.Text = Count(CommunityKinds.Zombies);
        MultiplayerCount.Text = Count(CommunityKinds.Multiplayer);
        ModsCount.Text = Count(CommunityKinds.Mod);
    }

    private void ShowEmpty(string title, string text)
    {
        EmptyTitle.Text = title;
        EmptyText.Text = text;
        EmptyText.ToolTip = text.Length > 0 ? text : null;
        EmptyText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Empty.Visibility = Visibility.Visible;
        ListScroll.Visibility = Visibility.Collapsed;
    }

    private static string Sentence(string text) => text.Length > 0 ? char.ToUpperInvariant(text[0]) + text[1..] : text;

    private void Write(string line) => Log?.Invoke(line);
}
