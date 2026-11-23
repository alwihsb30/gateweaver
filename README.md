# GateWeaver AR (v8.5.0)

**Aplikasi Pembelajaran Interaktif *Dual-Mode Spatial Augmented Reality* untuk Rangkaian Gerbang Logika Kombinasional — Kurikulum Merdeka Fase E (Informatika SMA/SMK)**

GateWeaver AR menyatukan dua pengalaman praktikum gerbang logika dalam satu mesin simulasi rangkaian terpadu (`CircuitManager` & `GateNode`): **Mode Udara (*Air-Weaving Hand Tracking*)** tanpa kartu cetak dan **Mode Kartu (*Tangible Vuforia AR*)** dengan 8 kartu marker fisik.

---

## Pratinjau Visual (`v8.5.0`)

> *Letakkan tangkapan layar aplikasi di folder `docs/screenshots/` agar tabel pratinjau berikut tampil di halaman GitHub.*

| Layar Judul & Pemilih Mode | Meja Kerja 3D Mode Udara | Panduan Gestur & Pose Summon |
| :---: | :---: | :---: |
| ![Title Overlay](docs/screenshots/01_title.png) | ![AirWeaving Stage](docs/screenshots/02_airweaving_stage.png) | ![Gesture Guide](docs/screenshots/03_gesture_guide.png) |

| Verifikasi Lulus `4/4` | Mode Kartu Fisik (Vuforia AR) |
| :---: | :---: |
| ![Level Complete](docs/screenshots/04_level_complete.png) | ![Vuforia Card Mode](docs/screenshots/05_vuforia_card_mode.png) |

---

## 1. Deskripsi Proyek & Pemetaan Kurikulum Merdeka

- **Mata Pelajaran:** Informatika — Kurikulum Merdeka Fase E (SMA/SMK Kelas X)
- **Elemen Capaian Pembelajaran:** Sistem Komputer (**SK**) dan Berpikir Komputasional (**BK**)
- **Tujuan Pedagogis:**
  - Memahami simbol ANSI/IEEE, fungsi, mekanisme aliran sinyal internal (*Open Cutaway*), dan tabel kebenaran gerbang `AND`, `OR`, `NOT`, `XOR`, `NAND`, `NOR`, `XNOR`, serta ekuivalensi **Hukum De Morgan**.
  - Menerjemahkan permasalahan logika kontekstual menjadi rangkaian kombinasional sesuai batas kuota gerbang.
  - Melakukan *debugging* mandiri secara sistematis pada seluruh kombinasi input biner (`00`, `01`, `10`, `11`) melalui umpan balik visual 3D dan tabel kebenaran real-time.

---

## 2. Arsitektur Dual-Mode AR (`v8.5.0`)

Aplikasi dibuka melalui **Layar Judul Pemilih Mode** (`AirWeavingTitleOverlay`) yang memungkinkan pengguna langsung masuk ke **Mode Udara**, **Mode Kartu**, atau **Sandbox (Laboratorium Bebas)**, serta berpindah mode kapan saja melalui Laci Menu Kompak (`≡`) dengan *Camera Handover* bersih (*Android Camera2-safe*).

| Aspek | Mode Udara (`v8.5` — Air-Weaving) | Mode Kartu (`v5.0` — Tangible AR, 0% Regresi) |
| :--- | :--- | :--- |
| **Pelacakan** | MediaPipe `HandLandmarker` (`0.16.3`, CPU) + `WebCamTexture` | PTC Vuforia Engine `11.4.4` (8 Image Targets) |
| **Kamera** | Kamera depan (cermin) / Webcam laptop | Kamera belakang |
| **Objek 3D** | Meja kerja hologram 3D + prop industrial *Brushed Titanium* (`GwIndustrial3DKit` & `V8_AirProps`) | 8 kartu fisik + 12 model sci-fi modular Quaternius (`V5_SciFi`) + *Open Cutaway* |
| **Interaksi Utama** | Gestur tangan: **Kepal (`Fist`)**, **Cubit (`Pinch`)**, **Dua Tangan** (Orbit/Zoom/Pan), & **Circle-Charge -> Open-5** | Geser kartu fisik di meja (auto-snap kiri-ke-kanan) + kapsul kontrol bawah |
| **Sistem UI** | *Smoked Optical Glass + Brushed Titanium* (`1920×1080`, ikon SDF prosedural Zero-GC) | HUD klasik Mode Kartu v5 + Laci Menu terpadu |
| **Mesin Logika** | `CircuitManager.cs` + `GateNode.cs` (*tracking-agnostic*) | `CircuitManager.cs` + `GateNode.cs` (*tracking-agnostic*) |

