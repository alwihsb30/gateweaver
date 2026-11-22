using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Pengendali Prop Sci-Fi Melayang 360° (.obj/.fbx native) khusus Mode Udara
/// (GateWeaver AR v8.0 Tahap 7 — Built-in Forward, Linear Color Space, OpenGLES3, 60 FPS Zero-GC):
/// - Menggantikan seluruh diorama darat v5 di Mode Udara untuk Input A/B (Floating Signal Emitter Pod +
///   cincin identitas permanen Sian/Emas) dan 5 varian Output (Beacon Drone, Relay Spire, Orbital Holo-Vault,
///   Containment Reactor Core, Twin-Core Resonator), serta memasang Underside Hover-Cradle tipis di bawah
///   5 gerbang Open Cutaway (tanpa mengubah 1 baris pun di GateMechanismVisual.cs).
/// - Kepemilikan material 100% terisolasi dari GateNode: semua bodi opaque memakai 1-pass HologramFresnelHardware,
///   sedangkan elemen transparan/kaca/beam/repulsor memakai Sprites/Default (renderQueue 2958..2965, ZWrite Off,
///   Cull Off) agar anti-strip dan ringan di OpenGLES3 Android.
/// - Proteksi dua arah SetActive(false): AR_AirFloatingPropRoot dimatikan total saat kembali ke Mode Kartu v5
///   (kebal DefaultObserverEventHandler Vuforia), dan bodi darat v5 dimatikan total saat masuk Mode Udara.
/// </summary>
public sealed class AirWeavingFloatingPropRig : MonoBehaviour
{
    public const string PropsResourcePrefix = "Models/V8_AirProps/";
    public const string InputBeaconPropName = "AirProp_InputBeacon";
    public const string GateHoverCradlePropName = "AirProp_GateHoverCradle";
    public const int OutputVariantCount = 5;

    public static readonly string[] OutputVariantPropNames =
    {
        "AirProp_OutputBeaconDrone",
        "AirProp_OutputRelaySpire",
        "AirProp_OutputHoloVault",
        "AirProp_OutputReactorCore",
        "AirProp_OutputTwinSyncCore"
    };

    // RenderQueue terkalibrasi: di depan ARBackdropDimming (1500) & Geometry (2000), di belakang HoloStage (2975) & Label (2998)
    private const int GlassRenderQueue = 2958;
    private const int RepulsorGlowRenderQueue = 2960;
    private const int SirenBeamRenderQueue = 2965;

    // Amplop normalisasi ketat agar nol benturan dengan 3D Port Sockets (±0.27f), OutputTruthPod, Halo (R=0.235f), & Label
    private const float InputMaxFootprintXZ = 0.44f;
    private const float InputMaxHeightY = 0.42f;
    private const float InputBaseSurfaceY = 0.015f;

    private const float OutputMaxFootprintXZ = 0.48f;
    private const float OutputMaxHeightY = 0.45f;
    private const float OutputBaseSurfaceY = 0.015f;

    // Hover Cradle menempel rapi tepat di bawah Chassis_SubDeck (GateMechanismVisual y = 0.040f)
    private const float CradleTargetWidthX = 0.45f;
    private const float CradleTargetDepthZ = 0.33f;
    private const float CradleTargetHeightY = 0.082f;
    private const float CradleTopAttachY = 0.040f;

    private const float InputLeverAngleDeg = 38.0f;
    private const float VaultShutterSlideTravel = 0.108f;

    private static readonly int ColorPropId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorPropId = Shader.PropertyToID("_EmissionColor");
    private static readonly int GlossinessPropId = Shader.PropertyToID("_Glossiness");
    private static readonly int MetallicPropId = Shader.PropertyToID("_Metallic");
    private static readonly int RimColorPropId = Shader.PropertyToID("_RimColor");
    private static readonly int RimPowerPropId = Shader.PropertyToID("_RimPower");
    private static readonly int RimIntensityPropId = Shader.PropertyToID("_RimIntensity");

    // Palet sRGB untuk Material.SetColor (Unity otomatis mengonversi ke Linear saat menulis ke Material)
    private static readonly Color HullMainFallbackColor = new Color(0.72f, 0.78f, 0.86f, 1.00f);
    private static readonly Color HullDarkGreyFallbackColor = new Color(0.22f, 0.26f, 0.34f, 1.00f);
    private static readonly Color HullDarkAccentFallbackColor = new Color(0.14f, 0.23f, 0.38f, 1.00f);

    private static readonly Color InputAIdentityCyanColor = new Color(0.16f, 0.90f, 1.00f, 1.00f);
    private static readonly Color InputBIdentityGoldColor = new Color(1.00f, 0.84f, 0.20f, 1.00f);

    private static readonly Color AccentHighColor = new Color(0.16f, 1.00f, 0.80f, 1.00f);
    private static readonly Color AccentHighEmission = new Color(0.14f, 0.94f, 0.74f, 1.00f) * 1.15f;
    private static readonly Color AccentLowColor = new Color(0.78f, 0.24f, 0.12f, 1.00f);
    private static readonly Color AccentLowEmission = new Color(0.56f, 0.15f, 0.07f, 1.00f) * 0.42f;
    private static readonly Color AccentParkedColor = new Color(0.26f, 0.82f, 0.96f, 1.00f);
    private static readonly Color AccentParkedEmission = new Color(0.16f, 0.56f, 0.72f, 1.00f) * 0.52f;

    private static readonly Color GlassTintHighColor = new Color(0.32f, 0.96f, 0.94f, 0.28f);
    private static readonly Color GlassTintLowColor = new Color(0.34f, 0.68f, 0.88f, 0.22f);

    private static readonly Color SirenBeamHighColor = new Color(0.20f, 1.00f, 0.84f, 0.38f);
    private static readonly Color RelayStepActiveColor = new Color(1.00f, 0.94f, 0.30f, 1.00f);
    private static readonly Color RelayStepActiveEmission = new Color(1.00f, 0.88f, 0.22f, 1.00f) * 1.25f;
    private static readonly Color RelayStepTrailColor = new Color(0.18f, 0.96f, 0.86f, 1.00f);
    private static readonly Color RelayStepTrailEmission = new Color(0.14f, 0.82f, 0.74f, 1.00f) * 0.72f;
    private static readonly Color RelayStepIdleColor = new Color(0.28f, 0.34f, 0.44f, 1.00f);
    private static readonly Color RelayStepIdleEmission = new Color(0.10f, 0.12f, 0.16f, 1.00f) * 0.28f;

    private static readonly Color RepulsorInputAColor = new Color(0.22f, 0.94f, 1.00f, 0.52f);
    private static readonly Color RepulsorInputBColor = new Color(1.00f, 0.86f, 0.24f, 0.52f);
    private static readonly Color RepulsorHighColor = new Color(0.18f, 1.00f, 0.82f, 0.56f);
    private static readonly Color RepulsorLowColor = new Color(0.34f, 0.76f, 0.94f, 0.34f);
    private static readonly Color RepulsorGrabColor = new Color(1.00f, 0.88f, 0.26f, 0.68f);
    private static readonly Color RepulsorDeleteColor = new Color(1.00f, 0.24f, 0.20f, 0.76f);

    private sealed class InputPropSlot
    {
        public GateNode node;
        public bool isInputB;
        public GameObject rootObj;
        public Transform bounceRoot;
        public Transform leverPivot;
        public Transform pulseHaloPivot;
        public Vector3 pulseHaloBasePos;
        public Vector3 pulseHaloBaseScale;
        public Transform repulsorTransform;
        public Material repulsorMat;
        public Material[] reactiveAccentMats;
        public Renderer[] rimRenderers;
        public float currentLeverAngle;
        public float phase;
        public int lastBinaryState = -1;
        public bool hasRimState;
        public Color lastRimColor;
        public float lastRimPower;
        public float lastRimIntensity;
    }

    private sealed class OutputVariantRig
    {
        public GameObject variantRootObj;
        public Material[] reactiveAccentMats;
        public Material[] glassMats;
        public Renderer[] rimRenderers;

        // Varian 0: Sentinel Beacon Drone
        public Transform droneRotorPivot;
        public Transform dronePulseRingPivot;
        public Vector3 dronePulseRingBaseScale;
        public GameObject droneSweepBeamsObj;

        // Varian 1: Dual-Way Luminance Relay Spire
        public Material[] relayStepMats;
        public Transform relayHaloLowerPivot;
        public Transform relayHaloUpperPivot;
        public Transform relayCorePivot;
        public float relayChasePhase;
        public int lastRelayChaseStep = -2;

        // Varian 2: Orbital Holo-Vault
        public Transform vaultShutterLeft;
        public Transform vaultShutterRight;
        public Vector3 vaultShutterLeftBasePos;
        public Vector3 vaultShutterRightBasePos;
        public float vaultShutterLeftOutwardSign = -1f;
        public float vaultShutterRightOutwardSign = 1f;
        public float vaultCurrentSlide;
        public Transform vaultWheelPivot;
        public Transform vaultInnerCorePivot;
        public Vector3 vaultInnerCoreBaseScale;

        // Varian 3: Containment Reactor Core
        public Transform reactorGyroPivotA;
        public Transform reactorGyroPivotB;
        public Transform reactorPlasmaPivot;
        public Vector3 reactorPlasmaBaseScale;

        // Varian 4: Twin-Core Resonator
        public Transform twinCrystalLeftPivot;
        public Transform twinCrystalRightPivot;
        public Vector3 twinCrystalLeftBaseScale;
        public Vector3 twinCrystalRightBaseScale;
        public Transform twinOrbitPivotA;
        public Transform twinOrbitPivotB;
        public float twinSyncSpinDeg;
    }

    private sealed class OutputPropSlot
    {
        public GateNode node;
        public GameObject rootObj;
        public Transform bounceRoot;
        public OutputVariantRig[] variants;
        public Transform repulsorTransform;
        public Material repulsorMat;
        public int activeVariantIndex = -1;
        public int lastBinaryState = -1;
        public bool hasRimState;
        public Color lastRimColor;
        public float lastRimPower;
        public float lastRimIntensity;
    }

    private sealed class GateCradleSlot
    {
        public GateNode node;
        public GameObject rootObj;
        public Transform thrusterCorePivot;
        public Transform repulsorTransform;
        public Material repulsorMat;
        public Material[] reactiveAccentMats;
        public Renderer[] rimRenderers;
        public int lastVisualStateKey = int.MinValue;
        public bool hasRimState;
        public Color lastRimColor;
        public float lastRimPower;
        public float lastRimIntensity;
    }

