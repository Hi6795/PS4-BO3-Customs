using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CommunityUploader;

internal sealed record Listed(string Name, string Title, string Kind, long Size);

internal static class Publisher
{
    public static readonly string[] ModZones = ["core_mod.ff", "zm_mod.ff", "mp_mod.ff", "cp_mod.ff", "mod.ff"];
    private static readonly string[] Wanted = [".ff", ".fd", ".xpak", ".sabl", ".sabs", ".png", ".jpg", ".jpeg", ".json", ".mkv"];
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly HttpClient Reader = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static bool HasMark(byte[] data) => data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF;

    public static string[] Files(string folder) =>
        [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(file => Wanted.Contains(Path.GetExtension(file).ToLowerInvariant()) && !file.EndsWith(".fidelity.json", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)];

    public static async Task<(JsonObject? List, byte[] Raw, bool Missing)> ReadListAsync(CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Server.ListAddress);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            using HttpResponseMessage response = await Reader.SendAsync(request, token);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return (null, [], true);
            if (!response.IsSuccessStatusCode)
                return (null, [], false);
            byte[] data = await response.Content.ReadAsByteArrayAsync(token);
            if (JsonNode.Parse(data.AsSpan(HasMark(data) ? 3 : 0)) is JsonObject root && root["maps"] is JsonArray)
                return (root, data, false);
            return (null, data, false);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or IOException || (error is TaskCanceledException && !token.IsCancellationRequested))
        {
            return (null, [], false);
        }
    }

    public static List<Listed> Entries(JsonObject list)
    {
        var entries = new List<Listed>();
        foreach (JsonNode? node in list["maps"]!.AsArray())
        {
            if (node is not JsonObject map || Text(map, "name") is not { Length: > 0 } name)
                continue;
            long size = map["size"] is JsonValue value && value.TryGetValue(out long bytes) ? bytes : 0;
            entries.Add(new Listed(name, Text(map, "title") ?? "", string.Equals(Text(map, "type"), "mod", StringComparison.OrdinalIgnoreCase) ? "Mod" : "Map", size));
        }
        return entries;
    }

    private static string? Text(JsonObject from, string field) => from[field] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static byte[] Bytes(JsonObject list) => new UTF8Encoding(false).GetBytes(list.ToJsonString(Pretty));

    private static string Sha1(byte[] data) => Convert.ToHexStringLower(SHA1.HashData(data));

    private static IEnumerable<string> Paths(JsonObject entry)
    {
        if (entry["parts"] is JsonArray parts)
        {
            foreach (JsonNode? part in parts)
            {
                if (part is JsonValue value && value.TryGetValue(out string? path) && !string.IsNullOrWhiteSpace(path))
                    yield return path;
            }
        }
        if (Text(entry, "thumbnail") is { Length: > 0 } picture)
            yield return picture;
    }

    private static bool Safe(string path) => !path.Contains("..") && !path.Contains(':') && !path.StartsWith('/') && !path.StartsWith('\\');

    private static JsonObject Describe(string name, string kind, string folder, long size, string[] parts, string sha256, string? thumbnail)
    {
        var entry = new JsonObject { ["name"] = name, ["title"] = name, ["size"] = size, ["folder"] = name, ["type"] = kind == "Mod" ? "mod" : "map" };
        entry["parts"] = new JsonArray([.. parts.Select(part => (JsonNode?)JsonValue.Create(part))]);
        entry["sha256"] = sha256;
        try
        {
            string workshop = Path.Combine(folder, "workshop.json");
            if (File.Exists(workshop))
            {
                byte[] data = File.ReadAllBytes(workshop);
                if (JsonNode.Parse(data.AsSpan(HasMark(data) ? 3 : 0)) is JsonObject about)
                {
                    string? Field(string wanted)
                    {
                        foreach (KeyValuePair<string, JsonNode?> pair in about)
                        {
                            if (pair.Key.Equals(wanted, StringComparison.OrdinalIgnoreCase) && pair.Value is JsonValue value)
                                return value.TryGetValue(out string? text) ? text : value.ToJsonString();
                        }
                        return null;
                    }
                    if (Field("title") is { Length: > 0 } title)
                        entry["title"] = title;
                    foreach (string field in (string[])["author", "description", "version"])
                    {
                        if (Field(field) is { Length: > 0 } text)
                            entry[field] = text;
                    }
                }
            }
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
        }
        if (thumbnail != null)
            entry["thumbnail"] = thumbnail;
        return entry;
    }

    private static void Put(JsonObject list, JsonObject entry)
    {
        JsonArray maps = list["maps"]!.AsArray();
        string name = Text(entry, "name")!;
        for (int index = maps.Count - 1; index >= 0; index--)
        {
            if (maps[index] is JsonObject old && Text(old, "name") == name)
                maps.RemoveAt(index);
        }
        maps.Add(entry);
    }

    private static JsonObject Empty() => new() { ["name"] = "Black Ops III community maps", ["maps"] = new JsonArray() };

    public static async Task UploadAsync(Storage? storage, string name, string kind, string folder, string work, Action<string, double, long> step, CancellationToken token)
    {
        Directory.CreateDirectory(work);
        string[] files = Files(folder);
        long total = files.Sum(file => new FileInfo(file).Length);
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(work))!);
        if (drive.AvailableFreeSpace < total + (1L << 30))
            throw new IOException($"Not enough free space on {drive.Name} for {name}: {(total + (1L << 30)) / 1073741824.0:0.0} GB needed.");

        string zip = Path.Combine(work, name + ".zip");
        try
        {
            step("Zipping", 0, -1);
            await Task.Run(() =>
            {
                using var stream = new FileStream(zip, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 20);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
                var buffer = new byte[4 << 20];
                long done = 0, shown = 0;
                foreach (string file in files)
                {
                    ZipArchiveEntry entry = archive.CreateEntry(Path.GetRelativePath(folder, file).Replace('\\', '/'), CompressionLevel.NoCompression);
                    entry.LastWriteTime = File.GetLastWriteTime(file);
                    using Stream target = entry.Open();
                    using FileStream source = File.OpenRead(file);
                    int read;
                    while ((read = source.Read(buffer)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        target.Write(buffer, 0, read);
                        done += read;
                        if (done - shown >= 64 << 20)
                        {
                            shown = done;
                            step("Zipping", 0.2 * done / Math.Max(1, total), -1);
                        }
                    }
                }
            }, token);

            long size = new FileInfo(zip).Length;
            int count = (int)Math.Max(1, (size + Server.PartBytes - 1) / Server.PartBytes);
            var sums = new string[count];
            string sha256 = "";
            step("Checking", 0.2, -1);
            await Task.Run(() =>
            {
                using FileStream source = File.OpenRead(zip);
                using var whole = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[4 << 20];
                long done = 0, shown = 0;
                for (int part = 0; part < count; part++)
                {
                    using var one = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
                    long left = Math.Min(Server.PartBytes, size - part * Server.PartBytes);
                    while (left > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                        if (read <= 0)
                            throw new IOException("the zip ended early");
                        whole.AppendData(buffer, 0, read);
                        one.AppendData(buffer, 0, read);
                        left -= read;
                        done += read;
                        if (done - shown >= 64 << 20)
                        {
                            shown = done;
                            step("Checking", 0.2 + 0.1 * done / Math.Max(1, size), -1);
                        }
                    }
                    sums[part] = Convert.ToHexStringLower(one.GetHashAndReset());
                }
                sha256 = Convert.ToHexStringLower(whole.GetHashAndReset());
            }, token);

            string release = Guid.NewGuid().ToString("N");
            string relative = $"{(kind == "Mod" ? "mods" : "maps")}/{name}/{release}";
            string[] parts = [.. Enumerable.Range(1, count).Select(number => $"{relative}/{name}.zip.{number:000}")];
            string picture = Path.Combine(folder, "previewimage.png");
            string? thumbnail = File.Exists(picture) ? $"thumbnails/{name}-{release}.png" : null;
            JsonObject described = Describe(name, kind, folder, size, parts, sha256, thumbnail);

            (JsonObject? list, _, bool missing) = await ReadListAsync(token);
            if (list == null && !missing)
                throw new IOException("The live list could not be read, so nothing was uploaded.");

            if (storage == null)
            {
                list ??= Empty();
                Put(list, described);
                await File.WriteAllBytesAsync(Path.Combine(work, "list.preview.json"), Bytes(list), token);
                step("Checked", 1, -1);
                return;
            }

            long before = 0;
            for (int part = 0; part < count; part++)
            {
                long offset = part * Server.PartBytes, length = Math.Min(Server.PartBytes, size - offset), start = before;
                string text = $"Uploading part {part + 1} of {count}";
                await storage.UploadAsync(parts[part], () => new Slice(zip, offset, length), length, sums[part], "application/octet-stream", null,
                    sent => step(text, 0.3 + 0.68 * (start + sent) / Math.Max(1, size), start + sent), token);
                before += length;
            }
            if (thumbnail != null)
            {
                byte[] image = await File.ReadAllBytesAsync(picture, token);
                await storage.UploadAsync(thumbnail, () => new MemoryStream(image), image.Length, Sha1(image), "image/png", null, _ => { }, token);
            }

            step("Updating the list", 0.99, -1);
            (list, _, missing) = await ReadListAsync(token);
            if (list == null && !missing)
                throw new IOException("The files are uploaded, but the live list could not be read, so nothing was listed. Try again.");
            list ??= Empty();
            Put(list, described);
            byte[] data = Bytes(list);
            await storage.UploadAsync("catalog.json", () => new MemoryStream(data), data.Length, Sha1(data), "application/json", "no-store", _ => { }, token);
            step("Uploaded", 1, -1);
        }
        finally
        {
            try
            {
                File.Delete(zip);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public static async Task RemoveAsync(Storage? storage, IReadOnlyList<string> names, bool delete, string work, Action<int, string> began, Action<int> finished, Action<string> log,
        CancellationToken token)
    {
        Directory.CreateDirectory(work);
        (JsonObject? list, byte[] raw, _) = await ReadListAsync(token);
        if (list == null)
            throw new IOException("The live list could not be read, so nothing was changed.");
        if (storage != null && delete && !storage.CanDelete)
            throw new IOException("This key may not list and delete files, so nothing was changed. Use Take off the list, or a key that can delete.");

        JsonArray maps = list["maps"]!.AsArray();
        var gone = new List<JsonObject>();
        foreach (string name in names)
        {
            for (int index = maps.Count - 1; index >= 0; index--)
            {
                if (maps[index] is JsonObject entry && Text(entry, "name") == name)
                {
                    maps.RemoveAt(index);
                    gone.Add(entry);
                }
            }
        }
        if (gone.Count == 0)
            throw new IOException("None of those are in the list any more.");

        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        await File.WriteAllBytesAsync(Path.Combine(work, $"list.before-{stamp}.json"), raw, token);
        byte[] data = Bytes(list);
        if (storage == null)
        {
            await File.WriteAllBytesAsync(Path.Combine(work, "list.preview.json"), data, token);
        }
        else
        {
            log("Updating the list");
            await storage.UploadAsync("catalog.json", () => new MemoryStream(data), data.Length, Sha1(data), "application/json", "no-store", _ => { }, token);
        }

        var used = new HashSet<string>(maps.OfType<JsonObject>().SelectMany(Paths), StringComparer.Ordinal);
        string left = Path.Combine(work, $"unlisted-files-{stamp}.txt");
        foreach (string name in names)
        {
            int index = names.ToList().IndexOf(name);
            if (gone.FirstOrDefault(entry => Text(entry, "name") == name) is not { } entry)
                continue;
            began(index, name);
            string[] paths = [.. Paths(entry).Where(path => Safe(path) && !used.Contains(path))];
            if (!delete)
            {
                await File.AppendAllLinesAsync(left, paths.Select(path => $"{Server.Prefix}/{path}"), token);
                log($"{name}: off the list; {paths.Length} files stay on the server");
            }
            else if (storage == null)
            {
                foreach (string path in paths)
                    log($"would delete {Server.Prefix}/{path}");
            }
            else
            {
                int removed = 0;
                foreach (string path in paths)
                {
                    if (await storage.DeleteAsync(path, token) > 0)
                        removed++;
                    else
                        await File.AppendAllLinesAsync(left, [$"{Server.Prefix}/{path}  (not found on the server)"], token);
                }
                log($"{name}: {removed} of {paths.Length} files deleted");
            }
            finished(index);
        }
    }
}
