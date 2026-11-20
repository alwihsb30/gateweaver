using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Renderer visual hologram 3D untuk Circle-Charge -> Open-5 Bloom Summoner (GateWeaver AR v7.0 Fase 4).
/// - Indikator pose live instan di ujung jari aktif ([ 1: AND ], [ 2: OR ], [ 3: XOR ], [ 4: NAND ], [ JEMPOL: NOT ])
///   + jejak garis putaran langsung sejak titik ke-2 bergerak (tersembunyi penuh saat Open-5 atau Fist).
/// - Cincin pengisian 0->360°, pratinjau hologram nama gerbang + lencana aksi ([ BUKA 5 JARI ] atau isyarat dini amber
///   [ <GATE> SUDAH DI PAPAN ]), serta efek ledakan cincin Bloom saat deploy.
/// - Ter-render jelas di depan feed kamera, meja kerja, & kerangka tangan (zPlane setara palm reticle & renderQueue 3044-3052),
///   100% terisolasi di Mode Udara, shader Sprites/Default (OpenGLES3-safe), Zero-GC, dan dibersihkan di OnDestroy().
/// </summary>
public sealed class AirWeavingSummonVisual : MonoBehaviour
{
    private const int RingSegments = 32;
    private const int TipRingSegments = 16;
    private const int MaxTrailPoints = AirWeavingCircleSummoner.MaxTrailPoints;
    private const float BloomBurstDurationSeconds = 0.42f;
    private const float VisualDepthOffsetFromWorkbench = 0.26f;

    private static readonly Color ChargeTrailCyanColor = new Color(0.24f, 0.96f, 1.00f, 0.96f);
    private static readonly Color ChargeTrailVioletColor = new Color(0.68f, 0.46f, 1.00f, 0.96f);
    private static readonly Color ChargeRingCyanColor = new Color(0.28f, 0.98f, 1.00f, 0.96f);
    private static readonly Color ReadyEmeraldColor = new Color(0.24f, 1.00f, 0.62f, 0.98f);
    private static readonly Color ReadyOuterEmeraldColor = new Color(0.45f, 1.00f, 0.84f, 0.56f);
    private static readonly Color AlreadyPlacedAmberColor = new Color(1.00f, 0.68f, 0.18f, 0.96f);
    private static readonly Color AlreadyPlacedOuterAmberColor = new Color(1.00f, 0.54f, 0.14f, 0.52f);
    private static readonly Color TipCoreWhiteColor = new Color(1.00f, 1.00f, 1.00f, 1.00f);
    private static readonly Color BackdropColor = new Color(0.03f, 0.06f, 0.12f, 0.88f);
    private static readonly Color TitleCyanColor = new Color(0.88f, 0.99f, 1.00f, 1.00f);
    private static readonly Color TitleReadyGreenColor = new Color(0.42f, 1.00f, 0.72f, 1.00f);
    private static readonly Color TitleAmberColor = new Color(1.00f, 0.84f, 0.36f, 1.00f);

    private const string PromptCharging = "PUTAR...";
    private const string PromptReadyDeploy = "[ BUKA 5 JARI ]";
    private const string PromptAlreadyAnd = "[ AND SUDAH DI PAPAN ]";
    private const string PromptAlreadyOr = "[ OR SUDAH DI PAPAN ]";
    private const string PromptAlreadyXor = "[ XOR SUDAH DI PAPAN ]";
    private const string PromptAlreadyNand = "[ NAND SUDAH DI PAPAN ]";
    private const string PromptAlreadyNot = "[ NOT SUDAH DI PAPAN ]";

    private const string TipBadgeAnd = "[ 1: AND ]";
    private const string TipBadgeOr = "[ 2: OR ]";
    private const string TipBadgeXor = "[ 3: XOR ]";
    private const string TipBadgeNand = "[ 4: NAND ]";
    private const string TipBadgeNot = "[ JEMPOL: NOT ]";

    private GameObject rootObj;
    private LineRenderer trailRenderer;
    private LineRenderer baseRingRenderer;
    private LineRenderer chargeArcRenderer;
    private LineRenderer outerReadyRingRenderer;
    private LineRenderer bloomBurstRenderer;
    private LineRenderer tipRingRenderer;
    private LineRenderer tipCoreRenderer;

