using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Panel Samping Navigasi &amp; Pengaturan Mode Udara GateWeaver AR v8.5 (04_Menu_Drawer_&amp;_Settings).
/// - Desain Smoked Optical Glass + Brushed Titanium 100% berbasis GwThemeTokens:
///   * Latar SmokedGlassDense (82%) + garis tepi kiri 1px HairlineIdle (tanpa border kanan).
///   * Header MENU NAVIGASI + sub GATEWEAVER AR // v8.5 + tombol tutup X.
///   * Seksi Mode Simulasi (MODE UDARA / MODE KARTU / SANDBOX) dengan sorotan border 1px SignalHighTeal (#38BDF8) + indikator aktif.
///   * Daftar Level 01-07 dengan penanda selesai StatusPassText (#34D399) + sorotan level aktif SignalHighTeal.
///   * Kontrol &amp; Kalibrasi (Kalibrasi/Pusatkan Panggung outline HairlineIdle, Audio, Panduan Gestur, &amp; Reset Rangkaian outline StatusErrorRed #EF4444 + teks StatusErrorText #FCA5A5).
/// - 0 perubahan di Core/ &amp; Levels/, 0% regresi Mode Kartu Vuforia v5 (drawer lama tetap utuh di Mode Kartu).
/// - 100% Zero-GC di TickLate(), pure ASCII 7-bit, tanpa LINQ, tanpa neon glow.
/// </summary>
public sealed class AirWeavingMenuDrawerOverlay : MonoBehaviour
{
    private const int CurriculumLevelCount = 7;
    private const int OverlayCanvasSortingOrder = 30;
    private const float ReferenceCanvasWidth = 1920f;
    private const float ReferenceCanvasHeight = 1080f;
    private const float ReferenceDrawerWidth = 420f;
    private const float ReferenceContentHeight = 882f;
    private const float ContentPadX = 20f;
    private const float ContentPadTop = 20f;
    private const float ContentPadBottom = 20f;

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

    private static readonly string[] CachedLevelNumberLabels =
    {
        "01", "02", "03", "04", "05", "06", "07"
    };

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

    private sealed class ModeCardUi
    {
        public Button button;
        public Image bgImage;
        public Image borderImage;
        public Image activeDotImage;
        public TMP_Text titleText;
        public TMP_Text stateText;
    }

    private sealed class LevelRowUi
    {
        public Button button;
        public UnityEngine.Events.UnityAction clickHandler;
        public Image bgImage;
        public Image borderImage;
        public Image activeBarImage;
        public TMP_Text numberText;
        public TMP_Text titleText;
        public GameObject checkIconRoot;
        public TMP_Text statusText;
    }

    public event Action OnRecenterStageRequested;
    public event Action OnGestureGuideRequested;
    public event Action OnResetCircuitRequested;
    public event Action OnLevelSelectorRequested;
    public event Action OnTitleScreenRequested;

    private LevelAndUIManager levelManager;
    private CompactHudMenuController compactHudMenu;
    private Canvas targetCanvas;
    private TMP_FontAsset safeFont;

    private GameObject legacyMenuDrawerObj;
    private GameObject legacyMenuScrimObj;

    private GameObject drawerRootObj;
    private Button scrimCloseButton;
    private RectTransform drawerPanelRect;
    private RectTransform drawerContentRect;
    private Button btnCloseX;

    private ModeCardUi modeAirCard;
    private ModeCardUi modeCardCard;
    private ModeCardUi modeSandboxCard;

    private readonly LevelRowUi[] levelRows = new LevelRowUi[CurriculumLevelCount];
    private Button btnOpenLevelMap;

    private Button btnCalibrateStage;
    private Button btnAudioToggle;
    private TMP_Text txtAudioLabel;
    private Button btnOpenGuide;
    private Button btnTitleScreen;
    private Button btnResetCircuit;

    private bool isUiBuilt = false;
    private bool isAirWeavingActive = false;
    private bool isDrawerVisible = false;
    private int lastScreenW = -1;
    private int lastScreenH = -1;
    private Rect lastSafeArea;

    public bool IsDrawerVisible => isDrawerVisible;

