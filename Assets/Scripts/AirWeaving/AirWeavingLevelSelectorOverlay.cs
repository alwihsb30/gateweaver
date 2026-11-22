using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Peta Progresi Sirkuit 7 Level khusus Mode Udara GateWeaver AR v8.5 (05_Level_Selector).
/// - Desain Smoked Optical Glass + Brushed Titanium 100% berbasis GwThemeTokens:
///   * Overlay penuh kaca asap SmokedGlassDense (82%) + pemblokir raycast penuh.
///   * Header atas: PILIH LEVEL // CIRCUIT MAP, pil progres X/7 SELESAI, dan tombol X KELUAR.
///   * Area Kiri-Tengah: 2 baris peta sirkuit (01-04 atas, 05-07 bawah) dengan garis ortogonal 1px bersih,
///     penanda centang geometris StatusPassText (#34D399) untuk level lulus, &amp; sorotan SignalHighTeal (#38BDF8) + titik aktif.
///   * Panel Detail Kanan (W = 404px): nomor &amp; judul level, misi aktual LevelAndUIManager, kuota gerbang,
///     gerbang tersedia, status (SELESAI / AKTIF / TERKUNCI), tombol primer solid putih MULAI SIMULASI -&gt;,
///     serta tombol outline SANDBOX MODE.
///   * Proteksi Pedagogis (Anti-Bocoran): rumus Boolean terverifikasi hanya tampil jika level sudah lulus;
///     level belum lulus menampilkan status TERKUNCI - RAKIT UNTUK VERIFIKASI tanpa membocorkan jawaban.
/// - 0 perubahan di Core/ &amp; Levels/, 0% regresi Mode Kartu Vuforia v5.
/// - 100% Zero-GC di Tick(), pure ASCII 7-bit, tanpa LINQ, tanpa neon glow.
/// </summary>
public sealed class AirWeavingLevelSelectorOverlay : MonoBehaviour
{
    private const int CurriculumLevelCount = 7;
    private const int OverlayCanvasSortingOrder = 35;
    private const float ReferenceCanvasWidth = 1920f;
    private const float ReferenceCanvasHeight = 1080f;
    private const float ContentWidth = 1720f;
    private const float ContentHeight = 900f;
    private const float HeaderHeight = 68f;
    private const float BodyTopOffset = 94f;
    private const float BodyHeight = 776f;
    private const float DetailPanelWidth = 404f;
    private const float BodyColumnGap = 28f;
    private const float MapPanelWidth = ContentWidth - DetailPanelWidth - BodyColumnGap;

    private static readonly string[] CachedLevelPrefKeys =
    {
        "GW_v3_LevelCleared_0",
        "GW_v3_LevelCleared_1",
        "GW_v3_LevelCleared_2",
        "GW_v3_LevelCleared_3",
        "GW_v3_LevelCleared_4",
        "GW_v3_LevelCleared_5",
        "GW_v3_LevelCleared_6"
    };

    private static readonly string[] CachedLevelNumbers =
    {
        "01", "02", "03", "04", "05", "06", "07"
    };

    private static readonly string[] CachedLevelShortTitles =
    {
        "Alarm Pintu & Jendela",
        "Sakelar Tangga Dua Arah",
        "Kunci Keamanan Brankas",
        "Pemutus Darurat Reaktor",
        "Sinkronisasi Inti Kembar",
        "Sensor Hening Server (NOR)",
        "Kunci Biometrik De Morgan"
    };

    private static readonly string[] CachedFallbackMissions =
    {
        "Bunyikan ALARM jika PINTU atau JENDELA terbuka",
        "Nyalakan LAMPU hanya jika sakelar ATAS dan BAWAH beda arah",
        "Buka BRANKAS hanya saat KUNCI masuk dan DARURAT off",
        "Putus REAKTOR hanya saat SUHU panas dan TEKANAN tinggi",
        "Buat SINKRON sama saat kecepatan INTI 1 dan INTI 2 sama",
        "Nyatakan SERVER aman hanya saat SUARA hening dan GERAK diam",
        "Kunci LAB hanya saat KARTU kosong dan MODE ketat"
    };

    private static readonly int[] CachedFallbackQuotas = { 1, 1, 2, 1, 2, 2, 2 };

    // Rumus terverifikasi HANYA dirender pada Panel Detail jika GW_v3_LevelCleared_i == 1 (Proteksi Pedagogis)
    private static readonly string[] CachedVerifiedFormulas =
    {
        "Y = A OR B",
        "Y = A XOR B",
        "Y = A AND (NOT B)",
        "Y = A NAND B",
        "Y = NOT (A XOR B)",
        "Y = NOT (A OR B)",
        "Y = A OR (NOT B)  ==  (NOT A) NAND B"
    };

    private static readonly string[] CachedAvailableGateNames =
    {
        "AND", "OR", "NOT", "XOR", "NAND"
    };

    private const string StrFormulaLocked = "TERKUNCI - RAKIT UNTUK VERIFIKASI";
    private const string StrStatusCleared = "SELESAI";
    private const string StrStatusActive = "AKTIF";
    private const string StrStatusLocked = "TERKUNCI";

    private sealed class MapNodeCardUi
    {
        public Button button;
        public UnityEngine.Events.UnityAction clickHandler;
        public Image bgImage;
        public Image borderImage;
        public Image activeDotImage;
        public Image topAccentBar;
        public GameObject checkIconRoot;
        public TMP_Text txtNumber;
        public TMP_Text txtTitle;
        public TMP_Text txtQuota;
        public TMP_Text txtStatus;
    }

    private LevelAndUIManager levelManager;
    private CompactHudMenuController compactHudMenu;
    private AirWeavingSpatialSfx spatialSfx;
    private TMP_FontAsset safeFont;

    private GameObject overlayRootObj;
    private RectTransform safeAreaRect;
    private RectTransform contentContainerRect;

    private Image progressDotImg;
    private TMP_Text txtProgressPill;
    private Button btnExitX;

    private readonly MapNodeCardUi[] nodeCards = new MapNodeCardUi[CurriculumLevelCount];
    // Garis konektor ortogonal 1px antar node:
    // [0]=01->02, [1]=02->03, [2]=03->04, [3]=04->05 (3 segmen ortogonal), [4]=05->06, [5]=06->07
    private Image conn01To02;
    private Image conn02To03;
    private Image conn03To04;
    private Image conn04DropV;
    private Image conn04MidH;
    private Image conn05EntryV;
    private Image conn05To06;
    private Image conn06To07;

    // Panel Detail Kanan
    private TMP_Text txtDetailLevelTag;
    private Image detailStatusPillBg;
    private Image detailStatusPillBorder;
    private Image detailStatusDot;
    private TMP_Text txtDetailStatus;
    private TMP_Text txtDetailTitle;
    private TMP_Text txtDetailMission;
    private TMP_Text txtDetailQuotaValue;
    private TMP_Text txtDetailIoSummary;
    private Image detailFormulaCardBorder;
    private Image detailFormulaLockDot;
    private TMP_Text txtDetailFormulaHeading;
    private TMP_Text txtDetailFormulaValue;
    private Button btnStartSimulation;
    private Button btnEnterSandbox;

    private readonly StringBuilder sb = new StringBuilder(128);

    private bool isUiBuilt = false;
    private bool isAirWeavingActive = false;
    private bool isSelectorOpen = false;
    private int selectedLevelIndex = 0;

    private Rect lastAppliedSafeArea;
    private int lastScreenW = -1;
    private int lastScreenH = -1;
    private bool hasAppliedScale = false;

    public bool IsSelectorOpen => isSelectorOpen;