---

## 3. Sistem Interaksi, UI v8.5, & Pipeline *Hand Tracking*

### A. 3 Gestur Utama Mode Udara
1. **Kepal (`Fist`) — Toggle Input A/B & Pindah/Buang Gerbang:**
   - Kepalkan tangan di dekat sakelar `Input A` / `Input B` untuk membalik nilai biner `0 <-> 1` secara instan (*rising-edge*).
   - Kepalkan tangan di atas badan gerbang untuk menggenggam dan memindahkannya dengan proyeksi *ray-plane* stabil; tarik keluar zona rakit lalu lepas untuk mengembalikannya ke Rak Parkir Atas dan memutus kabelnya.
2. **Cubit (`Pinch`: Jempol `#4` + Telunjuk `#8`) — Manajemen Kabel Manual:**
   - Cubit `Port_Out` lalu tarik ke `Port_In` tujuan untuk menyambungkan kabel tabung 3D PBR lengkap dengan *Pulsing Signal Packets* Zero-GC (mendukung *fan-out* multi-cabang).
   - Cubit `Port_In` terisi untuk mencabut/memindahkan kabel, atau lepas di ruang kosong untuk menghapus sambungan.
3. **Dua Tangan — Orbit, Zoom, & Pan Panggung 3D:**
   - Gunakan dua tangan untuk memutar meja kerja 3D (dengan batas aman Pitch `[-28°, +42°]` & Roll `[-25°, +25°]`), mengatur jarak pandang (`2.10m – 5.40m`), dan menggeser panggung dalam batas frustum terkunci.

### B. 5 Pose Summon Gerbang (`Circle-Charge -> Open-5 Bloom`)
Bentuk pose jari di area rakit, gambar lingkaran `360°` hingga cincin pengisian penuh, lalu buka **5 jari (*Open-5*)** untuk memunculkan gerbang:

| Pose Jari | Notasi | Gerbang Dipanggil |
| :---: | :---: | :---: |
| **1 Telunjuk** | `A . B` | **`AND`** |
| **2 Jari V** | `A + B` | **`OR`** |
| **Jempol Atas** | `!A` | **`NOT`** |
| **3 Jari** | `A (+) B` | **`XOR`** |
| **4 Jari** (jempol lipat) | `!(A . B)` | **`NAND`** |

