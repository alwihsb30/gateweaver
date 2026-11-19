# GateWeaver AR (v5.0 — Tangible 3D AR Overhaul & v6 Spatial Readiness)

**Media Pembelajaran Interaktif *Tangible Spatial Augmented Reality* untuk Rangkaian Gerbang Logika Kombinasional Berbasis Kurikulum Merdeka (Fase E)**

---

## 1. Deskripsi Proyek & Pemetaan Kurikulum Merdeka

**GateWeaver AR v5.0** adalah aplikasi pembelajaran interaktif berbasis **Tangible Spatial Augmented Reality** untuk perangkat Android yang menghadirkan mesin logika 3D sci-fi modular yang menapak realistis di atas kartu cetak meja nyata, digerakkan oleh tuas sakelar fisik 3D, mekanisme gerbang unik, diorama aktuator reaktif, saluran energi fisik berlapis (*plasma core + outer sheath*), serta arsitektur modular siap **v6.0** (*tracking-agnostic spatial pose* & *physical port anchors*). Seluruh visualisasi 3D dipadukan dengan interaksi sentuh hologram langsung, HUD bertab yang dapat dilipat (*collapsible*), lompat level instan, telemetri pin I/O, osiloskop sinyal digital, bimbingan *scaffolding* 3 tingkat, dan diagnostik miskonsepsi otomatis.

### Pemetaan Capaian Pembelajaran (CP)
- **Mata Pelajaran:** Informatika (Kurikulum Merdeka — SMA/SMK Kelas X Fase E, serta pengayaan SMP Fase D)
- **Elemen:** Sistem Komputer (**SK**) dan Berpikir Komputasional (**BK**)
- **Kompetensi Materi:**
  - Memahami fungsi, simbol, dan tabel kebenaran gerbang logika dasar maupun kombinasional (`AND`, `OR`, `NOT`, `XOR`, `NAND`, `NOR`, `XNOR`, serta ekuivalensi **Hukum De Morgan**).
  - Menerjemahkan spesifikasi permasalahan logika nyata menjadi ekspresi aljabar Boolean dan rangkaian kartu gerbang kombinasional.
  - Menganalisis perubahan sinyal waktu nyata menggunakan diagram pewaktuan digital (*timing diagram*) `A(t)`, `B(t)`, dan `Y(t)` serta mendiagnosis kesalahan sambungan atau polaritas rangkaian.
  - Mengoptimasi efisiensi rangkaian sesuai batas kuota komponen dan mengembangkan kecakapan abad ke-21 (**4C**: *Critical Thinking*, *Collaboration*, *Communication*, dan *Creativity*) melalui manipulasi kartu fisik dan interaksi sentuh mesin 3D langsung.

---

## 2. Daftar 8 Kartu Marker Vuforia & Pemetaan 12 Model 3D Sci-Fi (v5.0)

Seluruh target marker terdaftar pada basis data Vuforia [GateWeaverDB.xml](Assets/StreamingAssets/Vuforia/GateWeaverDB.xml) dengan dimensi virtual **`0.800000 x 0.800000` unit Unity** (setara kartu cetak fisik `8 cm x 8 cm` pada skala meja AR). Setiap kartu duduk di atas landasan soket **`AR_BaseTile3D`** (`FloorTile_Basic.fbx`) dan bayangan kontak **`AR_ContactShadow`** agar menapak nyata di permukaan meja.