    public void Configure(
        LevelAndUIManager manager,
        CompactHudMenuController hudMenu,
        TMP_FontAsset fallbackFont = null)
    {
        levelManager = manager;
        compactHudMenu = hudMenu;

        if (compactHudMenu != null)
        {
            if (compactHudMenu.TargetCanvas != null)
            {
                targetCanvas = compactHudMenu.TargetCanvas;
            }
            if (compactHudMenu.SafeFont != null)
            {
                safeFont = compactHudMenu.SafeFont;
            }
        }

        if (targetCanvas == null)
        {
            targetCanvas = FindTargetCanvas();
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
        HideDrawerVisualOnly();
    }

    public void OnAirWeavingModeExited()
    {
        isAirWeavingActive = false;
        HideDrawerVisualOnly();
    }

    /// <summary>
    /// Menyinkronkan Drawer v8.5 dengan status IsMenuDrawerOpen dari CompactHudMenuController
    /// tanpa mengubah skrip Levels/. Dipanggil dari AirWeavingController.LateUpdate(). 100% Zero-GC.
    /// </summary>
    public void TickLate(bool airWeavingActive)
    {
        isAirWeavingActive = airWeavingActive;

        if (!isAirWeavingActive || compactHudMenu == null)
        {
            if (isDrawerVisible)
            {
                HideDrawerVisualOnly();
            }
            return;
        }

        bool shouldShow = compactHudMenu.IsMenuDrawerOpen;
        if (!shouldShow)
        {
            if (isDrawerVisible)
            {
                HideDrawerVisualOnly();
            }
            return;
        }

        EnsureUiBuilt();
        if (drawerRootObj == null)
        {
            return;
        }

        SuppressLegacyDrawerObjects();

        if (!isDrawerVisible)
        {
            isDrawerVisible = true;
            drawerRootObj.SetActive(true);
            ApplySafeAreaAndVerticalFit(force: true);
            RefreshDrawerDynamicStates();
        }
        else
        {
            ApplySafeAreaAndVerticalFit(force: false);
        }
    }

    public void CleanupImmediate()
    {
        RemoveBtnListener(scrimCloseButton, HandleCloseRequested);
        RemoveBtnListener(btnCloseX, HandleCloseRequested);
        if (modeAirCard != null) RemoveBtnListener(modeAirCard.button, HandleModeAirClicked);
        if (modeCardCard != null) RemoveBtnListener(modeCardCard.button, HandleModeCardClicked);
        if (modeSandboxCard != null) RemoveBtnListener(modeSandboxCard.button, HandleModeSandboxClicked);

        for (int i = 0; i < levelRows.Length; i++)
        {
            LevelRowUi row = levelRows[i];
            if (row != null)
            {
                RemoveBtnListener(row.button, row.clickHandler);
                levelRows[i] = null;
            }
        }

        RemoveBtnListener(btnOpenLevelMap, HandleOpenLevelMapClicked);
        RemoveBtnListener(btnCalibrateStage, HandleCalibrateStageClicked);
        RemoveBtnListener(btnAudioToggle, HandleAudioToggleClicked);
        RemoveBtnListener(btnOpenGuide, HandleOpenGuideClicked);
        RemoveBtnListener(btnTitleScreen, HandleTitleScreenClicked);
        RemoveBtnListener(btnResetCircuit, HandleResetCircuitClicked);

        isAirWeavingActive = false;
        isDrawerVisible = false;

        if (drawerRootObj != null)
        {
            Destroy(drawerRootObj);
            drawerRootObj = null;
        }

        scrimCloseButton = null;
        drawerPanelRect = null;
        drawerContentRect = null;
        btnCloseX = null;
        modeAirCard = null;
        modeCardCard = null;
        modeSandboxCard = null;
        btnOpenLevelMap = null;
        btnCalibrateStage = null;
        btnAudioToggle = null;
        txtAudioLabel = null;
        btnOpenGuide = null;
        btnTitleScreen = null;
        btnResetCircuit = null;
        legacyMenuDrawerObj = null;
        legacyMenuScrimObj = null;
        levelManager = null;
        compactHudMenu = null;
        targetCanvas = null;
        safeFont = null;
        isUiBuilt = false;
    }

    private void OnDestroy()
    {
        CleanupImmediate();
    }

    // =========================================================================
    // AKSI TOMBOL DRAWER
    // =========================================================================

    private void HandleCloseRequested()
    {
        CloseDrawerAndNotifyMenu();
    }

    private void HandleModeAirClicked()
    {
        CloseDrawerAndNotifyMenu();
        if (levelManager != null && levelManager.IsSandboxMode && !levelManager.IsVerifying)
        {
            levelManager.LoadLevel(levelManager.currentLevelIndex);
        }
    }

    private void HandleModeCardClicked()
    {
        CloseDrawerAndNotifyMenu();
        if (levelManager != null && isAirWeavingActive)
        {
            levelManager.TriggerToggleAirWeavingFromMenu();
        }
    }

    private void HandleModeSandboxClicked()
    {
        CloseDrawerAndNotifyMenu();
        if (levelManager != null && !levelManager.IsSandboxMode && !levelManager.IsVerifying)
        {
            levelManager.EnterSandboxMode();
        }
    }

    private void HandleLevelRowClicked(int levelIndex)
    {
        if (levelManager == null || levelManager.IsVerifying)
        {
            return;
        }

        CloseDrawerAndNotifyMenu();
        levelManager.LoadLevel(levelIndex);
    }

    private void HandleCalibrateStageClicked()
    {
        CloseDrawerAndNotifyMenu();
        OnRecenterStageRequested?.Invoke();
    }

    private void HandleAudioToggleClicked()
    {
        if (levelManager != null)
        {
            levelManager.ToggleAudioMute();
        }
        RefreshAudioButtonVisual();
    }

    private void HandleOpenGuideClicked()
    {
        CloseDrawerAndNotifyMenu();
        OnGestureGuideRequested?.Invoke();
    }

    private void HandleOpenLevelMapClicked()
    {
        CloseDrawerAndNotifyMenu();
        OnLevelSelectorRequested?.Invoke();
    }

    private void HandleTitleScreenClicked()
    {
        CloseDrawerAndNotifyMenu();
        OnTitleScreenRequested?.Invoke();
    }

    private void HandleResetCircuitClicked()
    {
        CloseDrawerAndNotifyMenu();
        OnResetCircuitRequested?.Invoke();
    }

    private void CloseDrawerAndNotifyMenu()
    {
        HideDrawerVisualOnly();
        if (compactHudMenu != null && compactHudMenu.IsMenuDrawerOpen)
        {
            compactHudMenu.CloseMenuDrawer();
        }
    }

    private void HideDrawerVisualOnly()
    {
        isDrawerVisible = false;
        if (drawerRootObj != null && drawerRootObj.activeSelf)
        {
            drawerRootObj.SetActive(false);
        }
    }

    private void SuppressLegacyDrawerObjects()
    {
        if (targetCanvas != null)
        {
            if (legacyMenuDrawerObj == null)
            {
                Transform tr = targetCanvas.transform.Find("Panel_CompactMenuDrawer");
                if (tr != null)
                {
                    legacyMenuDrawerObj = tr.gameObject;
                }
            }
            if (legacyMenuScrimObj == null)
            {
                Transform tr = targetCanvas.transform.Find("Btn_CompactMenuOutsideScrim");
                if (tr != null)
                {
                    legacyMenuScrimObj = tr.gameObject;
                }
            }
        }

        if (legacyMenuDrawerObj != null && legacyMenuDrawerObj.activeSelf)
        {
            legacyMenuDrawerObj.SetActive(false);
        }
        if (legacyMenuScrimObj != null && legacyMenuScrimObj.activeSelf)
        {
            legacyMenuScrimObj.SetActive(false);
        }
    }

    // =========================================================================
    // REFRESH STATE DINAMIS (EVENT-DRIVEN SAAT DRAWER DIBUKA, ZERO-GC)
    // =========================================================================

    private void RefreshDrawerDynamicStates()
    {
        bool isSandbox = levelManager != null && levelManager.IsSandboxMode;
        bool isAirCurriculum = isAirWeavingActive && !isSandbox;
        bool isCardMode = !isAirWeavingActive;

        ApplyModeCardVisual(modeAirCard, isActive: isAirCurriculum);
        ApplyModeCardVisual(modeCardCard, isActive: isCardMode);
        ApplyModeCardVisual(modeSandboxCard, isActive: isSandbox);

        int activeLevelIndex = levelManager != null ? levelManager.currentLevelIndex : 0;

        for (int i = 0; i < CurriculumLevelCount; i++)
        {
            LevelRowUi row = levelRows[i];
            if (row == null)
            {
                continue;
            }

            bool isCleared = PlayerPrefs.GetInt(CachedLevelPrefKeys[i], 0) == 1;
            bool isActive = !isSandbox && activeLevelIndex == i;

            if (row.bgImage != null)
            {
                row.bgImage.color = GwThemeTokens.CardIdle;
            }

            if (row.borderImage != null)
            {
                row.borderImage.color = isActive
                    ? GwThemeTokens.SignalHighTeal
                    : GwThemeTokens.HairlineIdle;
            }

            if (row.activeBarImage != null)
            {
                if (row.activeBarImage.enabled != isActive)
                {
                    row.activeBarImage.enabled = isActive;
                }
                if (isActive)
                {
                    row.activeBarImage.color = GwThemeTokens.SignalHighTeal;
                }
            }

            if (row.numberText != null)
            {
                row.numberText.color = isActive
                    ? GwThemeTokens.SignalHighTeal
                    : (isCleared ? GwThemeTokens.StatusPassText : GwThemeTokens.TextSecondary);
            }

            if (row.titleText != null)
            {
                row.titleText.color = isActive
                    ? GwThemeTokens.TextPrimary
                    : (isCleared ? GwThemeTokens.TextPrimary : GwThemeTokens.TextSecondary);
            }

            if (row.checkIconRoot != null && row.checkIconRoot.activeSelf != isCleared)
            {
                row.checkIconRoot.SetActive(isCleared);
            }

            if (row.statusText != null)
            {
                if (isCleared)
                {
                    row.statusText.text = "OK";
                    row.statusText.color = GwThemeTokens.StatusPassText;
                }
                else if (isActive)
                {
                    row.statusText.text = "AKTIF";
                    row.statusText.color = GwThemeTokens.SignalHighTeal;
                }
                else
                {
                    row.statusText.text = "--";
                    row.statusText.color = GwThemeTokens.SignalLowSlate;
                }
            }
        }

        RefreshAudioButtonVisual();
    }

    private static void ApplyModeCardVisual(ModeCardUi card, bool isActive)
    {
        if (card == null)
        {
            return;
        }

        if (card.bgImage != null)
        {
            card.bgImage.color = isActive ? GwThemeTokens.SmokedGlass : GwThemeTokens.CardIdle;
        }
        if (card.borderImage != null)
        {
            card.borderImage.color = isActive ? GwThemeTokens.SignalHighTeal : GwThemeTokens.HairlineIdle;
        }
        if (card.activeDotImage != null)
        {
            card.activeDotImage.color = isActive ? GwThemeTokens.SignalHighTeal : GwThemeTokens.SignalLowSlate;
        }
        if (card.titleText != null)
        {
            card.titleText.color = isActive ? GwThemeTokens.TextPrimary : GwThemeTokens.TextSecondary;
        }
        if (card.stateText != null)
        {
            card.stateText.text = isActive ? "AKTIF" : "PILIH";
            card.stateText.color = isActive ? GwThemeTokens.SignalHighTeal : GwThemeTokens.SignalLowSlate;
        }
    }

    private void RefreshAudioButtonVisual()
    {
        if (txtAudioLabel == null)
        {
            return;
        }

        bool isMuted = levelManager != null && levelManager.isAudioMuted;
        txtAudioLabel.text = isMuted ? "AUDIO: OFF" : "AUDIO: ON";
        txtAudioLabel.color = isMuted ? GwThemeTokens.AccentSwitchOrange : GwThemeTokens.TextPrimary;
    }

    // =========================================================================
    // KONSTRUKSI UI PROSEDURAL 100% TOKEN GwThemeTokens
    // =========================================================================

    private void EnsureUiBuilt()
    {
        if (isUiBuilt)
        {
            return;
        }

        if (targetCanvas == null)
        {
            targetCanvas = compactHudMenu != null && compactHudMenu.TargetCanvas != null
                ? compactHudMenu.TargetCanvas
                : FindTargetCanvas();
        }
        if (targetCanvas == null)
        {
            return;
        }

        if (safeFont == null)
        {
            safeFont = compactHudMenu != null && compactHudMenu.SafeFont != null
                ? compactHudMenu.SafeFont
                : TMP_Settings.defaultFontAsset;
        }

        BuildDrawerHierarchy();
        isUiBuilt = true;
        ApplySafeAreaAndVerticalFit(force: true);
    }

    private void BuildDrawerHierarchy()
    {
        // Dedicated ScreenSpaceOverlay Canvas (sortingOrder = 30) strictly di atas Top HUD (0) & Pedagogical HUD (5)
        drawerRootObj = new GameObject(
            "Canvas_AirMenuDrawerOverlay_V85",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        drawerRootObj.transform.SetParent(transform, false);

        Canvas overlayCanvas = drawerRootObj.GetComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = OverlayCanvasSortingOrder;

        CanvasScaler scaler = drawerRootObj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceCanvasWidth, ReferenceCanvasHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform rootRect = drawerRootObj.GetComponent<RectTransform>();

        // 1. Full-screen dark dim scrim (#090B10 62%, raycast blocker aktif) di belakang drawer
        GameObject scrimObj = new GameObject("Btn_AirMenuDrawerScrim_V85", typeof(RectTransform), typeof(Image), typeof(Button));
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
        scrimCloseButton.onClick.AddListener(HandleCloseRequested);

        // 2. Panel samping kanan DrawerSurfaceUltra (#12141C 98%) + garis tepi kiri 1px HairlineIdle (tanpa border kanan)
        GameObject panelObj = new GameObject("Panel_AirMenuDrawer_V85", typeof(RectTransform), typeof(Image));
        panelObj.transform.SetParent(rootRect, false);
        drawerPanelRect = panelObj.GetComponent<RectTransform>();
        drawerPanelRect.anchorMin = new Vector2(1f, 0f);
        drawerPanelRect.anchorMax = new Vector2(1f, 1f);
        drawerPanelRect.pivot = new Vector2(1f, 0.5f);
        drawerPanelRect.anchoredPosition = Vector2.zero;
        drawerPanelRect.sizeDelta = new Vector2(ReferenceDrawerWidth, 0f);

        Image panelBg = panelObj.GetComponent<Image>();
        panelBg.color = GwThemeTokens.DrawerSurfaceUltra;
        panelBg.raycastTarget = true;

        // Garis tepi kiri 1px HairlineIdle (tanpa border kanan/atas/bawah)
        CreateFlatImage(
            "Drawer_LeftHairline",
            drawerPanelRect,
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(0f, 0.5f),
            Vector2.zero,
            new Vector2(GwThemeTokens.HairlineBorderPx, 0f),
            GwThemeTokens.HairlineIdle);

        // Kontainer konten top-anchored yang otomatis scale-fit vertikal bila viewport pendek
        GameObject contentObj = new GameObject("Drawer_Content", typeof(RectTransform));
        contentObj.transform.SetParent(drawerPanelRect, false);
        drawerContentRect = contentObj.GetComponent<RectTransform>();
        drawerContentRect.anchorMin = new Vector2(0f, 1f);
        drawerContentRect.anchorMax = new Vector2(0f, 1f);
        drawerContentRect.pivot = new Vector2(0f, 1f);
        drawerContentRect.anchoredPosition = Vector2.zero;
        drawerContentRect.sizeDelta = new Vector2(ReferenceDrawerWidth, ReferenceContentHeight);

        float innerWidth = ReferenceDrawerWidth - (ContentPadX * 2f);
        float y = -ContentPadTop;

        // ---------------------------------------------------------------------
        // HEADER: MENU NAVIGASI + GATEWEAVER AR // v8.5 + Tombol Tutup X
        // ---------------------------------------------------------------------
        TMP_Text titleTmp = CreateTopLeftTmp(
            "Txt_DrawerHeader",
            drawerContentRect,
            new Vector2(ContentPadX, y),
            new Vector2(innerWidth - 46f, 28f),
            "MENU NAVIGASI",
            18f,
            22f,
            GwThemeTokens.TextPrimary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);
        _ = titleTmp;

        CreateTopLeftTmp(
            "Txt_DrawerSub",
            drawerContentRect,
            new Vector2(ContentPadX, y - 28f),
            new Vector2(innerWidth - 46f, 20f),
            "GATEWEAVER AR // v8.5",
            11.5f,
            13.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Normal);

        btnCloseX = CreateRoundedFlatButton(
            "Btn_DrawerCloseX",
            drawerContentRect,
            ContentPadX + innerWidth - 36f,
            y - 4f,
            36f,
            36f,
            "X",
            GwThemeTokens.CardIdle,
            GwThemeTokens.HairlineIdle,
            GwThemeTokens.TextPrimary,
            16f,
            20f,
            HandleCloseRequested,
            out _);

        y -= 60f;
        CreateHorizontalDivider("Divider_Header", drawerContentRect, ContentPadX, y, innerWidth);
        y -= 14f;

        // ---------------------------------------------------------------------
        // SEKSI 1: MODE SIMULASI (MODE UDARA | MODE KARTU | SANDBOX)
        // ---------------------------------------------------------------------
        CreateSectionLabel("Sec_ModeSimulasi", drawerContentRect, ContentPadX, y, innerWidth, "MODE SIMULASI");
        y -= 24f;

        const float modeGap = 8f;
        float modeCardW = (innerWidth - (modeGap * 2f)) / 3f;
        const float modeCardH = 62f;

        modeAirCard = CreateModeCard(
            "Card_ModeAir",
            drawerContentRect,
            ContentPadX,
            y,
            modeCardW,
            modeCardH,
            "MODE UDARA",
            HandleModeAirClicked);

        modeCardCard = CreateModeCard(
            "Card_ModeCard",
            drawerContentRect,
            ContentPadX + modeCardW + modeGap,
            y,
            modeCardW,
            modeCardH,
            "MODE KARTU",
            HandleModeCardClicked);

        modeSandboxCard = CreateModeCard(
            "Card_ModeSandbox",
            drawerContentRect,
            ContentPadX + ((modeCardW + modeGap) * 2f),
            y,
            modeCardW,
            modeCardH,
            "SANDBOX",
            HandleModeSandboxClicked);

        y -= (modeCardH + 16f);
        CreateHorizontalDivider("Divider_Modes", drawerContentRect, ContentPadX, y, innerWidth);
        y -= 14f;

        // ---------------------------------------------------------------------
        // SEKSI 2: DAFTAR LEVEL 01-07 + TOMBOL BUKA PETA -> (05_Level_Selector)
        // ---------------------------------------------------------------------
        const float openMapBtnW = 116f;
        const float openMapBtnH = 24f;
        CreateSectionLabel("Sec_LevelList", drawerContentRect, ContentPadX, y - 3f, innerWidth - openMapBtnW - 8f, "DAFTAR LEVEL (01-07)");

        btnOpenLevelMap = CreateRoundedFlatButton(
            "Btn_OpenLevelMap",
            drawerContentRect,
            ContentPadX + innerWidth - openMapBtnW,
            y,
            openMapBtnW,
            openMapBtnH,
            "BUKA PETA ->",
            GwThemeTokens.CardIdle,
            GwThemeTokens.SignalHighTeal,
            GwThemeTokens.SignalHighTeal,
            10f,
            11.5f,
            HandleOpenLevelMapClicked,
            out _);
        y -= 30f;

        const float levelRowH = 44f;
        const float levelRowGap = 6f;

        for (int i = 0; i < CurriculumLevelCount; i++)
        {
            levelRows[i] = CreateLevelRow(
                $"Row_Level_{i + 1}",
                drawerContentRect,
                ContentPadX,
                y,
                innerWidth,
                levelRowH,
                i);
            y -= (levelRowH + levelRowGap);
        }

        y -= 10f;
        CreateHorizontalDivider("Divider_Levels", drawerContentRect, ContentPadX, y, innerWidth);
        y -= 14f;

        // ---------------------------------------------------------------------
        // SEKSI 3: KONTROL & KALIBRASI
        // ---------------------------------------------------------------------
        CreateSectionLabel("Sec_Controls", drawerContentRect, ContentPadX, y, innerWidth, "KONTROL & KALIBRASI");
        y -= 24f;

        const float ctrlBtnH = 42f;
        const float ctrlGap = 8f;

        btnCalibrateStage = CreateRoundedFlatButton(
            "Btn_CalibrateStage",
            drawerContentRect,
            ContentPadX,
            y,
            innerWidth,
            ctrlBtnH,
            "KALIBRASI & PUSATKAN PANGGUNG",
            GwThemeTokens.CardIdle,
            GwThemeTokens.HairlineIdle,
            GwThemeTokens.TextPrimary,
            12.5f,
            15f,
            HandleCalibrateStageClicked,
            out _);
        y -= (ctrlBtnH + ctrlGap);

        float halfW = (innerWidth - ctrlGap) * 0.5f;
        btnAudioToggle = CreateRoundedFlatButton(
            "Btn_AudioToggle",
            drawerContentRect,
            ContentPadX,
            y,
            halfW,
            ctrlBtnH,
            "AUDIO: ON",
            GwThemeTokens.CardIdle,
            GwThemeTokens.HairlineIdle,
            GwThemeTokens.TextPrimary,
            12.5f,
            15f,
            HandleAudioToggleClicked,
            out txtAudioLabel);

        btnOpenGuide = CreateRoundedFlatButton(
            "Btn_OpenGuide",
            drawerContentRect,
            ContentPadX + halfW + ctrlGap,
            y,
            halfW,
            ctrlBtnH,
            "PANDUAN GESTUR",
            GwThemeTokens.CardIdle,
            GwThemeTokens.HairlineIdle,
            GwThemeTokens.TextPrimary,
            12.5f,
            15f,
            HandleOpenGuideClicked,
            out _);
        y -= (ctrlBtnH + ctrlGap);

        btnTitleScreen = CreateRoundedFlatButton(
            "Btn_OpenTitleScreen",
            drawerContentRect,
            ContentPadX,
            y,
            innerWidth,
            38f,
            "LAYAR UTAMA",
            GwThemeTokens.CardIdle,
            GwThemeTokens.HairlineActive,
            GwThemeTokens.TextPrimary,
            12.5f,
            15f,
            HandleTitleScreenClicked,
            out _);
        y -= (38f + ctrlGap);

        btnResetCircuit = CreateRoundedFlatButton(
            "Btn_ResetCircuit",
            drawerContentRect,
            ContentPadX,
            y,
            innerWidth,
            44f,
            "RESET RANGKAIAN",
            GwThemeTokens.CardIdle,
            GwThemeTokens.StatusErrorRed,
            GwThemeTokens.StatusErrorText,
            13f,
            15.5f,
            HandleResetCircuitClicked,
            out _);

        drawerRootObj.SetActive(false);
    }

    private ModeCardUi CreateModeCard(
        string name,
        RectTransform parent,
        float leftX,
        float topY,
        float width,
        float height,
        string label,
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

        Button btn = obj.GetComponent<Button>();
        btn.targetGraphic = bg;
        btn.transition = Selectable.Transition.ColorTint;
        btn.colors = GwThemeTokens.FlatGlassButtonColors;
        if (onClick != null)
        {
            btn.onClick.AddListener(onClick);
        }

        // Indikator titik status aktif di pojok kiri atas kartu
        GameObject dotObj = new GameObject("ActiveDot", typeof(RectTransform), typeof(Image));
        dotObj.transform.SetParent(rect, false);
        RectTransform dotRect = dotObj.GetComponent<RectTransform>();
        dotRect.anchorMin = new Vector2(0f, 1f);
        dotRect.anchorMax = new Vector2(0f, 1f);
        dotRect.pivot = new Vector2(0.5f, 0.5f);
        dotRect.anchoredPosition = new Vector2(12f, -13f);
        dotRect.sizeDelta = new Vector2(GwThemeTokens.StatusDotSizePx, GwThemeTokens.StatusDotSizePx);

        Image dotImg = dotObj.GetComponent<Image>();
        dotImg.sprite = GwThemeTokens.GetCircleSprite();
        dotImg.type = Image.Type.Simple;
        dotImg.color = GwThemeTokens.SignalLowSlate;
        dotImg.raycastTarget = false;

        TMP_Text stateTxt = CreateTopLeftTmp(
            "Txt_ModeState",
            rect,
            new Vector2(20f, -5f),
            new Vector2(width - 26f, 16f),
            "PILIH",
            9.5f,
            11f,
            GwThemeTokens.SignalLowSlate,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);

        TMP_Text titleTxt = CreateTopLeftTmp(
            "Txt_ModeTitle",
            rect,
            new Vector2(8f, -26f),
            new Vector2(width - 16f, 28f),
            label,
            11f,
            13.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.Center,
            FontStyles.Bold);

        return new ModeCardUi
        {
            button = btn,
            bgImage = bg,
            borderImage = border,
            activeDotImage = dotImg,
            titleText = titleTxt,
            stateText = stateTxt
        };
    }

    private LevelRowUi CreateLevelRow(
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

        // Strip indikator kiri tipis 3px untuk level aktif (SignalHighTeal)
        Image activeBar = CreateFlatImage(
            "ActiveBar",
            rect,
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(2f, 0f),
            new Vector2(3f, height - 14f),
            GwThemeTokens.SignalHighTeal);
        activeBar.enabled = false;

        int capturedIndex = levelIndex;
        UnityEngine.Events.UnityAction handler = () => HandleLevelRowClicked(capturedIndex);

        Button btn = obj.GetComponent<Button>();
        btn.targetGraphic = bg;
        btn.transition = Selectable.Transition.ColorTint;
        btn.colors = GwThemeTokens.FlatGlassButtonColors;
        btn.onClick.AddListener(handler);

        TMP_Text numTxt = CreateTopLeftTmp(
            "Txt_LvlNum",
            rect,
            new Vector2(12f, 0f),
            new Vector2(30f, height),
            CachedLevelNumberLabels[levelIndex],
            12f,
            14.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.Center,
            FontStyles.Bold);

        TMP_Text titleTxt = CreateTopLeftTmp(
            "Txt_LvlTitle",
            rect,
            new Vector2(46f, 0f),
            new Vector2(width - 118f, height),
            CachedLevelTitles[levelIndex],
            12f,
            14.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Normal);

        // Ikon centang geometris prosedural 2-segmen (100% ASCII-safe, tanpa missing glyph font)
        GameObject checkRoot = CreateGeometricCheckmark("Icon_ClearedCheck", rect, width - 66f, -height * 0.5f, GwThemeTokens.StatusPassText);
        checkRoot.SetActive(false);

        TMP_Text statusTxt = CreateTopLeftTmp(
            "Txt_LvlStatus",
            rect,
            new Vector2(width - 54f, 0f),
            new Vector2(44f, height),
            "--",
            11f,
            13f,
            GwThemeTokens.SignalLowSlate,
            TextAlignmentOptions.MidlineRight,
            FontStyles.Bold);

        return new LevelRowUi
        {
            button = btn,
            clickHandler = handler,
            bgImage = bg,
            borderImage = border,
            activeBarImage = activeBar,
            numberText = numTxt,
            titleText = titleTxt,
            checkIconRoot = checkRoot,
            statusText = statusTxt
        };
    }

    private static GameObject CreateGeometricCheckmark(string name, RectTransform parent, float leftX, float centerY, Color color)
    {
        GameObject root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.anchoredPosition = new Vector2(leftX, centerY);
        rootRect.sizeDelta = new Vector2(14f, 14f);

        Image shortArm = CreateFlatImage(
            "Check_Short",
            rootRect,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(-3.0f, -1.2f),
            new Vector2(5.2f, 1.8f),
            color);
        shortArm.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);

        Image longArm = CreateFlatImage(
            "Check_Long",
            rootRect,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(1.4f, 0.4f),
            new Vector2(9.2f, 1.8f),
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

        GwThemeTokens.AttachHairlineBorder(rect, $"{name}_Border", borderColor);

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

    private void ApplySafeAreaAndVerticalFit(bool force)
    {
        if (drawerPanelRect == null || drawerContentRect == null)
        {
            return;
        }

        int sw = Screen.width;
        int sh = Screen.height;
        Rect sa = Screen.safeArea;
        if (!force && sw == lastScreenW && sh == lastScreenH && sa == lastSafeArea)
        {
            return;
        }

        lastScreenW = sw;
        lastScreenH = sh;
        lastSafeArea = sa;

        RectTransform rootRect = drawerRootObj != null ? drawerRootObj.GetComponent<RectTransform>() : null;
        float canvasW = (rootRect != null && rootRect.rect.width > 400f) ? rootRect.rect.width : 1920f;
        float canvasH = (rootRect != null && rootRect.rect.height > 300f) ? rootRect.rect.height : 1080f;

        float denomW = Mathf.Max(1f, sw);
        float denomH = Mathf.Max(1f, sh);
        float rightInsetCanvas = Mathf.Clamp((denomW - sa.xMax) / denomW, 0f, 0.08f) * canvasW;
        float topInsetCanvas = Mathf.Clamp((denomH - sa.yMax) / denomH, 0f, 0.08f) * canvasH;
        float bottomInsetCanvas = Mathf.Clamp(sa.yMin / denomH, 0f, 0.08f) * canvasH;

        float availH = Mathf.Max(320f, canvasH - topInsetCanvas - bottomInsetCanvas);
        float fitScale = Mathf.Clamp(availH / (ReferenceContentHeight + ContentPadBottom), 0.65f, 1.0f);

        float scaledContentW = ReferenceDrawerWidth * fitScale;
        drawerPanelRect.sizeDelta = new Vector2(scaledContentW + rightInsetCanvas, 0f);
        drawerContentRect.anchoredPosition = new Vector2(0f, -topInsetCanvas);
        drawerContentRect.localScale = new Vector3(fitScale, fitScale, 1f);
    }

    private void CreateSectionLabel(string name, RectTransform parent, float leftX, float topY, float width, string text)
    {
        CreateTopLeftTmp(
            name,
            parent,
            new Vector2(leftX, topY),
            new Vector2(width, 18f),
            text,
            10.5f,
            12.5f,
            GwThemeTokens.TextSecondary,
            TextAlignmentOptions.MidlineLeft,
            FontStyles.Bold);
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
