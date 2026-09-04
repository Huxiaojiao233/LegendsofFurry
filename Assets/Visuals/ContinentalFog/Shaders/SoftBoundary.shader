Shader "ContinentalFog/Soft Boundary"
{
    Properties
    {
        _FogColor("Fog Color", Color) = (.53, .64, .70, 1)
        _Map("Half Size / Inner / Outer", Vector) = (20,20,2,12)
        _Center("Map Center", Vector) = (0,0,0,0)
        _Noise("Scale / Strength / Velocity XZ", Vector) = (.18,.25,.025,.01)
        _Shape("Kind / Opacity / Falloff / Phase", Vector) = (0,.55,1.3,0)
        _Soft("Use Depth / Depth / Camera / Near Side", Vector) = (0,.6,.5,.12)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "SoftBoundary"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _FogColor;
                float4 _Map, _Center, _Noise, _Shape, _Soft;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }
            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            float Noise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1,0)), f.x),
                            lerp(Hash(cell + float2(0,1)), Hash(cell + 1), f.x), f.y);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 p = input.positionOS - _Center.xyz;
                float2 flow = (p.xz + p.y * float2(.37,.61)) * max(.001, _Noise.x)
                            - _Time.y * _Noise.zw + _Shape.w;
                float n = Noise(flow) * .68 + Noise(flow * 2.03 + 17.1) * .32;
                // A continuous density field: noise never punches cloud-shaped holes.
                float modulation = lerp(1, .55 + .65 * n, saturate(_Noise.y));
                float alpha;
                if (_Shape.x < .5)
                {
                    float height = saturate(input.uv.y + (n - .5) * .07);
                    float vertical = pow(abs(1 - smoothstep(.08, 1, height)), max(.2, _Shape.z));
                    float bottom = smoothstep(0, .10, input.uv.y);
                    float exterior = smoothstep(-.1, .65,
                        dot(normalize(input.normalWS), GetWorldSpaceNormalizeViewDir(input.positionWS)));
                    alpha = _Shape.y * vertical * bottom * modulation * lerp(1, _Soft.w, exterior);
                }
                else
                {
                    float d = max(abs(p.x) - _Map.x, abs(p.z) - _Map.y);
                    float inner = smoothstep(-max(.01, _Map.z), max(.01, _Map.z * .25), d);
                    if (_Shape.x > 1.5)
                    {
                        // Under the map edge: dense distant haze hides the underside/sky gap.
                        alpha = smoothstep(-_Map.z * .45, max(.1, _Map.w * .65), d);
                    }
                    else
                    {
                        float outer = 1 - smoothstep(_Map.w * .3, max(.1, _Map.w), d);
                        alpha = inner * outer * _Shape.y * modulation;
                    }
                }
                if (_Soft.x > .5 && _Shape.x < 1.5)
                {
                    float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    float rawDepth = SampleSceneDepth(screenUV);
                    #if !UNITY_REVERSED_Z
                        rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
                    #endif
                    float3 sceneWS = ComputeWorldSpacePosition(screenUV, rawDepth, UNITY_MATRIX_I_VP);
                    float sceneEye = -TransformWorldToView(sceneWS).z;
                    float fogEye = -TransformWorldToView(input.positionWS).z;
                    float softness = _Shape.x > .5 ? min(_Soft.y, .16) : _Soft.y;
                    alpha *= saturate((sceneEye - fogEye) / max(.01, softness));
                }
                float eyeDistance = -TransformWorldToView(input.positionWS).z;
                alpha *= smoothstep(0, max(.001, _Soft.z), eyeDistance - _ProjectionParams.y);
                return half4(_FogColor.rgb, saturate(alpha * _FogColor.a));
            }
            ENDHLSL
        }
    }
}
