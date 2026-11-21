# GEMINI.md — Aturan Arsitektur & Alur Kerja GateWeaver AR

Dokumen ini adalah panduan wajib bagi AI Agent (Antigravity) dalam mengembangkan **GateWeaver AR** (rilis stabil saat ini: **`v7.5`**, branch `main`). Baca seluruh aturan ini sebelum merencanakan atau mengubah kode apa pun.

> Untuk detail desain gameplay, parameter mekanik, peta tata letak HUD, kurikulum level, dan backlog fitur, baca `docs/GDD.md`.

## 1. Identitas & Tech Stack Terkunci
- **Tujuan Produk:** Game edukasi AR gerbang logika & aljabar Boolean untuk **Kurikulum Merdeka Fase E (Informatika SMA)** dengan 7 level kurikulum (hingga NOR & Hukum De Morgan) + mode Sandbox.
- **Engine & Render Pipeline:** Unity 6 (`6000.6.9f1`), **Built-in Render Pipeline** (BUKAN URP/HDRP).
- **Dual-Mode AR (`v7.5`):**
  - `Mode: Kartu (v5)` — **Vuforia Engine 11.4.4** (8 kartu marker fisik, tetap utuh 0% regresi + diperkaya *Open Cutaway*, Pod Tabel Kebenaran `2x2`, *Live Boolean Expression Chip*, & `Rapor Penguasaan`).
  - `Mode: Udara (v7)` — **MediaPipeUnityPlugin v0.16.3** (`HandLandmarker`, `Delegate.CPU`, model `Assets/StreamingAssets/hand_landmarker.task`) + `WebCamTexture` (kamera depan cermin di Android), hologram 3D melayang, HUD *Zero-Touch* + Onboarding 1 Halaman & Latihan Cepat 4 Langkah, kontrol dual-kanal (`Circle Summon + Open-5`, `Pinch` kabel + *Pulsing Signal Packets*, `Fist` objek, Sentuh Telunjuk `#8` sakelar), audio spasial PCM prosedural penuh, *Live Boolean Expression Chip*, & 100% debugging mandiri instan anti-*false-win*.
- **Target Build Android:** Minimum API Level 29, Target API Automatic, IL2CPP, ARM64 only, **OpenGLES3 only**, Landscape Left, New Input System (`activeInputHandler: 1`), `bundleVersion: 7.5`, `AndroidBundleVersionCode: 8`.
- **LARANGAN KERAS STACK:** Jangan pernah menginstal **AR Foundation**, **XR Plug-in Management**, **XR Simulation**, atau **URP**. Paket tersebut bentrok dengan Vuforia 11.4.4 & MediaPipe.

