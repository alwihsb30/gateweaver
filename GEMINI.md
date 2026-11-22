# GEMINI.md — Aturan Arsitektur & Alur Kerja GateWeaver AR

Dokumen ini adalah panduan wajib bagi AI Agent (Antigravity) dalam mengembangkan **GateWeaver AR** (rilis stabil terakhir: **`v7.5`**, saat ini dalam proses upgrade bertahap menuju **`v8.0`** pada branch `main`). Baca seluruh aturan ini sebelum merencanakan atau mengubah kode apa pun.

> Untuk detail desain gameplay, parameter mekanik, kurikulum level, dan backlog fitur, baca `docs/GDD.md`.

## 1. Identitas & Tech Stack Terkunci
- **Tujuan Produk:** Game edukasi AR gerbang logika & aljabar Boolean untuk **Kurikulum Merdeka Fase E (Informatika SMA)** dengan 7 level kurikulum (hingga NOR & Hukum De Morgan) + mode Sandbox.
- **Engine & Render Pipeline:** Unity 6 (`6000.6.9f1`), **Built-in Render Pipeline (Forward)** dengan **Linear Color Space** (BUKAN URP/HDRP, tanpa Post-Processing Stack/Volume).
- **Dual-Mode AR:**
  - `Mode: Kartu (v5)` — **Vuforia Engine 11.4.4** (8 kartu marker fisik, wajib tetap utuh **0% regresi** sebagai fondasi rencana adaptasi 6DoF di rilis selanjutnya).
  - `Mode: Udara (v7 → v8.0)` — **MediaPipeUnityPlugin v0.16.3** (`HandLandmarker`, `Delegate.CPU`, model `Assets/StreamingAssets/hand_landmarker.task`) + `WebCamTexture` (kamera depan cermin di Android), fokus penuh pada interaksi spasial 3D menyambung logika input-output, kontrol dual-kanal (`Circle Summon + Open-5`, `Pinch` kabel, `Fist` objek, Sentuh Telunjuk `#8` sakelar), audio spasial PCM prosedural, & Truth Pod `00–11` debugging mandiri.
- **Target Build Android:** Minimum API Level 29, Target API Automatic, IL2CPP, ARM64 only, **OpenGLES3 only**, Landscape Left, New Input System (`activeInputHandler: 1`).
- **Format Aset 3D Native:** Proyek tidak memasang importer glTF/GLB; seluruh prop 3D eksternal wajib berformat **`.fbx` atau `.obj`**.
- **LARANGAN KERAS STACK:** Jangan pernah menginstal **AR Foundation**, **XR Plug-in Management**, **XR Simulation**, atau **URP**. Paket tersebut bentrok dengan Vuforia 11.4.4 & MediaPipe.

## 2. Arah & Status Upgrade v8.0 (Tahapan Mikro)
Upgrade v8.0 berfokus penuh pada **UX spasial 3D modern, interaksi tangan responsif, dan pemangkasan fitur non-esensial**:
- **Fitur yang Sudah Dipangkas (Tahap 1 — Selesai):** `PedagogyMasteryHud.cs` (`Live Boolean Chip` & `Rapor Penguasaan`) dihapus permanen; `Rapor CP` dan `Petunjuk (Hint)` dihapus dari laci `≡`; auto-popup `Rapor CP` pasca Level 7 Mode Kartu dinonaktifkan.
- **Standar UI Diet v8.0:** Fokus murni pada misi 1 baris, kuota gerbang, Truth Pod `00–11`, Panduan Gestur (`?`) + Latihan 4 Langkah, serta laci `≡` minimalis (**maks 5 item** di Mode Udara: `Mode`, `Pilih Level / Sandbox`, `Reset Level`, `Audio`, `Panduan Gestur`; **4 item** di Mode Kartu tanpa `Panduan Gestur`). Jangan menambahkan kembali rapor, chip rumus, atau menu sekunder.
- **Status Visual & 3D v8.0 (Tahap 1–7 Selesai):**
  - **Tahap 1–2:** UI Diet & HUD *Floating Glass* bertipografi besar + kartu 9-slice.
  - **Tahap 3:** Single One-Euro filter, *soft aim-assist*, *sticky snap*, & fisika *spring-damper*.
  - **Tahap 4:** Panggung meja hologram 3D (`HologramStageRoot`) + Orbit 360°/Zoom/Pan + *Parallax* & *Inertial Coasting*.
  - **Tahap 5 & 5.5:** Kabel Tabung 3D PBR (`MeshRenderer`), 3D Port Sockets, *tangent-aligned signal capsules*, *Symmetric Log Z Carry-Pan*, dek 2x (`0.58x`, grid `17x13`), & *Auto Edge-Pan*.
  - **Tahap 6:** Custom Built-in `.shader` OpenGLES3 (`ARBackdropDimming` + 1-pass `HologramFresnelHardware` + 3-Point & Hemisphere Lighting Rig).
  - **Tahap 7:** 7 Prop sci-fi melayang 360° native (`.obj` + `.mtl`, *drop-in replaceable* `.fbx`) di `Assets/Resources/Models/V8_AirProps/` menggantikan diorama darat di Mode Udara (Input A/B Beacon Sian/Emas, 5 Varian Output kinetik, & Gate Underside Hover-Cradle) dengan **0% regresi Mode Kartu v5**.

