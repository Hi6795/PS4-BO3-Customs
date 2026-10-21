using System.Security.Cryptography;
using System.Text;

namespace FFPorter.Core.Common.Tools;

public static class CacheFiles
{
    public static string Temporary(string path, string suffix = ".tmp") =>
        $"{path}.{Environment.ProcessId}-{Environment.CurrentManagedThreadId}{suffix}";

    public static void Publish(string temporary, string path)
    {
        try
        {
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (!File.Exists(path))
                throw;
            Delete(temporary);
        }
    }

    public static void WriteAllBytes(string path, byte[] bytes)
    {
        string temporary = Temporary(path);
        File.WriteAllBytes(temporary, bytes);
        Publish(temporary, path);
    }

    public static void WriteAllText(string path, string text)
    {
        string temporary = Temporary(path);
        File.WriteAllText(temporary, text);
        Publish(temporary, path);
    }

    public static void WriteAllLines(string path, IEnumerable<string> lines)
    {
        string temporary = Temporary(path);
        File.WriteAllLines(temporary, lines);
        Publish(temporary, path);
    }

    public static void Copy(string source, string target)
    {
        var from = new FileInfo(source);
        var to = new FileInfo(target);
        if (to.Exists && to.Length == from.Length && to.LastWriteTimeUtc >= from.LastWriteTimeUtc)
            return;
        string temporary = Temporary(target);
        File.Copy(source, temporary, overwrite: true);
        Publish(temporary, target);
    }

    public static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static IDisposable Lock(string path) => new SharedLock(path);

    private sealed class SharedLock : IDisposable
    {
        private readonly Mutex _mutex;
        private bool _held;

        public SharedLock(string path)
        {
            string key = Path.GetFullPath(path).ToLowerInvariant();
            _mutex = new Mutex(false, @"Local\FFPorter." + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..40]);
            try
            {
                _mutex.WaitOne();
            }
            catch (AbandonedMutexException)
            {
            }
            _held = true;
        }

        public void Dispose()
        {
            if (_held)
            {
                _held = false;
                _mutex.ReleaseMutex();
            }
            _mutex.Dispose();
        }
    }
}
