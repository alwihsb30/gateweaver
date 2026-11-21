using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Panduan Gestur Interaktif 1 Halaman & Latihan Cepat 4 Langkah khusus Mode Udara (GateWeaver AR v7.5 Sprint 2).
/// - 100% Code-First & Prosedural (0 aset/prefab eksternal, 0% regresi Mode Kartu v5).
/// - Desain kartu hologram gelap kontras tinggi selaras CompactHudMenuController & aman terhadap Safe Area Android 20:9.
/// - Tampil otomatis 1 kali saja saat pertama kali masuk Mode Udara (PlayerPrefs: GW_AirGuideSeen_v75),
///   dapat dibuka ulang kapan saja lewat tombol '?' di Bilah Atas atau opsi 'Panduan Gestur' di laci menu.
/// - Mode Latihan Cepat 4 langkah non-intrusif di tengah-bawah layar (pil ramping di bawah AssemblyZone, bebas tabrakan
///   dengan sakelar Input B kiri, OutputTruthPod kanan, maupun toast Txt_Feedback) yang melacak aksi tangan nyata Zero-GC.
/// </summary>
public sealed class AirWeavingGestureGuide : MonoBehaviour
{
    private const string PrefKeyGuideSeen = "GW_AirGuideSeen_v75";
    private const float TopBarHeight = 72f;
    private const float GuideCardWidth = 1120f;
    private const float GuideCardHeight = 548f;
    private const float PracticePillWidth = 688f;
    private const float PracticePillHeight = 50f;
    private const float StepCompleteBeatSeconds = 0.42f;
    private const float CompletionBannerSeconds = 1.60f;
    private const float AudioReferenceDepth = 3.60f;

    private static readonly Color ScrimBgColor = new Color(0.01f, 0.03f, 0.07f, 0.68f);
    private static readonly Color CardBgColor = new Color(0.03f, 0.07f, 0.13f, 0.96f);
    private static readonly Color CardBorderCyanColor = new Color(0.18f, 0.88f, 1.00f, 0.90f);
    private static readonly Color CardDividerColor = new Color(0.20f, 0.68f, 0.86f, 0.45f);
    private static readonly Color SubtitleTextColor = new Color(0.72f, 0.88f, 0.96f, 0.95f);
    private static readonly Color RowBgColor = new Color(0.07f, 0.13f, 0.22f, 0.90f);
    private static readonly Color RowTextColor = new Color(0.92f, 0.97f, 1.00f, 1.00f);

    private static readonly Color TagIndexBgColor = new Color(0.06f, 0.30f, 0.24f, 0.96f);
    private static readonly Color TagIndexAccentColor = new Color(0.36f, 1.00f, 0.72f, 1.00f);
    private static readonly Color TagSummonBgColor = new Color(0.06f, 0.28f, 0.40f, 0.96f);
    private static readonly Color TagSummonAccentColor = new Color(0.30f, 0.96f, 1.00f, 1.00f);
    private static readonly Color TagPinchBgColor = new Color(0.17f, 0.29f, 0.09f, 0.96f);
    private static readonly Color TagPinchAccentColor = new Color(0.76f, 1.00f, 0.34f, 1.00f);
    private static readonly Color TagFistBgColor = new Color(0.34f, 0.18f, 0.08f, 0.96f);
    private static readonly Color TagFistAccentColor = new Color(1.00f, 0.74f, 0.32f, 1.00f);
    private static readonly Color TagTestBgColor = new Color(0.07f, 0.31f, 0.16f, 0.96f);
    private static readonly Color TagTestAccentColor = new Color(0.42f, 1.00f, 0.56f, 1.00f);

    private static readonly Color PrimaryBtnBgColor = new Color(0.08f, 0.44f, 0.58f, 0.98f);
    private static readonly Color PrimaryBtnOutlineColor = new Color(0.28f, 0.94f, 1.00f, 1.00f);
    private static readonly Color NeutralBtnBgColor = new Color(0.11f, 0.18f, 0.27f, 0.96f);
    private static readonly Color NeutralBtnOutlineColor = new Color(0.36f, 0.56f, 0.72f, 0.85f);

