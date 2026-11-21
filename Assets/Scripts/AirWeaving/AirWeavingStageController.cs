using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pengendali Ruang Meja Hologram 3D Horizontal (GateWeaver AR v8.0 Tahap 4.5):
/// - Pivot 3D <c>HologramStageRoot</c> di depan <c>Camera.main</c> dengan bidang meja horizontal nyata (bidang <c>XZ</c> lokal,
///   normal atas meja = <c>+Y</c> / <c>stageRoot.up</c>) yang dilihat dari sudut depan-atas (<c>DefaultElevationPitchDeg = 36°</c>)
///   sehingga membentuk perspektif trapesium 3D yang tegas (tepi belakang lebih tinggi &amp; menyempit alami dibanding tepi depan).
/// - 100% bebas kontrol Mouse / Touchscreen di Mode Udara: Orbit 360° (<c>Yaw</c> bebas 360°, <c>Pitch</c> 15°..68°),
///   Air-Zoom (0.68x..1.65x), dan Reset Panggung dikendalikan sepenuhnya oleh gestur 2 tangan di udara (<c>Both Hands Pinch</c>
///   &amp; <c>Both Hands Fist</c>) dari <c>AirWeavingController</c>, ditambah Dynamic 3D Hand Parallax (±7° yaw, ±4° pitch) saat 1 tangan.
/// - Lantai Proyeksi Hologram 3D di bidang <c>XZ</c> (<c>y = 0</c>) + Cincin Bayangan Elips (Grounded Contact Rings) +
///   Tiang Proyeksi Vertikal tegak lurus (<c>+Y</c>) menuju setiap node yang melayang di atas permukaan meja (<c>BaseHoverHeight = +0.26</c>,
///   terangkat hingga <c>~+0.58</c> saat dikepal dengan pelebaran cincin bayangan <c>+55%</c>).
/// - 100% Zero-GC di runtime loop, kompatibel Built-in Render Pipeline OpenGLES3, dan nonaktif bersih di Mode Kartu v5.
/// </summary>
public sealed class AirWeavingStageController : MonoBehaviour
{
    public const float DefaultElevationPitchDeg = 36.0f;
    public const float DefaultYawDeg = 0.0f;
    public const float MinElevationPitchDeg = -80.0f;
    public const float MaxElevationPitchDeg = 85.0f;

    public const float DefaultZoomScale = 1.00f;
    public const float MinZoomScale = 0.42f;
    public const float MaxZoomScale = 2.85f;

    public const float MaxPanOffsetX = 2.50f;
    public const float MaxPanOffsetY = 1.75f;
    public const float MaxPanDepthPullCloserZ = 2.25f;
    public const float MaxPanDepthPushFartherZ = 2.80f;

    public const float MaxParallaxYawDeg = 7.0f;
    public const float MaxParallaxPitchDeg = 4.0f;

    // Geometri vertikal di atas permukaan meja XZ (y = 0 adalah permukaan lantai meja) — Tahap 4.7 Full Free-View 6-DoF
    public const float StagePivotCameraOffsetY = -0.32f;
    public const float StageFloorLocalY = 0.000f;
    public const float ContactRingLocalY = 0.003f;
    public const float BaseHoverHeight = 0.220f;
    public const float GrabExtraLiftHeight = 0.240f;
    public const float ContactRingLiftExpandFactor = 0.50f;

    private const float OrbitSpringOmega = 15.0f;
    private const float ZoomSpringOmega = 14.0f;
    private const float PanSpringOmega = 14.0f;
    private const float ParallaxSpringOmega = 7.5f;

    // Parameter 3D Differential-Depth Twist & Anchor Z Halus pada Mode 2 Tangan Both Pinch (Tahap 4.8 -> Polish 4.9)
    private const float TwoHandTwistAsymDeadzone = 0.025f;
    private const float TwoHandTwistYawDegPerAsym = 140.0f;
    private const float TwoHandOrbitDepthDeadzone = 0.040f;
    private const float TwoHandOrbitDepthGain = 0.68f;
    private const float TwoHandOrbitDepthSpanMeters = 1.05f;

    // Parameter Inertial Coasting (efek ngerem / momentum meluncur halus pasca lepas 2 tangan) — Tahap 4.9
    private const float StageCoastDamping = 4.8f;
    private const float StageVelocityTrackRate = 14.0f;
    private const float MaxCoastYawDegPerSec = 190.0f;
    private const float MaxCoastPitchDegPerSec = 110.0f;
    private const float MaxCoastZoomPerSec = 1.40f;
    private const float MaxCoastPanUnitsPerSec = 2.10f;
    private const float StageCoastStopEpsilon = 0.0005f;

    private const int GridInternalVerticalLines = 9;
    private const int GridInternalHorizontalLines = 7;
    private const int ContactRingSegments = 28;
    private const int MaxContactRingSlots = 8; // 0: InputA, 1: InputB, 2: Output, 3..7: 5 Gerbang Logika

    private static readonly Color FloorDeckGlassColor = new Color(0.02f, 0.14f, 0.26f, 0.20f);
    private static readonly Color FloorGridLineColor = new Color(0.16f, 0.88f, 1.00f, 0.20f);
    private static readonly Color FloorCenterAxisColor = new Color(0.26f, 0.94f, 1.00f, 0.34f);
    private static readonly Color FloorWingLineColor = new Color(0.18f, 0.88f, 1.00f, 0.32f);
    private static readonly Color FloorOuterBorderColor = new Color(0.18f, 0.90f, 1.00f, 0.62f);
    private static readonly Color FloorInnerArenaBorderColor = new Color(0.22f, 0.92f, 1.00f, 0.44f);
    private static readonly Color FloorOuterShadowColor = new Color(0.02f, 0.06f, 0.12f, 0.68f);
    private static readonly Color FloorDeleteWarningColor = new Color(1.00f, 0.26f, 0.22f, 0.96f);
    private static readonly Color FloorDeleteShadowColor = new Color(0.22f, 0.02f, 0.02f, 0.78f);

    private static readonly Color ContactInputAColor = new Color(0.24f, 0.94f, 1.00f, 0.72f);
    private static readonly Color ContactInputBColor = new Color(1.00f, 0.86f, 0.26f, 0.72f);
    private static readonly Color ContactOutputColor = new Color(0.28f, 0.98f, 0.76f, 0.74f);
    private static readonly Color ContactGateRestColor = new Color(0.22f, 0.92f, 1.00f, 0.70f);
    private static readonly Color ContactGateParkedColor = new Color(0.20f, 0.86f, 1.00f, 0.56f);
    private static readonly Color ContactGateLiftedColor = new Color(1.00f, 0.88f, 0.30f, 0.68f);
    private static readonly Color ContactGateDeleteColor = new Color(1.00f, 0.26f, 0.22f, 0.86f);

    public struct ContactNodeDescriptor
    {
        public bool active;
        public Vector3 stageLocalPos; // (X = kiri-kanan, Y = tinggi melayang di atas meja, Z = kedalaman depan-belakang meja)
        public float baseRadius;
        public bool isInputA;
        public bool isInputB;
        public bool isOutput;
        public bool isParked;
        public bool isGrabbed;
        public bool isInDeleteZone;
    }

    private GameObject stageRootObj;
    private Transform stageRootTransform;
    private GameObject floorVisualGroupObj;

    private MeshFilter floorDeckMeshFilter;
    private MeshRenderer floorDeckQuadRenderer;
    private Mesh floorDeckOctagonMesh;
    private Material floorDeckQuadMat;
    private readonly Vector3[] floorDeckVerticesBuffer = new Vector3[9];

    private LineRenderer outerBorderShadowRenderer;
    private LineRenderer outerBorderGlowRenderer;
    private LineRenderer innerArenaBorderRenderer;
    private LineRenderer[] cornerShadowRenderers;
    private LineRenderer[] cornerBracketRenderers;
    private LineRenderer[] verticalGridRenderers;
    private LineRenderer[] horizontalGridRenderers;
    private LineRenderer[] wingGuideRenderers;

    private Material outerBorderShadowMat;
    private Material outerBorderGlowMat;
    private Material innerArenaBorderMat;
    private Material cornerShadowMat;
    private Material cornerBracketMat;
    private Material gridLineMat;
    private Material centerAxisMat;
    private Material wingLineMat;

    private LineRenderer[] contactOuterRings;
    private LineRenderer[] contactInnerRings;
    private LineRenderer[] contactElevationStems;
    private Material[] contactOuterMats;
    private Material[] contactInnerMats;
    private Material[] contactStemMats;

    private readonly List<Material> ownedMaterials = new List<Material>(40);
    private readonly Vector3[] borderLoopBuffer = new Vector3[8];
    private readonly Vector3[] cornerPointsBuffer = new Vector3[3];
    private readonly Vector3[] ringLocalBuffer = new Vector3[ContactRingSegments];
    private readonly Vector3[] stemLocalBuffer = new Vector3[2];

