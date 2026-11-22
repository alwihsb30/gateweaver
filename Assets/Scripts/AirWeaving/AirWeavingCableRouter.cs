using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pengendali kabel manual Port-ke-Port berbasis Pinch (Cubit #4-#8) &amp; Renderer Kabel Tabung 3D PBR Bervolume
/// + Soket Port 3D + Pulsing Signal Packets 3D (GateWeaver AR v8.0 Tahap 5).
/// - Memproyeksikan soket port 3D (Torus Collar + Energy Core Orb + Conduit Stub) keluar dari siluet node
///   mengikuti orientasi ruang 3D meja <c>HologramStageRoot</c> (tanpa billboard 2D).
/// - Terpisah total dari Fist (objek): seluruh hover port, tarik fan-out, cabut/pindah, &amp; snap magnetik digerakkan
///   oleh titik cubit presisi 0.5*(#4+#8) (PinchPointLocalXY).
/// - Aturan Fan-Out &amp; Unplug:
///   * Pinch pada Port_Out SELALU menarik cabang kabel baru (fan-out) tanpa memutus kabel lama.
///   * Pinch pada Port_In yang terisi MENCABUT ujung kabel lama (pindah ke Port_In lain, atau lepas di ruang kosong untuk hapus).
///   * Menempelkan kabel ke Port_In yang sudah terisi otomatis mengganti (replace) kabel lama pada slot tersebut.
/// - Merender 100% seluruh kabel Mode Udara (pool 20 kabel terpasang + 1 kabel live drag) sebagai mesh tabung 3D PBR
///   berbasis kurva Bezier 3D &amp; Parallel Transport Frame (Bishop Frame) agar bebas puntiran pada orbit 360°,
///   100% Zero-GC di runtime loop, 1 shared material Built-in Standard + MaterialPropertyBlock (OpenGLES3-safe).
/// </summary>
public sealed class AirWeavingCableRouter : MonoBehaviour
{
    public const float PortOutHitRadius = 0.225f;
    public const float PortInHitRadius = 0.185f;
    private const float MinPortScreenSpaceHitboxScale = 0.78f;
    private const float SnapMagnetRadius = 0.285f;
    private const float GateAutoSnapRadius = 0.340f;
    private const float SnapHysteresisUnlockRadius = 0.380f;
    private const float SnapTargetSwitchRatio = 0.720f;
    private const float SnapReleaseIntentFreezeRatio = 0.64f;
    private const float SnapReleaseLatchSeconds = 0.300f;
    private const float SnapReleaseMaxEscapeDist = 0.460f;
    private const float HoverMagnetRadius = 0.300f;
    private const float DragTargetMagnetRadius = 0.360f;
    private const float MagnetMaxPull = 0.500f;
    private const float MagnetMaxHandSpeed = 1.100f;
    private const float MagnetOffsetBlendSpeed = 18.0f;
    private const float InputPortOutOffsetX = 0.335f;
    private const float GatePortOutOffsetX = 0.325f;
    private const float PortOutOffsetY = 0.025f;
    private const float PortInOffsetX = -0.315f;
    private const float PortInSingleOffsetY = 0.025f;
    private const float PortInAOffsetY = 0.130f;
    private const float PortInBOffsetY = -0.095f;

    private const int MaxPortVisuals = 20;
    private const int MaxCableBeams = 20;

    // Geometri Mesh Tabung 3D PBR (20 segmen panjang x 8 sisi melingkar + 2 tutup ujung -> 191 vertex, 336 segitiga)
    private const int TubeLengthSegments = 20;
    private const int TubeRingCount = TubeLengthSegments + 1;
    private const int TubeRadialSegments = 8;
    private const int TubeRingVertexCount = TubeRadialSegments + 1;
    private const int TubeSideVertexCount = TubeRingCount * TubeRingVertexCount;
    private const int TubeVertexCount = TubeSideVertexCount + 2;
    private const int TubeIndexCount = ((TubeLengthSegments * TubeRadialSegments * 2) + (TubeRadialSegments * 2)) * 3;

    // Parameter Radius Tabung 3D PBR & Soket Port 3D (skala ruang meja HologramStageRoot)
    private const float TubeRadiusHigh = 0.0115f;
    private const float TubeRadiusLow = 0.0082f;
    private const float TubeRadiusDragFree = 0.0098f;
    private const float TubeRadiusDragSnapped = 0.0124f;
    private const float TubeRadiusDragInvalid = 0.0102f;
    private const float PortStubBaseRadius = 0.0046f;
    private const float SocketBaseRadius = 0.039f;
    private const float SocketScaleBlendSpeed = 16.0f;
    private const float PortDragAnticipationRadius = 0.56f;
    private const float PortDragAnticipationScaleBoost = 0.15f;
    private const float DragColorBlendSpeed = 18.0f;
    private const float LiveDragTipFreeSpringOmega = 24.0f;
    private const float LiveDragTipSnapSpringOmega = 34.0f;

    // Pool Zero-GC 60 Pulsing Signal Packets 3D (kapsul energi ramping sejajar tangen kurva tabung 3D)
    private const int PacketsPerCable = 3;
    private const int TotalPacketPoolSize = MaxCableBeams * PacketsPerCable;
    private const float PacketHighSpeed = 1.12f;
    private const float PacketLowSpeed = 0.34f;
    private const float PacketHighRadiusScale = 1.42f;
    private const float PacketHighHalfLength = 0.034f;
    private const float PacketLowRadiusScale = 1.36f;
    private const float PacketLowHalfLength = 0.022f;
    private const float PacketLongCableThreshold = 1.15f;
    private const float PacketEdgeFadeMargin = 0.12f;

    private static readonly int ColorPropId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorPropId = Shader.PropertyToID("_EmissionColor");
    private static readonly int MetallicPropId = Shader.PropertyToID("_Metallic");
    private static readonly int GlossinessPropId = Shader.PropertyToID("_Glossiness");
    private static readonly int RimColorPropId = Shader.PropertyToID("_RimColor");
    private static readonly int RimPowerPropId = Shader.PropertyToID("_RimPower");
    private static readonly int RimIntensityPropId = Shader.PropertyToID("_RimIntensity");

    // Palet Fresnel Rim Glow dalam Linear Space eksplisit untuk MaterialPropertyBlock.SetColor (tajam, bebas pucat)
    private static readonly Color CableHighRimLinear = AirWeavingLightingAndShaderRig.SrgbToLinearColor(0.24f, 1.00f, 0.94f);
    private static readonly Color CableLowRimLinear = AirWeavingLightingAndShaderRig.SrgbToLinearColor(0.95f, 0.34f, 0.14f);
    private static readonly Color DragFreeRimLinear = AirWeavingLightingAndShaderRig.SrgbToLinearColor(0.26f, 0.98f, 1.00f);
    private static readonly Color DragSnappedRimLinear = AirWeavingLightingAndShaderRig.SrgbToLinearColor(1.00f, 0.90f, 0.24f);
    private static readonly Color DragInvalidRimLinear = AirWeavingLightingAndShaderRig.SrgbToLinearColor(1.00f, 0.22f, 0.16f);
    private static readonly Color PortIdleRimLinear = AirWeavingLightingAndShaderRig.SrgbToLinearColor(0.32f, 0.78f, 1.00f);
    private static readonly Color PortHoverRimLinear = AirWeavingLightingAndShaderRig.SrgbToLinearColor(0.36f, 1.00f, 0.98f);
    private static readonly Color PortValidRimLinear = AirWeavingLightingAndShaderRig.SrgbToLinearColor(0.26f, 1.00f, 0.56f);
    private static readonly Color PortReplaceRimLinear = AirWeavingLightingAndShaderRig.SrgbToLinearColor(1.00f, 0.80f, 0.22f);
    private static readonly Color PortSnapRimLinear = AirWeavingLightingAndShaderRig.SrgbToLinearColor(1.00f, 0.94f, 0.26f);
    private static readonly Color PacketHighRimLinear = AirWeavingLightingAndShaderRig.SrgbToLinearColor(0.60f, 1.00f, 1.00f);
    private static readonly Color PacketLowRimLinear = AirWeavingLightingAndShaderRig.SrgbToLinearColor(1.00f, 0.44f, 0.16f);

    // Palet PBR Built-in Forward (Linear Color Space) — kontras kuat Sinyal 1 vs Sinyal 0 vs Live Drag
    private static readonly Color CableHighAlbedo = new Color(0.10f, 0.88f, 1.00f, 1f);
    private static readonly Color CableHighEmission = new Color(0.14f, 0.78f, 1.06f, 1f);
    private static readonly Color CableLowAlbedo = new Color(0.36f, 0.11f, 0.05f, 1f);
    private static readonly Color CableLowEmission = new Color(0.24f, 0.055f, 0.015f, 1f);

    private static readonly Color DragFreeAlbedo = new Color(0.18f, 0.90f, 1.00f, 1f);
    private static readonly Color DragFreeEmission = new Color(0.14f, 0.68f, 0.92f, 1f);
    private static readonly Color DragSnappedAlbedo = new Color(1.00f, 0.84f, 0.22f, 1f);
    private static readonly Color DragSnappedEmission = new Color(0.88f, 0.66f, 0.10f, 1f);
    private static readonly Color DragInvalidAlbedo = new Color(1.00f, 0.20f, 0.14f, 1f);
    private static readonly Color DragInvalidEmission = new Color(0.86f, 0.10f, 0.06f, 1f);

    private static readonly Color PacketHighAlbedo = new Color(0.76f, 0.98f, 1.00f, 1f);
    private static readonly Color PacketHighEmission = new Color(0.55f, 1.25f, 1.45f, 1f);
    private static readonly Color PacketLowAlbedo = new Color(0.95f, 0.44f, 0.16f, 1f);
    private static readonly Color PacketLowEmission = new Color(0.62f, 0.18f, 0.04f, 1f);

    // Palet Soket Port 3D (Torus Collar logam sci-fi + Inti Orb energi)
    private static readonly Color CollarDarkMetal = new Color(0.18f, 0.23f, 0.30f, 1f);
    private static readonly Color CollarCopperDarkMetal = new Color(0.28f, 0.14f, 0.10f, 1f);
    private static readonly Color CollarCyanMetal = new Color(0.18f, 0.56f, 0.70f, 1f);
    private static readonly Color CollarGoldMetal = new Color(0.86f, 0.68f, 0.18f, 1f);
    private static readonly Color CollarGreenMetal = new Color(0.18f, 0.68f, 0.38f, 1f);
    private static readonly Color CollarAmberMetal = new Color(0.84f, 0.56f, 0.16f, 1f);

    private static readonly Color PortInEmptyOrbColor = new Color(0.68f, 0.86f, 1.00f, 1f);
    private static readonly Color PortInEmptyOrbEmission = new Color(0.16f, 0.34f, 0.50f, 1f);
    private static readonly Color PortInConnectedHighOrbColor = new Color(0.18f, 0.96f, 0.88f, 1f);
    private static readonly Color PortInConnectedHighOrbEmission = new Color(0.14f, 0.82f, 0.76f, 1f);
    private static readonly Color PortInConnectedLowOrbColor = new Color(0.78f, 0.28f, 0.12f, 1f);
    private static readonly Color PortInConnectedLowOrbEmission = new Color(0.32f, 0.08f, 0.02f, 1f);
    private static readonly Color PortReplaceHintOrbColor = new Color(1.00f, 0.78f, 0.22f, 1f);
    private static readonly Color PortReplaceHintOrbEmission = new Color(0.76f, 0.48f, 0.06f, 1f);
    private static readonly Color PortHoverOrbColor = new Color(0.46f, 1.00f, 0.96f, 1f);
    private static readonly Color PortHoverOrbEmission = new Color(0.32f, 0.98f, 0.92f, 1f);
    private static readonly Color PortValidTargetOrbColor = new Color(0.28f, 1.00f, 0.54f, 1f);
    private static readonly Color PortValidTargetOrbEmission = new Color(0.18f, 0.90f, 0.38f, 1f);
    private static readonly Color PortSnapLockOrbColor = new Color(0.96f, 1.00f, 0.30f, 1f);
    private static readonly Color PortSnapLockOrbEmission = new Color(0.88f, 0.96f, 0.16f, 1f);

    private static readonly Color StubIdleAlbedo = new Color(0.20f, 0.26f, 0.34f, 1f);

    private struct ActivePortEntry
    {
        public GateNode node;
        public bool isOutput;
        public int slotIndex;
        public Vector2 nodeLocalXY;
        public Vector2 handleLocalXY;
        public Vector3 handleWorldPos;
        public Vector3 physicalWorldPos;
        public Vector3 socketAxisWorld;
        public float apparentScale;
        public float visualApparentScale;
        public bool isInteractable;
        public bool isConnected;
        public bool connectedSignalHigh;
    }

    private sealed class TubeCableSlot
    {
        public MeshFilter meshFilter;
        public MeshRenderer meshRenderer;
        public Mesh mesh;
        public Vector3[] vertices;
        public Vector3[] normals;
        public Vector2[] uvs;
        public int[] triangles;
        public Vector3 lastP0;
        public Vector3 lastC1;
        public Vector3 lastC2;
        public Vector3 lastP3;
        public Vector3 lastRefUp;
        public float lastRadius;
        public bool hasGeometry;
    }

    private struct PortSocketVisual
    {
        public Transform collarTransform;
        public MeshRenderer collarRenderer;
        public Transform orbTransform;
        public MeshRenderer orbRenderer;
        public Transform stubTransform;
        public MeshRenderer stubRenderer;
        public float smoothedRadiusFactor;
    }

    public event Action OnWireGrabbedSfx;
    public event Action OnWireTargetLockSfx;
    public event Action OnWireTargetUnlockSfx;
    public event Action OnWireConnectedSfx;
    public event Action OnWireDisconnectedSfx;
    public event Action OnWireRejectSfx;

    public Vector3 LastSfxWorldPosition { get; private set; }

    private readonly List<ManualConnectionEdge> wires = new List<ManualConnectionEdge>(MaxCableBeams);
    private readonly List<ActivePortEntry> activePorts = new List<ActivePortEntry>(MaxPortVisuals);

    private GameObject rootObj;

    // 1 Shared Material HologramFresnelHardware 1-Pass (fallback Standard) + 1 MaterialPropertyBlock (Zero-GC, OpenGLES3-safe)
    private Material pbrSharedMat;
    private MaterialPropertyBlock sharedMpb;
    private bool pbrHasEmissionProp = false;
    private bool pbrHasMetallicProp = false;
    private bool pbrHasGlossinessProp = false;
    private bool pbrHasRimProps = false;

    // Mesh 3D Statis Shared (dibangun sekali di EnsureVisualsCreated, dibersihkan di OnDestroy)
    private Mesh sharedSocketCollarMesh;
    private Mesh sharedSocketOrbMesh;
    private Mesh sharedLockRingMesh;
    private Mesh sharedStubCylinderMesh;
    private Mesh sharedPacketCapsuleMesh;

    // Pool Soket Port 3D (20 slot) + Cincin Lock Halo 3D
    private PortSocketVisual[] portSockets;
    private Transform lockHaloRingTransform;
    private MeshRenderer lockHaloRingRenderer;

    // Pool 20 Kabel Tabung 3D Terpasang + 1 Kabel Live Drag (Pinch)
    private TubeCableSlot[] installedTubePool;
    private TubeCableSlot liveDragTube;

    // Pool Zero-GC 60 Paket Sinyal Kapsul 3D
    private Transform[] packetTransforms;
    private MeshRenderer[] packetRenderers;
    private float[] cablePacketPhases;
    private int lastPacketAdvanceFrame = -1;
    private int lastSocketAnimFrame = -1;
    private int lastDragAnimFrame = -1;

    // Buffer pra-alokasi kurva & tabel trigonometri tabung 3D (Zero-GC)
    private readonly Vector3[] curvePointsBuffer = new Vector3[TubeRingCount];
    private readonly Vector3[] curveTangentsBuffer = new Vector3[TubeRingCount];
    private readonly float[] tubeCosTable = new float[TubeRingVertexCount];
    private readonly float[] tubeSinTable = new float[TubeRingVertexCount];

    private readonly List<Material> ownedMaterials = new List<Material>(4);
    private readonly List<Mesh> ownedMeshes = new List<Mesh>(MaxCableBeams + 8);

    private bool isInitialized = false;
    private int hoveredPortIndex = -1;
    private int secondaryHoveredPortIndex = -1;
    private int aimAssistTargetPortIndex = -1;
    private float aimAssistPullStrength = 0f;
    private bool isDraggingCable = false;
    private bool dragFromOutput = true;
    private bool dragStartedByUnplug = false;
    private bool isHoveringInvalidTargetDuringDrag = false;
    private GateNode dragOriginNode = null;
    private int dragOriginSlot = 0;
    private int snappedTargetPortIndex = -1;
    private Vector3 lastValidPinchWorldPos = Vector3.zero;
    private bool hasLastValidPinchWorld = false;
    private Vector3 smoothedLiveDragTipWorld = Vector3.zero;
    private Vector3 liveDragTipVelocity = Vector3.zero;
    private bool hasSmoothedLiveDragTip = false;
    private Vector2 lastLiveDragCursorLocalXY = Vector2.zero;
    private bool hasLastLiveDragCursorLocalXY = false;
    private bool hasUnpluggedEdgeBackup = false;
    private ManualConnectionEdge unpluggedEdgeBackup;

    // Warna transisi dinamis Live Drag Tube (halus tanpa alokasi)
    private Color currentDragAlbedo = DragFreeAlbedo;
    private Color currentDragEmission = DragFreeEmission;
    private Color currentDragRimLinear = DragFreeRimLinear;
    private float currentDragRimIntensity = 1.05f;
    private float currentDragRadius = TubeRadiusDragFree;

    // Snap Release-Latch (300 ms) + Sticky Snap Hysteresis + Soft Magnetic Aim-Assist
    private GateNode latchedTargetNode = null;
    private int latchedTargetSlot = 0;
    private Vector2 latchedTargetLocalXY = Vector2.zero;
    private float latchedTargetApparentScale = 1f;
    private float lastSnapUnscaledTime = -10f;
    private GateNode lockedSnapNode = null;
    private bool lockedSnapIsOutput = false;
    private int lockedSnapSlot = -1;
    private Vector2 currentMagnetOffsetLocalXY = Vector2.zero;

    // Edge-tracking target port yang terkunci selama drag kabel (Zero-GC, untuk ping Lock/Unlock tepat 1x per transisi)
    private GateNode lastLockedTargetNode = null;
    private bool lastLockedTargetIsOutput = false;
    private int lastLockedTargetSlot = -1;
    private Vector3 lastLockedTargetWorldPos = Vector3.zero;

    public bool IsHoveringPort => hoveredPortIndex >= 0 && hoveredPortIndex < activePorts.Count;
    public bool IsSecondaryHoveringPort => secondaryHoveredPortIndex >= 0 && secondaryHoveredPortIndex < activePorts.Count;
    public bool IsDraggingCable => isDraggingCable;
    public bool IsSnappedTargetPort => isDraggingCable && snappedTargetPortIndex >= 0 && snappedTargetPortIndex < activePorts.Count;
    public GateNode HoveredPortNode => IsHoveringPort ? activePorts[hoveredPortIndex].node : null;
    public GateNode SecondaryHoveredPortNode => IsSecondaryHoveringPort ? activePorts[secondaryHoveredPortIndex].node : null;
    public bool IsHoveringOutputNodePort => IsHoveringPort && activePorts[hoveredPortIndex].node != null && activePorts[hoveredPortIndex].node.nodeType == NodeType.OutputActuator;
    public int ActiveWireCount => wires.Count;
    public int TopologyVersion { get; private set; }
    public Vector3 AssistedPinchWorldPosition { get; private set; }

    /// <summary>
    /// Mereset offset magnetik aim-assist saat kendali hover kabel berpindah antar-slot tangan (ambidextrous Zero-GC).
    /// </summary>
    public void ResetAimAssistOffset()
    {
        currentMagnetOffsetLocalXY = Vector2.zero;
        aimAssistTargetPortIndex = -1;
        aimAssistPullStrength = 0f;
    }

    private static float ComputeEffectiveStageZoom(float stageZoomScale)
    {
        return Mathf.Clamp(stageZoomScale, AirWeavingStageController.MinZoomScale, AirWeavingStageController.MaxZoomScale);
    }

    private static float ComputeSocketVisualScale(float effectiveZoom)
    {
        if (effectiveZoom >= 1.0f)
        {
            return effectiveZoom;
        }
        float t = Mathf.Clamp01((effectiveZoom - AirWeavingStageController.MinZoomScale) / (1.0f - AirWeavingStageController.MinZoomScale));
        return Mathf.Lerp(0.26f, 1.0f, t);
    }

    private static float ComputeTubeVisualScale(float effectiveZoom)
    {
        if (effectiveZoom >= 1.0f)
        {
            return effectiveZoom;
        }
        float t = Mathf.Clamp01((effectiveZoom - AirWeavingStageController.MinZoomScale) / (1.0f - AirWeavingStageController.MinZoomScale));
        return Mathf.Lerp(0.32f, 1.0f, t);
    }

    /// <summary>
    /// Mengembalikan jarak ternormalisasi (0..1 di dalam hit radius port, &lt;1.6 dalam zona magnet, atau float.MaxValue)
    /// terhadap port aktif terdekat untuk arbitrer ambidextrous 2 tangan. Zero-GC.
    /// </summary>
    public float QueryNearestPortNormalizedDistance(Vector2 cursorLocalXY, float stageZoomScale)
    {
        if (activePorts.Count == 0)
        {
            return float.MaxValue;
        }

        float fallbackScale = Mathf.Clamp(stageZoomScale, MinPortScreenSpaceHitboxScale, 6.0f);
        float bestNorm = float.MaxValue;

        for (int i = 0; i < activePorts.Count; i++)
        {
            ActivePortEntry p = activePorts[i];
            if (!p.isInteractable)
            {
                continue;
            }
            float portScale = p.apparentScale > 0.01f ? p.apparentScale : fallbackScale;
            float hitRadius = (p.isOutput ? PortOutHitRadius : PortInHitRadius) * portScale;
            float magnetLimit = HoverMagnetRadius * portScale;
            float dist = Vector2.Distance(cursorLocalXY, p.handleLocalXY);
            if (dist <= Mathf.Max(hitRadius, magnetLimit))
            {
                float norm = dist / Mathf.Max(0.001f, hitRadius);
                if (norm < bestNorm)
                {
                    bestNorm = norm;
                }
            }
        }

        return bestNorm;
    }

    /// <summary>
    /// Mengembalikan identitas stabil &amp; posisi dunia soket port yang sedang di-hover (Zero-GC, untuk edge-triggered Hover Tick).
    /// </summary>
    public bool TryGetHoveredPortInfo(out GateNode portNode, out bool isOutput, out int slotIndex, out Vector3 handleWorldPos)
    {
        if (!isDraggingCable && hoveredPortIndex >= 0 && hoveredPortIndex < activePorts.Count)
        {
            ActivePortEntry p = activePorts[hoveredPortIndex];
            if (p.node != null)
            {
                portNode = p.node;
                isOutput = p.isOutput;
                slotIndex = p.slotIndex;
                handleWorldPos = p.handleWorldPos;
                return true;
            }
        }

        portNode = null;
        isOutput = false;
        slotIndex = 0;
        handleWorldPos = Vector3.zero;
        return false;
    }

    /// <summary>
    /// Mengembalikan posisi ujung seret yang sudah dihaluskan, panjang kabel aktif, &amp; status kunci port sasaran
    /// untuk continuous CableDragHum (Zero-GC).
    /// </summary>
    public bool TryGetActiveDragFeedback(out Vector3 tipWorldPos, out float dragLength, out bool isLockedTarget)
    {
        if (isDraggingCable && dragOriginNode != null)
        {
            isLockedTarget = snappedTargetPortIndex >= 0 && snappedTargetPortIndex < activePorts.Count;
            tipWorldPos = hasSmoothedLiveDragTip
                ? smoothedLiveDragTipWorld
                : (isLockedTarget
                    ? activePorts[snappedTargetPortIndex].handleWorldPos
                    : (hasLastValidPinchWorld ? lastValidPinchWorldPos : dragOriginNode.transform.position));

            Vector3 startWorld = TryGetPortHandleWorldPos(dragOriginNode, dragFromOutput, dragOriginSlot, out Vector3 originHandle)
                ? originHandle
                : dragOriginNode.transform.position;
            dragLength = Vector3.Distance(startWorld, tipWorldPos);
            return true;
        }

        tipWorldPos = Vector3.zero;
        dragLength = 0f;
        isLockedTarget = false;
        return false;
    }

    public void EnsureVisualsCreated()
    {
        if (isInitialized)
        {
            return;
        }

        rootObj = new GameObject("AR_AirWeavingCableRouter");
        rootObj.transform.SetParent(transform, false);
        rootObj.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        // Pre-compute tabel cos/sin penampang melingkar tabung (8 sisi + 1 titik jahitan UV mulus)
        for (int j = 0; j < TubeRingVertexCount; j++)
        {
            float angle = (j % TubeRadialSegments) * ((Mathf.PI * 2f) / TubeRadialSegments);
            tubeCosTable[j] = Mathf.Cos(angle);
            tubeSinTable[j] = Mathf.Sin(angle);
        }

        // 1. Satu Shared Material HologramFresnelHardware 1-Pass (fallback Standard PBR, Forward Linear & OpenGLES3)
        pbrSharedMat = CreatePbrStandardMaterial(Color.white, metallic: 0.58f, glossiness: 0.84f);
        pbrHasEmissionProp = pbrSharedMat != null && pbrSharedMat.HasProperty(EmissionColorPropId);
        pbrHasMetallicProp = pbrSharedMat != null && pbrSharedMat.HasProperty(MetallicPropId);
        pbrHasGlossinessProp = pbrSharedMat != null && pbrSharedMat.HasProperty(GlossinessPropId);
        pbrHasRimProps = pbrSharedMat != null
                         && pbrSharedMat.HasProperty(RimColorPropId)
                         && pbrSharedMat.HasProperty(RimPowerPropId)
                         && pbrSharedMat.HasProperty(RimIntensityPropId);
        sharedMpb = new MaterialPropertyBlock();

        // 2. Bangun Mesh 3D Statis Shared (Torus Collar, Energy Core Orb, Lock Halo Ring, Conduit Stub, Signal Capsule)
        sharedSocketCollarMesh = BuildTorusMesh("Holo_SharedSocketCollar3D", majorSegments: 16, minorSegments: 8, majorRadius: 1.0f, minorRadiusXY: 0.28f, minorRadiusZ: 0.36f);
        sharedLockRingMesh = BuildTorusMesh("Holo_SharedSocketLockHalo3D", majorSegments: 20, minorSegments: 6, majorRadius: 1.0f, minorRadiusXY: 0.085f, minorRadiusZ: 0.095f);
        sharedSocketOrbMesh = BuildUvSphereMesh("Holo_SharedSocketOrb3D", lonSegments: 12, latSegments: 8, radius: 1.0f);
        sharedStubCylinderMesh = BuildUnitZStubCylinderMesh("Holo_SharedPortStub3D", radialSegments: 8);
        sharedPacketCapsuleMesh = BuildZAlignedPacketCapsuleMesh("Holo_SharedSignalCapsule3D", lonSegments: 10, latSegments: 8);

        // 3. Pool 20 Soket Port 3D (Torus Collar 3D + Inti Orb Energi 3D + Batang Penghubung 3D)
        portSockets = new PortSocketVisual[MaxPortVisuals];
        for (int i = 0; i < MaxPortVisuals; i++)
        {
            MeshRenderer collarMr = CreateStaticMeshObject($"Holo_PortSocketCollar_{i}", sharedSocketCollarMesh, out Transform collarTr);
            MeshRenderer orbMr = CreateStaticMeshObject($"Holo_PortSocketOrb_{i}", sharedSocketOrbMesh, out Transform orbTr);
            MeshRenderer stubMr = CreateStaticMeshObject($"Holo_PortSocketStub_{i}", sharedStubCylinderMesh, out Transform stubTr);

            portSockets[i] = new PortSocketVisual
            {
                collarTransform = collarTr,
                collarRenderer = collarMr,
                orbTransform = orbTr,
                orbRenderer = orbMr,
                stubTransform = stubTr,
                stubRenderer = stubMr,
                smoothedRadiusFactor = 1.0f
            };
        }

        // 4. Cincin Lock Halo 3D untuk Port yang sedang di-Hover / Dikunci Aim-Assist Sticky Snap
        lockHaloRingRenderer = CreateStaticMeshObject("Holo_PortLockHalo3D", sharedLockRingMesh, out lockHaloRingTransform);

        // 5. Pool 20 Kabel Tabung 3D PBR Terpasang + 1 Kabel Live Drag (Pinch) — semua MarkDynamic() & buffer pra-alokasi
        installedTubePool = new TubeCableSlot[MaxCableBeams];
        cablePacketPhases = new float[MaxCableBeams];
        for (int i = 0; i < MaxCableBeams; i++)
        {
            installedTubePool[i] = CreateDynamicTubeSlot($"Holo_PbrTubeCable_{i}");
            cablePacketPhases[i] = Mathf.Repeat(i * 0.23f, 1f);
        }

        liveDragTube = CreateDynamicTubeSlot("Holo_PbrLiveDragTube");

        // 6. Pool Zero-GC 60 Paket Sinyal Kapsul 3D sejajar sumbu +Z (langsung pakai Quaternion.LookRotation(tangent))
        packetTransforms = new Transform[TotalPacketPoolSize];
        packetRenderers = new MeshRenderer[TotalPacketPoolSize];
        for (int i = 0; i < TotalPacketPoolSize; i++)
        {
            MeshRenderer mr = CreateStaticMeshObject($"Holo_SignalPacket3D_{i}", sharedPacketCapsuleMesh, out Transform pktTr);
            packetTransforms[i] = pktTr;
            packetRenderers[i] = mr;
        }

        isInitialized = true;
        HideImmediate();
    }

    public void HideImmediate()
    {
        CancelActiveDrag();
        hoveredPortIndex = -1;
        secondaryHoveredPortIndex = -1;
        aimAssistTargetPortIndex = -1;
        aimAssistPullStrength = 0f;
        hasLastValidPinchWorld = false;
        currentMagnetOffsetLocalXY = Vector2.zero;
        AssistedPinchWorldPosition = Vector3.zero;
        activePorts.Clear();

        if (!isInitialized)
        {
            return;
        }

        if (rootObj != null && rootObj.activeSelf)
        {
            rootObj.SetActive(false);
        }
        DisableAllRenderers();
    }

    public void ClearAllWires(CircuitManager circuitManager)
    {
        CancelActiveDrag();
        wires.Clear();
        TopologyVersion++;
        DisableCableTubesAndPacketsFrom(0);
        if (circuitManager != null)
        {
            circuitManager.SetManualConnections(wires);
        }
    }

    public void RemoveWiresForNode(GateNode node, CircuitManager circuitManager)
    {
        if (node == null)
        {
            return;
        }

        if (isDraggingCable && dragOriginNode == node)
        {
            CancelActiveDrag();
        }

        bool removedAny = false;
        for (int i = wires.Count - 1; i >= 0; i--)
        {
            if (wires[i].source == node || wires[i].target == node)
            {
                wires.RemoveAt(i);
                removedAny = true;
            }
        }

        if (removedAny)
        {
            TopologyVersion++;
            DisableCableTubesAndPacketsFrom(wires.Count);
            if (circuitManager != null)
            {
                circuitManager.SetManualConnections(wires);
            }
        }
    }

    /// <summary>
    /// Membatalkan CableDrag yang baru saja mulai saat pengguna meng-upgrade mulus ke mode 2 tangan,
    /// sambil mengembalikan kabel yang sempat tercabut (unplug) agar tidak ada kabel hilang. Zero-GC.
    /// </summary>
    public void RevertActiveDragForTwoHandUpgrade(CircuitManager circuitManager)
    {
        if (isDraggingCable
            && dragStartedByUnplug
            && hasUnpluggedEdgeBackup
            && unpluggedEdgeBackup.source != null
            && unpluggedEdgeBackup.target != null
            && unpluggedEdgeBackup.source.isTracked
            && unpluggedEdgeBackup.target.isTracked
            && FindWireByTargetSlot(unpluggedEdgeBackup.target, unpluggedEdgeBackup.targetSlotIndex) < 0
            && wires.Count < MaxCableBeams)
        {
            wires.Add(unpluggedEdgeBackup);
            TopologyVersion++;
            if (circuitManager != null)
            {
                circuitManager.SetManualConnections(wires);
            }
        }

        CancelActiveDrag();
    }

    public void CancelActiveDrag()
    {
        isDraggingCable = false;
        dragStartedByUnplug = false;
        hasUnpluggedEdgeBackup = false;
        isHoveringInvalidTargetDuringDrag = false;
        dragOriginNode = null;
        dragOriginSlot = 0;
        snappedTargetPortIndex = -1;
        latchedTargetNode = null;
        latchedTargetSlot = 0;
        lastSnapUnscaledTime = -10f;
        lockedSnapNode = null;
        lockedSnapIsOutput = false;
        lockedSnapSlot = -1;
        lastLockedTargetNode = null;
        lastLockedTargetIsOutput = false;
        lastLockedTargetSlot = -1;
        hasSmoothedLiveDragTip = false;
        liveDragTipVelocity = Vector3.zero;
        hasLastLiveDragCursorLocalXY = false;
        if (liveDragTube != null && liveDragTube.meshRenderer != null)
        {
            liveDragTube.meshRenderer.enabled = false;
        }
    }

    /// <summary>
    /// Membangun daftar soket port aktif, memproses interaksi Pinch (Cubit #4-#8) pada port kabel,
    /// dan merender 100% kabel Tabung 3D PBR &amp; soket 3D Mode Udara mengikuti <c>HologramStageRoot</c> (Zero-GC).
    /// </summary>
    public void Tick(
        Camera mainCam,
        CircuitManager circuitManager,
        GateNode inputANode,
        GateNode inputBNode,
        GateNode outputNode,
        List<GateNode> activePlacedGates,
        float workbenchDepth,
        bool isAirWeavingActive,
        bool isUiOverlayOpen,
        bool isVerifying,
        bool blockedByOtherMode,
        bool isHandDetected,
        bool isPinchActive,
        bool pinchJustStarted,
        bool pinchJustEnded,
        Vector2 stabilizedPinchLocalXY,
        Vector2 livePinchLocalXY,
        Vector3 pinchWorldPos,
        float dt = 0.016f,
        float pinchSpeedUnitsPerSec = 0f,
        float pinchRatio = 1f,
        bool isPinchReleaseIntent = false,
        Transform stageRoot = null,
        float stageZoomScale = 1f,
        bool allowBeginDrag = true,
        bool hasSecondaryHoverHand = false,
        Vector2 secondaryPinchLocalXY = default,
        bool isHandLiveTracking = true,
        bool pinchEndedByTrackingLoss = false,
        bool deferVisualRender = false)
    {
        if (!isInitialized)
        {
            return;
        }

        if (!isAirWeavingActive || mainCam == null)
        {
            HideImmediate();
            return;
        }

        if (rootObj != null && !rootObj.activeSelf)
        {
            rootObj.SetActive(true);
        }

        Transform camT = mainCam.transform;
        float effectiveZoom = ComputeEffectiveStageZoom(stageZoomScale);
        float fallbackHitRadiusScale = Mathf.Clamp(effectiveZoom, MinPortScreenSpaceHitboxScale, 6.0f);
        bool liveTracking = isHandDetected && isHandLiveTracking;

        // 1. Bersihkan edge yang node-nya sudah tidak aktif (defensif)
        PruneUntrackedWires(circuitManager);

        // 2. Bangun daftar soket port 3D proyektif mengikuti transform HologramStageRoot
        activePorts.Clear();
        RegisterNodePorts(camT, stageRoot, inputANode, workbenchDepth, effectiveZoom);
        RegisterNodePorts(camT, stageRoot, inputBNode, workbenchDepth, effectiveZoom);
        RegisterNodePorts(camT, stageRoot, outputNode, workbenchDepth, effectiveZoom);
        if (activePlacedGates != null)
        {
            for (int i = 0; i < activePlacedGates.Count; i++)
            {
                RegisterNodePorts(camT, stageRoot, activePlacedGates[i], workbenchDepth, effectiveZoom);
            }
        }

        // Koordinat livePinchLocalXY & stabilizedPinchLocalXY sudah disatukan pada bidang sinar referensi (workbenchDepth)
        // oleh AirWeavingHandGestureDetector — hindari proyeksi ganda agar bidikan 100% sinkron pada jarak tangan berapa pun.
        Vector3 pinchCamLocal = camT.InverseTransformPoint(pinchWorldPos);
        float pinchDepth = Mathf.Max(0.35f, pinchCamLocal.z);
        float pinchProjFactor = workbenchDepth / pinchDepth;
        Vector2 projectedLivePinchXY = livePinchLocalXY;
        Vector2 projectedStabilizedPinchXY = stabilizedPinchLocalXY;

        // 2B. Soft Magnetic Aim-Assist (R=0.30 hover port, R=0.36 drag target kompatibel, maxPull=0.50, blend 18/s, 0 saat >1.1 u/s)
        bool hardBlocked = isUiOverlayOpen || isVerifying || blockedByOtherMode;
        UpdateSoftMagneticAimOffset(
            projectedLivePinchXY,
            pinchSpeedUnitsPerSec,
            isHandDetected && !hardBlocked,
            circuitManager,
            fallbackHitRadiusScale,
            dt);

        Vector2 assistedLivePinchXY = projectedLivePinchXY + currentMagnetOffsetLocalXY;
        Vector2 assistedStabilizedPinchXY = projectedStabilizedPinchXY + currentMagnetOffsetLocalXY;
        float invPinchProj = 1f / Mathf.Max(0.25f, pinchProjFactor);
        Vector3 assistedPinchWorldPos = pinchWorldPos
                                        + (camT.right * (currentMagnetOffsetLocalXY.x * invPinchProj))
                                        + (camT.up * (currentMagnetOffsetLocalXY.y * invPinchProj));
        AssistedPinchWorldPosition = assistedPinchWorldPos;

        if (liveTracking)
        {
            lastValidPinchWorldPos = assistedPinchWorldPos;
            hasLastValidPinchWorld = true;
        }

        // 3. Evaluasi interaksi port & drag kabel berbasis Pinch (Cubit #4-#8) + Sticky Snap + Release-Intent Freeze
        secondaryHoveredPortIndex = -1;
        if (hardBlocked)
        {
            if (isDraggingCable)
            {
                CancelActiveDrag();
            }
            hoveredPortIndex = -1;
            snappedTargetPortIndex = -1;
            isHoveringInvalidTargetDuringDrag = false;
        }
        else if (isDraggingCable)
        {
            hoveredPortIndex = -1;
            int prevLockedIdx = FindActivePortIndex(lockedSnapNode, lockedSnapIsOutput, lockedSnapSlot);
            if (prevLockedIdx >= 0 && (!activePorts[prevLockedIdx].isInteractable || !IsCompatibleDragTarget(activePorts[prevLockedIdx], circuitManager)))
            {
                prevLockedIdx = -1;
            }

            // Bekukan target kunci hanya saat jari sengaja mulai membuka lebar (> 0.64f menuju ambang lepas sticky 0.75f).
            // Saat tangan hilang / occlusion grace (!liveTracking), tahan visual tanpa memperbarui latch agar tidak auto-commit.
            bool intentionalOpeningFreeze = liveTracking && (pinchRatio > SnapReleaseIntentFreezeRatio);
            if (!liveTracking)
            {
                snappedTargetPortIndex = prevLockedIdx;
            }
            else if (prevLockedIdx >= 0 && intentionalOpeningFreeze)
            {
                snappedTargetPortIndex = prevLockedIdx;
                ActivePortEntry frozen = activePorts[snappedTargetPortIndex];
                latchedTargetNode = frozen.node;
                latchedTargetSlot = frozen.slotIndex;
                latchedTargetLocalXY = frozen.handleLocalXY;
                latchedTargetApparentScale = frozen.apparentScale > 0.01f ? frozen.apparentScale : fallbackHitRadiusScale;
                lastSnapUnscaledTime = Time.unscaledTime;
            }
            else
            {
                snappedTargetPortIndex = FindNearestCompatibleTargetPort(assistedLivePinchXY, prevLockedIdx, circuitManager, fallbackHitRadiusScale);
                if (snappedTargetPortIndex >= 0 && snappedTargetPortIndex < activePorts.Count)
                {
                    ActivePortEntry snapped = activePorts[snappedTargetPortIndex];
                    lockedSnapNode = snapped.node;
                    lockedSnapIsOutput = snapped.isOutput;
                    lockedSnapSlot = snapped.slotIndex;
                    latchedTargetNode = snapped.node;
                    latchedTargetSlot = snapped.slotIndex;
                    latchedTargetLocalXY = snapped.handleLocalXY;
                    latchedTargetApparentScale = snapped.apparentScale > 0.01f ? snapped.apparentScale : fallbackHitRadiusScale;
                    lastSnapUnscaledTime = Time.unscaledTime;
                }
                else
                {
                    lockedSnapNode = null;
                    lockedSnapIsOutput = false;
                    lockedSnapSlot = -1;
                }
            }

            // Deteksi apakah ujung drag berada di atas port tidak kompatibel / zona batal cabut (untuk warna merah dinamis)
            isHoveringInvalidTargetDuringDrag = EvaluateDragInvalidVisualState(
                liveTracking,
                assistedLivePinchXY,
                fallbackHitRadiusScale,
                circuitManager);

            bool endingDragThisFrame = pinchJustEnded || !isPinchActive;
            if (!endingDragThisFrame)
            {
                if (snappedTargetPortIndex >= 0 && snappedTargetPortIndex < activePorts.Count)
                {
                    ActivePortEntry curTarget = activePorts[snappedTargetPortIndex];
                    if (curTarget.node != lastLockedTargetNode
                        || curTarget.isOutput != lastLockedTargetIsOutput
                        || curTarget.slotIndex != lastLockedTargetSlot)
                    {
                        lastLockedTargetNode = curTarget.node;
                        lastLockedTargetIsOutput = curTarget.isOutput;
                        lastLockedTargetSlot = curTarget.slotIndex;
                        lastLockedTargetWorldPos = curTarget.handleWorldPos;
                        LastSfxWorldPosition = curTarget.handleWorldPos;
                        OnWireTargetLockSfx?.Invoke();
                    }
                    else
                    {
                        lastLockedTargetWorldPos = curTarget.handleWorldPos;
                    }
                }
                else if (lastLockedTargetNode != null)
                {
                    LastSfxWorldPosition = lastLockedTargetWorldPos;
                    lastLockedTargetNode = null;
                    lastLockedTargetIsOutput = false;
                    lastLockedTargetSlot = -1;
                    OnWireTargetUnlockSfx?.Invoke();
                }
            }
            else
            {
                CompleteOrCancelCableDrag(
                    circuitManager,
                    liveTracking,
                    pinchEndedByTrackingLoss,
                    assistedLivePinchXY,
                    fallbackHitRadiusScale);
            }
        }
        else if (!isHandDetected)
        {
            hoveredPortIndex = -1;
            snappedTargetPortIndex = -1;
            isHoveringInvalidTargetDuringDrag = false;
        }
        else
        {
            isHoveringInvalidTargetDuringDrag = false;
            Vector2 queryXY = (pinchJustStarted && allowBeginDrag) ? assistedStabilizedPinchXY : assistedLivePinchXY;
            hoveredPortIndex = FindNearestPortIndex(queryXY, fallbackHitRadiusScale);

            if (pinchJustStarted && allowBeginDrag && hoveredPortIndex >= 0 && hoveredPortIndex < activePorts.Count)
            {
                BeginCableDragFromPort(activePorts[hoveredPortIndex], circuitManager);
            }
            else if (hasSecondaryHoverHand)
            {
                secondaryHoveredPortIndex = FindNearestPortIndex(secondaryPinchLocalXY, fallbackHitRadiusScale);
            }
        }

        // 4. Perbarui damping ujung kabel aktif berbasis dt (licin, berat, presisi tanpa teleportasi saat snap/unsnap)
        if (isDraggingCable)
        {
            if (isHandDetected && !hardBlocked)
            {
                lastLiveDragCursorLocalXY = assistedLivePinchXY;
                hasLastLiveDragCursorLocalXY = true;
            }
            bool snappedNow = snappedTargetPortIndex >= 0 && snappedTargetPortIndex < activePorts.Count;
            Vector3 desiredTipWorld = snappedNow
                ? activePorts[snappedTargetPortIndex].handleWorldPos
                : (hasLastValidPinchWorld ? lastValidPinchWorldPos : assistedPinchWorldPos);
            if (!hasSmoothedLiveDragTip)
            {
                smoothedLiveDragTipWorld = desiredTipWorld;
                liveDragTipVelocity = Vector3.zero;
                hasSmoothedLiveDragTip = true;
            }
            else
            {
                float safeTipDt = Mathf.Clamp(dt, 0.0001f, 0.050f);
                float tipOmega = snappedNow ? LiveDragTipSnapSpringOmega : LiveDragTipFreeSpringOmega;
                smoothedLiveDragTipWorld = StepCriticallyDampedSpring3D(
                    smoothedLiveDragTipWorld,
                    desiredTipWorld,
                    ref liveDragTipVelocity,
                    tipOmega,
                    safeTipDt);
            }
        }
        else
        {
            hasSmoothedLiveDragTip = false;
            liveDragTipVelocity = Vector3.zero;
            hasLastLiveDragCursorLocalXY = false;
        }

        // 5. Perbarui seluruh Soket Port 3D, Kabel Tabung 3D Live Drag, & 100% Kabel Tabung 3D Terpasang + Paket Sinyal
        //    (dilewati bila deferVisualRender == true karena SyncVisualsAfterPoseUpdate selalu merender 1x tepat pasca update pose node)
        if (!deferVisualRender)
        {
            Vector3 dragRenderTipWorld = hasSmoothedLiveDragTip
                ? smoothedLiveDragTipWorld
                : (hasLastValidPinchWorld ? lastValidPinchWorldPos : assistedPinchWorldPos);
            RenderPortSockets(camT, stageRoot, circuitManager, (isHandDetected || hasSecondaryHoverHand) && !hardBlocked, effectiveZoom);
            RenderLiveDragTube(camT, stageRoot, effectiveZoom, dragRenderTipWorld);
            RenderAllUniformTubeCables(camT, stageRoot, effectiveZoom);
        }
    }

    /// <summary>
    /// Menyinkronkan ulang posisi Soket Port 3D, stub, dan ujung Kabel Tabung 3D PBR tepat SETELAH pose melayang/miring 3D
    /// semua node diperbarui pada frame yang sama agar nol jeda (0-frame lag). Zero-GC.
    /// </summary>
    public void SyncVisualsAfterPoseUpdate(
        Camera mainCam,
        CircuitManager circuitManager,
        GateNode inputANode,
        GateNode inputBNode,
        GateNode outputNode,
        List<GateNode> activePlacedGates,
        float workbenchDepth,
        bool isAirWeavingActive,
        bool allowHoverHighlight,
        Vector3 pinchWorldPos,
        Transform stageRoot = null,
        float stageZoomScale = 1f)
    {
        if (!isInitialized || !isAirWeavingActive || mainCam == null)
        {
            return;
        }

        if (rootObj != null && !rootObj.activeSelf)
        {
            rootObj.SetActive(true);
        }

        Transform camT = mainCam.transform;
        float effectiveZoom = ComputeEffectiveStageZoom(stageZoomScale);
        activePorts.Clear();
        RegisterNodePorts(camT, stageRoot, inputANode, workbenchDepth, effectiveZoom);
        RegisterNodePorts(camT, stageRoot, inputBNode, workbenchDepth, effectiveZoom);
        RegisterNodePorts(camT, stageRoot, outputNode, workbenchDepth, effectiveZoom);
        if (activePlacedGates != null)
        {
            for (int i = 0; i < activePlacedGates.Count; i++)
            {
                RegisterNodePorts(camT, stageRoot, activePlacedGates[i], workbenchDepth, effectiveZoom);
            }
        }

        Vector3 dragRenderTipWorld = hasSmoothedLiveDragTip
            ? smoothedLiveDragTipWorld
            : (hasLastValidPinchWorld ? lastValidPinchWorldPos : pinchWorldPos);
        RenderPortSockets(camT, stageRoot, circuitManager, allowHoverHighlight, effectiveZoom);
        RenderLiveDragTube(camT, stageRoot, effectiveZoom, dragRenderTipWorld);
        RenderAllUniformTubeCables(camT, stageRoot, effectiveZoom);
    }

    private void BeginCableDragFromPort(ActivePortEntry port, CircuitManager circuitManager)
    {
        if (port.node == null)
        {
            return;
        }

        currentDragAlbedo = DragFreeAlbedo;
        currentDragEmission = DragFreeEmission;
        currentDragRadius = TubeRadiusDragFree;
        isHoveringInvalidTargetDuringDrag = false;
        smoothedLiveDragTipWorld = port.handleWorldPos;
        liveDragTipVelocity = Vector3.zero;
        hasSmoothedLiveDragTip = true;
        hasUnpluggedEdgeBackup = false;

        if (port.isOutput)
        {
            // Pinch pada Port_Out SELALU menarik cabang kabel baru (fan-out) tanpa memutus kabel lama
            isDraggingCable = true;
            dragFromOutput = true;
            dragStartedByUnplug = false;
            dragOriginNode = port.node;
            dragOriginSlot = 0;
            snappedTargetPortIndex = -1;
            latchedTargetNode = null;
            lastSnapUnscaledTime = -10f;
            port.node.TriggerTapFeedback();
            LastSfxWorldPosition = port.handleWorldPos;
            OnWireGrabbedSfx?.Invoke();
            return;
        }

        // Pinch pada Port_In: bila sudah terisi -> CABUT kabel lama untuk dipindahkan / dihapus di ruang kosong
        int existingIdx = FindWireByTargetSlot(port.node, port.slotIndex);
        if (existingIdx >= 0)
        {
            ManualConnectionEdge oldEdge = wires[existingIdx];
            unpluggedEdgeBackup = oldEdge;
            hasUnpluggedEdgeBackup = true;
            wires.RemoveAt(existingIdx);
            TopologyVersion++;
            DisableCableTubesAndPacketsFrom(wires.Count);
            if (circuitManager != null)
            {
                circuitManager.SetManualConnections(wires);
            }

            isDraggingCable = true;
            dragFromOutput = true;
            dragStartedByUnplug = true;
            dragOriginNode = oldEdge.source;
            dragOriginSlot = 0;
            snappedTargetPortIndex = -1;
            latchedTargetNode = null;
            lastSnapUnscaledTime = -10f;
            port.node.TriggerTapFeedback();
            LastSfxWorldPosition = port.handleWorldPos;
            OnWireGrabbedSfx?.Invoke();
            return;
        }

        // Port_In kosong -> tarik kabel mundur menuju Port_Out sumber
        isDraggingCable = true;
        dragFromOutput = false;
        dragStartedByUnplug = false;
        dragOriginNode = port.node;
        dragOriginSlot = port.slotIndex;
        snappedTargetPortIndex = -1;
        latchedTargetNode = null;
        lastSnapUnscaledTime = -10f;
        port.node.TriggerTapFeedback();
        LastSfxWorldPosition = port.handleWorldPos;
        OnWireGrabbedSfx?.Invoke();
    }

    private void CompleteOrCancelCableDrag(
        CircuitManager circuitManager,
        bool isHandLiveTracking,
        bool pinchEndedByTrackingLoss,
        Vector2 releaseCursorLocalXY,
        float hitRadiusScale)
    {
        // Anti-Nyasar Wajib: jika kabel lepas karena tangan hilang dari kamera (tracking lost),
        // WAJIB batalkan drag secara bersih — JANGAN pernah auto-snap atau commit ke soket terdekat!
        if (!isHandLiveTracking || pinchEndedByTrackingLoss)
        {
            LastSfxWorldPosition = hasLastValidPinchWorld
                ? lastValidPinchWorldPos
                : (dragOriginNode != null ? dragOriginNode.transform.position : Vector3.zero);
            OnWireDisconnectedSfx?.Invoke();
            CancelActiveDrag();
            return;
        }

        GateNode resolvedTargetNode = null;
        int resolvedTargetSlot = 0;

        if (snappedTargetPortIndex >= 0 && snappedTargetPortIndex < activePorts.Count)
        {
            ActivePortEntry targetPort = activePorts[snappedTargetPortIndex];
            resolvedTargetNode = targetPort.node;
            resolvedTargetSlot = targetPort.slotIndex;
        }
        else if (latchedTargetNode != null
                 && (Time.unscaledTime - lastSnapUnscaledTime) <= SnapReleaseLatchSeconds
                 && Vector2.Distance(releaseCursorLocalXY, latchedTargetLocalXY) <= (SnapReleaseMaxEscapeDist * Mathf.Max(MinPortScreenSpaceHitboxScale, latchedTargetApparentScale)))
        {
            resolvedTargetNode = latchedTargetNode;
            resolvedTargetSlot = latchedTargetSlot;
        }

        if (resolvedTargetNode != null && dragOriginNode != null)
        {
            GateNode srcNode = dragFromOutput ? dragOriginNode : resolvedTargetNode;
            GateNode dstNode = dragFromOutput ? resolvedTargetNode : dragOriginNode;
            int preferredSlot = dragFromOutput ? resolvedTargetSlot : dragOriginSlot;

            if (IsValidConnectionPair(srcNode, dstNode, circuitManager)
                && !HasExistingWireBetween(srcNode, dstNode, -1))
            {
                int finalSlot = ResolveCommitSlotForTarget(dstNode, preferredSlot);
                int replacedIdx = FindWireByTargetSlot(dstNode, finalSlot);
                if (replacedIdx >= 0)
                {
                    wires.RemoveAt(replacedIdx);
                }

                if (wires.Count < MaxCableBeams)
                {
                    wires.Add(new ManualConnectionEdge
                    {
                        source = srcNode,
                        target = dstNode,
                        targetSlotIndex = finalSlot
                    });
                }

                TopologyVersion++;

                if (circuitManager != null)
                {
                    circuitManager.SetManualConnections(wires);
                }

                dstNode.TriggerInputPortSnapFlash(finalSlot, dstNode.GetInputPortWorldPosition(finalSlot));
                srcNode.TriggerTapFeedback();
                LastSfxWorldPosition = TryGetPortHandleWorldPos(dstNode, false, finalSlot, out Vector3 snapPos)
                    ? snapPos
                    : dstNode.transform.position;
                OnWireConnectedSfx?.Invoke();
            }
            else
            {
                LastSfxWorldPosition = hasLastValidPinchWorld ? lastValidPinchWorldPos : dstNode.transform.position;
                OnWireRejectSfx?.Invoke();
            }
        }
        else
        {
            // Cek apakah pemain melepas kabel pada port lain yang tidak kompatibel (arah salah / siklus / duplikat)
            int droppedPortIdx = isHandLiveTracking ? FindNearestPortIndex(releaseCursorLocalXY, hitRadiusScale) : -1;
            bool droppedOnIncompatiblePort = false;
            if (droppedPortIdx >= 0 && droppedPortIdx < activePorts.Count)
            {
                ActivePortEntry droppedPort = activePorts[droppedPortIdx];
                bool isSameOriginPort = droppedPort.node == dragOriginNode
                                        && droppedPort.isOutput == dragFromOutput
                                        && (dragFromOutput || droppedPort.slotIndex == dragOriginSlot);
                if (!isSameOriginPort)
                {
                    droppedOnIncompatiblePort = true;
                    LastSfxWorldPosition = droppedPort.handleWorldPos;
                }
            }

            if (droppedOnIncompatiblePort)
            {
                OnWireRejectSfx?.Invoke();
            }
            else
            {
                LastSfxWorldPosition = hasLastValidPinchWorld
                    ? lastValidPinchWorldPos
                    : (dragOriginNode != null ? dragOriginNode.transform.position : Vector3.zero);
                OnWireDisconnectedSfx?.Invoke();
            }
        }

        CancelActiveDrag();
    }

    private bool EvaluateDragInvalidVisualState(
        bool isHandDetected,
        Vector2 cursorXY,
        float hitRadiusScale,
        CircuitManager circuitManager)
    {
        if (!isDraggingCable || !isHandDetected || (snappedTargetPortIndex >= 0 && snappedTargetPortIndex < activePorts.Count))
        {
            return false;
        }

        // 1. Jika kursor tepat mengarah ke port yang tidak kompatibel (polaritas sama / siklus / duplikat / node sama)
        int hoveredIdx = FindNearestPortIndex(cursorXY, hitRadiusScale);
        if (hoveredIdx >= 0 && hoveredIdx < activePorts.Count)
        {
            ActivePortEntry candidate = activePorts[hoveredIdx];
            bool isSameOriginPort = candidate.node == dragOriginNode
                                    && candidate.isOutput == dragFromOutput
                                    && (dragFromOutput || candidate.slotIndex == dragOriginSlot);
            if (!isSameOriginPort && !IsCompatibleDragTarget(candidate, circuitManager))
            {
                return true;
            }
        }

        // 2. Jika sedang mencabut kabel lama dan menariknya jauh ke zona bawah/luar kosong untuk membatalkan/memutus
        if (dragStartedByUnplug && cursorXY.y < -0.78f)
        {
            return true;
        }

        return false;
    }

    private static int ResolveCommitSlotForTarget(GateNode dstNode, int preferredSlot)
    {
        if (dstNode == null || dstNode.GetMaxInputCount() < 2)
        {
            return 0;
        }

        // Ikat persis ke soket yang dibidik (#0 = Input A, #1 = Input B) meskipun slot #0 masih kosong
        return preferredSlot == 1 ? 1 : 0;
    }

    private static Vector2 ProjectWorldToCameraRayPlane(Transform camT, Vector3 worldPos, float workbenchDepth)
    {
        Vector3 camLocal = camT.InverseTransformPoint(worldPos);
        float safeZ = Mathf.Max(0.35f, camLocal.z);
        float scale = workbenchDepth / safeZ;
        return new Vector2(camLocal.x * scale, camLocal.y * scale);
    }

    private static float ComputePortApparentScale(
        Transform camT,
        Vector3 handleWorld,
        float workbenchDepth,
        float stageZoomScale,
        out bool isInteractable,
        out float visualApparentScale)
    {
        float handleCamZ = camT.InverseTransformPoint(handleWorld).z;
        if (handleCamZ <= 0.25f)
        {
            isInteractable = false;
            visualApparentScale = 0.55f;
            return MinPortScreenSpaceHitboxScale;
        }
        isInteractable = true;
        float rawApparent = stageZoomScale * (workbenchDepth / handleCamZ);
        visualApparentScale = Mathf.Clamp(rawApparent, 0.18f, 6.0f);
        return Mathf.Clamp(rawApparent, MinPortScreenSpaceHitboxScale, 6.0f);
    }

    private void RegisterNodePorts(Transform camT, Transform stageRoot, GateNode node, float workbenchDepth, float stageZoomScale)
    {
        if (node == null || !node.isTracked || activePorts.Count >= MaxPortVisuals)
        {
            return;
        }

        Vector3 nodeWorldPos = node.transform.position;
        Vector2 nodeRayXY = ProjectWorldToCameraRayPlane(camT, nodeWorldPos, workbenchDepth);

        bool useStageSpace = stageRoot != null;
        Vector3 nodeStageLocal = useStageSpace
            ? stageRoot.InverseTransformPoint(nodeWorldPos)
            : camT.InverseTransformPoint(nodeWorldPos);

        // Sumbu keluar/masuk soket port 3D sejajar arah aliran ruang meja HologramStageRoot (+X untuk Port_Out, -X untuk Port_In)
        Vector3 stageRight = useStageSpace ? stageRoot.right : camT.right;

        if (node.nodeType != NodeType.OutputActuator && activePorts.Count < MaxPortVisuals)
        {
            float outOffX = node.nodeType == NodeType.InputSource ? InputPortOutOffsetX : GatePortOutOffsetX;
            Vector3 handleLocal3D = useStageSpace
                ? new Vector3(nodeStageLocal.x + outOffX, nodeStageLocal.y + 0.045f, nodeStageLocal.z + PortOutOffsetY)
                : new Vector3(nodeStageLocal.x + outOffX, nodeStageLocal.y + PortOutOffsetY, workbenchDepth - 0.04f);
            Vector3 handleWorld = useStageSpace
                ? stageRoot.TransformPoint(handleLocal3D)
                : camT.TransformPoint(handleLocal3D);
            Vector2 handleRayXY = ProjectWorldToCameraRayPlane(camT, handleWorld, workbenchDepth);
            float apparentOutScale = ComputePortApparentScale(camT, handleWorld, workbenchDepth, stageZoomScale, out bool outInteractable, out float visualOutScale);

            activePorts.Add(new ActivePortEntry
            {
                node = node,
                isOutput = true,
                slotIndex = 0,
                nodeLocalXY = nodeRayXY,
                handleLocalXY = handleRayXY,
                handleWorldPos = handleWorld,
                physicalWorldPos = node.GetOutputPortWorldPosition(),
                socketAxisWorld = stageRight,
                apparentScale = apparentOutScale,
                visualApparentScale = visualOutScale,
                isInteractable = outInteractable,
                isConnected = HasOutgoingWire(node),
                connectedSignalHigh = node.currentOutputValue
            });
        }

        int maxInputs = node.GetMaxInputCount();
        if (maxInputs >= 1 && activePorts.Count < MaxPortVisuals)
        {
            float offY = maxInputs == 2 ? PortInAOffsetY : PortInSingleOffsetY;
            Vector3 handleLocal3D = useStageSpace
                ? new Vector3(
                    nodeStageLocal.x + PortInOffsetX,
                    nodeStageLocal.y + (maxInputs == 2 ? 0.095f : 0.045f),
                    nodeStageLocal.z + (maxInputs == 2 ? 0.115f : PortInSingleOffsetY))
                : new Vector3(nodeStageLocal.x + PortInOffsetX, nodeStageLocal.y + offY, workbenchDepth - 0.04f);
            Vector3 handleWorld = useStageSpace
                ? stageRoot.TransformPoint(handleLocal3D)
                : camT.TransformPoint(handleLocal3D);
            Vector2 handleRayXY = ProjectWorldToCameraRayPlane(camT, handleWorld, workbenchDepth);
            float apparentIn0Scale = ComputePortApparentScale(camT, handleWorld, workbenchDepth, stageZoomScale, out bool in0Interactable, out float visualIn0Scale);
            int wireIdx0 = FindWireByTargetSlot(node, 0);
            bool conn0 = wireIdx0 >= 0;
            bool sig0 = conn0 && wires[wireIdx0].source != null && wires[wireIdx0].source.currentOutputValue;

            activePorts.Add(new ActivePortEntry
            {
                node = node,
                isOutput = false,
                slotIndex = 0,
                nodeLocalXY = nodeRayXY,
                handleLocalXY = handleRayXY,
                handleWorldPos = handleWorld,
                physicalWorldPos = node.GetInputPortWorldPosition(0),
                socketAxisWorld = -stageRight,
                apparentScale = apparentIn0Scale,
                visualApparentScale = visualIn0Scale,
                isInteractable = in0Interactable,
                isConnected = conn0,
                connectedSignalHigh = sig0
            });
        }

        if (maxInputs >= 2 && activePorts.Count < MaxPortVisuals)
        {
            Vector3 handleLocal3D = useStageSpace
                ? new Vector3(
                    nodeStageLocal.x + PortInOffsetX,
                    nodeStageLocal.y - 0.040f,
                    nodeStageLocal.z - 0.105f)
                : new Vector3(nodeStageLocal.x + PortInOffsetX, nodeStageLocal.y + PortInBOffsetY, workbenchDepth - 0.04f);
            Vector3 handleWorld = useStageSpace
                ? stageRoot.TransformPoint(handleLocal3D)
                : camT.TransformPoint(handleLocal3D);
            Vector2 handleRayXY = ProjectWorldToCameraRayPlane(camT, handleWorld, workbenchDepth);
            float apparentIn1Scale = ComputePortApparentScale(camT, handleWorld, workbenchDepth, stageZoomScale, out bool in1Interactable, out float visualIn1Scale);
            int wireIdx1 = FindWireByTargetSlot(node, 1);
            bool conn1 = wireIdx1 >= 0;
            bool sig1 = conn1 && wires[wireIdx1].source != null && wires[wireIdx1].source.currentOutputValue;

            activePorts.Add(new ActivePortEntry
            {
                node = node,
                isOutput = false,
                slotIndex = 1,
                nodeLocalXY = nodeRayXY,
                handleLocalXY = handleRayXY,
                handleWorldPos = handleWorld,
                physicalWorldPos = node.GetInputPortWorldPosition(1),
                socketAxisWorld = -stageRight,
                apparentScale = apparentIn1Scale,
                visualApparentScale = visualIn1Scale,
                isInteractable = in1Interactable,
                isConnected = conn1,
                connectedSignalHigh = sig1
            });
        }
    }

    private bool TryGetPortHandleWorldPos(GateNode node, bool isOutput, int slotIndex, out Vector3 worldPos)
    {
        for (int i = 0; i < activePorts.Count; i++)
        {
            ActivePortEntry p = activePorts[i];
            if (p.node == node && p.isOutput == isOutput && (isOutput || p.slotIndex == slotIndex))
            {
                worldPos = p.handleWorldPos;
                return true;
            }
        }

        if (node != null)
        {
            worldPos = isOutput ? node.GetOutputPortWorldPosition() : node.GetInputPortWorldPosition(slotIndex);
            return true;
        }

        worldPos = Vector3.zero;
        return false;
    }

    private bool TryGetPortHandlePose(
        GateNode node,
        bool isOutput,
        int slotIndex,
        Vector3 fallbackOutwardAxis,
        out Vector3 worldPos,
        out Vector3 outwardAxis)
    {
        for (int i = 0; i < activePorts.Count; i++)
        {
            ActivePortEntry p = activePorts[i];
            if (p.node == node && p.isOutput == isOutput && (isOutput || p.slotIndex == slotIndex))
            {
                worldPos = p.handleWorldPos;
                outwardAxis = p.socketAxisWorld.sqrMagnitude > 0.0001f ? p.socketAxisWorld : fallbackOutwardAxis;
                return true;
            }
        }

        if (node != null)
        {
            worldPos = isOutput ? node.GetOutputPortWorldPosition() : node.GetInputPortWorldPosition(slotIndex);
            outwardAxis = fallbackOutwardAxis;
            return true;
        }

        worldPos = Vector3.zero;
        outwardAxis = fallbackOutwardAxis;
        return false;
    }

    private void UpdateSoftMagneticAimOffset(
        Vector2 rawCursorXY,
        float cursorSpeedUnitsPerSec,
        bool canPull,
        CircuitManager circuitManager,
        float hitRadiusScale,
        float dt)
    {
        Vector2 desiredOffset = Vector2.zero;
        aimAssistTargetPortIndex = -1;
        aimAssistPullStrength = 0f;

        if (canPull && cursorSpeedUnitsPerSec < MagnetMaxHandSpeed && activePorts.Count > 0)
        {
            float slowFactor = Mathf.Clamp01(1f - (cursorSpeedUnitsPerSec / MagnetMaxHandSpeed));
            float baseRadius = isDraggingCable ? DragTargetMagnetRadius : HoverMagnetRadius;
            float bestNormDist = 1.0f;
            Vector2 bestTargetXY = Vector2.zero;
            int bestTargetIdx = -1;

            for (int i = 0; i < activePorts.Count; i++)
            {
                ActivePortEntry candidate = activePorts[i];
                if (!candidate.isInteractable)
                {
                    continue;
                }
                if (isDraggingCable && !IsCompatibleDragTarget(candidate, circuitManager))
                {
                    continue;
                }

                float portScale = candidate.apparentScale > 0.01f ? candidate.apparentScale : hitRadiusScale;
                float radius = baseRadius * portScale;
                float d = Vector2.Distance(rawCursorXY, candidate.handleLocalXY);
                if (radius > 0.0001f && d < radius)
                {
                    float normDist = d / radius;
                    if (normDist < bestNormDist)
                    {
                        bestNormDist = normDist;
                        bestTargetXY = candidate.handleLocalXY;
                        bestTargetIdx = i;
                    }
                }
            }

            if (bestTargetIdx >= 0)
            {
                float prox = 1f - bestNormDist;
                float pull = MagnetMaxPull * slowFactor * prox;
                desiredOffset = (bestTargetXY - rawCursorXY) * pull;
                aimAssistTargetPortIndex = bestTargetIdx;
                aimAssistPullStrength = Mathf.Clamp01(prox * slowFactor);
            }
        }

        float blendAlpha = 1f - Mathf.Exp(-MagnetOffsetBlendSpeed * Mathf.Max(0.0001f, dt));
        currentMagnetOffsetLocalXY = Vector2.Lerp(currentMagnetOffsetLocalXY, desiredOffset, blendAlpha);
    }

    private int FindActivePortIndex(GateNode node, bool isOutput, int slotIndex)
    {
        if (node == null)
        {
            return -1;
        }

        for (int i = 0; i < activePorts.Count; i++)
        {
            ActivePortEntry p = activePorts[i];
            if (p.node == node && p.isOutput == isOutput && (isOutput || p.slotIndex == slotIndex))
            {
                return i;
            }
        }
        return -1;
    }

    private int FindNearestPortIndex(Vector2 cursorXY, float hitRadiusScale)
    {
        int bestIdx = -1;
        float bestNormDist = 1.0001f;

        for (int i = 0; i < activePorts.Count; i++)
        {
            ActivePortEntry p = activePorts[i];
            if (!p.isInteractable)
            {
                continue;
            }
            float portScale = p.apparentScale > 0.01f ? p.apparentScale : hitRadiusScale;
            float radius = (p.isOutput ? PortOutHitRadius : PortInHitRadius) * portScale;
            float dist = Vector2.Distance(cursorXY, p.handleLocalXY);
            if (dist <= radius)
            {
                float normDist = dist / radius;
                if (normDist < bestNormDist)
                {
                    bestNormDist = normDist;
                    bestIdx = i;
                }
            }
        }

        return bestIdx;
    }

    private int FindNearestCompatibleTargetPort(Vector2 cursorXY, int currentLockedPortIdx, CircuitManager circuitManager, float hitRadiusScale)
    {
        bool hasValidLocked = false;
        float lockedEffDist = float.MaxValue;
        float lockedHandleDist = float.MaxValue;

        if (currentLockedPortIdx >= 0 && currentLockedPortIdx < activePorts.Count)
        {
            ActivePortEntry lockedEntry = activePorts[currentLockedPortIdx];
            if (lockedEntry.isInteractable && IsCompatibleDragTarget(lockedEntry, circuitManager))
            {
                float lockedScale = lockedEntry.apparentScale > 0.01f ? lockedEntry.apparentScale : hitRadiusScale;
                float lockedVisualScale = lockedEntry.visualApparentScale > 0.01f ? lockedEntry.visualApparentScale : lockedScale;
                float unlockRadius = SnapHysteresisUnlockRadius * lockedScale;
                lockedHandleDist = Vector2.Distance(cursorXY, lockedEntry.handleLocalXY);
                if (dragFromOutput)
                {
                    Vector2 lockedSmartCenter = new Vector2(lockedEntry.nodeLocalXY.x - (0.08f * lockedVisualScale), lockedEntry.nodeLocalXY.y + (0.02f * lockedVisualScale));
                    float lockedGateDist = Vector2.Distance(cursorXY, lockedSmartCenter);
                    lockedEffDist = Mathf.Min(lockedHandleDist, lockedGateDist);
                }
                else
                {
                    lockedEffDist = lockedHandleDist;
                }

                if (lockedEffDist <= unlockRadius)
                {
                    hasValidLocked = true;
                }
            }
        }

        int bestIdx = -1;
        float bestHandleDist = float.MaxValue;

        for (int i = 0; i < activePorts.Count; i++)
        {
            if (i == currentLockedPortIdx)
            {
                continue;
            }

            ActivePortEntry candidate = activePorts[i];
            if (!candidate.isInteractable || !IsCompatibleDragTarget(candidate, circuitManager))
            {
                continue;
            }

            float portScale = candidate.apparentScale > 0.01f ? candidate.apparentScale : hitRadiusScale;
            float portVisualScale = candidate.visualApparentScale > 0.01f ? candidate.visualApparentScale : portScale;
            float snapRadius = SnapMagnetRadius * portScale;
            float gateAutoRadius = GateAutoSnapRadius * portScale;

            if (dragFromOutput)
            {
                float distToHandle = Vector2.Distance(cursorXY, candidate.handleLocalXY);
                Vector2 gateSmartCenter = new Vector2(candidate.nodeLocalXY.x - (0.08f * portVisualScale), candidate.nodeLocalXY.y + (0.02f * portVisualScale));
                float distToGateZone = Vector2.Distance(cursorXY, gateSmartCenter);

                bool withinHandle = distToHandle <= snapRadius;
                bool withinGateZone = distToGateZone <= gateAutoRadius;
                if (!withinHandle && !withinGateZone)
                {
                    continue;
                }

                // Pilih kandidat strictly berdasarkan jarak langsung ke soket port (distToHandle) agar Port #1 vs Port #2
                // tidak pernah seri/terbias ke Port #1 akibat distToGateZone yang sama pada node 2-input.
                if (distToHandle < bestHandleDist)
                {
                    bestHandleDist = distToHandle;
                    bestIdx = i;
                }
            }
            else
            {
                float dist = Vector2.Distance(cursorXY, candidate.handleLocalXY);
                if (dist <= snapRadius && dist < bestHandleDist)
                {
                    bestHandleDist = dist;
                    bestIdx = i;
                }
            }
        }

        if (hasValidLocked)
        {
            if (bestIdx >= 0 && bestHandleDist < lockedHandleDist * SnapTargetSwitchRatio)
            {
                return bestIdx;
            }
            return currentLockedPortIdx;
        }

        return bestIdx;
    }

    private bool IsCompatibleDragTarget(ActivePortEntry candidate, CircuitManager circuitManager)
    {
        if (!isDraggingCable || dragOriginNode == null || candidate.node == null)
        {
            return false;
        }
        if (candidate.isOutput == dragFromOutput)
        {
            return false;
        }

        GateNode src = dragFromOutput ? dragOriginNode : candidate.node;
        GateNode dst = dragFromOutput ? candidate.node : dragOriginNode;
        if (HasExistingWireBetween(src, dst, -1))
        {
            return false;
        }
        return IsValidConnectionPair(src, dst, circuitManager);
    }

    private bool HasExistingWireBetween(GateNode source, GateNode target, int ignoredWireIndex)
    {
        if (source == null || target == null)
        {
            return false;
        }

        for (int i = 0; i < wires.Count; i++)
        {
            if (i == ignoredWireIndex)
            {
                continue;
            }
            if (wires[i].source == source && wires[i].target == target)
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsValidConnectionPair(GateNode src, GateNode dst, CircuitManager circuitManager)
    {
        if (src == null || dst == null || src == dst || !src.isTracked || !dst.isTracked)
        {
            return false;
        }
        if (src.nodeType == NodeType.OutputActuator || dst.nodeType == NodeType.InputSource)
        {
            return false;
        }
        if (circuitManager != null && circuitManager.WouldManualConnectionCreateCycle(src, dst))
        {
            return false;
        }

        return true;
    }

    private int FindWireByTargetSlot(GateNode targetNode, int slotIndex)
    {
        for (int i = 0; i < wires.Count; i++)
        {
            if (wires[i].target == targetNode && wires[i].targetSlotIndex == slotIndex)
            {
                return i;
            }
        }
        return -1;
    }

    private bool HasOutgoingWire(GateNode sourceNode)
    {
        for (int i = 0; i < wires.Count; i++)
        {
            if (wires[i].source == sourceNode)
            {
                return true;
            }
        }
        return false;
    }

    private void PruneUntrackedWires(CircuitManager circuitManager)
    {
        bool changed = false;
        for (int i = wires.Count - 1; i >= 0; i--)
        {
            GateNode src = wires[i].source;
            GateNode dst = wires[i].target;
            if (src == null || dst == null || !src.isTracked || !dst.isTracked)
            {
                wires.RemoveAt(i);
                changed = true;
            }
        }

        if (changed)
        {
            TopologyVersion++;
            if (circuitManager != null)
            {
                circuitManager.SetManualConnections(wires);
            }
        }
    }

    // =========================================================================
    // VISUAL RENDERING 3D PBR (SOKET PORT 3D, KABEL TABUNG 3D, & PAKET SINYAL)
    // =========================================================================

    /// <summary>
    /// Merender 20 Soket Port 3D (Torus Collar logam PBR + Inti Orb Energi 3D + Batang Penghubung 3D)
    /// terorientasi spasial mengikuti ruang 3D <c>HologramStageRoot</c>, plus Cincin Lock Halo 3D. Zero-GC.
    /// </summary>
    private void RenderPortSockets(
        Transform camT,
        Transform stageRoot,
        CircuitManager circuitManager,
        bool allowHoverHighlight,
        float stageZoomScale)
    {
        if (portSockets == null)
        {
            return;
        }

        Vector3 tableUp = stageRoot != null ? stageRoot.up : camT.up;
        float timeNow = Time.unscaledTime;
        float pulse = 0.5f + (0.5f * Mathf.Sin(timeNow * 7.2f));
        float socketVisualScale = ComputeSocketVisualScale(stageZoomScale);

        bool advanceSmoothThisCall = Time.frameCount != lastSocketAnimFrame;
        float smoothDt = advanceSmoothThisCall ? Mathf.Min(Time.unscaledDeltaTime, 0.05f) : 0f;
        if (advanceSmoothThisCall)
        {
            lastSocketAnimFrame = Time.frameCount;
        }
        float scaleBlendAlpha = advanceSmoothThisCall ? (1f - Mathf.Exp(-SocketScaleBlendSpeed * smoothDt)) : 0f;

        bool drewLockHalo = false;

        for (int i = 0; i < MaxPortVisuals; i++)
        {
            PortSocketVisual sock = portSockets[i];
            if (i >= activePorts.Count)
            {
                if (sock.collarRenderer != null && sock.collarRenderer.enabled) sock.collarRenderer.enabled = false;
                if (sock.orbRenderer != null && sock.orbRenderer.enabled) sock.orbRenderer.enabled = false;
                if (sock.stubRenderer != null && sock.stubRenderer.enabled) sock.stubRenderer.enabled = false;
                portSockets[i].smoothedRadiusFactor = 1.0f;
                continue;
            }

            ActivePortEntry p = activePorts[i];
            bool isHovered = allowHoverHighlight && !isDraggingCable && (i == hoveredPortIndex || i == secondaryHoveredPortIndex);
            bool isSnapped = allowHoverHighlight && isDraggingCable && i == snappedTargetPortIndex;
            bool isCompatible = allowHoverHighlight && isDraggingCable && IsCompatibleDragTarget(p, circuitManager);
            bool isAimAssisted = allowHoverHighlight && i == aimAssistTargetPortIndex && aimAssistPullStrength > 0.08f;

            Color collarAlbedo;
            Color collarEmission;
            Color orbAlbedo;
            Color orbEmission;
            Color socketRimLinear;
            float socketRimPower;
            float socketRimIntensity;
            float targetRadiusFactor = 1.00f;
            float orbRatio = 0.58f;

            if (isSnapped)
            {
                collarAlbedo = CollarGoldMetal;
                collarEmission = PortSnapLockOrbEmission * (0.95f + (0.35f * pulse));
                orbAlbedo = PortSnapLockOrbColor;
                orbEmission = PortSnapLockOrbEmission * (1.20f + (0.30f * pulse));
                socketRimLinear = PortSnapRimLinear;
                socketRimPower = 1.95f;
                socketRimIntensity = 1.55f + (0.25f * pulse);
                targetRadiusFactor = 1.50f + (0.14f * pulse);
                orbRatio = 0.68f;
            }
            else if (isHovered)
            {
                collarAlbedo = CollarCyanMetal;
                collarEmission = PortHoverOrbEmission * (0.85f + (0.30f * pulse));
                orbAlbedo = PortHoverOrbColor;
                orbEmission = PortHoverOrbEmission * (1.10f + (0.25f * pulse));
                socketRimLinear = PortHoverRimLinear;
                socketRimPower = 2.05f;
                socketRimIntensity = 1.35f + (0.20f * pulse);
                targetRadiusFactor = 1.40f + (0.12f * pulse);
                orbRatio = 0.65f;
            }
            else if (isCompatible)
            {
                if (!p.isOutput && !p.isConnected)
                {
                    collarAlbedo = CollarGreenMetal;
                    collarEmission = PortValidTargetOrbEmission * (0.60f + (0.30f * pulse));
                    orbAlbedo = PortValidTargetOrbColor;
                    orbEmission = PortValidTargetOrbEmission * (0.92f + (0.28f * pulse));
                    socketRimLinear = PortValidRimLinear;
                    socketRimPower = 2.15f;
                    socketRimIntensity = 1.15f + (0.20f * pulse);
                    targetRadiusFactor = 1.32f + (0.14f * pulse);
                    orbRatio = 0.64f;
                }
                else if (!p.isOutput && p.isConnected)
                {
                    collarAlbedo = CollarAmberMetal;
                    collarEmission = PortReplaceHintOrbEmission * (0.55f + (0.25f * pulse));
                    orbAlbedo = PortReplaceHintOrbColor;
                    orbEmission = PortReplaceHintOrbEmission * (0.88f + (0.22f * pulse));
                    socketRimLinear = PortReplaceRimLinear;
                    socketRimPower = 2.20f;
                    socketRimIntensity = 1.08f + (0.16f * pulse);
                    targetRadiusFactor = 1.15f + (0.10f * pulse);
                    orbRatio = 0.60f;
                }
                else
                {
                    collarAlbedo = CollarGreenMetal;
                    collarEmission = PortValidTargetOrbEmission * (0.55f + (0.25f * pulse));
                    orbAlbedo = PortValidTargetOrbColor;
                    orbEmission = PortValidTargetOrbEmission * (0.85f + (0.25f * pulse));
                    socketRimLinear = PortValidRimLinear;
                    socketRimPower = 2.20f;
                    socketRimIntensity = 1.10f + (0.18f * pulse);
                    targetRadiusFactor = 1.24f + (0.12f * pulse);
                    orbRatio = 0.62f;
                }
            }
            else if (p.isOutput)
            {
                if (p.connectedSignalHigh)
                {
                    collarAlbedo = CollarCyanMetal;
                    collarEmission = CableHighEmission * (0.48f + (0.12f * pulse));
                    orbAlbedo = CableHighAlbedo;
                    orbEmission = CableHighEmission * (0.95f + (0.18f * pulse));
                    socketRimLinear = CableHighRimLinear;
                    socketRimPower = 2.35f;
                    socketRimIntensity = 1.05f + (0.12f * pulse);
                    targetRadiusFactor = 1.08f;
                    orbRatio = 0.62f;
                }
                else
                {
                    collarAlbedo = CollarCopperDarkMetal;
                    collarEmission = CableLowEmission * 0.65f;
                    orbAlbedo = PortInConnectedLowOrbColor;
                    orbEmission = PortInConnectedLowOrbEmission;
                    socketRimLinear = CableLowRimLinear;
                    socketRimPower = 2.85f;
                    socketRimIntensity = 0.46f;
                    targetRadiusFactor = 0.98f;
                    orbRatio = 0.54f;
                }
            }
            else if (p.isConnected)
            {
                if (p.connectedSignalHigh)
                {
                    collarAlbedo = CollarCyanMetal;
                    collarEmission = PortInConnectedHighOrbEmission * (0.46f + (0.10f * pulse));
                    orbAlbedo = PortInConnectedHighOrbColor;
                    orbEmission = PortInConnectedHighOrbEmission * (0.95f + (0.16f * pulse));
                    socketRimLinear = CableHighRimLinear;
                    socketRimPower = 2.35f;
                    socketRimIntensity = 1.00f + (0.12f * pulse);
                    targetRadiusFactor = 1.06f;
                    orbRatio = 0.61f;
                }
                else
                {
                    collarAlbedo = CollarCopperDarkMetal;
                    collarEmission = CableLowEmission * 0.55f;
                    orbAlbedo = PortInConnectedLowOrbColor;
                    orbEmission = PortInConnectedLowOrbEmission;
                    socketRimLinear = CableLowRimLinear;
                    socketRimPower = 2.85f;
                    socketRimIntensity = 0.44f;
                    targetRadiusFactor = 0.96f;
                    orbRatio = 0.54f;
                }
            }
            else
            {
                // Port_In kosong siap colok
                collarAlbedo = CollarDarkMetal;
                collarEmission = PortInEmptyOrbEmission * 0.42f;
                orbAlbedo = PortInEmptyOrbColor;
                orbEmission = PortInEmptyOrbEmission * 0.85f;
                socketRimLinear = PortIdleRimLinear;
                socketRimPower = 2.60f;
                socketRimIntensity = 0.66f;
                targetRadiusFactor = 0.98f;
                orbRatio = 0.54f;
            }

            // Efek sentuhan aim-assist magnetik sebelum terkunci penuh: kollar mulai membesar halus & menyala lebih terang
            if (isAimAssisted && !isSnapped && !isHovered)
            {
                targetRadiusFactor += 0.20f * aimAssistPullStrength;
                collarEmission += PortHoverOrbEmission * (0.35f * aimAssistPullStrength);
                orbEmission += PortHoverOrbEmission * (0.30f * aimAssistPullStrength);
                socketRimLinear = Color.Lerp(socketRimLinear, PortHoverRimLinear, 0.55f * aimAssistPullStrength);
                socketRimIntensity += 0.35f * aimAssistPullStrength;
            }

            // Micro-Feedback Tahap 9 (Zero-GC): antisipasi soket port membesar ringan (~1.15x) saat ujung kabel Pinch mendekat
            if (isDraggingCable && isCompatible && !isSnapped && hasLastLiveDragCursorLocalXY)
            {
                float anticipationRadius = PortDragAnticipationRadius * Mathf.Max(MinPortScreenSpaceHitboxScale, p.apparentScale);
                float distToDragTip = Vector2.Distance(lastLiveDragCursorLocalXY, p.handleLocalXY);
                if (distToDragTip < anticipationRadius)
                {
                    float proxT = 1f - (distToDragTip / Mathf.Max(0.001f, anticipationRadius));
                    float smoothProx = proxT * proxT * (3f - (2f * proxT));
                    targetRadiusFactor *= 1f + (PortDragAnticipationScaleBoost * smoothProx);
                    socketRimIntensity += 0.22f * smoothProx;
                }
            }

            if (advanceSmoothThisCall)
            {
                sock.smoothedRadiusFactor = Mathf.Lerp(sock.smoothedRadiusFactor, targetRadiusFactor, scaleBlendAlpha);
                portSockets[i].smoothedRadiusFactor = sock.smoothedRadiusFactor;
            }

            float finalSocketRadius = SocketBaseRadius * sock.smoothedRadiusFactor * socketVisualScale;
            Vector3 socketAxis = p.socketAxisWorld.sqrMagnitude > 0.0001f ? p.socketAxisWorld.normalized : camT.right;
            Vector3 upHint = Mathf.Abs(Vector3.Dot(socketAxis, tableUp)) < 0.95f ? tableUp : camT.forward;
            Quaternion socketRot = Quaternion.LookRotation(socketAxis, upHint);

            // 1. 3D Torus Collar (logam PBR-lite reflektif dengan Fresnel rim reaktif)
            if (sock.collarTransform != null && sock.collarRenderer != null)
            {
                sock.collarTransform.position = p.handleWorldPos;
                sock.collarTransform.rotation = socketRot;
                sock.collarTransform.localScale = Vector3.one * finalSocketRadius;
                ApplyPbrPropertyBlock(sock.collarRenderer, collarAlbedo, collarEmission, metallic: 0.84f, glossiness: 0.90f, socketRimLinear, socketRimPower, socketRimIntensity);
                if (!sock.collarRenderer.enabled) sock.collarRenderer.enabled = true;
            }

            // 2. 3D Inner Energy Core Orb (bola inti bercahaya di tengah lubang torus)
            if (sock.orbTransform != null && sock.orbRenderer != null)
            {
                sock.orbTransform.position = p.handleWorldPos + (socketAxis * (finalSocketRadius * 0.08f));
                sock.orbTransform.rotation = socketRot;
                sock.orbTransform.localScale = Vector3.one * (finalSocketRadius * orbRatio);
                ApplyPbrPropertyBlock(sock.orbRenderer, orbAlbedo, orbEmission, metallic: 0.20f, glossiness: 0.94f, socketRimLinear, 1.85f, socketRimIntensity * 0.85f);
                if (!sock.orbRenderer.enabled) sock.orbRenderer.enabled = true;
            }

            // 3. 3D Cylindrical Conduit Stub (menghubungkan bodi node ke tepi kollar soket tanpa tembus orb)
            if (sock.stubTransform != null && sock.stubRenderer != null)
            {
                Vector3 stubDelta = p.handleWorldPos - p.physicalWorldPos;
                float fullStubLen = stubDelta.magnitude;
                float trimmedStubLen = fullStubLen - (finalSocketRadius * 0.42f);
                if (trimmedStubLen > 0.006f)
                {
                    Vector3 stubDir = stubDelta / fullStubLen;
                    Vector3 stubUp = Mathf.Abs(Vector3.Dot(stubDir, tableUp)) < 0.95f ? tableUp : camT.forward;
                    float stubRadius = PortStubBaseRadius * socketVisualScale;

                    sock.stubTransform.position = p.physicalWorldPos;
                    sock.stubTransform.rotation = Quaternion.LookRotation(stubDir, stubUp);
                    sock.stubTransform.localScale = new Vector3(stubRadius, stubRadius, trimmedStubLen);
                    ApplyPbrPropertyBlock(
                        sock.stubRenderer,
                        Color.Lerp(StubIdleAlbedo, collarAlbedo, 0.45f),
                        collarEmission * 0.28f,
                        metallic: 0.78f,
                        glossiness: 0.82f,
                        socketRimLinear,
                        2.75f,
                        socketRimIntensity * 0.48f);
                    if (!sock.stubRenderer.enabled) sock.stubRenderer.enabled = true;
                }
                else if (sock.stubRenderer.enabled)
                {
                    sock.stubRenderer.enabled = false;
                }
            }

            // 4. 3D Outer Lock Halo Ring (aktif saat Hover / Aim-Assist Sticky Snap Lock)
            if ((isHovered || isSnapped) && lockHaloRingTransform != null && lockHaloRingRenderer != null)
            {
                Color haloAlbedo = isSnapped ? PortSnapLockOrbColor : PortHoverOrbColor;
                Color haloEmission = isSnapped
                    ? PortSnapLockOrbEmission * (1.18f + (0.30f * pulse))
                    : PortHoverOrbEmission * (1.02f + (0.22f * pulse));
                Color haloRimLinear = isSnapped ? PortSnapRimLinear : PortHoverRimLinear;
                float haloRadius = finalSocketRadius * (isSnapped ? (1.62f + (0.10f * pulse)) : (1.52f + (0.08f * pulse)));
                float spinAngleDeg = timeNow * (isSnapped ? 110f : 55f);

                lockHaloRingTransform.position = p.handleWorldPos + (socketAxis * (finalSocketRadius * 0.14f));
                lockHaloRingTransform.rotation = socketRot * Quaternion.Euler(0f, 0f, spinAngleDeg);
                lockHaloRingTransform.localScale = Vector3.one * haloRadius;
                ApplyPbrPropertyBlock(lockHaloRingRenderer, haloAlbedo, haloEmission, metallic: 0.40f, glossiness: 0.92f, haloRimLinear, 1.80f, isSnapped ? 1.60f : 1.30f);
                if (!lockHaloRingRenderer.enabled) lockHaloRingRenderer.enabled = true;
                drewLockHalo = true;
            }
        }

        if (!drewLockHalo && lockHaloRingRenderer != null && lockHaloRingRenderer.enabled)
        {
            lockHaloRingRenderer.enabled = false;
        }
    }

    private static Vector3 StepCriticallyDampedSpring3D(Vector3 current, Vector3 target, ref Vector3 velocity, float omega, float dt)
    {
        if (dt <= 0.00001f)
        {
            return current;
        }

        Vector3 delta = current - target;
        Vector3 temp = (velocity + (delta * omega)) * dt;
        float exp = Mathf.Exp(-omega * dt);
        velocity = (velocity - (temp * omega)) * exp;
        return target + ((delta + temp) * exp);
    }

    /// <summary>
    /// Merender Kabel Tabung 3D PBR Live Drag (<c>Pinch</c>) dengan transisi warna dinamis:
    /// sian elektrik saat bebas, emas menyala saat mengunci target kompatibel, merah saat target invalid/batal. Zero-GC.
    /// </summary>
    private void RenderLiveDragTube(Transform camT, Transform stageRoot, float stageZoomScale, Vector3 pinchWorldPos)
    {
        if (liveDragTube == null || liveDragTube.meshRenderer == null)
        {
            return;
        }

        if (!isDraggingCable || dragOriginNode == null)
        {
            if (liveDragTube.meshRenderer.enabled)
            {
                liveDragTube.meshRenderer.enabled = false;
            }
            return;
        }

        Vector3 stageRight = stageRoot != null ? stageRoot.right : camT.right;
        Vector3 tableUp = stageRoot != null ? stageRoot.up : camT.up;
        Vector3 tableDepth = stageRoot != null ? stageRoot.forward : camT.forward;
        Vector3 fallbackOriginAxis = dragFromOutput ? stageRight : -stageRight;

        if (!TryGetPortHandlePose(dragOriginNode, dragFromOutput, dragOriginSlot, fallbackOriginAxis, out Vector3 startWorld, out Vector3 startOutDir))
        {
            liveDragTube.meshRenderer.enabled = false;
            return;
        }

        bool snapped = snappedTargetPortIndex >= 0 && snappedTargetPortIndex < activePorts.Count;
        Vector3 endWorld;
        if (snapped)
        {
            Vector3 targetHandle = activePorts[snappedTargetPortIndex].handleWorldPos;
            endWorld = (hasSmoothedLiveDragTip && Vector3.SqrMagnitude(pinchWorldPos - targetHandle) > 0.000225f)
                ? pinchWorldPos
                : targetHandle;
        }
        else
        {
            endWorld = pinchWorldPos;
        }

        float dist = Vector3.Distance(startWorld, endWorld);
        if (dist < 0.008f)
        {
            if (liveDragTube.meshRenderer.enabled)
            {
                liveDragTube.meshRenderer.enabled = false;
            }
            return;
        }

        float tubeVisualScale = ComputeTubeVisualScale(stageZoomScale);
        float tangentLen = Mathf.Clamp(dist * 0.28f, 0.055f * stageZoomScale, 0.42f * stageZoomScale);
        float archHeight = Mathf.Lerp(0.095f, 0.260f, Mathf.Clamp01((dist - (0.15f * stageZoomScale)) / Mathf.Max(0.10f, 2.40f * stageZoomScale))) * stageZoomScale;

        Vector3 c1 = startWorld + (startOutDir * tangentLen) + (tableUp * (archHeight * 0.90f));
        Vector3 c2;
        if (snapped)
        {
            Vector3 targetOutDir = activePorts[snappedTargetPortIndex].socketAxisWorld;
            if (targetOutDir.sqrMagnitude < 0.0001f)
            {
                targetOutDir = -startOutDir;
            }
            c2 = endWorld + (targetOutDir * tangentLen) + (tableUp * (archHeight * 0.90f));
        }
        else
        {
            c2 = Vector3.Lerp(startWorld, endWorld, 0.66f) + (tableUp * (archHeight * 0.85f)) - (tableDepth * (0.04f * stageZoomScale));
        }

        // Transisi warna & ketebalan tabung Live Drag halus (emas saat kunci kompatibel, merah saat invalid/batal, sian saat bebas)
        float timeNow = Time.unscaledTime;
        float dragPulse = 1f + (0.08f * Mathf.Sin(timeNow * 10.0f));
        Color targetAlbedo;
        Color targetEmission;
        Color targetRimLinear;
        float targetRimIntensity;
        float targetRadius;

        if (snapped)
        {
            targetAlbedo = DragSnappedAlbedo;
            targetEmission = DragSnappedEmission * dragPulse;
            targetRimLinear = DragSnappedRimLinear;
            targetRimIntensity = 1.45f * dragPulse;
            targetRadius = TubeRadiusDragSnapped * dragPulse;
        }
        else if (isHoveringInvalidTargetDuringDrag)
        {
            targetAlbedo = DragInvalidAlbedo;
            targetEmission = DragInvalidEmission * (0.95f + (0.20f * Mathf.Sin(timeNow * 15.0f)));
            targetRimLinear = DragInvalidRimLinear;
            targetRimIntensity = 1.35f * (0.95f + (0.18f * Mathf.Sin(timeNow * 15.0f)));
            targetRadius = TubeRadiusDragInvalid;
        }
        else
        {
            targetAlbedo = DragFreeAlbedo;
            targetEmission = DragFreeEmission;
            targetRimLinear = DragFreeRimLinear;
            targetRimIntensity = 1.05f;
            targetRadius = TubeRadiusDragFree;
        }

        bool advanceDragAnim = Time.frameCount != lastDragAnimFrame;
        if (advanceDragAnim)
        {
            lastDragAnimFrame = Time.frameCount;
            float animDt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float alpha = 1f - Mathf.Exp(-DragColorBlendSpeed * animDt);
            currentDragAlbedo = Color.Lerp(currentDragAlbedo, targetAlbedo, alpha);
            currentDragEmission = Color.Lerp(currentDragEmission, targetEmission, alpha);
            currentDragRimLinear = Color.Lerp(currentDragRimLinear, targetRimLinear, alpha);
            currentDragRimIntensity = Mathf.Lerp(currentDragRimIntensity, targetRimIntensity, alpha);
            currentDragRadius = Mathf.Lerp(currentDragRadius, targetRadius, alpha);
        }

        float finalDragRadius = currentDragRadius * tubeVisualScale;
        UpdateTubeMeshCubicBezier(liveDragTube, startWorld, c1, c2, endWorld, finalDragRadius, tableUp, tableDepth);
        ApplyPbrPropertyBlock(
            liveDragTube.meshRenderer,
            currentDragAlbedo,
            currentDragEmission,
            metallic: snapped ? 0.50f : 0.46f,
            glossiness: 0.88f,
            currentDragRimLinear,
            rimPower: snapped ? 2.00f : 2.25f,
            currentDragRimIntensity);

        if (!liveDragTube.meshRenderer.enabled)
        {
            liveDragTube.meshRenderer.enabled = true;
        }
    }

    /// <summary>
    /// Merender 100% seluruh kabel terpasang Mode Udara (primer &amp; fan-out) sebagai Kabel Tabung 3D PBR Bervolume
    /// melengkung di atas bidang meja XZ <c>HologramStageRoot</c>, plus 60 paket sinyal kapsul 3D sejajar tangen. Zero-GC.
    /// </summary>
    private void RenderAllUniformTubeCables(Transform camT, Transform stageRoot, float stageZoomScale)
    {
        if (installedTubePool == null)
        {
            return;
        }

        int beamCursor = 0;
        Vector3 stageRight = stageRoot != null ? stageRoot.right : camT.right;
        Vector3 tableUp = stageRoot != null ? stageRoot.up : camT.up;
        Vector3 tableDepth = stageRoot != null ? stageRoot.forward : camT.forward;
        float tubeVisualScale = ComputeTubeVisualScale(stageZoomScale);
        float timeNow = Time.unscaledTime;
        float highPulse = 1f + (0.07f * Mathf.Sin(timeNow * 8.0f));
        float highEmissionPulse = 1f + (0.14f * Mathf.Sin(timeNow * 8.0f));

        // Integrasikan fase paket sinyal tepat 1x per frame (method dipanggil di Tick & SyncVisualsAfterPoseUpdate)
        bool advancePhasesThisCall = Time.frameCount != lastPacketAdvanceFrame;
        float packetDt = advancePhasesThisCall ? Mathf.Min(Time.unscaledDeltaTime, 0.05f) : 0f;
        if (advancePhasesThisCall)
        {
            lastPacketAdvanceFrame = Time.frameCount;
        }

        for (int i = 0; i < wires.Count && beamCursor < MaxCableBeams; i++)
        {
            ManualConnectionEdge edge = wires[i];
            if (edge.source == null || edge.target == null || !edge.source.isTracked || !edge.target.isTracked)
            {
                continue;
            }

            int visualSlot = edge.targetSlotIndex == 1 && edge.target.GetMaxInputCount() >= 2 ? 1 : 0;
            if (!TryGetPortHandlePose(edge.source, true, 0, stageRight, out Vector3 p0, out Vector3 outAxis)
                || !TryGetPortHandlePose(edge.target, false, visualSlot, -stageRight, out Vector3 p3, out Vector3 inAxis))
            {
                continue;
            }

            float dist = Vector3.Distance(p0, p3);
            if (dist < 0.006f)
            {
                continue;
            }

            // Lengkungan 3D Kubik Bezier di atas bidang meja XZ (+tableUp) dengan pemisahan jalur kedalaman (laneWaveZ)
            // keluar mulus sejajar sumbu soket Port_Out dan masuk mulus sejajar sumbu soket Port_In (proporsional lapangan 2x & zoom 0.18x..15x)
            float spanT = Mathf.Clamp01((dist - (0.25f * stageZoomScale)) / Mathf.Max(0.10f, 2.60f * stageZoomScale));
            float archHeightY = Mathf.Lerp(0.095f, 0.285f, spanT) * stageZoomScale;
            float laneWaveZ = ((i % 3) - 1) * 0.048f * stageZoomScale;
            float tangentLen = Mathf.Clamp(dist * 0.28f, 0.065f * stageZoomScale, 0.45f * stageZoomScale);

            Vector3 c1 = p0 + (outAxis * tangentLen) + (tableUp * archHeightY) + (tableDepth * laneWaveZ);
            Vector3 c2 = p3 + (inAxis * tangentLen) + (tableUp * archHeightY) + (tableDepth * laneWaveZ);

            bool isHigh = edge.source.currentOutputValue;
            float tubeRadius = (isHigh ? (TubeRadiusHigh * highPulse) : TubeRadiusLow) * tubeVisualScale;

            TubeCableSlot slot = installedTubePool[beamCursor];
            UpdateTubeMeshCubicBezier(slot, p0, c1, c2, p3, tubeRadius, tableUp, tableDepth);

            if (isHigh)
            {
                ApplyPbrPropertyBlock(
                    slot.meshRenderer,
                    CableHighAlbedo,
                    CableHighEmission * highEmissionPulse,
                    metallic: 0.44f,
                    glossiness: 0.88f,
                    CableHighRimLinear,
                    rimPower: 2.35f,
                    rimIntensity: 1.15f * highEmissionPulse);
            }
            else
            {
                ApplyPbrPropertyBlock(
                    slot.meshRenderer,
                    CableLowAlbedo,
                    CableLowEmission,
                    metallic: 0.78f,
                    glossiness: 0.80f,
                    CableLowRimLinear,
                    rimPower: 3.00f,
                    rimIntensity: 0.42f);
            }

            if (!slot.meshRenderer.enabled)
            {
                slot.meshRenderer.enabled = true;
            }

            if (cablePacketPhases != null)
            {
                if (advancePhasesThisCall)
                {
                    float speed = isHigh ? PacketHighSpeed : PacketLowSpeed;
                    cablePacketPhases[beamCursor] = Mathf.Repeat(cablePacketPhases[beamCursor] + (packetDt * speed), 1f);
                }
                int activePackets = dist >= (PacketLongCableThreshold * stageZoomScale) ? 3 : 2;
                RenderCableSignalPackets3D(
                    beamCursor,
                    p0,
                    c1,
                    c2,
                    p3,
                    tableUp,
                    tubeRadius,
                    tubeVisualScale,
                    isHigh,
                    activePackets,
                    cablePacketPhases[beamCursor],
                    timeNow);
            }

            beamCursor++;
        }

        DisableCableTubesAndPacketsFrom(beamCursor);
    }

    /// <summary>
    /// Merender kapsul energi 3D (pool 60, Zero-GC) yang meluncur menyusuri pusat tabung Bezier 3D
    /// berorientasi tepat sejajar arah tangen kurva (<c>Quaternion.LookRotation(tangent)</c>), sedikit lebih lebar dari tabung.
    /// </summary>
    private void RenderCableSignalPackets3D(
        int beamIndex,
        Vector3 p0,
        Vector3 c1,
        Vector3 c2,
        Vector3 p3,
        Vector3 tableUp,
        float tubeRadius,
        float visualScale,
        bool isHigh,
        int activePacketCount,
        float basePhase,
        float timeNow)
    {
        if (packetTransforms == null || packetRenderers == null || sharedMpb == null)
        {
            return;
        }

        int poolBase = beamIndex * PacketsPerCable;
        int clampedCount = Mathf.Clamp(activePacketCount, 1, PacketsPerCable);
        Color baseAlbedo = isHigh ? PacketHighAlbedo : PacketLowAlbedo;
        Color baseEmission = isHigh ? PacketHighEmission : PacketLowEmission;
        float pktRadius = tubeRadius * (isHigh ? PacketHighRadiusScale : PacketLowRadiusScale);
        float pktHalfLen = (isHigh ? PacketHighHalfLength : PacketLowHalfLength) * visualScale;
        float pulseFreq = isHigh ? 9.5f : 3.8f;
        float pulseAmp = isHigh ? 0.14f : 0.06f;

        for (int p = 0; p < PacketsPerCable; p++)
        {
            int slot = poolBase + p;
            if (slot < 0 || slot >= TotalPacketPoolSize)
            {
                continue;
            }

            MeshRenderer rend = packetRenderers[slot];
            Transform tr = packetTransforms[slot];
            if (rend == null || tr == null)
            {
                continue;
            }

            if (p >= clampedCount)
            {
                if (rend.enabled)
                {
                    rend.enabled = false;
                }
                continue;
            }

            float t = Mathf.Repeat(basePhase + (p / (float)clampedCount), 1f);
            EvaluateCubicBezier(p0, c1, c2, p3, t, out Vector3 pos, out Vector3 tangent);

            float edgeFade = Mathf.Clamp01(t / PacketEdgeFadeMargin) * Mathf.Clamp01((1f - t) / PacketEdgeFadeMargin);
            float smoothEdge = edgeFade * edgeFade * (3f - (2f * edgeFade));
            float pulse = 1f + (pulseAmp * Mathf.Sin((timeNow * pulseFreq) + (p * 2.094395f) + (beamIndex * 0.7f)));
            float radialScale = pktRadius * Mathf.Lerp(0.18f, 1.0f, smoothEdge) * pulse;
            float lengthScale = pktHalfLen * Mathf.Lerp(0.25f, 1.0f, smoothEdge) * pulse;

            tr.position = pos;
            if (tangent.sqrMagnitude > 0.000001f)
            {
                Vector3 fwd = tangent.normalized;
                Vector3 upRef = Mathf.Abs(Vector3.Dot(fwd, tableUp)) < 0.96f ? tableUp : Vector3.right;
                tr.rotation = Quaternion.LookRotation(fwd, upRef);
            }
            tr.localScale = new Vector3(radialScale, radialScale, lengthScale);

            ApplyPbrPropertyBlock(
                rend,
                baseAlbedo,
                baseEmission * (0.25f + (0.95f * smoothEdge)),
                metallic: 0.16f,
                glossiness: 0.95f,
                isHigh ? PacketHighRimLinear : PacketLowRimLinear,
                rimPower: 1.85f,
                rimIntensity: (isHigh ? 1.25f : 0.65f) * (0.35f + (0.65f * smoothEdge)));

            if (!rend.enabled)
            {
                rend.enabled = true;
            }
        }
    }

    // =========================================================================
    // PROCEDURAL 3D TUBE GENERATOR (PARALLEL TRANSPORT / BISHOP FRAME, ZERO-GC)
    // =========================================================================

    private static void EvaluateCubicBezier(
        Vector3 p0,
        Vector3 c1,
        Vector3 c2,
        Vector3 p3,
        float t,
        out Vector3 point,
        out Vector3 tangent)
    {
        float u = 1f - t;
        float uu = u * u;
        float tt = t * t;

        point = (uu * u * p0)
                + (3f * uu * t * c1)
                + (3f * u * tt * c2)
                + (tt * t * p3);

        tangent = (3f * uu * (c1 - p0))
                  + (6f * u * t * (c2 - c1))
                  + (3f * tt * (p3 - c2));
    }

    /// <summary>
    /// Memperbarui vertex &amp; normal mesh tabung 3D melengkung mengikuti kurva Bezier 3D memakai
    /// Parallel Transport Frame (Bishop Frame) agar bebas puntiran/gepeng pada orbit 360° penuh. 100% Zero-GC.
    /// </summary>
    private void UpdateTubeMeshCubicBezier(
        TubeCableSlot slot,
        Vector3 p0,
        Vector3 c1,
        Vector3 c2,
        Vector3 p3,
        float radius,
        Vector3 primaryRefUp,
        Vector3 fallbackRefAxis)
    {
        if (slot == null || slot.mesh == null)
        {
            return;
        }

        // Lewati upload GPU jika kurva & radius identik (misal panggilan ganda Tick + SyncVisualsAfterPoseUpdate tanpa gerak)
        if (slot.hasGeometry
            && (p0 - slot.lastP0).sqrMagnitude < 1e-10f
            && (c1 - slot.lastC1).sqrMagnitude < 1e-10f
            && (c2 - slot.lastC2).sqrMagnitude < 1e-10f
            && (p3 - slot.lastP3).sqrMagnitude < 1e-10f
            && (primaryRefUp - slot.lastRefUp).sqrMagnitude < 1e-8f
            && Mathf.Abs(radius - slot.lastRadius) < 1e-6f)
        {
            return;
        }

        Vector3 chordDir = p3 - p0;
        Vector3 safeFallbackDir = chordDir.sqrMagnitude > 1e-8f ? chordDir.normalized : Vector3.right;

        // 1. Sample titik & tangen unit sepanjang kurva Kubik Bezier
        for (int s = 0; s < TubeRingCount; s++)
        {
            float t = s / (float)TubeLengthSegments;
            EvaluateCubicBezier(p0, c1, c2, p3, t, out Vector3 pt, out Vector3 tan);
            curvePointsBuffer[s] = pt;
            if (tan.sqrMagnitude > 1e-8f)
            {
                curveTangentsBuffer[s] = tan.normalized;
            }
            else
            {
                curveTangentsBuffer[s] = s > 0 ? curveTangentsBuffer[s - 1] : safeFallbackDir;
            }
        }

        // 2. Inisialisasi Bishop Frame awal (T0, N0, B0) berbasis sumbu atas meja HologramStageRoot (bukan kamera)
        Vector3 prevT = curveTangentsBuffer[0];
        Vector3 refUp = primaryRefUp.sqrMagnitude > 1e-6f ? primaryRefUp.normalized : Vector3.up;
        if (Mathf.Abs(Vector3.Dot(prevT, refUp)) > 0.94f)
        {
            refUp = fallbackRefAxis.sqrMagnitude > 1e-6f ? fallbackRefAxis.normalized : Vector3.forward;
            if (Mathf.Abs(Vector3.Dot(prevT, refUp)) > 0.94f)
            {
                refUp = Vector3.right;
            }
        }

        Vector3 currN = Vector3.Cross(refUp, prevT).normalized;
        Vector3 currB = Vector3.Cross(prevT, currN);

        Vector3[] verts = slot.vertices;
        Vector3[] norms = slot.normals;

        Vector3 minBounds = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 maxBounds = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        // 3. Propagasi Parallel Transport Frame (rotasi minimum Rodrigues) dari ring 0 hingga ring akhir
        for (int s = 0; s < TubeRingCount; s++)
        {
            Vector3 currT = curveTangentsBuffer[s];
            if (s > 0)
            {
                Vector3 rotAxis = Vector3.Cross(prevT, currT);
                float sinTheta = rotAxis.magnitude;
                if (sinTheta > 1e-5f)
                {
                    float cosTheta = Mathf.Clamp(Vector3.Dot(prevT, currT), -1f, 1f);
                    Vector3 axis = rotAxis / sinTheta;
                    Vector3 rotatedN = (currN * cosTheta)
                                       + (Vector3.Cross(axis, currN) * sinTheta)
                                       + (axis * (Vector3.Dot(axis, currN) * (1f - cosTheta)));
                    Vector3 orthoN = rotatedN - (currT * Vector3.Dot(rotatedN, currT));
                    if (orthoN.sqrMagnitude > 1e-8f)
                    {
                        currN = orthoN.normalized;
                    }
                }
                else
                {
                    Vector3 orthoN = currN - (currT * Vector3.Dot(currN, currT));
                    if (orthoN.sqrMagnitude > 1e-8f)
                    {
                        currN = orthoN.normalized;
                    }
                }

                currB = Vector3.Cross(currT, currN);
                prevT = currT;
            }

            Vector3 ringCenter = curvePointsBuffer[s];
            int rowBase = s * TubeRingVertexCount;

            for (int j = 0; j < TubeRingVertexCount; j++)
            {
                Vector3 radial = (currN * tubeCosTable[j]) + (currB * tubeSinTable[j]);
                Vector3 pos = ringCenter + (radial * radius);
                int idx = rowBase + j;
                verts[idx] = pos;
                norms[idx] = radial;

                if (pos.x < minBounds.x) minBounds.x = pos.x;
                if (pos.y < minBounds.y) minBounds.y = pos.y;
                if (pos.z < minBounds.z) minBounds.z = pos.z;
                if (pos.x > maxBounds.x) maxBounds.x = pos.x;
                if (pos.y > maxBounds.y) maxBounds.y = pos.y;
                if (pos.z > maxBounds.z) maxBounds.z = pos.z;
            }
        }

        // 4. Dua titik tutup ujung tabung (start cap & end cap agar penampang kabel tidak bolong saat ujung live drag terlihat)
        int startCapIdx = TubeSideVertexCount;
        int endCapIdx = TubeSideVertexCount + 1;
        verts[startCapIdx] = curvePointsBuffer[0];
        norms[startCapIdx] = -curveTangentsBuffer[0];
        verts[endCapIdx] = curvePointsBuffer[TubeLengthSegments];
        norms[endCapIdx] = curveTangentsBuffer[TubeLengthSegments];

        // 5. Upload ke Mesh dinamis tanpa alokasi heap (Zero-GC)
        slot.mesh.SetVertices(verts);
        slot.mesh.SetNormals(norms);
        Vector3 boundsCenter = (minBounds + maxBounds) * 0.5f;
        float boundsPad = Mathf.Max(0.04f, radius * 2.5f);
        Vector3 boundsSize = (maxBounds - minBounds) + new Vector3(boundsPad, boundsPad, boundsPad);
        slot.mesh.bounds = new Bounds(boundsCenter, boundsSize);

        slot.lastP0 = p0;
        slot.lastC1 = c1;
        slot.lastC2 = c2;
        slot.lastP3 = p3;
        slot.lastRefUp = primaryRefUp;
        slot.lastRadius = radius;
        slot.hasGeometry = true;
    }

    private TubeCableSlot CreateDynamicTubeSlot(string objName)
    {
        GameObject obj = new GameObject(objName);
        obj.transform.SetParent(rootObj.transform, false);

        MeshFilter mf = obj.AddComponent<MeshFilter>();
        MeshRenderer mr = obj.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        if (pbrSharedMat != null)
        {
            mr.sharedMaterial = pbrSharedMat;
        }
        mr.enabled = false;

        Mesh mesh = new Mesh { name = $"{objName}_Mesh" };
        mesh.MarkDynamic();
        ownedMeshes.Add(mesh);

        Vector3[] verts = new Vector3[TubeVertexCount];
        Vector3[] norms = new Vector3[TubeVertexCount];
        Vector2[] uvs = new Vector2[TubeVertexCount];
        int[] tris = new int[TubeIndexCount];

        // Inisialisasi UV & topologi segitiga permanen 1x (tidak pernah berubah di Update())
        for (int s = 0; s < TubeRingCount; s++)
        {
            float v = s / (float)TubeLengthSegments;
            int rowBase = s * TubeRingVertexCount;
            for (int j = 0; j < TubeRingVertexCount; j++)
            {
                float u = j / (float)TubeRadialSegments;
                int idx = rowBase + j;
                verts[idx] = Vector3.zero;
                norms[idx] = Vector3.up;
                uvs[idx] = new Vector2(u, v);
            }
        }

        int startCapIdx = TubeSideVertexCount;
        int endCapIdx = TubeSideVertexCount + 1;
        verts[startCapIdx] = Vector3.zero;
        norms[startCapIdx] = Vector3.back;
        uvs[startCapIdx] = new Vector2(0.5f, 0f);
        verts[endCapIdx] = Vector3.zero;
        norms[endCapIdx] = Vector3.forward;
        uvs[endCapIdx] = new Vector2(0.5f, 1f);

        int ti = 0;
        for (int s = 0; s < TubeLengthSegments; s++)
        {
            int currRow = s * TubeRingVertexCount;
            int nextRow = (s + 1) * TubeRingVertexCount;
            for (int j = 0; j < TubeRadialSegments; j++)
            {
                int v00 = currRow + j;
                int v01 = currRow + j + 1;
                int v10 = nextRow + j;
                int v11 = nextRow + j + 1;

                // Winding outward (Unity Left-Handed, B = Cross(T, N)): Cross(v01-v00, v10-v00) = +N
                tris[ti++] = v00;
                tris[ti++] = v01;
                tris[ti++] = v10;

                tris[ti++] = v01;
                tris[ti++] = v11;
                tris[ti++] = v10;
            }
        }

        // Tutup pangkal (s = 0, normal -T0)
        for (int j = 0; j < TubeRadialSegments; j++)
        {
            tris[ti++] = startCapIdx;
            tris[ti++] = j + 1;
            tris[ti++] = j;
        }

        // Tutup ujung (s = TubeLengthSegments, normal +T_end)
        int lastRow = TubeLengthSegments * TubeRingVertexCount;
        for (int j = 0; j < TubeRadialSegments; j++)
        {
            tris[ti++] = endCapIdx;
            tris[ti++] = lastRow + j;
            tris[ti++] = lastRow + j + 1;
        }

        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetIndices(tris, MeshTopology.Triangles, 0, calculateBounds: false);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
        mf.sharedMesh = mesh;

        return new TubeCableSlot
        {
            meshFilter = mf,
            meshRenderer = mr,
            mesh = mesh,
            vertices = verts,
            normals = norms,
            uvs = uvs,
            triangles = tris,
            hasGeometry = false
        };
    }

    // =========================================================================
    // PROCEDURAL STATIC 3D MESH BUILDERS (TORUS COLLAR, ORB, STUB, CAPSULE)
    // =========================================================================

    private Mesh CreateAndRegisterMesh(string meshName)
    {
        Mesh mesh = new Mesh { name = meshName };
        ownedMeshes.Add(mesh);
        return mesh;
    }

    private Mesh BuildTorusMesh(
        string meshName,
        int majorSegments,
        int minorSegments,
        float majorRadius,
        float minorRadiusXY,
        float minorRadiusZ)
    {
        Mesh mesh = CreateAndRegisterMesh(meshName);
        int majorCount = majorSegments + 1;
        int minorCount = minorSegments + 1;
        int vertCount = majorCount * minorCount;
        int triCount = majorSegments * minorSegments * 6;

        Vector3[] verts = new Vector3[vertCount];
        Vector3[] norms = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        int[] tris = new int[triCount];

        int vi = 0;
        for (int i = 0; i < majorCount; i++)
        {
            float uNorm = i / (float)majorSegments;
            float uAngle = (i % majorSegments) * ((Mathf.PI * 2f) / majorSegments);
            float cosU = Mathf.Cos(uAngle);
            float sinU = Mathf.Sin(uAngle);
            Vector3 radialDir = new Vector3(cosU, sinU, 0f);
            Vector3 ringCenter = radialDir * majorRadius;

            for (int j = 0; j < minorCount; j++)
            {
                float vNorm = j / (float)minorSegments;
                float vAngle = (j % minorSegments) * ((Mathf.PI * 2f) / minorSegments);
                float cosV = Mathf.Cos(vAngle);
                float sinV = Mathf.Sin(vAngle);

                verts[vi] = ringCenter + (radialDir * (minorRadiusXY * cosV)) + new Vector3(0f, 0f, minorRadiusZ * sinV);
                norms[vi] = ((radialDir * (cosV / minorRadiusXY)) + new Vector3(0f, 0f, sinV / minorRadiusZ)).normalized;
                uvs[vi] = new Vector2(uNorm, vNorm);
                vi++;
            }
        }

        int ti = 0;
        for (int i = 0; i < majorSegments; i++)
        {
            int rowA = i * minorCount;
            int rowB = (i + 1) * minorCount;
            for (int j = 0; j < minorSegments; j++)
            {
                int v00 = rowA + j;
                int v10 = rowB + j;
                int v01 = rowA + j + 1;
                int v11 = rowB + j + 1;

                // Winding outward: Cross(v10-v00, v01-v00) = Cross(+dU, +dV) = +N
                tris[ti++] = v00;
                tris[ti++] = v10;
                tris[ti++] = v01;

                tris[ti++] = v10;
                tris[ti++] = v11;
                tris[ti++] = v01;
            }
        }

        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetIndices(tris, MeshTopology.Triangles, 0, calculateBounds: true);
        return mesh;
    }

    private Mesh BuildUvSphereMesh(string meshName, int lonSegments, int latSegments, float radius)
    {
        Mesh mesh = CreateAndRegisterMesh(meshName);
        int lonCount = lonSegments + 1;
        int latCount = latSegments + 1;
        int vertCount = lonCount * latCount;
        int triCount = lonSegments * latSegments * 6;

        Vector3[] verts = new Vector3[vertCount];
        Vector3[] norms = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        int[] tris = new int[triCount];

        int vi = 0;
        for (int lat = 0; lat < latCount; lat++)
        {
            float vNorm = lat / (float)latSegments;
            float phi = vNorm * Mathf.PI;
            float sinPhi = Mathf.Sin(phi);
            float cosPhi = Mathf.Cos(phi);

            for (int lon = 0; lon < lonCount; lon++)
            {
                float uNorm = lon / (float)lonSegments;
                float theta = (lon % lonSegments) * ((Mathf.PI * 2f) / lonSegments);
                Vector3 n = new Vector3(sinPhi * Mathf.Cos(theta), cosPhi, sinPhi * Mathf.Sin(theta));
                verts[vi] = n * radius;
                norms[vi] = n;
                uvs[vi] = new Vector2(uNorm, vNorm);
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

                // Winding outward: Cross(v01-v00, v10-v00) = Cross(+theta, +phi) = +N
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
        mesh.SetUVs(0, uvs);
        mesh.SetIndices(tris, MeshTopology.Triangles, 0, calculateBounds: true);
        return mesh;
    }

    private Mesh BuildUnitZStubCylinderMesh(string meshName, int radialSegments)
    {
        Mesh mesh = CreateAndRegisterMesh(meshName);
        int ringVertCount = radialSegments + 1;
        Vector3[] verts = new Vector3[ringVertCount * 2];
        Vector3[] norms = new Vector3[ringVertCount * 2];
        Vector2[] uvs = new Vector2[ringVertCount * 2];
        int[] tris = new int[radialSegments * 6];

        for (int j = 0; j < ringVertCount; j++)
        {
            float uNorm = j / (float)radialSegments;
            float angle = (j % radialSegments) * ((Mathf.PI * 2f) / radialSegments);
            float cosA = Mathf.Cos(angle);
            float sinA = Mathf.Sin(angle);
            Vector3 radial = new Vector3(cosA, sinA, 0f);

            verts[j] = radial;
            norms[j] = radial;
            uvs[j] = new Vector2(uNorm, 0f);

            verts[ringVertCount + j] = new Vector3(cosA, sinA, 1f);
            norms[ringVertCount + j] = radial;
            uvs[ringVertCount + j] = new Vector2(uNorm, 1f);
        }

        int ti = 0;
        for (int j = 0; j < radialSegments; j++)
        {
            int v00 = j;
            int v01 = j + 1;
            int v10 = ringVertCount + j;
            int v11 = ringVertCount + j + 1;

            // Winding outward: Cross(v01-v00, v10-v00) = Cross(+Y, +Z) = +X
            tris[ti++] = v00;
            tris[ti++] = v01;
            tris[ti++] = v10;

            tris[ti++] = v01;
            tris[ti++] = v11;
            tris[ti++] = v10;
        }

        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetIndices(tris, MeshTopology.Triangles, 0, calculateBounds: true);
        return mesh;
    }

    /// <summary>
    /// Membangun kapsul energi 3D ramping berorientasi sumbu +Z (radius XY = 1, setengah panjang Z = 1)
    /// sehingga <c>Quaternion.LookRotation(tangent)</c> langsung menyejajarkan kapsul menyusuri tabung 3D.
    /// </summary>
    private Mesh BuildZAlignedPacketCapsuleMesh(string meshName, int lonSegments, int latSegments)
    {
        Mesh mesh = CreateAndRegisterMesh(meshName);
        int lonCount = lonSegments + 1;
        int latCount = latSegments + 1;
        int vertCount = lonCount * latCount;
        int triCount = lonSegments * latSegments * 6;

        Vector3[] verts = new Vector3[vertCount];
        Vector3[] norms = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        int[] tris = new int[triCount];

        const float ProfilePower = 0.72f;
        int vi = 0;
        for (int lat = 0; lat < latCount; lat++)
        {
            float vNorm = lat / (float)latSegments;
            float phi = vNorm * Mathf.PI; // 0 = hidung (+Z), PI = ekor (-Z)
            float sinPhi = Mathf.Sin(phi);
            float cosPhi = Mathf.Cos(phi);
            // Profil kapsul ramping sedikit silindris di tengah agar tampak seperti pil/pulsa energi 3D
            float radialProfile = sinPhi > 0.0001f ? Mathf.Pow(sinPhi, ProfilePower) : 0f;
            float normRadial = sinPhi > 0.0001f ? Mathf.Pow(sinPhi, 2f - ProfilePower) : 0f;
            float normZ = ProfilePower * cosPhi;

            for (int lon = 0; lon < lonCount; lon++)
            {
                float uNorm = lon / (float)lonSegments;
                float theta = (lon % lonSegments) * ((Mathf.PI * 2f) / lonSegments);
                float cosT = Mathf.Cos(theta);
                float sinT = Mathf.Sin(theta);
                verts[vi] = new Vector3(radialProfile * cosT, radialProfile * sinT, cosPhi);
                norms[vi] = new Vector3(normRadial * cosT, normRadial * sinT, normZ).normalized;
                uvs[vi] = new Vector2(uNorm, vNorm);
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

                // Winding outward: Cross(v10-v00, v01-v00) = Cross(-Z, +Y) = +X
                tris[ti++] = v00;
                tris[ti++] = v10;
                tris[ti++] = v01;

                tris[ti++] = v10;
                tris[ti++] = v11;
                tris[ti++] = v01;
            }
        }

        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetIndices(tris, MeshTopology.Triangles, 0, calculateBounds: true);
        return mesh;
    }

    private MeshRenderer CreateStaticMeshObject(string objName, Mesh sharedMesh, out Transform objTransform)
    {
        GameObject obj = new GameObject(objName);
        obj.transform.SetParent(rootObj.transform, false);
        objTransform = obj.transform;

        MeshFilter mf = obj.AddComponent<MeshFilter>();
        mf.sharedMesh = sharedMesh;

        MeshRenderer mr = obj.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        if (pbrSharedMat != null)
        {
            mr.sharedMaterial = pbrSharedMat;
        }
        mr.enabled = false;
        return mr;
    }

    private void ApplyPbrPropertyBlock(
        MeshRenderer renderer,
        Color albedo,
        Color emission,
        float metallic,
        float glossiness,
        Color rimColorLinear,
        float rimPower,
        float rimIntensity)
    {
        if (renderer == null || sharedMpb == null)
        {
            return;
        }

        sharedMpb.Clear();
        sharedMpb.SetColor(ColorPropId, albedo);
        if (pbrHasEmissionProp)
        {
            sharedMpb.SetColor(EmissionColorPropId, emission);
        }
        if (pbrHasMetallicProp)
        {
            sharedMpb.SetFloat(MetallicPropId, metallic);
        }
        if (pbrHasGlossinessProp)
        {
            sharedMpb.SetFloat(GlossinessPropId, glossiness);
        }
        if (pbrHasRimProps)
        {
            sharedMpb.SetColor(RimColorPropId, rimColorLinear);
            sharedMpb.SetFloat(RimPowerPropId, rimPower);
            sharedMpb.SetFloat(RimIntensityPropId, rimIntensity);
        }
        renderer.SetPropertyBlock(sharedMpb);
    }

    private Material CreatePbrStandardMaterial(Color baseColor, float metallic, float glossiness)
    {
        bool usesCustomHardwareShader = false;
        Shader shader = AirWeavingLightingAndShaderRig.ResolveHardwareFresnelShader();
        if (shader != null && shader.isSupported)
        {
            usesCustomHardwareShader = true;
        }
        else
        {
            shader = Shader.Find("Standard");
        }
        if (shader == null || !shader.isSupported)
        {
            shader = Shader.Find("Legacy Shaders/Diffuse");
        }
        if (shader == null || !shader.isSupported)
        {
            shader = Shader.Find("Sprites/Default");
        }
        if (shader == null || !shader.isSupported)
        {
            shader = Shader.Find("UI/Default");
        }
        if (shader == null)
        {
            return null;
        }

        Material mat = new Material(shader);
        if (mat.HasProperty(ColorPropId))
        {
            mat.SetColor(ColorPropId, baseColor);
        }
        else
        {
            mat.color = baseColor;
        }

        if (mat.HasProperty(MetallicPropId))
        {
            mat.SetFloat(MetallicPropId, metallic);
        }
        if (mat.HasProperty(GlossinessPropId))
        {
            mat.SetFloat(GlossinessPropId, glossiness);
        }
        if (mat.HasProperty(EmissionColorPropId))
        {
            if (usesCustomHardwareShader)
            {
                mat.SetColor(EmissionColorPropId, Color.black);
            }
            else
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                // Nilai non-nol kecil agar varian shader _EMISSION tidak pernah di-strip saat runtime & build IL2CPP OpenGLES3
                mat.SetColor(EmissionColorPropId, new Color(0.01f, 0.01f, 0.01f, 1f));
            }
        }
        if (usesCustomHardwareShader && mat.HasProperty(RimColorPropId))
        {
            mat.SetColor(RimColorPropId, CableHighRimLinear);
            mat.SetFloat(RimPowerPropId, 2.4f);
            mat.SetFloat(RimIntensityPropId, 0.65f);
        }

        ownedMaterials.Add(mat);
        return mat;
    }

    private void DisableCableTubesAndPacketsFrom(int startBeamIndex)
    {
        int start = Mathf.Max(0, startBeamIndex);
        for (int b = start; b < MaxCableBeams; b++)
        {
            if (installedTubePool != null
                && b < installedTubePool.Length
                && installedTubePool[b] != null
                && installedTubePool[b].meshRenderer != null
                && installedTubePool[b].meshRenderer.enabled)
            {
                installedTubePool[b].meshRenderer.enabled = false;
            }
            if (packetRenderers != null)
            {
                int poolBase = b * PacketsPerCable;
                for (int p = 0; p < PacketsPerCable; p++)
                {
                    int slot = poolBase + p;
                    if (slot < packetRenderers.Length && packetRenderers[slot] != null && packetRenderers[slot].enabled)
                    {
                        packetRenderers[slot].enabled = false;
                    }
                }
            }
        }
    }

    private void DisableAllRenderers()
    {
        if (portSockets != null)
        {
            for (int i = 0; i < portSockets.Length; i++)
            {
                if (portSockets[i].collarRenderer != null) portSockets[i].collarRenderer.enabled = false;
                if (portSockets[i].orbRenderer != null) portSockets[i].orbRenderer.enabled = false;
                if (portSockets[i].stubRenderer != null) portSockets[i].stubRenderer.enabled = false;
            }
        }
        if (lockHaloRingRenderer != null) lockHaloRingRenderer.enabled = false;
        if (liveDragTube != null && liveDragTube.meshRenderer != null) liveDragTube.meshRenderer.enabled = false;
        if (installedTubePool != null)
        {
            for (int i = 0; i < installedTubePool.Length; i++)
            {
                if (installedTubePool[i] != null && installedTubePool[i].meshRenderer != null)
                {
                    installedTubePool[i].meshRenderer.enabled = false;
                }
            }
        }
        if (packetRenderers != null)
        {
            for (int i = 0; i < packetRenderers.Length; i++)
            {
                if (packetRenderers[i] != null) packetRenderers[i].enabled = false;
            }
        }
    }

    private void OnDestroy()
    {
        if (rootObj != null)
        {
            Destroy(rootObj);
            rootObj = null;
        }

        for (int i = 0; i < ownedMeshes.Count; i++)
        {
            if (ownedMeshes[i] != null)
            {
                Destroy(ownedMeshes[i]);
            }
        }
        ownedMeshes.Clear();

        for (int i = 0; i < ownedMaterials.Count; i++)
        {
            if (ownedMaterials[i] != null)
            {
                Destroy(ownedMaterials[i]);
            }
        }
        ownedMaterials.Clear();

        wires.Clear();
        activePorts.Clear();
        pbrSharedMat = null;
        sharedMpb = null;
        sharedSocketCollarMesh = null;
        sharedSocketOrbMesh = null;
        sharedLockRingMesh = null;
        sharedStubCylinderMesh = null;
        sharedPacketCapsuleMesh = null;
        portSockets = null;
        lockHaloRingTransform = null;
        lockHaloRingRenderer = null;
        installedTubePool = null;
        liveDragTube = null;
        packetTransforms = null;
        packetRenderers = null;
        cablePacketPhases = null;
    }
}
