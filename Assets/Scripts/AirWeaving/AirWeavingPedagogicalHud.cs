using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Visual Pedagogis Informatika (Fase E) Hybrid 2 Pita Horizontal 2D Overlay + 3D World-Space khusus Mode Udara
/// (GateWeaver AR v8.5 - Smoked Optical Glass + Brushed Titanium):
/// 1. Pita Horizontal Atas (tepat di bawah Top HUD bar, tinggi 44px): Abstraksi ringkas variabel A &amp; B
///    (mis. "A KARTU 1" | "B MODE 0"), grafik gelombang kotak digital (Square Wave) 16-langkah berbasis segmen
///    UnityEngine.UI.Image pra-alokasi Zero-GC + tombol lipat '-' (28x28) / pil kompak 'INPUT [+]'.
/// 2. Pita Horizontal Bawah (tepi bawah layar, tinggi 56px): Generator ekspresi Boolean live Zero-GC
///    (mis. "Y = NOT (A OR B)") + titik status &amp; label konteks ("LAB: 0 TERKUNCI") + Tabel Kebenaran Horizontal
///    4 sel sejajar ([0,0], [0,1], [1,0], [1,1]) format ringkas (-/1, 0/1 X, 1/1 OK) + tombol lipat '-' / '[+] TABEL'.
/// 3. Sinkronisasi tema v8.5 Smoked Optical Glass (radius 6px, hairline 1px) pada Top HUD Bar &amp; Toast Bawah
///    khusus saat Mode Udara aktif, dengan restorasi penuh 100% saat kembali ke Mode Kartu Vuforia v5.
/// 4. Area Tengah 3D World-Space: Proyeksi simbol skematik standar ANSI/IEEE Std 91 (AND, OR, NOT, NAND, XOR)
///    di atas setiap gerbang aktif + lencana nilai biner 0/1 di setiap ujung port kabel tersambung.
/// - 100% Zero-GC di runtime loop, event-driven, tanpa Collider, raycastTarget=false pada semua elemen non-tombol,
///   ASCII 7-bit murni, 0 perubahan di Core/ &amp; Levels/, 0% regresi Mode Kartu Vuforia v5.
/// </summary>
public sealed class AirWeavingPedagogicalHud : MonoBehaviour
{
    private const float ReferenceWorkbenchDepth = 3.82f;
    private const float ReferenceCanvasWidth = 1920f;
    private const float ReferenceCanvasHeight = 1080f;

    // Geometri 2 Pita Horizontal Ramping v8.5 (ruang referensi 1920x1080, dalam PedHud_SafeArea)
    private const float StripSideMarginX = GwThemeTokens.StripSideMarginX;
    private const float TopStripTopOffsetY = GwThemeTokens.TopStripTopOffsetY;
    private const float TopStripExpandedHeight = GwThemeTokens.TopInputStripHeight;
    private const float TopStripCollapsedWidth = GwThemeTokens.TopCollapsedPillWidth;

    private const float BottomStripBottomMarginY = GwThemeTokens.BottomStripBottomMarginY;
    private const float BottomStripExpandedHeight = GwThemeTokens.BottomOutputStripHeight;
    private const float BottomStripCollapsedWidth = GwThemeTokens.BottomCollapsedPillWidth;
    private const float StripCollapsedHeight = GwThemeTokens.CollapsedPillHeight;
    private const float CollapseBtnSize = GwThemeTokens.CollapseButtonSize;

    // Parameter Gelombang Kotak 16-langkah horizontal ramping berbasis UnityEngine.UI.Image (Zero-GC)
    private const int WaveStepCount = 16;
    private const int WaveEdgeCount = WaveStepCount - 1;
    private const float WaveHighLocalY = 6.5f;
    private const float WaveLowLocalY = -6.5f;
    private const float WaveStrokeThickness = 2.0f;

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
    private const string StrTopCollapsedPill = "INPUT [+]";
    private const string StrBottomCollapsedPill = "[+] TABEL";
    private const string StrUnconnectedFormula = "Y = --";
    private const string StrCycleToken = "(SIKLUS)";
    private static readonly string[] StrComboPrefixes = { "[0,0] ", "[0,1] ", "[1,0] ", "[1,1] " };

    // Palet warna 1:1 GwThemeTokens (Smoked Optical Glass + Brushed Titanium, nol neon glow)
    private static readonly Color SignalHighColor = GwThemeTokens.SignalHighTeal;
    private static readonly Color SignalLowColor = GwThemeTokens.SignalLowSlate;
    private static readonly Color SignalIdleColor = GwThemeTokens.TextSecondary;
    private static readonly Color IeeeBackdropColor = GwThemeTokens.SmokedGlassDense;
    private static readonly Color PortBadgeBgColor = GwThemeTokens.SmokedGlassDense;

    private sealed class SquareWaveUiTrack
    {
        public readonly RectTransform[] stepRects = new RectTransform[WaveStepCount];
        public readonly Image[] stepImages = new Image[WaveStepCount];
        public readonly Image[] edgeImages = new Image[WaveEdgeCount];
    }

    private sealed class TruthCellUi
    {
        public Image bgImage;
        public Image borderImage;
        public Image activeBarImage;
        public TextMeshProUGUI txtSummary;
    }

    private struct SavedImageStyle
    {
        public Image image;
        public Sprite sprite;
        public Image.Type type;
        public float ppuMultiplier;
        public Color color;
    }

    private struct SavedOutlineStyle
    {
        public Outline outline;
        public bool enabled;
        public Color effectColor;
        public Vector2 effectDistance;
    }

    private struct SavedTmpStyle
    {
        public TMP_Text text;
        public Color color;
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
    private Image topStripBg;
    private Image topStripBorder;
    private GameObject topStripBodyObj;
    private Button btnToggleTopStrip;
    private RectTransform btnToggleTopRect;
    private Image btnToggleTopBg;
    private Image btnToggleTopBorder;
    private Image imgToggleTopMinusBar;
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
    private Image bottomStripBg;
    private Image bottomStripBorder;
    private GameObject bottomStripBodyObj;
    private Button btnToggleBottomStrip;
    private RectTransform btnToggleBottomRect;
    private Image btnToggleBottomBg;
    private Image btnToggleBottomBorder;
    private Image imgToggleBottomMinusBar;
    private TextMeshProUGUI txtToggleBottomLabel;
    private TextMeshProUGUI txtBooleanFormula;
    private Image imgOutputStatusDot;
    private TextMeshProUGUI txtOutputContext;
    private readonly TruthCellUi[] truthCells = new TruthCellUi[TruthRowCount];
    private bool isBottomStripCollapsed = false;

