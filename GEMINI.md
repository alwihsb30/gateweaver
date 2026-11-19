# GEMINI.md — Aturan Arsitektur & Alur Kerja GateWeaver AR

Dokumen ini adalah panduan wajib bagi AI Agent (Antigravity) dalam mengembangkan **GateWeaver AR** (rilis stabil saat ini: `v6.0`, branch `main`). Baca seluruh aturan ini sebelum merencanakan atau mengubah kode apa pun.

> Untuk detail desain gameplay, parameter mekanik, peta tata letak HUD, kurikulum level, dan backlog fitur, baca `docs/GDD.md`.

## 1. Identitas & Tech Stack Terkunci
- **Tujuan Produk:** Game edukasi AR gerbang logika & aljabar Boolean untuk **Kurikulum Merdeka Fase E (Informatika SMA)** dengan 7 level kurikulum (hingga NOR & Hukum De Morgan) + mode Sandbox.
- **Engine & Render Pipeline:** Unity 6 (`6000.6.9f1`), **Built-in Render Pipeline** (BUKAN URP/HDRP).
- **Dual-Mode AR:**
  - `Mode: Kartu (v5)` — **Vuforia Engine 11.4.4** (8 kartu marker fisik).
  - `Mode: Udara (v6)` — **MediaPipeUnityPlugin v0.16.3** (`HandLandmarker`, `Delegate.CPU`, model `Assets/StreamingAssets/hand_landmarker.task`) + `WebCamTexture` (kamera depan cermin di Android).
- **Target Build Android:** Minimum API Level 29, Target API Automatic, IL2CPP, ARM64 only, **OpenGLES3 only**, Landscape Left, New Input System (`activeInputHandler: 1`).
- **LARANGAN KERAS STACK:** Jangan pernah menginstal **AR Foundation**, **XR Plug-in Management**, **XR Simulation**, atau **URP**. Paket tersebut bentrok dengan Vuforia 11.4.4 & MediaPipe.

## 2. Batas Arsitektur & Prinsip Kode
- **Code-First (Lindungi Scene):** Dilarang mengedit YAML `Assets/Scenes/SampleScene.unity` atau `.prefab` secara manual kecuali diminta eksplisit. Gunakan *runtime auto-bootstrap* atau skrip C# di `Assets/Editor/`.
- **Tracking-Agnostic Core:** `Assets/Scripts/Core/CircuitManager.cs` dan `GateNode.cs` harus tetap bebas dari `using Vuforia;` dan bebas dependensi MediaPipe. Pertahankan `CircuitManager.cs` seminimal mungkin diubah (usahakan `0` diff untuk fitur input/UI baru).
- **Isolasi Modul:** Seluruh logika hand-tracking, One-Euro Filter, Spatial Workbench, dan `$1 Unistroke` Air-Draw tetap terisolasi di `Assets/Scripts/AirWeaving/`. Gunakan pola *decoupled* berbasis C# `event`/`Action` untuk fitur baru (jangan me-*refactor* kode stabil v6.0 ke `ScriptableObject Events` tanpa alasan kuat).
- **Zero-GC di Runtime Loop:** Dilarang melakukan alokasi memori (`new`, LINQ, `GetComponent`, string concat berat) di dalam `Update()` / loop inferensi kamera. Gunakan buffer pra-alokasi, struct value-type, dan cache komponen.
- **UI Diet:** Jangan menambah panel UI 2D baru yang memenuhi layar. Utamakan umpan balik 3D spasial dan manfaatkan kapsul `Txt_Feedback` yang sudah ada. (Jika suatu saat membuat *In-Game Debug Console*, wajib tersembunyi secara default dan hanya muncul lewat gestur tersembunyi mis. ketuk 3 jari.)

## 3. Pelajaran Teknis Unity 6 & MediaPipe (Wajib Dipatuhi)
- **Gradle 9 / AGP 9 & `libc++_shared.so`:** Jangan membuat `mainTemplate.gradle` dengan properti usang `android.ndkDirectory`. Penyalinan `libc++_shared.so` (arm64-v8a) sudah ditangani otomatis oleh `Assets/Editor/AndroidLibcppCopier.cs` (`IPostGenerateGradleAndroidProject`).
- **Keamanan Lifecycle & Shutdown:** MediaPipe wajib berjalan di `Delegate.CPU`. Seluruh resource native (`HandLandmarker`, `Protobuf.ResetLogHandler()`, `WebCamTexture`, `NativeArray<byte>`, dan material dinamis) wajib dibersihkan sinkron di `OnDisable()` / `OnDestroy()` / `OnApplicationQuit()` agar tidak memicu *crash* `SIGABRT` di Windows Editor.
- **Camera Handover (Camera2-Safe):** Saat masuk Mode Udara, jeda `DefaultObserverEventHandler` pada 8 `Card_*`, nonaktifkan `VuforiaBehaviour`, beri jeda real-time sebelum membuka `WebCamTexture`, dan pulihkan seluruh `Renderer` anak saat aktif/parkir. Saat kembali ke Mode Kartu, lepas kamera & pulihkan Vuforia 100%.
- **Pengaman Batas 100 MB GitHub:** File `Packages/com.github.homuler.mediapipe-*.tgz` (~277 MB) **WAJIB** tetap berada di `.gitignore`. Jangan pernah melakukan `git add -f` pada file tarball `.tgz` maupun file uji sementara (`*.py`, proyek test sementara) ke dalam repo.
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
