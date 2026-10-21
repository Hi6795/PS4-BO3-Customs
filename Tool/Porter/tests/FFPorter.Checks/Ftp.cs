using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;

internal sealed class MiniFtp : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly string _root;
    private readonly List<string> _commands = [];

    public volatile bool RefuseOverwrite, NoSize, RefuseTwice;
    public volatile string? Truncate, Slow;
    public volatile string Renamed = "250 renamed";
    public volatile int Greet;
    public ConcurrentDictionary<string, long> Sizes { get; } = new();
    public ConcurrentDictionary<string, bool> ReadOnly { get; } = new();
    public int Port { get; }

    public MiniFtp(string root)
    {
        _root = root;
        Directory.CreateDirectory(root);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptAsync);
    }

    public string[] Commands
    {
        get
        {
            lock (_commands)
                return [.. _commands];
        }
    }

    public void Forget()
    {
        lock (_commands)
            _commands.Clear();
    }

    public string Local(string path) => Path.Combine(_root, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    private async Task AcceptAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync();
            }
            catch (Exception)
            {
                return;
            }
            _ = Task.Run(() => Serve(client));
        }
    }

    private void Serve(TcpClient client)
    {
        try
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.UTF8);
                void Say(string text) => stream.Write(Encoding.UTF8.GetBytes(text + "\r\n"));
                if (Greet > 0)
                    Thread.Sleep(Greet);
                Say("220-Mini FTP");
                Say("220 ready");
                TcpListener? passive = null;
                string? from = null;
                string current = "/";
                while (reader.ReadLine() is { } line)
                {
                    lock (_commands)
                        _commands.Add(line);
                    string verb = line.Split(' ')[0].ToUpperInvariant();
                    string argument = line.Length > verb.Length ? line[(verb.Length + 1)..] : "";
                    switch (verb)
                    {
                        case "USER": Say("331 password please"); break;
                        case "PASS": Say("230 welcome"); break;
                        case "TYPE": Say("200 ok"); break;
                        case "CWD":
                            if (Directory.Exists(Local(argument)))
                            {
                                current = argument;
                                Say("250 ok");
                            }
                            else
                            {
                                Say("550 no such directory");
                            }
                            break;
                        case "LIST":
                            Say("150 here it comes");
                            using (TcpClient data = passive!.AcceptTcpClient())
                            {
                                var listing = new StringBuilder("drwxr-xr-x   1 ps4 ps4      4096 Jan 01 00:00 .\r\ndrwxr-xr-x   1 ps4 ps4      4096 Jan 01 00:00 ..\r\n");
                                foreach (string folder in Directory.GetDirectories(Local(current)))
                                    listing.Append($"drwxr-xr-x   1 ps4 ps4      4096 Oct 03 20:35 {Path.GetFileName(folder)}\r\n");
                                foreach (string file in Directory.GetFiles(Local(current)))
                                {
                                    long length = Sizes.TryGetValue($"{current.TrimEnd('/')}/{Path.GetFileName(file)}", out long listed) ? listed : new FileInfo(file).Length;
                                    listing.Append($"-rw-r--r--   1 ps4 ps4 {length,9} Oct 03  2026 {Path.GetFileName(file)}\r\n");
                                }
                                data.GetStream().Write(Encoding.UTF8.GetBytes(listing.ToString()));
                            }
                            Say("226 done");
                            break;
                        case "RMD":
                            if (!Directory.Exists(Local(argument)))
                            {
                                Say("550 no such directory");
                            }
                            else if (Directory.EnumerateFileSystemEntries(Local(argument)).Any())
                            {
                                Say("550 directory not empty");
                            }
                            else
                            {
                                Directory.Delete(Local(argument));
                                Say("250 removed");
                            }
                            break;
                        case "MKD":
                            if (Directory.Exists(Local(argument)))
                            {
                                Say("550 exists");
                            }
                            else
                            {
                                Directory.CreateDirectory(Local(argument));
                                Say($"257 \"{argument}\" created");
                            }
                            break;
                        case "PASV":
                            passive?.Stop();
                            passive = new TcpListener(IPAddress.Loopback, 0);
                            passive.Start();
                            int port = ((IPEndPoint)passive.LocalEndpoint).Port;
                            Say($"227 Entering Passive Mode (127,0,0,1,{port / 256},{port % 256})");
                            break;
                        case "STOR":
                            if (ReadOnly.Keys.Any(prefix => argument.StartsWith(prefix, StringComparison.Ordinal)))
                            {
                                Say("550 Read-only file system");
                                break;
                            }
                            Say("150 send it");
                            using (TcpClient data = passive!.AcceptTcpClient())
                            using (FileStream file = File.Create(Local(argument)))
                            {
                                NetworkStream source = data.GetStream();
                                var buffer = new byte[65536];
                                long total = 0;
                                int read;
                                bool cut = false;
                                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    file.Write(buffer, 0, read);
                                    total += read;
                                    if (argument == Slow)
                                        Thread.Sleep(40);
                                    if (argument == Truncate && total > 200_000)
                                    {
                                        cut = true;
                                        break;
                                    }
                                }
                                if (cut)
                                {
                                    data.Close();
                                    Say("426 connection closed; transfer aborted");
                                    break;
                                }
                            }
                            Say("226 done");
                            break;
                        case "SIZE":
                            Say(NoSize ? "502 Command not recognized"
                                : Sizes.TryGetValue(argument, out long stated) ? $"213 {stated}"
                                : File.Exists(Local(argument)) ? $"213 {new FileInfo(Local(argument)).Length}"
                                : "550 no such file");
                            break;
                        case "RNFR":
                            from = File.Exists(Local(argument)) ? argument : null;
                            Say(from != null ? "350 ready" : "550 no such file");
                            break;
                        case "RNTO":
                            if (from == null)
                            {
                                Say("503 RNFR first");
                            }
                            else if (RefuseOverwrite && File.Exists(Local(argument)))
                            {
                                Say("550 the file exists");
                                if (RefuseTwice)
                                    Say("226 Rename completed.");
                            }
                            else
                            {
                                File.Move(Local(from), Local(argument), overwrite: true);
                                Say(Renamed);
                            }
                            from = null;
                            break;
                        case "DELE":
                            File.Delete(Local(argument));
                            Say("226 File deleted");
                            break;
                        case "QUIT":
                            Say("221 bye");
                            return;
                        default:
                            Say("502 Command not recognized");
                            break;
                    }
                }
            }
        }
        catch (Exception)
        {
        }
    }

    public void Dispose() => _listener.Stop();
}

