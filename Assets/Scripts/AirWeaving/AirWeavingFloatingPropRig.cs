using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Pengendali Prop Industrial Precision Hardware 3D khusus Mode Udara
/// (GateWeaver AR v8.5 Tahap 4 -- Smoked Optical Glass + Brushed Titanium, OpenGLES3, 60 FPS Zero-GC):
/// - 01 Input Switch A &amp; B (Machined Actuator Pod): plinth grafit + bodi chamfered titanium + ceruk lingkaran
///   grafit + tuas mekanis berujung SignalOrange (-28 deg saat 0 vs +28 deg saat 1) + strip LED bibir depan
///   (SignalTeal saat 1, SignalSlate saat 0), tanpa cincin neon lantai.
/// - 02 Modul Logic Gate (AND, OR, NOT, XOR, NAND -- Industrial IC Cartridge): cangkang luar chamfered titanium +
///   jendela inti kaca asap gelap + pin logam pendek di sisi + soket port fisik di tepi.
/// - 03 Output Vault (Mechanical Iris Lock): plinth fisik titanium-grafit + silinder beveled bertingkat +
///   6 bilah iris mekanis di ceruk atas (menutup rapat + titik LED SignalRed saat 0; berputar mundur halus
///   membuka inti cahaya PassEmerald saat 1).
/// - Proteksi dua arah SetActive(false): 0% regresi pada Mode Kartu Vuforia v5.
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

    private const float InputLeverAngleDeg = 28.0f;
    private const int IrisBladeCount = 6;
    private const float IrisPivotRadiusClosed = 0.114f;
    private const float IrisPivotRadiusOpen = 0.132f;
    private const float IrisOpenRotateYawDeg = 52.0f;

    private static readonly int ColorPropId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorPropId = Shader.PropertyToID("_EmissionColor");
    private static readonly int RimColorPropId = Shader.PropertyToID("_RimColor");
    private static readonly int RimPowerPropId = Shader.PropertyToID("_RimPower");
    private static readonly int RimIntensityPropId = Shader.PropertyToID("_RimIntensity");

    // Palet fungsional mikro 10% (sRGB untuk Material.SetColor)
    private static readonly Color LedTealBase = GwIndustrial3DKit.ColorSignalTeal;
    private static readonly Color LedTealEmission = new Color(0.22f, 0.74f, 0.97f, 1.00f) * 0.88f;

    private static readonly Color LedSlateBase = GwIndustrial3DKit.ColorSignalSlate;
    private static readonly Color LedSlateEmission = new Color(0.18f, 0.22f, 0.28f, 1.00f) * 0.16f;

    private static readonly Color LedOrangeBase = GwIndustrial3DKit.ColorSignalOrange;
    private static readonly Color LedOrangeEmission = new Color(0.98f, 0.45f, 0.09f, 1.00f) * 0.72f;

    private static readonly Color LedRedBase = GwIndustrial3DKit.ColorSignalRed;
    private static readonly Color LedRedEmission = new Color(0.94f, 0.27f, 0.27f, 1.00f) * 0.82f;

    private static readonly Color LedEmeraldBase = GwIndustrial3DKit.ColorPassEmerald;
    private static readonly Color LedEmeraldEmission = new Color(0.06f, 0.73f, 0.51f, 1.00f) * 0.95f;

    private static readonly Color VaultCoreLockedBase = new Color(0.09f, 0.11f, 0.15f, 1.00f);
    private static readonly Color VaultCoreLockedEmission = new Color(0.05f, 0.06f, 0.08f, 1.00f) * 0.10f;

    private sealed class InputPropSlot
    {
        public GateNode node;
        public bool isInputB;
        public GameObject rootObj;
        public Transform bounceRoot;
        public Transform leverPivot;
        public Material frontLedMat;
        public Renderer[] rimRenderers;
        public float currentLeverAngle;
        public int lastBinaryState = -1;
        public bool hasRimState;
        public Color lastRimColor;
        public float lastRimPower;
        public float lastRimIntensity;
    }

    private sealed class OutputPropSlot
    {
        public GateNode node;
        public GameObject rootObj;
        public Transform bounceRoot;
        public Transform[] irisBladeHolders;
        public Transform[] irisBladePivots;
        public Transform innerCoreTransform;
        public Material frontLedMat;
        public Material innerCoreMat;
        public Renderer[] rimRenderers;
        public float irisOpenProgress;
        public int activeVariantIndex = -1;
        public int lastBinaryState = -1;
        public bool hasRimState;
        public Color lastRimColor;
        public float lastRimPower;
        public float lastRimIntensity;
    }

    private sealed class GateCartridgeSlot
    {
        public GateNode node;
        public GameObject rootObj;
        public Transform bounceRoot;
        public Material frontLedMat;
        public Material coreTraceMat;
        public Renderer[] rimRenderers;
        public int lastVisualStateKey = int.MinValue;
        public bool hasRimState;
        public Color lastRimColor;
        public float lastRimPower;
        public float lastRimIntensity;
    }

    private readonly InputPropSlot[] inputSlots = new InputPropSlot[2];
    private OutputPropSlot outputSlot;
    private readonly List<GateCartridgeSlot> gateCartridgeSlots = new List<GateCartridgeSlot>(5);

    private readonly List<Material> ownedMaterials = new List<Material>(32);
    private readonly List<GameObject> ownedRootObjects = new List<GameObject>(8);
    private readonly List<Renderer> tempRendererList = new List<Renderer>(24);

    private MaterialPropertyBlock sharedRimMpb;
    private bool isRigActive = false;
    private bool isInitialized = false;

    public bool IsRigActive => isRigActive;

    /// <summary>
    /// Mengaktifkan seluruh prop industrial v8.5 Mode Udara (Input Switch A/B, 5 Logic Gate IC Cartridges,
    /// &amp; Output Vault Mechanical Iris Lock), menyupresi bodi v5 lewat SetActive(false), &amp; mengisolasi material.
    /// </summary>
    public void ActivateForAirWeaving(
        GateNode inputANode,
        GateNode inputBNode,
        GateNode outputNode,
        List<GateNode> logicGateNodes)
    {
        GwIndustrial3DKit.EnsureInitialized();
        if (sharedRimMpb == null)
        {
            sharedRimMpb = new MaterialPropertyBlock();
        }

        if (!isInitialized)
        {
            BuildInputSlot(0, inputANode, isInputB: false);
            BuildInputSlot(1, inputBNode, isInputB: true);
            BuildOutputSlot(outputNode);
            BuildGateCartridgeSlots(logicGateNodes);
            isInitialized = true;
        }
        else
        {
            RebindMissingSlotsIfNeeded(inputANode, inputBNode, outputNode, logicGateNodes);
        }

        // 01 -- Input Switch A & B
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
            ApplyInputBinaryState(slot, slot.node.currentOutputValue, instantLever: true);
        }

        // 03 -- Output Vault (Mechanical Iris Lock)
        if (outputSlot != null && outputSlot.node != null && outputSlot.rootObj != null)
        {
            if (!outputSlot.rootObj.activeSelf)
            {
                outputSlot.rootObj.SetActive(true);
            }
            outputSlot.node.SetExternalBodyVisualSuppressed(true, outputSlot.bounceRoot, outputSlot.rootObj.transform);
            outputSlot.activeVariantIndex = outputSlot.node.ActiveDioramaVariantIndex;
            outputSlot.lastBinaryState = -1;
            outputSlot.hasRimState = false;
            ApplyOutputBinaryState(outputSlot.node.currentOutputValue, instantIris: true);
        }

        // 02 -- 5 Modul Logic Gate (Industrial IC Cartridge)
        for (int i = 0; i < gateCartridgeSlots.Count; i++)
        {
            GateCartridgeSlot slot = gateCartridgeSlots[i];
            if (slot == null || slot.node == null || slot.rootObj == null)
            {
                continue;
            }

            if (!slot.rootObj.activeSelf)
            {
                slot.rootObj.SetActive(true);
            }
            slot.node.SetExternalBodyVisualSuppressed(true, slot.bounceRoot, slot.rootObj.transform);
            slot.lastVisualStateKey = int.MinValue;
            slot.hasRimState = false;
            ApplyGateCartridgeVisualState(slot, isGrabbed: false, isInDeleteZone: false);
        }

        isRigActive = true;
    }

    /// <summary>
    /// Menyinkronkan state Output Vault segera saat pindah level / masuk Sandbox di Mode Udara. Zero-GC.
    /// </summary>
    public void SyncLevelVariantImmediate()
    {
        if (!isRigActive || outputSlot == null || outputSlot.node == null)
        {
            return;
        }

        outputSlot.activeVariantIndex = outputSlot.node.ActiveDioramaVariantIndex;
        outputSlot.lastBinaryState = -1;
        outputSlot.hasRimState = false;
        ApplyOutputBinaryState(outputSlot.node.currentOutputValue, instantIris: false);
    }

    /// <summary>
    /// Memperbarui animasi mekanis tuas Input (-28/+28 deg), 6 bilah Iris Output Vault, dan LED status. 100% Zero-GC.
    /// </summary>
    public void TickProps(float dt, GateNode grabbedGateNode = null, bool isGrabbedGateInDeleteZone = false)
    {
        if (!isRigActive)
        {
            return;
        }

        float safeDt = Mathf.Clamp(dt, 0f, 0.08f);
        float timeNow = Time.unscaledTime;

        // 01. Input Switch A & B (Machined Actuator Pod)
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
                ApplyInputBinaryState(slot, isHigh, instantLever: false);
            }

            if (slot.leverPivot != null)
            {
                float targetAngle = isHigh ? InputLeverAngleDeg : -InputLeverAngleDeg;
                float smooth = 1f - Mathf.Exp(-18.0f * safeDt);
                slot.currentLeverAngle = Mathf.Lerp(slot.currentLeverAngle, targetAngle, smooth);
                // Miring fisik tegas -28 deg (0) vs +28 deg (1)
                slot.leverPivot.localRotation = Quaternion.Euler(0f, 0f, -slot.currentLeverAngle);
            }
        }

        // 03. Output Vault (Mechanical Iris Lock)
        if (outputSlot != null && outputSlot.node != null && outputSlot.rootObj != null)
        {
            bool outHigh = outputSlot.node.currentOutputValue;
            int outBit = outHigh ? 1 : 0;
            if (outBit != outputSlot.lastBinaryState)
            {
                ApplyOutputBinaryState(outHigh, instantIris: false);
            }

            float targetOpen = outHigh ? 1f : 0f;
            float irisSmooth = 1f - Mathf.Exp(-11.5f * safeDt);
            outputSlot.irisOpenProgress = Mathf.Lerp(outputSlot.irisOpenProgress, targetOpen, irisSmooth);
            UpdateIrisBladesPose(outputSlot.irisOpenProgress);

            if (outputSlot.innerCoreTransform != null)
            {
                float pulse = outHigh ? (1.0f + (0.04f * Mathf.Sin(timeNow * 5.2f))) : 0.96f;
                outputSlot.innerCoreTransform.localScale = new Vector3(0.215f * pulse, 0.012f, 0.215f * pulse);
            }
        }

        // 02. 5 Modul Logic Gate (Industrial IC Cartridge)
        for (int i = 0; i < gateCartridgeSlots.Count; i++)
        {
            GateCartridgeSlot slot = gateCartridgeSlots[i];
            if (slot == null || slot.node == null || slot.rootObj == null)
            {
                continue;
            }

            bool isGrabbed = slot.node == grabbedGateNode;
            bool inDelete = isGrabbed && isGrabbedGateInDeleteZone;
            ApplyGateCartridgeVisualState(slot, isGrabbed, inDelete);
        }
    }

    /// <summary>
    /// Menerapkan Fresnel Rim Highlight (Linear eksplisit dari AirWeavingLightingAndShaderRig) ke prop
    /// milik node terkait via MaterialPropertyBlock tunggal (100% Zero-GC).
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
            ApplyRimToRenderersIfDirty(
                outputSlot.rimRenderers,
                linearRimColor,
                rimPower,
                rimIntensity,
                ref outputSlot.hasRimState,
                ref outputSlot.lastRimColor,
                ref outputSlot.lastRimPower,
                ref outputSlot.lastRimIntensity);
            return;
        }

        for (int i = 0; i < gateCartridgeSlots.Count; i++)
        {
            GateCartridgeSlot slot = gateCartridgeSlots[i];
            if (slot != null && slot.node == node)
            {
                ApplyRimToRenderersIfDirty(
                    slot.rimRenderers,
                    linearRimColor,
                    rimPower,
                    rimIntensity * 0.85f,
                    ref slot.hasRimState,
                    ref slot.lastRimColor,
                    ref slot.lastRimPower,
                    ref slot.lastRimIntensity);
                return;
            }
        }
    }

    /// <summary>
    /// Menonaktifkan seluruh prop Mode Udara via SetActive(false) dan memulihkan 100% bodi Mode Kartu v5.
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
            ClearRimPropertyBlocks(outputSlot.rimRenderers);
            outputSlot.hasRimState = false;
            if (outputSlot.node != null)
            {
                outputSlot.node.SetExternalBodyVisualSuppressed(false, null, null);
            }
        }

        for (int i = 0; i < gateCartridgeSlots.Count; i++)
        {
            GateCartridgeSlot slot = gateCartridgeSlots[i];
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

        inputSlots[0] = null;
        inputSlots[1] = null;
        outputSlot = null;
        gateCartridgeSlots.Clear();
        sharedRimMpb = null;
        isInitialized = false;
    }

    // =========================================================================
    // BUILDERS: 01 INPUT SWITCH, 02 GATE IC CARTRIDGE, 03 OUTPUT IRIS VAULT
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
        if (gateCartridgeSlots.Count == 0 && logicGateNodes != null && logicGateNodes.Count > 0)
        {
            BuildGateCartridgeSlots(logicGateNodes);
        }
    }

    /// <summary>
    /// 01 -- Input Switch A &amp; B (Machined Actuator Pod):
    /// Cakram alas plinth grafit + bodi kotak chamfered titanium + ceruk lingkaran grafit di tengah +
    /// tuas mekanis berujung SignalOrange (-28 deg saat 0 vs +28 deg saat 1) + strip LED tipis di bibir depan.
    /// </summary>
    private void BuildInputSlot(int slotIndex, GateNode node, bool isInputB)
    {
        if (node == null)
        {
            return;
        }

        GameObject rootObj = CreateFloatingPropRoot(node.transform);
        Transform bounceRoot = CreateEmptyChild("InputSwitch_BounceRoot", rootObj.transform, Vector3.zero);
        tempRendererList.Clear();

        Mesh chamferBox = GwIndustrial3DKit.GetChamferedBoxMesh();
        Mesh bevelCyl = GwIndustrial3DKit.GetBeveledCylinderMesh();
        Material titaniumMat = GwIndustrial3DKit.Mat_BrushedTitanium;
        Material graphiteMat = GwIndustrial3DKit.Mat_AnodizedGraphite;
        Material orangeMat = GwIndustrial3DKit.Mat_SignalOrange;

        // 1. Cakram alas plinth grafit
        tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
            "Input_GraphitePlinthDisc",
            bevelCyl,
            bounceRoot,
            new Vector3(0f, 0.022f, 0f),
            Vector3.zero,
            new Vector3(0.38f, 0.028f, 0.38f),
            graphiteMat));

        // 2. Bodi kotak chamfered Brushed Titanium
        tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
            "Input_TitaniumChassis",
            chamferBox,
            bounceRoot,
            new Vector3(0f, 0.086f, 0f),
            Vector3.zero,
            new Vector3(0.34f, 0.104f, 0.32f),
            titaniumMat));

        // 3. Soket port output fisik di sisi kanan (+0.27f adalah posisi orb Port_Out)
        tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
            "Input_PortOutSocketBoss",
            chamferBox,
            bounceRoot,
            new Vector3(0.205f, 0.056f, 0f),
            Vector3.zero,
            new Vector3(0.11f, 0.050f, 0.092f),
            graphiteMat));

        // 4. Collar titanium & ceruk lingkaran grafit di tengah atas
        tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
            "Input_TurretCollar",
            bevelCyl,
            bounceRoot,
            new Vector3(0f, 0.142f, 0f),
            Vector3.zero,
            new Vector3(0.225f, 0.016f, 0.225f),
            titaniumMat));

        tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
            "Input_CenterGraphiteRecess",
            bevelCyl,
            bounceRoot,
            new Vector3(0f, 0.146f, 0f),
            Vector3.zero,
            new Vector3(0.186f, 0.018f, 0.186f),
            graphiteMat));

        // 5. Poros & Tuas Mekanis berujung SignalOrange (-28 deg vs +28 deg)
        tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
            "Input_ActuatorAxleDrum",
            chamferBox,
            bounceRoot,
            new Vector3(0f, 0.154f, 0f),
            Vector3.zero,
            new Vector3(0.068f, 0.024f, 0.096f),
            graphiteMat));

        Transform leverPivot = CreateEmptyChild("Input_LeverPivot", bounceRoot, new Vector3(0f, 0.155f, 0f));

        tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
            "Input_LeverStemTitanium",
            chamferBox,
            leverPivot,
            new Vector3(0f, 0.048f, 0f),
            Vector3.zero,
            new Vector3(0.036f, 0.092f, 0.046f),
            titaniumMat));

        tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
            "Input_LeverTipOrange",
            chamferBox,
            leverPivot,
            new Vector3(0f, 0.102f, 0f),
            Vector3.zero,
            new Vector3(0.054f, 0.038f, 0.118f),
            orangeMat));

        // 6. Strip LED tipis di bibir depan (SignalTeal saat 1, SignalSlate saat 0)
        tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
            "Input_FrontLedBezel",
            chamferBox,
            bounceRoot,
            new Vector3(0f, 0.084f, -0.158f),
            Vector3.zero,
            new Vector3(0.186f, 0.026f, 0.014f),
            graphiteMat));

        Material frontLedMat = GwIndustrial3DKit.CreateDedicatedReactiveMaterial(
            isInputB ? "Mat_InputB_FrontLed" : "Mat_InputA_FrontLed",
            LedSlateBase,
            LedSlateEmission,
            0.14f,
            0.54f);
        if (frontLedMat != null)
        {
            ownedMaterials.Add(frontLedMat);
        }

        GwIndustrial3DKit.CreateMeshPart(
            "Input_FrontLedStrip",
            chamferBox,
            bounceRoot,
            new Vector3(0f, 0.084f, -0.163f),
            Vector3.zero,
            new Vector3(0.168f, 0.015f, 0.010f),
            frontLedMat);

        float initialAngle = node.currentOutputValue ? InputLeverAngleDeg : -InputLeverAngleDeg;
        leverPivot.localRotation = Quaternion.Euler(0f, 0f, -initialAngle);

        InputPropSlot slot = new InputPropSlot
        {
            node = node,
            isInputB = isInputB,
            rootObj = rootObj,
            bounceRoot = bounceRoot,
            leverPivot = leverPivot,
            frontLedMat = frontLedMat,
            rimRenderers = tempRendererList.ToArray(),
            currentLeverAngle = initialAngle
        };

        rootObj.SetActive(false);
        inputSlots[slotIndex] = slot;
    }

    /// <summary>
    /// 02 -- Modul Logic Gate (AND, OR, NOT, XOR, NAND -- Industrial IC Cartridge):
    /// Cangkang luar chamfered titanium + jendela inti kaca asap gelap di tengah +
    /// pin logam pendek di sisi kiri/kanan + soket port fisik di tepi.
    /// </summary>
    private void BuildGateCartridgeSlots(List<GateNode> logicGateNodes)
    {
        gateCartridgeSlots.Clear();
        if (logicGateNodes == null)
        {
            return;
        }

        Mesh chamferBox = GwIndustrial3DKit.GetChamferedBoxMesh();
        Material titaniumMat = GwIndustrial3DKit.Mat_BrushedTitanium;
        Material graphiteMat = GwIndustrial3DKit.Mat_AnodizedGraphite;
        Material smokedCoreMat = GwIndustrial3DKit.Mat_SmokedGlassCore;

        for (int i = 0; i < logicGateNodes.Count; i++)
        {
            GateNode node = logicGateNodes[i];
            if (node == null || !IsLogicGate(node.nodeType))
            {
                continue;
            }

            GameObject rootObj = CreateFloatingPropRoot(node.transform);
            Transform bounceRoot = CreateEmptyChild("GateCartridge_BounceRoot", rootObj.transform, Vector3.zero);
            tempRendererList.Clear();

            // 1. Plinth dasar Anodized Graphite
            tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
                "GateIC_GraphitePlinth",
                chamferBox,
                bounceRoot,
                new Vector3(0f, 0.024f, 0f),
                Vector3.zero,
                new Vector3(0.41f, 0.028f, 0.29f),
                graphiteMat));

            // 2. Cangkang luar chamfered Brushed Titanium
            tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
                "GateIC_TitaniumShell",
                chamferBox,
                bounceRoot,
                new Vector3(0f, 0.084f, 0f),
                Vector3.zero,
                new Vector3(0.43f, 0.094f, 0.31f),
                titaniumMat));

            // 3. Ceruk tengah grafit + jalur konduktor internal + jendela inti kaca asap gelap
            tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
                "GateIC_CoreRecessWell",
                chamferBox,
                bounceRoot,
                new Vector3(0f, 0.132f, 0f),
                Vector3.zero,
                new Vector3(0.296f, 0.014f, 0.206f),
                graphiteMat));

            Material coreTraceMat = GwIndustrial3DKit.CreateDedicatedReactiveMaterial(
                $"Mat_GateIC_{node.nodeType}_CoreTrace",
                LedSlateBase,
                LedSlateEmission,
                0.20f,
                0.60f);
            if (coreTraceMat != null)
            {
                ownedMaterials.Add(coreTraceMat);
            }

            GwIndustrial3DKit.CreateMeshPart(
                "GateIC_InternalBusBar",
                chamferBox,
                bounceRoot,
                new Vector3(0f, 0.138f, 0f),
                Vector3.zero,
                new Vector3(0.23f, 0.008f, 0.042f),
                coreTraceMat);

            GwIndustrial3DKit.CreateMeshPart(
                "GateIC_SmokedOpticalGlassWindow",
                chamferBox,
                bounceRoot,
                new Vector3(0f, 0.144f, 0f),
                Vector3.zero,
                new Vector3(0.286f, 0.014f, 0.196f),
                smokedCoreMat);

            // 4. Pin IC logam pendek di sisi depan/belakang & kiri/kanan
            for (int p = 0; p < 4; p++)
            {
                float pinX = -0.135f + (p * 0.090f);
                tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
                    $"GateIC_PinFront_{p}",
                    chamferBox,
                    bounceRoot,
                    new Vector3(pinX, 0.036f, -0.160f),
                    Vector3.zero,
                    new Vector3(0.030f, 0.022f, 0.024f),
                    titaniumMat));

                tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
                    $"GateIC_PinBack_{p}",
                    chamferBox,
                    bounceRoot,
                    new Vector3(pinX, 0.036f, 0.160f),
                    Vector3.zero,
                    new Vector3(0.030f, 0.022f, 0.024f),
                    titaniumMat));
            }

            // 5. Soket port fisik di tepi kiri (Port_In) dan kanan (Port_Out)
            bool isSingleInput = (node.nodeType == NodeType.NOT);
            if (isSingleInput)
            {
                BuildPhysicalPortSocketBoss(bounceRoot, chamferBox, graphiteMat, titaniumMat,
                    new Vector3(-0.232f, 0.056f, 0f), "GateIC_SocketInA");
            }
            else
            {
                BuildPhysicalPortSocketBoss(bounceRoot, chamferBox, graphiteMat, titaniumMat,
                    new Vector3(-0.232f, 0.056f, 0.10f), "GateIC_SocketInA");
                BuildPhysicalPortSocketBoss(bounceRoot, chamferBox, graphiteMat, titaniumMat,
                    new Vector3(-0.232f, 0.056f, -0.10f), "GateIC_SocketInB");

                // Pin IC tengah di sisi kiri di antara 2 soket input
                tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
                    "GateIC_PinLeftMid",
                    chamferBox,
                    bounceRoot,
                    new Vector3(-0.222f, 0.056f, 0f),
                    Vector3.zero,
                    new Vector3(0.026f, 0.024f, 0.036f),
                    titaniumMat));
            }

            BuildPhysicalPortSocketBoss(bounceRoot, chamferBox, graphiteMat, titaniumMat,
                new Vector3(0.232f, 0.056f, 0f), "GateIC_SocketOut");

            // Pin IC pengapit di sisi kanan soket output
            tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
                "GateIC_PinRightTop",
                chamferBox,
                bounceRoot,
                new Vector3(0.222f, 0.056f, 0.095f),
                Vector3.zero,
                new Vector3(0.026f, 0.024f, 0.032f),
                titaniumMat));
            tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
                "GateIC_PinRightBot",
                chamferBox,
                bounceRoot,
                new Vector3(0.222f, 0.056f, -0.095f),
                Vector3.zero,
                new Vector3(0.026f, 0.024f, 0.032f),
                titaniumMat));

            // 6. Strip LED tipis di bibir depan kartrid
            tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
                "GateIC_FrontLedBezel",
                chamferBox,
                bounceRoot,
                new Vector3(0f, 0.084f, -0.154f),
                Vector3.zero,
                new Vector3(0.184f, 0.024f, 0.012f),
                graphiteMat));

            Material frontLedMat = GwIndustrial3DKit.CreateDedicatedReactiveMaterial(
                $"Mat_GateIC_{node.nodeType}_FrontLed",
                LedSlateBase,
                LedSlateEmission,
                0.14f,
                0.54f);
            if (frontLedMat != null)
            {
                ownedMaterials.Add(frontLedMat);
            }

            GwIndustrial3DKit.CreateMeshPart(
                "GateIC_FrontLedStrip",
                chamferBox,
                bounceRoot,
                new Vector3(0f, 0.084f, -0.158f),
                Vector3.zero,
                new Vector3(0.166f, 0.014f, 0.009f),
                frontLedMat);

            rootObj.SetActive(false);
            gateCartridgeSlots.Add(new GateCartridgeSlot
            {
                node = node,
                rootObj = rootObj,
                bounceRoot = bounceRoot,
                frontLedMat = frontLedMat,
                coreTraceMat = coreTraceMat,
                rimRenderers = tempRendererList.ToArray()
            });
        }
    }

    private void BuildPhysicalPortSocketBoss(
        Transform parent,
        Mesh chamferBox,
        Material graphiteMat,
        Material titaniumMat,
        Vector3 localPos,
        string prefixName)
    {
        tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
            prefixName + "_Housing",
            chamferBox,
            parent,
            localPos,
            Vector3.zero,
            new Vector3(0.058f, 0.048f, 0.064f),
            graphiteMat));

        tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
            prefixName + "_PinCollar",
            chamferBox,
            parent,
            localPos + new Vector3(localPos.x < 0f ? -0.022f : 0.022f, 0f, 0f),
            Vector3.zero,
            new Vector3(0.024f, 0.034f, 0.044f),
            titaniumMat));
    }

    /// <summary>
    /// 03 -- Output Vault (Mechanical Iris Lock):
    /// Plinth fisik titanium-grafit + silinder beveled bertingkat (titanium + cincin alur grafit) +
    /// 6 bilah iris mekanis di ceruk atas + titik LED bibir depan (SignalRed saat 0, PassEmerald saat 1).
    /// </summary>
    private void BuildOutputSlot(GateNode node)
    {
        if (node == null)
        {
            return;
        }

        GameObject rootObj = CreateFloatingPropRoot(node.transform);
        Transform bounceRoot = CreateEmptyChild("OutputVault_BounceRoot", rootObj.transform, Vector3.zero);
        tempRendererList.Clear();

        Mesh chamferBox = GwIndustrial3DKit.GetChamferedBoxMesh();
        Mesh bevelCyl = GwIndustrial3DKit.GetBeveledCylinderMesh();
        Mesh irisBladeMesh = GwIndustrial3DKit.GetIrisBladeMesh();
        Material titaniumMat = GwIndustrial3DKit.Mat_BrushedTitanium;
        Material graphiteMat = GwIndustrial3DKit.Mat_AnodizedGraphite;

        // 1. Plinth fisik titanium-grafit (menggantikan cincin lantai hijau/biru)
        tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
            "Vault_PlinthTitaniumBase",
            chamferBox,
            bounceRoot,
            new Vector3(0f, 0.020f, 0f),
            Vector3.zero,
            new Vector3(0.43f, 0.024f, 0.43f),
            titaniumMat));

        tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
            "Vault_PlinthGraphiteStep",
            bevelCyl,
            bounceRoot,
            new Vector3(0f, 0.036f, 0f),
            Vector3.zero,
            new Vector3(0.415f, 0.022f, 0.415f),
            graphiteMat));

        // Soket port input fisik di sisi kiri (-0.27f adalah posisi orb Port_InA)
        BuildPhysicalPortSocketBoss(bounceRoot, chamferBox, graphiteMat, titaniumMat,
            new Vector3(-0.228f, 0.056f, 0f), "Vault_SocketIn");

        // 2. Silinder beveled bertingkat (titanium + cincin alur grafit)
        tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
            "Vault_LowerTitaniumCylinder",
            bevelCyl,
            bounceRoot,
            new Vector3(0f, 0.076f, 0f),
            Vector3.zero,
            new Vector3(0.386f, 0.064f, 0.386f),
            titaniumMat));

        tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
            "Vault_MidGraphiteGrooveRing",
            bevelCyl,
            bounceRoot,
            new Vector3(0f, 0.118f, 0f),
            Vector3.zero,
            new Vector3(0.352f, 0.028f, 0.352f),
            graphiteMat));

        tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
            "Vault_UpperTitaniumCrown",
            bevelCyl,
            bounceRoot,
            new Vector3(0f, 0.152f, 0f),
            Vector3.zero,
            new Vector3(0.376f, 0.044f, 0.376f),
            titaniumMat));

        // 3. Ceruk atas grafit & inti cahaya PassEmerald
        tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
            "Vault_TopApertureRecess",
            bevelCyl,
            bounceRoot,
            new Vector3(0f, 0.172f, 0f),
            Vector3.zero,
            new Vector3(0.282f, 0.012f, 0.282f),
            graphiteMat));

        Material innerCoreMat = GwIndustrial3DKit.CreateDedicatedReactiveMaterial(
            "Mat_OutputVault_InnerCore",
            VaultCoreLockedBase,
            VaultCoreLockedEmission,
            0.12f,
            0.66f);
        if (innerCoreMat != null)
        {
            ownedMaterials.Add(innerCoreMat);
        }

        Renderer coreRend = GwIndustrial3DKit.CreateMeshPart(
            "Vault_EmeraldCoreDisc",
            bevelCyl,
            bounceRoot,
            new Vector3(0f, 0.176f, 0f),
            Vector3.zero,
            new Vector3(0.215f, 0.012f, 0.215f),
            innerCoreMat);
        Transform innerCoreTransform = coreRend != null ? coreRend.transform : null;

        // 4. 6 Bilah Iris Mekanis di ceruk atas
        Transform irisGroup = CreateEmptyChild("Vault_IrisMechanismGroup", bounceRoot, new Vector3(0f, 0.181f, 0f));
        Transform[] bladeHolders = new Transform[IrisBladeCount];
        Transform[] bladePivots = new Transform[IrisBladeCount];

        for (int b = 0; b < IrisBladeCount; b++)
        {
            float sectorAngleDeg = b * (360f / IrisBladeCount);
            Transform sectorHolder = CreateEmptyChild($"Vault_IrisSector_{b}", irisGroup, Vector3.zero);
            sectorHolder.localRotation = Quaternion.Euler(0f, sectorAngleDeg, 0f);
            bladeHolders[b] = sectorHolder;

            // Poros engsel tiap bilah di tepi luar ceruk; ujung bilah (-Z lokal) mengarah ke pusat (0,0,0)
            Transform bladePivot = CreateEmptyChild($"Vault_IrisBladePivot_{b}", sectorHolder, new Vector3(0f, 0f, IrisPivotRadiusClosed));
            bladePivots[b] = bladePivot;

            // Sedikit overlap vertikal bertingkat (0.0012f) agar 6 bilah bebas z-fighting saat menutup/membuka
            float yOffset = (b - 2.5f) * 0.0011f;
            tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
                $"Vault_IrisBladeMesh_{b}",
                irisBladeMesh,
                bladePivot,
                new Vector3(0f, yOffset, 0f),
                Vector3.zero,
                new Vector3(0.118f, 0.011f, 0.116f),
                graphiteMat));
        }

        // 5. 1 Titik LED di bibir depan (SignalRed saat 0 Terkunci, PassEmerald saat 1 Terbuka)
        tempRendererList.Add(GwIndustrial3DKit.CreateMeshPart(
            "Vault_FrontLedBezel",
            chamferBox,
            bounceRoot,
            new Vector3(0f, 0.118f, -0.182f),
            Vector3.zero,
            new Vector3(0.052f, 0.032f, 0.020f),
            graphiteMat));

        Material frontLedMat = GwIndustrial3DKit.CreateDedicatedReactiveMaterial(
            "Mat_OutputVault_FrontLed",
            LedRedBase,
            LedRedEmission,
            0.14f,
            0.62f);
        if (frontLedMat != null)
        {
            ownedMaterials.Add(frontLedMat);
        }

        GwIndustrial3DKit.CreateMeshPart(
            "Vault_FrontLedDot",
            bevelCyl,
            bounceRoot,
            new Vector3(0f, 0.118f, -0.191f),
            new Vector3(90f, 0f, 0f),
            new Vector3(0.024f, 0.008f, 0.024f),
            frontLedMat);

        rootObj.SetActive(false);
        outputSlot = new OutputPropSlot
        {
            node = node,
            rootObj = rootObj,
            bounceRoot = bounceRoot,
            irisBladeHolders = bladeHolders,
            irisBladePivots = bladePivots,
            innerCoreTransform = innerCoreTransform,
            frontLedMat = frontLedMat,
            innerCoreMat = innerCoreMat,
            rimRenderers = tempRendererList.ToArray(),
            irisOpenProgress = node.currentOutputValue ? 1f : 0f,
            activeVariantIndex = node.ActiveDioramaVariantIndex,
            lastBinaryState = -1
        };

        UpdateIrisBladesPose(outputSlot.irisOpenProgress);
    }

    // =========================================================================
    // RUNTIME STATE SYNC & KINETIC UPDATES (100% ZERO-GC)
    // =========================================================================

    private void ApplyInputBinaryState(InputPropSlot slot, bool isHigh, bool instantLever)
    {
        slot.lastBinaryState = isHigh ? 1 : 0;
        Color ledBase = isHigh ? LedTealBase : LedSlateBase;
        Color ledEmit = isHigh ? LedTealEmission : LedSlateEmission;
        SetHardwareColorAndEmission(slot.frontLedMat, ledBase, ledEmit);

        if (instantLever && slot.leverPivot != null)
        {
            slot.currentLeverAngle = isHigh ? InputLeverAngleDeg : -InputLeverAngleDeg;
            slot.leverPivot.localRotation = Quaternion.Euler(0f, 0f, -slot.currentLeverAngle);
        }
    }

    private void ApplyOutputBinaryState(bool isHigh, bool instantIris)
    {
        if (outputSlot == null)
        {
            return;
        }

        outputSlot.lastBinaryState = isHigh ? 1 : 0;

        // Saat 0 (Terkunci): titik LED SignalRed + inti gelap. Saat 1 (Terbuka/Lulus): PassEmerald.
        Color ledBase = isHigh ? LedEmeraldBase : LedRedBase;
        Color ledEmit = isHigh ? LedEmeraldEmission : LedRedEmission;
        SetHardwareColorAndEmission(outputSlot.frontLedMat, ledBase, ledEmit);

        Color coreBase = isHigh ? LedEmeraldBase : VaultCoreLockedBase;
        Color coreEmit = isHigh ? LedEmeraldEmission : VaultCoreLockedEmission;
        SetHardwareColorAndEmission(outputSlot.innerCoreMat, coreBase, coreEmit);

        if (instantIris)
        {
            outputSlot.irisOpenProgress = isHigh ? 1f : 0f;
            UpdateIrisBladesPose(outputSlot.irisOpenProgress);
        }
    }

    private void UpdateIrisBladesPose(float open01)
    {
        if (outputSlot == null || outputSlot.irisBladePivots == null)
        {
            return;
        }

        float clamped = Mathf.Clamp01(open01);
        float pivotRadius = Mathf.Lerp(IrisPivotRadiusClosed, IrisPivotRadiusOpen, clamped);
        float yawDeg = clamped * IrisOpenRotateYawDeg;
        Quaternion bladeRot = Quaternion.Euler(0f, yawDeg, 0f);

        for (int i = 0; i < outputSlot.irisBladePivots.Length; i++)
        {
            Transform pivot = outputSlot.irisBladePivots[i];
            if (pivot == null)
            {
                continue;
            }

            pivot.localPosition = new Vector3(0f, 0f, pivotRadius);
            pivot.localRotation = bladeRot;
        }
    }

    private void ApplyGateCartridgeVisualState(GateCartridgeSlot slot, bool isGrabbed, bool isInDeleteZone)
    {
        bool isTracked = slot.node.isTracked;
        bool isHigh = isTracked && slot.node.currentOutputValue;
        int stateKey = (isTracked ? 1 : 0)
                       | (isHigh ? 2 : 0)
                       | (isGrabbed ? 4 : 0)
                       | (isInDeleteZone ? 8 : 0);

        if (stateKey == slot.lastVisualStateKey)
        {
            return;
        }

        slot.lastVisualStateKey = stateKey;

        Color ledBase;
        Color ledEmit;

        if (isInDeleteZone)
        {
            ledBase = LedRedBase;
            ledEmit = LedRedEmission;
        }
        else if (isGrabbed)
        {
            ledBase = LedOrangeBase;
            ledEmit = LedOrangeEmission;
        }
        else if (isHigh)
        {
            ledBase = LedTealBase;
            ledEmit = LedTealEmission;
        }
        else
        {
            ledBase = LedSlateBase;
            ledEmit = LedSlateEmission;
        }

        SetHardwareColorAndEmission(slot.frontLedMat, ledBase, ledEmit);
        SetHardwareColorAndEmission(slot.coreTraceMat, isHigh ? LedTealBase : LedSlateBase, isHigh ? LedTealEmission : LedSlateEmission);
    }

    // =========================================================================
    // MATERIAL & FRESNEL MPB HELPERS
    // =========================================================================

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

    private static Transform CreateEmptyChild(string name, Transform parent, Vector3 localPos)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPos;
        obj.transform.localRotation = Quaternion.identity;
        obj.transform.localScale = Vector3.one;
        return obj.transform;
    }

    private static bool IsLogicGate(NodeType type)
    {
        return type == NodeType.AND
               || type == NodeType.OR
               || type == NodeType.NOT
               || type == NodeType.XOR
               || type == NodeType.NAND;
    }
}
