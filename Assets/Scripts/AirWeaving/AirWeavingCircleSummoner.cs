using UnityEngine;

/// <summary>
/// State machine 2-tahap Circle-Charge -> Open-5 Bloom Summoner khusus Mode Udara (GateWeaver AR v7.0 Fase 4).
/// - Tahap 0 (Idle + Live Pose Indicator): begitu pose sah (AND/OR/XOR/NAND/NOT) terdeteksi di area rakit,
///   langsung memaparkan indikator ujung jari live dan merekam jejak garis mulai titik ke-2.
/// - Tahap 1 (Charging): memakai chord turning angle kumulatif segmen-ke-segmen (sum dHeading) yang kebal pergeseran
///   centroid, target sapuan 185 deg, pose vote >= 2/3, pose lock 35 deg, pose dropout grace 160 ms, dan baru mengevaluasi
///   radius (>= 0.045) serta circularity (>= 0.32) setelah sapuan >= 110 deg.
/// - Tahap 2 (Ready -> Open-5 Bloom): mengunci pusat lingkaran (di-clamp aman ke AssemblyZone), menampilkan pratinjau
///   hologram selama 2.2 dtk, dan men-deploy gerbang saat telapak dibuka santai (30 ms debounce); Fist membatalkan.
/// - 100% pure C# Zero-GC & saling eksklusif dengan kontrol Skema 2 (Open-5 kursor & Fist grab/kabel).
/// </summary>
public sealed class AirWeavingCircleSummoner
{
    public enum SummonStage
    {
        Idle = 0,
        Charging = 1,
        Ready = 2
    }

    public const int MaxTrailPoints = 72;

    private const int PoseVoteWindow = 5;
    private const int PoseVoteMajorityThreshold = 3;
    private const int MinTrailPointsForCharge = 6;
    private const float MinPointSpacing = 0.018f;
    private const float MinTotalArcLengthForStartVisual = 0.18f;
    private const float MinTotalArcLengthForReady = 0.30f;
    private const float MinCircleRadius = 0.062f;
    private const float MaxCircleRadius = 0.650f;
    private const float StartVisualAngleDegrees = 105f;
    private const float PoseLockAngleDegrees = 115f;
    private const float ShapeCheckAngleDegrees = 135f;
    private const float MinCircularityRatio = 0.36f;
    private const float MinDirectionalCoherence = 0.64f;
    private const float TargetSweepDegrees = 205f;
    private const float MaxSingleTurnRadians = 2.356f; // 135 deg: lonjakan balik tajam dianggap zig-zag

    private const float PoseConfirmHoldSeconds = 0.240f;
    private const float PoseDropoutGraceSeconds = 0.140f;
    private const float ChargePoseDropoutGraceSeconds = 0.280f;
    private const float ChargeStallTimeoutSeconds = 0.65f;
    private const float ChargeHandLostGraceSeconds = 0.24f;
    private const float ReadyWindowSeconds = 2.20f;
    private const float ReadyHandLostGraceSeconds = 0.28f;
    private const float OpenFiveBloomDebounceSeconds = 0.060f;
    private const float PostSummonCooldownSeconds = 0.75f;

    // Margin zona mulai summon dari Idle: lindungi terminal Input/Output & Rak Parkir, namun beri ruang atas (+0.52)
    // agar ujung telunjuk #8 pada pose OneFingerAnd yang teracung ke atas tidak keluar dari batas atas zona rakit.
    private const float StartZoneMarginX = 0.18f;
    private const float StartZoneMarginBottomY = 0.16f;
    private const float StartZoneMarginTopY = 0.52f;

    private readonly Vector2[] trailPoints = new Vector2[MaxTrailPoints];
    private int trailCount = 0;

    private readonly SummonHandPose[] poseVoteRing = new SummonHandPose[PoseVoteWindow];
    private int poseVoteWriteIndex = 0;
    private int poseVoteFilled = 0;

