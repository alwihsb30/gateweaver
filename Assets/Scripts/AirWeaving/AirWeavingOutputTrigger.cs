using System;
using UnityEngine;
using TMPro;

public enum OutputDebugGuidanceState
{
    ConnectToOutput = 0,
    ReduceGates = 1,
    CheckRedCells = 2,
    TestSwitches = 3,
    PassedAllFour = 4
}

/// <summary>
/// Pemandu debugging mandiri live pada diorama 3D Output khusus Mode Udara (GateWeaver AR v7.0 Polishing Final).
/// - 100% terisolasi di Mode Udara (disembunyikan penuh di Mode Kartu & Mode Sandbox).
/// - Menonaktifkan jalan pintas tahan-kepal [ KEPAL: UJI ] agar pemain melakukan debugging mandiri lewat sakelar Input A/B
///   hingga 4/4 sel Pod LED menyala hijau (Auto-Win). Setelah lulus, tahan-kepal tetap aktif untuk lanjut level Zero-Touch.
/// - Menampilkan lencana pemandu live tepat di bawah Pod LED 2x2:
///   * Belum terhubung valid: "SAMBUNG KE OUTPUT"
///   * Melebihi kuota gerbang: "KURANGI GERBANG!" (merah)
///   * Ada minimal 1 sel Pod LED merah: "CEK SEL MERAH!" (merah-oranye)
///   * Belum 4/4 teruji: "UJI SAKELAR (X/4)" (sian)
///   * 4/4 hijau & dalam kuota: "LULUS! 4/4" (hijau)
/// - Zero-GC & shader OpenGLES3-safe (Sprites/Default, seluruh material dibersihkan di OnDestroy).
/// </summary>
public sealed class AirWeavingOutputTrigger : MonoBehaviour
{
    private const int RingSegments = 28;
    private const float OutputInteractRadius = 0.28f;
    private const float OutputRearmRadius = 0.38f;
    private const float MinScreenSpaceHitboxScale = 0.82f;
    private const float FistHoldDurationSeconds = 0.45f;
    private const float RearmCooldownSeconds = 0.85f;
    private const float HaloCenterUpOffset = 0.065f;
    private const float HaloRadius = 0.235f;
    private const float ReferenceCameraDepth = 3.82f;
    private const float BadgeBelowPodOffset = -0.105f;
    private const float BadgeHeightLocal = 0.068f;
    private const float BadgeSpriteScale = 0.22f;
    private const float BadgeFontSize = 0.32f;

    // Jalan pintas tahan-kepal untuk verifikasi dinonaktifkan 100% di Mode Udara demi debugging mandiri
    private static readonly bool EnableHoldToVerifyShortcut = false;

    private const string BadgeConnectToOutput = "SAMBUNG KE OUTPUT";
    private const string BadgeReduceGates = "KURANGI GERBANG";
    private const string BadgeCheckRedCells = "CEK SEL MERAH";
    private const string BadgePassedAllFour = "LULUS 4/4";
    private static readonly string[] BadgeTestSwitchesCache =
    {
        "UJI SAKELAR (0/4)",
        "UJI SAKELAR (1/4)",
        "UJI SAKELAR (2/4)",
        "UJI SAKELAR (3/4)"
    };

    private static readonly Color TestHaloCyanColor = new Color(GwThemeTokens.SignalHighTeal.r, GwThemeTokens.SignalHighTeal.g, GwThemeTokens.SignalHighTeal.b, 0.72f);
    private static readonly Color WinHaloGreenColor = new Color(GwThemeTokens.StatusPassEmerald.r, GwThemeTokens.StatusPassEmerald.g, GwThemeTokens.StatusPassEmerald.b, 0.92f);
    private static readonly Color WinHaloOuterColor = new Color(GwThemeTokens.StatusPassText.r, GwThemeTokens.StatusPassText.g, GwThemeTokens.StatusPassText.b, 0.50f);

    public event Action OnVerifyRequested;
    public event Action OnNextLevelRequested;

    private GameObject visualRootObj;
    private LineRenderer invitationRingRenderer;
    private LineRenderer winOuterRingRenderer;
    private LineRenderer progressArcRenderer;
    private GameObject badgeRootObj;
    private TextMeshPro badgeText;
    private SpriteRenderer badgeBackdropRenderer;
    private SpriteRenderer badgeBorderRenderer;

