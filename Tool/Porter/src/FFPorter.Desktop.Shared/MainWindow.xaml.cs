using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using FFPorter.Core;
using FFPorter.Core.Common.Fidelity;
using FFPorter.Core.Common.Native;
using FFPorter.Core.Common.Tools;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;
using FFPorter.Desktop.Theme;
using Microsoft.Win32;

namespace FFPorter.Desktop;

public partial class MainWindow : Window
{
    private const long MemoryPerJob = 3L << 30;

    private readonly ObservableCollection<Job> _jobs = [];
    private readonly Dictionary<string, JobGroup> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<BackendProcess> _backends = [];
    private readonly Dictionary<Job, BackendProcess> _converting = [];
    private readonly HashSet<Job> _dropped = [];
    private readonly ObservableCollection<Job> _active = [];
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly HashSet<string> _staged = new(StringComparer.Ordinal);
    private bool _running, _stop, _clearing, _looking, _closeWhenIdle, _leaving, _closeAfterUpdate, _updateFailed, _idle;
    private CancellationTokenSource? _sending;
    private readonly string? _exe = AppUpdate.Location;
    private UpdateFile? _update;
    private CancellationTokenSource? _updating;
    private readonly bool _firstRun = !Directory.Exists(Workspace.WorkDirectory);
    private int _done, _count, _width = 1;
    private long _cacheSize = -1;
    private bool _measuring;
    private DateTime _started;
    private string _stage = "", _detail = "";

    private static AppSettings Settings => AppSettings.Current;
    private static Edition Edition => Edition.Current;

    public MainWindow()
    {
        InitializeComponent();
        Title = Edition.Title;
        TitleText.Text = Edition.Product;
        GameText.Text = Edition.GameName;
        AuthorText.Text = Edition.Author is { Length: > 0 } author ? $"by {author}" : "";
        SubtitleText.Text = Edition.Subtitle;
        InstalledButton.Visibility = AddInstalledItem.Visibility = Edition.FindsInstalled ? Visibility.Visible : Visibility.Collapsed;
        ThemeManager.StyleTitleBar(this);
        ThemeManager.FollowScreen(this, () => Settings.InterfaceScale);
        JobList.ItemsSource = _jobs;
        CollectionViewSource.GetDefaultView(_jobs).GroupDescriptions.Add(new PropertyGroupDescription(nameof(Job.Group)));
        _jobs.CollectionChanged += (_, _) => RefreshQueue();
        ShowOutput();
        ShowGameFolder();
        ShowSettings();
        ShowCache();
        Community.Log = Log.Append;
        Community.StatusChanged = () =>
        {
            LockConverted();
            if (CommunityTab.IsChecked == true || _idle)
                ShowIdle();
        };
        Community.Idle = () =>
        {
            if (_closeWhenIdle)
            {
                Close();
                return;
            }
            LockConverted();
            OnConsole.Changed();
            Converted.Refresh();
        };
        OnConsole.Log = Log.Append;
        OnConsole.StatusChanged = () =>
        {
            if (InstalledTab.IsChecked == true)
                ShowIdle();
        };
        OnConsole.Idle = () =>
        {
            if (_closeWhenIdle)
                Close();
        };
        OnConsole.Removed = Unmark;
        Converted.Log = Log.Append;
        Converted.StatusChanged = () =>
        {
            if (ConvertedTab.IsChecked == true)
                ShowIdle();
        };
        Converted.Downloaded = Community.Downloaded;
        Converted.Send = item => _ = SendConverted(item);
        Converted.Deleted = _ =>
        {
            Community.OutputChanged();
            ShowSend();
        };
        Converted.Idle = () =>
        {
            if (_closeWhenIdle)
                Close();
        };
        ConvertTab.Checked += (_, _) => ShowSection();
        ConvertedTab.Checked += (_, _) => ShowSection();
        CommunityTab.Checked += (_, _) => ShowSection();
        InstalledTab.Checked += (_, _) => ShowSection();
        Loaded += async (_, _) =>
        {
            Task<UpdateFile?> update = FindUpdate();
            Task<string?> drive = LostDrive();
            AskForGameFolder();
            await PrepareTools();
            _ = MeasureCache();
            WarnLostDrive(await drive);
            if (await update is { } found)
                await OfferUpdate(found);
        };
        DragOver += (_, e) =>
        {
            e.Effects = !_running && _updating == null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (_updating == null && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                ConvertTab.IsChecked = true;
                AddPaths(paths);
            }
            e.Handled = true;
        };
        Closing += WindowClosing;
        _clock.Tick += (_, _) => ShowProgress();
        RefreshQueue();
        Converted.Refresh();
    }


    private void ShowSection()
    {
        bool community = CommunityTab.IsChecked == true, installed = InstalledTab.IsChecked == true, converted = ConvertedTab.IsChecked == true;
        ConvertView.Visibility = community || installed || converted ? Visibility.Collapsed : Visibility.Visible;
        Community.Visibility = community ? Visibility.Visible : Visibility.Collapsed;
        OnConsole.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        Converted.Visibility = converted ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = community ? "Community" : installed ? "Installed" : converted ? "Converted" : "Convert";
        SubtitleText.Text = community ? "Converted maps and mods, ready to download or send to your console."
            : installed ? "Maps and mods installed on your console, on every drive."
            : converted ? "Converted maps and mods on this PC, ready for your console." : Edition.Subtitle;
        ShowActions();
        if (!_running)
            Progress.Value = 0;
        ShowIdle();
        if (community)
            Community.Activate();
        if (installed)
            OnConsole.Activate();
        if (converted)
            Converted.Activate();
    }

    private void LockConverted() => Converted.Locked = _running || Community.IsDownloading || Community.IsSending;

    private async Task SendConverted(ConvertedItem item)
    {
        if (_running || item.Remote is not { } part)
            return;
        item.Problem = "";
        item.IsSending = true;
        try
        {
            string remote = $"{Settings.ConsoleRoot}/{part}";
            Job[] jobs = [.. _jobs.Where(job => job.IsConverted && Edition.ConsoleFolder(job) == part)];
            item.Sent = await SendToConsole([new Shipment(item.Name, item.Folder, remote, jobs, item.Last)]);
            if (!item.Sent)
                item.Problem = "Not sent";
        }
        finally
        {
            item.IsSending = false;
        }
    }

    private void Unmark(string folder)
    {
        foreach (Job job in _jobs.Where(job => Edition.ConsoleFolder(job) is { } part && $"{Settings.ConsoleRoot}/{part}" == folder))
            job.OnConsole = false;
        ShowSend();
        Community.SettingsChanged();
    }

    private void ShowActions()
    {
        ConvertButton.Visibility = _running || ConvertTab.IsChecked != true ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = _running ? Visibility.Visible : Visibility.Collapsed;
        ShowSend();
    }