internal static class FtpChecks
{
    private static readonly Random Dice = new(11);

    private static byte[] Noise(int bytes)
    {
        var data = new byte[bytes];
        Dice.NextBytes(data);
        return data;
    }

    private static Dictionary<string, byte[]> Write(string folder, string id, int scale)
    {
        var files = new Dictionary<string, byte[]>
        {
            [$"{id}.ff"] = Noise(900_000 * scale),
            [$"en_{id}.ff"] = Noise(60_000),
            [$"{id}.xpak"] = Noise(2_600_000 * scale),
            [$"snd/all/{id}.all.sabl"] = Noise(300_000),
            ["workshop.json"] = Encoding.UTF8.GetBytes("{}"),
        };
        foreach ((string name, byte[] data) in files)
        {
            string path = Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
        }
        return files;
    }

    private static Exception? Try(Func<Task> action)
    {
        Task task = Task.Run(action);
        CommunityChecks.Pump(() => task.IsCompleted, 90);
        return task.Exception?.InnerException ?? (task.IsCanceled ? new OperationCanceledException() : null);
    }

    private static bool Same(MiniFtp server, string remote, Dictionary<string, byte[]> files) =>
        files.All(pair => File.Exists(server.Local($"{remote}/{pair.Key}")) && File.ReadAllBytes(server.Local($"{remote}/{pair.Key}")).AsSpan().SequenceEqual(pair.Value));

    private static string[] Parts(MiniFtp server, string remote) =>
        Directory.Exists(server.Local(remote)) ? [.. Directory.EnumerateFiles(server.Local(remote), "*.part", SearchOption.AllDirectories).Select(Path.GetFileName)!] : [];

