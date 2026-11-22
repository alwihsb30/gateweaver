using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Layar Masuk &amp; Pilih Mode GateWeaver AR v8.5 (01_Title_&amp;_Mode_Select).
/// - Desain Smoked Optical Glass + Brushed Titanium 100% berbasis GwThemeTokens:
///   * Header metadata tipis di atas: GATEWEAVER AR // v8.5 // SPATIAL COMPUTING LAB.
///   * Panel kaca asap vertikal di kiri (W = 536px, margin kiri 64px, SmokedGlassDense 82%, border 1px HairlineIdle, radius 6px):
///     - Brand GATEWEAVER AR + pil v8.5 (SignalHighTeal teks gelap) + sub LOGIC CIRCUITS AR EDUCATIONAL TOOL.
///     - Kartu Mode 1 (default terpilih, border SignalHighTeal + titik aktif): MODE UDARA - Hand Tracking (AIRWEAVING ENGINE // 60 FPS).
///     - Kartu Mode 2: MODE KARTU - Vuforia AR (MARKER TRACKING // VUFORIA SDK).
///     - Tombol primer solid putih MULAI SIMULASI -&gt; &amp; tombol outline SANDBOX (LABORATORIUM BEBAS).
///     - Footer: FILKOM UNIVERSITAS BRAWIJAYA // 2026.
/// - Tampil bersih satu kali saat startup aplikasi tanpa mengganggu inisialisasi kamera/Vuforia,
///   dan dapat dibuka kembali kapan saja melalui tombol LAYAR UTAMA di Menu Drawer.
/// - Scrim layar penuh raycastTarget=true (sortingOrder = 50) mencegah klik/sentuhan bocor ke HUD atau stage 3D/Vuforia.
/// - 0 perubahan di Core/ &amp; Levels/, 0% regresi Mode Kartu Vuforia v5, 100% Zero-GC di Tick(), pure ASCII 7-bit, tanpa LINQ.
/// </summary>
public sealed class AirWeavingTitleOverlay : MonoBehaviour
{
    public enum TitleModeSelection
    {
        AirMode = 0,
        CardMode = 1
    }

    private const int OverlayCanvasSortingOrder = 50;
    private const float ReferenceCanvasWidth = 1920f;
    private const float ReferenceCanvasHeight = 1080f;
    private const float TopMetaBarHeight = 40f;
    private const float LeftMarginPx = 64f;
    private const float PanelWidth = 536f;
    private const float PanelHeight = 708f;

    private const string StrTargetAirHint = "Target: Mode Udara (Hand Tracking) - Kurikulum 7 Level";
    private const string StrTargetCardHint = "Target: Mode Kartu (Vuforia AR) - Kurikulum 7 Level";
    private const string StrMetaStandby = "AR ENGINE // STANDBY";
    private const string StrMetaAirActive = "AIRWEAVING ENGINE // AKTIF";
    private const string StrMetaCardActive = "VUFORIA ENGINE // AKTIF";

    private sealed class TitleModeCardUi
    {
        public Button button;
        public Image bgImage;
        public Image borderImage;
        public Image leftAccentBar;
        public Image activeDotImage;
        public TMP_Text txtTitle;
        public TMP_Text txtEngineTag;
        public TMP_Text txtDesc;
        public TMP_Text txtSelectBadge;
    }

    public event Action<TitleModeSelection> OnStartSimulationRequested;
    public event Action OnSandboxRequested;

    private CompactHudMenuController compactHudMenu;
    private AirWeavingSpatialSfx spatialSfx;
    private TMP_FontAsset safeFont;

    private GameObject overlayRootObj;
    private Button scrimDismissButton;
    private RectTransform safeAreaRect;
    private RectTransform mainPanelRect;
    private TMP_Text txtTopMetaStatus;
    private GameObject btnCloseXObj;
    private Button btnCloseX;

    private TitleModeCardUi airModeCard;
    private TitleModeCardUi cardModeCard;
    private Button btnStartSimulation;
    private Button btnSandboxMode;
    private TMP_Text txtSelectionSummary;

