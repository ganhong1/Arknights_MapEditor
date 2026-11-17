Shader "Hidden/Arknights/OfflineArtSurface"
{
    Properties {
        _Color ("Surface", Color) = (1,1,1,1)
        _ShadeTint ("Shade", Color) = (1,1,1,1)
        _FogColor ("Height fog", Color) = (1,1,1,1)
    }
    SubShader {
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct vertex_output {
                float4 position : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPosition : TEXCOORD1;
            };

            fixed4 _Color, _ShadeTint, _FogColor;
            float4 _FogRange;
            float _Applied, _FogActive;

            vertex_output vert(appdata_base input) {
                vertex_output output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                return output;
            }

            fixed4 frag(vertex_output input) : SV_Target {
                float key = saturate(dot(normalize(input.worldNormal), normalize(float3(-0.5, 0.9, -0.55))));
                float3 tint = lerp(1, _ShadeTint.rgb, (1 - key) * _Applied);
                float3 color = _Color.rgb * (0.45 + 0.55 * key) * tint;
                float height = 1 - saturate((input.worldPosition.y - _FogRange.x) / max(0.05, _FogRange.y - _FogRange.x));
                float fog = height * _Applied * _FogActive * _FogColor.a * 0.82;
                return fixed4(lerp(color, _FogColor.rgb, fog), 1);
            }
            ENDCG
        }
    }
}
