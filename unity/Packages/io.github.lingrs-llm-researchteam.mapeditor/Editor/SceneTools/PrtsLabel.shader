Shader "Hidden/ArknightsMapEditor/PrtsLabel"
{
 Properties { _MainTex ("Glyph atlas", 2D) = "white" {} }
 SubShader
 {
  Tags { "Queue"="Transparent+20" "RenderType"="Transparent" }
  Cull Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha
  Pass
  {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   sampler2D _MainTex;
   struct a { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
   struct v { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
   v vert(a i) { v o; o.vertex=UnityObjectToClipPos(i.vertex);o.uv=i.uv;o.color=i.color;return o; }
   fixed4 frag(v i):SV_Target { return fixed4(i.color.rgb,i.color.a*tex2D(_MainTex,i.uv).a); }
   ENDCG
  }
 }
}
