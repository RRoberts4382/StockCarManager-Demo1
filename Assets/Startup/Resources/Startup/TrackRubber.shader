Shader "StockCarManager/TrackRubber"
{
    Properties { _Buildup ("Rubber buildup", Range(0, 1)) = 0 }
    SubShader
    {
        Tags { "Queue"="Transparent-20" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend DstColor Zero
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Buildup;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half strength : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.strength = input.color.a;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half shade = 1.0h - input.strength * _Buildup;
                return half4(shade, shade, shade, 1.0h);
            }
            ENDHLSL
        }
    }
}
