using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renderer visual Hologram 3D Melayang gaya Iron Man HUD khusus Mode Udara (GateWeaver AR v7.0 Polishing Final).
/// - Menghilangkan cincin lantai pipih (oval dasar) agar seluruh gerbang, sakelar Input, & Output benar-benar
///   terlihat melayang bebas di ruang 3D tanpa ilusi menempel pada meja.
/// - Sorotan hover/grab/buang memakai aura melingkar 3D menghadap kamera + efek ripple sentuhan telunjuk pada Input A/B.
/// - Kursor reticle telapak anti-lompat & indikator cincin cubit presisi otomatis tersembunyi saat tangan hilang/overlay.
/// - 100% terisolasi di Mode Udara, shader Sprites/Default (aman OpenGLES3), Zero-GC, & dibersihkan penuh di OnDestroy().
/// </summary>
public sealed class AirWeavingHologramVisual : MonoBehaviour
{
    public enum NodeHoloState
    {
        Idle = 0,
        Parked = 1,
        BodyHovered = 2,
        Grabbed = 3,
        ReturningToRack = 4,
        PortFocused = 5
    }

    private const int AuraSegments = 24;
    private const int ReticleSegments = 20;
    private const int PinchRingSegments = 16;
    private const int MaxNodeSlots = 8; // InputA, InputB, Output, + 5 Gerbang Logika

    private static readonly Color InputACyanHoverColor = new Color(0.38f, 0.96f, 1.00f, 0.96f);
    private static readonly Color InputBGoldHoverColor = new Color(1.00f, 0.88f, 0.30f, 0.96f);
    private static readonly Color GateBodyHoverColor = new Color(0.42f, 0.98f, 1.00f, 0.92f);
    private static readonly Color GateGrabbedColor = new Color(1.00f, 0.88f, 0.28f, 0.96f);
    private static readonly Color GateOutsideDeleteRedColor = new Color(1.00f, 0.22f, 0.22f, 1.00f);

    private static readonly Color ReticleOpenColor = new Color(0.45f, 0.98f, 1.00f, 1.00f);
    private static readonly Color ReticlePortAimColor = new Color(0.55f, 1.00f, 0.85f, 1.00f);
    private static readonly Color ReticleFistColor = new Color(0.84f, 1.00f, 0.30f, 1.00f);
    private static readonly Color ReticleCoreWhiteColor = new Color(1.00f, 1.00f, 1.00f, 1.00f);

    private static readonly Color PinchHoverColor = new Color(0.32f, 0.98f, 1.00f, 1.00f);
    private static readonly Color PinchActiveDragColor = new Color(0.48f, 1.00f, 0.62f, 1.00f);
    private static readonly Color PinchSnappedColor = new Color(0.84f, 1.00f, 0.30f, 1.00f);
    private static readonly Color TwoHandOrbitCyanColor = new Color(0.30f, 0.96f, 1.00f, 0.96f);
    private static readonly Color TwoHandFreePanGoldColor = new Color(0.96f, 0.82f, 0.26f, 0.96f);
    private static readonly Color TwoHandResetAmberColor = new Color(1.00f, 0.56f, 0.22f, 0.96f);

    private GameObject rootObj;
    private LineRenderer[] nodeOuterRings;
    private LineRenderer[] nodeInnerRings;
    private Material[] nodeOuterMats;
    private Material[] nodeInnerMats;
    private readonly Vector3[] auraBuffer = new Vector3[AuraSegments];

    private LineRenderer[] inputRippleRenderers;
    private Material[] inputRippleMats;

    private LineRenderer reticleOuterRenderer;
    private LineRenderer reticleInnerRenderer;
    private LineRenderer reticleCrossHRenderer;
    private LineRenderer reticleCrossVRenderer;
    private Material reticleOuterMat;
    private Material reticleInnerMat;
    private Material reticleCrossMat;
    private LineRenderer secondaryReticleOuterRenderer;
    private LineRenderer secondaryReticleInnerRenderer;
    private LineRenderer secondaryReticleCrossHRenderer;
    private LineRenderer secondaryReticleCrossVRenderer;
    private Material secondaryReticleOuterMat;
    private readonly Vector3[] reticleBuffer = new Vector3[ReticleSegments];

    private LineRenderer pinchRingRenderer;
    private LineRenderer pinchCoreRenderer;
    private Material pinchRingMat;
    private Material pinchCoreMat;
    private LineRenderer secondaryPinchRingRenderer;
    private LineRenderer secondaryPinchCoreRenderer;
    private Material secondaryPinchRingMat;
    private readonly Vector3[] pinchRingBuffer = new Vector3[PinchRingSegments];

