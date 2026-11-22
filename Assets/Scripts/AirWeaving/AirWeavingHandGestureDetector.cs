using System.Collections.Generic;
using UnityEngine;
using Landmark = Mediapipe.Tasks.Components.Containers.Landmark;
using NormalizedLandmark = Mediapipe.Tasks.Components.Containers.NormalizedLandmark;

/// <summary>
/// Detektor gestur dual-kanal 3D Metrik untuk GateWeaver AR v8.0 (Tahap 5.5 — Overhaul Presisi Tracking):
/// - Klasifikasi utama berbasis <c>handWorldLandmarks</c> (3D meter sejati, kebal perspektif &amp; rotasi telapak) dengan
///   fallback 2D aspect-compensated otomatis bila <c>handWorldLandmarks</c> kosong.
/// - Tekukan tiap jari dihitung melalui sudut sendi 3D (dot product vektor <c>Wrist-&gt;MCP</c>, <c>MCP-&gt;PIP</c>,
///   <c>PIP-&gt;DIP</c>, <c>DIP-&gt;TIP</c>), sedangkan Pinch memakai metrik hibrida anti Z-occlusion: nilai minimum antara
///   rasio 3D (<c>#4-#8</c> &amp; <c>#4-#7*1.15 / palmSpan3D</c>) dan rasio 2D aspect-compensated (<c>#4-#8</c> &amp; <c>#4-#7*1.15 / palmSpan2D</c>).
/// - Quality Gate: deadzone tepi frame <c>[0.04, 0.96]</c>, validasi <c>palmScale</c> minimum sebelum clamp,
///   hand-entry warm-up <c>80 ms</c>, filter EMA ringan Zero-GC pada metrik skalar, dan enter debounce ganda.
/// - Pemisahan anatomi natural antar-gestur: Pinch santai tanpa paksaan pose "OK" (jari tengah/manis/kelingking bebas menekuk
///   rileks, dibedakan dari Fist lewat <c>!isFistActive</c> &amp; tidak semua 4 jari tergenggam penuh ke telapak), Fist (jari
///   utama menekuk tegas berdasarkan sudut sendi 3D), Input Switch (hanya telunjuk teracung tegas), dan Circle Summon
///   berbasis identitas jari spesifik (AND/OR/XOR/NAND/NOT + Open-5 Bloom).
/// - Seluruh koordinat interaksi <c>*LocalXY</c> dan kecepatan <c>*SpeedUnitsPerSec</c> disatukan pada sinar kamera
///   bidang referensi konsisten (<c>referenceDepth</c> = <c>WorkbenchDepth</c>). 100% Zero-GC.
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

/// <summary>
/// Filter One-Euro 1D murni Zero-GC (Casiez et al.) mendukung mode single-call <c>Filter()</c>
/// maupun mode render-rate kontinu <c>PushSample()</c> + <c>Tick(dt)</c> untuk kehalusan 60-120 Hz.
/// </summary>
public struct OneEuroFilter1D
{
    private const float TwoPi = 6.283185307f;
    private const float MaxActiveCutoffHz = 14.0f;

    private bool initialized;
    private float xPrev;
    private float dxPrev;
    private float targetSample;
    private float prevRawSample;
    private float activeCutoffHz;

    public bool IsInitialized => initialized;
    public float CurrentValue => xPrev;
    public float TargetSample => targetSample;

    public void Reset()
    {
        initialized = false;
        xPrev = 0f;
        dxPrev = 0f;
        targetSample = 0f;
        prevRawSample = 0f;
        activeCutoffHz = 2.0f;
    }

    public float PushSample(
        float rawValue,
        float sampleDt,
        float minCutoff,
        float beta,
        float dCutoff,
        float velocityScale = 1f)
    {
        if (!initialized)
        {
            initialized = true;
            xPrev = rawValue;
            targetSample = rawValue;
            prevRawSample = rawValue;
            dxPrev = 0f;
            activeCutoffHz = Mathf.Min(MaxActiveCutoffHz, minCutoff);
            return xPrev;
        }

        float safeDt = Mathf.Clamp(sampleDt, 0.004f, 0.200f);
        float rawDx = ((rawValue - prevRawSample) * velocityScale) / safeDt;
        prevRawSample = rawValue;
        targetSample = rawValue;

        float alphaD = ComputeAlpha(safeDt, dCutoff);
        dxPrev += alphaD * (rawDx - dxPrev);
        activeCutoffHz = Mathf.Min(MaxActiveCutoffHz, minCutoff + (beta * Mathf.Abs(dxPrev)));
        return xPrev;
    }

    public float Tick(float dt)
    {
        if (!initialized)
        {
            return xPrev;
        }

        float safeDt = Mathf.Clamp(dt, 0.0005f, 0.050f);
        float alphaX = ComputeAlpha(safeDt, activeCutoffHz);
        xPrev += alphaX * (targetSample - xPrev);
        return xPrev;
    }

    public float Filter(
        float rawValue,
        float dt,
        float minCutoff,
        float beta,
        float dCutoff,
        float velocityScale = 1f)
    {
        if (!initialized)
        {
            initialized = true;
            xPrev = rawValue;
            targetSample = rawValue;
            prevRawSample = rawValue;
            dxPrev = 0f;
            activeCutoffHz = Mathf.Min(MaxActiveCutoffHz, minCutoff);
            return xPrev;
        }

        float safeDt = Mathf.Clamp(dt, 0.004f, 0.200f);
        float rawDx = ((rawValue - xPrev) * velocityScale) / safeDt;
        prevRawSample = rawValue;
        targetSample = rawValue;

        float alphaD = ComputeAlpha(safeDt, dCutoff);
        dxPrev += alphaD * (rawDx - dxPrev);
        activeCutoffHz = Mathf.Min(MaxActiveCutoffHz, minCutoff + (beta * Mathf.Abs(dxPrev)));

        float alphaX = ComputeAlpha(safeDt, activeCutoffHz);
        xPrev += alphaX * (rawValue - xPrev);
        return xPrev;
    }

    public static float ComputeAlpha(float dt, float cutoffHz)
    {
        float omegaDt = TwoPi * Mathf.Max(0.01f, cutoffHz) * dt;
        return omegaDt / (1f + omegaDt);
    }
}

/// <summary>
/// Filter One-Euro 3D isotropik murni Zero-GC (kecepatan 3D menyatu antar-sumbu agar lintasan diagonal/melingkar
/// tidak terdistorsi) dengan dukungan <c>PushSample()</c> pada laju kamera + <c>Tick(dt)</c> pada laju render.
/// </summary>
public struct OneEuroFilter3D
{
    private const float MaxActiveCutoffHz = 14.0f;

    private bool initialized;
    private Vector3 posPrev;
    private Vector3 velPrev;
    private Vector3 targetSample;
    private Vector3 prevRawSample;
    private float activeCutoffHz;

    public bool IsInitialized => initialized;
    public Vector3 CurrentValue => posPrev;
    public Vector3 TargetSample => targetSample;

    public void Reset()
    {
        initialized = false;
        posPrev = Vector3.zero;
        velPrev = Vector3.zero;
        targetSample = Vector3.zero;
        prevRawSample = Vector3.zero;
        activeCutoffHz = 2.0f;
    }

    public Vector3 PushSample(
        Vector3 rawValue,
        float sampleDt,
        float minCutoff,
        float beta,
        float dCutoff,
        float velocityScale = 1f)
    {
        if (!initialized)
        {
            initialized = true;
            posPrev = rawValue;
            targetSample = rawValue;
            prevRawSample = rawValue;
            velPrev = Vector3.zero;
            activeCutoffHz = Mathf.Min(MaxActiveCutoffHz, minCutoff);
            return posPrev;
        }

        float safeDt = Mathf.Clamp(sampleDt, 0.004f, 0.200f);
        Vector3 rawVel = (rawValue - prevRawSample) / safeDt;
        prevRawSample = rawValue;
        targetSample = rawValue;

        float alphaD = OneEuroFilter1D.ComputeAlpha(safeDt, dCutoff);
        velPrev += alphaD * (rawVel - velPrev);

        float scaledSpeed = velPrev.magnitude * velocityScale;
        activeCutoffHz = Mathf.Min(MaxActiveCutoffHz, minCutoff + (beta * scaledSpeed));
        return posPrev;
    }

    public Vector3 Tick(float dt)
    {
        if (!initialized)
        {
            return posPrev;
        }

        float safeDt = Mathf.Clamp(dt, 0.0005f, 0.050f);
        float alphaX = OneEuroFilter1D.ComputeAlpha(safeDt, activeCutoffHz);
        posPrev += alphaX * (targetSample - posPrev);
        return posPrev;
    }

    public Vector3 Filter(
        Vector3 rawValue,
        float dt,
        float minCutoff,
        float beta,
        float dCutoff,
        float velocityScale = 1f)
    {
        if (!initialized)
        {
            initialized = true;
            posPrev = rawValue;
            targetSample = rawValue;
            prevRawSample = rawValue;
            velPrev = Vector3.zero;
            activeCutoffHz = Mathf.Min(MaxActiveCutoffHz, minCutoff);
            return posPrev;
        }

        float safeDt = Mathf.Clamp(dt, 0.004f, 0.200f);
        Vector3 rawVel = (rawValue - posPrev) / safeDt;
        prevRawSample = rawValue;
        targetSample = rawValue;

        float alphaD = OneEuroFilter1D.ComputeAlpha(safeDt, dCutoff);
        velPrev += alphaD * (rawVel - velPrev);

        float scaledSpeed = velPrev.magnitude * velocityScale;
        activeCutoffHz = Mathf.Min(MaxActiveCutoffHz, minCutoff + (beta * scaledSpeed));

        float alphaX = OneEuroFilter1D.ComputeAlpha(safeDt, activeCutoffHz);
        posPrev += alphaX * (rawValue - posPrev);
        return posPrev;
    }
}

