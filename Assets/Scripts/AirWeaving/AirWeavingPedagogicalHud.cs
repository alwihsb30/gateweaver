using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Visual Pedagogis Informatika (Fase E) Hybrid 2 Pita Horizontal 2D Overlay + 3D World-Space khusus Mode Udara
/// (GateWeaver AR v8.0 Tahap 10):
/// 1. Pita Horizontal Atas (tepat di bawah Top HUD bar, tinggi 54px): Abstraksi variabel dunia nyata A &amp; B,
///    status biner live [0/1], serta grafik gelombang kotak digital (Square Wave) 16-langkah berbasis segmen
///    UnityEngine.UI.Image pra-alokasi Zero-GC + tombol lipat '-' / 'INPUT [+]'.
/// 2. Pita Horizontal Bawah (tepi bawah layar, tinggi 68px): Generator ekspresi Boolean live Zero-GC
///    (StringBuilder + cycle guard visited) + status Output kontekstual + Tabel Kebenaran Horizontal 4 sel
///    sejajar ([0,0], [0,1], [1,0], [1,1]) yang mempertahankan uji aktif 4 kombinasi sakelar (baris belum diuji
///    tetap menampilkan Y:- tanpa OK/X) + tombol lipat '-' / '[+] TABEL'.
/// 3. Area Tengah 3D World-Space: Proyeksi simbol skematik standar ANSI/IEEE Std 91 (AND, OR, NOT, NAND, XOR)
///    di atas setiap gerbang aktif area rakit + lencana nilai biner 0/1 kontras tinggi di setiap ujung port
///    kabel aktif tersambung, ikut membesar secara alami saat di-zoom masuk / didekatkan ke kamera.
/// - Membuka 100% sisi kiri, tengah, &amp; kanan panggung 3D (nol tabrakan dengan pod Input A/B &amp; Output),
///   100% Zero-GC di runtime loop, event-driven, tanpa Collider, raycastTarget=false pada semua elemen non-tombol,
///   ASCII 7-bit murni, otomatis sembunyi saat overlay UI terbuka atau kembali ke Mode Kartu Vuforia v5.
/// </summary>
public sealed class AirWeavingPedagogicalHud : MonoBehaviour
{
    private const float ReferenceWorkbenchDepth = 3.82f;
    private const float ReferenceCanvasWidth = 1920f;
    private const float ReferenceCanvasHeight = 1080f;

    // Geometri 2 Pita Horizontal Ramping (ruang referensi 1920x1080, dalam PedHud_SafeArea)
    private const float StripSideMarginX = 24f;
    private const float TopStripTopOffsetY = -82f;
    private const float TopStripExpandedHeight = 54f;
    private const float TopStripCollapsedWidth = 148f;

    private const float BottomStripBottomMarginY = 12f;
    private const float BottomStripExpandedHeight = 68f;
    private const float BottomStripCollapsedWidth = 158f;
    private const float StripCollapsedHeight = 40f;

    // Parameter Gelombang Kotak 16-langkah horizontal ramping berbasis UnityEngine.UI.Image (Zero-GC)
    private const int WaveStepCount = 16;
    private const int WaveEdgeCount = WaveStepCount - 1;
    private const float WaveHighLocalY = 8.5f;
    private const float WaveLowLocalY = -8.5f;
    private const float WaveStrokeThickness = 2.8f;

    // Pool kapasitas tetap (Zero-GC)
    private const int TruthRowCount = 4;
    private const int MaxGateSymbolSlots = 5;
    private const int MaxPortBadgeSlots = 20;
    private const int MaxBooleanDepth = 10;

    // RenderQueue 3D terkalibrasi untuk Simbol IEEE & Lencana Port 0/1
    private const int QueueIeeeBackdrop = 2990;
    private const int QueueIeeeStroke = 2991;
    private const int QueuePortBadgeBackdrop = 2992;
    private const int QueuePortBadgeBorder = 2993;
    private const int PortBadgeTextSortingOrder = 19;

    // Cached const ASCII strings (0 alokasi string per frame)
    private const string StrZero = "0";
    private const string StrOne = "1";
    private const string StrDash = "-";
    private const string StrOk = "OK";
    private const string StrFailX = "X";
    private const string StrCollapseMinus = "-";
    private const string StrTopCollapsedPill = "INPUT [+]";
    private const string StrBottomCollapsedPill = "[+] TABEL";
    private const string StrHeaderLeft = "VARIABEL INPUT";
    private const string StrHeaderTruthTable = "TABEL KEBENARAN:";
    private const string StrHeaderTruthTableCompact = "TABEL:";
    private const string StrUnconnectedFormula = "Y = -- (BELUM TERSAMBUNG)";
    private const string StrCycleToken = "(SIKLUS)";
    private static readonly string[] StrComboPrefixes = { "[0,0] ", "[0,1] ", "[1,0] ", "[1,1] " };

    // Palet warna Floating Glass v8.0 kontras tinggi (alpha ~0.84)
    private static readonly Color StripGlassBgColor = new Color(0.024f, 0.056f, 0.108f, 0.84f);
    private static readonly Color StripBorderCyanColor = new Color(0.22f, 0.90f, 1.00f, 0.76f);
    private static readonly Color StripInsetBgColor = new Color(0.014f, 0.036f, 0.072f, 0.90f);
    private static readonly Color StripDividerColor = new Color(0.24f, 0.86f, 1.00f, 0.36f);

    private static readonly Color ToggleBtnBgColor = new Color(0.08f, 0.25f, 0.39f, 0.95f);
    private static readonly Color ToggleBtnBorderColor = new Color(0.30f, 0.94f, 1.00f, 0.90f);
    private static readonly Color ToggleBtnTextColor = new Color(0.95f, 0.99f, 1.00f, 1.00f);

    private static readonly Color ChannelABgColor = new Color(0.028f, 0.098f, 0.165f, 0.82f);
    private static readonly Color ChannelBBgColor = new Color(0.112f, 0.088f, 0.030f, 0.82f);
    private static readonly Color InputACyanColor = new Color(0.26f, 0.95f, 1.00f, 1.00f);
    private static readonly Color InputACyanDimColor = new Color(0.20f, 0.64f, 0.78f, 0.95f);
    private static readonly Color InputBGoldColor = new Color(1.00f, 0.86f, 0.26f, 1.00f);
    private static readonly Color InputBGoldDimColor = new Color(0.78f, 0.60f, 0.18f, 0.95f);
    private static readonly Color WaveRailColor = new Color(0.44f, 0.62f, 0.78f, 0.32f);

    private static readonly Color SignalHighColor = new Color(0.24f, 1.00f, 0.76f, 1.00f);
    private static readonly Color SignalLowColor = new Color(1.00f, 0.54f, 0.30f, 1.00f);
    private static readonly Color SignalIdleColor = new Color(0.70f, 0.84f, 0.95f, 0.95f);
    private static readonly Color HeaderTextColor = new Color(0.88f, 0.98f, 1.00f, 1.00f);
    private static readonly Color FormulaReadyColor = new Color(0.95f, 1.00f, 1.00f, 1.00f);
    private static readonly Color FormulaIncompleteColor = new Color(1.00f, 0.84f, 0.36f, 1.00f);
    private static readonly Color FormulaDisconnectedColor = new Color(0.68f, 0.80f, 0.92f, 1.00f);

    private static readonly Color CellIdleBgColor = new Color(0.045f, 0.086f, 0.152f, 0.78f);
    private static readonly Color CellActiveBgColor = new Color(0.075f, 0.275f, 0.425f, 0.92f);
    private static readonly Color CellPassedBgColor = new Color(0.055f, 0.325f, 0.195f, 0.90f);
    private static readonly Color CellFailedBgColor = new Color(0.430f, 0.095f, 0.118f, 0.92f);
    private static readonly Color CellActiveCyanBorderColor = new Color(0.30f, 0.98f, 1.00f, 1.00f);
    private static readonly Color CellActiveRedBorderColor = new Color(1.00f, 0.34f, 0.28f, 1.00f);
    private static readonly Color StatusPassPillBg = new Color(0.08f, 0.54f, 0.29f, 0.96f);
    private static readonly Color StatusFailPillBg = new Color(0.70f, 0.14f, 0.16f, 0.96f);
    private static readonly Color StatusPassTextColor = new Color(0.90f, 1.00f, 0.93f, 1.00f);
    private static readonly Color StatusFailTextColor = new Color(1.00f, 0.92f, 0.90f, 1.00f);
    private static readonly Color CellTextPrimaryColor = new Color(0.95f, 0.99f, 1.00f, 1.00f);
    private static readonly Color CellTextMutedColor = new Color(0.62f, 0.74f, 0.86f, 0.94f);

    private static readonly Color IeeeBackdropColor = new Color(0.020f, 0.048f, 0.095f, 0.84f);
    private static readonly Color PortBadgeBgColor = new Color(0.018f, 0.045f, 0.092f, 0.94f);

    private static readonly ColorBlock HudToggleBtnColors = new ColorBlock
    {
        normalColor = Color.white,
        highlightedColor = new Color(0.92f, 0.98f, 1.00f, 1f),
        pressedColor = new Color(0.74f, 0.84f, 0.92f, 1f),
        selectedColor = Color.white,
        disabledColor = new Color(0.45f, 0.48f, 0.52f, 0.55f),
        colorMultiplier = 1f,
        fadeDuration = 0.08f
    };

    private sealed class SquareWaveUiTrack
    {
        public readonly RectTransform[] stepRects = new RectTransform[WaveStepCount];
        public readonly Image[] stepImages = new Image[WaveStepCount];
        public readonly Image[] edgeImages = new Image[WaveEdgeCount];
    }

    private sealed class TruthCellUi
    {
        public Image bgImage;
        public Image activeBorderImage;
        public TextMeshProUGUI txtSummary;
        public Image statusPillBg;
        public TextMeshProUGUI txtStatus;
    }

    private sealed class GateSymbolSlot
    {
        public GameObject rootObj;
        public Transform rootTransform;
        public LineRenderer bodyLine;
        public LineRenderer extraLine;
        public LineRenderer leadInALine;
        public LineRenderer leadInBLine;
        public LineRenderer leadOutLine;
        public Material bodyMat;
        public Material extraMat;
        public Material leadInAMat;
        public Material leadInBMat;
        public Material leadOutMat;
        public NodeType configuredType = NodeType.InputSource;
        public int lastStateKey = int.MinValue;
    }

    private sealed class PortBadgeSlot
    {
        public GameObject rootObj;
        public Transform rootTransform;
        public LineRenderer borderLine;
        public Material borderMat;
        public TextMeshPro bitText;
        public int lastBit = -1;
    }

    // 2D Screen-Space Overlay HUD Canvas & SafeArea
    private GameObject hudCanvasObj;
    private RectTransform safeAreaRect;
    private Rect lastAppliedSafeArea;
    private int lastScreenW = -1;
    private int lastScreenH = -1;

    // Pita Horizontal Atas (Variabel Input A/B + Square Wave 16-langkah)
    private RectTransform topStripRect;
    private GameObject topStripBodyObj;
    private Button btnToggleTopStrip;
    private RectTransform btnToggleTopRect;
    private TextMeshProUGUI txtToggleTopLabel;
    private TextMeshProUGUI txtInputATitle;
    private TextMeshProUGUI txtInputAState;
    private TextMeshProUGUI txtInputBTitle;
    private TextMeshProUGUI txtInputBState;
    private SquareWaveUiTrack waveTrackA;
    private SquareWaveUiTrack waveTrackB;
    private readonly bool[] waveHistoryA = new bool[WaveStepCount];
    private readonly bool[] waveHistoryB = new bool[WaveStepCount];
    private bool isTopStripCollapsed = false;

    // Pita Horizontal Bawah (Ekspresi Boolean Live + Tabel Kebenaran Horizontal 4 Sel)
    private RectTransform bottomStripRect;
    private GameObject bottomStripBodyObj;
    private Button btnToggleBottomStrip;
    private RectTransform btnToggleBottomRect;
    private TextMeshProUGUI txtToggleBottomLabel;
    private TextMeshProUGUI txtBooleanFormula;
    private TextMeshProUGUI txtOutputContext;
    private TextMeshProUGUI txtTruthTableLabel;
    private readonly TruthCellUi[] truthCells = new TruthCellUi[TruthRowCount];
    private bool isBottomStripCollapsed = false;

