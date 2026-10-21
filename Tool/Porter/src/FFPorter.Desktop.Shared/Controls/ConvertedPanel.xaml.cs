using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FFPorter.Core.Common.Fidelity;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;

namespace FFPorter.Desktop.Controls;

public partial class ConvertedPanel : UserControl
{
    private sealed record Found(string Name, string Folder, bool IsMod, long Size, int Files, DateTime When, string ScoreText, string ScoreGrade, IReadOnlyList<string> Scores);

    private const int Depth = 3;

    private readonly ObservableCollection<ConvertedItem> _items = [];
    private readonly ICollectionView _view;
    private string _kind = "", _status = "";
    private bool _scanning, _again, _listed, _locked;
    private int _deleting;

    private static AppSettings Settings => AppSettings.Current;

    public ConvertedPanel()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = Matches;
        ItemList.ItemsSource = _view;
        foreach (RadioButton filter in (RadioButton[])[AllFilter, ZombiesFilter, MultiplayerFilter, ModsFilter])
            filter.Checked += FilterChanged;
        ShowEmpty("Converted on this PC", "The list loads when this tab opens.");
    }

    public Action<string>? Log { get; set; }

    public Action? Idle { get; set; }

    public Action? StatusChanged { get; set; }

    public Action<ConvertedItem>? Send { get; set; }

    public Action<string>? Deleted { get; set; }

    public Func<string, bool>? Downloaded { get; set; }

    public int Count => _items.Count;

    public bool IsDeleting => _deleting > 0;

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

    public bool Locked
    {
        get => _locked;
        set
        {
            _locked = value;
            foreach (ConvertedItem item in _items)
                item.Locked = value;
        }
    }

    public void Activate() => Refresh();

    public void Refresh()
    {
        if (_scanning)
            _again = true;
        else
            _ = ScanAsync();
    }

    public void ConsoleChanged()
    {
        bool console = ConsoleFtp.TryAddress(Settings.ConsoleAddress, out _, out _);
        foreach (ConvertedItem item in _items)
        {
            item.HasConsole = console;
            item.Sent = false;
        }
    }

    private async Task ScanAsync()
    {
        _scanning = true;
        RefreshButton.IsEnabled = false;
        if (!_listed)
            Status = "Looking through the output folder";
        try
        {
            do
            {
                _again = false;
                string root = Settings.Output, reports = FFPorter.Core.Workspace.ReportDirectory;
                Edition edition = Edition.Current;
                Show(await Task.Run(() => Find(root, reports, edition)));
            }
            while (_again);
        }
        finally
        {
            _scanning = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private static IEnumerable<string> Folders(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetDirectories(folder) : [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string[] Zones(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.ff") : [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static List<Found> Find(string root, string reports, Edition edition)
    {
        var found = new List<Found>();
        Walk(root, Depth);
        return found;

        void Walk(string folder, int depth)
        {
            foreach (string child in Folders(folder))
            {
                string name = Path.GetFileName(child);
                if (name.EndsWith(".incoming", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".old", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!Add(child, name) && depth > 1)
                    Walk(child, depth - 1);
            }
        }

        bool Add(string folder, string name)
        {
            string[] own = [.. Zones(folder), .. Zones(Path.Combine(folder, "zone"))];
            if (own.Length == 0)
                return false;
            bool mod = own.Any(ConvertedItem.IsModZone);
            string main = mod ? own.First(ConvertedItem.IsModZone)
                : own.FirstOrDefault(zone => Path.GetFileNameWithoutExtension(zone).Equals(name, StringComparison.OrdinalIgnoreCase)) ?? own[0];
            if (!edition.IsConverted(main))
                return true;
            try
            {
                FileInfo[] files = [.. new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories)];
                var stems = new HashSet<string>(own.Select(zone => Path.GetFileNameWithoutExtension(zone)), StringComparer.OrdinalIgnoreCase);
                var read = new List<(string, FidelitySnapshot)>();
                foreach (string stem in stems.Where(stem => !(stem.Length > 3 && stem[2] == '_' && stems.Contains(stem[3..]))))
                {
                    string report = (mod ? $"{name}.{stem}" : stem) + ".fidelity.json";
                    if ((FidelityProtocol.Load(Path.Combine(reports, report)) ?? FidelityProtocol.Load(Path.Combine(folder, report))) is { } snapshot)
                        read.Add((stem, snapshot));
                }
                (string text, string grade, IReadOnlyList<string> lines) = ConvertedItem.Score(read);
                found.Add(new Found(name, folder, mod, files.Sum(file => file.Length), files.Length, own.Max(File.GetLastWriteTime), text, grade, lines));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
            return true;
        }
    }

    private void Show(List<Found> found)
    {
        bool console = ConsoleFtp.TryAddress(Settings.ConsoleAddress, out _, out _);
        var known = _items.ToDictionary(item => item.Folder, StringComparer.OrdinalIgnoreCase);
        var next = new List<ConvertedItem>();
        foreach (Found entry in found.OrderBy(entry => entry.IsMod).ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
        {
            bool downloaded = Downloaded?.Invoke(entry.Folder) == true;
            known.TryGetValue(entry.Folder, out ConvertedItem? old);
            if (old != null && (old.IsSending || old.IsDeleting || (old.When == entry.When && old.Size == entry.Size && old.Files == entry.Files
                && old.ScoreText == entry.ScoreText && old.Downloaded == downloaded)))
            {
                old.HasConsole = console;
                next.Add(old);
                continue;
            }
            next.Add(new ConvertedItem
            {
                Name = entry.Name, Folder = entry.Folder, Place = Path.GetRelativePath(Settings.Output, entry.Folder), IsMod = entry.IsMod,
                Remote = Edition.Current.ConsoleFolder(entry.Name, entry.IsMod), Size = entry.Size, Files = entry.Files,
                When = entry.When, Downloaded = downloaded, ScoreText = entry.ScoreText, ScoreGrade = entry.ScoreGrade, Scores = entry.Scores, HasConsole = console, Locked = _locked,
            });
        }
        if (!next.SequenceEqual(_items))
        {
            _items.Clear();
            foreach (ConvertedItem item in next)
                _items.Add(item);
        }
        _listed = true;
        ShowList();
        ShowStatus();
    }

    private void RefreshClick(object sender, RoutedEventArgs e) => Refresh();

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

    private bool Matches(object entry) => entry is ConvertedItem item && (_kind.Length == 0 || item.Kind == _kind) && Sought(item);

    private bool Sought(ConvertedItem item)
    {
        string query = SearchBox.Text.Trim();
        return query.Length == 0 || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Folder.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static ConvertedItem? Row(object sender) => (sender as FrameworkElement)?.DataContext as ConvertedItem;

    private void SendClick(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is { CanSend: true } item)
            Send?.Invoke(item);
    }

    private void OpenClick(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is not { } item)
            return;
        if (Directory.Exists(item.Folder))
            Process.Start(new ProcessStartInfo(item.Folder) { UseShellExecute = true });
        else
            Refresh();
    }

    private async void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is not { CanDelete: true } item)
            return;
        if (!MessageDialog.Confirm(Window.GetWindow(this), DialogKind.Warning, $"Delete {item.Name} from this PC?",
            $"{item.SizeText} is deleted for good." + (item.Sent ? " The copy on the console stays." : ""), "Delete", danger: true))
        {
            return;
        }
        await DeleteAsync(item);
    }

    private async Task DeleteAsync(ConvertedItem item)
    {
        item.Problem = "";
        item.IsDeleting = true;
        _deleting++;
        ShowStatus();
        try
        {
            Removal removed = await Task.Run(() => Cleanup.ClearFolders([item.Folder]));
            if (removed.Problems.Count == 0)
            {
                _items.Remove(item);
                Write($"{item.Name}: deleted from this PC ({Job.SizeText(removed.Bytes)} freed)");
            }
            else
            {
                item.Problem = "Not everything could be deleted";
                foreach (string problem in removed.Problems)
                    Write($"Could not delete all of {item.Name} from this PC: {problem}");
            }
            Deleted?.Invoke(item.Folder);
        }
        finally
        {
            item.IsDeleting = false;
            _deleting--;
            ShowList();
            ShowStatus();
            if (_deleting == 0)
                Idle?.Invoke();
        }
    }

    private void ShowStatus()
    {
        if (_deleting > 0)
        {
            Status = _deleting == 1 ? "Deleting from this PC" : $"Deleting {_deleting} from this PC";
            return;
        }
        int mods = _items.Count(item => item.IsMod), maps = _items.Count - mods;
        Status = _items.Count == 0 ? (_listed ? "Nothing converted yet" : "") : $"{Counted(maps, mods)} converted · {Job.SizeText(_items.Sum(item => item.Size))}";
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
        ConvertedItem[] found = [.. _items.Where(Sought)];
        string Count(string kind) => _listed ? "  " + found.Count(item => kind.Length == 0 || item.Kind == kind) : "";
        AllCount.Text = Count("");
        ZombiesCount.Text = Count(CommunityKinds.Zombies);
        MultiplayerCount.Text = Count(CommunityKinds.Multiplayer);
        ModsCount.Text = Count(CommunityKinds.Mod);
        if (_view.Cast<object>().Any())
        {
            Empty.Visibility = Visibility.Collapsed;
            ListScroll.Visibility = Visibility.Visible;
        }
        else if (_items.Count == 0)
        {
            ShowEmpty("Nothing converted yet", "Maps and mods in your output folder show here.");
        }
        else if (SearchBox.Text.Trim().Length > 0)
        {
            ShowEmpty("Nothing matches your search", "");
        }
        else
        {
            ShowEmpty(_kind switch
            {
                CommunityKinds.Zombies => "No Zombies maps converted yet",
                CommunityKinds.Multiplayer => "No Multiplayer maps converted yet",
                CommunityKinds.Mod => "No mods converted yet",
                _ => "Nothing converted yet",
            }, "");
        }
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

    private void Write(string line) => Log?.Invoke(line);
}