public sealed class AirWeavingHandGestureDetector
{
    // Quality Gate: edge deadzone longgar [0.015, 0.985] & skala telapak jarak jauh (0.032) agar tangan jauh tetap lolos
    private const float EdgeDeadzoneMin = 0.015f;
    private const float EdgeDeadzoneMax = 0.985f;
    private const float MinValidRawPalmScale2D = 0.032f;
    private const float MinValidPalmSpan3DMeters = 0.035f;
    private const float MinPalmScaleClamp = 0.045f;
    private const float HandEntryWarmupSeconds = 0.080f;
    private const float MetricEmaTauSeconds = 0.020f;
    private const float DefaultReferenceDepth = 3.82f;

    // Parameter One-Euro 3D kursor interaksi Tahap 8 (responsif gerak cepat bebas karet, stabil anti-jitter di webcam 30 Hz)
    private const float CursorOneEuroPalmMinCutoff = 1.70f;
    private const float CursorOneEuroPalmBeta = 0.12f;
    private const float CursorOneEuroPinchMinCutoff = 1.65f;
    private const float CursorOneEuroPinchBeta = 0.11f;
    private const float CursorOneEuroIndexMinCutoff = 1.75f;
    private const float CursorOneEuroIndexBeta = 0.13f;
    private const float CursorOneEuroDCutoff = 1.75f;
    private const float CursorOneEuroWorldSpeedScale = 22.0f;

    // Parameter True 3D Hand Depth Tracking (Monocular Z-Depth Logaritmik + Baseline Adaptif — Tahap 8 v8.0)
    public const float DefaultComfortPalmSpan = 0.172f;
    private const float MinRigidPalmSpanClamp = 0.030f;
    private const float MaxRigidPalmSpanClamp = 0.620f;
    private const float PalmSpanOneEuroMinCutoff = 1.25f;
    private const float PalmSpanOneEuroBeta = 0.10f;
    private const float PalmSpanOneEuroDCutoff = 1.50f;
    private const float PalmSpanMicroDeadzoneRatio = 0.020f;
    private const float MinAdaptiveBaselinePalmSpan = 0.065f;
    private const float MaxAdaptiveBaselinePalmSpan = 0.360f;
    private const float BaselineEntryFastLockSeconds = 0.160f;
    private const float BaselineEntryFastTauSeconds = 0.18f;
    private const float BaselineIdleSlowTauSeconds = 8.00f;
    private const float BaselineCalmLogVelMax = 0.32f;
    private const float BaselineCalmDelaySeconds = 0.42f;
    private const float DepthLogMicroDeadzone = 0.024f;
    private const float IndexPokeEmaTauSeconds = 0.030f;

    // Ambang sudut sendi 3D per jari (derajat) — kebal jarak kamera & rotasi miring
    private const float FingerExtendMaxPipDipDeg = 48.0f;
    private const float FingerExtendMaxWeightedDeg = 68.0f;
    private const float FingerExtendMinPalmRatio = 1.04f;
    private const float FingerUnextendMinPipDipDeg = 66.0f;
    private const float FingerUnextendMinWeightedDeg = 90.0f;
    private const float FingerUnextendMaxPalmRatio = 0.90f;

    private const float FingerFirmFoldPipDipDeg = 88.0f;
    private const float FingerFirmFoldWeightedDeg = 116.0f;
    private const float FingerFirmFoldDeepWeightedDeg = 132.0f;
    private const float FingerFirmFoldDeepPalmRatio = 1.02f;
    private const float FingerFirmFoldTuckPipDipDeg = 74.0f;
    private const float FingerFirmFoldTuckPalmRatio = 0.88f;

    // Ambang Kanal Objek (Fist / Kepal 3D): keempat jari utama menekuk tegas + sticky-drag hysteresis lebar
    private const float FistEnterMeanWeightedBendDeg = 112.0f;
    private const float FistEnterMaxMeanCurlRatio = 1.00f;
    private const float FistExitMeanWeightedBendDeg = 86.0f;
    private const float FistStickyExitMeanWeightedBendDeg = 80.0f;
    private const float PreFistLatchMinWeightedBendDeg = 104.0f;

    private const int FistEnterMinConsecutiveFrames = 2;
    private const float FistEnterDebounceSeconds = 0.055f;
    private const float FistExitSlowSeconds = 0.055f;
    private const float FistExitFastSeconds = 0.115f;
    private const float SlowReleaseSpeedThreshold = 1.50f;
    private const float FastReleaseSpeedThreshold = 2.50f;
    private const float OcclusionGraceSeconds = 0.080f;
    private const float StickyOcclusionGraceSeconds = 0.200f;
    private const float PreFistLatchMaxSeconds = 0.060f;

    // Ambang Kanal Kabel & Free-View (Pinch Hibrida 3D + 2D Aspect-Compensated, anti Z-occlusion, santai tanpa pose "OK")
    private const float PinchEnterRatio = 0.48f;
    private const float PinchExitRatio = 0.62f;
    private const float PinchCableStickyExitRatio = 0.75f;
    public const float PinchReleaseIntentRatio = 0.56f;
    private const float PrePinchSuppressSummonRatio = 0.54f;
    private const float PinchDipProximityWeight = 1.15f;
    private const float IndexTuckedForFullFistMaxPalmRatio = 0.96f;

    private const int PinchEnterMinConsecutiveFrames = 2;
    private const float PinchEnterDebounceSeconds = 0.035f;
    private const float PinchExitSlowSeconds = 0.045f;
    private const float PinchExitFastSeconds = 0.095f;
    private const float PinchCableStickyExitDebounceSeconds = 0.095f;
    private const float PinchOcclusionGraceSeconds = 0.080f;
    private const float PinchStickyOcclusionGraceSeconds = 0.200f;
    private const float PrePinchLatchMaxSeconds = 0.055f;

    // Ambang Ibu Jari 3D (Open-5 Bloom, NAND jempol lipat, NOT jempol kuat)
    private const float ThumbExtendEnterIndexRatio = 0.50f;
    private const float ThumbExtendEnterPinkyRatio = 1.00f;
    private const float ThumbExtendEnterMaxBendDeg = 60.0f;
    private const float ThumbExtendExitIndexRatio = 0.44f;
    private const float ThumbExtendExitPinkyRatio = 0.92f;
    private const float ThumbExtendExitMinBendDeg = 72.0f;

    private const float ThumbStrongNotIndexRatio = 0.60f;
    private const float ThumbStrongNotPinkyRatio = 1.12f;
    private const float ThumbStrongNotMaxBendDeg = 48.0f;

    private const float OpenFiveBloomMaxMeanBendDeg = 65.0f;
    private const float RelaxedOpenFiveMaxMeanBendDeg = 74.0f;

    private static readonly int[] FingerMcpIndices = { 5, 9, 13, 17 };
    private static readonly int[] FingerPipIndices = { 6, 10, 14, 18 };
    private static readonly int[] FingerDipIndices = { 7, 11, 15, 19 };
    private static readonly int[] FingerTipIndices = { 8, 12, 16, 20 };

    private struct ScalarEmaFilter
    {
        private bool initialized;
        private float value;

        public void Reset(float initialValue)
        {
            initialized = false;
            value = initialValue;
        }

        public float Update(float sample, float dt, float tauSeconds)
        {
            if (!initialized)
            {
                initialized = true;
                value = sample;
                return value;
            }

            float alpha = 1f - Mathf.Exp(-Mathf.Max(0.001f, dt) / Mathf.Max(0.001f, tauSeconds));
            value += alpha * (sample - value);
            return value;
        }
    }

    // Filter EMA ringan Zero-GC untuk metrik skalar 3D
    private ScalarEmaFilter pinchRatioEma;
    private ScalarEmaFilter thumbBendEma;
    private ScalarEmaFilter thumbIndexRatioEma;
    private ScalarEmaFilter thumbPinkyRatioEma;
    private readonly ScalarEmaFilter[] fingerPipDipBendEma = new ScalarEmaFilter[4];
    private readonly ScalarEmaFilter[] fingerWeightedBendEma = new ScalarEmaFilter[4];
    private readonly ScalarEmaFilter[] fingerPalmRatioEma = new ScalarEmaFilter[4];

    // State Quality Gate
    private bool isUsingWorld3DMetrics = false;
    private bool isInEdgeDeadzone = false;
    private bool isPalmCenterInSafeFrame = false;
    private bool isPalmScaleValid = false;
    private float entryWarmupTimer = HandEntryWarmupSeconds;
    private bool wasDetectedInTick = false;

    // State Fist / Open (Objek)
    private float meanCurlRatio = 1.50f;
    private float meanWeightedBendDeg = 18.0f;
    private int foldedFingerCount = 0;
    private bool rawEnterCondition = false;
    private bool rawExitCondition = true;
    private int fistEnterConsecFrames = 0;

    private bool isFistActive = false;
    private bool fistJustStarted = false;
    private bool fistJustEnded = false;
    private bool isPreFistCurling = false;

