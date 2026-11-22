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
    private const int SkeletonJointCount = 21;

    // One-Euro Filter 3D Zero-GC untuk 21 titik visual skeleton tangan (minCutoff 1.35 Hz bunuh jitter, beta 0.06 nol-lag, dCutoff = 1.0 Hz)
    private const float SkeletonOneEuroMinCutoff = 1.35f;
    private const float SkeletonOneEuroBeta = 0.060f;
    private const float SkeletonOneEuroDCutoff = 1.00f;
    private const float SkeletonOneEuroWorldSpeedScale = 22.0f;
    private const float SkeletonFadeInRate = 15.0f;
    private const float SkeletonFadeOutRate = 10.0f;
    private const float ReticleFadeInRate = 18.0f;
    private const float ReticleFadeOutRate = 11.0f;
    private const float FadeDisableEpsilon = 0.015f;

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

    private struct ReticleFadeState
    {
        public float alpha;
        public bool hasPose;
        public Vector3 lastPos;
        public bool lastIsFist;
        public bool lastIsTargetingPort;
        public bool lastIsCarryPan;
        public float lastMeanCurlRatio;

        public void ResetImmediate()
        {
            alpha = 0f;
            hasPose = false;
            lastPos = Vector3.zero;
            lastIsFist = false;
            lastIsTargetingPort = false;
            lastIsCarryPan = false;
            lastMeanCurlRatio = 1.4f;
        }
    }

    private struct PinchFadeState
    {
        public float alpha;
        public bool hasPose;
        public Vector3 lastPos;
        public bool lastIsPinchActive;
        public bool lastIsSnapped;

        public void ResetImmediate()
        {
            alpha = 0f;
            hasPose = false;
            lastPos = Vector3.zero;
            lastIsPinchActive = false;
            lastIsSnapped = false;
        }
    }

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
    private Material secondaryReticleInnerMat;
    private Material secondaryReticleCrossMat;
    private readonly Vector3[] reticleBuffer = new Vector3[ReticleSegments];

    private LineRenderer pinchRingRenderer;
    private LineRenderer pinchCoreRenderer;
    private Material pinchRingMat;
    private Material pinchCoreMat;
    private LineRenderer secondaryPinchRingRenderer;
    private LineRenderer secondaryPinchCoreRenderer;
    private Material secondaryPinchRingMat;
    private Material secondaryPinchCoreMat;
    private readonly Vector3[] pinchRingBuffer = new Vector3[PinchRingSegments];
    private readonly float[] auraCosTable = new float[AuraSegments];
    private readonly float[] auraSinTable = new float[AuraSegments];
    private readonly float[] reticleCosTable = new float[ReticleSegments];
    private readonly float[] reticleSinTable = new float[ReticleSegments];
    private readonly float[] pinchCosTable = new float[PinchRingSegments];
    private readonly float[] pinchSinTable = new float[PinchRingSegments];

    // One-Euro Filter 3D Zero-GC untuk 21 titik visual skeleton slot 0 & slot 1 + smooth fade alpha occlusion grace
    private readonly OneEuroFilter3D[] primarySkeletonFilters = new OneEuroFilter3D[SkeletonJointCount];
    private readonly OneEuroFilter3D[] secondarySkeletonFilters = new OneEuroFilter3D[SkeletonJointCount];
    private readonly Vector3[] primarySmoothedJoints = new Vector3[SkeletonJointCount];
    private readonly Vector3[] secondarySmoothedJoints = new Vector3[SkeletonJointCount];
    private float primarySkeletonAlpha = 0f;
    private float secondarySkeletonAlpha = 0f;
    private float lastAppliedPrimaryBoneAlpha = -1f;
    private float lastAppliedSecondaryBoneAlpha = -1f;
    private bool hasPrimarySkeletonPose = false;
    private bool hasSecondarySkeletonPose = false;
    private ReticleFadeState primaryReticleFade;
    private ReticleFadeState secondaryReticleFade;
    private PinchFadeState primaryPinchFade;
    private PinchFadeState secondaryPinchFade;

    // Visual Lock 2 Tangan (Air-Orbit 360°, Air-Zoom, & Dual-Fist Reset)
    private LineRenderer twoHandBeamRenderer;
    private LineRenderer twoHandAnchorARenderer;
    private LineRenderer twoHandAnchorBRenderer;
    private LineRenderer twoHandCenterRingRenderer;
    private Material twoHandBeamMat;
    private Material twoHandAnchorMat;
    private Material twoHandCenterMat;

    private readonly List<Material> ownedMaterials = new List<Material>(36);
    private bool isInitialized = false;

    public void EnsureVisualsCreated()
    {
        if (isInitialized)
        {
            return;
        }

        rootObj = new GameObject("AR_AirWeavingHologramHUD");
        rootObj.transform.SetParent(transform, false);

        for (int i = 0; i < AuraSegments; i++)
        {
            float angle = (i / (float)AuraSegments) * Mathf.PI * 2f;
            auraCosTable[i] = Mathf.Cos(angle);
            auraSinTable[i] = Mathf.Sin(angle);
        }
        for (int i = 0; i < ReticleSegments; i++)
        {
            float angle = (i / (float)ReticleSegments) * Mathf.PI * 2f;
            reticleCosTable[i] = Mathf.Cos(angle);
            reticleSinTable[i] = Mathf.Sin(angle);
        }
        for (int i = 0; i < PinchRingSegments; i++)
        {
            float angle = (i / (float)PinchRingSegments) * Mathf.PI * 2f;
            pinchCosTable[i] = Mathf.Cos(angle);
            pinchSinTable[i] = Mathf.Sin(angle);
        }

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
        secondaryReticleInnerMat = CreateSafeSpritesMat(ReticleCoreWhiteColor, 3041);
        secondaryReticleCrossMat = CreateSafeSpritesMat(ReticleCoreWhiteColor, 3042);
        secondaryReticleOuterRenderer = CreateLoopLine("Holo_SecPalmReticleOuter", ReticleSegments, 0.009f, secondaryReticleOuterMat);
        secondaryReticleInnerRenderer = CreateLoopLine("Holo_SecPalmReticleInner", ReticleSegments, 0.0065f, secondaryReticleInnerMat);
        secondaryReticleCrossHRenderer = CreateSegmentLine("Holo_SecPalmReticleCrossH", 0.006f, secondaryReticleCrossMat);
        secondaryReticleCrossVRenderer = CreateSegmentLine("Holo_SecPalmReticleCrossV", 0.006f, secondaryReticleCrossMat);

        pinchRingMat = CreateSafeSpritesMat(PinchHoverColor, 3043);
        pinchCoreMat = CreateSafeSpritesMat(ReticleCoreWhiteColor, 3044);
        pinchRingRenderer = CreateLoopLine("Holo_PinchCursorRing", PinchRingSegments, 0.0085f, pinchRingMat);
        pinchCoreRenderer = CreateLoopLine("Holo_PinchCursorCore", PinchRingSegments, 0.0065f, pinchCoreMat);

        secondaryPinchRingMat = CreateSafeSpritesMat(PinchHoverColor, 3043);
        secondaryPinchCoreMat = CreateSafeSpritesMat(ReticleCoreWhiteColor, 3044);
        secondaryPinchRingRenderer = CreateLoopLine("Holo_SecPinchCursorRing", PinchRingSegments, 0.0085f, secondaryPinchRingMat);
        secondaryPinchCoreRenderer = CreateLoopLine("Holo_SecPinchCursorCore", PinchRingSegments, 0.0065f, secondaryPinchCoreMat);

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
        ResetHandSkeletonFilters(0, clearAlphaImmediate: true);
        ResetHandSkeletonFilters(1, clearAlphaImmediate: true);
        primaryReticleFade.ResetImmediate();
        secondaryReticleFade.ResetImmediate();
        primaryPinchFade.ResetImmediate();
        secondaryPinchFade.ResetImmediate();

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
        primaryReticleFade.ResetImmediate();
        secondaryReticleFade.ResetImmediate();
        primaryPinchFade.ResetImmediate();
        secondaryPinchFade.ResetImmediate();

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

    /// <summary>
    /// Mereset seluruh 21 filter One-Euro 3D skeleton tangan untuk slot 0 atau slot 1 saat fresh entry / reset. Zero-GC.
    /// </summary>
    public void ResetHandSkeletonFilters(int handSlot, bool clearAlphaImmediate = false)
    {
        OneEuroFilter3D[] filters = handSlot == 1 ? secondarySkeletonFilters : primarySkeletonFilters;
        for (int i = 0; i < SkeletonJointCount; i++)
        {
            filters[i].Reset();
        }

        if (handSlot == 1)
        {
            if (clearAlphaImmediate)
            {
                secondarySkeletonAlpha = 0f;
                lastAppliedSecondaryBoneAlpha = -1f;
                hasSecondarySkeletonPose = false;
            }
        }
        else
        {
            if (clearAlphaImmediate)
            {
                primarySkeletonAlpha = 0f;
                lastAppliedPrimaryBoneAlpha = -1f;
                hasPrimarySkeletonPose = false;
            }
        }
    }

    /// <summary>
    /// Menerima 21 titik mentah dunia tangan pada laju inferensi kamera dan memasukkannya ke <c>OneEuroFilter3D</c>
    /// Zero-GC per-sendi. Pada <c>freshEntry</c>, state filter direset bersih agar nol tarikan dari posisi lama. Zero-GC.
    /// </summary>
    public void PushHandSkeletonSample(int handSlot, Vector3[] rawJointsWorld, float sampleDt, bool freshEntry)
    {
        if (rawJointsWorld == null || rawJointsWorld.Length < SkeletonJointCount)
        {
            return;
        }

        OneEuroFilter3D[] filters = handSlot == 1 ? secondarySkeletonFilters : primarySkeletonFilters;
        Vector3[] smoothed = handSlot == 1 ? secondarySmoothedJoints : primarySmoothedJoints;

        if (freshEntry)
        {
            for (int i = 0; i < SkeletonJointCount; i++)
            {
                filters[i].Reset();
            }
        }

        for (int i = 0; i < SkeletonJointCount; i++)
        {
            Vector3 filtered = filters[i].PushSample(
                rawJointsWorld[i],
                sampleDt,
                SkeletonOneEuroMinCutoff,
                SkeletonOneEuroBeta,
                SkeletonOneEuroDCutoff,
                SkeletonOneEuroWorldSpeedScale);
            if (freshEntry)
            {
                smoothed[i] = filtered;
            }
        }

        if (handSlot == 1)
        {
            hasSecondarySkeletonPose = true;
        }
        else
        {
            hasPrimarySkeletonPose = true;
        }
    }

    /// <summary>
    /// Memajukan One-Euro 3D untuk seluruh 21 sendi skeleton pada laju render <c>dt</c> serta menerapkan transisi fade alpha
    /// halus selama occlusion grace / kemunculan / menghilang agar visual tangan tidak pernah berkedip tiba-tiba. Zero-GC.
    /// </summary>
    public void UpdateHandSkeletonVisual(
        int handSlot,
        bool isHandDetected,
        bool isInOcclusionGrace,
        float graceNormalized,
        bool forceHide,
        float dt,
        GameObject rigRootObj,
        GameObject[] jointSpheres,
        LineRenderer[] boneLines,
        int[,] boneConnections,
        Material boneMat,
        Material jointBaseMat,
        Material jointTipMat,
        Color baseBoneColor,
        Color baseJointColor,
        Color baseTipColor)
    {
        if (rigRootObj == null)
        {
            return;
        }

        bool isSecondary = handSlot == 1;
        OneEuroFilter3D[] filters = isSecondary ? secondarySkeletonFilters : primarySkeletonFilters;
        Vector3[] smoothed = isSecondary ? secondarySmoothedJoints : primarySmoothedJoints;
        bool hasPose = isSecondary ? hasSecondarySkeletonPose : hasPrimarySkeletonPose;
        float curAlpha = isSecondary ? secondarySkeletonAlpha : primarySkeletonAlpha;

        float safeDt = Mathf.Clamp(dt, 0.0005f, 0.050f);

        float targetAlpha;
        if (forceHide || !hasPose || !isHandDetected)
        {
            targetAlpha = 0f;
        }
        else if (isInOcclusionGrace)
        {
            targetAlpha = Mathf.Lerp(0.88f, 0.32f, Mathf.Clamp01(graceNormalized));
        }
        else
        {
            targetAlpha = 1f;
        }

        float fadeRate = targetAlpha >= curAlpha ? SkeletonFadeInRate : SkeletonFadeOutRate;
        float fadeStep = 1f - Mathf.Exp(-fadeRate * safeDt);
        curAlpha = Mathf.Lerp(curAlpha, targetAlpha, fadeStep);
        if (curAlpha <= FadeDisableEpsilon && targetAlpha <= 0.001f)
        {
            curAlpha = 0f;
        }

        if (isSecondary)
        {
            secondarySkeletonAlpha = curAlpha;
        }
        else
        {
            primarySkeletonAlpha = curAlpha;
        }

        if (curAlpha <= FadeDisableEpsilon)
        {
            if (rigRootObj.activeSelf)
            {
                rigRootObj.SetActive(false);
            }
            return;
        }

        if (hasPose)
        {
            for (int i = 0; i < SkeletonJointCount; i++)
            {
                smoothed[i] = filters[i].Tick(safeDt);
            }
        }

        bool justActivated = !rigRootObj.activeSelf;
        if (justActivated)
        {
            rigRootObj.SetActive(true);
        }

        float lastAppliedAlpha = isSecondary ? lastAppliedSecondaryBoneAlpha : lastAppliedPrimaryBoneAlpha;
        bool alphaDirty = justActivated || Mathf.Abs(curAlpha - lastAppliedAlpha) > 0.002f;
        Color boneCol = default;
        if (alphaDirty)
        {
            boneCol = new Color(baseBoneColor.r, baseBoneColor.g, baseBoneColor.b, baseBoneColor.a * curAlpha);
            Color jointCol = new Color(baseJointColor.r, baseJointColor.g, baseJointColor.b, baseJointColor.a * curAlpha);
            Color tipCol = new Color(baseTipColor.r, baseTipColor.g, baseTipColor.b, baseTipColor.a * curAlpha);

            if (boneMat != null) boneMat.color = boneCol;
            if (jointBaseMat != null) jointBaseMat.color = jointCol;
            if (jointTipMat != null) jointTipMat.color = tipCol;

            if (isSecondary) lastAppliedSecondaryBoneAlpha = curAlpha;
            else lastAppliedPrimaryBoneAlpha = curAlpha;
        }

        if (jointSpheres != null)
        {
            int sphereCount = Mathf.Min(jointSpheres.Length, SkeletonJointCount);
            for (int i = 0; i < sphereCount; i++)
            {
                if (jointSpheres[i] != null)
                {
                    jointSpheres[i].transform.position = smoothed[i];
                }
            }
        }

        if (boneLines != null && boneConnections != null)
        {
            int boneCount = Mathf.Min(boneLines.Length, boneConnections.GetLength(0));
            for (int b = 0; b < boneCount; b++)
            {
                LineRenderer lr = boneLines[b];
                if (lr != null)
                {
                    if (alphaDirty)
                    {
                        lr.startColor = boneCol;
                        lr.endColor = boneCol;
                    }
                    lr.SetPosition(0, smoothed[boneConnections[b, 0]]);
                    lr.SetPosition(1, smoothed[boneConnections[b, 1]]);
                }
            }
        }
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
        float scaleRatio = Mathf.Clamp(nodeScale / 0.68f, 0.22f, 15.0f);
        float widthScale = Mathf.Clamp(scaleRatio, 0.72f, 2.80f);

        Color auraColor;
        float outerRadius;
        float outerWidth;
        bool showInnerEquatorialRing = true;

        if (isInputA)
        {
            auraColor = InputACyanHoverColor;
            outerRadius = (0.310f * nodeScale) + (0.012f * scaleRatio * pulse);
            outerWidth = 0.0085f * widthScale;
        }
        else if (isInputB)
        {
            auraColor = InputBGoldHoverColor;
            outerRadius = (0.310f * nodeScale) + (0.012f * scaleRatio * pulse);
            outerWidth = 0.0085f * widthScale;
        }
        else if (state == NodeHoloState.ReturningToRack)
        {
            // Tint cincin 3D merah terang saat gerbang dipegang keluar dari kotak AssemblyZone
            auraColor = GateOutsideDeleteRedColor;
            outerRadius = (0.340f * nodeScale) + (0.018f * scaleRatio * pulse);
            outerWidth = 0.0110f * widthScale;
        }
        else if (state == NodeHoloState.Grabbed)
        {
            auraColor = GateGrabbedColor;
            outerRadius = (0.320f * nodeScale) + (0.014f * scaleRatio * pulse);
            outerWidth = 0.0092f * widthScale;
        }
        else
        {
            // BodyHovered pada gerbang
            auraColor = GateBodyHoverColor;
            outerRadius = (0.295f * nodeScale) + (0.010f * scaleRatio * pulse);
            outerWidth = 0.0075f * widthScale;
        }

        DrawAuraCircle(outer, nodeOuterMats[slotIndex], waistCenter, tableRight, tableForward, outerRadius, auraColor, outerWidth);

        if (showInnerEquatorialRing && inner != null)
        {
            float innerAlpha = state == NodeHoloState.ReturningToRack ? 0.68f : 0.48f;
            Color innerColor = new Color(auraColor.r, auraColor.g, auraColor.b, auraColor.a * innerAlpha);
            float innerWidth = (state == NodeHoloState.ReturningToRack ? 0.0062f : 0.0044f) * widthScale;
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
        float zoomRatio = Mathf.Clamp(scale / 0.68f, 0.22f, 15.0f);
        float widthScale = Mathf.Clamp(zoomRatio, 0.72f, 2.60f);
        Vector3 axisA = stageRoot != null ? stageRoot.right : nodeT.right;
        Vector3 axisB = stageRoot != null ? stageRoot.forward : nodeT.forward;
        Vector3 tableUp = stageRoot != null ? stageRoot.up : nodeT.up;
        Vector3 center = nodeT.position + (tableUp * (0.095f * scale));
        float radius = Mathf.Lerp(0.16f, 0.38f, eased) * zoomRatio;
        Color baseCol = isInputB ? InputBGoldHoverColor : InputACyanHoverColor;
        Color faded = new Color(baseCol.r, baseCol.g, baseCol.b, (1f - normalizedProgress) * 0.95f);
        float width = Mathf.Lerp(0.014f, 0.003f, normalizedProgress) * widthScale;

        DrawAuraCircle(lr, inputRippleMats[inputIndex], center, axisA, axisB, radius, faded, width);
    }

    /// <summary>
    /// Memperbarui reticle kursor telapak tangan primer dengan transisi fade alpha halus saat occlusion grace / hilang. Zero-GC.
    /// </summary>
    public void UpdatePalmReticle(
        Transform camT,
        bool isHandDetected,
        bool hideReticle,
        Vector3 palmWorldPos,
        bool isFistActive,
        bool isTargetingPort,
        float meanCurlRatio,
        bool isInOcclusionGrace = false,
        float graceNormalized = 0f,
        float dt = 0.016f,
        bool isCarryPanMode = false)
    {
        RenderPalmReticle(
            camT,
            isHandDetected,
            hideReticle,
            palmWorldPos,
            isFistActive,
            isTargetingPort,
            meanCurlRatio,
            isInOcclusionGrace,
            graceNormalized,
            dt,
            isCarryPanMode,
            ref primaryReticleFade,
            reticleOuterRenderer,
            reticleOuterMat,
            reticleInnerRenderer,
            reticleInnerMat,
            reticleCrossHRenderer,
            reticleCrossVRenderer,
            reticleCrossMat,
            isSecondary: false);
    }

    /// <summary>
    /// Memperbarui reticle kursor telapak tangan sekunder dengan transisi fade alpha halus saat occlusion grace / hilang. Zero-GC.
    /// </summary>
    public void UpdateSecondaryPalmReticle(
        Transform camT,
        bool isHandDetected,
        bool hideReticle,
        Vector3 palmWorldPos,
        bool isFistActive,
        bool isTargetingPort,
        float meanCurlRatio,
        bool isInOcclusionGrace = false,
        float graceNormalized = 0f,
        float dt = 0.016f,
        bool isCarryPanMode = false)
    {
        RenderPalmReticle(
            camT,
            isHandDetected,
            hideReticle,
            palmWorldPos,
            isFistActive,
            isTargetingPort,
            meanCurlRatio,
            isInOcclusionGrace,
            graceNormalized,
            dt,
            isCarryPanMode,
            ref secondaryReticleFade,
            secondaryReticleOuterRenderer,
            secondaryReticleOuterMat,
            secondaryReticleInnerRenderer,
            secondaryReticleInnerMat,
            secondaryReticleCrossHRenderer,
            secondaryReticleCrossVRenderer,
            secondaryReticleCrossMat,
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
        bool isInOcclusionGrace,
        float graceNormalized,
        float dt,
        bool isCarryPanMode,
        ref ReticleFadeState fadeState,
        LineRenderer outerLr,
        Material outerMat,
        LineRenderer innerLr,
        Material innerMat,
        LineRenderer crossHLr,
        LineRenderer crossVLr,
        Material crossMat,
        bool isSecondary)
    {
        if (!isInitialized)
        {
            return;
        }

        bool show = isHandDetected && !hideReticle && camT != null;
        if (show)
        {
            fadeState.hasPose = true;
            fadeState.lastPos = palmWorldPos;
            fadeState.lastIsFist = isFistActive;
            fadeState.lastIsTargetingPort = isTargetingPort;
            fadeState.lastIsCarryPan = isCarryPanMode;
            fadeState.lastMeanCurlRatio = meanCurlRatio;
        }

        float safeDt = Mathf.Clamp(dt, 0.0005f, 0.050f);
        float targetAlpha;
        if (!show || !fadeState.hasPose)
        {
            targetAlpha = 0f;
        }
        else if (isInOcclusionGrace)
        {
            targetAlpha = Mathf.Lerp(0.86f, 0.34f, Mathf.Clamp01(graceNormalized));
        }
        else
        {
            targetAlpha = 1f;
        }

        float fadeRate = targetAlpha >= fadeState.alpha ? ReticleFadeInRate : ReticleFadeOutRate;
        fadeState.alpha = Mathf.Lerp(fadeState.alpha, targetAlpha, 1f - Mathf.Exp(-fadeRate * safeDt));
        if (fadeState.alpha <= FadeDisableEpsilon && targetAlpha <= 0.001f)
        {
            fadeState.alpha = 0f;
        }

        if (fadeState.alpha <= FadeDisableEpsilon || camT == null || !fadeState.hasPose)
        {
            if (isSecondary) SetSecondaryReticleVisible(false);
            else SetReticleVisible(false);
            return;
        }

        Vector3 right = camT.right;
        Vector3 up = camT.up;
        Vector3 center = fadeState.lastPos - (camT.forward * 0.14f);

        float openFrac = Mathf.Clamp01((fadeState.lastMeanCurlRatio - 0.78f) / 0.75f);
        float outerRadius;
        float innerRadius;
        float crossHalf;
        float outerWidth;
        float innerWidth;
        Color baseColor;

        if (fadeState.lastIsCarryPan)
        {
            float carryPulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * 8.5f));
            baseColor = TwoHandFreePanGoldColor;
            outerRadius = 0.044f + (0.005f * carryPulse);
            innerRadius = 0.017f;
            crossHalf = 0.026f;
            outerWidth = 0.012f;
            innerWidth = 0.0085f;
        }
        else
        {
            outerRadius = fadeState.lastIsFist ? 0.036f : Mathf.Lerp(0.044f, 0.074f, openFrac);
            innerRadius = fadeState.lastIsFist ? 0.015f : 0.018f;
            crossHalf = fadeState.lastIsFist ? 0.022f : 0.030f;
            outerWidth = fadeState.lastIsFist ? 0.013f : 0.009f;
            innerWidth = fadeState.lastIsFist ? 0.009f : 0.006f;
            baseColor = fadeState.lastIsFist
                ? ReticleFistColor
                : (fadeState.lastIsTargetingPort ? ReticlePortAimColor : ReticleOpenColor);
        }

        Color color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * fadeState.alpha);
        Color coreCol = new Color(ReticleCoreWhiteColor.r, ReticleCoreWhiteColor.g, ReticleCoreWhiteColor.b, ReticleCoreWhiteColor.a * fadeState.alpha);

        DrawReticleCircle(outerLr, outerMat, center, right, up, outerRadius, color, outerWidth);
        DrawReticleCircle(innerLr, innerMat, center - (camT.forward * 0.004f), right, up, innerRadius, coreCol, innerWidth);
        if (crossHLr != null && crossVLr != null)
        {
            if (crossMat != null) crossMat.color = coreCol;
            Vector3 crossCenter = center - (camT.forward * 0.006f);
            crossHLr.enabled = true;
            crossHLr.startColor = coreCol;
            crossHLr.endColor = coreCol;
            crossHLr.SetPosition(0, crossCenter - (right * crossHalf));
            crossHLr.SetPosition(1, crossCenter + (right * crossHalf));
            crossVLr.enabled = true;
            crossVLr.startColor = coreCol;
            crossVLr.endColor = coreCol;
            crossVLr.SetPosition(0, crossCenter - (up * crossHalf));
            crossVLr.SetPosition(1, crossCenter + (up * crossHalf));
        }
    }

    /// <summary>
    /// Menampilkan indikator cincin cubit kecil presisi di titik tengah #4-#8 dengan fade alpha halus saat occlusion grace. Zero-GC.
    /// </summary>
    public void UpdatePinchIndicator(
        Transform camT,
        bool isHandDetected,
        bool isAllowedAndRelevant,
        Vector3 pinchWorldPos,
        bool isPinchActive,
        bool isSnappedToPort,
        bool isInOcclusionGrace = false,
        float graceNormalized = 0f,
        float dt = 0.016f)
    {
        RenderPinchIndicator(
            camT,
            isHandDetected,
            isAllowedAndRelevant,
            pinchWorldPos,
            isPinchActive,
            isSnappedToPort,
            isInOcclusionGrace,
            graceNormalized,
            dt,
            ref primaryPinchFade,
            pinchRingRenderer,
            pinchRingMat,
            pinchCoreRenderer,
            pinchCoreMat,
            isSecondary: false);
    }

    /// <summary>
    /// Menampilkan indikator cincin cubit tangan kedua dengan fade alpha halus saat occlusion grace. Zero-GC.
    /// </summary>
    public void UpdateSecondaryPinchIndicator(
        Transform camT,
        bool isHandDetected,
        bool isAllowedAndRelevant,
        Vector3 pinchWorldPos,
        bool isPinchActive,
        bool isSnappedToPort,
        bool isInOcclusionGrace = false,
        float graceNormalized = 0f,
        float dt = 0.016f)
    {
        RenderPinchIndicator(
            camT,
            isHandDetected,
            isAllowedAndRelevant,
            pinchWorldPos,
            isPinchActive,
            isSnappedToPort,
            isInOcclusionGrace,
            graceNormalized,
            dt,
            ref secondaryPinchFade,
            secondaryPinchRingRenderer,
            secondaryPinchRingMat,
            secondaryPinchCoreRenderer,
            secondaryPinchCoreMat,
            isSecondary: true);
    }

    private void RenderPinchIndicator(
        Transform camT,
        bool isHandDetected,
        bool isAllowedAndRelevant,
        Vector3 pinchWorldPos,
        bool isPinchActive,
        bool isSnappedToPort,
        bool isInOcclusionGrace,
        float graceNormalized,
        float dt,
        ref PinchFadeState fadeState,
        LineRenderer ringLr,
        Material ringMat,
        LineRenderer coreLr,
        Material coreMat,
        bool isSecondary)
    {
        if (!isInitialized)
        {
            return;
        }

        bool show = isHandDetected && isAllowedAndRelevant && camT != null;
        if (show)
        {
            fadeState.hasPose = true;
            fadeState.lastPos = pinchWorldPos;
            fadeState.lastIsPinchActive = isPinchActive;
            fadeState.lastIsSnapped = isSnappedToPort;
        }

        float safeDt = Mathf.Clamp(dt, 0.0005f, 0.050f);
        float targetAlpha;
        if (!show || !fadeState.hasPose)
        {
            targetAlpha = 0f;
        }
        else if (isInOcclusionGrace)
        {
            targetAlpha = Mathf.Lerp(0.86f, 0.34f, Mathf.Clamp01(graceNormalized));
        }
        else
        {
            targetAlpha = 1f;
        }

        float fadeRate = targetAlpha >= fadeState.alpha ? ReticleFadeInRate : ReticleFadeOutRate;
        fadeState.alpha = Mathf.Lerp(fadeState.alpha, targetAlpha, 1f - Mathf.Exp(-fadeRate * safeDt));
        if (fadeState.alpha <= FadeDisableEpsilon && targetAlpha <= 0.001f)
        {
            fadeState.alpha = 0f;
        }

        if (fadeState.alpha <= FadeDisableEpsilon || camT == null || !fadeState.hasPose)
        {
            if (isSecondary) SetSecondaryPinchIndicatorVisible(false);
            else SetPinchIndicatorVisible(false);
            return;
        }

        Vector3 right = camT.right;
        Vector3 up = camT.up;
        Vector3 center = fadeState.lastPos - (camT.forward * 0.145f);
        float pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * 8.0f));
        float outerR = fadeState.lastIsPinchActive ? (0.020f + (0.003f * pulse)) : 0.032f;
        float coreR = fadeState.lastIsPinchActive ? 0.0085f : 0.0065f;
        Color baseRingCol = fadeState.lastIsSnapped
            ? PinchSnappedColor
            : (fadeState.lastIsPinchActive ? PinchActiveDragColor : PinchHoverColor);
        Color ringCol = new Color(baseRingCol.r, baseRingCol.g, baseRingCol.b, baseRingCol.a * fadeState.alpha);
        Color coreCol = new Color(ReticleCoreWhiteColor.r, ReticleCoreWhiteColor.g, ReticleCoreWhiteColor.b, ReticleCoreWhiteColor.a * fadeState.alpha);

        DrawPinchCircle(ringLr, ringMat, center, right, up, outerR, ringCol, fadeState.lastIsPinchActive ? 0.011f : 0.0085f);
        DrawPinchCircle(coreLr, coreMat, center - (camT.forward * 0.004f), right, up, coreR, coreCol, 0.007f);
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
            auraBuffer[i] = center + (axisA * (auraCosTable[i] * radius)) + (axisB * (auraSinTable[i] * radius));
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
            reticleBuffer[i] = center + (axisA * (reticleCosTable[i] * radius)) + (axisB * (reticleSinTable[i] * radius));
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
            pinchRingBuffer[i] = center + (axisA * (pinchCosTable[i] * radius)) + (axisB * (pinchSinTable[i] * radius));
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
        secondaryReticleInnerMat = null;
        secondaryReticleCrossMat = null;
        pinchRingRenderer = null;
        pinchCoreRenderer = null;
        pinchRingMat = null;
        pinchCoreMat = null;
        secondaryPinchRingRenderer = null;
        secondaryPinchCoreRenderer = null;
        secondaryPinchRingMat = null;
        secondaryPinchCoreMat = null;
    }
}