    private readonly InputPropSlot[] inputSlots = new InputPropSlot[2];
    private OutputPropSlot outputSlot;
    private readonly List<GateCradleSlot> gateCradleSlots = new List<GateCradleSlot>(5);

    private readonly List<Material> ownedMaterials = new List<Material>(96);
    private readonly List<GameObject> ownedRootObjects = new List<GameObject>(8);
    private readonly List<Renderer> tempRendererList = new List<Renderer>(16);
    private readonly List<Material> tempAccentList = new List<Material>(8);
    private readonly List<Material> tempGlassList = new List<Material>(4);

    private MaterialPropertyBlock sharedRimMpb;
    private Mesh sharedRepulsorRingMesh;
    private Mesh sharedFallbackTorusMesh;
    private Mesh sharedFallbackDiamondMesh;

    private Shader cachedHardwareShader;
    private Shader cachedSpritesShader;
    private bool isRigActive = false;
    private bool isInitialized = false;

    public bool IsRigActive => isRigActive;

    /// <summary>
    /// Mengaktifkan seluruh Prop Sci-Fi Melayang Mode Udara (Input A/B, 5 Varian Output, &amp; Gate Hover-Cradle),
    /// menyupresi diorama darat v5 lewat SetActive(false), &amp; mengisolasi penuh material dari GateNode.
    /// </summary>
    public void ActivateForAirWeaving(
        GateNode inputANode,
        GateNode inputBNode,
        GateNode outputNode,
        List<GateNode> logicGateNodes)
    {
        ResolveShaders();
        EnsureSharedProceduralMeshes();
        if (sharedRimMpb == null)
        {
            sharedRimMpb = new MaterialPropertyBlock();
        }

        if (!isInitialized)
        {
            BuildInputSlot(0, inputANode, isInputB: false);
            BuildInputSlot(1, inputBNode, isInputB: true);
            BuildOutputSlot(outputNode);
            BuildGateCradleSlots(logicGateNodes);
            isInitialized = true;
        }
        else
        {
            RebindMissingSlotsIfNeeded(inputANode, inputBNode, outputNode, logicGateNodes);
        }

        // Aktifkan AR_AirFloatingPropRoot dan supresi kontainer darat v5 pada Input A/B & Output (SetActive dua arah)
        for (int i = 0; i < inputSlots.Length; i++)
        {
            InputPropSlot slot = inputSlots[i];
            if (slot == null || slot.node == null || slot.rootObj == null)
            {
                continue;
            }

            if (!slot.rootObj.activeSelf)
            {
                slot.rootObj.SetActive(true);
            }
            slot.node.SetExternalBodyVisualSuppressed(true, slot.bounceRoot, slot.rootObj.transform);
            slot.lastBinaryState = -1;
            slot.hasRimState = false;
            ApplyInputBinaryState(slot, slot.node.currentOutputValue);
        }

        if (outputSlot != null && outputSlot.node != null && outputSlot.rootObj != null)
        {
            if (!outputSlot.rootObj.activeSelf)
            {
                outputSlot.rootObj.SetActive(true);
            }
            outputSlot.node.SetExternalBodyVisualSuppressed(true, outputSlot.bounceRoot, outputSlot.rootObj.transform);
            outputSlot.activeVariantIndex = -1;
            outputSlot.lastBinaryState = -1;
            outputSlot.hasRimState = false;
            SyncActiveOutputVariant(forceRefresh: true);
        }

        for (int i = 0; i < gateCradleSlots.Count; i++)
        {
            GateCradleSlot cradle = gateCradleSlots[i];
            if (cradle == null || cradle.node == null || cradle.rootObj == null)
            {
                continue;
            }

            if (!cradle.rootObj.activeSelf)
            {
                cradle.rootObj.SetActive(true);
            }
            // Open Cutaway GateMechanismVisual TETAP AKTIF; hanya daftarkan root agar material Hover-Cradle terisolasi!
            cradle.node.RegisterExternalFloatingPropRoot(cradle.rootObj.transform);
            cradle.lastVisualStateKey = int.MinValue;
            cradle.hasRimState = false;
            ApplyGateCradleVisualState(cradle, isGrabbed: false, isInDeleteZone: false);
        }

        isRigActive = true;
    }

    /// <summary>
    /// Menyinkronkan varian Output melayang segera saat pindah level / masuk Sandbox di Mode Udara. Zero-GC.
    /// </summary>
    public void SyncLevelVariantImmediate()
    {
        if (!isRigActive || outputSlot == null)
        {
            return;
        }

        SyncActiveOutputVariant(forceRefresh: true);
    }

    /// <summary>
    /// Memperbarui animasi kinetik, warna reaktif 0/1, dan pendar repulsor bawah seluruh prop melayang. 100% Zero-GC.
    /// </summary>
    public void TickProps(float dt, GateNode grabbedGateNode = null, bool isGrabbedGateInDeleteZone = false)
    {
        if (!isRigActive)
        {
            return;
        }

        float safeDt = Mathf.Clamp(dt, 0f, 0.08f);
        float timeNow = Time.unscaledTime;

        // 1. Input A & Input B Floating Signal Emitter Pods
        for (int i = 0; i < inputSlots.Length; i++)
        {
            InputPropSlot slot = inputSlots[i];
            if (slot == null || slot.node == null || slot.rootObj == null)
            {
                continue;
            }

            bool isHigh = slot.node.currentOutputValue;
            int bit = isHigh ? 1 : 0;
            if (bit != slot.lastBinaryState)
            {
                ApplyInputBinaryState(slot, isHigh);
            }

            slot.phase += safeDt * (isHigh ? 4.2f : 1.5f);

            if (slot.leverPivot != null)
            {
                float targetAngle = isHigh ? InputLeverAngleDeg : -InputLeverAngleDeg;
                float smooth = 1f - Mathf.Exp(-15.0f * safeDt);
                slot.currentLeverAngle = Mathf.Lerp(slot.currentLeverAngle, targetAngle, smooth);
                slot.leverPivot.localRotation = Quaternion.Euler(0f, 0f, -slot.currentLeverAngle);
            }

            if (slot.pulseHaloPivot != null)
            {
                float pulse = isHigh ? (1.0f + (0.09f * Mathf.Sin(slot.phase * 1.4f))) : 0.96f;
                float liftY = isHigh ? (0.012f * Mathf.Sin(slot.phase * 1.1f)) : 0f;
                slot.pulseHaloPivot.localPosition = slot.pulseHaloBasePos + new Vector3(0f, liftY, 0f);
                slot.pulseHaloPivot.localScale = slot.pulseHaloBaseScale * pulse;
                slot.pulseHaloPivot.Rotate(Vector3.up, (isHigh ? 95f : 22f) * safeDt, Space.Self);
            }

            if (slot.repulsorTransform != null)
            {
                float rPulse = 0.92f + (0.12f * Mathf.Sin((timeNow * 5.2f) + (i * 1.7f)));
                float highBoost = isHigh ? 1.12f : 0.92f;
                slot.repulsorTransform.localScale = Vector3.one * (0.145f * rPulse * highBoost);
            }
        }

        // 2. Output 5-Variant Floating Sci-Fi Props
        if (outputSlot != null && outputSlot.node != null && outputSlot.rootObj != null)
        {
            SyncActiveOutputVariant(forceRefresh: false);
            bool outHigh = outputSlot.node.currentOutputValue;
            int outBit = outHigh ? 1 : 0;
            if (outBit != outputSlot.lastBinaryState)
            {
                ApplyOutputBinaryState(outHigh);
            }

            AnimateActiveOutputVariant(outHigh, safeDt, timeNow);

            if (outputSlot.repulsorTransform != null)
            {
                float rPulse = 0.92f + (0.14f * Mathf.Sin(timeNow * (outHigh ? 7.0f : 3.2f)));
                outputSlot.repulsorTransform.localScale = Vector3.one * (0.175f * rPulse * (outHigh ? 1.16f : 0.94f));
            }
        }

        // 3. 5 Logic Gates Underside Hover-Cradles (Open Cutaway tetap dikelola GateMechanismVisual)
        for (int i = 0; i < gateCradleSlots.Count; i++)
        {
            GateCradleSlot cradle = gateCradleSlots[i];
            if (cradle == null || cradle.node == null || cradle.rootObj == null)
            {
                continue;
            }

            bool isGrabbed = cradle.node == grabbedGateNode;
            bool inDelete = isGrabbed && isGrabbedGateInDeleteZone;
            ApplyGateCradleVisualState(cradle, isGrabbed, inDelete);

            if (cradle.thrusterCorePivot != null)
            {
                float coreFreq = inDelete ? 11.5f : (isGrabbed ? 7.5f : 3.5f);
                float coreAmp = isGrabbed ? 0.09f : (cradle.node.isTracked && cradle.node.currentOutputValue ? 0.05f : 0.02f);
                float coreScale = 1.0f + (coreAmp * Mathf.Sin((timeNow * coreFreq) + (i * 1.1f)));
                cradle.thrusterCorePivot.localScale = Vector3.one * coreScale;
            }

            if (cradle.repulsorTransform != null)
            {
                float freq = inDelete ? 11.5f : (isGrabbed ? 7.8f : 4.2f);
                float baseRadius = isGrabbed ? 0.168f : (cradle.node.isTracked ? 0.132f : 0.105f);
                float rPulse = 0.92f + (0.12f * Mathf.Sin((timeNow * freq) + (i * 1.1f)));
                cradle.repulsorTransform.localScale = Vector3.one * (baseRadius * rPulse);
            }
        }
    }

