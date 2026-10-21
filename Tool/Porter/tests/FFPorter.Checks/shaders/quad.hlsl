cbuffer PerScene : register(b0)
{
    float4x4 viewProj;
    float4 eyePos;
    float4 tessParams;
};

Texture2D heightMap : register(t0);
SamplerState heightSampler : register(s0);

struct VsIn
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    uint id : SV_VertexID;
};

struct VsOut
{
    float4 position : SV_Position;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    uint id : VERTEXID;
};

struct HsOut
{
    float3 position : BEZIERPOS;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float crease : CREASE;
};

struct HsConst
{
    float edges[4] : SV_TessFactor;
    float inside[2] : SV_InsideTessFactor;
    float3 centre : CENTRE;
    uint patch : PATCHID;
    float bounds[3] : BOUNDS;
};

struct DsOut
{
    float4 position : SV_Position;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float crease : TEXCOORD1;
    float clip : SV_ClipDistance0;
};

VsOut VsMain(VsIn input)
{
    VsOut output;
    output.position = float4(input.position, 1.0f);
    output.normal = input.normal;
    output.uv = input.uv;
    output.id = input.id;
    return output;
}

HsConst HsPatch(InputPatch<VsOut, 4> patch, OutputPatch<HsOut, 4> points, uint patchId : SV_PrimitiveID)
{
    HsConst output;
    float3 centre = 0.0f;
    for (int i = 0; i < 4; i++)
    {
        float3 a = points[i].position, b = points[(i + 1) & 3].position;
        float size = length(a - b) * tessParams.x / max(length((a + b) * 0.5f - eyePos.xyz), 0.01f);
        output.edges[i] = clamp(size, 1.0f, tessParams.y) + points[i].crease;
        centre += patch[i].position.xyz;
    }
    output.inside[0] = (output.edges[1] + output.edges[3]) * 0.5f;
    output.inside[1] = (output.edges[0] + output.edges[2]) * 0.5f;
    output.centre = centre * 0.25f;
    output.patch = patchId + patch[0].id;
    output.bounds[0] = min(min(points[0].position.x, points[1].position.x), min(points[2].position.x, points[3].position.x));
    output.bounds[1] = max(max(points[0].position.y, points[1].position.y), max(points[2].position.y, points[3].position.y));
    output.bounds[2] = centre.z;
    return output;
}

[domain("quad")]
[partitioning("integer")]
[outputtopology("triangle_ccw")]
[outputcontrolpoints(4)]
[patchconstantfunc("HsPatch")]
[maxtessfactor(32.0f)]
HsOut HsMain(InputPatch<VsOut, 4> patch, uint id : SV_OutputControlPointID, uint patchId : SV_PrimitiveID)
{
    HsOut output;
    uint next = (id + 1) & 3, previous = (id + 3) & 3;
    float3 smooth = (patch[next].position.xyz + patch[previous].position.xyz) * 0.5f;
    output.position = lerp(patch[id].position.xyz, smooth, tessParams.z);
    output.normal = normalize(patch[id].normal + patch[next].normal * tessParams.w);
    output.uv = patch[id].uv;
    output.crease = (patchId & 1) ? 0.5f : 0.0f;
    if (patch[id].id == 0)
        output.crease += heightMap.SampleLevel(heightSampler, patch[id].uv, 0.0f).g;
    return output;
}

[domain("quad")]
DsOut DsMain(HsConst constants, float2 location : SV_DomainLocation, const OutputPatch<HsOut, 4> patch, uint patchId : SV_PrimitiveID)
{
    float3 top = lerp(patch[0].position, patch[1].position, location.x);
    float3 bottom = lerp(patch[3].position, patch[2].position, location.x);
    float3 position = lerp(top, bottom, location.y);
    float3 normal = normalize(lerp(lerp(patch[0].normal, patch[1].normal, location.x), lerp(patch[3].normal, patch[2].normal, location.x), location.y));
    float2 uv = lerp(lerp(patch[0].uv, patch[1].uv, location.x), lerp(patch[3].uv, patch[2].uv, location.x), location.y);
    position += normal * heightMap.SampleLevel(heightSampler, uv, 0.0f).r * constants.bounds[(patchId + constants.patch) % 3];
    position = lerp(position, constants.centre, constants.edges[2] * 0.001f);
    DsOut output;
    output.position = mul(float4(position, 1.0f), viewProj);
    output.normal = normal;
    output.uv = uv;
    output.crease = lerp(patch[0].crease, patch[2].crease, location.x) + constants.inside[1];
    output.clip = dot(position, eyePos.xyz) - eyePos.w;
    return output;
}

float4 PsMain(DsOut input) : SV_Target
{
    return float4(input.normal * 0.5f + 0.5f, input.crease) * heightMap.Sample(heightSampler, input.uv);
}
