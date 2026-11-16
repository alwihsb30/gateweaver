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

    [HideInInspector]
    public LineRenderer lineRenderer;

    private Coroutine debounceCoroutine;
    private bool lastDetectedRotationBit = false;
    private bool rotationInitialized = false;
    private bool isSimulationOverride = false;

    private static readonly Color BrightGreenTextColor = new Color(0.2f, 1f, 0.3f, 1f);
    private static readonly Color BrightRedOrangeTextColor = new Color(1f, 0.35f, 0.15f, 1f);

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null)
        {
            lineRenderer = gameObject.AddComponent<LineRenderer>();
        }

        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = true;
        lineRenderer.startWidth = laserWidth;
        lineRenderer.endWidth = laserWidth;
        lineRenderer.enabled = false;

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
        if (nodeType == NodeType.InputSource && isTracked && enablePhysicalRotationInput && !isSimulationOverride)
        {
            CheckPhysicalRotationInput();
        }

        if (isTracked)
        {
            UpdateLabelBillboard();
        }
    }

    private void UpdateLabelBillboard()
    {
        if (statusLabel == null || Camera.main == null)
        {
            return;
        }

        Vector3 dirFromCamera = statusLabel.transform.position - Camera.main.transform.position;
        if (dirFromCamera.sqrMagnitude > 0.0001f)
        {
            statusLabel.transform.rotation = Quaternion.LookRotation(dirFromCamera, Camera.main.transform.up);
        }
    }

    private void CheckPhysicalRotationInput()
    {
        if (Camera.main == null)
        {
            return;
        }

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
        UpdateVisuals();
    }

    public void SetSimulationOverride(bool active)
    {
        isSimulationOverride = active;
        rotationInitialized = false;
    }

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
        Color activeColor = currentOutputValue ? highSignalColor : lowSignalColor;

        if (lineRenderer != null)
        {
            if (isTracked && nodeType != NodeType.OutputActuator && connectedTarget != null && connectedTarget.isTracked)
            {
                lineRenderer.enabled = true;
                lineRenderer.startWidth = laserWidth;
                lineRenderer.endWidth = laserWidth;
                lineRenderer.startColor = activeColor;
                lineRenderer.endColor = activeColor;
                lineRenderer.SetPosition(0, transform.position);
                lineRenderer.SetPosition(1, connectedTarget.transform.position);
            }
            else
            {
                lineRenderer.enabled = false;
            }
        }

        if (statusIndicatorRenderer != null && statusIndicatorRenderer.material != null)
        {
            statusIndicatorRenderer.material.color = activeColor;
        }

        if (statusLabel != null)
        {
            if (statusLabel.gameObject.activeSelf != isTracked)
            {
                statusLabel.gameObject.SetActive(isTracked);
            }

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