    private SummonStage stage = SummonStage.Idle;
    private NodeType activeGateType = NodeType.AND;
    private bool isGatePoseLocked = false;
    private bool requireStrictOpenFiveInReady = false;
    private float chargeProgress = 0f;
    private float accumulatedSweepDeg = 0f;
    private Vector2 circleCenterLocalXY = Vector2.zero;
    private float circleRadius = 0.14f;
    private Vector2 lockedDeployCenterLocalXY = Vector2.zero;

    private SummonHandPose candidatePose = SummonHandPose.None;
    private float candidatePoseHoldTimer = 0f;
    private float poseDropoutTimer = 0f;
    private float stallTimer = 0f;
    private float handLostTimer = 0f;
    private float readyTimer = 0f;
    private float bloomHoldTimer = 0f;
    private float postSummonCooldownTimer = 0f;
    private bool hasPrevPoint = false;
    private Vector2 prevSampleLocalXY = Vector2.zero;

    public SummonStage Stage => stage;
    public bool IsActive => stage != SummonStage.Idle;
    public NodeType ActiveGateType => activeGateType;
    public float ChargeProgress => chargeProgress;
    public float AccumulatedSweepDegrees => accumulatedSweepDeg;
    public float ReadyRemainingNormalized => stage == SummonStage.Ready ? Mathf.Clamp01(1f - (readyTimer / ReadyWindowSeconds)) : 0f;
    public Vector2 PreviewCenterLocalXY => stage == SummonStage.Ready ? lockedDeployCenterLocalXY : circleCenterLocalXY;
    public float PreviewRadius => Mathf.Clamp(circleRadius, MinCircleRadius, 0.24f);
    public int TrailCount => trailCount;
    public Vector2[] TrailPointsBuffer => trailPoints;

    public bool HasLivePoseIndicator { get; private set; }
    public NodeType LiveIndicatorGateType { get; private set; }
    public Vector2 LiveIndicatorTipLocalXY { get; private set; }

    public bool BloomJustTriggeredThisFrame { get; private set; }
    public NodeType DeployedGateType { get; private set; }
    public Vector2 DeployedLocalXY { get; private set; }
    public bool ConsumedFistCancelThisFrame { get; private set; }

    public void Reset()
    {
        stage = SummonStage.Idle;
        trailCount = 0;
        poseVoteWriteIndex = 0;
        poseVoteFilled = 0;
        isGatePoseLocked = false;
        requireStrictOpenFiveInReady = false;
        chargeProgress = 0f;
        accumulatedSweepDeg = 0f;
        circleCenterLocalXY = Vector2.zero;
        circleRadius = 0.14f;
        lockedDeployCenterLocalXY = Vector2.zero;
        candidatePose = SummonHandPose.None;
        candidatePoseHoldTimer = 0f;
        poseDropoutTimer = 0f;
        stallTimer = 0f;
        handLostTimer = 0f;
        readyTimer = 0f;
        bloomHoldTimer = 0f;
        postSummonCooldownTimer = 0f;
        hasPrevPoint = false;
        prevSampleLocalXY = Vector2.zero;
        HasLivePoseIndicator = false;
        LiveIndicatorGateType = NodeType.AND;
        LiveIndicatorTipLocalXY = Vector2.zero;
        BloomJustTriggeredThisFrame = false;
        ConsumedFistCancelThisFrame = false;
    }

    public void ClearPoseVoteBuffer()
    {
        poseVoteWriteIndex = 0;
        poseVoteFilled = 0;
    }

    public void RecordPoseSample(SummonHandPose pose)
    {
        poseVoteRing[poseVoteWriteIndex] = pose;
        poseVoteWriteIndex = (poseVoteWriteIndex + 1) % PoseVoteWindow;
        if (poseVoteFilled < PoseVoteWindow)
        {
            poseVoteFilled++;
        }
    }

