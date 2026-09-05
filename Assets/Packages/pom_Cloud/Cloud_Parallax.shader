Shader "Custom/Cloud Parallax URP"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
        _MainTex("MainTex", 2D) = "white" {}
        _Alpha("Alpha", Range(0,1)) = 0.5
        _Height("Displacement Amount", Range(0,1)) = 0.15
        _HeightAmount("Turbulence Amount", Range(0,2)) = 1
        _HeightTileSpeed("Turbulence Tile & Speed", Vector) = (1,1,0.05,0)
        _LightIntensity("Ambient Intensity", Range(0,3)) = 1.0

        [Toggle] _UseFixedLight("Use Fixed Light", Float) = 1
        _FixedLightDir("Fixed Light Direction", Vector) =
            (0.981, 0.122, -0.148, 0)
    }

        SubShader
        {
            Tags
            {
                "RenderType" = "Transparent"
                "Queue" = "Transparent"
                "RenderPipeline" = "UniversalPipeline"
            }

            Pass
            {
                Name "CloudParallax"

                Tags
                {
                    "LightMode" = "UniversalForward"
                }

                Blend SrcAlpha OneMinusSrcAlpha
                ZWrite Off
                Cull Off

                HLSLPROGRAM

                #pragma vertex vert
                #pragma fragment frag
                #pragma target 3.0
                #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
                #pragma multi_compile_fragment _ _SHADOWS_SOFT

                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
                #include "CloudHoles.hlsl"

                TEXTURE2D(_MainTex);
                SAMPLER(sampler_MainTex);

                CBUFFER_START(UnityPerMaterial)
                    float4 _MainTex_ST;
                    half4 _Color;

                    half _Alpha;
                    half _Height;
                    half _HeightAmount;
                    float4 _HeightTileSpeed;

                    half _LightIntensity;
                    half _UseFixedLight;
                    float4 _FixedLightDir;
                CBUFFER_END

                struct Attributes
                {
                    float4 positionOS : POSITION;
                    float3 normalOS   : NORMAL;
                    float4 tangentOS  : TANGENT;
                    float2 uv         : TEXCOORD0;
                    float4 color      : COLOR;
                };

                struct Varyings
                {
                    float4 positionCS : SV_POSITION;

                    float2 uv         : TEXCOORD0;
                    float2 uv2        : TEXCOORD1;

                    float3 positionWS : TEXCOORD2;
                    float3 normalWS   : TEXCOORD3;

                    // View direction expressed in tangent space.
                    float3 viewDirTS  : TEXCOORD4;

                    float4 color      : TEXCOORD5;
                };

                Varyings vert(Attributes input)
                {
                    Varyings output;

                    VertexPositionInputs positionInputs =
                        GetVertexPositionInputs(input.positionOS.xyz);

                    VertexNormalInputs normalInputs =
                        GetVertexNormalInputs(
                            input.normalOS,
                            input.tangentOS
                        );

                    output.positionCS = positionInputs.positionCS;
                    output.positionWS = positionInputs.positionWS;
                    output.normalWS = normalInputs.normalWS;

                    // Main cloud UV + movement
                    float2 baseUV =
                        input.uv * _MainTex_ST.xy +
                        _MainTex_ST.zw;

                    output.uv =
                        baseUV +
                        frac(_Time.y * _HeightTileSpeed.zw);

                    // Turbulence UV
                    output.uv2 =
                        input.uv * _HeightTileSpeed.xy;

                    // World-space camera direction
                    float3 viewDirWS =
                        GetWorldSpaceViewDir(
                            positionInputs.positionWS
                        );

                    // World -> Tangent
                    float3x3 tangentToWorld =
                        float3x3(
                            normalInputs.tangentWS,
                            normalInputs.bitangentWS,
                            normalInputs.normalWS
                        );

                    output.viewDirTS =
                        mul(
                            transpose(tangentToWorld),
                            viewDirWS
                        );

                    output.color = input.color;

                    return output;
                }

                half4 frag(Varyings input) : SV_Target
                {
                    // ------------------------------------------------
                    // Parallax cloud calculation
                    // ------------------------------------------------

                    float3 viewRay =
                        normalize(-input.viewDirTS);

                    viewRay.z =
                        abs(viewRay.z) + 0.2;

                    viewRay.xy *= _Height;

                    float3 shadeP =
                        float3(input.uv, 0);

                    float3 shadeP2 =
                        float3(input.uv2, 0);

                    // Large-scale turbulence texture
                    half4 turbulence =
                        SAMPLE_TEXTURE2D(
                            _MainTex,
                            sampler_MainTex,
                            shadeP2.xy
                        );

                    float cloudHeight =
                        turbulence.a *
                        _HeightAmount;

                    // ------------------------------------------------
                    // Original shader uses a small parallax
                    // ray-marching loop.
                    // ------------------------------------------------

                    const float linearSteps = 16.0;

                    float3 rayOffset =
                        viewRay /
                        (viewRay.z * linearSteps);

                    float depth =
                        1.0 -
                        SAMPLE_TEXTURE2D_LOD(
                            _MainTex,
                            sampler_MainTex,
                            shadeP.xy,
                            0
                        ).a *
                        cloudHeight;

                    float previousDepth = depth;
                    float3 previousShadeP = shadeP;

                    [loop]
                    for (int stepIndex = 0;
                         stepIndex < 32;
                         stepIndex++)
                    {
                        if (depth <= shadeP.z)
                            break;

                        previousShadeP = shadeP;
                        previousDepth = depth;

                        shadeP += rayOffset;

                        depth =
                            1.0 -
                            SAMPLE_TEXTURE2D_LOD(
                                _MainTex,
                                sampler_MainTex,
                                shadeP.xy,
                                0
                            ).a *
                            cloudHeight;
                    }

                    float d1 =
                        depth - shadeP.z;

                    float d2 =
                        previousDepth -
                        previousShadeP.z;

                    float denominator =
                        d1 - d2;

                    float interpolation =
                        abs(denominator) > 0.00001
                        ? d1 / denominator
                        : 0.0;

                    shadeP =
                        lerp(
                            shadeP,
                            previousShadeP,
                            interpolation
                        );

                    // ------------------------------------------------
                    // Final cloud texture
                    // ------------------------------------------------

                    half4 cloudSample =
                        SAMPLE_TEXTURE2D(
                            _MainTex,
                            sampler_MainTex,
                            shadeP.xy
                        );

                    half4 cloud =
                        cloudSample *
                        turbulence *
                        _Color;

                    float holeCover = CloudHoleCover(input.positionWS, turbulence.r);
                    // 接缝处 cover 常为 0.5；用 <0.5 会留下十字/网格状细云线。
                    if (holeCover < 0.99)
                        discard;

                    half alpha = _Alpha;

                    // ------------------------------------------------
                    // URP lighting
                    // ------------------------------------------------

                    float3 normalWS =
                        normalize(input.normalWS);

                    float4 shadowCoord =
                        TransformWorldToShadowCoord(input.positionWS);
                    Light mainLight =
                        GetMainLight(shadowCoord);

                    float3 normalLightDir =
                        normalize(mainLight.direction);

                    float3 fixedLightDir =
                        normalize(_FixedLightDir.xyz);

                    float3 lightDir =
                        normalize(
                            lerp(
                                normalLightDir,
                                fixedLightDir,
                                saturate(_UseFixedLight)
                            )
                        );

                    half NdotL =
                        saturate(
                            dot(
                                normalWS,
                                lightDir
                            )
                        );

                    half3 ambient =
                        SampleSH(normalWS);

                    half3 directLight =
                        mainLight.color *
                        NdotL;

                    float terrainShadow =
                        TerrainShadowOnCloud(input.positionWS, lightDir);
                    half shadow =
                        lerp(0.52h, 1.0h, mainLight.shadowAttenuation);
                    shadow *= lerp(1.0h, 0.55h, terrainShadow);

                    half3 finalColor =
                        cloud.rgb *
                        (
                            ambient * _LightIntensity +
                            directLight +
                            0.35h
                        ) *
                        shadow;

                    return half4(
                        finalColor,
                        alpha
                    );
                }

                ENDHLSL
            }
        }
}