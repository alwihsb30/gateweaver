using UnityEngine;

public enum OutputPodCellStatus
{
    Untested = 0,
    Scanning = 1,
    Passed = 2,
    Failed = 3
}

/// <summary>
/// Modul Pod LED Ikon 2x2 tanpa angka biner mentah pada Card_Output (GateWeaver AR v7.0 Fase 1).
/// - Ditempatkan miring menghadap kamera di bibir depan alas AR_BaseTile3D (0.48 x 0.22, muat di dalam tapak 0.56 kartu fisik,
///   tepi bawah Y > 0 aman dari meja Mode Kartu & tepi atas-belakang bebas dari seluruh 5 diorama maupun Label_Status3D).
/// - Pemisahan visual tegas:
///   * Kiri sel: 2 dot vertikal kondisi input (Atas = Input A Sian, Bawah = Input B Kuning).
///   * Kanan sel: BENTUK target misi (Bola padat terang = Target AKTIF, Cincin berlubang gelap = Target MATI).
///   * Pelat latar sel: WARNA status eksperimen/uji (Abu gelap = Belum diuji, Hijau = Cocok target, Merah = Salah,
///     Putih-Biru elektrik berdenyut = Sedang dipindai — bebas bentrok dengan kuning Input B).
/// - Otomatis tersembunyi penuh di Mode Sandbox dan bebas alokasi GC (2 shared material + 1 MaterialPropertyBlock).
/// </summary>
public sealed class OutputTruthPodVisual : MonoBehaviour
{
    private const int CellCount = 4;

    private static readonly int ColorPropId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorPropId = Shader.PropertyToID("_EmissionColor");
    private static readonly int GlossinessPropId = Shader.PropertyToID("_Glossiness");
    private static readonly int MetallicPropId = Shader.PropertyToID("_Metallic");

    // Koordinat sel 2x2 pada bidang lokal +Y tray (lebar total tray 0.48, tinggi 0.22)
    private static readonly Vector2[] CellOffsetsXZ =
    {
        new Vector2(-0.112f,  0.048f), // Row 0: A=0, B=0 (Kiri Atas)
        new Vector2( 0.112f,  0.048f), // Row 1: A=0, B=1 (Kanan Atas)
        new Vector2(-0.112f, -0.048f), // Row 2: A=1, B=0 (Kiri Bawah)
        new Vector2( 0.112f, -0.048f)  // Row 3: A=1, B=1 (Kanan Bawah)
    };

    // Palet warna ortogonal (bebas bentrok antara Input A Sian, Input B Kuning, Target, & Status)
    private static readonly Color TrayBodyColor = new Color(0.11f, 0.14f, 0.19f, 1f);
    private static readonly Color TrayTrimColor = new Color(0.42f, 0.50f, 0.60f, 1f);

    private static readonly Color InputADotOnColor = new Color(0.18f, 0.85f, 1.00f, 1f);
    private static readonly Color InputADotOnEmit = new Color(0.16f, 0.80f, 1.00f, 1f) * 1.20f;
    private static readonly Color InputADotOffColor = new Color(0.12f, 0.17f, 0.23f, 1f);
    private static readonly Color InputADotOffEmit = new Color(0.03f, 0.05f, 0.08f, 1f);

    private static readonly Color InputBDotOnColor = new Color(1.00f, 0.84f, 0.22f, 1f);
    private static readonly Color InputBDotOnEmit = new Color(1.00f, 0.80f, 0.18f, 1f) * 1.20f;
    private static readonly Color InputBDotOffColor = new Color(0.17f, 0.16f, 0.12f, 1f);
    private static readonly Color InputBDotOffEmit = new Color(0.05f, 0.04f, 0.02f, 1f);

    private static readonly Color TargetOnOrbColor = new Color(0.92f, 1.00f, 0.98f, 1f);
    private static readonly Color TargetOnOrbEmit = new Color(0.84f, 1.00f, 0.96f, 1f) * 1.25f;
    private static readonly Color TargetOffRingColor = new Color(0.68f, 0.78f, 0.88f, 1f);
    private static readonly Color TargetOffRingEmit = new Color(0.32f, 0.42f, 0.54f, 1f) * 0.90f;