    /// <summary>
    /// Memperbarui state machine summon 2-tahap + status indikator ujung jari live di setiap frame Update(). Zero-GC.
    /// </summary>
    public void Tick(
        float dt,
        bool isAirWeavingActive,
        bool blockedByHigherPriority,
        bool isHandDetected,
        bool isFistActive,
        bool fistJustStarted,
        SummonHandPose currentPose,
        bool isRelaxedOpenFiveBloom,
        Vector2 activeTipLocalXY,
        Vector2 thumbTipLocalXY,
        Vector2 assemblyMinXY,
        Vector2 assemblyMaxXY)
    {
        BloomJustTriggeredThisFrame = false;
        ConsumedFistCancelThisFrame = false;
        HasLivePoseIndicator = false;

        if (postSummonCooldownTimer > 0f)
        {
            postSummonCooldownTimer = Mathf.Max(0f, postSummonCooldownTimer - dt);
        }

        if (!isAirWeavingActive || blockedByHigherPriority)
        {
            if (stage != SummonStage.Idle)
            {
                CancelToIdle();
            }
            candidatePose = SummonHandPose.None;
            candidatePoseHoldTimer = 0f;
            poseDropoutTimer = 0f;
            trailCount = 0;
            hasPrevPoint = false;
            return;
        }

        if (stage == SummonStage.Ready)
        {
            TickReadyStage(dt, isHandDetected, isFistActive, fistJustStarted, currentPose, isRelaxedOpenFiveBloom);
            return;
        }

        // Di luar Ready: Fist langsung membatalkan charge (tidak ada konflik dengan Skema 2)
        if (isFistActive || fistJustStarted)
        {
            if (stage == SummonStage.Charging)
            {
                ConsumedFistCancelThisFrame = fistJustStarted;
                CancelToIdle();
            }
            trailCount = 0;
            hasPrevPoint = false;
            candidatePose = SummonHandPose.None;
            candidatePoseHoldTimer = 0f;
            poseDropoutTimer = 0f;
            return;
        }

        if (!isHandDetected)
        {
            if (stage == SummonStage.Charging)
            {
                handLostTimer += dt;
                if (handLostTimer >= ChargeHandLostGraceSeconds)
                {
                    CancelToIdle();
                }
            }
            else
            {
                trailCount = 0;
                hasPrevPoint = false;
                candidatePose = SummonHandPose.None;
                candidatePoseHoldTimer = 0f;
                poseDropoutTimer = 0f;
            }
            return;
        }

        handLostTimer = 0f;

        if (postSummonCooldownTimer > 0f)
        {
            return;
        }

        // Evaluasi pose stabil (majority vote >= 2/3 frame) untuk 5 gerbang
        SummonHandPose votedPose = ComputeMajoritySummonPose();
        bool isOpenFive = currentPose == SummonHandPose.OpenFiveBloom;

        if (stage == SummonStage.Idle)
        {
            if (isOpenFive)
            {
                candidatePose = SummonHandPose.None;
                candidatePoseHoldTimer = 0f;
                poseDropoutTimer = 0f;
                trailCount = 0;
                hasPrevPoint = false;
                return;
            }

            bool hasVotedGate = TryMapPoseToGate(votedPose, out NodeType mappedGate);
            if (!hasVotedGate)
            {
                if (trailCount > 0 && candidatePose != SummonHandPose.None && candidatePoseHoldTimer >= PoseConfirmHoldSeconds)
                {
                    poseDropoutTimer += dt;
                    if (poseDropoutTimer < PoseDropoutGraceSeconds)
                    {
                        if (TryMapPoseToGate(candidatePose, out NodeType heldGate))
                        {
                            HasLivePoseIndicator = true;
                            LiveIndicatorGateType = heldGate;
                            LiveIndicatorTipLocalXY = heldGate == NodeType.NOT ? thumbTipLocalXY : activeTipLocalXY;
                        }
                        return;
                    }
                }

                candidatePose = SummonHandPose.None;
                candidatePoseHoldTimer = 0f;
                poseDropoutTimer = 0f;
                trailCount = 0;
                hasPrevPoint = false;
                return;
            }

            poseDropoutTimer = 0f;

            Vector2 activeTip = mappedGate == NodeType.NOT ? thumbTipLocalXY : activeTipLocalXY;
            if (!IsInsideSummonStartZone(activeTip, assemblyMinXY, assemblyMaxXY))
            {
                trailCount = 0;
                hasPrevPoint = false;
                candidatePose = SummonHandPose.None;
                candidatePoseHoldTimer = 0f;
                return;
            }

            if (votedPose == candidatePose)
            {
                candidatePoseHoldTimer += dt;
            }
            else
            {
                candidatePose = votedPose;
                candidatePoseHoldTimer = dt;
                trailCount = 0;
                hasPrevPoint = false;
            }

            // Tahan indikator & perekaman jejak sampai pose jari spesifik stabil >= 110 ms agar gerakan santai tidak memicu summon
            if (candidatePoseHoldTimer < PoseConfirmHoldSeconds)
            {
                return;
            }

            HasLivePoseIndicator = true;
            LiveIndicatorGateType = mappedGate;
            LiveIndicatorTipLocalXY = activeTip;

            AppendTrailSample(activeTip, dt);

            if (EvaluateCircleGeometry(out Vector2 center, out float estRad, out float absSweepDeg, out float coherence, out float circularity, out float totalArcLength))
            {
                // Masuk Charging hanya bila sudut belok, koherensi, radius minimal, dan panjang goresan nyata terpenuhi
                if (trailCount >= MinTrailPointsForCharge
                    && totalArcLength >= MinTotalArcLengthForStartVisual
                    && absSweepDeg >= StartVisualAngleDegrees
                    && estRad >= (MinCircleRadius * 0.65f)
                    && estRad <= MaxCircleRadius
                    && coherence >= MinDirectionalCoherence)
                {
                    stage = SummonStage.Charging;
                    activeGateType = mappedGate;
                    isGatePoseLocked = absSweepDeg >= PoseLockAngleDegrees;
                    circleCenterLocalXY = ClampToAssembly(center, assemblyMinXY, assemblyMaxXY);
                    circleRadius = Mathf.Max(MinCircleRadius, estRad);
                    accumulatedSweepDeg = absSweepDeg;
                    chargeProgress = Mathf.Clamp01(absSweepDeg / TargetSweepDegrees);
                    poseDropoutTimer = 0f;
                    stallTimer = 0f;

                    // Antisipasi gerakan cepat namun tetap wajib lolos panjang goresan & circularity ketat
                    if (absSweepDeg >= TargetSweepDegrees
                        && totalArcLength >= MinTotalArcLengthForReady
                        && estRad >= MinCircleRadius
                        && circularity >= MinCircularityRatio)
                    {
                        EnterReadyStage(isRelaxedOpenFiveBloom);
                    }
                }
            }
            else if (stallTimer >= ChargeStallTimeoutSeconds)
            {
                trailCount = 0;
                hasPrevPoint = false;
                stallTimer = 0f;
            }
            return;
        }

        // Stage == Charging
        if (isOpenFive)
        {
            // Membuka 5 jari penuh terlalu dini sebelum 185 deg -> kembali santai ke kursor (tidak spawn prematur)
            CancelToIdle();
            return;
        }

        Vector2 tipXY = activeGateType == NodeType.NOT ? thumbTipLocalXY : activeTipLocalXY;
        HasLivePoseIndicator = true;
        LiveIndicatorGateType = activeGateType;
        LiveIndicatorTipLocalXY = tipXY;

        bool hasVotedChargingGate = TryMapPoseToGate(votedPose, out NodeType updatedGate);
        bool hasCurrentChargingGate = TryMapPoseToGate(currentPose, out _);
        bool isPoseInDropout = false;
        if (hasVotedChargingGate || hasCurrentChargingGate)
        {
            poseDropoutTimer = 0f;
            if (!isGatePoseLocked && hasVotedChargingGate)
            {
                activeGateType = updatedGate;
                LiveIndicatorGateType = activeGateType;
                tipXY = activeGateType == NodeType.NOT ? thumbTipLocalXY : activeTipLocalXY;
                LiveIndicatorTipLocalXY = tipXY;
            }
        }
        else
        {
            // Toleransi kedip pose lebih lega (280 ms) setelah masuk fase Charging agar putaran AND (1 jari) / NOT (jempol)
            // tidak rontok di tengah jalan; tetap rekam titik lintasan agar kurva tidak loncat saat pose pulih.
            isPoseInDropout = true;
            poseDropoutTimer += dt;
            if (poseDropoutTimer >= ChargePoseDropoutGraceSeconds)
            {
                CancelToIdle();
                HasLivePoseIndicator = false;
                return;
            }
        }

        // Saat sudah Charging, jangan batalkan bila ujung jari melintas tepi papan; titik spawn tetap di-clamp aman ke AssemblyZone
        AppendTrailSample(tipXY, dt);
        if (stallTimer >= ChargeStallTimeoutSeconds)
        {
            CancelToIdle();
            HasLivePoseIndicator = false;
            return;
        }

        if (EvaluateCircleGeometry(out Vector2 cCenter, out float cRad, out float cAbsDeg, out float cCoherence, out float cCircularity, out float cTotalArcLength))
        {
            if (cRad > MaxCircleRadius || (cAbsDeg >= ShapeCheckAngleDegrees && cCoherence < MinDirectionalCoherence))
            {
                CancelToIdle();
                HasLivePoseIndicator = false;
                return;
            }

            if (cAbsDeg >= PoseLockAngleDegrees)
            {
                isGatePoseLocked = true;
            }

            circleCenterLocalXY = ClampToAssembly(cCenter, assemblyMinXY, assemblyMaxXY);
            circleRadius = Mathf.Max(MinCircleRadius, cRad);
            accumulatedSweepDeg = cAbsDeg;
            chargeProgress = Mathf.Clamp01(cAbsDeg / TargetSweepDegrees);

            // Evaluasi ketat jumlah titik, panjang goresan, radius, & circularity setelah sapuan penuh (>= 205 deg)
            bool shapeOk = !isPoseInDropout
                           && trailCount >= MinTrailPointsForCharge
                           && cTotalArcLength >= MinTotalArcLengthForReady
                           && cRad >= MinCircleRadius
                           && cCircularity >= MinCircularityRatio;
            if (cAbsDeg >= TargetSweepDegrees && cCoherence >= MinDirectionalCoherence && shapeOk)
            {
                EnterReadyStage(isRelaxedOpenFiveBloom);
            }
        }
    }

