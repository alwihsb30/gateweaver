# GateWeaver AR (v7.0 — Dual-Mode AR: Kartu Fisik v5.0 & Air-Weaving Hologram v7.0)

**Media Pembelajaran Interaktif *Dual-Mode Spatial Augmented Reality* untuk Rangkaian Gerbang Logika Kombinasional Berbasis Kurikulum Merdeka (Fase E)**

---

## 1. Deskripsi Proyek & Pemetaan Kurikulum Merdeka

**GateWeaver AR v7.0** adalah aplikasi pembelajaran interaktif **Dual-Mode Augmented Reality** untuk Android dan Unity Editor yang menyatukan dua pengalaman praktikum rangkaian logika kombinasional dalam satu mesin simulasi terpadu (`CircuitManager` & `GateNode`):

1. **Mode Kartu (`v5.0` — Tangible Spatial AR, 0% Regresi):** Menggunakan 8 kartu cetak fisik dengan Vuforia Engine `11.4.4` untuk menghadirkan 12 model mesin 3D sci-fi modular Quaternius, landasan soket `AR_BaseTile3D`, mekanisme terbuka (*Open Cutaway*) `GateMechanismVisual`, Pod Tabel Kebenaran `2x2` (`OutputTruthPodVisual`), tuas sakelar fisik 3D, 5 diorama Output reaktif, saluran energi berlapis (*plasma core + outer sheath*), dan *Connection Snap Lock VFX* — seluruh alur auto-snap kartu fisik v5 tetap utuh 100%.
2. **Mode Udara (`v7.0` — Markerless Hologram Air-Weaving, 100% *Zero-Touch*):** Memungkinkan siswa merakit, menyambungkan kabel manual, dan melakukan *debugging* rangkaian gerbang logika **tanpa kartu cetak dan tanpa tombol layar** menggunakan kamera depan Android / webcam + **MediaPipe `HandLandmarker`** (CPU mode) dengan hologram 3D melayang, **2-Tahap Circle Summon + Open-5 Bloom**, kabel port-ke-port berbasis **Cubit (`Pinch`)**, manipulasi objek berbasis **Kepal (`Fist`)**, sakelar **Sentuh Telunjuk `#8`**, serta **100% Debugging Mandiri** lewat Pod `00/01/10/11` menghadap kamera hingga *Auto-Win* otomatis.

### Pemetaan Capaian Pembelajaran (CP)
- **Mata Pelajaran:** Informatika (Kurikulum Merdeka — SMA/SMK Kelas X Fase E, serta pengayaan SMP Fase D)
- **Elemen:** Sistem Komputer (**SK**) dan Berpikir Komputasional (**BK**)
- **Kompetensi Materi:**
  - Memahami fungsi, simbol, mekanisme aliran energi internal (*Open Cutaway*), dan tabel kebenaran gerbang logika dasar maupun kombinasional (`AND`, `OR`, `NOT`, `XOR`, `NAND`, `NOR`, `XNOR`, serta ekuivalensi **Hukum De Morgan**).
  - Menerjemahkan spesifikasi permasalahan logika nyata menjadi ekspresi aljabar Boolean dan rangkaian gerbang kombinasional baik melalui kartu fisik maupun interaksi gestur tangan spasial.
  - Melakukan *debugging* mandiri secara sistematis pada keempat kombinasi biner (`00`, `01`, `10`, `11`) melalui Pod `OutputTruthPodVisual` 3D serta mendiagnosis kesalahan sambungan, polaritas, atau kelebihan kuota gerbang.
  - Mengoptimasi efisiensi rangkaian sesuai batas kuota komponen dan mengembangkan kecakapan abad ke-21 (**4C**: *Critical Thinking*, *Collaboration*, *Communication*, dan *Creativity*).

---

## 2. Arsitektur Dual-Mode v7.0, HUD Minimalis, & Skema Kontrol Mode Udara

