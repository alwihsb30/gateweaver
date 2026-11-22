Shader "GateWeaver/ARBackdropDimming"
{
    Properties
    {
        _MainTex ("Camera Feed (RGB)", 2D) = "white" {}
        _DimBrightness ("Perceptual Brightness", Range(0.25, 1.0)) = 0.70
        _Desaturation ("Desaturation Amount", Range(0.0, 1.0)) = 0.18
        _BackdropTint ("Backdrop Tint", Color) = (0.94, 0.97, 1.00, 1.0)
        _VignetteColor ("Vignette Color", Color) = (0.015, 0.035, 0.070, 1.0)
        _VignetteRadius ("Vignette Inner Radius", Range(0.10, 1.40)) = 0.58
        _VignetteSoftness ("Vignette Softness", Range(0.05, 1.20)) = 0.65
        _VignetteStrength ("Vignette Strength", Range(0.0, 1.0)) = 0.40
        _ForceLinearize ("Optional Non-sRGB Linearize", Range(0.0, 1.0)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background+500"
            "RenderType" = "Opaque"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        LOD 100

        Pass
        {
            Name "AR_BACKDROP_DIMMING"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend Off
            Fog { Mode Off }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            half _DimBrightness;
            half _Desaturation;
            half4 _BackdropTint;
            half4 _VignetteColor;
            half _VignetteRadius;
            half _VignetteSoftness;
            half _VignetteStrength;
            half _ForceLinearize;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                half linearDimGain : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.screenPos = ComputeScreenPos(o.pos);

                float safeDim = clamp((float)_DimBrightness, 1e-4, 1.0);
                #ifdef UNITY_COLORSPACE_GAMMA
                    o.linearDimGain = (half)safeDim;
                #else
                    o.linearDimGain = (half)pow(max(safeDim, 1e-4), 2.2);
                #endif
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half3 cam = max(tex2D(_MainTex, i.uv).rgb, half3(0.0h, 0.0h, 0.0h));

                // Opsional: kompensasi non-sRGB tanpa pow() per-fragmen (default 0 = identik baseline Sprites/Default)
                cam = lerp(cam, cam * cam, saturate(_ForceLinearize));

                // Turunkan saturasi ~15-20% menggunakan bobot luminansi Rec.709 Linear
                half luma = dot(cam, half3(0.2126h, 0.7152h, 0.0722h));
                cam = lerp(cam, luma.xxx, saturate(_Desaturation));

                // Redupkan kecerahan latar terkalibrasi Linear Color Space + tint dingin halus
                cam *= i.linearDimGain * _BackdropTint.rgb;

                // Vignette radial berbasis Screen-Space NDC [-1, 1] (kebal mirror, rotasi 90/270, & aspect-fill 20:9)
                float invW = 1.0 / max(abs(i.screenPos.w), 1e-4);
                half2 screenUV = (half2)(i.screenPos.xy * invW);
                half2 centered = (screenUV * 2.0h) - 1.0h;
                half dist = sqrt(max(dot(centered, centered), 1e-4h));
                half outerRadius = _VignetteRadius + max(_VignetteSoftness, 1e-2h);
                half vig = smoothstep(_VignetteRadius, outerRadius, dist);
                cam = lerp(cam, _VignetteColor.rgb, saturate(vig * _VignetteStrength));

                return half4(cam, 1.0h);
            }
            ENDCG
        }
    }

    Fallback Off
}
