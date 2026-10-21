using System.Buffers.Binary;
using System.Reflection;
using FFPorter.Core.Common.Fidelity;
using FFPorter.Core.T7.Formats;
using FFPorter.Core.T7.Port;
using FFPorter.Core.T7.Port.Converters;
using FFPorter.Core.T7.Streams;

internal static class FidelityChecks
{
    private const int Record = 16, Header = 48, Tail = 40;
    private static readonly int[] Fields = [8, 12];

    private delegate bool ConvertTexture(ReadOnlySpan<byte> pcTexture, ReadOnlySpan<byte> pcData, out byte[] ps4Texture, out byte[] ps4Data, out string? reason);

    public static void Run()
    {
        Console.WriteLine("== fidelity: packed normals, PS4-built data, animated textures and scores");
        Normals();
        Meshes();
        AnimatedTextures();
        GameShaders();
        Densities();
        Scores();
    }

    private static bool Near(float? value, double expected) => value is float found && Math.Abs(found - expected) <= 1e-6 * expected;

    private static void Densities()
    {
        Program.Check(Near(T7TexelDensity.Scale([(2048, 1024, 4)]), 1 / 64.0) && Near(T7TexelDensity.Scale([(256, 256, 3), (256, 256, 3)]), 1 / 16.0)
            && Near(T7TexelDensity.Scale([(256, 128, 2), (256, 128, 2)]), 1 / 4.0), "a material's texel density scale follows how far its textures stream: three, two or one level");
        Program.Check(Near(T7TexelDensity.Scale([(1024, 1024, 4), (512, 512, 3)]), 1 / 36.0) && Near(T7TexelDensity.Scale([(256, 256, 3), (256, 256, 2), (8, 8, 0)]), 9 / 64.0)
            && Near(T7TexelDensity.Scale([(1024, 1024, 4), (1024, 1024, 4), (256, 256, 3)]), 1 / 51.84)
            && Near(T7TexelDensity.Scale([(256, 256, 3), (256, 256, 2), (1024, 1024, 4)]), 1 / 23.04),
            "mixed texture sizes weigh in by size, as the console's own values do");
        Program.Check(Near(T7TexelDensity.Scale([(2048, 512, 4), (1024, 256, 3)]), 1 / 36.0) && Near(T7TexelDensity.Scale([(1024, 256, 4), (256, 256, 1), (1024, 256, 4)]), 1 / 64.0),
            "wide textures count by area, and textures that never stream are left out");
        Program.Check(T7TexelDensity.Scale([(8, 8, 0), (8, 8, 1)]) == null && T7TexelDensity.Scale(Array.Empty<(long, int)>()) == null && T7TexelDensity.Scale([(0, 0, 4)]) == null,
            "a material with no streamed texture to go by gets no scale of its own, so the fixed one applies");
        Program.Check(Near(T7TexelDensity.Scale([(1048576L, 4), (262144L, 3)]), 1 / 36.0)
            && T7StockImages.Pick([(1048576L, 4), (262144L, 3), (262144L, 3)]) == (262144L, 3) && T7StockImages.Pick([(1048576L, 4), (262144L, 3)]) == (1048576L, 4),
            "a texture kept in the game's own texture packs counts the same way, by the size most packs agree on");
        Program.Check(T7TechsetBuilder.Ps4StateBits(0x07104C49) == 0x0FB25649 && T7TechsetBuilder.Ps4StateBits(0x03104C49) == 0x07105649
            && T7TechsetBuilder.Ps4StateBits(0x0F104209) == 0x0F104209 && T7TechsetBuilder.Ps4StateBits(0) == 0,
            "decal render states get their PS4 value in any technique; every other state is kept");
        Program.Check(T7TechsetBuilder.DrawnPerInstance("ei/effect_lit_scroll_emissive_blend_nocull#2843f95a") && T7TechsetBuilder.DrawnPerInstance(",ei/effect_lit_blend#26fcba19")
            && T7TechsetBuilder.DrawnPerInstance("ei/effect_lit_blend~#0a1b2c3d") && !T7TechsetBuilder.DrawnPerInstance("el/effect_lit_blend#3236dcd6")
            && !T7TechsetBuilder.DrawnPerInstance("ec/effect_cloud_lit_blend#7503baf8") && !T7TechsetBuilder.DrawnPerInstance("mc/lit#e6142445")
            && !T7TechsetBuilder.DrawnPerInstance("wc/lit_emissive_scroll#86de3377") && !T7TechsetBuilder.DrawnPerInstance(null),
            "instanced effect shader sets read their sprite data once per sprite; every other set reads per vertex");
    }

