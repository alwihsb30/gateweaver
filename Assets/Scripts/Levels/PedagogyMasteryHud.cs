using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Modul Pedagogi Live & Evaluasi Belajar (GateWeaver AR v7.5 Sprint 3):
/// 1. Live Boolean Expression Chip (bekerja di Mode Kartu v5 & Mode Udara v7):
///    - Pil hologram gelap beraksen sian di kiri-atas tepat di bawah Bilah Atas 72 px (aman Safe Area Android 20:9).
///    - Membaca graf rangkaian aktif secara live (Zero-GC: hash topologi+nilai + StringBuilder + char[] + SetCharArray).
///    - Menampilkan rumus aljabar Boolean formal + hasil [0]/[1], cabang belum lengkap '?', dan guard anti-siklus.
/// 2. Kartu Rapor Penguasaan (diakses lewat 'Rapor Penguasaan' pada laci menu):
///    - Membaca progres resmi 7 level & bintang (X/21, Y/7) dari PlayerPrefs (GW_v3_*), 0 diff LevelAndUIManager.
///    - Menyimpan statistik self-debugging & penyelesaian Mode Kartu vs Mode Udara (GW_v75_*),
///      termasuk deteksi pasif reset progres bila bintang/level tersimpan turun dari > 0 ke 0.
///    - Eksklusif dua arah terhadap laci menu, Panduan Gestur, & modal lain.
/// </summary>
public sealed class PedagogyMasteryHud : MonoBehaviour
{
    private const float TopBarHeight = 72f;
    private const float ChipMinWidth = 300f;
    private const float ChipMaxWidth = 520f;
    private const float ChipHeight = 42f;
    private const float ChipHorizontalPadding = 34f;
    private const float MasteryCardWidth = 1040f;
    private const float MasteryCardHeight = 664f;

    private const int MaxLevelCount = 7;
    private const int MaxStarsPerLevel = 3;
    private const int MaxTotalStars = MaxLevelCount * MaxStarsPerLevel;
    private const int MaxRecursionDepth = 8;
    private const int ExpressionCharBufferCapacity = 224;

    private const string OutputValueHighRichSuffix = "  <color=#38F8A4>[1]</color>";
    private const string OutputValueLowRichSuffix = "  <color=#62E4FF>[0]</color>";
    private const string OutputValueWarnHighRichSuffix = "  <color=#FFD064>[1]</color>";
    private const string OutputValueWarnLowRichSuffix = "  <color=#FFD064>[0]</color>";

    // Kunci resmi progres LevelAndUIManager (read-only, pra-cache untuk Zero-GC)
    private static readonly string[] PrefKeysLevelCleared =
    {
        "GW_v3_LevelCleared_0",
        "GW_v3_LevelCleared_1",
        "GW_v3_LevelCleared_2",
        "GW_v3_LevelCleared_3",
        "GW_v3_LevelCleared_4",
        "GW_v3_LevelCleared_5",
        "GW_v3_LevelCleared_6"
    };

    private static readonly string[] PrefKeysLevelStars =
    {
        "GW_v3_LevelStars_0",
        "GW_v3_LevelStars_1",
        "GW_v3_LevelStars_2",
        "GW_v3_LevelStars_3",
        "GW_v3_LevelStars_4",
        "GW_v3_LevelStars_5",
        "GW_v3_LevelStars_6"
    };

    // Kunci statistik pedagogi terisolasi Sprint 3
    private const string PrefKeySelfDebugFixes = "GW_v75_SelfDebugFixes";
    private const string PrefKeyCardCompletions = "GW_v75_CardCompletions";
    private const string PrefKeyAirCompletions = "GW_v75_AirCompletions";
    private const string PrefKeyLastKnownProgressSum = "GW_v75_LastKnownProgressSum";

    private static readonly string[] DefaultLevelTopics =
    {
        "OR",
        "XOR",
        "AND+NOT",
        "NAND",
        "XNOR",
        "NOR",
        "De Morgan"
    };

    private static readonly string[] DefaultLevelNames =
    {
        "Alarm Pintu & Jendela",
        "Sakelar Tangga Dua Arah",
        "Kunci Keamanan Brankas",
        "Pemutus Darurat Reaktor",
        "Sinkronisasi Inti Kembar",
        "Sensor Hening Ruang Server",
        "Kunci Biometrik De Morgan"
    };

    private static readonly string[] StarFractionLabels = { "0/3", "1/3", "2/3", "3/3" };

    // Palet hologram gelap-sian selaras CompactHudMenuController & AirWeavingGestureGuide
    private static readonly Color ChipBgColor = new Color(0.03f, 0.07f, 0.14f, 0.86f);
    private static readonly Color ChipBorderIdleColor = new Color(0.24f, 0.62f, 0.78f, 0.72f);
    private static readonly Color ChipBorderLowColor = new Color(0.18f, 0.86f, 1.00f, 0.88f);
    private static readonly Color ChipBorderHighColor = new Color(0.22f, 0.96f, 0.66f, 0.94f);
    private static readonly Color ChipBorderWarnColor = new Color(1.00f, 0.76f, 0.24f, 0.90f);
    private static readonly Color ChipStripIdleColor = new Color(0.38f, 0.62f, 0.76f, 0.88f);
    private static readonly Color ChipStripLowColor = new Color(0.22f, 0.88f, 1.00f, 1.00f);
    private static readonly Color ChipStripHighColor = new Color(0.22f, 0.98f, 0.66f, 1.00f);
    private static readonly Color ChipStripWarnColor = new Color(1.00f, 0.74f, 0.24f, 1.00f);
    private static readonly Color ChipTextIdleColor = new Color(0.76f, 0.89f, 0.96f, 0.96f);
    private static readonly Color ChipTextConnectedColor = new Color(0.93f, 0.98f, 1.00f, 1.00f);
    private static readonly Color ChipTextWarnColor = new Color(1.00f, 0.91f, 0.68f, 1.00f);

    private static readonly Color ScrimBgColor = new Color(0.01f, 0.03f, 0.07f, 0.70f);
    private static readonly Color CardBgColor = new Color(0.03f, 0.07f, 0.13f, 0.97f);
    private static readonly Color CardBorderCyanColor = new Color(0.18f, 0.88f, 1.00f, 0.92f);
    private static readonly Color CardDividerColor = new Color(0.20f, 0.68f, 0.86f, 0.48f);
    private static readonly Color SubtitleTextColor = new Color(0.72f, 0.88f, 0.96f, 0.95f);
    private static readonly Color MetricCardBgColor = new Color(0.06f, 0.13f, 0.22f, 0.94f);
    private static readonly Color MetricCardOutlineColor = new Color(0.20f, 0.62f, 0.80f, 0.70f);
    private static readonly Color MetricCaptionColor = new Color(0.64f, 0.82f, 0.92f, 0.95f);
    private static readonly Color TableHeaderColor = new Color(0.58f, 0.82f, 0.94f, 0.92f);
    private static readonly Color RowIdleBgColor = new Color(0.07f, 0.12f, 0.20f, 0.86f);
    private static readonly Color RowClearedBgColor = new Color(0.06f, 0.22f, 0.18f, 0.90f);
    private static readonly Color RowAccentIdleColor = new Color(0.32f, 0.50f, 0.66f, 0.88f);
    private static readonly Color RowAccentClearedColor = new Color(0.26f, 0.98f, 0.62f, 1.00f);
    private static readonly Color StatusClearedBgColor = new Color(0.08f, 0.36f, 0.22f, 0.96f);
    private static readonly Color StatusClearedTextColor = new Color(0.54f, 1.00f, 0.72f, 1.00f);
    private static readonly Color StatusPendingBgColor = new Color(0.16f, 0.19f, 0.26f, 0.94f);
    private static readonly Color StatusPendingTextColor = new Color(0.96f, 0.74f, 0.40f, 1.00f);
    private static readonly Color StatsPanelBgColor = new Color(0.05f, 0.11f, 0.19f, 0.94f);
    private static readonly Color StatPillBgColor = new Color(0.08f, 0.17f, 0.28f, 0.94f);
    private static readonly Color FeedbackNoteColor = new Color(0.94f, 0.98f, 1.00f, 0.98f);
    private static readonly Color GoldAccentColor = new Color(1.00f, 0.86f, 0.28f, 1.00f);
    private static readonly Color EmeraldAccentColor = new Color(0.32f, 1.00f, 0.66f, 1.00f);
    private static readonly Color CyanAccentColor = new Color(0.32f, 0.96f, 1.00f, 1.00f);
    private static readonly Color OrangeAccentColor = new Color(1.00f, 0.68f, 0.30f, 1.00f);
    private static readonly Color MutedTextColor = new Color(0.64f, 0.74f, 0.82f, 0.92f);
    private static readonly Color CloseBtnBgColor = new Color(0.10f, 0.24f, 0.36f, 0.98f);
    private static readonly Color CloseBtnOutlineColor = new Color(0.28f, 0.92f, 1.00f, 0.96f);

    private static readonly ColorBlock MasteryButtonColorBlock = new ColorBlock
    {
        normalColor = Color.white,
        highlightedColor = new Color(0.90f, 0.97f, 1.00f, 1f),
        pressedColor = new Color(0.72f, 0.82f, 0.90f, 1f),
        selectedColor = Color.white,
        disabledColor = new Color(0.45f, 0.48f, 0.52f, 0.55f),
        colorMultiplier = 1f,
        fadeDuration = 0.08f
    };