    public static void Run(string scratch)
    {
        Console.WriteLine("== sending to a console");
        bool Address(string text, string host, int port) => ConsoleFtp.TryAddress(text, out string h, out int p) && h == host && p == port;
        Program.Check(Address("192.168.1.50", "192.168.1.50", 2121) && Address(" 10.0.0.83:21 ", "10.0.0.83", 21) && Address("ftp://10.0.0.83:2121/", "10.0.0.83", 2121) && Address("ps4.local", "ps4.local", 2121)
            && !ConsoleFtp.TryAddress("not an address", out _, out _) && !ConsoleFtp.TryAddress("1.2.3.4:99999", out _, out _) && !ConsoleFtp.TryAddress("", out _, out _)
            && !ConsoleFtp.TryAddress("1.2.3.4:", out _, out _), "a console address is an IP or name, with port 2121 unless one is given");
        var map = new CommunityMap { Id = "zm_send", Title = "Send Me", Folder = "zm_send", Parts = [new Uri("https://x/y")] };
        var mod = new CommunityMap { Id = "coolmod", Title = "Cool", Folder = "coolmod", Parts = [new Uri("https://x/y")], Kind = CommunityKinds.Mod };
        Program.Check(ConsoleFtp.Folder(map) == "/data/BO3-Customs/usermaps/zm_send" && ConsoleFtp.Folder(mod) == "/data/BO3-Customs/mods/coolmod", "maps go to usermaps and mods to mods on the console");

        string local = Path.Combine(scratch, "ftp", "local", "zm_send");
        Dictionary<string, byte[]> first = Write(local, "zm_send", 1);
        using var server = new MiniFtp(Path.Combine(scratch, "ftp", "console"));
        string remote = ConsoleFtp.Folder(map);
        var staged = new HashSet<string>();
        var log = new List<string>();
        var reports = new List<DownloadProgress>();
        var progress = new Progress<DownloadProgress>(reports.Add);
        Exception? Send(CancellationToken stop = default) =>
            Try(() => ConsoleFtp.SendAsync("127.0.0.1", server.Port, local, remote, map.MainFiles, staged, progress, line => { lock (log) log.Add(line); }, new PauseGate(), stop));

        Task<ConsoleLook> test = Task.Run(() => ConsoleFtp.TestAsync("127.0.0.1", server.Port, ConsoleFtp.Root, CancellationToken.None));
        CommunityChecks.Pump(() => test.IsCompleted);
        Program.Check(test.IsCompletedSuccessfully && test.Result is { Found: false, Drives.Count: 0 } && !Directory.Exists(server.Local("/data")),
            "Test connects and reports that BO3-Customs is not there yet, without making anything");

        Exception? error = Send();
        string[] commands = server.Commands;
        string[] stored = [.. commands.Where(line => line.StartsWith("STOR "))];
        string[] renamed = [.. commands.Where(line => line.StartsWith("RNTO "))];
        Console.WriteLine($"   first send: {error?.Message ?? "ok"}; {stored.Length} files stored, last put in place: {renamed.LastOrDefault()}");
        Program.Check(error == null && Same(server, remote, first) && Parts(server, remote).Length == 0, "a map arrives on the console byte for byte, in its folders, with nothing temporary left");
        Program.Check(stored.Length == first.Count && stored.All(line => line.EndsWith(".part")) && renamed.Length == first.Count && renamed[^1] == $"RNTO {remote}/zm_send.ff"
            && Array.IndexOf(commands, renamed[0]) > Array.IndexOf(commands, stored[^1]) && commands.Contains("TYPE I") && !commands.Any(line => line.StartsWith("DELE")),
            "every file is sent under a temporary name first; only then are they put in place, the map's own zone last, and nothing is deleted");
        CommunityChecks.Pump(() => false, 0.3);
        Program.Check(reports.Count >= 2 && reports.Any(report => report.Stage == "Sending to the console") && reports[^1].Fraction <= 1, $"it reports progress ({reports.Count} reports)");
        test = Task.Run(() => ConsoleFtp.TestAsync("127.0.0.1", server.Port, ConsoleFtp.Root, CancellationToken.None));
        CommunityChecks.Pump(() => test.IsCompleted);
        Program.Check(test.IsCompletedSuccessfully && test.Result.Found, "Test now finds BO3-Customs");

        Dictionary<string, byte[]> second = Write(local, "zm_send", 1);
        server.Forget();
        server.Slow = $"{remote}/zm_send.xpak.part";
        using (var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(250)))
            error = Send(stop.Token);
        server.Slow = null;
        string[] left = Parts(server, remote);
        Console.WriteLine($"   stopped send: {error?.GetType().Name}; temporary files on the console: {string.Join(", ", left)}");
        Program.Check(error is OperationCanceledException && Same(server, remote, first) && left.Length > 0 && !server.Commands.Any(line => line.StartsWith("RNTO")),
            "stopping lets the file in flight finish, then leaves the map that was already on the console exactly as it was");
        int before = server.Commands.Count(line => line.StartsWith("STOR "));
        error = Send();
        int after = server.Commands.Count(line => line.StartsWith("STOR "));
        Program.Check(error == null && Same(server, remote, second) && Parts(server, remote).Length == 0 && before + (after - before) == second.Count,
            $"sending again picks up where it stopped ({before} files the first time, {after - before} more) and the new version is in place");

