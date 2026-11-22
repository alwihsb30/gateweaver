using UnityEngine;
using TMPro;

public enum OutputPodCellStatus
{
    Untested = 0,
    Scanning = 1,
    Passed = 2,
    Failed = 3
}

/// <summary>
/// Modul Pod Tabel Kebenaran 2x2 pada Card_Output (GateWeaver AR v7.0 Polishing Final).
/// - Menampilkan 4 kotak mini dengan label biner 2 digit tegas & rata tengah: "00", "01", "10", "11".
/// - Warna status jernih: abu gelap = belum diuji, tepi sian terang = kombinasi aktif saat ini,
///   hijau = lolos, merah = gagal, putih-biru = sedang dipindai.
/// - Mode Kartu (v5): terpasang miring -48° di bibir depan kartu fisik (zero-regression).
/// - Mode Udara (v7): otomatis sejajar rata menghadap kamera (camera-facing) tanpa miring/terpelintir
///   dengan spasi bersih di atas lencana pemandu Output. Zero-GC & shader aman OpenGLES3.
/// </summary>
public sealed class OutputTruthPodVisual : MonoBehaviour
{
    private const int CellCount = 4;

    private static readonly int ColorPropId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorPropId = Shader.PropertyToID("_EmissionColor");
    private static readonly int GlossinessPropId = Shader.PropertyToID("_Glossiness");
    private static readonly int MetallicPropId = Shader.PropertyToID("_Metallic");
    private static readonly int RimColorPropId = Shader.PropertyToID("_RimColor");
    private static readonly int RimPowerPropId = Shader.PropertyToID("_RimPower");
    private static readonly int RimIntensityPropId = Shader.PropertyToID("_RimIntensity");

    private static readonly string[] BinaryRowLabels = { "00", "01", "10", "11" };

    // Koordinat 4 sel 2x2 simetris pada bidang lokal +Y tray (lebar 0.50, tinggi 0.24)
    private static readonly Vector2[] CellOffsetsXZ =
    {
        new Vector2(-0.120f,  0.054f), // Row 0: 00 (Kiri Atas)
        new Vector2( 0.120f,  0.054f), // Row 1: 01 (Kanan Atas)
        new Vector2(-0.120f, -0.054f), // Row 2: 10 (Kiri Bawah)
        new Vector2( 0.120f, -0.054f)  // Row 3: 11 (Kanan Bawah)
    };

    private static readonly Vector3 CardModeLocalPos = new Vector3(0f, 0.085f, -0.34f);
    private static readonly Quaternion CardModeLocalRot = Quaternion.Euler(-48f, 0f, 0f);

    // Palet warna tegas & kontras tinggi
    private static readonly Color TrayBodyColor = new Color(0.08f, 0.11f, 0.16f, 1f);
    private static readonly Color TrayTrimColor = new Color(0.32f, 0.42f, 0.54f, 1f);

    private static readonly Color StatusUntestedColor = new Color(0.14f, 0.18f, 0.25f, 1f);
    private static readonly Color StatusUntestedEmit = new Color(0.04f, 0.06f, 0.10f, 1f);
    private static readonly Color StatusPassedColor = new Color(0.09f, 0.72f, 0.40f, 1f);
    private static readonly Color StatusPassedEmit = new Color(0.08f, 0.68f, 0.36f, 1f) * 0.95f;
    private static readonly Color StatusFailedBaseColor = new Color(0.86f, 0.19f, 0.17f, 1f);
    private static readonly Color StatusFailedPulseColor = new Color(1.00f, 0.42f, 0.32f, 1f);
    private static readonly Color StatusScanSkyColor = new Color(0.46f, 0.82f, 1.00f, 1f);
    private static readonly Color StatusScanWhiteColor = new Color(0.98f, 1.00f, 1.00f, 1f);

    private static readonly Color RimIdleColor = new Color(0.24f, 0.32f, 0.42f, 1f);
    private static readonly Color RimIdleEmit = new Color(0.06f, 0.09f, 0.13f, 1f);
    private static readonly Color RimActiveCyanColor = new Color(0.22f, 0.96f, 1.00f, 1f);
    private static readonly Color RimActiveCyanEmit = new Color(0.20f, 0.92f, 1.00f, 1f) * 1.35f;

