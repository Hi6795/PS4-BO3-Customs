using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CommunityUploader;

internal sealed class StandIn : IDisposable
{
    private readonly HttpListener _listener = new();

    public ConcurrentDictionary<string, byte[]> Files { get; } = new();
    public string Address { get; }
    public volatile bool AllowDelete = true;
    public volatile int UploadsLeft = int.MaxValue;
    public volatile int UploadAsks;

    public StandIn()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Address = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(Address + "/");
        _listener.Start();
        _ = Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception error) when (error is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }
            _ = Task.Run(() => Handle(context));
        }
    }

    private static void Answer(HttpListenerContext context, int status, string json)
    {
        byte[] data = Encoding.UTF8.GetBytes(json);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = data.Length;
        context.Response.OutputStream.Write(data);
        context.Response.Close();
    }

    private void Handle(HttpListenerContext context)
    {
        try
        {
            string path = context.Request.Url!.AbsolutePath;
            using var body = new MemoryStream();
            context.Request.InputStream.CopyTo(body);
            byte[] sent = body.ToArray();
            string given = context.Request.Headers["Authorization"] ?? "";

            if (path == "/b2api/v4/b2_authorize_account")
            {
                if (given != "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("test-id:test-key")))
                {
                    Answer(context, 401, "{\"code\":\"bad_auth_token\"}");
                    return;
                }
                string can = AllowDelete ? "[\"writeFiles\",\"readFiles\",\"listFiles\",\"deleteFiles\"]" : "[\"writeFiles\",\"readFiles\"]";
                Answer(context, 200, "{\"authorizationToken\":\"api-token\",\"apiInfo\":{\"storageApi\":{\"apiUrl\":\"" + Address + "\",\"allowed\":{\"capabilities\":" + can
                    + ",\"buckets\":[{\"id\":\"bucket-1\",\"name\":\"" + Server.Bucket + "\"}]}}}}");
                return;
            }
            if (path.StartsWith("/file/", StringComparison.Ordinal))
            {
                string name = Uri.UnescapeDataString(path[("/file/" + Server.Bucket + "/").Length..]);
                if (!Files.TryGetValue(name, out byte[]? stored))
                {
                    Answer(context, 404, "{\"code\":\"not_found\"}");
                    return;
                }
                context.Response.StatusCode = 200;
                context.Response.ContentLength64 = stored.Length;
                context.Response.OutputStream.Write(stored);
                context.Response.Close();
                return;
            }
            if (path == "/upload")
            {
                string name = Uri.UnescapeDataString(context.Request.Headers["X-Bz-File-Name"] ?? "");
                string sum = Convert.ToHexStringLower(SHA1.HashData(sent));
                if (given != "upload-token" || name.Length == 0 || sum != context.Request.Headers["X-Bz-Content-Sha1"])
                {
                    Answer(context, 400, "{\"code\":\"bad_request\"}");
                    return;
                }
                Files[name] = sent;
                Answer(context, 200, JsonSerializer.Serialize(new { fileId = "id-" + name, fileName = name, contentSha1 = sum, contentLength = sent.Length }));
                return;
            }
            if (given != "api-token")
            {
                Answer(context, 401, "{\"code\":\"unauthorized\"}");
                return;
            }
            JsonNode? asked = sent.Length > 0 ? JsonNode.Parse(sent) : null;
            switch (path)
            {
                case "/b2api/v4/b2_get_upload_url":
                    Interlocked.Increment(ref UploadAsks);
                    if (Interlocked.Decrement(ref UploadsLeft) < 0)
                    {
                        Answer(context, 403, "{\"status\":403,\"code\":\"storage_cap_exceeded\",\"message\":\"Cannot upload files, storage cap exceeded.\"}");
                        return;
                    }
                    Answer(context, 200, "{\"uploadUrl\":\"" + Address + "/upload\",\"authorizationToken\":\"upload-token\"}");
                    return;
                case "/b2api/v4/b2_list_file_versions":
                {
                    string prefix = asked?["prefix"]?.GetValue<string>() ?? "";
                    var files = Files.Keys.Where(name => name.StartsWith(prefix, StringComparison.Ordinal)).Order().Select(name => new { fileId = "id-" + name, fileName = name });
                    Answer(context, AllowDelete ? 200 : 401, JsonSerializer.Serialize(new { files }));
                    return;
                }
                case "/b2api/v4/b2_delete_file_version":
                {
                    string name = asked?["fileName"]?.GetValue<string>() ?? "";
                    bool matches = asked?["fileId"]?.GetValue<string>() == "id-" + name;
                    Answer(context, AllowDelete && matches && Files.TryRemove(name, out _) ? 200 : 400, "{}");
                    return;
                }
            }
            Answer(context, 404, "{\"code\":\"no_such_call\"}");
        }
        catch (Exception error) when (error is IOException or HttpListenerException or JsonException or InvalidOperationException)
        {
        }
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
    }
}

