Shader "StockCarManager/Track Body Scuff"
{
    SubShader
    {
        Tags { "Queue"="Transparent+2" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half opacity : TEXCOORD1;
                half scuff : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.opacity = input.color.a;
                output.scuff = input.color.r;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv;
                float grain = frac(sin(dot(floor(p * float2(76, 126)), float2(34.87, 57.21))) * 21758.19);
                if (input.scuff < .5h)
                {
                    // Three broad rubber arcs with staggered ends.
                    float angle = atan2(p.y, p.x);
                    float radius = length(p);
                    float wobble = .024 * sin(angle * 4 + .5) + .022 * (grain - .5);
                    float outer = (1 - smoothstep(.057, .120, abs(radius - (.83 + wobble)))) *
                        smoothstep(-.64, -.38, cos(angle - .23));
                    float middle = (1 - smoothstep(.057, .120, abs(radius - (.56 + wobble)))) *
                        smoothstep(-.52, -.27, cos(angle + .20));
                    float inner = (1 - smoothstep(.057, .120, abs(radius - (.29 + wobble)))) *
                        smoothstep(-.39, -.13, cos(angle + .62));
                    half rubber = input.opacity * max(outer, max(.96 * middle, .90 * inner)) * (.60 + .40 * grain);
                    clip(rubber - .004);
                    return half4(.017h, .014h, .012h, rubber);
                }
                float edge = 1 - smoothstep(.19, 1.0, dot(p,p));
                float streak = smoothstep(.43, .83, sin((p.y + .07 * sin(p.x * 4)) * 54 + grain * .58) * .5 + .5);
                float chips = smoothstep(.79, 1, grain) * streak;
                half3 shade = lerp(half3(.05, .043, .036), half3(.56, .53, .47), chips);
                float opacity = input.opacity * edge * (.36 + .59 * streak + .05 * grain);
                clip(opacity - .003);
                return half4(shade, opacity);
            }
            ENDHLSL
        }
    }
}