    // State Air-Orbit 360°, Air-Zoom, & Dynamic Hand Parallax
    private float targetOrbitYawDeg = DefaultYawDeg;
    private float targetOrbitPitchDeg = DefaultElevationPitchDeg;
    private float currentOrbitYawDeg = DefaultYawDeg;
    private float currentOrbitPitchDeg = DefaultElevationPitchDeg;
    private float velocityOrbitYaw = 0f;
    private float velocityOrbitPitch = 0f;

    private float targetZoomScale = DefaultZoomScale;
    private float currentZoomScale = DefaultZoomScale;
    private float velocityZoom = 0f;

    private Vector3 targetPanOffset = Vector3.zero;
    private Vector3 currentPanOffset = Vector3.zero;
    private Vector3 velocityPanOffset = Vector3.zero;

    private float currentParallaxYawDeg = 0f;
    private float currentParallaxPitchDeg = 0f;
    private float velocityParallaxYaw = 0f;
    private float velocityParallaxPitch = 0f;

    private bool isTwoHandManipulationActive = false;
    private bool wasTwoHandManipulationActive = false;
    private bool hasTwoHandOrbitDepthAnchor = false;
    private float twoHandAnchorAsym = 0f;
    private float twoHandAppliedTwistYawDeg = 0f;
    private float twoHandAnchorAvgSpan = 0.172f;
    private float twoHandAnchorPanZ = 0f;

    // State Inertial Coasting 2 Tangan (smoothed velocity & target snapshot) — Tahap 4.9
    private float coastYawVelocityDeg = 0f;
    private float coastPitchVelocityDeg = 0f;
    private float coastZoomVelocity = 0f;
    private Vector3 coastPanVelocity = Vector3.zero;
    private float prevManipTargetYawDeg = DefaultYawDeg;
    private float prevManipTargetPitchDeg = DefaultElevationPitchDeg;
    private float prevManipTargetZoomScale = DefaultZoomScale;
    private Vector3 prevManipTargetPanOffset = Vector3.zero;

    private bool isInitialized = false;
    private float lastBuiltGridAspectScale = -1f;

    public Transform StageRoot => stageRootTransform;
    public float CurrentZoomScale => currentZoomScale;
    public float TargetZoomScale => targetZoomScale;
    public Vector3 CurrentPanOffset => currentPanOffset;
    public Vector3 TargetPanOffset => targetPanOffset;
    public float EffectivePitchDeg => currentOrbitPitchDeg + currentParallaxPitchDeg;
    public float EffectiveYawDeg => currentOrbitYawDeg + currentParallaxYawDeg;
    public bool IsTwoHandManipulationActive => isTwoHandManipulationActive;

    public void EnsureInitialized(Camera mainCam)
    {
        if (isInitialized)
        {
            return;
        }

        stageRootObj = new GameObject("HologramStageRoot");
        stageRootObj.transform.SetParent(transform, false);
        stageRootTransform = stageRootObj.transform;

        floorVisualGroupObj = new GameObject("HoloStage_HorizontalTableFloor3D");
        floorVisualGroupObj.transform.SetParent(stageRootTransform, false);

        // 1. Material Lantai Proyeksi & Grid Sci-Fi (Sprites/Default aman OpenGLES3)
        floorDeckQuadMat = CreateSafeSpritesMaterial(FloorDeckGlassColor, 2975);
        gridLineMat = CreateSafeSpritesMaterial(FloorGridLineColor, 2977);
        centerAxisMat = CreateSafeSpritesMaterial(FloorCenterAxisColor, 2978);
        wingLineMat = CreateSafeSpritesMaterial(FloorWingLineColor, 2978);
        innerArenaBorderMat = CreateSafeSpritesMaterial(FloorInnerArenaBorderColor, 2979);
        outerBorderShadowMat = CreateSafeSpritesMaterial(FloorOuterShadowColor, 2979);
        outerBorderGlowMat = CreateSafeSpritesMaterial(FloorOuterBorderColor, 2980);
        cornerShadowMat = CreateSafeSpritesMaterial(FloorOuterShadowColor, 2980);
        cornerBracketMat = CreateSafeSpritesMaterial(FloorOuterBorderColor, 2981);

        // 2. Bidang Kaca Meja Hologram Horizontal Utuh (Mesh Oktagon Ber-Chamfer rata di bidang XZ lokal panggung)
        GameObject deckObj = new GameObject("HoloStage_FloorGlassDeck");
        deckObj.transform.SetParent(floorVisualGroupObj.transform, false);
        floorDeckMeshFilter = deckObj.AddComponent<MeshFilter>();
        floorDeckQuadRenderer = deckObj.AddComponent<MeshRenderer>();
        floorDeckQuadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        floorDeckQuadRenderer.receiveShadows = false;
        if (floorDeckQuadMat != null)
        {
            floorDeckQuadRenderer.sharedMaterial = floorDeckQuadMat;
        }
        floorDeckOctagonMesh = new Mesh { name = "HoloStage_UnifiedOctagonDeckMesh" };
        floorDeckMeshFilter.sharedMesh = floorDeckOctagonMesh;

        // 3. Bingkai Luar Dek Meja Utuh + Bingkai Dalam Zona Rakit (Octagon/Chamfered Sci-Fi Frame di bidang XZ meja)
        outerBorderShadowRenderer = CreateLocalLine("HoloStage_OuterBorderShadow", floorVisualGroupObj.transform, 8, loop: true, width: 0.0130f, outerBorderShadowMat);
        outerBorderGlowRenderer = CreateLocalLine("HoloStage_OuterBorderGlow", floorVisualGroupObj.transform, 8, loop: true, width: 0.0075f, outerBorderGlowMat);
        innerArenaBorderRenderer = CreateLocalLine("HoloStage_InnerArenaBorder", floorVisualGroupObj.transform, 8, loop: true, width: 0.0052f, innerArenaBorderMat);

        // 4. 4 Siku Sudut Zona Rakit Dipertegas di Permukaan Meja XZ
        cornerShadowRenderers = new LineRenderer[4];
        cornerBracketRenderers = new LineRenderer[4];
        for (int c = 0; c < 4; c++)
        {
            cornerShadowRenderers[c] = CreateLocalLine($"HoloStage_CornerShadow_{c}", floorVisualGroupObj.transform, 3, loop: false, width: 0.0135f, cornerShadowMat);
            cornerBracketRenderers[c] = CreateLocalLine($"HoloStage_CornerBracket_{c}", floorVisualGroupObj.transform, 3, loop: false, width: 0.0085f, cornerBracketMat);
        }

        // 5. Grid Proyeksi Sci-Fi Internal di Bidang XZ Meja Utuh (Garis Bujur X & Garis Lintang Z)
        verticalGridRenderers = new LineRenderer[GridInternalVerticalLines];
        for (int v = 0; v < GridInternalVerticalLines; v++)
        {
            bool isCenter = v == (GridInternalVerticalLines / 2);
            verticalGridRenderers[v] = CreateLocalLine(
                $"HoloStage_GridX_{v}",
                floorVisualGroupObj.transform,
                2,
                loop: false,
                width: isCenter ? 0.0042f : 0.0026f,
                isCenter ? centerAxisMat : gridLineMat);
        }

        horizontalGridRenderers = new LineRenderer[GridInternalHorizontalLines];
        for (int h = 0; h < GridInternalHorizontalLines; h++)
        {
            bool isCenter = h == (GridInternalHorizontalLines / 2);
            horizontalGridRenderers[h] = CreateLocalLine(
                $"HoloStage_GridZ_{h}",
                floorVisualGroupObj.transform,
                2,
                loop: false,
                width: isCenter ? 0.0042f : 0.0026f,
                isCenter ? centerAxisMat : gridLineMat);
        }

        // 6. Garis Pembatas & Dermaga Sayap Input (Kiri), Sayap Output (Kanan), & Teras Rak Parkir (Belakang Meja)
        wingGuideRenderers = new LineRenderer[3];
        wingGuideRenderers[0] = CreateLocalLine("HoloStage_LeftInputWingBay", floorVisualGroupObj.transform, 4, loop: true, width: 0.0038f, wingLineMat);
        wingGuideRenderers[1] = CreateLocalLine("HoloStage_RightOutputWingBay", floorVisualGroupObj.transform, 4, loop: true, width: 0.0038f, wingLineMat);
        wingGuideRenderers[2] = CreateLocalLine("HoloStage_RearRackTerraceBay", floorVisualGroupObj.transform, 4, loop: true, width: 0.0038f, wingLineMat);

        // 7. Pool 8 Grounded Contact Rings (Terbaring rata di bidang XZ meja -> tampak sebagai elips perspektif 3D + Tiang Vertikal Y)
        contactOuterRings = new LineRenderer[MaxContactRingSlots];
        contactInnerRings = new LineRenderer[MaxContactRingSlots];
        contactElevationStems = new LineRenderer[MaxContactRingSlots];
        contactOuterMats = new Material[MaxContactRingSlots];
        contactInnerMats = new Material[MaxContactRingSlots];
        contactStemMats = new Material[MaxContactRingSlots];

        for (int i = 0; i < MaxContactRingSlots; i++)
        {
            contactOuterMats[i] = CreateSafeSpritesMaterial(ContactGateRestColor, 2982);
            contactInnerMats[i] = CreateSafeSpritesMaterial(ContactGateRestColor, 2981);
            contactStemMats[i] = CreateSafeSpritesMaterial(ContactGateRestColor, 2981);

            contactOuterRings[i] = CreateLocalLine($"HoloStage_ContactOuter_{i}", floorVisualGroupObj.transform, ContactRingSegments, loop: true, width: 0.0072f, contactOuterMats[i]);
            contactInnerRings[i] = CreateLocalLine($"HoloStage_ContactInner_{i}", floorVisualGroupObj.transform, ContactRingSegments, loop: true, width: 0.0040f, contactInnerMats[i]);
            contactElevationStems[i] = CreateLocalLine($"HoloStage_ContactStem_{i}", floorVisualGroupObj.transform, 2, loop: false, width: 0.0040f, contactStemMats[i]);
            contactOuterRings[i].enabled = false;
            contactInnerRings[i].enabled = false;
            contactElevationStems[i].enabled = false;
        }

        isInitialized = true;
        ResetViewToDefault(immediate: true);
        if (mainCam != null)
        {
            RebuildStaticFloorGridGeometry(
                ComputeAspectScale(mainCam),
                assemblyMinBaseX: -0.74f,
                assemblyMaxBaseX: 0.76f,
                assemblyMinZ: -0.78f,
                assemblyMaxZ: 0.46f,
                inputBaseX: -1.26f,
                outputBaseX: 1.28f,
                parkingRackZ: 0.82f);
        }
        HideImmediate();
    }