## 3. Batas Arsitektur, Peta Komponen, & Prinsip Kode
- **Code-First (Lindungi Scene):** Dilarang mengedit YAML `Assets/Scenes/SampleScene.unity` atau `.prefab` secara manual kecuali diminta eksplisit. Gunakan *runtime auto-bootstrap* atau skrip C# di `Assets/Editor/`.
- **Tracking-Agnostic Core:** `Assets/Scripts/Core/CircuitManager.cs`, `GateNode.cs`, `GateMechanismVisual.cs`, dan `OutputTruthPodVisual.cs` harus tetap bebas dari `using Vuforia;` dan bebas dependensi MediaPipe. Pertahankan `CircuitManager.cs` seminimal mungkin diubah.
- **Isolasi Modul Air-Weaving:** Seluruh logika hand-tracking, proyeksi ruang, deteksi gestur dual-kanal, visual 3D Mode Udara, kabel manual, *Circle Summon*, pemandu Output mandiri, audio spasial PCM, serta onboarding panduan gestur terisolasi di `Assets/Scripts/AirWeaving/`. Gunakan pola *decoupled* berbasis C# `event`/`Action`.
- **Zero-GC di Runtime Loop:** Dilarang melakukan alokasi memori (`new`, LINQ, `GetComponent`, string concat berat) di dalam `Update()` / loop inferensi kamera. Gunakan buffer pra-alokasi, pool objek, struct value-type, `MaterialPropertyBlock`, dan cache komponen.
- **Shader Android OpenGLES3:** Setiap *custom* Built-in `.shader` baru yang dimuat runtime wajib direferensikan lewat `Material` di `Assets/Resources/` atau didaftarkan di `AlwaysIncludedShaders` agar tidak ter-strip saat build IL2CPP.

### Peta Komponen & Skrip Aktif
- **`Assets/Scripts/Core/`** *(Tracking-Agnostic)*
  - `CircuitManager.cs` — Evaluasi rangkaian real-time, auto-snap Mode Kartu v5, & koneksi manual/fan-out Mode Udara.
  - `GateNode.cs` — Mesin 3D v5.0, port fisik, VFX kabel Mode Kartu, & dukungan *external spatial pose* Mode Udara.
  - `GateMechanismVisual.cs` — Visual mekanisme terbuka (*Open Cutaway*) untuk 5 gerbang (`AND`, `OR`, `NOT`, `XOR`, `NAND`).
  - `OutputTruthPodVisual.cs` — Pod Tabel Kebenaran `2x2` berlabel biner `00`, `01`, `10`, `11`.
- **`Assets/Scripts/Levels/`**
  - `LevelAndUIManager.cs` — 7 level kurikulum + Sandbox (21 bintang), pemindai tabel kebenaran, audio PCM, & *Dual-Path Auto-Win* (auto-popup Rapor CP dinonaktifkan).
  - `CompactHudMenuController.cs` — Bilah Atas (+ tombol `?` khusus Mode Udara), Laci Menu Kompak (5 item Mode Udara / 4 item Mode Kartu), kontrol bawah adaptif per mode, & `Txt_Feedback` *Toast-Only*.
- **`Assets/Scripts/AirWeaving/`** *(Terisolasi Khusus Mode Udara)*
  - `AirWeavingController.cs` — Orkestrator Camera Handover, proyeksi tangan MediaPipe, tata letak node Mode Udara, sakelar Input A/B sentuh telunjuk `#8`, & sinkronisasi evaluasi rangkaian instan anti-*false-win*.
  - `AirWeavingStageController.cs` — Pengendali meja hologram 3D `HologramStageRoot` (dek 2x, grid `17x13`, *Grounded Contact Rings*, Focus-Point Orbit 360°, Ultra-Wide Zoom `0.18x–15x`, *Carry-Pan* Log-Z, & *Inertial Coasting*).
  - `AirWeavingLightingAndShaderRig.cs` — Pengendali shader OpenGLES3 (`ARBackdropDimming`, 1-pass `HologramFresnelHardware`), 3-Point + Hemisphere Lighting Rig analitik, & penerus Fresnel Rim Glow Zero-GC.
  - `AirWeavingFloatingPropRig.cs` — Pengendali 7 prop sci-fi melayang 360° Mode Udara (`Assets/Resources/Models/V8_AirProps/`), animasi kinetik `0/1`, *underside repulsor glow* (`Sprites/Default`), & supresi/restorasi `SetActive` dua arah.
  - `AirWeavingHandGestureDetector.cs` — Detektor dual-kanal skala-invarian (`Fist`, `Pinch`, & `SummonHandPose`).
  - `AirWeavingHologramVisual.cs` — Visual hologram Mode Udara, aura hover/grab/buang, efek *ripple* Input A/B, & *reticle*.
  - `AirWeavingCableRouter.cs` — Manajemen kabel manual *Pinch* port-ke-port (`Port_Out` fan-out, `Port_In` cabut/ganti), renderer kabel Mode Udara, & *Pulsing Signal Packets* Zero-GC.
  - `AirWeavingCircleSummoner.cs` — State machine 2-tahap *Circle-Charge → Open-5 Bloom*.
  - `AirWeavingSummonVisual.cs` — Indikator pose live, cincin pengisian 0°–360°, pratinjau hologram, & efek *Bloom*.
  - `AirWeavingOutputTrigger.cs` — Pemandu debugging mandiri live pada Output (`SAMBUNG KE OUTPUT`, `KURANGI GERBANG`, `CEK SEL MERAH`, `UJI SAKELAR (X/4)`, `LULUS 4/4` → *Auto-Win*).
  - `AirWeavingSpatialSfx.cs` — Synthesizer audio spasial PCM prosedural 100% interaktif (24 cue *one-shot* + 2 *looping hum* + *soft limiter* `tanh`, Zero-GC).
  - `AirWeavingGestureGuide.cs` — Kartu Panduan Gestur 1 Halaman (`?` & laci `≡`) serta Pil Latihan Cepat 4 Langkah interaktif *real-time*.
  - `UnistrokeGestureRecognizer.cs` — Modul `$1 Unistroke` legacy v6 (nonaktif secara default).

