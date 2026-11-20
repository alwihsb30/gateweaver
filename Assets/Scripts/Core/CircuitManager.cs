using System.Collections.Generic;
using UnityEngine;

public struct ManualConnectionEdge
{
    public GateNode source;
    public GateNode target;
    public int targetSlotIndex;
}

public class CircuitManager : MonoBehaviour
{
    [Header("1. Daftar Node & Parameter Spasial")]
    [Tooltip("Daftar seluruh komponen GateNode di Scene (otomatis terisi saat Awake jika kosong)")]
    public List<GateNode> allNodes = new List<GateNode>();

    [Tooltip("Batas jarak maksimal antar-gerbang/output (skala marker Vuforia 0.8 = 8 cm; 3.0 = ~30 cm untuk mendukung susunan zig-zag 5 kartu)")]
    public float maxSnapDistance = 3.0f;

    [Tooltip("Pengali jangkauan khusus kartu InputSource agar dapat melewati kartu lain pada susunan berderet maupun zig-zag")]
    public float inputSourceRangeMultiplier = 1.8f;

    [Header("2. Status Runtime Rangkaian")]
    [Tooltip("Jumlah kartu gerbang logika (AND, OR, NOT, XOR, NAND) yang sedang aktif terdeteksi kamera")]
    public int activeGateCardsCount = 0;

    [HideInInspector]
    public bool useManualRouting = false;

    private readonly List<GateNode> trackedNodes = new List<GateNode>();
    private readonly List<GateNode> expressionActivePath = new List<GateNode>();
    private readonly List<ManualConnectionEdge> manualConnections = new List<ManualConnectionEdge>(16);
    private readonly List<GateNode> cycleQueue = new List<GateNode>(16);
    private readonly List<GateNode> cycleVisited = new List<GateNode>(16);
    private Camera cachedMainCamera;
    private Transform currentSortCameraTransform;
    private System.Comparison<GateNode> cachedNodeSortComparison;

    private void Awake()
    {
        cachedNodeSortComparison = CompareNodesByCameraX;

        // Otomatis isi allNodes menggunakan API Unity 6 jika belum diisi manual di Inspector
        allNodes.RemoveAll(node => node == null);

        if (allNodes.Count == 0)
        {
#if UNITY_6000_5_OR_NEWER
            GateNode[] foundNodes = FindObjectsByType<GateNode>();
#else
            GateNode[] foundNodes = FindObjectsByType<GateNode>(FindObjectsSortMode.None);
#endif
            allNodes.AddRange(foundNodes);
        }
    }

    private void Update()
    {
        EvaluateCircuit();
    }

