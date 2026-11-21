using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Pengendali HUD Floating Glass Modern & Menu Kompak pojok kanan atas (GateWeaver AR v8.0 Tahap 2).
/// - Code-First 100% (0 diff SampleScene.unity): mengganti bilah atas persegi panjang kaku menjadi 3 Floating Glass Pills
///   berbasis sprite 9-slice sudut membulat prosedural (Pil Level berlencana di kiri, Pil Misi adaptif berfont besar di tengah,
///   Pil Kuota + tombol squircle '?' dan '≡' di kanan) yang aman terhadap Safe Area & notch HP 20:9.
/// - Laci menu floating glass bersudut membulat dengan tinggi tombol & tipografi ~28% lebih besar (5 item Mode Udara, 4 item Mode Kartu).
/// - Kontrol bawah Mode Kartu v5 tetap 100% utuh (4 kapsul sudut membulat) dan 0 tombol gameplay di Mode Udara (100% Zero-Touch).
/// - Sistem Toast-Only rounded glass untuk Txt_Feedback (banner status, "Arahkan tangan ke kamera", & banner menang).
/// </summary>
public sealed class CompactHudMenuController : MonoBehaviour
{
    private const float TopHudTopMargin = 10f;
    private const float TopHudZoneHeight = 60f;
    private const float TopPillHeight = 58f;
    private const float TopEdgeMarginX = 16f;
    private const float TopSectionGapX = 12f;
    private const float TopRightBtnGapX = 10f;
    private const float LeftLevelPillWidth = 396f;
    private const float RightQuotaPillWidth = 258f;
    private const float SquircleBtnWidth = 60f;
    private const float SquircleBtnHeight = 58f;

    private const float DrawerWidth = 326f;
    private const float DrawerHeightCardMode = 294f;
    private const float DrawerHeightAirMode = 362f;
    private const float DrawerTopY = -78f;

    private const float ToastDefaultBottomY = 16f;
    private const float ToastPracticeOffsetBottomY = 88f;
    private const float DefaultToastDurationSeconds = 2.6f;
    private const float ImportantToastDurationSeconds = 4.6f;
    private const float FailureToastDurationSeconds = 5.5f;
    private const float HintToastDurationSeconds = 5.5f;

    // Palet Translucent Dark Glass (#08111EE0) & Aksen Bercahaya Halus (v8.0)
    private static readonly Color FloatingGlassBgColor = new Color(0.031f, 0.067f, 0.118f, 0.88f);
    private static readonly Color FloatingGlassBorderColor = new Color(0.20f, 0.88f, 1.00f, 0.76f);
    private static readonly Color MissionGlassBorderColor = new Color(0.24f, 0.92f, 1.00f, 0.86f);
    private static readonly Color TopBarAccentColor = new Color(0.20f, 0.90f, 1.00f, 0.86f);

    private static readonly Color LevelBadgeNormalBgColor = new Color(0.07f, 0.36f, 0.52f, 0.96f);
    private static readonly Color LevelBadgeNormalBorderColor = new Color(0.32f, 0.94f, 1.00f, 0.92f);
    private static readonly Color LevelBadgeSandboxBgColor = new Color(0.06f, 0.42f, 0.34f, 0.96f);
    private static readonly Color LevelBadgeSandboxBorderColor = new Color(0.34f, 1.00f, 0.78f, 0.94f);
    private static readonly Color LevelBadgeTextColor = new Color(0.92f, 0.99f, 1.00f, 1.00f);
    private static readonly Color LevelTitleTextColor = new Color(0.95f, 0.99f, 1.00f, 1.00f);

    private static readonly Color QuotaNormalPillBgColor = new Color(0.031f, 0.067f, 0.118f, 0.88f);
    private static readonly Color QuotaNormalPillBorderColor = new Color(0.20f, 0.88f, 1.00f, 0.76f);
    private static readonly Color QuotaAlertPillBgColor = new Color(0.74f, 0.09f, 0.14f, 0.96f);
    private static readonly Color QuotaAlertPillOutlineColor = new Color(1.00f, 0.56f, 0.36f, 1.00f);
    private static readonly Color QuotaAlertTextColor = new Color(1.00f, 0.97f, 0.92f, 1.00f);

    private static readonly Color ToastDefaultBgColor = new Color(0.031f, 0.067f, 0.118f, 0.93f);
    private static readonly Color ToastDefaultOutlineColor = new Color(0.22f, 0.90f, 1.00f, 0.90f);
    private static readonly Color ToastNoHandBgColor = new Color(0.04f, 0.09f, 0.16f, 0.94f);
    private static readonly Color ToastNoHandOutlineColor = new Color(1.00f, 0.82f, 0.32f, 0.94f);
    private static readonly Color ToastFailureBgColor = new Color(0.38f, 0.05f, 0.09f, 0.97f);
    private static readonly Color ToastFailureOutlineColor = new Color(1.00f, 0.36f, 0.36f, 1.00f);
    private static readonly Color ToastFailureTextColor = new Color(1.00f, 0.96f, 0.93f, 1.00f);
    private static readonly Color ToastSuccessBgColor = new Color(0.04f, 0.27f, 0.16f, 0.96f);
    private static readonly Color ToastSuccessOutlineColor = new Color(0.26f, 1.00f, 0.64f, 1.00f);
    private static readonly Color ToastSuccessTextColor = new Color(0.94f, 1.00f, 0.96f, 1.00f);
    private static readonly Color ToastHintBgColor = new Color(0.07f, 0.15f, 0.29f, 0.96f);
    private static readonly Color ToastHintOutlineColor = new Color(1.00f, 0.84f, 0.28f, 0.98f);

    private static readonly Color MenuButtonBgColor = new Color(0.05f, 0.16f, 0.27f, 0.92f);
    private static readonly Color MenuButtonOpenBgColor = new Color(0.11f, 0.44f, 0.58f, 0.98f);
    private static readonly Color MenuDrawerBgColor = new Color(0.031f, 0.067f, 0.118f, 0.96f);
    private static readonly Color MenuDrawerBorderColor = new Color(0.22f, 0.88f, 1.00f, 0.84f);

    private static readonly Color DrawerItemModeCardBgColor = new Color(0.08f, 0.27f, 0.42f, 0.96f);
    private static readonly Color DrawerItemModeCardBorderColor = new Color(0.28f, 0.88f, 1.00f, 0.85f);
    private static readonly Color DrawerItemModeAirBgColor = new Color(0.06f, 0.36f, 0.30f, 0.96f);
    private static readonly Color DrawerItemModeAirBorderColor = new Color(0.28f, 0.98f, 0.76f, 0.88f);
    private static readonly Color DrawerItemLevelBgColor = new Color(0.09f, 0.20f, 0.33f, 0.96f);
    private static readonly Color DrawerItemLevelBorderColor = new Color(0.26f, 0.74f, 0.92f, 0.76f);
    private static readonly Color DrawerItemResetBgColor = new Color(0.27f, 0.13f, 0.16f, 0.96f);
    private static readonly Color DrawerItemResetBorderColor = new Color(0.96f, 0.46f, 0.38f, 0.80f);
    private static readonly Color DrawerItemAudioOnBgColor = new Color(0.08f, 0.22f, 0.32f, 0.96f);
    private static readonly Color DrawerItemAudioOnBorderColor = new Color(0.26f, 0.78f, 0.88f, 0.76f);
    private static readonly Color DrawerItemMutedBgColor = new Color(0.27f, 0.16f, 0.11f, 0.96f);
    private static readonly Color DrawerItemMutedBorderColor = new Color(0.94f, 0.64f, 0.28f, 0.82f);
    private static readonly Color DrawerItemGuideBgColor = new Color(0.08f, 0.31f, 0.45f, 0.96f);
    private static readonly Color DrawerItemGuideBorderColor = new Color(0.30f, 0.92f, 1.00f, 0.86f);