    private float enterHoldTimer = 0f;
    private float exitHoldTimer = 0f;
    private float occlusionGraceTimer = 0f;
    private float preFistLatchTimer = 0f;
    private Vector2 latchedCursorLocalXY = Vector2.zero;
    private bool hasLatchedCursor = false;

    // State Pinch / Cubit (Kabel)
    private float currentPinchRatio = 1.00f;
    private bool rawPinchEnterCondition = false;
    private bool rawPinchExitCondition = true;
    private bool isPrePinchGuardActive = false;
    private int pinchEnterConsecFrames = 0;
    private bool isPinchActive = false;
    private bool pinchJustStarted = false;
    private bool pinchJustEnded = false;
    private bool pinchEndedByTrackingLoss = false;
    private float pinchEnterHoldTimer = 0f;
    private float pinchExitHoldTimer = 0f;
    private float pinchOcclusionGraceTimer = 0f;
    private float prePinchLatchTimer = 0f;
    private bool isPrePinchClosing = false;
    private Vector2 latchedPinchLocalXY = Vector2.zero;
    private bool hasLatchedPinch = false;

    // State sampel inferensi kursor pada bidang sinar konsisten (WorkbenchDepth)
    private bool hasPrevSampleCursors = false;
    private Vector2 prevRawPalmLocalXY = Vector2.zero;
    private Vector2 prevRawPinchLocalXY = Vector2.zero;
    private Vector2 prevRawIndexTipLocalXY = Vector2.zero;
    private float lastLandmarkUpdateUnscaledTime = -1f;

    // State 1D One-Euro Filter + Baseline Adaptif Monocular Z-Depth (Tahap 8 v8.0, Zero-GC)
    private OneEuroFilter1D palmSpanOneEuroFilter;
    private float rawRigidPalmSpan = DefaultComfortPalmSpan;
    private float filteredPalmSpan = DefaultComfortPalmSpan;
    private bool hasBaselinePalmSpan = false;
    private float baselinePalmSpan = DefaultComfortPalmSpan;
    private float baselineEntryElapsed = 0f;
    private float baselineCalmTimer = 0f;
    private float prevLogPalmSpan = 0f;
    private bool hasPrevLogPalmSpan = false;
    private float depthLogDeltaRaw = 0f;
    private float depthLogDeltaEffective = 0f;
    private ScalarEmaFilter indexPokeZEma;
    private float indexFingerPokeZ = 0f;

    // One-Euro Filter 3D Zero-GC untuk posisi kursor telapak, titik bidik Pinch (#4-#8), ujung telunjuk (#8), jempol (#4), & summon tip
    private OneEuroFilter3D palmCursorFilter;
    private OneEuroFilter3D pinchPointFilter;
    private OneEuroFilter3D indexTipFilter;
    private OneEuroFilter3D thumbTipFilter;
    private OneEuroFilter3D activeSummonTipFilter;
    private Transform cachedProjectionCamTransform;
    private float cachedProjectionRefDepth = DefaultReferenceDepth;

    // State Summon 5 Gerbang & Identitas Jari Spesifik
    private readonly bool[] mainFingerExtended = new bool[4];
    private bool isThumbExtended = false;
    private bool isThumbStrongOut = false;
    private bool isRelaxedOpenFiveBloom = false;
    private SummonHandPose currentSummonPose = SummonHandPose.None;

    public bool IsUsingWorld3DMetrics => isUsingWorld3DMetrics;
    public bool IsInEdgeDeadzone => isInEdgeDeadzone;
    public bool IsPalmScaleValid => isPalmScaleValid;
    public bool IsEntryWarmupComplete => entryWarmupTimer <= 0f;
    public bool IsQualityGatePassing => !isInEdgeDeadzone && isPalmScaleValid && entryWarmupTimer <= 0f;
    public bool IsTwoHandQualityGatePassing => isPalmCenterInSafeFrame && isPalmScaleValid && entryWarmupTimer <= 0f;

    public bool IsFistActive => isFistActive;
    public bool FistJustStarted => fistJustStarted;
    public bool FistJustEnded => fistJustEnded;
    public bool IsPreFistCurling => isPreFistCurling;
    public bool IsFormingFistIntent => isFistActive || isPreFistCurling;
    public float MeanCurlRatio => meanCurlRatio;
    public float MeanWeightedBendDeg => meanWeightedBendDeg;
    public int FoldedFingerCount => foldedFingerCount;
    public float RawRigidPalmSpan => rawRigidPalmSpan;
    public float FilteredPalmSpan => filteredPalmSpan;
    public float BaselinePalmSpan => baselinePalmSpan;
    public float DepthLogDelta => depthLogDeltaEffective;
    public float DepthLogDeltaRaw => depthLogDeltaRaw;
    public float IndexFingerPokeZ => indexFingerPokeZ;

    public bool IsPinchActive => isPinchActive;
    public bool PinchJustStarted => pinchJustStarted;
    public bool PinchJustEnded => pinchJustEnded;
    public bool PinchEndedByTrackingLoss => pinchEndedByTrackingLoss;
    public bool IsPrePinchClosing => isPrePinchClosing;
    public bool IsFormingPinchIntent => isPinchActive || isPrePinchGuardActive || isPrePinchClosing;
    public bool IsPinchReleaseIntent => currentPinchRatio > PinchReleaseIntentRatio;
    public bool IsPrePinchSuppressingSummon => isPinchActive || isPrePinchGuardActive;
    public float PinchRatio => currentPinchRatio;
    public float PinchSpeedUnitsPerSec { get; private set; }
    public float IndexTipSpeedUnitsPerSec { get; private set; }
    public Vector3 PinchPointWorldPosition { get; private set; }
    public Vector2 PinchPointLocalXY { get; private set; }
    public Vector2 StabilizedPinchLocalXY => hasLatchedPinch ? latchedPinchLocalXY : PinchPointLocalXY;

    public SummonHandPose CurrentSummonPose => currentSummonPose;
    public bool IsOpenFiveBloom => currentSummonPose == SummonHandPose.OpenFiveBloom;
    public bool IsRelaxedOpenFiveBloom => isRelaxedOpenFiveBloom;
    public bool IsIndexFingerExtended => mainFingerExtended[0];

    /// <summary>
    /// Pose telunjuk teracung tegas untuk sakelar Input A/B: hanya telunjuk lurus, tiga jari lain tidak terbuka lebar
    /// menutupi sakelar, serta bukan kepalan, cubitan, atau pra-cubitan.
    /// </summary>
    public bool IsIndexSwitchPoseValid => mainFingerExtended[0]
                                          && !mainFingerExtended[1]
                                          && !mainFingerExtended[2]
                                          && !mainFingerExtended[3]
                                          && !isFistActive
                                          && !isPinchActive
                                          && !isPrePinchGuardActive
                                          && !isRelaxedOpenFiveBloom;

    public float CursorSpeedUnitsPerSec { get; private set; }
    public Vector3 PalmCursorWorldPosition { get; private set; }
    public Vector2 PalmCursorLocalXY { get; private set; }
    public Vector3 IndexTipWorldPosition { get; private set; }
    public Vector2 IndexTipLocalXY { get; private set; }
    public Vector2 ThumbTipLocalXY { get; private set; }
    public Vector2 ActiveSummonTipLocalXY { get; private set; }
    public Vector2 StabilizedCursorLocalXY => hasLatchedCursor ? latchedCursorLocalXY : PalmCursorLocalXY;

    public void Reset()
    {
        ResetScalarEmaFilters();
        ResetCursorFilters();

        isUsingWorld3DMetrics = false;
        isInEdgeDeadzone = false;
        isPalmCenterInSafeFrame = false;
        isPalmScaleValid = false;
        entryWarmupTimer = HandEntryWarmupSeconds;
        wasDetectedInTick = false;

        meanCurlRatio = 1.50f;
        meanWeightedBendDeg = 18.0f;
        foldedFingerCount = 0;
        rawEnterCondition = false;
        rawExitCondition = true;
        fistEnterConsecFrames = 0;
        isFistActive = false;
        fistJustStarted = false;
        fistJustEnded = false;
        isPreFistCurling = false;
        enterHoldTimer = 0f;
        exitHoldTimer = 0f;
        occlusionGraceTimer = 0f;
        preFistLatchTimer = 0f;
        latchedCursorLocalXY = Vector2.zero;
        hasLatchedCursor = false;

        currentPinchRatio = 1.00f;
        rawPinchEnterCondition = false;
        rawPinchExitCondition = true;
        isPrePinchGuardActive = false;
        pinchEnterConsecFrames = 0;
        isPinchActive = false;
        pinchJustStarted = false;
        pinchJustEnded = false;
        pinchEndedByTrackingLoss = false;
        pinchEnterHoldTimer = 0f;
        pinchExitHoldTimer = 0f;
        pinchOcclusionGraceTimer = 0f;
        prePinchLatchTimer = 0f;
        isPrePinchClosing = false;
        latchedPinchLocalXY = Vector2.zero;
        hasLatchedPinch = false;

        hasPrevSampleCursors = false;
        prevRawPalmLocalXY = Vector2.zero;
        prevRawPinchLocalXY = Vector2.zero;
        prevRawIndexTipLocalXY = Vector2.zero;
        lastLandmarkUpdateUnscaledTime = -1f;
        palmSpanOneEuroFilter.Reset();
        rawRigidPalmSpan = DefaultComfortPalmSpan;
        filteredPalmSpan = DefaultComfortPalmSpan;
        hasBaselinePalmSpan = false;
        baselinePalmSpan = DefaultComfortPalmSpan;
        baselineEntryElapsed = 0f;
        baselineCalmTimer = 0f;
        prevLogPalmSpan = 0f;
        hasPrevLogPalmSpan = false;
        depthLogDeltaRaw = 0f;
        depthLogDeltaEffective = 0f;
        indexPokeZEma.Reset(0f);
        indexFingerPokeZ = 0f;
        cachedProjectionCamTransform = null;
        cachedProjectionRefDepth = DefaultReferenceDepth;

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
        IndexTipSpeedUnitsPerSec = 0f;
        PalmCursorWorldPosition = Vector3.zero;
        PalmCursorLocalXY = Vector2.zero;
        PinchPointWorldPosition = Vector3.zero;
        PinchPointLocalXY = Vector2.zero;
        IndexTipWorldPosition = Vector3.zero;
        IndexTipLocalXY = Vector2.zero;
        ThumbTipLocalXY = Vector2.zero;
        ActiveSummonTipLocalXY = Vector2.zero;
    }

