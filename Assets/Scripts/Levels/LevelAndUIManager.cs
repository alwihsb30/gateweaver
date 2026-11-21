using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[Serializable]
public class LevelScenario
{
    [Tooltip("Judul skenario level yang ditampilkan di bagian atas HUD")]
    public string levelTitle;

    [TextArea(2, 4)]
    [Tooltip("Deskripsi misi / permasalahan logika yang harus diselesaikan siswa")]
    public string missionDescription;

    [Tooltip("Batas maksimal jumlah kartu gerbang logika (AND, OR, NOT, XOR, NAND) untuk efisiensi rangkaian")]
    public int maxGateCardsQuota = 1;

    [Tooltip("Target Tabel Kebenaran 4 baris berurutan untuk kombinasi input AB: [0]=00, [1]=01, [2]=10, [3]=11")]
    public bool[] expectedTruthTable = new bool[4];

    [TextArea(1, 3)]
    [Tooltip("Petunjuk Tier 1 (Konsep): penjelasan logika kapan Output harus bernilai 1 atau 0")]
    public string hintTier1Concept;

    [TextArea(1, 3)]
    [Tooltip("Petunjuk Tier 2 (Komponen): jenis kartu gerbang logika yang disiapkan di meja")]
    public string hintTier2Component;

    [TextArea(1, 3)]
    [Tooltip("Petunjuk Tier 3 (Struktur Rakitan): urutan penyambungan Input -> Gerbang -> Output")]
    public string hintTier3Structure;

    [Header("Label Kontekstual 3D (v7.0 Fase 1)")]
    public string inputAName;
    public string inputAState0;
    public string inputAState1;
    public string inputBName;
    public string inputBState0;
    public string inputBState1;
    public string outputName;
    public string outputState0;
    public string outputState1;
}

public class LevelAndUIManager : MonoBehaviour
{
    private const int TruthTableRowCount = 4;

    [Header("1. Referensi Sistem & Node Inti")]
    [Tooltip("Referensi ke objek CircuitManager di Scene")]
    public CircuitManager circuitManager;

    [Tooltip("Kartu Input Source A (Card_Input_A)")]
    public GateNode inputANode;

    [Tooltip("Kartu Input Source B (Card_Input_B)")]
    public GateNode inputBNode;

    [Tooltip("Kartu Output Actuator (Card_Output)")]
    public GateNode outputNode;

    [Header("2. Referensi Komponen UI (TextMeshPro & Tombol)")]
    [Tooltip("Teks UI untuk menampilkan judul level saat ini")]
    public TMP_Text txtLevelTitle;

    [Tooltip("Teks UI untuk menampilkan deskripsi misi/soal")]
    public TMP_Text txtMissionDesc;

    [Tooltip("Teks UI untuk menampilkan pemakaian kuota kartu gerbang secara real-time")]
    public TMP_Text txtQuotaStatus;

    [Tooltip("Teks UI untuk menampilkan umpan balik status rangkaian & hasil verifikasi")]
    public TMP_Text txtFeedback;

    [Tooltip("Objek tombol 'Level Berikutnya' / 'Mode Sandbox Bebas' (di-auto-bind ke Btn_NextLevel jika kosong)")]
    public GameObject nextLevelButtonObj;

    [Tooltip("Teks label pada tombol nextLevelButtonObj (otomatis di-auto-bind ke TMP_Text child jika kosong)")]
    public TMP_Text txtNextLevelButtonLabel;

    [Tooltip("Objek tombol 'Reset Level' / 'Main Ulang (Level 1)' (di-auto-bind ke Btn_ResetLevel jika kosong)")]
    public GameObject resetLevelButtonObj;

    [Tooltip("Teks label pada tombol resetLevelButtonObj (otomatis di-auto-bind ke TMP_Text child jika kosong)")]
    public TMP_Text txtResetLevelButtonLabel;

    [Tooltip("Teks pada tombol Toggle Input A (opsional, diperbarui real-time menjadi 'Input A: 0/1')")]
    public TMP_Text txtBtnToggleA;

    [Tooltip("Teks pada tombol Toggle Input B (opsional, diperbarui real-time menjadi 'Input B: 0/1')")]
    public TMP_Text txtBtnToggleB;

    [Header("3. Daftar Skenario Level & Status Aktif")]
    [Tooltip("Daftar 7 skenario level berjenjang Kurikulum Merdeka (otomatis terisi di Awake jika kosong)")]
    public List<LevelScenario> levels = new List<LevelScenario>();

    [Tooltip("Indeks level yang sedang aktif (0..6 = Level 1..7)")]
    public int currentLevelIndex = 0;

    [Header("4. Panel Matrix & Rumus Boolean Real-Time (Hari 15)")]
    [Tooltip("Panel UI prosedural Live Truth Table Scanner (otomatis dibangun di Canvas bila kosong)")]
    public GameObject truthTableScannerPanelObj;

    [Header("5. Audio Prosedural, Haptik & Sistem Bintang (Hari 16)")]
    [Tooltip("Status mute efek suara prosedural (dapat diubah lewat tombol Audio ON/OFF pada panel)")]
    public bool isAudioMuted = false;

    private const int AudioSampleRate = 44100;
    private const float AudioEnvelopeSeconds = 0.005f;
    private const float HapticCooldownSeconds = 0.85f;
    private const int WaveformSampleCount = 16;
    private const float WaveformSampleIntervalSeconds = 0.25f;

    private const string PrefKeyLevelClearedPrefix = "GW_v3_LevelCleared_";
    private const string PrefKeyLevelStarsPrefix = "GW_v3_LevelStars_";
    private const string PrefKeyLastUnlockedLevel = "GW_v3_LastUnlockedLevel";
    private const string PrefKeyAudioMuted = "GW_v3_AudioMuted";

    private bool isVerifying = false;
    private bool isSandboxMode = false;
    private bool awaitingPostVictoryChoice = false;
    private bool[] levelCompletedFlags;
    private int[] levelBestStars;
    private int attemptsOnCurrentLevel = 0;
    private int hintTierUsed = 0;

    // Komponen Matrix UI Prosedural & Kontrol Tab/Lipat (v4.0)
    private const float ScannerPanelWidth = 460f;
    private const float ScannerExpandedHeight = 416f;
    private const float ScannerCollapsedHeight = 108f;

    private RectTransform scannerPanelRect;
    private RectTransform scannerBadgeRect;
    private RectTransform audioToggleRect;
    private GameObject scannerDividerObj;
    private GameObject truthTableTabContentObj;
    private GameObject waveformMonitorPanelObj;
    private GameObject scannerFooterGroupObj;

    private Button tabTruthTableButton;
    private Image tabTruthTableBackground;
    private TMP_Text txtTabTruthTableLabel;
    private Button tabOscilloscopeButton;
    private Image tabOscilloscopeBackground;
    private TMP_Text txtTabOscilloscopeLabel;
    private Button collapseHudButton;
    private RectTransform collapseHudButtonRect;
    private Image collapseHudBackground;
    private TMP_Text txtCollapseHudLabel;
    private bool isOscilloscopeTabActive = false;
    private bool isScannerHudCollapsed = false;

    // Komponen Modal Pemilih Level Instan (Direct Level Selector, v4.0)
    private GameObject levelSelectorModalPanel;
    private Button levelSelectorOpenButton;
    private Button levelSelectorCloseButton;
    private Button levelSelectorSandboxButton;
    private Image levelSelectorSandboxBg;
    private Outline levelSelectorSandboxOutline;
    private TMP_Text txtLevelSelectorSandboxStatus;
    private Button[] levelSelectorItemButtons;
    private UnityEngine.Events.UnityAction[] levelSelectorItemHandlers;
    private Image[] levelSelectorItemBackgrounds;
    private Outline[] levelSelectorItemOutlines;
    private TMP_Text[] levelSelectorItemStatusTexts;

    // Tombol & Pengendali Mode Udara Air-Weaving (v6.0)
    private AirWeavingController airWeavingController;
    private Button airWeavingToggleButton;
    private Image airWeavingToggleBg;
    private Outline airWeavingToggleOutline;
    private TMP_Text txtAirWeavingToggleLabel;
    private bool hasSavedMissionDescLayout = false;
    private Vector2 savedMissionDescAnchoredPos;
    private Vector2 savedMissionDescSizeDelta;
    private float savedMissionDescFontSize;
    private float savedMissionDescLineSpacing;
    private TextAlignmentOptions savedMissionDescAlignment;

    // Referensi Tombol HUD Bawaan Unity (Reskin Tema Sci-Fi v4.0)
    private Button btnInputA;
    private Image imgBtnInputA;
    private Outline outlineBtnInputA;
    private Button btnInputB;
    private Image imgBtnInputB;
    private Outline outlineBtnInputB;
    private Button btnVerify;
    private Button btnNextLevel;
    private Button btnResetLevel;
    private bool hudButtonThemeApplied = false;

    private TMP_Text txtBooleanFormula;
    private TMP_Text txtCurrentOutputReadout;
    private TMP_Text txtStarMastery;
    private TMP_Text txtAudioToggleLabel;
    private Image audioToggleBackground;
    private Button audioToggleButton;
    private TMP_Text txtHintButtonLabel;
    private Image hintButtonBackground;
    private Button hintButton;
    private Button reportOpenButton;

    // Komponen Modal Rapor Kompetensi Informatika (Hari 16 / v3.0)
    private GameObject competencyReportPanelObj;
    private TMP_Text txtReportSummaryStats;
    private TMP_Text txtReportPredicate;
    private TMP_Text[] reportRowTitleTexts;
    private TMP_Text[] reportRowStarsTexts;
    private TMP_Text[] reportRowStatusTexts;
    private Image[] reportRowBackgrounds;
    private Button reportCloseButton;
    private Button reportSandboxButton;
    private Button reportResetButton;

    // Komponen Osiloskop Sinyal Digital Prosedural (3 Kanal x 16 Sampel = 48 bar, Zero GC)
    private TMP_Text txtWaveformLabelA;
    private TMP_Text txtWaveformLabelB;
    private TMP_Text txtWaveformLabelY;
    private RectTransform[] waveBarRectsA;
    private Image[] waveBarImagesA;
    private RectTransform[] waveBarRectsB;
    private Image[] waveBarImagesB;
    private RectTransform[] waveBarRectsY;
    private Image[] waveBarImagesY;
    private readonly int[] sampleA = new int[WaveformSampleCount];
    private readonly int[] sampleB = new int[WaveformSampleCount];
    private readonly int[] sampleY = new int[WaveformSampleCount];
    private readonly float[] waveformBarXPositions = new float[WaveformSampleCount];
    private float waveformSampleTimer = 0f;
    private bool waveformBaselineInitialized = false;
    private int lastSampledWaveA = 0;
    private int lastSampledWaveB = 0;
    private int lastSampledWaveY = -1;
    private int renderedWaveLabelA = -2;
    private int renderedWaveLabelB = -2;
    private int renderedWaveLabelY = -2;
    private readonly Image[] matrixRowBackgrounds = new Image[TruthTableRowCount];
    private readonly TMP_Text[] matrixRowTargetTexts = new TMP_Text[TruthTableRowCount];
    private readonly TMP_Text[] matrixRowMeasuredTexts = new TMP_Text[TruthTableRowCount];
    private readonly TMP_Text[] matrixRowStatusTexts = new TMP_Text[TruthTableRowCount];
    private readonly bool[] matrixRowScannedFlags = new bool[TruthTableRowCount];
    private readonly bool[] matrixRowPassedFlags = new bool[TruthTableRowCount];

    private int scanningRowIndex = -1;
    private string lastTopologySignature = string.Empty;
    private bool hasLastTopologySignature = false;
    private int lastTopologyActiveGates = -1;
    private bool lastTopologyOutputTracked = false;
    private int lastTopologyConnectionCount = -1;
    private string lastRenderedFormula = string.Empty;
    private bool hasRenderedFormulaBaseline = false;
    private int lastRenderedOutputState = -2;
    private int lastDisplayedInputA = -1;
    private int lastDisplayedInputB = -1;
    private int lastQuotaActiveCount = -1;
    private int lastQuotaMaxCount = -1;
    private int lastQuotaSandboxMode = -1;
    private int cachedFormulaTopologyHash = 0;
    private bool hasCachedFormulaTopologyHash = false;
    private string cachedLiveBooleanFormula = "Y = (Belum Terhubung)";
    private int lastEdgeTopologyHash = 0;
    private bool hasLastEdgeTopologyHash = false;

    // Pencahayaan AR Studio Pelengkap & Kontras Txt_Feedback (v5.0)
    private GameObject arRimFillLightObj;
    private GameObject feedbackBackdropObj;
    private bool ownsFeedbackBackdrop = false;
    private Material feedbackOriginalSharedMaterial;
    private Material feedbackTextMaterialInstance;
    private string lastCheckedFeedbackText;

    // Interaksi Sentuh 3D AR Raycast (New Input System + Zero-GC, v4.0)
    private const float TouchRaycastMaxDistance = 100f;
    private Camera cachedMainCamera;
    private readonly RaycastHit[] touchRaycastHits = new RaycastHit[8];
    private readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>(8);
    private PointerEventData cachedPointerEventData;

    // Synthesizer Audio Prosedural & Pelacak Event Audio/Haptik
    private AudioSource sfxAudioSource;
    private AudioClip clipToggleInput;
    private AudioClip clipSnapConnect;
    private readonly AudioClip[] clipScanRows = new AudioClip[TruthTableRowCount];
    private AudioClip clipRowPass;
    private AudioClip clipRowFail;
    private AudioClip clipVictoryFanfare;

    private float lastHapticTime = -10f;
    private bool inputsBaselineInitialized = false;
    private bool lastInputAState = false;
    private bool lastInputBState = false;
    private int lastActiveConnectionCount = 0;
    private bool wasCircuitComplete = false;

    private static readonly Color MatrixPanelBgColor = new Color(0.04f, 0.08f, 0.15f, 0.80f);
    private static readonly Color MatrixAccentCyanColor = new Color(0.18f, 0.88f, 1.00f, 0.95f);
    private static readonly Color MatrixHeaderBgColor = new Color(0.08f, 0.16f, 0.28f, 0.90f);
    private static readonly Color RowIdleBgColor = new Color(0.07f, 0.12f, 0.20f, 0.65f);
    private static readonly Color RowActiveManualBgColor = new Color(0.12f, 0.42f, 0.65f, 0.78f);
    private static readonly Color RowScanningBgColor = new Color(0.55f, 0.42f, 0.08f, 0.85f);
    private static readonly Color RowPassBgColor = new Color(0.08f, 0.34f, 0.18f, 0.75f);
    private static readonly Color RowFailBgColor = new Color(0.42f, 0.10f, 0.12f, 0.78f);
    private static readonly Color RowSandboxDoneBgColor = new Color(0.08f, 0.30f, 0.36f, 0.75f);
    private static readonly Color MatrixBrightGreenColor = new Color(0.25f, 1.00f, 0.35f, 1f);
    private static readonly Color MatrixBrightRedColor = new Color(1.00f, 0.30f, 0.25f, 1f);
    private static readonly Color MatrixBrightCyanColor = new Color(0.30f, 0.95f, 1.00f, 1f);
    private static readonly Color WaveTrackBgColor = new Color(0.03f, 0.06f, 0.11f, 0.92f);
    private static readonly Color WaveHighAColor = new Color(0.18f, 0.85f, 1.00f, 1f);
    private static readonly Color WaveLowAColor = new Color(0.10f, 0.30f, 0.52f, 0.85f);
    private static readonly Color WaveHighBColor = new Color(1.00f, 0.84f, 0.22f, 1f);
    private static readonly Color WaveLowBColor = new Color(0.45f, 0.36f, 0.10f, 0.85f);
    private static readonly Color WaveHighYColor = new Color(0.20f, 1.00f, 0.45f, 1f);
    private static readonly Color WaveLowYColor = new Color(0.85f, 0.25f, 0.16f, 0.90f);
    private static readonly Color WaveUntrackedYColor = new Color(0.38f, 0.44f, 0.52f, 0.65f);

    // Palet Tab, Lipat HUD, & Reskin Tombol Sci-Fi Gelap-Sian (v4.0)
    private static readonly Color TabActiveBgColor = new Color(0.11f, 0.48f, 0.66f, 0.98f);
    private static readonly Color TabActiveTextColor = new Color(0.95f, 1.00f, 1.00f, 1f);
    private static readonly Color TabInactiveBgColor = new Color(0.07f, 0.14f, 0.24f, 0.88f);
    private static readonly Color TabInactiveTextColor = new Color(0.62f, 0.78f, 0.88f, 1f);
    private static readonly Color CollapseBtnExpandedBgColor = new Color(0.12f, 0.20f, 0.30f, 0.94f);
    private static readonly Color CollapseBtnExpandedTextColor = new Color(0.82f, 0.92f, 1.00f, 1f);
    private static readonly Color CollapseBtnCollapsedBgColor = new Color(0.10f, 0.50f, 0.64f, 0.98f);
    private static readonly Color CollapseBtnCollapsedTextColor = new Color(0.96f, 1.00f, 1.00f, 1f);

    private static readonly Color InputLowBgColor = new Color(0.09f, 0.16f, 0.26f, 0.96f);
    private static readonly Color InputLowOutlineColor = new Color(0.28f, 0.56f, 0.76f, 0.88f);
    private static readonly Color InputLowTextColor = new Color(0.86f, 0.94f, 1.00f, 1f);
    private static readonly Color InputHighBgColor = new Color(0.14f, 0.82f, 0.68f, 0.98f);
    private static readonly Color InputHighOutlineColor = new Color(0.45f, 1.00f, 0.88f, 1.00f);
    private static readonly Color InputHighTextColor = new Color(0.03f, 0.12f, 0.16f, 1f);
    private static readonly Color VerifyBtnBgColor = new Color(0.075f, 0.451f, 0.600f, 0.96f);
    private static readonly Color VerifyBtnOutlineColor = new Color(0.28f, 0.90f, 1.00f, 0.96f);
    private static readonly Color NextLevelBtnBgColor = new Color(0.078f, 0.431f, 0.294f, 0.96f);
    private static readonly Color NextLevelBtnOutlineColor = new Color(0.28f, 0.95f, 0.65f, 0.96f);
    private static readonly Color ResetBtnBgColor = new Color(0.14f, 0.16f, 0.22f, 0.96f);
    private static readonly Color ResetBtnOutlineColor = new Color(0.92f, 0.44f, 0.28f, 0.94f);
    private static readonly Color ResetBtnTextColor = new Color(1.00f, 0.64f, 0.48f, 1f);
    private static readonly Color LevelSelectorOpenBgColor = new Color(0.08f, 0.24f, 0.36f, 0.96f);
    private static readonly Color LevelSelectorCardIdleBgColor = new Color(0.08f, 0.15f, 0.25f, 0.95f);
    private static readonly Color LevelSelectorCardClearedBgColor = new Color(0.08f, 0.24f, 0.24f, 0.95f);
    private static readonly Color LevelSelectorCardActiveBgColor = new Color(0.11f, 0.38f, 0.54f, 0.98f);
    private static readonly Color LevelSelectorCardIdleOutlineColor = new Color(0.24f, 0.50f, 0.68f, 0.85f);
    private static readonly Color LevelSelectorCardActiveOutlineColor = new Color(0.32f, 0.96f, 1.00f, 1.00f);
    private static readonly Color LevelSelectorSandboxIdleBgColor = new Color(0.14f, 0.24f, 0.38f, 0.96f);
    private static readonly Color LevelSelectorSandboxActiveBgColor = new Color(0.12f, 0.46f, 0.44f, 0.98f);
    private static readonly Color LevelSelectorSandboxOutlineColor = new Color(0.35f, 0.95f, 0.82f, 0.95f);

    private static readonly ColorBlock SciFiButtonColorBlock = new ColorBlock
    {
        normalColor = Color.white,
        highlightedColor = new Color(0.90f, 0.97f, 1.00f, 1f),
        pressedColor = new Color(0.70f, 0.80f, 0.88f, 1f),
        selectedColor = Color.white,
        disabledColor = new Color(0.45f, 0.48f, 0.52f, 0.60f),
        colorMultiplier = 1f,
        fadeDuration = 0.08f
    };

    private CompactHudMenuController compactHudMenu;
    private bool isReadyForNextLevel = false;
    private bool isCircuitCompleteLive = false;
    private int manualPassedRowMask = 0;
    private bool hasShownManualOverQuotaToast = false;

    public bool IsSandboxMode => isSandboxMode;
    public bool IsVerifying => isVerifying;
    public bool IsReadyForNextLevel => isReadyForNextLevel;
    public bool IsAwaitingSandboxTransition => awaitingPostVictoryChoice;
    public bool IsCircuitCompleteLive => isCircuitCompleteLive;
    public int CurrentHintTier => hintTierUsed;
    public bool IsUiOverlayOpen => (compactHudMenu != null && compactHudMenu.IsMenuDrawerOpen)
                                   || (levelSelectorModalPanel != null && levelSelectorModalPanel.activeSelf)
                                   || (competencyReportPanelObj != null && competencyReportPanelObj.activeSelf);

    public void TriggerToggleAirWeavingFromMenu() => OnToggleAirWeavingClicked();
    public void CloseAllModalsForMenu() { CloseLevelSelector(false); CloseCompetencyReport(); }
    public void TryShowLowPriorityNoHandToast(string msg, Color color) => compactHudMenu?.ShowToast(msg, color, 3.2f, false, true);
    public void ClearLowPriorityNoHandToast() => compactHudMenu?.ClearLowPriorityNoHandToast();
    public void ShowFeedbackToast(string msg, Color color, float duration = 2.6f) => compactHudMenu?.ShowToast(msg, color, duration, true, false);

    private void Awake()
    {
        ConfigureMobileRuntimeOptimization();

        // Isi otomatis 7 skenario default jika daftar levels di Inspector masih kosong
        if (levels == null || levels.Count == 0)
        {
            InitializeDefaultLevels();
        }
        else
        {
            EnsureLevelProgressArraysInitialized();
        }

        // Cari otomatis CircuitManager jika belum di-assign di Inspector
        if (circuitManager == null)
        {
            circuitManager = FindAnyObjectByType<CircuitManager>();
        }

        LoadPersistentProgress();
        AutoBindCoreNodesIfNeeded();
        AutoBindToggleButtonsTextIfNeeded();
        AutoBindActionButtonsIfNeeded();
        ApplySciFiHudButtonTheme();
        ApplyFeedbackContrastStyle();
        InitializeProceduralAudio();
        InitializeTruthTableMatrixUI();
    }

    private void Start()
    {
        ConfigureARStudioLighting();
        InitializeCompactHudMenu();
        LoadLevel(currentLevelIndex);
        EnsureAirWeavingControllerBootstrapped();
    }

    private void Update()
    {
        Handle3DCardTouchRaycast();
        UpdateQuotaStatusUI();
        UpdateInputToggleButtonsUI();
        UpdateTruthTableMatrixLive();
        UpdateWaveformMonitorLive();
        MonitorInputStateAudio();
        if (compactHudMenu != null) compactHudMenu.TickToast(Time.unscaledDeltaTime, isVerifying);
        else UpdateFeedbackBackdropVisibility();
    }

    private void OnDestroy()
    {
        StopAllCoroutines();

        if (audioToggleButton != null)
        {
            audioToggleButton.onClick.RemoveListener(ToggleAudioMute);
            audioToggleButton = null;
        }

        if (hintButton != null)
        {
            hintButton.onClick.RemoveListener(OnHintButtonClicked);
            hintButton = null;
        }

        if (reportOpenButton != null)
        {
            reportOpenButton.onClick.RemoveListener(ToggleCompetencyReport);
            reportOpenButton = null;
        }

        if (reportCloseButton != null)
        {
            reportCloseButton.onClick.RemoveListener(CloseCompetencyReport);
            reportCloseButton = null;
        }

        if (reportSandboxButton != null)
        {
            reportSandboxButton.onClick.RemoveListener(OnReportSandboxClicked);
            reportSandboxButton = null;
        }

        if (reportResetButton != null)
        {
            reportResetButton.onClick.RemoveListener(OnReportResetProgressClicked);
            reportResetButton = null;
        }

        if (tabTruthTableButton != null)
        {
            tabTruthTableButton.onClick.RemoveListener(SelectTruthTableTab);
            tabTruthTableButton = null;
        }

        if (tabOscilloscopeButton != null)
        {
            tabOscilloscopeButton.onClick.RemoveListener(SelectOscilloscopeTab);
            tabOscilloscopeButton = null;
        }

        if (collapseHudButton != null)
        {
            collapseHudButton.onClick.RemoveListener(ToggleCollapseScannerHud);
            collapseHudButton = null;
        }

        if (levelSelectorOpenButton != null)
        {
            levelSelectorOpenButton.onClick.RemoveListener(ToggleLevelSelector);
            levelSelectorOpenButton = null;
        }

        if (airWeavingToggleButton != null)
        {
            airWeavingToggleButton.onClick.RemoveListener(OnToggleAirWeavingClicked);
            airWeavingToggleButton = null;
        }

        if (airWeavingController != null)
        {
            airWeavingController.OnAirWeavingModeChanged -= HandleAirWeavingModeChanged;
            airWeavingController.OnOutputVerifyRequested -= OnVerifyButtonClicked;
            airWeavingController.OnOutputNextLevelRequested -= NextLevel;
            airWeavingController = null;
        }
        compactHudMenu = null;

        if (levelSelectorCloseButton != null)
        {
            levelSelectorCloseButton.onClick.RemoveListener(CloseLevelSelectorWithAudio);
            levelSelectorCloseButton = null;
        }

        if (levelSelectorSandboxButton != null)
        {
            levelSelectorSandboxButton.onClick.RemoveListener(OnLevelSelectorSandboxClicked);
            levelSelectorSandboxButton = null;
        }

        if (levelSelectorItemButtons != null)
        {
            for (int i = 0; i < levelSelectorItemButtons.Length; i++)
            {
                if (levelSelectorItemButtons[i] != null)
                {
                    if (levelSelectorItemHandlers != null
                        && i < levelSelectorItemHandlers.Length
                        && levelSelectorItemHandlers[i] != null)
                    {
                        levelSelectorItemButtons[i].onClick.RemoveListener(levelSelectorItemHandlers[i]);
                    }
                    levelSelectorItemButtons[i] = null;
                }
            }
            levelSelectorItemButtons = null;
        }
        levelSelectorItemHandlers = null;

        uiRaycastResults.Clear();
        cachedPointerEventData = null;
        cachedMainCamera = null;

        if (arRimFillLightObj != null)
        {
            Destroy(arRimFillLightObj);
            arRimFillLightObj = null;
        }

        if (feedbackTextMaterialInstance != null)
        {
            if (txtFeedback != null && feedbackOriginalSharedMaterial != null)
            {
                txtFeedback.fontSharedMaterial = feedbackOriginalSharedMaterial;
            }
            Destroy(feedbackTextMaterialInstance);
            feedbackTextMaterialInstance = null;
        }
        feedbackOriginalSharedMaterial = null;

        if (feedbackBackdropObj != null)
        {
            if (ownsFeedbackBackdrop)
            {
                Destroy(feedbackBackdropObj);
            }
            feedbackBackdropObj = null;
        }
        ownsFeedbackBackdrop = false;

        lastCheckedFeedbackText = null;

        PlayerPrefs.Save();
        DestroyProceduralAudioClips();
        Screen.sleepTimeout = SleepTimeout.SystemSetting;
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Mengunci frame rate target 60 FPS dan mencegah layar smartphone redup/mati otomatis
    /// selama sesi pemindaian kartu AR berlangsung.
    /// </summary>
    private static void ConfigureMobileRuntimeOptimization()
    {
        Application.targetFrameRate = 60;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }

    /// <summary>
    /// Mengatur pencahayaan lingkungan Trilight yang seimbang untuk kamera AR Android serta menambahkan
    /// satu AR_RimFillLight tanpa bayangan bila scene hanya memiliki <= 1 Directional Light agar sisi gelap
    /// dan siluet model 3D Quaternius tetap terbaca jelas di atas meja nyata pada 60 FPS.
    /// </summary>
    private void ConfigureARStudioLighting()
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.76f, 0.85f, 0.96f, 1f);
        RenderSettings.ambientEquatorColor = new Color(0.56f, 0.62f, 0.70f, 1f);
        RenderSettings.ambientGroundColor = new Color(0.32f, 0.38f, 0.48f, 1f);

#if UNITY_6000_5_OR_NEWER
        Light[] sceneLights = FindObjectsByType<Light>();
#else
        Light[] sceneLights = FindObjectsByType<Light>(FindObjectsSortMode.None);
#endif

