using System;
using UnityEngine;
using TMPro;

/// <summary>
/// Pengendali interaksi spasial Zero-Touch pada diorama 3D Output (GateWeaver AR v7.0 Fase 2).
/// - 100% terisolasi di Mode Udara (disembunyikan penuh di Mode Kartu & di Mode Sandbox).
/// - Menampilkan affordance diegetik zero-tutorial pada Card_Output:
///   * Belum lengkap: tersembunyi (atau cincin amber terkunci singkat bila disentuh paksa).
///   * Siap diuji: Halo denyut sian-emas mengelilingi diorama + lencana mikro [ TAHAN: UJI ] di bawah Pod LED 2x2.
///   * Level lulus: Halo ganda hijau zamrud + lencana mikro [ TAHAN: LANJUT >> ] / [ TAHAN: SANDBOX >> ].
/// - Melindungi pemicu dengan kontrak Mutual Exclusion ketat (grab, 0.65s post-drop lockout, hover gerbang, Air-Draw,
///   verifikasi, & UI overlay terbuka) serta Re-Arm Guard (wajib keluar radius + jeda 1.0 dtk pasca-aksi).
/// - Zero-GC & shader OpenGLES3-safe (Sprites/Default, seluruh material dibersihkan di OnDestroy).
/// </summary>
public sealed class AirWeavingOutputTrigger : MonoBehaviour
{
    private const int RingSegments = 28;
    private const float OutputInteractRadius = 0.36f;
    private const float OutputRearmRadius = 0.48f;
    private const float DwellDurationSeconds = 1.00f;
    private const float PinchHoldDurationSeconds = 0.75f;
    private const float LockedWarnDurationSeconds = 0.55f;
    private const float DwellMoveTolerance = 0.13f;
    private const float RearmCooldownSeconds = 1.00f;
    private const float HaloCenterUpOffset = 0.185f;
    private const float HaloRadius = 0.188f;
    private const float BadgeBelowOffset = -0.31f;

    private const string BadgeTextVerify = "[ TAHAN: UJI ]";
    private const string BadgeTextNext = "[ TAHAN: LANJUT >> ]";
    private const string BadgeTextSandbox = "[ TAHAN: SANDBOX >> ]";

    private static readonly Color ReadyHaloCyanColor = new Color(0.22f, 0.94f, 1.00f, 0.78f);
    private static readonly Color ReadyHaloGoldColor = new Color(1.00f, 0.86f, 0.28f, 0.88f);
    private static readonly Color WinHaloGreenColor = new Color(0.18f, 1.00f, 0.56f, 0.90f);
    private static readonly Color WinHaloOuterColor = new Color(0.32f, 1.00f, 0.76f, 0.48f);
    private static readonly Color LockedAmberColor = new Color(1.00f, 0.62f, 0.18f, 0.62f);
    private static readonly Color BadgeBackdropColor = new Color(0.03f, 0.06f, 0.12f, 0.86f);
    private static readonly Color BadgeVerifyTextColor = new Color(0.86f, 0.98f, 1.00f, 1f);
    private static readonly Color BadgeWinTextColor = new Color(0.38f, 1.00f, 0.68f, 1f);

    public event Action OnVerifyRequested;
    public event Action OnNextLevelRequested;

    private GameObject visualRootObj;
    private LineRenderer invitationRingRenderer;
    private LineRenderer winOuterRingRenderer;
    private LineRenderer progressArcRenderer;
    private GameObject badgeRootObj;
    private TextMeshPro badgeText;
    private Transform badgeBackdropTransform;

    private Material invitationMat;
    private Material winOuterMat;
    private Material progressMat;
    private Material badgeBackdropMat;

    private readonly Vector3[] invitationRingPoints = new Vector3[RingSegments];
    private readonly Vector3[] winOuterRingPoints = new Vector3[RingSegments];
    private readonly Vector3[] progressArcPoints = new Vector3[RingSegments + 1];

    private bool isVisualInitialized = false;
    private bool requiresRearm = false;
    private float rearmCooldownTimer = 0f;
    private float chargeProgress = 0f;
    private bool hasDwellAnchor = false;
    private Vector2 dwellAnchorLocalXY = Vector2.zero;
    private bool wasReadyForNext = false;
    private string lastBadgeText = string.Empty;

    public bool IsCharging => chargeProgress > 0.01f;