    public void Configure(
        LevelAndUIManager manager,
        CompactHudMenuController hudMenu,
        AirWeavingSpatialSfx sfx,
        TMP_FontAsset fallbackFont = null)
    {
        levelManager = manager;
        compactHudMenu = hudMenu;
        spatialSfx = sfx;

        if (compactHudMenu != null && compactHudMenu.SafeFont != null)
        {
            safeFont = compactHudMenu.SafeFont;
        }

        if (safeFont == null)
        {
            safeFont = fallbackFont != null ? fallbackFont : TMP_Settings.defaultFontAsset;
        }
    }

    public void OnAirWeavingModeEntered()
    {
        isAirWeavingActive = true;
        EnsureUiBuilt();
        CloseOverlay();
    }

    public void OnAirWeavingModeExited()
    {
        isAirWeavingActive = false;
        CloseOverlay();
    }

    public void OnExternalModalOrMenuOpened()
    {
        if (isSelectorOpen)
        {
            CloseOverlay();
        }
    }

    public void OpenOverlay()
    {
        if (!isAirWeavingActive)
        {
            return;
        }

        EnsureUiBuilt();
        if (overlayRootObj == null)
        {
            return;
        }

        if (levelManager != null)
        {
            selectedLevelIndex = Mathf.Clamp(levelManager.currentLevelIndex, 0, CurriculumLevelCount - 1);
        }
        else
        {
            selectedLevelIndex = Mathf.Clamp(selectedLevelIndex, 0, CurriculumLevelCount - 1);
        }

        if (compactHudMenu != null)
        {
            compactHudMenu.CloseMenuDrawer();
            compactHudMenu.ClearToastImmediate();
        }

        if (spatialSfx != null)
        {
            spatialSfx.StopHumImmediate();
        }

        isSelectorOpen = true;
        overlayRootObj.SetActive(true);
        ApplySafeAreaAndResponsiveScaleIfNeeded(force: true);
        RefreshAllVisualStates();
    }

    public void CloseOverlay()
    {
        isSelectorOpen = false;
        if (overlayRootObj != null && overlayRootObj.activeSelf)
        {
            overlayRootObj.SetActive(false);
        }
    }

    /// <summary>
    /// Menjaga skala responsif &amp; redaman hum audio selama Peta Level terbuka. 100% Zero-GC.
    /// </summary>
    public void Tick()
    {
        if (!isSelectorOpen)
        {
            return;
        }

        if (!isAirWeavingActive)
        {
            CloseOverlay();
            return;
        }

        ApplySafeAreaAndResponsiveScaleIfNeeded(force: false);
        if (spatialSfx != null)
        {
            spatialSfx.StopHumImmediate();
        }
    }

    public void CleanupImmediate()
    {
        RemoveBtnListener(btnExitX, HandleExitClicked);
        RemoveBtnListener(btnStartSimulation, HandleStartSimulationClicked);
        RemoveBtnListener(btnEnterSandbox, HandleSandboxClicked);

        for (int i = 0; i < nodeCards.Length; i++)
        {
            MapNodeCardUi card = nodeCards[i];
            if (card != null)
            {
                RemoveBtnListener(card.button, card.clickHandler);
                nodeCards[i] = null;
            }
        }

        isAirWeavingActive = false;
        isSelectorOpen = false;

        if (overlayRootObj != null)
        {
            Destroy(overlayRootObj);
            overlayRootObj = null;
        }

        safeAreaRect = null;
        contentContainerRect = null;
        progressDotImg = null;
        txtProgressPill = null;
        btnExitX = null;
        conn01To02 = null;
        conn02To03 = null;
        conn03To04 = null;
        conn04DropV = null;
        conn04MidH = null;
        conn05EntryV = null;
        conn05To06 = null;
        conn06To07 = null;
        txtDetailLevelTag = null;
        detailStatusPillBg = null;
        detailStatusPillBorder = null;
        detailStatusDot = null;
        txtDetailStatus = null;
        txtDetailTitle = null;
        txtDetailMission = null;
        txtDetailQuotaValue = null;
        txtDetailIoSummary = null;
        detailFormulaCardBorder = null;
        detailFormulaLockDot = null;
        txtDetailFormulaHeading = null;
        txtDetailFormulaValue = null;
        btnStartSimulation = null;
        btnEnterSandbox = null;
        levelManager = null;
        compactHudMenu = null;
        spatialSfx = null;
        safeFont = null;
        isUiBuilt = false;
        hasAppliedScale = false;
    }

    private void OnDestroy()
    {
        CleanupImmediate();
    }

    // =========================================================================
    // AKSI TOMBOL & PILIHAN NODE
    // =========================================================================

    private void HandleExitClicked()
    {
        CloseOverlay();
    }

    private void HandleNodeCardClicked(int levelIndex)
    {
        selectedLevelIndex = Mathf.Clamp(levelIndex, 0, CurriculumLevelCount - 1);
        RefreshAllVisualStates();
    }

    private void HandleStartSimulationClicked()
    {
        if (levelManager != null && levelManager.IsVerifying)
        {
            return;
        }

        int targetIndex = Mathf.Clamp(selectedLevelIndex, 0, CurriculumLevelCount - 1);
        CloseOverlay();
        if (levelManager != null)
        {
            levelManager.LoadLevel(targetIndex);
        }
    }

    private void HandleSandboxClicked()
    {
        if (levelManager != null && levelManager.IsVerifying)
        {
            return;
        }

        CloseOverlay();
        if (levelManager != null)
        {
            levelManager.EnterSandboxMode();
        }
    }

    // =========================================================================
    // REFRESH STATE DINAMIS (EVENT-DRIVEN SAAT BUKA / PILIH NODE, ZERO-GC)
    // =========================================================================

    private void RefreshAllVisualStates()
    {
        bool isSandbox = levelManager != null && levelManager.IsSandboxMode;
        int activeGameplayLevel = levelManager != null ? levelManager.currentLevelIndex : 0;
        int clearedCount = 0;

        for (int i = 0; i < CurriculumLevelCount; i++)
        {
            bool isCleared = IsLevelCleared(i);
            if (isCleared)
            {
                clearedCount++;
            }

            bool isSelected = i == selectedLevelIndex;
            bool isCurrentGameplay = !isSandbox && i == activeGameplayLevel;
            RefreshSingleNodeCard(i, isCleared, isSelected, isCurrentGameplay);
        }

        // Pil progres header: X/7 SELESAI
        if (txtProgressPill != null)
        {
            sb.Clear();
            sb.Append(clearedCount).Append("/7 SELESAI");
            txtProgressPill.SetText(sb);
            txtProgressPill.color = clearedCount > 0 ? GwThemeTokens.StatusPassText : GwThemeTokens.TextSecondary;
        }

        if (progressDotImg != null)
        {
            progressDotImg.color = clearedCount > 0 ? GwThemeTokens.StatusPassEmerald : GwThemeTokens.SignalLowSlate;
        }

        // Garis penghubung ortogonal 1px (aksen hijau emerald bila node sumber sudah lulus)
        Color c01 = ResolveConnectorColor(0);
        Color c02 = ResolveConnectorColor(1);
        Color c03 = ResolveConnectorColor(2);
        Color c04 = ResolveConnectorColor(3);
        Color c05 = ResolveConnectorColor(4);
        Color c06 = ResolveConnectorColor(5);

        SetImageColorIfChanged(conn01To02, c01);
        SetImageColorIfChanged(conn02To03, c02);
        SetImageColorIfChanged(conn03To04, c03);
        SetImageColorIfChanged(conn04DropV, c04);
        SetImageColorIfChanged(conn04MidH, c04);
        SetImageColorIfChanged(conn05EntryV, c04);
        SetImageColorIfChanged(conn05To06, c05);
        SetImageColorIfChanged(conn06To07, c06);

        RefreshDetailPanel(selectedLevelIndex, isSandbox, activeGameplayLevel);
    }

