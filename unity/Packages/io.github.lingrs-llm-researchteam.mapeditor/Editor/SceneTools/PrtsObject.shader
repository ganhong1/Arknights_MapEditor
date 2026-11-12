Shader "Hidden/ArknightsMapEditor/PrtsObject"
{
 Properties { _MainTex ("Icon atlas", 2D) = "white" {} }
 SubShader
 {
  Tags { "Queue"="Transparent+10" "RenderType"="Transparent" }
  Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
  Pass
  {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   sampler2D _MainTex;
   struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float2 local:TEXCOORD1; };
   struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float2 local:TEXCOORD1; };
   v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.local=v.local; return o; }
   fixed4 frag(v2f i):SV_Target
   {
    fixed4 color=tex2D(_MainTex,i.uv);
    float radius=length(i.local-.5);
    // CSS: a 40px image inside a 3px transparent border, border-radius:50%.
    color.a*=1-smoothstep(.575-fwidth(radius),.575,radius);
    return color;
   }
   ENDCG
  }
 }
}