    private void EnterReadyStage(bool isRelaxedOpenFiveBloom)
    {
        stage = SummonStage.Ready;
        chargeProgress = 1f;
        lockedDeployCenterLocalXY = circleCenterLocalXY;
        requireStrictOpenFiveInReady = isRelaxedOpenFiveBloom;
        readyTimer = 0f;
        bloomHoldTimer = 0f;
        handLostTimer = 0f;
        poseDropoutTimer = 0f;
        trailCount = 0;
        hasPrevPoint = false;
        HasLivePoseIndicator = false;
    }

    private void TickReadyStage(
        float dt,
        bool isHandDetected,
        bool isFistActive,
        bool fistJustStarted,
        SummonHandPose currentPose,
        bool isRelaxedOpenFiveBloom)
    {
        // Kepal (Fist) di tahap Ready = Batal Instan & konsumsi edge kepal agar tidak salah ambil gerbang di belakangnya
        if (isFistActive || fistJustStarted)
        {
            ConsumedFistCancelThisFrame = true;
            CancelToIdle();
            postSummonCooldownTimer = 0.30f;
            return;
        }

        if (!isHandDetected)
        {
            handLostTimer += dt;
            if (handLostTimer >= ReadyHandLostGraceSeconds)
            {
                CancelToIdle();
            }
            return;
        }

        handLostTimer = 0f;
        readyTimer += dt;
        if (readyTimer >= ReadyWindowSeconds)
        {
            CancelToIdle();
            return;
        }

        if (requireStrictOpenFiveInReady && !isRelaxedOpenFiveBloom)
        {
            requireStrictOpenFiveInReady = false;
        }

        // Tahap 2 (Deploy): membuka telapak santai (OpenFiveBloom rileks >= 3 jari + meanCurl > 1.12 + ibu jari terbuka) stabil >= 30 ms.
        // Jika XOR masuk Ready dalam kondisi ibu jari sudah terbuka (relaxed sudah true), tunggu 4 jari + ibu jari penuh agar tidak auto-deploy.
        bool bloomPoseMet = requireStrictOpenFiveInReady
            ? (currentPose == SummonHandPose.OpenFiveBloom)
            : (isRelaxedOpenFiveBloom || currentPose == SummonHandPose.OpenFiveBloom);

        if (bloomPoseMet)
        {
            bloomHoldTimer += dt;
            if (bloomHoldTimer >= OpenFiveBloomDebounceSeconds)
            {
                BloomJustTriggeredThisFrame = true;
                DeployedGateType = activeGateType;
                DeployedLocalXY = lockedDeployCenterLocalXY;
                CancelToIdle();
                postSummonCooldownTimer = PostSummonCooldownSeconds;
            }
        }
        else
        {
            bloomHoldTimer = Mathf.Max(0f, bloomHoldTimer - (dt * 2f));
        }
    }