    /// <summary>
    /// Menerapkan Fresnel Rim Glow (Linear eksplisit dari AirWeavingLightingAndShaderRig) ke prop melayang
    /// milik node terkait via MaterialPropertyBlock tunggal (100% Zero-GC, tidak menimpa warna biner Accent).
    /// </summary>
    public void UpdateNodeFresnelHighlight(GateNode node, Color linearRimColor, float rimPower, float rimIntensity)
    {
        if (!isRigActive || node == null || sharedRimMpb == null)
        {
            return;
        }

        for (int i = 0; i < inputSlots.Length; i++)
        {
            InputPropSlot slot = inputSlots[i];
            if (slot != null && slot.node == node)
            {
                ApplyRimToRenderersIfDirty(
                    slot.rimRenderers,
                    linearRimColor,
                    rimPower,
                    rimIntensity,
                    ref slot.hasRimState,
                    ref slot.lastRimColor,
                    ref slot.lastRimPower,
                    ref slot.lastRimIntensity);
                return;
            }
        }

        if (outputSlot != null && outputSlot.node == node)
        {
            int vIdx = outputSlot.activeVariantIndex;
            if (outputSlot.variants != null && vIdx >= 0 && vIdx < outputSlot.variants.Length && outputSlot.variants[vIdx] != null)
            {
                ApplyRimToRenderersIfDirty(
                    outputSlot.variants[vIdx].rimRenderers,
                    linearRimColor,
                    rimPower,
                    rimIntensity,
                    ref outputSlot.hasRimState,
                    ref outputSlot.lastRimColor,
                    ref outputSlot.lastRimPower,
                    ref outputSlot.lastRimIntensity);
            }
            return;
        }

        for (int i = 0; i < gateCradleSlots.Count; i++)
        {
            GateCradleSlot cradle = gateCradleSlots[i];
            if (cradle != null && cradle.node == node)
            {
                ApplyRimToRenderersIfDirty(
                    cradle.rimRenderers,
                    linearRimColor,
                    rimPower,
                    rimIntensity * 0.85f,
                    ref cradle.hasRimState,
                    ref cradle.lastRimColor,
                    ref cradle.lastRimPower,
                    ref cradle.lastRimIntensity);
                return;
            }
        }
    }

    /// <summary>
    /// Menonaktifkan seluruh prop melayang via SetActive(false) (kebal Vuforia DefaultObserverEventHandler)
    /// dan memulihkan 100% diorama darat v5 &amp; state Mode Kartu pada seluruh 8 GateNode.
    /// </summary>
    public void DeactivateAndRestoreForCardMode()
    {
        for (int i = 0; i < inputSlots.Length; i++)
        {
            InputPropSlot slot = inputSlots[i];
            if (slot == null)
            {
                continue;
            }

            if (slot.rootObj != null && slot.rootObj.activeSelf)
            {
                slot.rootObj.SetActive(false);
            }
            ClearRimPropertyBlocks(slot.rimRenderers);
            slot.hasRimState = false;
            if (slot.node != null)
            {
                slot.node.SetExternalBodyVisualSuppressed(false, null, null);
            }
        }

        if (outputSlot != null)
        {
            if (outputSlot.rootObj != null && outputSlot.rootObj.activeSelf)
            {
                outputSlot.rootObj.SetActive(false);
            }
            if (outputSlot.variants != null)
            {
                for (int v = 0; v < outputSlot.variants.Length; v++)
                {
                    if (outputSlot.variants[v] != null)
                    {
                        ClearRimPropertyBlocks(outputSlot.variants[v].rimRenderers);
                    }
                }
            }
            outputSlot.hasRimState = false;
            if (outputSlot.node != null)
            {
                outputSlot.node.SetExternalBodyVisualSuppressed(false, null, null);
            }
        }

        for (int i = 0; i < gateCradleSlots.Count; i++)
        {
            GateCradleSlot cradle = gateCradleSlots[i];
            if (cradle == null)
            {
                continue;
            }

            if (cradle.rootObj != null && cradle.rootObj.activeSelf)
            {
                cradle.rootObj.SetActive(false);
            }
            ClearRimPropertyBlocks(cradle.rimRenderers);
            cradle.hasRimState = false;
            if (cradle.node != null)
            {
                cradle.node.RegisterExternalFloatingPropRoot(null);
            }
        }

        isRigActive = false;
    }

    private void OnDisable()
    {
        if (isRigActive)
        {
            DeactivateAndRestoreForCardMode();
        }
    }

    private void OnDestroy()
    {
        DeactivateAndRestoreForCardMode();

        for (int i = 0; i < ownedRootObjects.Count; i++)
        {
            if (ownedRootObjects[i] != null)
            {
                Destroy(ownedRootObjects[i]);
            }
        }
        ownedRootObjects.Clear();

        for (int i = 0; i < ownedMaterials.Count; i++)
        {
            if (ownedMaterials[i] != null)
            {
                Destroy(ownedMaterials[i]);
            }
        }
        ownedMaterials.Clear();

        DestroyMesh(ref sharedRepulsorRingMesh);
        DestroyMesh(ref sharedFallbackTorusMesh);
        DestroyMesh(ref sharedFallbackDiamondMesh);

        inputSlots[0] = null;
        inputSlots[1] = null;
        outputSlot = null;
        gateCradleSlots.Clear();
        sharedRimMpb = null;
        isInitialized = false;
    }

    // =========================================================================
    // BUILDERS: INPUT BEACON, 5 OUTPUT VARIANTS, & 5 GATE HOVER CRADLES
    // =========================================================================

    private void RebindMissingSlotsIfNeeded(
        GateNode inputANode,
        GateNode inputBNode,
        GateNode outputNode,
        List<GateNode> logicGateNodes)
    {
        if (inputSlots[0] == null && inputANode != null)
        {
            BuildInputSlot(0, inputANode, isInputB: false);
        }
        if (inputSlots[1] == null && inputBNode != null)
        {
            BuildInputSlot(1, inputBNode, isInputB: true);
        }
        if (outputSlot == null && outputNode != null)
        {
            BuildOutputSlot(outputNode);
        }
        if (gateCradleSlots.Count == 0 && logicGateNodes != null && logicGateNodes.Count > 0)
        {
            BuildGateCradleSlots(logicGateNodes);
        }
    }

    private void BuildInputSlot(int slotIndex, GateNode node, bool isInputB)
    {
        if (node == null)
        {
            return;
        }

        GameObject rootObj = CreateFloatingPropRoot(node.transform);
        Transform bounceRoot = CreateEmptyChild("InputBeacon_BounceRoot", rootObj.transform, Vector3.zero);

        tempRendererList.Clear();
        tempAccentList.Clear();
        tempGlassList.Clear();

        GameObject prefab = LoadAirPropPrefab(InputBeaconPropName);
        GameObject modelRoot;
        Transform leverPivot = null;
        Transform pulseHaloPivot = null;

        if (prefab != null)
        {
            modelRoot = Instantiate(prefab);
            modelRoot.name = prefab.name;
            RemoveCollidersRecursive(modelRoot);

            Transform pivotHolder = NormalizeModelToEnvelope(
                modelRoot,
                bounceRoot,
                InputMaxFootprintXZ,
                InputMaxHeightY,
                InputBaseSurfaceY);

            Color idColor = isInputB ? InputBIdentityGoldColor : InputAIdentityCyanColor;
            SanitizePropHierarchyMaterials(
                modelRoot,
                tempRendererList,
                tempAccentList,
                tempGlassList,
                identityRingSubName: "Input_IdentityRing",
                identityRingColor: idColor);

            Transform rawLever = FindDeepChild(modelRoot.transform, "Input_LeverPivot");
            if (rawLever != null)
            {
                leverPivot = CreatePivotForSubMesh(rawLever, new Vector3(0.5f, 0.14f, 0.5f));
            }

            Transform rawHalo = FindDeepChild(modelRoot.transform, "Input_PulseHalo");
            if (rawHalo != null)
            {
                pulseHaloPivot = CreatePivotForSubMesh(rawHalo, new Vector3(0.5f, 0.5f, 0.5f));
            }

            _ = pivotHolder;
        }
        else
        {
            BuildFallbackInputBeacon(bounceRoot, isInputB, tempRendererList, tempAccentList, out leverPivot, out pulseHaloPivot);
        }

        Color repCol = isInputB ? RepulsorInputBColor : RepulsorInputAColor;
        Material repMat = CreateSpritesMaterial(repCol, RepulsorGlowRenderQueue);
        Transform repT = CreateRepulsorGlowDisk(bounceRoot, new Vector3(0f, 0.008f, 0f), 0.145f, repMat);

        InputPropSlot slot = new InputPropSlot
        {
            node = node,
            isInputB = isInputB,
            rootObj = rootObj,
            bounceRoot = bounceRoot,
            leverPivot = leverPivot,
            pulseHaloPivot = pulseHaloPivot,
            pulseHaloBasePos = pulseHaloPivot != null ? pulseHaloPivot.localPosition : Vector3.zero,
            pulseHaloBaseScale = pulseHaloPivot != null ? pulseHaloPivot.localScale : Vector3.one,
            repulsorTransform = repT,
            repulsorMat = repMat,
            reactiveAccentMats = tempAccentList.ToArray(),
            rimRenderers = tempRendererList.ToArray(),
            currentLeverAngle = node.currentOutputValue ? InputLeverAngleDeg : -InputLeverAngleDeg,
            phase = isInputB ? 1.8f : 0.2f
        };

        if (slot.leverPivot != null)
        {
            slot.leverPivot.localRotation = Quaternion.Euler(0f, 0f, -slot.currentLeverAngle);
        }

        rootObj.SetActive(false);
        inputSlots[slotIndex] = slot;
    }

    private void BuildOutputSlot(GateNode node)
    {
        if (node == null)
        {
            return;
        }

        GameObject rootObj = CreateFloatingPropRoot(node.transform);
        Transform bounceRoot = CreateEmptyChild("OutputAirProp_BounceRoot", rootObj.transform, Vector3.zero);

        OutputVariantRig[] variants = new OutputVariantRig[OutputVariantCount];
        for (int v = 0; v < OutputVariantCount; v++)
        {
            variants[v] = BuildOutputVariantRig(v, bounceRoot);
            if (variants[v] != null && variants[v].variantRootObj != null)
            {
                variants[v].variantRootObj.SetActive(false);
            }
        }

        Material repMat = CreateSpritesMaterial(RepulsorLowColor, RepulsorGlowRenderQueue);
        Transform repT = CreateRepulsorGlowDisk(bounceRoot, new Vector3(0f, 0.008f, 0f), 0.175f, repMat);

        rootObj.SetActive(false);
        outputSlot = new OutputPropSlot
        {
            node = node,
            rootObj = rootObj,
            bounceRoot = bounceRoot,
            variants = variants,
            repulsorTransform = repT,
            repulsorMat = repMat,
            activeVariantIndex = -1,
            lastBinaryState = -1
        };
    }