        Dictionary<string, byte[]> third = Write(local, "zm_send", 1);
        server.Forget();
        server.RefuseOverwrite = true;
        error = Send();
        server.RefuseOverwrite = false;
        Program.Check(error == null && Same(server, remote, third) && server.Commands.Count(line => line.StartsWith("DELE ")) == third.Count,
            "a console that will not rename over a file has the old file removed first, and the send still works");

        Dictionary<string, byte[]> plain = Write(local, "zm_send", 1);
        server.Forget();
        server.Renamed = "226 Path renamed";
        error = Send();
        Console.WriteLine($"   rename answered with 226: {error?.Message ?? "ok"}");
        Program.Check(error == null && Same(server, remote, plain) && Parts(server, remote).Length == 0 && !server.Commands.Any(line => line.StartsWith("DELE ")),
            "a console that answers a rename with 226, as the common console servers do, gets every file in place and nothing is deleted");

        Dictionary<string, byte[]> twice = Write(local, "zm_send", 1);
        server.Forget();
        server.RefuseOverwrite = server.RefuseTwice = true;
        error = Send();
        server.RefuseOverwrite = server.RefuseTwice = false;
        server.Renamed = "250 renamed";
        Console.WriteLine($"   refused rename answered twice: {error?.Message ?? "ok"}");
        Program.Check(error == null && Same(server, remote, twice) && Parts(server, remote).Length == 0 && server.Commands.Count(line => line.StartsWith("DELE ")) == twice.Count,
            "a console that answers a refused rename twice does not confuse the send: the old file is removed and the new one put in place");

        Dictionary<string, byte[]> fourth = Write(local, "zm_send", 1);
        server.NoSize = true;
        lock (log)
            log.Clear();
        error = Send();
        server.NoSize = false;
        Program.Check(error == null && Same(server, remote, fourth) && log.Any(line => line.Contains("does not report file sizes")), "a console that cannot report sizes still works, and the log says the check was skipped");

        Write(local, "zm_send", 1);
        server.Truncate = $"{remote}/zm_send.xpak.part";
        error = Send();
        server.Truncate = null;
        Console.WriteLine($"   cut-off transfer: {error?.Message}");
        Program.Check(error is IOException && Same(server, remote, fourth), "a transfer the console cuts off fails, and the map on the console is untouched");

        using (var closed = new TcpListenerProbe())
        {
            Exception? dead = Try(() => ConsoleFtp.SendAsync("127.0.0.1", closed.Port, local, remote, map.MainFiles, staged, progress, _ => { }, new PauseGate(), default));
            Console.WriteLine($"   no console: {dead?.Message}");
            Program.Check(dead is IOException && dead.Message.Contains("did not answer"), "a console that is off or has no FTP server says so plainly");
        }

