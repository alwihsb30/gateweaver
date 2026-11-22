using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Panduan Gestur Interaktif 1 Halaman &amp; Latihan Cepat 4 Langkah khusus Mode Udara
/// (GateWeaver AR v8.5 - Smoked Optical Glass + Brushed Titanium, 06_Modal_Guide).
/// - 100% memakai token warna, metrik (radius 6px, hairline 1px), &amp; sprite 9-slice GwThemeTokens.
/// - Tata letak 2 kolom sejajar:
///   * Kiri: 3 gestur utama (KEPAL (FIST), CUBIT (PINCH), DUA TANGAN) dalam kartu CardIdle + HairlineIdle.
///   * Kanan: Grid 5 kartu pose summon gerbang (AND 1 Telunjuk, OR 2 Jari V, NOT Jempol Atas, XOR 3 Jari, NAND 4 Jari)
///     dengan aksen #38BDF8 + catatan alur 2-tahap (Circle-Charge -&gt; Open-5).
/// - Tombol aksi tutup utama ("PAHAM, KEMBALI KE SIMULASI") bergaya primer solid PrimaryFill (#FFFFFF) + PrimaryText (#12141C bold).
/// - Pil Latihan Cepat 4 Langkah non-intrusif di tengah-bawah layar yang melacak aksi tangan nyata Zero-GC.
/// - 0% regresi Mode Kartu Vuforia v5, 100% pure ASCII 7-bit, tanpa neon glow, tanpa LINQ.
/// </summary>
public sealed class AirWeavingGestureGuide : MonoBehaviour
{
    private const string PrefKeyGuideSeen = "GW_AirGuideSeen_v75";
    private const int OverlayCanvasSortingOrder = 25;
    private const float ReferenceCanvasWidth = 1920f;
    private const float ReferenceCanvasHeight = 1080f;
    private const float GuideCardWidth = 1220f;
    private const float GuideCardHeight = 668f;
    private const float PracticePillWidth = 840f;
    private const float PracticePillHeight = 58f;
    private const float StepCompleteBeatSeconds = 0.42f;
    private const float CompletionBannerSeconds = 1.60f;
    private const float AudioReferenceDepth = 3.60f;

    private enum PracticeState
    {
        Inactive = 0,
        Step1ToggleInput = 1,
        Step2SummonGate = 2,
        Step3ConnectCable = 3,
        Step4DeleteGate = 4,
        CompletedBanner = 5
    }

    public event Action OnGuideOverlayOpened;

    private CompactHudMenuController compactHudMenu;
    private AirWeavingSpatialSfx spatialSfx;
    private Camera referenceCamera;
    private Canvas targetCanvas;
    private TMP_FontAsset safeFont;

    // Overlay Panduan 1 Halaman (06_Modal_Guide)
    private GameObject guideRootObj;
    private Button guideScrimButton;
    private RectTransform guideSafeAreaRect;
    private RectTransform guideCardRect;
    private Button btnCloseTopX;
    private Button btnStartPractice;
    private Button btnCloseGuide;

    // Pil Latihan Cepat 4 Langkah
    private GameObject practiceRootObj;
    private RectTransform practiceSafeAreaRect;
    private GameObject practicePillObj;
    private Image practicePillBg;
    private Image practicePillBorderImg;
    private Image practiceBadgeBg;
    private Image practiceBadgeBorderImg;
    private TMP_Text txtPracticeBadge;
    private RectTransform practicePromptRect;
    private TMP_Text txtPracticePrompt;
    private GameObject skipPracticeBtnObj;
    private Button btnSkipPractice;

    // Pra-cache string ASCII 7-bit Zero-GC (bebas alokasi saat runtime)
    private const string Step1PromptText = "Kepal tangan di sakelar kiri untuk balik 0 <-> 1";
    private const string Step2PromptText = "Putar 1-4 jari atau jempol, lalu buka lebar 5 jari (Open-5)";
    private const string Step3PromptText = "Cubit orb port lalu sambungkan 1 kabel";
    private const string Step4PromptText = "Kepal gerbang lalu buang keluar batas zona rakit";
    private const string CompletedBannerText = "SIAP MERAKIT";
    private const string BadgeStep1 = "1/4";
    private const string BadgeStep2 = "2/4";
    private const string BadgeStep3 = "3/4";
    private const string BadgeStep4 = "4/4";
    private const string BadgeStepOk = "OK";

    private bool isUiBuilt = false;
    private bool isAirWeavingActive = false;
    private bool isGuideOverlayOpen = false;
    private PracticeState practiceState = PracticeState.Inactive;
    private PracticeState pendingNextState = PracticeState.Inactive;
    private float stepCompleteBeatTimer = 0f;
    private float completionBannerTimer = 0f;

    private Rect lastAppliedSafeArea;
    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;
    private bool hasAppliedCardScale = false;

    public bool IsGuideOverlayOpen => isGuideOverlayOpen;
    public bool IsPracticeActive => practiceState != PracticeState.Inactive;

    public void Configure(
        CompactHudMenuController hudMenu,
        AirWeavingSpatialSfx sfx,
        Camera mainCam,
        TMP_FontAsset fallbackFont = null)
    {
        if (compactHudMenu != hudMenu)
        {
            UnsubscribeHudEvents();
            compactHudMenu = hudMenu;
            SubscribeHudEvents();
        }

        spatialSfx = sfx;
        if (mainCam != null)
        {
            referenceCamera = mainCam;
        }

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
        StopPracticeImmediate();

        if (PlayerPrefs.GetInt(PrefKeyGuideSeen, 0) == 0)
        {
            OpenGuideOverlay();
        }
        else
        {
            CloseGuideOverlay(markAsSeen: false);
        }
    }

    public void OnAirWeavingModeExited()
    {
        isAirWeavingActive = false;
        CloseGuideOverlay(markAsSeen: false);
        StopPracticeImmediate();
    }

    public void OnExternalModalOrMenuOpened()
    {
        if (isGuideOverlayOpen)
        {
            CloseGuideOverlay(markAsSeen: true);
        }
        if (practiceState != PracticeState.Inactive)
        {
            StopPracticeImmediate();
        }
    }

