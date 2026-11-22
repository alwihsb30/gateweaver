using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Pengendali Custom Shader, 3-Point + Hemisphere Lighting Rig 1-Pass, &amp; Fresnel Rim Glow khusus Mode Udara
/// (GateWeaver AR v8.0 Tahap 6 — Built-in Forward, Linear Color Space, OpenGLES3, 60 FPS Zero-GC):
/// - Memuat shader ARBackdropDimming &amp; HologramFresnelHardware melalui manifest Assets/Resources/Shaders/ (anti-stripping APK).
/// - Mendorong 3-Point Lighting (Key + Fill + Rim) &amp; Hemisphere Ambient secara analitik di 1 pass ForwardBase
///   melalui global vector (0 lampu Unity baru, 0 pass ForwardAdd, 0 pass shadow map di Mode Udara, aman orbit 360°).
/// - Menyimpan &amp; memulihkan 100% lampu scene, RenderSettings, dan shader bawaan GateNode saat kembali ke Mode Kartu (v5).
/// </summary>
public sealed class AirWeavingLightingAndShaderRig : MonoBehaviour
{
    public const string BackdropShaderName = "GateWeaver/ARBackdropDimming";
    public const string HardwareShaderName = "GateWeaver/HologramFresnelHardware";

    private const string BackdropRefMaterialResourcePath = "Shaders/ARBackdropDimming_Ref";
    private const string HardwareRefMaterialResourcePath = "Shaders/HologramFresnelHardware_Ref";

    // Global 3-Point + Hemisphere Lighting Uniform IDs (HologramFresnelHardware.shader)
    private static readonly int GwKeyLightDirPropId = Shader.PropertyToID("_GW_KeyLightDir");
    private static readonly int GwKeyLightColorPropId = Shader.PropertyToID("_GW_KeyLightColor");
    private static readonly int GwFillLightDirPropId = Shader.PropertyToID("_GW_FillLightDir");
    private static readonly int GwFillLightColorPropId = Shader.PropertyToID("_GW_FillLightColor");
    private static readonly int GwRimLightDirPropId = Shader.PropertyToID("_GW_RimLightDir");
    private static readonly int GwRimLightColorPropId = Shader.PropertyToID("_GW_RimLightColor");
    private static readonly int GwHemiUpDirPropId = Shader.PropertyToID("_GW_HemiUpDir");
    private static readonly int GwHemiSkyColorPropId = Shader.PropertyToID("_GW_HemiSkyColor");
    private static readonly int GwHemiGroundColorPropId = Shader.PropertyToID("_GW_HemiGroundColor");

    // ARBackdropDimming Uniform IDs
    private static readonly int DimBrightnessPropId = Shader.PropertyToID("_DimBrightness");
    private static readonly int DesaturationPropId = Shader.PropertyToID("_Desaturation");
    private static readonly int BackdropTintPropId = Shader.PropertyToID("_BackdropTint");
    private static readonly int VignetteColorPropId = Shader.PropertyToID("_VignetteColor");
    private static readonly int VignetteRadiusPropId = Shader.PropertyToID("_VignetteRadius");
    private static readonly int VignetteSoftnessPropId = Shader.PropertyToID("_VignetteSoftness");
    private static readonly int VignetteStrengthPropId = Shader.PropertyToID("_VignetteStrength");
    private static readonly int ForceLinearizePropId = Shader.PropertyToID("_ForceLinearize");

    // Parameter Default ARBackdropDimming (kontras tajam untuk grid 17x13 & kabel 3D tanpa menggelapkan tangan)
    private const float DefaultBackdropDimBrightness = 0.70f;
    private const float DefaultBackdropDesaturation = 0.18f;
    private const float DefaultVignetteRadius = 0.58f;
    private const float DefaultVignetteSoftness = 0.65f;
    private const float DefaultVignetteStrength = 0.40f;

