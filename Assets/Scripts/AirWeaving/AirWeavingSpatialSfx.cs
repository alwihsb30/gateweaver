using System;
using UnityEngine;

/// <summary>
/// Synthesizer efek suara spasial prosedural PCM lengkap khusus Mode Udara (GateWeaver AR v7.5 Sprint 1).
/// - 100% prosedural via AudioClip.Create (0 aset audio/prefab eksternal, 0 diff pada LevelAndUIManager.cs).
/// - Seluruh AudioClip PCM 44100 Hz mono, pool 6 one-shot AudioSource, dan 1 dedicated looping AudioSource hum
///   dibuat sekali saat Awake()/EnsureInitialized() (Zero-GC penuh saat runtime) dan dibersihkan total di OnDestroy().
/// - Soft limiting tanh anti-clipping agar semua aksi utama terdengar tegas & renyah di speaker laptop/HP,
///   sementara continuous hum tetap jauh lebih lembut di latar belakang.
/// - Watchdog LateUpdate otomatis mematikan looping hum jika mute aktif, aksi berhenti, atau tidak di-update pada frame aktif
///   sehingga dengung tidak pernah nyangkut.
/// - Cooldown anti-spam dipisahkan per kategori SFX (array float[] tetap, Zero-GC).
/// </summary>
public sealed class AirWeavingSpatialSfx : MonoBehaviour
{
    private const int SampleRate = 44100;
    private const int VoicePoolSize = 6;
    private const float MaxStereoPan = 0.52f;
    private const float PanReferenceHalfWidth = 1.45f;
    private const string PrefKeyAudioMuted = "GW_v3_AudioMuted";

    private enum SfxCue
    {
        SummonChargeStep = 0,
        SummonChargeReady = 1,
        BloomSpawn = 2,
        SoftReject = 3,
        CableGrab = 4,
        CableSnapConnect = 5,
        CableDetach = 6,
        SwitchToggle = 7,
        OutsideBoxDelete = 8,
        TruthPodStep = 9,
        TruthPodRedWarning = 10,
        TruthPodPassAllFour = 11,
        GateGrab = 12,
        GatePlace = 13,
        HoverTick = 14,
        CableTargetLock = 15,
        CableTargetUnlock = 16,
        DeleteZoneEnter = 17,
        DeleteZoneExit = 18,
        SummonIgnite = 19,
        SummonPoseShift = 20,
        OutputHoldStep = 21,
        OutputHoldConfirm = 22,
        HologramWake = 23,
        Count = 24
    }

    private enum ContinuousHumMode
    {
        None = 0,
        CableDrag = 1,
        GateDrag = 2
    }

    // Cooldown anti-spam independen per kategori SFX (tidak saling memblokir antar kategori)
    private static readonly float[] CueCooldownSeconds =
    {
        0.045f, // SummonChargeStep
        0.060f, // SummonChargeReady
        0.060f, // BloomSpawn
        0.080f, // SoftReject
        0.035f, // CableGrab
        0.035f, // CableSnapConnect
        0.035f, // CableDetach
        0.035f, // SwitchToggle
        0.060f, // OutsideBoxDelete
        0.040f, // TruthPodStep
        0.080f, // TruthPodRedWarning
        0.100f, // TruthPodPassAllFour
        0.035f, // GateGrab
        0.035f, // GatePlace
        0.095f, // HoverTick
        0.040f, // CableTargetLock
        0.055f, // CableTargetUnlock
        0.085f, // DeleteZoneEnter
        0.085f, // DeleteZoneExit
        0.060f, // SummonIgnite
        0.055f, // SummonPoseShift
        0.050f, // OutputHoldStep
        0.080f, // OutputHoldConfirm
        0.250f  // HologramWake
    };

    private LevelAndUIManager levelManager;
    private Camera referenceCamera;
    private bool isInitialized = false;

    private GameObject voicesRootObj;
    private AudioSource[] voicePool;
    private AudioSource humVoice;
    private int nextVoiceIndex = 0;
    private readonly float[] lastCuePlayTimes = new float[(int)SfxCue.Count];

    private ContinuousHumMode activeHumMode = ContinuousHumMode.None;
    private int lastHumAliveFrame = -1;

    // Seluruh klip SFX prosedural Mode Udara (disintesis sekali di Awake / EnsureInitialized)
    private readonly AudioClip[] clipSummonChargeSteps = new AudioClip[3]; // 25%, 50%, 75%
    private AudioClip clipSummonChargeReady;
    private AudioClip clipSummonIgnite;
    private AudioClip clipSummonPoseShift;
    private AudioClip clipBloomSpawn;
    private AudioClip clipSoftReject;
    private AudioClip clipCableGrab;
    private AudioClip clipCableTargetLock;
    private AudioClip clipCableTargetUnlock;
    private AudioClip clipCableSnapConnect;
    private AudioClip clipCableDetach;
    private AudioClip clipSwitchToggleHigh;
    private AudioClip clipSwitchToggleLow;
    private AudioClip clipOutsideBoxDelete;
    private AudioClip clipDeleteZoneEnter;
    private AudioClip clipDeleteZoneExit;
    private readonly AudioClip[] clipTruthPodSteps = new AudioClip[4]; // 1/4, 2/4, 3/4, 4/4
    private AudioClip clipTruthPodRedWarning;
    private AudioClip clipTruthPodPassAllFour;
    private readonly AudioClip[] clipOutputHoldSteps = new AudioClip[3]; // 25%, 50%, 75%
    private AudioClip clipOutputHoldConfirm;
    private AudioClip clipGateGrab;
    private AudioClip clipGatePlace;
    private AudioClip clipHoverTick;
    private AudioClip clipHologramWake;

    // Loop PCM seamless (1.0 dtk, siklus integer penuh -> 0 klik sambungan loop)
    private AudioClip clipCableDragHumLoop;
    private AudioClip clipGateDragHumLoop;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void LateUpdate()
    {
        if (humVoice == null || !humVoice.isPlaying)
        {
            return;
        }

        // Watchdog anti-nyangkut: matikan segera jika audio di-mute atau hum tidak diperpanjang pada frame ini
        if (IsGloballyMuted() || (Time.frameCount - lastHumAliveFrame) > 1)
        {
            StopHumImmediate();
        }
    }

    public void Configure(LevelAndUIManager manager, Camera mainCam)
    {
        levelManager = manager;
        if (mainCam != null)
        {
            referenceCamera = mainCam;
        }
    }

    public void SetReferenceCamera(Camera mainCam)
    {
        if (mainCam != null)
        {
            referenceCamera = mainCam;
        }
    }