    // Visual Lock 2 Tangan (Air-Orbit 360°, Air-Zoom, & Dual-Fist Reset)
    private LineRenderer twoHandBeamRenderer;
    private LineRenderer twoHandAnchorARenderer;
    private LineRenderer twoHandAnchorBRenderer;
    private LineRenderer twoHandCenterRingRenderer;
    private Material twoHandBeamMat;
    private Material twoHandAnchorMat;
    private Material twoHandCenterMat;

    private readonly List<Material> ownedMaterials = new List<Material>(30);
    private bool isInitialized = false;

    public void EnsureVisualsCreated()
    {
        if (isInitialized)
        {
            return;
        }

        rootObj = new GameObject("AR_AirWeavingHologramHUD");
        rootObj.transform.SetParent(transform, false);

        nodeOuterRings = new LineRenderer[MaxNodeSlots];
        nodeInnerRings = new LineRenderer[MaxNodeSlots];
        nodeOuterMats = new Material[MaxNodeSlots];
        nodeInnerMats = new Material[MaxNodeSlots];

        for (int i = 0; i < MaxNodeSlots; i++)
        {
            nodeOuterMats[i] = CreateSafeSpritesMat(GateBodyHoverColor, 2986);
            nodeInnerMats[i] = CreateSafeSpritesMat(GateBodyHoverColor, 2985);
            nodeOuterRings[i] = CreateLoopLine($"Holo_NodeAuraOuter_{i}", AuraSegments, 0.007f, nodeOuterMats[i]);
            nodeInnerRings[i] = CreateLoopLine($"Holo_NodeAuraInner_{i}", AuraSegments, 0.004f, nodeInnerMats[i]);
        }

        inputRippleRenderers = new LineRenderer[2];
        inputRippleMats = new Material[2];
        inputRippleMats[0] = CreateSafeSpritesMat(InputACyanHoverColor, 3015);
        inputRippleMats[1] = CreateSafeSpritesMat(InputBGoldHoverColor, 3015);
        inputRippleRenderers[0] = CreateLoopLine("Holo_InputTouchRipple_A", AuraSegments, 0.011f, inputRippleMats[0]);
        inputRippleRenderers[1] = CreateLoopLine("Holo_InputTouchRipple_B", AuraSegments, 0.011f, inputRippleMats[1]);

        reticleOuterMat = CreateSafeSpritesMat(ReticleOpenColor, 3040);
        reticleInnerMat = CreateSafeSpritesMat(ReticleCoreWhiteColor, 3041);
        reticleCrossMat = CreateSafeSpritesMat(ReticleCoreWhiteColor, 3042);
        reticleOuterRenderer = CreateLoopLine("Holo_PalmReticleOuter", ReticleSegments, 0.009f, reticleOuterMat);
        reticleInnerRenderer = CreateLoopLine("Holo_PalmReticleInner", ReticleSegments, 0.0065f, reticleInnerMat);
        reticleCrossHRenderer = CreateSegmentLine("Holo_PalmReticleCrossH", 0.006f, reticleCrossMat);
        reticleCrossVRenderer = CreateSegmentLine("Holo_PalmReticleCrossV", 0.006f, reticleCrossMat);

        secondaryReticleOuterMat = CreateSafeSpritesMat(ReticleOpenColor, 3040);
        secondaryReticleOuterRenderer = CreateLoopLine("Holo_SecPalmReticleOuter", ReticleSegments, 0.009f, secondaryReticleOuterMat);
        secondaryReticleInnerRenderer = CreateLoopLine("Holo_SecPalmReticleInner", ReticleSegments, 0.0065f, reticleInnerMat);
        secondaryReticleCrossHRenderer = CreateSegmentLine("Holo_SecPalmReticleCrossH", 0.006f, reticleCrossMat);
        secondaryReticleCrossVRenderer = CreateSegmentLine("Holo_SecPalmReticleCrossV", 0.006f, reticleCrossMat);

        pinchRingMat = CreateSafeSpritesMat(PinchHoverColor, 3043);
        pinchCoreMat = CreateSafeSpritesMat(ReticleCoreWhiteColor, 3044);
        pinchRingRenderer = CreateLoopLine("Holo_PinchCursorRing", PinchRingSegments, 0.0085f, pinchRingMat);
        pinchCoreRenderer = CreateLoopLine("Holo_PinchCursorCore", PinchRingSegments, 0.0065f, pinchCoreMat);

        secondaryPinchRingMat = CreateSafeSpritesMat(PinchHoverColor, 3043);
        secondaryPinchRingRenderer = CreateLoopLine("Holo_SecPinchCursorRing", PinchRingSegments, 0.0085f, secondaryPinchRingMat);
        secondaryPinchCoreRenderer = CreateLoopLine("Holo_SecPinchCursorCore", PinchRingSegments, 0.0065f, pinchCoreMat);

        twoHandBeamMat = CreateSafeSpritesMat(TwoHandOrbitCyanColor, 3045);
        twoHandAnchorMat = CreateSafeSpritesMat(TwoHandOrbitCyanColor, 3046);
        twoHandCenterMat = CreateSafeSpritesMat(ReticleCoreWhiteColor, 3047);
        twoHandBeamRenderer = CreateSegmentLine("Holo_TwoHandLinkBeam", 0.0075f, twoHandBeamMat);
        twoHandAnchorARenderer = CreateLoopLine("Holo_TwoHandAnchorA", PinchRingSegments, 0.0095f, twoHandAnchorMat);
        twoHandAnchorBRenderer = CreateLoopLine("Holo_TwoHandAnchorB", PinchRingSegments, 0.0095f, twoHandAnchorMat);
        twoHandCenterRingRenderer = CreateLoopLine("Holo_TwoHandCenterCompass", ReticleSegments, 0.0085f, twoHandCenterMat);

        isInitialized = true;
        HideImmediate();
    }