    private static readonly Color LabelUntestedTextColor = new Color(0.84f, 0.91f, 0.98f, 1f);
    private static readonly Color LabelActiveTextColor = new Color(1.00f, 1.00f, 1.00f, 1f);

    private GameObject podRootObj;
    private Material trayMetalMat;
    private Material trayTrimMat;
    private Material emissiveSharedMat;
    private bool emissiveHasEmissionProp = false;
    private MaterialPropertyBlock mpb;

    private GateNode ownerGateNode;
    private Camera cachedMainCamera;
    private bool wasAirModePose = false;

    private readonly Transform[] cellRootTransforms = new Transform[CellCount];
    private readonly Renderer[] cellPlateRenderers = new Renderer[CellCount];
    private readonly Renderer[] cellRimRenderers = new Renderer[CellCount];
    private readonly TextMeshPro[] cellBinaryLabels = new TextMeshPro[CellCount];
    private readonly Renderer[] frameRenderers = new Renderer[5];

    private Shader savedTrayMetalShader;
    private int savedTrayMetalQueue = -1;
    private Shader savedTrayTrimShader;
    private int savedTrayTrimQueue = -1;
    private Shader savedEmissiveShader;
    private int savedEmissiveQueue = -1;
    private bool hasExternalHardwareShader = false;
    private bool hasExternalFrameRim = false;
    private Color externalFrameRimColorLinear = Color.black;
    private float externalFrameRimPower = 2.8f;
    private float externalFrameRimIntensity = -1f;

    private readonly bool[] expectedTargets = new bool[CellCount];
    private readonly OutputPodCellStatus[] cellStatuses = new OutputPodCellStatus[CellCount];

    private bool isInitialized = false;
    private bool isNodeTracked = false;
    private bool isSandboxHidden = false;
    private int activeManualRow = -1;
    private float pulseClock = 0f;

    /// <summary>
    /// Membangun seluruh struktur Pod Tabel Kebenaran 2x2 secara sinkron (dipanggil dari GateNode.Awake()).
    /// </summary>
    public void Initialize(Mesh sharedTorusMesh)
    {
        if (isInitialized)
        {
            return;
        }

        ownerGateNode = GetComponent<GateNode>();
        trayMetalMat = CreateOpaqueSciFiMaterial(TrayBodyColor, 0.24f, 0.22f, enableEmission: false);
        trayTrimMat = CreateOpaqueSciFiMaterial(TrayTrimColor, 0.30f, 0.26f, enableEmission: false);
        emissiveSharedMat = CreateOpaqueSciFiMaterial(Color.white, 0.32f, 0.12f, enableEmission: true);
        emissiveHasEmissionProp = emissiveSharedMat != null && emissiveSharedMat.HasProperty(EmissionColorPropId);
        mpb = new MaterialPropertyBlock();

        podRootObj = new GameObject("OutputTruthPod3D");
        podRootObj.transform.SetParent(transform, false);
        podRootObj.transform.localPosition = CardModeLocalPos;
        podRootObj.transform.localRotation = CardModeLocalRot;
        podRootObj.transform.localScale = Vector3.one;

        Transform root = podRootObj.transform;

        // Rangka nampan gelap simetris + lis tipis
        frameRenderers[0] = CreatePart("Pod_TrayBody", PrimitiveType.Cube, root,
            Vector3.zero, Vector3.zero, new Vector3(0.504f, 0.018f, 0.242f), trayMetalMat);
        frameRenderers[1] = CreatePart("Pod_TrimTop", PrimitiveType.Cube, root,
            new Vector3(0f, 0.008f, 0.118f), Vector3.zero, new Vector3(0.508f, 0.007f, 0.008f), trayTrimMat);
        frameRenderers[2] = CreatePart("Pod_TrimBottom", PrimitiveType.Cube, root,
            new Vector3(0f, 0.008f, -0.118f), Vector3.zero, new Vector3(0.508f, 0.007f, 0.008f), trayTrimMat);
        frameRenderers[3] = CreatePart("Pod_CrossDividerV", PrimitiveType.Cube, root,
            new Vector3(0f, 0.007f, 0f), Vector3.zero, new Vector3(0.007f, 0.007f, 0.226f), trayTrimMat);
        frameRenderers[4] = CreatePart("Pod_CrossDividerH", PrimitiveType.Cube, root,
            new Vector3(0f, 0.007f, 0f), Vector3.zero, new Vector3(0.490f, 0.007f, 0.007f), trayTrimMat);

        for (int row = 0; row < CellCount; row++)
        {
            BuildCell(row, root);
        }

        isInitialized = true;
        ApplyAllCellVisuals();
        UpdateRootVisibility();
    }

