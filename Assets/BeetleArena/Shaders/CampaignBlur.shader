Shader "Hidden/BugDuel/CampaignBlur"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader {
        Cull Off ZWrite Off ZTest Always
        Pass {
            Name "Gaussian"
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float2 _Direction;
            half4 frag(v2f_img i) : SV_Target {
                float2 d = _Direction * _MainTex_TexelSize.xy;
                // Normalized 17-tap Gaussian (sigma 3.5 texels), paired into nine bilinear reads.
                // Keep Direction at one texel: stretching these offsets creates gaps in the kernel.
                half4 color = tex2D(_MainTex,i.uv) * .1156976293;
                color += (tex2D(_MainTex,i.uv+d*1.4694259472)+tex2D(_MainTex,i.uv-d*1.4694259472)) * .2093399651;
                color += (tex2D(_MainTex,i.uv+d*3.4290534031)+tex2D(_MainTex,i.uv-d*3.4290534031)) * .1403434741;
                color += (tex2D(_MainTex,i.uv+d*5.3896034046)+tex2D(_MainTex,i.uv-d*5.3896034046)) * .0683210800;
                color += (tex2D(_MainTex,i.uv+d*7.3515472766)+tex2D(_MainTex,i.uv-d*7.3515472766)) * .0241466662;
                return color;
            }
            ENDHLSL
        }
        Pass {
            Name "Prefilter"
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            half4 frag(v2f_img i) : SV_Target {
                // Filter both axes at each 2:1 reduction instead of skipping fine scene details.
                float2 d = _MainTex_TexelSize.xy * .5;
                return (tex2D(_MainTex,i.uv+float2(-d.x,-d.y))
                      + tex2D(_MainTex,i.uv+float2( d.x,-d.y))
                      + tex2D(_MainTex,i.uv+float2(-d.x, d.y))
                      + tex2D(_MainTex,i.uv+float2( d.x, d.y))) * .25;
            }
            ENDHLSL
        }
    }
}
