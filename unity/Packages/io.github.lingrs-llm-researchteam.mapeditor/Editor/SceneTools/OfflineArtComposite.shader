Shader "Hidden/Arknights/OfflineArtComposite"
{
    Properties {
        _MainTex ("Preview frame", 2D) = "white" {}
        _VignetteColor ("Vignette color", Color) = (0,0,0,1)
        _Filter ("Filter", Color) = (1,1,1,1)
    }
    SubShader {
        Cull Off ZWrite Off ZTest Always
        Pass {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize, _Vignette, _Bloom, _Grading;
            fixed4 _VignetteColor, _Filter;
            float _Dither;

            float3 Bright(float2 uv) {
                return max(0, tex2D(_MainTex, uv).rgb - _Bloom.y);
            }

            fixed4 frag(v2f_img input) : SV_Target {
                float2 uv = input.uv;
                float3 color = tex2D(_MainTex, uv).rgb;
                if (_Bloom.x > 0) {
                    float2 step = _MainTex_TexelSize.xy * _Bloom.z;
                    float3 halo = Bright(uv + float2(step.x, 0)) + Bright(uv - float2(step.x, 0)) +
                                  Bright(uv + float2(0, step.y)) + Bright(uv - float2(0, step.y));
                    color += halo * _Bloom.x * 0.16;
                }
                if (_Grading.w > 0) {
                    color = (color - 0.5) * _Grading.z + 0.5;
                    color = lerp(dot(color, float3(.2126, .7152, .0722)), color, _Grading.y);
                    color *= _Grading.x * _Filter.rgb;
                }
                float distance = length((uv - _Vignette.zw) * 1.45);
                float vignette = smoothstep(0.2, max(0.25, 1.1 - _Vignette.y * 0.55), distance) * _Vignette.x;
                color = lerp(color, _VignetteColor.rgb, vignette);
                float2 cell = floor(uv * _MainTex_TexelSize.zw / 2.0);
                float threshold = frac((cell.x * 0.5 + cell.y * 0.25) + floor(cell.x / 2.0) * 0.25);
                float pattern = step(threshold, _Dither * 0.75) * step(0.001, _Dither);
                return fixed4(lerp(color, float3(.09, .12, .16), pattern * .83), 1);
            }
            ENDCG
        }
    }
}
