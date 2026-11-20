using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public enum NodeType
{
    InputSource,
    AND,
    OR,
    NOT,
    XOR,
    NAND,
    OutputActuator
}

[RequireComponent(typeof(LineRenderer))]
public class GateNode : MonoBehaviour
{
    private const int ArcSegmentCount = 20;
    private const int DataPacketCount = 4;
    private const int OutputDioramaVariantCount = 5;

    [Header("1. Identitas & Konfigurasi Kartu")]
    [Tooltip("Jenis kartu gerbang logika sesuai marker fisik Vuforia")]
    public NodeType nodeType = NodeType.InputSource;

    [Tooltip("Nilai biner awal khusus untuk kartu bertipe InputSource (False = 0, True = 1)")]
    public bool initialInputState = false;

    [Header("2. Status Runtime & Koneksi Spasial")]
    [Tooltip("Status apakah kartu sedang terdeteksi oleh kamera AR Vuforia")]
    public bool isTracked = false;

    [Tooltip("Hasil evaluasi logika biner saat ini (False = 0 / Merah, True = 1 / Hijau)")]
    public bool currentOutputValue = false;

    [Tooltip("Daftar kartu di sebelah kiri yang mengirimkan sinyal input ke kartu ini")]
    public List<GateNode> connectedInputs = new List<GateNode>();

    [Tooltip("Kartu tujuan di sebelah kanan yang menerima pancaran laser dari kartu ini")]
    public GateNode connectedTarget = null;

    [Header("3. Pengaturan Visual Laser (LineRenderer)")]
    [Tooltip("Ketebalan dasar busur energi penghubung antar-kartu (skala marker 0.8 = 8 cm)")]
    public float laserWidth = 0.015f;

    [Tooltip("Warna busur energi saat sinyal bernilai 1 (True / HIGH)")]
    public Color highSignalColor = Color.green;

    [Tooltip("Warna busur energi saat sinyal bernilai 0 (False / LOW)")]
    public Color lowSignalColor = Color.red;

    [Header("4. Interaksi Rotasi Fisik & Anti-Flicker (GEMINI.md)")]
    [Tooltip("Aktifkan deteksi putaran kartu fisik ~90 derajat untuk mengubah InputSource 0/1")]
    public bool enablePhysicalRotationInput = true;

    [Tooltip("Batas sudut putaran kartu (derajat) untuk dianggap bernilai logika 1 (True)")]
    public float rotationAngleThreshold = 45f;

    [Tooltip("Waktu tunda (detik) sebelum memutus status isTracked saat kartu sempat tertutup tangan")]
    public float trackingLossDebounceTime = 0.4f;

    [Header("5. Umpan Balik Visual 3D/AR di Atas Kartu")]
    [Tooltip("MeshRenderer objek 3D indikator pada kartu (otomatis dibuat untuk OutputActuator jika kosong)")]
    public Renderer statusIndicatorRenderer;

    [Tooltip("Label TextMeshPro 3D world-space di atas kartu (otomatis dibuat saat Awake jika kosong)")]
    public TMP_Text statusLabel;

    [Tooltip("Tinggi posisi lokal 3D TextMeshPro di atas permukaan kartu (sumbu Y lokal)")]
    public float labelHeightOffset = 0.045f;

    [Tooltip("Ukuran font 3D TextMeshPro di atas kartu agar terbaca jelas tanpa menutupi marker")]
    public float labelFontSize = 0.55f;

    [Tooltip("Ukuran skala bola lampu indikator 3D OutputActuator saat bernilai 0 (MATI)")]
    public float actuatorIdleScale = 0.045f;

    [Tooltip("Ukuran skala bola lampu indikator 3D OutputActuator saat bernilai 1 (AKTIF)")]
    public float actuatorActiveScale = 0.065f;

    [Header("6. Visual 3D Bezier Arc, Paket Data & Soket Terminal (Hari 11)")]
    [Tooltip("Ketinggian titik pangkal P0 dan titik ujung P2 sedikit di atas permukaan kartu")]
    public float socketSurfaceOffset = 0.02f;

    [Tooltip("Faktor tinggi titik kontrol P1 terhadap jarak antar-kartu")]
    public float arcHeightDistanceFactor = 0.45f;

    [Tooltip("Tinggi minimal titik kontrol P1 kurva Quadratic Bezier")]
    public float minArcHeight = 0.30f;

    [Tooltip("Tinggi maksimal titik kontrol P1 kurva Quadratic Bezier agar tetap rapi di kamera HP")]
    public float maxArcHeight = 1.15f;

    [Tooltip("Diameter bola paket data saat sinyal bernilai 1 (HIGH)")]
    public float packetHighScale = 0.055f;

    [Tooltip("Diameter bola paket data saat sinyal bernilai 0 (LOW / siaga)")]
    public float packetLowScale = 0.034f;

    [Tooltip("Kecepatan aliran paket data (siklus/detik) saat sinyal bernilai 1 (HIGH)")]
    public float packetHighSpeed = 1.35f;

    [Tooltip("Kecepatan aliran paket data (siklus/detik) saat sinyal bernilai 0 (LOW / siaga)")]
    public float packetLowSpeed = 0.35f;

    [Tooltip("Diameter cakram soket terminal 3D pada pangkal P0 dan ujung P2")]
    public float socketRingDiameter = 0.105f;

    [Tooltip("Ketebalan vertikal cakram soket terminal 3D (Cylinder pipih)")]
    public float socketRingThickness = 0.006f;

    [Header("7. Visual Totem Reaktor Hologram 3D (Hari 12)")]
    [Tooltip("Ketinggian minimum Label_Status3D agar melayang rapi di atas model/totem 3D tanpa bertabrakan")]
    public float totemLabelMinHeight = 0.58f;

    [Tooltip("Kecepatan putar totem (derajat/detik) saat sinyal bernilai 1 (HIGH)")]
    public float totemHighSpinSpeed = 145f;

    [Tooltip("Kecepatan putar totem (derajat/detik) saat sinyal bernilai 0 (LOW / siaga)")]
    public float totemLowSpinSpeed = 28f;

    [Tooltip("Amplitudo gerak melayang vertikal (bobbing) totem saat sinyal bernilai 1 (HIGH)")]
    public float totemHighBobAmplitude = 0.011f;

    [Header("8. Visual Diorama Aktuator 3D Kontekstual (Hari 13)")]
    [Tooltip("Ketinggian minimum Label_Status3D pada kartu OutputActuator agar melayang bersih di atas diorama 3D")]
    public float outputLabelMinHeight = 0.64f;

    [Tooltip("Kecepatan putar rotor sirine darurat (derajat/detik) pada Level 1 saat OUTPUT = 1")]
    public float sirenRotorSpeed = 360f;

    [Tooltip("Sudut buka daun pintu brankas (derajat) pada Level 3 saat OUTPUT = 1")]
    public float vaultOpenAngle = 75f;

    [Header("9. Telemetri Pin 3D & Rising-Edge Shockwave (v3.0)")]
    [Tooltip("Durasi animasi cincin gelombang kejut 3D (detik) saat terjadi transisi naik 0 -> 1")]
    public float shockwaveDuration = 0.45f;

    [Tooltip("Skala awal cincin gelombang kejut 3D pada pusat kartu")]
    public float shockwaveStartScale = 0.07f;

    [Tooltip("Skala maksimum cincin gelombang kejut 3D mendekati tepi kartu 0.8 unit")]
    public float shockwaveEndScale = 0.36f;

    [Tooltip("Opasitas awal cincin gelombang kejut 3D sebelum meluruh ke 0")]
    [Range(0.1f, 1f)]
    public float shockwaveStartAlpha = 0.85f;

    [Header("10. Interaksi Sentuh 3D AR (v4.0)")]
    [Tooltip("Durasi animasi pantulan skala elastis (detik) saat hologram kartu disentuh langsung")]
    public float tapBounceDuration = 0.18f;

    [Tooltip("Puncak pengali skala pantulan elastis saat hologram kartu disentuh")]
    public float tapBouncePeakScale = 1.18f;

    [Header("v5/v6 Spatial & Model Config")]
    [Tooltip("Aktifkan agar pose spasial dan isTracked dikendalikan sistem eksternal (v6 markerless/hand-tracking), bukan callback Vuforia")]
    public bool useExternalSpatialPose = false;

    [Tooltip("Prefab/model 3D kustom opsional via Inspector; jika null akan memuat model Quaternius dari Resources/Models/V5_SciFi/")]
    public GameObject customModelPrefab;

    [HideInInspector]
    public LineRenderer lineRenderer;

    [HideInInspector]
    public bool isInputAConnected;

    [HideInInspector]
    public bool isInputBConnected;

    [HideInInspector]
    public bool inputAValue;

    [HideInInspector]
    public bool inputBValue;

    private GameObject outputDioramaRootObj;
    private GameObject[] outputDioramaVariantObjs;
    private int activeDioramaVariantIndex = 0;

    private Transform sirenRotorPivot;
    private Transform sirenPulseRingTransform;
    private Vector3 sirenPulseRingBaseScale;

    private Transform stairHaloLowerTransform;
    private Transform stairHaloUpperTransform;

    private Transform vaultDoorPivotTransform;
    private Transform vaultWheelTransform;
    private Transform vaultDiamondTransform;
    private Vector3 vaultDiamondBaseScale;
    private float currentVaultDoorAngle = 0f;

    private Transform reactorRingPivotA;
    private Transform reactorRingPivotB;
    private Transform reactorCoreTransform;
    private Vector3 reactorCoreBaseScale;

    private Transform twinCrystalLeftTransform;
    private Transform twinCrystalRightTransform;
    private Transform twinBridgeHaloTransform;
    private Vector3 twinCrystalBaseScale;
    private Vector3 twinBridgeBaseScale;
    private float twinSyncSpinPhase = 0f;

    private GameObject[] dataPacketObjs;
    private GameObject sourceSocketObj;
    private GameObject targetSocketObj;

    private GameObject[] inputPinObjs;
    private Material[] inputPinMaterials;
    private GameObject shockwaveRingObj;
    private Material shockwaveMaterial;

    private GameObject totemRootObj;
    private Transform totemFloatGroup;
    private Transform totemPrimarySpinTransform;
    private Transform totemSecondarySpinTransform;
    private Transform totemHarmonicMoveTransform;
    private Vector3 totemFloatBasePos;
    private Vector3 totemHarmonicBasePos;
    private Vector3 totemHarmonicBaseScale;

    private Material lineMaterial;
    private Material dataPacketMaterial;
    private Material socketMaterial;
    private Material totemCoreMaterial;
    private Material totemAccentMaterial;
    private Material dioramaBodyMaterial;
    private Material dioramaAccentMaterial;
    private Material dioramaEmissiveMaterial;
    private Material customIndicatorMaterial;

    private Coroutine debounceCoroutine;
    private bool lastDetectedRotationBit = false;
    private bool rotationInitialized = false;
    private bool isSimulationOverride = false;
    private float packetFlowPhase = 0f;
    private float totemSpinPhase = 0f;
    private float totemBobPhase = 0f;

    private bool shockwaveBaselineInitialized = false;
    private bool lastShockwaveOutputState = false;
    private bool isShockwavePlaying = false;
    private float shockwaveElapsed = 0f;

    private SphereCollider touchHitboxCollider;
    private bool isTapBouncePlaying = false;
    private float tapBounceElapsed = 0f;

    // Arsitektur v5.0 / v6-Ready: AR Grounding, Port Fisik 3D, & Material Quaternius Reaktif
    private const string V5ModelsResourcePrefix = "Models/V5_SciFi/";
    private const float BaseTileTopSurfaceY = 0.040f;
    private const float BaseTileFootprintXZ = 0.56f;
    private const float BaseTileThicknessY = 0.038f;
    private const float BaseTileBottomY = 0.002f;

    private GameObject contactShadowObj;
    private GameObject baseTileRootObj;
    private Transform portInATransform;
    private Transform portInBTransform;
    private Transform portOutTransform;
    private Material portInAMaterial;
    private Material portInBMaterial;
    private Material portOutMaterial;

    private readonly List<Material> managedMaterials = new List<Material>();
    private readonly List<Material> totemReactiveAccentMaterials = new List<Material>();
    private List<Material>[] dioramaVariantReactiveAccentMaterials;
    private int lastAppliedAccentState = -1;
    private int lastAppliedAccentDioramaVariant = -1;
    private int lastPortInAVisualState = -2;
    private int lastPortInBVisualState = -2;
    private int lastPortOutVisualState = -2;

    // v5.0 Tahap 2: Mekanisme Reaktif 3D (Tuas Fisik, Mesin Gerbang, Diorama Reaktif, & Pool Percikan Zero-GC)
    private const int SparkPoolSize = 12;
    private const float LeverAngleMagnitude = 38f;
    private const float VaultPlateClosedX = 0.056f;
    private const float VaultPlateOpenX = 0.135f;

    private Material v5MetalDarkMaterial;
    private Material v5MetalSteelMaterial;
    private Material v5SirenBeamMaterial;
    private Material[] v5StairStepMaterials;

    private Transform v5LeverPivot;
    private float currentLeverAngle = -LeverAngleMagnitude;

    private Transform v5GateAnimPrimary;
    private Transform v5GateAnimSecondary;
    private Vector3 v5GateAnimPrimaryBasePos;
    private Vector3 v5GateAnimSecondaryBasePos;
    private float v5GateAnimSweepMinY;
    private float v5GateAnimSweepMaxY;
    private float v5GateAnimPhase = 0f;

    private Transform v5SirenRotorPivot;
    private GameObject v5SirenBeamsGroup;
    private Transform v5StairLanternHalo;
    private float v5StairChasePhase = 0f;
    private int lastStairChaseStep = -2;
    private Transform v5VaultSlideLeft;
    private Transform v5VaultSlideRight;
    private Transform v5VaultWheelPivot;
    private float currentVaultSlideOffset = VaultPlateClosedX;
    private Transform v5PodRingA;
    private Transform v5PodRingB;
    private Transform v5CapsuleOrbitA;
    private Transform v5CapsuleOrbitB;

    private GameObject sparkPoolRootObj;
    private Transform[] sparkTransforms;
    private Vector3[] sparkLocalPositions;
    private Vector3[] sparkVelocities;
    private float[] sparkInitialScales;
    private Material sparkBurstMaterial;
    private bool isSparkBurstActive = false;
    private bool isSparkCelebrationMode = false;
    private float sparkBurstElapsed = 0f;
    private float sparkBurstDuration = 0.38f;
    private Color sparkBurstBaseColor = Color.white;

    // v5.0 Tahap 3: Dual-Layer Physical Energy Conduits, Tangent Bolts, & Connection Snap VFX (Zero-GC)
    private const float SnapSurgeDuration = 0.36f;
    private const float LabelEdgeOffsetZ = 0.06f;
    private const float LabelClearanceAboveModelY = 0.14f;
    private const float LabelCompactScale = 0.78f;
    private readonly Vector3[] bezierPointsCache = new Vector3[ArcSegmentCount];
    private LineRenderer conduitSheathRenderer;
    private Material conduitSheathMat;
    private GateNode lastVisualConnectedTarget;
    private int lastVisualTargetSlotIndex = -1;
    private float snapSurgeTimer = 0f;
    private float portOutFlashTimer = 0f;
    private float portInAFlashTimer = 0f;
    private float portInBFlashTimer = 0f;
    private Camera cachedMainCamera;
    private int lastRenderedTelemetryKey = -1;
    private bool externalParkedPreview = false;
    private Renderer[] cachedExternalVisualRenderers;
    private Renderer[] cachedGroundHideRenderers;
    private GameObject labelBackdropObj;
    private Transform labelBackdropTransform;
    private Material labelBackdropMaterial;
    private float normalizedModelTopY = 0f;
    private readonly float[] dioramaVariantModelTopY = new float[OutputDioramaVariantCount];

    // v7.0 Fase 1: Thin hooks untuk visual mekanisme Open Cutaway, Pod LED 2x2 Output, jangkar warna Input, & label kontekstual
    private GateMechanismVisual mechanismVisual;
    private OutputTruthPodVisual outputTruthPod;
    private Material inputIdentityAccentMat;
    private Transform labelIdentityPipTransform;
    private bool hasContextualLabel = false;
    private string cachedContextualLabel0;
    private string cachedContextualLabel1;

    private static readonly Color InputAIdentityCyanColor = new Color(0.18f, 0.85f, 1.00f, 1f);
    private static readonly Color InputBIdentityGoldColor = new Color(1.00f, 0.84f, 0.22f, 1f);

    public OutputTruthPodVisual OutputTruthPod => outputTruthPod;

    private static int activeGateNodeCount = 0;
    private static Mesh sharedTorusMesh;
    private static Mesh sharedDiamondSpireMesh;
    private static Texture2D sharedContactShadowTexture;

    private static readonly int ColorPropId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorPropId = Shader.PropertyToID("_EmissionColor");
    private static readonly int GlossinessPropId = Shader.PropertyToID("_Glossiness");
    private static readonly int MetallicPropId = Shader.PropertyToID("_Metallic");
    private static readonly Color ReactiveAccentHighColor = new Color(0.14f, 1.00f, 0.78f, 1f);
    private static readonly Color ReactiveAccentLowColor = new Color(0.76f, 0.22f, 0.10f, 1f);
    private static readonly Color TapInspectBurstColor = new Color(1.00f, 0.76f, 0.22f, 1f);
    private static readonly Color VictoryBurstGoldColor = new Color(1.00f, 0.88f, 0.26f, 1f);
    private static readonly Color VictoryBurstCyanColor = new Color(0.22f, 1.00f, 0.88f, 1f);
    private static readonly Color SirenBeamHighColor = new Color(0.18f, 1.00f, 0.82f, 0.42f);
    private static readonly Color SirenBeamLowColor = new Color(0.75f, 0.22f, 0.10f, 0.08f);
    private static readonly Color StairStepActiveColor = new Color(1.00f, 0.94f, 0.32f, 1f);
    private static readonly Color StairStepTrailColor = new Color(0.18f, 0.92f, 0.85f, 1f);
    private static readonly Color StairStepIdleColor = new Color(0.26f, 0.32f, 0.42f, 1f);
    private static readonly Color ConduitCoreHighColor = new Color(0.28f, 1.00f, 0.86f, 1f);
    private static readonly Color ConduitCoreLowColor = new Color(0.84f, 0.30f, 0.14f, 0.95f);
    private static readonly Color ConduitSheathHighColor = new Color(0.16f, 0.98f, 0.82f, 0.32f);
    private static readonly Color ConduitSheathLowColor = new Color(0.72f, 0.28f, 0.14f, 0.14f);
    private static readonly Color SnapSurgeFlashColor = new Color(0.86f, 1.00f, 1.00f, 1f);
    private static readonly Color LabelBackdropColor = new Color(0.03f, 0.06f, 0.11f, 0.88f);
    private static readonly Color SnapSurgeSheathColor = new Color(0.72f, 1.00f, 1.00f, 0.54f);
    private static readonly Color EnergyBoltHighColor = new Color(0.82f, 1.00f, 0.98f, 1f);
    private static readonly Color EnergyBoltLowColor = new Color(0.78f, 0.40f, 0.22f, 0.86f);

    private static readonly float[] PacketPhaseOffsets = { 0f, 0.25f, 0.50f, 0.75f };

    private static readonly Color BrightGreenTextColor = new Color(0.36f, 1.00f, 0.84f, 1f);
    private static readonly Color BrightRedOrangeTextColor = new Color(1.00f, 0.54f, 0.36f, 1f);
    private static readonly Color HighSocketColor = new Color(0.15f, 1f, 0.65f, 1f);
    private static readonly Color LowSocketColor = new Color(0.65f, 0.18f, 0.1f, 1f);
    private static readonly Color HighTotemCoreColor = new Color(0.10f, 1.00f, 0.82f, 1f);
    private static readonly Color HighTotemAccentColor = new Color(1.00f, 0.85f, 0.22f, 1f);
    private static readonly Color LowTotemCoreColor = new Color(0.74f, 0.22f, 0.10f, 1f);
    private static readonly Color LowTotemAccentColor = new Color(0.24f, 0.38f, 0.54f, 1f);
    private static readonly Color HighDioramaBodyColor = new Color(0.32f, 0.56f, 0.72f, 1f);
    private static readonly Color HighDioramaAccentColor = new Color(1.00f, 0.82f, 0.20f, 1f);
    private static readonly Color HighDioramaEmissiveColor = new Color(0.15f, 1.00f, 0.78f, 1f);
    private static readonly Color HighStairLanternYellowColor = new Color(1.00f, 0.94f, 0.26f, 1f);
    private static readonly Color HighStairHaloCyanColor = new Color(0.22f, 0.96f, 1.00f, 1f);
    private static readonly Color LowDioramaBodyColor = new Color(0.22f, 0.27f, 0.35f, 1f);
    private static readonly Color LowDioramaAccentColor = new Color(0.25f, 0.35f, 0.48f, 1f);
    private static readonly Color LowDioramaEmissiveColor = new Color(0.62f, 0.16f, 0.10f, 1f);
    private static readonly Color PinUnconnectedColor = new Color(0.42f, 0.54f, 0.66f, 1f);
    private static readonly Color PinLowColor = new Color(0.86f, 0.26f, 0.12f, 1f);
    private static readonly Color PinHighColor = new Color(0.18f, 1.00f, 0.72f, 1f);
    private static readonly Color ShockwavePulseColor = new Color(0.20f, 1.00f, 0.82f, 1f);

    private void Awake()
    {
        activeGateNodeCount++;

        InitializeLineRenderer();
        Initialize3DVisualFeedback();
        InitializeArcAndTerminalVisuals();
        InitializeContactShadowVisual();
        InitializeBaseTileVisual();
        InitializePhysicalPorts();
        InitializeV5SharedRigMaterials();
        InitializeTotemVisual();
        InitializeOutputDioramaVisual();
        InitializeOutputTruthPodVisual();
        InitializeInputPinsVisual();
        InitializeShockwaveVisual();
        InitializeSparkBurstPool();
        InitializeTouchHitbox();
    }

    private void Start()
    {
        if (nodeType == NodeType.InputSource)
        {
            currentOutputValue = initialInputState;
        }

        EvaluateGateLogic();
        UpdateVisuals();
    }

    private void Update()
    {
        if (nodeType == NodeType.InputSource && isTracked && enablePhysicalRotationInput && !isSimulationOverride && !useExternalSpatialPose)
        {
            CheckPhysicalRotationInput();
        }

        if (isTracked)
        {
            SetTouchHitboxEnabled(true);
            SetGroundingAndPortsActive(true, showPorts: true);
            UpdateLabelBillboard();
            AnimateTotemVisual();
            AnimateOutputDioramaVisual();
            AnimateV5NodeMechanisms();
            AnimateV5OutputDioramaRig();
            if (mechanismVisual != null)
            {
                mechanismVisual.UpdateMechanism(isInputAConnected, inputAValue, isInputBConnected, inputBValue, currentOutputValue, isParkedPreview: false, deltaTime: Time.deltaTime);
            }
            if (outputTruthPod != null)
            {
                outputTruthPod.SetTrackedVisible(true);
                outputTruthPod.TickAnimation(Time.deltaTime);
            }
            DetectRisingEdgeAndAnimateShockwave();
            UpdateTapBounceAnimation();
            UpdateSparkBurstPool();
            UpdatePortSnapFlashTimers();
        }
        else if (useExternalSpatialPose && externalParkedPreview)
        {
            SetTouchHitboxEnabled(false);
            SetGroundingAndPortsActive(true, showPorts: false);
            SetTotemActive(true);
            SetOutputDioramaActive(false);
            SetOutputTruthPodVisible(false);
            SetInputPinsActive(false);
            if (mechanismVisual != null)
            {
                mechanismVisual.UpdateMechanism(false, false, false, false, false, isParkedPreview: true, deltaTime: Time.deltaTime);
            }
            UpdateLabelBillboard();
            UpdateTapBounceAnimation();
            ResetShockwaveState();
            ResetSparkBurstPool();
            ResetConnectionSnapState();
        }
        else
        {
            SetTouchHitboxEnabled(false);
            SetGroundingAndPortsActive(false, showPorts: false);
            SetTotemActive(false);
            SetOutputDioramaActive(false);
            SetOutputTruthPodVisible(false);
            SetInputPinsActive(false);
            ResetShockwaveState();
            ResetTapBounceState();
            ResetSparkBurstPool();
            ResetConnectionSnapState();
        }

        RefreshConnectionVisuals(advanceFlowPhase: true);
    }

    private void OnDestroy()
    {
        if (debounceCoroutine != null)
        {
            StopCoroutine(debounceCoroutine);
            debounceCoroutine = null;
        }

        DestroyProceduralMaterial(ref lineMaterial);
        DestroyProceduralMaterial(ref dataPacketMaterial);
        DestroyProceduralMaterial(ref socketMaterial);
        DestroyProceduralMaterial(ref totemCoreMaterial);
        DestroyProceduralMaterial(ref totemAccentMaterial);
        DestroyProceduralMaterial(ref dioramaBodyMaterial);
        DestroyProceduralMaterial(ref dioramaAccentMaterial);
        DestroyProceduralMaterial(ref dioramaEmissiveMaterial);
        DestroyProceduralMaterial(ref customIndicatorMaterial);
        DestroyProceduralMaterial(ref shockwaveMaterial);

        if (inputPinMaterials != null)
        {
            for (int i = 0; i < inputPinMaterials.Length; i++)
            {
                DestroyProceduralMaterial(ref inputPinMaterials[i]);
            }
        }

        for (int i = 0; i < managedMaterials.Count; i++)
        {
            Material mat = managedMaterials[i];
            DestroyProceduralMaterial(ref mat);
        }
        managedMaterials.Clear();
        totemReactiveAccentMaterials.Clear();
        dioramaVariantReactiveAccentMaterials = null;
        portInAMaterial = null;
        portInBMaterial = null;
        portOutMaterial = null;
        v5MetalDarkMaterial = null;
        v5MetalSteelMaterial = null;
        v5SirenBeamMaterial = null;
        v5StairStepMaterials = null;
        sparkBurstMaterial = null;
        conduitSheathMat = null;
        conduitSheathRenderer = null;
        lastVisualConnectedTarget = null;
        cachedMainCamera = null;
        cachedExternalVisualRenderers = null;
        labelBackdropObj = null;
        labelBackdropTransform = null;
        labelBackdropMaterial = null;
        labelIdentityPipTransform = null;
        inputIdentityAccentMat = null;
        mechanismVisual = null;
        outputTruthPod = null;
        touchHitboxCollider = null;

        activeGateNodeCount = Mathf.Max(0, activeGateNodeCount - 1);
        if (activeGateNodeCount == 0)
        {
            DestroyProceduralMesh(ref sharedTorusMesh);
            DestroyProceduralMesh(ref sharedDiamondSpireMesh);
            DestroyProceduralTexture(ref sharedContactShadowTexture);
        }
    }