    private Material invitationMat;
    private Material winOuterMat;
    private Material progressMat;
    private Material badgeBackdropMat;
    private Material badgeBorderMat;

    private readonly Vector3[] progressArcPoints = new Vector3[RingSegments + 1];

    private bool isVisualInitialized = false;
    private bool requiresRearm = false;
    private float rearmCooldownTimer = 0f;
    private float chargeProgress = 0f;
    private bool wasReadyForNext = false;
    private string lastBadgeText = string.Empty;

    public bool IsCharging => chargeProgress > 0.01f;
    public float ChargeProgress => chargeProgress;

    public void EnsureVisualsCreated()
    {
        if (isVisualInitialized)
        {
            return;
        }

        visualRootObj = new GameObject("AR_AirWeavingOutputTrigger");
        visualRootObj.transform.SetParent(transform, false);

        // Halo berada di belakang sumbu kamera bodi 3D Output (2982..2984) agar tidak pernah memotong/clipping model 3D LAB
        invitationMat = CreateSafeSpritesMaterial(TestHaloCyanColor, 2983);
        winOuterMat = CreateSafeSpritesMaterial(WinHaloOuterColor, 2982);
        progressMat = CreateSafeSpritesMaterial(WinHaloGreenColor, 2984);
        badgeBackdropMat = CreateSafeSpritesMaterial(Color.white, 2998);
        badgeBorderMat = CreateSafeSpritesMaterial(Color.white, 2999);

        invitationRingRenderer = CreateRingRenderer("Output_InvitationHalo", visualRootObj.transform, RingSegments, loop: true, width: 0.008f, invitationMat);
        winOuterRingRenderer = CreateRingRenderer("Output_WinOuterHalo", visualRootObj.transform, RingSegments, loop: true, width: 0.006f, winOuterMat);
        progressArcRenderer = CreateRingRenderer("Output_ProgressArc", visualRootObj.transform, 0, loop: false, width: 0.015f, progressMat);

        // Lencana pemandu debugging live tepat di bawah Pod LED 2x2 (v8.5: SmokedGlassDense 82% + hairline 1px + radius 6px)
        badgeRootObj = new GameObject("Output_AffordanceBadge");
        badgeRootObj.transform.SetParent(visualRootObj.transform, false);

        badgeText = badgeRootObj.AddComponent<TextMeshPro>();
        badgeText.fontSize = BadgeFontSize;
        badgeText.fontStyle = FontStyles.Bold;
        badgeText.alignment = TextAlignmentOptions.Center;
        badgeText.textWrappingMode = TextWrappingModes.NoWrap;
        badgeText.color = GwThemeTokens.TextSecondary;
        badgeText.sortingOrder = 22;
        RectTransform txtRect = badgeText.rectTransform;
        if (txtRect != null)
        {
            txtRect.sizeDelta = new Vector2(1.6f, 0.30f);
        }

        badgeBackdropRenderer = CreateSlicedBadgeLayer(
            "Badge_Backdrop",
            badgeRootObj.transform,
            GwThemeTokens.GetRoundedRect6Sprite(),
            GwThemeTokens.SmokedGlassDense,
            badgeBackdropMat,
            localZ: 0.010f,
            sortingOrder: 20,
            initialWidth: 0.45f);

        badgeBorderRenderer = CreateSlicedBadgeLayer(
            "Badge_Border",
            badgeRootObj.transform,
            GwThemeTokens.GetHairlineBorder6Sprite(),
            GwThemeTokens.HairlineIdle,
            badgeBorderMat,
            localZ: 0.008f,
            sortingOrder: 21,
            initialWidth: 0.45f);

        isVisualInitialized = true;
        HideImmediate();
    }

    public void HideImmediate()
    {
        chargeProgress = 0f;
        requiresRearm = false;
        rearmCooldownTimer = 0f;
        wasReadyForNext = false;

        if (visualRootObj != null && visualRootObj.activeSelf)
        {
            visualRootObj.SetActive(false);
        }
        SetRingEnabled(invitationRingRenderer, false);
        SetRingEnabled(winOuterRingRenderer, false);
        SetRingEnabled(progressArcRenderer, false);
        if (badgeRootObj != null && badgeRootObj.activeSelf)
        {
            badgeRootObj.SetActive(false);
        }
    }

