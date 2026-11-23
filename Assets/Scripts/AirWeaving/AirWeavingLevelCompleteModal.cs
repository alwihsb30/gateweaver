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
    private const float ModalWidth = 760f;
    private const float ModalHeight = 588f;
    private const float ShowDelayAfterVerifySeconds = 0.35f;

    private static readonly string[] CachedLevelTitles =
    {
        "ALARM PINTU & JENDELA",
        "SAKELAR TANGGA DUA ARAH",
        "KUNCI KEAMANAN BRANKAS",
        "PEMUTUS DARURAT REAKTOR",
        "SINKRONISASI INTI KEMBAR",
        "SENSOR HENING SERVER (NOR)",
        "KUNCI BIOMETRIK DE MORGAN"
    };

    private static readonly string[] CachedLevelNumberStrings =
    {
        "01", "02", "03", "04", "05", "06", "07"
    };

    private static readonly string[] RowInputALabels = { "0", "0", "1", "1" };
    private static readonly string[] RowInputBLabels = { "0", "1", "0", "1" };

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

        // 1. Judul utama level selesai (LEVEL 07 SELESAI - KUNCI BIOMETRIK DE MORGAN, 1:1 Figma)
        if (txtLevelSubtitle != null)
        {
            sb.Clear();
            string numStr = lvlIdx < CachedLevelNumberStrings.Length
                ? CachedLevelNumberStrings[lvlIdx]
                : (lvlIdx + 1).ToString();
            sb.Append("LEVEL ").Append(numStr).Append(" SELESAI - ");
            if (lvlIdx < CachedLevelTitles.Length)
            {
                sb.Append(CachedLevelTitles[lvlIdx]);
            }
            else if (scenario != null && !string.IsNullOrEmpty(scenario.levelTitle))
            {
                sb.Append(scenario.levelTitle.ToUpperInvariant());
            }
            else
            {
                sb.Append("MISI KURIKULUM");
            }
            txtLevelSubtitle.SetText(sb);
        }

        // 2. Ekspresi Boolean terverifikasi (Y = ...)
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

        // 3. Ringkasan tabel kebenaran 4 baris (1 = SignalHighTeal #38BDF8, 0 = TextPrimary, STATUS = OK #34D399)
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
            Color bitCol = expectedBit ? GwThemeTokens.SignalHighTeal : GwThemeTokens.TextPrimary;

            if (rowUi.txtY != null)
            {
                rowUi.txtY.text = bitStr;
                rowUi.txtY.color = bitCol;
            }
            if (rowUi.txtTgt != null)
            {
                rowUi.txtTgt.text = bitStr;
                rowUi.txtTgt.color = bitCol;
            }
            if (rowUi.txtStatus != null)
            {
                rowUi.txtStatus.text = "OK";
                rowUi.txtStatus.color = GwThemeTokens.StatusPassText;
            }
        }

        // 4. Metrik Kuota Gerbang ringkas ("X/Y") & Status Lab ("TERBUKA") 1:1 Figma
        int usedGates = circuitManager != null ? circuitManager.activeGateCardsCount : 1;
        int maxGates = scenario != null ? Mathf.Max(1, scenario.maxGateCardsQuota) : 1;
        if (txtQuotaValue != null)
        {
            sb.Clear();
            sb.Append(usedGates).Append('/').Append(maxGates);
            txtQuotaValue.SetText(sb);
            txtQuotaValue.color = GwThemeTokens.TextPrimary;
        }

        bool isLastLevel = lvlIdx >= (levelCount - 1);
        if (txtVerifySummaryValue != null)
        {
            txtVerifySummaryValue.text = "TERBUKA";
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
        // HEADER: Eyebrow hijau VERIFIKASI LULUS 4/4 + Judul putih LEVEL 0X SELESAI + Badge 4/4 kanan atas (1:1 Figma)
        // ---------------------------------------------------------------------
        GameObject statusDotObj = new GameObject("Header_PassDot", typeof(RectTransform), typeof(Image));
        statusDotObj.transform.SetParent(cardRect, false);
        RectTransform dotRect = statusDotObj.GetComponent<RectTransform>();
        dotRect.anchorMin = new Vector2(0f, 1f);
        dotRect.anchorMax = new Vector2(0f, 1f);
        dotRect.pivot = new Vector2(0.5f, 0.5f);
        dotRect.anchoredPosition = new Vector2(padX + 4f, -34f);
        dotRect.sizeDelta = new Vector2(8f, 8f);

        Image dotImg = statusDotObj.GetComponent<Image>();
        dotImg.sprite = GwThemeTokens.GetCircleSprite();
        dotImg.type = Image.Type.Simple;
        dotImg.color = GwThemeTokens.StatusPassEmerald;
        dotImg.raycastTarget = false;

        CreateTopLeftTmp(
            "Txt_VerifyHeader",
            cardRect,
            new Vector2(padX + 16f, -24f),
            new Vector2(contentW - 104f, 20f),
            "VERIFIKASI LULUS 4/4",
            11.5f,
            13f,
            GwThemeTokens.StatusPassText,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        txtLevelSubtitle = CreateTopLeftTmp(
            "Txt_LevelSubtitle",
            cardRect,
            new Vector2(padX, -48f),
            new Vector2(contentW - 84f, 30f),
            "LEVEL 01 SELESAI - ALARM PINTU & JENDELA",
            17f,
            21f,
            GwThemeTokens.TextPrimary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        // Lencana hijau kanan atas: centang geometris + 4/4 (tanpa tombol X & tanpa Divider_Header, 1:1 Figma)
        GameObject passBadgeObj = new GameObject("Badge_PassFourOfFour", typeof(RectTransform), typeof(Image));
        passBadgeObj.transform.SetParent(cardRect, false);
        RectTransform passBadgeRect = passBadgeObj.GetComponent<RectTransform>();
        passBadgeRect.anchorMin = new Vector2(0f, 1f);
        passBadgeRect.anchorMax = new Vector2(0f, 1f);
        passBadgeRect.pivot = new Vector2(0f, 1f);
        passBadgeRect.anchoredPosition = new Vector2(ModalWidth - padX - 68f, -32f);
        passBadgeRect.sizeDelta = new Vector2(68f, 30f);

        Image passBadgeBg = passBadgeObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(passBadgeBg, GwThemeTokens.SmokedGlass);
        passBadgeBg.raycastTarget = false;
        GwThemeTokens.AttachHairlineBorder(passBadgeRect, "Badge_PassFourOfFour_Border", GwThemeTokens.StatusPassEmerald);

        CreateGeometricCheckmark("Badge_Check", passBadgeRect, 17f, -15f, GwThemeTokens.StatusPassText);

        CreateTopLeftTmp(
            "Txt_BadgeFourOfFour",
            passBadgeRect,
            new Vector2(28f, 0f),
            new Vector2(34f, 30f),
            "4/4",
            11.5f,
            13f,
            GwThemeTokens.StatusPassText,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        // ---------------------------------------------------------------------
        // KARTU EKSPRESI BOOLEAN TERVERIFIKASI (Y = ... dalam TextPrimary bold)
        // ---------------------------------------------------------------------
        GameObject formulaCardObj = new GameObject("Card_VerifiedFormula", typeof(RectTransform), typeof(Image));
        formulaCardObj.transform.SetParent(cardRect, false);
        RectTransform formulaRect = formulaCardObj.GetComponent<RectTransform>();
        formulaRect.anchorMin = new Vector2(0f, 1f);
        formulaRect.anchorMax = new Vector2(0f, 1f);
        formulaRect.pivot = new Vector2(0f, 1f);
        formulaRect.anchoredPosition = new Vector2(padX, -96f);
        formulaRect.sizeDelta = new Vector2(contentW, 82f);

        Image formulaBg = formulaCardObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(formulaBg, GwThemeTokens.CardIdle);
        formulaBg.raycastTarget = false;
        GwThemeTokens.AttachHairlineBorder(formulaRect, "Card_VerifiedFormula_Border", GwThemeTokens.HairlineIdle);

        CreateTopLeftTmp(
            "Txt_FormulaLabel",
            formulaRect,
            new Vector2(20f, -12f),
            new Vector2(contentW - 40f, 18f),
            "EKSPRESI BOOLEAN TERVERIFIKASI",
            10f,
            11.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        txtVerifiedFormula = CreateTopLeftTmp(
            "Txt_VerifiedFormula",
            formulaRect,
            new Vector2(20f, -34f),
            new Vector2(contentW - 40f, 38f),
            "Y = A AND B",
            20f,
            26f,
            GwThemeTokens.TextPrimary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        // ---------------------------------------------------------------------
        // TABEL KEBENARAN 1 KONTAINER UTUH (GARIS GRID 1PX TANPA KOTAK PER BARIS, 1:1 FIGMA)
        // ---------------------------------------------------------------------
        const float tableTopY = -194f;
        const float rowH = 40f;
        const float tableTotalH = rowH * 5f; // 1 header + 4 baris = 200px

        GameObject tableCardObj = new GameObject("Card_TruthTableSummary", typeof(RectTransform), typeof(Image));
        tableCardObj.transform.SetParent(cardRect, false);
        RectTransform tableRect = tableCardObj.GetComponent<RectTransform>();
        tableRect.anchorMin = new Vector2(0f, 1f);
        tableRect.anchorMax = new Vector2(0f, 1f);
        tableRect.pivot = new Vector2(0f, 1f);
        tableRect.anchoredPosition = new Vector2(padX, tableTopY);
        tableRect.sizeDelta = new Vector2(contentW, tableTotalH);

        Image tableBg = tableCardObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(tableBg, GwThemeTokens.CardIdle);
        tableBg.raycastTarget = false;
        GwThemeTokens.AttachHairlineBorder(tableRect, "Card_TruthTableSummary_Border", GwThemeTokens.HairlineIdle);

        BuildTableHeaderRow(tableRect, 0f, 0f, contentW, rowH);

        for (int r = 0; r < TruthRowCount; r++)
        {
            float lineY = -rowH * (r + 1);
            CreateFlatDivider($"Table_RowDiv_{r}", tableRect, 1f, lineY, contentW - 2f, GwThemeTokens.HairlineBorderPx);

            summaryRows[r] = BuildTruthSummaryRow(
                $"TruthSummaryRow_{r}",
                tableRect,
                0f,
                lineY,
                contentW,
                rowH,
                r);
        }

        // 4 garis grid vertikal 1px penuh dari atas ke bawah tabel (A:17% | B:17% | Y:17% | TGT:17% | STATUS:32%)
        float colStep = contentW * 0.17f;
        for (int c = 1; c <= 4; c++)
        {
            CreateFlatDivider($"Table_ColDiv_{c}", tableRect, colStep * c, -1f, GwThemeTokens.HairlineBorderPx, tableTotalH - 2f);
        }

        // ---------------------------------------------------------------------
        // BARIS METRIK: 2 PIL RAMPING 1-BARIS BERDAMPINGAN (Kuota Gerbang X/Y | Status Lab TERBUKA)
        // ---------------------------------------------------------------------
        const float metricY = -410f;
        const float metricH = 44f;
        const float metricGap = 14f;
        float leftPillW = (contentW - metricGap) * 0.48f;
        float rightPillW = contentW - metricGap - leftPillW;

        GameObject quotaPillObj = new GameObject("Pill_QuotaMetric", typeof(RectTransform), typeof(Image));
        quotaPillObj.transform.SetParent(cardRect, false);
        RectTransform quotaPillRect = quotaPillObj.GetComponent<RectTransform>();
        quotaPillRect.anchorMin = new Vector2(0f, 1f);
        quotaPillRect.anchorMax = new Vector2(0f, 1f);
        quotaPillRect.pivot = new Vector2(0f, 1f);
        quotaPillRect.anchoredPosition = new Vector2(padX, metricY);
        quotaPillRect.sizeDelta = new Vector2(leftPillW, metricH);

        Image quotaPillBg = quotaPillObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(quotaPillBg, GwThemeTokens.CardIdle);
        quotaPillBg.raycastTarget = false;
        GwThemeTokens.AttachHairlineBorder(quotaPillRect, "Pill_QuotaMetric_Border", GwThemeTokens.HairlineIdle);

        CreateTopLeftTmp(
            "Txt_QuotaTitle",
            quotaPillRect,
            new Vector2(16f, 0f),
            new Vector2(leftPillW * 0.62f, metricH),
            "Kuota Gerbang",
            12f,
            14f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Normal);

        txtQuotaValue = CreateTopLeftTmp(
            "Txt_QuotaValue",
            quotaPillRect,
            new Vector2(leftPillW * 0.55f, 0f),
            new Vector2((leftPillW * 0.45f) - 16f, metricH),
            "1/1",
            14f,
            16.5f,
            GwThemeTokens.TextPrimary,
            TextAlignmentOptions.MidlineRight,
            FontStyles.Bold);

        GameObject labPillObj = new GameObject("Pill_LabStatusMetric", typeof(RectTransform), typeof(Image));
        labPillObj.transform.SetParent(cardRect, false);
        RectTransform labPillRect = labPillObj.GetComponent<RectTransform>();
        labPillRect.anchorMin = new Vector2(0f, 1f);
        labPillRect.anchorMax = new Vector2(0f, 1f);
        labPillRect.pivot = new Vector2(0f, 1f);
        labPillRect.anchoredPosition = new Vector2(padX + leftPillW + metricGap, metricY);
        labPillRect.sizeDelta = new Vector2(rightPillW, metricH);

        Image labPillBg = labPillObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(labPillBg, GwThemeTokens.SmokedGlass);
        labPillBg.raycastTarget = false;
        GwThemeTokens.AttachHairlineBorder(labPillRect, "Pill_LabStatusMetric_Border", GwThemeTokens.StatusPassEmerald);

        CreateTopLeftTmp(
            "Txt_VerifyStatusTitle",
            labPillRect,
            new Vector2(16f, 0f),
            new Vector2(rightPillW * 0.52f, metricH),
            "Status Lab",
            12f,
            14f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Normal);

        GameObject labDotObj = new GameObject("LabStatus_Dot", typeof(RectTransform), typeof(Image));
        labDotObj.transform.SetParent(labPillRect, false);
        RectTransform labDotRect = labDotObj.GetComponent<RectTransform>();
        labDotRect.anchorMin = new Vector2(1f, 0.5f);
        labDotRect.anchorMax = new Vector2(1f, 0.5f);
        labDotRect.pivot = new Vector2(0.5f, 0.5f);
        labDotRect.anchoredPosition = new Vector2(-92f, 0f);
        labDotRect.sizeDelta = new Vector2(6f, 6f);

        Image labDotImg = labDotObj.GetComponent<Image>();
        labDotImg.sprite = GwThemeTokens.GetCircleSprite();
        labDotImg.type = Image.Type.Simple;
        labDotImg.color = GwThemeTokens.StatusPassEmerald;
        labDotImg.raycastTarget = false;

        txtVerifySummaryValue = CreateTopLeftTmp(
            "Txt_VerifyStatusValue",
            labPillRect,
            new Vector2(rightPillW - 96f, 0f),
            new Vector2(80f, metricH),
            "TERBUKA",
            12.5f,
            14.5f,
            GwThemeTokens.StatusPassText,
            TextAlignmentOptions.MidlineRight,
            FontStyles.Bold);

        // ---------------------------------------------------------------------
        // FOOTER: LANJUT LEVEL -> (PRIMER PUTIH LEBAR DI KIRI) + ULANGI LEVEL (OUTLINE DI KANAN) 1:1 FIGMA
        // ---------------------------------------------------------------------
        const float footerBtnY = -504f;
        const float footerBtnH = 52f;
        const float btnGap = 14f;
        float replayBtnW = 220f;
        float nextBtnW = contentW - replayBtnW - btnGap;

        btnNextLevel = CreateFlatActionButton(
            "Btn_NextLevel_V85",
            cardRect,
            padX,
            footerBtnY,
            nextBtnW,
            footerBtnH,
            "LANJUT LEVEL ->",
            GwThemeTokens.PrimaryFill,
            GwThemeTokens.PrimaryFill,
            GwThemeTokens.PrimaryText,
            15f,
            18f,
            hasBorder: false,
            HandleNextLevelClicked,
            out txtNextLevelLabel);

        btnReplayLevel = CreateFlatActionButton(
            "Btn_ReplayLevel_V85",
            cardRect,
            padX + nextBtnW + btnGap,
            footerBtnY,
            replayBtnW,
            footerBtnH,
            "ULANGI LEVEL",
            GwThemeTokens.CardIdle,
            GwThemeTokens.HairlineActive,
            GwThemeTokens.TextPrimary,
            13.5f,
            16f,
            hasBorder: true,
            HandleReplayLevelClicked,
            out _);

        modalRootObj.SetActive(false);
    }

    private void BuildTableHeaderRow(RectTransform parent, float leftX, float topY, float width, float height)
    {
        float colW = width * 0.17f;
        float statusW = width - (colW * 4f);
        CreateTopLeftTmp("Hdr_A", parent, new Vector2(leftX + (colW * 0f), topY), new Vector2(colW, height), "A", 11f, 12.5f, GwThemeTokens.TextSecondary, TextAlignmentOptions.Center, FontStyles.Bold);
        CreateTopLeftTmp("Hdr_B", parent, new Vector2(leftX + (colW * 1f), topY), new Vector2(colW, height), "B", 11f, 12.5f, GwThemeTokens.TextSecondary, TextAlignmentOptions.Center, FontStyles.Bold);
        CreateTopLeftTmp("Hdr_Y", parent, new Vector2(leftX + (colW * 2f), topY), new Vector2(colW, height), "Y", 11f, 12.5f, GwThemeTokens.TextSecondary, TextAlignmentOptions.Center, FontStyles.Bold);
        CreateTopLeftTmp("Hdr_TGT", parent, new Vector2(leftX + (colW * 3f), topY), new Vector2(colW, height), "TGT", 11f, 12.5f, GwThemeTokens.TextSecondary, TextAlignmentOptions.Center, FontStyles.Bold);
        CreateTopLeftTmp("Hdr_OK", parent, new Vector2(leftX + (colW * 4f), topY), new Vector2(statusW, height), "STATUS", 11f, 12.5f, GwThemeTokens.TextSecondary, TextAlignmentOptions.Center, FontStyles.Bold);
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
        GameObject rowObj = new GameObject(name, typeof(RectTransform));
        rowObj.transform.SetParent(parent, false);
        RectTransform rect = rowObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(leftX, topY);
        rect.sizeDelta = new Vector2(width, height);

        float colW = width * 0.17f;
        float statusX = colW * 4f;
        float statusW = width - statusX;

        CreateTopLeftTmp("Col_A", rect, new Vector2(colW * 0f, 0f), new Vector2(colW, height), RowInputALabels[rowIndex], 13.5f, 16f, GwThemeTokens.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
        CreateTopLeftTmp("Col_B", rect, new Vector2(colW * 1f, 0f), new Vector2(colW, height), RowInputBLabels[rowIndex], 13.5f, 16f, GwThemeTokens.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);

        TMP_Text txtY = CreateTopLeftTmp("Col_Y", rect, new Vector2(colW * 2f, 0f), new Vector2(colW, height), "0", 13.5f, 16f, GwThemeTokens.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
        TMP_Text txtTgt = CreateTopLeftTmp("Col_TGT", rect, new Vector2(colW * 3f, 0f), new Vector2(colW, height), "0", 13.5f, 16f, GwThemeTokens.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);

        float checkCenterX = statusX + (statusW * 0.5f) - 14f;
        CreateGeometricCheckmark("Icon_RowCheck", rect, checkCenterX, -height * 0.5f, GwThemeTokens.StatusPassText);

        TMP_Text txtStatus = CreateTopLeftTmp(
            "Col_OK",
            rect,
            new Vector2(statusX + (statusW * 0.5f) - 2f, 0f),
            new Vector2(44f, height),
            "OK",
            12.5f,
            15f,
            GwThemeTokens.StatusPassText,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        return new SummaryRowUi
        {
            txtY = txtY,
            txtTgt = txtTgt,
            txtStatus = txtStatus
        };
    }

    private static void CreateGeometricCheckmark(string name, RectTransform parent, float centerX, float centerY, Color color)
    {
        GameObject root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.anchoredPosition = new Vector2(centerX, centerY);
        rootRect.sizeDelta = new Vector2(12f, 12f);

        GameObject shortObj = new GameObject("Check_Short", typeof(RectTransform), typeof(Image));
        shortObj.transform.SetParent(rootRect, false);
        RectTransform shortRect = shortObj.GetComponent<RectTransform>();
        shortRect.anchorMin = new Vector2(0.5f, 0.5f);
        shortRect.anchorMax = new Vector2(0.5f, 0.5f);
        shortRect.pivot = new Vector2(0.5f, 0.5f);
        shortRect.anchoredPosition = new Vector2(-2.4f, -1.0f);
        shortRect.sizeDelta = new Vector2(4.4f, 1.6f);
        shortRect.localRotation = Quaternion.Euler(0f, 0f, -45f);
        Image shortImg = shortObj.GetComponent<Image>();
        shortImg.color = color;
        shortImg.raycastTarget = false;

        GameObject longObj = new GameObject("Check_Long", typeof(RectTransform), typeof(Image));
        longObj.transform.SetParent(rootRect, false);
        RectTransform longRect = longObj.GetComponent<RectTransform>();
        longRect.anchorMin = new Vector2(0.5f, 0.5f);
        longRect.anchorMax = new Vector2(0.5f, 0.5f);
        longRect.pivot = new Vector2(0.5f, 0.5f);
        longRect.anchoredPosition = new Vector2(1.2f, 0.3f);
        longRect.sizeDelta = new Vector2(7.8f, 1.6f);
        longRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Image longImg = longObj.GetComponent<Image>();
        longImg.color = color;
        longImg.raycastTarget = false;
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
