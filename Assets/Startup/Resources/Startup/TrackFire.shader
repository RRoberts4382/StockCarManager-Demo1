Shader "StockCarManager/TrackFire"
{
    SubShader
    {
        Tags { "Queue"="Transparent-5" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

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
                float height = input.uv.y;
                float centerOffset = sin(height * 7.5 + _Time.y * 19.0) * height * .13;
                float width = lerp(.46, .055, height);
                float side = abs(input.uv.x - .5 - centerOffset) / width;
                half opacity = (1.0h - smoothstep(.38h, 1.0h, side)) *
                    smoothstep(0.0h, .18h, height) * (1.0h - smoothstep(.62h, 1.0h, height));
                half core = (1.0h - smoothstep(.05h, .65h, side)) *
                    (1.0h - smoothstep(.18h, .77h, height));
                half3 flame = lerp(half3(1.0h, .19h, .012h), half3(1.0h, .92h, .37h), core);
                return half4(flame * input.color.rgb, opacity * input.color.a);
            }
            ENDHLSL
        }
    }
}