    /// <summary>
    /// Memperbarui pemandu debugging live pada Card_Output. Hold-to-verify dinonaktifkan penuh;
    /// tahan-kepal hanya aktif pasca-Auto-Win untuk melanjutkan level secara Zero-Touch. Zero-GC.
    /// </summary>
    public void Tick(
        Camera mainCam,
        float dt,
        bool isAirWeavingActive,
        bool isSandboxMode,
        bool isVerifying,
        bool isCircuitComplete,
        bool isReadyForNextLevel,
        bool isUiOverlayOpen,
        bool isHandDetected,
        bool isFistActive,
        Vector2 cursorLocalXY,
        Vector2 outputLocalXY,
        Vector3 outputWorldPos,
        bool isGrabbingGate,
        bool isHoveringGate,
        bool isTargetingPort,
        bool isDraggingCable,
        bool isRecordingAirDraw,
        float postDropLockoutTimer,
        OutputDebugGuidanceState guidanceState,
        int passedRowCount,
        float stageZoomScale = 1f,
        Transform stageRoot = null,
        Vector3 truthPodWorldAnchor = default)
    {
        if (!isVisualInitialized)
        {
            return;
        }

        // 1. Isolasi penuh: selalu tersembunyi di Mode Kartu, Mode Sandbox, atau saat sedang verifikasi/animasi fanfare
        if (!isAirWeavingActive || mainCam == null || isSandboxMode || isVerifying)
        {
            if (visualRootObj != null && visualRootObj.activeSelf)
            {
                visualRootObj.SetActive(false);
            }
            chargeProgress = 0f;
            if (!isAirWeavingActive || isSandboxMode)
            {
                wasReadyForNext = false;
            }
            return;
        }

        if (visualRootObj != null && !visualRootObj.activeSelf)
        {
            visualRootObj.SetActive(true);
        }

        float zoomClamp = Mathf.Clamp(stageZoomScale, AirWeavingStageController.MinZoomScale, AirWeavingStageController.MaxZoomScale);
        float outputCamZ = mainCam.transform.InverseTransformPoint(outputWorldPos).z;
        bool outputBehindCamera = outputCamZ <= 0.25f;
        float rawVisualScale = outputBehindCamera
            ? 0.55f
            : Mathf.Clamp(zoomClamp * (ReferenceCameraDepth / Mathf.Max(0.35f, outputCamZ)), 0.18f, 6.0f);
        float hitRadiusScale = outputBehindCamera
            ? MinScreenSpaceHitboxScale
            : Mathf.Clamp(rawVisualScale, MinScreenSpaceHitboxScale, 6.0f);

        // Edge detection pasca-Auto-Win: hanya aktif jika rangkaian live benar-benar lulus 4/4 (0 sel merah & kuota valid)
        bool effectiveReadyForNext = isReadyForNextLevel
                                     && isCircuitComplete
                                     && guidanceState == OutputDebugGuidanceState.PassedAllFour;
        if (effectiveReadyForNext && !wasReadyForNext)
        {
            requiresRearm = true;
            rearmCooldownTimer = RearmCooldownSeconds;
            chargeProgress = 0f;
        }
        wasReadyForNext = effectiveReadyForNext;

        if (rearmCooldownTimer > 0f)
        {
            rearmCooldownTimer = Mathf.Max(0f, rearmCooldownTimer - dt);
        }

        Vector2 targetCenterLocalXY = new Vector2(outputLocalXY.x + (0.03f * rawVisualScale), outputLocalXY.y + (0.10f * rawVisualScale));
        float distToOutput = isHandDetected ? Vector2.Distance(cursorLocalXY, targetCenterLocalXY) : float.MaxValue;

        if (requiresRearm && rearmCooldownTimer <= 0f && (!isHandDetected || !isFistActive || distToOutput > (OutputRearmRadius * hitRadiusScale)))
        {
            requiresRearm = false;
        }

        bool blockedByMutualExclusion = isUiOverlayOpen
                                        || isGrabbingGate
                                        || isHoveringGate
                                        || isTargetingPort
                                        || isDraggingCable
                                        || isRecordingAirDraw
                                        || postDropLockoutTimer > 0f;

        // 2. Nonaktifkan jalan pintas hold-to-verify: tahan-kepal hanya aktif bila level sudah LULUS (untuk lanjut level Zero-Touch)
        bool allowHoldAction = effectiveReadyForNext || (EnableHoldToVerifyShortcut && isCircuitComplete);
        bool insideZone = allowHoldAction && !outputBehindCamera && isHandDetected && !blockedByMutualExclusion && distToOutput <= (OutputInteractRadius * hitRadiusScale);

        if (insideZone && isFistActive && !requiresRearm)
        {
            chargeProgress = Mathf.Clamp01(chargeProgress + (dt / FistHoldDurationSeconds));

            if (chargeProgress >= 1f)
            {
                chargeProgress = 0f;
                requiresRearm = true;
                rearmCooldownTimer = RearmCooldownSeconds;

                if (effectiveReadyForNext)
                {
                    OnNextLevelRequested?.Invoke();
                }
                else if (EnableHoldToVerifyShortcut)
                {
                    OnVerifyRequested?.Invoke();
                }
            }
        }
        else
        {
            chargeProgress = Mathf.Max(0f, chargeProgress - (dt * 4.0f));
        }

        // 3. Perbarui visual Halo 3D & Lencana Pemandu Debugging Mandiri dengan Distance-Compensated Scale Clamping
        UpdateVisuals(
            mainCam,
            outputWorldPos,
            guidanceState,
            passedRowCount,
            isUiOverlayOpen,
            zoomClamp,
            stageRoot,
            truthPodWorldAnchor);
    }

