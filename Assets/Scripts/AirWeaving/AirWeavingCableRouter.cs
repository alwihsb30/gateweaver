using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pengendali kabel manual Port-ke-Port khusus Mode Udara (GateWeaver AR v7.0 Fase 3).
/// - Memproyeksikan orb gagang port hologram sedikit keluar dari siluet 0.70x agar Port_InA & Port_InB terpisah lega (0.180 unit)
///   dan bebas tumpang tindih dengan hit-zone badan gerbang (R_port = 0.115 vs R_body = 0.140).
/// - Aturan Fan-Out & Unplug (Kunci v7.0):
///   * Fist pada Port_Out (meski sudah punya kabel) SELALU menarik cabang kabel baru (fan-out) tanpa memutus kabel lama.
///   * Fist pada Port_In yang terisi MENCABUT ujung kabel lama (geser ke Port_In lain, atau Buka di ruang kosong untuk hapus).
///   * Menempelkan kabel ke Port_In yang sudah terisi otomatis mengganti (replace) kabel lama pada slot tersebut.
/// - Merender busur Bezier tarik live, snap magnetik, serta pool kabel ekstra untuk cabang fan-out ke-2+ (zero-GC, Sprites/Default).
/// </summary>
public sealed class AirWeavingCableRouter : MonoBehaviour
{
    public const float PortOutHitRadius = 0.165f;
    public const float PortInHitRadius = 0.130f;
    private const float SnapMagnetRadius = 0.240f;
    private const float GateAutoSnapRadius = 0.300f;
    private const float SnapReleaseLatchSeconds = 0.200f;
    private const float SnapReleaseMaxEscapeDist = 0.440f;
    private const float InputPortOutOffsetX = 0.275f;
    private const float GatePortOutOffsetX = 0.265f;
    private const float PortOutOffsetY = 0.020f;
    private const float PortInOffsetX = -0.255f;
    private const float PortInSingleOffsetY = 0.020f;
    private const float PortInAOffsetY = 0.105f;
    private const float PortInBOffsetY = -0.075f;

    private const int MaxPortVisuals = 20;
    private const int MaxFanOutBeams = 10;
    private const int BezierSegments = 16;
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
    private static readonly Color DragBeamSnappedColor = new Color(0.28f, 1.00f, 0.54f, 1.00f);
    private static readonly Color FanOutHighColor = new Color(0.18f, 0.98f, 0.66f, 0.95f);
    private static readonly Color FanOutLowColor = new Color(1.00f, 0.42f, 0.26f, 0.88f);
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

    private readonly List<ManualConnectionEdge> wires = new List<ManualConnectionEdge>(16);
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
    private LineRenderer[] fanOutBeams;
    private Material[] fanOutMats;

    private readonly Vector3[] ringPointsBuffer = new Vector3[PortRingSegments];
    private readonly Vector3[] bezierBuffer = new Vector3[BezierSegments];
    private readonly List<Material> ownedMaterials = new List<Material>(40);

    private bool isInitialized = false;
    private int hoveredPortIndex = -1;
    private bool isDraggingCable = false;
    private bool dragFromOutput = true;
    private GateNode dragOriginNode = null;
    private int dragOriginSlot = 0;
    private int snappedTargetPortIndex = -1;

    // Snap Release-Latch (200 ms): tahan target snap valid sesaat agar drift telapak saat membuka kepalan tidak membatalkan kabel
    private GateNode latchedTargetNode = null;
    private int latchedTargetSlot = 0;
    private Vector2 latchedTargetLocalXY = Vector2.zero;
    private float lastSnapUnscaledTime = -10f;

