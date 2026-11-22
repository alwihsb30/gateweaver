using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Modal Verifikasi Lulus 4/4 khusus Mode Udara GateWeaver AR v8.5 (07_Modal_Level_Complete).
/// - Desain Smoked Optical Glass + Brushed Titanium 100% berbasis GwThemeTokens:
///   * Panel utama SmokedGlassDense (82%) + border 1px StatusPassEmerald (#10B981) + radius 6px.
///   * Header VERIFIKASI LULUS 4/4 (StatusPassText #34D399) + info level aktif.
///   * Kartu rumus Boolean terverifikasi (Y = ... dalam TextPrimary bold).
///   * Ringkasan tabel kebenaran 4 baris (A | B | Y | TGT | OK dalam #34D399).
///   * Metrik kuota gerbang (aktif/maks + status verifikasi).
///   * Tombol primer solid putih LANJUT LEVEL -&gt; (PrimaryFill + PrimaryText bold)
///     dan tombol sekunder outline ULANGI LEVEL (CardIdle + HairlineActive).
/// - 0 perubahan di Core/ &amp; Levels/, 0% regresi Mode Kartu Vuforia v5.
/// - 100% Zero-GC di Tick(), pure ASCII 7-bit, tanpa LINQ, tanpa neon glow.
/// </summary>
public sealed class AirWeavingLevelCompleteModal : MonoBehaviour
{
    private const int TruthRowCount = 4;
    private const int OverlayCanvasSortingOrder = 20;
    private const float ReferenceCanvasWidth = 1920f;
    private const float ReferenceCanvasHeight = 1080f;
    private const float ModalWidth = 820f;
    private const float ModalHeight = 626f;
    private const float ShowDelayAfterVerifySeconds = 0.35f;

    private static readonly string[] CachedLevelTitles =
    {
        "Alarm Pintu & Jendela",
        "Sakelar Tangga Dua Arah",
        "Kunci Keamanan Brankas",
        "Pemutus Darurat Reaktor",
        "Sinkronisasi Inti Kembar",
        "Sensor Hening Server (NOR)",
        "Kunci Biometrik De Morgan"
    };

    private static readonly string[] RowInputALabels = { "0", "0", "1", "1" };
    private static readonly string[] RowInputBLabels = { "0", "1", "0", "1" };
    private static readonly bool[] RowInputABits = { false, false, true, true };
    private static readonly bool[] RowInputBBits = { false, true, false, true };

    private sealed class SummaryRowUi
    {
        public TMP_Text txtY;
        public TMP_Text txtTgt;
        public TMP_Text txtStatus;
    }

    private LevelAndUIManager levelManager;
    private CircuitManager circuitManager;
    private CompactHudMenuController compactHudMenu;
    private AirWeavingPedagogicalHud pedagogicalHud;
    private AirWeavingSpatialSfx spatialSfx;
    private GateNode outputNode;
    private TMP_FontAsset safeFont;

    private GameObject modalRootObj;
    private Button scrimCloseButton;
    private RectTransform safeAreaRect;
    private RectTransform cardRect;
    private Button btnCloseX;

    private TMP_Text txtLevelSubtitle;
    private TMP_Text txtVerifiedFormula;
    private readonly SummaryRowUi[] summaryRows = new SummaryRowUi[TruthRowCount];
    private TMP_Text txtQuotaValue;
    private TMP_Text txtVerifySummaryValue;
    private Button btnReplayLevel;
    private Button btnNextLevel;
    private TMP_Text txtNextLevelLabel;

    private readonly StringBuilder sb = new StringBuilder(96);

    private bool isUiBuilt = false;
    private bool isAirWeavingActive = false;
    private bool isModalOpen = false;
    private bool hasShownForCurrentVictory = false;
    private float pendingShowDelayTimer = -1f;
    private int lastObservedLevelIndex = -1;

    private Rect lastAppliedSafeArea;
    private int lastScreenW = -1;
    private int lastScreenH = -1;
    private bool hasAppliedScale = false;

    public bool IsModalOpen => isModalOpen;

    public void Configure(
        LevelAndUIManager manager,
        CircuitManager circuit,
        CompactHudMenuController hudMenu,
        AirWeavingPedagogicalHud pedHud,
        AirWeavingSpatialSfx sfx,
        GateNode output,
        TMP_FontAsset fallbackFont = null)
    {
        levelManager = manager;
        circuitManager = circuit;
        compactHudMenu = hudMenu;
        pedagogicalHud = pedHud;
        spatialSfx = sfx;
        outputNode = output;

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
        ResetVictoryLatchAndClose();
    }