    public void EnsureInitialized()
    {
        if (isInitialized)
        {
            return;
        }

        for (int i = 0; i < lastCuePlayTimes.Length; i++)
        {
            lastCuePlayTimes[i] = -10f;
        }

        voicesRootObj = new GameObject("AR_AirWeavingSpatialSfxVoices");
        voicesRootObj.transform.SetParent(transform, false);

        voicePool = new AudioSource[VoicePoolSize];
        for (int i = 0; i < VoicePoolSize; i++)
        {
            GameObject voiceObj = new GameObject($"SpatialSfxVoice_{i}");
            voiceObj.transform.SetParent(voicesRootObj.transform, false);
            AudioSource src = voiceObj.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f;
            src.dopplerLevel = 0f;
            src.volume = 1f;
            src.panStereo = 0f;
            voicePool[i] = src;
        }

        GameObject humObj = new GameObject("SpatialSfxContinuousHum");
        humObj.transform.SetParent(voicesRootObj.transform, false);
        humVoice = humObj.AddComponent<AudioSource>();
        humVoice.playOnAwake = false;
        humVoice.loop = true;
        humVoice.spatialBlend = 0f;
        humVoice.dopplerLevel = 0f;
        humVoice.volume = 0f;
        humVoice.pitch = 1f;
        humVoice.panStereo = 0f;

        SynthesizeAllClips();
        isInitialized = true;
    }

    public void StopHumImmediate()
    {
        activeHumMode = ContinuousHumMode.None;
        lastHumAliveFrame = -1;
        if (humVoice != null)
        {
            humVoice.volume = 0f;
            if (humVoice.isPlaying)
            {
                humVoice.Stop();
            }
        }
    }

    public void StopAllImmediate()
    {
        StopHumImmediate();
        if (voicePool == null)
        {
            return;
        }

        for (int i = 0; i < voicePool.Length; i++)
        {
            if (voicePool[i] != null && voicePool[i].isPlaying)
            {
                voicePool[i].Stop();
            }
        }
    }

    // =========================================================================
    // CONTINUOUS LOOPING HUM (CABLE DRAG & GATE DRAG — ZERO-GC, FAIL-SAFE)
    // =========================================================================

    /// <summary>
    /// Dengung listrik/energi lembut selama menarik kabel (pitch bereaksi halus pada panjang kabel & lebih fokus saat mengunci port).
    /// </summary>
    public void UpdateCableDragHum(Vector3 tipWorldPos, float dragLength, bool isLockedTarget, float dt)
    {
        if (!isInitialized || humVoice == null || clipCableDragHumLoop == null || IsGloballyMuted())
        {
            StopHumImmediate();
            return;
        }

        if (activeHumMode != ContinuousHumMode.CableDrag || humVoice.clip != clipCableDragHumLoop)
        {
            activeHumMode = ContinuousHumMode.CableDrag;
            humVoice.clip = clipCableDragHumLoop;
            humVoice.volume = 0f;
            humVoice.Play();
        }
        else if (!humVoice.isPlaying)
        {
            humVoice.Play();
        }

        float lenT = Mathf.Clamp01(dragLength / 2.20f);
        float targetPitch = Mathf.Lerp(0.88f, 1.22f, lenT) + (isLockedTarget ? 0.16f : 0f);
        float targetVolume = isLockedTarget ? 0.36f : Mathf.Lerp(0.24f, 0.32f, lenT);
        float smoothAlpha = 1f - Mathf.Exp(-18f * Mathf.Clamp(dt, 0.001f, 0.05f));

        humVoice.pitch = Mathf.Lerp(humVoice.pitch, targetPitch, smoothAlpha);
        humVoice.volume = Mathf.Lerp(humVoice.volume, targetVolume, smoothAlpha);
        humVoice.panStereo = Mathf.Lerp(humVoice.panStereo, ComputeStereoPan(tipWorldPos), smoothAlpha);
        lastHumAliveFrame = Time.frameCount;
    }

    /// <summary>
    /// Dengung levitasi holografik rendah yang lembut selama menyeret gerbang dengan Fist (bereaksi pada kecepatan geser).
    /// </summary>
    public void UpdateGateDragHum(Vector3 gateWorldPos, float normalizedSpeed, bool inDeleteZone, float dt)
    {
        if (!isInitialized || humVoice == null || clipGateDragHumLoop == null || IsGloballyMuted())
        {
            StopHumImmediate();
            return;
        }

        if (activeHumMode != ContinuousHumMode.GateDrag || humVoice.clip != clipGateDragHumLoop)
        {
            activeHumMode = ContinuousHumMode.GateDrag;
            humVoice.clip = clipGateDragHumLoop;
            humVoice.volume = 0f;
            humVoice.Play();
        }
        else if (!humVoice.isPlaying)
        {
            humVoice.Play();
        }

        float speedT = Mathf.Clamp01(normalizedSpeed);
        float targetPitch = Mathf.Lerp(0.92f, 1.20f, speedT) - (inDeleteZone ? 0.07f : 0f);
        float targetVolume = Mathf.Lerp(0.22f, 0.36f, speedT);
        float smoothAlpha = 1f - Mathf.Exp(-16f * Mathf.Clamp(dt, 0.001f, 0.05f));

        humVoice.pitch = Mathf.Lerp(humVoice.pitch, targetPitch, smoothAlpha);
        humVoice.volume = Mathf.Lerp(humVoice.volume, targetVolume, smoothAlpha);
        humVoice.panStereo = Mathf.Lerp(humVoice.panStereo, ComputeStereoPan(gateWorldPos), smoothAlpha);
        lastHumAliveFrame = Time.frameCount;
    }

    // =========================================================================
    // PEMICU ONE-SHOT AUDIO TAKTIL MODE UDARA (100% ZERO-GC)
    // =========================================================================

    public void PlaySummonIgnite(Vector3 worldPos)
    {
        PlaySpatialClip(SfxCue.SummonIgnite, clipSummonIgnite, worldPos, 0.88f);
    }

    public void PlaySummonPoseShift(Vector3 worldPos)
    {
        PlaySpatialClip(SfxCue.SummonPoseShift, clipSummonPoseShift, worldPos, 0.80f);
    }

    public void PlaySummonChargeStep(int stepIndex, Vector3 worldPos)
    {
        int clamped = Mathf.Clamp(stepIndex, 0, clipSummonChargeSteps.Length - 1);
        PlaySpatialClip(SfxCue.SummonChargeStep, clipSummonChargeSteps[clamped], worldPos, 0.94f);
    }

