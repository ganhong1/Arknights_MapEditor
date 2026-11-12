Shader "Hidden/ArknightsMapEditor/Skeleton"
{
    Properties {
        [HideInInspector] _ZWrite ("Depth write", Float) = 1
        [HideInInspector] _TerrainSymbols ("Terrain symbols", 2D) = "black" {}
    }
    SubShader {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass {
            Cull Off ZWrite [_ZWrite]
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _TerrainSymbols;
            struct appdata { float4 vertex:POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float2 style:TEXCOORD1; float symbol:TEXCOORD2; };
            struct v2f { float4 pos:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float2 style:TEXCOORD1; float symbol:TEXCOORD2; };
            v2f vert(appdata v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv; o.style=v.style; o.symbol=v.symbol; return o; }
            float symbolMask(float2 uv, float index)
            {
                if (index < .5) return 0;
                float slot = floor(index + .5) - 1;
                float2 cell = float2(fmod(slot, 8), 7 - floor(slot / 8));
                return tex2D(_TerrainSymbols, (cell + clamp(uv, .0078125, .9921875)) / 8).a;
            }
            float strokeMask(float value,float width,float aa) { return 1-smoothstep(width-aa,width+aa,abs(value)); }
            // CSS uses a 48px tile. Express the original gradient stops in
            // that coordinate space before the Unity camera projects the face.
            float stripes(float2 uv, float2 direction, float period, float solid, float fade)
            {
                // CSS gradients are measured downwards; mesh V grows towards the back row.
                uv.y = 1-uv.y;
                float2 pixels = (uv - .5) * 48;
                float start = dot(abs(direction), float2(24,24));
                float distance = dot(pixels, direction) + start;
                return 1 - smoothstep(solid, fade, frac(distance / period) * period);
            }
            float crossed(float2 uv, float period, float solid, float fade, float opacity)
            {
                float first = stripes(uv, float2(.707107,.707107), period, solid, fade) * opacity;
                float second = stripes(uv, float2(.707107,-.707107), period, solid, fade) * opacity;
                return first + second * (1 - first);
            }
            // Supplemental texture is subordinate to the semantic symbol.
            float editorMotif(int kind, float2 uv, float aa)
            {
                float2 p = uv - .5;
                float2 q = abs(p);
                float outer = max(q.x, q.y);
                float radius = length(p);
                if (kind == 33) return strokeMask(outer-.35,.017,aa)*smoothstep(.20,.26,min(q.x,q.y));
                if (kind == 34) return stripes(uv+float2(0,sin(uv.x*12.566)*.044),float2(0,1),11,1.5,2.8);
                if (kind == 35) return max(strokeMask(radius-.33,.018,aa),strokeMask(radius-.39,.009,aa)*.55);
                if (kind == 36)
                {
                    float2 grid = abs(frac(uv*4+.5)-.5);
                    return (1-smoothstep(.025,.10,min(grid.x,grid.y)))*smoothstep(.16,.30,outer);
                }
                if (kind == 37) return stripes(uv,float2(.707107,-.707107),9,2.2,3.8)*.75;
                if (kind == 38) return crossed(uv,10,2.1,3.6,.65);
                if (kind == 39)
                {
                    float left = length((p+float2(.14,0))*float2(1,1.35));
                    float right = length((p-float2(.14,0))*float2(1,1.35));
                    return max(strokeMask(left-.23,.018,aa),strokeMask(right-.23,.018,aa));
                }
                if (kind == 40)
                    return max(strokeMask(p.y-q.x*.6-.20,.024,aa),strokeMask(p.y+q.x*.6+.20,.024,aa))*step(q.x,.34);
                if (kind == 41)
                {
                    float2 dotCell = frac(uv*5)-.5;
                    return (1-smoothstep(.12,.23,length(dotCell)))*smoothstep(.17,.33,outer);
                }
                if (kind == 42)
                {
                    float zig = p.y - .22*sin(p.x*12.6);
                    return max(strokeMask(zig,.020,aa),strokeMask(p.x+p.y*.34-.13,.012,aa)*step(.04,p.y));
                }
                return max(strokeMask(outer-.34,.018,aa),strokeMask(outer-.40,.008,aa)*.60);
            }

            fixed4 frag(v2f i):SV_Target {
                float2 p=i.uv-.5; float aa=max(fwidth(i.uv.x),fwidth(i.uv.y))*1.2;
                float edge=min(min(i.uv.x,i.uv.y),min(1-i.uv.x,1-i.uv.y));
                float border=1-smoothstep(.018,.018+aa,edge);
                float3 color=i.color.rgb; int kind=(int)(i.style.x+.5);
                if(kind==30) return fixed4(color,1);
                if(i.style.y<.5) {
                    float3 outline=kind==2?float3(.91,.18,.31):kind==3?float3(.21,.62,.87):
                        kind==12?float3(.58,.58,.58):float3(.24,.24,.24);
                    if (kind==2 || kind==3) return fixed4(outline,lerp(.10,.90,border));
                    return fixed4(lerp(color*.85,outline,border*.72),kind==13?.95:1);
                }
                float ink=0; float3 accent=float3(.70,.74,.78);
                float marker = symbolMask(i.uv, i.symbol);

                if(kind==2||kind==3) {
                    float3 boxColor=kind==2?float3(.91,.18,.31):float3(.21,.62,.87);
                    float3 topOutline=i.style.y>1.5?float3(.87,.87,.87):boxColor;
                    float3 face = lerp(boxColor,topOutline,border);
                    return fixed4(lerp(face,float3(.97,.98,1),marker),max(lerp(.10,.90,border),marker*.96));
                }
                if(kind==4){ink=1-smoothstep(.32,.34,max(abs(p.x),abs(p.y)));accent=float3(.015,.02,.025);}
                if(kind==5){accent=float3(42,148,24)/255;ink=stripes(i.uv,float2(.707107,.707107),8.484,3.535,4.242)*.8;}
                if(kind==20){accent=float3(73,80,60)/255;float first=stripes(i.uv,float2(1,0),8,3,4)*.8;float second=stripes(i.uv,float2(.8813,.4726),8.484,3.535,4.242)*.8;ink=first+second*(1-first);color=lerp(color,float3(.701,.684,.579),.9);}
                if(kind==6){accent=float3(.21,.62,.87);ink=max(step(abs(p.x),.055)*step(abs(p.y),.16),step(abs(p.y),.055)*step(abs(p.x),.16));}
                if(kind==7){accent=float3(.93,.57,.17);ink=strokeMask(abs(p.x)+abs(p.y)-.79,.03,aa);}
                if(kind==8){accent=float3(142,48,24)/255;ink=stripes(i.uv,float2(.707107,-.707107),8.484,3.535,4.242)*.8;}
                if(kind==9||kind==32){accent=float3(26,82,116)/255;ink=stripes(i.uv,float2(.087156,.996195),4.242,1.414,2.121)*.6;color=lerp(color,float3(12,101,131)/255,.2);}
                if(kind==19){accent=float3(19,37,24)/255;ink=crossed(i.uv,8.484,3.535,4.242,.8);color=lerp(color,float3(.446,.534,.5),.8);}
                if(kind==10){accent=float3(170,217,234)/255;ink=crossed(i.uv,8.484,3.535,4.242,.8);color=lerp(color,float3(109,214,255)/255,.65);}

                if(kind==12){ink=smoothstep(.34,.34+aa,max(abs(p.x),abs(p.y)))*.8;accent=float3(.78,.78,.78);}
                if(kind==13){
                    float upper = 1-smoothstep(.145-aa,.145+aa,abs(p.x)+abs(p.y-.5));
                    float lower = 1-smoothstep(.145-aa,.145+aa,abs(p.x)+abs(p.y+.5));
                    ink=max(upper,lower)*.8;accent=float3(224,177,82)/255;
                }

                if(kind==15){
                    // Fixed CSS: 36px white disk and 16px black ::after at left:50%, bottom:0.
                    ink=1-smoothstep(.375,.375+aa,length(p));
                    float dark=1-smoothstep(.166667,.166667+aa,length(p-float2(.166667,-.208333)));
                    accent=lerp(float3(.97255,.97255,.97255),float3(.06275,.06275,.06275),dark);
                }
                if(kind==16){accent=float3(70,190,36)/255;ink=crossed(i.uv,8.484,3.535,4.242,.8);}
                if(kind==17){ink=max(strokeMask(p.x-p.y,.03,aa),strokeMask(p.x+p.y,.03,aa));accent=float3(.85,.32,.66);}

                if(kind==21){accent=float3(.972,.863,.468);ink=(1-frac(i.uv.y*4))*.4;color=lerp(color,float3(.836,.759,.644),.4);}
                if(kind==28){accent=float3(142,48,24)/255;ink=crossed(i.uv,8.484,3.535,4.242,.8);}

                if(kind==22){accent=float3(42,88,184)/255;ink=stripes(i.uv,float2(.707107,-.707107),8.484,3.535,4.242)*.8;}
                if(kind==23){color=lerp(color,float3(220,84,50)/255,.8);ink=crossed(i.uv,8.484,3.535,4.242,.85);accent=float3(.09,.09,.09);}

                if(kind==25||kind==26){
                    float radius=length(p);
                    ink=1-smoothstep(.35,.35+aa,radius);
                    accent=radius>.27?float3(.78,.23,.14):(kind==25?float3(.06,.06,.06):float3(.97,.97,.97));
                }
                if(kind==27){accent=float3(45,45,45)/255;float horizontal=stripes(i.uv,float2(1,0),14,5,6)*.85;float vertical=stripes(i.uv,float2(0,1),14,5,6)*.85;ink=horizontal+vertical*(1-horizontal);}
                if (kind>=33 && kind<=43) {
                    ink=editorMotif(kind,i.uv,aa)*.20;
                    accent=lerp(color,float3(.90,.93,.96),.53);
                }
                color=lerp(color,accent,saturate(ink));color=lerp(color,kind==2||kind==3?accent:(i.style.y>1.5?float3(.87,.87,.87):float3(.25,.25,.25)),border*.85);
                float brightness = dot(i.color.rgb,float3(.2126,.7152,.0722));
                float3 markerColor = brightness > .60 ? float3(.10,.12,.15) : float3(.96,.97,.98);
                color = lerp(color, markerColor, marker);
                float opacity = kind==31||kind==32 ? lerp(.55,.92,border) : kind==13 ? .95 : 1;
                return fixed4(color,opacity);
            }
            ENDCG
        }
    }
}