    public void SetTrackedVisible(bool tracked)
    {
        if (isNodeTracked == tracked)
        {
            return;
        }

        isNodeTracked = tracked;
        UpdateRootVisibility();
    }

    public void ConfigureLevelTargets(bool[] expectedTruthTable, bool isSandbox)
    {
        isSandboxHidden = isSandbox;

        for (int i = 0; i < CellCount; i++)
        {
            expectedTargets[i] = !isSandbox
                                 && expectedTruthTable != null
                                 && i < expectedTruthTable.Length
                                 && expectedTruthTable[i];
        }

        ResetDiscovery();
        UpdateRootVisibility();
    }

    public void ResetDiscovery()
    {
        for (int i = 0; i < CellCount; i++)
        {
            cellStatuses[i] = OutputPodCellStatus.Untested;
        }

        if (isInitialized)
        {
            ApplyAllCellVisuals();
        }
    }

    public void SetActiveManualRow(int rowIndex)
    {
        int clamped = (rowIndex >= 0 && rowIndex < CellCount) ? rowIndex : -1;
        if (clamped == activeManualRow)
        {
            return;
        }

        int prev = activeManualRow;
        activeManualRow = clamped;

        if (!isInitialized)
        {
            return;
        }

        if (prev >= 0 && prev < CellCount)
        {
            ApplySingleCellVisual(prev);
        }
        if (activeManualRow >= 0 && activeManualRow < CellCount)
        {
            ApplySingleCellVisual(activeManualRow);
        }
    }

    public void RecordLiveDiscovery(int rowIndex, bool measuredOutput)
    {
        if (isSandboxHidden || rowIndex < 0 || rowIndex >= CellCount)
        {
            return;
        }

        OutputPodCellStatus nextStatus = (measuredOutput == expectedTargets[rowIndex])
            ? OutputPodCellStatus.Passed
            : OutputPodCellStatus.Failed;

        if (cellStatuses[rowIndex] != nextStatus)
        {
            cellStatuses[rowIndex] = nextStatus;
            if (isInitialized)
            {
                ApplySingleCellVisual(rowIndex);
            }
        }
    }

    public void SetVerificationRowScanning(int rowIndex)
    {
        if (isSandboxHidden || rowIndex < 0 || rowIndex >= CellCount)
        {
            return;
        }

        cellStatuses[rowIndex] = OutputPodCellStatus.Scanning;
        if (isInitialized)
        {
            ApplySingleCellVisual(rowIndex);
        }
    }

    public void SetVerificationRowResult(int rowIndex, bool passed)
    {
        if (isSandboxHidden || rowIndex < 0 || rowIndex >= CellCount)
        {
            return;
        }

        cellStatuses[rowIndex] = passed ? OutputPodCellStatus.Passed : OutputPodCellStatus.Failed;
        if (isInitialized)
        {
            ApplySingleCellVisual(rowIndex);
        }
    }

    /// <summary>
    /// Memperbarui orientasi camera-facing (khusus Mode Udara) & animasi denyut status sel. Zero-GC.
    /// </summary>
    public void TickAnimation(float deltaTime)
    {
        if (!isInitialized || podRootObj == null || !podRootObj.activeSelf)
        {
            return;
        }

        UpdatePodSpatialOrientation();

        pulseClock += deltaTime;

        for (int i = 0; i < CellCount; i++)
        {
            OutputPodCellStatus status = cellStatuses[i];
            if (status == OutputPodCellStatus.Scanning)
            {
                float t = 0.5f + (0.5f * Mathf.Sin(pulseClock * 16f));
                Color c = Color.Lerp(StatusScanSkyColor, StatusScanWhiteColor, t);
                SetRendererColor(cellPlateRenderers[i], c, c * 1.20f);
                SetRendererColor(cellRimRenderers[i], StatusScanWhiteColor, StatusScanWhiteColor * 1.35f);
                if (cellRootTransforms[i] != null)
                {
                    float s = 1.07f + (0.05f * t);
                    cellRootTransforms[i].localScale = new Vector3(s, 1f, s);
                }
            }
            else if (status == OutputPodCellStatus.Failed)
            {
                float t = 0.5f + (0.5f * Mathf.Sin(pulseClock * 8.5f));
                Color c = Color.Lerp(StatusFailedBaseColor, StatusFailedPulseColor, t);
                SetRendererColor(cellPlateRenderers[i], c, c * (0.75f + (0.35f * t)));
            }
        }
    }