    public void OnAirWeavingModeExited()
    {
        isAirWeavingActive = false;
        ResetVictoryLatchAndClose();
    }

    public void NotifyLevelOrWorkbenchReset()
    {
        ResetVictoryLatchAndClose();
    }

    public void OnExternalModalOrMenuOpened()
    {
        if (isModalOpen)
        {
            CloseModalOnly();
        }
        pendingShowDelayTimer = -1f;
    }

    /// <summary>
    /// Memantau transisi kemenangan Mode Udara (IsReadyForNextLevel &amp;&amp; !IsVerifying) secara Zero-GC
    /// dan menampilkan 07_Modal_Level_Complete satu kali setelah jeda selebrasi singkat.
    /// </summary>
    public void Tick(float dt, bool airWeavingActive, bool isOtherUiOverlayOpen)
    {
        isAirWeavingActive = airWeavingActive;

        if (!isAirWeavingActive || levelManager == null)
        {
            if (isModalOpen)
            {
                ResetVictoryLatchAndClose();
            }
            return;
        }

        int curLevelIdx = levelManager.currentLevelIndex;
        bool isSandbox = levelManager.IsSandboxMode;
        bool isReadyNext = levelManager.IsReadyForNextLevel;
        bool isVerifying = levelManager.IsVerifying;

        if (curLevelIdx != lastObservedLevelIndex)
        {
            lastObservedLevelIndex = curLevelIdx;
            ResetVictoryLatchAndClose();
        }

        if (isSandbox || !isReadyNext)
        {
            hasShownForCurrentVictory = false;
            pendingShowDelayTimer = -1f;
            if (isModalOpen)
            {
                CloseModalOnly();
            }
            return;
        }

        if (isOtherUiOverlayOpen)
        {
            if (isModalOpen)
            {
                CloseModalOnly();
            }
            pendingShowDelayTimer = -1f;
            return;
        }

        if (isModalOpen)
        {
            ApplySafeAreaAndResponsiveScaleIfNeeded(force: false);
            if (spatialSfx != null)
            {
                spatialSfx.StopHumImmediate();
            }
            return;
        }

        if (!hasShownForCurrentVictory && !isVerifying)
        {
            if (pendingShowDelayTimer < 0f)
            {
                pendingShowDelayTimer = ShowDelayAfterVerifySeconds;
            }
            else
            {
                pendingShowDelayTimer -= dt;
                if (pendingShowDelayTimer <= 0f)
                {
                    pendingShowDelayTimer = -1f;
                    OpenModalForCurrentVictory();
                }
            }
        }
    }

    public void CleanupImmediate()
    {
        RemoveBtnListener(scrimCloseButton, HandleDismissClicked);
        RemoveBtnListener(btnCloseX, HandleDismissClicked);
        RemoveBtnListener(btnReplayLevel, HandleReplayLevelClicked);
        RemoveBtnListener(btnNextLevel, HandleNextLevelClicked);

        isAirWeavingActive = false;
        isModalOpen = false;
        hasShownForCurrentVictory = false;
        pendingShowDelayTimer = -1f;

        if (modalRootObj != null)
        {
            Destroy(modalRootObj);
            modalRootObj = null;
        }

        scrimCloseButton = null;
        safeAreaRect = null;
        cardRect = null;
        btnCloseX = null;
        txtLevelSubtitle = null;
        txtVerifiedFormula = null;
        for (int i = 0; i < summaryRows.Length; i++)
        {
            summaryRows[i] = null;
        }
        txtQuotaValue = null;
        txtVerifySummaryValue = null;
        btnReplayLevel = null;
        btnNextLevel = null;
        txtNextLevelLabel = null;
        levelManager = null;
        circuitManager = null;
        compactHudMenu = null;
        pedagogicalHud = null;
        spatialSfx = null;
        outputNode = null;
        safeFont = null;
        isUiBuilt = false;
        hasAppliedScale = false;
    }

    private void OnDestroy()
    {
        CleanupImmediate();
    }

    // =========================================================================
    // KONTROL BUKA / TUTUP & AKSI TOMBOL
    // =========================================================================

