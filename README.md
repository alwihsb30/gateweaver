# GateWeaver AR (v6.0 — Dual-Mode AR: Kartu Fisik v5.0 & Air-Weaving Tanpa Kartu v6.0)

**Media Pembelajaran Interaktif *Dual-Mode Spatial Augmented Reality* untuk Rangkaian Gerbang Logika Kombinasional Berbasis Kurikulum Merdeka (Fase E)**

---

## 1. Deskripsi Proyek & Pemetaan Kurikulum Merdeka

**GateWeaver AR v6.0** adalah aplikasi pembelajaran interaktif **Dual-Mode Augmented Reality** untuk Android dan Unity Editor yang menyatukan dua pengalaman praktikum rangkaian logika kombinasional dalam satu mesin simulasi terpadu (`CircuitManager` & `GateNode`):

1. **Mode Kartu (`v5.0` — Tangible Spatial AR):** Menggunakan 8 kartu cetak fisik dengan Vuforia Engine `11.4.4` untuk menghadirkan 12 model mesin 3D sci-fi modular Quaternius lengkap dengan landasan soket `AR_BaseTile3D`, bayangan kontak, tuas sakelar fisik 3D, mekanisme animasi tiap gerbang, 5 diorama Output reaktif, saluran energi berlapis (*plasma core + outer sheath*), 4 *energy bolt*, dan *Connection Snap Lock VFX*.
2. **Mode Udara (`v6.0` — Markerless Air-Weaving):** Memungkinkan siswa merakit rangkaian gerbang logika **tanpa perlu mencetak kartu** menggunakan kamera depan Android / webcam + **MediaPipe `HandLandmarker`** (CPU mode) dengan rangka tangan neon 21 sendi berfilter **One-Euro Filter**, manipulasi *Scale-Invariant Air-Pinch + Occlusion Grace*, *Air-Tap* sakelar `Input A/B`, serta **`$1 Unistroke` Air-Draw Spawner** untuk memanggil 5 gerbang logika langsung dengan menggambar simbol di udara.

### Pemetaan Capaian Pembelajaran (CP)
- **Mata Pelajaran:** Informatika (Kurikulum Merdeka — SMA/SMK Kelas X Fase E, serta pengayaan SMP Fase D)
- **Elemen:** Sistem Komputer (**SK**) dan Berpikir Komputasional (**BK**)
- **Kompetensi Materi:**
  - Memahami fungsi, simbol, dan tabel kebenaran gerbang logika dasar maupun kombinasional (`AND`, `OR`, `NOT`, `XOR`, `NAND`, `NOR`, `XNOR`, serta ekuivalensi **Hukum De Morgan**).
  - Menerjemahkan spesifikasi permasalahan logika nyata menjadi ekspresi aljabar Boolean dan rangkaian gerbang kombinasional baik melalui kartu fisik maupun interaksi gestur tangan spasial.
  - Menganalisis perubahan sinyal waktu nyata menggunakan diagram pewaktuan digital (*timing diagram*) `A(t)`, `B(t)`, dan `Y(t)` serta mendiagnosis kesalahan sambungan atau polaritas rangkaian.
  - Mengoptimasi efisiensi rangkaian sesuai batas kuota komponen dan mengembangkan kecakapan abad ke-21 (**4C**: *Critical Thinking*, *Collaboration*, *Communication*, dan *Creativity*).

---

## 2. Arsitektur Dual-Mode v6.0 & Mekanik Air-Weaving

