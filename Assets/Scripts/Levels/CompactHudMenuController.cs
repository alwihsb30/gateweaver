using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Pengendali HUD Minimalis & Menu Kompak pojok kanan atas (GateWeaver AR v7.0 Fase 2).
/// - Code-First 100% (0 diff SampleScene.unity): menata ulang Panel_TopHud menjadi 1 Bilah Atas ramping (72 px),
///   memensiunkan Panel_TruthTableScanner & tumpukan tombol kanan lama, serta memisahkan kontrol bawah:
///   * Mode Udara (Air-Weaving): 0 tombol gameplay (100% Zero-Touch).
///   * Mode Kartu (Vuforia): 4 kapsul ramping semi-transparan di sudut bawah (Input A/B, Uji/Lanjut; sembunyi Uji di Sandbox).
/// - Laci menu kompak dengan ikon 3 garis prosedural (bebas masalah glyph font) + scrim klik-luar untuk menutup.
/// - Sistem Toast-Only untuk Txt_Feedback (tersembunyi saat idle, prioritas aman agar no-hand tidak menimpa hasil Uji/Lulus/Gagal).
/// </summary>
public sealed class CompactHudMenuController : MonoBehaviour
{
    private const float TopBarHeight = 72f;
    private const float DefaultToastDurationSeconds = 2.6f;
    private const float ImportantToastDurationSeconds = 4.6f;
    private const float FailureToastDurationSeconds = 5.5f;
    private const float HintToastDurationSeconds = 5.5f;

    private static readonly Color TopBarBgColor = new Color(0.03f, 0.06f, 0.12f, 0.80f);
    private static readonly Color TopBarAccentColor = new Color(0.18f, 0.88f, 1.00f, 0.82f);
    private static readonly Color QuotaAlertPillBgColor = new Color(0.74f, 0.09f, 0.14f, 0.96f);
    private static readonly Color QuotaAlertPillOutlineColor = new Color(1.00f, 0.56f, 0.36f, 1.00f);
    private static readonly Color QuotaAlertTextColor = new Color(1.00f, 0.97f, 0.92f, 1.00f);
    private static readonly Color ToastDefaultBgColor = new Color(0.03f, 0.08f, 0.15f, 0.92f);
    private static readonly Color ToastDefaultOutlineColor = new Color(0.20f, 0.88f, 1.00f, 0.88f);
    private static readonly Color ToastFailureBgColor = new Color(0.36f, 0.05f, 0.08f, 0.97f);
    private static readonly Color ToastFailureOutlineColor = new Color(1.00f, 0.32f, 0.32f, 1.00f);
    private static readonly Color ToastFailureTextColor = new Color(1.00f, 0.96f, 0.93f, 1.00f);
    private static readonly Color ToastSuccessBgColor = new Color(0.04f, 0.24f, 0.14f, 0.96f);
    private static readonly Color ToastSuccessOutlineColor = new Color(0.24f, 0.98f, 0.62f, 1.00f);
    private static readonly Color ToastSuccessTextColor = new Color(0.93f, 1.00f, 0.95f, 1.00f);
    private static readonly Color ToastHintBgColor = new Color(0.07f, 0.14f, 0.28f, 0.96f);
    private static readonly Color ToastHintOutlineColor = new Color(1.00f, 0.84f, 0.28f, 0.98f);
    private static readonly Color MenuButtonBgColor = new Color(0.08f, 0.22f, 0.34f, 0.94f);
    private static readonly Color MenuButtonOpenBgColor = new Color(0.12f, 0.42f, 0.56f, 0.98f);
    private static readonly Color MenuDrawerBgColor = new Color(0.04f, 0.08f, 0.15f, 0.96f);
    private static readonly Color DrawerItemBgColor = new Color(0.09f, 0.18f, 0.29f, 0.96f);
    private static readonly Color DrawerItemModeActiveBgColor = new Color(0.08f, 0.34f, 0.28f, 0.96f);
    private static readonly Color DrawerItemResetBgColor = new Color(0.24f, 0.14f, 0.16f, 0.96f);
    private static readonly Color DrawerItemMutedBgColor = new Color(0.26f, 0.16f, 0.12f, 0.96f);
    private static readonly Color InputACyanOutlineColor = new Color(0.18f, 0.85f, 1.00f, 0.96f);
    private static readonly Color InputBGoldOutlineColor = new Color(1.00f, 0.84f, 0.22f, 0.96f);
    private static readonly Color CardBottomVerifyBgColor = new Color(0.07f, 0.40f, 0.56f, 0.74f);
    private static readonly Color CardBottomNextBgColor = new Color(0.08f, 0.46f, 0.30f, 0.82f);

    private static readonly ColorBlock CompactBtnColorBlock = new ColorBlock
    {
        normalColor = Color.white,
        highlightedColor = new Color(0.90f, 0.97f, 1.00f, 1f),
        pressedColor = new Color(0.72f, 0.82f, 0.90f, 1f),
        selectedColor = Color.white,
        disabledColor = new Color(0.45f, 0.48f, 0.52f, 0.55f),
        colorMultiplier = 1f,
        fadeDuration = 0.08f
    };

