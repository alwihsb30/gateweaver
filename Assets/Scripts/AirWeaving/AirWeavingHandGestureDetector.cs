using System.Collections.Generic;
using UnityEngine;
using NormalizedLandmark = Mediapipe.Tasks.Components.Containers.NormalizedLandmark;

/// <summary>
/// Detektor gestur dual-kanal untuk GateWeaver AR v7.0 (Fase 3 + Fase 4 + Polishing Final):
/// - Kanal Objek (Fist / Kepal vs Open): skala-invarian 4 jari utama, Schmitt-trigger hysteresis lebar (masuk < 0.88,
///   keluar > 1.18 / sticky > 1.15 & <= 1 jari tertekuk), asymmetric debounce (45 ms masuk / 55-115 ms adaptif keluar),
///   occlusion grace 180 ms, dan kursor telapak anti-lompat (#9/#5/#0).
/// - Kanal Kabel (Pinch / Cubit #4-#8): skala-invarian (masuk < 0.34 selama 35 ms & bukan kepalan penuh, keluar > 0.50
///   dengan debounce adaptif 55-110 ms & occlusion grace 180 ms), titik bidik presisi 0.5*(#4+#8), serta pre-pinch guard
///   (< 0.42) yang menahan SummonHandPose agar cubit tidak pernah memicu 3 jari (XOR).
/// - Kanal Circle Summon: klasifikasi berbasis jumlah jari utama terbuka (1=AND, 2=OR, 3=XOR, 4+jempol lipat=NAND,
///   0+jempol kuat=NOT, OpenFiveBloom). 100% Zero-GC.
/// </summary>
public enum SummonHandPose
{
    None = 0,
    OneFingerAnd = 1,
    TwoFingersOr = 2,
    ThreeFingersXor = 3,
    FourFingersNand = 4,
    ThumbOnlyNot = 5,
    OpenFiveBloom = 6
}

public sealed class AirWeavingHandGestureDetector
{
    // Parameter Fist/Open: masuk rileks (0.94f, 30 ms), keluar sticky-drag Fase 3 TERKUNCI (1.15f, <= 1, 55-115 ms, 180 ms)
    private const float MinPalmScaleClamp = 0.055f;
    private const float FistEnterMeanRatio = 0.94f;
    private const float FistExitMeanRatio = 1.18f;
    private const float FistStickyExitMeanRatio = 1.15f;
    private const float FingerFoldedPalmRatio = 0.92f;
    private const float FingerFoldedPipRatio = 1.04f;
    private const float PreFistLatchMeanRatio = 1.05f;

    private const float DebounceEnterSeconds = 0.030f;
    private const float DebounceExitSlowSeconds = 0.055f;
    private const float DebounceExitFastSeconds = 0.115f;
    private const float SlowReleaseSpeedThreshold = 1.50f;
    private const float FastReleaseSpeedThreshold = 2.50f;
    private const float OcclusionGraceSeconds = 0.180f;
    private const float StickyOcclusionGraceSeconds = 0.180f;
    private const float PreFistLatchMaxSeconds = 0.060f;

    // Parameter Pinch / Cubit Kabel (rileks anti-kaku + guard anti-kepalan & anti-XOR)
    private const float PinchEnterRatio = 0.42f;
    private const float PinchExitRatio = 0.56f;
    private const float PrePinchSuppressSummonRatio = 0.48f;
    private const float PinchEnterDebounceSeconds = 0.025f;
    private const float PinchExitSlowSeconds = 0.045f;
    private const float PinchExitFastSeconds = 0.095f;
    private const float PinchOcclusionGraceSeconds = 0.180f;
    private const float PrePinchLatchMaxSeconds = 0.055f;

    // Smoothing adaptif berbasis kecepatan (One-Euro-style) untuk palm reticle & titik cubit:
    // halus stabil saat membidik orb kecil (alpha ~0.24), responsif tanpa lag saat berpindah cepat (alpha ~0.78)
    private const float CursorSmoothAlphaMin = 0.24f;
    private const float CursorSmoothAlphaMax = 0.78f;
    private const float CursorSmoothLowSpeed = 0.35f;
    private const float CursorSmoothHighSpeed = 2.60f;

