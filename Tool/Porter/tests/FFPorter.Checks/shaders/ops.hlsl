cbuffer PerScene : register(b0)
{
    float4 params;
    uint4 bits;
};

Texture2D colorMap : register(t0);
Texture2D<float> shadowMap : register(t1);
Texture2DMS<float4> msMap : register(t2);
Texture2DArray arrayMap : register(t3);
Buffer<float4> lineBuffer : register(t4);
ByteAddressBuffer rawBuffer : register(t5);
struct Node { uint a; uint b; float c; };
StructuredBuffer<Node> nodes : register(t6);
SamplerState linearSampler : register(s0);
SamplerComparisonState shadowSampler : register(s1);
RWTexture2D<uint> counters : register(u1);
RWStructuredBuffer<Node> output : register(u2);

struct PsIn
{
    linear noperspective centroid float4 position : SV_Position;
    float4 uv : TEXCOORD0;
    sample float4 color : COLOR0;
    float clip : SV_ClipDistance0;
    uint coverage : SV_Coverage;
};

struct PsOut
{
    float4 color : SV_Target0;
    float depth : SV_DepthGreaterEqual;
};

PsOut Gather(PsIn input)
{
    PsOut result;
    float4 r = colorMap.GatherRed(linearSampler, input.uv.xy);
    float4 g = colorMap.GatherGreen(linearSampler, input.uv.xy, int2(1, -1));
    float4 b = colorMap.GatherBlue(linearSampler, input.uv.xy, int2(params.xy));
    float4 a = arrayMap.GatherAlpha(linearSampler, input.uv.xyz);
    float4 s = shadowMap.GatherCmpRed(shadowSampler, input.uv.xy, input.uv.z);
    float4 t = shadowMap.GatherCmpRed(shadowSampler, input.uv.xy, input.uv.z, int2(params.zw));
    result.color = r + g + b + a + s + t + input.clip;
    result.depth = input.position.z + 0.001f;
    return result;
}

float4 Bits(PsIn input) : SV_Target
{
    uint value = bits.x + input.coverage;
    uint reversed = reversebits(value);
    uint packed = f32tof16(input.uv.x) | (f32tof16(input.uv.y) << 16);
    uint high = 0, low = bits.y * bits.z, carry = 0;
    int signedHigh = 0;
    uint elements, stride;
    nodes.GetDimensions(elements, stride);
    uint lines;
    lineBuffer.GetDimensions(lines);
    uint bytes;
    rawBuffer.GetDimensions(bytes);
    uint width, height, samples;
    msMap.GetDimensions(width, height, samples);
    float2 samplePosition = msMap.GetSamplePosition(input.coverage & 3);
    return float4(reversed, packed, high + low + carry + signedHigh, elements + lines + bytes + samples) * samplePosition.xyxy + countbits(value) + firstbitlow(value);
}

float4 Atomics(PsIn input) : SV_Target
{
    uint2 pixel = uint2(input.position.xy);
    uint original;
    InterlockedExchange(counters[pixel], bits.x, original);
    uint compared;
    InterlockedCompareExchange(counters[pixel], bits.y, bits.z, compared);
    InterlockedCompareStore(counters[pixel], bits.y, bits.w);
    InterlockedAnd(counters[pixel], bits.x);
    InterlockedOr(counters[pixel], bits.y);
    InterlockedXor(counters[pixel], bits.z);
    InterlockedMax(counters[pixel], bits.w);
    InterlockedMin(counters[pixel], bits.x);
    uint slot = output.IncrementCounter();
    Node node;
    node.a = original;
    node.b = compared;
    node.c = input.uv.x;
    output[slot] = node;
    float4 atCentroid = EvaluateAttributeCentroid(input.color);
    float4 atSample = EvaluateAttributeAtSample(input.color, input.coverage & 3);
    float4 snapped = EvaluateAttributeSnapped(input.color, int2(2, -3));
    return atCentroid + atSample + snapped + nodes[slot & 3].c;
}