## 2. Batas Arsitektur, Peta Komponen v7.5, & Prinsip Kode
- **Code-First (Lindungi Scene):** Dilarang mengedit YAML `Assets/Scenes/SampleScene.unity` atau `.prefab` secara manual kecuali diminta eksplisit. Gunakan *runtime auto-bootstrap* atau skrip C# di `Assets/Editor/`.
- **Tracking-Agnostic Core:** `Assets/Scripts/Core/CircuitManager.cs`, `GateNode.cs`, `GateMechanismVisual.cs`, dan `OutputTruthPodVisual.cs` harus tetap bebas dari `using Vuforia;` dan bebas dependensi MediaPipe. Pertahankan `CircuitManager.cs` seminimal mungkin diubah (di Mode Kartu tetap 100% auto-snap spasial v5; di Mode Udara mendukung koneksi manual port-ke-port & *fan-out*).
- **Isolasi Modul Air-Weaving v7.5:** Seluruh logika hand-tracking, proyeksi adaptif (*One-Euro-style*), deteksi gestur dual-kanal, hologram 3D melayang, kabel manual & *Pulsing Signal Packets*, 2-tahap *Circle Summon*, pemandu Output mandiri, synthesizer audio spasial PCM, serta onboarding panduan gestur terisolasi di `Assets/Scripts/AirWeaving/`. Gunakan pola *decoupled* berbasis C# `event`/`Action` untuk fitur baru (jangan me-*refactor* kode stabil ke `ScriptableObject Events` tanpa alasan kuat).
- **Zero-GC di Runtime Loop:** Dilarang melakukan alokasi memori (`new`, LINQ, `GetComponent`, string concat berat) di dalam `Update()` / loop inferensi kamera. Gunakan buffer pra-alokasi (`StringBuilder`/`char[]` + `SetCharArray`), pool objek (pool 20 kabel Bezier, pool 60 paket sinyal, pool 6 voice SFX + 1 hum, pool 12 spark), struct value-type, `MaterialPropertyBlock`, dan cache komponen.
- **UI Diet (HUD Minimalis v7.5):** Pertahankan 1 Bilah Atas ramping (`72 px`, ditambah tombol `?` khusus Mode Udara) + Laci Menu Kompak pojok kanan atas (`CompactHudMenuController.cs`), *Live Boolean Expression Chip* ramping kiri-atas + modal `Rapor Penguasaan` (`PedagogyMasteryHud.cs`), Kartu Panduan Gestur 1 Halaman + Pil Latihan Cepat 4 Langkah non-intrusif (`AirWeavingGestureGuide.cs`), **0 tombol gameplay** di Mode Udara (*100% Zero-Touch*), 4 kapsul ramping semi-transparan di Mode Kartu, dan `Txt_Feedback` dalam mode *Toast-Only*. Jangan menambah panel UI 2D permanen; utamakan umpan balik 3D spasial. (Jika suatu saat membuat *In-Game Debug Console*, wajib tersembunyi secara default dan hanya muncul lewat gestur tersembunyi mis. ketuk 3 jari.)

### Peta Komponen & Skrip v7.5 (Fase 1–4 + Polish + Sprint 1–3)
- **`Assets/Scripts/Core/`** *(Tracking-Agnostic)*
  - `CircuitManager.cs` — Evaluasi rangkaian real-time, auto-snap Mode Kartu v5, & koneksi manual/fan-out Mode Udara v7.
  - `GateNode.cs` — Mesin 3D v5.0, port fisik (penghapusan cincin port hantu saat Mode Udara), VFX kabel Mode Kartu, & dukungan *external spatial pose* Mode Udara.
  - `GateMechanismVisual.cs` *(Fase 1)* — Visual mekanisme terbuka (*Open Cutaway*) untuk 5 gerbang (`AND`, `OR`, `NOT`, `XOR`, `NAND`) + *Power Core* internal pada `NOT`/`NAND` (zero-GC, 3 shared material + `MaterialPropertyBlock`).
  - `OutputTruthPodVisual.cs` *(Fase 1 + Polish)* — Pod Tabel Kebenaran `2x2` berlabel biner `00`, `01`, `10`, `11`; miring `-48°` di Mode Kartu (v5) dan otomatis sejajar menghadap kamera (*camera-facing*) di Mode Udara (v7).
- **`Assets/Scripts/Levels/`**
  - `LevelAndUIManager.cs` — 7 level kurikulum + Sandbox (21 bintang), pemindai tabel kebenaran, *scaffolding* 3 tingkat, audio PCM, rapor CP, & *Dual-Path Auto-Win*.
  - `CompactHudMenuController.cs` *(Fase 2 + v7.5 Sprint 2–3)* — Bilah Atas `72 px` (+ tombol `?` khusus Mode Udara), Laci Menu Kompak (`Mode: Kartu (v5)` ↔ `Mode: Udara (v7)`, `Pilih Level / Sandbox`, `Reset Level`, `Rapor Penguasaan`, `Rapor CP`, `Petunjuk (0/3)`, `Audio`, & `Panduan Gestur` khusus Mode Udara), kontrol bawah adaptif per mode, & `Txt_Feedback` *Toast-Only* berkontras tinggi.
  - `PedagogyMasteryHud.cs` *(v7.5 Sprint 3)* — *Live Boolean Expression Chip* Zero-GC (`StringBuilder` + `char[]` + `SetCharArray`, bekerja di Mode Kartu & Mode Udara, deteksi cabang `?` & anti-siklus) serta Modal `Rapor Penguasaan` di laci `≡` (total bintang `X/21`, `Y/7` level, predikat, counter *Self-Debugging* & pemisahan Mode Kartu vs Mode Udara via `GW_v75_*`, baca progres `GW_v3_*`).