    private OutputVariantRig BuildOutputVariantRig(int variantIndex, Transform parentBounceRoot)
    {
        GameObject variantRoot = CreateEmptyChild($"AirOutputVariant_{variantIndex}", parentBounceRoot, Vector3.zero).gameObject;
        OutputVariantRig rig = new OutputVariantRig
        {
            variantRootObj = variantRoot
        };

        tempRendererList.Clear();
        tempAccentList.Clear();
        tempGlassList.Clear();

        string propName = OutputVariantPropNames[variantIndex];
        GameObject prefab = LoadAirPropPrefab(propName);

        if (prefab != null)
        {
            GameObject modelInstance = Instantiate(prefab);
            modelInstance.name = prefab.name;
            RemoveCollidersRecursive(modelInstance);

            NormalizeModelToEnvelope(
                modelInstance,
                variantRoot.transform,
                OutputMaxFootprintXZ,
                OutputMaxHeightY,
                OutputBaseSurfaceY);

            SanitizePropHierarchyMaterials(
                modelInstance,
                tempRendererList,
                tempAccentList,
                tempGlassList,
                identityRingSubName: null,
                identityRingColor: Color.white);

            BindOutputVariantKineticPivots(variantIndex, modelInstance.transform, rig);
        }
        else
        {
            BuildFallbackOutputVariant(variantIndex, variantRoot.transform, tempRendererList, tempAccentList, rig);
        }

        rig.reactiveAccentMats = tempAccentList.ToArray();
        rig.glassMats = tempGlassList.ToArray();
        rig.rimRenderers = tempRendererList.ToArray();
        return rig;
    }

    private void BindOutputVariantKineticPivots(int variantIndex, Transform modelRoot, OutputVariantRig rig)
    {
        switch (variantIndex)
        {
            case 0: // Sentinel Beacon Drone
            {
                Transform rawRotor = FindDeepChild(modelRoot, "Drone_SirenRotor");
                if (rawRotor != null)
                {
                    rig.droneRotorPivot = CreatePivotForSubMesh(rawRotor, new Vector3(0.5f, 0.5f, 0.5f));
                    BuildDroneSweepBeams(rig.droneRotorPivot, rig);
                }

                Transform rawPulse = FindDeepChild(modelRoot, "Drone_PulseRing");
                if (rawPulse != null)
                {
                    rig.dronePulseRingPivot = CreatePivotForSubMesh(rawPulse, new Vector3(0.5f, 0.5f, 0.5f));
                    rig.dronePulseRingBaseScale = rig.dronePulseRingPivot.localScale;
                }
                break;
            }

            case 1: // Dual-Way Luminance Relay Spire
            {
                rig.relayStepMats = new Material[3];
                for (int s = 0; s < 3; s++)
                {
                    Transform stepT = FindDeepChild(modelRoot, $"Relay_StepRing_{s}");
                    if (stepT != null)
                    {
                        rig.relayStepMats[s] = ExtractDedicatedStepMaterial(stepT, tempAccentList);
                    }
                }

                Transform rawLower = FindDeepChild(modelRoot, "Relay_HaloLower");
                if (rawLower != null)
                {
                    rig.relayHaloLowerPivot = CreatePivotForSubMesh(rawLower, new Vector3(0.5f, 0.5f, 0.5f));
                }

                Transform rawUpper = FindDeepChild(modelRoot, "Relay_HaloUpper");
                if (rawUpper != null)
                {
                    rig.relayHaloUpperPivot = CreatePivotForSubMesh(rawUpper, new Vector3(0.5f, 0.5f, 0.5f));
                }

                Transform rawCore = FindDeepChild(modelRoot, "Relay_CoreLantern");
                if (rawCore != null)
                {
                    rig.relayCorePivot = CreatePivotForSubMesh(rawCore, new Vector3(0.5f, 0.5f, 0.5f));
                }
                break;
            }

            case 2: // Orbital Holo-Vault
            {
                Transform rawShutterL = FindDeepChild(modelRoot, "Vault_ShutterL");
                if (rawShutterL != null)
                {
                    rig.vaultShutterLeft = CreatePivotForSubMesh(rawShutterL, new Vector3(0.5f, 0.5f, 0.5f));
                    rig.vaultShutterLeftBasePos = rig.vaultShutterLeft.localPosition;
                    rig.vaultShutterLeftOutwardSign = Mathf.Abs(rig.vaultShutterLeftBasePos.x) > 0.001f
                        ? Mathf.Sign(rig.vaultShutterLeftBasePos.x)
                        : -1f;
                }

                Transform rawShutterR = FindDeepChild(modelRoot, "Vault_ShutterR");
                if (rawShutterR != null)
                {
                    rig.vaultShutterRight = CreatePivotForSubMesh(rawShutterR, new Vector3(0.5f, 0.5f, 0.5f));
                    rig.vaultShutterRightBasePos = rig.vaultShutterRight.localPosition;
                    rig.vaultShutterRightOutwardSign = Mathf.Abs(rig.vaultShutterRightBasePos.x) > 0.001f
                        ? Mathf.Sign(rig.vaultShutterRightBasePos.x)
                        : 1f;
                }

                Transform rawWheel = FindDeepChild(modelRoot, "Vault_LockWheel");
                if (rawWheel != null)
                {
                    rig.vaultWheelPivot = CreatePivotForSubMesh(rawWheel, new Vector3(0.5f, 0.5f, 0.5f));
                }

                Transform rawInner = FindDeepChild(modelRoot, "Vault_InnerCore");
                if (rawInner != null)
                {
                    rig.vaultInnerCorePivot = CreatePivotForSubMesh(rawInner, new Vector3(0.5f, 0.5f, 0.5f));
                    rig.vaultInnerCoreBaseScale = rig.vaultInnerCorePivot.localScale;
                }
                break;
            }

            case 3: // Containment Reactor Core
            {
                Transform rawGyroA = FindDeepChild(modelRoot, "Reactor_GyroRingA");
                if (rawGyroA != null)
                {
                    rig.reactorGyroPivotA = CreatePivotForSubMesh(rawGyroA, new Vector3(0.5f, 0.5f, 0.5f));
                }

                Transform rawGyroB = FindDeepChild(modelRoot, "Reactor_GyroRingB");
                if (rawGyroB != null)
                {
                    rig.reactorGyroPivotB = CreatePivotForSubMesh(rawGyroB, new Vector3(0.5f, 0.5f, 0.5f));
                }

                Transform rawPlasma = FindDeepChild(modelRoot, "Reactor_PlasmaCore");
                if (rawPlasma != null)
                {
                    rig.reactorPlasmaPivot = CreatePivotForSubMesh(rawPlasma, new Vector3(0.5f, 0.5f, 0.5f));
                    rig.reactorPlasmaBaseScale = rig.reactorPlasmaPivot.localScale;
                }
                break;
            }

            default: // Varian 4: Twin-Core Resonator
            {
                Transform rawCryL = FindDeepChild(modelRoot, "TwinCore_CrystalL");
                if (rawCryL != null)
                {
                    rig.twinCrystalLeftPivot = CreatePivotForSubMesh(rawCryL, new Vector3(0.5f, 0.5f, 0.5f));
                    rig.twinCrystalLeftBaseScale = rig.twinCrystalLeftPivot.localScale;
                }

                Transform rawCryR = FindDeepChild(modelRoot, "TwinCore_CrystalR");
                if (rawCryR != null)
                {
                    rig.twinCrystalRightPivot = CreatePivotForSubMesh(rawCryR, new Vector3(0.5f, 0.5f, 0.5f));
                    rig.twinCrystalRightBaseScale = rig.twinCrystalRightPivot.localScale;
                }

                Transform rawOrbA = FindDeepChild(modelRoot, "TwinCore_OrbitRingA");
                if (rawOrbA != null)
                {
                    rig.twinOrbitPivotA = CreatePivotForSubMesh(rawOrbA, new Vector3(0.5f, 0.5f, 0.5f));
                }

                Transform rawOrbB = FindDeepChild(modelRoot, "TwinCore_OrbitRingB");
                if (rawOrbB != null)
                {
                    rig.twinOrbitPivotB = CreatePivotForSubMesh(rawOrbB, new Vector3(0.5f, 0.5f, 0.5f));
                }
                break;
            }
        }
    }

    private void BuildDroneSweepBeams(Transform rotorPivot, OutputVariantRig rig)
    {
        Material beamMat = CreateSpritesMaterial(SirenBeamHighColor, SirenBeamRenderQueue);

        GameObject beamsGroup = CreateEmptyChild("Drone_SweepBeams", rotorPivot, Vector3.zero).gameObject;
        CreatePrimitiveNoCollider("Drone_BeamL", PrimitiveType.Cube, beamsGroup.transform,
            new Vector3(-0.27f, 0.01f, 0f), new Vector3(0f, 0f, -6f), new Vector3(0.36f, 0.065f, 0.045f), beamMat);
        CreatePrimitiveNoCollider("Drone_BeamR", PrimitiveType.Cube, beamsGroup.transform,
            new Vector3(0.27f, 0.01f, 0f), new Vector3(0f, 0f, 6f), new Vector3(0.36f, 0.065f, 0.045f), beamMat);

        beamsGroup.SetActive(false);
        rig.droneSweepBeamsObj = beamsGroup;
    }

    private void BuildGateCradleSlots(List<GateNode> logicGateNodes)
    {
        gateCradleSlots.Clear();
        if (logicGateNodes == null)
        {
            return;
        }

        GameObject cradlePrefab = LoadAirPropPrefab(GateHoverCradlePropName);

        for (int i = 0; i < logicGateNodes.Count; i++)
        {
            GateNode node = logicGateNodes[i];
            if (node == null || !IsLogicGate(node.nodeType))
            {
                continue;
            }

            GameObject rootObj = CreateFloatingPropRoot(node.transform);
            tempRendererList.Clear();
            tempAccentList.Clear();
            tempGlassList.Clear();

            Transform thrusterCorePivot = null;

            if (cradlePrefab != null)
            {
                GameObject instance = Instantiate(cradlePrefab);
                instance.name = cradlePrefab.name;
                RemoveCollidersRecursive(instance);

                NormalizeModelToUndersideSlab(
                    instance,
                    rootObj.transform,
                    CradleTargetWidthX,
                    CradleTargetDepthZ,
                    CradleTargetHeightY,
                    CradleTopAttachY);

                SanitizePropHierarchyMaterials(
                    instance,
                    tempRendererList,
                    tempAccentList,
                    tempGlassList,
                    identityRingSubName: null,
                    identityRingColor: Color.white);

                Transform rawCore = FindDeepChild(instance.transform, "Cradle_ThrusterCore");
                if (rawCore != null)
                {
                    thrusterCorePivot = CreatePivotForSubMesh(rawCore, new Vector3(0.5f, 0.5f, 0.5f));
                }
            }
            else
            {
                BuildFallbackGateCradle(rootObj.transform, tempRendererList, tempAccentList, out thrusterCorePivot);
            }

            Material repMat = CreateSpritesMaterial(RepulsorLowColor, RepulsorGlowRenderQueue);
            float repY = CradleTopAttachY - CradleTargetHeightY - 0.004f;
            Transform repT = CreateRepulsorGlowDisk(rootObj.transform, new Vector3(0f, repY, 0f), 0.132f, repMat);

            rootObj.SetActive(false);
            gateCradleSlots.Add(new GateCradleSlot
            {
                node = node,
                rootObj = rootObj,
                thrusterCorePivot = thrusterCorePivot,
                repulsorTransform = repT,
                repulsorMat = repMat,
                reactiveAccentMats = tempAccentList.ToArray(),
                rimRenderers = tempRendererList.ToArray()
            });
        }
    }

