using UnityEngine;

/// <summary>
/// Visual mekanisme terbuka (Open Cutaway) untuk 5 gerbang logika (AND, OR, NOT, XOR, NAND) — GateWeaver AR v7.0 Fase 1.
/// - 100% tracking-agnostic (bebas Vuforia & MediaPipe), berjalan identik di Mode Kartu & Mode Udara.
/// - Menggunakan rangka logam gelap opaque + saluran energi reaktif yang digerakkan langsung oleh inputAValue, inputBValue,
///   dan currentOutputValue, dengan orientasi tepat mengikuti Port_InA, Port_InB, dan Port_Out pada GateNode.
/// - Khusus NOT & NAND menampilkan Inti Daya Internal (Power Core) menyala konstan agar mekanisme pemutusan daya terlihat nyata.
/// - Zero-GC di runtime loop: hanya 3 shared material OpenGLES3-safe + 1 MaterialPropertyBlock pra-alokasi.
/// </summary>
public sealed class GateMechanismVisual : MonoBehaviour
{
    private const float PortInX = -0.27f;
    private const float PortOutX = 0.27f;
    private const float PortY = 0.055f;
    private const float PortInAZ = 0.10f;
    private const float PortInBZ = -0.10f;
    private const float DeckY = 0.155f;

    private static readonly int ColorPropId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorPropId = Shader.PropertyToID("_EmissionColor");
    private static readonly int GlossinessPropId = Shader.PropertyToID("_Glossiness");
    private static readonly int MetallicPropId = Shader.PropertyToID("_Metallic");
    private static readonly int RimColorPropId = Shader.PropertyToID("_RimColor");
    private static readonly int RimPowerPropId = Shader.PropertyToID("_RimPower");
    private static readonly int RimIntensityPropId = Shader.PropertyToID("_RimIntensity");

    // Palet sci-fi selaras AR_BaseTile3D, Quaternius Props, & saluran energi GateNode
    private static readonly Color MetalDarkColor = new Color(0.14f, 0.18f, 0.24f, 1f);
    private static readonly Color MetalSteelColor = new Color(0.46f, 0.54f, 0.64f, 1f);
    private static readonly Color EnergyHighBaseColor = new Color(0.16f, 1.00f, 0.78f, 1f);
    private static readonly Color EnergyHighEmission = new Color(0.14f, 0.92f, 0.72f, 1f) * 1.15f;
    private static readonly Color EnergyLowBaseColor = new Color(0.68f, 0.20f, 0.11f, 1f);
    private static readonly Color EnergyLowEmission = new Color(0.50f, 0.12f, 0.06f, 1f) * 0.45f;
    private static readonly Color EnergyUnconnectedBaseColor = new Color(0.24f, 0.30f, 0.38f, 1f);
    private static readonly Color EnergyUnconnectedEmission = new Color(0.08f, 0.11f, 0.16f, 1f);
    private static readonly Color EnergyReadyBaseColor = new Color(0.24f, 0.78f, 0.94f, 1f);
    private static readonly Color EnergyReadyEmission = new Color(0.16f, 0.60f, 0.78f, 1f) * 0.75f;
    private static readonly Color PowerCoreBaseColor = new Color(1.00f, 0.86f, 0.26f, 1f);
    private static readonly Color PowerCoreEmission = new Color(1.00f, 0.78f, 0.20f, 1f) * 1.35f;
    private static readonly Color ClashBaseColor = new Color(1.00f, 0.44f, 0.16f, 1f);
    private static readonly Color ClashEmission = new Color(1.00f, 0.36f, 0.12f, 1f) * 1.10f;
    private static readonly Color ParkedAccentBaseColor = new Color(0.26f, 0.86f, 0.98f, 1f);
    private static readonly Color ParkedAccentEmission = new Color(0.18f, 0.66f, 0.82f, 1f) * 0.65f;

    private NodeType gateType = NodeType.AND;
    private bool isBuilt = false;

    private Material metalDarkMat;
    private Material metalSteelMat;
    private Material emissiveSharedMat;
    private bool emissiveHasEmissionProp = false;
    private MaterialPropertyBlock mpb;

    // Kelompok renderer yang diberi warna dinamis via MaterialPropertyBlock (zero-GC)
    private Renderer[] chassisRenderers;
    private Renderer[] inputARenderers;
    private Renderer[] inputBRenderers;
    private Renderer[] midBusRenderers;
    private Renderer[] outputRenderers;
    private Renderer[] powerCoreRenderers;
    private Renderer[] auxHighlightRenderers;

    // State external hardware shader & Fresnel Rim Glow (tracking-agnostic, Zero-GC, 0% regresi Mode Kartu v5)
    private Shader savedMetalDarkShader;
    private int savedMetalDarkQueue = -1;
    private Shader savedMetalSteelShader;
    private int savedMetalSteelQueue = -1;
    private Shader savedEmissiveShader;
    private int savedEmissiveQueue = -1;
    private bool hasExternalHardwareShader = false;
    private bool hasExternalRimHighlight = false;
    private Color externalRimColorLinear = Color.black;
    private float externalRimPower = 2.8f;
    private float externalRimIntensity = -1f;
    private bool lastInAConnected = false;
    private bool lastInAValue = false;
    private bool lastInBConnected = false;
    private bool lastInBValue = false;
    private bool lastOutputValue = false;
    private bool lastInputsComplete = false;
    private bool lastIsParkedPreview = true;

    // Transform bergerak per tipe gerbang
    private Transform animPivotA;
    private Transform animPivotB;
    private Transform animSlidePrimary;
    private Transform animSlideSecondary;
    private Transform animPulseOrbA;
    private Transform animPulseOrbB;
    private Transform animRotorPrimary;
    private Transform animRotorSecondary;

    // State animasi halus & dirty-key pewarnaan
    private float currentAngleA = 0f;
    private float currentAngleB = 0f;
    private float currentSlideA = 0f;
    private float currentSlideB = 0f;
    private float flowPhase = 0f;
    private int lastColorStateKey = int.MinValue;

    public bool IsBuilt => isBuilt;

    /// <summary>
    /// Membangun rangka & mekanisme Open Cutaway secara sinkron (dipanggil dari GateNode.Awake()).
    /// Mengembalikan tinggi puncak lokal (normalizedTopY) untuk penempatan bersih Label_Status3D.
    /// </summary>
    public void Build(
        NodeType type,
        Transform parentRoot,
        float baseSurfaceY,
        Mesh sharedTorusMesh,
        Mesh sharedDiamondMesh,
        out float normalizedTopY)
    {
        gateType = type;
        if (parentRoot == null)
        {
            normalizedTopY = baseSurfaceY + 0.34f;
            return;
        }

        EnsureSharedMaterialsInitialized();
        if (mpb == null)
        {
            mpb = new MaterialPropertyBlock();
        }

        Transform chamberRoot = CreateChild($"AR_OpenCutaway_{type}", parentRoot, Vector3.zero);
        BuildOpenGantryChassis(chamberRoot, baseSurfaceY);

        switch (type)
        {
            case NodeType.AND:
                BuildAndSeriesBridges(chamberRoot, baseSurfaceY, sharedDiamondMesh, out normalizedTopY);
                break;
            case NodeType.OR:
                BuildOrParallelLanes(chamberRoot, baseSurfaceY, sharedTorusMesh, sharedDiamondMesh, out normalizedTopY);
                break;
            case NodeType.NOT:
                BuildNotPowerCoreInverter(chamberRoot, baseSurfaceY, sharedTorusMesh, sharedDiamondMesh, out normalizedTopY);
                break;
            case NodeType.XOR:
                BuildXorBalanceComparator(chamberRoot, baseSurfaceY, sharedDiamondMesh, out normalizedTopY);
                break;
            case NodeType.NAND:
                BuildNandPowerCoreBreaker(chamberRoot, baseSurfaceY, sharedTorusMesh, sharedDiamondMesh, out normalizedTopY);
                break;
            default:
                normalizedTopY = baseSurfaceY + 0.34f;
                break;
        }

        CacheChassisRenderers(chamberRoot);
        isBuilt = true;
        lastColorStateKey = int.MinValue;
        UpdateMechanism(false, false, false, false, false, isParkedPreview: true, deltaTime: 1f);
    }