    private static void GameShaders()
    {
        IReadOnlySet<string> mod = T7PcShaderLibrary.AlwaysLoadedBefore("core_mod"), map = T7PcShaderLibrary.AlwaysLoadedBefore("zm_test");
        Program.Check(mod.Contains("core_common") && mod.Contains("core_pre_gfx") && mod.Contains("core_patch") && mod.SetEquals(map)
            && !map.Contains("zm_common") && !map.Contains("mp_common"), "only the game's core zones count as loaded before a map or mod");
        Program.Check(!T7PcShaderLibrary.AlwaysLoadedBefore("core_common").Contains("core_common") && !T7PcShaderLibrary.AlwaysLoadedBefore("en_core_patch").Contains("core_patch")
            && T7PcShaderLibrary.AlwaysLoadedBefore("core_patch").Contains("core_common"), "a game zone never counts as providing its own technique sets");
        Program.Check(T7Names.Detached("mc/lit#e4cce504") == "mc/lit~#e4cce504" && T7Names.Detached("plain") == "plain~",
            "a rebuilt copy of one of the game's technique sets gets its own name, so it cannot replace the game's");
    }

    private static uint Pack(int x, int y, int z, uint top = 0) => (uint)(x & 0x3FF) | (uint)(y & 0x3FF) << 10 | (uint)(z & 0x3FF) << 20 | top << 30;

    private static int Signed(uint value, int shift)
    {
        int c = (int)(value >> shift & 0x3FF);
        return c >= 512 ? c - 1024 : c;
    }

    private static int AsPc(int signed) => signed >= 0 ? signed + 512 : signed + 511;

    private static List<(double X, double Y, double Z)> Directions()
    {
        var dice = new Random(7);
        var list = new List<(double, double, double)>();
        for (int i = 0; i < 480; i++)
        {
            double x = dice.NextDouble() * 2 - 1, y = dice.NextDouble() * 2 - 1, z = dice.NextDouble() * 2 - 1;
            double length = Math.Sqrt(x * x + y * y + z * z);
            if (length < 0.2)
                continue;
            list.Add((x / length, y / length, z / length));
        }
        for (int i = 0; i < 90; i++)
        {
            double small = (i % 2 == 0 ? 1 : -1) * (0.7 + i % 5 * 0.15) / 511.0;
            double y = Math.Cos(i * 0.37), z = Math.Sin(i * 0.37);
            double scale = Math.Sqrt(1 - small * small);
            list.Add(i % 3 == 0 ? (small, y * scale, z * scale) : i % 3 == 1 ? (y * scale, small, z * scale) : (y * scale, z * scale, small));
        }
        list.AddRange([(0, 0, 1), (0, 0, -1), (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0)]);
        return list;
    }

