using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace FFPorter.Desktop.Services;

public sealed class SingleInstance : IDisposable
{
    private const int Knock = 2000, AnyProcess = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int process);

    private readonly Mutex? _mutex;
    private readonly string _pipe;
    private readonly CancellationTokenSource _closing = new();
    private readonly Lock _gate = new();
    private readonly List<string[]> _early = [];
    private Action<string[]>? _received;

    private SingleInstance(Mutex? mutex, string pipe)
    {
        _mutex = mutex;
        _pipe = pipe;
        if (mutex != null)
            _ = Task.Run(ListenAsync);
    }

    public bool IsFirst => _mutex != null;

    public Action<string[]>? Received
    {
        get => _received;
        set
        {
            string[][] early = [];
            lock (_gate)
            {
                _received = value;
                if (value != null)
                {
                    early = [.. _early];
                    _early.Clear();
                }
            }
            foreach (string[] batch in early)
                value!(batch);
        }
    }

    public static SingleInstance? Claim(string name, IReadOnlyList<string> arguments)
    {
        string key = $"PS4FFPorter.{name}.{Environment.UserName}";
        var mutex = new Mutex(true, @"Local\" + key, out bool first);
        if (first)
            return new SingleInstance(mutex, key);
        mutex.Dispose();
        return Hand(key, arguments) ? null : new SingleInstance(null, key);
    }

    private static bool Hand(string key, IReadOnlyList<string> arguments)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", key, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(Knock);
            AllowSetForegroundWindow(AnyProcess);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false));
            foreach (string argument in arguments)
                writer.WriteLine(argument);
            writer.Flush();
            return true;
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task ListenAsync()
    {
        while (!_closing.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(_pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_closing.Token);
                using var reader = new StreamReader(pipe, new UTF8Encoding(false));
                var lines = new List<string>();
                while (await reader.ReadLineAsync(_closing.Token) is { } line)
                {
                    if (line.Length > 0)
                        lines.Add(line);
                }
                Deliver([.. lines]);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(Knock);
            }
        }
    }

    private void Deliver(string[] lines)
    {
        Action<string[]>? received;
        lock (_gate)
        {
            received = _received;
            if (received == null)
                _early.Add(lines);
        }
        received?.Invoke(lines);
    }

    public void Dispose()
    {
        _closing.Cancel();
        _mutex?.Dispose();
    }
}
