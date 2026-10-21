using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FFPorter.Core.Common.Tools;
using FFPorter.Core.T7.Streams;

namespace FFPorter.Core.T7.Port;

public sealed class T7StockImages
{
    private const string Version = "t7-stock-images-1";

    private readonly List<string> _xpaks;
    private readonly string _cache;
    private readonly Action<string> _log;
    private readonly object _gate = new();
    private Dictionary<string, (long Area, int Parts)>? _images;

    public T7StockImages(IEnumerable<string> xpaks, string cacheDirectory, Action<string> log)
    {
        _xpaks = xpaks.Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        _log = log;
        var stamps = new StringBuilder(Version + "\n");
        foreach (string path in _xpaks)
        {
            var info = new FileInfo(path);
            stamps.Append($"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}\n");
        }
        string signature = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stamps.ToString())))[..16];
        _cache = Path.Combine(cacheDirectory, $"stock_images.{signature}.txt");
    }

    public int Count => _xpaks.Count;

    public bool TryGet(string name, out long area, out int parts)
    {
        lock (_gate)
            _images ??= Load();
        bool found = _images.TryGetValue(name, out (long Area, int Parts) image);
        (area, parts) = image;
        return found;
    }

    public static (long Area, int Parts) Pick(IEnumerable<(long Area, int Parts)> perPack) =>
        perPack.GroupBy(p => p).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key.Area).Select(g => g.Key).FirstOrDefault();

    private Dictionary<string, (long Area, int Parts)> Load()
    {
        if (Read() is { } cached)
            return cached;
        if (_xpaks.Count == 0)
            return new Dictionary<string, (long, int)>(StringComparer.Ordinal);
        Directory.CreateDirectory(Path.GetDirectoryName(_cache)!);
        using IDisposable shared = CacheFiles.Lock(_cache);
        if (Read() is { } done)
            return done;
        _log($"reading texture sizes from the game's {_xpaks.Count} texture packs (once per game folder)");
        var perPack = new Dictionary<string, List<(long Area, int Parts)>>(StringComparer.Ordinal);
        foreach (string path in _xpaks)
        {
            try
            {
                using var xpak = new XPak(path);
                var own = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
                foreach (XPakIndexRecord record in xpak.Index.Values)
                {
                    long area = (long)record.TrueWidth * record.TrueHeight;
                    if (record.Type != "image" || string.IsNullOrEmpty(record.Name) || area <= 0)
                        continue;
                    if (!own.TryGetValue(record.Name, out HashSet<long>? sizes))
                        own[record.Name] = sizes = [];
                    sizes.Add(area);
                }
                foreach ((string name, HashSet<long> sizes) in own)
                {
                    if (!perPack.TryGetValue(name, out List<(long, int)>? list))
                        perPack[name] = list = [];
                    list.Add((sizes.Max(), sizes.Count));
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _log($"  skipped {Path.GetFileName(path)}: {error.Message}");
            }
        }
        var images = new Dictionary<string, (long Area, int Parts)>(StringComparer.Ordinal);
        foreach ((string name, List<(long Area, int Parts)> list) in perPack)
            images[name] = Pick(list);
        try
        {
            CacheFiles.WriteAllLines(_cache, images.OrderBy(i => i.Key, StringComparer.Ordinal)
                .Select(i => string.Create(CultureInfo.InvariantCulture, $"{i.Key}\t{i.Value.Area}\t{i.Value.Parts}")).Prepend(Version));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log($"  could not save the game's texture sizes: {error.Message}");
        }
        return images;
    }

    private Dictionary<string, (long Area, int Parts)>? Read()
    {
        try
        {
            if (!File.Exists(_cache))
                return null;
            string[] lines = File.ReadAllLines(_cache);
            if (lines.Length == 0 || lines[0] != Version)
                return null;
            var images = new Dictionary<string, (long, int)>(StringComparer.Ordinal);
            foreach (string line in lines.Skip(1))
            {
                string[] fields = line.Split('\t');
                if (fields.Length == 3 && long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long area)
                    && int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parts))
                    images[fields[0]] = (area, parts);
            }
            return images;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