    private void UpdatePodSpatialOrientation()
    {
        bool isAirMode = ownerGateNode != null && ownerGateNode.useExternalSpatialPose;
        if (isAirMode)
        {
            Camera cam = ResolveMainCamera();
            if (cam != null)
            {
                Transform camT = cam.transform;
                // Sejajarkan rata menghadap kamera (local +Y = -camForward, local +Z = camUp, local +X = camRight)
                podRootObj.transform.position = transform.position + (camT.up * -0.225f) - (camT.forward * 0.22f);
                podRootObj.transform.rotation = Quaternion.LookRotation(camT.up, -camT.forward);
                podRootObj.transform.localScale = Vector3.one * 1.16f;
                wasAirModePose = true;
            }
        }
        else if (wasAirModePose)
        {
            // Pulihkan presisi mount kartu fisik Mode Kartu v5 (zero-regression)
            podRootObj.transform.localPosition = CardModeLocalPos;
            podRootObj.transform.localRotation = CardModeLocalRot;
            podRootObj.transform.localScale = Vector3.one;
            wasAirModePose = false;
        }
    }

    private Camera ResolveMainCamera()
    {
        if (cachedMainCamera != null && cachedMainCamera.isActiveAndEnabled)
        {
            return cachedMainCamera;
        }
        cachedMainCamera = Camera.main;
        return cachedMainCamera;
    }

    /// <summary>
    /// Mengganti 3 shared material Pod ke custom hardware shader 1-pass sambil menyimpan snapshot shader bawaan
    /// untuk pemulihan penuh Mode Kartu v5. Sel biner 00/01/10/11 tetap jernih (rim = 0 pada pelat sel).
    /// </summary>
    public void ApplyExternalHardwareShader(Shader customShader)
    {
        if (!isInitialized || customShader == null || !customShader.isSupported)
        {
            return;
        }

        if (!hasExternalHardwareShader)
        {
            savedTrayMetalShader = trayMetalMat != null ? trayMetalMat.shader : null;
            savedTrayMetalQueue = trayMetalMat != null ? trayMetalMat.renderQueue : -1;
            savedTrayTrimShader = trayTrimMat != null ? trayTrimMat.shader : null;
            savedTrayTrimQueue = trayTrimMat != null ? trayTrimMat.renderQueue : -1;
            savedEmissiveShader = emissiveSharedMat != null ? emissiveSharedMat.shader : null;
            savedEmissiveQueue = emissiveSharedMat != null ? emissiveSharedMat.renderQueue : -1;
        }

        ApplyShaderToMat(trayMetalMat, customShader, clearEmission: true);
        ApplyShaderToMat(trayTrimMat, customShader, clearEmission: true);
        ApplyShaderToMat(emissiveSharedMat, customShader, clearEmission: false);
        if (emissiveSharedMat != null && emissiveSharedMat.HasProperty(RimIntensityPropId))
        {
            emissiveSharedMat.SetFloat(RimIntensityPropId, 0f);
        }

        emissiveHasEmissionProp = emissiveSharedMat != null && emissiveSharedMat.HasProperty(EmissionColorPropId);
        hasExternalHardwareShader = true;

        if (hasExternalFrameRim)
        {
            ApplyFrameRimHighlight();
        }
        ApplyAllCellVisuals();
    }

