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
    private const int DataPacketCount = 3;

    [Header("1. Identitas & Konfigurasi Kartu")]
    public NodeType nodeType = NodeType.InputSource;
    public bool initialInputState = false;

    [Header("2. Status Runtime & Koneksi Spasial")]
    public bool isTracked = false;
    public bool currentOutputValue = false;
    public List<GateNode> connectedInputs = new List<GateNode>();
    public GateNode connectedTarget = null;

    [Header("3. Pengaturan Visual Laser (LineRenderer)")]
    public float laserWidth = 0.015f;
    public Color highSignalColor = Color.green;
    public Color lowSignalColor = Color.red;

    [Header("4. Interaksi Rotasi Fisik & Anti-Flicker")]
    public bool enablePhysicalRotationInput = true;
    public float rotationAngleThreshold = 45f;
    public float trackingLossDebounceTime = 0.4f;

    [Header("5. Umpan Balik Visual 3D/AR di Atas Kartu")]
    public Renderer statusIndicatorRenderer;
    public TMP_Text statusLabel;
    public float labelHeightOffset = 0.045f;
    public float labelFontSize = 0.55f;

    [Header("6. Visual 3D Bezier Arc, Paket Data & Soket Terminal")]
    public float socketSurfaceOffset = 0.02f;
    public float arcHeightDistanceFactor = 0.45f;
    public float minArcHeight = 0.30f;
    public float maxArcHeight = 1.15f;
    public float packetHighScale = 0.055f;
    public float packetLowScale = 0.034f;
    public float packetHighSpeed = 1.35f;
    public float packetLowSpeed = 0.35f;
    public float socketRingDiameter = 0.105f;
    public float socketRingThickness = 0.006f;

    [HideInInspector]
    public LineRenderer lineRenderer;

    private GameObject[] dataPacketObjs;
    private GameObject sourceSocketObj;
    private GameObject targetSocketObj;

    private Material lineMaterial;
    private Material dataPacketMaterial;
    private Material socketMaterial;

    private Coroutine debounceCoroutine;
    private bool lastDetectedRotationBit = false;
    private bool rotationInitialized = false;
    private bool isSimulationOverride = false;
    private float packetFlowPhase = 0f;

    private static readonly float[] PacketPhaseOffsets = { 0f, 0.33f, 0.66f };
    private static readonly Color BrightGreenTextColor = new Color(0.2f, 1f, 0.3f, 1f);
    private static readonly Color BrightRedOrangeTextColor = new Color(1f, 0.35f, 0.15f, 1f);
    private static readonly Color HighPacketColor = new Color(0.15f, 1f, 0.85f, 1f);
    private static readonly Color LowPacketColor = new Color(0.75f, 0.24f, 0.08f, 1f);
    private static readonly Color HighSocketColor = new Color(0.15f, 1f, 0.65f, 1f);
    private static readonly Color LowSocketColor = new Color(0.65f, 0.18f, 0.1f, 1f);

    private void Awake()
    {
        InitializeLineRenderer();
        Initialize3DVisualFeedback();
        InitializeArcAndTerminalVisuals();
    }

    private void Start()
    {
        if (nodeType == NodeType.InputSource) currentOutputValue = initialInputState;
        EvaluateGateLogic();
        UpdateVisuals();
    }

    private void Update()
    {
        if (nodeType == NodeType.InputSource && isTracked && enablePhysicalRotationInput && !isSimulationOverride)
        {
            CheckPhysicalRotationInput();
        }

        if (isTracked) UpdateLabelBillboard();
        RefreshConnectionVisuals(advanceFlowPhase: true);
    }

    private void OnDestroy()
    {
        if (lineMaterial != null) Destroy(lineMaterial);
        if (dataPacketMaterial != null) Destroy(dataPacketMaterial);
        if (socketMaterial != null) Destroy(socketMaterial);
    }

    private void InitializeLineRenderer()
    {
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null) lineRenderer = gameObject.AddComponent<LineRenderer>();

        lineMaterial = CreateSafeOpenGLMaterial();
        if (lineMaterial != null)
        {
            lineMaterial.color = Color.white;
            lineRenderer.material = lineMaterial;
        }

        lineRenderer.positionCount = ArcSegmentCount;
        lineRenderer.useWorldSpace = true;
        lineRenderer.numCapVertices = 4;
        lineRenderer.numCornerVertices = 4;
        lineRenderer.widthMultiplier = laserWidth;
        lineRenderer.widthCurve = new AnimationCurve(
            new Keyframe(0f, 0.5f),
            new Keyframe(0.5f, 1.8f),
            new Keyframe(1f, 0.5f)
        );
        lineRenderer.enabled = false;
    }

    private Material CreateSafeOpenGLMaterial()
    {
        Shader safeShader = Shader.Find("Sprites/Default");
        if (safeShader != null) return new Material(safeShader);
        if (lineRenderer != null && lineRenderer.sharedMaterial != null) return new Material(lineRenderer.sharedMaterial);
        Shader uiFallbackShader = Shader.Find("UI/Default");
        return uiFallbackShader != null ? new Material(uiFallbackShader) : null;
    }

    private void Initialize3DVisualFeedback()
    {
        if (statusLabel == null)
        {
            GameObject labelObj = new GameObject("Label_Status3D");
            labelObj.transform.SetParent(transform, false);
            labelObj.transform.localPosition = new Vector3(0f, labelHeightOffset, 0f);
            TextMeshPro tmp3D = labelObj.AddComponent<TextMeshPro>();
            tmp3D.fontSize = labelFontSize;
            tmp3D.alignment = TextAlignmentOptions.Center;
            tmp3D.textWrappingMode = TextWrappingModes.NoWrap;
            tmp3D.fontStyle = FontStyles.Bold;
            statusLabel = tmp3D;
        }
    }

    private void InitializeArcAndTerminalVisuals()
    {
        if (nodeType == NodeType.OutputActuator) return;

        dataPacketMaterial = CreateSafeOpenGLMaterial();
        socketMaterial = CreateSafeOpenGLMaterial();

        dataPacketObjs = new GameObject[DataPacketCount];
        for (int i = 0; i < DataPacketCount; i++)
        {
            GameObject packet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            packet.name = $"DataPacket_{i}";
            packet.transform.SetParent(transform, false);
            packet.transform.localScale = Vector3.one * packetLowScale;
            Collider col = packet.GetComponent<Collider>();
            if (col != null) Destroy(col);
            Renderer rend = packet.GetComponent<Renderer>();
            if (rend != null && dataPacketMaterial != null) rend.sharedMaterial = dataPacketMaterial;
            packet.SetActive(false);
            dataPacketObjs[i] = packet;
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
        Collider col = ringObj.GetComponent<Collider>();
        if (col != null) Destroy(col);
        Renderer rend = ringObj.GetComponent<Renderer>();
        if (rend != null && socketMaterial != null) rend.sharedMaterial = socketMaterial;
        ringObj.SetActive(false);
        return ringObj;
    }

    private bool TryGetBezierControlPoints(out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 sourceNormal, out Vector3 targetNormal)
    {
        bool canDraw = isTracked && nodeType != NodeType.OutputActuator && connectedTarget != null && connectedTarget.isTracked;
        if (!canDraw)
        {
            p0 = p1 = p2 = Vector3.zero;
            sourceNormal = targetNormal = Vector3.up;
            return false;
        }

        sourceNormal = transform.up.sqrMagnitude > 0.0001f ? transform.up.normalized : Vector3.up;
        targetNormal = connectedTarget.transform.up.sqrMagnitude > 0.0001f ? connectedTarget.transform.up.normalized : Vector3.up;

        int targetSlotIndex = connectedTarget.connectedInputs != null ? connectedTarget.connectedInputs.IndexOf(this) : -1;
        float targetOffset = socketSurfaceOffset + (targetSlotIndex > 0 ? targetSlotIndex * 0.003f : 0f);

        p0 = transform.position + sourceNormal * socketSurfaceOffset;
        p2 = connectedTarget.transform.position + targetNormal * targetOffset;
        Vector3 avgNormal = (sourceNormal + targetNormal).sqrMagnitude > 0.0001f ? (sourceNormal + targetNormal).normalized : sourceNormal;
        float arcHeight = Mathf.Clamp(Vector3.Distance(p0, p2) * arcHeightDistanceFactor, minArcHeight, maxArcHeight);
        p1 = ((p0 + p2) * 0.5f) + (avgNormal * arcHeight);
        return true;
    }

    private static Vector3 EvaluateQuadraticBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        float oneMinusT = 1f - t;
        return (oneMinusT * oneMinusT * p0) + (2f * oneMinusT * t * p1) + (t * t * p2);
    }

    private void RefreshConnectionVisuals(bool advanceFlowPhase)
    {
        if (!TryGetBezierControlPoints(out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 sourceNormal, out Vector3 targetNormal))
        {
            SetConnectionVisualsActive(false);
            return;
        }

        Color activeLaserColor = currentOutputValue ? highSignalColor : lowSignalColor;

        if (lineRenderer != null)
        {
            lineRenderer.enabled = true;
            lineRenderer.positionCount = ArcSegmentCount;
            lineRenderer.widthMultiplier = laserWidth;
            lineRenderer.startColor = activeLaserColor;
            lineRenderer.endColor = activeLaserColor;

            for (int i = 0; i < ArcSegmentCount; i++)
            {
                float t = i / (float)(ArcSegmentCount - 1);
                lineRenderer.SetPosition(i, EvaluateQuadraticBezier(p0, p1, p2, t));
            }
        }

        if (advanceFlowPhase)
        {
            float flowSpeed = currentOutputValue ? packetHighSpeed : packetLowSpeed;
            packetFlowPhase = Mathf.Repeat(packetFlowPhase + flowSpeed * Time.deltaTime, 1f);
        }

        float packetScale = currentOutputValue ? packetHighScale : packetLowScale;
        if (dataPacketMaterial != null) dataPacketMaterial.color = currentOutputValue ? HighPacketColor : LowPacketColor;

        if (dataPacketObjs != null)
        {
            for (int i = 0; i < dataPacketObjs.Length; i++)
            {
                GameObject packet = dataPacketObjs[i];
                if (packet == null) continue;
                if (!packet.activeSelf) packet.SetActive(true);
                float phase = Mathf.Repeat(packetFlowPhase + PacketPhaseOffsets[i], 1f);
                packet.transform.position = EvaluateQuadraticBezier(p0, p1, p2, phase);
                packet.transform.localScale = Vector3.one * packetScale;
            }
        }

        float pulseMultiplier = currentOutputValue ? (1f + 0.18f * Mathf.Sin(Time.time * 6f)) : 1f;
        Vector3 socketScale = new Vector3(socketRingDiameter * pulseMultiplier, socketRingThickness, socketRingDiameter * pulseMultiplier);
        if (socketMaterial != null) socketMaterial.color = currentOutputValue ? HighSocketColor : LowSocketColor;

        if (sourceSocketObj != null)
        {
            if (!sourceSocketObj.activeSelf) sourceSocketObj.SetActive(true);
            sourceSocketObj.transform.position = p0;
            sourceSocketObj.transform.up = sourceNormal;
            sourceSocketObj.transform.localScale = socketScale;
        }

        if (targetSocketObj != null)
        {
            if (!targetSocketObj.activeSelf) targetSocketObj.SetActive(true);
            targetSocketObj.transform.position = p2;
            targetSocketObj.transform.up = targetNormal;
            targetSocketObj.transform.localScale = socketScale;
        }
    }

    private void SetConnectionVisualsActive(bool active)
    {
        if (lineRenderer != null && lineRenderer.enabled != active) lineRenderer.enabled = active;
        if (dataPacketObjs != null)
        {
            for (int i = 0; i < dataPacketObjs.Length; i++)
            {
                if (dataPacketObjs[i] != null && dataPacketObjs[i].activeSelf != active) dataPacketObjs[i].SetActive(active);
            }
        }
        if (sourceSocketObj != null && sourceSocketObj.activeSelf != active) sourceSocketObj.SetActive(active);
        if (targetSocketObj != null && targetSocketObj.activeSelf != active) targetSocketObj.SetActive(active);
    }

    private void UpdateLabelBillboard()
    {
        if (statusLabel == null || Camera.main == null) return;
        Vector3 dir = statusLabel.transform.position - Camera.main.transform.position;
        if (dir.sqrMagnitude > 0.0001f) statusLabel.transform.rotation = Quaternion.LookRotation(dir, Camera.main.transform.up);
    }

    private void CheckPhysicalRotationInput()
    {
        if (Camera.main == null) return;
        Vector3 localCardForward = Camera.main.transform.InverseTransformDirection(transform.forward);
        float angleFromVertical = Mathf.Abs(Mathf.Atan2(localCardForward.x, localCardForward.y) * Mathf.Rad2Deg);
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

    public void SetTrackingState(bool tracked)
    {
        if (tracked)
        {
            if (debounceCoroutine != null) { StopCoroutine(debounceCoroutine); debounceCoroutine = null; }
            isTracked = true;
            EvaluateGateLogic();
            UpdateVisuals();
        }
        else
        {
            if (Application.isPlaying && gameObject.activeInHierarchy && trackingLossDebounceTime > 0f)
            {
                if (debounceCoroutine != null) StopCoroutine(debounceCoroutine);
                debounceCoroutine = StartCoroutine(TrackingLossDebounceRoutine());
            }
            else
            {
                ApplyTrackingLost();
            }
        }
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
        connectedInputs.Clear();
        connectedTarget = null;
        SetConnectionVisualsActive(false);
        UpdateVisuals();
    }

    public void SetSimulationOverride(bool active)
    {
        isSimulationOverride = active;
        rotationInitialized = false;
    }

    public void SetInputState(bool state)
    {
        if (nodeType != NodeType.InputSource) return;
        initialInputState = state;
        currentOutputValue = state;
        rotationInitialized = false;
        EvaluateGateLogic();
        UpdateVisuals();
    }

    public void ToggleInputState()
    {
        if (nodeType != NodeType.InputSource) return;
        initialInputState = !initialInputState;
        currentOutputValue = initialInputState;
        rotationInitialized = false;
        EvaluateGateLogic();
        UpdateVisuals();
    }

    public int GetMaxInputCount()
    {
        switch (nodeType)
        {
            case NodeType.InputSource: return 0;
            case NodeType.NOT:
            case NodeType.OutputActuator: return 1;
            case NodeType.AND:
            case NodeType.OR:
            case NodeType.XOR:
            case NodeType.NAND: return 2;
            default: return 0;
        }
    }

    public void EvaluateGateLogic()
    {
        switch (nodeType)
        {
            case NodeType.InputSource:
                currentOutputValue = initialInputState;
                break;
            case NodeType.NOT:
                currentOutputValue = connectedInputs.Count >= 1 && connectedInputs[0] != null && !connectedInputs[0].currentOutputValue;
                break;
            case NodeType.OutputActuator:
                currentOutputValue = connectedInputs.Count >= 1 && connectedInputs[0] != null && connectedInputs[0].currentOutputValue;
                break;
            case NodeType.AND:
                currentOutputValue = connectedInputs.Count >= 2 && connectedInputs[0] != null && connectedInputs[1] != null
                    && connectedInputs[0].currentOutputValue && connectedInputs[1].currentOutputValue;
                break;
            case NodeType.OR:
                currentOutputValue = connectedInputs.Count >= 2 && connectedInputs[0] != null && connectedInputs[1] != null
                    && (connectedInputs[0].currentOutputValue || connectedInputs[1].currentOutputValue);
                break;
            case NodeType.XOR:
                currentOutputValue = connectedInputs.Count >= 2 && connectedInputs[0] != null && connectedInputs[1] != null
                    && (connectedInputs[0].currentOutputValue ^ connectedInputs[1].currentOutputValue);
                break;
            case NodeType.NAND:
                currentOutputValue = connectedInputs.Count >= 2 && connectedInputs[0] != null && connectedInputs[1] != null
                    && !(connectedInputs[0].currentOutputValue && connectedInputs[1].currentOutputValue);
                break;
        }

        UpdateVisuals();
    }

    public void UpdateVisuals()
    {
        Color activeLaserColor = currentOutputValue ? highSignalColor : lowSignalColor;
        RefreshConnectionVisuals(advanceFlowPhase: false);

        if (statusIndicatorRenderer != null && statusIndicatorRenderer.material != null)
        {
            statusIndicatorRenderer.material.color = activeLaserColor;
        }

        if (statusLabel != null)
        {
            if (statusLabel.gameObject.activeSelf != isTracked) statusLabel.gameObject.SetActive(isTracked);
            if (isTracked)
            {
                if (nodeType == NodeType.InputSource)
                {
                    string inputName = gameObject.name.ToUpperInvariant().Contains("B") ? "B" : "A";
                    statusLabel.text = $"INPUT {inputName}: {(currentOutputValue ? "1" : "0")}";
                }
                else if (nodeType == NodeType.OutputActuator)
                {
                    statusLabel.text = $"OUTPUT: {(currentOutputValue ? "1 (AKTIF)" : "0 (MATI)")}";
                }
                else
                {
                    statusLabel.text = $"{nodeType} -> {(currentOutputValue ? "1" : "0")}";
                }

                statusLabel.color = currentOutputValue ? BrightGreenTextColor : BrightRedOrangeTextColor;
                UpdateLabelBillboard();
            }
        }
    }
}