    // =========================================================================
    // RUNTIME STATE SYNC & KINETIC ANIMATIONS (100% ZERO-GC)
    // =========================================================================

    private void SyncActiveOutputVariant(bool forceRefresh)
    {
        if (outputSlot == null || outputSlot.node == null || outputSlot.variants == null)
        {
            return;
        }

        int desiredIdx = outputSlot.node.ActiveDioramaVariantIndex;
        desiredIdx = ((desiredIdx % OutputVariantCount) + OutputVariantCount) % OutputVariantCount;
        if (!forceRefresh && desiredIdx == outputSlot.activeVariantIndex)
        {
            return;
        }

        outputSlot.activeVariantIndex = desiredIdx;
        outputSlot.lastBinaryState = -1;
        outputSlot.hasRimState = false;

        for (int i = 0; i < outputSlot.variants.Length; i++)
        {
            OutputVariantRig rig = outputSlot.variants[i];
            if (rig == null || rig.variantRootObj == null)
            {
                continue;
            }

            bool show = (i == desiredIdx);
            if (rig.variantRootObj.activeSelf != show)
            {
                rig.variantRootObj.SetActive(show);
            }
        }

        OutputVariantRig activeRig = outputSlot.variants[desiredIdx];
        if (activeRig != null)
        {
            activeRig.lastRelayChaseStep = -2;
            activeRig.vaultCurrentSlide = 0f;
            ApplyOutputBinaryState(outputSlot.node.currentOutputValue);
        }
    }

    private void ApplyInputBinaryState(InputPropSlot slot, bool isHigh)
    {
        slot.lastBinaryState = isHigh ? 1 : 0;
        Color baseCol = isHigh ? AccentHighColor : AccentLowColor;
        Color emitCol = isHigh ? AccentHighEmission : AccentLowEmission;
        ApplyAccentMaterialColors(slot.reactiveAccentMats, baseCol, emitCol);

        if (slot.repulsorMat != null)
        {
            Color idRep = slot.isInputB ? RepulsorInputBColor : RepulsorInputAColor;
            idRep.a = isHigh ? 0.64f : 0.34f;
            slot.repulsorMat.color = idRep;
        }
    }

    private void ApplyOutputBinaryState(bool isHigh)
    {
        if (outputSlot == null || outputSlot.variants == null)
        {
            return;
        }

        int vIdx = outputSlot.activeVariantIndex;
        if (vIdx < 0 || vIdx >= outputSlot.variants.Length)
        {
            return;
        }

        outputSlot.lastBinaryState = isHigh ? 1 : 0;
        OutputVariantRig rig = outputSlot.variants[vIdx];
        if (rig == null)
        {
            return;
        }

        Color baseCol = isHigh ? AccentHighColor : AccentLowColor;
        Color emitCol = isHigh ? AccentHighEmission : AccentLowEmission;
        ApplyAccentMaterialColors(rig.reactiveAccentMats, baseCol, emitCol);

        if (rig.glassMats != null)
        {
            Color gCol = isHigh ? GlassTintHighColor : GlassTintLowColor;
            for (int i = 0; i < rig.glassMats.Length; i++)
            {
                if (rig.glassMats[i] != null)
                {
                    rig.glassMats[i].color = gCol;
                }
            }
        }

        if (rig.droneSweepBeamsObj != null && rig.droneSweepBeamsObj.activeSelf != isHigh)
        {
            rig.droneSweepBeamsObj.SetActive(isHigh);
        }

        if (outputSlot.repulsorMat != null)
        {
            outputSlot.repulsorMat.color = isHigh ? RepulsorHighColor : RepulsorLowColor;
        }
    }

    private void AnimateActiveOutputVariant(bool isHigh, float dt, float timeNow)
    {
        int vIdx = outputSlot.activeVariantIndex;
        if (outputSlot.variants == null || vIdx < 0 || vIdx >= outputSlot.variants.Length)
        {
            return;
        }

        OutputVariantRig rig = outputSlot.variants[vIdx];
        if (rig == null)
        {
            return;
        }

        switch (vIdx)
        {
            case 0: // Sentinel Beacon Drone
                if (rig.droneSweepBeamsObj != null && rig.droneSweepBeamsObj.activeSelf != isHigh)
                {
                    rig.droneSweepBeamsObj.SetActive(isHigh);
                }
                if (rig.droneRotorPivot != null)
                {
                    float rotorSpeed = isHigh ? 360f : 26f;
                    rig.droneRotorPivot.Rotate(Vector3.up, rotorSpeed * dt, Space.Self);
                }
                if (rig.dronePulseRingPivot != null)
                {
                    float pulse = isHigh ? (1.0f + (0.14f * Mathf.Sin(timeNow * 8.5f))) : 1.0f;
                    rig.dronePulseRingPivot.localScale = rig.dronePulseRingBaseScale * pulse;
                }
                break;

            case 1: // Dual-Way Luminance Relay Spire
                if (rig.relayStepMats != null && rig.relayStepMats.Length == 3)
                {
                    int activeStep;
                    if (isHigh)
                    {
                        rig.relayChasePhase = Mathf.Repeat(rig.relayChasePhase + (dt * 4.5f), 3f);
                        activeStep = Mathf.FloorToInt(rig.relayChasePhase);
                    }
                    else
                    {
                        activeStep = -1;
                    }

                    if (activeStep != rig.lastRelayChaseStep)
                    {
                        rig.lastRelayChaseStep = activeStep;
                        for (int s = 0; s < 3; s++)
                        {
                            Material stepMat = rig.relayStepMats[s];
                            if (stepMat == null)
                            {
                                continue;
                            }

                            if (activeStep < 0)
                            {
                                SetHardwareColorAndEmission(stepMat, RelayStepIdleColor, RelayStepIdleEmission);
                            }
                            else if (s == activeStep)
                            {
                                SetHardwareColorAndEmission(stepMat, RelayStepActiveColor, RelayStepActiveEmission);
                            }
                            else
                            {
                                SetHardwareColorAndEmission(stepMat, RelayStepTrailColor, RelayStepTrailEmission);
                            }
                        }
                    }
                }

                float haloSpeed = isHigh ? 145f : 18f;
                if (rig.relayHaloLowerPivot != null)
                {
                    rig.relayHaloLowerPivot.Rotate(Vector3.up, haloSpeed * dt, Space.Self);
                }
                if (rig.relayHaloUpperPivot != null)
                {
                    rig.relayHaloUpperPivot.Rotate(Vector3.up, -haloSpeed * 1.15f * dt, Space.Self);
                }
                if (rig.relayCorePivot != null && isHigh)
                {
                    rig.relayCorePivot.Rotate(Vector3.up, 72f * dt, Space.Self);
                }
                break;

            case 2: // Orbital Holo-Vault
                float targetSlide = isHigh ? VaultShutterSlideTravel : 0f;
                rig.vaultCurrentSlide = Mathf.Lerp(rig.vaultCurrentSlide, targetSlide, 1f - Mathf.Exp(-10.5f * dt));

                if (rig.vaultShutterLeft != null)
                {
                    rig.vaultShutterLeft.localPosition = rig.vaultShutterLeftBasePos
                        + new Vector3(rig.vaultShutterLeftOutwardSign * rig.vaultCurrentSlide, 0f, 0f);
                }
                if (rig.vaultShutterRight != null)
                {
                    rig.vaultShutterRight.localPosition = rig.vaultShutterRightBasePos
                        + new Vector3(rig.vaultShutterRightOutwardSign * rig.vaultCurrentSlide, 0f, 0f);
                }
                if (rig.vaultWheelPivot != null && isHigh)
                {
                    rig.vaultWheelPivot.Rotate(Vector3.forward, 250f * dt, Space.Self);
                }
                if (rig.vaultInnerCorePivot != null)
                {
                    if (isHigh)
                    {
                        rig.vaultInnerCorePivot.Rotate(Vector3.up, 165f * dt, Space.Self);
                        float cPulse = 1.0f + (0.14f * Mathf.Sin(timeNow * 6.5f));
                        rig.vaultInnerCorePivot.localScale = rig.vaultInnerCoreBaseScale * cPulse;
                    }
                    else
                    {
                        rig.vaultInnerCorePivot.localScale = rig.vaultInnerCoreBaseScale;
                    }
                }
                break;

            case 3: // Containment Reactor Core
                float gyroSpeed = isHigh ? 235f : 22f;
                if (rig.reactorGyroPivotA != null)
                {
                    rig.reactorGyroPivotA.Rotate(new Vector3(0.24f, 1.0f, 0.14f), gyroSpeed * dt, Space.Self);
                }
                if (rig.reactorGyroPivotB != null)
                {
                    rig.reactorGyroPivotB.Rotate(new Vector3(-0.24f, -1.0f, 0.14f), gyroSpeed * 1.22f * dt, Space.Self);
                }
                if (rig.reactorPlasmaPivot != null)
                {
                    float pPulse = isHigh ? (1.0f + (0.15f * Mathf.Sin(timeNow * 7.2f))) : 1.0f;
                    rig.reactorPlasmaPivot.localScale = rig.reactorPlasmaBaseScale * pPulse;
                }
                break;

            default: // Varian 4: Twin-Core Resonator
                float syncSpeed = isHigh ? 225f : 20f;
                rig.twinSyncSpinDeg = Mathf.Repeat(rig.twinSyncSpinDeg + (syncSpeed * dt), 360f);
                Quaternion syncRot = Quaternion.Euler(0f, rig.twinSyncSpinDeg, 0f);
                float tPulse = isHigh ? (1.0f + (0.13f * Mathf.Sin(timeNow * 6.4f))) : 1.0f;

                if (rig.twinCrystalLeftPivot != null)
                {
                    rig.twinCrystalLeftPivot.localRotation = syncRot;
                    rig.twinCrystalLeftPivot.localScale = rig.twinCrystalLeftBaseScale * tPulse;
                }
                if (rig.twinCrystalRightPivot != null)
                {
                    rig.twinCrystalRightPivot.localRotation = isHigh ? syncRot : Quaternion.Euler(0f, -rig.twinSyncSpinDeg * 0.65f, 0f);
                    rig.twinCrystalRightPivot.localScale = rig.twinCrystalRightBaseScale * tPulse;
                }
                if (rig.twinOrbitPivotA != null)
                {
                    rig.twinOrbitPivotA.Rotate(Vector3.up, (isHigh ? 240f : 22f) * dt, Space.Self);
                }
                if (rig.twinOrbitPivotB != null)
                {
                    rig.twinOrbitPivotB.Rotate(Vector3.up, (isHigh ? -285f : -26f) * dt, Space.Self);
                }
                break;
        }
    }