    public void HideImmediate()
    {
        if (!isInitialized)
        {
            return;
        }

        if (rootObj != null && rootObj.activeSelf)
        {
            rootObj.SetActive(false);
        }
        HideCursors();
        SetTwoHandLinkVisible(false);
        if (inputRippleRenderers != null)
        {
            for (int i = 0; i < inputRippleRenderers.Length; i++)
            {
                if (inputRippleRenderers[i] != null) inputRippleRenderers[i].enabled = false;
            }
        }
    }

    public void HideCursors()
    {
        if (!isInitialized)
        {
            return;
        }

        SetReticleVisible(false);
        SetSecondaryReticleVisible(false);
        SetPinchIndicatorVisible(false);
        SetSecondaryPinchIndicatorVisible(false);
        SetTwoHandLinkVisible(false);
    }

    public void SetRootVisible(bool visible)
    {
        if (!isInitialized || rootObj == null)
        {
            return;
        }

        if (rootObj.activeSelf != visible)
        {
            rootObj.SetActive(visible);
        }
    }

    /// <summary>
    /// Memperbarui cincin aura 3D spasial yang sejajar bidang meja XZ (<c>HologramStageRoot</c>) dan mengitari bodi 3D
    /// ditambah cincin ekuator 3D tipis yang mengikuti kemiringan bodi gerbang (bukan lagi stiker 2D camera-billboard). Zero-GC.
    /// </summary>
    public void UpdateNodeFrame(
        int slotIndex,
        Transform camT,
        GateNode node,
        bool isInputA,
        bool isInputB,
        bool isOutput,
        NodeHoloState state,
        Transform stageRoot = null)
    {
        if (!isInitialized || slotIndex < 0 || slotIndex >= MaxNodeSlots)
        {
            return;
        }

        LineRenderer outer = nodeOuterRings[slotIndex];
        LineRenderer inner = nodeInnerRings[slotIndex];

        bool shouldDrawAura = camT != null
                              && node != null
                              && !isOutput
                              && (state == NodeHoloState.BodyHovered
                                  || state == NodeHoloState.Grabbed
                                  || state == NodeHoloState.ReturningToRack);
        if (!shouldDrawAura)
        {
            if (outer != null && outer.enabled) outer.enabled = false;
            if (inner != null && inner.enabled) inner.enabled = false;
            return;
        }

        Transform nodeT = node.transform;
        float nodeScale = nodeT.localScale.x;
        Vector3 tableRight = stageRoot != null ? stageRoot.right : nodeT.right;
        Vector3 tableForward = stageRoot != null ? stageRoot.forward : nodeT.forward;
        Vector3 tableUp = stageRoot != null ? stageRoot.up : nodeT.up;

        // Cincin utama 3D sejajar bidang meja XZ mengitari pinggang bawah bodi gerbang secara spasial
        Vector3 waistCenter = nodeT.position + (tableUp * (0.075f * nodeScale));
        // Cincin ekuator 3D kedua mengikuti orientasi 3D bodi gerbang (ikut miring saat velocity banking tilt)
        Vector3 equatorCenter = nodeT.position + (nodeT.up * (0.165f * nodeScale));
        float pulse = 0.5f + (0.5f * Mathf.Sin((Time.unscaledTime * 5.2f) + slotIndex));

        Color auraColor;
        float outerRadius;
        float outerWidth;
        bool showInnerEquatorialRing = true;

        if (isInputA)
        {
            auraColor = InputACyanHoverColor;
            outerRadius = (0.310f * nodeScale) + (0.012f * pulse);
            outerWidth = 0.0085f;
        }
        else if (isInputB)
        {
            auraColor = InputBGoldHoverColor;
            outerRadius = (0.310f * nodeScale) + (0.012f * pulse);
            outerWidth = 0.0085f;
        }
        else if (state == NodeHoloState.ReturningToRack)
        {
            // Tint cincin 3D merah terang saat gerbang dipegang keluar dari kotak AssemblyZone
            auraColor = GateOutsideDeleteRedColor;
            outerRadius = (0.340f * nodeScale) + (0.018f * pulse);
            outerWidth = 0.0110f;
        }
        else if (state == NodeHoloState.Grabbed)
        {
            auraColor = GateGrabbedColor;
            outerRadius = (0.320f * nodeScale) + (0.014f * pulse);
            outerWidth = 0.0092f;
        }
        else
        {
            // BodyHovered pada gerbang
            auraColor = GateBodyHoverColor;
            outerRadius = (0.295f * nodeScale) + (0.010f * pulse);
            outerWidth = 0.0075f;
        }

        DrawAuraCircle(outer, nodeOuterMats[slotIndex], waistCenter, tableRight, tableForward, outerRadius, auraColor, outerWidth);

        if (showInnerEquatorialRing && inner != null)
        {
            float innerAlpha = state == NodeHoloState.ReturningToRack ? 0.68f : 0.48f;
            Color innerColor = new Color(auraColor.r, auraColor.g, auraColor.b, auraColor.a * innerAlpha);
            float innerWidth = state == NodeHoloState.ReturningToRack ? 0.0062f : 0.0044f;
            DrawAuraCircle(inner, nodeInnerMats[slotIndex], equatorCenter, nodeT.right, nodeT.forward, outerRadius * 0.84f, innerColor, innerWidth);
        }
        else if (inner != null && inner.enabled)
        {
            inner.enabled = false;
        }
    }

