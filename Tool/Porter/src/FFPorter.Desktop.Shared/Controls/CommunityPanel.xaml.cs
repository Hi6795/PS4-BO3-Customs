using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;

namespace FFPorter.Desktop.Controls;

public partial class CommunityPanel : UserControl
{
    private const int PicturesAtOnce = 4;
    private const string StopNote = " · stops when this file is done", PauseNote = " · pauses when this file is done";
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(45), Relist = TimeSpan.FromMinutes(10);

    private sealed record Work(CommunityMap Map, bool Fetch, bool Send, bool Remove = false, bool Whole = false);

    private readonly ObservableCollection<CommunityMap> _maps = [];
    private readonly List<Work> _waiting = [];
    private readonly Dictionary<CommunityMap, Work> _parked = [];
    private readonly HashSet<string> _staged = new(StringComparer.Ordinal);
    private readonly ICollectionView _view;
    private readonly CommunityLibrary _library = CommunityLibrary.Load();
    private readonly PauseGate _pause = new();
    private CancellationTokenSource _session = new();
    private CancellationTokenSource? _download, _stop, _work;
    private bool _parking;
    private CommunityMap? _current, _files;
    private Task<CommunityMap?>? _listing;
    private HashSet<string>? _onConsole;
    private DateTime _looked, _listed;
    private string _kind = "", _console = "", _status = "";
    private bool _loading, _loaded, _closing, _looking, _removing, _mapsListed, _modsListed;

    private static AppSettings Settings => AppSettings.Current;

    private static string Remote(CommunityMap map) => ConsoleFtp.Folder(map, Settings.ConsoleStorage);