- **`Assets/Scripts/AirWeaving/`** *(Terisolasi Khusus Mode Udara v7.5)*
  - `AirWeavingController.cs` — Orkestrator Camera Handover, proyeksi tangan 1:1 adaptif (*One-Euro-style*), levitasi sinusoidal + *velocity tilt* `±12°`, 4 siku sudut `AssemblyZone` berbayangan, peredaman skeleton & label 3D kontras tinggi, sakelar Input A/B sentuh telunjuk `#8` (`75 ms` dwell + *Exit-to-Rearm*), & sinkronisasi evaluasi rangkaian instan anti-*false-win*.
  - `AirWeavingHandGestureDetector.cs` *(Fase 3–4 + Polish)* — Detektor dual-kanal skala-invarian: `Fist` (objek), `Pinch` `#4+#8` (kabel + *pre-pinch guard*), & `SummonHandPose` (`1=AND`, `2=OR`, `3=XOR`, `4+jempol lipat=NAND`, `Jempol saja=NOT`, `Open-5`).
  - `AirWeavingHologramVisual.cs` *(Fase 3 + Polish + v7.5 Sprint 1)* — Visual hologram 3D melayang (tanpa cincin lantai), aura hover/grab/buang merah (anti-clipping), efek *ripple* sentuhan Input A/B, & *reticle* otomatis tersembunyi saat tangan keluar kamera.
  - `AirWeavingCableRouter.cs` *(Fase 3 + Polish + v7.5 Sprint 1)* — Manajemen kabel manual *Pinch* port-ke-port (`Port_Out` fan-out, `Port_In` cabut/ganti, lepas di ruang kosong untuk putus), renderer seragam 100% kabel Mode Udara lewat pool 20 Bezier zero-GC orb-ke-orb, & *Pulsing Signal Packets* Zero-GC (pool 60 paket adaptif `1–3/kabel`, 1 shared material + `MaterialPropertyBlock`).
  - `AirWeavingCircleSummoner.cs` *(Fase 4)* — State machine 2-tahap *Circle-Charge → Open-5 Bloom* (deteksi sapuan sudut kumulatif, batal instan via `Fist`, anti-duplikat non-destruktif).
  - `AirWeavingSummonVisual.cs` *(Fase 4)* — Indikator pose live di ujung jari, cincin pengisian 0°–360°, pratinjau hologram `[ BUKA 5 JARI ]`, *Notice + Pulse* amber `[ <GATE> SUDAH DI PAPAN ]`, & ledakan *Bloom*.
  - `AirWeavingOutputTrigger.cs` *(Fase 2–3 + Polish + v7.5 Sprint 1 & 3)* — Pemandu debugging mandiri live pada Output (`SAMBUNG KE OUTPUT`, `KURANGI GERBANG`, `CEK SEL MERAH`, `UJI SAKELAR (X/4)`, `LULUS 4/4` → *Auto-Win*; cincin Output anti-clipping; sinkronisasi status kebenaran live anti-*false-win*; tahan kepal setelah lulus untuk *Zero-Touch Next Level*).
  - `AirWeavingSpatialSfx.cs` *(v7.5 Sprint 1)* — Synthesizer audio spasial PCM prosedural 100% interaktif (24 cue *one-shot* lewat pool 6 voice + 2 *looping hum* seret kabel/gerbang dengan watchdog `LateUpdate` anti-nyangkut + *soft limiter* `tanh` + cooldown independen per kategori, Zero-GC).
  - `AirWeavingGestureGuide.cs` *(v7.5 Sprint 2)* — Kartu Panduan Gestur 1 Halaman (auto-show 1x via `GW_AirGuideSeen_v75`, tombol `?` di Bilah Atas & opsi `Panduan Gestur` di laci `≡`) serta Pil Latihan Cepat 4 Langkah interaktif *real-time* Zero-GC.
  - `UnistrokeGestureRecognizer.cs` — Modul `$1 Unistroke` legacy v6 (disimpan utuh, nonaktif secara default di v7.5).

