#ifndef CLOUD_HOLES_INCLUDED
#define CLOUD_HOLES_INCLUDED

float _CloudHoleCount;
float4 _CloudHoles[32];
float4 _CloudBoardRight;
float4 _CloudBoardForward;
float _CloudHoleSoft;
float _CloudHoleNoise;
float _CloudHoleRound;

TEXTURE2D(_CloudTerrainHeightTex);
SAMPLER(sampler_CloudTerrainHeightTex);
float4 _CloudTerrainOrigin;
float4 _CloudTerrainSize;

float CloudHoleCover(float3 positionWS, float noiseSample)
{
    int count = (int)_CloudHoleCount;
    if (count <= 0)
        return 1.0;

    float cover = 1.0;
    float soft = max(_CloudHoleSoft, 0.02);
    float roundness = max(_CloudHoleRound, 0.0);
    float n = (noiseSample - 0.5) * _CloudHoleNoise;
    float2 right = _CloudBoardRight.xy;
    float2 fwd = _CloudBoardForward.xy;

    [loop]
    for (int i = 0; i < 32; i++)
    {
        if (i >= count)
            break;

        float4 hole = _CloudHoles[i];
        float2 delta = positionWS.xz - hole.xy;
        float2 local = float2(dot(delta, right), dot(delta, fwd));
        float2 q = abs(local) - hole.zw + roundness;
        float sd = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - roundness + n;
        float edge = smoothstep(-soft, soft, sd);
        cover = min(cover, edge);
    }

    return saturate(cover);
}

float SampleTerrainY(float3 positionWS)
{
    float2 size = max(_CloudTerrainSize.xy, 1.0);
    float spacing = max(_CloudTerrainOrigin.z, 0.0001);
    float2 delta = positionWS.xz - _CloudTerrainOrigin.xy;
    float2 local = float2(dot(delta, _CloudBoardRight.xy), dot(delta, _CloudBoardForward.xy));
    float2 uv = (local / spacing + 0.5) / size;
    if (any(uv < 0.0) || any(uv > 1.0))
        return -50.0;
    return SAMPLE_TEXTURE2D_LOD(_CloudTerrainHeightTex, sampler_CloudTerrainHeightTex, uv, 0).r;
}

float TerrainShadowOnCloud(float3 positionWS, float3 lightDir)
{
    float terrainY = SampleTerrainY(positionWS);
    float gap = positionWS.y - terrainY;
    float contact = saturate(1.0 - gap / 1.7);

    float2 push = lightDir.xz * 1.1;
    float upwindY = SampleTerrainY(positionWS + float3(push.x, 0.0, push.y));
    float slope = saturate((upwindY - terrainY) / 0.65);

    return saturate(max(contact * 0.75, slope * 0.55));
}

#endif
