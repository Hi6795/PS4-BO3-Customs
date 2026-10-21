using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CommunityUploader;

internal sealed class Slice(string file, long offset, long length) : Stream
{
    private readonly FileStream _source = Open(file, offset);
    private readonly long _length = length;
    private long _left = length;

    private static FileStream Open(string file, long offset)
    {
        var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan | FileOptions.Asynchronous);
        stream.Seek(offset, SeekOrigin.Begin);
        return stream;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => _length - _left; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int at, int count)
    {
        int read = _source.Read(buffer, at, (int)Math.Min(count, _left));
        _left -= read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        int read = await _source.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _left)], token);
        _left -= read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int at, int count, CancellationToken token) => ReadAsync(buffer.AsMemory(at, count), token).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long to, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int at, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _source.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class Counted(Stream inner, Action<long> sent) : Stream
{
    private long _total;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => _total; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int at, int count)
    {
        int read = inner.Read(buffer, at, count);
        sent(_total += read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        int read = await inner.ReadAsync(buffer, token);
        sent(_total += read);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int at, int count, CancellationToken token) => ReadAsync(buffer.AsMemory(at, count), token).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long to, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int at, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class Refused(string message, bool final) : IOException(message)
{
    public bool Final { get; } = final;
}

internal sealed class Storage : IDisposable
{
    private const int Attempts = 5;

    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private string _api = "", _token = "", _bucket = "";

    public bool CanDelete { get; private set; }

    public static async Task<Storage> ConnectAsync(string keyId, string key, CancellationToken token)
    {
        var storage = new Storage();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Server.AuthorizeAddress);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{key}")));
            using HttpResponseMessage response = await storage._http.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
                throw new IOException("The storage service refused the key. Check the key ID and the key.");
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(token));
            JsonElement api = document.RootElement.GetProperty("apiInfo").GetProperty("storageApi");
            JsonElement allowed = api.GetProperty("allowed");
            string[] can = [.. allowed.GetProperty("capabilities").EnumerateArray().Select(item => item.GetString() ?? "")];
            if (!can.Contains("writeFiles"))
                throw new IOException("This key may not write files.");
            string? bucket = null;
            if (allowed.TryGetProperty("buckets", out JsonElement buckets) && buckets.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in buckets.EnumerateArray())
                {
                    if (item.TryGetProperty("name", out JsonElement name) && name.GetString() == Server.Bucket && item.TryGetProperty("id", out JsonElement id))
                        bucket = id.GetString();
                }
            }
            if (bucket == null && allowed.TryGetProperty("bucketName", out JsonElement single) && single.ValueKind == JsonValueKind.String && single.GetString() == Server.Bucket
                && allowed.TryGetProperty("bucketId", out JsonElement singleId))
            {
                bucket = singleId.GetString();
            }
            if (string.IsNullOrEmpty(bucket))
                throw new IOException($"This key is not limited to the {Server.Bucket} bucket.");
            storage._bucket = bucket;
            storage._api = api.GetProperty("apiUrl").GetString() ?? "";
            storage._token = document.RootElement.GetProperty("authorizationToken").GetString() ?? "";
            storage.CanDelete = can.Contains("listFiles") && can.Contains("deleteFiles");
            return storage;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            storage.Dispose();
            throw new IOException("The storage service could not be reached, or answered in a way this tool does not know.", error);
        }
        catch
        {
            storage.Dispose();
            throw;
        }
    }

    private static Refused Why(int status, byte[] data)
    {
        string code = "", message = "";
        try
        {
            using JsonDocument document = JsonDocument.Parse(data);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                code = document.RootElement.TryGetProperty("code", out JsonElement named) && named.ValueKind == JsonValueKind.String ? named.GetString() ?? "" : "";
                message = document.RootElement.TryGetProperty("message", out JsonElement said) && said.ValueKind == JsonValueKind.String ? said.GetString() ?? "" : "";
            }
        }
        catch (JsonException)
        {
        }
        bool final = status is >= 400 and < 500 and not 408 and not 429;
        string reason = $"the storage service answered {status}{(code.Length > 0 ? " " + code : "")}{(message.Length > 0 ? ": " + message.TrimEnd('.') : "")}";
        if (code is "storage_cap_exceeded" or "transaction_cap_exceeded" or "download_cap_exceeded")
            reason += " (caps are set in Backblaze under Caps & Alerts)";
        return new Refused(reason, final);
    }

    private async Task<JsonDocument> CallAsync(string method, object body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_api}/b2api/v4/{method}");
        request.Headers.TryAddWithoutValidation("Authorization", _token);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _http.SendAsync(request, token);
        byte[] data = await response.Content.ReadAsByteArrayAsync(token);
        if (!response.IsSuccessStatusCode)
            throw Why((int)response.StatusCode, data);
        return JsonDocument.Parse(data);
    }

    public async Task CheckAsync(CancellationToken token)
    {
        try
        {
            using JsonDocument asked = await CallAsync("b2_get_upload_url", new { bucketId = _bucket }, token);
        }
        catch (Refused error)
        {
            throw new IOException($"The server will not take uploads right now: {error.Message}.");
        }
    }

    public async Task UploadAsync(string path, Func<Stream> open, long length, string sha1, string type, string? cache, Action<long> sent, CancellationToken token)
    {
        string remote = $"{Server.Prefix}/{path}";
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using JsonDocument target = await CallAsync("b2_get_upload_url", new { bucketId = _bucket }, token);
                using var request = new HttpRequestMessage(HttpMethod.Post, target.RootElement.GetProperty("uploadUrl").GetString());
                request.Headers.TryAddWithoutValidation("Authorization", target.RootElement.GetProperty("authorizationToken").GetString());
                request.Headers.TryAddWithoutValidation("X-Bz-File-Name", Uri.EscapeDataString(remote));
                request.Headers.TryAddWithoutValidation("X-Bz-Content-Sha1", sha1);
                if (cache != null)
                    request.Headers.TryAddWithoutValidation("X-Bz-Info-b2-cache-control", cache);
                request.Content = new StreamContent(new Counted(open(), sent), 1 << 20);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue(type);
                request.Content.Headers.ContentLength = length;
                using HttpResponseMessage response = await _http.SendAsync(request, token);
                byte[] data = await response.Content.ReadAsByteArrayAsync(token);
                if (!response.IsSuccessStatusCode)
                    throw Why((int)response.StatusCode, data);
                using JsonDocument result = JsonDocument.Parse(data);
                if (result.RootElement.GetProperty("contentSha1").GetString() != sha1 || result.RootElement.GetProperty("contentLength").GetInt64() != length)
                    throw new IOException("what arrived does not match what was sent");
                return;
            }
            catch (Exception error) when (!token.IsCancellationRequested && error is IOException or HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                if (error is Refused { Final: true })
                    throw new IOException($"{Path.GetFileName(path)} was not uploaded: {error.Message}.");
                if (attempt >= Attempts)
                    throw new IOException($"{Path.GetFileName(path)} could not be uploaded after {Attempts} tries: {error.Message}.");
                sent(0);
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), token);
            }
        }
    }

    public async Task<int> DeleteAsync(string path, CancellationToken token)
    {
        string remote = $"{Server.Prefix}/{path}";
        int removed = 0;
        using JsonDocument found = await CallAsync("b2_list_file_versions", new { bucketId = _bucket, startFileName = remote, prefix = remote, maxFileCount = 100 }, token);
        foreach (JsonElement file in found.RootElement.GetProperty("files").EnumerateArray())
        {
            if (file.GetProperty("fileName").GetString() != remote)
                continue;
            using JsonDocument gone = await CallAsync("b2_delete_file_version", new { fileName = remote, fileId = file.GetProperty("fileId").GetString() }, token);
            removed++;
        }
        return removed;
    }

    public void Dispose() => _http.Dispose();
}