    private void CancelToIdle()
    {
        stage = SummonStage.Idle;
        trailCount = 0;
        isGatePoseLocked = false;
        requireStrictOpenFiveInReady = false;
        chargeProgress = 0f;
        accumulatedSweepDeg = 0f;
        poseDropoutTimer = 0f;
        stallTimer = 0f;
        handLostTimer = 0f;
        readyTimer = 0f;
        bloomHoldTimer = 0f;
        hasPrevPoint = false;
    }

    private void AppendTrailSample(Vector2 pt, float dt)
    {
        if (!hasPrevPoint)
        {
            trailPoints[0] = pt;
            trailCount = 1;
            prevSampleLocalXY = pt;
            hasPrevPoint = true;
            stallTimer = 0f;
            return;
        }

        if (Vector2.Distance(pt, prevSampleLocalXY) < MinPointSpacing)
        {
            stallTimer += dt;
            return;
        }

        stallTimer = 0f;
        prevSampleLocalXY = pt;

        if (trailCount < MaxTrailPoints)
        {
            trailPoints[trailCount++] = pt;
        }
        else
        {
            // Geser jendela geser 72 titik agar putaran lebar tetap utuh tanpa alokasi
            for (int i = 1; i < MaxTrailPoints; i++)
            {
                trailPoints[i - 1] = trailPoints[i];
            }
            trailPoints[MaxTrailPoints - 1] = pt;
        }
    }