        Console.WriteLine("== the console: what is sent, what is on it, removing");
        string extra = Path.Combine(scratch, "ftp", "local", "zm_extra");
        Dictionary<string, byte[]> extraFiles = Write(extra, "zm_extra", 1);
        File.WriteAllText(Path.Combine(extra, "zm_extra.fidelity.json"), "{}");
        File.WriteAllText(Path.Combine(extra, "notes.txt"), "x");
        File.WriteAllBytes(Path.Combine(extra, "tool.exe"), Noise(2000));
        Directory.CreateDirectory(Path.Combine(extra, "scripts"));
        File.WriteAllBytes(Path.Combine(extra, "scripts", "menu.lua"), Noise(900));
        const string extraRemote = "/data/BO3-Customs/usermaps/zm_extra";
        server.Forget();
        error = Try(() => ConsoleFtp.SendAsync("127.0.0.1", server.Port, extra, extraRemote, ["zm_extra.ff"], staged, progress, _ => { }, new PauseGate(), default));
        int sent = server.Commands.Count(line => line.StartsWith("STOR "));
        Program.Check(error == null && Same(server, extraRemote, extraFiles) && File.Exists(server.Local(extraRemote + "/scripts/menu.lua")) && sent == extraFiles.Count + 1
            && !File.Exists(server.Local(extraRemote + "/notes.txt")) && !File.Exists(server.Local(extraRemote + "/tool.exe")) && !File.Exists(server.Local(extraRemote + "/zm_extra.fidelity.json")),
            $"only game files are sent ({sent} of {Directory.GetFiles(extra, "*", SearchOption.AllDirectories).Length}): the fidelity report, a text file and a program in the folder stay on the PC");

        Directory.CreateDirectory(server.Local("/data/BO3-Customs/mods/coolmod"));
        string stray = server.Local("/data/BO3-Customs/usermaps/stray file.txt");
        File.WriteAllText(stray, "x");
        Task<ConsoleContents> looking = Task.Run(() => ConsoleFtp.ContentsAsync("127.0.0.1", server.Port, ConsoleFtp.Root, CancellationToken.None));
        CommunityChecks.Pump(() => looking.IsCompleted);
        Console.WriteLine($"   on the console: maps {string.Join(", ", looking.IsCompletedSuccessfully ? looking.Result.Maps : [])}; mods {string.Join(", ", looking.IsCompletedSuccessfully ? looking.Result.Mods : [])}");
        Program.Check(looking.IsCompletedSuccessfully && looking.Result.Maps.SetEquals(["zm_send", "zm_extra"]) && looking.Result.Mods.SetEquals(["coolmod"]),
            "the console can be asked which maps and mods are on it; a file lying beside the folders is not counted");
        using (var bare = new MiniFtp(Path.Combine(scratch, "ftp", "bare")))
        {
            Task<ConsoleContents> none = Task.Run(() => ConsoleFtp.ContentsAsync("127.0.0.1", bare.Port, ConsoleFtp.Root, CancellationToken.None));
            CommunityChecks.Pump(() => none.IsCompleted);
            Program.Check(none.IsCompletedSuccessfully && none.Result.Maps.Count == 0 && none.Result.Mods.Count == 0 && !Directory.Exists(bare.Local("/data"))
                && !bare.Commands.Any(line => line.StartsWith("MKD ") || line.StartsWith("STOR ") || line.StartsWith("DELE ")),
                "a console with nothing on it yet lists as empty, and looking changes nothing on it");
        }

        server.Forget();
        bool refused = true;
        foreach (string folder in (string[])["/data/BO3-Customs", "/data/BO3-Customs/usermaps", "/data/BO3-Customs/usermaps/", "/data/BO3-Customs/usermaps/zm_send/snd", "/data/BO3-Customs/usermaps/..",
            "/data/BO3-Customs/usermaps/.", "/data/BO3-Customs/zone", "/data/other/zm_send", "/", "", "/mnt/usb8/BO3-Customs/usermaps/zm_send", "/mnt/usb0/usermaps/zm_send",
            "/mnt/usb0/BO3-Customs", "/mnt/hdd0/BO3-Customs/usermaps/zm_send", "/mnt/usb0/BO3-Customs/usermaps/zm_send/snd"])
        {
            Exception? no = Try(() => ConsoleFtp.RemoveAsync("127.0.0.1", server.Port, folder, CancellationToken.None));
            refused &= no is IOException && no.Message.Contains("only a map or mod folder");
        }
        Program.Check(refused && server.Commands.Length == 0 && Same(server, remote, fourth),
            "only a map's or a mod's own folder can be removed: anything above, beside or inside one is refused before the console is even contacted");
        error = Try(() => ConsoleFtp.RemoveAsync("127.0.0.1", server.Port, remote, CancellationToken.None));
        Console.WriteLine($"   removed {remote}: {error?.Message ?? "ok"}; {server.Commands.Count(line => line.StartsWith("DELE "))} files, {server.Commands.Count(line => line.StartsWith("RMD "))} folders");
        Program.Check(error == null && !Directory.Exists(server.Local(remote)) && server.Commands.Count(line => line.StartsWith("DELE ")) >= fourth.Count && server.Commands.Count(line => line.StartsWith("RMD ")) == 3
            && Same(server, extraRemote, extraFiles) && Directory.Exists(server.Local("/data/BO3-Customs/mods/coolmod")) && File.Exists(stray),
            "removing a map deletes its files and folders from the console and touches nothing else");
        error = Try(() => ConsoleFtp.RemoveAsync("127.0.0.1", server.Port, remote, CancellationToken.None));
        Program.Check(error == null, "removing something that is already gone is not an error");

