using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Renderer visual Hologram 3D Melayang gaya Iron Man HUD khusus Mode Udara (GateWeaver AR v7.0 Polishing Final).
/// - Menghilangkan cincin lantai pipih (oval dasar) agar seluruh gerbang, sakelar Input, & Output benar-benar
///   terlihat melayang bebas di ruang 3D tanpa ilusi menempel pada meja.
/// - Sorotan hover/grab/buang memakai aura melingkar 3D menghadap kamera + efek ripple sentuhan telunjuk pada Input A/B.
/// - Kursor reticle telapak anti-lompat & indikator cincin cubit presisi otomatis tersembunyi saat tangan hilang/overlay.
/// - 100% terisolasi di Mode Udara, shader Sprites/Default (aman OpenGLES3), Zero-GC, & dibersihkan penuh di OnDestroy().
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

    private const int AuraSegments = 24;
    private const int ReticleSegments = 20;
    private const int PinchRingSegments = 16;
    private const int MaxNodeSlots = 8; // InputA, InputB, Output, + 5 Gerbang Logika

    private static readonly Color InputACyanHoverColor = new Color(0.38f, 0.96f, 1.00f, 0.96f);
    private static readonly Color InputBGoldHoverColor = new Color(1.00f, 0.88f, 0.30f, 0.96f);
    private static readonly Color GateBodyHoverColor = new Color(0.42f, 0.98f, 1.00f, 0.92f);
    private static readonly Color GateGrabbedColor = new Color(1.00f, 0.88f, 0.28f, 0.96f);
    private static readonly Color GateOutsideDeleteRedColor = new Color(1.00f, 0.22f, 0.22f, 1.00f);

    private static readonly Color ReticleOpenColor = new Color(0.45f, 0.98f, 1.00f, 1.00f);
    private static readonly Color ReticlePortAimColor = new Color(0.55f, 1.00f, 0.85f, 1.00f);
    private static readonly Color ReticleFistColor = new Color(0.84f, 1.00f, 0.30f, 1.00f);
    private static readonly Color ReticleCoreWhiteColor = new Color(1.00f, 1.00f, 1.00f, 1.00f);

    private static readonly Color PinchHoverColor = new Color(0.32f, 0.98f, 1.00f, 1.00f);
    private static readonly Color PinchActiveDragColor = new Color(0.48f, 1.00f, 0.62f, 1.00f);
    private static readonly Color PinchSnappedColor = new Color(0.84f, 1.00f, 0.30f, 1.00f);

    private GameObject rootObj;
    private LineRenderer[] nodeOuterRings;
    private LineRenderer[] nodeInnerRings;
    private Material[] nodeOuterMats;
    private Material[] nodeInnerMats;
    private readonly Vector3[] auraBuffer = new Vector3[AuraSegments];

    private LineRenderer[] inputRippleRenderers;
    private Material[] inputRippleMats;

    private LineRenderer reticleOuterRenderer;
    private LineRenderer reticleInnerRenderer;
    private LineRenderer reticleCrossHRenderer;
    private LineRenderer reticleCrossVRenderer;
    private Material reticleOuterMat;
    private Material reticleInnerMat;
    private Material reticleCrossMat;
    private readonly Vector3[] reticleBuffer = new Vector3[ReticleSegments];

    private LineRenderer pinchRingRenderer;
    private LineRenderer pinchCoreRenderer;
    private Material pinchRingMat;
    private Material pinchCoreMat;
    private readonly Vector3[] pinchRingBuffer = new Vector3[PinchRingSegments];

    private readonly List<Material> ownedMaterials = new List<Material>(30);
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
            nodeOuterMats[i] = CreateSafeSpritesMat(GateBodyHoverColor, 3006);
            nodeInnerMats[i] = CreateSafeSpritesMat(GateBodyHoverColor, 3005);
            nodeOuterRings[i] = CreateLoopLine($"Holo_NodeAuraOuter_{i}", AuraSegments, 0.007f, nodeOuterMats[i]);
            nodeInnerRings[i] = CreateLoopLine($"Holo_NodeAuraInner_{i}", AuraSegments, 0.004f, nodeInnerMats[i]);
        }

        inputRippleRenderers = new LineRenderer[2];
        inputRippleMats = new Material[2];
        inputRippleMats[0] = CreateSafeSpritesMat(InputACyanHoverColor, 3015);
        inputRippleMats[1] = CreateSafeSpritesMat(InputBGoldHoverColor, 3015);
        inputRippleRenderers[0] = CreateLoopLine("Holo_InputTouchRipple_A", AuraSegments, 0.011f, inputRippleMats[0]);
        inputRippleRenderers[1] = CreateLoopLine("Holo_InputTouchRipple_B", AuraSegments, 0.011f, inputRippleMats[1]);

        reticleOuterMat = CreateSafeSpritesMat(ReticleOpenColor, 3040);
        reticleInnerMat = CreateSafeSpritesMat(ReticleCoreWhiteColor, 3041);
        reticleCrossMat = CreateSafeSpritesMat(ReticleCoreWhiteColor, 3042);
        reticleOuterRenderer = CreateLoopLine("Holo_PalmReticleOuter", ReticleSegments, 0.009f, reticleOuterMat);
        reticleInnerRenderer = CreateLoopLine("Holo_PalmReticleInner", ReticleSegments, 0.0065f, reticleInnerMat);
        reticleCrossHRenderer = CreateSegmentLine("Holo_PalmReticleCrossH", 0.006f, reticleCrossMat);
        reticleCrossVRenderer = CreateSegmentLine("Holo_PalmReticleCrossV", 0.006f, reticleCrossMat);

        pinchRingMat = CreateSafeSpritesMat(PinchHoverColor, 3043);
        pinchCoreMat = CreateSafeSpritesMat(ReticleCoreWhiteColor, 3044);
        pinchRingRenderer = CreateLoopLine("Holo_PinchCursorRing", PinchRingSegments, 0.0085f, pinchRingMat);
        pinchCoreRenderer = CreateLoopLine("Holo_PinchCursorCore", PinchRingSegments, 0.0065f, pinchCoreMat);

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
        HideCursors();
        if (inputRippleRenderers != null)
        {
            for (int i = 0; i < inputRippleRenderers.Length; i++)
            {
                if (inputRippleRenderers[i] != null) inputRippleRenderers[i].enabled = false;
            }
        }
    }

    public void HideCursors()
    {
        if (!isInitialized)
        {
            return;
        }

        SetReticleVisible(false);
        SetPinchIndicatorVisible(false);
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
    /// Memperbarui aura cahaya 3D menghadap kamera pada slot 0..7. Saat Idle/Parked/PortFocused tidak ada
    /// cincin lantai pipih sama sekali sehingga seluruh model 3D tampak melayang bebas. Zero-GC.
    /// </summary>
    public void UpdateNodeFrame(
        int slotIndex,
        Transform camT,
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

        bool shouldDrawAura = camT != null
                              && node != null
                              && !isOutput
                              && (state == NodeHoloState.BodyHovered
                                  || state == NodeHoloState.Grabbed
                                  || state == NodeHoloState.ReturningToRack);
        if (!shouldDrawAura)
        {
            if (outer != null && outer.enabled) outer.enabled = false;
            if (inner != null && inner.enabled) inner.enabled = false;
            return;
        }

        Transform nodeT = node.transform;
        float nodeScale = nodeT.localScale.x;
        // Pusat aura 3D tepat di tengah bodi 3D menghadap kamera (bukan oval lantai pipih di dasar)
        Vector3 center = nodeT.position + (nodeT.up * (0.14f * nodeScale)) - (camT.forward * 0.08f);
        Vector3 axisA = camT.right;
        Vector3 axisB = camT.up;
        float pulse = 0.5f + (0.5f * Mathf.Sin((Time.unscaledTime * 5.2f) + slotIndex));

        Color auraColor;
        float outerRadius;
        float outerWidth;
        bool showInnerAura = false;

        if (isInputA)
        {
            auraColor = InputACyanHoverColor;
            outerRadius = (0.285f * nodeScale) + (0.012f * pulse);
            outerWidth = 0.0085f;
            showInnerAura = true;
        }
        else if (isInputB)
        {
            auraColor = InputBGoldHoverColor;
            outerRadius = (0.285f * nodeScale) + (0.012f * pulse);
            outerWidth = 0.0085f;
            showInnerAura = true;
        }
        else if (state == NodeHoloState.ReturningToRack)
        {
            // Tint aura merah terang saat gerbang dipegang keluar dari kotak AssemblyZone
            auraColor = GateOutsideDeleteRedColor;
            outerRadius = (0.315f * nodeScale) + (0.018f * pulse);
            outerWidth = 0.0115f;
            showInnerAura = true;
        }
        else if (state == NodeHoloState.Grabbed)
        {
            auraColor = GateGrabbedColor;
            outerRadius = (0.295f * nodeScale) + (0.014f * pulse);
            outerWidth = 0.0090f;
            showInnerAura = true;
        }
        else
        {
            // BodyHovered pada gerbang
            auraColor = GateBodyHoverColor;
            outerRadius = (0.275f * nodeScale) + (0.010f * pulse);
            outerWidth = 0.0075f;
            showInnerAura = false;
        }

        DrawAuraCircle(outer, nodeOuterMats[slotIndex], center, axisA, axisB, outerRadius, auraColor, outerWidth);

        if (showInnerAura && inner != null)
        {
            float innerAlpha = state == NodeHoloState.ReturningToRack ? 0.62f : 0.42f;
            Color innerColor = new Color(auraColor.r, auraColor.g, auraColor.b, auraColor.a * innerAlpha);
            DrawAuraCircle(inner, nodeInnerMats[slotIndex], center - (camT.forward * 0.008f), axisA, axisB, outerRadius * 0.84f, innerColor, state == NodeHoloState.ReturningToRack ? 0.006f : 0.0042f);
        }
        else if (inner != null && inner.enabled)
        {
            inner.enabled = false;
        }
    }

    /// <summary>
    /// Merender efek ripple 3D meluas singkat saat sakelar Input A/B disentuh ujung telunjuk (#8). Zero-GC.
    /// </summary>
    public void UpdateInputTouchRipple(int inputIndex, Transform camT, GateNode node, bool isInputB, float normalizedProgress)
    {
        if (!isInitialized || inputRippleRenderers == null || inputIndex < 0 || inputIndex >= inputRippleRenderers.Length)
        {
            return;
        }

        LineRenderer lr = inputRippleRenderers[inputIndex];
        if (lr == null)
        {
            return;
        }

        if (camT == null || node == null || normalizedProgress <= 0.001f || normalizedProgress >= 1f)
        {
            if (lr.enabled) lr.enabled = false;
            return;
        }

        float eased = 1f - ((1f - normalizedProgress) * (1f - normalizedProgress));
        float scale = node.transform.localScale.x;
        Vector3 center = node.transform.position + (node.transform.up * (0.14f * scale)) - (camT.forward * 0.11f);
        float radius = Mathf.Lerp(0.14f, 0.34f, eased);
        Color baseCol = isInputB ? InputBGoldHoverColor : InputACyanHoverColor;
        Color faded = new Color(baseCol.r, baseCol.g, baseCol.b, (1f - normalizedProgress) * 0.95f);
        float width = Mathf.Lerp(0.015f, 0.003f, normalizedProgress);

        DrawAuraCircle(lr, inputRippleMats[inputIndex], center, camT.right, camT.up, radius, faded, width);
    }

    /// <summary>
    /// Memperbarui reticle kursor telapak tangan (tersembunyi otomatis saat tangan hilang / overlay / drag kabel). Zero-GC.
    /// </summary>
    public void UpdatePalmReticle(
        Transform camT,
        bool isHandDetected,
        bool hideReticle,
        Vector3 palmWorldPos,
        bool isFistActive,
        bool isTargetingPort,
        float meanCurlRatio)
    {
        if (!isInitialized)
        {
            return;
        }

        bool show = isHandDetected && !hideReticle && camT != null;
        if (!show)
        {
            SetReticleVisible(false);
            return;
        }

        Vector3 right = camT.right;
        Vector3 up = camT.up;
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

    /// <summary>
    /// Menampilkan indikator cincin cubit kecil presisi di titik tengah #4-#8 saat membidik port atau menyeret kabel. Zero-GC.
    /// </summary>
    public void UpdatePinchIndicator(
        Transform camT,
        bool isHandDetected,
        bool isAllowedAndRelevant,
        Vector3 pinchWorldPos,
        bool isPinchActive,
        bool isSnappedToPort)
    {
        if (!isInitialized)
        {
            return;
        }

        bool show = isHandDetected && isAllowedAndRelevant && camT != null;
        if (!show)
        {
            SetPinchIndicatorVisible(false);
            return;
        }

        Vector3 right = camT.right;
        Vector3 up = camT.up;
        Vector3 center = pinchWorldPos - (camT.forward * 0.145f);
        float pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * 8.0f));
        float outerR = isPinchActive ? (0.020f + (0.003f * pulse)) : 0.032f;
        float coreR = isPinchActive ? 0.0085f : 0.0065f;
        Color ringCol = isSnappedToPort ? PinchSnappedColor : (isPinchActive ? PinchActiveDragColor : PinchHoverColor);

        DrawPinchCircle(pinchRingRenderer, pinchRingMat, center, right, up, outerR, ringCol, isPinchActive ? 0.011f : 0.0085f);
        DrawPinchCircle(pinchCoreRenderer, pinchCoreMat, center - (camT.forward * 0.004f), right, up, coreR, ReticleCoreWhiteColor, 0.007f);
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

    private void SetPinchIndicatorVisible(bool visible)
    {
        if (pinchRingRenderer != null && pinchRingRenderer.enabled != visible)
        {
            pinchRingRenderer.enabled = visible;
        }
        if (pinchCoreRenderer != null && pinchCoreRenderer.enabled != visible)
        {
            pinchCoreRenderer.enabled = visible;
        }
    }

    private void DrawAuraCircle(
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

        for (int i = 0; i < AuraSegments; i++)
        {
            float angle = (i / (float)AuraSegments) * Mathf.PI * 2f;
            auraBuffer[i] = center + (axisA * (Mathf.Cos(angle) * radius)) + (axisB * (Mathf.Sin(angle) * radius));
        }

        if (mat != null)
        {
            mat.color = color;
        }
        lr.enabled = true;
        lr.widthMultiplier = width;
        lr.startColor = color;
        lr.endColor = color;
        lr.SetPositions(auraBuffer);
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

    private void DrawPinchCircle(
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

        for (int i = 0; i < PinchRingSegments; i++)
        {
            float angle = (i / (float)PinchRingSegments) * Mathf.PI * 2f;
            pinchRingBuffer[i] = center + (axisA * (Mathf.Cos(angle) * radius)) + (axisB * (Mathf.Sin(angle) * radius));
        }

        if (mat != null)
        {
            mat.color = color;
        }
        lr.enabled = true;
        lr.widthMultiplier = width;
        lr.startColor = color;
        lr.endColor = color;
        lr.SetPositions(pinchRingBuffer);
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
        inputRippleRenderers = null;
        inputRippleMats = null;
        reticleOuterRenderer = null;
        reticleInnerRenderer = null;
        reticleCrossHRenderer = null;
        reticleCrossVRenderer = null;
        reticleOuterMat = null;
        reticleInnerMat = null;
        reticleCrossMat = null;
        pinchRingRenderer = null;
        pinchCoreRenderer = null;
        pinchRingMat = null;
        pinchCoreMat = null;
    }
}