    private void OpenModalForCurrentVictory()
    {
        EnsureUiBuilt();
        if (modalRootObj == null || levelManager == null)
        {
            return;
        }

        hasShownForCurrentVictory = true;
        isModalOpen = true;

        if (compactHudMenu != null)
        {
            compactHudMenu.CloseMenuDrawer();
            compactHudMenu.ClearToastImmediate();
        }
        if (spatialSfx != null)
        {
            spatialSfx.StopHumImmediate();
        }

        PopulateVictoryData();

        modalRootObj.SetActive(true);
        ApplySafeAreaAndResponsiveScaleIfNeeded(force: true);
    }

    private void PopulateVictoryData()
    {
        if (levelManager == null)
        {
            return;
        }

        int levelCount = levelManager.levels != null && levelManager.levels.Count > 0
            ? levelManager.levels.Count
            : CachedLevelTitles.Length;
        int lvlIdx = Mathf.Clamp(levelManager.currentLevelIndex, 0, levelCount - 1);
        LevelScenario scenario = (levelManager.levels != null && lvlIdx < levelManager.levels.Count)
            ? levelManager.levels[lvlIdx]
            : null;

        // 1. Sub-header level
        if (txtLevelSubtitle != null)
        {
            sb.Clear();
            sb.Append("LVL ").Append(lvlIdx + 1).Append('/').Append(levelCount).Append("  //  ");
            if (lvlIdx < CachedLevelTitles.Length)
            {
                sb.Append(CachedLevelTitles[lvlIdx]);
            }
            else if (scenario != null && !string.IsNullOrEmpty(scenario.levelTitle))
            {
                sb.Append(scenario.levelTitle);
            }
            else
            {
                sb.Append("MISI KURIKULUM");
            }
            txtLevelSubtitle.SetText(sb);
        }

        // 2. Rumus Boolean terverifikasi (Y = ...)
        if (txtVerifiedFormula != null)
        {
            string formula = pedagogicalHud != null
                ? pedagogicalHud.FormatBooleanExpressionSnapshot(outputNode)
                : "Y = TERVERIFIKASI";
            if (string.IsNullOrEmpty(formula) || formula == "Y = --")
            {
                formula = "Y = TERVERIFIKASI";
            }
            txtVerifiedFormula.text = formula;
        }

        // 3. Ringkasan tabel kebenaran 4 baris (A | B | Y | TGT | OK dalam #34D399)
        for (int row = 0; row < TruthRowCount; row++)
        {
            SummaryRowUi rowUi = summaryRows[row];
            if (rowUi == null)
            {
                continue;
            }

            bool expectedBit = scenario != null
                               && scenario.expectedTruthTable != null
                               && row < scenario.expectedTruthTable.Length
                               && scenario.expectedTruthTable[row];
            string bitStr = expectedBit ? "1" : "0";

            if (rowUi.txtY != null)
            {
                rowUi.txtY.text = bitStr;
                rowUi.txtY.color = GwThemeTokens.StatusPassText;
            }
            if (rowUi.txtTgt != null)
            {
                rowUi.txtTgt.text = bitStr;
                rowUi.txtTgt.color = GwThemeTokens.StatusPassText;
            }
            if (rowUi.txtStatus != null)
            {
                rowUi.txtStatus.text = "OK";
                rowUi.txtStatus.color = GwThemeTokens.StatusPassText;
            }
        }

        // 4. Metrik Kuota Gerbang & Status Verifikasi
        int usedGates = circuitManager != null ? circuitManager.activeGateCardsCount : 1;
        int maxGates = scenario != null ? Mathf.Max(1, scenario.maxGateCardsQuota) : 1;
        if (txtQuotaValue != null)
        {
            sb.Clear();
            sb.Append(usedGates).Append(" / ").Append(maxGates).Append(" GERBANG (EFISIEN)");
            txtQuotaValue.SetText(sb);
            txtQuotaValue.color = GwThemeTokens.StatusPassText;
        }

        bool isLastLevel = lvlIdx >= (levelCount - 1);
        if (txtVerifySummaryValue != null)
        {
            txtVerifySummaryValue.text = isLastLevel
                ? "SEMUA 7 MISI SELESAI"
                : "4/4 KOMBINASI TERVERIFIKASI";
            txtVerifySummaryValue.color = GwThemeTokens.StatusPassText;
        }

        if (txtNextLevelLabel != null)
        {
            txtNextLevelLabel.text = isLastLevel
                ? "MODE SANDBOX ->"
                : "LANJUT LEVEL ->";
        }
    }

