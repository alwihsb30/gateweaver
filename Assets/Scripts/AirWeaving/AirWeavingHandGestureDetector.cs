using System.Collections.Generic;
using UnityEngine;
using NormalizedLandmark = Mediapipe.Tasks.Components.Containers.NormalizedLandmark;

/// <summary>
/// Detektor gestur Kepal (Fist) vs Buka (Open) & Kursor Telapak Anti-Lompat untuk GateWeaver AR v7.0 Fase 3.
/// - Scale-invariant menggunakan rata-rata panjang telapak (#0-#9 & #5-#17) dan pusat telapak (#0,#5,#9,#13,#17).
/// - Mengukur 4 jari utama (#8, #12, #16, #20) tanpa ketergantungan ibu jari (#4) yang rawan teroklusi saat mengepal.
/// - Schmitt-trigger hysteresis lebar (masuk < 0.88, keluar > 1.18), asymmetric debounce (45 ms masuk / 80-130 ms keluar),
///   velocity guard saat seret cepat, occlusion grace 180 ms, dan pre-fist target latch 55 ms. Zero-GC.
/// </summary>
public sealed class AirWeavingHandGestureDetector
{
    private const float MinPalmScaleClamp = 0.055f;
    private const float FistEnterMeanRatio = 0.88f;
    private const float FistExitMeanRatio = 1.18f;
    private const float FistStickyExitMeanRatio = 1.15f;
    private const float FingerFoldedPalmRatio = 0.92f;
    private const float FingerFoldedPipRatio = 1.04f;
    private const float PreFistLatchMeanRatio = 1.02f;

    private const float DebounceEnterSeconds = 0.045f;
    private const float DebounceExitSlowSeconds = 0.055f;
    private const float DebounceExitFastSeconds = 0.115f;
    private const float SlowReleaseSpeedThreshold = 1.50f;
    private const float FastReleaseSpeedThreshold = 2.50f;
    private const float OcclusionGraceSeconds = 0.180f;
    private const float StickyOcclusionGraceSeconds = 0.180f;
    private const float PreFistLatchMaxSeconds = 0.060f;

    private static readonly int[] FingerTipIndices = { 8, 12, 16, 20 };
    private static readonly int[] FingerPipIndices = { 6, 10, 14, 18 };

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

    public bool IsFistActive => isFistActive;
    public bool FistJustStarted => fistJustStarted;
    public bool FistJustEnded => fistJustEnded;
    public bool IsPreFistCurling => isPreFistCurling;
    public float MeanCurlRatio => meanCurlRatio;
    public int FoldedFingerCount => foldedFingerCount;
    public float CursorSpeedUnitsPerSec { get; private set; }
    public Vector3 PalmCursorWorldPosition { get; private set; }
    public Vector2 PalmCursorLocalXY { get; private set; }
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
        CursorSpeedUnitsPerSec = 0f;
        PalmCursorWorldPosition = Vector3.zero;
        PalmCursorLocalXY = Vector2.zero;
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
        NormalizedLandmark l5 = landmarks[5];
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

            if (rPalm < FingerFoldedPalmRatio || rPip < FingerFoldedPipRatio)
            {
                folded++;
            }
        }

        meanCurlRatio = sumRatio * 0.25f;
        foldedFingerCount = folded;
        rawEnterCondition = (meanCurlRatio < FistEnterMeanRatio) && (foldedFingerCount >= 3);
        rawExitCondition = (meanCurlRatio > FistExitMeanRatio) && (foldedFingerCount <= 1);

        // 4. Kursor telapak anti-lompat: 0.50*L9 + 0.30*L5 + 0.20*L0 (pergeseran < 0.02 unit saat mengepal)
        Vector3 palmWorld = (smoothedLandmarksWorld[9] * 0.50f)
                            + (smoothedLandmarksWorld[5] * 0.30f)
                            + (smoothedLandmarksWorld[0] * 0.20f);
        PalmCursorWorldPosition = palmWorld;

        Vector3 localPt = camTransform.InverseTransformPoint(palmWorld);
        PalmCursorLocalXY = new Vector2(localPt.x, localPt.y);
    }

    /// <summary>
    /// Memproses hysteresis, asymmetric debounce, velocity guard, sticky-hold saat seret kabel/gerbang, & occlusion grace. Zero-GC.
    /// </summary>
    public void TickTemporalState(float dt, bool isHandDetected, bool isStickyHoldActive = false)
    {
        fistJustStarted = false;
        fistJustEnded = false;

        if (dt <= 0.0001f)
        {
            dt = 0.016f;
        }

        if (isHandDetected)
        {
            occlusionGraceTimer = isStickyHoldActive ? StickyOcclusionGraceSeconds : OcclusionGraceSeconds;
            if (hasPrevCursor)
            {
                float instantSpeed = Vector2.Distance(PalmCursorLocalXY, prevCursorLocalXY) / dt;
                CursorSpeedUnitsPerSec = Mathf.Lerp(CursorSpeedUnitsPerSec, instantSpeed, Mathf.Clamp01(dt * 12f));
            }
            prevCursorLocalXY = PalmCursorLocalXY;
            hasPrevCursor = true;
        }
        else
        {
            CursorSpeedUnitsPerSec = 0f;
            enterHoldTimer = 0f;
            isPreFistCurling = false;
            hasLatchedCursor = false;
            preFistLatchTimer = 0f;

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
            return;
        }

        if (!isFistActive)
        {
            exitHoldTimer = 0f;

            // Pre-Fist Target Latch: kunci koordinat kursor singkat saat jari mulai menekuk menuju kepalan
            bool curlingNow = meanCurlRatio < PreFistLatchMeanRatio;
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

            if (rawEnterCondition)
            {
                enterHoldTimer += dt;
                if (enterHoldTimer >= DebounceEnterSeconds)
                {
                    isFistActive = true;
                    fistJustStarted = true;
                    enterHoldTimer = 0f;
                    isPreFistCurling = false;
                    // Pertahankan latchedCursorLocalXY tepat 1 frame rising-edge ini agar bidikan tetap presisi
                }
            }
            else
            {
                enterHoldTimer = Mathf.Max(0f, enterHoldTimer - (dt * 2f));
            }
        }
        else
        {
            // Begitu sudah masuk mode Fist (setelah frame rising-edge), lepaskan latch agar seretan mulus 1:1
            hasLatchedCursor = false;
            isPreFistCurling = false;
            enterHoldTimer = 0f;

            // Saat menyeret kabel/gerbang (sticky hold), cukup buka santai (rasio > 1.15 & maks 1 jari tertekuk alami)
            bool exitMet = isStickyHoldActive
                ? (meanCurlRatio > FistStickyExitMeanRatio && foldedFingerCount <= 1)
                : rawExitCondition;

            if (exitMet)
            {
                // Debounce lepas adaptif kecepatan: 55 ms saat berhenti/melambat (< 1.5) hingga 115 ms saat gerak cepat (>= 2.5)
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