    private Color ResolveConnectorColor(int fromLevelIndex)
    {
        if (IsLevelCleared(fromLevelIndex))
        {
            return GwThemeTokens.StatusPassEmerald;
        }

        if (selectedLevelIndex == fromLevelIndex || selectedLevelIndex == fromLevelIndex + 1)
        {
            return GwThemeTokens.HairlineActive;
        }

        return GwThemeTokens.HairlineIdle;
    }

    private void RefreshSingleNodeCard(int index, bool isCleared, bool isSelected, bool isCurrentGameplay)
    {
        MapNodeCardUi card = nodeCards[index];
        if (card == null)
        {
            return;
        }

        if (card.bgImage != null)
        {
            card.bgImage.color = isSelected ? GwThemeTokens.SmokedGlass : GwThemeTokens.CardIdle;
        }

        if (card.borderImage != null)
        {
            if (isSelected)
            {
                card.borderImage.color = GwThemeTokens.SignalHighTeal;
            }
            else if (isCleared)
            {
                card.borderImage.color = GwThemeTokens.StatusPassEmerald;
            }
            else
            {
                card.borderImage.color = GwThemeTokens.HairlineIdle;
            }
        }

        if (card.topAccentBar != null)
        {
            bool showBar = isSelected || isCleared;
            if (card.topAccentBar.enabled != showBar)
            {
                card.topAccentBar.enabled = showBar;
            }
            if (showBar)
            {
                card.topAccentBar.color = isSelected ? GwThemeTokens.SignalHighTeal : GwThemeTokens.StatusPassEmerald;
            }
        }

        if (card.activeDotImage != null)
        {
            bool showDot = isSelected || isCurrentGameplay;
            if (card.activeDotImage.enabled != showDot)
            {
                card.activeDotImage.enabled = showDot;
            }
            if (showDot)
            {
                card.activeDotImage.color = GwThemeTokens.SignalHighTeal;
            }
        }

        if (card.checkIconRoot != null && card.checkIconRoot.activeSelf != isCleared)
        {
            card.checkIconRoot.SetActive(isCleared);
        }

        if (card.txtNumber != null)
        {
            card.txtNumber.color = isSelected
                ? GwThemeTokens.SignalHighTeal
                : (isCleared ? GwThemeTokens.StatusPassText : GwThemeTokens.TextSecondary);
        }

        if (card.txtTitle != null)
        {
            card.txtTitle.color = (isSelected || isCleared)
                ? GwThemeTokens.TextPrimary
                : GwThemeTokens.TextSecondary;
            WriteCleanLevelTitleToBuilder(index, sb);
            card.txtTitle.SetText(sb);
        }

        int quota = ResolveLevelQuota(index);
        if (card.txtQuota != null)
        {
            sb.Clear();
            sb.Append("KUOTA: ").Append(quota);
            card.txtQuota.SetText(sb);
            card.txtQuota.color = GwThemeTokens.TextSecondary;
        }

        if (card.txtStatus != null)
        {
            if (isCleared)
            {
                card.txtStatus.text = StrStatusCleared;
                card.txtStatus.color = GwThemeTokens.StatusPassText;
            }
            else if (isCurrentGameplay)
            {
                card.txtStatus.text = StrStatusActive;
                card.txtStatus.color = GwThemeTokens.SignalHighTeal;
            }
            else
            {
                card.txtStatus.text = StrStatusLocked;
                card.txtStatus.color = GwThemeTokens.SignalLowSlate;
            }
        }
    }

    private void RefreshDetailPanel(int index, bool isSandbox, int activeGameplayLevel)
    {
        int safeIdx = Mathf.Clamp(index, 0, CurriculumLevelCount - 1);
        bool isCleared = IsLevelCleared(safeIdx);
        bool isCurrentGameplay = !isSandbox && safeIdx == activeGameplayLevel;
        LevelScenario scenario = GetScenario(safeIdx);

        if (txtDetailLevelTag != null)
        {
            sb.Clear();
            sb.Append("LEVEL ").Append(CachedLevelNumbers[safeIdx]).Append(" // DETAIL MISI");
            txtDetailLevelTag.SetText(sb);
        }

        // Badge Status (SELESAI / AKTIF / TERKUNCI)
        if (txtDetailStatus != null)
        {
            if (isCleared)
            {
                txtDetailStatus.text = StrStatusCleared;
                txtDetailStatus.color = GwThemeTokens.StatusPassText;
                SetImageColorIfChanged(detailStatusDot, GwThemeTokens.StatusPassEmerald);
                SetImageColorIfChanged(detailStatusPillBorder, GwThemeTokens.StatusPassEmerald);
                SetImageColorIfChanged(detailStatusPillBg, GwThemeTokens.SmokedGlass);
            }
            else if (isCurrentGameplay)
            {
                txtDetailStatus.text = StrStatusActive;
                txtDetailStatus.color = GwThemeTokens.SignalHighTeal;
                SetImageColorIfChanged(detailStatusDot, GwThemeTokens.SignalHighTeal);
                SetImageColorIfChanged(detailStatusPillBorder, GwThemeTokens.SignalHighTeal);
                SetImageColorIfChanged(detailStatusPillBg, GwThemeTokens.SmokedGlass);
            }
            else
            {
                txtDetailStatus.text = StrStatusLocked;
                txtDetailStatus.color = GwThemeTokens.TextSecondary;
                SetImageColorIfChanged(detailStatusDot, GwThemeTokens.StatusErrorRed);
                SetImageColorIfChanged(detailStatusPillBorder, GwThemeTokens.HairlineIdle);
                SetImageColorIfChanged(detailStatusPillBg, GwThemeTokens.CardIdle);
            }
        }

        if (txtDetailTitle != null)
        {
            WriteCleanLevelTitleToBuilder(safeIdx, sb);
            txtDetailTitle.SetText(sb);
        }

        if (txtDetailMission != null)
        {
            string mission = (scenario != null && !string.IsNullOrEmpty(scenario.missionDescription))
                ? scenario.missionDescription
                : CachedFallbackMissions[safeIdx];
            txtDetailMission.text = mission;
        }

        int quota = ResolveLevelQuota(safeIdx);
        if (txtDetailQuotaValue != null)
        {
            sb.Clear();
            sb.Append("MAKS ").Append(quota).Append(" GERBANG LOGIKA");
            txtDetailQuotaValue.SetText(sb);
        }

        if (txtDetailIoSummary != null)
        {
            string inA = (scenario != null && !string.IsNullOrEmpty(scenario.inputAName)) ? scenario.inputAName : "INPUT A";
            string inB = (scenario != null && !string.IsNullOrEmpty(scenario.inputBName)) ? scenario.inputBName : "INPUT B";
            string outY = (scenario != null && !string.IsNullOrEmpty(scenario.outputName)) ? scenario.outputName : "OUTPUT";
            sb.Clear();
            sb.Append("A: ").Append(inA).Append("  |  B: ").Append(inB).Append("  ->  Y: ").Append(outY);
            txtDetailIoSummary.SetText(sb);
        }

        // Proteksi Pedagogis (Anti-Bocoran): Rumus jawaban HANYA tampil jika level sudah LULUS
        if (isCleared)
        {
            SetImageColorIfChanged(detailFormulaCardBorder, GwThemeTokens.StatusPassEmerald);
            SetImageColorIfChanged(detailFormulaLockDot, GwThemeTokens.StatusPassEmerald);
            if (txtDetailFormulaHeading != null)
            {
                txtDetailFormulaHeading.text = "RUMUS BOOLEAN TERVERIFIKASI";
                txtDetailFormulaHeading.color = GwThemeTokens.StatusPassText;
            }
            if (txtDetailFormulaValue != null)
            {
                txtDetailFormulaValue.text = CachedVerifiedFormulas[safeIdx];
                txtDetailFormulaValue.color = GwThemeTokens.TextPrimary;
            }
        }
        else
        {
            SetImageColorIfChanged(detailFormulaCardBorder, GwThemeTokens.HairlineIdle);
            SetImageColorIfChanged(detailFormulaLockDot, GwThemeTokens.StatusErrorRed);
            if (txtDetailFormulaHeading != null)
            {
                txtDetailFormulaHeading.text = "RUMUS BOOLEAN (PROTEKSI MISI)";
                txtDetailFormulaHeading.color = GwThemeTokens.TextSecondary;
            }
            if (txtDetailFormulaValue != null)
            {
                txtDetailFormulaValue.text = StrFormulaLocked;
                txtDetailFormulaValue.color = GwThemeTokens.SignalLowSlate;
            }
        }
    }

