# GateWeaver AR (v4.0 — Spatial AR UX & Ergonomics Edition)

**Media Pembelajaran Interaktif *Tangible Spatial Augmented Reality* untuk Rangkaian Gerbang Logika Kombinasional Berbasis Kurikulum Merdeka (Fase E)**

---

## 1. Deskripsi Proyek & Pemetaan Kurikulum Merdeka

**GateWeaver AR v4.0** adalah aplikasi pembelajaran interaktif berbasis **Tangible Spatial Augmented Reality** untuk perangkat Android yang memindahkan pusat interaksi langsung ke hologram 3D AR di atas meja, membebaskan bidang pandang kamera melalui antarmuka HUD bertab yang dapat dilipat (*collapsible*), serta menyediakan navigasi lompat level instan, telemetri pin I/O, osiloskop sinyal digital, bimbingan bertingkat (*scaffolding*), dan diagnostik miskonsepsi otomatis.

### Pemetaan Capaian Pembelajaran (CP)
- **Mata Pelajaran:** Informatika (Kurikulum Merdeka — SMA/SMK Kelas X Fase E, serta pengayaan SMP Fase D)
- **Elemen:** Sistem Komputer (**SK**) dan Berpikir Komputasional (**BK**)
- **Kompetensi Materi:**
  - Memahami fungsi, simbol, dan tabel kebenaran gerbang logika dasar maupun kombinasional (`AND`, `OR`, `NOT`, `XOR`, `NAND`, `NOR`, `XNOR`, serta ekuivalensi **Hukum De Morgan**).
  - Menerjemahkan spesifikasi permasalahan logika nyata menjadi ekspresi aljabar Boolean dan rangkaian kartu gerbang kombinasional.
  - Menganalisis perubahan sinyal waktu nyata menggunakan diagram pewaktuan digital (*timing diagram*) `A(t)`, `B(t)`, dan `Y(t)` serta mendiagnosis kesalahan sambungan atau polaritas rangkaian.
  - Mengoptimasi efisiensi rangkaian sesuai batas kuota komponen dan mengembangkan kecakapan abad ke-21 (**4C**: *Critical Thinking*, *Collaboration*, *Communication*, dan *Creativity*) melalui manipulasi fisik dan interaksi sentuh hologram 3D langsung.

---

## 2. Daftar 8 Kartu Marker Vuforia

Seluruh target marker terdaftar pada basis data Vuforia [GateWeaverDB.xml](Assets/StreamingAssets/Vuforia/GateWeaverDB.xml) dengan dimensi virtual **`0.800000 x 0.800000` unit Unity** (setara kartu cetak fisik `8 cm x 8 cm` pada skala meja AR):

| No | Nama Target Vuforia | Objek Scene | Peran (`NodeType`) | Kuota Input | Keterangan Interaksi |
| :--- | :--- | :--- | :--- | :---: | :--- |
| 1 | `InputA` | `Card_Input_A` | `InputSource` | `0` | Sumber sinyal `A` (`0`/`1`), diubah lewat sentuh langsung hologram 3D, rotasi kartu `~90°`, atau tombol `Btn_ToggleInputA` |
| 2 | `InputB` | `Card_Input_B` | `InputSource` | `0` | Sumber sinyal `B` (`0`/`1`), diubah lewat sentuh langsung hologram 3D, rotasi kartu `~90°`, atau tombol `Btn_ToggleInputB` |
| 3 | `AND` | `Card_Gate_AND` | `AND` | `2` | Output `1` hanya jika kedua input bernilai `1` (sentuh hologram 3D untuk inspeksi pin & fungsi) |
| 4 | `OR` | `Card_Gate_OR` | `OR` | `2` | Output `1` jika minimal salah satu input bernilai `1` (sentuh hologram 3D untuk inspeksi pin & fungsi) |
| 5 | `XOR` | `Card_Gate_XOR` | `XOR` | `2` | Output `1` jika kedua input bernilai berbeda (sentuh hologram 3D untuk inspeksi pin & fungsi) |
| 6 | `NAND` | `Card_Gate_NAND` | `NAND` | `2` | Output `0` hanya jika kedua input bernilai `1` (`NOT(A AND B)`) |
| 7 | `NOT` | `Card_Gate_NOT` | `NOT` | `1` | Pembalik fase logika biner (`0 -> 1`, `1 -> 0`) |
| 8 | `Output` | `Card_Output` | `OutputActuator` | `1` | Aktuator 3D akhir dengan 5 diorama kontekstual; sentuh hologram 3D untuk memicu uji rangkaian (Level) atau inspeksi rumus (Sandbox) |