    // 3D World-Space Billboard Overlays (5 Simbol IEEE Std 91 + 20 Lencana Port 0/1)
    private GameObject billboardOverlaysRootObj;
    private GameObject gateSymbolsRootObj;
    private GameObject portBadgesRootObj;
    private readonly GateSymbolSlot[] gateSymbolPool = new GateSymbolSlot[MaxGateSymbolSlots];
    private readonly PortBadgeSlot[] portBadgePool = new PortBadgeSlot[MaxPortBadgeSlots];

    // Buffer pra-alokasi Zero-GC
    private readonly Vector3[] ieeePolyBuffer = new Vector3[24];
    private readonly StringBuilder textBuilder = new StringBuilder(128);
    private readonly List<GateNode> formulaVisited = new List<GateNode>(12);

    // Kepemilikan material 3D (100% dibersihkan di OnDestroy)
    private readonly List<Material> ownedMaterials = new List<Material>(48);
    private Material sharedIeeeBackdropMat;
    private Material sharedPortBadgeBgMat;

    private TMP_FontAsset cachedPreferredFont;
    private bool isInitialized = false;
    private bool isHudActive = false;
    private bool waveHistoryInitialized = false;

    // Event-driven dirty-check state keys (Zero-GC)
    private int lastLevelIndex = int.MinValue;
    private int lastSandboxFlag = -1;
    private int lastInputABit = -1;
    private int lastInputBBit = -1;
    private int lastOutputBit = -1;
    private int lastTopologyVersion = int.MinValue;
    private int lastActiveGateCount = -1;
    private int lastActiveWireCount = -1;
    private int lastPassedRowMask = -1;
    private int lastFailedRowMask = -1;
    private int lastCircuitCompleteFlag = -1;
    private int lastVerifyingFlag = -1;

    // Pelacakan lokal sel teruji khusus Mode Sandbox
    private int sandboxTestedRowMask = 0;
    private int sandboxMeasuredHighMask = 0;
    private bool lastFormulaIsComplete = false;

    // Konteks label level aktif (di-refresh hanya saat level/sandbox berubah)
    private string cachedInAName = "INPUT A";
    private string cachedInA0 = "OFF";
    private string cachedInA1 = "ON";
    private string cachedInBName = "INPUT B";
    private string cachedInB0 = "OFF";
    private string cachedInB1 = "ON";
    private string cachedOutName = "OUTPUT";
    private string cachedOut0 = "MATI";
    private string cachedOut1 = "AKTIF";
    private readonly bool[] cachedExpectedTruth = new bool[TruthRowCount];
    private bool cachedHasExpectedTruth = false;

    public bool IsHudActive => isHudActive;
    public bool IsBottomStripExpanded => isHudActive && !isBottomStripCollapsed && hudCanvasObj != null && hudCanvasObj.activeSelf;
    public event Action<bool> OnBottomStripExpandedChanged;

    public void EnsureVisualsCreated(Transform stageRoot, TMP_FontAsset preferredFont = null)
    {
        _ = stageRoot;
        if (preferredFont != null)
        {
            cachedPreferredFont = preferredFont;
        }

        if (isInitialized)
        {
            return;
        }

        BuildAllVisuals();
        isInitialized = true;
        HideImmediate();
    }

    public void ActivateForAirWeaving(
        Transform stageRoot,
        Camera mainCam,
        LevelAndUIManager levelManager,
        GateNode inputANode,
        GateNode inputBNode,
        GateNode outputNode,
        TMP_FontAsset preferredFont = null)
    {
        _ = mainCam;
        EnsureVisualsCreated(stageRoot, preferredFont);
        isHudActive = true;

        ApplySafeAreaIfNeeded(force: true);
        SetHudCanvasVisible(true);
        SetBillboardRootVisible(true);
        ResetTransientHistoriesAndDirtyState();

        int lvlIdx = levelManager != null ? levelManager.currentLevelIndex : 0;
        bool isSandbox = levelManager != null && levelManager.IsSandboxMode;
        RefreshLevelContextCache(levelManager, lvlIdx, isSandbox);
        InitializeWaveHistoryToCurrent(inputANode, inputBNode);

        RefreshTopInputStripVisuals(inputANode, inputBNode);
        RefreshBottomBooleanAndTruthCells(
            isSandbox,
            isCircuitCompleteLive: false,
            isVerifying: false,
            inputANode,
            inputBNode,
            outputNode,
            passedRowMask: 0,
            failedRowMask: 0);
    }

    public void NotifyLevelOrWorkbenchReset(
        LevelAndUIManager levelManager,
        GateNode inputANode,
        GateNode inputBNode,
        GateNode outputNode)
    {
        if (!isInitialized)
        {
            return;
        }

        ResetTransientHistoriesAndDirtyState();
        if (!isHudActive)
        {
            return;
        }

        int lvlIdx = levelManager != null ? levelManager.currentLevelIndex : 0;
        bool isSandbox = levelManager != null && levelManager.IsSandboxMode;
        RefreshLevelContextCache(levelManager, lvlIdx, isSandbox);
        InitializeWaveHistoryToCurrent(inputANode, inputBNode);
        RefreshTopInputStripVisuals(inputANode, inputBNode);
        RefreshBottomBooleanAndTruthCells(
            isSandbox,
            isCircuitCompleteLive: false,
            isVerifying: false,
            inputANode,
            inputBNode,
            outputNode,
            passedRowMask: 0,
            failedRowMask: 0);
        HideAllGateSymbolsAndPortBadges();
    }

    public void HideImmediate()
    {
        isHudActive = false;
        waveHistoryInitialized = false;
        sandboxTestedRowMask = 0;
        sandboxMeasuredHighMask = 0;
        InvalidateDirtyCaches();

        if (!isInitialized)
        {
            return;
        }

        HideAllGateSymbolsAndPortBadges();
        SetHudCanvasVisible(false);
        SetBillboardRootVisible(false);
    }

    /// <summary>
    /// Pembaruan event-driven Pita 2D Atas/Bawah serta sinkronisasi 3D Simbol IEEE &amp; Lencana Port 0/1
    /// dengan zoom alami proporsional. Dipanggil dari AirWeavingController.LateUpdate(). 100% Zero-GC.
    /// </summary>
    public void TickLateVisuals(
        Camera mainCam,
        Transform stageRoot,
        float stageZoomScale,
        bool isAirWeavingActive,
        bool isUiOverlayOpen,
        LevelAndUIManager levelManager,
        CircuitManager circuitManager,
        AirWeavingCableRouter cableRouter,
        GateNode inputANode,
        GateNode inputBNode,
        GateNode outputNode,
        List<GateNode> activePlacedGates,
        int debugPassedRowMask,
        int debugFailedRowMask)
    {
        _ = stageRoot;
        if (!isInitialized)
        {
            return;
        }

        if (!isAirWeavingActive || !isHudActive || mainCam == null)
        {
            SetHudCanvasVisible(false);
            SetBillboardRootVisible(false);
            return;
        }

        // 1. Sembunyikan otomatis seluruh HUD 2D & overlay 3D saat overlay/modal UI terbuka (Menu, Level Selector, Panduan)
        if (isUiOverlayOpen)
        {
            SetHudCanvasVisible(false);
            HideAllGateSymbolsAndPortBadges();
            return;
        }

        SetHudCanvasVisible(true);
        SetBillboardRootVisible(true);
        ApplySafeAreaIfNeeded(force: false);

        // 2. Deteksi perubahan event-driven (level, input A/B, topologi, status 4 sel kebenaran)
        int curLevelIdx = levelManager != null ? levelManager.currentLevelIndex : 0;
        int curSandboxFlag = (levelManager != null && levelManager.IsSandboxMode) ? 1 : 0;
        bool isSandbox = curSandboxFlag == 1;
        bool isVerifying = levelManager != null && levelManager.IsVerifying;
        int curVerifyingFlag = isVerifying ? 1 : 0;
        bool isCircuitCompleteLive = levelManager != null && levelManager.IsCircuitCompleteLive;
        int curCompleteFlag = isCircuitCompleteLive ? 1 : 0;

        int curInABit = (inputANode != null && inputANode.currentOutputValue) ? 1 : 0;
        int curInBBit = (inputBNode != null && inputBNode.currentOutputValue) ? 1 : 0;
        int curOutBit = (outputNode != null && outputNode.isTracked) ? (outputNode.currentOutputValue ? 1 : 0) : -1;

        int curTopoVer = cableRouter != null ? cableRouter.TopologyVersion : 0;
        int curActiveGates = circuitManager != null ? circuitManager.activeGateCardsCount : 0;
        int curWireCount = cableRouter != null ? cableRouter.ActiveWireCount : 0;

        bool levelChanged = curLevelIdx != lastLevelIndex || curSandboxFlag != lastSandboxFlag;
        bool inputsChanged = curInABit != lastInputABit || curInBBit != lastInputBBit;
        bool topologyChanged = curTopoVer != lastTopologyVersion
                               || curActiveGates != lastActiveGateCount
                               || curWireCount != lastActiveWireCount;
        bool outputOrMaskChanged = curOutBit != lastOutputBit
                                   || debugPassedRowMask != lastPassedRowMask
                                   || debugFailedRowMask != lastFailedRowMask
                                   || curCompleteFlag != lastCircuitCompleteFlag
                                   || curVerifyingFlag != lastVerifyingFlag;

        if (levelChanged)
        {
            RefreshLevelContextCache(levelManager, curLevelIdx, isSandbox);
            InitializeWaveHistoryToCurrent(inputANode, inputBNode);
            sandboxTestedRowMask = 0;
            sandboxMeasuredHighMask = 0;
        }
        else if (inputsChanged)
        {
            if (!waveHistoryInitialized)
            {
                InitializeWaveHistoryToCurrent(inputANode, inputBNode);
            }
            else
            {
                PushWaveHistoryStep(curInABit == 1, curInBBit == 1);
            }
        }

        if (topologyChanged)
        {
            sandboxTestedRowMask = 0;
            sandboxMeasuredHighMask = 0;
        }

        if (levelChanged || inputsChanged)
        {
            RefreshTopInputStripVisuals(inputANode, inputBNode);
        }

        if (levelChanged || inputsChanged || topologyChanged || outputOrMaskChanged)
        {
            RefreshBottomBooleanAndTruthCells(
                isSandbox,
                isCircuitCompleteLive,
                isVerifying,
                inputANode,
                inputBNode,
                outputNode,
                debugPassedRowMask,
                debugFailedRowMask);
        }

        lastLevelIndex = curLevelIdx;
        lastSandboxFlag = curSandboxFlag;
        lastInputABit = curInABit;
        lastInputBBit = curInBBit;
        lastOutputBit = curOutBit;
        lastTopologyVersion = curTopoVer;
        lastActiveGateCount = curActiveGates;
        lastActiveWireCount = curWireCount;
        lastPassedRowMask = debugPassedRowMask;
        lastFailedRowMask = debugFailedRowMask;
        lastCircuitCompleteFlag = curCompleteFlag;
        lastVerifyingFlag = curVerifyingFlag;

        // 3. Perbarui 3D Simbol IEEE Std 91 & Lencana Biner Port 0/1 dengan pembesaran alami terhadap zoom & jarak
        Transform camT = mainCam.transform;
        float zoomRatio = Mathf.Max(0.20f, stageZoomScale / AirWeavingStageController.DefaultZoomScale);

        UpdateGateIeeeSymbols(camT, zoomRatio, activePlacedGates);
        UpdateConnectedPortBinaryBadges(camT, zoomRatio, cableRouter);
    }

    private void OnDisable()
    {
        if (isInitialized)
        {
            HideImmediate();
        }
    }

