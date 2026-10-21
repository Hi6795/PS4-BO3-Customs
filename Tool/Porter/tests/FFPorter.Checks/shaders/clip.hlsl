cbuffer PerScene : register(b0)
{
    float4x4 viewProj;
    float4 planes[2];
};

struct VsOut
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
    float2 clip : SV_ClipDistance0;
    float cull : SV_CullDistance0;
    float4 color : COLOR0;
};

VsOut VsMain(float3 position : POSITION, float2 uv : TEXCOORD0, float4 color : COLOR0)
{
    VsOut output;
    output.position = mul(float4(position, 1.0f), viewProj);
    output.uv = uv;
    output.clip.x = dot(float4(position, 1.0f), planes[0]);
    output.clip.y = dot(float4(position, 1.0f), planes[1]);
    output.cull = position.z;
    output.color = color;
    return output;
}

float4 PsMain(VsOut input) : SV_Target
{
    return input.color * input.clip.x + input.cull + input.uv.xyxy;
}