    private LevelAndUIManager manager;
    private Canvas targetCanvas;
    private TMP_FontAsset safeFont;

    private TMP_Text txtLevelTitle;
    private TMP_Text txtMissionDesc;
    private TMP_Text txtQuotaStatus;
    private TMP_Text txtFeedback;
    private GameObject feedbackBackdropObj;
    private Image feedbackBackdropImage;
    private Outline feedbackBackdropOutline;
    private GameObject quotaAlertPillObj;

    private GameObject legacyScannerPanelObj;
    private GameObject legacyResetBtnObj;
    private GameObject legacyLevelSelectorBtnObj;
    private GameObject legacyAirWeavingBtnObj;

    private Button btnInputA;
    private Button btnInputB;
    private Button btnVerify;
    private Button btnNextLevel;

    private Button menuToggleButton;
    private Image menuToggleBg;
    private GameObject menuScrimObj;
    private Button menuScrimButton;
    private GameObject menuDrawerObj;

    private Button drawerModeBtn;
    private Image drawerModeBg;
    private TMP_Text txtDrawerMode;
    private Button drawerLevelBtn;
    private Button drawerResetBtn;
    private TMP_Text txtDrawerReset;
    private Button drawerReportBtn;
    private Button drawerHintBtn;
    private TMP_Text txtDrawerHint;
    private Button drawerAudioBtn;
    private Image drawerAudioBg;
    private TMP_Text txtDrawerAudio;

    private bool isInitialized = false;
    private bool isMenuDrawerOpen = false;
    private bool isAirWeavingActive = false;

    // Toast-Only state (zero-GC)
    private string lastObservedFeedbackText;
    private float toastRemainingSeconds = 0f;
    private bool activeToastIsProtected = false;
    private bool activeToastIsNoHand = false;

    public bool IsMenuDrawerOpen => isMenuDrawerOpen;
    public bool HasActiveProtectedToast => toastRemainingSeconds > 0f && (activeToastIsProtected || !activeToastIsNoHand);

    public void Initialize(
        LevelAndUIManager owner,
        Canvas canvas,
        TMP_FontAsset font,
        TMP_Text levelTitleText,
        TMP_Text missionDescText,
        TMP_Text quotaStatusText,
        TMP_Text feedbackText,
        GameObject feedbackBackdrop,
        GameObject scannerPanel,
        GameObject oldResetBtn,
        GameObject oldLevelSelectorBtn,
        GameObject oldAirWeavingBtn,
        Button inputAButton,
        Button inputBButton,
        Button verifyButton,
        Button nextLevelButton)
    {
        if (isInitialized)
        {
            return;
        }

        manager = owner;
        targetCanvas = canvas;
        safeFont = font;
        txtLevelTitle = levelTitleText;
        txtMissionDesc = missionDescText;
        txtQuotaStatus = quotaStatusText;
        txtFeedback = feedbackText;
        feedbackBackdropObj = feedbackBackdrop;
        feedbackBackdropImage = feedbackBackdrop != null ? feedbackBackdrop.GetComponent<Image>() : null;
        feedbackBackdropOutline = feedbackBackdrop != null ? feedbackBackdrop.GetComponent<Outline>() : null;

        legacyScannerPanelObj = scannerPanel;
        legacyResetBtnObj = oldResetBtn;
        legacyLevelSelectorBtnObj = oldLevelSelectorBtn;
        legacyAirWeavingBtnObj = oldAirWeavingBtn;

        btnInputA = inputAButton;
        btnInputB = inputBButton;
        btnVerify = verifyButton;
        btnNextLevel = nextLevelButton;

        RetireLegacyPanelsAndRightStack();
        RectTransform topBarRect = ReshapeSlimTopBar();
        if (topBarRect != null)
        {
            BuildCompactMenuButtonAndDrawer(topBarRect);
        }

        ConfigureMinimalistBottomControls();
        ConfigureFeedbackToastLayout();

        isInitialized = true;
        CloseMenuDrawer();
        ClearToastImmediate();
        RefreshAllVisualStates();
    }

    public void SetAirWeavingMode(bool active)
    {
        isAirWeavingActive = active;
        if (!isInitialized)
        {
            return;
        }

        CloseMenuDrawer();
        ConfigureFeedbackToastLayout();
        RefreshAllVisualStates();
    }

    public void RefreshAllVisualStates()
    {
        if (!isInitialized || manager == null)
        {
            return;
        }

        // Pastikan panel kiri pensiun tidak pernah hidup kembali
        if (legacyScannerPanelObj != null && legacyScannerPanelObj.activeSelf)
        {
            legacyScannerPanelObj.SetActive(false);
        }

        RefreshActionControlsState();
        RefreshDrawerItemLabels();
    }

