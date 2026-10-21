using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using FFPorter.Desktop;
using FFPorter.Desktop.Models;
using FFPorter.Desktop.Services;

internal static class ExtraChecks
{
    public static void Run(string scratch)
    {
        Console.WriteLine("== smaller things");
        string site = Path.Combine(scratch, "extras", "site");
        Directory.CreateDirectory(Path.Combine(site, "t7"));
        object[] entries =
        [
            new { name = "zm_alpha", title = "[ZM] Alpha", file = "a.zip" },
            new { name = "cp_beta", title = "[cp]   Beta Story", file = "b.zip" },
            new { name = "mp_gamma", title = "[MP] Gamma", file = "c.zip" },
            new { name = "delta", title = "[Mod] Delta Pack", type = "mod", file = "d.zip" },
            new { name = "zm_only", title = "[ZM]", file = "e.zip" },
            new { name = "zm_inside", title = "Late [ZM] Tag", file = "f.zip" },
            new { name = "zm_other", title = "[HD] Remaster", file = "g.zip" },
        ];
        File.WriteAllText(Path.Combine(site, "t7", "catalog.json"), JsonSerializer.Serialize(new { name = "Tags", maps = entries }), new UTF8Encoding(true));
        using var server = new MiniServer(site);
        CommunityCatalog.TryAddress($"http://127.0.0.1:{server.Port}/t7/catalog.json", out Uri list);
        Task<IReadOnlyList<CommunityMap>> loading = Task.Run(() => CommunityCatalog.LoadAsync(list, _ => { }, CancellationToken.None));
        CommunityChecks.Pump(() => loading.IsCompleted, 20);
        string[] titles = loading.IsCompletedSuccessfully ? [.. loading.Result.Select(map => map.Title)] : [];
        Console.WriteLine("   titles: " + string.Join(" | ", titles));
        Program.Check(titles.SequenceEqual(["Alpha", "Beta Story", "Gamma", "Delta Pack", "[ZM]", "Late [ZM] Tag", "[HD] Remaster"]),
            "a leading [ZM], [CP], [MP] or [MOD] is dropped from a title; anything else is left alone");

        var dialog = new SettingsWindow(null);
        var build = (TextBlock)dialog.FindName("BuildText");
        Program.Check(build != null && build.Text == "" && AppUpdate.Built == null && ((TextBlock)dialog.FindName("StateText")).Visibility == System.Windows.Visibility.Collapsed
            && ((Button)dialog.FindName("CheckButton")).Visibility == System.Windows.Visibility.Collapsed,
            "Settings shows no build line and no update check where the build time is unknown (as here, outside the packed app)");
        DateTimeOffset? stamp = AppUpdate.BuiltOf(typeof(T7Edition).Assembly);
        Console.WriteLine($"   the app's own line would read: Built {stamp?.ToLocalTime():d MMM yyyy, HH:mm}");
    }
}