### C. Pembaruan Utama `v8.5.0`
- **Sistem UI *Smoked Optical Glass + Brushed Titanium* (`GwThemeTokens.cs`):** Seluruh overlay (`Title`, `Menu Drawer`, `Level Selector`, `Gesture Guide`, `Level Complete`, dan `Pedagogical HUD`) dibangun dengan prinsip *quiet minimalism* menggunakan ikon vektor SDF prosedural (simbol gerbang IEEE, siluet pose jari, ikon gestur, cincin radio) secara Zero-GC.
- **Peta Sirkuit & Proteksi Pedagogis (`AirWeavingLevelSelectorOverlay.cs`):** Peta 7 node interaktif dengan detail misi, kuota gerbang, dan status progres. Seksi `GERBANG TARGET` (bersimbol IEEE) dan `EKSPRESI BOOLEAN` otomatis terkunci hingga level diselesaikan agar tidak membocorkan jawaban.
- **Filter Anti-Wajah & Stabilitas *Tracking* (`AirWeavingController.cs`, `AirWeavingHandGestureDetector.cs`):**
  - Ambang MediaPipe diperketat (`0.62 / 0.58 / 0.55`, inferensi `256px`), validasi anatomi tangan Zero-GC di worker (rasio & luas segitiga telapak, proporsi tulang proksimal, skala `worldLandmarks`, skor handedness, filter duplikat/skala tangan ke-2), serta *temporal entry confirmation* `3 frame` berturut-turut anti-teleportasi.
  - Deteksi `Fist` *dorsal-safe* (metrik lipatan radial 2D untuk sudut punggung tangan) dengan proteksi anti-konflik terhadap `Pinch` kabel.
  - Batas *pan/depth/orbit* panggung ketat, penonaktifan *Auto Edge-Pan* vertikal ke atas saat `GateGrab`, proyeksi *ray-plane* pada `GateGrab`, dan pembatasan tinggi garis proyeksi kursor (`Holo_CursorDropLine`).
- **Audio Spasial PCM Prosedural (`AirWeavingSpatialSfx.cs`):** Synthesizer 100% prosedural Zero-GC (tanpa file audio eksternal) dengan 24 cue *one-shot*, 2 *looping hum* anti-nyangkut, dan *soft limiter* `tanh`.

---

## 4. Kurikulum 7 Level Progresif & Mode Sandbox (21 Bintang)

| Level | Judul Misi | Target Logika | Solusi Referensi | Kuota | Target (`00, 01, 10, 11`) |
| :---: | :--- | :--- | :--- | :---: | :---: |
| **01** | Alarm Pintu & Jendela | Output `1` jika `A` atau `B` bernilai `1` | `Y = A OR B` | Maks `1` | `0, 1, 1, 1` |
| **02** | Sakelar Tangga Dua Arah | Output `1` jika `A` dan `B` berbeda | `Y = A XOR B` | Maks `1` | `0, 1, 1, 0` |
| **03** | Kunci Keamanan Brankas | Output `1` hanya saat `A = 1` dan `B = 0` | `Y = A AND (NOT B)` | Maks `2` | `0, 0, 1, 0` |
| **04** | Pemutus Darurat Reaktor | Output `0` hanya saat `A = 1` dan `B = 1` | `Y = A NAND B` | Maks `1` | `1, 1, 1, 0` |
| **05** | Sinkronisasi Inti Kembar | Output `1` saat `A` dan `B` bernilai sama (`XNOR`) | `Y = NOT(A XOR B)` | Maks `2` | `1, 0, 0, 1` |
| **06** | Sensor Hening Ruang Server | Output `1` hanya saat `A = 0` dan `B = 0` (`NOR`) | `Y = NOT(A OR B)` | Maks `2` | `1, 0, 0, 0` |
| **07** | Kunci Biometrik De Morgan | Output `0` hanya saat `A = 0` dan `B = 1` | `Y = A OR (NOT B)` atau `Y = (NOT A) NAND B` | Maks `2` | `1, 0, 1, 1` |
| **Sandbox** | Laboratorium Bebas | Eksplorasi bebas tanpa batas kuota | Rangkaian bebas | Bebas (`∞`) | Eksplorasi `00..11` |

---

## 5. Spesifikasi Teknis

| Parameter | Nilai |
| :--- | :--- |
| **Unity Editor** | `6000.6.9f1` (Built-in Render Pipeline, Forward, Linear Color Space) |
| **AR Marker SDK** | PTC Vuforia Engine `11.4.4` (`com.ptc.vuforia.engine`) |
| **Hand Tracking SDK** | MediaPipeUnityPlugin `0.16.3` (`HandLandmarker`, `Delegate.CPU`) |
| **Input System** | Unity New Input System `1.20.1` (`ENABLE_INPUT_SYSTEM`) |
| **Target Android** | Min API `29` (Android 10), `IL2CPP`, `ARM64`, **`OpenGLES3`** |
| **Application ID** | `com.GateWeaverStudio.GateWeaverAR` |
| **Orientasi & Referensi UI** | `Landscape Left` (`1920 × 1080`, target `60 FPS`) |
| **Gradle 9 / NDK Hook** | `Assets/Editor/AndroidLibcppCopier.cs` (salin otomatis `libc++_shared.so`) |

