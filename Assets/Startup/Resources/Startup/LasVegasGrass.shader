Shader "StockCarManager/Las Vegas Grass"
{
    Properties
    {
        [NoScaleOffset] _GroundMask("Mowing and pavement shoulders", 2D) = "black" {}
        [NoScaleOffset] _ExtraWearMask("Additional paved shoulders", 2D) = "black" {}
        [HideInInspector] _UseLayoutMask("Layout mask", Float) = 0
        [HideInInspector] _UseExtraWear("Additional wear", Float) = 0
        [HideInInspector] _GroundMaskBounds("Layout mask bounds", Vector) = (0, 0, 1, 1)
        [HideInInspector] _ExtraWearBounds("Additional wear bounds", Vector) = (0, 0, 1, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "Grass Forward"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex GrassVertex
            #pragma fragment GrassFragment
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_GroundMask);
            SAMPLER(sampler_GroundMask);
            TEXTURE2D(_ExtraWearMask);
            SAMPLER(sampler_ExtraWearMask);

            CBUFFER_START(UnityPerMaterial)
                float4x4 _WorldToTrackSource;
                float4x4 _WorldToTrackGrass;
                float4 _GroundMaskBounds;
                float4 _ExtraWearBounds;
                float _UseLayoutMask;
                float _UseExtraWear;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 sourceXZ : TEXCOORD2;
                half4 fogAndVertexLight : TEXCOORD3;
                float2 localXZ : TEXCOORD4;
            };

            Varyings GrassVertex(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.sourceXZ = mul(_WorldToTrackSource, float4(position.positionWS, 1.0)).xz;
                output.localXZ = mul(_WorldToTrackGrass, float4(position.positionWS, 1.0)).xz;
                output.fogAndVertexLight = half4(ComputeFogFactor(position.positionCS.z),
                    VertexLighting(position.positionWS, output.normalWS));
                return output;
            }

            float GrassHash(float2 cell)
            {
                float3 hash = frac(float3(cell.xyx) * 0.1031);
                hash += dot(hash, hash.yzx + 33.33);
                return frac((hash.x + hash.y) * hash.z);
            }

            float GrassNoise(float2 position)
            {
                float2 cell = floor(position);
                float2 weight = frac(position);
                weight = weight * weight * (3.0 - 2.0 * weight);
                float nearRow = lerp(GrassHash(cell), GrassHash(cell + float2(1, 0)), weight.x);
                float farRow = lerp(GrassHash(cell + float2(0, 1)), GrassHash(cell + float2(1, 1)), weight.x);
                return lerp(nearRow, farRow, weight.y);
            }

            half3 GrassAlbedo(float2 sourceXZ, float2 localXZ)
            {
                float2 maskUV = _UseLayoutMask > 0.5
                    ? (sourceXZ - _GroundMaskBounds.xy) * _GroundMaskBounds.zw
                    : sourceXZ / 1024.0 + 0.5;
                half2 mask = SAMPLE_TEXTURE2D(_GroundMask, sampler_GroundMask, maskUV).rg;
                float footprint = max(length(ddx(sourceXZ)), length(ddy(sourceXZ)));

                float broadGrowth = GrassNoise(sourceXZ / 39.0);
                float patchGrowth = GrassNoise(sourceXZ / 5.5 + 47.1);
                float dryPatches = smoothstep(0.34, 0.76, broadGrowth * 0.75 + patchGrowth * 0.25);
                half3 turf = lerp(half3(0.137, 0.182, 0.077), half3(0.206, 0.224, 0.105), dryPatches);

                float stripe = mask.r - 128.0 / 255.0;
                if (_UseLayoutMask < 0.5)
                {
                    // Las Vegas keeps its original fitted mower passes.
                    float curveX = min(abs(sourceXZ.x), 185.0);
                    float curve = 0.00115 * curveX * (2.0 * abs(sourceXZ.x) - curveX);
                    float mowPhase = (sourceXZ.y - curve) / 13.0;
                    stripe = (smoothstep(-0.36, 0.36, cos(mowPhase * TWO_PI)) - 0.5)
                        * mask.r * (1.0 - smoothstep(0.10, 0.45, fwidth(mowPhase)));
                }
                turf *= 1.0 + stripe * 0.19;

                float clumps = GrassNoise(sourceXZ * 1.65 + 25.2) - 0.5;
                float grain = GrassNoise(sourceXZ * float2(9.4, 14.8) + 93.7) - 0.5;
                turf *= 1.0 + clumps * 0.23 * (1.0 - smoothstep(0.12, 0.72, footprint));
                turf *= 1.0 + grain * 0.35 * (1.0 - smoothstep(0.022, 0.11, footprint));

                float wear = mask.g;
                if (_UseExtraWear > 0.5)
                {
                    float2 extraUV = (localXZ - _ExtraWearBounds.xy) * _ExtraWearBounds.zw;
                    wear = max(wear, SAMPLE_TEXTURE2D(_ExtraWearMask, sampler_ExtraWearMask, extraUV).g);
                }
                float shoulder = wear * lerp(0.20, 0.95, GrassNoise(sourceXZ * 0.76 + 19.6));
                return lerp(turf, half3(0.30, 0.262, 0.122), shoulder * 0.68);
            }

            half4 GrassFragment(Varyings input) : SV_Target
            {
                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.positionCS = input.positionCS;
                lighting.normalWS = NormalizeNormalPerPixel(input.normalWS);
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    lighting.shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                lighting.vertexLighting = input.fogAndVertexLight.yzw;
                lighting.bakedGI = SampleSH(lighting.normalWS);
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = GrassAlbedo(input.sourceXZ, input.localXZ);
                surface.smoothness = 0.08;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1;
                surface.alpha = 1;
                half4 color = UniversalFragmentPBR(lighting, surface);
                color.rgb = MixFog(color.rgb, input.fogAndVertexLight.x);
                return color;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