    private static readonly Color InputACyanOutlineColor = new Color(0.18f, 0.86f, 1.00f, 0.96f);
    private static readonly Color InputBGoldOutlineColor = new Color(1.00f, 0.84f, 0.22f, 0.96f);
    private static readonly Color CardBottomVerifyBgColor = new Color(0.06f, 0.38f, 0.54f, 0.86f);
    private static readonly Color CardBottomNextBgColor = new Color(0.07f, 0.46f, 0.30f, 0.90f);

    private static readonly string[] CachedLevelBadgeTexts =
    {
        "LVL 1/7", "LVL 2/7", "LVL 3/7", "LVL 4/7", "LVL 5/7", "LVL 6/7", "LVL 7/7"
    };

    private static readonly string[] CachedShortLevelTitles =
    {
        "Alarm Pintu & Jendela",
        "Sakelar Tangga Dua Arah",
        "Kunci Keamanan Brankas",
        "Pemutus Darurat Reaktor",
        "Sinkronisasi Inti Kembar",
        "Sensor Hening Server (NOR)",
        "Kunci Biometrik De Morgan"
    };

    private const string SandboxBadgeText = "SANDBOX";
    private const string SandboxShortTitle = "Laboratorium Bebas";

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

    // =========================================================================
    // GENERATOR SPRITE 9-SLICE SUDUT MEMBULAT PROSEDURAL (CACHED, ZERO-GC)
    // =========================================================================

    private static Texture2D cachedPillTex;
    private static Sprite cachedPillSprite;
    private static Texture2D cachedPillBorderTex;
    private static Sprite cachedPillBorderSprite;

    private static Texture2D cachedCardTex;
    private static Sprite cachedCardSprite;
    private static Texture2D cachedCardBorderTex;
    private static Sprite cachedCardBorderSprite;

    private static Texture2D cachedSquircleTex;
    private static Sprite cachedSquircleSprite;
    private static Texture2D cachedSquircleBorderTex;
    private static Sprite cachedSquircleBorderSprite;

    public static Sprite GetRoundedPillSprite()
    {
        if (cachedPillSprite == null)
        {
            cachedPillSprite = CreateNineSliceRoundedSprite("GW_RoundedPill_9S", 64, 24f, 26f, isBorderRing: false, 0f, out cachedPillTex);
        }
        return cachedPillSprite;
    }

    public static Sprite GetRoundedPillBorderSprite()
    {
        if (cachedPillBorderSprite == null)
        {
            cachedPillBorderSprite = CreateNineSliceRoundedSprite("GW_RoundedPillBorder_9S", 64, 24f, 26f, isBorderRing: true, 2.1f, out cachedPillBorderTex);
        }
        return cachedPillBorderSprite;
    }

    public static Sprite GetRoundedCardSprite()
    {
        if (cachedCardSprite == null)
        {
            cachedCardSprite = CreateNineSliceRoundedSprite("GW_RoundedCard_9S", 64, 18f, 20f, isBorderRing: false, 0f, out cachedCardTex);
        }
        return cachedCardSprite;
    }

    public static Sprite GetRoundedCardBorderSprite()
    {
        if (cachedCardBorderSprite == null)
        {
            cachedCardBorderSprite = CreateNineSliceRoundedSprite("GW_RoundedCardBorder_9S", 64, 18f, 20f, isBorderRing: true, 2.2f, out cachedCardBorderTex);
        }
        return cachedCardBorderSprite;
    }

    public static Sprite GetRoundedSquircleSprite()
    {
        if (cachedSquircleSprite == null)
        {
            cachedSquircleSprite = CreateNineSliceRoundedSprite("GW_RoundedSquircle_9S", 48, 13f, 15f, isBorderRing: false, 0f, out cachedSquircleTex);
        }
        return cachedSquircleSprite;
    }

    public static Sprite GetRoundedSquircleBorderSprite()
    {
        if (cachedSquircleBorderSprite == null)
        {
            cachedSquircleBorderSprite = CreateNineSliceRoundedSprite("GW_RoundedSquircleBorder_9S", 48, 13f, 15f, isBorderRing: true, 2.0f, out cachedSquircleBorderTex);
        }
        return cachedSquircleBorderSprite;
    }

    public static void ApplyRoundedPill(Image img)
    {
        if (img == null) return;
        img.sprite = GetRoundedPillSprite();
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1f;
    }

    public static void ApplyRoundedCard(Image img)
    {
        if (img == null) return;
        img.sprite = GetRoundedCardSprite();
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1f;
    }

    public static void ApplyRoundedSquircle(Image img)
    {
        if (img == null) return;
        img.sprite = GetRoundedSquircleSprite();
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1f;
    }

    public static Image AttachRoundedBorderOverlay(RectTransform parent, string name, Sprite borderSprite, Color borderColor)
    {
        if (parent == null) return null;
        Transform existing = parent.Find(name);
        GameObject borderObj = existing != null
            ? existing.gameObject
            : new GameObject(name, typeof(RectTransform), typeof(Image));
        if (existing == null)
        {
            borderObj.transform.SetParent(parent, false);
        }

        RectTransform rect = borderObj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image img = borderObj.GetComponent<Image>();
        img.sprite = borderSprite;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1f;
        img.color = borderColor;
        img.raycastTarget = false;
        return img;
    }