    public void PlaySummonChargeReady(Vector3 worldPos)
    {
        PlaySpatialClip(SfxCue.SummonChargeReady, clipSummonChargeReady, worldPos, 0.98f);
    }

    public void PlayBloomSpawn(Vector3 worldPos)
    {
        PlaySpatialClip(SfxCue.BloomSpawn, clipBloomSpawn, worldPos, 1.0f);
    }

    public void PlaySoftReject(Vector3 worldPos)
    {
        PlaySpatialClip(SfxCue.SoftReject, clipSoftReject, worldPos, 0.96f);
    }

    public void PlayCableGrab(Vector3 worldPos)
    {
        PlaySpatialClip(SfxCue.CableGrab, clipCableGrab, worldPos, 1.0f);
    }

    public void PlayCableTargetLock(Vector3 worldPos)
    {
        PlaySpatialClip(SfxCue.CableTargetLock, clipCableTargetLock, worldPos, 0.98f);
    }

    public void PlayCableTargetUnlock(Vector3 worldPos)
    {
        PlaySpatialClip(SfxCue.CableTargetUnlock, clipCableTargetUnlock, worldPos, 0.80f);
    }

    public void PlayCableSnapConnect(Vector3 worldPos)
    {
        PlaySpatialClip(SfxCue.CableSnapConnect, clipCableSnapConnect, worldPos, 1.0f);
    }

    public void PlayCableDetach(Vector3 worldPos)
    {
        PlaySpatialClip(SfxCue.CableDetach, clipCableDetach, worldPos, 0.96f);
    }

    public void PlaySwitchToggle(bool newStateHigh, Vector3 worldPos)
    {
        AudioClip clip = newStateHigh ? clipSwitchToggleHigh : clipSwitchToggleLow;
        PlaySpatialClip(SfxCue.SwitchToggle, clip, worldPos, 1.0f);
    }

    public void PlayOutsideBoxDelete(Vector3 worldPos)
    {
        PlaySpatialClip(SfxCue.OutsideBoxDelete, clipOutsideBoxDelete, worldPos, 0.96f);
    }

    public void PlayDeleteZoneEnter(Vector3 worldPos)
    {
        PlaySpatialClip(SfxCue.DeleteZoneEnter, clipDeleteZoneEnter, worldPos, 0.94f);
    }

    public void PlayDeleteZoneExit(Vector3 worldPos)
    {
        PlaySpatialClip(SfxCue.DeleteZoneExit, clipDeleteZoneExit, worldPos, 0.86f);
    }

    public void PlayTruthPodStep(int passedRowCount, Vector3 worldPos)
    {
        int idx = Mathf.Clamp(passedRowCount - 1, 0, clipTruthPodSteps.Length - 1);
        PlaySpatialClip(SfxCue.TruthPodStep, clipTruthPodSteps[idx], worldPos, 0.96f);
    }

    public void PlayTruthPodRedWarning(Vector3 worldPos)
    {
        PlaySpatialClip(SfxCue.TruthPodRedWarning, clipTruthPodRedWarning, worldPos, 0.95f);
    }

    public void PlayTruthPodPassAllFour(Vector3 worldPos)
    {
        PlaySpatialClip(SfxCue.TruthPodPassAllFour, clipTruthPodPassAllFour, worldPos, 1.0f);
    }

    public void PlayOutputHoldStep(int stepIndex, Vector3 worldPos)
    {
        int clamped = Mathf.Clamp(stepIndex, 0, clipOutputHoldSteps.Length - 1);
        PlaySpatialClip(SfxCue.OutputHoldStep, clipOutputHoldSteps[clamped], worldPos, 0.94f);
    }

    public void PlayOutputHoldConfirm(Vector3 worldPos)
    {
        PlaySpatialClip(SfxCue.OutputHoldConfirm, clipOutputHoldConfirm, worldPos, 1.0f);
    }

    public void PlayGateGrab(Vector3 worldPos)
    {
        PlaySpatialClip(SfxCue.GateGrab, clipGateGrab, worldPos, 1.0f);
    }

    public void PlayGatePlace(Vector3 worldPos)
    {
        PlaySpatialClip(SfxCue.GatePlace, clipGatePlace, worldPos, 0.98f);
    }

    public void PlayHoverTick(Vector3 worldPos)
    {
        PlaySpatialClip(SfxCue.HoverTick, clipHoverTick, worldPos, 0.72f);
    }

    public void PlayHologramWake(Vector3 worldPos)
    {
        PlaySpatialClip(SfxCue.HologramWake, clipHologramWake, worldPos, 0.80f);
    }

    // =========================================================================
    // PEMUTARAN ZERO-GC, COOLDOWN PER KATEGORI, & HORMAT STATUS MUTE GLOBAL
    // =========================================================================

    private void PlaySpatialClip(SfxCue cue, AudioClip clip, Vector3 worldPos, float volumeScale)
    {
        if (!isInitialized || clip == null || voicePool == null || IsGloballyMuted())
        {
            return;
        }

        float now = Time.unscaledTime;
        int cueIdx = (int)cue;
        float minInterval = cueIdx < CueCooldownSeconds.Length ? CueCooldownSeconds[cueIdx] : 0.040f;
        if (now - lastCuePlayTimes[cueIdx] < minInterval)
        {
            return;
        }
        lastCuePlayTimes[cueIdx] = now;

        AudioSource voice = AcquireVoice();
        if (voice == null)
        {
            return;
        }

        voice.panStereo = ComputeStereoPan(worldPos);
        voice.volume = Mathf.Clamp01(volumeScale);
        voice.clip = clip;
        voice.Play();
    }

    private AudioSource AcquireVoice()
    {
        for (int i = 0; i < voicePool.Length; i++)
        {
            int idx = (nextVoiceIndex + i) % voicePool.Length;
            AudioSource candidate = voicePool[idx];
            if (candidate != null && !candidate.isPlaying)
            {
                nextVoiceIndex = (idx + 1) % voicePool.Length;
                return candidate;
            }
        }

        AudioSource fallback = voicePool[nextVoiceIndex];
        nextVoiceIndex = (nextVoiceIndex + 1) % voicePool.Length;
        return fallback;
    }

    private float ComputeStereoPan(Vector3 worldPos)
    {
        if (referenceCamera == null)
        {
            return 0f;
        }

        Vector3 localPos = referenceCamera.transform.InverseTransformPoint(worldPos);
        return Mathf.Clamp(localPos.x / PanReferenceHalfWidth, -MaxStereoPan, MaxStereoPan);
    }

