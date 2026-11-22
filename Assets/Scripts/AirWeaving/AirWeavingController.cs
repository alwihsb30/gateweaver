using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using TMPro;
using Mediapipe;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Color = UnityEngine.Color;
using Landmark = Mediapipe.Tasks.Components.Containers.Landmark;
using NormalizedLandmark = Mediapipe.Tasks.Components.Containers.NormalizedLandmark;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>
/// Pengendali Mode Air-Weaving (GateWeaver AR v6.0 Tahap 1 + Tahap 2):
/// - Camera Handover bersih antara Vuforia Engine (v5) dan WebCamTexture (480x360@30fps) + MediaPipe HandLandmarker (CPU).
/// - Scale-Invariant Pinch Ratio (Landmark #4-#8 dibagi jarak telapak #0-#9) + Hysteresis + Occlusion Grace (0.16s).
/// - Meja Kerja Spasial 3D di depan ARCamera selaras skala alami GateNode & CircuitManager (tanpa ubah CircuitManager.cs):
///   * Input A, Input B, dan Output terkunci rapi sebagai terminal papan roti virtual.
///   * 5 Gerbang Logika (AND, OR, XOR, NAND, NOT) diparkir aman di Rak Parkir Atas (pratinjau 3D tanpa kabel/kuota)
///     dan dapat dicubit-seret (Air-Pinch & Drag) ke Zona Rakit tengah atau dikembalikan ke Rak Parkir.
///   * Air-Tap / Pinch singkat pada Input A & Input B menjungkit tuas sakelar fisik 3D (-38° <-> +38°) dengan cooldown 0.55s.
/// </summary>
public class AirWeavingController : MonoBehaviour
{
    private const string ModelFileName = "hand_landmarker.task";
    private const int RequestedCamWidth = 480;
    private const int RequestedCamHeight = 360;
    private const int RequestedCamFps = 30;
    private const int MaxInferenceLongEdge = 224;
    private const int LandmarkCount = 21;
    private const int WorkerShutdownJoinTimeoutMs = 1500;
    private const int MaxAsyncGpuReadbackErrorsBeforeFallback = 3;

    private const float VuforiaReleaseDelaySeconds = 0.45f;
    private const float WebCamReleaseDelaySeconds = 0.40f;
    private const float WebCamStartTimeoutSeconds = 4.0f;

    // Geometri Meja Hologram 3D Horizontal (bidang XZ lokal HologramStageRoot, +Y = tinggi melayang di atas meja) — Tahap 4.6 v8.0
    private const float VideoQuadDepth = 8.0f;
    private const float WorkbenchDepth = 3.82f;
    private const float DepthParallaxScale = 0.38f;
    private const float BaseHoverHeight = AirWeavingStageController.BaseHoverHeight; // +0.22m di atas permukaan meja
    private const float GrabExtraLiftHeight = AirWeavingStageController.GrabExtraLiftHeight; // +0.185m ekstra saat dikepal (~0.405m total, anti-fish-eye)
    private const float InputAHoverHeight = BaseHoverHeight + 0.050f;
    private const float InputBHoverHeight = BaseHoverHeight + 0.014f;
    private const float OutputHoverHeight = BaseHoverHeight + 0.032f;
    private const float ParkingRackHoverHeight = 0.340f;
    private const float ArenaArcHeightY = 0.028f;
    private const float ParkingRackArcDepthZ = 0.150f;
    private const float InputATerminalArcYawDeg = 22.0f;
    private const float InputBTerminalArcYawDeg = 13.0f;
    private const float OutputTerminalArcYawDeg = -18.0f;
    private const float ArenaMaxArcYawDeg = 12.5f;
    private const float ParkingMaxArcYawDeg = 16.0f;
    private const float NodeTableErgonomicPitchBiasDeg = -4.0f;

    // Parameter Monocular Z-Depth 3D Logaritmik + Baseline Adaptif & Mode Meja Catur 3D (Tahap 8 v8.0)
    private const float SingleHandNeutralHoverFrontOffset = 0.55f;
    private const float SingleHandDepthMetersPerLog = 1.65f;
    private const float FistGrabDepthGainMeters = 2.60f;
    private const float FistGrabDepthLogDeadzone = 0.016f;
    private const float MinGrabCameraDepth = 0.55f;
    private const float MaxGrabCameraDepth = 7.75f;
    private const float MinGrabTableLocalY = 0.14f;
    private const float MaxGrabTableLocalY = 1.65f;
    private const float MinHandRigCameraDepth = 0.85f;
    private const float HandNearClipMinOffsetMeters = 0.35f;
    private const float HandMaxBehindVideoQuadMarginMeters = 0.25f;
    private const float MinNearClipSafeOffsetMeters = 0.28f;

    // Parameter Kontrol 2 Tangan Full Free-View 6-DoF Focus-Point Orbit 360° + Ultra-Wide Zoom 0.18x..15x + True Z-Axis Push/Pull
    private const float TwoHandOrbitYawDegPerUnit = 98.0f;
    private const float TwoHandOrbitPitchDegPerUnit = 98.0f;
    private const float TwoHandOrbitMaxAxisDeltaPerFrameDeg = 16.0f;
    private const float TwoHandOrbitRollGain = 0.82f;
    private const float TwoHandOrbitRollEmaRate = 18.0f;
    private const float TwoHandOrbitRollMaxDeltaPerFrameDeg = 12.0f;
    private const float TwoHandRollMinSepDist = 0.16f;
    private const float TwoHandRollFullSepDist = 0.46f;
    private const float TwoHandMinPalmScale = AirWeavingStageController.MinSafePalmScale;
    private const float TwoHandAvgPalmOneEuroMinCutoff = 2.60f;
    private const float TwoHandAvgPalmOneEuroBeta = 0.45f;
    private const float TwoHandAvgPalmOneEuroDCutoff = 1.00f;
    private const float TwoHandAvgPalmOneEuroVelScale = 18.0f;
    private const float TwoHandZoomSensitivity = 1.55f;
    private const float TwoHandZoomDistEmaRate = 14.0f;
    private const float TwoHandZoomNormSpanSoftFloor = 1.25f;
    private const float TwoHandZoomNormSpanMinFloor = 0.50f;
    private const float TwoHandZoomNormSpanInitialFloor = 0.85f;
    private const float TwoHandZoomRatioMicroDeadzone = 0.025f;
    private const float TwoHandZoomMaxLogStepPerFrame = 0.115f;
    private const float TwoHandFreePanXYGain = 1.50f;
    private const float TwoHandFreePanMaxDeltaXYPerFrame = 0.38f;
    private const float TwoHandOrbitConfirmHoldSeconds = 0.040f;
    private const float TwoHandOrbitHoldMaxPinchRatio = 0.74f;
    private const float TwoHandFreePanHoldMinFistBendDeg = 78.0f;
    private const float TwoHandPinchReleaseFreezeRatio = 0.60f;
    private const float TwoHandFistReleaseFreezeMaxBendDeg = 92.0f;
    private const float TwoHandFreePanConfirmHoldSeconds = 0.055f;
    private const float TwoHandIntentGraceMaxSeconds = 0.220f;
    private const float TwoHandSingleHandUpgradeWindowSeconds = 0.190f;
    private const float TwoHandFistResetHoldSeconds = 0.55f;

    // Parameter Carry-Pan Tangan Kedua (simetris 1 Pinch + 1 Fist pasca > 190 ms) & Auto Edge-Pan 1 Tangan (Zero-GC)
    private const float CarryPanConfirmHoldSeconds = 0.055f;
    private const float CarryPanXYGain = TwoHandFreePanXYGain;
    private const float CarryPanMaxDeltaXYPerFrame = TwoHandFreePanMaxDeltaXYPerFrame;
    private const float CarryPanFistHoldMinBendDeg = 70.0f;
    private const float CarryPanFistReleaseFreezeMaxBendDeg = 76.0f;
    private const float CarryPanPalmOneEuroMinCutoff = 2.20f;
    private const float CarryPanPalmOneEuroBeta = 0.45f;
    private const float CarryPanPalmOneEuroDCutoff = 1.00f;
    private const float CarryPanPalmOneEuroVelScale = 16.0f;

    private const float EdgePanStripFraction = 0.12f;
    private const float EdgePanDwellSeconds = 0.095f;
    private const float EdgePanRampInSeconds = 0.110f;
    private const float EdgePanMaxSpeedAtRefDepth = 2.85f;
    private const float EdgePanCableReleasePauseRatio = 0.70f;

    // Parameter Quality Gate, Stabilisasi Dual-Hand, Exclusive Gesture Arbiter, & Confidence Jarak Jauh MediaPipe (Tahap 5.5+)
    private const float MediaPipeMinHandDetectionConfidence = 0.40f;
    private const float MediaPipeMinHandPresenceConfidence = 0.38f;
    private const float MediaPipeMinTrackingConfidence = 0.30f;
    private const float MinCandidateRawPalmScale = 0.028f;
    private const float HandednessConfidenceThreshold = 0.70f;
    private const float HandAssociationMaxDistNorm = 0.36f;
    private const float HandAssociationMatchedHandednessMaxDistNorm = 0.48f;
    private const float HandFrameDropGraceSeconds = 0.120f;
    private const float HandCarryFrameDropGraceSeconds = 0.200f;
    private const float CrossGestureNeutralCooldownSeconds = 0.120f;
    private const float CablePinchEnterMaxSpeed = 2.20f;
    private const float PinchBeginGraceSeconds = 0.140f;
    private const float GateGrabEnterMaxSpeed = 1.85f;
    private const float InputSwitchEnterMaxSpeed = 1.65f;

    // Critically Damped Spring-Damper analitik & Elastic Scale Pop (responsif gerak cepat Tahap 8, tetap berbobot & konsisten dt)
    private const float GateDragSpringOmega = 28.0f;
    private const float GateDragTargetDampRate = 34.0f;
    private const float GateLiftZSpringOmega = 19.0f;
    private const float GateScaleSpringOmega = 22.0f;
    private const float GateSettleSpringOmega = 22.0f;
    private const float GateGrabTiltSpringOmega = 15.0f;
    private const float DragVelocityTiltDegPerUnitSec = 13.5f;
    private const float GateGrabPopAmplitude = 0.10f;
    private const float GatePlaceBloomPopAmplitude = 0.12f;
    private const float InputTogglePopAmplitude = 0.12f;
    private const float GateThrowMaxSpeed = 4.60f;
    private const float GateCoastDamping = 6.5f;
    private const float GateCoastStopSpeed = 0.025f;
    private const float GateCoastEdgeBounceFactor = 0.30f;
    private const float GateDragVelocitySmoothRate = 18.0f;

    // Soft Magnetic Aim-Assist kalem & Konfirmasi Kepal (Fist) 70 ms untuk sakelar Input A/B (Tahap 9: Edge-Trigger Aman)
    private const float InputSwitchMagnetRadius = 0.28f;
    private const float InputSwitchMagnetMaxPull = 0.24f;
    private const float SoftMagnetMaxHandSpeed = InputSwitchEnterMaxSpeed;
    private const float SoftMagnetOffsetBlendSpeed = 18.0f;
    private const float InputFistConfirmSeconds = 0.070f;

    // Magnetic Grid Snap + Auto-Separate Zero-GC (Tahap 9: spiral search berjarak aman port kiri-kanan)
    private const float AssemblyGridBaseStepX = 0.46f;
    private const float AssemblyGridStepZ = 0.46f;
    private const float AutoSeparateGateMinBaseDx = 0.72f;
    private const float AutoSeparateGateMinDz = 0.50f;
    private const float AutoSeparateTerminalMinBaseDx = 0.72f;
    private const float AutoSeparateTerminalMinDz = 0.56f;
    private const int AutoSeparateMaxSpiralRing = 4;

    // Adaptive Inference Idle (~10 Hz saat > 1 dtk tanpa tangan, pulih instan saat tangan masuk)
    private const float AdaptiveIdleThresholdSeconds = 1.00f;
    private const float AdaptiveIdleInferenceIntervalSeconds = 0.100f;
    private const float SummonTerminalBlockRadius = 0.42f;

    // Batas dt sampel untuk One-Euro Filter 3D (filter berjalan bersih di AirWeavingHandGestureDetector & AirWeavingHologramVisual)
    private const float OneEuroMinDtSeconds = 0.005f;
    private const float OneEuroMaxDtSeconds = 0.200f;
    private const float TwoPi = 6.283185307f;

    // Skala hologram melayang Mode Udara Tahap 4.6 (ramping & anti-fish-eye saat meja di-orbit 360°)
    private const float TerminalScale = 0.68f;
    private const float OutputTerminalScale = 0.70f;
    private const float ActiveGateScale = 0.68f;
    private const float GrabbedGateScale = 0.72f;
    private const float ParkedGateScale = 0.44f;

    // Parameter kompatibilitas Pinch lama (disimpan untuk fallback/referensi)
    private const float PinchRatioActivateThreshold = 0.56f;
    private const float PinchRatioReleaseThreshold = 0.70f;

    // Radius akses lega + Screen-Space Minimum Hitbox Floor (Fist badan gerbang & sakelar Input A/B)
    private const float MinScreenSpaceHitboxScale = 0.82f;
    private const float GateHoverGrabRadius = 0.220f;
    private const float ParkedGateHoverRadius = 0.245f;
    private const float InputAirTapRadius = 0.225f;
    private const float InputAirTapRearmRadius = InputAirTapRadius * 1.35f;
    private const float InputAirTapCooldownSeconds = 0.38f;
    private const float InputTouchRippleDuration = 0.34f;
    private const float ParkReturnThresholdY = 1.06f;
    private const float ParkSlotSnapRadius = 0.38f;
    private const float HandFrameStaleTimeoutSeconds = 0.35f;

    // Efek melayang 3D nyata (sinusoidal bob ±0.014, gentle gimbal sway ±3.5°, 2-axis velocity banking tilt maks ±24°)
    private const float FloatBobAmplitude = 0.014f;
    private const float FloatBobSpeed = 1.85f;
    private const float GimbalSwayPitchDeg = 2.6f;
    private const float GimbalSwayYawDeg = 3.5f;
    private const float GimbalSwayRollDeg = 2.4f;
    private const float MaxDragInertiaTiltDeg = 24.0f;

    // Flag pencadangan Air-Draw lama (nonaktif di Fase 3; UnistrokeGestureRecognizer.cs tetap utuh untuk Fase 4)
    private static readonly bool EnableLegacyAirDraw = false;

    // Parameter $1 Unistroke Air-Draw Spawner (Tahap 3)
    private const int MaxAirDrawPoints = 128;
    private const int AirDrawMinPoints = 10;
    private const float AirDrawMinPointSpacing = 0.026f;
    private const float AirDrawMinSpan = 0.22f;
    private const float AirDrawConfidenceThreshold = 0.72f;
    private const float AirDrawDwellCompleteSeconds = 0.38f;
    private const float AirDrawDwellMoveTolerance = 0.032f;
    private const float AirDrawFadeDurationSeconds = 0.35f;
    private const float AirDrawPostSpawnCooldownSeconds = 0.55f;

    // Koordinat meja hologram horizontal XZ 2.0x yang dirapatkan (Tahap 9: mengurangi white space kiri Input & kanan Output)
    // (X = kiri-kanan, Y pada konstanta di bawah = kedalaman Z depan-belakang meja)
    private const float InputTerminalBaseX = -2.16f;
    private const float InputATerminalY = 0.28f;
    private const float InputBTerminalY = -1.02f;
    private const float OutputTerminalBaseX = 2.20f;
    private const float OutputTerminalY = -0.34f;
    private const float ParkingRackY = 1.56f;
    private const float AssemblyMinBaseX = -1.40f;
    private const float AssemblyMaxBaseX = 1.44f;
    private const float AssemblyMinY = -1.44f;
    private const float AssemblyMaxY = 0.88f;

    private static readonly float[] ParkingSlotBaseX = { -1.44f, -0.72f, 0.00f, 0.72f, 1.44f };
    private static readonly NodeType[] ParkingSlotOrder =
    {
        NodeType.AND,
        NodeType.OR,
        NodeType.XOR,
        NodeType.NAND,
        NodeType.NOT
    };

    // Kemiringan dasar GateNode di atas meja horizontal XZ (sejajar bidang meja XZ + sedikit bias ergonomis -4° ke kamera)
    private static readonly Quaternion WorkbenchTiltRotation = Quaternion.Euler(-38f, 0f, 0f);
    private static readonly Quaternion NodeBaseTableRotation = Quaternion.Euler(NodeTableErgonomicPitchBiasDeg, 0f, 0f);

    private const float PostDropOutputLockoutSeconds = 0.65f;
    private const float OutputZonePriorityRadius = 0.30f;
    private const float NoHandPromptDelaySeconds = 5.0f;
    private const int PinchRingSegments = 20;
    private const int HoverRingSegments = 28;

    // 21 sambungan tulang standar resmi MediaPipe HandLandmarker
    private static readonly int[,] HandBoneConnections =
    {
        { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 4 },
        { 0, 5 }, { 5, 6 }, { 6, 7 }, { 7, 8 },
        { 9, 10 }, { 10, 11 }, { 11, 12 },
        { 13, 14 }, { 14, 15 }, { 15, 16 },
        { 0, 17 }, { 17, 18 }, { 18, 19 }, { 19, 20 },
        { 5, 9 }, { 9, 13 }, { 13, 17 }
    };

    // Skeleton tangan dibuat 65-70% lebih halus & rendah opasitas agar tidak menumpuk di atas label/hologram
    private static readonly Color NeonCyanBoneColor = new Color(0.20f, 0.88f, 1.00f, 0.16f);
    private static readonly Color NeonJointBaseColor = new Color(0.28f, 0.90f, 1.00f, 0.18f);
    private static readonly Color NeonJointTipColor = new Color(0.80f, 0.98f, 1.00f, 0.24f);
    private static readonly Color PinchActiveColor = new Color(0.76f, 1.00f, 0.28f, 1.00f);
    private static readonly Color PinchIdleColor = new Color(0.20f, 0.92f, 1.00f, 0.85f);
    private static readonly Color HoverGateRingColor = new Color(0.28f, 0.98f, 1.00f, 0.92f);
    private static readonly Color HoverGrabbedRingColor = new Color(1.00f, 0.88f, 0.26f, 0.96f);
    private static readonly Color HoverReturnRackColor = new Color(1.00f, 0.56f, 0.26f, 0.95f);
    private static readonly Color HoverInputRingColor = new Color(0.42f, 1.00f, 0.68f, 0.92f);
    private static readonly Color AirWeavingLabelPillColor = new Color(0.02f, 0.05f, 0.11f, 0.94f);
    private static readonly Color FeedbackActiveColor = new Color(0.35f, 0.96f, 1.00f, 1.00f);
    private static readonly Color FeedbackPinchColor = new Color(0.76f, 1.00f, 0.30f, 1.00f);
    private static readonly Color FeedbackWaitingColor = new Color(1.00f, 0.82f, 0.34f, 1.00f);
    private static readonly Color AirDrawStrokeStartColor = new Color(0.28f, 0.98f, 1.00f, 0.96f);
    private static readonly Color AirDrawStrokeEndColor = new Color(0.74f, 0.42f, 1.00f, 0.96f);
    private static readonly Color AirDrawSuccessFlashColor = new Color(0.68f, 1.00f, 0.32f, 1.00f);
    private static readonly Color AirDrawFailFlashColor = new Color(1.00f, 0.38f, 0.32f, 0.88f);

    private static byte[] cachedModelBytes;
    private static bool protobufLogHandlerActive = false;

    private struct ElasticScalePop
    {
        private const float DecayRate = 9.0f;
        private const float FrequencyHz = 4.2f;
        private const float MaxDurationSeconds = 0.75f;

        private float amplitude;
        private float elapsed;
        private bool active;

        public void Trigger(float popAmplitude)
        {
            amplitude = popAmplitude;
            elapsed = 0f;
            active = true;
        }

        public void Reset()
        {
            amplitude = 0f;
            elapsed = 0f;
            active = false;
        }

        public float Evaluate(float dt)
        {
            if (!active)
            {
                return 1f;
            }

            if (dt > 0f)
            {
                elapsed += dt;
            }

            if (elapsed >= MaxDurationSeconds)
            {
                active = false;
                return 1f;
            }

            return 1f + (amplitude * Mathf.Exp(-DecayRate * elapsed) * Mathf.Sin(TwoPi * FrequencyHz * elapsed));
        }
    }

    private static float StepCriticallyDampedSpring(float current, float target, ref float velocity, float omega, float dt)
    {
        if (dt <= 0.00001f)
        {
            return current;
        }

        float delta = current - target;
        float temp = (velocity + (omega * delta)) * dt;
        float exp = Mathf.Exp(-omega * dt);
        velocity = (velocity - (temp * omega)) * exp;
        return target + ((delta + temp) * exp);
    }

    private static Vector2 StepCriticallyDampedSpring2D(Vector2 current, Vector2 target, ref Vector2 velocity, float omega, float dt)
    {
        if (dt <= 0.00001f)
        {
            return current;
        }

        Vector2 delta = current - target;
        Vector2 temp = (velocity + (delta * omega)) * dt;
        float exp = Mathf.Exp(-omega * dt);
        velocity = (velocity - (temp * omega)) * exp;
        return target + ((delta + temp) * exp);
    }

    private sealed class WorkbenchGateSlot
    {
        public GateNode node;
        public int slotIndex;
        public bool isPlacedOnWorkbench;
        public Vector2 placedLocalXY;
        public Vector2 currentLocalXY;
        public float currentLocalZ;
        public float currentScale;
        public Vector2 velocityXY;
        public float velocityZ;
        public float velocityScale;
        public Vector2 smoothedVelocityXY;
        public Vector2 coastVelocityXY;
        public bool isCoasting;
        public Vector2 currentInertiaTiltDeg;
        public Vector2 velocityTiltDeg;
        public Vector2 smoothedDragTargetXZ;
        public float smoothedDragTargetY;
        public ElasticScalePop scalePop;

        public void ResetSpringState()
        {
            velocityXY = Vector2.zero;
            velocityZ = 0f;
            velocityScale = 0f;
            smoothedVelocityXY = Vector2.zero;
            coastVelocityXY = Vector2.zero;
            isCoasting = false;
            currentInertiaTiltDeg = Vector2.zero;
            velocityTiltDeg = Vector2.zero;
            smoothedDragTargetXZ = currentLocalXY;
            smoothedDragTargetY = currentLocalZ;
            scalePop.Reset();
        }
    }

    private LevelAndUIManager levelManager;
    private CircuitManager circuitManager;
    private GateNode inputANode;
    private GateNode inputBNode;
    private GateNode outputNode;
    private TMP_Text txtFeedback;

    private Camera cachedMainCamera;
    private Behaviour cachedVuforiaBehaviour;
    private CameraClearFlags savedCameraClearFlags = CameraClearFlags.SolidColor;
    private Color savedCameraBgColor = Color.black;
    private bool hasSavedCameraState = false;
    private bool hasSavedFramePacing = false;
    private int savedVSyncCount = 1;
    private int savedTargetFrameRate = 60;
    private bool hasSavedPhysicsSimulationMode = false;
    private SimulationMode savedPhysicsSimulationMode = SimulationMode.FixedUpdate;

    private bool isAirWeavingActive = false;
    private bool isTransitioning = false;
    private bool isHandDetected = false;
    private bool wasHandDetected = false;
    private bool isSecondaryHandDetected = false;
    private bool wasSecondaryHandDetected = false;
    private bool rawPinching = false;
    private bool isPinching = false;
    private bool wasPinching = false;
    private bool mirrorHorizontalDisplay = true;

    private float currentPinchRatio = 1f;
    private float inputATapCooldown = 0f;
    private float inputBTapCooldown = 0f;
    private readonly float[] inputADwellTimers = new float[2];
    private readonly float[] inputBDwellTimers = new float[2];
    private readonly bool[] inputARequiresExitRearmBySlot = new bool[2];
    private readonly bool[] inputBRequiresExitRearmBySlot = new bool[2];
    private readonly bool[] inputAReadyForFistBySlot = new bool[2];
    private readonly bool[] inputBReadyForFistBySlot = new bool[2];
    private readonly GateNode[] inputPushHoverTargetBySlot = new GateNode[2];
    private readonly float[] inputPushProgressBySlot = new float[2];
    private float inputATouchRippleTimer = 0f;
    private float inputBTouchRippleTimer = 0f;
    private readonly Vector2[] inputSwitchMagnetOffsetBySlot = new Vector2[2];
    private ElasticScalePop inputAScalePop;
    private ElasticScalePop inputBScalePop;

    private Vector3 pinchWorldPosition = Vector3.zero;
    private Vector3 indexTipWorldPosition = Vector3.zero;
    private Vector2 pinchWorkbenchLocalXY = Vector2.zero;
    private Vector2 indexWorkbenchLocalXY = Vector2.zero;
    private enum ActiveInteractionMode
    {
        Idle = 0,
        GateGrab = 1,
        CableDrag = 2,
        InputSwitch = 3,
        CircleSummon = 4,
        TwoHandOrbitZoom = 5,
        TwoHandFreePan = 6,
        TwoHandReset = 7,
        OutputHold = 8
    }

    private enum TrackedHandedness
    {
        Unknown = 0,
        Left = 1,
        Right = 2
    }

    private readonly Vector3[] rawLandmarksWorld = new Vector3[LandmarkCount];
    private readonly Vector3[] secondaryRawLandmarksWorld = new Vector3[LandmarkCount];
    private long lastPrimaryFilterTimestampMs = 0;
    private long lastSecondaryFilterTimestampMs = 0;
    private bool primarySlotActive = false;
    private bool primaryMissedLastInference = false;
    private float primaryMissingTimer = 0f;
    private TrackedHandedness primarySlotHandedness = TrackedHandedness.Unknown;
    private Vector2 lastPrimaryPalmNormXY = new Vector2(0.5f, 0.5f);
    private bool secondarySlotActive = false;
    private bool secondaryMissedLastInference = false;
    private float secondaryMissingTimer = 0f;
    private TrackedHandedness secondarySlotHandedness = TrackedHandedness.Unknown;
    private Vector2 lastSecondaryPalmNormXY = new Vector2(0.5f, 0.5f);
    private readonly Vector3[] pinchRingPoints = new Vector3[PinchRingSegments];
    private readonly Vector3[] hoverRingPoints = new Vector3[HoverRingSegments];

    // State Exclusive Gesture Arbiter (Ambidextrous: activeInteractionHandSlot 0=primer, 1=sekunder, -1=bebas) + Kontrol 2 Tangan Global 6-DoF
    private ActiveInteractionMode activeInteractionMode = ActiveInteractionMode.Idle;
    private int activeInteractionHandSlot = -1;
    private int activeCableHoverHandSlot = -1;
    private float neutralCooldownTimer = 0f;
    private readonly float[] pinchBeginGraceTimers = new float[2];
    private float singleHandModeEnteredTime = -10f;
    private float twoHandOrbitConfirmTimer = 0f;
    private float twoHandFreePanConfirmTimer = 0f;
    private float twoHandIntentPendingTimer = 0f;
    private bool isTwoHandConfirming = false;
    private bool twoHandFreePanPrimaryIsPinch = true;
    private bool twoHandFistResetLatched = false;
    private float twoHandFistResetTimer = 0f;
    private float twoHandPinchInitialNormSpan = 10.0f;
    private float twoHandPinchSmoothedNormSpan = 10.0f;
    private float twoHandPinchInitialZoom = AirWeavingStageController.DefaultZoomScale;
    private Vector2 twoHandPinchPrevMidpointXY = Vector2.zero;
    private float twoHandPinchSmoothedLineAngleDeg = 0f;
    private bool wasTwoHandOrbitReleaseFrozen = false;
    private Vector2 twoHandPanPrevMidpointXY = Vector2.zero;
    private bool wasTwoHandPanReleaseFrozen = false;
    private OneEuroFilter1D twoHandAvgPalmScaleFilter;

    // Sub-state Carry-Pan Tangan Kedua (ortogonal terhadap CableDrag/GateGrab agar 0% mengganggu sticky lock Tangan 1)
    private bool isCarryPanActive = false;
    private int carryPanHandSlot = -1;
    private bool carryPanUsesFist = true;
    private float carryPanConfirmTimer = 0f;
    private Vector2 carryPanPrevHandXY = Vector2.zero;
    private bool wasCarryPanReleaseFrozen = false;
    private bool wasCarryPanOcclusionPaused = false;
    private OneEuroFilter1D carryPanPalmScaleFilter;

    // State Auto Edge-Pan 1 Tangan (aktif hanya saat CableDrag/GateGrab stabil & Carry-Pan tidak aktif)
    private bool isAutoEdgePanActive = false;
    private float edgePanDwellTimer = 0f;

    // Kompensasi kedalaman GateGrab saat panggung digeser X/Y/Z oleh Carry-Pan / Auto Edge-Pan
    private float grabStageDepthCompMeters = 0f;
    private Vector3 lastGrabStagePanOffset = Vector3.zero;
    private bool hasLastGrabStagePanOffset = false;

    private float grabStartPalmSpan = AirWeavingHandGestureDetector.DefaultComfortPalmSpan;
    private float grabStartCameraDepth = WorkbenchDepth;
    private bool preGrabWasPlaced = false;
    private Vector2 preGrabPlacedLocalXY = Vector2.zero;
    private Vector2 preGrabCurrentLocalXY = Vector2.zero;
    private float preGrabLocalZ = BaseHoverHeight;
    private float preGrabScale = ActiveGateScale;

    private bool IsTwoHandOrbitZoomActive => activeInteractionMode == ActiveInteractionMode.TwoHandOrbitZoom;
    private bool IsTwoHandFreePanActive => activeInteractionMode == ActiveInteractionMode.TwoHandFreePan;
    private bool IsTwoHandFistResetCharging => activeInteractionMode == ActiveInteractionMode.TwoHandReset && !twoHandFistResetLatched;
    private bool IsAnyTwoHandModeActive => activeInteractionMode == ActiveInteractionMode.TwoHandOrbitZoom
                                           || activeInteractionMode == ActiveInteractionMode.TwoHandFreePan
                                           || activeInteractionMode == ActiveInteractionMode.TwoHandReset;

    private readonly List<WorkbenchGateSlot> gateSlots = new List<WorkbenchGateSlot>(5);
    private int grabbedGateSlotIndex = -1;
    private int hoveredGateSlotIndex = -1;
    private int secondaryHoveredGateSlotIndex = -1;
    private GateNode hoveredInputNode = null;
    private GateNode secondaryHoveredInputNode = null;

    // $1 Unistroke Air-Draw Spawner (semua buffer pra-alokasi zero-GC)
    private readonly UnistrokeGestureRecognizer gestureRecognizer = new UnistrokeGestureRecognizer();
    private readonly List<Vector2> airDrawPointsLocal = new List<Vector2>(MaxAirDrawPoints);
    private readonly Vector3[] airDrawWorldBuffer = new Vector3[MaxAirDrawPoints];
    private bool isIndexOnlyWandPose = false;
    private bool isRecordingAirDraw = false;
    private float airDrawDwellTimer = 0f;
    private Vector2 airDrawDwellAnchorLocal = Vector2.zero;
    private float airDrawFadeTimer = 0f;
    private bool airDrawFadeWasSuccess = false;
    private float airDrawPostSpawnCooldown = 0f;
    private float postDropLockoutTimer = 0f;
    private float noHandAbsentTimer = 0f;
    private bool noHandToastShown = false;

    private sealed class WorkerHandRecord
    {
        public readonly List<NormalizedLandmark> landmarks = new List<NormalizedLandmark>(LandmarkCount);
        public readonly List<Landmark> worldLandmarks = new List<Landmark>(LandmarkCount);
        public bool hasWorldLandmarks;
        public TrackedHandedness handedness;
    }

    private sealed class WorkerInferenceSnapshot
    {
        public readonly WorkerHandRecord[] hands = { new WorkerHandRecord(), new WorkerHandRecord() };
        public int validHandCount;
        public long timestampMs;
        public string errorMessage;
    }

    private Coroutine transitionCoroutine;
    private WebCamTexture webCamTexture;
    private Color32[] rawWebcamPixels;
    private int lastCamWidth = 0;
    private int lastCamHeight = 0;

    // Jalur GPU Downscale (ARGB32/RGBA32) + AsyncGPUReadback non-blocking + Fallback GetPixels32 + Worker Thread MediaPipe Zero-GC
    private RenderTexture inferenceRenderTexture;
    private bool useAsyncGpuReadback = true;
    private int asyncGpuReadbackErrorCount = 0;
    private bool gpuReadbackInFlight = false;
    private AsyncGPUReadbackRequest gpuReadbackRequest;
    private int gpuReadbackSrcW = 0;
    private int gpuReadbackSrcH = 0;
    private int gpuReadbackDstW = 0;
    private int gpuReadbackDstH = 0;
    private int gpuReadbackRotAngle = 0;
    private bool gpuReadbackEffectiveVMirror = false;
    private bool gpuReadbackHMirror = false;
    private bool gpuReadbackUseTwoTap = false;

    // 3 slot RGBA32 terpisah ketat: gpuWriteSlotIdx (GPU/Main) | pendingSlotIdx (mailbox) | workerReadSlotIdx (Worker)
    private readonly NativeArray<byte>[] inputRgbaSlots = new NativeArray<byte>[3];
    private int inputSlotWidth = 0;
    private int inputSlotHeight = 0;
    private int gpuWriteSlotIdx = 0;
    private int pendingSlotIdx = 1;
    private int workerReadSlotIdx = 2;

    // Buffer Srgb 3-byte eksklusif Worker Thread (dialokasi & didispose di Main Thread hanya saat worker idle/berhenti)
    private NativeArray<byte> workerRgbPixels;
    private int workerRgbWidth = 0;
    private int workerRgbHeight = 0;

    private readonly object workerFrameLock = new object();
    private bool hasPendingWorkerFrame = false;
    private bool workerIsProcessingFrame = false;
    private int pendingSrcW = 0;
    private int pendingSrcH = 0;
    private int pendingDstW = 0;
    private int pendingDstH = 0;
    private int pendingRotAngle = 0;
    private bool pendingEffectiveVMirror = false;
    private bool pendingHMirror = false;
    private bool pendingUseTwoTap = false;

    private readonly object workerResultLock = new object();
    private readonly WorkerInferenceSnapshot[] resultSlots =
    {
        new WorkerInferenceSnapshot(),
        new WorkerInferenceSnapshot(),
        new WorkerInferenceSnapshot()
    };
    private int workerWriteResultIdx = 0;
    private int latestResultIdx = 1;
    private int mainReadResultIdx = 2;
    private int latestResultSequence = 0;
    private int consumedResultSequence = 0;

    private Thread inferenceWorkerThread;
    private AutoResetEvent workerWakeEvent;
    private volatile bool workerStopRequested = false;
    private readonly Stopwatch inferenceStopwatch = new Stopwatch();
    private long lastWorkerTimestampMs = 0;

    // Pre-computed 1D LUT untuk konversi RGBA->RGB + rotasi/mirror di Worker Thread (hanya dihitung ulang bila resolusi/rotasi/mirror berubah)
    private int[] downscaleLutX0;
    private int[] downscaleLutX1;
    private int[] downscaleLutY0;
    private int[] downscaleLutY1;
    private int lutSrcW = 0;
    private int lutSrcH = 0;
    private int lutDstW = 0;
    private int lutDstH = 0;
    private int lutRotAngle = -1;
    private bool lutVMirror = false;
    private bool lutHMirror = false;

    private HandLandmarker handLandmarker;
    private HandLandmarkerResult cachedResult;
    private readonly ImageProcessingOptions zeroRotationOptions = new ImageProcessingOptions(rotationDegrees: 0);

    // Bidang video kamera depan, panduan meja kerja, & rig 3D tangan neon (semua pra-alokasi, tanpa Collider, tanpa shadow)
    private GameObject videoQuadRootObj;
    private Transform videoQuadMirrorTransform;
    private Transform videoQuadMeshTransform;
    private Renderer videoQuadRenderer;
    private Material videoQuadMaterial;
    private float currentVideoDisplayWidth = 1f;
    private float currentVideoDisplayHeight = 1f;
    private int lastVideoQuadCamW = -1;
    private int lastVideoQuadCamH = -1;
    private int lastVideoQuadRotAngle = -1;
    private bool lastVideoQuadVMirror = false;
    private bool lastVideoQuadHMirror = false;
    private float lastVideoQuadFov = -1f;
    private float lastVideoQuadAspect = -1f;

    private Mesh sharedJointSphereMesh;
    private GameObject handRigRootObj;
    private GameObject[] jointSphereObjs;
    private LineRenderer[] boneLineRenderers;
    private GameObject secondaryHandRigRootObj;
    private GameObject[] secondaryJointSphereObjs;
    private LineRenderer[] secondaryBoneLineRenderers;
    private GameObject indexTrailObj;
    private TrailRenderer indexTrailRenderer;
    private LineRenderer pinchCursorRingRenderer;

    private GameObject workbenchVisualRootObj;
    private LineRenderer hoverHighlightRingRenderer;
    private LineRenderer airDrawStrokeRenderer;

    private readonly List<Material> ownedMaterials = new List<Material>(20);
    private Material jointBaseMat;
    private Material jointTipMat;
    private Material boneMat;
    private Material secondaryJointBaseMat;
    private Material secondaryJointTipMat;
    private Material secondaryBoneMat;
    private Material trailMat;
    private Material pinchRingMat;
    private Material hoverRingMat;
    private Material airDrawStrokeMat;

    private struct SavedNodeLabelStyle
    {
        public GateNode node;
        public Vector3 originalLocalPos;
        public Vector3 originalLocalScale;
        public MeshRenderer labelMeshRenderer;
        public int originalSortingOrder;
        public Material backdropMat;
        public Color originalBackdropColor;
        public int originalBackdropQueue;
        public Material pipMat;
        public int originalPipQueue;
    }

    private readonly List<SavedNodeLabelStyle> savedNodeLabelStyles = new List<SavedNodeLabelStyle>(12);
    private Transform cachedOutputTruthPodRootTransform;
    private Vector3 lastTruthPodWorldAnchor = Vector3.zero;

    private AirWeavingStageController stageController;
    private AirWeavingLightingAndShaderRig lightingAndShaderRig;
    private AirWeavingFloatingPropRig floatingPropRig;
    private AirWeavingOutputTrigger outputTrigger;
    private AirWeavingHologramVisual hologramVisual;
    private AirWeavingCableRouter cableRouter;
    private AirWeavingSummonVisual summonVisual;
    private AirWeavingSpatialSfx spatialSfx;
    private AirWeavingGestureGuide gestureGuide;
    private CompactHudMenuController cachedHudMenu;
    private CompactHudMenuController subscribedHudMenu;
    private readonly AirWeavingHandGestureDetector handGestureDetector = new AirWeavingHandGestureDetector();
    private readonly AirWeavingHandGestureDetector secondaryHandGestureDetector = new AirWeavingHandGestureDetector();
    private readonly AirWeavingCircleSummoner[] circleSummoners =
    {
        new AirWeavingCircleSummoner(),
        new AirWeavingCircleSummoner()
    };
    private readonly List<GateNode> activePlacedGatesBuffer = new List<GateNode>(5);
    private readonly AirWeavingStageController.ContactNodeDescriptor[] stageContactNodesBuffer = new AirWeavingStageController.ContactNodeDescriptor[8];
    private float fpsTimer = 0f;
    private float timeSinceLastValidHand = 0f;
    private float idleNoHandElapsedSeconds = 0f;
    private float lastInferenceDispatchUnscaledTime = -10f;
    private int inferenceFramesInWindow = 0;
    private int displayedInferenceFps = 0;

    // Cermin ringan status 4 sel Pod LED & edge-tracking SFX spasial Mode Udara (zero-GC, 0 diff pada LevelAndUIManager)
    private int debugPassedRowMask = 0;
    private int debugFailedRowMask = 0;
    private int lastDebugLevelIndex = -1;
    private int lastDebugActiveGates = -1;
    private int lastDebugWireCount = -1;
    private int lastDebugTopologyVersion = -1;
    private bool lastDebugCircuitComplete = false;
    private int lastEvaluatedCircuitTopologyVersion = -1;
    private bool isLiveCircuitPassedAllFour = false;
    private bool hasCachedTopologyTruthEval = false;
    private bool cachedTopologyMatchesTruthTable = false;
    private readonly List<GateNode> truthEvalVisited = new List<GateNode>(10);
    private enum HoverSfxCategory
    {
        None = 0,
        PortOrb = 1,
        GateBody = 2,
        InputSwitch = 3
    }

    private const float HoverNoneLeaveGraceSeconds = 0.09f;
    private const float HoverPostActionSuppressSeconds = 0.14f;
    private const float HologramWakeMinAbsentSeconds = 1.20f;

    private AirWeavingCircleSummoner.SummonStage lastSfxSummonStage = AirWeavingCircleSummoner.SummonStage.Idle;
    private NodeType lastSfxSummonGateType = NodeType.AND;
    private int lastSfxSummonChargeStep = 0;
    private Vector3 lastSfxSummonWorldPos = Vector3.zero;
    private int lastSfxPassedRowMask = 0;
    private int lastSfxFailedRowMask = 0;
    private OutputDebugGuidanceState lastSfxGuidanceState = OutputDebugGuidanceState.ConnectToOutput;
    private HoverSfxCategory lastHoverSfxCategory = HoverSfxCategory.None;
    private GateNode lastHoverSfxNode = null;
    private int lastHoverSfxSubId = 0;
    private float hoverSfxNoneGraceTimer = 0f;
    private float hoverSfxPostActionSuppressTimer = 0f;
    private bool wasGrabbedGateInDeleteZone = false;
    private int lastOutputHoldSfxStep = 0;
    private float handAbsentForWakeSeconds = 0f;
    private bool wasHandDetectedForWakeSfx = false;
    private readonly List<GateNode> cachedGateNodes = new List<GateNode>(12);
    private readonly List<Behaviour> pausedObserverHandlers = new List<Behaviour>(12);

    public bool IsAirWeavingActive => isAirWeavingActive;
    public bool IsTransitioning => isTransitioning;
    public bool IsHandDetected => isHandDetected || isSecondaryHandDetected;
    public bool IsPinching => isPinching;
    public int DisplayedInferenceFps => displayedInferenceFps;
    public Vector3 PinchWorldPosition => pinchWorldPosition;
    public Vector3 IndexTipWorldPosition => indexTipWorldPosition;
    private bool IsAnyHandDetected => isHandDetected || isSecondaryHandDetected;
    private bool IsAnyCircleSummonActive => circleSummoners[0].IsActive || circleSummoners[1].IsActive;
    private bool IsAnyUiOverlayOpen => (levelManager != null && levelManager.IsUiOverlayOpen)
                                       || (gestureGuide != null && gestureGuide.IsGuideOverlayOpen);

    private bool IsHandSlotDetected(int handSlot)
    {
        return handSlot == 0 ? isHandDetected : (handSlot == 1 && isSecondaryHandDetected);
    }

    private AirWeavingHandGestureDetector GetHandDetector(int handSlot)
    {
        return handSlot == 1 ? secondaryHandGestureDetector : handGestureDetector;
    }

    private Vector3 GetHandIndexTipWorldPosition(int handSlot)
    {
        return GetHandDetector(handSlot).IndexTipWorldPosition;
    }

    private void ResetAllCircleSummoners()
    {
        circleSummoners[0].Reset();
        circleSummoners[1].Reset();
    }

    private AirWeavingCircleSummoner GetActiveOrPreviewCircleSummoner()
    {
        if (activeInteractionMode == ActiveInteractionMode.CircleSummon
            && (activeInteractionHandSlot == 0 || activeInteractionHandSlot == 1))
        {
            return circleSummoners[activeInteractionHandSlot];
        }
        if (circleSummoners[0].IsActive) return circleSummoners[0];
        if (circleSummoners[1].IsActive) return circleSummoners[1];
        if (circleSummoners[0].HasLivePoseIndicator) return circleSummoners[0];
        if (circleSummoners[1].HasLivePoseIndicator) return circleSummoners[1];
        return circleSummoners[0];
    }

    public event Action<bool> OnAirWeavingModeChanged;
    public event Action OnOutputVerifyRequested;
    public event Action OnOutputNextLevelRequested;

    /// <summary>
    /// Dipanggil otomatis oleh LevelAndUIManager saat Start() (zero manual Inspector wiring).
    /// </summary>
    public void Configure(
        LevelAndUIManager manager,
        CircuitManager circuit,
        GateNode inputA,
        GateNode inputB,
        GateNode output,
        TMP_Text feedbackText)
    {
        levelManager = manager;
        circuitManager = circuit;
        inputANode = inputA;
        inputBNode = inputB;
        outputNode = output;
        txtFeedback = feedbackText;
        cachedHudMenu = levelManager != null ? levelManager.GetComponent<CompactHudMenuController>() : null;
        BindHudMenuEvents(cachedHudMenu);
        if (gestureGuide != null)
        {
            gestureGuide.Configure(cachedHudMenu, spatialSfx, ResolveMainCamera(), txtFeedback != null ? txtFeedback.font : null);
        }
    }

    private void BindHudMenuEvents(CompactHudMenuController hudMenu)
    {
        if (subscribedHudMenu == hudMenu)
        {
            return;
        }

        UnbindHudMenuEvents();
        subscribedHudMenu = hudMenu;
        if (subscribedHudMenu != null)
        {
            subscribedHudMenu.OnStageRecenterRequested += HandleHudStageRecenterRequested;
        }
    }

    private void UnbindHudMenuEvents()
    {
        if (subscribedHudMenu != null)
        {
            subscribedHudMenu.OnStageRecenterRequested -= HandleHudStageRecenterRequested;
            subscribedHudMenu = null;
        }
    }

    private void HandleHudStageRecenterRequested()
    {
        if (!isAirWeavingActive || isTransitioning || stageController == null)
        {
            return;
        }

        stageController.ResetStageView(immediate: false);
        Camera mainCam = ResolveMainCamera();
        if (spatialSfx != null && mainCam != null)
        {
            spatialSfx.PlayOutputHoldConfirm(mainCam.transform.position + (mainCam.transform.forward * WorkbenchDepth));
        }
        SetFeedbackImmediate("Tampilan meja dipusatkan.", FeedbackActiveColor, 1.5f);
    }

    public void ToggleAirWeavingMode()
    {
        if (isTransitioning)
        {
            return;
        }

        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }

        if (isAirWeavingActive)
        {
            transitionCoroutine = StartCoroutine(ExitAirWeavingRoutine());
        }
        else
        {
            transitionCoroutine = StartCoroutine(EnterAirWeavingRoutine());
        }
    }

    /// <summary>
    /// Mengembalikan seluruh 5 gerbang logika ke Rak Parkir Atas dan mempertahankan Input A, Input B, serta Output
    /// tetap aktif di papan roti virtual (dipanggil otomatis saat Reset Level atau pindah level di Mode Udara).
    /// </summary>
    public void ResetAirWeavingWorkbench()
    {
        grabbedGateSlotIndex = -1;
        hoveredGateSlotIndex = -1;
        secondaryHoveredGateSlotIndex = -1;
        hoveredInputNode = null;
        secondaryHoveredInputNode = null;
        activeCableHoverHandSlot = -1;
        postDropLockoutTimer = 0f;
        CancelAirDrawStrokeImmediate();
        handGestureDetector.Reset();
        secondaryHandGestureDetector.Reset();
        ResetExclusiveArbiterAndTwoHandState();
        ResetAllCircleSummoners();
        lastSfxSummonStage = AirWeavingCircleSummoner.SummonStage.Idle;
        lastSfxSummonChargeStep = 0;
        wasGrabbedGateInDeleteZone = false;
        lastOutputHoldSfxStep = 0;
        ResetHoverSfxTracking();
        if (spatialSfx != null)
        {
            spatialSfx.StopHumImmediate();
        }
        cableRouter?.ClearAllWires(circuitManager);
        summonVisual?.HideImmediate();
        outputTrigger?.HideImmediate();
        ResetLiveDebugMirror();
        ResetInputTouchState();

        if (!isAirWeavingActive)
        {
            return;
        }

        Camera mainCam = ResolveMainCamera();
        float aspectScale = ComputeHorizontalAspectScale(mainCam);

        for (int i = 0; i < gateSlots.Count; i++)
        {
            WorkbenchGateSlot slot = gateSlots[i];
            slot.isPlacedOnWorkbench = false;
            Vector2 homeXY = GetParkingSlotLocalXY(slot.slotIndex, aspectScale);
            slot.placedLocalXY = homeXY;
            slot.currentLocalXY = homeXY;
            slot.currentLocalZ = ComputeParkingSlotHoverHeightY(slot.slotIndex);
            slot.currentScale = ParkedGateScale;
            slot.ResetSpringState();
        }

        stageController?.ResetStageView(immediate: false);
        floatingPropRig?.SyncLevelVariantImmediate();

        if (mainCam != null)
        {
            bool anyHandForStage = IsAnyHandDetected;
            Vector2 stageParallaxPalmXY = isHandDetected
                ? handGestureDetector.PalmCursorLocalXY
                : secondaryHandGestureDetector.PalmCursorLocalXY;
            stageController?.TickStageTransform(
                mainCam,
                0.016f,
                isAirWeavingActive,
                IsAnyUiOverlayOpen,
                anyHandForStage,
                stageParallaxPalmXY,
                WorkbenchDepth);
            SyncAllWorkbenchNodesToCamera(mainCam, snapImmediately: true);
            UpdateWorkbenchGuideVisuals(mainCam);
            RefreshActivePlacedGatesBuffer();
            SyncCableRouterVisualsAfterPoseUpdate(mainCam);
        }

        SetHoverRingVisible(false);
    }

    private void Update()
    {
        if (!isAirWeavingActive || isTransitioning)
        {
            return;
        }

        Camera mainCam = ResolveMainCamera();
        if (mainCam == null || webCamTexture == null || !webCamTexture.isPlaying)
        {
            hologramVisual?.HideCursors();
            if (spatialSfx != null)
            {
                spatialSfx.StopHumImmediate();
            }
            return;
        }

        float dt = Time.unscaledDeltaTime;
        if (inputATapCooldown > 0f)
        {
            inputATapCooldown = Mathf.Max(0f, inputATapCooldown - dt);
        }
        if (inputBTapCooldown > 0f)
        {
            inputBTapCooldown = Mathf.Max(0f, inputBTapCooldown - dt);
        }
        if (inputATouchRippleTimer > 0f)
        {
            inputATouchRippleTimer = Mathf.Max(0f, inputATouchRippleTimer - dt);
        }
        if (inputBTouchRippleTimer > 0f)
        {
            inputBTouchRippleTimer = Mathf.Max(0f, inputBTouchRippleTimer - dt);
        }
        if (airDrawPostSpawnCooldown > 0f)
        {
            airDrawPostSpawnCooldown = Mathf.Max(0f, airDrawPostSpawnCooldown - dt);
        }
        if (postDropLockoutTimer > 0f) postDropLockoutTimer = Mathf.Max(0f, postDropLockoutTimer - dt);
        if (neutralCooldownTimer > 0f) neutralCooldownTimer = Mathf.Max(0f, neutralCooldownTimer - dt);

        if (levelManager != null && levelManager.IsUiOverlayOpen)
        {
            if (gestureGuide != null)
            {
                gestureGuide.OnExternalModalOrMenuOpened();
            }
        }
        if (gestureGuide != null)
        {
            if (gestureGuide.IsGuideOverlayOpen && spatialSfx != null)
            {
                spatialSfx.StopHumImmediate();
            }
            gestureGuide.Tick(dt, mainCam);
        }

        UpdateVideoQuadTransform(mainCam);

        if (handLandmarker != null && webCamTexture.width > 16 && webCamTexture.height > 16)
        {
            PumpAsyncCameraInference();
        }

        bool consumedNewInference = TryConsumeLatestWorkerInferenceResult(mainCam);
        if (!consumedNewInference && (isHandDetected || isSecondaryHandDetected || primarySlotActive || secondarySlotActive))
        {
            timeSinceLastValidHand += dt;
            if (timeSinceLastValidHand >= HandFrameStaleTimeoutSeconds)
            {
                ExpirePrimaryHandSlot(HandCarryFrameDropGraceSeconds);
                ExpireSecondaryHandSlot(HandCarryFrameDropGraceSeconds);
                ResetTwoHandManipulationState();
            }
        }

        TickHandSlotFrameDropGrace(dt);

        // Adaptive Inference Idle (Tahap 9): lacak durasi kosong tangan untuk throttle worker ~10 Hz, pulih instan saat tangan masuk
        if (IsAnyHandDetected)
        {
            idleNoHandElapsedSeconds = 0f;
        }
        else
        {
            idleNoHandElapsedSeconds += dt;
        }

        bool isSingleHandFistSticky = activeInteractionMode == ActiveInteractionMode.GateGrab
                                      || activeInteractionMode == ActiveInteractionMode.OutputHold;
        bool isSingleHandPinchSticky = activeInteractionMode == ActiveInteractionMode.CableDrag;
        bool isTwoHandFistSticky = activeInteractionMode == ActiveInteractionMode.TwoHandFreePan
                                   || activeInteractionMode == ActiveInteractionMode.TwoHandReset;
        bool isTwoHandPinchSticky = activeInteractionMode == ActiveInteractionMode.TwoHandOrbitZoom
                                    || activeInteractionMode == ActiveInteractionMode.TwoHandFreePan;
        bool isCarryPanFistSlot0 = isCarryPanActive && carryPanUsesFist && carryPanHandSlot == 0;
        bool isCarryPanFistSlot1 = isCarryPanActive && carryPanUsesFist && carryPanHandSlot == 1;
        bool isCarryPanPinchSlot0 = isCarryPanActive && !carryPanUsesFist && carryPanHandSlot == 0;
        bool isCarryPanPinchSlot1 = isCarryPanActive && !carryPanUsesFist && carryPanHandSlot == 1;

        bool isFistStickyDrag = isTwoHandFistSticky || (isSingleHandFistSticky && activeInteractionHandSlot == 0) || isCarryPanFistSlot0;
        bool isPinchStickyDrag = isTwoHandPinchSticky || (isSingleHandPinchSticky && activeInteractionHandSlot == 0) || isCarryPanPinchSlot0;
        bool isPrimaryCableDragSticky = isSingleHandPinchSticky && activeInteractionHandSlot == 0;
        bool isSecFistStickyDrag = isTwoHandFistSticky || (isSingleHandFistSticky && activeInteractionHandSlot == 1) || isCarryPanFistSlot1;
        bool isSecPinchStickyDrag = isTwoHandPinchSticky || (isSingleHandPinchSticky && activeInteractionHandSlot == 1) || isCarryPanPinchSlot1;
        bool isSecondaryCableDragSticky = isSingleHandPinchSticky && activeInteractionHandSlot == 1;
        bool twoHandsVisibleForEdgeRelax = isHandDetected && isSecondaryHandDetected;
        handGestureDetector.TickTemporalState(
            dt,
            isHandDetected,
            isFistStickyDrag,
            isPinchStickyDrag,
            twoHandsVisibleForEdgeRelax,
            isPrimaryCableDragSticky,
            primaryMissedLastInference);
        secondaryHandGestureDetector.TickTemporalState(
            dt,
            isSecondaryHandDetected,
            isSecFistStickyDrag,
            isSecPinchStickyDrag,
            twoHandsVisibleForEdgeRelax,
            isSecondaryCableDragSticky,
            secondaryMissedLastInference);
        UpdatePinchBeginGraceTimers(dt);
        UpdateTwoHandStageManipulation(mainCam, dt);
        UpdateSingleHandAutoEdgePan(mainCam, dt);
        if (stageController != null)
        {
            bool hasStageParallaxHand = IsAnyHandDetected;
            Vector2 stageParallaxPalmXY;
            if (activeInteractionHandSlot == 1 && isSecondaryHandDetected)
            {
                stageParallaxPalmXY = secondaryHandGestureDetector.PalmCursorLocalXY;
            }
            else if (isHandDetected)
            {
                stageParallaxPalmXY = handGestureDetector.PalmCursorLocalXY;
            }
            else
            {
                stageParallaxPalmXY = secondaryHandGestureDetector.PalmCursorLocalXY;
            }
            stageController.TickStageTransform(
                mainCam,
                dt,
                isAirWeavingActive,
                IsAnyUiOverlayOpen,
                hasStageParallaxHand,
                stageParallaxPalmXY,
                WorkbenchDepth);
        }
        lightingAndShaderRig?.TickLightingRig(mainCam, stageController != null ? stageController.StageRoot : null);
        UpdateHandWakeAudioFeedback(dt);
        UpdateCableRouterInteraction(mainCam);
        UpdateWorkbenchInteractions(mainCam, dt);
        UpdateCircleSummoner(mainCam, dt);
        UpdateAirDrawStrokeFade(mainCam, dt);
        SyncAllWorkbenchNodesToCamera(mainCam, snapImmediately: false);
        if (cableRouter != null && cableRouter.TopologyVersion != lastEvaluatedCircuitTopologyVersion)
        {
            lastEvaluatedCircuitTopologyVersion = cableRouter.TopologyVersion;
            if (circuitManager != null)
            {
                circuitManager.EvaluateCircuit();
            }
        }
        SyncCableRouterVisualsAfterPoseUpdate(mainCam);
        UpdateHoverSfxFeedback(dt);
        UpdateContinuousHumFeedback(mainCam, dt);
        UpdateWorkbenchGuideVisuals(mainCam);
        GateNode activeGrabbedGate = (grabbedGateSlotIndex >= 0
                                      && grabbedGateSlotIndex < gateSlots.Count
                                      && gateSlots[grabbedGateSlotIndex] != null)
            ? gateSlots[grabbedGateSlotIndex].node
            : null;
        floatingPropRig?.TickProps(dt, activeGrabbedGate, wasGrabbedGateInDeleteZone);
        UpdateHologramVisuals(mainCam, dt);
        UpdateCircleSummonVisuals(mainCam, dt);
        UpdateOutputSpatialTrigger(mainCam, dt);
        UpdateAirWeavingStatusFeedback();
    }

    private void LateUpdate()
    {
        if (!isAirWeavingActive || isTransitioning)
        {
            return;
        }

        Camera mainCam = ResolveMainCamera();
        if (mainCam == null)
        {
            return;
        }

        UpdateAirWeavingLabelsAndTruthPodSpatialPose(mainCam);
    }

    private void OnDisable()
    {
        floatingPropRig?.DeactivateAndRestoreForCardMode();
        lightingAndShaderRig?.DeactivateAndRestoreForCardMode();
        ShutdownNativeAndCameraImmediate();
        RestoreFramePacingState();
    }

    private void OnDestroy()
    {
        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }

        if (outputTrigger != null)
        {
            outputTrigger.OnVerifyRequested -= HandleOutputTriggerVerify;
            outputTrigger.OnNextLevelRequested -= HandleOutputTriggerNextLevel;
        }
        if (cableRouter != null)
        {
            cableRouter.OnWireGrabbedSfx -= HandleCableGrabSfx;
            cableRouter.OnWireTargetLockSfx -= HandleCableTargetLockSfx;
            cableRouter.OnWireTargetUnlockSfx -= HandleCableTargetUnlockSfx;
            cableRouter.OnWireConnectedSfx -= HandleCableConnectSfx;
            cableRouter.OnWireDisconnectedSfx -= HandleCableDetachSfx;
            cableRouter.OnWireRejectSfx -= HandleCableRejectSfx;
        }
        if (gestureGuide != null)
        {
            gestureGuide.CleanupImmediate();
            gestureGuide = null;
        }
        UnbindHudMenuEvents();
        cachedHudMenu = null;
        circuitManager?.SetManualRoutingMode(false);
        floatingPropRig?.DeactivateAndRestoreForCardMode();
        lightingAndShaderRig?.DeactivateAndRestoreForCardMode();
        RestoreAirWeavingLabelReadabilityStyles();
        ShutdownNativeAndCameraImmediate();
        RestoreFramePacingState();
        DestroyVisualRigAndMaterials();
        cachedGateNodes.Clear();
        pausedObserverHandlers.Clear();
        gateSlots.Clear();
        activePlacedGatesBuffer.Clear();
        truthEvalVisited.Clear();
        airDrawPointsLocal.Clear();
        cachedMainCamera = null;
        cachedVuforiaBehaviour = null;
    }

    private void ApplyAirWeavingFramePacingState()
    {
        if (!hasSavedFramePacing)
        {
            savedVSyncCount = QualitySettings.vSyncCount;
            savedTargetFrameRate = Application.targetFrameRate;
            hasSavedFramePacing = true;
        }

        // Nonaktifkan vSyncCount selama Mode Udara agar Application.targetFrameRate = 60 aktif di Android (anti stair-stepping 120Hz/60Hz)
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;

        // Efisiensi Tahap 9: matikan simulasi PhysX otomatis selama Mode Udara (interaksi 100% analitik tanpa Collider)
        if (!hasSavedPhysicsSimulationMode)
        {
            savedPhysicsSimulationMode = Physics.simulationMode;
            hasSavedPhysicsSimulationMode = true;
        }
        Physics.simulationMode = SimulationMode.Script;
    }

    private void RestoreFramePacingState()
    {
        if (hasSavedFramePacing)
        {
            QualitySettings.vSyncCount = savedVSyncCount;
            Application.targetFrameRate = savedTargetFrameRate;
            hasSavedFramePacing = false;
        }

        if (hasSavedPhysicsSimulationMode)
        {
            Physics.simulationMode = savedPhysicsSimulationMode;
            hasSavedPhysicsSimulationMode = false;
        }
    }

    private void OnApplicationQuit()
    {
        ShutdownNativeAndCameraImmediate();
        RestoreFramePacingState();
    }

    // =========================================================================
    // CAMERA HANDOVER COROUTINES (VUFORIA v5 <-> WEBCAM + MEDIAPIPE v6)
    // =========================================================================

    private IEnumerator EnterAirWeavingRoutine()
    {
        isTransitioning = true;
        SetFeedbackImmediate("[Air-Weaving v6] Menyiapkan kamera & meja kerja 3D MediaPipe...", FeedbackWaitingColor);

        // 1. Kunci semua GateNode ke kendali eksternal & nonaktifkan DefaultObserverEventHandler agar Vuforia tidak mematikan MeshRenderer
        RefreshGateNodesList();
        PauseVuforiaObserverHandlers();
        for (int i = 0; i < cachedGateNodes.Count; i++)
        {
            GateNode node = cachedGateNodes[i];
            if (node != null)
            {
                node.useExternalSpatialPose = true;
                node.SetExternalSpatialPose(false, node.transform.position, node.transform.rotation);
            }
        }

        // 2. Nonaktifkan VuforiaBehaviour agar hardware Camera2 Android benar-benar dilepas
        Camera mainCam = ResolveMainCamera();
        cachedVuforiaBehaviour = FindVuforiaBehaviour(mainCam);
        if (cachedVuforiaBehaviour != null && cachedVuforiaBehaviour.enabled)
        {
            cachedVuforiaBehaviour.enabled = false;
        }

        yield return new WaitForSecondsRealtime(VuforiaReleaseDelaySeconds);

        // 3. Pastikan izin kamera Android aktif
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
        {
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
            float permWait = 0f;
            while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera) && permWait < 6f)
            {
                permWait += Time.unscaledDeltaTime;
                yield return null;
            }
        }
#endif

        // 4. Pilih kamera (480x360@30fps untuk stabilisasi FPS CPU Android; prioritas kamera depan di Android)
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices == null || devices.Length == 0)
        {
            yield return RollbackToVuforiaMode("[Air-Weaving v6] Kamera tidak ditemukan. Kembali ke Mode Kartu (v5).");
            yield break;
        }

        int selectedIndex = 0;
        for (int i = 0; i < devices.Length; i++)
        {
            if (devices[i].isFrontFacing)
            {
                selectedIndex = i;
                break;
            }
        }

        WebCamDevice selectedDevice = devices[selectedIndex];
#if UNITY_ANDROID && !UNITY_EDITOR
        mirrorHorizontalDisplay = selectedDevice.isFrontFacing;
#else
        mirrorHorizontalDisplay = true;
#endif

        webCamTexture = new WebCamTexture(selectedDevice.name, RequestedCamWidth, RequestedCamHeight, RequestedCamFps);
        webCamTexture.Play();

        float startTimer = 0f;
        while ((webCamTexture.width <= 16 || webCamTexture.height <= 16 || !webCamTexture.didUpdateThisFrame)
               && startTimer < WebCamStartTimeoutSeconds)
        {
            startTimer += Time.unscaledDeltaTime;
            yield return null;
        }

        if (webCamTexture == null || !webCamTexture.isPlaying || webCamTexture.width <= 16 || webCamTexture.height <= 16)
        {
            yield return RollbackToVuforiaMode("[Air-Weaving v6] Gagal membuka umpan kamera. Kembali ke Mode Kartu (v5).");
            yield break;
        }

        // 5. Muat model hand_landmarker.task (lintas platform Editor Windows & Android APK)
        if (cachedModelBytes == null || cachedModelBytes.Length == 0)
        {
            yield return LoadHandLandmarkerModelBytesRoutine();
        }

        if (cachedModelBytes == null || cachedModelBytes.Length == 0)
        {
            yield return RollbackToVuforiaMode("[Air-Weaving v6] Model hand_landmarker.task tidak ditemukan di StreamingAssets.");
            yield break;
        }

        // 6. Inisialisasi HandLandmarker (Delegate.CPU + RunningMode.VIDEO, 100% aman OpenGLES3 & Windows)
        if (!TryInitializeHandLandmarker())
        {
            yield return RollbackToVuforiaMode("[Air-Weaving v6] Gagal inisialisasi MediaPipe HandLandmarker. Kembali ke Mode Kartu.");
            yield break;
        }

        // 7. Siapkan bidang video latar, kerangka meja kerja 3D, dan rig 21 sendi neon + trail
        EnsureVideoQuadCreated(mainCam);
        EnsureHandVisualRigCreated();
        EnsureWorkbenchVisualsCreated();

        if (mainCam != null)
        {
            if (!hasSavedCameraState)
            {
                savedCameraClearFlags = mainCam.clearFlags;
                savedCameraBgColor = mainCam.backgroundColor;
                hasSavedCameraState = true;
            }
            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = new Color(0.03f, 0.05f, 0.09f, 1f);
        }

        if (videoQuadMaterial != null)
        {
            videoQuadMaterial.mainTexture = webCamTexture;
        }

        if (videoQuadRootObj != null)
        {
            videoQuadRootObj.SetActive(true);
        }

        if (workbenchVisualRootObj != null)
        {
            workbenchVisualRootObj.SetActive(true);
        }

        if (stageController != null)
        {
            stageController.EnsureInitialized(mainCam);
            stageController.ResetStageView(immediate: true);
            stageController.SetStageVisible(true);
            stageController.TickStageTransform(
                mainCam,
                0.016f,
                isAirWeavingActive: true,
                isUiOverlayOpen: false,
                isHandDetected: false,
                palmCameraLocalXY: Vector2.zero,
                workbenchDepth: WorkbenchDepth);
        }

        lastVideoQuadCamW = -1;
        UpdateVideoQuadTransform(mainCam);
        SetHandRigVisible(false);
        SetSecondaryHandRigVisible(false);
        SetHoverRingVisible(false);
        outputTrigger?.HideImmediate();
        hologramVisual?.HideImmediate();
        cableRouter?.HideImmediate();
        ResetAllCircleSummoners();
        summonVisual?.HideImmediate();

        // 8. Tata seluruh 8 GateNode (Input A, Input B, Output terkunci di papan roti; 5 gerbang di Rak Parkir Atas)
        InitializeWorkbenchSlots(mainCam);
        ApplyAirWeavingLabelReadabilityStyles();
        floatingPropRig?.ActivateForAirWeaving(inputANode, inputBNode, outputNode, cachedGateNodes);
        lightingAndShaderRig?.SetFloatingPropRig(floatingPropRig);
        lightingAndShaderRig?.ActivateForAirWeaving(
            mainCam,
            stageController != null ? stageController.StageRoot : null,
            cachedGateNodes);

        isHandDetected = false;
        wasHandDetected = false;
        isSecondaryHandDetected = false;
        wasSecondaryHandDetected = false;
        primarySlotActive = false;
        primaryMissedLastInference = false;
        primaryMissingTimer = 0f;
        primarySlotHandedness = TrackedHandedness.Unknown;
        secondarySlotActive = false;
        secondaryMissedLastInference = false;
        secondaryMissingTimer = 0f;
        secondarySlotHandedness = TrackedHandedness.Unknown;
        rawPinching = false;
        isPinching = false;
        wasPinching = false;
        grabbedGateSlotIndex = -1;
        hoveredGateSlotIndex = -1;
        secondaryHoveredGateSlotIndex = -1;
        hoveredInputNode = null;
        secondaryHoveredInputNode = null;
        activeCableHoverHandSlot = -1;
        inputATapCooldown = 0f;
        inputBTapCooldown = 0f;
        isIndexOnlyWandPose = false;
        airDrawPostSpawnCooldown = 0f;
        postDropLockoutTimer = 0f;
        noHandAbsentTimer = 0f;
        noHandToastShown = false;
        CancelAirDrawStrokeImmediate();
        ResetLandmarkOneEuroFilters();
        ResetSecondaryLandmarkOneEuroFilters();
        ResetExclusiveArbiterAndTwoHandState();
        ApplyAirWeavingFramePacingState();
        StartInferenceWorkerThread();
        fpsTimer = 0f;
        timeSinceLastValidHand = 0f;
        idleNoHandElapsedSeconds = 0f;
        lastInferenceDispatchUnscaledTime = -10f;
        inferenceFramesInWindow = 0;
        displayedInferenceFps = 0;

        isAirWeavingActive = true;
        isTransitioning = false;
        transitionCoroutine = null;

        handGestureDetector.Reset();
        secondaryHandGestureDetector.Reset();
        ResetAllCircleSummoners();
        lastSfxSummonStage = AirWeavingCircleSummoner.SummonStage.Idle;
        lastSfxSummonChargeStep = 0;
        wasGrabbedGateInDeleteZone = false;
        lastOutputHoldSfxStep = 0;
        handAbsentForWakeSeconds = 0f;
        wasHandDetectedForWakeSfx = false;
        ResetHoverSfxTracking();
        if (spatialSfx != null)
        {
            spatialSfx.Configure(levelManager, mainCam);
        }
        if (cachedHudMenu == null && levelManager != null)
        {
            cachedHudMenu = levelManager.GetComponent<CompactHudMenuController>();
        }
        BindHudMenuEvents(cachedHudMenu);
        if (gestureGuide != null)
        {
            gestureGuide.Configure(cachedHudMenu, spatialSfx, mainCam, txtFeedback != null ? txtFeedback.font : null);
        }
        ResetLiveDebugMirror();
        ResetInputTouchState();
        circuitManager?.SetManualRoutingMode(true);
        cableRouter?.ClearAllWires(circuitManager);
        hologramVisual?.SetRootVisible(true);
        summonVisual?.HideImmediate();

        SyncAllWorkbenchNodesToCamera(mainCam, snapImmediately: true);
        UpdateWorkbenchGuideVisuals(mainCam);
        RefreshActivePlacedGatesBuffer();
        SyncCableRouterVisualsAfterPoseUpdate(mainCam);

        OnAirWeavingModeChanged?.Invoke(true);
        gestureGuide?.OnAirWeavingModeEntered();
        if (gestureGuide == null || !gestureGuide.IsGuideOverlayOpen)
        {
            SetFeedbackImmediate("Mode Udara aktif — Kepal: sakelar Input & pegang gerbang, Cubit: tarik kabel.", FeedbackActiveColor, 2.8f);
        }
    }

    private IEnumerator ExitAirWeavingRoutine()
    {
        isTransitioning = true;
        isAirWeavingActive = false;
        gestureGuide?.OnAirWeavingModeExited();
        grabbedGateSlotIndex = -1;
        hoveredGateSlotIndex = -1;
        secondaryHoveredGateSlotIndex = -1;
        hoveredInputNode = null;
        secondaryHoveredInputNode = null;
        activeCableHoverHandSlot = -1;
        cableRouter?.ClearAllWires(circuitManager);
        circuitManager?.SetManualRoutingMode(false);
        SetFeedbackImmediate("[Air-Weaving v6] Mengembalikan kamera ke Mode Kartu (v5)...", FeedbackWaitingColor);

        ShutdownNativeAndCameraImmediate();
        floatingPropRig?.DeactivateAndRestoreForCardMode();
        HideAllAirWeavingVisuals();

        yield return new WaitForSecondsRealtime(WebCamReleaseDelaySeconds);

        RestoreCameraAndVuforiaState();

        isTransitioning = false;
        transitionCoroutine = null;

        SetFeedbackImmediate("Mode Kartu (v5) aktif kembali. Arahkan kamera ke kartu marker.", Color.white);
        OnAirWeavingModeChanged?.Invoke(false);
    }

    private IEnumerator RollbackToVuforiaMode(string warningMessage)
    {
        isAirWeavingActive = false;
        gestureGuide?.OnAirWeavingModeExited();
        grabbedGateSlotIndex = -1;
        hoveredGateSlotIndex = -1;
        secondaryHoveredGateSlotIndex = -1;
        hoveredInputNode = null;
        secondaryHoveredInputNode = null;
        activeCableHoverHandSlot = -1;
        cableRouter?.ClearAllWires(circuitManager);
        circuitManager?.SetManualRoutingMode(false);
        floatingPropRig?.DeactivateAndRestoreForCardMode();
        lightingAndShaderRig?.DeactivateAndRestoreForCardMode();
        RestoreAirWeavingLabelReadabilityStyles();
        ShutdownNativeAndCameraImmediate();
        HideAllAirWeavingVisuals();

        yield return new WaitForSecondsRealtime(WebCamReleaseDelaySeconds);

        RestoreCameraAndVuforiaState();

        isTransitioning = false;
        transitionCoroutine = null;

        SetFeedbackImmediate(warningMessage, FeedbackWaitingColor);
        OnAirWeavingModeChanged?.Invoke(false);
    }

    private void RestoreCameraAndVuforiaState()
    {
        RestoreFramePacingState();

        Camera mainCam = ResolveMainCamera();
        if (mainCam != null && hasSavedCameraState)
        {
            mainCam.clearFlags = savedCameraClearFlags;
            mainCam.backgroundColor = savedCameraBgColor;
        }

        RestoreAirWeavingLabelReadabilityStyles();
        floatingPropRig?.DeactivateAndRestoreForCardMode();
        lightingAndShaderRig?.DeactivateAndRestoreForCardMode();
        RefreshGateNodesList();
        for (int i = 0; i < cachedGateNodes.Count; i++)
        {
            GateNode node = cachedGateNodes[i];
            if (node != null)
            {
                node.ClearExternalSpatialOverride();
            }
        }

        ResumeVuforiaObserverHandlers();

        if (cachedVuforiaBehaviour == null)
        {
            cachedVuforiaBehaviour = FindVuforiaBehaviour(mainCam);
        }

        if (cachedVuforiaBehaviour != null && !cachedVuforiaBehaviour.enabled)
        {
            cachedVuforiaBehaviour.enabled = true;
        }
    }

    /// <summary>
    /// Menonaktifkan sementara Vuforia DefaultObserverEventHandler pada seluruh kartu selama Mode Udara aktif
    /// agar callback OnTrackingLost() tidak mematikan MeshRenderer model 3D Quaternius & TextMeshPro.
    /// </summary>
    private void PauseVuforiaObserverHandlers()
    {
        pausedObserverHandlers.Clear();
        for (int i = 0; i < cachedGateNodes.Count; i++)
        {
            GateNode node = cachedGateNodes[i];
            if (node == null)
            {
                continue;
            }

            Behaviour[] behaviours = node.GetComponents<Behaviour>();
            for (int b = 0; b < behaviours.Length; b++)
            {
                Behaviour beh = behaviours[b];
                if (beh != null
                    && beh.enabled
                    && string.Equals(beh.GetType().Name, "DefaultObserverEventHandler", StringComparison.Ordinal))
                {
                    beh.enabled = false;
                    pausedObserverHandlers.Add(beh);
                }
            }
        }
    }

    private void ResumeVuforiaObserverHandlers()
    {
        for (int i = 0; i < pausedObserverHandlers.Count; i++)
        {
            if (pausedObserverHandlers[i] != null)
            {
                pausedObserverHandlers[i].enabled = true;
            }
        }
        pausedObserverHandlers.Clear();
    }

    private IEnumerator LoadHandLandmarkerModelBytesRoutine()
    {
        string modelPath = Path.Combine(Application.streamingAssetsPath, ModelFileName);

#if UNITY_ANDROID && !UNITY_EDITOR
        using (UnityWebRequest request = UnityWebRequest.Get(modelPath))
        {
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success && request.downloadHandler != null)
            {
                cachedModelBytes = request.downloadHandler.data;
            }
            else
            {
                Debug.LogError($"[AirWeavingController] Gagal membaca model Android '{modelPath}': {request.error}");
                cachedModelBytes = null;
            }
        }
#else
        if (File.Exists(modelPath))
        {
            try
            {
                cachedModelBytes = File.ReadAllBytes(modelPath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AirWeavingController] Gagal membaca model '{modelPath}': {ex.Message}");
                cachedModelBytes = null;
            }
        }
        else
        {
            Debug.LogError($"[AirWeavingController] File model tidak ditemukan: {modelPath}");
            cachedModelBytes = null;
        }
        yield return null;
#endif
    }

    private bool TryInitializeHandLandmarker()
    {
        try
        {
            if (!protobufLogHandlerActive)
            {
                Protobuf.SetLogHandler(Protobuf.DefaultLogHandler);
                protobufLogHandlerActive = true;
            }

            BaseOptions baseOptions = new BaseOptions(
                delegateCase: BaseOptions.Delegate.CPU,
                modelAssetBuffer: cachedModelBytes);

            HandLandmarkerOptions options = new HandLandmarkerOptions(
                baseOptions: baseOptions,
                runningMode: RunningMode.VIDEO,
                numHands: 2,
                minHandDetectionConfidence: MediaPipeMinHandDetectionConfidence,
                minHandPresenceConfidence: MediaPipeMinHandPresenceConfidence,
                minTrackingConfidence: MediaPipeMinTrackingConfidence);

            handLandmarker = HandLandmarker.CreateFromOptions(options);
            cachedResult = HandLandmarkerResult.Alloc(2);
            return handLandmarker != null;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AirWeavingController] Inisialisasi HandLandmarker gagal: {ex}");
            DisposeHandLandmarkerOnly();
            return false;
        }
    }

    // =========================================================================
    // SPATIAL WORKBENCH SETUP & AIR-PINCH / AIR-TAP INTERACTIONS (TAHAP 2)
    // =========================================================================

    private void InitializeWorkbenchSlots(Camera mainCam)
    {
        RefreshGateNodesList();
        ResolveCoreTerminalNodes();
        gateSlots.Clear();

        float aspectScale = ComputeHorizontalAspectScale(mainCam);

        for (int orderIdx = 0; orderIdx < ParkingSlotOrder.Length; orderIdx++)
        {
            NodeType desiredType = ParkingSlotOrder[orderIdx];
            GateNode matchedNode = FindGateNodeByType(desiredType);
            if (matchedNode == null)
            {
                continue;
            }

            matchedNode.useExternalSpatialPose = true;
            Vector2 homeXY = GetParkingSlotLocalXY(orderIdx, aspectScale);
            gateSlots.Add(new WorkbenchGateSlot
            {
                node = matchedNode,
                slotIndex = orderIdx,
                isPlacedOnWorkbench = false,
                placedLocalXY = homeXY,
                currentLocalXY = homeXY,
                currentLocalZ = ComputeParkingSlotHoverHeightY(orderIdx),
                currentScale = ParkedGateScale
            });
        }

        if (inputANode != null)
        {
            inputANode.useExternalSpatialPose = true;
        }
        if (inputBNode != null)
        {
            inputBNode.useExternalSpatialPose = true;
        }
        if (outputNode != null)
        {
            outputNode.useExternalSpatialPose = true;
            cachedOutputTruthPodRootTransform = outputNode.transform.Find("OutputTruthPod3D");
        }
    }

    private void ResetCarryPanState(bool brakeStageWithoutCoast)
    {
        bool wasActive = isCarryPanActive;
        isCarryPanActive = false;
        carryPanHandSlot = -1;
        carryPanUsesFist = true;
        carryPanConfirmTimer = 0f;
        carryPanPrevHandXY = Vector2.zero;
        wasCarryPanReleaseFrozen = false;
        wasCarryPanOcclusionPaused = false;
        carryPanPalmScaleFilter.Reset();

        if (wasActive && brakeStageWithoutCoast)
        {
            stageController?.EndCarryPanWithoutCoast();
        }
    }

    private void ResetAutoEdgePanState(bool brakeStageImmediate)
    {
        bool wasActive = isAutoEdgePanActive;
        isAutoEdgePanActive = false;
        edgePanDwellTimer = 0f;

        if (wasActive && brakeStageImmediate)
        {
            stageController?.BrakeEdgePanImmediate();
        }
    }

    private void ResetExclusiveArbiterAndTwoHandState()
    {
        activeInteractionMode = ActiveInteractionMode.Idle;
        activeInteractionHandSlot = -1;
        neutralCooldownTimer = 0f;
        pinchBeginGraceTimers[0] = 0f;
        pinchBeginGraceTimers[1] = 0f;
        singleHandModeEnteredTime = -10f;
        twoHandOrbitConfirmTimer = 0f;
        twoHandFreePanConfirmTimer = 0f;
        twoHandIntentPendingTimer = 0f;
        isTwoHandConfirming = false;
        twoHandFreePanPrimaryIsPinch = true;
        twoHandFistResetLatched = false;
        twoHandFistResetTimer = 0f;
        wasTwoHandOrbitReleaseFrozen = false;
        wasTwoHandPanReleaseFrozen = false;
        twoHandAvgPalmScaleFilter.Reset();
        ResetCarryPanState(brakeStageWithoutCoast: false);
        ResetAutoEdgePanState(brakeStageImmediate: false);
        grabStageDepthCompMeters = 0f;
        lastGrabStagePanOffset = Vector3.zero;
        hasLastGrabStagePanOffset = false;
        stageController?.SetTwoHandManipulationState(false);
    }

    private void ResetTwoHandManipulationState()
    {
        if (IsAnyTwoHandModeActive)
        {
            activeInteractionMode = ActiveInteractionMode.Idle;
            activeInteractionHandSlot = -1;
        }
        twoHandOrbitConfirmTimer = 0f;
        twoHandFreePanConfirmTimer = 0f;
        twoHandIntentPendingTimer = 0f;
        isTwoHandConfirming = false;
        twoHandFistResetLatched = false;
        twoHandFistResetTimer = 0f;
        wasTwoHandOrbitReleaseFrozen = false;
        wasTwoHandPanReleaseFrozen = false;
        twoHandAvgPalmScaleFilter.Reset();
        bool hadCarryPan = isCarryPanActive;
        ResetCarryPanState(brakeStageWithoutCoast: hadCarryPan);
        ResetAutoEdgePanState(brakeStageImmediate: isAutoEdgePanActive);
        if (!hadCarryPan)
        {
            stageController?.SetTwoHandManipulationState(false);
        }
    }

    private void ExitInteractionMode(ActiveInteractionMode expectedMode, bool triggerNeutralCooldown)
    {
        if (activeInteractionMode == expectedMode)
        {
            if (expectedMode == ActiveInteractionMode.CableDrag || expectedMode == ActiveInteractionMode.GateGrab)
            {
                ResetCarryPanState(brakeStageWithoutCoast: isCarryPanActive);
                ResetAutoEdgePanState(brakeStageImmediate: isAutoEdgePanActive);
                grabStageDepthCompMeters = 0f;
                hasLastGrabStagePanOffset = false;
            }
            activeInteractionMode = ActiveInteractionMode.Idle;
            activeInteractionHandSlot = -1;
            singleHandModeEnteredTime = -10f;
        }
        if (triggerNeutralCooldown)
        {
            neutralCooldownTimer = Mathf.Max(neutralCooldownTimer, CrossGestureNeutralCooldownSeconds);
        }
    }

    /// <summary>
    /// Membatalkan aksi 1-tangan yang baru saja mulai dalam jendela grace singkat (&lt;= 190 ms) tanpa efek samping
    /// (mengembalikan gerbang/kabel yang baru disentuh) agar mode 2 tangan global mengambil alih secara mulus. Zero-GC.
    /// </summary>
    private void RevertSingleHandForTwoHandUpgrade(Camera mainCam)
    {
        ResetCarryPanState(brakeStageWithoutCoast: false);
        ResetAutoEdgePanState(brakeStageImmediate: false);
        grabStageDepthCompMeters = 0f;
        hasLastGrabStagePanOffset = false;

        if (activeInteractionMode == ActiveInteractionMode.CableDrag)
        {
            cableRouter?.RevertActiveDragForTwoHandUpgrade(circuitManager);
        }
        else if (activeInteractionMode == ActiveInteractionMode.GateGrab
                 && grabbedGateSlotIndex >= 0
                 && grabbedGateSlotIndex < gateSlots.Count)
        {
            WorkbenchGateSlot slot = gateSlots[grabbedGateSlotIndex];
            if (slot != null)
            {
                slot.isPlacedOnWorkbench = preGrabWasPlaced;
                slot.placedLocalXY = preGrabPlacedLocalXY;
                slot.currentLocalXY = preGrabCurrentLocalXY;
                slot.currentLocalZ = preGrabLocalZ;
                slot.currentScale = preGrabScale;
                slot.ResetSpringState();
            }
            grabbedGateSlotIndex = -1;
            wasGrabbedGateInDeleteZone = false;
        }
        else if (activeInteractionMode == ActiveInteractionMode.CircleSummon)
        {
            ResetAllCircleSummoners();
        }
        else if (activeInteractionMode == ActiveInteractionMode.InputSwitch)
        {
            inputADwellTimers[0] = 0f;
            inputADwellTimers[1] = 0f;
            inputBDwellTimers[0] = 0f;
            inputBDwellTimers[1] = 0f;
        }

        if (isRecordingAirDraw)
        {
            CancelAirDrawStrokeImmediate();
        }
        activeInteractionMode = ActiveInteractionMode.Idle;
        activeInteractionHandSlot = -1;
        singleHandModeEnteredTime = -10f;
        neutralCooldownTimer = 0f;
        if (mainCam != null)
        {
            SetHoverRingVisible(false);
        }
    }

    /// <summary>
    /// Kontrol 2 Tangan Full Free-View 6-DoF Global di Seluruh Layar Kamera (Tahap 5.5+ Zero-GC):
    /// - 100% bebas lokasi: bekerja di ruang kosong mana pun, pinggir layar, maupun di atas hologram/objek.
    /// - Prioritas 2-tangan anti-bajak: jeda pra-niat saat kedua tangan mulai membentuk gestur 2 tangan + upgrade mulus
    ///   dari aksi 1-tangan yang baru mulai &lt;= 190 ms; setelah &gt; 190 ms aksi 1-tangan tetap terkunci aman oleh Arbiter
    ///   dan membuka sub-mode simetris Carry-Pan Tangan Kedua (CableDrag+Fist / GateGrab+Pinch).
    /// - Tidak pernah membatalkan mode 2 tangan yang sudah aktif hanya karena salah satu tangan mendekati tepi layar.
    /// - Both Hands Pinch: Orbit 360° Quaternion Arcball + Roll + Zoom.
    /// - Free Air-Pan 3D: 1 Pinch + 1 Fist (dengan hold hysteresis &amp; Anti-Kick Release Gate).
    /// - Both Hands Fist: tahan 550 ms untuk reset panggung.
    /// </summary>
    private void UpdateTwoHandStageManipulation(Camera mainCam, float dt)
    {
        isTwoHandConfirming = false;

        bool overlayOrVerify = IsAnyUiOverlayOpen || (levelManager != null && levelManager.IsVerifying);
        if (overlayOrVerify)
        {
            if (IsAnyTwoHandModeActive)
            {
                ExitInteractionMode(activeInteractionMode, triggerNeutralCooldown: false);
            }
            bool hadCarryPanOverlay = isCarryPanActive;
            ResetCarryPanState(brakeStageWithoutCoast: hadCarryPanOverlay);
            twoHandOrbitConfirmTimer = 0f;
            twoHandFreePanConfirmTimer = 0f;
            twoHandIntentPendingTimer = 0f;
            twoHandFistResetTimer = 0f;
            twoHandFistResetLatched = false;
            wasTwoHandOrbitReleaseFrozen = false;
            wasTwoHandPanReleaseFrozen = false;
            if (!hadCarryPanOverlay)
            {
                stageController?.SetTwoHandManipulationState(false);
            }
            return;
        }

        bool twoHandsPresent = isHandDetected && isSecondaryHandDetected;
        if (!twoHandsPresent)
        {
            if (IsAnyTwoHandModeActive)
            {
                ExitInteractionMode(activeInteractionMode, triggerNeutralCooldown: true);
            }
            // Jika Tangan 2 hilang saat Carry-Pan aktif: akhiri Carry-Pan bersih TANPA mengganggu sticky lock Tangan 1
            bool hadCarryPanLost = isCarryPanActive;
            ResetCarryPanState(brakeStageWithoutCoast: hadCarryPanLost);
            twoHandOrbitConfirmTimer = 0f;
            twoHandFreePanConfirmTimer = 0f;
            twoHandIntentPendingTimer = 0f;
            twoHandFistResetTimer = 0f;
            twoHandFistResetLatched = false;
            wasTwoHandOrbitReleaseFrozen = false;
            wasTwoHandPanReleaseFrozen = false;
            if (!hadCarryPanLost)
            {
                stageController?.SetTwoHandManipulationState(false);
            }
            return;
        }

        // Predikat anatomi murni (nol lokasi pembatas — berlaku global di seluruh layar kamera)
        bool primaryPurePinch = handGestureDetector.IsPinchActive && !handGestureDetector.IsFistActive;
        bool secondaryPurePinch = secondaryHandGestureDetector.IsPinchActive && !secondaryHandGestureDetector.IsFistActive;
        bool primaryPureFist = handGestureDetector.IsFistActive && !handGestureDetector.IsPinchActive;
        bool secondaryPureFist = secondaryHandGestureDetector.IsFistActive && !secondaryHandGestureDetector.IsPinchActive;

        bool bothPurePinch = primaryPurePinch && secondaryPurePinch;
        bool primaryPinchSecondaryFist = primaryPurePinch && secondaryPureFist;
        bool primaryFistSecondaryPinch = primaryPureFist && secondaryPurePinch;
        bool validFreePanPose = primaryPinchSecondaryFist || primaryFistSecondaryPinch;
        bool bothPureFists = primaryPureFist && secondaryPureFist;

        // Kunci Eksklusif Arbiter: lindungi aksi 1-tangan yang sedang berlangsung;
        // - <= 190 ms: upgrade mulus ke mode 2 tangan global (Orbit/FreePan) agar tidak kalah cepat dekat objek.
        // - > 190 ms: aksi 1-tangan (CableDrag/GateGrab) terkunci penuh, dan Tangan 2 dapat mengaktifkan sub-state
        //   Carry-Pan simetris (CableDrag + Fist / GateGrab + Pinch) tanpa pernah memutus Tangan 1 atau memicu Both Fist.
        bool lockedBySingleHandMode = activeInteractionMode != ActiveInteractionMode.Idle && !IsAnyTwoHandModeActive;
        if (lockedBySingleHandMode)
        {
            float singleHandElapsed = Time.unscaledTime - singleHandModeEnteredTime;
            bool canUpgradeSingleHand = singleHandElapsed >= 0f
                                        && singleHandElapsed <= TwoHandSingleHandUpgradeWindowSeconds
                                        && activeInteractionMode != ActiveInteractionMode.OutputHold
                                        && (bothPurePinch || validFreePanPose)
                                        && handGestureDetector.IsTwoHandQualityGatePassing
                                        && secondaryHandGestureDetector.IsTwoHandQualityGatePassing;
            if (canUpgradeSingleHand)
            {
                RevertSingleHandForTwoHandUpgrade(mainCam);
            }
            else
            {
                twoHandOrbitConfirmTimer = 0f;
                twoHandFreePanConfirmTimer = 0f;
                twoHandIntentPendingTimer = 0f;
                twoHandFistResetTimer = 0f;
                twoHandFistResetLatched = false;
                wasTwoHandOrbitReleaseFrozen = false;
                wasTwoHandPanReleaseFrozen = false;

                bool eligibleForCarryPan = singleHandElapsed > TwoHandSingleHandUpgradeWindowSeconds
                                           && (activeInteractionHandSlot == 0 || activeInteractionHandSlot == 1)
                                           && ((activeInteractionMode == ActiveInteractionMode.CableDrag
                                                && cableRouter != null
                                                && cableRouter.IsDraggingCable)
                                               || (activeInteractionMode == ActiveInteractionMode.GateGrab
                                                   && grabbedGateSlotIndex >= 0
                                                   && grabbedGateSlotIndex < gateSlots.Count));
                if (eligibleForCarryPan)
                {
                    UpdateCarryPanHelper(dt);
                }
                else
                {
                    bool hadCarryPan = isCarryPanActive;
                    ResetCarryPanState(brakeStageWithoutCoast: hadCarryPan);
                    if (!hadCarryPan)
                    {
                        stageController?.SetTwoHandManipulationState(false);
                    }
                }
                return;
            }
        }
        else if (isCarryPanActive || carryPanConfirmTimer > 0f)
        {
            ResetCarryPanState(brakeStageWithoutCoast: isCarryPanActive);
        }

        // 1. Kelanjutan mode 2-tangan yang SUDAH terkunci (kebal tepi layar & dilarang loncat paksa antar-mode 2-tangan)
        if (activeInteractionMode == ActiveInteractionMode.TwoHandOrbitZoom)
        {
            twoHandIntentPendingTimer = 0f;
            bool primaryHoldPinch = primaryPurePinch || (!handGestureDetector.IsFistActive && handGestureDetector.PinchRatio < TwoHandOrbitHoldMaxPinchRatio);
            bool secondaryHoldPinch = secondaryPurePinch || (!secondaryHandGestureDetector.IsFistActive && secondaryHandGestureDetector.PinchRatio < TwoHandOrbitHoldMaxPinchRatio);
            if (primaryHoldPinch && secondaryHoldPinch)
            {
                StepTwoHandOrbitZoom(mainCam, dt, isStarting: false);
                return;
            }

            ExitInteractionMode(ActiveInteractionMode.TwoHandOrbitZoom, triggerNeutralCooldown: true);
            twoHandOrbitConfirmTimer = 0f;
            wasTwoHandOrbitReleaseFrozen = false;
            stageController?.SetTwoHandManipulationState(false);
            return;
        }

        if (activeInteractionMode == ActiveInteractionMode.TwoHandFreePan)
        {
            twoHandIntentPendingTimer = 0f;
            AirWeavingHandGestureDetector pinchDet = twoHandFreePanPrimaryIsPinch ? handGestureDetector : secondaryHandGestureDetector;
            AirWeavingHandGestureDetector fistDet = twoHandFreePanPrimaryIsPinch ? secondaryHandGestureDetector : handGestureDetector;
            bool holdPinchOk = !pinchDet.IsFistActive && (pinchDet.IsPinchActive || pinchDet.PinchRatio < TwoHandOrbitHoldMaxPinchRatio);
            bool holdFistOk = !fistDet.IsPinchActive
                              && (fistDet.IsFistActive || (fistDet.MeanWeightedBendDeg >= TwoHandFreePanHoldMinFistBendDeg && fistDet.FoldedFingerCount >= 2));
            if (holdPinchOk && holdFistOk)
            {
                StepTwoHandFreePan(twoHandFreePanPrimaryIsPinch, dt, isStarting: false);
                return;
            }

            ExitInteractionMode(ActiveInteractionMode.TwoHandFreePan, triggerNeutralCooldown: true);
            twoHandFreePanConfirmTimer = 0f;
            wasTwoHandPanReleaseFrozen = false;
            stageController?.SetTwoHandManipulationState(false);
            return;
        }

        if (activeInteractionMode == ActiveInteractionMode.TwoHandReset)
        {
            twoHandIntentPendingTimer = 0f;
            if (bothPureFists)
            {
                if (!twoHandFistResetLatched)
                {
                    stageController?.SetTwoHandManipulationState(true);
                    twoHandFistResetTimer += dt;
                    if (twoHandFistResetTimer >= TwoHandFistResetHoldSeconds)
                    {
                        twoHandFistResetTimer = 0f;
                        twoHandFistResetLatched = true;
                        stageController?.ResetViewToDefault(immediate: false);
                        if (spatialSfx != null && mainCam != null)
                        {
                            Vector3 midWorld = (handGestureDetector.PalmCursorWorldPosition + secondaryHandGestureDetector.PalmCursorWorldPosition) * 0.5f;
                            spatialSfx.PlayOutputHoldConfirm(midWorld);
                        }
                        SetFeedbackImmediate("Posisi, sudut, & skala meja hologram 3D direset ke bawaan.", FeedbackActiveColor, 2.0f);
                    }
                }
                else
                {
                    stageController?.SetTwoHandManipulationState(false);
                }
                return;
            }

            twoHandFistResetTimer = 0f;
            twoHandFistResetLatched = false;
            ExitInteractionMode(ActiveInteractionMode.TwoHandReset, triggerNeutralCooldown: true);
            stageController?.SetTwoHandManipulationState(false);
            return;
        }

        // 2. Transisi masuk dari Idle: gunakan IsTwoHandQualityGatePassing (longgar tepi layar [0.015, 0.985] tanpa syarat di atas hologram)
        if (neutralCooldownTimer > 0f
            || !handGestureDetector.IsTwoHandQualityGatePassing
            || !secondaryHandGestureDetector.IsTwoHandQualityGatePassing)
        {
            twoHandOrbitConfirmTimer = 0f;
            twoHandFreePanConfirmTimer = 0f;
            twoHandIntentPendingTimer = 0f;
            twoHandFistResetTimer = 0f;
            if (!bothPureFists)
            {
                twoHandFistResetLatched = false;
            }
            stageController?.SetTwoHandManipulationState(false);
            return;
        }

        // 2A. Masuk Both Pinch (Orbit 360° Diagonal Bebas & Focal-Zoom global): stabil >= 40 ms
        if (bothPurePinch)
        {
            twoHandFreePanConfirmTimer = 0f;
            twoHandFistResetTimer = 0f;
            twoHandIntentPendingTimer = 0f;
            twoHandOrbitConfirmTimer += dt;
            isTwoHandConfirming = true;
            if (twoHandOrbitConfirmTimer >= TwoHandOrbitConfirmHoldSeconds)
            {
                twoHandOrbitConfirmTimer = 0f;
                activeInteractionMode = ActiveInteractionMode.TwoHandOrbitZoom;
                activeInteractionHandSlot = -1;
                if (isRecordingAirDraw) CancelAirDrawStrokeImmediate();
                if (IsAnyCircleSummonActive) ResetAllCircleSummoners();
                StepTwoHandOrbitZoom(mainCam, dt, isStarting: true);
            }
            else
            {
                stageController?.SetTwoHandManipulationState(false);
            }
            return;
        }
        twoHandOrbitConfirmTimer = 0f;

        // 2B. Masuk Free Air-Pan 3D global (1 Pinch + 1 Fist murni): stabil >= 55 ms
        if (validFreePanPose)
        {
            twoHandFistResetTimer = 0f;
            twoHandIntentPendingTimer = 0f;
            twoHandFreePanConfirmTimer += dt;
            isTwoHandConfirming = true;
            if (twoHandFreePanConfirmTimer >= TwoHandFreePanConfirmHoldSeconds)
            {
                twoHandFreePanConfirmTimer = 0f;
                twoHandFreePanPrimaryIsPinch = primaryPinchSecondaryFist;
                activeInteractionMode = ActiveInteractionMode.TwoHandFreePan;
                activeInteractionHandSlot = -1;
                if (isRecordingAirDraw) CancelAirDrawStrokeImmediate();
                if (IsAnyCircleSummonActive) ResetAllCircleSummoners();
                StepTwoHandFreePan(twoHandFreePanPrimaryIsPinch, dt, isStarting: true);
            }
            else
            {
                stageController?.SetTwoHandManipulationState(false);
            }
            return;
        }
        twoHandFreePanConfirmTimer = 0f;

        // 2C. Masuk Both Fist (Reset Panggung 550 ms)
        if (bothPureFists && !twoHandFistResetLatched)
        {
            twoHandIntentPendingTimer = 0f;
            activeInteractionMode = ActiveInteractionMode.TwoHandReset;
            activeInteractionHandSlot = -1;
            twoHandFistResetTimer = 0f;
            if (isRecordingAirDraw) CancelAirDrawStrokeImmediate();
            if (IsAnyCircleSummonActive) ResetAllCircleSummoners();
            stageController?.SetTwoHandManipulationState(true);
            return;
        }

        // 2D. Pra-Niat Anti-Bajak: jika KEDUA tangan sedang sama-sama mulai menutup Pinch/Fist (asinkron ringan),
        //     tahan sementara (maks 220 ms) agar CableDrag/GateGrab 1 tangan tidak membajak niat 2 tangan.
        bool h0FormPinch = handGestureDetector.IsFormingPinchIntent;
        bool h1FormPinch = secondaryHandGestureDetector.IsFormingPinchIntent;
        bool h0FormFist = handGestureDetector.IsFormingFistIntent;
        bool h1FormFist = secondaryHandGestureDetector.IsFormingFistIntent;
        bool formingTwoHandCombo = (h0FormPinch && h1FormPinch)
                                   || (h0FormPinch && h1FormFist)
                                   || (h0FormFist && h1FormPinch)
                                   || (h0FormFist && h1FormFist);
        if (formingTwoHandCombo)
        {
            twoHandIntentPendingTimer += dt;
            if (twoHandIntentPendingTimer <= TwoHandIntentGraceMaxSeconds)
            {
                isTwoHandConfirming = true;
            }
        }
        else
        {
            twoHandIntentPendingTimer = 0f;
        }

        if (!bothPureFists)
        {
            twoHandFistResetLatched = false;
        }
        twoHandFistResetTimer = 0f;
        stageController?.SetTwoHandManipulationState(false);
    }

    /// <summary>
    /// Menghitung ukuran telapak rata-rata kedua tangan (<c>avgPalmScale</c>) berbasis <c>FilteredPalmSpan</c> kaku
    /// dengan penyaringan One-Euro 1D render-rate ringan agar bebas step 30 Hz dan tahan rotasi telapak. Zero-GC.
    /// </summary>
    private float SampleSmoothedTwoHandAvgPalmScale(float dt, bool resetFilter)
    {
        float span0 = Mathf.Clamp(handGestureDetector.FilteredPalmSpan, TwoHandMinPalmScale, 0.620f);
        float span1 = Mathf.Clamp(secondaryHandGestureDetector.FilteredPalmSpan, TwoHandMinPalmScale, 0.620f);
        float rawAvgPalmScale = 0.5f * (span0 + span1);
        if (resetFilter)
        {
            twoHandAvgPalmScaleFilter.Reset();
        }

        float safeDt = Mathf.Clamp(dt, 0.001f, 0.050f);
        float smoothed = twoHandAvgPalmScaleFilter.Filter(
            rawAvgPalmScale,
            safeDt,
            TwoHandAvgPalmOneEuroMinCutoff,
            TwoHandAvgPalmOneEuroBeta,
            TwoHandAvgPalmOneEuroDCutoff,
            TwoHandAvgPalmOneEuroVelScale);
        return Mathf.Clamp(smoothed, TwoHandMinPalmScale, 0.620f);
    }

    private void StepTwoHandOrbitZoom(Camera mainCam, float dt, bool isStarting)
    {
        // Koordinat sudah disatukan pada bidang sinar referensi konsisten (WorkbenchDepth)
        Vector2 p0 = handGestureDetector.PinchPointLocalXY;
        Vector2 p1 = secondaryHandGestureDetector.PinchPointLocalXY;
        Vector2 handVec = p1 - p0;
        float span2D = Mathf.Max(0.08f, handVec.magnitude);
        Vector2 curMid = (p0 + p1) * 0.5f;
        float curLineAngleDeg = Mathf.Atan2(handVec.y, handVec.x) * Mathf.Rad2Deg;

        // Kedalaman absolut 2 tangan (avgPalmScale) terpisah dari Zoom:
        // normalizedSpan = span2D / avgPalmScale memastikan maju-mundur kedua tangan dengan jarak fisik tetap TIDAK memicu zoom palsu
        float safeDt = Mathf.Clamp(dt, 0.001f, 0.050f);
        float avgPalmScale = SampleSmoothedTwoHandAvgPalmScale(safeDt, resetFilter: isStarting);
        float curNormSpan = span2D / Mathf.Max(avgPalmScale, TwoHandMinPalmScale);

        if (isStarting)
        {
            twoHandPinchInitialNormSpan = Mathf.Max(TwoHandZoomNormSpanInitialFloor, curNormSpan);
            twoHandPinchSmoothedNormSpan = curNormSpan;
            twoHandPinchInitialZoom = stageController != null ? stageController.TargetZoomScale : AirWeavingStageController.DefaultZoomScale;
            twoHandPinchPrevMidpointXY = curMid;
            twoHandPinchSmoothedLineAngleDeg = curLineAngleDeg;
            wasTwoHandOrbitReleaseFrozen = false;
            stageController?.BeginTwoHandManipulationAnchor(avgPalmScale, useFocusPointOrbit: true, focalMidpointCameraXY: curMid);
        }
        else
        {
            // Release-Intent Freeze Gate: hentikan seluruh injeksi delta (Orbit, Roll, Zoom, & Z-Axis) begitu salah satu tangan mulai membuka (>0.50f)
            bool releaseIntentFreeze = !handGestureDetector.IsPinchActive
                                       || !secondaryHandGestureDetector.IsPinchActive
                                       || handGestureDetector.PinchRatio > TwoHandPinchReleaseFreezeRatio
                                       || secondaryHandGestureDetector.PinchRatio > TwoHandPinchReleaseFreezeRatio
                                       || handGestureDetector.IsPinchReleaseIntent
                                       || secondaryHandGestureDetector.IsPinchReleaseIntent;
            if (releaseIntentFreeze)
            {
                wasTwoHandOrbitReleaseFrozen = true;
                stageController?.SetTwoHandReleaseFreeze(true);
                return;
            }

            if (wasTwoHandOrbitReleaseFrozen)
            {
                wasTwoHandOrbitReleaseFrozen = false;
                twoHandPinchPrevMidpointXY = curMid;
                twoHandPinchSmoothedLineAngleDeg = curLineAngleDeg;
                twoHandPinchInitialNormSpan = Mathf.Max(TwoHandZoomNormSpanInitialFloor, curNormSpan);
                twoHandPinchSmoothedNormSpan = curNormSpan;
                twoHandPinchInitialZoom = stageController != null ? stageController.TargetZoomScale : AirWeavingStageController.DefaultZoomScale;
                stageController?.ReanchorTwoHandDepth(avgPalmScale, refreshOrbitFocus: true, focalMidpointCameraXY: curMid);
                stageController?.SetTwoHandReleaseFreeze(false);
                return;
            }

            stageController?.SetTwoHandReleaseFreeze(false);

            Vector2 midDelta = curMid - twoHandPinchPrevMidpointXY;
            twoHandPinchPrevMidpointXY = curMid;

            // Quaternion Arcball 360° langsung pada sumbu layar kamera (camT.up, camT.right, camT.forward) secara pre-multiply:
            // bobot presisi 60 deg/unit & clamp maksimum per frame agar terasa berat, halus, dan anti-lonjakan.
            float yawDelta = Mathf.Clamp(
                -midDelta.x * TwoHandOrbitYawDegPerUnit,
                -TwoHandOrbitMaxAxisDeltaPerFrameDeg,
                TwoHandOrbitMaxAxisDeltaPerFrameDeg);
            float pitchDelta = Mathf.Clamp(
                midDelta.y * TwoHandOrbitPitchDegPerUnit,
                -TwoHandOrbitMaxAxisDeltaPerFrameDeg,
                TwoHandOrbitMaxAxisDeltaPerFrameDeg);

            // Kemiringan sudut garis antar kedua tangan (roll camT.forward): redaman halus berdasarkan jarak pisah tangan
            // agar tetap stabil saat kedua tangan rapat di satu sisi layar maupun lebar di tepi kiri-kanan.
            float rollDelta = 0f;
            float rollSepWeight = Mathf.Clamp01((span2D - TwoHandRollMinSepDist) / (TwoHandRollFullSepDist - TwoHandRollMinSepDist));
            if (rollSepWeight > 0.001f)
            {
                float rollAlpha = 1f - Mathf.Exp(-TwoHandOrbitRollEmaRate * safeDt);
                float angleErr = Mathf.DeltaAngle(twoHandPinchSmoothedLineAngleDeg, curLineAngleDeg);
                float smoothedStepDeg = angleErr * rollAlpha;
                twoHandPinchSmoothedLineAngleDeg += smoothedStepDeg;
                rollDelta = Mathf.Clamp(
                    smoothedStepDeg * TwoHandOrbitRollGain * rollSepWeight,
                    -TwoHandOrbitRollMaxDeltaPerFrameDeg,
                    TwoHandOrbitRollMaxDeltaPerFrameDeg);
            }
            else
            {
                twoHandPinchSmoothedLineAngleDeg = curLineAngleDeg;
            }

            // Ultra-Wide Zoom multiplikatif/logaritmik (0.18x..15.0x) berbasis normalizedSpan terkompensasi kedalaman (span2D / avgPalmScale):
            // kecepatan zoom terasa 100% seragam baik di 0.2x maupun di 12.0x (simetris di ruang logaritmik).
            float zoomAlpha = 1f - Mathf.Exp(-TwoHandZoomDistEmaRate * safeDt);
            twoHandPinchSmoothedNormSpan = Mathf.Lerp(twoHandPinchSmoothedNormSpan, curNormSpan, zoomAlpha);
            float rawZoomRatio = (Mathf.Max(TwoHandZoomNormSpanMinFloor, twoHandPinchSmoothedNormSpan) + TwoHandZoomNormSpanSoftFloor)
                                 / (Mathf.Max(TwoHandZoomNormSpanInitialFloor, twoHandPinchInitialNormSpan) + TwoHandZoomNormSpanSoftFloor);
            float logZoomRatio = Mathf.Log(Mathf.Max(0.05f, rawZoomRatio));
            float effectiveLogZoom = 0f;
            if (Mathf.Abs(logZoomRatio) > TwoHandZoomRatioMicroDeadzone)
            {
                effectiveLogZoom = logZoomRatio - (Mathf.Sign(logZoomRatio) * TwoHandZoomRatioMicroDeadzone);
            }

            float uncappedZoom = twoHandPinchInitialZoom * Mathf.Exp(effectiveLogZoom * TwoHandZoomSensitivity);
            float prevTargetZoom = stageController != null ? Mathf.Max(0.05f, stageController.TargetZoomScale) : twoHandPinchInitialZoom;
            float maxZoomStepFactor = Mathf.Exp(TwoHandZoomMaxLogStepPerFrame);
            float desiredZoom = Mathf.Clamp(
                uncappedZoom,
                prevTargetZoom / maxZoomStepFactor,
                prevTargetZoom * maxZoomStepFactor);

            stageController?.ApplyTwoHandOrbitAndZoom(
                yawDelta,
                pitchDelta,
                desiredZoom,
                curMid,
                avgPalmScale,
                rollDelta,
                mainCam != null ? mainCam.transform : null,
                safeDt);
        }
    }

    private void StepTwoHandFreePan(bool primaryPinchSecondaryFist, float dt, bool isStarting)
    {
        Vector2 handA = primaryPinchSecondaryFist
            ? handGestureDetector.PinchPointLocalXY
            : handGestureDetector.PalmCursorLocalXY;
        Vector2 handB = primaryPinchSecondaryFist
            ? secondaryHandGestureDetector.PalmCursorLocalXY
            : secondaryHandGestureDetector.PinchPointLocalXY;
        Vector2 curMid = (handA + handB) * 0.5f;
        float safeDt = Mathf.Clamp(dt, 0.001f, 0.050f);
        float avgPalmScale = SampleSmoothedTwoHandAvgPalmScale(safeDt, resetFilter: isStarting);

        if (isStarting)
        {
            twoHandPanPrevMidpointXY = curMid;
            wasTwoHandPanReleaseFrozen = false;
            stageController?.BeginTwoHandManipulationAnchor(avgPalmScale, useFocusPointOrbit: false, focalMidpointCameraXY: curMid);
        }
        else
        {
            AirWeavingHandGestureDetector pinchDet = primaryPinchSecondaryFist ? handGestureDetector : secondaryHandGestureDetector;
            AirWeavingHandGestureDetector fistDet = primaryPinchSecondaryFist ? secondaryHandGestureDetector : handGestureDetector;
            bool releaseIntentFreeze = !pinchDet.IsPinchActive
                                       || !fistDet.IsFistActive
                                       || pinchDet.PinchRatio > TwoHandPinchReleaseFreezeRatio
                                       || pinchDet.IsPinchReleaseIntent
                                       || fistDet.MeanWeightedBendDeg < TwoHandFistReleaseFreezeMaxBendDeg
                                       || fistDet.FoldedFingerCount <= 2;
            if (releaseIntentFreeze)
            {
                wasTwoHandPanReleaseFrozen = true;
                stageController?.SetTwoHandReleaseFreeze(true);
                return;
            }

            if (wasTwoHandPanReleaseFrozen)
            {
                wasTwoHandPanReleaseFrozen = false;
                twoHandPanPrevMidpointXY = curMid;
                stageController?.ReanchorTwoHandDepth(avgPalmScale, refreshOrbitFocus: false, focalMidpointCameraXY: curMid);
                stageController?.SetTwoHandReleaseFreeze(false);
                return;
            }

            stageController?.SetTwoHandReleaseFreeze(false);

            // Skalakan pan X/Y terhadap kedalaman Z observasi aktual agar geseran 1:1 dengan tangan baik saat dekat (1.15m) maupun jauh (9.50m)
            float stageDepthRatio = stageController != null
                ? Mathf.Clamp(stageController.CurrentStageDepthZ / WorkbenchDepth, 0.30f, 2.55f)
                : 1f;
            Vector2 panDeltaXY = (curMid - twoHandPanPrevMidpointXY) * (TwoHandFreePanXYGain * stageDepthRatio);
            float maxDeltaXY = TwoHandFreePanMaxDeltaXYPerFrame * Mathf.Clamp(stageDepthRatio, 0.50f, 2.55f);
            float panDeltaMag = panDeltaXY.magnitude;
            if (panDeltaMag > maxDeltaXY)
            {
                panDeltaXY *= maxDeltaXY / panDeltaMag;
            }
            twoHandPanPrevMidpointXY = curMid;

            stageController?.ApplyTwoHandFreePan(panDeltaXY, avgPalmScale, safeDt);
        }
    }

    /// <summary>
    /// Menyaring <c>FilteredPalmSpan</c> 1 tangan Tangan 2 dengan One-Euro 1D lebih tenang untuk Carry-Pan Z
    /// agar geseran X/Y biasa atau kemiringan pergelangan tidak memicu goyangan kedalaman palsu. Zero-GC.
    /// </summary>
    private float SampleSmoothedCarryPanPalmScale(AirWeavingHandGestureDetector helperDet, float dt, bool resetFilter)
    {
        float rawPalmSpan = Mathf.Clamp(helperDet.FilteredPalmSpan, TwoHandMinPalmScale, 0.620f);
        if (resetFilter)
        {
            carryPanPalmScaleFilter.Reset();
        }

        float safeDt = Mathf.Clamp(dt, 0.001f, 0.050f);
        float smoothed = carryPanPalmScaleFilter.Filter(
            rawPalmSpan,
            safeDt,
            CarryPanPalmOneEuroMinCutoff,
            CarryPanPalmOneEuroBeta,
            CarryPanPalmOneEuroDCutoff,
            CarryPanPalmOneEuroVelScale);
        return Mathf.Clamp(smoothed, TwoHandMinPalmScale, 0.620f);
    }

    /// <summary>
    /// Mengelola sub-state simetris Carry-Pan Tangan Kedua (1 Pinch + 1 Fist pasca &gt; 190 ms):
    /// - CableDrag stabil (Tangan 1 Pinch) + Tangan 2 Fist -> Carry-Pan X/Y/Z.
    /// - GateGrab stabil (Tangan 1 Fist) + Tangan 2 Pinch -> Carry-Pan X/Y/Z (0% bentrok Both Fist).
    /// Masuk/keluarnya Tangan 2 tidak pernah menyentuh activeInteractionMode maupun sticky lock Tangan 1. Zero-GC.
    /// </summary>
    private void UpdateCarryPanHelper(float dt)
    {
        int ownerSlot = activeInteractionHandSlot;
        int helperSlot = 1 - ownerSlot;
        AirWeavingHandGestureDetector ownerDet = GetHandDetector(ownerSlot);
        AirWeavingHandGestureDetector helperDet = GetHandDetector(helperSlot);
        bool helperMissed = helperSlot == 0 ? primaryMissedLastInference : secondaryMissedLastInference;
        bool wantHelperFist = activeInteractionMode == ActiveInteractionMode.CableDrag;

        // Jika Tangan 1 baru saja melepas carry pada frame ini, tutup Carry-Pan bersih sebelum commit/drop Tangan 1
        bool ownerStillHolding = wantHelperFist ? ownerDet.IsPinchActive : ownerDet.IsFistActive;
        if (!ownerStillHolding)
        {
            bool hadCarryPan = isCarryPanActive;
            ResetCarryPanState(brakeStageWithoutCoast: hadCarryPan);
            if (!hadCarryPan)
            {
                stageController?.SetTwoHandManipulationState(false);
            }
            return;
        }

        if (isCarryPanActive)
        {
            if (carryPanHandSlot != helperSlot || carryPanUsesFist != wantHelperFist)
            {
                ResetCarryPanState(brakeStageWithoutCoast: true);
                return;
            }

            bool holdHelperOk;
            if (carryPanUsesFist)
            {
                holdHelperOk = !helperDet.IsPinchActive
                               && (helperDet.IsFistActive
                                   || (helperDet.MeanWeightedBendDeg >= CarryPanFistHoldMinBendDeg
                                       && helperDet.FoldedFingerCount >= 2));
            }
            else
            {
                holdHelperOk = !helperDet.IsFistActive
                               && (helperDet.IsPinchActive
                                   || helperDet.PinchRatio < TwoHandOrbitHoldMaxPinchRatio);
            }

            if (holdHelperOk)
            {
                StepCarryPan(helperDet, carryPanUsesFist, helperMissed, dt, isStarting: false);
                return;
            }

            ResetCarryPanState(brakeStageWithoutCoast: true);
            return;
        }

        bool helperPoseMatches = wantHelperFist
            ? (helperDet.IsFistActive && !helperDet.IsPinchActive)
            : (helperDet.IsPinchActive && !helperDet.IsFistActive);

        if (!helperMissed && helperDet.IsTwoHandQualityGatePassing && helperPoseMatches)
        {
            carryPanConfirmTimer += dt;
            if (carryPanConfirmTimer >= CarryPanConfirmHoldSeconds)
            {
                carryPanConfirmTimer = 0f;
                isCarryPanActive = true;
                carryPanHandSlot = helperSlot;
                carryPanUsesFist = wantHelperFist;
                ResetAutoEdgePanState(brakeStageImmediate: false);
                StepCarryPan(helperDet, carryPanUsesFist, helperMissed: false, dt, isStarting: true);
                return;
            }
        }
        else
        {
            carryPanConfirmTimer = 0f;
        }

        stageController?.SetTwoHandManipulationState(false);
    }

    private void StepCarryPan(
        AirWeavingHandGestureDetector helperDet,
        bool usesFist,
        bool helperMissed,
        float dt,
        bool isStarting)
    {
        Vector2 curHelperXY = usesFist ? helperDet.PalmCursorLocalXY : helperDet.PinchPointLocalXY;
        float safeDt = Mathf.Clamp(dt, 0.001f, 0.050f);
        float helperPalmScale = SampleSmoothedCarryPanPalmScale(helperDet, safeDt, resetFilter: isStarting);

        if (isStarting)
        {
            carryPanPrevHandXY = curHelperXY;
            wasCarryPanReleaseFrozen = false;
            wasCarryPanOcclusionPaused = false;
            stageController?.BeginTwoHandManipulationAnchor(helperPalmScale, useFocusPointOrbit: false, focalMidpointCameraXY: curHelperXY);
            return;
        }

        // 1. Kedipan occlusion singkat (helperMissed): jeda injeksi delta sementara TANPA ReanchorTwoHandDepth()
        //    agar progres tarikan mundur Z tidak pernah terhapus saat tangan kembali terdeteksi.
        if (helperMissed)
        {
            wasCarryPanOcclusionPaused = true;
            stageController?.SetTwoHandReleaseFreeze(true);
            return;
        }

        // 2. Anti-Kick Release Gate murni (hanya saat Tangan 2 benar-benar mulai membuka gestur):
        //    Untuk Fist di Carry-Pan, gunakan ambang longgar (< 76° atau <= 1 jari) agar kepalan yang mengecil saat mundur tidak palsu ter-freeze.
        bool releaseIntentFreeze;
        if (usesFist)
        {
            releaseIntentFreeze = !helperDet.IsFistActive
                                  || helperDet.MeanWeightedBendDeg < CarryPanFistReleaseFreezeMaxBendDeg
                                  || helperDet.FoldedFingerCount <= 1;
        }
        else
        {
            releaseIntentFreeze = !helperDet.IsPinchActive
                                  || helperDet.PinchRatio > TwoHandPinchReleaseFreezeRatio
                                  || helperDet.IsPinchReleaseIntent;
        }

        if (releaseIntentFreeze)
        {
            wasCarryPanReleaseFrozen = true;
            stageController?.SetTwoHandReleaseFreeze(true);
            return;
        }

        if (wasCarryPanReleaseFrozen)
        {
            wasCarryPanReleaseFrozen = false;
            wasCarryPanOcclusionPaused = false;
            carryPanPrevHandXY = curHelperXY;
            stageController?.ReanchorTwoHandDepth(helperPalmScale, refreshOrbitFocus: false, focalMidpointCameraXY: curHelperXY);
            stageController?.SetTwoHandReleaseFreeze(false);
            return;
        }

        if (wasCarryPanOcclusionPaused)
        {
            wasCarryPanOcclusionPaused = false;
            // Sejajarkan XY saja agar tidak menyentak pasca frame drop, tetapi pertahankan jangkar Z asli agar gerak mundur tetap mulus
            carryPanPrevHandXY = curHelperXY;
        }

        stageController?.SetTwoHandReleaseFreeze(false);

        // Direct manipulation 100% konsisten dengan TwoHandFreePan:
        // tarik Tangan 2 ke kiri -> targetPanOffset.x berkurang (tarik sisi kanan papan masuk layar);
        // mundurkan Tangan 2 menjauhi kamera -> palmScale mengecil -> targetPanOffset.z bertambah (objek belakang kamera maju ke depan).
        float stageDepthRatio = stageController != null
            ? Mathf.Clamp(stageController.CurrentStageDepthZ / WorkbenchDepth, 0.30f, 2.55f)
            : 1f;
        Vector2 panDeltaXY = (curHelperXY - carryPanPrevHandXY) * (CarryPanXYGain * stageDepthRatio);
        float maxDeltaXY = CarryPanMaxDeltaXYPerFrame * Mathf.Clamp(stageDepthRatio, 0.50f, 2.55f);
        float panDeltaMag = panDeltaXY.magnitude;
        if (panDeltaMag > maxDeltaXY)
        {
            panDeltaXY *= maxDeltaXY / panDeltaMag;
        }
        carryPanPrevHandXY = curHelperXY;

        stageController?.ApplyCarryPan(panDeltaXY, helperPalmScale, safeDt);
    }

    /// <summary>
    /// Auto Edge-Pan 1 Tangan saat CableDrag atau GateGrab stabil (&gt; 190 ms) &amp; Carry-Pan tidak aktif:
    /// - Strip tepi 12% kiri/kanan/atas/bawah dalam koordinat viewport frustum kamera (aman rasio 16:9 &amp; 20:9).
    /// - Dwell 95 ms + ramp-in 110 ms + kecepatan kuadratik halus terkalibrasi CurrentStageDepthZ &amp; OBB pan bounds.
    /// - Panggung bergeser berlawanan arah tepi kursor (kursor kanan -> papan geser kiri; kursor atas -> papan turun).
    /// - Pengaman Anti-Kabur Wajib: otomatis pause + BrakeEdgePanImmediate saat kabel snapped ke soket,
    ///   Tangan 1 occlusion grace (missedLastInference), atau mulai membuka gestur. Zero-GC.
    /// </summary>
    private void UpdateSingleHandAutoEdgePan(Camera mainCam, float dt)
    {
        bool overlayOrVerify = IsAnyUiOverlayOpen || (levelManager != null && levelManager.IsVerifying);
        if (overlayOrVerify
            || mainCam == null
            || stageController == null
            || isCarryPanActive
            || carryPanConfirmTimer > 0f
            || IsAnyTwoHandModeActive)
        {
            ResetAutoEdgePanState(brakeStageImmediate: isAutoEdgePanActive && !isCarryPanActive && !IsAnyTwoHandModeActive);
            return;
        }

        bool isCarryingCable = activeInteractionMode == ActiveInteractionMode.CableDrag
                               && cableRouter != null
                               && cableRouter.IsDraggingCable;
        bool isCarryingGate = activeInteractionMode == ActiveInteractionMode.GateGrab
                              && grabbedGateSlotIndex >= 0
                              && grabbedGateSlotIndex < gateSlots.Count;
        if ((!isCarryingCable && !isCarryingGate) || (activeInteractionHandSlot != 0 && activeInteractionHandSlot != 1))
        {
            ResetAutoEdgePanState(brakeStageImmediate: isAutoEdgePanActive);
            return;
        }

        float singleHandElapsed = Time.unscaledTime - singleHandModeEnteredTime;
        if (singleHandElapsed <= TwoHandSingleHandUpgradeWindowSeconds)
        {
            ResetAutoEdgePanState(brakeStageImmediate: false);
            return;
        }

        int ownerSlot = activeInteractionHandSlot;
        bool ownerDetected = IsHandSlotDetected(ownerSlot);
        bool ownerMissed = ownerSlot == 0 ? primaryMissedLastInference : secondaryMissedLastInference;
        AirWeavingHandGestureDetector ownerDet = GetHandDetector(ownerSlot);

        bool pausedBySnap = isCarryingCable && cableRouter.IsSnappedTargetPort;
        bool pausedByOcclusion = !ownerDetected || ownerMissed;
        bool pausedByReleaseIntent = isCarryingCable
            ? (!ownerDet.IsPinchActive || ownerDet.PinchRatio > EdgePanCableReleasePauseRatio)
            : (!ownerDet.IsFistActive || ownerDet.MeanWeightedBendDeg < TwoHandFistReleaseFreezeMaxBendDeg);

        if (pausedBySnap || pausedByOcclusion || pausedByReleaseIntent)
        {
            ResetAutoEdgePanState(brakeStageImmediate: isAutoEdgePanActive);
            return;
        }

        Vector2 cursorRayXY = isCarryingCable ? ownerDet.PinchPointLocalXY : ownerDet.PalmCursorLocalXY;
        float halfFovRad = mainCam.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float aspect = mainCam.aspect > 0.1f ? mainCam.aspect : (16f / 9f);
        float frustumHalfH = WorkbenchDepth * Mathf.Tan(halfFovRad);
        float frustumHalfW = frustumHalfH * aspect;
        if (frustumHalfH <= 0.01f || frustumHalfW <= 0.01f)
        {
            ResetAutoEdgePanState(brakeStageImmediate: isAutoEdgePanActive);
            return;
        }

        float viewportX = 0.5f + (0.5f * Mathf.Clamp(cursorRayXY.x / frustumHalfW, -1f, 1f));
        float viewportY = 0.5f + (0.5f * Mathf.Clamp(cursorRayXY.y / frustumHalfH, -1f, 1f));

        float rawPushX = 0f;
        if (viewportX < EdgePanStripFraction)
        {
            rawPushX = -Mathf.Clamp01((EdgePanStripFraction - viewportX) / EdgePanStripFraction);
        }
        else if (viewportX > (1f - EdgePanStripFraction))
        {
            rawPushX = Mathf.Clamp01((viewportX - (1f - EdgePanStripFraction)) / EdgePanStripFraction);
        }

        float rawPushY = 0f;
        if (viewportY < EdgePanStripFraction)
        {
            rawPushY = -Mathf.Clamp01((EdgePanStripFraction - viewportY) / EdgePanStripFraction);
        }
        else if (viewportY > (1f - EdgePanStripFraction))
        {
            rawPushY = Mathf.Clamp01((viewportY - (1f - EdgePanStripFraction)) / EdgePanStripFraction);
        }

        Vector2 rawEdgePush = new Vector2(rawPushX, rawPushY);
        float edgeMag = rawEdgePush.magnitude;
        if (edgeMag <= 0.005f)
        {
            ResetAutoEdgePanState(brakeStageImmediate: isAutoEdgePanActive);
            return;
        }

        float safeDt = Mathf.Clamp(dt, 0.0005f, 0.050f);
        edgePanDwellTimer += safeDt;
        if (edgePanDwellTimer < EdgePanDwellSeconds)
        {
            isAutoEdgePanActive = false;
            return;
        }

        float rampT = Mathf.Clamp01((edgePanDwellTimer - EdgePanDwellSeconds) / EdgePanRampInSeconds);
        float clampedMag = Mathf.Min(1f, edgeMag);
        Vector2 quadPush = (rawEdgePush / edgeMag) * (clampedMag * clampedMag);
        float stageDepthRatio = Mathf.Clamp(stageController.CurrentStageDepthZ / WorkbenchDepth, 0.30f, 2.55f);
        float panSpeed = EdgePanMaxSpeedAtRefDepth * stageDepthRatio * rampT;

        // Panggung bergeser berlawanan arah tepi kursor:
        // kursor kanan (quadPush.x > 0) -> panDeltaXY.x < 0 (papan ke kiri); kursor atas (quadPush.y > 0) -> panDeltaXY.y < 0 (papan turun)
        Vector2 panDeltaXY = -quadPush * (panSpeed * safeDt);
        stageController.ApplyContinuousEdgePanXY(panDeltaXY);
        isAutoEdgePanActive = true;
    }

    private void UpdateWorkbenchInteractions(Camera mainCam, float dt)
    {
        float aspectScale = ComputeHorizontalAspectScale(mainCam);

        // 0. Jeda seluruh gestur tangan 1-kanal selama laci menu / modal UI / verifikasi / kartu panduan terbuka atau mode lain terkunci di Arbiter
        bool overlayOrVerify = IsAnyUiOverlayOpen || (levelManager != null && levelManager.IsVerifying);
        bool blockedByOtherArbiterMode = activeInteractionMode != ActiveInteractionMode.Idle
                                         && activeInteractionMode != ActiveInteractionMode.GateGrab
                                         && activeInteractionMode != ActiveInteractionMode.InputSwitch;
        if (overlayOrVerify || blockedByOtherArbiterMode)
        {
            if (overlayOrVerify && grabbedGateSlotIndex >= 0 && grabbedGateSlotIndex < gateSlots.Count)
            {
                ReleaseGrabbedGate(gateSlots[grabbedGateSlotIndex], aspectScale);
                grabbedGateSlotIndex = -1;
                ExitInteractionMode(ActiveInteractionMode.GateGrab, triggerNeutralCooldown: false);
            }
            if (overlayOrVerify && activeInteractionMode == ActiveInteractionMode.InputSwitch)
            {
                ExitInteractionMode(ActiveInteractionMode.InputSwitch, triggerNeutralCooldown: false);
            }
            CancelAirDrawStrokeImmediate();
            hoveredGateSlotIndex = -1;
            secondaryHoveredGateSlotIndex = -1;
            hoveredInputNode = null;
            secondaryHoveredInputNode = null;
            inputADwellTimers[0] = 0f;
            inputADwellTimers[1] = 0f;
            inputBDwellTimers[0] = 0f;
            inputBDwellTimers[1] = 0f;
            ClearInputPushHoverState(0);
            ClearInputPushHoverState(1);
            inputSwitchMagnetOffsetBySlot[0] = Vector2.zero;
            inputSwitchMagnetOffsetBySlot[1] = Vector2.zero;
            isPinching = false;
            wasPinching = false;
            SetHoverRingVisible(false);
            return;
        }

        // 1. Kelola seret badan gerbang 3D nyata (Mode Meja Catur 3D) memakai slot tangan yang mengunci GateGrab (0=primer / 1=sekunder)
        if (grabbedGateSlotIndex >= 0 && grabbedGateSlotIndex < gateSlots.Count)
        {
            if (isRecordingAirDraw)
            {
                CancelAirDrawStrokeImmediate();
            }

            int grabSlot = (activeInteractionHandSlot == 0 || activeInteractionHandSlot == 1) ? activeInteractionHandSlot : 0;
            AirWeavingHandGestureDetector grabDetector = GetHandDetector(grabSlot);
            bool grabHandDetected = IsHandSlotDetected(grabSlot);
            bool grabFistActive = grabDetector.IsFistActive;
            Vector2 grabPalmXY = grabDetector.PalmCursorLocalXY;
            Vector3 grabPalmWorldPos = grabDetector.PalmCursorWorldPosition;

            WorkbenchGateSlot grabbed = gateSlots[grabbedGateSlotIndex];
            float safeGrabDt = Mathf.Clamp(dt, 0.0001f, 0.050f);
            if (grabFistActive)
            {
                isPinching = true;
                float liftedTargetHeightY = grabbed.smoothedDragTargetY;

                if (grabHandDetected && mainCam != null)
                {
                    Transform camT = mainCam.transform;
                    Vector3 camPos = camT.position;
                    Vector3 handRayDir = grabPalmWorldPos - camPos;
                    if (handRayDir.sqrMagnitude < 0.00001f)
                    {
                        handRayDir = camT.forward;
                    }
                    else
                    {
                        handRayDir.Normalize();
                    }

                    // Hitung target jarak kamera 3D logaritmik simetris (Monocular Z-Depth Tahap 8):
                    // Tangan didorong maju ke layar (PalmSpan membesar) -> logDelta positif -> objek terdorong masuk/turun mendekati dek;
                    // tangan mundur menjauhi layar -> logDelta negatif -> objek terangkat naik dalam ruang 3D.
                    float safeMinGrabDepth = Mathf.Max(MinGrabCameraDepth, mainCam.nearClipPlane + HandNearClipMinOffsetMeters);
                    float safeMaxGrabDepth = Mathf.Min(MaxGrabCameraDepth, VideoQuadDepth - HandMaxBehindVideoQuadMarginMeters);
                    float currentFilteredPalmSpan = Mathf.Max(0.055f, grabDetector.FilteredPalmSpan);
                    float grabLogDelta = Mathf.Log(currentFilteredPalmSpan / Mathf.Max(0.055f, grabStartPalmSpan));
                    float absGrabLog = Mathf.Abs(grabLogDelta);
                    float effGrabLog = absGrabLog > FistGrabDepthLogDeadzone
                        ? (grabLogDelta - (Mathf.Sign(grabLogDelta) * FistGrabDepthLogDeadzone))
                        : 0f;
                    float baseGrabDepth = grabStartCameraDepth + (effGrabLog * FistGrabDepthGainMeters);

                    // Kompensasi kedalaman mulus saat panggung bergeser X/Y/Z oleh Carry-Pan atau Auto Edge-Pan
                    // agar gerbang tetap menempel di bawah kepalan Tangan 1 tanpa melompat vertikal/kedalaman
                    if (stageController != null)
                    {
                        Vector3 curStagePan = stageController.CurrentPanOffset;
                        if (!hasLastGrabStagePanOffset)
                        {
                            lastGrabStagePanOffset = curStagePan;
                            hasLastGrabStagePanOffset = true;
                        }
                        else
                        {
                            Vector3 deltaStagePan = curStagePan - lastGrabStagePanOffset;
                            lastGrabStagePanOffset = curStagePan;
                            if (deltaStagePan.sqrMagnitude > 1e-8f)
                            {
                                Transform stageRootForComp = stageController.StageRoot;
                                float depthCompDelta;
                                if (stageRootForComp != null)
                                {
                                    Vector3 rayDirCam = camT.InverseTransformDirection(handRayDir);
                                    Vector3 planeNormalCam = camT.InverseTransformDirection(stageRootForComp.up);
                                    float denom = Vector3.Dot(rayDirCam, planeNormalCam);
                                    if (Mathf.Abs(denom) > 0.22f)
                                    {
                                        depthCompDelta = Mathf.Clamp(Vector3.Dot(deltaStagePan, planeNormalCam) / denom, -0.80f, 0.80f);
                                    }
                                    else
                                    {
                                        depthCompDelta = Mathf.Clamp(deltaStagePan.z, -0.80f, 0.80f);
                                    }
                                }
                                else
                                {
                                    depthCompDelta = Mathf.Clamp(deltaStagePan.z, -0.80f, 0.80f);
                                }
                                grabStageDepthCompMeters = Mathf.Clamp(
                                    grabStageDepthCompMeters + depthCompDelta,
                                    safeMinGrabDepth - baseGrabDepth,
                                    safeMaxGrabDepth - baseGrabDepth);
                            }
                        }
                    }

                    float targetCamDepth = Mathf.Clamp(baseGrabDepth + grabStageDepthCompMeters, safeMinGrabDepth, safeMaxGrabDepth);
                    Vector3 targetWorldPos = camPos + (handRayDir * targetCamDepth);

                    Transform stageRoot = stageController != null ? stageController.StageRoot : null;
                    Vector2 targetStageXZ;
                    if (stageRoot != null)
                    {
                        Vector3 stageLocal3D = stageRoot.InverseTransformPoint(targetWorldPos);
                        targetStageXZ = new Vector2(stageLocal3D.x, stageLocal3D.z);
                        float defaultLiftedY = ComputeArenaBaseHoverHeightY(targetStageXZ, aspectScale, grabbed.slotIndex) + GrabExtraLiftHeight;
                        // Beri bobot elevasi 3D nyata (0.90) agar maju-mundur tangan mengatur tinggi/kedalaman gerbang secara langsung
                        liftedTargetHeightY = Mathf.Clamp(Mathf.Lerp(defaultLiftedY, stageLocal3D.y, 0.90f), MinGrabTableLocalY, MaxGrabTableLocalY);
                    }
                    else
                    {
                        targetStageXZ = grabPalmXY;
                        liftedTargetHeightY = ComputeArenaBaseHoverHeightY(targetStageXZ, aspectScale, grabbed.slotIndex) + GrabExtraLiftHeight;
                    }

                    Vector2 clampedTarget = ClampDraggedGateLocalXY(targetStageXZ, aspectScale);
                    float targetDampAlpha = 1f - Mathf.Exp(-GateDragTargetDampRate * safeGrabDt);
                    grabbed.smoothedDragTargetXZ = Vector2.Lerp(grabbed.smoothedDragTargetXZ, clampedTarget, targetDampAlpha);
                    grabbed.smoothedDragTargetY = Mathf.Lerp(grabbed.smoothedDragTargetY, liftedTargetHeightY, targetDampAlpha);

                    grabbed.currentLocalXY = StepCriticallyDampedSpring2D(
                        grabbed.currentLocalXY,
                        grabbed.smoothedDragTargetXZ,
                        ref grabbed.velocityXY,
                        GateDragSpringOmega,
                        safeGrabDt);
                }
                else
                {
                    grabbed.currentLocalXY = StepCriticallyDampedSpring2D(
                        grabbed.currentLocalXY,
                        grabbed.smoothedDragTargetXZ,
                        ref grabbed.velocityXY,
                        GateDragSpringOmega,
                        safeGrabDt);
                }

                // Micro-Feedback Tahap 9 (Zero-GC): inersia miring halus (sway/banking 2-sumbu) berbasis kecepatan tersaring
                float velSmoothAlpha = 1f - Mathf.Exp(-GateDragVelocitySmoothRate * safeGrabDt);
                grabbed.smoothedVelocityXY = Vector2.Lerp(grabbed.smoothedVelocityXY, grabbed.velocityXY, velSmoothAlpha);
                Vector2 targetTilt = new Vector2(
                    Mathf.Clamp(grabbed.smoothedVelocityXY.y * DragVelocityTiltDegPerUnitSec, -MaxDragInertiaTiltDeg, MaxDragInertiaTiltDeg),
                    Mathf.Clamp(grabbed.smoothedVelocityXY.x * DragVelocityTiltDegPerUnitSec, -MaxDragInertiaTiltDeg, MaxDragInertiaTiltDeg));
                grabbed.currentInertiaTiltDeg = StepCriticallyDampedSpring2D(
                    grabbed.currentInertiaTiltDeg,
                    targetTilt,
                    ref grabbed.velocityTiltDeg,
                    GateGrabTiltSpringOmega,
                    safeGrabDt);

                grabbed.currentLocalZ = StepCriticallyDampedSpring(
                    grabbed.currentLocalZ,
                    grabbed.smoothedDragTargetY,
                    ref grabbed.velocityZ,
                    GateLiftZSpringOmega,
                    safeGrabDt);
                grabbed.currentScale = StepCriticallyDampedSpring(
                    grabbed.currentScale,
                    GrabbedGateScale,
                    ref grabbed.velocityScale,
                    GateScaleSpringOmega,
                    safeGrabDt);
                grabbed.isPlacedOnWorkbench = true;

                bool curOutsideBox = IsOutsideAssemblyZone(grabbed.currentLocalXY, aspectScale);
                if (curOutsideBox != wasGrabbedGateInDeleteZone)
                {
                    wasGrabbedGateInDeleteZone = curOutsideBox;
                    if (spatialSfx != null && grabbed.node != null)
                    {
                        if (curOutsideBox)
                        {
                            spatialSfx.PlayDeleteZoneEnter(grabbed.node.transform.position);
                        }
                        else
                        {
                            spatialSfx.PlayDeleteZoneExit(grabbed.node.transform.position);
                        }
                    }
                }
            }
            else
            {
                isPinching = false;
                ReleaseGrabbedGate(grabbed, aspectScale);
                grabbedGateSlotIndex = -1;
                ExitInteractionMode(ActiveInteractionMode.GateGrab, triggerNeutralCooldown: true);
            }

            hoveredGateSlotIndex = grabSlot == 0 ? grabbedGateSlotIndex : -1;
            secondaryHoveredGateSlotIndex = grabSlot == 1 ? grabbedGateSlotIndex : -1;
            hoveredInputNode = null;
            secondaryHoveredInputNode = null;
            inputADwellTimers[0] = 0f;
            inputADwellTimers[1] = 0f;
            inputBDwellTimers[0] = 0f;
            inputBDwellTimers[1] = 0f;
            ClearInputPushHoverState(0);
            ClearInputPushHoverState(1);
            DecayInputSwitchMagnetOffset(0, dt);
            DecayInputSwitchMagnetOffset(1, dt);
            wasPinching = isPinching;
            SetHoverRingVisible(false);
            return;
        }

        if (activeInteractionMode == ActiveInteractionMode.GateGrab)
        {
            ExitInteractionMode(ActiveInteractionMode.GateGrab, triggerNeutralCooldown: true);
        }

        // 2. Tidak sedang memegang gerbang: sinkronkan status Kepal ambidextrous
        isPinching = (isHandDetected && handGestureDetector.IsFistActive)
                     || (isSecondaryHandDetected && secondaryHandGestureDetector.IsFistActive);

        if (!IsAnyHandDetected)
        {
            if (isRecordingAirDraw)
            {
                CompleteOrCancelAirDrawStroke(mainCam, aspectScale);
            }
            if (activeInteractionMode == ActiveInteractionMode.InputSwitch)
            {
                ExitInteractionMode(ActiveInteractionMode.InputSwitch, triggerNeutralCooldown: false);
            }
            hoveredGateSlotIndex = -1;
            secondaryHoveredGateSlotIndex = -1;
            hoveredInputNode = null;
            secondaryHoveredInputNode = null;
            ResetInputTouchState();
            wasPinching = false;
            SetHoverRingVisible(false);
            return;
        }

        // 2B. Prioritas Drag Kabel & Summon Aktif: badan gerbang & Input A/B mengalah
        if ((cableRouter != null && cableRouter.IsDraggingCable) || IsAnyCircleSummonActive)
        {
            if (isRecordingAirDraw)
            {
                CancelAirDrawStrokeImmediate();
            }
            if (activeInteractionMode == ActiveInteractionMode.InputSwitch)
            {
                ExitInteractionMode(ActiveInteractionMode.InputSwitch, triggerNeutralCooldown: false);
            }
            hoveredGateSlotIndex = -1;
            secondaryHoveredGateSlotIndex = -1;
            hoveredInputNode = null;
            secondaryHoveredInputNode = null;
            inputADwellTimers[0] = 0f;
            inputADwellTimers[1] = 0f;
            inputBDwellTimers[0] = 0f;
            inputBDwellTimers[1] = 0f;
            ClearInputPushHoverState(0);
            ClearInputPushHoverState(1);
            DecayInputSwitchMagnetOffset(0, dt);
            DecayInputSwitchMagnetOffset(1, dt);
            wasPinching = isPinching;
            SetHoverRingVisible(false);
            return;
        }

        // 3. Evaluasi Ambidextrous untuk KEDUA TANGAN (slot 0 = primer DAN slot 1 = sekunder)
        //    Saat Idle: kedua tangan dievaluasi penuh untuk hover gerbang, sakelar Input A/B, & rising-edge Fist GateGrab.
        //    Tangan mana pun yang lebih dulu memicu gestur langsung mengunci activeInteractionHandSlot sampai selesai/dilepas.
        hoveredGateSlotIndex = -1;
        secondaryHoveredGateSlotIndex = -1;
        hoveredInputNode = null;
        secondaryHoveredInputNode = null;

        for (int handSlot = 0; handSlot < 2; handSlot++)
        {
            bool slotDetected = IsHandSlotDetected(handSlot);
            AirWeavingHandGestureDetector det = GetHandDetector(handSlot);
            bool lockedToOtherHand = activeInteractionMode == ActiveInteractionMode.InputSwitch
                                     && activeInteractionHandSlot >= 0
                                     && activeInteractionHandSlot != handSlot;

            if (!slotDetected || det.IsPinchActive || det.IsFormingPinchIntent || lockedToOtherHand)
            {
                if (!slotDetected)
                {
                    inputARequiresExitRearmBySlot[handSlot] = false;
                    inputBRequiresExitRearmBySlot[handSlot] = false;
                }
                inputADwellTimers[handSlot] = 0f;
                inputBDwellTimers[handSlot] = 0f;
                ClearInputPushHoverState(handSlot);
                DecayInputSwitchMagnetOffset(handSlot, dt);
                if (activeInteractionMode == ActiveInteractionMode.InputSwitch && activeInteractionHandSlot == handSlot)
                {
                    ExitInteractionMode(ActiveInteractionMode.InputSwitch, triggerNeutralCooldown: false);
                }
                continue;
            }

            Vector2 palmXY = det.PalmCursorLocalXY;
            Vector2 aimXY = det.FistJustStarted ? det.StabilizedCursorLocalXY : palmXY;
            Vector3 aimWorldPos = mainCam != null
                ? mainCam.transform.TransformPoint(new Vector3(aimXY.x, aimXY.y, WorkbenchDepth))
                : det.PalmCursorWorldPosition;

            // Zona badan Output menang prioritas atas hover gerbang di dekatnya saat tidak sedang menggenggam
            bool cursorInOutputZone = IsCursorInsideOutputPriorityZone(mainCam, aimWorldPos, aspectScale, det);
            int slotHoveredGate;
            if (cursorInOutputZone || activeInteractionMode == ActiveInteractionMode.InputSwitch)
            {
                if (isRecordingAirDraw)
                {
                    CancelAirDrawStrokeImmediate();
                }
                slotHoveredGate = -1;
            }
            else
            {
                slotHoveredGate = FindNearestGateSlotIndex(mainCam, aimWorldPos, GateHoverGrabRadius, ParkedGateHoverRadius);
            }

            if (handSlot == 0) hoveredGateSlotIndex = slotHoveredGate;
            else secondaryHoveredGateSlotIndex = slotHoveredGate;

            // 4. Sentuh ringan ujung telunjuk (#8) pada sakelar Input A & Input B untuk tangan ini
            GateNode slotHoveredInput = null;
            if (slotHoveredGate < 0)
            {
                HandleInputIndexTouchForSlot(handSlot, det, mainCam, aspectScale, dt, out slotHoveredInput);
            }
            else
            {
                inputADwellTimers[handSlot] = 0f;
                inputBDwellTimers[handSlot] = 0f;
                ClearInputPushHoverState(handSlot);
                DecayInputSwitchMagnetOffset(handSlot, dt);
                if (activeInteractionMode == ActiveInteractionMode.InputSwitch && activeInteractionHandSlot == handSlot)
                {
                    ExitInteractionMode(ActiveInteractionMode.InputSwitch, triggerNeutralCooldown: false);
                }
            }

            if (handSlot == 0) hoveredInputNode = slotHoveredInput;
            else secondaryHoveredInputNode = slotHoveredInput;

            // 5. Mulai genggam badan gerbang pada rising-edge Open -> Fist oleh tangan mana pun yang lebih dulu memicu
            bool canBeginGateGrab = activeInteractionMode == ActiveInteractionMode.Idle
                                    && neutralCooldownTimer <= 0f
                                    && !isTwoHandConfirming
                                    && !det.IsFormingPinchIntent
                                    && det.IsQualityGatePassing
                                    && det.CursorSpeedUnitsPerSec <= GateGrabEnterMaxSpeed;
            if (canBeginGateGrab && det.FistJustStarted && slotHoveredGate >= 0 && slotHoveredGate < gateSlots.Count)
            {
                BeginGateGrabWithHandSlot(handSlot, det, slotHoveredGate, mainCam, aspectScale);
                break;
            }
        }

        if (grabbedGateSlotIndex < 0 && EnableLegacyAirDraw && isHandDetected)
        {
            // Air-Draw lama dicadangkan nonaktif di Fase 3 (siap dikembangkan ulang di Fase 4)
            UpdateAirDrawInteraction(mainCam, aspectScale, dt);
        }

        wasPinching = isPinching;
        SetHoverRingVisible(false);
    }

    private void BeginGateGrabWithHandSlot(
        int handSlot,
        AirWeavingHandGestureDetector grabDetector,
        int targetGateSlotIndex,
        Camera mainCam,
        float aspectScale)
    {
        CancelAirDrawStrokeImmediate();
        grabbedGateSlotIndex = targetGateSlotIndex;
        activeInteractionMode = ActiveInteractionMode.GateGrab;
        activeInteractionHandSlot = handSlot;
        singleHandModeEnteredTime = Time.unscaledTime;

        hoveredGateSlotIndex = handSlot == 0 ? targetGateSlotIndex : -1;
        secondaryHoveredGateSlotIndex = handSlot == 1 ? targetGateSlotIndex : -1;
        hoveredInputNode = null;
        secondaryHoveredInputNode = null;

        WorkbenchGateSlot grabbed = gateSlots[grabbedGateSlotIndex];
        preGrabWasPlaced = grabbed.isPlacedOnWorkbench;
        preGrabPlacedLocalXY = grabbed.placedLocalXY;
        preGrabCurrentLocalXY = grabbed.currentLocalXY;
        preGrabLocalZ = grabbed.currentLocalZ;
        preGrabScale = grabbed.currentScale;
        grabbed.smoothedDragTargetXZ = grabbed.currentLocalXY;
        grabbed.smoothedDragTargetY = grabbed.currentLocalZ;

        grabStartPalmSpan = Mathf.Max(0.055f, grabDetector.FilteredPalmSpan);
        if (grabbed.node != null && mainCam != null)
        {
            grabStartCameraDepth = Mathf.Clamp(
                Vector3.Distance(mainCam.transform.position, grabbed.node.transform.position),
                MinGrabCameraDepth,
                MaxGrabCameraDepth);
        }
        else
        {
            grabStartCameraDepth = WorkbenchDepth;
        }
        grabStageDepthCompMeters = 0f;
        if (stageController != null)
        {
            lastGrabStagePanOffset = stageController.CurrentPanOffset;
            hasLastGrabStagePanOffset = true;
        }
        else
        {
            lastGrabStagePanOffset = Vector3.zero;
            hasLastGrabStagePanOffset = false;
        }
        grabbed.isPlacedOnWorkbench = true;
        grabbed.coastVelocityXY = Vector2.zero;
        grabbed.isCoasting = false;
        grabbed.scalePop.Trigger(GateGrabPopAmplitude);

        if (grabbed.node != null && mainCam != null)
        {
            float gatePhase = 1.15f + (grabbed.slotIndex * 1.27f);
            float arcYaw = ComputeArenaArcYawDeg(grabbed.currentLocalXY.x, aspectScale);
            float stageZoom = stageController != null ? stageController.CurrentZoomScale : 1f;
            ComputeStageNodePose(
                mainCam.transform,
                new Vector3(grabbed.currentLocalXY.x, grabbed.currentLocalZ, grabbed.currentLocalXY.y),
                arcYaw,
                gatePhase,
                isGrabbed: true,
                grabbed.currentInertiaTiltDeg,
                out Vector3 worldPos,
                out Quaternion worldRot);
            grabbed.node.transform.localScale = Vector3.one * (grabbed.currentScale * stageZoom);
            grabbed.node.SetExternalSpatialPose(true, worldPos, worldRot);
            grabbed.node.TriggerTapFeedback();
            if (spatialSfx != null)
            {
                spatialSfx.PlayGateGrab(worldPos);
            }
        }
        else if (grabbed.node != null && spatialSfx != null)
        {
            spatialSfx.PlayGateGrab(grabbed.node.transform.position);
        }
        hoverSfxPostActionSuppressTimer = HoverPostActionSuppressSeconds;
        lastHoverSfxCategory = HoverSfxCategory.GateBody;
        lastHoverSfxNode = grabbed.node;
        lastHoverSfxSubId = grabbed.slotIndex + 1;
        hoverSfxNoneGraceTimer = 0f;
        wasGrabbedGateInDeleteZone = IsOutsideAssemblyZone(grabbed.currentLocalXY, aspectScale);
    }

    private bool IsCursorInsideOutputPriorityZone(
        Camera mainCam,
        Vector3 cursorWorldPos,
        float aspectScale,
        AirWeavingHandGestureDetector detector)
    {
        if (grabbedGateSlotIndex >= 0 || postDropLockoutTimer > 0f)
        {
            return false;
        }
        if ((cableRouter != null && cableRouter.IsDraggingCable) || (detector != null && detector.IsPinchActive))
        {
            return false;
        }
        // Hold-to-verify dinonaktifkan: zona Output hanya memprioritaskan tahan-kepal setelah rangkaian live benar-benar LULUS 4/4
        if (levelManager == null || !levelManager.IsReadyForNextLevel || !isLiveCircuitPassedAllFour || levelManager.IsSandboxMode || levelManager.IsVerifying || IsAnyUiOverlayOpen)
        {
            return false;
        }

        float zoomScale;
        Vector2 outputCenterRayXY;
        if (outputNode != null)
        {
            zoomScale = ComputeNodeApparentRayScale(mainCam, outputNode.transform.position, MinScreenSpaceHitboxScale, 6.0f, out bool outBehind, out float outVisualScale);
            if (outBehind)
            {
                return false;
            }
            outputCenterRayXY = ProjectWorldToCameraRayPlane(mainCam, outputNode.transform.position) + new Vector2(0.03f * outVisualScale, 0.10f * outVisualScale);
        }
        else
        {
            zoomScale = stageController != null ? Mathf.Clamp(stageController.CurrentZoomScale, MinScreenSpaceHitboxScale, 6.0f) : 1f;
            outputCenterRayXY = new Vector2((OutputTerminalBaseX * aspectScale) + 0.03f, OutputTerminalY + 0.10f);
        }
        Vector2 cursorRayXY = ProjectWorldToCameraRayPlane(mainCam, cursorWorldPos);
        return Vector2.Distance(cursorRayXY, outputCenterRayXY) <= (OutputZonePriorityRadius * zoomScale);
    }

    private void UpdateAirDrawInteraction(Camera mainCam, float aspectScale, float dt)
    {
        bool blockedByHigherPriority = isPinching
                                       || rawPinching
                                       || hoveredGateSlotIndex >= 0
                                       || hoveredInputNode != null
                                       || IsCursorInsideOutputPriorityZone(mainCam, indexTipWorldPosition, aspectScale, handGestureDetector)
                                       || (levelManager != null && levelManager.IsVerifying);

        if (blockedByHigherPriority)
        {
            if (isRecordingAirDraw)
            {
                CancelAirDrawStrokeImmediate();
            }
            return;
        }

        bool insideStartZone = IsInsideAirDrawZone(indexWorkbenchLocalXY, aspectScale, margin: 0.06f);
        bool insideContinueZone = IsInsideAirDrawZone(indexWorkbenchLocalXY, aspectScale, margin: 0.24f);

        if (!isRecordingAirDraw)
        {
            if (isIndexOnlyWandPose && insideStartZone && airDrawPostSpawnCooldown <= 0f)
            {
                StartAirDrawStroke(indexWorkbenchLocalXY);
                UpdateAirDrawStrokeRenderer(mainCam);
            }
            return;
        }

        // Sedang merekam goresan Air-Draw
        if (!isIndexOnlyWandPose || !insideContinueZone)
        {
            CompleteOrCancelAirDrawStroke(mainCam, aspectScale);
            return;
        }

        if (airDrawPointsLocal.Count < MaxAirDrawPoints)
        {
            Vector2 lastPt = airDrawPointsLocal[airDrawPointsLocal.Count - 1];
            if (Vector2.Distance(lastPt, indexWorkbenchLocalXY) >= AirDrawMinPointSpacing)
            {
                airDrawPointsLocal.Add(indexWorkbenchLocalXY);
            }
        }

        // Cek dwell (ujung telunjuk ditahan diam ~0.38 dtk setelah goresan cukup panjang)
        if (Vector2.Distance(indexWorkbenchLocalXY, airDrawDwellAnchorLocal) <= AirDrawDwellMoveTolerance)
        {
            airDrawDwellTimer += dt;
        }
        else
        {
            airDrawDwellTimer = 0f;
            airDrawDwellAnchorLocal = indexWorkbenchLocalXY;
        }

        UpdateAirDrawStrokeRenderer(mainCam);

        bool enoughPointsAndSpan = airDrawPointsLocal.Count >= AirDrawMinPoints
                                   && UnistrokeGestureRecognizer.ComputeStrokeMaxSpan(airDrawPointsLocal) >= AirDrawMinSpan;
        if (enoughPointsAndSpan && airDrawDwellTimer >= AirDrawDwellCompleteSeconds)
        {
            CompleteOrCancelAirDrawStroke(mainCam, aspectScale);
        }
    }

    private void StartAirDrawStroke(Vector2 startLocalXY)
    {
        isRecordingAirDraw = true;
        airDrawFadeTimer = 0f;
        airDrawPointsLocal.Clear();
        airDrawPointsLocal.Add(startLocalXY);
        airDrawDwellTimer = 0f;
        airDrawDwellAnchorLocal = startLocalXY;

        if (airDrawStrokeRenderer != null)
        {
            airDrawStrokeRenderer.enabled = true;
            airDrawStrokeRenderer.widthMultiplier = 0.022f;
            airDrawStrokeRenderer.startColor = AirDrawStrokeStartColor;
            airDrawStrokeRenderer.endColor = AirDrawStrokeEndColor;
            if (airDrawStrokeMat != null)
            {
                airDrawStrokeMat.color = Color.white;
            }
        }
    }

    private void CompleteOrCancelAirDrawStroke(Camera mainCam, float aspectScale)
    {
        isRecordingAirDraw = false;
        airDrawDwellTimer = 0f;

        int pointCount = airDrawPointsLocal.Count;
        float maxSpan = UnistrokeGestureRecognizer.ComputeStrokeMaxSpan(airDrawPointsLocal);
        if (pointCount < AirDrawMinPoints || maxSpan < AirDrawMinSpan)
        {
            CancelAirDrawStrokeImmediate();
            return;
        }

        UnistrokeRecognitionResult result = gestureRecognizer.Recognize(
            airDrawPointsLocal,
            AirDrawMinSpan,
            AirDrawConfidenceThreshold);

        if (result.matched && TrySpawnGateFromAirDraw(result, mainCam, aspectScale))
        {
            airDrawFadeWasSuccess = true;
            airDrawFadeTimer = AirDrawFadeDurationSeconds;
            airDrawPostSpawnCooldown = AirDrawPostSpawnCooldownSeconds;
            int pct = Mathf.Clamp(Mathf.RoundToInt(result.confidence * 100f), 1, 100);
            SetFeedbackImmediate(
                $"[Air-Draw] Pola '{result.symbolLabel}' dikenali -> Gerbang {result.gateType} dipanggil! ({pct}%)",
                FeedbackPinchColor,
                1.65f);
        }
        else
        {
            airDrawFadeWasSuccess = false;
            airDrawFadeTimer = AirDrawFadeDurationSeconds;
            airDrawPostSpawnCooldown = 0.25f;
            SetFeedbackImmediate(
                "[Air-Draw] Pola belum dikenali — gambar tegas ^ (AND), O (OR), Z (XOR), V (NAND), atau | (NOT).",
                FeedbackWaitingColor,
                1.05f);
        }
    }

    private bool TrySpawnGateFromAirDraw(UnistrokeRecognitionResult recognition, Camera mainCam, float aspectScale)
    {
        WorkbenchGateSlot targetSlot = null;
        for (int i = 0; i < gateSlots.Count; i++)
        {
            if (gateSlots[i] != null && gateSlots[i].node != null && gateSlots[i].node.nodeType == recognition.gateType)
            {
                targetSlot = gateSlots[i];
                break;
            }
        }

        if (targetSlot == null || targetSlot.node == null)
        {
            return false;
        }

        Vector2 strokeCenter = UnistrokeGestureRecognizer.ComputeStrokeBoundsCenter(airDrawPointsLocal);
        Vector2 spawnXY = ClampAssemblyZoneLocalXY(strokeCenter, aspectScale);

        targetSlot.isPlacedOnWorkbench = true;
        targetSlot.placedLocalXY = spawnXY;
        targetSlot.currentLocalXY = spawnXY;
        targetSlot.currentLocalZ = ComputeArenaBaseHoverHeightY(spawnXY, aspectScale);
        targetSlot.currentScale = ParkedGateScale;
        targetSlot.ResetSpringState();
        targetSlot.scalePop.Trigger(GatePlaceBloomPopAmplitude);

        if (mainCam != null)
        {
            float gatePhase = 1.15f + (targetSlot.slotIndex * 1.27f);
            float arcYaw = ComputeArenaArcYawDeg(spawnXY.x, aspectScale);
            float stageZoom = stageController != null ? stageController.CurrentZoomScale : 1f;
            ComputeStageNodePose(
                mainCam.transform,
                new Vector3(spawnXY.x, targetSlot.currentLocalZ, spawnXY.y),
                arcYaw,
                gatePhase,
                isGrabbed: false,
                Vector2.zero,
                out Vector3 worldPos,
                out Quaternion worldRot);
            targetSlot.node.transform.localScale = Vector3.one * (ActiveGateScale * stageZoom);
            targetSlot.node.SetExternalSpatialPose(true, worldPos, worldRot);
        }

        targetSlot.node.TriggerTapFeedback();
        targetSlot.node.TriggerEnergyBurst(AirDrawSuccessFlashColor, 1.15f);
        return true;
    }

    private void UpdateAirDrawStrokeRenderer(Camera mainCam)
    {
        if (airDrawStrokeRenderer == null || mainCam == null)
        {
            return;
        }

        int count = Mathf.Min(airDrawPointsLocal.Count, MaxAirDrawPoints);
        if (count < 2)
        {
            airDrawStrokeRenderer.positionCount = 0;
            return;
        }

        Transform camT = mainCam.transform;
        float strokeZ = WorkbenchDepth - 0.12f;
        for (int i = 0; i < count; i++)
        {
            Vector2 pt = airDrawPointsLocal[i];
            airDrawWorldBuffer[i] = camT.TransformPoint(new Vector3(pt.x, pt.y, strokeZ));
        }

        airDrawStrokeRenderer.positionCount = count;
        airDrawStrokeRenderer.SetPositions(airDrawWorldBuffer);
    }

    private void UpdateAirDrawStrokeFade(Camera mainCam, float dt)
    {
        if (airDrawFadeTimer <= 0f || airDrawStrokeRenderer == null)
        {
            return;
        }

        airDrawFadeTimer = Mathf.Max(0f, airDrawFadeTimer - dt);
        float t = airDrawFadeTimer / AirDrawFadeDurationSeconds;
        Color flashBase = airDrawFadeWasSuccess ? AirDrawSuccessFlashColor : AirDrawFailFlashColor;
        Color faded = new Color(flashBase.r, flashBase.g, flashBase.b, flashBase.a * t);

        airDrawStrokeRenderer.enabled = t > 0.01f;
        airDrawStrokeRenderer.widthMultiplier = Mathf.Lerp(0.004f, airDrawFadeWasSuccess ? 0.032f : 0.020f, t);
        airDrawStrokeRenderer.startColor = faded;
        airDrawStrokeRenderer.endColor = faded;
        UpdateAirDrawStrokeRenderer(mainCam);

        if (airDrawFadeTimer <= 0f)
        {
            airDrawPointsLocal.Clear();
            airDrawStrokeRenderer.positionCount = 0;
            airDrawStrokeRenderer.enabled = false;
        }
    }

    private void CancelAirDrawStrokeImmediate()
    {
        isRecordingAirDraw = false;
        airDrawDwellTimer = 0f;
        airDrawFadeTimer = 0f;
        airDrawPointsLocal.Clear();

        if (airDrawStrokeRenderer != null)
        {
            airDrawStrokeRenderer.positionCount = 0;
            airDrawStrokeRenderer.enabled = false;
        }
    }

    private static bool IsInsideAirDrawZone(Vector2 localXY, float aspectScale, float margin)
    {
        float minX = (AssemblyMinBaseX - margin) * aspectScale;
        float maxX = (AssemblyMaxBaseX + margin) * aspectScale;
        float minY = AssemblyMinY - margin;
        float maxY = Mathf.Min(ParkReturnThresholdY - 0.02f, AssemblyMaxY + margin);
        return localXY.x >= minX && localXY.x <= maxX && localXY.y >= minY && localXY.y <= maxY;
    }

    private void ReleaseGrabbedGate(WorkbenchGateSlot slot, float aspectScale)
    {
        if (slot == null || slot.node == null)
        {
            return;
        }

        postDropLockoutTimer = PostDropOutputLockoutSeconds;
        Vector2 homeXY = GetParkingSlotLocalXY(slot.slotIndex, aspectScale);
        float distToHome = Vector2.Distance(slot.currentLocalXY, homeXY);
        bool outsideAssembly = IsOutsideAssemblyZone(slot.currentLocalXY, aspectScale);
        bool shouldReturnToRack = outsideAssembly
                                  || slot.currentLocalXY.y > ParkReturnThresholdY
                                  || distToHome <= ParkSlotSnapRadius;

        // Batasi kecepatan lemparan terakhir agar efek meluncur/kembali ke rak tetap mulus & terkendali (Tahap 4.9)
        Vector2 throwVel = slot.smoothedVelocityXY;
        float throwSpeedSqr = throwVel.sqrMagnitude;
        if (throwSpeedSqr > GateThrowMaxSpeed * GateThrowMaxSpeed)
        {
            throwVel *= GateThrowMaxSpeed / Mathf.Sqrt(throwSpeedSqr);
        }

        if (shouldReturnToRack)
        {
            Vector3 releaseWorldPos = slot.node.transform.position;
            slot.isPlacedOnWorkbench = false;
            slot.placedLocalXY = homeXY;
            slot.coastVelocityXY = Vector2.zero;
            slot.isCoasting = false;
            slot.velocityXY = throwVel;
            slot.scalePop.Reset();

            Camera mainCam = ResolveMainCamera();
            if (mainCam != null)
            {
                float gatePhase = 1.15f + (slot.slotIndex * 1.27f);
                float arcYaw = ComputeParkingArcYawDeg(slot.slotIndex);
                float stageZoom = stageController != null ? stageController.CurrentZoomScale : 1f;
                ComputeStageNodePose(
                    mainCam.transform,
                    new Vector3(slot.currentLocalXY.x, slot.currentLocalZ, slot.currentLocalXY.y),
                    arcYaw,
                    gatePhase,
                    isGrabbed: false,
                    slot.currentInertiaTiltDeg,
                    out Vector3 currentWorldPos,
                    out Quaternion worldRot);
                slot.node.SetExternalParkedPreview(true, currentWorldPos, worldRot, slot.currentScale * stageZoom);
            }

            // Putus seluruh kabel gerbang & reset status uji manual (perubahan topologi otomatis mereset manualPassedRowMask = 0)
            cableRouter?.RemoveWiresForNode(slot.node, circuitManager);
            if (circuitManager != null)
            {
                circuitManager.EvaluateCircuit();
            }
            ResetLiveDebugMirror();
            if (!IsAnyUiOverlayOpen)
            {
                if (spatialSfx != null)
                {
                    spatialSfx.PlayOutsideBoxDelete(releaseWorldPos);
                }
                gestureGuide?.NotifyGateDeletedOutsideZone();
            }
        }
        else
        {
            // Magnetic Grid Snap + Auto-Separate Zero-GC (Tahap 9):
            // 100% identik dengan pratinjau cincin grid saat digenggam; pendaratan halus lewat pegas tanpa coasting liar
            Vector2 snappedAssembly = ResolveSnappedAutoSeparateGridXZ(slot.currentLocalXY, slot.slotIndex, aspectScale);
            slot.isPlacedOnWorkbench = true;
            slot.placedLocalXY = snappedAssembly;
            slot.coastVelocityXY = Vector2.zero;
            slot.isCoasting = false;
            slot.velocityXY = throwVel * 0.35f;
            slot.scalePop.Trigger(GatePlaceBloomPopAmplitude);
            slot.node.TriggerTapFeedback();
            if (circuitManager != null)
            {
                circuitManager.EvaluateCircuit();
            }
            if (spatialSfx != null && !IsAnyUiOverlayOpen)
            {
                spatialSfx.PlayGatePlace(slot.node.transform.position);
            }
        }
        hoverSfxPostActionSuppressTimer = HoverPostActionSuppressSeconds;
        wasGrabbedGateInDeleteZone = false;
    }

    private void ResetInputTouchState()
    {
        for (int s = 0; s < 2; s++)
        {
            inputADwellTimers[s] = 0f;
            inputBDwellTimers[s] = 0f;
            inputARequiresExitRearmBySlot[s] = false;
            inputBRequiresExitRearmBySlot[s] = false;
            inputAReadyForFistBySlot[s] = false;
            inputBReadyForFistBySlot[s] = false;
            inputPushHoverTargetBySlot[s] = null;
            inputPushProgressBySlot[s] = 0f;
            inputSwitchMagnetOffsetBySlot[s] = Vector2.zero;
        }
        inputATouchRippleTimer = 0f;
        inputBTouchRippleTimer = 0f;
        inputAScalePop.Reset();
        inputBScalePop.Reset();
    }

    private void ClearInputPushHoverState(int handSlot)
    {
        inputPushHoverTargetBySlot[handSlot] = null;
        inputPushProgressBySlot[handSlot] = 0f;
        inputAReadyForFistBySlot[handSlot] = false;
        inputBReadyForFistBySlot[handSlot] = false;
    }

    private void DecayInputSwitchMagnetOffset(int handSlot, float dt)
    {
        float blendAlpha = 1f - Mathf.Exp(-SoftMagnetOffsetBlendSpeed * Mathf.Max(0.0001f, dt));
        inputSwitchMagnetOffsetBySlot[handSlot] = Vector2.Lerp(inputSwitchMagnetOffsetBySlot[handSlot], Vector2.zero, blendAlpha);
    }

    private bool IsTipInsideInputSwitchZone(Camera mainCam, Vector3 tipWorldPos, float aspectScale)
    {
        Vector2 tipRayXY = ProjectWorldToCameraRayPlane(mainCam, tipWorldPos);
        float fallbackScale = stageController != null ? Mathf.Clamp(stageController.CurrentZoomScale, MinScreenSpaceHitboxScale, 6.0f) : 1f;

        bool insideA = false;
        if (inputANode != null)
        {
            float scaleA = ComputeNodeApparentRayScale(mainCam, inputANode.transform.position, MinScreenSpaceHitboxScale, 6.0f, out bool behindA, out float visualScaleA);
            if (!behindA)
            {
                Vector2 inputARayXY = ProjectWorldToCameraRayPlane(mainCam, inputANode.transform.position) - new Vector2(0.04f * visualScaleA, 0f);
                insideA = Vector2.Distance(tipRayXY, inputARayXY) <= (InputAirTapRearmRadius * scaleA);
            }
        }
        else
        {
            Vector2 inputARayXY = new Vector2((InputTerminalBaseX * aspectScale) - 0.04f, InputATerminalY);
            insideA = Vector2.Distance(tipRayXY, inputARayXY) <= (InputAirTapRearmRadius * fallbackScale);
        }

        if (insideA)
        {
            return true;
        }

        if (inputBNode != null)
        {
            float scaleB = ComputeNodeApparentRayScale(mainCam, inputBNode.transform.position, MinScreenSpaceHitboxScale, 6.0f, out bool behindB, out float visualScaleB);
            if (!behindB)
            {
                Vector2 inputBRayXY = ProjectWorldToCameraRayPlane(mainCam, inputBNode.transform.position) - new Vector2(0.04f * visualScaleB, 0f);
                return Vector2.Distance(tipRayXY, inputBRayXY) <= (InputAirTapRearmRadius * scaleB);
            }
            return false;
        }

        Vector2 fallbackInputBXY = new Vector2((InputTerminalBaseX * aspectScale) - 0.04f, InputBTerminalY);
        return Vector2.Distance(tipRayXY, fallbackInputBXY) <= (InputAirTapRearmRadius * fallbackScale);
    }

    /// <summary>
    /// Sakelar Input A &amp; Input B berbasis kepal (<c>Fist</c>) dengan Edge-Trigger Aman (Tahap 9, Ambidextrous):
    /// - Hanya ter-arm saat tangan terbuka (<c>!IsFistActive</c>) berada di dalam radius terminal Input A/B,
    ///   sehingga tangan yang sudah mengepal dari luar sekadar melintas TIDAK pernah memicu sakelar.
    /// - Konfirmasi kepal mantap ~70 ms, otomatis rearm saat kepalan dibuka atau tangan keluar radius rearm.
    /// - Dipanggil hanya saat <c>slotHoveredGate &lt; 0</c> sehingga 0% bentrok dengan <c>GateGrab</c>. Zero-GC.
    /// </summary>
    private void HandleInputIndexTouchForSlot(
        int handSlot,
        AirWeavingHandGestureDetector detector,
        Camera mainCam,
        float aspectScale,
        float dt,
        out GateNode slotHoveredInput)
    {
        slotHoveredInput = null;
        bool isOwner = activeInteractionMode == ActiveInteractionMode.InputSwitch && activeInteractionHandSlot == handSlot;

        if (levelManager != null && levelManager.IsVerifying)
        {
            inputADwellTimers[handSlot] = 0f;
            inputBDwellTimers[handSlot] = 0f;
            ClearInputPushHoverState(handSlot);
            DecayInputSwitchMagnetOffset(handSlot, dt);
            if (isOwner)
            {
                ExitInteractionMode(ActiveInteractionMode.InputSwitch, triggerNeutralCooldown: false);
            }
            return;
        }

        float fallbackScale = stageController != null ? Mathf.Clamp(stageController.CurrentZoomScale, MinScreenSpaceHitboxScale, 6.0f) : 1f;
        Vector2 rawPalmXY = detector.PalmCursorLocalXY;

        bool inputABehind = false;
        float scaleA = fallbackScale;
        Vector2 inputAXY;
        if (inputANode != null)
        {
            scaleA = ComputeNodeApparentRayScale(mainCam, inputANode.transform.position, MinScreenSpaceHitboxScale, 6.0f, out inputABehind, out float visualScaleA);
            inputAXY = ProjectWorldToCameraRayPlane(mainCam, inputANode.transform.position) - new Vector2(0.03f * visualScaleA, 0f);
        }
        else
        {
            inputAXY = new Vector2((InputTerminalBaseX * aspectScale) - 0.03f, InputATerminalY);
        }

        bool inputBBehind = false;
        float scaleB = fallbackScale;
        Vector2 inputBXY;
        if (inputBNode != null)
        {
            scaleB = ComputeNodeApparentRayScale(mainCam, inputBNode.transform.position, MinScreenSpaceHitboxScale, 6.0f, out inputBBehind, out float visualScaleB);
            inputBXY = ProjectWorldToCameraRayPlane(mainCam, inputBNode.transform.position) - new Vector2(0.03f * visualScaleB, 0f);
        }
        else
        {
            inputBXY = new Vector2((InputTerminalBaseX * aspectScale) - 0.03f, InputBTerminalY);
        }

        float rawDistA = inputABehind ? float.MaxValue : Vector2.Distance(rawPalmXY, inputAXY);
        float rawDistB = inputBBehind ? float.MaxValue : Vector2.Distance(rawPalmXY, inputBXY);
        float scaledRearmRadiusA = InputAirTapRearmRadius * scaleA;
        float scaledRearmRadiusB = InputAirTapRearmRadius * scaleB;
        float scaledMagnetRadiusA = InputSwitchMagnetRadius * scaleA;
        float scaledMagnetRadiusB = InputSwitchMagnetRadius * scaleB;
        float scaledTapRadiusA = InputAirTapRadius * scaleA;
        float scaledTapRadiusB = InputAirTapRadius * scaleB;

        // Logika Exit-to-Rearm & Edge-Trigger Reset 2D WAJIB memakai posisi telapak mentah agar tidak pernah nyangkut oleh magnet
        if (rawDistA > scaledRearmRadiusA)
        {
            inputARequiresExitRearmBySlot[handSlot] = false;
            inputAReadyForFistBySlot[handSlot] = false;
        }
        if (rawDistB > scaledRearmRadiusB)
        {
            inputBRequiresExitRearmBySlot[handSlot] = false;
            inputBReadyForFistBySlot[handSlot] = false;
        }

        // Gerbang Quality Gate, Exclusive Arbiter, & Anti-Pinch (cegah tangan saat transisi mode 2 tangan / cubit kabel)
        float palmSpeed = detector.CursorSpeedUnitsPerSec;
        bool gateAllowed = !detector.IsPinchActive
                           && !detector.IsPrePinchSuppressingSummon
                           && detector.IsQualityGatePassing
                           && !isTwoHandConfirming
                           && (isOwner || (activeInteractionMode == ActiveInteractionMode.Idle && neutralCooldownTimer <= 0f));
        bool speedAllowed = palmSpeed <= InputSwitchEnterMaxSpeed;

        if (!gateAllowed)
        {
            inputADwellTimers[handSlot] = Mathf.Max(0f, inputADwellTimers[handSlot] - (dt * 6f));
            inputBDwellTimers[handSlot] = Mathf.Max(0f, inputBDwellTimers[handSlot] - (dt * 6f));
            inputPushHoverTargetBySlot[handSlot] = null;
            inputPushProgressBySlot[handSlot] = 0f;
            if (detector.IsFistActive)
            {
                inputAReadyForFistBySlot[handSlot] = false;
                inputBReadyForFistBySlot[handSlot] = false;
            }
            DecayInputSwitchMagnetOffset(handSlot, dt);
            if (isOwner && inputADwellTimers[handSlot] <= 0f && inputBDwellTimers[handSlot] <= 0f)
            {
                ExitInteractionMode(ActiveInteractionMode.InputSwitch, triggerNeutralCooldown: false);
            }
            return;
        }

        // Soft Magnetic Aim-Assist kalem menuju terminal Input A/B terdekat
        Vector2 desiredMagnetOffset = Vector2.zero;
        if (speedAllowed && palmSpeed < SoftMagnetMaxHandSpeed)
        {
            bool canPullA = inputANode != null && !inputABehind && rawDistA < scaledMagnetRadiusA;
            bool canPullB = inputBNode != null && !inputBBehind && rawDistB < scaledMagnetRadiusB;
            if (canPullA || canPullB)
            {
                bool pickA = canPullA && (!canPullB || (rawDistA / scaledMagnetRadiusA) <= (rawDistB / scaledMagnetRadiusB));
                Vector2 targetXY = pickA ? inputAXY : inputBXY;
                float d = pickA ? rawDistA : rawDistB;
                float targetMagnetRadius = pickA ? scaledMagnetRadiusA : scaledMagnetRadiusB;
                float slowFactor = Mathf.Clamp01(1f - (palmSpeed / SoftMagnetMaxHandSpeed));
                float pull = InputSwitchMagnetMaxPull * slowFactor * (1f - (d / Mathf.Max(0.0001f, targetMagnetRadius)));
                desiredMagnetOffset = (targetXY - rawPalmXY) * pull;
            }
        }

        float blendAlpha = 1f - Mathf.Exp(-SoftMagnetOffsetBlendSpeed * Mathf.Max(0.0001f, dt));
        inputSwitchMagnetOffsetBySlot[handSlot] = Vector2.Lerp(inputSwitchMagnetOffsetBySlot[handSlot], desiredMagnetOffset, blendAlpha);

        Vector2 assistedPalmXY = rawPalmXY + inputSwitchMagnetOffsetBySlot[handSlot];
        float distA = inputABehind ? float.MaxValue : Vector2.Distance(assistedPalmXY, inputAXY);
        float distB = inputBBehind ? float.MaxValue : Vector2.Distance(assistedPalmXY, inputBXY);

        bool inA = !inputABehind && distA <= scaledTapRadiusA && inputANode != null;
        bool inB = !inputBBehind && distB <= scaledTapRadiusB && inputBNode != null;

        if (inA && (!inB || distA <= distB))
        {
            slotHoveredInput = inputANode;
            inputPushHoverTargetBySlot[handSlot] = inputANode;
            inputBDwellTimers[handSlot] = 0f;
            inputBReadyForFistBySlot[handSlot] = false;

            if (!detector.IsFistActive)
            {
                // Tangan terbuka di dalam zona Input A -> otomatis rearm & arm edge-trigger agar siap menerima kepalan baru
                inputARequiresExitRearmBySlot[handSlot] = false;
                inputAReadyForFistBySlot[handSlot] = true;
                inputADwellTimers[handSlot] = 0f;
                inputPushProgressBySlot[handSlot] = 0.22f;
                if (isOwner)
                {
                    ExitInteractionMode(ActiveInteractionMode.InputSwitch, triggerNeutralCooldown: false);
                }
            }
            else if (inputAReadyForFistBySlot[handSlot]
                     && !inputARequiresExitRearmBySlot[handSlot]
                     && inputATapCooldown <= 0f
                     && (isOwner || speedAllowed))
            {
                if (activeInteractionMode == ActiveInteractionMode.Idle)
                {
                    activeInteractionMode = ActiveInteractionMode.InputSwitch;
                    activeInteractionHandSlot = handSlot;
                    singleHandModeEnteredTime = Time.unscaledTime;
                    isOwner = true;
                }
                if (isOwner)
                {
                    inputADwellTimers[handSlot] += dt;
                    inputPushProgressBySlot[handSlot] = Mathf.Clamp01(0.25f + (0.75f * (inputADwellTimers[handSlot] / InputFistConfirmSeconds)));
                    if (inputADwellTimers[handSlot] >= InputFistConfirmSeconds)
                    {
                        inputADwellTimers[handSlot] = 0f;
                        inputARequiresExitRearmBySlot[handSlot] = true;
                        inputAReadyForFistBySlot[handSlot] = false;
                        inputATapCooldown = InputAirTapCooldownSeconds;
                        inputATouchRippleTimer = InputTouchRippleDuration;
                        inputPushProgressBySlot[handSlot] = 1f;
                        ToggleInputTerminalNode(isInputB: false);
                        ExitInteractionMode(ActiveInteractionMode.InputSwitch, triggerNeutralCooldown: true);
                    }
                }
            }
            else
            {
                // Kepalan sudah terlanjur terbentuk dari luar zona (melintas) atau baru saja toggle -> jangan picu
                inputADwellTimers[handSlot] = 0f;
                inputPushProgressBySlot[handSlot] = 0f;
                if (isOwner)
                {
                    ExitInteractionMode(ActiveInteractionMode.InputSwitch, triggerNeutralCooldown: false);
                }
            }
        }
        else if (inB)
        {
            slotHoveredInput = inputBNode;
            inputPushHoverTargetBySlot[handSlot] = inputBNode;
            inputADwellTimers[handSlot] = 0f;
            inputAReadyForFistBySlot[handSlot] = false;

            if (!detector.IsFistActive)
            {
                // Tangan terbuka di dalam zona Input B -> otomatis rearm & arm edge-trigger agar siap menerima kepalan baru
                inputBRequiresExitRearmBySlot[handSlot] = false;
                inputBReadyForFistBySlot[handSlot] = true;
                inputBDwellTimers[handSlot] = 0f;
                inputPushProgressBySlot[handSlot] = 0.22f;
                if (isOwner)
                {
                    ExitInteractionMode(ActiveInteractionMode.InputSwitch, triggerNeutralCooldown: false);
                }
            }
            else if (inputBReadyForFistBySlot[handSlot]
                     && !inputBRequiresExitRearmBySlot[handSlot]
                     && inputBTapCooldown <= 0f
                     && (isOwner || speedAllowed))
            {
                if (activeInteractionMode == ActiveInteractionMode.Idle)
                {
                    activeInteractionMode = ActiveInteractionMode.InputSwitch;
                    activeInteractionHandSlot = handSlot;
                    singleHandModeEnteredTime = Time.unscaledTime;
                    isOwner = true;
                }
                if (isOwner)
                {
                    inputBDwellTimers[handSlot] += dt;
                    inputPushProgressBySlot[handSlot] = Mathf.Clamp01(0.25f + (0.75f * (inputBDwellTimers[handSlot] / InputFistConfirmSeconds)));
                    if (inputBDwellTimers[handSlot] >= InputFistConfirmSeconds)
                    {
                        inputBDwellTimers[handSlot] = 0f;
                        inputBRequiresExitRearmBySlot[handSlot] = true;
                        inputBReadyForFistBySlot[handSlot] = false;
                        inputBTapCooldown = InputAirTapCooldownSeconds;
                        inputBTouchRippleTimer = InputTouchRippleDuration;
                        inputPushProgressBySlot[handSlot] = 1f;
                        ToggleInputTerminalNode(isInputB: true);
                        ExitInteractionMode(ActiveInteractionMode.InputSwitch, triggerNeutralCooldown: true);
                    }
                }
            }
            else
            {
                // Kepalan sudah terlanjur terbentuk dari luar zona (melintas) atau baru saja toggle -> jangan picu
                inputBDwellTimers[handSlot] = 0f;
                inputPushProgressBySlot[handSlot] = 0f;
                if (isOwner)
                {
                    ExitInteractionMode(ActiveInteractionMode.InputSwitch, triggerNeutralCooldown: false);
                }
            }
        }
        else
        {
            inputADwellTimers[handSlot] = Mathf.Max(0f, inputADwellTimers[handSlot] - (dt * 6f));
            inputBDwellTimers[handSlot] = Mathf.Max(0f, inputBDwellTimers[handSlot] - (dt * 6f));
            inputPushHoverTargetBySlot[handSlot] = null;
            inputPushProgressBySlot[handSlot] = 0f;
            if (isOwner && inputADwellTimers[handSlot] <= 0f && inputBDwellTimers[handSlot] <= 0f)
            {
                ExitInteractionMode(ActiveInteractionMode.InputSwitch, triggerNeutralCooldown: false);
            }
        }
    }

    private void ToggleInputTerminalNode(bool isInputB)
    {
        GateNode targetNode = isInputB ? inputBNode : inputANode;
        if (targetNode == null)
        {
            return;
        }

        if (levelManager != null)
        {
            if (isInputB)
            {
                levelManager.OnToggleInputBClicked();
            }
            else
            {
                levelManager.OnToggleInputAClicked();
            }
        }
        else
        {
            targetNode.ToggleInputState();
        }

        // Evaluasi rangkaian sinkron agar outputNode.currentOutputValue tidak pernah tertinggal 1 frame pasca-toggle sakelar
        if (circuitManager != null)
        {
            circuitManager.EvaluateCircuit();
        }

        if (isInputB)
        {
            inputBScalePop.Trigger(InputTogglePopAmplitude);
        }
        else
        {
            inputAScalePop.Trigger(InputTogglePopAmplitude);
        }

        targetNode.TriggerTapFeedback();
        if (spatialSfx != null)
        {
            spatialSfx.PlaySwitchToggle(targetNode.currentOutputValue, targetNode.transform.position);
        }
        gestureGuide?.NotifyInputToggled();
    }

    private Vector2 ProjectWorldToCameraRayPlane(Camera mainCam, Vector3 worldPos)
    {
        if (stageController != null && mainCam != null)
        {
            return stageController.ProjectWorldPointToCameraRayPlane(mainCam, worldPos, WorkbenchDepth);
        }
        if (mainCam == null)
        {
            return new Vector2(worldPos.x, worldPos.y);
        }

        Vector3 camLocal = mainCam.transform.InverseTransformPoint(worldPos);
        float safeZ = Mathf.Max(0.35f, camLocal.z);
        float scale = WorkbenchDepth / safeZ;
        return new Vector2(camLocal.x * scale, camLocal.y * scale);
    }

    private static float ComputeParkingSlotHoverHeightY(int slotIndex)
    {
        int safeIdx = Mathf.Clamp(slotIndex, 0, ParkingSlotBaseX.Length - 1);
        float normX = (safeIdx - 2.0f) * 0.5f;
        float archOffset = 0.028f * (1f - (normX * normX));
        float staggerOffset = ((safeIdx & 1) == 0) ? 0.012f : -0.010f;
        return ParkingRackHoverHeight + archOffset + staggerOffset;
    }

    private static float ComputeParkingArcYawDeg(int slotIndex)
    {
        int safeIdx = Mathf.Clamp(slotIndex, 0, ParkingSlotBaseX.Length - 1);
        float normX = (safeIdx - 2.0f) * 0.5f;
        return -normX * ParkingMaxArcYawDeg;
    }

    private float ComputeNodeApparentRayScale(
        Camera mainCam,
        Vector3 nodeWorldPos,
        float minScale,
        float maxScale,
        out bool isBehindCamera,
        out float rawVisualScale)
    {
        float stageZoom = stageController != null ? stageController.CurrentZoomScale : AirWeavingStageController.DefaultZoomScale;
        if (mainCam == null)
        {
            isBehindCamera = false;
            rawVisualScale = Mathf.Clamp(stageZoom, 0.18f, maxScale);
            return Mathf.Clamp(stageZoom, minScale, maxScale);
        }

        float camZ = mainCam.transform.InverseTransformPoint(nodeWorldPos).z;
        if (camZ <= 0.25f)
        {
            isBehindCamera = true;
            rawVisualScale = 0.55f;
            return minScale;
        }

        isBehindCamera = false;
        float apparent = stageZoom * (WorkbenchDepth / camZ);
        rawVisualScale = Mathf.Clamp(apparent, 0.18f, maxScale);
        return Mathf.Clamp(apparent, minScale, maxScale);
    }

    private static float ComputeArenaBaseHoverHeightY(Vector2 stageLocalXZ, float aspectScale, int slotIndex = 2)
    {
        float halfSpanX = Mathf.Max(0.35f, AssemblyMaxBaseX * aspectScale);
        float normX = Mathf.Clamp(stageLocalXZ.x / halfSpanX, -1.25f, 1.25f);
        float centerZ = (AssemblyMinY + AssemblyMaxY) * 0.5f;
        float halfSpanZ = Mathf.Max(0.35f, (AssemblyMaxY - AssemblyMinY) * 0.5f);
        float normZ = Mathf.Clamp((stageLocalXZ.y - centerZ) / halfSpanZ, -1.2f, 1.2f);
        float slotStagger = (Mathf.Clamp(slotIndex, 0, 4) - 2) * 0.011f;
        return BaseHoverHeight + (ArenaArcHeightY * normX * normX) + (0.016f * normZ) + slotStagger;
    }

    private static float ComputeArenaArcYawDeg(float stageLocalX, float aspectScale)
    {
        float halfSpanX = Mathf.Max(0.35f, AssemblyMaxBaseX * aspectScale);
        float normX = Mathf.Clamp(stageLocalX / halfSpanX, -1.25f, 1.25f);
        return -normX * ArenaMaxArcYawDeg;
    }

    private static float ComputeAntiFishEyeNodeScale(Transform camT, Vector3 nodeWorldPos, float desiredWorldScale)
    {
        // Tahap 4.7: Biarkan perspektif 3D kamera bekerja 100% alami tanpa mengecilkan objek saat mendekat ke mata / deep zoom
        return desiredWorldScale;
    }

    private void ComputeStageNodePose(
        Transform camT,
        Vector3 stageTableLocalPos,
        float arcYawDeg,
        float phaseOffset,
        bool isGrabbed,
        Vector2 inertiaTiltDeg,
        out Vector3 worldPos,
        out Quaternion worldRot)
    {
        float t = Time.unscaledTime;
        float bobOffset = isGrabbed ? 0f : (Mathf.Sin((t * FloatBobSpeed) + phaseOffset) * FloatBobAmplitude);
        float swayPitch = isGrabbed ? 0f : (Mathf.Sin((t * 1.15f) + (phaseOffset * 1.3f)) * GimbalSwayPitchDeg);
        float swayYaw = isGrabbed ? 0f : (Mathf.Cos((t * 0.95f) + (phaseOffset * 0.8f)) * GimbalSwayYawDeg);
        float swayRoll = isGrabbed ? 0f : (Mathf.Sin((t * 1.30f) + (phaseOffset * 1.7f)) * GimbalSwayRollDeg);

        float pitch = swayPitch + inertiaTiltDeg.x;
        float yaw = swayYaw + (inertiaTiltDeg.y * 0.45f);
        float roll = swayRoll - inertiaTiltDeg.y;

        Transform stageRoot = stageController != null ? stageController.StageRoot : null;
        if (stageRoot != null)
        {
            // stageTableLocalPos = (X = kiri-kanan meja, Y = tinggi melayang di atas meja, Z = kedalaman depan-belakang meja)
            Vector3 finalLocal = new Vector3(
                stageTableLocalPos.x,
                stageTableLocalPos.y + bobOffset,
                stageTableLocalPos.z);
            worldPos = stageRoot.TransformPoint(finalLocal);
            Quaternion localNodeRot = Quaternion.Euler(0f, arcYawDeg, 0f)
                                      * NodeBaseTableRotation
                                      * Quaternion.Euler(pitch, yaw, roll);
            worldRot = stageRoot.rotation * localNodeRot;
        }
        else
        {
            worldPos = camT.TransformPoint(new Vector3(
                stageTableLocalPos.x,
                stageTableLocalPos.z + bobOffset,
                WorkbenchDepth));
            worldRot = camT.rotation
                       * Quaternion.Euler(0f, arcYawDeg, 0f)
                       * WorkbenchTiltRotation
                       * Quaternion.Euler(pitch, yaw, roll);
        }

        // Pengaman nearClipPlane: hanya tahan node yang benar-benar menyerempet lensa di depan pandangan,
        // agar node yang sah berada jauh di belakang kamera saat Focus-Point Orbit makro (10x–15x) tidak ditarik ke depan lensa.
        if (camT != null)
        {
            Vector3 camLocal = camT.InverseTransformPoint(worldPos);
            float forwardDepth = camLocal.z;
            float minSafeDepth = (cachedMainCamera != null ? cachedMainCamera.nearClipPlane : 0.15f) + MinNearClipSafeOffsetMeters;
            float lateralSqr = (camLocal.x * camLocal.x) + (camLocal.y * camLocal.y);
            if (forwardDepth > -0.35f && forwardDepth < minSafeDepth && lateralSqr < 2.25f)
            {
                worldPos += camT.forward * (minSafeDepth - forwardDepth);
            }
        }
    }

    private void SyncAllWorkbenchNodesToCamera(Camera mainCam, bool snapImmediately)
    {
        if (mainCam == null)
        {
            return;
        }

        Transform camT = mainCam.transform;
        float aspectScale = ComputeHorizontalAspectScale(mainCam);
        float dt = Time.unscaledDeltaTime;
        float stageZoom = stageController != null ? stageController.CurrentZoomScale : 1f;

        // 1. Terminal tetap di atas sayap kiri & kanan meja horizontal XZ: Input A, Input B, Output (dengan stagger tinggi & yaw menghadap arena)
        if (inputANode != null)
        {
            if (snapImmediately)
            {
                inputAScalePop.Reset();
            }
            float popA = snapImmediately ? 1f : inputAScalePop.Evaluate(dt);
            ComputeStageNodePose(
                camT,
                new Vector3(InputTerminalBaseX * aspectScale, InputAHoverHeight, InputATerminalY),
                InputATerminalArcYawDeg,
                0.35f,
                isGrabbed: false,
                Vector2.zero,
                out Vector3 posA,
                out Quaternion rotA);
            float scaleA = ComputeAntiFishEyeNodeScale(camT, posA, TerminalScale * stageZoom * popA);
            inputANode.transform.localScale = Vector3.one * scaleA;
            inputANode.SetExternalSpatialPose(true, posA, rotA);
        }

        if (inputBNode != null)
        {
            if (snapImmediately)
            {
                inputBScalePop.Reset();
            }
            float popB = snapImmediately ? 1f : inputBScalePop.Evaluate(dt);
            ComputeStageNodePose(
                camT,
                new Vector3(InputTerminalBaseX * aspectScale, InputBHoverHeight, InputBTerminalY),
                InputBTerminalArcYawDeg,
                2.05f,
                isGrabbed: false,
                Vector2.zero,
                out Vector3 posB,
                out Quaternion rotB);
            float scaleB = ComputeAntiFishEyeNodeScale(camT, posB, TerminalScale * stageZoom * popB);
            inputBNode.transform.localScale = Vector3.one * scaleB;
            inputBNode.SetExternalSpatialPose(true, posB, rotB);
        }

        if (outputNode != null)
        {
            ComputeStageNodePose(
                camT,
                new Vector3(OutputTerminalBaseX * aspectScale, OutputHoverHeight, OutputTerminalY),
                OutputTerminalArcYawDeg,
                3.70f,
                isGrabbed: false,
                Vector2.zero,
                out Vector3 posOut,
                out Quaternion rotOut);
            float scaleOut = ComputeAntiFishEyeNodeScale(camT, posOut, OutputTerminalScale * stageZoom);
            outputNode.transform.localScale = Vector3.one * scaleOut;
            outputNode.SetExternalSpatialPose(true, posOut, rotOut);
        }

        // 2. 5 Gerbang Logika melayang di atas meja horizontal XZ (currentLocalXY = (stageX, stageZ), currentLocalZ = hoverHeightY)
        for (int i = 0; i < gateSlots.Count; i++)
        {
            WorkbenchGateSlot slot = gateSlots[i];
            if (slot == null || slot.node == null)
            {
                continue;
            }

            float gatePhase = 1.15f + (slot.slotIndex * 1.27f);
            float popFactor = snapImmediately ? 1f : slot.scalePop.Evaluate(dt);

            if (i == grabbedGateSlotIndex)
            {
                float arcYaw = ComputeArenaArcYawDeg(slot.currentLocalXY.x, aspectScale);
                ComputeStageNodePose(
                    camT,
                    new Vector3(slot.currentLocalXY.x, slot.currentLocalZ, slot.currentLocalXY.y),
                    arcYaw,
                    gatePhase,
                    isGrabbed: true,
                    slot.currentInertiaTiltDeg,
                    out Vector3 grabbedPos,
                    out Quaternion grabbedRot);
                float grabbedScale = ComputeAntiFishEyeNodeScale(camT, grabbedPos, slot.currentScale * stageZoom * popFactor);
                slot.node.transform.localScale = Vector3.one * grabbedScale;
                slot.node.SetExternalSpatialPose(true, grabbedPos, grabbedRot);
            }
            else if (slot.isPlacedOnWorkbench)
            {
                if (snapImmediately)
                {
                    float targetHoverY = ComputeArenaBaseHoverHeightY(slot.placedLocalXY, aspectScale, slot.slotIndex);
                    slot.currentLocalXY = slot.placedLocalXY;
                    slot.currentLocalZ = targetHoverY;
                    slot.currentScale = ActiveGateScale;
                    slot.ResetSpringState();
                }
                else
                {
                    // Luncuran inersia singkat yang mengerem halus di dalam batas zona rakit (Tahap 4.9)
                    if (slot.isCoasting && dt > 0.00001f)
                    {
                        slot.placedLocalXY += slot.coastVelocityXY * dt;

                        float minX = AssemblyMinBaseX * aspectScale;
                        float maxX = AssemblyMaxBaseX * aspectScale;
                        if (slot.placedLocalXY.x < minX)
                        {
                            slot.placedLocalXY.x = minX;
                            if (slot.coastVelocityXY.x < 0f)
                            {
                                slot.coastVelocityXY.x = -slot.coastVelocityXY.x * GateCoastEdgeBounceFactor;
                            }
                        }
                        else if (slot.placedLocalXY.x > maxX)
                        {
                            slot.placedLocalXY.x = maxX;
                            if (slot.coastVelocityXY.x > 0f)
                            {
                                slot.coastVelocityXY.x = -slot.coastVelocityXY.x * GateCoastEdgeBounceFactor;
                            }
                        }

                        if (slot.placedLocalXY.y < AssemblyMinY)
                        {
                            slot.placedLocalXY.y = AssemblyMinY;
                            if (slot.coastVelocityXY.y < 0f)
                            {
                                slot.coastVelocityXY.y = -slot.coastVelocityXY.y * GateCoastEdgeBounceFactor;
                            }
                        }
                        else if (slot.placedLocalXY.y > AssemblyMaxY)
                        {
                            slot.placedLocalXY.y = AssemblyMaxY;
                            if (slot.coastVelocityXY.y > 0f)
                            {
                                slot.coastVelocityXY.y = -slot.coastVelocityXY.y * GateCoastEdgeBounceFactor;
                            }
                        }

                        slot.coastVelocityXY *= Mathf.Exp(-GateCoastDamping * dt);
                        if (slot.coastVelocityXY.sqrMagnitude <= GateCoastStopSpeed * GateCoastStopSpeed)
                        {
                            slot.coastVelocityXY = Vector2.zero;
                            slot.isCoasting = false;
                        }
                    }

                    float targetHoverY = ComputeArenaBaseHoverHeightY(slot.placedLocalXY, aspectScale, slot.slotIndex);
                    slot.currentLocalXY = StepCriticallyDampedSpring2D(
                        slot.currentLocalXY,
                        slot.placedLocalXY,
                        ref slot.velocityXY,
                        GateSettleSpringOmega,
                        dt);
                    slot.currentLocalZ = StepCriticallyDampedSpring(
                        slot.currentLocalZ,
                        targetHoverY,
                        ref slot.velocityZ,
                        GateLiftZSpringOmega,
                        dt);
                    slot.currentScale = StepCriticallyDampedSpring(
                        slot.currentScale,
                        ActiveGateScale,
                        ref slot.velocityScale,
                        GateScaleSpringOmega,
                        dt);
                    slot.smoothedVelocityXY = slot.velocityXY;
                    Vector2 targetTilt = new Vector2(
                        Mathf.Clamp(slot.velocityXY.y * DragVelocityTiltDegPerUnitSec, -MaxDragInertiaTiltDeg, MaxDragInertiaTiltDeg),
                        Mathf.Clamp(slot.velocityXY.x * DragVelocityTiltDegPerUnitSec, -MaxDragInertiaTiltDeg, MaxDragInertiaTiltDeg));
                    slot.currentInertiaTiltDeg = StepCriticallyDampedSpring2D(
                        slot.currentInertiaTiltDeg,
                        targetTilt,
                        ref slot.velocityTiltDeg,
                        GateSettleSpringOmega,
                        dt);
                }

                float arcYaw = ComputeArenaArcYawDeg(slot.currentLocalXY.x, aspectScale);
                ComputeStageNodePose(
                    camT,
                    new Vector3(slot.currentLocalXY.x, slot.currentLocalZ, slot.currentLocalXY.y),
                    arcYaw,
                    gatePhase,
                    isGrabbed: false,
                    slot.currentInertiaTiltDeg,
                    out Vector3 placedPos,
                    out Quaternion placedRot);
                float placedScale = ComputeAntiFishEyeNodeScale(camT, placedPos, slot.currentScale * stageZoom * popFactor);
                slot.node.transform.localScale = Vector3.one * placedScale;
                slot.node.SetExternalSpatialPose(true, placedPos, placedRot);
            }
            else
            {
                Vector2 homeXZ = GetParkingSlotLocalXY(slot.slotIndex, aspectScale);
                float homeHoverY = ComputeParkingSlotHoverHeightY(slot.slotIndex);
                slot.placedLocalXY = homeXZ;
                if (snapImmediately)
                {
                    slot.currentLocalXY = homeXZ;
                    slot.currentLocalZ = homeHoverY;
                    slot.currentScale = ParkedGateScale;
                    slot.ResetSpringState();
                }
                else
                {
                    slot.currentLocalXY = StepCriticallyDampedSpring2D(
                        slot.currentLocalXY,
                        homeXZ,
                        ref slot.velocityXY,
                        GateSettleSpringOmega,
                        dt);
                    slot.currentLocalZ = StepCriticallyDampedSpring(
                        slot.currentLocalZ,
                        homeHoverY,
                        ref slot.velocityZ,
                        GateLiftZSpringOmega,
                        dt);
                    slot.currentScale = StepCriticallyDampedSpring(
                        slot.currentScale,
                        ParkedGateScale,
                        ref slot.velocityScale,
                        GateScaleSpringOmega,
                        dt);
                    slot.smoothedVelocityXY = slot.velocityXY;
                    Vector2 targetTilt = new Vector2(
                        Mathf.Clamp(slot.velocityXY.y * DragVelocityTiltDegPerUnitSec, -MaxDragInertiaTiltDeg, MaxDragInertiaTiltDeg),
                        Mathf.Clamp(slot.velocityXY.x * DragVelocityTiltDegPerUnitSec, -MaxDragInertiaTiltDeg, MaxDragInertiaTiltDeg));
                    slot.currentInertiaTiltDeg = StepCriticallyDampedSpring2D(
                        slot.currentInertiaTiltDeg,
                        targetTilt,
                        ref slot.velocityTiltDeg,
                        GateSettleSpringOmega,
                        dt);
                }

                float arcYaw = ComputeParkingArcYawDeg(slot.slotIndex);
                ComputeStageNodePose(
                    camT,
                    new Vector3(slot.currentLocalXY.x, slot.currentLocalZ, slot.currentLocalXY.y),
                    arcYaw,
                    gatePhase,
                    isGrabbed: false,
                    slot.currentInertiaTiltDeg,
                    out Vector3 parkedPos,
                    out Quaternion parkedRot);
                float parkedScale = ComputeAntiFishEyeNodeScale(camT, parkedPos, slot.currentScale * stageZoom * popFactor);
                slot.node.SetExternalParkedPreview(true, parkedPos, parkedRot, parkedScale);
            }
        }

        if (snapImmediately)
        {
            UpdateAirWeavingLabelsAndTruthPodSpatialPose(mainCam);
        }
    }

    private int FindNearestGateSlotIndex(Camera mainCam, Vector3 cursorWorldPos, float placedMaxRadius, float parkedMaxRadius)
    {
        int bestIdx = -1;
        float bestNormDist = 1.0001f;
        Vector2 cursorRayXY = ProjectWorldToCameraRayPlane(mainCam, cursorWorldPos);

        for (int i = 0; i < gateSlots.Count; i++)
        {
            WorkbenchGateSlot slot = gateSlots[i];
            if (slot == null || slot.node == null)
            {
                continue;
            }

            float apparentScale = ComputeNodeApparentRayScale(mainCam, slot.node.transform.position, MinScreenSpaceHitboxScale, 8.0f, out bool isBehindCamera, out _);
            if (isBehindCamera)
            {
                continue;
            }

            float limit = (slot.isPlacedOnWorkbench ? placedMaxRadius : parkedMaxRadius) * apparentScale;
            Vector2 gateRayXY = ProjectWorldToCameraRayPlane(mainCam, slot.node.transform.position);
            float dist = Vector2.Distance(cursorRayXY, gateRayXY);
            if (dist < limit)
            {
                float normDist = dist / Mathf.Max(0.001f, limit);
                if (normDist < bestNormDist)
                {
                    bestNormDist = normDist;
                    bestIdx = i;
                }
            }
        }

        return bestIdx;
    }

    private static float ComputeHorizontalAspectScale(Camera mainCam)
    {
        float aspect = (mainCam != null && mainCam.aspect > 0.1f) ? mainCam.aspect : (16f / 9f);
        return Mathf.Clamp(aspect / (16f / 9f), 0.78f, 1.12f);
    }

    private static Vector2 GetParkingSlotLocalXY(int slotIndex, float aspectScale)
    {
        int safeIdx = Mathf.Clamp(slotIndex, 0, ParkingSlotBaseX.Length - 1);
        float normX = (safeIdx - 2.0f) * 0.5f; // -1.0 .. +1.0
        float curvedRackZ = ParkingRackY + (ParkingRackArcDepthZ * normX * normX);
        return new Vector2(ParkingSlotBaseX[safeIdx] * aspectScale, curvedRackZ);
    }

    private static Vector2 ClampDraggedGateLocalXY(Vector2 rawXY, float aspectScale)
    {
        float minX = (InputTerminalBaseX - 0.36f) * aspectScale;
        float maxX = (OutputTerminalBaseX + 0.36f) * aspectScale;
        return new Vector2(
            Mathf.Clamp(rawXY.x, minX, maxX),
            Mathf.Clamp(rawXY.y, AssemblyMinY - 0.90f, ParkingRackY + 0.65f));
    }

    /// <summary>
    /// Mengecek apakah posisi gerbang yang dipegang sudah keluar dari batas visual kotak AssemblyZone 2x
    /// (sisi atas, bawah, kiri, maupun kanan).
    /// </summary>
    private static bool IsOutsideAssemblyZone(Vector2 localXY, float aspectScale)
    {
        float leftX = (AssemblyMinBaseX - 0.16f) * aspectScale;
        float rightX = (AssemblyMaxBaseX + 0.16f) * aspectScale;
        float botY = AssemblyMinY - 0.14f;
        float topY = AssemblyMaxY + 0.14f;
        return localXY.x < leftX || localXY.x > rightX || localXY.y < botY || localXY.y > topY;
    }

    private static Vector2 ClampAssemblyZoneLocalXY(Vector2 rawXY, float aspectScale)
    {
        return new Vector2(
            Mathf.Clamp(rawXY.x, AssemblyMinBaseX * aspectScale, AssemblyMaxBaseX * aspectScale),
            Mathf.Clamp(rawXY.y, AssemblyMinY, AssemblyMaxY));
    }

    /// <summary>
    /// Magnetic Grid Snap + Auto-Separate Zero-GC (Tahap 9):
    /// memetakan posisi gerbang ke persimpangan grid AssemblyZone terdekat, lalu menjalankan spiral search
    /// (ring 0..4) bila sel sudah terisi atau terlalu dempet dengan gerbang lain / Input / Output (margin port kiri-kanan).
    /// Dipakai identik untuk pratinjau cincin grid real-time saat GateGrab maupun pendaratan saat dilepas / summon.
    /// </summary>
    private Vector2 ResolveSnappedAutoSeparateGridXZ(Vector2 rawXZ, int selfSlotIndex, float aspectScale)
    {
        Vector2 clampedRaw = ClampAssemblyZoneLocalXY(rawXZ, aspectScale);
        float minX = AssemblyMinBaseX * aspectScale;
        float maxX = AssemblyMaxBaseX * aspectScale;
        float centerX = (minX + maxX) * 0.5f;
        float centerZ = (AssemblyMinY + AssemblyMaxY) * 0.5f;
        float stepX = Mathf.Max(0.10f, AssemblyGridBaseStepX * aspectScale);
        float stepZ = AssemblyGridStepZ;

        int baseCol = Mathf.RoundToInt((clampedRaw.x - centerX) / stepX);
        int baseRow = Mathf.RoundToInt((clampedRaw.y - centerZ) / stepZ);

        for (int ring = 0; ring <= AutoSeparateMaxSpiralRing; ring++)
        {
            bool foundInRing = false;
            Vector2 bestInRing = clampedRaw;
            float bestDistSqr = float.MaxValue;

            for (int dc = -ring; dc <= ring; dc++)
            {
                for (int dr = -ring; dr <= ring; dr++)
                {
                    if (ring > 0 && Mathf.Abs(dc) != ring && Mathf.Abs(dr) != ring)
                    {
                        continue;
                    }

                    float cx = centerX + ((baseCol + dc) * stepX);
                    float cz = centerZ + ((baseRow + dr) * stepZ);
                    if (cx < (minX - 0.002f) || cx > (maxX + 0.002f) || cz < (AssemblyMinY - 0.002f) || cz > (AssemblyMaxY + 0.002f))
                    {
                        continue;
                    }

                    Vector2 candidate = new Vector2(
                        Mathf.Clamp(cx, minX, maxX),
                        Mathf.Clamp(cz, AssemblyMinY, AssemblyMaxY));
                    if (HasOverlapAtGridCell(candidate, selfSlotIndex, aspectScale))
                    {
                        continue;
                    }

                    float distSqr = (candidate - clampedRaw).sqrMagnitude;
                    if (distSqr < bestDistSqr)
                    {
                        bestDistSqr = distSqr;
                        bestInRing = candidate;
                        foundInRing = true;
                    }
                }
            }

            if (foundInRing)
            {
                return bestInRing;
            }
        }

        return new Vector2(
            Mathf.Clamp(centerX + (baseCol * stepX), minX, maxX),
            Mathf.Clamp(centerZ + (baseRow * stepZ), AssemblyMinY, AssemblyMaxY));
    }

    private bool HasOverlapAtGridCell(Vector2 candidateXZ, int selfSlotIndex, float aspectScale)
    {
        float minGateDx = AutoSeparateGateMinBaseDx * aspectScale;
        for (int i = 0; i < gateSlots.Count; i++)
        {
            if (i == selfSlotIndex)
            {
                continue;
            }
            WorkbenchGateSlot other = gateSlots[i];
            if (other == null || !other.isPlacedOnWorkbench)
            {
                continue;
            }

            Vector2 otherXZ = i == grabbedGateSlotIndex ? other.currentLocalXY : other.placedLocalXY;
            if (Mathf.Abs(candidateXZ.x - otherXZ.x) < minGateDx
                && Mathf.Abs(candidateXZ.y - otherXZ.y) < AutoSeparateGateMinDz)
            {
                return true;
            }
        }

        float minTermDx = AutoSeparateTerminalMinBaseDx * aspectScale;
        float inputX = InputTerminalBaseX * aspectScale;
        float outputX = OutputTerminalBaseX * aspectScale;
        if (Mathf.Abs(candidateXZ.x - inputX) < minTermDx
            && (Mathf.Abs(candidateXZ.y - InputATerminalY) < AutoSeparateTerminalMinDz
                || Mathf.Abs(candidateXZ.y - InputBTerminalY) < AutoSeparateTerminalMinDz))
        {
            return true;
        }
        if (Mathf.Abs(candidateXZ.x - outputX) < minTermDx
            && Mathf.Abs(candidateXZ.y - OutputTerminalY) < AutoSeparateTerminalMinDz)
        {
            return true;
        }

        return false;
    }

    private bool IsHandNearAnyTerminalForSummonBlock(
        Camera mainCam,
        Vector3 palmWorldPos,
        Vector3 tipWorldPos,
        float aspectScale)
    {
        Vector2 palmRayXY = ProjectWorldToCameraRayPlane(mainCam, palmWorldPos);
        Vector2 tipRayXY = ProjectWorldToCameraRayPlane(mainCam, tipWorldPos);
        float fallbackScale = stageController != null ? Mathf.Clamp(stageController.CurrentZoomScale, MinScreenSpaceHitboxScale, 6.0f) : 1f;

        if (IsPointNearTerminalNode(mainCam, inputANode, new Vector2(InputTerminalBaseX * aspectScale, InputATerminalY), palmRayXY, tipRayXY, fallbackScale))
        {
            return true;
        }
        if (IsPointNearTerminalNode(mainCam, inputBNode, new Vector2(InputTerminalBaseX * aspectScale, InputBTerminalY), palmRayXY, tipRayXY, fallbackScale))
        {
            return true;
        }
        if (IsPointNearTerminalNode(mainCam, outputNode, new Vector2(OutputTerminalBaseX * aspectScale, OutputTerminalY), palmRayXY, tipRayXY, fallbackScale))
        {
            return true;
        }
        return false;
    }

    private bool IsPointNearTerminalNode(
        Camera mainCam,
        GateNode terminalNode,
        Vector2 fallbackTerminalRayXY,
        Vector2 palmRayXY,
        Vector2 tipRayXY,
        float fallbackScale)
    {
        Vector2 termRayXY = fallbackTerminalRayXY;
        float termScale = fallbackScale;
        if (terminalNode != null)
        {
            termScale = ComputeNodeApparentRayScale(mainCam, terminalNode.transform.position, MinScreenSpaceHitboxScale, 6.0f, out bool behind, out _);
            if (behind)
            {
                return false;
            }
            termRayXY = ProjectWorldToCameraRayPlane(mainCam, terminalNode.transform.position);
        }

        float blockRadius = SummonTerminalBlockRadius * termScale;
        float blockRadiusSqr = blockRadius * blockRadius;
        return (palmRayXY - termRayXY).sqrMagnitude <= blockRadiusSqr
               || (tipRayXY - termRayXY).sqrMagnitude <= blockRadiusSqr;
    }

    // =========================================================================
    // ASYNC GPU READBACK + DEDICATED WORKER THREAD MEDIAPIPE & VOLUMETRIC 3D Z
    // =========================================================================

    private void StartInferenceWorkerThread()
    {
        StopInferenceWorkerThread();

        useAsyncGpuReadback = SystemInfo.supportsAsyncGPUReadback;
        asyncGpuReadbackErrorCount = 0;
        gpuReadbackInFlight = false;

        lock (workerFrameLock)
        {
            hasPendingWorkerFrame = false;
            workerIsProcessingFrame = false;
            gpuWriteSlotIdx = 0;
            pendingSlotIdx = 1;
            workerReadSlotIdx = 2;
        }

        lock (workerResultLock)
        {
            workerWriteResultIdx = 0;
            latestResultIdx = 1;
            mainReadResultIdx = 2;
            latestResultSequence = 0;
            consumedResultSequence = 0;
            for (int i = 0; i < resultSlots.Length; i++)
            {
                resultSlots[i].validHandCount = 0;
                resultSlots[i].timestampMs = 0;
                resultSlots[i].errorMessage = null;
            }
        }

        workerStopRequested = false;
        lastWorkerTimestampMs = 0;
        inferenceStopwatch.Restart();

        workerWakeEvent = new AutoResetEvent(false);
        inferenceWorkerThread = new Thread(InferenceWorkerLoop)
        {
            Name = "GateWeaver_MP_Worker",
            IsBackground = true
        };
        inferenceWorkerThread.Start();
    }

    private bool StopInferenceWorkerThread()
    {
        workerStopRequested = true;
        if (workerWakeEvent != null)
        {
            try
            {
                workerWakeEvent.Set();
            }
            catch (ObjectDisposedException)
            {
                // Abaikan bila event sudah ditutup
            }
        }

        bool exitedCleanly = true;
        if (inferenceWorkerThread != null)
        {
            if (inferenceWorkerThread.IsAlive)
            {
                exitedCleanly = inferenceWorkerThread.Join(WorkerShutdownJoinTimeoutMs);
                if (!exitedCleanly)
                {
                    Debug.LogWarning("[AirWeavingController] Worker thread MediaPipe belum berhenti dalam 1500 ms.");
                }
            }
            inferenceWorkerThread = null;
        }

        if (workerWakeEvent != null)
        {
            workerWakeEvent.Dispose();
            workerWakeEvent = null;
        }

        inferenceStopwatch.Stop();
        return exitedCleanly;
    }

    private void PumpAsyncCameraInference()
    {
        // 1. Selesaikan AsyncGPUReadback yang sedang berjalan (polling Zero-GC di Main Thread)
        if (gpuReadbackInFlight)
        {
            if (!gpuReadbackRequest.done)
            {
                return;
            }

            gpuReadbackInFlight = false;
            if (gpuReadbackRequest.hasError)
            {
                asyncGpuReadbackErrorCount++;
                if (asyncGpuReadbackErrorCount >= MaxAsyncGpuReadbackErrorsBeforeFallback)
                {
                    useAsyncGpuReadback = false;
                    Debug.LogWarning("[AirWeavingController] AsyncGPUReadback gagal berulang; beralih otomatis ke fallback GetPixels32().");
                }
                return;
            }

            asyncGpuReadbackErrorCount = 0;
            CommitGpuWriteSlotToWorker(
                gpuReadbackSrcW,
                gpuReadbackSrcH,
                gpuReadbackDstW,
                gpuReadbackDstH,
                gpuReadbackRotAngle,
                gpuReadbackEffectiveVMirror,
                gpuReadbackHMirror,
                gpuReadbackUseTwoTap);
        }

        // 2. Hanya jadwalkan frame baru saat WebCamTexture benar-benar menghasilkan frame baru
        if (!webCamTexture.didUpdateThisFrame)
        {
            return;
        }

        // Efisiensi Tahap 9 (Adaptive Inference Idle): jika > 1 dtk tanpa tangan, turunkan ke ~10 Hz; pulih penuh seketika saat tangan masuk
        float nowUnscaled = Time.unscaledTime;
        if (!IsAnyHandDetected && idleNoHandElapsedSeconds >= AdaptiveIdleThresholdSeconds)
        {
            if ((nowUnscaled - lastInferenceDispatchUnscaledTime) < AdaptiveIdleInferenceIntervalSeconds)
            {
                return;
            }
        }

        int camW = webCamTexture.width;
        int camH = webCamTexture.height;
        int rotAngle = ((webCamTexture.videoRotationAngle % 360) + 360) % 360;
        bool isRotated90 = (rotAngle == 90 || rotAngle == 270);
        bool vMirror = webCamTexture.videoVerticallyMirrored;

        int uprightW = isRotated90 ? camH : camW;
        int uprightH = isRotated90 ? camW : camH;
        float downscale = Mathf.Min(1f, (float)MaxInferenceLongEdge / Mathf.Max(uprightW, uprightH));
        int targetW = Mathf.Max(64, (Mathf.RoundToInt(uprightW * downscale) / 4) * 4);
        int targetH = Mathf.Max(64, (Mathf.RoundToInt(uprightH * downscale) / 4) * 4);

        bool useGpuPath = useAsyncGpuReadback && SystemInfo.supportsAsyncGPUReadback;
        int srcW = useGpuPath ? (isRotated90 ? targetH : targetW) : camW;
        int srcH = useGpuPath ? (isRotated90 ? targetW : targetH) : camH;

        if (!EnsureAsyncInferenceBuffersReady(camW, camH, srcW, srcH, targetW, targetH, useGpuPath))
        {
            // Worker masih menyelesaikan frame terakhir tepat saat perubahan resolusi; coba lagi frame render berikutnya (16 ms)
            return;
        }

        if (useGpuPath && inferenceRenderTexture != null)
        {
            lastInferenceDispatchUnscaledTime = nowUnscaled;
            RenderTexture prevActive = RenderTexture.active;
            Graphics.Blit(webCamTexture, inferenceRenderTexture);
            RenderTexture.active = prevActive;

            // Paritas orientasi vertikal OpenGLES3 & Windows DX11:
            // Graphics.Blit(webCamTexture, RT) + AsyncGPUReadback selalu mengembalikan baris 0 = v=0 (bawah tekstur),
            // 100% identik dengan WebCamTexture.GetPixels32() di DX11 maupun OpenGLES3 (tanpa XOR graphicsUVStartsAtTop).
            gpuReadbackSrcW = srcW;
            gpuReadbackSrcH = srcH;
            gpuReadbackDstW = targetW;
            gpuReadbackDstH = targetH;
            gpuReadbackRotAngle = rotAngle;
            gpuReadbackEffectiveVMirror = vMirror;
            gpuReadbackHMirror = mirrorHorizontalDisplay;
            gpuReadbackUseTwoTap = false;

            gpuReadbackRequest = AsyncGPUReadback.RequestIntoNativeArray(
                ref inputRgbaSlots[gpuWriteSlotIdx],
                inferenceRenderTexture,
                0,
                TextureFormat.RGBA32);
            gpuReadbackInFlight = true;
        }
        else
        {
            lastInferenceDispatchUnscaledTime = nowUnscaled;
            // Fallback aman GetPixels32() -> salin ke slot RGBA eksklusif Main Thread -> proses LUT & MediaPipe di Worker Thread
            webCamTexture.GetPixels32(rawWebcamPixels);
            NativeArray<Color32> dstColors = inputRgbaSlots[gpuWriteSlotIdx].Reinterpret<Color32>(1);
            dstColors.CopyFrom(rawWebcamPixels);

            CommitGpuWriteSlotToWorker(
                camW,
                camH,
                targetW,
                targetH,
                rotAngle,
                vMirror,
                mirrorHorizontalDisplay,
                useTwoTap: true);
        }
    }

    private void CommitGpuWriteSlotToWorker(
        int srcW,
        int srcH,
        int dstW,
        int dstH,
        int rotAngle,
        bool effectiveVMirror,
        bool hMirror,
        bool useTwoTap)
    {
        // Tukar slot eksklusif Main Thread (gpuWriteSlotIdx) dengan mailbox (pendingSlotIdx):
        // TIDAK pernah menyentuh workerReadSlotIdx yang mungkin sedang dibaca oleh Worker Thread (latest-frame-wins).
        lock (workerFrameLock)
        {
            int tmp = gpuWriteSlotIdx;
            gpuWriteSlotIdx = pendingSlotIdx;
            pendingSlotIdx = tmp;

            pendingSrcW = srcW;
            pendingSrcH = srcH;
            pendingDstW = dstW;
            pendingDstH = dstH;
            pendingRotAngle = rotAngle;
            pendingEffectiveVMirror = effectiveVMirror;
            pendingHMirror = hMirror;
            pendingUseTwoTap = useTwoTap;
            hasPendingWorkerFrame = true;
        }

        if (workerWakeEvent != null)
        {
            try
            {
                workerWakeEvent.Set();
            }
            catch (ObjectDisposedException)
            {
                // Abaikan bila shutdown sedang berlangsung
            }
        }
    }

    private bool EnsureAsyncInferenceBuffersReady(
        int camW,
        int camH,
        int srcW,
        int srcH,
        int dstW,
        int dstH,
        bool needRenderTexture)
    {
        int neededRgbaBytes = srcW * srcH * 4;
        int neededRgbBytes = dstW * dstH * 3;

        bool needRgbaRealloc = inputSlotWidth != srcW
                               || inputSlotHeight != srcH
                               || !inputRgbaSlots[0].IsCreated
                               || !inputRgbaSlots[1].IsCreated
                               || !inputRgbaSlots[2].IsCreated
                               || inputRgbaSlots[0].Length != neededRgbaBytes;
        bool needRgbRealloc = workerRgbWidth != dstW
                              || workerRgbHeight != dstH
                              || !workerRgbPixels.IsCreated
                              || workerRgbPixels.Length != neededRgbBytes;

        if (needRgbaRealloc || needRgbRealloc)
        {
            if (gpuReadbackInFlight)
            {
                return false;
            }

            lock (workerFrameLock)
            {
                if (workerIsProcessingFrame)
                {
                    hasPendingWorkerFrame = false;
                    return false;
                }

                hasPendingWorkerFrame = false;

                if (needRgbaRealloc)
                {
                    for (int i = 0; i < inputRgbaSlots.Length; i++)
                    {
                        if (inputRgbaSlots[i].IsCreated)
                        {
                            inputRgbaSlots[i].Dispose();
                        }
                        inputRgbaSlots[i] = new NativeArray<byte>(neededRgbaBytes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                    }
                    inputSlotWidth = srcW;
                    inputSlotHeight = srcH;
                    gpuWriteSlotIdx = 0;
                    pendingSlotIdx = 1;
                    workerReadSlotIdx = 2;
                }

                if (needRgbRealloc)
                {
                    if (workerRgbPixels.IsCreated)
                    {
                        workerRgbPixels.Dispose();
                    }
                    workerRgbPixels = new NativeArray<byte>(neededRgbBytes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                    workerRgbWidth = dstW;
                    workerRgbHeight = dstH;
                }
            }
        }

        if (needRenderTexture)
        {
            if (inferenceRenderTexture == null || inferenceRenderTexture.width != srcW || inferenceRenderTexture.height != srcH)
            {
                if (inferenceRenderTexture != null)
                {
                    inferenceRenderTexture.Release();
                    Destroy(inferenceRenderTexture);
                }
                inferenceRenderTexture = new RenderTexture(srcW, srcH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                inferenceRenderTexture.filterMode = FilterMode.Bilinear;
                inferenceRenderTexture.wrapMode = TextureWrapMode.Clamp;
                inferenceRenderTexture.Create();
            }
        }
        else if (rawWebcamPixels == null || lastCamWidth != camW || lastCamHeight != camH)
        {
            rawWebcamPixels = new Color32[camW * camH];
            lastCamWidth = camW;
            lastCamHeight = camH;
        }

        return true;
    }

    /// <summary>
    /// Loop Worker Thread murni (0 panggilan API UnityEngine):
    /// melakukan konversi RGBA32-&gt;Srgb (3 byte) + rotasi/mirror 1D-LUT + inferensi MediaPipe HandLandmarker
    /// menggunakan timestamp strictly monotonic dari <c>Stopwatch</c> dan menulis ke triple-buffer output Zero-GC.
    /// </summary>
    private void InferenceWorkerLoop()
    {
        while (!workerStopRequested)
        {
            if (workerWakeEvent != null)
            {
                workerWakeEvent.WaitOne(200);
            }
            if (workerStopRequested)
            {
                break;
            }

            int readSlot;
            int srcW;
            int srcH;
            int dstW;
            int dstH;
            int rotAngle;
            bool effVMirror;
            bool hMirror;
            bool useTwoTap;

            lock (workerFrameLock)
            {
                if (!hasPendingWorkerFrame || workerStopRequested)
                {
                    continue;
                }

                int tmp = workerReadSlotIdx;
                workerReadSlotIdx = pendingSlotIdx;
                pendingSlotIdx = tmp;

                readSlot = workerReadSlotIdx;
                srcW = pendingSrcW;
                srcH = pendingSrcH;
                dstW = pendingDstW;
                dstH = pendingDstH;
                rotAngle = pendingRotAngle;
                effVMirror = pendingEffectiveVMirror;
                hMirror = pendingHMirror;
                useTwoTap = pendingUseTwoTap;
                hasPendingWorkerFrame = false;
                workerIsProcessingFrame = true;
            }

            try
            {
                if (handLandmarker == null || !inputRgbaSlots[readSlot].IsCreated || !workerRgbPixels.IsCreated)
                {
                    continue;
                }

                FillWorkerRgbBufferFromRgba(
                    inputRgbaSlots[readSlot],
                    workerRgbPixels,
                    srcW,
                    srcH,
                    dstW,
                    dstH,
                    rotAngle,
                    effVMirror,
                    hMirror,
                    useTwoTap);

                long tsMs = inferenceStopwatch.ElapsedMilliseconds;
                if (tsMs <= lastWorkerTimestampMs)
                {
                    tsMs = lastWorkerTimestampMs + 1;
                }
                lastWorkerTimestampMs = tsMs;

                bool detected = false;
                string errMsg = null;
                try
                {
                    using (Image mpImage = new Image(
                        ImageFormat.Types.Format.Srgb,
                        dstW,
                        dstH,
                        dstW * 3,
                        workerRgbPixels))
                    {
                        detected = handLandmarker.TryDetectForVideo(mpImage, tsMs, zeroRotationOptions, ref cachedResult);
                    }
                }
                catch (Exception ex)
                {
                    detected = false;
                    errMsg = ex.Message;
                }

                WorkerInferenceSnapshot dstSnap = resultSlots[workerWriteResultIdx];
                PopulateWorkerSnapshot(dstSnap, detected, tsMs, errMsg);

                lock (workerResultLock)
                {
                    int tmpRes = latestResultIdx;
                    latestResultIdx = workerWriteResultIdx;
                    workerWriteResultIdx = tmpRes;
                    latestResultSequence++;
                }
            }
            finally
            {
                lock (workerFrameLock)
                {
                    workerIsProcessingFrame = false;
                }
            }
        }
    }

    private void PopulateWorkerSnapshot(WorkerInferenceSnapshot dstSnap, bool detected, long tsMs, string errMsg)
    {
        dstSnap.timestampMs = tsMs;
        dstSnap.errorMessage = errMsg;
        int validCount = 0;

        if (detected && cachedResult.handLandmarks != null)
        {
            int count = cachedResult.handLandmarks.Count;
            for (int h = 0; h < count && validCount < 2; h++)
            {
                List<NormalizedLandmark> cand = cachedResult.handLandmarks[h].landmarks;
                if (!IsValidHandLandmarkCandidateWorker(cand))
                {
                    continue;
                }

                WorkerHandRecord rec = dstSnap.hands[validCount];
                rec.landmarks.Clear();
                for (int k = 0; k < LandmarkCount; k++)
                {
                    rec.landmarks.Add(cand[k]);
                }

                rec.hasWorldLandmarks = false;
                if (cachedResult.handWorldLandmarks != null && h < cachedResult.handWorldLandmarks.Count)
                {
                    List<Landmark> worldLms = cachedResult.handWorldLandmarks[h].landmarks;
                    if (worldLms != null && worldLms.Count >= LandmarkCount)
                    {
                        rec.worldLandmarks.Clear();
                        for (int k = 0; k < LandmarkCount; k++)
                        {
                            rec.worldLandmarks.Add(worldLms[k]);
                        }
                        rec.hasWorldLandmarks = true;
                    }
                }

                rec.handedness = ExtractHandednessWorker(h);
                validCount++;
            }
        }

        dstSnap.validHandCount = validCount;
    }

    private bool TryConsumeLatestWorkerInferenceResult(Camera mainCam)
    {
        bool hasNew = false;
        lock (workerResultLock)
        {
            if (latestResultSequence != consumedResultSequence)
            {
                int tmp = mainReadResultIdx;
                mainReadResultIdx = latestResultIdx;
                latestResultIdx = tmp;
                consumedResultSequence = latestResultSequence;
                hasNew = true;
            }
        }

        if (!hasNew)
        {
            return false;
        }

        WorkerInferenceSnapshot snap = resultSlots[mainReadResultIdx];
        if (!string.IsNullOrEmpty(snap.errorMessage))
        {
            Debug.LogWarning($"[AirWeavingController] Inferensi frame dilewati: {snap.errorMessage}");
            snap.errorMessage = null;
        }

        inferenceFramesInWindow++;
        long timestampMs = snap.timestampMs;
        int validHandCount = snap.validHandCount;

        if (validHandCount <= 0)
        {
            // Jangan langsung me-reset One-Euro & status TwoHand pada 1-2 frame drop: beri frame-drop grace per slot
            if (primarySlotActive)
            {
                primaryMissedLastInference = true;
            }
            if (secondarySlotActive)
            {
                secondaryMissedLastInference = true;
            }
            return true;
        }

        // Stabilisasi Dual-Hand berbasis handedness (skor >= 0.70) + nearest-palm + gating radius + anti-swap & anti-promosi paksa
        int primarySourceIndex = -1;
        int secondarySourceIndex = -1;
        bool primaryJumpedOutsideGate = false;
        bool secondaryJumpedOutsideGate = false;

        if (validHandCount >= 2)
        {
            WorkerHandRecord rec0 = snap.hands[0];
            WorkerHandRecord rec1 = snap.hands[1];
            Vector2 palm0 = ComputeNormalizedPalmCenter(rec0.landmarks);
            Vector2 palm1 = ComputeNormalizedPalmCenter(rec1.landmarks);
            TrackedHandedness handed0 = rec0.handedness;
            TrackedHandedness handed1 = rec1.handedness;

            if (primarySlotActive && secondarySlotActive)
            {
                float cost0P = ComputeSlotAssociationCost(palm0, handed0, lastPrimaryPalmNormXY, primarySlotHandedness, out bool gate0P);
                float cost1S = ComputeSlotAssociationCost(palm1, handed1, lastSecondaryPalmNormXY, secondarySlotHandedness, out bool gate1S);
                float cost1P = ComputeSlotAssociationCost(palm1, handed1, lastPrimaryPalmNormXY, primarySlotHandedness, out bool gate1P);
                float cost0S = ComputeSlotAssociationCost(palm0, handed0, lastSecondaryPalmNormXY, secondarySlotHandedness, out bool gate0S);

                if ((cost1P + cost0S) < (cost0P + cost1S))
                {
                    primarySourceIndex = 1;
                    secondarySourceIndex = 0;
                    primaryJumpedOutsideGate = !gate1P;
                    secondaryJumpedOutsideGate = !gate0S;
                }
                else
                {
                    primarySourceIndex = 0;
                    secondarySourceIndex = 1;
                    primaryJumpedOutsideGate = !gate0P;
                    secondaryJumpedOutsideGate = !gate1S;
                }
            }
            else if (primarySlotActive)
            {
                float cost0P = ComputeSlotAssociationCost(palm0, handed0, lastPrimaryPalmNormXY, primarySlotHandedness, out bool gate0P);
                float cost1P = ComputeSlotAssociationCost(palm1, handed1, lastPrimaryPalmNormXY, primarySlotHandedness, out bool gate1P);
                bool prefer1 = (gate1P && !gate0P) || (gate1P == gate0P && cost1P < cost0P);
                if (prefer1)
                {
                    primarySourceIndex = 1;
                    secondarySourceIndex = 0;
                    primaryJumpedOutsideGate = !gate1P;
                }
                else
                {
                    primarySourceIndex = 0;
                    secondarySourceIndex = 1;
                    primaryJumpedOutsideGate = !gate0P;
                }
            }
            else if (secondarySlotActive)
            {
                float cost0S = ComputeSlotAssociationCost(palm0, handed0, lastSecondaryPalmNormXY, secondarySlotHandedness, out bool gate0S);
                float cost1S = ComputeSlotAssociationCost(palm1, handed1, lastSecondaryPalmNormXY, secondarySlotHandedness, out bool gate1S);
                bool prefer1 = (gate1S && !gate0S) || (gate1S == gate0S && cost1S < cost0S);
                if (prefer1)
                {
                    secondarySourceIndex = 1;
                    primarySourceIndex = 0;
                    secondaryJumpedOutsideGate = !gate1S;
                }
                else
                {
                    secondarySourceIndex = 0;
                    primarySourceIndex = 1;
                    secondaryJumpedOutsideGate = !gate0S;
                }
            }
            else
            {
                // Kedua slot baru masuk bersamaan: jika handedness jelas, pilih Right sebagai primer secara deterministik
                if (handed0 == TrackedHandedness.Left && handed1 == TrackedHandedness.Right)
                {
                    primarySourceIndex = 1;
                    secondarySourceIndex = 0;
                }
                else
                {
                    primarySourceIndex = 0;
                    secondarySourceIndex = 1;
                }
            }

            lastPrimaryPalmNormXY = primarySourceIndex == 0 ? palm0 : palm1;
            lastSecondaryPalmNormXY = secondarySourceIndex == 0 ? palm0 : palm1;
            TrackedHandedness matchedPrimaryHanded = primarySourceIndex == 0 ? handed0 : handed1;
            TrackedHandedness matchedSecondaryHanded = secondarySourceIndex == 0 ? handed0 : handed1;
            if (matchedPrimaryHanded != TrackedHandedness.Unknown) primarySlotHandedness = matchedPrimaryHanded;
            if (matchedSecondaryHanded != TrackedHandedness.Unknown) secondarySlotHandedness = matchedSecondaryHanded;
        }
        else
        {
            // Hanya 1 tangan terdeteksi: cocokkan ke slot asalnya — DILARANG mempromosikan tangan sekunder ke slot primer 0!
            WorkerHandRecord rec0 = snap.hands[0];
            Vector2 palm0 = ComputeNormalizedPalmCenter(rec0.landmarks);
            TrackedHandedness handed0 = rec0.handedness;

            if (primarySlotActive && secondarySlotActive)
            {
                float costP = ComputeSlotAssociationCost(palm0, handed0, lastPrimaryPalmNormXY, primarySlotHandedness, out bool gateP);
                float costS = ComputeSlotAssociationCost(palm0, handed0, lastSecondaryPalmNormXY, secondarySlotHandedness, out bool gateS);

                bool matchSecondary;
                if (gateS && !gateP)
                {
                    matchSecondary = true;
                }
                else if (gateP && !gateS)
                {
                    matchSecondary = false;
                }
                else if (handed0 != TrackedHandedness.Unknown
                         && secondarySlotHandedness != TrackedHandedness.Unknown
                         && handed0 == secondarySlotHandedness
                         && handed0 != primarySlotHandedness)
                {
                    matchSecondary = true;
                }
                else if (handed0 != TrackedHandedness.Unknown
                         && primarySlotHandedness != TrackedHandedness.Unknown
                         && handed0 == primarySlotHandedness
                         && handed0 != secondarySlotHandedness)
                {
                    matchSecondary = false;
                }
                else
                {
                    matchSecondary = costS < costP;
                }

                if (matchSecondary)
                {
                    secondarySourceIndex = 0;
                    secondaryJumpedOutsideGate = !gateS;
                    lastSecondaryPalmNormXY = palm0;
                    if (handed0 != TrackedHandedness.Unknown) secondarySlotHandedness = handed0;
                }
                else
                {
                    primarySourceIndex = 0;
                    primaryJumpedOutsideGate = !gateP;
                    lastPrimaryPalmNormXY = palm0;
                    if (handed0 != TrackedHandedness.Unknown) primarySlotHandedness = handed0;
                }
            }
            else if (secondarySlotActive)
            {
                // Slot sekunder masih aktif & primer sudah habis: tetap cocokkan ke slot sekunder bila sesuai identitas/gating radius
                ComputeSlotAssociationCost(palm0, handed0, lastSecondaryPalmNormXY, secondarySlotHandedness, out bool gateS);
                bool matchesSecondaryHanded = handed0 != TrackedHandedness.Unknown
                                              && secondarySlotHandedness != TrackedHandedness.Unknown
                                              && handed0 == secondarySlotHandedness;
                bool contradictsSecondaryHanded = handed0 != TrackedHandedness.Unknown
                                                  && secondarySlotHandedness != TrackedHandedness.Unknown
                                                  && handed0 != secondarySlotHandedness;

                if (!contradictsSecondaryHanded && (gateS || matchesSecondaryHanded))
                {
                    secondarySourceIndex = 0;
                    secondaryJumpedOutsideGate = !gateS;
                    lastSecondaryPalmNormXY = palm0;
                    if (handed0 != TrackedHandedness.Unknown) secondarySlotHandedness = handed0;
                }
                else
                {
                    // Tangan berbeda (primer baru masuk kembali) -> tempatkan bersih di slot primer
                    primarySourceIndex = 0;
                    lastPrimaryPalmNormXY = palm0;
                    primarySlotHandedness = handed0;
                }
            }
            else if (primarySlotActive)
            {
                ComputeSlotAssociationCost(palm0, handed0, lastPrimaryPalmNormXY, primarySlotHandedness, out bool gateP);
                bool contradictsPrimaryHanded = handed0 != TrackedHandedness.Unknown
                                                && primarySlotHandedness != TrackedHandedness.Unknown
                                                && handed0 != primarySlotHandedness;
                if (contradictsPrimaryHanded && !gateP)
                {
                    // Tangan lain muncul saat primer jeda sesaat -> masukkan ke slot sekunder agar primer tidak melompat
                    secondarySourceIndex = 0;
                    lastSecondaryPalmNormXY = palm0;
                    secondarySlotHandedness = handed0;
                }
                else
                {
                    primarySourceIndex = 0;
                    primaryJumpedOutsideGate = !gateP;
                    lastPrimaryPalmNormXY = palm0;
                    if (handed0 != TrackedHandedness.Unknown) primarySlotHandedness = handed0;
                }
            }
            else
            {
                primarySourceIndex = 0;
                lastPrimaryPalmNormXY = palm0;
                primarySlotHandedness = handed0;
            }
        }

        timeSinceLastValidHand = 0f;

        if (primarySourceIndex >= 0)
        {
            if (primaryJumpedOutsideGate && primarySlotActive)
            {
                ResetLandmarkOneEuroFilters();
                handGestureDetector.RestartEntryWarmup();
            }

            float primarySampleDt = (lastPrimaryFilterTimestampMs > 0 && timestampMs > lastPrimaryFilterTimestampMs)
                ? Mathf.Clamp((timestampMs - lastPrimaryFilterTimestampMs) * 0.001f, OneEuroMinDtSeconds, OneEuroMaxDtSeconds)
                : Mathf.Clamp(Time.unscaledDeltaTime, OneEuroMinDtSeconds, OneEuroMaxDtSeconds);
            lastPrimaryFilterTimestampMs = timestampMs;

            primarySlotActive = true;
            primaryMissedLastInference = false;
            primaryMissingTimer = 0f;

            WorkerHandRecord primaryRec = snap.hands[primarySourceIndex];
            UpdateHandVisualsFromLandmarks(
                primaryRec.landmarks,
                primaryRec.hasWorldLandmarks ? primaryRec.worldLandmarks : null,
                mainCam,
                primarySampleDt);
        }
        else if (primarySlotActive)
        {
            primaryMissedLastInference = true;
        }

        if (secondarySourceIndex >= 0)
        {
            if (secondaryJumpedOutsideGate && secondarySlotActive)
            {
                ResetSecondaryLandmarkOneEuroFilters();
                secondaryHandGestureDetector.RestartEntryWarmup();
            }

            float secondarySampleDt = (lastSecondaryFilterTimestampMs > 0 && timestampMs > lastSecondaryFilterTimestampMs)
                ? Mathf.Clamp((timestampMs - lastSecondaryFilterTimestampMs) * 0.001f, OneEuroMinDtSeconds, OneEuroMaxDtSeconds)
                : Mathf.Clamp(Time.unscaledDeltaTime, OneEuroMinDtSeconds, OneEuroMaxDtSeconds);
            lastSecondaryFilterTimestampMs = timestampMs;

            secondarySlotActive = true;
            secondaryMissedLastInference = false;
            secondaryMissingTimer = 0f;

            WorkerHandRecord secondaryRec = snap.hands[secondarySourceIndex];
            UpdateSecondaryHandVisualsFromLandmarks(
                secondaryRec.landmarks,
                secondaryRec.hasWorldLandmarks ? secondaryRec.worldLandmarks : null,
                mainCam,
                secondarySampleDt);
        }
        else if (secondarySlotActive)
        {
            secondaryMissedLastInference = true;
        }

        return true;
    }

    private float GetActiveFrameDropGraceSecondsForSlot(int handSlot)
    {
        bool isCarryMode = activeInteractionMode == ActiveInteractionMode.GateGrab
                           || activeInteractionMode == ActiveInteractionMode.CableDrag
                           || activeInteractionMode == ActiveInteractionMode.OutputHold;
        return (isCarryMode && activeInteractionHandSlot == handSlot)
            ? HandCarryFrameDropGraceSeconds
            : HandFrameDropGraceSeconds;
    }

    private void TickHandSlotFrameDropGrace(float dt)
    {
        if (primarySlotActive && primaryMissedLastInference)
        {
            primaryMissingTimer += dt;
            if (primaryMissingTimer >= GetActiveFrameDropGraceSecondsForSlot(0))
            {
                float waited = primaryMissingTimer;
                ExpirePrimaryHandSlot(waited);
            }
        }

        if (secondarySlotActive && secondaryMissedLastInference)
        {
            secondaryMissingTimer += dt;
            if (secondaryMissingTimer >= GetActiveFrameDropGraceSecondsForSlot(1))
            {
                float waited = secondaryMissingTimer;
                ExpireSecondaryHandSlot(waited);
            }
        }
    }

    private void ExpirePrimaryHandSlot(float consumedGraceSeconds = 0f)
    {
        if (consumedGraceSeconds > 0f)
        {
            handGestureDetector.ConsumeExternalGraceSeconds(consumedGraceSeconds);
        }
        primarySlotActive = false;
        primaryMissedLastInference = false;
        primaryMissingTimer = 0f;
        primarySlotHandedness = TrackedHandedness.Unknown;
        isHandDetected = false;
        wasHandDetected = false;
        rawPinching = false;
        isIndexOnlyWandPose = false;
        hoveredGateSlotIndex = -1;
        hoveredInputNode = null;
        pinchBeginGraceTimers[0] = 0f;
        inputADwellTimers[0] = 0f;
        inputBDwellTimers[0] = 0f;
        inputARequiresExitRearmBySlot[0] = false;
        inputBRequiresExitRearmBySlot[0] = false;
        ClearInputPushHoverState(0);
        inputSwitchMagnetOffsetBySlot[0] = Vector2.zero;
        circleSummoners[0].ClearPoseVoteBuffer();
        ResetLandmarkOneEuroFilters();
        SetHoverRingVisible(false);
    }

    private void ExpireSecondaryHandSlot(float consumedGraceSeconds = 0f)
    {
        if (consumedGraceSeconds > 0f)
        {
            secondaryHandGestureDetector.ConsumeExternalGraceSeconds(consumedGraceSeconds);
        }
        secondarySlotActive = false;
        secondaryMissedLastInference = false;
        secondaryMissingTimer = 0f;
        secondarySlotHandedness = TrackedHandedness.Unknown;
        isSecondaryHandDetected = false;
        wasSecondaryHandDetected = false;
        secondaryHoveredGateSlotIndex = -1;
        secondaryHoveredInputNode = null;
        pinchBeginGraceTimers[1] = 0f;
        inputADwellTimers[1] = 0f;
        inputBDwellTimers[1] = 0f;
        inputARequiresExitRearmBySlot[1] = false;
        inputBRequiresExitRearmBySlot[1] = false;
        ClearInputPushHoverState(1);
        inputSwitchMagnetOffsetBySlot[1] = Vector2.zero;
        circleSummoners[1].ClearPoseVoteBuffer();
        if (!secondaryHandGestureDetector.IsFistActive && !secondaryHandGestureDetector.IsPinchActive)
        {
            secondaryHandGestureDetector.Reset();
        }
        ResetSecondaryLandmarkOneEuroFilters();
    }

    private static bool IsValidHandLandmarkCandidateWorker(List<NormalizedLandmark> landmarks)
    {
        if (landmarks == null || landmarks.Count < LandmarkCount)
        {
            return false;
        }

        NormalizedLandmark l0 = landmarks[0];
        NormalizedLandmark l5 = landmarks[5];
        NormalizedLandmark l9 = landmarks[9];
        NormalizedLandmark l17 = landmarks[17];
        if (float.IsNaN(l0.x) || float.IsNaN(l0.y) || float.IsNaN(l9.x) || float.IsNaN(l9.y))
        {
            return false;
        }

        float dx09 = l0.x - l9.x;
        float dy09 = l0.y - l9.y;
        float dx517 = l5.x - l17.x;
        float dy517 = l5.y - l17.y;
        double d09 = Math.Sqrt((dx09 * dx09) + (dy09 * dy09));
        double d517 = Math.Sqrt((dx517 * dx517) + (dy517 * dy517));
        return (0.5 * (d09 + d517)) >= MinCandidateRawPalmScale;
    }

    private TrackedHandedness ExtractHandednessWorker(int sourceIndex)
    {
        if (cachedResult.handedness == null || sourceIndex < 0 || sourceIndex >= cachedResult.handedness.Count)
        {
            return TrackedHandedness.Unknown;
        }

        List<Category> cats = cachedResult.handedness[sourceIndex].categories;
        if (cats == null || cats.Count == 0)
        {
            return TrackedHandedness.Unknown;
        }

        int bestIdx = 0;
        float bestScore = cats[0].score;
        for (int i = 1; i < cats.Count; i++)
        {
            if (cats[i].score > bestScore)
            {
                bestScore = cats[i].score;
                bestIdx = i;
            }
        }

        if (bestScore < HandednessConfidenceThreshold)
        {
            return TrackedHandedness.Unknown;
        }

        string label = cats[bestIdx].categoryName;
        if (string.IsNullOrEmpty(label))
        {
            label = cats[bestIdx].displayName;
        }

        if (string.Equals(label, "Left", StringComparison.OrdinalIgnoreCase))
        {
            return TrackedHandedness.Left;
        }
        if (string.Equals(label, "Right", StringComparison.OrdinalIgnoreCase))
        {
            return TrackedHandedness.Right;
        }
        return TrackedHandedness.Unknown;
    }

    private static float ComputeSlotAssociationCost(
        Vector2 candPalm,
        TrackedHandedness candHanded,
        Vector2 slotPalm,
        TrackedHandedness slotHanded,
        out bool withinGate)
    {
        float dist = Vector2.Distance(candPalm, slotPalm);
        if (candHanded != TrackedHandedness.Unknown && slotHanded != TrackedHandedness.Unknown)
        {
            if (candHanded == slotHanded)
            {
                withinGate = dist <= HandAssociationMatchedHandednessMaxDistNorm;
                return dist * 0.65f;
            }

            withinGate = false;
            return dist + 0.65f;
        }

        withinGate = dist <= HandAssociationMaxDistNorm;
        return dist;
    }

    private void EnsureDownscaleLutBuilt(
        int srcW,
        int srcH,
        int dstW,
        int dstH,
        int rotAngle,
        bool vMirror,
        bool hMirror)
    {
        if (lutSrcW == srcW
            && lutSrcH == srcH
            && lutDstW == dstW
            && lutDstH == dstH
            && lutRotAngle == rotAngle
            && lutVMirror == vMirror
            && lutHMirror == hMirror
            && downscaleLutX0 != null
            && downscaleLutY0 != null)
        {
            return;
        }

        lutSrcW = srcW;
        lutSrcH = srcH;
        lutDstW = dstW;
        lutDstH = dstH;
        lutRotAngle = rotAngle;
        lutVMirror = vMirror;
        lutHMirror = hMirror;

        bool isRotated90 = (rotAngle == 90 || rotAngle == 270);
        int xLutLen = isRotated90 ? dstH : dstW;
        int yLutLen = isRotated90 ? dstW : dstH;

        if (downscaleLutX0 == null || downscaleLutX0.Length != xLutLen)
        {
            downscaleLutX0 = new int[xLutLen];
            downscaleLutX1 = new int[xLutLen];
        }
        if (downscaleLutY0 == null || downscaleLutY0.Length != yLutLen)
        {
            downscaleLutY0 = new int[yLutLen];
            downscaleLutY1 = new int[yLutLen];
        }

        float invDstW = 1f / dstW;
        float invDstH = 1f / dstH;
        int maxSrcX = srcW - 1;
        int maxSrcY = srcH - 1;
        float halfSpanX = 0.25f * srcW * (isRotated90 ? invDstH : invDstW);
        float halfSpanY = 0.25f * srcH * (isRotated90 ? invDstW : invDstH);

        if (!isRotated90)
        {
            bool flip180 = (rotAngle == 180);
            for (int dx = 0; dx < dstW; dx++)
            {
                float uScreen = (dx + 0.5f) * invDstW;
                float rx = hMirror ? (0.5f - uScreen) : (uScreen - 0.5f);
                float lx = flip180 ? -rx : rx;
                float fx = (lx + 0.5f) * srcW;

                int px0 = (int)(fx - halfSpanX);
                int px1 = (int)(fx + halfSpanX);
                downscaleLutX0[dx] = px0 < 0 ? 0 : (px0 > maxSrcX ? maxSrcX : px0);
                downscaleLutX1[dx] = px1 < 0 ? 0 : (px1 > maxSrcX ? maxSrcX : px1);
            }

            for (int dy = 0; dy < dstH; dy++)
            {
                float ry = 0.5f - ((dy + 0.5f) * invDstH);
                float ly = flip180 ? -ry : ry;
                float fy = ((vMirror ? -ly : ly) + 0.5f) * srcH;

                int py0 = (int)(fy - halfSpanY);
                int py1 = (int)(fy + halfSpanY);
                int cpy0 = py0 < 0 ? 0 : (py0 > maxSrcY ? maxSrcY : py0);
                int cpy1 = py1 < 0 ? 0 : (py1 > maxSrcY ? maxSrcY : py1);
                downscaleLutY0[dy] = cpy0 * srcW;
                downscaleLutY1[dy] = cpy1 * srcW;
            }
        }
        else
        {
            bool rot90 = (rotAngle == 90);
            for (int dy = 0; dy < dstH; dy++)
            {
                float ry = 0.5f - ((dy + 0.5f) * invDstH);
                float lx = rot90 ? -ry : ry;
                float fx = (lx + 0.5f) * srcW;

                int px0 = (int)(fx - halfSpanX);
                int px1 = (int)(fx + halfSpanX);
                downscaleLutX0[dy] = px0 < 0 ? 0 : (px0 > maxSrcX ? maxSrcX : px0);
                downscaleLutX1[dy] = px1 < 0 ? 0 : (px1 > maxSrcX ? maxSrcX : px1);
            }

            for (int dx = 0; dx < dstW; dx++)
            {
                float uScreen = (dx + 0.5f) * invDstW;
                float rx = hMirror ? (0.5f - uScreen) : (uScreen - 0.5f);
                float ly = rot90 ? rx : -rx;
                float fy = ((vMirror ? -ly : ly) + 0.5f) * srcH;

                int py0 = (int)(fy - halfSpanY);
                int py1 = (int)(fy + halfSpanY);
                int cpy0 = py0 < 0 ? 0 : (py0 > maxSrcY ? maxSrcY : py0);
                int cpy1 = py1 < 0 ? 0 : (py1 > maxSrcY ? maxSrcY : py1);
                downscaleLutY0[dx] = cpy0 * srcW;
                downscaleLutY1[dx] = cpy1 * srcW;
            }
        }
    }

    /// <summary>
    /// Konversi RGBA32-&gt;Srgb (3 byte/piksel) + rotasi/mirror 1D-LUT di Worker Thread (0 panggilan API UnityEngine, Zero-GC):
    /// - Jalur GPU Readback (<c>useTwoTap = false</c>): 1-tap langsung (downscale bilinear sudah dikerjakan Adreno 710 GPU).
    /// - Jalur Fallback GetPixels32 (<c>useTwoTap = true</c>): 2-tap diagonal anti-aliasing untuk tepi jari tetap halus.
    /// </summary>
    private void FillWorkerRgbBufferFromRgba(
        NativeArray<byte> srcRgba,
        NativeArray<byte> dstRgb,
        int srcW,
        int srcH,
        int dstW,
        int dstH,
        int rotAngle,
        bool vMirror,
        bool hMirror,
        bool useTwoTap)
    {
        EnsureDownscaleLutBuilt(srcW, srcH, dstW, dstH, rotAngle, vMirror, hMirror);

        int[] lutX0 = downscaleLutX0;
        int[] lutX1 = downscaleLutX1;
        int[] lutY0 = downscaleLutY0;
        int[] lutY1 = downscaleLutY1;
        int dstIdx = 0;

        if (rotAngle != 90 && rotAngle != 270)
        {
            if (!useTwoTap)
            {
                for (int dy = 0; dy < dstH; dy++)
                {
                    int row0 = lutY0[dy];
                    for (int dx = 0; dx < dstW; dx++)
                    {
                        int s0 = (row0 + lutX0[dx]) << 2;
                        dstRgb[dstIdx++] = srcRgba[s0];
                        dstRgb[dstIdx++] = srcRgba[s0 + 1];
                        dstRgb[dstIdx++] = srcRgba[s0 + 2];
                    }
                }
            }
            else
            {
                for (int dy = 0; dy < dstH; dy++)
                {
                    int row0 = lutY0[dy];
                    int row1 = lutY1[dy];
                    for (int dx = 0; dx < dstW; dx++)
                    {
                        int s0 = (row0 + lutX0[dx]) << 2;
                        int s1 = (row1 + lutX1[dx]) << 2;
                        dstRgb[dstIdx++] = (byte)((srcRgba[s0] + srcRgba[s1] + 1) >> 1);
                        dstRgb[dstIdx++] = (byte)((srcRgba[s0 + 1] + srcRgba[s1 + 1] + 1) >> 1);
                        dstRgb[dstIdx++] = (byte)((srcRgba[s0 + 2] + srcRgba[s1 + 2] + 1) >> 1);
                    }
                }
            }
        }
        else
        {
            if (!useTwoTap)
            {
                for (int dy = 0; dy < dstH; dy++)
                {
                    int col0 = lutX0[dy];
                    for (int dx = 0; dx < dstW; dx++)
                    {
                        int s0 = (lutY0[dx] + col0) << 2;
                        dstRgb[dstIdx++] = srcRgba[s0];
                        dstRgb[dstIdx++] = srcRgba[s0 + 1];
                        dstRgb[dstIdx++] = srcRgba[s0 + 2];
                    }
                }
            }
            else
            {
                for (int dy = 0; dy < dstH; dy++)
                {
                    int col0 = lutX0[dy];
                    int col1 = lutX1[dy];
                    for (int dx = 0; dx < dstW; dx++)
                    {
                        int s0 = (lutY0[dx] + col0) << 2;
                        int s1 = (lutY1[dx] + col1) << 2;
                        dstRgb[dstIdx++] = (byte)((srcRgba[s0] + srcRgba[s1] + 1) >> 1);
                        dstRgb[dstIdx++] = (byte)((srcRgba[s0 + 1] + srcRgba[s1 + 1] + 1) >> 1);
                        dstRgb[dstIdx++] = (byte)((srcRgba[s0 + 2] + srcRgba[s1 + 2] + 1) >> 1);
                    }
                }
            }
        }
    }

    private static Vector2 ComputeNormalizedPalmCenter(List<NormalizedLandmark> landmarks)
    {
        NormalizedLandmark l0 = landmarks[0];
        NormalizedLandmark l5 = landmarks[5];
        NormalizedLandmark l9 = landmarks[9];
        NormalizedLandmark l13 = landmarks[13];
        NormalizedLandmark l17 = landmarks[17];
        return new Vector2(
            (l0.x + l5.x + l9.x + l13.x + l17.x) * 0.20f,
            (l0.y + l5.y + l9.y + l13.y + l17.y) * 0.20f);
    }

    private void ResetLandmarkOneEuroFilters()
    {
        hologramVisual?.ResetHandSkeletonFilters(0, clearAlphaImmediate: false);
        handGestureDetector.ResetCursorFilters();
        lastPrimaryFilterTimestampMs = 0;
    }

    private void ResetSecondaryLandmarkOneEuroFilters()
    {
        hologramVisual?.ResetHandSkeletonFilters(1, clearAlphaImmediate: false);
        secondaryHandGestureDetector.ResetCursorFilters();
        lastSecondaryFilterTimestampMs = 0;
    }

    private bool ShouldFreezeDepthBaselineForSlot(int handSlot)
    {
        if (IsAnyTwoHandModeActive || isCarryPanActive)
        {
            return true;
        }

        if (activeInteractionMode != ActiveInteractionMode.Idle && activeInteractionHandSlot == handSlot)
        {
            return true;
        }

        if (inputPushHoverTargetBySlot[handSlot] != null || inputADwellTimers[handSlot] > 0f || inputBDwellTimers[handSlot] > 0f)
        {
            return true;
        }

        AirWeavingHandGestureDetector det = GetHandDetector(handSlot);
        return det.IsFistActive || det.IsPinchActive || det.IsPreFistCurling || det.IsPrePinchClosing;
    }

    private void ProjectHandLandmarksVolumetric3D(
        List<NormalizedLandmark> landmarks,
        AirWeavingHandGestureDetector detector,
        Camera mainCam,
        Vector3[] outWorldLandmarks)
    {
        Transform camTransform = mainCam.transform;
        float nearClip = mainCam != null ? mainCam.nearClipPlane : 0.15f;
        float minSafeDepth = Mathf.Max(MinHandRigCameraDepth, nearClip + HandNearClipMinOffsetMeters);
        float maxSafeDepth = VideoQuadDepth - HandMaxBehindVideoQuadMarginMeters;

        float stageRefDepth = stageController != null ? stageController.CurrentStageDepthZ : WorkbenchDepth;
        float neutralHandDepth = Mathf.Clamp(
            stageRefDepth - SingleHandNeutralHoverFrontOffset,
            minSafeDepth,
            Mathf.Max(minSafeDepth, maxSafeDepth - 0.50f));
        float baseHandCamDepth = Mathf.Clamp(
            neutralHandDepth + (detector.DepthLogDelta * SingleHandDepthMetersPerLog),
            minSafeDepth,
            maxSafeDepth);

        // Skala kedalaman volumetrik relatif jari (-lm.z -> jari yang dijulurkan ke kamera menonjol maju ke papan 3D)
        float volumetricZScale = DepthParallaxScale * Mathf.Clamp(baseHandCamDepth / WorkbenchDepth, 0.45f, 1.80f);

        for (int i = 0; i < LandmarkCount; i++)
        {
            NormalizedLandmark lm = landmarks[i];
            float relForwardZ = -Mathf.Clamp(lm.z, -0.18f, 0.18f);
            float localZ = Mathf.Clamp(baseHandCamDepth + (relForwardZ * volumetricZScale), minSafeDepth, maxSafeDepth);
            float rayDepthRatio = localZ / VideoQuadDepth;
            float localX = (lm.x - 0.5f) * currentVideoDisplayWidth * rayDepthRatio;
            float localY = (0.5f - lm.y) * currentVideoDisplayHeight * rayDepthRatio;
            outWorldLandmarks[i] = camTransform.TransformPoint(new Vector3(localX, localY, localZ));
        }
    }

    private void UpdateHandVisualsFromLandmarks(
        List<NormalizedLandmark> landmarks,
        List<Landmark> worldLandmarks,
        Camera mainCam,
        float sampleDt)
    {
        Transform camTransform = mainCam.transform;
        float camAspect = (mainCam != null && mainCam.aspect > 0.1f) ? mainCam.aspect : (16f / 9f);

        bool justAppeared = !wasHandDetected;
        if (justAppeared)
        {
            handGestureDetector.RestartEntryWarmup();
        }

        // Perbarui Rigid Metacarpal Palm Span + Baseline Adaptif Monocular Z-Depth tepat 1x per frame inferensi
        bool freezeBaseline = ShouldFreezeDepthBaselineForSlot(0);
        handGestureDetector.UpdateRigidPalmSpanOnly(landmarks, sampleDt, camAspect, freezeBaseline);

        // Proyeksikan 21 titik mentah volumetrik 3D (palmScale logaritmik + -lm.z relatif + clamp aman nearClip..VideoQuad)
        ProjectHandLandmarksVolumetric3D(landmarks, handGestureDetector, mainCam, rawLandmarksWorld);

        hologramVisual?.PushHandSkeletonSample(0, rawLandmarksWorld, sampleDt, freshEntry: justAppeared);

        isHandDetected = true;
        wasHandDetected = true;
        timeSinceLastValidHand = 0f;

        // Perbarui detektor 3D Metrik (handWorldLandmarks + One-Euro 3D kursor interaksi Zero-GC)
        handGestureDetector.UpdateFromLandmarks(landmarks, worldLandmarks, rawLandmarksWorld, camTransform, WorkbenchDepth, camAspect);
        circleSummoners[0].RecordPoseSample(handGestureDetector.CurrentSummonPose);

        // Sinkronkan status kompatibilitas Pinch legacy dengan rasio 3D metrik terbaru
        currentPinchRatio = handGestureDetector.PinchRatio;
        if (!rawPinching && currentPinchRatio < PinchRatioActivateThreshold)
        {
            rawPinching = true;
        }
        else if (rawPinching && currentPinchRatio > PinchRatioReleaseThreshold)
        {
            rawPinching = false;
        }

        // Deteksi pose "Tongkat Telunjuk" (Index-Only Wand) untuk kompatibilitas Air-Draw legacy
        isIndexOnlyWandPose = EvaluateIndexOnlyWandPose(landmarks);

        indexTipWorldPosition = handGestureDetector.IndexTipWorldPosition;
        pinchWorldPosition = handGestureDetector.PalmCursorWorldPosition;

        // Simpan koordinat XY lokal yang sudah disatukan pada bidang sinar referensi (WorkbenchDepth)
        indexWorkbenchLocalXY = handGestureDetector.IndexTipLocalXY;
        pinchWorkbenchLocalXY = handGestureDetector.PalmCursorLocalXY;

        if (pinchCursorRingRenderer != null && pinchCursorRingRenderer.enabled)
        {
            pinchCursorRingRenderer.enabled = false;
        }
        if (indexTrailRenderer != null && indexTrailRenderer.emitting)
        {
            indexTrailRenderer.emitting = false;
            indexTrailRenderer.Clear();
        }

        if (indexTrailObj != null)
        {
            indexTrailObj.transform.position = indexTipWorldPosition;
            if (justAppeared && indexTrailRenderer != null)
            {
                indexTrailRenderer.Clear();
            }
        }

        UpdatePinchCursorRing(camTransform, currentPinchRatio);
    }

    private void UpdateSecondaryHandVisualsFromLandmarks(
        List<NormalizedLandmark> landmarks,
        List<Landmark> worldLandmarks,
        Camera mainCam,
        float sampleDt)
    {
        Transform camTransform = mainCam.transform;
        float camAspect = (mainCam != null && mainCam.aspect > 0.1f) ? mainCam.aspect : (16f / 9f);

        bool justAppeared = !wasSecondaryHandDetected;
        if (justAppeared)
        {
            secondaryHandGestureDetector.RestartEntryWarmup();
        }

        // Perbarui Rigid Metacarpal Palm Span + Baseline Adaptif Monocular Z-Depth tepat 1x per frame inferensi
        bool freezeBaseline = ShouldFreezeDepthBaselineForSlot(1);
        secondaryHandGestureDetector.UpdateRigidPalmSpanOnly(landmarks, sampleDt, camAspect, freezeBaseline);

        ProjectHandLandmarksVolumetric3D(landmarks, secondaryHandGestureDetector, mainCam, secondaryRawLandmarksWorld);

        hologramVisual?.PushHandSkeletonSample(1, secondaryRawLandmarksWorld, sampleDt, freshEntry: justAppeared);

        isSecondaryHandDetected = true;
        wasSecondaryHandDetected = true;
        timeSinceLastValidHand = 0f;

        secondaryHandGestureDetector.UpdateFromLandmarks(landmarks, worldLandmarks, secondaryRawLandmarksWorld, camTransform, WorkbenchDepth, camAspect);
        circleSummoners[1].RecordPoseSample(secondaryHandGestureDetector.CurrentSummonPose);
    }

    private static bool EvaluateIndexOnlyWandPose(List<NormalizedLandmark> landmarks)
    {
        NormalizedLandmark wrist = landmarks[0];
        float dIndexTip = LandmarkDist2D(landmarks[8], wrist);
        float dIndexPip = LandmarkDist2D(landmarks[6], wrist);
        if (dIndexTip <= dIndexPip * 1.12f)
        {
            return false;
        }

        int curledCount = 0;
        if (LandmarkDist2D(landmarks[12], wrist) < LandmarkDist2D(landmarks[10], wrist) * 1.08f)
        {
            curledCount++;
        }
        if (LandmarkDist2D(landmarks[16], wrist) < LandmarkDist2D(landmarks[14], wrist) * 1.08f)
        {
            curledCount++;
        }
        if (LandmarkDist2D(landmarks[20], wrist) < LandmarkDist2D(landmarks[18], wrist) * 1.08f)
        {
            curledCount++;
        }

        return curledCount >= 2;
    }

    private static float LandmarkDist2D(NormalizedLandmark a, NormalizedLandmark b)
    {
        float dx = a.x - b.x;
        float dy = a.y - b.y;
        return Mathf.Sqrt((dx * dx) + (dy * dy));
    }

    private void UpdatePinchCursorRing(Transform camTransform, float pinchRatio)
    {
        if (pinchCursorRingRenderer == null || !pinchCursorRingRenderer.enabled)
        {
            return;
        }

        bool highlightActive = isPinching || rawPinching;
        Color ringColor = highlightActive ? PinchActiveColor : PinchIdleColor;
        if (pinchRingMat != null)
        {
            pinchRingMat.color = ringColor;
        }

        float tOpen = Mathf.Clamp01((pinchRatio - PinchRatioActivateThreshold) / 0.65f);
        float radius = highlightActive ? 0.030f : Mathf.Lerp(0.042f, 0.072f, tOpen);
        pinchCursorRingRenderer.widthMultiplier = highlightActive ? 0.011f : 0.007f;
        pinchCursorRingRenderer.startColor = ringColor;
        pinchCursorRingRenderer.endColor = ringColor;

        Vector3 right = camTransform.right;
        Vector3 up = camTransform.up;
        for (int i = 0; i < PinchRingSegments; i++)
        {
            float angle = (i / (float)PinchRingSegments) * Mathf.PI * 2f;
            float cos = Mathf.Cos(angle) * radius;
            float sin = Mathf.Sin(angle) * radius;
            pinchRingPoints[i] = pinchWorldPosition + (right * cos) + (up * sin);
        }

        pinchCursorRingRenderer.SetPositions(pinchRingPoints);
    }

    private void UpdateHoverHighlightVisual(Camera mainCam, float aspectScale)
    {
        if (hoverHighlightRingRenderer == null || mainCam == null)
        {
            return;
        }

        Vector3 centerWorld;
        float radius;
        Color ringColor;
        Transform targetTransform = null;

        if (grabbedGateSlotIndex >= 0 && grabbedGateSlotIndex < gateSlots.Count)
        {
            WorkbenchGateSlot grabbed = gateSlots[grabbedGateSlotIndex];
            Vector2 homeXY = GetParkingSlotLocalXY(grabbed.slotIndex, aspectScale);
            bool returningToRack = grabbed.currentLocalXY.y > ParkReturnThresholdY
                                   || Vector2.Distance(grabbed.currentLocalXY, homeXY) <= ParkSlotSnapRadius;

            centerWorld = mainCam.transform.TransformPoint(new Vector3(grabbed.currentLocalXY.x, grabbed.currentLocalXY.y, grabbed.currentLocalZ));
            targetTransform = grabbed.node != null ? grabbed.node.transform : null;
            radius = 0.29f + (0.010f * Mathf.Sin(Time.unscaledTime * 8f));
            ringColor = returningToRack ? HoverReturnRackColor : HoverGrabbedRingColor;
        }
        else if (hoveredGateSlotIndex >= 0 && hoveredGateSlotIndex < gateSlots.Count)
        {
            WorkbenchGateSlot hovered = gateSlots[hoveredGateSlotIndex];
            centerWorld = mainCam.transform.TransformPoint(new Vector3(hovered.currentLocalXY.x, hovered.currentLocalXY.y, hovered.currentLocalZ));
            targetTransform = hovered.node != null ? hovered.node.transform : null;
            radius = (hovered.isPlacedOnWorkbench ? 0.28f : 0.18f) + (0.008f * Mathf.Sin(Time.unscaledTime * 7f));
            ringColor = HoverGateRingColor;
        }
        else if (hoveredInputNode != null)
        {
            centerWorld = hoveredInputNode.transform.position;
            targetTransform = hoveredInputNode.transform;
            radius = 0.29f + (0.008f * Mathf.Sin(Time.unscaledTime * 7f));
            ringColor = HoverInputRingColor;
        }
        else
        {
            SetHoverRingVisible(false);
            return;
        }

        SetHoverRingVisible(true);
        if (hoverRingMat != null)
        {
            hoverRingMat.color = ringColor;
        }

        hoverHighlightRingRenderer.startColor = ringColor;
        hoverHighlightRingRenderer.endColor = ringColor;
        hoverHighlightRingRenderer.widthMultiplier = grabbedGateSlotIndex >= 0 ? 0.009f : 0.007f;

        // Lingkari tepi alas AR_BaseTile3D secara presisi mengikuti kemiringan papan kerja (foreshortened ~50% di layar)
        Vector3 axisA = targetTransform != null ? targetTransform.right : mainCam.transform.right;
        Vector3 axisB = targetTransform != null ? targetTransform.forward : mainCam.transform.up;
        Vector3 ringCenter = targetTransform != null
            ? (centerWorld + (targetTransform.up * 0.045f))
            : centerWorld;

        for (int i = 0; i < HoverRingSegments; i++)
        {
            float angle = (i / (float)HoverRingSegments) * Mathf.PI * 2f;
            hoverRingPoints[i] = ringCenter + (axisA * (Mathf.Cos(angle) * radius)) + (axisB * (Mathf.Sin(angle) * radius));
        }

        hoverHighlightRingRenderer.SetPositions(hoverRingPoints);
    }

    private void HandleOutputTriggerVerify() => OnOutputVerifyRequested?.Invoke();

    private void HandleOutputTriggerNextLevel()
    {
        lastOutputHoldSfxStep = 0;
        if (activeInteractionMode == ActiveInteractionMode.OutputHold)
        {
            ExitInteractionMode(ActiveInteractionMode.OutputHold, triggerNeutralCooldown: true);
        }
        if (isAirWeavingActive && spatialSfx != null)
        {
            Vector3 pos = outputNode != null ? outputNode.transform.position : Vector3.zero;
            spatialSfx.PlayOutputHoldConfirm(pos);
        }
        OnOutputNextLevelRequested?.Invoke();
    }

    private void HandleCableGrabSfx()
    {
        hoverSfxPostActionSuppressTimer = HoverPostActionSuppressSeconds;
        if (isAirWeavingActive && spatialSfx != null && cableRouter != null)
        {
            spatialSfx.PlayCableGrab(cableRouter.LastSfxWorldPosition);
        }
    }

    private void HandleCableTargetLockSfx()
    {
        if (isAirWeavingActive && spatialSfx != null && cableRouter != null)
        {
            spatialSfx.PlayCableTargetLock(cableRouter.LastSfxWorldPosition);
        }
    }

    private void HandleCableTargetUnlockSfx()
    {
        if (isAirWeavingActive && spatialSfx != null && cableRouter != null)
        {
            spatialSfx.PlayCableTargetUnlock(cableRouter.LastSfxWorldPosition);
        }
    }

    private void HandleCableConnectSfx()
    {
        hoverSfxPostActionSuppressTimer = HoverPostActionSuppressSeconds;
        if (isAirWeavingActive && spatialSfx != null && cableRouter != null)
        {
            spatialSfx.PlayCableSnapConnect(cableRouter.LastSfxWorldPosition);
        }
        if (isAirWeavingActive)
        {
            gestureGuide?.NotifyCableConnected();
        }
    }

    private void HandleCableDetachSfx()
    {
        hoverSfxPostActionSuppressTimer = HoverPostActionSuppressSeconds;
        if (isAirWeavingActive && spatialSfx != null && cableRouter != null)
        {
            spatialSfx.PlayCableDetach(cableRouter.LastSfxWorldPosition);
        }
    }

    private void HandleCableRejectSfx()
    {
        hoverSfxPostActionSuppressTimer = HoverPostActionSuppressSeconds;
        if (isAirWeavingActive && spatialSfx != null && cableRouter != null)
        {
            spatialSfx.PlaySoftReject(cableRouter.LastSfxWorldPosition);
        }
    }

    private void UpdateHandWakeAudioFeedback(float dt)
    {
        bool overlayOrVerify = IsAnyUiOverlayOpen || (levelManager != null && levelManager.IsVerifying);
        if (IsAnyHandDetected)
        {
            if (!wasHandDetectedForWakeSfx
                && handAbsentForWakeSeconds >= HologramWakeMinAbsentSeconds
                && !overlayOrVerify
                && spatialSfx != null)
            {
                Vector3 wakePos = isHandDetected
                    ? handGestureDetector.PalmCursorWorldPosition
                    : secondaryHandGestureDetector.PalmCursorWorldPosition;
                spatialSfx.PlayHologramWake(wakePos);
            }
            wasHandDetectedForWakeSfx = true;
            handAbsentForWakeSeconds = 0f;
        }
        else
        {
            wasHandDetectedForWakeSfx = false;
            if (!overlayOrVerify)
            {
                handAbsentForWakeSeconds += dt;
            }
        }
    }

    private void UpdateContinuousHumFeedback(Camera mainCam, float dt)
    {
        if (spatialSfx == null)
        {
            return;
        }

        bool overlayOrVerify = IsAnyUiOverlayOpen || (levelManager != null && levelManager.IsVerifying);
        if (!isAirWeavingActive || !IsAnyHandDetected || overlayOrVerify || mainCam == null)
        {
            spatialSfx.StopHumImmediate();
            return;
        }

        if (cableRouter != null
            && cableRouter.IsDraggingCable
            && cableRouter.TryGetActiveDragFeedback(out Vector3 dragTipWorldPos, out float dragLength, out bool isLockedTarget))
        {
            spatialSfx.UpdateCableDragHum(dragTipWorldPos, dragLength, isLockedTarget, dt);
            return;
        }

        if (grabbedGateSlotIndex >= 0
            && grabbedGateSlotIndex < gateSlots.Count
            && gateSlots[grabbedGateSlotIndex] != null
            && gateSlots[grabbedGateSlotIndex].node != null)
        {
            WorkbenchGateSlot grabbed = gateSlots[grabbedGateSlotIndex];
            float normalizedSpeed = Mathf.Clamp01(grabbed.smoothedVelocityXY.magnitude / 2.60f);
            spatialSfx.UpdateGateDragHum(grabbed.node.transform.position, normalizedSpeed, wasGrabbedGateInDeleteZone, dt);
            return;
        }

        spatialSfx.StopHumImmediate();
    }

    private void ResetHoverSfxTracking()
    {
        lastHoverSfxCategory = HoverSfxCategory.None;
        lastHoverSfxNode = null;
        lastHoverSfxSubId = 0;
        hoverSfxNoneGraceTimer = 0f;
        hoverSfxPostActionSuppressTimer = 0f;
    }

    /// <summary>
    /// Memicu micro-tick elektronik ringan tepat 1x per transisi target hover baru (orb port, badan gerbang, atau sakelar Input A/B)
    /// untuk kedua tangan secara ambidextrous. Zero-GC.
    /// </summary>
    private void UpdateHoverSfxFeedback(float dt)
    {
        if (hoverSfxPostActionSuppressTimer > 0f)
        {
            hoverSfxPostActionSuppressTimer = Mathf.Max(0f, hoverSfxPostActionSuppressTimer - dt);
        }

        bool overlayOrVerify = IsAnyUiOverlayOpen || (levelManager != null && levelManager.IsVerifying);
        bool busyGesture = activeInteractionMode != ActiveInteractionMode.Idle
                           || grabbedGateSlotIndex >= 0
                           || (cableRouter != null && cableRouter.IsDraggingCable)
                           || IsAnyCircleSummonActive;

        if (!isAirWeavingActive || !IsAnyHandDetected || overlayOrVerify)
        {
            ResetHoverSfxTracking();
            return;
        }

        if (busyGesture)
        {
            hoverSfxNoneGraceTimer = 0f;
            return;
        }

        HoverSfxCategory curCategory = HoverSfxCategory.None;
        GateNode curNode = null;
        int curSubId = 0;
        Vector3 curWorldPos = Vector3.zero;

        GateNode anyHoveredInput = hoveredInputNode != null ? hoveredInputNode : secondaryHoveredInputNode;
        int anyHoveredGateIdx = hoveredGateSlotIndex >= 0 ? hoveredGateSlotIndex : secondaryHoveredGateSlotIndex;

        if (cableRouter != null && cableRouter.TryGetHoveredPortInfo(out GateNode portNode, out bool portIsOutput, out int portSlot, out Vector3 portWorldPos))
        {
            curCategory = HoverSfxCategory.PortOrb;
            curNode = portNode;
            curSubId = (portIsOutput ? 1 : 0) | ((portSlot & 3) << 1);
            curWorldPos = portWorldPos;
        }
        else if (anyHoveredInput != null)
        {
            curCategory = HoverSfxCategory.InputSwitch;
            curNode = anyHoveredInput;
            curSubId = anyHoveredInput == inputBNode ? 2 : 1;
            curWorldPos = anyHoveredInput.transform.position;
        }
        else if (anyHoveredGateIdx >= 0
                 && anyHoveredGateIdx < gateSlots.Count
                 && gateSlots[anyHoveredGateIdx] != null
                 && gateSlots[anyHoveredGateIdx].node != null)
        {
            curCategory = HoverSfxCategory.GateBody;
            curNode = gateSlots[anyHoveredGateIdx].node;
            curSubId = gateSlots[anyHoveredGateIdx].slotIndex + 1;
            curWorldPos = curNode.transform.position;
        }

        if (curCategory == HoverSfxCategory.None)
        {
            hoverSfxNoneGraceTimer += dt;
            if (hoverSfxNoneGraceTimer >= HoverNoneLeaveGraceSeconds)
            {
                lastHoverSfxCategory = HoverSfxCategory.None;
                lastHoverSfxNode = null;
                lastHoverSfxSubId = 0;
            }
            return;
        }

        hoverSfxNoneGraceTimer = 0f;
        if (curCategory != lastHoverSfxCategory || curNode != lastHoverSfxNode || curSubId != lastHoverSfxSubId)
        {
            lastHoverSfxCategory = curCategory;
            lastHoverSfxNode = curNode;
            lastHoverSfxSubId = curSubId;
            if (spatialSfx != null && hoverSfxPostActionSuppressTimer <= 0f)
            {
                spatialSfx.PlayHoverTick(curWorldPos);
            }
        }
    }

    private void RefreshActivePlacedGatesBuffer()
    {
        activePlacedGatesBuffer.Clear();
        for (int i = 0; i < gateSlots.Count; i++)
        {
            WorkbenchGateSlot slot = gateSlots[i];
            if (slot != null && slot.isPlacedOnWorkbench && slot.node != null && slot.node.isTracked)
            {
                activePlacedGatesBuffer.Add(slot.node);
            }
        }
    }

    private void UpdatePinchBeginGraceTimers(float dt)
    {
        if (IsAnyTwoHandModeActive || activeInteractionMode == ActiveInteractionMode.GateGrab)
        {
            pinchBeginGraceTimers[0] = 0f;
            pinchBeginGraceTimers[1] = 0f;
            return;
        }

        bool allowArmGrace = activeInteractionMode == ActiveInteractionMode.Idle
                             || activeInteractionMode == ActiveInteractionMode.CircleSummon;
        for (int s = 0; s < 2; s++)
        {
            bool detected = IsHandSlotDetected(s);
            AirWeavingHandGestureDetector det = GetHandDetector(s);
            if (!detected || !det.IsPinchActive || det.IsFistActive)
            {
                pinchBeginGraceTimers[s] = 0f;
            }
            else if (det.PinchJustStarted)
            {
                pinchBeginGraceTimers[s] = allowArmGrace ? PinchBeginGraceSeconds : 0f;
            }
            else if (pinchBeginGraceTimers[s] > 0f)
            {
                pinchBeginGraceTimers[s] = Mathf.Max(0f, pinchBeginGraceTimers[s] - dt);
            }
        }
    }

    private void UpdateCableRouterInteraction(Camera mainCam)
    {
        if (cableRouter == null) return;

        RefreshActivePlacedGatesBuffer();

        Transform stageRoot = stageController != null ? stageController.StageRoot : null;
        float stageZoom = stageController != null ? stageController.CurrentZoomScale : 1f;

        // Cegah pose Summon AND (1 telunjuk) / NOT (jempol) yang baru mulai Charging memakan rising-edge Pinch tepat di atas port
        if (activeInteractionMode == ActiveInteractionMode.CircleSummon
            && (activeInteractionHandSlot == 0 || activeInteractionHandSlot == 1)
            && circleSummoners[activeInteractionHandSlot].Stage == AirWeavingCircleSummoner.SummonStage.Charging)
        {
            int summonSlot = activeInteractionHandSlot;
            AirWeavingHandGestureDetector summonDet = GetHandDetector(summonSlot);
            bool summonPinchBegin = IsHandSlotDetected(summonSlot)
                                    && !summonDet.IsFistActive
                                    && (summonDet.PinchJustStarted || (summonDet.IsPinchActive && pinchBeginGraceTimers[summonSlot] > 0f));
            if (summonPinchBegin)
            {
                float portDist = Mathf.Min(
                    cableRouter.QueryNearestPortNormalizedDistance(summonDet.StabilizedPinchLocalXY, stageZoom),
                    cableRouter.QueryNearestPortNormalizedDistance(summonDet.PinchPointLocalXY, stageZoom));
                if (portDist <= 1.25f)
                {
                    ResetAllCircleSummoners();
                    ExitInteractionMode(ActiveInteractionMode.CircleSummon, triggerNeutralCooldown: false);
                }
            }
        }

        bool blockedByArbiter = (activeInteractionMode != ActiveInteractionMode.Idle && activeInteractionMode != ActiveInteractionMode.CableDrag)
                                || (activeInteractionMode == ActiveInteractionMode.Idle && isTwoHandConfirming);
        bool baseBlockedCable = blockedByArbiter || grabbedGateSlotIndex >= 0 || IsAnyCircleSummonActive;

        int chosenSlot = -1;
        int secondaryHoverSlot = -1;

        if (activeInteractionMode == ActiveInteractionMode.CableDrag && (activeInteractionHandSlot == 0 || activeInteractionHandSlot == 1))
        {
            chosenSlot = activeInteractionHandSlot;
        }
        else if (!baseBlockedCable)
        {
            bool slot0Avail = isHandDetected && !handGestureDetector.IsFistActive;
            bool slot1Avail = isSecondaryHandDetected && !secondaryHandGestureDetector.IsFistActive;

            bool canStartBase = activeInteractionMode == ActiveInteractionMode.Idle
                                && neutralCooldownTimer <= 0f
                                && !isTwoHandConfirming;
            bool slot0PinchBegin = handGestureDetector.PinchJustStarted
                                   || (handGestureDetector.IsPinchActive && pinchBeginGraceTimers[0] > 0f);
            bool slot1PinchBegin = secondaryHandGestureDetector.PinchJustStarted
                                   || (secondaryHandGestureDetector.IsPinchActive && pinchBeginGraceTimers[1] > 0f);
            bool slot0CanBegin = slot0Avail
                                 && canStartBase
                                 && slot0PinchBegin
                                 && handGestureDetector.IsQualityGatePassing
                                 && handGestureDetector.PinchSpeedUnitsPerSec <= CablePinchEnterMaxSpeed;
            bool slot1CanBegin = slot1Avail
                                 && canStartBase
                                 && slot1PinchBegin
                                 && secondaryHandGestureDetector.IsQualityGatePassing
                                 && secondaryHandGestureDetector.PinchSpeedUnitsPerSec <= CablePinchEnterMaxSpeed;

            float d0 = slot0Avail
                ? (slot0CanBegin
                    ? Mathf.Min(
                        cableRouter.QueryNearestPortNormalizedDistance(handGestureDetector.StabilizedPinchLocalXY, stageZoom),
                        cableRouter.QueryNearestPortNormalizedDistance(handGestureDetector.PinchPointLocalXY, stageZoom))
                    : cableRouter.QueryNearestPortNormalizedDistance(handGestureDetector.PinchPointLocalXY, stageZoom))
                : float.MaxValue;
            float d1 = slot1Avail
                ? (slot1CanBegin
                    ? Mathf.Min(
                        cableRouter.QueryNearestPortNormalizedDistance(secondaryHandGestureDetector.StabilizedPinchLocalXY, stageZoom),
                        cableRouter.QueryNearestPortNormalizedDistance(secondaryHandGestureDetector.PinchPointLocalXY, stageZoom))
                    : cableRouter.QueryNearestPortNormalizedDistance(secondaryHandGestureDetector.PinchPointLocalXY, stageZoom))
                : float.MaxValue;

            if (slot0CanBegin && !slot1CanBegin && d0 <= 1.25f)
            {
                chosenSlot = 0;
            }
            else if (slot1CanBegin && !slot0CanBegin && d1 <= 1.25f)
            {
                chosenSlot = 1;
            }
            else if (slot0CanBegin && slot1CanBegin)
            {
                chosenSlot = d1 < d0 ? 1 : 0;
            }
            else if (d1 < d0)
            {
                chosenSlot = 1;
            }
            else if (d0 < float.MaxValue)
            {
                chosenSlot = 0;
            }
            else if (slot0Avail)
            {
                chosenSlot = 0;
            }
            else if (slot1Avail)
            {
                chosenSlot = 1;
            }

            if (chosenSlot == 0 && slot1Avail)
            {
                secondaryHoverSlot = 1;
            }
            else if (chosenSlot == 1 && slot0Avail)
            {
                secondaryHoverSlot = 0;
            }
        }

        if (chosenSlot != activeCableHoverHandSlot)
        {
            if (activeInteractionMode != ActiveInteractionMode.CableDrag)
            {
                cableRouter.ResetAimAssistOffset();
            }
            activeCableHoverHandSlot = chosenSlot;
        }

        bool hasChosenHand = chosenSlot >= 0 && IsHandSlotDetected(chosenSlot);
        bool chosenSlotMissing = (chosenSlot == 0 && primaryMissedLastInference)
                                 || (chosenSlot == 1 && secondaryMissedLastInference);
        bool isDragOwned = activeInteractionMode == ActiveInteractionMode.CableDrag
                           && (chosenSlot == 0 || chosenSlot == 1);
        AirWeavingHandGestureDetector cableDet = chosenSlot >= 0 ? GetHandDetector(chosenSlot) : handGestureDetector;
        bool blockedCable = baseBlockedCable || (chosenSlot >= 0 && cableDet.IsFistActive);
        bool allowBeginCableDrag = activeInteractionMode == ActiveInteractionMode.Idle
                                   && neutralCooldownTimer <= 0f
                                   && !isTwoHandConfirming
                                   && hasChosenHand
                                   && !chosenSlotMissing
                                   && cableDet.IsQualityGatePassing
                                   && cableDet.PinchSpeedUnitsPerSec <= CablePinchEnterMaxSpeed;
        bool hasSecondaryHoverHand = secondaryHoverSlot >= 0 && IsHandSlotDetected(secondaryHoverSlot) && !blockedCable;
        Vector2 secondaryHoverPinchXY = hasSecondaryHoverHand
            ? GetHandDetector(secondaryHoverSlot).PinchPointLocalXY
            : Vector2.zero;

        bool cablePinchActive = isDragOwned ? cableDet.IsPinchActive : (hasChosenHand && cableDet.IsPinchActive);
        bool chosenPinchGraceActive = chosenSlot >= 0 && chosenSlot < 2 && pinchBeginGraceTimers[chosenSlot] > 0f;
        bool cablePinchJustStarted = hasChosenHand
                                     && !chosenSlotMissing
                                     && (cableDet.PinchJustStarted || (cableDet.IsPinchActive && chosenPinchGraceActive));
        bool cablePinchJustEnded = isDragOwned ? cableDet.PinchJustEnded : (hasChosenHand && cableDet.PinchJustEnded);
        bool isHandLiveTracking = hasChosenHand && !chosenSlotMissing;

        cableRouter.Tick(
            mainCam,
            circuitManager,
            inputANode,
            inputBNode,
            outputNode,
            activePlacedGatesBuffer,
            WorkbenchDepth,
            isAirWeavingActive,
            IsAnyUiOverlayOpen,
            levelManager != null && levelManager.IsVerifying,
            blockedCable,
            hasChosenHand,
            cablePinchActive,
            cablePinchJustStarted,
            cablePinchJustEnded,
            hasChosenHand ? cableDet.StabilizedPinchLocalXY : Vector2.zero,
            hasChosenHand ? cableDet.PinchPointLocalXY : Vector2.zero,
            hasChosenHand ? cableDet.PinchPointWorldPosition : Vector3.zero,
            Time.unscaledDeltaTime,
            hasChosenHand ? cableDet.PinchSpeedUnitsPerSec : 0f,
            hasChosenHand ? cableDet.PinchRatio : 1f,
            hasChosenHand && cableDet.IsPinchReleaseIntent,
            stageRoot,
            stageZoom,
            allowBeginCableDrag,
            hasSecondaryHoverHand,
            secondaryHoverPinchXY,
            isHandLiveTracking,
            cableDet.PinchEndedByTrackingLoss,
            deferVisualRender: true);

        if (activeInteractionMode == ActiveInteractionMode.Idle && cableRouter.IsDraggingCable)
        {
            activeInteractionMode = ActiveInteractionMode.CableDrag;
            activeInteractionHandSlot = chosenSlot >= 0 ? chosenSlot : 0;
            activeCableHoverHandSlot = activeInteractionHandSlot;
            singleHandModeEnteredTime = Time.unscaledTime;
            pinchBeginGraceTimers[0] = 0f;
            pinchBeginGraceTimers[1] = 0f;
        }
        else if (activeInteractionMode == ActiveInteractionMode.CableDrag && !cableRouter.IsDraggingCable)
        {
            bool overlayOrVerify = IsAnyUiOverlayOpen || (levelManager != null && levelManager.IsVerifying);
            ExitInteractionMode(ActiveInteractionMode.CableDrag, triggerNeutralCooldown: !overlayOrVerify);
        }
    }

    private void SyncCableRouterVisualsAfterPoseUpdate(Camera mainCam)
    {
        if (cableRouter == null || mainCam == null) return;
        bool overlayOrVerify = IsAnyUiOverlayOpen || (levelManager != null && levelManager.IsVerifying);
        bool blockedByArbiter = (activeInteractionMode != ActiveInteractionMode.Idle && activeInteractionMode != ActiveInteractionMode.CableDrag)
                                || IsAnyTwoHandModeActive;
        int activeCableSlot = activeInteractionMode == ActiveInteractionMode.CableDrag
            ? activeInteractionHandSlot
            : activeCableHoverHandSlot;
        bool hasCableSlot = activeCableSlot >= 0 && IsHandSlotDetected(activeCableSlot);
        AirWeavingHandGestureDetector cableDet = activeCableSlot >= 0 ? GetHandDetector(activeCableSlot) : handGestureDetector;
        bool blockedCable = overlayOrVerify || blockedByArbiter || grabbedGateSlotIndex >= 0 || IsAnyCircleSummonActive || (hasCableSlot && cableDet.IsFistActive);
        Vector3 effectivePinchWorld = cableRouter.AssistedPinchWorldPosition != Vector3.zero
            ? cableRouter.AssistedPinchWorldPosition
            : (hasCableSlot ? cableDet.PinchPointWorldPosition : Vector3.zero);
        Transform stageRoot = stageController != null ? stageController.StageRoot : null;
        float stageZoom = stageController != null ? stageController.CurrentZoomScale : 1f;
        cableRouter.SyncVisualsAfterPoseUpdate(
            mainCam,
            circuitManager,
            inputANode,
            inputBNode,
            outputNode,
            activePlacedGatesBuffer,
            WorkbenchDepth,
            isAirWeavingActive,
            (hasCableSlot || cableRouter.IsSecondaryHoveringPort) && !blockedCable,
            effectivePinchWorld,
            stageRoot,
            stageZoom);
    }

    private void UpdateHologramVisuals(Camera mainCam, float dt)
    {
        if (hologramVisual == null || mainCam == null) return;
        Transform camT = mainCam.transform;
        bool overlayOpen = IsAnyUiOverlayOpen;
        bool isVerifying = levelManager != null && levelManager.IsVerifying;
        bool canShowHover = IsAnyHandDetected && !overlayOpen && !isVerifying && !IsAnyTwoHandModeActive;
        hologramVisual.SetRootVisible(isAirWeavingActive);

        // Perbarui visual skeleton 21 titik (One-Euro 3D pada laju render dt + fade alpha halus saat occlusion grace)
        float primaryGraceNorm = primaryMissedLastInference
            ? Mathf.Clamp01(primaryMissingTimer / GetActiveFrameDropGraceSecondsForSlot(0))
            : 0f;
        float secondaryGraceNorm = secondaryMissedLastInference
            ? Mathf.Clamp01(secondaryMissingTimer / GetActiveFrameDropGraceSecondsForSlot(1))
            : 0f;
        bool hideSkeletonsForUi = overlayOpen || isVerifying;
        hologramVisual.UpdateHandSkeletonVisual(
            0,
            isHandDetected,
            primaryMissedLastInference,
            primaryGraceNorm,
            hideSkeletonsForUi,
            dt,
            handRigRootObj,
            jointSphereObjs,
            boneLineRenderers,
            HandBoneConnections,
            boneMat,
            jointBaseMat,
            jointTipMat,
            NeonCyanBoneColor,
            NeonJointBaseColor,
            NeonJointTipColor);
        hologramVisual.UpdateHandSkeletonVisual(
            1,
            isSecondaryHandDetected,
            secondaryMissedLastInference,
            secondaryGraceNorm,
            hideSkeletonsForUi,
            dt,
            secondaryHandRigRootObj,
            secondaryJointSphereObjs,
            secondaryBoneLineRenderers,
            HandBoneConnections,
            secondaryBoneMat,
            secondaryJointBaseMat,
            secondaryJointTipMat,
            NeonCyanBoneColor,
            NeonJointBaseColor,
            NeonJointTipColor);

        Transform stageRoot = stageController != null ? stageController.StageRoot : null;
        GateNode portFocusedNode = (canShowHover && cableRouter != null) ? cableRouter.HoveredPortNode : null;
        GateNode secPortFocusedNode = (canShowHover && cableRouter != null) ? cableRouter.SecondaryHoveredPortNode : null;
        bool inputAHovered = canShowHover && (hoveredInputNode == inputANode || secondaryHoveredInputNode == inputANode);
        bool inputBHovered = canShowHover && (hoveredInputNode == inputBNode || secondaryHoveredInputNode == inputBNode);
        float inputAPushProg = 0f;
        float inputBPushProg = 0f;
        if (inputAHovered)
        {
            if (inputPushHoverTargetBySlot[0] == inputANode) inputAPushProg = Mathf.Max(inputAPushProg, inputPushProgressBySlot[0]);
            if (inputPushHoverTargetBySlot[1] == inputANode) inputAPushProg = Mathf.Max(inputAPushProg, inputPushProgressBySlot[1]);
        }
        if (inputBHovered)
        {
            if (inputPushHoverTargetBySlot[0] == inputBNode) inputBPushProg = Mathf.Max(inputBPushProg, inputPushProgressBySlot[0]);
            if (inputPushHoverTargetBySlot[1] == inputBNode) inputBPushProg = Mathf.Max(inputBPushProg, inputPushProgressBySlot[1]);
        }
        AirWeavingHologramVisual.NodeHoloState inputAState = inputAHovered
            ? AirWeavingHologramVisual.NodeHoloState.BodyHovered
            : AirWeavingHologramVisual.NodeHoloState.Idle;
        AirWeavingHologramVisual.NodeHoloState inputBState = inputBHovered
            ? AirWeavingHologramVisual.NodeHoloState.BodyHovered
            : AirWeavingHologramVisual.NodeHoloState.Idle;
        hologramVisual.UpdateNodeFrame(0, camT, inputANode, true, false, false, inputAState, stageRoot, inputAPushProg);
        hologramVisual.UpdateNodeFrame(1, camT, inputBNode, false, true, false, inputBState, stageRoot, inputBPushProg);
        hologramVisual.UpdateNodeFrame(2, camT, outputNode, false, false, true, AirWeavingHologramVisual.NodeHoloState.Idle, stageRoot);
        if (lightingAndShaderRig != null)
        {
            float outputHoldProg = outputTrigger != null ? outputTrigger.ChargeProgress : 0f;
            lightingAndShaderRig.UpdateNodeFresnelState(inputANode, true, false, false, inputAState);
            lightingAndShaderRig.UpdateNodeFresnelState(inputBNode, false, true, false, inputBState);
            lightingAndShaderRig.UpdateNodeFresnelState(outputNode, false, false, true, AirWeavingHologramVisual.NodeHoloState.Idle, outputHoldProg);
        }

        // Efek ripple 3D singkat sejajar bidang meja XZ saat ujung telunjuk menyentuh sakelar Input A / B
        float rippleA = inputATouchRippleTimer > 0f ? (1f - (inputATouchRippleTimer / InputTouchRippleDuration)) : 0f;
        float rippleB = inputBTouchRippleTimer > 0f ? (1f - (inputBTouchRippleTimer / InputTouchRippleDuration)) : 0f;
        hologramVisual.UpdateInputTouchRipple(0, camT, inputANode, isInputB: false, rippleA, stageRoot);
        hologramVisual.UpdateInputTouchRipple(1, camT, inputBNode, isInputB: true, rippleB, stageRoot);

        float aspectScale = ComputeHorizontalAspectScale(mainCam);
        bool isGrabbingGate = grabbedGateSlotIndex >= 0 && grabbedGateSlotIndex < gateSlots.Count;

        for (int i = 0; i < gateSlots.Count && i < 5; i++)
        {
            WorkbenchGateSlot slot = gateSlots[i];
            if (slot == null || slot.node == null) continue;

            AirWeavingHologramVisual.NodeHoloState state;
            if (grabbedGateSlotIndex == i)
            {
                Vector2 homeXY = GetParkingSlotLocalXY(slot.slotIndex, aspectScale);
                bool outsideAssembly = IsOutsideAssemblyZone(slot.currentLocalXY, aspectScale);
                bool returning = outsideAssembly
                                 || slot.currentLocalXY.y > ParkReturnThresholdY
                                 || Vector2.Distance(slot.currentLocalXY, homeXY) <= ParkSlotSnapRadius;
                state = returning ? AirWeavingHologramVisual.NodeHoloState.ReturningToRack : AirWeavingHologramVisual.NodeHoloState.Grabbed;
            }
            else if (canShowHover && (portFocusedNode == slot.node || secPortFocusedNode == slot.node))
            {
                state = AirWeavingHologramVisual.NodeHoloState.PortFocused;
            }
            else if (canShowHover && (hoveredGateSlotIndex == i || secondaryHoveredGateSlotIndex == i))
            {
                state = AirWeavingHologramVisual.NodeHoloState.BodyHovered;
            }
            else
            {
                state = slot.isPlacedOnWorkbench ? AirWeavingHologramVisual.NodeHoloState.Idle : AirWeavingHologramVisual.NodeHoloState.Parked;
            }

            hologramVisual.UpdateNodeFrame(3 + i, camT, slot.node, false, false, false, state, stageRoot);
            lightingAndShaderRig?.UpdateNodeFresnelState(slot.node, false, false, false, state);
        }

        bool draggingCable = cableRouter != null && cableRouter.IsDraggingCable;
        bool hoveringPrimaryPort = cableRouter != null && cableRouter.IsHoveringPort;
        bool hoveringSecondaryPort = cableRouter != null && cableRouter.IsSecondaryHoveringPort;
        bool twoHandVisualActive = !overlayOpen && !isVerifying && (IsTwoHandOrbitZoomActive || IsTwoHandFreePanActive || IsTwoHandFistResetCharging);
        bool anySummonActive = IsAnyCircleSummonActive;

        int primaryCableSlot = draggingCable ? activeInteractionHandSlot : activeCableHoverHandSlot;
        int otherCableSlot = primaryCableSlot == 0 ? 1 : (primaryCableSlot == 1 ? 0 : -1);

        bool slot0PortHovering = (primaryCableSlot == 0 && (hoveringPrimaryPort || draggingCable))
                                 || (otherCableSlot == 0 && hoveringSecondaryPort);
        bool slot1PortHovering = (primaryCableSlot == 1 && (hoveringPrimaryPort || draggingCable))
                                 || (otherCableSlot == 1 && hoveringSecondaryPort);

        bool isSlot0CarryPan = isCarryPanActive && carryPanHandSlot == 0 && !overlayOpen && !isVerifying && !twoHandVisualActive;
        bool isSlot1CarryPan = isCarryPanActive && carryPanHandSlot == 1 && !overlayOpen && !isVerifying && !twoHandVisualActive;

        bool lockedOtherThanSlot0 = activeInteractionMode != ActiveInteractionMode.Idle && activeInteractionHandSlot != 0 && !isSlot0CarryPan;
        bool lockedOtherThanSlot1 = activeInteractionMode != ActiveInteractionMode.Idle && activeInteractionHandSlot != 1 && !isSlot1CarryPan;

        // Saat Idle, tampilkan reticle aktif untuk KEDUA tangan (slot 0 DAN slot 1) agar pengguna tahu kiri & kanan sama-sama siap pakai;
        // saat Carry-Pan aktif, tampilkan reticle emas pada Tangan 2 tanpa menyembunyikan indikator kabel/gerbang Tangan 1.
        bool hideSlot0Reticle = !isSlot0CarryPan
                                && (overlayOpen
                                    || isVerifying
                                    || twoHandVisualActive
                                    || lockedOtherThanSlot0
                                    || (isGrabbingGate && activeInteractionHandSlot == 0)
                                    || slot0PortHovering
                                    || hoveredGateSlotIndex >= 0
                                    || hoveredInputNode != null
                                    || circleSummoners[0].IsActive);

        bool hideSlot1Reticle = !isSlot1CarryPan
                                && (overlayOpen
                                    || isVerifying
                                    || twoHandVisualActive
                                    || lockedOtherThanSlot1
                                    || (isGrabbingGate && activeInteractionHandSlot == 1)
                                    || slot1PortHovering
                                    || secondaryHoveredGateSlotIndex >= 0
                                    || secondaryHoveredInputNode != null
                                    || circleSummoners[1].IsActive);

        Vector3 slot0ReticleWorldPos = (isSlot0CarryPan && !carryPanUsesFist)
            ? handGestureDetector.PinchPointWorldPosition
            : handGestureDetector.PalmCursorWorldPosition;
        Vector3 slot1ReticleWorldPos = (isSlot1CarryPan && !carryPanUsesFist)
            ? secondaryHandGestureDetector.PinchPointWorldPosition
            : secondaryHandGestureDetector.PalmCursorWorldPosition;

        hologramVisual.UpdatePalmReticle(
            camT,
            isHandDetected,
            hideSlot0Reticle,
            slot0ReticleWorldPos,
            handGestureDetector.IsFistActive,
            slot0PortHovering,
            handGestureDetector.MeanCurlRatio,
            primaryMissedLastInference,
            primaryGraceNorm,
            dt,
            isCarryPanMode: isSlot0CarryPan);

        hologramVisual.UpdateSecondaryPalmReticle(
            camT,
            isSecondaryHandDetected,
            hideSlot1Reticle,
            slot1ReticleWorldPos,
            secondaryHandGestureDetector.IsFistActive,
            slot1PortHovering,
            secondaryHandGestureDetector.MeanCurlRatio,
            secondaryMissedLastInference,
            secondaryGraceNorm,
            dt,
            isCarryPanMode: isSlot1CarryPan);

        // Indikator cincin cubit presisi untuk tangan kabel aktif (baik slot 0 maupun slot 1) & tangan kedua bila ikut hover port saat Idle
        bool hasPrimaryCableHand = primaryCableSlot >= 0 && IsHandSlotDetected(primaryCableSlot);
        AirWeavingHandGestureDetector primaryCableDet = primaryCableSlot >= 0 ? GetHandDetector(primaryCableSlot) : handGestureDetector;
        bool primaryCableInGrace = primaryCableSlot == 1 ? secondaryMissedLastInference : primaryMissedLastInference;
        float primaryCableGraceNorm = primaryCableSlot == 1 ? secondaryGraceNorm : primaryGraceNorm;
        bool showPrimaryPinchRing = !overlayOpen
                                    && !isVerifying
                                    && !twoHandVisualActive
                                    && !isGrabbingGate
                                    && hasPrimaryCableHand
                                    && !primaryCableDet.IsFistActive
                                    && !anySummonActive
                                    && (hoveringPrimaryPort || draggingCable);
        Vector3 primaryPinchVisualWorldPos = (cableRouter != null && cableRouter.AssistedPinchWorldPosition != Vector3.zero)
            ? cableRouter.AssistedPinchWorldPosition
            : (hasPrimaryCableHand ? primaryCableDet.PinchPointWorldPosition : Vector3.zero);
        hologramVisual.UpdatePinchIndicator(
            camT,
            hasPrimaryCableHand,
            showPrimaryPinchRing,
            primaryPinchVisualWorldPos,
            hasPrimaryCableHand && primaryCableDet.IsPinchActive,
            cableRouter != null && cableRouter.IsSnappedTargetPort,
            primaryCableInGrace,
            primaryCableGraceNorm,
            dt);

        bool hasOtherCableHand = otherCableSlot >= 0 && IsHandSlotDetected(otherCableSlot);
        AirWeavingHandGestureDetector otherCableDet = otherCableSlot >= 0 ? GetHandDetector(otherCableSlot) : secondaryHandGestureDetector;
        bool otherCableInGrace = otherCableSlot == 0 ? primaryMissedLastInference : secondaryMissedLastInference;
        float otherCableGraceNorm = otherCableSlot == 0 ? primaryGraceNorm : secondaryGraceNorm;
        bool showSecondaryPinchRing = !overlayOpen
                                      && !isVerifying
                                      && !twoHandVisualActive
                                      && !isGrabbingGate
                                      && !draggingCable
                                      && !anySummonActive
                                      && hasOtherCableHand
                                      && !otherCableDet.IsFistActive
                                      && hoveringSecondaryPort;
        hologramVisual.UpdateSecondaryPinchIndicator(
            camT,
            hasOtherCableHand,
            showSecondaryPinchRing,
            hasOtherCableHand ? otherCableDet.PinchPointWorldPosition : Vector3.zero,
            hasOtherCableHand && otherCableDet.IsPinchActive,
            isSnappedToPort: false,
            otherCableInGrace,
            otherCableGraceNorm,
            dt);

        // Garis proyeksi vertikal tipis (drop-line) + cincin bayangan di grid untuk persepsi kedalaman Z monokular 3D;
        // otomatis tersembunyi saat mode 2 tangan (Orbit/Zoom/FreePan/Reset), Carry-Pan, overlay UI, atau sedang menggenggam gerbang.
        bool allowCursorDropLines = !overlayOpen && !isVerifying && !twoHandVisualActive && !isCarryPanActive;
        bool showSlot0Drop = allowCursorDropLines
                             && isHandDetected
                             && !lockedOtherThanSlot0
                             && !(isGrabbingGate && activeInteractionHandSlot == 0)
                             && !circleSummoners[0].IsActive;
        Vector3 slot0DropWorldPos = slot0PortHovering
            ? ((primaryCableSlot == 0 && cableRouter != null && cableRouter.AssistedPinchWorldPosition != Vector3.zero)
                ? cableRouter.AssistedPinchWorldPosition
                : handGestureDetector.PinchPointWorldPosition)
            : (handGestureDetector.IsIndexSwitchPoseValid
                ? handGestureDetector.IndexTipWorldPosition
                : handGestureDetector.PalmCursorWorldPosition);
        bool slot0ContactActive = handGestureDetector.IsFistActive
                                  || handGestureDetector.IsPinchActive
                                  || inputPushProgressBySlot[0] >= 0.70f;
        hologramVisual.UpdateCursorDepthProjection(0, stageRoot, showSlot0Drop, slot0DropWorldPos, slot0ContactActive, camT);

        bool showSlot1Drop = allowCursorDropLines
                             && isSecondaryHandDetected
                             && !lockedOtherThanSlot1
                             && !(isGrabbingGate && activeInteractionHandSlot == 1)
                             && !circleSummoners[1].IsActive;
        Vector3 slot1DropWorldPos = slot1PortHovering
            ? ((primaryCableSlot == 1 && cableRouter != null && cableRouter.AssistedPinchWorldPosition != Vector3.zero)
                ? cableRouter.AssistedPinchWorldPosition
                : secondaryHandGestureDetector.PinchPointWorldPosition)
            : (secondaryHandGestureDetector.IsIndexSwitchPoseValid
                ? secondaryHandGestureDetector.IndexTipWorldPosition
                : secondaryHandGestureDetector.PalmCursorWorldPosition);
        bool slot1ContactActive = secondaryHandGestureDetector.IsFistActive
                                  || secondaryHandGestureDetector.IsPinchActive
                                  || inputPushProgressBySlot[1] >= 0.70f;
        hologramVisual.UpdateCursorDepthProjection(1, stageRoot, showSlot1Drop, slot1DropWorldPos, slot1ContactActive, camT);

        // Jangkar visual link 2 tangan selalu terkunci langsung pada posisi 3D tangan pengguna di seluruh bidang layar (global)
        Vector3 handAWorld = (IsTwoHandFistResetCharging || (IsTwoHandFreePanActive && !twoHandFreePanPrimaryIsPinch))
            ? handGestureDetector.PalmCursorWorldPosition
            : handGestureDetector.PinchPointWorldPosition;
        Vector3 handBWorld = (IsTwoHandFistResetCharging || (IsTwoHandFreePanActive && twoHandFreePanPrimaryIsPinch))
            ? secondaryHandGestureDetector.PalmCursorWorldPosition
            : secondaryHandGestureDetector.PinchPointWorldPosition;

        float resetProgress = IsTwoHandFistResetCharging
            ? Mathf.Clamp01(twoHandFistResetTimer / TwoHandFistResetHoldSeconds)
            : 0f;
        hologramVisual.UpdateTwoHandManipulatorLink(
            camT,
            twoHandVisualActive,
            handAWorld,
            handBWorld,
            IsTwoHandFistResetCharging,
            resetProgress,
            IsTwoHandFreePanActive);
    }

    private void UpdateCircleSummoner(Camera mainCam, float dt)
    {
        if (mainCam == null) return;
        float aspectScale = ComputeHorizontalAspectScale(mainCam);
        bool overlayOrVerify = IsAnyUiOverlayOpen || (levelManager != null && levelManager.IsVerifying);
        bool draggingCable = cableRouter != null && cableRouter.IsDraggingCable;
        bool blockedByOtherMode = activeInteractionMode != ActiveInteractionMode.Idle
                                  && activeInteractionMode != ActiveInteractionMode.CircleSummon;

        Vector2 assemblyMin = new Vector2(AssemblyMinBaseX * aspectScale, AssemblyMinY);
        Vector2 assemblyMax = new Vector2(AssemblyMaxBaseX * aspectScale, AssemblyMaxY);

        int lockedSlot = (activeInteractionMode == ActiveInteractionMode.CircleSummon
                          && (activeInteractionHandSlot == 0 || activeInteractionHandSlot == 1))
            ? activeInteractionHandSlot
            : -1;

        for (int handSlot = 0; handSlot < 2; handSlot++)
        {
            AirWeavingCircleSummoner summoner = circleSummoners[handSlot];
            AirWeavingHandGestureDetector det = GetHandDetector(handSlot);
            bool slotDetected = IsHandSlotDetected(handSlot);
            bool slotBlockedByLock = lockedSlot >= 0 && lockedSlot != handSlot;

            bool blocked;
            if (slotBlockedByLock || blockedByOtherMode || overlayOrVerify || grabbedGateSlotIndex >= 0 || draggingCable)
            {
                blocked = true;
            }
            else if (summoner.IsActive)
            {
                // Begitu sudah masuk Charging/Ready (terkunci di Arbiter), tangan kedua maupun hover tepi DILARANG membatalkan summon
                blocked = det.IsPinchActive;
            }
            else
            {
                bool slotHoveringPort = (cableRouter != null)
                                        && ((activeCableHoverHandSlot == handSlot && cableRouter.IsHoveringPort)
                                            || (activeCableHoverHandSlot != handSlot && cableRouter.IsSecondaryHoveringPort));
                // Guard pra-cubit hanya memblokir summon bila benar-benar dalam posisi cubit/di atas port kabel;
                // pose tegas OneFingerAnd (telunjuk lurus) & ThumbOnlyNot (jempol terbentang) tidak diblokir pra-cubit.
                bool pinchOrFistBusy = det.IsPinchActive
                                       || det.IsPrePinchSuppressingSummon
                                       || (slotHoveringPort && det.IsFormingPinchIntent)
                                       || det.IsFormingFistIntent;
                int slotHoveredGate = handSlot == 0 ? hoveredGateSlotIndex : secondaryHoveredGateSlotIndex;
                GateNode slotHoveredInput = handSlot == 0 ? hoveredInputNode : secondaryHoveredInputNode;
                bool inOutputZone = IsCursorInsideOutputPriorityZone(mainCam, det.PalmCursorWorldPosition, aspectScale, det);
                bool tipNearInputSwitch = IsTipInsideInputSwitchZone(mainCam, GetHandIndexTipWorldPosition(handSlot), aspectScale);
                bool nearAnyTerminal = IsHandNearAnyTerminalForSummonBlock(
                    mainCam,
                    det.PalmCursorWorldPosition,
                    GetHandIndexTipWorldPosition(handSlot),
                    aspectScale);
                bool blockedByCooldownOrQuality = neutralCooldownTimer > 0f
                                                  || postDropLockoutTimer > 0f
                                                  || !det.IsQualityGatePassing
                                                  || isTwoHandConfirming;
                blocked = blockedByCooldownOrQuality
                          || pinchOrFistBusy
                          || slotHoveringPort
                          || slotHoveredGate >= 0
                          || slotHoveredInput != null
                          || tipNearInputSwitch
                          || nearAnyTerminal
                          || inOutputZone;
            }

            summoner.Tick(
                dt,
                isAirWeavingActive,
                blocked,
                slotDetected,
                det.IsFistActive,
                det.FistJustStarted,
                det.CurrentSummonPose,
                det.IsRelaxedOpenFiveBloom,
                det.ActiveSummonTipLocalXY,
                det.ThumbTipLocalXY,
                assemblyMin,
                assemblyMax);

            if (activeInteractionMode == ActiveInteractionMode.Idle && summoner.IsActive)
            {
                activeInteractionMode = ActiveInteractionMode.CircleSummon;
                activeInteractionHandSlot = handSlot;
                singleHandModeEnteredTime = Time.unscaledTime;
                lockedSlot = handSlot;
                circleSummoners[1 - handSlot].Reset();
            }

            if (summoner.BloomJustTriggeredThisFrame)
            {
                HandleCircleSummonBloomDeploy(mainCam, aspectScale, summoner.DeployedGateType, summoner.DeployedLocalXY);
            }
        }

        if (activeInteractionMode == ActiveInteractionMode.CircleSummon)
        {
            int ownerSlot = (activeInteractionHandSlot == 0 || activeInteractionHandSlot == 1) ? activeInteractionHandSlot : 0;
            if (!circleSummoners[ownerSlot].IsActive)
            {
                ExitInteractionMode(ActiveInteractionMode.CircleSummon, triggerNeutralCooldown: !overlayOrVerify);
            }
        }

        UpdateCircleSummonAudioFeedback(mainCam, overlayOrVerify);
    }

    private void UpdateCircleSummonAudioFeedback(Camera mainCam, bool overlayOrVerify)
    {
        AirWeavingCircleSummoner activeSummoner = GetActiveOrPreviewCircleSummoner();
        AirWeavingCircleSummoner.SummonStage curStage = activeSummoner.Stage;
        if (curStage != AirWeavingCircleSummoner.SummonStage.Idle && mainCam != null)
        {
            Vector2 centerXY = activeSummoner.PreviewCenterLocalXY;
            lastSfxSummonWorldPos = mainCam.transform.TransformPoint(new Vector3(centerXY.x, centerXY.y, WorkbenchDepth));
        }

        if (spatialSfx != null && !overlayOrVerify)
        {
            NodeType curGateType = activeSummoner.ActiveGateType;
            bool enteredChargingFromIdle = curStage == AirWeavingCircleSummoner.SummonStage.Charging
                                           && lastSfxSummonStage == AirWeavingCircleSummoner.SummonStage.Idle;

            if (enteredChargingFromIdle)
            {
                spatialSfx.PlaySummonIgnite(lastSfxSummonWorldPos);
                lastSfxSummonGateType = curGateType;
            }
            else if ((curStage == AirWeavingCircleSummoner.SummonStage.Charging
                      || curStage == AirWeavingCircleSummoner.SummonStage.Ready)
                     && lastSfxSummonStage != AirWeavingCircleSummoner.SummonStage.Idle
                     && curGateType != lastSfxSummonGateType)
            {
                spatialSfx.PlaySummonPoseShift(lastSfxSummonWorldPos);
                lastSfxSummonGateType = curGateType;
            }

            if (curStage == AirWeavingCircleSummoner.SummonStage.Ready
                && lastSfxSummonStage != AirWeavingCircleSummoner.SummonStage.Ready)
            {
                spatialSfx.PlaySummonChargeReady(lastSfxSummonWorldPos);
                lastSfxSummonGateType = curGateType;
                lastSfxSummonChargeStep = 4;
            }
            else if (curStage == AirWeavingCircleSummoner.SummonStage.Charging)
            {
                float prog = activeSummoner.ChargeProgress;
                int desiredStep = prog >= 0.75f ? 3 : (prog >= 0.50f ? 2 : (prog >= 0.25f ? 1 : 0));
                if (desiredStep > lastSfxSummonChargeStep)
                {
                    if (!enteredChargingFromIdle || desiredStep >= 2)
                    {
                        spatialSfx.PlaySummonChargeStep(desiredStep - 1, lastSfxSummonWorldPos);
                    }
                    lastSfxSummonChargeStep = desiredStep;
                }
            }
            else if (curStage == AirWeavingCircleSummoner.SummonStage.Idle)
            {
                bool cancelledByFist = circleSummoners[0].ConsumedFistCancelThisFrame || circleSummoners[1].ConsumedFistCancelThisFrame;
                bool anyBloomTriggered = circleSummoners[0].BloomJustTriggeredThisFrame || circleSummoners[1].BloomJustTriggeredThisFrame;
                bool expiredFromReady = lastSfxSummonStage == AirWeavingCircleSummoner.SummonStage.Ready
                                        && !anyBloomTriggered;
                if (cancelledByFist || expiredFromReady)
                {
                    spatialSfx.PlaySoftReject(lastSfxSummonWorldPos);
                }
                lastSfxSummonChargeStep = 0;
            }
        }
        else if (curStage == AirWeavingCircleSummoner.SummonStage.Idle)
        {
            lastSfxSummonChargeStep = 0;
        }

        lastSfxSummonStage = curStage;
    }

    private void HandleCircleSummonBloomDeploy(Camera mainCam, float aspectScale, NodeType gateType, Vector2 deployLocalXY)
    {
        WorkbenchGateSlot targetSlot = null;
        for (int i = 0; i < gateSlots.Count; i++)
        {
            if (gateSlots[i] != null && gateSlots[i].node != null && gateSlots[i].node.nodeType == gateType)
            {
                targetSlot = gateSlots[i];
                break;
            }
        }

        if (targetSlot == null || targetSlot.node == null) return;

        // Aturan #3 (Notice + Pulse Non-Destruktif): jika gerbang sudah terpasang di papan, jangan pindahkan/ganggu kabelnya
        if (targetSlot.isPlacedOnWorkbench)
        {
            targetSlot.scalePop.Trigger(GatePlaceBloomPopAmplitude);
            targetSlot.node.TriggerTapFeedback();
            summonVisual?.TriggerBloomBurst(targetSlot.node.transform.position, isAmberNotice: true);
            if (spatialSfx != null)
            {
                spatialSfx.PlaySoftReject(targetSlot.node.transform.position);
            }
            SetFeedbackImmediate($"Gerbang {gateType} sudah terpasang di papan!", FeedbackWaitingColor, 2.2f);
            return;
        }

        Vector2 stageDeployXZ = deployLocalXY;
        if (stageController != null && mainCam != null)
        {
            Vector3 deployRayWorld = mainCam.transform.TransformPoint(new Vector3(deployLocalXY.x, deployLocalXY.y, WorkbenchDepth));
            stageController.TryIntersectHandWithHoverPlane(mainCam, deployRayWorld, BaseHoverHeight, out stageDeployXZ, out _);
        }

        Vector2 spawnXY = ResolveSnappedAutoSeparateGridXZ(stageDeployXZ, targetSlot.slotIndex, aspectScale);
        targetSlot.isPlacedOnWorkbench = true;
        targetSlot.placedLocalXY = spawnXY;
        targetSlot.currentLocalXY = spawnXY;
        targetSlot.currentLocalZ = ComputeArenaBaseHoverHeightY(spawnXY, aspectScale);
        targetSlot.currentScale = ParkedGateScale;
        targetSlot.ResetSpringState();
        targetSlot.scalePop.Trigger(GatePlaceBloomPopAmplitude);

        float gatePhase = 1.15f + (targetSlot.slotIndex * 1.27f);
        float arcYaw = ComputeArenaArcYawDeg(spawnXY.x, aspectScale);
        float stageZoom = stageController != null ? stageController.CurrentZoomScale : 1f;
        ComputeStageNodePose(
            mainCam.transform,
            new Vector3(spawnXY.x, targetSlot.currentLocalZ, spawnXY.y),
            arcYaw,
            gatePhase,
            isGrabbed: false,
            Vector2.zero,
            out Vector3 worldPos,
            out Quaternion worldRot);
        targetSlot.node.transform.localScale = Vector3.one * (targetSlot.currentScale * stageZoom);
        targetSlot.node.SetExternalSpatialPose(true, worldPos, worldRot);
        if (circuitManager != null)
        {
            circuitManager.EvaluateCircuit();
        }
        targetSlot.node.TriggerTapFeedback();
        summonVisual?.TriggerBloomBurst(worldPos, isAmberNotice: false);
        if (spatialSfx != null)
        {
            spatialSfx.PlayBloomSpawn(worldPos);
        }
        postDropLockoutTimer = PostDropOutputLockoutSeconds;
        gestureGuide?.NotifyGateSummoned();
        SetFeedbackImmediate($"Gerbang {gateType} dipanggil ke zona rakit!", FeedbackPinchColor, 1.8f);
    }

    private void UpdateCircleSummonVisuals(Camera mainCam, float dt)
    {
        if (summonVisual == null) return;
        AirWeavingCircleSummoner activeSummoner = GetActiveOrPreviewCircleSummoner();
        bool hasGateContext = activeSummoner.IsActive || activeSummoner.HasLivePoseIndicator;
        NodeType visualGate = activeSummoner.IsActive ? activeSummoner.ActiveGateType : activeSummoner.LiveIndicatorGateType;
        bool alreadyOnBoard = hasGateContext && IsGateTypePlacedOnWorkbench(visualGate);
        summonVisual.TickVisuals(mainCam, dt, isAirWeavingActive, WorkbenchDepth, activeSummoner, alreadyOnBoard);
    }

    private bool IsGateTypePlacedOnWorkbench(NodeType gateType)
    {
        for (int i = 0; i < gateSlots.Count; i++)
        {
            WorkbenchGateSlot slot = gateSlots[i];
            if (slot != null && slot.node != null && slot.node.nodeType == gateType)
            {
                return slot.isPlacedOnWorkbench;
            }
        }
        return false;
    }

    private void UpdateOutputSpatialTrigger(Camera mainCam, float dt)
    {
        if (outputTrigger == null) return;
        float aspectScale = ComputeHorizontalAspectScale(mainCam);
        Vector2 fallbackOutputXY = new Vector2(OutputTerminalBaseX * aspectScale, OutputTerminalY);
        Vector3 outputWorldPos = outputNode != null
            ? outputNode.transform.position
            : mainCam.transform.TransformPoint(new Vector3(fallbackOutputXY.x, fallbackOutputXY.y, WorkbenchDepth));
        Vector2 outputRayXY = ProjectWorldToCameraRayPlane(mainCam, outputWorldPos);
        float stageZoom = stageController != null ? stageController.CurrentZoomScale : 1f;

        int outputHandSlot = -1;
        if (activeInteractionMode == ActiveInteractionMode.OutputHold
            && (activeInteractionHandSlot == 0 || activeInteractionHandSlot == 1))
        {
            outputHandSlot = activeInteractionHandSlot;
        }
        else if (isHandDetected && isSecondaryHandDetected)
        {
            float dist0 = Vector2.Distance(handGestureDetector.PalmCursorLocalXY, outputRayXY);
            float dist1 = Vector2.Distance(secondaryHandGestureDetector.PalmCursorLocalXY, outputRayXY);
            outputHandSlot = dist1 < dist0 ? 1 : 0;
        }
        else if (isHandDetected)
        {
            outputHandSlot = 0;
        }
        else if (isSecondaryHandDetected)
        {
            outputHandSlot = 1;
        }

        bool outputHandDetected = outputHandSlot >= 0 && IsHandSlotDetected(outputHandSlot);
        AirWeavingHandGestureDetector outDet = outputHandSlot >= 0 ? GetHandDetector(outputHandSlot) : handGestureDetector;
        Vector2 palmRayXY = outputHandDetected ? outDet.PalmCursorLocalXY : Vector2.zero;

        OutputDebugGuidanceState guidanceState = EvaluateOutputDebugGuidance(outputWorldPos, out int passedRowCount);

        bool effectiveReadyForNext = isLiveCircuitPassedAllFour
                                     && levelManager != null
                                     && levelManager.IsReadyForNextLevel;

        bool outputBlockedByArbiter = (activeInteractionMode != ActiveInteractionMode.Idle && activeInteractionMode != ActiveInteractionMode.OutputHold)
                                      || (activeInteractionMode == ActiveInteractionMode.Idle
                                          && (neutralCooldownTimer > 0f || !outputHandDetected || !outDet.IsQualityGatePassing || isTwoHandConfirming));

        int slotHoveredGate = outputHandSlot == 1 ? secondaryHoveredGateSlotIndex : hoveredGateSlotIndex;
        bool anyConsumedFistCancel = circleSummoners[0].ConsumedFistCancelThisFrame || circleSummoners[1].ConsumedFistCancelThisFrame;

        Transform stageRoot = stageController != null ? stageController.StageRoot : null;
        outputTrigger.Tick(mainCam, dt, isAirWeavingActive,
            levelManager != null && levelManager.IsSandboxMode,
            levelManager != null && levelManager.IsVerifying,
            levelManager != null && levelManager.IsCircuitCompleteLive,
            effectiveReadyForNext,
            IsAnyUiOverlayOpen,
            outputHandDetected, outputHandDetected && outDet.IsFistActive, palmRayXY,
            outputRayXY, outputWorldPos,
            grabbedGateSlotIndex >= 0, slotHoveredGate >= 0,
            outputHandDetected && outDet.IsPinchActive,
            cableRouter != null && cableRouter.IsDraggingCable,
            isRecordingAirDraw || IsAnyCircleSummonActive || anyConsumedFistCancel || outputBlockedByArbiter,
            postDropLockoutTimer,
            guidanceState,
            passedRowCount,
            stageZoom,
            stageRoot,
            lastTruthPodWorldAnchor);

        if (activeInteractionMode == ActiveInteractionMode.Idle && outputTrigger.IsCharging)
        {
            activeInteractionMode = ActiveInteractionMode.OutputHold;
            activeInteractionHandSlot = outputHandSlot >= 0 ? outputHandSlot : 0;
        }
        else if (activeInteractionMode == ActiveInteractionMode.OutputHold && !outputTrigger.IsCharging)
        {
            bool overlayOrVerify = IsAnyUiOverlayOpen || (levelManager != null && levelManager.IsVerifying);
            ExitInteractionMode(ActiveInteractionMode.OutputHold, triggerNeutralCooldown: !overlayOrVerify);
        }

        // Detak progresif bernada naik (25% / 50% / 75%) saat menahan kepal di Output pasca-lulus untuk lanjut level Zero-Touch
        float holdProg = outputTrigger.ChargeProgress;
        if (holdProg <= 0.01f)
        {
            lastOutputHoldSfxStep = 0;
        }
        else
        {
            int desiredHoldStep = holdProg >= 0.75f ? 3 : (holdProg >= 0.50f ? 2 : (holdProg >= 0.25f ? 1 : 0));
            if (desiredHoldStep > lastOutputHoldSfxStep)
            {
                if (spatialSfx != null && isAirWeavingActive)
                {
                    spatialSfx.PlayOutputHoldStep(desiredHoldStep - 1, outputWorldPos);
                }
                lastOutputHoldSfxStep = desiredHoldStep;
            }
        }
    }

    private void ResetLiveDebugMirror()
    {
        debugPassedRowMask = 0;
        debugFailedRowMask = 0;
        lastDebugLevelIndex = -1;
        lastDebugActiveGates = -1;
        lastDebugWireCount = -1;
        lastDebugTopologyVersion = -1;
        lastDebugCircuitComplete = false;
        lastEvaluatedCircuitTopologyVersion = -1;
        isLiveCircuitPassedAllFour = false;
        hasCachedTopologyTruthEval = false;
        cachedTopologyMatchesTruthTable = false;
        lastSfxPassedRowMask = 0;
        lastSfxFailedRowMask = 0;
        lastSfxGuidanceState = OutputDebugGuidanceState.ConnectToOutput;
    }

    private OutputDebugGuidanceState EvaluateOutputDebugGuidance(Vector3 outputWorldPos, out int passedRowCount)
    {
        passedRowCount = 0;

        bool isSandbox = levelManager != null && levelManager.IsSandboxMode;
        bool isComplete = levelManager != null && levelManager.IsCircuitCompleteLive;
        int curLevelIdx = levelManager != null ? levelManager.currentLevelIndex : -1;
        int activeGates = circuitManager != null ? circuitManager.activeGateCardsCount : 0;
        int wireCount = circuitManager != null ? circuitManager.GetActiveConnectionCount() : 0;
        int topoVersion = cableRouter != null ? cableRouter.TopologyVersion : 0;

        bool topologyOrLevelChanged = !isComplete
                                      || curLevelIdx != lastDebugLevelIndex
                                      || activeGates != lastDebugActiveGates
                                      || wireCount != lastDebugWireCount
                                      || topoVersion != lastDebugTopologyVersion
                                      || !lastDebugCircuitComplete;

        if (topologyOrLevelChanged)
        {
            debugPassedRowMask = 0;
            debugFailedRowMask = 0;
            isLiveCircuitPassedAllFour = false;
            hasCachedTopologyTruthEval = false;
            cachedTopologyMatchesTruthTable = false;
            lastSfxPassedRowMask = 0;
            lastSfxFailedRowMask = 0;
            if (!isComplete || curLevelIdx != lastDebugLevelIndex)
            {
                lastSfxGuidanceState = OutputDebugGuidanceState.ConnectToOutput;
            }
            lastDebugLevelIndex = curLevelIdx;
            lastDebugActiveGates = activeGates;
            lastDebugWireCount = wireCount;
            lastDebugTopologyVersion = topoVersion;
            lastDebugCircuitComplete = isComplete;
        }

        if (isSandbox || !isComplete)
        {
            isLiveCircuitPassedAllFour = false;
            lastSfxGuidanceState = OutputDebugGuidanceState.ConnectToOutput;
            return OutputDebugGuidanceState.ConnectToOutput;
        }

        if (levelManager == null || levelManager.levels == null || levelManager.levels.Count == 0)
        {
            isLiveCircuitPassedAllFour = false;
            lastSfxGuidanceState = OutputDebugGuidanceState.ConnectToOutput;
            return OutputDebugGuidanceState.ConnectToOutput;
        }

        LevelScenario currentLevel = levelManager.levels[Mathf.Clamp(curLevelIdx, 0, levelManager.levels.Count - 1)];
        if (currentLevel == null || currentLevel.expectedTruthTable == null || currentLevel.expectedTruthTable.Length < 4)
        {
            isLiveCircuitPassedAllFour = false;
            lastSfxGuidanceState = OutputDebugGuidanceState.ConnectToOutput;
            return OutputDebugGuidanceState.ConnectToOutput;
        }

        bool isOverQuota = activeGates > currentLevel.maxGateCardsQuota;

        if (!levelManager.IsVerifying && outputNode != null && outputNode.isTracked)
        {
            bool curA = inputANode != null && inputANode.currentOutputValue;
            bool curB = inputBNode != null && inputBNode.currentOutputValue;
            int rowIndex = (curA ? 2 : 0) + (curB ? 1 : 0);
            bool matchesTarget = outputNode.currentOutputValue == currentLevel.expectedTruthTable[rowIndex];

            if (matchesTarget)
            {
                debugPassedRowMask |= (1 << rowIndex);
                debugFailedRowMask &= ~(1 << rowIndex);
            }
            else
            {
                debugFailedRowMask |= (1 << rowIndex);
                debugPassedRowMask &= ~(1 << rowIndex);
            }
        }

        int passed = 0;
        for (int b = 0; b < 4; b++)
        {
            if ((debugPassedRowMask & (1 << b)) != 0)
            {
                passed++;
            }
        }
        passedRowCount = passed;

        // Verifikasi matematis penuh 4 baris graf live (Zero-GC, di-cache per topologi & level) agar status LULUS 4/4 tidak pernah salah
        if (topologyOrLevelChanged || !hasCachedTopologyTruthEval)
        {
            cachedTopologyMatchesTruthTable = DoesLiveCircuitMatchExpectedTruthTable(currentLevel.expectedTruthTable);
            hasCachedTopologyTruthEval = true;
        }
        bool liveCircuitMatchesTruthTable = cachedTopologyMatchesTruthTable;

        OutputDebugGuidanceState resultState;
        if (isOverQuota)
        {
            resultState = OutputDebugGuidanceState.ReduceGates;
        }
        else if (debugFailedRowMask != 0)
        {
            resultState = OutputDebugGuidanceState.CheckRedCells;
        }
        else if (liveCircuitMatchesTruthTable && passed >= 4)
        {
            passedRowCount = 4;
            resultState = OutputDebugGuidanceState.PassedAllFour;
        }
        else
        {
            resultState = OutputDebugGuidanceState.TestSwitches;
        }

        isLiveCircuitPassedAllFour = (resultState == OutputDebugGuidanceState.PassedAllFour);
        TriggerTruthPodDebugSfx(resultState, passedRowCount, outputWorldPos);
        return resultState;
    }

    private bool DoesLiveCircuitMatchExpectedTruthTable(bool[] expectedTruthTable)
    {
        if (expectedTruthTable == null || expectedTruthTable.Length < 4)
        {
            return false;
        }

        if (outputNode == null
            || !outputNode.isTracked
            || outputNode.connectedInputs == null
            || outputNode.connectedInputs.Count == 0
            || outputNode.connectedInputs[0] == null
            || !outputNode.connectedInputs[0].isTracked)
        {
            return false;
        }

        GateNode root = outputNode.connectedInputs[0];
        for (int row = 0; row < 4; row++)
        {
            bool valA = (row & 2) != 0;
            bool valB = (row & 1) != 0;
            truthEvalVisited.Clear();
            if (!TryEvaluateLiveNodeForInputs(root, valA, valB, depth: 0, out bool actualOut)
                || actualOut != expectedTruthTable[row])
            {
                truthEvalVisited.Clear();
                return false;
            }
        }

        truthEvalVisited.Clear();
        return true;
    }

    private bool TryEvaluateLiveNodeForInputs(GateNode node, bool valA, bool valB, int depth, out bool value)
    {
        value = false;
        if (node == null || !node.isTracked || depth >= 8)
        {
            return false;
        }

        for (int i = 0; i < truthEvalVisited.Count; i++)
        {
            if (truthEvalVisited[i] == node)
            {
                return false;
            }
        }

        truthEvalVisited.Add(node);
        bool ok = false;

        switch (node.nodeType)
        {
            case NodeType.InputSource:
                if (inputBNode != null && node == inputBNode)
                {
                    value = valB;
                }
                else
                {
                    value = valA;
                }
                ok = true;
                break;

            case NodeType.NOT:
                if (node.connectedInputs != null && node.connectedInputs.Count >= 1
                    && TryEvaluateLiveNodeForInputs(node.connectedInputs[0], valA, valB, depth + 1, out bool notIn))
                {
                    value = !notIn;
                    ok = true;
                }
                break;

            case NodeType.AND:
            case NodeType.OR:
            case NodeType.XOR:
            case NodeType.NAND:
                if (node.connectedInputs != null && node.connectedInputs.Count >= 2
                    && TryEvaluateLiveNodeForInputs(node.connectedInputs[0], valA, valB, depth + 1, out bool leftVal)
                    && TryEvaluateLiveNodeForInputs(node.connectedInputs[1], valA, valB, depth + 1, out bool rightVal))
                {
                    switch (node.nodeType)
                    {
                        case NodeType.AND: value = leftVal && rightVal; break;
                        case NodeType.OR: value = leftVal || rightVal; break;
                        case NodeType.XOR: value = leftVal ^ rightVal; break;
                        case NodeType.NAND: value = !(leftVal && rightVal); break;
                    }
                    ok = true;
                }
                break;

            case NodeType.OutputActuator:
                if (node.connectedInputs != null && node.connectedInputs.Count >= 1)
                {
                    ok = TryEvaluateLiveNodeForInputs(node.connectedInputs[0], valA, valB, depth + 1, out value);
                }
                break;
        }

        if (truthEvalVisited.Count > 0)
        {
            truthEvalVisited.RemoveAt(truthEvalVisited.Count - 1);
        }
        return ok;
    }

    private void TriggerTruthPodDebugSfx(OutputDebugGuidanceState resultState, int passedRowCount, Vector3 outputWorldPos)
    {
        int newlyPassedBits = debugPassedRowMask & ~lastSfxPassedRowMask;
        int newlyFailedBits = debugFailedRowMask & ~lastSfxFailedRowMask;

        if (spatialSfx != null && isAirWeavingActive)
        {
            if (resultState == OutputDebugGuidanceState.ReduceGates
                && lastSfxGuidanceState != OutputDebugGuidanceState.ReduceGates)
            {
                spatialSfx.PlaySoftReject(outputWorldPos);
            }
            else if (resultState == OutputDebugGuidanceState.PassedAllFour
                     && lastSfxGuidanceState != OutputDebugGuidanceState.PassedAllFour)
            {
                spatialSfx.PlayTruthPodPassAllFour(outputWorldPos);
            }
            else if (resultState != OutputDebugGuidanceState.ReduceGates && newlyFailedBits != 0)
            {
                spatialSfx.PlayTruthPodRedWarning(outputWorldPos);
            }
            else if (resultState != OutputDebugGuidanceState.ReduceGates
                     && resultState != OutputDebugGuidanceState.PassedAllFour
                     && newlyPassedBits != 0)
            {
                spatialSfx.PlayTruthPodStep(passedRowCount, outputWorldPos);
            }
        }

        lastSfxPassedRowMask = debugPassedRowMask;
        lastSfxFailedRowMask = debugFailedRowMask;
        lastSfxGuidanceState = resultState;
    }

    private void UpdateAirWeavingStatusFeedback()
    {
        float dt = Time.unscaledDeltaTime;
        fpsTimer += dt;
        if (fpsTimer >= 0.50f)
        {
            displayedInferenceFps = Mathf.RoundToInt(inferenceFramesInWindow / fpsTimer);
            inferenceFramesInWindow = 0;
            fpsTimer = 0f;
        }

        if (IsAnyHandDetected)
        {
            if (noHandToastShown) { noHandToastShown = false; levelManager?.ClearLowPriorityNoHandToast(); }
            noHandAbsentTimer = 0f;
        }
        else if (!IsAnyUiOverlayOpen && (levelManager == null || !levelManager.IsVerifying))
        {
            noHandAbsentTimer += dt;
            if (noHandAbsentTimer >= NoHandPromptDelaySeconds && !noHandToastShown)
            {
                noHandToastShown = true;
                levelManager?.TryShowLowPriorityNoHandToast("Arahkan tangan ke kamera", FeedbackWaitingColor);
            }
        }
    }

    // =========================================================================
    // VISUAL RIG CONSTRUCTION (VIDEO QUAD + WORKBENCH GUIDES + 21 JOINTS/BONES)
    // =========================================================================

    private void EnsureVideoQuadCreated(Camera mainCam)
    {
        if (videoQuadRootObj != null || mainCam == null)
        {
            return;
        }

        videoQuadRootObj = new GameObject("AR_AirWeavingVideoQuad");
        videoQuadRootObj.transform.SetParent(mainCam.transform, false);
        videoQuadRootObj.transform.localPosition = new Vector3(0f, 0f, VideoQuadDepth);
        videoQuadRootObj.transform.localRotation = Quaternion.identity;
        videoQuadRootObj.transform.localScale = Vector3.one;
        videoQuadMirrorTransform = videoQuadRootObj.transform;

        GameObject meshObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
        meshObj.name = "VideoQuadMesh";
        meshObj.transform.SetParent(videoQuadMirrorTransform, false);
        meshObj.transform.localPosition = Vector3.zero;
        meshObj.transform.localRotation = Quaternion.identity;
        meshObj.transform.localScale = Vector3.one;

        Collider col = meshObj.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }

        // ARBackdropDimming (Cull Off, ZWrite Off, ZTest Always, Blend Off, Queue 1500): meredupkan latar + vignette
        // agar hologram, grid 17x13, & kabel langsung kontras tajam tanpa menyentuh BackgroundPlane Vuforia Mode Kartu.
        videoQuadMaterial = AirWeavingLightingAndShaderRig.CreateBackdropDimmingMaterial(out _);
        if (videoQuadMaterial != null)
        {
            ownedMaterials.Add(videoQuadMaterial);
        }
        else
        {
            videoQuadMaterial = CreateSafeSpritesMaterial(Color.white, 1500);
        }
        videoQuadRenderer = meshObj.GetComponent<Renderer>();
        if (videoQuadRenderer != null)
        {
            videoQuadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            videoQuadRenderer.receiveShadows = false;
            if (videoQuadMaterial != null)
            {
                videoQuadRenderer.sharedMaterial = videoQuadMaterial;
            }
        }

        videoQuadMeshTransform = meshObj.transform;
        videoQuadRootObj.SetActive(false);
    }

    private void UpdateVideoQuadTransform(Camera mainCam)
    {
        if (mainCam == null || webCamTexture == null || videoQuadMirrorTransform == null || videoQuadMeshTransform == null)
        {
            return;
        }

        int camW = Mathf.Max(1, webCamTexture.width);
        int camH = Mathf.Max(1, webCamTexture.height);
        int rotAngle = ((webCamTexture.videoRotationAngle % 360) + 360) % 360;
        bool isRotated90 = (rotAngle == 90 || rotAngle == 270);
        bool vMirror = webCamTexture.videoVerticallyMirrored;
        float camFov = mainCam.fieldOfView;
        float camAspect = Mathf.Max(0.1f, mainCam.aspect);

        if (lastVideoQuadCamW == camW
            && lastVideoQuadCamH == camH
            && lastVideoQuadRotAngle == rotAngle
            && lastVideoQuadVMirror == vMirror
            && lastVideoQuadHMirror == mirrorHorizontalDisplay
            && Mathf.Abs(lastVideoQuadFov - camFov) < 0.01f
            && Mathf.Abs(lastVideoQuadAspect - camAspect) < 0.001f)
        {
            return;
        }

        lastVideoQuadCamW = camW;
        lastVideoQuadCamH = camH;
        lastVideoQuadRotAngle = rotAngle;
        lastVideoQuadVMirror = vMirror;
        lastVideoQuadHMirror = mirrorHorizontalDisplay;
        lastVideoQuadFov = camFov;
        lastVideoQuadAspect = camAspect;

        float frustumH = 2f * VideoQuadDepth * Mathf.Tan(camFov * 0.5f * Mathf.Deg2Rad);
        float frustumW = frustumH * camAspect;
        float videoAspect = isRotated90 ? ((float)camH / camW) : ((float)camW / camH);

        if (videoAspect >= camAspect)
        {
            currentVideoDisplayHeight = frustumH;
            currentVideoDisplayWidth = frustumH * videoAspect;
        }
        else
        {
            currentVideoDisplayWidth = frustumW;
            currentVideoDisplayHeight = frustumW / Mathf.Max(0.1f, videoAspect);
        }

        videoQuadMirrorTransform.localPosition = new Vector3(0f, 0f, VideoQuadDepth);
        videoQuadMirrorTransform.localRotation = Quaternion.identity;
        videoQuadMirrorTransform.localScale = new Vector3(mirrorHorizontalDisplay ? -1f : 1f, 1f, 1f);

        float meshW = isRotated90 ? currentVideoDisplayHeight : currentVideoDisplayWidth;
        float meshH = isRotated90 ? currentVideoDisplayWidth : currentVideoDisplayHeight;
        videoQuadMeshTransform.localRotation = Quaternion.Euler(0f, 0f, -rotAngle);
        videoQuadMeshTransform.localScale = new Vector3(meshW, vMirror ? -meshH : meshH, 1f);
    }

    private void EnsureWorkbenchVisualsCreated()
    {
        if (workbenchVisualRootObj != null)
        {
            return;
        }

        workbenchVisualRootObj = new GameObject("AR_AirWeavingWorkbenchGuides");
        workbenchVisualRootObj.transform.SetParent(transform, false);

        hoverRingMat = CreateSafeSpritesMaterial(HoverGateRingColor, 2992);
        airDrawStrokeMat = CreateSafeSpritesMaterial(Color.white, 2993);

        // 1. Panggung 3D HologramStageRoot + Lantai Proyeksi Grid Sci-Fi + Grounded Contact Rings (Tahap 4 v8.0)
        if (stageController == null)
        {
            stageController = GetComponent<AirWeavingStageController>() ?? gameObject.AddComponent<AirWeavingStageController>();
        }
        stageController.EnsureInitialized(ResolveMainCamera());

        if (lightingAndShaderRig == null)
        {
            lightingAndShaderRig = GetComponent<AirWeavingLightingAndShaderRig>() ?? gameObject.AddComponent<AirWeavingLightingAndShaderRig>();
        }

        if (floatingPropRig == null)
        {
            floatingPropRig = GetComponent<AirWeavingFloatingPropRig>() ?? gameObject.AddComponent<AirWeavingFloatingPropRig>();
        }
        lightingAndShaderRig.SetFloatingPropRig(floatingPropRig);

        // 2. Cincin Hover / Grab Highlight ramping di sekitar alas target gerbang atau terminal Input
        GameObject hoverObj = new GameObject("AR_HoverHighlightRing");
        hoverObj.transform.SetParent(workbenchVisualRootObj.transform, false);
        hoverHighlightRingRenderer = hoverObj.AddComponent<LineRenderer>();
        hoverHighlightRingRenderer.useWorldSpace = true;
        hoverHighlightRingRenderer.loop = true;
        hoverHighlightRingRenderer.positionCount = HoverRingSegments;
        hoverHighlightRingRenderer.widthMultiplier = 0.007f;
        hoverHighlightRingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        hoverHighlightRingRenderer.receiveShadows = false;
        if (hoverRingMat != null)
        {
            hoverHighlightRingRenderer.sharedMaterial = hoverRingMat;
        }
        hoverHighlightRingRenderer.enabled = false;

        // 3. Lintasan goresan Air-Draw berpendar sian-violet (AR_AirDrawStrokeLine)
        GameObject strokeObj = new GameObject("AR_AirDrawStrokeLine");
        strokeObj.transform.SetParent(workbenchVisualRootObj.transform, false);
        airDrawStrokeRenderer = strokeObj.AddComponent<LineRenderer>();
        airDrawStrokeRenderer.useWorldSpace = true;
        airDrawStrokeRenderer.positionCount = 0;
        airDrawStrokeRenderer.widthMultiplier = 0.022f;
        airDrawStrokeRenderer.numCapVertices = 4;
        airDrawStrokeRenderer.numCornerVertices = 4;
        airDrawStrokeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        airDrawStrokeRenderer.receiveShadows = false;
        airDrawStrokeRenderer.startColor = AirDrawStrokeStartColor;
        airDrawStrokeRenderer.endColor = AirDrawStrokeEndColor;
        if (airDrawStrokeMat != null)
        {
            airDrawStrokeRenderer.sharedMaterial = airDrawStrokeMat;
        }
        airDrawStrokeRenderer.enabled = false;
        if (outputTrigger == null)
        {
            outputTrigger = GetComponent<AirWeavingOutputTrigger>() ?? gameObject.AddComponent<AirWeavingOutputTrigger>();
            outputTrigger.OnVerifyRequested += HandleOutputTriggerVerify;
            outputTrigger.OnNextLevelRequested += HandleOutputTriggerNextLevel;
        }
        outputTrigger.EnsureVisualsCreated();
        if (hologramVisual == null)
        {
            hologramVisual = GetComponent<AirWeavingHologramVisual>() ?? gameObject.AddComponent<AirWeavingHologramVisual>();
        }
        hologramVisual.EnsureVisualsCreated();
        if (cableRouter == null)
        {
            cableRouter = GetComponent<AirWeavingCableRouter>() ?? gameObject.AddComponent<AirWeavingCableRouter>();
            cableRouter.OnWireGrabbedSfx += HandleCableGrabSfx;
            cableRouter.OnWireTargetLockSfx += HandleCableTargetLockSfx;
            cableRouter.OnWireTargetUnlockSfx += HandleCableTargetUnlockSfx;
            cableRouter.OnWireConnectedSfx += HandleCableConnectSfx;
            cableRouter.OnWireDisconnectedSfx += HandleCableDetachSfx;
            cableRouter.OnWireRejectSfx += HandleCableRejectSfx;
        }
        cableRouter.EnsureVisualsCreated();
        if (summonVisual == null)
        {
            summonVisual = GetComponent<AirWeavingSummonVisual>() ?? gameObject.AddComponent<AirWeavingSummonVisual>();
        }
        summonVisual.EnsureVisualsCreated();
        if (spatialSfx == null)
        {
            spatialSfx = GetComponent<AirWeavingSpatialSfx>() ?? gameObject.AddComponent<AirWeavingSpatialSfx>();
        }
        spatialSfx.EnsureInitialized();
        spatialSfx.Configure(levelManager, ResolveMainCamera());
        if (gestureGuide == null)
        {
            gestureGuide = GetComponent<AirWeavingGestureGuide>() ?? gameObject.AddComponent<AirWeavingGestureGuide>();
        }
        if (cachedHudMenu == null && levelManager != null)
        {
            cachedHudMenu = levelManager.GetComponent<CompactHudMenuController>();
        }
        BindHudMenuEvents(cachedHudMenu);
        gestureGuide.Configure(cachedHudMenu, spatialSfx, ResolveMainCamera(), txtFeedback != null ? txtFeedback.font : null);

        workbenchVisualRootObj.SetActive(false);
    }

    private void UpdateWorkbenchGuideVisuals(Camera mainCam)
    {
        if (mainCam == null || stageController == null || !isAirWeavingActive)
        {
            return;
        }

        float aspectScale = ComputeHorizontalAspectScale(mainCam);
        bool isGrabbingGate = grabbedGateSlotIndex >= 0
                              && grabbedGateSlotIndex < gateSlots.Count
                              && gateSlots[grabbedGateSlotIndex] != null;
        Vector2 grabbedXY = isGrabbingGate ? gateSlots[grabbedGateSlotIndex].currentLocalXY : Vector2.zero;
        float grabbedHoverY = isGrabbingGate ? gateSlots[grabbedGateSlotIndex].currentLocalZ : BaseHoverHeight;
        bool grabbedOutside = isGrabbingGate && IsOutsideAssemblyZone(grabbedXY, aspectScale);
        bool showGridSnapTarget = isGrabbingGate && !grabbedOutside;
        Vector2 gridSnapPreviewXZ = showGridSnapTarget
            ? ResolveSnappedAutoSeparateGridXZ(grabbedXY, grabbedGateSlotIndex, aspectScale)
            : Vector2.zero;

        // Susun deskriptor 8 Grounded Contact Rings di bidang meja XZ untuk SELURUH 8 node (termasuk gerbang di Rak Parkir)
        stageContactNodesBuffer[0] = new AirWeavingStageController.ContactNodeDescriptor
        {
            active = inputANode != null && inputANode.isTracked,
            stageLocalPos = new Vector3(InputTerminalBaseX * aspectScale, InputAHoverHeight, InputATerminalY),
            baseRadius = 0.205f,
            isInputA = true,
            isInputB = false,
            isOutput = false,
            isParked = false,
            isGrabbed = false,
            isInDeleteZone = false
        };

        stageContactNodesBuffer[1] = new AirWeavingStageController.ContactNodeDescriptor
        {
            active = inputBNode != null && inputBNode.isTracked,
            stageLocalPos = new Vector3(InputTerminalBaseX * aspectScale, InputBHoverHeight, InputBTerminalY),
            baseRadius = 0.205f,
            isInputA = false,
            isInputB = true,
            isOutput = false,
            isParked = false,
            isGrabbed = false,
            isInDeleteZone = false
        };

        stageContactNodesBuffer[2] = new AirWeavingStageController.ContactNodeDescriptor
        {
            active = outputNode != null && outputNode.isTracked,
            stageLocalPos = new Vector3(OutputTerminalBaseX * aspectScale, OutputHoverHeight, OutputTerminalY),
            baseRadius = 0.235f,
            isInputA = false,
            isInputB = false,
            isOutput = true,
            isParked = false,
            isGrabbed = false,
            isInDeleteZone = false
        };

        for (int i = 0; i < 5; i++)
        {
            WorkbenchGateSlot slot = i < gateSlots.Count ? gateSlots[i] : null;
            bool isSlotGrabbed = i == grabbedGateSlotIndex;
            bool showGateContactRing = slot != null && slot.node != null;
            bool isParkedSlot = showGateContactRing && !slot.isPlacedOnWorkbench && !isSlotGrabbed;
            bool slotInDelete = isSlotGrabbed && grabbedOutside;

            stageContactNodesBuffer[3 + i] = new AirWeavingStageController.ContactNodeDescriptor
            {
                active = showGateContactRing,
                stageLocalPos = showGateContactRing
                    ? new Vector3(slot.currentLocalXY.x, slot.currentLocalZ, slot.currentLocalXY.y)
                    : Vector3.zero,
                baseRadius = isParkedSlot ? 0.145f : 0.195f,
                isInputA = false,
                isInputB = false,
                isOutput = false,
                isParked = isParkedSlot,
                isGrabbed = isSlotGrabbed,
                isInDeleteZone = slotInDelete
            };
        }

        stageController.UpdateFloorAndContactRings(
            mainCam,
            aspectScale,
            AssemblyMinBaseX,
            AssemblyMaxBaseX,
            AssemblyMinY,
            AssemblyMaxY,
            InputTerminalBaseX,
            OutputTerminalBaseX,
            ParkingRackY,
            isGrabbingGate,
            grabbedXY,
            grabbedOutside,
            stageContactNodesBuffer,
            stageContactNodesBuffer.Length,
            showGridSnapTarget,
            gridSnapPreviewXZ,
            grabbedHoverY);
    }

    private void EnsureHandVisualRigCreated()
    {
        if (handRigRootObj != null)
        {
            return;
        }

        handRigRootObj = new GameObject("AR_AirWeavingHandRig");
        handRigRootObj.transform.SetParent(transform, false);

        // RenderQueue 2994..2996: berada di bawah pil & teks label 3D (2998..3000) agar titik jari tidak menutupi teks
        boneMat = CreateSafeSpritesMaterial(NeonCyanBoneColor, 2994);
        jointBaseMat = CreateSafeSpritesMaterial(NeonJointBaseColor, 2995);
        jointTipMat = CreateSafeSpritesMaterial(NeonJointTipColor, 2996);
        trailMat = CreateSafeSpritesMaterial(Color.white, 2996);
        pinchRingMat = CreateSafeSpritesMaterial(PinchIdleColor, 3018);

        // 1 Shared procedural low-poly sphere mesh (6x4 = 48 segitiga vs 760 segitiga PrimitiveType.Sphere -> -93% geometri untuk 42 sendi)
        if (sharedJointSphereMesh == null)
        {
            sharedJointSphereMesh = BuildLowPolyJointSphereMesh(lonSegments: 6, latSegments: 4, radius: 0.5f);
        }

        // Skala sendi & garis tulang dibuat ramping & halus (~65% lebih kecil) agar tidak membebani visual papan kerja
        jointSphereObjs = new GameObject[LandmarkCount];
        for (int i = 0; i < LandmarkCount; i++)
        {
            bool isTip = (i == 4 || i == 8 || i == 12 || i == 16 || i == 20);
            bool isWrist = (i == 0);
            float diameter = isTip ? 0.022f : (isWrist ? 0.024f : 0.015f);

            GameObject jointObj = new GameObject($"Joint_{i}");
            jointObj.transform.SetParent(handRigRootObj.transform, false);
            jointObj.transform.localScale = Vector3.one * diameter;

            MeshFilter mf = jointObj.AddComponent<MeshFilter>();
            mf.sharedMesh = sharedJointSphereMesh;

            MeshRenderer rend = jointObj.AddComponent<MeshRenderer>();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.sharedMaterial = isTip ? jointTipMat : jointBaseMat;

            jointSphereObjs[i] = jointObj;
        }

        int boneCount = HandBoneConnections.GetLength(0);
        boneLineRenderers = new LineRenderer[boneCount];
        for (int b = 0; b < boneCount; b++)
        {
            GameObject boneObj = new GameObject($"Bone_{b}");
            boneObj.transform.SetParent(handRigRootObj.transform, false);

            LineRenderer lr = boneObj.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.widthMultiplier = 0.0075f;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.startColor = NeonCyanBoneColor;
            lr.endColor = NeonCyanBoneColor;
            if (boneMat != null)
            {
                lr.sharedMaterial = boneMat;
            }

            boneLineRenderers[b] = lr;
        }

        indexTrailObj = new GameObject("AR_IndexNeonTrail");
        indexTrailObj.transform.SetParent(handRigRootObj.transform, false);
        indexTrailRenderer = indexTrailObj.AddComponent<TrailRenderer>();
        indexTrailRenderer.time = 0.34f;
        indexTrailRenderer.minVertexDistance = 0.014f;
        indexTrailRenderer.startWidth = 0.056f;
        indexTrailRenderer.endWidth = 0.004f;
        indexTrailRenderer.numCapVertices = 4;
        indexTrailRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        indexTrailRenderer.receiveShadows = false;
        if (trailMat != null)
        {
            indexTrailRenderer.sharedMaterial = trailMat;
        }

        Gradient trailGradient = new Gradient();
        trailGradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.84f, 1.00f, 1.00f), 0f),
                new GradientColorKey(new Color(0.22f, 0.96f, 1.00f), 0.38f),
                new GradientColorKey(new Color(0.58f, 0.38f, 1.00f), 0.80f),
                new GradientColorKey(new Color(0.20f, 0.16f, 0.80f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1.00f, 0f),
                new GradientAlphaKey(0.76f, 0.45f),
                new GradientAlphaKey(0.28f, 0.82f),
                new GradientAlphaKey(0.00f, 1f)
            });
        indexTrailRenderer.colorGradient = trailGradient;

        GameObject ringObj = new GameObject("AR_PinchCursorRing");
        ringObj.transform.SetParent(handRigRootObj.transform, false);
        pinchCursorRingRenderer = ringObj.AddComponent<LineRenderer>();
        pinchCursorRingRenderer.useWorldSpace = true;
        pinchCursorRingRenderer.loop = true;
        pinchCursorRingRenderer.positionCount = PinchRingSegments;
        pinchCursorRingRenderer.widthMultiplier = 0.012f;
        pinchCursorRingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pinchCursorRingRenderer.receiveShadows = false;
        if (pinchRingMat != null)
        {
            pinchCursorRingRenderer.sharedMaterial = pinchRingMat;
        }

        handRigRootObj.SetActive(false);

        // Rig 3D neon tangan ke-2 (slot sekunder) memakai material terpisah agar transisi fade alpha independen per slot (Zero-GC)
        secondaryBoneMat = CreateSafeSpritesMaterial(NeonCyanBoneColor, 2994);
        secondaryJointBaseMat = CreateSafeSpritesMaterial(NeonJointBaseColor, 2995);
        secondaryJointTipMat = CreateSafeSpritesMaterial(NeonJointTipColor, 2996);

        secondaryHandRigRootObj = new GameObject("AR_AirWeavingSecondaryHandRig");
        secondaryHandRigRootObj.transform.SetParent(transform, false);
        secondaryJointSphereObjs = new GameObject[LandmarkCount];
        for (int i = 0; i < LandmarkCount; i++)
        {
            bool isTip = (i == 4 || i == 8 || i == 12 || i == 16 || i == 20);
            bool isWrist = (i == 0);
            float diameter = isTip ? 0.022f : (isWrist ? 0.024f : 0.015f);

            GameObject jointObj = new GameObject($"SecJoint_{i}");
            jointObj.transform.SetParent(secondaryHandRigRootObj.transform, false);
            jointObj.transform.localScale = Vector3.one * diameter;

            MeshFilter mf = jointObj.AddComponent<MeshFilter>();
            mf.sharedMesh = sharedJointSphereMesh;

            MeshRenderer rend = jointObj.AddComponent<MeshRenderer>();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.sharedMaterial = isTip ? secondaryJointTipMat : secondaryJointBaseMat;

            secondaryJointSphereObjs[i] = jointObj;
        }

        secondaryBoneLineRenderers = new LineRenderer[boneCount];
        for (int b = 0; b < boneCount; b++)
        {
            GameObject boneObj = new GameObject($"SecBone_{b}");
            boneObj.transform.SetParent(secondaryHandRigRootObj.transform, false);

            LineRenderer lr = boneObj.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.widthMultiplier = 0.0075f;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.startColor = NeonCyanBoneColor;
            lr.endColor = NeonCyanBoneColor;
            if (secondaryBoneMat != null)
            {
                lr.sharedMaterial = secondaryBoneMat;
            }

            secondaryBoneLineRenderers[b] = lr;
        }

        secondaryHandRigRootObj.SetActive(false);
    }

    private static Mesh BuildLowPolyJointSphereMesh(int lonSegments, int latSegments, float radius)
    {
        Mesh mesh = new Mesh { name = "Holo_SharedLowPolyJointSphere" };
        int lonCount = lonSegments + 1;
        int latCount = latSegments + 1;
        Vector3[] verts = new Vector3[lonCount * latCount];
        Vector3[] norms = new Vector3[lonCount * latCount];
        int[] tris = new int[lonSegments * latSegments * 6];

        int vi = 0;
        for (int lat = 0; lat < latCount; lat++)
        {
            float phi = (lat / (float)latSegments) * Mathf.PI;
            float sinPhi = Mathf.Sin(phi);
            float cosPhi = Mathf.Cos(phi);
            for (int lon = 0; lon < lonCount; lon++)
            {
                float theta = (lon % lonSegments) * ((Mathf.PI * 2f) / lonSegments);
                Vector3 n = new Vector3(sinPhi * Mathf.Cos(theta), cosPhi, sinPhi * Mathf.Sin(theta));
                verts[vi] = n * radius;
                norms[vi] = n;
                vi++;
            }
        }

        int ti = 0;
        for (int lat = 0; lat < latSegments; lat++)
        {
            int rowA = lat * lonCount;
            int rowB = (lat + 1) * lonCount;
            for (int lon = 0; lon < lonSegments; lon++)
            {
                int v00 = rowA + lon;
                int v01 = rowA + lon + 1;
                int v10 = rowB + lon;
                int v11 = rowB + lon + 1;

                tris[ti++] = v00;
                tris[ti++] = v01;
                tris[ti++] = v10;

                tris[ti++] = v01;
                tris[ti++] = v11;
                tris[ti++] = v10;
            }
        }

        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetIndices(tris, MeshTopology.Triangles, 0, calculateBounds: true);
        return mesh;
    }

    private Material CreateSafeSpritesMaterial(Color tint, int renderQueue)
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
        ownedMaterials.Add(mat);
        return mat;
    }

    private void SetHandRigVisible(bool visible)
    {
        if (handRigRootObj != null && handRigRootObj.activeSelf != visible)
        {
            handRigRootObj.SetActive(visible);
            if (!visible && indexTrailRenderer != null)
            {
                indexTrailRenderer.Clear();
            }
        }
    }

    private void SetSecondaryHandRigVisible(bool visible)
    {
        if (secondaryHandRigRootObj != null && secondaryHandRigRootObj.activeSelf != visible)
        {
            secondaryHandRigRootObj.SetActive(visible);
        }
    }

    private void SetHoverRingVisible(bool visible)
    {
        if (hoverHighlightRingRenderer != null && hoverHighlightRingRenderer.enabled != visible)
        {
            hoverHighlightRingRenderer.enabled = visible;
        }
    }

    private void HideAllAirWeavingVisuals()
    {
        isHandDetected = false;
        wasHandDetected = false;
        isSecondaryHandDetected = false;
        wasSecondaryHandDetected = false;
        primarySlotActive = false;
        primaryMissedLastInference = false;
        primaryMissingTimer = 0f;
        primarySlotHandedness = TrackedHandedness.Unknown;
        secondarySlotActive = false;
        secondaryMissedLastInference = false;
        secondaryMissingTimer = 0f;
        secondarySlotHandedness = TrackedHandedness.Unknown;
        rawPinching = false;
        isPinching = false;
        wasPinching = false;
        hoveredGateSlotIndex = -1;
        secondaryHoveredGateSlotIndex = -1;
        hoveredInputNode = null;
        secondaryHoveredInputNode = null;
        activeCableHoverHandSlot = -1;
        isIndexOnlyWandPose = false;
        CancelAirDrawStrokeImmediate();
        ResetLandmarkOneEuroFilters();
        ResetSecondaryLandmarkOneEuroFilters();
        ResetExclusiveArbiterAndTwoHandState();
        ResetInputTouchState();
        handGestureDetector.Reset();
        secondaryHandGestureDetector.Reset();

        SetHandRigVisible(false);
        SetSecondaryHandRigVisible(false);
        SetHoverRingVisible(false);
        stageController?.HideImmediate();
        outputTrigger?.HideImmediate();
        hologramVisual?.HideImmediate();
        cableRouter?.HideImmediate();
        ResetAllCircleSummoners();
        lastSfxSummonStage = AirWeavingCircleSummoner.SummonStage.Idle;
        lastSfxSummonChargeStep = 0;
        wasGrabbedGateInDeleteZone = false;
        lastOutputHoldSfxStep = 0;
        handAbsentForWakeSeconds = 0f;
        wasHandDetectedForWakeSfx = false;
        ResetHoverSfxTracking();
        ResetLiveDebugMirror();
        if (spatialSfx != null)
        {
            spatialSfx.StopAllImmediate();
        }
        gestureGuide?.OnAirWeavingModeExited();
        summonVisual?.HideImmediate();

        if (workbenchVisualRootObj != null && workbenchVisualRootObj.activeSelf)
        {
            workbenchVisualRootObj.SetActive(false);
        }

        if (videoQuadMaterial != null)
        {
            videoQuadMaterial.mainTexture = null;
        }

        if (videoQuadRootObj != null && videoQuadRootObj.activeSelf)
        {
            videoQuadRootObj.SetActive(false);
        }
    }

    // =========================================================================
    // CLEANUP & LIFECYCLE SAFETY (ANTI-SIGABRT WINDOWS EDITOR & ZERO LEAKS)
    // =========================================================================

    private void ShutdownNativeAndCameraImmediate()
    {
        // Urutan shutdown deterministik anti-SIGABRT:
        // 1. stopRequested = true -> signal event -> workerThread.Join(1500)
        bool workerExitedCleanly = StopInferenceWorkerThread();

        // 2. Pastikan seluruh AsyncGPUReadback yang sedang berjalan selesai sebelum membebaskan RenderTexture / NativeArray
        if (gpuReadbackInFlight)
        {
            try
            {
                AsyncGPUReadback.WaitAllRequests();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AirWeavingController] AsyncGPUReadback wait warning: {ex.Message}");
            }
            gpuReadbackInFlight = false;
        }

        // 3. Bebaskan HandLandmarker native hanya setelah worker thread berhenti penuh
        if (workerExitedCleanly)
        {
            DisposeHandLandmarkerOnly();
        }

        // 4. Hentikan & hancurkan WebCamTexture
        if (webCamTexture != null)
        {
            try
            {
                if (webCamTexture.isPlaying)
                {
                    webCamTexture.Stop();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AirWeavingController] WebCam stop warning: {ex.Message}");
            }

            if (videoQuadMaterial != null && ReferenceEquals(videoQuadMaterial.mainTexture, webCamTexture))
            {
                videoQuadMaterial.mainTexture = null;
            }

            Destroy(webCamTexture);
            webCamTexture = null;
        }

        // 5. Bebaskan RenderTexture downscale GPU
        if (inferenceRenderTexture != null)
        {
            inferenceRenderTexture.Release();
            Destroy(inferenceRenderTexture);
            inferenceRenderTexture = null;
        }

        // 6. Bebaskan seluruh NativeArray triple-buffer RGBA & buffer RGB worker
        if (workerExitedCleanly)
        {
            for (int i = 0; i < inputRgbaSlots.Length; i++)
            {
                if (inputRgbaSlots[i].IsCreated)
                {
                    inputRgbaSlots[i].Dispose();
                }
            }
            if (workerRgbPixels.IsCreated)
            {
                workerRgbPixels.Dispose();
            }
        }

        inputSlotWidth = 0;
        inputSlotHeight = 0;
        workerRgbWidth = 0;
        workerRgbHeight = 0;
        hasPendingWorkerFrame = false;
        workerIsProcessingFrame = false;
        latestResultSequence = 0;
        consumedResultSequence = 0;
        asyncGpuReadbackErrorCount = 0;

        rawWebcamPixels = null;
        downscaleLutX0 = null;
        downscaleLutX1 = null;
        downscaleLutY0 = null;
        downscaleLutY1 = null;
        lutSrcW = 0;
        lutSrcH = 0;
        lutDstW = 0;
        lutDstH = 0;
        lutRotAngle = -1;
        lastVideoQuadCamW = -1;
        lastCamWidth = 0;
        lastCamHeight = 0;

        // 7. Reset Protobuf native log handler paling akhir setelah seluruh native resource MediaPipe dibersihkan
        if (workerExitedCleanly && protobufLogHandlerActive)
        {
            try
            {
                Protobuf.ResetLogHandler();
            }
            catch (Exception)
            {
                // Abaikan bila runtime native sudah dimatikan
            }
            protobufLogHandlerActive = false;
        }
    }

    private void DisposeHandLandmarkerOnly()
    {
        if (handLandmarker != null)
        {
            try
            {
                ((IDisposable)handLandmarker).Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AirWeavingController] HandLandmarker dispose warning: {ex.Message}");
            }
            handLandmarker = null;
        }
        cachedResult = default;
    }

    private void DestroyVisualRigAndMaterials()
    {
        if (videoQuadRootObj != null)
        {
            Destroy(videoQuadRootObj);
            videoQuadRootObj = null;
        }
        videoQuadMirrorTransform = null;
        videoQuadMeshTransform = null;
        videoQuadRenderer = null;

        if (workbenchVisualRootObj != null)
        {
            Destroy(workbenchVisualRootObj);
            workbenchVisualRootObj = null;
        }
        hoverHighlightRingRenderer = null;
        airDrawStrokeRenderer = null;

        if (handRigRootObj != null)
        {
            Destroy(handRigRootObj);
            handRigRootObj = null;
        }
        jointSphereObjs = null;
        boneLineRenderers = null;
        if (secondaryHandRigRootObj != null)
        {
            Destroy(secondaryHandRigRootObj);
            secondaryHandRigRootObj = null;
        }
        if (sharedJointSphereMesh != null)
        {
            Destroy(sharedJointSphereMesh);
            sharedJointSphereMesh = null;
        }
        secondaryJointSphereObjs = null;
        secondaryBoneLineRenderers = null;
        indexTrailObj = null;
        indexTrailRenderer = null;
        pinchCursorRingRenderer = null;

        for (int i = 0; i < ownedMaterials.Count; i++)
        {
            if (ownedMaterials[i] != null)
            {
                Destroy(ownedMaterials[i]);
            }
        }
        ownedMaterials.Clear();
        videoQuadMaterial = null;
        jointBaseMat = null;
        jointTipMat = null;
        boneMat = null;
        secondaryJointBaseMat = null;
        secondaryJointTipMat = null;
        secondaryBoneMat = null;
        trailMat = null;
        pinchRingMat = null;
        hoverRingMat = null;
        airDrawStrokeMat = null;
    }

    /// <summary>
    /// Merapikan offset ketinggian Label_Status3D agar duduk dekat di atas masing-masing objek (anti-tabrakan dengan HUD atas),
    /// mempertegas pil gelap AR_LabelBackdrop, &amp; mengangkat renderQueue (2998..3000) khusus selama Mode Udara aktif (0 diff di GateNode.cs).
    /// </summary>
    private void ApplyAirWeavingLabelReadabilityStyles()
    {
        RestoreAirWeavingLabelReadabilityStyles();

        for (int i = 0; i < cachedGateNodes.Count; i++)
        {
            GateNode node = cachedGateNodes[i];
            if (node == null || node.statusLabel == null)
            {
                continue;
            }

            Transform labelT = node.statusLabel.transform;
            MeshRenderer labelMr = node.statusLabel.GetComponent<MeshRenderer>();
            Transform backdropT = labelT.Find("AR_LabelBackdrop");
            Renderer backdropRend = backdropT != null ? backdropT.GetComponent<Renderer>() : null;
            Material backdropMat = backdropRend != null ? backdropRend.sharedMaterial : null;
            Transform pipT = labelT.Find("AR_LabelIdentityPip");
            Renderer pipRend = pipT != null ? pipT.GetComponent<Renderer>() : null;
            Material pipMat = pipRend != null ? pipRend.sharedMaterial : null;

            SavedNodeLabelStyle saved = new SavedNodeLabelStyle
            {
                node = node,
                originalLocalPos = labelT.localPosition,
                originalLocalScale = labelT.localScale,
                labelMeshRenderer = labelMr,
                originalSortingOrder = labelMr != null ? labelMr.sortingOrder : 0,
                backdropMat = backdropMat,
                originalBackdropColor = backdropMat != null ? backdropMat.color : Color.black,
                originalBackdropQueue = backdropMat != null ? backdropMat.renderQueue : 2995,
                pipMat = pipMat,
                originalPipQueue = pipMat != null ? pipMat.renderQueue : 2996
            };
            savedNodeLabelStyles.Add(saved);

            bool isLogicGate = node.nodeType != NodeType.InputSource && node.nodeType != NodeType.OutputActuator;
            Vector3 compactPos = saved.originalLocalPos;
            compactPos.y = Mathf.Max(0.38f, saved.originalLocalPos.y - (isLogicGate ? 0.115f : 0.135f));
            compactPos.z = 0.0f;
            labelT.localPosition = compactPos;

            if (labelMr != null)
            {
                labelMr.sortingOrder = 20;
            }
            if (backdropMat != null)
            {
                backdropMat.color = AirWeavingLabelPillColor;
                backdropMat.renderQueue = 2998;
            }
            if (pipMat != null)
            {
                pipMat.renderQueue = 2999;
            }
        }
    }

    private void RestoreAirWeavingLabelReadabilityStyles()
    {
        for (int i = 0; i < savedNodeLabelStyles.Count; i++)
        {
            SavedNodeLabelStyle s = savedNodeLabelStyles[i];
            if (s.node != null && s.node.statusLabel != null)
            {
                s.node.statusLabel.transform.localPosition = s.originalLocalPos;
                s.node.statusLabel.transform.localScale = s.originalLocalScale;
            }
            if (s.labelMeshRenderer != null)
            {
                s.labelMeshRenderer.sortingOrder = s.originalSortingOrder;
            }
            if (s.backdropMat != null)
            {
                s.backdropMat.color = s.originalBackdropColor;
                s.backdropMat.renderQueue = s.originalBackdropQueue;
            }
            if (s.pipMat != null)
            {
                s.pipMat.renderQueue = s.originalPipQueue;
            }
        }
        savedNodeLabelStyles.Clear();
    }

    /// <summary>
    /// Menerapkan Distance-Compensated Scale Clamping pada seluruh label world-space 3D (KARTU, MODE, LAB, AND/OR/XOR/NAND/NOT)
    /// serta menempatkan OutputTruthPodVisual di tepi luar/depan-bawah dudukan Output dalam ruang lokal meja dengan skala terkendali
    /// agar tidak pernah membengkak raksasa atau terbelah bodi gerbang saat meja di-orbit 360°. Zero-GC.
    /// </summary>
    private void UpdateAirWeavingLabelsAndTruthPodSpatialPose(Camera mainCam)
    {
        if (mainCam == null || !isAirWeavingActive)
        {
            return;
        }

        Transform camT = mainCam.transform;
        Vector3 camPos = camT.position;
        Vector3 camForward = camT.forward;
        Vector3 camUp = camT.up;
        Quaternion camRot = camT.rotation;
        float stageZoom = stageController != null ? stageController.CurrentZoomScale : AirWeavingStageController.DefaultZoomScale;
        float gentleZoom = Mathf.Clamp(Mathf.Lerp(1f, Mathf.Clamp(stageZoom, 0.35f, 3.50f), 0.22f), 0.72f, 1.55f);

        // 1. Distance-Compensated Scale Clamping untuk semua label 3D (KARTU, LAB, AND/OR/XOR/NAND/NOT) di seluruh rentang zoom 0.18x..15.0x
        for (int i = 0; i < savedNodeLabelStyles.Count; i++)
        {
            SavedNodeLabelStyle s = savedNodeLabelStyles[i];
            if (s.node == null || s.node.statusLabel == null)
            {
                continue;
            }

            Transform labelT = s.node.statusLabel.transform;
            labelT.rotation = camRot;

            float camDepth = Mathf.Max(0.65f, Vector3.Dot(labelT.position - camPos, camForward));
            float distRatio = Mathf.Clamp(camDepth / WorkbenchDepth, 0.18f, 2.10f);

            bool isLogicGate = s.node.nodeType != NodeType.InputSource && s.node.nodeType != NodeType.OutputActuator;
            bool isParkedGate = isLogicGate && !s.node.isTracked;
            float baseWorldLabelScale = isParkedGate ? 0.29f : (isLogicGate ? 0.39f : 0.41f);
            float desiredWorldLabelScale = baseWorldLabelScale * distRatio * gentleZoom;

            float parentScale = Mathf.Max(0.08f, s.node.transform.localScale.x);
            float clampedLocalScale = Mathf.Clamp(desiredWorldLabelScale / parentScale, 0.025f, 1.35f);
            labelT.localScale = Vector3.one * clampedLocalScale;
        }

        // 2. Penempatan Spasial OutputTruthPod3D di tepi luar/depan-bawah dudukan Output dalam ruang meja + Scale Clamping
        if (outputNode != null)
        {
            if (cachedOutputTruthPodRootTransform == null)
            {
                cachedOutputTruthPodRootTransform = outputNode.transform.Find("OutputTruthPod3D");
            }

            if (cachedOutputTruthPodRootTransform != null)
            {
                float aspectScale = ComputeHorizontalAspectScale(mainCam);
                Transform stageRoot = stageController != null ? stageController.StageRoot : null;

                // Jangkar di tepi luar-depan-bawah sayap Output dalam ruang lokal meja XZ agar jauh dari lintasan bodi gerbang di arena
                Vector3 basePodWorld = stageRoot != null
                    ? stageRoot.TransformPoint(new Vector3((OutputTerminalBaseX + 0.14f) * aspectScale, 0.075f, OutputTerminalY - 0.15f))
                    : outputNode.transform.position;

                float podCamDepth = Mathf.Max(0.75f, Vector3.Dot(basePodWorld - camPos, camForward));
                float podDistRatio = Mathf.Clamp(podCamDepth / WorkbenchDepth, 0.20f, 1.95f);

                Vector3 finalPodWorld = basePodWorld - (camUp * (0.100f * podDistRatio)) - (camForward * 0.24f);
                cachedOutputTruthPodRootTransform.position = finalPodWorld;
                cachedOutputTruthPodRootTransform.rotation = Quaternion.LookRotation(camUp, -camForward);

                float desiredPodWorldScale = 0.52f * podDistRatio * gentleZoom;
                float outParentScale = Mathf.Max(0.08f, outputNode.transform.localScale.x);
                float clampedPodLocalScale = Mathf.Clamp(desiredPodWorldScale / outParentScale, 0.035f, 1.35f);
                cachedOutputTruthPodRootTransform.localScale = Vector3.one * clampedPodLocalScale;

                lastTruthPodWorldAnchor = finalPodWorld;
            }
        }
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private Camera ResolveMainCamera()
    {
        if (cachedMainCamera == null || !cachedMainCamera.isActiveAndEnabled)
        {
            cachedMainCamera = Camera.main;
        }

        return cachedMainCamera;
    }

    private static Behaviour FindVuforiaBehaviour(Camera targetCam)
    {
        if (targetCam != null)
        {
            Behaviour[] camBehaviours = targetCam.GetComponents<Behaviour>();
            for (int i = 0; i < camBehaviours.Length; i++)
            {
                if (camBehaviours[i] != null
                    && string.Equals(camBehaviours[i].GetType().Name, "VuforiaBehaviour", StringComparison.Ordinal))
                {
                    return camBehaviours[i];
                }
            }
        }

#if UNITY_6000_5_OR_NEWER
        MonoBehaviour[] allBehaviours = FindObjectsByType<MonoBehaviour>();
#else
        MonoBehaviour[] allBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
#endif
        for (int i = 0; i < allBehaviours.Length; i++)
        {
            if (allBehaviours[i] != null
                && string.Equals(allBehaviours[i].GetType().Name, "VuforiaBehaviour", StringComparison.Ordinal))
            {
                return allBehaviours[i];
            }
        }

        return null;
    }

    private void RefreshGateNodesList()
    {
        cachedGateNodes.Clear();

        if (circuitManager != null && circuitManager.allNodes != null && circuitManager.allNodes.Count > 0)
        {
            for (int i = 0; i < circuitManager.allNodes.Count; i++)
            {
                if (circuitManager.allNodes[i] != null)
                {
                    cachedGateNodes.Add(circuitManager.allNodes[i]);
                }
            }
            return;
        }

#if UNITY_6000_5_OR_NEWER
        GateNode[] foundNodes = FindObjectsByType<GateNode>();
#else
        GateNode[] foundNodes = FindObjectsByType<GateNode>(FindObjectsSortMode.None);
#endif
        for (int i = 0; i < foundNodes.Length; i++)
        {
            if (foundNodes[i] != null)
            {
                cachedGateNodes.Add(foundNodes[i]);
            }
        }
    }

    private void ResolveCoreTerminalNodes()
    {
        for (int i = 0; i < cachedGateNodes.Count; i++)
        {
            GateNode node = cachedGateNodes[i];
            if (node == null)
            {
                continue;
            }

            if (node.nodeType == NodeType.OutputActuator && outputNode == null)
            {
                outputNode = node;
            }
            else if (node.nodeType == NodeType.InputSource)
            {
                string upper = node.gameObject.name.ToUpperInvariant();
                if (upper.Contains("B") && inputBNode == null)
                {
                    inputBNode = node;
                }
                else if (inputANode == null)
                {
                    inputANode = node;
                }
                else if (inputBNode == null && node != inputANode)
                {
                    inputBNode = node;
                }
            }
        }
    }

    private GateNode FindGateNodeByType(NodeType type)
    {
        for (int i = 0; i < cachedGateNodes.Count; i++)
        {
            GateNode node = cachedGateNodes[i];
            if (node != null && node.nodeType == type)
            {
                return node;
            }
        }

        return null;
    }

    private void SetFeedbackImmediate(string message, Color color, float holdSeconds = 2.4f)
    {
        if (levelManager != null) { levelManager.ShowFeedbackToast(message, color, holdSeconds > 0f ? holdSeconds : 2.4f); return; }

        if (txtFeedback != null)
        {
            txtFeedback.text = message;
            txtFeedback.color = color;
        }
    }
}