    /// <summary>
    /// Mereset koneksi, mengurutkan kartu aktif dari kiri ke kanan relatif terhadap kamera,
    /// menghubungkan kartu secara spasial, mengevaluasi rambatan logika biner,
    /// dan memperbarui visual garis laser LineRenderer pada seluruh node.
    /// </summary>
    public void EvaluateCircuit()
    {
        if (allNodes == null || allNodes.Count == 0)
        {
            activeGateCardsCount = 0;
            return;
        }

        // 1. Reset koneksi pada seluruh node terlebih dahulu
        for (int i = 0; i < allNodes.Count; i++)
        {
            GateNode node = allNodes[i];
            if (node == null)
            {
                continue;
            }

            node.connectedInputs.Clear();
            node.connectedTarget = null;
        }

        // 2. Kumpulkan node yang sedang aktif terlacak (isTracked == true) dan hitung kuota kartu gerbang
        trackedNodes.Clear();
        int gateCount = 0;

        for (int i = 0; i < allNodes.Count; i++)
        {
            GateNode node = allNodes[i];
            if (node != null && node.isTracked)
            {
                trackedNodes.Add(node);

                if (IsLogicGateCard(node.nodeType))
                {
                    gateCount++;
                }
            }
        }

        activeGateCardsCount = gateCount;

        // Urutkan node aktif dari kiri ke kanan berdasarkan sumbu X lokal kamera (tanpa alokasi closure per frame)
        Transform camTransform = ResolveMainCameraTransform();
        if (trackedNodes.Count > 1)
        {
            if (cachedNodeSortComparison == null)
            {
                cachedNodeSortComparison = CompareNodesByCameraX;
            }

            currentSortCameraTransform = camTransform;
            trackedNodes.Sort(cachedNodeSortComparison);
            currentSortCameraTransform = null;
        }

        // 3. Routing koneksi: Mode Udara manual (port-ke-port + fan-out) vs Mode Kartu v5 auto-routing spasial (dibekukan)
        if (useManualRouting)
        {
            ApplyManualConnections();
        }
        else
        {
            // 3A. Hubungkan seluruh kartu InputSource ke kartu Gerbang Logika terdekat (Greedy Closest-Pair).
            // Selama masih ada kartu gerbang logika aktif di rangkaian (activeGateCardsCount > 0),
            // InputSource TIDAK PERNAH menyambung langsung ke OutputActuator.
            float maxInputReach = maxSnapDistance * Mathf.Max(1f, inputSourceRangeMultiplier);
            RouteInputSources(camTransform, maxInputReach);

            // 3B. Hubungkan antar-kartu Gerbang Logika (Gate -> Gate) dan terakhir menuju OutputActuator (Gate -> Output).
            // Dipisahkan menjadi dua tahap agar gerbang yang masih membutuhkan input (misal AND setelah NOT pada Level 3)
            // selalu diprioritaskan daripada melompat langsung ke OutputActuator.
            RouteLogicGates(camTransform);
        }

        // 4. Evaluasi logika gerbang secara berurutan dari kiri ke kanan hingga nilai sinyal stabil
        int maxPasses = Mathf.Max(1, trackedNodes.Count);
        for (int pass = 0; pass < maxPasses; pass++)
        {
            bool anyChanged = false;
            for (int i = 0; i < trackedNodes.Count; i++)
            {
                bool previousValue = trackedNodes[i].currentOutputValue;
                trackedNodes[i].EvaluateGateLogic();
                if (trackedNodes[i].currentOutputValue != previousValue)
                {
                    anyChanged = true;
                }
            }

            if (!anyChanged)
            {
                break;
            }
        }

        // 5. Perbarui visual LineRenderer pada seluruh node (termasuk mematikan garis pada node yang tidak aktif)
        for (int i = 0; i < allNodes.Count; i++)
        {
            if (allNodes[i] != null)
            {
                allNodes[i].UpdateVisuals();
            }
        }
    }

    /// <summary>
    /// Memasangkan setiap kartu InputSource ke Gerbang Logika yang tersedia berdasarkan pasangan jarak terdekat.
    /// Jika activeGateCardsCount > 0, target bertipe OutputActuator diblokir sepenuhnya.
    /// </summary>
    private void RouteInputSources(Transform camTransform, float maxInputReach)
    {
        bool hasActiveLogicGates = activeGateCardsCount > 0;

        // Tahap 1: Utamakan pasangan dengan target di sebelah kanan (toleransi -0.15 untuk susunan zig-zag sejajar)
        AssignClosestInputPairs(camTransform, maxInputReach, hasActiveLogicGates, requireRightward: true);

        // Tahap 2 (fallback): Jika masih ada InputSource belum tersambung pada susunan zig-zag vertikal, izinkan semua arah
        AssignClosestInputPairs(camTransform, maxInputReach, hasActiveLogicGates, requireRightward: false);
    }