    /// <summary>
    /// Merender efek gelombang ripple 3D sejajar bidang meja XZ saat sakelar Input A/B disentuh ujung telunjuk (#8). Zero-GC.
    /// </summary>
    public void UpdateInputTouchRipple(
        int inputIndex,
        Transform camT,
        GateNode node,
        bool isInputB,
        float normalizedProgress,
        Transform stageRoot = null)
    {
        if (!isInitialized || inputRippleRenderers == null || inputIndex < 0 || inputIndex >= inputRippleRenderers.Length)
        {
            return;
        }

        LineRenderer lr = inputRippleRenderers[inputIndex];
        if (lr == null)
        {
            return;
        }

        if (camT == null || node == null || normalizedProgress <= 0.001f || normalizedProgress >= 1f)
        {
            if (lr.enabled) lr.enabled = false;
            return;
        }

        float eased = 1f - ((1f - normalizedProgress) * (1f - normalizedProgress));
        Transform nodeT = node.transform;
        float scale = nodeT.localScale.x;
        float zoomRatio = Mathf.Clamp(scale / 0.68f, 0.72f, 1.38f);
        Vector3 axisA = stageRoot != null ? stageRoot.right : nodeT.right;
        Vector3 axisB = stageRoot != null ? stageRoot.forward : nodeT.forward;
        Vector3 tableUp = stageRoot != null ? stageRoot.up : nodeT.up;
        Vector3 center = nodeT.position + (tableUp * (0.095f * scale));
        float radius = Mathf.Lerp(0.16f, 0.38f, eased) * zoomRatio;
        Color baseCol = isInputB ? InputBGoldHoverColor : InputACyanHoverColor;
        Color faded = new Color(baseCol.r, baseCol.g, baseCol.b, (1f - normalizedProgress) * 0.95f);
        float width = Mathf.Lerp(0.014f, 0.003f, normalizedProgress);

        DrawAuraCircle(lr, inputRippleMats[inputIndex], center, axisA, axisB, radius, faded, width);
    }

