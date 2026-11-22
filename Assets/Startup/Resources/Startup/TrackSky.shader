Shader "StockCarManager/Track Sky"
{
    Properties
    {
        _Night ("Night", Range(0, 1)) = 0
        _CloudSeed ("Cloud Seed", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
        }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Night;
                float _CloudSeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionCS.z = UNITY_RAW_FAR_CLIP_VALUE * output.positionCS.w;
                output.directionWS = TransformObjectToWorldDir(input.positionOS.xyz, false);
                return output;
            }

            float Hash(float2 cell)
            {
                float3 value = frac(float3(cell.x, cell.y, cell.x) * 0.1031);
                value += dot(value, value.yzx + 19.19);
                return frac((value.x + value.y) * value.z);
            }

            float Noise(float2 position)
            {
                float2 cell = floor(position);
                float2 blend = frac(position);
                blend = blend * blend * (3.0 - 2.0 * blend);
                float lower = lerp(Hash(cell), Hash(cell + float2(1, 0)), blend.x);
                float upper = lerp(Hash(cell + float2(0, 1)), Hash(cell + 1), blend.x);
                return lerp(lower, upper, blend.y);
            }

            float Cloud(float3 direction)
            {
                float2 plane = direction.xz / max(direction.y + 0.18, 0.22);
                float2 offset = float2(Hash(float2(_CloudSeed, 18.7)), Hash(float2(4.1, _CloudSeed))) * 52.0;
                float2 position = float2(plane.x * 1.8, plane.y * 2.8) + offset;
                float large = Noise(position);
                float detail = Noise(position * 2.07 + 4.3);
                float wisps = Noise(position * 4.37 - 8.6);
                float shape = 0.63 * large + 0.26 * detail + 0.11 * wisps;
                float cloud = smoothstep(0.46, 0.71, shape);
                return cloud * smoothstep(0.02, 0.18, direction.y);
            }

            float Stars(float3 direction)
            {
                float2 sky = float2(atan2(direction.z, direction.x) * 0.159154943 + 0.5,
                    asin(clamp(direction.y, -1.0, 1.0)) * 0.318309886 + 0.5);
                float2 position = sky * float2(192.0, 96.0);
                float2 cell = floor(position);
                cell.x -= floor(cell.x / 192.0) * 192.0;
                float chance = Hash(cell + 27.3);
                float2 center = 0.22 + 0.56 * float2(Hash(cell + 65.8), Hash(cell - 41.7));
                float radius = lerp(0.030, 0.057, Hash(cell + 14.8));
                float pixelWidth = clamp(max(fwidth(position.x), fwidth(position.y)) * 0.5, 0.014, 0.23);
                float distanceToStar = length(frac(position) - center);
                float shape = 1.0 - smoothstep(max(0.0, radius - pixelWidth), radius + pixelWidth, distanceToStar);
                float area = saturate(radius / max(pixelWidth, radius));
                return step(0.985, chance) * shape * area * area * smoothstep(0.10, 0.32, direction.y);
            }

            float3 Moon(float3 direction)
            {
                const float moonRadius = 0.0047;
                float3 moonDirection = normalize(float3(0.762, 0.289, -0.579));
                float delta = length(direction - moonDirection);
                float feather = max(fwidth(delta) * 0.55, 0.00025);
                float disc = 1.0 - smoothstep(moonRadius - feather, moonRadius + feather, delta);
                float3 side = cross(float3(0, 1, 0), moonDirection);
                float variation = saturate(0.80 + dot(direction, side) * 65.0);
                float core = disc * variation;
                float halo = 0.04 * (1.0 - smoothstep(moonRadius, 0.046, delta));
                return (core + halo) * float3(0.67, 0.72, 0.80);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.directionWS);
                float elevation = saturate(direction.y);
                float aboveHorizon = smoothstep(-0.15, 0.02, direction.y);
                float cloud = Cloud(direction);

                // The horizon carries most of the atmosphere's light.
                float3 day = lerp(float3(0.78, 0.85, 0.90), float3(0.29, 0.56, 0.80), sqrt(elevation));
                day = lerp(float3(0.41, 0.47, 0.51), day, aboveHorizon);
                float3 daylightCloud = lerp(float3(0.91, 0.94, 0.96), float3(0.98, 0.98, 0.97), cloud);
                day = lerp(day, daylightCloud, cloud * 0.87);

                float3 night = lerp(float3(0.19, 0.24, 0.34), float3(0.020, 0.038, 0.085), sqrt(elevation));
                night = lerp(float3(0.036, 0.054, 0.080), night, aboveHorizon);
                float3 nightCloud = float3(0.12, 0.15, 0.22);
                night = lerp(night, nightCloud, cloud * 0.31);
                if (_Night > 0.001)
                {
                    float3 stars = Stars(direction) * float3(0.40, 0.43, 0.48);
                    night += (stars + Moon(direction)) * (1.0 - cloud * 0.94);
                }

                float3 color = saturate(lerp(day, night, _Night));
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    color = SRGBToLinear(color);
                #endif
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
