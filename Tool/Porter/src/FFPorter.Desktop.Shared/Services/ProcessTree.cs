using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FFPorter.Desktop.Services;

public static class ProcessTree
{
    private const int Passes = 6;
    private const uint SuspendResume = 0x0800, QueryLimited = 0x1000, SnapProcesses = 0x2;

    public sealed class Hold : IDisposable
    {
        private readonly List<nint> _handles = [];

        public int Count => _handles.Count;

        internal void Add(nint handle) => _handles.Add(handle);

        public void Dispose()
        {
            for (int index = _handles.Count - 1; index >= 0; index--)
            {
                NtResumeProcess(_handles[index]);
                CloseHandle(_handles[index]);
            }
            _handles.Clear();
        }
    }

    public static Hold? Suspend(int root)
    {
        var hold = new Hold();
        var held = new HashSet<int>();
        if (!Freeze(root, hold))
            return null;
        held.Add(root);
        for (int pass = 0; pass < Passes; pass++)
        {
            int before = held.Count;
            foreach (int child in Descendants(root))
            {
                if (!held.Contains(child) && Freeze(child, hold))
                    held.Add(child);
            }
            if (held.Count == before)
                break;
        }
        return hold;
    }

    private static bool Freeze(int id, Hold hold)
    {
        nint handle = OpenProcess(SuspendResume | QueryLimited, false, id);
        if (handle == 0)
            return false;
        if (NtSuspendProcess(handle) != 0)
        {
            CloseHandle(handle);
            return false;
        }
        hold.Add(handle);
        return true;
    }

    public static IReadOnlyList<int> Descendants(int root)
    {
        var children = new Dictionary<int, List<int>>();
        nint snapshot = CreateToolhelp32Snapshot(SnapProcesses, 0);
        if (snapshot == -1)
            return [];
        try
        {
            var entry = new Entry { Size = (uint)Marshal.SizeOf<Entry>() };
            for (bool more = Process32FirstW(snapshot, ref entry); more; more = Process32NextW(snapshot, ref entry))
            {
                if (!children.TryGetValue((int)entry.Parent, out List<int>? list))
                    children[(int)entry.Parent] = list = [];
                list.Add((int)entry.Id);
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }
        var found = new List<int>();
        var seen = new HashSet<int> { root };
        var queue = new Queue<int>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            int parent = queue.Dequeue();
            if (!children.TryGetValue(parent, out List<int>? list) || Born(parent) is not DateTime before)
                continue;
            foreach (int child in list)
            {
                if (child == 0 || !seen.Add(child) || Born(child) is not DateTime born || born < before)
                    continue;
                found.Add(child);
                queue.Enqueue(child);
            }
        }
        return found;
    }

    private static DateTime? Born(int id)
    {
        try
        {
            using Process process = Process.GetProcessById(id);
            return process.StartTime;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Entry
    {
        public uint Size, Usage, Id;
        public nint Heap;
        public uint Module, Threads, Parent;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string File;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int id);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint id);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(nint snapshot, ref Entry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(nint snapshot, ref Entry entry);

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(nint handle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(nint handle);
}
