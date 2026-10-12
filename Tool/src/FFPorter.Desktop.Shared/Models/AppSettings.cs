using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FFPorter.Desktop.Models;

public sealed class AppSettings : Observable
{
    public const string FolderVariable = "FFPORTER_SETTINGS";

    public static string Folder { get; } = Environment.GetEnvironmentVariable(FolderVariable) is { Length: > 0 } chosen
        ? Path.GetFullPath(chosen)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS4 FF Porter");

    private static readonly string LegacyFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "H1FFStudio");

    public static string DefaultOutput { get; } = Path.Combine(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), "exports");

    private static readonly string UiPath = Path.Combine(Folder, Edition.Current.SettingsFile);

    private static readonly string[] LegacyUiPaths =
    [
        Path.Combine(LegacyFolder, Edition.Current.SettingsFile),
        Path.Combine(LegacyFolder, "ui.json"),
    ];
    private static readonly string T7Path = Path.Combine(LegacyFolder, "t7settings.json");

    public static AppSettings Current { get; } = Load();

    private string? _outputOverride;
    private string? _gameFolder;
    private string? _consoleAddress, _consoleStorage;
    private bool _async, _multiConversion, _deleteOriginals, _deleteCache, _sendToConsole;
    private double? _interfaceScale;

    private AppSettings(string workspace) => Workspace = workspace;

    public string Workspace { get; }

    public bool FirstRun { get; private set; }

    public string? GameFolder => _gameFolder;

    public void SetGameFolder(string? folder)
    {
        string? chosen = string.IsNullOrWhiteSpace(folder) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Trim()));
        if (string.Equals(chosen, _gameFolder, StringComparison.OrdinalIgnoreCase))
            return;
        _gameFolder = chosen;
        Raise(nameof(GameFolder));
        Save();
    }

    public bool Async => _async;

    public bool MultiConversion => _async && _multiConversion;

    public bool DeleteOriginals => _deleteOriginals;

    public bool DeleteCache => _deleteCache;

    public void SetAsync(bool value)
    {
        if (_async == value)
            return;
        _async = value;
        Raise(nameof(Async));
        Raise(nameof(MultiConversion));
        Save();
    }

    public void SetMultiConversion(bool value)
    {
        if (_multiConversion == value)
            return;
        _multiConversion = value;
        Raise(nameof(MultiConversion));
        Save();
    }

    public void SetDeleteOriginals(bool value)
    {
        if (_deleteOriginals == value)
            return;
        _deleteOriginals = value;
        Raise(nameof(DeleteOriginals));
        Save();
    }

    public void SetDeleteCache(bool value)
    {
        if (_deleteCache == value)
            return;
        _deleteCache = value;
        Raise(nameof(DeleteCache));
        Save();
    }

    public double? InterfaceScale => _interfaceScale;

    public void SetInterfaceScale(double? scale)
    {
        double? chosen = Scaled(scale);
        if (chosen == _interfaceScale)
            return;
        _interfaceScale = chosen;
        Raise(nameof(InterfaceScale));
        Save();
    }

    private static double? Scaled(double? scale) => scale is double value && double.IsFinite(value) && value > 0
        ? Math.Clamp(value, Theme.ThemeManager.SmallestScale, Theme.ThemeManager.LargestScale)
        : null;

    public string? ConsoleAddress => _consoleAddress;

    public bool SendToConsole => _sendToConsole && _consoleAddress != null;

    public void SetConsoleAddress(string? address)
    {
        string? chosen = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        if (string.Equals(chosen, _consoleAddress, StringComparison.Ordinal))
            return;
        _consoleAddress = chosen;
        Raise(nameof(ConsoleAddress));
        Raise(nameof(SendToConsole));
        Save();
    }

    public string? ConsoleStorage => _consoleStorage;

    public string ConsoleRoot => Services.ConsoleFtp.RootOf(_consoleStorage);

    public void SetConsoleStorage(string? storage)
    {
        string? chosen = Services.ConsoleFtp.IsDrive(storage) ? storage : null;
        if (chosen == _consoleStorage)
            return;
        _consoleStorage = chosen;
        Raise(nameof(ConsoleStorage));
        Raise(nameof(ConsoleRoot));
        Save();
    }

    public void SetSendToConsole(bool value)
    {
        if (_sendToConsole == value)
            return;
        _sendToConsole = value;
        Raise(nameof(SendToConsole));
        Save();
    }

    public string CommunityFolder(CommunityMap map) =>
        map.IsMod ? Path.Combine(Output, Edition.Current.Codename, "mods", map.Folder) : Path.Combine(Output, Edition.Current.Codename, map.Folder);

    public string? OutputOverride => _outputOverride;

    public string Output => _outputOverride ?? DefaultOutput;

    public string OutputFor(Job job) => job.Mod == null ? Path.Combine(Output, job.Codename, job.Name) : Path.Combine(Output, job.Codename, "mods", job.Mod);

    public void SetOutput(string? folder)
    {
        string? chosen = string.IsNullOrWhiteSpace(folder) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Trim()));
        if (chosen != null && string.Equals(chosen, DefaultOutput, StringComparison.OrdinalIgnoreCase))
            chosen = null;
        if (string.Equals(chosen, _outputOverride, StringComparison.OrdinalIgnoreCase))
            return;
        _outputOverride = chosen;
        Raise(nameof(OutputOverride));
        Raise(nameof(Output));
        Save();
    }

    private sealed class UiFile
    {
        public string? Output { get; set; }
        public string? GameFolder { get; set; }
        public bool Async { get; set; }
        [JsonPropertyName("MultiMap")]
        public bool MultiConversion { get; set; }
        public bool DeleteOriginals { get; set; }
        public bool DeleteCache { get; set; }
        public string? ConsoleAddress { get; set; }
        public string? ConsoleStorage { get; set; }
        public bool SendToConsole { get; set; }
        public double? InterfaceScale { get; set; }
    }

    private static AppSettings Load()
    {
        string workspace;
        try
        {
            workspace = FFPorter.Core.Workspace.Locate().Root;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            workspace = AppContext.BaseDirectory;
        }
        var settings = new AppSettings(workspace);
        UiFile? saved = File.Exists(UiPath) ? Read<UiFile>(UiPath)
            : LegacyUiPaths.FirstOrDefault(File.Exists) is { } legacy ? Read<UiFile>(legacy)
            : null;
        settings.FirstRun = !File.Exists(UiPath);
        settings._async = saved?.Async ?? false;
        settings._multiConversion = saved?.MultiConversion ?? false;
        settings._deleteOriginals = saved?.DeleteOriginals ?? false;
        settings._deleteCache = saved?.DeleteCache ?? false;
        settings._consoleAddress = string.IsNullOrWhiteSpace(saved?.ConsoleAddress) ? null : saved.ConsoleAddress.Trim();
        settings._consoleStorage = Services.ConsoleFtp.IsDrive(saved?.ConsoleStorage) ? saved!.ConsoleStorage : null;
        settings._sendToConsole = saved?.SendToConsole ?? false;
        settings._interfaceScale = Scaled(saved?.InterfaceScale);
        string? output = saved?.Output;
        if (saved?.GameFolder is { Length: > 0 } game && Directory.Exists(game))
            settings._gameFolder = Path.TrimEndingDirectorySeparator(game);
        if (output == null && Read<string[]>(T7Path) is { Length: >= 2 } t7 && !string.IsNullOrWhiteSpace(t7[1]) && Directory.Exists(t7[1].Trim()))
        {
            output = Path.TrimEndingDirectorySeparator(t7[1].Trim());
            if (Path.GetFileName(output).Equals(Edition.Current.Codename, StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(output) is { Length: > 0 } parent)
                output = parent;
        }
        try
        {
            string? chosen = string.IsNullOrWhiteSpace(output) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(output.Trim()));
            settings._outputOverride = chosen != null && !string.Equals(chosen, DefaultOutput, StringComparison.OrdinalIgnoreCase) ? chosen : null;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            settings._outputOverride = null;
        }
        return settings;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var file = new UiFile
            {
                Output = _outputOverride, GameFolder = _gameFolder, Async = _async, MultiConversion = _multiConversion,
                DeleteOriginals = _deleteOriginals, DeleteCache = _deleteCache, ConsoleAddress = _consoleAddress,
                ConsoleStorage = _consoleStorage, SendToConsole = _sendToConsole, InterfaceScale = _interfaceScale,
            };
            File.WriteAllText(UiPath, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static T? Read<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : null;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