    private static readonly Color StatusUntestedColor = new Color(0.10f, 0.14f, 0.21f, 1f);
    private static readonly Color StatusUntestedEmit = new Color(0.03f, 0.05f, 0.08f, 1f);
    private static readonly Color StatusPassedColor = new Color(0.09f, 0.72f, 0.40f, 1f);
    private static readonly Color StatusPassedEmit = new Color(0.08f, 0.68f, 0.36f, 1f) * 0.95f;
    private static readonly Color StatusFailedBaseColor = new Color(0.88f, 0.20f, 0.17f, 1f);
    private static readonly Color StatusFailedPulseColor = new Color(1.00f, 0.44f, 0.34f, 1f);
    private static readonly Color StatusScanSkyColor = new Color(0.46f, 0.82f, 1.00f, 1f);
    private static readonly Color StatusScanWhiteColor = new Color(0.98f, 1.00f, 1.00f, 1f);

    private static readonly Color RimIdleColor = new Color(0.20f, 0.27f, 0.36f, 1f);
    private static readonly Color RimIdleEmit = new Color(0.05f, 0.07f, 0.10f, 1f);
    private static readonly Color RimActiveFocusColor = new Color(0.92f, 0.96f, 1.00f, 1f);
    private static readonly Color RimActiveFocusEmit = new Color(0.84f, 0.92f, 1.00f, 1f) * 1.15f;

    private GameObject podRootObj;
    private Material trayMetalMat;
    private Material trayTrimMat;
    private Material emissiveSharedMat;
    private bool emissiveHasEmissionProp = false;
    private MaterialPropertyBlock mpb;

    private readonly Transform[] cellRootTransforms = new Transform[CellCount];
    private readonly Renderer[] cellPlateRenderers = new Renderer[CellCount];
    private readonly Renderer[] cellRimRenderers = new Renderer[CellCount];
    private readonly GameObject[] targetOnOrbObjs = new GameObject[CellCount];
    private readonly GameObject[] targetOffRingObjs = new GameObject[CellCount];

    private readonly bool[] expectedTargets = new bool[CellCount];
    private readonly OutputPodCellStatus[] cellStatuses = new OutputPodCellStatus[CellCount];

    private bool isInitialized = false;
    private bool isNodeTracked = false;
    private bool isSandboxHidden = false;
    private int activeManualRow = -1;
    private float pulseClock = 0f;

    /// <summary>
    /// Membangun seluruh struktur Pod LED 2x2 secara sinkron (dipanggil dari GateNode.Awake()).
    /// </summary>
    public void Initialize(Mesh sharedTorusMesh)
    {
        if (isInitialized)
        {
            return;
        }

        trayMetalMat = CreateOpaqueSciFiMaterial(TrayBodyColor, 0.24f, 0.22f, enableEmission: false);
        trayTrimMat = CreateOpaqueSciFiMaterial(TrayTrimColor, 0.30f, 0.26f, enableEmission: false);
        emissiveSharedMat = CreateOpaqueSciFiMaterial(Color.white, 0.34f, 0.12f, enableEmission: true);
        emissiveHasEmissionProp = emissiveSharedMat != null && emissiveSharedMat.HasProperty(EmissionColorPropId);
        mpb = new MaterialPropertyBlock();

        // Posisi terverifikasi: Z=-0.34, Y=0.085, Euler(-48,0,0) -> tepi bawah Y=+0.003 (di atas meja),
        // tepi atas-belakang Z=-0.266 (bersih dari 5 diorama & Label_Status3D), lebar 0.48 (di dalam tapak 0.56).
        podRootObj = new GameObject("OutputTruthPod3D");
        podRootObj.transform.SetParent(transform, false);
        podRootObj.transform.localPosition = new Vector3(0f, 0.085f, -0.34f);
        podRootObj.transform.localRotation = Quaternion.Euler(-48f, 0f, 0f);
        podRootObj.transform.localScale = Vector3.one;

        Transform root = podRootObj.transform;

        // Rangka nampan gelap + lis baja sci-fi
        CreatePart("Pod_TrayBody", PrimitiveType.Cube, root,
            Vector3.zero, Vector3.zero, new Vector3(0.480f, 0.020f, 0.220f), trayMetalMat);
        CreatePart("Pod_TrimTop", PrimitiveType.Cube, root,
            new Vector3(0f, 0.009f, 0.107f), Vector3.zero, new Vector3(0.484f, 0.008f, 0.010f), trayTrimMat);
        CreatePart("Pod_TrimBottom", PrimitiveType.Cube, root,
            new Vector3(0f, 0.009f, -0.107f), Vector3.zero, new Vector3(0.484f, 0.008f, 0.010f), trayTrimMat);
        CreatePart("Pod_CrossDividerV", PrimitiveType.Cube, root,
            new Vector3(0f, 0.008f, 0f), Vector3.zero, new Vector3(0.008f, 0.008f, 0.204f), trayTrimMat);
        CreatePart("Pod_CrossDividerH", PrimitiveType.Cube, root,
            new Vector3(0f, 0.008f, 0f), Vector3.zero, new Vector3(0.464f, 0.008f, 0.008f), trayTrimMat);

        for (int row = 0; row < CellCount; row++)
        {
            BuildCell(row, root, sharedTorusMesh);
        }

        isInitialized = true;
        ApplyAllCellVisuals();
        UpdateRootVisibility();
    }