    private Material trailMat;
    private Material baseRingMat;
    private Material chargeArcMat;
    private Material outerReadyMat;
    private Material bloomBurstMat;
    private Material backdropMat;
    private Material tipRingMat;
    private Material tipCoreMat;
    private Material tipBackdropMat;

    private GameObject labelRootObj;
    private TextMeshPro gateTitleText;
    private TextMeshPro actionPromptText;
    private Transform backdropTransform;

    private GameObject tipBadgeRootObj;
    private TextMeshPro tipBadgeText;
    private Transform tipBadgeBackdropTransform;

    private readonly Vector3[] trailWorldBuffer = new Vector3[MaxTrailPoints];
    private readonly Vector3[] ringBuffer = new Vector3[RingSegments];
    private readonly Vector3[] tipRingBuffer = new Vector3[TipRingSegments];
    private readonly Vector3[] arcBuffer = new Vector3[RingSegments + 1];
    private readonly List<Material> ownedMaterials = new List<Material>(12);

    private bool isInitialized = false;
    private string lastTitleText = string.Empty;
    private string lastPromptText = string.Empty;
    private string lastTipBadgeText = string.Empty;

    private float bloomBurstTimer = 0f;
    private Vector3 bloomBurstCenterWorld = Vector3.zero;
    private bool bloomBurstWasAmber = false;