    private void AssignClosestInputPairs(Transform camTransform, float maxRange, bool hasActiveLogicGates, bool requireRightward)
    {
        const float HorizontalAlignmentTolerance = 0.15f;

        while (true)
        {
            GateNode bestSource = null;
            GateNode bestTarget = null;
            float bestDistance = maxRange;

            for (int i = 0; i < trackedNodes.Count; i++)
            {
                GateNode source = trackedNodes[i];
                if (source.nodeType != NodeType.InputSource || source.connectedTarget != null)
                {
                    continue;
                }

                float sourceX = GetCameraRelativeX(source.transform.position, camTransform);

                for (int j = 0; j < trackedNodes.Count; j++)
                {
                    GateNode candidate = trackedNodes[j];
                    if (candidate == source || candidate.nodeType == NodeType.InputSource)
                    {
                        continue;
                    }

                    // Aturan mutlak: selama ada kartu gerbang logika aktif, InputSource dilarang menyambung langsung ke OutputActuator
                    if (hasActiveLogicGates && candidate.nodeType == NodeType.OutputActuator)
                    {
                        continue;
                    }

                    // Jika masih ada gerbang logika aktif, hanya izinkan target bertipe gerbang logika
                    if (hasActiveLogicGates && !IsLogicGateCard(candidate.nodeType))
                    {
                        continue;
                    }

                    if (candidate.connectedInputs.Count >= candidate.GetMaxInputCount())
                    {
                        continue;
                    }

                    if (requireRightward)
                    {
                        float candidateX = GetCameraRelativeX(candidate.transform.position, camTransform);
                        if (candidateX < sourceX - HorizontalAlignmentTolerance)
                        {
                            continue;
                        }
                    }

                    float distance = Vector3.Distance(source.transform.position, candidate.transform.position);
                    if (distance <= bestDistance)
                    {
                        bestDistance = distance;
                        bestSource = source;
                        bestTarget = candidate;
                    }
                }
            }

            if (bestSource == null || bestTarget == null)
            {
                break;
            }

            bestSource.connectedTarget = bestTarget;
            bestTarget.connectedInputs.Add(bestSource);
        }
    }

    /// <summary>
    /// Menghubungkan kartu Gerbang Logika secara berjenjang:
    /// 1) Gate ke Gate lain yang masih membutuhkan input (termasuk dukungan zig-zag diagonal/vertikal).
    /// 2) Gate akhir menuju OutputActuator.
    /// </summary>
    private void RouteLogicGates(Transform camTransform)
    {
        // Tahap 3B.1: Prioritaskan koneksi antar-gerbang logika (kanan dahulu, lalu fallback segala arah bebas siklus)
        RouteGatesToTargets(camTransform, targetGatesOnly: true, requireRightward: true);
        RouteGatesToTargets(camTransform, targetGatesOnly: true, requireRightward: false);

        // Tahap 3B.2: Hubungkan gerbang yang belum memiliki target ke OutputActuator (dari kanan ke kiri terlebih dahulu)
        RouteGatesToTargets(camTransform, targetGatesOnly: false, requireRightward: true);
        RouteGatesToTargets(camTransform, targetGatesOnly: false, requireRightward: false);
    }

    private void RouteGatesToTargets(Transform camTransform, bool targetGatesOnly, bool requireRightward)
    {
        const float HorizontalAlignmentTolerance = 0.15f;

        for (int i = trackedNodes.Count - 1; i >= 0; i--)
        {
            GateNode source = trackedNodes[i];
            if (!IsLogicGateCard(source.nodeType) || source.connectedTarget != null)
            {
                continue;
            }

            float sourceX = GetCameraRelativeX(source.transform.position, camTransform);
            GateNode bestTarget = null;
            float closestDistance = maxSnapDistance;

            for (int j = 0; j < trackedNodes.Count; j++)
            {
                GateNode candidate = trackedNodes[j];
                if (candidate == null || candidate == source || candidate.nodeType == NodeType.InputSource)
                {
                    continue;
                }

                if (targetGatesOnly)
                {
                    if (!IsLogicGateCard(candidate.nodeType))
                    {
                        continue;
                    }
                }
                else if (candidate.nodeType != NodeType.OutputActuator)
                {
                    continue;
                }

                if (candidate.connectedInputs.Count >= candidate.GetMaxInputCount())
                {
                    continue;
                }

                if (requireRightward)
                {
                    float candidateX = GetCameraRelativeX(candidate.transform.position, camTransform);
                    if (candidateX < sourceX - HorizontalAlignmentTolerance)
                    {
                        continue;
                    }
                }

                if (WouldCreateCycle(source, candidate))
                {
                    continue;
                }

                float distance = Vector3.Distance(source.transform.position, candidate.transform.position);
                if (distance <= closestDistance)
                {
                    closestDistance = distance;
                    bestTarget = candidate;
                }
            }

            if (bestTarget != null)
            {
                source.connectedTarget = bestTarget;
                bestTarget.connectedInputs.Add(source);
            }
        }
    }