    public void RefreshActionControlsState()
    {
        if (!isInitialized || manager == null)
        {
            return;
        }

        bool isSandbox = manager.IsSandboxMode;
        bool isReadyNext = manager.IsReadyForNextLevel;

        if (isAirWeavingActive)
        {
            SetObjActive(btnInputA, false);
            SetObjActive(btnInputB, false);
            SetObjActive(btnVerify, false);
            SetObjActive(btnNextLevel, false);
            return;
        }

        // Mode Kartu (Vuforia): 4 kapsul ramping semi-transparan; sembunyikan Uji & Lanjut di Sandbox
        SetObjActive(btnInputA, true);
        SetObjActive(btnInputB, true);
        SetObjActive(btnVerify, !isSandbox && !isReadyNext);
        SetObjActive(btnNextLevel, !isSandbox && isReadyNext);
    }

    public void CloseMenuDrawer()
    {
        isMenuDrawerOpen = false;
        if (menuDrawerObj != null && menuDrawerObj.activeSelf)
        {
            menuDrawerObj.SetActive(false);
        }
        if (menuScrimObj != null && menuScrimObj.activeSelf)
        {
            menuScrimObj.SetActive(false);
        }
        if (menuToggleBg != null)
        {
            menuToggleBg.color = MenuButtonBgColor;
        }
    }

    public void ToggleMenuDrawer()
    {
        if (!isInitialized)
        {
            return;
        }

        if (isMenuDrawerOpen)
        {
            CloseMenuDrawer();
            return;
        }

        if (manager != null)
        {
            manager.CloseAllModalsForMenu();
        }

        RefreshDrawerItemLabels();
        isMenuDrawerOpen = true;
        if (menuScrimObj != null)
        {
            menuScrimObj.transform.SetAsLastSibling();
            menuScrimObj.SetActive(true);
        }
        if (menuDrawerObj != null)
        {
            menuDrawerObj.transform.SetAsLastSibling();
            menuDrawerObj.SetActive(true);
        }
        if (menuToggleBg != null)
        {
            menuToggleBg.color = MenuButtonOpenBgColor;
        }
    }

    /// <summary>
    /// Menampilkan notifikasi toast dengan prioritas & durasi terkontrol.
    /// Toast rendah (no-hand) tidak pernah menimpa toast hasil Uji/Lulus/Gagal/Hint yang sedang tampil.
    /// </summary>
    public void SetQuotaAlertState(bool isOverQuota)
    {
        if (quotaAlertPillObj != null && quotaAlertPillObj.activeSelf != isOverQuota)
        {
            quotaAlertPillObj.SetActive(isOverQuota);
        }

        if (isOverQuota && txtQuotaStatus != null)
        {
            txtQuotaStatus.color = QuotaAlertTextColor;
        }
    }

    public void ShowToast(string message, Color color, float durationSeconds, bool isProtected = true, bool isNoHandPrompt = false)
    {
        if (txtFeedback == null)
        {
            return;
        }

        if (isNoHandPrompt && toastRemainingSeconds > 0f && !activeToastIsNoHand)
        {
            return;
        }

        txtFeedback.text = message;
        txtFeedback.color = color;
        ApplyToastStyleForMessage(message);
        lastObservedFeedbackText = message;
        toastRemainingSeconds = Mathf.Max(0.4f, durationSeconds);
        activeToastIsProtected = isProtected;
        activeToastIsNoHand = isNoHandPrompt;
        SetFeedbackToastVisible(!string.IsNullOrWhiteSpace(message));
    }

    public void ClearLowPriorityNoHandToast()
    {
        if (activeToastIsNoHand)
        {
            ClearToastImmediate();
        }
    }

    public void ClearToastImmediate()
    {
        toastRemainingSeconds = 0f;
        activeToastIsProtected = false;
        activeToastIsNoHand = false;

        if (txtFeedback != null)
        {
            txtFeedback.text = string.Empty;
            lastObservedFeedbackText = string.Empty;
        }

        SetFeedbackToastVisible(false);
    }

    /// <summary>
    /// Memantau perubahan langsung pada txtFeedback.text dan mengelola auto-hide toast secara zero-GC.
    /// </summary>
    public void TickToast(float unscaledDt, bool isVerifying)
    {
        if (!isInitialized || txtFeedback == null)
        {
            return;
        }

        string currentText = txtFeedback.text;
        if (!ReferenceEquals(currentText, lastObservedFeedbackText))
        {
            lastObservedFeedbackText = currentText;
            if (string.IsNullOrWhiteSpace(currentText))
            {
                toastRemainingSeconds = 0f;
                activeToastIsProtected = false;
                activeToastIsNoHand = false;
                SetFeedbackToastVisible(false);
            }
            else
            {
                bool isFailure = currentText.StartsWith("Gagal", StringComparison.Ordinal);
                bool isSuccess = currentText.StartsWith("BERHASIL", StringComparison.Ordinal)
                                 || currentText.StartsWith("SELAMAT", StringComparison.Ordinal);
                bool isHint = currentText.StartsWith("[Petunjuk", StringComparison.Ordinal);
                ApplyToastStyleForMessage(currentText);

                if (isFailure)
                {
                    toastRemainingSeconds = FailureToastDurationSeconds;
                }
                else if (isHint)
                {
                    toastRemainingSeconds = HintToastDurationSeconds;
                }
                else if (isSuccess)
                {
                    toastRemainingSeconds = ImportantToastDurationSeconds;
                }
                else
                {
                    toastRemainingSeconds = DefaultToastDurationSeconds;
                }

                activeToastIsProtected = isFailure || isSuccess || isHint;
                activeToastIsNoHand = false;
                SetFeedbackToastVisible(true);
            }
        }

        if (isVerifying)
        {
            return;
        }

        if (toastRemainingSeconds > 0f)
        {
            toastRemainingSeconds -= unscaledDt;
            if (toastRemainingSeconds <= 0f)
            {
                ClearToastImmediate();
            }
        }
    }

