Shader "StockCarManager/TrackFireworks"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off
            ZWrite Off
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half2 offset = input.uv * 2.0h - 1.0h;
                half distanceSquared = dot(offset, offset);
                half glow = saturate(1.0h - distanceSquared);
                half core = 1.0h - smoothstep(.03h, .25h, distanceSquared);
                half3 color = lerp(input.color.rgb, half3(1.0h, 1.0h, 1.0h), core * .36h);
                return half4(color * (.90h + 1.2h * core), input.color.a * glow * glow);
            }
            ENDHLSL
        }
    }
}
