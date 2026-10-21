using System.IO;
using System.Windows;

namespace CommunityUploader;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string[] args = e.Args;
        var window = new MainWindow();

        if (args.Length >= 2 && args[0] == "--shot")
        {
            window.Quiet = true;
            window.StartFolder = args.Length >= 3 && args[2] != "-" ? Path.GetFullPath(args[2]) : "";
            await window.StartAsync();
            if (args.Length >= 4 && args[3] == "published")
                window.ShowPublished();
            window.Render(Path.GetFullPath(args[1]));
            Shutdown(0);
            return;
        }

        if (args.Length >= 3 && args[0] == "--selftest")
        {
            bool ok = false;
            try
            {
                ok = await SelfTest.RunAsync(window, Path.GetFullPath(args[2]), Path.GetFullPath(args[1]));
            }
            catch (Exception error)
            {
                File.AppendAllText(Path.GetFullPath(args[1]), "CRASHED: " + error + "\n");
            }
            if (args.Length >= 4)
                window.Render(Path.GetFullPath(args[3]));
            Shutdown(ok ? 0 : 1);
            return;
        }

        MainWindow = window;
        window.Closed += (_, _) => Shutdown(0);
        window.Show();
        await window.StartAsync();
    }
}
