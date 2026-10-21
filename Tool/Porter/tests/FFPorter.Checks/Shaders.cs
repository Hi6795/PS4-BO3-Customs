using System.IO;
using FFPorter.Core.T7.Port.Converters;
using FFPorter.Core.T7.Shaders;

internal static class ShaderChecks
{
    public static void Run()
    {
        Console.WriteLine("== shaders: tessellated passes and the instructions custom shaders use");
        Stages();
        Tessellation();
        Instructions();
    }

    private static byte[] Bytecode(string name)
    {
        string resource = typeof(ShaderChecks).Assembly.GetManifestResourceNames().Single(n => n.EndsWith($".{name}.dxbc", StringComparison.Ordinal));
        using Stream stream = typeof(ShaderChecks).Assembly.GetManifestResourceStream(resource)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string Translate(string name, string? after = null, bool local = false)
    {
        byte[] dxbc = Bytecode(name);
        return T7DxbcTranslator.Translate(dxbc, T7DxbcTranslator.StageOf(dxbc, local), null, null,
            link: after == null ? null : T7DxbcTranslator.LinkFrom(Bytecode(after))).Text;
    }

    private static bool Has(string text, params string[] parts) => parts.All(part => text.Contains(part, StringComparison.Ordinal));

    private static string Struct(string text, string name)
    {
        int start = text.IndexOf($"struct {name}", StringComparison.Ordinal);
        if (start < 0)
            return "";
        int open = text.IndexOf('{', start), close = text.IndexOf("};", start, StringComparison.Ordinal);
        return open < 0 || close < 0 ? "" : text[open..close];
    }

    private static void Stages()
    {
        Program.Check(T7DxbcTranslator.StageOf(Bytecode("quad_vs")) == "vs" && T7DxbcTranslator.StageOf(Bytecode("quad_vs"), true) == "ls"
            && T7DxbcTranslator.StageOf(Bytecode("quad_hs")) == "hs" && T7DxbcTranslator.StageOf(Bytecode("quad_ds")) == "ds"
            && T7DxbcTranslator.StageOf(Bytecode("clip_ps")) == "ps", "each PC program is recognised as a vertex, hull, domain or pixel shader");
        Program.Check(T7DxbcTranslator.VersionOf("vs") == "t7-dxbc-6" && T7DxbcTranslator.VersionOf("ps") == "t7-dxbc-7"
            && T7DxbcTranslator.VersionOf("hs") == T7DxbcTranslator.VersionOf("ds") && T7DxbcTranslator.VersionOf("ls") != T7DxbcTranslator.VersionOf("vs"),
            "vertex and pixel shaders compiled before keep their place in the shader cache");
        T7DxbcTranslator.Link local = T7DxbcTranslator.LinkFrom(Bytecode("quad_vs")), hull = T7DxbcTranslator.LinkFrom(Bytecode("quad_hs"));
        Program.Check(local is { ControlRegisters: 4, PatchRegisters: 0, ControlPoints: 0 } && hull is { ControlRegisters: 3, ControlPoints: 4 } && hull.PatchRegisters > 0,
            "each tessellation stage is told what the stage before it hands over");
        Program.Check(T7TechsetBuilder.Ps4LocalShaderSize == 80 && T7TechsetBuilder.Ps4HullShaderSize == 72 && T7TechsetBuilder.Ps4ShaderSize == 64,
            "the three shader records of a tessellated pass have the sizes the PS4 game loads");
    }

    private static void Tessellation()
    {
        string local = Translate("quad_vs", local: true);
        Program.Check(Has(local, "out float4 out_r0 : TEXCOORD0", "out float4 out_r3 : TEXCOORD3", "S_VERTEX_ID") && !local.Contains("S_POSITION", StringComparison.Ordinal),
            "the vertex shader of a tessellated pass hands every output on as control point data");

        string hull = Translate("quad_hs", "quad_vs");
        Program.Check(Has(hull, "[DOMAIN_PATCH_TYPE(\"quad\")]", "[PARTITIONING_TYPE(\"integer\")]", "[OUTPUT_TOPOLOGY_TYPE(\"triangle_ccw\")]",
            "[OUTPUT_CONTROL_POINTS(4)]", "[PATCH_CONSTANT_FUNC(\"t7_patch\")]", "[MAX_TESS_FACTOR(32.0)]"), "a hull shader keeps its patch type, partitioning, winding and limits");
        Program.Check(Has(hull, "InputPatch<T7PointIn, 4> ip", "const OutputPatch<T7PointOut, 4> op", "uint primitive : S_PRIMITIVE_ID",
            "uint control : S_OUTPUT_CONTROL_POINT_ID", "float edges[4] : S_EDGE_TESS_FACTOR", "float inside[2] : S_INSIDE_TESS_FACTOR")
            && !hull.Contains("RETURN", StringComparison.Ordinal), "a hull shader with its own control point code reads both patches and writes four edge and two inside factors");

        string passthrough = Translate("pn_hs", "pn_vs");
        Program.Check(Has(passthrough, "result.r0 = ip[control].r0;", "for (int k1 = 0; k1 < 4; k1++)", "pc[r0.w + 4].xyz", "float edges[3] : S_EDGE_TESS_FACTOR",
            "float inside : S_INSIDE_TESS_FACTOR", "result.edges[2] = asfloat(pc[2].x);", "result.inside = asfloat(pc[3].x);"),
            "a hull shader that passes its control points through still runs every patch constant phase, instance by instance");

        string domain = Translate("quad_ds", "quad_hs");
        Program.Check(Has(domain, "[DOMAIN_PATCH_TYPE(\"quad\")]", "float2 location : S_DOMAIN_LOCATION", "const OutputPatch<T7ControlPoint, 4> patch",
            "uint primitive : S_PRIMITIVE_ID", ": S_POSITION", ": S_CLIP_DISTANCE0"), "a domain shader takes the patch, its constants and the domain location and writes the vertex");
        Program.Check(Struct(hull, "T7PatchConstants").Length > 0 && Struct(hull, "T7PatchConstants") == Struct(domain, "T7PatchConstants")
            && Struct(hull, "T7PointOut") == Struct(domain, "T7ControlPoint") && Struct(Translate("pn_hs", "pn_vs"), "T7PatchConstants") == Struct(Translate("pn_ds", "pn_hs"), "T7PatchConstants"),
            "hull and domain shaders lay out control points and patch constants the same way");

        bool refused = false;
        try
        {
            T7DxbcTranslator.Translate(Bytecode("quad_ds"), "ds", null, null, link: new T7DxbcTranslator.Link(1, 1, 4));
        }
        catch (NotSupportedException)
        {
            refused = true;
        }
        Program.Check(refused, "a domain shader that needs more than its hull shader writes is refused, not built wrong");
    }

    private static void Instructions()
    {
        string gather = Translate("gather_ps");
        Program.Check(Has(gather, ".GatherRed(s0,", ".GatherGreen(s0,", "int2(1, -1)", ".GatherBlue(s0,", ".GatherAlpha(s0,", ".GatherCmp(s1,", ": S_DEPTH_GE_OUTPUT"),
            "four-texel gathers, with fixed or computed offsets and with a depth comparison, are translated");
        string bits = Translate("bits_ps");
        Program.Check(Has(bits, "__bfrev(", "__f32tof16(", ".GetSamplePoint(", "uint coverage : S_COVERAGE", ".GetDimensions("),
            "bit reversal, half-float packing, sample positions, buffer sizes and the coverage mask are translated");
        string atomics = Translate("atomics_ps");
        Program.Check(Has(atomics, "AtomicExchange(", "AtomicCmpExchange(", "AtomicCmpStore(", "AtomicAnd(", "AtomicOr(", "AtomicXor(", "AtomicMax(", "AtomicMin(",
            ".IncrementCount()", ".d[2] = ", "EvaluateAttributeAtCentroid(in_r2)", "EvaluateAttributeAtSample(in_r2,", "EvaluateAttributeSnapped(in_r2,"),
            "atomic updates, counted buffers, structured writes and per-sample attribute reads are translated");
        string clip = Translate("clip_vs"), clipped = Translate("clip_ps");
        Program.Check(Has(clip, "out float2 out2 : S_CLIP_DISTANCE0", "out float out3 : S_CULL_DISTANCE0", "out float4 out_r2 : TEXCOORD2") && Has(clipped, "in_r2 : TEXCOORD2"),
            "clip and cull distances reach the rasteriser and stay readable in the pixel shader");
    }
}