---

## 6. Struktur Repositori

```text
GateWeaverAR/
├── Assets/
│   ├── Editor/
│   │   ├── AndroidLibcppCopier.cs                  # Salin libc++_shared.so otomatis saat build Android (Gradle 9)
│   │   ├── GateWeaverAirPropValidator.cs           # Validator aset prop 3D Mode Udara
│   │   ├── GateWeaverShaderManifestEnsurer.cs      # Pengaman shader OpenGLES3 agar tidak ter-strip IL2CPP
│   │   └── Migration/AddVuforiaEnginePackage.cs
│   ├── Resources/
│   │   ├── Models/V5_SciFi/                        # 12 model FBX sci-fi modular Quaternius (CC0)
│   │   ├── Models/V8_AirProps/                     # 7 prop melayang 3D (.obj/.mtl) Mode Udara
│   │   └── Shaders/                                # Material referensi ARBackdropDimming & HologramFresnelHardware
│   ├── Scenes/SampleScene.unity
│   ├── Scripts/
│   │   ├── AirWeaving/                             # 20 skrip terisolasi Mode Udara & Sistem UI v8.5
│   │   │   ├── AirWeavingCableRouter.cs            # Kabel tabung 3D PBR, Pinch port-ke-port, & Pulsing Signal Packets
│   │   │   ├── AirWeavingCircleSummoner.cs         # State machine 2-tahap Circle-Charge -> Open-5 Bloom
│   │   │   ├── AirWeavingController.cs             # Camera Handover, worker anti-wajah Zero-GC, & orkestrator interaksi
│   │   │   ├── AirWeavingFloatingPropRig.cs        # Prop industrial 3D melayang & animasi kinetik Input/Output/Gerbang
│   │   │   ├── AirWeavingGestureGuide.cs           # Modal Panduan Gestur (grid 3x2 + ikon SDF) & Latihan Cepat 4 Langkah
│   │   │   ├── AirWeavingHandGestureDetector.cs    # Detektor Fist dorsal-safe, Pinch kabel, & 5 Pose Summon
│   │   │   ├── AirWeavingHologramVisual.cs         # Visual hologram meja kerja, aura, & garis proyeksi kursor terbatas
│   │   │   ├── AirWeavingLevelCompleteModal.cs     # Modal Verifikasi Lulus 4/4 (tabel kebenaran grid 1px)
│   │   │   ├── AirWeavingLevelSelectorOverlay.cs   # Peta Sirkuit 7 Node + detail misi & proteksi pedagogis
│   │   │   ├── AirWeavingLightingAndShaderRig.cs   # Rig pencahayaan 3-Point + pengontrol shader OpenGLES3
│   │   │   ├── AirWeavingMenuDrawerOverlay.cs      # Laci Menu Kompak (Mode, Progres Level, Kalibrasi, Reset, Audio)
│   │   │   ├── AirWeavingOutputTrigger.cs          # Pemandu debugging mandiri live Output & Auto-Win 4/4
│   │   │   ├── AirWeavingPedagogicalHud.cs         # HUD Atas, strip waveform Input A/B, & bar tabel kebenaran live
│   │   │   ├── AirWeavingSpatialSfx.cs             # Synthesizer PCM prosedural 24 cue one-shot + 2 looping hum
│   │   │   ├── AirWeavingStageController.cs        # Pengendali meja 3D (Orbit/Zoom/Pan dengan clamp OBB & sudut)
│   │   │   ├── AirWeavingSummonVisual.cs           # Visual cincin charge 360°, indikator pose, & efek Bloom
│   │   │   ├── AirWeavingTitleOverlay.cs           # Layar Judul & Pemilih Mode v8.5
│   │   │   ├── GwIndustrial3DKit.cs                # Geometri prosedural Brushed Titanium & Smoked Tray 3D
│   │   │   ├── GwThemeTokens.cs                    # Token desain v8.5 + generator ikon vektor SDF Zero-GC
│   │   │   └── UnistrokeGestureRecognizer.cs       # Modul unistroke legacy (nonaktif default)
│   │   ├── Core/                                   # 4 skrip inti tracking-agnostic
│   │   │   ├── CircuitManager.cs
│   │   │   ├── GateMechanismVisual.cs
│   │   │   ├── GateNode.cs
│   │   │   └── OutputTruthPodVisual.cs
│   │   └── Levels/                                 # 2 skrip kurikulum & HUD Mode Kartu
│   │       ├── CompactHudMenuController.cs
│   │       └── LevelAndUIManager.cs
│   ├── Shaders/
│   │   ├── ARBackdropDimming.shader
│   │   └── HologramFresnelHardware.shader
│   └── StreamingAssets/
│       ├── hand_landmarker.task                    # Model MediaPipe HandLandmarker (~7,8 MB)
│       └── Vuforia/GateWeaverDB.{dat,xml}          # Database 8 marker Vuforia
├── docs/
│   └── GDD.md                                      # Game Design Document (v8.5.0)
├── Packages/
│   ├── manifest.json                               # Referensi relatif Vuforia 11.4.4 & MediaPipe 0.16.3
│   └── com.ptc.vuforia.engine-11.4.4.tgz           # Dikelola via Git LFS
└── ProjectSettings/
```