    private bool IsGloballyMuted()
    {
        if (levelManager != null)
        {
            return levelManager.isAudioMuted;
        }

        return PlayerPrefs.GetInt(PrefKeyAudioMuted, 0) == 1;
    }

    // =========================================================================
    // SINTESIS PCM PROSEDURAL + SOFT LIMITING (DIPANGGIL SEKALI — 0 GC RUNTIME)
    // =========================================================================

    private void SynthesizeAllClips()
    {
        // (a) Circle Summon: Ignition, Pose-Shift, Charge 25/50/75%, & Ready
        clipSummonIgnite = CreateSweepChirpClip("AW_SFX_SummonIgnite", 420.0f, 820.0f, 0.056f, 0.32f);
        clipSummonPoseShift = CreateSweepChirpClip("AW_SFX_SummonPoseShift", 1220.0f, 1560.0f, 0.034f, 0.26f);
        clipSummonChargeSteps[0] = CreateSoftChargeTickClip("AW_SFX_Charge25", 392.00f, 415.30f, 0.060f, 0.27f);
        clipSummonChargeSteps[1] = CreateSoftChargeTickClip("AW_SFX_Charge50", 493.88f, 523.25f, 0.064f, 0.30f);
        clipSummonChargeSteps[2] = CreateSoftChargeTickClip("AW_SFX_Charge75", 587.33f, 622.25f, 0.068f, 0.33f);
        clipSummonChargeReady = CreateDualToneShimmerClip("AW_SFX_ChargeReady", 587.33f, 880.00f, 0.128f, 0.37f);

        // (b) Open-5 Bloom Spawn: arpeggio kristal futuristik naik (D5 -> A5 -> D6)
        clipBloomSpawn = CreateArpeggioChimeClip(
            "AW_SFX_BloomSpawn",
            new float[] { 587.33f, 880.00f, 1174.66f },
            0.062f,
            0.44f,
            overtoneMix: 0.26f);

        // (c) Soft Reject / Cancel / Over-Quota / Illegal Wire: bunyi tumpul rendah lembut
        clipSoftReject = CreateSoftRejectThudClip("AW_SFX_SoftReject", 305.0f, 190.0f, 0.120f, 0.38f);

        // (d) Cable Drag: Grab Pluck/Zip, Target Lock Ping, Target Unlock Micro, Snap Connect, & Soft Pop Detach
        clipCableGrab = CreateCableMagneticPluckZipClip("AW_SFX_CableGrab", 0.084f, 0.48f);
        clipCableTargetLock = CreateCableTargetLockPingClip("AW_SFX_CableTargetLock", 0.072f, 0.44f);
        clipCableTargetUnlock = CreateSweepChirpClip("AW_SFX_CableTargetUnlock", 940.0f, 580.0f, 0.044f, 0.24f);
        clipCableSnapConnect = CreateMagneticSnapClip("AW_SFX_CableSnap", 0.086f, 0.48f);
        clipCableDetach = CreateSoftPopDetachClip("AW_SFX_CableDetach", 600.0f, 225.0f, 0.074f, 0.40f);

        // (e) Index-Touch Switch Toggle: klik sakelar mekanis (1 = tinggi renyah, 0 = rendah mantap)
        clipSwitchToggleHigh = CreateMechanicalSwitchClickClip("AW_SFX_SwitchHigh", 880.0f, 1340.0f, 0.058f, 0.46f);
        clipSwitchToggleLow = CreateMechanicalSwitchClickClip("AW_SFX_SwitchLow", 560.0f, 340.0f, 0.058f, 0.44f);

        // (f) Gate Delete & Boundary Zone: Enter Warning, Safe-Return Exit, & Outside-Box Swoosh
        clipDeleteZoneEnter = CreateBoundaryAlertClip("AW_SFX_DeleteZoneEnter", 680.0f, 440.0f, 0.078f, 0.40f);
        clipDeleteZoneExit = CreateBoundaryAlertClip("AW_SFX_DeleteZoneExit", 460.0f, 660.0f, 0.064f, 0.32f);
        clipOutsideBoxDelete = CreateDownwardSwooshClip("AW_SFX_OutsideBoxDelete", 780.0f, 190.0f, 0.175f, 0.38f);

        // (g) Self-Debugging Truth Pod Chime: tangga nada naik 1/4 -> 4/4, warning lembut, & LULUS! 4/4
        clipTruthPodSteps[0] = CreateBellStepClip("AW_SFX_TruthPod_1of4", 523.25f, 0.100f, 0.32f);
        clipTruthPodSteps[1] = CreateBellStepClip("AW_SFX_TruthPod_2of4", 659.25f, 0.100f, 0.34f);
        clipTruthPodSteps[2] = CreateBellStepClip("AW_SFX_TruthPod_3of4", 783.99f, 0.108f, 0.36f);
        clipTruthPodSteps[3] = CreateBellStepClip("AW_SFX_TruthPod_4of4", 1046.50f, 0.120f, 0.38f);
        clipTruthPodRedWarning = CreateSoftWarningDoubleClip("AW_SFX_TruthPod_RedWarn", 349.23f, 311.13f, 0.150f, 0.34f);
        clipTruthPodPassAllFour = CreateArpeggioChimeClip(
            "AW_SFX_TruthPod_Pass4of4",
            new float[] { 1046.50f, 1318.51f, 1567.98f },
            0.066f,
            0.40f,
            overtoneMix: 0.22f);

        // (h) Zero-Touch Output Hold (25/50/75% + Confirm)
        clipOutputHoldSteps[0] = CreateSoftChargeTickClip("AW_SFX_OutHold25", 523.25f, 554.37f, 0.058f, 0.30f);
        clipOutputHoldSteps[1] = CreateSoftChargeTickClip("AW_SFX_OutHold50", 659.25f, 698.46f, 0.060f, 0.33f);
        clipOutputHoldSteps[2] = CreateSoftChargeTickClip("AW_SFX_OutHold75", 783.99f, 830.61f, 0.064f, 0.36f);
        clipOutputHoldConfirm = CreateArpeggioChimeClip(
            "AW_SFX_OutHoldConfirm",
            new float[] { 783.99f, 1046.50f, 1567.98f },
            0.054f,
            0.44f,
            overtoneMix: 0.24f);

        // (i) Manipulasi Objek & Navigasi: Gate Grab, Gate Place, Hover Micro-Tick, & Hologram Wake
        clipGateGrab = CreateGateThumpLockClip("AW_SFX_GateGrab", 0.078f, 0.50f);
        clipGatePlace = CreateGateTactileSettleClip("AW_SFX_GatePlace", 0.066f, 0.44f);
        clipHoverTick = CreateHoverMicroTickClip("AW_SFX_HoverTick", 0.022f, 0.20f);
        clipHologramWake = CreateHologramWakeClip("AW_SFX_HologramWake", 0.155f, 0.26f);

        // (j) Loop PCM Seamless 1.0 dtk (frekuensi integer -> nol klik sambungan)
        clipCableDragHumLoop = CreateSeamlessHumLoopClip("AW_SFX_CableDragHumLoop", 180, 270, 540, 3, 0.15f);
        clipGateDragHumLoop = CreateSeamlessHumLoopClip("AW_SFX_GateDragHumLoop", 110, 220, 330, 2, 0.16f);
    }