    // Warna 3-Point + Hemisphere Lighting dalam Linear Space eksplisit (dikirim lewat Shader.SetGlobalVector -> 0 konversi ganda)
    private static readonly Vector4 KeyLightColorLinear = ToLinearLightVector(1.00f, 0.97f, 0.92f, 1.05f);
    private static readonly Vector4 FillLightColorLinear = ToLinearLightVector(0.58f, 0.78f, 0.98f, 0.44f);
    private static readonly Vector4 RimLightColorLinear = ToLinearLightVector(0.46f, 0.90f, 1.00f, 0.60f);
    private static readonly Vector4 HemiSkyColorLinear = ToLinearLightVector(0.56f, 0.66f, 0.78f, 0.52f);
    private static readonly Vector4 HemiGroundColorLinear = ToLinearLightVector(0.38f, 0.46f, 0.58f, 0.48f);

    // Palet Backdrop dalam Linear Space eksplisit
    private static readonly Color BackdropTintLinear = SrgbToLinearColor(0.94f, 0.97f, 1.00f);
    private static readonly Color VignetteNavyLinear = SrgbToLinearColor(0.015f, 0.035f, 0.070f);

    // Palet Fresnel Rim Glow dalam Linear Space eksplisit untuk MaterialPropertyBlock.SetColor (tajam, tidak pucat!)
    private static readonly Color RimHighEmeraldLinear = SrgbToLinearColor(0.14f, 1.00f, 0.78f);
    private static readonly Color RimHighInputACyanLinear = SrgbToLinearColor(0.18f, 0.94f, 1.00f);
    private static readonly Color RimHighInputBGoldLinear = SrgbToLinearColor(1.00f, 0.88f, 0.24f);
    private static readonly Color RimLowCopperLinear = SrgbToLinearColor(0.86f, 0.28f, 0.12f);
    private static readonly Color RimLowInputALinear = SrgbToLinearColor(0.28f, 0.62f, 0.74f);
    private static readonly Color RimLowInputBLinear = SrgbToLinearColor(0.76f, 0.54f, 0.16f);
    private static readonly Color RimOutputHighLinear = SrgbToLinearColor(0.18f, 1.00f, 0.82f);
    private static readonly Color RimOutputLowLinear = SrgbToLinearColor(0.74f, 0.26f, 0.14f);
    private static readonly Color RimParkedIceLinear = SrgbToLinearColor(0.24f, 0.80f, 0.98f);
    private static readonly Color RimHoverCyanLinear = SrgbToLinearColor(0.34f, 0.98f, 1.00f);
    private static readonly Color RimHoverGoldLinear = SrgbToLinearColor(1.00f, 0.88f, 0.28f);
    private static readonly Color RimPortFocusMintLinear = SrgbToLinearColor(0.30f, 1.00f, 0.84f);
    private static readonly Color RimGrabGoldLinear = SrgbToLinearColor(1.00f, 0.86f, 0.22f);
    private static readonly Color RimDeleteRedLinear = SrgbToLinearColor(1.00f, 0.22f, 0.18f);

    private static Shader cachedBackdropShader;
    private static Shader cachedHardwareShader;

    private readonly List<GateNode> trackedNodes = new List<GateNode>(12);
    private bool isRigActive = false;

    // Snapshot pencahayaan scene Mode Kartu v5 (dipulihkan 100% saat keluar Mode Udara / OnDisable / OnDestroy)
    private bool hasSavedSceneLighting = false;
    private AmbientMode savedAmbientMode = AmbientMode.Trilight;
    private Color savedAmbientSkyColor = Color.gray;
    private Color savedAmbientEquatorColor = Color.gray;
    private Color savedAmbientGroundColor = Color.gray;

    private Light primaryDirectionalLight;
    private Quaternion savedPrimaryLightRotation = Quaternion.identity;
    private Color savedPrimaryLightColor = Color.white;
    private float savedPrimaryLightIntensity = 1f;
    private LightShadows savedPrimaryLightShadows = LightShadows.Soft;
    private bool savedPrimaryLightEnabled = true;

    private Light arRimFillLight;
    private bool savedRimFillLightEnabled = true;

    public bool IsRigActive => isRigActive;

    /// <summary>
    /// Konversi eksak sRGB -> Linear (IEC 61966-2-1, pure managed math aman untuk static readonly &amp; Zero-GC).
    /// </summary>
    public static Color SrgbToLinearColor(float r, float g, float b, float a = 1f)
    {
        return new Color(
            SrgbChannelToLinear(r),
            SrgbChannelToLinear(g),
            SrgbChannelToLinear(b),
            a);
    }