    public void Receive(IReadOnlyList<string> paths)
    {
        if (IsVisible)
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
        }
        if (paths.Count == 0)
            return;
        if (_running || _updating != null)
        {
            Log.Append($"Not added while the app is busy: {string.Join(", ", paths)}");
            return;
        }
        ConvertTab.IsChecked = true;
        AddPaths(paths);
    }

    public void AddPaths(IEnumerable<string> paths)
    {
        if (_running)
            return;
        List<Job> found = JobScanner.Scan(paths, Log.Append);
        Job? last = null;
        foreach (Job job in found)
        {
            Forget(_jobs.Where(j => j.OriginalDeleted && (string.Equals(j.MainFile, job.MainFile, StringComparison.OrdinalIgnoreCase)
                || string.Equals(j.Key, job.Key, StringComparison.OrdinalIgnoreCase))));
            if (_jobs.Any(j => string.Equals(j.MainFile, job.MainFile, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (_jobs.Any(j => string.Equals(j.Key, job.Key, StringComparison.OrdinalIgnoreCase)))
            {
                Log.Append($"Skipped {job.MainFile}: another {job.Title} is already queued, and both would convert into the same folder.");
                continue;
            }
            LoadPreviousReport(job);
            if (job.Mod != null)
            {
                if (!_groups.TryGetValue(job.Mod, out JobGroup? group))
                    _groups[job.Mod] = group = new JobGroup(job.Mod);
                job.Group = group;
                group.Add(job);
            }
            _jobs.Add(job);
            last = job;
        }
        if (last != null)
        {
            JobList.SelectedItem = last;
            foreach (Job job in _jobs)
                job.IsExpanded = _jobs.Count == 1;
        }
        else if (found.Count == 0)
            SetStatus("Nothing to convert there", "the log says what was skipped");
    }

    private static void LoadPreviousReport(Job job)
    {
        string report = job.Key + ".fidelity.json";
        if ((FidelityProtocol.Load(Path.Combine(FFPorter.Core.Workspace.ReportDirectory, report))
            ?? FidelityProtocol.Load(Path.Combine(Settings.OutputFor(job), report))
            ?? FidelityProtocol.Load(Path.Combine(Settings.EarlierOutputFor(job), report))
            ?? FidelityProtocol.Load(Path.Combine(Settings.Output, job.Name, report))) is not { } snapshot)
            return;
        job.Report = FidelityView.From(snapshot);
        job.StatusText = snapshot.State == FidelityStates.Done ? "Converted earlier" : "Last run failed";
    }

    private bool CanConvert => !_running && !_clearing && _jobs.Any(job => !job.OriginalDeleted);

    private void RefreshQueue()
    {
        ShowTotals();
        EmptyQueue.Visibility = _jobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        JobList.Visibility = _jobs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RemoveButton.Visibility = ClearButton.Visibility = _jobs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ConvertButton.IsEnabled = CanConvert;
        RemoveButton.IsEnabled = ClearButton.IsEnabled = !_running && _jobs.Count > 0;
        if (!_running)
            Progress.Value = 0;
        ShowIdle();
        ShowSend();
    }

    private void ShowIdle()
    {
        if (_running)
            return;
        if (CommunityTab.IsChecked == true)
        {
            SetStatus(Community.Status.Length > 0 ? Community.Status : "Community maps and mods", "");
            return;
        }
        if (InstalledTab.IsChecked == true)
        {
            SetStatus(OnConsole.Status.Length > 0 ? OnConsole.Status : "On your console", "");
            return;
        }
        if (ConvertedTab.IsChecked == true)
        {
            SetStatus(Converted.Status.Length > 0 ? Converted.Status : "Converted on this PC", "");
            return;
        }
        int ready = _jobs.Count(job => !job.OriginalDeleted);
        var detail = new List<string>();
        if (ready > 0)
            detail.Add($"{Job.SizeText(_jobs.Where(job => !job.OriginalDeleted).Sum(job => job.Length))} in total");
        else if (_jobs.Count > 0)
            detail.Add("add something else to convert");
        if (Settings.DeleteOriginals)
            detail.Add("PC originals are deleted after converting");
        if (Settings.SendToConsole && ready > 0)
            detail.Add("sent to the console when converted");
        if (Community.ConsoleWork.Length > 0)
            detail.Add(Community.ConsoleWork);
        SetStatus(_jobs.Count == 0 ? "Drop something to convert" : ready == 0 ? "Everything here is converted" : $"{ready} ready to convert", string.Join(" · ", detail));
        _idle = true;
    }

    private string ShowTotals()
    {
        string total = Job.SizeText(_jobs.Where(job => !job.OriginalDeleted).Sum(job => job.Length));
        CountText.Text = _jobs.Count.ToString();
        TotalText.Text = _jobs.Any(job => !job.OriginalDeleted) ? total : "";
        return total;
    }

    private void Forget(IEnumerable<Job> jobs)
    {
        foreach (Job job in jobs.ToArray())
        {
            Ungroup(job);
            _jobs.Remove(job);
        }
    }

    private void AddClick(object sender, RoutedEventArgs e)
    {
        if (AddButton.ContextMenu is not { } menu)
            return;
        menu.PlacementTarget = AddButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private async void AddInstalledClick(object sender, RoutedEventArgs e)
    {
        if (_running || _looking)
            return;
        _looking = true;
        IReadOnlyList<Installed> found;
        try
        {
            string? game = Settings.GameFolder;
            found = await Task.Run(() => Edition.FindInstalled(game));
        }
        finally
        {
            _looking = false;
        }
        if (_running)
            return;
        var picker = new InstalledWindow(this, found);
        if (picker.ShowDialog() == true)
            AddPaths(picker.Chosen);
    }

    private void AddFilesClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Fastfiles|*.ff", Multiselect = true, Title = $"Add {Edition.GameName} fastfiles" };
        if (dialog.ShowDialog(this) == true)
            AddPaths(dialog.FileNames);
    }

    private void AddFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Add a folder (a usermap, a mod, a zone folder, or a folder of them)", Multiselect = true };
        if (dialog.ShowDialog(this) == true)
            AddPaths(dialog.FolderNames);
    }

    private void RemoveClick(object sender, RoutedEventArgs e) => RemoveSelected();

    private void RemoveSelected()
    {
        if (_running)
            return;
        Forget(JobList.SelectedItems.Cast<Job>());
    }

    private void ClearClick(object sender, RoutedEventArgs e)
    {
        if (_running)
            return;
        foreach (Job job in _jobs)
            Ungroup(job);
        _jobs.Clear();
    }

    private void Ungroup(Job job)
    {
        if (job.Group is not { } group)
            return;
        group.Remove(job);
        if (group.Jobs.Count == 0)
            _groups.Remove(group.Name);
    }

    private void ExpandClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Job job)
            job.IsExpanded = !job.IsExpanded;
    }

    private void JobPauseClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Job job || !_converting.TryGetValue(job, out BackendProcess? backend))
            return;
        if (job.IsPaused)
        {
            backend.Resume();
            job.IsPaused = false;
            job.Report?.Hold(false);
            Log.Append($"[{DateTime.Now:T}] Resumed {job.Title}");
        }
        else if (backend.Pause())
        {
            job.IsPaused = true;
            job.Report?.Hold(true);
            Log.Append($"[{DateTime.Now:T}] Paused {job.Title}");
        }
        else
        {
            Log.Append($"{job.Title} could not be paused");
        }
        ShowProgress();
    }

    private void JobCancelClick(object sender, RoutedEventArgs e)
    {
        if (!_running || _stop || (sender as FrameworkElement)?.DataContext is not Job job)
            return;
        if (_converting.TryGetValue(job, out BackendProcess? backend))
        {
            _dropped.Add(job);
            Log.Append($"[{DateTime.Now:T}] Cancelled {job.Title}; the rest of the queue carries on");
            backend.Cancel();
        }
        else if (job.State == RunStates.Queued)
        {
            _dropped.Add(job);
            job.State = RunStates.Cancelled;
            job.StatusText = "Cancelled";
            _done++;
            Log.Append($"[{DateTime.Now:T}] {job.Title} was taken out of this run");
            ShowProgress();
        }
    }

    private void JobHeadMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && (sender as FrameworkElement)?.DataContext is Job job)
            job.IsExpanded = !job.IsExpanded;
    }

    private void JobListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            RemoveSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && JobList.SelectedItems.Count == 1 && JobList.SelectedItem is Job job)
        {
            job.IsExpanded = !job.IsExpanded;
            e.Handled = true;
        }
    }

    private void JobSelectionChanged(object sender, SelectionChangedEventArgs e) => ShowSend();

    private void JobMenuOpening(object sender, ContextMenuEventArgs e)
    {
        Job[] chosen = [.. JobList.SelectedItems.Cast<Job>()];
        if (chosen.Length == 0)
        {
            e.Handled = true;
            return;
        }
        List<Shipment> shipments = _running ? [] : Shipments(chosen, true);
        JobSendItem.IsEnabled = shipments.Count > 0;
        JobSendItem.Header = shipments.Count > 0 && shipments.All(shipment => shipment.Jobs.All(job => job.OnConsole)) ? "Send to the console again" : "Send to the console";
        JobOpenItem.IsEnabled = chosen.Length == 1 && Directory.Exists(Settings.OutputFor(chosen[0]));
        JobRemoveItem.IsEnabled = !_running;
    }

    private async void JobSendClick(object sender, RoutedEventArgs e)
    {
        if (ConsoleFtp.TryAddress(Settings.ConsoleAddress, out _, out _))
            await SendToConsole(Shipments(JobList.SelectedItems.Cast<Job>(), true));
        else
            SetStatus("No console is set", "add its IP in Settings");
    }


    private static List<string> Command(Job job, string workspace, int slots)
    {
        List<string> command = [job.Codename, "convert", job.MainFile, "-o", Settings.OutputFor(job), "--force", "--progress", "--root", workspace];
        if (Settings.GameFolder is { Length: > 0 } game && Directory.Exists(game))
            command.AddRange([Edition.GameFolderOption, game]);
        if (job.Mod != null)
            command.AddRange(["--mod", job.Mod]);
        if (Settings.Async)
            command.Add("--async");
        if (slots > 1)
            command.AddRange(["--slots", slots.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (!Settings.MultiCore)
            command.Add("--single-core");
        if (Settings.GpuVideo)
            command.Add("--gpu-video");
        return command;
    }

    internal static int JobsAtOnce() =>
        Math.Clamp(Math.Min(Environment.ProcessorCount / 2, (int)Math.Max(1, SystemMemory.Total / (6L << 30))), 2, 4);

    private async void ConvertClick(object sender, RoutedEventArgs e) => await ConvertAll();

    public IReadOnlyList<string> DllMods() =>
        [.. _jobs.Where(job => job.UsesDll && !job.OriginalDeleted && job.Mod != null).Select(job => job.Mod!).Distinct(StringComparer.OrdinalIgnoreCase)];

    private bool ConfirmDllMods()
    {
        IReadOnlyList<string> mods = DllMods();
        if (mods.Count == 0)
            return true;
        string heading = mods.Count == 1 ? $"{mods[0]} uses a PC DLL" : $"{mods.Count} mods use a PC DLL";
        string body = (mods.Count == 1 ? "" : string.Join(", ", mods) + "\n\n") + "Parts that need the DLL may not work on PS4.";
        return MessageDialog.Confirm(this, DialogKind.Warning, heading, body, "Convert anyway");
    }

    private async Task ConvertAll()
    {
        if (!CanConvert || !ConfirmDllMods())
            return;
        Forget(_jobs.Where(job => job.OriginalDeleted));
        string workspace = Settings.Workspace;
        try
        {
            Directory.CreateDirectory(Settings.Output);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            SetStatus("Cannot use the output folder", error.Message);
            return;
        }

        Job[] queue = [.. _jobs];
        int width = Settings.MultiConversion ? Math.Min(queue.Length, JobsAtOnce()) : 1;
        long freed = 0;
        _stop = false;
        _count = queue.Length;
        _done = 0;
        _width = width;
        _active.Clear();
        _dropped.Clear();
        SetBusy(true);
        Log.Clear();
        foreach (Job job in queue)
        {
            job.IsExpanded = queue.Length == 1;
            job.State = RunStates.Queued;
            job.StatusText = "Queued";
            job.Progress = 0;
            job.Estimate = null;
            job.StartedAt = null;
            job.RemainingText = "";
        }
        EstimateQueue(queue);
        int passed = 0, failed = 0;
        _started = DateTime.Now;
        _clock.Start();
        try
        {
            try
            {
                Log.StartFile(Path.Combine(Workspace.Locate(workspace).Join("analysis/gui_logs"), $"convert-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log"));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                Log.Append($"The log is not saved to disk: {error.Message}");
            }
            if (width > 1)
                Log.Append($"Converting up to {width} at once (Multi-conversion); each conversion runs its steps side by side (Parallel).");
            var running = new Dictionary<Task<bool>, Job>();
            int next = 0;
            while (true)
            {
                while (!_stop && next < queue.Length && running.Count < width && (running.Count == 0 || SystemMemory.Available >= MemoryPerJob))
                {
                    Job job = queue[next];
                    next++;
                    if (_dropped.Remove(job))
                        continue;
                    running[RunJob(job, workspace, width > 1)] = job;
                }
                if (running.Count == 0)
                    break;
                await Task.WhenAny(running.Keys);
                foreach (Task<bool> finished in running.Keys.Where(task => task.IsCompleted).ToList())
                {
                    Job job = running[finished];
                    running.Remove(finished);
                    if (finished.Result)
                        passed++;
                    else if (job.State == RunStates.Failed)
                        failed++;
                    freed += await CleanUpAfter(job, queue);
                }
                if (running.Count > 0 && running.Count < width && next < queue.Length && !_stop && SystemMemory.Available < MemoryPerJob)
                    await WaitForMemory(running.Keys.ToList());
            }
            foreach (Job job in queue.Where(j => j.State == RunStates.Queued))
            {
                job.State = RunStates.Ready;
                job.StatusText = "Not run";
            }
            _clock.Stop();
            if (Settings.DeleteCache && !_stop)
                freed += await ClearCacheAfterRun();
            int dropped = _stop ? 0 : queue.Count(job => job.State == RunStates.Cancelled);
            string summary = $"{passed} converted" + (failed > 0 ? $" · {failed} failed" : "") + (dropped > 0 ? $" · {dropped} cancelled" : "");
            SetStatus(_stop ? "Cancelled" : failed > 0 ? "Finished with problems" : "Finished",
                $"{summary} · {Elapsed(DateTime.Now - _started)}" + (freed > 0 ? $" · {Job.SizeText(freed)} freed" : ""));
            Log.Append(_stop ? $"Cancelled · {summary}" : summary);
            if (!_stop)
                Progress.Value = 1;
            PercentText.Text = "";
            ShowTotals();
        }
        finally
        {
            Log.StopFile();
            _clock.Stop();
            _active.Clear();
            SetBusy(false);
        }
        await SendFinished(queue);
    }

    private async Task SendFinished(IReadOnlyList<Job> queue)
    {
        if (_stop || !Settings.SendToConsole)
            return;
        List<Shipment> shipments = Shipments(queue.Where(job => job.State == RunStates.Done), false);
        List<Shipment> there = await AlreadyThere(shipments);
        bool sent = await SendToConsole([.. shipments.Except(there)], true);
        if (there.Count == 0 || !sent || _closeWhenIdle)
            return;
        if (MessageDialog.Overwrite(this, [.. there.Select(shipment => shipment.Title)]))
            await SendToConsole(there, true);
        else
            Log.Append($"Not sent, already on the console: {string.Join(", ", there.Select(shipment => shipment.Title))}");
    }

    private async Task<List<Shipment>> AlreadyThere(IReadOnlyList<Shipment> shipments)
    {
        var there = new List<Shipment>();
        if (shipments.Count == 0 || !ConsoleFtp.TryAddress(Settings.ConsoleAddress, out string host, out int port))
            return there;
        try
        {
            foreach (Shipment shipment in shipments)
            {
                if (await Task.Run(() => ConsoleFtp.PresentAsync(host, port, shipment.Local, shipment.Remote, CancellationToken.None)) > 0)
                    there.Add(shipment);
            }
        }
        catch (IOException)
        {
            there.Clear();
        }
        return there;
    }

    private async Task<long> CleanUpAfter(Job job, Job[] queue)
    {
        if (_stop)
            return 0;
        long freed = 0;
        try
        {
            freed += await ClearCacheOf(job);
            freed += await DeleteOriginalsOf(job, queue);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            Log.Append($"Cleaning up after {job.Title} failed: {error.Message}");
        }
        return freed;
    }

    private async Task<long> ClearCacheOf(Job job)
    {
        if (!Settings.DeleteCache || Edition.CacheFolder(job) is not { } cache || !Directory.Exists(cache))
            return 0;
        Removal cleared = await Task.Run(() => Cleanup.ClearFolders([cache]));
        foreach (string problem in cleared.Problems)
            Log.Append($"Could not clear the cache of {problem}");
        return cleared.Bytes;
    }

    private async Task<long> DeleteOriginalsOf(Job job, Job[] queue)
    {
        if (!Settings.DeleteOriginals)
            return 0;
        Job[] together = [.. queue.Where(other => string.Equals(other.SourceFolder, job.SourceFolder, StringComparison.OrdinalIgnoreCase))];
        if (together.Any(other => other.State is RunStates.Queued or RunStates.Running))
            return 0;
        string names = string.Join(", ", together.Select(other => other.Title));
        if (together.FirstOrDefault(other => other.State != RunStates.Done) is { } unfinished)
        {
            if (together.Length > 1)
                Log.Append($"Kept the PC originals of {names}: they share a folder and {unfinished.Title} did not convert.");
            return 0;
        }
        string[] outputs = [.. together.Select(Settings.OutputFor)];
        Removal removal = await Task.Run(() => Cleanup.DeleteOriginals(together, Edition, Settings.GameFolder, outputs));
        if (removal.Kept != null)
        {
            Log.Append($"Kept the PC originals of {names}: {removal.Kept}.");
            return 0;
        }
        foreach (string problem in removal.Problems)
            Log.Append($"Could not delete a PC original of {names}: {problem}");
        if (removal.Problems.Count > 0)
            LogToggle.IsChecked = true;
        if (removal.Files == 0)
            return 0;
        foreach (Job other in together)
            other.OriginalDeleted = true;
        Log.Append($"Deleted the PC originals of {names}: {removal.Files} file{(removal.Files == 1 ? "" : "s")}, {Job.SizeText(removal.Bytes)}.");
        return removal.Bytes;
    }

    private async Task<long> ClearCacheAfterRun()
    {
        SetStatus("Clearing the cache", "");
        try
        {
            Removal cleared = await Task.Run(() => Cleanup.ClearFolders(Edition.CacheFolders()));
            foreach (string problem in cleared.Problems)
                Log.Append($"Could not clear {problem}");
            if (cleared.Bytes > 0)
                Log.Append($"Cleared the cache: {Job.SizeText(cleared.Bytes)}.");
            return cleared.Bytes;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            Log.Append($"Could not clear the cache: {error.Message}");
            return 0;
        }
    }

    private async Task WaitForMemory(List<Task<bool>> running)
    {
        Task<Task<bool>> any = Task.WhenAny(running);
        while (!any.IsCompleted && !_stop && SystemMemory.Available < MemoryPerJob)
            await Task.WhenAny(any, Task.Delay(TimeSpan.FromSeconds(5)));
    }

    private async Task<bool> RunJob(Job job, string workspace, bool many)
    {
        var backend = new BackendProcess();
        _backends.Add(backend);
        _converting[job] = backend;
        _active.Add(job);
        Community.Converting(Settings.OutputFor(job));
        job.OnConsole = false;
        job.State = RunStates.Running;
        job.StatusText = "Converting";
        job.StageText = "Starting";
        job.StartedAt = DateTime.UtcNow;
        job.Report = new FidelityView();
        if (!many || JobList.SelectedItems.Count == 0)
        {
            if (job.Group is { IsExpanded: false } group)
                group.IsExpanded = true;
            JobList.SelectedItem = job;
            JobList.ScrollIntoView(job);
        }
        _stage = $"Converting {job.Title}";
        _detail = "starting";
        ShowProgress();
        string prefix = many ? $"[{job.Title}] " : "";
        Log.Append($"\n[{DateTime.Now:T}] {job.GameName} {job.Kind.ToLowerInvariant()} {job.Title}: {job.MainFile}");

        var errors = new List<string>();
        int code;
        try
        {
            code = await backend.RunAsync(Command(job, workspace, _width), workspace, line => OnOutput(job, line, prefix), line =>
            {
                errors.Add(line);
                Log.Append(prefix + line);
            });
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            code = -1;
            errors.Add(error.Message);
            Log.Append(prefix + error.Message);
        }
        finally
        {
            _backends.Remove(backend);
            _converting.Remove(job);
            _active.Remove(job);
            job.IsPaused = false;
            job.RemainingText = "";
            _done++;
        }
        bool converted = Finish(job, code, errors);
        ShowProgress();
        return converted;
    }

    private void OnOutput(Job job, string line, string prefix)
    {
        if (FidelityProtocol.TryParse(line, out FidelitySnapshot? snapshot) && snapshot != null)
        {
            job.Report ??= new FidelityView();
            job.Report.Update(snapshot);
            job.Progress = snapshot.Progress;
            job.StageText = snapshot.Stage;
            if (_active.Count <= 1)
            {
                _stage = snapshot.Stage;
                _detail = snapshot.Detail;
            }
            ShowProgress();
            return;
        }
        Log.Append(prefix + line);
    }

    private bool Finish(Job job, int code, List<string> errors)
    {
        if (_dropped.Remove(job) || _stop)
        {
            job.State = RunStates.Cancelled;
            job.StatusText = "Cancelled";
            if (job.Report is { IsRunning: true } report)
                report.Interrupt(FidelityStates.Cancelled, "Cancelled before the conversion finished. The output folder may hold a partial conversion.");
            return false;
        }
        if (code == 0)
        {
            job.State = RunStates.Done;
            job.StatusText = "Converted";
            job.Progress = 1;
            return true;
        }
        job.State = RunStates.Failed;
        IEnumerable<string> reasons = errors.Where(e => !string.IsNullOrWhiteSpace(e)).TakeLast(3);
        job.StatusText = errors.Any(e => e.Contains("needs parts this converter cannot port yet", StringComparison.Ordinal)) ? "Not supported yet" : $"Failed ({code})";
        LogToggle.IsChecked = true;
        if (job.Report == null || job.Report.IsRunning)
        {
            job.Report ??= new FidelityView();
            job.Report.Interrupt(FidelityStates.Failed, "The converter stopped before it finished. Its last lines are below; the log has the rest.", reasons);
        }
        return false;
    }

    private void ShowProgress()
    {
        if (!_running)
            return;
        foreach (Job job in _active)
        {
            job.Report?.Tick();
            job.RemainingText = !job.IsPaused && job.Remaining is double seconds ? FidelityView.Remaining(seconds) : "";
        }
        double overall = _count == 0 ? 0 : Math.Clamp((_done + _active.Sum(job => job.Progress)) / _count, 0, 1);
        if (_active.Count > 1)
        {
            int converted = _jobs.Count(job => job.State == RunStates.Done);
            int failed = _jobs.Count(job => job.State == RunStates.Failed);
            int waiting = _jobs.Count(job => job.State == RunStates.Queued);
            var counts = new List<string> { $"{converted} of {_count} converted" };
            if (failed > 0)
                counts.Add($"{failed} failed");
            if (waiting > 0)
                counts.Add($"{waiting} waiting");
            if (_active.Count(job => job.IsPaused) is > 0 and int paused)
                counts.Add($"{paused} paused");
            SetStatus($"Converting {_active.Count} at once", string.Join(" · ", counts));
        }
        else
        {
            Job? current = _active.FirstOrDefault();
            string position = _count > 1 ? $"{Math.Min(_done + 1, _count)} of {_count} · " : "";
            SetStatus(current is { IsPaused: true } ? "Paused" : _stage.Length > 0 ? _stage : "Converting", $"{position}{current?.Title}{(_detail.Length > 0 ? " · " + _detail : "")}");
        }
        Progress.Value = overall;
        PercentText.Text = $"{overall:P0} · {Elapsed(DateTime.Now - _started)}" + (QueueRemaining(_active, _jobs, _width) is double left ? $" · {FidelityView.Remaining(left)}" : "");
    }

    private void EstimateQueue(Job[] queue)
    {
        Edition edition = Edition;
        var speeds = queue.ToDictionary(job => job, job => new ConvertSpeed(Settings.OutputFor(job), Settings.Async, _width, Settings.MultiCore, Settings.GpuVideo));
        _ = Task.Run(() =>
        {
            foreach (Job job in queue)
            {
                double? seconds = edition.EstimateSeconds(job, speeds[job]);
                Dispatcher.BeginInvoke(() => job.Estimate = seconds);
            }
        });
    }

    public static double? QueueRemaining(IEnumerable<Job> active, IEnumerable<Job> all, int width)
    {
        var slots = new List<double>();
        foreach (Job job in active)
        {
            if (job.Remaining is not double left)
                return null;
            slots.Add(left);
        }
        if (slots.Count == 0)
            return null;
        while (slots.Count < width)
            slots.Add(0);
        foreach (Job job in all.Where(job => job.State == RunStates.Queued))
        {
            if (job.Estimate is not double whole)
                return null;
            slots[slots.IndexOf(slots.Min())] += whole;
        }
        return slots.Max();
    }

    private void SetStatus(string status, string detail)
    {
        _idle = false;
        StatusText.Text = status;
        DetailText.Text = detail.Length > 0 ? "   " + detail : "";
        StatusLine.ToolTip = detail.Length > 0 ? $"{status} · {detail}" : status;
        if (!_running)
            PercentText.Text = "";
        Progress.Visibility = _running || Progress.Value > 0 ? Visibility.Visible : Visibility.Hidden;
    }

    private void SetBusy(bool busy)
    {
        _running = busy;
        AddButton.IsEnabled = InstalledButton.IsEnabled = OutputButton.IsEnabled = GameFolderButton.IsEnabled = SettingsButton.IsEnabled = !busy;
        ClearCacheButton.IsEnabled = !busy && !_clearing;
        UpdateButton.IsEnabled = !busy;
        RemoveButton.IsEnabled = ClearButton.IsEnabled = !busy && _jobs.Count > 0;
        ConvertButton.IsEnabled = CanConvert;
        ShowActions();
        LockConverted();
        if (busy)
            return;
        _ = MeasureCache();
        Converted.Refresh();
    }

    private void CancelClick(object sender, RoutedEventArgs e) => StopRun();

    private void StopRun()
    {
        if (!_running)
            return;
        if (_sending is { } sending)
        {
            sending.Cancel();
            SetStatus("Stopping once this file is sent", _closeWhenIdle ? "the app closes as soon as it is done" : "");
            return;
        }
        _stop = true;
        foreach (BackendProcess backend in _backends.ToList())
            backend.Cancel();
        SetStatus("Cancelling", _backends.Count > 1 ? $"waiting for {_backends.Count} conversions to stop" : "waiting for the converter to stop");
    }

    private sealed record Shipment(string Title, string Local, string Remote, IReadOnlyList<Job> Jobs, IReadOnlyList<string>? Last = null);

    private List<Shipment> Shipments(IEnumerable<Job> jobs, bool again)
    {
        var found = new List<Shipment>();
        foreach (Job job in jobs)
        {
            if (Edition.ConsoleFolder(job) is not { } folder)
                continue;
            string remote = $"{Settings.ConsoleRoot}/{folder}";
            if (found.Any(shipment => shipment.Remote == remote))
                continue;
            Job[] together = job.Mod == null ? [job] : [.. _jobs.Where(other => string.Equals(other.Mod, job.Mod, StringComparison.OrdinalIgnoreCase))];
            if (together.All(other => other.IsConverted) && (again || together.Any(other => !other.OnConsole)))
                found.Add(new Shipment(job.Mod ?? job.Name, Settings.OutputFor(job), remote, together));
        }
        return found;
    }

    private List<Shipment> Unsent() => Shipments(JobList.SelectedItems.Count > 1 ? JobList.SelectedItems.Cast<Job>() : _jobs, false);

    private void ShowSend()
    {
        List<Shipment> waiting = !_running && ConvertTab.IsChecked == true && ConsoleFtp.TryAddress(Settings.ConsoleAddress, out _, out _) ? Unsent() : [];
        SendButton.Visibility = waiting.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SendText.Text = waiting.Count > 1 ? $"Send {waiting.Count} to console" : "Send to console";
        SendButton.ToolTip = waiting.Count == 0 ? null
            : $"Send {string.Join(", ", waiting.Select(shipment => shipment.Title))} to {Settings.ConsoleRoot} on the console at {Settings.ConsoleAddress}.";
    }

    private async void SendClick(object sender, RoutedEventArgs e) => await SendToConsole(Unsent());

    private async Task<bool> SendToConsole(IReadOnlyList<Shipment> shipments, bool converted = false)
    {
        if (shipments.Count == 0)
            return true;
        if (_running || !ConsoleFtp.TryAddress(Settings.ConsoleAddress, out string host, out int port))
            return false;
        using var stop = new CancellationTokenSource();
        var gate = new PauseGate();
        int sent = 0;
        string? problem = null;
        _sending = stop;
        _started = DateTime.Now;
        SetBusy(true);
        Progress.Value = 0;
        SetStatus("Connecting to the console", $"{host}:{port}");
        try
        {
            for (int index = 0; index < shipments.Count && problem == null && !stop.IsCancellationRequested; index++)
            {
                Shipment shipment = shipments[index];
                double from = (double)index / shipments.Count;
                string position = shipments.Count > 1 ? $"{index + 1} of {shipments.Count} · " : "";
                var progress = new Progress<DownloadProgress>(report =>
                {
                    if (!ReferenceEquals(_sending, stop))
                        return;
                    Progress.Value = from + report.Fraction / shipments.Count;
                    PercentText.Text = $"{Progress.Value:P0} · {Elapsed(DateTime.Now - _started)}";
                    SetStatus(stop.IsCancellationRequested ? "Stopping once this file is sent" : $"Sending {shipment.Title} to the console",
                        position + (report.Detail.Length > 0 ? report.Detail : report.Stage.ToLowerInvariant()));
                });
                Log.Append($"\n[{DateTime.Now:T}] Sending {shipment.Title} to the console at {host}:{port}, into {shipment.Remote}");
                try
                {
                    if (!Directory.Exists(shipment.Local))
                        throw new IOException($"its converted files are no longer in {shipment.Local}");
                    string[] last = [.. shipment.Last ?? shipment.Jobs.Select(job => job.Name + ".ff")];
                    await Task.Run(() => ConsoleFtp.SendAsync(host, port, shipment.Local, shipment.Remote, last, _staged, progress, Post, gate, stop.Token));
                    foreach (Job job in shipment.Jobs)
                        job.OnConsole = true;
                    sent++;
                    Log.Append($"{shipment.Title} is on the console in {shipment.Remote}");
                }
                catch (OperationCanceledException)
                {
                    Log.Append($"{shipment.Title}: sending to the console was stopped; what is already on the console was left as it was");
                }
                catch (IOException error)
                {
                    problem = $"{shipment.Title}: {error.Message}";
                    Log.Append($"{shipment.Title} was not sent to the console: {error.Message}");
                }
            }
        }
        finally
        {
            _sending = null;
            SetBusy(false);
        }
        bool stopped = stop.IsCancellationRequested;
        Progress.Value = problem == null && !stopped ? 1 : 0;
        string count = shipments.Count == 1 ? shipments[0].Title : sent == shipments.Count ? $"all {sent}" : $"{sent} of {shipments.Count}";
        if (problem != null)
            SetStatus(converted ? "Converted, but not sent to the console" : "Not sent to the console", problem);
        else if (stopped)
            SetStatus(converted ? "Converted, but sending was stopped" : "Stopped sending", sent > 0 ? $"{count} sent" : "what is already on the console was left as it was");
        else
            SetStatus(converted ? "Converted and sent to the console" : "Sent to the console", $"{count} · {Elapsed(DateTime.Now - _started)}");
        if (problem != null)
            LogToggle.IsChecked = true;
        Community.SettingsChanged();
        if (_closeWhenIdle)
            Close();
        else
            OnConsole.Changed();
        return problem == null && !stopped;
    }

    private void Post(string line) => Dispatcher.InvokeAsync(() => Log.Append(line));

    private void ShowSettings()
    {
        static string State(bool on) => on ? "on" : "off";
        SettingsButton.ToolTip = "Speed, clean-up and console options.\n\n"
            + $"Parallel: {State(Settings.Async)}\nMulti-conversion: {State(Settings.MultiConversion)}\nMulti-core: {State(Settings.MultiCore)}\n"
            + $"Delete PC originals: {State(Settings.DeleteOriginals)}\nDelete the cache: {State(Settings.DeleteCache)}\n"
            + $"Console: {(Settings.ConsoleAddress == null ? "not set" : $"{Settings.ConsoleAddress}, {ConsoleFtp.StorageName(Settings.ConsoleStorage)}")}\n"
            + $"Auto-FTP: {State(Settings.SendToConsole)}";
    }

    private async void SettingsClick(object sender, RoutedEventArgs e)
    {
        if (_running)
            return;
        UpdateFile? found = null;
        async Task<bool> Check()
        {
            found = await FindUpdate();
            return found != null;
        }
        string? address = Settings.ConsoleAddress;
        string console = $"{address}{Settings.ConsoleRoot}";
        new SettingsWindow(this, _exe != null && Edition.UpdateSource != null ? Check : null, Community).ShowDialog();
        if (console != $"{Settings.ConsoleAddress}{Settings.ConsoleRoot}")
        {
            foreach (Job job in _jobs)
                job.OnConsole = false;
        }
        ShowSettings();
        RefreshQueue();
        Community.SettingsChanged();
        if (address != Settings.ConsoleAddress)
            OnConsole.Changed();
        if (console != $"{Settings.ConsoleAddress}{Settings.ConsoleRoot}")
            Converted.ConsoleChanged();
        if (found != null)
            await OfferUpdate(found);
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (_leaving)
            return;
        if (_updating is { } update)
        {
            e.Cancel = true;
            _closeAfterUpdate = true;
            update.Cancel();
            return;
        }
        if (_running)
        {
            e.Cancel = true;
            _closeWhenIdle |= _sending != null;
            StopRun();
            return;
        }
        bool working = Community.IsDownloading, sending = Community.IsSending, removing = OnConsole.IsRemoving, deleting = Converted.IsDeleting;
        Community.CancelAll();
        OnConsole.CancelAll();
        if (!working && !removing && deleting)
        {
            e.Cancel = true;
            _closeWhenIdle = true;
            SetStatus("Finishing the delete", "the app closes as soon as it is done");
            return;
        }
        if (!working && !removing)
        {
            if (!ConsoleFtp.Busy)
                return;
            e.Cancel = true;
            _ = CloseWhenFree();
            return;
        }
        e.Cancel = true;
        _closeWhenIdle = true;
        if (sending)
            SetStatus("Finishing the file being sent to the console", "the app closes as soon as it is done");
        else if (removing)
            SetStatus("Finishing the removal on the console", "the app closes as soon as it is done");
        else
            _ = CloseAnyway();
    }

    private async Task CloseWhenFree()
    {
        await Task.WhenAny(ConsoleFtp.IdleAsync(), Task.Delay(TimeSpan.FromSeconds(5)));
        if (!IsLoaded)
            return;
        _leaving = true;
        Close();
    }

    private async Task<string?> LostDrive()
    {
        if (Settings.ConsoleStorage is not { } drive || !ConsoleFtp.TryAddress(Settings.ConsoleAddress, out string host, out int port))
            return null;
        try
        {
            IReadOnlyList<string> usable = await Task.Run(() => ConsoleFtp.UsableAsync(host, port, [drive], true, CancellationToken.None));
            if (usable.Contains(drive) || Settings.ConsoleStorage != drive)
                return null;
        }
        catch (IOException)
        {
            return null;
        }
        Settings.SetConsoleStorage(null);
        foreach (Job job in _jobs)
            job.OnConsole = false;
        Log.Append($"{ConsoleFtp.StorageName(drive)} could not be written to, so maps go to {ConsoleFtp.Root} again.");
        ShowSettings();
        RefreshQueue();
        Community.SettingsChanged();
        return drive;
    }

    private void WarnLostDrive(string? drive)
    {
        if (drive == null)
            return;
        string name = ConsoleFtp.StorageName(drive);
        if (!Settings.SendToConsole)
        {
            MessageDialog.Inform(this, DialogKind.Warning, $"{name} is not usable", "A test file could not be written. Now using console storage.");
            return;
        }
        if (MessageDialog.Confirm(this, DialogKind.Warning, $"{name} is not usable", "A test file could not be written. Keep Auto-FTP on?",
            "Use console storage", "Turn off Auto-FTP"))
        {
            return;
        }
        Settings.SetSendToConsole(false);
        ShowSettings();
        RefreshQueue();
    }

    private async Task CloseAnyway()
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        if (!IsLoaded)
            return;
        _leaving = true;
        Close();
    }


    private void ShowGameFolder()
    {
        string? folder = Settings.GameFolder;
        GameFolderLabel.Text = "Game files";
        GameFolderText.Text = folder == null ? "not set" : ShortPath(folder);
        GameFolderButton.ToolTip = folder == null
            ? $"Set the {Edition.GameFolderName}. {Edition.GameFolderHint}"
            : $"{folder}\n\nClick to choose another folder.";
    }

    private void AskForGameFolder()
    {
        if (!Settings.FirstRun || Settings.GameFolder != null)
            return;
        bool choose = MessageDialog.Confirm(this, DialogKind.Info, $"Choose your {Edition.GameName} files", Edition.GameFolderHint, "Choose folder", "Not now");
        if (choose)
            ChooseGameFolder();
        else
            SetStatus("Game files not set", "use the Game files button");
    }

    private async Task PrepareTools()
    {
        Task work = Task.Run(() =>
        {
            Ps4Sdk.EnsureFiles();
            Dispatcher.Invoke(() => PrepareDetail.Text = "Extracting files…");
            ToolData.Extract();
            Dispatcher.Invoke(() => PrepareDetail.Text = "Setting up tools…");
            Edition.PrepareTools(line => Dispatcher.Invoke(() =>
            {
                Log.Append(line);
                PrepareDetail.Text = line;
            }));
        });
        if (_firstRun || await Task.WhenAny(work, Task.Delay(400)) != work)
            PrepareOverlay.Visibility = Visibility.Visible;
        try
        {
            await work;
        }
        catch (Exception error)
        {
            Log.Append($"Preparing files failed: {error.Message}");
        }
        PrepareOverlay.Visibility = Visibility.Collapsed;
    }

    private bool IsOccupied => _running || _clearing || _updating != null || Community.IsDownloading || Community.IsSending || OnConsole.IsRemoving || Converted.IsDeleting
        || OwnedWindows.Count > 0;

    private static string Tagged(UpdateFile update) => update.Release.Length > 0 ? $" ({update.Release})" : "";

    private async Task<UpdateFile?> FindUpdate()
    {
        if (_exe is not { } exe || Edition.UpdateSource is not { } source)
            return null;
        try
        {
            await AppUpdate.CleanUpAsync(exe);
            UpdateCheck check = await AppUpdate.CheckAsync(AppUpdate.Releases(source), exe, AppUpdate.Name, AppUpdate.Built, CancellationToken.None);
            if (check is { State: UpdateStates.Ahead, File: { } release })
                Log.Append($"This build is newer than the latest release{Tagged(release)}, so it is not replaced.");
            if (check is { State: UpdateStates.Doubtful, File: { } small })
                Log.Append($"The exe in the latest release{Tagged(small)} is far too small to be the app ({Job.SizeText(small.Size)}), so it is not installed.");
            AppUpdate.Summary = check switch
            {
                { State: UpdateStates.Current, File: { } file } => $"Up to date{Tagged(file)}",
                { State: UpdateStates.Ahead, File: { } file } => file.Release.Length > 0 ? $"Newer than {file.Release}" : "Newer than the latest release",
                { State: UpdateStates.Available, File: { } file } => file.Release.Length > 0 ? $"{file.Release} is out" : "A newer version is out",
                { State: UpdateStates.Doubtful } => "The latest release was skipped",
                _ => "No release to compare with",
            };
            return check.State == UpdateStates.Available ? check.File : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Append($"Checking for a newer version failed: {error.Message.TrimEnd('.')}.");
            AppUpdate.Summary = "Could not check for updates";
            return null;
        }
    }

    public void UpdateFailed()
    {
        _updateFailed = true;
        Log.Append("The update was not installed: something was still using the app.");
        SetStatus("The update was not installed", "close the other windows, then press Update");
    }

    private async Task OfferUpdate(UpdateFile update)
    {
        _update = update;
        Log.Append($"A newer version is out{Tagged(update)}: {update.Name}, {Job.SizeText(update.Size)}.");
        bool writable = _exe is { } exe && AppUpdate.Writable(exe);
        if (!writable)
            Log.Append("The app's folder cannot be written to, so the update waits for the Update button.");
        if (writable && !_updateFailed && !IsOccupied)
            await InstallUpdate();
        else
            UpdateButton.Visibility = Visibility.Visible;
    }

    private async Task InstallUpdate()
    {
        if (_update is not { } update || _exe is not { } exe || IsOccupied)
            return;
        if (AppUpdate.Others(exe) > 0)
        {
            UpdateButton.Visibility = Visibility.Visible;
            SetStatus("The update waits for the other windows", "close them, then press Update");
            return;
        }
        using var stop = new CancellationTokenSource();
        _updating = stop;
        UpdateButton.Visibility = Visibility.Collapsed;
        UpdateHeading.Text = "Installing an update";
        UpdateBody.Text = $"{(update.Release.Length > 0 ? update.Release : "A newer version")} is downloading. The app restarts by itself.";
        UpdateBody.ToolTip = null;
        UpdateNotes.Text = update.Notes;
        UpdateNotesBox.Visibility = update.Notes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateDetail.Text = "Starting the download…";
        UpdateProgress.Value = 0;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateCancel.Content = "Not now";
        UpdateCancel.IsEnabled = true;
        UpdateOverlay.Visibility = Visibility.Visible;
        UpdateCancel.Focus();
        try
        {
            var progress = new Progress<DownloadProgress>(report =>
            {
                UpdateProgress.Value = report.Fraction;
                UpdateDetail.Text = report.Stage == "Checking" ? "Checking the download…" : report.Detail;
            });
            await Task.Run(() => AppUpdate.DownloadAsync(update, exe, progress, stop.Token));
            UpdateCancel.IsEnabled = false;
            UpdateDetail.Text = "Restarting…";
            string[] queued = [.. _jobs.Where(job => !job.OriginalDeleted).Select(job => job.MainFile)];
            await Task.Run(() => AppUpdate.HandOver(exe, queued));
            _leaving = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            UpdateOverlay.Visibility = Visibility.Collapsed;
            UpdateButton.Visibility = Visibility.Visible;
            SetStatus("Update postponed", "press Update to install it");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or Win32Exception)
        {
            string reason = error.Message.TrimEnd('.');
            Log.Append($"The update was not installed: {reason}.");
            UpdateHeading.Text = "The update was not installed";
            UpdateBody.Text = $"{char.ToUpperInvariant(reason[0])}{reason[1..]}.";
            UpdateBody.ToolTip = UpdateBody.Text;
            UpdateDetail.Text = "The Update button tries again.";
            UpdateNotesBox.Visibility = Visibility.Collapsed;
            UpdateProgress.Visibility = Visibility.Collapsed;
            UpdateCancel.Content = "Close";
            UpdateCancel.IsEnabled = true;
            UpdateButton.Visibility = Visibility.Visible;
        }
        finally
        {
            _updating = null;
            if (_closeAfterUpdate && !_leaving)
                Close();
        }
    }

    private async void UpdateClick(object sender, RoutedEventArgs e)
    {
        if (Community.IsDownloading || Community.IsSending || OnConsole.IsRemoving)
            SetStatus("The update waits for the work in progress", "press Update again once it is done");
        else
            await InstallUpdate();
    }

    private void UpdateCancelClick(object sender, RoutedEventArgs e)
    {
        if (_updating is { } update)
            update.Cancel();
        else
            UpdateOverlay.Visibility = Visibility.Collapsed;
    }

    private void ChooseGameFolderClick(object sender, RoutedEventArgs e) => ChooseGameFolder();

    private void ChooseGameFolder()
    {
        if (_running)
            return;
        while (true)
        {
            var dialog = new OpenFolderDialog { Title = $"Choose the {Edition.GameFolderName}" };
            if (Settings.GameFolder is { } current && Directory.Exists(current))
                dialog.InitialDirectory = current;
            if (dialog.ShowDialog(this) != true)
                return;
            if (Edition.TryGameFolder(dialog.FolderName, out string? folder) && folder != null)
            {
                Settings.SetGameFolder(folder);
                ShowGameFolder();
                SetStatus("Game files set", folder);
                return;
            }
            if (!MessageDialog.Confirm(this, DialogKind.Warning, $"That folder is not the {Edition.GameFolderName}",
                $"{dialog.FolderName}\n\n{Edition.GameFolderHint}", "Try again"))
            {
                return;
            }
        }
    }

    private void ShowOutput()
    {
        OutputText.Text = ShortPath(Settings.Output);
        OutputButton.ToolTip = $"Maps go to {Path.Combine(Settings.Output, "usermaps")}, mods to {Path.Combine(Settings.Output, "mods")}. "
            + (Settings.OutputOverride == null ? "Click to pick another folder." : $"Click to pick another folder; right-click to go back to {AppSettings.DefaultOutput}.");
        DefaultOutputItem.IsEnabled = Settings.OutputOverride != null;
        DefaultOutputItem.Header = $"Use the default folder ({AppSettings.DefaultOutput})";
    }

    internal static string ShortPath(string path)
    {
        string[] parts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return path.Length <= 48 || parts.Length <= 3 ? path : $"…{Path.DirectorySeparatorChar}{string.Join(Path.DirectorySeparatorChar, parts[^2..])}";
    }

    private void ChooseOutputClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Where converted files go (usermaps and mods folders are made inside)" };
        if (Directory.Exists(Settings.Output))
            dialog.InitialDirectory = Settings.Output;
        if (dialog.ShowDialog(this) != true)
            return;
        Settings.SetOutput(dialog.FolderName);
        ShowOutput();
        ReloadEarlierReports();
        Community.OutputChanged();
        Converted.Refresh();
    }

    private void ReloadEarlierReports()
    {
        foreach (Job job in _jobs.Where(j => j.State == RunStates.Ready))
        {
            job.Report = null;
            job.StatusText = "Ready";
            job.OnConsole = false;
            LoadPreviousReport(job);
        }
        ShowSend();
    }

    private void DefaultOutputClick(object sender, RoutedEventArgs e)
    {
        if (_running)
            return;
        Settings.SetOutput(null);
        ShowOutput();
        ReloadEarlierReports();
        Community.OutputChanged();
        Converted.Refresh();
    }

    private void OpenOutputClick(object sender, RoutedEventArgs e)
    {
        string folder = Settings.Output;
        if (JobList.SelectedItem is Job job)
        {
            if (Directory.Exists(Settings.OutputFor(job)))
                folder = Settings.OutputFor(job);
            else if (Directory.Exists(Path.Combine(folder, job.Mod == null ? "usermaps" : "mods")))
                folder = Path.Combine(folder, job.Mod == null ? "usermaps" : "mods");
        }
        if (Directory.Exists(folder))
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        else if (!_running)
            SetStatus("Nothing converted yet", $"{folder} is created by the first conversion");
    }

    private void LogToggled(object sender, RoutedEventArgs e) => Log.Visibility = LogToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void CacheTipOpening(object sender, ToolTipEventArgs e) => _ = MeasureCache();

    private async Task MeasureCache()
    {
        if (_measuring)
            return;
        _measuring = true;
        try
        {
            _cacheSize = await Task.Run(() => Edition.CacheFolders().Sum(Cleanup.FolderSize));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            _measuring = false;
            ShowCache();
        }
    }

    private void ShowCache() =>
        CacheSizeText.Text = _cacheSize < 0 ? "Measuring the cache…" : _cacheSize == 0 ? "The cache is empty" : $"{Job.SizeText(_cacheSize)} in the cache";

    private async void ClearCacheClick(object sender, RoutedEventArgs e)
    {
        if (_running || _clearing)
            return;
        SetClearing(true);
        try
        {
            (IReadOnlyList<string> folders, long size) = await Task.Run(() =>
            {
                IReadOnlyList<string> found = Edition.CacheFolders();
                return (found, found.Sum(Cleanup.FolderSize));
            });
            _cacheSize = size;
            ShowCache();
            if (size == 0)
                SetStatus("The cache is already empty", "");
            else if (MessageDialog.Confirm(this, DialogKind.Warning, "Clear the tool cache?", $"{Job.SizeText(size)} of saved work is deleted. Converted files are kept.", "Clear cache", danger: true))
                await ClearCache(folders, size);
        }
        finally
        {
            SetClearing(false);
            _ = MeasureCache();
        }
    }

    private async Task ClearCache(IReadOnlyList<string> folders, long size)
    {
        SetStatus("Clearing the cache", Job.SizeText(size));
        try
        {
            Removal cleared = await Task.Run(() => Cleanup.ClearFolders(folders));
            if (cleared.Problems.Count == 0)
            {
                SetStatus("Cache cleared", $"{Job.SizeText(cleared.Bytes)} freed");
                return;
            }
            foreach (string problem in cleared.Problems)
                Log.Append($"Could not clear {problem}");
            LogToggle.IsChecked = true;
            SetStatus("Cache partly cleared", $"{cleared.Problems.Count} folder{(cleared.Problems.Count == 1 ? "" : "s")} could not be deleted; the log says why");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not clear the cache", error.Message);
        }
    }

    private void SetClearing(bool clearing)
    {
        _clearing = clearing;
        ClearCacheButton.IsEnabled = !clearing && !_running;
        ConvertButton.IsEnabled = CanConvert;
    }

    private static string Elapsed(TimeSpan time) =>
        time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes:00}m" : time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes}m {time.Seconds:00}s" : $"{time.Seconds}s";
}
