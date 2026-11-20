using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renderer visual Hologram Melayang Transparan gaya Iron Man HUD khusus Mode Udara (GateWeaver AR v7.0 Fase 3).
/// - Menggantikan nuansa lempeng alas abu-abu padat dengan cincin bingkai hologram tipis semi-transparan per node & rak parkir.
/// - Menampilkan bingkai identitas permanen Sian (Input A) dan Kuning-Emas (Input B), reaksi sorotan hover/grab/return-rack,
///   serta kursor reticle telapak anti-lompat (terbuka saat Open, terkunci saat Fist).
/// - 100% terisolasi di Mode Udara, shader Sprites/Default (aman OpenGLES3), Zero-GC, & seluruh material dibersihkan di OnDestroy().
/// </summary>
public sealed class AirWeavingHologramVisual : MonoBehaviour
{
    public enum NodeHoloState
    {
        Idle = 0,
        Parked = 1,
        BodyHovered = 2,
        Grabbed = 3,
        ReturningToRack = 4,
        PortFocused = 5
    }

    private const int HoloRingSegments = 16;
    private const int ReticleSegments = 20;
    private const int MaxNodeSlots = 8; // InputA, InputB, Output, + 5 Gerbang Logika

    private static readonly Color InputACyanIdleColor = new Color(0.18f, 0.88f, 1.00f, 0.72f);
    private static readonly Color InputACyanHoverColor = new Color(0.45f, 0.98f, 1.00f, 1.00f);
    private static readonly Color InputBGoldIdleColor = new Color(1.00f, 0.82f, 0.22f, 0.76f);
    private static readonly Color InputBGoldHoverColor = new Color(1.00f, 0.94f, 0.46f, 1.00f);
    private static readonly Color OutputTealIdleColor = new Color(0.24f, 0.84f, 0.94f, 0.56f);
    private static readonly Color GateIdleHoloColor = new Color(0.20f, 0.86f, 1.00f, 0.48f);
    private static readonly Color GateParkedHoloColor = new Color(0.24f, 0.82f, 0.96f, 0.34f);
    private static readonly Color GateBodyHoverColor = new Color(0.42f, 0.98f, 1.00f, 0.96f);
    private static readonly Color GateGrabbedColor = new Color(1.00f, 0.88f, 0.28f, 0.98f);
    private static readonly Color GateReturnRackColor = new Color(1.00f, 0.54f, 0.22f, 0.98f);
    private static readonly Color GatePortDimColor = new Color(0.18f, 0.72f, 0.88f, 0.26f);

    private static readonly Color ReticleOpenColor = new Color(0.45f, 0.98f, 1.00f, 1.00f);
    private static readonly Color ReticlePortAimColor = new Color(0.55f, 1.00f, 0.85f, 1.00f);
    private static readonly Color ReticleFistColor = new Color(0.84f, 1.00f, 0.30f, 1.00f);
    private static readonly Color ReticleCoreWhiteColor = new Color(1.00f, 1.00f, 1.00f, 1.00f);

    private GameObject rootObj;
    private LineRenderer[] nodeOuterRings;
    private LineRenderer[] nodeInnerRings;
    private Material[] nodeOuterMats;
    private Material[] nodeInnerMats;
    private readonly Vector3[] ringBuffer = new Vector3[HoloRingSegments];

    private LineRenderer reticleOuterRenderer;
    private LineRenderer reticleInnerRenderer;
    private LineRenderer reticleCrossHRenderer;
    private LineRenderer reticleCrossVRenderer;
    private Material reticleOuterMat;
    private Material reticleInnerMat;
    private Material reticleCrossMat;
    private readonly Vector3[] reticleBuffer = new Vector3[ReticleSegments];

    private readonly List<Material> ownedMaterials = new List<Material>(24);
    private bool isInitialized = false;