    private static float SrgbChannelToLinear(float c)
    {
        if (c <= 0f)
        {
            return 0f;
        }
        if (c <= 0.04045f)
        {
            return c / 12.92f;
        }
        return (float)Math.Pow((c + 0.055d) / 1.055d, 2.4d);
    }

    private static Vector4 ToLinearLightVector(float srgbR, float srgbG, float srgbB, float intensity)
    {
        return new Vector4(
            SrgbChannelToLinear(srgbR) * intensity,
            SrgbChannelToLinear(srgbG) * intensity,
            SrgbChannelToLinear(srgbB) * intensity,
            1f);
    }

    /// <summary>
    /// Memuat shader ARBackdropDimming dari manifest Assets/Resources/Shaders/ (anti-stripping APK),
    /// dengan fallback Shader.Find. Asset manifest di Resources TIDAK pernah di-Destroy.
    /// </summary>
    public static Shader ResolveBackdropDimmingShader()
    {
        if (cachedBackdropShader != null && cachedBackdropShader.isSupported)
        {
            return cachedBackdropShader;
        }

        Material refMat = Resources.Load<Material>(BackdropRefMaterialResourcePath);
        if (refMat != null && refMat.shader != null && refMat.shader.isSupported)
        {
            cachedBackdropShader = refMat.shader;
            return cachedBackdropShader;
        }

        Shader found = Shader.Find(BackdropShaderName);
        if (found != null && found.isSupported)
        {
            cachedBackdropShader = found;
            return cachedBackdropShader;
        }

        return null;
    }

    /// <summary>
    /// Memuat shader HologramFresnelHardware dari manifest Assets/Resources/Shaders/ (anti-stripping APK),
    /// dengan fallback Shader.Find. Asset manifest di Resources TIDAK pernah di-Destroy.
    /// </summary>
    public static Shader ResolveHardwareFresnelShader()
    {
        if (cachedHardwareShader != null && cachedHardwareShader.isSupported)
        {
            return cachedHardwareShader;
        }

        Material refMat = Resources.Load<Material>(HardwareRefMaterialResourcePath);
        if (refMat != null && refMat.shader != null && refMat.shader.isSupported)
        {
            cachedHardwareShader = refMat.shader;
            return cachedHardwareShader;
        }

        Shader found = Shader.Find(HardwareShaderName);
        if (found != null && found.isSupported)
        {
            cachedHardwareShader = found;
            return cachedHardwareShader;
        }

        return null;
    }

    /// <summary>
    /// Membuat instance Material dinamis untuk VideoQuad Mode Udara memakai ARBackdropDimming
    /// (caller bertanggung jawab mendaftarkan Material ini ke ownedMaterials &amp; Destroy di OnDestroy).
    /// </summary>
    public static Material CreateBackdropDimmingMaterial(out bool usesCustomBackdropShader)
    {
        Shader backdropShader = ResolveBackdropDimmingShader();
        if (backdropShader != null)
        {
            Material mat = new Material(backdropShader);
            mat.renderQueue = 1500;
            if (mat.HasProperty(DimBrightnessPropId)) mat.SetFloat(DimBrightnessPropId, DefaultBackdropDimBrightness);
            if (mat.HasProperty(DesaturationPropId)) mat.SetFloat(DesaturationPropId, DefaultBackdropDesaturation);
            if (mat.HasProperty(BackdropTintPropId)) mat.SetColor(BackdropTintPropId, BackdropTintLinear);
            if (mat.HasProperty(VignetteColorPropId)) mat.SetColor(VignetteColorPropId, VignetteNavyLinear);
            if (mat.HasProperty(VignetteRadiusPropId)) mat.SetFloat(VignetteRadiusPropId, DefaultVignetteRadius);
            if (mat.HasProperty(VignetteSoftnessPropId)) mat.SetFloat(VignetteSoftnessPropId, DefaultVignetteSoftness);
            if (mat.HasProperty(VignetteStrengthPropId)) mat.SetFloat(VignetteStrengthPropId, DefaultVignetteStrength);
            if (mat.HasProperty(ForceLinearizePropId)) mat.SetFloat(ForceLinearizePropId, 0f);
            usesCustomBackdropShader = true;
            return mat;
        }

        usesCustomBackdropShader = false;
        return null;
    }

