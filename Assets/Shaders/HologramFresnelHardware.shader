Shader "GateWeaver/HologramFresnelHardware"
{
    Properties
    {
        _Color ("Albedo Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Metallic ("Metallic", Range(0.0, 1.0)) = 0.25
        _Glossiness ("Smoothness", Range(0.0, 1.0)) = 0.55
        [HDR] _EmissionColor ("Emission Color", Color) = (0.0, 0.0, 0.0, 1.0)
        [HDR] _RimColor ("Fresnel Rim Color", Color) = (0.033, 0.830, 1.000, 1.0)
        _RimPower ("Fresnel Rim Power", Range(0.5, 8.0)) = 2.8
        _RimIntensity ("Fresnel Rim Intensity", Range(0.0, 3.0)) = 0.45
        _WrapDiffuse ("Diffuse Wrap (360 Softness)", Range(0.0, 0.5)) = 0.22
        _RimFillBias ("Under-Side Fresnel Floor", Range(0.0, 0.35)) = 0.04
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        LOD 200

        // Single-Pass ForwardBase khusus OpenGLES3 (tanpa ForwardAdd & tanpa ShadowCaster -> hemat draw call 60 FPS)
        Pass
        {
            Name "FORWARD_BASE"
            Tags { "LightMode" = "ForwardBase" }

            Cull Back
            ZWrite On
            Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            half4 _Color;
            half _Metallic;
            half _Glossiness;
            half4 _EmissionColor;
            half4 _RimColor;
            half _RimPower;
            half _RimIntensity;
            half _WrapDiffuse;
            half _RimFillBias;

            // Global 3-Point + Hemisphere Lighting Rig (diperbarui 1x/frame lewat Shader.SetGlobalVector, Zero-GC, Linear)
            float4 _GW_KeyLightDir;
            half4 _GW_KeyLightColor;
            float4 _GW_FillLightDir;
            half4 _GW_FillLightColor;
            float4 _GW_RimLightDir;
            half4 _GW_RimLightColor;
            float4 _GW_HemiUpDir;
            half4 _GW_HemiSkyColor;
            half4 _GW_HemiGroundColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                half3 worldNormal : TEXCOORD2;
            };

            inline half3 SafeNormalizeHalf3(half3 v, half3 fallbackDir)
            {
                half lenSq = dot(v, v);
                return lenSq > 1e-6h ? (v * rsqrt(lenSq)) : fallbackDir;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = (half3)UnityObjectToWorldNormal(v.normal);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half3 N = SafeNormalizeHalf3(i.worldNormal, half3(0.0h, 1.0h, 0.0h));
                half3 V = SafeNormalizeHalf3((half3)(_WorldSpaceCameraPos.xyz - i.worldPos), half3(0.0h, 0.0h, -1.0h));
                half NdotV = saturate(dot(N, V));

                half3 albedo = max(tex2D(_MainTex, i.uv).rgb * _Color.rgb, half3(0.0h, 0.0h, 0.0h));
                half metallic = saturate(_Metallic);
                half smoothness = saturate(_Glossiness);

                // Konservasi energi PBR-lite (retensi 15% diffuse bounce agar logam tetap kaya tanpa Reflection Probe)
                half3 diffuseColor = albedo * (1.0h - (metallic * 0.85h));
                half3 specColor = lerp(half3(0.04h, 0.04h, 0.04h), albedo, metallic);
                half shininess = exp2(lerp(3.0h, 9.0h, smoothness));
                half specNorm = min((shininess + 8.0h) * 0.0397887h, 6.0h);
                half specStrength = lerp(0.28h, 1.0h, smoothness);
                half wrap = clamp(_WrapDiffuse, 0.0h, 0.5h);

                // Resolve 3-Point + Hemisphere Lighting (aktif saat Mode Udara rig w >= 0.5; fallback aman di Preview/Scene)
                half3 keyDir;
                half3 keyCol;
                half3 fillDir;
                half3 fillCol;
                half3 rimDir;
                half3 rimLightCol;
                half3 hemiUp;
                half3 hemiSky;
                half3 hemiGround;

                if (_GW_KeyLightDir.w >= 0.5)
                {
                    keyDir = SafeNormalizeHalf3((half3)_GW_KeyLightDir.xyz, half3(-0.45h, 0.72h, -0.52h));
                    keyCol = max(_GW_KeyLightColor.rgb, half3(0.0h, 0.0h, 0.0h));
                    fillDir = SafeNormalizeHalf3((half3)_GW_FillLightDir.xyz, half3(0.74h, -0.34h, -0.58h));
                    fillCol = max(_GW_FillLightColor.rgb, half3(0.0h, 0.0h, 0.0h));
                    rimDir = SafeNormalizeHalf3((half3)_GW_RimLightDir.xyz, half3(0.38h, 0.54h, 0.75h));
                    rimLightCol = max(_GW_RimLightColor.rgb, half3(0.0h, 0.0h, 0.0h));
                    hemiUp = SafeNormalizeHalf3((half3)_GW_HemiUpDir.xyz, half3(0.0h, 1.0h, 0.0h));
                    hemiSky = max(_GW_HemiSkyColor.rgb, half3(0.0h, 0.0h, 0.0h));
                    hemiGround = max(_GW_HemiGroundColor.rgb, half3(0.0h, 0.0h, 0.0h));
                }
                else
                {
                    keyDir = SafeNormalizeHalf3((half3)_WorldSpaceLightPos0.xyz, half3(-0.45h, 0.72h, -0.52h));
                    keyCol = max(_LightColor0.rgb, half3(0.0h, 0.0h, 0.0h));
                    if (dot(keyCol, keyCol) < 1e-4h)
                    {
                        keyCol = half3(0.95h, 0.93h, 0.88h);
                    }
                    fillDir = SafeNormalizeHalf3(half3(-keyDir.x, -0.35h, -keyDir.z * 0.5h), half3(0.65h, -0.35h, -0.50h));
                    fillCol = half3(0.18h, 0.28h, 0.42h);
                    rimDir = SafeNormalizeHalf3(half3(-keyDir.x * 0.6h, 0.55h, -keyDir.z), half3(0.38h, 0.54h, 0.75h));
                    rimLightCol = half3(0.22h, 0.48h, 0.64h);
                    hemiUp = half3(0.0h, 1.0h, 0.0h);
                    hemiSky = max(UNITY_LIGHTMODEL_AMBIENT.rgb, half3(0.16h, 0.21h, 0.28h));
                    hemiGround = hemiSky * 0.58h;
                }

                // 1. Key Light (wrapped diffuse + normalized Blinn-Phong specular, NaN-guarded pow)
                half ndlKeyRaw = dot(N, keyDir);
                half ndlKey = saturate((ndlKeyRaw + wrap) / (1.0h + wrap));
                half3 HKey = SafeNormalizeHalf3(keyDir + V, N);
                half ndhKey = saturate(dot(N, HKey));
                half specKey = pow(max(ndhKey, 1e-4h), shininess) * saturate(ndlKeyRaw * 4.0h) * specNorm * specStrength;
                half3 lit = ((diffuseColor * ndlKey) + (specColor * specKey)) * keyCol;

                // 2. Fill Light (sisi berlawanan & bawah untuk pahatan 3D saat orbit 360 derajat, NaN-guarded pow)
                half ndlFillRaw = dot(N, fillDir);
                half ndlFill = saturate((ndlFillRaw + 0.35h) / 1.35h);
                half3 HFill = SafeNormalizeHalf3(fillDir + V, N);
                half ndhFill = saturate(dot(N, HFill));
                half fillExp = max(shininess * 0.35h, 6.0h);
                half specFill = pow(max(ndhFill, 1e-4h), fillExp) * saturate((ndlFillRaw + 0.25h) * 2.5h) * 0.40h * specStrength;
                lit += ((diffuseColor * ndlFill) + (specColor * specFill)) * fillCol;

                // 3. Rim / Back Light (pemisah siluet logam dari latar kamera, NaN-guarded pow)
                half ndlRimRaw = dot(N, rimDir);
                half ndlRim = saturate((ndlRimRaw * 0.70h) + 0.30h);
                half3 HRim = SafeNormalizeHalf3(rimDir + V, N);
                half ndhRim = saturate(dot(N, HRim));
                half rimExp = max(shininess * 0.50h, 10.0h);
                half specRim = pow(max(ndhRim, 1e-4h), rimExp) * saturate((ndlRimRaw + 0.20h) * 3.0h) * 0.65h * specStrength;
                lit += ((diffuseColor * (ndlRim * 0.45h)) + (specColor * specRim)) * rimLightCol;

                // 4. Hemisphere Sky/Ground Ambient + Fake Metallic Studio Reflection (tanpa Reflection Probe)
                half hemiT = saturate((dot(N, hemiUp) * 0.5h) + 0.5h);
                half3 hemiAmbient = lerp(hemiGround, hemiSky, hemiT);
                half3 R = reflect(-V, N);
                half reflT = saturate((dot(R, hemiUp) * 0.5h) + 0.5h);
                half3 envRefl = lerp(hemiGround * 0.85h, hemiSky * 1.45h, reflT) * (smoothness * smoothness);
                lit += (diffuseColor * hemiAmbient) + (specColor * envRefl * (0.35h + (0.65h * metallic)));

                // 5. Fresnel Rim Glow Reaktif (MaterialPropertyBlock Zero-GC, NaN-guarded pow OpenGLES3)
                half rimPower = max(_RimPower, 0.5h);
                half rimIntensity = max(_RimIntensity, 0.0h);
                half fresnel = pow(max(1.0h - NdotV, 1e-4h), rimPower);
                half3 rimGlow = max(_RimColor.rgb, half3(0.0h, 0.0h, 0.0h)) * ((fresnel + max(_RimFillBias, 0.0h)) * rimIntensity);

                half3 finalCol = lit + max(_EmissionColor.rgb, half3(0.0h, 0.0h, 0.0h)) + rimGlow;
                return half4(finalCol, 1.0h);
            }
            ENDCG
        }
    }

    Fallback Off
}