## 4. Pelajaran Teknis Unity 6 & MediaPipe (Wajib Dipatuhi)
- **Gradle 9 / AGP 9 & `libc++_shared.so`:** Jangan membuat `mainTemplate.gradle` dengan properti usang `android.ndkDirectory`. Penyalinan `libc++_shared.so` (arm64-v8a) ditangani otomatis oleh `Assets/Editor/AndroidLibcppCopier.cs` (`IPostGenerateGradleAndroidProject`).
- **Keamanan Lifecycle & Shutdown:** MediaPipe wajib berjalan di `Delegate.CPU`. Seluruh resource native (`HandLandmarker`, `Protobuf.ResetLogHandler()`, `WebCamTexture`, `NativeArray<byte>`, dan material dinamis) wajib dibersihkan sinkron di `OnDisable()` / `OnDestroy()` / `OnApplicationQuit()` agar tidak memicu *crash* `SIGABRT` di Windows Editor.
- **Camera Handover (Camera2-Safe):** Saat masuk Mode Udara, jeda `DefaultObserverEventHandler` pada 8 `Card_*`, nonaktifkan `VuforiaBehaviour`, beri jeda real-time sebelum membuka `WebCamTexture`, dan pulihkan seluruh `Renderer` anak saat aktif/parkir. Saat kembali ke Mode Kartu, lepas kamera & pulihkan Vuforia 100%.
- **Pengaman Batas 100 MB GitHub & Kebersihan Repo:** File `Packages/com.github.homuler.mediapipe-*.tgz` (~277 MB) serta folder recovery otomatis Unity (`Assets/_Recovery/`, `Assets/_Recovery.meta`) **WAJIB** tetap berada di `.gitignore`. Jangan pernah melakukan `git add -f` pada file tarball `.tgz`, folder recovery, maupun file uji sementara (`*.py`, proyek test sementara).
- **Warning Unity 6 yang Aman Diabaikan (Jangan "Diperbaiki"):**
  1. Shader warning `Vuforia/URP/DepthContourMask` (normal karena proyek memakai Built-in RP).
  2. `Internal build system error. Read the full binlog without getting a BuildFinishedMessage` yang muncul 1 detik *setelah* `Build completed with a result of 'Succeeded'` (race condition `bee_backend.exe` Unity 6, APK tetap utuh).
  3. Warning `Missing Signature` untuk paket `.tgz` lokal MediaPipe.

## 5. Alur Kerja, Verifikasi, & Disiplin Git
1. **Planning Mode First:** Untuk perubahan yang menyentuh > 2 skrip atau mengubah arsitektur 3D/shader, lakukan audit *read-only* dan presentasikan rencana singkat sebelum mengedit kode.
2. **Verifikasi Wajib Roslyn C#:** Setelah setiap perubahan C#, jalankan kompilasi verifikasi Roslyn (`/warnaserror+` + Unity Roslyn Analyzers) untuk 3 target (`UNITY_EDITOR`, `UNITY_ANDROID`, dan seluruh skrip Editor: `Assets/Editor/AndroidLibcppCopier.cs`, `Assets/Editor/GateWeaverShaderManifestEnsurer.cs`, `Assets/Editor/GateWeaverAirPropValidator.cs`) hingga **`0 error, 0 warning`**. Hapus skrip verifikasi sementara bila dibuat di luar `%TEMP%`.
3. **Aturan Git:**
   - Kerjakan setiap tahap di branch terpisah (`feat/<nama-fitur>`), jangan langsung di `main`.
   - **Dilarang keras** menjalankan `git commit`, `git merge`, `git tag`, atau `git push` secara otomatis tanpa perintah eksplisit dari pengguna.