    public void EnsureVisualsCreated()
    {
        if (isInitialized)
        {
            return;
        }

        rootObj = new GameObject("AR_AirWeavingCircleSummonVisual");
        rootObj.transform.SetParent(transform, false);

        // renderQueue 3044..3052 setara/di depan palm reticle (3040-3042) & jauh di depan video quad (1500)
        backdropMat = CreateSafeSpritesMat(BackdropColor, 3044);
        tipBackdropMat = CreateSafeSpritesMat(BackdropColor, 3044);
        baseRingMat = CreateSafeSpritesMat(ChargeRingCyanColor, 3045);
        outerReadyMat = CreateSafeSpritesMat(ReadyOuterEmeraldColor, 3046);
        trailMat = CreateSafeSpritesMat(ChargeTrailCyanColor, 3047);
        chargeArcMat = CreateSafeSpritesMat(ReadyEmeraldColor, 3048);
        tipRingMat = CreateSafeSpritesMat(ChargeTrailCyanColor, 3049);
        tipCoreMat = CreateSafeSpritesMat(TipCoreWhiteColor, 3050);
        bloomBurstMat = CreateSafeSpritesMat(ReadyEmeraldColor, 3052);

        trailRenderer = CreateLine("Summon_CircleTrail", 0, loop: false, width: 0.017f, trailMat);
        baseRingRenderer = CreateLine("Summon_BaseRing", RingSegments, loop: true, width: 0.0065f, baseRingMat);
        chargeArcRenderer = CreateLine("Summon_ChargeArc", 0, loop: false, width: 0.015f, chargeArcMat);
        outerReadyRingRenderer = CreateLine("Summon_ReadyOuterRing", RingSegments, loop: true, width: 0.0075f, outerReadyMat);
        bloomBurstRenderer = CreateLine("Summon_BloomBurstRing", RingSegments, loop: true, width: 0.018f, bloomBurstMat);
        tipRingRenderer = CreateLine("Summon_LiveTipRing", TipRingSegments, loop: true, width: 0.009f, tipRingMat);
        tipCoreRenderer = CreateLine("Summon_LiveTipCore", TipRingSegments, loop: true, width: 0.009f, tipCoreMat);

        // Pratinjau hologram 3D nama gerbang & lencana aksi di pusat lingkaran
        labelRootObj = new GameObject("Summon_HologramPreviewLabel");
        labelRootObj.transform.SetParent(rootObj.transform, false);

        GameObject titleObj = new GameObject("Txt_SummonGateName");
        titleObj.transform.SetParent(labelRootObj.transform, false);
        titleObj.transform.localPosition = new Vector3(0f, 0.032f, 0f);
        gateTitleText = titleObj.AddComponent<TextMeshPro>();
        gateTitleText.fontSize = 0.72f;
        gateTitleText.fontStyle = FontStyles.Bold;
        gateTitleText.alignment = TextAlignmentOptions.Center;
        gateTitleText.textWrappingMode = TextWrappingModes.NoWrap;
        gateTitleText.color = TitleCyanColor;
        gateTitleText.sortingOrder = 25;
        if (gateTitleText.rectTransform != null)
        {
            gateTitleText.rectTransform.sizeDelta = new Vector2(1.8f, 0.35f);
        }

        GameObject promptObj = new GameObject("Txt_SummonPrompt");
        promptObj.transform.SetParent(labelRootObj.transform, false);
        promptObj.transform.localPosition = new Vector3(0f, -0.036f, 0f);
        actionPromptText = promptObj.AddComponent<TextMeshPro>();
        actionPromptText.fontSize = 0.38f;
        actionPromptText.fontStyle = FontStyles.Bold;
        actionPromptText.alignment = TextAlignmentOptions.Center;
        actionPromptText.textWrappingMode = TextWrappingModes.NoWrap;
        actionPromptText.color = TitleCyanColor;
        actionPromptText.sortingOrder = 25;
        if (actionPromptText.rectTransform != null)
        {
            actionPromptText.rectTransform.sizeDelta = new Vector2(2.4f, 0.30f);
        }

        GameObject bgQuad = CreateBackdropQuad("Summon_LabelBackdrop", labelRootObj.transform, new Vector3(0.56f, 0.165f, 1f), backdropMat);
        backdropTransform = bgQuad.transform;

        // Indikator label mikro live di ujung jari aktif ([ 1: AND ] .. [ JEMPOL: NOT ])
        tipBadgeRootObj = new GameObject("Summon_LiveTipBadge");
        tipBadgeRootObj.transform.SetParent(rootObj.transform, false);

        GameObject tipTxtObj = new GameObject("Txt_SummonLiveTip");
        tipTxtObj.transform.SetParent(tipBadgeRootObj.transform, false);
        tipTxtObj.transform.localPosition = Vector3.zero;
        tipBadgeText = tipTxtObj.AddComponent<TextMeshPro>();
        tipBadgeText.fontSize = 0.42f;
        tipBadgeText.fontStyle = FontStyles.Bold;
        tipBadgeText.alignment = TextAlignmentOptions.Center;
        tipBadgeText.textWrappingMode = TextWrappingModes.NoWrap;
        tipBadgeText.color = TitleCyanColor;
        tipBadgeText.sortingOrder = 26;
        if (tipBadgeText.rectTransform != null)
        {
            tipBadgeText.rectTransform.sizeDelta = new Vector2(2.0f, 0.30f);
        }

        GameObject tipBgQuad = CreateBackdropQuad("Summon_TipBackdrop", tipBadgeRootObj.transform, new Vector3(0.38f, 0.085f, 1f), tipBackdropMat);
        tipBadgeBackdropTransform = tipBgQuad.transform;

        isInitialized = true;
        HideImmediate();
    }

    public void HideImmediate()
    {
        bloomBurstTimer = 0f;
        if (!isInitialized)
        {
            return;
        }

        if (rootObj != null && rootObj.activeSelf)
        {
            rootObj.SetActive(false);
        }
        DisableActiveStageVisuals();
        DisableLiveTipIndicator();
        if (bloomBurstRenderer != null) bloomBurstRenderer.enabled = false;
    }

    public void TriggerBloomBurst(Vector3 centerWorldPos, bool isAmberNotice)
    {
        if (!isInitialized)
        {
            return;
        }

        bloomBurstCenterWorld = centerWorldPos;
        bloomBurstWasAmber = isAmberNotice;
        bloomBurstTimer = BloomBurstDurationSeconds;
        if (rootObj != null && !rootObj.activeSelf)
        {
            rootObj.SetActive(true);
        }
    }