    private bool EvaluateCircleGeometry(
        out Vector2 center,
        out float estimatedRadius,
        out float absSweepDeg,
        out float coherence,
        out float circularity,
        out float totalArcLength)
    {
        center = Vector2.zero;
        estimatedRadius = 0f;
        absSweepDeg = 0f;
        coherence = 0f;
        circularity = 0f;
        totalArcLength = 0f;

        if (trailCount < 3)
        {
            return false;
        }

        float minX = trailPoints[0].x;
        float maxX = minX;
        float minY = trailPoints[0].y;
        float maxY = minY;

        for (int i = 1; i < trailCount; i++)
        {
            Vector2 p = trailPoints[i];
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }

        float rx = (maxX - minX) * 0.5f;
        float ry = (maxY - minY) * 0.5f;
        float meanRadius = (rx + ry) * 0.5f;
        float maxAxis = Mathf.Max(rx, ry);
        float minAxis = Mathf.Min(rx, ry);

        // Untuk busur lingkaran, gabungkan meanRadius dan 0.85*maxAxis agar estimasi radius stabil
        estimatedRadius = Mathf.Max(meanRadius, maxAxis * 0.85f);
        center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        circularity = maxAxis > 0.001f ? (minAxis / maxAxis) : 0f;

        // Chord turning angle kumulatif segmen-ke-segmen (sum dHeading) + panjang total goresan:
        // 100% stabil dan independen dari pergeseran pusat lingkaran pada busur awal.
        float signedTurnRad = 0f;
        float absTurnRad = 0f;
        bool hasPrevDir = false;
        Vector2 prevDir = Vector2.zero;

        for (int i = 1; i < trailCount; i++)
        {
            Vector2 seg = trailPoints[i] - trailPoints[i - 1];
            float sqrLen = seg.sqrMagnitude;
            if (sqrLen < 0.0001f)
            {
                continue;
            }

            float segLen = Mathf.Sqrt(sqrLen);
            totalArcLength += segLen;
            Vector2 dir = seg / segLen;
            if (hasPrevDir)
            {
                float cross = (prevDir.x * dir.y) - (prevDir.y * dir.x);
                float dot = (prevDir.x * dir.x) + (prevDir.y * dir.y);
                float dHeading = Mathf.Atan2(cross, dot);
                float absDHeading = Mathf.Abs(dHeading);
                if (absDHeading <= MaxSingleTurnRadians)
                {
                    signedTurnRad += dHeading;
                    absTurnRad += absDHeading;
                }
                else
                {
                    // Pembalikan tajam (> 135 deg) hanya menambah penyebut sehingga menurunkan koherensi zig-zag
                    absTurnRad += absDHeading;
                }
            }

            prevDir = dir;
            hasPrevDir = true;
        }

        absSweepDeg = Mathf.Abs(signedTurnRad) * Mathf.Rad2Deg;
        coherence = absTurnRad > 0.05f ? (Mathf.Abs(signedTurnRad) / absTurnRad) : 0f;
        return true;
    }

