using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using FFPorter.Core;
using FFPorter.Core.Common.Tools;
using FFPorter.Core.T7;
using FFPorter.Core.T7.Scripts;

internal static class ScriptChecks
{
    private const uint BuiltinSpace = 775726276;
    private const int Pointer = 1, Call = 2, Method = 4;

    private static byte[] Script(params (string Name, int Parameters, int Kind)[] imports)
    {
        var buffer = new byte[0x48 + 12 * imports.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x0C), 0x48);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x20), 0x48);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x24), 0x48);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0x3C), (ushort)imports.Length);
        for (int i = 0; i < imports.Length; i++)
        {
            Span<byte> entry = buffer.AsSpan(0x48 + 12 * i, 12);
            BinaryPrimitives.WriteUInt32LittleEndian(entry, T7GscBuiltins.Hash(imports[i].Name));
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], BuiltinSpace);
            entry[10] = (byte)imports[i].Parameters;
            entry[11] = (byte)imports[i].Kind;
        }
        return buffer;
    }

    private static string? Refused(T7GscBuiltins builtins, string name, params (string Name, int Parameters, int Kind)[] imports)
    {
        try
        {
            builtins.Apply(Script(imports), name);
            return null;
        }
        catch (InvalidDataException refused)
        {
            return refused.Message;
        }
    }

    private static string? Source()
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
        {
            string path = Path.Combine(folder.FullName, "SPRX", "t7_builtins.cpp");
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    public static void Run()
    {
        Console.WriteLine("== scripts: PC-only builtins the plugin provides");
        string table = ToolData.Locate(Workspace.Locate(), "t7_gsc/ps4_builtins.json");
        if (!File.Exists(table))
        {
            Program.Check(false, $"the builtin table is there to test with ({table})");
            return;
        }
        T7GscBuiltins builtins = T7GscBuiltins.Load(table);

        byte[] script = Script(("modvar", 2, Call), ("isusingmods", 0, Call), ("getdvarstring", 1, Call));
        byte[] before = (byte[])script.Clone();
        IReadOnlyList<string> provided = builtins.Apply(script, "scripts/zm/a.gsc");
        Program.Check(provided.SequenceEqual(["modvar", "isusingmods"]) && script.AsSpan().SequenceEqual(before),
            $"modvar and isusingmods calls are reported and left as they are ({string.Join(", ", provided)})");
        Program.Check(builtins.Apply(Script(("modvar", 2, Call), ("clearallcharactertables", 0, Call)), "scripts/zm/a.csc").Count == 2,
            "client scripts get modvar and clearallcharactertables too");
        Program.Check(builtins.Apply(Script(("bottogglegodmode", 0, Method), ("bottoggledemigodmode", 0, Method), ("__protected__notelootconsume", 2, Method),
                ("__protected__reportnotedloot", 0, Method), ("__protected__setbgbunlocked", 1, Call), ("__protected__getbgbunlocked", 0, Call)), "scripts/zm/b.gsc").Count == 6,
            "the bot and __protected__ builtins are provided to server scripts");
        Program.Check(builtins.Apply(Script(("modvar", 0, Pointer), ("bottogglegodmode", 0, Pointer)), "scripts/zm/c.gsc").Count == 2,
            "a pointer to a provided builtin is fine whatever its argument count");

        string? arguments = Refused(builtins, "scripts/zm/d.gsc", ("modvar", 1, Call));
        Program.Check(arguments != null && arguments.Contains("modvar with 1 arguments; it takes 2 to 2"), $"modvar with one argument is refused ({arguments})");
        Program.Check(Refused(builtins, "scripts/zm/e.csc", ("bottogglegodmode", 0, Method)) is { } client && client.Contains("only the PC build has"),
            "a client script calling the bot toggle is refused: the plugin has it for server scripts only");
        Program.Check(Refused(builtins, "scripts/zm/f.gsc", ("__protected__getbgbunlocked", 0, Method)) != null && Refused(builtins, "scripts/zm/f.gsc", ("bottogglegodmode", 0, Call)) != null,
            "a function called as a method, or a method as a function, is refused");

        string? source = Source();
        if (source == null)
        {
            Console.WriteLine("   plugin source not found from here; the table was not compared with it");
            return;
        }
        string text = File.ReadAllText(source);
        var plugin = new Dictionary<(string Table, uint Hash), (int Min, int Max)>();
        foreach ((string array, string[] tables) in ((string, string[])[])[("k_commonFunctions", ["gsc_function", "csc_function"]), ("k_gscFunctions", ["gsc_function"]), ("k_gscMethods", ["gsc_method"])])
        {
            Match body = Regex.Match(text, $@"{array}\[\]\s*=\s*\{{(.*?)\}};", RegexOptions.Singleline);
            foreach (Match row in Regex.Matches(body.Groups[1].Value, @"\{\s*0x([0-9A-Fa-f]{8}),[^,]+,\s*(\d+),\s*(\d+)\s*\}"))
                foreach (string name in tables)
                    plugin[(name, uint.Parse(row.Groups[1].Value, NumberStyles.HexNumber))] = (int.Parse(row.Groups[2].Value), int.Parse(row.Groups[3].Value));
        }
        var listed = new Dictionary<(string Table, uint Hash), (int Min, int Max)>();
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(table));
        bool named = true;
        foreach (JsonProperty entry in document.RootElement.GetProperty("pc_only").EnumerateObject())
        {
            uint hash = uint.Parse(entry.Name, NumberStyles.HexNumber);
            named &= T7GscBuiltins.Hash(entry.Value.GetProperty("name").GetString()!) == hash;
            foreach (JsonProperty range in entry.Value.GetProperty("plugin").EnumerateObject())
                listed[(range.Name, hash)] = (range.Value[0].GetInt32(), range.Value[1].GetInt32());
        }
        Program.Check(named, "every PC-only builtin is listed under the hash of its name");
        Program.Check(plugin.Count == 12 && listed.Count == plugin.Count && listed.All(entry => plugin.TryGetValue(entry.Key, out var range) && range == entry.Value),
            $"the plugin source and the converter's table list the same builtins with the same argument counts ({plugin.Count} and {listed.Count})");
        foreach (string kind in (string[])["gsc_function", "gsc_method", "csc_function", "csc_method"])
        {
            JsonElement stock = document.RootElement.GetProperty(kind);
            Program.Check(!listed.Keys.Any(key => key.Table == kind && stock.TryGetProperty(key.Hash.ToString("x8"), out _)), $"none of them is already a PS4 builtin ({kind})");
        }
    }

    public static void Acts(string scratch)
    {
        Console.WriteLine("== acts: runs without the Visual C++ runtime installed");
        string? published = null;
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder != null && published == null; folder = folder.Parent)
        {
            string candidate = Path.Combine(folder.FullName, "publish", "work", "tools", "acts", "acts.exe");
            if (File.Exists(candidate))
                published = candidate;
        }
        if (published == null)
        {
            Console.WriteLine("  skipped: no downloaded acts to test with (publish/work/tools/acts)");
            return;
        }
        string? work = Environment.GetEnvironmentVariable(Workspace.WorkVariable);
        string? acts = Environment.GetEnvironmentVariable(T7Paths.ActsVariable);
        string root = Path.Combine(scratch, "acts_runtime");
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
            string own = Path.Combine(root, "work", "tools", "acts");
            string other = Path.Combine(root, "elsewhere");
            foreach (string folder in (string[])[own, other])
            {
                Directory.CreateDirectory(folder);
                foreach (string name in (string[])["acts.exe", "acts-common.dll", "acts-bo3.dll"])
                    File.Copy(Path.Combine(Path.GetDirectoryName(published)!, name), Path.Combine(folder, name));
            }
            Environment.SetEnvironmentVariable(Workspace.WorkVariable, Path.Combine(root, "work"));
            Environment.SetEnvironmentVariable(T7Paths.ActsVariable, null);
            var log = new List<string>();
            string? found = ActsInstall.Ensure(Workspace.Locate(), log.Add, download: false);
            string[] runtime = ["msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll"];
            bool placed = runtime.All(name => File.Exists(Path.Combine(own, name))
                && new FileInfo(Path.Combine(own, name)).Length == new FileInfo(ToolData.Locate(Workspace.Locate(), "acts_runtime/" + name)).Length);
            Program.Check(placed, "the shipped Visual C++ runtime is copied beside the tool's own acts.exe");
            Program.Check(found != null && Path.GetFullPath(found) == Path.GetFullPath(Path.Combine(own, "acts.exe")) && log.Count == 0,
                $"that acts starts and is used for the script check ({string.Join(" | ", log)})");
            File.WriteAllBytes(Path.Combine(other, "msvcp140.dll"), new byte[4096]);
            Environment.SetEnvironmentVariable(T7Paths.ActsVariable, Path.Combine(other, "acts.exe"));
            log.Clear();
            string? broken = ActsInstall.Ensure(Workspace.Locate(), log.Add, download: false);
            Program.Check(broken == null && log.Count == 1 && log[0].Contains("could not start") && log[0].Contains("converted scripts will not be checked"),
                $"an acts that Windows cannot start is reported once and the check is skipped, not failed ({string.Join(" | ", log)})");
            Program.Check(!File.Exists(Path.Combine(other, "vcruntime140.dll")), "nothing is copied beside an acts.exe the user chose themselves");
            Program.Check(ActsInstall.StartFailure(unchecked((int)0xC0000135))?.Contains("Visual C++ runtime") == true && ActsInstall.StartFailure(1) == null,
                "a missing-DLL exit code reads as the missing Visual C++ runtime");
        }
        finally
        {
            Environment.SetEnvironmentVariable(Workspace.WorkVariable, work);
            Environment.SetEnvironmentVariable(T7Paths.ActsVariable, acts);
            try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