    /// <summary>
    /// Memperbarui reticle kursor telapak tangan primer (tersembunyi otomatis saat tangan hilang / overlay / drag kabel). Zero-GC.
    /// </summary>
    public void UpdatePalmReticle(
        Transform camT,
        bool isHandDetected,
        bool hideReticle,
        Vector3 palmWorldPos,
        bool isFistActive,
        bool isTargetingPort,
        float meanCurlRatio)
    {
        RenderPalmReticle(
            camT,
            isHandDetected,
            hideReticle,
            palmWorldPos,
            isFistActive,
            isTargetingPort,
            meanCurlRatio,
            reticleOuterRenderer,
            reticleOuterMat,
            reticleInnerRenderer,
            reticleCrossHRenderer,
            reticleCrossVRenderer,
            isSecondary: false);
    }

    /// <summary>
    /// Memperbarui reticle kursor telapak tangan sekunder agar kedua tangan sama-sama tampak siap pakai saat Idle. Zero-GC.
    /// </summary>
    public void UpdateSecondaryPalmReticle(
        Transform camT,
        bool isHandDetected,
        bool hideReticle,
        Vector3 palmWorldPos,
        bool isFistActive,
        bool isTargetingPort,
        float meanCurlRatio)
    {
        RenderPalmReticle(
            camT,
            isHandDetected,
            hideReticle,
            palmWorldPos,
            isFistActive,
            isTargetingPort,
            meanCurlRatio,
            secondaryReticleOuterRenderer,
            secondaryReticleOuterMat,
            secondaryReticleInnerRenderer,
            secondaryReticleCrossHRenderer,
            secondaryReticleCrossVRenderer,
            isSecondary: true);
    }

    private void RenderPalmReticle(
        Transform camT,
        bool isHandDetected,
        bool hideReticle,
        Vector3 palmWorldPos,
        bool isFistActive,
        bool isTargetingPort,
        float meanCurlRatio,
        LineRenderer outerLr,
        Material outerMat,
        LineRenderer innerLr,
        LineRenderer crossHLr,
        LineRenderer crossVLr,
        bool isSecondary)
    {
        if (!isInitialized)
        {
            return;
        }

        bool show = isHandDetected && !hideReticle && camT != null;
        if (!show)
        {
            if (isSecondary) SetSecondaryReticleVisible(false);
            else SetReticleVisible(false);
            return;
        }

        Vector3 right = camT.right;
        Vector3 up = camT.up;
        Vector3 center = palmWorldPos - (camT.forward * 0.14f);

        float openFrac = Mathf.Clamp01((meanCurlRatio - 0.78f) / 0.75f);
        float outerRadius = isFistActive ? 0.036f : Mathf.Lerp(0.044f, 0.074f, openFrac);
        float innerRadius = isFistActive ? 0.015f : 0.018f;
        float crossHalf = isFistActive ? 0.022f : 0.030f;

        Color color = isFistActive
            ? ReticleFistColor
            : (isTargetingPort ? ReticlePortAimColor : ReticleOpenColor);

        DrawReticleCircle(outerLr, outerMat, center, right, up, outerRadius, color, isFistActive ? 0.013f : 0.009f);
        DrawReticleCircle(innerLr, reticleInnerMat, center - (camT.forward * 0.004f), right, up, innerRadius, ReticleCoreWhiteColor, isFistActive ? 0.009f : 0.006f);
        if (crossHLr != null && crossVLr != null)
        {
            Vector3 crossCenter = center - (camT.forward * 0.006f);
            crossHLr.enabled = true;
            crossHLr.SetPosition(0, crossCenter - (right * crossHalf));
            crossHLr.SetPosition(1, crossCenter + (right * crossHalf));
            crossVLr.enabled = true;
            crossVLr.SetPosition(0, crossCenter - (up * crossHalf));
            crossVLr.SetPosition(1, crossCenter + (up * crossHalf));
        }
    }

    /// <summary>
    /// Menampilkan indikator cincin cubit kecil presisi di titik tengah #4-#8 saat membidik port atau menyeret kabel. Zero-GC.
    /// </summary>
    public void UpdatePinchIndicator(
        Transform camT,
        bool isHandDetected,
        bool isAllowedAndRelevant,
        Vector3 pinchWorldPos,
        bool isPinchActive,
        bool isSnappedToPort)
    {
        RenderPinchIndicator(
            camT,
            isHandDetected,
            isAllowedAndRelevant,
            pinchWorldPos,
            isPinchActive,
            isSnappedToPort,
            pinchRingRenderer,
            pinchRingMat,
            pinchCoreRenderer,
            isSecondary: false);
    }