    // Parameter klasifikasi 5 pose Summon + Open-5 Bloom (Fase 4)
    private const float SummonFingerExtendPalmRatio = 1.00f;
    private const float SummonFingerExtendPipRatio = 1.02f;
    private const float SummonFingerFoldPalmRatio = 0.88f;
    private const float SummonFingerFoldPipRatio = 0.98f;
    private const float ThumbBloomIndexRatio = 0.46f;
    private const float ThumbBloomPinkyRatio = 0.90f;
    private const float ThumbStrongNotIndexRatio = 0.52f;
    private const float ThumbStrongNotPinkyRatio = 0.96f;
    private const float OpenFiveBloomMeanRatio = 1.12f;

    private static readonly int[] FingerTipIndices = { 8, 12, 16, 20 };
    private static readonly int[] FingerPipIndices = { 6, 10, 14, 18 };

    // State Fist / Open (Objek)
    private float meanCurlRatio = 1.50f;
    private int foldedFingerCount = 0;
    private bool rawEnterCondition = false;
    private bool rawExitCondition = true;

    private bool isFistActive = false;
    private bool fistJustStarted = false;
    private bool fistJustEnded = false;
    private bool isPreFistCurling = false;

    private float enterHoldTimer = 0f;
    private float exitHoldTimer = 0f;
    private float occlusionGraceTimer = 0f;
    private float preFistLatchTimer = 0f;
    private bool hasPrevCursor = false;
    private Vector2 prevCursorLocalXY = Vector2.zero;
    private Vector2 latchedCursorLocalXY = Vector2.zero;
    private bool hasLatchedCursor = false;

    // State Pinch / Cubit (Kabel)
    private float currentPinchRatio = 1.00f;
    private bool rawPinchEnterCondition = false;
    private bool rawPinchExitCondition = true;
    private bool isPrePinchGuardActive = false;
    private bool isPinchActive = false;
    private bool pinchJustStarted = false;
    private bool pinchJustEnded = false;
    private float pinchEnterHoldTimer = 0f;
    private float pinchExitHoldTimer = 0f;
    private float pinchOcclusionGraceTimer = 0f;
    private float prePinchLatchTimer = 0f;
    private bool isPrePinchClosing = false;
    private bool hasPrevPinchCursor = false;
    private Vector2 prevPinchLocalXY = Vector2.zero;
    private Vector2 latchedPinchLocalXY = Vector2.zero;
    private bool hasLatchedPinch = false;

    // State smoothing adaptif One-Euro-style untuk kursor telapak & titik cubit
    private bool hasSmoothedCursors = false;
    private Vector3 smoothedPalmLocal = Vector3.zero;
    private Vector3 smoothedPinchLocal = Vector3.zero;
    private Vector2 prevRawPalmLocalXY = Vector2.zero;
    private Vector2 prevRawPinchLocalXY = Vector2.zero;
    private float lastLandmarkUpdateUnscaledTime = -1f;

    // State Summon 5 Gerbang
    private readonly bool[] mainFingerExtended = new bool[4];
    private bool isThumbExtended = false;
    private bool isThumbStrongOut = false;
    private bool isRelaxedOpenFiveBloom = false;
    private SummonHandPose currentSummonPose = SummonHandPose.None;

    public bool IsFistActive => isFistActive;
    public bool FistJustStarted => fistJustStarted;
    public bool FistJustEnded => fistJustEnded;
    public bool IsPreFistCurling => isPreFistCurling;
    public float MeanCurlRatio => meanCurlRatio;
    public int FoldedFingerCount => foldedFingerCount;

    public bool IsPinchActive => isPinchActive;
    public bool PinchJustStarted => pinchJustStarted;
    public bool PinchJustEnded => pinchJustEnded;
    public bool IsPrePinchSuppressingSummon => isPinchActive || isPrePinchGuardActive;
    public float PinchRatio => currentPinchRatio;
    public float PinchSpeedUnitsPerSec { get; private set; }
    public Vector3 PinchPointWorldPosition { get; private set; }
    public Vector2 PinchPointLocalXY { get; private set; }
    public Vector2 StabilizedPinchLocalXY => hasLatchedPinch ? latchedPinchLocalXY : PinchPointLocalXY;