    public void EnsureVisualsCreated()
    {
        if (isInitialized)
        {
            return;
        }

        rootObj = new GameObject("AR_AirWeavingHologramHUD");
        rootObj.transform.SetParent(transform, false);

        nodeOuterRings = new LineRenderer[MaxNodeSlots];
        nodeInnerRings = new LineRenderer[MaxNodeSlots];
        nodeOuterMats = new Material[MaxNodeSlots];
        nodeInnerMats = new Material[MaxNodeSlots];

        for (int i = 0; i < MaxNodeSlots; i++)
        {
            nodeOuterMats[i] = CreateSafeSpritesMat(GateIdleHoloColor, 3006);
            nodeInnerMats[i] = CreateSafeSpritesMat(GateParkedHoloColor, 3005);
            nodeOuterRings[i] = CreateLoopLine($"Holo_NodeOuter_{i}", HoloRingSegments, 0.0055f, nodeOuterMats[i]);
            nodeInnerRings[i] = CreateLoopLine($"Holo_NodeInner_{i}", HoloRingSegments, 0.0032f, nodeInnerMats[i]);
        }

        reticleOuterMat = CreateSafeSpritesMat(ReticleOpenColor, 3040);
        reticleInnerMat = CreateSafeSpritesMat(ReticleCoreWhiteColor, 3041);
        reticleCrossMat = CreateSafeSpritesMat(ReticleCoreWhiteColor, 3042);
        reticleOuterRenderer = CreateLoopLine("Holo_PalmReticleOuter", ReticleSegments, 0.009f, reticleOuterMat);
        reticleInnerRenderer = CreateLoopLine("Holo_PalmReticleInner", ReticleSegments, 0.0065f, reticleInnerMat);
        reticleCrossHRenderer = CreateSegmentLine("Holo_PalmReticleCrossH", 0.006f, reticleCrossMat);
        reticleCrossVRenderer = CreateSegmentLine("Holo_PalmReticleCrossV", 0.006f, reticleCrossMat);

        isInitialized = true;
        HideImmediate();
    }

    public void HideImmediate()
    {
        if (!isInitialized)
        {
            return;
        }

        if (rootObj != null && rootObj.activeSelf)
        {
            rootObj.SetActive(false);
        }
        SetReticleVisible(false);
    }

    public void SetRootVisible(bool visible)
    {
        if (!isInitialized || rootObj == null)
        {
            return;
        }

        if (rootObj.activeSelf != visible)
        {
            rootObj.SetActive(visible);
        }
    }