    private static byte[] Vertices(Func<double, int> encode, int seed)
    {
        List<(double X, double Y, double Z)> directions = Directions();
        var dice = new Random(seed);
        var data = new byte[directions.Count * Record];
        dice.NextBytes(data);
        for (int i = 0; i < directions.Count; i++)
        {
            (double x, double y, double z) = directions[i];
            (double tx, double ty, double tz) = directions[(i * 7 + 3) % directions.Count];
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * Record + 8), Pack(encode(x), encode(y), encode(z)));
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * Record + 12), Pack(encode(tx), encode(ty), encode(tz), i % 2 == 0 ? 3u : 0u));
        }
        return data;
    }

    private static int Round(double n) => (int)Math.Round(n * 511, MidpointRounding.AwayFromZero);

    private static void Normals()
    {
        bool inverse = true;
        for (int signed = -511; signed <= 511; signed++)
        {
            uint restored = T7PackedNormals.FromPs4AsPc(Pack(AsPc(signed), AsPc(-signed), AsPc(signed), 2));
            inverse &= Signed(restored, 0) == signed && Signed(restored, 10) == -signed && Signed(restored, 20) == signed && restored >> 30 == 2;
        }
        Program.Check(inverse, "normals a PC copy was made from come back as every one of the 1,023 PS4 values, with the top bits kept");

        uint up = T7PackedNormals.FromPc(Pack(512, 512, 1023, 3)), left = T7PackedNormals.FromPc(Pack(1, 512, 512));
        Program.Check(Signed(up, 0) == 0 && Signed(up, 20) == 511 && up >> 30 == 3 && Signed(left, 0) == -511 && Signed(left, 10) == 0,
            "PC normals along an axis become the same axis in the PS4 format");

        byte[] pc = Vertices(n => (int)Math.Floor(n * 511) + 512, 1);
        byte[] ps4 = Vertices(n => Round(n), 1);
        byte[] copied = Vertices(n => AsPc(Round(n)), 1);
        long count = pc.Length / Record;
        Program.Check(T7PackedNormals.Classify(pc, 0, Record, count, Fields, false) == T7NormalForm.Pc
            && T7PackedNormals.Classify(pc, 0, Record, count, Fields, true) == T7NormalForm.Pc, "PC normals are recognised as PC normals, in any zone");
        Program.Check(T7PackedNormals.Classify(ps4, 0, Record, count, Fields, false) == T7NormalForm.Ps4
            && T7PackedNormals.Classify(ps4, 0, Record, count, Fields, true) == T7NormalForm.Ps4, "normals already in the PS4 format are recognised as such");
        Program.Check(T7PackedNormals.Classify(copied, 0, Record, count, Fields, true) == T7NormalForm.Ps4AsPc,
            "in a PS4-built zone, normals copied from PS4 values are recognised");
        Program.Check(T7PackedNormals.Classify(copied, 0, Record, count, Fields, false) == T7NormalForm.Pc,
            "without that proof the same normals are treated as plain PC normals");

        byte[] kept = (byte[])ps4.Clone();
        T7PackedNormals.Convert(kept, 0, Record, count, Fields, T7NormalForm.Ps4);
        Program.Check(kept.AsSpan().SequenceEqual(ps4), "normals already in the PS4 format are left as they are");
        byte[] back = (byte[])copied.Clone();
        T7PackedNormals.Convert(back, 0, Record, count, Fields, T7NormalForm.Ps4AsPc);
        Program.Check(back.AsSpan().SequenceEqual(ps4), "normals copied from PS4 values convert back to the PS4 bytes exactly");
        byte[] converted = (byte[])pc.Clone();
        T7PackedNormals.Convert(converted, 0, Record, count, Fields, T7NormalForm.Pc);
        bool unit = true, outside = true;
        for (int i = 0; i < count; i++)
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(converted.AsSpan(i * Record + 8));
            double length = Math.Sqrt(Math.Pow(Signed(value, 0), 2) + Math.Pow(Signed(value, 10), 2) + Math.Pow(Signed(value, 20), 2));
            unit &= Math.Abs(length - 511) < 1.5;
            outside &= converted.AsSpan(i * Record, 8).SequenceEqual(pc.AsSpan(i * Record, 8));
        }
        Program.Check(unit && outside, "PC normals become unit-length PS4 normals and nothing else in a vertex changes");
    }

    private static (byte[] Payload, byte[] Shared) Mesh(byte[] vertices, int seed)
    {
        var dice = new Random(seed);
        var payload = new byte[Header + vertices.Length + Tail];
        dice.NextBytes(payload);
        vertices.CopyTo(payload, Header);
        var shared = new byte[T7XModelConverter.SharedSize];
        shared[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(shared.AsSpan(4), (uint)(vertices.Length / Record));
        BinaryPrimitives.WriteUInt32LittleEndian(shared.AsSpan(0x18), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(shared.AsSpan(0x20), Header);
        return (payload, shared);
    }

    private static void Meshes()
    {
        (byte[] original, byte[] shared) = Mesh(Vertices(n => Round(n), 2), 5);
        (byte[] stored, _) = Mesh(Vertices(n => AsPc(Round(n)), 2), 5);
        ulong key = XPak.ComputeKey(original, 0);
        byte[] restored = (byte[])stored.Clone();
        Program.Check(T7XModelConverter.TryRestorePayload(restored, shared, key) && restored.AsSpan().SequenceEqual(original),
            "a streamed mesh made from PS4 data converts back to the bytes its key was hashed from");

        byte[] own = (byte[])original.Clone();
        Program.Check(!T7XModelConverter.TryRestorePayload(own, shared, key) && own.AsSpan().SequenceEqual(original)
            && T7XModelConverter.RepackPayload(own, shared) == T7NormalForm.Ps4 && own.AsSpan().SequenceEqual(original),
            "a streamed mesh that is still PS4 data is kept byte for byte");

        (byte[] pc, _) = Mesh(Vertices(n => (int)Math.Floor(n * 511) + 512, 2), 5);
        byte[] plain = (byte[])pc.Clone();
        ulong pcKey = XPak.ComputeKey(pc, 0);
        bool untouched = !T7XModelConverter.TryRestorePayload(plain, shared, pcKey) && plain.AsSpan().SequenceEqual(pc);
        bool converted = T7XModelConverter.RepackPayload(plain, shared) == T7NormalForm.Pc && !plain.AsSpan().SequenceEqual(pc)
            && plain.AsSpan(0, Header).SequenceEqual(pc.AsSpan(0, Header)) && plain.AsSpan(pc.Length - Tail).SequenceEqual(pc.AsSpan(pc.Length - Tail));
        Program.Check(untouched && converted, "an ordinary PC mesh still converts by the PC rule, vertices only");

        byte[] foreign = (byte[])pc.Clone();
        Program.Check(!T7XModelConverter.TryRestorePayload(foreign, shared, key ^ 0x55) && foreign.AsSpan().SequenceEqual(pc),
            "a mesh whose key matches nothing is not touched by the PS4 rule");
    }

    private static uint Align(uint value, uint to) => (value + to - 1) / to * to;

    private static void AnimatedTextures()
    {
        MethodInfo? method = typeof(T7SAnimConverter).GetMethod("TryConvertTexture", BindingFlags.NonPublic | BindingFlags.Static);
        if (method == null)
        {
            Program.Check(false, "the animated texture converter can be reached");
            return;
        }
        var convert = method.CreateDelegate<ConvertTexture>();
        (uint Width, uint Frames, int Ps4Size)[] known =
        [
            (18, 80, 31744), (26, 80, 41984), (22, 80, 31744), (3, 120, 16384), (22, 339, 348160), (4, 140, 36864), (28, 360, 185344), (18, 500, 257024),
        ];
        var wrong = new List<string>();
        foreach ((uint width, uint frames, int ps4Size) in known)
        {
            uint baseRow = width * 16, row = width * 8;
            uint first = Align(baseRow, 256), second = Align(first + frames * row, 256), size = second + Align(frames * row, 256);
            var texture = new byte[T7SAnimConverter.PcTextureSize];
            BinaryPrimitives.WriteUInt32LittleEndian(texture.AsSpan(0x34), size);
            BinaryPrimitives.WriteUInt64LittleEndian(texture.AsSpan(0x38), ulong.MaxValue);
            uint[] fields = [baseRow, row, row, 0, first, second, width, frames, size, 10, 10, 0];
            for (int i = 0; i < fields.Length; i++)
                BinaryPrimitives.WriteUInt32LittleEndian(texture.AsSpan(T7SAnimConverter.PcLayoutAt + 4 * i), fields[i]);
            var data = new byte[size];
            new Random((int)(width * 1000 + frames)).NextBytes(data);
            if (!convert(texture, data, out byte[] ps4Texture, out byte[] ps4Data, out string? reason))
            {
                wrong.Add($"{width}x{frames}: {reason}");
                continue;
            }
            int pitch = (ps4Size - 1024) / (int)(2 * frames * 8);
            bool rows = true;
            for (int frame = 0; frame < frames && rows; frame++)
            {
                rows &= ps4Data.AsSpan(1024 + frame * pitch * 8, (int)row).SequenceEqual(data.AsSpan((int)(first + frame * row), (int)row));
                rows &= ps4Data.AsSpan(1024 + (int)frames * pitch * 8 + frame * pitch * 8, (int)row).SequenceEqual(data.AsSpan((int)(second + frame * row), (int)row));
            }
            if (ps4Data.Length != ps4Size || !rows || BinaryPrimitives.ReadUInt32LittleEndian(ps4Texture.AsSpan(T7SAnimConverter.Ps4LayoutAt + 4)) != pitch * 8)
                wrong.Add($"{width}x{frames}: {ps4Data.Length} bytes, {ps4Size} expected");
        }
        Program.Check(wrong.Count == 0, "animated textures get the row size the PS4 uses, also with 80 frames" + (wrong.Count > 0 ? $" ({string.Join("; ", wrong)})" : ""));
    }

    private static void Scores()
    {
        var converted = new FidelityTracker("t7", "mod", null) { Subject = "mod" };
        converted.Add(FidelityDimension.Sound, FidelityGrade.Native, 23);
        converted.Add(FidelityDimension.Scripts, FidelityGrade.Exact, 14);
        FidelitySnapshot snapshot = converted.Finish(FidelityStates.Done, converted.Headline(true, 0));
        FidelityDimensionReport sound = snapshot.Dimensions.First(d => d.Name == "Sound");
        Program.Check(snapshot.Percent == 99 && snapshot.Grade == "strong" && sound.Percent == 99 && sound.Grade == "strong",
            "sound and shaders in PS4 formats cost no points, and the score still never reads 100%");
        Program.Check(snapshot.Headline == "Everything came across in PS4 formats. Verify in-game.", "the headline then says everything came across in PS4 formats");

        var exact = new FidelityTracker("t7", "mod", null);
        exact.Add(FidelityDimension.Scripts, FidelityGrade.Exact, 14);
        FidelitySnapshot all = exact.Finish(FidelityStates.Done, exact.Headline(true, 0));
        Program.Check(all.Percent == 100 && all.Grade == "exact", "only a conversion with nothing re-encoded reads 100%");

        var lossy = new FidelityTracker("t7", "map", null);
        lossy.Add(FidelityDimension.Geometry, FidelityGrade.Exact, 80);
        lossy.Add(FidelityDimension.Geometry, FidelityGrade.Approximate, 20);
        lossy.Add(FidelityDimension.Sound, FidelityGrade.Native, 10);
        FidelitySnapshot some = lossy.Finish(FidelityStates.Done, lossy.Headline(true, 0));
        Program.Check(some.Percent < 99 && some.Dimensions.First(d => d.Name == "Geometry").Percent == 90 && some.Headline.StartsWith("Nearly all", StringComparison.Ordinal),
            "a real loss still lowers the score");

        var fidelity = new T7Fidelity("map", null);
        fidelity.SoundBank(4, 4, 0, 2);
        FidelitySnapshot bank = fidelity.Tracker.Snapshot();
        FidelityDimensionReport banked = bank.Dimensions.First(d => d.Name == "Sound");
        Program.Check(banked.Percent == 99 && banked.Notes.Any(n => n.Grade == "strong" && n.Text.Contains("re-encoded from FLAC to MP3", StringComparison.Ordinal)),
            "re-encoded sounds are still listed under Sound");
    }
}