    /// <summary>
    /// Menyinkronkan visibilitas Pod dengan status tracking Card_Output (otomatis selalu mati di Sandbox).
    /// </summary>
    public void SetTrackedVisible(bool tracked)
    {
        if (isNodeTracked == tracked)
        {
            return;
        }

        isNodeTracked = tracked;
        UpdateRootVisibility();
    }

    /// <summary>
    /// Mengatur target 4 kombinasi untuk level aktif dan langsung menyembunyikan Pod bila masuk Mode Sandbox.
    /// </summary>
    public void ConfigureLevelTargets(bool[] expectedTruthTable, bool isSandbox)
    {
        isSandboxHidden = isSandbox;

        for (int i = 0; i < CellCount; i++)
        {
            bool targetBit = !isSandbox
                             && expectedTruthTable != null
                             && i < expectedTruthTable.Length
                             && expectedTruthTable[i];
            expectedTargets[i] = targetBit;

            if (targetOnOrbObjs[i] != null && targetOnOrbObjs[i].activeSelf != targetBit)
            {
                targetOnOrbObjs[i].SetActive(targetBit);
            }

            if (targetOffRingObjs[i] != null && targetOffRingObjs[i].activeSelf == targetBit)
            {
                targetOffRingObjs[i].SetActive(!targetBit);
            }
        }

        ResetDiscovery();
        UpdateRootVisibility();
    }

    /// <summary>
    /// Menghapus seluruh status eksperimen/uji ke kondisi belum teruji (dipanggil saat topologi/level/reset berubah).
    /// </summary>
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

    /// <summary>
    /// Menyorot sel kombinasi (A,B) yang sedang aktif sesuai posisi tuas manual pemain.
    /// </summary>
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