| No | Target Vuforia | Objek Scene | Peran (`NodeType`) | Kuota Input | Model 3D Quaternius (`Assets/Resources/Models/V5_SciFi/`) | Mekanisme & Interaksi v5.0 |
| :---: | :--- | :--- | :--- | :---: | :--- | :--- |
| 1 | `InputA` | `Card_Input_A` | `InputSource` | `0` | `Props_Computer.fbx` | Tuas sakelar fisik 3D (`-38°` LOW / `+38°` HIGH); ubah via sentuh 3D, rotasi kartu `~90°`, atau tombol `Btn_ToggleInputA` |
| 2 | `InputB` | `Card_Input_B` | `InputSource` | `0` | `Props_Computer.fbx` | Tuas sakelar fisik 3D (`-38°` LOW / `+38°` HIGH); ubah via sentuh 3D, rotasi kartu `~90°`, atau tombol `Btn_ToggleInputB` |
| 3 | `AND` | `Card_Gate_AND` | `AND` | `2` | `Props_ContainerFull.fbx` | 2 piston kompresor vertikal sinkron + percikan energi; sentuh 3D untuk inspeksi pin & fungsi |
| 4 | `OR` | `Card_Gate_OR` | `OR` | `2` | `Props_Teleporter_1.fbx` | Turbin energi berputar & kristal inti melayang; sentuh 3D untuk inspeksi pin & fungsi |
| 5 | `XOR` | `Card_Gate_XOR` | `XOR` | `2` | `Props_Teleporter_2.fbx` | 2 cincin giroskop berputar berlawanan arah; sentuh 3D untuk inspeksi pin & fungsi |
| 6 | `NAND` | `Card_Gate_NAND` | `NAND` | `2` | `Props_Vessel_Tall.fbx` | Koil energi menyapu naik-turun tabung & kubah pembalik; output `0` hanya jika `1,1` |
| 7 | `NOT` | `Card_Gate_NOT` | `NOT` | `1` | `Props_Laser.fbx` | Cincin lensa fokus & bilah pembalik fase berputar; membalik biner `0 <-> 1` |
| 8 | `Output` | `Card_Output` | `OutputActuator` | `1` | 5 Varian Diorama (`RoofTile_OrangeVent`, `Staircase`, `Door_Double`, `Props_Pod`, `Props_Capsule`) | Diorama reaktif + selebrasi percikan 3D saat lulus; sentuh 3D untuk uji rangkaian (Level) atau inspeksi rumus (Sandbox) |

### Kurasi 12 Aset FBX Terpilih (`Assets/Resources/Models/V5_SciFi/`)
Seluruh 12 model sci-fi modular dari **Quaternius (Ultimate Modular Sci-Fi Pack, CC0)** dikurasi ketat dengan total ukuran ~440 KB (tanpa tekstur eksternal berat, berbasis slot material solid PBR tersemat):
1. `FloorTile_Basic.fbx` — Landasan soket seragam (`AR_BaseTile3D`) di bawah seluruh 8 kartu.
2. `Props_Computer.fbx` — Konsol sakelar `Input A` & `Input B`.
3. `Props_ContainerFull.fbx` — Mesin gerbang `AND`.
4. `Props_Teleporter_1.fbx` — Mesin gerbang `OR`.
5. `Props_Teleporter_2.fbx` — Mesin gerbang `XOR`.
6. `Props_Vessel_Tall.fbx` — Mesin gerbang `NAND`.
7. `Props_Laser.fbx` — Mesin gerbang `NOT`.
8. `RoofTile_OrangeVent.fbx` — Diorama Output Varian `0` (Level 1 Alarm & Level 6 Sensor Hening NOR) dengan menara sirine & sorot cahaya berputar.
9. `Staircase.fbx` — Diorama Output Varian `1` (Level 2 Sakelar Tangga Dua Arah) dengan 3 strip LED anak tangga berurutan & lentera puncak.
10. `Door_Double.fbx` — Diorama Output Varian `2` (Level 3 Brankas & Level 7 Kunci Biometrik De Morgan) dengan daun pintu brankas geser & roda kunci berputar.
11. `Props_Pod.fbx` — Diorama Output Varian `3` (Level 4 Pemutus Darurat Reaktor) dengan cincin pendingin bersilangan.
12. `Props_Capsule.fbx` — Diorama Output Varian `4` (Level 5 Sinkronisasi Inti Kembar) dengan cincin orbital kapsul sinkron.

---

## 3. Kurikulum 7 Level Progresif & Mode Sandbox Bebas (Total 21 Bintang)

Seluruh skenario misi dikelola oleh `LevelAndUIManager` ([LevelAndUIManager.cs](Assets/Scripts/Levels/LevelAndUIManager.cs)) dengan penghargaan **3 bintang per level** (skor maksimal **21 bintang**) dan akses lompat instan melalui modal **`Panel_LevelSelector`** (`"Pilih Level"`):