    public void SetStageVisible(bool visible)
    {
        if (!isInitialized || stageRootObj == null)
        {
            return;
        }

        if (stageRootObj.activeSelf != visible)
        {
            stageRootObj.SetActive(visible);
        }
    }

    private void ClearInertialCoastVelocities()
    {
        coastYawVelocityDeg = 0f;
        coastPitchVelocityDeg = 0f;
        coastZoomVelocity = 0f;
        coastPanVelocity = Vector3.zero;
    }

    public void HideImmediate()
    {
        isTwoHandManipulationActive = false;
        wasTwoHandManipulationActive = false;
        hasTwoHandOrbitDepthAnchor = false;
        twoHandAppliedTwistYawDeg = 0f;
        ClearInertialCoastVelocities();

        if (!isInitialized)
        {
            return;
        }

        if (contactOuterRings != null)
        {
            for (int i = 0; i < MaxContactRingSlots; i++)
            {
                if (contactOuterRings[i] != null) contactOuterRings[i].enabled = false;
                if (contactInnerRings[i] != null) contactInnerRings[i].enabled = false;
                if (contactElevationStems[i] != null) contactElevationStems[i].enabled = false;
            }
        }

        if (stageRootObj != null && stageRootObj.activeSelf)
        {
            stageRootObj.SetActive(false);
        }
    }

    public void ResetViewToDefault(bool immediate = false)
    {
        // Pantulkan yaw ke kelipatan 360° terdekat agar pegas selalu mengambil busur terpendek menuju 0°
        float wrappedCurrentYaw = Mathf.Repeat(currentOrbitYawDeg + 180f, 360f) - 180f;
        currentOrbitYawDeg = wrappedCurrentYaw;
        targetOrbitYawDeg = DefaultYawDeg;
        targetOrbitPitchDeg = DefaultElevationPitchDeg;
        targetZoomScale = DefaultZoomScale;
        targetPanOffset = Vector3.zero;
        isTwoHandManipulationActive = false;
        wasTwoHandManipulationActive = false;
        hasTwoHandOrbitDepthAnchor = false;
        twoHandAppliedTwistYawDeg = 0f;
        ClearInertialCoastVelocities();

        if (immediate)
        {
            currentOrbitYawDeg = DefaultYawDeg;
            currentOrbitPitchDeg = DefaultElevationPitchDeg;
            currentZoomScale = DefaultZoomScale;
            currentPanOffset = Vector3.zero;
            velocityOrbitYaw = 0f;
            velocityOrbitPitch = 0f;
            velocityZoom = 0f;
            velocityPanOffset = Vector3.zero;
            currentParallaxYawDeg = 0f;
            currentParallaxPitchDeg = 0f;
            velocityParallaxYaw = 0f;
            velocityParallaxPitch = 0f;
        }
    }

    public void ResetStageView(bool immediate)
    {
        ResetViewToDefault(immediate);
    }

    /// <summary>
    /// Mengaktifkan / menonaktifkan status manipulasi panggung 2 tangan (Air-Orbit, Air-Zoom, &amp; Free Air-Pan).
    /// </summary>
    public void SetTwoHandManipulationState(bool active)
    {
        isTwoHandManipulationActive = active;
        if (!active)
        {
            hasTwoHandOrbitDepthAnchor = false;
            twoHandAppliedTwistYawDeg = 0f;
        }
    }

    /// <summary>
    /// Mengunci jangkar awal kedalaman 2 tangan (kiri vs kanan layar) saat memasuki mode <c>Both Pinch</c>
    /// untuk 3D Differential-Depth Twist (putar nampan) &amp; dorong-tarik Z halus. Zero-GC.
    /// </summary>
    public void BeginTwoHandOrbitZoomAnchor(float leftScreenPalmSpan, float rightScreenPalmSpan)
    {
        isTwoHandManipulationActive = true;
        ClearInertialCoastVelocities();
        float safeLeft = Mathf.Max(0.055f, leftScreenPalmSpan);
        float safeRight = Mathf.Max(0.055f, rightScreenPalmSpan);
        twoHandAnchorAsym = (safeRight - safeLeft) / Mathf.Max(0.001f, safeRight + safeLeft);
        twoHandAppliedTwistYawDeg = 0f;
        twoHandAnchorAvgSpan = 0.5f * (safeLeft + safeRight);
        twoHandAnchorPanZ = targetPanOffset.z;
        hasTwoHandOrbitDepthAnchor = true;
    }

