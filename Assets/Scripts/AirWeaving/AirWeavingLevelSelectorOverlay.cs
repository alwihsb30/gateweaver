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

    private static readonly string[] CachedNodeUppercaseTitles =
    {
        "ALARM PINTU",
        "SAKELAR TANGGA",
        "KUNCI BRANKAS",
        "PEMUTUS REAKTOR",
        "INTI KEMBAR",
        "SENSOR SERVER",
        "KUNCI BIOMETRIK DE MORGAN"
    };

    private static readonly string[] CachedNodeClearedGateTags =
    {
        "OR",
        "XOR",
        "AND + NOT",
        "NAND",
        "XNOR",
        "NOR",
        "OR + NOT"
    };

    private static readonly string[] CachedNodeLockedConceptTags =
    {
        "LOGIKA DASAR",
        "2 ARAH",
        "2 GERBANG",
        "PROTEKSI",
        "SINKRON",
        "HENING",
        "DE MORGAN"
    };

    private static readonly string[] CachedDetailHeaderClearedTags =
    {
        "LEVEL 01 // GERBANG OR",
        "LEVEL 02 // GERBANG XOR",
        "LEVEL 03 // AND + NOT",
        "LEVEL 04 // GERBANG NAND",
        "LEVEL 05 // LOGIKA XNOR",
        "LEVEL 06 // GERBANG NOR",
        "LEVEL 07 // DE MORGAN"
    };

    private static readonly string[] CachedDetailHeaderLockedTags =
    {
        "LEVEL 01 // MISI DASAR",
        "LEVEL 02 // SAKELAR 2 ARAH",
        "LEVEL 03 // KUNCI BRANKAS",
        "LEVEL 04 // PROTEKSI REAKTOR",
        "LEVEL 05 // INTI KEMBAR",
        "LEVEL 06 // RUANG SERVER",
        "LEVEL 07 // DE MORGAN"
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
        "Y = NOT (A OR B)"
    };

    // Konfigurasi Gerbang Target per level (HANYA dibuka jika GW_v3_LevelCleared_i == 1)
    private static readonly int[] CachedTargetGateCounts = { 1, 1, 2, 1, 2, 2, 2 };

    private static readonly GwThemeTokens.IeeeGateIconKind[] CachedPrimaryGateIcons =
    {
        GwThemeTokens.IeeeGateIconKind.Or,
        GwThemeTokens.IeeeGateIconKind.Xor,
        GwThemeTokens.IeeeGateIconKind.And,
        GwThemeTokens.IeeeGateIconKind.Nand,
        GwThemeTokens.IeeeGateIconKind.Xor,
        GwThemeTokens.IeeeGateIconKind.Or,
        GwThemeTokens.IeeeGateIconKind.Or
    };

    private static readonly string[] CachedPrimaryGateLabels =
    {
        "OR", "XOR", "AND", "NAND", "XOR", "OR", "OR"
    };

    private static readonly GwThemeTokens.IeeeGateIconKind[] CachedSecondaryGateIcons =
    {
        GwThemeTokens.IeeeGateIconKind.Not,
        GwThemeTokens.IeeeGateIconKind.Not,
        GwThemeTokens.IeeeGateIconKind.Not,
        GwThemeTokens.IeeeGateIconKind.Not,
        GwThemeTokens.IeeeGateIconKind.Not,
        GwThemeTokens.IeeeGateIconKind.Not,
        GwThemeTokens.IeeeGateIconKind.Not
    };

    private static readonly string[] CachedSecondaryGateLabels =
    {
        "NOT", "NOT", "NOT", "NOT", "NOT", "NOT", "NOT"
    };

    private const string StrFormulaLocked = "TERKUNCI - BELUM TERVERIFIKASI";
    private const string StrTargetGateLocked = "TERKUNCI - RAKIT UNTUK MEMBUKA";
    private const string StrStatusCleared = "SELESAI";
    private const string StrStatusActive = "AKTIF";
    private const string StrStatusLocked = "TERKUNCI";

    private sealed class MapNodeCardUi
    {
        public Button button;
        public UnityEngine.Events.UnityAction clickHandler;
        public Image bgImage;
        public Image borderImage;
        public Image statusDotImage;
        public GameObject checkIconRoot;
        public TMP_Text txtNumber;
        public TMP_Text txtTitle;
        public TMP_Text txtConceptTag;
    }

    private LevelAndUIManager levelManager;
    private CompactHudMenuController compactHudMenu;
    private AirWeavingSpatialSfx spatialSfx;
    private TMP_FontAsset safeFont;

    private GameObject overlayRootObj;
    private RectTransform safeAreaRect;
    private RectTransform contentContainerRect;

    private Image progressPillBorderImg;
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
    private TMP_Text txtDetailTitle;
    private TMP_Text txtDetailMission;
    private TMP_Text txtDetailQuotaValue;
    private Image detailStatusCardBorder;
    private Image detailStatusDot;
    private TMP_Text txtDetailStatus;

    private RectTransform targetGateSlot1Rect;
    private Image targetGateSlot1Border;
    private Image targetGateSlot1Icon;
    private RectTransform targetGateSlot1LabelRect;
    private TMP_Text txtTargetGateSlot1;
    private GameObject targetGateSlot2Obj;
    private Image targetGateSlot2Icon;
    private TMP_Text txtTargetGateSlot2;

    private Image detailFormulaCardBorder;
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
        progressPillBorderImg = null;
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
        txtDetailTitle = null;
        txtDetailMission = null;
        txtDetailQuotaValue = null;
        detailStatusCardBorder = null;
        detailStatusDot = null;
        txtDetailStatus = null;
        targetGateSlot1Rect = null;
        targetGateSlot1Border = null;
        targetGateSlot1Icon = null;
        targetGateSlot1LabelRect = null;
        txtTargetGateSlot1 = null;
        targetGateSlot2Obj = null;
        targetGateSlot2Icon = null;
        txtTargetGateSlot2 = null;
        detailFormulaCardBorder = null;
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

        // Pil progres header: X/7 SELESAI tepat di sebelah kanan judul utama (1:1 Figma)
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

        if (progressPillBorderImg != null)
        {
            progressPillBorderImg.color = clearedCount > 0 ? GwThemeTokens.StatusPassEmerald : GwThemeTokens.HairlineIdle;
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
            return GwThemeTokens.SignalHighTeal;
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
                card.borderImage.color = GwThemeTokens.HairlineIdle;
            }
            else
            {
                card.borderImage.color = GwThemeTokens.HairlineIdle;
            }
        }

        // Di pojok kanan atas kartu node: centang hijau jika lulus, atau titik status kecil jika belum lulus (1:1 Figma)
        if (card.checkIconRoot != null && card.checkIconRoot.activeSelf != isCleared)
        {
            card.checkIconRoot.SetActive(isCleared);
        }

        if (card.statusDotImage != null)
        {
            bool showDot = !isCleared;
            if (card.statusDotImage.enabled != showDot)
            {
                card.statusDotImage.enabled = showDot;
            }
            if (showDot)
            {
                card.statusDotImage.color = (isSelected || isCurrentGameplay)
                    ? GwThemeTokens.StatusErrorRed
                    : GwThemeTokens.SignalLowSlate;
            }
        }

        if (card.txtNumber != null)
        {
            card.txtNumber.color = isSelected
                ? GwThemeTokens.SignalHighTeal
                : GwThemeTokens.TextPrimary;
        }

        if (card.txtTitle != null)
        {
            card.txtTitle.color = (isSelected || isCleared)
                ? GwThemeTokens.TextPrimary
                : GwThemeTokens.TextSecondary;
            card.txtTitle.text = CachedNodeUppercaseTitles[index];
        }

        if (card.txtConceptTag != null)
        {
            card.txtConceptTag.text = isCleared
                ? CachedNodeClearedGateTags[index]
                : CachedNodeLockedConceptTags[index];
            card.txtConceptTag.color = isSelected
                ? GwThemeTokens.SignalHighTeal
                : GwThemeTokens.SignalLowSlate;
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
            txtDetailLevelTag.text = isCleared
                ? CachedDetailHeaderClearedTags[safeIdx]
                : CachedDetailHeaderLockedTags[safeIdx];
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

        // Kotak 1 (Kiri): KUOTA GERBANG -> "X Gerbang"
        int quota = ResolveLevelQuota(safeIdx);
        if (txtDetailQuotaValue != null)
        {
            sb.Clear();
            sb.Append(quota).Append(" Gerbang");
            txtDetailQuotaValue.SetText(sb);
        }

        // Kotak 2 (Kanan): STATUS -> SELESAI / AKTIF / TERKUNCI (1:1 Figma)
        if (txtDetailStatus != null)
        {
            if (isCleared)
            {
                txtDetailStatus.text = StrStatusCleared;
                txtDetailStatus.color = GwThemeTokens.StatusPassText;
                SetImageColorIfChanged(detailStatusDot, GwThemeTokens.StatusPassEmerald);
                SetImageColorIfChanged(detailStatusCardBorder, GwThemeTokens.StatusPassEmerald);
            }
            else if (isCurrentGameplay)
            {
                txtDetailStatus.text = StrStatusActive;
                txtDetailStatus.color = GwThemeTokens.SignalHighTeal;
                SetImageColorIfChanged(detailStatusDot, GwThemeTokens.SignalHighTeal);
                SetImageColorIfChanged(detailStatusCardBorder, GwThemeTokens.SignalHighTeal);
            }
            else
            {
                txtDetailStatus.text = StrStatusLocked;
                txtDetailStatus.color = GwThemeTokens.StatusErrorText;
                SetImageColorIfChanged(detailStatusDot, GwThemeTokens.StatusErrorRed);
                SetImageColorIfChanged(detailStatusCardBorder, GwThemeTokens.StatusErrorRed);
            }
        }

        // Proteksi Pedagogis (Anti-Bocoran) pada GERBANG TARGET & EKSPRESI BOOLEAN
        const float padX = 24f;
        float innerW = DetailPanelWidth - (padX * 2f);
        float halfSlotW = (innerW - 12f) * 0.5f;

        if (isCleared)
        {
            int gateCount = CachedTargetGateCounts[safeIdx];
            bool hasTwoGates = gateCount >= 2;

            if (targetGateSlot1Rect != null)
            {
                targetGateSlot1Rect.sizeDelta = new Vector2(hasTwoGates ? halfSlotW : innerW, 44f);
            }
            if (targetGateSlot1Border != null)
            {
                targetGateSlot1Border.color = GwThemeTokens.HairlineIdle;
            }
            if (targetGateSlot1Icon != null)
            {
                targetGateSlot1Icon.sprite = GwThemeTokens.GetIeeeGateSprite(CachedPrimaryGateIcons[safeIdx]);
                targetGateSlot1Icon.color = GwThemeTokens.SignalHighTeal;
            }
            if (targetGateSlot1LabelRect != null)
            {
                targetGateSlot1LabelRect.sizeDelta = new Vector2((hasTwoGates ? halfSlotW : innerW) - 70f, 44f);
            }
            if (txtTargetGateSlot1 != null)
            {
                txtTargetGateSlot1.text = CachedPrimaryGateLabels[safeIdx];
                txtTargetGateSlot1.color = GwThemeTokens.TextPrimary;
            }

            if (targetGateSlot2Obj != null && targetGateSlot2Obj.activeSelf != hasTwoGates)
            {
                targetGateSlot2Obj.SetActive(hasTwoGates);
            }
            if (hasTwoGates)
            {
                if (targetGateSlot2Icon != null)
                {
                    targetGateSlot2Icon.sprite = GwThemeTokens.GetIeeeGateSprite(CachedSecondaryGateIcons[safeIdx]);
                    targetGateSlot2Icon.color = GwThemeTokens.SignalHighTeal;
                }
                if (txtTargetGateSlot2 != null)
                {
                    txtTargetGateSlot2.text = CachedSecondaryGateLabels[safeIdx];
                    txtTargetGateSlot2.color = GwThemeTokens.TextPrimary;
                }
            }

            SetImageColorIfChanged(detailFormulaCardBorder, GwThemeTokens.HairlineIdle);
            if (txtDetailFormulaHeading != null)
            {
                txtDetailFormulaHeading.text = safeIdx == 6 ? "HUKUM DE MORGAN" : "EKSPRESI BOOLEAN";
                txtDetailFormulaHeading.color = GwThemeTokens.TextSecondary;
            }
            if (txtDetailFormulaValue != null)
            {
                txtDetailFormulaValue.text = CachedVerifiedFormulas[safeIdx];
                txtDetailFormulaValue.color = GwThemeTokens.SignalHighTeal;
            }
        }
        else
        {
            if (targetGateSlot1Rect != null)
            {
                targetGateSlot1Rect.sizeDelta = new Vector2(innerW, 44f);
            }
            if (targetGateSlot1Border != null)
            {
                targetGateSlot1Border.color = GwThemeTokens.HairlineIdle;
            }
            if (targetGateSlot1Icon != null)
            {
                targetGateSlot1Icon.sprite = GwThemeTokens.GetIeeeGateSprite(GwThemeTokens.IeeeGateIconKind.And);
                targetGateSlot1Icon.color = GwThemeTokens.SignalLowSlate;
            }
            if (targetGateSlot1LabelRect != null)
            {
                targetGateSlot1LabelRect.sizeDelta = new Vector2(innerW - 70f, 44f);
            }
            if (txtTargetGateSlot1 != null)
            {
                txtTargetGateSlot1.text = StrTargetGateLocked;
                txtTargetGateSlot1.color = GwThemeTokens.SignalLowSlate;
            }

            if (targetGateSlot2Obj != null && targetGateSlot2Obj.activeSelf)
            {
                targetGateSlot2Obj.SetActive(false);
            }

            SetImageColorIfChanged(detailFormulaCardBorder, GwThemeTokens.HairlineIdle);
            if (txtDetailFormulaHeading != null)
            {
                txtDetailFormulaHeading.text = safeIdx == 6 ? "HUKUM DE MORGAN" : "EKSPRESI BOOLEAN";
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
        // Panel Header tipis di atas (1:1 Figma: PILIH LEVEL // CIRCUIT MAP + pil progres di sebelahnya + X KELUAR di kanan)
        GameObject headerObj = new GameObject("Header_CircuitMap", typeof(RectTransform));
        headerObj.transform.SetParent(parent, false);
        RectTransform headerRect = headerObj.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(0f, 1f);
        headerRect.pivot = new Vector2(0f, 1f);
        headerRect.anchoredPosition = Vector2.zero;
        headerRect.sizeDelta = new Vector2(ContentWidth, HeaderHeight);

        CreateTopLeftTmp(
            "Txt_MapHeaderTitle",
            headerRect,
            new Vector2(0f, -10f),
            new Vector2(372f, 40f),
            "PILIH LEVEL // CIRCUIT MAP",
            22f,
            26f,
            GwThemeTokens.TextPrimary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        // Pil progres X/7 SELESAI tepat di sebelah kanan judul utama (1:1 Figma)
        const float progressPillW = 136f;
        const float progressPillH = 30f;
        const float progressPillX = 382f;
        const float progressPillY = -15f;

        GameObject progObj = new GameObject("Pill_LevelProgress", typeof(RectTransform), typeof(Image));
        progObj.transform.SetParent(headerRect, false);
        RectTransform progRect = progObj.GetComponent<RectTransform>();
        progRect.anchorMin = new Vector2(0f, 1f);
        progRect.anchorMax = new Vector2(0f, 1f);
        progRect.pivot = new Vector2(0f, 1f);
        progRect.anchoredPosition = new Vector2(progressPillX, progressPillY);
        progRect.sizeDelta = new Vector2(progressPillW, progressPillH);

        Image progBg = progObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(progBg, GwThemeTokens.SmokedGlass);
        progBg.raycastTarget = false;
        progressPillBorderImg = GwThemeTokens.AttachHairlineBorder(progRect, "Pill_LevelProgress_Border", GwThemeTokens.StatusPassEmerald);

        progressDotImg = CreateCircleDot("Progress_Dot", progRect, 15f, -progressPillH * 0.5f, 6f, GwThemeTokens.StatusPassEmerald);

        txtProgressPill = CreateTopLeftTmp(
            "Txt_ProgressPill",
            progRect,
            new Vector2(26f, 0f),
            new Vector2(progressPillW - 34f, progressPillH),
            "0/7 SELESAI",
            10.5f,
            12f,
            GwThemeTokens.StatusPassText,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        // Tombol X KELUAR di pojok kanan atas
        const float exitBtnW = 124f;
        const float exitBtnH = 38f;
        btnExitX = CreateRoundedFlatButton(
            "Btn_LevelSelectorExit",
            headerRect,
            ContentWidth - exitBtnW,
            -11f,
            exitBtnW,
            exitBtnH,
            "X  KELUAR",
            GwThemeTokens.CardIdle,
            GwThemeTokens.HairlineIdle,
            GwThemeTokens.TextPrimary,
            12f,
            14f,
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
            new Vector2(0f, -HeaderHeight),
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

        // Label metadata tunggal tenang di kiri atas peta (1:1 Figma)
        CreateTopLeftTmp(
            "Txt_MapSectionLabel",
            mapRect,
            new Vector2(28f, -22f),
            new Vector2(MapPanelWidth - 56f, 20f),
            "KURIKULUM MERDEKA FASE E  //  PROGRESI SIRKUIT 01-07",
            10.5f,
            12f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        // Geometri 2 baris node kartu kompak (01-04 baris atas, 05-07 baris bawah terpusat, ruang grid lega seperti Figma)
        const float cardW = 206f;
        const float cardH = 148f;
        const float cardGapX = 82f;
        float row1TotalW = (4f * cardW) + (3f * cardGapX);
        float row1StartX = (MapPanelWidth - row1TotalW) * 0.5f;
        const float row1TopY = -176f;
        const float rowGapY = 120f;
        const float row2TopY = row1TopY - cardH - rowGapY;

        float[] row1LeftX = new float[4];
        for (int c = 0; c < 4; c++)
        {
            row1LeftX[c] = row1StartX + (c * (cardW + cardGapX));
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

        // Layer 3: Legenda 2 item tenang di kiri bawah peta tanpa garis divider (1:1 Figma)
        float legendY = -(BodyHeight - 36f);
        CreateGeometricCheckmark("Legend_Check", mapRect, 34f, legendY - 10f, GwThemeTokens.StatusPassText);
        CreateTopLeftTmp(
            "LegendTxt_Cleared",
            mapRect,
            new Vector2(46f, legendY),
            new Vector2(120f, 20f),
            "SELESAI",
            10f,
            11.5f,
            GwThemeTokens.StatusPassText,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        CreateCircleDot("Legend_ActiveDot", mapRect, 144f, legendY - 10f, 6f, GwThemeTokens.SignalHighTeal);
        CreateTopLeftTmp(
            "LegendTxt_Active",
            mapRect,
            new Vector2(154f, legendY),
            new Vector2(260f, 20f),
            "AKTIF / BELUM TERVERIFIKASI",
            10f,
            11.5f,
            GwThemeTokens.SignalHighTeal,
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

        int capturedIndex = levelIndex;
        UnityEngine.Events.UnityAction clickHandler = () => HandleNodeCardClicked(capturedIndex);

        Button btn = obj.GetComponent<Button>();
        btn.targetGraphic = bg;
        btn.transition = Selectable.Transition.ColorTint;
        btn.colors = GwThemeTokens.FlatGlassButtonColors;
        btn.onClick.AddListener(clickHandler);

        const float padX = 16f;
        float innerW = width - (padX * 2f);

        // 1. Kiri atas: Nomor level (01..07)
        TMP_Text numTmp = CreateTopLeftTmp(
            "Txt_NodeNum",
            rect,
            new Vector2(padX, -14f),
            new Vector2(60f, 28f),
            CachedLevelNumbers[levelIndex],
            17f,
            21f,
            GwThemeTokens.TextPrimary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        // Kanan atas: Centang hijau jika lulus, atau titik status kecil jika belum lulus (1:1 Figma)
        GameObject checkRoot = CreateGeometricCheckmark(
            "Icon_ClearedCheck",
            rect,
            width - padX - 6f,
            -26f,
            GwThemeTokens.StatusPassText);
        checkRoot.SetActive(false);

        Image statusDotImg = CreateCircleDot(
            "Icon_StatusDot",
            rect,
            width - padX - 4f,
            -26f,
            6f,
            GwThemeTokens.StatusErrorRed);

        // 2. Tengah: Judul singkat uppercase (tanpa divider internal)
        TMP_Text titleTmp = CreateTopLeftTmp(
            "Txt_NodeTitle",
            rect,
            new Vector2(padX, -56f),
            new Vector2(innerW, 44f),
            CachedNodeUppercaseTitles[levelIndex],
            11.5f,
            13.5f,
            GwThemeTokens.TextPrimary,
            TextAlignmentOptions.TopLeft,
            FontStyles.Bold,
            allowWrap: true);

        // 3. Kiri bawah: Tag konsep/gerbang singkat (tanpa pengulangan KUOTA: X dan SELESAI)
        TMP_Text conceptTmp = CreateTopLeftTmp(
            "Txt_NodeConceptTag",
            rect,
            new Vector2(padX, -(height - 30f)),
            new Vector2(innerW, 18f),
            CachedNodeLockedConceptTags[levelIndex],
            9.5f,
            11f,
            GwThemeTokens.SignalLowSlate,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        return new MapNodeCardUi
        {
            button = btn,
            clickHandler = clickHandler,
            bgImage = bg,
            borderImage = border,
            statusDotImage = statusDotImg,
            checkIconRoot = checkRoot,
            txtNumber = numTmp,
            txtTitle = titleTmp,
            txtConceptTag = conceptTmp
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

        // ---------------------------------------------------------------------
        // 1. HEADER DETAIL: Eyebrow NODE SIRKUIT TERPILIH + LEVEL 0X + Sub-judul
        // ---------------------------------------------------------------------
        CreateTopLeftTmp(
            "Txt_DetailEyebrow",
            panelRect,
            new Vector2(padX, -24f),
            new Vector2(innerW, 16f),
            "NODE SIRKUIT TERPILIH",
            10f,
            11.5f,
            GwThemeTokens.SignalHighTeal,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        txtDetailLevelTag = CreateTopLeftTmp(
            "Txt_DetailLevelTag",
            panelRect,
            new Vector2(padX, -44f),
            new Vector2(innerW, 30f),
            CachedDetailHeaderLockedTags[0],
            19f,
            23f,
            GwThemeTokens.TextPrimary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        txtDetailTitle = CreateTopLeftTmp(
            "Txt_DetailTitle",
            panelRect,
            new Vector2(padX, -76f),
            new Vector2(innerW, 22f),
            CachedLevelShortTitles[0],
            12f,
            14f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Normal);

        // ---------------------------------------------------------------------
        // 2. KARTU MISI OPERASI (label MISI OPERASI di dalam kartu 1:1 Figma)
        // ---------------------------------------------------------------------
        GameObject missionCardObj = new GameObject("Card_DetailMission", typeof(RectTransform), typeof(Image));
        missionCardObj.transform.SetParent(panelRect, false);
        RectTransform missionCardRect = missionCardObj.GetComponent<RectTransform>();
        missionCardRect.anchorMin = new Vector2(0f, 1f);
        missionCardRect.anchorMax = new Vector2(0f, 1f);
        missionCardRect.pivot = new Vector2(0f, 1f);
        missionCardRect.anchoredPosition = new Vector2(padX, -118f);
        missionCardRect.sizeDelta = new Vector2(innerW, 96f);

        Image missionBg = missionCardObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(missionBg, GwThemeTokens.CardIdle);
        missionBg.raycastTarget = false;
        GwThemeTokens.AttachHairlineBorder(missionCardRect, "Card_DetailMission_Border", GwThemeTokens.HairlineIdle);

        CreateTopLeftTmp(
            "Txt_MissionLabel",
            missionCardRect,
            new Vector2(16f, -12f),
            new Vector2(innerW - 32f, 16f),
            "MISI OPERASI",
            9.5f,
            11f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        txtDetailMission = CreateTopLeftTmp(
            "Txt_DetailMission",
            missionCardRect,
            new Vector2(16f, -32f),
            new Vector2(innerW - 32f, 52f),
            CachedFallbackMissions[0],
            13f,
            15f,
            GwThemeTokens.TextPrimary,
            TextAlignmentOptions.TopLeft,
            FontStyles.Normal,
            allowWrap: true);

        // ---------------------------------------------------------------------
        // 3. DUA KOTAK METRIK 50:50 (KUOTA GERBANG | STATUS) 1:1 FIGMA
        // ---------------------------------------------------------------------
        const float metricGap = 12f;
        float halfMetricW = (innerW - metricGap) * 0.5f;
        const float metricCardY = -228f;
        const float metricCardH = 66f;

        GameObject quotaCardObj = new GameObject("Card_DetailQuota", typeof(RectTransform), typeof(Image));
        quotaCardObj.transform.SetParent(panelRect, false);
        RectTransform quotaCardRect = quotaCardObj.GetComponent<RectTransform>();
        quotaCardRect.anchorMin = new Vector2(0f, 1f);
        quotaCardRect.anchorMax = new Vector2(0f, 1f);
        quotaCardRect.pivot = new Vector2(0f, 1f);
        quotaCardRect.anchoredPosition = new Vector2(padX, metricCardY);
        quotaCardRect.sizeDelta = new Vector2(halfMetricW, metricCardH);

        Image quotaBg = quotaCardObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(quotaBg, GwThemeTokens.CardIdle);
        quotaBg.raycastTarget = false;
        GwThemeTokens.AttachHairlineBorder(quotaCardRect, "Card_DetailQuota_Border", GwThemeTokens.HairlineIdle);

        CreateTopLeftTmp(
            "Txt_QuotaHeading",
            quotaCardRect,
            new Vector2(14f, -10f),
            new Vector2(halfMetricW - 28f, 16f),
            "KUOTA GERBANG",
            9.5f,
            11f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        txtDetailQuotaValue = CreateTopLeftTmp(
            "Txt_DetailQuotaValue",
            quotaCardRect,
            new Vector2(14f, -30f),
            new Vector2(halfMetricW - 28f, 24f),
            "1 Gerbang",
            14f,
            16.5f,
            GwThemeTokens.TextPrimary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        GameObject statusCardObj = new GameObject("Card_DetailStatus", typeof(RectTransform), typeof(Image));
        statusCardObj.transform.SetParent(panelRect, false);
        RectTransform statusCardRect = statusCardObj.GetComponent<RectTransform>();
        statusCardRect.anchorMin = new Vector2(0f, 1f);
        statusCardRect.anchorMax = new Vector2(0f, 1f);
        statusCardRect.pivot = new Vector2(0f, 1f);
        statusCardRect.anchoredPosition = new Vector2(padX + halfMetricW + metricGap, metricCardY);
        statusCardRect.sizeDelta = new Vector2(halfMetricW, metricCardH);

        Image statusCardBg = statusCardObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(statusCardBg, GwThemeTokens.CardIdle);
        statusCardBg.raycastTarget = false;
        detailStatusCardBorder = GwThemeTokens.AttachHairlineBorder(statusCardRect, "Card_DetailStatus_Border", GwThemeTokens.StatusErrorRed);

        CreateTopLeftTmp(
            "Txt_StatusHeading",
            statusCardRect,
            new Vector2(14f, -10f),
            new Vector2(halfMetricW - 28f, 16f),
            "STATUS",
            9.5f,
            11f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        detailStatusDot = CreateCircleDot("DetailStatus_Dot", statusCardRect, 18f, -42f, 6f, GwThemeTokens.StatusErrorRed);

        txtDetailStatus = CreateTopLeftTmp(
            "Txt_DetailStatus",
            statusCardRect,
            new Vector2(28f, -30f),
            new Vector2(halfMetricW - 38f, 24f),
            StrStatusLocked,
            12f,
            14f,
            GwThemeTokens.StatusErrorText,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        // ---------------------------------------------------------------------
        // 4. SEKSI GERBANG TARGET (dengan Simbol IEEE & Proteksi Pedagogis)
        // ---------------------------------------------------------------------
        CreateTopLeftTmp(
            "Txt_TargetGatesHeading",
            panelRect,
            new Vector2(padX, -310f),
            new Vector2(innerW, 16f),
            "GERBANG TARGET",
            10f,
            11.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        const float gateSlotY = -332f;
        const float gateSlotH = 44f;

        targetGateSlot1Rect = BuildTargetGateSlot(
            "Slot_TargetGate_1",
            panelRect,
            padX,
            gateSlotY,
            halfMetricW,
            gateSlotH,
            GwThemeTokens.IeeeGateIconKind.Or,
            "OR",
            out targetGateSlot1Border,
            out targetGateSlot1Icon,
            out targetGateSlot1LabelRect,
            out txtTargetGateSlot1);

        RectTransform slot2Rect = BuildTargetGateSlot(
            "Slot_TargetGate_2",
            panelRect,
            padX + halfMetricW + metricGap,
            gateSlotY,
            halfMetricW,
            gateSlotH,
            GwThemeTokens.IeeeGateIconKind.Not,
            "NOT",
            out _,
            out targetGateSlot2Icon,
            out _,
            out txtTargetGateSlot2);
        targetGateSlot2Obj = slot2Rect.gameObject;

        // ---------------------------------------------------------------------
        // 5. KARTU EKSPRESI BOOLEAN / HUKUM DE MORGAN (Proteksi Pedagogis)
        // ---------------------------------------------------------------------
        GameObject formulaObj = new GameObject("Card_DetailFormula", typeof(RectTransform), typeof(Image));
        formulaObj.transform.SetParent(panelRect, false);
        RectTransform formulaRect = formulaObj.GetComponent<RectTransform>();
        formulaRect.anchorMin = new Vector2(0f, 1f);
        formulaRect.anchorMax = new Vector2(0f, 1f);
        formulaRect.pivot = new Vector2(0f, 1f);
        formulaRect.anchoredPosition = new Vector2(padX, -392f);
        formulaRect.sizeDelta = new Vector2(innerW, 74f);

        Image formulaBg = formulaObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(formulaBg, GwThemeTokens.CardIdle);
        formulaBg.raycastTarget = false;
        detailFormulaCardBorder = GwThemeTokens.AttachHairlineBorder(formulaRect, "Card_DetailFormula_Border", GwThemeTokens.HairlineIdle);

        txtDetailFormulaHeading = CreateTopLeftTmp(
            "Txt_DetailFormulaHeading",
            formulaRect,
            new Vector2(16f, -10f),
            new Vector2(innerW - 32f, 16f),
            "EKSPRESI BOOLEAN",
            9.5f,
            11f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        txtDetailFormulaValue = CreateTopLeftTmp(
            "Txt_DetailFormulaValue",
            formulaRect,
            new Vector2(16f, -30f),
            new Vector2(innerW - 32f, 32f),
            StrFormulaLocked,
            12.5f,
            16.5f,
            GwThemeTokens.SignalLowSlate,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        // ---------------------------------------------------------------------
        // 6. TOMBOL AKSI BAWAH: MULAI SIMULASI -> & SANDBOX MODE (tanpa divider)
        // ---------------------------------------------------------------------
        btnStartSimulation = CreateRoundedFlatButton(
            "Btn_StartSelectedLevel",
            panelRect,
            padX,
            -(BodyHeight - 122f),
            innerW,
            52f,
            "MULAI SIMULASI ->",
            GwThemeTokens.PrimaryFill,
            GwThemeTokens.PrimaryFill,
            GwThemeTokens.PrimaryText,
            14.5f,
            18f,
            hasBorder: false,
            HandleStartSimulationClicked,
            out _);

        btnEnterSandbox = CreateRoundedFlatButton(
            "Btn_OpenSandboxFromMap",
            panelRect,
            padX,
            -(BodyHeight - 58f),
            innerW,
            44f,
            "SANDBOX MODE",
            GwThemeTokens.CardIdle,
            GwThemeTokens.HairlineActive,
            GwThemeTokens.TextPrimary,
            13f,
            15.5f,
            hasBorder: true,
            HandleSandboxClicked,
            out _);
    }

    private RectTransform BuildTargetGateSlot(
        string name,
        RectTransform parent,
        float leftX,
        float topY,
        float width,
        float height,
        GwThemeTokens.IeeeGateIconKind initialKind,
        string initialLabel,
        out Image borderOut,
        out Image iconOut,
        out RectTransform labelRectOut,
        out TMP_Text labelOut)
    {
        GameObject slotObj = new GameObject(name, typeof(RectTransform), typeof(Image));
        slotObj.transform.SetParent(parent, false);
        RectTransform slotRect = slotObj.GetComponent<RectTransform>();
        slotRect.anchorMin = new Vector2(0f, 1f);
        slotRect.anchorMax = new Vector2(0f, 1f);
        slotRect.pivot = new Vector2(0f, 1f);
        slotRect.anchoredPosition = new Vector2(leftX, topY);
        slotRect.sizeDelta = new Vector2(width, height);

        Image slotBg = slotObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(slotBg, GwThemeTokens.CardIdle);
        slotBg.raycastTarget = false;
        borderOut = GwThemeTokens.AttachHairlineBorder(slotRect, $"{name}_Border", GwThemeTokens.HairlineIdle);

        GameObject iconObj = new GameObject("Icon_IeeeGate", typeof(RectTransform), typeof(Image));
        iconObj.transform.SetParent(slotRect, false);
        RectTransform iconRect = iconObj.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0f, 0.5f);
        iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(12f, 0f);
        iconRect.sizeDelta = new Vector2(40f, 24f);

        iconOut = iconObj.GetComponent<Image>();
        iconOut.sprite = GwThemeTokens.GetIeeeGateSprite(initialKind);
        iconOut.type = Image.Type.Simple;
        iconOut.preserveAspect = true;
        iconOut.color = GwThemeTokens.SignalHighTeal;
        iconOut.raycastTarget = false;

        labelOut = CreateTopLeftTmp(
            "Txt_GateLabel",
            slotRect,
            new Vector2(56f, 0f),
            new Vector2(width - 68f, height),
            initialLabel,
            11.5f,
            13.5f,
            GwThemeTokens.TextPrimary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);
        labelRectOut = labelOut.rectTransform;

        return slotRect;
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