    private static readonly Color PillBgIdleColor = new Color(0.03f, 0.08f, 0.15f, 0.93f);
    private static readonly Color PillOutlineIdleColor = new Color(0.20f, 0.88f, 1.00f, 0.90f);
    private static readonly Color PillBgSuccessColor = new Color(0.05f, 0.28f, 0.16f, 0.96f);
    private static readonly Color PillOutlineSuccessColor = new Color(0.28f, 1.00f, 0.62f, 1.00f);
    private static readonly Color BadgeBgIdleColor = new Color(0.08f, 0.30f, 0.44f, 0.98f);
    private static readonly Color BadgeTextIdleColor = new Color(0.76f, 0.98f, 1.00f, 1.00f);
    private static readonly Color BadgeBgSuccessColor = new Color(0.10f, 0.48f, 0.28f, 0.98f);
    private static readonly Color BadgeTextSuccessColor = new Color(0.94f, 1.00f, 0.96f, 1.00f);
    private static readonly Color SkipBtnBgColor = new Color(0.12f, 0.19f, 0.29f, 0.94f);
    private static readonly Color SkipBtnOutlineColor = new Color(0.38f, 0.58f, 0.74f, 0.80f);
    private static readonly Color SkipBtnTextColor = new Color(0.84f, 0.93f, 0.99f, 1.00f);
    private static readonly Color PracticePromptColor = new Color(0.94f, 0.98f, 1.00f, 1.00f);
    private static readonly Color PracticeCompleteTextColor = new Color(0.42f, 1.00f, 0.64f, 1.00f);

    private static readonly ColorBlock GuideButtonColorBlock = new ColorBlock
    {
        normalColor = Color.white,
        highlightedColor = new Color(0.90f, 0.97f, 1.00f, 1f),
        pressedColor = new Color(0.72f, 0.82f, 0.90f, 1f),
        selectedColor = Color.white,
        disabledColor = new Color(0.45f, 0.48f, 0.52f, 0.55f),
        colorMultiplier = 1f,
        fadeDuration = 0.08f
    };

    private enum PracticeState
    {
        Inactive = 0,
        Step1ToggleInput = 1,
        Step2SummonGate = 2,
        Step3ConnectCable = 3,
        Step4DeleteGate = 4,
        CompletedBanner = 5
    }

    private CompactHudMenuController compactHudMenu;
    private AirWeavingSpatialSfx spatialSfx;
    private Camera referenceCamera;
    private Canvas targetCanvas;
    private TMP_FontAsset safeFont;

    // Overlay Panduan 1 Halaman
    private GameObject guideRootObj;
    private Button guideScrimButton;
    private RectTransform guideSafeAreaRect;
    private RectTransform guideCardRect;
    private Button btnStartPractice;
    private Button btnCloseGuide;

    // Pil Latihan Cepat 4 Langkah
    private GameObject practiceRootObj;
    private RectTransform practiceSafeAreaRect;
    private GameObject practicePillObj;
    private Image practicePillBg;
    private Outline practicePillOutline;
    private Image practiceBadgeBg;
    private TMP_Text txtPracticeBadge;
    private RectTransform practicePromptRect;
    private TMP_Text txtPracticePrompt;
    private GameObject skipPracticeBtnObj;
    private Button btnSkipPractice;

    // Pra-cache string Zero-GC (bebas alokasi saat runtime)
    private string step1PromptText;
    private string step2PromptText;
    private string step3PromptText;
    private string step4PromptText;
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
        compactHudMenu?.CloseMenuDrawer();
        compactHudMenu?.ClearToastImmediate();
        spatialSfx?.StopHumImmediate();

        isGuideOverlayOpen = true;
        guideRootObj.transform.SetAsLastSibling();
        guideRootObj.SetActive(true);
        ApplySafeAreaAndResponsiveScaleIfNeeded(force: true);
        compactHudMenu?.SetGuideButtonActiveState(true);
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
        compactHudMenu?.SetGuideButtonActiveState(false);
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
        compactHudMenu?.CloseMenuDrawer();

        practiceState = PracticeState.Step1ToggleInput;
        pendingNextState = PracticeState.Inactive;
        stepCompleteBeatTimer = 0f;
        completionBannerTimer = 0f;