        string plugin = Path.Combine(scratch, "ftp", "local", "plugin");
        Directory.CreateDirectory(plugin);
        byte[] signed = Noise(90_000);
        new byte[] { 0x4F, 0x15, 0x3D, 0x1D }.CopyTo(signed, 0);
        File.WriteAllBytes(Path.Combine(plugin, "BO3-Customs.sprx"), signed);
        File.WriteAllBytes(Path.Combine(plugin, "menu.lua"), Noise(3000));
        Exception? Plugin() => Try(() => ConsoleFtp.SendAsync("127.0.0.1", server.Port, plugin, ConsoleFtp.Root, ["BO3-Customs.sprx"], staged, progress, _ => { }, new PauseGate(), default));
        server.Sizes[ConsoleFtp.Root + "/BO3-Customs.sprx.part"] = 70_000;
        error = Plugin();
        Program.Check(error == null && File.ReadAllBytes(server.Local(ConsoleFtp.Root + "/BO3-Customs.sprx")).AsSpan().SequenceEqual(signed) && Same(server, extraRemote, extraFiles),
            $"a signed plugin, which the console reports at another size than was sent, is not mistaken for a broken transfer ({error?.Message ?? "ok"})");
        server.Sizes[ConsoleFtp.Root + "/menu.lua.part"] = 10;
        error = Plugin();
        server.Sizes.Clear();
        Program.Check(error is IOException && error.Message.Contains("arrived on the console as"), $"any other file whose size on the console differs still fails the send ({error?.Message})");