    /// <summary>
    /// Memulihkan shader bawaan Mode Kartu v5 pada Pod 2x2 dan membersihkan MPB rangka.
    /// </summary>
    public void RestoreDefaultHardwareShaders()
    {
        if (!isInitialized)
        {
            return;
        }

        if (hasExternalHardwareShader)
        {
            RestoreMatShader(trayMetalMat, savedTrayMetalShader, savedTrayMetalQueue);
            RestoreMatShader(trayTrimMat, savedTrayTrimShader, savedTrayTrimQueue);
            RestoreMatShader(emissiveSharedMat, savedEmissiveShader, savedEmissiveQueue);
            emissiveHasEmissionProp = emissiveSharedMat != null && emissiveSharedMat.HasProperty(EmissionColorPropId);
            hasExternalHardwareShader = false;
        }

        hasExternalFrameRim = false;
        externalFrameRimIntensity = -1f;

        for (int i = 0; i < frameRenderers.Length; i++)
        {
            if (frameRenderers[i] != null)
            {
                frameRenderers[i].SetPropertyBlock(null);
            }
        }

        ApplyAllCellVisuals();
    }

    /// <summary>
    /// Menerapkan Fresnel Rim Glow halus khusus pada rangka logam Pod (Linear eksplisit, Zero-GC).
    /// </summary>
    public void SetExternalFrameRimHighlight(Color linearRimColor, float rimPower, float rimIntensity)
    {
        if (!isInitialized || !hasExternalHardwareShader)
        {
            return;
        }

        if (hasExternalFrameRim
            && Mathf.Abs(externalFrameRimColorLinear.r - linearRimColor.r) < 0.005f
            && Mathf.Abs(externalFrameRimColorLinear.g - linearRimColor.g) < 0.005f
            && Mathf.Abs(externalFrameRimColorLinear.b - linearRimColor.b) < 0.005f
            && Mathf.Abs(externalFrameRimPower - rimPower) < 0.02f
            && Mathf.Abs(externalFrameRimIntensity - rimIntensity) < 0.015f)
        {
            return;
        }

        hasExternalFrameRim = true;
        externalFrameRimColorLinear = linearRimColor;
        externalFrameRimPower = rimPower;
        externalFrameRimIntensity = rimIntensity;
        ApplyFrameRimHighlight();
    }

    private void ApplyFrameRimHighlight()
    {
        if (mpb == null)
        {
            return;
        }

        mpb.Clear();
        mpb.SetColor(RimColorPropId, externalFrameRimColorLinear);
        mpb.SetFloat(RimPowerPropId, externalFrameRimPower);
        mpb.SetFloat(RimIntensityPropId, externalFrameRimIntensity);

        for (int i = 0; i < frameRenderers.Length; i++)
        {
            if (frameRenderers[i] != null)
            {
                frameRenderers[i].SetPropertyBlock(mpb);
            }
        }
    }

    private static void ApplyShaderToMat(Material mat, Shader customShader, bool clearEmission)
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

