Shader "Hidden/StockCar/ShopBackgroundBlur"
{
    Properties
    {
        _MainTex ("Shop Render", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Blur
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float2 _BlurStep;

            half4 Blur(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                half4 color = tex2D(_MainTex, uv) * 0.227027;
                color += (tex2D(_MainTex, saturate(uv + _BlurStep)) +
                          tex2D(_MainTex, saturate(uv - _BlurStep))) * 0.194595;
                color += (tex2D(_MainTex, saturate(uv + 2 * _BlurStep)) +
                          tex2D(_MainTex, saturate(uv - 2 * _BlurStep))) * 0.121622;
                color += (tex2D(_MainTex, saturate(uv + 3 * _BlurStep)) +
                          tex2D(_MainTex, saturate(uv - 3 * _BlurStep))) * 0.054054;
                color += (tex2D(_MainTex, saturate(uv + 4 * _BlurStep)) +
                          tex2D(_MainTex, saturate(uv - 4 * _BlurStep))) * 0.016216;
                return color;
            }
            ENDCG
        }
    }
}