    public void Tick(float dt, Camera mainCam)
    {
        if (mainCam != null)
        {
            referenceCamera = mainCam;
        }

        if (!isUiBuilt)
        {
            return;
        }

        if (isGuideOverlayOpen || practiceState != PracticeState.Inactive)
        {
            ApplySafeAreaAndResponsiveScaleIfNeeded(force: false);
        }

        if (isGuideOverlayOpen && spatialSfx != null)
        {
            spatialSfx.StopHumImmediate();
        }

        if (practiceState == PracticeState.Inactive)
        {
            return;
        }

        if (stepCompleteBeatTimer > 0f)
        {
            stepCompleteBeatTimer -= dt;
            if (stepCompleteBeatTimer <= 0f)
            {
                stepCompleteBeatTimer = 0f;
                if (pendingNextState != PracticeState.Inactive)
                {
                    practiceState = pendingNextState;
                    pendingNextState = PracticeState.Inactive;
                    RefreshPracticePillVisuals(isStepFlashSuccess: false);
                }
            }
            return;
        }

        if (practiceState == PracticeState.CompletedBanner)
        {
            completionBannerTimer -= dt;
            if (completionBannerTimer <= 0f)
            {
                StopPracticeImmediate();
            }
        }
    }

    // =========================================================================
    // 4 NOTIFIKASI AKSI NYATA ZERO-GC UNTUK LATIHAN CEPAT BERURUTAN
    // =========================================================================

    public void NotifyInputToggled()
    {
        if (!isAirWeavingActive || isGuideOverlayOpen || stepCompleteBeatTimer > 0f)
        {
            return;
        }

        if (practiceState == PracticeState.Step1ToggleInput)
        {
            CompleteCurrentStepAndAdvance(completedStepNumber: 1, nextState: PracticeState.Step2SummonGate);
        }
    }

    public void NotifyGateSummoned()
    {
        if (!isAirWeavingActive || isGuideOverlayOpen || stepCompleteBeatTimer > 0f)
        {
            return;
        }

        if (practiceState == PracticeState.Step2SummonGate)
        {
            CompleteCurrentStepAndAdvance(completedStepNumber: 2, nextState: PracticeState.Step3ConnectCable);
        }
    }

    public void NotifyCableConnected()
    {
        if (!isAirWeavingActive || isGuideOverlayOpen || stepCompleteBeatTimer > 0f)
        {
            return;
        }

        if (practiceState == PracticeState.Step3ConnectCable)
        {
            CompleteCurrentStepAndAdvance(completedStepNumber: 3, nextState: PracticeState.Step4DeleteGate);
        }
    }

    public void NotifyGateDeletedOutsideZone()
    {
        if (!isAirWeavingActive || isGuideOverlayOpen || stepCompleteBeatTimer > 0f)
        {
            return;
        }

        if (practiceState == PracticeState.Step4DeleteGate)
        {
            practiceState = PracticeState.CompletedBanner;
            pendingNextState = PracticeState.Inactive;
            stepCompleteBeatTimer = 0f;
            completionBannerTimer = CompletionBannerSeconds;
            RefreshPracticePillVisuals(isStepFlashSuccess: true);
            PlayPracticeCompleteChime();
        }
    }

    // =========================================================================
    // KONTROL OVERLAY PANDUAN & PIL LATIHAN CEPAT
    // =========================================================================

    public void ToggleGuideOverlay()
    {
        if (!isAirWeavingActive)
        {
            return;
        }

        if (isGuideOverlayOpen)
        {
            CloseGuideOverlay(markAsSeen: true);
        }
        else
        {
            OpenGuideOverlay();
        }
    }

    public void OpenGuideOverlay()
    {
        if (!isAirWeavingActive)
        {
            return;
        }

        EnsureUiBuilt();
        if (guideRootObj == null)
        {
            return;
        }

        StopPracticeImmediate();
        if (compactHudMenu != null)
        {
            compactHudMenu.CloseMenuDrawer();
            compactHudMenu.ClearToastImmediate();
        }
        if (spatialSfx != null)
        {
            spatialSfx.StopHumImmediate();
        }

        OnGuideOverlayOpened?.Invoke();

        isGuideOverlayOpen = true;
        guideRootObj.SetActive(true);
        ApplySafeAreaAndResponsiveScaleIfNeeded(force: true);
        if (compactHudMenu != null)
        {
            compactHudMenu.SetGuideButtonActiveState(true);
        }
    }

    public void CloseGuideOverlay(bool markAsSeen)
    {
        if (markAsSeen)
        {
            MarkGuideSeenPersistent();
        }

        isGuideOverlayOpen = false;
        if (guideRootObj != null && guideRootObj.activeSelf)
        {
            guideRootObj.SetActive(false);
        }
        if (compactHudMenu != null)
        {
            compactHudMenu.SetGuideButtonActiveState(false);
        }
    }

    public void StartQuickPractice()
    {
        if (!isAirWeavingActive)
        {
            return;
        }

        EnsureUiBuilt();
        if (practiceRootObj == null)
        {
            return;
        }

        CloseGuideOverlay(markAsSeen: true);
        if (compactHudMenu != null)
        {
            compactHudMenu.CloseMenuDrawer();
        }

        practiceState = PracticeState.Step1ToggleInput;
        pendingNextState = PracticeState.Inactive;
        stepCompleteBeatTimer = 0f;
        completionBannerTimer = 0f;

        practiceRootObj.transform.SetAsLastSibling();
        practiceRootObj.SetActive(true);
        ApplySafeAreaAndResponsiveScaleIfNeeded(force: true);
        RefreshPracticePillVisuals(isStepFlashSuccess: false);
        if (compactHudMenu != null)
        {
            compactHudMenu.SetPracticePillBottomOffset(true);
        }
    }

    public void StopPracticeImmediate()
    {
        practiceState = PracticeState.Inactive;
        pendingNextState = PracticeState.Inactive;
        stepCompleteBeatTimer = 0f;
        completionBannerTimer = 0f;

        if (practiceRootObj != null && practiceRootObj.activeSelf)
        {
            practiceRootObj.SetActive(false);
        }
        if (compactHudMenu != null)
        {
            compactHudMenu.SetPracticePillBottomOffset(false);
        }
    }

