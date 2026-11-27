Shader "TrickShot/Opening Sunset"
{
    Properties
    {
        _Zenith ("Upper sky", Color) = (.16,.20,.38,1)
        _Horizon ("Twilight horizon", Color) = (.83,.50,.48,1)
        _Amber ("Western glow", Color) = (1,.57,.28,1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Interpolator { float4 pos:SV_POSITION; float3 ray:TEXCOORD0; };
            fixed4 _Zenith, _Horizon, _Amber;
            Interpolator vert(appdata_base v)
            {
                Interpolator o;
                o.pos=UnityObjectToClipPos(v.vertex);
                o.ray=v.vertex.xyz;
                return o;
            }
            half4 frag(Interpolator i):SV_Target
            {
                float3 d=normalize(i.ray);
                float alignment=saturate(dot(d,normalize(float3(-.81,.14,-.57))));
                half3 horizon=lerp(_Horizon.rgb,_Amber.rgb,pow(alignment,4));
                half3 sky=lerp(horizon,_Zenith.rgb,smoothstep(-.03,.69,d.y));
                sky+=_Amber.rgb*(.27*pow(alignment,60)+.85*smoothstep(.99977,.9999,alignment));
                return half4(sky,1);
            }
            ENDCG
        }
    }
}