    public SummonHandPose CurrentSummonPose => currentSummonPose;
    public bool IsOpenFiveBloom => currentSummonPose == SummonHandPose.OpenFiveBloom;
    public bool IsRelaxedOpenFiveBloom => isRelaxedOpenFiveBloom;
    public bool IsIndexFingerExtended => mainFingerExtended[0];
    public float CursorSpeedUnitsPerSec { get; private set; }
    public Vector3 PalmCursorWorldPosition { get; private set; }
    public Vector2 PalmCursorLocalXY { get; private set; }
    public Vector2 IndexTipLocalXY { get; private set; }
    public Vector2 ThumbTipLocalXY { get; private set; }
    public Vector2 ActiveSummonTipLocalXY { get; private set; }
    public Vector2 StabilizedCursorLocalXY => hasLatchedCursor ? latchedCursorLocalXY : PalmCursorLocalXY;

    public void Reset()
    {
        meanCurlRatio = 1.50f;
        foldedFingerCount = 0;
        rawEnterCondition = false;
        rawExitCondition = true;
        isFistActive = false;
        fistJustStarted = false;
        fistJustEnded = false;
        isPreFistCurling = false;
        enterHoldTimer = 0f;
        exitHoldTimer = 0f;
        occlusionGraceTimer = 0f;
        preFistLatchTimer = 0f;
        hasPrevCursor = false;
        prevCursorLocalXY = Vector2.zero;
        latchedCursorLocalXY = Vector2.zero;
        hasLatchedCursor = false;

        currentPinchRatio = 1.00f;
        rawPinchEnterCondition = false;
        rawPinchExitCondition = true;
        isPrePinchGuardActive = false;
        isPinchActive = false;
        pinchJustStarted = false;
        pinchJustEnded = false;
        pinchEnterHoldTimer = 0f;
        pinchExitHoldTimer = 0f;
        pinchOcclusionGraceTimer = 0f;
        prePinchLatchTimer = 0f;
        isPrePinchClosing = false;
        hasPrevPinchCursor = false;
        prevPinchLocalXY = Vector2.zero;
        latchedPinchLocalXY = Vector2.zero;
        hasLatchedPinch = false;

        hasSmoothedCursors = false;
        smoothedPalmLocal = Vector3.zero;
        smoothedPinchLocal = Vector3.zero;
        prevRawPalmLocalXY = Vector2.zero;
        prevRawPinchLocalXY = Vector2.zero;
        lastLandmarkUpdateUnscaledTime = -1f;

        for (int i = 0; i < 4; i++)
        {
            mainFingerExtended[i] = false;
        }
        isThumbExtended = false;
        isThumbStrongOut = false;
        isRelaxedOpenFiveBloom = false;
        currentSummonPose = SummonHandPose.None;

        CursorSpeedUnitsPerSec = 0f;
        PinchSpeedUnitsPerSec = 0f;
        PalmCursorWorldPosition = Vector3.zero;
        PalmCursorLocalXY = Vector2.zero;
        PinchPointWorldPosition = Vector3.zero;
        PinchPointLocalXY = Vector2.zero;
        IndexTipLocalXY = Vector2.zero;
        ThumbTipLocalXY = Vector2.zero;
        ActiveSummonTipLocalXY = Vector2.zero;
    }