    /// <summary>
    /// Memperbarui bingkai hologram 1 node pada slot indeks 0..7 (0=InputA, 1=InputB, 2=Output, 3..7=Gerbang). Zero-GC.
    /// </summary>
    public void UpdateNodeFrame(
        int slotIndex,
        GateNode node,
        bool isInputA,
        bool isInputB,
        bool isOutput,
        NodeHoloState state)
    {
        if (!isInitialized || slotIndex < 0 || slotIndex >= MaxNodeSlots)
        {
            return;
        }

        LineRenderer outer = nodeOuterRings[slotIndex];
        LineRenderer inner = nodeInnerRings[slotIndex];
        if (node == null || (!node.isTracked && state != NodeHoloState.Parked))
        {
            if (outer != null) outer.enabled = false;
            if (inner != null) inner.enabled = false;
            return;
        }

        Transform nodeT = node.transform;
        float nodeScale = nodeT.localScale.x;
        Vector3 center = nodeT.position + (nodeT.up * (0.015f * nodeScale));
        Vector3 axisA = nodeT.right;
        Vector3 axisB = nodeT.forward;

        float baseRadius = (state == NodeHoloState.Parked ? 0.305f : 0.335f) * nodeScale;
        float pulse = 0.5f + (0.5f * Mathf.Sin((Time.unscaledTime * 4.5f) + slotIndex));

        Color outerColor;
        float outerWidth = 0.0052f;
        bool showInner = true;

        if (isInputA)
        {
            outerColor = state == NodeHoloState.BodyHovered ? InputACyanHoverColor : InputACyanIdleColor;
            outerWidth = state == NodeHoloState.BodyHovered ? 0.0085f : 0.0062f;
            if (state == NodeHoloState.BodyHovered) baseRadius += 0.012f * pulse;
        }
        else if (isInputB)
        {
            outerColor = state == NodeHoloState.BodyHovered ? InputBGoldHoverColor : InputBGoldIdleColor;
            outerWidth = state == NodeHoloState.BodyHovered ? 0.0085f : 0.0062f;
            if (state == NodeHoloState.BodyHovered) baseRadius += 0.012f * pulse;
        }
        else if (isOutput)
        {
            outerColor = OutputTealIdleColor;
            outerWidth = 0.0055f;
        }
        else
        {
            switch (state)
            {
                case NodeHoloState.Grabbed:
                    outerColor = GateGrabbedColor;
                    outerWidth = 0.0085f;
                    baseRadius += 0.014f * pulse;
                    break;
                case NodeHoloState.ReturningToRack:
                    outerColor = GateReturnRackColor;
                    outerWidth = 0.0085f;
                    baseRadius += 0.014f * pulse;
                    break;
                case NodeHoloState.BodyHovered:
                    outerColor = GateBodyHoverColor;
                    outerWidth = 0.0080f;
                    baseRadius += 0.010f * pulse;
                    break;
                case NodeHoloState.PortFocused:
                    outerColor = GatePortDimColor;
                    outerWidth = 0.0040f;
                    showInner = false;
                    break;
                case NodeHoloState.Parked:
                    outerColor = GateParkedHoloColor;
                    outerWidth = 0.0040f;
                    showInner = false;
                    break;
                default:
                    outerColor = GateIdleHoloColor;
                    outerWidth = 0.0050f;
                    break;
            }
        }

        DrawChamferedRing(outer, nodeOuterMats[slotIndex], center, axisA, axisB, baseRadius, outerColor, outerWidth);

        if (showInner && inner != null)
        {
            Color innerColor = new Color(outerColor.r, outerColor.g, outerColor.b, outerColor.a * 0.45f);
            DrawChamferedRing(inner, nodeInnerMats[slotIndex], center, axisA, axisB, baseRadius * 0.82f, innerColor, 0.0030f);
        }
        else if (inner != null && inner.enabled)
        {
            inner.enabled = false;
        }
    }

    /// <summary>
    /// Memperbarui reticle kursor telapak tangan (terbuka saat Open, terkunci mengecil saat Fist). Zero-GC.
    /// </summary>
    public void UpdatePalmReticle(
        Transform camT,
        bool isHandDetected,
        bool isUiOverlayOpen,
        Vector3 palmWorldPos,
        bool isFistActive,
        bool isTargetingPort,
        float meanCurlRatio)
    {
        if (!isInitialized)
        {
            return;
        }

        bool show = isHandDetected && !isUiOverlayOpen && camT != null;
        SetReticleVisible(show);
        if (!show)
        {
            return;
        }

        Vector3 right = camT.right;
        Vector3 up = camT.up;
        // Dorong reticle di depan kerangka tangan agar kontras tinggi & tidak tertutup siluet jari
        Vector3 center = palmWorldPos - (camT.forward * 0.14f);

        float openFrac = Mathf.Clamp01((meanCurlRatio - 0.78f) / 0.75f);
        float outerRadius = isFistActive ? 0.036f : Mathf.Lerp(0.044f, 0.074f, openFrac);
        float innerRadius = isFistActive ? 0.015f : 0.018f;
        float crossHalf = isFistActive ? 0.022f : 0.030f;

        Color color = isFistActive
            ? ReticleFistColor
            : (isTargetingPort ? ReticlePortAimColor : ReticleOpenColor);

        DrawReticleCircle(reticleOuterRenderer, reticleOuterMat, center, right, up, outerRadius, color, isFistActive ? 0.013f : 0.009f);
        DrawReticleCircle(reticleInnerRenderer, reticleInnerMat, center - (camT.forward * 0.004f), right, up, innerRadius, ReticleCoreWhiteColor, isFistActive ? 0.009f : 0.006f);
        if (reticleCrossHRenderer != null && reticleCrossVRenderer != null)
        {
            Vector3 crossCenter = center - (camT.forward * 0.006f);
            reticleCrossHRenderer.enabled = true;
            reticleCrossHRenderer.SetPosition(0, crossCenter - (right * crossHalf));
            reticleCrossHRenderer.SetPosition(1, crossCenter + (right * crossHalf));
            reticleCrossVRenderer.enabled = true;
            reticleCrossVRenderer.SetPosition(0, crossCenter - (up * crossHalf));
            reticleCrossVRenderer.SetPosition(1, crossCenter + (up * crossHalf));
        }
    }