---

## 3. Kurikulum 7 Level Progresif & Mode Sandbox Bebas (Total 21 Bintang)

Seluruh skenario misi dikelola oleh `LevelAndUIManager` ([LevelAndUIManager.cs](Assets/Scripts/Levels/LevelAndUIManager.cs)) dengan penghargaan **3 bintang per level** (skor maksimal **21 bintang**) dan dapat diakses langsung melalui modal **`Panel_LevelSelector`** (`"Pilih Level"`):

| Level | Judul Misi | Deskripsi Misi | Rangkaian Solusi | Kuota Gerbang | Tabel Kebenaran Target (`AB: 00, 01, 10, 11`) |
| :---: | :--- | :--- | :--- | :---: | :---: |
| **1** | Alarm Pintu & Jendela | Output = `1` jika `A` atau `B` bernilai `1` | `Y = (A OR B)` | Maks `1` kartu | `0, 1, 1, 1` |
| **2** | Sakelar Tangga Dua Arah | Output = `1` jika posisi `A` dan `B` berbeda | `Y = (A XOR B)` | Maks `1` kartu | `0, 1, 1, 0` |
| **3** | Kunci Keamanan Brankas | Output = `1` hanya saat `A = 1` dan `B = 0` | `Y = (A AND (NOT B))` | Maks `2` kartu | `0, 0, 1, 0` |
| **4** | Pemutus Darurat Reaktor | Output = `0` hanya saat `A = 1` dan `B = 1` | `Y = (A NAND B)` | Maks `1` kartu | `1, 1, 1, 0` |
| **5** | Sinkronisasi Inti Kembar | Output = `1` saat `A` dan `B` bernilai **sama** (`XNOR`) | `Y = NOT(A XOR B)` | Maks `2` kartu | `1, 0, 0, 1` |
| **6** | Sensor Hening Ruang Server (NOR) | Output = `1` **hanya** saat `A = 0` dan `B = 0` | `Y = NOT(A OR B)` | Maks `2` kartu | `1, 0, 0, 0` |
| **7 (Boss)** | Kunci Biometrik De Morgan | Hukum De Morgan: Output = `0` **hanya** saat `A = 0` dan `B = 1` | `Y = (A OR (NOT B))` **atau** `Y = ((NOT A) NAND B)` | Maks `2` kartu | `1, 0, 1, 1` |
| **Sandbox** | Laboratorium Rangkaian Bebas | Eksplorasi bebas tanpa batas kuota & pindai 4 kombinasi logika | Rangkaian bebas | Bebas (`∞` / `Bebas`) | Pemindaian terbuka 4 baris (`00, 01, 10, 11`) |

---

## 4. Arsitektur Fitur Lengkap (v1.0 s.d. v4.0)

### A. Fondasi Rangkaian Spasial & Visualisasi 3D (v1.0–v2.0)
1. **Algoritma Routing Spasial *Greedy Closest-Pair* ([`CircuitManager.cs`](Assets/Scripts/Core/CircuitManager.cs))**
   - Pengurutan kartu terlacak dari kiri ke kanan di ruang lokal kamera tanpa alokasi closure per frame (`ResolveMainCameraTransform()`, `CompareNodesByCameraX`).
   - Pemblokiran sambungan bypass `InputSource -> OutputActuator` saat masih ada kartu gerbang aktif (`activeGateCardsCount > 0`), jangkauan `Input -> Gate` (`maxSnapDistance * inputSourceRangeMultiplier`, default `3.0f * 1.8f`), *Gate-to-Gate* lalu *Gate-to-Output* (`maxSnapDistance = 3.0f`), dan guard anti-siklus (`WouldCreateCycle`).
   - Evaluasi sinyal multi-pass, *tracking loss debounce* (`0.4f`), dan input rotasi fisik `~90°` di [`GateNode.cs`](Assets/Scripts/Core/GateNode.cs).
2. ***3D Quadratic Bezier Energy Arc*, Totem Reaktor 3D, & 5 Varian Diorama ([`GateNode.cs`](Assets/Scripts/Core/GateNode.cs))**
   - Busur energi 20 segmen (`Sprites/Default`), 3 bola paket data mengalir, soket terminal, totem unik per gerbang dengan mesh bersama (`sharedTorusMesh`, `sharedDiamondSpireMesh`), dan 5 diorama aktuator pada `Card_Output`.