---

## 7. Setup Pengembang (`git clone`) & Build APK

### Prasyarat Setelah `git clone`
1. **Git LFS (Vuforia SDK):** Pastikan [Git LFS](https://git-lfs.com/) terpasang lalu jalankan `git lfs pull` agar `Packages/com.ptc.vuforia.engine-11.4.4.tgz` terunduh penuh (bukan file pointer 134 byte).
2. **Paket MediaPipe Lokal (dikecualikan dari Git karena ~277 MB):**
   - Unduh **`com.github.homuler.mediapipe-0.16.3.tgz`** dari [homuler/MediaPipeUnityPlugin v0.16.3](https://github.com/homuler/MediaPipeUnityPlugin/releases/tag/v0.16.3).
   - Letakkan di `Packages/com.github.homuler.mediapipe-0.16.3.tgz`.
3. Pastikan `Assets/StreamingAssets/hand_landmarker.task` tersedia di repositori hasil clone.

### Menjalankan & Build Android
1. Buka proyek menggunakan **Unity `6000.6.9f1`** dan buka `Assets/Scenes/SampleScene.unity`.
2. Jalankan **Play Mode** untuk menguji Layar Judul, Mode Udara (via webcam), dan Mode Kartu (via Vuforia).
3. Untuk build APK: buka **File -> Build Profiles -> Android -> Build** (`IL2CPP`, `ARM64`, `OpenGLES3`, `Min API 29`). `AndroidLibcppCopier.cs` akan menyalin `libc++_shared.so` secara otomatis ke proyek Gradle.

---

## 8. Kredit Aset & Lisensi

- **Model 3D Sci-Fi Modular (`V5_SciFi`):** *Ultimate Modular Sci-Fi Pack* oleh **Quaternius** — Lisensi **CC0 1.0 Universal**.
- **AR Marker Tracking SDK:** PTC Vuforia Engine `11.4.4`.
- **Hand Tracking SDK:** [MediaPipeUnityPlugin (`homuler`, v0.16.3)](https://github.com/homuler/MediaPipeUnityPlugin) (MIT) & Google MediaPipe `hand_landmarker.task` (Apache-2.0).
