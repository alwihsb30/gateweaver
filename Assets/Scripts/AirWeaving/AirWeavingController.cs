using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using Mediapipe;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Color = UnityEngine.Color;
using NormalizedLandmark = Mediapipe.Tasks.Components.Containers.NormalizedLandmark;

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
    private const int MaxInferenceLongEdge = 256;
    private const int LandmarkCount = 21;

    private const float VuforiaReleaseDelaySeconds = 0.45f;
    private const float WebCamReleaseDelaySeconds = 0.40f;
    private const float WebCamStartTimeoutSeconds = 4.0f;

    // Geometri Meja Hologram 3D Horizontal (bidang XZ lokal HologramStageRoot, +Y = tinggi melayang di atas meja) — Tahap 4.6 v8.0
    private const float VideoQuadDepth = 8.0f;
    private const float WorkbenchDepth = 3.82f;
    private const float DepthParallaxScale = 0.25f;
    private const float BaseHoverHeight = AirWeavingStageController.BaseHoverHeight; // +0.22m di atas permukaan meja
    private const float GrabExtraLiftHeight = AirWeavingStageController.GrabExtraLiftHeight; // +0.185m ekstra saat dikepal (~0.405m total, anti-fish-eye)
    private const float InputAHoverHeight = BaseHoverHeight + 0.050f;
    private const float InputBHoverHeight = BaseHoverHeight + 0.014f;
    private const float OutputHoverHeight = BaseHoverHeight + 0.032f;
    private const float ParkingRackHoverHeight = 0.340f;
    private const float ArenaArcHeightY = 0.028f;
    private const float ParkingRackArcDepthZ = 0.075f;
    private const float InputATerminalArcYawDeg = 22.0f;
    private const float InputBTerminalArcYawDeg = 13.0f;
    private const float OutputTerminalArcYawDeg = -18.0f;
    private const float ArenaMaxArcYawDeg = 12.5f;
    private const float ParkingMaxArcYawDeg = 16.0f;
    private const float NodeTableErgonomicPitchBiasDeg = -4.0f;

    // Parameter True 3D Hand Depth Tracking (Rigid Metacarpal Palm Span) & Mode Meja Catur 3D (Tahap 4.7 v8.0)
    private const float FistGrabDepthGain = 1.24f;
    private const float MinGrabCameraDepth = 0.85f;
    private const float MaxGrabCameraDepth = 7.50f;
    private const float MinGrabTableLocalY = 0.08f;
    private const float MaxGrabTableLocalY = 1.85f;
    private const float HandCursorDepthTrackGain = 0.72f;
    private const float MinHandRigCameraDepth = 1.25f;
    private const float MaxHandRigCameraDepth = 5.40f;
    private const float MinNearClipSafeOffsetMeters = 0.28f;

    // Parameter Kontrol 2 Tangan Full Free-View 6-DoF (Both Pinch = Orbit 360°/-80°..+85°, Twist & Focal Zoom; 1 Pinch + 1 Fist = Free Air-Pan 3D; Both Fist = Reset) — Polish 4.9
    private const float TwoHandOrbitYawDegPerUnit = 135.0f;
    private const float TwoHandOrbitPitchDegPerUnit = 85.0f;
    private const float TwoHandZoomSensitivity = 0.76f;
    private const float TwoHandFreePanXYGain = 1.05f;
    private const float TwoHandFreePanDepthGain = 0.94f;
    private const float TwoHandFreePanDepthSpanMeters = 1.75f;
    private const float TwoHandFistResetHoldSeconds = 0.55f;
    private const float TwoHandPrePinchPriorityRatio = 0.48f;
    private const float TwoHandOrbitPinchHoldRatio = 0.54f;
    private const float TwoHandFreePanConfirmHoldSeconds = 0.16f;

    // Critically Damped Spring-Damper analitik & Elastic Scale Pop (Tahap 4.5 v8.0 + Inertial Coasting Tahap 4.9)
    private const float GateDragSpringOmega = 30.0f;
    private const float GateLiftZSpringOmega = 26.0f;
    private const float GateScaleSpringOmega = 22.0f;
    private const float GateSettleSpringOmega = 22.0f;
    private const float DragVelocityTiltDegPerUnitSec = 11.5f;
    private const float GateGrabPopAmplitude = 0.10f;
    private const float GatePlaceBloomPopAmplitude = 0.12f;
    private const float InputTogglePopAmplitude = 0.12f;
    private const float GateThrowMaxSpeed = 3.20f;
    private const float GateCoastDamping = 6.5f;
    private const float GateCoastStopSpeed = 0.025f;
    private const float GateCoastEdgeBounceFactor = 0.30f;
    private const float GateDragVelocitySmoothRate = 18.0f;

    // Soft Magnetic Aim-Assist untuk sakelar Input A/B (R=0.30, maxPull=0.45, blend 18/s, 0 saat >1.1 u/s)
    private const float InputSwitchMagnetRadius = 0.30f;
    private const float InputSwitchMagnetMaxPull = 0.45f;
    private const float SoftMagnetMaxHandSpeed = 1.10f;
    private const float SoftMagnetOffsetBlendSpeed = 18.0f;

    // One-Euro Filter tunggal per-landmark (zero-GC, kalibrasi responsif Tahap 3 v8.0: minCutoff=1.3, beta=0.22, dCutoff=2.0)
    private const float OneEuroMinCutoff = 1.3f;
    private const float OneEuroBeta = 0.22f;
    private const float OneEuroDCutoff = 2.0f;
    private const float OneEuroCoordScale = 100.0f;
    private const float OneEuroZMinCutoff = 2.2f;
    private const float OneEuroZBeta = 0.02f;
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
    private const float MinPalmScaleClamp = 0.06f;
    private const float PinchRatioActivateThreshold = 0.34f;
    private const float PinchRatioReleaseThreshold = 0.46f;

    // Radius akses lega (Fist badan gerbang, sentuh telunjuk sakelar Input A/B dengan dwell 75 ms & exit-to-rearm 1.35x)
    private const float GateHoverGrabRadius = 0.220f;
    private const float ParkedGateHoverRadius = 0.245f;
    private const float InputAirTapRadius = 0.205f;
    private const float InputAirTapRearmRadius = InputAirTapRadius * 1.35f;
    private const float InputAirTapDwellSeconds = 0.075f;
    private const float InputAirTapCooldownSeconds = 0.40f;
    private const float InputTouchRippleDuration = 0.34f;
    private const float ParkReturnThresholdY = 0.56f;
    private const float ParkSlotSnapRadius = 0.30f;
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

    // Koordinat meja hologram horizontal XZ (X = kiri-kanan, Y pada konstanta di bawah = kedalaman Z depan-belakang meja)
    private const float InputTerminalBaseX = -1.26f;
    private const float InputATerminalY = 0.16f;
    private const float InputBTerminalY = -0.58f;
    private const float OutputTerminalBaseX = 1.28f;
    private const float OutputTerminalY = -0.18f;
    private const float ParkingRackY = 0.82f;
    private const float AssemblyMinBaseX = -0.74f;
    private const float AssemblyMaxBaseX = 0.76f;
    private const float AssemblyMinY = -0.78f;
    private const float AssemblyMaxY = 0.46f;

    private static readonly float[] ParkingSlotBaseX = { -0.76f, -0.38f, 0.00f, 0.38f, 0.76f };
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

    private struct OneEuroScalarFilter
    {
        private bool initialized;
        private float xPrev;
        private float dxPrev;

        public void Reset()
        {
            initialized = false;
            xPrev = 0f;
            dxPrev = 0f;
        }

        public float Filter(float value, float dt, float minCutoff, float beta, float dCutoff)
        {
            if (!initialized)
            {
                initialized = true;
                xPrev = value;
                dxPrev = 0f;
                return value;
            }

            float rawDx = (value - xPrev) / dt;
            float alphaD = ComputeAlpha(dt, dCutoff);
            float smoothedDx = dxPrev + (alphaD * (rawDx - dxPrev));

            float adaptiveCutoff = minCutoff + (beta * Mathf.Abs(smoothedDx));
            float alphaX = ComputeAlpha(dt, adaptiveCutoff);
            float filtered = xPrev + (alphaX * (value - xPrev));

            xPrev = filtered;
            dxPrev = smoothedDx;
            return filtered;
        }

        private static float ComputeAlpha(float dt, float cutoffHz)
        {
            float omegaDt = TwoPi * cutoffHz * dt;
            return omegaDt / (1f + omegaDt);
        }
    }

    private struct OneEuroLandmarkFilter
    {
        public OneEuroScalarFilter x;
        public OneEuroScalarFilter y;
        public OneEuroScalarFilter z;

        public void Reset()
        {
            x.Reset();
            y.Reset();
            z.Reset();
        }
    }

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
    private float inputADwellTimer = 0f;
    private float inputBDwellTimer = 0f;
    private bool inputARequiresExitRearm = false;
    private bool inputBRequiresExitRearm = false;
    private float inputATouchRippleTimer = 0f;
    private float inputBTouchRippleTimer = 0f;
    private Vector2 inputSwitchMagnetOffsetXY = Vector2.zero;
    private ElasticScalePop inputAScalePop;
    private ElasticScalePop inputBScalePop;

    private Vector3 pinchWorldPosition = Vector3.zero;
    private Vector3 indexTipWorldPosition = Vector3.zero;
    private Vector2 pinchWorkbenchLocalXY = Vector2.zero;
    private Vector2 indexWorkbenchLocalXY = Vector2.zero;
    private readonly Vector3[] smoothedLandmarksWorld = new Vector3[LandmarkCount];
    private readonly Vector3[] secondarySmoothedLandmarksWorld = new Vector3[LandmarkCount];
    private readonly OneEuroLandmarkFilter[] landmarkOneEuroFilters = new OneEuroLandmarkFilter[LandmarkCount];
    private readonly OneEuroLandmarkFilter[] secondaryLandmarkOneEuroFilters = new OneEuroLandmarkFilter[LandmarkCount];
    private long lastLandmarkFilterTimestampMs = 0;
    private Vector2 lastPrimaryPalmNormXY = new Vector2(0.5f, 0.5f);
    private Vector2 lastSecondaryPalmNormXY = new Vector2(0.5f, 0.5f);
    private readonly Vector3[] pinchRingPoints = new Vector3[PinchRingSegments];
    private readonly Vector3[] hoverRingPoints = new Vector3[HoverRingSegments];

    // State Kontrol 2 Tangan Full Free-View 6-DoF (Both Pinch = Orbit/Focal-Zoom, 1 Pinch + 1 Fist = Free Air-Pan 3D, Both Fist = Reset)
    private bool isTwoHandOrbitZoomActive = false;
    private bool isTwoHandFreePanActive = false;
    private float twoHandFreePanConfirmTimer = 0f;
    private bool isTwoHandFistResetCharging = false;
    private bool twoHandFistResetLatched = false;
    private float twoHandFistResetTimer = 0f;
    private float twoHandPinchInitialDist = 0.25f;
    private float twoHandPinchInitialZoom = 1.0f;
    private Vector2 twoHandPinchPrevMidpointXY = Vector2.zero;
    private Vector2 twoHandPanPrevMidpointXY = Vector2.zero;
    private float twoHandPanInitialAvgSpan = AirWeavingHandGestureDetector.DefaultComfortPalmSpan;
    private float twoHandPanInitialPanZ = 0f;
    private float lastPrimaryGrabStartTime = -10f;
    private bool lastPrimaryGrabWasPlacedBeforeGrab = false;
    private Vector2 lastPrimaryGrabPrevPlacedXY = Vector2.zero;
    private float grabStartPalmSpan = AirWeavingHandGestureDetector.DefaultComfortPalmSpan;
    private float grabStartCameraDepth = WorkbenchDepth;

    private readonly List<WorkbenchGateSlot> gateSlots = new List<WorkbenchGateSlot>(5);
    private int grabbedGateSlotIndex = -1;
    private int hoveredGateSlotIndex = -1;
    private GateNode hoveredInputNode = null;

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

    private Coroutine transitionCoroutine;
    private WebCamTexture webCamTexture;
    private Color32[] rawWebcamPixels;
    private NativeArray<byte> nativeWorkPixels;
    private int workWidth = 0;
    private int workHeight = 0;
    private int lastCamWidth = 0;
    private int lastCamHeight = 0;

    private HandLandmarker handLandmarker;
    private HandLandmarkerResult cachedResult;
    private readonly ImageProcessingOptions zeroRotationOptions = new ImageProcessingOptions(rotationDegrees: 0);
    private long lastTimestampMs = 0;

    // Bidang video kamera depan, panduan meja kerja, & rig 3D tangan neon (semua pra-alokasi, tanpa Collider, tanpa shadow)
    private GameObject videoQuadRootObj;
    private Transform videoQuadMirrorTransform;
    private Transform videoQuadMeshTransform;
    private Renderer videoQuadRenderer;
    private Material videoQuadMaterial;
    private float currentVideoDisplayWidth = 1f;
    private float currentVideoDisplayHeight = 1f;

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

    private readonly List<Material> ownedMaterials = new List<Material>(16);
    private Material jointBaseMat;
    private Material jointTipMat;
    private Material boneMat;
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
    private AirWeavingOutputTrigger outputTrigger;
    private AirWeavingHologramVisual hologramVisual;
    private AirWeavingCableRouter cableRouter;
    private AirWeavingSummonVisual summonVisual;
    private AirWeavingSpatialSfx spatialSfx;
    private AirWeavingGestureGuide gestureGuide;
    private CompactHudMenuController cachedHudMenu;
    private readonly AirWeavingHandGestureDetector handGestureDetector = new AirWeavingHandGestureDetector();
    private readonly AirWeavingHandGestureDetector secondaryHandGestureDetector = new AirWeavingHandGestureDetector();
    private readonly AirWeavingCircleSummoner circleSummoner = new AirWeavingCircleSummoner();
    private readonly List<GateNode> activePlacedGatesBuffer = new List<GateNode>(5);
    private readonly AirWeavingStageController.ContactNodeDescriptor[] stageContactNodesBuffer = new AirWeavingStageController.ContactNodeDescriptor[8];
    private float fpsTimer = 0f;
    private float timeSinceLastValidHand = 0f;
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
    public bool IsHandDetected => isHandDetected;
    public bool IsPinching => isPinching;
    public int DisplayedInferenceFps => displayedInferenceFps;
    public Vector3 PinchWorldPosition => pinchWorldPosition;
    public Vector3 IndexTipWorldPosition => indexTipWorldPosition;
    private bool IsAnyUiOverlayOpen => (levelManager != null && levelManager.IsUiOverlayOpen)
                                       || (gestureGuide != null && gestureGuide.IsGuideOverlayOpen);

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
        if (gestureGuide != null)
        {
            gestureGuide.Configure(cachedHudMenu, spatialSfx, ResolveMainCamera(), txtFeedback != null ? txtFeedback.font : null);
        }
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
        hoveredInputNode = null;
        postDropLockoutTimer = 0f;
        CancelAirDrawStrokeImmediate();
        handGestureDetector.Reset();
        secondaryHandGestureDetector.Reset();
        ResetTwoHandManipulationState();
        circleSummoner.Reset();
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

        if (mainCam != null)
        {
            stageController?.TickStageTransform(
                mainCam,
                0.016f,
                isAirWeavingActive,
                IsAnyUiOverlayOpen,
                isHandDetected,
                handGestureDetector.PalmCursorLocalXY,
                WorkbenchDepth);
            SyncAllWorkbenchNodesToCamera(mainCam, snapImmediately: true);
            UpdateWorkbenchGuideVisuals(mainCam);
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

        if (webCamTexture.didUpdateThisFrame && handLandmarker != null && webCamTexture.width > 16 && webCamTexture.height > 16)
        {
            ProcessCurrentWebCamFrame(mainCam);
        }
        else if (isHandDetected || isSecondaryHandDetected)
        {
            timeSinceLastValidHand += dt;
            if (timeSinceLastValidHand >= HandFrameStaleTimeoutSeconds)
            {
                isHandDetected = false;
                wasHandDetected = false;
                isSecondaryHandDetected = false;
                wasSecondaryHandDetected = false;
                rawPinching = false;
                isIndexOnlyWandPose = false;
                hoveredGateSlotIndex = -1;
                hoveredInputNode = null;
                inputADwellTimer = 0f;
                inputBDwellTimer = 0f;
                ResetTwoHandManipulationState();
                circleSummoner.ClearPoseVoteBuffer();
                ResetLandmarkOneEuroFilters();
                ResetSecondaryLandmarkOneEuroFilters();
                SetHandRigVisible(false);
                SetSecondaryHandRigVisible(false);
                SetHoverRingVisible(false);
            }
        }

        bool isFistStickyDrag = grabbedGateSlotIndex >= 0 || isTwoHandFreePanActive;
        bool isPinchStickyDrag = (cableRouter != null && cableRouter.IsDraggingCable) || isTwoHandOrbitZoomActive || isTwoHandFreePanActive;
        handGestureDetector.TickTemporalState(dt, isHandDetected, isFistStickyDrag, isPinchStickyDrag);
        secondaryHandGestureDetector.TickTemporalState(dt, isSecondaryHandDetected, isTwoHandFreePanActive, isTwoHandOrbitZoomActive || isTwoHandFreePanActive);
        UpdateTwoHandStageManipulation(mainCam, dt);
        if (stageController != null)
        {
            stageController.TickStageTransform(
                mainCam,
                dt,
                isAirWeavingActive,
                IsAnyUiOverlayOpen,
                isHandDetected,
                handGestureDetector.PalmCursorLocalXY,
                WorkbenchDepth);
        }
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
        UpdateHologramVisuals(mainCam);
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
        ShutdownNativeAndCameraImmediate();
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
        cachedHudMenu = null;
        circuitManager?.SetManualRoutingMode(false);
        RestoreAirWeavingLabelReadabilityStyles();
        ShutdownNativeAndCameraImmediate();
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

    private void OnApplicationQuit()
    {
        ShutdownNativeAndCameraImmediate();
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

        UpdateVideoQuadTransform(mainCam);
        SetHandRigVisible(false);
        SetSecondaryHandRigVisible(false);
        SetHoverRingVisible(false);
        outputTrigger?.HideImmediate();
        hologramVisual?.HideImmediate();
        cableRouter?.HideImmediate();
        circleSummoner.Reset();
        summonVisual?.HideImmediate();

        // 8. Tata seluruh 8 GateNode (Input A, Input B, Output terkunci di papan roti; 5 gerbang di Rak Parkir Atas)
        InitializeWorkbenchSlots(mainCam);
        ApplyAirWeavingLabelReadabilityStyles();

        isHandDetected = false;
        wasHandDetected = false;
        isSecondaryHandDetected = false;
        wasSecondaryHandDetected = false;
        rawPinching = false;
        isPinching = false;
        wasPinching = false;
        grabbedGateSlotIndex = -1;
        hoveredGateSlotIndex = -1;
        hoveredInputNode = null;
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
        ResetTwoHandManipulationState();
        lastTimestampMs = 0;
        fpsTimer = 0f;
        timeSinceLastValidHand = 0f;
        inferenceFramesInWindow = 0;
        displayedInferenceFps = 0;

        isAirWeavingActive = true;
        isTransitioning = false;
        transitionCoroutine = null;

        handGestureDetector.Reset();
        secondaryHandGestureDetector.Reset();
        circleSummoner.Reset();
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

        OnAirWeavingModeChanged?.Invoke(true);
        gestureGuide?.OnAirWeavingModeEntered();
        if (gestureGuide == null || !gestureGuide.IsGuideOverlayOpen)
        {
            SetFeedbackImmediate("Mode Udara aktif — Sentuh telunjuk: sakelar Input, Kepal: pegang gerbang, Cubit: tarik kabel.", FeedbackActiveColor, 2.8f);
        }
    }

    private IEnumerator ExitAirWeavingRoutine()
    {
        isTransitioning = true;
        isAirWeavingActive = false;
        gestureGuide?.OnAirWeavingModeExited();
        grabbedGateSlotIndex = -1;
        hoveredGateSlotIndex = -1;
        hoveredInputNode = null;
        cableRouter?.ClearAllWires(circuitManager);
        circuitManager?.SetManualRoutingMode(false);
        SetFeedbackImmediate("[Air-Weaving v6] Mengembalikan kamera ke Mode Kartu (v5)...", FeedbackWaitingColor);

        ShutdownNativeAndCameraImmediate();
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
        hoveredInputNode = null;
        cableRouter?.ClearAllWires(circuitManager);
        circuitManager?.SetManualRoutingMode(false);
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
        Camera mainCam = ResolveMainCamera();
        if (mainCam != null && hasSavedCameraState)
        {
            mainCam.clearFlags = savedCameraClearFlags;
            mainCam.backgroundColor = savedCameraBgColor;
        }

        RestoreAirWeavingLabelReadabilityStyles();
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
                minHandDetectionConfidence: 0.55f,
                minHandPresenceConfidence: 0.55f,
                minTrackingConfidence: 0.55f);

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

    private void ResetTwoHandManipulationState()
    {
        isTwoHandOrbitZoomActive = false;
        isTwoHandFreePanActive = false;
        twoHandFreePanConfirmTimer = 0f;
        isTwoHandFistResetCharging = false;
        twoHandFistResetLatched = false;
        twoHandFistResetTimer = 0f;
        stageController?.SetTwoHandManipulationState(false);
    }

    private void CancelAccidentalRecentPrimaryGateGrab()
    {
        if (grabbedGateSlotIndex >= 0
            && grabbedGateSlotIndex < gateSlots.Count
            && (Time.unscaledTime - lastPrimaryGrabStartTime) <= 0.18f)
        {
            WorkbenchGateSlot accidentalGrab = gateSlots[grabbedGateSlotIndex];
            if (accidentalGrab != null)
            {
                accidentalGrab.isPlacedOnWorkbench = lastPrimaryGrabWasPlacedBeforeGrab;
                accidentalGrab.placedLocalXY = lastPrimaryGrabPrevPlacedXY;
                accidentalGrab.currentLocalXY = lastPrimaryGrabPrevPlacedXY;
                accidentalGrab.ResetSpringState();
            }
            grabbedGateSlotIndex = -1;
        }
    }

    /// <summary>
    /// Kontrol 2 Tangan Full Free-View 6-DoF ala Iron Man (Zero-GC):
    /// - Both Hands Pinch (Cubit 2 Tangan): Air-Orbit 360° (pitch bebas -80°..+85°) &amp; Focal-Point Air-Zoom (0.42x..2.85x menuju midpoint 2 tangan).
    /// - 1 Hand Pinch + 1 Hand Fist (1 Cubit + 1 Kepal): Free Air-Pan 3D (geser meja bebas X/Y &amp; dorong/tarik kedalaman Z mengikuti rata-rata PalmSpan).
    /// - Both Hands Fist (Kepal 2 Tangan ~0.55 dtk): Reset pan, sudut, &amp; skala meja hologram kembali ke posisi bawaan.
    /// </summary>
    private void UpdateTwoHandStageManipulation(Camera mainCam, float dt)
    {
        bool overlayOrVerify = IsAnyUiOverlayOpen || (levelManager != null && levelManager.IsVerifying);
        bool cableBusy = cableRouter != null && cableRouter.IsDraggingCable;
        bool twoHandsPresent = isHandDetected && isSecondaryHandDetected && !overlayOrVerify && !cableBusy;

        if (!twoHandsPresent)
        {
            isTwoHandOrbitZoomActive = false;
            isTwoHandFreePanActive = false;
            twoHandFreePanConfirmTimer = 0f;
            isTwoHandFistResetCharging = false;
            twoHandFistResetTimer = 0f;
            twoHandFistResetLatched = false;
            stageController?.SetTwoHandManipulationState(false);
            return;
        }

        // 1. Mode Air-Orbit 360° (-80°..+85°), 3D Differential-Depth Twist, & Focal-Point Air-Zoom (0.42x..2.85x): Both Hands Pinch
        // Prioritaskan Both Pinch saat aktif atau kedua tangan di bawah ambang cubit/pra-cubit agar 3 jari lain yang ikut menekuk
        // tidak secara tidak sengaja memicu garis emas Free Air-Pan (1 Pinch + 1 Fist).
        bool primaryPinchIntent = handGestureDetector.IsPinchActive
                                  || handGestureDetector.IsPrePinchSuppressingSummon
                                  || handGestureDetector.PinchRatio < TwoHandPrePinchPriorityRatio;
        bool secondaryPinchIntent = secondaryHandGestureDetector.IsPinchActive
                                    || secondaryHandGestureDetector.IsPrePinchSuppressingSummon
                                    || secondaryHandGestureDetector.PinchRatio < TwoHandPrePinchPriorityRatio;
        bool primaryPinchHold = primaryPinchIntent || handGestureDetector.PinchRatio < TwoHandOrbitPinchHoldRatio;
        bool secondaryPinchHold = secondaryPinchIntent || secondaryHandGestureDetector.PinchRatio < TwoHandOrbitPinchHoldRatio;

        bool bothPinchCandidate = (handGestureDetector.IsPinchActive && secondaryHandGestureDetector.IsPinchActive)
                                  || (isTwoHandOrbitZoomActive && primaryPinchHold && secondaryPinchHold)
                                  || ((handGestureDetector.IsPinchActive || secondaryHandGestureDetector.IsPinchActive)
                                      && primaryPinchIntent
                                      && secondaryPinchIntent);
        bool bothPinching = bothPinchCandidate && grabbedGateSlotIndex < 0;
        if (bothPinching)
        {
            isTwoHandFreePanActive = false;
            twoHandFreePanConfirmTimer = 0f;
            isTwoHandFistResetCharging = false;
            twoHandFistResetTimer = 0f;
            if (isRecordingAirDraw)
            {
                CancelAirDrawStrokeImmediate();
            }
            if (circleSummoner.IsActive)
            {
                circleSummoner.Reset();
            }

            Vector2 p0 = handGestureDetector.PinchPointLocalXY;
            Vector2 p1 = secondaryHandGestureDetector.PinchPointLocalXY;
            float curDist = Mathf.Max(0.08f, Vector2.Distance(p0, p1));
            Vector2 curMid = (p0 + p1) * 0.5f;

            // Urutkan ukuran telapak terfilter berdasarkan posisi X layar (kiri vs kanan) agar bebas dari urutan slot 0/1
            float span0 = Mathf.Max(0.055f, handGestureDetector.FilteredPalmSpan);
            float span1 = Mathf.Max(0.055f, secondaryHandGestureDetector.FilteredPalmSpan);
            float leftScreenSpan = p0.x <= p1.x ? span0 : span1;
            float rightScreenSpan = p0.x <= p1.x ? span1 : span0;

            if (!isTwoHandOrbitZoomActive)
            {
                isTwoHandOrbitZoomActive = true;
                twoHandPinchInitialDist = Mathf.Max(0.14f, curDist);
                twoHandPinchInitialZoom = stageController != null ? stageController.TargetZoomScale : 1f;
                twoHandPinchPrevMidpointXY = curMid;
                stageController?.BeginTwoHandOrbitZoomAnchor(leftScreenSpan, rightScreenSpan);
            }
            else
            {
                Vector2 midDelta = curMid - twoHandPinchPrevMidpointXY;
                twoHandPinchPrevMidpointXY = curMid;

                float yawDelta = -midDelta.x * TwoHandOrbitYawDegPerUnit;
                float pitchDelta = -midDelta.y * TwoHandOrbitPitchDegPerUnit;
                float rawZoomRatio = curDist / twoHandPinchInitialDist;
                float desiredZoom = twoHandPinchInitialZoom * Mathf.Pow(rawZoomRatio, TwoHandZoomSensitivity);
                stageController?.ApplyTwoHandOrbitAndZoom(
                    yawDelta,
                    pitchDelta,
                    desiredZoom,
                    curMid,
                    leftScreenSpan,
                    rightScreenSpan);
            }
            return;
        }

        if (isTwoHandOrbitZoomActive)
        {
            isTwoHandOrbitZoomActive = false;
        }

        // 2. Mode Free Air-Pan 3D (X/Y/Z): 1 Tangan Pinch + 1 Tangan Fist murni (bukan IsPinchActive/pra-cubit) stabil >= 160 ms
        bool primaryPureFist = handGestureDetector.IsFistActive
                               && !handGestureDetector.IsPinchActive
                               && !handGestureDetector.IsPrePinchSuppressingSummon;
        bool secondaryPureFist = secondaryHandGestureDetector.IsFistActive
                                 && !secondaryHandGestureDetector.IsPinchActive
                                 && !secondaryHandGestureDetector.IsPrePinchSuppressingSummon;
        bool primaryPinchSecondaryFist = handGestureDetector.IsPinchActive && secondaryPureFist;
        bool primaryFistSecondaryPinch = primaryPureFist && secondaryHandGestureDetector.IsPinchActive;
        bool validFreePanPose = (primaryPinchSecondaryFist || primaryFistSecondaryPinch)
                                && !(primaryPinchIntent && secondaryPinchIntent);
        if (validFreePanPose)
        {
            if (!isTwoHandFreePanActive)
            {
                twoHandFreePanConfirmTimer += dt;
                if (twoHandFreePanConfirmTimer < TwoHandFreePanConfirmHoldSeconds)
                {
                    isTwoHandFistResetCharging = false;
                    twoHandFistResetTimer = 0f;
                    stageController?.SetTwoHandManipulationState(false);
                    return;
                }
            }

            CancelAccidentalRecentPrimaryGateGrab();
            if (grabbedGateSlotIndex < 0)
            {
                isTwoHandFistResetCharging = false;
                twoHandFistResetTimer = 0f;
                if (isRecordingAirDraw)
                {
                    CancelAirDrawStrokeImmediate();
                }
                if (circleSummoner.IsActive)
                {
                    circleSummoner.Reset();
                }

                Vector2 handA = primaryPinchSecondaryFist
                    ? handGestureDetector.PinchPointLocalXY
                    : handGestureDetector.PalmCursorLocalXY;
                Vector2 handB = primaryPinchSecondaryFist
                    ? secondaryHandGestureDetector.PalmCursorLocalXY
                    : secondaryHandGestureDetector.PinchPointLocalXY;
                Vector2 curMid = (handA + handB) * 0.5f;
                float curAvgSpan = Mathf.Max(
                    0.055f,
                    0.5f * (handGestureDetector.FilteredPalmSpan + secondaryHandGestureDetector.FilteredPalmSpan));

                if (!isTwoHandFreePanActive)
                {
                    isTwoHandFreePanActive = true;
                    twoHandPanPrevMidpointXY = curMid;
                    twoHandPanInitialAvgSpan = curAvgSpan;
                    twoHandPanInitialPanZ = stageController != null ? stageController.TargetPanOffset.z : 0f;
                    stageController?.SetTwoHandManipulationState(true);
                }
                else
                {
                    Vector2 panDeltaXY = (curMid - twoHandPanPrevMidpointXY) * TwoHandFreePanXYGain;
                    twoHandPanPrevMidpointXY = curMid;

                    // Kamera depan: dorong kedua tangan maju ke arah layar (curAvgSpan > initial) -> meja terdorong masuk lebih dalam (+Z)
                    float spanRatio = Mathf.Pow(curAvgSpan / Mathf.Max(0.055f, twoHandPanInitialAvgSpan), TwoHandFreePanDepthGain);
                    float desiredPanZ = twoHandPanInitialPanZ + ((spanRatio - 1.0f) * TwoHandFreePanDepthSpanMeters);
                    stageController?.ApplyTwoHandFreePan(panDeltaXY, desiredPanZ);
                }
                return;
            }
        }
        else
        {
            twoHandFreePanConfirmTimer = 0f;
        }

        if (isTwoHandFreePanActive)
        {
            isTwoHandFreePanActive = false;
        }

        // 3. Mode Reset Panggung Meja: Both Hands Fist (~0.55 dtk)
        bool bothFists = handGestureDetector.IsFistActive && secondaryHandGestureDetector.IsFistActive;
        if (bothFists)
        {
            CancelAccidentalRecentPrimaryGateGrab();

            if (grabbedGateSlotIndex < 0 && !twoHandFistResetLatched)
            {
                isTwoHandFistResetCharging = true;
                stageController?.SetTwoHandManipulationState(true);
                twoHandFistResetTimer += dt;
                if (twoHandFistResetTimer >= TwoHandFistResetHoldSeconds)
                {
                    twoHandFistResetTimer = 0f;
                    isTwoHandFistResetCharging = false;
                    twoHandFistResetLatched = true;
                    stageController?.ResetViewToDefault(immediate: false);
                    if (spatialSfx != null && mainCam != null)
                    {
                        Vector3 midWorld = (handGestureDetector.PalmCursorWorldPosition + secondaryHandGestureDetector.PalmCursorWorldPosition) * 0.5f;
                        spatialSfx.PlayOutputHoldConfirm(midWorld);
                    }
                    SetFeedbackImmediate("Posisi, sudut, & skala meja hologram 3D direset ke bawaan.", FeedbackActiveColor, 2.0f);
                }
                return;
            }
        }
        else
        {
            isTwoHandFistResetCharging = false;
            twoHandFistResetTimer = 0f;
            if (!handGestureDetector.IsFistActive || !secondaryHandGestureDetector.IsFistActive)
            {
                twoHandFistResetLatched = false;
            }
        }

        stageController?.SetTwoHandManipulationState(false);
    }

    private void UpdateWorkbenchInteractions(Camera mainCam, float dt)
    {
        float aspectScale = ComputeHorizontalAspectScale(mainCam);

        // 0. Jeda seluruh gestur tangan 1-kanal selama laci menu / modal UI / kartu panduan terbuka atau mode 2 tangan aktif
        if (IsAnyUiOverlayOpen || isTwoHandOrbitZoomActive || isTwoHandFreePanActive || isTwoHandFistResetCharging || twoHandFistResetLatched)
        {
            if (IsAnyUiOverlayOpen && grabbedGateSlotIndex >= 0 && grabbedGateSlotIndex < gateSlots.Count)
            {
                ReleaseGrabbedGate(gateSlots[grabbedGateSlotIndex], aspectScale);
                grabbedGateSlotIndex = -1;
            }
            CancelAirDrawStrokeImmediate();
            hoveredGateSlotIndex = -1;
            hoveredInputNode = null;
            inputADwellTimer = 0f;
            inputBDwellTimer = 0f;
            inputSwitchMagnetOffsetXY = Vector2.zero;
            isPinching = false;
            wasPinching = false;
            SetHoverRingVisible(false);
            return;
        }

        // 1. Kelola seret badan gerbang 3D nyata (Mode Meja Catur 3D) memakai status Kepal (Fist) + Rigid Palm Span Depth Tracking
        bool fistActive = handGestureDetector.IsFistActive;
        bool fistJustStarted = handGestureDetector.FistJustStarted;
        Vector2 palmXY = handGestureDetector.PalmCursorLocalXY;
        Vector2 aimXY = fistJustStarted ? handGestureDetector.StabilizedCursorLocalXY : palmXY;
        Vector3 palmWorldPos = handGestureDetector.PalmCursorWorldPosition;
        Vector3 aimWorldPos = mainCam != null
            ? mainCam.transform.TransformPoint(new Vector3(aimXY.x, aimXY.y, WorkbenchDepth - 0.14f))
            : palmWorldPos;

        if (grabbedGateSlotIndex >= 0 && grabbedGateSlotIndex < gateSlots.Count)
        {
            if (isRecordingAirDraw)
            {
                CancelAirDrawStrokeImmediate();
            }

            WorkbenchGateSlot grabbed = gateSlots[grabbedGateSlotIndex];
            if (fistActive)
            {
                isPinching = true;
                float liftedTargetHeightY = grabbed.currentLocalZ;

                if (isHandDetected && mainCam != null)
                {
                    Transform camT = mainCam.transform;
                    Vector3 camPos = camT.position;
                    Vector3 handRayDir = palmWorldPos - camPos;
                    if (handRayDir.sqrMagnitude < 0.00001f)
                    {
                        handRayDir = camT.forward;
                    }
                    else
                    {
                        handRayDir.Normalize();
                    }

                    // Hitung target jarak kamera 3D nyata khusus kamera depan (Front-Cam Mirror Depth):
                    // Tangan didorong maju ke arah layar (PalmSpan membesar) -> targetCamDepth bertambah (objek terdorong masuk ke dalam meja)
                    float currentFilteredPalmSpan = Mathf.Max(0.055f, handGestureDetector.FilteredPalmSpan);
                    float spanDepthRatio = Mathf.Pow(currentFilteredPalmSpan / Mathf.Max(0.055f, grabStartPalmSpan), FistGrabDepthGain);
                    float targetCamDepth = Mathf.Clamp(grabStartCameraDepth * spanDepthRatio, MinGrabCameraDepth, MaxGrabCameraDepth);
                    Vector3 targetWorldPos = camPos + (handRayDir * targetCamDepth);

                    Transform stageRoot = stageController != null ? stageController.StageRoot : null;
                    Vector2 targetStageXZ;
                    if (stageRoot != null)
                    {
                        Vector3 stageLocal3D = stageRoot.InverseTransformPoint(targetWorldPos);
                        targetStageXZ = new Vector2(stageLocal3D.x, stageLocal3D.z);
                        float defaultLiftedY = ComputeArenaBaseHoverHeightY(targetStageXZ, aspectScale, grabbed.slotIndex) + GrabExtraLiftHeight;
                        // Padukan posisi tinggi 3D langsung dari ray tangan dengan angkatan dasar di atas permukaan meja (localY >= 0.08f)
                        liftedTargetHeightY = Mathf.Clamp(Mathf.Max(MinGrabTableLocalY, Mathf.Lerp(defaultLiftedY, stageLocal3D.y, 0.72f)), MinGrabTableLocalY, MaxGrabTableLocalY);
                    }
                    else
                    {
                        targetStageXZ = palmXY;
                        liftedTargetHeightY = ComputeArenaBaseHoverHeightY(targetStageXZ, aspectScale, grabbed.slotIndex) + GrabExtraLiftHeight;
                    }

                    Vector2 clampedTarget = ClampDraggedGateLocalXY(targetStageXZ, aspectScale);
                    grabbed.currentLocalXY = StepCriticallyDampedSpring2D(
                        grabbed.currentLocalXY,
                        clampedTarget,
                        ref grabbed.velocityXY,
                        GateDragSpringOmega,
                        dt);
                }
                else
                {
                    grabbed.currentLocalXY = StepCriticallyDampedSpring2D(
                        grabbed.currentLocalXY,
                        grabbed.currentLocalXY,
                        ref grabbed.velocityXY,
                        GateDragSpringOmega,
                        dt);
                }

                // Velocity banking tilt 2-sumbu (maju-mundur & kiri-kanan mengikuti kecepatan di atas meja, maks ±24°)
                float velSmoothAlpha = 1f - Mathf.Exp(-GateDragVelocitySmoothRate * Mathf.Max(0.0001f, dt));
                grabbed.smoothedVelocityXY = Vector2.Lerp(grabbed.smoothedVelocityXY, grabbed.velocityXY, velSmoothAlpha);
                Vector2 targetTilt = new Vector2(
                    Mathf.Clamp(grabbed.velocityXY.y * DragVelocityTiltDegPerUnitSec, -MaxDragInertiaTiltDeg, MaxDragInertiaTiltDeg),
                    Mathf.Clamp(grabbed.velocityXY.x * DragVelocityTiltDegPerUnitSec, -MaxDragInertiaTiltDeg, MaxDragInertiaTiltDeg));
                grabbed.currentInertiaTiltDeg = StepCriticallyDampedSpring2D(
                    grabbed.currentInertiaTiltDeg,
                    targetTilt,
                    ref grabbed.velocityTiltDeg,
                    GateSettleSpringOmega,
                    dt);

                grabbed.currentLocalZ = StepCriticallyDampedSpring(
                    grabbed.currentLocalZ,
                    liftedTargetHeightY,
                    ref grabbed.velocityZ,
                    GateLiftZSpringOmega,
                    dt);
                grabbed.currentScale = StepCriticallyDampedSpring(
                    grabbed.currentScale,
                    GrabbedGateScale,
                    ref grabbed.velocityScale,
                    GateScaleSpringOmega,
                    dt);
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
            }

            hoveredGateSlotIndex = grabbedGateSlotIndex;
            hoveredInputNode = null;
            inputADwellTimer = 0f;
            inputBDwellTimer = 0f;
            DecayInputSwitchMagnetOffset(dt);
            wasPinching = isPinching;
            SetHoverRingVisible(false);
            return;
        }

        // 2. Tidak sedang memegang gerbang: sinkronkan status Kepal
        isPinching = fistActive;

        if (!isHandDetected)
        {
            if (isRecordingAirDraw)
            {
                CompleteOrCancelAirDrawStroke(mainCam, aspectScale);
            }
            hoveredGateSlotIndex = -1;
            hoveredInputNode = null;
            inputADwellTimer = 0f;
            inputBDwellTimer = 0f;
            inputSwitchMagnetOffsetXY = Vector2.zero;
            inputARequiresExitRearm = false;
            inputBRequiresExitRearm = false;
            wasPinching = false;
            SetHoverRingVisible(false);
            return;
        }

        // 2B. Prioritas Drag Kabel (Pinch aktif) & Summon Aktif: badan gerbang & Input A/B mengalah
        // (Hover port tanpa cubit TIDAK memblokir Fist maupun sentuh telunjuk sakelar Input)
        if ((cableRouter != null && cableRouter.IsDraggingCable) || handGestureDetector.IsPinchActive || circleSummoner.IsActive)
        {
            if (isRecordingAirDraw)
            {
                CancelAirDrawStrokeImmediate();
            }
            hoveredGateSlotIndex = -1;
            hoveredInputNode = null;
            inputADwellTimer = 0f;
            inputBDwellTimer = 0f;
            DecayInputSwitchMagnetOffset(dt);
            wasPinching = isPinching;
            SetHoverRingVisible(false);
            return;
        }

        // 3. Zona badan Output menang prioritas atas hover gerbang di dekatnya saat tidak sedang menggenggam
        bool cursorInOutputZone = IsCursorInsideOutputPriorityZone(mainCam, aimWorldPos, aspectScale);
        if (cursorInOutputZone)
        {
            if (isRecordingAirDraw)
            {
                CancelAirDrawStrokeImmediate();
            }
            hoveredGateSlotIndex = -1;
        }
        else
        {
            hoveredGateSlotIndex = FindNearestGateSlotIndex(mainCam, aimWorldPos, GateHoverGrabRadius, ParkedGateHoverRadius);
        }

        // 4. Sentuh ringan ujung telunjuk (#8) pada sakelar Input A & Input B (dwell 75 ms + exit-to-rearm 1.35x + cooldown 0.40 dtk)
        hoveredInputNode = null;
        if (hoveredGateSlotIndex < 0)
        {
            HandleInputIndexTouch(mainCam, aspectScale, dt);
        }
        else
        {
            inputADwellTimer = 0f;
            inputBDwellTimer = 0f;
            DecayInputSwitchMagnetOffset(dt);
        }

        // 5. Mulai genggam badan gerbang pada rising-edge Open -> Fist (disertai Elastic Scale Pop A=0.10 & pegas Y/skala)
        if (fistJustStarted && hoveredGateSlotIndex >= 0 && hoveredGateSlotIndex < gateSlots.Count)
        {
            CancelAirDrawStrokeImmediate();
            grabbedGateSlotIndex = hoveredGateSlotIndex;

            WorkbenchGateSlot grabbed = gateSlots[grabbedGateSlotIndex];
            lastPrimaryGrabStartTime = Time.unscaledTime;
            lastPrimaryGrabWasPlacedBeforeGrab = grabbed.isPlacedOnWorkbench;
            lastPrimaryGrabPrevPlacedXY = grabbed.placedLocalXY;
            grabStartPalmSpan = Mathf.Max(0.055f, handGestureDetector.FilteredPalmSpan);
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
        else if (EnableLegacyAirDraw)
        {
            // Air-Draw lama dicadangkan nonaktif di Fase 3 (siap dikembangkan ulang di Fase 4)
            UpdateAirDrawInteraction(mainCam, aspectScale, dt);
        }

        wasPinching = isPinching;
        SetHoverRingVisible(false);
    }

    private bool IsCursorInsideOutputPriorityZone(Camera mainCam, Vector3 cursorWorldPos, float aspectScale)
    {
        if (grabbedGateSlotIndex >= 0 || postDropLockoutTimer > 0f)
        {
            return false;
        }
        if ((cableRouter != null && cableRouter.IsDraggingCable) || handGestureDetector.IsPinchActive)
        {
            return false;
        }
        // Hold-to-verify dinonaktifkan: zona Output hanya memprioritaskan tahan-kepal setelah rangkaian live benar-benar LULUS 4/4
        if (levelManager == null || !levelManager.IsReadyForNextLevel || !isLiveCircuitPassedAllFour || levelManager.IsSandboxMode || levelManager.IsVerifying || IsAnyUiOverlayOpen)
        {
            return false;
        }

        float zoomScale = stageController != null ? Mathf.Clamp(stageController.CurrentZoomScale, 0.55f, 2.05f) : 1f;
        Vector2 cursorRayXY = ProjectWorldToCameraRayPlane(mainCam, cursorWorldPos);
        Vector2 outputCenterRayXY = outputNode != null
            ? (ProjectWorldToCameraRayPlane(mainCam, outputNode.transform.position) + new Vector2(0.03f * zoomScale, 0.10f * zoomScale))
            : new Vector2((OutputTerminalBaseX * aspectScale) + 0.03f, OutputTerminalY + 0.10f);
        return Vector2.Distance(cursorRayXY, outputCenterRayXY) <= (OutputZonePriorityRadius * zoomScale);
    }

    private void UpdateAirDrawInteraction(Camera mainCam, float aspectScale, float dt)
    {
        bool blockedByHigherPriority = isPinching
                                       || rawPinching
                                       || hoveredGateSlotIndex >= 0
                                       || hoveredInputNode != null
                                       || IsCursorInsideOutputPriorityZone(mainCam, indexTipWorldPosition, aspectScale)
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
            Vector2 clampedAssembly = ClampAssemblyZoneLocalXY(slot.currentLocalXY, aspectScale);
            slot.isPlacedOnWorkbench = true;
            slot.placedLocalXY = clampedAssembly;
            if (throwVel.sqrMagnitude > GateCoastStopSpeed * GateCoastStopSpeed)
            {
                slot.coastVelocityXY = throwVel;
                slot.isCoasting = true;
                slot.velocityXY = throwVel;
            }
            else
            {
                slot.coastVelocityXY = Vector2.zero;
                slot.isCoasting = false;
            }
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
        inputADwellTimer = 0f;
        inputBDwellTimer = 0f;
        inputARequiresExitRearm = false;
        inputBRequiresExitRearm = false;
        inputATouchRippleTimer = 0f;
        inputBTouchRippleTimer = 0f;
        inputSwitchMagnetOffsetXY = Vector2.zero;
        inputAScalePop.Reset();
        inputBScalePop.Reset();
    }

    private void DecayInputSwitchMagnetOffset(float dt)
    {
        float blendAlpha = 1f - Mathf.Exp(-SoftMagnetOffsetBlendSpeed * Mathf.Max(0.0001f, dt));
        inputSwitchMagnetOffsetXY = Vector2.Lerp(inputSwitchMagnetOffsetXY, Vector2.zero, blendAlpha);
    }

    private bool IsTipInsideInputSwitchZone(Camera mainCam, Vector3 tipWorldPos, float aspectScale)
    {
        float zoomScale = stageController != null ? Mathf.Clamp(stageController.CurrentZoomScale, 0.55f, 2.05f) : 1f;
        Vector2 tipRayXY = ProjectWorldToCameraRayPlane(mainCam, tipWorldPos);
        Vector2 inputARayXY = inputANode != null
            ? (ProjectWorldToCameraRayPlane(mainCam, inputANode.transform.position) - new Vector2(0.04f * zoomScale, 0f))
            : new Vector2((InputTerminalBaseX * aspectScale) - 0.04f, InputATerminalY);
        Vector2 inputBRayXY = inputBNode != null
            ? (ProjectWorldToCameraRayPlane(mainCam, inputBNode.transform.position) - new Vector2(0.04f * zoomScale, 0f))
            : new Vector2((InputTerminalBaseX * aspectScale) - 0.04f, InputBTerminalY);
        float rearmRadius = InputAirTapRearmRadius * zoomScale;
        return Vector2.Distance(tipRayXY, inputARayXY) <= rearmRadius
               || Vector2.Distance(tipRayXY, inputBRayXY) <= rearmRadius;
    }

    /// <summary>
    /// Sentuh ringan ujung telunjuk (#8) pada sakelar Input A &amp; Input B berbasis proyeksi sinar 3D:
    /// Soft Magnetic Aim-Assist (R=0.30, maxPull=0.45, blend 18/s) + dwell singkat 75 ms + exit-to-rearm berbasis posisi jari mentah (1.35x radius) + cooldown 0.40 dtk. Zero-GC.
    /// </summary>
    private void HandleInputIndexTouch(Camera mainCam, float aspectScale, float dt)
    {
        if (levelManager != null && levelManager.IsVerifying)
        {
            inputADwellTimer = 0f;
            inputBDwellTimer = 0f;
            DecayInputSwitchMagnetOffset(dt);
            return;
        }

        float zoomScale = stageController != null ? Mathf.Clamp(stageController.CurrentZoomScale, 0.55f, 2.05f) : 1f;
        Vector2 rawTipXY = ProjectWorldToCameraRayPlane(mainCam, indexTipWorldPosition);
        Vector2 inputAXY = inputANode != null
            ? (ProjectWorldToCameraRayPlane(mainCam, inputANode.transform.position) - new Vector2(0.04f * zoomScale, 0f))
            : new Vector2((InputTerminalBaseX * aspectScale) - 0.04f, InputATerminalY);
        Vector2 inputBXY = inputBNode != null
            ? (ProjectWorldToCameraRayPlane(mainCam, inputBNode.transform.position) - new Vector2(0.04f * zoomScale, 0f))
            : new Vector2((InputTerminalBaseX * aspectScale) - 0.04f, InputBTerminalY);

        float rawDistA = Vector2.Distance(rawTipXY, inputAXY);
        float rawDistB = Vector2.Distance(rawTipXY, inputBXY);
        float scaledRearmRadius = InputAirTapRearmRadius * zoomScale;
        float scaledMagnetRadius = InputSwitchMagnetRadius * zoomScale;
        float scaledTapRadius = InputAirTapRadius * zoomScale;

        // Logika Exit-to-Rearm WAJIB memakai posisi ujung jari mentah (rawDistA/rawDistB) agar tidak pernah nyangkut oleh magnet
        if (inputARequiresExitRearm && rawDistA > scaledRearmRadius)
        {
            inputARequiresExitRearm = false;
        }
        if (inputBRequiresExitRearm && rawDistB > scaledRearmRadius)
        {
            inputBRequiresExitRearm = false;
        }

        // Soft Magnetic Aim-Assist menuju sakelar Input A/B terdekat (R = 0.30, maxPull = 0.45, blend 18/s, 0 saat > 1.1 u/s)
        Vector2 desiredMagnetOffset = Vector2.zero;
        float tipSpeed = handGestureDetector.IndexTipSpeedUnitsPerSec;
        if (tipSpeed < SoftMagnetMaxHandSpeed && scaledMagnetRadius > 0.0001f)
        {
            bool canPullA = inputANode != null && rawDistA < scaledMagnetRadius;
            bool canPullB = inputBNode != null && rawDistB < scaledMagnetRadius;
            if (canPullA || canPullB)
            {
                bool pickA = canPullA && (!canPullB || rawDistA <= rawDistB);
                Vector2 targetXY = pickA ? inputAXY : inputBXY;
                float d = pickA ? rawDistA : rawDistB;
                float slowFactor = Mathf.Clamp01(1f - (tipSpeed / SoftMagnetMaxHandSpeed));
                float pull = InputSwitchMagnetMaxPull * slowFactor * (1f - (d / scaledMagnetRadius));
                desiredMagnetOffset = (targetXY - rawTipXY) * pull;
            }
        }

        float blendAlpha = 1f - Mathf.Exp(-SoftMagnetOffsetBlendSpeed * Mathf.Max(0.0001f, dt));
        inputSwitchMagnetOffsetXY = Vector2.Lerp(inputSwitchMagnetOffsetXY, desiredMagnetOffset, blendAlpha);

        Vector2 assistedTipXY = rawTipXY + inputSwitchMagnetOffsetXY;
        float distA = Vector2.Distance(assistedTipXY, inputAXY);
        float distB = Vector2.Distance(assistedTipXY, inputBXY);

        bool inA = distA <= scaledTapRadius && inputANode != null;
        bool inB = distB <= scaledTapRadius && inputBNode != null;

        if (inA && (!inB || distA <= distB))
        {
            hoveredInputNode = inputANode;
            inputBDwellTimer = Mathf.Max(0f, inputBDwellTimer - (dt * 4f));
            if (!inputARequiresExitRearm && inputATapCooldown <= 0f)
            {
                inputADwellTimer += dt;
                if (inputADwellTimer >= InputAirTapDwellSeconds)
                {
                    inputADwellTimer = 0f;
                    inputARequiresExitRearm = true;
                    inputATapCooldown = InputAirTapCooldownSeconds;
                    inputATouchRippleTimer = InputTouchRippleDuration;
                    ToggleInputTerminalNode(isInputB: false);
                }
            }
            else
            {
                inputADwellTimer = 0f;
            }
        }
        else if (inB)
        {
            hoveredInputNode = inputBNode;
            inputADwellTimer = Mathf.Max(0f, inputADwellTimer - (dt * 4f));
            if (!inputBRequiresExitRearm && inputBTapCooldown <= 0f)
            {
                inputBDwellTimer += dt;
                if (inputBDwellTimer >= InputAirTapDwellSeconds)
                {
                    inputBDwellTimer = 0f;
                    inputBRequiresExitRearm = true;
                    inputBTapCooldown = InputAirTapCooldownSeconds;
                    inputBTouchRippleTimer = InputTouchRippleDuration;
                    ToggleInputTerminalNode(isInputB: true);
                }
            }
            else
            {
                inputBDwellTimer = 0f;
            }
        }
        else
        {
            inputADwellTimer = Mathf.Max(0f, inputADwellTimer - (dt * 4f));
            inputBDwellTimer = Mathf.Max(0f, inputBDwellTimer - (dt * 4f));
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

    private static float ComputeArenaBaseHoverHeightY(Vector2 stageLocalXZ, float aspectScale, int slotIndex = 2)
    {
        float halfSpanX = Mathf.Max(0.35f, 0.76f * aspectScale);
        float normX = Mathf.Clamp(stageLocalXZ.x / halfSpanX, -1.25f, 1.25f);
        float normZ = Mathf.Clamp((stageLocalXZ.y + 0.16f) / 0.65f, -1.2f, 1.2f);
        float slotStagger = (Mathf.Clamp(slotIndex, 0, 4) - 2) * 0.011f;
        return BaseHoverHeight + (ArenaArcHeightY * normX * normX) + (0.016f * normZ) + slotStagger;
    }

    private static float ComputeArenaArcYawDeg(float stageLocalX, float aspectScale)
    {
        float halfSpanX = Mathf.Max(0.35f, 0.76f * aspectScale);
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

        // Pengaman nearClipPlane: cegah mesh terpotong lensa kamera saat deep zoom (2.85x) atau gerbang ditarik sangat dekat
        if (camT != null)
        {
            Vector3 camToNode = worldPos - camT.position;
            float forwardDepth = Vector3.Dot(camToNode, camT.forward);
            float minSafeDepth = (cachedMainCamera != null ? cachedMainCamera.nearClipPlane : 0.15f) + MinNearClipSafeOffsetMeters;
            if (forwardDepth < minSafeDepth)
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

        UpdateAirWeavingLabelsAndTruthPodSpatialPose(mainCam);
    }

    private int FindNearestGateSlotIndex(Camera mainCam, Vector3 cursorWorldPos, float placedMaxRadius, float parkedMaxRadius)
    {
        int bestIdx = -1;
        float bestDist = float.MaxValue;
        float zoomScale = stageController != null ? Mathf.Clamp(stageController.CurrentZoomScale, 0.55f, 2.05f) : 1f;
        Vector2 cursorRayXY = ProjectWorldToCameraRayPlane(mainCam, cursorWorldPos);

        for (int i = 0; i < gateSlots.Count; i++)
        {
            WorkbenchGateSlot slot = gateSlots[i];
            if (slot == null || slot.node == null)
            {
                continue;
            }

            float limit = (slot.isPlacedOnWorkbench ? placedMaxRadius : parkedMaxRadius) * zoomScale;
            Vector2 gateRayXY = ProjectWorldToCameraRayPlane(mainCam, slot.node.transform.position);
            float dist = Vector2.Distance(cursorRayXY, gateRayXY);
            if (dist < limit && dist < bestDist)
            {
                bestDist = dist;
                bestIdx = i;
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
        float minX = (InputTerminalBaseX - 0.18f) * aspectScale;
        float maxX = (OutputTerminalBaseX + 0.18f) * aspectScale;
        return new Vector2(
            Mathf.Clamp(rawXY.x, minX, maxX),
            Mathf.Clamp(rawXY.y, AssemblyMinY - 0.65f, ParkingRackY + 0.45f));
    }

    /// <summary>
    /// Mengecek apakah posisi gerbang yang dipegang sudah keluar dari batas visual kotak AssemblyZone
    /// (sisi atas, bawah, kiri, maupun kanan).
    /// </summary>
    private static bool IsOutsideAssemblyZone(Vector2 localXY, float aspectScale)
    {
        float leftX = (AssemblyMinBaseX - 0.08f) * aspectScale;
        float rightX = (AssemblyMaxBaseX + 0.08f) * aspectScale;
        float botY = AssemblyMinY - 0.06f;
        float topY = AssemblyMaxY + 0.06f;
        return localXY.x < leftX || localXY.x > rightX || localXY.y < botY || localXY.y > topY;
    }

    private static Vector2 ClampAssemblyZoneLocalXY(Vector2 rawXY, float aspectScale)
    {
        return new Vector2(
            Mathf.Clamp(rawXY.x, AssemblyMinBaseX * aspectScale, AssemblyMaxBaseX * aspectScale),
            Mathf.Clamp(rawXY.y, AssemblyMinY, AssemblyMaxY));
    }

    // =========================================================================
    // PER-FRAME INFERENCE, 1:1 COORDINATE MAPPING, SMOOTHING, & PINCH RATIO
    // =========================================================================

    private void ProcessCurrentWebCamFrame(Camera mainCam)
    {
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

        EnsurePixelBuffersAllocated(camW, camH, targetW, targetH);

        webCamTexture.GetPixels32(rawWebcamPixels);
        FillUprightMirroredRgbaBuffer(camW, camH, targetW, targetH, rotAngle, vMirror, mirrorHorizontalDisplay);

        long timestampMs = (long)(Time.realtimeSinceStartupAsDouble * 1000.0);
        if (timestampMs <= lastTimestampMs)
        {
            timestampMs = lastTimestampMs + 1;
        }
        lastTimestampMs = timestampMs;

        bool detected = false;
        try
        {
            using (Image mpImage = new Image(
                ImageFormat.Types.Format.Srgba,
                targetW,
                targetH,
                targetW * 4,
                nativeWorkPixels))
            {
                detected = handLandmarker.TryDetectForVideo(mpImage, timestampMs, zeroRotationOptions, ref cachedResult);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AirWeavingController] Inferensi frame dilewati: {ex.Message}");
            detected = false;
        }

        inferenceFramesInWindow++;

        int validHandCount = 0;
        if (detected && cachedResult.handLandmarks != null)
        {
            for (int h = 0; h < cachedResult.handLandmarks.Count && validHandCount < 2; h++)
            {
                if (cachedResult.handLandmarks[h].landmarks != null && cachedResult.handLandmarks[h].landmarks.Count >= LandmarkCount)
                {
                    validHandCount++;
                }
            }
        }

        if (validHandCount <= 0)
        {
            isHandDetected = false;
            wasHandDetected = false;
            isSecondaryHandDetected = false;
            wasSecondaryHandDetected = false;
            rawPinching = false;
            isIndexOnlyWandPose = false;
            hoveredGateSlotIndex = -1;
            hoveredInputNode = null;
            ResetTwoHandManipulationState();
            circleSummoner.ClearPoseVoteBuffer();
            ResetLandmarkOneEuroFilters();
            ResetSecondaryLandmarkOneEuroFilters();
            SetHandRigVisible(false);
            SetSecondaryHandRigVisible(false);
            SetHoverRingVisible(false);
            return;
        }

        // Nearest-Palm Tracking Assignment agar tangan primer (slot 0) dan sekunder (slot 1) tidak saling bertukar antar-frame
        int primarySourceIndex = 0;
        int secondarySourceIndex = -1;

        if (validHandCount >= 2)
        {
            List<NormalizedLandmark> hand0 = cachedResult.handLandmarks[0].landmarks;
            List<NormalizedLandmark> hand1 = cachedResult.handLandmarks[1].landmarks;
            Vector2 palm0 = ComputeNormalizedPalmCenter(hand0);
            Vector2 palm1 = ComputeNormalizedPalmCenter(hand1);

            if (wasHandDetected)
            {
                if (wasSecondaryHandDetected)
                {
                    float keepCost = (palm0 - lastPrimaryPalmNormXY).sqrMagnitude + (palm1 - lastSecondaryPalmNormXY).sqrMagnitude;
                    float swapCost = (palm1 - lastPrimaryPalmNormXY).sqrMagnitude + (palm0 - lastSecondaryPalmNormXY).sqrMagnitude;
                    if (swapCost < keepCost)
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
                else
                {
                    float dist0 = (palm0 - lastPrimaryPalmNormXY).sqrMagnitude;
                    float dist1 = (palm1 - lastPrimaryPalmNormXY).sqrMagnitude;
                    if (dist1 < dist0)
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
            }
            else
            {
                primarySourceIndex = 0;
                secondarySourceIndex = 1;
            }

            lastPrimaryPalmNormXY = primarySourceIndex == 0 ? palm0 : palm1;
            lastSecondaryPalmNormXY = secondarySourceIndex == 0 ? palm0 : palm1;
        }
        else
        {
            List<NormalizedLandmark> singleHand = cachedResult.handLandmarks[0].landmarks;
            lastPrimaryPalmNormXY = ComputeNormalizedPalmCenter(singleHand);
        }

        float sampleDt = (lastLandmarkFilterTimestampMs > 0 && timestampMs > lastLandmarkFilterTimestampMs)
            ? Mathf.Clamp((timestampMs - lastLandmarkFilterTimestampMs) * 0.001f, OneEuroMinDtSeconds, OneEuroMaxDtSeconds)
            : Mathf.Clamp(Time.unscaledDeltaTime, OneEuroMinDtSeconds, OneEuroMaxDtSeconds);
        lastLandmarkFilterTimestampMs = timestampMs;

        List<NormalizedLandmark> primaryLandmarks = cachedResult.handLandmarks[primarySourceIndex].landmarks;
        UpdateHandVisualsFromLandmarks(primaryLandmarks, mainCam, sampleDt);

        if (secondarySourceIndex >= 0)
        {
            List<NormalizedLandmark> secondaryLandmarks = cachedResult.handLandmarks[secondarySourceIndex].landmarks;
            UpdateSecondaryHandVisualsFromLandmarks(secondaryLandmarks, mainCam, sampleDt);
        }
        else if (isSecondaryHandDetected || wasSecondaryHandDetected)
        {
            isSecondaryHandDetected = false;
            wasSecondaryHandDetected = false;
            ResetSecondaryLandmarkOneEuroFilters();
            SetSecondaryHandRigVisible(false);
        }
    }

    private void EnsurePixelBuffersAllocated(int camW, int camH, int targetW, int targetH)
    {
        if (rawWebcamPixels == null || lastCamWidth != camW || lastCamHeight != camH)
        {
            rawWebcamPixels = new Color32[camW * camH];
            lastCamWidth = camW;
            lastCamHeight = camH;
        }

        int requiredBytes = targetW * targetH * 4;
        if (!nativeWorkPixels.IsCreated || workWidth != targetW || workHeight != targetH || nativeWorkPixels.Length != requiredBytes)
        {
            if (nativeWorkPixels.IsCreated)
            {
                nativeWorkPixels.Dispose();
            }

            nativeWorkPixels = new NativeArray<byte>(requiredBytes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            workWidth = targetW;
            workHeight = targetH;
        }
    }

    private void FillUprightMirroredRgbaBuffer(
        int srcW,
        int srcH,
        int dstW,
        int dstH,
        int rotAngle,
        bool vMirror,
        bool hMirror)
    {
        float invDstW = 1f / dstW;
        float invDstH = 1f / dstH;
        int maxSrcX = srcW - 1;
        int maxSrcY = srcH - 1;
        int dstIdx = 0;

        for (int dy = 0; dy < dstH; dy++)
        {
            float ry = 0.5f - ((dy + 0.5f) * invDstH);
            for (int dx = 0; dx < dstW; dx++)
            {
                float uScreen = (dx + 0.5f) * invDstW;
                float rx = hMirror ? (0.5f - uScreen) : (uScreen - 0.5f);

                float lx;
                float ly;
                switch (rotAngle)
                {
                    case 90:
                        lx = -ry;
                        ly = rx;
                        break;
                    case 180:
                        lx = -rx;
                        ly = -ry;
                        break;
                    case 270:
                        lx = ry;
                        ly = -rx;
                        break;
                    default:
                        lx = rx;
                        ly = ry;
                        break;
                }

                float u = lx + 0.5f;
                float v = (vMirror ? -ly : ly) + 0.5f;

                int px = (int)(u * srcW);
                if (px < 0) px = 0;
                else if (px > maxSrcX) px = maxSrcX;

                int py = (int)(v * srcH);
                if (py < 0) py = 0;
                else if (py > maxSrcY) py = maxSrcY;

                Color32 c = rawWebcamPixels[(py * srcW) + px];
                nativeWorkPixels[dstIdx] = c.r;
                nativeWorkPixels[dstIdx + 1] = c.g;
                nativeWorkPixels[dstIdx + 2] = c.b;
                nativeWorkPixels[dstIdx + 3] = 255;
                dstIdx += 4;
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
        for (int i = 0; i < LandmarkCount; i++)
        {
            landmarkOneEuroFilters[i].Reset();
        }
        lastLandmarkFilterTimestampMs = 0;
    }

    private void ResetSecondaryLandmarkOneEuroFilters()
    {
        for (int i = 0; i < LandmarkCount; i++)
        {
            secondaryLandmarkOneEuroFilters[i].Reset();
        }
    }

    private void UpdateHandVisualsFromLandmarks(List<NormalizedLandmark> landmarks, Camera mainCam, float sampleDt)
    {
        Transform camTransform = mainCam.transform;
        float camAspect = (mainCam != null && mainCam.aspect > 0.1f) ? mainCam.aspect : (16f / 9f);

        bool justAppeared = !wasHandDetected;
        if (justAppeared)
        {
            for (int i = 0; i < LandmarkCount; i++)
            {
                landmarkOneEuroFilters[i].Reset();
            }
        }

        // Perbarui Rigid Metacarpal Palm Span lebih dulu (Front-Cam Mirror Depth: dorong maju -> PalmSpan membesar -> rig masuk +Z)
        float filteredPalmSpan = handGestureDetector.UpdateRigidPalmSpanOnly(landmarks, sampleDt, camAspect);
        float handDepthScale = Mathf.Pow(
            Mathf.Max(0.055f, filteredPalmSpan) / AirWeavingHandGestureDetector.DefaultComfortPalmSpan,
            HandCursorDepthTrackGain);
        float baseHandCamDepth = Mathf.Clamp((WorkbenchDepth - 0.14f) * handDepthScale, MinHandRigCameraDepth, MaxHandRigCameraDepth);

        for (int i = 0; i < LandmarkCount; i++)
        {
            NormalizedLandmark lm = landmarks[i];
            float filteredXNorm = landmarkOneEuroFilters[i].x.Filter(
                lm.x * OneEuroCoordScale,
                sampleDt,
                OneEuroMinCutoff,
                OneEuroBeta,
                OneEuroDCutoff) / OneEuroCoordScale;
            float filteredYNorm = landmarkOneEuroFilters[i].y.Filter(
                lm.y * OneEuroCoordScale,
                sampleDt,
                OneEuroMinCutoff,
                OneEuroBeta,
                OneEuroDCutoff) / OneEuroCoordScale;
            float filteredZ = landmarkOneEuroFilters[i].z.Filter(
                Mathf.Clamp(lm.z, -0.25f, 0.25f) * OneEuroCoordScale,
                sampleDt,
                OneEuroZMinCutoff,
                OneEuroZBeta,
                OneEuroDCutoff) / OneEuroCoordScale;

            float localZ = Mathf.Clamp(baseHandCamDepth + (filteredZ * DepthParallaxScale), 0.75f, 6.20f);
            float rayDepthRatio = localZ / VideoQuadDepth;
            float localX = (filteredXNorm - 0.5f) * currentVideoDisplayWidth * rayDepthRatio;
            float localY = (0.5f - filteredYNorm) * currentVideoDisplayHeight * rayDepthRatio;
            smoothedLandmarksWorld[i] = camTransform.TransformPoint(new Vector3(localX, localY, localZ));
        }

        isHandDetected = true;
        wasHandDetected = true;
        timeSinceLastValidHand = 0f;

        // Scale-Invariant Pinch Ratio: dist(#4, #8) / max(0.06, dist(#0, #9))
        NormalizedLandmark wrist = landmarks[0];
        NormalizedLandmark thumbTip = landmarks[4];
        NormalizedLandmark indexTip = landmarks[8];
        NormalizedLandmark middleMcp = landmarks[9];

        float palmDx = wrist.x - middleMcp.x;
        float palmDy = wrist.y - middleMcp.y;
        float palmScale = Mathf.Max(MinPalmScaleClamp, Mathf.Sqrt((palmDx * palmDx) + (palmDy * palmDy)));

        float pinchDx = thumbTip.x - indexTip.x;
        float pinchDy = thumbTip.y - indexTip.y;
        float pinchDist = Mathf.Sqrt((pinchDx * pinchDx) + (pinchDy * pinchDy));
        currentPinchRatio = pinchDist / palmScale;

        if (!rawPinching && currentPinchRatio < PinchRatioActivateThreshold)
        {
            rawPinching = true;
        }
        else if (rawPinching && currentPinchRatio > PinchRatioReleaseThreshold)
        {
            rawPinching = false;
        }

        // Deteksi pose "Tongkat Telunjuk" (Index-Only Wand): telunjuk terentang & minimal 2 dari 3 jari lain tertekuk
        isIndexOnlyWandPose = EvaluateIndexOnlyWandPose(landmarks);

        // Perbarui detektor Kepal/Buka & Kursor Telapak Anti-Lompat (v7.0 Fase 3 + Fase 4 + Tahap 4.7 3D Depth)
        handGestureDetector.UpdateFromLandmarks(landmarks, smoothedLandmarksWorld, camTransform, camAspect);
        circleSummoner.RecordPoseSample(handGestureDetector.CurrentSummonPose);

        indexTipWorldPosition = smoothedLandmarksWorld[8];
        pinchWorldPosition = handGestureDetector.PalmCursorWorldPosition;

        // Simpan koordinat XY lokal di bidang meja kerja
        Vector3 indexLocal = camTransform.InverseTransformPoint(indexTipWorldPosition);
        indexWorkbenchLocalXY = new Vector2(indexLocal.x, indexLocal.y);
        pinchWorkbenchLocalXY = handGestureDetector.PalmCursorLocalXY;

        SetHandRigVisible(true);
        if (pinchCursorRingRenderer != null && pinchCursorRingRenderer.enabled)
        {
            pinchCursorRingRenderer.enabled = false;
        }
        if (indexTrailRenderer != null && indexTrailRenderer.emitting)
        {
            indexTrailRenderer.emitting = false;
            indexTrailRenderer.Clear();
        }

        if (jointSphereObjs != null)
        {
            for (int i = 0; i < LandmarkCount; i++)
            {
                if (jointSphereObjs[i] != null)
                {
                    jointSphereObjs[i].transform.position = smoothedLandmarksWorld[i];
                }
            }
        }

        if (boneLineRenderers != null)
        {
            int boneCount = HandBoneConnections.GetLength(0);
            for (int b = 0; b < boneCount; b++)
            {
                LineRenderer lr = boneLineRenderers[b];
                if (lr != null)
                {
                    lr.SetPosition(0, smoothedLandmarksWorld[HandBoneConnections[b, 0]]);
                    lr.SetPosition(1, smoothedLandmarksWorld[HandBoneConnections[b, 1]]);
                }
            }
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

    private void UpdateSecondaryHandVisualsFromLandmarks(List<NormalizedLandmark> landmarks, Camera mainCam, float sampleDt)
    {
        Transform camTransform = mainCam.transform;
        float camAspect = (mainCam != null && mainCam.aspect > 0.1f) ? mainCam.aspect : (16f / 9f);

        if (!wasSecondaryHandDetected)
        {
            ResetSecondaryLandmarkOneEuroFilters();
        }

        float secFilteredSpan = secondaryHandGestureDetector.UpdateRigidPalmSpanOnly(landmarks, sampleDt, camAspect);
        float secDepthScale = Mathf.Pow(
            Mathf.Max(0.055f, secFilteredSpan) / AirWeavingHandGestureDetector.DefaultComfortPalmSpan,
            HandCursorDepthTrackGain);
        float baseSecCamDepth = Mathf.Clamp((WorkbenchDepth - 0.14f) * secDepthScale, MinHandRigCameraDepth, MaxHandRigCameraDepth);

        for (int i = 0; i < LandmarkCount; i++)
        {
            NormalizedLandmark lm = landmarks[i];
            float filteredXNorm = secondaryLandmarkOneEuroFilters[i].x.Filter(
                lm.x * OneEuroCoordScale,
                sampleDt,
                OneEuroMinCutoff,
                OneEuroBeta,
                OneEuroDCutoff) / OneEuroCoordScale;
            float filteredYNorm = secondaryLandmarkOneEuroFilters[i].y.Filter(
                lm.y * OneEuroCoordScale,
                sampleDt,
                OneEuroMinCutoff,
                OneEuroBeta,
                OneEuroDCutoff) / OneEuroCoordScale;
            float filteredZ = secondaryLandmarkOneEuroFilters[i].z.Filter(
                Mathf.Clamp(lm.z, -0.25f, 0.25f) * OneEuroCoordScale,
                sampleDt,
                OneEuroZMinCutoff,
                OneEuroZBeta,
                OneEuroDCutoff) / OneEuroCoordScale;

            float localZ = Mathf.Clamp(baseSecCamDepth + (filteredZ * DepthParallaxScale), 0.75f, 6.20f);
            float rayDepthRatio = localZ / VideoQuadDepth;
            float localX = (filteredXNorm - 0.5f) * currentVideoDisplayWidth * rayDepthRatio;
            float localY = (0.5f - filteredYNorm) * currentVideoDisplayHeight * rayDepthRatio;
            secondarySmoothedLandmarksWorld[i] = camTransform.TransformPoint(new Vector3(localX, localY, localZ));
        }

        isSecondaryHandDetected = true;
        wasSecondaryHandDetected = true;
        timeSinceLastValidHand = 0f;

        secondaryHandGestureDetector.UpdateFromLandmarks(landmarks, secondarySmoothedLandmarksWorld, camTransform, camAspect);
        SetSecondaryHandRigVisible(true);

        if (secondaryJointSphereObjs != null)
        {
            for (int i = 0; i < LandmarkCount; i++)
            {
                if (secondaryJointSphereObjs[i] != null)
                {
                    secondaryJointSphereObjs[i].transform.position = secondarySmoothedLandmarksWorld[i];
                }
            }
        }

        if (secondaryBoneLineRenderers != null)
        {
            int boneCount = HandBoneConnections.GetLength(0);
            for (int b = 0; b < boneCount; b++)
            {
                LineRenderer lr = secondaryBoneLineRenderers[b];
                if (lr != null)
                {
                    lr.SetPosition(0, secondarySmoothedLandmarksWorld[HandBoneConnections[b, 0]]);
                    lr.SetPosition(1, secondarySmoothedLandmarksWorld[HandBoneConnections[b, 1]]);
                }
            }
        }
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
        if (pinchCursorRingRenderer == null)
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
        if (isHandDetected)
        {
            if (!wasHandDetectedForWakeSfx
                && handAbsentForWakeSeconds >= HologramWakeMinAbsentSeconds
                && !overlayOrVerify
                && spatialSfx != null)
            {
                spatialSfx.PlayHologramWake(handGestureDetector.PalmCursorWorldPosition);
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
        if (!isAirWeavingActive || !isHandDetected || overlayOrVerify || mainCam == null)
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
    /// Memicu micro-tick elektronik ringan tepat 1x per transisi target hover baru (orb port, badan gerbang, atau sakelar Input A/B).
    /// Dilengkapi hysteresis lepas 90 ms & jeda pasca-aksi agar tidak spam saat tangan diam atau melepas objek/kabel. Zero-GC.
    /// </summary>
    private void UpdateHoverSfxFeedback(float dt)
    {
        if (hoverSfxPostActionSuppressTimer > 0f)
        {
            hoverSfxPostActionSuppressTimer = Mathf.Max(0f, hoverSfxPostActionSuppressTimer - dt);
        }

        bool overlayOrVerify = IsAnyUiOverlayOpen || (levelManager != null && levelManager.IsVerifying);
        bool busyGesture = grabbedGateSlotIndex >= 0
                           || (cableRouter != null && cableRouter.IsDraggingCable)
                           || handGestureDetector.IsFistActive
                           || handGestureDetector.IsPinchActive
                           || circleSummoner.IsActive;

        if (!isAirWeavingActive || !isHandDetected || overlayOrVerify)
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

        if (cableRouter != null && cableRouter.TryGetHoveredPortInfo(out GateNode portNode, out bool portIsOutput, out int portSlot, out Vector3 portWorldPos))
        {
            curCategory = HoverSfxCategory.PortOrb;
            curNode = portNode;
            curSubId = (portIsOutput ? 1 : 0) | ((portSlot & 3) << 1);
            curWorldPos = portWorldPos;
        }
        else if (hoveredInputNode != null)
        {
            curCategory = HoverSfxCategory.InputSwitch;
            curNode = hoveredInputNode;
            curSubId = hoveredInputNode == inputBNode ? 2 : 1;
            curWorldPos = hoveredInputNode.transform.position;
        }
        else if (hoveredGateSlotIndex >= 0
                 && hoveredGateSlotIndex < gateSlots.Count
                 && gateSlots[hoveredGateSlotIndex] != null
                 && gateSlots[hoveredGateSlotIndex].node != null)
        {
            curCategory = HoverSfxCategory.GateBody;
            curNode = gateSlots[hoveredGateSlotIndex].node;
            curSubId = gateSlots[hoveredGateSlotIndex].slotIndex + 1;
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

    private void UpdateCableRouterInteraction(Camera mainCam)
    {
        if (cableRouter == null) return;

        activePlacedGatesBuffer.Clear();
        for (int i = 0; i < gateSlots.Count; i++)
        {
            WorkbenchGateSlot slot = gateSlots[i];
            if (slot != null && slot.isPlacedOnWorkbench && slot.node != null && slot.node.isTracked)
            {
                activePlacedGatesBuffer.Add(slot.node);
            }
        }

        bool twoHandBusy = isTwoHandOrbitZoomActive || isTwoHandFreePanActive || isTwoHandFistResetCharging || twoHandFistResetLatched;
        bool blockedCable = grabbedGateSlotIndex >= 0 || circleSummoner.IsActive || handGestureDetector.IsFistActive || twoHandBusy;
        Transform stageRoot = stageController != null ? stageController.StageRoot : null;
        float stageZoom = stageController != null ? stageController.CurrentZoomScale : 1f;
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
            isHandDetected,
            handGestureDetector.IsPinchActive,
            handGestureDetector.PinchJustStarted,
            handGestureDetector.PinchJustEnded,
            handGestureDetector.StabilizedPinchLocalXY,
            handGestureDetector.PinchPointLocalXY,
            handGestureDetector.PinchPointWorldPosition,
            Time.unscaledDeltaTime,
            handGestureDetector.PinchSpeedUnitsPerSec,
            handGestureDetector.PinchRatio,
            handGestureDetector.IsPinchReleaseIntent,
            stageRoot,
            stageZoom);
    }

    private void SyncCableRouterVisualsAfterPoseUpdate(Camera mainCam)
    {
        if (cableRouter == null || mainCam == null) return;
        bool overlayOrVerify = IsAnyUiOverlayOpen || (levelManager != null && levelManager.IsVerifying);
        bool twoHandBusy = isTwoHandOrbitZoomActive || isTwoHandFreePanActive || isTwoHandFistResetCharging || twoHandFistResetLatched;
        bool blockedCable = overlayOrVerify || grabbedGateSlotIndex >= 0 || circleSummoner.IsActive || handGestureDetector.IsFistActive || twoHandBusy;
        Vector3 effectivePinchWorld = cableRouter.AssistedPinchWorldPosition != Vector3.zero
            ? cableRouter.AssistedPinchWorldPosition
            : handGestureDetector.PinchPointWorldPosition;
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
            isHandDetected && !blockedCable,
            effectivePinchWorld,
            stageRoot,
            stageZoom);
    }

    private void UpdateHologramVisuals(Camera mainCam)
    {
        if (hologramVisual == null || mainCam == null) return;
        Transform camT = mainCam.transform;
        bool overlayOpen = IsAnyUiOverlayOpen;
        bool isVerifying = levelManager != null && levelManager.IsVerifying;
        bool canShowHover = isHandDetected && !overlayOpen && !isVerifying;
        hologramVisual.SetRootVisible(isAirWeavingActive);

        Transform stageRoot = stageController != null ? stageController.StageRoot : null;
        GateNode portFocusedNode = (canShowHover && cableRouter != null) ? cableRouter.HoveredPortNode : null;
        hologramVisual.UpdateNodeFrame(0, camT, inputANode, true, false, false,
            (canShowHover && hoveredInputNode == inputANode) ? AirWeavingHologramVisual.NodeHoloState.BodyHovered : AirWeavingHologramVisual.NodeHoloState.Idle,
            stageRoot);
        hologramVisual.UpdateNodeFrame(1, camT, inputBNode, false, true, false,
            (canShowHover && hoveredInputNode == inputBNode) ? AirWeavingHologramVisual.NodeHoloState.BodyHovered : AirWeavingHologramVisual.NodeHoloState.Idle,
            stageRoot);
        hologramVisual.UpdateNodeFrame(2, camT, outputNode, false, false, true, AirWeavingHologramVisual.NodeHoloState.Idle, stageRoot);

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
            else if (canShowHover && portFocusedNode == slot.node)
            {
                state = AirWeavingHologramVisual.NodeHoloState.PortFocused;
            }
            else if (canShowHover && hoveredGateSlotIndex == i)
            {
                state = AirWeavingHologramVisual.NodeHoloState.BodyHovered;
            }
            else
            {
                state = slot.isPlacedOnWorkbench ? AirWeavingHologramVisual.NodeHoloState.Idle : AirWeavingHologramVisual.NodeHoloState.Parked;
            }

            hologramVisual.UpdateNodeFrame(3 + i, camT, slot.node, false, false, false, state, stageRoot);
        }

        bool draggingCable = cableRouter != null && cableRouter.IsDraggingCable;
        bool hoveringPort = cableRouter != null && cableRouter.IsHoveringPort;
        bool isHoveringOrGrabbingGate = isGrabbingGate || hoveredGateSlotIndex >= 0;
        bool twoHandVisualActive = !overlayOpen && !isVerifying && (isTwoHandOrbitZoomActive || isTwoHandFreePanActive || isTwoHandFistResetCharging);
        // Sembunyikan reticle telapak (+) saat menarik kabel, mode 2 tangan, atau saat sudah mengunci target hover/grab
        bool hidePalmReticle = overlayOpen
                               || isVerifying
                               || twoHandVisualActive
                               || draggingCable
                               || hoveringPort
                               || isHoveringOrGrabbingGate
                               || hoveredInputNode != null
                               || circleSummoner.IsActive;

        hologramVisual.UpdatePalmReticle(
            camT,
            isHandDetected,
            hidePalmReticle,
            handGestureDetector.PalmCursorWorldPosition,
            handGestureDetector.IsFistActive,
            hoveringPort,
            handGestureDetector.MeanCurlRatio);

        bool showPinchRing = !overlayOpen
                             && !isVerifying
                             && !twoHandVisualActive
                             && !isGrabbingGate
                             && !handGestureDetector.IsFistActive
                             && !circleSummoner.IsActive
                             && (hoveringPort || draggingCable);
        Vector3 pinchVisualWorldPos = (cableRouter != null && cableRouter.AssistedPinchWorldPosition != Vector3.zero)
            ? cableRouter.AssistedPinchWorldPosition
            : handGestureDetector.PinchPointWorldPosition;
        hologramVisual.UpdatePinchIndicator(
            camT,
            isHandDetected,
            showPinchRing,
            pinchVisualWorldPos,
            handGestureDetector.IsPinchActive,
            cableRouter != null && cableRouter.IsSnappedTargetPort);

        Vector3 handAWorld = (isTwoHandFistResetCharging || (isTwoHandFreePanActive && handGestureDetector.IsFistActive))
            ? handGestureDetector.PalmCursorWorldPosition
            : handGestureDetector.PinchPointWorldPosition;
        Vector3 handBWorld = (isTwoHandFistResetCharging || (isTwoHandFreePanActive && secondaryHandGestureDetector.IsFistActive))
            ? secondaryHandGestureDetector.PalmCursorWorldPosition
            : secondaryHandGestureDetector.PinchPointWorldPosition;

        // Kunci titik jangkar visual cincin 2 tangan tepat pada bidang meja sejajar titik cubitan selama Both Pinch
        if (isTwoHandOrbitZoomActive && stageController != null && mainCam != null)
        {
            if (stageController.TryIntersectHandWithHoverPlane(mainCam, handAWorld, BaseHoverHeight, out _, out Vector3 anchorHitA))
            {
                float distA = Vector3.Distance(camT.position, anchorHitA);
                if (distA >= 1.0f && distA <= 6.4f)
                {
                    handAWorld = anchorHitA;
                }
            }
            if (stageController.TryIntersectHandWithHoverPlane(mainCam, handBWorld, BaseHoverHeight, out _, out Vector3 anchorHitB))
            {
                float distB = Vector3.Distance(camT.position, anchorHitB);
                if (distB >= 1.0f && distB <= 6.4f)
                {
                    handBWorld = anchorHitB;
                }
            }
        }
        float resetProgress = isTwoHandFistResetCharging
            ? Mathf.Clamp01(twoHandFistResetTimer / TwoHandFistResetHoldSeconds)
            : 0f;
        hologramVisual.UpdateTwoHandManipulatorLink(
            camT,
            twoHandVisualActive,
            handAWorld,
            handBWorld,
            isTwoHandFistResetCharging,
            resetProgress,
            isTwoHandFreePanActive);
    }

    private void UpdateCircleSummoner(Camera mainCam, float dt)
    {
        if (mainCam == null) return;
        float aspectScale = ComputeHorizontalAspectScale(mainCam);
        bool overlayOrVerify = IsAnyUiOverlayOpen || (levelManager != null && levelManager.IsVerifying);
        bool twoHandBusy = isTwoHandOrbitZoomActive || isTwoHandFreePanActive || isTwoHandFistResetCharging || twoHandFistResetLatched;
        bool draggingCable = cableRouter != null && cableRouter.IsDraggingCable;
        bool pinchBusy = handGestureDetector.IsPinchActive || handGestureDetector.IsPrePinchSuppressingSummon;
        bool blocked;
        if (circleSummoner.IsActive)
        {
            // Begitu sudah masuk Charging/Ready, jangan batalkan hanya karena ujung jari melintas di atas gerbang/port/Output/tepi papan
            blocked = overlayOrVerify || twoHandBusy || grabbedGateSlotIndex >= 0 || draggingCable || handGestureDetector.IsPinchActive;
        }
        else
        {
            bool cableBusy = draggingCable || pinchBusy || (cableRouter != null && cableRouter.IsHoveringPort);
            bool inOutputZone = IsCursorInsideOutputPriorityZone(mainCam, handGestureDetector.PalmCursorWorldPosition, aspectScale);
            bool tipNearInputSwitch = IsTipInsideInputSwitchZone(mainCam, indexTipWorldPosition, aspectScale);
            blocked = overlayOrVerify
                      || twoHandBusy
                      || grabbedGateSlotIndex >= 0
                      || cableBusy
                      || hoveredGateSlotIndex >= 0
                      || hoveredInputNode != null
                      || tipNearInputSwitch
                      || inOutputZone;
        }

        Vector2 assemblyMin = new Vector2(AssemblyMinBaseX * aspectScale, AssemblyMinY);
        Vector2 assemblyMax = new Vector2(AssemblyMaxBaseX * aspectScale, AssemblyMaxY);

        circleSummoner.Tick(
            dt,
            isAirWeavingActive,
            blocked,
            isHandDetected,
            handGestureDetector.IsFistActive,
            handGestureDetector.FistJustStarted,
            handGestureDetector.CurrentSummonPose,
            handGestureDetector.IsRelaxedOpenFiveBloom,
            handGestureDetector.ActiveSummonTipLocalXY,
            handGestureDetector.ThumbTipLocalXY,
            assemblyMin,
            assemblyMax);

        UpdateCircleSummonAudioFeedback(mainCam, overlayOrVerify);

        if (circleSummoner.BloomJustTriggeredThisFrame)
        {
            HandleCircleSummonBloomDeploy(mainCam, aspectScale, circleSummoner.DeployedGateType, circleSummoner.DeployedLocalXY);
        }
    }

    private void UpdateCircleSummonAudioFeedback(Camera mainCam, bool overlayOrVerify)
    {
        AirWeavingCircleSummoner.SummonStage curStage = circleSummoner.Stage;
        if (curStage != AirWeavingCircleSummoner.SummonStage.Idle && mainCam != null)
        {
            Vector2 centerXY = circleSummoner.PreviewCenterLocalXY;
            lastSfxSummonWorldPos = mainCam.transform.TransformPoint(new Vector3(centerXY.x, centerXY.y, WorkbenchDepth));
        }

        if (spatialSfx != null && !overlayOrVerify)
        {
            NodeType curGateType = circleSummoner.ActiveGateType;
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
                float prog = circleSummoner.ChargeProgress;
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
                bool cancelledByFist = circleSummoner.ConsumedFistCancelThisFrame;
                bool expiredFromReady = lastSfxSummonStage == AirWeavingCircleSummoner.SummonStage.Ready
                                        && !circleSummoner.BloomJustTriggeredThisFrame;
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

        Vector2 spawnXY = ClampAssemblyZoneLocalXY(stageDeployXZ, aspectScale);
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
        bool hasGateContext = circleSummoner.IsActive || circleSummoner.HasLivePoseIndicator;
        NodeType visualGate = circleSummoner.IsActive ? circleSummoner.ActiveGateType : circleSummoner.LiveIndicatorGateType;
        bool alreadyOnBoard = hasGateContext && IsGateTypePlacedOnWorkbench(visualGate);
        summonVisual.TickVisuals(mainCam, dt, isAirWeavingActive, WorkbenchDepth, circleSummoner, alreadyOnBoard);
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
        Vector2 palmRayXY = ProjectWorldToCameraRayPlane(mainCam, handGestureDetector.PalmCursorWorldPosition);
        float stageZoom = stageController != null ? stageController.CurrentZoomScale : 1f;

        OutputDebugGuidanceState guidanceState = EvaluateOutputDebugGuidance(outputWorldPos, out int passedRowCount);

        bool effectiveReadyForNext = isLiveCircuitPassedAllFour
                                     && levelManager != null
                                     && levelManager.IsReadyForNextLevel;

        Transform stageRoot = stageController != null ? stageController.StageRoot : null;
        outputTrigger.Tick(mainCam, dt, isAirWeavingActive,
            levelManager != null && levelManager.IsSandboxMode,
            levelManager != null && levelManager.IsVerifying,
            levelManager != null && levelManager.IsCircuitCompleteLive,
            effectiveReadyForNext,
            IsAnyUiOverlayOpen,
            isHandDetected, handGestureDetector.IsFistActive, palmRayXY,
            outputRayXY, outputWorldPos,
            grabbedGateSlotIndex >= 0, hoveredGateSlotIndex >= 0,
            handGestureDetector.IsPinchActive,
            cableRouter != null && cableRouter.IsDraggingCable,
            isRecordingAirDraw || circleSummoner.IsActive || circleSummoner.ConsumedFistCancelThisFrame,
            postDropLockoutTimer,
            guidanceState,
            passedRowCount,
            stageZoom,
            stageRoot,
            lastTruthPodWorldAnchor);

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

        // Verifikasi matematis penuh 4 baris graf live (Zero-GC) agar status LULUS 4/4 tidak pernah muncul pada rangkaian salah
        bool liveCircuitMatchesTruthTable = DoesLiveCircuitMatchExpectedTruthTable(currentLevel.expectedTruthTable);

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

        if (isHandDetected)
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

        // Sprites/Default (Cull Off, ZWrite Off) + renderQueue 1500: kebal backface culling saat mirror & selalu di belakang 3D
        videoQuadMaterial = CreateSafeSpritesMaterial(Color.white, 1500);
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

        float frustumH = 2f * VideoQuadDepth * Mathf.Tan(mainCam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float frustumW = frustumH * Mathf.Max(0.1f, mainCam.aspect);
        float videoAspect = isRotated90 ? ((float)camH / camW) : ((float)camW / camH);

        if (videoAspect >= mainCam.aspect)
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
        bool grabbedOutside = isGrabbingGate && IsOutsideAssemblyZone(grabbedXY, aspectScale);

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
            stageContactNodesBuffer.Length);
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

        // Skala sendi & garis tulang dibuat ramping & halus (~65% lebih kecil) agar tidak membebani visual papan kerja
        jointSphereObjs = new GameObject[LandmarkCount];
        for (int i = 0; i < LandmarkCount; i++)
        {
            bool isTip = (i == 4 || i == 8 || i == 12 || i == 16 || i == 20);
            bool isWrist = (i == 0);
            float diameter = isTip ? 0.022f : (isWrist ? 0.024f : 0.015f);

            GameObject jointObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            jointObj.name = $"Joint_{i}";
            jointObj.transform.SetParent(handRigRootObj.transform, false);
            jointObj.transform.localScale = Vector3.one * diameter;

            Collider col = jointObj.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            Renderer rend = jointObj.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
                rend.sharedMaterial = isTip ? jointTipMat : jointBaseMat;
            }

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

        // Rig 3D neon tangan ke-2 (slot sekunder) berbagi material yang sama (Zero-GC)
        secondaryHandRigRootObj = new GameObject("AR_AirWeavingSecondaryHandRig");
        secondaryHandRigRootObj.transform.SetParent(transform, false);
        secondaryJointSphereObjs = new GameObject[LandmarkCount];
        for (int i = 0; i < LandmarkCount; i++)
        {
            bool isTip = (i == 4 || i == 8 || i == 12 || i == 16 || i == 20);
            bool isWrist = (i == 0);
            float diameter = isTip ? 0.022f : (isWrist ? 0.024f : 0.015f);

            GameObject jointObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            jointObj.name = $"SecJoint_{i}";
            jointObj.transform.SetParent(secondaryHandRigRootObj.transform, false);
            jointObj.transform.localScale = Vector3.one * diameter;

            Collider col = jointObj.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            Renderer rend = jointObj.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
                rend.sharedMaterial = isTip ? jointTipMat : jointBaseMat;
            }

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
            if (boneMat != null)
            {
                lr.sharedMaterial = boneMat;
            }

            secondaryBoneLineRenderers[b] = lr;
        }

        secondaryHandRigRootObj.SetActive(false);
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
        rawPinching = false;
        isPinching = false;
        wasPinching = false;
        isIndexOnlyWandPose = false;
        CancelAirDrawStrokeImmediate();
        ResetLandmarkOneEuroFilters();
        ResetSecondaryLandmarkOneEuroFilters();
        ResetTwoHandManipulationState();

        SetHandRigVisible(false);
        SetSecondaryHandRigVisible(false);
        SetHoverRingVisible(false);
        stageController?.HideImmediate();
        outputTrigger?.HideImmediate();
        hologramVisual?.HideImmediate();
        cableRouter?.HideImmediate();
        circleSummoner.Reset();
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
        DisposeHandLandmarkerOnly();

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

        if (nativeWorkPixels.IsCreated)
        {
            nativeWorkPixels.Dispose();
        }
        rawWebcamPixels = null;
        workWidth = 0;
        workHeight = 0;
        lastCamWidth = 0;
        lastCamHeight = 0;

        if (protobufLogHandlerActive)
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
        float stageZoom = stageController != null ? stageController.CurrentZoomScale : 1f;
        float gentleZoom = Mathf.Lerp(1f, stageZoom, 0.22f);

        // 1. Distance-Compensated Scale Clamping untuk semua label 3D (KARTU, LAB, AND/OR/XOR/NAND/NOT) di seluruh rentang zoom 0.42x..2.85x
        for (int i = 0; i < savedNodeLabelStyles.Count; i++)
        {
            SavedNodeLabelStyle s = savedNodeLabelStyles[i];
            if (s.node == null || s.node.statusLabel == null)
            {
                continue;
            }

            Transform labelT = s.node.statusLabel.transform;
            labelT.rotation = camRot;

            float camDepth = Mathf.Max(0.75f, Vector3.Dot(labelT.position - camPos, camForward));
            float distRatio = Mathf.Clamp(camDepth / WorkbenchDepth, 0.22f, 1.48f);

            bool isLogicGate = s.node.nodeType != NodeType.InputSource && s.node.nodeType != NodeType.OutputActuator;
            bool isParkedGate = isLogicGate && !s.node.isTracked;
            float baseWorldLabelScale = isParkedGate ? 0.29f : (isLogicGate ? 0.39f : 0.41f);
            float desiredWorldLabelScale = baseWorldLabelScale * distRatio * gentleZoom;

            float parentScale = Mathf.Max(0.14f, s.node.transform.localScale.x);
            float clampedLocalScale = Mathf.Clamp(desiredWorldLabelScale / parentScale, 0.13f, 1.08f);
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

                float podCamDepth = Mathf.Max(0.85f, Vector3.Dot(basePodWorld - camPos, camForward));
                float podDistRatio = Mathf.Clamp(podCamDepth / WorkbenchDepth, 0.24f, 1.42f);

                Vector3 finalPodWorld = basePodWorld - (camUp * (0.100f * podDistRatio)) - (camForward * 0.24f);
                cachedOutputTruthPodRootTransform.position = finalPodWorld;
                cachedOutputTruthPodRootTransform.rotation = Quaternion.LookRotation(camUp, -camForward);

                float desiredPodWorldScale = 0.52f * podDistRatio * gentleZoom;
                float outParentScale = Mathf.Max(0.16f, outputNode.transform.localScale.x);
                float clampedPodLocalScale = Mathf.Clamp(desiredPodWorldScale / outParentScale, 0.22f, 1.05f);
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