    private void ResetVictoryLatchAndClose()
    {
        hasShownForCurrentVictory = false;
        pendingShowDelayTimer = -1f;
        CloseModalOnly();
    }

    private void CloseModalOnly()
    {
        isModalOpen = false;
        if (modalRootObj != null && modalRootObj.activeSelf)
        {
            modalRootObj.SetActive(false);
        }
    }

    private void HandleDismissClicked()
    {
        CloseModalOnly();
    }

    private void HandleReplayLevelClicked()
    {
        ResetVictoryLatchAndClose();
        if (levelManager != null)
        {
            levelManager.LoadLevel(levelManager.currentLevelIndex);
        }
    }

    private void HandleNextLevelClicked()
    {
        ResetVictoryLatchAndClose();
        if (levelManager == null)
        {
            return;
        }

        int totalLevels = (levelManager.levels != null && levelManager.levels.Count > 0)
            ? levelManager.levels.Count
            : CachedLevelTitles.Length;
        bool isLastLevel = levelManager.currentLevelIndex >= (totalLevels - 1);
        if (isLastLevel)
        {
            levelManager.EnterSandboxMode();
        }
        else
        {
            levelManager.LoadLevel(levelManager.currentLevelIndex + 1);
        }
    }

    // =========================================================================
    // KONSTRUKSI UI PROSEDURAL 100% TOKEN GwThemeTokens (07_Modal_Level_Complete)
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