        int directionalLightCount = 0;
        Light primaryDirectional = null;
        bool hasExistingRimLight = false;

        if (sceneLights != null)
        {
            for (int i = 0; i < sceneLights.Length; i++)
            {
                Light lightComp = sceneLights[i];
                if (lightComp == null || !lightComp.isActiveAndEnabled)
                {
                    continue;
                }

                if (string.Equals(lightComp.gameObject.name, "AR_RimFillLight", StringComparison.Ordinal))
                {
                    hasExistingRimLight = true;
                }

                if (lightComp.type == LightType.Directional)
                {
                    directionalLightCount++;
                    if (primaryDirectional == null)
                    {
                        primaryDirectional = lightComp;
                    }
                }
            }
        }

        if (directionalLightCount <= 1 && !hasExistingRimLight)
        {
            arRimFillLightObj = new GameObject("AR_RimFillLight");
            Light rimLight = arRimFillLightObj.AddComponent<Light>();
            rimLight.type = LightType.Directional;
            rimLight.shadows = LightShadows.None;
            rimLight.intensity = 0.42f;
            rimLight.color = new Color(0.82f, 0.94f, 1.00f, 1f);

            float baseYaw = primaryDirectional != null
                ? primaryDirectional.transform.eulerAngles.y
                : -30f;
            arRimFillLightObj.transform.rotation = Quaternion.Euler(34f, baseYaw + 145f, 0f);
        }
    }

    /// <summary>
    /// Mencari referensi Input A, Input B, dan Output secara otomatis jika belum diisi manual di Inspector.
    /// </summary>
    private void AutoBindCoreNodesIfNeeded()
    {
        if (inputANode != null && inputBNode != null && outputNode != null)
        {
            return;
        }

#if UNITY_6000_5_OR_NEWER
        GateNode[] nodes = FindObjectsByType<GateNode>();
#else
        GateNode[] nodes = FindObjectsByType<GateNode>(FindObjectsSortMode.None);
#endif
        for (int i = 0; i < nodes.Length; i++)
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
                string objName = node.gameObject.name.ToUpperInvariant();
                if (inputANode == null && objName.Contains("A"))
                {
                    inputANode = node;
                }
                else if (inputBNode == null && objName.Contains("B"))
                {
                    inputBNode = node;
                }
            }
        }
    }

    /// <summary>
    /// Mencari otomatis TMP_Text pada tombol Btn_ToggleInputA dan Btn_ToggleInputB jika belum di-assign di Inspector.
    /// </summary>
    private void AutoBindToggleButtonsTextIfNeeded()
    {
        if (txtBtnToggleA == null)
        {
            GameObject btnA = GameObject.Find("Btn_ToggleInputA");
            if (btnA != null)
            {
                txtBtnToggleA = btnA.GetComponentInChildren<TMP_Text>(true);
            }
        }

        if (txtBtnToggleB == null)
        {
            GameObject btnB = GameObject.Find("Btn_ToggleInputB");
            if (btnB != null)
            {
                txtBtnToggleB = btnB.GetComponentInChildren<TMP_Text>(true);
            }
        }
    }

    /// <summary>
    /// Mencari otomatis referensi tombol & label untuk Next Level dan Reset Level jika belum diisi di Inspector.
    /// </summary>
    private void AutoBindActionButtonsIfNeeded()
    {
        if (nextLevelButtonObj == null)
        {
            nextLevelButtonObj = GameObject.Find("Btn_NextLevel");
        }

        if (txtNextLevelButtonLabel == null && nextLevelButtonObj != null)
        {
            txtNextLevelButtonLabel = nextLevelButtonObj.GetComponentInChildren<TMP_Text>(true);
        }

        if (resetLevelButtonObj == null)
        {
            resetLevelButtonObj = GameObject.Find("Btn_ResetLevel");
        }

        if (txtResetLevelButtonLabel == null && resetLevelButtonObj != null)
        {
            txtResetLevelButtonLabel = resetLevelButtonObj.GetComponentInChildren<TMP_Text>(true);
        }

        if (txtFeedback != null && !txtFeedback.enableAutoSizing)
        {
            txtFeedback.enableAutoSizing = true;
            txtFeedback.fontSizeMin = 20f;
            txtFeedback.fontSizeMax = 30f;
        }
    }

    /// <summary>
    /// Menyelaraskan gaya visual tombol HUD bawaan Unity (Input A/B, Uji Rangkaian, Level Berikutnya, Reset Level)
    /// ke tema sci-fi gelap-sian seragam tanpa mengubah posisi anchor asli di Scene.
    /// </summary>
    private void ApplySciFiHudButtonTheme()
    {
        if (hudButtonThemeApplied)
        {
            return;
        }

        Canvas targetCanvas = FindTargetCanvas();

        btnInputA = ResolveSceneHudButton("Btn_ToggleInputA", targetCanvas);
        btnInputB = ResolveSceneHudButton("Btn_ToggleInputB", targetCanvas);
        btnVerify = ResolveSceneHudButton("Btn_VerifyCircuit", targetCanvas);
        btnNextLevel = nextLevelButtonObj != null
            ? nextLevelButtonObj.GetComponent<Button>()
            : ResolveSceneHudButton("Btn_NextLevel", targetCanvas);
        btnResetLevel = resetLevelButtonObj != null
            ? resetLevelButtonObj.GetComponent<Button>()
            : ResolveSceneHudButton("Btn_ResetLevel", targetCanvas);

        if (btnInputA != null)
        {
            if (txtBtnToggleA == null)
            {
                txtBtnToggleA = btnInputA.GetComponentInChildren<TMP_Text>(true);
            }

            ApplySingleSciFiButtonStyle(
                btnInputA,
                InputLowBgColor,
                InputLowOutlineColor,
                InputLowTextColor,
                out imgBtnInputA,
                out outlineBtnInputA);
        }

        if (btnInputB != null)
        {
            if (txtBtnToggleB == null)
            {
                txtBtnToggleB = btnInputB.GetComponentInChildren<TMP_Text>(true);
            }

            ApplySingleSciFiButtonStyle(
                btnInputB,
                InputLowBgColor,
                InputLowOutlineColor,
                InputLowTextColor,
                out imgBtnInputB,
                out outlineBtnInputB);
        }

        if (btnVerify != null)
        {
            ApplySingleSciFiButtonStyle(
                btnVerify,
                VerifyBtnBgColor,
                VerifyBtnOutlineColor,
                Color.white,
                out _,
                out _);
        }

        if (btnNextLevel != null)
        {
            ApplySingleSciFiButtonStyle(
                btnNextLevel,
                NextLevelBtnBgColor,
                NextLevelBtnOutlineColor,
                Color.white,
                out _,
                out _);
        }

        if (btnResetLevel != null)
        {
            ApplySingleSciFiButtonStyle(
                btnResetLevel,
                ResetBtnBgColor,
                ResetBtnOutlineColor,
                ResetBtnTextColor,
                out _,
                out _);
        }

        hudButtonThemeApplied = btnInputA != null
                                || btnInputB != null
                                || btnVerify != null
                                || btnNextLevel != null
                                || btnResetLevel != null;

        // Paksa penyegaran tampilan status 0/1 pada tombol Input A & B segera setelah tema diterapkan
        lastDisplayedInputA = -1;
        lastDisplayedInputB = -1;
    }

    /// <summary>
    /// Mempertajam keterbacaan Txt_Feedback di atas kartu marker putih terang menggunakan outline SDF gelap
    /// pada material klon khusus TMP + latar kapsul gelap tipis yang menempel pada RectTransform Txt_Feedback
    /// dan otomatis nonaktif bila teks kosong.
    /// </summary>
    private void ApplyFeedbackContrastStyle()
    {
        if (txtFeedback == null)
        {
            return;
        }

        txtFeedback.margin = new Vector4(14f, 6f, 14f, 6f);
        txtFeedback.raycastTarget = false;

        // 1. Terapkan outline SDF hitam pekat pada klon material TMP khusus (dibersihkan di OnDestroy)
        if (feedbackTextMaterialInstance == null && txtFeedback.fontSharedMaterial != null)
        {
            feedbackOriginalSharedMaterial = txtFeedback.fontSharedMaterial;
            feedbackTextMaterialInstance = new Material(feedbackOriginalSharedMaterial);
            feedbackTextMaterialInstance.EnableKeyword("OUTLINE_ON");
            if (feedbackTextMaterialInstance.HasProperty(ShaderUtilities.ID_OutlineWidth))
            {
                feedbackTextMaterialInstance.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.14f);
            }
            if (feedbackTextMaterialInstance.HasProperty(ShaderUtilities.ID_OutlineColor))
            {
                feedbackTextMaterialInstance.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.02f, 0.04f, 0.08f, 1f));
            }
            txtFeedback.fontMaterial = feedbackTextMaterialInstance;
        }

        // 2. Tempelkan kapsul latar gelap tipis tepat di belakang RectTransform Txt_Feedback (bukan panel HUD baru)
        RectTransform fbRect = txtFeedback.rectTransform;
        Transform fbParent = fbRect != null ? fbRect.parent : null;
        if (fbRect != null && fbParent != null && feedbackBackdropObj == null)
        {
            Transform existingBackdrop = fbParent.Find("Txt_Feedback_Backdrop");
            if (existingBackdrop != null)
            {
                feedbackBackdropObj = existingBackdrop.gameObject;
                ownsFeedbackBackdrop = false;
            }
            else
            {
                feedbackBackdropObj = new GameObject("Txt_Feedback_Backdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
                ownsFeedbackBackdrop = true;
                feedbackBackdropObj.transform.SetParent(fbParent, false);
                feedbackBackdropObj.transform.SetSiblingIndex(fbRect.GetSiblingIndex());

                RectTransform bgRect = feedbackBackdropObj.GetComponent<RectTransform>();
                bgRect.anchorMin = fbRect.anchorMin;
                bgRect.anchorMax = fbRect.anchorMax;
                bgRect.pivot = fbRect.pivot;
                bgRect.anchoredPosition = fbRect.anchoredPosition;
                bgRect.sizeDelta = new Vector2(fbRect.sizeDelta.x + 18f, fbRect.sizeDelta.y + 8f);
                bgRect.localRotation = fbRect.localRotation;
                bgRect.localScale = fbRect.localScale;

                Image bgImage = feedbackBackdropObj.GetComponent<Image>();
                CompactHudMenuController.ApplyRoundedPill(bgImage);
                bgImage.color = new Color(0.031f, 0.067f, 0.118f, 0.92f);
                bgImage.raycastTarget = false;

                Outline bgOutline = feedbackBackdropObj.GetComponent<Outline>();
                bgOutline.effectColor = new Color(0.18f, 0.86f, 1.00f, 0.52f);
                bgOutline.effectDistance = new Vector2(1.4f, -1.4f);
            }
        }

        lastCheckedFeedbackText = null;
        UpdateFeedbackBackdropVisibility();
    }

    private void UpdateFeedbackBackdropVisibility()
    {
        if (feedbackBackdropObj == null || txtFeedback == null)
        {
            return;
        }

        string currentText = txtFeedback.text;
        if (ReferenceEquals(currentText, lastCheckedFeedbackText))
        {
            return;
        }

        lastCheckedFeedbackText = currentText;
        bool hasVisibleText = !string.IsNullOrWhiteSpace(currentText) && txtFeedback.gameObject.activeInHierarchy;
        if (feedbackBackdropObj.activeSelf != hasVisibleText)
        {
            feedbackBackdropObj.SetActive(hasVisibleText);
        }
    }

    private static Button ResolveSceneHudButton(string buttonName, Canvas targetCanvas)
    {
        GameObject directObj = GameObject.Find(buttonName);
        if (directObj != null)
        {
            Button directBtn = directObj.GetComponent<Button>();
            if (directBtn != null)
            {
                return directBtn;
            }
        }

        if (targetCanvas != null)
        {
            Button[] canvasButtons = targetCanvas.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < canvasButtons.Length; i++)
            {
                Button candidate = canvasButtons[i];
                if (candidate != null && string.Equals(candidate.gameObject.name, buttonName, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static void ApplySingleSciFiButtonStyle(
        Button button,
        Color bgColor,
        Color outlineColor,
        Color textColor,
        out Image buttonImage,
        out Outline buttonOutline)
    {
        buttonImage = button.GetComponent<Image>();
        if (buttonImage != null)
        {
            buttonImage.color = bgColor;
            button.targetGraphic = buttonImage;

            buttonOutline = buttonImage.GetComponent<Outline>();
            if (buttonOutline == null)
            {
                buttonOutline = buttonImage.gameObject.AddComponent<Outline>();
            }

            buttonOutline.effectColor = outlineColor;
            buttonOutline.effectDistance = new Vector2(1.8f, -1.8f);
            buttonOutline.useGraphicAlpha = true;
        }
        else
        {
            buttonOutline = null;
        }

        button.transition = Selectable.Transition.ColorTint;
        button.colors = SciFiButtonColorBlock;

        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            float baseMaxSize = Mathf.Max(12f, label.fontSize);
            label.color = textColor;
            label.fontStyle = FontStyles.Bold;
            label.enableAutoSizing = true;
            label.fontSizeMin = 12f;
            label.fontSizeMax = baseMaxSize;
            label.margin = new Vector4(8f, 2f, 8f, 2f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
        }
    }

    private static void ApplyInputToggleVisualState(Image bgImage, Outline borderOutline, TMP_Text labelText, bool isHigh, bool isInputB = false)
    {
        if (bgImage != null)
        {
            bgImage.color = isInputB
                ? (isHigh ? new Color(0.95f, 0.74f, 0.14f, 0.96f) : new Color(0.18f, 0.15f, 0.08f, 0.86f))
                : (isHigh ? InputHighBgColor : InputLowBgColor);
        }

        if (borderOutline != null)
        {
            borderOutline.effectColor = isInputB
                ? (isHigh ? new Color(1.00f, 0.92f, 0.45f, 1.00f) : new Color(0.84f, 0.66f, 0.18f, 0.86f))
                : (isHigh ? InputHighOutlineColor : InputLowOutlineColor);
        }

        if (labelText != null)
        {
            labelText.color = isInputB
                ? (isHigh ? new Color(0.08f, 0.06f, 0.02f, 1.00f) : new Color(0.96f, 0.88f, 0.64f, 1.00f))
                : (isHigh ? InputHighTextColor : InputLowTextColor);
        }
    }

    /// <summary>
    /// Membangun secara prosedural panel samping "Panel_TruthTableScanner" di Canvas (1920x1080)
    /// agar berada rapi di sisi kiri tanpa menabrak Panel_TopHud, Panel_BottomHUD, maupun area tengah kamera AR.
    /// </summary>
    private void InitializeTruthTableMatrixUI()
    {
        if (txtBooleanFormula != null)
        {
            return;
        }

        GameObject existingPanel = truthTableScannerPanelObj != null
            ? truthTableScannerPanelObj
            : GameObject.Find("Panel_TruthTableScanner");

        if (existingPanel != null && existingPanel != truthTableScannerPanelObj)
        {
            truthTableScannerPanelObj = existingPanel;
        }

        Canvas targetCanvas = FindTargetCanvas();
        if (targetCanvas == null)
        {
            return;
        }

        TMP_FontAsset safeFont = ResolveSafeTMPFont();

        if (truthTableScannerPanelObj == null)
        {
            truthTableScannerPanelObj = new GameObject("Panel_TruthTableScanner", typeof(RectTransform), typeof(Image));
            truthTableScannerPanelObj.transform.SetParent(targetCanvas.transform, false);
        }

        RectTransform panelRect = truthTableScannerPanelObj.GetComponent<RectTransform>();
        scannerPanelRect = panelRect;
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(22f, -176f);
        panelRect.sizeDelta = new Vector2(ScannerPanelWidth, ScannerExpandedHeight);

        Image panelImage = truthTableScannerPanelObj.GetComponent<Image>();
        if (panelImage != null)
        {
            panelImage.color = MatrixPanelBgColor;
            panelImage.raycastTarget = false;
        }

        // Garis aksen sian di tepi atas panel
        CreateMatrixPanelAccentBar(panelRect);

        // Header & Rumus Aljabar Boolean Aktif + Tombol Mute Audio
        TMP_Text scannerBadge = CreateMatrixText("Txt_ScannerBadge", panelRect, new Vector2(14f, -10f), new Vector2(308f, 22f),
            "MATRIX TABEL KEBENARAN & RUMUS", 13f, TextAlignmentOptions.Left, MatrixAccentCyanColor, safeFont);
        scannerBadge.enableAutoSizing = true;
        scannerBadge.fontSizeMin = 9.5f;
        scannerBadge.fontSizeMax = 13f;
        scannerBadgeRect = scannerBadge.rectTransform;
        CreateAudioToggleButton(panelRect, safeFont);

        txtBooleanFormula = CreateMatrixText("Txt_BooleanFormula", panelRect, new Vector2(14f, -34f), new Vector2(432f, 36f),
            "Rumus Rakitan: Y = (Belum Terhubung)", 17f, TextAlignmentOptions.Left, Color.white, safeFont);
        txtBooleanFormula.enableAutoSizing = true;
        txtBooleanFormula.fontSizeMin = 12f;
        txtBooleanFormula.fontSizeMax = 18f;

        txtCurrentOutputReadout = CreateMatrixText("Txt_CurrentOutputReadout", panelRect, new Vector2(14f, -72f), new Vector2(432f, 24f),
            "Output Y = - [BELUM TERLACAK]", 15f, TextAlignmentOptions.Left, new Color(1f, 0.72f, 0.25f, 1f), safeFont);

        // Garis pemisah horizontal
        scannerDividerObj = new GameObject("Divider_Scanner", typeof(RectTransform), typeof(Image));
        scannerDividerObj.transform.SetParent(panelRect, false);
        RectTransform divRect = scannerDividerObj.GetComponent<RectTransform>();
        divRect.anchorMin = new Vector2(0f, 1f);
        divRect.anchorMax = new Vector2(0f, 1f);
        divRect.pivot = new Vector2(0f, 1f);
        divRect.anchoredPosition = new Vector2(14f, -100f);
        divRect.sizeDelta = new Vector2(432f, 2f);
        Image divImg = scannerDividerObj.GetComponent<Image>();
        divImg.color = new Color(0.22f, 0.65f, 0.85f, 0.45f);
        divImg.raycastTarget = false;

        // Bar Navigasi Tab & Lipat HUD: [Tabel] | [Osiloskop] | [Lipat/Buka]
        CreateScannerTabBar(panelRect, safeFont);

        // Kontainer Tab 0: Matriks 4 Baris Tabel Kebenaran (default aktif)
        truthTableTabContentObj = new GameObject("Group_TruthTableTab", typeof(RectTransform));
        truthTableTabContentObj.transform.SetParent(panelRect, false);
        RectTransform truthTabRect = truthTableTabContentObj.GetComponent<RectTransform>();
        truthTabRect.anchorMin = new Vector2(0f, 1f);
        truthTabRect.anchorMax = new Vector2(0f, 1f);
        truthTabRect.pivot = new Vector2(0f, 1f);
        truthTabRect.anchoredPosition = Vector2.zero;
        truthTabRect.sizeDelta = new Vector2(ScannerPanelWidth, ScannerExpandedHeight);

        // Baris header kolom: [A] [B] [Target] [Uji Y] [Status]
        RectTransform headerRowRect = CreateMatrixRowContainer("Row_Header", truthTabRect, -141f, 28f, MatrixHeaderBgColor);
        Color headerTextColor = new Color(0.75f, 0.92f, 1f, 1f);
        CreateMatrixColumnText("Hdr_A", headerRowRect, 0f, 46f, 28f, "[A]", 13.5f, headerTextColor, safeFont);
        CreateMatrixColumnText("Hdr_B", headerRowRect, 46f, 46f, 28f, "[B]", 13.5f, headerTextColor, safeFont);
        CreateMatrixColumnText("Hdr_Target", headerRowRect, 92f, 92f, 28f, "[Target]", 13.5f, headerTextColor, safeFont);
        CreateMatrixColumnText("Hdr_Measured", headerRowRect, 184f, 86f, 28f, "[Uji Y]", 13.5f, headerTextColor, safeFont);
        CreateMatrixColumnText("Hdr_Status", headerRowRect, 270f, 162f, 28f, "[Status]", 13.5f, headerTextColor, safeFont);

        // 4 Baris data kombinasi input AB: 00, 01, 10, 11
        for (int i = 0; i < TruthTableRowCount; i++)
        {
            float rowY = -173f - (i * 40f);
            RectTransform rowRect = CreateMatrixRowContainer($"Row_Data_{i}", truthTabRect, rowY, 36f, RowIdleBgColor);
            matrixRowBackgrounds[i] = rowRect.GetComponent<Image>();

            string bitA = (i == 2 || i == 3) ? "1" : "0";
            string bitB = (i == 1 || i == 3) ? "1" : "0";

            CreateMatrixColumnText($"Col_A_{i}", rowRect, 0f, 46f, 36f, bitA, 15.5f, Color.white, safeFont);
            CreateMatrixColumnText($"Col_B_{i}", rowRect, 46f, 46f, 36f, bitB, 15.5f, Color.white, safeFont);
            matrixRowTargetTexts[i] = CreateMatrixColumnText($"Col_Target_{i}", rowRect, 92f, 92f, 36f, "-", 14.5f, MatrixBrightCyanColor, safeFont);
            matrixRowMeasuredTexts[i] = CreateMatrixColumnText($"Col_Measured_{i}", rowRect, 184f, 86f, 36f, "-", 15.5f, Color.white, safeFont);
            matrixRowStatusTexts[i] = CreateMatrixColumnText($"Col_Status_{i}", rowRect, 270f, 162f, 36f, "-", 13.5f, Color.white, safeFont);
        }

        // Kontainer Tab 1: Osiloskop Sinyal Digital Real-Time [A(t) | B(t) | Y(t)] pada slot konten yang sama
        CreateWaveformMonitorUI(panelRect, safeFont);

        // Kontainer Footer: Rating Bintang + Tombol Petunjuk 3 Tingkat + Tombol Rapor CP
        scannerFooterGroupObj = new GameObject("Group_ScannerFooter", typeof(RectTransform));
        scannerFooterGroupObj.transform.SetParent(panelRect, false);
        RectTransform footerGroupRect = scannerFooterGroupObj.GetComponent<RectTransform>();
        footerGroupRect.anchorMin = new Vector2(0f, 1f);
        footerGroupRect.anchorMax = new Vector2(0f, 1f);
        footerGroupRect.pivot = new Vector2(0f, 1f);
        footerGroupRect.anchoredPosition = Vector2.zero;
        footerGroupRect.sizeDelta = new Vector2(ScannerPanelWidth, ScannerExpandedHeight);

        RectTransform starRowRect = CreateMatrixRowContainer("Row_StarMastery", footerGroupRect, -336f, 30f, MatrixHeaderBgColor);
        txtStarMastery = CreateMatrixText("Txt_StarMastery", starRowRect, new Vector2(10f, -2f), new Vector2(412f, 26f),
            "Bintang Level: [...] | Total: 0/21 | Uji ke-0", 13f, TextAlignmentOptions.Left, Color.white, safeFont);
        txtStarMastery.enableAutoSizing = true;
        txtStarMastery.fontSizeMin = 10f;
        txtStarMastery.fontSizeMax = 13.5f;

        CreateScaffoldingHintButton(footerGroupRect, safeFont);
        CreateCompetencyReportButton(footerGroupRect, safeFont);

        // Modal Rapor Kompetensi Informatika Fase E & Pemilih Level Instan (default tersembunyi)
        InitializeCompetencyReportUI(targetCanvas.transform, safeFont);
        InitializeLevelSelectorUI(targetCanvas.transform, safeFont);

        isOscilloscopeTabActive = false;
        isScannerHudCollapsed = false;
        ApplyScannerHudLayoutState();

        RefreshTruthTableTargets();
        ResetTruthTableScanResults();
        ResetWaveformSamples();
        UpdateAudioToggleButtonUI();
        UpdateStarMasteryUI();
        UpdateHintButtonUI();
    }

    private void CreateScannerTabBar(RectTransform panelRect, TMP_FontAsset fontAsset)
    {
        tabTruthTableButton = CreateScannerTabButton(
            "Btn_TabTruthTable", panelRect, new Vector2(14f, -107f), new Vector2(160f, 28f),
            "Tabel", fontAsset, SelectTruthTableTab, out tabTruthTableBackground, out txtTabTruthTableLabel);

        tabOscilloscopeButton = CreateScannerTabButton(
            "Btn_TabOscilloscope", panelRect, new Vector2(180f, -107f), new Vector2(160f, 28f),
            "Osiloskop", fontAsset, SelectOscilloscopeTab, out tabOscilloscopeBackground, out txtTabOscilloscopeLabel);

        collapseHudButton = CreateScannerTabButton(
            "Btn_ToggleCollapseHUD", panelRect, new Vector2(346f, -107f), new Vector2(100f, 28f),
            "Lipat", fontAsset, ToggleCollapseScannerHud, out collapseHudBackground, out txtCollapseHudLabel);
        collapseHudButtonRect = collapseHudButton.GetComponent<RectTransform>();
    }

    private static Button CreateScannerTabButton(
        string objName,
        RectTransform parent,
        Vector2 anchoredPos,
        Vector2 size,
        string labelText,
        TMP_FontAsset fontAsset,
        UnityEngine.Events.UnityAction onClickHandler,
        out Image bgImage,
        out TMP_Text labelTmp)
    {
        GameObject btnObj = new GameObject(objName, typeof(RectTransform), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(parent, false);

        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        bgImage = btnObj.GetComponent<Image>();
        bgImage.color = TabInactiveBgColor;
        bgImage.raycastTarget = true;

        Button btn = btnObj.GetComponent<Button>();
        btn.targetGraphic = bgImage;
        btn.colors = SciFiButtonColorBlock;
        btn.onClick.AddListener(onClickHandler);

        labelTmp = CreateMatrixText($"Txt_{objName}", rect, Vector2.zero, size,
            labelText, 12.5f, TextAlignmentOptions.Center, TabInactiveTextColor, fontAsset);
        labelTmp.enableAutoSizing = true;
        labelTmp.fontSizeMin = 10f;
        labelTmp.fontSizeMax = 13f;
        RectTransform labelRect = labelTmp.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.offsetMin = new Vector2(4f, 0f);
        labelRect.offsetMax = new Vector2(-4f, 0f);

        return btn;
    }

    private void SelectTruthTableTab()
    {
        if (!isOscilloscopeTabActive && !isScannerHudCollapsed)
        {
            return;
        }

        isOscilloscopeTabActive = false;
        isScannerHudCollapsed = false;
        PlaySfx(clipToggleInput);
        ApplyScannerHudLayoutState();
    }

    private void SelectOscilloscopeTab()
    {
        if (isOscilloscopeTabActive && !isScannerHudCollapsed)
        {
            return;
        }

        isOscilloscopeTabActive = true;
        isScannerHudCollapsed = false;
        PlaySfx(clipToggleInput);
        ApplyScannerHudLayoutState();
        RefreshWaveformVisuals();
        UpdateWaveformChannelLabels(lastSampledWaveA, lastSampledWaveB, lastSampledWaveY);
    }

    private void ToggleCollapseScannerHud()
    {
        isScannerHudCollapsed = !isScannerHudCollapsed;
        PlaySfx(clipToggleInput);
        ApplyScannerHudLayoutState();
        if (!isScannerHudCollapsed && isOscilloscopeTabActive)
        {
            RefreshWaveformVisuals();
            UpdateWaveformChannelLabels(lastSampledWaveA, lastSampledWaveB, lastSampledWaveY);
        }
    }

    /// <summary>
    /// Membuka otomatis panel yang sedang terlipat dan memindahkannya ke tab Tabel Kebenaran
    /// saat proses verifikasi 4 baris dimulai agar animasi pemindaian selalu terlihat jelas.
    /// </summary>
    private void EnsureTruthTableVisibleForVerification()
    {
        if (compactHudMenu != null || (!isScannerHudCollapsed && !isOscilloscopeTabActive))
        {
            return;
        }

        isScannerHudCollapsed = false;
        isOscilloscopeTabActive = false;
        ApplyScannerHudLayoutState();
    }

    private void ApplyScannerHudLayoutState()
    {
        if (scannerPanelRect != null)
        {
            scannerPanelRect.sizeDelta = new Vector2(
                ScannerPanelWidth,
                isScannerHudCollapsed ? ScannerCollapsedHeight : ScannerExpandedHeight);
        }

        if (scannerDividerObj != null)
        {
            scannerDividerObj.SetActive(!isScannerHudCollapsed);
        }

        if (tabTruthTableButton != null)
        {
            tabTruthTableButton.gameObject.SetActive(!isScannerHudCollapsed);
        }

        if (tabOscilloscopeButton != null)
        {
            tabOscilloscopeButton.gameObject.SetActive(!isScannerHudCollapsed);
        }

        if (truthTableTabContentObj != null)
        {
            truthTableTabContentObj.SetActive(!isScannerHudCollapsed && !isOscilloscopeTabActive);
        }

        if (waveformMonitorPanelObj != null)
        {
            waveformMonitorPanelObj.SetActive(!isScannerHudCollapsed && isOscilloscopeTabActive);
        }

        if (scannerFooterGroupObj != null)
        {
            scannerFooterGroupObj.SetActive(!isScannerHudCollapsed);
        }

        if (scannerBadgeRect != null)
        {
            scannerBadgeRect.sizeDelta = isScannerHudCollapsed
                ? new Vector2(236f, 22f)
                : new Vector2(308f, 22f);
        }

        if (audioToggleRect != null)
        {
            audioToggleRect.anchoredPosition = isScannerHudCollapsed
                ? new Vector2(254f, -8f)
                : new Vector2(328f, -8f);
            audioToggleRect.sizeDelta = isScannerHudCollapsed
                ? new Vector2(104f, 24f)
                : new Vector2(118f, 24f);
        }

        if (collapseHudButtonRect != null)
        {
            collapseHudButtonRect.anchoredPosition = isScannerHudCollapsed
                ? new Vector2(364f, -8f)
                : new Vector2(346f, -107f);
            collapseHudButtonRect.sizeDelta = isScannerHudCollapsed
                ? new Vector2(82f, 24f)
                : new Vector2(100f, 28f);
        }

        if (txtCollapseHudLabel != null)
        {
            txtCollapseHudLabel.text = isScannerHudCollapsed ? "Buka" : "Lipat";
            txtCollapseHudLabel.color = isScannerHudCollapsed
                ? CollapseBtnCollapsedTextColor
                : CollapseBtnExpandedTextColor;
        }

        if (collapseHudBackground != null)
        {
            collapseHudBackground.color = isScannerHudCollapsed
                ? CollapseBtnCollapsedBgColor
                : CollapseBtnExpandedBgColor;
        }

        if (tabTruthTableBackground != null)
        {
            tabTruthTableBackground.color = !isOscilloscopeTabActive ? TabActiveBgColor : TabInactiveBgColor;
        }

        if (txtTabTruthTableLabel != null)
        {
            txtTabTruthTableLabel.color = !isOscilloscopeTabActive ? TabActiveTextColor : TabInactiveTextColor;
        }

        if (tabOscilloscopeBackground != null)
        {
            tabOscilloscopeBackground.color = isOscilloscopeTabActive ? TabActiveBgColor : TabInactiveBgColor;
        }

        if (txtTabOscilloscopeLabel != null)
        {
            txtTabOscilloscopeLabel.color = isOscilloscopeTabActive ? TabActiveTextColor : TabInactiveTextColor;
        }
    }

    private void CreateWaveformMonitorUI(RectTransform panelRect, TMP_FontAsset fontAsset)
    {
        for (int i = 0; i < WaveformSampleCount; i++)
        {
            waveformBarXPositions[i] = 2f + (i * 21f);
        }

        RectTransform wavePanelRect = CreateMatrixRowContainer("Panel_WaveformMonitor", panelRect, -141f, 188f, MatrixHeaderBgColor);
        waveformMonitorPanelObj = wavePanelRect.gameObject;

        CreateMatrixText("Txt_WaveformHeader", wavePanelRect, new Vector2(8f, -6f), new Vector2(416f, 20f),
            "OSILOSKOP SINYAL DIGITAL [A(t) | B(t) | Y(t)]", 12.5f, TextAlignmentOptions.Left, MatrixAccentCyanColor, fontAsset);

        CreateWaveformChannelRow("Wave_Channel_A", wavePanelRect, -32f, "A(t): 0", WaveHighAColor, fontAsset,
            out txtWaveformLabelA, out waveBarRectsA, out waveBarImagesA);
        CreateWaveformChannelRow("Wave_Channel_B", wavePanelRect, -82f, "B(t): 0", WaveHighBColor, fontAsset,
            out txtWaveformLabelB, out waveBarRectsB, out waveBarImagesB);
        CreateWaveformChannelRow("Wave_Channel_Y", wavePanelRect, -132f, "Y(t): -", MatrixBrightGreenColor, fontAsset,
            out txtWaveformLabelY, out waveBarRectsY, out waveBarImagesY);
    }

    private void CreateWaveformChannelRow(
        string rowName,
        RectTransform parent,
        float topY,
        string initialLabel,
        Color labelColor,
        TMP_FontAsset fontAsset,
        out TMP_Text labelText,
        out RectTransform[] barRects,
        out Image[] barImages)
    {
        GameObject rowObj = new GameObject(rowName, typeof(RectTransform));
        rowObj.transform.SetParent(parent, false);

        RectTransform rowRect = rowObj.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(0f, 1f);
        rowRect.pivot = new Vector2(0f, 1f);
        rowRect.anchoredPosition = new Vector2(0f, topY);
        rowRect.sizeDelta = new Vector2(432f, 44f);

        labelText = CreateMatrixText($"{rowName}_Label", rowRect, new Vector2(8f, -11f), new Vector2(76f, 22f),
            initialLabel, 13.5f, TextAlignmentOptions.Left, labelColor, fontAsset);

        GameObject trackObj = new GameObject($"{rowName}_Track", typeof(RectTransform), typeof(Image));
        trackObj.transform.SetParent(rowRect, false);

        RectTransform trackRect = trackObj.GetComponent<RectTransform>();
        trackRect.anchorMin = new Vector2(0f, 1f);
        trackRect.anchorMax = new Vector2(0f, 1f);
        trackRect.pivot = new Vector2(0f, 1f);
        trackRect.anchoredPosition = new Vector2(88f, -4f);
        trackRect.sizeDelta = new Vector2(336f, 36f);

        Image trackImg = trackObj.GetComponent<Image>();
        trackImg.color = WaveTrackBgColor;
        trackImg.raycastTarget = false;

        barRects = new RectTransform[WaveformSampleCount];
        barImages = new Image[WaveformSampleCount];

        for (int i = 0; i < WaveformSampleCount; i++)
        {
            GameObject barObj = new GameObject($"Pulse_{i}", typeof(RectTransform), typeof(Image));
            barObj.transform.SetParent(trackRect, false);

            RectTransform barRect = barObj.GetComponent<RectTransform>();
            barRect.anchorMin = Vector2.zero;
            barRect.anchorMax = Vector2.zero;
            barRect.pivot = Vector2.zero;
            barRect.anchoredPosition = new Vector2(waveformBarXPositions[i], 4f);
            barRect.sizeDelta = new Vector2(17f, 3.5f);

            Image barImg = barObj.GetComponent<Image>();
            barImg.color = WaveUntrackedYColor;
            barImg.raycastTarget = false;

            barRects[i] = barRect;
            barImages[i] = barImg;
        }
    }

    private void ResetWaveformSamples()
    {
        int initA = (inputANode != null && inputANode.currentOutputValue) ? 1 : 0;
        int initB = (inputBNode != null && inputBNode.currentOutputValue) ? 1 : 0;
        int initY = (outputNode != null && outputNode.isTracked) ? (outputNode.currentOutputValue ? 1 : 0) : -1;

        for (int i = 0; i < WaveformSampleCount; i++)
        {
            sampleA[i] = initA;
            sampleB[i] = initB;
            sampleY[i] = initY;
        }

        waveformSampleTimer = 0f;
        waveformBaselineInitialized = true;
        lastSampledWaveA = initA;
        lastSampledWaveB = initB;
        lastSampledWaveY = initY;
        renderedWaveLabelA = -2;
        renderedWaveLabelB = -2;
        renderedWaveLabelY = -2;

        RefreshWaveformVisuals();
        UpdateWaveformChannelLabels(initA, initB, initY);
    }

    private void UpdateWaveformMonitorLive()
    {
        if (waveBarRectsA == null || isVerifying || truthTableScannerPanelObj == null || !truthTableScannerPanelObj.activeInHierarchy)
        {
            return;
        }

        int curA = (inputANode != null && inputANode.currentOutputValue) ? 1 : 0;
        int curB = (inputBNode != null && inputBNode.currentOutputValue) ? 1 : 0;
        int curY = (outputNode != null && outputNode.isTracked) ? (outputNode.currentOutputValue ? 1 : 0) : -1;

        waveformSampleTimer += Time.unscaledDeltaTime;

        bool stateChanged = !waveformBaselineInitialized
                            || curA != lastSampledWaveA
                            || curB != lastSampledWaveB
                            || curY != lastSampledWaveY;

        if (stateChanged || waveformSampleTimer >= WaveformSampleIntervalSeconds)
        {
            RecordWaveformSample(curA, curB, curY);
            waveformSampleTimer = 0f;
        }
    }

    private void SampleWaveformFromCurrentNodes()
    {
        int curA = (inputANode != null && inputANode.currentOutputValue) ? 1 : 0;
        int curB = (inputBNode != null && inputBNode.currentOutputValue) ? 1 : 0;
        int curY = (outputNode != null && outputNode.isTracked) ? (outputNode.currentOutputValue ? 1 : 0) : -1;
        waveformSampleTimer = 0f;
        RecordWaveformSample(curA, curB, curY);
    }

    private void RecordWaveformSample(int valA, int valB, int valY)
    {
        for (int i = 0; i < WaveformSampleCount - 1; i++)
        {
            sampleA[i] = sampleA[i + 1];
            sampleB[i] = sampleB[i + 1];
            sampleY[i] = sampleY[i + 1];
        }

        sampleA[WaveformSampleCount - 1] = valA;
        sampleB[WaveformSampleCount - 1] = valB;
        sampleY[WaveformSampleCount - 1] = valY;

        waveformBaselineInitialized = true;
        lastSampledWaveA = valA;
        lastSampledWaveB = valB;
        lastSampledWaveY = valY;

        RefreshWaveformVisuals();
        UpdateWaveformChannelLabels(valA, valB, valY);
    }

    private void RefreshWaveformVisuals()
    {
        UpdateChannelBars(sampleA, waveBarRectsA, waveBarImagesA, WaveHighAColor, WaveLowAColor);
        UpdateChannelBars(sampleB, waveBarRectsB, waveBarImagesB, WaveHighBColor, WaveLowBColor);
        UpdateChannelBars(sampleY, waveBarRectsY, waveBarImagesY, WaveHighYColor, WaveLowYColor);
    }

    private void UpdateChannelBars(int[] samples, RectTransform[] rects, Image[] images, Color highColor, Color lowColor)
    {
        if (rects == null || images == null)
        {
            return;
        }

        for (int i = 0; i < WaveformSampleCount; i++)
        {
            RectTransform barRect = rects[i];
            Image barImg = images[i];
            if (barRect == null || barImg == null)
            {
                continue;
            }

            int val = samples[i];
            if (val > 0)
            {
                barRect.anchoredPosition = new Vector2(waveformBarXPositions[i], 12f);
                barRect.sizeDelta = new Vector2(17f, 20f);
                barImg.color = highColor;
            }
            else if (val == 0)
            {
                barRect.anchoredPosition = new Vector2(waveformBarXPositions[i], 4f);
                barRect.sizeDelta = new Vector2(17f, 5f);
                barImg.color = lowColor;
            }
            else
            {
                barRect.anchoredPosition = new Vector2(waveformBarXPositions[i], 16f);
                barRect.sizeDelta = new Vector2(17f, 4f);
                barImg.color = WaveUntrackedYColor;
            }
        }
    }

    private void UpdateWaveformChannelLabels(int valA, int valB, int valY)
    {
        if (txtWaveformLabelA != null && valA != renderedWaveLabelA)
        {
            renderedWaveLabelA = valA;
            txtWaveformLabelA.text = valA > 0 ? "A(t): 1" : "A(t): 0";
            txtWaveformLabelA.color = valA > 0 ? WaveHighAColor : new Color(0.55f, 0.72f, 0.85f, 1f);
        }

        if (txtWaveformLabelB != null && valB != renderedWaveLabelB)
        {
            renderedWaveLabelB = valB;
            txtWaveformLabelB.text = valB > 0 ? "B(t): 1" : "B(t): 0";
            txtWaveformLabelB.color = valB > 0 ? WaveHighBColor : new Color(0.75f, 0.68f, 0.42f, 1f);
        }

        if (txtWaveformLabelY != null && valY != renderedWaveLabelY)
        {
            renderedWaveLabelY = valY;
            if (valY > 0)
            {
                txtWaveformLabelY.text = "Y(t): 1";
                txtWaveformLabelY.color = WaveHighYColor;
            }
            else if (valY == 0)
            {
                txtWaveformLabelY.text = "Y(t): 0";
                txtWaveformLabelY.color = MatrixBrightRedColor;
            }
            else
            {
                txtWaveformLabelY.text = "Y(t): -";
                txtWaveformLabelY.color = new Color(0.70f, 0.76f, 0.84f, 1f);
            }
        }
    }

    private void CreateScaffoldingHintButton(RectTransform parent, TMP_FontAsset fontAsset)
    {
        GameObject btnObj = new GameObject("Btn_ScaffoldingHint", typeof(RectTransform), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(parent, false);

        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(14f, -372f);
        rect.sizeDelta = new Vector2(292f, 34f);

        hintButtonBackground = btnObj.GetComponent<Image>();
        hintButtonBackground.raycastTarget = true;

        hintButton = btnObj.GetComponent<Button>();
        hintButton.targetGraphic = hintButtonBackground;
        hintButton.onClick.AddListener(OnHintButtonClicked);

        txtHintButtonLabel = CreateMatrixText("Txt_ScaffoldingHint", rect, Vector2.zero, new Vector2(292f, 34f),
            "Petunjuk (0/3)", 13f, TextAlignmentOptions.Center, Color.white, fontAsset);
        txtHintButtonLabel.enableAutoSizing = true;
        txtHintButtonLabel.fontSizeMin = 10f;
        txtHintButtonLabel.fontSizeMax = 13.5f;
        RectTransform labelRect = txtHintButtonLabel.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.offsetMin = new Vector2(6f, 0f);
        labelRect.offsetMax = new Vector2(-6f, 0f);
    }

    private void CreateCompetencyReportButton(RectTransform parent, TMP_FontAsset fontAsset)
    {
        GameObject btnObj = new GameObject("Btn_OpenCompetencyReport", typeof(RectTransform), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(parent, false);

        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(314f, -372f);
        rect.sizeDelta = new Vector2(132f, 34f);

        Image btnBg = btnObj.GetComponent<Image>();
        btnBg.color = new Color(0.10f, 0.34f, 0.42f, 0.96f);
        btnBg.raycastTarget = true;

        reportOpenButton = btnObj.GetComponent<Button>();
        reportOpenButton.targetGraphic = btnBg;
        reportOpenButton.onClick.AddListener(ToggleCompetencyReport);

        TMP_Text btnLabel = CreateMatrixText("Txt_OpenCompetencyReport", rect, Vector2.zero, new Vector2(132f, 34f),
            "Rapor CP", 13f, TextAlignmentOptions.Center, new Color(0.75f, 0.98f, 1f, 1f), fontAsset);
        RectTransform labelRect = btnLabel.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
    }

    private void InitializeCompetencyReportUI(Transform canvasTransform, TMP_FontAsset fontAsset)
    {
        if (competencyReportPanelObj != null || canvasTransform == null)
        {
            return;
        }

        competencyReportPanelObj = new GameObject("Panel_CompetencyReport", typeof(RectTransform), typeof(Image));
        competencyReportPanelObj.transform.SetParent(canvasTransform, false);
        competencyReportPanelObj.transform.SetAsLastSibling();

        RectTransform modalRect = competencyReportPanelObj.GetComponent<RectTransform>();
        modalRect.anchorMin = new Vector2(0.5f, 0.5f);
        modalRect.anchorMax = new Vector2(0.5f, 0.5f);
        modalRect.pivot = new Vector2(0.5f, 0.5f);
        modalRect.anchoredPosition = Vector2.zero;
        modalRect.sizeDelta = new Vector2(760f, 520f);

        Image modalBg = competencyReportPanelObj.GetComponent<Image>();
        modalBg.color = new Color(0.04f, 0.08f, 0.14f, 0.96f);
        modalBg.raycastTarget = true;

        // Bingkai sian 4 sisi
        CreateReportBorderStrip("Border_Top", modalRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 3f));
        CreateReportBorderStrip("Border_Bottom", modalRect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 3f));
        CreateReportBorderStrip("Border_Left", modalRect, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(3f, 0f));
        CreateReportBorderStrip("Border_Right", modalRect, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(3f, 0f));

        TMP_Text headerTitle = CreateMatrixText("Txt_ReportHeader", modalRect, new Vector2(20f, -14f), new Vector2(720f, 30f),
            "RAPOR KOMPETENSI INFORMATIKA FASE E - SISTEM KOMPUTER", 18f, TextAlignmentOptions.Center, MatrixAccentCyanColor, fontAsset);
        headerTitle.enableAutoSizing = true;
        headerTitle.fontSizeMin = 13f;
        headerTitle.fontSizeMax = 18f;

        RectTransform topDivRect = CreateModalContainer("Divider_ReportTop", modalRect, 20f, -48f, 720f, 2f, new Color(0.22f, 0.65f, 0.85f, 0.55f));
        topDivRect.GetComponent<Image>().raycastTarget = false;

        RectTransform summaryRect = CreateModalContainer("Panel_ReportSummary", modalRect, 20f, -56f, 720f, 54f, MatrixHeaderBgColor);
        summaryRect.GetComponent<Image>().raycastTarget = false;

        txtReportSummaryStats = CreateMatrixText("Txt_ReportSummaryStats", summaryRect, new Vector2(12f, -4f), new Vector2(696f, 22f),
            "Total Bintang: 0 / 21  |  Misi Tuntas: 0 / 7", 15f, TextAlignmentOptions.Center, Color.white, fontAsset);

        txtReportPredicate = CreateMatrixText("Txt_ReportPredicate", summaryRect, new Vector2(12f, -28f), new Vector2(696f, 22f),
            "PREDIKAT: KADET LOGIKA DIGITAL (PEMULA)", 14f, TextAlignmentOptions.Center, MatrixBrightCyanColor, fontAsset);
        txtReportPredicate.enableAutoSizing = true;
        txtReportPredicate.fontSizeMin = 11f;
        txtReportPredicate.fontSizeMax = 14.5f;

        int rowCount = Mathf.Min(7, (levels != null && levels.Count > 0) ? levels.Count : 7);
        reportRowBackgrounds = new Image[rowCount];
        reportRowTitleTexts = new TMP_Text[rowCount];
        reportRowStarsTexts = new TMP_Text[rowCount];
        reportRowStatusTexts = new TMP_Text[rowCount];

        for (int i = 0; i < rowCount; i++)
        {
            float rowY = -120f - (i * 43f);
            RectTransform rowRect = CreateModalContainer($"Row_ReportLevel_{i}", modalRect, 20f, rowY, 720f, 38f, RowIdleBgColor);
            Image rowBg = rowRect.GetComponent<Image>();
            rowBg.raycastTarget = false;
            reportRowBackgrounds[i] = rowBg;

            reportRowTitleTexts[i] = CreateMatrixText($"Txt_ReportTitle_{i}", rowRect, new Vector2(14f, -2f), new Vector2(430f, 34f),
                $"Level {i + 1}", 14f, TextAlignmentOptions.Left, Color.white, fontAsset);
            reportRowTitleTexts[i].enableAutoSizing = true;
            reportRowTitleTexts[i].fontSizeMin = 11f;
            reportRowTitleTexts[i].fontSizeMax = 14.5f;

            reportRowStarsTexts[i] = CreateMatrixText($"Txt_ReportStars_{i}", rowRect, new Vector2(454f, -2f), new Vector2(120f, 34f),
                "[...]", 15f, TextAlignmentOptions.Center, Color.white, fontAsset);

            reportRowStatusTexts[i] = CreateMatrixText($"Txt_ReportStatus_{i}", rowRect, new Vector2(584f, -2f), new Vector2(122f, 34f),
                "BELUM", 14f, TextAlignmentOptions.Center, new Color(0.95f, 0.68f, 0.35f, 1f), fontAsset);
        }

        RectTransform botDivRect = CreateModalContainer("Divider_ReportBottom", modalRect, 20f, -430f, 720f, 2f, new Color(0.22f, 0.65f, 0.85f, 0.55f));
        botDivRect.GetComponent<Image>().raycastTarget = false;

        reportCloseButton = CreateReportModalButton("Btn_ReportClose", modalRect, 20f, -444f, 228f, 50f,
            "Tutup Rapor", new Color(0.14f, 0.30f, 0.44f, 1f), Color.white, fontAsset, CloseCompetencyReport);

        reportSandboxButton = CreateReportModalButton("Btn_ReportSandbox", modalRect, 266f, -444f, 228f, 50f,
            "Mode Sandbox", new Color(0.10f, 0.38f, 0.36f, 1f), new Color(0.78f, 1.00f, 0.94f, 1f), fontAsset, OnReportSandboxClicked);

        reportResetButton = CreateReportModalButton("Btn_ReportReset", modalRect, 512f, -444f, 228f, 50f,
            "Reset Progres (0/21)", new Color(0.42f, 0.14f, 0.14f, 1f), new Color(1.00f, 0.84f, 0.82f, 1f), fontAsset, OnReportResetProgressClicked);

        competencyReportPanelObj.SetActive(false);
        RefreshCompetencyReportUI();
    }

    private static void CreateReportBorderStrip(
        string name,
        RectTransform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPos,
        Vector2 sizeDelta)
    {
        GameObject strip = new GameObject(name, typeof(RectTransform), typeof(Image));
        strip.transform.SetParent(parent, false);

        RectTransform rect = strip.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = sizeDelta;

        Image img = strip.GetComponent<Image>();
        img.color = MatrixAccentCyanColor;
        img.raycastTarget = false;
    }

    private static RectTransform CreateModalContainer(string name, RectTransform parent, float leftX, float topY, float width, float height, Color bgColor)
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
        img.color = bgColor;
        return rect;
    }

    private static Button CreateReportModalButton(
        string name,
        RectTransform parent,
        float leftX,
        float topY,
        float width,
        float height,
        string label,
        Color bgColor,
        Color textColor,
        TMP_FontAsset fontAsset,
        UnityEngine.Events.UnityAction onClickAction)
    {
        RectTransform btnRect = CreateModalContainer(name, parent, leftX, topY, width, height, bgColor);
        Image img = btnRect.GetComponent<Image>();
        img.raycastTarget = true;

        Button btn = btnRect.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClickAction != null)
        {
            btn.onClick.AddListener(onClickAction);
        }

        TMP_Text btnLabel = CreateMatrixText($"{name}_Label", btnRect, Vector2.zero, new Vector2(width, height),
            label, 14.5f, TextAlignmentOptions.Center, textColor, fontAsset);
        btnLabel.enableAutoSizing = true;
        btnLabel.fontSizeMin = 11f;
        btnLabel.fontSizeMax = 15f;
        RectTransform labelRect = btnLabel.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.offsetMin = new Vector2(6f, 0f);
        labelRect.offsetMax = new Vector2(-6f, 0f);

        return btn;
    }

    /// <summary>
    /// Membuka atau menutup modal Rapor Kompetensi Informatika Fase E dari tombol "Rapor CP".
    /// </summary>
    public void ToggleCompetencyReport()
    {
        PlaySfx(clipToggleInput);

        if (competencyReportPanelObj == null)
        {
            return;
        }

        if (competencyReportPanelObj.activeSelf)
        {
            CloseCompetencyReport();
        }
        else
        {
            OpenCompetencyReport();
        }
    }

    public void OpenCompetencyReport()
    {
        if (competencyReportPanelObj == null)
        {
            return;
        }

        compactHudMenu?.CloseMenuDrawer();
        CloseLevelSelector(playAudio: false);
        RefreshCompetencyReportUI();
        competencyReportPanelObj.transform.SetAsLastSibling();
        competencyReportPanelObj.SetActive(true);
    }

    public void CloseCompetencyReport()
    {
        if (competencyReportPanelObj != null && competencyReportPanelObj.activeSelf)
        {
            competencyReportPanelObj.SetActive(false);
        }
    }

    /// <summary>
    /// Membangun tombol "Pilih Level" (tepat di bawah Btn_ResetLevel) dan modal "Panel_LevelSelector"
    /// bergrid 2x4 untuk lompat instan ke Level 1-7 maupun Mode Sandbox Bebas.
    /// </summary>
    private void InitializeLevelSelectorUI(Transform canvasTransform, TMP_FontAsset fontAsset)
    {
        if (levelSelectorModalPanel != null || canvasTransform == null)
        {
            return;
        }

        CreateLevelSelectorOpenButton(canvasTransform, fontAsset);

        levelSelectorModalPanel = new GameObject("Panel_LevelSelector", typeof(RectTransform), typeof(Image), typeof(Outline));
        levelSelectorModalPanel.transform.SetParent(canvasTransform, false);
        levelSelectorModalPanel.transform.SetAsLastSibling();

        RectTransform modalRect = levelSelectorModalPanel.GetComponent<RectTransform>();
        modalRect.anchorMin = new Vector2(0.5f, 0.5f);
        modalRect.anchorMax = new Vector2(0.5f, 0.5f);
        modalRect.pivot = new Vector2(0.5f, 0.5f);
        modalRect.anchoredPosition = new Vector2(0f, -14f);
        modalRect.sizeDelta = new Vector2(920f, 596f);

        Image modalBg = levelSelectorModalPanel.GetComponent<Image>();
        CompactHudMenuController.ApplyRoundedCard(modalBg);
        modalBg.color = new Color(0.031f, 0.067f, 0.118f, 0.96f);
        modalBg.raycastTarget = true;

        Outline modalOutline = levelSelectorModalPanel.GetComponent<Outline>();
        modalOutline.effectColor = new Color(0.18f, 0.88f, 1.00f, 0.42f);
        modalOutline.effectDistance = new Vector2(2.2f, -2.2f);
        modalOutline.useGraphicAlpha = true;

        CompactHudMenuController.AttachRoundedBorderOverlay(
            modalRect,
            "Panel_LevelSelector_Border",
            CompactHudMenuController.GetRoundedCardBorderSprite(),
            MatrixAccentCyanColor);

        TMP_Text headerTitle = CreateMatrixText("Txt_LevelSelectorHeader", modalRect, new Vector2(28f, -16f), new Vector2(864f, 40f),
            "PILIH MISI & MODE SIMULASI (LEVEL 1 - 7 & SANDBOX)", 23.5f, TextAlignmentOptions.Center, MatrixAccentCyanColor, fontAsset);
        headerTitle.enableAutoSizing = true;
        headerTitle.fontSizeMin = 17f;
        headerTitle.fontSizeMax = 24f;

        RectTransform topDivRect = CreateModalContainer("Divider_LevelSelectorTop", modalRect, 28f, -62f, 864f, 2f, new Color(0.22f, 0.65f, 0.85f, 0.48f));
        topDivRect.GetComponent<Image>().raycastTarget = false;

        int levelCount = Mathf.Min(7, (levels != null && levels.Count > 0) ? levels.Count : 7);
        levelSelectorItemButtons = new Button[levelCount];
        levelSelectorItemHandlers = new UnityEngine.Events.UnityAction[levelCount];
        levelSelectorItemBackgrounds = new Image[levelCount];
        levelSelectorItemOutlines = new Outline[levelCount];
        levelSelectorItemStatusTexts = new TMP_Text[levelCount];

        const float cardWidth = 420f;
        const float cardHeight = 92f;
        const float colLeftX = 28f;
        const float colRightX = 472f;
        const float gridTopY = -78f;
        const float rowStepY = 104f;

        for (int i = 0; i < levelCount; i++)
        {
            int col = i % 2;
            int row = i / 2;
            float xPos = col == 0 ? colLeftX : colRightX;
            float yPos = gridTopY - (row * rowStepY);

            RectTransform cardRect = CreateModalContainer($"Btn_SelectLevel_{i + 1}", modalRect, xPos, yPos, cardWidth, cardHeight, LevelSelectorCardIdleBgColor);
            Image cardBg = cardRect.GetComponent<Image>();
            CompactHudMenuController.ApplyRoundedSquircle(cardBg);
            cardBg.raycastTarget = true;

            Button cardBtn = cardRect.gameObject.AddComponent<Button>();
            cardBtn.targetGraphic = cardBg;
            cardBtn.transition = Selectable.Transition.ColorTint;
            cardBtn.colors = SciFiButtonColorBlock;

            Outline cardOutline = cardRect.gameObject.AddComponent<Outline>();
            cardOutline.effectColor = LevelSelectorCardIdleOutlineColor;
            cardOutline.effectDistance = new Vector2(1.8f, -1.8f);
            cardOutline.useGraphicAlpha = true;

            int capturedIndex = i;
            UnityEngine.Events.UnityAction handler = () => OnLevelSelectorItemClicked(capturedIndex);
            cardBtn.onClick.AddListener(handler);

            TMP_Text cardTitle = CreateMatrixText($"Txt_SelectLevelTitle_{i + 1}", cardRect, new Vector2(18f, -10f), new Vector2(cardWidth - 36f, 36f),
                GetLevelSelectorCardTitle(i), 20f, TextAlignmentOptions.Left, Color.white, fontAsset);
            cardTitle.enableAutoSizing = true;
            cardTitle.fontSizeMin = 15.5f;
            cardTitle.fontSizeMax = 20.5f;

            TMP_Text cardStatus = CreateMatrixText($"Txt_SelectLevelStatus_{i + 1}", cardRect, new Vector2(18f, -50f), new Vector2(cardWidth - 36f, 30f),
                "Bintang: [...] | SIAP", 17f, TextAlignmentOptions.Left, MatrixBrightCyanColor, fontAsset);
            cardStatus.enableAutoSizing = true;
            cardStatus.fontSizeMin = 14f;
            cardStatus.fontSizeMax = 17.5f;

            levelSelectorItemButtons[i] = cardBtn;
            levelSelectorItemHandlers[i] = handler;
            levelSelectorItemBackgrounds[i] = cardBg;
            levelSelectorItemOutlines[i] = cardOutline;
            levelSelectorItemStatusTexts[i] = cardStatus;
        }

        // Slot ke-8 (Baris 4 Kolom Kanan): Mode Sandbox Bebas
        RectTransform sandboxRect = CreateModalContainer("Btn_SelectSandbox", modalRect, colRightX, gridTopY - (3 * rowStepY), cardWidth, cardHeight, LevelSelectorSandboxIdleBgColor);
        levelSelectorSandboxBg = sandboxRect.GetComponent<Image>();
        CompactHudMenuController.ApplyRoundedSquircle(levelSelectorSandboxBg);
        levelSelectorSandboxBg.raycastTarget = true;

        levelSelectorSandboxButton = sandboxRect.gameObject.AddComponent<Button>();
        levelSelectorSandboxButton.targetGraphic = levelSelectorSandboxBg;
        levelSelectorSandboxButton.transition = Selectable.Transition.ColorTint;
        levelSelectorSandboxButton.colors = SciFiButtonColorBlock;
        levelSelectorSandboxButton.onClick.AddListener(OnLevelSelectorSandboxClicked);

        levelSelectorSandboxOutline = sandboxRect.gameObject.AddComponent<Outline>();
        levelSelectorSandboxOutline.effectColor = LevelSelectorSandboxOutlineColor;
        levelSelectorSandboxOutline.effectDistance = new Vector2(1.8f, -1.8f);
        levelSelectorSandboxOutline.useGraphicAlpha = true;

        TMP_Text sandboxTitle = CreateMatrixText("Txt_SelectSandboxTitle", sandboxRect, new Vector2(18f, -10f), new Vector2(cardWidth - 36f, 36f),
            "MODE SANDBOX BEBAS", 20f, TextAlignmentOptions.Left, new Color(0.84f, 1.00f, 0.94f, 1f), fontAsset);
        sandboxTitle.enableAutoSizing = true;
        sandboxTitle.fontSizeMin = 15.5f;
        sandboxTitle.fontSizeMax = 20.5f;

        txtLevelSelectorSandboxStatus = CreateMatrixText("Txt_SelectSandboxStatus", sandboxRect, new Vector2(18f, -50f), new Vector2(cardWidth - 36f, 30f),
            "Eksplorasi Bebas Tanpa Batas Kuota", 16.5f, TextAlignmentOptions.Left, MatrixBrightCyanColor, fontAsset);
        txtLevelSelectorSandboxStatus.enableAutoSizing = true;
        txtLevelSelectorSandboxStatus.fontSizeMin = 13.5f;
        txtLevelSelectorSandboxStatus.fontSizeMax = 17f;

        RectTransform botDivRect = CreateModalContainer("Divider_LevelSelectorBottom", modalRect, 28f, -500f, 864f, 2f, new Color(0.22f, 0.65f, 0.85f, 0.48f));
        botDivRect.GetComponent<Image>().raycastTarget = false;

        levelSelectorCloseButton = CreateReportModalButton("Btn_LevelSelectorClose", modalRect, 310f, -516f, 300f, 58f,
            "Tutup", new Color(0.11f, 0.28f, 0.42f, 0.98f), Color.white, fontAsset, CloseLevelSelectorWithAudio);
        Image closeBtnImg = levelSelectorCloseButton.GetComponent<Image>();
        CompactHudMenuController.ApplyRoundedSquircle(closeBtnImg);
        TMP_Text closeBtnTmp = levelSelectorCloseButton.GetComponentInChildren<TMP_Text>(true);
        if (closeBtnTmp != null)
        {
            closeBtnTmp.fontSizeMin = 16f;
            closeBtnTmp.fontSizeMax = 21f;
        }
        levelSelectorCloseButton.colors = SciFiButtonColorBlock;
        Outline closeOutline = levelSelectorCloseButton.gameObject.AddComponent<Outline>();
        closeOutline.effectColor = MatrixAccentCyanColor;
        closeOutline.effectDistance = new Vector2(1.8f, -1.8f);
        closeOutline.useGraphicAlpha = true;

        levelSelectorModalPanel.SetActive(false);
        RefreshLevelSelectorUI();
    }

    private void CreateLevelSelectorOpenButton(Transform canvasTransform, TMP_FontAsset fontAsset)
    {
        if (levelSelectorOpenButton != null)
        {
            return;
        }

        Transform parentTransform = canvasTransform;
        Vector2 anchorMin = new Vector2(0.5f, 0.5f);
        Vector2 anchorMax = new Vector2(0.5f, 0.5f);
        Vector2 pivot = new Vector2(0.5f, 0.5f);
        Vector2 anchoredPosition = new Vector2(800f, 295f);
        Vector2 sizeDelta = new Vector2(220f, 56f);

        RectTransform resetRect = null;
        if (resetLevelButtonObj != null)
        {
            resetRect = resetLevelButtonObj.GetComponent<RectTransform>();
        }
        else if (btnResetLevel != null)
        {
            resetRect = btnResetLevel.GetComponent<RectTransform>();
        }

        if (resetRect != null)
        {
            if (resetRect.parent != null)
            {
                parentTransform = resetRect.parent;
            }

            anchorMin = resetRect.anchorMin;
            anchorMax = resetRect.anchorMax;
            pivot = resetRect.pivot;
            float width = resetRect.sizeDelta.x > 10f ? resetRect.sizeDelta.x : 220f;
            float height = 56f;
            const float verticalGap = 14f;
            float yBelowReset = resetRect.anchoredPosition.y - (resetRect.sizeDelta.y * 0.5f) - verticalGap - (height * 0.5f);
            anchoredPosition = new Vector2(resetRect.anchoredPosition.x, yBelowReset);
            sizeDelta = new Vector2(width, height);
        }

        GameObject btnObj = new GameObject("Btn_OpenLevelSelector", typeof(RectTransform), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(parentTransform, false);

        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        TMP_Text btnLabel = CreateMatrixText("Txt_OpenLevelSelector", rect, Vector2.zero, sizeDelta,
            "Pilih Level", 22f, TextAlignmentOptions.Center, Color.white, fontAsset);
        btnLabel.enableAutoSizing = true;
        btnLabel.fontSizeMin = 15f;
        btnLabel.fontSizeMax = 24f;
        RectTransform labelRect = btnLabel.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.offsetMin = new Vector2(8f, 0f);
        labelRect.offsetMax = new Vector2(-8f, 0f);

        levelSelectorOpenButton = btnObj.GetComponent<Button>();
        ApplySingleSciFiButtonStyle(
            levelSelectorOpenButton,
            LevelSelectorOpenBgColor,
            MatrixAccentCyanColor,
            new Color(0.86f, 0.98f, 1.00f, 1f),
            out _,
            out _);
        levelSelectorOpenButton.onClick.AddListener(ToggleLevelSelector);

        CreateAirWeavingModeToggleButton(canvasTransform, fontAsset);
    }

    private void CreateAirWeavingModeToggleButton(Transform canvasTransform, TMP_FontAsset fontAsset)
    {
        if (airWeavingToggleButton != null)
        {
            return;
        }

        Transform parentTransform = canvasTransform;
        Vector2 anchorMin = new Vector2(0.5f, 0.5f);
        Vector2 anchorMax = new Vector2(0.5f, 0.5f);
        Vector2 pivot = new Vector2(0.5f, 0.5f);
        Vector2 anchoredPosition = new Vector2(800f, 225f);
        Vector2 sizeDelta = new Vector2(220f, 56f);

        RectTransform levelBtnRect = levelSelectorOpenButton != null
            ? levelSelectorOpenButton.GetComponent<RectTransform>()
            : null;
        if (levelBtnRect != null)
        {
            if (levelBtnRect.parent != null)
            {
                parentTransform = levelBtnRect.parent;
            }

            anchorMin = levelBtnRect.anchorMin;
            anchorMax = levelBtnRect.anchorMax;
            pivot = levelBtnRect.pivot;
            float width = levelBtnRect.sizeDelta.x > 10f ? levelBtnRect.sizeDelta.x : 220f;
            float height = 56f;
            const float verticalGap = 14f;
            float yBelowLevelBtn = levelBtnRect.anchoredPosition.y - (levelBtnRect.sizeDelta.y * 0.5f) - verticalGap - (height * 0.5f);
            anchoredPosition = new Vector2(levelBtnRect.anchoredPosition.x, yBelowLevelBtn);
            sizeDelta = new Vector2(width, height);
        }

        GameObject btnObj = new GameObject("Btn_ToggleAirWeaving", typeof(RectTransform), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(parentTransform, false);

        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        txtAirWeavingToggleLabel = CreateMatrixText("Txt_ToggleAirWeaving", rect, Vector2.zero, sizeDelta,
            "Mode: Udara (v6)", 20f, TextAlignmentOptions.Center, Color.white, fontAsset);
        txtAirWeavingToggleLabel.enableAutoSizing = true;
        txtAirWeavingToggleLabel.fontSizeMin = 13f;
        txtAirWeavingToggleLabel.fontSizeMax = 21f;
        RectTransform labelRect = txtAirWeavingToggleLabel.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.offsetMin = new Vector2(8f, 0f);
        labelRect.offsetMax = new Vector2(-8f, 0f);

        airWeavingToggleButton = btnObj.GetComponent<Button>();
        ApplySingleSciFiButtonStyle(
            airWeavingToggleButton,
            LevelSelectorOpenBgColor,
            MatrixAccentCyanColor,
            new Color(0.86f, 0.98f, 1.00f, 1f),
            out airWeavingToggleBg,
            out airWeavingToggleOutline);
        airWeavingToggleButton.onClick.AddListener(OnToggleAirWeavingClicked);
    }

    private void InitializeCompactHudMenu()
    {
        if (compactHudMenu == null) compactHudMenu = GetComponent<CompactHudMenuController>() ?? gameObject.AddComponent<CompactHudMenuController>();
        compactHudMenu.Initialize(this, FindTargetCanvas(), ResolveSafeTMPFont(), txtLevelTitle, txtMissionDesc, txtQuotaStatus, txtFeedback,
            feedbackBackdropObj, truthTableScannerPanelObj, resetLevelButtonObj,
            levelSelectorOpenButton != null ? levelSelectorOpenButton.gameObject : null,
            airWeavingToggleButton != null ? airWeavingToggleButton.gameObject : null, btnInputA, btnInputB, btnVerify, btnNextLevel);
    }

    private void EnsureAirWeavingControllerBootstrapped()
    {
        if (airWeavingController == null)
        {
            airWeavingController = FindAnyObjectByType<AirWeavingController>();
        }

        if (airWeavingController == null)
        {
            GameObject controllerObj = new GameObject("AirWeavingController");
            airWeavingController = controllerObj.AddComponent<AirWeavingController>();
        }

        airWeavingController.Configure(this, circuitManager, inputANode, inputBNode, outputNode, txtFeedback);
        airWeavingController.OnAirWeavingModeChanged -= HandleAirWeavingModeChanged;
        airWeavingController.OnAirWeavingModeChanged += HandleAirWeavingModeChanged;
        airWeavingController.OnOutputVerifyRequested -= OnVerifyButtonClicked;
        airWeavingController.OnOutputVerifyRequested += OnVerifyButtonClicked;
        airWeavingController.OnOutputNextLevelRequested -= NextLevel;
        airWeavingController.OnOutputNextLevelRequested += NextLevel;
        HandleAirWeavingModeChanged(airWeavingController.IsAirWeavingActive);
    }

    private void OnToggleAirWeavingClicked()
    {
        if (isVerifying)
        {
            PlaySfx(clipToggleInput);
            if (txtFeedback != null)
            {
                txtFeedback.text = "Tunggu hingga proses Uji Rangkaian selesai sebelum berganti mode kamera.";
                txtFeedback.color = new Color(1.00f, 0.78f, 0.28f, 1f);
            }
            return;
        }

        EnsureAirWeavingControllerBootstrapped();
        if (airWeavingController == null || airWeavingController.IsTransitioning)
        {
            return;
        }

        if (levelSelectorModalPanel != null && levelSelectorModalPanel.activeSelf)
        {
            CloseLevelSelector(playAudio: false);
        }

        if (competencyReportPanelObj != null && competencyReportPanelObj.activeSelf)
        {
            CloseCompetencyReport();
        }

        PlaySfx(clipToggleInput);
        airWeavingController.ToggleAirWeavingMode();
    }

    private void HandleAirWeavingModeChanged(bool isAirWeavingActive)
    {
        if (txtAirWeavingToggleLabel != null)
        {
            txtAirWeavingToggleLabel.text = isAirWeavingActive ? "Mode: Kartu (v5)" : "Mode: Udara (v6)";
            txtAirWeavingToggleLabel.color = isAirWeavingActive
                ? new Color(0.88f, 1.00f, 0.90f, 1f)
                : new Color(0.86f, 0.98f, 1.00f, 1f);
        }

        if (airWeavingToggleBg != null)
        {
            airWeavingToggleBg.color = isAirWeavingActive
                ? new Color(0.08f, 0.34f, 0.28f, 0.94f)
                : LevelSelectorOpenBgColor;
        }

        if (airWeavingToggleOutline != null)
        {
            airWeavingToggleOutline.effectColor = isAirWeavingActive
                ? new Color(0.26f, 1.00f, 0.74f, 0.98f)
                : MatrixAccentCyanColor;
        }

        isReadyForNextLevel = false;
        awaitingPostVictoryChoice = false;
        manualPassedRowMask = 0;
        hasShownManualOverQuotaToast = false;
        if (compactHudMenu != null) { compactHudMenu.SetAirWeavingMode(isAirWeavingActive); return; }

        // Padatkan teks misi atas menjadi pita horizontal ramping di Mode Udara agar bersih di atas label Rak Parkir (XOR)
        if (txtMissionDesc != null)
        {
            RectTransform missionRect = txtMissionDesc.rectTransform;
            if (!hasSavedMissionDescLayout)
            {
                savedMissionDescAnchoredPos = missionRect.anchoredPosition;
                savedMissionDescSizeDelta = missionRect.sizeDelta;
                savedMissionDescFontSize = txtMissionDesc.fontSize;
                savedMissionDescLineSpacing = txtMissionDesc.lineSpacing;
                savedMissionDescAlignment = txtMissionDesc.alignment;
                hasSavedMissionDescLayout = true;
            }

            if (isAirWeavingActive)
            {
                missionRect.anchoredPosition = new Vector2(0f, -54f);
                missionRect.sizeDelta = new Vector2(1160f, 52f);
                txtMissionDesc.fontSize = Mathf.Min(savedMissionDescFontSize, 23f);
                txtMissionDesc.lineSpacing = -6f;
                txtMissionDesc.alignment = TextAlignmentOptions.Top;
            }
            else
            {
                missionRect.anchoredPosition = savedMissionDescAnchoredPos;
                missionRect.sizeDelta = savedMissionDescSizeDelta;
                txtMissionDesc.fontSize = savedMissionDescFontSize;
                txtMissionDesc.lineSpacing = savedMissionDescLineSpacing;
                txtMissionDesc.alignment = savedMissionDescAlignment;
            }
        }
    }

    public void ToggleLevelSelector()
    {
        if (isVerifying)
        {
            PlaySfx(clipToggleInput);
            if (txtFeedback != null)
            {
                txtFeedback.text = "Tunggu hingga proses Uji Rangkaian selesai sebelum berpindah level.";
                txtFeedback.color = new Color(1.00f, 0.78f, 0.28f, 1f);
            }
            return;
        }

        if (levelSelectorModalPanel == null)
        {
            return;
        }

        if (levelSelectorModalPanel.activeSelf)
        {
            CloseLevelSelector(playAudio: true);
        }
        else
        {
            OpenLevelSelector();
        }
    }

    public void OpenLevelSelector()
    {
        if (isVerifying || levelSelectorModalPanel == null)
        {
            return;
        }

        compactHudMenu?.CloseMenuDrawer();
        CloseCompetencyReport();
        PlaySfx(clipToggleInput);
        RefreshLevelSelectorUI();
        levelSelectorModalPanel.transform.SetAsLastSibling();
        levelSelectorModalPanel.SetActive(true);
    }

    public void CloseLevelSelector(bool playAudio = false)
    {
        if (playAudio)
        {
            PlaySfx(clipToggleInput);
        }

        if (levelSelectorModalPanel != null && levelSelectorModalPanel.activeSelf)
        {
            levelSelectorModalPanel.SetActive(false);
        }
    }

    private void CloseLevelSelectorWithAudio()
    {
        CloseLevelSelector(playAudio: true);
    }

    private void OnLevelSelectorItemClicked(int levelIndex)
    {
        if (isVerifying)
        {
            PlaySfx(clipToggleInput);
            if (txtFeedback != null)
            {
                txtFeedback.text = "Tunggu hingga proses Uji Rangkaian selesai sebelum berpindah level.";
                txtFeedback.color = new Color(1.00f, 0.78f, 0.28f, 1f);
            }
            return;
        }

        PlaySfx(clipToggleInput);
        CloseLevelSelector(playAudio: false);
        LoadLevel(levelIndex);
    }

    private void OnLevelSelectorSandboxClicked()
    {
        if (isVerifying)
        {
            PlaySfx(clipToggleInput);
            if (txtFeedback != null)
            {
                txtFeedback.text = "Tunggu hingga proses Uji Rangkaian selesai sebelum berpindah ke Mode Sandbox.";
                txtFeedback.color = new Color(1.00f, 0.78f, 0.28f, 1f);
            }
            return;
        }

        PlaySfx(clipToggleInput);
        CloseLevelSelector(playAudio: false);
        EnterSandboxMode();
    }

    private void RefreshLevelSelectorUI()
    {
        EnsureLevelProgressArraysInitialized();

        if (levelSelectorItemButtons != null)
        {
            for (int i = 0; i < levelSelectorItemButtons.Length; i++)
            {
                bool isCleared = levelCompletedFlags != null && i < levelCompletedFlags.Length && levelCompletedFlags[i];
                int bestStars = (levelBestStars != null && i < levelBestStars.Length) ? levelBestStars[i] : 0;
                bool isActive = !isSandboxMode && i == currentLevelIndex;

                if (levelSelectorItemBackgrounds != null && i < levelSelectorItemBackgrounds.Length && levelSelectorItemBackgrounds[i] != null)
                {
                    levelSelectorItemBackgrounds[i].color = isActive
                        ? LevelSelectorCardActiveBgColor
                        : (isCleared ? LevelSelectorCardClearedBgColor : LevelSelectorCardIdleBgColor);
                }

                if (levelSelectorItemOutlines != null && i < levelSelectorItemOutlines.Length && levelSelectorItemOutlines[i] != null)
                {
                    levelSelectorItemOutlines[i].effectColor = isActive
                        ? LevelSelectorCardActiveOutlineColor
                        : (isCleared ? NextLevelBtnOutlineColor : LevelSelectorCardIdleOutlineColor);
                }

                if (levelSelectorItemStatusTexts != null && i < levelSelectorItemStatusTexts.Length && levelSelectorItemStatusTexts[i] != null)
                {
                    string statusTag = isActive ? "AKTIF" : (isCleared ? "SELESAI" : "SIAP");
                    levelSelectorItemStatusTexts[i].text = $"Bintang: {FormatStarsAscii(bestStars)} | {statusTag}";
                    if (isActive)
                    {
                        levelSelectorItemStatusTexts[i].color = new Color(1.00f, 0.90f, 0.30f, 1f);
                    }
                    else if (bestStars >= 3)
                    {
                        levelSelectorItemStatusTexts[i].color = new Color(1.00f, 0.86f, 0.25f, 1f);
                    }
                    else if (isCleared)
                    {
                        levelSelectorItemStatusTexts[i].color = MatrixBrightGreenColor;
                    }
                    else
                    {
                        levelSelectorItemStatusTexts[i].color = new Color(0.70f, 0.84f, 0.94f, 1f);
                    }
                }
            }
        }

        if (levelSelectorSandboxBg != null)
        {
            levelSelectorSandboxBg.color = isSandboxMode
                ? LevelSelectorSandboxActiveBgColor
                : LevelSelectorSandboxIdleBgColor;
        }

        if (levelSelectorSandboxOutline != null)
        {
            levelSelectorSandboxOutline.effectColor = isSandboxMode
                ? LevelSelectorCardActiveOutlineColor
                : LevelSelectorSandboxOutlineColor;
        }

        if (txtLevelSelectorSandboxStatus != null)
        {
            txtLevelSelectorSandboxStatus.text = isSandboxMode
                ? "MODE AKTIF | Tanpa Batas Kuota"
                : "Eksplorasi Bebas Tanpa Batas Kuota";
            txtLevelSelectorSandboxStatus.color = isSandboxMode
                ? new Color(1.00f, 0.90f, 0.30f, 1f)
                : MatrixBrightCyanColor;
        }
    }

    private static string GetLevelSelectorCardTitle(int levelIndex)
    {
        switch (levelIndex)
        {
            case 0: return "L1 [OR] - Alarm Pintu";
            case 1: return "L2 [XOR] - Sakelar Tangga";
            case 2: return "L3 [AND+NOT] - Brankas";
            case 3: return "L4 [NAND] - Reaktor";
            case 4: return "L5 [XNOR] - Inti Kembar";
            case 5: return "L6 [NOR] - Ruang Server";
            case 6: return "L7 [De Morgan] - Biometrik";
            default: return $"L{levelIndex + 1} - Misi Logika";
        }
    }

    private void OnReportSandboxClicked()
    {
        PlaySfx(clipToggleInput);
        CloseCompetencyReport();
        EnterSandboxMode();
    }

    private void OnReportResetProgressClicked()
    {
        PlaySfx(clipToggleInput);
        ClearPersistentProgress();
        RefreshCompetencyReportUI();
    }

    private void RefreshCompetencyReportUI()
    {
        EnsureLevelProgressArraysInitialized();
        int totalStars = GetTotalStars();
        int maxStars = GetMaxTotalStars();
        int levelCount = levels != null ? levels.Count : 0;

        int clearedCount = 0;
        if (levelCompletedFlags != null)
        {
            for (int i = 0; i < levelCompletedFlags.Length; i++)
            {
                if (levelCompletedFlags[i])
                {
                    clearedCount++;
                }
            }
        }

        if (txtReportSummaryStats != null)
        {
            txtReportSummaryStats.text = $"Total Bintang: {totalStars} / {maxStars}  |  Misi Tuntas: {clearedCount} / {levelCount}";
        }

        if (txtReportPredicate != null)
        {
            txtReportPredicate.text = GetCompetencyPredicateText(totalStars, out Color predicateColor);
            txtReportPredicate.color = predicateColor;
        }

        if (reportRowTitleTexts == null)
        {
            return;
        }

        for (int i = 0; i < reportRowTitleTexts.Length; i++)
        {
            bool hasLevel = levels != null && i < levels.Count;
            bool isCleared = levelCompletedFlags != null && i < levelCompletedFlags.Length && levelCompletedFlags[i];
            int stars = (levelBestStars != null && i < levelBestStars.Length) ? levelBestStars[i] : 0;

            if (reportRowTitleTexts[i] != null)
            {
                string levelTitle = hasLevel ? levels[i].levelTitle : $"Level {i + 1}";
                reportRowTitleTexts[i].text = FormatReportRowTitle(i, levelTitle);
            }

            if (reportRowStarsTexts[i] != null)
            {
                reportRowStarsTexts[i].text = FormatStarsAscii(stars);
                reportRowStarsTexts[i].color = stars switch
                {
                    3 => new Color(1.00f, 0.86f, 0.25f, 1f),
                    2 => MatrixBrightCyanColor,
                    1 => new Color(1.00f, 0.65f, 0.28f, 1f),
                    _ => new Color(0.65f, 0.72f, 0.80f, 1f)
                };
            }

            if (reportRowStatusTexts[i] != null)
            {
                reportRowStatusTexts[i].text = isCleared ? "TUNTAS" : "BELUM";
                reportRowStatusTexts[i].color = isCleared
                    ? MatrixBrightGreenColor
                    : new Color(0.95f, 0.68f, 0.35f, 1f);
            }

            if (reportRowBackgrounds != null && reportRowBackgrounds[i] != null)
            {
                reportRowBackgrounds[i].color = isCleared ? RowPassBgColor : RowIdleBgColor;
            }
        }
    }

    private static string GetCompetencyPredicateText(int totalStars, out Color predicateColor)
    {
        if (totalStars >= 19)
        {
            predicateColor = new Color(1.00f, 0.86f, 0.25f, 1f);
            return "PREDIKAT: ARSITEK LOGIKA UTAMA (SANGAT MAHIR)";
        }

        if (totalStars >= 14)
        {
            predicateColor = MatrixBrightGreenColor;
            return "PREDIKAT: TEKNISI RANGKAIAN KOMBINASIONAL (MAHIR)";
        }

        if (totalStars >= 7)
        {
            predicateColor = new Color(1.00f, 0.76f, 0.28f, 1f);
            return "PREDIKAT: PERAKIT GERBANG DASAR (CUKUP)";
        }

        predicateColor = new Color(0.75f, 0.86f, 0.96f, 1f);
        return "PREDIKAT: KADET LOGIKA DIGITAL (PEMULA)";
    }

    private static string GetCompetencyTopicTag(int levelIndex)
    {
        switch (levelIndex)
        {
            case 0: return "[OR]";
            case 1: return "[XOR]";
            case 2: return "[A AND NOT B]";
            case 3: return "[NAND]";
            case 4: return "[XNOR]";
            case 5: return "[NOR]";
            case 6: return "[De Morgan]";
            default: return $"[L{levelIndex + 1}]";
        }
    }

    private static string FormatReportRowTitle(int levelIndex, string rawTitle)
    {
        string topicTag = GetCompetencyTopicTag(levelIndex);
        if (string.IsNullOrEmpty(rawTitle))
        {
            return $"Level {levelIndex + 1} {topicTag}";
        }

        int colonIdx = rawTitle.IndexOf(':');
        if (colonIdx > 0 && colonIdx + 1 < rawTitle.Length)
        {
            string missionName = rawTitle.Substring(colonIdx + 1).Trim();
            return $"Level {levelIndex + 1} {topicTag}: {missionName}";
        }

        return $"Level {levelIndex + 1} {topicTag}: {rawTitle}";
    }

    /// <summary>
    /// Menampilkan petunjuk pedagogis bertahap (Tier 1 Konsep -> Tier 2 Komponen -> Tier 3 Struktur Rakitan).
    /// Membuka Tier 3 sebelum level terselesaikan pertama kali membatasi perolehan maksimal menjadi 2 bintang.
    /// </summary>
    public void OnHintButtonClicked()
    {
        if (isVerifying)
        {
            return;
        }

        PlaySfx(clipToggleInput);

        if (isSandboxMode)
        {
            if (txtFeedback != null)
            {
                txtFeedback.text = "Mode Sandbox bebas berkreasi! Amati rumus Boolean & aliran sinyal 3D secara langsung.";
                txtFeedback.color = new Color(0.35f, 0.95f, 1f, 1f);
            }
            return;
        }

        if (levels == null || levels.Count == 0)
        {
            InitializeDefaultLevels();
        }

        if (hintTierUsed < 3)
        {
            hintTierUsed++;
        }

        UpdateHintButtonUI();
        compactHudMenu?.RefreshAllVisualStates();

        int safeIndex = Mathf.Clamp(currentLevelIndex, 0, levels.Count - 1);
        LevelScenario currentLevel = levels[safeIndex];
        string hintBody = GetHintTextForTier(currentLevel, safeIndex, hintTierUsed);

        if (txtFeedback != null)
        {
            bool hasPrefix = hintBody.StartsWith("[Petunjuk", StringComparison.Ordinal);
            switch (hintTierUsed)
            {
                case 1:
                    txtFeedback.text = hasPrefix ? hintBody : $"[Petunjuk 1/3 - Konsep] {hintBody}";
                    txtFeedback.color = new Color(0.35f, 0.95f, 1.00f, 1f);
                    break;
                case 2:
                    txtFeedback.text = hasPrefix ? hintBody : $"[Petunjuk 2/3 - Komponen] {hintBody}";
                    txtFeedback.color = new Color(1.00f, 0.88f, 0.28f, 1f);
                    break;
                default:
                    string tier3Text = hasPrefix ? hintBody : $"[Petunjuk 3/3 - Struktur] {hintBody}";
                    txtFeedback.text = $"{tier3Text} (Penalti Tier 3: maks [**])";
                    txtFeedback.color = new Color(1.00f, 0.76f, 0.22f, 1f);
                    break;
            }
        }
    }

    private void UpdateHintButtonUI()
    {
        if (txtHintButtonLabel == null)
        {
            return;
        }

        if (isSandboxMode)
        {
            txtHintButtonLabel.text = "Petunjuk: Mode Sandbox Bebas";
            txtHintButtonLabel.color = new Color(0.68f, 0.90f, 0.96f, 1f);
            if (hintButtonBackground != null)
            {
                hintButtonBackground.color = new Color(0.10f, 0.24f, 0.34f, 0.90f);
            }
            return;
        }

        int clampedTier = Mathf.Clamp(hintTierUsed, 0, 3);
        txtHintButtonLabel.text = clampedTier >= 3
            ? "Petunjuk (3/3) - Maks [**]"
            : $"Petunjuk ({clampedTier}/3)";

        switch (clampedTier)
        {
            case 0:
                txtHintButtonLabel.color = new Color(0.86f, 0.97f, 1.00f, 1f);
                if (hintButtonBackground != null) hintButtonBackground.color = new Color(0.12f, 0.30f, 0.45f, 0.94f);
                break;
            case 1:
                txtHintButtonLabel.color = new Color(0.42f, 0.98f, 1.00f, 1f);
                if (hintButtonBackground != null) hintButtonBackground.color = new Color(0.12f, 0.38f, 0.48f, 0.94f);
                break;
            case 2:
                txtHintButtonLabel.color = new Color(1.00f, 0.90f, 0.32f, 1f);
                if (hintButtonBackground != null) hintButtonBackground.color = new Color(0.32f, 0.30f, 0.12f, 0.94f);
                break;
            default:
                txtHintButtonLabel.color = new Color(1.00f, 0.78f, 0.30f, 1f);
                if (hintButtonBackground != null) hintButtonBackground.color = new Color(0.42f, 0.24f, 0.10f, 0.94f);
                break;
        }
    }

    private static string GetHintTextForTier(LevelScenario level, int levelIndex, int tier)
    {
        if (level != null)
        {
            switch (tier)
            {
                case 1:
                    if (!string.IsNullOrEmpty(level.hintTier1Concept)) return level.hintTier1Concept;
                    break;
                case 2:
                    if (!string.IsNullOrEmpty(level.hintTier2Component)) return level.hintTier2Component;
                    break;
                case 3:
                    if (!string.IsNullOrEmpty(level.hintTier3Structure)) return level.hintTier3Structure;
                    break;
            }
        }

        return GetFallbackHintForLevel(levelIndex, tier);
    }

    private static string GetFallbackHintForLevel(int levelIndex, int tier)
    {
        switch (levelIndex)
        {
            case 0:
                if (tier == 1) return "Output bernilai 1 jika minimal salah satu dari A atau B bernilai 1 (hanya 0 saat 00).";
                if (tier == 2) return "Siapkan 1 kartu gerbang OR di antara kartu Input dan Output.";
                return "Susun Input A dan B di kiri -> sambungkan keduanya ke OR -> arahkan OR ke Output.";
            case 1:
                if (tier == 1) return "Output bernilai 1 hanya ketika A dan B BERBEDA (01 atau 10).";
                if (tier == 2) return "Siapkan 1 kartu gerbang XOR (Exclusive OR).";
                return "Susun Input A dan B di kiri -> sambungkan keduanya ke XOR -> arahkan XOR ke Output.";
            case 2:
                if (tier == 1) return "Output hanya boleh 1 saat A=1 dan B=0, sehingga sinyal B harus dibalik dahulu.";
                if (tier == 2) return "Siapkan 2 kartu gerbang: NOT untuk membalik B, dan AND untuk menggabungkannya dengan A.";
                return "Sambungkan Input B -> NOT, lalu arahkan Input A dan keluaran NOT ke AND -> Output.";
            case 3:
                if (tier == 1) return "Kebalikan dari AND: Output tetap 1 untuk 00, 01, 10, dan baru menjadi 0 saat 11.";
                if (tier == 2) return "Siapkan 1 kartu gerbang NAND agar memenuhi batas kuota 1 komponen.";
                return "Susun Input A dan B di kiri -> sambungkan keduanya ke NAND -> arahkan NAND ke Output.";
            case 4:
                if (tier == 1) return "Output bernilai 1 ketika A dan B SAMA (00 atau 11), kebalikan tepat dari XOR.";
                if (tier == 2) return "Kombinasikan 2 kartu gerbang: XOR diikuti gerbang pembalik NOT.";
                return "Sambungkan Input A dan B ke XOR -> sambungkan keluaran XOR ke NOT -> Output.";
            case 5:
                if (tier == 1) return "Gerbang NOR adalah kebalikan dari OR: Output bernilai 1 HANYA ketika kedua sensor A=0 dan B=0 (00).";
                if (tier == 2) return "Siapkan 2 kartu gerbang: kartu OR dan kartu pembalik NOT.";
                return "Hubungkan Input A dan Input B ke gerbang OR, lalu arahkan keluaran OR ke NOT menuju Output: Y = NOT(A OR B).";
            case 6:
                if (tier == 1) return "Menurut Hukum De Morgan, rumus A OR (NOT B) ekuivalen dengan (NOT A) NAND B. Output hanya 0 pada baris A=0, B=1.";
                if (tier == 2) return "Gunakan kartu NOT bersama kartu OR, ATAU gunakan kartu NOT bersama kartu NAND (kuota 2 gerbang).";
                return "Jalur 1: Input B -> NOT, lalu gabungkan dengan Input A di OR. Jalur 2 (De Morgan): Input A -> NOT, lalu gabungkan dengan Input B di NAND.";
            default:
                if (tier == 1) return "Amati kolom Target pada tabel kebenaran untuk baris 00, 01, 10, dan 11.";
                if (tier == 2) return "Pilih kartu gerbang logika yang sesuai dengan batasan kuota di HUD atas.";
                return "Susun kartu Input di kiri, gerbang logika di tengah, dan kartu Output di sisi kanan.";
        }
    }

    private void CreateAudioToggleButton(RectTransform parent, TMP_FontAsset fontAsset)
    {
        GameObject btnObj = new GameObject("Btn_AudioToggle", typeof(RectTransform), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(parent, false);

        RectTransform rect = btnObj.GetComponent<RectTransform>();
        audioToggleRect = rect;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(328f, -8f);
        rect.sizeDelta = new Vector2(118f, 24f);

        audioToggleBackground = btnObj.GetComponent<Image>();
        audioToggleBackground.raycastTarget = true;

        audioToggleButton = btnObj.GetComponent<Button>();
        audioToggleButton.targetGraphic = audioToggleBackground;
        audioToggleButton.onClick.AddListener(ToggleAudioMute);

        txtAudioToggleLabel = CreateMatrixText("Txt_AudioToggle", rect, Vector2.zero, new Vector2(118f, 24f),
            "Audio: ON", 12f, TextAlignmentOptions.Center, Color.white, fontAsset);
        RectTransform labelRect = txtAudioToggleLabel.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
    }

    private Canvas FindTargetCanvas()
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

    private TMP_FontAsset ResolveSafeTMPFont()
    {
        if (txtLevelTitle != null && txtLevelTitle.font != null)
        {
            return txtLevelTitle.font;
        }

        if (txtMissionDesc != null && txtMissionDesc.font != null)
        {
            return txtMissionDesc.font;
        }

        if (txtFeedback != null && txtFeedback.font != null)
        {
            return txtFeedback.font;
        }

        return TMP_Settings.defaultFontAsset;
    }

    private static void CreateMatrixPanelAccentBar(RectTransform parent)
    {
        GameObject accentObj = new GameObject("AccentBar_Top", typeof(RectTransform), typeof(Image));
        accentObj.transform.SetParent(parent, false);

        RectTransform rect = accentObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, 4f);

        Image img = accentObj.GetComponent<Image>();
        img.color = MatrixAccentCyanColor;
        img.raycastTarget = false;
    }

    private static RectTransform CreateMatrixRowContainer(string name, RectTransform parent, float topY, float height, Color bgColor)
    {
        GameObject rowObj = new GameObject(name, typeof(RectTransform), typeof(Image));
        rowObj.transform.SetParent(parent, false);

        RectTransform rect = rowObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(14f, topY);
        rect.sizeDelta = new Vector2(432f, height);

        Image img = rowObj.GetComponent<Image>();
        img.color = bgColor;
        img.raycastTarget = false;

        return rect;
    }

    private static TMP_Text CreateMatrixText(string name, RectTransform parent, Vector2 pos, Vector2 size,
        string initialText, float fontSize, TextAlignmentOptions alignment, Color color, TMP_FontAsset fontAsset)
    {
        GameObject textObj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObj.transform.SetParent(parent, false);

        RectTransform rect = textObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;

        TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
        if (fontAsset != null)
        {
            tmp.font = fontAsset;
        }
        tmp.text = initialText;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;

        return tmp;
    }

    private static TMP_Text CreateMatrixColumnText(string name, RectTransform rowParent, float xPos, float width, float height,
        string initialText, float fontSize, Color color, TMP_FontAsset fontAsset)
    {
        return CreateMatrixText(name, rowParent, new Vector2(xPos, 0f), new Vector2(width, height),
            initialText, fontSize, TextAlignmentOptions.Center, color, fontAsset);
    }

    private void RefreshTruthTableTargets()
    {
        if (matrixRowTargetTexts[0] == null)
        {
            return;
        }

        LevelScenario activeLevel = (!isSandboxMode && levels != null && levels.Count > 0)
            ? levels[Mathf.Clamp(currentLevelIndex, 0, levels.Count - 1)]
            : null;

        for (int i = 0; i < TruthTableRowCount; i++)
        {
            if (isSandboxMode || activeLevel == null || activeLevel.expectedTruthTable == null || i >= activeLevel.expectedTruthTable.Length)
            {
                matrixRowTargetTexts[i].text = "BEBAS";
                matrixRowTargetTexts[i].color = MatrixBrightCyanColor;
            }
            else
            {
                bool targetBit = activeLevel.expectedTruthTable[i];
                matrixRowTargetTexts[i].text = targetBit ? "1" : "0";
                matrixRowTargetTexts[i].color = Color.white;
            }
        }
    }

    private void ResetTruthTableScanResults()
    {
        scanningRowIndex = -1;
        manualPassedRowMask = 0;
        hasShownManualOverQuotaToast = false;

        bool panelActive = truthTableScannerPanelObj != null && truthTableScannerPanelObj.activeInHierarchy;
        for (int i = 0; i < TruthTableRowCount; i++)
        {
            matrixRowScannedFlags[i] = false;
            matrixRowPassedFlags[i] = false;
            if (!panelActive) continue;

            if (matrixRowMeasuredTexts[i] != null)
            {
                matrixRowMeasuredTexts[i].text = "-";
                matrixRowMeasuredTexts[i].color = Color.white;
            }

            if (matrixRowStatusTexts[i] != null)
            {
                matrixRowStatusTexts[i].text = "-";
                matrixRowStatusTexts[i].color = Color.white;
            }

            if (matrixRowBackgrounds[i] != null)
            {
                matrixRowBackgrounds[i].color = RowIdleBgColor;
            }
        }

        if (outputNode != null && outputNode.OutputTruthPod != null)
        {
            outputNode.OutputTruthPod.ResetDiscovery();
        }
    }

    private bool HasAnyScannedMatrixRow()
    {
        for (int i = 0; i < TruthTableRowCount; i++)
        {
            if (matrixRowScannedFlags[i])
            {
                return true;
            }
        }

        return false;
    }

    private void SetTruthTableRowScanning(int rowIndex)
    {
        scanningRowIndex = rowIndex;
        if (rowIndex < 0 || rowIndex >= TruthTableRowCount)
        {
            return;
        }

        if (matrixRowMeasuredTexts[rowIndex] != null)
        {
            matrixRowMeasuredTexts[rowIndex].text = "...";
            matrixRowMeasuredTexts[rowIndex].color = Color.yellow;
        }

        if (matrixRowStatusTexts[rowIndex] != null)
        {
            matrixRowStatusTexts[rowIndex].text = "UJI...";
            matrixRowStatusTexts[rowIndex].color = Color.yellow;
        }

        if (matrixRowBackgrounds[rowIndex] != null)
        {
            matrixRowBackgrounds[rowIndex].color = RowScanningBgColor;
        }

        if (outputNode != null && outputNode.OutputTruthPod != null)
        {
            outputNode.OutputTruthPod.SetVerificationRowScanning(rowIndex);
        }
    }

    private void SetTruthTableRowResult(int rowIndex, bool measuredOutput, bool passed)
    {
        if (rowIndex < 0 || rowIndex >= TruthTableRowCount)
        {
            return;
        }

        matrixRowScannedFlags[rowIndex] = true;
        matrixRowPassedFlags[rowIndex] = passed;

        if (matrixRowMeasuredTexts[rowIndex] != null)
        {
            matrixRowMeasuredTexts[rowIndex].text = measuredOutput ? "1" : "0";
            matrixRowMeasuredTexts[rowIndex].color = measuredOutput ? MatrixBrightGreenColor : MatrixBrightRedColor;
        }

        if (matrixRowStatusTexts[rowIndex] != null)
        {
            if (isSandboxMode)
            {
                matrixRowStatusTexts[rowIndex].text = "[TERCATAT]";
                matrixRowStatusTexts[rowIndex].color = MatrixBrightCyanColor;
            }
            else if (passed)
            {
                matrixRowStatusTexts[rowIndex].text = "[OK] LULUS";
                matrixRowStatusTexts[rowIndex].color = MatrixBrightGreenColor;
            }
            else
            {
                matrixRowStatusTexts[rowIndex].text = "[X] GAGAL";
                matrixRowStatusTexts[rowIndex].color = MatrixBrightRedColor;
            }
        }

        if (matrixRowBackgrounds[rowIndex] != null)
        {
            matrixRowBackgrounds[rowIndex].color = isSandboxMode
                ? RowSandboxDoneBgColor
                : (passed ? RowPassBgColor : RowFailBgColor);
        }

        if (outputNode != null && outputNode.OutputTruthPod != null)
        {
            outputNode.OutputTruthPod.SetVerificationRowResult(rowIndex, passed);
        }
    }

    private int ComputeCircuitTopologyHash()
    {
        unchecked
        {
            int hash = 17;
            if (outputNode != null)
            {
                hash = (hash * 31) + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(outputNode);
                hash = (hash * 31) + (outputNode.isTracked ? 1 : 0);
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

                    hash = (hash * 31) + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(node);
                    hash = (hash * 31) + ((int)node.nodeType * 17);
                    hash = (hash * 31) + (node.isTracked ? 1 : 0);
                    hash = (hash * 31) + (node.connectedTarget != null
                        ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(node.connectedTarget)
                        : 0);

                    List<GateNode> inputs = node.connectedInputs;
                    if (inputs != null)
                    {
                        int inCount = inputs.Count;
                        hash = (hash * 31) + inCount;
                        for (int j = 0; j < inCount; j++)
                        {
                            GateNode input = inputs[j];
                            int inputHash = input != null
                                ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(input)
                                : 0;
                            // Bobot slot port (j + 1) agar penukaran sambungan port Input A <-> Input B selalu mengubah hash
                            hash = (hash * 31) + ((j + 1) * 131) + inputHash;
                        }
                    }
                }
            }

            return hash;
        }
    }

    private void UpdateTruthTableMatrixLive()
    {
        if (txtBooleanFormula == null)
        {
            return;
        }

        int currentTopologyHash = ComputeCircuitTopologyHash();
        if (!hasCachedFormulaTopologyHash || currentTopologyHash != cachedFormulaTopologyHash)
        {
            cachedFormulaTopologyHash = currentTopologyHash;
            hasCachedFormulaTopologyHash = true;
            cachedLiveBooleanFormula = circuitManager != null
                ? circuitManager.GetCircuitBooleanExpression(outputNode)
                : "Y = (Belum Terhubung)";
        }

        string currentFormula = cachedLiveBooleanFormula;

        int activeGates = circuitManager != null ? circuitManager.activeGateCardsCount : 0;
        bool outputTracked = outputNode != null && outputNode.isTracked;
        int currentConnectionCount = circuitManager != null ? circuitManager.GetActiveConnectionCount() : 0;

        bool edgeHashChanged = !hasLastEdgeTopologyHash || lastEdgeTopologyHash != currentTopologyHash;
        bool topologyChanged = !hasLastTopologySignature
            || edgeHashChanged
            || lastTopologyActiveGates != activeGates
            || lastTopologyOutputTracked != outputTracked
            || lastTopologyConnectionCount != currentConnectionCount
            || !string.Equals(lastTopologySignature, currentFormula, StringComparison.Ordinal);

        bool isCircuitComplete = !string.IsNullOrEmpty(currentFormula)
            && currentFormula != "Y = (Belum Terhubung)"
            && !currentFormula.Contains("?")
            && !currentFormula.Contains("(Siklus)");
        bool hasValidLiveCircuit = isCircuitComplete && (isSandboxMode || activeGates > 0);
        isCircuitCompleteLive = hasValidLiveCircuit;

        if (!isVerifying)
        {
            if ((hasLastTopologySignature && topologyChanged) || !hasValidLiveCircuit)
            {
                manualPassedRowMask = 0;
                hasShownManualOverQuotaToast = false;
                if (isReadyForNextLevel)
                {
                    isReadyForNextLevel = false;
                    awaitingPostVictoryChoice = false;
                    compactHudMenu?.RefreshAllVisualStates();
                }
            }

            if (hasLastTopologySignature && topologyChanged)
            {
                if (HasAnyScannedMatrixRow())
                {
                    ResetTruthTableScanResults();
                }
                else if (outputNode != null && outputNode.OutputTruthPod != null)
                {
                    outputNode.OutputTruthPod.ResetDiscovery();
                }

                if (currentConnectionCount > lastActiveConnectionCount)
                {
                    PlaySfx(clipSnapConnect);
                }
            }
            else if (!isCircuitComplete && wasCircuitComplete && outputNode != null && outputNode.OutputTruthPod != null)
            {
                outputNode.OutputTruthPod.ResetDiscovery();
            }

            if (!wasCircuitComplete && isCircuitComplete)
            {
                TriggerHapticPulse();
            }

            lastActiveConnectionCount = currentConnectionCount;
            wasCircuitComplete = isCircuitComplete;
            lastTopologySignature = currentFormula;
            lastTopologyActiveGates = activeGates;
            lastTopologyOutputTracked = outputTracked;
            lastTopologyConnectionCount = currentConnectionCount;
            hasLastTopologySignature = true;
            lastEdgeTopologyHash = currentTopologyHash;
            hasLastEdgeTopologyHash = true;
        }

        bool panelActive = truthTableScannerPanelObj != null && truthTableScannerPanelObj.activeInHierarchy;
        if (!hasRenderedFormulaBaseline || !string.Equals(lastRenderedFormula, currentFormula, StringComparison.Ordinal))
        {
            hasRenderedFormulaBaseline = true;
            lastRenderedFormula = currentFormula;
            if (isSandboxMode && txtMissionDesc != null) txtMissionDesc.text = $"Rumus: {currentFormula}";
            if (panelActive) txtBooleanFormula.text = $"Rumus Rakitan: {currentFormula}";
        }

        if (panelActive && txtCurrentOutputReadout != null)
        {
            int outputState = (outputNode != null && outputNode.isTracked)
                ? (outputNode.currentOutputValue ? 1 : 0)
                : -1;

            if (outputState != lastRenderedOutputState)
            {
                lastRenderedOutputState = outputState;
                if (outputState > 0)
                {
                    txtCurrentOutputReadout.text = "Output Y = 1 [AKTIF]";
                    txtCurrentOutputReadout.color = MatrixBrightGreenColor;
                }
                else if (outputState == 0)
                {
                    txtCurrentOutputReadout.text = "Output Y = 0 [MATI]";
                    txtCurrentOutputReadout.color = MatrixBrightRedColor;
                }
                else
                {
                    txtCurrentOutputReadout.text = "Output Y = - [BELUM TERLACAK]";
                    txtCurrentOutputReadout.color = new Color(1f, 0.72f, 0.25f, 1f);
                }
            }
        }

        // Sorot baris kombinasi Input A & Input B yang aktif secara manual di luar proses scanning
        int activeManualRow = -1;
        if (!isVerifying)
        {
            bool anyInputTracked = (inputANode != null && inputANode.isTracked) || (inputBNode != null && inputBNode.isTracked);
            if (anyInputTracked)
            {
                bool curA = inputANode != null && inputANode.currentOutputValue;
                bool curB = inputBNode != null && inputBNode.currentOutputValue;
                activeManualRow = (curA ? 2 : 0) + (curB ? 1 : 0);
            }

            if (outputNode != null && outputNode.OutputTruthPod != null)
            {
                outputNode.OutputTruthPod.SetActiveManualRow(activeManualRow);
                if (!isSandboxMode && hasValidLiveCircuit && activeManualRow >= 0 && outputNode.isTracked)
                {
                    outputNode.OutputTruthPod.RecordLiveDiscovery(activeManualRow, outputNode.currentOutputValue);
                    TryEvaluateManualAutoWin(activeManualRow, outputNode.currentOutputValue, activeGates);
                }
            }
        }

        if (!panelActive) return;

        for (int i = 0; i < TruthTableRowCount; i++)
        {
            if (matrixRowBackgrounds[i] == null)
            {
                continue;
            }

            if (i == scanningRowIndex)
            {
                matrixRowBackgrounds[i].color = RowScanningBgColor;
            }
            else if (!isVerifying && i == activeManualRow)
            {
                matrixRowBackgrounds[i].color = RowActiveManualBgColor;
            }
            else if (matrixRowScannedFlags[i])
            {
                matrixRowBackgrounds[i].color = isSandboxMode
                    ? RowSandboxDoneBgColor
                    : (matrixRowPassedFlags[i] ? RowPassBgColor : RowFailBgColor);
            }
            else
            {
                matrixRowBackgrounds[i].color = RowIdleBgColor;
            }
        }
    }

    /// <summary>
    /// Memantau perubahan nilai logika Input A dan Input B dari tombol UI maupun rotasi fisik kartu AR,
    /// lalu memainkan efek klik elektronik prosedural di luar fase verifikasi otomatis.
    /// </summary>
    private void MonitorInputStateAudio()
    {
        if (isVerifying)
        {
            return;
        }

        bool currentA = inputANode != null && inputANode.currentOutputValue;
        bool currentB = inputBNode != null && inputBNode.currentOutputValue;

        if (!inputsBaselineInitialized)
        {
            lastInputAState = currentA;
            lastInputBState = currentB;
            inputsBaselineInitialized = true;
            return;
        }

        if (currentA != lastInputAState || currentB != lastInputBState)
        {
            PlaySfx(clipToggleInput);
            lastInputAState = currentA;
            lastInputBState = currentB;
        }
    }

    private void SyncAudioStateBaselines(bool resetTopologySignature)
    {
        lastInputAState = inputANode != null && inputANode.currentOutputValue;
        lastInputBState = inputBNode != null && inputBNode.currentOutputValue;
        inputsBaselineInitialized = true;
        lastActiveConnectionCount = circuitManager != null ? circuitManager.GetActiveConnectionCount() : 0;
        wasCircuitComplete = circuitManager != null && circuitManager.IsCircuitCompleteToOutput(outputNode);

        if (resetTopologySignature)
        {
            hasLastTopologySignature = false;
            lastTopologySignature = string.Empty;
            hasCachedFormulaTopologyHash = false;
            hasLastEdgeTopologyHash = false;
        }
    }

    /// <summary>
    /// Menginisialisasi AudioSource runtime serta membangkitkan seluruh AudioClip PCM 44100 Hz mono prosedural sekali saja.
    /// </summary>
    private void InitializeProceduralAudio()
    {
        sfxAudioSource = GetComponent<AudioSource>();
        if (sfxAudioSource == null)
        {
            sfxAudioSource = gameObject.AddComponent<AudioSource>();
        }

        sfxAudioSource.playOnAwake = false;
        sfxAudioSource.loop = false;
        sfxAudioSource.spatialBlend = 0f;
        sfxAudioSource.volume = 1f;

        if (clipToggleInput == null)
        {
            clipToggleInput = CreateMultiSegmentToneClip("SFX_ToggleInput", new float[] { 680f, 920f }, 0.03f, 0.25f, false);
        }

        if (clipSnapConnect == null)
        {
            clipSnapConnect = CreateSweepAudioClip("SFX_SnapConnect", 420f, 840f, 0.10f, 0.24f, true);
        }

        float[] scanFrequencies = new float[] { 520f, 620f, 720f, 820f };
        for (int i = 0; i < TruthTableRowCount; i++)
        {
            if (clipScanRows[i] == null)
            {
                clipScanRows[i] = CreateSweepAudioClip($"SFX_ScanRow_{i}", scanFrequencies[i], scanFrequencies[i], 0.065f, 0.22f, false);
            }
        }

        if (clipRowPass == null)
        {
            clipRowPass = CreateSweepAudioClip("SFX_RowPass", 1046f, 1046f, 0.09f, 0.24f, true);
        }

        if (clipRowFail == null)
        {
            clipRowFail = CreateRowFailBuzzClip();
        }

        if (clipVictoryFanfare == null)
        {
            clipVictoryFanfare = CreateMultiSegmentToneClip(
                "SFX_VictoryFanfare",
                new float[] { 523.25f, 659.25f, 783.99f, 1046.50f },
                0.105f,
                0.23f,
                true);
        }
    }

    private void DestroyProceduralAudioClips()
    {
        DestroyAudioClipRef(ref clipToggleInput);
        DestroyAudioClipRef(ref clipSnapConnect);
        for (int i = 0; i < clipScanRows.Length; i++)
        {
            DestroyAudioClipRef(ref clipScanRows[i]);
        }
        DestroyAudioClipRef(ref clipRowPass);
        DestroyAudioClipRef(ref clipRowFail);
        DestroyAudioClipRef(ref clipVictoryFanfare);
    }

    private static void DestroyAudioClipRef(ref AudioClip clip)
    {
        if (clip != null)
        {
            Destroy(clip);
            clip = null;
        }
    }

    private void PlaySfx(AudioClip clip)
    {
        if (isAudioMuted || sfxAudioSource == null || clip == null)
        {
            return;
        }

        sfxAudioSource.PlayOneShot(clip);
    }

    /// <summary>
    /// Mengaktifkan / menonaktifkan efek suara prosedural dari tombol HUD "Audio: ON/OFF".
    /// </summary>
    public void ToggleAudioMute()
    {
        isAudioMuted = !isAudioMuted;
        UpdateAudioToggleButtonUI();
        SavePersistentProgress();

        if (!isAudioMuted)
        {
            PlaySfx(clipToggleInput);
        }
    }

    private void UpdateAudioToggleButtonUI()
    {
        if (txtAudioToggleLabel != null)
        {
            txtAudioToggleLabel.text = isAudioMuted ? "Audio: OFF" : "Audio: ON";
            txtAudioToggleLabel.color = isAudioMuted
                ? new Color(1.00f, 0.65f, 0.35f, 1f)
                : new Color(0.85f, 0.98f, 1.00f, 1f);
        }

        if (audioToggleBackground != null)
        {
            audioToggleBackground.color = isAudioMuted
                ? new Color(0.28f, 0.14f, 0.12f, 0.92f)
                : new Color(0.10f, 0.34f, 0.48f, 0.92f);
        }
    }

    /// <summary>
    /// Memicu umpan balik getar Android (Handheld.Vibrate) dengan jeda cooldown minimum agar tidak bergetar berulang.
    /// </summary>
    private void TriggerHapticPulse()
    {
        float now = Time.unscaledTime;
        if (now - lastHapticTime < HapticCooldownSeconds)
        {
            return;
        }

        lastHapticTime = now;

#if UNITY_ANDROID && !UNITY_EDITOR
        Handheld.Vibrate();
#endif
    }

    private static AudioClip CreateSweepAudioClip(string clipName, float startFreq, float endFreq, float durationSec, float amplitude, bool addShimmerHarmonic)
    {
        int sampleCount = Mathf.Max(2, Mathf.RoundToInt(durationSec * AudioSampleRate));
        float[] samples = new float[sampleCount];
        double phase = 0.0;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / (sampleCount - 1);
            float freq = Mathf.Lerp(startFreq, endFreq, t);
            phase += (2.0 * Math.PI * freq) / AudioSampleRate;

            double wave = Math.Sin(phase);
            if (addShimmerHarmonic)
            {
                wave = (wave + 0.22 * Math.Sin(phase * 2.0)) * 0.82;
            }

            samples[i] = (float)(wave * amplitude);
        }

        ApplyFadeEnvelope(samples, sampleCount, AudioEnvelopeSeconds);
        return CreateAudioClipFromSamples(clipName, samples);
    }

    private static AudioClip CreateMultiSegmentToneClip(string clipName, float[] frequencies, float segmentDurationSec, float amplitude, bool applyPerNoteDecay)
    {
        int segmentSamples = Mathf.Max(2, Mathf.RoundToInt(segmentDurationSec * AudioSampleRate));
        int totalSamples = segmentSamples * frequencies.Length;
        float[] samples = new float[totalSamples];
        double phase = 0.0;

        for (int s = 0; s < frequencies.Length; s++)
        {
            double phaseStep = (2.0 * Math.PI * frequencies[s]) / AudioSampleRate;
            for (int j = 0; j < segmentSamples; j++)
            {
                phase += phaseStep;
                float env = 1f;
                if (applyPerNoteDecay)
                {
                    float localT = (float)j / segmentSamples;
                    env = 1f - (0.35f * localT);
                }

                double wave = Math.Sin(phase) + (applyPerNoteDecay ? 0.18 * Math.Sin(phase * 2.0) : 0.0);
                samples[(s * segmentSamples) + j] = (float)(wave * amplitude * env);
            }
        }

        ApplyFadeEnvelope(samples, totalSamples, AudioEnvelopeSeconds);
        return CreateAudioClipFromSamples(clipName, samples);
    }

    private static AudioClip CreateRowFailBuzzClip()
    {
        const float durationSec = 0.15f;
        const float baseFreq = 190f;
        const float amplitude = 0.22f;

        int sampleCount = Mathf.Max(2, Mathf.RoundToInt(durationSec * AudioSampleRate));
        float[] samples = new float[sampleCount];
        double phase = 0.0;
        double phaseStep = (2.0 * Math.PI * baseFreq) / AudioSampleRate;

        for (int i = 0; i < sampleCount; i++)
        {
            phase += phaseStep;
            double wave = (0.72 * Math.Sin(phase)) + (0.28 * Math.Sin(phase * 3.0)) + (0.12 * Math.Sin(phase * 5.0));
            samples[i] = (float)(wave * amplitude * 0.85);
        }

        ApplyFadeEnvelope(samples, sampleCount, AudioEnvelopeSeconds);
        return CreateAudioClipFromSamples("SFX_RowFailBuzz", samples);
    }

    private static void ApplyFadeEnvelope(float[] samples, int sampleCount, float fadeDurationSec)
    {
        int fadeSamples = Mathf.Clamp(Mathf.RoundToInt(fadeDurationSec * AudioSampleRate), 1, sampleCount / 2);
        for (int i = 0; i < fadeSamples; i++)
        {
            float t = (float)i / fadeSamples;
            float smoothFactor = 0.5f * (1f - Mathf.Cos(Mathf.PI * t));
            samples[i] *= smoothFactor;
            samples[sampleCount - 1 - i] *= smoothFactor;
        }
    }

    private static AudioClip CreateAudioClipFromSamples(string clipName, float[] samples)
    {
        AudioClip clip = AudioClip.Create(clipName, samples.Length, 1, AudioSampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// Memastikan array pelacakan penyelesaian level & rating bintang sesuai dengan jumlah skenario level yang aktif.
    /// </summary>
    private void EnsureLevelProgressArraysInitialized()
    {
        int count = levels != null ? levels.Count : 0;
        if (levelCompletedFlags == null || levelCompletedFlags.Length != count)
        {
            levelCompletedFlags = new bool[count];
        }

        if (levelBestStars == null || levelBestStars.Length != count)
        {
            levelBestStars = new int[count];
        }
    }

    /// <summary>
    /// Memuat progres bintang, status selesai 7 level, level terakhir terbuka, dan preferensi audio dari PlayerPrefs.
    /// </summary>
    private void LoadPersistentProgress()
    {
        EnsureLevelProgressArraysInitialized();

        isAudioMuted = PlayerPrefs.GetInt(PrefKeyAudioMuted, isAudioMuted ? 1 : 0) == 1;

        int levelCount = levels != null ? levels.Count : 0;
        for (int i = 0; i < levelCount; i++)
        {
            levelCompletedFlags[i] = PlayerPrefs.GetInt($"{PrefKeyLevelClearedPrefix}{i}", 0) == 1;
            levelBestStars[i] = Mathf.Clamp(PlayerPrefs.GetInt($"{PrefKeyLevelStarsPrefix}{i}", 0), 0, 3);
        }

        if (levelCount > 0 && PlayerPrefs.HasKey(PrefKeyLastUnlockedLevel))
        {
            int savedUnlocked = PlayerPrefs.GetInt(PrefKeyLastUnlockedLevel, 0);
            currentLevelIndex = Mathf.Clamp(savedUnlocked, 0, levelCount - 1);
        }
    }

    /// <summary>
    /// Menyimpan seluruh progres level, bintang terbaik, dan status mute audio ke PlayerPrefs perangkat.
    /// </summary>
    private void SavePersistentProgress()
    {
        EnsureLevelProgressArraysInitialized();

        PlayerPrefs.SetInt(PrefKeyAudioMuted, isAudioMuted ? 1 : 0);

        int levelCount = levels != null ? levels.Count : 0;
        int highestUnlocked = 0;
        for (int i = 0; i < levelCount; i++)
        {
            bool cleared = levelCompletedFlags != null && i < levelCompletedFlags.Length && levelCompletedFlags[i];
            int stars = (levelBestStars != null && i < levelBestStars.Length) ? Mathf.Clamp(levelBestStars[i], 0, 3) : 0;

            PlayerPrefs.SetInt($"{PrefKeyLevelClearedPrefix}{i}", cleared ? 1 : 0);
            PlayerPrefs.SetInt($"{PrefKeyLevelStarsPrefix}{i}", stars);

            if (cleared)
            {
                highestUnlocked = Mathf.Min(levelCount - 1, i + 1);
            }
        }

        if (levelCount > 0)
        {
            int lastUnlocked = Mathf.Clamp(Mathf.Max(highestUnlocked, currentLevelIndex), 0, levelCount - 1);
            PlayerPrefs.SetInt(PrefKeyLastUnlockedLevel, lastUnlocked);
        }

        PlayerPrefs.Save();
    }

    /// <summary>
    /// Menghapus seluruh penyimpanan progres level & bintang (0/21) di PlayerPrefs dan mengembalikan permainan ke Level 1.
    /// </summary>
    public void ClearPersistentProgress()
    {
        EnsureLevelProgressArraysInitialized();
        Array.Clear(levelCompletedFlags, 0, levelCompletedFlags.Length);
        Array.Clear(levelBestStars, 0, levelBestStars.Length);
        attemptsOnCurrentLevel = 0;
        hintTierUsed = 0;

        int levelCount = levels != null ? levels.Count : 0;
        for (int i = 0; i < levelCount; i++)
        {
            PlayerPrefs.DeleteKey($"{PrefKeyLevelClearedPrefix}{i}");
            PlayerPrefs.DeleteKey($"{PrefKeyLevelStarsPrefix}{i}");
        }
        PlayerPrefs.DeleteKey(PrefKeyLastUnlockedLevel);
        PlayerPrefs.Save();

        if (inputANode != null)
        {
            inputANode.SetSimulationOverride(false);
            inputANode.SetInputState(false);
        }

        if (inputBNode != null)
        {
            inputBNode.SetSimulationOverride(false);
            inputBNode.SetInputState(false);
        }

        LoadLevel(0);
        RefreshCompetencyReportUI();
    }

    private static int CalculateEarnedStars(int attempts, bool withinQuota, bool applyTier3HintCap)
    {
        int stars;
        if (withinQuota && attempts <= 2)
        {
            stars = 3;
        }
        else if (attempts <= 4)
        {
            stars = 2;
        }
        else
        {
            stars = 1;
        }

        if (applyTier3HintCap)
        {
            stars = Mathf.Min(stars, 2);
        }

        return stars;
    }

    /// <summary>
    /// Menganalisis miskonsepsi logika siswa dengan membandingkan ekspresi Boolean aktual rangkaian
    /// terhadap pola tabel kebenaran target dan baris pertama yang gagal.
    /// </summary>
    private static string BuildMisconceptionDiagnostic(
        string actualFormula,
        bool[] expectedTable,
        bool testA,
        bool testB,
        bool actualOutput,
        bool expectedOutput)
    {
        if (string.IsNullOrEmpty(actualFormula)
            || actualFormula == "Y = (Belum Terhubung)"
            || actualFormula.Contains("?")
            || actualFormula.Contains("(Siklus)"))
        {
            return "Masih ada kaki input gerbang yang belum menerima sinyal ('?') sehingga evaluasi belum lengkap.";
        }

        bool hasFourRows = expectedTable != null && expectedTable.Length == 4;
        bool targetIsOr = hasFourRows && !expectedTable[0] && expectedTable[1] && expectedTable[2] && expectedTable[3];
        bool targetIsXor = hasFourRows && !expectedTable[0] && expectedTable[1] && expectedTable[2] && !expectedTable[3];
        bool targetIsAndNotB = hasFourRows && !expectedTable[0] && !expectedTable[1] && expectedTable[2] && !expectedTable[3];
        bool targetIsNand = hasFourRows && expectedTable[0] && expectedTable[1] && expectedTable[2] && !expectedTable[3];
        bool targetIsXnor = hasFourRows && expectedTable[0] && !expectedTable[1] && !expectedTable[2] && expectedTable[3];
        bool targetIsNor = hasFourRows && expectedTable[0] && !expectedTable[1] && !expectedTable[2] && !expectedTable[3];
        bool targetIsDeMorgan = hasFourRows && expectedTable[0] && !expectedTable[1] && expectedTable[2] && expectedTable[3];

        bool hasNot = actualFormula.Contains("NOT");
        bool hasNand = actualFormula.Contains("NAND");
        bool hasXor = actualFormula.Contains("XOR");
        bool hasAnd = !hasNand && actualFormula.Contains("AND");
        bool hasOr = !hasXor && actualFormula.Contains("OR");

        if (targetIsOr && hasAnd && !hasNot)
        {
            return $"Rumus {actualFormula} memakai AND yang butuh kedua input 1, sehingga saat A={(testA ? 1 : 0)}, B={(testB ? 1 : 0)} keluar 0. Misi ini butuh gerbang OR.";
        }

        if (targetIsOr && hasXor && !hasNot)
        {
            return $"Gerbang XOR pada {actualFormula} memberi 0 saat A=1 dan B=1. Ganti dengan gerbang OR agar kondisi 11 tetap bernilai 1.";
        }

        if (targetIsXor && hasOr && !hasNot)
        {
            return $"Gerbang OR pada {actualFormula} mengeluarkan 1 saat A=1 dan B=1, padahal target butuh XOR (1 hanya jika input berbeda).";
        }

        if (targetIsXor && hasAnd && !hasNot)
        {
            return $"Gerbang AND pada {actualFormula} keluar 0 saat input berbeda (A={(testA ? 1 : 0)}, B={(testB ? 1 : 0)}). Ganti dengan gerbang XOR.";
        }

        if (targetIsAndNotB)
        {
            if (actualFormula.Contains("(NOT A)"))
            {
                return "Gerbang NOT terpasang di jalur A, bukan B. Pindahkan kartu NOT ke jalur Input B sebelum masuk ke AND.";
            }

            if (hasAnd && !hasNot)
            {
                return $"Rangkaian {actualFormula} belum membalik Input B. Tambahkan gerbang NOT di jalur B sebelum masuk ke AND.";
            }

            if (hasNand || actualFormula.Contains("NOT("))
            {
                return "Posisi NOT membalik seluruh hasil AND, padahal hanya Input B yang perlu dibalik sebelum masuk ke AND.";
            }
        }

        if (targetIsNand && hasAnd && !hasNot)
        {
            return $"Gerbang AND menghasilkan kebalikan tepat target NAND. Gunakan kartu NAND atau tambahkan pembalik NOT di keluaran AND.";
        }

        if (targetIsXnor && hasXor && !hasNot)
        {
            return "Gerbang XOR bernilai 0 saat 00/11 (kebalikan tepat XNOR). Tambahkan kartu pembalik NOT di sisi kanan XOR.";
        }

        if (targetIsNor)
        {
            if (hasOr && !hasNot)
            {
                return "Gerbang OR menghasilkan kebalikan tepat target NOR. Tambahkan kartu pembalik NOT di keluaran gerbang OR: Y = NOT(A OR B).";
            }

            if (hasNand)
            {
                return $"Gerbang NAND pada {actualFormula} baru mati (0) saat A=1 dan B=1, sedangkan NOR hanya menyala (1) saat A=0 dan B=0. Gunakan OR diikuti NOT.";
            }

            return $"Rangkaian {actualFormula} belum membentuk NOR (hanya 1 saat A=0, B=0). Hubungkan Input A dan B ke OR lalu balik keluarannya dengan NOT.";
        }

        if (targetIsDeMorgan)
        {
            if (hasOr && actualFormula.Contains("(NOT A)"))
            {
                return "Posisi NOT terbalik! (NOT A) OR B menghasilkan 1 pada [A=0, B=1] dan 0 pada [A=1, B=0]. Pindahkan NOT ke jalur Input B.";
            }

            if (hasNand && actualFormula.Contains("(NOT B)"))
            {
                return "Pada ekuivalensi De Morgan dengan NAND, pembalik NOT harus dipasang di Input A ((NOT A) NAND B), bukan di Input B.";
            }

            if (hasOr && !hasNot)
            {
                return "Gerbang OR tanpa pembalik menghasilkan 0 pada [A=0, B=0]. Tambahkan kartu NOT di jalur Input B agar menjadi A OR (NOT B).";
            }

            if (hasNand && !hasNot)
            {
                return "Gerbang NAND saja belum mematikan baris [A=0, B=1]. Pasang kartu NOT di jalur Input A agar menjadi (NOT A) NAND B.";
            }

            if (hasOr && hasNot)
            {
                return "Posisi NOT membalik keseluruhan OR (menjadi NOR), padahal hanya Input B yang harus dibalik sebelum masuk ke OR.";
            }

            return $"Susunan {actualFormula} belum memenuhi target De Morgan (hanya 0 saat A=0, B=1). Gunakan A OR (NOT B) atau (NOT A) NAND B.";
        }

        if (!hasNot && !hasNand && !testA && !testB && !actualOutput && expectedOutput)
        {
            return $"Saat A=0 dan B=0, {actualFormula} keluar 0 padahal target 1. Tambahkan atau periksa posisi gerbang pembalik (NOT / NAND).";
        }

        if (targetIsXnor && !hasXor)
        {
            return $"Susunan {actualFormula} belum menguji kesamaan bit A dan B. Kombinasikan gerbang XOR diikuti pembalik NOT.";
        }

        return $"Susunan {actualFormula} menghasilkan {(actualOutput ? 1 : 0)} saat A={(testA ? 1 : 0)}, B={(testB ? 1 : 0)}. Periksa tipe gerbang dan posisi pembalik NOT.";
    }

    private int GetTotalStars()
    {
        if (levelBestStars == null || levelBestStars.Length == 0)
        {
            return 0;
        }

        int total = 0;
        for (int i = 0; i < levelBestStars.Length; i++)
        {
            total += levelBestStars[i];
        }

        return total;
    }

    private int GetMaxTotalStars()
    {
        int count = levels != null ? levels.Count : 0;
        return count * 3;
    }

    private static string FormatStarsAscii(int stars)
    {
        switch (Mathf.Clamp(stars, 0, 3))
        {
            case 3:
                return "[***]";
            case 2:
                return "[**.]";
            case 1:
                return "[*..]";
            default:
                return "[...]";
        }
    }

    private void UpdateStarMasteryUI()
    {
        if (txtStarMastery == null)
        {
            return;
        }

        EnsureLevelProgressArraysInitialized();
        int totalStars = GetTotalStars();
        int maxTotalStars = GetMaxTotalStars();

        if (isSandboxMode)
        {
            txtStarMastery.text = $"Mode Sandbox | Total Bintang: {totalStars}/{maxTotalStars}";
            txtStarMastery.color = MatrixBrightCyanColor;
            return;
        }

        int safeIndex = (levelBestStars != null && levelBestStars.Length > 0)
            ? Mathf.Clamp(currentLevelIndex, 0, levelBestStars.Length - 1)
            : -1;
        int currentBestStars = safeIndex >= 0 ? levelBestStars[safeIndex] : 0;

        txtStarMastery.text = $"Bintang Level: {FormatStarsAscii(currentBestStars)} | Total: {totalStars}/{maxTotalStars} | Uji ke-{attemptsOnCurrentLevel}";

        switch (currentBestStars)
        {
            case 3:
                txtStarMastery.color = new Color(1.00f, 0.86f, 0.25f, 1f);
                break;
            case 2:
                txtStarMastery.color = MatrixBrightCyanColor;
                break;
            case 1:
                txtStarMastery.color = new Color(1.00f, 0.65f, 0.28f, 1f);
                break;
            default:
                txtStarMastery.color = new Color(0.82f, 0.90f, 0.96f, 1f);
                break;
        }
    }

    /// <summary>
    /// Mengecek apakah seluruh level dalam daftar sudah pernah diselesaikan.
    /// </summary>
    private bool AreAllLevelsCompleted()
    {
        if (levelCompletedFlags == null || levelCompletedFlags.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < levelCompletedFlags.Length; i++)
        {
            if (!levelCompletedFlags[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Memperbarui teks dan gaya sci-fi tombol Toggle Input A dan Input B secara real-time sesuai nilai 0/1 saat ini.
    /// </summary>
    private void UpdateInputToggleButtonsUI()
    {
        bool needA = txtBtnToggleA != null && inputANode != null && ((inputANode.currentOutputValue ? 1 : 0) != lastDisplayedInputA);
        bool needB = txtBtnToggleB != null && inputBNode != null && ((inputBNode.currentOutputValue ? 1 : 0) != lastDisplayedInputB);
        if (!needA && !needB) return;

        ResolveActiveContextualLabels(currentLevelIndex, isSandboxMode,
            out string aName, out string a0, out string a1,
            out string bName, out string b0, out string b1,
            out _, out _, out _);

        if (needA)
        {
            int stateA = inputANode.currentOutputValue ? 1 : 0;
            lastDisplayedInputA = stateA;
            txtBtnToggleA.text = $"{aName}: {(stateA > 0 ? a1 : a0)} [{stateA}]";
            ApplyInputToggleVisualState(imgBtnInputA, outlineBtnInputA, txtBtnToggleA, stateA > 0, isInputB: false);
        }

        if (needB)
        {
            int stateB = inputBNode.currentOutputValue ? 1 : 0;
            lastDisplayedInputB = stateB;
            txtBtnToggleB.text = $"{bName}: {(stateB > 0 ? b1 : b0)} [{stateB}]";
            ApplyInputToggleVisualState(imgBtnInputB, outlineBtnInputB, txtBtnToggleB, stateB > 0, isInputB: true);
        }
    }

    /// <summary>
    /// Mengisi 7 skenario level berjenjang sesuai spesifikasi Kurikulum Merdeka Fase E.
    /// </summary>
    private void InitializeDefaultLevels()
    {
        levels = new List<LevelScenario>
        {
            // Level 1: Gerbang OR (Maks 1 Kartu) -> AB: 00=0, 01=1, 10=1, 11=1
            new LevelScenario
            {
                levelTitle = "Level 1: Alarm Pintu & Jendela",
                missionDescription = "Bunyikan ALARM jika PINTU atau JENDELA terbuka",
                maxGateCardsQuota = 1,
                expectedTruthTable = new bool[] { false, true, true, true },
                hintTier1Concept = "Output bernilai 1 jika minimal salah satu dari A atau B bernilai 1 (hanya 0 saat 00).",
                hintTier2Component = "Siapkan 1 kartu gerbang OR di antara kartu Input (A, B) dan Output.",
                hintTier3Structure = "Susun Input A dan B di kiri -> sambungkan keduanya ke OR -> arahkan OR ke Output.",
                inputAName = "PINTU", inputAState0 = "TUTUP", inputAState1 = "BUKA",
                inputBName = "JENDELA", inputBState0 = "TUTUP", inputBState1 = "BUKA",
                outputName = "ALARM", outputState0 = "AMAN", outputState1 = "BUNYI"
            },

            // Level 2: Gerbang XOR (Maks 1 Kartu) -> AB: 00=0, 01=1, 10=1, 11=0
            new LevelScenario
            {
                levelTitle = "Level 2: Sakelar Tangga Dua Arah",
                missionDescription = "Nyalakan LAMPU hanya jika sakelar ATAS dan BAWAH beda arah",
                maxGateCardsQuota = 1,
                expectedTruthTable = new bool[] { false, true, true, false },
                hintTier1Concept = "Output bernilai 1 hanya ketika A dan B BERBEDA (01 atau 10), dan 0 saat keduanya sama.",
                hintTier2Component = "Siapkan 1 kartu gerbang XOR (Exclusive OR) sebagai pembanding dua sinyal.",
                hintTier3Structure = "Susun Input A dan B di kiri -> sambungkan keduanya ke XOR -> arahkan XOR ke Output.",
                inputAName = "ATAS", inputAState0 = "KIRI", inputAState1 = "KANAN",
                inputBName = "BAWAH", inputBState0 = "KIRI", inputBState1 = "KANAN",
                outputName = "LAMPU", outputState0 = "PADAM", outputState1 = "NYALA"
            },

            // Level 3: Kombinasi A AND (NOT B) (Maks 2 Kartu) -> AB: 00=0, 01=0, 10=1, 11=0
            new LevelScenario
            {
                levelTitle = "Level 3: Kunci Keamanan Brankas",
                missionDescription = "Buka BRANKAS hanya saat KUNCI masuk dan DARURAT off",
                maxGateCardsQuota = 2,
                expectedTruthTable = new bool[] { false, false, true, false },
                hintTier1Concept = "Output hanya boleh 1 saat A=1 dan B=0, sehingga sinyal B perlu dibalik dahulu.",
                hintTier2Component = "Siapkan 2 kartu gerbang: NOT untuk membalik B, dan AND untuk menggabungkannya dengan A.",
                hintTier3Structure = "Sambungkan Input B -> NOT, lalu arahkan Input A dan keluaran NOT ke AND -> Output.",
                inputAName = "KUNCI", inputAState0 = "LEPAS", inputAState1 = "MASUK",
                inputBName = "DARURAT", inputBState0 = "OFF", inputBState1 = "ON",
                outputName = "BRANKAS", outputState0 = "KUNCI", outputState1 = "BUKA"
            },

            // Level 4: Gerbang NAND (Maks 1 Kartu) -> !(A && B): 00=1, 01=1, 10=1, 11=0
            new LevelScenario
            {
                levelTitle = "Level 4: Pemutus Darurat Reaktor",
                missionDescription = "Putus REAKTOR hanya saat SUHU panas dan TEKANAN tinggi",
                maxGateCardsQuota = 1,
                expectedTruthTable = new bool[] { true, true, true, false },
                hintTier1Concept = "Kebalikan dari AND: Output tetap 1 untuk 00, 01, 10 dan turun ke 0 hanya saat 11.",
                hintTier2Component = "Gunakan 1 kartu gerbang NAND (Not-AND) agar hemat kuota 1 komponen.",
                hintTier3Structure = "Susun Input A dan B di kiri -> sambungkan keduanya langsung ke NAND -> Output.",
                inputAName = "SUHU", inputAState0 = "NORMAL", inputAState1 = "PANAS",
                inputBName = "TEKANAN", inputBState0 = "OK", inputBState1 = "TINGGI",
                outputName = "REAKTOR", outputState0 = "PUTUS", outputState1 = "AKTIF"
            },

            // Level 5: Gerbang XNOR (XOR -> NOT, Maks 2 Kartu) -> (A == B): 00=1, 01=0, 10=0, 11=1
            new LevelScenario
            {
                levelTitle = "Level 5: Sinkronisasi Inti Kembar",
                missionDescription = "Buat SINKRON sama saat kecepatan INTI 1 dan INTI 2 sama",
                maxGateCardsQuota = 2,
                expectedTruthTable = new bool[] { true, false, false, true },
                hintTier1Concept = "Output bernilai 1 jika A dan B SAMA (00 atau 11), yaitu kebalikan tepat dari XOR.",
                hintTier2Component = "Kombinasikan 2 kartu gerbang: XOR diikuti gerbang pembalik NOT di sisi kanannya.",
                hintTier3Structure = "Sambungkan Input A dan B ke XOR -> sambungkan keluaran XOR ke NOT -> Output.",
                inputAName = "INTI 1", inputAState0 = "PELAN", inputAState1 = "CEPAT",
                inputBName = "INTI 2", inputBState0 = "PELAN", inputBState1 = "CEPAT",
                outputName = "SINKRON", outputState0 = "BEDA", outputState1 = "SAMA"
            },

            // Level 6: Gerbang NOR Kombinasional (OR -> NOT, Maks 2 Kartu) -> !(A || B): 00=1, 01=0, 10=0, 11=0
            new LevelScenario
            {
                levelTitle = "Level 6: Sensor Hening Ruang Server (NOR)",
                missionDescription = "Nyatakan SERVER aman hanya saat SUARA hening dan GERAK diam",
                maxGateCardsQuota = 2,
                expectedTruthTable = new bool[] { true, false, false, false },
                hintTier1Concept = "Gerbang NOR adalah kebalikan dari OR: Output bernilai 1 HANYA ketika kedua sensor A=0 dan B=0 (00).",
                hintTier2Component = "Siapkan 2 kartu gerbang: kartu OR dan kartu pembalik NOT.",
                hintTier3Structure = "Hubungkan Input A dan Input B ke gerbang OR, lalu arahkan keluaran OR ke NOT menuju Output: Y = NOT(A OR B).",
                inputAName = "SUARA", inputAState0 = "HENING", inputAState1 = "BISING",
                inputBName = "GERAK", inputBState0 = "DIAM", inputBState1 = "ADA",
                outputName = "SERVER", outputState0 = "WASPADA", outputState1 = "AMAN"
            },

            // Level 7 Boss Akhir: Hukum De Morgan (A OR NOT B == NOT A NAND B, Maks 2 Kartu) -> 00=1, 01=0, 10=1, 11=1
            new LevelScenario
            {
                levelTitle = "Level 7: Kunci Biometrik De Morgan",
                missionDescription = "Kunci LAB hanya saat KARTU kosong dan MODE ketat",
                maxGateCardsQuota = 2,
                expectedTruthTable = new bool[] { true, false, true, true },
                hintTier1Concept = "Menurut Hukum De Morgan, rumus A OR (NOT B) ekuivalen dengan (NOT A) NAND B. Output hanya 0 pada baris A=0, B=1.",
                hintTier2Component = "Gunakan kartu NOT bersama kartu OR, ATAU gunakan kartu NOT bersama kartu NAND (kuota 2 gerbang).",
                hintTier3Structure = "Jalur 1: Input B -> NOT, lalu gabungkan dengan Input A di OR. Jalur 2 (De Morgan): Input A -> NOT, lalu gabungkan dengan Input B di NAND.",
                inputAName = "KARTU", inputAState0 = "KOSONG", inputAState1 = "VALID",
                inputBName = "MODE", inputBState0 = "UMUM", inputBState1 = "KETAT",
                outputName = "LAB", outputState0 = "TERKUNCI", outputState1 = "TERBUKA"
            }
        };

        EnsureLevelProgressArraysInitialized();
    }

    private static int ResolveDioramaVariantForLevel(int levelIndex)
    {
        switch (levelIndex)
        {
            case 5:
                return 0; // Level 6 (Sensor Hening NOR) menggunakan menara sirine/sensor darurat (Diorama 0)
            case 6:
                return 2; // Level 7 (Kunci Biometrik De Morgan) menggunakan brankas mekanis berpelindung (Diorama 2)
            default:
                return levelIndex;
        }
    }

    private void ResolveActiveContextualLabels(
        int levelIndex,
        bool sandboxMode,
        out string inAName, out string inA0, out string inA1,
        out string inBName, out string inB0, out string inB1,
        out string outName, out string out0, out string out1)
    {
        if (sandboxMode)
        {
            inAName = "INPUT A"; inA0 = "OFF"; inA1 = "ON";
            inBName = "INPUT B"; inB0 = "OFF"; inB1 = "ON";
            outName = "OUTPUT"; out0 = "MATI"; out1 = "AKTIF";
            return;
        }

        LevelScenario scenario = (levels != null && levelIndex >= 0 && levelIndex < levels.Count)
            ? levels[levelIndex]
            : null;
        if (scenario != null && !string.IsNullOrEmpty(scenario.inputAName) && !string.IsNullOrEmpty(scenario.outputName))
        {
            inAName = scenario.inputAName; inA0 = scenario.inputAState0; inA1 = scenario.inputAState1;
            inBName = scenario.inputBName; inB0 = scenario.inputBState0; inB1 = scenario.inputBState1;
            outName = scenario.outputName; out0 = scenario.outputState0; out1 = scenario.outputState1;
            return;
        }

        // Fallback aman apabila daftar levels di-serialize di Inspector sebelum v7.0
        switch (levelIndex)
        {
            case 0:
                inAName = "PINTU"; inA0 = "TUTUP"; inA1 = "BUKA";
                inBName = "JENDELA"; inB0 = "TUTUP"; inB1 = "BUKA";
                outName = "ALARM"; out0 = "AMAN"; out1 = "BUNYI";
                break;
            case 1:
                inAName = "ATAS"; inA0 = "KIRI"; inA1 = "KANAN";
                inBName = "BAWAH"; inB0 = "KIRI"; inB1 = "KANAN";
                outName = "LAMPU"; out0 = "PADAM"; out1 = "NYALA";
                break;
            case 2:
                inAName = "KUNCI"; inA0 = "LEPAS"; inA1 = "MASUK";
                inBName = "DARURAT"; inB0 = "OFF"; inB1 = "ON";
                outName = "BRANKAS"; out0 = "KUNCI"; out1 = "BUKA";
                break;
            case 3:
                inAName = "SUHU"; inA0 = "NORMAL"; inA1 = "PANAS";
                inBName = "TEKANAN"; inB0 = "OK"; inB1 = "TINGGI";
                outName = "REAKTOR"; out0 = "PUTUS"; out1 = "AKTIF";
                break;
            case 4:
                inAName = "INTI 1"; inA0 = "PELAN"; inA1 = "CEPAT";
                inBName = "INTI 2"; inB0 = "PELAN"; inB1 = "CEPAT";
                outName = "SINKRON"; out0 = "BEDA"; out1 = "SAMA";
                break;
            case 5:
                inAName = "SUARA"; inA0 = "HENING"; inA1 = "BISING";
                inBName = "GERAK"; inB0 = "DIAM"; inB1 = "ADA";
                outName = "SERVER"; out0 = "WASPADA"; out1 = "AMAN";
                break;
            case 6:
                inAName = "KARTU"; inA0 = "KOSONG"; inA1 = "VALID";
                inBName = "MODE"; inB0 = "UMUM"; inB1 = "KETAT";
                outName = "LAB"; out0 = "TERKUNCI"; out1 = "TERBUKA";
                break;
            default:
                inAName = "INPUT A"; inA0 = "OFF"; inA1 = "ON";
                inBName = "INPUT B"; inB0 = "OFF"; inB1 = "ON";
                outName = "OUTPUT"; out0 = "MATI"; out1 = "AKTIF";
                break;
        }
    }

    private void ApplyContextualLabelsAndOutputPod(LevelScenario activeLevel, bool sandboxMode)
    {
        ResolveActiveContextualLabels(
            currentLevelIndex,
            sandboxMode,
            out string inAName, out string inA0, out string inA1,
            out string inBName, out string inB0, out string inB1,
            out string outName, out string out0, out string out1);

        if (inputANode != null)
        {
            inputANode.SetContextualLabelConfig(inAName, inA0, inA1);
        }

        if (inputBNode != null)
        {
            inputBNode.SetContextualLabelConfig(inBName, inB0, inB1);
        }

        if (outputNode != null)
        {
            outputNode.SetContextualLabelConfig(outName, out0, out1);
            if (outputNode.OutputTruthPod != null)
            {
                bool[] expected = (!sandboxMode && activeLevel != null) ? activeLevel.expectedTruthTable : null;
                outputNode.OutputTruthPod.ConfigureLevelTargets(expected, sandboxMode);
            }
        }
    }

    /// <summary>
    /// Memperbarui teks kuota gerbang secara real-time di setiap frame:
    /// Di Mode Sandbox bebas batas kuota, sedangkan di Level 1..7 merah bila melebihi kuota.
    /// </summary>
    private void UpdateQuotaStatusUI()
    {
        if (txtQuotaStatus == null || circuitManager == null)
        {
            return;
        }

        int activeCount = circuitManager.activeGateCardsCount;

        if (isSandboxMode)
        {
            if (lastQuotaSandboxMode == 1 && lastQuotaActiveCount == activeCount)
            {
                return;
            }

            lastQuotaSandboxMode = 1;
            lastQuotaActiveCount = activeCount;
            lastQuotaMaxCount = -1;

            bool supportsInfinityGlyph = txtQuotaStatus.font != null && txtQuotaStatus.font.HasCharacter('\u221E');
            string unlimitedSymbol = supportsInfinityGlyph ? "\u221E" : "Bebas";
            txtQuotaStatus.text = $"Kuota Gerbang: {activeCount} / {unlimitedSymbol}";
            txtQuotaStatus.color = new Color(0.55f, 1f, 0.92f, 1f);
            compactHudMenu?.SetQuotaAlertState(false);
            return;
        }

        if (levels == null || levels.Count == 0)
        {
            return;
        }

        int safeIndex = Mathf.Clamp(currentLevelIndex, 0, levels.Count - 1);
        LevelScenario currentLevel = levels[safeIndex];
        int maxQuota = currentLevel.maxGateCardsQuota;

        if (lastQuotaSandboxMode == 0 && lastQuotaActiveCount == activeCount && lastQuotaMaxCount == maxQuota)
        {
            return;
        }

        lastQuotaSandboxMode = 0;
        lastQuotaActiveCount = activeCount;
        lastQuotaMaxCount = maxQuota;

        bool isOverQuota = activeCount > maxQuota;
        txtQuotaStatus.text = $"Kuota Gerbang: {activeCount} / {maxQuota}";
        txtQuotaStatus.color = isOverQuota ? new Color(1.00f, 0.97f, 0.92f, 1f) : Color.white;
        compactHudMenu?.SetQuotaAlertState(isOverQuota);
    }

    /// <summary>
    /// Memuat data skenario level berdasarkan indeks, menyinkronkan varian diorama 3D OutputActuator,
    /// mereset matrix scanner, dan memperbarui seluruh komponen HUD UI.
    /// </summary>
    public void LoadLevel(int index)
    {
        StopAllCoroutines();
        isVerifying = false;
        isSandboxMode = false;
        awaitingPostVictoryChoice = false;
        attemptsOnCurrentLevel = 0;
        hintTierUsed = 0;

        if (inputANode != null)
        {
            inputANode.SetSimulationOverride(false);
        }

        if (inputBNode != null)
        {
            inputBNode.SetSimulationOverride(false);
        }

        if (levels == null || levels.Count == 0)
        {
            InitializeDefaultLevels();
        }
        else
        {
            EnsureLevelProgressArraysInitialized();
        }

        currentLevelIndex = Mathf.Clamp(index, 0, levels.Count - 1);
        LevelScenario currentLevel = levels[currentLevelIndex];

        if (outputNode == null)
        {
            AutoBindCoreNodesIfNeeded();
        }

        if (outputNode != null)
        {
            outputNode.SetOutputDioramaVariant(ResolveDioramaVariantForLevel(currentLevelIndex));
        }

        ApplyContextualLabelsAndOutputPod(currentLevel, sandboxMode: false);

        AutoBindActionButtonsIfNeeded();
        InitializeTruthTableMatrixUI();

        if (txtNextLevelButtonLabel != null)
        {
            txtNextLevelButtonLabel.text = "Level Berikutnya";
        }

        if (nextLevelButtonObj != null)
        {
            nextLevelButtonObj.SetActive(false);
        }

        if (txtResetLevelButtonLabel != null)
        {
            txtResetLevelButtonLabel.text = "Reset Level";
        }

        if (resetLevelButtonObj != null && compactHudMenu == null)
        {
            resetLevelButtonObj.SetActive(true);
        }

        isReadyForNextLevel = false;

        if (txtLevelTitle != null)
        {
            txtLevelTitle.text = $"[{currentLevelIndex + 1}/{levels.Count}] {currentLevel.levelTitle}";
        }

        if (txtMissionDesc != null)
        {
            txtMissionDesc.text = currentLevel.missionDescription;
            txtMissionDesc.color = Color.white;
        }

        if (compactHudMenu != null) { compactHudMenu.ClearToastImmediate(); compactHudMenu.RefreshAllVisualStates(); }
        else if (txtFeedback != null)
        {
            txtFeedback.text = "Susun kartu fisik di atas meja, lalu tekan tombol Uji Rangkaian.";
            txtFeedback.color = Color.white;
        }

        RefreshTruthTableTargets();
        ResetTruthTableScanResults();
        ResetWaveformSamples();
        UpdateQuotaStatusUI();
        lastDisplayedInputA = -1;
        lastDisplayedInputB = -1;
        UpdateInputToggleButtonsUI();
        UpdateStarMasteryUI();
        UpdateHintButtonUI();
        SyncAudioStateBaselines(true);

        if (airWeavingController != null && airWeavingController.IsAirWeavingActive)
        {
            airWeavingController.ResetAirWeavingWorkbench();
        }
    }

    /// <summary>
    /// Mengaktifkan Mode Sandbox Bebas tanpa batasan kuota dengan Diorama 4 (Twin Sync Cyber Obelisk).
    /// </summary>
    public void EnterSandboxMode()
    {
        StopAllCoroutines();
        isVerifying = false;
        isSandboxMode = true;
        awaitingPostVictoryChoice = false;
        attemptsOnCurrentLevel = 0;
        hintTierUsed = 0;

        if (inputANode != null)
        {
            inputANode.SetSimulationOverride(false);
        }

        if (inputBNode != null)
        {
            inputBNode.SetSimulationOverride(false);
        }

        if (outputNode == null)
        {
            AutoBindCoreNodesIfNeeded();
        }

        if (outputNode != null)
        {
            outputNode.SetOutputDioramaVariant(4);
        }

        ApplyContextualLabelsAndOutputPod(null, sandboxMode: true);

        AutoBindActionButtonsIfNeeded();
        InitializeTruthTableMatrixUI();

        if (nextLevelButtonObj != null)
        {
            nextLevelButtonObj.SetActive(false);
        }

        if (txtResetLevelButtonLabel != null)
        {
            txtResetLevelButtonLabel.text = "Main Ulang (Level 1)";
        }

        if (resetLevelButtonObj != null && compactHudMenu == null)
        {
            resetLevelButtonObj.SetActive(true);
        }

        isReadyForNextLevel = false;
        hasRenderedFormulaBaseline = false;

        if (txtLevelTitle != null)
        {
            txtLevelTitle.text = "[SANDBOX] Laboratorium Bebas";
        }

        if (txtMissionDesc != null)
        {
            txtMissionDesc.text = "Rumus: Y = (Belum Terhubung)";
            txtMissionDesc.color = MatrixBrightCyanColor;
        }

        if (compactHudMenu != null) { compactHudMenu.ClearToastImmediate(); compactHudMenu.RefreshAllVisualStates(); }
        else if (txtFeedback != null)
        {
            txtFeedback.text = "Mode Sandbox aktif. Susun gerbang apa pun lalu tekan Uji Rangkaian.";
            txtFeedback.color = Color.white;
        }

        RefreshTruthTableTargets();
        ResetTruthTableScanResults();
        ResetWaveformSamples();
        UpdateQuotaStatusUI();
        lastDisplayedInputA = -1;
        lastDisplayedInputB = -1;
        UpdateInputToggleButtonsUI();
        UpdateStarMasteryUI();
        UpdateHintButtonUI();
        SyncAudioStateBaselines(true);

        if (airWeavingController != null && airWeavingController.IsAirWeavingActive)
        {
            airWeavingController.ResetAirWeavingWorkbench();
        }
    }

    /// <summary>
    /// Berpindah ke skenario level berikutnya, atau masuk ke Mode Sandbox Bebas setelah seluruh misi selesai.
    /// </summary>
    public void NextLevel()
    {
        if (levels == null || levels.Count == 0)
        {
            return;
        }

        if (isSandboxMode)
        {
            RestartFromFirstLevel();
            return;
        }

        EnsureLevelProgressArraysInitialized();

        bool isLastLevelCleared = (currentLevelIndex == levels.Count - 1) && levelCompletedFlags[currentLevelIndex];
        if (awaitingPostVictoryChoice || AreAllLevelsCompleted() || isLastLevelCleared)
        {
            EnterSandboxMode();
            return;
        }

        int nextIndex = (currentLevelIndex + 1) % levels.Count;
        LoadLevel(nextIndex);
    }

    /// <summary>
    /// Mengulang misi aktif dari awal. Jika dipanggil setelah kemenangan Level 7 atau di Mode Sandbox,
    /// mengembalikan permainan ke Level 1 serta mereset progres dan nilai input.
    /// </summary>
    public void ResetCurrentLevel()
    {
        if (awaitingPostVictoryChoice || isSandboxMode)
        {
            RestartFromFirstLevel();
            return;
        }

        LoadLevel(currentLevelIndex);
    }

    private void RestartFromFirstLevel()
    {
        ClearPersistentProgress();
    }

    /// <summary>
    /// Dipanggil oleh tombol UI "Uji Rangkaian".
    /// Menjalankan Coroutine VerifyTruthTableRoutine() jika belum ada proses pemindaian yang berjalan.
    /// </summary>
    public void OnVerifyButtonClicked()
    {
        if (isVerifying || isSandboxMode)
        {
            return;
        }

        StartCoroutine(VerifyTruthTableRoutine());
    }

    /// <summary>
    /// Coroutine penguji otomatis 4 baris Tabel Kebenaran (AB: 00, 01, 10, 11)
    /// untuk Level 1..7 maupun pemindaian bebas di Mode Sandbox, terhubung langsung dengan Scanner Matrix UI.
    /// </summary>
    private IEnumerator VerifyTruthTableRoutine()
    {
        isVerifying = true;
        isReadyForNextLevel = false;
        awaitingPostVictoryChoice = false;
        compactHudMenu?.RefreshAllVisualStates();
        EnsureTruthTableVisibleForVerification();

        if (levels == null || levels.Count == 0)
        {
            InitializeDefaultLevels();
        }

        LevelScenario currentLevel = levels[Mathf.Clamp(currentLevelIndex, 0, levels.Count - 1)];

        if (circuitManager != null)
        {
            circuitManager.EvaluateCircuit();
        }

        // Tahap A: Validasi Kuota Kartu Gerbang (dilewati di Mode Sandbox Bebas)
        if (!isSandboxMode && circuitManager != null && circuitManager.activeGateCardsCount > currentLevel.maxGateCardsQuota)
        {
            if (txtFeedback != null)
            {
                txtFeedback.text = $"Gagal! Melebihi kuota maksimal gerbang ({circuitManager.activeGateCardsCount}/{currentLevel.maxGateCardsQuota}).";
                txtFeedback.color = Color.red;
            }

            PlaySfx(clipRowFail);
            SyncAudioStateBaselines(false);
            isVerifying = false;
            yield break;
        }

        // Tahap B: Validasi Koneksi Input, Gerbang, dan Output Actuator
        if (isSandboxMode)
        {
            bool anyInputTracked = (inputANode != null && inputANode.isTracked) || (inputBNode != null && inputBNode.isTracked);
            if (!anyInputTracked)
            {
                if (txtFeedback != null)
                {
                    txtFeedback.text = "Arahkan kamera ke minimal satu kartu Input (A/B) dan kartu Output untuk memindai.";
                    txtFeedback.color = new Color(1f, 0.5f, 0f);
                }

                PlaySfx(clipRowFail);
                SyncAudioStateBaselines(false);
                isVerifying = false;
                yield break;
            }
        }
        else
        {
            if (inputANode == null || !inputANode.isTracked || inputBNode == null || !inputBNode.isTracked)
            {
                if (txtFeedback != null)
                {
                    txtFeedback.text = "Gagal! Pastikan kartu Input A dan Input B keduanya sudah terdeteksi kamera.";
                    txtFeedback.color = new Color(1f, 0.5f, 0f);
                }

                PlaySfx(clipRowFail);
                SyncAudioStateBaselines(false);
                isVerifying = false;
                yield break;
            }

            bool isAirModeValidation = airWeavingController != null && airWeavingController.IsAirWeavingActive;
            if (circuitManager != null && circuitManager.allNodes != null)
            {
                for (int i = 0; i < circuitManager.allNodes.Count; i++)
                {
                    GateNode node = circuitManager.allNodes[i];
                    if (node != null && node.isTracked && node.nodeType != NodeType.InputSource && node.nodeType != NodeType.OutputActuator)
                    {
                        int requiredInputs = node.GetMaxInputCount();
                        int connectedValidInputs = 0;
                        if (node.connectedInputs != null)
                        {
                            for (int k = 0; k < node.connectedInputs.Count; k++)
                            {
                                if (node.connectedInputs[k] != null && node.connectedInputs[k].isTracked)
                                {
                                    connectedValidInputs++;
                                }
                            }
                        }
                        if (connectedValidInputs < requiredInputs)
                        {
                            if (txtFeedback != null)
                            {
                                string emptyPinHint = isAirModeValidation
                                    ? "Sambungkan kabel sumber ke slot kosong gerbang tersebut."
                                    : "Dekatkan kartu sumber.";
                                txtFeedback.text = $"Gagal! Slot pin gerbang {node.nodeType} masih kosong ('?') ({connectedValidInputs}/{requiredInputs}). {emptyPinHint}";
                                txtFeedback.color = new Color(1.00f, 0.96f, 0.93f, 1f);
                            }

                            PlaySfx(clipRowFail);
                            SyncAudioStateBaselines(false);
                            isVerifying = false;
                            yield break;
                        }
                    }
                }
            }

            if (circuitManager != null && circuitManager.activeGateCardsCount == 0)
            {
                if (txtFeedback != null)
                {
                    txtFeedback.text = isAirModeValidation
                        ? "Gagal! Belum ada gerbang logika terpasang di zona rakit."
                        : "Gagal! Pasang minimal 1 kartu gerbang logika di antara Input dan Output.";
                    txtFeedback.color = new Color(1.00f, 0.96f, 0.93f, 1f);
                }

                PlaySfx(clipRowFail);
                SyncAudioStateBaselines(false);
                isVerifying = false;
                yield break;
            }
        }

        if (outputNode == null || !outputNode.isTracked || outputNode.connectedInputs.Count == 0)
        {
            if (txtFeedback != null)
            {
                bool isAirModeOut = airWeavingController != null && airWeavingController.IsAirWeavingActive;
                txtFeedback.text = isAirModeOut
                    ? "Gagal! Output belum tersambung ke rangkaian — geser gerbang lebih dekat ke Output."
                    : "Gagal! Kartu Output belum terdeteksi kamera atau belum tersambung ke rangkaian.";
                txtFeedback.color = new Color(1.00f, 0.96f, 0.93f, 1f);
            }

            PlaySfx(clipRowFail);
            SyncAudioStateBaselines(false);
            isVerifying = false;
            yield break;
        }

        if (!isSandboxMode) { attemptsOnCurrentLevel++; UpdateStarMasteryUI(); }
        ResetTruthTableScanResults();

        bool originalInputA = inputANode != null && inputANode.initialInputState;
        bool originalInputB = inputBNode != null && inputBNode.initialInputState;

        if (inputANode != null)
        {
            inputANode.SetSimulationOverride(true);
        }

        if (inputBNode != null)
        {
            inputBNode.SetSimulationOverride(true);
        }

        bool[] scannedOutputs = new bool[TruthTableRowCount];

        for (int i = 0; i < TruthTableRowCount; i++)
        {
            bool testA = (i == 2 || i == 3);
            bool testB = (i == 1 || i == 3);

            SetTruthTableRowScanning(i);
            PlaySfx(clipScanRows[i]);

            if (inputANode != null)
            {
                inputANode.SetInputState(testA);
            }

            if (inputBNode != null)
            {
                inputBNode.SetInputState(testB);
            }

            if (circuitManager != null)
            {
                circuitManager.EvaluateCircuit();
            }

            SampleWaveformFromCurrentNodes();

            if (txtFeedback != null)
            {
                txtFeedback.text = isSandboxMode
                    ? $"Memindai baris {i + 1}/4: A={(testA ? 1 : 0)}, B={(testB ? 1 : 0)}..."
                    : $"Menguji baris {i + 1}/4: A={(testA ? 1 : 0)}, B={(testB ? 1 : 0)}...";
                txtFeedback.color = Color.yellow;
            }

            yield return new WaitForSeconds(0.225f);
            SampleWaveformFromCurrentNodes();
            yield return new WaitForSeconds(0.225f);

            if (circuitManager != null)
            {
                circuitManager.EvaluateCircuit();
            }

            bool actualOutput = outputNode.currentOutputValue;
            scannedOutputs[i] = actualOutput;

            bool expectedOutput = (!isSandboxMode && currentLevel.expectedTruthTable != null && i < currentLevel.expectedTruthTable.Length)
                                  && currentLevel.expectedTruthTable[i];
            bool rowPassed = isSandboxMode || (actualOutput == expectedOutput);

            SetTruthTableRowResult(i, actualOutput, rowPassed);

            if (!isSandboxMode && !rowPassed)
            {
                scanningRowIndex = -1;
                PlaySfx(clipRowFail);
                RestoreInitialInputStates(originalInputA, originalInputB);
                SampleWaveformFromCurrentNodes();
                SyncAudioStateBaselines(false);

                if (txtFeedback != null)
                {
                    ResolveActiveContextualLabels(
                        currentLevelIndex,
                        sandboxMode: false,
                        out string ctxAName, out string ctxA0, out string ctxA1,
                        out string ctxBName, out string ctxB0, out string ctxB1,
                        out string ctxOutName, out string ctxOut0, out string ctxOut1);
                    string stateA = testA ? ctxA1 : ctxA0;
                    string stateB = testB ? ctxB1 : ctxB0;
                    string actualOutText = actualOutput ? ctxOut1 : ctxOut0;
                    string expectedOutText = expectedOutput ? ctxOut1 : ctxOut0;
                    txtFeedback.text = $"Gagal saat {ctxAName}: {stateA} & {ctxBName}: {stateB} — {ctxOutName} masih {actualOutText}, seharusnya {expectedOutText}!";
                    txtFeedback.color = new Color(1.00f, 0.96f, 0.93f, 1f);
                }

                isVerifying = false;
                yield break;
            }

            PlaySfx(clipRowPass);
        }

        // Tahap D: Hasil Akhir Pemindaian / Verifikasi
        scanningRowIndex = -1;
        RestoreInitialInputStates(originalInputA, originalInputB);
        SampleWaveformFromCurrentNodes();
        SyncAudioStateBaselines(false);

        if (isSandboxMode)
        {
            if (txtFeedback != null)
            {
                string r0 = scannedOutputs[0] ? "1" : "0";
                string r1 = scannedOutputs[1] ? "1" : "0";
                string r2 = scannedOutputs[2] ? "1" : "0";
                string r3 = scannedOutputs[3] ? "1" : "0";
                txtFeedback.text = $"Hasil Pindai Sandbox -> [00:{r0}] [01:{r1}] [10:{r2}] [11:{r3}]";
                txtFeedback.color = new Color(0.35f, 1f, 0.85f, 1f);
            }

            isVerifying = false;
            yield break;
        }

        FinalizeLevelVictoryState(currentLevel, isManualDiscovery: false, out _, out _);

        yield return new WaitForSeconds(0.10f);
        PlaySfx(clipVictoryFanfare);
        TriggerHapticPulse();
        if (outputNode != null)
        {
            outputNode.TriggerVictoryCelebration();
        }

        isVerifying = false;
    }

    /// <summary>
    /// Jalur Dual-Path Mode Udara (v7.0 Fase 3): jika pemain menguji sendiri seluruh 4 kombinasi tuas hingga
    /// ke-4 sel Pod LED 2x2 menyala hijau pada rangkaian valid dalam batas kuota, level otomatis LULUS.
    /// </summary>
    private void TryEvaluateManualAutoWin(int rowIndex, bool measuredOutput, int activeGates)
    {
        if (isSandboxMode || isVerifying || isReadyForNextLevel || levels == null || levels.Count == 0)
        {
            return;
        }

        bool isAirMode = airWeavingController != null && airWeavingController.IsAirWeavingActive;
        if (!isAirMode)
        {
            return;
        }

        LevelScenario currentLevel = levels[Mathf.Clamp(currentLevelIndex, 0, levels.Count - 1)];
        if (currentLevel == null || currentLevel.expectedTruthTable == null || rowIndex < 0 || rowIndex >= currentLevel.expectedTruthTable.Length)
        {
            return;
        }

        if (measuredOutput == currentLevel.expectedTruthTable[rowIndex])
        {
            manualPassedRowMask |= (1 << rowIndex);
        }
        else
        {
            manualPassedRowMask &= ~(1 << rowIndex);
            return;
        }

        if (manualPassedRowMask != 0b1111)
        {
            return;
        }

        if (activeGates > currentLevel.maxGateCardsQuota)
        {
            if (!hasShownManualOverQuotaToast)
            {
                hasShownManualOverQuotaToast = true;
                PlaySfx(clipRowFail);
                if (txtFeedback != null)
                {
                    txtFeedback.text = $"Gagal! Semua 4 kasus benar, tetapi melebihi kuota maksimal gerbang ({activeGates}/{currentLevel.maxGateCardsQuota}).";
                    txtFeedback.color = new Color(1.00f, 0.96f, 0.93f, 1f);
                }
            }
            return;
        }

        StartCoroutine(CompleteManualAutoWinRoutine(currentLevel));
    }

    private IEnumerator CompleteManualAutoWinRoutine(LevelScenario currentLevel)
    {
        isVerifying = true;
        attemptsOnCurrentLevel++;
        UpdateStarMasteryUI();
        FinalizeLevelVictoryState(currentLevel, isManualDiscovery: true, out _, out _);

        yield return new WaitForSeconds(0.10f);
        PlaySfx(clipVictoryFanfare);
        TriggerHapticPulse();
        if (outputNode != null)
        {
            outputNode.TriggerVictoryCelebration();
        }

        isVerifying = false;
    }

    private void FinalizeLevelVictoryState(LevelScenario currentLevel, bool isManualDiscovery, out bool isLastLevel, out bool wasAlreadyCleared)
    {
        EnsureLevelProgressArraysInitialized();
        wasAlreadyCleared = (currentLevelIndex >= 0 && currentLevelIndex < levelCompletedFlags.Length)
                            && levelCompletedFlags[currentLevelIndex];
        if (currentLevelIndex >= 0 && currentLevelIndex < levelCompletedFlags.Length)
        {
            levelCompletedFlags[currentLevelIndex] = true;
        }

        bool withinQuota = circuitManager == null || circuitManager.activeGateCardsCount <= currentLevel.maxGateCardsQuota;
        bool applyTier3HintCap = !wasAlreadyCleared && hintTierUsed >= 3;
        int earnedStars = CalculateEarnedStars(attemptsOnCurrentLevel, withinQuota, applyTier3HintCap);
        if (currentLevelIndex >= 0 && currentLevelIndex < levelBestStars.Length && earnedStars > levelBestStars[currentLevelIndex])
        {
            levelBestStars[currentLevelIndex] = earnedStars;
        }
        SavePersistentProgress();
        UpdateStarMasteryUI();
        RefreshCompetencyReportUI();

        isLastLevel = (levels != null && levels.Count > 0 && currentLevelIndex == levels.Count - 1);
        bool isGameCleared = AreAllLevelsCompleted() || isLastLevel;

        AutoBindActionButtonsIfNeeded();

        isReadyForNextLevel = true;
        if (isGameCleared)
        {
            awaitingPostVictoryChoice = true;
            if (txtFeedback != null)
            {
                txtFeedback.text = $"SELAMAT! Seluruh {levels.Count} Misi Selesai! Rating: {FormatStarsAscii(earnedStars)} (Total: {GetTotalStars()}/{GetMaxTotalStars()})";
                txtFeedback.color = Color.green;
            }
            if (txtNextLevelButtonLabel != null) txtNextLevelButtonLabel.text = "Mode Sandbox Bebas";
            if (compactHudMenu == null && nextLevelButtonObj != null) nextLevelButtonObj.SetActive(true);
            if (txtResetLevelButtonLabel != null) txtResetLevelButtonLabel.text = "Main Ulang (Level 1)";
            if (compactHudMenu == null && resetLevelButtonObj != null) resetLevelButtonObj.SetActive(true);
        }
        else
        {
            awaitingPostVictoryChoice = false;
            if (txtFeedback != null)
            {
                txtFeedback.text = isManualDiscovery
                    ? $"BERHASIL! Semua 4 kasus teruji manual & raih {FormatStarsAscii(earnedStars)}!"
                    : $"BERHASIL! Lolos 4 baris & raih {FormatStarsAscii(earnedStars)} (Uji ke-{attemptsOnCurrentLevel}).";
                txtFeedback.color = Color.green;
            }
            if (txtNextLevelButtonLabel != null) txtNextLevelButtonLabel.text = "Level Berikutnya";
            if (compactHudMenu == null && nextLevelButtonObj != null) nextLevelButtonObj.SetActive(true);
        }
        compactHudMenu?.RefreshAllVisualStates();
    }

    private void RestoreInitialInputStates(bool stateA, bool stateB)
    {
        if (inputANode != null)
        {
            inputANode.SetSimulationOverride(false);
            inputANode.SetInputState(stateA);
        }

        if (inputBNode != null)
        {
            inputBNode.SetSimulationOverride(false);
            inputBNode.SetInputState(stateB);
        }

        if (circuitManager != null)
        {
            circuitManager.EvaluateCircuit();
        }
    }

    /// <summary>
    /// Helper opsional untuk tombol UI Toggle Input A (dinonaktifkan sementara saat sedang verifikasi).
    /// </summary>
    public void OnToggleInputAClicked()
    {
        if (!isVerifying && inputANode != null)
        {
            inputANode.ToggleInputState();
        }
    }

    /// <summary>
    /// Helper opsional untuk tombol UI Toggle Input B (dinonaktifkan sementara saat sedang verifikasi).
    /// </summary>
    public void OnToggleInputBClicked()
    {
        if (!isVerifying && inputBNode != null)
        {
            inputBNode.ToggleInputState();
        }
    }

    /// <summary>
    /// Mendeteksi sentuhan layar Android maupun klik mouse Editor langsung pada hologram kartu 3D AR
    /// menggunakan New Input System, proteksi raycast UI 2D, dan Physics.RaycastNonAlloc tanpa alokasi GC.
    /// </summary>
    private void Handle3DCardTouchRaycast()
    {
        if (!TryGetTapScreenPosition(out Vector2 screenPosition))
        {
            return;
        }

        if (IsUiOverlayOpen)
        {
            return;
        }

        if (IsScreenPointOverUI(screenPosition))
        {
            return;
        }

        Camera mainCam = ResolveMainCamera();
        if (mainCam == null)
        {
            return;
        }

        Ray touchRay = mainCam.ScreenPointToRay(screenPosition);
        int hitCount = Physics.RaycastNonAlloc(
            touchRay,
            touchRaycastHits,
            TouchRaycastMaxDistance,
            ~0,
            QueryTriggerInteraction.Collide);

        if (hitCount <= 0)
        {
            return;
        }

        GateNode nearestNode = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = touchRaycastHits[i].collider;
            if (hitCollider == null)
            {
                continue;
            }

            GateNode candidate = hitCollider.GetComponentInParent<GateNode>();
            if (candidate != null && candidate.isTracked && touchRaycastHits[i].distance < nearestDistance)
            {
                nearestDistance = touchRaycastHits[i].distance;
                nearestNode = candidate;
            }
        }

        if (nearestNode == null)
        {
            return;
        }

        Dispatch3DCardTouchInteraction(nearestNode);
    }

    private void Dispatch3DCardTouchInteraction(GateNode touchedNode)
    {
        if (touchedNode.nodeType == NodeType.InputSource)
        {
            if (isVerifying)
            {
                return;
            }

            bool isInputB = touchedNode == inputBNode
                            || (touchedNode != inputANode && touchedNode.gameObject.name.ToUpperInvariant().Contains("B"));

            if (isInputB)
            {
                if (inputBNode != null)
                {
                    OnToggleInputBClicked();
                }
                else
                {
                    touchedNode.ToggleInputState();
                }
            }
            else
            {
                if (inputANode != null)
                {
                    OnToggleInputAClicked();
                }
                else
                {
                    touchedNode.ToggleInputState();
                }
            }

            touchedNode.TriggerTapFeedback();
            return;
        }

        if (touchedNode.nodeType == NodeType.OutputActuator)
        {
            touchedNode.TriggerTapFeedback();
            if (isVerifying)
            {
                return;
            }

            if (!isSandboxMode)
            {
                if (isReadyForNextLevel) NextLevel();
                else OnVerifyButtonClicked();
            }
            else
            {
                PlaySfx(clipToggleInput);
                if (txtFeedback != null)
                {
                    string formula = circuitManager != null
                        ? circuitManager.GetCircuitBooleanExpression(outputNode)
                        : "Y = (Belum Terhubung)";
                    string outState = touchedNode.currentOutputValue ? "1 [AKTIF]" : "0 [MATI]";
                    txtFeedback.text = $"[Sentuh 3D Sandbox] {formula} | Output Y = {outState}";
                    txtFeedback.color = new Color(0.35f, 0.96f, 1.00f, 1f);
                }
            }
            return;
        }

        // Kartu Gerbang Logika (AND, OR, XOR, NAND, NOT)
        touchedNode.TriggerTapFeedback();
        if (!isVerifying)
        {
            PlaySfx(clipToggleInput);
            if (txtFeedback != null)
            {
                txtFeedback.text = touchedNode.GetTouchInspectionSummary();
                txtFeedback.color = new Color(0.35f, 0.96f, 1.00f, 1f);
            }
        }
    }

    private static bool TryGetTapScreenPosition(out Vector2 screenPosition)
    {
#if ENABLE_INPUT_SYSTEM
        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
        {
            screenPosition = touchscreen.primaryTouch.position.ReadValue();
            return true;
        }
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            screenPosition = Mouse.current.position.ReadValue();
            return true;
        }
#endif
        screenPosition = Vector2.zero;
        return false;
    }

    private Camera ResolveMainCamera()
    {
        if (cachedMainCamera == null || !cachedMainCamera.isActiveAndEnabled)
        {
            cachedMainCamera = Camera.main;
        }

        return cachedMainCamera;
    }

    private bool IsScreenPointOverUI(Vector2 screenPosition)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            return false;
        }

        if (cachedPointerEventData == null)
        {
            cachedPointerEventData = new PointerEventData(eventSystem);
        }

        cachedPointerEventData.position = screenPosition;
        uiRaycastResults.Clear();
        eventSystem.RaycastAll(cachedPointerEventData, uiRaycastResults);
        bool isOverUI = uiRaycastResults.Count > 0;
        uiRaycastResults.Clear();
        return isOverUI;
    }
}
