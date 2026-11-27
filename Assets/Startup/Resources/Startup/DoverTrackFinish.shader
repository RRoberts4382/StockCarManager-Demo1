Shader "StockCarManager/Dover Track Finish"
{
    Properties
    {
        _BaseColor("Base color", Color) = (0.45,0.45,0.43,1)
        [HideInInspector] _BaseMap("Surface", 2D) = "white" {}
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _Cutoff("Cutoff", Float) = .5
        [HideInInspector] _FinishKind("Finish", Float) = 0
        [HideInInspector] _UseGroove("Use groove", Float) = 0
        [NoScaleOffset] _Groove("Preferred-line rubber", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "Dover surface forward"
            Tags { "LightMode" = "UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Groove);
            SAMPLER(sampler_Groove);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4x4 _WorldToTrackSource;
                float _FinishKind;
                float _UseGroove;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Interpolators
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 source : TEXCOORD2;
                float fog : TEXCOORD3;
            };

            Interpolators Vert(Attributes input)
            {
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                Interpolators result;
                result.positionCS = position.positionCS;
                result.positionWS = position.positionWS;
                result.normalWS = TransformObjectToWorldNormal(input.normalOS);
                result.source = mul(_WorldToTrackSource, float4(position.positionWS, 1.0)).xyz;
                result.fog = ComputeFogFactor(position.positionCS.z);
                return result;
            }

            float Hash(float2 cell)
            {
                float3 p = frac(float3(cell.xyx) * .1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float Noise(float2 position)
            {
                float2 cell = floor(position);
                float2 t = frac(position);
                t = t * t * (3.0 - 2.0 * t);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1,0)), t.x),
                    lerp(Hash(cell + float2(0,1)), Hash(cell + 1), t.x), t.y);
            }

            // The source datum is the approved symmetric oval, in meters.
            float2 StationAndLateral(float2 position)
            {
                const float halfStraight = 163.9824;
                const float radius = 151.74062;
                const float straight = 2.0 * halfStraight;
                const float turn = PI * radius;
                float x = position.x;
                float z = position.y;
                if (x > halfStraight)
                    return float2(straight + radius * atan2(x - halfStraight, z),
                         length(float2(x - halfStraight, z)) - radius);
                if (x < -halfStraight)
                    return float2(2.0 * straight + turn + radius * atan2(-x - halfStraight, -z),
                         length(float2(-x - halfStraight, z)) - radius);
                float station = z >= 0.0 ? x + halfStraight : straight + turn + halfStraight - x;
                return float2(station, abs(z) - radius);
            }

            float Joint(float span, float spacing, float lineWidth)
            {
                float fractionalSpan = frac(span / spacing);
                float distance = min(fractionalSpan, 1.0 - fractionalSpan) * spacing;
                float edge = min(.12, max(.004, .65 * fwidth(span)));
                return (1.0 - smoothstep(lineWidth, lineWidth + edge, distance)) *
                    (1.0 - smoothstep(.05, .22, edge));
            }

            half4 Frag(Interpolators input) : SV_Target
            {
                bool wall = _FinishKind > 1.5;
                float2 position = input.source.xz;
                float2 track = StationAndLateral(position);
                float2 grainPlane = wall ? float2(track.x, input.source.y) : position;
                float screenSize = max(length(ddx(grainPlane)), length(ddy(grainPlane)));
                float fineMix = 1.0 - smoothstep(.026, .17, screenSize);
                float broad = Noise(grainPlane / (wall ? float2(3.9, 1.8) : float2(19.0, 8.6)));
                float fine = Noise(grainPlane * (wall ? 42.0 : 36.0)) - .5;
                float3 albedo = _BaseColor.rgb * (.90 + .19 * broad + fine * fineMix * .15);
                float3 normal = normalize(input.normalWS);

                float2 bumpUV = grainPlane * 13.0;
                float2 grade = float2(Noise(bumpUV + float2(.37,0)) - Noise(bumpUV - float2(.37,0)),
                    Noise(bumpUV + float2(0,.37)) - Noise(bumpUV - float2(0,.37)));
                float3 tangent = normalize(wall ? cross(float3(0,1,0), normal) + float3(.0001,0,0) :
                    float3(1,0,0) - normal * normal.x);
                float3 bitangent = normalize(cross(normal, tangent));
                normal = normalize(normal + .13 * fineMix * (grade.x * tangent + grade.y * bitangent));

                if (wall)
                {
                    float concreteFace = 1.0 - smoothstep(.72, .94, abs(normal.y));
                    float seam = Joint(track.x, 3.6, .011) * concreteFace;
                    float pour = Hash(float2(floor(track.x / 3.6), 4));
                    albedo *= (.947 + pour * .095) * (1.0 - seam * .21);
                    float crossSlope = smoothstep(7.34, 8.84, track.y);
                    float height = input.source.y - (_FinishKind > 2.5 ? lerp(2.35, 7.25, crossSlope) : .06);
                    float dust = (1.0 - smoothstep(.04, .22, height)) * .12;
                    float scrape = (1.0 - smoothstep(.07, .15, abs(height - .42))) *
                        smoothstep(.67, .82, Noise(float2(track.x * .38, height * 10.0)));
                    albedo *= 1.0 - concreteFace * (dust + .18 * scrape);
                    albedo *= 1.0 + .055 * smoothstep(.60, .93, normal.y);
                }
                else if (_FinishKind > .5)
                {
                    float joints = max(Joint(track.x, 6.1, .008), Joint(track.y + 1.2, 3.72, .009));
                    albedo *= 1.0 - joints * .16;
                    if (_UseGroove > .5)
                    {
                        float groove = SAMPLE_TEXTURE2D(_Groove, sampler_Groove, float2(frac(track.x / 1609.344), .5)).r;
                        float darkening = 1.0 - smoothstep(.21, 1.72, abs(track.y - (groove * 40.0 - 20.0)));
                        albedo *= 1.0 - darkening * (.065 + .10 * Noise(float2(track.x / 39.0, 0)));
                    }
                }
                else
                {
                    float patches = smoothstep(.54, .84, Noise(position * float2(.104, .038) + 9.3));
                    albedo *= 1.0 - .13 * patches;
                }

                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float amount = saturate(dot(normal, sun.direction)) * sun.distanceAttenuation * sun.shadowAttenuation;
                half3 sky = max(half3(.13, .13, .13), SampleSH(normal));
                half3 lit = albedo * (sky + sun.color * amount);
                return half4(MixFog(lit, input.fog), 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