    // Sinkronisasi tema v8.5 Smoked Optical Glass untuk Top HUD Bar & Toast Bawah (Zero-GC, lossless restore Mode Kartu)
    private CompactHudMenuController externalHudMenu;
    private TMP_Text externalFeedbackText;
    private bool externalHudThemeCached = false;
    private bool externalHudThemeApplied = false;
    private readonly List<SavedImageStyle> savedExternalImages = new List<SavedImageStyle>(28);
    private readonly List<SavedOutlineStyle> savedExternalOutlines = new List<SavedOutlineStyle>(6);
    private readonly List<SavedTmpStyle> savedExternalTmps = new List<SavedTmpStyle>(8);

    private Image extLevelPillBg;
    private Image extLevelPillBorder;
    private Image extLevelBadgeBg;
    private Image extLevelBadgeBorder;
    private TMP_Text extLevelBadgeTxt;
    private TMP_Text extLevelTitleTxt;
    private Image extMissionPillBg;
    private Image extMissionPillBorder;
    private TMP_Text extMissionDescTxt;
    private Image extQuotaPillBg;
    private Image extQuotaPillBorder;
    private Outline extQuotaPillOutline;
    private TMP_Text extQuotaStatusTxt;
    private Image extRecenterBg;
    private Image extRecenterBorder;
    private Outline extRecenterOutline;
    private readonly Image[] extRecenterIcons = new Image[6];
    private Image extGuideBg;
    private Image extGuideBorder;
    private Outline extGuideOutline;
    private TMP_Text extGuideIconTxt;
    private Image extMenuBg;
    private Image extMenuBorder;
    private Outline extMenuOutline;
    private readonly Image[] extMenuBars = new Image[3];
    private GameObject extToastBackdropObj;
    private Image extToastBg;
    private Image extToastBorder;
    private Outline extToastOutline;

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

    public void BindExternalHudTargets(CompactHudMenuController hudMenu, TMP_Text feedbackText)
    {
        if (hudMenu != null)
        {
            externalHudMenu = hudMenu;
        }
        if (feedbackText != null)
        {
            externalFeedbackText = feedbackText;
        }
    }

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
        RestoreExternalTopHudAndToastTheme();

        if (!isInitialized)
        {
            return;
        }

        HideAllGateSymbolsAndPortBadges();
        SetHudCanvasVisible(false);
        SetBillboardRootVisible(false);
    }