| Level | Judul Misi | Deskripsi Misi | Rangkaian Solusi | Kuota Gerbang | Diorama Output (`v5.0`) | Tabel Kebenaran Target (`AB: 00, 01, 10, 11`) |
| :---: | :--- | :--- | :--- | :---: | :--- | :---: |
| **1** | Alarm Pintu & Jendela | Output = `1` jika `A` atau `B` bernilai `1` | `Y = (A OR B)` | Maks `1` kartu | `RoofTile_OrangeVent` + Sirine Berputar | `0, 1, 1, 1` |
| **2** | Sakelar Tangga Dua Arah | Output = `1` jika posisi `A` dan `B` berbeda | `Y = (A XOR B)` | Maks `1` kartu | `Staircase` + LED Tangga & Lentera | `0, 1, 1, 0` |
| **3** | Kunci Keamanan Brankas | Output = `1` hanya saat `A = 1` dan `B = 0` | `Y = (A AND (NOT B))` | Maks `2` kartu | `Door_Double` + Pintu Geser & Roda Kunci | `0, 0, 1, 0` |
| **4** | Pemutus Darurat Reaktor | Output = `0` hanya saat `A = 1` dan `B = 1` | `Y = (A NAND B)` | Maks `1` kartu | `Props_Pod` + Cincin Pendingin Reaktor | `1, 1, 1, 0` |
| **5** | Sinkronisasi Inti Kembar | Output = `1` saat `A` dan `B` bernilai **sama** (`XNOR`) | `Y = NOT(A XOR B)` | Maks `2` kartu | `Props_Capsule` + Cincin Orbital Inti | `1, 0, 0, 1` |
| **6** | Sensor Hening Ruang Server (NOR) | Output = `1` **hanya** saat `A = 0` dan `B = 0` | `Y = NOT(A OR B)` | Maks `2` kartu | `RoofTile_OrangeVent` + Sirine Sensor | `1, 0, 0, 0` |
| **7 (Boss)** | Kunci Biometrik De Morgan | Hukum De Morgan: Output = `0` **hanya** saat `A = 0` dan `B = 1` | `Y = (A OR (NOT B))` **atau** `Y = ((NOT A) NAND B)` | Maks `2` kartu | `Door_Double` + Brankas Biometrik | `1, 0, 1, 1` |
| **Sandbox** | Laboratorium Rangkaian Bebas | Eksplorasi bebas tanpa batas kuota & pindai 4 kombinasi logika | Rangkaian bebas | Bebas (`∞` / `Bebas`) | Menyesuaikan level terakhir | Pemindaian terbuka 4 baris (`00, 01, 10, 11`) |

---

## 4. Arsitektur Fitur Lengkap (v1.0 s.d. v5.0)

### A. Fondasi Rangkaian Spasial, Pedagogi, & Ergonomi UX (v1.0–v4.0)
1. **Algoritma Routing Spasial *Greedy Closest-Pair* ([`CircuitManager.cs`](Assets/Scripts/Core/CircuitManager.cs))**
   - Pengurutan kartu terlacak kiri-ke-kanan di ruang lokal kamera tanpa alokasi closure per frame (`ResolveMainCameraTransform()`, `CompareNodesByCameraX`), pemblokiran bypass `InputSource -> OutputActuator` saat ada gerbang aktif, jangkauan adaptif `maxSnapDistance * inputSourceRangeMultiplier` (`3.0f * 1.8f`), dan guard anti-siklus (`WouldCreateCycle`).