## 3. Pelajaran Teknis Unity 6 & MediaPipe (Wajib Dipatuhi)
- **Gradle 9 / AGP 9 & `libc++_shared.so`:** Jangan membuat `mainTemplate.gradle` dengan properti usang `android.ndkDirectory`. Penyalinan `libc++_shared.so` (arm64-v8a) sudah ditangani otomatis oleh `Assets/Editor/AndroidLibcppCopier.cs` (`IPostGenerateGradleAndroidProject`).
- **Keamanan Lifecycle & Shutdown:** MediaPipe wajib berjalan di `Delegate.CPU`. Seluruh resource native (`HandLandmarker`, `Protobuf.ResetLogHandler()`, `WebCamTexture`, `NativeArray<byte>`, dan material dinamis) wajib dibersihkan sinkron di `OnDisable()` / `OnDestroy()` / `OnApplicationQuit()` agar tidak memicu *crash* `SIGABRT` di Windows Editor.
- **Camera Handover (Camera2-Safe):** Saat masuk Mode Udara, jeda `DefaultObserverEventHandler` pada 8 `Card_*`, nonaktifkan `VuforiaBehaviour`, beri jeda real-time sebelum membuka `WebCamTexture`, dan pulihkan seluruh `Renderer` anak saat aktif/parkir. Saat kembali ke Mode Kartu, lepas kamera & pulihkan Vuforia 100%.
- **Pengaman Batas 100 MB GitHub & Kebersihan Repo:** File `Packages/com.github.homuler.mediapipe-*.tgz` (~277 MB) serta folder recovery otomatis Unity (`Assets/_Recovery/`, `Assets/_Recovery.meta`) **WAJIB** tetap berada di `.gitignore`. Jangan pernah melakukan `git add -f` pada file tarball `.tgz`, folder recovery, maupun file uji sementara (`*.py`, proyek test sementara) ke dalam repo.
- **Warning Unity 6 yang Aman Diabaikan (Jangan "Diperbaiki"):**
  1. Shader warning `Vuforia/URP/DepthContourMask` (normal karena proyek memakai Built-in RP).
  2. `Internal build system error. Read the full binlog without getting a BuildFinishedMessage` yang muncul 1 detik *setelah* `Build completed with a result of 'Succeeded'` (race condition `bee_backend.exe` Unity 6, APK tetap utuh).
  3. Warning `Missing Signature` untuk paket `.tgz` lokal MediaPipe.

## 4. Alur Kerja, Verifikasi, & Disiplin Git
1. **Planning Mode First:** Untuk perubahan yang menyentuh > 2 skrip atau menambah fitur baru, lakukan audit *read-only* dan presentasikan rencana arsitektur singkat terlebih dahulu sebelum mengedit kode.
2. **Verifikasi Wajib Roslyn C#:** Setelah setiap perubahan C#, jalankan kompilasi verifikasi Roslyn (`/warnaserror+` + Unity Roslyn Analyzers) untuk 3 target (`UNITY_EDITOR`, `UNITY_ANDROID`, dan `Assets/Editor/AndroidLibcppCopier.cs`) hingga **`0 error, 0 warning`**. Hapus skrip verifikasi sementara bila dibuat di luar `%TEMP%`.
3. **Aturan Git:**
   - Kerjakan fitur baru di branch terpisah (`feat/<nama-fitur>`), jangan langsung di `main`.
   - **Dilarang keras** menjalankan `git commit`, `git merge`, `git tag`, atau `git push` secara otomatis tanpa perintah eksplisit dari pengguna.
   - Gunakan *progressive commits* yang bersih dan terkelompok secara logis saat pengguna meminta rilis.
