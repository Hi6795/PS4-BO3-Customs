cbuffer PerScene : register(b0)
{
    float4x4 viewProj;
    float4 eyePos;
};

cbuffer PerObject : register(b1)
{
    float4x4 world;
};

cbuffer Globals : register(b2)
{
    float4 tessParams;
    float4 dispParams;
};

Texture2D heightMap : register(t0);
SamplerState heightSampler : register(s0);
Texture2D colorMap : register(t1);
SamplerState colorSampler : register(s1);

struct VsIn
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float4 color : COLOR0;
};

struct VsOut
{
    float3 worldPos : WORLDPOS;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float4 color : COLOR0;
};

struct HsConst
{
    float edges[3] : SV_TessFactor;
    float inside : SV_InsideTessFactor;
    float3 b210 : POSITION3;
    float3 b120 : POSITION4;
    float3 b021 : POSITION5;
    float3 b012 : POSITION6;
    float3 b102 : POSITION7;
    float3 b201 : POSITION8;
    float3 b111 : CENTER;
};

struct HsOut
{
    float3 worldPos : WORLDPOS;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float4 color : COLOR0;
};

struct DsOut
{
    float4 position : SV_Position;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float4 color : COLOR0;
    float3 worldPos : TEXCOORD1;
};

VsOut VsMain(VsIn input)
{
    VsOut output;
    output.worldPos = mul(float4(input.position, 1.0f), world).xyz;
    output.normal = normalize(mul(float4(input.normal, 0.0f), world).xyz);
    output.uv = input.uv;
    output.color = input.color;
    return output;
}

float EdgeFactor(float3 a, float3 b)
{
    float3 mid = (a + b) * 0.5f;
    float distance = length(mid - eyePos.xyz);
    return clamp(tessParams.x / max(distance, 1.0f), 1.0f, tessParams.y);
}

float3 Project(float3 point0, float3 plane, float3 normal)
{
    return point0 - dot(point0 - plane, normal) * normal;
}

HsConst HsPatch(InputPatch<VsOut, 3> patch, uint patchId : SV_PrimitiveID)
{
    HsConst output;
    output.edges[0] = EdgeFactor(patch[1].worldPos, patch[2].worldPos);
    output.edges[1] = EdgeFactor(patch[2].worldPos, patch[0].worldPos);
    output.edges[2] = EdgeFactor(patch[0].worldPos, patch[1].worldPos);
    output.inside = (output.edges[0] + output.edges[1] + output.edges[2]) / 3.0f;
    float3 p0 = patch[0].worldPos, p1 = patch[1].worldPos, p2 = patch[2].worldPos;
    float3 n0 = patch[0].normal, n1 = patch[1].normal, n2 = patch[2].normal;
    output.b210 = Project((2.0f * p0 + p1) / 3.0f, p0, n0);
    output.b120 = Project((2.0f * p1 + p0) / 3.0f, p1, n1);
    output.b021 = Project((2.0f * p1 + p2) / 3.0f, p1, n1);
    output.b012 = Project((2.0f * p2 + p1) / 3.0f, p2, n2);
    output.b102 = Project((2.0f * p2 + p0) / 3.0f, p2, n2);
    output.b201 = Project((2.0f * p0 + p2) / 3.0f, p0, n0);
    float3 e = (output.b210 + output.b120 + output.b021 + output.b012 + output.b102 + output.b201) / 6.0f;
    float3 v = (p0 + p1 + p2) / 3.0f;
    output.b111 = e + (e - v) * 0.5f;
    return output;
}

[domain("tri")]
[partitioning("fractional_odd")]
[outputtopology("triangle_cw")]
[outputcontrolpoints(3)]
[patchconstantfunc("HsPatch")]
[maxtessfactor(16.0f)]
HsOut HsMain(InputPatch<VsOut, 3> patch, uint id : SV_OutputControlPointID, uint patchId : SV_PrimitiveID)
{
    HsOut output;
    output.worldPos = patch[id].worldPos;
    output.normal = patch[id].normal;
    output.uv = patch[id].uv;
    output.color = patch[id].color;
    return output;
}

[domain("tri")]
DsOut DsMain(HsConst constants, float3 location : SV_DomainLocation, const OutputPatch<HsOut, 3> patch)
{
    float u = location.x, v = location.y, w = location.z;
    float3 p0 = patch[0].worldPos, p1 = patch[1].worldPos, p2 = patch[2].worldPos;
    float uu = u * u, vv = v * v, ww = w * w;
    float3 position = p0 * uu * u + p1 * vv * v + p2 * ww * w
        + constants.b210 * 3.0f * uu * v + constants.b120 * 3.0f * u * vv + constants.b201 * 3.0f * uu * w
        + constants.b021 * 3.0f * vv * w + constants.b102 * 3.0f * u * ww + constants.b012 * 3.0f * v * ww
        + constants.b111 * 6.0f * u * v * w;
    float3 normal = normalize(patch[0].normal * u + patch[1].normal * v + patch[2].normal * w);
    float2 uv = patch[0].uv * u + patch[1].uv * v + patch[2].uv * w;
    float height = heightMap.SampleLevel(heightSampler, uv * dispParams.zw, 0.0f).r;
    position += normal * (height * dispParams.x + dispParams.y);
    DsOut output;
    output.position = mul(float4(position, 1.0f), viewProj);
    output.normal = normal;
    output.uv = uv;
    output.color = patch[0].color * u + patch[1].color * v + patch[2].color * w;
    output.worldPos = position;
    return output;
}

float4 PsMain(DsOut input) : SV_Target
{
    float4 albedo = colorMap.Sample(colorSampler, input.uv) * input.color;
    float light = saturate(dot(normalize(input.normal), normalize(eyePos.xyz - input.worldPos)));
    return float4(albedo.rgb * light, albedo.a);
}