    /// <summary>
    /// Mereset seluruh filter One-Euro 3D kursor (telapak, Pinch #4-#8, telunjuk #8, jempol #4, &amp; summon tip)
    /// saat fresh entry agar tidak pernah ada tarikan dari posisi lama. Zero-GC.
    /// </summary>
    public void ResetCursorFilters()
    {
        palmCursorFilter.Reset();
        pinchPointFilter.Reset();
        indexTipFilter.Reset();
        thumbTipFilter.Reset();
        activeSummonTipFilter.Reset();
    }

    /// <summary>
    /// Memicu ulang warm-up masuk tangan 80 ms &amp; reset filter EMA skalar + One-Euro 3D kursor saat tangan baru muncul
    /// kembali atau melompat di luar radius asosiasi, tanpa memutus paksa sticky hold occlusion grace. Zero-GC.
    /// </summary>
    public void RestartEntryWarmup()
    {
        entryWarmupTimer = HandEntryWarmupSeconds;
        fistEnterConsecFrames = 0;
        pinchEnterConsecFrames = 0;
        enterHoldTimer = 0f;
        pinchEnterHoldTimer = 0f;
        hasPrevSampleCursors = false;
        lastLandmarkUpdateUnscaledTime = -1f;
        palmSpanOneEuroFilter.Reset();
        hasBaselinePalmSpan = false;
        baselineEntryElapsed = 0f;
        baselineCalmTimer = 0f;
        hasPrevLogPalmSpan = false;
        depthLogDeltaRaw = 0f;
        depthLogDeltaEffective = 0f;
        indexPokeZEma.Reset(0f);
        indexFingerPokeZ = 0f;
        ResetScalarEmaFilters();
        ResetCursorFilters();
    }

    /// <summary>
    /// Mengurangi timer occlusion grace internal sesuai durasi grace yang sudah dihabiskan di level slot tangan
    /// (Stage 1) agar total occlusion grace tetap tepat (~200 ms saat membawa objek) dan tidak bertumpuk ganda. Zero-GC.
    /// </summary>
    public void ConsumeExternalGraceSeconds(float waitedSeconds)
    {
        if (waitedSeconds <= 0f)
        {
            return;
        }

        occlusionGraceTimer = Mathf.Max(0f, occlusionGraceTimer - waitedSeconds);
        pinchOcclusionGraceTimer = Mathf.Max(0f, pinchOcclusionGraceTimer - waitedSeconds);
    }

    private void ResetScalarEmaFilters()
    {
        pinchRatioEma.Reset(1.00f);
        thumbBendEma.Reset(30.0f);
        thumbIndexRatioEma.Reset(0.55f);
        thumbPinkyRatioEma.Reset(1.05f);
        for (int i = 0; i < 4; i++)
        {
            fingerPipDipBendEma[i].Reset(20.0f);
            fingerWeightedBendEma[i].Reset(25.0f);
            fingerPalmRatioEma[i].Reset(1.40f);
        }
    }

    /// <summary>
    /// Menghitung metrik ukuran telapak kaku (Rigid Metacarpal Span) yang tidak berubah bentuk saat tangan mengepal/mencubit
    /// dari kombinasi 3D-compensated #0-&gt;#9, #0-&gt;#5, #0-&gt;#17, dan #5-&gt;#17, menyaringnya dengan 1D One-Euro Filter,
    /// memperbarui baseline adaptif jarak duduk (<c>baselinePalmSpan</c>), delta logaritmik (<c>DepthLogDelta</c>),
    /// serta juluran telunjuk relatif (<c>IndexFingerPokeZ</c>). Dipanggil tepat 1x per frame inferensi per tangan. Zero-GC.
    /// </summary>
    public float UpdateRigidPalmSpanOnly(
        List<NormalizedLandmark> landmarks,
        float sampleDt,
        float cameraAspect = 1.3333f,
        bool freezeBaselineAdaptation = false)
    {
        if (landmarks == null || landmarks.Count < 21)
        {
            return filteredPalmSpan;
        }

        NormalizedLandmark l0 = landmarks[0];
        NormalizedLandmark l5 = landmarks[5];
        NormalizedLandmark l8 = landmarks[8];
        NormalizedLandmark l9 = landmarks[9];
        NormalizedLandmark l17 = landmarks[17];

        float safeAspect = Mathf.Clamp(cameraAspect, 0.65f, 2.40f);
        float d09 = Dist3DCompensated(l0, l9, safeAspect);
        float d05 = Dist3DCompensated(l0, l5, safeAspect);
        float d017 = Dist3DCompensated(l0, l17, safeAspect);
        float d517 = Dist3DCompensated(l5, l17, safeAspect);

        float computedSpan = (0.34f * d09) + (0.24f * d05) + (0.22f * d017) + (0.20f * d517);
        rawRigidPalmSpan = Mathf.Clamp(computedSpan, MinRigidPalmSpanClamp, MaxRigidPalmSpanClamp);

        float dt = Mathf.Clamp(sampleDt, 0.005f, 0.200f);
        bool wasInitialized = palmSpanOneEuroFilter.IsInitialized;
        float oneEuroSpan = palmSpanOneEuroFilter.Filter(
            rawRigidPalmSpan,
            dt,
            PalmSpanOneEuroMinCutoff,
            PalmSpanOneEuroBeta,
            PalmSpanOneEuroDCutoff,
            velocityScale: 100.0f);

        if (!wasInitialized)
        {
            filteredPalmSpan = Mathf.Clamp(oneEuroSpan, MinRigidPalmSpanClamp, MaxRigidPalmSpanClamp);
        }
        else
        {
            // Micro-deadzone halus (~1.5%) agar kedalaman 3D terkunci tenang saat tangan diam namun responsif saat maju/mundur
            float deadzoneRadius = Mathf.Max(0.0010f, filteredPalmSpan * PalmSpanMicroDeadzoneRatio);
            float deltaFromStable = oneEuroSpan - filteredPalmSpan;
            float absDelta = Mathf.Abs(deltaFromStable);
            if (absDelta > deadzoneRadius)
            {
                float excess = absDelta - deadzoneRadius;
                filteredPalmSpan += Mathf.Sign(deltaFromStable) * excess;
            }
            else
            {
                filteredPalmSpan = Mathf.Lerp(filteredPalmSpan, oneEuroSpan, dt * 1.40f);
            }
            filteredPalmSpan = Mathf.Clamp(filteredPalmSpan, MinRigidPalmSpanClamp, MaxRigidPalmSpanClamp);
        }

        // Kalibrasi baseline jarak duduk adaptif otomatis (cepat kunci saat fresh entry, drift amat lambat hanya saat Idle tenang,
        // dan DIBEKUKAN penuh saat gestur aktif / Push-to-Touch / Carry-Pan / mode 2 tangan)
        float clampedTargetBase = Mathf.Clamp(filteredPalmSpan, MinAdaptiveBaselinePalmSpan, MaxAdaptiveBaselinePalmSpan);
        float curLogSpan = Mathf.Log(Mathf.Max(MinRigidPalmSpanClamp, filteredPalmSpan));
        if (!hasBaselinePalmSpan)
        {
            baselinePalmSpan = clampedTargetBase;
            hasBaselinePalmSpan = true;
            baselineEntryElapsed = 0f;
            baselineCalmTimer = 0f;
            prevLogPalmSpan = curLogSpan;
            hasPrevLogPalmSpan = true;
        }
        else
        {
            float logVel = hasPrevLogPalmSpan ? (Mathf.Abs(curLogSpan - prevLogPalmSpan) / dt) : 0f;
            prevLogPalmSpan = curLogSpan;
            hasPrevLogPalmSpan = true;
            baselineEntryElapsed += dt;

            if (!freezeBaselineAdaptation)
            {
                if (baselineEntryElapsed < BaselineEntryFastLockSeconds)
                {
                    float entryAlpha = 1f - Mathf.Exp(-dt / BaselineEntryFastTauSeconds);
                    baselinePalmSpan = Mathf.Clamp(
                        Mathf.Lerp(baselinePalmSpan, clampedTargetBase, entryAlpha),
                        MinAdaptiveBaselinePalmSpan,
                        MaxAdaptiveBaselinePalmSpan);
                }
                else if (logVel <= BaselineCalmLogVelMax)
                {
                    baselineCalmTimer += dt;
                    if (baselineCalmTimer >= BaselineCalmDelaySeconds)
                    {
                        float slowAlpha = 1f - Mathf.Exp(-dt / BaselineIdleSlowTauSeconds);
                        baselinePalmSpan = Mathf.Clamp(
                            Mathf.Lerp(baselinePalmSpan, clampedTargetBase, slowAlpha),
                            MinAdaptiveBaselinePalmSpan,
                            MaxAdaptiveBaselinePalmSpan);
                    }
                }
                else
                {
                    baselineCalmTimer = 0f;
                }
            }
            else
            {
                baselineCalmTimer = 0f;
            }
        }

        // Delta kedalaman logaritmik simetris: >0 saat tangan maju mendekati layar, <0 saat tangan mundur menjauhi layar
        depthLogDeltaRaw = Mathf.Log(
            Mathf.Max(MinRigidPalmSpanClamp, filteredPalmSpan) / Mathf.Max(MinAdaptiveBaselinePalmSpan, baselinePalmSpan));
        float absLogDelta = Mathf.Abs(depthLogDeltaRaw);
        depthLogDeltaEffective = absLogDelta > DepthLogMicroDeadzone
            ? (depthLogDeltaRaw - (Mathf.Sign(depthLogDeltaRaw) * DepthLogMicroDeadzone))
            : 0f;

        // Juluran ujung telunjuk (#8) maju menuju kamera relatif terhadap pergelangan (#0):
        // di MediaPipe lm.z negatif = lebih dekat ke kamera -> -(lm8.z - lm0.z) positif saat jari menjulur maju menekan sakelar
        float rawIndexPoke = Mathf.Clamp(-(l8.z - l0.z), -0.18f, 0.28f);
        indexFingerPokeZ = indexPokeZEma.Update(rawIndexPoke, dt, IndexPokeEmaTauSeconds);

        return filteredPalmSpan;
    }