    private void ApplyGateCradleVisualState(GateCradleSlot cradle, bool isGrabbed, bool isInDeleteZone)
    {
        bool isTracked = cradle.node.isTracked;
        bool isHigh = isTracked && cradle.node.currentOutputValue;
        int stateKey = (isTracked ? 1 : 0)
                       | (isHigh ? 2 : 0)
                       | (isGrabbed ? 4 : 0)
                       | (isInDeleteZone ? 8 : 0);

        if (stateKey == cradle.lastVisualStateKey)
        {
            return;
        }

        cradle.lastVisualStateKey = stateKey;

        Color accentCol;
        Color accentEmit;
        Color repCol;

        if (isInDeleteZone)
        {
            accentCol = RepulsorDeleteColor;
            accentEmit = RepulsorDeleteColor * 1.25f;
            repCol = RepulsorDeleteColor;
        }
        else if (isGrabbed)
        {
            accentCol = InputBIdentityGoldColor;
            accentEmit = InputBIdentityGoldColor * 1.15f;
            repCol = RepulsorGrabColor;
        }
        else if (!isTracked)
        {
            accentCol = AccentParkedColor;
            accentEmit = AccentParkedEmission;
            repCol = new Color(0.24f, 0.82f, 0.96f, 0.30f);
        }
        else if (isHigh)
        {
            accentCol = AccentHighColor;
            accentEmit = AccentHighEmission;
            repCol = RepulsorHighColor;
        }
        else
        {
            accentCol = AccentLowColor;
            accentEmit = AccentLowEmission;
            repCol = RepulsorLowColor;
        }

        ApplyAccentMaterialColors(cradle.reactiveAccentMats, accentCol, accentEmit);
        if (cradle.repulsorMat != null)
        {
            cradle.repulsorMat.color = repCol;
        }
    }

    // =========================================================================
    // MATERIAL SANITIZATION, FRESNEL MPB, & BOUNDS NORMALIZATION
    // =========================================================================

    private void ResolveShaders()
    {
        cachedHardwareShader = AirWeavingLightingAndShaderRig.ResolveHardwareFresnelShader();
        if (cachedHardwareShader == null || !cachedHardwareShader.isSupported)
        {
            cachedHardwareShader = Shader.Find("Standard");
        }
        if (cachedHardwareShader == null || !cachedHardwareShader.isSupported)
        {
            cachedHardwareShader = Shader.Find("Legacy Shaders/Diffuse");
        }

        cachedSpritesShader = Shader.Find("Sprites/Default");
        if (cachedSpritesShader == null || !cachedSpritesShader.isSupported)
        {
            cachedSpritesShader = Shader.Find("UI/Default");
        }
    }