    private static Sprite CreateNineSliceRoundedSprite(
        string name,
        int size,
        float radiusPx,
        float borderPx,
        bool isBorderRing,
        float ringThicknessPx,
        out Texture2D tex)
    {
        tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
        {
            name = name + "_Tex",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color32[] pixels = new Color32[size * size];
        float half = size * 0.5f;
        float innerHalf = Mathf.Max(1f, half - radiusPx - 1.0f);

        for (int y = 0; y < size; y++)
        {
            float py = Mathf.Abs((y + 0.5f) - half);
            float qy = Mathf.Max(py - innerHalf, 0f);
            int rowOffset = y * size;

            for (int x = 0; x < size; x++)
            {
                float px = Mathf.Abs((x + 0.5f) - half);
                float qx = Mathf.Max(px - innerHalf, 0f);
                float dist = Mathf.Sqrt((qx * qx) + (qy * qy)) - radiusPx;

                float alpha;
                if (!isBorderRing)
                {
                    alpha = Mathf.Clamp01(0.5f - (dist / 1.15f));
                }
                else
                {
                    float outerAlpha = Mathf.Clamp01(0.5f - (dist / 1.10f));
                    float innerAlpha = Mathf.Clamp01(0.5f + ((dist + ringThicknessPx) / 1.10f));
                    alpha = outerAlpha * innerAlpha;
                }

                byte aByte = (byte)Mathf.RoundToInt(alpha * 255f);
                pixels[rowOffset + x] = new Color32(255, 255, 255, aByte);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

        Vector4 border = new Vector4(borderPx, borderPx, borderPx, borderPx);
        Sprite sprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            border);
        sprite.name = name;
        return sprite;
    }

    // =========================================================================
    // STATE & REFERENCES
    // =========================================================================

    private LevelAndUIManager manager;
    private Canvas targetCanvas;
    private TMP_FontAsset safeFont;

    private RectTransform topBarRect;
    private GameObject levelPillObj;
    private RectTransform levelPillRect;
    private Image levelBadgeBg;
    private Image levelBadgeBorderImg;
    private TMP_Text txtLevelBadge;
    private TMP_Text txtLevelTitle;

    private GameObject missionPillObj;
    private RectTransform missionPillRect;
    private TMP_Text txtMissionDesc;

    private TMP_Text txtQuotaStatus;
    private GameObject quotaAlertPillObj;
    private RectTransform quotaAlertPillRect;
    private Image quotaPillBg;
    private Image quotaPillBorderImg;
    private Outline quotaPillOutline;

    private TMP_Text txtFeedback;
    private GameObject feedbackBackdropObj;
    private Image feedbackBackdropImage;
    private Outline feedbackBackdropOutline;
    private Image feedbackBackdropBorderImg;

    private GameObject legacyScannerPanelObj;
    private GameObject legacyResetBtnObj;
    private GameObject legacyLevelSelectorBtnObj;
    private GameObject legacyAirWeavingBtnObj;

    private Button btnInputA;
    private Button btnInputB;
    private Button btnVerify;
    private Button btnNextLevel;

    private Button menuToggleButton;
    private RectTransform menuToggleRect;
    private Image menuToggleBg;
    private GameObject guideToggleBtnObj;
    private RectTransform guideToggleRect;
    private Button guideToggleButton;
    private Image guideToggleBg;
    private GameObject menuScrimObj;
    private Button menuScrimButton;
    private GameObject menuDrawerObj;
    private RectTransform menuDrawerRect;

    private Button drawerModeBtn;
    private Image drawerModeBg;
    private Image drawerModeBorderImg;
    private TMP_Text txtDrawerMode;
    private Button drawerLevelBtn;
    private Button drawerResetBtn;
    private TMP_Text txtDrawerReset;
    private Button drawerAudioBtn;
    private Image drawerAudioBg;
    private Image drawerAudioBorderImg;
    private TMP_Text txtDrawerAudio;
    private GameObject drawerGuideBtnObj;
    private Button drawerGuideBtn;

    private bool isInitialized = false;
    private bool isMenuDrawerOpen = false;
    private bool isAirWeavingActive = false;
    private bool isPracticePillBottomOffsetActive = false;
    private bool isQuotaOverAlertActive = false;

    // Safe-area & adaptive mission pill tracking (zero-GC)
    private string lastObservedMissionText;
    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;
    private Rect lastSafeArea;
    private float cachedSafeLeftInsetCanvas = 0f;
    private float cachedSafeRightInsetCanvas = 0f;

    // Toast-Only state (zero-GC)
    private string lastObservedFeedbackText;
    private float toastRemainingSeconds = 0f;
    private bool activeToastIsProtected = false;
    private bool activeToastIsNoHand = false;

    public bool IsMenuDrawerOpen => isMenuDrawerOpen;
    public bool HasActiveProtectedToast => toastRemainingSeconds > 0f && (activeToastIsProtected || !activeToastIsNoHand);
    public Canvas TargetCanvas => targetCanvas;
    public TMP_FontAsset SafeFont => safeFont;

    public event Action OnGestureGuideRequested;
    public event Action OnMenuDrawerOpened;

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
        topBarRect = ReshapeSlimTopBar();
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
        if (!active)
        {
            isPracticePillBottomOffsetActive = false;
            SetGuideButtonActiveState(false);
        }
        if (!isInitialized)
        {
            return;
        }

        CloseMenuDrawer();
        RefreshModeSpecificLayout();
        ConfigureFeedbackToastLayout();
        RefreshAllVisualStates();
    }

    public void SetGuideButtonActiveState(bool isGuideOpen)
    {
        if (guideToggleBg != null)
        {
            guideToggleBg.color = isGuideOpen ? MenuButtonOpenBgColor : MenuButtonBgColor;
        }
    }

    public void SetPracticePillBottomOffset(bool active)
    {
        if (isPracticePillBottomOffsetActive == active)
        {
            return;
        }

        isPracticePillBottomOffsetActive = active;
        if (isInitialized)
        {
            ConfigureFeedbackToastLayout();
        }
    }

    public void RefreshAllVisualStates()
    {
        if (!isInitialized || manager == null)
        {
            return;
        }

        if (legacyScannerPanelObj != null && legacyScannerPanelObj.activeSelf)
        {
            legacyScannerPanelObj.SetActive(false);
        }

        RefreshLevelPillBadgeAndShortTitle();
        RefreshModeSpecificLayout();
        UpdateTopHudAdaptiveLayout(force: true);
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

        // Mode Kartu (Vuforia v5): 4 kapsul ramping semi-transparan; sembunyikan Uji & Lanjut di Sandbox
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
        OnMenuDrawerOpened?.Invoke();

        RefreshModeSpecificLayout();
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
    /// Mengubah gaya Pil Kuota Gerbang antara status normal (translucent dark glass) dan over-quota (merah terang).
    /// </summary>
    public void SetQuotaAlertState(bool isOverQuota)
    {
        isQuotaOverAlertActive = isOverQuota;

        if (quotaAlertPillObj != null && !quotaAlertPillObj.activeSelf)
        {
            quotaAlertPillObj.SetActive(true);
        }

        if (quotaPillBg != null)
        {
            quotaPillBg.color = isOverQuota ? QuotaAlertPillBgColor : QuotaNormalPillBgColor;
        }

        if (quotaPillBorderImg != null)
        {
            quotaPillBorderImg.color = isOverQuota ? QuotaAlertPillOutlineColor : QuotaNormalPillBorderColor;
        }

        if (quotaPillOutline != null)
        {
            quotaPillOutline.effectColor = isOverQuota ? QuotaAlertPillOutlineColor : new Color(0.18f, 0.86f, 1.00f, 0.38f);
            quotaPillOutline.effectDistance = isOverQuota ? new Vector2(2.0f, -2.0f) : new Vector2(1.2f, -1.2f);
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
        ApplyToastStyleForMessage(message, isNoHandPrompt);
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
    /// Memantau perubahan langsung pada txtFeedback.text, menyesuaikan lebar adaptif Pil Misi & Safe Area,
    /// serta mengelola auto-hide toast secara zero-GC.
    /// </summary>
    public void TickToast(float unscaledDt, bool isVerifying)
    {
        if (!isInitialized)
        {
            return;
        }

        UpdateTopHudAdaptiveLayout(force: false);

        if (txtFeedback == null)
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
                ApplyToastStyleForMessage(currentText, isNoHandPrompt: false);

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
        RemoveBtnListener(guideToggleButton, OnTopBarGuideClicked);
        RemoveBtnListener(menuScrimButton, CloseMenuDrawer);
        RemoveBtnListener(drawerModeBtn, OnDrawerModeClicked);
        RemoveBtnListener(drawerLevelBtn, OnDrawerLevelClicked);
        RemoveBtnListener(drawerResetBtn, OnDrawerResetClicked);
        RemoveBtnListener(drawerAudioBtn, OnDrawerAudioClicked);
        RemoveBtnListener(drawerGuideBtn, OnDrawerGuideClicked);

        if (guideToggleBtnObj != null)
        {
            Destroy(guideToggleBtnObj);
            guideToggleBtnObj = null;
        }
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
            if (txtQuotaStatus != null && topBarRect != null && txtQuotaStatus.transform.parent == quotaAlertPillObj.transform)
            {
                txtQuotaStatus.transform.SetParent(topBarRect, false);
            }
            Destroy(quotaAlertPillObj);
            quotaAlertPillObj = null;
        }
        if (levelPillObj != null)
        {
            if (txtLevelTitle != null && topBarRect != null && txtLevelTitle.transform.parent == levelPillObj.transform)
            {
                txtLevelTitle.transform.SetParent(topBarRect, false);
            }
            Destroy(levelPillObj);
            levelPillObj = null;
        }
        if (missionPillObj != null)
        {
            if (txtMissionDesc != null && topBarRect != null && txtMissionDesc.transform.parent == missionPillObj.transform)
            {
                txtMissionDesc.transform.SetParent(topBarRect, false);
            }
            Destroy(missionPillObj);
            missionPillObj = null;
        }

        topBarRect = null;
        levelPillRect = null;
        levelBadgeBg = null;
        levelBadgeBorderImg = null;
        txtLevelBadge = null;
        missionPillRect = null;
        quotaAlertPillRect = null;
        quotaPillBg = null;
        quotaPillBorderImg = null;
        quotaPillOutline = null;
        menuToggleRect = null;
        guideToggleRect = null;
        guideToggleButton = null;
        guideToggleBg = null;
        menuDrawerRect = null;
        drawerModeBorderImg = null;
        drawerAudioBorderImg = null;
        drawerGuideBtnObj = null;
        drawerGuideBtn = null;
        feedbackBackdropImage = null;
        feedbackBackdropOutline = null;
        feedbackBackdropBorderImg = null;
    }

    // =========================================================================
    // CODE-FIRST FLOATING GLASS PILLS & COMPACT MENU CONSTRUCTION
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
        topRect.anchoredPosition = new Vector2(0f, -TopHudTopMargin);
        topRect.sizeDelta = new Vector2(0f, TopHudZoneHeight);

        // Matikan latar persegi panjang penuh agar berganti menjadi Floating Glass Pills
        Image topBg = topPanelObj.GetComponent<Image>();
        if (topBg != null)
        {
            topBg.enabled = false;
            topBg.raycastTarget = false;
        }

        // 1. KIRI: Pil Level Ringkas (Lencana Level Tebal + Judul Level Kontras Tinggi)
        levelPillObj = new GameObject("Pill_TopLevel", typeof(RectTransform), typeof(Image));
        levelPillObj.transform.SetParent(topRect, false);
        levelPillRect = levelPillObj.GetComponent<RectTransform>();
        levelPillRect.anchorMin = new Vector2(0f, 0.5f);
        levelPillRect.anchorMax = new Vector2(0f, 0.5f);
        levelPillRect.pivot = new Vector2(0f, 0.5f);
        levelPillRect.anchoredPosition = new Vector2(TopEdgeMarginX, 0f);
        levelPillRect.sizeDelta = new Vector2(LeftLevelPillWidth, TopPillHeight);

        Image levelPillBg = levelPillObj.GetComponent<Image>();
        ApplyRoundedPill(levelPillBg);
        levelPillBg.color = FloatingGlassBgColor;
        levelPillBg.raycastTarget = false;
        AttachRoundedBorderOverlay(levelPillRect, "Pill_TopLevel_Border", GetRoundedPillBorderSprite(), FloatingGlassBorderColor);

        GameObject badgeObj = new GameObject("Badge_LevelTag", typeof(RectTransform), typeof(Image));
        badgeObj.transform.SetParent(levelPillRect, false);
        RectTransform badgeRect = badgeObj.GetComponent<RectTransform>();
        badgeRect.anchorMin = new Vector2(0f, 0.5f);
        badgeRect.anchorMax = new Vector2(0f, 0.5f);
        badgeRect.pivot = new Vector2(0f, 0.5f);
        badgeRect.anchoredPosition = new Vector2(8f, 0f);
        badgeRect.sizeDelta = new Vector2(96f, 42f);

        levelBadgeBg = badgeObj.GetComponent<Image>();
        ApplyRoundedSquircle(levelBadgeBg);
        levelBadgeBg.color = LevelBadgeNormalBgColor;
        levelBadgeBg.raycastTarget = false;
        levelBadgeBorderImg = AttachRoundedBorderOverlay(badgeRect, "Badge_LevelTag_Border", GetRoundedSquircleBorderSprite(), LevelBadgeNormalBorderColor);

        GameObject badgeTxtObj = new GameObject("Txt_LevelBadge", typeof(RectTransform), typeof(TextMeshProUGUI));
        badgeTxtObj.transform.SetParent(badgeRect, false);
        RectTransform badgeTxtRect = badgeTxtObj.GetComponent<RectTransform>();
        badgeTxtRect.anchorMin = Vector2.zero;
        badgeTxtRect.anchorMax = Vector2.one;
        badgeTxtRect.offsetMin = new Vector2(6f, 2f);
        badgeTxtRect.offsetMax = new Vector2(-6f, -2f);

        TextMeshProUGUI badgeTmp = badgeTxtObj.GetComponent<TextMeshProUGUI>();
        if (safeFont != null)
        {
            badgeTmp.font = safeFont;
        }
        badgeTmp.text = CachedLevelBadgeTexts[0];
        badgeTmp.fontStyle = FontStyles.Bold;
        badgeTmp.alignment = TextAlignmentOptions.Center;
        badgeTmp.color = LevelBadgeTextColor;
        badgeTmp.enableAutoSizing = true;
        badgeTmp.fontSizeMin = 14f;
        badgeTmp.fontSizeMax = 18.5f;
        badgeTmp.textWrappingMode = TextWrappingModes.NoWrap;
        badgeTmp.raycastTarget = false;
        txtLevelBadge = badgeTmp;

        if (txtLevelTitle != null)
        {
            RectTransform titleRect = txtLevelTitle.rectTransform;
            titleRect.SetParent(levelPillRect, false);
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            titleRect.pivot = new Vector2(0f, 0.5f);
            titleRect.offsetMin = new Vector2(114f, 4f);
            titleRect.offsetMax = new Vector2(-16f, -4f);
            txtLevelTitle.fontStyle = FontStyles.Bold;
            txtLevelTitle.color = LevelTitleTextColor;
            txtLevelTitle.alignment = TextAlignmentOptions.MidlineLeft;
            txtLevelTitle.textWrappingMode = TextWrappingModes.NoWrap;
            txtLevelTitle.enableAutoSizing = true;
            txtLevelTitle.fontSizeMin = 15.5f;
            txtLevelTitle.fontSizeMax = 23.5f;
            txtLevelTitle.raycastTarget = false;
        }

        // 2. TENGAH: Pil Misi 1 Baris Adaptif (~38% Font Lebih Besar, Tebal, Kontras Tinggi)
        missionPillObj = new GameObject("Pill_TopMission", typeof(RectTransform), typeof(Image));
        missionPillObj.transform.SetParent(topRect, false);
        missionPillRect = missionPillObj.GetComponent<RectTransform>();
        missionPillRect.anchorMin = new Vector2(0.5f, 0.5f);
        missionPillRect.anchorMax = new Vector2(0.5f, 0.5f);
        missionPillRect.pivot = new Vector2(0.5f, 0.5f);
        missionPillRect.anchoredPosition = Vector2.zero;
        missionPillRect.sizeDelta = new Vector2(920f, TopPillHeight);

        Image missionPillBg = missionPillObj.GetComponent<Image>();
        ApplyRoundedPill(missionPillBg);
        missionPillBg.color = FloatingGlassBgColor;
        missionPillBg.raycastTarget = false;
        AttachRoundedBorderOverlay(missionPillRect, "Pill_TopMission_Border", GetRoundedPillBorderSprite(), MissionGlassBorderColor);

        if (txtMissionDesc != null)
        {
            RectTransform missionRect = txtMissionDesc.rectTransform;
            missionRect.SetParent(missionPillRect, false);
            missionRect.anchorMin = Vector2.zero;
            missionRect.anchorMax = Vector2.one;
            missionRect.pivot = new Vector2(0.5f, 0.5f);
            missionRect.offsetMin = new Vector2(24f, 4f);
            missionRect.offsetMax = new Vector2(-24f, -4f);
            txtMissionDesc.fontStyle = FontStyles.Bold;
            txtMissionDesc.alignment = TextAlignmentOptions.Center;
            txtMissionDesc.textWrappingMode = TextWrappingModes.NoWrap;
            txtMissionDesc.enableAutoSizing = true;
            txtMissionDesc.fontSizeMin = 19f;
            txtMissionDesc.fontSizeMax = 30.5f;
            txtMissionDesc.lineSpacing = 0f;
            txtMissionDesc.raycastTarget = false;
        }

        // 3. KANAN: Pil Kuota Gerbang Berfont Besar (selalu aktif sebagai pil floating glass)
        if (txtQuotaStatus != null)
        {
            quotaAlertPillObj = new GameObject("Quota_AlertPill", typeof(RectTransform), typeof(Image), typeof(Outline));
            quotaAlertPillObj.transform.SetParent(topRect, false);
            RectTransform pillRect = quotaAlertPillObj.GetComponent<RectTransform>();
            quotaAlertPillRect = pillRect;
            pillRect.anchorMin = new Vector2(1f, 0.5f);
            pillRect.anchorMax = new Vector2(1f, 0.5f);
            pillRect.pivot = new Vector2(1f, 0.5f);
            pillRect.anchoredPosition = new Vector2(-(TopEdgeMarginX + SquircleBtnWidth + TopRightBtnGapX), 0f);
            pillRect.sizeDelta = new Vector2(RightQuotaPillWidth, TopPillHeight);

            quotaPillBg = quotaAlertPillObj.GetComponent<Image>();
            ApplyRoundedPill(quotaPillBg);
            quotaPillBg.color = QuotaNormalPillBgColor;
            quotaPillBg.raycastTarget = false;

            quotaPillOutline = quotaAlertPillObj.GetComponent<Outline>();
            quotaPillOutline.effectColor = new Color(0.18f, 0.86f, 1.00f, 0.38f);
            quotaPillOutline.effectDistance = new Vector2(1.2f, -1.2f);

            quotaPillBorderImg = AttachRoundedBorderOverlay(pillRect, "Quota_AlertPill_Border", GetRoundedPillBorderSprite(), QuotaNormalPillBorderColor);

            RectTransform quotaRect = txtQuotaStatus.rectTransform;
            quotaRect.SetParent(pillRect, false);
            quotaRect.anchorMin = Vector2.zero;
            quotaRect.anchorMax = Vector2.one;
            quotaRect.pivot = new Vector2(0.5f, 0.5f);
            quotaRect.offsetMin = new Vector2(16f, 4f);
            quotaRect.offsetMax = new Vector2(-16f, -4f);
            txtQuotaStatus.fontStyle = FontStyles.Bold;
            txtQuotaStatus.alignment = TextAlignmentOptions.Center;
            txtQuotaStatus.textWrappingMode = TextWrappingModes.NoWrap;
            txtQuotaStatus.enableAutoSizing = true;
            txtQuotaStatus.fontSizeMin = 17f;
            txtQuotaStatus.fontSizeMax = 25f;
            txtQuotaStatus.raycastTarget = false;
        }

        return topRect;
    }

    private void BuildCompactMenuButtonAndDrawer(RectTransform topRect)
    {
        // 1. Tombol '≡' berbentuk Squircle (60x58 px) dengan 3 garis membulat tegas
        GameObject btnObj = new GameObject("Btn_CompactMenu", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        btnObj.transform.SetParent(topRect, false);
        RectTransform btnRect = btnObj.GetComponent<RectTransform>();
        menuToggleRect = btnRect;
        btnRect.anchorMin = new Vector2(1f, 0.5f);
        btnRect.anchorMax = new Vector2(1f, 0.5f);
        btnRect.pivot = new Vector2(1f, 0.5f);
        btnRect.anchoredPosition = new Vector2(-TopEdgeMarginX, 0f);
        btnRect.sizeDelta = new Vector2(SquircleBtnWidth, SquircleBtnHeight);

        menuToggleBg = btnObj.GetComponent<Image>();
        ApplyRoundedSquircle(menuToggleBg);
        menuToggleBg.color = MenuButtonBgColor;
        menuToggleBg.raycastTarget = true;

        Outline btnOutline = btnObj.GetComponent<Outline>();
        btnOutline.effectColor = new Color(0.20f, 0.88f, 1.00f, 0.45f);
        btnOutline.effectDistance = new Vector2(1.4f, -1.4f);

        AttachRoundedBorderOverlay(btnRect, "Btn_CompactMenu_Border", GetRoundedSquircleBorderSprite(), TopBarAccentColor);

        menuToggleButton = btnObj.GetComponent<Button>();
        menuToggleButton.targetGraphic = menuToggleBg;
        menuToggleButton.colors = CompactBtnColorBlock;
        menuToggleButton.onClick.AddListener(ToggleMenuDrawer);

        CreateHamburgerBar(btnRect, "Bar_Top", 10f);
        CreateHamburgerBar(btnRect, "Bar_Mid", 0f);
        CreateHamburgerBar(btnRect, "Bar_Bot", -10f);

        // 1B. Tombol '?' berbentuk Squircle (60x58 px) di sebelah kiri tombol menu (khusus Mode Udara)
        guideToggleBtnObj = new GameObject("Btn_AirGestureGuide", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        guideToggleBtnObj.transform.SetParent(topRect, false);
        RectTransform guideBtnRect = guideToggleBtnObj.GetComponent<RectTransform>();
        guideToggleRect = guideBtnRect;
        guideBtnRect.anchorMin = new Vector2(1f, 0.5f);
        guideBtnRect.anchorMax = new Vector2(1f, 0.5f);
        guideBtnRect.pivot = new Vector2(1f, 0.5f);
        guideBtnRect.anchoredPosition = new Vector2(-(TopEdgeMarginX + SquircleBtnWidth + TopRightBtnGapX), 0f);
        guideBtnRect.sizeDelta = new Vector2(SquircleBtnWidth, SquircleBtnHeight);

        guideToggleBg = guideToggleBtnObj.GetComponent<Image>();
        ApplyRoundedSquircle(guideToggleBg);
        guideToggleBg.color = MenuButtonBgColor;
        guideToggleBg.raycastTarget = true;

        Outline guideBtnOutline = guideToggleBtnObj.GetComponent<Outline>();
        guideBtnOutline.effectColor = new Color(0.20f, 0.88f, 1.00f, 0.45f);
        guideBtnOutline.effectDistance = new Vector2(1.4f, -1.4f);

        AttachRoundedBorderOverlay(guideBtnRect, "Btn_AirGestureGuide_Border", GetRoundedSquircleBorderSprite(), TopBarAccentColor);

        guideToggleButton = guideToggleBtnObj.GetComponent<Button>();
        guideToggleButton.targetGraphic = guideToggleBg;
        guideToggleButton.colors = CompactBtnColorBlock;
        guideToggleButton.onClick.AddListener(OnTopBarGuideClicked);

        GameObject guideTxtObj = new GameObject("Txt_GuideIcon", typeof(RectTransform), typeof(TextMeshProUGUI));
        guideTxtObj.transform.SetParent(guideBtnRect, false);
        RectTransform guideTxtRect = guideTxtObj.GetComponent<RectTransform>();
        guideTxtRect.anchorMin = Vector2.zero;
        guideTxtRect.anchorMax = Vector2.one;
        guideTxtRect.offsetMin = Vector2.zero;
        guideTxtRect.offsetMax = Vector2.zero;

        TextMeshProUGUI guideTmp = guideTxtObj.GetComponent<TextMeshProUGUI>();
        if (safeFont != null)
        {
            guideTmp.font = safeFont;
        }
        guideTmp.text = "?";
        guideTmp.fontStyle = FontStyles.Bold;
        guideTmp.fontSize = 31f;
        guideTmp.alignment = TextAlignmentOptions.Center;
        guideTmp.color = new Color(0.93f, 0.99f, 1.00f, 1f);
        guideTmp.textWrappingMode = TextWrappingModes.NoWrap;
        guideTmp.raycastTarget = false;
        guideToggleBtnObj.SetActive(false);

        if (targetCanvas == null)
        {
            return;
        }

        // 2. Scrim transparan klik-luar di bawah area HUD atas agar tombol '?' & '≡' tetap responsif 1 ketukan
        menuScrimObj = new GameObject("Btn_CompactMenuOutsideScrim", typeof(RectTransform), typeof(Image), typeof(Button));
        menuScrimObj.transform.SetParent(targetCanvas.transform, false);
        RectTransform scrimRect = menuScrimObj.GetComponent<RectTransform>();
        scrimRect.anchorMin = Vector2.zero;
        scrimRect.anchorMax = Vector2.one;
        scrimRect.offsetMin = Vector2.zero;
        scrimRect.offsetMax = new Vector2(0f, -(TopHudTopMargin + TopHudZoneHeight + 4f));

        Image scrimImg = menuScrimObj.GetComponent<Image>();
        scrimImg.color = new Color(0f, 0f, 0f, 0.01f);
        scrimImg.raycastTarget = true;

        menuScrimButton = menuScrimObj.GetComponent<Button>();
        menuScrimButton.targetGraphic = scrimImg;
        menuScrimButton.transition = Selectable.Transition.None;
        menuScrimButton.onClick.AddListener(CloseMenuDrawer);
        menuScrimObj.SetActive(false);

        // 3. Laci Menu Floating Glass Bersudut Membulat (4 item Mode Kartu / 5 item Mode Udara)
        menuDrawerObj = new GameObject("Panel_CompactMenuDrawer", typeof(RectTransform), typeof(Image), typeof(Outline));
        menuDrawerObj.transform.SetParent(targetCanvas.transform, false);
        RectTransform drawerRect = menuDrawerObj.GetComponent<RectTransform>();
        menuDrawerRect = drawerRect;
        drawerRect.anchorMin = new Vector2(1f, 1f);
        drawerRect.anchorMax = new Vector2(1f, 1f);
        drawerRect.pivot = new Vector2(1f, 1f);
        drawerRect.anchoredPosition = new Vector2(-TopEdgeMarginX, DrawerTopY);
        drawerRect.sizeDelta = new Vector2(DrawerWidth, DrawerHeightCardMode);

        Image drawerBg = menuDrawerObj.GetComponent<Image>();
        ApplyRoundedCard(drawerBg);
        drawerBg.color = MenuDrawerBgColor;
        drawerBg.raycastTarget = true;

        Outline drawerOutline = menuDrawerObj.GetComponent<Outline>();
        drawerOutline.effectColor = new Color(0.18f, 0.86f, 1.00f, 0.42f);
        drawerOutline.effectDistance = new Vector2(2f, -2f);

        AttachRoundedBorderOverlay(drawerRect, "Panel_CompactMenuDrawer_Border", GetRoundedCardBorderSprite(), MenuDrawerBorderColor);

        float itemY = -16f;
        const float itemStep = 68f;

        drawerModeBtn = CreateDrawerItem(
            "Item_ToggleMode",
            drawerRect,
            itemY,
            "Mode: Udara (v7)",
            DrawerItemModeCardBgColor,
            DrawerItemModeCardBorderColor,
            new Color(0.94f, 1.00f, 1.00f, 1f),
            OnDrawerModeClicked,
            out drawerModeBg,
            out drawerModeBorderImg,
            out txtDrawerMode);
        itemY -= itemStep;

        drawerLevelBtn = CreateDrawerItem(
            "Item_SelectLevel",
            drawerRect,
            itemY,
            "Pilih Level / Sandbox",
            DrawerItemLevelBgColor,
            DrawerItemLevelBorderColor,
            new Color(0.93f, 0.98f, 1.00f, 1f),
            OnDrawerLevelClicked,
            out _,
            out _,
            out _);
        itemY -= itemStep;

        drawerResetBtn = CreateDrawerItem(
            "Item_ResetLevel",
            drawerRect,
            itemY,
            "Reset Level",
            DrawerItemResetBgColor,
            DrawerItemResetBorderColor,
            new Color(1.00f, 0.91f, 0.88f, 1f),
            OnDrawerResetClicked,
            out _,
            out _,
            out txtDrawerReset);
        itemY -= itemStep;

        drawerAudioBtn = CreateDrawerItem(
            "Item_AudioToggle",
            drawerRect,
            itemY,
            "Audio: ON",
            DrawerItemAudioOnBgColor,
            DrawerItemAudioOnBorderColor,
            new Color(0.92f, 0.99f, 1.00f, 1f),
            OnDrawerAudioClicked,
            out drawerAudioBg,
            out drawerAudioBorderImg,
            out txtDrawerAudio);
        itemY -= itemStep;

        drawerGuideBtn = CreateDrawerItem(
            "Item_GestureGuide",
            drawerRect,
            itemY,
            "Panduan Gestur",
            DrawerItemGuideBgColor,
            DrawerItemGuideBorderColor,
            new Color(0.90f, 0.99f, 1.00f, 1f),
            OnDrawerGuideClicked,
            out _,
            out _,
            out _);
        drawerGuideBtnObj = drawerGuideBtn != null ? drawerGuideBtn.gameObject : null;
        if (drawerGuideBtnObj != null)
        {
            drawerGuideBtnObj.SetActive(false);
        }

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
        rect.sizeDelta = new Vector2(30f, 4.2f);

        Image img = barObj.GetComponent<Image>();
        ApplyRoundedPill(img);
        img.color = new Color(0.93f, 0.99f, 1.00f, 1f);
        img.raycastTarget = false;
    }

    private Button CreateDrawerItem(
        string name,
        RectTransform parent,
        float topY,
        string initialLabel,
        Color bgColor,
        Color borderColor,
        Color textColor,
        UnityEngine.Events.UnityAction onClick,
        out Image bgImg,
        out Image borderImg,
        out TMP_Text labelTmp)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, topY);
        rect.sizeDelta = new Vector2(-32f, 58f);

        bgImg = obj.GetComponent<Image>();
        ApplyRoundedSquircle(bgImg);
        bgImg.color = bgColor;
        bgImg.raycastTarget = true;

        Outline outline = obj.GetComponent<Outline>();
        outline.effectColor = new Color(borderColor.r, borderColor.g, borderColor.b, 0.35f);
        outline.effectDistance = new Vector2(1.2f, -1.2f);

        borderImg = AttachRoundedBorderOverlay(rect, $"{name}_Border", GetRoundedSquircleBorderSprite(), borderColor);

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
        txtRect.offsetMin = new Vector2(16f, 4f);
        txtRect.offsetMax = new Vector2(-16f, -4f);

        TextMeshProUGUI tmp = txtObj.GetComponent<TextMeshProUGUI>();
        if (safeFont != null)
        {
            tmp.font = safeFont;
        }
        tmp.text = initialLabel;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = textColor;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 16.5f;
        tmp.fontSizeMax = 23.0f;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        labelTmp = tmp;

        return btn;
    }

    private void ConfigureMinimalistBottomControls()
    {
        StyleBottomCornerButton(btnInputA, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 16f), new Vector2(226f, 58f), InputACyanOutlineColor, overrideBg: false, Color.clear, isInputCapsule: true);
        StyleBottomCornerButton(btnInputB, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(254f, 16f), new Vector2(226f, 58f), InputBGoldOutlineColor, overrideBg: false, Color.clear, isInputCapsule: true);
        AddButtonIdentityStrip(btnInputA, "IdentityStrip_A", InputACyanOutlineColor);
        AddButtonIdentityStrip(btnInputB, "IdentityStrip_B", InputBGoldOutlineColor);
        StyleBottomCornerButton(btnVerify, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-18f, 16f), new Vector2(214f, 58f), TopBarAccentColor, overrideBg: true, CardBottomVerifyBgColor, isInputCapsule: false);
        StyleBottomCornerButton(btnNextLevel, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-18f, 16f), new Vector2(228f, 58f), new Color(0.28f, 0.96f, 0.64f, 0.96f), overrideBg: true, CardBottomNextBgColor, isInputCapsule: false);
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
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(8f, 0f);
        rect.sizeDelta = new Vector2(6f, 34f);