    /// <summary>
    /// Memperbarui gerak mekanis dan pancaran energi gerbang berdasarkan status real-time pin input & output.
    /// Zero-GC: tidak ada alokasi memori per frame.
    /// </summary>
    public void UpdateMechanism(
        bool isInAConnected,
        bool inAValue,
        bool isInBConnected,
        bool inBValue,
        bool outputValue,
        bool isParkedPreview,
        float deltaTime)
    {
        if (!isBuilt)
        {
            return;
        }

        float dt = Mathf.Clamp(deltaTime, 0f, 0.1f);
        float lerpAlpha = dt >= 0.099f ? 1f : (1f - Mathf.Exp(-14f * dt));
        flowPhase = Mathf.Repeat(flowPhase + (dt * (outputValue ? 1.55f : 0.45f)), 1f);

        bool inputsComplete = (gateType == NodeType.NOT)
            ? isInAConnected
            : (isInAConnected && isInBConnected);

        // 1. Perbarui warna emisi hanya saat kombinasi state berubah (dirty bitmask)
        int colorKey = (isParkedPreview ? 1 : 0)
                       | (isInAConnected ? 2 : 0)
                       | (inAValue ? 4 : 0)
                       | (isInBConnected ? 8 : 0)
                       | (inBValue ? 16 : 0)
                       | (outputValue ? 32 : 0)
                       | (inputsComplete ? 64 : 0);

        if (colorKey != lastColorStateKey)
        {
            lastColorStateKey = colorKey;
            lastInAConnected = isInAConnected;
            lastInAValue = inAValue;
            lastInBConnected = isInBConnected;
            lastInBValue = inBValue;
            lastOutputValue = outputValue;
            lastInputsComplete = inputsComplete;
            lastIsParkedPreview = isParkedPreview;
            ApplyEmissiveStates(isInAConnected, inAValue, isInBConnected, inBValue, outputValue, inputsComplete, isParkedPreview);
        }

        // 2. Animasikan bagian mekanis sesuai tipe gerbang
        switch (gateType)
        {
            case NodeType.AND:
                AnimateAndMechanism(isInAConnected, inAValue, isInBConnected, inBValue, outputValue, isParkedPreview, lerpAlpha, dt);
                break;
            case NodeType.OR:
                AnimateOrMechanism(isInAConnected, inAValue, isInBConnected, inBValue, outputValue, isParkedPreview, dt);
                break;
            case NodeType.NOT:
                AnimateNotMechanism(isInAConnected, inAValue, outputValue, isParkedPreview, lerpAlpha, dt);
                break;
            case NodeType.XOR:
                AnimateXorMechanism(isInAConnected, inAValue, isInBConnected, inBValue, outputValue, isParkedPreview, lerpAlpha, dt);
                break;
            case NodeType.NAND:
                AnimateNandMechanism(isInAConnected, inAValue, isInBConnected, inBValue, outputValue, inputsComplete, isParkedPreview, lerpAlpha, dt);
                break;
        }
    }

    /// <summary>
    /// Mengganti 3 shared material mekanisme ke custom hardware shader 1-pass (tracking-agnostic) sambil menyimpan
    /// snapshot shader bawaan untuk pemulihan penuh Mode Kartu v5.
    /// </summary>
    public void ApplyExternalHardwareShader(Shader customShader)
    {
        if (!isBuilt || customShader == null || !customShader.isSupported)
        {
            return;
        }

        if (!hasExternalHardwareShader)
        {
            savedMetalDarkShader = metalDarkMat != null ? metalDarkMat.shader : null;
            savedMetalDarkQueue = metalDarkMat != null ? metalDarkMat.renderQueue : -1;
            savedMetalSteelShader = metalSteelMat != null ? metalSteelMat.shader : null;
            savedMetalSteelQueue = metalSteelMat != null ? metalSteelMat.renderQueue : -1;
            savedEmissiveShader = emissiveSharedMat != null ? emissiveSharedMat.shader : null;
            savedEmissiveQueue = emissiveSharedMat != null ? emissiveSharedMat.renderQueue : -1;
        }

        ApplyShaderToSharedMat(metalDarkMat, customShader, clearEmission: true);
        ApplyShaderToSharedMat(metalSteelMat, customShader, clearEmission: true);
        ApplyShaderToSharedMat(emissiveSharedMat, customShader, clearEmission: false);

        emissiveHasEmissionProp = emissiveSharedMat != null && emissiveSharedMat.HasProperty(EmissionColorPropId);
        hasExternalHardwareShader = true;

        if (hasExternalRimHighlight)
        {
            ApplyChassisRimHighlight();
        }
        ApplyEmissiveStates(lastInAConnected, lastInAValue, lastInBConnected, lastInBValue, lastOutputValue, lastInputsComplete, lastIsParkedPreview);
    }

    /// <summary>
    /// Memulihkan 3 shared material ke shader bawaan Mode Kartu v5 &amp; membersihkan seluruh override Fresnel MPB.
    /// </summary>
    public void RestoreDefaultHardwareShaders()
    {
        if (!isBuilt)
        {
            return;
        }

        if (hasExternalHardwareShader)
        {
            RestoreSharedMatShader(metalDarkMat, savedMetalDarkShader, savedMetalDarkQueue);
            RestoreSharedMatShader(metalSteelMat, savedMetalSteelShader, savedMetalSteelQueue);
            RestoreSharedMatShader(emissiveSharedMat, savedEmissiveShader, savedEmissiveQueue);
            emissiveHasEmissionProp = emissiveSharedMat != null && emissiveSharedMat.HasProperty(EmissionColorPropId);
            hasExternalHardwareShader = false;
        }

        hasExternalRimHighlight = false;
        externalRimIntensity = -1f;

        if (chassisRenderers != null)
        {
            for (int i = 0; i < chassisRenderers.Length; i++)
            {
                if (chassisRenderers[i] != null)
                {
                    chassisRenderers[i].SetPropertyBlock(null);
                }
            }
        }

        ApplyEmissiveStates(lastInAConnected, lastInAValue, lastInBConnected, lastInBValue, lastOutputValue, lastInputsComplete, lastIsParkedPreview);
    }

    /// <summary>
    /// Menerapkan warna Fresnel Rim Glow (Linear eksplisit) via MaterialPropertyBlock secara Zero-GC
    /// ke seluruh sasis logam &amp; saluran energi mekanisme gerbang tanpa menimpa warna status logika.
    /// </summary>
    public void SetExternalRimHighlight(Color linearRimColor, float rimPower, float rimIntensity)
    {
        if (!isBuilt || !hasExternalHardwareShader)
        {
            return;
        }

        if (hasExternalRimHighlight
            && Mathf.Abs(externalRimColorLinear.r - linearRimColor.r) < 0.005f
            && Mathf.Abs(externalRimColorLinear.g - linearRimColor.g) < 0.005f
            && Mathf.Abs(externalRimColorLinear.b - linearRimColor.b) < 0.005f
            && Mathf.Abs(externalRimPower - rimPower) < 0.02f
            && Mathf.Abs(externalRimIntensity - rimIntensity) < 0.015f)
        {
            return;
        }

        hasExternalRimHighlight = true;
        externalRimColorLinear = linearRimColor;
        externalRimPower = rimPower;
        externalRimIntensity = rimIntensity;

        ApplyChassisRimHighlight();
        ApplyEmissiveStates(lastInAConnected, lastInAValue, lastInBConnected, lastInBValue, lastOutputValue, lastInputsComplete, lastIsParkedPreview);
    }