    /// <summary>
    /// Memastikan penyambungan source -> target tidak membentuk siklus loop pada susunan zig-zag.
    /// </summary>
    private bool WouldCreateCycle(GateNode source, GateNode target)
    {
        GateNode current = target;
        int safetyCounter = trackedNodes.Count + 1;

        while (current != null && safetyCounter > 0)
        {
            if (current == source)
            {
                return true;
            }

            current = current.connectedTarget;
            safetyCounter--;
        }

        return false;
    }

    public void SetManualRoutingMode(bool enabled)
    {
        useManualRouting = enabled;
        if (!enabled)
        {
            manualConnections.Clear();
        }
    }

    public void SetManualConnections(List<ManualConnectionEdge> edges)
    {
        manualConnections.Clear();
        if (edges == null)
        {
            return;
        }

        for (int i = 0; i < edges.Count; i++)
        {
            manualConnections.Add(edges[i]);
        }
    }

    /// <summary>
    /// Mengecek apakah penambahan kabel manual source -> target membentuk siklus pada DAG fan-out (zero-GC).
    /// </summary>
    public bool WouldManualConnectionCreateCycle(GateNode candidateSource, GateNode candidateTarget)
    {
        if (candidateSource == null || candidateTarget == null)
        {
            return true;
        }
        if (candidateSource == candidateTarget)
        {
            return true;
        }

        cycleQueue.Clear();
        cycleVisited.Clear();
        cycleQueue.Add(candidateTarget);
        cycleVisited.Add(candidateTarget);

        int head = 0;
        while (head < cycleQueue.Count)
        {
            GateNode current = cycleQueue[head++];
            for (int i = 0; i < manualConnections.Count; i++)
            {
                ManualConnectionEdge edge = manualConnections[i];
                if (edge.source != current || edge.target == null)
                {
                    continue;
                }
                if (edge.target == candidateSource)
                {
                    return true;
                }
                if (!cycleVisited.Contains(edge.target))
                {
                    cycleVisited.Add(edge.target);
                    cycleQueue.Add(edge.target);
                }
            }
        }

        return false;
    }

    private void ApplyManualConnections()
    {
        for (int slotPass = 0; slotPass < 2; slotPass++)
        {
            for (int i = 0; i < manualConnections.Count; i++)
            {
                ManualConnectionEdge edge = manualConnections[i];
                int normalizedSlot = edge.targetSlotIndex == 1 ? 1 : 0;
                if (normalizedSlot != slotPass)
                {
                    continue;
                }

                GateNode src = edge.source;
                GateNode dst = edge.target;
                if (src == null || dst == null || src == dst || !src.isTracked || !dst.isTracked)
                {
                    continue;
                }
                if (src.nodeType == NodeType.OutputActuator || dst.nodeType == NodeType.InputSource)
                {
                    continue;
                }
                if (dst.connectedInputs.Count >= dst.GetMaxInputCount())
                {
                    continue;
                }
                // Pengaman lapis kedua anti parallel duplicate edge: tolak pasangan (source, target) identik di slot kedua (zero-GC)
                bool duplicateOnTarget = false;
                for (int k = 0; k < dst.connectedInputs.Count; k++)
                {
                    if (dst.connectedInputs[k] == src)
                    {
                        duplicateOnTarget = true;
                        break;
                    }
                }
                if (duplicateOnTarget)
                {
                    continue;
                }
                if (WouldAppliedInputsCreateCycle(src, dst))
                {
                    continue;
                }

                if (src.connectedTarget == null)
                {
                    src.connectedTarget = dst;
                }
                dst.connectedInputs.Add(src);
            }
        }
    }