    /// <summary>
    /// Memperbarui indikator pose live ujung jari, jejak putaran sejak titik ke-2, visual hologram 2-tahap, & Bloom Burst. Zero-GC.
    /// </summary>
    public void TickVisuals(
        Camera mainCam,
        float dt,
        bool isAirWeavingActive,
        float workbenchDepth,
        AirWeavingCircleSummoner summoner,
        bool isGateAlreadyOnWorkbench)
    {
        if (!isInitialized)
        {
            return;
        }

        if (!isAirWeavingActive || mainCam == null || summoner == null)
        {
            HideImmediate();
            return;
        }

        bool hasStageVisual = summoner.IsActive;
        bool hasLiveTip = summoner.HasLivePoseIndicator;
        bool hasBloomBurst = bloomBurstTimer > 0f;
        if (!hasStageVisual && !hasLiveTip && !hasBloomBurst)
        {
            if (rootObj != null && rootObj.activeSelf)
            {
                rootObj.SetActive(false);
            }
            DisableActiveStageVisuals();
            DisableLiveTipIndicator();
            return;
        }

        if (rootObj != null && !rootObj.activeSelf)
        {
            rootObj.SetActive(true);
        }

        Transform camT = mainCam.transform;
        Vector3 right = camT.right;
        Vector3 up = camT.up;
        Vector3 forward = camT.forward;

        // 1. Efek ledakan Bloom Burst pasca-deploy / notice
        if (hasBloomBurst)
        {
            bloomBurstTimer = Mathf.Max(0f, bloomBurstTimer - dt);
            float t = 1f - (bloomBurstTimer / BloomBurstDurationSeconds);
            float eased = 1f - ((1f - t) * (1f - t));
            float radius = Mathf.Lerp(0.12f, 0.42f, eased);
            Color baseCol = bloomBurstWasAmber ? AlreadyPlacedAmberColor : ReadyEmeraldColor;
            Color faded = new Color(baseCol.r, baseCol.g, baseCol.b, (1f - t) * 0.95f);
            DrawCircle(bloomBurstRenderer, bloomBurstMat, bloomBurstCenterWorld - (forward * 0.14f), right, up, radius, faded, Mathf.Lerp(0.022f, 0.004f, t));
            if (bloomBurstTimer <= 0f && bloomBurstRenderer != null)
            {
                bloomBurstRenderer.enabled = false;
            }
        }
        else if (bloomBurstRenderer != null && bloomBurstRenderer.enabled)
        {
            bloomBurstRenderer.enabled = false;
        }

        // Kedalaman zPlane setara bidang palm reticle (3.34f di depan rangka tangan 3.46f & meja 3.60f)
        float zPlane = workbenchDepth - VisualDepthOffsetFromWorkbench;
        float pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * 7.0f));
        bool isReady = summoner.Stage == AirWeavingCircleSummoner.SummonStage.Ready;

        // 2. Indikator pose live instan di ujung jari aktif ([ 1: AND ] .. [ JEMPOL: NOT ])
        if (hasLiveTip && !isReady)
        {
            Vector2 tipLocal = summoner.LiveIndicatorTipLocalXY;
            Vector3 tipWorld = camT.TransformPoint(new Vector3(tipLocal.x, tipLocal.y, zPlane - 0.02f));
            Color tipRingCol = isGateAlreadyOnWorkbench ? AlreadyPlacedAmberColor : ChargeTrailCyanColor;
            float tipOuterR = 0.038f + (0.007f * pulse);
            DrawTipCircle(tipRingRenderer, tipRingMat, tipWorld, right, up, tipOuterR, tipRingCol, 0.009f);
            DrawTipCircle(tipCoreRenderer, tipCoreMat, tipWorld - (forward * 0.004f), right, up, 0.012f, TipCoreWhiteColor, 0.0085f);

            if (tipBadgeRootObj != null)
            {
                if (!tipBadgeRootObj.activeSelf)
                {
                    tipBadgeRootObj.SetActive(true);
                }
                Vector3 badgePos = tipWorld + (up * 0.095f) - (forward * 0.012f);
                tipBadgeRootObj.transform.position = badgePos;
                tipBadgeRootObj.transform.rotation = Quaternion.LookRotation(badgePos - camT.position, up);

                NodeType tipGate = summoner.LiveIndicatorGateType;
                string desiredTipBadge = GetCachedTipBadge(tipGate);
                if (!string.Equals(desiredTipBadge, lastTipBadgeText, StringComparison.Ordinal) && tipBadgeText != null)
                {
                    lastTipBadgeText = desiredTipBadge;
                    tipBadgeText.text = desiredTipBadge;
                    if (tipBadgeBackdropTransform != null)
                    {
                        float bw = tipGate == NodeType.NOT ? 0.52f : (tipGate == NodeType.NAND ? 0.42f : 0.38f);
                        tipBadgeBackdropTransform.localScale = new Vector3(bw, 0.088f, 1f);
                    }
                }

                if (tipBadgeText != null)
                {
                    tipBadgeText.color = isGateAlreadyOnWorkbench ? TitleAmberColor : TitleCyanColor;
                }
            }
        }
        else
        {
            DisableLiveTipIndicator();
        }

        // 3. Jejak lingkaran live langsung sejak titik ke-2 bergerak (baik saat Idle warm-up maupun Charging)
        int trailCount = summoner.TrailCount;
        if (!isReady && (hasLiveTip || hasStageVisual) && trailCount >= 2 && trailRenderer != null)
        {
            Vector2[] pts = summoner.TrailPointsBuffer;
            int clamped = Mathf.Min(trailCount, MaxTrailPoints);
            for (int i = 0; i < clamped; i++)
            {
                trailWorldBuffer[i] = camT.TransformPoint(new Vector3(pts[i].x, pts[i].y, zPlane - 0.015f));
            }
            Color startCol = isGateAlreadyOnWorkbench ? AlreadyPlacedAmberColor : ChargeTrailCyanColor;
            Color endCol = isGateAlreadyOnWorkbench ? AlreadyPlacedOuterAmberColor : ChargeTrailVioletColor;
            if (trailMat != null) trailMat.color = startCol;
            trailRenderer.enabled = true;
            trailRenderer.positionCount = clamped;
            trailRenderer.startColor = startCol;
            trailRenderer.endColor = endCol;
            trailRenderer.SetPositions(trailWorldBuffer);
        }
        else if (trailRenderer != null && trailRenderer.enabled)
        {
            trailRenderer.enabled = false;
        }

        if (!hasStageVisual)
        {
            DisableCenterStageVisuals();
            return;
        }

        Vector2 centerLocal = summoner.PreviewCenterLocalXY;
        Vector3 centerWorld = camT.TransformPoint(new Vector3(centerLocal.x, centerLocal.y, zPlane));
        float ringRadius = isReady ? 0.195f : Mathf.Clamp(summoner.PreviewRadius, 0.135f, 0.225f);

        // 4. Cincin dasar, busur pengisian (0->360°), & halo luar tahap Ready
        Color baseColor = isGateAlreadyOnWorkbench
            ? new Color(AlreadyPlacedAmberColor.r, AlreadyPlacedAmberColor.g, AlreadyPlacedAmberColor.b, 0.46f)
            : (isReady
                ? new Color(ReadyEmeraldColor.r, ReadyEmeraldColor.g, ReadyEmeraldColor.b, 0.58f)
                : new Color(ChargeRingCyanColor.r, ChargeRingCyanColor.g, ChargeRingCyanColor.b, 0.42f));
        DrawCircle(baseRingRenderer, baseRingMat, centerWorld, right, up, ringRadius, baseColor, 0.006f);

        float arcFraction = isReady ? summoner.ReadyRemainingNormalized : summoner.ChargeProgress;
        if (arcFraction > 0.02f && chargeArcRenderer != null)
        {
            int segs = Mathf.Clamp(Mathf.CeilToInt(arcFraction * RingSegments), 2, RingSegments);
            float maxAngle = arcFraction * Mathf.PI * 2f;
            Vector3 arcCenter = centerWorld - (forward * 0.012f);
            float arcRad = ringRadius + (isReady ? 0.010f : 0.006f);
            for (int i = 0; i <= segs; i++)
            {
                float f = i / (float)segs;
                float angle = (Mathf.PI * 0.5f) - (f * maxAngle);
                arcBuffer[i] = arcCenter + (right * (Mathf.Cos(angle) * arcRad)) + (up * (Mathf.Sin(angle) * arcRad));
            }

            Color arcCol = isGateAlreadyOnWorkbench
                ? AlreadyPlacedAmberColor
                : (isReady ? ReadyEmeraldColor : Color.Lerp(ChargeRingCyanColor, ReadyEmeraldColor, arcFraction));
            if (chargeArcMat != null) chargeArcMat.color = arcCol;
            chargeArcRenderer.enabled = true;
            chargeArcRenderer.positionCount = segs + 1;
            chargeArcRenderer.widthMultiplier = isReady ? 0.015f : 0.013f;
            chargeArcRenderer.startColor = arcCol;
            chargeArcRenderer.endColor = arcCol;
            chargeArcRenderer.SetPositions(arcBuffer);
        }
        else if (chargeArcRenderer != null && chargeArcRenderer.enabled)
        {
            chargeArcRenderer.enabled = false;
        }

        if (isReady)
        {
            Color outerCol = isGateAlreadyOnWorkbench ? AlreadyPlacedOuterAmberColor : ReadyOuterEmeraldColor;
            float outerR = ringRadius + 0.032f + (0.016f * pulse);
            DrawCircle(outerReadyRingRenderer, outerReadyMat, centerWorld - (forward * 0.006f), right, up, outerR, outerCol, 0.0075f);
        }
        else if (outerReadyRingRenderer != null && outerReadyRingRenderer.enabled)
        {
            outerReadyRingRenderer.enabled = false;
        }

        // 5. Label hologram nama gerbang & lencana aksi (Zero-GC string cache)
        if (labelRootObj != null)
        {
            if (!labelRootObj.activeSelf)
            {
                labelRootObj.SetActive(true);
            }
            Vector3 labelPos = centerWorld - (forward * 0.025f);
            labelRootObj.transform.position = labelPos;
            labelRootObj.transform.rotation = Quaternion.LookRotation(labelPos - camT.position, up);

            string desiredTitle = GetCachedGateName(summoner.ActiveGateType);
            string desiredPrompt = isGateAlreadyOnWorkbench
                ? GetCachedAlreadyPlacedPrompt(summoner.ActiveGateType)
                : (isReady ? PromptReadyDeploy : PromptCharging);

            if (!string.Equals(desiredTitle, lastTitleText, StringComparison.Ordinal) && gateTitleText != null)
            {
                lastTitleText = desiredTitle;
                gateTitleText.text = desiredTitle;
            }

            if (!string.Equals(desiredPrompt, lastPromptText, StringComparison.Ordinal) && actionPromptText != null)
            {
                lastPromptText = desiredPrompt;
                actionPromptText.text = desiredPrompt;
                if (backdropTransform != null)
                {
                    float w = isGateAlreadyOnWorkbench ? 0.72f : (isReady ? 0.56f : 0.42f);
                    backdropTransform.localScale = new Vector3(w, 0.165f, 1f);
                }
            }

            Color textCol = isGateAlreadyOnWorkbench
                ? TitleAmberColor
                : (isReady ? TitleReadyGreenColor : TitleCyanColor);
            if (gateTitleText != null) gateTitleText.color = textCol;
            if (actionPromptText != null) actionPromptText.color = textCol;
        }
    }

    private void DisableActiveStageVisuals()
    {
        if (trailRenderer != null) trailRenderer.enabled = false;
        DisableCenterStageVisuals();
    }

    private void DisableCenterStageVisuals()
    {
        if (baseRingRenderer != null) baseRingRenderer.enabled = false;
        if (chargeArcRenderer != null) chargeArcRenderer.enabled = false;
        if (outerReadyRingRenderer != null) outerReadyRingRenderer.enabled = false;
        if (labelRootObj != null && labelRootObj.activeSelf) labelRootObj.SetActive(false);
    }

    private void DisableLiveTipIndicator()
    {
        if (tipRingRenderer != null && tipRingRenderer.enabled) tipRingRenderer.enabled = false;
        if (tipCoreRenderer != null && tipCoreRenderer.enabled) tipCoreRenderer.enabled = false;
        if (tipBadgeRootObj != null && tipBadgeRootObj.activeSelf) tipBadgeRootObj.SetActive(false);
    }

    private void DrawCircle(LineRenderer lr, Material mat, Vector3 center, Vector3 axisA, Vector3 axisB, float radius, Color color, float width)
    {
        if (lr == null)
        {
            return;
        }

        for (int i = 0; i < RingSegments; i++)
        {
            float angle = (i / (float)RingSegments) * Mathf.PI * 2f;
            ringBuffer[i] = center + (axisA * (Mathf.Cos(angle) * radius)) + (axisB * (Mathf.Sin(angle) * radius));
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

    private void DrawTipCircle(LineRenderer lr, Material mat, Vector3 center, Vector3 axisA, Vector3 axisB, float radius, Color color, float width)
    {
        if (lr == null)
        {
            return;
        }

        for (int i = 0; i < TipRingSegments; i++)
        {
            float angle = (i / (float)TipRingSegments) * Mathf.PI * 2f;
            tipRingBuffer[i] = center + (axisA * (Mathf.Cos(angle) * radius)) + (axisB * (Mathf.Sin(angle) * radius));
        }

        if (mat != null)
        {
            mat.color = color;
        }
        lr.enabled = true;
        lr.widthMultiplier = width;
        lr.startColor = color;
        lr.endColor = color;
        lr.SetPositions(tipRingBuffer);
    }

    private static GameObject CreateBackdropQuad(string quadName, Transform parent, Vector3 scale, Material mat)
    {
        GameObject bgQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bgQuad.name = quadName;
        bgQuad.transform.SetParent(parent, false);
        bgQuad.transform.localPosition = new Vector3(0f, 0f, 0.012f);
        bgQuad.transform.localScale = scale;
        Collider col = bgQuad.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }
        Renderer bgRend = bgQuad.GetComponent<Renderer>();
        if (bgRend != null)
        {
            bgRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bgRend.receiveShadows = false;
            if (mat != null)
            {
                bgRend.sharedMaterial = mat;
            }
        }
        return bgQuad;
    }

    private static string GetCachedGateName(NodeType gateType)
    {
        switch (gateType)
        {
            case NodeType.AND: return "AND";
            case NodeType.OR: return "OR";
            case NodeType.XOR: return "XOR";
            case NodeType.NAND: return "NAND";
            case NodeType.NOT: return "NOT";
            default: return "GATE";
        }
    }

    private static string GetCachedAlreadyPlacedPrompt(NodeType gateType)
    {
        switch (gateType)
        {
            case NodeType.AND: return PromptAlreadyAnd;
            case NodeType.OR: return PromptAlreadyOr;
            case NodeType.XOR: return PromptAlreadyXor;
            case NodeType.NAND: return PromptAlreadyNand;
            case NodeType.NOT: return PromptAlreadyNot;
            default: return PromptAlreadyAnd;
        }
    }

    private static string GetCachedTipBadge(NodeType gateType)
    {
        switch (gateType)
        {
            case NodeType.AND: return TipBadgeAnd;
            case NodeType.OR: return TipBadgeOr;
            case NodeType.XOR: return TipBadgeXor;
            case NodeType.NAND: return TipBadgeNand;
            case NodeType.NOT: return TipBadgeNot;
            default: return TipBadgeAnd;
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
        trailRenderer = null;
        baseRingRenderer = null;
        chargeArcRenderer = null;
        outerReadyRingRenderer = null;
        bloomBurstRenderer = null;
        tipRingRenderer = null;
        tipCoreRenderer = null;
        trailMat = null;
        baseRingMat = null;
        chargeArcMat = null;
        outerReadyMat = null;
        bloomBurstMat = null;
        backdropMat = null;
        tipRingMat = null;
        tipCoreMat = null;
        tipBackdropMat = null;
        labelRootObj = null;
        gateTitleText = null;
        actionPromptText = null;
        backdropTransform = null;
        tipBadgeRootObj = null;
        tipBadgeText = null;
        tipBadgeBackdropTransform = null;
    }
}