    /// <summary>
    /// Mengaktifkan 3-Point Lighting Rig 1-pass &amp; beralih shader opaque 8 GateNode ke HologramFresnelHardware
    /// saat memasuki Mode Udara. Menyimpan snapshot pencahayaan Mode Kartu v5 agar pemulihan 100% identik.
    /// </summary>
    public void ActivateForAirWeaving(Camera mainCam, Transform stageRoot, List<GateNode> nodes)
    {
        SnapshotAndConfigureSceneLightsForAirMode();

        trackedNodes.Clear();
        Shader hardwareShader = ResolveHardwareFresnelShader();
        if (nodes != null)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                GateNode node = nodes[i];
                if (node == null)
                {
                    continue;
                }

                trackedNodes.Add(node);
                if (hardwareShader != null)
                {
                    node.ApplyExternalHardwareShader(hardwareShader);
                }
            }
        }

        isRigActive = true;
        TickLightingRig(mainCam, stageRoot);
    }

    /// <summary>
    /// Memperbarui vektor arah &amp; warna 3-Point + Hemisphere Lighting Rig (terikat kamera + bias halus panggung)
    /// sekali per frame via Shader.SetGlobalVector (100% Zero-GC, 1-pass ForwardBase, aman orbit 360°).
    /// </summary>
    public void TickLightingRig(Camera mainCam, Transform stageRoot)
    {
        if (!isRigActive || mainCam == null)
        {
            return;
        }

        Transform camT = mainCam.transform;
        Vector3 camR = camT.right;
        Vector3 camU = camT.up;
        Vector3 camF = camT.forward;

        // Bias orientasi panggung halus (~14%) hanya bila meja masih menghadap atas relatif terhadap kamera;
        // saat panggung dibalik 180°, bias meluruh halus ke 0 sehingga sisi bawah tetap diterangi tajam.
        Vector3 stageBias = Vector3.zero;
        if (stageRoot != null)
        {
            Vector3 stageUp = stageRoot.up;
            float uprightness = Mathf.Clamp01(Vector3.Dot(stageUp, camU));
            stageBias = (stageUp - camU) * (0.14f * uprightness);
        }

        Vector3 keyDir = ((-0.52f * camR) + (0.68f * camU) - (0.52f * camF) + stageBias).normalized;
        Vector3 fillDir = ((0.74f * camR) - (0.34f * camU) - (0.58f * camF)).normalized;
        Vector3 rimDir = ((0.38f * camR) + (0.54f * camU) + (0.75f * camF)).normalized;
        Vector3 hemiUp = (camU + (stageBias * 0.65f)).normalized;

        Shader.SetGlobalVector(GwKeyLightDirPropId, new Vector4(keyDir.x, keyDir.y, keyDir.z, 1.0f));
        Shader.SetGlobalVector(GwKeyLightColorPropId, KeyLightColorLinear);
        Shader.SetGlobalVector(GwFillLightDirPropId, new Vector4(fillDir.x, fillDir.y, fillDir.z, 1.0f));
        Shader.SetGlobalVector(GwFillLightColorPropId, FillLightColorLinear);
        Shader.SetGlobalVector(GwRimLightDirPropId, new Vector4(rimDir.x, rimDir.y, rimDir.z, 1.0f));
        Shader.SetGlobalVector(GwRimLightColorPropId, RimLightColorLinear);
        Shader.SetGlobalVector(GwHemiUpDirPropId, new Vector4(hemiUp.x, hemiUp.y, hemiUp.z, 1.0f));
        Shader.SetGlobalVector(GwHemiSkyColorPropId, HemiSkyColorLinear);
        Shader.SetGlobalVector(GwHemiGroundColorPropId, HemiGroundColorLinear);

        // Selaraskan Directional Light utama ke arah Key Light (tanpa bayangan) untuk kesatuan objek fallback bila ada
        if (primaryDirectionalLight != null && primaryDirectionalLight.isActiveAndEnabled)
        {
            Vector3 lookDir = -keyDir;
            if (lookDir.sqrMagnitude > 0.0001f)
            {
                Vector3 upHint = Mathf.Abs(Vector3.Dot(lookDir, camU)) < 0.96f ? camU : camR;
                primaryDirectionalLight.transform.rotation = Quaternion.LookRotation(lookDir, upHint);
            }
        }
    }

    /// <summary>
    /// Memperbarui Fresnel Rim Glow per node berdasarkan status interaksi (NodeHoloState), status logika (0/1),
    /// serta progres tahan-kepal Output (Linear eksplisit, Zero-GC lewat MaterialPropertyBlock).
    /// </summary>
    public void UpdateNodeFresnelState(
        GateNode node,
        bool isInputA,
        bool isInputB,
        bool isOutput,
        AirWeavingHologramVisual.NodeHoloState state,
        float outputHoldProgress = 0f)
    {
        if (!isRigActive || node == null)
        {
            return;
        }

        float timeNow = Time.unscaledTime;
        Color rimColor;
        float rimPower;
        float rimIntensity;

        if (isOutput && outputHoldProgress > 0.02f)
        {
            float holdPulse = 0.5f + (0.5f * Mathf.Sin(timeNow * 10.5f));
            rimColor = Color.Lerp(RimOutputHighLinear, RimGrabGoldLinear, Mathf.Clamp01(outputHoldProgress));
            rimPower = Mathf.Lerp(2.3f, 1.85f, outputHoldProgress);
            rimIntensity = Mathf.Lerp(1.00f, 1.55f, outputHoldProgress) + (0.14f * holdPulse);
        }
        else if (state == AirWeavingHologramVisual.NodeHoloState.ReturningToRack)
        {
            float warnPulse = 0.5f + (0.5f * Mathf.Sin(timeNow * 11.0f));
            rimColor = RimDeleteRedLinear;
            rimPower = 1.90f;
            rimIntensity = 1.38f + (0.28f * warnPulse);
        }
        else if (state == AirWeavingHologramVisual.NodeHoloState.Grabbed)
        {
            float grabPulse = 0.5f + (0.5f * Mathf.Sin(timeNow * 6.5f));
            rimColor = RimGrabGoldLinear;
            rimPower = 2.10f;
            rimIntensity = 1.24f + (0.16f * grabPulse);
        }
        else if (state == AirWeavingHologramVisual.NodeHoloState.BodyHovered)
        {
            float hoverPulse = 0.5f + (0.5f * Mathf.Sin(timeNow * 5.8f));
            rimColor = isInputB ? RimHoverGoldLinear : RimHoverCyanLinear;
            rimPower = 2.20f;
            rimIntensity = 1.12f + (0.14f * hoverPulse);
        }
        else if (state == AirWeavingHologramVisual.NodeHoloState.PortFocused)
        {
            rimColor = RimPortFocusMintLinear;
            rimPower = 2.40f;
            rimIntensity = 0.92f;
        }
        else if (state == AirWeavingHologramVisual.NodeHoloState.Parked)
        {
            rimColor = RimParkedIceLinear;
            rimPower = 2.65f;
            rimIntensity = 0.56f;
        }
        else
        {
            // Idle aktif di meja kerja: pendar Fresnel mencerminkan status logika biner LOW (0) vs HIGH (1)
            bool isHigh = node.currentOutputValue;
            if (isOutput)
            {
                rimColor = isHigh ? RimOutputHighLinear : RimOutputLowLinear;
                rimPower = isHigh ? 2.35f : 2.95f;
                rimIntensity = isHigh ? 0.96f : 0.44f;
            }
            else if (isInputA)
            {
                rimColor = isHigh ? RimHighInputACyanLinear : RimLowInputALinear;
                rimPower = isHigh ? 2.35f : 2.90f;
                rimIntensity = isHigh ? 0.92f : 0.44f;
            }
            else if (isInputB)
            {
                rimColor = isHigh ? RimHighInputBGoldLinear : RimLowInputBLinear;
                rimPower = isHigh ? 2.35f : 2.90f;
                rimIntensity = isHigh ? 0.92f : 0.44f;
            }
            else
            {
                rimColor = isHigh ? RimHighEmeraldLinear : RimLowCopperLinear;
                rimPower = isHigh ? 2.40f : 3.00f;
                rimIntensity = isHigh ? 0.88f : 0.42f;
            }
        }

        node.SetExternalRimHighlight(rimColor, rimPower, rimIntensity);
    }

    /// <summary>
    /// Menonaktifkan rig Mode Udara dan memulihkan 100% lampu scene, RenderSettings, serta shader bawaan Mode Kartu v5.
    /// </summary>
    public void DeactivateAndRestoreForCardMode()
    {
        for (int i = 0; i < trackedNodes.Count; i++)
        {
            GateNode node = trackedNodes[i];
            if (node != null)
            {
                node.RestoreDefaultHardwareShaders();
            }
        }
        trackedNodes.Clear();

        Shader.SetGlobalVector(GwKeyLightDirPropId, Vector4.zero);
        RestoreSceneLightsToCardMode();
        isRigActive = false;
    }

    private void SnapshotAndConfigureSceneLightsForAirMode()
    {
        if (!hasSavedSceneLighting)
        {
            savedAmbientMode = RenderSettings.ambientMode;
            savedAmbientSkyColor = RenderSettings.ambientSkyColor;
            savedAmbientEquatorColor = RenderSettings.ambientEquatorColor;
            savedAmbientGroundColor = RenderSettings.ambientGroundColor;

            ResolveSceneDirectionalLights();

            if (primaryDirectionalLight != null)
            {
                savedPrimaryLightRotation = primaryDirectionalLight.transform.rotation;
                savedPrimaryLightColor = primaryDirectionalLight.color;
                savedPrimaryLightIntensity = primaryDirectionalLight.intensity;
                savedPrimaryLightShadows = primaryDirectionalLight.shadows;
                savedPrimaryLightEnabled = primaryDirectionalLight.enabled;
            }

            if (arRimFillLight != null)
            {
                savedRimFillLightEnabled = arRimFillLight.enabled;
            }

            hasSavedSceneLighting = true;
        }

        // Matikan Directional Light ke-2 (AR_RimFillLight) & shadow map pass selama Mode Udara aktif:
        // 3-Point Lighting ditangani 100% analitik dalam 1 pass ForwardBase tanpa memicu ForwardAdd!
        if (arRimFillLight != null && arRimFillLight.enabled)
        {
            arRimFillLight.enabled = false;
        }

        if (primaryDirectionalLight != null)
        {
            primaryDirectionalLight.shadows = LightShadows.None;
            primaryDirectionalLight.color = new Color(1.00f, 0.97f, 0.92f, 1f);
            primaryDirectionalLight.intensity = 1.05f;
        }
    }

    private void RestoreSceneLightsToCardMode()
    {
        if (!hasSavedSceneLighting)
        {
            return;
        }

        RenderSettings.ambientMode = savedAmbientMode;
        RenderSettings.ambientSkyColor = savedAmbientSkyColor;
        RenderSettings.ambientEquatorColor = savedAmbientEquatorColor;
        RenderSettings.ambientGroundColor = savedAmbientGroundColor;

        if (primaryDirectionalLight != null)
        {
            primaryDirectionalLight.transform.rotation = savedPrimaryLightRotation;
            primaryDirectionalLight.color = savedPrimaryLightColor;
            primaryDirectionalLight.intensity = savedPrimaryLightIntensity;
            primaryDirectionalLight.shadows = savedPrimaryLightShadows;
            primaryDirectionalLight.enabled = savedPrimaryLightEnabled;
        }

        if (arRimFillLight != null)
        {
            arRimFillLight.enabled = savedRimFillLightEnabled;
        }

        hasSavedSceneLighting = false;
    }

    private void ResolveSceneDirectionalLights()
    {
        primaryDirectionalLight = null;
        arRimFillLight = null;

#if UNITY_6000_5_OR_NEWER
        Light[] lights = FindObjectsByType<Light>();
#else
        Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
#endif
        if (lights == null)
        {
            return;
        }

        for (int i = 0; i < lights.Length; i++)
        {
            Light l = lights[i];
            if (l == null || l.type != LightType.Directional)
            {
                continue;
            }

            if (string.Equals(l.gameObject.name, "AR_RimFillLight", StringComparison.Ordinal))
            {
                arRimFillLight = l;
            }
            else if (primaryDirectionalLight == null)
            {
                primaryDirectionalLight = l;
            }
        }
    }

    private void OnDisable()
    {
        if (isRigActive || hasSavedSceneLighting)
        {
            DeactivateAndRestoreForCardMode();
        }
    }

    private void OnDestroy()
    {
        DeactivateAndRestoreForCardMode();
        primaryDirectionalLight = null;
        arRimFillLight = null;
    }
}
