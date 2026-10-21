using System.Globalization;

namespace FFPorter.Core.Common.Native;

public enum CpuTrapMode
{
    Emulate,
    Collect,
}

public sealed unsafe class CpuTraps
{
    public const uint IllegalInstruction = 0xC000001D;

    private readonly Dictionary<ulong, byte[]> _originals = [];
    private readonly HashSet<ulong> _collected = [];
    private readonly HashSet<string> _unsupported = [];
    private readonly byte[] _scratch = new byte[16];

    public CpuTrapMode Mode { get; set; } = CpuTrapMode.Emulate;

    public string? CollectPath { get; set; }

    public int Count => _originals.Count;

    public long Emulated { get; private set; }

    public string? LastUnsupported { get; private set; }

    public void Trap(NativeHost host, ulong address, int length)
    {
        if (length < 2 || length > 15 || _originals.ContainsKey(address) || !host.TryRegion(address, out MemoryRange range)
            || (ulong)length > range.End - address)
            return;
        _originals[address] = host.Read(address, (long)length);
        host.Write(address, [0x0F, 0x0B]);
    }

    public void Flush() => Kernel32.FlushInstructionCache(Kernel32.GetCurrentProcess(), 0, 0);

    internal bool Handle(NativeHost host, nint context)
    {
        ulong rip = *(ulong*)(context + CpuContext.Rip);
        ReadOnlySpan<byte> code;
        if (_originals.TryGetValue(rip, out byte[]? original))
        {
            if (Mode == CpuTrapMode.Collect)
            {
                fixed (byte* source = original)
                    Buffer.MemoryCopy(source, (void*)rip, original.Length, original.Length);
                Kernel32.FlushInstructionCache(Kernel32.GetCurrentProcess(), (nint)rip, (nuint)original.Length);
                if (_collected.Add(rip) && CollectPath != null)
                    File.AppendAllText(CollectPath, rip.ToString("x", CultureInfo.InvariantCulture) + "\n");
                return true;
            }
            code = original;
        }
        else
        {
            if (!host.TryRegion(rip, out MemoryRange range))
                return false;
            int available = (int)Math.Min(15UL, range.End - rip);
            new ReadOnlySpan<byte>((void*)rip, available).CopyTo(_scratch);
            code = _scratch.AsSpan(0, available);
        }
        if (CpuEmulator.Execute(context, code, rip, out string? problem))
        {
            Emulated++;
            return true;
        }
        LastUnsupported = $"{problem} at {NativeHost.Hex(rip)} ({Convert.ToHexString(code)})";
        if (_unsupported.Add(LastUnsupported))
            host.WriteLog(new JsonMap { ["cpu_emulation"] = LastUnsupported });
        return false;
    }
}