    /// <summary>
    /// Mencatat hasil eksperimen langsung untuk kombinasi input yang sedang aktif saat rangkaian tersambung lengkap.
    /// </summary>
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
    /// Menganimasikan denyut halus pada sel yang sedang dipindai (Putih-Biru) atau gagal (Merah). Zero-GC.
    /// </summary>
    public void TickAnimation(float deltaTime)
    {
        if (!isInitialized || podRootObj == null || !podRootObj.activeSelf)
        {
            return;
        }

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
                    float s = 1.08f + (0.06f * t);
                    cellRootTransforms[i].localScale = new Vector3(s, 1f, s);
                }
            }
            else if (status == OutputPodCellStatus.Failed)
            {
                float t = 0.5f + (0.5f * Mathf.Sin(pulseClock * 9.5f));
                Color c = Color.Lerp(StatusFailedBaseColor, StatusFailedPulseColor, t);
                SetRendererColor(cellPlateRenderers[i], c, c * (0.75f + (0.40f * t)));
            }
        }
    }

    private void OnDestroy()
    {
        DestroyMat(ref trayMetalMat);
        DestroyMat(ref trayTrimMat);
        DestroyMat(ref emissiveSharedMat);
        mpb = null;
    }

    // =========================================================================
    // CONSTRUCTION & ZERO-GC VISUAL UPDATES
    // =========================================================================

    private void BuildCell(int row, Transform parentRoot, Mesh sharedTorusMesh)
    {
        Vector2 offset = CellOffsetsXZ[row];
        GameObject cellObj = new GameObject($"PodCell_{row}");
        cellObj.transform.SetParent(parentRoot, false);
        cellObj.transform.localPosition = new Vector3(offset.x, 0.011f, offset.y);
        cellObj.transform.localRotation = Quaternion.identity;
        cellObj.transform.localScale = Vector3.one;
        cellRootTransforms[row] = cellObj.transform;

        Transform cellT = cellObj.transform;

        // 1. Bingkai sorot sel (sedikit lebih lebar dari pelat status)
        cellRimRenderers[row] = CreatePart($"Cell_{row}_Rim", PrimitiveType.Cube, cellT,
            new Vector3(0f, 0.001f, 0f), Vector3.zero, new Vector3(0.214f, 0.005f, 0.088f), emissiveSharedMat);

        // 2. Pelat latar status hasil uji (Untested / Passed Hijau / Failed Merah / Scanning Putih-Biru)
        cellPlateRenderers[row] = CreatePart($"Cell_{row}_Plate", PrimitiveType.Cube, cellT,
            new Vector3(0f, 0.005f, 0f), Vector3.zero, new Vector3(0.198f, 0.006f, 0.074f), emissiveSharedMat);

        // 3. Kiri sel: 2 dot kondisi input vertikal (Atas = Input A Sian, Bawah = Input B Kuning)
        bool bitA = (row == 2 || row == 3);
        bool bitB = (row == 1 || row == 3);

        Renderer dotA = CreatePart($"Cell_{row}_DotA", PrimitiveType.Sphere, cellT,
            new Vector3(-0.064f, 0.014f, 0.019f), Vector3.zero, Vector3.one * (bitA ? 0.029f : 0.022f), emissiveSharedMat);
        SetRendererColor(dotA, bitA ? InputADotOnColor : InputADotOffColor, bitA ? InputADotOnEmit : InputADotOffEmit);

        Renderer dotB = CreatePart($"Cell_{row}_DotB", PrimitiveType.Sphere, cellT,
            new Vector3(-0.064f, 0.014f, -0.019f), Vector3.zero, Vector3.one * (bitB ? 0.029f : 0.022f), emissiveSharedMat);
        SetRendererColor(dotB, bitB ? InputBDotOnColor : InputBDotOffColor, bitB ? InputBDotOnEmit : InputBDotOffEmit);

        // Pemisah tipis antara ikon input dan ikon target
        CreatePart($"Cell_{row}_Arrow", PrimitiveType.Cube, cellT,
            new Vector3(-0.014f, 0.011f, 0f), Vector3.zero, new Vector3(0.028f, 0.005f, 0.008f), trayTrimMat);

        // 4. Kanan sel: Bentuk Target Misi (Bola padat menyala = Target 1, Cincin kosong gelap = Target 0)
        Renderer onOrbRend = CreatePart($"Cell_{row}_TargetOnOrb", PrimitiveType.Sphere, cellT,
            new Vector3(0.048f, 0.018f, 0f), Vector3.zero, Vector3.one * 0.044f, emissiveSharedMat);
        SetRendererColor(onOrbRend, TargetOnOrbColor, TargetOnOrbEmit);
        targetOnOrbObjs[row] = onOrbRend != null ? onOrbRend.gameObject : null;

        GameObject offRingObj = CreateMeshPart($"Cell_{row}_TargetOffRing", sharedTorusMesh, cellT,
            new Vector3(0.048f, 0.014f, 0f), Vector3.zero, Vector3.one * 0.025f, emissiveSharedMat, out Renderer offRingRend);
        SetRendererColor(offRingRend, TargetOffRingColor, TargetOffRingEmit);
        targetOffRingObjs[row] = offRingObj;
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
            SetRendererColor(cellRimRenderers[row], RimActiveFocusColor, RimActiveFocusEmit);
        }
        else
        {
            SetRendererColor(cellRimRenderers[row], RimIdleColor, RimIdleEmit);
        }

        if (cellRootTransforms[row] != null)
        {
            float scaleXZ = status == OutputPodCellStatus.Scanning ? 1.12f : (isManualFocused ? 1.07f : 1.00f);
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

    private static GameObject CreateMeshPart(
        string name,
        Mesh mesh,
        Transform parent,
        Vector3 localPos,
        Vector3 localEuler,
        Vector3 localScale,
        Material sharedMat,
        out Renderer createdRenderer)
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
        if (sharedMat != null)
        {
            mr.sharedMaterial = sharedMat;
        }

        createdRenderer = mr;
        return obj;
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