    public void EnsureVisualsCreated()
    {
        if (isVisualInitialized)
        {
            return;
        }

        visualRootObj = new GameObject("AR_AirWeavingOutputTrigger");
        visualRootObj.transform.SetParent(transform, false);

        invitationMat = CreateSafeSpritesMaterial(ReadyHaloCyanColor, 3016);
        winOuterMat = CreateSafeSpritesMaterial(WinHaloOuterColor, 3015);
        progressMat = CreateSafeSpritesMaterial(ReadyHaloGoldColor, 3018);
        badgeBackdropMat = CreateSafeSpritesMaterial(BadgeBackdropColor, 2995);

        invitationRingRenderer = CreateRingRenderer("Output_InvitationHalo", visualRootObj.transform, RingSegments, loop: true, width: 0.008f, invitationMat);
        winOuterRingRenderer = CreateRingRenderer("Output_WinOuterHalo", visualRootObj.transform, RingSegments, loop: true, width: 0.006f, winOuterMat);
        progressArcRenderer = CreateRingRenderer("Output_ProgressArc", visualRootObj.transform, 0, loop: false, width: 0.015f, progressMat);

        // Lencana mikro diegetik di bawah Pod LED 2x2 (bebas tabrakan dengan Label_Status3D & OutputTruthPod3D)
        badgeRootObj = new GameObject("Output_AffordanceBadge");
        badgeRootObj.transform.SetParent(visualRootObj.transform, false);

        badgeText = badgeRootObj.AddComponent<TextMeshPro>();
        badgeText.fontSize = 0.44f;
        badgeText.fontStyle = FontStyles.Bold;
        badgeText.alignment = TextAlignmentOptions.Center;
        badgeText.textWrappingMode = TextWrappingModes.NoWrap;
        badgeText.color = BadgeVerifyTextColor;
        RectTransform txtRect = badgeText.rectTransform;
        if (txtRect != null)
        {
            txtRect.sizeDelta = new Vector2(2.2f, 0.45f);
        }

        GameObject bgQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bgQuad.name = "Badge_Backdrop";
        bgQuad.transform.SetParent(badgeRootObj.transform, false);
        bgQuad.transform.localPosition = new Vector3(0f, 0f, 0.010f);
        bgQuad.transform.localScale = new Vector3(0.54f, 0.088f, 1f);
        Collider col = bgQuad.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }
        Renderer bgRend = bgQuad.GetComponent<Renderer>();
        if (bgRend != null)
        {
            bgRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bgRend.receiveShadows = false;
            if (badgeBackdropMat != null)
            {
                bgRend.sharedMaterial = badgeBackdropMat;
            }
        }
        badgeBackdropTransform = bgQuad.transform;

