Shader "StockCarManager/TrackSkidMark"
{
    SubShader
    {
        Tags { "Queue"="Transparent-17" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend DstColor Zero
            ColorMask RGB

            // Reserve a URP user bit so an overlap darkens each visible sample once.
            Stencil
            {
                Ref 4
                ReadMask 4
                WriteMask 4
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half darkness : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.darkness = input.color.a;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half shade = 1.0h - input.darkness;
                return half4(shade, shade, shade, 1.0h);
            }
            ENDHLSL
        }
    }
}