3. **Pemindai Tabel Kebenaran, Aljabar Boolean, & Synthesizer Audio PCM ([`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs))**
   - `GetCircuitBooleanExpression()` menampilkan rumus Boolean real-time, dipadukan dengan verifikasi otomatis 4 baris (`00..11`), 9 klip audio mono `44100 Hz` prosedural (`AudioClip.Create`), haptik Android, serta sistem skor 3 bintang per level.

### B. Ekspansi Pedagogi, Osiloskop, & Persistensi (v3.0)
4. **Telemetri Pin I/O 3D, Indikator Mikro Slot Input, & *Rising-Edge Shockwave Ring* ([`GateNode.cs`](Assets/Scripts/Core/GateNode.cs))**
   - `Label_Status3D` menampilkan status eksplisit ASCII aman (mis. `AND [1,0] -> 0`, `OR [1,?] -> 1`, `NOT [1] -> 0`), dilengkapi pin mikro 3D (`InputPin_0`, `InputPin_1`) dan cincin gelombang kejut `ShockwaveRing3D` pada transisi naik `0 -> 1`.
5. **Petunjuk *Scaffolding* 3 Tingkat, Mesin Diagnostik Miskonsepsi, & 7 Level Kurikulum ([`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs))**
   - `Btn_ScaffoldingHint` menyediakan 3 tingkatan petunjuk (*Konsep -> Komponen -> Struktur*, batas maksimal 2 bintang jika Tier 3 dibuka sebelum lulus pertama kali), `BuildMisconceptionDiagnostic()` menjelaskan penyebab kegagalan spesifik per baris, serta tambahan Level 6 (`NOR`) dan Level 7 (`De Morgan` dual solusi).
6. **Osiloskop Sinyal Digital `[A(t) | B(t) | Y(t)]`, Persistensi `PlayerPrefs`, & Rapor CP ([`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs))**
   - Sampling ring-buffer 3 kanal x 16 pulsa (`0.25s`), penyimpanan permanen progres/bintang/audio (`GW_v3_*`), serta modal **`Panel_CompetencyReport`** dengan ringkasan capaian `/21` bintang.

### C. Pembaruan Ergonomi AR & UX (v4.0 — 3 Tahap)
7. ***Direct 3D AR Raycast Touch Interaction* (v4.0 Tahap 1 | [`GateNode.cs`](Assets/Scripts/Core/GateNode.cs), [`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs))**
   - Setiap `GateNode` membangun satu hitbox trigger ringan `AR_TouchHitbox3D` (`SphereCollider`, `isTrigger = true`, `localPosition = (0, 0.08, 0)`, `radius = 0.26f`) yang otomatis aktif hanya ketika `isTracked == true`.
   - Deteksi tap layar Android dan klik mouse Editor memanfaatkan **New Input System** (`UnityEngine.InputSystem`, `#if ENABLE_INPUT_SYSTEM`), `Physics.RaycastNonAlloc` zero-GC (`touchRaycastHits[8]`), serta proteksi tumpukan UI 2D (`EventSystem.RaycastAll` dengan `uiRaycastResults(8)` & `cachedPointerEventData`) dan guard modal aktif.
   - Menyentuh hologram 3D memicu animasi pantulan skala elastis zero-GC (`TriggerTapFeedback()`, `0.18s`, puncak `1.18x`) pada kontainer visual tanpa drift:
     - **`Card_Input_A` / `Card_Input_B`:** langsung toggle nilai biner `0 <-> 1`.
     - **`Card_Output`:** memicu `OnVerifyButtonClicked()` di mode Level, atau menampilkan status rumus & output aktif di mode Sandbox.
     - **Kartu Gerbang (`AND`, `OR`, `XOR`, `NAND`, `NOT`):** menampilkan ringkasan fungsi & telemetri pin (`GetTouchInspectionSummary()`) di `Txt_Feedback`.