    private void OnDestroy()
    {
        RemoveBtnListener(menuToggleButton, ToggleMenuDrawer);
        RemoveBtnListener(menuScrimButton, CloseMenuDrawer);
        RemoveBtnListener(drawerModeBtn, OnDrawerModeClicked);
        RemoveBtnListener(drawerLevelBtn, OnDrawerLevelClicked);
        RemoveBtnListener(drawerResetBtn, OnDrawerResetClicked);
        RemoveBtnListener(drawerReportBtn, OnDrawerReportClicked);
        RemoveBtnListener(drawerHintBtn, OnDrawerHintClicked);
        RemoveBtnListener(drawerAudioBtn, OnDrawerAudioClicked);

        if (menuDrawerObj != null)
        {
            Destroy(menuDrawerObj);
            menuDrawerObj = null;
        }
        if (menuScrimObj != null)
        {
            Destroy(menuScrimObj);
            menuScrimObj = null;
        }
        if (quotaAlertPillObj != null)
        {
            Destroy(quotaAlertPillObj);
            quotaAlertPillObj = null;
        }
        feedbackBackdropImage = null;
        feedbackBackdropOutline = null;
    }

    // =========================================================================
    // CODE-FIRST LAYOUT RESHAPING & COMPACT MENU CONSTRUCTION
    // =========================================================================

    private void RetireLegacyPanelsAndRightStack()
    {
        if (legacyScannerPanelObj != null)
        {
            legacyScannerPanelObj.SetActive(false);
        }
        if (legacyResetBtnObj != null)
        {
            legacyResetBtnObj.SetActive(false);
        }
        if (legacyLevelSelectorBtnObj != null)
        {
            legacyLevelSelectorBtnObj.SetActive(false);
        }
        if (legacyAirWeavingBtnObj != null)
        {
            legacyAirWeavingBtnObj.SetActive(false);
        }

        // Matikan strip pita hitam 150 px pada Panel_BottomHUD tanpa mematikan anak lain di Canvas
        GameObject bottomPanel = GameObject.Find("Panel_BottomHUD");
        if (bottomPanel != null)
        {
            Image bottomImg = bottomPanel.GetComponent<Image>();
            if (bottomImg != null)
            {
                bottomImg.enabled = false;
            }
        }
    }

