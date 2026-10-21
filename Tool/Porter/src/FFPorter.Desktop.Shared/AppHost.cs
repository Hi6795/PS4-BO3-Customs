using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using FFPorter.Core.Common.Native;
using FFPorter.Desktop.Services;

namespace FFPorter.Desktop;

public static class AppHost
{
    public const string BackendVerb = "--backend";

    public static int Run(Edition edition, string[] args)
    {
        if (args.Length > 0 && args[0] == AppUpdate.Verb)
        {
            return AppUpdate.Apply(args[1..], reason => MessageBox.Show(
                $"{edition.Title} could not be started again: {reason}. Start it yourself.", edition.Title, MessageBoxButton.OK, MessageBoxImage.Warning));
        }
        Environment.SetEnvironmentVariable(NativeProcess.HostVariable, Environment.ProcessPath);
        Edition.Use(edition);
        if (args.Length > 0 && (args[0] == BackendVerb || args[0] == NativeChild.Verb))
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
            using var error = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };
            return args[0] == NativeChild.Verb
                ? NativeChild.Main(args[1..], output, error)
                : edition.RunBackend(args[1..], output, error);
        }
        bool failed = args.Length > 0 && args[0] == AppUpdate.Failed;
        if (failed)
            args = args[1..];
        using SingleInstance? only = SingleInstance.Claim(edition.Codename, [.. args.Select(Full)]);
        if (only == null)
            return 0;
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            try
            {
                MessageDialog.Inform(app.MainWindow, DialogKind.Error, "Something went wrong", e.Exception.Message);
            }
            catch (Exception)
            {
                MessageBox.Show(e.Exception.Message, edition.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        Theme.ThemeManager.Initialize(app);
        var window = new MainWindow();
        if (args.Length > 0)
            window.AddPaths(args);
        if (failed)
            window.UpdateFailed();
        only.Received = paths => window.Dispatcher.BeginInvoke(() => window.Receive(paths));
        return app.Run(window);
    }

    private static string Full(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException)
        {
            return path;
        }
    }
}