    private void OnDestroy()
    {
        HideImmediate();

        if (btnToggleTopStrip != null)
        {
            btnToggleTopStrip.onClick.RemoveListener(ToggleTopStripCollapsed);
            btnToggleTopStrip = null;
        }
        if (btnToggleBottomStrip != null)
        {
            btnToggleBottomStrip.onClick.RemoveListener(ToggleBottomStripCollapsed);
            btnToggleBottomStrip = null;
        }

        if (hudCanvasObj != null)
        {
            Destroy(hudCanvasObj);
            hudCanvasObj = null;
        }
        if (billboardOverlaysRootObj != null)
        {
            Destroy(billboardOverlaysRootObj);
            billboardOverlaysRootObj = null;
        }

        safeAreaRect = null;
        topStripRect = null;
        topStripBodyObj = null;
        btnToggleTopRect = null;
        bottomStripRect = null;
        bottomStripBodyObj = null;
        btnToggleBottomRect = null;
        gateSymbolsRootObj = null;
        portBadgesRootObj = null;

        for (int i = 0; i < ownedMaterials.Count; i++)
        {
            if (ownedMaterials[i] != null)
            {
                Destroy(ownedMaterials[i]);
            }
        }
        ownedMaterials.Clear();
        formulaVisited.Clear();
        isInitialized = false;
    }

    // =========================================================================
    // CONSTRUCTION: 2 PITA HORIZONTAL 2D OVERLAY + 3D IEEE & PORT POOLS
    // =========================================================================

    private void BuildAllVisuals()
    {
        sharedIeeeBackdropMat = CreateSpritesMaterial(IeeeBackdropColor, QueueIeeeBackdrop);
        sharedPortBadgeBgMat = CreateSpritesMaterial(PortBadgeBgColor, QueuePortBadgeBackdrop);

        Build2DOverlayHudCanvas();
        Build3DBillboardOverlays();
    }

    private void Build2DOverlayHudCanvas()
    {
        hudCanvasObj = new GameObject(
            "AR_AirPedagogicalHudCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        hudCanvasObj.transform.SetParent(transform, false);

        // sortingOrder = 5: menjamin tombol '-' / '+' selalu dapat diklik di atas panel Canvas utama (sortingOrder 0),
        // sementara seluruh elemen non-tombol memakai raycastTarget=false & Canvas otomatis sembunyi saat modal/drawer buka.
        Canvas canvas = hudCanvasObj.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;

        CanvasScaler scaler = hudCanvasObj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceCanvasWidth, ReferenceCanvasHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GameObject safeObj = new GameObject("PedHud_SafeArea", typeof(RectTransform));
        safeObj.transform.SetParent(hudCanvasObj.transform, false);
        safeAreaRect = safeObj.GetComponent<RectTransform>();
        safeAreaRect.anchorMin = Vector2.zero;
        safeAreaRect.anchorMax = Vector2.one;
        safeAreaRect.offsetMin = Vector2.zero;
        safeAreaRect.offsetMax = Vector2.zero;

        BuildTop2DInputStrip(safeAreaRect);
        BuildBottom2DOutputStrip(safeAreaRect);
        ApplyStripCollapseLayout();
    }

    private void BuildTop2DInputStrip(RectTransform parent)
    {
        GameObject stripObj = new GameObject("PedHud_TopInputStrip", typeof(RectTransform), typeof(Image));
        stripObj.transform.SetParent(parent, false);
        topStripRect = stripObj.GetComponent<RectTransform>();

        Image stripBg = stripObj.GetComponent<Image>();
        stripBg.color = StripGlassBgColor;
        stripBg.raycastTarget = false;
        CompactHudMenuController.ApplyRoundedPill(stripBg);

        Image stripBorder = CompactHudMenuController.AttachRoundedBorderOverlay(
            topStripRect,
            "TopStrip_Border",
            CompactHudMenuController.GetRoundedPillBorderSprite(),
            StripBorderCyanColor);
        if (stripBorder != null)
        {
            stripBorder.raycastTarget = false;
        }

        // Kontainer isi 1 baris horizontal (menyisakan ruang 52px di kanan untuk tombol '-')
        topStripBodyObj = new GameObject("TopStrip_Body", typeof(RectTransform));
        topStripBodyObj.transform.SetParent(topStripRect, false);
        RectTransform bodyRect = topStripBodyObj.GetComponent<RectTransform>();
        bodyRect.anchorMin = Vector2.zero;
        bodyRect.anchorMax = Vector2.one;
        bodyRect.offsetMin = new Vector2(14f, 4f);
        bodyRect.offsetMax = new Vector2(-52f, -4f);

        // 1. Label kiri 'VARIABEL INPUT' (0.000..0.112)
        CreateUiTmp(
            "TopStrip_HeaderTxt",
            bodyRect,
            new Vector2(0.000f, 0f),
            new Vector2(0.112f, 1f),
            new Vector2(2f, 0f),
            new Vector2(-4f, 0f),
            16f,
            12f,
            16.5f,
            HeaderTextColor,
            TextAlignmentOptions.MidlineLeft,
            StrHeaderLeft);

        CreateVerticalAnchorDivider("TopStrip_Div0", bodyRect, 0.116f, 32f);

        // 2. Kanal A (aksen sian, 0.123..0.548)
        RectTransform chARect = CreateAnchorUiPanel("TopStrip_ChannelA", bodyRect, 0.123f, 0.548f, ChannelABgColor);
        CreateUiRectImage("ChA_Accent", chARect, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(4f, -10f), InputACyanColor);
        txtInputATitle = CreateUiTmp("ChA_Title", chARect, new Vector2(0.018f, 0f), new Vector2(0.245f, 1f), new Vector2(4f, 0f), new Vector2(-2f, 0f), 18.5f, 13f, 19f, InputACyanColor, TextAlignmentOptions.MidlineLeft, "A = INPUT A");
        txtInputAState = CreateUiTmp("ChA_State", chARect, new Vector2(0.248f, 0f), new Vector2(0.568f, 1f), new Vector2(2f, 0f), new Vector2(-4f, 0f), 17.5f, 12.5f, 18f, SignalLowColor, TextAlignmentOptions.MidlineLeft, "[0] OFF (LOW)");
        waveTrackA = BuildHorizontalSquareWaveTrack("WaveTrack_A", chARect, 0.574f, 0.990f, InputACyanColor);

        CreateVerticalAnchorDivider("TopStrip_Div1", bodyRect, 0.556f, 32f);

        // 3. Kanal B (aksen emas, 0.564..0.994)
        RectTransform chBRect = CreateAnchorUiPanel("TopStrip_ChannelB", bodyRect, 0.564f, 0.994f, ChannelBBgColor);
        CreateUiRectImage("ChB_Accent", chBRect, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(4f, -10f), InputBGoldColor);
        txtInputBTitle = CreateUiTmp("ChB_Title", chBRect, new Vector2(0.018f, 0f), new Vector2(0.245f, 1f), new Vector2(4f, 0f), new Vector2(-2f, 0f), 18.5f, 13f, 19f, InputBGoldColor, TextAlignmentOptions.MidlineLeft, "B = INPUT B");
        txtInputBState = CreateUiTmp("ChB_State", chBRect, new Vector2(0.248f, 0f), new Vector2(0.568f, 1f), new Vector2(2f, 0f), new Vector2(-4f, 0f), 17.5f, 12.5f, 18f, SignalLowColor, TextAlignmentOptions.MidlineLeft, "[0] OFF (LOW)");
        waveTrackB = BuildHorizontalSquareWaveTrack("WaveTrack_B", chBRect, 0.574f, 0.990f, InputBGoldColor);

        // 4. Tombol Lipat '-' / 'INPUT [+]'
        btnToggleTopStrip = CreateStripCollapseButton("Btn_ToggleTopStrip", topStripRect, out btnToggleTopRect, out txtToggleTopLabel);
        btnToggleTopStrip.onClick.AddListener(ToggleTopStripCollapsed);
    }

    private SquareWaveUiTrack BuildHorizontalSquareWaveTrack(
        string name,
        RectTransform parentChannel,
        float anchorMinX,
        float anchorMaxX,
        Color initialWaveColor)
    {
        GameObject trackObj = new GameObject(name, typeof(RectTransform), typeof(Image));
        trackObj.transform.SetParent(parentChannel, false);
        RectTransform trackRect = trackObj.GetComponent<RectTransform>();
        trackRect.anchorMin = new Vector2(anchorMinX, 0.5f);
        trackRect.anchorMax = new Vector2(anchorMaxX, 0.5f);
        trackRect.pivot = new Vector2(0.5f, 0.5f);
        trackRect.offsetMin = new Vector2(0f, -15f);
        trackRect.offsetMax = new Vector2(0f, 15f); // tinggi total track 30px (grafik 20px di dalam)

        Image trackBg = trackObj.GetComponent<Image>();
        trackBg.color = StripInsetBgColor;
        trackBg.raycastTarget = false;

        // Sumbu mini 1 & 0 di kiri track
        CreateUiTmp("Tick_1", trackRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, WaveHighLocalY - 7f), new Vector2(16f, WaveHighLocalY + 7f), 11f, 9f, 11.5f, CellTextMutedColor, TextAlignmentOptions.Center, StrOne);
        CreateUiTmp("Tick_0", trackRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, WaveLowLocalY - 7f), new Vector2(16f, WaveLowLocalY + 7f), 11f, 9f, 11.5f, CellTextMutedColor, TextAlignmentOptions.Center, StrZero);

        // Area plot dinamis proporsional (otomatis rapi pada 4:3, 16:9, maupun 20:9)
        GameObject plotObj = new GameObject("WavePlotArea", typeof(RectTransform));
        plotObj.transform.SetParent(trackRect, false);
        RectTransform plotRect = plotObj.GetComponent<RectTransform>();
        plotRect.anchorMin = Vector2.zero;
        plotRect.anchorMax = Vector2.one;
        plotRect.offsetMin = new Vector2(17f, 2f);
        plotRect.offsetMax = new Vector2(-5f, -2f);