    private bool isUiBuilt = false;
    private bool isTitleOpen = false;
    private bool isInitialStartupSession = true;
    private bool currentAirWeavingActive = false;
    private TitleModeSelection selectedMode = TitleModeSelection.AirMode;

    private Rect lastAppliedSafeArea;
    private int lastScreenW = -1;
    private int lastScreenH = -1;
    private bool hasAppliedScale = false;

    public bool IsTitleOpen => isTitleOpen;

    public void Configure(
        CompactHudMenuController hudMenu,
        AirWeavingSpatialSfx sfx,
        TMP_FontAsset fallbackFont = null)
    {
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

    public void OpenOverlay(bool isInitialStartup, bool isCurrentlyAirWeaving)
    {
        EnsureUiBuilt();
        if (overlayRootObj == null)
        {
            return;
        }

        isInitialStartupSession = isInitialStartup;
        currentAirWeavingActive = isCurrentlyAirWeaving;
        selectedMode = isInitialStartup
            ? TitleModeSelection.AirMode
            : (isCurrentlyAirWeaving ? TitleModeSelection.AirMode : TitleModeSelection.CardMode);

        if (compactHudMenu != null)
        {
            compactHudMenu.CloseMenuDrawer();
            compactHudMenu.ClearToastImmediate();
        }

        if (spatialSfx != null)
        {
            spatialSfx.StopHumImmediate();
        }

        if (btnCloseXObj != null)
        {
            btnCloseXObj.SetActive(!isInitialStartupSession);
        }

        isTitleOpen = true;
        overlayRootObj.SetActive(true);
        ApplySafeAreaAndResponsiveScaleIfNeeded(force: true);
        RefreshDynamicVisuals();
    }

    public void CloseOverlay()
    {
        isTitleOpen = false;
        if (overlayRootObj != null && overlayRootObj.activeSelf)
        {
            overlayRootObj.SetActive(false);
        }
    }

    public void OnExternalModalOrMenuOpened()
    {
        if (isTitleOpen && !isInitialStartupSession)
        {
            CloseOverlay();
        }
    }

    /// <summary>
    /// Menjaga skala responsif &amp; redaman hum audio selama Layar Masuk terbuka (baik saat startup Mode Kartu maupun Mode Udara). 100% Zero-GC.
    /// </summary>
    public void Tick()
    {
        if (!isTitleOpen)
        {
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
        RemoveBtnListener(scrimDismissButton, HandleScrimClicked);
        RemoveBtnListener(btnCloseX, HandleCloseXClicked);
        if (airModeCard != null) RemoveBtnListener(airModeCard.button, HandleSelectAirModeClicked);
        if (cardModeCard != null) RemoveBtnListener(cardModeCard.button, HandleSelectCardModeClicked);
        RemoveBtnListener(btnStartSimulation, HandleStartSimulationClicked);
        RemoveBtnListener(btnSandboxMode, HandleSandboxClicked);

        isTitleOpen = false;

        if (overlayRootObj != null)
        {
            Destroy(overlayRootObj);
            overlayRootObj = null;
        }

        scrimDismissButton = null;
        safeAreaRect = null;
        mainPanelRect = null;
        txtTopMetaStatus = null;
        btnCloseXObj = null;
        btnCloseX = null;
        airModeCard = null;
        cardModeCard = null;
        btnStartSimulation = null;
        btnSandboxMode = null;
        txtSelectionSummary = null;
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
    // AKSI TOMBOL & PILIHAN MODE
    // =========================================================================

    private void HandleScrimClicked()
    {
        if (!isInitialStartupSession)
        {
            CloseOverlay();
        }
    }

    private void HandleCloseXClicked()
    {
        CloseOverlay();
    }

    private void HandleSelectAirModeClicked()
    {
        if (selectedMode == TitleModeSelection.AirMode)
        {
            return;
        }
        selectedMode = TitleModeSelection.AirMode;
        RefreshDynamicVisuals();
    }

    private void HandleSelectCardModeClicked()
    {
        if (selectedMode == TitleModeSelection.CardMode)
        {
            return;
        }
        selectedMode = TitleModeSelection.CardMode;
        RefreshDynamicVisuals();
    }

    private void HandleStartSimulationClicked()
    {
        TitleModeSelection chosen = selectedMode;
        CloseOverlay();
        OnStartSimulationRequested?.Invoke(chosen);
    }

    private void HandleSandboxClicked()
    {
        CloseOverlay();
        OnSandboxRequested?.Invoke();
    }

    // =========================================================================
    // REFRESH VISUAL DINAMIS (EVENT-DRIVEN, ZERO-GC)
    // =========================================================================

    private void RefreshDynamicVisuals()
    {
        bool airSelected = selectedMode == TitleModeSelection.AirMode;
        ApplyModeCardVisual(airModeCard, isSelected: airSelected);
        ApplyModeCardVisual(cardModeCard, isSelected: !airSelected);

        if (txtSelectionSummary != null)
        {
            txtSelectionSummary.text = airSelected ? StrTargetAirHint : StrTargetCardHint;
        }

        if (txtTopMetaStatus != null)
        {
            if (isInitialStartupSession)
            {
                txtTopMetaStatus.text = StrMetaStandby;
                txtTopMetaStatus.color = GwThemeTokens.TextSecondary;
            }
            else if (currentAirWeavingActive)
            {
                txtTopMetaStatus.text = StrMetaAirActive;
                txtTopMetaStatus.color = GwThemeTokens.SignalHighTeal;
            }
            else
            {
                txtTopMetaStatus.text = StrMetaCardActive;
                txtTopMetaStatus.color = GwThemeTokens.StatusPassText;
            }
        }
    }

    private static void ApplyModeCardVisual(TitleModeCardUi card, bool isSelected)
    {
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
            card.borderImage.color = isSelected ? GwThemeTokens.SignalHighTeal : GwThemeTokens.HairlineIdle;
        }
        if (card.leftAccentBar != null)
        {
            if (card.leftAccentBar.enabled != isSelected)
            {
                card.leftAccentBar.enabled = isSelected;
            }
            if (isSelected)
            {
                card.leftAccentBar.color = GwThemeTokens.SignalHighTeal;
            }
        }
        if (card.activeDotImage != null)
        {
            card.activeDotImage.color = isSelected ? GwThemeTokens.SignalHighTeal : GwThemeTokens.SignalLowSlate;
        }
        if (card.txtTitle != null)
        {
            card.txtTitle.color = isSelected ? GwThemeTokens.TextPrimary : GwThemeTokens.TextSecondary;
        }
        if (card.txtEngineTag != null)
        {
            card.txtEngineTag.color = isSelected ? GwThemeTokens.SignalHighTeal : GwThemeTokens.SignalLowSlate;
        }
        if (card.txtDesc != null)
        {
            card.txtDesc.color = GwThemeTokens.TextSecondary;
        }
        if (card.txtSelectBadge != null)
        {
            card.txtSelectBadge.text = isSelected ? "DIPILIH" : "PILIH";
            card.txtSelectBadge.color = isSelected ? GwThemeTokens.SignalHighTeal : GwThemeTokens.SignalLowSlate;
        }
    }

    // =========================================================================
    // KONSTRUKSI UI PROSEDURAL 100% TOKEN GwThemeTokens (01_Title_&_Mode_Select)
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
        // Dedicated ScreenSpaceOverlay Canvas (sortingOrder = 50) di atas seluruh HUD & modal lain
        overlayRootObj = new GameObject(
            "Canvas_AirTitleOverlay_V85",
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

        // 1. Full-screen TitleBackdropScrim (#0A0C12 94% pekat terpoles, raycast blocker aktif untuk HUD & 3D Vuforia/Stage)
        GameObject scrimObj = new GameObject("Btn_TitleOverlayScrim_V85", typeof(RectTransform), typeof(Image), typeof(Button));
        scrimObj.transform.SetParent(rootRect, false);
        RectTransform scrimRect = scrimObj.GetComponent<RectTransform>();
        scrimRect.anchorMin = Vector2.zero;
        scrimRect.anchorMax = Vector2.one;
        scrimRect.offsetMin = Vector2.zero;
        scrimRect.offsetMax = Vector2.zero;

        Image scrimImg = scrimObj.GetComponent<Image>();
        scrimImg.color = GwThemeTokens.TitleBackdropScrim;
        scrimImg.raycastTarget = true;

        scrimDismissButton = scrimObj.GetComponent<Button>();
        scrimDismissButton.targetGraphic = scrimImg;
        scrimDismissButton.transition = Selectable.Transition.None;
        scrimDismissButton.onClick.AddListener(HandleScrimClicked);

        // 2. Kontainer Safe Area
        GameObject safeObj = new GameObject("TitleOverlay_SafeArea", typeof(RectTransform));
        safeObj.transform.SetParent(rootRect, false);
        safeAreaRect = safeObj.GetComponent<RectTransform>();
        safeAreaRect.anchorMin = Vector2.zero;
        safeAreaRect.anchorMax = Vector2.one;
        safeAreaRect.offsetMin = Vector2.zero;
        safeAreaRect.offsetMax = Vector2.zero;

        // 3. Header Metadata Tipis di Atas: GATEWEAVER AR // v8.5 // SPATIAL COMPUTING LAB
        BuildTopMetadataHeader(safeAreaRect);

        // 4. Panel Kaca Asap Vertikal di Kiri (W = 536px, margin kiri 64px, SmokedGlassDense 82%, border 1px HairlineIdle, radius 6px)
        BuildLeftVerticalPanel(safeAreaRect);

        overlayRootObj.SetActive(false);
    }

    private void BuildTopMetadataHeader(RectTransform parent)
    {
        GameObject barObj = new GameObject("Header_TitleMetadata_V85", typeof(RectTransform), typeof(Image));
        barObj.transform.SetParent(parent, false);
        RectTransform barRect = barObj.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0f, 1f);
        barRect.anchorMax = new Vector2(1f, 1f);
        barRect.pivot = new Vector2(0.5f, 1f);
        barRect.anchoredPosition = Vector2.zero;
        barRect.sizeDelta = new Vector2(0f, TopMetaBarHeight);

        Image barBg = barObj.GetComponent<Image>();
        barBg.color = GwThemeTokens.TitleSurfaceSolid;
        barBg.raycastTarget = false;

        // Garis tepi bawah tipis 1px HairlineIdle
        CreateFlatImage(
            "Header_MetaBottomLine",
            barRect,
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0.5f, 0f),
            Vector2.zero,
            new Vector2(0f, GwThemeTokens.HairlineBorderPx),
            GwThemeTokens.HairlineIdle);

        // Titik indikator kiri sejajar margin kiri 64px
        CreateCircleDot("Header_MetaDot", barRect, LeftMarginPx + 4f, -TopMetaBarHeight * 0.5f, 7f, GwThemeTokens.SignalHighTeal);

        CreateTopLeftTmp(
            "Txt_TitleTopMetadata",
            barRect,
            new Vector2(LeftMarginPx + 18f, 0f),
            new Vector2(760f, TopMetaBarHeight),
            "GATEWEAVER AR // v8.5 // SPATIAL COMPUTING LAB",
            11.5f,
            13.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        // Status engine ringkas kanan atas
        GameObject statusObj = new GameObject("Txt_TitleTopMetaStatus", typeof(RectTransform), typeof(TextMeshProUGUI));
        statusObj.transform.SetParent(barRect, false);
        RectTransform statusRect = statusObj.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(1f, 0f);
        statusRect.anchorMax = new Vector2(1f, 1f);
        statusRect.pivot = new Vector2(1f, 0.5f);
        statusRect.anchoredPosition = new Vector2(-LeftMarginPx, 0f);
        statusRect.sizeDelta = new Vector2(320f, 0f);

        TextMeshProUGUI statusTmp = statusObj.GetComponent<TextMeshProUGUI>();
        if (safeFont != null)
        {
            statusTmp.font = safeFont;
        }
        statusTmp.text = StrMetaStandby;
        statusTmp.fontStyle = FontStyles.Bold;
        statusTmp.alignment = TextAlignmentOptions.MidlineRight;
        statusTmp.color = GwThemeTokens.TextSecondary;
        statusTmp.enableAutoSizing = true;
        statusTmp.fontSizeMin = 10.5f;
        statusTmp.fontSizeMax = 12.5f;
        statusTmp.textWrappingMode = TextWrappingModes.NoWrap;
        statusTmp.raycastTarget = false;
        txtTopMetaStatus = statusTmp;
    }

    private void BuildLeftVerticalPanel(RectTransform parent)
    {
        GameObject panelObj = new GameObject("Panel_TitleVertical_V85", typeof(RectTransform), typeof(Image));
        panelObj.transform.SetParent(parent, false);
        mainPanelRect = panelObj.GetComponent<RectTransform>();
        mainPanelRect.anchorMin = new Vector2(0f, 0.5f);
        mainPanelRect.anchorMax = new Vector2(0f, 0.5f);
        mainPanelRect.pivot = new Vector2(0f, 0.5f);
        mainPanelRect.anchoredPosition = new Vector2(LeftMarginPx, -(TopMetaBarHeight * 0.5f));
        mainPanelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

        Image panelBg = panelObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(panelBg, GwThemeTokens.TitleSurfaceSolid);
        panelBg.raycastTarget = true;
        GwThemeTokens.AttachHairlineBorder(mainPanelRect, "Panel_TitleVertical_Border", GwThemeTokens.HairlineIdle);

        const float padX = 32f;
        float innerW = PanelWidth - (padX * 2f);
        float y = -30f;

        // ---------------------------------------------------------------------
        // BRAND HEADER: GATEWEAVER AR + pil v8.5 + sub LOGIC CIRCUITS AR EDUCATIONAL TOOL
        // ---------------------------------------------------------------------
        CreateTopLeftTmp(
            "Txt_TitleBrand",
            mainPanelRect,
            new Vector2(padX, y),
            new Vector2(308f, 44f),
            "GATEWEAVER AR",
            24f,
            34f,
            GwThemeTokens.TextPrimary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        // Pil versi v8.5 (SignalHighTeal teks gelap PrimaryText)
        GameObject verPillObj = new GameObject("Pill_VersionV85", typeof(RectTransform), typeof(Image));
        verPillObj.transform.SetParent(mainPanelRect, false);
        RectTransform verPillRect = verPillObj.GetComponent<RectTransform>();
        verPillRect.anchorMin = new Vector2(0f, 1f);
        verPillRect.anchorMax = new Vector2(0f, 1f);
        verPillRect.pivot = new Vector2(0f, 1f);
        verPillRect.anchoredPosition = new Vector2(padX + 318f, y - 8f);
        verPillRect.sizeDelta = new Vector2(64f, 28f);

        Image verPillBg = verPillObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(verPillBg, GwThemeTokens.SignalHighTeal);
        verPillBg.raycastTarget = false;

        CreateStretchedTmp(
            "Txt_VersionV85",
            verPillRect,
            "v8.5",
            12.5f,
            15f,
            GwThemeTokens.PrimaryText,
            TextAlignmentOptions.Center,
            FontStyles.Bold);

        // Tombol tutup X tipis (HANYA tampil bila dibuka kembali lewat LAYAR UTAMA dari drawer)
        btnCloseX = CreateRoundedFlatButton(
            "Btn_TitleCloseX",
            mainPanelRect,
            PanelWidth - padX - 34f,
            y - 4f,
            34f,
            34f,
            "X",
            GwThemeTokens.CardIdle,
            GwThemeTokens.HairlineIdle,
            GwThemeTokens.TextPrimary,
            14f,
            17f,
            hasBorder: true,
            HandleCloseXClicked,
            out _);
        btnCloseXObj = btnCloseX.gameObject;
        btnCloseXObj.SetActive(false);

        y -= 48f;
        CreateTopLeftTmp(
            "Txt_TitleSub",
            mainPanelRect,
            new Vector2(padX, y),
            new Vector2(innerW, 22f),
            "LOGIC CIRCUITS AR EDUCATIONAL TOOL",
            11.5f,
            13.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        y -= 34f;
        CreateHorizontalDivider("TitleDiv_Brand", mainPanelRect, padX, y, innerW);
        y -= 18f;

        // ---------------------------------------------------------------------
        // SEKSI PILIH MODE INTERAKSI (2 KARTU MODE)
        // ---------------------------------------------------------------------
        CreateTopLeftTmp(
            "Txt_SelectModeLabel",
            mainPanelRect,
            new Vector2(padX, y),
            new Vector2(innerW, 20f),
            "PILIH MODE SIMULASI",
            10.5f,
            12.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);
        y -= 28f;

        const float cardH = 122f;
        const float cardGap = 14f;

        // Kartu 1 (default terpilih): MODE UDARA - Hand Tracking
        airModeCard = CreateTitleModeCard(
            "Card_TitleModeAir",
            mainPanelRect,
            padX,
            y,
            innerW,
            cardH,
            "MODE UDARA - Hand Tracking",
            "AIRWEAVING ENGINE // 60 FPS",
            "Rakit gerbang logika 3D melayang bebas dengan gestur tangan MediaPipe.",
            HandleSelectAirModeClicked);
        y -= (cardH + cardGap);

        // Kartu 2: MODE KARTU - Vuforia AR
        cardModeCard = CreateTitleModeCard(
            "Card_TitleModeCard",
            mainPanelRect,
            padX,
            y,
            innerW,
            cardH,
            "MODE KARTU - Vuforia AR",
            "MARKER TRACKING // VUFORIA SDK",
            "Pindai kartu marker fisik di atas meja nyata untuk simulasi gerbang.",
            HandleSelectCardModeClicked);
        y -= (cardH + 20f);

        CreateHorizontalDivider("TitleDiv_Actions", mainPanelRect, padX, y, innerW);
        y -= 20f;

        // ---------------------------------------------------------------------
        // TOMBOL AKSI PRIMER (SOLID PUTIH) + SEKUNDER OUTLINE (SANDBOX)
        // ---------------------------------------------------------------------
        btnStartSimulation = CreateRoundedFlatButton(
            "Btn_TitleStartSim",
            mainPanelRect,
            padX,
            y,
            innerW,
            56f,
            "MULAI SIMULASI ->",
            GwThemeTokens.PrimaryFill,
            GwThemeTokens.PrimaryFill,
            GwThemeTokens.PrimaryText,
            16f,
            20f,
            hasBorder: false,
            HandleStartSimulationClicked,
            out _);
        y -= 70f;

        btnSandboxMode = CreateRoundedFlatButton(
            "Btn_TitleSandbox",
            mainPanelRect,
            padX,
            y,
            innerW,
            48f,
            "SANDBOX (LABORATORIUM BEBAS)",
            GwThemeTokens.CardIdle,
            GwThemeTokens.HairlineActive,
            GwThemeTokens.TextPrimary,
            13.5f,
            16f,
            hasBorder: true,
            HandleSandboxClicked,
            out _);
        y -= 58f;

        txtSelectionSummary = CreateTopLeftTmp(
            "Txt_TitleSelectionSummary",
            mainPanelRect,
            new Vector2(padX, y),
            new Vector2(innerW, 20f),
            StrTargetAirHint,
            10.5f,
            12.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Normal);

        // ---------------------------------------------------------------------
        // FOOTER KECIL: FILKOM UNIVERSITAS BRAWIJAYA // 2026
        // ---------------------------------------------------------------------
        float footerDividerY = -(PanelHeight - 54f);
        CreateHorizontalDivider("TitleDiv_Footer", mainPanelRect, padX, footerDividerY, innerW);

        CreateTopLeftTmp(
            "Txt_TitleFooterLeft",
            mainPanelRect,
            new Vector2(padX, -(PanelHeight - 42f)),
            new Vector2(innerW * 0.68f, 24f),
            "FILKOM UNIVERSITAS BRAWIJAYA // 2026",
            10.5f,
            12.5f,
            GwThemeTokens.SignalLowSlate,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        CreateTopLeftTmp(
            "Txt_TitleFooterRight",
            mainPanelRect,
            new Vector2(padX + (innerW * 0.68f), -(PanelHeight - 42f)),
            new Vector2(innerW * 0.32f, 24f),
            "FASE E // AR LAB",
            10f,
            12f,
            GwThemeTokens.SignalLowSlate,
            TextAlignmentOptions.MidlineRight,
            FontStyles.Normal);
    }

    private TitleModeCardUi CreateTitleModeCard(
        string name,
        RectTransform parent,
        float leftX,
        float topY,
        float width,
        float height,
        string title,
        string engineTag,
        string description,
        UnityEngine.Events.UnityAction onClick)
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

        // Strip aksen kiri tipis 3px untuk kartu yang terpilih
        Image leftAccent = CreateFlatImage(
            "LeftAccentBar",
            rect,
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(2f, 0f),
            new Vector2(3f, height - 20f),
            GwThemeTokens.SignalHighTeal);
        leftAccent.enabled = false;

        Button btn = obj.GetComponent<Button>();
        btn.targetGraphic = bg;
        btn.transition = Selectable.Transition.ColorTint;
        btn.colors = GwThemeTokens.FlatGlassButtonColors;
        if (onClick != null)
        {
            btn.onClick.AddListener(onClick);
        }

        // Titik aktif di kiri atas judul
        Image activeDot = CreateCircleDot("ActiveDot", rect, 22f, -28f, GwThemeTokens.StatusDotSizePx, GwThemeTokens.SignalLowSlate);

        TMP_Text titleTmp = CreateTopLeftTmp(
            "Txt_ModeTitle",
            rect,
            new Vector2(36f, -15f),
            new Vector2(width - 114f, 26f),
            title,
            15f,
            18.5f,
            GwThemeTokens.TextPrimary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        TMP_Text badgeTmp = CreateTopLeftTmp(
            "Txt_ModeBadge",
            rect,
            new Vector2(width - 76f, -15f),
            new Vector2(60f, 26f),
            "PILIH",
            10f,
            12f,
            GwThemeTokens.SignalLowSlate,
            TextAlignmentOptions.MidlineRight,
            FontStyles.Bold);

        TMP_Text engineTmp = CreateTopLeftTmp(
            "Txt_ModeEngine",
            rect,
            new Vector2(36f, -45f),
            new Vector2(width - 52f, 20f),
            engineTag,
            11f,
            13f,
            GwThemeTokens.SignalLowSlate,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        TMP_Text descTmp = CreateTopLeftTmp(
            "Txt_ModeDesc",
            rect,
            new Vector2(36f, -70f),
            new Vector2(width - 52f, 40f),
            description,
            11.5f,
            13.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.TopLeft,
            FontStyles.Normal,
            allowWrap: true);

        return new TitleModeCardUi
        {
            button = btn,
            bgImage = bg,
            borderImage = border,
            leftAccentBar = leftAccent,
            activeDotImage = activeDot,
            txtTitle = titleTmp,
            txtEngineTag = engineTmp,
            txtDesc = descTmp,
            txtSelectBadge = badgeTmp
        };
    }

    // =========================================================================
    // HELPER VISUAL & SAFE AREA RESPONSIF (100% TOKEN GwThemeTokens)
    // =========================================================================

    private void ApplySafeAreaAndResponsiveScaleIfNeeded(bool force)
    {
        if (safeAreaRect == null || mainPanelRect == null)
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

        float availH = Mathf.Max(320f, safeH - TopMetaBarHeight - 44f);
        float availW = Mathf.Max(360f, safeW - (LeftMarginPx * 2f));
        float fitScale = Mathf.Clamp(Mathf.Min(availH / PanelHeight, availW / PanelWidth), 0.58f, 1.0f);

        mainPanelRect.localScale = new Vector3(fitScale, fitScale, 1f);
        mainPanelRect.anchoredPosition = new Vector2(LeftMarginPx, -(TopMetaBarHeight * 0.5f));
    }

    private static void CreateHorizontalDivider(string name, RectTransform parent, float leftX, float topY, float width)
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

    private static void RemoveBtnListener(Button btn, UnityEngine.Events.UnityAction action)
    {
        if (btn != null && action != null)
        {
            btn.onClick.RemoveListener(action);
        }
    }
}