    /// <summary>
    /// Memperbarui fitur geometris 3D metrik (<c>worldLandmarks</c>) bersama proyeksi layar (<c>landmarks</c>) setiap frame
    /// inferensi baru masuk. Memproyeksikan &amp; menyaring seluruh kursor interaksi memakai <c>OneEuroFilter3D</c> Zero-GC
    /// pada sinar kamera <c>referenceDepth</c> konsisten. Tidak memanggil ulang <c>UpdateRigidPalmSpanOnly</c>. Zero-GC.
    /// </summary>
    public void UpdateFromLandmarks(
        List<NormalizedLandmark> landmarks,
        List<Landmark> worldLandmarks,
        Vector3[] rawLandmarksWorld,
        Transform camTransform,
        float referenceDepth = DefaultReferenceDepth,
        float cameraAspect = 1.3333f)
    {
        if (landmarks == null || landmarks.Count < 21 || rawLandmarksWorld == null || rawLandmarksWorld.Length < 21 || camTransform == null)
        {
            return;
        }

        float nowTime = Time.unscaledTime;
        float sampleDt = (lastLandmarkUpdateUnscaledTime >= 0f)
            ? Mathf.Clamp(nowTime - lastLandmarkUpdateUnscaledTime, 0.008f, 0.150f)
            : 0.033f;
        lastLandmarkUpdateUnscaledTime = nowTime;

        float safeAspect = Mathf.Clamp(cameraAspect, 0.65f, 2.40f);
        float safeRefDepth = Mathf.Max(0.50f, referenceDepth);
        cachedProjectionCamTransform = camTransform;
        cachedProjectionRefDepth = safeRefDepth;

        NormalizedLandmark l0 = landmarks[0];
        NormalizedLandmark l4 = landmarks[4];
        NormalizedLandmark l5 = landmarks[5];
        NormalizedLandmark l8 = landmarks[8];
        NormalizedLandmark l9 = landmarks[9];
        NormalizedLandmark l13 = landmarks[13];
        NormalizedLandmark l17 = landmarks[17];

        // 1. Quality Gate: validasi palmScale 2D mentah SEBELUM clamp & edge deadzone longgar [0.015, 0.985]
        //    Fokus pada pusat telapak & ujung bidik utama (#4, #8, #9) agar pergelangan (#0) atau kelingking (#17) di tepi layar tidak memutus gestur.
        float rawPalmScale2D = 0.5f * (Dist2DCompensated(l0, l9, safeAspect) + Dist2DCompensated(l5, l17, safeAspect));
        isPalmScaleValid = rawPalmScale2D >= MinValidRawPalmScale2D;

        float cx2D = (l0.x + l5.x + l9.x + l13.x + l17.x) * 0.20f;
        float cy2D = (l0.y + l5.y + l9.y + l13.y + l17.y) * 0.20f;
        isPalmCenterInSafeFrame = IsInsideSafeFrame(cx2D, cy2D);
        isInEdgeDeadzone = !isPalmCenterInSafeFrame
                           || !IsInsideSafeFrame(l4.x, l4.y)
                           || !IsInsideSafeFrame(l8.x, l8.y)
                           || !IsInsideSafeFrame(l9.x, l9.y);

        // 2. Pilih sumber vektor 3D: handWorldLandmarks (3D meter) dengan fallback 2D aspect-compensated bila kosong
        bool hasWorld3D = worldLandmarks != null && worldLandmarks.Count >= 21;
        Vector3 p0, p1, p2, p3, p4, p5, p9, p13, p17;
        float palmSpanMetric;

        if (hasWorld3D)
        {
            p0 = ToVec3(worldLandmarks[0]);
            p1 = ToVec3(worldLandmarks[1]);
            p2 = ToVec3(worldLandmarks[2]);
            p3 = ToVec3(worldLandmarks[3]);
            p4 = ToVec3(worldLandmarks[4]);
            p5 = ToVec3(worldLandmarks[5]);
            p9 = ToVec3(worldLandmarks[9]);
            p13 = ToVec3(worldLandmarks[13]);
            p17 = ToVec3(worldLandmarks[17]);

            float d09W = Vector3.Distance(p0, p9);
            float d517W = Vector3.Distance(p5, p17);
            float rawSpan3D = 0.5f * (d09W + d517W);
            if (rawSpan3D >= MinValidPalmSpan3DMeters && !float.IsNaN(rawSpan3D) && !float.IsInfinity(rawSpan3D))
            {
                isUsingWorld3DMetrics = true;
                palmSpanMetric = rawSpan3D;
            }
            else
            {
                hasWorld3D = false;
                isUsingWorld3DMetrics = false;
                p0 = ToAspectCompensatedVec3(l0, safeAspect);
                p1 = ToAspectCompensatedVec3(landmarks[1], safeAspect);
                p2 = ToAspectCompensatedVec3(landmarks[2], safeAspect);
                p3 = ToAspectCompensatedVec3(landmarks[3], safeAspect);
                p4 = ToAspectCompensatedVec3(l4, safeAspect);
                p5 = ToAspectCompensatedVec3(l5, safeAspect);
                p9 = ToAspectCompensatedVec3(l9, safeAspect);
                p13 = ToAspectCompensatedVec3(l13, safeAspect);
                p17 = ToAspectCompensatedVec3(l17, safeAspect);
                palmSpanMetric = Mathf.Max(MinPalmScaleClamp, rawPalmScale2D);
            }
        }
        else
        {
            isUsingWorld3DMetrics = false;
            p0 = ToAspectCompensatedVec3(l0, safeAspect);
            p1 = ToAspectCompensatedVec3(landmarks[1], safeAspect);
            p2 = ToAspectCompensatedVec3(landmarks[2], safeAspect);
            p3 = ToAspectCompensatedVec3(landmarks[3], safeAspect);
            p4 = ToAspectCompensatedVec3(l4, safeAspect);
            p5 = ToAspectCompensatedVec3(l5, safeAspect);
            p9 = ToAspectCompensatedVec3(l9, safeAspect);
            p13 = ToAspectCompensatedVec3(l13, safeAspect);
            p17 = ToAspectCompensatedVec3(l17, safeAspect);
            palmSpanMetric = Mathf.Max(MinPalmScaleClamp, rawPalmScale2D);
        }

        Vector3 palmCenter3D = (p0 + p5 + p9 + p13 + p17) * 0.20f;

        // 3. Evaluasi sudut sendi 3D (dot product MCP->PIP, PIP->DIP, DIP->TIP + Wrist->MCP) untuk 4 jari utama
        float sumPalmRatio = 0f;
        float sumWeightedBend = 0f;
        int firmlyFolded = 0;
        bool indexFirmlyFolded = false;
        float indexSmoothPalmRatio = 1.40f;

        for (int i = 0; i < 4; i++)
        {
            int mcpIdx = FingerMcpIndices[i];
            int pipIdx = FingerPipIndices[i];
            int dipIdx = FingerDipIndices[i];
            int tipIdx = FingerTipIndices[i];

            Vector3 pMcp = hasWorld3D ? ToVec3(worldLandmarks[mcpIdx]) : ToAspectCompensatedVec3(landmarks[mcpIdx], safeAspect);
            Vector3 pPip = hasWorld3D ? ToVec3(worldLandmarks[pipIdx]) : ToAspectCompensatedVec3(landmarks[pipIdx], safeAspect);
            Vector3 pDip = hasWorld3D ? ToVec3(worldLandmarks[dipIdx]) : ToAspectCompensatedVec3(landmarks[dipIdx], safeAspect);
            Vector3 pTip = hasWorld3D ? ToVec3(worldLandmarks[tipIdx]) : ToAspectCompensatedVec3(landmarks[tipIdx], safeAspect);

            float bendMcp = ComputeSegmentBendDegrees(p0, pMcp, pPip);
            float bendPip = ComputeSegmentBendDegrees(pMcp, pPip, pDip);
            float bendDip = ComputeSegmentBendDegrees(pPip, pDip, pTip);

            float rawPipDipBend = bendPip + bendDip;
            float rawWeightedBend = (0.60f * bendMcp) + bendPip + (0.80f * bendDip);
            float rawTipPalmRatio = Vector3.Distance(pTip, palmCenter3D) / palmSpanMetric;

            float smoothPipDip = fingerPipDipBendEma[i].Update(rawPipDipBend, sampleDt, MetricEmaTauSeconds);
            float smoothWeighted = fingerWeightedBendEma[i].Update(rawWeightedBend, sampleDt, MetricEmaTauSeconds);
            float smoothPalmRatio = fingerPalmRatioEma[i].Update(rawTipPalmRatio, sampleDt, MetricEmaTauSeconds);

            sumPalmRatio += smoothPalmRatio;
            sumWeightedBend += smoothWeighted;

            bool isFirmlyFolded = (smoothPipDip >= FingerFirmFoldPipDipDeg && smoothWeighted >= FingerFirmFoldWeightedDeg)
                                  || (smoothWeighted >= FingerFirmFoldDeepWeightedDeg && smoothPalmRatio <= FingerFirmFoldDeepPalmRatio)
                                  || (smoothPipDip >= FingerFirmFoldTuckPipDipDeg && smoothPalmRatio <= FingerFirmFoldTuckPalmRatio);
            if (isFirmlyFolded)
            {
                firmlyFolded++;
            }

            if (i == 0)
            {
                indexFirmlyFolded = isFirmlyFolded;
                indexSmoothPalmRatio = smoothPalmRatio;
            }

            // Schmitt-trigger per-jari berbasis sudut sendi 3D (anti-flicker untuk Summon & Input Switch)
            if (!mainFingerExtended[i])
            {
                if (smoothPipDip <= FingerExtendMaxPipDipDeg
                    && smoothWeighted <= FingerExtendMaxWeightedDeg
                    && smoothPalmRatio >= FingerExtendMinPalmRatio)
                {
                    mainFingerExtended[i] = true;
                }
            }
            else if (smoothPipDip >= FingerUnextendMinPipDipDeg
                     || smoothWeighted >= FingerUnextendMinWeightedDeg
                     || smoothPalmRatio <= FingerUnextendMaxPalmRatio)
            {
                mainFingerExtended[i] = false;
            }
        }

        meanCurlRatio = sumPalmRatio * 0.25f;
        meanWeightedBendDeg = sumWeightedBend * 0.25f;
        foldedFingerCount = firmlyFolded;

        // 4. Evaluasi ibu jari 3D (orientation-invariant) untuk Open-5 Bloom, NAND (jempol lipat), & NOT (jempol kuat)
        float rawThumbBend = (0.60f * ComputeSegmentBendDegrees(p1, p2, p3)) + ComputeSegmentBendDegrees(p2, p3, p4);
        float rawThumbIndexRatio = Vector3.Distance(p4, p5) / palmSpanMetric;
        float rawThumbPinkyRatio = Vector3.Distance(p4, p17) / palmSpanMetric;

        float smoothThumbBend = thumbBendEma.Update(rawThumbBend, sampleDt, MetricEmaTauSeconds);
        float smoothThumbIndexRatio = thumbIndexRatioEma.Update(rawThumbIndexRatio, sampleDt, MetricEmaTauSeconds);
        float smoothThumbPinkyRatio = thumbPinkyRatioEma.Update(rawThumbPinkyRatio, sampleDt, MetricEmaTauSeconds);

        if (!isThumbExtended)
        {
            if (smoothThumbIndexRatio > ThumbExtendEnterIndexRatio
                && smoothThumbPinkyRatio > ThumbExtendEnterPinkyRatio
                && smoothThumbBend < ThumbExtendEnterMaxBendDeg)
            {
                isThumbExtended = true;
            }
        }
        else if (smoothThumbIndexRatio < ThumbExtendExitIndexRatio
                 || smoothThumbPinkyRatio < ThumbExtendExitPinkyRatio
                 || smoothThumbBend > ThumbExtendExitMinBendDeg)
        {
            isThumbExtended = false;
        }

        isThumbStrongOut = smoothThumbIndexRatio > ThumbStrongNotIndexRatio
                           && smoothThumbPinkyRatio > ThumbStrongNotPinkyRatio
                           && smoothThumbBend < ThumbStrongNotMaxBendDeg;

        // 5. Evaluasi Kanal Pinch Hibrida (Anti Z-Occlusion) vs Kanal Fist
        // Pinch hibrida: ambil nilai minimum antara rasio 3D (#4-#8 & #4-#7*1.15 / palmSpan3D)
        // DAN rasio 2D aspect-compensated (#4-#8 & #4-#7*1.15 / palmSpan2D) agar cubitan miring/samping selalu mulus.
        NormalizedLandmark l7 = landmarks[7];
        float safePalmSpan2D = Mathf.Max(MinPalmScaleClamp, rawPalmScale2D);
        float pinchRatio2DTip = Dist2DCompensated(l4, l8, safeAspect) / safePalmSpan2D;
        float pinchRatio2DDip = (Dist2DCompensated(l4, l7, safeAspect) / safePalmSpan2D) * PinchDipProximityWeight;
        float pinchRatio2D = Mathf.Min(pinchRatio2DTip, pinchRatio2DDip);

        Vector3 pIndexDip = hasWorld3D ? ToVec3(worldLandmarks[7]) : ToAspectCompensatedVec3(l7, safeAspect);
        Vector3 pIndexTip = hasWorld3D ? ToVec3(worldLandmarks[8]) : ToAspectCompensatedVec3(l8, safeAspect);
        float pinchRatio3DTip = Vector3.Distance(p4, pIndexTip) / palmSpanMetric;
        float pinchRatio3DDip = (Vector3.Distance(p4, pIndexDip) / palmSpanMetric) * PinchDipProximityWeight;
        float pinchRatio3D = Mathf.Min(pinchRatio3DTip, pinchRatio3DDip);

        float rawPinchRatio = Mathf.Min(pinchRatio3D, pinchRatio2D);
        currentPinchRatio = pinchRatioEma.Update(rawPinchRatio, sampleDt, MetricEmaTauSeconds);

        // Pinch santai tanpa harus pose "OK": jari tengah/manis/kelingking bebas menekuk rileks.
        // Cukup bedakan dari Fist dengan !isFistActive dan tidak semua 4 jari tergenggam penuh ke telapak.
        bool allFourFingersClenched = (firmlyFolded >= 4)
                                      && indexFirmlyFolded
                                      && (indexSmoothPalmRatio <= IndexTuckedForFullFistMaxPalmRatio)
                                      && (meanCurlRatio < FistEnterMaxMeanCurlRatio);
        bool pinchAnatomyValid = !isFistActive && !allFourFingersClenched;

        rawPinchEnterCondition = (currentPinchRatio < PinchEnterRatio) && pinchAnatomyValid;
        rawPinchExitCondition = currentPinchRatio > PinchExitRatio;
        isPrePinchGuardActive = (currentPinchRatio < PrePinchSuppressSummonRatio) && pinchAnatomyValid;

        // Fist: jari utama menekuk tegas; saat Fist belum terkunci, Pinch tetap menang jika #4-#8 rapat & bukan kepalan 4 jari penuh
        rawEnterCondition = (firmlyFolded >= 3)
                            && (meanWeightedBendDeg >= FistEnterMeanWeightedBendDeg)
                            && (meanCurlRatio < FistEnterMaxMeanCurlRatio)
                            && !isThumbStrongOut
                            && (isFistActive || !rawPinchEnterCondition);
        rawExitCondition = (meanWeightedBendDeg < FistExitMeanWeightedBendDeg) && (firmlyFolded <= 1);

        // Hitung frame inferensi valid berturut-turut (mencegah glitch 1 frame pada 15-30 FPS lolos debounce)
        bool qualityAllowsCount = IsQualityGatePassing;
        if (rawEnterCondition && qualityAllowsCount)
        {
            if (fistEnterConsecFrames < 10)
            {
                fistEnterConsecFrames++;
            }
        }
        else
        {
            fistEnterConsecFrames = 0;
        }

        if (rawPinchEnterCondition && qualityAllowsCount)
        {
            if (pinchEnterConsecFrames < 10)
            {
                pinchEnterConsecFrames++;
            }
        }
        else
        {
            pinchEnterConsecFrames = 0;
        }

        // 6. Klasifikasi Circle Summon berbasis IDENTITAS JARI SPESIFIK (bukan sekadar jumlah acak)
        bool idxExt = mainFingerExtended[0];
        bool midExt = mainFingerExtended[1];
        bool rngExt = mainFingerExtended[2];
        bool pnkExt = mainFingerExtended[3];
        int extendedMainCount = (idxExt ? 1 : 0) + (midExt ? 1 : 0) + (rngExt ? 1 : 0) + (pnkExt ? 1 : 0);

        isRelaxedOpenFiveBloom = (extendedMainCount >= 3)
                                 && (meanWeightedBendDeg < RelaxedOpenFiveMaxMeanBendDeg)
                                 && isThumbExtended
                                 && !isPinchActive
                                 && !isPrePinchGuardActive;

        if (isPinchActive || isPrePinchGuardActive)
        {
            currentSummonPose = SummonHandPose.None;
        }
        else if (idxExt && midExt && rngExt && pnkExt && isThumbExtended && meanWeightedBendDeg < OpenFiveBloomMaxMeanBendDeg)
        {
            currentSummonPose = SummonHandPose.OpenFiveBloom;
        }
        else if (idxExt && midExt && rngExt && pnkExt && !isThumbExtended)
        {
            // NAND: 4 jari utama teracung + jempol terlipat
            currentSummonPose = SummonHandPose.FourFingersNand;
        }
        else if (idxExt && midExt && rngExt && !pnkExt)
        {
            // XOR: hanya telunjuk + tengah + manis
            currentSummonPose = SummonHandPose.ThreeFingersXor;
        }
        else if (idxExt && midExt && !rngExt && !pnkExt)
        {
            // OR: hanya telunjuk + tengah
            currentSummonPose = SummonHandPose.TwoFingersOr;
        }
        else if (idxExt && !midExt && !rngExt && !pnkExt)
        {
            // AND: hanya telunjuk
            currentSummonPose = SummonHandPose.OneFingerAnd;
        }
        else if (!idxExt && !midExt && !rngExt && !pnkExt && firmlyFolded >= 3 && isThumbStrongOut)
        {
            // NOT: hanya jempol teracung kuat (4 jari utama tertekuk)
            currentSummonPose = SummonHandPose.ThumbOnlyNot;
        }
        else
        {
            currentSummonPose = SummonHandPose.None;
        }

        // 7. Proyeksi & One-Euro Filter 3D Zero-GC untuk koordinat interaksi pada sinar kamera bidang referensi (WorkbenchDepth):
        //    Telapak, Pinch (#4-#8), Telunjuk (#8), Jempol (#4), & Summon Tip disaring satu lapis bersih tanpa tumpukan smoothing.
        Vector3 rawPalmWorld = (rawLandmarksWorld[9] * 0.50f)
                               + (rawLandmarksWorld[5] * 0.30f)
                               + (rawLandmarksWorld[0] * 0.20f);
        Vector3 rawPinchWorld = (rawLandmarksWorld[4] + rawLandmarksWorld[8]) * 0.5f;
        Vector3 rawIndexTipWorld = rawLandmarksWorld[8];
        Vector3 rawThumbTipWorld = rawLandmarksWorld[4];

        Vector3 rawActiveSummonTipWorld;
        if (currentSummonPose == SummonHandPose.ThumbOnlyNot)
        {
            rawActiveSummonTipWorld = rawThumbTipWorld;
        }
        else if (idxExt)
        {
            rawActiveSummonTipWorld = rawIndexTipWorld;
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
            rawActiveSummonTipWorld = rawLandmarksWorld[activeTipLandmarkIdx];
        }

        palmCursorFilter.PushSample(rawPalmWorld, sampleDt, CursorOneEuroPalmMinCutoff, CursorOneEuroPalmBeta, CursorOneEuroDCutoff, CursorOneEuroWorldSpeedScale);
        pinchPointFilter.PushSample(rawPinchWorld, sampleDt, CursorOneEuroPinchMinCutoff, CursorOneEuroPinchBeta, CursorOneEuroDCutoff, CursorOneEuroWorldSpeedScale);
        indexTipFilter.PushSample(rawIndexTipWorld, sampleDt, CursorOneEuroIndexMinCutoff, CursorOneEuroIndexBeta, CursorOneEuroDCutoff, CursorOneEuroWorldSpeedScale);
        thumbTipFilter.PushSample(rawThumbTipWorld, sampleDt, CursorOneEuroPalmMinCutoff, CursorOneEuroPalmBeta, CursorOneEuroDCutoff, CursorOneEuroWorldSpeedScale);
        activeSummonTipFilter.PushSample(rawActiveSummonTipWorld, sampleDt, CursorOneEuroIndexMinCutoff, CursorOneEuroIndexBeta, CursorOneEuroDCutoff, CursorOneEuroWorldSpeedScale);

        SyncFilteredCursorOutputs();

        Vector2 samplePalmRayXY = ProjectWorldToRefRayPlane(camTransform, rawPalmWorld, safeRefDepth);
        Vector2 samplePinchRayXY = ProjectWorldToRefRayPlane(camTransform, rawPinchWorld, safeRefDepth);
        Vector2 sampleIndexRayXY = ProjectWorldToRefRayPlane(camTransform, rawIndexTipWorld, safeRefDepth);

        if (!hasPrevSampleCursors)
        {
            prevRawPalmLocalXY = samplePalmRayXY;
            prevRawPinchLocalXY = samplePinchRayXY;
            prevRawIndexTipLocalXY = sampleIndexRayXY;
            CursorSpeedUnitsPerSec = 0f;
            PinchSpeedUnitsPerSec = 0f;
            IndexTipSpeedUnitsPerSec = 0f;
            hasPrevSampleCursors = true;
        }
        else
        {
            CursorSpeedUnitsPerSec = Vector2.Distance(samplePalmRayXY, prevRawPalmLocalXY) / sampleDt;
            PinchSpeedUnitsPerSec = Vector2.Distance(samplePinchRayXY, prevRawPinchLocalXY) / sampleDt;
            IndexTipSpeedUnitsPerSec = Vector2.Distance(sampleIndexRayXY, prevRawIndexTipLocalXY) / sampleDt;
            prevRawPalmLocalXY = samplePalmRayXY;
            prevRawPinchLocalXY = samplePinchRayXY;
            prevRawIndexTipLocalXY = sampleIndexRayXY;
        }
    }