    private static bool IsLevelCleared(int index)
    {
        if (index < 0 || index >= CachedLevelPrefKeys.Length)
        {
            return false;
        }
        return PlayerPrefs.GetInt(CachedLevelPrefKeys[index], 0) == 1;
    }

    private LevelScenario GetScenario(int index)
    {
        if (levelManager != null && levelManager.levels != null && index >= 0 && index < levelManager.levels.Count)
        {
            return levelManager.levels[index];
        }
        return null;
    }

    private int ResolveLevelQuota(int index)
    {
        LevelScenario scenario = GetScenario(index);
        if (scenario != null && scenario.maxGateCardsQuota > 0)
        {
            return scenario.maxGateCardsQuota;
        }
        return (index >= 0 && index < CachedFallbackQuotas.Length) ? CachedFallbackQuotas[index] : 1;
    }

    private void WriteCleanLevelTitleToBuilder(int index, StringBuilder builder)
    {
        builder.Clear();
        LevelScenario scenario = GetScenario(index);
        if (scenario != null && !string.IsNullOrEmpty(scenario.levelTitle))
        {
            string raw = scenario.levelTitle;
            int colonIdx = raw.IndexOf(": ", StringComparison.Ordinal);
            if (colonIdx >= 0 && colonIdx + 2 < raw.Length && raw.StartsWith("Level ", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(raw, colonIdx + 2, raw.Length - (colonIdx + 2));
                return;
            }
            builder.Append(raw);
            return;
        }

        if (index >= 0 && index < CachedLevelShortTitles.Length)
        {
            builder.Append(CachedLevelShortTitles[index]);
        }
        else
        {
            builder.Append("Misi Kurikulum");
        }
    }

    // =========================================================================
    // KONSTRUKSI UI PROSEDURAL 100% TOKEN GwThemeTokens (05_Level_Selector)
    // =========================================================================

    private void EnsureUiBuilt()
    {
        if (isUiBuilt)
        {
            return;
        }

        if (safeFont == null)
        {
            safeFont = compactHudMenu != null && compactHudMenu.SafeFont != null
                ? compactHudMenu.SafeFont
                : TMP_Settings.defaultFontAsset;
        }

        BuildOverlayHierarchy();
        isUiBuilt = true;
        ApplySafeAreaAndResponsiveScaleIfNeeded(force: true);
    }

    private void BuildOverlayHierarchy()
    {
        // Dedicated ScreenSpaceOverlay Canvas (sortingOrder = 35) di atas Drawer (30), Guide (25), LevelComplete (20)
        overlayRootObj = new GameObject(
            "Canvas_AirLevelSelectorOverlay_V85",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        overlayRootObj.transform.SetParent(transform, false);

        Canvas overlayCanvas = overlayRootObj.GetComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = OverlayCanvasSortingOrder;

        CanvasScaler scaler = overlayRootObj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceCanvasWidth, ReferenceCanvasHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform rootRect = overlayRootObj.GetComponent<RectTransform>();

        // 1. Full-screen LevelMapBackdropSolid (#0B0D14 94%) backdrop dengan raycastTarget = true (anti-bocor ke stage 3D & Top HUD)
        GameObject backdropObj = new GameObject("Panel_LevelSelectorBackdrop_V85", typeof(RectTransform), typeof(Image));
        backdropObj.transform.SetParent(rootRect, false);
        RectTransform backdropRect = backdropObj.GetComponent<RectTransform>();
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = Vector2.zero;
        backdropRect.offsetMax = Vector2.zero;

        Image backdropImg = backdropObj.GetComponent<Image>();
        backdropImg.color = GwThemeTokens.LevelMapBackdropSolid;
        backdropImg.raycastTarget = true;

        // 2. Kontainer Safe Area
        GameObject safeObj = new GameObject("LevelSelector_SafeArea", typeof(RectTransform));
        safeObj.transform.SetParent(rootRect, false);
        safeAreaRect = safeObj.GetComponent<RectTransform>();
        safeAreaRect.anchorMin = Vector2.zero;
        safeAreaRect.anchorMax = Vector2.one;
        safeAreaRect.offsetMin = Vector2.zero;
        safeAreaRect.offsetMax = Vector2.zero;

        // 3. Kontainer Utama Terpusat (1720x900) yang otomatis scale-fit pada layar kompak / 20:9
        GameObject contentObj = new GameObject("LevelSelector_Content", typeof(RectTransform));
        contentObj.transform.SetParent(safeAreaRect, false);
        contentContainerRect = contentObj.GetComponent<RectTransform>();
        contentContainerRect.anchorMin = new Vector2(0.5f, 0.5f);
        contentContainerRect.anchorMax = new Vector2(0.5f, 0.5f);
        contentContainerRect.pivot = new Vector2(0.5f, 0.5f);
        contentContainerRect.anchoredPosition = Vector2.zero;
        contentContainerRect.sizeDelta = new Vector2(ContentWidth, ContentHeight);

        BuildHeaderBar(contentContainerRect);
        BuildCircuitMapPanel(contentContainerRect);
        BuildRightDetailPanel(contentContainerRect);

        overlayRootObj.SetActive(false);
    }

    private void BuildHeaderBar(RectTransform parent)
    {
        // Panel Header tipis di atas
        GameObject headerObj = new GameObject("Header_CircuitMap", typeof(RectTransform));
        headerObj.transform.SetParent(parent, false);
        RectTransform headerRect = headerObj.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(0f, 1f);
        headerRect.pivot = new Vector2(0f, 1f);
        headerRect.anchoredPosition = Vector2.zero;
        headerRect.sizeDelta = new Vector2(ContentWidth, HeaderHeight);

        // Titik aksen kiri + judul utama PILIH LEVEL // CIRCUIT MAP
        CreateCircleDot("Header_AccentDot", headerRect, 6f, -24f, GwThemeTokens.StatusDotSizePx, GwThemeTokens.SignalHighTeal);

        CreateTopLeftTmp(
            "Txt_MapHeaderTitle",
            headerRect,
            new Vector2(22f, -4f),
            new Vector2(760f, 38f),
            "PILIH LEVEL // CIRCUIT MAP",
            22f,
            28f,
            GwThemeTokens.TextPrimary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        CreateTopLeftTmp(
            "Txt_MapHeaderSub",
            headerRect,
            new Vector2(22f, -42f),
            new Vector2(760f, 20f),
            "KURIKULUM MERDEKA FASE E  //  7 MISI GERBANG LOGIKA & ALJABAR BOOLEAN",
            11f,
            13f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Normal);

        // Pil progres X/7 SELESAI di sebelah kiri tombol keluar
        const float exitBtnW = 148f;
        const float progressPillW = 186f;
        const float headerCtrlH = 44f;
        const float headerCtrlY = -10f;
        float exitBtnX = ContentWidth - exitBtnW;
        float progressPillX = exitBtnX - 14f - progressPillW;

        GameObject progObj = new GameObject("Pill_LevelProgress", typeof(RectTransform), typeof(Image));
        progObj.transform.SetParent(headerRect, false);
        RectTransform progRect = progObj.GetComponent<RectTransform>();
        progRect.anchorMin = new Vector2(0f, 1f);
        progRect.anchorMax = new Vector2(0f, 1f);
        progRect.pivot = new Vector2(0f, 1f);
        progRect.anchoredPosition = new Vector2(progressPillX, headerCtrlY);
        progRect.sizeDelta = new Vector2(progressPillW, headerCtrlH);

        Image progBg = progObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(progBg, GwThemeTokens.CardIdle);
        progBg.raycastTarget = false;
        GwThemeTokens.AttachHairlineBorder(progRect, "Pill_LevelProgress_Border", GwThemeTokens.HairlineIdle);

        progressDotImg = CreateCircleDot("Progress_Dot", progRect, 20f, -headerCtrlH * 0.5f, GwThemeTokens.StatusDotSizePx, GwThemeTokens.StatusPassEmerald);

        txtProgressPill = CreateTopLeftTmp(
            "Txt_ProgressPill",
            progRect,
            new Vector2(34f, 0f),
            new Vector2(progressPillW - 44f, headerCtrlH),
            "0/7 SELESAI",
            13f,
            16f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        // Tombol X KELUAR di pojok kanan atas
        btnExitX = CreateRoundedFlatButton(
            "Btn_LevelSelectorExit",
            headerRect,
            exitBtnX,
            headerCtrlY,
            exitBtnW,
            headerCtrlH,
            "X  KELUAR",
            GwThemeTokens.CardIdle,
            GwThemeTokens.HairlineActive,
            GwThemeTokens.TextPrimary,
            13.5f,
            16f,
            hasBorder: true,
            HandleExitClicked,
            out _);

        // Pembatas bawah header 1px HairlineIdle
        CreateFlatImage(
            "Header_BottomHairline",
            parent,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, -HeaderHeight - 8f),
            new Vector2(ContentWidth, GwThemeTokens.HairlineBorderPx),
            GwThemeTokens.HairlineIdle);
    }

    private void BuildCircuitMapPanel(RectTransform parent)
    {
        GameObject mapObj = new GameObject("Panel_CircuitMapArea", typeof(RectTransform), typeof(Image));
        mapObj.transform.SetParent(parent, false);
        RectTransform mapRect = mapObj.GetComponent<RectTransform>();
        mapRect.anchorMin = new Vector2(0f, 1f);
        mapRect.anchorMax = new Vector2(0f, 1f);
        mapRect.pivot = new Vector2(0f, 1f);
        mapRect.anchoredPosition = new Vector2(0f, -BodyTopOffset);
        mapRect.sizeDelta = new Vector2(MapPanelWidth, BodyHeight);

        Image mapBg = mapObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(mapBg, GwThemeTokens.SmokedGlassSoft);
        mapBg.raycastTarget = true;
        GwThemeTokens.AttachHairlineBorder(mapRect, "Panel_CircuitMapArea_Border", GwThemeTokens.HairlineIdle);

        // Label sub-seksi di kiri atas peta
        CreateTopLeftTmp(
            "Txt_MapSectionLabel",
            mapRect,
            new Vector2(28f, -20f),
            new Vector2(MapPanelWidth - 56f, 22f),
            "PETA PROGRESI SIRKUIT  //  PILIH NODE UNTUK INSPEKSI MISI",
            11.5f,
            13.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        // Geometri 2 baris node kartu (01-04 baris atas, 05-07 baris bawah terpusat)
        const float mapPadX = 36f;
        const float cardGapX = 42f;
        const float cardW = (MapPanelWidth - (mapPadX * 2f) - (cardGapX * 3f)) / 4f; // ~272.5px
        const float cardH = 224f;
        const float row1TopY = -98f;
        const float rowGapY = 92f;
        const float row2TopY = row1TopY - cardH - rowGapY; // -414f

        float[] row1LeftX = new float[4];
        for (int c = 0; c < 4; c++)
        {
            row1LeftX[c] = mapPadX + (c * (cardW + cardGapX));
        }

        float row2TotalW = (3f * cardW) + (2f * cardGapX);
        float row2StartX = (MapPanelWidth - row2TotalW) * 0.5f;
        float[] row2LeftX = new float[3];
        for (int c = 0; c < 3; c++)
        {
            row2LeftX[c] = row2StartX + (c * (cardW + cardGapX));
        }

        // Layer 1: Garis konektor ortogonal 1px di belakang kartu node
        float row1MidY = row1TopY - (cardH * 0.5f);
        float row2MidY = row2TopY - (cardH * 0.5f);
        float busChannelY = row1TopY - cardH - (rowGapY * 0.5f);
        float lineThick = GwThemeTokens.HairlineBorderPx;

        conn01To02 = CreateOrthogonalSegment("Conn_01_02", mapRect, row1LeftX[0] + cardW, row1MidY, cardGapX, lineThick);
        conn02To03 = CreateOrthogonalSegment("Conn_02_03", mapRect, row1LeftX[1] + cardW, row1MidY, cardGapX, lineThick);
        conn03To04 = CreateOrthogonalSegment("Conn_03_04", mapRect, row1LeftX[2] + cardW, row1MidY, cardGapX, lineThick);

        // Jalur ortogonal 04 -> 05: turun dari tengah-bawah 04 -> horizontal kiri ke tengah-atas 05 -> turun ke 05
        float node04CenterX = row1LeftX[3] + (cardW * 0.5f);
        float node05CenterX = row2LeftX[0] + (cardW * 0.5f);
        float dropHeight = rowGapY * 0.5f;

        conn04DropV = CreateOrthogonalSegment("Conn_04_DropV", mapRect, node04CenterX - (lineThick * 0.5f), row1TopY - cardH, lineThick, dropHeight);
        float hLeft = Mathf.Min(node04CenterX, node05CenterX);
        float hWidth = Mathf.Abs(node04CenterX - node05CenterX) + lineThick;
        conn04MidH = CreateOrthogonalSegment("Conn_04_05_MidH", mapRect, hLeft, busChannelY, hWidth, lineThick);
        conn05EntryV = CreateOrthogonalSegment("Conn_05_EntryV", mapRect, node05CenterX - (lineThick * 0.5f), busChannelY, lineThick, dropHeight);

        conn05To06 = CreateOrthogonalSegment("Conn_05_06", mapRect, row2LeftX[0] + cardW, row2MidY, cardGapX, lineThick);
        conn06To07 = CreateOrthogonalSegment("Conn_06_07", mapRect, row2LeftX[1] + cardW, row2MidY, cardGapX, lineThick);

        // Layer 2: 7 Kartu Node Level (01-04 baris atas, 05-07 baris bawah)
        for (int i = 0; i < 4; i++)
        {
            nodeCards[i] = CreateMapNodeCard(
                $"NodeCard_Level_{i + 1}",
                mapRect,
                row1LeftX[i],
                row1TopY,
                cardW,
                cardH,
                i);
        }

        for (int i = 0; i < 3; i++)
        {
            int levelIdx = i + 4;
            nodeCards[levelIdx] = CreateMapNodeCard(
                $"NodeCard_Level_{levelIdx + 1}",
                mapRect,
                row2LeftX[i],
                row2TopY,
                cardW,
                cardH,
                levelIdx);
        }

        // Layer 3: Legenda status ringkas di tepi bawah panel peta
        float legendY = -(BodyHeight - 44f);
        CreateFlatImage(
            "Map_LegendDivider",
            mapRect,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(28f, legendY + 14f),
            new Vector2(MapPanelWidth - 56f, GwThemeTokens.HairlineBorderPx),
            GwThemeTokens.HairlineIdle);

        BuildLegendItem(mapRect, 28f, legendY, GwThemeTokens.SignalHighTeal, "DIPILIH / AKTIF");
        BuildLegendItem(mapRect, 210f, legendY, GwThemeTokens.StatusPassEmerald, "SELESAI (RUMUS TERBUKA)");
        BuildLegendItem(mapRect, 480f, legendY, GwThemeTokens.SignalLowSlate, "TERKUNCI (RAKIT UNTUK VERIFIKASI)");
    }

    private void BuildLegendItem(RectTransform parent, float leftX, float topY, Color dotColor, string label)
    {
        CreateCircleDot($"LegendDot_{label}", parent, leftX + 4f, topY - 12f, 8f, dotColor);
        CreateTopLeftTmp(
            $"LegendTxt_{label}",
            parent,
            new Vector2(leftX + 16f, topY - 2f),
            new Vector2(360f, 20f),
            label,
            10.5f,
            12.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);
    }

    private MapNodeCardUi CreateMapNodeCard(
        string name,
        RectTransform parent,
        float leftX,
        float topY,
        float width,
        float height,
        int levelIndex)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(leftX, topY);
        rect.sizeDelta = new Vector2(width, height);

        Image bg = obj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(bg, GwThemeTokens.CardIdle);
        bg.raycastTarget = true;

        Image border = GwThemeTokens.AttachHairlineBorder(rect, $"{name}_Border", GwThemeTokens.HairlineIdle);

        // Strip tipis 2px di sisi atas kartu untuk penanda terpilih/lulus
        Image topBar = CreateFlatImage(
            "TopAccentBar",
            rect,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, -1f),
            new Vector2(-12f, 2f),
            GwThemeTokens.SignalHighTeal);
        topBar.enabled = false;

        int capturedIndex = levelIndex;
        UnityEngine.Events.UnityAction clickHandler = () => HandleNodeCardClicked(capturedIndex);

        Button btn = obj.GetComponent<Button>();
        btn.targetGraphic = bg;
        btn.transition = Selectable.Transition.ColorTint;
        btn.colors = GwThemeTokens.FlatGlassButtonColors;
        btn.onClick.AddListener(clickHandler);

        const float padX = 18f;
        float innerW = width - (padX * 2f);

        // Baris atas: nomor level besar (01..07) + titik aktif + centang lulus
        TMP_Text numTmp = CreateTopLeftTmp(
            "Txt_NodeNum",
            rect,
            new Vector2(padX, -18f),
            new Vector2(68f, 36f),
            CachedLevelNumbers[levelIndex],
            22f,
            28f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        Image dotImg = CreateCircleDot(
            "Icon_ActiveDot",
            rect,
            padX + 66f,
            -36f,
            GwThemeTokens.StatusDotSizePx,
            GwThemeTokens.SignalHighTeal);
        dotImg.enabled = false;

        GameObject checkRoot = CreateGeometricCheckmark(
            "Icon_ClearedCheck",
            rect,
            width - padX - 10f,
            -36f,
            GwThemeTokens.StatusPassText);
        checkRoot.SetActive(false);

        // Judul level di tengah kartu (wrap 2 baris bersih)
        TMP_Text titleTmp = CreateTopLeftTmp(
            "Txt_NodeTitle",
            rect,
            new Vector2(padX, -66f),
            new Vector2(innerW, 84f),
            CachedLevelShortTitles[levelIndex],
            14f,
            18f,
            GwThemeTokens.TextPrimary,
            TextAlignmentOptions.TopLeft,
            FontStyles.Bold,
            allowWrap: true);

        // Pembatas bawah internal 1px
        CreateFlatImage(
            "Node_FooterDivider",
            rect,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(padX, -(height - 50f)),
            new Vector2(innerW, GwThemeTokens.HairlineBorderPx),
            GwThemeTokens.HairlineIdle);

        // Baris footer: KUOTA kiri + STATUS kanan
        TMP_Text quotaTmp = CreateTopLeftTmp(
            "Txt_NodeQuota",
            rect,
            new Vector2(padX, -(height - 40f)),
            new Vector2(innerW * 0.55f, 26f),
            "KUOTA: 1",
            11f,
            13f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        TMP_Text statusTmp = CreateTopLeftTmp(
            "Txt_NodeStatus",
            rect,
            new Vector2(padX + (innerW * 0.50f), -(height - 40f)),
            new Vector2(innerW * 0.50f, 26f),
            StrStatusLocked,
            11f,
            13f,
            GwThemeTokens.SignalLowSlate,
            TextAlignmentOptions.MidlineRight,
            FontStyles.Bold);

        return new MapNodeCardUi
        {
            button = btn,
            clickHandler = clickHandler,
            bgImage = bg,
            borderImage = border,
            activeDotImage = dotImg,
            topAccentBar = topBar,
            checkIconRoot = checkRoot,
            txtNumber = numTmp,
            txtTitle = titleTmp,
            txtQuota = quotaTmp,
            txtStatus = statusTmp
        };
    }

    private void BuildRightDetailPanel(RectTransform parent)
    {
        GameObject panelObj = new GameObject("Panel_LevelDetail_V85", typeof(RectTransform), typeof(Image));
        panelObj.transform.SetParent(parent, false);
        RectTransform panelRect = panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(MapPanelWidth + BodyColumnGap, -BodyTopOffset);
        panelRect.sizeDelta = new Vector2(DetailPanelWidth, BodyHeight);

        Image panelBg = panelObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(panelBg, GwThemeTokens.SmokedGlassOverlay);
        panelBg.raycastTarget = true;
        GwThemeTokens.AttachHairlineBorder(panelRect, "Panel_LevelDetail_Border", GwThemeTokens.HairlineIdle);

        const float padX = 24f;
        float innerW = DetailPanelWidth - (padX * 2f);
        float y = -24f;

        // 1. Metadata nomor level + badge status (SELESAI / AKTIF / TERKUNCI)
        txtDetailLevelTag = CreateTopLeftTmp(
            "Txt_DetailLevelTag",
            panelRect,
            new Vector2(padX, y),
            new Vector2(innerW - 116f, 26f),
            "LEVEL 01 // DETAIL MISI",
            11.5f,
            13.5f,
            GwThemeTokens.SignalHighTeal,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        GameObject statusPillObj = new GameObject("Pill_DetailStatus", typeof(RectTransform), typeof(Image));
        statusPillObj.transform.SetParent(panelRect, false);
        RectTransform statusPillRect = statusPillObj.GetComponent<RectTransform>();
        statusPillRect.anchorMin = new Vector2(0f, 1f);
        statusPillRect.anchorMax = new Vector2(0f, 1f);
        statusPillRect.pivot = new Vector2(0f, 1f);
        statusPillRect.anchoredPosition = new Vector2(padX + innerW - 112f, y - 1f);
        statusPillRect.sizeDelta = new Vector2(112f, 28f);

        detailStatusPillBg = statusPillObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(detailStatusPillBg, GwThemeTokens.CardIdle);
        detailStatusPillBg.raycastTarget = false;
        detailStatusPillBorder = GwThemeTokens.AttachHairlineBorder(statusPillRect, "Pill_DetailStatus_Border", GwThemeTokens.HairlineIdle);

        detailStatusDot = CreateCircleDot("DetailStatus_Dot", statusPillRect, 14f, -14f, 7f, GwThemeTokens.StatusErrorRed);

        txtDetailStatus = CreateTopLeftTmp(
            "Txt_DetailStatus",
            statusPillRect,
            new Vector2(24f, 0f),
            new Vector2(82f, 28f),
            StrStatusLocked,
            10.5f,
            12f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        y -= 38f;

        // 2. Judul level besar
        txtDetailTitle = CreateTopLeftTmp(
            "Txt_DetailTitle",
            panelRect,
            new Vector2(padX, y),
            new Vector2(innerW, 64f),
            CachedLevelShortTitles[0],
            20f,
            25f,
            GwThemeTokens.TextPrimary,
            TextAlignmentOptions.TopLeft,
            FontStyles.Bold,
            allowWrap: true);

        y -= 74f;
        CreateDetailDivider("DetailDiv_Title", panelRect, padX, y, innerW);
        y -= 18f;

        // 3. Kartu Misi Operasi
        CreateTopLeftTmp(
            "Txt_MissionLabel",
            panelRect,
            new Vector2(padX, y),
            new Vector2(innerW, 18f),
            "MISI OPERASI",
            10.5f,
            12.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);
        y -= 24f;

        GameObject missionCardObj = new GameObject("Card_DetailMission", typeof(RectTransform), typeof(Image));
        missionCardObj.transform.SetParent(panelRect, false);
        RectTransform missionCardRect = missionCardObj.GetComponent<RectTransform>();
        missionCardRect.anchorMin = new Vector2(0f, 1f);
        missionCardRect.anchorMax = new Vector2(0f, 1f);
        missionCardRect.pivot = new Vector2(0f, 1f);
        missionCardRect.anchoredPosition = new Vector2(padX, y);
        missionCardRect.sizeDelta = new Vector2(innerW, 108f);

        Image missionBg = missionCardObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(missionBg, GwThemeTokens.CardIdle);
        missionBg.raycastTarget = false;
        GwThemeTokens.AttachHairlineBorder(missionCardRect, "Card_DetailMission_Border", GwThemeTokens.HairlineIdle);

        txtDetailMission = CreateTopLeftTmp(
            "Txt_DetailMission",
            missionCardRect,
            new Vector2(14f, -12f),
            new Vector2(innerW - 28f, 84f),
            CachedFallbackMissions[0],
            14f,
            17.5f,
            GwThemeTokens.TextPrimary,
            TextAlignmentOptions.TopLeft,
            FontStyles.Normal,
            allowWrap: true);

        y -= 124f;

        // 4. Metrik Kuota Gerbang & Terminal I/O
        GameObject quotaCardObj = new GameObject("Card_DetailQuota", typeof(RectTransform), typeof(Image));
        quotaCardObj.transform.SetParent(panelRect, false);
        RectTransform quotaCardRect = quotaCardObj.GetComponent<RectTransform>();
        quotaCardRect.anchorMin = new Vector2(0f, 1f);
        quotaCardRect.anchorMax = new Vector2(0f, 1f);
        quotaCardRect.pivot = new Vector2(0f, 1f);
        quotaCardRect.anchoredPosition = new Vector2(padX, y);
        quotaCardRect.sizeDelta = new Vector2(innerW, 88f);

        Image quotaBg = quotaCardObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(quotaBg, GwThemeTokens.CardIdle);
        quotaBg.raycastTarget = false;
        GwThemeTokens.AttachHairlineBorder(quotaCardRect, "Card_DetailQuota_Border", GwThemeTokens.HairlineIdle);

        CreateTopLeftTmp(
            "Txt_QuotaHeading",
            quotaCardRect,
            new Vector2(14f, -10f),
            new Vector2(innerW - 28f, 18f),
            "KUOTA GERBANG & TERMINAL",
            10f,
            12f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        txtDetailQuotaValue = CreateTopLeftTmp(
            "Txt_DetailQuotaValue",
            quotaCardRect,
            new Vector2(14f, -30f),
            new Vector2(innerW - 28f, 26f),
            "MAKS 1 GERBANG LOGIKA",
            14f,
            17f,
            GwThemeTokens.SignalHighTeal,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        txtDetailIoSummary = CreateTopLeftTmp(
            "Txt_DetailIoSummary",
            quotaCardRect,
            new Vector2(14f, -58f),
            new Vector2(innerW - 28f, 22f),
            "A: PINTU  |  B: JENDELA  ->  Y: ALARM",
            10.5f,
            12.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Normal);

        y -= 104f;

        // 5. Seksi Gerbang Tersedia (5 gerbang rak kerja, tanpa bocoran jawaban)
        CreateTopLeftTmp(
            "Txt_AvailGatesHeading",
            panelRect,
            new Vector2(padX, y),
            new Vector2(innerW, 18f),
            "GERBANG TERSEDIA DI MEJA KERJA",
            10.5f,
            12f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);
        y -= 24f;

        const float chipGap = 6f;
        float chipW = (innerW - (chipGap * 4f)) / 5f;
        const float chipH = 34f;
        for (int g = 0; g < CachedAvailableGateNames.Length; g++)
        {
            GameObject chipObj = new GameObject($"Chip_Gate_{CachedAvailableGateNames[g]}", typeof(RectTransform), typeof(Image));
            chipObj.transform.SetParent(panelRect, false);
            RectTransform chipRect = chipObj.GetComponent<RectTransform>();
            chipRect.anchorMin = new Vector2(0f, 1f);
            chipRect.anchorMax = new Vector2(0f, 1f);
            chipRect.pivot = new Vector2(0f, 1f);
            chipRect.anchoredPosition = new Vector2(padX + (g * (chipW + chipGap)), y);
            chipRect.sizeDelta = new Vector2(chipW, chipH);

            Image chipBg = chipObj.GetComponent<Image>();
            GwThemeTokens.ApplyRoundedRect6(chipBg, GwThemeTokens.CardIdle);
            chipBg.raycastTarget = false;
            GwThemeTokens.AttachHairlineBorder(chipRect, $"Chip_Gate_{g}_Border", GwThemeTokens.HairlineIdle);

            CreateStretchedTmp(
                $"Txt_ChipGate_{g}",
                chipRect,
                CachedAvailableGateNames[g],
                10.5f,
                13f,
                GwThemeTokens.TextPrimary,
                TextAlignmentOptions.Center,
                FontStyles.Bold);
        }

        y -= (chipH + 18f);

        // 6. Kartu Rumus Boolean (Proteksi Pedagogis: terkunci hingga level lulus)
        GameObject formulaObj = new GameObject("Card_DetailFormula", typeof(RectTransform), typeof(Image));
        formulaObj.transform.SetParent(panelRect, false);
        RectTransform formulaRect = formulaObj.GetComponent<RectTransform>();
        formulaRect.anchorMin = new Vector2(0f, 1f);
        formulaRect.anchorMax = new Vector2(0f, 1f);
        formulaRect.pivot = new Vector2(0f, 1f);
        formulaRect.anchoredPosition = new Vector2(padX, y);
        formulaRect.sizeDelta = new Vector2(innerW, 88f);

        Image formulaBg = formulaObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(formulaBg, GwThemeTokens.SmokedGlass);
        formulaBg.raycastTarget = false;
        detailFormulaCardBorder = GwThemeTokens.AttachHairlineBorder(formulaRect, "Card_DetailFormula_Border", GwThemeTokens.HairlineIdle);

        detailFormulaLockDot = CreateCircleDot("Formula_LockDot", formulaRect, 18f, -20f, 8f, GwThemeTokens.StatusErrorRed);

        txtDetailFormulaHeading = CreateTopLeftTmp(
            "Txt_DetailFormulaHeading",
            formulaRect,
            new Vector2(30f, -10f),
            new Vector2(innerW - 44f, 20f),
            "RUMUS BOOLEAN (PROTEKSI MISI)",
            10f,
            12f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        txtDetailFormulaValue = CreateTopLeftTmp(
            "Txt_DetailFormulaValue",
            formulaRect,
            new Vector2(14f, -36f),
            new Vector2(innerW - 28f, 40f),
            StrFormulaLocked,
            12f,
            18f,
            GwThemeTokens.SignalLowSlate,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        // 7. Tombol Aksi Bawah: MULAI SIMULASI -> (Primer Solid Putih) & SANDBOX MODE (Outline)
        float footerDividerY = -(BodyHeight - 148f);
        CreateDetailDivider("DetailDiv_Footer", panelRect, padX, footerDividerY, innerW);

        btnStartSimulation = CreateRoundedFlatButton(
            "Btn_StartSelectedLevel",
            panelRect,
            padX,
            -(BodyHeight - 132f),
            innerW,
            54f,
            "MULAI SIMULASI ->",
            GwThemeTokens.PrimaryFill,
            GwThemeTokens.PrimaryFill,
            GwThemeTokens.PrimaryText,
            15f,
            19f,
            hasBorder: false,
            HandleStartSimulationClicked,
            out _);

        btnEnterSandbox = CreateRoundedFlatButton(
            "Btn_OpenSandboxFromMap",
            panelRect,
            padX,
            -(BodyHeight - 66f),
            innerW,
            46f,
            "SANDBOX MODE",
            GwThemeTokens.CardIdle,
            GwThemeTokens.HairlineActive,
            GwThemeTokens.TextPrimary,
            13.5f,
            16f,
            hasBorder: true,
            HandleSandboxClicked,
            out _);
    }

    // =========================================================================
    // HELPER VISUAL & SAFE AREA RESPONSIF (100% TOKEN GwThemeTokens)
    // =========================================================================

    private void ApplySafeAreaAndResponsiveScaleIfNeeded(bool force)
    {
        if (safeAreaRect == null || contentContainerRect == null)
        {
            return;
        }

        Rect sa = Screen.safeArea;
        int sw = Screen.width;
        int sh = Screen.height;
        if (!force && hasAppliedScale && sw == lastScreenW && sh == lastScreenH && sa == lastAppliedSafeArea)
        {
            return;
        }

        lastScreenW = sw;
        lastScreenH = sh;
        lastAppliedSafeArea = sa;
        hasAppliedScale = true;

        float denomW = Mathf.Max(1f, sw);
        float denomH = Mathf.Max(1f, sh);
        Vector2 anchorMin = new Vector2(Mathf.Clamp01(sa.xMin / denomW), Mathf.Clamp01(sa.yMin / denomH));
        Vector2 anchorMax = new Vector2(Mathf.Clamp01(sa.xMax / denomW), Mathf.Clamp01(sa.yMax / denomH));
        safeAreaRect.anchorMin = anchorMin;
        safeAreaRect.anchorMax = anchorMax;
        safeAreaRect.offsetMin = Vector2.zero;
        safeAreaRect.offsetMax = Vector2.zero;

        RectTransform rootRect = overlayRootObj != null ? overlayRootObj.GetComponent<RectTransform>() : null;
        float canvasW = (rootRect != null && rootRect.rect.width > 400f) ? rootRect.rect.width : ReferenceCanvasWidth;
        float canvasH = (rootRect != null && rootRect.rect.height > 300f) ? rootRect.rect.height : ReferenceCanvasHeight;

        float safeW = canvasW * Mathf.Max(0.5f, anchorMax.x - anchorMin.x);
        float safeH = canvasH * Mathf.Max(0.5f, anchorMax.y - anchorMin.y);

        float scaleX = (safeW - 64f) / ContentWidth;
        float scaleY = (safeH - 48f) / ContentHeight;
        float fitScale = Mathf.Clamp(Mathf.Min(scaleX, scaleY), 0.56f, 1.0f);
        contentContainerRect.localScale = new Vector3(fitScale, fitScale, 1f);
    }

    private static Image CreateOrthogonalSegment(
        string name,
        RectTransform parent,
        float leftX,
        float topY,
        float width,
        float height)
    {
        return CreateFlatImage(
            name,
            parent,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(leftX, topY),
            new Vector2(width, height),
            GwThemeTokens.HairlineIdle);
    }

    private static void CreateDetailDivider(string name, RectTransform parent, float leftX, float topY, float width)
    {
        CreateFlatImage(
            name,
            parent,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(leftX, topY),
            new Vector2(width, GwThemeTokens.HairlineBorderPx),
            GwThemeTokens.HairlineIdle);
    }

    private static Image CreateCircleDot(string name, RectTransform parent, float centerX, float centerY, float sizePx, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(centerX, centerY);
        rect.sizeDelta = new Vector2(sizePx, sizePx);

        Image img = obj.GetComponent<Image>();
        img.sprite = GwThemeTokens.GetCircleSprite();
        img.type = Image.Type.Simple;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static GameObject CreateGeometricCheckmark(string name, RectTransform parent, float centerX, float centerY, Color color)
    {
        GameObject root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.anchoredPosition = new Vector2(centerX, centerY);
        rootRect.sizeDelta = new Vector2(18f, 18f);

        Image shortArm = CreateFlatImage(
            "Check_Short",
            rootRect,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(-3.6f, -1.4f),
            new Vector2(6.4f, 2.2f),
            color);
        shortArm.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);

        Image longArm = CreateFlatImage(
            "Check_Long",
            rootRect,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(1.8f, 0.5f),
            new Vector2(11.4f, 2.2f),
            color);
        longArm.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        return root;
    }

    private Button CreateRoundedFlatButton(
        string name,
        RectTransform parent,
        float leftX,
        float topY,
        float width,
        float height,
        string label,
        Color bgColor,
        Color borderColor,
        Color textColor,
        float minFont,
        float maxFont,
        bool hasBorder,
        UnityEngine.Events.UnityAction onClick,
        out TMP_Text labelOut)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(leftX, topY);
        rect.sizeDelta = new Vector2(width, height);

        Image bg = obj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(bg, bgColor);
        bg.raycastTarget = true;

        if (hasBorder)
        {
            GwThemeTokens.AttachHairlineBorder(rect, $"{name}_Border", borderColor);
        }

        Button btn = obj.GetComponent<Button>();
        btn.targetGraphic = bg;
        btn.transition = Selectable.Transition.ColorTint;
        btn.colors = GwThemeTokens.FlatGlassButtonColors;
        if (onClick != null)
        {
            btn.onClick.AddListener(onClick);
        }

        labelOut = CreateStretchedTmp(
            $"{name}_Label",
            rect,
            label,
            minFont,
            maxFont,
            textColor,
            TextAlignmentOptions.Center,
            FontStyles.Bold);
        return btn;
    }

    private static Image CreateFlatImage(
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

    private TMP_Text CreateTopLeftTmp(
        string name,
        RectTransform parent,
        Vector2 anchoredPos,
        Vector2 sizeDelta,
        string content,
        float minSize,
        float maxSize,
        Color color,
        TextAlignmentOptions alignment,
        FontStyles style,
        bool allowWrap = false)
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
        tmp.fontStyle = style;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = minSize;
        tmp.fontSizeMax = maxSize;
        tmp.textWrappingMode = allowWrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        return tmp;
    }

    private TMP_Text CreateStretchedTmp(
        string name,
        RectTransform parent,
        string content,
        float minSize,
        float maxSize,
        Color color,
        TextAlignmentOptions alignment,
        FontStyles style)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(10f, 2f);
        rect.offsetMax = new Vector2(-10f, -2f);

        TextMeshProUGUI tmp = obj.GetComponent<TextMeshProUGUI>();
        if (safeFont != null)
        {
            tmp.font = safeFont;
        }
        tmp.text = content;
        tmp.fontStyle = style;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = minSize;
        tmp.fontSizeMax = maxSize;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void SetImageColorIfChanged(Image img, Color color)
    {
        if (img != null && img.color != color)
        {
            img.color = color;
        }
    }

    private static void RemoveBtnListener(Button btn, UnityEngine.Events.UnityAction action)
    {
        if (btn != null && action != null)
        {
            btn.onClick.RemoveListener(action);
        }
    }
}