### A. Perpindahan Dual-Mode, *Camera Handover* (Camera2-Safe), & HUD Minimalis
Pengaturan mode, level, petunjuk, reset, rapor, dan audio disatukan ke dalam **Bilah Atas Ramping (`72 px`) + Laci Menu Kompak** pojok kanan atas ([`CompactHudMenuController.cs`](Assets/Scripts/Levels/CompactHudMenuController.cs), [`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs)):
- **Mode Udara (`v7`):** **0 tombol gameplay** di layar (*100% Zero-Touch*). Seluruh interaksi dilakukan lewat gestur tangan 3D; `Txt_Feedback` hanya muncul sementara dalam mode *Toast-Only* berkontras tinggi.
- **Mode Kartu (`v5`):** Menampilkan 4 kapsul ramping semi-transparan di sudut bawah (`Input A`, `Input B`, `Uji Rangkaian` / `Level Berikutnya`; tombol Uji otomatis tersembunyi di Mode Sandbox).
- **Perpindahan Instan (`Mode: Udara (v7)` ↔ `Mode: Kartu (v5)`):**
  - **Masuk ke Mode Udara (`v7`):** `VuforiaBehaviour` beserta seluruh `DefaultObserverEventHandler` pada 8 kartu dijeda secara bersih dengan jeda rilis perangkat keras kamera Android Camera2 (`0.35 dtk`), lalu kamera depan (atau webcam di Editor) dibuka pada resolusi efisien `480 x 360 @ 30 FPS` dan dihubungkan ke `MediaPipeUnityPlugin 0.16.3` (`HandLandmarker` CPU mode dengan model [`Assets/StreamingAssets/hand_landmarker.task`](Assets/StreamingAssets/hand_landmarker.task)). Seluruh 8 `GateNode` masuk ke mode `useExternalSpatialPose = true` pada meja kerja spasial 3D (`Input A/B` di kiri, `Output` di kanan, 5 gerbang di **Rak Parkir Atas**).
  - **Kembali ke Mode Kartu (`v5`):** Handle native MediaPipe (`IDisposable`), `NativeArray<byte>`, dan `WebCamTexture` dimatikan serta dilepas penuh (`OnDisable`/`OnDestroy` anti-SIGABRT), lalu `DefaultObserverEventHandler` dan `VuforiaBehaviour` dipulihkan 100%.

### B. Skema Kontrol Final Mode Udara (`v7.0`)
Seluruh interaksi Mode Udara dipisahkan secara tegas berdasarkan gestur (*dual-channel, zero-GC*) sehingga tidak saling bertabrakan:

1. **Tracking & Visual 3D Melayang ([`AirWeavingController.cs`](Assets/Scripts/AirWeaving/AirWeavingController.cs), [`AirWeavingHologramVisual.cs`](Assets/Scripts/AirWeaving/AirWeavingHologramVisual.cs))**
   - Proyeksi tangan 21 sendi **1:1** dengan *smoothing* adaptif berbasis kecepatan (*One-Euro-style*): tenang dan presisi saat membidik port atau sakelar, namun responsif tanpa *lag* saat memindah objek cepat.
   - Seluruh hologram 3D tampil **melayang bebas tanpa cincin lantai** dengan animasi *sinusoidal levitation* halus dan *velocity tilt* hingga **`±12°`** mengikuti gerak seret.
   - Area rakit ditandai dengan **4 siku sudut (`AssemblyZone`)** minimalis (tanpa kotak 2D permanen), dan *reticle* telapak / cincin cubit otomatis tersembunyi ketika tangan keluar dari jangkauan kamera.

2. **Summon Gerbang — 2-Tahap *Circle Summon* + *Open-5 Bloom* ([`AirWeavingCircleSummoner.cs`](Assets/Scripts/AirWeaving/AirWeavingCircleSummoner.cs), [`AirWeavingSummonVisual.cs`](Assets/Scripts/AirWeaving/AirWeavingSummonVisual.cs))**
   - **Tahap 1 (Pose + Putar Lingkaran):** Acungkan pose jari di dalam ruang rakit — indikator pose live langsung tampil di ujung jari aktif — lalu putar ujung jari membentuk lingkaran hingga cincin pengisian penuh:
     | Pose Jari | Indikator Live | Gerbang Dipanggil |
     | :---: | :---: | :---: |
     | **`1` jari** (Telunjuk) | `[ 1: AND ]` | **`AND`** |
     | **`2` jari** (Telunjuk + Tengah) | `[ 2: OR ]` | **`OR`** |
     | **`3` jari** (Telunjuk + Tengah + Manis) | `[ 3: XOR ]` | **`XOR`** |
     | **`4` jari** (Jempol lipat) | `[ 4: NAND ]` | **`NAND`** |
     | **`Jempol saja`** (4 jari lain lipat) | `[ JEMPOL: NOT ]` | **`NOT`** |
   - **Tahap 2 (*Open-5 Bloom*):** Setelah pratinjau hologram terkunci, buka lebar **5 jari** untuk memunculkan (*spawn*) gerbang dari **Rak Parkir Atas** tepat di pusat lingkaran dengan efek ledakan cincin *Bloom*.
   - **Batal & Anti-Duplikat Non-Destruktif:** Kepalkan tangan (`Fist`) untuk membatalkan panggilan secara instan. Jika jenis gerbang tersebut sudah terpasang di papan, sistem menampilkan **Notice + Pulse amber** non-destruktif (`[ <GATE> SUDAH DI PAPAN ]`) tanpa memindahkan gerbang yang sudah dirangkai.

3. **Manajemen Kabel Manual — Cubit / `Pinch` ([`AirWeavingCableRouter.cs`](Assets/Scripts/AirWeaving/AirWeavingCableRouter.cs), [`AirWeavingHandGestureDetector.cs`](Assets/Scripts/AirWeaving/AirWeavingHandGestureDetector.cs))**
   - Cubit **Jempol `#4` + Telunjuk `#8`** pada orb port hologram (`Port_Out` atau `Port_In`) dengan *pre-pinch guard* skala-invarian agar tidak bentrok dengan gestur lain:
     - **Tarik & *Fan-Out*:** Cubit `Port_Out` lalu seret ke `Port_In` tujuan untuk memasang kabel (mencubit `Port_Out` yang sudah terhubung menarik cabang *fan-out* baru tanpa memutus kabel lama).
     - **Cabut, Pindah, & Hapus:** Cubit `Port_In` yang sudah terisi untuk mencabut ujung kabel — seret ke `Port_In` lain untuk memindah, atau **lepas di ruang kosong** untuk memutus kabel. Menempelkan kabel baru ke `Port_In` terisi otomatis mengganti kabel lama.
   - **Renderer Kabel Seragam 100%:** Seluruh kabel Mode Udara (primer maupun *fan-out*) dirender seragam tepat dari orb ke orb oleh `AirWeavingCableRouter` menggunakan **pool 20 Bezier zero-GC** dengan warna sinyal live (`1` = sian terang, `0` = merah/oranye menyala).

4. **Manipulasi Objek — Kepal / `Fist` ([`AirWeavingHandGestureDetector.cs`](Assets/Scripts/AirWeaving/AirWeavingHandGestureDetector.cs), [`AirWeavingController.cs`](Assets/Scripts/AirWeaving/AirWeavingController.cs))**
   - Kepal badan gerbang (*sticky hysteresis* + *occlusion grace* `180 ms`) untuk menggenggam dan memindahkan posisinya secara bebas di dalam `AssemblyZone`.
   - **Hapus / Kembalikan ke Rak:** Tarik gerbang yang sedang digenggam keluar dari 4 siku `AssemblyZone` (bingkai aura dan 4 siku menyala merah) lalu lepas kepalan untuk mengembalikan gerbang ke **Rak Parkir Atas** dan memutus seluruh kabelnya secara bersih.

5. **Sakelar `Input A` & `Input B` — Sentuh Telunjuk `#8` ([`AirWeavingController.cs`](Assets/Scripts/AirWeaving/AirWeavingController.cs))**
   - Sentuh ringan hologram `Input A` atau `Input B` menggunakan ujung jari telunjuk `#8` (`75 ms` *dwell* + *Exit-to-Rearm* anti-dobel) dengan umpan balik cincin ***ripple*** 3D dan jungkit tuas fisik `-38° ↔ +38°` untuk membalik nilai biner **`0 ↔ 1`**.

6. **100% Debugging Mandiri & *Auto-Win* ([`OutputTruthPodVisual.cs`](Assets/Scripts/Core/OutputTruthPodVisual.cs), [`AirWeavingOutputTrigger.cs`](Assets/Scripts/AirWeaving/AirWeavingOutputTrigger.cs))**
   - Diorama `Output` dilengkapi panel **Pod Tabel Kebenaran `2x2`** berisi 4 kotak biner **`00`**, **`01`**, **`10`**, **`11`** yang otomatis sejajar menghadap kamera (*camera-facing*) di Mode Udara, menandai kombinasi aktif saat ini serta status tiap baris (belum diuji / hijau lolos / merah salah).
   - Tepat di bawah Pod tampil **lencana pemandu live** yang menuntun siswa mendiagnosis rangkaian secara mandiri:
     - `SAMBUNG KE OUTPUT` — rangkaian belum terhubung valid ke `Output`.
     - `KURANGI GERBANG!` — jumlah gerbang di papan melebihi kuota misi.
     - `CEK SEL MERAH!` — ada kombinasi input yang keluarannya belum sesuai target.
     - `UJI SAKELAR (X/4)` — gerakkan sakelar `Input A/B` untuk menguji sisa kombinasi biner.
     - `LULUS! 4/4` — keempat sel hijau & dalam kuota → memicu ***Auto-Win* otomatis** + selebrasi 3D.
   - Setelah lulus, **tahan kepal (`Fist`) pada diorama `Output`** untuk lanjut ke level berikutnya secara ***Zero-Touch Next Level***.

### C. Visual Mekanisme Terbuka (*Open Cutaway*) & Otomatisasi `libc++_shared.so` Android
- **Open Cutaway (`GateMechanismVisual.cs`):** Kelima gerbang (`AND`, `OR`, `NOT`, `XOR`, `NAND`) menampilkan konstruksi mekanik terbuka berkode warna sinyal live langsung pada mesin 3D (termasuk *Power Core* internal menyala konstan pada `NOT` & `NAND`) secara *tracking-agnostic* di Mode Kartu maupun Mode Udara.
- **Unity 6 / Gradle 9 Safe (`AndroidLibcppCopier.cs`):** MediaPipe native ARM64 (`libmediapipe_c.so`) membutuhkan `libc++_shared.so` di dalam APK. Skrip editor C# murni [`Assets/Editor/AndroidLibcppCopier.cs`](Assets/Editor/AndroidLibcppCopier.cs) (`IPostGenerateGradleAndroidProject`, `callbackOrder = 99`) secara otomatis mendeteksi NDK bawaan Unity 6 dan menyalin `libc++_shared.so` ke `src/main/jniLibs/arm64-v8a/` saat build Android tanpa `mainTemplate.gradle` usang.

---

## 3. Daftar 8 Kartu Marker Vuforia & Pemetaan 12 Model 3D Sci-Fi

Seluruh target marker terdaftar pada basis data Vuforia [GateWeaverDB.xml](Assets/StreamingAssets/Vuforia/GateWeaverDB.xml) (`0.80 x 0.80` unit Unity, setara kartu cetak `8 cm x 8 cm`) dan berbagi 12 aset FBX modular **Quaternius (CC0)** dari `Assets/Resources/Models/V5_SciFi/` di Mode Kartu maupun Mode Udara:

| No | Target Vuforia | Objek Scene | Peran (`NodeType`) | Kuota Input | Model 3D Quaternius (`V5_SciFi/`) | Mekanisme Visual & Interaksi |
| :---: | :--- | :--- | :--- | :---: | :--- | :--- |
| 1 | `InputA` | `Card_Input_A` | `InputSource` | `0` | `Props_Computer.fbx` | Tuas sakelar 3D (`-38°` LOW / `+38°` HIGH); Mode Udara: sentuh ujung telunjuk `#8` (`75 ms` + *ripple*); Mode Kartu: kapsul bawah, sentuh 3D, atau rotasi kartu `~90°` |
| 2 | `InputB` | `Card_Input_B` | `InputSource` | `0` | `Props_Computer.fbx` | Tuas sakelar 3D (`-38°` LOW / `+38°` HIGH); Mode Udara: sentuh ujung telunjuk `#8` (`75 ms` + *ripple*); Mode Kartu: kapsul bawah, sentuh 3D, atau rotasi kartu `~90°` |
| 3 | `AND` | `Card_Gate_AND` | `AND` | `2` | `Props_ContainerFull.fbx` | Mekanisme *Open Cutaway* seri (`GateMechanismVisual`) + piston kompresor; Mode Udara: Circle Summon **`1` jari** → **Open-5** |
| 4 | `OR` | `Card_Gate_OR` | `OR` | `2` | `Props_Teleporter_1.fbx` | Mekanisme *Open Cutaway* paralel (`GateMechanismVisual`) + turbin energi; Mode Udara: Circle Summon **`2` jari** → **Open-5** |
| 5 | `XOR` | `Card_Gate_XOR` | `XOR` | `2` | `Props_Teleporter_2.fbx` | Mekanisme *Open Cutaway* komparator fasa (`GateMechanismVisual`) + cincin giroskop; Mode Udara: Circle Summon **`3` jari** → **Open-5** |
| 6 | `NAND` | `Card_Gate_NAND` | `NAND` | `2` | `Props_Vessel_Tall.fbx` | Mekanisme *Open Cutaway* + *Power Core* internal (`GateMechanismVisual`) + koil pembalik; Mode Udara: Circle Summon **`4` jari (jempol lipat)** → **Open-5** |
| 7 | `NOT` | `Card_Gate_NOT` | `NOT` | `1` | `Props_Laser.fbx` | Mekanisme *Open Cutaway* + *Power Core* internal (`GateMechanismVisual`) + lensa pembalik fase; Mode Udara: Circle Summon **`Jempol saja`** → **Open-5** |
| 8 | `Output` | `Card_Output` | `OutputActuator` | `1` | 5 Varian Diorama (`RoofTile_OrangeVent`, `Staircase`, `Door_Double`, `Props_Pod`, `Props_Capsule`) | Diorama reaktif + Pod Tabel Kebenaran `2x2` (`00/01/10/11`, `OutputTruthPodVisual`) + lencana pemandu & *Auto-Win* / tahan kepal *Next Level* (Mode Udara) atau tombol/sentuh Uji (Mode Kartu) |

---

## 4. Kurikulum 7 Level Progresif & Mode Sandbox Bebas (Total 21 Bintang)

Seluruh skenario misi dikelola oleh `LevelAndUIManager` ([`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs)) & `CompactHudMenuController` ([`CompactHudMenuController.cs`](Assets/Scripts/Levels/CompactHudMenuController.cs)) dengan penghargaan **3 bintang per level** (maksimal **21 bintang**), lompat instan melalui modal **`Pilih Level`**, *scaffolding* 3 tingkat (`Petunjuk 0/3`), diagnostik miskonsepsi, audio PCM prosedural, dan rapor kompetensi **`Rapor CP`**:

| Level | Judul Misi | Deskripsi Misi | Rangkaian Solusi | Kuota Gerbang | Diorama Output | Target (`AB: 00, 01, 10, 11`) |
| :---: | :--- | :--- | :--- | :---: | :--- | :---: |
| **1** | Alarm Pintu & Jendela | Output = `1` jika `A` atau `B` bernilai `1` | `Y = (A OR B)` | Maks `1` | `RoofTile_OrangeVent` + Sirine | `0, 1, 1, 1` |
| **2** | Sakelar Tangga Dua Arah | Output = `1` jika posisi `A` dan `B` berbeda | `Y = (A XOR B)` | Maks `1` | `Staircase` + LED Tangga | `0, 1, 1, 0` |
| **3** | Kunci Keamanan Brankas | Output = `1` hanya saat `A = 1` dan `B = 0` | `Y = (A AND (NOT B))` | Maks `2` | `Door_Double` + Pintu Brankas | `0, 0, 1, 0` |
| **4** | Pemutus Darurat Reaktor | Output = `0` hanya saat `A = 1` dan `B = 1` | `Y = (A NAND B)` | Maks `1` | `Props_Pod` + Cincin Pendingin | `1, 1, 1, 0` |
| **5** | Sinkronisasi Inti Kembar | Output = `1` saat `A` dan `B` bernilai **sama** (`XNOR`) | `Y = NOT(A XOR B)` | Maks `2` | `Props_Capsule` + Cincin Orbital | `1, 0, 0, 1` |
| **6** | Sensor Hening Ruang Server (NOR) | Output = `1` **hanya** saat `A = 0` dan `B = 0` | `Y = NOT(A OR B)` | Maks `2` | `RoofTile_OrangeVent` + Sirine | `1, 0, 0, 0` |
| **7 (Boss)** | Kunci Biometrik De Morgan | Output = `0` **hanya** saat `A = 0` dan `B = 1` | `Y = (A OR (NOT B))` **atau** `Y = ((NOT A) NAND B)` | Maks `2` | `Door_Double` + Brankas Biometrik | `1, 0, 1, 1` |
| **Sandbox** | Laboratorium Rangkaian Bebas | Eksplorasi bebas tanpa batas kuota & pengamatan sinyal real-time | Rangkaian bebas | Bebas (`∞`) | Menyesuaikan level terakhir | Eksplorasi 4 kombinasi (`00..11`) |

---

## 5. Spesifikasi Teknis Build Android

Dikonfigurasi berdasarkan [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt), [manifest.json](Packages/manifest.json), dan [ProjectSettings.asset](ProjectSettings/ProjectSettings.asset):

| Parameter | Nilai Konfigurasi |
| :--- | :--- |
| **Unity Editor Version** | `6000.6.9f1` (`8c45277fb9c5`) |
| **AR Marker SDK (v5 Mode)** | PTC Vuforia Engine `11.4.4` (`com.ptc.vuforia.engine`) |
| **Hand Tracking SDK (v7 Mode)** | MediaPipeUnityPlugin `0.16.3` (`com.github.homuler.mediapipe`, `HandLandmarker` CPU mode) |
| **Native Android C++ Hook** | [`Assets/Editor/AndroidLibcppCopier.cs`](Assets/Editor/AndroidLibcppCopier.cs) (`IPostGenerateGradleAndroidProject`) |
| **Input System** | Unity New Input System `1.20.1` (`activeInputHandler: 1`, `ENABLE_INPUT_SYSTEM`) |
| **Application ID (Android)** | `com.GateWeaverStudio.GateWeaverAR` |
| **Version / Version Code** | **`7.0`** / **`7`** (`bundleVersion: 7.0`, `AndroidBundleVersionCode: 7`) |
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
│   │   └── AndroidLibcppCopier.cs                  # Otomatisasi salin libc++_shared.so (Unity 6 / Gradle 9)
│   ├── Resources/
│   │   └── Models/V5_SciFi/                        # 12 model FBX sci-fi modular Quaternius (CC0)
│   ├── Scenes/
│   │   └── SampleScene.unity
│   ├── Scripts/
│   │   ├── AirWeaving/
│   │   │   ├── AirWeavingCableRouter.cs            # Kabel manual Pinch (fan-out/unplug) & pool 20 Bezier seragam
│   │   │   ├── AirWeavingCircleSummoner.cs         # State machine 2-tahap Circle-Charge -> Open-5 Bloom
│   │   │   ├── AirWeavingController.cs             # Camera Handover, tracking 1:1 adaptif, levitasi, sakelar #8
│   │   │   ├── AirWeavingHandGestureDetector.cs    # Detektor dual-kanal: Fist (objek), Pinch (kabel), Pose Summon
│   │   │   ├── AirWeavingHologramVisual.cs         # Hologram 3D melayang, aura hover/buang, ripple, reticle adaptif
│   │   │   ├── AirWeavingOutputTrigger.cs          # Pemandu debugging mandiri live Output & Zero-Touch Next Level
│   │   │   ├── AirWeavingSummonVisual.cs           # Indikator pose ujung jari, cincin charge, Notice amber, Bloom VFX
│   │   │   └── UnistrokeGestureRecognizer.cs       # Modul $1 Unistroke legacy v6 (nonaktif default di v7.0)
│   │   ├── Core/
│   │   │   ├── CircuitManager.cs                   # Evaluasi rangkaian: auto-snap Mode Kartu + manual Mode Udara
│   │   │   ├── GateMechanismVisual.cs              # Visual mekanisme terbuka (Open Cutaway) 5 gerbang + Power Core
│   │   │   ├── GateNode.cs                         # Mesin 3D v5.0, port fisik, VFX kabel Kartu, spatial pose Udara
│   │   │   └── OutputTruthPodVisual.cs             # Pod Tabel Kebenaran 2x2 (00/01/10/11) miring/camera-facing
│   │   └── Levels/
│   │       ├── CompactHudMenuController.cs         # Bilah Atas 72px, Laci Menu Kompak, Zero-Touch HUD, Toast-Only
│   │       └── LevelAndUIManager.cs                # 7 level + Sandbox, rapor CP, petunjuk 3 tingkat, audio PCM
│   └── StreamingAssets/
│       ├── hand_landmarker.task                    # Model MediaPipe HandLandmarker (~7,5 MB)
│       └── Vuforia/
│           └── GateWeaverDB.xml                    # Basis data 8 kartu marker Vuforia
├── docs/
│   └── GDD.md                                      # Game Design Document (v7.0)
├── Packages/
│   ├── manifest.json                               # Referensi Vuforia 11.4.4 & MediaPipe 0.16.3
│   └── com.github.homuler.mediapipe-0.16.3.tgz     # (Diabaikan di .gitignore — unduh lokal, lihat Bagian 7)
├── ProjectSettings/
│   ├── ProjectSettings.asset                       # Version 7.0 / VersionCode 7 (IL2CPP ARM64 OpenGLES3)
│   └── ProjectVersion.txt
├── GEMINI.md                                       # Aturan arsitektur & alur kerja pengembang/AI (v7.0)
└── README.md
```

---

## 7. Setup Pengembang (`git clone`), Verifikasi, & Build `GateWeaverAR_v7.apk`

### Prasyarat Setelah `git clone` (Penting!)
Berkas tarball lokal `Packages/com.github.homuler.mediapipe-0.16.3.tgz` (~277 MB) dikecualikan di [`.gitignore`](.gitignore) untuk mematuhi batas 100 MB/file GitHub. Sebelum membuka proyek di Unity Editor:
1. Unduh **`com.github.homuler.mediapipe-0.16.3.tgz`** dari halaman rilis resmi [homuler/MediaPipeUnityPlugin (v0.16.3)](https://github.com/homuler/MediaPipeUnityPlugin/releases/tag/v0.16.3).
2. Letakkan berkas tersebut di folder **`Packages/com.github.homuler.mediapipe-0.16.3.tgz`** (nama berkas harus persis sesuai entri di [`Packages/manifest.json`](Packages/manifest.json)).
3. Pastikan berkas model [`Assets/StreamingAssets/hand_landmarker.task`](Assets/StreamingAssets/hand_landmarker.task) (~7,5 MB) telah tersedia di repositori hasil clone.

### Verifikasi *Play Mode* & Build Android
1. Buka `Assets/Scenes/SampleScene.unity` di Unity Editor **`6000.6.9f1`**.
2. Uji **Mode Kartu (`v5`)**: pastikan 8 kartu marker menampilkan model Quaternius, mekanisme terbuka *Open Cutaway*, Pod Tabel Kebenaran `2x2` miring `-48°`, saluran energi berlapis + *Snap Lock VFX*, 4 kapsul ramping bawah, dan Laci Menu Kompak pojok kanan atas (`Pilih Level`, `Rapor CP`, `Petunjuk`, `Audio`).
3. Buka Laci Menu Kompak lalu pilih **`Mode: Udara (v7)`** untuk menguji **Mode Air-Weaving v7.0**:
   - Verifikasi *Camera Handover* bersih, HUD bawah memiliki **0 tombol gameplay**, hologram 3D melayang halus (*sinusoidal levitation* + *velocity tilt* `±12°`), 4 siku `AssemblyZone` tampil rapi, dan *reticle* menghilang saat tangan keluar kamera.
   - Uji **2-Tahap Circle Summon + Open-5 Bloom**: putar jari untuk 5 pose (`1`=AND, `2`=OR, `3`=XOR, `4` jempol lipat=NAND, `Jempol`=NOT) lalu buka **5 jari** untuk *spawn*, kepal untuk batal instan, serta uji *Notice + Pulse* amber saat memanggil gerbang yang sudah ada di papan.
   - Uji **Kabel Manual (`Pinch` `#4+#8`)**: tarik kabel orb-ke-orb, buat *fan-out* dari `Port_Out`, cabut & lepas di ruang kosong dari `Port_In`.
   - Uji **Manipulasi Objek (`Fist`)**: kepal gerbang untuk memindah posisi; tarik keluar 4 siku `AssemblyZone` (menyala merah) lalu lepas untuk mengembalikan ke rak & memutus kabelnya.
   - Uji **Sakelar Telunjuk `#8` & Debugging Mandiri**: sentuh `Input A`/`Input B` (`75 ms` + *ripple*) untuk menguji keempat kotak `00`, `01`, `10`, `11` pada Pod `OutputTruthPodVisual` hingga `LULUS! 4/4` memicu *Auto-Win*, lalu tahan kepal pada Output untuk *Zero-Touch Next Level*.
   - Buka Laci Menu Kompak lalu kembali ke **`Mode: Kartu (v5)`** dan pastikan kamera Vuforia pulih 100%.
4. Build **`GateWeaverAR_v7.apk`**: buka **File → Build Profiles → Android → Build** (`Version = 7.0`, `Bundle Version Code = 7`, `IL2CPP`, `ARM64`, `OpenGLES3`, `Min API 29`). Skrip [`AndroidLibcppCopier.cs`](Assets/Editor/AndroidLibcppCopier.cs) akan otomatis menyisipkan `libc++_shared.so` ke proyek Gradle.

---

## 8. Kredit Aset & Lisensi

- **Model 3D Sci-Fi Modular (`Assets/Resources/Models/V5_SciFi/`):** *Ultimate Modular Sci-Fi Pack* oleh **Quaternius** — lisensi **CC0 1.0 Universal (Public Domain)**.
- **AR Marker Tracking SDK:** PTC Vuforia Engine (`11.4.4`).
- **Hand Landmark Tracking SDK:** [MediaPipeUnityPlugin (`homuler`, v0.16.3)](https://github.com/homuler/MediaPipeUnityPlugin) (MIT License) & Google MediaPipe (`hand_landmarker.task`, Apache-2.0 License).