    private bool WouldAppliedInputsCreateCycle(GateNode source, GateNode target)
    {
        cycleQueue.Clear();
        cycleVisited.Clear();
        cycleQueue.Add(source);
        cycleVisited.Add(source);

        int head = 0;
        while (head < cycleQueue.Count)
        {
            GateNode current = cycleQueue[head++];
            List<GateNode> inputs = current.connectedInputs;
            for (int i = 0; i < inputs.Count; i++)
            {
                GateNode prev = inputs[i];
                if (prev == null)
                {
                    continue;
                }
                if (prev == target)
                {
                    return true;
                }
                if (!cycleVisited.Contains(prev))
                {
                    cycleVisited.Add(prev);
                    cycleQueue.Add(prev);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Mengembalikan Transform kamera utama yang di-cache untuk menghindari pencarian tag tiap frame.
    /// </summary>
    private Transform ResolveMainCameraTransform()
    {
        if (cachedMainCamera == null || !cachedMainCamera.isActiveAndEnabled)
        {
            cachedMainCamera = Camera.main;
        }

        return cachedMainCamera != null ? cachedMainCamera.transform : null;
    }

    private int CompareNodesByCameraX(GateNode a, GateNode b)
    {
        float xA = GetCameraRelativeX(a.transform.position, currentSortCameraTransform);
        float xB = GetCameraRelativeX(b.transform.position, currentSortCameraTransform);
        return xA.CompareTo(xB);
    }

    /// <summary>
    /// Menghitung posisi horizontal (sumbu X) kartu relatif terhadap sudut pandang kamera.
    /// </summary>
    private float GetCameraRelativeX(Vector3 worldPosition, Transform cameraTransform)
    {
        if (cameraTransform != null)
        {
            return cameraTransform.InverseTransformPoint(worldPosition).x;
        }

        return worldPosition.x;
    }

    /// <summary>
    /// Mengecek apakah tipe kartu termasuk gerbang logika pemroses (AND, OR, NOT, XOR, NAND)
    /// yang dihitung dalam kuota kartu level.
    /// </summary>
    private bool IsLogicGateCard(NodeType type)
    {
        return type == NodeType.AND
               || type == NodeType.OR
               || type == NodeType.NOT
               || type == NodeType.XOR
               || type == NodeType.NAND;
    }

    /// <summary>
    /// Menelusuri pohon koneksi aktif mundur dari Card_Output hingga InputSource (dengan guard anti-siklus)
    /// untuk menghasilkan ekspresi aljabar Boolean aktual menggunakan karakter ASCII aman TMP.
    /// </summary>
    public string GetCircuitBooleanExpression(GateNode targetOutputNode = null)
    {
        GateNode outputNode = targetOutputNode;
        if (outputNode == null && allNodes != null)
        {
            for (int i = 0; i < allNodes.Count; i++)
            {
                if (allNodes[i] != null && allNodes[i].nodeType == NodeType.OutputActuator)
                {
                    outputNode = allNodes[i];
                    break;
                }
            }
        }

        if (outputNode == null
            || !outputNode.isTracked
            || outputNode.connectedInputs == null
            || outputNode.connectedInputs.Count == 0
            || outputNode.connectedInputs[0] == null
            || !outputNode.connectedInputs[0].isTracked)
        {
            return "Y = (Belum Terhubung)";
        }

        expressionActivePath.Clear();
        string bodyExpression = BuildBooleanExpressionRecursive(outputNode.connectedInputs[0]);
        expressionActivePath.Clear();

        return $"Y = {bodyExpression}";
    }

    /// <summary>
    /// Menghitung jumlah koneksi aktif antar-kartu yang saat ini terdeteksi dan saling ter-track.
    /// Digunakan oleh LevelAndUIManager untuk mendeteksi event snap koneksi baru bagi efek audio.
    /// </summary>
    public int GetActiveConnectionCount()
    {
        if (allNodes == null || allNodes.Count == 0)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < allNodes.Count; i++)
        {
            GateNode node = allNodes[i];
            if (node != null
                && node.isTracked
                && node.connectedTarget != null
                && node.connectedTarget.isTracked)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Memastikan rangkaian membentuk ekspresi aljabar Boolean utuh hingga Card_Output
    /// tanpa cabang kosong ('?'), tanpa siklus, dan bukan kondisi belum terhubung.
    /// </summary>
    public bool IsCircuitCompleteToOutput(GateNode targetOutputNode = null)
    {
        string expression = GetCircuitBooleanExpression(targetOutputNode);
        return !string.IsNullOrEmpty(expression)
            && expression != "Y = (Belum Terhubung)"
            && !expression.Contains("?")
            && !expression.Contains("(Siklus)");
    }

    private string BuildBooleanExpressionRecursive(GateNode node)
    {
        if (node == null || !node.isTracked)
        {
            return "?";
        }

        if (expressionActivePath.Contains(node))
        {
            return "(Siklus)";
        }

        expressionActivePath.Add(node);

        string result;
        switch (node.nodeType)
        {
            case NodeType.InputSource:
                result = node.gameObject.name.ToUpperInvariant().Contains("B") ? "B" : "A";
                break;

            case NodeType.NOT:
                if (node.connectedInputs == null || node.connectedInputs.Count == 0 || node.connectedInputs[0] == null)
                {
                    result = "(NOT ?)";
                }
                else
                {
                    string operand = BuildBooleanExpressionRecursive(node.connectedInputs[0]);
                    if (operand.StartsWith("(") && operand.EndsWith(")"))
                    {
                        result = "NOT" + operand;
                    }
                    else
                    {
                        result = $"(NOT {operand})";
                    }
                }
                break;

            case NodeType.AND:
            case NodeType.OR:
            case NodeType.XOR:
            case NodeType.NAND:
                string opLabel = GetGateOperatorLabel(node.nodeType);
                int inputCount = node.connectedInputs != null ? node.connectedInputs.Count : 0;
                if (inputCount == 0)
                {
                    result = $"(? {opLabel} ?)";
                }
                else if (inputCount == 1)
                {
                    string leftOnly = BuildBooleanExpressionRecursive(node.connectedInputs[0]);
                    result = $"({leftOnly} {opLabel} ?)";
                }
                else
                {
                    string leftExpr = BuildBooleanExpressionRecursive(node.connectedInputs[0]);
                    string rightExpr = BuildBooleanExpressionRecursive(node.connectedInputs[1]);

                    // Normalisasi urutan agar kombinasi murni A & B terbaca standar "(A <OP> B)"
                    if (leftExpr == "B" && rightExpr == "A")
                    {
                        leftExpr = "A";
                        rightExpr = "B";
                    }

                    result = $"({leftExpr} {opLabel} {rightExpr})";
                }
                break;

            case NodeType.OutputActuator:
                result = (node.connectedInputs != null && node.connectedInputs.Count > 0 && node.connectedInputs[0] != null)
                    ? BuildBooleanExpressionRecursive(node.connectedInputs[0])
                    : "?";
                break;

            default:
                result = "?";
                break;
        }

        if (expressionActivePath.Count > 0)
        {
            expressionActivePath.RemoveAt(expressionActivePath.Count - 1);
        }
        return result;
    }

    private static string GetGateOperatorLabel(NodeType type)
    {
        switch (type)
        {
            case NodeType.AND: return "AND";
            case NodeType.OR: return "OR";
            case NodeType.XOR: return "XOR";
            case NodeType.NAND: return "NAND";
            default: return type.ToString();
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (allNodes == null)
        {
            return;
        }

        for (int i = 0; i < allNodes.Count; i++)
        {
            GateNode node = allNodes[i];
            if (node != null && node.isTracked)
            {
                float radius = (node.nodeType == NodeType.InputSource)
                    ? maxSnapDistance * Mathf.Max(1f, inputSourceRangeMultiplier)
                    : maxSnapDistance;

                Gizmos.color = (node.nodeType == NodeType.InputSource)
                    ? new Color(0.2f, 1f, 0.4f, 0.2f)
                    : new Color(0f, 1f, 1f, 0.25f);

                Gizmos.DrawWireSphere(node.transform.position, radius);
            }
        }
    }
#endif
}