    public void CleanupImmediate()
    {
        UnsubscribeHudEvents();
        RemoveBtnListener(guideScrimButton, OnCloseGuideClicked);
        RemoveBtnListener(btnCloseTopX, OnCloseGuideClicked);
        RemoveBtnListener(btnStartPractice, OnStartPracticeClicked);
        RemoveBtnListener(btnCloseGuide, OnCloseGuideClicked);
        RemoveBtnListener(btnSkipPractice, OnSkipPracticeClicked);

        if (compactHudMenu != null)
        {
            compactHudMenu.SetGuideButtonActiveState(false);
            compactHudMenu.SetPracticePillBottomOffset(false);
        }

        isAirWeavingActive = false;
        isGuideOverlayOpen = false;
        practiceState = PracticeState.Inactive;
        pendingNextState = PracticeState.Inactive;
        stepCompleteBeatTimer = 0f;
        completionBannerTimer = 0f;

        if (guideRootObj != null)
        {
            Destroy(guideRootObj);
            guideRootObj = null;
        }
        if (practiceRootObj != null)
        {
            Destroy(practiceRootObj);
            practiceRootObj = null;
        }

        guideScrimButton = null;
        guideSafeAreaRect = null;
        guideCardRect = null;
        btnCloseTopX = null;
        btnStartPractice = null;
        btnCloseGuide = null;
        practiceSafeAreaRect = null;
        practicePillObj = null;
        practicePillBg = null;
        practicePillBorderImg = null;
        practiceBadgeBg = null;
        practiceBadgeBorderImg = null;
        txtPracticeBadge = null;
        practicePromptRect = null;
        txtPracticePrompt = null;
        skipPracticeBtnObj = null;
        btnSkipPractice = null;
        compactHudMenu = null;
        spatialSfx = null;
        referenceCamera = null;
        targetCanvas = null;
        safeFont = null;
        isUiBuilt = false;
        hasAppliedCardScale = false;
    }

    private void OnDestroy()
    {
        CleanupImmediate();
    }

    // =========================================================================
    // EVENT HANDLERS & AUDIO CHIME
    // =========================================================================

    private void SubscribeHudEvents()
    {
        if (compactHudMenu == null)
        {
            return;
        }

        compactHudMenu.OnGestureGuideRequested += HandleHudGuideRequested;
        compactHudMenu.OnMenuDrawerOpened += HandleHudMenuDrawerOpened;
    }

    private void UnsubscribeHudEvents()
    {
        if (compactHudMenu == null)
        {
            return;
        }

        compactHudMenu.OnGestureGuideRequested -= HandleHudGuideRequested;
        compactHudMenu.OnMenuDrawerOpened -= HandleHudMenuDrawerOpened;
    }

    private void HandleHudGuideRequested()
    {
        ToggleGuideOverlay();
    }

    private void HandleHudMenuDrawerOpened()
    {
        OnExternalModalOrMenuOpened();
    }

    private void OnStartPracticeClicked()
    {
        if (spatialSfx != null)
        {
            spatialSfx.PlayHologramWake(ResolveCenterAudioWorldPos());
        }
        StartQuickPractice();
    }

    private void OnCloseGuideClicked()
    {
        CloseGuideOverlay(markAsSeen: true);
    }

    private void OnSkipPracticeClicked()
    {
        StopPracticeImmediate();
    }

    private void CompleteCurrentStepAndAdvance(int completedStepNumber, PracticeState nextState)
    {
        pendingNextState = nextState;
        stepCompleteBeatTimer = StepCompleteBeatSeconds;
        RefreshPracticePillVisuals(isStepFlashSuccess: true);
        PlayPracticeStepChime(completedStepNumber);
    }

    private void PlayPracticeStepChime(int stepNumber)
    {
        if (spatialSfx == null)
        {
            return;
        }

        spatialSfx.PlayTruthPodStep(stepNumber, ResolveCenterAudioWorldPos());
    }

    private void PlayPracticeCompleteChime()
    {
        if (spatialSfx == null)
        {
            return;
        }

        spatialSfx.PlayTruthPodPassAllFour(ResolveCenterAudioWorldPos());
    }

    private Vector3 ResolveCenterAudioWorldPos()
    {
        if (referenceCamera != null)
        {
            return referenceCamera.transform.TransformPoint(new Vector3(0f, 0f, AudioReferenceDepth));
        }

        return Vector3.zero;
    }

    private static void MarkGuideSeenPersistent()
    {
        if (PlayerPrefs.GetInt(PrefKeyGuideSeen, 0) != 1)
        {
            PlayerPrefs.SetInt(PrefKeyGuideSeen, 1);
            PlayerPrefs.Save();
        }
    }

    // =========================================================================
    // PEMBANGUNAN UI PROSEDURAL 100% TOKEN GwThemeTokens (06_Modal_Guide)
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

        BuildGuideOverlayUi();
        BuildPracticePillUi(targetCanvas.transform);