        // Rel referensi HIGH & LOW tipis
        CreateUiRectImage("Rail_High", plotRect, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, WaveHighLocalY), new Vector2(0f, 1.0f), WaveRailColor);
        CreateUiRectImage("Rail_Low", plotRect, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, WaveLowLocalY), new Vector2(0f, 1.0f), WaveRailColor);

        SquareWaveUiTrack track = new SquareWaveUiTrack();
        float invSteps = 1f / WaveStepCount;
        float verticalEdgeH = (WaveHighLocalY - WaveLowLocalY) + WaveStrokeThickness;

        // 16 segmen horizontal dengan anchor proporsional [s/16 .. (s+1)/16]
        for (int s = 0; s < WaveStepCount; s++)
        {
            float ax0 = s * invSteps;
            float ax1 = (s + 1) * invSteps;
            Image segImg = CreateUiRectImage(
                $"Step_{s}",
                plotRect,
                new Vector2(ax0, 0.5f),
                new Vector2(ax1, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, WaveLowLocalY),
                new Vector2(0.8f, WaveStrokeThickness),
                initialWaveColor);
            track.stepImages[s] = segImg;
            track.stepRects[s] = segImg.rectTransform;
        }

        // 15 segmen transisi vertikal 90 derajat pada batas (e+1)/16 (aktif hanya saat bit[e] != bit[e+1])
        for (int e = 0; e < WaveEdgeCount; e++)
        {
            float axEdge = (e + 1) * invSteps;
            Image edgeImg = CreateUiRectImage(
                $"Edge_{e}",
                plotRect,
                new Vector2(axEdge, 0.5f),
                new Vector2(axEdge, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, (WaveHighLocalY + WaveLowLocalY) * 0.5f),
                new Vector2(WaveStrokeThickness, verticalEdgeH),
                initialWaveColor);
            edgeImg.enabled = false;
            track.edgeImages[e] = edgeImg;
        }

        return track;
    }

    private void BuildBottom2DOutputStrip(RectTransform parent)
    {
        GameObject stripObj = new GameObject("PedHud_BottomOutputStrip", typeof(RectTransform), typeof(Image));
        stripObj.transform.SetParent(parent, false);
        bottomStripRect = stripObj.GetComponent<RectTransform>();

        Image stripBg = stripObj.GetComponent<Image>();
        stripBg.color = StripGlassBgColor;
        stripBg.raycastTarget = false;
        CompactHudMenuController.ApplyRoundedCard(stripBg);

        Image stripBorder = CompactHudMenuController.AttachRoundedBorderOverlay(
            bottomStripRect,
            "BottomStrip_Border",
            CompactHudMenuController.GetRoundedCardBorderSprite(),
            StripBorderCyanColor);
        if (stripBorder != null)
        {
            stripBorder.raycastTarget = false;
        }

        // Kontainer isi 1 baris horizontal (menyisakan ruang 52px di kanan untuk tombol '-')
        bottomStripBodyObj = new GameObject("BottomStrip_Body", typeof(RectTransform));
        bottomStripBodyObj.transform.SetParent(bottomStripRect, false);
        RectTransform bodyRect = bottomStripBodyObj.GetComponent<RectTransform>();
        bodyRect.anchorMin = Vector2.zero;
        bodyRect.anchorMax = Vector2.one;
        bodyRect.offsetMin = new Vector2(12f, 5f);
        bodyRect.offsetMax = new Vector2(-52f, -5f);

        // 1. Bagian Kiri (~35% lebar: 0.000..0.348): Ekspresi Boolean Live + Status Output Kontekstual
        RectTransform formulaPanel = CreateAnchorUiPanel("Bottom_FormulaPanel", bodyRect, 0.000f, 0.348f, StripInsetBgColor);
        txtBooleanFormula = CreateUiTmp(
            "Bottom_FormulaTxt",
            formulaPanel,
            new Vector2(0f, 0.44f),
            new Vector2(1f, 0.98f),
            new Vector2(12f, 0f),
            new Vector2(-10f, 0f),
            20f,
            13.5f,
            21.5f,
            FormulaDisconnectedColor,
            TextAlignmentOptions.MidlineLeft,
            "EKSPRESI BOOLEAN: Y = -- (BELUM TERSAMBUNG)");

        txtOutputContext = CreateUiTmp(
            "Bottom_OutputCtxTxt",
            formulaPanel,
            new Vector2(0f, 0.04f),
            new Vector2(1f, 0.46f),
            new Vector2(12f, 0f),
            new Vector2(-10f, 0f),
            15.5f,
            12f,
            16f,
            SignalIdleColor,
            TextAlignmentOptions.MidlineLeft,
            "OUTPUT: --");

        CreateVerticalAnchorDivider("Bottom_MidDiv", bodyRect, 0.356f, 46f);

        // 2. Bagian Kanan (~63% lebar: 0.364..0.996): Label berjarak napas + 4 Sel Tabel Kebenaran Horizontal Sejajar
        txtTruthTableLabel = CreateUiTmp(
            "Bottom_TruthLabel",
            bodyRect,
            new Vector2(0.364f, 0f),
            new Vector2(0.478f, 1f),
            new Vector2(2f, 0f),
            new Vector2(-14f, 0f),
            15f,
            10.5f,
            15.5f,
            HeaderTextColor,
            TextAlignmentOptions.MidlineLeft,
            StrHeaderTruthTable);

        const float cellsMinX = 0.490f;
        const float cellsMaxX = 0.996f;
        float cellSpan = (cellsMaxX - cellsMinX) / TruthRowCount;

        for (int r = 0; r < TruthRowCount; r++)
        {
            float ax0 = cellsMinX + (r * cellSpan);
            float ax1 = ax0 + cellSpan;

            GameObject cellObj = new GameObject($"TruthCell_{r}", typeof(RectTransform), typeof(Image));
            cellObj.transform.SetParent(bodyRect, false);
            RectTransform cellRect = cellObj.GetComponent<RectTransform>();
            cellRect.anchorMin = new Vector2(ax0, 0f);
            cellRect.anchorMax = new Vector2(ax1, 1f);
            cellRect.offsetMin = new Vector2(4f, 2f);
            cellRect.offsetMax = new Vector2(-4f, -2f);

            Image cellBg = cellObj.GetComponent<Image>();
            cellBg.color = CellIdleBgColor;
            cellBg.raycastTarget = false;
            CompactHudMenuController.ApplyRoundedSquircle(cellBg);

            // Bingkai sorot aktif per sel (sian jika netral/cocok, merah jika gagal)
            Image activeBorder = CompactHudMenuController.AttachRoundedBorderOverlay(
                cellRect,
                "Cell_ActiveBorder",
                CompactHudMenuController.GetRoundedSquircleBorderSprite(),
                CellActiveCyanBorderColor);
            if (activeBorder != null)
            {
                activeBorder.raycastTarget = false;
                activeBorder.enabled = false;
            }

            // Teks ringkas sel: "[0,0] Y:- | TGT:0" (atau "[0,0] Y:-" pada Mode Sandbox)
            TextMeshProUGUI txtSummary = CreateUiTmp(
                "Cell_SummaryTxt",
                cellRect,
                new Vector2(0.03f, 0f),
                new Vector2(0.73f, 1f),
                new Vector2(4f, 0f),
                new Vector2(-2f, 0f),
                17.5f,
                12.5f,
                18.5f,
                CellTextPrimaryColor,
                TextAlignmentOptions.MidlineLeft,
                StrComboPrefixes[r]);

            // Lencana status kanan sel ('OK' hijau / 'X' merah / '-' abu)
            GameObject pillObj = new GameObject("Cell_StPill", typeof(RectTransform), typeof(Image));
            pillObj.transform.SetParent(cellRect, false);
            RectTransform pillRect = pillObj.GetComponent<RectTransform>();
            pillRect.anchorMin = new Vector2(0.74f, 0.5f);
            pillRect.anchorMax = new Vector2(0.97f, 0.5f);
            pillRect.pivot = new Vector2(0.5f, 0.5f);
            pillRect.offsetMin = new Vector2(0f, -14f);
            pillRect.offsetMax = new Vector2(0f, 14f);

            Image pillImg = pillObj.GetComponent<Image>();
            pillImg.color = StatusPassPillBg;
            pillImg.raycastTarget = false;
            CompactHudMenuController.ApplyRoundedPill(pillImg);
            pillImg.enabled = false;

            TextMeshProUGUI txtStatus = CreateUiTmp(
                "Cell_StTxt",
                cellRect,
                new Vector2(0.74f, 0f),
                new Vector2(0.97f, 1f),
                Vector2.zero,
                Vector2.zero,
                15.5f,
                12f,
                16.5f,
                CellTextMutedColor,
                TextAlignmentOptions.Center,
                StrDash);

            truthCells[r] = new TruthCellUi
            {
                bgImage = cellBg,
                activeBorderImage = activeBorder,
                txtSummary = txtSummary,
                statusPillBg = pillImg,
                txtStatus = txtStatus
            };
        }

        // 3. Tombol Lipat '-' / '[+] TABEL'
        btnToggleBottomStrip = CreateStripCollapseButton("Btn_ToggleBottomStrip", bottomStripRect, out btnToggleBottomRect, out txtToggleBottomLabel);
        btnToggleBottomStrip.onClick.AddListener(ToggleBottomStripCollapsed);
    }

    private Button CreateStripCollapseButton(
        string name,
        RectTransform parentStrip,
        out RectTransform rectOut,
        out TextMeshProUGUI labelOut)
    {
        GameObject btnObj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(parentStrip, false);
        rectOut = btnObj.GetComponent<RectTransform>();

        Image bg = btnObj.GetComponent<Image>();
        bg.color = ToggleBtnBgColor;
        bg.raycastTarget = true;
        CompactHudMenuController.ApplyRoundedSquircle(bg);

        Image border = CompactHudMenuController.AttachRoundedBorderOverlay(
            rectOut,
            "Btn_Border",
            CompactHudMenuController.GetRoundedSquircleBorderSprite(),
            ToggleBtnBorderColor);
        if (border != null)
        {
            border.raycastTarget = false;
        }

        Button btn = btnObj.GetComponent<Button>();
        btn.targetGraphic = bg;
        btn.transition = Selectable.Transition.ColorTint;
        btn.colors = HudToggleBtnColors;

        labelOut = CreateUiTmp(
            "Txt_Toggle",
            rectOut,
            Vector2.zero,
            Vector2.one,
            new Vector2(4f, 0f),
            new Vector2(-4f, 0f),
            20f,
            14f,
            22f,
            ToggleBtnTextColor,
            TextAlignmentOptions.Center,
            StrCollapseMinus);
        return btn;
    }

    private void ToggleTopStripCollapsed()
    {
        isTopStripCollapsed = !isTopStripCollapsed;
        ApplyStripCollapseLayout();
    }

    private void ToggleBottomStripCollapsed()
    {
        isBottomStripCollapsed = !isBottomStripCollapsed;
        ApplyStripCollapseLayout();
        OnBottomStripExpandedChanged?.Invoke(IsBottomStripExpanded);
    }

    private void ApplyStripCollapseLayout()
    {
        // 1. Pita Horizontal Atas: melebar penuh di bawah Top HUD bar, atau mengecil menjadi pil 'INPUT [+]' di pojok kiri-atas
        if (topStripRect != null)
        {
            if (isTopStripCollapsed)
            {
                topStripRect.anchorMin = new Vector2(0f, 1f);
                topStripRect.anchorMax = new Vector2(0f, 1f);
                topStripRect.pivot = new Vector2(0f, 1f);
                topStripRect.anchoredPosition = new Vector2(StripSideMarginX, TopStripTopOffsetY);
                topStripRect.sizeDelta = new Vector2(TopStripCollapsedWidth, StripCollapsedHeight);
            }
            else
            {
                topStripRect.anchorMin = new Vector2(0f, 1f);
                topStripRect.anchorMax = new Vector2(1f, 1f);
                topStripRect.pivot = new Vector2(0.5f, 1f);
                topStripRect.offsetMin = new Vector2(StripSideMarginX, TopStripTopOffsetY - TopStripExpandedHeight);
                topStripRect.offsetMax = new Vector2(-StripSideMarginX, TopStripTopOffsetY);
            }
        }
        if (topStripBodyObj != null && topStripBodyObj.activeSelf == isTopStripCollapsed)
        {
            topStripBodyObj.SetActive(!isTopStripCollapsed);
        }
        if (btnToggleTopRect != null)
        {
            if (isTopStripCollapsed)
            {
                btnToggleTopRect.anchorMin = Vector2.zero;
                btnToggleTopRect.anchorMax = Vector2.one;
                btnToggleTopRect.pivot = new Vector2(0.5f, 0.5f);
                btnToggleTopRect.offsetMin = new Vector2(4f, 4f);
                btnToggleTopRect.offsetMax = new Vector2(-4f, -4f);
            }
            else
            {
                btnToggleTopRect.anchorMin = new Vector2(1f, 0.5f);
                btnToggleTopRect.anchorMax = new Vector2(1f, 0.5f);
                btnToggleTopRect.pivot = new Vector2(1f, 0.5f);
                btnToggleTopRect.anchoredPosition = new Vector2(-8f, 0f);
                btnToggleTopRect.sizeDelta = new Vector2(38f, 36f);
            }
        }
        if (txtToggleTopLabel != null)
        {
            txtToggleTopLabel.text = isTopStripCollapsed ? StrTopCollapsedPill : StrCollapseMinus;
        }

        // 2. Pita Horizontal Bawah: melebar penuh di tepi bawah, atau mengecil menjadi pil '[+] TABEL' di pojok kanan-bawah
        if (bottomStripRect != null)
        {
            if (isBottomStripCollapsed)
            {
                bottomStripRect.anchorMin = new Vector2(1f, 0f);
                bottomStripRect.anchorMax = new Vector2(1f, 0f);
                bottomStripRect.pivot = new Vector2(1f, 0f);
                bottomStripRect.anchoredPosition = new Vector2(-StripSideMarginX, BottomStripBottomMarginY);
                bottomStripRect.sizeDelta = new Vector2(BottomStripCollapsedWidth, StripCollapsedHeight);
            }
            else
            {
                bottomStripRect.anchorMin = new Vector2(0f, 0f);
                bottomStripRect.anchorMax = new Vector2(1f, 0f);
                bottomStripRect.pivot = new Vector2(0.5f, 0f);
                bottomStripRect.offsetMin = new Vector2(StripSideMarginX, BottomStripBottomMarginY);
                bottomStripRect.offsetMax = new Vector2(-StripSideMarginX, BottomStripBottomMarginY + BottomStripExpandedHeight);
            }
        }
        if (bottomStripBodyObj != null && bottomStripBodyObj.activeSelf == isBottomStripCollapsed)
        {
            bottomStripBodyObj.SetActive(!isBottomStripCollapsed);
        }
        if (btnToggleBottomRect != null)
        {
            if (isBottomStripCollapsed)
            {
                btnToggleBottomRect.anchorMin = Vector2.zero;
                btnToggleBottomRect.anchorMax = Vector2.one;
                btnToggleBottomRect.pivot = new Vector2(0.5f, 0.5f);
                btnToggleBottomRect.offsetMin = new Vector2(4f, 4f);
                btnToggleBottomRect.offsetMax = new Vector2(-4f, -4f);
            }
            else
            {
                btnToggleBottomRect.anchorMin = new Vector2(1f, 0.5f);
                btnToggleBottomRect.anchorMax = new Vector2(1f, 0.5f);
                btnToggleBottomRect.pivot = new Vector2(1f, 0.5f);
                btnToggleBottomRect.anchoredPosition = new Vector2(-8f, 0f);
                btnToggleBottomRect.sizeDelta = new Vector2(38f, 42f);
            }
        }
        if (txtToggleBottomLabel != null)
        {
            txtToggleBottomLabel.text = isBottomStripCollapsed ? StrBottomCollapsedPill : StrCollapseMinus;
        }
    }

    private void ApplySafeAreaIfNeeded(bool force)
    {
        if (safeAreaRect == null)
        {
            return;
        }

        int sw = Screen.width;
        int sh = Screen.height;
        Rect sa = Screen.safeArea;
        if (!force && sw == lastScreenW && sh == lastScreenH && sa == lastAppliedSafeArea)
        {
            return;
        }

        lastScreenW = sw;
        lastScreenH = sh;
        lastAppliedSafeArea = sa;

        float denomW = Mathf.Max(1f, sw);
        float denomH = Mathf.Max(1f, sh);
        safeAreaRect.anchorMin = new Vector2(Mathf.Clamp01(sa.xMin / denomW), Mathf.Clamp01(sa.yMin / denomH));
        safeAreaRect.anchorMax = new Vector2(Mathf.Clamp01(sa.xMax / denomW), Mathf.Clamp01(sa.yMax / denomH));
        safeAreaRect.offsetMin = Vector2.zero;
        safeAreaRect.offsetMax = Vector2.zero;

        if (txtTruthTableLabel != null)
        {
            float safeAspect = Mathf.Max(0.1f, sa.width) / Mathf.Max(1f, sa.height);
            string desiredLabel = safeAspect < 1.50f ? StrHeaderTruthTableCompact : StrHeaderTruthTable;
            if (!string.Equals(txtTruthTableLabel.text, desiredLabel, StringComparison.Ordinal))
            {
                txtTruthTableLabel.text = desiredLabel;
            }
        }
    }

    private void Build3DBillboardOverlays()
    {
        billboardOverlaysRootObj = new GameObject("AR_AirPedagogicalOverlays3D");
        billboardOverlaysRootObj.transform.SetParent(transform, false);
        billboardOverlaysRootObj.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        billboardOverlaysRootObj.transform.localScale = Vector3.one;

        gateSymbolsRootObj = new GameObject("HoloPed_GateSymbolsRoot");
        gateSymbolsRootObj.transform.SetParent(billboardOverlaysRootObj.transform, false);
        for (int i = 0; i < MaxGateSymbolSlots; i++)
        {
            gateSymbolPool[i] = BuildGateSymbolSlot(i, gateSymbolsRootObj.transform);
        }

        portBadgesRootObj = new GameObject("HoloPed_PortBadgesRoot");
        portBadgesRootObj.transform.SetParent(billboardOverlaysRootObj.transform, false);
        for (int i = 0; i < MaxPortBadgeSlots; i++)
        {
            portBadgePool[i] = BuildPortBadgeSlot(i, portBadgesRootObj.transform);
        }
    }

    private GateSymbolSlot BuildGateSymbolSlot(int index, Transform parent)
    {
        GameObject rootObj = new GameObject($"HoloPed_IeeeGate_{index}");
        rootObj.transform.SetParent(parent, false);
        Transform root = rootObj.transform;

        CreateNoColliderQuad("Ieee_Backdrop", root, new Vector3(0f, 0f, 0.008f), new Vector3(0.245f, 0.140f, 1f), sharedIeeeBackdropMat);

        Material bodyMat = CreateSpritesMaterial(SignalIdleColor, QueueIeeeStroke);
        Material extraMat = CreateSpritesMaterial(SignalIdleColor, QueueIeeeStroke);
        Material inAMat = CreateSpritesMaterial(SignalIdleColor, QueueIeeeStroke);
        Material inBMat = CreateSpritesMaterial(SignalIdleColor, QueueIeeeStroke);
        Material outMat = CreateSpritesMaterial(SignalIdleColor, QueueIeeeStroke);

        LineRenderer bodyLr = CreateLocalLine("Ieee_Body", root, 4, loop: true, 0.0082f, bodyMat, numCorners: 2, numCaps: 2);
        LineRenderer extraLr = CreateLocalLine("Ieee_Extra", root, 4, loop: false, 0.0076f, extraMat, numCorners: 2, numCaps: 2);
        LineRenderer leadInA = CreateLocalLine("Ieee_LeadInA", root, 2, loop: false, 0.0070f, inAMat, numCorners: 0, numCaps: 1);
        LineRenderer leadInB = CreateLocalLine("Ieee_LeadInB", root, 2, loop: false, 0.0070f, inBMat, numCorners: 0, numCaps: 1);
        LineRenderer leadOut = CreateLocalLine("Ieee_LeadOut", root, 2, loop: false, 0.0074f, outMat, numCorners: 0, numCaps: 1);

        extraLr.enabled = false;
        rootObj.SetActive(false);

        return new GateSymbolSlot
        {
            rootObj = rootObj,
            rootTransform = root,
            bodyLine = bodyLr,
            extraLine = extraLr,
            leadInALine = leadInA,
            leadInBLine = leadInB,
            leadOutLine = leadOut,
            bodyMat = bodyMat,
            extraMat = extraMat,
            leadInAMat = inAMat,
            leadInBMat = inBMat,
            leadOutMat = outMat,
            configuredType = NodeType.InputSource,
            lastStateKey = int.MinValue
        };
    }

    private PortBadgeSlot BuildPortBadgeSlot(int index, Transform parent)
    {
        GameObject rootObj = new GameObject($"HoloPed_PortBit_{index}");
        rootObj.transform.SetParent(parent, false);
        Transform root = rootObj.transform;

        CreateNoColliderQuad("PortBit_Bg", root, new Vector3(0f, 0f, 0.006f), new Vector3(0.092f, 0.092f, 1f), sharedPortBadgeBgMat);

        Material borderMat = CreateSpritesMaterial(SignalLowColor, QueuePortBadgeBorder);
        LineRenderer borderLr = CreateLocalRectBorder("PortBit_Border", root, 0.092f, 0.092f, 0.003f, 0.0058f, borderMat);

        TextMeshPro tmp = CreateWorldTmp("PortBit_Txt", root, Vector3.zero, new Vector2(0.094f, 0.094f), 0.60f, SignalLowColor, TextAlignmentOptions.Center, PortBadgeTextSortingOrder, StrZero);

        rootObj.SetActive(false);

        return new PortBadgeSlot
        {
            rootObj = rootObj,
            rootTransform = root,
            borderLine = borderLr,
            borderMat = borderMat,
            bitText = tmp,
            lastBit = -1
        };
    }

    // =========================================================================
    // PITA ATAS 2D: ABSTRAKSI VARIABEL + SQUARE WAVE 16-LANGKAH (EVENT-DRIVEN)
    // =========================================================================

    private void RefreshLevelContextCache(LevelAndUIManager levelManager, int levelIndex, bool isSandbox)
    {
        cachedHasExpectedTruth = false;
        for (int i = 0; i < TruthRowCount; i++)
        {
            cachedExpectedTruth[i] = false;
        }

        if (isSandbox)
        {
            cachedInAName = "INPUT A"; cachedInA0 = "OFF"; cachedInA1 = "ON";
            cachedInBName = "INPUT B"; cachedInB0 = "OFF"; cachedInB1 = "ON";
            cachedOutName = "OUTPUT"; cachedOut0 = "MATI"; cachedOut1 = "AKTIF";
            return;
        }

        LevelScenario scenario = (levelManager != null
                                  && levelManager.levels != null
                                  && levelIndex >= 0
                                  && levelIndex < levelManager.levels.Count)
            ? levelManager.levels[levelIndex]
            : null;

        if (scenario != null && scenario.expectedTruthTable != null && scenario.expectedTruthTable.Length >= TruthRowCount)
        {
            cachedHasExpectedTruth = true;
            for (int i = 0; i < TruthRowCount; i++)
            {
                cachedExpectedTruth[i] = scenario.expectedTruthTable[i];
            }
        }

        if (scenario != null && !string.IsNullOrEmpty(scenario.inputAName) && !string.IsNullOrEmpty(scenario.outputName))
        {
            cachedInAName = scenario.inputAName;
            cachedInA0 = string.IsNullOrEmpty(scenario.inputAState0) ? "OFF" : scenario.inputAState0;
            cachedInA1 = string.IsNullOrEmpty(scenario.inputAState1) ? "ON" : scenario.inputAState1;
            cachedInBName = string.IsNullOrEmpty(scenario.inputBName) ? "INPUT B" : scenario.inputBName;
            cachedInB0 = string.IsNullOrEmpty(scenario.inputBState0) ? "OFF" : scenario.inputBState0;
            cachedInB1 = string.IsNullOrEmpty(scenario.inputBState1) ? "ON" : scenario.inputBState1;
            cachedOutName = scenario.outputName;
            cachedOut0 = string.IsNullOrEmpty(scenario.outputState0) ? "MATI" : scenario.outputState0;
            cachedOut1 = string.IsNullOrEmpty(scenario.outputState1) ? "AKTIF" : scenario.outputState1;
            return;
        }

        cachedInAName = "INPUT A"; cachedInA0 = "OFF"; cachedInA1 = "ON";
        cachedInBName = "INPUT B"; cachedInB0 = "OFF"; cachedInB1 = "ON";
        cachedOutName = "OUTPUT"; cachedOut0 = "MATI"; cachedOut1 = "AKTIF";
    }

    private void InitializeWaveHistoryToCurrent(GateNode inputANode, GateNode inputBNode)
    {
        bool curA = inputANode != null && inputANode.currentOutputValue;
        bool curB = inputBNode != null && inputBNode.currentOutputValue;

        for (int i = 0; i < WaveStepCount; i++)
        {
            waveHistoryA[i] = curA;
            waveHistoryB[i] = curB;
        }

        waveHistoryInitialized = true;
        RefreshSquareWaveUiTrack(waveTrackA, waveHistoryA, curA ? InputACyanColor : InputACyanDimColor);
        RefreshSquareWaveUiTrack(waveTrackB, waveHistoryB, curB ? InputBGoldColor : InputBGoldDimColor);
    }

    private void PushWaveHistoryStep(bool nextA, bool nextB)
    {
        for (int i = 0; i < WaveStepCount - 1; i++)
        {
            waveHistoryA[i] = waveHistoryA[i + 1];
            waveHistoryB[i] = waveHistoryB[i + 1];
        }
        waveHistoryA[WaveStepCount - 1] = nextA;
        waveHistoryB[WaveStepCount - 1] = nextB;

        RefreshSquareWaveUiTrack(waveTrackA, waveHistoryA, nextA ? InputACyanColor : InputACyanDimColor);
        RefreshSquareWaveUiTrack(waveTrackB, waveHistoryB, nextB ? InputBGoldColor : InputBGoldDimColor);
    }

    private static void RefreshSquareWaveUiTrack(SquareWaveUiTrack track, bool[] history, Color waveColor)
    {
        if (track == null || history == null)
        {
            return;
        }

        for (int s = 0; s < WaveStepCount; s++)
        {
            RectTransform rt = track.stepRects[s];
            Image img = track.stepImages[s];
            if (rt != null)
            {
                Vector2 pos = rt.anchoredPosition;
                pos.y = history[s] ? WaveHighLocalY : WaveLowLocalY;
                rt.anchoredPosition = pos;
            }
            if (img != null)
            {
                img.color = waveColor;
            }
        }

        for (int e = 0; e < WaveEdgeCount; e++)
        {
            Image edgeImg = track.edgeImages[e];
            if (edgeImg == null)
            {
                continue;
            }

            bool hasTransition = history[e] != history[e + 1];
            if (edgeImg.enabled != hasTransition)
            {
                edgeImg.enabled = hasTransition;
            }
            if (hasTransition)
            {
                edgeImg.color = waveColor;
            }
        }
    }

    private void RefreshTopInputStripVisuals(GateNode inputANode, GateNode inputBNode)
    {
        bool curA = inputANode != null && inputANode.currentOutputValue;
        bool curB = inputBNode != null && inputBNode.currentOutputValue;

        if (txtInputATitle != null)
        {
            textBuilder.Clear();
            textBuilder.Append("A = ").Append(cachedInAName);
            txtInputATitle.SetText(textBuilder);
        }

        if (txtInputAState != null)
        {
            textBuilder.Clear();
            textBuilder.Append(curA ? "[1] " : "[0] ")
                .Append(curA ? cachedInA1 : cachedInA0)
                .Append(curA ? " (HIGH)" : " (LOW)");
            txtInputAState.color = curA ? SignalHighColor : SignalLowColor;
            txtInputAState.SetText(textBuilder);
        }

        if (txtInputBTitle != null)
        {
            textBuilder.Clear();
            textBuilder.Append("B = ").Append(cachedInBName);
            txtInputBTitle.SetText(textBuilder);
        }

        if (txtInputBState != null)
        {
            textBuilder.Clear();
            textBuilder.Append(curB ? "[1] " : "[0] ")
                .Append(curB ? cachedInB1 : cachedInB0)
                .Append(curB ? " (HIGH)" : " (LOW)");
            txtInputBState.color = curB ? SignalHighColor : SignalLowColor;
            txtInputBState.SetText(textBuilder);
        }
    }

    // =========================================================================
    // PITA BAWAH 2D: EKSPRESI BOOLEAN ZERO-GC + TABEL KEBENARAN 4 SEL SEJAJAR
    // =========================================================================

    private void RefreshBottomBooleanAndTruthCells(
        bool isSandbox,
        bool isCircuitCompleteLive,
        bool isVerifying,
        GateNode inputANode,
        GateNode inputBNode,
        GateNode outputNode,
        int passedRowMask,
        int failedRowMask)
    {
        // 1. Generator ekspresi Boolean live Zero-GC (StringBuilder + cycle guard visited)
        BuildLiveBooleanExpressionZeroGc(outputNode, out bool isConnected, out bool hasMissingBranch, out bool hasCycle);
        lastFormulaIsComplete = isConnected && !hasMissingBranch && !hasCycle;

        if (txtBooleanFormula != null)
        {
            txtBooleanFormula.SetText(textBuilder);
            if (!isConnected)
            {
                txtBooleanFormula.color = FormulaDisconnectedColor;
            }
            else if (hasMissingBranch || hasCycle)
            {
                txtBooleanFormula.color = FormulaIncompleteColor;
            }
            else
            {
                txtBooleanFormula.color = FormulaReadyColor;
            }
        }

        // 2. Baris status Output kontekstual live
        if (txtOutputContext != null)
        {
            textBuilder.Clear();
            textBuilder.Append(cachedOutName).Append(": ");
            if (isConnected && outputNode != null && outputNode.isTracked)
            {
                bool outVal = outputNode.currentOutputValue;
                textBuilder.Append(outVal ? "1 [" : "0 [")
                    .Append(outVal ? cachedOut1 : cachedOut0)
                    .Append(']');
                txtOutputContext.color = outVal ? SignalHighColor : SignalLowColor;
            }
            else
            {
                textBuilder.Append("-- [BELUM TERSAMBUNG]");
                txtOutputContext.color = SignalIdleColor;
            }
            txtOutputContext.SetText(textBuilder);
        }

        // 3. Tabel Kebenaran Horizontal 4 Sel Sejajar: [0,0], [0,1], [1,0], [1,1]
        bool curA = inputANode != null && inputANode.currentOutputValue;
        bool curB = inputBNode != null && inputBNode.currentOutputValue;
        int activeRow = (curA ? 2 : 0) + (curB ? 1 : 0);

        bool outputReadyForActiveEval = lastFormulaIsComplete && outputNode != null && outputNode.isTracked;
        if (isSandbox && !isVerifying && outputReadyForActiveEval)
        {
            int bit = 1 << activeRow;
            sandboxTestedRowMask |= bit;
            if (outputNode.currentOutputValue)
            {
                sandboxMeasuredHighMask |= bit;
            }
            else
            {
                sandboxMeasuredHighMask &= ~bit;
            }
        }

        for (int row = 0; row < TruthRowCount; row++)
        {
            TruthCellUi cell = truthCells[row];
            if (cell == null)
            {
                continue;
            }

            bool isActiveRow = row == activeRow;
            int rowBit = 1 << row;

            if (isSandbox)
            {
                bool rowHasSandboxVal = outputReadyForActiveEval && ((sandboxTestedRowMask & rowBit) != 0);
                textBuilder.Clear();
                textBuilder.Append(StrComboPrefixes[row]).Append("Y:");
                if (rowHasSandboxVal)
                {
                    bool yVal = (sandboxMeasuredHighMask & rowBit) != 0;
                    textBuilder.Append(yVal ? '1' : '0');
                    cell.txtSummary.color = yVal ? SignalHighColor : SignalLowColor;
                }
                else
                {
                    textBuilder.Append('-');
                    cell.txtSummary.color = isActiveRow ? CellTextPrimaryColor : CellTextMutedColor;
                }
                cell.txtSummary.SetText(textBuilder);

                SetCellStatusBadge(cell, state: 0);
                cell.bgImage.color = isActiveRow ? CellActiveBgColor : CellIdleBgColor;
                SetCellActiveBorder(cell, isActiveRow, isFailed: false);
                continue;
            }

            // Mode Kurikulum (Level 1-7): pertahankan uji aktif 4 kombinasi (anti-bocoran)
            bool targetVal = cachedHasExpectedTruth && cachedExpectedTruth[row];
            char tgtChar = cachedHasExpectedTruth ? (targetVal ? '1' : '0') : '-';
            bool isPassed = (passedRowMask & rowBit) != 0;
            bool isFailed = (failedRowMask & rowBit) != 0;

            char yChar;
            int statusState; // 1 = OK, -1 = X, 0 = untested
            Color bgColor;
            Color textColor;
            bool cellIsFailedVisual = false;

            if (isPassed)
            {
                yChar = targetVal ? '1' : '0';
                statusState = 1;
                bgColor = CellPassedBgColor;
                textColor = CellTextPrimaryColor;
            }
            else if (isFailed)
            {
                yChar = targetVal ? '0' : '1';
                statusState = -1;
                bgColor = CellFailedBgColor;
                textColor = CellTextPrimaryColor;
                cellIsFailedVisual = true;
            }
            else if (isActiveRow && (isCircuitCompleteLive || outputReadyForActiveEval) && outputNode != null && outputNode.isTracked && lastFormulaIsComplete)
            {
                bool liveY = outputNode.currentOutputValue;
                bool matchesTarget = cachedHasExpectedTruth && (liveY == targetVal);
                yChar = liveY ? '1' : '0';
                statusState = cachedHasExpectedTruth ? (matchesTarget ? 1 : -1) : 0;
                bgColor = cachedHasExpectedTruth ? (matchesTarget ? CellPassedBgColor : CellFailedBgColor) : CellActiveBgColor;
                textColor = CellTextPrimaryColor;
                cellIsFailedVisual = cachedHasExpectedTruth && !matchesTarget;
            }
            else
            {
                // Belum diuji: Y:- tanpa lencana OK/X
                yChar = '-';
                statusState = 0;
                bgColor = isActiveRow ? CellActiveBgColor : CellIdleBgColor;
                textColor = isActiveRow ? CellTextPrimaryColor : CellTextMutedColor;
            }

            textBuilder.Clear();
            textBuilder.Append(StrComboPrefixes[row])
                .Append("Y:")
                .Append(yChar)
                .Append(" | TGT:")
                .Append(tgtChar);
            cell.txtSummary.color = textColor;
            cell.txtSummary.SetText(textBuilder);

            SetCellStatusBadge(cell, statusState);
            cell.bgImage.color = bgColor;
            SetCellActiveBorder(cell, isActiveRow, cellIsFailedVisual);
        }
    }

    private static void SetCellStatusBadge(TruthCellUi cell, int state)
    {
        if (cell == null || cell.txtStatus == null || cell.statusPillBg == null)
        {
            return;
        }

        if (state > 0)
        {
            cell.statusPillBg.enabled = true;
            cell.statusPillBg.color = StatusPassPillBg;
            cell.txtStatus.text = StrOk;
            cell.txtStatus.color = StatusPassTextColor;
        }
        else if (state < 0)
        {
            cell.statusPillBg.enabled = true;
            cell.statusPillBg.color = StatusFailPillBg;
            cell.txtStatus.text = StrFailX;
            cell.txtStatus.color = StatusFailTextColor;
        }
        else
        {
            cell.statusPillBg.enabled = false;
            cell.txtStatus.text = StrDash;
            cell.txtStatus.color = CellTextMutedColor;
        }
    }

    private static void SetCellActiveBorder(TruthCellUi cell, bool isActive, bool isFailed)
    {
        if (cell == null || cell.activeBorderImage == null)
        {
            return;
        }

        if (cell.activeBorderImage.enabled != isActive)
        {
            cell.activeBorderImage.enabled = isActive;
        }
        if (isActive)
        {
            cell.activeBorderImage.color = isFailed ? CellActiveRedBorderColor : CellActiveCyanBorderColor;
        }
    }

    private void BuildLiveBooleanExpressionZeroGc(
        GateNode outputNode,
        out bool isConnected,
        out bool hasMissingBranch,
        out bool hasCycle)
    {
        textBuilder.Clear();
        formulaVisited.Clear();
        hasMissingBranch = false;
        hasCycle = false;

        textBuilder.Append("EKSPRESI BOOLEAN: ");

        if (outputNode == null
            || !outputNode.isTracked
            || outputNode.connectedInputs == null
            || outputNode.connectedInputs.Count == 0
            || outputNode.connectedInputs[0] == null
            || !outputNode.connectedInputs[0].isTracked)
        {
            isConnected = false;
            textBuilder.Append(StrUnconnectedFormula);
            return;
        }

        isConnected = true;
        textBuilder.Append("Y = ");
        AppendBooleanNodeZeroGc(outputNode.connectedInputs[0], isRoot: true, depth: 0, ref hasMissingBranch, ref hasCycle);
        formulaVisited.Clear();
    }

    private void AppendBooleanNodeZeroGc(
        GateNode node,
        bool isRoot,
        int depth,
        ref bool hasMissingBranch,
        ref bool hasCycle)
    {
        if (node == null || !node.isTracked)
        {
            hasMissingBranch = true;
            textBuilder.Append('?');
            return;
        }

        if (depth >= MaxBooleanDepth)
        {
            hasCycle = true;
            textBuilder.Append(StrCycleToken);
            return;
        }

        for (int i = 0; i < formulaVisited.Count; i++)
        {
            if (formulaVisited[i] == node)
            {
                hasCycle = true;
                textBuilder.Append(StrCycleToken);
                return;
            }
        }

        formulaVisited.Add(node);

        switch (node.nodeType)
        {
            case NodeType.InputSource:
                textBuilder.Append(IsInputBNode(node) ? 'B' : 'A');
                break;

            case NodeType.NOT:
                {
                    GateNode child = (node.connectedInputs != null && node.connectedInputs.Count > 0)
                        ? node.connectedInputs[0]
                        : null;

                    if (!isRoot)
                    {
                        textBuilder.Append('(');
                    }
                    textBuilder.Append("NOT ");
                    if (child == null || !child.isTracked)
                    {
                        hasMissingBranch = true;
                        textBuilder.Append('?');
                    }
                    else
                    {
                        AppendBooleanNodeZeroGc(child, isRoot: false, depth + 1, ref hasMissingBranch, ref hasCycle);
                    }
                    if (!isRoot)
                    {
                        textBuilder.Append(')');
                    }
                    break;
                }

            case NodeType.AND:
            case NodeType.OR:
            case NodeType.XOR:
            case NodeType.NAND:
                {
                    string opLabel = GetOperatorAscii(node.nodeType);
                    GateNode leftNode = (node.connectedInputs != null && node.connectedInputs.Count > 0)
                        ? node.connectedInputs[0]
                        : null;
                    GateNode rightNode = (node.connectedInputs != null && node.connectedInputs.Count > 1)
                        ? node.connectedInputs[1]
                        : null;

                    if (leftNode != null
                        && rightNode != null
                        && leftNode.nodeType == NodeType.InputSource
                        && rightNode.nodeType == NodeType.InputSource
                        && IsInputBNode(leftNode)
                        && !IsInputBNode(rightNode))
                    {
                        GateNode tmp = leftNode;
                        leftNode = rightNode;
                        rightNode = tmp;
                    }

                    if (!isRoot)
                    {
                        textBuilder.Append('(');
                    }
                    AppendBooleanNodeZeroGc(leftNode, isRoot: false, depth + 1, ref hasMissingBranch, ref hasCycle);
                    textBuilder.Append(' ').Append(opLabel).Append(' ');
                    AppendBooleanNodeZeroGc(rightNode, isRoot: false, depth + 1, ref hasMissingBranch, ref hasCycle);
                    if (!isRoot)
                    {
                        textBuilder.Append(')');
                    }
                    break;
                }

            case NodeType.OutputActuator:
                {
                    GateNode child = (node.connectedInputs != null && node.connectedInputs.Count > 0)
                        ? node.connectedInputs[0]
                        : null;
                    AppendBooleanNodeZeroGc(child, isRoot, depth + 1, ref hasMissingBranch, ref hasCycle);
                    break;
                }

            default:
                hasMissingBranch = true;
                textBuilder.Append('?');
                break;
        }

        if (formulaVisited.Count > 0)
        {
            formulaVisited.RemoveAt(formulaVisited.Count - 1);
        }
    }

    private static bool IsInputBNode(GateNode node)
    {
        if (node == null)
        {
            return false;
        }

        string name = node.name;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c == 'B' || c == 'b')
            {
                return true;
            }
        }
        return false;
    }

    private static string GetOperatorAscii(NodeType type)
    {
        switch (type)
        {
            case NodeType.AND: return "AND";
            case NodeType.OR: return "OR";
            case NodeType.XOR: return "XOR";
            case NodeType.NAND: return "NAND";
            default: return "OP";
        }
    }

    // =========================================================================
    // AREA TENGAH 3D: SIMBOL SKEMATIK ANSI/IEEE STD 91 (ZOOM ALAMI)
    // =========================================================================

    private void UpdateGateIeeeSymbols(Transform camT, float zoomRatio, List<GateNode> activePlacedGates)
    {
        Vector3 camPos = camT.position;
        Vector3 camForward = camT.forward;
        Vector3 camUp = camT.up;
        Quaternion camRot = camT.rotation;

        int usedCount = 0;
        if (activePlacedGates != null)
        {
            for (int i = 0; i < activePlacedGates.Count && usedCount < MaxGateSymbolSlots; i++)
            {
                GateNode gate = activePlacedGates[i];
                if (gate == null || !gate.isTracked)
                {
                    continue;
                }
                if (gate.nodeType == NodeType.InputSource || gate.nodeType == NodeType.OutputActuator)
                {
                    continue;
                }

                GateSymbolSlot slot = gateSymbolPool[usedCount++];
                if (!slot.rootObj.activeSelf)
                {
                    slot.rootObj.SetActive(true);
                }

                if (slot.configuredType != gate.nodeType)
                {
                    ConfigureIeeeSymbolGeometry(slot, gate.nodeType);
                }

                Vector3 anchorPos = gate.statusLabel != null
                    ? gate.statusLabel.transform.position
                    : (gate.transform.position + (camUp * 0.30f));

                float camDepth = Mathf.Max(0.65f, Vector3.Dot(anchorPos - camPos, camForward));
                float distRatio = Mathf.Clamp(camDepth / ReferenceWorkbenchDepth, 0.16f, 2.25f);
                float depthProximityGain = Mathf.Clamp(ReferenceWorkbenchDepth / camDepth, 0.50f, 2.80f);
                float naturalZoomGain = Mathf.Clamp(zoomRatio * Mathf.Lerp(1f, depthProximityGain, 0.72f), 0.55f, 2.75f);
                float worldScale = 1.06f * distRatio * naturalZoomGain;

                slot.rootTransform.position = anchorPos + (camUp * (0.148f * worldScale)) - (camForward * 0.014f);
                slot.rootTransform.rotation = camRot;
                slot.rootTransform.localScale = Vector3.one * worldScale;

                float strokeW = 0.0082f * worldScale;
                float extraW = 0.0074f * worldScale;
                float leadW = 0.0068f * worldScale;
                slot.bodyLine.widthMultiplier = strokeW;
                slot.extraLine.widthMultiplier = extraW;
                slot.leadInALine.widthMultiplier = leadW;
                slot.leadInBLine.widthMultiplier = leadW;
                slot.leadOutLine.widthMultiplier = leadW;

                bool anyInConn = gate.isInputAConnected || gate.isInputBConnected;
                int stateKey = (gate.currentOutputValue ? 1 : 0)
                               | (anyInConn ? 2 : 0)
                               | (gate.isInputAConnected ? 4 : 0)
                               | (gate.inputAValue ? 8 : 0)
                               | (gate.isInputBConnected ? 16 : 0)
                               | (gate.inputBValue ? 32 : 0);

                if (stateKey != slot.lastStateKey)
                {
                    slot.lastStateKey = stateKey;
                    Color bodyColor = !anyInConn
                        ? SignalIdleColor
                        : (gate.currentOutputValue ? SignalHighColor : SignalLowColor);
                    Color inAColor = !gate.isInputAConnected
                        ? SignalIdleColor
                        : (gate.inputAValue ? SignalHighColor : SignalLowColor);
                    Color inBColor = !gate.isInputBConnected
                        ? SignalIdleColor
                        : (gate.inputBValue ? SignalHighColor : SignalLowColor);

                    ApplyLineColor(slot.bodyLine, slot.bodyMat, bodyColor);
                    ApplyLineColor(slot.extraLine, slot.extraMat, bodyColor);
                    ApplyLineColor(slot.leadOutLine, slot.leadOutMat, bodyColor);
                    ApplyLineColor(slot.leadInALine, slot.leadInAMat, inAColor);
                    ApplyLineColor(slot.leadInBLine, slot.leadInBMat, inBColor);
                }
            }
        }

        for (int i = usedCount; i < MaxGateSymbolSlots; i++)
        {
            if (gateSymbolPool[i] != null && gateSymbolPool[i].rootObj.activeSelf)
            {
                gateSymbolPool[i].rootObj.SetActive(false);
            }
        }
    }

    private void ConfigureIeeeSymbolGeometry(GateSymbolSlot slot, NodeType type)
    {
        slot.configuredType = type;
        slot.lastStateKey = int.MinValue;

        switch (type)
        {
            case NodeType.AND:
                BuildDGateContour(slot.bodyLine, leftX: -0.056f, arcCenterX: 0.008f, radiusX: 0.048f, halfH: 0.044f);
                slot.extraLine.enabled = false;
                SetSegment2(slot.leadInALine, -0.098f, 0.022f, -0.056f, 0.022f);
                SetSegment2(slot.leadInBLine, -0.098f, -0.022f, -0.056f, -0.022f);
                slot.leadInBLine.enabled = true;
                SetSegment2(slot.leadOutLine, 0.056f, 0.000f, 0.098f, 0.000f);
                break;

            case NodeType.NAND:
                BuildDGateContour(slot.bodyLine, leftX: -0.066f, arcCenterX: -0.006f, radiusX: 0.044f, halfH: 0.044f);
                BuildCircleContour(slot.extraLine, centerX: 0.053f, centerY: 0.000f, radius: 0.015f, segments: 12);
                slot.extraLine.enabled = true;
                SetSegment2(slot.leadInALine, -0.100f, 0.022f, -0.066f, 0.022f);
                SetSegment2(slot.leadInBLine, -0.100f, -0.022f, -0.066f, -0.022f);
                slot.leadInBLine.enabled = true;
                SetSegment2(slot.leadOutLine, 0.068f, 0.000f, 0.100f, 0.000f);
                break;

            case NodeType.NOT:
                slot.bodyLine.loop = true;
                slot.bodyLine.positionCount = 3;
                ieeePolyBuffer[0] = new Vector3(-0.060f, 0.044f, 0f);
                ieeePolyBuffer[1] = new Vector3(0.034f, 0.000f, 0f);
                ieeePolyBuffer[2] = new Vector3(-0.060f, -0.044f, 0f);
                slot.bodyLine.SetPositions(ieeePolyBuffer);
                BuildCircleContour(slot.extraLine, centerX: 0.049f, centerY: 0.000f, radius: 0.015f, segments: 12);
                slot.extraLine.enabled = true;
                SetSegment2(slot.leadInALine, -0.100f, 0.000f, -0.060f, 0.000f);
                slot.leadInBLine.enabled = false;
                SetSegment2(slot.leadOutLine, 0.064f, 0.000f, 0.100f, 0.000f);
                break;

            case NodeType.OR:
                BuildOrGateContour(slot.bodyLine, leftX: -0.064f, tipX: 0.060f, bulgeX: 0.022f, halfH: 0.044f);
                slot.extraLine.enabled = false;
                SetSegment2(slot.leadInALine, -0.098f, 0.022f, -0.0475f, 0.022f);
                SetSegment2(slot.leadInBLine, -0.098f, -0.022f, -0.0475f, -0.022f);
                slot.leadInBLine.enabled = true;
                SetSegment2(slot.leadOutLine, 0.060f, 0.000f, 0.098f, 0.000f);
                break;

            case NodeType.XOR:
                BuildOrGateContour(slot.bodyLine, leftX: -0.052f, tipX: 0.066f, bulgeX: 0.022f, halfH: 0.044f);
                BuildRearConcaveArc(slot.extraLine, leftX: -0.072f, bulgeX: 0.022f, halfH: 0.044f, segments: 9);
                slot.extraLine.enabled = true;
                SetSegment2(slot.leadInALine, -0.100f, 0.022f, -0.0555f, 0.022f);
                SetSegment2(slot.leadInBLine, -0.100f, -0.022f, -0.0555f, -0.022f);
                slot.leadInBLine.enabled = true;
                SetSegment2(slot.leadOutLine, 0.066f, 0.000f, 0.100f, 0.000f);
                break;
        }
    }

    private void BuildDGateContour(LineRenderer lr, float leftX, float arcCenterX, float radiusX, float halfH)
    {
        const int arcSamples = 11;
        int totalPoints = 2 + arcSamples;
        lr.loop = true;
        lr.positionCount = totalPoints;

        int idx = 0;
        ieeePolyBuffer[idx++] = new Vector3(leftX, -halfH, 0f);
        ieeePolyBuffer[idx++] = new Vector3(leftX, halfH, 0f);

        for (int i = 0; i < arcSamples; i++)
        {
            float t = i / (float)(arcSamples - 1);
            float angle = Mathf.Lerp(Mathf.PI * 0.5f, -Mathf.PI * 0.5f, t);
            float x = arcCenterX + (Mathf.Cos(angle) * radiusX);
            float y = Mathf.Sin(angle) * halfH;
            ieeePolyBuffer[idx++] = new Vector3(x, y, 0f);
        }

        lr.SetPositions(ieeePolyBuffer);
    }

    private void BuildOrGateContour(LineRenderer lr, float leftX, float tipX, float bulgeX, float halfH)
    {
        const int backSamples = 7;
        const int curveSamples = 7;
        lr.loop = true;

        int idx = 0;
        for (int i = 0; i < backSamples; i++)
        {
            float u = Mathf.Lerp(-1f, 1f, i / (float)(backSamples - 1));
            float x = leftX + (bulgeX * (1f - (u * u)));
            float y = halfH * u;
            ieeePolyBuffer[idx++] = new Vector3(x, y, 0f);
        }

        float ctrlX = Mathf.Lerp(leftX, tipX, 0.64f);
        for (int i = 1; i < curveSamples; i++)
        {
            float t = i / (float)(curveSamples - 1);
            float inv = 1f - t;
            float x = (inv * inv * leftX) + (2f * inv * t * ctrlX) + (t * t * tipX);
            float y = (inv * inv * halfH) + (2f * inv * t * halfH);
            ieeePolyBuffer[idx++] = new Vector3(x, y, 0f);
        }

        for (int i = 1; i < curveSamples - 1; i++)
        {
            float t = i / (float)(curveSamples - 1);
            float inv = 1f - t;
            float x = (inv * inv * tipX) + (2f * inv * t * ctrlX) + (t * t * leftX);
            float y = (2f * inv * t * (-halfH)) + (t * t * (-halfH));
            ieeePolyBuffer[idx++] = new Vector3(x, y, 0f);
        }

        lr.positionCount = idx;
        lr.SetPositions(ieeePolyBuffer);
    }

    private void BuildRearConcaveArc(LineRenderer lr, float leftX, float bulgeX, float halfH, int segments)
    {
        lr.loop = false;
        lr.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float u = Mathf.Lerp(-1f, 1f, i / (float)(segments - 1));
            float x = leftX + (bulgeX * (1f - (u * u)));
            float y = halfH * u;
            ieeePolyBuffer[i] = new Vector3(x, y, 0f);
        }
        lr.SetPositions(ieeePolyBuffer);
    }

    private void BuildCircleContour(LineRenderer lr, float centerX, float centerY, float radius, int segments)
    {
        lr.loop = true;
        lr.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            ieeePolyBuffer[i] = new Vector3(
                centerX + (Mathf.Cos(angle) * radius),
                centerY + (Mathf.Sin(angle) * radius),
                0f);
        }
        lr.SetPositions(ieeePolyBuffer);
    }

    private static void SetSegment2(LineRenderer lr, float x0, float y0, float x1, float y1)
    {
        lr.loop = false;
        lr.positionCount = 2;
        lr.SetPosition(0, new Vector3(x0, y0, 0f));
        lr.SetPosition(1, new Vector3(x1, y1, 0f));
    }

    // =========================================================================
    // AREA TENGAH 3D: LENCANA BINER 0/1 PADA UJUNG PORT TERSAMBUNG (ZOOM ALAMI)
    // =========================================================================

    private void UpdateConnectedPortBinaryBadges(Transform camT, float zoomRatio, AirWeavingCableRouter cableRouter)
    {
        Vector3 camPos = camT.position;
        Vector3 camForward = camT.forward;
        Vector3 camUp = camT.up;
        Quaternion camRot = camT.rotation;

        int usedCount = 0;
        int totalPorts = cableRouter != null ? cableRouter.ActivePortCount : 0;

        for (int i = 0; i < totalPorts && usedCount < MaxPortBadgeSlots; i++)
        {
            if (!cableRouter.TryGetActivePortSnapshot(
                    i,
                    out GateNode portNode,
                    out bool isOutput,
                    out int slotIndex,
                    out Vector3 handleWorldPos,
                    out Vector3 socketAxisWorld,
                    out bool isConnected,
                    out bool signalHigh,
                    out float visualApparentScale))
            {
                continue;
            }

            _ = portNode;
            _ = isOutput;
            _ = slotIndex;
            _ = visualApparentScale;

            if (!isConnected)
            {
                continue;
            }

            float camDepth = Vector3.Dot(handleWorldPos - camPos, camForward);
            if (camDepth <= 0.35f)
            {
                continue;
            }

            PortBadgeSlot slot = portBadgePool[usedCount++];
            if (!slot.rootObj.activeSelf)
            {
                slot.rootObj.SetActive(true);
            }

            float distRatio = Mathf.Clamp(camDepth / ReferenceWorkbenchDepth, 0.16f, 2.25f);
            float depthProximityGain = Mathf.Clamp(ReferenceWorkbenchDepth / camDepth, 0.50f, 2.75f);
            float naturalZoomGain = Mathf.Clamp(zoomRatio * Mathf.Lerp(1f, depthProximityGain, 0.72f), 0.55f, 2.65f);
            float badgeScale = 1.12f * distRatio * naturalZoomGain;

            Vector3 badgePos = handleWorldPos
                               + (camUp * (0.078f * badgeScale))
                               + (socketAxisWorld * (0.014f * badgeScale))
                               - (camForward * 0.028f);

            slot.rootTransform.position = badgePos;
            slot.rootTransform.rotation = camRot;
            slot.rootTransform.localScale = Vector3.one * badgeScale;
            slot.borderLine.widthMultiplier = 0.0058f * badgeScale;

            int bit = signalHigh ? 1 : 0;
            if (bit != slot.lastBit)
            {
                slot.lastBit = bit;
                Color bitColor = signalHigh ? SignalHighColor : SignalLowColor;
                slot.bitText.text = signalHigh ? StrOne : StrZero;
                slot.bitText.color = bitColor;
                ApplyLineColor(slot.borderLine, slot.borderMat, bitColor);
            }
        }

        for (int i = usedCount; i < MaxPortBadgeSlots; i++)
        {
            if (portBadgePool[i] != null && portBadgePool[i].rootObj.activeSelf)
            {
                portBadgePool[i].rootObj.SetActive(false);
            }
        }
    }

    // =========================================================================
    // HELPERS (ZERO-GC, ZERO COLLIDERS, RAYCAST-SAFE 2D & OPENGLES3 3D)
    // =========================================================================

    private void SetHudCanvasVisible(bool visible)
    {
        if (hudCanvasObj != null && hudCanvasObj.activeSelf != visible)
        {
            hudCanvasObj.SetActive(visible);
        }
    }

    private void SetBillboardRootVisible(bool visible)
    {
        if (billboardOverlaysRootObj != null && billboardOverlaysRootObj.activeSelf != visible)
        {
            billboardOverlaysRootObj.SetActive(visible);
        }
    }

    private void HideAllGateSymbolsAndPortBadges()
    {
        for (int i = 0; i < MaxGateSymbolSlots; i++)
        {
            if (gateSymbolPool[i] != null && gateSymbolPool[i].rootObj != null && gateSymbolPool[i].rootObj.activeSelf)
            {
                gateSymbolPool[i].rootObj.SetActive(false);
            }
        }

        for (int i = 0; i < MaxPortBadgeSlots; i++)
        {
            if (portBadgePool[i] != null && portBadgePool[i].rootObj != null && portBadgePool[i].rootObj.activeSelf)
            {
                portBadgePool[i].rootObj.SetActive(false);
            }
        }
    }

    private void ResetTransientHistoriesAndDirtyState()
    {
        sandboxTestedRowMask = 0;
        sandboxMeasuredHighMask = 0;
        waveHistoryInitialized = false;
        InvalidateDirtyCaches();
    }

    private void InvalidateDirtyCaches()
    {
        lastLevelIndex = int.MinValue;
        lastSandboxFlag = -1;
        lastInputABit = -1;
        lastInputBBit = -1;
        lastOutputBit = -1;
        lastTopologyVersion = int.MinValue;
        lastActiveGateCount = -1;
        lastActiveWireCount = -1;
        lastPassedRowMask = -1;
        lastFailedRowMask = -1;
        lastCircuitCompleteFlag = -1;
        lastVerifyingFlag = -1;
    }

    private static RectTransform CreateAnchorUiPanel(string name, RectTransform parent, float anchorMinX, float anchorMaxX, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(anchorMinX, 0f);
        rect.anchorMax = new Vector2(anchorMaxX, 1f);
        rect.offsetMin = new Vector2(0f, 2f);
        rect.offsetMax = new Vector2(0f, -2f);

        Image img = obj.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        CompactHudMenuController.ApplyRoundedSquircle(img);
        return rect;
    }

    private static void CreateVerticalAnchorDivider(string name, RectTransform parent, float anchorX, float height)
    {
        CreateUiRectImage(
            name,
            parent,
            new Vector2(anchorX, 0.5f),
            new Vector2(anchorX, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(1.5f, height),
            StripDividerColor);
    }

    private static Image CreateUiRectImage(
        string name,
        RectTransform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPos,
        Vector2 sizeDelta,
        Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = sizeDelta;

        Image img = obj.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private TextMeshProUGUI CreateUiTmp(
        string name,
        RectTransform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax,
        float fontSize,
        float minSize,
        float maxSize,
        Color color,
        TextAlignmentOptions alignment,
        string initialText)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        TextMeshProUGUI tmp = obj.GetComponent<TextMeshProUGUI>();
        if (cachedPreferredFont != null)
        {
            tmp.font = cachedPreferredFont;
        }
        tmp.text = initialText;
        tmp.fontSize = fontSize;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = minSize;
        tmp.fontSizeMax = maxSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.color = color;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void ApplyLineColor(LineRenderer lr, Material mat, Color color)
    {
        if (mat != null)
        {
            mat.color = color;
        }
        if (lr != null)
        {
            lr.startColor = color;
            lr.endColor = color;
        }
    }

    private Renderer CreateNoColliderQuad(string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Quad);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPos;
        obj.transform.localRotation = Quaternion.identity;
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
            if (mat != null)
            {
                rend.sharedMaterial = mat;
            }
        }
        return rend;
    }

    private TextMeshPro CreateWorldTmp(
        string name,
        Transform parent,
        Vector3 localPos,
        Vector2 sizeDelta,
        float fontSize,
        Color color,
        TextAlignmentOptions alignment,
        int sortingOrder,
        string initialText)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPos;
        obj.transform.localRotation = Quaternion.identity;
        obj.transform.localScale = Vector3.one;

        TextMeshPro tmp = obj.AddComponent<TextMeshPro>();
        if (cachedPreferredFont != null)
        {
            tmp.font = cachedPreferredFont;
        }
        tmp.text = initialText;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.color = color;
        tmp.sortingOrder = sortingOrder;
        if (tmp.rectTransform != null)
        {
            tmp.rectTransform.sizeDelta = sizeDelta;
        }
        return tmp;
    }

    private static LineRenderer CreateLocalLine(
        string name,
        Transform parent,
        int positionCount,
        bool loop,
        float width,
        Material mat,
        int numCorners,
        int numCaps)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = Vector3.zero;
        obj.transform.localRotation = Quaternion.identity;
        obj.transform.localScale = Vector3.one;

        LineRenderer lr = obj.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = loop;
        lr.positionCount = positionCount;
        lr.widthMultiplier = width;
        lr.numCornerVertices = numCorners;
        lr.numCapVertices = numCaps;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        if (mat != null)
        {
            lr.sharedMaterial = mat;
        }
        return lr;
    }

    private static LineRenderer CreateLocalRectBorder(
        string name,
        Transform parent,
        float width,
        float height,
        float localZ,
        float lineWidth,
        Material mat)
    {
        float hw = width * 0.5f;
        float hh = height * 0.5f;
        LineRenderer lr = CreateLocalLine(name, parent, 4, loop: true, lineWidth, mat, numCorners: 1, numCaps: 1);
        lr.SetPosition(0, new Vector3(-hw, -hh, localZ));
        lr.SetPosition(1, new Vector3(hw, -hh, localZ));
        lr.SetPosition(2, new Vector3(hw, hh, localZ));
        lr.SetPosition(3, new Vector3(-hw, hh, localZ));
        return lr;
    }

    private Material CreateSpritesMaterial(Color tint, int renderQueue)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null || !shader.isSupported)
        {
            shader = Shader.Find("UI/Default");
        }
        if (shader == null)
        {
            return null;
        }

        Material mat = new Material(shader);
        mat.color = tint;
        mat.renderQueue = renderQueue;
        ownedMaterials.Add(mat);
        return mat;
    }
}