    private void OnDestroy()
    {
        if (visualRootObj != null)
        {
            Destroy(visualRootObj);
            visualRootObj = null;
        }
        DestroyMat(ref invitationMat);
        DestroyMat(ref winOuterMat);
        DestroyMat(ref progressMat);
        DestroyMat(ref badgeBackdropMat);
        DestroyMat(ref badgeBorderMat);
        invitationRingRenderer = null;
        winOuterRingRenderer = null;
        progressArcRenderer = null;
        badgeRootObj = null;
        badgeText = null;
        badgeBackdropRenderer = null;
        badgeBorderRenderer = null;
    }

    private void UpdateVisuals(
        Camera mainCam,
        Vector3 outputWorldPos,
        OutputDebugGuidanceState guidanceState,
        int passedRowCount,
        bool isUiOverlayOpen,
        float zoomScale,
        Transform stageRoot,
        Vector3 truthPodWorldAnchor)
    {
        if (isUiOverlayOpen || mainCam == null)
        {
            SetRingEnabled(invitationRingRenderer, false);
            SetRingEnabled(winOuterRingRenderer, false);
            SetRingEnabled(progressArcRenderer, false);
            if (badgeRootObj != null && badgeRootObj.activeSelf)
            {
                badgeRootObj.SetActive(false);
            }
            return;
        }

        Transform camT = mainCam.transform;

        Vector3 camRight = camT.right;
        Vector3 camUp = camT.up;
        Vector3 camForward = camT.forward;

        // Halo 3D sejajar skala fisik Output (0.18x..15.0x) agar cincin selalu pas mengitari dudukan Output tanpa mengecil/membengkak
        float camDepth = Mathf.Max(0.85f, Vector3.Dot(outputWorldPos - camT.position, camForward));
        float distFactor = Mathf.Clamp(camDepth / ReferenceCameraDepth, 0.32f, 1.65f);
        float effectiveHaloScale = Mathf.Clamp(zoomScale, 0.22f, AirWeavingStageController.MaxZoomScale);
        float scaledHaloRadius = HaloRadius * effectiveHaloScale;

        // Halo 3D sejajar bidang meja XZ mengitari dudukan Output secara spasial (bukan stiker 2D di layar)
        Vector3 ringAxisA = stageRoot != null ? stageRoot.right : camRight;
        Vector3 ringAxisB = stageRoot != null ? stageRoot.forward : camUp;
        Vector3 tableUp = stageRoot != null ? stageRoot.up : camUp;
        Vector3 haloCenter = outputWorldPos + (tableUp * (HaloCenterUpOffset * effectiveHaloScale));

        // A. v8.5 Output Vault memakai plinth fisik titanium-grafit; cincin neon lantai dinonaktifkan
        SetRingEnabled(invitationRingRenderer, false);
        SetRingEnabled(winOuterRingRenderer, false);

        // B. Cincin Progres 0 -> 360 derajat (hanya pasca-Auto-Win saat menahan Kepal untuk lanjut level)
        if (chargeProgress > 0.01f && progressArcRenderer != null)
        {
            int segCount = Mathf.Clamp(Mathf.CeilToInt(chargeProgress * RingSegments), 2, RingSegments);
            float arcRadius = scaledHaloRadius + (0.014f * effectiveHaloScale);
            float maxAngle = chargeProgress * Mathf.PI * 2f;
            Vector3 arcCenter = haloCenter + (tableUp * 0.008f);

            for (int i = 0; i <= segCount; i++)
            {
                float frac = i / (float)segCount;
                float angle = (Mathf.PI * 0.5f) - (frac * maxAngle);
                progressArcPoints[i] = arcCenter + (ringAxisA * (Mathf.Cos(angle) * arcRadius)) + (ringAxisB * (Mathf.Sin(angle) * arcRadius));
            }

            if (progressMat != null)
            {
                progressMat.color = WinHaloGreenColor;
            }
            progressArcRenderer.enabled = true;
            progressArcRenderer.positionCount = segCount + 1;
            progressArcRenderer.startColor = WinHaloGreenColor;
            progressArcRenderer.endColor = WinHaloGreenColor;
            progressArcRenderer.SetPositions(progressArcPoints);
        }
        else
        {
            SetRingEnabled(progressArcRenderer, false);
        }

        // C. Lencana Pemandu Debugging Mandiri tepat di bawah Pod LED 2x2 (v8.5: SmokedGlassDense 82% + hairline 1px)
        if (badgeRootObj != null)
        {
            if (!badgeRootObj.activeSelf)
            {
                badgeRootObj.SetActive(true);
            }

            Vector3 podAnchor = truthPodWorldAnchor.sqrMagnitude > 0.001f
                ? truthPodWorldAnchor
                : (outputWorldPos - (camUp * (0.20f * distFactor)) - (camForward * 0.24f));
            float podDepth = Mathf.Max(0.75f, Vector3.Dot(podAnchor - camT.position, camForward));
            float podDistRatio = Mathf.Clamp(podDepth / ReferenceCameraDepth, 0.16f, 1.85f);
            float zoomRatio = Mathf.Max(0.20f, zoomScale / AirWeavingStageController.DefaultZoomScale);
            float depthProximityGain = Mathf.Clamp(ReferenceCameraDepth / podDepth, 0.50f, 2.20f);
            float naturalBadgeZoom = Mathf.Clamp(zoomRatio * Mathf.Lerp(1f, depthProximityGain, 0.60f), 0.55f, 1.85f);
            float badgeDistScale = Mathf.Clamp(0.92f * podDistRatio * naturalBadgeZoom, 0.42f, 1.65f);

            // Lencana selalu mengikuti status debugging live agar tidak pernah menimpa CEK SEL MERAH / KURANGI GERBANG
            string desiredText = ResolveGuidanceText(
                guidanceState,
                passedRowCount,
                out Color desiredTextColor,
                out Color desiredBorderColor,
                out float desiredWidth);

            Vector3 badgePos = podAnchor + (camUp * (BadgeBelowPodOffset * badgeDistScale)) - (camForward * 0.015f);

            // Clamp horizontal terhadap frustum kamera agar lencana tidak pernah terpotong di tepi kanan/kiri layar
            float badgeDepth = Mathf.Max(0.75f, Vector3.Dot(badgePos - camT.position, camForward));
            float halfViewH = badgeDepth * Mathf.Tan(mainCam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfViewW = halfViewH * mainCam.aspect;
            float badgeHalfW = desiredWidth * 0.5f * badgeDistScale;
            float maxCenterX = Mathf.Max(0f, (halfViewW * 0.96f) - badgeHalfW);
            float curRightX = Vector3.Dot(badgePos - camT.position, camRight);
            if (curRightX > maxCenterX)
            {
                badgePos -= camRight * (curRightX - maxCenterX);
            }
            else if (curRightX < -maxCenterX)
            {
                badgePos += camRight * (-maxCenterX - curRightX);
            }

            badgeRootObj.transform.position = badgePos;
            badgeRootObj.transform.localScale = Vector3.one * badgeDistScale;
            badgeRootObj.transform.rotation = camT.rotation;

            if (!string.Equals(desiredText, lastBadgeText, StringComparison.Ordinal) && badgeText != null)
            {
                lastBadgeText = desiredText;
                badgeText.text = desiredText;
                Vector2 slicedSize = new Vector2(desiredWidth / BadgeSpriteScale, BadgeHeightLocal / BadgeSpriteScale);
                if (badgeBackdropRenderer != null)
                {
                    badgeBackdropRenderer.size = slicedSize;
                }
                if (badgeBorderRenderer != null)
                {
                    badgeBorderRenderer.size = slicedSize;
                }
            }

            if (badgeText != null && badgeText.color != desiredTextColor)
            {
                badgeText.color = desiredTextColor;
            }
            if (badgeBackdropRenderer != null && badgeBackdropRenderer.color != GwThemeTokens.SmokedGlassDense)
            {
                badgeBackdropRenderer.color = GwThemeTokens.SmokedGlassDense;
            }
            if (badgeBorderRenderer != null && badgeBorderRenderer.color != desiredBorderColor)
            {
                badgeBorderRenderer.color = desiredBorderColor;
            }
        }
    }

    private static string ResolveGuidanceText(
        OutputDebugGuidanceState state,
        int passedRowCount,
        out Color textColor,
        out Color borderColor,
        out float backdropWidth)
    {
        switch (state)
        {
            case OutputDebugGuidanceState.ReduceGates:
                textColor = GwThemeTokens.StatusErrorText;
                borderColor = GwThemeTokens.StatusErrorRed;
                backdropWidth = 0.42f;
                return BadgeReduceGates;

            case OutputDebugGuidanceState.CheckRedCells:
                textColor = GwThemeTokens.StatusErrorText;
                borderColor = GwThemeTokens.StatusErrorRed;
                backdropWidth = 0.37f;
                return BadgeCheckRedCells;

            case OutputDebugGuidanceState.TestSwitches:
                textColor = GwThemeTokens.SignalHighTeal;
                borderColor = GwThemeTokens.SignalHighTeal;
                backdropWidth = 0.44f;
                int clampedPassed = Mathf.Clamp(passedRowCount, 0, BadgeTestSwitchesCache.Length - 1);
                return BadgeTestSwitchesCache[clampedPassed];

            case OutputDebugGuidanceState.PassedAllFour:
                textColor = GwThemeTokens.StatusPassText;
                borderColor = GwThemeTokens.StatusPassEmerald;
                backdropWidth = 0.28f;
                return BadgePassedAllFour;

            default:
                textColor = GwThemeTokens.TextSecondary;
                borderColor = GwThemeTokens.HairlineIdle;
                backdropWidth = 0.45f;
                return BadgeConnectToOutput;
        }
    }

    private static SpriteRenderer CreateSlicedBadgeLayer(
        string name,
        Transform parent,
        Sprite sprite,
        Color color,
        Material mat,
        float localZ,
        int sortingOrder,
        float initialWidth)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(0f, 0f, localZ);
        obj.transform.localRotation = Quaternion.identity;
        obj.transform.localScale = Vector3.one * BadgeSpriteScale;

        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = new Vector2(initialWidth / BadgeSpriteScale, BadgeHeightLocal / BadgeSpriteScale);
        sr.color = color;
        sr.sortingOrder = sortingOrder;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;
        if (mat != null)
        {
            sr.sharedMaterial = mat;
        }
        return sr;
    }

    private static LineRenderer CreateRingRenderer(string name, Transform parent, int positionCount, bool loop, float width, Material mat)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        LineRenderer lr = obj.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = loop;
        lr.positionCount = positionCount;
        lr.widthMultiplier = width;
        lr.numCapVertices = 3;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        if (mat != null)
        {
            lr.sharedMaterial = mat;
        }
        lr.enabled = false;
        return lr;
    }

    private static void SetRingEnabled(LineRenderer lr, bool enabledState)
    {
        if (lr != null && lr.enabled != enabledState)
        {
            lr.enabled = enabledState;
        }
    }

    private static Material CreateSafeSpritesMaterial(Color tint, int renderQueue)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null || !shader.isSupported)
        {
            shader = Shader.Find("UI/Default");
        }
        if (shader == null)
        {
            return null;
        }

        Material mat = new Material(shader);
        mat.color = tint;
        mat.renderQueue = renderQueue;
        return mat;
    }

    private static void DestroyMat(ref Material mat)
    {
        if (mat != null)
        {
            Destroy(mat);
            mat = null;
        }
    }
}