    private void CacheChassisRenderers(Transform chamberRoot)
    {
        if (chamberRoot == null)
        {
            chassisRenderers = null;
            return;
        }

        Renderer[] all = chamberRoot.GetComponentsInChildren<Renderer>(true);
        int matchCount = 0;
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r != null && (r.sharedMaterial == metalDarkMat || r.sharedMaterial == metalSteelMat))
            {
                matchCount++;
            }
        }

        chassisRenderers = new Renderer[matchCount];
        int cursor = 0;
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r != null && (r.sharedMaterial == metalDarkMat || r.sharedMaterial == metalSteelMat))
            {
                chassisRenderers[cursor++] = r;
            }
        }
    }

    private void ApplyChassisRimHighlight()
    {
        if (chassisRenderers == null || mpb == null)
        {
            return;
        }

        mpb.Clear();
        mpb.SetColor(RimColorPropId, externalRimColorLinear);
        mpb.SetFloat(RimPowerPropId, externalRimPower);
        mpb.SetFloat(RimIntensityPropId, externalRimIntensity);

        for (int i = 0; i < chassisRenderers.Length; i++)
        {
            if (chassisRenderers[i] != null)
            {
                chassisRenderers[i].SetPropertyBlock(mpb);
            }
        }
    }

    private static void ApplyShaderToSharedMat(Material mat, Shader customShader, bool clearEmission)
    {
        if (mat == null || customShader == null)
        {
            return;
        }

        mat.shader = customShader;
        mat.renderQueue = -1;
        if (clearEmission && mat.HasProperty(EmissionColorPropId))
        {
            mat.SetColor(EmissionColorPropId, Color.black);
        }
    }

    private static void RestoreSharedMatShader(Material mat, Shader originalShader, int originalQueue)
    {
        if (mat == null || originalShader == null)
        {
            return;
        }

        mat.shader = originalShader;
        mat.renderQueue = originalQueue;
    }

    private void OnDestroy()
    {
        DestroyMat(ref metalDarkMat);
        DestroyMat(ref metalSteelMat);
        DestroyMat(ref emissiveSharedMat);
        mpb = null;
        chassisRenderers = null;
        inputARenderers = null;
        inputBRenderers = null;
        midBusRenderers = null;
        outputRenderers = null;
        powerCoreRenderers = null;
        auxHighlightRenderers = null;
        savedMetalDarkShader = null;
        savedMetalSteelShader = null;
        savedEmissiveShader = null;
    }

    // =========================================================================
    // SHARED CHASSIS & 5 OPEN CUTAWAY MECHANISM BUILDERS
    // =========================================================================

    private void BuildOpenGantryChassis(Transform root, float baseY)
    {
        // Pelat dek kerja gelap di atas AR_BaseTile3D
        CreatePart("Chassis_SubDeck", PrimitiveType.Cube, root,
            new Vector3(0f, baseY + 0.012f, 0f), Vector3.zero, new Vector3(0.46f, 0.024f, 0.34f), metalDarkMat);

        // 4 pilar sudut baja tipis (sisi depan -Z & belakang +Z dibiarkan terbuka lebar)
        float pillarH = 0.25f;
        float pillarY = baseY + 0.024f + (pillarH * 0.5f);
        CreatePart("Chassis_PostFL", PrimitiveType.Cube, root,
            new Vector3(-0.21f, pillarY, -0.155f), Vector3.zero, new Vector3(0.022f, pillarH, 0.022f), metalDarkMat);
        CreatePart("Chassis_PostFR", PrimitiveType.Cube, root,
            new Vector3(0.21f, pillarY, -0.155f), Vector3.zero, new Vector3(0.022f, pillarH, 0.022f), metalDarkMat);
        CreatePart("Chassis_PostBL", PrimitiveType.Cube, root,
            new Vector3(-0.21f, pillarY, 0.155f), Vector3.zero, new Vector3(0.022f, pillarH, 0.022f), metalDarkMat);
        CreatePart("Chassis_PostBR", PrimitiveType.Cube, root,
            new Vector3(0.21f, pillarY, 0.155f), Vector3.zero, new Vector3(0.022f, pillarH, 0.022f), metalDarkMat);

        // Balok tepi atas tipis (sisi kiri & kanan saja agar bagian tengah terbuka penuh dari atas/depan)
        float topRailY = baseY + 0.024f + pillarH + 0.008f;
        CreatePart("Chassis_TopSideL", PrimitiveType.Cube, root,
            new Vector3(-0.21f, topRailY, 0f), Vector3.zero, new Vector3(0.024f, 0.018f, 0.33f), metalSteelMat);
        CreatePart("Chassis_TopSideR", PrimitiveType.Cube, root,
            new Vector3(0.21f, topRailY, 0f), Vector3.zero, new Vector3(0.024f, 0.018f, 0.33f), metalSteelMat);
        CreatePart("Chassis_TopBackBar", PrimitiveType.Cube, root,
            new Vector3(0f, topRailY, 0.155f), Vector3.zero, new Vector3(0.44f, 0.016f, 0.020f), metalDarkMat);
    }

    private void BuildAndSeriesBridges(Transform root, float baseY, Mesh diamondMesh, out float topY)
    {
        // Rel masuk dari Port_InA (-0.27, 0.055, +0.10) menuju pangkal Jembatan A (-0.15, DeckY, 0)
        Renderer inA1 = CreateEmissivePart("AND_InA_Stub", PrimitiveType.Cube, root,
            new Vector3(-0.235f, PortY + 0.018f, PortInAZ), Vector3.zero, new Vector3(0.075f, 0.026f, 0.026f));
        Renderer inA2 = CreateEmissivePart("AND_InA_Riser", PrimitiveType.Cube, root,
            new Vector3(-0.195f, (PortY + DeckY) * 0.5f, PortInAZ * 0.5f), new Vector3(-28f, 0f, -20f), new Vector3(0.026f, 0.125f, 0.026f));
        Renderer inA3 = CreateEmissivePart("AND_InA_Bus", PrimitiveType.Cube, root,
            new Vector3(-0.175f, DeckY, 0f), Vector3.zero, new Vector3(0.068f, 0.028f, 0.036f));

        // Rel penggerak dari Port_InB (-0.27, 0.055, -0.10) sepanjang dek depan menuju engsel Jembatan B (+0.015, DeckY, 0)
        Renderer inB1 = CreateEmissivePart("AND_InB_Stub", PrimitiveType.Cube, root,
            new Vector3(-0.235f, PortY + 0.018f, PortInBZ), Vector3.zero, new Vector3(0.075f, 0.026f, 0.026f));
        Renderer inB2 = CreateEmissivePart("AND_InB_Rail", PrimitiveType.Cube, root,
            new Vector3(-0.095f, baseY + 0.042f, PortInBZ), Vector3.zero, new Vector3(0.24f, 0.022f, 0.024f));
        Renderer inB3 = CreateEmissivePart("AND_InB_Riser", PrimitiveType.Cube, root,
            new Vector3(0.015f, (baseY + 0.042f + DeckY) * 0.5f, PortInBZ * 0.5f), new Vector3(36f, 0f, 0f), new Vector3(0.024f, 0.115f, 0.024f));

        // 3 pilar penyangga rel seri tengah (kiri, tengah, kanan)
        CreatePart("AND_PierLeft", PrimitiveType.Cube, root,
            new Vector3(-0.145f, (baseY + DeckY) * 0.5f, 0f), Vector3.zero, new Vector3(0.034f, DeckY - baseY, 0.070f), metalSteelMat);
        CreatePart("AND_PierMid", PrimitiveType.Cube, root,
            new Vector3(0.005f, (baseY + DeckY) * 0.5f, 0f), Vector3.zero, new Vector3(0.038f, DeckY - baseY, 0.070f), metalSteelMat);
        CreatePart("AND_PierRight", PrimitiveType.Cube, root,
            new Vector3(0.155f, (baseY + DeckY) * 0.5f, 0f), Vector3.zero, new Vector3(0.034f, DeckY - baseY, 0.070f), metalSteelMat);

        // Jembatan A (engsel di X = -0.145, merentang ke kanan sepanjang 0.132 menuju PierMid)
        animPivotA = CreateChild("AND_BridgeA_Pivot", root, new Vector3(-0.145f, DeckY, 0f));
        CreatePart("AND_BridgeA_Frame", PrimitiveType.Cube, animPivotA,
            new Vector3(0.066f, 0.006f, 0f), Vector3.zero, new Vector3(0.132f, 0.014f, 0.068f), metalSteelMat);
        Renderer bridgeABar = CreateEmissivePart("AND_BridgeA_Core", PrimitiveType.Cube, animPivotA,
            new Vector3(0.066f, 0.018f, 0f), Vector3.zero, new Vector3(0.124f, 0.020f, 0.038f));

        // Node persimpangan tengah (menyala jika Bridge A turun tersambung, memperlihatkan energi tertahan bila Bridge B terbuka!)
        Renderer midPad = CreateEmissivePart("AND_MidJunction", PrimitiveType.Cube, root,
            new Vector3(0.005f, DeckY + 0.010f, 0f), Vector3.zero, new Vector3(0.036f, 0.026f, 0.052f));

        // Jembatan B (engsel di X = +0.015, merentang ke kanan sepanjang 0.132 menuju PierRight)
        animPivotB = CreateChild("AND_BridgeB_Pivot", root, new Vector3(0.015f, DeckY, 0f));
        CreatePart("AND_BridgeB_Frame", PrimitiveType.Cube, animPivotB,
            new Vector3(0.066f, 0.006f, 0f), Vector3.zero, new Vector3(0.132f, 0.014f, 0.068f), metalSteelMat);
        Renderer bridgeBBar = CreateEmissivePart("AND_BridgeB_Core", PrimitiveType.Cube, animPivotB,
            new Vector3(0.066f, 0.018f, 0f), Vector3.zero, new Vector3(0.124f, 0.020f, 0.038f));

        // Saluran keluaran ke Port_Out (+0.27, 0.055, 0) + kristal inti output
        Renderer outBus = CreateEmissivePart("AND_Out_Bus", PrimitiveType.Cube, root,
            new Vector3(0.185f, DeckY, 0f), Vector3.zero, new Vector3(0.065f, 0.028f, 0.042f));
        Renderer outDrop = CreateEmissivePart("AND_Out_Drop", PrimitiveType.Cube, root,
            new Vector3(0.235f, (PortY + DeckY) * 0.5f, 0f), new Vector3(0f, 0f, 34f), new Vector3(0.026f, 0.125f, 0.030f));
        Renderer outGem = CreateEmissiveMeshPart("AND_Out_Gem", diamondMesh, root,
            new Vector3(0.165f, DeckY + 0.072f, 0f), Vector3.zero, Vector3.one * 0.046f);
        animRotorPrimary = outGem.transform;

        inputARenderers = new[] { inA1, inA2, inA3, bridgeABar };
        inputBRenderers = new[] { inB1, inB2, inB3 };
        midBusRenderers = new[] { midPad };
        auxHighlightRenderers = new[] { bridgeBBar };
        outputRenderers = new[] { outBus, outDrop, outGem };
        topY = baseY + 0.30f;
    }

    private void BuildOrParallelLanes(Transform root, float baseY, Mesh torusMesh, Mesh diamondMesh, out float topY)
    {
        // Penyangga jalur paralel atas (+Z) dan bawah (-Z)
        CreatePart("OR_BedUpper", PrimitiveType.Cube, root,
            new Vector3(-0.04f, DeckY - 0.016f, PortInAZ), Vector3.zero, new Vector3(0.32f, 0.014f, 0.044f), metalSteelMat);
        CreatePart("OR_BedLower", PrimitiveType.Cube, root,
            new Vector3(-0.04f, DeckY - 0.016f, PortInBZ), Vector3.zero, new Vector3(0.32f, 0.014f, 0.044f), metalSteelMat);

        // Jalur Paralel A (Atas, Z = +0.10) dari Port_InA -> Kolektor (+0.15, DeckY, 0)
        Renderer inA1 = CreateEmissivePart("OR_LaneA_Rise", PrimitiveType.Cube, root,
            new Vector3(-0.230f, (PortY + DeckY) * 0.5f, PortInAZ), new Vector3(0f, 0f, -36f), new Vector3(0.026f, 0.125f, 0.028f));
        Renderer inA2 = CreateEmissivePart("OR_LaneA_Main", PrimitiveType.Cube, root,
            new Vector3(-0.045f, DeckY, PortInAZ), Vector3.zero, new Vector3(0.28f, 0.028f, 0.032f));
        Renderer inA3 = CreateEmissivePart("OR_LaneA_Merge", PrimitiveType.Cube, root,
            new Vector3(0.115f, DeckY, PortInAZ * 0.5f), new Vector3(0f, 48f, 0f), new Vector3(0.135f, 0.026f, 0.028f));
        Renderer orbA = CreateEmissivePart("OR_LaneA_PulseOrb", PrimitiveType.Sphere, root,
            new Vector3(-0.16f, DeckY + 0.022f, PortInAZ), Vector3.zero, Vector3.one * 0.040f);
        animPulseOrbA = orbA.transform;

        // Jalur Paralel B (Bawah, Z = -0.10) dari Port_InB -> Kolektor (+0.15, DeckY, 0)
        Renderer inB1 = CreateEmissivePart("OR_LaneB_Rise", PrimitiveType.Cube, root,
            new Vector3(-0.230f, (PortY + DeckY) * 0.5f, PortInBZ), new Vector3(0f, 0f, -36f), new Vector3(0.026f, 0.125f, 0.028f));
        Renderer inB2 = CreateEmissivePart("OR_LaneB_Main", PrimitiveType.Cube, root,
            new Vector3(-0.045f, DeckY, PortInBZ), Vector3.zero, new Vector3(0.28f, 0.028f, 0.032f));
        Renderer inB3 = CreateEmissivePart("OR_LaneB_Merge", PrimitiveType.Cube, root,
            new Vector3(0.115f, DeckY, PortInBZ * 0.5f), new Vector3(0f, -48f, 0f), new Vector3(0.135f, 0.026f, 0.028f));
        Renderer orbB = CreateEmissivePart("OR_LaneB_PulseOrb", PrimitiveType.Sphere, root,
            new Vector3(-0.16f, DeckY + 0.022f, PortInBZ), Vector3.zero, Vector3.one * 0.040f);
        animPulseOrbB = orbB.transform;

        // Kolektor kanan (+0.155, DeckY, 0) + cincin turbin berputar + saluran ke Port_Out
        CreatePart("OR_CollectorPedestal", PrimitiveType.Cylinder, root,
            new Vector3(0.155f, (baseY + DeckY) * 0.5f, 0f), Vector3.zero, new Vector3(0.088f, (DeckY - baseY) * 0.5f, 0.088f), metalSteelMat);

        animRotorPrimary = CreateChild("OR_CollectorRingPivot", root, new Vector3(0.155f, DeckY + 0.028f, 0f));
        Renderer ringOut = CreateEmissiveMeshPart("OR_CollectorRing", torusMesh, animRotorPrimary,
            Vector3.zero, Vector3.zero, Vector3.one * 0.074f);
        Renderer coreOut = CreateEmissiveMeshPart("OR_CollectorGem", diamondMesh, root,
            new Vector3(0.155f, DeckY + 0.068f, 0f), Vector3.zero, Vector3.one * 0.052f);
        animRotorSecondary = coreOut.transform;

        Renderer outDrop = CreateEmissivePart("OR_Out_Drop", PrimitiveType.Cube, root,
            new Vector3(0.225f, (PortY + DeckY) * 0.5f, 0f), new Vector3(0f, 0f, 38f), new Vector3(0.028f, 0.135f, 0.032f));

        inputARenderers = new[] { inA1, inA2, inA3, orbA };
        inputBRenderers = new[] { inB1, inB2, inB3, orbB };
        outputRenderers = new[] { ringOut, coreOut, outDrop };
        topY = baseY + 0.30f;
    }

    private void BuildNotPowerCoreInverter(Transform root, float baseY, Mesh torusMesh, Mesh diamondMesh, out float topY)
    {
        // 1. INTI DAYA INTERNAL (ALWAYS LIT!) di ruang atas-tengah: menjelaskan sumber daya '1' saat input '0'
        Transform coreTower = CreateChild("NOT_InternalPowerCore", root, new Vector3(0.055f, DeckY + 0.145f, 0f));
        CreatePart("NOT_CoreCapTop", PrimitiveType.Cylinder, coreTower,
            new Vector3(0f, 0.052f, 0f), Vector3.zero, new Vector3(0.082f, 0.012f, 0.082f), metalSteelMat);
        CreatePart("NOT_CoreCapBot", PrimitiveType.Cylinder, coreTower,
            new Vector3(0f, -0.050f, 0f), Vector3.zero, new Vector3(0.082f, 0.012f, 0.082f), metalSteelMat);
        Renderer coreCrystal = CreateEmissiveMeshPart("NOT_CoreCrystal", diamondMesh, coreTower,
            Vector3.zero, Vector3.zero, new Vector3(0.048f, 0.056f, 0.048f));
        Renderer coreHalo = CreateEmissiveMeshPart("NOT_CoreHalo", torusMesh, coreTower,
            Vector3.zero, new Vector3(18f, 0f, 0f), Vector3.one * 0.068f);
        Renderer coreDropBus = CreateEmissivePart("NOT_CoreFeedBus", PrimitiveType.Cylinder, root,
            new Vector3(0.055f, DeckY + 0.048f, 0f), Vector3.zero, new Vector3(0.030f, 0.055f, 0.030f));
        animRotorSecondary = coreCrystal.transform;

        // 2. Jalur masukan tunggal dari Port_InA (-0.27, 0.055, 0) -> pendorong kiri
        Renderer inA1 = CreateEmissivePart("NOT_In_Rise", PrimitiveType.Cube, root,
            new Vector3(-0.228f, (PortY + DeckY) * 0.5f, 0f), new Vector3(0f, 0f, -36f), new Vector3(0.028f, 0.125f, 0.032f));
        Renderer inA2 = CreateEmissivePart("NOT_In_Rail", PrimitiveType.Cube, root,
            new Vector3(-0.135f, DeckY, 0f), Vector3.zero, new Vector3(0.145f, 0.028f, 0.034f));

        // 3. Batang Pendorong + Pelat Katup Penyekat (bergerak ke kanan untuk memblokir saluran Inti Daya saat Input=1!)
        animSlidePrimary = CreateChild("NOT_BlockingValveAssembly", root, new Vector3(-0.045f, DeckY, 0f));
        CreatePart("NOT_PushRod", PrimitiveType.Cylinder, animSlidePrimary,
            new Vector3(-0.055f, 0f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.022f, 0.058f, 0.022f), metalSteelMat);
        CreatePart("NOT_ShutterPlate", PrimitiveType.Cube, animSlidePrimary,
            new Vector3(0.015f, 0.008f, 0f), Vector3.zero, new Vector3(0.028f, 0.115f, 0.120f), metalDarkMat);
        Renderer shutterTrim = CreateEmissivePart("NOT_ShutterWarningBar", PrimitiveType.Cube, animSlidePrimary,
            new Vector3(0.015f, 0.008f, -0.062f), Vector3.zero, new Vector3(0.032f, 0.095f, 0.010f));

        // 4. Inverter Bubble (cincin + bola pembalik mencolok di kanan) & jalur ke Port_Out (+0.27, 0.055, 0)
        Renderer outBridge = CreateEmissivePart("NOT_Out_Bridge", PrimitiveType.Cube, root,
            new Vector3(0.115f, DeckY, 0f), Vector3.zero, new Vector3(0.115f, 0.028f, 0.036f));
        animRotorPrimary = CreateChild("NOT_InverterBubblePivot", root, new Vector3(0.178f, DeckY, 0f));
        Renderer invBubbleRing = CreateEmissiveMeshPart("NOT_InverterBubbleRing", torusMesh, animRotorPrimary,
            Vector3.zero, new Vector3(0f, 0f, 90f), Vector3.one * 0.062f);
        Renderer invBubbleCore = CreateEmissivePart("NOT_InverterBubbleCore", PrimitiveType.Sphere, animRotorPrimary,
            Vector3.zero, Vector3.zero, Vector3.one * 0.058f);
        Renderer outDrop = CreateEmissivePart("NOT_Out_Drop", PrimitiveType.Cube, root,
            new Vector3(0.232f, (PortY + DeckY) * 0.5f, 0f), new Vector3(0f, 0f, 36f), new Vector3(0.028f, 0.125f, 0.032f));

        powerCoreRenderers = new[] { coreCrystal, coreHalo, coreDropBus };
        inputARenderers = new[] { inA1, inA2, shutterTrim };
        outputRenderers = new[] { outBridge, invBubbleRing, invBubbleCore, outDrop };
        topY = DeckY + 0.215f;
    }

    private void BuildXorBalanceComparator(Transform root, float baseY, Mesh diamondMesh, out float topY)
    {
        // Poros penyangga timbangan di tengah
        CreatePart("XOR_FulcrumPost", PrimitiveType.Cylinder, root,
            new Vector3(-0.01f, (baseY + DeckY + 0.03f) * 0.5f, 0f), Vector3.zero, new Vector3(0.044f, (DeckY + 0.03f - baseY) * 0.5f, 0.044f), metalSteelMat);

        // Jalur & Piston A (+Z, dari Port_InA)
        Renderer inA1 = CreateEmissivePart("XOR_InA_Stub", PrimitiveType.Cube, root,
            new Vector3(-0.225f, PortY + 0.018f, PortInAZ), Vector3.zero, new Vector3(0.095f, 0.024f, 0.024f));
        Renderer inA2 = CreateEmissivePart("XOR_InA_Feed", PrimitiveType.Cube, root,
            new Vector3(-0.105f, baseY + 0.036f, PortInAZ), Vector3.zero, new Vector3(0.19f, 0.022f, 0.024f));
        animSlidePrimary = CreateChild("XOR_PistonA", root, new Vector3(-0.01f, baseY + 0.070f, PortInAZ));
        CreatePart("XOR_PistonA_Rod", PrimitiveType.Cylinder, animSlidePrimary,
            Vector3.zero, Vector3.zero, new Vector3(0.024f, 0.036f, 0.024f), metalSteelMat);
        Renderer capA = CreateEmissivePart("XOR_PistonA_Head", PrimitiveType.Cylinder, animSlidePrimary,
            new Vector3(0f, 0.038f, 0f), Vector3.zero, new Vector3(0.052f, 0.012f, 0.052f));

        // Jalur & Piston B (-Z, dari Port_InB)
        Renderer inB1 = CreateEmissivePart("XOR_InB_Stub", PrimitiveType.Cube, root,
            new Vector3(-0.225f, PortY + 0.018f, PortInBZ), Vector3.zero, new Vector3(0.095f, 0.024f, 0.024f));
        Renderer inB2 = CreateEmissivePart("XOR_InB_Feed", PrimitiveType.Cube, root,
            new Vector3(-0.105f, baseY + 0.036f, PortInBZ), Vector3.zero, new Vector3(0.19f, 0.022f, 0.024f));
        animSlideSecondary = CreateChild("XOR_PistonB", root, new Vector3(-0.01f, baseY + 0.070f, PortInBZ));
        CreatePart("XOR_PistonB_Rod", PrimitiveType.Cylinder, animSlideSecondary,
            Vector3.zero, Vector3.zero, new Vector3(0.024f, 0.036f, 0.024f), metalSteelMat);
        Renderer capB = CreateEmissivePart("XOR_PistonB_Head", PrimitiveType.Cylinder, animSlideSecondary,
            new Vector3(0f, 0.038f, 0f), Vector3.zero, new Vector3(0.052f, 0.012f, 0.052f));

        // Palang Timbangan Komparator (berputar pada sumbu X: ujung +Z dan -Z naik-turun bergantian)
        animPivotA = CreateChild("XOR_BalanceBeamPivot", root, new Vector3(-0.01f, DeckY + 0.042f, 0f));
        CreatePart("XOR_BeamBar", PrimitiveType.Cube, animPivotA,
            Vector3.zero, Vector3.zero, new Vector3(0.052f, 0.022f, 0.255f), metalSteelMat);
        Renderer beamStrip = CreateEmissivePart("XOR_BeamCoreStrip", PrimitiveType.Cube, animPivotA,
            new Vector3(0.028f, 0f, 0f), Vector3.zero, new Vector3(0.014f, 0.016f, 0.240f));

        // Indikator benturan tengah (menyala oranye-merah saat 11 saling mengunci imbang!)
        Renderer clashCore = CreateEmissiveMeshPart("XOR_ClashCore", diamondMesh, root,
            new Vector3(-0.01f, DeckY + 0.115f, 0f), Vector3.zero, Vector3.one * 0.042f);
        animRotorPrimary = clashCore.transform;

        // 2 Kontak Output di sisi kanan atas (+Z) & bawah (-Z) yang tersentuh saat palang miring
        Renderer contactA = CreateEmissivePart("XOR_ContactUpper", PrimitiveType.Sphere, root,
            new Vector3(0.075f, DeckY + 0.088f, PortInAZ), Vector3.zero, Vector3.one * 0.042f);
        Renderer contactB = CreateEmissivePart("XOR_ContactLower", PrimitiveType.Sphere, root,
            new Vector3(0.075f, DeckY + 0.088f, PortInBZ), Vector3.zero, Vector3.one * 0.042f);
        Renderer outCollector = CreateEmissivePart("XOR_OutCollector", PrimitiveType.Cube, root,
            new Vector3(0.145f, DeckY + 0.020f, 0f), Vector3.zero, new Vector3(0.095f, 0.030f, 0.16f));
        Renderer outDrop = CreateEmissivePart("XOR_OutDrop", PrimitiveType.Cube, root,
            new Vector3(0.228f, (PortY + DeckY + 0.015f) * 0.5f, 0f), new Vector3(0f, 0f, 38f), new Vector3(0.028f, 0.140f, 0.032f));

        inputARenderers = new[] { inA1, inA2, capA };
        inputBRenderers = new[] { inB1, inB2, capB };
        auxHighlightRenderers = new[] { clashCore };
        outputRenderers = new[] { beamStrip, contactA, contactB, outCollector, outDrop };
        topY = DeckY + 0.175f;
    }

    private void BuildNandPowerCoreBreaker(Transform root, float baseY, Mesh torusMesh, Mesh diamondMesh, out float topY)
    {
        // 1. INTI DAYA INTERNAL (ALWAYS LIT!) di kanan-atas rangka
        Transform coreTower = CreateChild("NAND_InternalPowerCore", root, new Vector3(0.075f, DeckY + 0.152f, 0f));
        CreatePart("NAND_CoreCapTop", PrimitiveType.Cylinder, coreTower,
            new Vector3(0f, 0.048f, 0f), Vector3.zero, new Vector3(0.078f, 0.011f, 0.078f), metalSteelMat);
        CreatePart("NAND_CoreCapBot", PrimitiveType.Cylinder, coreTower,
            new Vector3(0f, -0.046f, 0f), Vector3.zero, new Vector3(0.078f, 0.011f, 0.078f), metalSteelMat);
        Renderer coreCrystal = CreateEmissiveMeshPart("NAND_CoreCrystal", diamondMesh, coreTower,
            Vector3.zero, Vector3.zero, new Vector3(0.046f, 0.052f, 0.046f));
        Renderer coreHalo = CreateEmissiveMeshPart("NAND_CoreHalo", torusMesh, coreTower,
            Vector3.zero, new Vector3(-18f, 0f, 0f), Vector3.one * 0.064f);
        Renderer coreFeed = CreateEmissivePart("NAND_CoreFeed", PrimitiveType.Cylinder, root,
            new Vector3(0.075f, DeckY + 0.052f, 0f), Vector3.zero, new Vector3(0.028f, 0.054f, 0.028f));
        animRotorSecondary = coreCrystal.transform;

        // 2. Rel feeder A & B dari Port_InA & Port_InB menuju Pendorong Seri 2-Tahap
        Renderer inA1 = CreateEmissivePart("NAND_InA_Stub", PrimitiveType.Cube, root,
            new Vector3(-0.232f, PortY + 0.018f, PortInAZ), Vector3.zero, new Vector3(0.080f, 0.024f, 0.024f));
        Renderer inA2 = CreateEmissivePart("NAND_InA_Rail", PrimitiveType.Cube, root,
            new Vector3(-0.145f, DeckY, PortInAZ * 0.65f), new Vector3(0f, 24f, 0f), new Vector3(0.145f, 0.024f, 0.024f));

        Renderer inB1 = CreateEmissivePart("NAND_InB_Stub", PrimitiveType.Cube, root,
            new Vector3(-0.232f, PortY + 0.018f, PortInBZ), Vector3.zero, new Vector3(0.080f, 0.024f, 0.024f));
        Renderer inB2 = CreateEmissivePart("NAND_InB_Rail", PrimitiveType.Cube, root,
            new Vector3(-0.145f, DeckY, PortInBZ * 0.65f), new Vector3(0f, -24f, 0f), new Vector3(0.145f, 0.024f, 0.024f));

        // Pendorong Seri 2-Tahap: 0 input = mundur, 1 input = maju 50% (belum sentuh sekring!), 2 input = maju 100% memutus sekring!
        animSlidePrimary = CreateChild("NAND_DualStageRam", root, new Vector3(-0.155f, DeckY, 0f));
        CreatePart("NAND_RamBody", PrimitiveType.Cube, animSlidePrimary,
            Vector3.zero, Vector3.zero, new Vector3(0.095f, 0.044f, 0.086f), metalSteelMat);
        Renderer ramTip = CreateEmissivePart("NAND_RamTip", PrimitiveType.Cube, animSlidePrimary,
            new Vector3(0.062f, 0f, 0f), Vector3.zero, new Vector3(0.042f, 0.028f, 0.048f));

        // 3. Tuas Sekring / Breaker (normal tertutup datar menghubungkan Inti Daya ke Inverter Bubble; terangkat saat 11!)
        animPivotA = CreateChild("NAND_BreakerPivot", root, new Vector3(0.075f, DeckY, 0f));
        CreatePart("NAND_BreakerArm", PrimitiveType.Cube, animPivotA,
            new Vector3(0.052f, 0.005f, 0f), Vector3.zero, new Vector3(0.105f, 0.016f, 0.062f), metalSteelMat);
        Renderer breakerCore = CreateEmissivePart("NAND_BreakerCore", PrimitiveType.Cube, animPivotA,
            new Vector3(0.052f, 0.018f, 0f), Vector3.zero, new Vector3(0.096f, 0.020f, 0.036f));

        // 4. Inverter Bubble di kanan + saluran ke Port_Out (+0.27, 0.055, 0)
        animRotorPrimary = CreateChild("NAND_InverterBubblePivot", root, new Vector3(0.188f, DeckY, 0f));
        Renderer invRing = CreateEmissiveMeshPart("NAND_InverterRing", torusMesh, animRotorPrimary,
            Vector3.zero, new Vector3(0f, 0f, 90f), Vector3.one * 0.058f);
        Renderer invCore = CreateEmissivePart("NAND_InverterCore", PrimitiveType.Sphere, animRotorPrimary,
            Vector3.zero, Vector3.zero, Vector3.one * 0.054f);
        Renderer outDrop = CreateEmissivePart("NAND_Out_Drop", PrimitiveType.Cube, root,
            new Vector3(0.235f, (PortY + DeckY) * 0.5f, 0f), new Vector3(0f, 0f, 38f), new Vector3(0.026f, 0.125f, 0.030f));

        powerCoreRenderers = new[] { coreCrystal, coreHalo, coreFeed };
        inputARenderers = new[] { inA1, inA2 };
        inputBRenderers = new[] { inB1, inB2 };
        auxHighlightRenderers = new[] { ramTip };
        outputRenderers = new[] { breakerCore, invRing, invCore, outDrop };
        topY = DeckY + 0.215f;
    }

    // =========================================================================
    // PER-GATE EMISSIVE COLORING & CONTINUOUS KINEMATICS (ZERO-GC)
    // =========================================================================

    private void ApplyEmissiveStates(
        bool isInAConnected,
        bool inAValue,
        bool isInBConnected,
        bool inBValue,
        bool outputValue,
        bool inputsComplete,
        bool isParkedPreview)
    {
        // Inti Daya Internal (NOT & NAND) selalu bercahaya terang konstan (redup lembut saat parkir)
        if (powerCoreRenderers != null)
        {
            ApplyColorToGroup(
                powerCoreRenderers,
                isParkedPreview ? ParkedAccentBaseColor : PowerCoreBaseColor,
                isParkedPreview ? ParkedAccentEmission : PowerCoreEmission);
        }

        if (isParkedPreview)
        {
            ApplyColorToGroup(inputARenderers, EnergyUnconnectedBaseColor, EnergyUnconnectedEmission);
            ApplyColorToGroup(inputBRenderers, EnergyUnconnectedBaseColor, EnergyUnconnectedEmission);
            ApplyColorToGroup(midBusRenderers, EnergyUnconnectedBaseColor, EnergyUnconnectedEmission);
            ApplyColorToGroup(auxHighlightRenderers, ParkedAccentBaseColor, ParkedAccentEmission);
            ApplyColorToGroup(outputRenderers, ParkedAccentBaseColor, ParkedAccentEmission);
            return;
        }

        ResolveSignalColors(isInAConnected, inAValue, out Color aBase, out Color aEmit);
        ResolveSignalColors(isInBConnected, inBValue, out Color bBase, out Color bEmit);
        ResolveSignalColors(inputsComplete, outputValue, out Color outBase, out Color outEmit);

        ApplyColorToGroup(inputARenderers, aBase, aEmit);
        ApplyColorToGroup(inputBRenderers, bBase, bEmit);
        ApplyColorToGroup(outputRenderers, outBase, outEmit);

        switch (gateType)
        {
            case NodeType.AND:
                // Mid-junction menyala jika Jembatan A sudah membawa sinyal 1 dari kiri
                ResolveSignalColors(isInAConnected, inAValue, out Color midBase, out Color midEmit);
                ApplyColorToGroup(midBusRenderers, midBase, midEmit);

                // Jembatan B: hijau-sian penuh jika A=1&B=1, biru siaga jika B=1 tapi A=0 (tertutup menunggu daya kiri)
                if (isInBConnected && inBValue)
                {
                    ApplyColorToGroup(
                        auxHighlightRenderers,
                        (isInAConnected && inAValue) ? EnergyHighBaseColor : EnergyReadyBaseColor,
                        (isInAConnected && inAValue) ? EnergyHighEmission : EnergyReadyEmission);
                }
                else
                {
                    ApplyColorToGroup(auxHighlightRenderers, bBase, bEmit);
                }
                break;

            case NodeType.XOR:
                bool isBothHighClash = isInAConnected && isInBConnected && inAValue && inBValue;
                if (isBothHighClash)
                {
                    ApplyColorToGroup(auxHighlightRenderers, ClashBaseColor, ClashEmission);
                }
                else
                {
                    ApplyColorToGroup(auxHighlightRenderers, outBase, outEmit);
                }
                break;

            case NodeType.NAND:
                int activeInputs = (isInAConnected && inAValue ? 1 : 0) + (isInBConnected && inBValue ? 1 : 0);
                if (activeInputs >= 2)
                {
                    ApplyColorToGroup(auxHighlightRenderers, ClashBaseColor, ClashEmission);
                }
                else if (activeInputs == 1)
                {
                    ApplyColorToGroup(auxHighlightRenderers, EnergyReadyBaseColor, EnergyReadyEmission);
                }
                else
                {
                    ApplyColorToGroup(auxHighlightRenderers, EnergyUnconnectedBaseColor, EnergyUnconnectedEmission);
                }
                break;
        }
    }

    private void AnimateAndMechanism(
        bool isInAConnected,
        bool inAValue,
        bool isInBConnected,
        bool inBValue,
        bool outputValue,
        bool isParkedPreview,
        float lerpAlpha,
        float dt)
    {
        float targetAngleA = isParkedPreview ? 26f : ((isInAConnected && inAValue) ? 0f : 52f);
        float targetAngleB = isParkedPreview ? 26f : ((isInBConnected && inBValue) ? 0f : 52f);

        currentAngleA = Mathf.Lerp(currentAngleA, targetAngleA, lerpAlpha);
        currentAngleB = Mathf.Lerp(currentAngleB, targetAngleB, lerpAlpha);

        if (animPivotA != null)
        {
            animPivotA.localRotation = Quaternion.Euler(0f, 0f, currentAngleA);
        }
        if (animPivotB != null)
        {
            animPivotB.localRotation = Quaternion.Euler(0f, 0f, currentAngleB);
        }
        if (animRotorPrimary != null)
        {
            float spin = isParkedPreview ? 32f : (outputValue ? 200f : 18f);
            animRotorPrimary.Rotate(Vector3.up, spin * dt, Space.Self);
        }
    }

    private void AnimateOrMechanism(
        bool isInAConnected,
        bool inAValue,
        bool isInBConnected,
        bool inBValue,
        bool outputValue,
        bool isParkedPreview,
        float dt)
    {
        if (animPulseOrbA != null)
        {
            bool laneAActive = !isParkedPreview && isInAConnected && inAValue;
            if (animPulseOrbA.gameObject.activeSelf != laneAActive)
            {
                animPulseOrbA.gameObject.SetActive(laneAActive);
            }
            if (laneAActive)
            {
                float x = Mathf.Lerp(-0.17f, 0.12f, flowPhase);
                animPulseOrbA.localPosition = new Vector3(x, DeckY + 0.022f, PortInAZ);
            }
        }

        if (animPulseOrbB != null)
        {
            bool laneBActive = !isParkedPreview && isInBConnected && inBValue;
            if (animPulseOrbB.gameObject.activeSelf != laneBActive)
            {
                animPulseOrbB.gameObject.SetActive(laneBActive);
            }
            if (laneBActive)
            {
                float x = Mathf.Lerp(-0.17f, 0.12f, Mathf.Repeat(flowPhase + 0.5f, 1f));
                animPulseOrbB.localPosition = new Vector3(x, DeckY + 0.022f, PortInBZ);
            }
        }

        float ringSpin = isParkedPreview ? 36f : (outputValue ? 240f : 22f);
        if (animRotorPrimary != null)
        {
            animRotorPrimary.Rotate(Vector3.up, ringSpin * dt, Space.Self);
        }
        if (animRotorSecondary != null)
        {
            animRotorSecondary.Rotate(Vector3.up, -ringSpin * 0.8f * dt, Space.Self);
        }
    }

    private void AnimateNotMechanism(
        bool isInAConnected,
        bool inAValue,
        bool outputValue,
        bool isParkedPreview,
        float lerpAlpha,
        float dt)
    {
        // Saat Input=1, pelat penyekat meluncur ke kanan (X=+0.088) memblokir jalur Inti Daya -> Inverter Bubble!
        // Saat Input=0, pelat mundur ke kiri (X=-0.055) membuka jalur daya internal.
        float targetSlideX = isParkedPreview ? -0.020f : ((isInAConnected && inAValue) ? 0.088f : -0.055f);
        currentSlideA = Mathf.Lerp(currentSlideA, targetSlideX, lerpAlpha);

        if (animSlidePrimary != null)
        {
            animSlidePrimary.localPosition = new Vector3(currentSlideA, DeckY, 0f);
        }

        if (animRotorSecondary != null)
        {
            // Inti Daya Internal selalu berputar hidup
            animRotorSecondary.Rotate(Vector3.up, (isParkedPreview ? 45f : 150f) * dt, Space.Self);
        }

        if (animRotorPrimary != null)
        {
            float invSpin = isParkedPreview ? 32f : (outputValue ? 220f : 16f);
            animRotorPrimary.Rotate(Vector3.right, invSpin * dt, Space.Self);
        }
    }

    private void AnimateXorMechanism(
        bool isInAConnected,
        bool inAValue,
        bool isInBConnected,
        bool inBValue,
        bool outputValue,
        bool isParkedPreview,
        float lerpAlpha,
        float dt)
    {
        bool aHigh = !isParkedPreview && isInAConnected && inAValue;
        bool bHigh = !isParkedPreview && isInBConnected && inBValue;

        float targetPistonAY = aHigh ? (DeckY + 0.020f) : (DeckY - 0.036f);
        float targetPistonBY = bHigh ? (DeckY + 0.020f) : (DeckY - 0.036f);

        currentSlideA = Mathf.Lerp(currentSlideA, targetPistonAY, lerpAlpha);
        currentSlideB = Mathf.Lerp(currentSlideB, targetPistonBY, lerpAlpha);

        if (animSlidePrimary != null)
        {
            animSlidePrimary.localPosition = new Vector3(-0.01f, currentSlideA, PortInAZ);
        }
        if (animSlideSecondary != null)
        {
            animSlideSecondary.localPosition = new Vector3(-0.01f, currentSlideB, PortInBZ);
        }

        // Palang timbangan: hanya miring bila A != B! Saat 00 atau 11 terkunci datar (0 derajat).
        float targetTiltX = 0f;
        if (isParkedPreview)
        {
            targetTiltX = 14f;
        }
        else if (aHigh && !bHigh)
        {
            targetTiltX = -24f;
        }
        else if (!aHigh && bHigh)
        {
            targetTiltX = 24f;
        }

        currentAngleA = Mathf.Lerp(currentAngleA, targetTiltX, lerpAlpha);
        float clashVibe = (aHigh && bHigh) ? (Mathf.Sin(Time.time * 42f) * 1.8f) : 0f;
        if (animPivotA != null)
        {
            animPivotA.localRotation = Quaternion.Euler(currentAngleA + clashVibe, 0f, 0f);
        }

        if (animRotorPrimary != null)
        {
            float coreSpin = (aHigh && bHigh) ? 310f : (outputValue ? 190f : 28f);
            animRotorPrimary.Rotate(Vector3.up, coreSpin * dt, Space.Self);
        }
    }

    private void AnimateNandMechanism(
        bool isInAConnected,
        bool inAValue,
        bool isInBConnected,
        bool inBValue,
        bool outputValue,
        bool inputsComplete,
        bool isParkedPreview,
        float lerpAlpha,
        float dt)
    {
        int activeCount = (!isParkedPreview && isInAConnected && inAValue ? 1 : 0)
                          + (!isParkedPreview && isInBConnected && inBValue ? 1 : 0);

        // 0/2 -> X=-0.155 (mundur), 1/2 -> X=-0.068 (setengah langkah, belum sentuh sekring!), 2/2 -> X=+0.025 (menekan & memutus sekring!)
        float targetRamX = isParkedPreview
            ? -0.105f
            : (activeCount >= 2 ? 0.025f : (activeCount == 1 ? -0.068f : -0.155f));
        bool breakerTripped = !isParkedPreview && inputsComplete && activeCount >= 2;
        float targetBreakerAngle = isParkedPreview ? 12f : (breakerTripped ? 54f : 0f);

        currentSlideA = Mathf.Lerp(currentSlideA, targetRamX, lerpAlpha);
        currentAngleA = Mathf.Lerp(currentAngleA, targetBreakerAngle, lerpAlpha);

        if (animSlidePrimary != null)
        {
            animSlidePrimary.localPosition = new Vector3(currentSlideA, DeckY, 0f);
        }
        if (animPivotA != null)
        {
            animPivotA.localRotation = Quaternion.Euler(0f, 0f, currentAngleA);
        }
        if (animRotorSecondary != null)
        {
            animRotorSecondary.Rotate(Vector3.up, (isParkedPreview ? 45f : 150f) * dt, Space.Self);
        }
        if (animRotorPrimary != null)
        {
            float invSpin = isParkedPreview ? 32f : (outputValue ? 220f : 16f);
            animRotorPrimary.Rotate(Vector3.right, invSpin * dt, Space.Self);
        }
    }

    // =========================================================================
    // HELPERS (OPENGLES3-SAFE SHADERS & ZERO-GC MPB)
    // =========================================================================

    private void EnsureSharedMaterialsInitialized()
    {
        if (metalDarkMat == null)
        {
            metalDarkMat = CreateOpaqueSciFiMaterial(MetalDarkColor, 0.24f, 0.22f, enableEmission: false);
        }
        if (metalSteelMat == null)
        {
            metalSteelMat = CreateOpaqueSciFiMaterial(MetalSteelColor, 0.30f, 0.28f, enableEmission: false);
        }
        if (emissiveSharedMat == null)
        {
            emissiveSharedMat = CreateOpaqueSciFiMaterial(Color.white, 0.36f, 0.14f, enableEmission: true);
            emissiveHasEmissionProp = emissiveSharedMat != null && emissiveSharedMat.HasProperty(EmissionColorPropId);
        }
    }

    private static Material CreateOpaqueSciFiMaterial(Color baseColor, float glossiness, float metallic, bool enableEmission)
    {
        Shader shader = Shader.Find("Standard");
        if (shader == null || !shader.isSupported)
        {
            shader = Shader.Find("Legacy Shaders/Diffuse");
        }
        if (shader == null || !shader.isSupported)
        {
            shader = Shader.Find("Sprites/Default");
        }
        if (shader == null || !shader.isSupported)
        {
            shader = Shader.Find("UI/Default");
        }
        if (shader == null)
        {
            return null;
        }

        Material mat = new Material(shader);
        if (mat.HasProperty(ColorPropId))
        {
            mat.SetColor(ColorPropId, baseColor);
        }
        else
        {
            mat.color = baseColor;
        }

        if (mat.HasProperty(GlossinessPropId))
        {
            mat.SetFloat(GlossinessPropId, glossiness);
        }
        if (mat.HasProperty(MetallicPropId))
        {
            mat.SetFloat(MetallicPropId, metallic);
        }
        if (enableEmission && mat.HasProperty(EmissionColorPropId))
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor(EmissionColorPropId, Color.black);
        }

        return mat;
    }

    private static void ResolveSignalColors(bool isConnected, bool value, out Color baseColor, out Color emissionColor)
    {
        if (!isConnected)
        {
            baseColor = EnergyUnconnectedBaseColor;
            emissionColor = EnergyUnconnectedEmission;
        }
        else if (value)
        {
            baseColor = EnergyHighBaseColor;
            emissionColor = EnergyHighEmission;
        }
        else
        {
            baseColor = EnergyLowBaseColor;
            emissionColor = EnergyLowEmission;
        }
    }

    private void ApplyColorToGroup(Renderer[] group, Color baseColor, Color emissionColor)
    {
        if (group == null || mpb == null)
        {
            return;
        }

        mpb.Clear();
        mpb.SetColor(ColorPropId, baseColor);
        if (emissiveHasEmissionProp)
        {
            mpb.SetColor(EmissionColorPropId, emissionColor);
        }
        if (hasExternalHardwareShader && hasExternalRimHighlight)
        {
            mpb.SetColor(RimColorPropId, externalRimColorLinear);
            mpb.SetFloat(RimPowerPropId, externalRimPower);
            mpb.SetFloat(RimIntensityPropId, externalRimIntensity * 0.58f);
        }

        for (int i = 0; i < group.Length; i++)
        {
            if (group[i] != null)
            {
                group[i].SetPropertyBlock(mpb);
            }
        }
    }

    private static Transform CreateChild(string name, Transform parent, Vector3 localPos)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPos;
        obj.transform.localRotation = Quaternion.identity;
        obj.transform.localScale = Vector3.one;
        return obj.transform;
    }

    private static Renderer CreatePart(
        string name,
        PrimitiveType primitiveType,
        Transform parent,
        Vector3 localPos,
        Vector3 localEuler,
        Vector3 localScale,
        Material sharedMat)
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
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            if (sharedMat != null)
            {
                rend.sharedMaterial = sharedMat;
            }
        }

        return rend;
    }

    private Renderer CreateEmissivePart(
        string name,
        PrimitiveType primitiveType,
        Transform parent,
        Vector3 localPos,
        Vector3 localEuler,
        Vector3 localScale)
    {
        return CreatePart(name, primitiveType, parent, localPos, localEuler, localScale, emissiveSharedMat);
    }

    private Renderer CreateEmissiveMeshPart(
        string name,
        Mesh mesh,
        Transform parent,
        Vector3 localPos,
        Vector3 localEuler,
        Vector3 localScale)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPos;
        obj.transform.localEulerAngles = localEuler;
        obj.transform.localScale = localScale;

        MeshFilter mf = obj.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        MeshRenderer mr = obj.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        if (emissiveSharedMat != null)
        {
            mr.sharedMaterial = emissiveSharedMat;
        }

        return mr;
    }

    private static void DestroyMat(ref Material mat)
    {
        if (mat != null)
        {
            Destroy(mat);
            mat = null;
        }
    }
}
