using System.Diagnostics;

namespace FFPorter.Core.Common.Tools;

public static class ProcessRunner
{
    private const int WaitSlice = 250;

    public sealed record Result(int ExitCode, string Output, bool TimedOut);

    public static Result Run(string tool, IEnumerable<string> arguments, string workingDirectory, TimeSpan timeout)
    {
        var start = new ProcessStartInfo(tool)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new IOException($"could not start {tool}");
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        var active = ActiveClock.StartNew();
        bool exited = process.WaitForExit(WaitSlice);
        while (!exited && active.Elapsed < timeout)
            exited = process.WaitForExit(WaitSlice);
        if (!exited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            return new Result(-1, "", true);
        }
        return new Result(process.ExitCode, output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult(), false);
    }
}