    private void StepCursorFilters(float dt)
    {
        palmCursorFilter.Tick(dt);
        pinchPointFilter.Tick(dt);
        indexTipFilter.Tick(dt);
        thumbTipFilter.Tick(dt);
        activeSummonTipFilter.Tick(dt);
        SyncFilteredCursorOutputs();
    }

    private void SyncFilteredCursorOutputs()
    {
        if (cachedProjectionCamTransform == null)
        {
            return;
        }

        Matrix4x4 worldToCam = cachedProjectionCamTransform.worldToLocalMatrix;
        float refDepth = cachedProjectionRefDepth;

        Vector3 filteredPalm = palmCursorFilter.CurrentValue;
        Vector3 filteredPinch = pinchPointFilter.CurrentValue;
        Vector3 filteredIndex = indexTipFilter.CurrentValue;
        Vector3 filteredThumb = thumbTipFilter.CurrentValue;
        Vector3 filteredSummonTip = activeSummonTipFilter.CurrentValue;

        PalmCursorWorldPosition = filteredPalm;
        PalmCursorLocalXY = ProjectWorldToRefRayPlaneMatrix(ref worldToCam, filteredPalm, refDepth);

        PinchPointWorldPosition = filteredPinch;
        PinchPointLocalXY = ProjectWorldToRefRayPlaneMatrix(ref worldToCam, filteredPinch, refDepth);

        IndexTipWorldPosition = filteredIndex;
        IndexTipLocalXY = ProjectWorldToRefRayPlaneMatrix(ref worldToCam, filteredIndex, refDepth);

        ThumbTipLocalXY = ProjectWorldToRefRayPlaneMatrix(ref worldToCam, filteredThumb, refDepth);
        ActiveSummonTipLocalXY = ProjectWorldToRefRayPlaneMatrix(ref worldToCam, filteredSummonTip, refDepth);
    }