    /// <summary>
    /// Menerapkan delta Air-Orbit Free-View (<c>yawDeltaDeg</c> bebas 360° penuh, <c>pitchDeltaDeg</c> bebas -80°..+85°),
    /// target Air-Zoom (<c>desiredZoomScale</c> 0.42x..2.85x) dengan Focal-Point Zoom menuju titik tengah 2 tangan,
    /// serta 3D Differential-Depth Twist &amp; dorong-tarik Z halus bila ukuran telapak kiri/kanan layar diberikan.
    /// </summary>
    public void ApplyTwoHandOrbitAndZoom(
        float yawDeltaDeg,
        float pitchDeltaDeg,
        float desiredZoomScale,
        Vector2 focalMidpointCameraXY = default,
        float leftScreenPalmSpan = -1f,
        float rightScreenPalmSpan = -1f)
    {
        isTwoHandManipulationActive = true;
        float totalYawDeltaDeg = yawDeltaDeg;

        // 3D Differential-Depth Twist (Putar Meja Seperti Memegang Nampan) + Dorong-Tarik Z Halus (Front-Cam Mirror Depth)
        if (leftScreenPalmSpan > 0f && rightScreenPalmSpan > 0f)
        {
            float safeLeft = Mathf.Max(0.055f, leftScreenPalmSpan);
            float safeRight = Mathf.Max(0.055f, rightScreenPalmSpan);
            float curAsym = (safeRight - safeLeft) / Mathf.Max(0.001f, safeRight + safeLeft);
            float curAvgSpan = 0.5f * (safeLeft + safeRight);

            if (!hasTwoHandOrbitDepthAnchor)
            {
                twoHandAnchorAsym = curAsym;
                twoHandAppliedTwistYawDeg = 0f;
                twoHandAnchorAvgSpan = curAvgSpan;
                twoHandAnchorPanZ = targetPanOffset.z;
                hasTwoHandOrbitDepthAnchor = true;
            }
            else
            {
                // Mendorong tangan kanan layar maju ke lensa (rightSpan membesar -> deltaAsym > 0) -> Yaw negatif (sisi kanan meja masuk +Z)
                // Mendorong tangan kiri layar maju ke lensa (leftSpan membesar -> deltaAsym < 0) -> Yaw positif (sisi kiri meja masuk +Z)
                float rawDeltaAsym = curAsym - twoHandAnchorAsym;
                float effectiveDeltaAsym = 0f;
                if (Mathf.Abs(rawDeltaAsym) > TwoHandTwistAsymDeadzone)
                {
                    effectiveDeltaAsym = rawDeltaAsym - (Mathf.Sign(rawDeltaAsym) * TwoHandTwistAsymDeadzone);
                }

                float desiredTwistYawDeg = -effectiveDeltaAsym * TwoHandTwistYawDegPerAsym;
                float twistYawDeltaDeg = desiredTwistYawDeg - twoHandAppliedTwistYawDeg;
                twoHandAppliedTwistYawDeg = desiredTwistYawDeg;
                totalYawDeltaDeg += twistYawDeltaDeg;

                // Dorong kedua tangan maju bersama (curAvgSpan > anchor) -> meja masuk lebih dalam (+Z); tarik mundur -> meja mendekat (-Z)
                float avgSpanRatio = Mathf.Pow(curAvgSpan / Mathf.Max(0.055f, twoHandAnchorAvgSpan), TwoHandOrbitDepthGain);
                float deltaRatio = avgSpanRatio - 1.0f;
                float effectiveDepthDelta = 0f;
                if (Mathf.Abs(deltaRatio) > TwoHandOrbitDepthDeadzone)
                {
                    effectiveDepthDelta = deltaRatio - (Mathf.Sign(deltaRatio) * TwoHandOrbitDepthDeadzone);
                }

                float desiredPanZ = twoHandAnchorPanZ + (effectiveDepthDelta * TwoHandOrbitDepthSpanMeters);
                targetPanOffset.z = Mathf.Clamp(desiredPanZ, -MaxPanDepthPullCloserZ, MaxPanDepthPushFartherZ);
            }
        }

        targetOrbitYawDeg += totalYawDeltaDeg;
        targetOrbitPitchDeg = Mathf.Clamp(targetOrbitPitchDeg + pitchDeltaDeg, MinElevationPitchDeg, MaxElevationPitchDeg);

        float prevZoom = Mathf.Max(0.10f, targetZoomScale);
        float newZoom = Mathf.Clamp(desiredZoomScale, MinZoomScale, MaxZoomScale);
        float zoomRatio = newZoom / prevZoom;
        targetZoomScale = newZoom;

        // Focal-Point Zoom: geser pan XY proporsional terhadap titik tengah kedua tangan agar zoom langsung menuju sudut/area yang dibidik
        if (Mathf.Abs(zoomRatio - 1.0f) > 0.0001f)
        {
            Vector2 focalOffsetFromStage = focalMidpointCameraXY - new Vector2(targetPanOffset.x, targetPanOffset.y);
            Vector2 focalShift = focalOffsetFromStage * (1.0f - zoomRatio) * 0.85f;
            targetPanOffset.x = Mathf.Clamp(targetPanOffset.x + focalShift.x, -MaxPanOffsetX, MaxPanOffsetX);
            targetPanOffset.y = Mathf.Clamp(targetPanOffset.y + focalShift.y, -MaxPanOffsetY, MaxPanOffsetY);
        }
    }

    /// <summary>
    /// Menerapkan Free Air-Pan 3D (1 Tangan Pinch + 1 Tangan Fist): menggeser panggung meja bebas ke kiri/kanan/atas/bawah
    /// serta maju/mundur mengikuti kedalaman kedua tangan. Zero-GC.
    /// </summary>
    public void ApplyTwoHandFreePan(Vector2 panDeltaXY, float desiredPanZ)
    {
        isTwoHandManipulationActive = true;
        targetPanOffset.x = Mathf.Clamp(targetPanOffset.x + panDeltaXY.x, -MaxPanOffsetX, MaxPanOffsetX);
        targetPanOffset.y = Mathf.Clamp(targetPanOffset.y + panDeltaXY.y, -MaxPanOffsetY, MaxPanOffsetY);
        targetPanOffset.z = Mathf.Clamp(desiredPanZ, -MaxPanDepthPullCloserZ, MaxPanDepthPushFartherZ);
    }