    private void SanitizePropHierarchyMaterials(
        GameObject modelRoot,
        List<Renderer> outRimRenderers,
        List<Material> outReactiveAccentMats,
        List<Material> outGlassMats,
        string identityRingSubName,
        Color identityRingColor)
    {
        Renderer[] renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer rend = renderers[r];
            if (rend == null)
            {
                continue;
            }

            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;

            bool isIdentityRingObject = !string.IsNullOrEmpty(identityRingSubName)
                                        && string.Equals(rend.gameObject.name, identityRingSubName, StringComparison.Ordinal);

            Material[] srcMats = rend.sharedMaterials;
            if (srcMats == null || srcMats.Length == 0)
            {
                continue;
            }

            Material[] clonedMats = new Material[srcMats.Length];
            bool hasOpaqueHardwareMat = false;

            for (int m = 0; m < srcMats.Length; m++)
            {
                Material src = srcMats[m];
                string matName = src != null ? src.name : string.Empty;
                bool isGlassSlot = matName.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isDarkAccentSlot = matName.IndexOf("DarkAccent", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isDarkGreySlot = matName.IndexOf("DarkGrey", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isReactiveAccentSlot = !isIdentityRingObject
                                            && !isDarkAccentSlot
                                            && matName.IndexOf("Accent", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isGlassSlot)
                {
                    // Requirement #4: elemen transparan/kaca wajib pakai Sprites/Default (anti-strip, ZWrite Off, Cull Off)
                    Material glassMat = CreateSpritesMaterial(GlassTintLowColor, GlassRenderQueue);
                    clonedMats[m] = glassMat;
                    if (glassMat != null)
                    {
                        outGlassMats.Add(glassMat);
                    }
                    continue;
                }

                Color baseCol = ResolveBaseColorFromMaterial(src, isDarkGreySlot, isDarkAccentSlot);
                Material hwMat;
                if (isIdentityRingObject)
                {
                    hwMat = CreateHardwareMaterial(identityRingColor, metallic: 0.34f, smoothness: 0.68f, emissionColor: identityRingColor * 0.58f);
                }
                else if (isReactiveAccentSlot)
                {
                    hwMat = CreateHardwareMaterial(AccentLowColor, metallic: 0.22f, smoothness: 0.72f, emissionColor: AccentLowEmission);
                    if (hwMat != null)
                    {
                        outReactiveAccentMats.Add(hwMat);
                    }
                }
                else if (isDarkGreySlot)
                {
                    hwMat = CreateHardwareMaterial(baseCol, metallic: 0.28f, smoothness: 0.42f, emissionColor: Color.black);
                }
                else if (isDarkAccentSlot)
                {
                    hwMat = CreateHardwareMaterial(baseCol, metallic: 0.32f, smoothness: 0.52f, emissionColor: Color.black);
                }
                else
                {
                    hwMat = CreateHardwareMaterial(baseCol, metallic: 0.36f, smoothness: 0.62f, emissionColor: Color.black);
                }

                clonedMats[m] = hwMat;
                if (hwMat != null)
                {
                    hasOpaqueHardwareMat = true;
                }
            }

            rend.sharedMaterials = clonedMats;
            if (hasOpaqueHardwareMat)
            {
                outRimRenderers.Add(rend);
            }
        }
    }

    private Material ExtractDedicatedStepMaterial(Transform stepTransform, List<Material> accentPoolToRemoveFrom)
    {
        Renderer rend = stepTransform.GetComponentInChildren<Renderer>(true);
        if (rend == null)
        {
            return null;
        }

        Material[] mats = rend.sharedMaterials;
        if (mats == null || mats.Length == 0)
        {
            return null;
        }

        Material oldMat = mats[0];
        if (oldMat != null && accentPoolToRemoveFrom != null)
        {
            accentPoolToRemoveFrom.Remove(oldMat);
        }

        Material stepMat = CreateHardwareMaterial(RelayStepIdleColor, metallic: 0.22f, smoothness: 0.74f, emissionColor: RelayStepIdleEmission);
        mats[0] = stepMat;
        rend.sharedMaterials = mats;
        return stepMat;
    }

    private Material CreateHardwareMaterial(Color baseColor, float metallic, float smoothness, Color emissionColor)
    {
        Shader shader = cachedHardwareShader != null ? cachedHardwareShader : cachedSpritesShader;
        if (shader == null)
        {
            return null;
        }

        Material mat = new Material(shader);
        mat.renderQueue = -1;
        SetHardwareColorAndEmission(mat, baseColor, emissionColor);
        if (mat.HasProperty(MetallicPropId))
        {
            mat.SetFloat(MetallicPropId, metallic);
        }
        if (mat.HasProperty(GlossinessPropId))
        {
            mat.SetFloat(GlossinessPropId, smoothness);
        }
        ownedMaterials.Add(mat);
        return mat;
    }

    private Material CreateSpritesMaterial(Color tint, int renderQueue)
    {
        if (cachedSpritesShader == null)
        {
            return null;
        }

        Material mat = new Material(cachedSpritesShader);
        mat.color = tint;
        mat.renderQueue = renderQueue;
        ownedMaterials.Add(mat);
        return mat;
    }

    private static Color ResolveBaseColorFromMaterial(Material src, bool isDarkGrey, bool isDarkAccent)
    {
        if (src != null && src.HasProperty(ColorPropId))
        {
            Color c = src.GetColor(ColorPropId);
            if (c.r + c.g + c.b > 0.02f)
            {
                c.a = 1f;
                return c;
            }
        }

        if (isDarkAccent) return HullDarkAccentFallbackColor;
        if (isDarkGrey) return HullDarkGreyFallbackColor;
        return HullMainFallbackColor;
    }

    private static void ApplyAccentMaterialColors(Material[] mats, Color baseColor, Color emissionColor)
    {
        if (mats == null)
        {
            return;
        }

        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] != null)
            {
                SetHardwareColorAndEmission(mats[i], baseColor, emissionColor);
            }
        }
    }

    private static void SetHardwareColorAndEmission(Material mat, Color baseColor, Color emissionColor)
    {
        if (mat == null)
        {
            return;
        }

        if (mat.HasProperty(ColorPropId))
        {
            mat.SetColor(ColorPropId, baseColor);
        }
        else
        {
            mat.color = baseColor;
        }

        if (mat.HasProperty(EmissionColorPropId))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor(EmissionColorPropId, emissionColor);
        }
    }

    private void ApplyRimToRenderersIfDirty(
        Renderer[] renderers,
        Color linearRimColor,
        float rimPower,
        float rimIntensity,
        ref bool hasRimState,
        ref Color lastColor,
        ref float lastPower,
        ref float lastIntensity)
    {
        if (renderers == null || renderers.Length == 0 || sharedRimMpb == null)
        {
            return;
        }

        if (hasRimState
            && Mathf.Abs(lastColor.r - linearRimColor.r) < 0.005f
            && Mathf.Abs(lastColor.g - linearRimColor.g) < 0.005f
            && Mathf.Abs(lastColor.b - linearRimColor.b) < 0.005f
            && Mathf.Abs(lastPower - rimPower) < 0.02f
            && Mathf.Abs(lastIntensity - rimIntensity) < 0.015f)
        {
            return;
        }

        hasRimState = true;
        lastColor = linearRimColor;
        lastPower = rimPower;
        lastIntensity = rimIntensity;

        sharedRimMpb.Clear();
        sharedRimMpb.SetColor(RimColorPropId, linearRimColor);
        sharedRimMpb.SetFloat(RimPowerPropId, rimPower);
        sharedRimMpb.SetFloat(RimIntensityPropId, rimIntensity);

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].SetPropertyBlock(sharedRimMpb);
            }
        }
    }

    private static void ClearRimPropertyBlocks(Renderer[] renderers)
    {
        if (renderers == null)
        {
            return;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].SetPropertyBlock(null);
            }
        }
    }

    private GameObject CreateFloatingPropRoot(Transform parentNodeTransform)
    {
        GameObject rootObj = new GameObject(GateNode.ExternalFloatingPropRootName);
        rootObj.transform.SetParent(parentNodeTransform, false);
        rootObj.transform.localPosition = Vector3.zero;
        rootObj.transform.localRotation = Quaternion.identity;
        rootObj.transform.localScale = Vector3.one;
        ownedRootObjects.Add(rootObj);
        return rootObj;
    }

    private static Transform NormalizeModelToEnvelope(
        GameObject modelInstance,
        Transform containerParent,
        float maxFootprintXZ,
        float maxHeightY,
        float baseSurfaceY)
    {
        Transform pivot = CreateEmptyChild($"{modelInstance.name}_NormPivot", containerParent, Vector3.zero);
        modelInstance.transform.SetParent(pivot, false);
        modelInstance.transform.localPosition = Vector3.zero;
        modelInstance.transform.localRotation = Quaternion.identity;
        modelInstance.transform.localScale = Vector3.one;

        if (!TryComputeHierarchyLocalBounds(modelInstance.transform, pivot, out Bounds localBounds))
        {
            pivot.localPosition = new Vector3(0f, baseSurfaceY, 0f);
            return pivot;
        }

        float sizeX = Mathf.Max(0.0001f, localBounds.size.x);
        float sizeY = Mathf.Max(0.0001f, localBounds.size.y);
        float sizeZ = Mathf.Max(0.0001f, localBounds.size.z);
        float maxXZ = Mathf.Max(sizeX, sizeZ);

        float uniformScale = Mathf.Min(maxFootprintXZ / maxXZ, maxHeightY / sizeY);
        pivot.localScale = Vector3.one * uniformScale;
        pivot.localPosition = new Vector3(
            -localBounds.center.x * uniformScale,
            baseSurfaceY - (localBounds.min.y * uniformScale),
            -localBounds.center.z * uniformScale);
        return pivot;
    }

    private static void NormalizeModelToUndersideSlab(
        GameObject modelInstance,
        Transform containerParent,
        float targetWidthX,
        float targetDepthZ,
        float targetHeightY,
        float topAttachY)
    {
        Transform pivot = CreateEmptyChild($"{modelInstance.name}_SlabPivot", containerParent, Vector3.zero);
        modelInstance.transform.SetParent(pivot, false);
        modelInstance.transform.localPosition = Vector3.zero;
        modelInstance.transform.localRotation = Quaternion.identity;
        modelInstance.transform.localScale = Vector3.one;

        if (!TryComputeHierarchyLocalBounds(modelInstance.transform, pivot, out Bounds localBounds))
        {
            pivot.localPosition = new Vector3(0f, topAttachY - targetHeightY, 0f);
            return;
        }

        float sx = targetWidthX / Mathf.Max(0.0001f, localBounds.size.x);
        float sy = targetHeightY / Mathf.Max(0.0001f, localBounds.size.y);
        float sz = targetDepthZ / Mathf.Max(0.0001f, localBounds.size.z);

        pivot.localScale = new Vector3(sx, sy, sz);
        pivot.localPosition = new Vector3(
            -localBounds.center.x * sx,
            topAttachY - (localBounds.max.y * sy),
            -localBounds.center.z * sz);
    }

    /// <summary>
    /// Membungkus sub-mesh hasil impor .obj/.fbx dengan Transform pivot lokal tepat pada titik bounds
    /// yang diinginkan agar rotasi/skala kinetik berporos akurat tanpa bergantung pada origin .obj.
    /// </summary>
    private static Transform CreatePivotForSubMesh(Transform subMeshTransform, Vector3 normalizedPivot01)
    {
        Transform parent = subMeshTransform.parent;
        if (parent == null)
        {
            return subMeshTransform;
        }

        if (!TryComputeHierarchyLocalBounds(subMeshTransform, parent, out Bounds b))
        {
            return subMeshTransform;
        }

        Vector3 pivotLocalPos = new Vector3(
            Mathf.Lerp(b.min.x, b.max.x, normalizedPivot01.x),
            Mathf.Lerp(b.min.y, b.max.y, normalizedPivot01.y),
            Mathf.Lerp(b.min.z, b.max.z, normalizedPivot01.z));

        Transform pivot = CreateEmptyChild($"{subMeshTransform.name}_Pivot", parent, pivotLocalPos);
        Vector3 oldLocalPos = subMeshTransform.localPosition;
        Quaternion oldLocalRot = subMeshTransform.localRotation;
        Vector3 oldLocalScale = subMeshTransform.localScale;

        subMeshTransform.SetParent(pivot, false);
        subMeshTransform.localPosition = oldLocalPos - pivotLocalPos;
        subMeshTransform.localRotation = oldLocalRot;
        subMeshTransform.localScale = oldLocalScale;
        return pivot;
    }

    private static bool TryComputeHierarchyLocalBounds(Transform targetRoot, Transform referenceSpace, out Bounds result)
    {
        MeshFilter[] filters = targetRoot.GetComponentsInChildren<MeshFilter>(true);
        bool hasBounds = false;
        result = new Bounds(Vector3.zero, Vector3.zero);

        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter mf = filters[i];
            if (mf == null || mf.sharedMesh == null)
            {
                continue;
            }

            Matrix4x4 toRef = BuildLocalMatrix(mf.transform, referenceSpace);
            Bounds mb = mf.sharedMesh.bounds;
            Vector3 mn = mb.min;
            Vector3 mx = mb.max;

            EncapsulateCorner(toRef, new Vector3(mn.x, mn.y, mn.z), ref result, ref hasBounds);
            EncapsulateCorner(toRef, new Vector3(mn.x, mn.y, mx.z), ref result, ref hasBounds);
            EncapsulateCorner(toRef, new Vector3(mn.x, mx.y, mn.z), ref result, ref hasBounds);
            EncapsulateCorner(toRef, new Vector3(mn.x, mx.y, mx.z), ref result, ref hasBounds);
            EncapsulateCorner(toRef, new Vector3(mx.x, mn.y, mn.z), ref result, ref hasBounds);
            EncapsulateCorner(toRef, new Vector3(mx.x, mn.y, mx.z), ref result, ref hasBounds);
            EncapsulateCorner(toRef, new Vector3(mx.x, mx.y, mn.z), ref result, ref hasBounds);
            EncapsulateCorner(toRef, new Vector3(mx.x, mx.y, mx.z), ref result, ref hasBounds);
        }

        return hasBounds;
    }

    private static Matrix4x4 BuildLocalMatrix(Transform leaf, Transform ancestor)
    {
        Matrix4x4 m = Matrix4x4.identity;
        Transform cur = leaf;
        while (cur != null && cur != ancestor)
        {
            m = Matrix4x4.TRS(cur.localPosition, cur.localRotation, cur.localScale) * m;
            cur = cur.parent;
        }
        return m;
    }

    private static void EncapsulateCorner(Matrix4x4 mat, Vector3 pt, ref Bounds bounds, ref bool hasBounds)
    {
        Vector3 t = mat.MultiplyPoint3x4(pt);
        if (!hasBounds)
        {
            bounds = new Bounds(t, Vector3.zero);
            hasBounds = true;
        }
        else
        {
            bounds.Encapsulate(t);
        }
    }

    private static Transform FindDeepChild(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrEmpty(targetName))
        {
            return null;
        }

        if (string.Equals(root.name, targetName, StringComparison.Ordinal))
        {
            return root;
        }

        int count = root.childCount;
        for (int i = 0; i < count; i++)
        {
            Transform found = FindDeepChild(root.GetChild(i), targetName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static GameObject LoadAirPropPrefab(string propName)
    {
        if (string.IsNullOrEmpty(propName))
        {
            return null;
        }

        return Resources.Load<GameObject>(PropsResourcePrefix + propName);
    }

    private static bool IsLogicGate(NodeType type)
    {
        return type == NodeType.AND
               || type == NodeType.OR
               || type == NodeType.NOT
               || type == NodeType.XOR
               || type == NodeType.NAND;
    }

    private static void RemoveCollidersRecursive(GameObject rootObj)
    {
        Collider[] cols = rootObj.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null)
            {
                Destroy(cols[i]);
            }
        }
    }

    // =========================================================================
    // PROCEDURAL REPULSOR GLOW & GRACEFUL FALLBACK BUILDERS
    // =========================================================================

    private void EnsureSharedProceduralMeshes()
    {
        if (sharedRepulsorRingMesh == null)
        {
            sharedRepulsorRingMesh = BuildFlatAnnularRingMesh("AirProp_SharedRepulsorRingMesh", 16, innerRadius: 0.42f, outerRadius: 1.0f);
        }
        if (sharedFallbackTorusMesh == null)
        {
            sharedFallbackTorusMesh = BuildSimpleTorusMesh("AirProp_FallbackTorusMesh", 14, 6, 1.0f, 0.16f);
        }
        if (sharedFallbackDiamondMesh == null)
        {
            sharedFallbackDiamondMesh = BuildSimpleDiamondMesh("AirProp_FallbackDiamondMesh");
        }
    }

    private Transform CreateRepulsorGlowDisk(Transform parent, Vector3 localPos, float radius, Material mat)
    {
        GameObject obj = new GameObject("AirProp_RepulsorGlow");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPos;
        obj.transform.localRotation = Quaternion.identity;
        obj.transform.localScale = Vector3.one * radius;

        MeshFilter mf = obj.AddComponent<MeshFilter>();
        mf.sharedMesh = sharedRepulsorRingMesh;

        MeshRenderer mr = obj.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        if (mat != null)
        {
            mr.sharedMaterial = mat;
        }

        return obj.transform;
    }

    private void BuildFallbackInputBeacon(
        Transform parent,
        bool isInputB,
        List<Renderer> rimList,
        List<Material> accentList,
        out Transform leverPivot,
        out Transform pulseHaloPivot)
    {
        Material bodyMat = CreateHardwareMaterial(HullMainFallbackColor, 0.36f, 0.62f, Color.black);
        Color idCol = isInputB ? InputBIdentityGoldColor : InputAIdentityCyanColor;
        Material idMat = CreateHardwareMaterial(idCol, 0.34f, 0.68f, idCol * 0.55f);
        Material accentMat = CreateHardwareMaterial(AccentLowColor, 0.22f, 0.72f, AccentLowEmission);
        if (accentMat != null) accentList.Add(accentMat);

        rimList.Add(CreatePrimitiveNoCollider("FallbackInput_Body", PrimitiveType.Cylinder, parent,
            new Vector3(0f, 0.15f, 0f), Vector3.zero, new Vector3(0.32f, 0.08f, 0.32f), bodyMat));
        rimList.Add(CreateMeshPart("FallbackInput_IdRing", sharedFallbackTorusMesh, parent,
            new Vector3(0f, 0.15f, 0f), Vector3.zero, Vector3.one * 0.19f, idMat));

        leverPivot = CreateEmptyChild("FallbackInput_LeverPivot", parent, new Vector3(0f, 0.22f, 0f));
        rimList.Add(CreatePrimitiveNoCollider("FallbackInput_Shaft", PrimitiveType.Cylinder, leverPivot,
            new Vector3(0f, 0.07f, 0f), Vector3.zero, new Vector3(0.03f, 0.07f, 0.03f), bodyMat));
        rimList.Add(CreatePrimitiveNoCollider("FallbackInput_Grip", PrimitiveType.Cube, leverPivot,
            new Vector3(0f, 0.14f, 0f), Vector3.zero, new Vector3(0.05f, 0.05f, 0.16f), accentMat));

        pulseHaloPivot = CreateEmptyChild("FallbackInput_HaloPivot", parent, new Vector3(0f, 0.27f, 0f));
        rimList.Add(CreateMeshPart("FallbackInput_Halo", sharedFallbackTorusMesh, pulseHaloPivot,
            Vector3.zero, Vector3.zero, Vector3.one * 0.14f, accentMat));
    }

    private void BuildFallbackOutputVariant(
        int variantIndex,
        Transform parent,
        List<Renderer> rimList,
        List<Material> accentList,
        OutputVariantRig rig)
    {
        Material bodyMat = CreateHardwareMaterial(HullMainFallbackColor, 0.36f, 0.62f, Color.black);
        Material darkMat = CreateHardwareMaterial(HullDarkGreyFallbackColor, 0.28f, 0.42f, Color.black);
        Material accentMat = CreateHardwareMaterial(AccentLowColor, 0.22f, 0.72f, AccentLowEmission);
        if (accentMat != null) accentList.Add(accentMat);

        rimList.Add(CreatePrimitiveNoCollider($"FallbackOut_{variantIndex}_Hull", PrimitiveType.Cylinder, parent,
            new Vector3(0f, 0.16f, 0f), Vector3.zero, new Vector3(0.36f, 0.09f, 0.36f), bodyMat));
        rimList.Add(CreatePrimitiveNoCollider($"FallbackOut_{variantIndex}_Under", PrimitiveType.Cylinder, parent,
            new Vector3(0f, 0.06f, 0f), Vector3.zero, new Vector3(0.20f, 0.04f, 0.20f), darkMat));

        Transform corePivot = CreateEmptyChild($"FallbackOut_{variantIndex}_CorePivot", parent, new Vector3(0f, 0.30f, 0f));
        rimList.Add(CreateMeshPart($"FallbackOut_{variantIndex}_Core", sharedFallbackDiamondMesh, corePivot,
            Vector3.zero, Vector3.zero, Vector3.one * 0.09f, accentMat));
        rimList.Add(CreateMeshPart($"FallbackOut_{variantIndex}_Ring", sharedFallbackTorusMesh, corePivot,
            Vector3.zero, new Vector3(24f, 0f, 0f), Vector3.one * 0.18f, accentMat));

        rig.reactorGyroPivotA = corePivot;
        rig.droneRotorPivot = corePivot;
        rig.relayHaloLowerPivot = corePivot;
        rig.vaultInnerCorePivot = corePivot;
        rig.vaultInnerCoreBaseScale = corePivot.localScale;
        rig.twinOrbitPivotA = corePivot;
    }

    private void BuildFallbackGateCradle(
        Transform parent,
        List<Renderer> rimList,
        List<Material> accentList,
        out Transform thrusterCorePivot)
    {
        Material darkMat = CreateHardwareMaterial(HullDarkGreyFallbackColor, 0.28f, 0.42f, Color.black);
        Material accentMat = CreateHardwareMaterial(AccentLowColor, 0.22f, 0.72f, AccentLowEmission);
        if (accentMat != null) accentList.Add(accentMat);

        rimList.Add(CreatePrimitiveNoCollider("FallbackCradle_Hull", PrimitiveType.Cube, parent,
            new Vector3(0f, 0.014f, 0f), Vector3.zero, new Vector3(0.42f, 0.032f, 0.30f), darkMat));

        thrusterCorePivot = CreateEmptyChild("FallbackCradle_Thruster", parent, new Vector3(0f, -0.015f, 0f));
        rimList.Add(CreateMeshPart("FallbackCradle_Ring", sharedFallbackTorusMesh, thrusterCorePivot,
            Vector3.zero, Vector3.zero, Vector3.one * 0.095f, accentMat));
    }

    private static Transform CreateEmptyChild(string name, Transform parent, Vector3 localPos)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPos;
        obj.transform.localRotation = Quaternion.identity;
        obj.transform.localScale = Vector3.one;
        return obj.transform;
    }

    private static Renderer CreatePrimitiveNoCollider(
        string name,
        PrimitiveType primitiveType,
        Transform parent,
        Vector3 localPos,
        Vector3 localEuler,
        Vector3 localScale,
        Material mat)
    {
        GameObject obj = GameObject.CreatePrimitive(primitiveType);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPos;
        obj.transform.localEulerAngles = localEuler;
        obj.transform.localScale = localScale;

        Collider col = obj.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }

        Renderer rend = obj.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
            if (mat != null)
            {
                rend.sharedMaterial = mat;
            }
        }
        return rend;
    }

    private static Renderer CreateMeshPart(
        string name,
        Mesh mesh,
        Transform parent,
        Vector3 localPos,
        Vector3 localEuler,
        Vector3 localScale,
        Material mat)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPos;
        obj.transform.localEulerAngles = localEuler;
        obj.transform.localScale = localScale;

        MeshFilter mf = obj.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        MeshRenderer mr = obj.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        if (mat != null)
        {
            mr.sharedMaterial = mat;
        }
        return mr;
    }

    private static Mesh BuildFlatAnnularRingMesh(string meshName, int segments, float innerRadius, float outerRadius)
    {
        Mesh mesh = new Mesh { name = meshName };
        Vector3[] vertices = new Vector3[segments * 2];
        Vector3[] normals = new Vector3[segments * 2];
        Vector2[] uvs = new Vector2[segments * 2];
        int[] tris = new int[segments * 6];

        for (int i = 0; i < segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            float c = Mathf.Cos(angle);
            float s = Mathf.Sin(angle);
            int vi = i * 2;
            vertices[vi] = new Vector3(c * innerRadius, 0f, s * innerRadius);
            vertices[vi + 1] = new Vector3(c * outerRadius, 0f, s * outerRadius);
            normals[vi] = Vector3.down;
            normals[vi + 1] = Vector3.down;
            float u = i / (float)segments;
            uvs[vi] = new Vector2(u, 0f);
            uvs[vi + 1] = new Vector2(u, 1f);

            int next = ((i + 1) % segments) * 2;
            int ti = i * 6;
            tris[ti] = vi;
            tris[ti + 1] = vi + 1;
            tris[ti + 2] = next + 1;
            tris[ti + 3] = vi;
            tris[ti + 4] = next + 1;
            tris[ti + 5] = next;
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildSimpleTorusMesh(string meshName, int majorSeg, int minorSeg, float majorR, float minorR)
    {
        Mesh mesh = new Mesh { name = meshName };
        int vCount = majorSeg * minorSeg;
        Vector3[] verts = new Vector3[vCount];
        Vector3[] norms = new Vector3[vCount];
        int[] tris = new int[vCount * 6];

        for (int i = 0; i < majorSeg; i++)
        {
            float u = (i / (float)majorSeg) * Mathf.PI * 2f;
            float cu = Mathf.Cos(u);
            float su = Mathf.Sin(u);
            for (int j = 0; j < minorSeg; j++)
            {
                float v = (j / (float)minorSeg) * Mathf.PI * 2f;
                float cv = Mathf.Cos(v);
                float sv = Mathf.Sin(v);
                int idx = (i * minorSeg) + j;
                float rr = majorR + (minorR * cv);
                verts[idx] = new Vector3(rr * cu, minorR * sv, rr * su);
                norms[idx] = new Vector3(cv * cu, sv, cv * su).normalized;

                int iNext = (i + 1) % majorSeg;
                int jNext = (j + 1) % minorSeg;
                int a = idx;
                int b = (i * minorSeg) + jNext;
                int c = (iNext * minorSeg) + jNext;
                int d = (iNext * minorSeg) + j;
                int ti = idx * 6;
                tris[ti] = a;
                tris[ti + 1] = b;
                tris[ti + 2] = c;
                tris[ti + 3] = a;
                tris[ti + 4] = c;
                tris[ti + 5] = d;
            }
        }

        mesh.vertices = verts;
        mesh.normals = norms;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildSimpleDiamondMesh(string meshName)
    {
        Mesh mesh = new Mesh { name = meshName };
        Vector3[] verts =
        {
            new Vector3(0f, 1.35f, 0f),
            new Vector3(0.82f, 0f, 0f),
            new Vector3(0f, 0f, 0.82f),
            new Vector3(-0.82f, 0f, 0f),
            new Vector3(0f, 0f, -0.82f),
            new Vector3(0f, -1.35f, 0f)
        };
        int[] tris =
        {
            0, 2, 1, 0, 3, 2, 0, 4, 3, 0, 1, 4,
            5, 1, 2, 5, 2, 3, 5, 3, 4, 5, 4, 1
        };
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void DestroyMesh(ref Mesh mesh)
    {
        if (mesh != null)
        {
            Destroy(mesh);
            mesh = null;
        }
    }
}
