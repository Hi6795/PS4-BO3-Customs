using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Media.Imaging;
using FFPorter.Desktop.Models;

namespace FFPorter.Desktop.Services;

public static class CommunityCatalog
{
    private const int LargestList = 4 << 20, LargestPicture = 8 << 20, PictureWidth = 256;
    private static readonly TimeSpan SmallRequest = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };

    internal static HttpClient Http { get; } = CreateClient(DecompressionMethods.All);

    internal static HttpClient Files { get; } = CreateClient(DecompressionMethods.None);

    private sealed class ListFile
    {
        public string? Name { get; set; }
        public List<Entry?>? Maps { get; set; }
        public List<Entry?>? Mods { get; set; }
    }

    private sealed class Entry
    {
        public string? Name { get; set; }
        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? Description { get; set; }
        public string? Version { get; set; }
        public long Size { get; set; }
        public string? Folder { get; set; }
        public string? File { get; set; }
        public string? Url { get; set; }
        public List<string?>? Parts { get; set; }
        public string? Thumbnail { get; set; }
        public string? Sha256 { get; set; }
        public string? Type { get; set; }
        public string? Category { get; set; }
    }

    private static HttpClient CreateClient(DecompressionMethods decompression)
    {
        var client = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(20), AutomaticDecompression = decompression })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PS4-FF-Porter");
        return client;
    }

    public static bool TryAddress(string? text, out Uri address)
    {
        if (Uri.TryCreate(text?.Trim(), UriKind.Absolute, out Uri? parsed) && Allowed(parsed))
        {
            address = parsed;
            return true;
        }
        address = null!;
        return false;
    }

    private static bool Allowed(Uri address) =>
        address.Scheme == Uri.UriSchemeHttps || (address.Scheme == Uri.UriSchemeHttp && address.IsLoopback);

    public static async Task<IReadOnlyList<CommunityMap>> LoadAsync(Uri address, Action<string> log, CancellationToken token)
    {
        byte[] bytes = await ReadAsync(address, LargestList, fresh: true, token);
        ListFile? list;
        try
        {
            using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
            list = JsonSerializer.Deserialize<ListFile>(reader.ReadToEnd(), Json);
        }
        catch (JsonException error)
        {
            throw new IOException($"the map list could not be read: {error.Message}", error);
        }
        if (list == null || (list.Maps == null && list.Mods == null))
            throw new IOException("the map server did not send a map list");

        var maps = new List<CommunityMap>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((Entry? entry, bool mod) in (list.Maps ?? []).Select(entry => (entry, false)).Concat((list.Mods ?? []).Select(entry => (entry, true))))
        {
            if (entry == null)
                continue;
            string label = Untagged(entry.Title?.Trim() is { Length: > 0 } title ? title : entry.Name?.Trim() ?? "");
            if (SafeName(entry.Name) is not { } id || SafeName(string.IsNullOrWhiteSpace(entry.Folder) ? entry.Name : entry.Folder) is not { } folder)
            {
                log($"Skipped {(label.Length > 0 ? label : "a map")} in the list: its name is not a folder name.");
                continue;
            }
            if (!seen.Add(id))
            {
                log($"Skipped a second {id} in the list.");
                continue;
            }
            List<string> sources = entry.Parts?.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()).ToList() is { Count: > 0 } parts ? parts
                : !string.IsNullOrWhiteSpace(entry.File) ? [entry.File.Trim()]
                : !string.IsNullOrWhiteSpace(entry.Url) ? [entry.Url.Trim()]
                : [];
            var links = new List<Uri>();
            foreach (string source in sources)
            {
                if (Uri.TryCreate(address, source, out Uri? link) && Allowed(link))
                    links.Add(link);
            }
            if (links.Count == 0 || links.Count != sources.Count)
            {
                log($"Skipped {label}: {(sources.Count == 0 ? "the list has no file for it" : "its files are not at a secure (https) address")}.");
                continue;
            }
            string checksum = entry.Sha256?.Trim().ToLowerInvariant() ?? "";
            if (checksum.Length > 0 && (checksum.Length != 64 || !checksum.All(Uri.IsHexDigit)))
            {
                log($"Skipped {label}: its checksum in the list is not a SHA-256.");
                continue;
            }
            maps.Add(new CommunityMap
            {
                Id = id,
                Title = label,
                Folder = folder,
                Parts = links,
                Author = entry.Author?.Trim() ?? "",
                Description = entry.Description?.Trim() ?? "",
                Version = entry.Version?.Trim() ?? "",
                Size = Math.Max(0, entry.Size),
                Thumbnail = !string.IsNullOrWhiteSpace(entry.Thumbnail) && Uri.TryCreate(address, entry.Thumbnail.Trim(), out Uri? picture) && Allowed(picture) ? picture : null,
                Sha256 = checksum,
                Kind = mod ? CommunityKinds.Mod : CommunityKinds.Of(string.IsNullOrWhiteSpace(entry.Type) ? entry.Category : entry.Type, id),
            });
        }
        return maps;
    }

    public static async Task<BitmapSource?> PictureAsync(Uri address, CancellationToken token)
    {
        try
        {
            byte[] bytes = await ReadAsync(address, LargestPicture, fresh: false, token);
            return await Task.Run(() =>
            {
                var image = new BitmapImage();
                using var stream = new MemoryStream(bytes);
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = PictureWidth;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return (BitmapSource)image;
            }, token);
        }
        catch (Exception error) when (error is IOException or NotSupportedException or InvalidOperationException or ArgumentException or COMException)
        {
            return null;
        }
    }

    private static string Untagged(string title)
    {
        foreach (string tag in (string[])["[MP]", "[ZM]", "[CP]", "[MOD]"])
        {
            if (title.StartsWith(tag, StringComparison.OrdinalIgnoreCase) && title[tag.Length..].Trim() is { Length: > 0 } rest)
                return rest;
        }
        return title;
    }

    private static string? SafeName(string? text)
    {
        string name = text?.Trim() ?? "";
        return name.Length is 0 or > 64 || name.StartsWith('.') || name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? null : name;
    }

    private static async Task<byte[]> ReadAsync(Uri address, int most, bool fresh, CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(SmallRequest);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            if (fresh)
                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            using HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token);
            if (!response.IsSuccessStatusCode)
                throw new IOException(Refused(response));
            await using Stream stream = await response.Content.ReadAsStreamAsync(limit.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, limit.Token)) > 0)
            {
                if (buffer.Length + read > most)
                    throw new IOException("the map server sent more than a map list or picture should be");
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException("the map server did not answer in time");
        }
        catch (HttpRequestException error)
        {
            throw new IOException(Unreachable(error), error);
        }
    }

    internal static string Refused(HttpResponseMessage response) => $"the map server answered {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd();

    internal static string Unreachable(HttpRequestException error) => error.HttpRequestError switch
    {
        HttpRequestError.NameResolutionError => "the map server could not be found; check your internet connection",
        HttpRequestError.SecureConnectionError => "a secure connection to the map server could not be made",
        _ => "the map server could not be reached; check your internet connection",
    };
}