    private RectTransform ReshapeSlimTopBar()
    {
        GameObject topPanelObj = GameObject.Find("Panel_TopHud");
        if (topPanelObj == null && txtLevelTitle != null && txtLevelTitle.transform.parent != null)
        {
            topPanelObj = txtLevelTitle.transform.parent.gameObject;
        }
        if (topPanelObj == null)
        {
            return null;
        }

        RectTransform topRect = topPanelObj.GetComponent<RectTransform>();
        if (topRect == null)
        {
            return null;
        }

        topRect.anchorMin = new Vector2(0f, 1f);
        topRect.anchorMax = new Vector2(1f, 1f);
        topRect.pivot = new Vector2(0.5f, 1f);
        topRect.anchoredPosition = Vector2.zero;
        topRect.sizeDelta = new Vector2(0f, TopBarHeight);

        Image topBg = topPanelObj.GetComponent<Image>();
        if (topBg != null)
        {
            topBg.color = TopBarBgColor;
            topBg.raycastTarget = false;
        }

        // Garis aksen sian tipis di tepi bawah Bilah Atas
        GameObject accentLine = new GameObject("TopBar_AccentLine", typeof(RectTransform), typeof(Image));
        accentLine.transform.SetParent(topRect, false);
        RectTransform lineRect = accentLine.GetComponent<RectTransform>();
        lineRect.anchorMin = new Vector2(0f, 0f);
        lineRect.anchorMax = new Vector2(1f, 0f);
        lineRect.pivot = new Vector2(0.5f, 0f);
        lineRect.anchoredPosition = Vector2.zero;
        lineRect.sizeDelta = new Vector2(0f, 2f);
        Image lineImg = accentLine.GetComponent<Image>();
        lineImg.color = TopBarAccentColor;
        lineImg.raycastTarget = false;

        // Kiri (~24%): Judul Level singkat 1 baris
        if (txtLevelTitle != null)
        {
            RectTransform titleRect = txtLevelTitle.rectTransform;
            titleRect.SetParent(topRect, false);
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(0.24f, 1f);
            titleRect.pivot = new Vector2(0f, 0.5f);
            titleRect.offsetMin = new Vector2(16f, 6f);
            titleRect.offsetMax = new Vector2(-6f, -6f);
            txtLevelTitle.alignment = TextAlignmentOptions.MidlineLeft;
            txtLevelTitle.textWrappingMode = TextWrappingModes.NoWrap;
            txtLevelTitle.enableAutoSizing = true;
            txtLevelTitle.fontSizeMin = 12f;
            txtLevelTitle.fontSizeMax = 21f;
            txtLevelTitle.raycastTarget = false;
        }

        // Tengah (~51%): Misi kontekstual 1 baris (atau Rumus Boolean real-time di Mode Sandbox)
        if (txtMissionDesc != null)
        {
            RectTransform missionRect = txtMissionDesc.rectTransform;
            missionRect.SetParent(topRect, false);
            missionRect.anchorMin = new Vector2(0.24f, 0f);
            missionRect.anchorMax = new Vector2(0.75f, 1f);
            missionRect.pivot = new Vector2(0.5f, 0.5f);
            missionRect.offsetMin = new Vector2(8f, 6f);
            missionRect.offsetMax = new Vector2(-8f, -6f);
            txtMissionDesc.alignment = TextAlignmentOptions.Center;
            txtMissionDesc.textWrappingMode = TextWrappingModes.NoWrap;
            txtMissionDesc.enableAutoSizing = true;
            txtMissionDesc.fontSizeMin = 13f;
            txtMissionDesc.fontSizeMax = 22f;
            txtMissionDesc.lineSpacing = 0f;
            txtMissionDesc.raycastTarget = false;
        }

        // Kanan (~25% minus ruang tombol menu 84 px): Status Kuota kompak + lencana kapsul merah saat over-quota
        if (txtQuotaStatus != null)
        {
            quotaAlertPillObj = new GameObject("Quota_AlertPill", typeof(RectTransform), typeof(Image), typeof(Outline));
            quotaAlertPillObj.transform.SetParent(topRect, false);
            RectTransform pillRect = quotaAlertPillObj.GetComponent<RectTransform>();
            pillRect.anchorMin = new Vector2(1f, 0.5f);
            pillRect.anchorMax = new Vector2(1f, 0.5f);
            pillRect.pivot = new Vector2(1f, 0.5f);
            pillRect.anchoredPosition = new Vector2(-80f, 0f);
            pillRect.sizeDelta = new Vector2(252f, 44f);
            Image pillImg = quotaAlertPillObj.GetComponent<Image>();
            pillImg.color = QuotaAlertPillBgColor;
            pillImg.raycastTarget = false;
            Outline pillOutline = quotaAlertPillObj.GetComponent<Outline>();
            pillOutline.effectColor = QuotaAlertPillOutlineColor;
            pillOutline.effectDistance = new Vector2(2f, -2f);
            quotaAlertPillObj.SetActive(false);

            RectTransform quotaRect = txtQuotaStatus.rectTransform;
            quotaRect.SetParent(topRect, false);
            quotaAlertPillObj.transform.SetSiblingIndex(quotaRect.GetSiblingIndex());
            quotaRect.anchorMin = new Vector2(0.75f, 0f);
            quotaRect.anchorMax = new Vector2(1f, 1f);
            quotaRect.pivot = new Vector2(1f, 0.5f);
            quotaRect.offsetMin = new Vector2(6f, 6f);
            quotaRect.offsetMax = new Vector2(-90f, -6f);
            txtQuotaStatus.alignment = TextAlignmentOptions.MidlineRight;
            txtQuotaStatus.textWrappingMode = TextWrappingModes.NoWrap;
            txtQuotaStatus.enableAutoSizing = true;
            txtQuotaStatus.fontSizeMin = 13f;
            txtQuotaStatus.fontSizeMax = 20f;
            txtQuotaStatus.raycastTarget = false;
        }

        return topRect;
    }