        isVisualInitialized = true;
        HideImmediate();
    }

    public void HideImmediate()
    {
        chargeProgress = 0f;
        hasDwellAnchor = false;
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
    /// Memperbarui deteksi tahan/cubit pada Card_Output serta visual halo & cincin progres. Zero-GC.
    /// </summary>
    public void Tick(
        Camera mainCam,
        float dt,
        bool isAirWeavingActive,
        bool isSandboxMode,
        bool isVerifying,
        bool isCircuitComplete,
        bool isReadyForNextLevel,
        bool isAwaitingSandboxTransition,
        bool isUiOverlayOpen,
        bool isHandDetected,
        bool isPinching,
        Vector2 indexLocalXY,
        Vector2 pinchLocalXY,
        Vector2 outputLocalXY,
        Vector3 outputWorldPos,
        bool isGrabbingGate,
        bool isHoveringGate,
        bool isRecordingAirDraw,
        float postDropLockoutTimer)
    {
        if (!isVisualInitialized)
        {
            return;
        }

        // 1. Isolasi penuh: selalu tersembunyi di Mode Kartu, Mode Sandbox, atau saat sedang verifikasi
        if (!isAirWeavingActive || mainCam == null || isSandboxMode || isVerifying)
        {
            if (visualRootObj != null && visualRootObj.activeSelf)
            {
                visualRootObj.SetActive(false);
            }
            chargeProgress = 0f;
            hasDwellAnchor = false;
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

        // Edge detection: saat baru saja lulus level pada rangkaian lengkap, wajib Re-Arm agar selebrasi tidak langsung melompat level
        bool effectiveReadyForNext = isReadyForNextLevel && isCircuitComplete;
        if (effectiveReadyForNext && !wasReadyForNext)
        {
            requiresRearm = true;
            rearmCooldownTimer = RearmCooldownSeconds;
            chargeProgress = 0f;
            hasDwellAnchor = false;
        }
        wasReadyForNext = effectiveReadyForNext;

        if (rearmCooldownTimer > 0f)
        {
            rearmCooldownTimer = Mathf.Max(0f, rearmCooldownTimer - dt);
        }

        // Pusat interaksi sedikit naik ke badan diorama (bebas Pod LED di bawah)
        Vector2 targetCenterLocalXY = new Vector2(outputLocalXY.x, outputLocalXY.y + 0.12f);
        Vector2 cursorXY = isPinching ? pinchLocalXY : indexLocalXY;
        float distToOutput = isHandDetected ? Vector2.Distance(cursorXY, targetCenterLocalXY) : float.MaxValue;

        if (requiresRearm && rearmCooldownTimer <= 0f && (!isHandDetected || (distToOutput > OutputRearmRadius && !isPinching)))
        {
            requiresRearm = false;
        }

        // 2. Kontrak Mutual Exclusion ketat
        bool blockedByMutualExclusion = isUiOverlayOpen
                                        || isGrabbingGate
                                        || isHoveringGate
                                        || isRecordingAirDraw
                                        || postDropLockoutTimer > 0f;

        bool canInteractAction = effectiveReadyForNext || isCircuitComplete;
        bool insideZone = isHandDetected && !blockedByMutualExclusion && distToOutput <= OutputInteractRadius;

        // 3. Logika pengisian progres tahan stabil / cubit-tahan (termasuk peringatan amber saat rangkaian belum lengkap)
        if (insideZone && !requiresRearm)
        {
            if (!hasDwellAnchor)
            {
                dwellAnchorLocalXY = cursorXY;
                hasDwellAnchor = true;
            }
            else if (!isPinching && Vector2.Distance(cursorXY, dwellAnchorLocalXY) > DwellMoveTolerance)
            {
                // Gerak melintas terlalu cepat -> geser jangkar & reset progres (anti false-trigger)
                dwellAnchorLocalXY = cursorXY;
                chargeProgress = 0f;
            }

            float requiredDuration = canInteractAction
                ? (isPinching ? PinchHoldDurationSeconds : DwellDurationSeconds)
                : LockedWarnDurationSeconds;
            chargeProgress = Mathf.Clamp01(chargeProgress + (dt / requiredDuration));

            if (chargeProgress >= 1f)
            {
                chargeProgress = 0f;
                hasDwellAnchor = false;
                requiresRearm = true;
                rearmCooldownTimer = RearmCooldownSeconds;

                if (effectiveReadyForNext)
                {
                    OnNextLevelRequested?.Invoke();
                }
                else
                {
                    OnVerifyRequested?.Invoke();
                }
            }
        }
        else
        {
            hasDwellAnchor = false;
            chargeProgress = Mathf.Max(0f, chargeProgress - (dt * 3.5f));
        }

        // 4. Perbarui visual Halo Diegetik, Cincin Progres, & Lencana Mikro
        UpdateVisuals(
            mainCam.transform,
            outputWorldPos,
            effectiveReadyForNext,
            isAwaitingSandboxTransition,
            isCircuitComplete,
            insideZone && !canInteractAction,
            isUiOverlayOpen);
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
        invitationRingRenderer = null;
        winOuterRingRenderer = null;
        progressArcRenderer = null;
        badgeRootObj = null;
        badgeText = null;
        badgeBackdropTransform = null;
    }

    // =========================================================================
    // DIEGETIC VISUALS (CAMERA-FACING HALO, PROGRESS ARC, & MICRO-BADGE)
    // =========================================================================

    private void UpdateVisuals(
        Transform camT,
        Vector3 outputWorldPos,
        bool isReadyForNextLevel,
        bool isAwaitingSandboxTransition,
        bool isCircuitComplete,
        bool showLockedAmber,
        bool isUiOverlayOpen)
    {
        if (isUiOverlayOpen)
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

        Vector3 camRight = camT.right;
        Vector3 camUp = camT.up;
        Vector3 camForward = camT.forward;
        Vector3 haloCenter = outputWorldPos + (camUp * HaloCenterUpOffset) - (camForward * 0.16f);

        // A. Halo Ajakan (Invitation Ring)
        if (isReadyForNextLevel)
        {
            float pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * 5.0f));
            float innerR = HaloRadius + (0.010f * pulse);
            float outerR = HaloRadius + 0.026f + (0.012f * pulse);
            DrawFullCircle(invitationRingRenderer, invitationRingPoints, haloCenter, camRight, camUp, innerR, WinHaloGreenColor, 0.0085f);
            DrawFullCircle(winOuterRingRenderer, winOuterRingPoints, haloCenter, camRight, camUp, outerR, WinHaloOuterColor, 0.0055f);
        }
        else if (isCircuitComplete)
        {
            float pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * 2.6f));
            Color haloColor = Color.Lerp(ReadyHaloCyanColor, ReadyHaloGoldColor, pulse * 0.65f);
            float r = HaloRadius + (0.009f * pulse);
            DrawFullCircle(invitationRingRenderer, invitationRingPoints, haloCenter, camRight, camUp, r, haloColor, 0.0075f);
            SetRingEnabled(winOuterRingRenderer, false);
        }
        else if (showLockedAmber)
        {
            DrawFullCircle(invitationRingRenderer, invitationRingPoints, haloCenter, camRight, camUp, HaloRadius, LockedAmberColor, 0.0065f);
            SetRingEnabled(winOuterRingRenderer, false);
        }
        else
        {
            SetRingEnabled(invitationRingRenderer, false);
            SetRingEnabled(winOuterRingRenderer, false);
        }

        // B. Cincin Progres 0 -> 360 derajat saat tangan menahan
        if (chargeProgress > 0.01f && progressArcRenderer != null)
        {
            int segCount = Mathf.Clamp(Mathf.CeilToInt(chargeProgress * RingSegments), 2, RingSegments);
            float arcRadius = HaloRadius + 0.014f;
            float maxAngle = chargeProgress * Mathf.PI * 2f;
            Vector3 arcCenter = haloCenter - (camForward * 0.015f);

            for (int i = 0; i <= segCount; i++)
            {
                float frac = i / (float)segCount;
                // Mulai dari puncak jam 12 (+camUp), mengisi searah jarum jam
                float angle = (Mathf.PI * 0.5f) - (frac * maxAngle);
                progressArcPoints[i] = arcCenter + (camRight * (Mathf.Cos(angle) * arcRadius)) + (camUp * (Mathf.Sin(angle) * arcRadius));
            }

            Color progColor = showLockedAmber
                ? new Color(1.00f, 0.72f, 0.18f, 0.98f)
                : (isReadyForNextLevel
                    ? WinHaloGreenColor
                    : Color.Lerp(ReadyHaloCyanColor, ReadyHaloGoldColor, chargeProgress));

            if (progressMat != null)
            {
                progressMat.color = progColor;
            }
            progressArcRenderer.enabled = true;
            progressArcRenderer.positionCount = segCount + 1;
            progressArcRenderer.startColor = progColor;
            progressArcRenderer.endColor = progColor;
            progressArcRenderer.SetPositions(progressArcPoints);
        }
        else
        {
            SetRingEnabled(progressArcRenderer, false);
        }

        // C. Lencana Mikro Diegetik tepat di bawah Pod LED 2x2 (bebas tabrakan dengan Pod & Label_Status3D)
        bool showBadge = isReadyForNextLevel || isCircuitComplete;
        if (badgeRootObj != null)
        {
            if (badgeRootObj.activeSelf != showBadge)
            {
                badgeRootObj.SetActive(showBadge);
            }

            if (showBadge)
            {
                Vector3 badgePos = outputWorldPos + (camUp * BadgeBelowOffset) - (camForward * 0.20f);
                badgeRootObj.transform.position = badgePos;
                badgeRootObj.transform.rotation = Quaternion.LookRotation(badgePos - camT.position, camUp);

                string desiredText = isReadyForNextLevel
                    ? (isAwaitingSandboxTransition ? BadgeTextSandbox : BadgeTextNext)
                    : BadgeTextVerify;

                if (!string.Equals(desiredText, lastBadgeText, StringComparison.Ordinal) && badgeText != null)
                {
                    lastBadgeText = desiredText;
                    badgeText.text = desiredText;
                    badgeText.color = isReadyForNextLevel ? BadgeWinTextColor : BadgeVerifyTextColor;
                    if (badgeBackdropTransform != null)
                    {
                        float width = isReadyForNextLevel ? 0.64f : 0.48f;
                        badgeBackdropTransform.localScale = new Vector3(width, 0.084f, 1f);
                    }
                }
            }
        }
    }

    private static void DrawFullCircle(
        LineRenderer lr,
        Vector3[] buffer,
        Vector3 center,
        Vector3 axisA,
        Vector3 axisB,
        float radius,
        Color color,
        float width)
    {
        if (lr == null || buffer == null)
        {
            return;
        }

        for (int i = 0; i < RingSegments; i++)
        {
            float angle = (i / (float)RingSegments) * Mathf.PI * 2f;
            buffer[i] = center + (axisA * (Mathf.Cos(angle) * radius)) + (axisB * (Mathf.Sin(angle) * radius));
        }

        if (lr.sharedMaterial != null)
        {
            lr.sharedMaterial.color = color;
        }
        lr.enabled = true;
        lr.widthMultiplier = width;
        lr.startColor = color;
        lr.endColor = color;
        lr.SetPositions(buffer);
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