    public bool IsHoveringPort => hoveredPortIndex >= 0 && hoveredPortIndex < activePorts.Count;
    public bool IsDraggingCable => isDraggingCable;
    public GateNode HoveredPortNode => IsHoveringPort ? activePorts[hoveredPortIndex].node : null;
    public bool IsHoveringOutputNodePort => IsHoveringPort && activePorts[hoveredPortIndex].node != null && activePorts[hoveredPortIndex].node.nodeType == NodeType.OutputActuator;
    public int ActiveWireCount => wires.Count;

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
            portOrbMats[i] = CreateSafeSpritesMat(PortOutIdleColor, 3012);
            portOrbRings[i] = CreateLine($"Holo_PortOrb_{i}", PortRingSegments, loop: true, width: 0.011f, mat: portOrbMats[i]);
            portStubLines[i] = CreateLine($"Holo_PortStub_{i}", 2, loop: false, width: 0.0045f, mat: stubSharedMat);
        }

        hoverTargetMat = CreateSafeSpritesMat(PortHoverColor, 3019);
        hoverTargetRing = CreateLine("Holo_PortHoverRing", PortRingSegments, loop: true, width: 0.0075f, mat: hoverTargetMat);

        dragBeamMat = CreateSafeSpritesMat(DragBeamFreeColor, 3020);
        dragBeamRenderer = CreateLine("Holo_LiveDragCable", BezierSegments, loop: false, width: 0.014f, mat: dragBeamMat);

        fanOutBeams = new LineRenderer[MaxFanOutBeams];
        fanOutMats = new Material[MaxFanOutBeams];
        for (int i = 0; i < MaxFanOutBeams; i++)
        {
            fanOutMats[i] = CreateSafeSpritesMat(FanOutLowColor, 3010);
            fanOutBeams[i] = CreateLine($"Holo_FanOutCable_{i}", BezierSegments, loop: false, width: 0.011f, mat: fanOutMats[i]);
        }

        isInitialized = true;
        HideImmediate();
    }

    public void HideImmediate()
    {
        CancelActiveDrag();
        hoveredPortIndex = -1;
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

        if (removedAny && circuitManager != null)
        {
            NormalizeAllTwoInputGateSlots();
            circuitManager.SetManualConnections(wires);
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
    /// Membangun daftar port aktif pada meja kerja dan memproses hover/tarik/lepas kabel manual (Zero-GC).
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
        bool isGrabbingGate,
        bool isHandDetected,
        bool isFistActive,
        bool fistJustStarted,
        bool fistJustEnded,
        Vector2 stabilizedCursorLocalXY,
        Vector2 liveCursorLocalXY,
        Vector3 palmWorldPos)
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

        // 3. Evaluasi interaksi port & drag kabel
        bool hardBlocked = isUiOverlayOpen || isVerifying || isGrabbingGate;
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
            // Sedang menyeret kabel aktif -> perbarui snap magnetik & latch 200 ms selama tangan terlihat
            hoveredPortIndex = -1;
            if (isHandDetected)
            {
                snappedTargetPortIndex = FindNearestCompatibleTargetPort(liveCursorLocalXY, circuitManager);
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

            if (fistJustEnded || !isFistActive)
            {
                CompleteOrCancelCableDrag(circuitManager, isHandDetected, liveCursorLocalXY);
            }
        }
        else if (!isHandDetected)
        {
            hoveredPortIndex = -1;
            snappedTargetPortIndex = -1;
        }
        else
        {
            Vector2 queryXY = fistJustStarted ? stabilizedCursorLocalXY : liveCursorLocalXY;
            hoveredPortIndex = FindNearestPortIndex(queryXY);

            if (fistJustStarted && hoveredPortIndex >= 0 && hoveredPortIndex < activePorts.Count)
            {
                BeginCableDragFromPort(activePorts[hoveredPortIndex], circuitManager);
            }
        }

        // 4. Perbarui seluruh visual port hologram, busur tarik live, & kabel fan-out ekstra
        RenderPortHandles(camT, circuitManager);
        RenderLiveDragBeam(camT, palmWorldPos);
        RenderExtraFanOutBeams();
    }

    private void BeginCableDragFromPort(ActivePortEntry port, CircuitManager circuitManager)
    {
        if (port.node == null)
        {
            return;
        }

        if (port.isOutput)
        {
            // Aturan #1: Fist pada Port_Out SELALU menarik cabang kabel baru (fan-out) tanpa memutus kabel lama
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

        // Fist pada Port_In: bila sudah terisi -> CABUT kabel lama untuk dipindahkan / dihapus di ruang kosong
        int existingIdx = FindWireByTargetSlot(port.node, port.slotIndex);
        if (existingIdx >= 0)
        {
            ManualConnectionEdge oldEdge = wires[existingIdx];
            wires.RemoveAt(existingIdx);
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
            // Snap Release-Latch (200 ms): pertahankan target valid saat telapak sedikit bergeser ketika membuka kepalan
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
                // Smart Empty-Slot Commit: pada gerbang 2-input, utamakan slot yang masih kosong (?) sebelum replace
                int finalSlot = ResolveCommitSlotForTarget(dstNode, preferredSlot);
                int replacedIdx = FindWireByTargetSlot(dstNode, finalSlot);
                if (replacedIdx >= 0)
                {
                    wires.RemoveAt(replacedIdx);
                }

                wires.Add(new ManualConnectionEdge
                {
                    source = srcNode,
                    target = dstNode,
                    targetSlotIndex = finalSlot
                });

                NormalizeSingleWireSlotOnGate(dstNode);

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
                // Ditolak (duplikat paralel source->target atau siklus) -> batalkan bersih tanpa menimpa kabel lama
                OnWireDisconnectedSfx?.Invoke();
            }
        }
        else
        {
            // Dilepas di ruang kosong -> batal / selesai mencabut kabel
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

        // Kedua slot sudah penuh -> replace port yang paling dekat dibidik pemain
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

    /// <summary>
    /// Mengecek apakah pasangan (source -> target) sudah tersambung di slot mana pun (blokir parallel duplicate edge,
    /// sambil tetap mengizinkan fan-out ke gerbang tujuan yang berbeda). Zero-GC.
    /// </summary>
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

        if (changed && circuitManager != null)
        {
            NormalizeAllTwoInputGateSlots();
            circuitManager.SetManualConnections(wires);
        }
    }

    // =========================================================================
    // VISUAL RENDERING (PORT ORBS, MAGNET RING, DRAG BEZIER, & FAN-OUT POOL)
    // =========================================================================

    private void RenderPortHandles(Transform camT, CircuitManager circuitManager)
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
            bool isHovered = !isDraggingCable && i == hoveredPortIndex;
            bool isSnapped = isDraggingCable && i == snappedTargetPortIndex;
            bool isCompatible = isDraggingCable && IsCompatibleDragTarget(p, circuitManager);

            Color orbColor;
            float orbRadius = 0.028f;
            float orbWidth = 0.010f;

            if (isSnapped)
            {
                orbColor = PortSnapLockColor;
                orbRadius = 0.048f + (0.006f * pulse);
                orbWidth = 0.014f;
            }
            else if (isHovered)
            {
                orbColor = PortHoverColor;
                orbRadius = 0.045f + (0.005f * pulse);
                orbWidth = 0.013f;
            }
            else if (isCompatible)
            {
                if (!p.isOutput && !p.isConnected)
                {
                    // Slot kosong (?) berdenyut terang agar pemain langsung melihat pin yang masih kosong
                    orbColor = PortValidTargetColor;
                    orbRadius = 0.044f + (0.008f * pulse);
                    orbWidth = 0.014f;
                }
                else if (!p.isOutput && p.node.GetMaxInputCount() >= 2 && HasEmptyInputSlot(p.node))
                {
                    // Slot sudah terisi sementara gerbang masih punya slot kosong -> redupkan agar fokus ke slot kosong
                    orbColor = PortInConnectedDimColor;
                    orbRadius = 0.022f;
                    orbWidth = 0.008f;
                }
                else if (!p.isOutput && p.isConnected)
                {
                    // Semua slot gerbang sudah penuh -> tandai kuning-amber bahwa menempel di sini akan mengganti kabel
                    orbColor = PortReplaceHintColor;
                    orbRadius = 0.034f + (0.004f * pulse);
                    orbWidth = 0.011f;
                }
                else
                {
                    orbColor = PortValidTargetColor;
                    orbRadius = 0.038f + (0.006f * pulse);
                    orbWidth = 0.012f;
                }
            }
            else if (p.isOutput)
            {
                orbColor = p.node.currentOutputValue ? PortValidTargetColor : PortOutIdleColor;
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
                DrawCircle(hoverTargetRing, hoverTargetMat, p.handleWorldPos - (camT.forward * 0.01f), right, up, orbRadius + 0.030f, ringCol, 0.0065f);
                drewHoverRing = true;
            }
        }

        if (!drewHoverRing && hoverTargetRing != null && hoverTargetRing.enabled)
        {
            hoverTargetRing.enabled = false;
        }
    }

    private void RenderLiveDragBeam(Transform camT, Vector3 palmWorldPos)
    {
        if (!isDraggingCable || dragOriginNode == null || dragBeamRenderer == null)
        {
            if (dragBeamRenderer != null && dragBeamRenderer.enabled)
            {
                dragBeamRenderer.enabled = false;
            }
            return;
        }

        Vector3 startWorld = dragFromOutput
            ? dragOriginNode.GetOutputPortWorldPosition()
            : dragOriginNode.GetInputPortWorldPosition(dragOriginSlot);

        bool snapped = snappedTargetPortIndex >= 0 && snappedTargetPortIndex < activePorts.Count;
        Vector3 endWorld = snapped
            ? activePorts[snappedTargetPortIndex].physicalWorldPos
            : palmWorldPos;

        Vector3 apexDir = -camT.forward * 0.22f + camT.up * 0.08f;
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
        dragBeamRenderer.widthMultiplier = snapped ? 0.017f : 0.013f;
        dragBeamRenderer.startColor = beamCol;
        dragBeamRenderer.endColor = beamCol;
        dragBeamRenderer.SetPositions(bezierBuffer);
    }

    /// <summary>
    /// Merender cabang kabel fan-out ke-2 dst. (yang tidak digambar oleh LineRenderer tunggal GateNode.connectedTarget).
    /// </summary>
    private void RenderExtraFanOutBeams()
    {
        int beamCursor = 0;

        for (int i = 0; i < wires.Count && beamCursor < MaxFanOutBeams; i++)
        {
            ManualConnectionEdge edge = wires[i];
            if (edge.source == null || edge.target == null || !edge.source.isTracked || !edge.target.isTracked)
            {
                continue;
            }

            // Cek apakah edge ini sudah digambar oleh LineRenderer bawaan GateNode.connectedTarget (slot pertama ke target primer)
            bool drawnByPrimary = edge.source.connectedTarget == edge.target
                                  && IsFirstEdgeBetween(i, edge.source, edge.target);
            if (drawnByPrimary)
            {
                continue;
            }

            Vector3 p0 = edge.source.GetOutputPortWorldPosition();
            int visualSlot = edge.targetSlotIndex == 1 && edge.target.GetMaxInputCount() >= 2 ? 1 : 0;
            Vector3 p2 = edge.target.GetInputPortWorldPosition(visualSlot);

            Vector3 srcUp = edge.source.transform.up.sqrMagnitude > 0.0001f ? edge.source.transform.up.normalized : Vector3.up;
            Vector3 dstUp = edge.target.transform.up.sqrMagnitude > 0.0001f ? edge.target.transform.up.normalized : Vector3.up;
            Vector3 avgUp = (srcUp + dstUp).sqrMagnitude > 0.0001f ? (srcUp + dstUp).normalized : srcUp;
            float dist = Vector3.Distance(p0, p2);
            float apex = Mathf.Lerp(0.18f, 0.30f, Mathf.Clamp01((dist - 0.30f) / 1.40f));
            Vector3 p1 = ((p0 + p2) * 0.5f) + (avgUp * (apex * 2f));

            for (int s = 0; s < BezierSegments; s++)
            {
                float t = s / (float)(BezierSegments - 1);
                float u = 1f - t;
                bezierBuffer[s] = (u * u * p0) + (2f * u * t * p1) + (t * t * p2);
            }

            Color col = edge.source.currentOutputValue ? FanOutHighColor : FanOutLowColor;
            LineRenderer lr = fanOutBeams[beamCursor];
            if (fanOutMats[beamCursor] != null)
            {
                fanOutMats[beamCursor].color = col;
            }
            lr.enabled = true;
            lr.startColor = col;
            lr.endColor = col;
            lr.SetPositions(bezierBuffer);
            beamCursor++;
        }

        for (int b = beamCursor; b < MaxFanOutBeams; b++)
        {
            if (fanOutBeams[b] != null && fanOutBeams[b].enabled)
            {
                fanOutBeams[b].enabled = false;
            }
        }
    }

    private bool IsFirstEdgeBetween(int currentIndex, GateNode src, GateNode dst)
    {
        for (int j = 0; j < currentIndex; j++)
        {
            if (wires[j].source == src && wires[j].target == dst)
            {
                return false;
            }
        }
        return true;
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
        if (fanOutBeams != null)
        {
            for (int i = 0; i < fanOutBeams.Length; i++)
            {
                if (fanOutBeams[i] != null) fanOutBeams[i].enabled = false;
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
        fanOutBeams = null;
        fanOutMats = null;
    }
}