    private SummonHandPose ComputeMajoritySummonPose()
    {
        if (poseVoteFilled < PoseVoteMajorityThreshold)
        {
            return SummonHandPose.None;
        }

        int cAnd = 0;
        int cOr = 0;
        int cXor = 0;
        int cNand = 0;
        int cNot = 0;

        for (int i = 0; i < poseVoteFilled; i++)
        {
            switch (poseVoteRing[i])
            {
                case SummonHandPose.OneFingerAnd: cAnd++; break;
                case SummonHandPose.TwoFingersOr: cOr++; break;
                case SummonHandPose.ThreeFingersXor: cXor++; break;
                case SummonHandPose.FourFingersNand: cNand++; break;
                case SummonHandPose.ThumbOnlyNot: cNot++; break;
            }
        }

        // Vote jendela 5 frame: butuh >= 3/5 agar gerakan transisi singkat tetap tertolak namun tahan 1-2 frame kedip saat memutar AND/NOT
        if (cAnd >= PoseVoteMajorityThreshold) return SummonHandPose.OneFingerAnd;
        if (cOr >= PoseVoteMajorityThreshold) return SummonHandPose.TwoFingersOr;
        if (cXor >= PoseVoteMajorityThreshold) return SummonHandPose.ThreeFingersXor;
        if (cNand >= PoseVoteMajorityThreshold) return SummonHandPose.FourFingersNand;
        if (cNot >= PoseVoteMajorityThreshold) return SummonHandPose.ThumbOnlyNot;
        return SummonHandPose.None;
    }

    private static bool TryMapPoseToGate(SummonHandPose pose, out NodeType gateType)
    {
        switch (pose)
        {
            case SummonHandPose.OneFingerAnd: gateType = NodeType.AND; return true;
            case SummonHandPose.TwoFingersOr: gateType = NodeType.OR; return true;
            case SummonHandPose.ThreeFingersXor: gateType = NodeType.XOR; return true;
            case SummonHandPose.FourFingersNand: gateType = NodeType.NAND; return true;
            case SummonHandPose.ThumbOnlyNot: gateType = NodeType.NOT; return true;
            default: gateType = NodeType.AND; return false;
        }
    }

    private static bool IsInsideSummonStartZone(Vector2 pt, Vector2 minXY, Vector2 maxXY)
    {
        return pt.x >= (minXY.x - StartZoneMarginX)
               && pt.x <= (maxXY.x + StartZoneMarginX)
               && pt.y >= (minXY.y - StartZoneMarginBottomY)
               && pt.y <= (maxXY.y + StartZoneMarginTopY);
    }

    private static Vector2 ClampToAssembly(Vector2 pt, Vector2 minXY, Vector2 maxXY)
    {
        return new Vector2(
            Mathf.Clamp(pt.x, minXY.x, maxXY.x),
            Mathf.Clamp(pt.y, minXY.y, maxXY.y));
    }
}