    public CommunityPanel()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_maps);
        _view.Filter = Matches;
        MapList.ItemsSource = _view;
        foreach (RadioButton filter in (RadioButton[])[AllFilter, ZombiesFilter, MultiplayerFilter, ModsFilter])
            filter.Checked += FilterChanged;
        CommunityDownloader.ClearStale();
        ShowEmpty("Community maps and mods", "The list loads when this tab opens.");
    }

    public Action<string>? Log { get; set; }

    public Action? Idle { get; set; }

    public Action? StatusChanged { get; set; }

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

    public bool IsDownloading => _current != null || _waiting.Count > 0;

    public bool IsSending => _stop != null || _removing;

    public CommunityMap? ConsoleFiles => _files;

    public string ConsoleWork => _current is not { IsConsole: true } ? "" : _stop != null ? "sending the console files" : "downloading the console files";

    private bool ListBusy => _current is { IsConsole: false } || _waiting.Any(work => !work.Map.IsConsole);

    public void Activate()
    {
        if (!_loaded)
        {
            _ = LoadAsync();
            return;
        }
        ShowLocal();
        _ = LookAsync(false);
    }

    public void OutputChanged() => ShowLocal();

    public bool Downloaded(string folder) => _library.Knows(folder);

    public void SettingsChanged()
    {
        ShowLocal();
        if (_loaded)
            _ = LookAsync(true);
    }

    public void Converting(string folder)
    {
        if (_library.Forget(folder))
            ShowLocal();
    }

    public void CancelAll()
    {
        _closing = true;
        foreach (Work work in _waiting)
            work.Map.State = RunStates.Cancelled;
        _waiting.Clear();
        foreach (CommunityMap map in _parked.Keys)
        {
            map.IsPaused = false;
            map.CanPause = false;
            map.StageText = "";
            map.State = RunStates.Cancelled;
        }
        _parked.Clear();
        _work?.Cancel();
        _download?.Cancel();
        StopSending();
        _session.Cancel();
    }

    private void StopSending()
    {
        if (_stop == null || _stop.IsCancellationRequested)
            return;
        _stop.Cancel();
        if (_current is not { } map)
            return;
        map.CanPause = false;
        if (map.StageText.EndsWith(PauseNote, StringComparison.Ordinal))
            map.StageText = map.StageText[..^PauseNote.Length];
        if (!map.StageText.EndsWith(StopNote, StringComparison.Ordinal))
            map.StageText += StopNote;
    }

    private async Task LoadAsync()
    {
        if (_loading)
            return;
        if (ListBusy)
        {
            Status = "The list reloads once the downloads have finished";
            return;
        }
        _session.Cancel();
        CancellationTokenSource session = _session = new CancellationTokenSource();
        _maps.Clear();
        _loaded = false;
        ShowCounts();
        if (!CommunityCatalog.TryAddress(Edition.Current.CommunityList, out Uri list))
        {
            ShowEmpty("No community list", $"This converter has no list of {Edition.Current.GameName} maps to download.");
            Status = "";
            return;
        }

        _loading = true;
        RefreshButton.IsEnabled = false;
        ShowEmpty("Loading the map list", "");
        Status = "Loading the map list";
        try
        {
            IReadOnlyList<CommunityMap> maps = await CommunityCatalog.LoadAsync(list, Write, session.Token);
            foreach (CommunityMap map in maps)
                _maps.Add(map);
            ShowLocal();
            _loaded = true;
            ShowList();
            ShowStatus();
            _ = LoadPicturesAsync(maps, session.Token);
            _ = LookAsync(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException error)
        {
            ShowEmpty("Could not load the map list", Sentence(error.Message));
            Status = "The map list could not be loaded";
            Write($"Could not load the community map list: {error.Message}");
        }
        finally
        {
            _loading = false;
            RefreshButton.IsEnabled = true;
        }
    }

    public Task<CommunityMap?> FindConsoleFilesAsync()
    {
        if (_listing is { IsCompleted: false })
            return _listing;
        if (_files != null && (_files.IsBusy || DateTime.UtcNow - _listed < Relist))
            return Task.FromResult<CommunityMap?>(_files);
        return _listing = ListConsoleFilesAsync();
    }

    private async Task<CommunityMap?> ListConsoleFilesAsync()
    {
        Edition edition = Edition.Current;
        if (edition.UpdateSource is not { } source || edition.ConsoleArchive is not { } archive || edition.ConsolePlugin is not { } plugin)
            return null;
        ConsolePackage? package;
        try
        {
            package = await AppUpdate.ConsoleAsync(AppUpdate.Releases(source), archive, plugin, CancellationToken.None);
        }
        catch (IOException error)
        {
            Write($"The console files could not be listed: {error.Message}");
            if (_files == null)
                throw;
            return _files;
        }
        _listed = DateTime.UtcNow;
        if (package == null || package.Plugin.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return _files;
        string name = Path.GetFileNameWithoutExtension(package.Plugin.Name);
        var found = new CommunityMap
        {
            Id = name,
            Title = "Console files",
            Folder = name,
            Kind = CommunityKinds.Console,
            Parts = [package.Archive.Link],
            Extras = [new CommunityFile(package.Plugin.Name, package.Plugin.Link, package.Plugin.Size, package.Plugin.Sha256)],
            Size = package.Archive.Size,
            Sha256 = package.Archive.Sha256,
            Version = package.Release,
        };
        if (_files is not { } known || (!known.IsBusy && known.Stamp != found.Stamp))
            _files = found;
        ShowLocal(_files);
        return _files;
    }

    public void ShowConsoleFiles()
    {
        if (_files != null)
            ShowLocal(_files);
    }

    private async Task LookAsync(bool again)
    {
        if (!ConsoleFtp.TryAddress(Settings.ConsoleAddress, out string host, out int port))
        {
            _onConsole = null;
            return;
        }
        string root = Settings.ConsoleRoot, address = $"{host}:{port}{root}";
        if (_looking || IsSending || (!again && _onConsole != null && _console == address && DateTime.UtcNow - _looked < Fresh))
            return;
        _looking = true;
        try
        {
            CancellationToken token = _session.Token;
            ConsoleContents contents = await Task.Run(() => ConsoleFtp.ContentsAsync(host, port, root, token));
            _onConsole = new HashSet<string>(contents.Maps.Select(name => $"{ConsoleFtp.Maps(root)}/{name}").Concat(contents.Mods.Select(name => $"{ConsoleFtp.Mods(root)}/{name}")),
                StringComparer.OrdinalIgnoreCase);
            _mapsListed = contents.Maps.Count > 0;
            _modsListed = contents.Mods.Count > 0;
            _console = address;
            _looked = DateTime.UtcNow;
            ShowLocal();
        }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        {
            if (_console != address)
                _onConsole = null;
        }
        finally
        {
            _looking = false;
        }
    }

    private static async Task LoadPicturesAsync(IReadOnlyList<CommunityMap> maps, CancellationToken token)
    {
        var pending = new Queue<CommunityMap>(maps.Where(map => map.Thumbnail != null));
        async Task Next()
        {
            try
            {
                while (pending.Count > 0 && !token.IsCancellationRequested)
                {
                    CommunityMap map = pending.Dequeue();
                    map.Picture = await CommunityCatalog.PictureAsync(map.Thumbnail!, token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
        await Task.WhenAll(Enumerable.Range(0, PicturesAtOnce).Select(_ => Next()));
    }

    private void RefreshClick(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _view.Refresh();
        if (_loaded)
            ShowList();
    }

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        _kind = (sender as FrameworkElement)?.Tag as string ?? "";
        _view.Refresh();
        if (_loaded)
            ShowList();
    }

    private bool Matches(object item) => item is CommunityMap map && (_kind.Length == 0 || map.Kind == _kind) && Found(map);

    private bool Found(CommunityMap map)
    {
        string query = SearchBox.Text.Trim();
        return query.Length == 0
            || map.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || map.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
            || map.Author.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void MoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
            return;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private static CommunityMap? Row(object sender) => (sender as FrameworkElement)?.DataContext as CommunityMap;

    private void StartClick(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is { } map)
            Start(map, Window.GetWindow(this));
    }

    public void Start(CommunityMap map, Window? owner)
    {
        if (!map.CanStart)
            return;
        string destination = Settings.CommunityFolder(map);
        bool fetch = map.Local != LocalCopies.Current;
        if (fetch && map.Local == LocalCopies.None && HasFiles(destination)
            && !MessageDialog.Confirm(owner, DialogKind.Warning, $"Replace {map.Folder}?", "It is already in your output folder and gets replaced.", "Replace", danger: true))
        {
            return;
        }
        Enqueue(new Work(map, fetch, !fetch));
    }

    private void ResendClick(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is { } map)
            Resend(map);
    }

    public void Resend(CommunityMap map)
    {
        if (map.CanResend)
            Enqueue(new Work(map, false, true, Whole: true));
    }

    private void RemoveClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CommunityMap { CanRemove: true } map)
            return;
        if (!MessageDialog.Confirm(Window.GetWindow(this), DialogKind.Warning, $"Remove {map.Title} from the console?",
            $"{Remote(map)}\nIt is deleted from the console for good.", "Remove", danger: true))
        {
            return;
        }
        Enqueue(new Work(map, false, false, true));
    }

    private void Enqueue(Work work)
    {
        work.Map.Problem = "";
        work.Map.Progress = 0;
        work.Map.State = RunStates.Queued;
        _waiting.Add(work);
        StepAside();
        ShowStatus();
        _ = PumpAsync();
    }

    private void StepAside()
    {
        if (_parking || _waiting.Count == 0 || _current is not { IsPaused: true })
            return;
        _parking = true;
        if (_stop != null)
            _stop.Cancel();
        else
            _work?.Cancel();
    }

    private async Task PumpAsync()
    {
        if (_current != null)
            return;
        while (_waiting.Count > 0)
        {
            Work work = _waiting[0];
            _waiting.RemoveAt(0);
            CommunityMap map = work.Map;
            _current = map;
            string destination = Settings.CommunityFolder(map);
            bool send = work.Send || (work.Fetch && Settings.SendToConsole), sending = false, parked = false;
            string? extras = null;
            using CancellationTokenSource asked = _work = new CancellationTokenSource();
            map.StageText = "Starting";
            map.State = RunStates.Running;
            map.CanPause = !work.Remove;
            ShowStatus();
            try
            {
                if (work.Remove)
                    await RemoveAsync(map);
                bool known = !work.Whole && map.Extras.Count > 0 && ArchiveSent(map);
                bool? there = null;
                if (work.Fetch)
                {
                    bool held = map.Extras.Count > 0 && map.Local == LocalCopies.Outdated && _library.Part(destination) == map.Sha256;
                    if (held || (known && send && map.Local == LocalCopies.None && !HasFiles(destination)))
                        extras = await FetchExtrasAsync(map, send ? 0.5 : 1);
                    if (extras != null && held)
                    {
                        Renew(map, extras, destination);
                    }
                    else if (extras == null || (there = await OnConsoleAsync(map, extras)) == false)
                    {
                        Discard(ref extras);
                        await FetchAsync(map, destination, send ? 0.5 : 1);
                    }
                }
                asked.Token.ThrowIfCancellationRequested();
                string source = destination;
                if (send && known)
                {
                    extras ??= CommunityDownloader.CopyExtras(map, destination);
                    if (extras is { } only && (there ?? await OnConsoleAsync(map, only)))
                        source = only;
                }
                if (send && !work.Send && !await MayReplaceAsync(map, source))
                    send = false;
                asked.Token.ThrowIfCancellationRequested();
                if (send)
                {
                    sending = true;
                    bool confirmed = await SendAsync(map, source, work.Fetch ? 0.5 : 0);
                    if (Settings.SendToConsole && confirmed && HasFiles(destination))
                        RemoveLocal(map, destination, " now that it is on the console");
                    else if (Settings.SendToConsole && !confirmed)
                        Write($"{map.Title} was kept on this PC: the console could not confirm the sizes of what it received.");
                }
                map.Progress = 1;
                map.State = work.Remove ? RunStates.Ready : RunStates.Done;
            }
            catch (OperationCanceledException) when (_parking && !_closing)
            {
                parked = true;
            }
            catch (OperationCanceledException)
            {
                if (!sending && !_closing)
                    CommunityDownloader.Discard(map);
                map.State = RunStates.Cancelled;
                Write(sending ? $"{map.Title}: sending to the console was stopped; what is already on the console was left as it was" : $"{map.Title}: cancelled");
            }
            catch (Exception error)
            {
                map.Problem = work.Remove ? $"Not removed from the console: {error.Message}" : sending ? $"Not sent to the console: {error.Message}" : Sentence(error.Message);
                map.State = work.Remove ? RunStates.Ready : RunStates.Failed;
                Write(work.Remove ? $"{map.Title} was not removed from the console: {error.Message}"
                    : sending ? $"{map.Title} was not sent to the console: {error.Message}" : $"{map.Title} was not downloaded: {error.Message}");
            }
            finally
            {
                Discard(ref extras);
                map.Locked = false;
                map.Fetched = CommunityDownloader.Fetched(map);
                if (parked)
                {
                    _parked[map] = sending ? new Work(map, false, true, Whole: work.Whole) : work;
                    map.CanPause = true;
                    map.IsPaused = true;
                    if (!sending)
                        map.StageText = map.Fetched > 0 ? $"{DownloadProgress.Paused} · {Job.SizeText(map.Fetched)}{(map.Size > 0 ? " of " + map.SizeText : "")}" : DownloadProgress.Paused;
                    else if (!map.StageText.StartsWith(DownloadProgress.Paused, StringComparison.Ordinal))
                        map.StageText = DownloadProgress.Paused;
                }
                else
                {
                    map.StageText = "";
                    map.CanPause = false;
                    map.IsPaused = false;
                }
                _pause.Resume();
                _parking = false;
                _work = null;
                _current = null;
                ShowLocal(map);
            }
        }
        ShowStatus();
        Idle?.Invoke();
    }

    private CancellationTokenSource Asked() => CancellationTokenSource.CreateLinkedTokenSource(_work?.Token ?? CancellationToken.None);

    private static void Discard(ref string? extras)
    {
        if (extras != null)
            CommunityDownloader.Drop(extras);
        extras = null;
    }

    private bool ArchiveSent(CommunityMap map) =>
        ConsoleFtp.TryAddress(Settings.ConsoleAddress, out string host, out int port) && _library.Part(ConsoleFtp.Target(host, port, Remote(map))) == map.Sha256;

    private async Task<string> FetchExtrasAsync(CommunityMap map, double span)
    {
        using CancellationTokenSource source = _download = Asked();
        try
        {
            Write($"\n[{DateTime.Now:T}] Downloading the latest {string.Join(", ", map.Extras.Select(extra => extra.Name))} for {map.Title}");
            IProgress<DownloadProgress> progress = Scaled(map, 0, span);
            return await Task.Run(() => CommunityDownloader.ExtrasAsync(map, progress, source.Token));
        }
        finally
        {
            _download = null;
        }
    }

    private void Renew(CommunityMap map, string extras, string destination)
    {
        foreach (CommunityFile extra in map.Extras)
            File.Copy(Path.Combine(extras, extra.Name), Path.Combine(destination, extra.Name), overwrite: true);
        _library.Remember(map, destination);
        map.Local = LocalCopies.Current;
        map.Sent = false;
        Write($"{map.Title}: {string.Join(", ", map.Extras.Select(extra => extra.Name))} in {destination} is the latest now");
    }

    private async Task<bool> OnConsoleAsync(CommunityMap map, string extras)
    {
        if (!ConsoleFtp.TryAddress(Settings.ConsoleAddress, out string host, out int port))
            throw new IOException("no console address is saved in Settings");
        string remote = Remote(map);
        map.StageText = "Checking the console";
        CancellationToken token = _work?.Token ?? CancellationToken.None;
        return await Task.Run(() => ConsoleFtp.PresentAsync(host, port, extras, remote, token)) >= map.Extras.Count;
    }

    private async Task FetchAsync(CommunityMap map, string destination, double span)
    {
        using CancellationTokenSource source = _download = Asked();
        try
        {
            Write($"\n[{DateTime.Now:T}] Downloading {map.Title} ({map.Meta}) to {destination}");
            IProgress<DownloadProgress> progress = Scaled(map, 0, span);
            await Task.Run(() => CommunityDownloader.DownloadAsync(map, destination, progress, Post, _pause, source.Token));
        }
        finally
        {
            _download = null;
        }
        _library.Remember(map, destination);
        map.Local = LocalCopies.Current;
        map.Sent = false;
        Write($"{map.Title} is in {destination}");
    }

    private async Task<bool> MayReplaceAsync(CommunityMap map, string destination)
    {
        if (!ConsoleFtp.TryAddress(Settings.ConsoleAddress, out string host, out int port))
            return true;
        string remote = Remote(map);
        try
        {
            map.StageText = "Checking the console";
            CancellationToken token = _work?.Token ?? CancellationToken.None;
            if (await Task.Run(() => ConsoleFtp.PresentAsync(host, port, destination, remote, token)) == 0)
                return true;
        }
        catch (IOException)
        {
            return true;
        }
        Window? front = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive) ?? Window.GetWindow(this);
        if (!_closing && MessageDialog.Overwrite(front, [map.Title]))
            return true;
        Write($"{map.Title} was not sent: it is already on the console, which was left as it is.");
        return false;
    }

    private async Task<bool> SendAsync(CommunityMap map, string destination, double from)
    {
        if (!ConsoleFtp.TryAddress(Settings.ConsoleAddress, out string host, out int port))
            throw new IOException("no console address is saved in Settings");
        string remote = Remote(map);
        using CancellationTokenSource source = _stop = Asked();
        bool confirmed;
        try
        {
            ShowStatus();
            Write($"\n[{DateTime.Now:T}] Sending {map.Title} to the console at {host}:{port}, into {remote}");
            IProgress<DownloadProgress> progress = Scaled(map, from, 1 - from);
            confirmed = await Task.Run(() => ConsoleFtp.SendAsync(host, port, destination, remote, map.MainFiles, _staged, progress, Post, _pause, source.Token));
        }
        finally
        {
            _stop = null;
        }
        _library.RememberSent(map, ConsoleFtp.Target(host, port, remote));
        if (!map.IsConsole && _onConsole?.Add(remote) == true)
        {
            _mapsListed |= !map.IsMod;
            _modsListed |= map.IsMod;
        }
        map.HasConsole = true;
        map.Sent = true;
        map.Stale = false;
        Write($"{map.Title} is on the console in {remote}");
        return confirmed;
    }

    private async Task RemoveAsync(CommunityMap map)
    {
        if (!ConsoleFtp.TryAddress(Settings.ConsoleAddress, out string host, out int port))
            throw new IOException("no console address is saved in Settings");
        string remote = Remote(map);
        _removing = true;
        try
        {
            map.Locked = true;
            map.StageText = "Removing from the console";
            ShowStatus();
            Write($"\n[{DateTime.Now:T}] Removing {map.Title} from the console at {host}:{port}: {remote}");
            await Task.Run(() => ConsoleFtp.RemoveAsync(host, port, remote, CancellationToken.None));
        }
        finally
        {
            _removing = false;
        }
        _library.Forget(ConsoleFtp.Target(host, port, remote));
        _onConsole?.Remove(remote);
        map.Sent = false;
        map.Stale = false;
        map.Found = false;
        Write($"{map.Title} was removed from the console");
    }

    private Removal RemoveLocal(CommunityMap map, string destination, string why)
    {
        Removal removed = Directory.Exists(destination) ? Cleanup.ClearFolders([destination]) : new Removal(0, 0, []);
        _library.Forget(destination);
        map.Local = LocalCopies.None;
        if (!map.IsBusy)
        {
            map.Problem = "";
            map.State = RunStates.Ready;
        }
        foreach (string problem in removed.Problems)
            Write($"Could not delete all of {map.Title} from this PC: {problem}");
        if (removed.Problems.Count == 0)
            Write($"{map.Title}: deleted from this PC{why} ({Job.SizeText(removed.Bytes)} freed)");
        return removed;
    }

    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is { } map)
            Delete(map, Window.GetWindow(this));
    }

    public void Delete(CommunityMap map, Window? owner)
    {
        if (!map.CanDelete)
            return;
        if (map.Local == LocalCopies.None)
        {
            Forget(map);
            return;
        }
        string destination = Settings.CommunityFolder(map);
        if (!MessageDialog.Confirm(owner, DialogKind.Warning, $"Delete {map.Title} from this PC?",
            $"{Job.SizeText(Cleanup.FolderSize(destination))} is deleted for good." + (map.OnConsole ? " The copy on the console stays." : " It can be downloaded again."),
            "Delete", danger: true))
        {
            return;
        }
        Removal removed = RemoveLocal(map, destination, "");
        if (removed.Problems.Count > 0)
        {
            MessageDialog.Inform(owner, DialogKind.Warning, "Not everything could be deleted",
                $"{string.Join("\n", removed.Problems)}\n\nWhat is left is in {destination}.");
        }
    }

    private Progress<DownloadProgress> Scaled(CommunityMap map, double from, double span) => new(report =>
    {
        if (!map.IsRunning || _parked.ContainsKey(map) || (_stop == null && _work is { IsCancellationRequested: true }))
            return;
        map.Progress = from + span * report.Fraction;
        string note = _stop is { IsCancellationRequested: true } && !_parking ? StopNote : _stop != null && map.IsPaused && report.Stage != DownloadProgress.Paused ? PauseNote : "";
        map.StageText = (report.Detail.Length > 0 ? $"{report.Stage} · {report.Detail}" : report.Stage) + note;
    });

    private void Post(string line) => Dispatcher.InvokeAsync(() => Write(line));

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is { } map)
            Cancel(map);
    }

    public void Cancel(CommunityMap map)
    {
        if (!map.CanCancel)
            return;
        if (ReferenceEquals(map, _current))
        {
            _parking = false;
            if (_stop != null)
            {
                StopSending();
            }
            else
            {
                map.StageText = "Cancelling";
                map.CanPause = false;
                _work?.Cancel();
            }
        }
        else if (_parked.Remove(map, out Work? parked))
        {
            if (parked.Fetch)
                CommunityDownloader.Discard(map);
            map.IsPaused = false;
            map.CanPause = false;
            map.StageText = "";
            map.State = RunStates.Cancelled;
            Write($"{map.Title}: cancelled");
            ShowLocal(map);
            ShowStatus();
        }
        else if (_waiting.RemoveAll(work => ReferenceEquals(work.Map, map)) > 0)
        {
            map.State = RunStates.Cancelled;
            ShowStatus();
        }
    }

    private void PauseClick(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is { } map)
            Pause(map);
    }

    public void Pause(CommunityMap map)
    {
        if (_parked.Remove(map, out Work? parked))
        {
            map.IsPaused = false;
            map.CanPause = false;
            map.StageText = "";
            map.State = RunStates.Queued;
            _waiting.Insert(0, parked);
            Write($"{map.Title}: carrying on");
            StepAside();
            ShowStatus();
            _ = PumpAsync();
            return;
        }
        if (!ReferenceEquals(map, _current) || !map.CanPause || _parking)
            return;
        map.IsPaused = !map.IsPaused;
        if (map.IsPaused)
        {
            _pause.Pause();
            if (_stop != null)
                map.StageText += PauseNote;
        }
        else
        {
            _pause.Resume();
            if (map.StageText.EndsWith(PauseNote, StringComparison.Ordinal))
                map.StageText = map.StageText[..^PauseNote.Length];
        }
        Write($"{map.Title}: {(map.IsPaused ? "paused" : "carrying on")}");
        StepAside();
        ShowStatus();
    }

    private void OpenClick(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is { } map)
            Open(map);
        else
            ShowLocal();
    }

    public void Open(CommunityMap map)
    {
        if (Directory.Exists(Settings.CommunityFolder(map)))
            Process.Start(new ProcessStartInfo(Settings.CommunityFolder(map)) { UseShellExecute = true });
        else
            ShowLocal();
    }

    private void Forget(CommunityMap map)
    {
        long bytes = map.Fetched;
        CommunityDownloader.Discard(map);
        map.Fetched = CommunityDownloader.Fetched(map);
        map.Problem = "";
        map.State = RunStates.Ready;
        Write($"{map.Title}: the part that was downloaded was deleted ({Job.SizeText(bytes)} freed)");
    }

    private void ShowLocal()
    {
        foreach (CommunityMap map in _files == null ? _maps : _maps.Prepend(_files))
            ShowLocal(map);
    }

    private void ShowLocal(CommunityMap map)
    {
        if (map.IsBusy)
            return;
        bool console = ConsoleFtp.TryAddress(Settings.ConsoleAddress, out string host, out int port);
        bool looked = console && _onConsole != null && _console == $"{host}:{port}{Settings.ConsoleRoot}";
        map.Local = _library.Check(map, Settings.CommunityFolder(map));
        map.Fetched = CommunityDownloader.Fetched(map);
        string remote = Remote(map), target = console ? ConsoleFtp.Target(host, port, remote) : "";
        bool there = looked && !map.IsConsole && _onConsole!.Contains(remote);
        if (looked && !there && !map.IsConsole && (map.IsMod ? _modsListed : _mapsListed))
            _library.Forget(target);
        map.HasConsole = console;
        map.Sent = console && _library.Sent(map, target);
        map.Stale = console && !map.Sent && _library.Knows(target);
        map.Found = there;
        map.Light = map.Extras.Count > 0 && (map.Local == LocalCopies.Outdated ? _library.Part(Settings.CommunityFolder(map)) == map.Sha256
            : map.Local == LocalCopies.None && map.Stale && Settings.SendToConsole && _library.Part(target) == map.Sha256);
    }

    private void ShowStatus()
    {
        if (_current != null)
        {
            string doing = _removing ? $"Removing {_current.Title} from the console" : _stop != null ? $"Sending {_current.Title} to the console" : $"Downloading {_current.Title}";
            Status = doing + (_current.IsPaused ? " · paused" : "") + (_waiting.Count > 0 ? $" · {_waiting.Count} waiting" : "")
                + (_parked.Count > 0 ? $" · {_parked.Count} paused" : "");
        }
        else if (_parked.Count > 0)
        {
            Status = _parked.Count == 1 ? $"{_parked.Keys.First().Title} · paused" : $"{_parked.Count} downloads paused";
        }
        else if (_loaded)
        {
            int mods = _maps.Count(map => map.IsMod), maps = _maps.Count - mods;
            var parts = new List<string>();
            if (maps > 0 || mods == 0)
                parts.Add($"{maps} map{(maps == 1 ? "" : "s")}");
            if (mods > 0)
                parts.Add($"{mods} mod{(mods == 1 ? "" : "s")}");
            Status = string.Join(" and ", parts) + " to download";
        }
        else if (!_loading)
        {
            Status = "";
        }
    }

    private void ShowList()
    {
        ShowCounts();
        if (_view.Cast<object>().Any())
        {
            Empty.Visibility = Visibility.Collapsed;
            ListScroll.Visibility = Visibility.Visible;
        }
        else if (_maps.Count == 0)
        {
            ShowEmpty("Nothing here yet", "The list is empty. Check back later.");
        }
        else if (SearchBox.Text.Trim().Length > 0)
        {
            ShowEmpty("Nothing matches your search", "");
        }
        else
        {
            ShowEmpty(_kind switch
            {
                CommunityKinds.Zombies => "No Zombies maps in the list yet",
                CommunityKinds.Multiplayer => "No Multiplayer maps in the list yet",
                CommunityKinds.Mod => "No mods in the list yet",
                _ => "Nothing here yet",
            }, "");
        }
    }

    private void ShowCounts()
    {
        CommunityMap[] found = [.. _maps.Where(Found)];
        string Count(string kind) => _loaded ? "  " + found.Count(map => kind.Length == 0 || map.Kind == kind) : "";
        AllCount.Text = Count("");
        ZombiesCount.Text = Count(CommunityKinds.Zombies);
        MultiplayerCount.Text = Count(CommunityKinds.Multiplayer);
        ModsCount.Text = Count(CommunityKinds.Mod);
    }

    private void ShowEmpty(string title, string text)
    {
        EmptyTitle.Text = title;
        EmptyText.Text = text;
        EmptyText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Empty.Visibility = Visibility.Visible;
        ListScroll.Visibility = Visibility.Collapsed;
    }

    private static bool HasFiles(string folder)
    {
        try
        {
            return Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string Sentence(string text) => text.Length > 0 ? char.ToUpperInvariant(text[0]) + text[1..] : text;

    private void Write(string line) => Log?.Invoke(line);
}