        practiceRootObj.transform.SetAsLastSibling();
        practiceRootObj.SetActive(true);
        ApplySafeAreaAndResponsiveScaleIfNeeded(force: true);
        RefreshPracticePillVisuals(isStepFlashSuccess: false);
        compactHudMenu?.SetPracticePillBottomOffset(true);
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
        compactHudMenu?.SetPracticePillBottomOffset(false);
    }

    public void CleanupImmediate()
    {
        UnsubscribeHudEvents();
        RemoveBtnListener(guideScrimButton, OnCloseGuideClicked);
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
        btnStartPractice = null;
        btnCloseGuide = null;
        practiceSafeAreaRect = null;
        practicePillObj = null;
        practicePillBg = null;
        practicePillOutline = null;
        practiceBadgeBg = null;
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
    // PEMBANGUNAN UI PROSEDURAL ZERO-GC (OVERLAY PANDUAN + PIL LATIHAN CEPAT)
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

        string arrowGlyph = SupportsGlyph('\u2194') ? "\u2194" : "<->";
        string dotGlyph = SupportsGlyph('\u00B7') ? "\u00B7" : "|";
        string dashGlyph = SupportsGlyph('\u2013') ? "\u2013" : "-";

        step1PromptText = "Sentuh sakelar kiri dengan ujung telunjuk";
        step2PromptText = $"Putar 1{dashGlyph}4 jari atau jempol, lalu buka 5 jari";
        step3PromptText = "Cubit orb port lalu sambungkan 1 kabel";
        step4PromptText = "Kepal gerbang lalu buang keluar 4 siku";

        BuildGuideOverlayUi(targetCanvas.transform, arrowGlyph, dotGlyph, dashGlyph);
        BuildPracticePillUi(targetCanvas.transform);

        isUiBuilt = true;
        ApplySafeAreaAndResponsiveScaleIfNeeded(force: true);
    }

    private void BuildGuideOverlayUi(Transform canvasTransform, string arrowGlyph, string dotGlyph, string dashGlyph)
    {
        // Root berada tepat di bawah Bilah Atas 72 px agar tombol '?' & menu '=' selalu responsif dalam 1 ketukan
        guideRootObj = new GameObject("Panel_AirGestureGuideRoot", typeof(RectTransform));
        guideRootObj.transform.SetParent(canvasTransform, false);
        RectTransform rootRect = guideRootObj.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = new Vector2(0f, -TopBarHeight);

        // Scrim gelap semi-transparan klik-luar
        GameObject scrimObj = new GameObject("Btn_AirGestureGuideScrim", typeof(RectTransform), typeof(Image), typeof(Button));
        scrimObj.transform.SetParent(rootRect, false);
        RectTransform scrimRect = scrimObj.GetComponent<RectTransform>();
        scrimRect.anchorMin = Vector2.zero;
        scrimRect.anchorMax = Vector2.one;
        scrimRect.offsetMin = Vector2.zero;
        scrimRect.offsetMax = Vector2.zero;

        Image scrimImg = scrimObj.GetComponent<Image>();
        scrimImg.color = ScrimBgColor;
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

        // Kartu Hologram Gelap Kontras Tinggi
        GameObject cardObj = new GameObject("Card_AirGestureGuide", typeof(RectTransform), typeof(Image));
        cardObj.transform.SetParent(guideSafeAreaRect, false);
        guideCardRect = cardObj.GetComponent<RectTransform>();
        guideCardRect.anchorMin = new Vector2(0.5f, 0.5f);
        guideCardRect.anchorMax = new Vector2(0.5f, 0.5f);
        guideCardRect.pivot = new Vector2(0.5f, 0.5f);
        guideCardRect.anchoredPosition = Vector2.zero;
        guideCardRect.sizeDelta = new Vector2(GuideCardWidth, GuideCardHeight);

        Image cardBg = cardObj.GetComponent<Image>();
        cardBg.color = CardBgColor;
        cardBg.raycastTarget = true;

        CreateBorderStrip("Border_Top", guideCardRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 4f), CardBorderCyanColor);
        CreateBorderStrip("Border_Bottom", guideCardRect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 2.5f), CardBorderCyanColor);
        CreateBorderStrip("Border_Left", guideCardRect, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(2.5f, 0f), CardBorderCyanColor);
        CreateBorderStrip("Border_Right", guideCardRect, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(2.5f, 0f), CardBorderCyanColor);

        // Header Kartu
        TMP_Text titleTmp = CreateTopLeftText(
            "Txt_GuideHeader",
            guideCardRect,
            new Vector2(26f, -14f),
            new Vector2(GuideCardWidth - 52f, 34f),
            "PANDUAN GESTUR MODE UDARA",
            TextAlignmentOptions.Center,
            CardBorderCyanColor,
            16f,
            23f,
            TextWrappingModes.NoWrap);
        titleTmp.fontStyle = FontStyles.Bold;

        TMP_Text subTmp = CreateTopLeftText(
            "Txt_GuideSubtitle",
            guideCardRect,
            new Vector2(26f, -48f),
            new Vector2(GuideCardWidth - 52f, 24f),
            $"5 Kontrol Utama Tanpa Sentuh Layar  {dotGlyph}  Buka ulang kapan saja via tombol ?",
            TextAlignmentOptions.Center,
            SubtitleTextColor,
            12f,
            15.5f,
            TextWrappingModes.NoWrap);
        subTmp.fontStyle = FontStyles.Normal;

        CreateDivider("Divider_Top", guideCardRect, 26f, -78f, GuideCardWidth - 52f, 2f);

        // 5 Baris Kontrol Utama Mode Udara v7
        float rowY = -92f;
        const float rowStep = 72f;
        const float rowHeight = 62f;

        CreateGuideRuleRow(
            "Row_Rule_1",
            guideCardRect,
            rowY,
            rowHeight,
            "TELUNJUK",
            TagIndexBgColor,
            TagIndexAccentColor,
            $"<color=#6EFFB8><b>Sakelar Input:</b></color> Sentuh ringan ujung telunjuk pada sakelar kiri untuk membalik 0 {arrowGlyph} 1.");
        rowY -= rowStep;

        CreateGuideRuleRow(
            "Row_Rule_2",
            guideCardRect,
            rowY,
            rowHeight,
            "PUTAR + 5 JARI",
            TagSummonBgColor,
            TagSummonAccentColor,
            $"<color=#54F5FF><b>Summon Gerbang:</b></color> Putar jari di ruang tengah (1 AND {dotGlyph} 2 OR {dotGlyph} 3 XOR {dotGlyph} 4 NAND {dotGlyph} Jempol NOT), lalu buka lebar 5 jari (Bloom).");
        rowY -= rowStep;

        CreateGuideRuleRow(
            "Row_Rule_3",
            guideCardRect,
            rowY,
            rowHeight,
            "CUBIT",
            TagPinchBgColor,
            TagPinchAccentColor,
            "<color=#C6FF5C><b>Kabel Manual:</b></color> Cubit (Jempol + Telunjuk) pada orb port untuk tarik/colok; lepas di ruang kosong untuk putus.");
        rowY -= rowStep;

        CreateGuideRuleRow(
            "Row_Rule_4",
            guideCardRect,
            rowY,
            rowHeight,
            "KEPAL",
            TagFistBgColor,
            TagFistAccentColor,
            "<color=#FFBE52><b>Pindah & Hapus:</b></color> Kepal badan gerbang untuk geser; tarik keluar dari 4 siku sudut (bingkai merah) lalu lepas untuk hapus.");
        rowY -= rowStep;

        CreateGuideRuleRow(
            "Row_Rule_5",
            guideCardRect,
            rowY,
            rowHeight,
            $"UJI 00{dashGlyph}11",
            TagTestBgColor,
            TagTestAccentColor,
            "<color=#6EFF8F><b>Lulus Otomatis:</b></color> Balik sakelar untuk menguji 4 kombinasi sampai 4/4 hijau (Auto-Win).");

        CreateDivider("Divider_Bottom", guideCardRect, 26f, -458f, GuideCardWidth - 52f, 2f);

        // 2 Tombol Responsif Bawah: MULAI LATIHAN CEPAT (sian) & TUTUP (netral)
        const float startBtnW = 360f;
        const float closeBtnW = 220f;
        const float btnGap = 28f;
        const float btnH = 54f;
        float totalBtnW = startBtnW + btnGap + closeBtnW;
        float startX = (GuideCardWidth - totalBtnW) * 0.5f;
        const float btnY = -474f;

        btnStartPractice = CreateCardActionButton(
            "Btn_StartQuickPractice",
            guideCardRect,
            startX,
            btnY,
            startBtnW,
            btnH,
            "MULAI LATIHAN CEPAT",
            PrimaryBtnBgColor,
            PrimaryBtnOutlineColor,
            Color.white,
            OnStartPracticeClicked);

        btnCloseGuide = CreateCardActionButton(
            "Btn_CloseGestureGuide",
            guideCardRect,
            startX + startBtnW + btnGap,
            btnY,
            closeBtnW,
            btnH,
            "TUTUP",
            NeutralBtnBgColor,
            NeutralBtnOutlineColor,
            new Color(0.88f, 0.94f, 0.99f, 1f),
            OnCloseGuideClicked);

        guideRootObj.SetActive(false);
    }

    private void CreateGuideRuleRow(
        string name,
        RectTransform parent,
        float topY,
        float height,
        string tagLabel,
        Color tagBgColor,
        Color tagAccentColor,
        string bodyRichText)
    {
        float rowWidth = GuideCardWidth - 52f;
        GameObject rowObj = new GameObject(name, typeof(RectTransform), typeof(Image));
        rowObj.transform.SetParent(parent, false);

        RectTransform rowRect = rowObj.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(0f, 1f);
        rowRect.pivot = new Vector2(0f, 1f);
        rowRect.anchoredPosition = new Vector2(26f, topY);
        rowRect.sizeDelta = new Vector2(rowWidth, height);

        Image rowBg = rowObj.GetComponent<Image>();
        rowBg.color = RowBgColor;
        rowBg.raycastTarget = false;

        // Strip aksen warna di sisi kiri baris
        GameObject stripObj = new GameObject("AccentStrip", typeof(RectTransform), typeof(Image));
        stripObj.transform.SetParent(rowRect, false);
        RectTransform stripRect = stripObj.GetComponent<RectTransform>();
        stripRect.anchorMin = new Vector2(0f, 0f);
        stripRect.anchorMax = new Vector2(0f, 1f);
        stripRect.pivot = new Vector2(0f, 0.5f);
        stripRect.anchoredPosition = Vector2.zero;
        stripRect.sizeDelta = new Vector2(5f, 0f);
        Image stripImg = stripObj.GetComponent<Image>();
        stripImg.color = tagAccentColor;
        stripImg.raycastTarget = false;

        // Tag gestur berwarna kontras di kiri
        GameObject tagObj = new GameObject("TagPill", typeof(RectTransform), typeof(Image), typeof(Outline));
        tagObj.transform.SetParent(rowRect, false);
        RectTransform tagRect = tagObj.GetComponent<RectTransform>();
        tagRect.anchorMin = new Vector2(0f, 0.5f);
        tagRect.anchorMax = new Vector2(0f, 0.5f);
        tagRect.pivot = new Vector2(0f, 0.5f);
        tagRect.anchoredPosition = new Vector2(16f, 0f);
        tagRect.sizeDelta = new Vector2(188f, 38f);

        Image tagBg = tagObj.GetComponent<Image>();
        tagBg.color = tagBgColor;
        tagBg.raycastTarget = false;

        Outline tagOutline = tagObj.GetComponent<Outline>();
        tagOutline.effectColor = tagAccentColor;
        tagOutline.effectDistance = new Vector2(1.4f, -1.4f);

        TMP_Text tagTmp = CreateStretchedText("TagLabel", tagRect, tagLabel, TextAlignmentOptions.Center, tagAccentColor, 12f, 15.5f, TextWrappingModes.NoWrap);
        tagTmp.fontStyle = FontStyles.Bold;

        // Deskripsi 1 baris inti (dengan dukungan wrap halus bila layar sangat sempit agar tidak terpotong)
        GameObject bodyObj = new GameObject("BodyText", typeof(RectTransform), typeof(TextMeshProUGUI));
        bodyObj.transform.SetParent(rowRect, false);
        RectTransform bodyRect = bodyObj.GetComponent<RectTransform>();
        bodyRect.anchorMin = new Vector2(0f, 0.5f);
        bodyRect.anchorMax = new Vector2(1f, 0.5f);
        bodyRect.pivot = new Vector2(0.5f, 0.5f);
        bodyRect.offsetMin = new Vector2(220f, -26f);
        bodyRect.offsetMax = new Vector2(-16f, 26f);

        TextMeshProUGUI bodyTmp = bodyObj.GetComponent<TextMeshProUGUI>();
        if (safeFont != null)
        {
            bodyTmp.font = safeFont;
        }
        bodyTmp.text = bodyRichText;
        bodyTmp.richText = true;
        bodyTmp.fontStyle = FontStyles.Normal;
        bodyTmp.alignment = TextAlignmentOptions.MidlineLeft;
        bodyTmp.color = RowTextColor;
        bodyTmp.enableAutoSizing = true;
        bodyTmp.fontSizeMin = 13f;
        bodyTmp.fontSizeMax = 16.5f;
        bodyTmp.textWrappingMode = TextWrappingModes.Normal;
        bodyTmp.raycastTarget = false;
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

        // Pil tipis non-intrusif di tengah-bawah layar (di bawah AssemblyZone, bebas Input B & OutputTruthPod)
        practicePillObj = new GameObject("Pill_AirQuickPractice", typeof(RectTransform), typeof(Image), typeof(Outline));
        practicePillObj.transform.SetParent(practiceSafeAreaRect, false);
        RectTransform pillRect = practicePillObj.GetComponent<RectTransform>();
        pillRect.anchorMin = new Vector2(0.5f, 0f);
        pillRect.anchorMax = new Vector2(0.5f, 0f);
        pillRect.pivot = new Vector2(0.5f, 0f);
        pillRect.anchoredPosition = new Vector2(0f, 12f);
        pillRect.sizeDelta = new Vector2(PracticePillWidth, PracticePillHeight);

        practicePillBg = practicePillObj.GetComponent<Image>();
        practicePillBg.color = PillBgIdleColor;
        practicePillBg.raycastTarget = false;

        practicePillOutline = practicePillObj.GetComponent<Outline>();
        practicePillOutline.effectColor = PillOutlineIdleColor;
        practicePillOutline.effectDistance = new Vector2(1.8f, -1.8f);

        // Lencana langkah 1/4..4/4 di ujung kiri pil
        GameObject badgeObj = new GameObject("Practice_StepBadge", typeof(RectTransform), typeof(Image));
        badgeObj.transform.SetParent(pillRect, false);
        RectTransform badgeRect = badgeObj.GetComponent<RectTransform>();
        badgeRect.anchorMin = new Vector2(0f, 0.5f);
        badgeRect.anchorMax = new Vector2(0f, 0.5f);
        badgeRect.pivot = new Vector2(0f, 0.5f);
        badgeRect.anchoredPosition = new Vector2(10f, 0f);
        badgeRect.sizeDelta = new Vector2(72f, 34f);

        practiceBadgeBg = badgeObj.GetComponent<Image>();
        practiceBadgeBg.color = BadgeBgIdleColor;
        practiceBadgeBg.raycastTarget = false;

        txtPracticeBadge = CreateStretchedText("Txt_StepBadge", badgeRect, BadgeStep1, TextAlignmentOptions.Center, BadgeTextIdleColor, 13f, 17f, TextWrappingModes.NoWrap);
        txtPracticeBadge.fontStyle = FontStyles.Bold;

        // Teks instruksi langkah aktif di tengah pil
        GameObject promptObj = new GameObject("Txt_PracticePrompt", typeof(RectTransform), typeof(TextMeshProUGUI));
        promptObj.transform.SetParent(pillRect, false);
        practicePromptRect = promptObj.GetComponent<RectTransform>();
        practicePromptRect.anchorMin = new Vector2(0f, 0f);
        practicePromptRect.anchorMax = new Vector2(1f, 1f);
        practicePromptRect.offsetMin = new Vector2(92f, 4f);
        practicePromptRect.offsetMax = new Vector2(-104f, -4f);

        TextMeshProUGUI promptTmp = promptObj.GetComponent<TextMeshProUGUI>();
        if (safeFont != null)
        {
            promptTmp.font = safeFont;
        }
        promptTmp.text = step1PromptText;
        promptTmp.fontStyle = FontStyles.Bold;
        promptTmp.alignment = TextAlignmentOptions.MidlineLeft;
        promptTmp.color = PracticePromptColor;
        promptTmp.enableAutoSizing = true;
        promptTmp.fontSizeMin = 13f;
        promptTmp.fontSizeMax = 18f;
        promptTmp.textWrappingMode = TextWrappingModes.NoWrap;
        promptTmp.raycastTarget = false;
        txtPracticePrompt = promptTmp;

        // Tombol kecil 'Lewati' di ujung kanan pil
        skipPracticeBtnObj = new GameObject("Btn_SkipQuickPractice", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        skipPracticeBtnObj.transform.SetParent(pillRect, false);
        RectTransform skipRect = skipPracticeBtnObj.GetComponent<RectTransform>();
        skipRect.anchorMin = new Vector2(1f, 0.5f);
        skipRect.anchorMax = new Vector2(1f, 0.5f);
        skipRect.pivot = new Vector2(1f, 0.5f);
        skipRect.anchoredPosition = new Vector2(-10f, 0f);
        skipRect.sizeDelta = new Vector2(84f, 34f);

        Image skipBg = skipPracticeBtnObj.GetComponent<Image>();
        skipBg.color = SkipBtnBgColor;
        skipBg.raycastTarget = true;

        Outline skipOutline = skipPracticeBtnObj.GetComponent<Outline>();
        skipOutline.effectColor = SkipBtnOutlineColor;
        skipOutline.effectDistance = new Vector2(1.2f, -1.2f);

        btnSkipPractice = skipPracticeBtnObj.GetComponent<Button>();
        btnSkipPractice.targetGraphic = skipBg;
        btnSkipPractice.colors = GuideButtonColorBlock;
        btnSkipPractice.onClick.AddListener(OnSkipPracticeClicked);

        TMP_Text skipTmp = CreateStretchedText("Txt_SkipLabel", skipRect, "Lewati", TextAlignmentOptions.Center, SkipBtnTextColor, 12f, 15.5f, TextWrappingModes.NoWrap);
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
            practicePillBg.color = highlightGreen ? PillBgSuccessColor : PillBgIdleColor;
        }
        if (practicePillOutline != null)
        {
            practicePillOutline.effectColor = highlightGreen ? PillOutlineSuccessColor : PillOutlineIdleColor;
        }
        if (practiceBadgeBg != null)
        {
            practiceBadgeBg.color = highlightGreen ? BadgeBgSuccessColor : BadgeBgIdleColor;
        }
        if (txtPracticeBadge != null)
        {
            txtPracticeBadge.color = highlightGreen ? BadgeTextSuccessColor : BadgeTextIdleColor;
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
            practicePromptRect.offsetMin = new Vector2(92f, 4f);
            practicePromptRect.offsetMax = new Vector2(isCompleted ? -20f : -104f, -4f);
        }

        if (txtPracticePrompt != null)
        {
            if (isCompleted)
            {
                txtPracticePrompt.text = CompletedBannerText;
                txtPracticePrompt.alignment = TextAlignmentOptions.Center;
                txtPracticePrompt.color = PracticeCompleteTextColor;
            }
            else
            {
                txtPracticePrompt.alignment = TextAlignmentOptions.MidlineLeft;
                txtPracticePrompt.color = highlightGreen ? PracticeCompleteTextColor : PracticePromptColor;
                switch (practiceState)
                {
                    case PracticeState.Step1ToggleInput: txtPracticePrompt.text = step1PromptText; break;
                    case PracticeState.Step2SummonGate: txtPracticePrompt.text = step2PromptText; break;
                    case PracticeState.Step3ConnectCable: txtPracticePrompt.text = step3PromptText; break;
                    case PracticeState.Step4DeleteGate: txtPracticePrompt.text = step4PromptText; break;
                    default: txtPracticePrompt.text = step1PromptText; break;
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
            guideSafeAreaRect.anchorMax = new Vector2(maxAnchor.x, 1f);
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
    // HELPER UI PROSEDURAL
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
        btn.colors = GuideButtonColorBlock;
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

    private static void CreateBorderStrip(
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