    private void BuildCompactMenuButtonAndDrawer(RectTransform topBarRect)
    {
        // 1. Tombol kompak pojok kanan atas dengan 3 garis prosedural (100% bebas masalah glyph font)
        GameObject btnObj = new GameObject("Btn_CompactMenu", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        btnObj.transform.SetParent(topBarRect, false);
        RectTransform btnRect = btnObj.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(1f, 0.5f);
        btnRect.anchorMax = new Vector2(1f, 0.5f);
        btnRect.pivot = new Vector2(1f, 0.5f);
        btnRect.anchoredPosition = new Vector2(-14f, 0f);
        btnRect.sizeDelta = new Vector2(56f, 50f);

        menuToggleBg = btnObj.GetComponent<Image>();
        menuToggleBg.color = MenuButtonBgColor;
        menuToggleBg.raycastTarget = true;

        Outline btnOutline = btnObj.GetComponent<Outline>();
        btnOutline.effectColor = TopBarAccentColor;
        btnOutline.effectDistance = new Vector2(1.6f, -1.6f);

        menuToggleButton = btnObj.GetComponent<Button>();
        menuToggleButton.targetGraphic = menuToggleBg;
        menuToggleButton.colors = CompactBtnColorBlock;
        menuToggleButton.onClick.AddListener(ToggleMenuDrawer);

        CreateHamburgerBar(btnRect, "Bar_Top", 9f);
        CreateHamburgerBar(btnRect, "Bar_Mid", 0f);
        CreateHamburgerBar(btnRect, "Bar_Bot", -9f);

        if (targetCanvas == null)
        {
            return;
        }

        // 2. Scrim transparan klik-luar untuk menutup laci menu dengan 1 ketukan di luar area laci
        menuScrimObj = new GameObject("Btn_CompactMenuOutsideScrim", typeof(RectTransform), typeof(Image), typeof(Button));
        menuScrimObj.transform.SetParent(targetCanvas.transform, false);
        RectTransform scrimRect = menuScrimObj.GetComponent<RectTransform>();
        scrimRect.anchorMin = Vector2.zero;
        scrimRect.anchorMax = Vector2.one;
        scrimRect.offsetMin = Vector2.zero;
        scrimRect.offsetMax = Vector2.zero;

        Image scrimImg = menuScrimObj.GetComponent<Image>();
        scrimImg.color = new Color(0f, 0f, 0f, 0.01f);
        scrimImg.raycastTarget = true;

        menuScrimButton = menuScrimObj.GetComponent<Button>();
        menuScrimButton.targetGraphic = scrimImg;
        menuScrimButton.transition = Selectable.Transition.None;
        menuScrimButton.onClick.AddListener(CloseMenuDrawer);
        menuScrimObj.SetActive(false);

        // 3. Laci popover kompak pojok kanan atas (6 aksi meta)
        menuDrawerObj = new GameObject("Panel_CompactMenuDrawer", typeof(RectTransform), typeof(Image), typeof(Outline));
        menuDrawerObj.transform.SetParent(targetCanvas.transform, false);
        RectTransform drawerRect = menuDrawerObj.GetComponent<RectTransform>();
        drawerRect.anchorMin = new Vector2(1f, 1f);
        drawerRect.anchorMax = new Vector2(1f, 1f);
        drawerRect.pivot = new Vector2(1f, 1f);
        drawerRect.anchoredPosition = new Vector2(-14f, -(TopBarHeight + 8f));
        drawerRect.sizeDelta = new Vector2(268f, 344f);

        Image drawerBg = menuDrawerObj.GetComponent<Image>();
        drawerBg.color = MenuDrawerBgColor;
        drawerBg.raycastTarget = true;

        Outline drawerOutline = menuDrawerObj.GetComponent<Outline>();
        drawerOutline.effectColor = TopBarAccentColor;
        drawerOutline.effectDistance = new Vector2(2f, -2f);

        float itemY = -12f;
        const float itemStep = 54f;

        drawerModeBtn = CreateDrawerItem("Item_ToggleMode", drawerRect, itemY, "Mode: Udara (v7)", DrawerItemBgColor, OnDrawerModeClicked, out drawerModeBg, out txtDrawerMode);
        itemY -= itemStep;
        drawerLevelBtn = CreateDrawerItem("Item_SelectLevel", drawerRect, itemY, "Pilih Level / Sandbox", DrawerItemBgColor, OnDrawerLevelClicked, out _, out _);
        itemY -= itemStep;
        drawerResetBtn = CreateDrawerItem("Item_ResetLevel", drawerRect, itemY, "Reset Level", DrawerItemResetBgColor, OnDrawerResetClicked, out _, out txtDrawerReset);
        itemY -= itemStep;
        drawerReportBtn = CreateDrawerItem("Item_CompetencyReport", drawerRect, itemY, "Rapor CP", DrawerItemBgColor, OnDrawerReportClicked, out _, out _);
        itemY -= itemStep;
        drawerHintBtn = CreateDrawerItem("Item_Hint", drawerRect, itemY, "Petunjuk (0/3)", DrawerItemBgColor, OnDrawerHintClicked, out _, out txtDrawerHint);
        itemY -= itemStep;
        drawerAudioBtn = CreateDrawerItem("Item_AudioToggle", drawerRect, itemY, "Audio: ON", DrawerItemBgColor, OnDrawerAudioClicked, out drawerAudioBg, out txtDrawerAudio);

        menuDrawerObj.SetActive(false);
    }

    private static void CreateHamburgerBar(RectTransform parent, string name, float yOffset)
    {
        GameObject barObj = new GameObject(name, typeof(RectTransform), typeof(Image));
        barObj.transform.SetParent(parent, false);
        RectTransform rect = barObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, yOffset);
        rect.sizeDelta = new Vector2(26f, 3.2f);

        Image img = barObj.GetComponent<Image>();
        img.color = new Color(0.90f, 0.98f, 1.00f, 1f);
        img.raycastTarget = false;
    }

