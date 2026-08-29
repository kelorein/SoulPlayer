Shader "SoulPlayer/Recorder Hands PBR"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0,2)) = 1
        _MetallicGlossMap ("Metallic (RGB) Smoothness (A)", 2D) = "black" {}
        _Metallic ("Metallic", Range(0,1)) = 0
        _GlossMapScale ("Smoothness", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _BumpMap;
        sampler2D _MetallicGlossMap;
        fixed4 _Color;
        half _BumpScale;
        half _Metallic;
        half _GlossMapScale;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 albedo = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            fixed4 pbr = tex2D(_MetallicGlossMap, IN.uv_MainTex);
            o.Albedo = albedo.rgb;
            o.Alpha = albedo.a;
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);
            o.Metallic = saturate(pbr.r + _Metallic);
            o.Smoothness = saturate(pbr.a * _GlossMapScale);
        }
        ENDCG
    }

    FallBack "Diffuse"
}