    /// <summary>
    /// Memperbarui pegas analitik Air-Orbit 360° (-80°..+85° pitch), Air-Zoom (0.42x..2.85x), Free Air-Pan 3D,
    /// dan Dynamic Hand Parallax, lalu menerapkan transform dunia meja hologram horizontal <c>HologramStageRoot</c>. Zero-GC.
    /// </summary>
    public void TickStageTransform(
        Camera mainCam,
        float dt,
        bool isAirWeavingActive,
        bool isUiOverlayOpen,
        bool isHandDetected,
        Vector2 palmCameraLocalXY,
        float workbenchDepth)
    {
        if (!isInitialized)
        {
            EnsureInitialized(mainCam);
        }

        if (!isAirWeavingActive || mainCam == null || stageRootTransform == null)
        {
            HideImmediate();
            return;
        }

        if (!stageRootObj.activeSelf)
        {
            stageRootObj.SetActive(true);
        }

        if (isUiOverlayOpen)
        {
            isTwoHandManipulationActive = false;
            wasTwoHandManipulationActive = false;
            ClearInertialCoastVelocities();
        }

        // 0. Catat smoothed velocity saat manipulasi 2 tangan aktif & terapkan Inertial Coasting halus saat dilepas (Tahap 4.9)
        if (isTwoHandManipulationActive)
        {
            if (!wasTwoHandManipulationActive)
            {
                ClearInertialCoastVelocities();
                prevManipTargetYawDeg = targetOrbitYawDeg;
                prevManipTargetPitchDeg = targetOrbitPitchDeg;
                prevManipTargetZoomScale = targetZoomScale;
                prevManipTargetPanOffset = targetPanOffset;
                wasTwoHandManipulationActive = true;
            }
            else if (dt > 0.0001f)
            {
                float invDt = 1f / dt;
                float instYawVel = (targetOrbitYawDeg - prevManipTargetYawDeg) * invDt;
                float instPitchVel = (targetOrbitPitchDeg - prevManipTargetPitchDeg) * invDt;
                float instZoomVel = (targetZoomScale - prevManipTargetZoomScale) * invDt;
                Vector3 instPanVel = (targetPanOffset - prevManipTargetPanOffset) * invDt;

                float trackAlpha = 1f - Mathf.Exp(-StageVelocityTrackRate * dt);
                coastYawVelocityDeg = Mathf.Clamp(
                    Mathf.Lerp(coastYawVelocityDeg, instYawVel, trackAlpha),
                    -MaxCoastYawDegPerSec,
                    MaxCoastYawDegPerSec);
                coastPitchVelocityDeg = Mathf.Clamp(
                    Mathf.Lerp(coastPitchVelocityDeg, instPitchVel, trackAlpha),
                    -MaxCoastPitchDegPerSec,
                    MaxCoastPitchDegPerSec);
                coastZoomVelocity = Mathf.Clamp(
                    Mathf.Lerp(coastZoomVelocity, instZoomVel, trackAlpha),
                    -MaxCoastZoomPerSec,
                    MaxCoastZoomPerSec);
                coastPanVelocity = Vector3.Lerp(coastPanVelocity, instPanVel, trackAlpha);
                float panSpeedSqr = coastPanVelocity.sqrMagnitude;
                if (panSpeedSqr > MaxCoastPanUnitsPerSec * MaxCoastPanUnitsPerSec)
                {
                    coastPanVelocity *= MaxCoastPanUnitsPerSec / Mathf.Sqrt(panSpeedSqr);
                }

                prevManipTargetYawDeg = targetOrbitYawDeg;
                prevManipTargetPitchDeg = targetOrbitPitchDeg;
                prevManipTargetZoomScale = targetZoomScale;
                prevManipTargetPanOffset = targetPanOffset;
            }
        }
        else
        {
            wasTwoHandManipulationActive = false;
            if (!isUiOverlayOpen && dt > 0.00001f)
            {
                if (Mathf.Abs(coastYawVelocityDeg) > StageCoastStopEpsilon)
                {
                    targetOrbitYawDeg += coastYawVelocityDeg * dt;
                }

                if (Mathf.Abs(coastPitchVelocityDeg) > StageCoastStopEpsilon)
                {
                    float nextPitch = targetOrbitPitchDeg + (coastPitchVelocityDeg * dt);
                    if (nextPitch <= MinElevationPitchDeg)
                    {
                        targetOrbitPitchDeg = MinElevationPitchDeg;
                        coastPitchVelocityDeg = 0f;
                    }
                    else if (nextPitch >= MaxElevationPitchDeg)
                    {
                        targetOrbitPitchDeg = MaxElevationPitchDeg;
                        coastPitchVelocityDeg = 0f;
                    }
                    else
                    {
                        targetOrbitPitchDeg = nextPitch;
                    }
                }

                if (Mathf.Abs(coastZoomVelocity) > StageCoastStopEpsilon)
                {
                    float nextZoom = targetZoomScale + (coastZoomVelocity * dt);
                    if (nextZoom <= MinZoomScale)
                    {
                        targetZoomScale = MinZoomScale;
                        coastZoomVelocity = 0f;
                    }
                    else if (nextZoom >= MaxZoomScale)
                    {
                        targetZoomScale = MaxZoomScale;
                        coastZoomVelocity = 0f;
                    }
                    else
                    {
                        targetZoomScale = nextZoom;
                    }
                }

                if (coastPanVelocity.sqrMagnitude > StageCoastStopEpsilon * StageCoastStopEpsilon)
                {
                    float nextPanX = targetPanOffset.x + (coastPanVelocity.x * dt);
                    if (nextPanX <= -MaxPanOffsetX)
                    {
                        targetPanOffset.x = -MaxPanOffsetX;
                        coastPanVelocity.x = 0f;
                    }
                    else if (nextPanX >= MaxPanOffsetX)
                    {
                        targetPanOffset.x = MaxPanOffsetX;
                        coastPanVelocity.x = 0f;
                    }
                    else
                    {
                        targetPanOffset.x = nextPanX;
                    }

                    float nextPanY = targetPanOffset.y + (coastPanVelocity.y * dt);
                    if (nextPanY <= -MaxPanOffsetY)
                    {
                        targetPanOffset.y = -MaxPanOffsetY;
                        coastPanVelocity.y = 0f;
                    }
                    else if (nextPanY >= MaxPanOffsetY)
                    {
                        targetPanOffset.y = MaxPanOffsetY;
                        coastPanVelocity.y = 0f;
                    }
                    else
                    {
                        targetPanOffset.y = nextPanY;
                    }

                    float nextPanZ = targetPanOffset.z + (coastPanVelocity.z * dt);
                    if (nextPanZ <= -MaxPanDepthPullCloserZ)
                    {
                        targetPanOffset.z = -MaxPanDepthPullCloserZ;
                        coastPanVelocity.z = 0f;
                    }
                    else if (nextPanZ >= MaxPanDepthPushFartherZ)
                    {
                        targetPanOffset.z = MaxPanDepthPushFartherZ;
                        coastPanVelocity.z = 0f;
                    }
                    else
                    {
                        targetPanOffset.z = nextPanZ;
                    }
                }

                float coastDecay = Mathf.Exp(-StageCoastDamping * dt);
                coastYawVelocityDeg *= coastDecay;
                coastPitchVelocityDeg *= coastDecay;
                coastZoomVelocity *= coastDecay;
                coastPanVelocity *= coastDecay;

                if (Mathf.Abs(coastYawVelocityDeg) <= StageCoastStopEpsilon) coastYawVelocityDeg = 0f;
                if (Mathf.Abs(coastPitchVelocityDeg) <= StageCoastStopEpsilon) coastPitchVelocityDeg = 0f;
                if (Mathf.Abs(coastZoomVelocity) <= StageCoastStopEpsilon) coastZoomVelocity = 0f;
                if (coastPanVelocity.sqrMagnitude <= StageCoastStopEpsilon * StageCoastStopEpsilon) coastPanVelocity = Vector3.zero;
            }
        }

        // 1. Step pegas analitik Air-Orbit, Air-Zoom, & Free Air-Pan 3D
        currentOrbitYawDeg = StepCriticallyDampedSpring(currentOrbitYawDeg, targetOrbitYawDeg, ref velocityOrbitYaw, OrbitSpringOmega, dt);
        currentOrbitPitchDeg = StepCriticallyDampedSpring(currentOrbitPitchDeg, targetOrbitPitchDeg, ref velocityOrbitPitch, OrbitSpringOmega, dt);
        currentZoomScale = StepCriticallyDampedSpring(currentZoomScale, targetZoomScale, ref velocityZoom, ZoomSpringOmega, dt);
        currentPanOffset.x = StepCriticallyDampedSpring(currentPanOffset.x, targetPanOffset.x, ref velocityPanOffset.x, PanSpringOmega, dt);
        currentPanOffset.y = StepCriticallyDampedSpring(currentPanOffset.y, targetPanOffset.y, ref velocityPanOffset.y, PanSpringOmega, dt);
        currentPanOffset.z = StepCriticallyDampedSpring(currentPanOffset.z, targetPanOffset.z, ref velocityPanOffset.z, PanSpringOmega, dt);

        // 2. Dynamic 3D Hand Parallax (±7° yaw, ±4° pitch mengikuti PalmCursor saat tidak sedang manipulasi 2 tangan)
        float desiredParallaxYaw = 0f;
        float desiredParallaxPitch = 0f;
        if (isHandDetected && !isTwoHandManipulationActive && !isUiOverlayOpen)
        {
            float normX = Mathf.Clamp(palmCameraLocalXY.x / 1.15f, -1f, 1f);
            float normY = Mathf.Clamp((palmCameraLocalXY.y + 0.16f) / 0.85f, -1f, 1f);
            desiredParallaxYaw = -normX * MaxParallaxYawDeg;
            desiredParallaxPitch = -normY * MaxParallaxPitchDeg;
        }

        currentParallaxYawDeg = StepCriticallyDampedSpring(currentParallaxYawDeg, desiredParallaxYaw, ref velocityParallaxYaw, ParallaxSpringOmega, dt);
        currentParallaxPitchDeg = StepCriticallyDampedSpring(currentParallaxPitchDeg, desiredParallaxPitch, ref velocityParallaxPitch, ParallaxSpringOmega, dt);

        // 3. Terapkan Transform Pivot HologramStageRoot dengan dukungan Zoom Dekat Ekstrem & Free Air-Pan 3D (aman nearClipPlane):
        Transform camT = mainCam.transform;
        float effectivePitch = Mathf.Clamp(currentOrbitPitchDeg + currentParallaxPitchDeg, MinElevationPitchDeg, MaxElevationPitchDeg);
        float effectiveYaw = currentOrbitYawDeg + currentParallaxYawDeg;

        // Saat zoom > 1.0x, majukan juga sedikit pivot Z secara proporsional agar deep inspection terasa benar-benar mendekati objek
        float zoomApproachZ = currentZoomScale > 1.0f
            ? -Mathf.Min(1.15f, (currentZoomScale - 1.0f) * 0.58f)
            : Mathf.Min(0.85f, (1.0f - currentZoomScale) * 0.90f);
        float minSafePivotZ = Mathf.Max(0.95f, mainCam.nearClipPlane + 0.65f);
        float effectivePivotZ = Mathf.Clamp(workbenchDepth + currentPanOffset.z + zoomApproachZ, minSafePivotZ, 7.20f);

        stageRootTransform.position = camT.TransformPoint(new Vector3(
            currentPanOffset.x,
            StagePivotCameraOffsetY + currentPanOffset.y,
            effectivePivotZ));
        stageRootTransform.rotation = camT.rotation
                                      * Quaternion.Euler(-effectivePitch, 0f, 0f)
                                      * Quaternion.Euler(0f, effectiveYaw, 0f);
        stageRootTransform.localScale = Vector3.one * currentZoomScale;
    }