internal static class SelfTest
{
    public static async Task<bool> RunAsync(MainWindow window, string scratch, string report)
    {
        var lines = new List<string>();
        bool all = true;
        void Check(bool ok, string what)
        {
            all &= ok;
            lines.Add((ok ? "ok   " : "FAIL ") + what);
        }

        string root = Path.Combine(scratch, "root");
        string map = Path.Combine(root, "usermaps", "zm_fake"), mod = Path.Combine(root, "mods", "fakemod");
        Directory.CreateDirectory(Path.Combine(map, "snd", "all"));
        Directory.CreateDirectory(mod);
        File.WriteAllBytes(Path.Combine(map, "zm_fake.ff"), RandomNumberGenerator.GetBytes(250_000));
        File.WriteAllBytes(Path.Combine(map, "snd", "all", "zm_fake.all.sabl"), RandomNumberGenerator.GetBytes(40_000));
        File.WriteAllBytes(Path.Combine(map, "previewimage.png"), RandomNumberGenerator.GetBytes(3_000));
        File.WriteAllBytes(Path.Combine(map, "zm_fake.fidelity.json"), RandomNumberGenerator.GetBytes(500));
        File.WriteAllText(Path.Combine(map, "workshop.json"), "{\"Title\":\"Fake Map é\",\"Author\":\"someone\",\"Version\":3}");
        File.WriteAllBytes(Path.Combine(mod, "core_mod.ff"), RandomNumberGenerator.GetBytes(120_000));
        File.WriteAllBytes(Path.Combine(mod, "zm_mod.ff"), RandomNumberGenerator.GetBytes(90_000));
        File.WriteAllText(Path.Combine(mod, "workshop.json"), "{\"Title\":\"Fake Mod\",\"Type\":\"mod\"}");

        using var server = new StandIn();
        Server.AuthorizeAddress = server.Address + "/b2api/v4/b2_authorize_account";
        Server.ListAddress = $"{server.Address}/file/{Server.Bucket}/{Server.Prefix}/catalog.json";
        Server.PartBytes = 100_000;
        string listName = $"{Server.Prefix}/catalog.json";

        window.Quiet = true;
        window.WorkFolder = Path.Combine(scratch, "work");
        window.StartFolder = root;
        window.TestKeyId = "test-id";
        window.TestKey = "test-key";
        await window.StartAsync();
        Check(window.LocalRows.Count == 2 && window.LocalRows.All(row => row.CanSelect && row.Selected) && window.PublishedList.Count == 0,
            $"an empty server lists nothing; the folder shows 1 map and 1 mod ({string.Join(", ", window.LocalRows.Select(row => $"{row.Name} {row.Tag} {row.Status}"))})");

        JsonArray Maps() => JsonNode.Parse(server.Files[listName])!["maps"]!.AsArray();
        JsonObject? Entry(string name) => Maps().OfType<JsonObject>().FirstOrDefault(entry => entry["name"]!.GetValue<string>() == name);
        byte[] Joined(JsonObject entry) => [.. entry["parts"]!.AsArray().SelectMany(part => server.Files[$"{Server.Prefix}/{part!.GetValue<string>()}"])];

        bool sentMap = await window.UploadOnlyAsync("zm_fake");
        JsonObject? first = server.Files.ContainsKey(listName) ? Entry("zm_fake") : null;
        Check(sentMap && first != null, $"uploading a map finishes and lists it ({window.LastProblem})");
        if (first != null)
        {
            byte[] zip = Joined(first);
            using var archive = new ZipArchive(new MemoryStream(zip));
            string[] names = [.. archive.Entries.Select(entry => entry.FullName).Order()];
            lines.Add("   entry: " + first.ToJsonString());
            Check(first["parts"]!.AsArray().Count >= 3 && first["parts"]!.AsArray().All(part => part!.GetValue<string>().StartsWith("maps/zm_fake/", StringComparison.Ordinal)),
                "the map is split into parts under maps/<name>/<release>/");
            Check(Convert.ToHexStringLower(SHA256.HashData(zip)) == first["sha256"]!.GetValue<string>() && zip.Length == first["size"]!.GetValue<long>(),
                "the parts on the server join into a zip with the listed size and SHA-256");
            Check(names.SequenceEqual(["previewimage.png", "snd/all/zm_fake.all.sabl", "workshop.json", "zm_fake.ff"]), $"the zip holds the map's files at its top, without the report file ({string.Join(", ", names)})");
            Check(first["type"]!.GetValue<string>() == "map" && first["title"]!.GetValue<string>() == "Fake Map é" && first["author"]!.GetValue<string>() == "someone"
                && first["version"]!.GetValue<string>() == "3" && first["folder"]!.GetValue<string>() == "zm_fake", "title, author and version come from workshop.json");
            string? picture = first["thumbnail"]?.GetValue<string>();
            Check(picture != null && server.Files.TryGetValue($"{Server.Prefix}/{picture}", out byte[]? image)
                && image.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(map, "previewimage.png"))), "the preview picture is uploaded and listed as the thumbnail");
        }
        Check(!HasMark(server.Files.GetValueOrDefault(listName) ?? []), "the list is written without a byte-order mark");
        Check(!Directory.EnumerateFiles(window.WorkFolder, "*.zip").Any(), "the temporary zip is removed afterwards");

        bool sentMod = await window.UploadOnlyAsync("fakemod");
        JsonObject? second = Entry("fakemod");
        Check(sentMod && second != null && second["type"]!.GetValue<string>() == "mod" && second["parts"]![0]!.GetValue<string>().StartsWith("mods/fakemod/", StringComparison.Ordinal)
            && Maps().Count == 2, "uploading a mod lists it as a mod under mods/, and the map stays listed");

        string[] oldParts = [.. Entry("zm_fake")!["parts"]!.AsArray().Select(part => $"{Server.Prefix}/{part!.GetValue<string>()}")];
        bool again = await window.UploadOnlyAsync("zm_fake");
        Check(again && Maps().Count == 2 && Maps().OfType<JsonObject>().Count(entry => entry["name"]!.GetValue<string>() == "zm_fake") == 1
            && !Entry("zm_fake")!["parts"]![0]!.GetValue<string>().Equals(oldParts[0][(Server.Prefix.Length + 1)..]), "uploading a map again replaces its entry instead of adding a second one");

        await window.ScanAsync();
        Check(window.PublishedList.Count == 2 && window.LocalRows.All(row => row.Listed && !row.Selected && row.Status == "In the list"),
            "after a refresh both show as published, and the local rows say In the list and are unticked");

        string[] mapParts = [.. Entry("zm_fake")!["parts"]!.AsArray().Select(part => $"{Server.Prefix}/{part!.GetValue<string>()}")];
        bool unlisted = await window.RemoveOnlyAsync("zm_fake", false);
        Check(unlisted && Maps().Count == 1 && Entry("zm_fake") == null && mapParts.All(server.Files.ContainsKey), "Take off the list removes the entry and leaves its files on the server");
        Check(Directory.EnumerateFiles(window.WorkFolder, "list.before-*.json").Any() && Directory.EnumerateFiles(window.WorkFolder, "unlisted-files-*.txt").Any(),
            "the list from before is kept, and the unlisted files are named in a text file");

        await window.ScanAsync();
        server.AllowDelete = false;
        bool refused = await window.RemoveOnlyAsync("fakemod", true);
        Check(!refused && window.LastProblem.Contains("may not list and delete") && Entry("fakemod") != null, $"a key that may not delete changes nothing and says so ({window.LastProblem})");
        server.AllowDelete = true;

        string[] modFiles = [.. Entry("fakemod")!["parts"]!.AsArray().Select(part => $"{Server.Prefix}/{part!.GetValue<string>()}")];
        await window.ScanAsync();
        bool deleted = await window.RemoveOnlyAsync("fakemod", true);
        Check(deleted && Maps().Count == 0 && !modFiles.Any(server.Files.ContainsKey) && mapParts.All(server.Files.ContainsKey),
            "Delete from server removes the entry and exactly its own files; other files are untouched");

        await window.ScanAsync();
        server.UploadsLeft = 2;
        server.UploadAsks = 0;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        bool capped = await window.UploadOnlyAsync("zm_fake");
        timer.Stop();
        Check(!capped && window.LastProblem.Contains("403 storage_cap_exceeded: Cannot upload files") && window.LastProblem.Contains("zm_fake.zip.002") && server.UploadAsks == 3
            && timer.Elapsed.TotalSeconds < 4 && Maps().Count == 0, $"a refusal part-way stops at once and shows the service's own words, with no retries ({window.LastProblem})");

        server.UploadsLeft = 0;
        server.UploadAsks = 0;
        bool refusedFirst = await window.UploadOnlyAsync("zm_fake");
        server.UploadsLeft = int.MaxValue;
        Check(!refusedFirst && window.LastProblem.StartsWith("The server will not take uploads right now") && window.LastProblem.Contains("storage_cap_exceeded") && server.UploadAsks == 1
            && !Directory.EnumerateFiles(window.WorkFolder, "*.zip").Any(), $"a server that refuses uploads is noticed before anything is zipped ({window.LastProblem})");

        window.TestKey = "wrong";
        await window.ScanAsync();
        bool wrong = await window.UploadOnlyAsync("fakemod");
        Check(!wrong && window.LastProblem.Contains("refused the key") && Maps().Count == 0, $"a wrong key is refused before anything is sent ({window.LastProblem})");

        lines.Add(all ? "ALL PASSED" : "SOMETHING FAILED");
        File.WriteAllLines(report, lines);
        return all;
    }

    private static bool HasMark(byte[] data) => Publisher.HasMark(data);
}