    private LevelAndUIManager levelManager;
    private CompactHudMenuController compactHudMenu;
    private CircuitManager circuitManager;
    private GateNode inputANode;
    private GateNode inputBNode;
    private GateNode outputNode;
    private Canvas targetCanvas;
    private TMP_FontAsset safeFont;

    // UI Live Boolean Expression Chip
    private GameObject chipRootObj;
    private RectTransform chipSafeAreaRect;
    private RectTransform chipPillRect;
    private Image chipAccentStrip;
    private readonly Image[] chipBorderStrips = new Image[3];
    private TMP_Text txtBooleanChip;

    // UI Modal Kartu Rapor Penguasaan
    private GameObject masteryRootObj;
    private Button masteryScrimButton;
    private RectTransform masterySafeAreaRect;
    private RectTransform masteryCardRect;
    private Button btnCloseMastery;

    private TMP_Text txtSummaryStars;
    private TMP_Text txtSummaryCleared;
    private TMP_Text txtSummaryPredicate;
    private readonly Image[] rowBackgrounds = new Image[MaxLevelCount];
    private readonly Image[] rowAccentStrips = new Image[MaxLevelCount];
    private readonly TMP_Text[] rowTitleTexts = new TMP_Text[MaxLevelCount];
    private readonly TMP_Text[] rowStarsTexts = new TMP_Text[MaxLevelCount];
    private readonly Image[] rowStatusBgs = new Image[MaxLevelCount];
    private readonly TMP_Text[] rowStatusTexts = new TMP_Text[MaxLevelCount];
    private TMP_Text txtStatSelfDebug;
    private TMP_Text txtStatModeSplit;
    private TMP_Text txtPedagogicalNote;

    // Buffer pra-alokasi Zero-GC untuk ekspresi Boolean live
    private readonly StringBuilder exprBuilder = new StringBuilder(160);
    private readonly char[] exprCharBuffer = new char[ExpressionCharBufferCapacity];
    private readonly List<GateNode> exprVisitedPath = new List<GateNode>(12);
    private readonly bool[] cachedClearedFlags = new bool[MaxLevelCount];
    private readonly int[] cachedStarCounts = new int[MaxLevelCount];

    private string safeDotGlyph = "|";
    private string safeDashGlyph = "-";

    private bool isInitialized = false;
    private bool isAirWeavingActive = false;
    private bool isMasteryReportOpen = false;

    // Cache hash & state Zero-GC untuk chip & pelacakan progres
    private bool hasRenderedChipBaseline = false;
    private int lastRenderedChipHash = 0;

    private int lastObservedTotalStars = 0;
    private int lastObservedClearedCount = 0;
    private int cachedSelfDebugFixes = 0;
    private int cachedCardCompletions = 0;
    private int cachedAirCompletions = 0;

    private int lastTrackedLevelIndex = -1;
    private bool lastTrackedSandboxMode = false;
    private bool wasLevelUiOverlayOpen = false;
    private bool wasCardVerifying = false;
    private bool hadDebugErrorOnCurrentAttempt = false;
    private bool hasCountedCompletionForCurrentReady = false;

    private Rect lastAppliedSafeArea;
    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;
    private bool hasAppliedCardScale = false;

    public bool IsMasteryReportOpen => isMasteryReportOpen;

    public event Action OnMasteryReportOpened;

    public void Initialize(
        LevelAndUIManager manager,
        CompactHudMenuController hudMenu,
        Canvas canvas,
        TMP_FontAsset font)
    {
        if (isInitialized)
        {
            return;
        }

        levelManager = manager;
        compactHudMenu = hudMenu;
        targetCanvas = canvas != null ? canvas : FindTargetCanvas();
        safeFont = font != null ? font : TMP_Settings.defaultFontAsset;

        ResolveCircuitReferences();
        LoadInitialPersistentStatsAndSyncReset();

        safeDotGlyph = SupportsGlyph('\u00B7') ? "\u00B7" : "|";
        safeDashGlyph = SupportsGlyph('\u2013') ? "\u2013" : "-";

        if (targetCanvas != null)
        {
            BuildLiveBooleanChipUi(targetCanvas.transform);
            BuildMasteryReportModalUi(targetCanvas.transform);
        }

        if (compactHudMenu != null)
        {
            compactHudMenu.OnMenuDrawerOpened += HandleExternalOverlayOpened;
            compactHudMenu.OnGestureGuideRequested += HandleExternalOverlayOpened;
            compactHudMenu.OnMasteryReportRequested += HandleMasteryReportRequested;
        }

        if (levelManager != null)
        {
            lastTrackedLevelIndex = levelManager.currentLevelIndex;
            lastTrackedSandboxMode = levelManager.IsSandboxMode;
            wasLevelUiOverlayOpen = levelManager.IsUiOverlayOpen;
            wasCardVerifying = levelManager.IsVerifying;
            hasCountedCompletionForCurrentReady = levelManager.IsReadyForNextLevel;
        }

        isInitialized = true;
        ApplySafeAreaAndResponsiveScaleIfNeeded(force: true);
        UpdateLiveBooleanExpressionChip(forceRebuild: true);
    }

    public void SetAirWeavingMode(bool active)
    {
        isAirWeavingActive = active;
        CloseMasteryReport();
        NotifyAttemptReset();
        if (isInitialized)
        {
            UpdateLiveBooleanExpressionChip(forceRebuild: true);
        }
    }

    /// <summary>
    /// Mereset flag percobaan self-debugging saat pindah level, reset rangkaian, atau ganti mode (Zero-GC).
    /// </summary>
    public void NotifyAttemptReset()
    {
        hadDebugErrorOnCurrentAttempt = false;
        hasCountedCompletionForCurrentReady = false;
        wasCardVerifying = false;
    }

    /// <summary>
    /// Dipanggil oleh AirWeavingController saat sel merah / over-quota teramati pada percobaan Mode Udara saat ini (Zero-GC).
    /// </summary>
    public void NotifyAirDebugErrorObserved()
    {
        if (!isInitialized || !isAirWeavingActive || levelManager == null || levelManager.IsSandboxMode)
        {
            return;
        }

        if (!hasCountedCompletionForCurrentReady)
        {
            hadDebugErrorOnCurrentAttempt = true;
        }
    }

    /// <summary>
    /// Dipanggil oleh AirWeavingController tepat 1x pada rising-edge lulus level Mode Udara (Zero-GC).
    /// </summary>
    public void NotifyAirLevelCompleted()
    {
        if (!isInitialized || !isAirWeavingActive || levelManager == null || levelManager.IsSandboxMode)
        {
            return;
        }

        RecordLevelCompletionInternal(isAirMode: true);
    }

    public void ToggleMasteryReport()
    {
        if (!isInitialized)
        {
            return;
        }

        if (isMasteryReportOpen)
        {
            CloseMasteryReport();
        }
        else
        {
            OpenMasteryReport();
        }
    }

    public void OpenMasteryReport()
    {
        if (!isInitialized || masteryRootObj == null)
        {
            return;
        }

        if (compactHudMenu != null)
        {
            compactHudMenu.CloseMenuDrawer();
            compactHudMenu.ClearToastImmediate();
        }

        if (levelManager != null)
        {
            levelManager.CloseAllModalsForMenu();
        }

        SyncPassiveProgressResetIfNeeded(forceSave: true);
        RefreshMasteryReportUi();

        isMasteryReportOpen = true;
        masteryRootObj.transform.SetAsLastSibling();
        masteryRootObj.SetActive(true);
        ApplySafeAreaAndResponsiveScaleIfNeeded(force: true);

        OnMasteryReportOpened?.Invoke();
    }

    public void CloseMasteryReport()
    {
        isMasteryReportOpen = false;
        if (masteryRootObj != null && masteryRootObj.activeSelf)
        {
            masteryRootObj.SetActive(false);
        }
    }

    private void Update()
    {
        if (!isInitialized)
        {
            return;
        }

        ResolveCircuitReferences();
        ApplySafeAreaAndResponsiveScaleIfNeeded(force: false);
        UpdateLiveBooleanExpressionChip(forceRebuild: false);
        TrackLifecycleAndCardModeProgress();
    }

    private void OnDestroy()
    {
        if (compactHudMenu != null)
        {
            compactHudMenu.OnMenuDrawerOpened -= HandleExternalOverlayOpened;
            compactHudMenu.OnGestureGuideRequested -= HandleExternalOverlayOpened;
            compactHudMenu.OnMasteryReportRequested -= HandleMasteryReportRequested;
        }

        RemoveBtnListener(masteryScrimButton, CloseMasteryReport);
        RemoveBtnListener(btnCloseMastery, CloseMasteryReport);

        if (chipRootObj != null)
        {
            Destroy(chipRootObj);
            chipRootObj = null;
        }

        if (masteryRootObj != null)
        {
            Destroy(masteryRootObj);
            masteryRootObj = null;
        }

        chipSafeAreaRect = null;
        chipPillRect = null;
        chipAccentStrip = null;
        Array.Clear(chipBorderStrips, 0, chipBorderStrips.Length);
        txtBooleanChip = null;
        masteryScrimButton = null;
        masterySafeAreaRect = null;
        masteryCardRect = null;
        btnCloseMastery = null;
        txtSummaryStars = null;
        txtSummaryCleared = null;
        txtSummaryPredicate = null;
        txtStatSelfDebug = null;
        txtStatModeSplit = null;
        txtPedagogicalNote = null;

        Array.Clear(rowBackgrounds, 0, rowBackgrounds.Length);
        Array.Clear(rowAccentStrips, 0, rowAccentStrips.Length);
        Array.Clear(rowTitleTexts, 0, rowTitleTexts.Length);
        Array.Clear(rowStarsTexts, 0, rowStarsTexts.Length);
        Array.Clear(rowStatusBgs, 0, rowStatusBgs.Length);
        Array.Clear(rowStatusTexts, 0, rowStatusTexts.Length);

        exprVisitedPath.Clear();
        levelManager = null;
        compactHudMenu = null;
        circuitManager = null;
        inputANode = null;
        inputBNode = null;
        outputNode = null;
        targetCanvas = null;
        safeFont = null;
        isInitialized = false;
        isMasteryReportOpen = false;
    }

