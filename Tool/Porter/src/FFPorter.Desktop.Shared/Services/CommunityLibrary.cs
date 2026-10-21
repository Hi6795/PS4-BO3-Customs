using System.IO;
using System.Text.Json;
using FFPorter.Desktop.Models;

namespace FFPorter.Desktop.Services;

public sealed class CommunityLibrary
{
    private readonly string _path;
    private readonly Dictionary<string, string> _copies = new(StringComparer.OrdinalIgnoreCase);

    private CommunityLibrary(string path) => _path = path;

    public static CommunityLibrary Load(string? path = null)
    {
        var library = new CommunityLibrary(path ?? Path.Combine(AppSettings.Folder, $"community.{Edition.Current.Codename}.json"));
        try
        {
            if (File.Exists(library._path) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(library._path)) is { } saved)
            {
                foreach ((string folder, string checksum) in saved)
                    library._copies[folder] = checksum ?? "";
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return library;
    }

    public string Check(CommunityMap map, string destination)
    {
        if (!_copies.TryGetValue(destination, out string? checksum))
            return LocalCopies.None;
        if (!map.MainFiles.Any(file => File.Exists(Path.Combine(destination, file))))
        {
            _copies.Remove(destination);
            Save();
            return LocalCopies.None;
        }
        return map.Stamp.Length == 0 || checksum.Equals(map.Stamp, StringComparison.OrdinalIgnoreCase) ? LocalCopies.Current : LocalCopies.Outdated;
    }

    public bool Knows(string copy) => _copies.ContainsKey(copy);

    public string? Part(string copy) => _copies.TryGetValue(copy, out string? checksum) ? checksum.Split('+')[0] : null;

    public bool Forget(string copy)
    {
        if (!_copies.Remove(copy))
            return false;
        Save();
        return true;
    }

    public void Remember(CommunityMap map, string destination)
    {
        _copies[destination] = map.Stamp;
        Save();
    }

    public bool Sent(CommunityMap map, string target) =>
        _copies.TryGetValue(target, out string? checksum) && checksum.Equals(map.Stamp, StringComparison.OrdinalIgnoreCase);

    public void RememberSent(CommunityMap map, string target)
    {
        _copies[target] = map.Stamp;
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_copies, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