        BuildModalHierarchy();
        isUiBuilt = true;
        ApplySafeAreaAndResponsiveScaleIfNeeded(force: true);
    }

    private void BuildModalHierarchy()
    {
        // Dedicated ScreenSpaceOverlay Canvas (sortingOrder = 20) strictly di atas Top HUD (0) & Pedagogical HUD (5)
        modalRootObj = new GameObject(
            "Canvas_AirLevelCompleteOverlay_V85",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        modalRootObj.transform.SetParent(transform, false);

        Canvas overlayCanvas = modalRootObj.GetComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = OverlayCanvasSortingOrder;

        CanvasScaler scaler = modalRootObj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceCanvasWidth, ReferenceCanvasHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform rootRect = modalRootObj.GetComponent<RectTransform>();

        // Full-screen dark dim scrim (#090B10 62%, raycast blocker aktif) meredupkan Top HUD & 3D panggung
        GameObject scrimObj = new GameObject("Btn_LevelCompleteScrim_V85", typeof(RectTransform), typeof(Image), typeof(Button));
        scrimObj.transform.SetParent(rootRect, false);
        RectTransform scrimRect = scrimObj.GetComponent<RectTransform>();
        scrimRect.anchorMin = Vector2.zero;
        scrimRect.anchorMax = Vector2.one;
        scrimRect.offsetMin = Vector2.zero;
        scrimRect.offsetMax = Vector2.zero;

        Image scrimImg = scrimObj.GetComponent<Image>();
        scrimImg.color = GwThemeTokens.BackdropScrim;
        scrimImg.raycastTarget = true;

        scrimCloseButton = scrimObj.GetComponent<Button>();
        scrimCloseButton.targetGraphic = scrimImg;
        scrimCloseButton.transition = Selectable.Transition.None;
        scrimCloseButton.onClick.AddListener(HandleDismissClicked);

        // Kontainer Safe Area
        GameObject safeObj = new GameObject("LevelComplete_SafeArea", typeof(RectTransform));
        safeObj.transform.SetParent(rootRect, false);
        safeAreaRect = safeObj.GetComponent<RectTransform>();
        safeAreaRect.anchorMin = Vector2.zero;
        safeAreaRect.anchorMax = Vector2.one;
        safeAreaRect.offsetMin = Vector2.zero;
        safeAreaRect.offsetMax = Vector2.zero;

        // Kartu modal tengah: SmokedGlassOverlay (94%) + border 1px StatusPassEmerald (#10B981) + radius 6px
        GameObject cardObj = new GameObject("Card_LevelComplete_V85", typeof(RectTransform), typeof(Image));
        cardObj.transform.SetParent(safeAreaRect, false);
        cardRect = cardObj.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.anchoredPosition = Vector2.zero;
        cardRect.sizeDelta = new Vector2(ModalWidth, ModalHeight);

        Image cardBg = cardObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(cardBg, GwThemeTokens.SmokedGlassOverlay);
        cardBg.raycastTarget = true;

        GwThemeTokens.AttachHairlineBorder(cardRect, "Card_LevelComplete_Border", GwThemeTokens.StatusPassEmerald);

        const float padX = 28f;
        float contentW = ModalWidth - (padX * 2f);

        // ---------------------------------------------------------------------
        // HEADER: VERIFIKASI LULUS 4/4 (StatusPassText #34D399) + Sub Level + X
        // ---------------------------------------------------------------------
        GameObject statusDotObj = new GameObject("Header_PassDot", typeof(RectTransform), typeof(Image));
        statusDotObj.transform.SetParent(cardRect, false);
        RectTransform dotRect = statusDotObj.GetComponent<RectTransform>();
        dotRect.anchorMin = new Vector2(0f, 1f);
        dotRect.anchorMax = new Vector2(0f, 1f);
        dotRect.pivot = new Vector2(0.5f, 0.5f);
        dotRect.anchoredPosition = new Vector2(padX + 6f, -38f);
        dotRect.sizeDelta = new Vector2(10f, 10f);

        Image dotImg = statusDotObj.GetComponent<Image>();
        dotImg.sprite = GwThemeTokens.GetCircleSprite();
        dotImg.type = Image.Type.Simple;
        dotImg.color = GwThemeTokens.StatusPassEmerald;
        dotImg.raycastTarget = false;

        TMP_Text headerTmp = CreateTopLeftTmp(
            "Txt_VerifyHeader",
            cardRect,
            new Vector2(padX + 20f, -20f),
            new Vector2(contentW - 72f, 36f),
            "VERIFIKASI LULUS 4/4",
            22f,
            27f,
            GwThemeTokens.StatusPassText,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);
        _ = headerTmp;

        txtLevelSubtitle = CreateTopLeftTmp(
            "Txt_LevelSubtitle",
            cardRect,
            new Vector2(padX, -56f),
            new Vector2(contentW - 52f, 22f),
            "LVL 1/7  //  Alarm Pintu & Jendela",
            12.5f,
            15f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Normal);

        btnCloseX = CreateFlatActionButton(
            "Btn_LevelCompleteCloseX",
            cardRect,
            ModalWidth - padX - 36f,
            -22f,
            36f,
            36f,
            "X",
            GwThemeTokens.CardIdle,
            GwThemeTokens.HairlineIdle,
            GwThemeTokens.TextPrimary,
            16f,
            20f,
            hasBorder: true,
            HandleDismissClicked,
            out _);

        CreateFlatDivider("Divider_Header", cardRect, padX, -88f, contentW, GwThemeTokens.HairlineBorderPx);

        // ---------------------------------------------------------------------
        // KARTU RUMUS BOOLEAN TERVERIFIKASI (Y = ... dalam TextPrimary bold)
        // ---------------------------------------------------------------------
        GameObject formulaCardObj = new GameObject("Card_VerifiedFormula", typeof(RectTransform), typeof(Image));
        formulaCardObj.transform.SetParent(cardRect, false);
        RectTransform formulaRect = formulaCardObj.GetComponent<RectTransform>();
        formulaRect.anchorMin = new Vector2(0f, 1f);
        formulaRect.anchorMax = new Vector2(0f, 1f);
        formulaRect.pivot = new Vector2(0f, 1f);
        formulaRect.anchoredPosition = new Vector2(padX, -104f);
        formulaRect.sizeDelta = new Vector2(contentW, 86f);

        Image formulaBg = formulaCardObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(formulaBg, GwThemeTokens.CardIdle);
        formulaBg.raycastTarget = false;
        GwThemeTokens.AttachHairlineBorder(formulaRect, "Card_VerifiedFormula_Border", GwThemeTokens.HairlineIdle);

        CreateTopLeftTmp(
            "Txt_FormulaLabel",
            formulaRect,
            new Vector2(18f, -10f),
            new Vector2(contentW - 36f, 20f),
            "RUMUS BOOLEAN TERVERIFIKASI",
            11f,
            12.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        txtVerifiedFormula = CreateTopLeftTmp(
            "Txt_VerifiedFormula",
            formulaRect,
            new Vector2(18f, -34f),
            new Vector2(contentW - 36f, 42f),
            "Y = A AND B",
            20f,
            28f,
            GwThemeTokens.TextPrimary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        // ---------------------------------------------------------------------
        // RINGKASAN TABEL KEBENARAN 4 BARIS (A | B | Y | TGT | OK dalam #34D399)
        // ---------------------------------------------------------------------
        GameObject tableCardObj = new GameObject("Card_TruthTableSummary", typeof(RectTransform), typeof(Image));
        tableCardObj.transform.SetParent(cardRect, false);
        RectTransform tableRect = tableCardObj.GetComponent<RectTransform>();
        tableRect.anchorMin = new Vector2(0f, 1f);
        tableRect.anchorMax = new Vector2(0f, 1f);
        tableRect.pivot = new Vector2(0f, 1f);
        tableRect.anchoredPosition = new Vector2(padX, -204f);
        tableRect.sizeDelta = new Vector2(contentW, 224f);

        Image tableBg = tableCardObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(tableBg, GwThemeTokens.SmokedGlass);
        tableBg.raycastTarget = false;
        GwThemeTokens.AttachHairlineBorder(tableRect, "Card_TruthTableSummary_Border", GwThemeTokens.HairlineIdle);

        // Header 5 kolom sejajar: A | B | Y | TGT | OK
        const float tablePadX = 16f;
        float tableInnerW = contentW - (tablePadX * 2f);
        BuildTableHeaderRow(tableRect, tablePadX, -10f, tableInnerW, 28f);
        CreateFlatDivider("Table_HeaderDivider", tableRect, tablePadX, -42f, tableInnerW, GwThemeTokens.HairlineBorderPx);

        const float rowH = 38f;
        const float rowGap = 6f;
        float rowY = -50f;

        for (int r = 0; r < TruthRowCount; r++)
        {
            summaryRows[r] = BuildTruthSummaryRow(
                $"TruthSummaryRow_{r}",
                tableRect,
                tablePadX,
                rowY,
                tableInnerW,
                rowH,
                r);
            rowY -= (rowH + rowGap);
        }

        // ---------------------------------------------------------------------
        // METRIK KUOTA GERBANG & STATUS VERIFIKASI
        // ---------------------------------------------------------------------
        GameObject metricsObj = new GameObject("Card_QuotaMetrics", typeof(RectTransform), typeof(Image));
        metricsObj.transform.SetParent(cardRect, false);
        RectTransform metricsRect = metricsObj.GetComponent<RectTransform>();
        metricsRect.anchorMin = new Vector2(0f, 1f);
        metricsRect.anchorMax = new Vector2(0f, 1f);
        metricsRect.pivot = new Vector2(0f, 1f);
        metricsRect.anchoredPosition = new Vector2(padX, -442f);
        metricsRect.sizeDelta = new Vector2(contentW, 72f);

        Image metricsBg = metricsObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(metricsBg, GwThemeTokens.CardIdle);
        metricsBg.raycastTarget = false;
        GwThemeTokens.AttachHairlineBorder(metricsRect, "Card_QuotaMetrics_Border", GwThemeTokens.HairlineIdle);

        float halfMetricW = (contentW * 0.5f) - 24f;
        CreateTopLeftTmp(
            "Txt_QuotaTitle",
            metricsRect,
            new Vector2(18f, -10f),
            new Vector2(halfMetricW, 18f),
            "KUOTA GERBANG",
            10.5f,
            12.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        txtQuotaValue = CreateTopLeftTmp(
            "Txt_QuotaValue",
            metricsRect,
            new Vector2(18f, -32f),
            new Vector2(halfMetricW, 30f),
            "1 / 1 GERBANG (EFISIEN)",
            15f,
            19f,
            GwThemeTokens.StatusPassText,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        // Pembatas vertikal tengah
        CreateFlatDivider("Metrics_MidDivider", metricsRect, contentW * 0.5f, -12f, GwThemeTokens.HairlineBorderPx, 48f);

        float rightMetricX = (contentW * 0.5f) + 18f;
        CreateTopLeftTmp(
            "Txt_VerifyStatusTitle",
            metricsRect,
            new Vector2(rightMetricX, -10f),
            new Vector2(halfMetricW, 18f),
            "HASIL PENGUJIAN",
            10.5f,
            12.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        txtVerifySummaryValue = CreateTopLeftTmp(
            "Txt_VerifyStatusValue",
            metricsRect,
            new Vector2(rightMetricX, -32f),
            new Vector2(halfMetricW, 30f),
            "4/4 KOMBINASI COCOK",
            15f,
            19f,
            GwThemeTokens.StatusPassText,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        // ---------------------------------------------------------------------
        // FOOTER: ULANGI LEVEL (OUTLINE SEKUNDER) + LANJUT LEVEL -> (PRIMER PUTIH)
        // ---------------------------------------------------------------------
        CreateFlatDivider("Divider_Footer", cardRect, padX, -530f, contentW, GwThemeTokens.HairlineBorderPx);

        const float footerBtnY = -548f;
        const float footerBtnH = 54f;
        const float btnGap = 16f;
        float replayBtnW = 240f;
        float nextBtnW = contentW - replayBtnW - btnGap;

        btnReplayLevel = CreateFlatActionButton(
            "Btn_ReplayLevel_V85",
            cardRect,
            padX,
            footerBtnY,
            replayBtnW,
            footerBtnH,
            "ULANGI LEVEL",
            GwThemeTokens.CardIdle,
            GwThemeTokens.HairlineActive,
            GwThemeTokens.TextPrimary,
            14.5f,
            18f,
            hasBorder: true,
            HandleReplayLevelClicked,
            out _);

        btnNextLevel = CreateFlatActionButton(
            "Btn_NextLevel_V85",
            cardRect,
            padX + replayBtnW + btnGap,
            footerBtnY,
            nextBtnW,
            footerBtnH,
            "LANJUT LEVEL ->",
            GwThemeTokens.PrimaryFill,
            GwThemeTokens.PrimaryFill,
            GwThemeTokens.PrimaryText,
            16f,
            20f,
            hasBorder: false,
            HandleNextLevelClicked,
            out txtNextLevelLabel);

        modalRootObj.SetActive(false);
    }

    private void BuildTableHeaderRow(RectTransform parent, float leftX, float topY, float width, float height)
    {
        float colW = width / 5f;
        CreateTopLeftTmp("Hdr_A", parent, new Vector2(leftX + (colW * 0f), topY), new Vector2(colW, height), "A", 12f, 14f, GwThemeTokens.TextSecondary, TextAlignmentOptions.Center, FontStyles.Bold);
        CreateTopLeftTmp("Hdr_B", parent, new Vector2(leftX + (colW * 1f), topY), new Vector2(colW, height), "B", 12f, 14f, GwThemeTokens.TextSecondary, TextAlignmentOptions.Center, FontStyles.Bold);
        CreateTopLeftTmp("Hdr_Y", parent, new Vector2(leftX + (colW * 2f), topY), new Vector2(colW, height), "Y", 12f, 14f, GwThemeTokens.TextSecondary, TextAlignmentOptions.Center, FontStyles.Bold);
        CreateTopLeftTmp("Hdr_TGT", parent, new Vector2(leftX + (colW * 3f), topY), new Vector2(colW, height), "TGT", 12f, 14f, GwThemeTokens.TextSecondary, TextAlignmentOptions.Center, FontStyles.Bold);
        CreateTopLeftTmp("Hdr_OK", parent, new Vector2(leftX + (colW * 4f), topY), new Vector2(colW, height), "STATUS", 12f, 14f, GwThemeTokens.TextSecondary, TextAlignmentOptions.Center, FontStyles.Bold);
    }

    private SummaryRowUi BuildTruthSummaryRow(
        string name,
        RectTransform parent,
        float leftX,
        float topY,
        float width,
        float height,
        int rowIndex)
    {
        GameObject rowObj = new GameObject(name, typeof(RectTransform), typeof(Image));
        rowObj.transform.SetParent(parent, false);
        RectTransform rect = rowObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(leftX, topY);
        rect.sizeDelta = new Vector2(width, height);

        Image bg = rowObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(bg, GwThemeTokens.CardIdle);
        bg.raycastTarget = false;

        GwThemeTokens.AttachHairlineBorder(rect, $"{name}_Border", GwThemeTokens.HairlineIdle);

        float colW = width / 5f;
        Color colA = GwThemeTokens.GetSignalColor(RowInputABits[rowIndex]);
        Color colB = GwThemeTokens.GetSignalColor(RowInputBBits[rowIndex]);

        CreateTopLeftTmp("Col_A", rect, new Vector2(colW * 0f, 0f), new Vector2(colW, height), RowInputALabels[rowIndex], 14f, 17f, colA, TextAlignmentOptions.Center, FontStyles.Bold);
        CreateTopLeftTmp("Col_B", rect, new Vector2(colW * 1f, 0f), new Vector2(colW, height), RowInputBLabels[rowIndex], 14f, 17f, colB, TextAlignmentOptions.Center, FontStyles.Bold);

        TMP_Text txtY = CreateTopLeftTmp("Col_Y", rect, new Vector2(colW * 2f, 0f), new Vector2(colW, height), "0", 14f, 17f, GwThemeTokens.StatusPassText, TextAlignmentOptions.Center, FontStyles.Bold);
        TMP_Text txtTgt = CreateTopLeftTmp("Col_TGT", rect, new Vector2(colW * 3f, 0f), new Vector2(colW, height), "0", 14f, 17f, GwThemeTokens.StatusPassText, TextAlignmentOptions.Center, FontStyles.Bold);
        TMP_Text txtStatus = CreateTopLeftTmp("Col_OK", rect, new Vector2(colW * 4f, 0f), new Vector2(colW, height), "OK", 13.5f, 16.5f, GwThemeTokens.StatusPassText, TextAlignmentOptions.Center, FontStyles.Bold);

        // Pembatas kolom tipis 1px HairlineIdle antar sel tabel
        for (int c = 1; c <= 4; c++)
        {
            CreateFlatDivider($"ColDiv_{c}", rect, colW * c, -8f, GwThemeTokens.HairlineBorderPx, height - 16f);
        }

        return new SummaryRowUi
        {
            txtY = txtY,
            txtTgt = txtTgt,
            txtStatus = txtStatus
        };
    }

    private void ApplySafeAreaAndResponsiveScaleIfNeeded(bool force)
    {
        Rect sa = Screen.safeArea;
        int sw = Screen.width;
        int sh = Screen.height;
        bool unchanged = sw == lastScreenW && sh == lastScreenH && sa == lastAppliedSafeArea;
        if (!force && unchanged && hasAppliedScale)
        {
            return;
        }

        if (!unchanged || force)
        {
            lastScreenW = sw;
            lastScreenH = sh;
            lastAppliedSafeArea = sa;
            hasAppliedScale = false;
        }

        if (safeAreaRect == null)
        {
            return;
        }

        float denomW = Mathf.Max(1f, sw);
        float denomH = Mathf.Max(1f, sh);
        safeAreaRect.anchorMin = new Vector2(Mathf.Clamp01(sa.xMin / denomW), Mathf.Clamp01(sa.yMin / denomH));
        safeAreaRect.anchorMax = new Vector2(Mathf.Clamp01(sa.xMax / denomW), Mathf.Clamp01(sa.yMax / denomH));
        safeAreaRect.offsetMin = Vector2.zero;
        safeAreaRect.offsetMax = Vector2.zero;

        if (cardRect != null)
        {
            Rect avail = safeAreaRect.rect;
            if (avail.width > 120f && avail.height > 120f)
            {
                float scaleX = (avail.width - 36f) / ModalWidth;
                float scaleY = (avail.height - 28f) / ModalHeight;
                float fitScale = Mathf.Clamp(Mathf.Min(scaleX, scaleY), 0.65f, 1.0f);
                cardRect.localScale = new Vector3(fitScale, fitScale, 1f);
                hasAppliedScale = true;
            }
        }
    }

    // =========================================================================
    // HELPER UI PROSEDURAL 100% TOKEN GwThemeTokens
    // =========================================================================

    private Button CreateFlatActionButton(
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

        GameObject txtObj = new GameObject($"{name}_Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        txtObj.transform.SetParent(rect, false);
        RectTransform txtRect = txtObj.GetComponent<RectTransform>();
        txtRect.anchorMin = Vector2.zero;
        txtRect.anchorMax = Vector2.one;
        txtRect.offsetMin = new Vector2(12f, 2f);
        txtRect.offsetMax = new Vector2(-12f, -2f);

        TextMeshProUGUI tmp = txtObj.GetComponent<TextMeshProUGUI>();
        if (safeFont != null)
        {
            tmp.font = safeFont;
        }
        tmp.text = label;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = textColor;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = minFont;
        tmp.fontSizeMax = maxFont;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        labelOut = tmp;

        return btn;
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
        FontStyles style)
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
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void CreateFlatDivider(string name, RectTransform parent, float leftX, float topY, float width, float height)
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
        img.color = GwThemeTokens.HairlineIdle;
        img.raycastTarget = false;
    }

    private static void RemoveBtnListener(Button btn, UnityEngine.Events.UnityAction action)
    {
        if (btn != null && action != null)
        {
            btn.onClick.RemoveListener(action);
        }
    }
}