    // =========================================================================
    // EVENT HANDLERS & PELACAKAN PROGRES / RESET PASIF (ZERO-GC)
    // =========================================================================

    private void HandleExternalOverlayOpened()
    {
        CloseMasteryReport();
    }

    private void HandleMasteryReportRequested()
    {
        ToggleMasteryReport();
    }

    private void ResolveCircuitReferences()
    {
        if (levelManager != null)
        {
            if (circuitManager == null)
            {
                circuitManager = levelManager.circuitManager;
            }
            if (inputANode == null)
            {
                inputANode = levelManager.inputANode;
            }
            if (inputBNode == null)
            {
                inputBNode = levelManager.inputBNode;
            }
            if (outputNode == null)
            {
                outputNode = levelManager.outputNode;
            }
        }

        if (circuitManager == null)
        {
            circuitManager = FindAnyObjectByType<CircuitManager>();
        }

        if (circuitManager != null && circuitManager.allNodes != null
            && (inputANode == null || inputBNode == null || outputNode == null))
        {
            List<GateNode> nodes = circuitManager.allNodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                GateNode node = nodes[i];
                if (node == null)
                {
                    continue;
                }

                if (outputNode == null && node.nodeType == NodeType.OutputActuator)
                {
                    outputNode = node;
                }
                else if (node.nodeType == NodeType.InputSource)
                {
                    if (inputANode == null && node != inputBNode)
                    {
                        string upper = node.gameObject.name.ToUpperInvariant();
                        if (!upper.Contains("B"))
                        {
                            inputANode = node;
                        }
                        else if (inputBNode == null)
                        {
                            inputBNode = node;
                        }
                    }
                    else if (inputBNode == null && node != inputANode)
                    {
                        inputBNode = node;
                    }
                }
            }
        }
    }

    private void TrackLifecycleAndCardModeProgress()
    {
        if (levelManager == null)
        {
            return;
        }

        // 1. Eksklusivitas modal otomatis: tutup Rapor Penguasaan jika overlay LevelAndUIManager terbuka (mis. auto-buka Rapor CP di L7)
        bool curOverlayOpen = levelManager.IsUiOverlayOpen;
        if (isMasteryReportOpen && curOverlayOpen)
        {
            CloseMasteryReport();
        }

        // 2. Deteksi pasif reset progres saat overlay ditutup (mis. pasca klik Reset Progres 0/21 di Rapor CP) atau kembali ke Level 1
        bool overlayJustClosed = wasLevelUiOverlayOpen && !curOverlayOpen;
        wasLevelUiOverlayOpen = curOverlayOpen;

        int curLevelIdx = levelManager.currentLevelIndex;
        bool curSandbox = levelManager.IsSandboxMode;
        bool levelOrSandboxChanged = (curLevelIdx != lastTrackedLevelIndex) || (curSandbox != lastTrackedSandboxMode);
        if (levelOrSandboxChanged)
        {
            lastTrackedLevelIndex = curLevelIdx;
            lastTrackedSandboxMode = curSandbox;
            NotifyAttemptReset();
            if (curLevelIdx == 0 && !curSandbox)
            {
                SyncPassiveProgressResetIfNeeded(forceSave: true);
            }
        }
        else if (overlayJustClosed)
        {
            SyncPassiveProgressResetIfNeeded(forceSave: true);
        }

        // 3. Sinkronkan unlock completion saat status IsReadyForNextLevel turun kembali ke false
        bool curReady = levelManager.IsReadyForNextLevel;
        if (!curReady)
        {
            hasCountedCompletionForCurrentReady = false;
        }

        // 4. Pelacakan khusus Mode Kartu v5 (Mode Udara dilaporkan oleh AirWeavingController)
        if (isAirWeavingActive || curSandbox)
        {
            wasCardVerifying = false;
            return;
        }

        bool curVerifying = levelManager.IsVerifying;
        if (wasCardVerifying && !curVerifying && !curReady)
        {
            // Pemindaian Uji Rangkaian gagal (baris merah / over-quota / tidak lengkap) -> tandai percobaan butuh perbaikan
            hadDebugErrorOnCurrentAttempt = true;
        }
        wasCardVerifying = curVerifying;

        // Pantau sel merah live di Pod Tabel Kebenaran selama Mode Kartu (Zero-GC)
        if (!curVerifying && !curReady && levelManager.IsCircuitCompleteLive
            && outputNode != null && outputNode.isTracked
            && levelManager.levels != null && curLevelIdx >= 0 && curLevelIdx < levelManager.levels.Count)
        {
            LevelScenario scenario = levelManager.levels[curLevelIdx];
            if (scenario != null && scenario.expectedTruthTable != null && scenario.expectedTruthTable.Length >= 4)
            {
                bool curA = inputANode != null && inputANode.currentOutputValue;
                bool curB = inputBNode != null && inputBNode.currentOutputValue;
                int rowIdx = (curA ? 2 : 0) + (curB ? 1 : 0);
                if (outputNode.currentOutputValue != scenario.expectedTruthTable[rowIdx])
                {
                    hadDebugErrorOnCurrentAttempt = true;
                }
            }
        }

        if (curReady && !hasCountedCompletionForCurrentReady)
        {
            RecordLevelCompletionInternal(isAirMode: false);
        }
    }

    private void RecordLevelCompletionInternal(bool isAirMode)
    {
        if (hasCountedCompletionForCurrentReady)
        {
            return;
        }

        hasCountedCompletionForCurrentReady = true;
        SyncPassiveProgressResetIfNeeded(forceSave: false);

        if (isAirMode)
        {
            cachedAirCompletions++;
            PlayerPrefs.SetInt(PrefKeyAirCompletions, cachedAirCompletions);
        }
        else
        {
            cachedCardCompletions++;
            PlayerPrefs.SetInt(PrefKeyCardCompletions, cachedCardCompletions);
        }

        if (hadDebugErrorOnCurrentAttempt)
        {
            cachedSelfDebugFixes++;
            PlayerPrefs.SetInt(PrefKeySelfDebugFixes, cachedSelfDebugFixes);
            hadDebugErrorOnCurrentAttempt = false;
        }

        PlayerPrefs.Save();
    }

    private void LoadInitialPersistentStatsAndSyncReset()
    {
        cachedSelfDebugFixes = Mathf.Max(0, PlayerPrefs.GetInt(PrefKeySelfDebugFixes, 0));
        cachedCardCompletions = Mathf.Max(0, PlayerPrefs.GetInt(PrefKeyCardCompletions, 0));
        cachedAirCompletions = Mathf.Max(0, PlayerPrefs.GetInt(PrefKeyAirCompletions, 0));

        ReadOfficialProgressFromPrefs(out int totalStars, out int clearedCount);
        int storedProgressSum = PlayerPrefs.GetInt(PrefKeyLastKnownProgressSum, 0);

        if (totalStars == 0 && clearedCount == 0 && storedProgressSum > 0)
        {
            ClearV75PersistentStats(saveImmediately: true);
            lastObservedTotalStars = 0;
            lastObservedClearedCount = 0;
        }
        else
        {
            lastObservedTotalStars = totalStars;
            lastObservedClearedCount = clearedCount;
            int currentSum = totalStars + clearedCount;
            if (currentSum > 0 && storedProgressSum != currentSum)
            {
                PlayerPrefs.SetInt(PrefKeyLastKnownProgressSum, currentSum);
                PlayerPrefs.Save();
            }
        }
    }

    private void SyncPassiveProgressResetIfNeeded(bool forceSave)
    {
        ReadOfficialProgressFromPrefs(out int totalStars, out int clearedCount);
        int storedProgressSum = PlayerPrefs.GetInt(PrefKeyLastKnownProgressSum, 0);

        if (totalStars == 0 && clearedCount == 0)
        {
            if (lastObservedTotalStars > 0 || lastObservedClearedCount > 0 || storedProgressSum > 0)
            {
                ClearV75PersistentStats(saveImmediately: forceSave);
            }
            lastObservedTotalStars = 0;
            lastObservedClearedCount = 0;
            return;
        }

        lastObservedTotalStars = totalStars;
        lastObservedClearedCount = clearedCount;

        int currentSum = totalStars + clearedCount;
        if (storedProgressSum != currentSum)
        {
            PlayerPrefs.SetInt(PrefKeyLastKnownProgressSum, currentSum);
            if (forceSave)
            {
                PlayerPrefs.Save();
            }
        }
    }

    private void ClearV75PersistentStats(bool saveImmediately)
    {
        cachedSelfDebugFixes = 0;
        cachedCardCompletions = 0;
        cachedAirCompletions = 0;
        hadDebugErrorOnCurrentAttempt = false;

        PlayerPrefs.DeleteKey(PrefKeySelfDebugFixes);
        PlayerPrefs.DeleteKey(PrefKeyCardCompletions);
        PlayerPrefs.DeleteKey(PrefKeyAirCompletions);
        PlayerPrefs.DeleteKey(PrefKeyLastKnownProgressSum);
        if (saveImmediately)
        {
            PlayerPrefs.Save();
        }
    }

    private void ReadOfficialProgressFromPrefs(out int totalStars, out int clearedCount)
    {
        totalStars = 0;
        clearedCount = 0;

        for (int i = 0; i < MaxLevelCount; i++)
        {
            bool cleared = PlayerPrefs.GetInt(PrefKeysLevelCleared[i], 0) == 1;
            int stars = Mathf.Clamp(PlayerPrefs.GetInt(PrefKeysLevelStars[i], 0), 0, MaxStarsPerLevel);
            cachedClearedFlags[i] = cleared;
            cachedStarCounts[i] = stars;
            totalStars += stars;
            if (cleared)
            {
                clearedCount++;
            }
        }
    }

    // =========================================================================
    // FITUR A: LIVE BOOLEAN EXPRESSION CHIP (100% ZERO-GC)
    // =========================================================================

    private void UpdateLiveBooleanExpressionChip(bool forceRebuild)
    {
        if (txtBooleanChip == null)
        {
            return;
        }

        int stateHash = ComputeLiveCircuitStateHash();
        if (!forceRebuild && hasRenderedChipBaseline && stateHash == lastRenderedChipHash)
        {
            return;
        }

        hasRenderedChipBaseline = true;
        lastRenderedChipHash = stateHash;

        BuildLiveExpressionIntoBuilder(out bool isConnected, out bool hasIncompleteOrCycle, out bool outputHigh, out int visibleCharLength);

        int copyLen = exprBuilder.Length;
        if (copyLen > ExpressionCharBufferCapacity)
        {
            copyLen = ExpressionCharBufferCapacity;
        }
        exprBuilder.CopyTo(0, exprCharBuffer, 0, copyLen);
        txtBooleanChip.SetCharArray(exprCharBuffer, 0, copyLen);

        if (chipPillRect != null)
        {
            float preferredW = txtBooleanChip.preferredWidth;
            if (preferredW <= 1f)
            {
                preferredW = visibleCharLength * 10.6f;
            }
            float targetWidth = Mathf.Clamp(preferredW + ChipHorizontalPadding, ChipMinWidth, ChipMaxWidth);
            chipPillRect.sizeDelta = new Vector2(targetWidth, ChipHeight);
        }

        if (!isConnected)
        {
            ApplyChipVisualTint(ChipBorderIdleColor, ChipStripIdleColor, ChipTextIdleColor);
        }
        else if (hasIncompleteOrCycle)
        {
            ApplyChipVisualTint(ChipBorderWarnColor, ChipStripWarnColor, ChipTextWarnColor);
        }
        else if (outputHigh)
        {
            ApplyChipVisualTint(ChipBorderHighColor, ChipStripHighColor, ChipTextConnectedColor);
        }
        else
        {
            ApplyChipVisualTint(ChipBorderLowColor, ChipStripLowColor, ChipTextConnectedColor);
        }
    }

    private void ApplyChipVisualTint(Color borderCol, Color stripCol, Color textCol)
    {
        for (int i = 0; i < chipBorderStrips.Length; i++)
        {
            if (chipBorderStrips[i] != null)
            {
                chipBorderStrips[i].color = borderCol;
            }
        }
        if (chipAccentStrip != null)
        {
            chipAccentStrip.color = stripCol;
        }
        if (txtBooleanChip != null)
        {
            txtBooleanChip.color = textCol;
        }
    }

    private int ComputeLiveCircuitStateHash()
    {
        unchecked
        {
            int hash = 17;
            if (outputNode != null)
            {
                hash = (hash * 31) + RuntimeHelpers.GetHashCode(outputNode);
                hash = (hash * 31) + (outputNode.isTracked ? 1 : 0);
                hash = (hash * 31) + (outputNode.currentOutputValue ? 3 : 2);
            }

            if (circuitManager != null && circuitManager.allNodes != null)
            {
                List<GateNode> nodes = circuitManager.allNodes;
                int count = nodes.Count;
                hash = (hash * 31) + count;
                for (int i = 0; i < count; i++)
                {
                    GateNode node = nodes[i];
                    if (node == null)
                    {
                        hash = (hash * 31) + 7;
                        continue;
                    }

                    hash = (hash * 31) + RuntimeHelpers.GetHashCode(node);
                    hash = (hash * 31) + ((int)node.nodeType * 19);
                    hash = (hash * 31) + (node.isTracked ? 1 : 0);

                    List<GateNode> inputs = node.connectedInputs;
                    if (inputs != null)
                    {
                        int inCount = inputs.Count;
                        hash = (hash * 31) + inCount;
                        for (int j = 0; j < inCount; j++)
                        {
                            GateNode src = inputs[j];
                            int srcHash = src != null ? RuntimeHelpers.GetHashCode(src) : 0;
                            hash = (hash * 31) + ((j + 1) * 131) + srcHash;
                        }
                    }
                }
            }

            return hash;
        }
    }

    private void BuildLiveExpressionIntoBuilder(out bool isConnected, out bool hasIncompleteOrCycle, out bool outputHigh, out int visibleCharLength)
    {
        exprBuilder.Clear();
        exprVisitedPath.Clear();
        hasIncompleteOrCycle = false;

        if (outputNode == null
            || !outputNode.isTracked
            || outputNode.connectedInputs == null
            || outputNode.connectedInputs.Count == 0
            || outputNode.connectedInputs[0] == null
            || !outputNode.connectedInputs[0].isTracked)
        {
            isConnected = false;
            outputHigh = false;
            exprBuilder.Append("OUT = Belum Tersambung");
            visibleCharLength = exprBuilder.Length;
            return;
        }

        isConnected = true;
        outputHigh = outputNode.currentOutputValue;
        exprBuilder.Append("OUT = ");
        AppendNodeExpression(
            outputNode.connectedInputs[0],
            depth: 0,
            wrapBinaryInParens: false,
            wrapNotInParens: false,
            ref hasIncompleteOrCycle);
        exprVisitedPath.Clear();

        visibleCharLength = exprBuilder.Length + 5; // "  [0]" / "  [1]"
        if (hasIncompleteOrCycle)
        {
            exprBuilder.Append(outputHigh ? OutputValueWarnHighRichSuffix : OutputValueWarnLowRichSuffix);
        }
        else
        {
            exprBuilder.Append(outputHigh ? OutputValueHighRichSuffix : OutputValueLowRichSuffix);
        }
    }

    private void AppendNodeExpression(
        GateNode node,
        int depth,
        bool wrapBinaryInParens,
        bool wrapNotInParens,
        ref bool hasIncompleteOrCycle)
    {
        if (node == null || !node.isTracked)
        {
            hasIncompleteOrCycle = true;
            exprBuilder.Append('?');
            return;
        }

        if (depth >= MaxRecursionDepth || IsNodeInVisitedPath(node))
        {
            hasIncompleteOrCycle = true;
            exprBuilder.Append("[Siklus]");
            return;
        }

        exprVisitedPath.Add(node);

        switch (node.nodeType)
        {
            case NodeType.InputSource:
                exprBuilder.Append(IsInputBNode(node) ? 'B' : 'A');
                break;

            case NodeType.NOT:
                AppendNotGateExpression(node, depth, wrapNotInParens, ref hasIncompleteOrCycle);
                break;

            case NodeType.AND:
            case NodeType.OR:
            case NodeType.XOR:
            case NodeType.NAND:
                AppendBinaryGateExpression(node, depth, wrapBinaryInParens, ref hasIncompleteOrCycle);
                break;

            case NodeType.OutputActuator:
                if (node.connectedInputs != null && node.connectedInputs.Count > 0 && node.connectedInputs[0] != null)
                {
                    AppendNodeExpression(node.connectedInputs[0], depth + 1, wrapBinaryInParens, wrapNotInParens, ref hasIncompleteOrCycle);
                }
                else
                {
                    hasIncompleteOrCycle = true;
                    exprBuilder.Append('?');
                }
                break;

            default:
                hasIncompleteOrCycle = true;
                exprBuilder.Append('?');
                break;
        }

        if (exprVisitedPath.Count > 0)
        {
            exprVisitedPath.RemoveAt(exprVisitedPath.Count - 1);
        }
    }

    private void AppendNotGateExpression(GateNode node, int depth, bool wrapNotInParens, ref bool hasIncompleteOrCycle)
    {
        GateNode child = (node.connectedInputs != null && node.connectedInputs.Count > 0)
            ? node.connectedInputs[0]
            : null;

        if (child == null || !child.isTracked)
        {
            hasIncompleteOrCycle = true;
            exprBuilder.Append("NOT(?)");
            return;
        }

        if (child.nodeType == NodeType.InputSource)
        {
            if (wrapNotInParens)
            {
                exprBuilder.Append("(NOT ");
                exprBuilder.Append(IsInputBNode(child) ? 'B' : 'A');
                exprBuilder.Append(')');
            }
            else
            {
                exprBuilder.Append("NOT ");
                exprBuilder.Append(IsInputBNode(child) ? 'B' : 'A');
            }
            return;
        }

        exprBuilder.Append("NOT(");
        AppendNodeExpression(
            child,
            depth + 1,
            wrapBinaryInParens: false,
            wrapNotInParens: true,
            ref hasIncompleteOrCycle);
        exprBuilder.Append(')');
    }

    private void AppendBinaryGateExpression(GateNode node, int depth, bool wrapBinaryInParens, ref bool hasIncompleteOrCycle)
    {
        GateNode in0 = null;
        GateNode in1 = null;

        List<GateNode> inputs = node.connectedInputs;
        if (inputs != null)
        {
            for (int i = 0; i < inputs.Count; i++)
            {
                GateNode candidate = inputs[i];
                if (candidate == null || !candidate.isTracked)
                {
                    continue;
                }

                if (in0 == null)
                {
                    in0 = candidate;
                }
                else if (in1 == null)
                {
                    in1 = candidate;
                    break;
                }
            }
        }

        // Cabang input belum lengkap -> format fungsional ringkas, mis. AND(A, ?) atau AND(?, ?)
        if (in0 == null || in1 == null)
        {
            hasIncompleteOrCycle = true;
            AppendGateOperatorLabel(node.nodeType);
            exprBuilder.Append('(');
            if (in0 == null)
            {
                exprBuilder.Append("?, ?");
            }
            else
            {
                AppendNodeExpression(
                    in0,
                    depth + 1,
                    wrapBinaryInParens: false,
                    wrapNotInParens: false,
                    ref hasIncompleteOrCycle);
                exprBuilder.Append(", ?");
            }
            exprBuilder.Append(')');
            return;
        }

        // Normalisasi urutan komunikatif untuk pasangan input murni B <OP> A -> A <OP> B
        if (in0.nodeType == NodeType.InputSource
            && in1.nodeType == NodeType.InputSource
            && IsInputBNode(in0)
            && !IsInputBNode(in1))
        {
            GateNode tmp = in0;
            in0 = in1;
            in1 = tmp;
        }

        if (wrapBinaryInParens)
        {
            exprBuilder.Append('(');
        }

        AppendNodeExpression(
            in0,
            depth + 1,
            wrapBinaryInParens: true,
            wrapNotInParens: true,
            ref hasIncompleteOrCycle);
        exprBuilder.Append(' ');
        AppendGateOperatorLabel(node.nodeType);
        exprBuilder.Append(' ');
        AppendNodeExpression(
            in1,
            depth + 1,
            wrapBinaryInParens: true,
            wrapNotInParens: true,
            ref hasIncompleteOrCycle);

        if (wrapBinaryInParens)
        {
            exprBuilder.Append(')');
        }
    }

    private void AppendGateOperatorLabel(NodeType type)
    {
        switch (type)
        {
            case NodeType.AND: exprBuilder.Append("AND"); break;
            case NodeType.OR: exprBuilder.Append("OR"); break;
            case NodeType.XOR: exprBuilder.Append("XOR"); break;
            case NodeType.NAND: exprBuilder.Append("NAND"); break;
            default: exprBuilder.Append("OP"); break;
        }
    }

    private bool IsNodeInVisitedPath(GateNode node)
    {
        for (int i = 0; i < exprVisitedPath.Count; i++)
        {
            if (exprVisitedPath[i] == node)
            {
                return true;
            }
        }
        return false;
    }

    private bool IsInputBNode(GateNode node)
    {
        if (inputBNode != null && node == inputBNode)
        {
            return true;
        }
        if (inputANode != null && node == inputANode)
        {
            return false;
        }
        return false;
    }

    // =========================================================================
    // FITUR B: REFRESH DATA KARTU RAPOR PENGUASAAN (HANYA SAAT MODAL DIBUKA)
    // =========================================================================

    private void RefreshMasteryReportUi()
    {
        ReadOfficialProgressFromPrefs(out int totalStars, out int clearedCount);

        if (txtSummaryStars != null)
        {
            txtSummaryStars.text = $"{totalStars} / {MaxTotalStars}";
            txtSummaryStars.color = totalStars >= 18 ? GoldAccentColor : (totalStars > 0 ? CyanAccentColor : MutedTextColor);
        }

        if (txtSummaryCleared != null)
        {
            txtSummaryCleared.text = $"{clearedCount} / {MaxLevelCount}";
            txtSummaryCleared.color = clearedCount >= MaxLevelCount ? EmeraldAccentColor : (clearedCount > 0 ? CyanAccentColor : MutedTextColor);
        }

        if (txtSummaryPredicate != null)
        {
            string predicate = ResolveMasteryPredicate(totalStars, clearedCount, out Color predicateColor);
            txtSummaryPredicate.text = predicate;
            txtSummaryPredicate.color = predicateColor;
        }

        for (int i = 0; i < MaxLevelCount; i++)
        {
            bool cleared = cachedClearedFlags[i];
            int stars = cachedStarCounts[i];

            if (rowBackgrounds[i] != null)
            {
                rowBackgrounds[i].color = cleared ? RowClearedBgColor : RowIdleBgColor;
            }

            if (rowAccentStrips[i] != null)
            {
                rowAccentStrips[i].color = cleared ? RowAccentClearedColor : RowAccentIdleColor;
            }

            if (rowTitleTexts[i] != null)
            {
                rowTitleTexts[i].text = BuildCompactLevelRowLabel(i);
            }

            if (rowStarsTexts[i] != null)
            {
                rowStarsTexts[i].text = StarFractionLabels[Mathf.Clamp(stars, 0, MaxStarsPerLevel)];
                rowStarsTexts[i].color = stars switch
                {
                    3 => GoldAccentColor,
                    2 => CyanAccentColor,
                    1 => OrangeAccentColor,
                    _ => MutedTextColor
                };
            }

            if (rowStatusBgs[i] != null)
            {
                rowStatusBgs[i].color = cleared ? StatusClearedBgColor : StatusPendingBgColor;
            }

            if (rowStatusTexts[i] != null)
            {
                rowStatusTexts[i].text = cleared ? "Lulus" : "Belum";
                rowStatusTexts[i].color = cleared ? StatusClearedTextColor : StatusPendingTextColor;
            }
        }

        if (txtStatSelfDebug != null)
        {
            txtStatSelfDebug.text = $"Self-Debugging Berhasil: {cachedSelfDebugFixes}x perbaikan";
        }

        if (txtStatModeSplit != null)
        {
            txtStatModeSplit.text = $"Mode Kartu: {cachedCardCompletions}  {safeDotGlyph}  Mode Udara: {cachedAirCompletions}";
        }

        if (txtPedagogicalNote != null)
        {
            txtPedagogicalNote.text = BuildDynamicPedagogicalNote(totalStars, clearedCount);
        }
    }

    private static string ResolveMasteryPredicate(int totalStars, int clearedCount, out Color predicateColor)
    {
        if (totalStars >= 18 || (clearedCount >= MaxLevelCount && totalStars >= 16))
        {
            predicateColor = GoldAccentColor;
            return "Master De Morgan";
        }

        if (totalStars >= 12)
        {
            predicateColor = EmeraldAccentColor;
            return "Arsitek Logika";
        }

        if (totalStars >= 5)
        {
            predicateColor = CyanAccentColor;
            return "Perakit";
        }

        predicateColor = MutedTextColor;
        return "Perintis";
    }

    private string BuildCompactLevelRowLabel(int levelIndex)
    {
        string topic = levelIndex < DefaultLevelTopics.Length ? DefaultLevelTopics[levelIndex] : $"L{levelIndex + 1}";
        string missionName = levelIndex < DefaultLevelNames.Length ? DefaultLevelNames[levelIndex] : $"Misi {levelIndex + 1}";

        if (levelManager != null && levelManager.levels != null && levelIndex < levelManager.levels.Count && levelManager.levels[levelIndex] != null)
        {
            string rawTitle = levelManager.levels[levelIndex].levelTitle;
            if (!string.IsNullOrEmpty(rawTitle))
            {
                int colonIdx = rawTitle.IndexOf(':');
                if (colonIdx > 0 && colonIdx + 1 < rawTitle.Length)
                {
                    missionName = rawTitle.Substring(colonIdx + 1).Trim();
                }
                else
                {
                    missionName = rawTitle.Trim();
                }
            }
        }

        return $"L{levelIndex + 1}  [{topic}]  {missionName}";
    }

    private string BuildDynamicPedagogicalNote(int totalStars, int clearedCount)
    {
        if (clearedCount >= MaxLevelCount && totalStars >= MaxTotalStars)
        {
            if (cachedSelfDebugFixes > 0)
            {
                return $"Catatan: Penguasaan sempurna 21/21 termasuk Hukum De Morgan, dengan {cachedSelfDebugFixes}x perbaikan mandiri.";
            }
            return "Catatan: Penguasaan sempurna 21/21! Seluruh 7 misi gerbang logika & Hukum De Morgan tuntas optimal.";
        }

        if (clearedCount >= MaxLevelCount)
        {
            int improvableLevel = 1;
            for (int i = 0; i < MaxLevelCount; i++)
            {
                if (cachedStarCounts[i] < MaxStarsPerLevel)
                {
                    improvableLevel = i + 1;
                    break;
                }
            }
            return $"Catatan: Semua 7 level lulus! Ulangi Level {improvableLevel} secara efisien untuk mengejar 21/21 bintang.";
        }

        if (clearedCount == 0)
        {
            return $"Catatan: Mulai dari Level 1 (OR) {safeDashGlyph} amati rumus Boolean live sambil menguji kombinasi 00{safeDashGlyph}11.";
        }

        int nextLevelIdx = 0;
        for (int i = 0; i < MaxLevelCount; i++)
        {
            if (!cachedClearedFlags[i])
            {
                nextLevelIdx = i;
                break;
            }
        }

        string nextTopic = nextLevelIdx < DefaultLevelTopics.Length ? DefaultLevelTopics[nextLevelIdx] : $"L{nextLevelIdx + 1}";
        if (cachedSelfDebugFixes > 0)
        {
            return $"Catatan: Progres baik ({cachedSelfDebugFixes}x self-debugging berhasil)! Target lanjut: Level {nextLevelIdx + 1} ({nextTopic}).";
        }

        return $"Catatan: Progres mantap ({clearedCount}/7 lulus)! Target berikutnya: Level {nextLevelIdx + 1} ({nextTopic}).";
    }

    // =========================================================================
    // PEMBANGUNAN UI PROSEDURAL ZERO-ASSET (CHIP BOOLEAN + MODAL RAPOR)
    // =========================================================================

    private void BuildLiveBooleanChipUi(Transform canvasTransform)
    {
        chipRootObj = new GameObject("Panel_LiveBooleanChipRoot", typeof(RectTransform));
        chipRootObj.transform.SetParent(canvasTransform, false);
        RectTransform rootRect = chipRootObj.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = new Vector2(0f, -TopBarHeight);

        // Tempatkan tepat di atas bilah HUD dasar tetapi di bawah scrim/laci menu & seluruh modal
        GameObject topHudObj = GameObject.Find("Panel_TopHud");
        if (topHudObj != null && topHudObj.transform.parent == canvasTransform)
        {
            chipRootObj.transform.SetSiblingIndex(topHudObj.transform.GetSiblingIndex() + 1);
        }

        GameObject safeAreaObj = new GameObject("LiveBooleanChip_SafeArea", typeof(RectTransform));
        safeAreaObj.transform.SetParent(rootRect, false);
        chipSafeAreaRect = safeAreaObj.GetComponent<RectTransform>();
        chipSafeAreaRect.anchorMin = Vector2.zero;
        chipSafeAreaRect.anchorMax = Vector2.one;
        chipSafeAreaRect.offsetMin = Vector2.zero;
        chipSafeAreaRect.offsetMax = Vector2.zero;

        GameObject pillObj = new GameObject("Pill_LiveBooleanExpr", typeof(RectTransform), typeof(Image));
        pillObj.transform.SetParent(chipSafeAreaRect, false);
        RectTransform pillRect = pillObj.GetComponent<RectTransform>();
        chipPillRect = pillRect;
        pillRect.anchorMin = new Vector2(0f, 1f);
        pillRect.anchorMax = new Vector2(0f, 1f);
        pillRect.pivot = new Vector2(0f, 1f);
        pillRect.anchoredPosition = new Vector2(14f, -10f);
        pillRect.sizeDelta = new Vector2(ChipMinWidth, ChipHeight);

        Image pillBg = pillObj.GetComponent<Image>();
        pillBg.color = ChipBgColor;
        pillBg.raycastTarget = false;

        // 3 bingkai tepi tipis (tanpa komponen Outline agar latar gelap semi-transparan tidak pernah tembus warna hijau/sian solid)
        chipBorderStrips[0] = CreateBorderStrip("Chip_BorderTop", pillRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 1.5f), ChipBorderIdleColor);
        chipBorderStrips[1] = CreateBorderStrip("Chip_BorderBottom", pillRect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 1.5f), ChipBorderIdleColor);
        chipBorderStrips[2] = CreateBorderStrip("Chip_BorderRight", pillRect, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(1.5f, 0f), ChipBorderIdleColor);

        GameObject stripObj = new GameObject("Chip_AccentStrip", typeof(RectTransform), typeof(Image));
        stripObj.transform.SetParent(pillRect, false);
        RectTransform stripRect = stripObj.GetComponent<RectTransform>();
        stripRect.anchorMin = new Vector2(0f, 0f);
        stripRect.anchorMax = new Vector2(0f, 1f);
        stripRect.pivot = new Vector2(0f, 0.5f);
        stripRect.anchoredPosition = Vector2.zero;
        stripRect.sizeDelta = new Vector2(4.5f, 0f);
        chipAccentStrip = stripObj.GetComponent<Image>();
        chipAccentStrip.color = ChipStripIdleColor;
        chipAccentStrip.raycastTarget = false;

        GameObject txtObj = new GameObject("Txt_LiveBooleanExpr", typeof(RectTransform), typeof(TextMeshProUGUI));
        txtObj.transform.SetParent(pillRect, false);
        RectTransform txtRect = txtObj.GetComponent<RectTransform>();
        txtRect.anchorMin = Vector2.zero;
        txtRect.anchorMax = Vector2.one;
        txtRect.offsetMin = new Vector2(15f, 2f);
        txtRect.offsetMax = new Vector2(-13f, -2f);

        TextMeshProUGUI tmp = txtObj.GetComponent<TextMeshProUGUI>();
        if (safeFont != null)
        {
            tmp.font = safeFont;
        }
        tmp.text = "OUT = Belum Tersambung";
        tmp.richText = true;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = ChipTextIdleColor;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 12.5f;
        tmp.fontSizeMax = 17.5f;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        txtBooleanChip = tmp;
    }

    private void BuildMasteryReportModalUi(Transform canvasTransform)
    {
        // Root berada tepat di bawah Bilah Atas 72 px agar tombol menu & panduan tetap responsif 1 ketukan
        masteryRootObj = new GameObject("Panel_PedagogyMasteryRoot", typeof(RectTransform));
        masteryRootObj.transform.SetParent(canvasTransform, false);
        RectTransform rootRect = masteryRootObj.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = new Vector2(0f, -TopBarHeight);

        // Scrim gelap klik-luar
        GameObject scrimObj = new GameObject("Btn_MasteryReportScrim", typeof(RectTransform), typeof(Image), typeof(Button));
        scrimObj.transform.SetParent(rootRect, false);
        RectTransform scrimRect = scrimObj.GetComponent<RectTransform>();
        scrimRect.anchorMin = Vector2.zero;
        scrimRect.anchorMax = Vector2.one;
        scrimRect.offsetMin = Vector2.zero;
        scrimRect.offsetMax = Vector2.zero;

        Image scrimImg = scrimObj.GetComponent<Image>();
        scrimImg.color = ScrimBgColor;
        scrimImg.raycastTarget = true;

        masteryScrimButton = scrimObj.GetComponent<Button>();
        masteryScrimButton.targetGraphic = scrimImg;
        masteryScrimButton.transition = Selectable.Transition.None;
        masteryScrimButton.onClick.AddListener(CloseMasteryReport);

        // Kontainer Safe Area (Android 20:9 notch & gesture bar safe)
        GameObject safeAreaObj = new GameObject("MasteryReport_SafeArea", typeof(RectTransform));
        safeAreaObj.transform.SetParent(rootRect, false);
        masterySafeAreaRect = safeAreaObj.GetComponent<RectTransform>();
        masterySafeAreaRect.anchorMin = Vector2.zero;
        masterySafeAreaRect.anchorMax = Vector2.one;
        masterySafeAreaRect.offsetMin = Vector2.zero;
        masterySafeAreaRect.offsetMax = Vector2.zero;

        // Kartu Hologram Gelap Kontras Tinggi
        GameObject cardObj = new GameObject("Card_MasteryReport", typeof(RectTransform), typeof(Image));
        cardObj.transform.SetParent(masterySafeAreaRect, false);
        masteryCardRect = cardObj.GetComponent<RectTransform>();
        masteryCardRect.anchorMin = new Vector2(0.5f, 0.5f);
        masteryCardRect.anchorMax = new Vector2(0.5f, 0.5f);
        masteryCardRect.pivot = new Vector2(0.5f, 0.5f);
        masteryCardRect.anchoredPosition = Vector2.zero;
        masteryCardRect.sizeDelta = new Vector2(MasteryCardWidth, MasteryCardHeight);

        Image cardBg = cardObj.GetComponent<Image>();
        cardBg.color = CardBgColor;
        cardBg.raycastTarget = true;

        CreateBorderStrip("Border_Top", masteryCardRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 4f), CardBorderCyanColor);
        CreateBorderStrip("Border_Bottom", masteryCardRect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 2.5f), CardBorderCyanColor);
        CreateBorderStrip("Border_Left", masteryCardRect, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(2.5f, 0f), CardBorderCyanColor);
        CreateBorderStrip("Border_Right", masteryCardRect, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(2.5f, 0f), CardBorderCyanColor);

        const float padX = 26f;
        float contentW = MasteryCardWidth - (padX * 2f);

        // 1. Header
        TMP_Text titleTmp = CreateTopLeftText(
            "Txt_MasteryHeader",
            masteryCardRect,
            new Vector2(padX, -14f),
            new Vector2(contentW, 30f),
            "KARTU RAPOR PENGUASAAN",
            TextAlignmentOptions.Center,
            CardBorderCyanColor,
            16f,
            23f,
            TextWrappingModes.NoWrap);
        titleTmp.fontStyle = FontStyles.Bold;

        CreateTopLeftText(
            "Txt_MasterySubtitle",
            masteryCardRect,
            new Vector2(padX, -45f),
            new Vector2(contentW, 22f),
            $"Evaluasi Belajar Aljabar Boolean & Gerbang Logika  {safeDotGlyph}  GateWeaver AR",
            TextAlignmentOptions.Center,
            SubtitleTextColor,
            11.5f,
            14.5f,
            TextWrappingModes.NoWrap);

        CreateDivider("Divider_Top", masteryCardRect, padX, -72f, contentW, 2f);

        // 2. Ringkasan Utama (3 kartu metrik: Total Bintang, Level Tuntas, Predikat Penguasaan)
        const float metricY = -82f;
        const float metricH = 62f;
        const float metricGap = 14f;
        const float wStars = 260f;
        const float wCleared = 250f;
        float wPredicate = contentW - wStars - wCleared - (metricGap * 2f);

        txtSummaryStars = CreateMetricBox("Metric_TotalStars", masteryCardRect, padX, metricY, wStars, metricH, "TOTAL BINTANG", "0 / 21", GoldAccentColor);
        txtSummaryCleared = CreateMetricBox("Metric_LevelsCleared", masteryCardRect, padX + wStars + metricGap, metricY, wCleared, metricH, "LEVEL TUNTAS", "0 / 7", EmeraldAccentColor);
        txtSummaryPredicate = CreateMetricBox("Metric_Predicate", masteryCardRect, padX + wStars + wCleared + (metricGap * 2f), metricY, wPredicate, metricH, "PREDIKAT PENGUASAAN", "Perintis", CyanAccentColor);

        // 3. Header & 7 Baris Daftar Level Ringkas (1 baris per level)
        const float tableHeaderY = -152f;
        TMP_Text hdrMission = CreateTopLeftText("Hdr_Mission", masteryCardRect, new Vector2(padX + 14f, tableHeaderY), new Vector2(680f, 20f), "DAFTAR 7 LEVEL KURIKULUM", TextAlignmentOptions.MidlineLeft, TableHeaderColor, 11f, 13f, TextWrappingModes.NoWrap);
        hdrMission.fontStyle = FontStyles.Bold;
        TMP_Text hdrStars = CreateTopLeftText("Hdr_Stars", masteryCardRect, new Vector2(padX + contentW - 260f, tableHeaderY), new Vector2(120f, 20f), "BINTANG", TextAlignmentOptions.Center, TableHeaderColor, 11f, 13f, TextWrappingModes.NoWrap);
        hdrStars.fontStyle = FontStyles.Bold;
        TMP_Text hdrStatus = CreateTopLeftText("Hdr_Status", masteryCardRect, new Vector2(padX + contentW - 126f, tableHeaderY), new Vector2(116f, 20f), "STATUS", TextAlignmentOptions.Center, TableHeaderColor, 11f, 13f, TextWrappingModes.NoWrap);
        hdrStatus.fontStyle = FontStyles.Bold;

        const float firstRowY = -176f;
        const float rowStepY = 37f;
        const float rowHeight = 32f;

        for (int i = 0; i < MaxLevelCount; i++)
        {
            BuildLevelSummaryRow(i, padX, firstRowY - (i * rowStepY), contentW, rowHeight);
        }

        CreateDivider("Divider_Middle", masteryCardRect, padX, -440f, contentW, 2f);

        // 4. Panel Statistik Belajar, Self-Debugging, & 1 Baris Catatan Umpan Balik Pedagogis
        GameObject statsObj = new GameObject("Panel_LearningStats", typeof(RectTransform), typeof(Image));
        statsObj.transform.SetParent(masteryCardRect, false);
        RectTransform statsRect = statsObj.GetComponent<RectTransform>();
        statsRect.anchorMin = new Vector2(0f, 1f);
        statsRect.anchorMax = new Vector2(0f, 1f);
        statsRect.pivot = new Vector2(0f, 1f);
        statsRect.anchoredPosition = new Vector2(padX, -450f);
        statsRect.sizeDelta = new Vector2(contentW, 92f);
        Image statsBg = statsObj.GetComponent<Image>();
        statsBg.color = StatsPanelBgColor;
        statsBg.raycastTarget = false;

        float pillW = (contentW - 36f) * 0.5f;
        txtStatSelfDebug = CreateStatPill("Pill_SelfDebugStat", statsRect, 12f, -10f, pillW, 34f, "Self-Debugging Berhasil: 0x perbaikan", EmeraldAccentColor);
        txtStatModeSplit = CreateStatPill("Pill_ModeSplitStat", statsRect, 24f + pillW, -10f, pillW, 34f, $"Mode Kartu: 0  {safeDotGlyph}  Mode Udara: 0", CyanAccentColor);

        txtPedagogicalNote = CreateTopLeftText(
            "Txt_PedagogicalNote",
            statsRect,
            new Vector2(14f, -52f),
            new Vector2(contentW - 28f, 32f),
            "Catatan: Mulai dari Level 1 (OR).",
            TextAlignmentOptions.MidlineLeft,
            FeedbackNoteColor,
            12f,
            15.5f,
            TextWrappingModes.NoWrap);
        txtPedagogicalNote.fontStyle = FontStyles.Bold;

        CreateDivider("Divider_Bottom", masteryCardRect, padX, -552f, contentW, 2f);

        // 5. Tombol Bawah TUTUP + keterangan singkat
        const float closeBtnW = 248f;
        const float closeBtnH = 52f;
        float closeBtnX = (MasteryCardWidth - closeBtnW) * 0.5f;
        btnCloseMastery = CreateCardActionButton(
            "Btn_CloseMasteryReport",
            masteryCardRect,
            closeBtnX,
            -566f,
            closeBtnW,
            closeBtnH,
            "TUTUP",
            CloseBtnBgColor,
            CloseBtnOutlineColor,
            Color.white,
            CloseMasteryReport);

        CreateTopLeftText(
            "Txt_MasteryFooterHint",
            masteryCardRect,
            new Vector2(padX, -624f),
            new Vector2(contentW, 20f),
            "Ketuk area gelap di luar kartu atau tombol TUTUP untuk kembali",
            TextAlignmentOptions.Center,
            MutedTextColor,
            11f,
            13f,
            TextWrappingModes.NoWrap);

        masteryRootObj.SetActive(false);
    }

    private TMP_Text CreateMetricBox(
        string name,
        RectTransform parent,
        float leftX,
        float topY,
        float width,
        float height,
        string caption,
        string initialValue,
        Color valueColor)
    {
        GameObject boxObj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Outline));
        boxObj.transform.SetParent(parent, false);
        RectTransform rect = boxObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(leftX, topY);
        rect.sizeDelta = new Vector2(width, height);

        Image bg = boxObj.GetComponent<Image>();
        bg.color = MetricCardBgColor;
        bg.raycastTarget = false;

        Outline outline = boxObj.GetComponent<Outline>();
        outline.effectColor = MetricCardOutlineColor;
        outline.effectDistance = new Vector2(1.2f, -1.2f);

        TMP_Text capTmp = CreateTopLeftText($"{name}_Caption", rect, new Vector2(10f, -6f), new Vector2(width - 20f, 18f), caption, TextAlignmentOptions.Center, MetricCaptionColor, 10.5f, 12.5f, TextWrappingModes.NoWrap);
        capTmp.fontStyle = FontStyles.Bold;

        TMP_Text valTmp = CreateTopLeftText($"{name}_Value", rect, new Vector2(10f, -26f), new Vector2(width - 20f, 30f), initialValue, TextAlignmentOptions.Center, valueColor, 15f, 22f, TextWrappingModes.NoWrap);
        valTmp.fontStyle = FontStyles.Bold;
        return valTmp;
    }

    private void BuildLevelSummaryRow(int index, float leftX, float topY, float width, float height)
    {
        GameObject rowObj = new GameObject($"Row_MasteryLevel_{index + 1}", typeof(RectTransform), typeof(Image));
        rowObj.transform.SetParent(masteryCardRect, false);
        RectTransform rowRect = rowObj.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(0f, 1f);
        rowRect.pivot = new Vector2(0f, 1f);
        rowRect.anchoredPosition = new Vector2(leftX, topY);
        rowRect.sizeDelta = new Vector2(width, height);

        Image rowBg = rowObj.GetComponent<Image>();
        rowBg.color = RowIdleBgColor;
        rowBg.raycastTarget = false;
        rowBackgrounds[index] = rowBg;

        GameObject stripObj = new GameObject("AccentStrip", typeof(RectTransform), typeof(Image));
        stripObj.transform.SetParent(rowRect, false);
        RectTransform stripRect = stripObj.GetComponent<RectTransform>();
        stripRect.anchorMin = new Vector2(0f, 0f);
        stripRect.anchorMax = new Vector2(0f, 1f);
        stripRect.pivot = new Vector2(0f, 0.5f);
        stripRect.anchoredPosition = Vector2.zero;
        stripRect.sizeDelta = new Vector2(4f, 0f);
        Image stripImg = stripObj.GetComponent<Image>();
        stripImg.color = RowAccentIdleColor;
        stripImg.raycastTarget = false;
        rowAccentStrips[index] = stripImg;

        TMP_Text titleTmp = CreateTopLeftText(
            $"Txt_LevelTitle_{index + 1}",
            rowRect,
            new Vector2(14f, -2f),
            new Vector2(width - 284f, height - 4f),
            $"L{index + 1}",
            TextAlignmentOptions.MidlineLeft,
            Color.white,
            12f,
            15.5f,
            TextWrappingModes.NoWrap);
        titleTmp.fontStyle = FontStyles.Bold;
        rowTitleTexts[index] = titleTmp;

        TMP_Text starsTmp = CreateTopLeftText(
            $"Txt_LevelStars_{index + 1}",
            rowRect,
            new Vector2(width - 260f, -2f),
            new Vector2(120f, height - 4f),
            "0/3",
            TextAlignmentOptions.Center,
            MutedTextColor,
            12.5f,
            16f,
            TextWrappingModes.NoWrap);
        starsTmp.fontStyle = FontStyles.Bold;
        rowStarsTexts[index] = starsTmp;

        GameObject statusObj = new GameObject($"Badge_Status_{index + 1}", typeof(RectTransform), typeof(Image));
        statusObj.transform.SetParent(rowRect, false);
        RectTransform statusRect = statusObj.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(1f, 0.5f);
        statusRect.anchorMax = new Vector2(1f, 0.5f);
        statusRect.pivot = new Vector2(1f, 0.5f);
        statusRect.anchoredPosition = new Vector2(-10f, 0f);
        statusRect.sizeDelta = new Vector2(114f, 24f);
        Image statusBg = statusObj.GetComponent<Image>();
        statusBg.color = StatusPendingBgColor;
        statusBg.raycastTarget = false;
        rowStatusBgs[index] = statusBg;

        TMP_Text statusTmp = CreateStretchedText($"Txt_Status_{index + 1}", statusRect, "Belum", TextAlignmentOptions.Center, StatusPendingTextColor, 11.5f, 14.5f, TextWrappingModes.NoWrap);
        statusTmp.fontStyle = FontStyles.Bold;
        rowStatusTexts[index] = statusTmp;
    }

    private TMP_Text CreateStatPill(string name, RectTransform parent, float leftX, float topY, float width, float height, string initialText, Color textColor)
    {
        GameObject pillObj = new GameObject(name, typeof(RectTransform), typeof(Image));
        pillObj.transform.SetParent(parent, false);
        RectTransform rect = pillObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(leftX, topY);
        rect.sizeDelta = new Vector2(width, height);

        Image bg = pillObj.GetComponent<Image>();
        bg.color = StatPillBgColor;
        bg.raycastTarget = false;

        TMP_Text tmp = CreateStretchedText($"{name}_Text", rect, initialText, TextAlignmentOptions.Center, textColor, 12f, 15.5f, TextWrappingModes.NoWrap);
        tmp.fontStyle = FontStyles.Bold;
        return tmp;
    }

    // =========================================================================
    // SAFE AREA & RESPONSIVE SCALE (ANDROID 20:9 & EDITOR 16:9)
    // =========================================================================

    private void ApplySafeAreaAndResponsiveScaleIfNeeded(bool force)
    {
        Rect safeArea = Screen.safeArea;
        int sw = Screen.width;
        int sh = Screen.height;
        bool screenUnchanged = sw == lastScreenWidth && sh == lastScreenHeight && safeArea == lastAppliedSafeArea;
        if (!force && screenUnchanged && (!isMasteryReportOpen || hasAppliedCardScale))
        {
            return;
        }

        if (!screenUnchanged || force)
        {
            lastScreenWidth = sw;
            lastScreenHeight = sh;
            lastAppliedSafeArea = safeArea;
            hasAppliedCardScale = false;
        }

        float denomW = Mathf.Max(1f, sw);
        float denomH = Mathf.Max(1f, sh);
        Vector2 minAnchor = new Vector2(Mathf.Clamp01(safeArea.xMin / denomW), Mathf.Clamp01(safeArea.yMin / denomH));
        Vector2 maxAnchor = new Vector2(Mathf.Clamp01(safeArea.xMax / denomW), 1f);

        if (chipSafeAreaRect != null)
        {
            chipSafeAreaRect.anchorMin = minAnchor;
            chipSafeAreaRect.anchorMax = maxAnchor;
            chipSafeAreaRect.offsetMin = Vector2.zero;
            chipSafeAreaRect.offsetMax = Vector2.zero;
        }

        if (masterySafeAreaRect != null)
        {
            masterySafeAreaRect.anchorMin = minAnchor;
            masterySafeAreaRect.anchorMax = maxAnchor;
            masterySafeAreaRect.offsetMin = Vector2.zero;
            masterySafeAreaRect.offsetMax = Vector2.zero;

            if (masteryCardRect != null)
            {
                Rect avail = masterySafeAreaRect.rect;
                if (avail.width > 120f && avail.height > 120f)
                {
                    float scaleX = (avail.width - 36f) / MasteryCardWidth;
                    float scaleY = (avail.height - 24f) / MasteryCardHeight;
                    float fitScale = Mathf.Clamp(Mathf.Min(scaleX, scaleY), 0.62f, 1.0f);
                    masteryCardRect.localScale = new Vector3(fitScale, fitScale, 1f);
                    hasAppliedCardScale = true;
                }
            }
        }
    }

    // =========================================================================
    // HELPER UI PROSEDURAL & GLYPH SAFETY
    // =========================================================================

    private bool SupportsGlyph(char glyph)
    {
        return safeFont != null && safeFont.HasCharacter(glyph);
    }

    private Button CreateCardActionButton(
        string name,
        RectTransform parent,
        float leftX,
        float topY,
        float width,
        float height,
        string label,
        Color bgColor,
        Color outlineColor,
        Color textColor,
        UnityEngine.Events.UnityAction onClick)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(leftX, topY);
        rect.sizeDelta = new Vector2(width, height);

        Image bgImg = obj.GetComponent<Image>();
        bgImg.color = bgColor;
        bgImg.raycastTarget = true;

        Outline outline = obj.GetComponent<Outline>();
        outline.effectColor = outlineColor;
        outline.effectDistance = new Vector2(1.8f, -1.8f);

        Button btn = obj.GetComponent<Button>();
        btn.targetGraphic = bgImg;
        btn.colors = MasteryButtonColorBlock;
        if (onClick != null)
        {
            btn.onClick.AddListener(onClick);
        }

        TMP_Text labelTmp = CreateStretchedText($"{name}_Label", rect, label, TextAlignmentOptions.Center, textColor, 14f, 18.5f, TextWrappingModes.NoWrap);
        labelTmp.fontStyle = FontStyles.Bold;
        return btn;
    }

    private TMP_Text CreateTopLeftText(
        string name,
        RectTransform parent,
        Vector2 anchoredPos,
        Vector2 sizeDelta,
        string content,
        TextAlignmentOptions alignment,
        Color color,
        float minSize,
        float maxSize,
        TextWrappingModes wrapMode)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = sizeDelta;

        TextMeshProUGUI tmp = obj.GetComponent<TextMeshProUGUI>();
        if (safeFont != null)
        {
            tmp.font = safeFont;
        }
        tmp.text = content;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = minSize;
        tmp.fontSizeMax = maxSize;
        tmp.textWrappingMode = wrapMode;
        tmp.raycastTarget = false;
        return tmp;
    }

    private TMP_Text CreateStretchedText(
        string name,
        RectTransform parent,
        string content,
        TextAlignmentOptions alignment,
        Color color,
        float minSize,
        float maxSize,
        TextWrappingModes wrapMode)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(8f, 2f);
        rect.offsetMax = new Vector2(-8f, -2f);

        TextMeshProUGUI tmp = obj.GetComponent<TextMeshProUGUI>();
        if (safeFont != null)
        {
            tmp.font = safeFont;
        }
        tmp.text = content;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = minSize;
        tmp.fontSizeMax = maxSize;
        tmp.textWrappingMode = wrapMode;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Image CreateBorderStrip(
        string name,
        RectTransform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 sizeDelta,
        Color color)
    {
        GameObject strip = new GameObject(name, typeof(RectTransform), typeof(Image));
        strip.transform.SetParent(parent, false);

        RectTransform rect = strip.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = sizeDelta;

        Image img = strip.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static void CreateDivider(string name, RectTransform parent, float leftX, float topY, float width, float height)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(leftX, topY);
        rect.sizeDelta = new Vector2(width, height);

        Image img = obj.GetComponent<Image>();
        img.color = CardDividerColor;
        img.raycastTarget = false;
    }

    private static Canvas FindTargetCanvas()
    {
        GameObject canvasObj = GameObject.Find("Canvas");
        if (canvasObj != null)
        {
            Canvas namedCanvas = canvasObj.GetComponent<Canvas>();
            if (namedCanvas != null)
            {
                return namedCanvas;
            }
        }

        return FindAnyObjectByType<Canvas>();
    }

    private static void RemoveBtnListener(Button btn, UnityEngine.Events.UnityAction action)
    {
        if (btn != null && action != null)
        {
            btn.onClick.RemoveListener(action);
        }
    }
}