    /// <summary>
    /// Pembaruan event-driven Pita 2D Atas/Bawah, sinkronisasi tema Top HUD &amp; Toast v8.5,
    /// serta 3D Simbol IEEE &amp; Lencana Port 0/1 dengan zoom alami proporsional.
    /// Dipanggil dari AirWeavingController.LateUpdate(). 100% Zero-GC.
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
        int debugFailedRowMask,
        bool isGuideOverlayOpen = false)
    {
        _ = stageRoot;
        if (!isInitialized)
        {
            return;
        }

        if (!isAirWeavingActive || !isHudActive || mainCam == null)
        {
            RestoreExternalTopHudAndToastTheme();
            SetHudCanvasVisible(false);
            SetBillboardRootVisible(false);
            return;
        }

        // 0. Pertahankan tema Smoked Optical Glass v8.5 pada Top HUD Bar & Toast Bawah selama Mode Udara aktif
        SyncExternalTopHudAndToastTheme(levelManager, circuitManager, isGuideOverlayOpen);

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
        RestoreExternalTopHudAndToastTheme();
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
        topStripBg = null;
        topStripBorder = null;
        topStripBodyObj = null;
        btnToggleTopRect = null;
        btnToggleTopBg = null;
        btnToggleTopBorder = null;
        imgToggleTopMinusBar = null;
        bottomStripRect = null;
        bottomStripBg = null;
        bottomStripBorder = null;
        bottomStripBodyObj = null;
        btnToggleBottomRect = null;
        btnToggleBottomBg = null;
        btnToggleBottomBorder = null;
        imgToggleBottomMinusBar = null;
        imgOutputStatusDot = null;
        gateSymbolsRootObj = null;
        portBadgesRootObj = null;

        savedExternalImages.Clear();
        savedExternalOutlines.Clear();
        savedExternalTmps.Clear();
        externalHudThemeCached = false;
        externalHudThemeApplied = false;
        externalHudMenu = null;
        externalFeedbackText = null;

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

        topStripBg = stripObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(topStripBg, GwThemeTokens.SmokedGlass);
        topStripBg.raycastTarget = false;

        topStripBorder = GwThemeTokens.AttachHairlineBorder(topStripRect, "TopStrip_Border", GwThemeTokens.HairlineIdle);

        // Kontainer isi 1 baris horizontal ramping H=44px (ruang 44px di kanan untuk tombol '-' 28x28)
        topStripBodyObj = new GameObject("TopStrip_Body", typeof(RectTransform));
        topStripBodyObj.transform.SetParent(topStripRect, false);
        RectTransform bodyRect = topStripBodyObj.GetComponent<RectTransform>();
        bodyRect.anchorMin = Vector2.zero;
        bodyRect.anchorMax = Vector2.one;
        bodyRect.offsetMin = new Vector2(14f, 4f);
        bodyRect.offsetMax = new Vector2(-44f, -4f);

        // 1. Kanal A (kiri, 0.000..0.488): "A  KARTU  1" + Square Wave
        RectTransform chARect = CreateTransparentAnchorContainer("TopStrip_ChannelA", bodyRect, 0.000f, 0.488f);
        CreateUiTmp("ChA_Var", chARect, new Vector2(0.000f, 0f), new Vector2(0.040f, 1f), new Vector2(2f, 0f), Vector2.zero, 16f, 12f, 17f, GwThemeTokens.TextPrimary, TextAlignmentOptions.MidlineLeft, "A", FontStyles.Bold);
        txtInputATitle = CreateUiTmp("ChA_Title", chARect, new Vector2(0.042f, 0f), new Vector2(0.225f, 1f), new Vector2(2f, 0f), new Vector2(-4f, 0f), 14.5f, 11f, 15.5f, GwThemeTokens.TextSecondary, TextAlignmentOptions.MidlineLeft, "INPUT A", FontStyles.Normal);
        txtInputAState = CreateUiTmp("ChA_State", chARect, new Vector2(0.228f, 0f), new Vector2(0.278f, 1f), Vector2.zero, Vector2.zero, 17f, 12.5f, 18f, GwThemeTokens.SignalLowSlate, TextAlignmentOptions.Center, StrZero, FontStyles.Bold);
        waveTrackA = BuildHorizontalSquareWaveTrack("WaveTrack_A", chARect, 0.292f, 0.992f, GwThemeTokens.SignalLowSlate);

        // Pembatas vertikal 1px HairlineIdle tepat di tengah
        CreateVerticalAnchorDivider("TopStrip_DivMid", bodyRect, 0.500f, 24f);

        // 2. Kanal B (kanan, 0.512..1.000): "B  MODE  0" + Square Wave
        RectTransform chBRect = CreateTransparentAnchorContainer("TopStrip_ChannelB", bodyRect, 0.512f, 1.000f);
        CreateUiTmp("ChB_Var", chBRect, new Vector2(0.000f, 0f), new Vector2(0.040f, 1f), new Vector2(2f, 0f), Vector2.zero, 16f, 12f, 17f, GwThemeTokens.TextPrimary, TextAlignmentOptions.MidlineLeft, "B", FontStyles.Bold);
        txtInputBTitle = CreateUiTmp("ChB_Title", chBRect, new Vector2(0.042f, 0f), new Vector2(0.225f, 1f), new Vector2(2f, 0f), new Vector2(-4f, 0f), 14.5f, 11f, 15.5f, GwThemeTokens.TextSecondary, TextAlignmentOptions.MidlineLeft, "INPUT B", FontStyles.Normal);
        txtInputBState = CreateUiTmp("ChB_State", chBRect, new Vector2(0.228f, 0f), new Vector2(0.278f, 1f), Vector2.zero, Vector2.zero, 17f, 12.5f, 18f, GwThemeTokens.SignalLowSlate, TextAlignmentOptions.Center, StrZero, FontStyles.Bold);
        waveTrackB = BuildHorizontalSquareWaveTrack("WaveTrack_B", chBRect, 0.292f, 0.992f, GwThemeTokens.SignalLowSlate);

        // 3. Tombol Lipat '-' (28x28) / Pil Kompak 'INPUT [+]'
        btnToggleTopStrip = CreateStripCollapseButton(
            "Btn_ToggleTopStrip",
            topStripRect,
            out btnToggleTopRect,
            out btnToggleTopBg,
            out btnToggleTopBorder,
            out imgToggleTopMinusBar,
            out txtToggleTopLabel);
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
        trackRect.offsetMin = new Vector2(0f, -12f);
        trackRect.offsetMax = new Vector2(0f, 12f); // tinggi total track 24px dalam pita 44px

        Image trackBg = trackObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(trackBg, GwThemeTokens.SmokedGlassSoft);
        trackBg.raycastTarget = false;

        // Area plot dinamis proporsional (otomatis rapi pada 4:3, 16:9, maupun 20:9)
        GameObject plotObj = new GameObject("WavePlotArea", typeof(RectTransform));
        plotObj.transform.SetParent(trackRect, false);
        RectTransform plotRect = plotObj.GetComponent<RectTransform>();
        plotRect.anchorMin = Vector2.zero;
        plotRect.anchorMax = Vector2.one;
        plotRect.offsetMin = new Vector2(8f, 2f);
        plotRect.offsetMax = new Vector2(-8f, -2f);

        // Rel referensi HIGH & LOW tipis 1px
        CreateUiRectImage("Rail_High", plotRect, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, WaveHighLocalY), new Vector2(0f, 1.0f), GwThemeTokens.WaveRailFaint);
        CreateUiRectImage("Rail_Low", plotRect, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, WaveLowLocalY), new Vector2(0f, 1.0f), GwThemeTokens.WaveRailFaint);

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
                new Vector2(0.6f, WaveStrokeThickness),
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

        bottomStripBg = stripObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(bottomStripBg, GwThemeTokens.SmokedGlass);
        bottomStripBg.raycastTarget = false;

        bottomStripBorder = GwThemeTokens.AttachHairlineBorder(bottomStripRect, "BottomStrip_Border", GwThemeTokens.HairlineIdle);

        // Kontainer isi 1 baris horizontal H=56px (ruang 44px di kanan untuk tombol '-' 28x28)
        bottomStripBodyObj = new GameObject("BottomStrip_Body", typeof(RectTransform));
        bottomStripBodyObj.transform.SetParent(bottomStripRect, false);
        RectTransform bodyRect = bottomStripBodyObj.GetComponent<RectTransform>();
        bodyRect.anchorMin = Vector2.zero;
        bodyRect.anchorMax = Vector2.one;
        bodyRect.offsetMin = new Vector2(14f, 4f);
        bodyRect.offsetMax = new Vector2(-44f, -4f);

        // 1. Bagian Kiri (0.000..0.340): Rumus Boolean Live ("Y = NOT (A OR B)") + titik status & label ("LAB: 0 TERKUNCI")
        RectTransform leftBlock = CreateTransparentAnchorContainer("Bottom_FormulaBlock", bodyRect, 0.000f, 0.340f);
        txtBooleanFormula = CreateUiTmp(
            "Bottom_FormulaTxt",
            leftBlock,
            new Vector2(0f, 0.50f),
            new Vector2(1f, 0.98f),
            new Vector2(4f, 0f),
            new Vector2(-6f, -1f),
            18f,
            12.5f,
            19.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            StrUnconnectedFormula,
            FontStyles.Bold);

        GameObject dotObj = new GameObject("Bottom_OutputStatusDot", typeof(RectTransform), typeof(Image));
        dotObj.transform.SetParent(leftBlock, false);
        RectTransform dotRect = dotObj.GetComponent<RectTransform>();
        dotRect.anchorMin = new Vector2(0f, 0.28f);
        dotRect.anchorMax = new Vector2(0f, 0.28f);
        dotRect.pivot = new Vector2(0.5f, 0.5f);
        dotRect.anchoredPosition = new Vector2(8f, 1f);
        dotRect.sizeDelta = new Vector2(GwThemeTokens.StatusDotSizePx, GwThemeTokens.StatusDotSizePx);

        imgOutputStatusDot = dotObj.GetComponent<Image>();
        imgOutputStatusDot.sprite = GwThemeTokens.GetCircleSprite();
        imgOutputStatusDot.type = Image.Type.Simple;
        imgOutputStatusDot.color = GwThemeTokens.SignalLowSlate;
        imgOutputStatusDot.raycastTarget = false;

        txtOutputContext = CreateUiTmp(
            "Bottom_OutputCtxTxt",
            leftBlock,
            new Vector2(0f, 0.06f),
            new Vector2(1f, 0.50f),
            new Vector2(18f, 2f),
            new Vector2(-6f, 0f),
            13.5f,
            10.5f,
            14.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            "OUTPUT: --",
            FontStyles.Normal);

        CreateVerticalAnchorDivider("Bottom_MidDiv", bodyRect, 0.348f, 34f);

        // 2. Bagian Kanan (0.358..0.996): 4 Sel Tabel Kebenaran Horizontal Sejajar ([0,0]..[1,1])
        const float cellsMinX = 0.358f;
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
            cellRect.offsetMin = new Vector2(4f, 4f);
            cellRect.offsetMax = new Vector2(-4f, -4f);

            Image cellBg = cellObj.GetComponent<Image>();
            GwThemeTokens.ApplyRoundedRect6(cellBg, GwThemeTokens.CardIdle);
            cellBg.raycastTarget = false;

            // Hairline border 1px seragam (idle / active / error #EF4444 / pass #10B981)
            Image cellBorder = GwThemeTokens.AttachHairlineBorder(cellRect, "Cell_Border", GwThemeTokens.HairlineIdle);

            // Indikator tipis 2px di bawah sel untuk menandai baris kombinasi sakelar aktif saat ini
            Image activeBar = CreateUiRectImage(
                "Cell_ActiveBar",
                cellRect,
                new Vector2(0.14f, 0f),
                new Vector2(0.86f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 2f),
                new Vector2(0f, 2f),
                GwThemeTokens.SignalHighTeal);
            activeBar.enabled = false;

            // Teks ringkas sel: "[0,0] -/1", "[1,0] 0/1 X", "[0,0] 1/1 OK"
            TextMeshProUGUI txtSummary = CreateUiTmp(
                "Cell_SummaryTxt",
                cellRect,
                Vector2.zero,
                Vector2.one,
                new Vector2(8f, 0f),
                new Vector2(-8f, 0f),
                15.5f,
                11.5f,
                16.5f,
                GwThemeTokens.TextSecondary,
                TextAlignmentOptions.Center,
                StrComboPrefixes[r],
                FontStyles.Bold);

            truthCells[r] = new TruthCellUi
            {
                bgImage = cellBg,
                borderImage = cellBorder,
                activeBarImage = activeBar,
                txtSummary = txtSummary
            };
        }

        // 3. Tombol Lipat '-' (28x28) / Pil Kompak '[+] TABEL'
        btnToggleBottomStrip = CreateStripCollapseButton(
            "Btn_ToggleBottomStrip",
            bottomStripRect,
            out btnToggleBottomRect,
            out btnToggleBottomBg,
            out btnToggleBottomBorder,
            out imgToggleBottomMinusBar,
            out txtToggleBottomLabel);
        btnToggleBottomStrip.onClick.AddListener(ToggleBottomStripCollapsed);
    }

    private Button CreateStripCollapseButton(
        string name,
        RectTransform parentStrip,
        out RectTransform rectOut,
        out Image bgOut,
        out Image borderOut,
        out Image minusBarOut,
        out TextMeshProUGUI labelOut)
    {
        GameObject btnObj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(parentStrip, false);
        rectOut = btnObj.GetComponent<RectTransform>();

        bgOut = btnObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(bgOut, GwThemeTokens.CardIdle);
        bgOut.raycastTarget = true;

        borderOut = GwThemeTokens.AttachHairlineBorder(rectOut, "Btn_Border", GwThemeTokens.HairlineIdle);

        Button btn = btnObj.GetComponent<Button>();
        btn.targetGraphic = bgOut;
        btn.transition = Selectable.Transition.ColorTint;
        btn.colors = GwThemeTokens.FlatGlassButtonColors;

        // Ikon '-' geometris prosedural (10x2px): selalu tajam & identik pada tombol 28x28 atas maupun bawah
        minusBarOut = CreateUiRectImage(
            "Icon_MinusBar",
            rectOut,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(10f, 2f),
            GwThemeTokens.TextPrimary);

        labelOut = CreateUiTmp(
            "Txt_Toggle",
            rectOut,
            Vector2.zero,
            Vector2.one,
            new Vector2(6f, 0f),
            new Vector2(-6f, 0f),
            14f,
            11f,
            16f,
            GwThemeTokens.TextPrimary,
            TextAlignmentOptions.Center,
            string.Empty,
            FontStyles.Bold);
        labelOut.enabled = false;
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
        // 1. Pita Horizontal Atas: H=44px melebar penuh, atau mengecil jadi pil 'INPUT [+]' (SmokedGlassDense + HairlineIdle)
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
        if (topStripBg != null)
        {
            topStripBg.enabled = !isTopStripCollapsed;
        }
        if (topStripBorder != null)
        {
            topStripBorder.enabled = !isTopStripCollapsed;
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
                btnToggleTopRect.offsetMin = Vector2.zero;
                btnToggleTopRect.offsetMax = Vector2.zero;
            }
            else
            {
                btnToggleTopRect.anchorMin = new Vector2(1f, 0.5f);
                btnToggleTopRect.anchorMax = new Vector2(1f, 0.5f);
                btnToggleTopRect.pivot = new Vector2(1f, 0.5f);
                btnToggleTopRect.anchoredPosition = new Vector2(-8f, 0f);
                btnToggleTopRect.sizeDelta = new Vector2(CollapseBtnSize, CollapseBtnSize);
            }
        }
        if (btnToggleTopBg != null)
        {
            btnToggleTopBg.color = isTopStripCollapsed ? GwThemeTokens.SmokedGlassDense : GwThemeTokens.CardIdle;
        }
        if (btnToggleTopBorder != null)
        {
            btnToggleTopBorder.color = GwThemeTokens.HairlineIdle;
        }
        if (imgToggleTopMinusBar != null)
        {
            imgToggleTopMinusBar.enabled = !isTopStripCollapsed;
            imgToggleTopMinusBar.color = GwThemeTokens.TextPrimary;
        }
        if (txtToggleTopLabel != null)
        {
            txtToggleTopLabel.enabled = isTopStripCollapsed;
            if (isTopStripCollapsed)
            {
                txtToggleTopLabel.text = StrTopCollapsedPill;
                txtToggleTopLabel.color = GwThemeTokens.TextPrimary;
            }
        }

        // 2. Pita Horizontal Bawah: H=56px melebar penuh, atau mengecil jadi pil '[+] TABEL' (SmokedGlassDense + HairlineIdle)
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
        if (bottomStripBg != null)
        {
            bottomStripBg.enabled = !isBottomStripCollapsed;
        }
        if (bottomStripBorder != null)
        {
            bottomStripBorder.enabled = !isBottomStripCollapsed;
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
                btnToggleBottomRect.offsetMin = Vector2.zero;
                btnToggleBottomRect.offsetMax = Vector2.zero;
            }
            else
            {
                btnToggleBottomRect.anchorMin = new Vector2(1f, 0.5f);
                btnToggleBottomRect.anchorMax = new Vector2(1f, 0.5f);
                btnToggleBottomRect.pivot = new Vector2(1f, 0.5f);
                btnToggleBottomRect.anchoredPosition = new Vector2(-8f, 0f);
                btnToggleBottomRect.sizeDelta = new Vector2(CollapseBtnSize, CollapseBtnSize);
            }
        }
        if (btnToggleBottomBg != null)
        {
            btnToggleBottomBg.color = isBottomStripCollapsed ? GwThemeTokens.SmokedGlassDense : GwThemeTokens.CardIdle;
        }
        if (btnToggleBottomBorder != null)
        {
            btnToggleBottomBorder.color = GwThemeTokens.HairlineIdle;
        }
        if (imgToggleBottomMinusBar != null)
        {
            imgToggleBottomMinusBar.enabled = !isBottomStripCollapsed;
            imgToggleBottomMinusBar.color = GwThemeTokens.TextPrimary;
        }
        if (txtToggleBottomLabel != null)
        {
            txtToggleBottomLabel.enabled = isBottomStripCollapsed;
            if (isBottomStripCollapsed)
            {
                txtToggleBottomLabel.text = StrBottomCollapsedPill;
                txtToggleBottomLabel.color = GwThemeTokens.TextPrimary;
            }
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
        RefreshSquareWaveUiTrack(waveTrackA, waveHistoryA, GwThemeTokens.GetSignalColor(curA));
        RefreshSquareWaveUiTrack(waveTrackB, waveHistoryB, GwThemeTokens.GetSignalColor(curB));
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

        RefreshSquareWaveUiTrack(waveTrackA, waveHistoryA, GwThemeTokens.GetSignalColor(nextA));
        RefreshSquareWaveUiTrack(waveTrackB, waveHistoryB, GwThemeTokens.GetSignalColor(nextB));
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
            txtInputATitle.text = cachedInAName;
        }

        if (txtInputAState != null)
        {
            txtInputAState.text = curA ? StrOne : StrZero;
            txtInputAState.color = GwThemeTokens.GetSignalColor(curA);
        }

        if (txtInputBTitle != null)
        {
            txtInputBTitle.text = cachedInBName;
        }

        if (txtInputBState != null)
        {
            txtInputBState.text = curB ? StrOne : StrZero;
            txtInputBState.color = GwThemeTokens.GetSignalColor(curB);
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
        // 1. Generator ekspresi Boolean live Zero-GC ("Y = NOT (A OR B)")
        BuildLiveBooleanExpressionZeroGc(outputNode, out bool isConnected, out bool hasMissingBranch, out bool hasCycle);
        lastFormulaIsComplete = isConnected && !hasMissingBranch && !hasCycle;

        if (txtBooleanFormula != null)
        {
            txtBooleanFormula.SetText(textBuilder);
            if (!isConnected)
            {
                txtBooleanFormula.color = GwThemeTokens.TextSecondary;
            }
            else if (hasMissingBranch || hasCycle)
            {
                txtBooleanFormula.color = GwThemeTokens.AccentSwitchOrange;
            }
            else
            {
                txtBooleanFormula.color = GwThemeTokens.TextPrimary;
            }
        }

        // 2. Baris status Output kontekstual live (titik status + "LAB: 0 TERKUNCI")
        bool curA = inputANode != null && inputANode.currentOutputValue;
        bool curB = inputBNode != null && inputBNode.currentOutputValue;
        int activeRow = (curA ? 2 : 0) + (curB ? 1 : 0);
        int activeBit = 1 << activeRow;

        bool outputTracked = isConnected && outputNode != null && outputNode.isTracked;
        bool outVal = outputTracked && outputNode.currentOutputValue;

        if (txtOutputContext != null)
        {
            textBuilder.Clear();
            textBuilder.Append(cachedOutName).Append(": ");
            if (outputTracked)
            {
                textBuilder.Append(outVal ? "1 " : "0 ")
                    .Append(outVal ? cachedOut1 : cachedOut0);
            }
            else
            {
                textBuilder.Append("--");
            }
            txtOutputContext.color = GwThemeTokens.TextSecondary;
            txtOutputContext.SetText(textBuilder);
        }

        if (imgOutputStatusDot != null)
        {
            Color dotColor;
            if (!outputTracked)
            {
                dotColor = GwThemeTokens.SignalLowSlate;
            }
            else if (isSandbox)
            {
                dotColor = GwThemeTokens.GetSignalColor(outVal);
            }
            else
            {
                bool activeRowFailed = (failedRowMask & activeBit) != 0;
                bool activeRowPassed = (passedRowMask & activeBit) != 0;
                if (!activeRowFailed && !activeRowPassed && lastFormulaIsComplete && cachedHasExpectedTruth)
                {
                    bool expected = cachedExpectedTruth[activeRow];
                    if (outVal == expected)
                    {
                        activeRowPassed = true;
                    }
                    else
                    {
                        activeRowFailed = true;
                    }
                }

                if (activeRowFailed || failedRowMask != 0)
                {
                    dotColor = GwThemeTokens.StatusErrorRed;
                }
                else if (activeRowPassed)
                {
                    dotColor = GwThemeTokens.StatusPassEmerald;
                }
                else
                {
                    dotColor = GwThemeTokens.AccentSwitchOrange;
                }
            }

            imgOutputStatusDot.color = dotColor;
        }

        // 3. Tabel Kebenaran Horizontal 4 Sel Sejajar: [0,0], [0,1], [1,0], [1,1]
        bool outputReadyForActiveEval = lastFormulaIsComplete && outputNode != null && outputNode.isTracked;
        if (isSandbox && !isVerifying && outputReadyForActiveEval)
        {
            sandboxTestedRowMask |= activeBit;
            if (outputNode.currentOutputValue)
            {
                sandboxMeasuredHighMask |= activeBit;
            }
            else
            {
                sandboxMeasuredHighMask &= ~activeBit;
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
                    cell.txtSummary.color = GwThemeTokens.GetSignalColor(yVal);
                }
                else
                {
                    textBuilder.Append('-');
                    cell.txtSummary.color = isActiveRow ? GwThemeTokens.TextPrimary : GwThemeTokens.TextSecondary;
                }
                cell.txtSummary.SetText(textBuilder);

                cell.bgImage.color = GwThemeTokens.CardIdle;
                ApplyCellBorderAndBar(
                    cell,
                    isActiveRow ? GwThemeTokens.HairlineActive : GwThemeTokens.HairlineIdle,
                    isActiveRow,
                    GwThemeTokens.SignalHighTeal);
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
            Color borderColor;
            Color textColor;
            Color activeBarColor;

            if (isPassed)
            {
                yChar = targetVal ? '1' : '0';
                statusState = 1;
                bgColor = GwThemeTokens.SmokedGlassDense;
                borderColor = GwThemeTokens.StatusPassEmerald;
                textColor = GwThemeTokens.StatusPassText;
                activeBarColor = GwThemeTokens.StatusPassEmerald;
            }
            else if (isFailed)
            {
                yChar = targetVal ? '0' : '1';
                statusState = -1;
                bgColor = GwThemeTokens.SmokedGlassDense;
                borderColor = GwThemeTokens.StatusErrorRed;
                textColor = GwThemeTokens.StatusErrorText;
                activeBarColor = GwThemeTokens.StatusErrorRed;
            }
            else if (isActiveRow && (isCircuitCompleteLive || outputReadyForActiveEval) && outputNode != null && outputNode.isTracked && lastFormulaIsComplete)
            {
                bool liveY = outputNode.currentOutputValue;
                bool matchesTarget = cachedHasExpectedTruth && (liveY == targetVal);
                yChar = liveY ? '1' : '0';
                statusState = cachedHasExpectedTruth ? (matchesTarget ? 1 : -1) : 0;
                if (cachedHasExpectedTruth)
                {
                    bgColor = GwThemeTokens.SmokedGlassDense;
                    borderColor = matchesTarget ? GwThemeTokens.StatusPassEmerald : GwThemeTokens.StatusErrorRed;
                    textColor = matchesTarget ? GwThemeTokens.StatusPassText : GwThemeTokens.StatusErrorText;
                    activeBarColor = borderColor;
                }
                else
                {
                    bgColor = GwThemeTokens.CardIdle;
                    borderColor = GwThemeTokens.HairlineActive;
                    textColor = GwThemeTokens.TextPrimary;
                    activeBarColor = GwThemeTokens.SignalHighTeal;
                }
            }
            else
            {
                // Belum diuji: "[0,0] -/1" tanpa lencana OK/X
                yChar = '-';
                statusState = 0;
                bgColor = GwThemeTokens.CardIdle;
                borderColor = isActiveRow ? GwThemeTokens.HairlineActive : GwThemeTokens.HairlineIdle;
                textColor = isActiveRow ? GwThemeTokens.TextPrimary : GwThemeTokens.TextSecondary;
                activeBarColor = GwThemeTokens.SignalHighTeal;
            }

            textBuilder.Clear();
            textBuilder.Append(StrComboPrefixes[row])
                .Append(yChar)
                .Append('/')
                .Append(tgtChar);
            if (statusState > 0)
            {
                textBuilder.Append(" OK");
            }
            else if (statusState < 0)
            {
                textBuilder.Append(" X");
            }

            cell.txtSummary.color = textColor;
            cell.txtSummary.SetText(textBuilder);
            cell.bgImage.color = bgColor;
            ApplyCellBorderAndBar(cell, borderColor, isActiveRow, activeBarColor);
        }
    }

    private static void ApplyCellBorderAndBar(TruthCellUi cell, Color borderColor, bool isActiveRow, Color barColor)
    {
        if (cell == null)
        {
            return;
        }

        if (cell.borderImage != null)
        {
            cell.borderImage.color = borderColor;
        }
        if (cell.activeBarImage != null)
        {
            if (cell.activeBarImage.enabled != isActiveRow)
            {
                cell.activeBarImage.enabled = isActiveRow;
            }
            if (isActiveRow)
            {
                cell.activeBarImage.color = barColor;
            }
        }
    }

    /// <summary>
    /// Menghasilkan snapshot string ekspresi Boolean rangkaian saat ini (dipanggil event-driven
    /// satu kali saat Modal Verifikasi Menang terbuka, bukan di runtime loop).
    /// </summary>
    public string FormatBooleanExpressionSnapshot(GateNode outputNode)
    {
        BuildLiveBooleanExpressionZeroGc(outputNode, out _, out _, out _);
        return textBuilder.ToString();
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

    // =========================================================================
    // SINKRONISASI TEMA v8.5 TOP HUD BAR & TOAST BAWAH (ZERO-GC, 0 EDIT LEVELS/)
    // =========================================================================

    public void SyncExternalTopHudAndToastTheme(
        LevelAndUIManager levelManager,
        CircuitManager circuitManager,
        bool isGuideOverlayOpen)
    {
        if (!externalHudThemeCached)
        {
            TryCacheExternalHudTargets(levelManager);
            if (!externalHudThemeCached)
            {
                return;
            }
        }

        externalHudThemeApplied = true;

        // 1. Pill Level kiri (SmokedGlass + HairlineIdle 1px + 6px radius)
        bool isSandbox = levelManager != null && levelManager.IsSandboxMode;
        ApplyRect6IfChanged(extLevelPillBg, GwThemeTokens.SmokedGlass);
        ApplyHairlineIfChanged(extLevelPillBorder, GwThemeTokens.HairlineIdle);
        ApplyRect6IfChanged(extLevelBadgeBg, GwThemeTokens.CardIdle);
        ApplyHairlineIfChanged(extLevelBadgeBorder, isSandbox ? GwThemeTokens.SignalHighTeal : GwThemeTokens.HairlineActive);
        ApplyTmpColorIfChanged(extLevelBadgeTxt, GwThemeTokens.TextPrimary);
        ApplyTmpColorIfChanged(extLevelTitleTxt, GwThemeTokens.TextPrimary);

        // 2. Pill Misi tengah (SmokedGlass + HairlineIdle 1px + 6px radius)
        ApplyRect6IfChanged(extMissionPillBg, GwThemeTokens.SmokedGlass);
        ApplyHairlineIfChanged(extMissionPillBorder, GwThemeTokens.HairlineIdle);
        ApplyTmpColorIfChanged(extMissionDescTxt, GwThemeTokens.TextPrimary);

        // 3. Pill Kuota Gerbang kanan
        bool isOverQuota = false;
        if (!isSandbox
            && circuitManager != null
            && levelManager != null
            && levelManager.levels != null
            && levelManager.currentLevelIndex >= 0
            && levelManager.currentLevelIndex < levelManager.levels.Count)
        {
            LevelScenario activeScenario = levelManager.levels[levelManager.currentLevelIndex];
            if (activeScenario != null)
            {
                isOverQuota = circuitManager.activeGateCardsCount > activeScenario.maxGateCardsQuota;
            }
        }

        DisableOutlineIfEnabled(extQuotaPillOutline);
        ApplyRect6IfChanged(extQuotaPillBg, isOverQuota ? GwThemeTokens.SmokedGlassDense : GwThemeTokens.SmokedGlass);
        ApplyHairlineIfChanged(extQuotaPillBorder, isOverQuota ? GwThemeTokens.StatusErrorRed : GwThemeTokens.HairlineIdle);
        ApplyTmpColorIfChanged(
            extQuotaStatusTxt,
            isOverQuota
                ? GwThemeTokens.StatusErrorText
                : (isSandbox ? GwThemeTokens.SignalHighTeal : GwThemeTokens.TextPrimary));

        // 4. Tombol Re-Center, '?', & Menu '=' (SmokedGlass + HairlineIdle 1px + 6px radius)
        DisableOutlineIfEnabled(extRecenterOutline);
        ApplyRect6IfChanged(extRecenterBg, GwThemeTokens.SmokedGlass);
        ApplyHairlineIfChanged(extRecenterBorder, GwThemeTokens.HairlineIdle);
        for (int i = 0; i < extRecenterIcons.Length; i++)
        {
            ApplyImageColorOnlyIfChanged(extRecenterIcons[i], GwThemeTokens.TextPrimary);
        }

        DisableOutlineIfEnabled(extGuideOutline);
        ApplyRect6IfChanged(extGuideBg, isGuideOverlayOpen ? GwThemeTokens.CardIdle : GwThemeTokens.SmokedGlass);
        ApplyHairlineIfChanged(extGuideBorder, isGuideOverlayOpen ? GwThemeTokens.HairlineActive : GwThemeTokens.HairlineIdle);
        ApplyTmpColorIfChanged(extGuideIconTxt, GwThemeTokens.TextPrimary);

        bool isMenuOpen = externalHudMenu != null && externalHudMenu.IsMenuDrawerOpen;
        DisableOutlineIfEnabled(extMenuOutline);
        ApplyRect6IfChanged(extMenuBg, isMenuOpen ? GwThemeTokens.CardIdle : GwThemeTokens.SmokedGlass);
        ApplyHairlineIfChanged(extMenuBorder, isMenuOpen ? GwThemeTokens.HairlineActive : GwThemeTokens.HairlineIdle);
        for (int i = 0; i < extMenuBars.Length; i++)
        {
            ApplyImageColorOnlyIfChanged(extMenuBars[i], GwThemeTokens.TextPrimary);
        }

        // 5. Toast Bawah ("Arahkan tangan ke kamera..." & feedback status)
        DisableOutlineIfEnabled(extToastOutline);
        if (extToastBackdropObj != null && extToastBackdropObj.activeSelf)
        {
            string msg = externalFeedbackText != null ? externalFeedbackText.text : string.Empty;
            Color toastBorder = GwThemeTokens.HairlineIdle;
            Color toastText = GwThemeTokens.TextPrimary;

            if (!string.IsNullOrEmpty(msg))
            {
                if (msg.StartsWith("Gagal", StringComparison.Ordinal))
                {
                    toastBorder = GwThemeTokens.StatusErrorRed;
                    toastText = GwThemeTokens.StatusErrorText;
                }
                else if (msg.StartsWith("BERHASIL", StringComparison.Ordinal)
                         || msg.StartsWith("SELAMAT", StringComparison.Ordinal))
                {
                    toastBorder = GwThemeTokens.StatusPassEmerald;
                    toastText = GwThemeTokens.StatusPassText;
                }
                else if (msg.StartsWith("[Petunjuk", StringComparison.Ordinal)
                         || msg.StartsWith("Gerbang ", StringComparison.Ordinal)
                         || msg.StartsWith("[Air-", StringComparison.Ordinal))
                {
                    toastBorder = GwThemeTokens.HairlineActive;
                    toastText = GwThemeTokens.TextPrimary;
                }
            }

            ApplyRect6IfChanged(extToastBg, GwThemeTokens.SmokedGlassDense);
            ApplyHairlineIfChanged(extToastBorder, toastBorder);
            ApplyTmpColorIfChanged(externalFeedbackText, toastText);
        }
        else
        {
            ApplyRect6IfChanged(extToastBg, GwThemeTokens.SmokedGlassDense);
            ApplyHairlineIfChanged(extToastBorder, GwThemeTokens.HairlineIdle);
        }
    }

    public void RestoreExternalTopHudAndToastTheme()
    {
        if (!externalHudThemeApplied)
        {
            return;
        }

        for (int i = 0; i < savedExternalImages.Count; i++)
        {
            SavedImageStyle s = savedExternalImages[i];
            if (s.image != null)
            {
                s.image.sprite = s.sprite;
                s.image.type = s.type;
                s.image.pixelsPerUnitMultiplier = s.ppuMultiplier;
                s.image.color = s.color;
            }
        }

        for (int i = 0; i < savedExternalOutlines.Count; i++)
        {
            SavedOutlineStyle s = savedExternalOutlines[i];
            if (s.outline != null)
            {
                s.outline.enabled = s.enabled;
                s.outline.effectColor = s.effectColor;
                s.outline.effectDistance = s.effectDistance;
            }
        }

        for (int i = 0; i < savedExternalTmps.Count; i++)
        {
            SavedTmpStyle s = savedExternalTmps[i];
            if (s.text != null)
            {
                s.text.color = s.color;
            }
        }

        externalHudThemeApplied = false;
    }

    private void TryCacheExternalHudTargets(LevelAndUIManager levelManager)
    {
        Transform topBarTr = ResolveExternalTopBarTransform(levelManager);
        if (topBarTr == null && externalFeedbackText == null)
        {
            return;
        }

        savedExternalImages.Clear();
        savedExternalOutlines.Clear();
        savedExternalTmps.Clear();

        if (topBarTr != null)
        {
            Transform levelPillTr = topBarTr.Find("Pill_TopLevel");
            if (levelPillTr != null)
            {
                extLevelPillBg = CaptureImage(levelPillTr.GetComponent<Image>());
                extLevelPillBorder = CaptureChildImage(levelPillTr, "Pill_TopLevel_Border");
                Transform badgeTr = levelPillTr.Find("Badge_LevelTag");
                if (badgeTr != null)
                {
                    extLevelBadgeBg = CaptureImage(badgeTr.GetComponent<Image>());
                    extLevelBadgeBorder = CaptureChildImage(badgeTr, "Badge_LevelTag_Border");
                    Transform badgeTxtTr = badgeTr.Find("Txt_LevelBadge");
                    if (badgeTxtTr != null)
                    {
                        extLevelBadgeTxt = CaptureTmp(badgeTxtTr.GetComponent<TMP_Text>());
                    }
                }
            }

            if (levelManager != null)
            {
                extLevelTitleTxt = CaptureTmp(levelManager.txtLevelTitle);
                extMissionDescTxt = CaptureTmp(levelManager.txtMissionDesc);
                extQuotaStatusTxt = CaptureTmp(levelManager.txtQuotaStatus);
            }

            Transform missionPillTr = topBarTr.Find("Pill_TopMission");
            if (missionPillTr != null)
            {
                extMissionPillBg = CaptureImage(missionPillTr.GetComponent<Image>());
                extMissionPillBorder = CaptureChildImage(missionPillTr, "Pill_TopMission_Border");
            }

            Transform quotaPillTr = topBarTr.Find("Quota_AlertPill");
            if (quotaPillTr != null)
            {
                extQuotaPillBg = CaptureImage(quotaPillTr.GetComponent<Image>());
                extQuotaPillBorder = CaptureChildImage(quotaPillTr, "Quota_AlertPill_Border");
                extQuotaPillOutline = CaptureOutline(quotaPillTr.GetComponent<Outline>());
            }

            Transform recenterTr = topBarTr.Find("Btn_AirStageRecenter");
            if (recenterTr != null)
            {
                extRecenterBg = CaptureImage(recenterTr.GetComponent<Image>());
                extRecenterBorder = CaptureChildImage(recenterTr, "Btn_AirStageRecenter_Border");
                extRecenterOutline = CaptureOutline(recenterTr.GetComponent<Outline>());
                extRecenterIcons[0] = CaptureChildImage(recenterTr, "Icon_RecenterRing");
                extRecenterIcons[1] = CaptureChildImage(recenterTr, "Icon_RecenterDot");
                extRecenterIcons[2] = CaptureChildImage(recenterTr, "Icon_RecenterTop");
                extRecenterIcons[3] = CaptureChildImage(recenterTr, "Icon_RecenterBottom");
                extRecenterIcons[4] = CaptureChildImage(recenterTr, "Icon_RecenterLeft");
                extRecenterIcons[5] = CaptureChildImage(recenterTr, "Icon_RecenterRight");
            }

            Transform guideTr = topBarTr.Find("Btn_AirGestureGuide");
            if (guideTr != null)
            {
                extGuideBg = CaptureImage(guideTr.GetComponent<Image>());
                extGuideBorder = CaptureChildImage(guideTr, "Btn_AirGestureGuide_Border");
                extGuideOutline = CaptureOutline(guideTr.GetComponent<Outline>());
                Transform guideTxtTr = guideTr.Find("Txt_GuideIcon");
                if (guideTxtTr != null)
                {
                    extGuideIconTxt = CaptureTmp(guideTxtTr.GetComponent<TMP_Text>());
                }
            }

            Transform menuTr = topBarTr.Find("Btn_CompactMenu");
            if (menuTr != null)
            {
                extMenuBg = CaptureImage(menuTr.GetComponent<Image>());
                extMenuBorder = CaptureChildImage(menuTr, "Btn_CompactMenu_Border");
                extMenuOutline = CaptureOutline(menuTr.GetComponent<Outline>());
                extMenuBars[0] = CaptureChildImage(menuTr, "Bar_Top");
                extMenuBars[1] = CaptureChildImage(menuTr, "Bar_Mid");
                extMenuBars[2] = CaptureChildImage(menuTr, "Bar_Bot");
            }
        }

        if (externalFeedbackText != null)
        {
            CaptureTmp(externalFeedbackText);
            RectTransform fbRect = externalFeedbackText.rectTransform;
            Transform fbParent = fbRect != null ? fbRect.parent : null;
            if (fbParent != null)
            {
                Transform toastBgTr = fbParent.Find("Txt_Feedback_Backdrop");
                if (toastBgTr != null)
                {
                    extToastBackdropObj = toastBgTr.gameObject;
                    extToastBg = CaptureImage(toastBgTr.GetComponent<Image>());
                    extToastBorder = CaptureChildImage(toastBgTr, "Txt_Feedback_Backdrop_Border");
                    extToastOutline = CaptureOutline(toastBgTr.GetComponent<Outline>());
                }
            }
        }

        externalHudThemeCached = savedExternalImages.Count > 0 || savedExternalTmps.Count > 0;
    }

    private Transform ResolveExternalTopBarTransform(LevelAndUIManager levelManager)
    {
        if (levelManager != null && levelManager.txtLevelTitle != null)
        {
            Transform pillTr = levelManager.txtLevelTitle.transform.parent;
            if (pillTr != null && pillTr.parent != null)
            {
                return pillTr.parent;
            }
        }

        if (externalHudMenu != null && externalHudMenu.TargetCanvas != null)
        {
            return externalHudMenu.TargetCanvas.transform.Find("Panel_TopHud");
        }

        return null;
    }

    private Image CaptureChildImage(Transform parent, string childName)
    {
        if (parent == null)
        {
            return null;
        }

        Transform child = parent.Find(childName);
        return child != null ? CaptureImage(child.GetComponent<Image>()) : null;
    }

    private Image CaptureImage(Image img)
    {
        if (img == null)
        {
            return null;
        }

        savedExternalImages.Add(new SavedImageStyle
        {
            image = img,
            sprite = img.sprite,
            type = img.type,
            ppuMultiplier = img.pixelsPerUnitMultiplier,
            color = img.color
        });
        return img;
    }

    private Outline CaptureOutline(Outline outline)
    {
        if (outline == null)
        {
            return null;
        }

        savedExternalOutlines.Add(new SavedOutlineStyle
        {
            outline = outline,
            enabled = outline.enabled,
            effectColor = outline.effectColor,
            effectDistance = outline.effectDistance
        });
        return outline;
    }

    private TMP_Text CaptureTmp(TMP_Text tmp)
    {
        if (tmp == null)
        {
            return null;
        }

        savedExternalTmps.Add(new SavedTmpStyle
        {
            text = tmp,
            color = tmp.color
        });
        return tmp;
    }

    private static void ApplyRect6IfChanged(Image img, Color color)
    {
        if (img != null)
        {
            GwThemeTokens.ApplyRoundedRect6(img, color);
        }
    }

    private static void ApplyHairlineIfChanged(Image img, Color color)
    {
        if (img != null)
        {
            GwThemeTokens.ApplyHairlineBorder6(img, color);
        }
    }

    private static void ApplyImageColorOnlyIfChanged(Image img, Color color)
    {
        if (img != null && img.color != color)
        {
            img.color = color;
        }
    }

    private static void ApplyTmpColorIfChanged(TMP_Text tmp, Color color)
    {
        if (tmp != null && tmp.color != color)
        {
            tmp.color = color;
        }
    }

    private static void DisableOutlineIfEnabled(Outline outline)
    {
        if (outline != null && outline.enabled)
        {
            outline.enabled = false;
        }
    }

    private static RectTransform CreateTransparentAnchorContainer(string name, RectTransform parent, float anchorMinX, float anchorMaxX)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(anchorMinX, 0f);
        rect.anchorMax = new Vector2(anchorMaxX, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
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
            new Vector2(GwThemeTokens.HairlineBorderPx, height),
            GwThemeTokens.HairlineIdle);
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
        string initialText,
        FontStyles fontStyle = FontStyles.Bold)
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
        tmp.fontStyle = fontStyle;
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