    /// <summary>
    /// Memperbarui fitur geometris landmark 21 titik MediaPipe setiap frame inferensi baru masuk. Zero-GC.
    /// </summary>
    public void UpdateFromLandmarks(List<NormalizedLandmark> landmarks, Vector3[] smoothedLandmarksWorld, Transform camTransform)
    {
        if (landmarks == null || landmarks.Count < 21 || smoothedLandmarksWorld == null || smoothedLandmarksWorld.Length < 21 || camTransform == null)
        {
            return;
        }

        NormalizedLandmark l0 = landmarks[0];
        NormalizedLandmark l4 = landmarks[4];
        NormalizedLandmark l5 = landmarks[5];
        NormalizedLandmark l8 = landmarks[8];
        NormalizedLandmark l9 = landmarks[9];
        NormalizedLandmark l13 = landmarks[13];
        NormalizedLandmark l17 = landmarks[17];

        // 1. Skala telapak ganda (tahan rotasi miring maupun jarak kamera)
        float dWristMid = Dist2D(l0, l9);
        float dPalmWidth = Dist2D(l5, l17);
        float palmScale = Mathf.Max(MinPalmScaleClamp, 0.5f * (dWristMid + dPalmWidth));

        // 2. Pusat telapak (centroid 5 titik kaku)
        float cx = (l0.x + l5.x + l9.x + l13.x + l17.x) * 0.20f;
        float cy = (l0.y + l5.y + l9.y + l13.y + l17.y) * 0.20f;

        // 3. Evaluasi kelengkungan 4 jari utama (Telunjuk, Tengah, Manis, Kelingking)
        float sumRatio = 0f;
        int folded = 0;
        int otherThreeFolded = 0;
        bool indexBuriedInPalm = false;

        for (int i = 0; i < 4; i++)
        {
            NormalizedLandmark tip = landmarks[FingerTipIndices[i]];
            NormalizedLandmark pip = landmarks[FingerPipIndices[i]];

            float dxC = tip.x - cx;
            float dyC = tip.y - cy;
            float tipToCenter = Mathf.Sqrt((dxC * dxC) + (dyC * dyC));
            float rPalm = tipToCenter / palmScale;
            sumRatio += rPalm;

            float tipToWrist = Dist2D(tip, l0);
            float pipToWrist = Mathf.Max(0.02f, Dist2D(pip, l0));
            float rPip = tipToWrist / pipToWrist;

            bool fingerFolded = rPalm < FingerFoldedPalmRatio || rPip < FingerFoldedPipRatio;
            if (fingerFolded)
            {
                folded++;
                if (i >= 1)
                {
                    otherThreeFolded++;
                }
            }

            if (i == 0)
            {
                // Telunjuk dianggap tenggelam di pusat telapak bila kedua rasio tertekuk rapat (ciri kepalan penuh)
                indexBuriedInPalm = rPalm < FingerFoldedPalmRatio && rPip < FingerFoldedPipRatio;
            }

            // Schmitt-trigger per-jari rileks untuk klasifikasi 1-4 jari anti-flicker (Fase 4)
            if (!mainFingerExtended[i])
            {
                if (rPalm > SummonFingerExtendPalmRatio && rPip > SummonFingerExtendPipRatio)
                {
                    mainFingerExtended[i] = true;
                }
            }
            else if (rPalm < SummonFingerFoldPalmRatio || rPip < SummonFingerFoldPipRatio)
            {
                mainFingerExtended[i] = false;
            }
        }

        meanCurlRatio = sumRatio * 0.25f;
        foldedFingerCount = folded;
        rawEnterCondition = (meanCurlRatio < FistEnterMeanRatio) && (foldedFingerCount >= 3);
        rawExitCondition = (meanCurlRatio > FistExitMeanRatio) && (foldedFingerCount <= 1);

        // 3A. Evaluasi Pinch / Cubit skala-invarian (#4-#8) dengan guard anti-kepalan penuh
        currentPinchRatio = Dist2D(l4, l8) / palmScale;
        bool isFullFistShape = rawEnterCondition && otherThreeFolded >= 3 && indexBuriedInPalm;
        bool notFullFistForPinch = !isFullFistShape && (otherThreeFolded < 3 || !indexBuriedInPalm);
        rawPinchEnterCondition = currentPinchRatio < PinchEnterRatio && notFullFistForPinch;
        rawPinchExitCondition = currentPinchRatio > PinchExitRatio;
        isPrePinchGuardActive = currentPinchRatio < PrePinchSuppressSummonRatio && notFullFistForPinch;

        // 3B. Evaluasi ibu jari orientation-invariant + pemetaan 5 gerbang & Open-5 Bloom
        float dThumbIndex = Dist2D(l4, l5) / palmScale;
        float dThumbPinky = Dist2D(l4, l17) / palmScale;
        isThumbExtended = dThumbIndex > ThumbBloomIndexRatio && dThumbPinky > ThumbBloomPinkyRatio;
        isThumbStrongOut = dThumbIndex > ThumbStrongNotIndexRatio && dThumbPinky > ThumbStrongNotPinkyRatio;

        int extendedMainCount = (mainFingerExtended[0] ? 1 : 0)
                                + (mainFingerExtended[1] ? 1 : 0)
                                + (mainFingerExtended[2] ? 1 : 0)
                                + (mainFingerExtended[3] ? 1 : 0);

        isRelaxedOpenFiveBloom = extendedMainCount >= 3 && meanCurlRatio > OpenFiveBloomMeanRatio && isThumbExtended && !isPrePinchGuardActive;

        // Anti-Konflik XOR: saat mencubit atau pre-pinch (< 0.42f), 3 jari terbuka lain tidak boleh memicu 3 jari (XOR)
        if (isPinchActive || isPrePinchGuardActive)
        {
            currentSummonPose = SummonHandPose.None;
        }
        else if (extendedMainCount == 4 && isThumbExtended && meanCurlRatio > OpenFiveBloomMeanRatio)
        {
            currentSummonPose = SummonHandPose.OpenFiveBloom;
        }
        else if (extendedMainCount == 4 && !isThumbExtended)
        {
            // Khusus 4 jari (NAND): wajib ibu jari terlipat (<= 0.46 || <= 0.90) agar tidak bentrok dengan OpenFiveBloom
            currentSummonPose = SummonHandPose.FourFingersNand;
        }
        else if (extendedMainCount == 3)
        {
            currentSummonPose = SummonHandPose.ThreeFingersXor;
        }
        else if (extendedMainCount == 2)
        {
            currentSummonPose = SummonHandPose.TwoFingersOr;
        }
        else if (extendedMainCount == 1)
        {
            currentSummonPose = SummonHandPose.OneFingerAnd;
        }
        else if (extendedMainCount == 0 && foldedFingerCount >= 3 && isThumbStrongOut)
        {
            currentSummonPose = SummonHandPose.ThumbOnlyNot;
        }
        else
        {
            currentSummonPose = SummonHandPose.None;
        }

        // 4. Kursor telapak anti-lompat (0.50*L9 + 0.30*L5 + 0.20*L0) & titik cubit presisi (0.5*(L4 + L8))
        // dengan smoothing adaptif berbasis kecepatan (alpha 0.24 saat pelan -> 0.78 saat berpindah cepat)
        Vector3 rawPalmWorld = (smoothedLandmarksWorld[9] * 0.50f)
                               + (smoothedLandmarksWorld[5] * 0.30f)
                               + (smoothedLandmarksWorld[0] * 0.20f);
        Vector3 rawPalmLocal = camTransform.InverseTransformPoint(rawPalmWorld);
        Vector2 rawPalmXY = new Vector2(rawPalmLocal.x, rawPalmLocal.y);

        Vector3 rawPinchWorld = (smoothedLandmarksWorld[4] + smoothedLandmarksWorld[8]) * 0.5f;
        Vector3 rawPinchLocal = camTransform.InverseTransformPoint(rawPinchWorld);
        Vector2 rawPinchXY = new Vector2(rawPinchLocal.x, rawPinchLocal.y);

        float nowTime = Time.unscaledTime;
        float sampleDt = (lastLandmarkUpdateUnscaledTime >= 0f)
            ? Mathf.Clamp(nowTime - lastLandmarkUpdateUnscaledTime, 0.008f, 0.150f)
            : 0.033f;
        lastLandmarkUpdateUnscaledTime = nowTime;

        if (!hasSmoothedCursors)
        {
            smoothedPalmLocal = rawPalmLocal;
            smoothedPinchLocal = rawPinchLocal;
            prevRawPalmLocalXY = rawPalmXY;
            prevRawPinchLocalXY = rawPinchXY;
            hasSmoothedCursors = true;
        }
        else
        {
            float rawPalmSpeed = Vector2.Distance(rawPalmXY, prevRawPalmLocalXY) / sampleDt;
            float palmSpeedT = Mathf.Clamp01((rawPalmSpeed - CursorSmoothLowSpeed) / (CursorSmoothHighSpeed - CursorSmoothLowSpeed));
            float palmAlpha = Mathf.Lerp(CursorSmoothAlphaMin, CursorSmoothAlphaMax, palmSpeedT);
            smoothedPalmLocal = Vector3.Lerp(smoothedPalmLocal, rawPalmLocal, palmAlpha);
            prevRawPalmLocalXY = rawPalmXY;

            float rawPinchSpeed = Vector2.Distance(rawPinchXY, prevRawPinchLocalXY) / sampleDt;
            float pinchSpeedT = Mathf.Clamp01((rawPinchSpeed - CursorSmoothLowSpeed) / (CursorSmoothHighSpeed - CursorSmoothLowSpeed));
            float pinchAlpha = Mathf.Lerp(CursorSmoothAlphaMin, CursorSmoothAlphaMax, pinchSpeedT);
            smoothedPinchLocal = Vector3.Lerp(smoothedPinchLocal, rawPinchLocal, pinchAlpha);
            prevRawPinchLocalXY = rawPinchXY;
        }

        PalmCursorLocalXY = new Vector2(smoothedPalmLocal.x, smoothedPalmLocal.y);
        PalmCursorWorldPosition = camTransform.TransformPoint(smoothedPalmLocal);

        PinchPointLocalXY = new Vector2(smoothedPinchLocal.x, smoothedPinchLocal.y);
        PinchPointWorldPosition = camTransform.TransformPoint(smoothedPinchLocal);

        Vector3 indexLocal = camTransform.InverseTransformPoint(smoothedLandmarksWorld[8]);
        IndexTipLocalXY = new Vector2(indexLocal.x, indexLocal.y);
        Vector3 thumbLocal = camTransform.InverseTransformPoint(smoothedLandmarksWorld[4]);
        ThumbTipLocalXY = new Vector2(thumbLocal.x, thumbLocal.y);

        if (currentSummonPose == SummonHandPose.ThumbOnlyNot)
        {
            ActiveSummonTipLocalXY = ThumbTipLocalXY;
        }
        else if (mainFingerExtended[0])
        {
            ActiveSummonTipLocalXY = IndexTipLocalXY;
        }
        else
        {
            int activeTipLandmarkIdx = 8;
            for (int i = 1; i < 4; i++)
            {
                if (mainFingerExtended[i])
                {
                    activeTipLandmarkIdx = FingerTipIndices[i];
                    break;
                }
            }
            Vector3 activeTipLocal = camTransform.InverseTransformPoint(smoothedLandmarksWorld[activeTipLandmarkIdx]);
            ActiveSummonTipLocalXY = new Vector2(activeTipLocal.x, activeTipLocal.y);
        }
    }