    private Button CreateDrawerItem(
        string name,
        RectTransform parent,
        float topY,
        string initialLabel,
        Color bgColor,
        UnityEngine.Events.UnityAction onClick,
        out Image bgImg,
        out TMP_Text labelTmp)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, topY);
        rect.sizeDelta = new Vector2(-24f, 46f);

        bgImg = obj.GetComponent<Image>();
        bgImg.color = bgColor;
        bgImg.raycastTarget = true;

        Outline outline = obj.GetComponent<Outline>();
        outline.effectColor = new Color(0.24f, 0.64f, 0.80f, 0.65f);
        outline.effectDistance = new Vector2(1.2f, -1.2f);

        Button btn = obj.GetComponent<Button>();
        btn.targetGraphic = bgImg;
        btn.colors = CompactBtnColorBlock;
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
        tmp.text = initialLabel;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 13f;
        tmp.fontSizeMax = 18f;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        labelTmp = tmp;

        return btn;
    }

    private void ConfigureMinimalistBottomControls()
    {
        StyleBottomCornerButton(btnInputA, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 16f), new Vector2(212f, 54f), InputACyanOutlineColor, overrideBg: false, Color.clear, isInputCapsule: true);
        StyleBottomCornerButton(btnInputB, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(240f, 16f), new Vector2(212f, 54f), InputBGoldOutlineColor, overrideBg: false, Color.clear, isInputCapsule: true);
        AddButtonIdentityStrip(btnInputA, "IdentityStrip_A", InputACyanOutlineColor);
        AddButtonIdentityStrip(btnInputB, "IdentityStrip_B", InputBGoldOutlineColor);
        StyleBottomCornerButton(btnVerify, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-18f, 16f), new Vector2(198f, 54f), TopBarAccentColor, overrideBg: true, CardBottomVerifyBgColor, isInputCapsule: false);
        StyleBottomCornerButton(btnNextLevel, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-18f, 16f), new Vector2(212f, 54f), new Color(0.28f, 0.96f, 0.64f, 0.96f), overrideBg: true, CardBottomNextBgColor, isInputCapsule: false);
    }

    private static void AddButtonIdentityStrip(Button btn, string name, Color stripColor)
    {
        if (btn == null)
        {
            return;
        }

        GameObject stripObj = new GameObject(name, typeof(RectTransform), typeof(Image));
        stripObj.transform.SetParent(btn.transform, false);
        RectTransform rect = stripObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(7f, 0f);

        Image img = stripObj.GetComponent<Image>();
        img.color = stripColor;
        img.raycastTarget = false;
    }

    private static void StyleBottomCornerButton(
        Button btn,
        Vector2 anchor,
        Vector2 pivot,
        Vector2 anchoredPos,
        Vector2 sizeDelta,
        Color outlineColor,
        bool overrideBg,
        Color bgColor,
        bool isInputCapsule)
    {
        if (btn == null)
        {
            return;
        }

        RectTransform rect = btn.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;
        }

        Image img = btn.GetComponent<Image>();
        if (img != null)
        {
            if (overrideBg)
            {
                img.color = bgColor;
            }
            else
            {
                Color c = img.color;
                c.a = 0.86f;
                img.color = c;
            }

            Outline outline = img.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = outlineColor;
            }
        }

        TMP_Text label = btn.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.fontSizeMin = isInputCapsule ? 12f : 13f;
            label.fontSizeMax = isInputCapsule ? 18f : 19f;
            if (isInputCapsule)
            {
                label.margin = new Vector4(14f, 2f, 8f, 2f);
            }
        }
    }

    private void ConfigureFeedbackToastLayout()
    {
        if (txtFeedback == null)
        {
            return;
        }

        float toastWidth = isAirWeavingActive ? 1140f : 920f;
        const float toastHeight = 62f;

        RectTransform fbRect = txtFeedback.rectTransform;
        fbRect.anchorMin = new Vector2(0.5f, 0f);
        fbRect.anchorMax = new Vector2(0.5f, 0f);
        fbRect.pivot = new Vector2(0.5f, 0f);
        fbRect.anchoredPosition = new Vector2(0f, 16f);
        fbRect.sizeDelta = new Vector2(toastWidth, toastHeight);
        txtFeedback.alignment = TextAlignmentOptions.Center;
        txtFeedback.textWrappingMode = TextWrappingModes.Normal;
        txtFeedback.enableAutoSizing = true;
        txtFeedback.fontSizeMin = 17f;
        txtFeedback.fontSizeMax = 25f;
        txtFeedback.margin = new Vector4(18f, 4f, 18f, 4f);

        if (feedbackBackdropObj != null)
        {
            RectTransform bgRect = feedbackBackdropObj.GetComponent<RectTransform>();
            if (bgRect != null)
            {
                bgRect.anchorMin = fbRect.anchorMin;
                bgRect.anchorMax = fbRect.anchorMax;
                bgRect.pivot = fbRect.pivot;
                bgRect.anchoredPosition = new Vector2(0f, 13f);
                bgRect.sizeDelta = new Vector2(toastWidth + 28f, toastHeight + 8f);
            }
        }
    }

    private void ApplyToastStyleForMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        bool isFailure = message.StartsWith("Gagal", StringComparison.Ordinal);
        bool isSuccess = message.StartsWith("BERHASIL", StringComparison.Ordinal)
                         || message.StartsWith("SELAMAT", StringComparison.Ordinal);
        bool isHint = message.StartsWith("[Petunjuk", StringComparison.Ordinal);

        if (feedbackBackdropImage != null)
        {
            if (isFailure) feedbackBackdropImage.color = ToastFailureBgColor;
            else if (isSuccess) feedbackBackdropImage.color = ToastSuccessBgColor;
            else if (isHint) feedbackBackdropImage.color = ToastHintBgColor;
            else feedbackBackdropImage.color = ToastDefaultBgColor;
        }

        if (feedbackBackdropOutline != null)
        {
            if (isFailure)
            {
                feedbackBackdropOutline.effectColor = ToastFailureOutlineColor;
                feedbackBackdropOutline.effectDistance = new Vector2(2.5f, -2.5f);
            }
            else if (isSuccess)
            {
                feedbackBackdropOutline.effectColor = ToastSuccessOutlineColor;
                feedbackBackdropOutline.effectDistance = new Vector2(2.2f, -2.2f);
            }
            else if (isHint)
            {
                feedbackBackdropOutline.effectColor = ToastHintOutlineColor;
                feedbackBackdropOutline.effectDistance = new Vector2(2.2f, -2.2f);
            }
            else
            {
                feedbackBackdropOutline.effectColor = ToastDefaultOutlineColor;
                feedbackBackdropOutline.effectDistance = new Vector2(1.8f, -1.8f);
            }
        }

        if (txtFeedback != null)
        {
            if (isFailure) txtFeedback.color = ToastFailureTextColor;
            else if (isSuccess) txtFeedback.color = ToastSuccessTextColor;
        }
    }

    private void RefreshDrawerItemLabels()
    {
        if (manager == null)
        {
            return;
        }

        if (txtDrawerMode != null)
        {
            txtDrawerMode.text = isAirWeavingActive ? "Mode: Kartu (v5)" : "Mode: Udara (v7)";
        }
        if (drawerModeBg != null)
        {
            drawerModeBg.color = isAirWeavingActive ? DrawerItemModeActiveBgColor : DrawerItemBgColor;
        }

        if (txtDrawerReset != null)
        {
            txtDrawerReset.text = (manager.IsSandboxMode || manager.IsAwaitingSandboxTransition)
                ? "Main Ulang (Level 1)"
                : "Reset Level";
        }

        if (txtDrawerHint != null)
        {
            txtDrawerHint.text = manager.IsSandboxMode
                ? "Petunjuk (Sandbox)"
                : $"Petunjuk ({Mathf.Clamp(manager.CurrentHintTier, 0, 3)}/3)";
        }

        if (txtDrawerAudio != null)
        {
            txtDrawerAudio.text = manager.isAudioMuted ? "Audio: OFF" : "Audio: ON";
        }
        if (drawerAudioBg != null)
        {
            drawerAudioBg.color = manager.isAudioMuted ? DrawerItemMutedBgColor : DrawerItemBgColor;
        }
    }

    private void SetFeedbackToastVisible(bool visible)
    {
        if (txtFeedback != null && txtFeedback.enabled != visible)
        {
            txtFeedback.enabled = visible;
        }
        if (feedbackBackdropObj != null && feedbackBackdropObj.activeSelf != visible)
        {
            feedbackBackdropObj.SetActive(visible);
        }
    }

    // =========================================================================
    // DRAWER ITEM CLICK HANDLERS
    // =========================================================================

    private void OnDrawerModeClicked()
    {
        CloseMenuDrawer();
        if (manager != null)
        {
            manager.TriggerToggleAirWeavingFromMenu();
        }
    }

    private void OnDrawerLevelClicked()
    {
        CloseMenuDrawer();
        if (manager != null)
        {
            manager.ToggleLevelSelector();
        }
    }

    private void OnDrawerResetClicked()
    {
        CloseMenuDrawer();
        if (manager != null)
        {
            manager.ResetCurrentLevel();
        }
    }

    private void OnDrawerReportClicked()
    {
        CloseMenuDrawer();
        if (manager != null)
        {
            manager.ToggleCompetencyReport();
        }
    }

    private void OnDrawerHintClicked()
    {
        CloseMenuDrawer();
        if (manager != null)
        {
            manager.OnHintButtonClicked();
        }
    }

    private void OnDrawerAudioClicked()
    {
        if (manager != null)
        {
            manager.ToggleAudioMute();
        }
        RefreshDrawerItemLabels();
    }

    private static void SetObjActive(Component comp, bool active)
    {
        if (comp != null && comp.gameObject.activeSelf != active)
        {
            comp.gameObject.SetActive(active);
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
