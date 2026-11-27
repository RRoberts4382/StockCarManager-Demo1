Shader "StockCarManager/TrackMarbles"
{
    Properties { _Density ("Marble density around the lap", 2D) = "black" {} }
    SubShader
    {
        Tags { "Queue"="Transparent-18" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off
            ZWrite Off
            Blend DstColor Zero
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Density);
            SAMPLER(sampler_Density);

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                return output;
            }

            float Hash(float2 cell)
            {
                float3 value = frac(float3(cell.xyx) * .1031);
                value += dot(value, value.yzx + 33.33);
                return frac((value.x + value.y) * value.z);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half density = SAMPLE_TEXTURE2D(_Density, sampler_Density, float2(input.uv.x, .5)).r;
                // Match the grip gradient from the clean groove to the outside wall.
                half towardWall = saturate(input.uv.y);
                density *= towardWall * towardWall;
                float2 grid = input.positionWS.xz / .075;
                float2 cell = floor(grid);
                float footprint = length(fwidth(grid));
                float2 offset = float2(Hash(cell + 19.3), Hash(cell + 42.7)) * .48 + .26;
                float radius = lerp(.10, .27, Hash(cell + 7.2));
                float distanceToPebble = length(frac(grid) - offset);
                float pebble = 1.0 - smoothstep(radius, radius + max(.02, footprint * .45), distanceToPebble);
                float occupied = 1.0 - step(density * .84, Hash(cell));
                // Fade individual specks with distance to keep the surface from shimmering.
                float detail = 1.0 - smoothstep(.35, 1.3, footprint);
                half shade = 1.0h - density * .14h - pebble * occupied * detail * .65h;
                return half4(shade, shade, shade, 1.0h);
            }
            ENDHLSL
        }
    }
}