    /// <summary>
    /// Memproses warm-up 80 ms, kemajuan One-Euro 3D kursor per-frame render (<c>dt</c>), hysteresis 3D,
    /// enter debounce ganda, asymmetric exit debounce, CableDrag sticky lock (<c>&gt; 0.75f</c>, <c>95 ms</c>),
    /// jeda timer saat frame drop, &amp; occlusion grace ~200 ms. Zero-GC.
    /// </summary>
    public void TickTemporalState(
        float dt,
        bool isHandDetected,
        bool isFistStickyHoldActive = false,
        bool isPinchStickyHoldActive = false,
        bool relaxEdgeDeadzoneForTwoHand = false,
        bool isCableDragStickyActive = false,
        bool isSlotMissingFrame = false)
    {
        fistJustStarted = false;
        fistJustEnded = false;
        pinchJustStarted = false;
        pinchJustEnded = false;
        pinchEndedByTrackingLoss = false;

        if (dt <= 0.0001f)
        {
            dt = 0.016f;
        }

        if (isHandDetected)
        {
            StepCursorFilters(dt);

            if (!wasDetectedInTick)
            {
                wasDetectedInTick = true;
                entryWarmupTimer = HandEntryWarmupSeconds;
                fistEnterConsecFrames = 0;
                pinchEnterConsecFrames = 0;
                enterHoldTimer = 0f;
                pinchEnterHoldTimer = 0f;
            }
            else if (!isSlotMissingFrame && entryWarmupTimer > 0f)
            {
                entryWarmupTimer = Mathf.Max(0f, entryWarmupTimer - dt);
            }

            if (!isSlotMissingFrame)
            {
                occlusionGraceTimer = isFistStickyHoldActive ? StickyOcclusionGraceSeconds : OcclusionGraceSeconds;
                pinchOcclusionGraceTimer = (isCableDragStickyActive || isPinchStickyHoldActive)
                    ? PinchStickyOcclusionGraceSeconds
                    : PinchOcclusionGraceSeconds;
            }
            else
            {
                // Selama tangan dalam jendela frame-drop grace (Stage 1), jeda timer enter/exit agar 1 frame landmark
                // terdistorsi sesaat sebelum tangan hilang tidak memicu lepas gerbang atau commit kabel palsu.
                return;
            }
        }
        else
        {
            wasDetectedInTick = false;
            entryWarmupTimer = HandEntryWarmupSeconds;
            CursorSpeedUnitsPerSec = 0f;
            PinchSpeedUnitsPerSec = 0f;
            IndexTipSpeedUnitsPerSec = 0f;
            hasPrevSampleCursors = false;
            lastLandmarkUpdateUnscaledTime = -1f;
            fistEnterConsecFrames = 0;
            pinchEnterConsecFrames = 0;
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
                    pinchEndedByTrackingLoss = true;
                    pinchExitHoldTimer = 0f;
                }
            }
            return;
        }

        bool qualityGateOk = relaxEdgeDeadzoneForTwoHand ? IsTwoHandQualityGatePassing : IsQualityGatePassing;

        // ---------------------------------------------------------------------
        // KANAL 1: PINCH / CUBIT 3D (KHUSUS KABEL — DISJOINT DARI FIST)
        // ---------------------------------------------------------------------
        if (!isPinchActive)
        {
            pinchExitHoldTimer = 0f;

            bool prePinchingNow = qualityGateOk && isPrePinchGuardActive && !isFistActive && !isFistStickyHoldActive;
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

            bool allowPinchEnter = qualityGateOk && rawPinchEnterCondition && !isFistActive && !isFistStickyHoldActive;
            if (allowPinchEnter)
            {
                pinchEnterHoldTimer += dt;
                if (pinchEnterConsecFrames >= PinchEnterMinConsecutiveFrames
                    && pinchEnterHoldTimer >= PinchEnterDebounceSeconds)
                {
                    isPinchActive = true;
                    pinchJustStarted = true;
                    pinchEnterHoldTimer = 0f;
                    isPrePinchClosing = false;
                }
            }
            else
            {
                pinchEnterHoldTimer = Mathf.Max(0f, pinchEnterHoldTimer - (dt * 2.5f));
            }
        }
        else
        {
            hasLatchedPinch = false;
            isPrePinchClosing = false;
            pinchEnterHoldTimer = 0f;

            // CableDrag Sticky Lock (terisolasi dari mode 2-tangan): wajib membuka lebar (> 0.75f) stabil >= 95 ms
            // agar kabel tidak putus di tengah jalan akibat 1-2 frame jitter saat bergerak pelan maupun cepat.
            bool pinchExitMet = isCableDragStickyActive
                ? (currentPinchRatio > PinchCableStickyExitRatio)
                : rawPinchExitCondition;

            if (pinchExitMet)
            {
                float requiredExit;
                if (isCableDragStickyActive)
                {
                    requiredExit = PinchCableStickyExitDebounceSeconds;
                }
                else
                {
                    float speedT = Mathf.Clamp01((PinchSpeedUnitsPerSec - SlowReleaseSpeedThreshold) / (FastReleaseSpeedThreshold - SlowReleaseSpeedThreshold));
                    requiredExit = isPinchStickyHoldActive
                        ? Mathf.Lerp(PinchExitSlowSeconds, PinchExitFastSeconds, speedT)
                        : PinchExitSlowSeconds;
                }

                pinchExitHoldTimer += dt;
                if (pinchExitHoldTimer >= requiredExit)
                {
                    isPinchActive = false;
                    pinchJustEnded = true;
                    pinchEndedByTrackingLoss = false;
                    pinchExitHoldTimer = 0f;
                }
            }
            else
            {
                pinchExitHoldTimer = Mathf.Max(0f, pinchExitHoldTimer - (dt * 2f));
            }
        }

        // ---------------------------------------------------------------------
        // KANAL 2: FIST / KEPAL 3D (KHUSUS OBJEK — 4 JARI MENEKUK TEGAS)
        // ---------------------------------------------------------------------
        if (!isFistActive)
        {
            exitHoldTimer = 0f;

            bool curlingNow = qualityGateOk
                              && (meanWeightedBendDeg >= PreFistLatchMinWeightedBendDeg)
                              && !isPinchActive
                              && !isPinchStickyHoldActive;
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

            // Tanpa pemblokiran oleh pre-pinch: karena Pinch & Fist sudah terpisah anatomi 3D, transisi Open->Fist mulus tanpa race
            bool isSummonGatePose = currentSummonPose == SummonHandPose.OneFingerAnd
                                    || currentSummonPose == SummonHandPose.TwoFingersOr
                                    || currentSummonPose == SummonHandPose.ThreeFingersXor
                                    || currentSummonPose == SummonHandPose.FourFingersNand
                                    || currentSummonPose == SummonHandPose.ThumbOnlyNot;
            bool blockedByPinch = isPinchActive || isPinchStickyHoldActive || rawPinchEnterCondition;
            bool allowFistEnter = qualityGateOk
                                  && rawEnterCondition
                                  && !blockedByPinch
                                  && (isFistStickyHoldActive || (!mainFingerExtended[0] && !isSummonGatePose));
            if (allowFistEnter)
            {
                enterHoldTimer += dt;
                if (fistEnterConsecFrames >= FistEnterMinConsecutiveFrames
                    && enterHoldTimer >= FistEnterDebounceSeconds)
                {
                    isFistActive = true;
                    fistJustStarted = true;
                    enterHoldTimer = 0f;
                    isPreFistCurling = false;
                }
            }
            else
            {
                enterHoldTimer = Mathf.Max(0f, enterHoldTimer - (dt * 2.5f));
            }
        }
        else
        {
            hasLatchedCursor = false;
            isPreFistCurling = false;
            enterHoldTimer = 0f;

            // Sticky-Drag 3D: sudut rata-rata membuka di bawah 80° (atau 86° normal) & <= 1 jari tertekuk, adaptif 55-115 ms
            bool exitMet = isFistStickyHoldActive
                ? (meanWeightedBendDeg < FistStickyExitMeanWeightedBendDeg && foldedFingerCount <= 1)
                : rawExitCondition;

            if (exitMet)
            {
                float speedT = Mathf.Clamp01((CursorSpeedUnitsPerSec - SlowReleaseSpeedThreshold) / (FastReleaseSpeedThreshold - SlowReleaseSpeedThreshold));
                float requiredExit = Mathf.Lerp(FistExitSlowSeconds, FistExitFastSeconds, speedT);
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

    private static bool IsInsideSafeFrame(float normX, float normY)
    {
        return normX >= EdgeDeadzoneMin
               && normX <= EdgeDeadzoneMax
               && normY >= EdgeDeadzoneMin
               && normY <= EdgeDeadzoneMax;
    }

    private static Vector3 ToVec3(Landmark lm)
    {
        return new Vector3(lm.x, lm.y, lm.z);
    }

    private static Vector3 ToAspectCompensatedVec3(NormalizedLandmark lm, float aspectX)
    {
        return new Vector3((lm.x - 0.5f) * aspectX, 0.5f - lm.y, lm.z * 0.60f);
    }

    private static float ComputeSegmentBendDegrees(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 v1 = b - a;
        Vector3 v2 = c - b;
        float len1Sq = v1.sqrMagnitude;
        float len2Sq = v2.sqrMagnitude;
        if (len1Sq < 1e-8f || len2Sq < 1e-8f)
        {
            return 0f;
        }

        float cosTheta = Vector3.Dot(v1, v2) / Mathf.Sqrt(len1Sq * len2Sq);
        return Mathf.Acos(Mathf.Clamp(cosTheta, -1f, 1f)) * Mathf.Rad2Deg;
    }

    private static Vector2 ProjectWorldToRefRayPlane(Transform camTransform, Vector3 worldPos, float referenceDepth)
    {
        Vector3 camLocal = camTransform.InverseTransformPoint(worldPos);
        float safeZ = Mathf.Max(0.35f, camLocal.z);
        float scale = referenceDepth / safeZ;
        return new Vector2(camLocal.x * scale, camLocal.y * scale);
    }

    private static Vector2 ProjectWorldToRefRayPlaneMatrix(ref Matrix4x4 worldToCam, Vector3 worldPos, float referenceDepth)
    {
        Vector3 camLocal = worldToCam.MultiplyPoint3x4(worldPos);
        float safeZ = Mathf.Max(0.35f, camLocal.z);
        float scale = referenceDepth / safeZ;
        return new Vector2(camLocal.x * scale, camLocal.y * scale);
    }

    private static float Dist2DCompensated(NormalizedLandmark a, NormalizedLandmark b, float aspectX)
    {
        float dx = (a.x - b.x) * Mathf.Clamp(aspectX * 0.75f, 0.70f, 1.35f);
        float dy = a.y - b.y;
        return Mathf.Sqrt((dx * dx) + (dy * dy));
    }

    private static float Dist3DCompensated(NormalizedLandmark a, NormalizedLandmark b, float aspectX)
    {
        // Normalisasi skala horizontal-vertikal ke tinggi layar + kompensasi kemiringan Z tulang telapak
        float dx = (a.x - b.x) * Mathf.Clamp(aspectX * 0.75f, 0.70f, 1.35f);
        float dy = a.y - b.y;
        float dz = Mathf.Clamp(a.z - b.z, -0.18f, 0.18f) * 0.52f;
        return Mathf.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}