    /// <summary>
    /// Menampilkan indikator cincin cubit untuk tangan kedua saat membidik port di mode Idle ambidextrous. Zero-GC.
    /// </summary>
    public void UpdateSecondaryPinchIndicator(
        Transform camT,
        bool isHandDetected,
        bool isAllowedAndRelevant,
        Vector3 pinchWorldPos,
        bool isPinchActive,
        bool isSnappedToPort)
    {
        RenderPinchIndicator(
            camT,
            isHandDetected,
            isAllowedAndRelevant,
            pinchWorldPos,
            isPinchActive,
            isSnappedToPort,
            secondaryPinchRingRenderer,
            secondaryPinchRingMat,
            secondaryPinchCoreRenderer,
            isSecondary: true);
    }

    private void RenderPinchIndicator(
        Transform camT,
        bool isHandDetected,
        bool isAllowedAndRelevant,
        Vector3 pinchWorldPos,
        bool isPinchActive,
        bool isSnappedToPort,
        LineRenderer ringLr,
        Material ringMat,
        LineRenderer coreLr,
        bool isSecondary)
    {
        if (!isInitialized)
        {
            return;
        }

        bool show = isHandDetected && isAllowedAndRelevant && camT != null;
        if (!show)
        {
            if (isSecondary) SetSecondaryPinchIndicatorVisible(false);
            else SetPinchIndicatorVisible(false);
            return;
        }

        Vector3 right = camT.right;
        Vector3 up = camT.up;
        Vector3 center = pinchWorldPos - (camT.forward * 0.145f);
        float pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * 8.0f));
        float outerR = isPinchActive ? (0.020f + (0.003f * pulse)) : 0.032f;
        float coreR = isPinchActive ? 0.0085f : 0.0065f;
        Color ringCol = isSnappedToPort ? PinchSnappedColor : (isPinchActive ? PinchActiveDragColor : PinchHoverColor);

        DrawPinchCircle(ringLr, ringMat, center, right, up, outerR, ringCol, isPinchActive ? 0.011f : 0.0085f);
        DrawPinchCircle(coreLr, pinchCoreMat, center - (camT.forward * 0.004f), right, up, coreR, ReticleCoreWhiteColor, 0.007f);
    }

    /// <summary>
    /// Menampilkan garis sinar penghubung hologram + 2 cincin jangkar di titik tangan kiri-kanan + cincin kompas tengah
    /// selama mode Air-Orbit/Air-Zoom 2 tangan (<c>Both Hands Pinch</c>, sian), Free Air-Pan 3D (<c>1 Pinch + 1 Fist</c>, emas lembut),
    /// atau tahan Reset (<c>Both Hands Fist</c>, amber). Zero-GC.
    /// </summary>
    public void UpdateTwoHandManipulatorLink(
        Transform camT,
        bool isVisible,
        Vector3 primaryHandWorldPos,
        Vector3 secondaryHandWorldPos,
        bool isFistResetMode,
        float fistResetProgress,
        bool isFreePanMode = false)
    {
        if (!isInitialized)
        {
            return;
        }

        if (!isVisible || camT == null)
        {
            SetTwoHandLinkVisible(false);
            return;
        }

        Vector3 right = camT.right;
        Vector3 up = camT.up;
        Vector3 forward = camT.forward;
        Vector3 posA = primaryHandWorldPos - (forward * 0.14f);
        Vector3 posB = secondaryHandWorldPos - (forward * 0.14f);
        Vector3 midPos = (posA + posB) * 0.5f;

        float pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * 8.5f));
        Color mainCol = isFistResetMode
            ? TwoHandResetAmberColor
            : (isFreePanMode ? TwoHandFreePanGoldColor : TwoHandOrbitCyanColor);
        float anchorR = isFistResetMode
            ? Mathf.Lerp(0.052f, 0.028f, Mathf.Clamp01(fistResetProgress))
            : (0.040f + (0.005f * pulse));
        float centerR = isFistResetMode
            ? Mathf.Lerp(0.024f, 0.078f, Mathf.Clamp01(fistResetProgress))
            : (isFreePanMode ? (0.054f + (0.005f * pulse)) : (0.048f + (0.006f * pulse)));

        if (twoHandBeamRenderer != null)
        {
            if (twoHandBeamMat != null)
            {
                twoHandBeamMat.color = mainCol;
            }
            twoHandBeamRenderer.enabled = true;
            twoHandBeamRenderer.widthMultiplier = (isFistResetMode || isFreePanMode) ? 0.0095f : 0.0075f;
            twoHandBeamRenderer.startColor = mainCol;
            twoHandBeamRenderer.endColor = mainCol;
            twoHandBeamRenderer.SetPosition(0, posA);
            twoHandBeamRenderer.SetPosition(1, posB);
        }

        DrawPinchCircle(twoHandAnchorARenderer, twoHandAnchorMat, posA, right, up, anchorR, mainCol, 0.010f);
        DrawPinchCircle(twoHandAnchorBRenderer, twoHandAnchorMat, posB, right, up, anchorR, mainCol, 0.010f);
        DrawReticleCircle(twoHandCenterRingRenderer, twoHandCenterMat, midPos - (forward * 0.005f), right, up, centerR, (isFistResetMode || isFreePanMode) ? mainCol : ReticleCoreWhiteColor, 0.0085f);
    }

    private void SetTwoHandLinkVisible(bool visible)
    {
        if (twoHandBeamRenderer != null && twoHandBeamRenderer.enabled != visible)
        {
            twoHandBeamRenderer.enabled = visible;
        }
        if (twoHandAnchorARenderer != null && twoHandAnchorARenderer.enabled != visible)
        {
            twoHandAnchorARenderer.enabled = visible;
        }
        if (twoHandAnchorBRenderer != null && twoHandAnchorBRenderer.enabled != visible)
        {
            twoHandAnchorBRenderer.enabled = visible;
        }
        if (twoHandCenterRingRenderer != null && twoHandCenterRingRenderer.enabled != visible)
        {
            twoHandCenterRingRenderer.enabled = visible;
        }
    }

    private void SetReticleVisible(bool visible)
    {
        if (reticleOuterRenderer != null && reticleOuterRenderer.enabled != visible)
        {
            reticleOuterRenderer.enabled = visible;
        }
        if (reticleInnerRenderer != null && reticleInnerRenderer.enabled != visible)
        {
            reticleInnerRenderer.enabled = visible;
        }
        if (reticleCrossHRenderer != null && reticleCrossHRenderer.enabled != visible)
        {
            reticleCrossHRenderer.enabled = visible;
        }
        if (reticleCrossVRenderer != null && reticleCrossVRenderer.enabled != visible)
        {
            reticleCrossVRenderer.enabled = visible;
        }
    }

    private void SetSecondaryReticleVisible(bool visible)
    {
        if (secondaryReticleOuterRenderer != null && secondaryReticleOuterRenderer.enabled != visible)
        {
            secondaryReticleOuterRenderer.enabled = visible;
        }
        if (secondaryReticleInnerRenderer != null && secondaryReticleInnerRenderer.enabled != visible)
        {
            secondaryReticleInnerRenderer.enabled = visible;
        }
        if (secondaryReticleCrossHRenderer != null && secondaryReticleCrossHRenderer.enabled != visible)
        {
            secondaryReticleCrossHRenderer.enabled = visible;
        }
        if (secondaryReticleCrossVRenderer != null && secondaryReticleCrossVRenderer.enabled != visible)
        {
            secondaryReticleCrossVRenderer.enabled = visible;
        }
    }

    private void SetPinchIndicatorVisible(bool visible)
    {
        if (pinchRingRenderer != null && pinchRingRenderer.enabled != visible)
        {
            pinchRingRenderer.enabled = visible;
        }
        if (pinchCoreRenderer != null && pinchCoreRenderer.enabled != visible)
        {
            pinchCoreRenderer.enabled = visible;
        }
    }

    private void SetSecondaryPinchIndicatorVisible(bool visible)
    {
        if (secondaryPinchRingRenderer != null && secondaryPinchRingRenderer.enabled != visible)
        {
            secondaryPinchRingRenderer.enabled = visible;
        }
        if (secondaryPinchCoreRenderer != null && secondaryPinchCoreRenderer.enabled != visible)
        {
            secondaryPinchCoreRenderer.enabled = visible;
        }
    }

    private void DrawAuraCircle(
        LineRenderer lr,
        Material mat,
        Vector3 center,
        Vector3 axisA,
        Vector3 axisB,
        float radius,
        Color color,
        float width)
    {
        if (lr == null)
        {
            return;
        }

        for (int i = 0; i < AuraSegments; i++)
        {
            float angle = (i / (float)AuraSegments) * Mathf.PI * 2f;
            auraBuffer[i] = center + (axisA * (Mathf.Cos(angle) * radius)) + (axisB * (Mathf.Sin(angle) * radius));
        }

        if (mat != null)
        {
            mat.color = color;
        }
        lr.enabled = true;
        lr.widthMultiplier = width;
        lr.startColor = color;
        lr.endColor = color;
        lr.SetPositions(auraBuffer);
    }

    private void DrawReticleCircle(
        LineRenderer lr,
        Material mat,
        Vector3 center,
        Vector3 axisA,
        Vector3 axisB,
        float radius,
        Color color,
        float width)
    {
        if (lr == null)
        {
            return;
        }

        for (int i = 0; i < ReticleSegments; i++)
        {
            float angle = (i / (float)ReticleSegments) * Mathf.PI * 2f;
            reticleBuffer[i] = center + (axisA * (Mathf.Cos(angle) * radius)) + (axisB * (Mathf.Sin(angle) * radius));
        }

        if (mat != null)
        {
            mat.color = color;
        }
        lr.enabled = true;
        lr.widthMultiplier = width;
        lr.startColor = color;
        lr.endColor = color;
        lr.SetPositions(reticleBuffer);
    }

    private void DrawPinchCircle(
        LineRenderer lr,
        Material mat,
        Vector3 center,
        Vector3 axisA,
        Vector3 axisB,
        float radius,
        Color color,
        float width)
    {
        if (lr == null)
        {
            return;
        }

        for (int i = 0; i < PinchRingSegments; i++)
        {
            float angle = (i / (float)PinchRingSegments) * Mathf.PI * 2f;
            pinchRingBuffer[i] = center + (axisA * (Mathf.Cos(angle) * radius)) + (axisB * (Mathf.Sin(angle) * radius));
        }

        if (mat != null)
        {
            mat.color = color;
        }
        lr.enabled = true;
        lr.widthMultiplier = width;
        lr.startColor = color;
        lr.endColor = color;
        lr.SetPositions(pinchRingBuffer);
    }

    private LineRenderer CreateLoopLine(string objName, int segCount, float width, Material mat)
    {
        GameObject obj = new GameObject(objName);
        obj.transform.SetParent(rootObj.transform, false);
        LineRenderer lr = obj.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = true;
        lr.positionCount = segCount;
        lr.widthMultiplier = width;
        lr.numCornerVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        if (mat != null)
        {
            lr.sharedMaterial = mat;
        }
        lr.enabled = false;
        return lr;
    }

    private LineRenderer CreateSegmentLine(string objName, float width, Material mat)
    {
        GameObject obj = new GameObject(objName);
        obj.transform.SetParent(rootObj.transform, false);
        LineRenderer lr = obj.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = false;
        lr.positionCount = 2;
        lr.widthMultiplier = width;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        if (mat != null)
        {
            lr.sharedMaterial = mat;
        }
        lr.enabled = false;
        return lr;
    }

    private Material CreateSafeSpritesMat(Color initialColor, int renderQueue)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("UI/Default");
        }
        if (shader == null)
        {
            return null;
        }

        Material mat = new Material(shader);
        mat.color = initialColor;
        mat.renderQueue = renderQueue;
        ownedMaterials.Add(mat);
        return mat;
    }

    private void OnDestroy()
    {
        if (rootObj != null)
        {
            Destroy(rootObj);
            rootObj = null;
        }

        for (int i = 0; i < ownedMaterials.Count; i++)
        {
            if (ownedMaterials[i] != null)
            {
                Destroy(ownedMaterials[i]);
            }
        }
        ownedMaterials.Clear();
        nodeOuterRings = null;
        nodeInnerRings = null;
        nodeOuterMats = null;
        nodeInnerMats = null;
        inputRippleRenderers = null;
        inputRippleMats = null;
        reticleOuterRenderer = null;
        reticleInnerRenderer = null;
        reticleCrossHRenderer = null;
        reticleCrossVRenderer = null;
        reticleOuterMat = null;
        reticleInnerMat = null;
        reticleCrossMat = null;
        secondaryReticleOuterRenderer = null;
        secondaryReticleInnerRenderer = null;
        secondaryReticleCrossHRenderer = null;
        secondaryReticleCrossVRenderer = null;
        secondaryReticleOuterMat = null;
        pinchRingRenderer = null;
        pinchCoreRenderer = null;
        pinchRingMat = null;
        pinchCoreMat = null;
        secondaryPinchRingRenderer = null;
        secondaryPinchCoreRenderer = null;
        secondaryPinchRingMat = null;
    }
}
