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

    // Geometri ruang kerja 3D (selaras skala 1:1 GateNode & ambang CircuitManager: maxSnap=3.0, inputReach=5.4)
    private const float VideoQuadDepth = 8.0f;
    private const float WorkbenchDepth = 3.60f;
    private const float GrabLiftOffsetZ = -0.28f;
    private const float DepthParallaxScale = 0.25f;
    private const float GateDragSmoothingSpeed = 16.0f;

    // One-Euro Filter adaptif untuk 21 landmark tangan (zero-GC, terkalibrasi untuk 20-30 FPS Android/webcam)
    // Ruang persen [0..100] agar minCutoff=1.6 Hz, beta=0.045, dCutoff=1.0 Hz tenang saat hover & responsif saat drag/Air-Draw
    private const float OneEuroMinCutoff = 1.6f;
    private const float OneEuroBeta = 0.045f;
    private const float OneEuroDCutoff = 1.0f;
    private const float OneEuroCoordScale = 100.0f;
    private const float OneEuroZMinCutoff = 2.2f;
    private const float OneEuroZBeta = 0.02f;
    private const float OneEuroMinDtSeconds = 0.005f;
    private const float OneEuroMaxDtSeconds = 0.200f;
    private const float TwoPi = 6.283185307f;

    // Skala hologram melayang Mode Udara v7.0 Fase 3 (~22% lebih ramping agar kamera lega)
    private const float TerminalScale = 0.70f;
    private const float OutputTerminalScale = 0.72f;
    private const float ActiveGateScale = 0.70f;
    private const float GrabbedGateScale = 0.75f;
    private const float ParkedGateScale = 0.44f;

    // Parameter kompatibilitas Pinch lama (disimpan untuk fallback/referensi)
    private const float MinPalmScaleClamp = 0.06f;
    private const float PinchRatioActivateThreshold = 0.34f;
    private const float PinchRatioReleaseThreshold = 0.46f;

    // Radius interaksi Badan Gerbang vs Input (bebas tumpang tindih dengan PortOutHitRadius = 0.165 pada AirWeavingCableRouter)
    private const float GateHoverGrabRadius = 0.135f;
    private const float ParkedGateHoverRadius = 0.210f;
    private const float InputAirTapRadius = 0.145f;
    private const float InputAirTapCooldownSeconds = 0.35f;
    private const float ParkReturnThresholdY = 0.28f;
    private const float ParkSlotSnapRadius = 0.30f;

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

    // Koordinat meja kerja terkalibrasi v7.0 Fase 3 (bebas bilah atas 72 px & beri jarak bersih antar orb port ±0.255)
    private const float InputTerminalBaseX = -1.26f;
    private const float InputATerminalY = -0.06f;
    private const float InputBTerminalY = -0.68f;
    private const float OutputTerminalBaseX = 1.28f;
    private const float OutputTerminalY = -0.30f;
    private const float ParkingRackY = 0.52f;
    private const float AssemblyMinBaseX = -0.62f;
    private const float AssemblyMaxBaseX = 0.66f;
    private const float AssemblyMinY = -0.74f;
    private const float AssemblyMaxY = 0.16f;

    private static readonly float[] ParkingSlotBaseX = { -0.64f, -0.22f, 0.20f, 0.62f, 1.04f };
    private static readonly NodeType[] ParkingSlotOrder =
    {
        NodeType.AND,
        NodeType.OR,
        NodeType.XOR,
        NodeType.NAND,
        NodeType.NOT
    };

    // Kemiringan ergonomis papan roti menghadap ARCamera (+X kanan, +Y atas-depan menampakkan siluet Quaternius & label 3D)
    private static readonly Quaternion WorkbenchTiltRotation = Quaternion.Euler(-38f, 0f, 0f);

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

    private static readonly Color NeonCyanBoneColor = new Color(0.14f, 0.96f, 1.00f, 0.42f);
    private static readonly Color NeonJointBaseColor = new Color(0.24f, 0.92f, 1.00f, 0.52f);
    private static readonly Color NeonJointTipColor = new Color(0.86f, 1.00f, 0.98f, 0.62f);
    private static readonly Color PinchActiveColor = new Color(0.76f, 1.00f, 0.28f, 1.00f);
    private static readonly Color PinchIdleColor = new Color(0.20f, 0.92f, 1.00f, 0.85f);
    private static readonly Color HoverGateRingColor = new Color(0.28f, 0.98f, 1.00f, 0.92f);
    private static readonly Color HoverGrabbedRingColor = new Color(1.00f, 0.88f, 0.26f, 0.96f);
    private static readonly Color HoverReturnRackColor = new Color(1.00f, 0.56f, 0.26f, 0.95f);
    private static readonly Color HoverInputRingColor = new Color(0.42f, 1.00f, 0.68f, 0.92f);
    private static readonly Color WorkbenchFrameColor = new Color(0.18f, 0.80f, 0.96f, 0.28f);
    private static readonly Color RackFrameColor = new Color(0.32f, 0.90f, 1.00f, 0.52f);
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

    private sealed class WorkbenchGateSlot
    {
        public GateNode node;
        public int slotIndex;
        public bool isPlacedOnWorkbench;
        public Vector2 placedLocalXY;
        public Vector2 currentLocalXY;
        public float currentLocalZ;
        public float currentScale;
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
    private bool rawPinching = false;
    private bool isPinching = false;
    private bool wasPinching = false;
    private bool mirrorHorizontalDisplay = true;

    private float currentPinchRatio = 1f;
    private float inputATapCooldown = 0f;
    private float inputBTapCooldown = 0f;

    private Vector3 pinchWorldPosition = Vector3.zero;
    private Vector3 indexTipWorldPosition = Vector3.zero;
    private Vector2 pinchWorkbenchLocalXY = Vector2.zero;
    private Vector2 indexWorkbenchLocalXY = Vector2.zero;
    private readonly Vector3[] smoothedLandmarksWorld = new Vector3[LandmarkCount];
    private readonly OneEuroLandmarkFilter[] landmarkOneEuroFilters = new OneEuroLandmarkFilter[LandmarkCount];
    private long lastLandmarkFilterTimestampMs = 0;
    private readonly Vector3[] pinchRingPoints = new Vector3[PinchRingSegments];
    private readonly Vector3[] hoverRingPoints = new Vector3[HoverRingSegments];
    private readonly Vector3[] workbenchFramePoints = new Vector3[5];
    private readonly Vector3[] rackLinePoints = new Vector3[2];

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
    private GameObject indexTrailObj;
    private TrailRenderer indexTrailRenderer;
    private LineRenderer pinchCursorRingRenderer;

    private GameObject workbenchVisualRootObj;
    private LineRenderer workbenchBorderRenderer;
    private LineRenderer rackDividerRenderer;
    private LineRenderer hoverHighlightRingRenderer;
    private LineRenderer airDrawStrokeRenderer;

    private readonly List<Material> ownedMaterials = new List<Material>(16);
    private Material jointBaseMat;
    private Material jointTipMat;
    private Material boneMat;
    private Material trailMat;
    private Material pinchRingMat;
    private Material hoverRingMat;
    private Material workbenchFrameMat;
    private Material rackFrameMat;
    private Material airDrawStrokeMat;

    private AirWeavingOutputTrigger outputTrigger;
    private AirWeavingHologramVisual hologramVisual;
    private AirWeavingCableRouter cableRouter;
    private AirWeavingSummonVisual summonVisual;
    private readonly AirWeavingHandGestureDetector handGestureDetector = new AirWeavingHandGestureDetector();
    private readonly AirWeavingCircleSummoner circleSummoner = new AirWeavingCircleSummoner();
    private readonly List<GateNode> activePlacedGatesBuffer = new List<GateNode>(5);
    private float fpsTimer = 0f;
    private int inferenceFramesInWindow = 0;
    private int displayedInferenceFps = 0;
    private readonly List<GateNode> cachedGateNodes = new List<GateNode>(12);
    private readonly List<Behaviour> pausedObserverHandlers = new List<Behaviour>(12);

    public bool IsAirWeavingActive => isAirWeavingActive;
    public bool IsTransitioning => isTransitioning;
    public bool IsHandDetected => isHandDetected;
    public bool IsPinching => isPinching;
    public int DisplayedInferenceFps => displayedInferenceFps;
    public Vector3 PinchWorldPosition => pinchWorldPosition;
    public Vector3 IndexTipWorldPosition => indexTipWorldPosition;

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
        circleSummoner.Reset();
        cableRouter?.ClearAllWires(circuitManager);
        summonVisual?.HideImmediate();
        outputTrigger?.HideImmediate();

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
            slot.currentLocalZ = WorkbenchDepth;
            slot.currentScale = ParkedGateScale;
        }

        if (mainCam != null)
        {
            SyncAllWorkbenchNodesToCamera(mainCam, snapImmediately: true);
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
        if (airDrawPostSpawnCooldown > 0f)
        {
            airDrawPostSpawnCooldown = Mathf.Max(0f, airDrawPostSpawnCooldown - dt);
        }
        if (postDropLockoutTimer > 0f) postDropLockoutTimer = Mathf.Max(0f, postDropLockoutTimer - dt);

        UpdateVideoQuadTransform(mainCam);
        UpdateWorkbenchGuideVisuals(mainCam);

        if (webCamTexture.didUpdateThisFrame && handLandmarker != null && webCamTexture.width > 16 && webCamTexture.height > 16)
        {
            ProcessCurrentWebCamFrame(mainCam);
        }

        bool isStickyDrag = (cableRouter != null && cableRouter.IsDraggingCable) || grabbedGateSlotIndex >= 0;
        handGestureDetector.TickTemporalState(dt, isHandDetected, isStickyDrag);
        UpdateCableRouterInteraction(mainCam);
        UpdateWorkbenchInteractions(mainCam, dt);
        UpdateCircleSummoner(mainCam, dt);
        UpdateAirDrawStrokeFade(mainCam, dt);
        SyncAllWorkbenchNodesToCamera(mainCam, snapImmediately: false);
        UpdateHologramVisuals(mainCam);
        UpdateCircleSummonVisuals(mainCam, dt);
        UpdateOutputSpatialTrigger(mainCam, dt);
        UpdateAirWeavingStatusFeedback();
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
        circuitManager?.SetManualRoutingMode(false);
        ShutdownNativeAndCameraImmediate();
        DestroyVisualRigAndMaterials();
        cachedGateNodes.Clear();
        pausedObserverHandlers.Clear();
        gateSlots.Clear();
        activePlacedGatesBuffer.Clear();
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

        UpdateVideoQuadTransform(mainCam);
        UpdateWorkbenchGuideVisuals(mainCam);
        SetHandRigVisible(false);
        SetHoverRingVisible(false);
        outputTrigger?.HideImmediate();
        hologramVisual?.HideImmediate();
        cableRouter?.HideImmediate();
        circleSummoner.Reset();
        summonVisual?.HideImmediate();

        // 8. Tata seluruh 8 GateNode (Input A, Input B, Output terkunci di papan roti; 5 gerbang di Rak Parkir Atas)
        InitializeWorkbenchSlots(mainCam);

        isHandDetected = false;
        wasHandDetected = false;
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
        lastTimestampMs = 0;
        fpsTimer = 0f;
        inferenceFramesInWindow = 0;
        displayedInferenceFps = 0;

        isAirWeavingActive = true;
        isTransitioning = false;
        transitionCoroutine = null;

        handGestureDetector.Reset();
        circleSummoner.Reset();
        circuitManager?.SetManualRoutingMode(true);
        cableRouter?.ClearAllWires(circuitManager);
        hologramVisual?.SetRootVisible(true);
        summonVisual?.HideImmediate();

        SyncAllWorkbenchNodesToCamera(mainCam, snapImmediately: true);

        SetFeedbackImmediate("Mode Udara aktif — arahkan telapak, Kepal untuk pegang/tarik kabel, Buka untuk lepas.", FeedbackActiveColor, 2.8f);
        OnAirWeavingModeChanged?.Invoke(true);
    }

    private IEnumerator ExitAirWeavingRoutine()
    {
        isTransitioning = true;
        isAirWeavingActive = false;
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
        grabbedGateSlotIndex = -1;
        hoveredGateSlotIndex = -1;
        hoveredInputNode = null;
        cableRouter?.ClearAllWires(circuitManager);
        circuitManager?.SetManualRoutingMode(false);
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
                numHands: 1,
                minHandDetectionConfidence: 0.55f,
                minHandPresenceConfidence: 0.55f,
                minTrackingConfidence: 0.55f);

            handLandmarker = HandLandmarker.CreateFromOptions(options);
            cachedResult = HandLandmarkerResult.Alloc(1);
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
                currentLocalZ = WorkbenchDepth,
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
        }
    }

    private void UpdateWorkbenchInteractions(Camera mainCam, float dt)
    {
        float aspectScale = ComputeHorizontalAspectScale(mainCam);

        // 0. Jeda seluruh gestur tangan selama laci menu / modal UI terbuka (Guard #2 v7.0)
        if (levelManager != null && levelManager.IsUiOverlayOpen)
        {
            if (grabbedGateSlotIndex >= 0 && grabbedGateSlotIndex < gateSlots.Count) { ReleaseGrabbedGate(gateSlots[grabbedGateSlotIndex], aspectScale); grabbedGateSlotIndex = -1; }
            CancelAirDrawStrokeImmediate();
            hoveredGateSlotIndex = -1;
            hoveredInputNode = null;
            isPinching = false;
            wasPinching = false;
            SetHoverRingVisible(false);
            return;
        }

        // 1. Kelola seret badan gerbang memakai status Kepal (Fist) ter-debounce + Occlusion Grace 180 ms
        bool fistActive = handGestureDetector.IsFistActive;
        bool fistJustStarted = handGestureDetector.FistJustStarted;
        Vector2 palmXY = handGestureDetector.PalmCursorLocalXY;
        Vector2 aimXY = fistJustStarted ? handGestureDetector.StabilizedCursorLocalXY : palmXY;

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

                if (isHandDetected)
                {
                    Vector2 clampedTarget = ClampDraggedGateLocalXY(palmXY, aspectScale);
                    float dragAlpha = 1f - Mathf.Exp(-GateDragSmoothingSpeed * dt);
                    grabbed.currentLocalXY = Vector2.Lerp(grabbed.currentLocalXY, clampedTarget, dragAlpha);
                }
                grabbed.currentLocalZ = Mathf.Lerp(grabbed.currentLocalZ, WorkbenchDepth + GrabLiftOffsetZ, 1f - Mathf.Exp(-GateDragSmoothingSpeed * dt));
                grabbed.currentScale = Mathf.Lerp(grabbed.currentScale, GrabbedGateScale, 1f - Mathf.Exp(-GateDragSmoothingSpeed * dt));
                grabbed.isPlacedOnWorkbench = true;
            }
            else
            {
                isPinching = false;
                ReleaseGrabbedGate(grabbed, aspectScale);
                grabbedGateSlotIndex = -1;
            }

            hoveredGateSlotIndex = grabbedGateSlotIndex;
            hoveredInputNode = null;
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
            wasPinching = false;
            SetHoverRingVisible(false);
            return;
        }

        // 2B. Prioritas #1 Port Kabel & Summon Aktif: badan gerbang & Input A/B wajib mengalah penuh
        if ((cableRouter != null && (cableRouter.IsDraggingCable || cableRouter.IsHoveringPort)) || circleSummoner.IsActive)
        {
            if (isRecordingAirDraw)
            {
                CancelAirDrawStrokeImmediate();
            }
            hoveredGateSlotIndex = -1;
            hoveredInputNode = null;
            wasPinching = isPinching;
            SetHoverRingVisible(false);
            return;
        }

        // 3. Zona badan Output menang prioritas atas hover gerbang di dekatnya saat tidak sedang menggenggam
        bool cursorInOutputZone = IsCursorInsideOutputPriorityZone(aimXY, aspectScale);
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
            hoveredGateSlotIndex = FindNearestGateSlotIndex(aimXY, GateHoverGrabRadius, ParkedGateHoverRadius);
        }

        // 4. Cek Kepal singkat (edge Open -> Fist) pada badan Input A & Input B (hanya bila tidak membidik gerbang/port)
        hoveredInputNode = null;
        if (hoveredGateSlotIndex < 0)
        {
            HandleInputAirTap(aspectScale, fistJustStarted);
        }

        // 5. Mulai genggam badan gerbang pada rising-edge Open -> Fist
        if (fistJustStarted && hoveredGateSlotIndex >= 0 && hoveredGateSlotIndex < gateSlots.Count)
        {
            CancelAirDrawStrokeImmediate();
            grabbedGateSlotIndex = hoveredGateSlotIndex;

            WorkbenchGateSlot grabbed = gateSlots[grabbedGateSlotIndex];
            grabbed.isPlacedOnWorkbench = true;
            grabbed.currentLocalZ = WorkbenchDepth + GrabLiftOffsetZ;
            grabbed.currentScale = GrabbedGateScale;

            if (grabbed.node != null && mainCam != null)
            {
                Transform camT = mainCam.transform;
                Vector3 worldPos = camT.TransformPoint(new Vector3(grabbed.currentLocalXY.x, grabbed.currentLocalXY.y, grabbed.currentLocalZ));
                Quaternion worldRot = camT.rotation * WorkbenchTiltRotation;
                grabbed.node.transform.localScale = Vector3.one * grabbed.currentScale;
                grabbed.node.SetExternalSpatialPose(true, worldPos, worldRot);
                grabbed.node.TriggerTapFeedback();
            }
        }
        else if (EnableLegacyAirDraw)
        {
            // Air-Draw lama dicadangkan nonaktif di Fase 3 (siap dikembangkan ulang di Fase 4)
            UpdateAirDrawInteraction(mainCam, aspectScale, dt);
        }

        wasPinching = isPinching;
        SetHoverRingVisible(false);
    }

    private bool IsCursorInsideOutputPriorityZone(Vector2 cursorXY, float aspectScale)
    {
        if (grabbedGateSlotIndex >= 0 || postDropLockoutTimer > 0f)
        {
            return false;
        }
        if (cableRouter != null && (cableRouter.IsDraggingCable || cableRouter.IsHoveringOutputNodePort))
        {
            return false;
        }
        if (levelManager != null && (levelManager.IsSandboxMode || levelManager.IsVerifying || levelManager.IsUiOverlayOpen))
        {
            return false;
        }

        Vector2 outputCenterXY = new Vector2((OutputTerminalBaseX * aspectScale) + 0.03f, OutputTerminalY + 0.10f);
        return Vector2.Distance(cursorXY, outputCenterXY) <= OutputZonePriorityRadius;
    }

    private void UpdateAirDrawInteraction(Camera mainCam, float aspectScale, float dt)
    {
        bool blockedByHigherPriority = isPinching
                                       || rawPinching
                                       || hoveredGateSlotIndex >= 0
                                       || hoveredInputNode != null
                                       || IsCursorInsideOutputPriorityZone(indexWorkbenchLocalXY, aspectScale)
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
        targetSlot.currentLocalZ = WorkbenchDepth;
        targetSlot.currentScale = ActiveGateScale;

        if (mainCam != null)
        {
            Transform camT = mainCam.transform;
            Vector3 worldPos = camT.TransformPoint(new Vector3(spawnXY.x, spawnXY.y, WorkbenchDepth));
            Quaternion worldRot = camT.rotation * WorkbenchTiltRotation;
            targetSlot.node.transform.localScale = Vector3.one * ActiveGateScale;
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
        bool shouldReturnToRack = slot.currentLocalXY.y > ParkReturnThresholdY || distToHome <= ParkSlotSnapRadius;

        if (shouldReturnToRack)
        {
            slot.isPlacedOnWorkbench = false;
            slot.placedLocalXY = homeXY;
            slot.currentLocalXY = homeXY;
            slot.currentLocalZ = WorkbenchDepth;
            slot.currentScale = ParkedGateScale;
            cableRouter?.RemoveWiresForNode(slot.node, circuitManager);
        }
        else
        {
            Vector2 clampedAssembly = ClampAssemblyZoneLocalXY(slot.currentLocalXY, aspectScale);
            slot.isPlacedOnWorkbench = true;
            slot.placedLocalXY = clampedAssembly;
            slot.currentLocalXY = clampedAssembly;
            slot.currentLocalZ = WorkbenchDepth;
            slot.currentScale = ActiveGateScale;
            slot.node.TriggerTapFeedback();
        }
    }

    private void HandleInputAirTap(float aspectScale, bool fistJustStarted)
    {
        if (levelManager != null && levelManager.IsVerifying)
        {
            return;
        }

        Vector2 cursorXY = fistJustStarted ? handGestureDetector.StabilizedCursorLocalXY : handGestureDetector.PalmCursorLocalXY;
        // Geser pusat badan sakelar sedikit ke kiri (-0.04) agar jarak aman terhadap Port_Out (+0.275, R=0.165) nol tumpang tindih
        Vector2 inputAXY = new Vector2((InputTerminalBaseX * aspectScale) - 0.04f, InputATerminalY);
        Vector2 inputBXY = new Vector2((InputTerminalBaseX * aspectScale) - 0.04f, InputBTerminalY);

        float distA = Vector2.Distance(cursorXY, inputAXY);
        float distB = Vector2.Distance(cursorXY, inputBXY);

        if (distA <= InputAirTapRadius && inputANode != null)
        {
            hoveredInputNode = inputANode;
            if (fistJustStarted && inputATapCooldown <= 0f)
            {
                inputATapCooldown = InputAirTapCooldownSeconds;
                ToggleInputTerminalNode(isInputB: false);
            }
        }

        if (distB <= InputAirTapRadius && inputBNode != null)
        {
            if (hoveredInputNode == null || distB < distA)
            {
                hoveredInputNode = inputBNode;
            }
            if (fistJustStarted && inputBTapCooldown <= 0f && hoveredInputNode == inputBNode)
            {
                inputBTapCooldown = InputAirTapCooldownSeconds;
                ToggleInputTerminalNode(isInputB: true);
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

        targetNode.TriggerTapFeedback();
    }

    private void SyncAllWorkbenchNodesToCamera(Camera mainCam, bool snapImmediately)
    {
        if (mainCam == null)
        {
            return;
        }

        Transform camT = mainCam.transform;
        Quaternion worldRot = camT.rotation * WorkbenchTiltRotation;
        float aspectScale = ComputeHorizontalAspectScale(mainCam);
        float settleAlpha = snapImmediately ? 1f : (1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));

        // 1. Terminal tetap papan roti: Input A, Input B, Output (selalu aktif tracked)
        if (inputANode != null)
        {
            Vector3 posA = camT.TransformPoint(new Vector3(InputTerminalBaseX * aspectScale, InputATerminalY, WorkbenchDepth));
            inputANode.transform.localScale = Vector3.one * TerminalScale;
            inputANode.SetExternalSpatialPose(true, posA, worldRot);
        }

        if (inputBNode != null)
        {
            Vector3 posB = camT.TransformPoint(new Vector3(InputTerminalBaseX * aspectScale, InputBTerminalY, WorkbenchDepth));
            inputBNode.transform.localScale = Vector3.one * TerminalScale;
            inputBNode.SetExternalSpatialPose(true, posB, worldRot);
        }

        if (outputNode != null)
        {
            Vector3 posOut = camT.TransformPoint(new Vector3(OutputTerminalBaseX * aspectScale, OutputTerminalY, WorkbenchDepth));
            outputNode.transform.localScale = Vector3.one * OutputTerminalScale;
            outputNode.SetExternalSpatialPose(true, posOut, worldRot);
        }

        // 2. 5 Gerbang Logika (Rak Parkir vs Zona Rakit vs Sedang Digenggam)
        for (int i = 0; i < gateSlots.Count; i++)
        {
            WorkbenchGateSlot slot = gateSlots[i];
            if (slot == null || slot.node == null)
            {
                continue;
            }

            if (i == grabbedGateSlotIndex)
            {
                Vector3 grabbedPos = camT.TransformPoint(new Vector3(slot.currentLocalXY.x, slot.currentLocalXY.y, slot.currentLocalZ));
                slot.node.transform.localScale = Vector3.one * slot.currentScale;
                slot.node.SetExternalSpatialPose(true, grabbedPos, worldRot);
            }
            else if (slot.isPlacedOnWorkbench)
            {
                slot.currentLocalXY = Vector2.Lerp(slot.currentLocalXY, slot.placedLocalXY, settleAlpha);
                slot.currentLocalZ = Mathf.Lerp(slot.currentLocalZ, WorkbenchDepth, settleAlpha);
                slot.currentScale = Mathf.Lerp(slot.currentScale, ActiveGateScale, settleAlpha);
                Vector3 placedPos = camT.TransformPoint(new Vector3(slot.currentLocalXY.x, slot.currentLocalXY.y, slot.currentLocalZ));
                slot.node.transform.localScale = Vector3.one * slot.currentScale;
                slot.node.SetExternalSpatialPose(true, placedPos, worldRot);
            }
            else
            {
                Vector2 homeXY = GetParkingSlotLocalXY(slot.slotIndex, aspectScale);
                slot.placedLocalXY = homeXY;
                slot.currentLocalXY = Vector2.Lerp(slot.currentLocalXY, homeXY, settleAlpha);
                slot.currentLocalZ = Mathf.Lerp(slot.currentLocalZ, WorkbenchDepth, settleAlpha);
                slot.currentScale = Mathf.Lerp(slot.currentScale, ParkedGateScale, settleAlpha);
                Vector3 parkedPos = camT.TransformPoint(new Vector3(slot.currentLocalXY.x, slot.currentLocalXY.y, slot.currentLocalZ));
                slot.node.SetExternalParkedPreview(true, parkedPos, worldRot, slot.currentScale);
            }
        }
    }

    private int FindNearestGateSlotIndex(Vector2 cursorXY, float placedMaxRadius, float parkedMaxRadius)
    {
        int bestIdx = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < gateSlots.Count; i++)
        {
            WorkbenchGateSlot slot = gateSlots[i];
            if (slot == null || slot.node == null)
            {
                continue;
            }

            float limit = slot.isPlacedOnWorkbench ? placedMaxRadius : parkedMaxRadius;
            float dist = Vector2.Distance(cursorXY, slot.currentLocalXY);
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
        return new Vector2(ParkingSlotBaseX[safeIdx] * aspectScale, ParkingRackY);
    }

    private static Vector2 ClampDraggedGateLocalXY(Vector2 rawXY, float aspectScale)
    {
        float minX = (AssemblyMinBaseX - 0.20f) * aspectScale;
        float maxX = (OutputTerminalBaseX - 0.05f) * aspectScale;
        return new Vector2(
            Mathf.Clamp(rawXY.x, minX, maxX),
            Mathf.Clamp(rawXY.y, AssemblyMinY, ParkingRackY + 0.16f));
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

        bool hasValidHand = detected
                            && cachedResult.handLandmarks != null
                            && cachedResult.handLandmarks.Count > 0
                            && cachedResult.handLandmarks[0].landmarks != null
                            && cachedResult.handLandmarks[0].landmarks.Count >= LandmarkCount;

        if (!hasValidHand)
        {
            isHandDetected = false;
            wasHandDetected = false;
            rawPinching = false;
            isIndexOnlyWandPose = false;
            circleSummoner.ClearPoseVoteBuffer();
            ResetLandmarkOneEuroFilters();
            SetHandRigVisible(false);
            return;
        }

        List<NormalizedLandmark> landmarks = cachedResult.handLandmarks[0].landmarks;
        UpdateHandVisualsFromLandmarks(landmarks, mainCam, timestampMs);
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

    private void ResetLandmarkOneEuroFilters()
    {
        for (int i = 0; i < LandmarkCount; i++)
        {
            landmarkOneEuroFilters[i].Reset();
        }
        lastLandmarkFilterTimestampMs = 0;
    }

    private void UpdateHandVisualsFromLandmarks(List<NormalizedLandmark> landmarks, Camera mainCam, long sampleTimestampMs)
    {
        Transform camTransform = mainCam.transform;
        float depthRatio = WorkbenchDepth / VideoQuadDepth;
        float planeW = currentVideoDisplayWidth * depthRatio;
        float planeH = currentVideoDisplayHeight * depthRatio;

        bool justAppeared = !wasHandDetected;
        if (justAppeared)
        {
            ResetLandmarkOneEuroFilters();
        }

        float sampleDt = (lastLandmarkFilterTimestampMs > 0 && sampleTimestampMs > lastLandmarkFilterTimestampMs)
            ? Mathf.Clamp((sampleTimestampMs - lastLandmarkFilterTimestampMs) * 0.001f, OneEuroMinDtSeconds, OneEuroMaxDtSeconds)
            : Mathf.Clamp(Time.unscaledDeltaTime, OneEuroMinDtSeconds, OneEuroMaxDtSeconds);
        lastLandmarkFilterTimestampMs = sampleTimestampMs;

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

            float localX = (filteredXNorm - 0.5f) * planeW;
            float localY = (0.5f - filteredYNorm) * planeH;
            float localZ = WorkbenchDepth - 0.14f + (filteredZ * DepthParallaxScale);
            smoothedLandmarksWorld[i] = camTransform.TransformPoint(new Vector3(localX, localY, localZ));
        }

        isHandDetected = true;
        wasHandDetected = true;

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

        // Perbarui detektor Kepal/Buka & Kursor Telapak Anti-Lompat (v7.0 Fase 3 + Fase 4)
        handGestureDetector.UpdateFromLandmarks(landmarks, smoothedLandmarksWorld, camTransform);
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
    private void HandleOutputTriggerNextLevel() => OnOutputNextLevelRequested?.Invoke();

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

        cableRouter.Tick(
            mainCam,
            circuitManager,
            inputANode,
            inputBNode,
            outputNode,
            activePlacedGatesBuffer,
            WorkbenchDepth,
            isAirWeavingActive,
            levelManager != null && levelManager.IsUiOverlayOpen,
            levelManager != null && levelManager.IsVerifying,
            grabbedGateSlotIndex >= 0 || circleSummoner.IsActive,
            isHandDetected,
            handGestureDetector.IsFistActive,
            handGestureDetector.FistJustStarted,
            handGestureDetector.FistJustEnded,
            handGestureDetector.StabilizedCursorLocalXY,
            handGestureDetector.PalmCursorLocalXY,
            handGestureDetector.PalmCursorWorldPosition);
    }

    private void UpdateHologramVisuals(Camera mainCam)
    {
        if (hologramVisual == null || mainCam == null) return;
        bool overlayOpen = levelManager != null && levelManager.IsUiOverlayOpen;
        hologramVisual.SetRootVisible(isAirWeavingActive);

        GateNode portFocusedNode = cableRouter != null ? cableRouter.HoveredPortNode : null;
        hologramVisual.UpdateNodeFrame(0, inputANode, true, false, false,
            hoveredInputNode == inputANode ? AirWeavingHologramVisual.NodeHoloState.BodyHovered : AirWeavingHologramVisual.NodeHoloState.Idle);
        hologramVisual.UpdateNodeFrame(1, inputBNode, false, true, false,
            hoveredInputNode == inputBNode ? AirWeavingHologramVisual.NodeHoloState.BodyHovered : AirWeavingHologramVisual.NodeHoloState.Idle);
        hologramVisual.UpdateNodeFrame(2, outputNode, false, false, true, AirWeavingHologramVisual.NodeHoloState.Idle);

        float aspectScale = ComputeHorizontalAspectScale(mainCam);
        for (int i = 0; i < gateSlots.Count && i < 5; i++)
        {
            WorkbenchGateSlot slot = gateSlots[i];
            if (slot == null || slot.node == null) continue;

            AirWeavingHologramVisual.NodeHoloState state;
            if (grabbedGateSlotIndex == i)
            {
                Vector2 homeXY = GetParkingSlotLocalXY(slot.slotIndex, aspectScale);
                bool returning = slot.currentLocalXY.y > ParkReturnThresholdY || Vector2.Distance(slot.currentLocalXY, homeXY) <= ParkSlotSnapRadius;
                state = returning ? AirWeavingHologramVisual.NodeHoloState.ReturningToRack : AirWeavingHologramVisual.NodeHoloState.Grabbed;
            }
            else if (portFocusedNode == slot.node)
            {
                state = AirWeavingHologramVisual.NodeHoloState.PortFocused;
            }
            else if (hoveredGateSlotIndex == i)
            {
                state = AirWeavingHologramVisual.NodeHoloState.BodyHovered;
            }
            else
            {
                state = slot.isPlacedOnWorkbench ? AirWeavingHologramVisual.NodeHoloState.Idle : AirWeavingHologramVisual.NodeHoloState.Parked;
            }

            hologramVisual.UpdateNodeFrame(3 + i, slot.node, false, false, false, state);
        }

        hologramVisual.UpdatePalmReticle(
            mainCam.transform,
            isHandDetected,
            overlayOpen,
            handGestureDetector.PalmCursorWorldPosition,
            handGestureDetector.IsFistActive,
            cableRouter != null && (cableRouter.IsHoveringPort || cableRouter.IsDraggingCable),
            handGestureDetector.MeanCurlRatio);
    }

    private void UpdateCircleSummoner(Camera mainCam, float dt)
    {
        if (mainCam == null) return;
        float aspectScale = ComputeHorizontalAspectScale(mainCam);
        bool overlayOrVerify = levelManager != null && (levelManager.IsUiOverlayOpen || levelManager.IsVerifying);
        bool draggingCable = cableRouter != null && cableRouter.IsDraggingCable;
        bool blocked;
        if (circleSummoner.IsActive)
        {
            // Begitu sudah masuk Charging/Ready, jangan batalkan hanya karena ujung jari melintas di atas gerbang/port/Output/tepi papan
            blocked = overlayOrVerify || grabbedGateSlotIndex >= 0 || draggingCable;
        }
        else
        {
            bool cableBusy = draggingCable || (cableRouter != null && cableRouter.IsHoveringPort);
            bool inOutputZone = IsCursorInsideOutputPriorityZone(handGestureDetector.PalmCursorLocalXY, aspectScale);
            blocked = overlayOrVerify
                      || grabbedGateSlotIndex >= 0
                      || cableBusy
                      || hoveredGateSlotIndex >= 0
                      || hoveredInputNode != null
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

        if (circleSummoner.BloomJustTriggeredThisFrame)
        {
            HandleCircleSummonBloomDeploy(mainCam, aspectScale, circleSummoner.DeployedGateType, circleSummoner.DeployedLocalXY);
        }
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
            targetSlot.node.TriggerTapFeedback();
            summonVisual?.TriggerBloomBurst(targetSlot.node.transform.position, isAmberNotice: true);
            SetFeedbackImmediate($"Gerbang {gateType} sudah terpasang di papan!", FeedbackWaitingColor, 2.2f);
            return;
        }

        Vector2 spawnXY = ClampAssemblyZoneLocalXY(deployLocalXY, aspectScale);
        targetSlot.isPlacedOnWorkbench = true;
        targetSlot.placedLocalXY = spawnXY;
        targetSlot.currentLocalXY = spawnXY;
        targetSlot.currentLocalZ = WorkbenchDepth;
        targetSlot.currentScale = ParkedGateScale;

        Transform camT = mainCam.transform;
        Vector3 worldPos = camT.TransformPoint(new Vector3(spawnXY.x, spawnXY.y, WorkbenchDepth));
        Quaternion worldRot = camT.rotation * WorkbenchTiltRotation;
        targetSlot.node.transform.localScale = Vector3.one * targetSlot.currentScale;
        targetSlot.node.SetExternalSpatialPose(true, worldPos, worldRot);
        targetSlot.node.TriggerTapFeedback();
        summonVisual?.TriggerBloomBurst(worldPos, isAmberNotice: false);
        postDropLockoutTimer = PostDropOutputLockoutSeconds;
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
        Vector2 outputLocalXY = new Vector2(OutputTerminalBaseX * aspectScale, OutputTerminalY);
        Vector3 outputWorldPos = outputNode != null
            ? outputNode.transform.position
            : mainCam.transform.TransformPoint(new Vector3(outputLocalXY.x, outputLocalXY.y, WorkbenchDepth));
        outputTrigger.Tick(mainCam, dt, isAirWeavingActive,
            levelManager != null && levelManager.IsSandboxMode,
            levelManager != null && levelManager.IsVerifying,
            levelManager != null && levelManager.IsCircuitCompleteLive,
            levelManager != null && levelManager.IsReadyForNextLevel,
            levelManager != null && levelManager.IsAwaitingSandboxTransition,
            levelManager != null && levelManager.IsUiOverlayOpen,
            isHandDetected, handGestureDetector.IsFistActive, handGestureDetector.PalmCursorLocalXY,
            outputLocalXY, outputWorldPos,
            grabbedGateSlotIndex >= 0, hoveredGateSlotIndex >= 0,
            cableRouter != null && cableRouter.IsHoveringPort,
            cableRouter != null && cableRouter.IsDraggingCable,
            isRecordingAirDraw || circleSummoner.IsActive || circleSummoner.ConsumedFistCancelThisFrame,
            postDropLockoutTimer);
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
        else if (levelManager == null || (!levelManager.IsVerifying && !levelManager.IsUiOverlayOpen))
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

        workbenchFrameMat = CreateSafeSpritesMaterial(WorkbenchFrameColor, 2988);
        rackFrameMat = CreateSafeSpritesMaterial(RackFrameColor, 2989);
        hoverRingMat = CreateSafeSpritesMaterial(HoverGateRingColor, 3016);
        airDrawStrokeMat = CreateSafeSpritesMaterial(Color.white, 3017);

        // 1. Bingkai tipis Zona Rakit Rangkaian di tengah
        GameObject borderObj = new GameObject("Workbench_AssemblyBorder");
        borderObj.transform.SetParent(workbenchVisualRootObj.transform, false);
        workbenchBorderRenderer = borderObj.AddComponent<LineRenderer>();
        workbenchBorderRenderer.useWorldSpace = true;
        workbenchBorderRenderer.positionCount = 5;
        workbenchBorderRenderer.widthMultiplier = 0.007f;
        workbenchBorderRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        workbenchBorderRenderer.receiveShadows = false;
        workbenchBorderRenderer.startColor = WorkbenchFrameColor;
        workbenchBorderRenderer.endColor = WorkbenchFrameColor;
        if (workbenchFrameMat != null)
        {
            workbenchBorderRenderer.sharedMaterial = workbenchFrameMat;
        }

        // 2. Garis pemisah Rak Parkir Gerbang Atas yang minimalis & tegas
        GameObject rackObj = new GameObject("Workbench_RackShelfLine");
        rackObj.transform.SetParent(workbenchVisualRootObj.transform, false);
        rackDividerRenderer = rackObj.AddComponent<LineRenderer>();
        rackDividerRenderer.useWorldSpace = true;
        rackDividerRenderer.positionCount = 2;
        rackDividerRenderer.widthMultiplier = 0.0085f;
        rackDividerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rackDividerRenderer.receiveShadows = false;
        rackDividerRenderer.startColor = RackFrameColor;
        rackDividerRenderer.endColor = RackFrameColor;
        if (rackFrameMat != null)
        {
            rackDividerRenderer.sharedMaterial = rackFrameMat;
        }

        // 3. Cincin Hover / Grab Highlight ramping di sekitar alas target gerbang atau terminal Input
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

        // 4. Lintasan goresan Air-Draw berpendar sian-violet (AR_AirDrawStrokeLine)
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
        }
        cableRouter.EnsureVisualsCreated();
        if (summonVisual == null)
        {
            summonVisual = GetComponent<AirWeavingSummonVisual>() ?? gameObject.AddComponent<AirWeavingSummonVisual>();
        }
        summonVisual.EnsureVisualsCreated();

        workbenchVisualRootObj.SetActive(false);
    }

    private void UpdateWorkbenchGuideVisuals(Camera mainCam)
    {
        if (mainCam == null || workbenchVisualRootObj == null || !workbenchVisualRootObj.activeSelf)
        {
            return;
        }

        Transform camT = mainCam.transform;
        float aspectScale = ComputeHorizontalAspectScale(mainCam);
        float guideZ = WorkbenchDepth + 0.14f;

        if (workbenchBorderRenderer != null)
        {
            float leftX = (AssemblyMinBaseX - 0.12f) * aspectScale;
            float rightX = (AssemblyMaxBaseX + 0.12f) * aspectScale;
            float botY = AssemblyMinY - 0.10f;
            float topY = AssemblyMaxY + 0.08f;

            workbenchFramePoints[0] = camT.TransformPoint(new Vector3(leftX, botY, guideZ));
            workbenchFramePoints[1] = camT.TransformPoint(new Vector3(rightX, botY, guideZ));
            workbenchFramePoints[2] = camT.TransformPoint(new Vector3(rightX, topY, guideZ));
            workbenchFramePoints[3] = camT.TransformPoint(new Vector3(leftX, topY, guideZ));
            workbenchFramePoints[4] = workbenchFramePoints[0];
            workbenchBorderRenderer.SetPositions(workbenchFramePoints);
        }

        if (rackDividerRenderer != null)
        {
            float rackLeftX = (ParkingSlotBaseX[0] - 0.34f) * aspectScale;
            float rackRightX = (ParkingSlotBaseX[ParkingSlotBaseX.Length - 1] + 0.34f) * aspectScale;
            float shelfY = ParkReturnThresholdY + 0.04f;
            rackLinePoints[0] = camT.TransformPoint(new Vector3(rackLeftX, shelfY, guideZ));
            rackLinePoints[1] = camT.TransformPoint(new Vector3(rackRightX, shelfY, guideZ));
            rackDividerRenderer.SetPositions(rackLinePoints);
        }
    }

    private void EnsureHandVisualRigCreated()
    {
        if (handRigRootObj != null)
        {
            return;
        }

        handRigRootObj = new GameObject("AR_AirWeavingHandRig");
        handRigRootObj.transform.SetParent(transform, false);

        jointBaseMat = CreateSafeSpritesMaterial(NeonJointBaseColor, 3010);
        jointTipMat = CreateSafeSpritesMaterial(NeonJointTipColor, 3012);
        boneMat = CreateSafeSpritesMaterial(NeonCyanBoneColor, 3008);
        trailMat = CreateSafeSpritesMaterial(Color.white, 3015);
        pinchRingMat = CreateSafeSpritesMaterial(PinchIdleColor, 3018);

        // Skala sendi & garis tulang diselaraskan dengan WorkbenchDepth (3.60f) agar tajam proporsional
        jointSphereObjs = new GameObject[LandmarkCount];
        for (int i = 0; i < LandmarkCount; i++)
        {
            bool isTip = (i == 4 || i == 8 || i == 12 || i == 16 || i == 20);
            bool isWrist = (i == 0);
            float diameter = isTip ? 0.052f : (isWrist ? 0.056f : 0.036f);

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
            lr.widthMultiplier = 0.015f;
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
        rawPinching = false;
        isPinching = false;
        wasPinching = false;
        isIndexOnlyWandPose = false;
        CancelAirDrawStrokeImmediate();
        ResetLandmarkOneEuroFilters();

        SetHandRigVisible(false);
        SetHoverRingVisible(false);
        outputTrigger?.HideImmediate();
        hologramVisual?.HideImmediate();
        cableRouter?.HideImmediate();
        circleSummoner.Reset();
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
        workbenchBorderRenderer = null;
        rackDividerRenderer = null;
        hoverHighlightRingRenderer = null;
        airDrawStrokeRenderer = null;

        if (handRigRootObj != null)
        {
            Destroy(handRigRootObj);
            handRigRootObj = null;
        }
        jointSphereObjs = null;
        boneLineRenderers = null;
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
        workbenchFrameMat = null;
        rackFrameMat = null;
        airDrawStrokeMat = null;
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