        isUiBuilt = true;
        ApplySafeAreaAndResponsiveScaleIfNeeded(force: true);
    }

    private void BuildGuideOverlayUi()
    {
        // Dedicated ScreenSpaceOverlay Canvas (sortingOrder = 25) strictly di atas Top HUD (0) & Pedagogical HUD (5)
        guideRootObj = new GameObject(
            "Canvas_AirGestureGuideOverlay_V85",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        guideRootObj.transform.SetParent(transform, false);

        Canvas overlayCanvas = guideRootObj.GetComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = OverlayCanvasSortingOrder;

        CanvasScaler scaler = guideRootObj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceCanvasWidth, ReferenceCanvasHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform rootRect = guideRootObj.GetComponent<RectTransform>();

        // Full-screen dark dim scrim (#090B10 62%, raycast blocker aktif) meredupkan Top HUD & 3D panggung
        GameObject scrimObj = new GameObject("Btn_AirGestureGuideScrim", typeof(RectTransform), typeof(Image), typeof(Button));
        scrimObj.transform.SetParent(rootRect, false);
        RectTransform scrimRect = scrimObj.GetComponent<RectTransform>();
        scrimRect.anchorMin = Vector2.zero;
        scrimRect.anchorMax = Vector2.one;
        scrimRect.offsetMin = Vector2.zero;
        scrimRect.offsetMax = Vector2.zero;

        Image scrimImg = scrimObj.GetComponent<Image>();
        scrimImg.color = GwThemeTokens.BackdropScrim;
        scrimImg.raycastTarget = true;

        guideScrimButton = scrimObj.GetComponent<Button>();
        guideScrimButton.targetGraphic = scrimImg;
        guideScrimButton.transition = Selectable.Transition.None;
        guideScrimButton.onClick.AddListener(OnCloseGuideClicked);

        // Kontainer Safe Area (aman terhadap notch/cutout & gesture bar Android 20:9)
        GameObject safeAreaObj = new GameObject("AirGestureGuide_SafeArea", typeof(RectTransform));
        safeAreaObj.transform.SetParent(rootRect, false);
        guideSafeAreaRect = safeAreaObj.GetComponent<RectTransform>();
        guideSafeAreaRect.anchorMin = Vector2.zero;
        guideSafeAreaRect.anchorMax = Vector2.one;
        guideSafeAreaRect.offsetMin = Vector2.zero;
        guideSafeAreaRect.offsetMax = Vector2.zero;

        // Panel modal tengah: SmokedGlassOverlay (94%) + HairlineActive 1px + radius 6px (nol neon glow)
        GameObject cardObj = new GameObject("Card_AirGestureGuide", typeof(RectTransform), typeof(Image));
        cardObj.transform.SetParent(guideSafeAreaRect, false);
        guideCardRect = cardObj.GetComponent<RectTransform>();
        guideCardRect.anchorMin = new Vector2(0.5f, 0.5f);
        guideCardRect.anchorMax = new Vector2(0.5f, 0.5f);
        guideCardRect.pivot = new Vector2(0.5f, 0.5f);
        guideCardRect.anchoredPosition = Vector2.zero;
        guideCardRect.sizeDelta = new Vector2(GuideCardWidth, GuideCardHeight);

        Image cardBg = cardObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(cardBg, GwThemeTokens.SmokedGlassOverlay);
        cardBg.raycastTarget = true;

        GwThemeTokens.AttachHairlineBorder(guideCardRect, "Card_AirGestureGuide_Border", GwThemeTokens.HairlineActive);

        const float padX = 28f;
        float contentW = GuideCardWidth - (padX * 2f);

        // ---------------------------------------------------------------------
        // HEADER MODAL
        // ---------------------------------------------------------------------
        TMP_Text titleTmp = CreateTopLeftText(
            "Txt_GuideHeader",
            guideCardRect,
            new Vector2(padX, -20f),
            new Vector2(contentW - 52f, 34f),
            "PANDUAN GESTUR MODE UDARA",
            TextAlignmentOptions.MidlineLeft,
            GwThemeTokens.TextPrimary,
            20f,
            25f,
            TextWrappingModes.NoWrap);
        titleTmp.fontStyle = FontStyles.Bold;

        TMP_Text subTmp = CreateTopLeftText(
            "Txt_GuideSubtitle",
            guideCardRect,
            new Vector2(padX, -54f),
            new Vector2(contentW - 52f, 22f),
            "KONTROL SPASIAL 3D TANPA SENTUH LAYAR  //  BUKA ULANG KAPAN SAJA VIA TOMBOL ?",
            TextAlignmentOptions.MidlineLeft,
            GwThemeTokens.TextSecondary,
            12f,
            14f,
            TextWrappingModes.NoWrap);
        subTmp.fontStyle = FontStyles.Normal;

        btnCloseTopX = CreateCardActionButton(
            "Btn_GuideCloseTopX",
            guideCardRect,
            GuideCardWidth - padX - 36f,
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
            OnCloseGuideClicked);

        CreateDivider("Divider_Top", guideCardRect, padX, -88f, contentW, GwThemeTokens.HairlineBorderPx);

        // ---------------------------------------------------------------------
        // KONTEN 2 KOLOM SEJAJAR (KIRI: 3 GESTUR UTAMA | KANAN: 5 POSE SUMMON)
        // ---------------------------------------------------------------------
        const float colsTopY = -106f;
        const float colGap = 28f;
        float leftColW = (contentW - colGap) * 0.485f;
        float rightColW = contentW - colGap - leftColW;
        float rightColX = padX + leftColW + colGap;
        float midDividerX = padX + leftColW + (colGap * 0.5f);

        // Garis pembatas vertikal 1px HairlineIdle antar kolom
        CreateDivider("Divider_CenterCols", guideCardRect, midDividerX, colsTopY, GwThemeTokens.HairlineBorderPx, 450f);

        // --- KOLOM KIRI: 3 GESTUR UTAMA ---
        TMP_Text leftSecTmp = CreateTopLeftText(
            "Txt_LeftColHeader",
            guideCardRect,
            new Vector2(padX, colsTopY),
            new Vector2(leftColW, 24f),
            "GESTUR INTERAKSI UTAMA",
            TextAlignmentOptions.MidlineLeft,
            GwThemeTokens.TextSecondary,
            12f,
            14f,
            TextWrappingModes.NoWrap);
        leftSecTmp.fontStyle = FontStyles.Bold;

        const float mainCardGap = 14f;
        const float mainCardH = 128f;
        float leftCardY = colsTopY - 34f;

        CreateMainGestureCard(
            "Card_GestureFist",
            guideCardRect,
            padX,
            leftCardY,
            leftColW,
            mainCardH,
            "KEPAL (FIST)",
            "SAKELAR & GERBANG",
            "Arahkan ke sakelar kiri lalu kepal untuk membalik nilai 0 <-> 1. Kepal badan gerbang untuk memindahkan; tarik keluar batas zona rakit lalu lepas untuk menghapus.");
        leftCardY -= (mainCardH + mainCardGap);

        CreateMainGestureCard(
            "Card_GesturePinch",
            guideCardRect,
            padX,
            leftCardY,
            leftColW,
            mainCardH,
            "CUBIT (PINCH)",
            "KABEL MANUAL",
            "Cubit Jempol + Telunjuk pada orb port untuk menarik dan mencolok kabel antar terminal. Lepas cubitan di ruang kosong untuk memutus kabel.");
        leftCardY -= (mainCardH + mainCardGap);

        CreateMainGestureCard(
            "Card_GestureTwoHand",
            guideCardRect,
            padX,
            leftCardY,
            leftColW,
            mainCardH,
            "DUA TANGAN",
            "ORBIT, ZOOM & PAN",
            "2 Cubit untuk orbit 360 deg dan zoom panggung meja. 1 Cubit + 1 Kepal untuk geser bebas. Tahan 2 Kepal sejenak untuk memusatkan ulang kamera.");

        // --- KOLOM KANAN: GRID 5 KARTU POSE SUMMON + CATATAN ALUR 2-TAHAP ---
        TMP_Text rightSecTmp = CreateTopLeftText(
            "Txt_RightColHeader",
            guideCardRect,
            new Vector2(rightColX, colsTopY),
            new Vector2(rightColW, 24f),
            "POSE SUMMON GERBANG (5 GERBANG)",
            TextAlignmentOptions.MidlineLeft,
            GwThemeTokens.TextSecondary,
            12f,
            14f,
            TextWrappingModes.NoWrap);
        rightSecTmp.fontStyle = FontStyles.Bold;

        const float poseGap = 12f;
        const float poseRowH = 132f;
        float poseTopY = colsTopY - 34f;

        // Baris 1 (3 kartu sejajar): AND (1 Telunjuk), OR (2 Jari V), NOT (Jempol Atas)
        float topRowCardW = (rightColW - (poseGap * 2f)) / 3f;
        CreateSummonPoseCard(
            "Card_PoseAnd",
            guideCardRect,
            rightColX,
            poseTopY,
            topRowCardW,
            poseRowH,
            "AND",
            "1 Telunjuk",
            "Y = A AND B");

        CreateSummonPoseCard(
            "Card_PoseOr",
            guideCardRect,
            rightColX + topRowCardW + poseGap,
            poseTopY,
            topRowCardW,
            poseRowH,
            "OR",
            "2 Jari V",
            "Y = A OR B");

        CreateSummonPoseCard(
            "Card_PoseNot",
            guideCardRect,
            rightColX + ((topRowCardW + poseGap) * 2f),
            poseTopY,
            topRowCardW,
            poseRowH,
            "NOT",
            "Jempol Atas",
            "Y = NOT A");

        // Baris 2 (2 kartu sejajar): XOR (3 Jari), NAND (4 Jari) - selaras 1:1 AirWeavingHandGestureDetector & GDD
        float botRowY = poseTopY - (poseRowH + poseGap);
        float botRowCardW = (rightColW - poseGap) * 0.5f;
        CreateSummonPoseCard(
            "Card_PoseXor",
            guideCardRect,
            rightColX,
            botRowY,
            botRowCardW,
            poseRowH,
            "XOR",
            "3 Jari",
            "Y = A XOR B");

        CreateSummonPoseCard(
            "Card_PoseNand",
            guideCardRect,
            rightColX + botRowCardW + poseGap,
            botRowY,
            botRowCardW,
            poseRowH,
            "NAND",
            "4 Jari",
            "Y = A NAND B");

        // Kotak catatan alur 2-tahap (Circle-Charge -> Open-5)
        float noteY = botRowY - (poseRowH + poseGap);
        const float noteH = 122f;
        CreateTwoStageFlowNoteCard(guideCardRect, rightColX, noteY, rightColW, noteH);

        // ---------------------------------------------------------------------
        // FOOTER: LATIHAN CEPAT (SEKUNDER) + PAHAM, KEMBALI KE SIMULASI (PRIMER SOLID PUTIH)
        // ---------------------------------------------------------------------
        CreateDivider("Divider_Bottom", guideCardRect, padX, -574f, contentW, GwThemeTokens.HairlineBorderPx);

        const float footerBtnY = -594f;
        const float footerBtnH = 52f;
        const float practiceBtnW = 340f;
        const float primaryCloseBtnW = 420f;

        btnStartPractice = CreateCardActionButton(
            "Btn_StartQuickPractice",
            guideCardRect,
            padX,
            footerBtnY,
            practiceBtnW,
            footerBtnH,
            "LATIHAN CEPAT (4 LANGKAH)",
            GwThemeTokens.CardIdle,
            GwThemeTokens.HairlineIdle,
            GwThemeTokens.TextPrimary,
            14f,
            17f,
            hasBorder: true,
            OnStartPracticeClicked);

        btnCloseGuide = CreateCardActionButton(
            "Btn_CloseGestureGuide",
            guideCardRect,
            GuideCardWidth - padX - primaryCloseBtnW,
            footerBtnY,
            primaryCloseBtnW,
            footerBtnH,
            "PAHAM, KEMBALI KE SIMULASI",
            GwThemeTokens.PrimaryFill,
            GwThemeTokens.PrimaryFill,
            GwThemeTokens.PrimaryText,
            15f,
            18.5f,
            hasBorder: false,
            OnCloseGuideClicked);

        guideRootObj.SetActive(false);
    }

    private void CreateMainGestureCard(
        string name,
        RectTransform parent,
        float leftX,
        float topY,
        float width,
        float height,
        string gestureTitle,
        string roleBadge,
        string description)
    {
        GameObject cardObj = new GameObject(name, typeof(RectTransform), typeof(Image));
        cardObj.transform.SetParent(parent, false);
        RectTransform rect = cardObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(leftX, topY);
        rect.sizeDelta = new Vector2(width, height);

        Image bg = cardObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(bg, GwThemeTokens.CardIdle);
        bg.raycastTarget = false;

        GwThemeTokens.AttachHairlineBorder(rect, $"{name}_Border", GwThemeTokens.HairlineIdle);

        // Baris atas kartu: Judul Gestur (kiri) + Badge Peran (kanan)
        TMP_Text titleTmp = CreateTopLeftText(
            "Txt_Title",
            rect,
            new Vector2(18f, -14f),
            new Vector2(width - 210f, 28f),
            gestureTitle,
            TextAlignmentOptions.MidlineLeft,
            GwThemeTokens.TextPrimary,
            16f,
            20f,
            TextWrappingModes.NoWrap);
        titleTmp.fontStyle = FontStyles.Bold;

        GameObject badgeObj = new GameObject("Badge_Role", typeof(RectTransform), typeof(Image));
        badgeObj.transform.SetParent(rect, false);
        RectTransform badgeRect = badgeObj.GetComponent<RectTransform>();
        badgeRect.anchorMin = new Vector2(1f, 1f);
        badgeRect.anchorMax = new Vector2(1f, 1f);
        badgeRect.pivot = new Vector2(1f, 1f);
        badgeRect.anchoredPosition = new Vector2(-16f, -14f);
        badgeRect.sizeDelta = new Vector2(174f, 26f);

        Image badgeBg = badgeObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(badgeBg, GwThemeTokens.SmokedGlass);
        badgeBg.raycastTarget = false;
        GwThemeTokens.AttachHairlineBorder(badgeRect, "Badge_Role_Border", GwThemeTokens.HairlineIdle);

        TMP_Text badgeTxt = CreateStretchedText(
            "Txt_Role",
            badgeRect,
            roleBadge,
            TextAlignmentOptions.Center,
            GwThemeTokens.SignalHighTeal,
            10.5f,
            12.5f,
            TextWrappingModes.NoWrap);
        badgeTxt.fontStyle = FontStyles.Bold;

        // Deskripsi rapi multi-baris
        TMP_Text descTmp = CreateTopLeftText(
            "Txt_Desc",
            rect,
            new Vector2(18f, -48f),
            new Vector2(width - 36f, height - 60f),
            description,
            TextAlignmentOptions.TopLeft,
            GwThemeTokens.TextSecondary,
            13.5f,
            16f,
            TextWrappingModes.Normal);
        descTmp.fontStyle = FontStyles.Normal;
    }

    private void CreateSummonPoseCard(
        string name,
        RectTransform parent,
        float leftX,
        float topY,
        float width,
        float height,
        string gateName,
        string fingerPoseLabel,
        string formulaHint)
    {
        GameObject cardObj = new GameObject(name, typeof(RectTransform), typeof(Image));
        cardObj.transform.SetParent(parent, false);
        RectTransform rect = cardObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(leftX, topY);
        rect.sizeDelta = new Vector2(width, height);

        Image bg = cardObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(bg, GwThemeTokens.CardIdle);
        bg.raycastTarget = false;

        GwThemeTokens.AttachHairlineBorder(rect, $"{name}_Border", GwThemeTokens.HairlineIdle);

        // Strip aksen tipis SignalHighTeal (#38BDF8) di tepi atas dalam kartu
        GameObject accentObj = new GameObject("TopAccentBar", typeof(RectTransform), typeof(Image));
        accentObj.transform.SetParent(rect, false);
        RectTransform accentRect = accentObj.GetComponent<RectTransform>();
        accentRect.anchorMin = new Vector2(0f, 1f);
        accentRect.anchorMax = new Vector2(1f, 1f);
        accentRect.pivot = new Vector2(0.5f, 1f);
        accentRect.offsetMin = new Vector2(14f, -3f);
        accentRect.offsetMax = new Vector2(-14f, -1f);
        Image accentImg = accentObj.GetComponent<Image>();
        accentImg.color = GwThemeTokens.SignalHighTeal;
        accentImg.raycastTarget = false;

        // Nama gerbang besar dengan aksen SignalHighTeal (#38BDF8)
        TMP_Text gateTmp = CreateTopLeftText(
            "Txt_GateName",
            rect,
            new Vector2(14f, -16f),
            new Vector2(width - 28f, 36f),
            gateName,
            TextAlignmentOptions.Center,
            GwThemeTokens.SignalHighTeal,
            20f,
            26f,
            TextWrappingModes.NoWrap);
        gateTmp.fontStyle = FontStyles.Bold;

        // Pil pose jari (1 Telunjuk, 2 Jari V, Jempol Atas, 3 Jari, 4 Jari)
        GameObject posePillObj = new GameObject("Pill_FingerPose", typeof(RectTransform), typeof(Image));
        posePillObj.transform.SetParent(rect, false);
        RectTransform pillRect = posePillObj.GetComponent<RectTransform>();
        pillRect.anchorMin = new Vector2(0f, 1f);
        pillRect.anchorMax = new Vector2(1f, 1f);
        pillRect.pivot = new Vector2(0.5f, 1f);
        pillRect.offsetMin = new Vector2(14f, -90f);
        pillRect.offsetMax = new Vector2(-14f, -58f);

        Image pillBg = posePillObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(pillBg, GwThemeTokens.SmokedGlass);
        pillBg.raycastTarget = false;
        GwThemeTokens.AttachHairlineBorder(pillRect, "Pill_FingerPose_Border", GwThemeTokens.SignalHighTeal);

        TMP_Text poseTmp = CreateStretchedText(
            "Txt_FingerPose",
            pillRect,
            fingerPoseLabel,
            TextAlignmentOptions.Center,
            GwThemeTokens.TextPrimary,
            13f,
            16f,
            TextWrappingModes.NoWrap);
        poseTmp.fontStyle = FontStyles.Bold;

        // Rumus singkat gerbang
        TMP_Text hintTmp = CreateTopLeftText(
            "Txt_FormulaHint",
            rect,
            new Vector2(12f, -98f),
            new Vector2(width - 24f, 22f),
            formulaHint,
            TextAlignmentOptions.Center,
            GwThemeTokens.TextSecondary,
            11.5f,
            13.5f,
            TextWrappingModes.NoWrap);
        hintTmp.fontStyle = FontStyles.Normal;
    }

    private void CreateTwoStageFlowNoteCard(RectTransform parent, float leftX, float topY, float width, float height)
    {
        GameObject noteObj = new GameObject("Card_TwoStageSummonFlow", typeof(RectTransform), typeof(Image));
        noteObj.transform.SetParent(parent, false);
        RectTransform rect = noteObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(leftX, topY);
        rect.sizeDelta = new Vector2(width, height);

        Image bg = noteObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(bg, GwThemeTokens.SmokedGlass);
        bg.raycastTarget = false;

        GwThemeTokens.AttachHairlineBorder(rect, "Card_TwoStageSummonFlow_Border", GwThemeTokens.SignalHighTeal);

        TMP_Text headerTmp = CreateTopLeftText(
            "Txt_FlowHeader",
            rect,
            new Vector2(16f, -12f),
            new Vector2(width - 32f, 24f),
            "ALUR 2-TAHAP: CIRCLE-CHARGE -> OPEN-5",
            TextAlignmentOptions.MidlineLeft,
            GwThemeTokens.SignalHighTeal,
            13f,
            15.5f,
            TextWrappingModes.NoWrap);
        headerTmp.fontStyle = FontStyles.Bold;

        TMP_Text bodyTmp = CreateTopLeftText(
            "Txt_FlowBody",
            rect,
            new Vector2(16f, -40f),
            new Vector2(width - 32f, height - 50f),
            "1. Arahkan pose jari di area tengah lalu putar melingkar hingga cincin penuh (Circle-Charge).\n2. Buka lebar 5 jari (Open-5) untuk menempatkan gerbang; kepal (Fist) untuk membatalkan.",
            TextAlignmentOptions.TopLeft,
            GwThemeTokens.TextPrimary,
            12.5f,
            15f,
            TextWrappingModes.Normal);
        bodyTmp.fontStyle = FontStyles.Normal;
    }

    private void BuildPracticePillUi(Transform canvasTransform)
    {
        practiceRootObj = new GameObject("Panel_AirQuickPracticeRoot", typeof(RectTransform));
        practiceRootObj.transform.SetParent(canvasTransform, false);
        RectTransform rootRect = practiceRootObj.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        GameObject safeAreaObj = new GameObject("AirQuickPractice_SafeArea", typeof(RectTransform));
        safeAreaObj.transform.SetParent(rootRect, false);
        practiceSafeAreaRect = safeAreaObj.GetComponent<RectTransform>();
        practiceSafeAreaRect.anchorMin = Vector2.zero;
        practiceSafeAreaRect.anchorMax = Vector2.one;
        practiceSafeAreaRect.offsetMin = Vector2.zero;
        practiceSafeAreaRect.offsetMax = Vector2.zero;

        // Pil Latihan Cepat v8.5: SmokedGlassDense + HairlineActive 1px + radius 6px (nol neon glow)
        practicePillObj = new GameObject("Pill_AirQuickPractice", typeof(RectTransform), typeof(Image));
        practicePillObj.transform.SetParent(practiceSafeAreaRect, false);
        RectTransform pillRect = practicePillObj.GetComponent<RectTransform>();
        pillRect.anchorMin = new Vector2(0.5f, 0f);
        pillRect.anchorMax = new Vector2(0.5f, 0f);
        pillRect.pivot = new Vector2(0.5f, 0f);
        pillRect.anchoredPosition = new Vector2(0f, 14f);
        pillRect.sizeDelta = new Vector2(PracticePillWidth, PracticePillHeight);

        practicePillBg = practicePillObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(practicePillBg, GwThemeTokens.SmokedGlassDense);
        practicePillBg.raycastTarget = false;

        practicePillBorderImg = GwThemeTokens.AttachHairlineBorder(
            pillRect,
            "Pill_AirQuickPractice_Border",
            GwThemeTokens.HairlineActive);

        // Lencana langkah 1/4..4/4
        GameObject badgeObj = new GameObject("Practice_StepBadge", typeof(RectTransform), typeof(Image));
        badgeObj.transform.SetParent(pillRect, false);
        RectTransform badgeRect = badgeObj.GetComponent<RectTransform>();
        badgeRect.anchorMin = new Vector2(0f, 0.5f);
        badgeRect.anchorMax = new Vector2(0f, 0.5f);
        badgeRect.pivot = new Vector2(0f, 0.5f);
        badgeRect.anchoredPosition = new Vector2(12f, 0f);
        badgeRect.sizeDelta = new Vector2(82f, 38f);

        practiceBadgeBg = badgeObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(practiceBadgeBg, GwThemeTokens.CardIdle);
        practiceBadgeBg.raycastTarget = false;

        practiceBadgeBorderImg = GwThemeTokens.AttachHairlineBorder(
            badgeRect,
            "Practice_StepBadge_Border",
            GwThemeTokens.SignalHighTeal);

        txtPracticeBadge = CreateStretchedText(
            "Txt_StepBadge",
            badgeRect,
            BadgeStep1,
            TextAlignmentOptions.Center,
            GwThemeTokens.SignalHighTeal,
            15f,
            20f,
            TextWrappingModes.NoWrap);
        txtPracticeBadge.fontStyle = FontStyles.Bold;

        // Teks instruksi langkah aktif
        GameObject promptObj = new GameObject("Txt_PracticePrompt", typeof(RectTransform), typeof(TextMeshProUGUI));
        promptObj.transform.SetParent(pillRect, false);
        practicePromptRect = promptObj.GetComponent<RectTransform>();
        practicePromptRect.anchorMin = new Vector2(0f, 0f);
        practicePromptRect.anchorMax = new Vector2(1f, 1f);
        practicePromptRect.offsetMin = new Vector2(108f, 6f);
        practicePromptRect.offsetMax = new Vector2(-124f, -6f);

        TextMeshProUGUI promptTmp = promptObj.GetComponent<TextMeshProUGUI>();
        if (safeFont != null)
        {
            promptTmp.font = safeFont;
        }
        promptTmp.text = Step1PromptText;
        promptTmp.fontStyle = FontStyles.Bold;
        promptTmp.alignment = TextAlignmentOptions.MidlineLeft;
        promptTmp.color = GwThemeTokens.TextPrimary;
        promptTmp.enableAutoSizing = true;
        promptTmp.fontSizeMin = 15.5f;
        promptTmp.fontSizeMax = 21.5f;
        promptTmp.textWrappingMode = TextWrappingModes.NoWrap;
        promptTmp.raycastTarget = false;
        txtPracticePrompt = promptTmp;

        // Tombol 'LEWATI' di ujung kanan pil
        skipPracticeBtnObj = new GameObject("Btn_SkipQuickPractice", typeof(RectTransform), typeof(Image), typeof(Button));
        skipPracticeBtnObj.transform.SetParent(pillRect, false);
        RectTransform skipRect = skipPracticeBtnObj.GetComponent<RectTransform>();
        skipRect.anchorMin = new Vector2(1f, 0.5f);
        skipRect.anchorMax = new Vector2(1f, 0.5f);
        skipRect.pivot = new Vector2(1f, 0.5f);
        skipRect.anchoredPosition = new Vector2(-12f, 0f);
        skipRect.sizeDelta = new Vector2(98f, 38f);

        Image skipBg = skipPracticeBtnObj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(skipBg, GwThemeTokens.CardIdle);
        skipBg.raycastTarget = true;

        GwThemeTokens.AttachHairlineBorder(skipRect, "Btn_SkipQuickPractice_Border", GwThemeTokens.HairlineIdle);

        btnSkipPractice = skipPracticeBtnObj.GetComponent<Button>();
        btnSkipPractice.targetGraphic = skipBg;
        btnSkipPractice.transition = Selectable.Transition.ColorTint;
        btnSkipPractice.colors = GwThemeTokens.FlatGlassButtonColors;
        btnSkipPractice.onClick.AddListener(OnSkipPracticeClicked);

        TMP_Text skipTmp = CreateStretchedText(
            "Txt_SkipLabel",
            skipRect,
            "LEWATI",
            TextAlignmentOptions.Center,
            GwThemeTokens.TextSecondary,
            13f,
            16f,
            TextWrappingModes.NoWrap);
        skipTmp.fontStyle = FontStyles.Bold;

        practiceRootObj.SetActive(false);
    }

    private void RefreshPracticePillVisuals(bool isStepFlashSuccess)
    {
        if (!isUiBuilt)
        {
            return;
        }

        bool isCompleted = practiceState == PracticeState.CompletedBanner;
        bool highlightGreen = isStepFlashSuccess || isCompleted;

        if (practicePillBg != null)
        {
            practicePillBg.color = GwThemeTokens.SmokedGlassDense;
        }
        if (practicePillBorderImg != null)
        {
            practicePillBorderImg.color = highlightGreen
                ? GwThemeTokens.StatusPassEmerald
                : GwThemeTokens.HairlineActive;
        }
        if (practiceBadgeBg != null)
        {
            practiceBadgeBg.color = GwThemeTokens.CardIdle;
        }
        if (practiceBadgeBorderImg != null)
        {
            practiceBadgeBorderImg.color = highlightGreen
                ? GwThemeTokens.StatusPassEmerald
                : GwThemeTokens.SignalHighTeal;
        }
        if (txtPracticeBadge != null)
        {
            txtPracticeBadge.color = highlightGreen
                ? GwThemeTokens.StatusPassText
                : GwThemeTokens.SignalHighTeal;
            if (isStepFlashSuccess && !isCompleted)
            {
                txtPracticeBadge.text = BadgeStepOk;
            }
            else
            {
                switch (practiceState)
                {
                    case PracticeState.Step1ToggleInput: txtPracticeBadge.text = BadgeStep1; break;
                    case PracticeState.Step2SummonGate: txtPracticeBadge.text = BadgeStep2; break;
                    case PracticeState.Step3ConnectCable: txtPracticeBadge.text = BadgeStep3; break;
                    case PracticeState.Step4DeleteGate: txtPracticeBadge.text = BadgeStep4; break;
                    case PracticeState.CompletedBanner: txtPracticeBadge.text = BadgeStep4; break;
                    default: txtPracticeBadge.text = BadgeStep1; break;
                }
            }
        }

        if (skipPracticeBtnObj != null && skipPracticeBtnObj.activeSelf == isCompleted)
        {
            skipPracticeBtnObj.SetActive(!isCompleted);
        }

        if (practicePromptRect != null)
        {
            practicePromptRect.offsetMin = new Vector2(108f, 6f);
            practicePromptRect.offsetMax = new Vector2(isCompleted ? -24f : -124f, -6f);
        }

        if (txtPracticePrompt != null)
        {
            if (isCompleted)
            {
                txtPracticePrompt.text = CompletedBannerText;
                txtPracticePrompt.alignment = TextAlignmentOptions.Center;
                txtPracticePrompt.color = GwThemeTokens.StatusPassText;
            }
            else
            {
                txtPracticePrompt.alignment = TextAlignmentOptions.MidlineLeft;
                txtPracticePrompt.color = highlightGreen ? GwThemeTokens.StatusPassText : GwThemeTokens.TextPrimary;
                switch (practiceState)
                {
                    case PracticeState.Step1ToggleInput: txtPracticePrompt.text = Step1PromptText; break;
                    case PracticeState.Step2SummonGate: txtPracticePrompt.text = Step2PromptText; break;
                    case PracticeState.Step3ConnectCable: txtPracticePrompt.text = Step3PromptText; break;
                    case PracticeState.Step4DeleteGate: txtPracticePrompt.text = Step4PromptText; break;
                    default: txtPracticePrompt.text = Step1PromptText; break;
                }
            }
        }
    }

    private void ApplySafeAreaAndResponsiveScaleIfNeeded(bool force)
    {
        Rect safeArea = Screen.safeArea;
        int sw = Screen.width;
        int sh = Screen.height;
        bool screenUnchanged = sw == lastScreenWidth && sh == lastScreenHeight && safeArea == lastAppliedSafeArea;
        if (!force && screenUnchanged && (!isGuideOverlayOpen || hasAppliedCardScale))
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
        Vector2 maxAnchor = new Vector2(Mathf.Clamp01(safeArea.xMax / denomW), Mathf.Clamp01(safeArea.yMax / denomH));

        if (guideSafeAreaRect != null)
        {
            guideSafeAreaRect.anchorMin = minAnchor;
            guideSafeAreaRect.anchorMax = maxAnchor;
            guideSafeAreaRect.offsetMin = Vector2.zero;
            guideSafeAreaRect.offsetMax = Vector2.zero;

            if (guideCardRect != null)
            {
                Rect avail = guideSafeAreaRect.rect;
                if (avail.width > 120f && avail.height > 120f)
                {
                    float scaleX = (avail.width - 36f) / GuideCardWidth;
                    float scaleY = (avail.height - 28f) / GuideCardHeight;
                    float fitScale = Mathf.Clamp(Mathf.Min(scaleX, scaleY), 0.65f, 1.0f);
                    guideCardRect.localScale = new Vector3(fitScale, fitScale, 1f);
                    hasAppliedCardScale = true;
                }
            }
        }

        if (practiceSafeAreaRect != null)
        {
            practiceSafeAreaRect.anchorMin = minAnchor;
            practiceSafeAreaRect.anchorMax = maxAnchor;
            practiceSafeAreaRect.offsetMin = Vector2.zero;
            practiceSafeAreaRect.offsetMax = Vector2.zero;
        }
    }

    // =========================================================================
    // HELPER UI PROSEDURAL 100% TOKEN GwThemeTokens
    // =========================================================================

    private Button CreateCardActionButton(
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
        float minFontSize,
        float maxFontSize,
        bool hasBorder,
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

        Image bgImg = obj.GetComponent<Image>();
        GwThemeTokens.ApplyRoundedRect6(bgImg, bgColor);
        bgImg.raycastTarget = true;

        if (hasBorder)
        {
            GwThemeTokens.AttachHairlineBorder(rect, $"{name}_Border", borderColor);
        }

        Button btn = obj.GetComponent<Button>();
        btn.targetGraphic = bgImg;
        btn.transition = Selectable.Transition.ColorTint;
        btn.colors = GwThemeTokens.FlatGlassButtonColors;
        if (onClick != null)
        {
            btn.onClick.AddListener(onClick);
        }

        TMP_Text labelTmp = CreateStretchedText(
            $"{name}_Label",
            rect,
            label,
            TextAlignmentOptions.Center,
            textColor,
            minFontSize,
            maxFontSize,
            TextWrappingModes.NoWrap);
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
        rect.offsetMin = new Vector2(10f, 3f);
        rect.offsetMax = new Vector2(-10f, -3f);

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
        img.color = GwThemeTokens.HairlineIdle;
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
