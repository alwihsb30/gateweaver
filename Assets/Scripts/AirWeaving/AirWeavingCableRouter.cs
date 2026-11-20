using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pengendali kabel manual Port-ke-Port berbasis Pinch (Cubit #4-#8) & Renderer Kabel Seragam 100% Mode Udara
/// (GateWeaver AR v7.0 Fase 3 + Polishing Final).
/// - Memproyeksikan orb gagang port hologram keluar dari siluet 0.70x agar Port_InA & Port_InB terpisah lega.
/// - Terpisah total dari Fist (objek): seluruh hover port, tarik fan-out, cabut/pindah, & snap magnetik digerakkan
///   oleh titik cubit presisi 0.5*(#4+#8) (PinchPointLocalXY).
/// - Aturan Fan-Out & Unplug:
///   * Pinch pada Port_Out SELALU menarik cabang kabel baru (fan-out) tanpa memutus kabel lama.
///   * Pinch pada Port_In yang terisi MENCABUT ujung kabel lama (pindah ke Port_In lain, atau lepas di ruang kosong untuk hapus).
///   * Menempelkan kabel ke Port_In yang sudah terisi otomatis mengganti (replace) kabel lama pada slot tersebut.
/// - Merender 100% seluruh kabel Mode Udara (primer & fan-out) seragam lewat pool Bezier 20 slot zero-GC tepat dari
///   orb Port_Out ke orb Port_In dengan pendar seragam (1 = sian terang, 0 = merah/oranye menyala).
/// </summary>
public sealed class AirWeavingCableRouter : MonoBehaviour
{
    public const float PortOutHitRadius = 0.225f;
    public const float PortInHitRadius = 0.185f;
    private const float SnapMagnetRadius = 0.285f;
    private const float GateAutoSnapRadius = 0.340f;
    private const float SnapReleaseLatchSeconds = 0.200f;
    private const float SnapReleaseMaxEscapeDist = 0.460f;
    private const float InputPortOutOffsetX = 0.335f;
    private const float GatePortOutOffsetX = 0.325f;
    private const float PortOutOffsetY = 0.025f;
    private const float PortInOffsetX = -0.315f;
    private const float PortInSingleOffsetY = 0.025f;
    private const float PortInAOffsetY = 0.130f;
    private const float PortInBOffsetY = -0.095f;

    private const int MaxPortVisuals = 20;
    private const int MaxCableBeams = 20;
    private const int BezierSegments = 18;
    private const int PortRingSegments = 16;

    private static readonly Color PortOutIdleColor = new Color(0.24f, 0.92f, 1.00f, 0.92f);
    private static readonly Color PortInEmptyColor = new Color(0.76f, 0.90f, 1.00f, 0.88f);
    private static readonly Color PortInConnectedColor = new Color(0.22f, 0.96f, 0.62f, 0.92f);
    private static readonly Color PortInConnectedDimColor = new Color(0.22f, 0.60f, 0.48f, 0.48f);
    private static readonly Color PortReplaceHintColor = new Color(1.00f, 0.78f, 0.24f, 0.94f);
    private static readonly Color PortHoverColor = new Color(0.55f, 1.00f, 0.96f, 1.00f);
    private static readonly Color PortValidTargetColor = new Color(0.32f, 1.00f, 0.54f, 1.00f);
    private static readonly Color PortSnapLockColor = new Color(0.84f, 1.00f, 0.30f, 1.00f);
    private static readonly Color DragBeamFreeColor = new Color(0.32f, 0.96f, 1.00f, 0.96f);
    private static readonly Color DragBeamSnappedColor = new Color(0.28f, 1.00f, 0.54f, 0.98f);
    private static readonly Color CableHighCoreColor = new Color(0.20f, 0.96f, 1.00f, 0.98f);
    private static readonly Color CableHighGlowColor = new Color(0.16f, 0.90f, 1.00f, 0.30f);
    private static readonly Color CableLowCoreColor = new Color(1.00f, 0.38f, 0.16f, 0.96f);
    private static readonly Color CableLowGlowColor = new Color(1.00f, 0.28f, 0.12f, 0.26f);
    private static readonly Color StubLineColor = new Color(0.26f, 0.88f, 1.00f, 0.48f);

    private struct ActivePortEntry
    {
        public GateNode node;
        public bool isOutput;
        public int slotIndex;
        public Vector2 nodeLocalXY;
        public Vector2 handleLocalXY;
        public Vector3 handleWorldPos;
        public Vector3 physicalWorldPos;
        public bool isConnected;
    }

    public event Action OnWireConnectedSfx;
    public event Action OnWireDisconnectedSfx;

    private readonly List<ManualConnectionEdge> wires = new List<ManualConnectionEdge>(MaxCableBeams);
    private readonly List<ActivePortEntry> activePorts = new List<ActivePortEntry>(MaxPortVisuals);

    private GameObject rootObj;
    private LineRenderer[] portOrbRings;
    private LineRenderer[] portStubLines;
    private Material[] portOrbMats;
    private Material stubSharedMat;
    private LineRenderer hoverTargetRing;
    private Material hoverTargetMat;
    private LineRenderer dragBeamRenderer;
    private Material dragBeamMat;
    private LineRenderer[] cableCoreBeams;
    private Material[] cableCoreMats;
    private LineRenderer[] cableGlowBeams;
    private Material[] cableGlowMats;

    private readonly Vector3[] ringPointsBuffer = new Vector3[PortRingSegments];
    private readonly Vector3[] bezierBuffer = new Vector3[BezierSegments];
    private readonly List<Material> ownedMaterials = new List<Material>(72);

    private bool isInitialized = false;
    private int hoveredPortIndex = -1;
    private bool isDraggingCable = false;
    private bool dragFromOutput = true;
    private GateNode dragOriginNode = null;
    private int dragOriginSlot = 0;
    private int snappedTargetPortIndex = -1;
    private Vector3 lastValidPinchWorldPos = Vector3.zero;
    private bool hasLastValidPinchWorld = false;

    // Snap Release-Latch (200 ms): tahan target snap valid sesaat agar drift ujung jari saat melepas cubit tidak membatalkan kabel
    private GateNode latchedTargetNode = null;
    private int latchedTargetSlot = 0;
    private Vector2 latchedTargetLocalXY = Vector2.zero;
    private float lastSnapUnscaledTime = -10f;

    public bool IsHoveringPort => hoveredPortIndex >= 0 && hoveredPortIndex < activePorts.Count;
    public bool IsDraggingCable => isDraggingCable;
    public bool IsSnappedTargetPort => isDraggingCable && snappedTargetPortIndex >= 0 && snappedTargetPortIndex < activePorts.Count;
    public GateNode HoveredPortNode => IsHoveringPort ? activePorts[hoveredPortIndex].node : null;
    public bool IsHoveringOutputNodePort => IsHoveringPort && activePorts[hoveredPortIndex].node != null && activePorts[hoveredPortIndex].node.nodeType == NodeType.OutputActuator;
    public int ActiveWireCount => wires.Count;
    public int TopologyVersion { get; private set; }

    public void EnsureVisualsCreated()
    {
        if (isInitialized)
        {
            return;
        }

        rootObj = new GameObject("AR_AirWeavingCableRouter");
        rootObj.transform.SetParent(transform, false);

        stubSharedMat = CreateSafeSpritesMat(StubLineColor, 3007);
        portOrbRings = new LineRenderer[MaxPortVisuals];
        portStubLines = new LineRenderer[MaxPortVisuals];
        portOrbMats = new Material[MaxPortVisuals];

        for (int i = 0; i < MaxPortVisuals; i++)
        {
            portOrbMats[i] = CreateSafeSpritesMat(PortOutIdleColor, 3013);
            portOrbRings[i] = CreateLine($"Holo_PortOrb_{i}", PortRingSegments, loop: true, width: 0.011f, mat: portOrbMats[i]);
            portStubLines[i] = CreateLine($"Holo_PortStub_{i}", 2, loop: false, width: 0.0045f, mat: stubSharedMat);
        }

        hoverTargetMat = CreateSafeSpritesMat(PortHoverColor, 3019);
        hoverTargetRing = CreateLine("Holo_PortHoverRing", PortRingSegments, loop: true, width: 0.0075f, mat: hoverTargetMat);

        dragBeamMat = CreateSafeSpritesMat(DragBeamFreeColor, 3020);
        dragBeamRenderer = CreateLine("Holo_LiveDragCable", BezierSegments, loop: false, width: 0.015f, mat: dragBeamMat);

        // Pool 20 kabel Bezier seragam (inti plasma + pendar luar) untuk 100% koneksi Mode Udara
        cableCoreBeams = new LineRenderer[MaxCableBeams];
        cableCoreMats = new Material[MaxCableBeams];
        cableGlowBeams = new LineRenderer[MaxCableBeams];
        cableGlowMats = new Material[MaxCableBeams];
        for (int i = 0; i < MaxCableBeams; i++)
        {
            cableGlowMats[i] = CreateSafeSpritesMat(CableLowGlowColor, 3009);
            cableGlowBeams[i] = CreateLine($"Holo_UniformCableGlow_{i}", BezierSegments, loop: false, width: 0.032f, mat: cableGlowMats[i]);
            cableCoreMats[i] = CreateSafeSpritesMat(CableLowCoreColor, 3011);
            cableCoreBeams[i] = CreateLine($"Holo_UniformCableCore_{i}", BezierSegments, loop: false, width: 0.0135f, mat: cableCoreMats[i]);
        }

        isInitialized = true;
        HideImmediate();
    }

    public void HideImmediate()
    {
        CancelActiveDrag();
        hoveredPortIndex = -1;
        hasLastValidPinchWorld = false;
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
            if (circuitManager != null)
            {
                NormalizeAllTwoInputGateSlots();
                circuitManager.SetManualConnections(wires);
            }
        }
    }

    public void CancelActiveDrag()
    {
        isDraggingCable = false;
        dragOriginNode = null;
        dragOriginSlot = 0;
        snappedTargetPortIndex = -1;
        latchedTargetNode = null;
        latchedTargetSlot = 0;
        lastSnapUnscaledTime = -10f;
        if (dragBeamRenderer != null)
        {
            dragBeamRenderer.enabled = false;
        }
    }

    /// <summary>
    /// Membangun daftar port aktif, memproses interaksi Pinch (Cubit #4-#8) pada port kabel,
    /// dan merender 100% kabel Mode Udara secara seragam dari orb Port_Out ke orb Port_In (Zero-GC).
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
        Vector3 pinchWorldPos)
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

        if (isHandDetected)
        {
            lastValidPinchWorldPos = pinchWorldPos;
            hasLastValidPinchWorld = true;
        }

        // 1. Bersihkan edge yang node-nya sudah tidak aktif (defensif)
        PruneUntrackedWires(circuitManager);

        // 2. Bangun daftar gagang port proyektif untuk semua node aktif di meja kerja
        activePorts.Clear();
        RegisterNodePorts(camT, inputANode, workbenchDepth);
        RegisterNodePorts(camT, inputBNode, workbenchDepth);
        RegisterNodePorts(camT, outputNode, workbenchDepth);
        if (activePlacedGates != null)
        {
            for (int i = 0; i < activePlacedGates.Count; i++)
            {
                RegisterNodePorts(camT, activePlacedGates[i], workbenchDepth);
            }
        }

        // 3. Evaluasi interaksi port & drag kabel berbasis Pinch (Cubit #4-#8)
        bool hardBlocked = isUiOverlayOpen || isVerifying || blockedByOtherMode;
        if (hardBlocked)
        {
            if (isDraggingCable)
            {
                CancelActiveDrag();
            }
            hoveredPortIndex = -1;
            snappedTargetPortIndex = -1;
        }
        else if (isDraggingCable)
        {
            hoveredPortIndex = -1;
            if (isHandDetected)
            {
                snappedTargetPortIndex = FindNearestCompatibleTargetPort(livePinchLocalXY, circuitManager);
                if (snappedTargetPortIndex >= 0 && snappedTargetPortIndex < activePorts.Count)
                {
                    ActivePortEntry snapped = activePorts[snappedTargetPortIndex];
                    latchedTargetNode = snapped.node;
                    latchedTargetSlot = snapped.slotIndex;
                    latchedTargetLocalXY = snapped.handleLocalXY;
                    lastSnapUnscaledTime = Time.unscaledTime;
                }
            }
            else
            {
                snappedTargetPortIndex = -1;
            }

            if (pinchJustEnded || !isPinchActive)
            {
                CompleteOrCancelCableDrag(circuitManager, isHandDetected, livePinchLocalXY);
            }
        }
        else if (!isHandDetected)
        {
            hoveredPortIndex = -1;
            snappedTargetPortIndex = -1;
        }
        else
        {
            Vector2 queryXY = pinchJustStarted ? stabilizedPinchLocalXY : livePinchLocalXY;
            hoveredPortIndex = FindNearestPortIndex(queryXY);

            if (pinchJustStarted && hoveredPortIndex >= 0 && hoveredPortIndex < activePorts.Count)
            {
                BeginCableDragFromPort(activePorts[hoveredPortIndex], circuitManager);
            }
        }

        // 4. Perbarui seluruh visual port hologram, busur tarik live, & 100% kabel seragam orb-ke-orb
        RenderPortHandles(camT, circuitManager, isHandDetected && !hardBlocked);
        RenderLiveDragBeam(camT, hasLastValidPinchWorld ? lastValidPinchWorldPos : pinchWorldPos);
        RenderAllUniformCables(camT);
    }

    /// <summary>
    /// Menyinkronkan ulang posisi orb port, stub, dan ujung kabel Bezier tepat SETELAH pose melayang/miring 3D
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
        Vector3 pinchWorldPos)
    {
        if (!isInitialized || !isAirWeavingActive || mainCam == null)
        {
            return;
        }

        Transform camT = mainCam.transform;
        activePorts.Clear();
        RegisterNodePorts(camT, inputANode, workbenchDepth);
        RegisterNodePorts(camT, inputBNode, workbenchDepth);
        RegisterNodePorts(camT, outputNode, workbenchDepth);
        if (activePlacedGates != null)
        {
            for (int i = 0; i < activePlacedGates.Count; i++)
            {
                RegisterNodePorts(camT, activePlacedGates[i], workbenchDepth);
            }
        }

        RenderPortHandles(camT, circuitManager, allowHoverHighlight);
        RenderLiveDragBeam(camT, hasLastValidPinchWorld ? lastValidPinchWorldPos : pinchWorldPos);
        RenderAllUniformCables(camT);
    }

    private void BeginCableDragFromPort(ActivePortEntry port, CircuitManager circuitManager)
    {
        if (port.node == null)
        {
            return;
        }

        if (port.isOutput)
        {
            // Pinch pada Port_Out SELALU menarik cabang kabel baru (fan-out) tanpa memutus kabel lama
            isDraggingCable = true;
            dragFromOutput = true;
            dragOriginNode = port.node;
            dragOriginSlot = 0;
            snappedTargetPortIndex = -1;
            latchedTargetNode = null;
            lastSnapUnscaledTime = -10f;
            port.node.TriggerTapFeedback();
            return;
        }

        // Pinch pada Port_In: bila sudah terisi -> CABUT kabel lama untuk dipindahkan / dihapus di ruang kosong
        int existingIdx = FindWireByTargetSlot(port.node, port.slotIndex);
        if (existingIdx >= 0)
        {
            ManualConnectionEdge oldEdge = wires[existingIdx];
            wires.RemoveAt(existingIdx);
            TopologyVersion++;
            NormalizeSingleWireSlotOnGate(port.node);
            if (circuitManager != null)
            {
                circuitManager.SetManualConnections(wires);
            }

            isDraggingCable = true;
            dragFromOutput = true;
            dragOriginNode = oldEdge.source;
            dragOriginSlot = 0;
            snappedTargetPortIndex = -1;
            latchedTargetNode = null;
            lastSnapUnscaledTime = -10f;
            OnWireDisconnectedSfx?.Invoke();
            return;
        }

        // Port_In kosong -> tarik kabel mundur menuju Port_Out sumber
        isDraggingCable = true;
        dragFromOutput = false;
        dragOriginNode = port.node;
        dragOriginSlot = port.slotIndex;
        snappedTargetPortIndex = -1;
        latchedTargetNode = null;
        lastSnapUnscaledTime = -10f;
        port.node.TriggerTapFeedback();
    }

    private void CompleteOrCancelCableDrag(CircuitManager circuitManager, bool isHandDetected, Vector2 releaseCursorLocalXY)
    {
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
                 && (!isHandDetected || Vector2.Distance(releaseCursorLocalXY, latchedTargetLocalXY) <= SnapReleaseMaxEscapeDist))
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

                NormalizeSingleWireSlotOnGate(dstNode);
                TopologyVersion++;

                if (circuitManager != null)
                {
                    circuitManager.SetManualConnections(wires);
                }

                dstNode.TriggerInputPortSnapFlash(finalSlot, dstNode.GetInputPortWorldPosition(finalSlot));
                srcNode.TriggerTapFeedback();
                OnWireConnectedSfx?.Invoke();
            }
            else
            {
                OnWireDisconnectedSfx?.Invoke();
            }
        }
        else
        {
            OnWireDisconnectedSfx?.Invoke();
        }

        CancelActiveDrag();
    }

    private int ResolveCommitSlotForTarget(GateNode dstNode, int preferredSlot)
    {
        if (dstNode == null || dstNode.GetMaxInputCount() < 2)
        {
            return 0;
        }

        bool slot0Occupied = FindWireByTargetSlot(dstNode, 0) >= 0;
        bool slot1Occupied = FindWireByTargetSlot(dstNode, 1) >= 0;

        if (!slot0Occupied)
        {
            return 0;
        }
        if (!slot1Occupied)
        {
            return 1;
        }

        return preferredSlot == 1 ? 1 : 0;
    }

    private void NormalizeSingleWireSlotOnGate(GateNode gate)
    {
        if (gate == null || gate.GetMaxInputCount() < 2)
        {
            return;
        }

        int idx0 = FindWireByTargetSlot(gate, 0);
        int idx1 = FindWireByTargetSlot(gate, 1);
        if (idx0 < 0 && idx1 >= 0)
        {
            ManualConnectionEdge promoted = wires[idx1];
            promoted.targetSlotIndex = 0;
            wires[idx1] = promoted;
        }
    }

    private void NormalizeAllTwoInputGateSlots()
    {
        for (int i = 0; i < wires.Count; i++)
        {
            GateNode target = wires[i].target;
            if (target != null && target.GetMaxInputCount() >= 2)
            {
                NormalizeSingleWireSlotOnGate(target);
            }
        }
    }

    private void RegisterNodePorts(Transform camT, GateNode node, float workbenchDepth)
    {
        if (node == null || !node.isTracked || activePorts.Count >= MaxPortVisuals)
        {
            return;
        }

        Vector3 nodeLocal = camT.InverseTransformPoint(node.transform.position);
        Vector2 nodeLocalXY = new Vector2(nodeLocal.x, nodeLocal.y);
        float zHandle = workbenchDepth - 0.04f;

        if (node.nodeType != NodeType.OutputActuator && activePorts.Count < MaxPortVisuals)
        {
            float outOffX = node.nodeType == NodeType.InputSource ? InputPortOutOffsetX : GatePortOutOffsetX;
            Vector2 handleXY = new Vector2(nodeLocal.x + outOffX, nodeLocal.y + PortOutOffsetY);
            activePorts.Add(new ActivePortEntry
            {
                node = node,
                isOutput = true,
                slotIndex = 0,
                nodeLocalXY = nodeLocalXY,
                handleLocalXY = handleXY,
                handleWorldPos = camT.TransformPoint(new Vector3(handleXY.x, handleXY.y, zHandle)),
                physicalWorldPos = node.GetOutputPortWorldPosition(),
                isConnected = HasOutgoingWire(node)
            });
        }

        int maxInputs = node.GetMaxInputCount();
        if (maxInputs >= 1 && activePorts.Count < MaxPortVisuals)
        {
            float offY = maxInputs == 2 ? PortInAOffsetY : PortInSingleOffsetY;
            Vector2 handleXY = new Vector2(nodeLocal.x + PortInOffsetX, nodeLocal.y + offY);
            activePorts.Add(new ActivePortEntry
            {
                node = node,
                isOutput = false,
                slotIndex = 0,
                nodeLocalXY = nodeLocalXY,
                handleLocalXY = handleXY,
                handleWorldPos = camT.TransformPoint(new Vector3(handleXY.x, handleXY.y, zHandle)),
                physicalWorldPos = node.GetInputPortWorldPosition(0),
                isConnected = FindWireByTargetSlot(node, 0) >= 0
            });
        }

        if (maxInputs >= 2 && activePorts.Count < MaxPortVisuals)
        {
            Vector2 handleXY = new Vector2(nodeLocal.x + PortInOffsetX, nodeLocal.y + PortInBOffsetY);
            activePorts.Add(new ActivePortEntry
            {
                node = node,
                isOutput = false,
                slotIndex = 1,
                nodeLocalXY = nodeLocalXY,
                handleLocalXY = handleXY,
                handleWorldPos = camT.TransformPoint(new Vector3(handleXY.x, handleXY.y, zHandle)),
                physicalWorldPos = node.GetInputPortWorldPosition(1),
                isConnected = FindWireByTargetSlot(node, 1) >= 0
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

    private int FindNearestPortIndex(Vector2 cursorXY)
    {
        int bestIdx = -1;
        float bestNormDist = 1.0001f;

        for (int i = 0; i < activePorts.Count; i++)
        {
            float radius = activePorts[i].isOutput ? PortOutHitRadius : PortInHitRadius;
            float dist = Vector2.Distance(cursorXY, activePorts[i].handleLocalXY);
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

    private int FindNearestCompatibleTargetPort(Vector2 cursorXY, CircuitManager circuitManager)
    {
        int bestIdx = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < activePorts.Count; i++)
        {
            ActivePortEntry candidate = activePorts[i];
            if (!IsCompatibleDragTarget(candidate, circuitManager))
            {
                continue;
            }

            if (dragFromOutput)
            {
                float distToHandle = Vector2.Distance(cursorXY, candidate.handleLocalXY);
                Vector2 gateSmartCenter = new Vector2(candidate.nodeLocalXY.x - 0.08f, candidate.nodeLocalXY.y + 0.02f);
                float distToGateZone = Vector2.Distance(cursorXY, gateSmartCenter);

                bool withinHandle = distToHandle <= SnapMagnetRadius;
                bool withinGateZone = distToGateZone <= GateAutoSnapRadius;
                if (!withinHandle && !withinGateZone)
                {
                    continue;
                }

                // Smart Empty-Slot Auto-Snap pada gerbang 2-input: utamakan slot kosong (?) daripada slot terisi
                if (candidate.node.GetMaxInputCount() >= 2)
                {
                    bool slot0Full = FindWireByTargetSlot(candidate.node, 0) >= 0;
                    bool slot1Full = FindWireByTargetSlot(candidate.node, 1) >= 0;
                    if (!slot0Full && candidate.slotIndex != 0)
                    {
                        continue;
                    }
                    if (slot0Full && !slot1Full && candidate.slotIndex != 1)
                    {
                        continue;
                    }
                }

                float effDist = Mathf.Min(distToHandle, distToGateZone);
                if (effDist < bestDist)
                {
                    bestDist = effDist;
                    bestIdx = i;
                }
            }
            else
            {
                float dist = Vector2.Distance(cursorXY, candidate.handleLocalXY);
                if (dist <= SnapMagnetRadius && dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }
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

    private bool HasEmptyInputSlot(GateNode node)
    {
        if (node == null)
        {
            return false;
        }

        int maxIn = node.GetMaxInputCount();
        for (int s = 0; s < maxIn; s++)
        {
            if (FindWireByTargetSlot(node, s) < 0)
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
                NormalizeAllTwoInputGateSlots();
                circuitManager.SetManualConnections(wires);
            }
        }
    }

    // =========================================================================
    // VISUAL RENDERING (PORT ORBS, MAGNET RING, DRAG BEZIER, & 100% UNIFORM POOL)
    // =========================================================================

    private void RenderPortHandles(Transform camT, CircuitManager circuitManager, bool allowHoverHighlight)
    {
        Vector3 right = camT.right;
        Vector3 up = camT.up;
        float pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * 7f));
        bool drewHoverRing = false;

        for (int i = 0; i < MaxPortVisuals; i++)
        {
            if (i >= activePorts.Count)
            {
                if (portOrbRings[i] != null) portOrbRings[i].enabled = false;
                if (portStubLines[i] != null) portStubLines[i].enabled = false;
                continue;
            }

            ActivePortEntry p = activePorts[i];
            bool isHovered = allowHoverHighlight && !isDraggingCable && i == hoveredPortIndex;
            bool isSnapped = allowHoverHighlight && isDraggingCable && i == snappedTargetPortIndex;
            bool isCompatible = allowHoverHighlight && isDraggingCable && IsCompatibleDragTarget(p, circuitManager);

            Color orbColor;
            float orbRadius = 0.039f;
            float orbWidth = 0.013f;

            if (isSnapped)
            {
                orbColor = PortSnapLockColor;
                orbRadius = 0.064f + (0.008f * pulse);
                orbWidth = 0.017f;
            }
            else if (isHovered)
            {
                orbColor = PortHoverColor;
                orbRadius = 0.060f + (0.007f * pulse);
                orbWidth = 0.016f;
            }
            else if (isCompatible)
            {
                if (!p.isOutput && !p.isConnected)
                {
                    orbColor = PortValidTargetColor;
                    orbRadius = 0.058f + (0.009f * pulse);
                    orbWidth = 0.017f;
                }
                else if (!p.isOutput && p.node.GetMaxInputCount() >= 2 && HasEmptyInputSlot(p.node))
                {
                    orbColor = PortInConnectedDimColor;
                    orbRadius = 0.030f;
                    orbWidth = 0.010f;
                }
                else if (!p.isOutput && p.isConnected)
                {
                    orbColor = PortReplaceHintColor;
                    orbRadius = 0.046f + (0.005f * pulse);
                    orbWidth = 0.014f;
                }
                else
                {
                    orbColor = PortValidTargetColor;
                    orbRadius = 0.050f + (0.007f * pulse);
                    orbWidth = 0.015f;
                }
            }
            else if (p.isOutput)
            {
                orbColor = p.node.currentOutputValue ? CableHighCoreColor : CableLowCoreColor;
            }
            else
            {
                orbColor = p.isConnected ? PortInConnectedColor : PortInEmptyColor;
            }

            DrawCircle(portOrbRings[i], portOrbMats[i], p.handleWorldPos, right, up, orbRadius, orbColor, orbWidth);

            LineRenderer stub = portStubLines[i];
            if (stub != null)
            {
                stub.enabled = true;
                stub.SetPosition(0, p.physicalWorldPos);
                stub.SetPosition(1, p.handleWorldPos);
            }

            if (isHovered || isSnapped)
            {
                Color ringCol = isSnapped ? PortSnapLockColor : PortHoverColor;
                DrawCircle(hoverTargetRing, hoverTargetMat, p.handleWorldPos - (camT.forward * 0.01f), right, up, orbRadius + 0.034f, ringCol, 0.0075f);
                drewHoverRing = true;
            }
        }

        if (!drewHoverRing && hoverTargetRing != null && hoverTargetRing.enabled)
        {
            hoverTargetRing.enabled = false;
        }
    }

    private void RenderLiveDragBeam(Transform camT, Vector3 pinchWorldPos)
    {
        if (!isDraggingCable || dragOriginNode == null || dragBeamRenderer == null)
        {
            if (dragBeamRenderer != null && dragBeamRenderer.enabled)
            {
                dragBeamRenderer.enabled = false;
            }
            return;
        }

        if (!TryGetPortHandleWorldPos(dragOriginNode, dragFromOutput, dragOriginSlot, out Vector3 startWorld))
        {
            dragBeamRenderer.enabled = false;
            return;
        }

        bool snapped = snappedTargetPortIndex >= 0 && snappedTargetPortIndex < activePorts.Count;
        Vector3 endWorld = snapped
            ? activePorts[snappedTargetPortIndex].handleWorldPos
            : pinchWorldPos;

        Vector3 apexDir = (-camT.forward * 0.14f) + (camT.up * 0.08f);
        Vector3 midCtrl = ((startWorld + endWorld) * 0.5f) + apexDir;

        for (int i = 0; i < BezierSegments; i++)
        {
            float t = i / (float)(BezierSegments - 1);
            float u = 1f - t;
            bezierBuffer[i] = (u * u * startWorld) + (2f * u * t * midCtrl) + (t * t * endWorld);
        }

        Color beamCol = snapped ? DragBeamSnappedColor : DragBeamFreeColor;
        if (dragBeamMat != null)
        {
            dragBeamMat.color = beamCol;
        }
        dragBeamRenderer.enabled = true;
        dragBeamRenderer.widthMultiplier = snapped ? 0.017f : 0.014f;
        dragBeamRenderer.startColor = beamCol;
        dragBeamRenderer.endColor = beamCol;
        dragBeamRenderer.SetPositions(bezierBuffer);
    }

    /// <summary>
    /// Merender 100% seluruh kabel Mode Udara (primer maupun cabang fan-out) seragam dari orb Port_Out ke orb Port_In
    /// dengan inti + pendar luar dan warna reaktif sinyal sumber (1 = sian terang, 0 = merah/oranye menyala). Zero-GC.
    /// </summary>
    private void RenderAllUniformCables(Transform camT)
    {
        int beamCursor = 0;
        Vector3 camForward = camT.forward;
        Vector3 camUp = camT.up;
        float highPulse = 1f + (0.08f * Mathf.Sin(Time.unscaledTime * 8.0f));

        for (int i = 0; i < wires.Count && beamCursor < MaxCableBeams; i++)
        {
            ManualConnectionEdge edge = wires[i];
            if (edge.source == null || edge.target == null || !edge.source.isTracked || !edge.target.isTracked)
            {
                continue;
            }

            int visualSlot = edge.targetSlotIndex == 1 && edge.target.GetMaxInputCount() >= 2 ? 1 : 0;
            if (!TryGetPortHandleWorldPos(edge.source, true, 0, out Vector3 p0)
                || !TryGetPortHandleWorldPos(edge.target, false, visualSlot, out Vector3 p2))
            {
                continue;
            }

            float dist = Vector3.Distance(p0, p2);
            float archUp = Mathf.Lerp(0.06f, 0.22f, Mathf.Clamp01((dist - 0.25f) / 1.50f));
            Vector3 p1 = ((p0 + p2) * 0.5f) - (camForward * 0.08f) + (camUp * archUp);

            for (int s = 0; s < BezierSegments; s++)
            {
                float t = s / (float)(BezierSegments - 1);
                float u = 1f - t;
                bezierBuffer[s] = (u * u * p0) + (2f * u * t * p1) + (t * t * p2);
            }

            bool isHigh = edge.source.currentOutputValue;
            Color coreCol = isHigh ? CableHighCoreColor : CableLowCoreColor;
            Color glowCol = isHigh ? CableHighGlowColor : CableLowGlowColor;
            float coreWidth = isHigh ? (0.0145f * highPulse) : 0.0125f;
            float glowWidth = isHigh ? (0.034f * highPulse) : 0.028f;

            LineRenderer glowLr = cableGlowBeams[beamCursor];
            if (cableGlowMats[beamCursor] != null)
            {
                cableGlowMats[beamCursor].color = glowCol;
            }
            glowLr.enabled = true;
            glowLr.widthMultiplier = glowWidth;
            glowLr.startColor = glowCol;
            glowLr.endColor = glowCol;
            glowLr.SetPositions(bezierBuffer);

            LineRenderer coreLr = cableCoreBeams[beamCursor];
            if (cableCoreMats[beamCursor] != null)
            {
                cableCoreMats[beamCursor].color = coreCol;
            }
            coreLr.enabled = true;
            coreLr.widthMultiplier = coreWidth;
            coreLr.startColor = coreCol;
            coreLr.endColor = coreCol;
            coreLr.SetPositions(bezierBuffer);

            beamCursor++;
        }

        for (int b = beamCursor; b < MaxCableBeams; b++)
        {
            if (cableCoreBeams[b] != null && cableCoreBeams[b].enabled)
            {
                cableCoreBeams[b].enabled = false;
            }
            if (cableGlowBeams[b] != null && cableGlowBeams[b].enabled)
            {
                cableGlowBeams[b].enabled = false;
            }
        }
    }

    private void DrawCircle(LineRenderer lr, Material mat, Vector3 center, Vector3 axisA, Vector3 axisB, float radius, Color color, float width)
    {
        if (lr == null)
        {
            return;
        }

        for (int i = 0; i < PortRingSegments; i++)
        {
            float angle = (i / (float)PortRingSegments) * Mathf.PI * 2f;
            ringPointsBuffer[i] = center + (axisA * (Mathf.Cos(angle) * radius)) + (axisB * (Mathf.Sin(angle) * radius));
        }

        if (mat != null)
        {
            mat.color = color;
        }
        lr.enabled = true;
        lr.widthMultiplier = width;
        lr.startColor = color;
        lr.endColor = color;
        lr.SetPositions(ringPointsBuffer);
    }

    private void DisableAllRenderers()
    {
        if (portOrbRings != null)
        {
            for (int i = 0; i < portOrbRings.Length; i++)
            {
                if (portOrbRings[i] != null) portOrbRings[i].enabled = false;
            }
        }
        if (portStubLines != null)
        {
            for (int i = 0; i < portStubLines.Length; i++)
            {
                if (portStubLines[i] != null) portStubLines[i].enabled = false;
            }
        }
        if (hoverTargetRing != null) hoverTargetRing.enabled = false;
        if (dragBeamRenderer != null) dragBeamRenderer.enabled = false;
        if (cableCoreBeams != null)
        {
            for (int i = 0; i < cableCoreBeams.Length; i++)
            {
                if (cableCoreBeams[i] != null) cableCoreBeams[i].enabled = false;
            }
        }
        if (cableGlowBeams != null)
        {
            for (int i = 0; i < cableGlowBeams.Length; i++)
            {
                if (cableGlowBeams[i] != null) cableGlowBeams[i].enabled = false;
            }
        }
    }

    private LineRenderer CreateLine(string objName, int count, bool loop, float width, Material mat)
    {
        GameObject obj = new GameObject(objName);
        obj.transform.SetParent(rootObj.transform, false);
        LineRenderer lr = obj.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = loop;
        lr.positionCount = count;
        lr.widthMultiplier = width;
        lr.numCapVertices = 3;
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
        wires.Clear();
        activePorts.Clear();
        portOrbRings = null;
        portStubLines = null;
        portOrbMats = null;
        stubSharedMat = null;
        hoverTargetRing = null;
        hoverTargetMat = null;
        dragBeamRenderer = null;
        dragBeamMat = null;
        cableCoreBeams = null;
        cableCoreMats = null;
        cableGlowBeams = null;
        cableGlowMats = null;
    }
}
