using System.ComponentModel;
using System.Runtime.InteropServices;
using FFPorter.Core.Common.Tools;

namespace FFPorter.Core.T7.Shaders;

public static class T7GnmSdk
{
    public static bool Available(string? sdkBin = null)
    {
        sdkBin ??= Ps4Sdk.BinDirectory();
        return File.Exists(Path.Combine(sdkBin, Ps4Sdk.GnmxLibraryName)) && File.Exists(Path.Combine(sdkBin, Ps4Sdk.GnmLibraryName));
    }

    public static byte[] FetchShader(ReadOnlySpan<byte> vsHeader, string? sdkBin = null)
    {
        sdkBin = Load(sdkBin);
        byte[] header = new byte[vsHeader.Length + 1];
        vsHeader.CopyTo(header);
        string gnmx = Path.Combine(sdkBin, Ps4Sdk.GnmxLibraryName);
        uint size = GnmSdk.FetchShaderSize(gnmx, header);
        if (size == 0 || size >= 65536)
            throw new InvalidDataException($"the SDK sizes a fetch shader at {size} bytes");
        return GnmSdk.GenerateFetchShader(gnmx, header, size).Fetch;
    }

    public static unsafe uint[] UsageTable(ReadOnlySpan<byte> vsHeader, ReadOnlySpan<byte> psHeader, string? sdkBin = null)
    {
        sdkBin = Load(sdkBin);
        (byte[] exports, int exportCount) = VsExports(vsHeader);
        (byte[] inputs, int inputCount) = PsInputs(psHeader);
        var table = new uint[Math.Max(inputCount, 1)];
        byte[] exportBuffer = [.. exports, 0, 0];
        byte[] inputBuffer = [.. inputs, 0, 0];
        fixed (uint* output = table)
        fixed (byte* vs = exportBuffer)
        fixed (byte* ps = inputBuffer)
            GnmSdk.GenerateUsageTable(Path.Combine(sdkBin, Ps4Sdk.GnmLibraryName), output, vs, (uint)exportCount, ps, (uint)inputCount);
        return table[..inputCount];
    }

    public static int UsageSlotCount(ReadOnlySpan<byte> header) => header[3];

    public static (byte[] Table, int Count) VsInputs(ReadOnlySpan<byte> vsHeader)
    {
        int count = vsHeader[36];
        int start = 40 + 4 * UsageSlotCount(vsHeader);
        return (vsHeader.Slice(start, 4 * count).ToArray(), count);
    }

    public static (byte[] Table, int Count) VsExports(ReadOnlySpan<byte> vsHeader)
    {
        int inputs = vsHeader[36], count = vsHeader[37];
        int start = 40 + 4 * UsageSlotCount(vsHeader) + 4 * inputs;
        return (vsHeader.Slice(start, 2 * count).ToArray(), count);
    }

    public static (byte[] Table, int Count) PsInputs(ReadOnlySpan<byte> psHeader)
    {
        int count = psHeader[56];
        int start = 60 + 4 * UsageSlotCount(psHeader);
        return (psHeader.Slice(start, 2 * count).ToArray(), count);
    }

    public static int CodeSize(ReadOnlySpan<byte> header) => (int)ShaderBinary.CodeSize(header);

    public static (byte[] Header, byte[] Code) SplitShaderBinary(ReadOnlySpan<byte> sb)
    {
        if (ShaderBinary.TrySplit(sb, out byte[] header, out byte[] code, out ShaderBinary.Problem problem))
            return (header, code);
        throw new InvalidDataException(problem == ShaderBinary.Problem.TruncatedCode
            ? "the compiled shader's code is truncated"
            : "the compiled shader has no Shdr header");
    }

    public sealed record Tessellation(byte[] LocalHeader, byte[] Fetch, uint LocalStride, uint Patches, uint VgtPrimitives);