    private static AudioClip CreateSeamlessHumLoopClip(
        string clipName,
        int baseHz,
        int harmonicAHz,
        int harmonicBHz,
        int lfoHz,
        float amplitude)
    {
        int count = SampleRate; // tepat 1.0 detik agar setiap frekuensi integer menyelesaikan siklus utuh (0 seam click)
        float[] samples = new float[count];
        double twoPiOverN = (2.0 * Math.PI) / count;

        for (int i = 0; i < count; i++)
        {
            double angleUnit = i * twoPiOverN;
            double lfo = 0.88 + (0.12 * Math.Sin(angleUnit * lfoHz));
            double wave = (0.56 * Math.Sin(angleUnit * baseHz))
                          + (0.32 * Math.Sin(angleUnit * harmonicAHz))
                          + (0.12 * Math.Sin(angleUnit * harmonicBHz));
            samples[i] = SoftLimit((float)(wave * amplitude * lfo));
        }

        return CreateMonoClip(clipName, samples);
    }

    private static AudioClip CreateCableTargetLockPingClip(string clipName, float durationSec, float amplitude)
    {
        // Ping kunci magnetik nada tinggi bersih & renyah (F6 -> A6, berbeda jelas dari magnetic snap low-mid CableConnect)
        int count = Mathf.Max(2, Mathf.RoundToInt(durationSec * SampleRate));
        float[] samples = new float[count];
        double phaseA = 0.0;
        double phaseB = 0.0;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float freqA = Mathf.Lerp(1318.5f, 1760.0f, Mathf.Clamp01(t * 2.8f));
            float freqB = 2637.0f;
            phaseA += (2.0 * Math.PI * freqA) / SampleRate;
            phaseB += (2.0 * Math.PI * freqB) / SampleRate;

            float envA = Mathf.Exp(-4.6f * t);
            float envB = Mathf.Exp(-14.0f * t);
            double wave = (0.78 * Math.Sin(phaseA) * envA)
                          + (0.28 * Math.Sin(phaseB) * envB);
            samples[i] = SoftLimit((float)(wave * amplitude));
        }