        Console.WriteLine("== the console: drives");
        Program.Check(ConsoleFtp.RootOf(null) == "/data/BO3-Customs" && ConsoleFtp.RootOf("usb0") == "/mnt/usb0/BO3-Customs" && ConsoleFtp.RootOf("ext7") == "/mnt/ext7/BO3-Customs"
            && ConsoleFtp.RootOf("usb8") == "/data/BO3-Customs" && ConsoleFtp.RootOf("../x") == "/data/BO3-Customs" && ConsoleFtp.Drives.Count == 16
            && ConsoleFtp.Folder(map, "usb3") == "/mnt/usb3/BO3-Customs/usermaps/zm_send" && ConsoleFtp.Folder(mod, "ext1") == "/mnt/ext1/BO3-Customs/mods/coolmod"
            && ConsoleFtp.StorageName(null) == "Console storage" && ConsoleFtp.StorageName("usb3") == "USB 3" && ConsoleFtp.StorageName("ext0") == "Extended 0",
            "maps and mods can go to the console's own storage, USB 0-7 or extended storage 0-7, and nowhere else");
        const string trial = "ffporter-write-test.tmp";
        Directory.CreateDirectory(server.Local("/mnt/usb0/PS4"));
        Directory.CreateDirectory(server.Local("/mnt/usb1/PS4"));
        Directory.CreateDirectory(server.Local("/mnt/usb3"));
        server.ReadOnly["/mnt/usb1/"] = true;
        Dictionary<string, byte[]> driveFiles = Write(local, "zm_send", 1);
        Exception? Drive(string storage) =>
            Try(() => ConsoleFtp.SendAsync("127.0.0.1", server.Port, local, ConsoleFtp.Folder(map, storage), map.MainFiles, staged, progress, _ => { }, new PauseGate(), default));
        const string usbRemote = "/mnt/usb0/BO3-Customs/usermaps/zm_send";
        server.Forget();
        error = Drive("usb0");
        string[] tried = [.. server.Commands.Where(line => line.Contains(trial))];
        Console.WriteLine($"   write test: {string.Join(" | ", tried)}");
        Program.Check(error == null && Same(server, usbRemote, driveFiles) && Directory.Exists(server.Local("/mnt/usb0/PS4")) && !Directory.Exists(server.Local(remote)),
            $"a map sent to USB 0 lands in {usbRemote}, beside what is already on the drive ({error?.Message ?? "ok"})");
        Program.Check(tried.SequenceEqual([$"STOR /mnt/usb0/{trial}", $"SIZE /mnt/usb0/{trial}", $"DELE /mnt/usb0/{trial}"]) && !File.Exists(server.Local($"/mnt/usb0/{trial}"))
            && Array.IndexOf(server.Commands, tried[2]) < Array.FindIndex(server.Commands, line => line.StartsWith("MKD ")),
            "before anything else, a small test file is written to the drive, checked and deleted again");
        server.Forget();
        Exception? unplugged = Drive("usb1"), absent = Drive("ext2");
        Console.WriteLine($"   not usable: {unplugged?.Message} | {absent?.Message}");
        Program.Check(unplugged is IOException && unplugged.Message == "USB 1 is not usable" && absent is IOException && absent.Message == "Extended 2 is not usable"
            && Directory.GetFileSystemEntries(server.Local("/mnt/usb1")).Length == 1 && !Directory.Exists(server.Local("/mnt/ext2"))
            && !server.Commands.Any(line => line.StartsWith("MKD ")) && server.Commands.Count(line => line.StartsWith("STOR ")) == 1,
            "a drive that shows up but cannot be written to, or is not there at all, is refused before anything is made on it");
        error = Drive("usb3");
        Program.Check(error == null && Same(server, "/mnt/usb3/BO3-Customs/usermaps/zm_send", driveFiles) && !File.Exists(server.Local($"/mnt/usb3/{trial}")),
            "an empty drive that can be written to is fine");
        Task<IReadOnlyList<string>> some = Task.Run(() => ConsoleFtp.UsableAsync("127.0.0.1", server.Port, ["usb1", "usb3", "ext5", "nonsense"], true, CancellationToken.None));
        CommunityChecks.Pump(() => some.IsCompleted);
        Program.Check(some.IsCompletedSuccessfully && some.Result.SequenceEqual(["usb3"]), "any set of drives can be tested this way; only the ones that took the test file come back");
        Task<ConsoleLook> drives = Task.Run(() => ConsoleFtp.TestAsync("127.0.0.1", server.Port, ConsoleFtp.RootOf("usb0"), CancellationToken.None));
        CommunityChecks.Pump(() => drives.IsCompleted);
        Task<ConsoleContents> onDrive = Task.Run(() => ConsoleFtp.ContentsAsync("127.0.0.1", server.Port, ConsoleFtp.RootOf("usb0"), CancellationToken.None));
        CommunityChecks.Pump(() => onDrive.IsCompleted);
        Program.Check(drives.IsCompletedSuccessfully && drives.Result.Found && drives.Result.Drives.SequenceEqual(["usb0", "usb3"]) && onDrive.IsCompletedSuccessfully
            && onDrive.Result.Maps.SetEquals(["zm_send"]) && onDrive.Result.Mods.Count == 0 && !Directory.EnumerateFiles(server.Local("/mnt"), trial, SearchOption.AllDirectories).Any(),
            "Test says which drives can really be used, leaving no test file behind, and a drive's maps can be listed like the console's own");
        error = Try(() => ConsoleFtp.RemoveAsync("127.0.0.1", server.Port, usbRemote, CancellationToken.None));
        Program.Check(error == null && !Directory.Exists(server.Local(usbRemote)) && Directory.Exists(server.Local("/mnt/usb0/PS4")) && Same(server, extraRemote, extraFiles),
            "a map can be removed from a drive too, and nothing else on the drive or the console is touched");
    }
}

internal sealed class TcpListenerProbe : IDisposable
{
    public int Port { get; }

    public TcpListenerProbe()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
    }

    public void Dispose()
    {
    }
}