    private const string LocalFetchSizeExport = "?computeLsFetchShaderSize@Gnmx@sce@@YAIPEBVLsShader@12@@Z";
    private const string LocalFetchExport = "?generateLsFetchShader@Gnmx@sce@@YAXPEAXPEAIPEBVLsShader@12@@Z";
    private const string LocalModifierExport = "?applyFetchShaderModifier@LsShader@Gnmx@sce@@QEAAXI@Z";
    private const string PatchCountsExport = "?computeVgtPrimitiveAndPatchCounts@Gnmx@sce@@YAXPEAI0IIPEBVLsShader@12@PEBVHsShader@12@@Z";
    private const string HullVgprsExport = "?getNumVgprs@HsStageRegisters@Gnm@sce@@QEBAIXZ";
    private const string PatchLdsExport = "?computeLdsUsagePerPatchInBytesPerThreadGroup@Gnm@sce@@YAIPEBVHullStateConstants@12@I@Z";
    private const string LocalLdsExport = "?updateLdsSize@LsStageRegisters@Gnm@sce@@QEAAXPEBVHullStateConstants@23@II@Z";
    private const int LocalHeaderSize = 32, HullHeaderSize = 64, StageRegisters = 8, HullConstants = 36, LocalStrideField = 28;

    public static unsafe Tessellation Tessellate(ReadOnlySpan<byte> localHeader, ReadOnlySpan<byte> hullHeader, string? sdkBin = null)
    {
        if (localHeader.Length < LocalHeaderSize || hullHeader.Length < HullHeaderSize)
            throw new InvalidDataException("a tessellation shader header is too short");
        sdkBin = Load(sdkBin);
        nint gnm = NativeLibrary.Load(Path.Combine(sdkBin, Ps4Sdk.GnmLibraryName)), gnmx = NativeLibrary.Load(Path.Combine(sdkBin, Ps4Sdk.GnmxLibraryName));
        byte[] local = new byte[localHeader.Length + 16], hull = new byte[hullHeader.Length + 16];
        localHeader.CopyTo(local);
        hullHeader.CopyTo(hull);
        fixed (byte* l = local)
        fixed (byte* h = hull)
        {
            uint size = ((delegate* unmanaged[Cdecl]<byte*, uint>)NativeLibrary.GetExport(gnmx, LocalFetchSizeExport))(l);
            if (size == 0 || size >= 65536)
                throw new InvalidDataException($"the SDK sizes a fetch shader at {size} bytes");
            byte[] fetch = new byte[size];
            uint modifier = 0;
            fixed (byte* output = fetch)
                ((delegate* unmanaged[Cdecl]<byte*, uint*, byte*, void>)NativeLibrary.GetExport(gnmx, LocalFetchExport))(output, &modifier, l);
            ((delegate* unmanaged[Cdecl]<byte*, uint, void>)NativeLibrary.GetExport(gnmx, LocalModifierExport))(l, modifier);
            uint stride = BitConverter.ToUInt32(local, LocalStrideField);
            uint vgprs = ((delegate* unmanaged[Cdecl]<byte*, uint>)NativeLibrary.GetExport(gnm, HullVgprsExport))(h + StageRegisters);
            uint lds = ((delegate* unmanaged[Cdecl]<byte*, uint, uint>)NativeLibrary.GetExport(gnm, PatchLdsExport))(h + HullConstants, stride);
            uint primitives = 0, patches = 0;
            ((delegate* unmanaged[Cdecl]<uint*, uint*, uint, uint, byte*, byte*, void>)NativeLibrary.GetExport(gnmx, PatchCountsExport))(&primitives, &patches, vgprs, lds, l, h);
            if (patches == 0 || primitives == 0)
                throw new InvalidDataException("the SDK finds no patch count that fits these hull and vertex shaders");
            ((delegate* unmanaged[Cdecl]<byte*, byte*, uint, uint, void>)NativeLibrary.GetExport(gnm, LocalLdsExport))(l + StageRegisters, h + HullConstants, stride, patches);
            return new Tessellation(local[..localHeader.Length], fetch, stride, patches, primitives);
        }
    }

    private static string Load(string? sdkBin)
    {
        sdkBin ??= Ps4Sdk.BinDirectory();
        if (!Available(sdkBin))
            throw new FileNotFoundException($"The PS4 SDK host libraries (libSceGnm.dll, libSceGnmx.dll) are not in {sdkBin}");
        try
        {
            GnmSdk.Load(Path.Combine(sdkBin, Ps4Sdk.GnmLibraryName));
            GnmSdk.Load(Path.Combine(sdkBin, Ps4Sdk.GnmxLibraryName));
        }
        catch (Win32Exception error)
        {
            throw new IOException($"The PS4 SDK host libraries in {sdkBin} could not be loaded (error {error.NativeErrorCode})", error);
        }
        return sdkBin;
    }
}