2. **Pemindai Tabel Kebenaran, Aljabar Boolean, Osiloskop, Audio PCM, & Rapor CP ([`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs))**
   - Evaluasi rumus Boolean waktu nyata (`GetCircuitBooleanExpression()` dengan cache hash topologi *zero-GC*), verifikasi otomatis 4 baris (`00..11`), 9 klip audio mono `44100 Hz` prosedural, haptik Android, *scaffolding* 3 tingkat (`Btn_ScaffoldingHint`), diagnostik miskonsepsi (`BuildMisconceptionDiagnostic()`), osiloskop ring-buffer 3 kanal x 16 pulsa, persistensi `PlayerPrefs` (`GW_v3_*`), dan modal **`Panel_CompetencyReport`** (`/21` bintang).
3. ***Direct 3D AR Raycast Touch*, Diet HUD Bertab/Lipat, *Sci-Fi Reskin*, & *Direct Level Selector* (v4.0)**
   - Interaksi sentuh langsung pada hitbox `AR_TouchHitbox3D` (`Physics.RaycastNonAlloc` zero-GC, **New Input System**, proteksi UI 2D & modal aktif) dengan animasi pantulan elastis `TriggerTapFeedback()`.
   - `Panel_TruthTableScanner` ringkas (`416 px` terbuka / `108 px` terlipat) dengan tab **`Tabel`**, **`Osiloskop`**, dan **`Lipat`/`Buka`**, *auto-expand* saat pengujian, tema sci-fi gelap-sian dengan *auto-sizing* TMP pada seluruh tombol HUD, serta tombol **`Pilih Level`** untuk modal **`Panel_LevelSelector`** (`2 x 4` kartu misi).

### B. Pembaruan Visual 3D Nyata & Kesiapan Spasial v6 (v5.0 — 4 Tahap)
4. **Pipeline 12 Model 3D Quaternius, *Auto-Bounds Normalization* (~2x Skala), *AR Grounding*, & *Material Sanitizer* (v5.0 Tahap 1 | [`GateNode.cs`](Assets/Scripts/Core/GateNode.cs))**
   - Memuat 12 aset FBX terkurasi dari `Assets/Resources/Models/V5_SciFi/` melalui `Resources.Load<GameObject>()` (serta mendukung override `customModelPrefab` per node dan fallback prosedural otomatis).
   - `NormalizeModelToFitFootprint()` menghitung *local mesh bounds* secara presisi sehingga setiap model duduk rata tepat di atas permukaan `AR_BaseTile3D` (`FloorTile_Basic`, tapak `0.56 x 0.038 x 0.56`) pada skala ~2x (`footprint ~0.46`, tinggi maksimal `~0.42`), lengkap dengan `AR_ContactShadow` (tekstur radial `64x64` statis bersama) dan `Label_Status3D` dinaikkan ke ketinggian nyaman (`0.48f`/`0.52f`) serta hitbox sentuh 3D yang diperbesar (`radius = 0.34f`, `Y = 0.20f`).
   - `SanitizeAndRegisterModelMaterials()` mengklon material FBX, me-remap shader tidak kompatibel ke `Standard`/`Mobile/Diffuse`/`Sprites/Default`, dan memetakan seluruh slot material bernama **`Accent`** agar menyala reaktif terhadap sinyal biner `0` (oranye-merah redup) dan `1` (hijau-sian terang).
5. **Mesin 3D Hidup & Reaktif + Pool Percikan 3D *Zero-GC* (v5.0 Tahap 2 | [`GateNode.cs`](Assets/Scripts/Core/GateNode.cs), [`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs))**
   - **Tuas Sakelar Fisik 3D (`AR_PhysicalLeverRig`)** di puncak konsol `Props_Computer` (`Input A`/`Input B`) berjungkit halus (`-38°` saat LOW `0` ke `+38°` saat HIGH `1`) dengan gagang & LED depan reaktif.
   - **5 Mekanisme Unik Gerbang Logika:** kompresor piston vertikal ganda (`AND`), turbin energi & kristal melayang (`OR`), cincin giroskop berlawanan arah (`XOR`), koil penyapu tabung & kubah pembalik (`NAND`), serta cincin lensa fokus berputar (`NOT`).
   - **5 Varian Diorama `Output` Reaktif:** menara sirine dengan sorot lampu berputar (`RoofTile_OrangeVent`), 3 strip LED anak tangga berurutan & lentera puncak (`Staircase`), daun pintu brankas geser & roda kunci (`Door_Double`), cincin pendingin bersilangan (`Props_Pod`), dan cincin orbital sinkron (`Props_Capsule`).
   - **`AR_SparkBurstPool` (12 Fragmen Kristal Pra-Alokasi Zero-GC):** memercikkan kilau energi 3D saat toggle/rising-edge serta memicu selebrasi multi-letupan **`TriggerVictoryCelebration()`** di `Card_Output` saat misi lulus.