    /// <summary>
    /// Memperbarui lantai proyeksi meja horizontal XZ, warna batas zona rakit, serta 8 Grounded Contact Rings elips
    /// dan tiang proyeksi vertikal di bawah setiap node aktif. Zero-GC.
    /// </summary>
    public void UpdateFloorAndContactRings(
        Camera mainCam,
        float aspectScale,
        float assemblyMinBaseX,
        float assemblyMaxBaseX,
        float assemblyMinZ,
        float assemblyMaxZ,
        float inputBaseX,
        float outputBaseX,
        float parkingRackZ,
        bool isGrabbingGate,
        Vector2 grabbedGateStageXZ,
        bool isGrabbedGateOutsideZone,
        ContactNodeDescriptor[] contactNodes,
        int contactNodeCount)
    {
        if (!isInitialized || mainCam == null || stageRootTransform == null)
        {
            return;
        }

        if (Mathf.Abs(aspectScale - lastBuiltGridAspectScale) > 0.002f)
        {
            RebuildStaticFloorGridGeometry(
                aspectScale,
                assemblyMinBaseX,
                assemblyMaxBaseX,
                assemblyMinZ,
                assemblyMaxZ,
                inputBaseX,
                outputBaseX,
                parkingRackZ);
        }

        // 1. Respons warna & ketebalan bingkai Zona Rakit (Inner Arena) dan Dek Meja Utuh (Outer Deck)
        float leftX = (assemblyMinBaseX - 0.08f) * aspectScale;
        float rightX = (assemblyMaxBaseX + 0.08f) * aspectScale;
        float frontZ = assemblyMinZ - 0.08f;
        float backZ = assemblyMaxZ + 0.08f;

        Color outerDeckCol = FloorOuterBorderColor;
        Color arenaBorderCol = FloorInnerArenaBorderColor;
        Color cornerCol = FloorOuterBorderColor;
        Color shadowCol = FloorOuterShadowColor;
        float cornerWidth = 0.0085f;

        if (isGrabbingGate)
        {
            if (isGrabbedGateOutsideZone)
            {
                float pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * 9.0f));
                outerDeckCol = new Color(1.00f, 0.34f, 0.26f, 0.62f + (0.18f * pulse));
                arenaBorderCol = new Color(1.00f, 0.26f, 0.22f, 0.74f + (0.22f * pulse));
                cornerCol = new Color(FloorDeleteWarningColor.r, FloorDeleteWarningColor.g, FloorDeleteWarningColor.b, 0.86f + (0.14f * pulse));
                shadowCol = FloorDeleteShadowColor;
                cornerWidth = 0.0115f;
            }
            else
            {
                float minEdgeDist = Mathf.Min(
                    Mathf.Min(grabbedGateStageXZ.x - leftX, rightX - grabbedGateStageXZ.x),
                    Mathf.Min(grabbedGateStageXZ.y - frontZ, backZ - grabbedGateStageXZ.y));
                float nearT = 1f - Mathf.Clamp01(minEdgeDist / 0.26f);
                outerDeckCol = new Color(0.20f, 0.92f, 1.00f, Mathf.Lerp(0.60f, 0.78f, nearT));
                arenaBorderCol = new Color(0.24f, 0.96f, 1.00f, Mathf.Lerp(0.50f, 0.88f, nearT));
                cornerCol = new Color(0.28f, 0.98f, 1.00f, Mathf.Lerp(0.64f, 0.98f, nearT));
                shadowCol = new Color(0.02f, 0.06f, 0.12f, Mathf.Lerp(0.64f, 0.78f, nearT));
                cornerWidth = Mathf.Lerp(0.0085f, 0.0108f, nearT);
            }
        }

        if (outerBorderGlowMat != null) outerBorderGlowMat.color = outerDeckCol;
        if (outerBorderShadowMat != null) outerBorderShadowMat.color = shadowCol;
        if (innerArenaBorderMat != null) innerArenaBorderMat.color = arenaBorderCol;
        if (cornerBracketMat != null) cornerBracketMat.color = cornerCol;
        if (cornerShadowMat != null) cornerShadowMat.color = shadowCol;

        if (outerBorderGlowRenderer != null)
        {
            outerBorderGlowRenderer.startColor = outerDeckCol;
            outerBorderGlowRenderer.endColor = outerDeckCol;
        }
        if (outerBorderShadowRenderer != null)
        {
            outerBorderShadowRenderer.startColor = shadowCol;
            outerBorderShadowRenderer.endColor = shadowCol;
        }
        if (innerArenaBorderRenderer != null)
        {
            innerArenaBorderRenderer.startColor = arenaBorderCol;
            innerArenaBorderRenderer.endColor = arenaBorderCol;
        }

        if (cornerBracketRenderers != null)
        {
            for (int c = 0; c < 4; c++)
            {
                if (cornerBracketRenderers[c] != null)
                {
                    cornerBracketRenderers[c].widthMultiplier = cornerWidth;
                    cornerBracketRenderers[c].startColor = cornerCol;
                    cornerBracketRenderers[c].endColor = cornerCol;
                }
                if (cornerShadowRenderers[c] != null)
                {
                    cornerShadowRenderers[c].widthMultiplier = cornerWidth + 0.0050f;
                    cornerShadowRenderers[c].startColor = shadowCol;
                    cornerShadowRenderers[c].endColor = shadowCol;
                }
            }
        }

        // 2. Perbarui Grounded Contact Rings di bidang meja XZ (y = ContactRingLocalY) + Tiang Proyeksi Vertikal (+Y) untuk SELURUH 8 node
        int clampedCount = contactNodes != null ? Mathf.Min(contactNodeCount, MaxContactRingSlots) : 0;
        for (int i = 0; i < MaxContactRingSlots; i++)
        {
            LineRenderer outerRing = contactOuterRings[i];
            LineRenderer innerRing = contactInnerRings[i];
            LineRenderer stemLine = contactElevationStems[i];

            if (i >= clampedCount || !contactNodes[i].active)
            {
                if (outerRing != null && outerRing.enabled) outerRing.enabled = false;
                if (innerRing != null && innerRing.enabled) innerRing.enabled = false;
                if (stemLine != null && stemLine.enabled) stemLine.enabled = false;
                continue;
            }

            ContactNodeDescriptor desc = contactNodes[i];
            float hoverHeightY = Mathf.Max(0.02f, desc.stageLocalPos.y);
            float liftNormalized = desc.isParked
                ? 0f
                : Mathf.Clamp01((hoverHeightY - BaseHoverHeight) / Mathf.Max(0.05f, GrabExtraLiftHeight));

            // Saat gerbang dikepal & terangkat, cincin bayangan di meja melebar proporsional & sedikit memudar
            float effectiveRadius = desc.baseRadius * (1.0f + (ContactRingLiftExpandFactor * liftNormalized));
            float innerRadius = effectiveRadius * (desc.isParked ? 0.58f : Mathf.Lerp(0.54f, 0.38f, liftNormalized));

            Color baseColor;
            if (desc.isInputA)
            {
                baseColor = ContactInputAColor;
            }
            else if (desc.isInputB)
            {
                baseColor = ContactInputBColor;
            }
            else if (desc.isOutput)
            {
                baseColor = ContactOutputColor;
            }
            else if (desc.isInDeleteZone)
            {
                baseColor = ContactGateDeleteColor;
            }
            else if (desc.isGrabbed)
            {
                baseColor = Color.Lerp(ContactGateRestColor, ContactGateLiftedColor, Mathf.Clamp01(liftNormalized * 1.25f));
            }
            else if (desc.isParked)
            {
                baseColor = ContactGateParkedColor;
            }
            else
            {
                baseColor = ContactGateRestColor;
            }

            float fadeFactor = desc.isParked ? 0.92f : Mathf.Lerp(1.0f, 0.54f, liftNormalized);
            Color outerCol = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * fadeFactor);
            Color innerCol = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * 0.64f * fadeFactor);

            DrawLocalTableXZCircle(
                outerRing,
                contactOuterMats[i],
                desc.stageLocalPos.x,
                ContactRingLocalY,
                desc.stageLocalPos.z,
                effectiveRadius,
                outerCol,
                desc.isGrabbed ? 0.0080f : (desc.isParked ? 0.0052f : 0.0066f));

            DrawLocalTableXZCircle(
                innerRing,
                contactInnerMats[i],
                desc.stageLocalPos.x,
                ContactRingLocalY + 0.001f,
                desc.stageLocalPos.z,
                innerRadius,
                innerCol,
                desc.isParked ? 0.0034f : 0.0040f);

            // Tiang proyeksi vertikal tegak lurus dari setiap node aktif/parkir turun ke pusat cincin bayangan di permukaan meja
            if (stemLine != null)
            {
                float stemAlpha = desc.isGrabbed
                    ? Mathf.Lerp(0.46f, 0.78f, liftNormalized)
                    : (desc.isParked ? 0.28f : 0.36f);
                Color stemCol = new Color(baseColor.r, baseColor.g, baseColor.b, stemAlpha);
                if (contactStemMats[i] != null)
                {
                    contactStemMats[i].color = stemCol;
                }
                stemLocalBuffer[0] = new Vector3(desc.stageLocalPos.x, ContactRingLocalY, desc.stageLocalPos.z);
                stemLocalBuffer[1] = new Vector3(desc.stageLocalPos.x, Mathf.Max(ContactRingLocalY + 0.02f, hoverHeightY - 0.015f), desc.stageLocalPos.z);
                stemLine.enabled = true;
                stemLine.widthMultiplier = desc.isGrabbed ? 0.0052f : (desc.isParked ? 0.0030f : 0.0036f);
                stemLine.startColor = stemCol;
                stemLine.endColor = stemCol;
                stemLine.SetPositions(stemLocalBuffer);
            }
        }
    }

    // =========================================================================
    // RAY-TO-HOVER-PLANE & 3D PROJECTION HELPERS (ZERO-GC)
    // =========================================================================

    /// <summary>
    /// Memproyeksikan titik dunia 3D (node, sakelar, port orb, maupun kursor tangan) sepanjang sinar perspektif
    /// <c>Camera.main</c> ke bidang referensi kamera pada <c>workbenchDepth</c>.
    /// Menjamin hit-test 100% akurat tanpa meleset saat meja miring, di-orbit 360°, maupun di-zoom. Zero-GC.
    /// </summary>
    public Vector2 ProjectWorldPointToCameraRayPlane(Camera mainCam, Vector3 worldPos, float workbenchDepth)
    {
        if (mainCam == null)
        {
            return new Vector2(worldPos.x, worldPos.y);
        }

        Vector3 camLocal = mainCam.transform.InverseTransformPoint(worldPos);
        float safeZ = Mathf.Max(0.35f, camLocal.z);
        float scale = workbenchDepth / safeZ;
        return new Vector2(camLocal.x * scale, camLocal.y * scale);
    }

    /// <summary>
    /// Menembakkan ray dari <c>Camera.main</c> melewati posisi dunia tangan (<c>handWorldPos</c>) untuk memotong
    /// bidang melayang horizontal (<c>y = targetHoverHeightY</c>) yang sejajar permukaan meja <c>XZ</c> <c>HologramStageRoot</c>.
    /// Mengembalikan koordinat meja <c>(stageX, stageZ)</c> dengan jangkauan kedalaman ergonomis. Zero-GC.
    /// </summary>
    public bool TryIntersectHandWithHoverPlane(
        Camera mainCam,
        Vector3 handWorldPos,
        float targetHoverHeightY,
        out Vector2 stageLocalXZ,
        out Vector3 worldHitPos)
    {
        if (mainCam == null || stageRootTransform == null)
        {
            stageLocalXZ = Vector2.zero;
            worldHitPos = handWorldPos;
            return false;
        }

        Transform camT = mainCam.transform;
        Vector3 rayOrigin = camT.position;
        Vector3 rayDir = handWorldPos - rayOrigin;
        if (rayDir.sqrMagnitude < 0.00001f)
        {
            rayDir = camT.forward;
        }
        else
        {
            rayDir.Normalize();
        }

        Vector3 planeOrigin = stageRootTransform.TransformPoint(new Vector3(0f, targetHoverHeightY, 0f));
        Vector3 tableUpNormal = stageRootTransform.up;
        float denom = Vector3.Dot(rayDir, tableUpNormal);

        if (Mathf.Abs(denom) > 0.16f)
        {
            float t = Vector3.Dot(planeOrigin - rayOrigin, tableUpNormal) / denom;
            if (t > 0.35f && t < 14.0f)
            {
                worldHitPos = rayOrigin + (rayDir * t);
                Vector3 localHit = stageRootTransform.InverseTransformPoint(worldHitPos);
                stageLocalXZ = new Vector2(localHit.x, localHit.z);
                return true;
            }
        }

        // Fallback bila sinar hampir sejajar horizon meja
        Vector3 camPlaneNormal = -camT.forward;
        float camDenom = Vector3.Dot(rayDir, camPlaneNormal);
        float fallbackT = Mathf.Abs(camDenom) > 0.001f
            ? Mathf.Clamp(Vector3.Dot(planeOrigin - rayOrigin, camPlaneNormal) / camDenom, 0.5f, 10.0f)
            : 3.60f;
        worldHitPos = rayOrigin + (rayDir * fallbackT);
        Vector3 fallbackLocal = stageRootTransform.InverseTransformPoint(worldHitPos);
        stageLocalXZ = new Vector2(fallbackLocal.x, fallbackLocal.z);
        return true;
    }

    // =========================================================================
    // PROCEDURAL HORIZONTAL TABLE FLOOR (XZ PLANE, Y = 0, ZERO-GC)
    // =========================================================================

    private void RebuildStaticFloorGridGeometry(
        float aspectScale,
        float assemblyMinBaseX,
        float assemblyMaxBaseX,
        float assemblyMinZ,
        float assemblyMaxZ,
        float inputBaseX,
        float outputBaseX,
        float parkingRackZ)
    {
        lastBuiltGridAspectScale = aspectScale;

        // Batas Zona Rakit Tengah (Inner Assembly Arena)
        float arenaLeftX = (assemblyMinBaseX - 0.08f) * aspectScale;
        float arenaRightX = (assemblyMaxBaseX + 0.08f) * aspectScale;
        float arenaFrontZ = assemblyMinZ - 0.08f;
        float arenaBackZ = assemblyMaxZ + 0.08f;

        // Batas Dek Meja Hologram Utuh (mencakup Sayap Input Kiri, Zona Rakit Tengah, Sayap Output Kanan + Truth Pod, & Teras Rak Parkir Belakang)
        float deckLeftX = Mathf.Min(inputBaseX - 0.28f, assemblyMinBaseX - 0.60f) * aspectScale;
        float deckRightX = Mathf.Max(outputBaseX + 0.34f, assemblyMaxBaseX + 0.64f) * aspectScale;
        float deckFrontZ = assemblyMinZ - 0.18f;
        float deckBackZ = parkingRackZ + 0.28f;
        float deckChamfer = 0.24f;

        // 1. Mesh Kaca Lantai Meja Horizontal Utuh (Oktagon Ber-Chamfer rata di bidang XZ meja, bebas sudut lancip keluar)
        RebuildOctagonFloorDeckMesh(deckLeftX, deckRightX, deckFrontZ, deckBackZ, deckChamfer, StageFloorLocalY - 0.002f);

        // 2. Bingkai Luar Dek Meja Utuh & Bingkai Dalam Zona Rakit di Bidang XZ
        FillChamferedTableXZRectLoop(borderLoopBuffer, deckLeftX, deckRightX, deckFrontZ, deckBackZ, deckChamfer, StageFloorLocalY);
        if (outerBorderGlowRenderer != null)
        {
            outerBorderGlowRenderer.SetPositions(borderLoopBuffer);
        }
        FillChamferedTableXZRectLoop(borderLoopBuffer, deckLeftX, deckRightX, deckFrontZ, deckBackZ, deckChamfer, StageFloorLocalY - 0.001f);
        if (outerBorderShadowRenderer != null)
        {
            outerBorderShadowRenderer.SetPositions(borderLoopBuffer);
        }

        float arenaChamfer = 0.10f;
        FillChamferedTableXZRectLoop(borderLoopBuffer, arenaLeftX, arenaRightX, arenaFrontZ, arenaBackZ, arenaChamfer, StageFloorLocalY + 0.001f);
        if (innerArenaBorderRenderer != null)
        {
            innerArenaBorderRenderer.SetPositions(borderLoopBuffer);
        }

        // 3. 4 Siku Sudut Zona Rakit Dipertegas di Permukaan Meja XZ
        float armX = 0.155f * aspectScale;
        float armZ = 0.155f;
        float cornerY = StageFloorLocalY + 0.0025f;
        float shadowY = StageFloorLocalY + 0.0015f;

        SetLocalTableCornerBracket(cornerShadowRenderers[0], arenaLeftX, arenaFrontZ + armZ, arenaLeftX, arenaFrontZ, arenaLeftX + armX, arenaFrontZ, shadowY);
        SetLocalTableCornerBracket(cornerShadowRenderers[1], arenaRightX - armX, arenaFrontZ, arenaRightX, arenaFrontZ, arenaRightX, arenaFrontZ + armZ, shadowY);
        SetLocalTableCornerBracket(cornerShadowRenderers[2], arenaRightX, arenaBackZ - armZ, arenaRightX, arenaBackZ, arenaRightX - armX, arenaBackZ, shadowY);
        SetLocalTableCornerBracket(cornerShadowRenderers[3], arenaLeftX + armX, arenaBackZ, arenaLeftX, arenaBackZ, arenaLeftX, arenaBackZ - armZ, shadowY);

        SetLocalTableCornerBracket(cornerBracketRenderers[0], arenaLeftX, arenaFrontZ + armZ, arenaLeftX, arenaFrontZ, arenaLeftX + armX, arenaFrontZ, cornerY);
        SetLocalTableCornerBracket(cornerBracketRenderers[1], arenaRightX - armX, arenaFrontZ, arenaRightX, arenaFrontZ, arenaRightX, arenaFrontZ + armZ, cornerY);
        SetLocalTableCornerBracket(cornerBracketRenderers[2], arenaRightX, arenaBackZ - armZ, arenaRightX, arenaBackZ, arenaRightX - armX, arenaBackZ, cornerY);
        SetLocalTableCornerBracket(cornerBracketRenderers[3], arenaLeftX + armX, arenaBackZ, arenaLeftX, arenaBackZ, arenaLeftX, arenaBackZ - armZ, cornerY);

        // 4. Garis Grid Proyeksi Internal Menyatu di Seluruh Dek Meja XZ (terpotong rapi mengikuti batas chamfer oktagon)
        float gridY = StageFloorLocalY + 0.001f;
        for (int v = 0; v < GridInternalVerticalLines; v++)
        {
            float frac = (v + 1f) / (GridInternalVerticalLines + 1f);
            float gx = Mathf.Lerp(deckLeftX + 0.08f, deckRightX - 0.08f, frac);
            float chamferInsetZ = ComputeOctagonZInsetAtX(gx, deckLeftX, deckRightX, deckChamfer);
            LineRenderer lr = verticalGridRenderers[v];
            if (lr != null)
            {
                lr.SetPosition(0, new Vector3(gx, gridY, deckFrontZ + chamferInsetZ + 0.015f));
                lr.SetPosition(1, new Vector3(gx, gridY, deckBackZ - chamferInsetZ - 0.015f));
            }
        }

        for (int h = 0; h < GridInternalHorizontalLines; h++)
        {
            float frac = (h + 1f) / (GridInternalHorizontalLines + 1f);
            float gz = Mathf.Lerp(deckFrontZ + 0.08f, deckBackZ - 0.08f, frac);
            float chamferInsetX = ComputeOctagonXInsetAtZ(gz, deckFrontZ, deckBackZ, deckChamfer);
            LineRenderer lr = horizontalGridRenderers[h];
            if (lr != null)
            {
                lr.SetPosition(0, new Vector3(deckLeftX + chamferInsetX + 0.015f, gridY, gz));
                lr.SetPosition(1, new Vector3(deckRightX - chamferInsetX - 0.015f, gridY, gz));
            }
        }

        // 5. Dermaga Sayap Terminal Input (Kiri), Sayap Output (Kanan), & Teras Rak Parkir (Belakang Meja) rata di permukaan dek XZ
        float inBayOuterX = (inputBaseX - 0.20f) * aspectScale;
        float inBayInnerX = arenaLeftX - (0.04f * aspectScale);
        if (wingGuideRenderers[0] != null)
        {
            wingGuideRenderers[0].SetPosition(0, new Vector3(inBayInnerX, gridY, arenaBackZ - 0.04f));
            wingGuideRenderers[0].SetPosition(1, new Vector3(inBayOuterX, gridY, arenaBackZ - 0.10f));
            wingGuideRenderers[0].SetPosition(2, new Vector3(inBayOuterX, gridY, arenaFrontZ + 0.04f));
            wingGuideRenderers[0].SetPosition(3, new Vector3(inBayInnerX, gridY, arenaFrontZ + 0.02f));
        }

        float outBayInnerX = arenaRightX + (0.04f * aspectScale);
        float outBayOuterX = (outputBaseX + 0.24f) * aspectScale;
        if (wingGuideRenderers[1] != null)
        {
            wingGuideRenderers[1].SetPosition(0, new Vector3(outBayInnerX, gridY, arenaBackZ - 0.06f));
            wingGuideRenderers[1].SetPosition(1, new Vector3(outBayOuterX, gridY, arenaBackZ - 0.12f));
            wingGuideRenderers[1].SetPosition(2, new Vector3(outBayOuterX, gridY, arenaFrontZ + 0.06f));
            wingGuideRenderers[1].SetPosition(3, new Vector3(outBayInnerX, gridY, arenaFrontZ + 0.04f));
        }

        float rackFrontZ = arenaBackZ + 0.05f;
        float rackBackZ = parkingRackZ + 0.20f;
        float rackLeftX = -0.96f * aspectScale;
        float rackRightX = 0.96f * aspectScale;
        if (wingGuideRenderers[2] != null)
        {
            wingGuideRenderers[2].SetPosition(0, new Vector3(rackLeftX, gridY, rackFrontZ));
            wingGuideRenderers[2].SetPosition(1, new Vector3(rackRightX, gridY, rackFrontZ));
            wingGuideRenderers[2].SetPosition(2, new Vector3(rackRightX - (0.08f * aspectScale), gridY, rackBackZ));
            wingGuideRenderers[2].SetPosition(3, new Vector3(rackLeftX + (0.08f * aspectScale), gridY, rackBackZ));
        }
    }

    private void RebuildOctagonFloorDeckMesh(
        float leftX,
        float rightX,
        float frontZ,
        float backZ,
        float chamfer,
        float y)
    {
        if (floorDeckOctagonMesh == null)
        {
            return;
        }

        float centerX = (leftX + rightX) * 0.5f;
        float centerZ = (frontZ + backZ) * 0.5f;
        floorDeckVerticesBuffer[0] = new Vector3(centerX, y, centerZ);
        floorDeckVerticesBuffer[1] = new Vector3(leftX + chamfer, y, frontZ);
        floorDeckVerticesBuffer[2] = new Vector3(rightX - chamfer, y, frontZ);
        floorDeckVerticesBuffer[3] = new Vector3(rightX, y, frontZ + chamfer);
        floorDeckVerticesBuffer[4] = new Vector3(rightX, y, backZ - chamfer);
        floorDeckVerticesBuffer[5] = new Vector3(rightX - chamfer, y, backZ);
        floorDeckVerticesBuffer[6] = new Vector3(leftX + chamfer, y, backZ);
        floorDeckVerticesBuffer[7] = new Vector3(leftX, y, backZ - chamfer);
        floorDeckVerticesBuffer[8] = new Vector3(leftX, y, frontZ + chamfer);

        floorDeckOctagonMesh.vertices = floorDeckVerticesBuffer;
        if (floorDeckOctagonMesh.triangles == null || floorDeckOctagonMesh.triangles.Length != 24)
        {
            floorDeckOctagonMesh.triangles = new[]
            {
                0, 8, 1,
                0, 1, 2,
                0, 2, 3,
                0, 3, 4,
                0, 4, 5,
                0, 5, 6,
                0, 6, 7,
                0, 7, 8
            };
        }
        floorDeckOctagonMesh.RecalculateBounds();
    }

    private static float ComputeOctagonZInsetAtX(float x, float leftX, float rightX, float chamfer)
    {
        if (x < leftX + chamfer)
        {
            return (leftX + chamfer) - x;
        }
        if (x > rightX - chamfer)
        {
            return x - (rightX - chamfer);
        }
        return 0f;
    }

    private static float ComputeOctagonXInsetAtZ(float z, float frontZ, float backZ, float chamfer)
    {
        if (z < frontZ + chamfer)
        {
            return (frontZ + chamfer) - z;
        }
        if (z > backZ - chamfer)
        {
            return z - (backZ - chamfer);
        }
        return 0f;
    }

    private static void FillChamferedTableXZRectLoop(
        Vector3[] buffer,
        float leftX,
        float rightX,
        float frontZ,
        float backZ,
        float chamfer,
        float y)
    {
        buffer[0] = new Vector3(leftX + chamfer, y, frontZ);
        buffer[1] = new Vector3(rightX - chamfer, y, frontZ);
        buffer[2] = new Vector3(rightX, y, frontZ + chamfer);
        buffer[3] = new Vector3(rightX, y, backZ - chamfer);
        buffer[4] = new Vector3(rightX - chamfer, y, backZ);
        buffer[5] = new Vector3(leftX + chamfer, y, backZ);
        buffer[6] = new Vector3(leftX, y, backZ - chamfer);
        buffer[7] = new Vector3(leftX, y, frontZ + chamfer);
    }

    private void SetLocalTableCornerBracket(
        LineRenderer lr,
        float x0, float z0,
        float x1, float z1,
        float x2, float z2,
        float y)
    {
        if (lr == null)
        {
            return;
        }

        cornerPointsBuffer[0] = new Vector3(x0, y, z0);
        cornerPointsBuffer[1] = new Vector3(x1, y, z1);
        cornerPointsBuffer[2] = new Vector3(x2, y, z2);
        lr.SetPositions(cornerPointsBuffer);
    }

    private void DrawLocalTableXZCircle(
        LineRenderer lr,
        Material mat,
        float centerX,
        float centerY,
        float centerZ,
        float radius,
        Color color,
        float width)
    {
        if (lr == null)
        {
            return;
        }

        for (int i = 0; i < ContactRingSegments; i++)
        {
            float angle = (i / (float)ContactRingSegments) * Mathf.PI * 2f;
            ringLocalBuffer[i] = new Vector3(
                centerX + (Mathf.Cos(angle) * radius),
                centerY,
                centerZ + (Mathf.Sin(angle) * radius));
        }

        if (mat != null)
        {
            mat.color = color;
        }
        lr.enabled = true;
        lr.widthMultiplier = width;
        lr.startColor = color;
        lr.endColor = color;
        lr.SetPositions(ringLocalBuffer);
    }

    private static LineRenderer CreateLocalLine(string name, Transform parent, int positionCount, bool loop, float width, Material mat)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        LineRenderer lr = obj.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = loop;
        lr.positionCount = positionCount;
        lr.widthMultiplier = width;
        lr.numCornerVertices = 2;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        if (mat != null)
        {
            lr.sharedMaterial = mat;
        }
        lr.enabled = true;
        return lr;
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

    private static float ComputeAspectScale(Camera mainCam)
    {
        float aspect = (mainCam != null && mainCam.aspect > 0.1f) ? mainCam.aspect : (16f / 9f);
        return Mathf.Clamp(aspect / (16f / 9f), 0.78f, 1.12f);
    }

    private void OnDestroy()
    {
        if (floorDeckOctagonMesh != null)
        {
            Destroy(floorDeckOctagonMesh);
            floorDeckOctagonMesh = null;
        }
        if (stageRootObj != null)
        {
            Destroy(stageRootObj);
            stageRootObj = null;
        }
        stageRootTransform = null;
        floorVisualGroupObj = null;

        for (int i = 0; i < ownedMaterials.Count; i++)
        {
            if (ownedMaterials[i] != null)
            {
                Destroy(ownedMaterials[i]);
            }
        }
        ownedMaterials.Clear();
    }
}