        Image img = stripObj.GetComponent<Image>();
        ApplyRoundedPill(img);
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
            ApplyRoundedSquircle(img);
            if (overrideBg)
            {
                img.color = bgColor;
            }
            else
            {
                Color c = img.color;
                c.a = 0.90f;
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
            label.fontSizeMin = isInputCapsule ? 14f : 15f;
            label.fontSizeMax = isInputCapsule ? 20.5f : 21.5f;
            if (isInputCapsule)
            {
                label.margin = new Vector4(18f, 2f, 10f, 2f);
            }
        }
    }

    private void ConfigureFeedbackToastLayout()
    {
        if (txtFeedback == null)
        {
            return;
        }

        float toastWidth = isAirWeavingActive ? 1160f : 960f;
        const float toastHeight = 68f;
        float toastBottomY = (isAirWeavingActive && isPracticePillBottomOffsetActive)
            ? ToastPracticeOffsetBottomY
            : ToastDefaultBottomY;

        RectTransform fbRect = txtFeedback.rectTransform;
        fbRect.anchorMin = new Vector2(0.5f, 0f);
        fbRect.anchorMax = new Vector2(0.5f, 0f);
        fbRect.pivot = new Vector2(0.5f, 0f);
        fbRect.anchoredPosition = new Vector2(0f, toastBottomY);
        fbRect.sizeDelta = new Vector2(toastWidth, toastHeight);
        txtFeedback.fontStyle = FontStyles.Bold;
        txtFeedback.alignment = TextAlignmentOptions.Center;
        txtFeedback.textWrappingMode = TextWrappingModes.Normal;
        txtFeedback.enableAutoSizing = true;
        txtFeedback.fontSizeMin = 20f;
        txtFeedback.fontSizeMax = 30f;
        txtFeedback.margin = new Vector4(26f, 5f, 26f, 5f);

        if (feedbackBackdropObj != null)
        {
            if (feedbackBackdropImage != null)
            {
                ApplyRoundedPill(feedbackBackdropImage);
            }

            RectTransform bgRect = feedbackBackdropObj.GetComponent<RectTransform>();
            if (bgRect != null)
            {
                bgRect.anchorMin = fbRect.anchorMin;
                bgRect.anchorMax = fbRect.anchorMax;
                bgRect.pivot = fbRect.pivot;
                bgRect.anchoredPosition = new Vector2(0f, toastBottomY - 3f);
                bgRect.sizeDelta = new Vector2(toastWidth + 32f, toastHeight + 10f);

                if (feedbackBackdropBorderImg == null)
                {
                    feedbackBackdropBorderImg = AttachRoundedBorderOverlay(
                        bgRect,
                        "Txt_Feedback_Backdrop_Border",
                        GetRoundedPillBorderSprite(),
                        ToastDefaultOutlineColor);
                }
            }
        }
    }

    private void ApplyToastStyleForMessage(string message, bool isNoHandPrompt)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        bool isFailure = message.StartsWith("Gagal", StringComparison.Ordinal);
        bool isSuccess = message.StartsWith("BERHASIL", StringComparison.Ordinal)
                         || message.StartsWith("SELAMAT", StringComparison.Ordinal);
        bool isHint = message.StartsWith("[Petunjuk", StringComparison.Ordinal);
        bool isNoHand = isNoHandPrompt || message.StartsWith("Arahkan tangan", StringComparison.Ordinal);

        Color bgCol = ToastDefaultBgColor;
        Color borderCol = ToastDefaultOutlineColor;
        Vector2 outlineDist = new Vector2(1.8f, -1.8f);

        if (isFailure)
        {
            bgCol = ToastFailureBgColor;
            borderCol = ToastFailureOutlineColor;
            outlineDist = new Vector2(2.4f, -2.4f);
        }
        else if (isSuccess)
        {
            bgCol = ToastSuccessBgColor;
            borderCol = ToastSuccessOutlineColor;
            outlineDist = new Vector2(2.4f, -2.4f);
        }
        else if (isHint)
        {
            bgCol = ToastHintBgColor;
            borderCol = ToastHintOutlineColor;
            outlineDist = new Vector2(2.2f, -2.2f);
        }
        else if (isNoHand)
        {
            bgCol = ToastNoHandBgColor;
            borderCol = ToastNoHandOutlineColor;
            outlineDist = new Vector2(2.0f, -2.0f);
        }

        if (feedbackBackdropImage != null)
        {
            feedbackBackdropImage.color = bgCol;
        }

        if (feedbackBackdropBorderImg != null)
        {
            feedbackBackdropBorderImg.color = borderCol;
        }

        if (feedbackBackdropOutline != null)
        {
            feedbackBackdropOutline.effectColor = new Color(borderCol.r, borderCol.g, borderCol.b, 0.50f);
            feedbackBackdropOutline.effectDistance = outlineDist;
        }

        if (txtFeedback != null)
        {
            if (isFailure) txtFeedback.color = ToastFailureTextColor;
            else if (isSuccess) txtFeedback.color = ToastSuccessTextColor;
        }
    }

    private void RefreshLevelPillBadgeAndShortTitle()
    {
        if (manager == null)
        {
            return;
        }

        bool isSandbox = manager.IsSandboxMode;
        if (isSandbox)
        {
            if (txtLevelBadge != null) txtLevelBadge.text = SandboxBadgeText;
            if (levelBadgeBg != null) levelBadgeBg.color = LevelBadgeSandboxBgColor;
            if (levelBadgeBorderImg != null) levelBadgeBorderImg.color = LevelBadgeSandboxBorderColor;
            if (txtLevelTitle != null) txtLevelTitle.text = SandboxShortTitle;
            return;
        }

        int levelIdx = Mathf.Clamp(manager.currentLevelIndex, 0, CachedLevelBadgeTexts.Length - 1);
        if (txtLevelBadge != null)
        {
            txtLevelBadge.text = CachedLevelBadgeTexts[levelIdx];
        }
        if (levelBadgeBg != null)
        {
            levelBadgeBg.color = LevelBadgeNormalBgColor;
        }
        if (levelBadgeBorderImg != null)
        {
            levelBadgeBorderImg.color = LevelBadgeNormalBorderColor;
        }
        if (txtLevelTitle != null)
        {
            txtLevelTitle.text = CachedShortLevelTitles[levelIdx];
        }
    }

    /// <summary>
    /// Menyesuaikan margin Safe Area horizontal (aman dari kamera depan / notch HP 20:9) dan menghitung
    /// lebar adaptif Pil Misi tengah secara zero-GC sehingga teks misi tampil besar tanpa menabrak pil kiri/kanan.
    /// </summary>
    private void UpdateTopHudAdaptiveLayout(bool force)
    {
        if (topBarRect == null)
        {
            return;
        }

        int sw = Screen.width;
        int sh = Screen.height;
        Rect safe = Screen.safeArea;
        string currentMissionText = txtMissionDesc != null ? txtMissionDesc.text : string.Empty;

        bool screenChanged = sw != lastScreenWidth || sh != lastScreenHeight || safe != lastSafeArea;
        bool missionChanged = !ReferenceEquals(currentMissionText, lastObservedMissionText);

        if (!force && !screenChanged && !missionChanged)
        {
            return;
        }

        lastScreenWidth = sw;
        lastScreenHeight = sh;
        lastSafeArea = safe;
        lastObservedMissionText = currentMissionText;

        float topWidth = topBarRect.rect.width > 600f ? topBarRect.rect.width : 1920f;
        float denomW = Mathf.Max(1f, sw);
        float safeLeftNorm = Mathf.Clamp(safe.xMin / denomW, 0f, 0.08f);
        float safeRightNorm = Mathf.Clamp((denomW - safe.xMax) / denomW, 0f, 0.08f);

        cachedSafeLeftInsetCanvas = Mathf.Round(safeLeftNorm * topWidth);
        cachedSafeRightInsetCanvas = Mathf.Round(safeRightNorm * topWidth);

        float leftEdgeX = TopEdgeMarginX + cachedSafeLeftInsetCanvas;
        float rightEdgeX = TopEdgeMarginX + cachedSafeRightInsetCanvas;

        if (levelPillRect != null)
        {
            levelPillRect.anchoredPosition = new Vector2(leftEdgeX, 0f);
        }

        if (menuToggleRect != null)
        {
            menuToggleRect.anchoredPosition = new Vector2(-rightEdgeX, 0f);
        }

        float guideRightX = rightEdgeX + SquircleBtnWidth + TopRightBtnGapX;
        if (guideToggleRect != null)
        {
            guideToggleRect.anchoredPosition = new Vector2(-guideRightX, 0f);
        }

        float quotaRightX = isAirWeavingActive
            ? (guideRightX + SquircleBtnWidth + TopRightBtnGapX)
            : guideRightX;
        if (quotaAlertPillRect != null)
        {
            quotaAlertPillRect.anchoredPosition = new Vector2(-quotaRightX, 0f);
        }

        if (menuDrawerRect != null)
        {
            menuDrawerRect.anchoredPosition = new Vector2(-rightEdgeX, DrawerTopY);
        }

        if (missionPillRect != null && txtMissionDesc != null)
        {
            float leftOccupied = leftEdgeX + LeftLevelPillWidth + TopSectionGapX;
            float rightOccupied = quotaRightX + RightQuotaPillWidth + TopSectionGapX;
            float availCenterWidth = Mathf.Max(420f, topWidth - leftOccupied - rightOccupied);
            float centerMidX = ((leftOccupied + (topWidth - rightOccupied)) * 0.5f) - (topWidth * 0.5f);

            Vector2 pref = txtMissionDesc.GetPreferredValues(currentMissionText, 10000f, TopPillHeight);
            float desiredPillWidth = Mathf.Clamp(pref.x + 56f, Mathf.Min(560f, availCenterWidth), availCenterWidth);

            missionPillRect.anchoredPosition = new Vector2(centerMidX, 0f);
            missionPillRect.sizeDelta = new Vector2(desiredPillWidth, TopPillHeight);
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
            drawerModeBg.color = isAirWeavingActive ? DrawerItemModeAirBgColor : DrawerItemModeCardBgColor;
        }
        if (drawerModeBorderImg != null)
        {
            drawerModeBorderImg.color = isAirWeavingActive ? DrawerItemModeAirBorderColor : DrawerItemModeCardBorderColor;
        }

        if (txtDrawerReset != null)
        {
            txtDrawerReset.text = (manager.IsSandboxMode || manager.IsAwaitingSandboxTransition)
                ? "Main Ulang (Level 1)"
                : "Reset Level";
        }

        if (txtDrawerAudio != null)
        {
            txtDrawerAudio.text = manager.isAudioMuted ? "Audio: OFF" : "Audio: ON";
            txtDrawerAudio.color = manager.isAudioMuted
                ? new Color(1.00f, 0.86f, 0.72f, 1f)
                : new Color(0.92f, 0.99f, 1.00f, 1f);
        }
        if (drawerAudioBg != null)
        {
            drawerAudioBg.color = manager.isAudioMuted ? DrawerItemMutedBgColor : DrawerItemAudioOnBgColor;
        }
        if (drawerAudioBorderImg != null)
        {
            drawerAudioBorderImg.color = manager.isAudioMuted ? DrawerItemMutedBorderColor : DrawerItemAudioOnBorderColor;
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

    private void OnDrawerAudioClicked()
    {
        if (manager != null)
        {
            manager.ToggleAudioMute();
        }
        RefreshDrawerItemLabels();
    }

    private void OnTopBarGuideClicked()
    {
        if (!isAirWeavingActive)
        {
            return;
        }

        CloseMenuDrawer();
        if (manager != null)
        {
            manager.CloseAllModalsForMenu();
        }
        OnGestureGuideRequested?.Invoke();
    }

    private void OnDrawerGuideClicked()
    {
        CloseMenuDrawer();
        if (!isAirWeavingActive)
        {
            return;
        }

        if (manager != null)
        {
            manager.CloseAllModalsForMenu();
        }
        OnGestureGuideRequested?.Invoke();
    }

    private void RefreshModeSpecificLayout()
    {
        if (guideToggleBtnObj != null && guideToggleBtnObj.activeSelf != isAirWeavingActive)
        {
            guideToggleBtnObj.SetActive(isAirWeavingActive);
        }

        if (drawerGuideBtnObj != null && drawerGuideBtnObj.activeSelf != isAirWeavingActive)
        {
            drawerGuideBtnObj.SetActive(isAirWeavingActive);
        }

        if (menuDrawerRect != null)
        {
            menuDrawerRect.sizeDelta = new Vector2(DrawerWidth, isAirWeavingActive ? DrawerHeightAirMode : DrawerHeightCardMode);
        }

        SetQuotaAlertState(isQuotaOverAlertActive);
        UpdateTopHudAdaptiveLayout(force: true);
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