8. **Diet UI — *Tabbed & Collapsible Scanner HUD* + *Sci-Fi Dark-Cyan Reskin* (v4.0 Tahap 2 | [`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs))**
   - Tinggi `Panel_TruthTableScanner` saat terbuka dipangkas dari `536 px` menjadi **`416 px`** dengan bar navigasi 3 tombol:
     - **`Btn_TabTruthTable` (`"Tabel"`):** menampilkan matriks 4 baris (`00..11`) pada slot konten utama.
     - **`Btn_TabOscilloscope` (`"Osiloskop"`):** menukar slot konten yang sama ke `Panel_WaveformMonitor` berdimensi lega (`188 px`), sementara perekaman 16 pulsa sinyal tetap berjalan di latar belakang meski tab tidak aktif.
     - **`Btn_ToggleCollapseHUD` (`"Lipat"` / `"Buka"`):** melipat panel menjadi mode mini **`108 px`** yang hanya menyisakan header, tombol Audio/`Buka`, rumus Boolean, dan `Output Y`.
   - **Auto-Expand saat Verifikasi:** `EnsureTruthTableVisibleForVerification()` otomatis membuka panel yang terlipat dan beralih ke tab `"Tabel"` ketika pengujian 4 baris dimulai.
   - **`ApplySciFiHudButtonTheme()`:** menyelaraskan seluruh tombol putih bawaan Unity (`Btn_ToggleInputA`, `Btn_ToggleInputB`, `Btn_VerifyCircuit`, `Btn_NextLevel`, `Btn_ResetLevel`) ke tema gelap-sian dengan `Outline` `1.8 px`, `FontStyles.Bold`, `SciFiButtonColorBlock`, serta warna dinamis `LOW (0)` / `HIGH (1)` pada tombol Input A dan B.
9. ***Direct Level Selector* & Audit Siklus Hidup Memori v4.0 (v4.0 Tahap 3 | [`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs))**
   - Tombol **`Btn_OpenLevelSelector` (`"Pilih Level"`)** terpasang prosedural tepat di bawah `Btn_ResetLevel` (sejajar posisi X dan lebarnya) untuk membuka modal **`Panel_LevelSelector`** (`720 x 470 px`) bergrid `2 x 4` berisi akses lompat instan ke **Level 1–7** dan **Mode Sandbox Bebas**.
   - Setiap kartu misi menampilkan capaian bintang terkini dan sorotan level aktif, dilengkapi proteksi `isVerifying` (mencegah `isSimulationOverride` macet), *mutual exclusion* dengan `Panel_CompetencyReport`, guard raycast 3D, serta pelepasan seluruh listener tombol (`onClick.RemoveListener`) di `OnDestroy()`.

---

## 5. Spesifikasi Teknis Build Android

Dikonfigurasi berdasarkan [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt), [manifest.json](Packages/manifest.json), dan [ProjectSettings.asset](ProjectSettings/ProjectSettings.asset):

| Parameter | Nilai Konfigurasi |
| :--- | :--- |
| **Unity Editor Version** | `6000.6.9f1` (`8c45277fb9c5`) |
| **AR SDK** | PTC Vuforia Engine `11.4.4` (`com.ptc.vuforia.engine`) |
| **Input System** | Unity New Input System `1.20.1` (`activeInputHandler: 1`, `ENABLE_INPUT_SYSTEM`) |
| **Application ID (Android)** | `com.GateWeaverStudio.GateWeaverAR` |
| **Version / Version Code** | **`4.0`** / **`4`** (`bundleVersion: 4.0`, `AndroidBundleVersionCode: 4`) |
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

## 7. Pengaturan, Verifikasi *Play Mode*, & Build `GateWeaverAR_v4.apk`

1. Buka `Assets/Scenes/SampleScene.unity` di Unity Editor (`6000.6.9f1`).
2. Pastikan **`ARCamera`**, **`EventSystem`**, **`CircuitManager`** (`maxSnapDistance = 3`, `inputSourceRangeMultiplier = 1.8`), **`LevelAndUIManager`**, seluruh 8 kartu marker, dan Canvas HUD (`1920 x 1080`) aktif di Hierarchy.
3. Uji di *Play Mode*:
   - Klik/sentuh langsung hologram 3D **`Card_Input_A`** / **`Card_Input_B`** (toggle `0 <-> 1` + pantulan elastis), kartu gerbang logika (ringkasan inspeksi di `Txt_Feedback`), serta **`Card_Output`** (uji rangkaian / inspeksi Sandbox).
   - Uji tab **`Tabel`** vs **`Osiloskop`**, tombol **`Lipat`** / **`Buka`**, *auto-expand* saat menekan **`Uji Rangkaian`**, serta tema gelap-sian pada seluruh tombol HUD.
   - Tekan **`Pilih Level`** di bawah **`Reset Level`** untuk melompat langsung ke Level 1–7 maupun **Mode Sandbox Bebas**, lalu pastikan modal tidak bertumpuk dengan **`Rapor CP`** dan tidak dapat memutus uji rangkaian yang sedang berjalan.
4. Untuk build rilis **`GateWeaverAR_v4.apk`**: buka **File → Build Profiles → Android → Build**, lalu pastikan **`Version = 4.0`**, **`Bundle Version Code = 4`**, Scripting Backend **IL2CPP**, Target Architecture **ARM64**, Graphics API **OpenGLES3**, dan Minimum API Level **Android 10.0 (API 29)**.
