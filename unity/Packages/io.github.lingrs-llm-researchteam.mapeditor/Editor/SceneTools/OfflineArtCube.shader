Shader "Hidden/Arknights/OfflineArtCube"
{
    Properties { _Cube ("Cube map", Cube) = "" {} }
    SubShader {
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            samplerCUBE _Cube;
            float _Applied;
            struct vertex_output {
                float4 position : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPosition : TEXCOORD1;
            };

            vertex_output vert(appdata_base input) {
                vertex_output output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                return output;
            }

            fixed4 frag(vertex_output input) : SV_Target {
                float3 normal = normalize(input.worldNormal);
                float3 view = normalize(_WorldSpaceCameraPos - input.worldPosition);
                float3 reflected = reflect(-view, normal);
                float3 reflection = texCUBE(_Cube, reflected).rgb;
                float3 gray = float3(.33, .38, .42) * (0.6 + 0.4 * saturate(normal.y));
                return fixed4(lerp(gray, reflection, _Applied), 1);
            }
            ENDCG
        }
    }
}
