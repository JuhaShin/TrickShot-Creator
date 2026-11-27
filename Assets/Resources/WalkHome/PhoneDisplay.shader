Shader "TrickShot/OpeningPhoneDisplay"
{
    Properties { _MainTex ("Short frames", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Frame;
            struct Varyings { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(appdata_base v)
            {
                Varyings o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }
            fixed4 frag(Varyings i) : SV_Target
            {
                float2 tile = float2(fmod(_Frame, 8), floor(_Frame / 8));
                float2 uv = clamp(i.uv, .006, .994);
                return tex2D(_MainTex, (tile + uv) / float2(8, 4));
            }
            ENDCG
        }
    }
}