    /// <summary>
    /// Menginisialisasi komponen LineRenderer untuk busur Quadratic Bezier 20 segmen
    /// dengan material Sprites/Default agar kompatibel penuh dengan Android OpenGLES3.
    /// </summary>
    private void InitializeLineRenderer()
    {
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null)
        {
            lineRenderer = gameObject.AddComponent<LineRenderer>();
        }

        lineMaterial = CreateSafeOpenGLMaterial();
        if (lineMaterial != null)
        {
            lineMaterial.color = Color.white;
            lineRenderer.sharedMaterial = lineMaterial;
        }

        lineRenderer.positionCount = ArcSegmentCount;
        lineRenderer.useWorldSpace = true;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.numCapVertices = 4;
        lineRenderer.numCornerVertices = 4;
        lineRenderer.widthMultiplier = 0.012f;
        lineRenderer.widthCurve = new AnimationCurve(
            new Keyframe(0f, 0.82f),
            new Keyframe(0.5f, 1.12f),
            new Keyframe(1f, 0.82f)
        );
        lineRenderer.enabled = false;

        // Lapisan selubung energi luar transparan (AR_ConduitOuterSheath)
        if (nodeType != NodeType.OutputActuator)
        {
            Transform existingSheath = transform.Find("AR_ConduitOuterSheath");
            GameObject sheathObj = existingSheath != null
                ? existingSheath.gameObject
                : CreateEmptyChild("AR_ConduitOuterSheath", transform, Vector3.zero).gameObject;

            conduitSheathRenderer = sheathObj.GetComponent<LineRenderer>();
            if (conduitSheathRenderer == null)
            {
                conduitSheathRenderer = sheathObj.AddComponent<LineRenderer>();
            }

            conduitSheathMat = CreateSafeOpenGLMaterial();
            if (conduitSheathMat != null)
            {
                conduitSheathMat.color = Color.white;
                managedMaterials.Add(conduitSheathMat);
                conduitSheathRenderer.sharedMaterial = conduitSheathMat;
            }

            conduitSheathRenderer.positionCount = ArcSegmentCount;
            conduitSheathRenderer.useWorldSpace = true;
            conduitSheathRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            conduitSheathRenderer.receiveShadows = false;
            conduitSheathRenderer.numCapVertices = 4;
            conduitSheathRenderer.numCornerVertices = 4;
            conduitSheathRenderer.widthMultiplier = 0.032f;
            conduitSheathRenderer.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.76f),
                new Keyframe(0.5f, 1.16f),
                new Keyframe(1f, 0.76f)
            );
            conduitSheathRenderer.enabled = false;
        }
    }

    /// <summary>
    /// Membuat material unlit dengan shader Sprites/Default (atau klon material aman)
    /// agar tidak pernah ter-strip maupun berwarna magenta pada build IL2CPP Android OpenGLES3.
    /// </summary>
    private Material CreateSafeOpenGLMaterial()
    {
        Shader safeShader = Shader.Find("Sprites/Default");
        if (safeShader != null)
        {
            return new Material(safeShader);
        }

        if (lineRenderer != null && lineRenderer.sharedMaterial != null)
        {
            return new Material(lineRenderer.sharedMaterial);
        }

        Shader uiFallbackShader = Shader.Find("UI/Default");
        if (uiFallbackShader != null)
        {
            return new Material(uiFallbackShader);
        }

        return null;
    }

    private static void DestroyProceduralMaterial(ref Material mat)
    {
        if (mat != null)
        {
            Destroy(mat);
            mat = null;
        }
    }

    private static void DestroyProceduralMesh(ref Mesh mesh)
    {
        if (mesh != null)
        {
            Destroy(mesh);
            mesh = null;
        }
    }

    private static void DestroyProceduralTexture(ref Texture2D tex)
    {
        if (tex != null)
        {
            Destroy(tex);
            tex = null;
        }
    }

    /// <summary>
    /// Membuat 3D TextMeshPro world-space tepat di atas atap mesin (localPosition.z = +0.06f) secara otomatis saat Awake(),
    /// lengkap dengan papan latar gelap semi-transparan AR_LabelBackdrop agar teks selalu kontras dari sudut miring maupun tegak lurus.
    /// </summary>
    private void Initialize3DVisualFeedback()
    {
        if (statusLabel == null)
        {
            GameObject labelObj = new GameObject("Label_Status3D");
            labelObj.transform.SetParent(transform, false);
            labelObj.transform.localPosition = Vector3.zero;
            labelObj.transform.localRotation = Quaternion.identity;
            labelObj.transform.localScale = Vector3.one;

            TextMeshPro tmp3D = labelObj.AddComponent<TextMeshPro>();
            tmp3D.fontSize = labelFontSize;
            tmp3D.alignment = TextAlignmentOptions.Center;
            tmp3D.textWrappingMode = TextWrappingModes.NoWrap;
            tmp3D.fontStyle = FontStyles.Bold;

            RectTransform rectTransform = tmp3D.rectTransform;
            if (rectTransform != null)
            {
                rectTransform.sizeDelta = new Vector2(2.5f, 0.6f);
            }

            statusLabel = tmp3D;
        }

        statusLabel.transform.localScale = Vector3.one * LabelCompactScale;
        UpdateStatusLabelPlacement();
        InitializeLabelBackdrop();
    }

    /// <summary>
    /// Memastikan statusLabel selalu melayang bersih di atas atap model 3D Quaternius
    /// (minimal +0.14f di atas modelTopY dan >= totemLabelMinHeight/outputLabelMinHeight) pada Z = +0.06f.
    /// </summary>
    private void UpdateStatusLabelPlacement()
    {
        if (statusLabel == null)
        {
            return;
        }

        float requiredLabelHeight = (nodeType == NodeType.OutputActuator)
            ? Mathf.Max(outputLabelMinHeight, 0.64f)
            : Mathf.Max(totemLabelMinHeight, 0.58f);

        float activeModelTopY = GetActiveNormalizedModelTopY();
        float clearanceHeight = activeModelTopY > 0.001f ? (activeModelTopY + LabelClearanceAboveModelY) : 0f;
        float targetYPos = Mathf.Max(labelHeightOffset, Mathf.Max(requiredLabelHeight, clearanceHeight));

        Vector3 labelPos = statusLabel.transform.localPosition;
        labelPos.y = targetYPos;
        labelPos.z = LabelEdgeOffsetZ;
        statusLabel.transform.localPosition = labelPos;
    }

    private float GetActiveNormalizedModelTopY()
    {
        if (nodeType == NodeType.OutputActuator)
        {
            if (activeDioramaVariantIndex >= 0 && activeDioramaVariantIndex < dioramaVariantModelTopY.Length)
            {
                return dioramaVariantModelTopY[activeDioramaVariantIndex];
            }

            return 0f;
        }

        return normalizedModelTopY;
    }

    private void InitializeLabelBackdrop()
    {
        if (statusLabel == null)
        {
            return;
        }

        Transform existingBackdrop = statusLabel.transform.Find("AR_LabelBackdrop");
        if (existingBackdrop != null)
        {
            labelBackdropObj = existingBackdrop.gameObject;
            labelBackdropTransform = existingBackdrop;
            return;
        }

        labelBackdropMaterial = CreateSafeOpenGLMaterial();
        if (labelBackdropMaterial != null)
        {
            labelBackdropMaterial.color = LabelBackdropColor;
            // Render setelah model 3D opaque (2000) dan tepat sebelum TextMeshPro Transparent (3000)
            labelBackdropMaterial.renderQueue = 2995;
            managedMaterials.Add(labelBackdropMaterial);
        }

        labelBackdropObj = CreatePrimitivePart(
            "AR_LabelBackdrop",
            PrimitiveType.Quad,
            statusLabel.transform,
            new Vector3(0f, 0f, 0.012f),
            Vector3.zero,
            new Vector3(0.58f, 0.096f, 1f),
            labelBackdropMaterial);
        labelBackdropTransform = labelBackdropObj.transform;
        labelBackdropObj.SetActive(false);

        if (nodeType == NodeType.InputSource)
        {
            Color idColor = IsInputBNode() ? InputBIdentityGoldColor : InputAIdentityCyanColor;
            Material pipMat = CreateSafeOpenGLMaterial();
            if (pipMat != null)
            {
                pipMat.color = idColor;
                pipMat.renderQueue = 2996;
                managedMaterials.Add(pipMat);
            }

            GameObject pipObj = CreatePrimitivePart(
                "AR_LabelIdentityPip",
                PrimitiveType.Quad,
                statusLabel.transform,
                new Vector3(-0.308f, 0f, 0.008f),
                Vector3.zero,
                new Vector3(0.026f, 0.088f, 1f),
                pipMat);
            labelIdentityPipTransform = pipObj.transform;
        }
    }

    private void UpdateLabelBackdropSize(string labelText)
    {
        if (statusLabel == null || labelBackdropTransform == null)
        {
            return;
        }

        Vector2 preferred = statusLabel.GetPreferredValues(labelText);
        float width = preferred.x > 0.01f ? Mathf.Clamp(preferred.x + 0.055f, 0.22f, 1.14f) : 0.58f;
        float height = preferred.y > 0.01f ? Mathf.Clamp(preferred.y + 0.026f, 0.078f, 0.20f) : 0.096f;
        labelBackdropTransform.localScale = new Vector3(width, height, 1f);
        if (labelIdentityPipTransform != null)
        {
            labelIdentityPipTransform.localPosition = new Vector3(-(width * 0.5f) - 0.018f, 0f, 0.008f);
            labelIdentityPipTransform.localScale = new Vector3(0.026f, height * 0.92f, 1f);
        }
    }

    /// <summary>
    /// Alias publik v5.0 untuk mengganti tema/varian diorama 3D OutputActuator berdasarkan level aktif.
    /// </summary>
    public void SetOutputDioramaTheme(int themeIndex)
    {
        SetOutputDioramaVariant(themeIndex);
    }

    /// <summary>
    /// Mengatur varian diorama 3D kontekstual pada kartu OutputActuator berdasarkan indeks level aktif.
    /// Menggunakan modulo aman agar kompatibel dengan ekspansi Level 4..7 dan Sandbox.
    /// </summary>
    public void SetOutputDioramaVariant(int variantIndex)
    {
        if (nodeType != NodeType.OutputActuator)
        {
            return;
        }

        int safeIndex = ((variantIndex % OutputDioramaVariantCount) + OutputDioramaVariantCount) % OutputDioramaVariantCount;
        activeDioramaVariantIndex = safeIndex;
        currentVaultDoorAngle = 0f;
        if (vaultDoorPivotTransform != null)
        {
            vaultDoorPivotTransform.localRotation = Quaternion.identity;
        }

        ApplyActiveDioramaVariantVisibility();
        UpdateStatusLabelPlacement();

        if (isTracked)
        {
            UpdateOutputDioramaColors();
            UpdateReactiveAccentMaterials();
            lastStairChaseStep = -2;
        }
    }

    /// <summary>
    /// Membangun kontainer OutputDiorama3D dengan 5 varian model 3D Quaternius terkurasi (fallback prosedural bila aset tidak ada)
    /// khusus untuk kartu bertipe OutputActuator.
    /// </summary>
    private void InitializeOutputDioramaVisual()
    {
        if (nodeType != NodeType.OutputActuator)
        {
            return;
        }

        dioramaBodyMaterial = CreateSafeOpenGLMaterial();
        dioramaAccentMaterial = CreateSafeOpenGLMaterial();
        dioramaEmissiveMaterial = CreateSafeOpenGLMaterial();

        outputDioramaRootObj = new GameObject("OutputDiorama3D");
        outputDioramaRootObj.transform.SetParent(transform, false);
        outputDioramaRootObj.transform.localPosition = Vector3.zero;
        outputDioramaRootObj.transform.localRotation = Quaternion.identity;
        outputDioramaRootObj.transform.localScale = Vector3.one;

        outputDioramaVariantObjs = new GameObject[OutputDioramaVariantCount];
        dioramaVariantReactiveAccentMaterials = new List<Material>[OutputDioramaVariantCount];

        for (int i = 0; i < OutputDioramaVariantCount; i++)
        {
            dioramaVariantReactiveAccentMaterials[i] = new List<Material>();
            GameObject prefab = (i == 0 && customModelPrefab != null)
                ? customModelPrefab
                : LoadV5ModelPrefab(GetOutputDioramaModelResourceName(i));

            if (prefab != null)
            {
                GameObject variantRoot = CreateEmptyChild($"Diorama_V5_{i}", outputDioramaRootObj.transform, Vector3.zero).gameObject;
                GameObject modelInstance = Instantiate(prefab);
                modelInstance.name = prefab.name;
                RemoveCollidersRecursive(modelInstance);
                SanitizeAndRegisterModelMaterials(modelInstance, dioramaVariantReactiveAccentMaterials[i]);
                NormalizeModelToFitFootprint(
                    modelInstance,
                    variantRoot.transform,
                    0.52f,
                    0.46f,
                    BaseTileTopSurfaceY,
                    GetOutputDioramaInitialEuler(i),
                    out float modelTopY,
                    out Vector3 modelHalfExtents);
                dioramaVariantModelTopY[i] = modelTopY;
                BuildV5OutputDioramaMechanism(i, variantRoot.transform, modelTopY, modelHalfExtents, dioramaVariantReactiveAccentMaterials[i]);
                outputDioramaVariantObjs[i] = variantRoot;
            }
            else
            {
                dioramaVariantModelTopY[i] = 0f;
                outputDioramaVariantObjs[i] = BuildFallbackOutputDioramaVariant(i, outputDioramaRootObj.transform);
            }
        }

        ApplyActiveDioramaVariantVisibility();
        UpdateStatusLabelPlacement();
        UpdateOutputDioramaColors();
        UpdateReactiveAccentMaterials();
        outputDioramaRootObj.SetActive(false);
    }

    private GameObject BuildFallbackOutputDioramaVariant(int variantIndex, Transform root)
    {
        switch (variantIndex)
        {
            case 0: return BuildSirenDiorama(root);
            case 1: return BuildStaircaseLanternDiorama(root);
            case 2: return BuildVaultDiorama(root);
            case 3: return BuildReactorContainmentDiorama(root);
            default: return BuildTwinSyncObeliskDiorama(root);
        }
    }

    private GameObject BuildSirenDiorama(Transform root)
    {
        GameObject sirenRoot = CreateEmptyChild("Diorama_0_EmergencySiren", root, Vector3.zero).gameObject;
        Transform parent = sirenRoot.transform;

        // Alas heksagonal/silinder bertingkat + cincin dasar
        CreatePrimitivePart("Siren_BaseLower", PrimitiveType.Cylinder, parent,
            new Vector3(0f, 0.036f, 0f), Vector3.zero, new Vector3(0.176f, 0.006f, 0.176f), dioramaBodyMaterial);
        CreatePrimitivePart("Siren_BaseUpper", PrimitiveType.Cylinder, parent,
            new Vector3(0f, 0.046f, 0f), Vector3.zero, new Vector3(0.136f, 0.005f, 0.136f), dioramaAccentMaterial);
        CreateProceduralMeshPart("Siren_BaseHaloRing", GetSharedTorusMesh(), parent,
            new Vector3(0f, 0.050f, 0f), Vector3.zero, Vector3.one * 0.086f, dioramaEmissiveMaterial);

        // Tiang penyangga utama
        CreatePrimitivePart("Siren_Pedestal", PrimitiveType.Cylinder, parent,
            new Vector3(0f, 0.088f, 0f), Vector3.zero, new Vector3(0.044f, 0.040f, 0.044f), dioramaBodyMaterial);

        // Bingkai sangkar pelindung sirine (4 batang vertikal, 2 cincin pengikat, & topi atas)
        CreatePrimitivePart("Siren_CagePostF", PrimitiveType.Cube, parent,
            new Vector3(0f, 0.140f, 0.056f), Vector3.zero, new Vector3(0.011f, 0.068f, 0.011f), dioramaAccentMaterial);
        CreatePrimitivePart("Siren_CagePostB", PrimitiveType.Cube, parent,
            new Vector3(0f, 0.140f, -0.056f), Vector3.zero, new Vector3(0.011f, 0.068f, 0.011f), dioramaAccentMaterial);
        CreatePrimitivePart("Siren_CagePostL", PrimitiveType.Cube, parent,
            new Vector3(-0.056f, 0.140f, 0f), Vector3.zero, new Vector3(0.011f, 0.068f, 0.011f), dioramaAccentMaterial);
        CreatePrimitivePart("Siren_CagePostR", PrimitiveType.Cube, parent,
            new Vector3(0.056f, 0.140f, 0f), Vector3.zero, new Vector3(0.011f, 0.068f, 0.011f), dioramaAccentMaterial);

        CreateProceduralMeshPart("Siren_CageLowerRing", GetSharedTorusMesh(), parent,
            new Vector3(0f, 0.106f, 0f), Vector3.zero, Vector3.one * 0.060f, dioramaAccentMaterial);
        CreateProceduralMeshPart("Siren_CageUpperRing", GetSharedTorusMesh(), parent,
            new Vector3(0f, 0.174f, 0f), Vector3.zero, Vector3.one * 0.058f, dioramaAccentMaterial);
        CreatePrimitivePart("Siren_TopCap", PrimitiveType.Cylinder, parent,
            new Vector3(0f, 0.180f, 0f), Vector3.zero, new Vector3(0.090f, 0.005f, 0.090f), dioramaBodyMaterial);

        // Bilah rotor reflektor ganda + cincin suar berdenyut
        sirenRotorPivot = CreateEmptyChild("Siren_RotorPivot", parent, new Vector3(0f, 0.140f, 0f));
        CreatePrimitivePart("Siren_BeaconCore", PrimitiveType.Sphere, sirenRotorPivot,
            Vector3.zero, Vector3.zero, Vector3.one * 0.044f, dioramaEmissiveMaterial);
        CreatePrimitivePart("Siren_RotorBar", PrimitiveType.Cube, sirenRotorPivot,
            Vector3.zero, Vector3.zero, new Vector3(0.132f, 0.022f, 0.016f), dioramaEmissiveMaterial);
        CreatePrimitivePart("Siren_ReflectorHeadA", PrimitiveType.Cube, sirenRotorPivot,
            new Vector3(0.058f, 0f, 0.012f), new Vector3(0f, 25f, 0f), new Vector3(0.024f, 0.032f, 0.024f), dioramaEmissiveMaterial);
        CreatePrimitivePart("Siren_ReflectorHeadB", PrimitiveType.Cube, sirenRotorPivot,
            new Vector3(-0.058f, 0f, -0.012f), new Vector3(0f, 25f, 0f), new Vector3(0.024f, 0.032f, 0.024f), dioramaEmissiveMaterial);

        GameObject pulseRingObj = CreateProceduralMeshPart("Siren_PulseRing", GetSharedTorusMesh(), parent,
            new Vector3(0f, 0.140f, 0f), Vector3.zero, Vector3.one * 0.082f, dioramaEmissiveMaterial);
        sirenPulseRingTransform = pulseRingObj.transform;
        sirenPulseRingBaseScale = sirenPulseRingTransform.localScale;

        return sirenRoot;
    }

    private GameObject BuildStaircaseLanternDiorama(Transform root)
    {
        GameObject stairRoot = CreateEmptyChild("Diorama_1_TwinStaircaseLantern", root, Vector3.zero).gameObject;
        Transform parent = stairRoot.transform;

        // Alas dasar + miniatur 3 anak tangga bertingkat
        CreatePrimitivePart("Stair_BasePlate", PrimitiveType.Cube, parent,
            new Vector3(0f, 0.036f, 0f), Vector3.zero, new Vector3(0.200f, 0.008f, 0.115f), dioramaBodyMaterial);
        CreatePrimitivePart("Stair_Step1", PrimitiveType.Cube, parent,
            new Vector3(-0.058f, 0.050f, 0f), Vector3.zero, new Vector3(0.064f, 0.020f, 0.088f), dioramaAccentMaterial);
        CreatePrimitivePart("Stair_Step2", PrimitiveType.Cube, parent,
            new Vector3(0f, 0.066f, 0f), Vector3.zero, new Vector3(0.064f, 0.036f, 0.088f), dioramaBodyMaterial);
        CreatePrimitivePart("Stair_Step3", PrimitiveType.Cube, parent,
            new Vector3(0.058f, 0.082f, 0f), Vector3.zero, new Vector3(0.064f, 0.052f, 0.088f), dioramaAccentMaterial);

        // Tiang lentera ujung bawah (kiri)
        CreatePrimitivePart("Lantern_LowerPost", PrimitiveType.Cylinder, parent,
            new Vector3(-0.072f, 0.104f, 0f), Vector3.zero, new Vector3(0.014f, 0.044f, 0.014f), dioramaBodyMaterial);
        CreatePrimitivePart("Lantern_LowerHead", PrimitiveType.Sphere, parent,
            new Vector3(-0.072f, 0.156f, 0f), Vector3.zero, Vector3.one * 0.036f, dioramaEmissiveMaterial);
        CreatePrimitivePart("Lantern_LowerCap", PrimitiveType.Cylinder, parent,
            new Vector3(-0.072f, 0.177f, 0f), Vector3.zero, new Vector3(0.046f, 0.004f, 0.046f), dioramaBodyMaterial);
        stairHaloLowerTransform = CreateLanternHaloAssembly("Lantern_LowerHalo", parent, new Vector3(-0.072f, 0.156f, 0f), dioramaAccentMaterial);

        // Tiang lentera ujung atas (kanan)
        CreatePrimitivePart("Lantern_UpperPost", PrimitiveType.Cylinder, parent,
            new Vector3(0.072f, 0.144f, 0f), Vector3.zero, new Vector3(0.014f, 0.036f, 0.014f), dioramaBodyMaterial);
        CreatePrimitivePart("Lantern_UpperHead", PrimitiveType.Sphere, parent,
            new Vector3(0.072f, 0.188f, 0f), Vector3.zero, Vector3.one * 0.036f, dioramaEmissiveMaterial);
        CreatePrimitivePart("Lantern_UpperCap", PrimitiveType.Cylinder, parent,
            new Vector3(0.072f, 0.209f, 0f), Vector3.zero, new Vector3(0.046f, 0.004f, 0.046f), dioramaBodyMaterial);
        stairHaloUpperTransform = CreateLanternHaloAssembly("Lantern_UpperHalo", parent, new Vector3(0.072f, 0.188f, 0f), dioramaAccentMaterial);

        return stairRoot;
    }

    private Transform CreateLanternHaloAssembly(string name, Transform parent, Vector3 localPos, Material haloMat)
    {
        Transform haloPivot = CreateEmptyChild(name, parent, localPos);
        CreateProceduralMeshPart($"{name}_Ring", GetSharedTorusMesh(), haloPivot,
            Vector3.zero, Vector3.zero, Vector3.one * 0.042f, haloMat);
        CreatePrimitivePart($"{name}_NodeA", PrimitiveType.Sphere, haloPivot,
            new Vector3(0.042f, 0f, 0f), Vector3.zero, Vector3.one * 0.012f, haloMat);
        CreatePrimitivePart($"{name}_NodeB", PrimitiveType.Sphere, haloPivot,
            new Vector3(-0.042f, 0f, 0f), Vector3.zero, Vector3.one * 0.012f, haloMat);
        return haloPivot;
    }

    private GameObject BuildVaultDiorama(Transform root)
    {
        GameObject vaultRoot = CreateEmptyChild("Diorama_2_MechanicalVaultDoor", root, Vector3.zero).gameObject;
        Transform parent = vaultRoot.transform;

        // Bingkai bodi brankas 3D (menghadap kamera ke arah -Z)
        CreatePrimitivePart("Vault_Base", PrimitiveType.Cube, parent,
            new Vector3(0f, 0.040f, 0f), Vector3.zero, new Vector3(0.190f, 0.010f, 0.155f), dioramaBodyMaterial);
        CreatePrimitivePart("Vault_BackWall", PrimitiveType.Cube, parent,
            new Vector3(0f, 0.112f, 0.056f), Vector3.zero, new Vector3(0.175f, 0.135f, 0.022f), dioramaBodyMaterial);
        CreatePrimitivePart("Vault_LeftWall", PrimitiveType.Cube, parent,
            new Vector3(-0.076f, 0.112f, 0f), Vector3.zero, new Vector3(0.022f, 0.135f, 0.125f), dioramaBodyMaterial);
        CreatePrimitivePart("Vault_RightWall", PrimitiveType.Cube, parent,
            new Vector3(0.076f, 0.112f, 0f), Vector3.zero, new Vector3(0.022f, 0.135f, 0.125f), dioramaBodyMaterial);
        CreatePrimitivePart("Vault_TopRoof", PrimitiveType.Cube, parent,
            new Vector3(0f, 0.180f, 0f), Vector3.zero, new Vector3(0.175f, 0.020f, 0.125f), dioramaBodyMaterial);

        // Pedestal & kristal berlian di dalam ruang brankas
        CreatePrimitivePart("Vault_InnerPedestal", PrimitiveType.Cylinder, parent,
            new Vector3(0f, 0.050f, 0.002f), Vector3.zero, new Vector3(0.064f, 0.006f, 0.064f), dioramaAccentMaterial);
        GameObject diamondObj = CreateProceduralMeshPart("Vault_CoreDiamond", GetSharedDiamondSpireMesh(), parent,
            new Vector3(0f, 0.110f, 0.002f), Vector3.zero, new Vector3(0.042f, 0.046f, 0.042f), dioramaEmissiveMaterial);
        vaultDiamondTransform = diamondObj.transform;
        vaultDiamondBaseScale = vaultDiamondTransform.localScale;

        // Poros engsel sisi kiri depan + daun pintu brankas + roda kombinasi 4-palang
        vaultDoorPivotTransform = CreateEmptyChild("VaultDoorPivot", parent, new Vector3(-0.065f, 0.112f, -0.058f));
        CreatePrimitivePart("Vault_HingePin", PrimitiveType.Cylinder, vaultDoorPivotTransform,
            Vector3.zero, Vector3.zero, new Vector3(0.016f, 0.064f, 0.016f), dioramaAccentMaterial);
        CreatePrimitivePart("Vault_DoorLeaf", PrimitiveType.Cube, vaultDoorPivotTransform,
            new Vector3(0.065f, 0f, 0f), Vector3.zero, new Vector3(0.130f, 0.124f, 0.016f), dioramaAccentMaterial);

        vaultWheelTransform = CreateEmptyChild("VaultWheel", vaultDoorPivotTransform, new Vector3(0.065f, 0f, -0.014f));
        CreatePrimitivePart("VaultWheel_Hub", PrimitiveType.Cylinder, vaultWheelTransform,
            Vector3.zero, new Vector3(90f, 0f, 0f), new Vector3(0.022f, 0.009f, 0.022f), dioramaBodyMaterial);
        CreateProceduralMeshPart("VaultWheel_Rim", GetSharedTorusMesh(), vaultWheelTransform,
            Vector3.zero, new Vector3(90f, 0f, 0f), Vector3.one * 0.036f, dioramaEmissiveMaterial);
        CreatePrimitivePart("VaultWheel_SpokeH", PrimitiveType.Cube, vaultWheelTransform,
            Vector3.zero, Vector3.zero, new Vector3(0.068f, 0.009f, 0.009f), dioramaEmissiveMaterial);
        CreatePrimitivePart("VaultWheel_SpokeV", PrimitiveType.Cube, vaultWheelTransform,
            Vector3.zero, Vector3.zero, new Vector3(0.009f, 0.068f, 0.009f), dioramaEmissiveMaterial);

        currentVaultDoorAngle = 0f;
        return vaultRoot;
    }

    private GameObject BuildReactorContainmentDiorama(Transform root)
    {
        GameObject reactorRoot = CreateEmptyChild("Diorama_3_ReactorContainment", root, Vector3.zero).gameObject;
        Transform parent = reactorRoot.transform;

        // Alas reaktor & cincin dasar
        CreatePrimitivePart("Reactor_BasePad", PrimitiveType.Cylinder, parent,
            new Vector3(0f, 0.036f, 0f), Vector3.zero, new Vector3(0.172f, 0.005f, 0.172f), dioramaBodyMaterial);
        CreateProceduralMeshPart("Reactor_BaseRing", GetSharedTorusMesh(), parent,
            new Vector3(0f, 0.042f, 0f), Vector3.zero, Vector3.one * 0.082f, dioramaAccentMaterial);

        // Kolom rangka pelindung 4 sudut & cincin pengikat atas
        CreatePrimitivePart("Reactor_ColFL", PrimitiveType.Cube, parent,
            new Vector3(-0.056f, 0.118f, -0.056f), Vector3.zero, new Vector3(0.011f, 0.148f, 0.011f), dioramaBodyMaterial);
        CreatePrimitivePart("Reactor_ColFR", PrimitiveType.Cube, parent,
            new Vector3(0.056f, 0.118f, -0.056f), Vector3.zero, new Vector3(0.011f, 0.148f, 0.011f), dioramaBodyMaterial);
        CreatePrimitivePart("Reactor_ColBL", PrimitiveType.Cube, parent,
            new Vector3(-0.056f, 0.118f, 0.056f), Vector3.zero, new Vector3(0.011f, 0.148f, 0.011f), dioramaBodyMaterial);
        CreatePrimitivePart("Reactor_ColBR", PrimitiveType.Cube, parent,
            new Vector3(0.056f, 0.118f, 0.056f), Vector3.zero, new Vector3(0.011f, 0.148f, 0.011f), dioramaBodyMaterial);
        CreateProceduralMeshPart("Reactor_TopGuardRing", GetSharedTorusMesh(), parent,
            new Vector3(0f, 0.192f, 0f), Vector3.zero, Vector3.one * 0.078f, dioramaBodyMaterial);

        // 2 cincin pengungkung vertikal bersilang & bola inti reaktor di tengah
        reactorRingPivotA = CreateEmptyChild("Reactor_RingPivotA", parent, new Vector3(0f, 0.118f, 0f));
        CreateProceduralMeshPart("Reactor_ContainmentRingA", GetSharedTorusMesh(), reactorRingPivotA,
            Vector3.zero, new Vector3(82f, 0f, 0f), Vector3.one * 0.070f, dioramaAccentMaterial);

        reactorRingPivotB = CreateEmptyChild("Reactor_RingPivotB", parent, new Vector3(0f, 0.118f, 0f));
        CreateProceduralMeshPart("Reactor_ContainmentRingB", GetSharedTorusMesh(), reactorRingPivotB,
            Vector3.zero, new Vector3(-82f, 90f, 0f), Vector3.one * 0.058f, dioramaEmissiveMaterial);

        GameObject coreObj = CreatePrimitivePart("Reactor_CenterCore", PrimitiveType.Sphere, parent,
            new Vector3(0f, 0.118f, 0f), Vector3.zero, Vector3.one * 0.056f, dioramaEmissiveMaterial);
        reactorCoreTransform = coreObj.transform;
        reactorCoreBaseScale = reactorCoreTransform.localScale;

        return reactorRoot;
    }

    private GameObject BuildTwinSyncObeliskDiorama(Transform root)
    {
        GameObject obeliskRoot = CreateEmptyChild("Diorama_4_TwinSyncObelisk", root, Vector3.zero).gameObject;
        Transform parent = obeliskRoot.transform;

        // Alas podium & dua dudukan kiri-kanan
        CreatePrimitivePart("Obelisk_PodiumBase", PrimitiveType.Cube, parent,
            new Vector3(0f, 0.036f, 0f), Vector3.zero, new Vector3(0.205f, 0.008f, 0.105f), dioramaBodyMaterial);
        CreatePrimitivePart("Obelisk_PedestalL", PrimitiveType.Cylinder, parent,
            new Vector3(-0.064f, 0.045f, 0f), Vector3.zero, new Vector3(0.068f, 0.005f, 0.068f), dioramaAccentMaterial);
        CreatePrimitivePart("Obelisk_PedestalR", PrimitiveType.Cylinder, parent,
            new Vector3(0.064f, 0.045f, 0f), Vector3.zero, new Vector3(0.068f, 0.005f, 0.068f), dioramaAccentMaterial);

        // 2 menara kristal berlian kembar di sisi kiri & kanan
        GameObject crystalL = CreateProceduralMeshPart("Obelisk_TwinCrystalL", GetSharedDiamondSpireMesh(), parent,
            new Vector3(-0.064f, 0.115f, 0f), Vector3.zero, new Vector3(0.044f, 0.066f, 0.044f), dioramaEmissiveMaterial);
        GameObject crystalR = CreateProceduralMeshPart("Obelisk_TwinCrystalR", GetSharedDiamondSpireMesh(), parent,
            new Vector3(0.064f, 0.115f, 0f), Vector3.zero, new Vector3(0.044f, 0.066f, 0.044f), dioramaEmissiveMaterial);
        twinCrystalLeftTransform = crystalL.transform;
        twinCrystalRightTransform = crystalR.transform;
        twinCrystalBaseScale = twinCrystalLeftTransform.localScale;

        // Palang jembatan & cincin halo sinkronisasi di tengah
        CreatePrimitivePart("Obelisk_SyncBridgeBar", PrimitiveType.Cube, parent,
            new Vector3(0f, 0.115f, 0f), Vector3.zero, new Vector3(0.118f, 0.010f, 0.010f), dioramaAccentMaterial);

        twinBridgeHaloTransform = CreateEmptyChild("Obelisk_SyncHaloPivot", parent, new Vector3(0f, 0.115f, 0f));
        CreateProceduralMeshPart("Obelisk_SyncHaloRing", GetSharedTorusMesh(), twinBridgeHaloTransform,
            Vector3.zero, new Vector3(90f, 0f, 0f), Vector3.one * 0.044f, dioramaEmissiveMaterial);
        CreatePrimitivePart("Obelisk_SyncNodeA", PrimitiveType.Sphere, twinBridgeHaloTransform,
            new Vector3(0.044f, 0f, 0f), Vector3.zero, Vector3.one * 0.014f, dioramaAccentMaterial);
        CreatePrimitivePart("Obelisk_SyncNodeB", PrimitiveType.Sphere, twinBridgeHaloTransform,
            new Vector3(-0.044f, 0f, 0f), Vector3.zero, Vector3.one * 0.014f, dioramaAccentMaterial);
        CreatePrimitivePart("Obelisk_SyncCenterHub", PrimitiveType.Sphere, twinBridgeHaloTransform,
            Vector3.zero, Vector3.zero, Vector3.one * 0.024f, dioramaAccentMaterial);
        twinBridgeBaseScale = twinBridgeHaloTransform.localScale;

        return obeliskRoot;
    }

    private void ApplyActiveDioramaVariantVisibility()
    {
        if (outputDioramaVariantObjs == null)
        {
            return;
        }

        for (int i = 0; i < outputDioramaVariantObjs.Length; i++)
        {
            if (outputDioramaVariantObjs[i] != null)
            {
                bool shouldShow = (i == activeDioramaVariantIndex);
                if (outputDioramaVariantObjs[i].activeSelf != shouldShow)
                {
                    outputDioramaVariantObjs[i].SetActive(shouldShow);
                }
            }
        }
    }

    private void SetOutputDioramaActive(bool active)
    {
        if (outputDioramaRootObj != null && outputDioramaRootObj.activeSelf != active)
        {
            outputDioramaRootObj.SetActive(active);
        }
    }

    private void UpdateOutputDioramaColors()
    {
        if (outputDioramaRootObj == null || nodeType != NodeType.OutputActuator)
        {
            return;
        }

        if (!currentOutputValue)
        {
            if (dioramaBodyMaterial != null) dioramaBodyMaterial.color = LowDioramaBodyColor;
            if (dioramaAccentMaterial != null) dioramaAccentMaterial.color = LowDioramaAccentColor;
            if (dioramaEmissiveMaterial != null) dioramaEmissiveMaterial.color = LowDioramaEmissiveColor;
            return;
        }

        if (dioramaBodyMaterial != null)
        {
            dioramaBodyMaterial.color = HighDioramaBodyColor;
        }

        if (activeDioramaVariantIndex == 1)
        {
            // Level 2: Kepala lentera kuning terang & cincin halo sian terang
            if (dioramaEmissiveMaterial != null) dioramaEmissiveMaterial.color = HighStairLanternYellowColor;
            if (dioramaAccentMaterial != null) dioramaAccentMaterial.color = HighStairHaloCyanColor;
        }
        else
        {
            if (dioramaAccentMaterial != null) dioramaAccentMaterial.color = HighDioramaAccentColor;
            if (dioramaEmissiveMaterial != null) dioramaEmissiveMaterial.color = HighDioramaEmissiveColor;
        }
    }

    private void AnimateOutputDioramaVisual()
    {
        if (outputDioramaRootObj == null || nodeType != NodeType.OutputActuator)
        {
            return;
        }

        SetOutputDioramaActive(true);

        switch (activeDioramaVariantIndex)
        {
            case 0:
                // Diorama 0 (Sirine Darurat): rotor berputar 360°/detik dan cincin suar berdenyut saat HIGH
                if (sirenRotorPivot != null && currentOutputValue)
                {
                    sirenRotorPivot.Rotate(Vector3.up, sirenRotorSpeed * Time.deltaTime, Space.Self);
                }
                if (sirenPulseRingTransform != null)
                {
                    float ringPulse = currentOutputValue ? (1f + 0.22f * Mathf.Sin(Time.time * 9f)) : 1f;
                    sirenPulseRingTransform.localScale = sirenPulseRingBaseScale * ringPulse;
                }
                break;

            case 1:
                // Diorama 1 (Sakelar Tangga): cincin halo di atas lentera bawah & atas berputar halus saat HIGH
                if (currentOutputValue)
                {
                    if (stairHaloLowerTransform != null)
                    {
                        stairHaloLowerTransform.Rotate(Vector3.up, 95f * Time.deltaTime, Space.Self);
                    }
                    if (stairHaloUpperTransform != null)
                    {
                        stairHaloUpperTransform.Rotate(Vector3.up, -95f * Time.deltaTime, Space.Self);
                    }
                }
                break;

            case 2:
                // Diorama 2 (Brankas Mekanis): roda berputar, pintu berayun ke 75° / menutup ke 0°, berlian berputar
                float targetDoorAngle = currentOutputValue ? vaultOpenAngle : 0f;
                currentVaultDoorAngle = Mathf.MoveTowards(currentVaultDoorAngle, targetDoorAngle, 150f * Time.deltaTime);

                if (vaultDoorPivotTransform != null)
                {
                    vaultDoorPivotTransform.localRotation = Quaternion.Euler(0f, currentVaultDoorAngle, 0f);
                }

                if (vaultWheelTransform != null && currentOutputValue)
                {
                    vaultWheelTransform.Rotate(Vector3.forward, 260f * Time.deltaTime, Space.Self);
                }

                if (vaultDiamondTransform != null)
                {
                    if (currentOutputValue)
                    {
                        vaultDiamondTransform.Rotate(Vector3.up, 180f * Time.deltaTime, Space.Self);
                        float diamondPulse = 1f + 0.14f * Mathf.Sin(Time.time * 6f);
                        vaultDiamondTransform.localScale = vaultDiamondBaseScale * diamondPulse;
                    }
                    else
                    {
                        vaultDiamondTransform.localScale = vaultDiamondBaseScale;
                    }
                }
                break;

            case 3:
                // Diorama 3 (Reaktor NAND): cepat memancarkan energi saat HIGH=1, melambat aman/stabil saat LOW=0
                float reactorRingSpeed = currentOutputValue ? 210f : 22f;
                if (reactorRingPivotA != null)
                {
                    reactorRingPivotA.Rotate(new Vector3(0.2f, 1f, 0.1f), reactorRingSpeed * Time.deltaTime, Space.Self);
                }
                if (reactorRingPivotB != null)
                {
                    reactorRingPivotB.Rotate(new Vector3(-0.2f, -1f, 0.1f), reactorRingSpeed * 1.15f * Time.deltaTime, Space.Self);
                }
                if (reactorCoreTransform != null)
                {
                    float corePulse = currentOutputValue ? (1f + 0.16f * Mathf.Sin(Time.time * 7f)) : 1f;
                    reactorCoreTransform.localScale = reactorCoreBaseScale * corePulse;
                }
                break;

            case 4:
                // Diorama 4 (Obelisk Kembar Level 5 & Sandbox): kristal kembar & cincin sinkronisasi berputar selaras saat HIGH
                float syncSpeed = currentOutputValue ? 190f : 20f;
                twinSyncSpinPhase = Mathf.Repeat(twinSyncSpinPhase + syncSpeed * Time.deltaTime, 360f);

                Quaternion syncRot = Quaternion.Euler(0f, twinSyncSpinPhase, 0f);
                float syncPulse = currentOutputValue ? (1f + 0.14f * Mathf.Sin(Time.time * 6.5f)) : 1f;

                if (twinCrystalLeftTransform != null)
                {
                    twinCrystalLeftTransform.localRotation = syncRot;
                    twinCrystalLeftTransform.localScale = twinCrystalBaseScale * syncPulse;
                }
                if (twinCrystalRightTransform != null)
                {
                    twinCrystalRightTransform.localRotation = syncRot;
                    twinCrystalRightTransform.localScale = twinCrystalBaseScale * syncPulse;
                }
                if (twinBridgeHaloTransform != null)
                {
                    twinBridgeHaloTransform.localRotation = syncRot;
                    twinBridgeHaloTransform.localScale = twinBridgeBaseScale * syncPulse;
                }
                break;
        }
    }

    /// <summary>
    /// Membangun totem reaktor hologram 3D prosedural (tanpa Collider) sesuai identitas kartu
    /// untuk InputSource dan LogicGate. OutputActuator dibiarkan untuk diorama Hari ke-13.
    /// </summary>
    private void InitializeTotemVisual()
    {
        if (nodeType == NodeType.OutputActuator)
        {
            return;
        }

        totemCoreMaterial = CreateSafeOpenGLMaterial();
        totemAccentMaterial = CreateSafeOpenGLMaterial();

        totemRootObj = new GameObject("ReactorTotem3D");
        totemRootObj.transform.SetParent(transform, false);
        totemRootObj.transform.localPosition = Vector3.zero;
        totemRootObj.transform.localRotation = Quaternion.identity;
        totemRootObj.transform.localScale = Vector3.one;

        // v7.0 Fase 1: 5 gerbang logika menggunakan mekanisme Open Cutaway (tanpa menumpuk FBX tertutup v5)
        if (customModelPrefab == null && IsLogicGateNodeType(nodeType))
        {
            mechanismVisual = GetComponent<GateMechanismVisual>();
            if (mechanismVisual == null)
            {
                mechanismVisual = gameObject.AddComponent<GateMechanismVisual>();
            }

            mechanismVisual.Build(
                nodeType,
                totemRootObj.transform,
                BaseTileTopSurfaceY,
                GetSharedTorusMesh(),
                GetSharedDiamondSpireMesh(),
                out float cutawayTopY);
            normalizedModelTopY = cutawayTopY;
            UpdateStatusLabelPlacement();
            totemRootObj.SetActive(false);
            return;
        }

        GameObject curatedPrefab = customModelPrefab != null
            ? customModelPrefab
            : LoadV5ModelPrefab(GetNodeModelResourceName(nodeType));

        if (curatedPrefab != null)
        {
            GameObject modelInstance = Instantiate(curatedPrefab);
            modelInstance.name = curatedPrefab.name;
            RemoveCollidersRecursive(modelInstance);
            SanitizeAndRegisterModelMaterials(modelInstance, totemReactiveAccentMaterials);

            float maxFootprintXZ = (nodeType == NodeType.InputSource) ? 0.44f : 0.48f;
            float maxHeightY = (nodeType == NodeType.InputSource) ? 0.38f : 0.42f;
            NormalizeModelToFitFootprint(
                modelInstance,
                totemRootObj.transform,
                maxFootprintXZ,
                maxHeightY,
                BaseTileTopSurfaceY,
                GetNodeModelInitialEuler(nodeType),
                out float modelTopY,
                out Vector3 modelHalfExtents,
                forceLongestAxisVertical: (nodeType == NodeType.NAND));
            normalizedModelTopY = modelTopY;
            UpdateStatusLabelPlacement();
            BuildV5NodeMechanism(nodeType, totemRootObj.transform, modelTopY, modelHalfExtents, totemReactiveAccentMaterials);
        }
        else
        {
            switch (nodeType)
            {
                case NodeType.InputSource:
                    BuildInputSourceTotem(totemRootObj.transform);
                    break;
                case NodeType.AND:
                    BuildAndTotem(totemRootObj.transform);
                    break;
                case NodeType.OR:
                    BuildOrTotem(totemRootObj.transform);
                    break;
                case NodeType.XOR:
                    BuildXorTotem(totemRootObj.transform);
                    break;
                case NodeType.NAND:
                    BuildNandTotem(totemRootObj.transform);
                    break;
                case NodeType.NOT:
                    BuildNotTotem(totemRootObj.transform);
                    break;
            }
        }

        if (totemFloatGroup != null)
        {
            totemFloatBasePos = totemFloatGroup.localPosition;
        }

        if (totemHarmonicMoveTransform != null)
        {
            totemHarmonicBasePos = totemHarmonicMoveTransform.localPosition;
            totemHarmonicBaseScale = totemHarmonicMoveTransform.localScale;
        }

        UpdateTotemColors();
        UpdateReactiveAccentMaterials();
        totemRootObj.SetActive(false);
    }

    private void BuildInputSourceTotem(Transform root)
    {
        // Cincin & bantalan alas tipis
        CreatePrimitivePart("Input_BasePad", PrimitiveType.Cylinder, root,
            new Vector3(0f, 0.040f, 0f), Vector3.zero, new Vector3(0.105f, 0.004f, 0.105f), totemAccentMaterial);
        CreateProceduralMeshPart("Input_BaseRing", GetSharedTorusMesh(), root,
            new Vector3(0f, 0.043f, 0f), Vector3.zero, Vector3.one * 0.062f, totemAccentMaterial);

        // Kluster mengapung: inti kubus berlian miring 45° + 2 satelit orbit horizontal
        totemFloatGroup = CreateEmptyChild("Input_FloatCluster", root, new Vector3(0f, 0.105f, 0f));
        CreatePrimitivePart("Input_DiamondCore", PrimitiveType.Cube, totemFloatGroup,
            Vector3.zero, new Vector3(45f, 0f, 45f), Vector3.one * 0.050f, totemCoreMaterial);

        totemPrimarySpinTransform = CreateEmptyChild("Input_OrbitPivot", totemFloatGroup, Vector3.zero);
        CreateProceduralMeshPart("Input_OrbitTrack", GetSharedTorusMesh(), totemPrimarySpinTransform,
            Vector3.zero, Vector3.zero, Vector3.one * 0.072f, totemAccentMaterial);
        CreatePrimitivePart("Input_SatelliteA", PrimitiveType.Sphere, totemPrimarySpinTransform,
            new Vector3(0.072f, 0f, 0f), Vector3.zero, Vector3.one * 0.024f, totemAccentMaterial);
        CreatePrimitivePart("Input_SatelliteB", PrimitiveType.Sphere, totemPrimarySpinTransform,
            new Vector3(-0.072f, 0f, 0f), Vector3.zero, Vector3.one * 0.024f, totemAccentMaterial);
    }

    private void BuildAndTotem(Transform root)
    {
        // Alas penghubung dua pilar
        CreatePrimitivePart("AND_BasePlate", PrimitiveType.Cube, root,
            new Vector3(0f, 0.042f, 0f), Vector3.zero, new Vector3(0.145f, 0.008f, 0.056f), totemAccentMaterial);

        // Menara kembar konvergensi (2 pilar kiri-kanan + palang energi + bola inti tengah)
        totemPrimarySpinTransform = CreateEmptyChild("AND_TowerPivot", root, Vector3.zero);
        CreatePrimitivePart("AND_LeftPillar", PrimitiveType.Cube, totemPrimarySpinTransform,
            new Vector3(-0.056f, 0.100f, 0f), Vector3.zero, new Vector3(0.026f, 0.112f, 0.026f), totemAccentMaterial);
        CreatePrimitivePart("AND_RightPillar", PrimitiveType.Cube, totemPrimarySpinTransform,
            new Vector3(0.056f, 0.100f, 0f), Vector3.zero, new Vector3(0.026f, 0.112f, 0.026f), totemAccentMaterial);

        totemFloatGroup = CreateEmptyChild("AND_CoreCluster", totemPrimarySpinTransform, new Vector3(0f, 0.102f, 0f));
        CreatePrimitivePart("AND_EnergyBridge", PrimitiveType.Cube, totemFloatGroup,
            Vector3.zero, Vector3.zero, new Vector3(0.114f, 0.013f, 0.013f), totemAccentMaterial);

        GameObject coreSphere = CreatePrimitivePart("AND_CenterCore", PrimitiveType.Sphere, totemFloatGroup,
            Vector3.zero, Vector3.zero, Vector3.one * 0.054f, totemCoreMaterial);
        totemHarmonicMoveTransform = coreSphere.transform;
    }

    private void BuildOrTotem(Transform root)
    {
        CreatePrimitivePart("OR_BasePad", PrimitiveType.Cylinder, root,
            new Vector3(0f, 0.042f, 0f), Vector3.zero, new Vector3(0.084f, 0.004f, 0.084f), totemAccentMaterial);

        // Wide Halo Collector: bola pusat + 2 cincin sabuk miring bersilang + 2 bilah sayap penangkap sinyal
        totemFloatGroup = CreateEmptyChild("OR_CollectorCluster", root, new Vector3(0f, 0.105f, 0f));
        GameObject coreSphere = CreatePrimitivePart("OR_CenterCore", PrimitiveType.Sphere, totemFloatGroup,
            Vector3.zero, Vector3.zero, Vector3.one * 0.052f, totemCoreMaterial);
        totemHarmonicMoveTransform = coreSphere.transform;

        CreatePrimitivePart("OR_LeftWing", PrimitiveType.Cube, totemFloatGroup,
            new Vector3(-0.068f, 0.004f, 0f), new Vector3(0f, 0f, 26f), new Vector3(0.032f, 0.008f, 0.054f), totemAccentMaterial);
        CreatePrimitivePart("OR_RightWing", PrimitiveType.Cube, totemFloatGroup,
            new Vector3(0.068f, 0.004f, 0f), new Vector3(0f, 0f, -26f), new Vector3(0.032f, 0.008f, 0.054f), totemAccentMaterial);

        totemPrimarySpinTransform = CreateEmptyChild("OR_HaloSpinA", totemFloatGroup, Vector3.zero);
        CreateProceduralMeshPart("OR_HaloBeltA", GetSharedTorusMesh(), totemPrimarySpinTransform,
            Vector3.zero, new Vector3(38f, 0f, 24f), Vector3.one * 0.068f, totemAccentMaterial);

        totemSecondarySpinTransform = CreateEmptyChild("OR_HaloSpinB", totemFloatGroup, Vector3.zero);
        CreateProceduralMeshPart("OR_HaloBeltB", GetSharedTorusMesh(), totemSecondarySpinTransform,
            Vector3.zero, new Vector3(-38f, 90f, -24f), Vector3.one * 0.068f, totemCoreMaterial);
    }

    private void BuildXorTotem(Transform root)
    {
        CreateProceduralMeshPart("XOR_BaseRing", GetSharedTorusMesh(), root,
            new Vector3(0f, 0.042f, 0f), Vector3.zero, Vector3.one * 0.050f, totemAccentMaterial);

        // Counter-Rotating Dual Gyro mengelilingi kristal inti tengah
        totemFloatGroup = CreateEmptyChild("XOR_GyroCluster", root, new Vector3(0f, 0.108f, 0f));
        GameObject crystalCore = CreateProceduralMeshPart("XOR_CrystalCore", GetSharedDiamondSpireMesh(), totemFloatGroup,
            Vector3.zero, Vector3.zero, new Vector3(0.040f, 0.045f, 0.040f), totemCoreMaterial);
        totemHarmonicMoveTransform = crystalCore.transform;

        totemPrimarySpinTransform = CreateEmptyChild("XOR_GyroOuterPivot", totemFloatGroup, Vector3.zero);
        CreateProceduralMeshPart("XOR_GyroOuterRing", GetSharedTorusMesh(), totemPrimarySpinTransform,
            Vector3.zero, new Vector3(62f, 0f, 0f), Vector3.one * 0.072f, totemAccentMaterial);

        totemSecondarySpinTransform = CreateEmptyChild("XOR_GyroInnerPivot", totemFloatGroup, Vector3.zero);
        CreateProceduralMeshPart("XOR_GyroInnerRing", GetSharedTorusMesh(), totemSecondarySpinTransform,
            Vector3.zero, new Vector3(-62f, 90f, 0f), Vector3.one * 0.054f, totemCoreMaterial);
    }

    private void BuildNandTotem(Transform root)
    {
        // Struktur 2 pilar seperti AND
        CreatePrimitivePart("NAND_BasePlate", PrimitiveType.Cube, root,
            new Vector3(0f, 0.042f, 0f), Vector3.zero, new Vector3(0.138f, 0.008f, 0.054f), totemAccentMaterial);

        totemPrimarySpinTransform = CreateEmptyChild("NAND_PillarPivot", root, Vector3.zero);
        CreatePrimitivePart("NAND_LeftPillar", PrimitiveType.Cube, totemPrimarySpinTransform,
            new Vector3(-0.053f, 0.090f, 0f), Vector3.zero, new Vector3(0.023f, 0.092f, 0.023f), totemAccentMaterial);
        CreatePrimitivePart("NAND_RightPillar", PrimitiveType.Cube, totemPrimarySpinTransform,
            new Vector3(0.053f, 0.090f, 0f), Vector3.zero, new Vector3(0.023f, 0.092f, 0.023f), totemAccentMaterial);
        CreatePrimitivePart("NAND_EnergyBridge", PrimitiveType.Cube, totemPrimarySpinTransform,
            new Vector3(0f, 0.090f, 0f), Vector3.zero, new Vector3(0.108f, 0.011f, 0.011f), totemAccentMaterial);
        CreatePrimitivePart("NAND_CenterCore", PrimitiveType.Sphere, totemPrimarySpinTransform,
            new Vector3(0f, 0.090f, 0f), Vector3.zero, Vector3.one * 0.046f, totemCoreMaterial);

        // Inverter cap (cincin + kubah pembalik) yang melayang berdenyut di puncaknya
        totemFloatGroup = CreateEmptyChild("NAND_InverterCapFloat", root, new Vector3(0f, 0.154f, 0f));
        totemHarmonicMoveTransform = CreateEmptyChild("NAND_InverterCapPulse", totemFloatGroup, Vector3.zero);
        CreateProceduralMeshPart("NAND_InverterRing", GetSharedTorusMesh(), totemHarmonicMoveTransform,
            Vector3.zero, Vector3.zero, Vector3.one * 0.066f, totemAccentMaterial);
        CreatePrimitivePart("NAND_InverterDome", PrimitiveType.Sphere, totemHarmonicMoveTransform,
            new Vector3(0f, 0.006f, 0f), Vector3.zero, new Vector3(0.052f, 0.024f, 0.052f), totemCoreMaterial);
    }

    private void BuildNotTotem(Transform root)
    {
        CreatePrimitivePart("NOT_BasePad", PrimitiveType.Cylinder, root,
            new Vector3(0f, 0.041f, 0f), Vector3.zero, new Vector3(0.066f, 0.003f, 0.066f), totemAccentMaterial);
        CreateProceduralMeshPart("NOT_BaseRing", GetSharedTorusMesh(), root,
            new Vector3(0f, 0.043f, 0f), Vector3.zero, Vector3.one * 0.054f, totemAccentMaterial);

        // Phase-Inverter Pyramid/Diamond + cincin pembalik fase naik-turun secara harmonik
        totemFloatGroup = CreateEmptyChild("NOT_SpireCluster", root, new Vector3(0f, 0.106f, 0f));
        GameObject spireObj = CreateProceduralMeshPart("NOT_DiamondSpire", GetSharedDiamondSpireMesh(), totemFloatGroup,
            Vector3.zero, Vector3.zero, new Vector3(0.058f, 0.058f, 0.058f), totemCoreMaterial);
        totemPrimarySpinTransform = spireObj.transform;

        GameObject phaseRing = CreateProceduralMeshPart("NOT_PhaseRing", GetSharedTorusMesh(), totemFloatGroup,
            Vector3.zero, new Vector3(8f, 0f, 0f), Vector3.one * 0.066f, totemAccentMaterial);
        totemHarmonicMoveTransform = phaseRing.transform;
    }

    private static Transform CreateEmptyChild(string childName, Transform parent, Vector3 localPos)
    {
        GameObject obj = new GameObject(childName);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPos;
        obj.transform.localRotation = Quaternion.identity;
        obj.transform.localScale = Vector3.one;
        return obj.transform;
    }

    private static GameObject CreatePrimitivePart(string partName, PrimitiveType type, Transform parent,
        Vector3 localPos, Vector3 localEuler, Vector3 localScale, Material mat)
    {
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.name = partName;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPos;
        obj.transform.localEulerAngles = localEuler;
        obj.transform.localScale = localScale;
        RemoveCollider(obj);

        Renderer rend = obj.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            if (mat != null)
            {
                rend.sharedMaterial = mat;
            }
        }

        return obj;
    }

    private static GameObject CreateProceduralMeshPart(string partName, Mesh mesh, Transform parent,
        Vector3 localPos, Vector3 localEuler, Vector3 localScale, Material mat)
    {
        GameObject obj = new GameObject(partName);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPos;
        obj.transform.localEulerAngles = localEuler;
        obj.transform.localScale = localScale;

        MeshFilter meshFilter = obj.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;

        MeshRenderer meshRenderer = obj.AddComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        if (mat != null)
        {
            meshRenderer.sharedMaterial = mat;
        }

        return obj;
    }

    private static Mesh GetSharedTorusMesh()
    {
        if (sharedTorusMesh != null)
        {
            return sharedTorusMesh;
        }

        const float MajorRadius = 1f;
        const float MinorRadius = 0.115f;
        const int RadialSegments = 24;
        const int TubularSegments = 8;

        int ringVertexCount = RadialSegments + 1;
        int tubeVertexCount = TubularSegments + 1;
        int totalVertices = ringVertexCount * tubeVertexCount;

        Vector3[] vertices = new Vector3[totalVertices];
        Vector3[] normals = new Vector3[totalVertices];
        Color[] colors = new Color[totalVertices];
        int[] triangles = new int[RadialSegments * TubularSegments * 6];

        int vertIdx = 0;
        for (int r = 0; r <= RadialSegments; r++)
        {
            float u = (r / (float)RadialSegments) * Mathf.PI * 2f;
            float cosU = Mathf.Cos(u);
            float sinU = Mathf.Sin(u);
            Vector3 center = new Vector3(cosU * MajorRadius, 0f, sinU * MajorRadius);

            for (int t = 0; t <= TubularSegments; t++)
            {
                float v = (t / (float)TubularSegments) * Mathf.PI * 2f;
                float cosV = Mathf.Cos(v);
                float sinV = Mathf.Sin(v);

                Vector3 vertex = new Vector3(
                    (MajorRadius + MinorRadius * cosV) * cosU,
                    MinorRadius * sinV,
                    (MajorRadius + MinorRadius * cosV) * sinU
                );

                Vector3 normal = (vertex - center).normalized;
                vertices[vertIdx] = vertex;
                normals[vertIdx] = normal;

                // Shading halus agar siluet 3D tetap tegas pada shader unlit OpenGLES3
                float shade = Mathf.Lerp(0.76f, 1f, 0.5f + 0.5f * normal.y);
                colors[vertIdx] = new Color(shade, shade, shade, 1f);
                vertIdx++;
            }
        }

        int triIdx = 0;
        for (int r = 0; r < RadialSegments; r++)
        {
            for (int t = 0; t < TubularSegments; t++)
            {
                int a = r * tubeVertexCount + t;
                int b = (r + 1) * tubeVertexCount + t;
                int c = (r + 1) * tubeVertexCount + (t + 1);
                int d = r * tubeVertexCount + (t + 1);

                triangles[triIdx++] = a;
                triangles[triIdx++] = b;
                triangles[triIdx++] = d;

                triangles[triIdx++] = b;
                triangles[triIdx++] = c;
                triangles[triIdx++] = d;
            }
        }

        sharedTorusMesh = new Mesh { name = "GateWeaver_SharedTorus" };
        sharedTorusMesh.vertices = vertices;
        sharedTorusMesh.normals = normals;
        sharedTorusMesh.colors = colors;
        sharedTorusMesh.triangles = triangles;
        sharedTorusMesh.RecalculateBounds();
        return sharedTorusMesh;
    }

    private static Mesh GetSharedDiamondSpireMesh()
    {
        if (sharedDiamondSpireMesh != null)
        {
            return sharedDiamondSpireMesh;
        }

        Vector3 top = new Vector3(0f, 1f, 0f);
        Vector3 bottom = new Vector3(0f, -1f, 0f);
        Vector3[] ring =
        {
            new Vector3(0.55f, 0f, 0f),
            new Vector3(0f, 0f, 0.55f),
            new Vector3(-0.55f, 0f, 0f),
            new Vector3(0f, 0f, -0.55f)
        };

        // 8 segitiga (4 sisi atas + 4 sisi bawah) dengan vertex terpisah agar faceted flat shading tegas
        Vector3[] vertices = new Vector3[24];
        Vector3[] normals = new Vector3[24];
        Color[] colors = new Color[24];
        int[] triangles = new int[24];

        float[] upperShades = { 1.00f, 0.88f, 0.96f, 0.82f };
        float[] lowerShades = { 0.78f, 0.68f, 0.75f, 0.64f };

        int v = 0;
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;

            // Sisi atas (urutan CCW dilihat dari luar)
            Vector3 pA = top;
            Vector3 pB = ring[next];
            Vector3 pC = ring[i];
            Vector3 upperNormal = Vector3.Cross(pB - pA, pC - pA).normalized;
            Color upperCol = new Color(upperShades[i], upperShades[i], upperShades[i], 1f);

            vertices[v] = pA; normals[v] = upperNormal; colors[v] = upperCol; triangles[v] = v; v++;
            vertices[v] = pB; normals[v] = upperNormal; colors[v] = upperCol; triangles[v] = v; v++;
            vertices[v] = pC; normals[v] = upperNormal; colors[v] = upperCol; triangles[v] = v; v++;

            // Sisi bawah (urutan CCW dilihat dari luar)
            Vector3 lA = bottom;
            Vector3 lB = ring[i];
            Vector3 lC = ring[next];
            Vector3 lowerNormal = Vector3.Cross(lB - lA, lC - lA).normalized;
            Color lowerCol = new Color(lowerShades[i], lowerShades[i], lowerShades[i], 1f);

            vertices[v] = lA; normals[v] = lowerNormal; colors[v] = lowerCol; triangles[v] = v; v++;
            vertices[v] = lB; normals[v] = lowerNormal; colors[v] = lowerCol; triangles[v] = v; v++;
            vertices[v] = lC; normals[v] = lowerNormal; colors[v] = lowerCol; triangles[v] = v; v++;
        }

        sharedDiamondSpireMesh = new Mesh { name = "GateWeaver_SharedDiamondSpire" };
        sharedDiamondSpireMesh.vertices = vertices;
        sharedDiamondSpireMesh.normals = normals;
        sharedDiamondSpireMesh.colors = colors;
        sharedDiamondSpireMesh.triangles = triangles;
        sharedDiamondSpireMesh.RecalculateBounds();
        return sharedDiamondSpireMesh;
    }

    private void SetTotemActive(bool active)
    {
        if (totemRootObj != null && totemRootObj.activeSelf != active)
        {
            totemRootObj.SetActive(active);
        }
    }

    private void UpdateTotemColors()
    {
        if (totemRootObj == null)
        {
            return;
        }

        if (totemCoreMaterial != null)
        {
            totemCoreMaterial.color = currentOutputValue ? HighTotemCoreColor : LowTotemCoreColor;
        }

        if (totemAccentMaterial != null)
        {
            totemAccentMaterial.color = currentOutputValue ? HighTotemAccentColor : LowTotemAccentColor;
        }
    }

    private void AnimateTotemVisual()
    {
        if (totemRootObj == null || nodeType == NodeType.OutputActuator)
        {
            return;
        }

        SetTotemActive(true);

        float spinSpeed = currentOutputValue ? totemHighSpinSpeed : totemLowSpinSpeed;
        float bobSpeed = currentOutputValue ? 3.4f : 1.15f;
        float bobAmplitude = currentOutputValue ? totemHighBobAmplitude : (totemHighBobAmplitude * 0.32f);

        totemSpinPhase += spinSpeed * Time.deltaTime;
        totemBobPhase += bobSpeed * Time.deltaTime;

        if (totemFloatGroup != null)
        {
            float bobOffset = Mathf.Sin(totemBobPhase) * bobAmplitude;
            totemFloatGroup.localPosition = totemFloatBasePos + new Vector3(0f, bobOffset, 0f);
        }

        switch (nodeType)
        {
            case NodeType.InputSource:
                if (totemPrimarySpinTransform != null)
                {
                    totemPrimarySpinTransform.Rotate(Vector3.up, spinSpeed * 1.15f * Time.deltaTime, Space.Self);
                }
                break;

            case NodeType.AND:
                if (totemPrimarySpinTransform != null)
                {
                    totemPrimarySpinTransform.Rotate(Vector3.up, spinSpeed * 0.65f * Time.deltaTime, Space.Self);
                }
                if (totemHarmonicMoveTransform != null)
                {
                    float pulse = currentOutputValue ? (1f + 0.15f * Mathf.Sin(totemBobPhase * 1.6f)) : 1f;
                    totemHarmonicMoveTransform.localScale = totemHarmonicBaseScale * pulse;
                }
                break;

            case NodeType.OR:
                if (totemPrimarySpinTransform != null)
                {
                    totemPrimarySpinTransform.Rotate(Vector3.up, spinSpeed * 0.95f * Time.deltaTime, Space.Self);
                }
                if (totemSecondarySpinTransform != null)
                {
                    totemSecondarySpinTransform.Rotate(Vector3.up, -spinSpeed * 0.85f * Time.deltaTime, Space.Self);
                }
                if (totemHarmonicMoveTransform != null)
                {
                    float pulse = currentOutputValue ? (1f + 0.12f * Mathf.Sin(totemBobPhase * 1.5f)) : 1f;
                    totemHarmonicMoveTransform.localScale = totemHarmonicBaseScale * pulse;
                }
                break;

            case NodeType.XOR:
                if (totemPrimarySpinTransform != null)
                {
                    totemPrimarySpinTransform.Rotate(new Vector3(0.25f, 1f, 0.25f), spinSpeed * Time.deltaTime, Space.Self);
                }
                if (totemSecondarySpinTransform != null)
                {
                    totemSecondarySpinTransform.Rotate(new Vector3(-0.25f, -1f, 0.25f), spinSpeed * 1.3f * Time.deltaTime, Space.Self);
                }
                if (totemHarmonicMoveTransform != null)
                {
                    totemHarmonicMoveTransform.localRotation = Quaternion.Euler(0f, totemSpinPhase * 0.8f, 0f);
                }
                break;

            case NodeType.NAND:
                if (totemPrimarySpinTransform != null)
                {
                    totemPrimarySpinTransform.Rotate(Vector3.up, spinSpeed * 0.55f * Time.deltaTime, Space.Self);
                }
                if (totemHarmonicMoveTransform != null)
                {
                    float capPulse = currentOutputValue ? (1f + 0.16f * Mathf.Sin(totemBobPhase * 1.9f)) : 1f;
                    totemHarmonicMoveTransform.localScale = totemHarmonicBaseScale * capPulse;
                }
                break;

            case NodeType.NOT:
                if (totemPrimarySpinTransform != null)
                {
                    totemPrimarySpinTransform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.Self);
                }
                if (totemHarmonicMoveTransform != null)
                {
                    float harmonicAmp = currentOutputValue ? 0.032f : 0.012f;
                    float yWave = Mathf.Sin(totemBobPhase * 1.45f) * harmonicAmp;
                    totemHarmonicMoveTransform.localPosition = totemHarmonicBasePos + new Vector3(0f, yWave, 0f);
                    totemHarmonicMoveTransform.Rotate(Vector3.up, -spinSpeed * 0.7f * Time.deltaTime, Space.Self);
                }
                break;
        }
    }

    /// <summary>
    /// Membuat 4 paket pulsa energi kristal terarah (zero-GC) dan 2 cincin terminal soket 3D secara prosedural
    /// tanpa komponen Collider agar tidak membebani sistem fisika di perangkat Android.
    /// </summary>
    private void InitializeArcAndTerminalVisuals()
    {
        if (nodeType == NodeType.OutputActuator)
        {
            return;
        }

        dataPacketMaterial = CreateSafeOpenGLMaterial();
        socketMaterial = CreateSafeOpenGLMaterial();

        Mesh boltMesh = GetSharedDiamondSpireMesh();
        dataPacketObjs = new GameObject[DataPacketCount];
        for (int i = 0; i < DataPacketCount; i++)
        {
            GameObject boltRoot = CreateEmptyChild($"EnergyBolt_{i}", transform, Vector3.zero).gameObject;
            boltRoot.transform.localScale = Vector3.one * packetLowScale;

            // Putar kristal 90 derajat pada sumbu X agar ujung tajam (+Y mesh) mengarah persis ke +Z (tangen kurva)
            CreateProceduralMeshPart(
                "BoltCrystal",
                boltMesh,
                boltRoot.transform,
                Vector3.zero,
                new Vector3(90f, 0f, 0f),
                new Vector3(0.46f, 1.65f, 0.46f),
                dataPacketMaterial);

            boltRoot.SetActive(false);
            dataPacketObjs[i] = boltRoot;
        }

        sourceSocketObj = CreateSocketRingObject("TerminalSocket_Start");
        targetSocketObj = CreateSocketRingObject("TerminalSocket_End");
    }

    private GameObject CreateSocketRingObject(string objectName)
    {
        GameObject ringObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ringObj.name = objectName;
        ringObj.transform.SetParent(transform, false);
        ringObj.transform.localScale = new Vector3(socketRingDiameter, socketRingThickness, socketRingDiameter);
        RemoveCollider(ringObj);

        Renderer ringRenderer = ringObj.GetComponent<Renderer>();
        if (ringRenderer != null)
        {
            ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ringRenderer.receiveShadows = false;
            if (socketMaterial != null)
            {
                ringRenderer.sharedMaterial = socketMaterial;
            }
        }

        ringObj.SetActive(false);
        return ringObj;
    }

    private static void RemoveCollider(GameObject obj)
    {
        Collider col = obj.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }
    }

    /// <summary>
    /// Menghitung titik pangkal P0, titik kontrol puncak P1 adaptif di atas model Quaternius,
    /// titik ujung P2 pada port fisik, indeks slot tujuan, dan normal permukaan.
    /// </summary>
    private bool TryGetBezierControlPoints(
        out Vector3 p0,
        out Vector3 p1,
        out Vector3 p2,
        out Vector3 sourceNormal,
        out Vector3 targetNormal,
        out Vector3 avgNormal,
        out int targetSlotIndex)
    {
        bool canDraw = isTracked
                       && nodeType != NodeType.OutputActuator
                       && connectedTarget != null
                       && connectedTarget.isTracked;

        if (!canDraw)
        {
            p0 = Vector3.zero;
            p1 = Vector3.zero;
            p2 = Vector3.zero;
            sourceNormal = Vector3.up;
            targetNormal = Vector3.up;
            avgNormal = Vector3.up;
            targetSlotIndex = -1;
            return false;
        }

        sourceNormal = transform.up.sqrMagnitude > 0.0001f ? transform.up.normalized : Vector3.up;
        targetNormal = connectedTarget.transform.up.sqrMagnitude > 0.0001f ? connectedTarget.transform.up.normalized : Vector3.up;

        // Tentukan indeks slot port tujuan (0 atau 1) secara konsisten sesuai urutan input pada targetNode
        targetSlotIndex = connectedTarget.connectedInputs != null ? connectedTarget.connectedInputs.IndexOf(this) : 0;
        if (targetSlotIndex < 0)
        {
            targetSlotIndex = 0;
        }

        p0 = GetOutputPortWorldPosition();
        p2 = connectedTarget.GetInputPortWorldPosition(targetSlotIndex);

        Vector3 combinedNormal = sourceNormal + targetNormal;
        avgNormal = combinedNormal.sqrMagnitude > 0.0001f ? combinedNormal.normalized : sourceNormal;

        // Ketinggian puncak lengkungan kurva adaptif 0.22f..0.34f di atas port agar melengkung anggun di atas mesin 3D
        float distance = Vector3.Distance(p0, p2);
        float targetApexHeight = Mathf.Lerp(0.22f, 0.34f, Mathf.Clamp01((distance - 0.35f) / 1.45f));
        p1 = ((p0 + p2) * 0.5f) + (avgNormal * (targetApexHeight * 2f));

        return true;
    }

    private static Vector3 EvaluateQuadraticBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        float oneMinusT = 1f - t;
        return (oneMinusT * oneMinusT * p0)
               + (2f * oneMinusT * t * p1)
               + (t * t * p2);
    }

    private static Vector3 EvaluateQuadraticBezierTangent(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        return (2f * (1f - t) * (p1 - p0)) + (2f * t * (p2 - p1));
    }

    /// <summary>
    /// Menggambar saluran energi berlapis (Inti Plasma + Selubung Transparan) memakai buffer Bezier 20 titik zero-GC,
    /// menggerakkan 4 energy bolt terarah sepanjang tangen kurva, serta memicu efek snap lock VFX saat koneksi baru terbentuk.
    /// </summary>
    private void RefreshConnectionVisuals(bool advanceFlowPhase)
    {
        if (!TryGetBezierControlPoints(
                out Vector3 p0,
                out Vector3 p1,
                out Vector3 p2,
                out Vector3 sourceNormal,
                out Vector3 targetNormal,
                out Vector3 avgNormal,
                out int targetSlotIndex))
        {
            ResetConnectionSnapState();
            SetConnectionVisualsActive(false);
            return;
        }

        // 0. Deteksi koneksi baru / pergantian port (edge-triggered) untuk efek Connection Snap Lock VFX
        if (connectedTarget != lastVisualConnectedTarget || targetSlotIndex != lastVisualTargetSlotIndex)
        {
            lastVisualConnectedTarget = connectedTarget;
            lastVisualTargetSlotIndex = targetSlotIndex;
            snapSurgeTimer = SnapSurgeDuration;
            portOutFlashTimer = SnapSurgeDuration;
            lastPortOutVisualState = -2;

            TriggerEnergyBurstAtWorldPoint(p0, SnapSurgeFlashColor, 0.85f);
            connectedTarget.TriggerInputPortSnapFlash(targetSlotIndex, p2);
        }

        if (advanceFlowPhase && snapSurgeTimer > 0f)
        {
            snapSurgeTimer = Mathf.Max(0f, snapSurgeTimer - Time.deltaTime);
        }

        float surgeT = snapSurgeTimer > 0f ? Mathf.Clamp01(snapSurgeTimer / SnapSurgeDuration) : 0f;
        float surgeWidthMultiplier = 1f + (0.65f * surgeT * surgeT);
        float highCorePulse = currentOutputValue ? (1f + (0.10f * Mathf.Sin(Time.time * 9f))) : 1f;

        // 1. Hitung 20 titik kurva Quadratic Bezier SEKALI ke buffer pra-alokasi zero-GC
        for (int i = 0; i < ArcSegmentCount; i++)
        {
            float t = i / (float)(ArcSegmentCount - 1);
            bezierPointsCache[i] = EvaluateQuadraticBezier(p0, p1, p2, t);
        }

        // 1A. Lapisan Inti Plasma (LineRenderer eksisting)
        Color baseCoreColor = currentOutputValue ? ConduitCoreHighColor : ConduitCoreLowColor;
        Color activeCoreColor = surgeT > 0f ? Color.Lerp(baseCoreColor, SnapSurgeFlashColor, surgeT * 0.85f) : baseCoreColor;
        float coreWidth = (currentOutputValue ? (0.015f * highCorePulse) : 0.008f) * surgeWidthMultiplier;

        if (lineRenderer != null)
        {
            lineRenderer.enabled = true;
            lineRenderer.positionCount = ArcSegmentCount;
            lineRenderer.widthMultiplier = coreWidth;
            lineRenderer.startColor = activeCoreColor;
            lineRenderer.endColor = activeCoreColor;
            lineRenderer.SetPositions(bezierPointsCache);
        }

        // 1B. Lapisan Selubung Energi Transparan (AR_ConduitOuterSheath ~2.7x lebih tebal)
        Color baseSheathColor = currentOutputValue ? ConduitSheathHighColor : ConduitSheathLowColor;
        Color activeSheathColor = surgeT > 0f ? Color.Lerp(baseSheathColor, SnapSurgeSheathColor, surgeT * 0.90f) : baseSheathColor;
        float sheathWidth = (currentOutputValue ? (0.042f * highCorePulse) : 0.022f) * surgeWidthMultiplier;

        if (conduitSheathRenderer != null)
        {
            conduitSheathRenderer.enabled = true;
            conduitSheathRenderer.positionCount = ArcSegmentCount;
            conduitSheathRenderer.widthMultiplier = sheathWidth;
            conduitSheathRenderer.startColor = activeSheathColor;
            conduitSheathRenderer.endColor = activeSheathColor;
            conduitSheathRenderer.SetPositions(bezierPointsCache);
        }

        // 2. Update aliran 4 paket pulsa energi kristal yang sejajar dengan tangen kurva Bezier
        if (advanceFlowPhase)
        {
            float flowSpeed = currentOutputValue ? Mathf.Max(1.45f, packetHighSpeed) : Mathf.Min(0.35f, packetLowSpeed);
            packetFlowPhase = Mathf.Repeat(packetFlowPhase + (flowSpeed * Time.deltaTime), 1f);
        }

        float boltScale = (currentOutputValue ? 0.048f : 0.030f) * (1f + (0.28f * surgeT));
        Color baseBoltColor = currentOutputValue ? EnergyBoltHighColor : EnergyBoltLowColor;
        Color boltColor = surgeT > 0f ? Color.Lerp(baseBoltColor, SnapSurgeFlashColor, surgeT) : baseBoltColor;

        if (dataPacketMaterial != null)
        {
            dataPacketMaterial.color = boltColor;
        }

        if (dataPacketObjs != null)
        {
            for (int i = 0; i < dataPacketObjs.Length; i++)
            {
                GameObject bolt = dataPacketObjs[i];
                if (bolt == null)
                {
                    continue;
                }

                if (!bolt.activeSelf)
                {
                    bolt.SetActive(true);
                }

                float phase = Mathf.Repeat(packetFlowPhase + PacketPhaseOffsets[i], 1f);
                bolt.transform.position = EvaluateQuadraticBezier(p0, p1, p2, phase);

                Vector3 tangent = EvaluateQuadraticBezierTangent(p0, p1, p2, phase);
                if (tangent.sqrMagnitude > 0.00001f)
                {
                    bolt.transform.rotation = Quaternion.LookRotation(tangent, avgNormal);
                }

                bolt.transform.localScale = Vector3.one * boltScale;
            }
        }

        // 3. Update cincin/node terminal soket 3D di titik P0 dan P2 dengan kilatan surge saat snap
        float pulseMultiplier = (currentOutputValue ? (1f + 0.18f * Mathf.Sin(Time.time * 6f)) : 1f) * (1f + (0.35f * surgeT));
        Vector3 socketScale = new Vector3(
            socketRingDiameter * pulseMultiplier,
            socketRingThickness,
            socketRingDiameter * pulseMultiplier
        );

        if (socketMaterial != null)
        {
            Color baseSocket = currentOutputValue ? HighSocketColor : LowSocketColor;
            socketMaterial.color = surgeT > 0f ? Color.Lerp(baseSocket, SnapSurgeFlashColor, surgeT) : baseSocket;
        }

        if (sourceSocketObj != null)
        {
            if (!sourceSocketObj.activeSelf)
            {
                sourceSocketObj.SetActive(true);
            }

            sourceSocketObj.transform.position = p0;
            sourceSocketObj.transform.up = sourceNormal;
            sourceSocketObj.transform.localScale = socketScale;
        }

        if (targetSocketObj != null)
        {
            if (!targetSocketObj.activeSelf)
            {
                targetSocketObj.SetActive(true);
            }

            targetSocketObj.transform.position = p2;
            targetSocketObj.transform.up = targetNormal;
            targetSocketObj.transform.localScale = socketScale;
        }
    }

    private void SetConnectionVisualsActive(bool active)
    {
        if (lineRenderer != null && lineRenderer.enabled != active)
        {
            lineRenderer.enabled = active;
        }

        if (conduitSheathRenderer != null && conduitSheathRenderer.enabled != active)
        {
            conduitSheathRenderer.enabled = active;
        }

        if (dataPacketObjs != null)
        {
            for (int i = 0; i < dataPacketObjs.Length; i++)
            {
                if (dataPacketObjs[i] != null && dataPacketObjs[i].activeSelf != active)
                {
                    dataPacketObjs[i].SetActive(active);
                }
            }
        }

        if (sourceSocketObj != null && sourceSocketObj.activeSelf != active)
        {
            sourceSocketObj.SetActive(active);
        }

        if (targetSocketObj != null && targetSocketObj.activeSelf != active)
        {
            targetSocketObj.SetActive(active);
        }
    }

    /// <summary>
    /// Mengembalikan kamera utama yang di-cache agar loop Update() tidak memanggil Camera.main per frame.
    /// </summary>
    private Camera ResolveMainCamera()
    {
        if (cachedMainCamera == null || !cachedMainCamera.isActiveAndEnabled)
        {
            cachedMainCamera = Camera.main;
        }

        return cachedMainCamera;
    }

    /// <summary>
    /// Mengatur orientasi 3D TextMeshPro agar selalu menghadap kamera utama (billboard effect)
    /// dan tidak terbalik saat kamera AR bergerak.
    /// </summary>
    private void UpdateLabelBillboard()
    {
        Camera mainCam = ResolveMainCamera();
        if (statusLabel == null || mainCam == null)
        {
            return;
        }

        Transform camTransform = mainCam.transform;
        Vector3 dirFromCamera = statusLabel.transform.position - camTransform.position;
        if (dirFromCamera.sqrMagnitude > 0.0001f)
        {
            statusLabel.transform.rotation = Quaternion.LookRotation(dirFromCamera, camTransform.up);
        }
    }

    /// <summary>
    /// Mendeteksi perubahan orientasi sudut kartu InputSource terhadap kamera (~90 derajat = 1, ~0 derajat = 0).
    /// Hanya mengubah state saat melewati ambang sudut agar tidak menimpa tombol UI ToggleInputState().
    /// </summary>
    private void CheckPhysicalRotationInput()
    {
        Camera mainCam = ResolveMainCamera();
        if (mainCam == null)
        {
            return;
        }

        // Proyeksikan arah atas kartu (transform.forward pada bidang ImageTarget) ke ruang lokal kamera
        Vector3 localCardForward = mainCam.transform.InverseTransformDirection(transform.forward);
        float angleFromVertical = Mathf.Abs(Mathf.Atan2(localCardForward.x, localCardForward.y) * Mathf.Rad2Deg);

        // Jika kartu diputar miring mendekati 90 derajat (45° s/d 135°), terdeteksi sebagai logika 1
        bool isRotatedToOne = (angleFromVertical >= rotationAngleThreshold && angleFromVertical <= (180f - rotationAngleThreshold));

        if (!rotationInitialized)
        {
            lastDetectedRotationBit = isRotatedToOne;
            rotationInitialized = true;
            return;
        }

        if (isRotatedToOne != lastDetectedRotationBit)
        {
            lastDetectedRotationBit = isRotatedToOne;
            initialInputState = isRotatedToOne;
            currentOutputValue = initialInputState;
            EvaluateGateLogic();
        }
    }

    /// <summary>
    /// Dipanggil oleh event Vuforia DefaultObserverEventHandler:
    /// - OnTargetFound() -> SetTrackingState(true)
    /// - OnTargetLost()  -> SetTrackingState(false)
    /// Diabaikan saat useExternalSpatialPose == true agar sistem tracking v6 tidak ditimpa Vuforia.
    /// </summary>
    public void SetTrackingState(bool tracked)
    {
        if (useExternalSpatialPose)
        {
            return;
        }

        if (tracked)
        {
            if (debounceCoroutine != null)
            {
                StopCoroutine(debounceCoroutine);
                debounceCoroutine = null;
            }

            isTracked = true;
            EvaluateGateLogic();
            UpdateVisuals();
        }
        else
        {
            if (Application.isPlaying && gameObject.activeInHierarchy && trackingLossDebounceTime > 0f)
            {
                if (debounceCoroutine != null)
                {
                    StopCoroutine(debounceCoroutine);
                }
                debounceCoroutine = StartCoroutine(TrackingLossDebounceRoutine());
            }
            else
            {
                ApplyTrackingLost();
            }
        }
    }

    /// <summary>
    /// Hook tracking agnostik (v6-Ready) untuk memosisikan kartu dan mengatur status pelacakan
    /// dari sistem markerless / hand-tracking eksternal saat useExternalSpatialPose == true.
    /// </summary>
    public void SetExternalSpatialPose(bool tracked, Vector3 worldPosition, Quaternion worldRotation)
    {
        if (!useExternalSpatialPose)
        {
            return;
        }

        if (debounceCoroutine != null)
        {
            StopCoroutine(debounceCoroutine);
            debounceCoroutine = null;
        }

        transform.SetPositionAndRotation(worldPosition, worldRotation);

        if (tracked)
        {
            bool wasTracked = isTracked;
            bool wasParked = externalParkedPreview;
            externalParkedPreview = false;
            isTracked = true;
            RestoreExternalChildRenderers();
            if (!wasTracked || wasParked)
            {
                lastRenderedTelemetryKey = -1;
                EvaluateGateLogic();
                UpdateVisuals();
            }
        }
        else if (isTracked || externalParkedPreview)
        {
            externalParkedPreview = false;
            ApplyTrackingLost();
        }
    }

    /// <summary>
    /// Menampilkan pratinjau model 3D gerbang di Rak Parkir Mode Udara (v6) sambil menjaga isTracked == false
    /// agar tidak masuk kuota gerbang dan tidak tersambung kabel CircuitManager sebelum ditarik ke zona rakit.
    /// </summary>
    public void SetExternalParkedPreview(bool parked, Vector3 worldPosition, Quaternion worldRotation, float uniformScale = 0.66f)
    {
        if (!useExternalSpatialPose)
        {
            return;
        }

        if (debounceCoroutine != null)
        {
            StopCoroutine(debounceCoroutine);
            debounceCoroutine = null;
        }

        transform.SetPositionAndRotation(worldPosition, worldRotation);
        transform.localScale = Vector3.one * uniformScale;

        bool wasTracked = isTracked;
        bool wasParked = externalParkedPreview;
        externalParkedPreview = parked;

        if (wasTracked)
        {
            ApplyTrackingLost();
            externalParkedPreview = parked;
        }

        if (parked)
        {
            RestoreExternalChildRenderers();
        }

        if (wasParked != parked || wasTracked)
        {
            lastRenderedTelemetryKey = -1;
            UpdateVisuals();
        }
    }

    /// <summary>
    /// Memulihkan Renderer visual anak (MeshRenderer, SkinnedMeshRenderer, TextMeshPro, AR_LabelBackdrop, dll.)
    /// yang sebelumnya dimatikan oleh Vuforia DefaultObserverEventHandler.OnTrackingLost() secara zero-GC,
    /// tanpa mengganggu LineRenderer saluran energi.
    /// </summary>
    private void RestoreExternalChildRenderers()
    {
        if (cachedExternalVisualRenderers == null)
        {
            cachedExternalVisualRenderers = GetComponentsInChildren<Renderer>(true);
        }

        for (int i = 0; i < cachedExternalVisualRenderers.Length; i++)
        {
            Renderer rend = cachedExternalVisualRenderers[i];
            if (rend == null || rend == lineRenderer || rend == conduitSheathRenderer)
            {
                continue;
            }
            if (useExternalSpatialPose && IsGroundHideRenderer(rend))
            {
                if (rend.enabled)
                {
                    rend.enabled = false;
                }
                continue;
            }

            if (!rend.enabled)
            {
                rend.enabled = true;
            }
        }

        if (statusLabel != null && !statusLabel.enabled)
        {
            statusLabel.enabled = true;
        }
    }

    public void InvalidateExternalRendererCache()
    {
        cachedExternalVisualRenderers = null;
        cachedGroundHideRenderers = null;
    }

    /// <summary>
    /// Mengatur label 3D kontekstual berbasis cerita level + lencana mikro [0]/[1] secara zero-GC
    /// (di-cache sekali saat LoadLevel, tidak ada alokasi string di UpdateVisuals).
    /// </summary>
    public void SetContextualLabelConfig(string entityName, string state0Text, string state1Text)
    {
        if (string.IsNullOrEmpty(entityName))
        {
            hasContextualLabel = false;
            cachedContextualLabel0 = null;
            cachedContextualLabel1 = null;
        }
        else
        {
            cachedContextualLabel0 = $"{entityName}: {state0Text} [0]";
            cachedContextualLabel1 = $"{entityName}: {state1Text} [1]";
            hasContextualLabel = true;
        }

        lastRenderedTelemetryKey = -1;
        if (isTracked)
        {
            UpdateVisuals();
        }
    }

    private void InitializeOutputTruthPodVisual()
    {
        if (nodeType != NodeType.OutputActuator)
        {
            return;
        }

        outputTruthPod = GetComponent<OutputTruthPodVisual>();
        if (outputTruthPod == null)
        {
            outputTruthPod = gameObject.AddComponent<OutputTruthPodVisual>();
        }

        outputTruthPod.Initialize(GetSharedTorusMesh());
    }

    private void SetOutputTruthPodVisible(bool visible)
    {
        if (outputTruthPod != null)
        {
            outputTruthPod.SetTrackedVisible(visible);
        }
    }

    private bool IsInputBNode()
    {
        return gameObject.name.ToUpperInvariant().Contains("B");
    }

    private static bool IsLogicGateNodeType(NodeType type)
    {
        return type == NodeType.AND
               || type == NodeType.OR
               || type == NodeType.NOT
               || type == NodeType.XOR
               || type == NodeType.NAND;
    }

    /// <summary>
    /// Melepas seluruh kendali pose/skala eksternal v6 dan mengembalikan node ke Mode Kartu Vuforia v5.
    /// </summary>
    public void ClearExternalSpatialOverride()
    {
        externalParkedPreview = false;
        useExternalSpatialPose = false;
        SetGroundRenderersEnabled(true);
        transform.localScale = Vector3.one;
        if (statusLabel != null)
        {
            statusLabel.transform.localScale = Vector3.one * LabelCompactScale;
        }
        lastRenderedTelemetryKey = -1;
        ApplyTrackingLost();
    }

    private IEnumerator TrackingLossDebounceRoutine()
    {
        yield return new WaitForSeconds(trackingLossDebounceTime);
        debounceCoroutine = null;
        ApplyTrackingLost();
    }

    private void ApplyTrackingLost()
    {
        isTracked = false;
        rotationInitialized = false;
        lastRenderedTelemetryKey = -1;
        connectedInputs.Clear();
        connectedTarget = null;
        UpdateInputPinTelemetry();

        SetConnectionVisualsActive(false);
        SetTouchHitboxEnabled(false);
        SetGroundingAndPortsActive(false);
        SetTotemActive(false);
        SetOutputDioramaActive(false);
        SetOutputTruthPodVisible(false);
        SetInputPinsActive(false);
        ResetShockwaveState();
        ResetTapBounceState();
        ResetSparkBurstPool();
        ResetConnectionSnapState();
        UpdateVisuals();
    }

    /// <summary>
    /// Menangguhkan atau mengaktifkan kembali pembacaan rotasi fisik kartu InputSource
    /// selama proses Coroutine verifikasi Tabel Kebenaran berlangsung.
    /// </summary>
    public void SetSimulationOverride(bool active)
    {
        isSimulationOverride = active;
        rotationInitialized = false;
    }

    /// <summary>
    /// Membalik nilai biner (0 <-> 1) khusus untuk kartu bertipe InputSource.
    /// Dapat dipanggil dari tombol UI Toggle Input A / Input B maupun pengujian di Inspector.
    /// </summary>
    public void ToggleInputState()
    {
        if (nodeType != NodeType.InputSource)
        {
            return;
        }

        initialInputState = !initialInputState;
        currentOutputValue = initialInputState;
        rotationInitialized = false;

        EvaluateGateLogic();
        UpdateVisuals();
    }

    /// <summary>
    /// Mengatur nilai InputSource secara eksplisit (digunakan saat simulasi/verifikasi tabel kebenaran).
    /// </summary>
    public void SetInputState(bool state)
    {
        if (nodeType != NodeType.InputSource)
        {
            return;
        }

        initialInputState = state;
        currentOutputValue = state;
        rotationInitialized = false;

        EvaluateGateLogic();
        UpdateVisuals();
    }

    /// <summary>
    /// Mengembalikan kuota maksimal jumlah input untuk masing-masing tipe gerbang logika.
    /// </summary>
    public int GetMaxInputCount()
    {
        switch (nodeType)
        {
            case NodeType.InputSource:
                return 0;
            case NodeType.NOT:
            case NodeType.OutputActuator:
                return 1;
            case NodeType.AND:
            case NodeType.OR:
            case NodeType.XOR:
            case NodeType.NAND:
                return 2;
            default:
                return 0;
        }
    }

    /// <summary>
    /// Mengevaluasi nilai output biner (currentOutputValue) berdasarkan tipe gerbang
    /// dan daftar input yang terhubung pada connectedInputs.
    /// </summary>
    public void EvaluateGateLogic()
    {
        UpdateInputPinTelemetry();

        switch (nodeType)
        {
            case NodeType.InputSource:
                currentOutputValue = initialInputState;
                break;

            case NodeType.NOT:
                if (connectedInputs.Count >= 1 && connectedInputs[0] != null)
                {
                    currentOutputValue = !connectedInputs[0].currentOutputValue;
                }
                else
                {
                    currentOutputValue = false;
                }
                break;

            case NodeType.OutputActuator:
                if (connectedInputs.Count >= 1 && connectedInputs[0] != null)
                {
                    currentOutputValue = connectedInputs[0].currentOutputValue;
                }
                else
                {
                    currentOutputValue = false;
                }
                break;

            case NodeType.AND:
                if (connectedInputs.Count >= 2 && connectedInputs[0] != null && connectedInputs[1] != null)
                {
                    currentOutputValue = connectedInputs[0].currentOutputValue && connectedInputs[1].currentOutputValue;
                }
                else
                {
                    currentOutputValue = false;
                }
                break;

            case NodeType.OR:
                if (connectedInputs.Count >= 2 && connectedInputs[0] != null && connectedInputs[1] != null)
                {
                    currentOutputValue = connectedInputs[0].currentOutputValue || connectedInputs[1].currentOutputValue;
                }
                else
                {
                    currentOutputValue = false;
                }
                break;

            case NodeType.XOR:
                if (connectedInputs.Count >= 2 && connectedInputs[0] != null && connectedInputs[1] != null)
                {
                    currentOutputValue = connectedInputs[0].currentOutputValue ^ connectedInputs[1].currentOutputValue;
                }
                else
                {
                    currentOutputValue = false;
                }
                break;

            case NodeType.NAND:
                if (connectedInputs.Count >= 2 && connectedInputs[0] != null && connectedInputs[1] != null)
                {
                    currentOutputValue = !(connectedInputs[0].currentOutputValue && connectedInputs[1].currentOutputValue);
                }
                else
                {
                    currentOutputValue = false;
                }
                break;
        }

        UpdateVisuals();
    }

    /// <summary>
    /// Memperbarui visual busur Bezier (LineRenderer), bola paket data, node soket terminal,
    /// indikator pin input mikro 3D, diorama OutputActuator, dan label telemetri 3D TextMeshPro.
    /// </summary>
    public void UpdateVisuals()
    {
        UpdateInputPinTelemetry();
        Color activeLaserColor = currentOutputValue ? highSignalColor : lowSignalColor;

        // 1. Perbarui busur energi Quadratic Bezier, bola paket data, & cincin soket terminal
        RefreshConnectionVisuals(advanceFlowPhase: false);

        // 2. Perbarui grounding AR, port fisik, totem/model 3D, indikator pin mikro 3D, hitbox sentuh 3D, & diorama OutputActuator
        bool showParkedVisual = !isTracked && useExternalSpatialPose && externalParkedPreview;
        bool showModelVisual = isTracked || showParkedVisual;
        SetTouchHitboxEnabled(isTracked);
        SetGroundingAndPortsActive(showModelVisual, showPorts: isTracked);
        SetTotemActive(showModelVisual);
        SetOutputDioramaActive(isTracked);
        SetOutputTruthPodVisible(isTracked);
        SetInputPinsActive(isTracked);
        if (isTracked)
        {
            UpdateTotemColors();
            UpdateOutputDioramaColors();
            UpdateReactiveAccentMaterials();
            UpdatePortVisuals();
            UpdateInputPinsVisual();
            if (mechanismVisual != null)
            {
                mechanismVisual.UpdateMechanism(isInputAConnected, inputAValue, isInputBConnected, inputBValue, currentOutputValue, isParkedPreview: false, deltaTime: 0f);
            }
        }
        else if (showParkedVisual)
        {
            UpdateTotemColors();
            UpdateReactiveAccentMaterials();
            if (mechanismVisual != null)
            {
                mechanismVisual.UpdateMechanism(false, false, false, false, false, isParkedPreview: true, deltaTime: 0f);
            }
        }

        // 3. Perbarui indikator tambahan opsional di Inspector (jika terpasang manual)
        if (statusIndicatorRenderer != null)
        {
            if (customIndicatorMaterial == null)
            {
                customIndicatorMaterial = statusIndicatorRenderer.material;
            }

            if (customIndicatorMaterial != null)
            {
                customIndicatorMaterial.color = activeLaserColor;
            }
        }

        // 4. Perbarui visibilitas, teks telemetri pin, warna, latar AR_LabelBackdrop, dan orientasi 3D TextMeshPro
        if (statusLabel != null)
        {
            if (statusLabel.gameObject.activeSelf != showModelVisual)
            {
                statusLabel.gameObject.SetActive(showModelVisual);
            }

            if (labelBackdropObj != null && labelBackdropObj.activeSelf != showModelVisual)
            {
                labelBackdropObj.SetActive(showModelVisual);
            }

            if (isTracked)
            {
                int telemetryKey = (isInputAConnected ? 1 : 0)
                                   | (inputAValue ? 2 : 0)
                                   | (isInputBConnected ? 4 : 0)
                                   | (inputBValue ? 8 : 0)
                                   | (currentOutputValue ? 16 : 0);
                if (telemetryKey != lastRenderedTelemetryKey)
                {
                    lastRenderedTelemetryKey = telemetryKey;
                    statusLabel.transform.localScale = Vector3.one * (useExternalSpatialPose ? LabelCompactScale * 1.15f : LabelCompactScale);
                    string telemetryText = BuildTelemetryStatusText();
                    statusLabel.text = telemetryText;
                    statusLabel.color = currentOutputValue ? BrightGreenTextColor : BrightRedOrangeTextColor;
                    UpdateLabelBackdropSize(telemetryText);
                }
                UpdateLabelBillboard();
            }
            else if (showParkedVisual)
            {
                const int ParkedTelemetryKey = -100;
                if (lastRenderedTelemetryKey != ParkedTelemetryKey)
                {
                    lastRenderedTelemetryKey = ParkedTelemetryKey;
                    // Kompensasi skala label saat model parkir (0.56x) agar nama gerbang AND/OR/XOR/NAND/NOT tetap tajam terbaca
                    statusLabel.transform.localScale = Vector3.one * (LabelCompactScale * 1.35f);
                    string parkedLabel = nodeType.ToString();
                    statusLabel.text = parkedLabel;
                    statusLabel.color = new Color(0.84f, 0.98f, 1.00f, 1f);
                    UpdateLabelBackdropSize(parkedLabel);
                }
                UpdateLabelBillboard();
            }
        }
    }

    private void UpdateInputPinTelemetry()
    {
        isInputAConnected = connectedInputs != null
                            && connectedInputs.Count >= 1
                            && connectedInputs[0] != null
                            && connectedInputs[0].isTracked;
        inputAValue = isInputAConnected && connectedInputs[0].currentOutputValue;

        isInputBConnected = connectedInputs != null
                            && connectedInputs.Count >= 2
                            && connectedInputs[1] != null
                            && connectedInputs[1].isTracked;
        inputBValue = isInputBConnected && connectedInputs[1].currentOutputValue;
    }

    private string BuildTelemetryStatusText()
    {
        if (nodeType == NodeType.InputSource)
        {
            if (hasContextualLabel)
            {
                return currentOutputValue ? cachedContextualLabel1 : cachedContextualLabel0;
            }

            string inputName = IsInputBNode() ? "B" : "A";
            return $"INPUT {inputName}: {(currentOutputValue ? "1 [HIGH]" : "0 [LOW]")}";
        }

        string pinA = FormatPinBit(isInputAConnected, inputAValue);
        if (nodeType == NodeType.OutputActuator)
        {
            if (hasContextualLabel)
            {
                return currentOutputValue ? cachedContextualLabel1 : cachedContextualLabel0;
            }

            return $"OUTPUT [{pinA}] -> {(currentOutputValue ? "AKTIF" : "MATI")}";
        }

        if (nodeType == NodeType.NOT)
        {
            return $"NOT [{pinA}] -> {(currentOutputValue ? "1" : "0")}";
        }

        string pinB = FormatPinBit(isInputBConnected, inputBValue);
        return $"{nodeType} [{pinA},{pinB}] -> {(currentOutputValue ? "1" : "0")}";
    }

    private static string FormatPinBit(bool isConnected, bool value)
    {
        return isConnected ? (value ? "1" : "0") : "?";
    }

    /// <summary>
    /// Membangun 1 atau 2 indikator mikro pin input 3D (tanpa Collider) di sisi kiri totem pada permukaan kartu LogicGate.
    /// </summary>
    private void InitializeInputPinsVisual()
    {
        if (nodeType == NodeType.InputSource || nodeType == NodeType.OutputActuator)
        {
            return;
        }

        int pinCount = GetMaxInputCount();
        if (pinCount <= 0)
        {
            return;
        }

        inputPinObjs = new GameObject[pinCount];
        inputPinMaterials = new Material[pinCount];

        for (int i = 0; i < pinCount; i++)
        {
            float zOffset = pinCount == 2 ? (i == 0 ? 0.10f : -0.10f) : 0f;
            Material pinMat = CreateSafeOpenGLMaterial();
            if (pinMat != null)
            {
                pinMat.color = PinUnconnectedColor;
            }
            inputPinMaterials[i] = pinMat;

            GameObject pinObj = CreatePrimitivePart(
                $"InputPinNode_{i}",
                PrimitiveType.Sphere,
                transform,
                new Vector3(-0.27f, 0.064f, zOffset),
                Vector3.zero,
                Vector3.one * 0.028f,
                pinMat);
            pinObj.SetActive(false);
            inputPinObjs[i] = pinObj;
        }
    }

    private void SetInputPinsActive(bool active)
    {
        if (inputPinObjs == null)
        {
            return;
        }

        for (int i = 0; i < inputPinObjs.Length; i++)
        {
            if (inputPinObjs[i] != null && inputPinObjs[i].activeSelf != active)
            {
                inputPinObjs[i].SetActive(active);
            }
        }
    }

    private void UpdateInputPinsVisual()
    {
        if (inputPinObjs == null || inputPinObjs.Length == 0)
        {
            return;
        }

        UpdateSingleInputPin(0, isInputAConnected, inputAValue);
        if (inputPinObjs.Length >= 2)
        {
            UpdateSingleInputPin(1, isInputBConnected, inputBValue);
        }
    }

    private void UpdateSingleInputPin(int index, bool isConnected, bool value)
    {
        if (index < 0 || inputPinObjs == null || index >= inputPinObjs.Length)
        {
            return;
        }

        GameObject pinObj = inputPinObjs[index];
        Material pinMat = (inputPinMaterials != null && index < inputPinMaterials.Length) ? inputPinMaterials[index] : null;

        Color targetColor;
        float targetScale;
        if (!isConnected)
        {
            targetColor = PinUnconnectedColor;
            targetScale = 0.024f;
        }
        else if (value)
        {
            targetColor = PinHighColor;
            targetScale = 0.044f;
        }
        else
        {
            targetColor = PinLowColor;
            targetScale = 0.033f;
        }

        if (pinMat != null)
        {
            pinMat.color = targetColor;
        }

        if (pinObj != null)
        {
            pinObj.transform.localScale = Vector3.one * targetScale;
        }
    }

    /// <summary>
    /// Membangun cincin gelombang kejut 3D prosedural menggunakan GetSharedTorusMesh() tanpa Collider.
    /// </summary>
    private void InitializeShockwaveVisual()
    {
        shockwaveMaterial = CreateSafeOpenGLMaterial();
        if (shockwaveMaterial != null)
        {
            shockwaveMaterial.color = new Color(ShockwavePulseColor.r, ShockwavePulseColor.g, ShockwavePulseColor.b, 0f);
        }

        shockwaveRingObj = CreateProceduralMeshPart(
            "ShockwaveRing3D",
            GetSharedTorusMesh(),
            transform,
            new Vector3(0f, 0.018f, 0f),
            Vector3.zero,
            new Vector3(shockwaveStartScale, 0.035f, shockwaveStartScale),
            shockwaveMaterial);
        shockwaveRingObj.SetActive(false);
    }

    private void DetectRisingEdgeAndAnimateShockwave()
    {
        if (!shockwaveBaselineInitialized)
        {
            lastShockwaveOutputState = currentOutputValue;
            shockwaveBaselineInitialized = true;
        }
        else
        {
            if (!lastShockwaveOutputState && currentOutputValue)
            {
                TriggerShockwavePulse();
            }

            lastShockwaveOutputState = currentOutputValue;
        }

        if (!isShockwavePlaying)
        {
            return;
        }

        shockwaveElapsed += Time.deltaTime;
        float duration = Mathf.Max(0.05f, shockwaveDuration);
        float t = Mathf.Clamp01(shockwaveElapsed / duration);
        ApplyShockwaveFrame(t);

        if (t >= 1f)
        {
            isShockwavePlaying = false;
            if (shockwaveRingObj != null)
            {
                shockwaveRingObj.SetActive(false);
            }
        }
    }

    private void TriggerShockwavePulse()
    {
        isShockwavePlaying = true;
        shockwaveElapsed = 0f;

        if (shockwaveRingObj != null)
        {
            shockwaveRingObj.SetActive(true);
            ApplyShockwaveFrame(0f);
        }
    }

    private void ApplyShockwaveFrame(float normalizedTime)
    {
        float eased = 1f - ((1f - normalizedTime) * (1f - normalizedTime));
        float scaleXZ = Mathf.Lerp(shockwaveStartScale, shockwaveEndScale, eased);
        float alpha = Mathf.Lerp(shockwaveStartAlpha, 0f, normalizedTime);

        if (shockwaveRingObj != null)
        {
            shockwaveRingObj.transform.localScale = new Vector3(scaleXZ, 0.035f, scaleXZ);
        }

        if (shockwaveMaterial != null)
        {
            shockwaveMaterial.color = new Color(ShockwavePulseColor.r, ShockwavePulseColor.g, ShockwavePulseColor.b, alpha);
        }
    }

    private void ResetShockwaveState()
    {
        shockwaveBaselineInitialized = false;
        lastShockwaveOutputState = false;
        isShockwavePlaying = false;
        shockwaveElapsed = 0f;

        if (shockwaveRingObj != null && shockwaveRingObj.activeSelf)
        {
            shockwaveRingObj.SetActive(false);
        }
    }

    /// <summary>
    /// Membangun satu hitbox trigger ringan "AR_TouchHitbox3D" di bawah kartu agar hologram
    /// dapat disentuh langsung di ruang 3D AR tanpa membebani simulasi fisika.
    /// </summary>
    private void InitializeTouchHitbox()
    {
        Transform existingHitbox = transform.Find("AR_TouchHitbox3D");
        GameObject hitboxObj;
        if (existingHitbox != null)
        {
            hitboxObj = existingHitbox.gameObject;
        }
        else
        {
            hitboxObj = new GameObject("AR_TouchHitbox3D");
            hitboxObj.transform.SetParent(transform, false);
        }

        hitboxObj.transform.localPosition = new Vector3(0f, 0.20f, 0f);
        hitboxObj.transform.localRotation = Quaternion.identity;
        hitboxObj.transform.localScale = Vector3.one;

        touchHitboxCollider = hitboxObj.GetComponent<SphereCollider>();
        if (touchHitboxCollider == null)
        {
            touchHitboxCollider = hitboxObj.AddComponent<SphereCollider>();
        }

        touchHitboxCollider.isTrigger = true;
        touchHitboxCollider.center = Vector3.zero;
        touchHitboxCollider.radius = 0.34f;
        touchHitboxCollider.enabled = isTracked;
    }

    private void SetTouchHitboxEnabled(bool enabledState)
    {
        if (touchHitboxCollider != null && touchHitboxCollider.enabled != enabledState)
        {
            touchHitboxCollider.enabled = enabledState;
        }
    }

    /// <summary>
    /// Memicu animasi pantulan skala elastis singkat (~0.18 detik hingga puncak ~1.18x)
    /// pada kontainer visual 3D tanpa alokasi Coroutine dan tanpa drift skala permanen.
    /// </summary>
    public void TriggerTapFeedback()
    {
        if (!isTracked)
        {
            return;
        }

        isTapBouncePlaying = true;
        tapBounceElapsed = 0f;
        ApplyTapBounceScale(1f);
        TriggerEnergyBurst(currentOutputValue ? ReactiveAccentHighColor : TapInspectBurstColor, 1f);
    }

    /// <summary>
    /// Mengembalikan ringkasan fungsi singkat kartu beserta status pin terkini (100% ASCII aman LiberationSans SDF)
    /// saat hologram gerbang disentuh langsung di ruang 3D AR.
    /// </summary>
    public string GetTouchInspectionSummary()
    {
        UpdateInputPinTelemetry();
        string pinA = FormatPinBit(isInputAConnected, inputAValue);
        string pinB = FormatPinBit(isInputBConnected, inputBValue);
        string outBit = currentOutputValue ? "1" : "0";

        switch (nodeType)
        {
            case NodeType.AND:
                return $"[Sentuh 3D] AND [{pinA},{pinB}]->{outBit} | Output 1 hanya jika kedua input bernilai 1.";
            case NodeType.OR:
                return $"[Sentuh 3D] OR [{pinA},{pinB}]->{outBit} | Output 1 jika minimal satu input bernilai 1.";
            case NodeType.XOR:
                return $"[Sentuh 3D] XOR [{pinA},{pinB}]->{outBit} | Output 1 hanya jika input A dan B berbeda.";
            case NodeType.NAND:
                return $"[Sentuh 3D] NAND [{pinA},{pinB}]->{outBit} | Kebalikan AND: Output 0 hanya jika keduanya 1.";
            case NodeType.NOT:
                return $"[Sentuh 3D] NOT [{pinA}]->{outBit} | Inverter: membalik nilai sinyal 0 <-> 1.";
            case NodeType.InputSource:
                string inputName = gameObject.name.ToUpperInvariant().Contains("B") ? "B" : "A";
                return $"[Sentuh 3D] INPUT {inputName} = {outBit} | Sumber sinyal biner (sentuh hologram atau putar ~90 derajat).";
            case NodeType.OutputActuator:
                return $"[Sentuh 3D] OUTPUT [{pinA}]->{outBit} | Aktuator 3D akhir penerima hasil evaluasi rangkaian.";
            default:
                return $"[Sentuh 3D] {nodeType} -> {outBit} | Komponen logika digital aktif.";
        }
    }

    private Transform GetTapBounceTargetTransform()
    {
        if (nodeType == NodeType.OutputActuator)
        {
            return outputDioramaRootObj != null ? outputDioramaRootObj.transform : null;
        }

        return totemRootObj != null ? totemRootObj.transform : null;
    }

    private void UpdateTapBounceAnimation()
    {
        if (!isTapBouncePlaying)
        {
            return;
        }

        tapBounceElapsed += Time.deltaTime;
        float duration = Mathf.Max(0.05f, tapBounceDuration);
        float normalized = Mathf.Clamp01(tapBounceElapsed / duration);

        if (normalized >= 1f)
        {
            isTapBouncePlaying = false;
            tapBounceElapsed = 0f;
            ApplyTapBounceScale(1f);
            return;
        }

        // Kurva pantulan elastis responsif: naik cepat menuju puncak (~1.18x) lalu kembali mulus ke 1.0x
        float peakDelta = Mathf.Max(0f, tapBouncePeakScale - 1f);
        float shapedPulse = Mathf.Sin(Mathf.Pow(normalized, 0.72f) * Mathf.PI);
        float scaleMultiplier = 1f + (peakDelta * shapedPulse);
        ApplyTapBounceScale(scaleMultiplier);
    }

    private void ApplyTapBounceScale(float scaleMultiplier)
    {
        Transform target = GetTapBounceTargetTransform();
        if (target != null)
        {
            target.localScale = Vector3.one * scaleMultiplier;
        }
    }

    private void ResetTapBounceState()
    {
        if (isTapBouncePlaying || tapBounceElapsed > 0f)
        {
            isTapBouncePlaying = false;
            tapBounceElapsed = 0f;
        }

        ApplyTapBounceScale(1f);
    }

    // =========================================================================
    // V5.0 / V6-READY: AR GROUNDING, PHYSICAL PORTS, MODEL PIPELINE & SANITIZER
    // =========================================================================

    /// <summary>
    /// Mengembalikan posisi dunia (world space) anchor port input fisik kartu (slot 0 = Port_InA, slot 1 = Port_InB).
    /// </summary>
    public Vector3 GetInputPortWorldPosition(int slotIndex)
    {
        if (slotIndex == 1 && portInBTransform != null)
        {
            return portInBTransform.position;
        }

        if (portInATransform != null)
        {
            return portInATransform.position;
        }

        Vector3 normal = transform.up.sqrMagnitude > 0.0001f ? transform.up.normalized : Vector3.up;
        return transform.position + normal * socketSurfaceOffset;
    }

    /// <summary>
    /// Mengembalikan posisi dunia (world space) anchor port output fisik kartu (Port_Out).
    /// </summary>
    public Vector3 GetOutputPortWorldPosition()
    {
        if (portOutTransform != null)
        {
            return portOutTransform.position;
        }

        Vector3 normal = transform.up.sqrMagnitude > 0.0001f ? transform.up.normalized : Vector3.up;
        return transform.position + normal * socketSurfaceOffset;
    }

    /// <summary>
    /// Membangun bayangan kontak radial transparan (Fake Contact Shadow) di Y = 0.001f
    /// menggunakan satu Texture2D 64x64 statis bersama agar mesin menapak nyata di atas meja AR.
    /// </summary>
    private void InitializeContactShadowVisual()
    {
        Material shadowMat = CreateSafeOpenGLMaterial();
        if (shadowMat != null)
        {
            shadowMat.mainTexture = GetSharedContactShadowTexture();
            shadowMat.color = Color.white;
            managedMaterials.Add(shadowMat);
        }

        contactShadowObj = CreatePrimitivePart(
            "AR_ContactShadow",
            PrimitiveType.Quad,
            transform,
            new Vector3(0f, 0.001f, 0f),
            new Vector3(90f, 0f, 0f),
            new Vector3(0.72f, 0.72f, 1f),
            shadowMat);
        contactShadowObj.SetActive(false);
    }

    private static Texture2D GetSharedContactShadowTexture()
    {
        if (sharedContactShadowTexture != null)
        {
            return sharedContactShadowTexture;
        }

        const int texSize = 64;
        Texture2D tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
        tex.name = "AR_ContactShadowRadial64";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[texSize * texSize];
        float half = (texSize - 1) * 0.5f;

        for (int y = 0; y < texSize; y++)
        {
            float dy = (y - half) / half;
            for (int x = 0; x < texSize; x++)
            {
                float dx = (x - half) / half;
                float dist = Mathf.Sqrt((dx * dx) + (dy * dy));
                float falloff = Mathf.Clamp01(1f - dist);
                float smooth = falloff * falloff * (3f - (2f * falloff));
                float alpha = smooth * 0.62f;
                pixels[(y * texSize) + x] = new Color(0f, 0f, 0f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, true);
        sharedContactShadowTexture = tex;
        return sharedContactShadowTexture;
    }

    /// <summary>
    /// Membangun landasan soket menapak kartu ("AR_BaseTile3D") dari FloorTile_Basic (dinormalisasi ke 0.56 x 0.038 x 0.56)
    /// dengan fallback pelat prosedural bila aset tidak tersedia.
    /// </summary>
    private void InitializeBaseTileVisual()
    {
        baseTileRootObj = new GameObject("AR_BaseTile3D");
        baseTileRootObj.transform.SetParent(transform, false);
        baseTileRootObj.transform.localPosition = Vector3.zero;
        baseTileRootObj.transform.localRotation = Quaternion.identity;
        baseTileRootObj.transform.localScale = Vector3.one;

        GameObject floorPrefab = LoadV5ModelPrefab("FloorTile_Basic");
        if (floorPrefab != null)
        {
            GameObject tileInstance = Instantiate(floorPrefab);
            tileInstance.name = floorPrefab.name;
            RemoveCollidersRecursive(tileInstance);
            SanitizeAndRegisterModelMaterials(tileInstance, null);
            NormalizeBaseTileToSlab(tileInstance, baseTileRootObj.transform, BaseTileFootprintXZ, BaseTileThicknessY, BaseTileBottomY);
        }
        else
        {
            Material fallbackBaseMat = CreateSafeOpenGLMaterial();
            if (fallbackBaseMat != null)
            {
                fallbackBaseMat.color = new Color(0.18f, 0.24f, 0.32f, 1f);
                managedMaterials.Add(fallbackBaseMat);
            }

            CreatePrimitivePart(
                "BaseTile_Fallback",
                PrimitiveType.Cube,
                baseTileRootObj.transform,
                new Vector3(0f, BaseTileBottomY + (BaseTileThicknessY * 0.5f), 0f),
                Vector3.zero,
                new Vector3(BaseTileFootprintXZ, BaseTileThicknessY, BaseTileFootprintXZ),
                fallbackBaseMat);
        }

        // v7.0 Fase 1: Jangkar warna permanen Input A (Sian) & Input B (Kuning) pada tepi pelat dasar AR_BaseTile3D
        if (nodeType == NodeType.InputSource)
        {
            Color idColor = IsInputBNode() ? InputBIdentityGoldColor : InputAIdentityCyanColor;
            inputIdentityAccentMat = CreateSafeRemappedModelMaterial(idColor);
            if (inputIdentityAccentMat != null)
            {
                if (inputIdentityAccentMat.HasProperty(EmissionColorPropId))
                {
                    inputIdentityAccentMat.EnableKeyword("_EMISSION");
                    inputIdentityAccentMat.SetColor(EmissionColorPropId, idColor * 0.90f);
                }
                managedMaterials.Add(inputIdentityAccentMat);
            }

            float trimY = BaseTileTopSurfaceY + 0.004f;
            CreatePrimitivePart(
                "InputIdentity_FrontRim",
                PrimitiveType.Cube,
                baseTileRootObj.transform,
                new Vector3(0f, trimY, -0.266f),
                Vector3.zero,
                new Vector3(0.50f, 0.014f, 0.028f),
                inputIdentityAccentMat);
            CreatePrimitivePart(
                "InputIdentity_LeftRim",
                PrimitiveType.Cube,
                baseTileRootObj.transform,
                new Vector3(-0.266f, trimY, 0f),
                Vector3.zero,
                new Vector3(0.022f, 0.012f, 0.48f),
                inputIdentityAccentMat);
            CreatePrimitivePart(
                "InputIdentity_RightRim",
                PrimitiveType.Cube,
                baseTileRootObj.transform,
                new Vector3(0.266f, trimY, 0f),
                Vector3.zero,
                new Vector3(0.022f, 0.012f, 0.48f),
                inputIdentityAccentMat);
        }

        baseTileRootObj.SetActive(false);
    }

    /// <summary>
    /// Membangun anchor port fisik standar (Port_InA, Port_InB, Port_Out) dengan cincin soket menyala
    /// di tepi kiri/kanan alas mesin agar kabel energi menancap akurat di posisi fisik.
    /// </summary>
    private void InitializePhysicalPorts()
    {
        int maxInputs = GetMaxInputCount();

        if (maxInputs >= 1)
        {
            float inAZ = maxInputs == 2 ? 0.10f : 0f;
            portInAMaterial = CreateManagedPortMaterial(PinUnconnectedColor);
            portInATransform = CreatePhysicalPortAnchor("Port_InA", new Vector3(-0.27f, 0.055f, inAZ), portInAMaterial);
        }

        if (maxInputs >= 2)
        {
            portInBMaterial = CreateManagedPortMaterial(PinUnconnectedColor);
            portInBTransform = CreatePhysicalPortAnchor("Port_InB", new Vector3(-0.27f, 0.055f, -0.10f), portInBMaterial);
        }

        if (nodeType != NodeType.OutputActuator)
        {
            portOutMaterial = CreateManagedPortMaterial(LowSocketColor);
            portOutTransform = CreatePhysicalPortAnchor("Port_Out", new Vector3(0.27f, 0.055f, 0f), portOutMaterial);
        }
    }

    private Material CreateManagedPortMaterial(Color initialColor)
    {
        Material mat = CreateSafeOpenGLMaterial();
        if (mat != null)
        {
            mat.color = initialColor;
            managedMaterials.Add(mat);
        }

        return mat;
    }

    private Transform CreatePhysicalPortAnchor(string portName, Vector3 localPos, Material ringMat)
    {
        Transform portRoot = CreateEmptyChild(portName, transform, localPos);

        // Cincin soket menyala kecil tanpa Collider di titik anchor kabel
        CreateProceduralMeshPart(
            $"{portName}_Ring",
            GetSharedTorusMesh(),
            portRoot,
            Vector3.zero,
            Vector3.zero,
            Vector3.one * 0.040f,
            ringMat);

        portRoot.gameObject.SetActive(false);
        return portRoot;
    }

    private void SetGroundingAndPortsActive(bool active, bool showPorts = true)
    {
        if (contactShadowObj != null && contactShadowObj.activeSelf != active)
        {
            contactShadowObj.SetActive(active);
        }

        if (baseTileRootObj != null && baseTileRootObj.activeSelf != active)
        {
            baseTileRootObj.SetActive(active);
        }

        // v7.0 Fase 3: Di Mode Udara (hologram melayang), matikan komponen Renderer pada AR_BaseTile3D & AR_ContactShadow tanpa mengubah hierarki SetActive
        SetGroundRenderersEnabled(!useExternalSpatialPose);

        bool portsActive = active && showPorts;

        if (portInATransform != null && portInATransform.gameObject.activeSelf != portsActive)
        {
            portInATransform.gameObject.SetActive(portsActive);
        }

        if (portInBTransform != null && portInBTransform.gameObject.activeSelf != portsActive)
        {
            portInBTransform.gameObject.SetActive(portsActive);
        }

        if (portOutTransform != null && portOutTransform.gameObject.activeSelf != portsActive)
        {
            portOutTransform.gameObject.SetActive(portsActive);
        }
    }

    private void EnsureGroundHideRenderersCached()
    {
        if (cachedGroundHideRenderers != null)
        {
            return;
        }

        Renderer[] tileRends = baseTileRootObj != null ? baseTileRootObj.GetComponentsInChildren<Renderer>(true) : null;
        Renderer[] shadowRends = contactShadowObj != null ? contactShadowObj.GetComponentsInChildren<Renderer>(true) : null;
        int tileLen = tileRends != null ? tileRends.Length : 0;
        int shadowLen = shadowRends != null ? shadowRends.Length : 0;
        cachedGroundHideRenderers = new Renderer[tileLen + shadowLen];
        for (int i = 0; i < tileLen; i++)
        {
            cachedGroundHideRenderers[i] = tileRends[i];
        }
        for (int i = 0; i < shadowLen; i++)
        {
            cachedGroundHideRenderers[tileLen + i] = shadowRends[i];
        }
    }

    private bool IsGroundHideRenderer(Renderer candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        EnsureGroundHideRenderersCached();
        for (int i = 0; i < cachedGroundHideRenderers.Length; i++)
        {
            if (cachedGroundHideRenderers[i] == candidate)
            {
                return true;
            }
        }
        return false;
    }

    private void SetGroundRenderersEnabled(bool enabled)
    {
        EnsureGroundHideRenderersCached();
        for (int i = 0; i < cachedGroundHideRenderers.Length; i++)
        {
            Renderer r = cachedGroundHideRenderers[i];
            if (r != null && r.enabled != enabled)
            {
                r.enabled = enabled;
            }
        }
    }

    private void UpdatePortVisuals()
    {
        if (portInAMaterial != null && portInAFlashTimer <= 0f)
        {
            int stateInA = isInputAConnected ? (inputAValue ? 1 : 0) : -1;
            if (stateInA != lastPortInAVisualState)
            {
                lastPortInAVisualState = stateInA;
                portInAMaterial.color = stateInA > 0 ? PinHighColor : (stateInA == 0 ? PinLowColor : PinUnconnectedColor);
            }
        }

        if (portInBMaterial != null && portInBFlashTimer <= 0f)
        {
            int stateInB = isInputBConnected ? (inputBValue ? 1 : 0) : -1;
            if (stateInB != lastPortInBVisualState)
            {
                lastPortInBVisualState = stateInB;
                portInBMaterial.color = stateInB > 0 ? PinHighColor : (stateInB == 0 ? PinLowColor : PinUnconnectedColor);
            }
        }

        if (portOutMaterial != null && portOutFlashTimer <= 0f)
        {
            int stateOut = currentOutputValue ? 1 : 0;
            if (stateOut != lastPortOutVisualState)
            {
                lastPortOutVisualState = stateOut;
                portOutMaterial.color = stateOut > 0 ? HighSocketColor : LowSocketColor;
            }
        }
    }

    /// <summary>
    /// Memicu kilatan sesaat dan percikan 3D pada port input (Port_InA/Port_InB) kartu tujuan saat kabel menancap (snap).
    /// </summary>
    public void TriggerInputPortSnapFlash(int slotIndex, Vector3 worldPortPos)
    {
        if (!isTracked)
        {
            return;
        }

        if (slotIndex == 1)
        {
            portInBFlashTimer = SnapSurgeDuration;
            lastPortInBVisualState = -2;
        }
        else
        {
            portInAFlashTimer = SnapSurgeDuration;
            lastPortInAVisualState = -2;
        }

        TriggerEnergyBurstAtWorldPoint(worldPortPos, SnapSurgeFlashColor, 0.85f);
    }

    private void UpdatePortSnapFlashTimers()
    {
        float dt = Time.deltaTime;

        if (portOutFlashTimer > 0f)
        {
            portOutFlashTimer = Mathf.Max(0f, portOutFlashTimer - dt);
            if (portOutMaterial != null)
            {
                float t = portOutFlashTimer / SnapSurgeDuration;
                Color baseCol = currentOutputValue ? HighSocketColor : LowSocketColor;
                portOutMaterial.color = Color.Lerp(baseCol, SnapSurgeFlashColor, t);
            }
        }

        if (portInAFlashTimer > 0f)
        {
            portInAFlashTimer = Mathf.Max(0f, portInAFlashTimer - dt);
            if (portInAMaterial != null)
            {
                float t = portInAFlashTimer / SnapSurgeDuration;
                Color baseCol = isInputAConnected ? (inputAValue ? PinHighColor : PinLowColor) : PinUnconnectedColor;
                portInAMaterial.color = Color.Lerp(baseCol, SnapSurgeFlashColor, t);
            }
        }

        if (portInBFlashTimer > 0f)
        {
            portInBFlashTimer = Mathf.Max(0f, portInBFlashTimer - dt);
            if (portInBMaterial != null)
            {
                float t = portInBFlashTimer / SnapSurgeDuration;
                Color baseCol = isInputBConnected ? (inputBValue ? PinHighColor : PinLowColor) : PinUnconnectedColor;
                portInBMaterial.color = Color.Lerp(baseCol, SnapSurgeFlashColor, t);
            }
        }
    }

    private void ResetConnectionSnapState()
    {
        lastVisualConnectedTarget = null;
        lastVisualTargetSlotIndex = -1;
        snapSurgeTimer = 0f;
        portOutFlashTimer = 0f;
        portInAFlashTimer = 0f;
        portInBFlashTimer = 0f;
    }

    private static GameObject LoadV5ModelPrefab(string modelName)
    {
        if (string.IsNullOrEmpty(modelName))
        {
            return null;
        }

        return Resources.Load<GameObject>(V5ModelsResourcePrefix + modelName);
    }

    private static string GetNodeModelResourceName(NodeType type)
    {
        switch (type)
        {
            case NodeType.InputSource: return "Props_Computer";
            case NodeType.AND: return "Props_ContainerFull";
            case NodeType.OR: return "Props_Teleporter_1";
            case NodeType.XOR: return "Props_Teleporter_2";
            case NodeType.NAND: return "Props_Vessel_Tall";
            case NodeType.NOT: return "Props_Laser";
            default: return string.Empty;
        }
    }

    private static Vector3 GetNodeModelInitialEuler(NodeType type)
    {
        switch (type)
        {
            case NodeType.InputSource: return new Vector3(0f, 180f, 0f); // Layar menghadap kamera depan
            case NodeType.NAND: return new Vector3(-90f, 0f, 0f);        // Tegakkan tabung Props_Vessel_Tall (FBX Z-up -> Unity +Y)
            case NodeType.NOT: return new Vector3(0f, 90f, 0f);          // Proyektor laser menghadap ke kanan (+X)
            default: return Vector3.zero;
        }
    }

    private static string GetOutputDioramaModelResourceName(int variantIndex)
    {
        switch (variantIndex)
        {
            case 0: return "RoofTile_OrangeVent";
            case 1: return "Staircase";
            case 2: return "Door_Double";
            case 3: return "Props_Pod";
            default: return "Props_Capsule";
        }
    }

    private static Vector3 GetOutputDioramaInitialEuler(int variantIndex)
    {
        switch (variantIndex)
        {
            case 1: return new Vector3(0f, 90f, 0f);  // Tangga naik ke kanan (+X)
            case 2: return new Vector3(0f, 180f, 0f); // Pintu airlock menghadap kamera
            case 3: return new Vector3(0f, 180f, 0f); // Kaca pod menghadap kamera
            case 4: return new Vector3(0f, 180f, 0f); // Kapsul menghadap kamera
            default: return Vector3.zero;
        }
    }

    /// <summary>
    /// Menormalisasi model FBX secara seragam berdasarkan gabungan mesh bounds lokalnya
    /// agar pas di footprint & tinggi target serta menapak tepat di atas permukaan alas kartu.
    /// </summary>
    private static Transform NormalizeModelToFitFootprint(
        GameObject modelInstance,
        Transform containerParent,
        float maxFootprintXZ,
        float maxHeightY,
        float baseSurfaceY,
        Vector3 initialLocalEuler,
        out float normalizedTopY,
        out Vector3 normalizedHalfExtents,
        bool forceLongestAxisVertical = false)
    {
        Transform modelPivot = CreateEmptyChild($"{modelInstance.name}_Pivot", containerParent, Vector3.zero);
        modelInstance.transform.SetParent(modelPivot, false);
        modelInstance.transform.localPosition = Vector3.zero;
        modelInstance.transform.localEulerAngles = initialLocalEuler;
        modelInstance.transform.localScale = Vector3.one;

        if (!TryCalculateHierarchyMeshBounds(modelInstance.transform, modelPivot, out Bounds localBounds))
        {
            modelPivot.localPosition = new Vector3(0f, baseSurfaceY, 0f);
            normalizedTopY = baseSurfaceY + (maxHeightY * 0.75f);
            normalizedHalfExtents = new Vector3(maxFootprintXZ * 0.45f, maxHeightY * 0.375f, maxFootprintXZ * 0.45f);
            return modelPivot;
        }

        // Pengaman orientasi vertikal (mis. Props_Vessel_Tall pada NAND) agar sumbu terpanjang selalu berdiri tegak di +Y
        if (forceLongestAxisVertical && localBounds.size.y < Mathf.Max(localBounds.size.x, localBounds.size.z))
        {
            Quaternion uprightCorrection = localBounds.size.z >= localBounds.size.x
                ? Quaternion.Euler(-90f, 0f, 0f)
                : Quaternion.Euler(0f, 0f, 90f);
            modelInstance.transform.localRotation = uprightCorrection * modelInstance.transform.localRotation;
            TryCalculateHierarchyMeshBounds(modelInstance.transform, modelPivot, out localBounds);
        }

        float sizeX = Mathf.Max(0.0001f, localBounds.size.x);
        float sizeY = Mathf.Max(0.0001f, localBounds.size.y);
        float sizeZ = Mathf.Max(0.0001f, localBounds.size.z);
        float maxXZ = Mathf.Max(sizeX, sizeZ);

        float scaleXZ = maxFootprintXZ / maxXZ;
        float scaleY = maxHeightY / sizeY;
        float uniformScale = Mathf.Min(scaleXZ, scaleY);

        modelPivot.localScale = Vector3.one * uniformScale;
        modelPivot.localPosition = new Vector3(
            -localBounds.center.x * uniformScale,
            baseSurfaceY - (localBounds.min.y * uniformScale),
            -localBounds.center.z * uniformScale);

        normalizedTopY = baseSurfaceY + (sizeY * uniformScale);
        normalizedHalfExtents = new Vector3(
            sizeX * uniformScale * 0.5f,
            sizeY * uniformScale * 0.5f,
            sizeZ * uniformScale * 0.5f);

        return modelPivot;
    }

    private static void NormalizeBaseTileToSlab(
        GameObject tileInstance,
        Transform containerParent,
        float targetFootprintXZ,
        float targetThicknessY,
        float bottomY)
    {
        Transform tilePivot = CreateEmptyChild($"{tileInstance.name}_Pivot", containerParent, Vector3.zero);
        tileInstance.transform.SetParent(tilePivot, false);
        tileInstance.transform.localPosition = Vector3.zero;
        tileInstance.transform.localRotation = Quaternion.identity;
        tileInstance.transform.localScale = Vector3.one;

        if (!TryCalculateHierarchyMeshBounds(tileInstance.transform, tilePivot, out Bounds localBounds))
        {
            tilePivot.localPosition = new Vector3(0f, bottomY, 0f);
            return;
        }

        float scaleX = targetFootprintXZ / Mathf.Max(0.0001f, localBounds.size.x);
        float scaleY = targetThicknessY / Mathf.Max(0.0001f, localBounds.size.y);
        float scaleZ = targetFootprintXZ / Mathf.Max(0.0001f, localBounds.size.z);

        tilePivot.localScale = new Vector3(scaleX, scaleY, scaleZ);
        tilePivot.localPosition = new Vector3(
            -localBounds.center.x * scaleX,
            bottomY - (localBounds.min.y * scaleY),
            -localBounds.center.z * scaleZ);
    }

    private static bool TryCalculateHierarchyMeshBounds(Transform modelRoot, Transform referenceSpace, out Bounds resultBounds)
    {
        MeshFilter[] meshFilters = modelRoot.GetComponentsInChildren<MeshFilter>(true);
        bool hasBounds = false;
        resultBounds = new Bounds(Vector3.zero, Vector3.zero);

        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter mf = meshFilters[i];
            if (mf == null || mf.sharedMesh == null)
            {
                continue;
            }

            Matrix4x4 toRef = BuildLocalTransformMatrix(mf.transform, referenceSpace);
            Bounds meshBounds = mf.sharedMesh.bounds;
            Vector3 min = meshBounds.min;
            Vector3 max = meshBounds.max;

            EncapsulateTransformedCorner(toRef, new Vector3(min.x, min.y, min.z), ref resultBounds, ref hasBounds);
            EncapsulateTransformedCorner(toRef, new Vector3(min.x, min.y, max.z), ref resultBounds, ref hasBounds);
            EncapsulateTransformedCorner(toRef, new Vector3(min.x, max.y, min.z), ref resultBounds, ref hasBounds);
            EncapsulateTransformedCorner(toRef, new Vector3(min.x, max.y, max.z), ref resultBounds, ref hasBounds);
            EncapsulateTransformedCorner(toRef, new Vector3(max.x, min.y, min.z), ref resultBounds, ref hasBounds);
            EncapsulateTransformedCorner(toRef, new Vector3(max.x, min.y, max.z), ref resultBounds, ref hasBounds);
            EncapsulateTransformedCorner(toRef, new Vector3(max.x, max.y, min.z), ref resultBounds, ref hasBounds);
            EncapsulateTransformedCorner(toRef, new Vector3(max.x, max.y, max.z), ref resultBounds, ref hasBounds);
        }

        return hasBounds;
    }

    private static Matrix4x4 BuildLocalTransformMatrix(Transform leaf, Transform ancestor)
    {
        Matrix4x4 matrix = Matrix4x4.identity;
        Transform current = leaf;
        while (current != null && current != ancestor)
        {
            matrix = Matrix4x4.TRS(current.localPosition, current.localRotation, current.localScale) * matrix;
            current = current.parent;
        }

        return matrix;
    }

    private static void EncapsulateTransformedCorner(Matrix4x4 matrix, Vector3 corner, ref Bounds bounds, ref bool hasBounds)
    {
        Vector3 transformed = matrix.MultiplyPoint3x4(corner);
        if (!hasBounds)
        {
            bounds = new Bounds(transformed, Vector3.zero);
            hasBounds = true;
        }
        else
        {
            bounds.Encapsulate(transformed);
        }
    }

    private static void RemoveCollidersRecursive(GameObject rootObj)
    {
        Collider[] colliders = rootObj.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
            {
                Destroy(colliders[i]);
            }
        }
    }

    /// <summary>
    /// Mengklon dan menyanitasi seluruh material pada model FBX instansiasi agar aman untuk Android OpenGLES3,
    /// mematikan real-time shadow yang berat di GPU mobile, meredam kilap plastik berlebih pada permukaan solid,
    /// mendaftarkannya ke managedMaterials untuk pembersihan di OnDestroy(), dan memetakan slot bernama "Accent"
    /// ke daftar material reaktif sinyal biner.
    /// </summary>
    private void SanitizeAndRegisterModelMaterials(GameObject rootObj, List<Material> reactiveAccentList)
    {
        Renderer[] renderers = rootObj.GetComponentsInChildren<Renderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer rend = renderers[r];
            if (rend == null)
            {
                continue;
            }

            // Matikan bayangan real-time berat di GPU mobile; kedalaman di atas meja AR ditangani AR_ContactShadow
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;

            Material[] sourceMaterials = rend.sharedMaterials;
            if (sourceMaterials == null || sourceMaterials.Length == 0)
            {
                continue;
            }

            Material[] sanitizedMaterials = new Material[sourceMaterials.Length];
            for (int m = 0; m < sourceMaterials.Length; m++)
            {
                Material srcMat = sourceMaterials[m];
                string matName = srcMat != null ? srcMat.name : string.Empty;
                bool isAccentSlot = matName.IndexOf("Accent", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isGlassSlot = matName.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0;
                Color preservedColor = (srcMat != null && srcMat.HasProperty(ColorPropId))
                    ? srcMat.GetColor(ColorPropId)
                    : new Color(0.75f, 0.78f, 0.84f, 1f);

                bool needsRemap = srcMat == null
                                  || srcMat.shader == null
                                  || !srcMat.shader.isSupported
                                  || string.Equals(srcMat.shader.name, "Hidden/InternalErrorShader", StringComparison.Ordinal);

                Material clonedMat;
                if (needsRemap)
                {
                    clonedMat = CreateSafeRemappedModelMaterial(preservedColor);
                }
                else
                {
                    clonedMat = new Material(srcMat);
                }

                if (clonedMat != null)
                {
                    ApplyQuaterniusContrastPolish(clonedMat, isAccentSlot, isGlassSlot);
                    managedMaterials.Add(clonedMat);
                    if (reactiveAccentList != null && isAccentSlot)
                    {
                        reactiveAccentList.Add(clonedMat);
                    }
                }

                sanitizedMaterials[m] = clonedMat;
            }

            rend.sharedMaterials = sanitizedMaterials;
        }
    }

    private static void ApplyQuaterniusContrastPolish(Material mat, bool isAccentSlot, bool isGlassSlot)
    {
        if (mat == null || isAccentSlot)
        {
            return;
        }

        // Redam kilap plastik & metalik berlebih di lingkungan AR tanpa reflection probe agar bodi solid tetap tegas
        float maxGloss = isGlassSlot ? 0.55f : 0.26f;
        float maxMetallic = isGlassSlot ? 0.15f : 0.18f;

        if (mat.HasProperty(GlossinessPropId))
        {
            float currentGloss = mat.GetFloat(GlossinessPropId);
            if (currentGloss > maxGloss)
            {
                mat.SetFloat(GlossinessPropId, maxGloss);
            }
        }

        if (mat.HasProperty(MetallicPropId))
        {
            float currentMetallic = mat.GetFloat(MetallicPropId);
            if (currentMetallic > maxMetallic)
            {
                mat.SetFloat(MetallicPropId, maxMetallic);
            }
        }
    }

    private Material CreateSafeRemappedModelMaterial(Color preservedColor)
    {
        Shader fallbackShader = Shader.Find("Standard");
        if (fallbackShader == null || !fallbackShader.isSupported)
        {
            fallbackShader = Shader.Find("Mobile/Diffuse");
        }

        Material mat = (fallbackShader != null && fallbackShader.isSupported)
            ? new Material(fallbackShader)
            : CreateSafeOpenGLMaterial();

        if (mat != null)
        {
            if (mat.HasProperty(ColorPropId))
            {
                mat.SetColor(ColorPropId, preservedColor);
            }
            else
            {
                mat.color = preservedColor;
            }

            if (mat.HasProperty(GlossinessPropId))
            {
                mat.SetFloat(GlossinessPropId, 0.26f);
            }

            if (mat.HasProperty(MetallicPropId))
            {
                mat.SetFloat(MetallicPropId, 0.18f);
            }
        }

        return mat;
    }

    /// <summary>
    /// Memperbarui warna & emisi slot material "Accent" pada model Quaternius secara zero-GC
    /// hanya ketika state biner HIGH/LOW atau varian diorama aktif berubah.
    /// </summary>
    private void UpdateReactiveAccentMaterials()
    {
        int stateBit = currentOutputValue ? 1 : 0;
        int currentVariant = (nodeType == NodeType.OutputActuator) ? activeDioramaVariantIndex : -1;

        if (stateBit == lastAppliedAccentState && currentVariant == lastAppliedAccentDioramaVariant)
        {
            return;
        }

        lastAppliedAccentState = stateBit;
        lastAppliedAccentDioramaVariant = currentVariant;

        Color accentColor = currentOutputValue ? ReactiveAccentHighColor : ReactiveAccentLowColor;
        Color emissionColor = currentOutputValue ? (ReactiveAccentHighColor * 1.15f) : (ReactiveAccentLowColor * 0.35f);

        if (nodeType == NodeType.OutputActuator)
        {
            if (dioramaVariantReactiveAccentMaterials != null
                && activeDioramaVariantIndex >= 0
                && activeDioramaVariantIndex < dioramaVariantReactiveAccentMaterials.Length)
            {
                ApplyColorToReactiveMaterials(dioramaVariantReactiveAccentMaterials[activeDioramaVariantIndex], accentColor, emissionColor);
            }
        }
        else
        {
            ApplyColorToReactiveMaterials(totemReactiveAccentMaterials, accentColor, emissionColor);
        }
    }

    private static void ApplyColorToReactiveMaterials(List<Material> materials, Color baseColor, Color emissionColor)
    {
        if (materials == null || materials.Count == 0)
        {
            return;
        }

        for (int i = 0; i < materials.Count; i++)
        {
            Material mat = materials[i];
            if (mat == null)
            {
                continue;
            }

            if (mat.HasProperty(ColorPropId))
            {
                mat.SetColor(ColorPropId, baseColor);
            }
            else
            {
                mat.color = baseColor;
            }

            if (mat.HasProperty(EmissionColorPropId))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor(EmissionColorPropId, emissionColor);
            }
        }
    }

    // =========================================================================
    // V5.0 TAHAP 2: REACTIVE 3D MACHINES, DIORAMAS, & ZERO-GC SPARK BURST POOL
    // =========================================================================

    private void InitializeV5SharedRigMaterials()
    {
        v5MetalDarkMaterial = CreateSafeRemappedModelMaterial(new Color(0.18f, 0.22f, 0.28f, 1f));
        if (v5MetalDarkMaterial != null)
        {
            managedMaterials.Add(v5MetalDarkMaterial);
        }

        v5MetalSteelMaterial = CreateSafeRemappedModelMaterial(new Color(0.46f, 0.54f, 0.62f, 1f));
        if (v5MetalSteelMaterial != null)
        {
            managedMaterials.Add(v5MetalSteelMaterial);
        }
    }

    private Material CreateV5ReactiveGlowMaterial(List<Material> reactiveTargetList)
    {
        Material glowMat = CreateSafeOpenGLMaterial();
        if (glowMat != null)
        {
            glowMat.color = ReactiveAccentLowColor;
            managedMaterials.Add(glowMat);
            if (reactiveTargetList != null)
            {
                reactiveTargetList.Add(glowMat);
            }
        }

        return glowMat;
    }

    /// <summary>
    /// Memasang rig sakelar fisik 3D (InputSource) atau mekanisme bergerak unik per LogicGate
    /// tepat di atas/sekeliling model Quaternius yang telah dinormalisasi.
    /// </summary>
    private void BuildV5NodeMechanism(
        NodeType type,
        Transform parentRoot,
        float modelTopY,
        Vector3 halfExtents,
        List<Material> reactiveTargetList)
    {
        Material glowMat = CreateV5ReactiveGlowMaterial(reactiveTargetList);

        switch (type)
        {
            case NodeType.InputSource:
                BuildV5PhysicalInputLever(parentRoot, modelTopY, glowMat);
                break;
            case NodeType.AND:
                BuildV5AndCompressorPistons(parentRoot, modelTopY, glowMat);
                break;
            case NodeType.OR:
                BuildV5OrTurbineAndCrystal(parentRoot, modelTopY, glowMat);
                break;
            case NodeType.XOR:
                BuildV5XorCounterGyroRings(parentRoot, modelTopY, glowMat);
                break;
            case NodeType.NAND:
                BuildV5NandEnergyCoil(parentRoot, modelTopY, glowMat);
                break;
            case NodeType.NOT:
                BuildV5NotLaserLensAndPhaseRing(parentRoot, modelTopY, halfExtents, glowMat);
                break;
        }
    }

    private void BuildV5PhysicalInputLever(Transform parentRoot, float modelTopY, Material glowMat)
    {
        Transform rigRoot = CreateEmptyChild("AR_PhysicalLeverRig", parentRoot, new Vector3(0f, modelTopY - 0.005f, 0f));

        // Blok engsel metalik gelap + penjepit baja di puncak konsol
        CreatePrimitivePart("Lever_HingeBase", PrimitiveType.Cube, rigRoot,
            new Vector3(0f, 0.016f, 0f), Vector3.zero, new Vector3(0.145f, 0.032f, 0.095f), v5MetalDarkMaterial);
        CreatePrimitivePart("Lever_BracketL", PrimitiveType.Cube, rigRoot,
            new Vector3(-0.048f, 0.034f, 0f), Vector3.zero, new Vector3(0.018f, 0.028f, 0.072f), v5MetalSteelMaterial);
        CreatePrimitivePart("Lever_BracketR", PrimitiveType.Cube, rigRoot,
            new Vector3(0.048f, 0.034f, 0f), Vector3.zero, new Vector3(0.018f, 0.028f, 0.072f), v5MetalSteelMaterial);

        // Indikator LED depan menyala sesuai bit 0/1
        CreatePrimitivePart("Lever_FrontLED", PrimitiveType.Cube, rigRoot,
            new Vector3(0f, 0.018f, -0.052f), Vector3.zero, new Vector3(0.096f, 0.016f, 0.014f), glowMat);

        // Pivot tuas yang menjungkit halus antara -38 derajat (LOW/0) dan +38 derajat (HIGH/1)
        currentLeverAngle = initialInputState ? LeverAngleMagnitude : -LeverAngleMagnitude;
        v5LeverPivot = CreateEmptyChild("Lever_Pivot", rigRoot, new Vector3(0f, 0.024f, 0f));
        v5LeverPivot.localRotation = Quaternion.Euler(0f, 0f, -currentLeverAngle);

        CreatePrimitivePart("Lever_Shaft", PrimitiveType.Cylinder, v5LeverPivot,
            new Vector3(0f, 0.056f, 0f), Vector3.zero, new Vector3(0.018f, 0.056f, 0.018f), v5MetalSteelMaterial);
        CreatePrimitivePart("Lever_Grip", PrimitiveType.Cube, v5LeverPivot,
            new Vector3(0f, 0.114f, 0f), Vector3.zero, new Vector3(0.030f, 0.030f, 0.116f), glowMat);
        CreatePrimitivePart("Lever_CapFront", PrimitiveType.Sphere, v5LeverPivot,
            new Vector3(0f, 0.114f, -0.058f), Vector3.zero, Vector3.one * 0.034f, v5MetalDarkMaterial);
        CreatePrimitivePart("Lever_CapBack", PrimitiveType.Sphere, v5LeverPivot,
            new Vector3(0f, 0.114f, 0.058f), Vector3.zero, Vector3.one * 0.034f, v5MetalDarkMaterial);
    }

    private void BuildV5AndCompressorPistons(Transform parentRoot, float modelTopY, Material glowMat)
    {
        Transform rigRoot = CreateEmptyChild("AR_AndCompressorRig", parentRoot, Vector3.zero);
        float mountY = modelTopY - 0.004f;

        CreatePrimitivePart("AND_MountLeft", PrimitiveType.Cylinder, rigRoot,
            new Vector3(-0.095f, mountY + 0.014f, 0f), Vector3.zero, new Vector3(0.068f, 0.016f, 0.068f), v5MetalDarkMaterial);
        CreatePrimitivePart("AND_MountRight", PrimitiveType.Cylinder, rigRoot,
            new Vector3(0.095f, mountY + 0.014f, 0f), Vector3.zero, new Vector3(0.068f, 0.016f, 0.068f), v5MetalDarkMaterial);

        v5GateAnimPrimaryBasePos = new Vector3(-0.095f, mountY + 0.030f, 0f);
        v5GateAnimPrimary = CreateEmptyChild("AND_PistonLeft", rigRoot, v5GateAnimPrimaryBasePos);
        CreatePrimitivePart("AND_RodLeft", PrimitiveType.Cylinder, v5GateAnimPrimary,
            new Vector3(0f, 0.030f, 0f), Vector3.zero, new Vector3(0.024f, 0.034f, 0.024f), v5MetalSteelMaterial);
        CreatePrimitivePart("AND_CrownLeft", PrimitiveType.Cylinder, v5GateAnimPrimary,
            new Vector3(0f, 0.066f, 0f), Vector3.zero, new Vector3(0.074f, 0.014f, 0.074f), glowMat);

        v5GateAnimSecondaryBasePos = new Vector3(0.095f, mountY + 0.030f, 0f);
        v5GateAnimSecondary = CreateEmptyChild("AND_PistonRight", rigRoot, v5GateAnimSecondaryBasePos);
        CreatePrimitivePart("AND_RodRight", PrimitiveType.Cylinder, v5GateAnimSecondary,
            new Vector3(0f, 0.030f, 0f), Vector3.zero, new Vector3(0.024f, 0.034f, 0.024f), v5MetalSteelMaterial);
        CreatePrimitivePart("AND_CrownRight", PrimitiveType.Cylinder, v5GateAnimSecondary,
            new Vector3(0f, 0.066f, 0f), Vector3.zero, new Vector3(0.074f, 0.014f, 0.074f), glowMat);
    }

    private void BuildV5OrTurbineAndCrystal(Transform parentRoot, float modelTopY, Material glowMat)
    {
        float hubY = Mathf.Max(BaseTileTopSurfaceY + 0.14f, modelTopY + 0.045f);
        Transform rigRoot = CreateEmptyChild("AR_OrTurbineRig", parentRoot, Vector3.zero);

        v5GateAnimPrimary = CreateEmptyChild("OR_TurbinePivot", rigRoot, new Vector3(0f, hubY, 0f));
        CreateProceduralMeshPart("OR_TurbineRing", GetSharedTorusMesh(), v5GateAnimPrimary,
            Vector3.zero, Vector3.zero, Vector3.one * 0.135f, v5MetalSteelMaterial);
        CreatePrimitivePart("OR_VaneX", PrimitiveType.Cube, v5GateAnimPrimary,
            Vector3.zero, new Vector3(14f, 0f, 0f), new Vector3(0.24f, 0.010f, 0.026f), v5MetalDarkMaterial);
        CreatePrimitivePart("OR_VaneZ", PrimitiveType.Cube, v5GateAnimPrimary,
            Vector3.zero, new Vector3(0f, 0f, 14f), new Vector3(0.026f, 0.010f, 0.24f), v5MetalDarkMaterial);

        v5GateAnimSecondaryBasePos = new Vector3(0f, hubY + 0.030f, 0f);
        v5GateAnimSecondary = CreateEmptyChild("OR_CrystalPivot", rigRoot, v5GateAnimSecondaryBasePos);
        CreateProceduralMeshPart("OR_FloatingCrystal", GetSharedDiamondSpireMesh(), v5GateAnimSecondary,
            Vector3.zero, Vector3.zero, Vector3.one * 0.072f, glowMat);
    }

    private void BuildV5XorCounterGyroRings(Transform parentRoot, float modelTopY, Material glowMat)
    {
        float centerY = Mathf.Max(BaseTileTopSurfaceY + 0.17f, modelTopY * 0.68f);
        Transform rigRoot = CreateEmptyChild("AR_XorGyroRig", parentRoot, Vector3.zero);

        v5GateAnimPrimary = CreateEmptyChild("XOR_GyroOuterPivot", rigRoot, new Vector3(0f, centerY, 0f));
        CreateProceduralMeshPart("XOR_GyroOuterRing", GetSharedTorusMesh(), v5GateAnimPrimary,
            Vector3.zero, new Vector3(58f, 0f, 0f), Vector3.one * 0.145f, v5MetalSteelMaterial);

        v5GateAnimSecondary = CreateEmptyChild("XOR_GyroInnerPivot", rigRoot, new Vector3(0f, centerY, 0f));
        CreateProceduralMeshPart("XOR_GyroInnerRing", GetSharedTorusMesh(), v5GateAnimSecondary,
            Vector3.zero, new Vector3(-58f, 90f, 0f), Vector3.one * 0.105f, glowMat);
        CreateProceduralMeshPart("XOR_CenterCore", GetSharedDiamondSpireMesh(), v5GateAnimSecondary,
            Vector3.zero, Vector3.zero, Vector3.one * 0.052f, glowMat);
    }

    private void BuildV5NandEnergyCoil(Transform parentRoot, float modelTopY, Material glowMat)
    {
        v5GateAnimSweepMinY = BaseTileTopSurfaceY + 0.070f;
        v5GateAnimSweepMaxY = Mathf.Max(v5GateAnimSweepMinY + 0.12f, modelTopY - 0.035f);

        v5GateAnimPrimaryBasePos = new Vector3(0f, v5GateAnimSweepMinY, 0f);
        v5GateAnimPrimary = CreateEmptyChild("AR_NandEnergyCoil", parentRoot, v5GateAnimPrimaryBasePos);

        CreateProceduralMeshPart("NAND_CoilRing", GetSharedTorusMesh(), v5GateAnimPrimary,
            Vector3.zero, Vector3.zero, Vector3.one * 0.165f, glowMat);
        CreatePrimitivePart("NAND_ClampL", PrimitiveType.Cube, v5GateAnimPrimary,
            new Vector3(-0.165f, 0f, 0f), Vector3.zero, new Vector3(0.028f, 0.024f, 0.044f), v5MetalDarkMaterial);
        CreatePrimitivePart("NAND_ClampR", PrimitiveType.Cube, v5GateAnimPrimary,
            new Vector3(0.165f, 0f, 0f), Vector3.zero, new Vector3(0.028f, 0.024f, 0.044f), v5MetalDarkMaterial);
    }

    private void BuildV5NotLaserLensAndPhaseRing(Transform parentRoot, float modelTopY, Vector3 halfExtents, Material glowMat)
    {
        float muzzleX = Mathf.Min(0.22f, halfExtents.x + 0.018f);
        float muzzleY = BaseTileTopSurfaceY + ((modelTopY - BaseTileTopSurfaceY) * 0.52f);
        Transform rigRoot = CreateEmptyChild("AR_NotLaserRig", parentRoot, Vector3.zero);

        v5GateAnimPrimaryBasePos = new Vector3(muzzleX, muzzleY, 0f);
        v5GateAnimPrimary = CreateEmptyChild("NOT_FocusLensPivot", rigRoot, v5GateAnimPrimaryBasePos);
        CreateProceduralMeshPart("NOT_LensHousing", GetSharedTorusMesh(), v5GateAnimPrimary,
            Vector3.zero, new Vector3(0f, 0f, 90f), Vector3.one * 0.072f, v5MetalSteelMaterial);
        CreatePrimitivePart("NOT_LensFinA", PrimitiveType.Cube, v5GateAnimPrimary,
            new Vector3(0f, 0.068f, 0f), Vector3.zero, new Vector3(0.022f, 0.032f, 0.014f), v5MetalDarkMaterial);
        CreatePrimitivePart("NOT_LensFinB", PrimitiveType.Cube, v5GateAnimPrimary,
            new Vector3(0f, -0.034f, 0.059f), new Vector3(120f, 0f, 0f), new Vector3(0.022f, 0.032f, 0.014f), v5MetalDarkMaterial);
        CreatePrimitivePart("NOT_LensFinC", PrimitiveType.Cube, v5GateAnimPrimary,
            new Vector3(0f, -0.034f, -0.059f), new Vector3(-120f, 0f, 0f), new Vector3(0.022f, 0.032f, 0.014f), v5MetalDarkMaterial);

        v5GateAnimSecondaryBasePos = new Vector3(muzzleX + 0.035f, muzzleY, 0f);
        v5GateAnimSecondary = CreateEmptyChild("NOT_PhaseProjectorRing", rigRoot, v5GateAnimSecondaryBasePos);
        CreateProceduralMeshPart("NOT_PhaseRingMesh", GetSharedTorusMesh(), v5GateAnimSecondary,
            Vector3.zero, new Vector3(0f, 0f, 90f), Vector3.one * 0.052f, glowMat);
    }

    private void AnimateV5NodeMechanisms()
    {
        if (nodeType == NodeType.OutputActuator)
        {
            return;
        }

        if (nodeType == NodeType.InputSource && v5LeverPivot != null)
        {
            float targetAngle = currentOutputValue ? LeverAngleMagnitude : -LeverAngleMagnitude;
            float smooth = 1f - Mathf.Exp(-14f * Time.deltaTime);
            currentLeverAngle = Mathf.Lerp(currentLeverAngle, targetAngle, smooth);
            v5LeverPivot.localRotation = Quaternion.Euler(0f, 0f, -currentLeverAngle);
            return;
        }

        if (v5GateAnimPrimary == null)
        {
            return;
        }

        float dt = Time.deltaTime;
        float speedFactor = currentOutputValue ? 1f : 0.12f;
        v5GateAnimPhase += dt * (currentOutputValue ? 10.5f : 1.2f);

        switch (nodeType)
        {
            case NodeType.AND:
                float pistonLift = currentOutputValue ? ((Mathf.Sin(v5GateAnimPhase) * 0.5f + 0.5f) * 0.048f) : 0f;
                v5GateAnimPrimary.localPosition = v5GateAnimPrimaryBasePos + new Vector3(0f, pistonLift, 0f);
                if (v5GateAnimSecondary != null)
                {
                    v5GateAnimSecondary.localPosition = v5GateAnimSecondaryBasePos + new Vector3(0f, pistonLift, 0f);
                }
                break;

            case NodeType.OR:
                v5GateAnimPrimary.Rotate(Vector3.up, 290f * speedFactor * dt, Space.Self);
                if (v5GateAnimSecondary != null)
                {
                    v5GateAnimSecondary.Rotate(Vector3.up, -210f * speedFactor * dt, Space.Self);
                    float hoverY = currentOutputValue ? (Mathf.Sin(v5GateAnimPhase * 0.6f) * 0.018f) : 0f;
                    v5GateAnimSecondary.localPosition = v5GateAnimSecondaryBasePos + new Vector3(0f, hoverY, 0f);
                }
                break;

            case NodeType.XOR:
                v5GateAnimPrimary.Rotate(new Vector3(0.30f, 1f, 0.25f), 270f * speedFactor * dt, Space.Self);
                if (v5GateAnimSecondary != null)
                {
                    v5GateAnimSecondary.Rotate(new Vector3(-0.30f, -1f, 0.25f), 340f * speedFactor * dt, Space.Self);
                }
                break;

            case NodeType.NAND:
                if (currentOutputValue)
                {
                    float t = Mathf.Sin(v5GateAnimPhase * 0.48f) * 0.5f + 0.5f;
                    float y = Mathf.Lerp(v5GateAnimSweepMinY, v5GateAnimSweepMaxY, t);
                    v5GateAnimPrimary.localPosition = new Vector3(0f, y, 0f);
                    v5GateAnimPrimary.Rotate(Vector3.up, 200f * dt, Space.Self);
                }
                else
                {
                    float settledY = Mathf.Lerp(v5GateAnimPrimary.localPosition.y, v5GateAnimSweepMinY, 1f - Mathf.Exp(-8f * dt));
                    v5GateAnimPrimary.localPosition = new Vector3(0f, settledY, 0f);
                }
                break;

            case NodeType.NOT:
                v5GateAnimPrimary.Rotate(Vector3.right, 320f * speedFactor * dt, Space.Self);
                if (v5GateAnimSecondary != null)
                {
                    float pulseX = currentOutputValue ? (Mathf.Sin(v5GateAnimPhase * 0.85f) * 0.016f) : 0f;
                    v5GateAnimSecondary.localPosition = v5GateAnimSecondaryBasePos + new Vector3(pulseX, 0f, 0f);
                }
                break;
        }
    }

    /// <summary>
    /// Memasang mekanisme bergerak spesifik pada masing-masing dari 5 varian diorama Quaternius OutputActuator.
    /// </summary>
    private void BuildV5OutputDioramaMechanism(
        int variantIndex,
        Transform variantRoot,
        float modelTopY,
        Vector3 halfExtents,
        List<Material> reactiveTargetList)
    {
        Material glowMat = CreateV5ReactiveGlowMaterial(reactiveTargetList);

        switch (variantIndex)
        {
            case 0:
                BuildV5SirenDioramaRig(variantRoot, modelTopY, glowMat);
                break;
            case 1:
                BuildV5StaircaseDioramaRig(variantRoot, modelTopY, glowMat);
                break;
            case 2:
                BuildV5VaultDoorDioramaRig(variantRoot, modelTopY, halfExtents, glowMat);
                break;
            case 3:
                BuildV5ReactorPodDioramaRig(variantRoot, modelTopY, glowMat);
                break;
            default:
                BuildV5CapsuleOrbitalDioramaRig(variantRoot, modelTopY, glowMat);
                break;
        }
    }

    private void BuildV5SirenDioramaRig(Transform variantRoot, float modelTopY, Material glowMat)
    {
        float baseY = modelTopY - 0.004f;
        Transform towerRoot = CreateEmptyChild("V5Siren_Tower", variantRoot, new Vector3(0f, baseY, 0f));

        CreatePrimitivePart("V5Siren_Pedestal", PrimitiveType.Cylinder, towerRoot,
            new Vector3(0f, 0.025f, 0f), Vector3.zero, new Vector3(0.115f, 0.025f, 0.115f), v5MetalDarkMaterial);
        CreatePrimitivePart("V5Siren_BeaconDome", PrimitiveType.Sphere, towerRoot,
            new Vector3(0f, 0.068f, 0f), Vector3.zero, Vector3.one * 0.072f, glowMat);

        v5SirenBeamMaterial = CreateSafeOpenGLMaterial();
        if (v5SirenBeamMaterial != null)
        {
            v5SirenBeamMaterial.color = SirenBeamLowColor;
            managedMaterials.Add(v5SirenBeamMaterial);
        }

        v5SirenRotorPivot = CreateEmptyChild("V5Siren_RotorPivot", towerRoot, new Vector3(0f, 0.072f, 0f));
        CreatePrimitivePart("V5Siren_RotorCap", PrimitiveType.Cube, v5SirenRotorPivot,
            Vector3.zero, Vector3.zero, new Vector3(0.088f, 0.020f, 0.030f), v5MetalSteelMaterial);

        v5SirenBeamsGroup = CreateEmptyChild("V5Siren_Beams", v5SirenRotorPivot, Vector3.zero).gameObject;
        CreatePrimitivePart("V5Siren_BeamL", PrimitiveType.Cube, v5SirenBeamsGroup.transform,
            new Vector3(-0.145f, 0.012f, 0f), new Vector3(0f, 0f, -8f), new Vector3(0.22f, 0.042f, 0.032f), v5SirenBeamMaterial);
        CreatePrimitivePart("V5Siren_BeamR", PrimitiveType.Cube, v5SirenBeamsGroup.transform,
            new Vector3(0.145f, 0.012f, 0f), new Vector3(0f, 0f, 8f), new Vector3(0.22f, 0.042f, 0.032f), v5SirenBeamMaterial);
    }

    private void BuildV5StaircaseDioramaRig(Transform variantRoot, float modelTopY, Material glowMat)
    {
        Transform rigRoot = CreateEmptyChild("V5Stair_LightsRig", variantRoot, Vector3.zero);
        v5StairStepMaterials = new Material[3];

        float heightSpan = Mathf.Max(0.16f, modelTopY - BaseTileTopSurfaceY);
        for (int s = 0; s < 3; s++)
        {
            Material stepMat = CreateSafeOpenGLMaterial();
            if (stepMat != null)
            {
                stepMat.color = StairStepIdleColor;
                managedMaterials.Add(stepMat);
            }
            v5StairStepMaterials[s] = stepMat;

            float frac = (s + 0.5f) / 3f;
            float stepX = Mathf.Lerp(-0.14f, 0.14f, frac);
            float stepY = BaseTileTopSurfaceY + (heightSpan * (0.20f + (frac * 0.65f)));

            CreatePrimitivePart($"V5Stair_StepStrip_{s}", PrimitiveType.Cube, rigRoot,
                new Vector3(stepX, stepY, 0f), Vector3.zero, new Vector3(0.036f, 0.014f, 0.30f), stepMat);
        }

        // Lentera puncak di ujung atas tangga
        Transform lanternRoot = CreateEmptyChild("V5Stair_TopLantern", rigRoot, new Vector3(0.16f, modelTopY + 0.036f, 0f));
        CreatePrimitivePart("V5Stair_LanternPost", PrimitiveType.Cylinder, lanternRoot,
            new Vector3(0f, -0.018f, 0f), Vector3.zero, new Vector3(0.018f, 0.022f, 0.018f), v5MetalSteelMaterial);
        CreatePrimitivePart("V5Stair_LanternBulb", PrimitiveType.Sphere, lanternRoot,
            Vector3.zero, Vector3.zero, Vector3.one * 0.056f, glowMat);
        v5StairLanternHalo = CreateProceduralMeshPart("V5Stair_LanternHalo", GetSharedTorusMesh(), lanternRoot,
            Vector3.zero, new Vector3(18f, 0f, 0f), Vector3.one * 0.068f, glowMat).transform;
    }

    private void BuildV5VaultDoorDioramaRig(Transform variantRoot, float modelTopY, Vector3 halfExtents, Material glowMat)
    {
        float midY = BaseTileTopSurfaceY + ((modelTopY - BaseTileTopSurfaceY) * 0.48f);
        float frontZ = -(Mathf.Max(0.05f, halfExtents.z) + 0.016f);
        Transform rigRoot = CreateEmptyChild("V5Vault_MechanismRig", variantRoot, new Vector3(0f, midY, frontZ));

        currentVaultSlideOffset = VaultPlateClosedX;

        // Sepasang daun pelat brankas geser kiri-kanan
        v5VaultSlideLeft = CreateEmptyChild("V5Vault_PlateLeft", rigRoot, new Vector3(-currentVaultSlideOffset, 0f, 0f));
        CreatePrimitivePart("V5Vault_SlabL", PrimitiveType.Cube, v5VaultSlideLeft,
            Vector3.zero, Vector3.zero, new Vector3(0.106f, 0.25f, 0.024f), v5MetalDarkMaterial);
        CreatePrimitivePart("V5Vault_TrimL", PrimitiveType.Cube, v5VaultSlideLeft,
            new Vector3(0.044f, 0f, -0.014f), Vector3.zero, new Vector3(0.012f, 0.22f, 0.008f), glowMat);

        v5VaultSlideRight = CreateEmptyChild("V5Vault_PlateRight", rigRoot, new Vector3(currentVaultSlideOffset, 0f, 0f));
        CreatePrimitivePart("V5Vault_SlabR", PrimitiveType.Cube, v5VaultSlideRight,
            Vector3.zero, Vector3.zero, new Vector3(0.106f, 0.25f, 0.024f), v5MetalDarkMaterial);
        CreatePrimitivePart("V5Vault_TrimR", PrimitiveType.Cube, v5VaultSlideRight,
            new Vector3(-0.044f, 0f, -0.014f), Vector3.zero, new Vector3(0.012f, 0.22f, 0.008f), glowMat);

        // Roda kunci mekanis di tengah pintu brankas
        v5VaultWheelPivot = CreateEmptyChild("V5Vault_LockWheel", rigRoot, new Vector3(0f, 0f, -0.022f));
        CreateProceduralMeshPart("V5Vault_WheelRim", GetSharedTorusMesh(), v5VaultWheelPivot,
            Vector3.zero, new Vector3(90f, 0f, 0f), Vector3.one * 0.056f, v5MetalSteelMaterial);
        CreatePrimitivePart("V5Vault_SpokeH", PrimitiveType.Cube, v5VaultWheelPivot,
            Vector3.zero, Vector3.zero, new Vector3(0.105f, 0.014f, 0.012f), v5MetalSteelMaterial);
        CreatePrimitivePart("V5Vault_SpokeV", PrimitiveType.Cube, v5VaultWheelPivot,
            Vector3.zero, Vector3.zero, new Vector3(0.014f, 0.105f, 0.012f), v5MetalSteelMaterial);
        CreatePrimitivePart("V5Vault_WheelHub", PrimitiveType.Sphere, v5VaultWheelPivot,
            Vector3.zero, Vector3.zero, Vector3.one * 0.032f, glowMat);
    }

    private void BuildV5ReactorPodDioramaRig(Transform variantRoot, float modelTopY, Material glowMat)
    {
        float midY = BaseTileTopSurfaceY + ((modelTopY - BaseTileTopSurfaceY) * 0.50f);
        Transform rigRoot = CreateEmptyChild("V5Pod_TurbineRig", variantRoot, new Vector3(0f, midY, 0f));

        v5PodRingA = CreateEmptyChild("V5Pod_TurbineRingA", rigRoot, Vector3.zero);
        CreateProceduralMeshPart("V5Pod_RingMeshA", GetSharedTorusMesh(), v5PodRingA,
            Vector3.zero, new Vector3(36f, 0f, 0f), Vector3.one * 0.215f, v5MetalSteelMaterial);
        CreatePrimitivePart("V5Pod_NodeA", PrimitiveType.Sphere, v5PodRingA,
            new Vector3(0.215f, 0f, 0f), Vector3.zero, Vector3.one * 0.032f, glowMat);

        v5PodRingB = CreateEmptyChild("V5Pod_TurbineRingB", rigRoot, Vector3.zero);
        CreateProceduralMeshPart("V5Pod_RingMeshB", GetSharedTorusMesh(), v5PodRingB,
            Vector3.zero, new Vector3(-36f, 90f, 0f), Vector3.one * 0.195f, glowMat);
    }

    private void BuildV5CapsuleOrbitalDioramaRig(Transform variantRoot, float modelTopY, Material glowMat)
    {
        float midY = BaseTileTopSurfaceY + ((modelTopY - BaseTileTopSurfaceY) * 0.50f);
        Transform rigRoot = CreateEmptyChild("V5Capsule_OrbitalRig", variantRoot, new Vector3(0f, midY, 0f));

        v5CapsuleOrbitA = CreateEmptyChild("V5Capsule_OrbitRingA", rigRoot, Vector3.zero);
        CreateProceduralMeshPart("V5Capsule_OrbitMeshA", GetSharedTorusMesh(), v5CapsuleOrbitA,
            Vector3.zero, new Vector3(24f, 0f, 18f), Vector3.one * 0.210f, glowMat);
        CreateProceduralMeshPart("V5Capsule_SatelliteA", GetSharedDiamondSpireMesh(), v5CapsuleOrbitA,
            new Vector3(0.210f, 0.020f, 0f), Vector3.zero, Vector3.one * 0.040f, glowMat);

        v5CapsuleOrbitB = CreateEmptyChild("V5Capsule_OrbitRingB", rigRoot, Vector3.zero);
        CreateProceduralMeshPart("V5Capsule_OrbitMeshB", GetSharedTorusMesh(), v5CapsuleOrbitB,
            Vector3.zero, new Vector3(-28f, 45f, -18f), Vector3.one * 0.170f, v5MetalSteelMaterial);
        CreateProceduralMeshPart("V5Capsule_SatelliteB", GetSharedDiamondSpireMesh(), v5CapsuleOrbitB,
            new Vector3(-0.170f, 0f, 0f), Vector3.zero, Vector3.one * 0.036f, glowMat);
    }

    private void AnimateV5OutputDioramaRig()
    {
        if (nodeType != NodeType.OutputActuator
            || outputDioramaVariantObjs == null
            || activeDioramaVariantIndex < 0
            || activeDioramaVariantIndex >= outputDioramaVariantObjs.Length)
        {
            return;
        }

        GameObject activeVariant = outputDioramaVariantObjs[activeDioramaVariantIndex];
        if (activeVariant == null || !activeVariant.activeSelf)
        {
            return;
        }

        float dt = Time.deltaTime;
        switch (activeDioramaVariantIndex)
        {
            case 0:
                if (v5SirenBeamsGroup != null && v5SirenBeamsGroup.activeSelf != currentOutputValue)
                {
                    v5SirenBeamsGroup.SetActive(currentOutputValue);
                }
                if (v5SirenBeamMaterial != null)
                {
                    v5SirenBeamMaterial.color = currentOutputValue ? SirenBeamHighColor : SirenBeamLowColor;
                }
                if (v5SirenRotorPivot != null && currentOutputValue)
                {
                    v5SirenRotorPivot.Rotate(Vector3.up, 360f * dt, Space.Self);
                }
                break;

            case 1:
                if (v5StairStepMaterials != null && v5StairStepMaterials.Length == 3)
                {
                    int targetStep;
                    if (currentOutputValue)
                    {
                        v5StairChasePhase = Mathf.Repeat(v5StairChasePhase + (dt * 4.5f), 3f);
                        targetStep = Mathf.FloorToInt(v5StairChasePhase);
                    }
                    else
                    {
                        targetStep = -1;
                    }

                    if (targetStep != lastStairChaseStep)
                    {
                        lastStairChaseStep = targetStep;
                        for (int s = 0; s < 3; s++)
                        {
                            if (v5StairStepMaterials[s] != null)
                            {
                                v5StairStepMaterials[s].color = targetStep < 0
                                    ? StairStepIdleColor
                                    : (s == targetStep ? StairStepActiveColor : StairStepTrailColor);
                            }
                        }
                    }
                }

                if (v5StairLanternHalo != null && currentOutputValue)
                {
                    v5StairLanternHalo.Rotate(Vector3.up, 180f * dt, Space.Self);
                }
                break;

            case 2:
                float targetSlide = currentOutputValue ? VaultPlateOpenX : VaultPlateClosedX;
                currentVaultSlideOffset = Mathf.Lerp(currentVaultSlideOffset, targetSlide, 1f - Mathf.Exp(-10f * dt));

                if (v5VaultSlideLeft != null)
                {
                    v5VaultSlideLeft.localPosition = new Vector3(-currentVaultSlideOffset, 0f, 0f);
                }
                if (v5VaultSlideRight != null)
                {
                    v5VaultSlideRight.localPosition = new Vector3(currentVaultSlideOffset, 0f, 0f);
                }
                if (v5VaultWheelPivot != null && currentOutputValue)
                {
                    v5VaultWheelPivot.Rotate(Vector3.forward, 240f * dt, Space.Self);
                }
                break;

            case 3:
                float podSpeed = currentOutputValue ? 240f : 20f;
                if (v5PodRingA != null)
                {
                    v5PodRingA.Rotate(new Vector3(0.25f, 1f, 0.15f), podSpeed * dt, Space.Self);
                }
                if (v5PodRingB != null)
                {
                    v5PodRingB.Rotate(new Vector3(-0.25f, -1f, 0.15f), podSpeed * 1.18f * dt, Space.Self);
                }
                break;

            case 4:
                float orbSpeed = currentOutputValue ? 260f : 22f;
                if (v5CapsuleOrbitA != null)
                {
                    v5CapsuleOrbitA.Rotate(Vector3.up, orbSpeed * dt, Space.Self);
                }
                if (v5CapsuleOrbitB != null)
                {
                    v5CapsuleOrbitB.Rotate(Vector3.up, -orbSpeed * 1.2f * dt, Space.Self);
                }
                break;
        }
    }

    /// <summary>
    /// Membangun pool 12 fragmen percikan 3D pra-alokasi (zero-GC, kebal shader stripping Android OpenGLES3)
    /// tanpa ParticleSystem eksternal.
    /// </summary>
    private void InitializeSparkBurstPool()
    {
        sparkBurstMaterial = CreateSafeOpenGLMaterial();
        if (sparkBurstMaterial != null)
        {
            sparkBurstMaterial.color = ReactiveAccentHighColor;
            managedMaterials.Add(sparkBurstMaterial);
        }

        sparkPoolRootObj = CreateEmptyChild("AR_SparkBurstPool", transform, Vector3.zero).gameObject;
        sparkTransforms = new Transform[SparkPoolSize];
        sparkLocalPositions = new Vector3[SparkPoolSize];
        sparkVelocities = new Vector3[SparkPoolSize];
        sparkInitialScales = new float[SparkPoolSize];

        Mesh sparkMesh = GetSharedDiamondSpireMesh();
        for (int i = 0; i < SparkPoolSize; i++)
        {
            GameObject frag = CreateProceduralMeshPart(
                $"SparkFrag_{i}",
                sparkMesh,
                sparkPoolRootObj.transform,
                Vector3.zero,
                Vector3.zero,
                Vector3.zero,
                sparkBurstMaterial);
            sparkTransforms[i] = frag.transform;
        }

        sparkPoolRootObj.SetActive(false);
    }

    /// <summary>
    /// Melontarkan percikan energi 3D radial singkat (~0.38 detik) dengan gravitasi ringan dan scale fade-out zero-GC.
    /// </summary>
    public void TriggerEnergyBurst(Color burstColor, float speedScale = 1f)
    {
        if (!isTracked || sparkPoolRootObj == null || sparkTransforms == null)
        {
            return;
        }

        LaunchSparkBurstInternal(
            new Vector3(0f, 0.22f, 0f),
            burstColor,
            isCelebration: false,
            duration: 0.38f,
            speedScale: Mathf.Max(0.25f, speedScale),
            baseFragScale: 0.032f);
    }

    /// <summary>
    /// Melontarkan percikan energi 3D langsung dari koordinat world space tertentu (mis. titik port tancap p0/p2 saat snap) secara zero-GC.
    /// </summary>
    public void TriggerEnergyBurstAtWorldPoint(Vector3 worldPos, Color burstColor, float speedScale = 1f)
    {
        if (!isTracked || sparkPoolRootObj == null || sparkTransforms == null)
        {
            return;
        }

        Vector3 localOrigin = transform.InverseTransformPoint(worldPos);
        LaunchSparkBurstInternal(
            localOrigin,
            burstColor,
            isCelebration: false,
            duration: 0.34f,
            speedScale: Mathf.Max(0.25f, speedScale),
            baseFragScale: 0.028f);
    }

    /// <summary>
    /// Selebrasi percikan 3D emas-sian meriah di atas diorama OutputActuator saat verifikasi level dinyatakan lulus.
    /// </summary>
    public void TriggerVictoryCelebration()
    {
        if (!isTracked || sparkPoolRootObj == null || sparkTransforms == null)
        {
            return;
        }

        LaunchSparkBurstInternal(
            new Vector3(0f, 0.30f, 0f),
            VictoryBurstGoldColor,
            isCelebration: true,
            duration: 0.62f,
            speedScale: 1.65f,
            baseFragScale: 0.048f);
    }

    private void LaunchSparkBurstInternal(
        Vector3 localOrigin,
        Color baseColor,
        bool isCelebration,
        float duration,
        float speedScale,
        float baseFragScale)
    {
        isSparkBurstActive = true;
        isSparkCelebrationMode = isCelebration;
        sparkBurstElapsed = 0f;
        sparkBurstDuration = Mathf.Max(0.10f, duration);
        sparkBurstBaseColor = baseColor;

        float phaseSeed = Mathf.Repeat(Time.time * 2.3f, Mathf.PI * 2f);
        const float goldenAngle = 2.39996323f;

        for (int i = 0; i < SparkPoolSize; i++)
        {
            float angle = phaseSeed + (i * goldenAngle);
            float radialSpeed = (0.48f + ((i % 3) * 0.16f)) * speedScale;
            float upwardSpeed = ((isCelebration ? 0.85f : 0.56f) + ((i % 4) * 0.12f)) * speedScale;

            sparkLocalPositions[i] = localOrigin;
            sparkVelocities[i] = new Vector3(
                Mathf.Cos(angle) * radialSpeed,
                upwardSpeed,
                Mathf.Sin(angle) * radialSpeed);
            sparkInitialScales[i] = baseFragScale * (0.82f + ((i % 3) * 0.18f));

            Transform st = sparkTransforms[i];
            if (st != null)
            {
                st.localPosition = sparkLocalPositions[i];
                st.localScale = Vector3.one * sparkInitialScales[i];
                st.localRotation = Quaternion.Euler(i * 31f, i * 47f, i * 19f);
            }
        }

        if (sparkBurstMaterial != null)
        {
            sparkBurstMaterial.color = baseColor;
        }

        if (!sparkPoolRootObj.activeSelf)
        {
            sparkPoolRootObj.SetActive(true);
        }
    }

    private void UpdateSparkBurstPool()
    {
        if (!isSparkBurstActive || sparkTransforms == null)
        {
            return;
        }

        float dt = Time.deltaTime;
        sparkBurstElapsed += dt;
        float t = Mathf.Clamp01(sparkBurstElapsed / sparkBurstDuration);

        if (t >= 1f)
        {
            ResetSparkBurstPool();
            return;
        }

        float scaleFade = 1f - (t * t);
        float gravity = isSparkCelebrationMode ? 1.85f : 1.65f;

        if (sparkBurstMaterial != null)
        {
            Color col = isSparkCelebrationMode
                ? Color.Lerp(VictoryBurstGoldColor, VictoryBurstCyanColor, t)
                : sparkBurstBaseColor;
            col.a = Mathf.Clamp01(1f - t);
            sparkBurstMaterial.color = col;
        }

        for (int i = 0; i < SparkPoolSize; i++)
        {
            sparkVelocities[i].y -= gravity * dt;
            sparkLocalPositions[i] += sparkVelocities[i] * dt;

            Transform st = sparkTransforms[i];
            if (st != null)
            {
                st.localPosition = sparkLocalPositions[i];
                st.localScale = Vector3.one * (sparkInitialScales[i] * scaleFade);
                st.Rotate(new Vector3(180f, 240f, 120f) * dt, Space.Self);
            }
        }
    }

    private void ResetSparkBurstPool()
    {
        isSparkBurstActive = false;
        isSparkCelebrationMode = false;
        sparkBurstElapsed = 0f;

        if (sparkPoolRootObj != null && sparkPoolRootObj.activeSelf)
        {
            sparkPoolRootObj.SetActive(false);
        }
    }
}