### A. Perpindahan Dual-Mode & *Camera Handover* (Camera2-Safe)
Tombol **`Btn_ToggleAirWeaving`** pada HUD memungkinkan perpindahan seketika antara **`Mode: Kartu (v5)`** dan **`Mode: Udara (v6)`** tanpa memuat ulang scene ([`AirWeavingController.cs`](Assets/Scripts/AirWeaving/AirWeavingController.cs), [`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs)):
- **Saat masuk ke Mode Udara (`v6`):** `VuforiaBehaviour` beserta seluruh `DefaultObserverEventHandler` pada 8 kartu dijeda secara bersih dengan jeda rilis perangkat keras kamera Android Camera2 (`0.35 dtk`), lalu kamera depan (atau webcam di Editor) dibuka pada resolusi efisien `480 x 360 @ 30 FPS` dan dihubungkan ke `MediaPipeUnityPlugin 0.16.3` (`HandLandmarker` CPU mode dengan berkas model [`Assets/StreamingAssets/hand_landmarker.task`](Assets/StreamingAssets/hand_landmarker.task)).
- **Penataan Meja Kerja Spasial 3D:** Seluruh 8 `GateNode` masuk ke mode `useExternalSpatialPose = true` pada kedalaman meja kerja `WorkbenchDepth = 3.60f` (skala 1:1 selaras ambang sambungan `CircuitManager` v5.0 tanpa mengubah satu baris pun di `CircuitManager.cs`): terminal `Input A`, `Input B`, dan `Output` terkunci ergonomis di sisi kiri/kanan, sementara 5 gerbang (`AND`, `OR`, `XOR`, `NAND`, `NOT`) tersusun rapi pada **Rak Parkir Atas** dengan lencana 3D tajam.
- **Saat kembali ke Mode Kartu (`v5`):** Handle native MediaPipe (`IDisposable`), `NativeArray<byte>`, dan `WebCamTexture` dimatikan serta dilepas penuh (`OnDisable`/`OnDestroy` anti-SIGABRT), status `DefaultObserverEventHandler` dan `VuforiaBehaviour` dipulihkan, dan seluruh 8 kartu kembali melacak marker fisik.

### B. Interaksi Gestur Tangan Air-Weaving (`v6.0`)
1. **Rangka Tangan Neon 21 Sendi + *One-Euro Filter* Adaptif (*Zero-GC*)**
   - Ke-21 landmark tangan dipetakan 1:1 ke bidang meja kerja 3D menggunakan **One-Euro Filter** adaptif (`minCutoff = 1.6 Hz`, `beta = 0.045`, `dCutoff = 1.0 Hz`, terkalibrasi untuk 20–30 FPS kamera Android/webcam dengan array *struct* pra-alokasi tanpa GC).
   - Sangat tenang dan bebas getaran saat *hover* atau menahan ujung telunjuk, namun responsif tanpa *lag* saat menarik mesin 3D atau menggambar pola cepat; filter otomatis di-reset bersih ketika tangan baru muncul kembali.
2. ***Scale-Invariant Air-Pinch* + *Hysteresis* + *Occlusion Grace***
   - Rasio cubitan ternormalisasi terhadap skala telapak tangan (`dist(#4, #8) / max(0.06, dist(#0, #9))`) dengan *hysteresis* (`aktif < 0.34`, `lepas > 0.46`) dan masa tenggang oklusi `0.16 dtk` agar genggaman tidak terputus meski jari sesaat tertutup saat memindahkan gerbang.
   - Cubit gerbang di **Rak Parkir Atas** untuk menariknya ke **Zona Rakit** (kabel energi berlapis v5.0 & *Snap Lock VFX* otomatis tersambung), atau seret kembali ke atas ambang rak untuk memarkir ulang.
3. ***Air-Tap* Sakelar `Input A` & `Input B`**
   - Sentuh langsung dengan ujung telunjuk (atau cubit singkat) pada konsol 3D `Input A` / `Input B` untuk menjungkitkan tuas fisik `-38° <-> +38°` dengan *cooldown* anti-dobel.
4. **`$1 Unistroke` Air-Draw Gate Spawner & *Enhanced Neon Energy Trail* ([`UnistrokeGestureRecognizer.cs`](Assets/Scripts/AirWeaving/UnistrokeGestureRecognizer.cs))**
   - Prioritas interaksi terjamin: *Air-Pinch* dan *Air-Tap* `Input A/B` selalu mendahului Air-Draw dan langsung membatalkan goresan aktif bila pengguna mencubit gerbang.
   - Acungkan pose **Tongkat Telunjuk** (jari telunjuk lurus, minimal 2 dari 3 jari lain terlipat) di dalam Zona Rakit untuk menggores lintasan berpendar sian-violet (`AR_IndexNeonTrail` + `AR_AirDrawStrokeLine`). Lepas pose atau tahan diam `0.38 dtk` untuk memanggil gerbang tepat di pusat goresan disertai kilatan emas-hijau dan *energy burst*:

| Simbol Udara | Gerbang Dipanggil | Arah Goresan yang Didukung |
| :---: | :---: | :--- |
| **`^`** | **`AND`** | Kiri-ke-kanan & kanan-ke-kiri |
| **`O`** | **`OR`** | Lingkaran searah (`CW`) & berlawanan (`CCW`) dari segala sudut awal (`0°`, `90°`, `-90°`, `180°`) |
| **`Z`** | **`XOR`** | Maju, mundur, & cermin |
| **`V`** | **`NAND`** | Kiri-ke-kanan & kanan-ke-kiri |
| **`\|`** | **`NOT`** | Garis vertikal tegas atas-ke-bawah & bawah-ke-atas (prapemeriksaan proporsi *bounding-box* anti-salah-baca sapuan datar) |

### C. Otomatisasi `libc++_shared.so` Android (Unity 6 / Gradle 9 Safe)
- MediaPipe native ARM64 (`libmediapipe_c.so`) membutuhkan `libc++_shared.so` di dalam APK.
- Alih-alih memakai `mainTemplate.gradle` kustom yang rentan terhadap perubahan AGP 9 / Gradle 9 di Unity 6, proyek menggunakan skrip editor C# murni [`Assets/Editor/AndroidLibcppCopier.cs`](Assets/Editor/AndroidLibcppCopier.cs) (`IPostGenerateGradleAndroidProject`, `callbackOrder = 99`) yang secara otomatis mendeteksi NDK bawaan Unity 6 dan menyalin `libc++_shared.so` ke `src/main/jniLibs/arm64-v8a/` saat build Android.

---

## 3. Daftar 8 Kartu Marker Vuforia & Pemetaan 12 Model 3D Sci-Fi

Seluruh target marker terdaftar pada basis data Vuforia [GateWeaverDB.xml](Assets/StreamingAssets/Vuforia/GateWeaverDB.xml) (`0.80 x 0.80` unit Unity, setara kartu cetak `8 cm x 8 cm`) dan berbagi 12 aset FBX modular **Quaternius (CC0)** dari `Assets/Resources/Models/V5_SciFi/` di Mode Kartu maupun Mode Udara:

| No | Target Vuforia | Objek Scene | Peran (`NodeType`) | Kuota Input | Model 3D Quaternius (`V5_SciFi/`) | Mekanisme & Interaksi |
| :---: | :--- | :--- | :--- | :---: | :--- | :--- |
| 1 | `InputA` | `Card_Input_A` | `InputSource` | `0` | `Props_Computer.fbx` | Tuas sakelar 3D (`-38°` LOW / `+38°` HIGH); ubah via sentuh 3D, *Air-Tap*, rotasi kartu `~90°`, atau `Btn_ToggleInputA` |
| 2 | `InputB` | `Card_Input_B` | `InputSource` | `0` | `Props_Computer.fbx` | Tuas sakelar 3D (`-38°` LOW / `+38°` HIGH); ubah via sentuh 3D, *Air-Tap*, rotasi kartu `~90°`, atau `Btn_ToggleInputB` |
| 3 | `AND` | `Card_Gate_AND` | `AND` | `2` | `Props_ContainerFull.fbx` | 2 piston kompresor vertikal sinkron + percikan energi; panggil di udara via simbol **`^`** |
| 4 | `OR` | `Card_Gate_OR` | `OR` | `2` | `Props_Teleporter_1.fbx` | Turbin energi berputar & kristal inti melayang; panggil di udara via simbol **`O`** |
| 5 | `XOR` | `Card_Gate_XOR` | `XOR` | `2` | `Props_Teleporter_2.fbx` | 2 cincin giroskop berlawanan arah; panggil di udara via simbol **`Z`** |
| 6 | `NAND` | `Card_Gate_NAND` | `NAND` | `2` | `Props_Vessel_Tall.fbx` | Koil energi menyapu tabung vertikal & kubah pembalik; panggil di udara via simbol **`V`** |
| 7 | `NOT` | `Card_Gate_NOT` | `NOT` | `1` | `Props_Laser.fbx` | Cincin lensa fokus & bilah pembalik fase; panggil di udara via simbol **`\|`** |
| 8 | `Output` | `Card_Output` | `OutputActuator` | `1` | 5 Varian Diorama (`RoofTile_OrangeVent`, `Staircase`, `Door_Double`, `Props_Pod`, `Props_Capsule`) | Diorama reaktif + selebrasi percikan 3D saat lulus; sentuh 3D untuk uji rangkaian (Level) atau inspeksi rumus (Sandbox) |

---

## 4. Kurikulum 7 Level Progresif & Mode Sandbox Bebas (Total 21 Bintang)

Seluruh skenario misi dikelola oleh `LevelAndUIManager` ([`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs)) dengan penghargaan **3 bintang per level** (maksimal **21 bintang**), lompat instan melalui modal **`Panel_LevelSelector`**, pemindai tabel kebenaran & osiloskop bertab yang dapat dilipat, *scaffolding* 3 tingkat, diagnostik miskonsepsi, audio PCM prosedural, dan rapor kompetensi **`Panel_CompetencyReport`**:

| Level | Judul Misi | Deskripsi Misi | Rangkaian Solusi | Kuota Gerbang | Diorama Output | Target (`AB: 00, 01, 10, 11`) |
| :---: | :--- | :--- | :--- | :---: | :--- | :---: |
| **1** | Alarm Pintu & Jendela | Output = `1` jika `A` atau `B` bernilai `1` | `Y = (A OR B)` | Maks `1` | `RoofTile_OrangeVent` + Sirine | `0, 1, 1, 1` |
| **2** | Sakelar Tangga Dua Arah | Output = `1` jika posisi `A` dan `B` berbeda | `Y = (A XOR B)` | Maks `1` | `Staircase` + LED Tangga | `0, 1, 1, 0` |
| **3** | Kunci Keamanan Brankas | Output = `1` hanya saat `A = 1` dan `B = 0` | `Y = (A AND (NOT B))` | Maks `2` | `Door_Double` + Pintu Brankas | `0, 0, 1, 0` |
| **4** | Pemutus Darurat Reaktor | Output = `0` hanya saat `A = 1` dan `B = 1` | `Y = (A NAND B)` | Maks `1` | `Props_Pod` + Cincin Pendingin | `1, 1, 1, 0` |
| **5** | Sinkronisasi Inti Kembar | Output = `1` saat `A` dan `B` bernilai **sama** (`XNOR`) | `Y = NOT(A XOR B)` | Maks `2` | `Props_Capsule` + Cincin Orbital | `1, 0, 0, 1` |
| **6** | Sensor Hening Ruang Server (NOR) | Output = `1` **hanya** saat `A = 0` dan `B = 0` | `Y = NOT(A OR B)` | Maks `2` | `RoofTile_OrangeVent` + Sirine | `1, 0, 0, 0` |
| **7 (Boss)** | Kunci Biometrik De Morgan | Output = `0` **hanya** saat `A = 0` dan `B = 1` | `Y = (A OR (NOT B))` **atau** `Y = ((NOT A) NAND B)` | Maks `2` | `Door_Double` + Brankas Biometrik | `1, 0, 1, 1` |
| **Sandbox** | Laboratorium Rangkaian Bebas | Eksplorasi bebas tanpa batas kuota & pindai 4 kombinasi | Rangkaian bebas | Bebas (`∞`) | Menyesuaikan level terakhir | Pemindaian 4 baris (`00..11`) |

---

## 5. Spesifikasi Teknis Build Android

Dikonfigurasi berdasarkan [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt), [manifest.json](Packages/manifest.json), dan [ProjectSettings.asset](ProjectSettings/ProjectSettings.asset):

| Parameter | Nilai Konfigurasi |
| :--- | :--- |
| **Unity Editor Version** | `6000.6.9f1` (`8c45277fb9c5`) |
| **AR Marker SDK (v5 Mode)** | PTC Vuforia Engine `11.4.4` (`com.ptc.vuforia.engine`) |
| **Hand Tracking SDK (v6 Mode)** | MediaPipeUnityPlugin `0.16.3` (`com.github.homuler.mediapipe`, `HandLandmarker` CPU mode) |
| **Native Android C++ Hook** | [`Assets/Editor/AndroidLibcppCopier.cs`](Assets/Editor/AndroidLibcppCopier.cs) (`IPostGenerateGradleAndroidProject`) |
| **Input System** | Unity New Input System `1.20.1` (`activeInputHandler: 1`, `ENABLE_INPUT_SYSTEM`) |
| **Application ID (Android)** | `com.GateWeaverStudio.GateWeaverAR` |
| **Version / Version Code** | **`6.0`** / **`6`** (`bundleVersion: 6.0`, `AndroidBundleVersionCode: 6`) |
| **Minimum API Level** | Android `10.0` (`API Level 29`) |
| **Scripting Backend** | `IL2CPP` (`scriptingBackend.Android: 1`) |
| **Target Architecture** | `ARM64` (`AndroidTargetArchitectures: 2`) |
| **Graphics API** | `OpenGLES3` (Built-in Render Pipeline, `m_Automatic: 0`) |
| **Orientasi Layar Utama** | `Landscape Left` (`defaultScreenOrientation: 3`, Canvas referensi `1920 x 1080`) |
| **Target Frame Rate & Layar** | `60 FPS` (`Application.targetFrameRate = 60`) + Anti-Sleep (`SleepTimeout.NeverSleep`) |

---

## 6. Struktur Skrip & Aset Utama

```text
GateWeaverAR/
├── Assets/
│   ├── Editor/
│   │   └── AndroidLibcppCopier.cs              # Otomatisasi salin libc++_shared.so (Unity 6 / Gradle 9)
│   ├── Resources/
│   │   └── Models/V5_SciFi/                    # 12 model FBX sci-fi modular Quaternius (CC0)
│   ├── Scenes/
│   │   └── SampleScene.unity
│   ├── Scripts/
│   │   ├── AirWeaving/
│   │   │   ├── AirWeavingController.cs         # Camera Handover, One-Euro Filter, Air-Pinch/Tap, Air-Draw
│   │   │   └── UnistrokeGestureRecognizer.cs   # $1 Unistroke Recognizer murni C# (zero-GC) untuk ^, O, Z, V, |
│   │   ├── Core/
│   │   │   ├── CircuitManager.cs               # Routing spasial Greedy Closest-Pair & anti-siklus
│   │   │   └── GateNode.cs                     # Mesin 3D v5.0, port fisik, VFX kabel, external spatial pose
│   │   └── Levels/
│   │       └── LevelAndUIManager.cs            # 7 level + Sandbox, HUD bertab, audio PCM, tombol Dual-Mode
│   └── StreamingAssets/
│       ├── hand_landmarker.task                # Model MediaPipe HandLandmarker (~7,5 MB)
│       └── Vuforia/
│           └── GateWeaverDB.xml                # Basis data 8 kartu marker Vuforia
├── Packages/
│   ├── manifest.json                           # Referensi Vuforia 11.4.4 & MediaPipe 0.16.3
│   └── com.github.homuler.mediapipe-0.16.3.tgz # (Diabaikan di .gitignore — unduh lokal, lihat Bagian 7)
├── ProjectSettings/
│   ├── ProjectSettings.asset                   # Version 6.0 / VersionCode 6 (IL2CPP ARM64 OpenGLES3)
│   └── ProjectVersion.txt
└── README.md
```

---

## 7. Setup Pengembang (`git clone`), Verifikasi, & Build `GateWeaverAR_v6.apk`

### Prasyarat Setelah `git clone` (Penting!)
Berkas tarball lokal `Packages/com.github.homuler.mediapipe-0.16.3.tgz` (~277 MB) dikecualikan di [`.gitignore`](.gitignore) untuk mematuhi batas 100 MB/file GitHub. Sebelum membuka proyek di Unity Editor:
1. Unduh **`com.github.homuler.mediapipe-0.16.3.tgz`** dari halaman rilis resmi [homuler/MediaPipeUnityPlugin (v0.16.3)](https://github.com/homuler/MediaPipeUnityPlugin/releases/tag/v0.16.3).
2. Letakkan berkas tersebut di folder **`Packages/com.github.homuler.mediapipe-0.16.3.tgz`** (nama berkas harus persis sesuai entri di [`Packages/manifest.json`](Packages/manifest.json)).
3. Pastikan berkas model [`Assets/StreamingAssets/hand_landmarker.task`](Assets/StreamingAssets/hand_landmarker.task) (~7,5 MB) telah tersedia di repositori hasil clone.

### Verifikasi *Play Mode* & Build Android
1. Buka `Assets/Scenes/SampleScene.unity` di Unity Editor **`6000.6.9f1`**.
2. Uji **Mode Kartu (`v5`)**: pastikan 8 kartu marker menampilkan model Quaternius skala ~2x, tuas 3D interaktif, saluran energi berlapis + *Snap Lock VFX*, HUD bertab/lipat, `Pilih Level`, dan `Rapor CP`.
3. Tekan **`Mode: Udara (v6)`** untuk menguji **Mode Air-Weaving**:
   - Verifikasi *Camera Handover* bersih dan rangka 21 sendi neon tampil halus tanpa getaran saat diam (One-Euro Filter) namun responsif saat bergerak cepat.
   - Uji *Air-Pinch* untuk menarik gerbang dari Rak Parkir Atas ke Zona Rakit (dan mengembalikannya ke atas untuk parkir), serta *Air-Tap* pada `Input A`/`Input B`.
   - Uji **Air-Draw** dengan pose Tongkat Telunjuk di Zona Rakit untuk 5 pola: `^` (`AND`), `O` (`OR`), `Z` (`XOR`), `V` (`NAND`), dan `|` (`NOT`).
   - Kembali ke **`Mode: Kartu (v5)`** dan pastikan kamera Vuforia pulih bersih.
4. Build **`GateWeaverAR_v6.apk`**: buka **File → Build Profiles → Android → Build** (`Version = 6.0`, `Bundle Version Code = 6`, `IL2CPP`, `ARM64`, `OpenGLES3`, `Min API 29`). Skrip [`AndroidLibcppCopier.cs`](Assets/Editor/AndroidLibcppCopier.cs) akan otomatis menyisipkan `libc++_shared.so` ke proyek Gradle.

---

## 8. Kredit Aset & Lisensi

- **Model 3D Sci-Fi Modular (`Assets/Resources/Models/V5_SciFi/`):** *Ultimate Modular Sci-Fi Pack* oleh **Quaternius** — lisensi **CC0 1.0 Universal (Public Domain)**.
- **AR Marker Tracking SDK:** PTC Vuforia Engine (`11.4.4`).
- **Hand Landmark Tracking SDK:** [MediaPipeUnityPlugin (`homuler`, v0.16.3)](https://github.com/homuler/MediaPipeUnityPlugin) (MIT License) & Google MediaPipe (`hand_landmarker.task`, Apache-2.0 License).
