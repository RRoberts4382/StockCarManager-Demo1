Shader "StockCarManager/TrackSmoke"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
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

            half Puff(float2 puffPosition, float2 center, float radius)
            {
                float2 delta = (puffPosition - center) / radius;
                half density = saturate(1.0 - dot(delta, delta));
                return density * density;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 puffPosition = input.uv * 2.0 - 1.0;
                half alpha = Puff(puffPosition, float2(-.22, .14), .65) * .65
                           + Puff(puffPosition, float2(.22, .18), .61) * .58
                           + Puff(puffPosition, float2(.12, -.25), .64) * .62
                           + Puff(puffPosition, float2(-.30, -.23), .54) * .35;
                return half4(input.color.rgb, input.color.a * saturate(alpha));
            }
            ENDHLSL
        }
    }
}