        ApplySmoothEnds(samples, count, 0.0015f, 0.009f);
        return CreateMonoClip(clipName, samples);
    }

    private static AudioClip CreateSweepChirpClip(string clipName, float startFreq, float endFreq, float durationSec, float amplitude)
    {
        int count = Mathf.Max(2, Mathf.RoundToInt(durationSec * SampleRate));
        float[] samples = new float[count];
        double phase = 0.0;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float freq = startFreq * Mathf.Pow(endFreq / startFreq, t);
            phase += (2.0 * Math.PI * freq) / SampleRate;

            float env = Mathf.Sin(Mathf.Pow(t, 0.72f) * Mathf.PI) * Mathf.Exp(-1.6f * t);
            double wave = (0.82 * Math.Sin(phase)) + (0.18 * Math.Sin(phase * 2.0));
            samples[i] = SoftLimit((float)(wave * amplitude * env));
        }

        ApplySmoothEnds(samples, count, 0.002f, 0.006f);
        return CreateMonoClip(clipName, samples);
    }

    private static AudioClip CreateBoundaryAlertClip(string clipName, float freqFirst, float freqSecond, float durationSec, float amplitude)
    {
        int count = Mathf.Max(2, Mathf.RoundToInt(durationSec * SampleRate));
        int split = (count * 45) / 100;
        float[] samples = new float[count];
        double phase = 0.0;

        for (int i = 0; i < count; i++)
        {
            bool second = i >= split;
            float localT = second
                ? ((i - split) / (float)Mathf.Max(1, count - split - 1))
                : (i / (float)Mathf.Max(1, split - 1));
            float freq = second ? freqSecond : freqFirst;
            phase += (2.0 * Math.PI * freq) / SampleRate;

            float env = Mathf.Sin(localT * Mathf.PI) * Mathf.Exp(-1.4f * localT);
            double wave = (0.76 * Math.Sin(phase)) + (0.24 * Math.Sin(phase * 2.0));
            samples[i] = SoftLimit((float)(wave * amplitude * env));
        }

        ApplySmoothEnds(samples, count, 0.002f, 0.008f);
        return CreateMonoClip(clipName, samples);
    }

    private static AudioClip CreateHologramWakeClip(string clipName, float durationSec, float amplitude)
    {
        int count = Mathf.Max(2, Mathf.RoundToInt(durationSec * SampleRate));
        float[] samples = new float[count];
        double phaseA = 0.0;
        double phaseB = 0.0;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float freqA = Mathf.Lerp(330.0f, 659.25f, t);
            float freqB = Mathf.Lerp(659.25f, 987.77f, t);
            phaseA += (2.0 * Math.PI * freqA) / SampleRate;
            phaseB += (2.0 * Math.PI * freqB) / SampleRate;

            float swell = Mathf.Sin(Mathf.Pow(t, 0.70f) * Mathf.PI);
            double wave = (0.64 * Math.Sin(phaseA)) + (0.36 * Math.Sin(phaseB));
            samples[i] = SoftLimit((float)(wave * amplitude * swell));
        }

        ApplySmoothEnds(samples, count, 0.012f, 0.024f);
        return CreateMonoClip(clipName, samples);
    }

    private static AudioClip CreateGateThumpLockClip(string clipName, float durationSec, float amplitude)
    {
        int count = Mathf.Max(2, Mathf.RoundToInt(durationSec * SampleRate));
        float[] samples = new float[count];
        double punchPhase = 0.0;
        double clickPhase = 0.0;
        double lockPhase = 0.0;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float punchFreq = Mathf.Lerp(230f, 108f, Mathf.Sqrt(t));
            float clickFreq = Mathf.Lerp(1550f, 2280f, Mathf.Clamp01(t * 5.0f));
            float lockFreq = Mathf.Lerp(460f, 640f, Mathf.Clamp01(t * 2.2f));

            punchPhase += (2.0 * Math.PI * punchFreq) / SampleRate;
            clickPhase += (2.0 * Math.PI * clickFreq) / SampleRate;
            lockPhase += (2.0 * Math.PI * lockFreq) / SampleRate;

            float punchEnv = Mathf.Exp(-5.2f * t);
            float clickEnv = Mathf.Exp(-26.0f * t);
            float lockEnv = Mathf.Clamp01(t * 14f) * Mathf.Exp(-4.6f * t);

            double punchWave = (0.76 * Math.Sin(punchPhase)) + (0.34 * Math.Sin(punchPhase * 2.0));
            double clickWave = Math.Sin(clickPhase) + (0.28 * Math.Sin(clickPhase * 1.5));
            double lockWave = (0.72 * Math.Sin(lockPhase)) + (0.22 * Math.Sin(lockPhase * 2.0));

            double mixed = (0.64 * punchWave * punchEnv)
                           + (0.34 * clickWave * clickEnv)
                           + (0.36 * lockWave * lockEnv);
            samples[i] = SoftLimit((float)(mixed * amplitude));
        }

        ApplySmoothEnds(samples, count, 0.0018f, 0.009f);
        return CreateMonoClip(clipName, samples);
    }

    private static AudioClip CreateGateTactileSettleClip(string clipName, float durationSec, float amplitude)
    {
        int count = Mathf.Max(2, Mathf.RoundToInt(durationSec * SampleRate));
        float[] samples = new float[count];
        double bodyPhase = 0.0;
        double tapPhase = 0.0;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float bodyFreq = Mathf.Lerp(320f, 168f, t);
            float tapFreq = Mathf.Lerp(920f, 520f, Mathf.Clamp01(t * 2.6f));

            bodyPhase += (2.0 * Math.PI * bodyFreq) / SampleRate;
            tapPhase += (2.0 * Math.PI * tapFreq) / SampleRate;

            float bodyEnv = Mathf.Exp(-5.6f * t);
            float tapEnv = Mathf.Exp(-15.5f * t);

            double bodyWave = (0.78 * Math.Sin(bodyPhase)) + (0.26 * Math.Sin(bodyPhase * 2.0));
            double tapWave = Math.Sin(tapPhase) + (0.20 * Math.Sin(tapPhase * 2.0));

            double mixed = (0.68 * bodyWave * bodyEnv) + (0.42 * tapWave * tapEnv);
            samples[i] = SoftLimit((float)(mixed * amplitude));
        }

        ApplySmoothEnds(samples, count, 0.002f, 0.008f);
        return CreateMonoClip(clipName, samples);
    }

    private static AudioClip CreateHoverMicroTickClip(string clipName, float durationSec, float amplitude)
    {
        int count = Mathf.Max(2, Mathf.RoundToInt(durationSec * SampleRate));
        float[] samples = new float[count];
        double phase = 0.0;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float freq = Mathf.Lerp(1720f, 2180f, t);
            phase += (2.0 * Math.PI * freq) / SampleRate;

            float env = Mathf.Exp(-5.8f * t);
            double wave = (0.86 * Math.Sin(phase)) + (0.14 * Math.Sin(phase * 1.5));
            samples[i] = SoftLimit((float)(wave * amplitude * env));
        }

        ApplySmoothEnds(samples, count, 0.0015f, 0.004f);
        return CreateMonoClip(clipName, samples);
    }

    private static AudioClip CreateCableMagneticPluckZipClip(string clipName, float durationSec, float amplitude)
    {
        int count = Mathf.Max(2, Mathf.RoundToInt(durationSec * SampleRate));
        float[] samples = new float[count];
        double pluckPhase = 0.0;
        double zipPhase = 0.0;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float pluckFreq = Mathf.Lerp(390f, 210f, Mathf.Clamp01(t * 5.0f));
            float zipFreq = 480f * Mathf.Pow(1580f / 480f, t);

            pluckPhase += (2.0 * Math.PI * pluckFreq) / SampleRate;
            zipPhase += (2.0 * Math.PI * zipFreq) / SampleRate;

            float pluckEnv = Mathf.Exp(-24.0f * t);
            float zipEnv = Mathf.Clamp01(t * 16f) * Mathf.Exp(-2.9f * t);

            double pluckWave = (0.80 * Math.Sin(pluckPhase)) + (0.32 * Math.Sin(pluckPhase * 3.0));
            double zipWave = (0.72 * Math.Sin(zipPhase))
                             + (0.28 * Math.Sin(zipPhase * 2.0))
                             + (0.10 * Math.Sin(zipPhase * 3.0));

            double mixed = (0.50 * pluckWave * pluckEnv) + (0.76 * zipWave * zipEnv);
            samples[i] = SoftLimit((float)(mixed * amplitude));
        }

        ApplySmoothEnds(samples, count, 0.0016f, 0.009f);
        return CreateMonoClip(clipName, samples);
    }

    private static AudioClip CreateSoftChargeTickClip(string clipName, float startFreq, float endFreq, float durationSec, float amplitude)
    {
        int count = Mathf.Max(2, Mathf.RoundToInt(durationSec * SampleRate));
        float[] samples = new float[count];
        double phase = 0.0;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float freq = Mathf.Lerp(startFreq, endFreq, t);
            phase += (2.0 * Math.PI * freq) / SampleRate;

            float decay = Mathf.Exp(-3.2f * t);
            double wave = Math.Sin(phase) + (0.20 * Math.Sin(phase * 2.0));
            samples[i] = SoftLimit((float)(wave * amplitude * decay));
        }

        ApplySmoothEnds(samples, count, 0.004f, 0.008f);
        return CreateMonoClip(clipName, samples);
    }

    private static AudioClip CreateDualToneShimmerClip(string clipName, float freqA, float freqB, float durationSec, float amplitude)
    {
        int count = Mathf.Max(2, Mathf.RoundToInt(durationSec * SampleRate));
        float[] samples = new float[count];
        double phaseA = 0.0;
        double phaseB = 0.0;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            phaseA += (2.0 * Math.PI * freqA) / SampleRate;
            phaseB += (2.0 * Math.PI * (freqB * (1f + (0.015f * t)))) / SampleRate;

            float env = Mathf.Exp(-2.4f * t);
            double wave = (0.62 * Math.Sin(phaseA)) + (0.50 * Math.Sin(phaseB)) + (0.14 * Math.Sin(phaseB * 2.0));
            samples[i] = SoftLimit((float)(wave * amplitude * env));
        }

        ApplySmoothEnds(samples, count, 0.005f, 0.014f);
        return CreateMonoClip(clipName, samples);
    }

    private static AudioClip CreateArpeggioChimeClip(string clipName, float[] notes, float noteDurationSec, float amplitude, float overtoneMix)
    {
        int segSamples = Mathf.Max(2, Mathf.RoundToInt(noteDurationSec * SampleRate));
        int tailSamples = Mathf.RoundToInt(0.042f * SampleRate);
        int totalSamples = (segSamples * notes.Length) + tailSamples;
        float[] samples = new float[totalSamples];

        for (int n = 0; n < notes.Length; n++)
        {
            int startIdx = n * segSamples;
            int ringLen = Mathf.Min(totalSamples - startIdx, segSamples + tailSamples);
            double phase = 0.0;
            double step = (2.0 * Math.PI * notes[n]) / SampleRate;

            for (int j = 0; j < ringLen; j++)
            {
                phase += step;
                float localT = j / (float)Mathf.Max(1, ringLen - 1);
                float attack = Mathf.Clamp01(j / (0.0035f * SampleRate));
                float decay = Mathf.Exp(-3.5f * localT);
                double wave = Math.Sin(phase) + (overtoneMix * Math.Sin(phase * 2.0)) + (0.08 * Math.Sin(phase * 3.0));
                samples[startIdx + j] += (float)(wave * amplitude * 0.78f * attack * decay);
            }
        }

        for (int i = 0; i < totalSamples; i++)
        {
            samples[i] = SoftLimit(samples[i]);
        }

        ApplySmoothEnds(samples, totalSamples, 0.003f, 0.014f);
        return CreateMonoClip(clipName, samples);
    }

    private static AudioClip CreateSoftRejectThudClip(string clipName, float startFreq, float endFreq, float durationSec, float amplitude)
    {
        int count = Mathf.Max(2, Mathf.RoundToInt(durationSec * SampleRate));
        float[] samples = new float[count];
        double phase = 0.0;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float freq = Mathf.Lerp(startFreq, endFreq, t * t);
            phase += (2.0 * Math.PI * freq) / SampleRate;

            float bodyEnv = Mathf.Exp(-3.8f * t);
            float softDouble = 0.78f + (0.22f * Mathf.Cos(t * Mathf.PI * 2.5f));
            double wave = (0.78 * Math.Sin(phase)) + (0.22 * Math.Sin(phase * 1.5)) + (0.10 * Math.Sin(phase * 2.0));
            samples[i] = SoftLimit((float)(wave * amplitude * bodyEnv * softDouble));
        }

        ApplySmoothEnds(samples, count, 0.006f, 0.016f);
        return CreateMonoClip(clipName, samples);
    }

    private static AudioClip CreateMechanicalSwitchClickClip(
        string clipName,
        float startFreq,
        float endFreq,
        float durationSec,
        float amplitude)
    {
        int count = Mathf.Max(2, Mathf.RoundToInt(durationSec * SampleRate));
        float[] samples = new float[count];
        double phase = 0.0;
        double clickPhase = 0.0;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float freq = Mathf.Lerp(startFreq, endFreq, Mathf.Sqrt(t));
            phase += (2.0 * Math.PI * freq) / SampleRate;
            clickPhase += (2.0 * Math.PI * 2550.0) / SampleRate;

            float bodyEnv = Mathf.Exp(-4.6f * t);
            float transientEnv = Mathf.Exp(-30.0f * t);
            double wave = (0.78 * Math.Sin(phase))
                          + (0.24 * Math.Sin(phase * 2.0))
                          + (0.42 * Math.Sin(clickPhase) * transientEnv);
            samples[i] = SoftLimit((float)(wave * amplitude * bodyEnv));
        }

        ApplySmoothEnds(samples, count, 0.0016f, 0.007f);
        return CreateMonoClip(clipName, samples);
    }

    private static AudioClip CreateMagneticSnapClip(string clipName, float durationSec, float amplitude)
    {
        int count = Mathf.Max(2, Mathf.RoundToInt(durationSec * SampleRate));
        float[] samples = new float[count];
        double thumpPhase = 0.0;
        double pingPhase = 0.0;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float thumpFreq = Mathf.Lerp(360f, 165f, Mathf.Clamp01(t * 4.2f));
            float pingFreq = Mathf.Lerp(720f, 1440.0f, Mathf.Clamp01(t * 2.2f));
            thumpPhase += (2.0 * Math.PI * thumpFreq) / SampleRate;
            pingPhase += (2.0 * Math.PI * pingFreq) / SampleRate;

            float thumpEnv = Mathf.Exp(-16.0f * t);
            float pingEnv = Mathf.Clamp01(t * 18f) * Mathf.Exp(-4.2f * t);
            double thumpWave = (0.72 * Math.Sin(thumpPhase)) + (0.28 * Math.Sin(thumpPhase * 2.0));
            double pingWave = (0.70 * Math.Sin(pingPhase)) + (0.22 * Math.Sin(pingPhase * 2.0));
            double wave = (0.62 * thumpWave * thumpEnv) + (0.72 * pingWave * pingEnv);
            samples[i] = SoftLimit((float)(wave * amplitude));
        }

        ApplySmoothEnds(samples, count, 0.0018f, 0.010f);
        return CreateMonoClip(clipName, samples);
    }

    private static AudioClip CreateSoftPopDetachClip(string clipName, float startFreq, float endFreq, float durationSec, float amplitude)
    {
        int count = Mathf.Max(2, Mathf.RoundToInt(durationSec * SampleRate));
        float[] samples = new float[count];
        double phase = 0.0;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float freq = Mathf.Lerp(startFreq, endFreq, 1f - ((1f - t) * (1f - t)));
            phase += (2.0 * Math.PI * freq) / SampleRate;

            float env = Mathf.Sin(t * Mathf.PI) * Mathf.Exp(-1.8f * t);
            double wave = (0.86 * Math.Sin(phase)) + (0.18 * Math.Sin(phase * 2.0));
            samples[i] = SoftLimit((float)(wave * amplitude * env));
        }

        ApplySmoothEnds(samples, count, 0.0025f, 0.008f);
        return CreateMonoClip(clipName, samples);
    }

    private static AudioClip CreateDownwardSwooshClip(string clipName, float startFreq, float endFreq, float durationSec, float amplitude)
    {
        int count = Mathf.Max(2, Mathf.RoundToInt(durationSec * SampleRate));
        float[] samples = new float[count];
        double phase = 0.0;
        double shimmerPhase = 0.0;
        uint lcg = 0x5A17C31Du;
        float lpNoise = 0f;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float glide = Mathf.Pow(endFreq / startFreq, t);
            float freq = startFreq * glide;
            phase += (2.0 * Math.PI * freq) / SampleRate;
            shimmerPhase += (2.0 * Math.PI * (freq * 1.5)) / SampleRate;

            lcg = (lcg * 1664525u) + 1013904223u;
            float white = ((lcg >> 16) / 32767.5f) - 1f;
            lpNoise = (lpNoise * 0.86f) + (white * 0.14f);

            float env = Mathf.Sin(Mathf.Pow(t, 0.65f) * Mathf.PI);
            double wave = (0.70 * Math.Sin(phase)) + (0.20 * Math.Sin(shimmerPhase)) + (0.28 * lpNoise);
            samples[i] = SoftLimit((float)(wave * amplitude * env));
        }

        ApplySmoothEnds(samples, count, 0.008f, 0.020f);
        return CreateMonoClip(clipName, samples);
    }

    private static AudioClip CreateBellStepClip(string clipName, float baseFreq, float durationSec, float amplitude)
    {
        int count = Mathf.Max(2, Mathf.RoundToInt(durationSec * SampleRate));
        float[] samples = new float[count];
        double phase = 0.0;
        double step = (2.0 * Math.PI * baseFreq) / SampleRate;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            phase += step;

            float env = Mathf.Exp(-3.3f * t);
            float overtoneEnv = Mathf.Exp(-6.0f * t);
            double wave = (0.80 * Math.Sin(phase)) + (0.26 * Math.Sin(phase * 2.0) * overtoneEnv);
            samples[i] = SoftLimit((float)(wave * amplitude * env));
        }

        ApplySmoothEnds(samples, count, 0.003f, 0.012f);
        return CreateMonoClip(clipName, samples);
    }

    private static AudioClip CreateSoftWarningDoubleClip(string clipName, float freqFirst, float freqSecond, float durationSec, float amplitude)
    {
        int count = Mathf.Max(2, Mathf.RoundToInt(durationSec * SampleRate));
        int half = count / 2;
        float[] samples = new float[count];
        double phase = 0.0;

        for (int i = 0; i < count; i++)
        {
            bool secondHalf = i >= half;
            float localT = secondHalf
                ? ((i - half) / (float)Mathf.Max(1, count - half - 1))
                : (i / (float)Mathf.Max(1, half - 1));
            float freq = secondHalf ? freqSecond : freqFirst;
            phase += (2.0 * Math.PI * freq) / SampleRate;

            float env = Mathf.Sin(localT * Mathf.PI) * (secondHalf ? 0.92f : 1.0f);
            double wave = (0.82 * Math.Sin(phase)) + (0.20 * Math.Sin(phase * 2.0));
            samples[i] = SoftLimit((float)(wave * amplitude * env));
        }

        ApplySmoothEnds(samples, count, 0.004f, 0.012f);
        return CreateMonoClip(clipName, samples);
    }

    private static float SoftLimit(float sample)
    {
        const float ceiling = 0.92f;
        return (float)(ceiling * Math.Tanh(sample / ceiling));
    }

    private static void ApplySmoothEnds(float[] samples, int sampleCount, float fadeInSec, float fadeOutSec)
    {
        int fadeIn = Mathf.Clamp(Mathf.RoundToInt(fadeInSec * SampleRate), 1, sampleCount / 2);
        int fadeOut = Mathf.Clamp(Mathf.RoundToInt(fadeOutSec * SampleRate), 1, sampleCount / 2);

        for (int i = 0; i < fadeIn; i++)
        {
            float t = i / (float)fadeIn;
            samples[i] *= 0.5f * (1f - Mathf.Cos(Mathf.PI * t));
        }

        for (int i = 0; i < fadeOut; i++)
        {
            float t = i / (float)fadeOut;
            samples[sampleCount - 1 - i] *= 0.5f * (1f - Mathf.Cos(Mathf.PI * t));
        }
    }

    private static AudioClip CreateMonoClip(string clipName, float[] samples)
    {
        AudioClip clip = AudioClip.Create(clipName, samples.Length, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    // =========================================================================
    // PEMBERSIHAN LIFECYCLE (ZERO LEAK)
    // =========================================================================

    private void OnDisable()
    {
        StopAllImmediate();
    }

    private void OnDestroy()
    {
        StopAllImmediate();

        for (int i = 0; i < clipSummonChargeSteps.Length; i++)
        {
            DestroyClipRef(ref clipSummonChargeSteps[i]);
        }
        DestroyClipRef(ref clipSummonChargeReady);
        DestroyClipRef(ref clipSummonIgnite);
        DestroyClipRef(ref clipSummonPoseShift);
        DestroyClipRef(ref clipBloomSpawn);
        DestroyClipRef(ref clipSoftReject);
        DestroyClipRef(ref clipCableGrab);
        DestroyClipRef(ref clipCableTargetLock);
        DestroyClipRef(ref clipCableTargetUnlock);
        DestroyClipRef(ref clipCableSnapConnect);
        DestroyClipRef(ref clipCableDetach);
        DestroyClipRef(ref clipSwitchToggleHigh);
        DestroyClipRef(ref clipSwitchToggleLow);
        DestroyClipRef(ref clipOutsideBoxDelete);
        DestroyClipRef(ref clipDeleteZoneEnter);
        DestroyClipRef(ref clipDeleteZoneExit);
        for (int i = 0; i < clipTruthPodSteps.Length; i++)
        {
            DestroyClipRef(ref clipTruthPodSteps[i]);
        }
        DestroyClipRef(ref clipTruthPodRedWarning);
        DestroyClipRef(ref clipTruthPodPassAllFour);
        for (int i = 0; i < clipOutputHoldSteps.Length; i++)
        {
            DestroyClipRef(ref clipOutputHoldSteps[i]);
        }
        DestroyClipRef(ref clipOutputHoldConfirm);
        DestroyClipRef(ref clipGateGrab);
        DestroyClipRef(ref clipGatePlace);
        DestroyClipRef(ref clipHoverTick);
        DestroyClipRef(ref clipHologramWake);
        DestroyClipRef(ref clipCableDragHumLoop);
        DestroyClipRef(ref clipGateDragHumLoop);

        if (voicesRootObj != null)
        {
            Destroy(voicesRootObj);
            voicesRootObj = null;
        }
        voicePool = null;
        humVoice = null;
        levelManager = null;
        referenceCamera = null;
        isInitialized = false;
    }

    private static void DestroyClipRef(ref AudioClip clip)
    {
        if (clip != null)
        {
            Destroy(clip);
            clip = null;
        }
    }
}
