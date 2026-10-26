#!/usr/bin/env python3
"""Compile and execute the production workshop-thumbnail resolver without WPF."""
from pathlib import Path
import os
import subprocess
import tempfile


root = Path(__file__).resolve().parents[1]
source = (root / "src/FFPorter.Core.T7/Port/T7MapPort.cs").read_text(encoding="utf-8")
begin = source.index("    internal static string? WorkshopThumbnail(")
end = source.index("\n    private static", begin)
resolver = source[begin:end]
begin = source.index("    private static IEnumerable<(string Source, string Name)> UsermapPackageFiles(")
end = source.index("\n    private static void CopyUsermapFiles(", begin)
packages = source[begin:end].replace("private static IEnumerable", "internal static IEnumerable", 1)

assert 'UsermapPackageFiles(roots)' in source
assert 'CopyUsermapFiles(T7MapPortOptions options' in source
assert source.count('UsermapPackageFiles(roots)') >= 2

fixture = '''
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

static class T7MapPort {
    private static readonly string[] UsermapFiles = ["workshop.json", "previewimage.png", "loadingimage.png"];
PRODUCTION
PACKAGES
}

static class Program {
    static void Expect(bool valid, string what) { if (!valid) throw new Exception(what); }

    static int Main() {
        string folder = Path.Combine(Path.GetTempPath(), "bo3-map-thumbnail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string manifest = Path.Combine(folder, "workshop.json");
        try {
            Expect(T7MapPort.WorkshopThumbnail(manifest) == null, "missing json");
            File.WriteAllText(manifest, "{bad json}");
            Expect(T7MapPort.WorkshopThumbnail(manifest) == null, "invalid json");
            File.WriteAllText(manifest, "{} ");
            Expect(T7MapPort.WorkshopThumbnail(manifest) == null, "no thumbnail");
            File.WriteAllText(manifest, "{\\"Thumbnail\\":\\"images/map_icon.png\\"}");
            Expect(T7MapPort.WorkshopThumbnail(manifest) == "map_icon.png", "relative icon path");
            File.WriteAllText(Path.Combine(folder, "map_icon.png"), "image");
            var paths = T7MapPort.UsermapPackageFiles([folder]).ToArray();
            Expect(paths.Length == 2 && paths.Any(p => p.Name == "map_icon.png"), "custom thumbnail copied");
            File.WriteAllText(Path.Combine(folder, "previewimage.png"), "standard");
            paths = T7MapPort.UsermapPackageFiles([folder]).ToArray();
            Expect(paths.Length == 2 && paths.Any(p => p.Name == "previewimage.png") &&
                   !paths.Any(p => p.Name == "map_icon.png"), "standard preview wins");
            File.Delete(Path.Combine(folder, "previewimage.png"));
            File.WriteAllText(manifest, "{\\"Thumbnail\\":\\"..\\\\\\\\other\\\\\\\\icon.PNG\\"}");
            Expect(T7MapPort.WorkshopThumbnail(manifest) == "icon.PNG", "windows-style basename");
            File.WriteAllText(manifest, "{\\"Thumbnail\\":\\"../hidden.bmp\\"}");
            Expect(T7MapPort.WorkshopThumbnail(manifest) == null, "non-PNG denied");
            File.WriteAllText(manifest, "{\\"Thumbnail\\":\\"..\\"}");
            Expect(T7MapPort.WorkshopThumbnail(manifest) == null, "dot-dot denied");
            File.WriteAllText(manifest, "{\\"Thumbnail\\":\\"folder/\\"}");
            Expect(T7MapPort.WorkshopThumbnail(manifest) == null, "empty basename denied");
            File.WriteAllText(manifest, "{\\"Thumbnail\\":\\"a:b.png\\"}");
            Expect(T7MapPort.WorkshopThumbnail(manifest) == null, "invalid character denied");
            File.WriteAllText(manifest, "{\\"Thumbnail\\":\\"" + new string('a', 91) + ".png\\"}");
            Expect(T7MapPort.WorkshopThumbnail(manifest)?.Length == 95, "maximum console filename");
            File.WriteAllText(manifest, "{\\"Thumbnail\\":\\"" + new string('a', 92) + ".png\\"}");
            Expect(T7MapPort.WorkshopThumbnail(manifest) == null, "console filename overflow denied");
            File.WriteAllText(manifest, "{\\"Thumbnail\\":\\"" + new string('a', 260) + ".png\\"}");
            Expect(T7MapPort.WorkshopThumbnail(manifest) == null, "oversized basename denied");
            File.WriteAllText(manifest, new string(' ', 65537));
            Expect(T7MapPort.WorkshopThumbnail(manifest) == null, "oversized json denied");
            Console.WriteLine("PASS: workshop map icons parse safely with path normalization, invalid data and size bounds");
            return 0;
        } finally { Directory.Delete(folder, true); }
    }
}
'''.replace("PRODUCTION", resolver).replace("PACKAGES", packages)

with tempfile.TemporaryDirectory(prefix="bo3-thumbnail-compile-") as directory:
    temp = Path(directory)
    (temp / "ThumbnailHarness.csproj").write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings><RestoreIgnoreFailedSources>true</RestoreIgnoreFailedSources>
</PropertyGroup></Project>''', encoding="utf-8")
    (temp / "Program.cs").write_text(fixture, encoding="utf-8")
    environment = dict(os.environ, DOTNET_CLI_HOME=str(temp), DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1",
                       DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    subprocess.run(["dotnet", "run", "--project", str(temp / "ThumbnailHarness.csproj"),
                    "--configuration", "Release"], check=True, timeout=120, env=environment)