    private static void RestoreMatShader(Material mat, Shader originalShader, int originalQueue)
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
        DestroyMat(ref trayMetalMat);
        DestroyMat(ref trayTrimMat);
        DestroyMat(ref emissiveSharedMat);
        mpb = null;
        ownerGateNode = null;
        cachedMainCamera = null;
        savedTrayMetalShader = null;
        savedTrayTrimShader = null;
        savedEmissiveShader = null;
    }

    // =========================================================================
    // CONSTRUCTION & ZERO-GC VISUAL UPDATES
    // =========================================================================

    private void BuildCell(int row, Transform parentRoot)
    {
        Vector2 offset = CellOffsetsXZ[row];
        GameObject cellObj = new GameObject($"PodCell_{row}");
        cellObj.transform.SetParent(parentRoot, false);
        cellObj.transform.localPosition = new Vector3(offset.x, 0.011f, offset.y);
        cellObj.transform.localRotation = Quaternion.identity;
        cellObj.transform.localScale = Vector3.one;
        cellRootTransforms[row] = cellObj.transform;

        Transform cellT = cellObj.transform;

        // 1. Bingkai tepi kotak (sian terang saat kombinasi aktif, abu rapi saat idle)
        cellRimRenderers[row] = CreatePart($"Cell_{row}_Rim", PrimitiveType.Cube, cellT,
            new Vector3(0f, 0.001f, 0f), Vector3.zero, new Vector3(0.224f, 0.005f, 0.096f), emissiveSharedMat);

        // 2. Pelat latar status kotak (abu gelap = belum diuji, hijau = lolos, merah = gagal)
        cellPlateRenderers[row] = CreatePart($"Cell_{row}_Plate", PrimitiveType.Cube, cellT,
            new Vector3(0f, 0.006f, 0f), Vector3.zero, new Vector3(0.200f, 0.006f, 0.078f), emissiveSharedMat);

        // 3. Label biner 2 digit tegas & rata tengah: "00", "01", "10", "11"
        GameObject lblObj = new GameObject($"Cell_{row}_BinaryLabel");
        lblObj.transform.SetParent(cellT, false);
        lblObj.transform.localPosition = new Vector3(0f, 0.014f, 0f);
        lblObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        lblObj.transform.localScale = Vector3.one;

        TextMeshPro tmp = lblObj.AddComponent<TextMeshPro>();
        tmp.text = BinaryRowLabels[row];
        tmp.fontSize = 0.56f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.color = LabelUntestedTextColor;
        tmp.sortingOrder = 18;
        if (tmp.rectTransform != null)
        {
            tmp.rectTransform.sizeDelta = new Vector2(0.195f, 0.074f);
        }
        cellBinaryLabels[row] = tmp;
    }

    private void ApplyAllCellVisuals()
    {
        for (int i = 0; i < CellCount; i++)
        {
            ApplySingleCellVisual(i);
        }
    }

    private void ApplySingleCellVisual(int row)
    {
        OutputPodCellStatus status = cellStatuses[row];
        bool isManualFocused = (row == activeManualRow) && (status != OutputPodCellStatus.Scanning);

        Color plateColor;
        Color plateEmit;
        switch (status)
        {
            case OutputPodCellStatus.Passed:
                plateColor = StatusPassedColor;
                plateEmit = StatusPassedEmit;
                break;
            case OutputPodCellStatus.Failed:
                plateColor = StatusFailedBaseColor;
                plateEmit = StatusFailedBaseColor * 0.90f;
                break;
            case OutputPodCellStatus.Scanning:
                plateColor = StatusScanSkyColor;
                plateEmit = StatusScanWhiteColor * 1.15f;
                break;
            default:
                plateColor = StatusUntestedColor;
                plateEmit = StatusUntestedEmit;
                break;
        }

        SetRendererColor(cellPlateRenderers[row], plateColor, plateEmit);

        if (status == OutputPodCellStatus.Scanning)
        {
            SetRendererColor(cellRimRenderers[row], StatusScanWhiteColor, StatusScanWhiteColor * 1.30f);
        }
        else if (isManualFocused)
        {
            SetRendererColor(cellRimRenderers[row], RimActiveCyanColor, RimActiveCyanEmit);
        }
        else
        {
            SetRendererColor(cellRimRenderers[row], RimIdleColor, RimIdleEmit);
        }

        if (cellBinaryLabels[row] != null)
        {
            bool brightText = isManualFocused || status == OutputPodCellStatus.Passed || status == OutputPodCellStatus.Failed;
            cellBinaryLabels[row].color = brightText ? LabelActiveTextColor : LabelUntestedTextColor;
        }

        if (cellRootTransforms[row] != null)
        {
            float scaleXZ = status == OutputPodCellStatus.Scanning ? 1.09f : (isManualFocused ? 1.05f : 1.00f);
            cellRootTransforms[row].localScale = new Vector3(scaleXZ, 1f, scaleXZ);
        }
    }

    private void UpdateRootVisibility()
    {
        if (podRootObj == null)
        {
            return;
        }

        bool shouldShow = isNodeTracked && !isSandboxHidden;
        if (podRootObj.activeSelf != shouldShow)
        {
            podRootObj.SetActive(shouldShow);
        }
    }

    private void SetRendererColor(Renderer rend, Color baseColor, Color emissionColor)
    {
        if (rend == null || mpb == null)
        {
            return;
        }

        mpb.Clear();
        mpb.SetColor(ColorPropId, baseColor);
        if (emissiveHasEmissionProp)
        {
            mpb.SetColor(EmissionColorPropId, emissionColor);
        }
        if (hasExternalHardwareShader)
        {
            mpb.SetFloat(RimIntensityPropId, 0f);
        }
        rend.SetPropertyBlock(mpb);
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

    private static void DestroyMat(ref Material mat)
    {
        if (mat != null)
        {
            Destroy(mat);
            mat = null;
        }
    }
}