6. **Saluran Energi Fisik Berlapis, 4 *Energy Bolt* Terarah Tangen, & *Connection Snap Lock VFX* (v5.0 Tahap 3 | [`GateNode.cs`](Assets/Scripts/Core/GateNode.cs))**
   - Menggunakan buffer pra-alokasi `bezierPointsCache[20]` (100% *zero-GC*) bersama untuk dua lapisan saluran yang menancap tepat dari `GetOutputPortWorldPosition()` ke `GetInputPortWorldPosition(slotIndex)` dengan lengkungan adaptif (`0.22f..0.34f`):
     - **Inti Plasma (`lineRenderer`):** inti tajam berdenyut (`0.015f` HIGH / `0.008f` LOW).
     - **Selubung Energi (`AR_ConduitOuterSheath`):** selubung luar transparan ~2.7x lebih tebal (`0.042f` HIGH / `0.022f` LOW).
   - **4 *Energy Bolt* Kristal (`EnergyBolt_0..3`):** meluncur searah tangen kurva (`EvaluateQuadraticBezierTangent()` + `Quaternion.LookRotation`).
   - ***Connection Snap Lock VFX* (Edge-Triggered):** saat koneksi baru atau perpindahan slot terbentuk, memicu *width surge* (`1.65x -> 1.0x` selama `0.36s`), kilat cincin `Port_Out` & `Port_InA`/`Port_InB`, serta percikan `AR_SparkBurstPool` tepat di kedua port fisik.