    /// <summary>
    /// Memproses hysteresis, asymmetric debounce, velocity guard, sticky-hold, & occlusion grace
    /// untuk kanal Fist (objek) dan kanal Pinch (kabel) secara saling terpisah. Zero-GC.
    /// </summary>
    public void TickTemporalState(float dt, bool isHandDetected, bool isFistStickyHoldActive = false, bool isPinchStickyHoldActive = false)
    {
        fistJustStarted = false;
        fistJustEnded = false;
        pinchJustStarted = false;
        pinchJustEnded = false;

        if (dt <= 0.0001f)
        {
            dt = 0.016f;
        }

        if (isHandDetected)
        {
            occlusionGraceTimer = isFistStickyHoldActive ? StickyOcclusionGraceSeconds : OcclusionGraceSeconds;
            pinchOcclusionGraceTimer = PinchOcclusionGraceSeconds;

            if (hasPrevCursor)
            {
                float instantSpeed = Vector2.Distance(PalmCursorLocalXY, prevCursorLocalXY) / dt;
                CursorSpeedUnitsPerSec = Mathf.Lerp(CursorSpeedUnitsPerSec, instantSpeed, Mathf.Clamp01(dt * 12f));
            }
            prevCursorLocalXY = PalmCursorLocalXY;
            hasPrevCursor = true;

            if (hasPrevPinchCursor)
            {
                float instantPinchSpeed = Vector2.Distance(PinchPointLocalXY, prevPinchLocalXY) / dt;
                PinchSpeedUnitsPerSec = Mathf.Lerp(PinchSpeedUnitsPerSec, instantPinchSpeed, Mathf.Clamp01(dt * 12f));
            }
            prevPinchLocalXY = PinchPointLocalXY;
            hasPrevPinchCursor = true;
        }
        else
        {
            CursorSpeedUnitsPerSec = 0f;
            PinchSpeedUnitsPerSec = 0f;
            hasSmoothedCursors = false;
            lastLandmarkUpdateUnscaledTime = -1f;
            enterHoldTimer = 0f;
            pinchEnterHoldTimer = 0f;
            isPreFistCurling = false;
            hasLatchedCursor = false;
            preFistLatchTimer = 0f;
            isPrePinchClosing = false;
            hasLatchedPinch = false;
            prePinchLatchTimer = 0f;

            if (isFistActive)
            {
                occlusionGraceTimer -= dt;
                if (occlusionGraceTimer <= 0f)
                {
                    isFistActive = false;
                    fistJustEnded = true;
                    exitHoldTimer = 0f;
                }
            }

            if (isPinchActive)
            {
                pinchOcclusionGraceTimer -= dt;
                if (pinchOcclusionGraceTimer <= 0f)
                {
                    isPinchActive = false;
                    pinchJustEnded = true;
                    pinchExitHoldTimer = 0f;
                }
            }
            return;
        }

        // ---------------------------------------------------------------------
        // KANAL 1: PINCH / CUBIT (KHUSUS KABEL — TIDAK PERNAH AKTIF SAAT FIST)
        // ---------------------------------------------------------------------
        if (!isPinchActive)
        {
            pinchExitHoldTimer = 0f;

            bool prePinchingNow = isPrePinchGuardActive && !isFistActive;
            if (prePinchingNow)
            {
                if (!isPrePinchClosing)
                {
                    isPrePinchClosing = true;
                    latchedPinchLocalXY = PinchPointLocalXY;
                    hasLatchedPinch = true;
                    prePinchLatchTimer = PrePinchLatchMaxSeconds;
                }
                else
                {
                    prePinchLatchTimer -= dt;
                    if (prePinchLatchTimer <= 0f)
                    {
                        hasLatchedPinch = false;
                    }
                }
            }
            else
            {
                isPrePinchClosing = false;
                hasLatchedPinch = false;
                prePinchLatchTimer = 0f;
            }

            bool allowPinchEnter = rawPinchEnterCondition && !isFistActive && !isFistStickyHoldActive;
            if (allowPinchEnter)
            {
                pinchEnterHoldTimer += dt;
                if (pinchEnterHoldTimer >= PinchEnterDebounceSeconds)
                {
                    isPinchActive = true;
                    pinchJustStarted = true;
                    pinchEnterHoldTimer = 0f;
                    isPrePinchClosing = false;
                }
            }
            else
            {
                pinchEnterHoldTimer = Mathf.Max(0f, pinchEnterHoldTimer - (dt * 2f));
            }
        }
        else
        {
            hasLatchedPinch = false;
            isPrePinchClosing = false;
            pinchEnterHoldTimer = 0f;

            if (rawPinchExitCondition)
            {
                float speedT = Mathf.Clamp01((PinchSpeedUnitsPerSec - SlowReleaseSpeedThreshold) / (FastReleaseSpeedThreshold - SlowReleaseSpeedThreshold));
                float requiredExit = isPinchStickyHoldActive
                    ? Mathf.Lerp(PinchExitSlowSeconds, PinchExitFastSeconds, speedT)
                    : PinchExitSlowSeconds;
                pinchExitHoldTimer += dt;
                if (pinchExitHoldTimer >= requiredExit)
                {
                    isPinchActive = false;
                    pinchJustEnded = true;
                    pinchExitHoldTimer = 0f;
                }
            }
            else
            {
                pinchExitHoldTimer = Mathf.Max(0f, pinchExitHoldTimer - (dt * 2f));
            }
        }

        // ---------------------------------------------------------------------
        // KANAL 2: FIST / KEPAL (KHUSUS OBJEK — PARAMETER STICKY FASE 3 UTUH)
        // ---------------------------------------------------------------------
        if (!isFistActive)
        {
            exitHoldTimer = 0f;

            bool curlingNow = meanCurlRatio < PreFistLatchMeanRatio && !isPinchActive && !isPrePinchGuardActive;
            if (curlingNow)
            {
                if (!isPreFistCurling)
                {
                    isPreFistCurling = true;
                    latchedCursorLocalXY = PalmCursorLocalXY;
                    hasLatchedCursor = true;
                    preFistLatchTimer = PreFistLatchMaxSeconds;
                }
                else
                {
                    preFistLatchTimer -= dt;
                    if (preFistLatchTimer <= 0f)
                    {
                        hasLatchedCursor = false;
                    }
                }
            }
            else
            {
                isPreFistCurling = false;
                hasLatchedCursor = false;
                preFistLatchTimer = 0f;
            }

            // Guard Anti-Fist: tolak masuk Fist saat sedang Pinch/pre-pinch, 1 jari telunjuk (#8) teracung,
            // atau pose summon sah aktif. Kepalan penuh (4 jari tertekuk) tetap masuk Fist dalam 45 ms.
            bool isSummonGatePose = currentSummonPose == SummonHandPose.OneFingerAnd
                                    || currentSummonPose == SummonHandPose.TwoFingersOr
                                    || currentSummonPose == SummonHandPose.ThreeFingersXor
                                    || currentSummonPose == SummonHandPose.FourFingersNand
                                    || currentSummonPose == SummonHandPose.ThumbOnlyNot;
            bool blockedByOtherChannel = isPinchActive || isPinchStickyHoldActive || isPrePinchGuardActive;
            bool allowFistEnter = rawEnterCondition
                                  && !blockedByOtherChannel
                                  && (isFistStickyHoldActive || (!mainFingerExtended[0] && !isSummonGatePose));
            if (allowFistEnter)
            {
                enterHoldTimer += dt;
                if (enterHoldTimer >= DebounceEnterSeconds)
                {
                    isFistActive = true;
                    fistJustStarted = true;
                    enterHoldTimer = 0f;
                    isPreFistCurling = false;
                }
            }
            else
            {
                enterHoldTimer = Mathf.Max(0f, enterHoldTimer - (dt * 2f));
            }
        }
        else
        {
            hasLatchedCursor = false;
            isPreFistCurling = false;
            enterHoldTimer = 0f;

            // Fase 3 Sticky-Drag (TERKUNCI): rasio > 1.15f & foldedFingerCount <= 1, adaptif 55-115 ms
            bool exitMet = isFistStickyHoldActive
                ? (meanCurlRatio > FistStickyExitMeanRatio && foldedFingerCount <= 1)
                : rawExitCondition;

            if (exitMet)
            {
                float speedT = Mathf.Clamp01((CursorSpeedUnitsPerSec - SlowReleaseSpeedThreshold) / (FastReleaseSpeedThreshold - SlowReleaseSpeedThreshold));
                float requiredExit = Mathf.Lerp(DebounceExitSlowSeconds, DebounceExitFastSeconds, speedT);
                exitHoldTimer += dt;
                if (exitHoldTimer >= requiredExit)
                {
                    isFistActive = false;
                    fistJustEnded = true;
                    exitHoldTimer = 0f;
                }
            }
            else
            {
                exitHoldTimer = Mathf.Max(0f, exitHoldTimer - (dt * 2f));
            }
        }
    }

    private static float Dist2D(NormalizedLandmark a, NormalizedLandmark b)
    {
        float dx = a.x - b.x;
        float dy = a.y - b.y;
        return Mathf.Sqrt((dx * dx) + (dy * dy));
    }
}