    private void SetReticleVisible(bool visible)
    {
        if (reticleOuterRenderer != null && reticleOuterRenderer.enabled != visible)
        {
            reticleOuterRenderer.enabled = visible;
        }
        if (reticleInnerRenderer != null && reticleInnerRenderer.enabled != visible)
        {
            reticleInnerRenderer.enabled = visible;
        }
        if (reticleCrossHRenderer != null && reticleCrossHRenderer.enabled != visible)
        {
            reticleCrossHRenderer.enabled = visible;
        }
        if (reticleCrossVRenderer != null && reticleCrossVRenderer.enabled != visible)
        {
            reticleCrossVRenderer.enabled = visible;
        }
    }

    private void DrawChamferedRing(
        LineRenderer lr,
        Material mat,
        Vector3 center,
        Vector3 axisA,
        Vector3 axisB,
        float radius,
        Color color,
        float width)
    {
        if (lr == null)
        {
            return;
        }

        for (int i = 0; i < HoloRingSegments; i++)
        {
            float angle = (i / (float)HoloRingSegments) * Mathf.PI * 2f;
            // Sedikit variasi oktagonal halus agar terasa seperti bingkai hologram HUD
            float octMod = 1f + (0.045f * Mathf.Cos(angle * 4f));
            float r = radius * octMod;
            ringBuffer[i] = center + (axisA * (Mathf.Cos(angle) * r)) + (axisB * (Mathf.Sin(angle) * r));
        }

        if (mat != null)
        {
            mat.color = color;
        }
        lr.enabled = true;
        lr.widthMultiplier = width;
        lr.startColor = color;
        lr.endColor = color;
        lr.SetPositions(ringBuffer);
    }

    private void DrawReticleCircle(
        LineRenderer lr,
        Material mat,
        Vector3 center,
        Vector3 axisA,
        Vector3 axisB,
        float radius,
        Color color,
        float width)
    {
        if (lr == null)
        {
            return;
        }

        for (int i = 0; i < ReticleSegments; i++)
        {
            float angle = (i / (float)ReticleSegments) * Mathf.PI * 2f;
            reticleBuffer[i] = center + (axisA * (Mathf.Cos(angle) * radius)) + (axisB * (Mathf.Sin(angle) * radius));
        }

        if (mat != null)
        {
            mat.color = color;
        }
        lr.enabled = true;
        lr.widthMultiplier = width;
        lr.startColor = color;
        lr.endColor = color;
        lr.SetPositions(reticleBuffer);
    }

    private LineRenderer CreateLoopLine(string objName, int segCount, float width, Material mat)
    {
        GameObject obj = new GameObject(objName);
        obj.transform.SetParent(rootObj.transform, false);
        LineRenderer lr = obj.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = true;
        lr.positionCount = segCount;
        lr.widthMultiplier = width;
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

    private LineRenderer CreateSegmentLine(string objName, float width, Material mat)
    {
        GameObject obj = new GameObject(objName);
        obj.transform.SetParent(rootObj.transform, false);
        LineRenderer lr = obj.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = false;
        lr.positionCount = 2;
        lr.widthMultiplier = width;
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
        nodeOuterRings = null;
        nodeInnerRings = null;
        nodeOuterMats = null;
        nodeInnerMats = null;
        reticleOuterRenderer = null;
        reticleInnerRenderer = null;
        reticleCrossHRenderer = null;
        reticleCrossVRenderer = null;
        reticleOuterMat = null;
        reticleInnerMat = null;
        reticleCrossMat = null;
    }
}