7. **Poles Pencahayaan *AR Studio Fill-Light*, Optimasi 60 FPS Android, & Audit Memori *Zero-GC* (v5.0 Tahap 4 | [`GateNode.cs`](Assets/Scripts/Core/GateNode.cs), [`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs))**
   - Mematikan *real-time shadow* berat GPU mobile (`ShadowCastingMode.Off`, `receiveShadows = false`) pada seluruh renderer FBX & rig prosedural karena kedalaman di meja AR telah ditangani `AR_ContactShadow`.
   - Penyetelan kontras material OpenGLES3 (`ApplyQuaterniusContrastPolish()`) meredam kilap plastik & metalik berlebih pada permukaan solid (`Main`/`DarkGrey`) di lingkungan kamera AR tanpa *reflection probe*, sembari mempertahankan ketajaman slot `Accent`.
   - `ConfigureARStudioLighting()` di `LevelAndUIManager.Start()` menyetel pencahayaan lingkungan **`AmbientMode.Trilight`** sejuk-seimbang dan menambahkan satu **`AR_RimFillLight`** (`LightType.Directional`, `LightShadows.None`, intensitas `0.42f`, warna putih-sian dari azimut berlawanan) bila scene hanya memiliki `<= 1` *Directional Light*.
   - Audit siklus hidup & *zero-GC* menyeluruh: seluruh material klon/prosedural/VFX tercatat di `managedMaterials` dan dibersihkan di `OnDestroy()`; aset statis bersama (`sharedTorusMesh`, `sharedDiamondSpireMesh`, `sharedContactShadowTexture`) dijaga *reference counter* `activeGateNodeCount`; `Camera.main`, status teks telemetri 3D (`lastRenderedTelemetryKey`), dan hash topologi rumus Boolean (`ComputeCircuitTopologyHash()`) di-cache sehingga loop `Update()` bebas alokasi GC per frame.
8. **Kesiapan Arsitektur v6.0 (*Spatial & Hand-Tracking Ready* | [`GateNode.cs`](Assets/Scripts/Core/GateNode.cs))**
   - Flag **`useExternalSpatialPose`** dan API publik **`SetExternalSpatialPose(bool tracked, Vector3 worldPosition, Quaternion worldRotation)`** memungkinkan sistem *markerless* / *hand-tracking* v6 mengendalikan posisi & pelacakan kartu tanpa konflik dengan event Vuforia `SetTrackingState()`.
   - *Anchor* port fisik standar **`Port_InA`**, **`Port_InB`**, dan **`Port_Out`** beserta API **`GetInputPortWorldPosition(int slotIndex)`** dan **`GetOutputPortWorldPosition()`** menyatukan titik sambungan kabel fisik lintas mode Vuforia maupun *spatial hand-tracking*.

---

## 5. Spesifikasi Teknis Build Android

Dikonfigurasi berdasarkan [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt), [manifest.json](Packages/manifest.json), dan [ProjectSettings.asset](ProjectSettings/ProjectSettings.asset):

| Parameter | Nilai Konfigurasi |
| :--- | :--- |
| **Unity Editor Version** | `6000.6.9f1` (`8c45277fb9c5`) |
| **AR SDK** | PTC Vuforia Engine `11.4.4` (`com.ptc.vuforia.engine`) |
| **Input System** | Unity New Input System `1.20.1` (`activeInputHandler: 1`, `ENABLE_INPUT_SYSTEM`) |
| **Application ID (Android)** | `com.GateWeaverStudio.GateWeaverAR` |
| **Version / Version Code** | **`5.0`** / **`5`** (`bundleVersion: 5.0`, `AndroidBundleVersionCode: 5`) |
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
│   ├── Resources/
│   │   └── Models/
│   │       └── V5_SciFi/
│   │           ├── Door_Double.fbx
│   │           ├── FloorTile_Basic.fbx
│   │           ├── Props_Capsule.fbx
│   │           ├── Props_Computer.fbx
│   │           ├── Props_ContainerFull.fbx
│   │           ├── Props_Laser.fbx
│   │           ├── Props_Pod.fbx
│   │           ├── Props_Teleporter_1.fbx
│   │           ├── Props_Teleporter_2.fbx
│   │           ├── Props_Vessel_Tall.fbx
│   │           ├── RoofTile_OrangeVent.fbx
│   │           └── Staircase.fbx
│   ├── Scenes/
│   │   └── SampleScene.unity
│   ├── Scripts/
│   │   ├── Core/
│   │   │   ├── CircuitManager.cs
│   │   │   └── GateNode.cs
│   │   └── Levels/
│   │       └── LevelAndUIManager.cs
│   └── StreamingAssets/
│       └── Vuforia/
│           └── GateWeaverDB.xml
├── Packages/
│   └── manifest.json
├── ProjectSettings/
│   ├── ProjectSettings.asset
│   └── ProjectVersion.txt
└── README.md
```

---

## 7. Pengaturan, Verifikasi *Play Mode*, & Build `GateWeaverAR_v5.apk`

1. Buka `Assets/Scenes/SampleScene.unity` di Unity Editor (`6000.6.9f1`).
2. Pastikan **`ARCamera`**, **`EventSystem`**, **`CircuitManager`** (`maxSnapDistance = 3`, `inputSourceRangeMultiplier = 1.8`), **`LevelAndUIManager`**, seluruh 8 kartu marker, dan Canvas HUD (`1920 x 1080`) aktif di Hierarchy.
3. Pastikan seluruh 12 file `.fbx` beserta `.meta` berada di `Assets/Resources/Models/V5_SciFi/`.
4. Uji di *Play Mode*:
   - Verifikasi seluruh 8 kartu menampilkan landasan `AR_BaseTile3D`, bayangan kontak `AR_ContactShadow`, dan model 3D Quaternius skala ~2x dengan pencahayaan `Trilight` + `AR_RimFillLight` yang kontras dan bebas artefak bayangan.
   - Sentuh/klik langsung `Card_Input_A` / `Card_Input_B` untuk memastikan tuas fisik 3D berjungkit `-38° <-> +38°` disertai percikan 3D `AR_SparkBurstPool`, dan warna slot `Accent` berubah tajam.
   - Dekatkan kartu gerbang (`AND`, `OR`, `XOR`, `NAND`, `NOT`) dan `Card_Output` untuk memverifikasi saluran energi berlapis (*core* + *outer sheath*), 4 *energy bolt* mengikuti tangen kurva, *Connection Snap Lock VFX* di `Port_Out -> Port_InA/B`, serta animasi mekanis masing-masing gerbang dan 5 diorama Output (termasuk pintu brankas geser Level 3/7 & selebrasi kemenangan).
   - Uji tab **`Tabel`** / **`Osiloskop`**, tombol **`Lipat`** / **`Buka`**, modal **`Pilih Level`** (Level 1–7 & Sandbox), serta modal **`Rapor CP`**.
5. Untuk build rilis **`GateWeaverAR_v5.apk`**: buka **File → Build Profiles → Android → Build**, lalu pastikan **`Version = 5.0`**, **`Bundle Version Code = 5`**, Scripting Backend **IL2CPP**, Target Architecture **ARM64**, Graphics API **OpenGLES3**, dan Minimum API Level **Android 10.0 (API 29)**.

---

## 8. Kredit Aset 3D & Lisensi

- **Model 3D Sci-Fi Modular (`Assets/Resources/Models/V5_SciFi/`):** *Ultimate Modular Sci-Fi Pack* oleh **Quaternius** — dirilis di bawah lisensi **CC0 1.0 Universal (Public Domain)**.
- **AR Tracking SDK:** PTC Vuforia Engine (`11.4.4`).
