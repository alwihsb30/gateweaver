# GateWeaver AR (v3.0)

**Media Pembelajaran Interaktif *Tangible Spatial Augmented Reality* untuk Rangkaian Gerbang Logika Kombinasional Berbasis Kurikulum Merdeka (Fase E)**

---

## 1. Deskripsi Proyek & Pemetaan Kurikulum Merdeka

**GateWeaver AR v3.0** adalah aplikasi pembelajaran interaktif berbasis **Tangible Spatial Augmented Reality** untuk perangkat Android yang memungkinkan peserta didik menyusun kartu marker fisik gerbang logika secara langsung di atas meja, memantau telemetri pin I/O serta osiloskop gelombang sinyal digital biner (`0` / `1`) secara *real-time* dalam ruang 3D, memperoleh bimbingan bertingkat (*scaffolding*) dan diagnostik miskonsepsi otomatis, serta memverifikasi tabel kebenaran rangkaian secara menyeluruh.

### Pemetaan Capaian Pembelajaran (CP)
- **Mata Pelajaran:** Informatika (Kurikulum Merdeka — SMA/SMK Kelas X Fase E, serta pengayaan SMP Fase D)
- **Elemen:** Sistem Komputer (**SK**) dan Berpikir Komputasional (**BK**)
- **Kompetensi Materi:**
  - Memahami fungsi, simbol, dan tabel kebenaran gerbang logika dasar maupun kombinasional (`AND`, `OR`, `NOT`, `XOR`, `NAND`, `NOR`, `XNOR`, serta ekuivalensi **Hukum De Morgan**).
  - Menerjemahkan spesifikasi permasalahan logika nyata menjadi ekspresi aljabar Boolean dan rangkaian kartu gerbang kombinasional.
  - Menganalisis perubahan sinyal waktu nyata menggunakan diagram pewaktuan digital (*timing diagram*) `A(t)`, `B(t)`, dan `Y(t)` serta mendiagnosis kesalahan sambungan atau polaritas rangkaian.
  - Mengoptimasi efisiensi rangkaian sesuai batas kuota komponen dan mengembangkan kecakapan abad ke-21 (**4C**: *Critical Thinking*, *Collaboration*, *Communication*, dan *Creativity*) melalui eksperimen kolaboratif berbasis kartu fisik.

---

## 2. Daftar 8 Kartu Marker Vuforia

Seluruh target marker terdaftar pada basis data Vuforia [GateWeaverDB.xml](Assets/StreamingAssets/Vuforia/GateWeaverDB.xml) dengan dimensi virtual **`0.800000 x 0.800000` unit Unity** (setara kartu cetak fisik `8 cm x 8 cm` pada skala meja AR):

| No | Nama Target Vuforia | Objek Scene | Peran (`NodeType`) | Kuota Input | Keterangan Interaksi |
| :--- | :--- | :--- | :--- | :---: | :--- |
| 1 | `InputA` | `Card_Input_A` | `InputSource` | `0` | Sumber sinyal `A` (`0`/`1`), dapat diubah dengan memutar kartu fisik `~90°` atau menekan `Btn_ToggleInputA` |
| 2 | `InputB` | `Card_Input_B` | `InputSource` | `0` | Sumber sinyal `B` (`0`/`1`), dapat diubah dengan memutar kartu fisik `~90°` atau menekan `Btn_ToggleInputB` |
| 3 | `AND` | `Card_Gate_AND` | `AND` | `2` | Output `1` hanya jika kedua input bernilai `1` |
| 4 | `OR` | `Card_Gate_OR` | `OR` | `2` | Output `1` jika minimal salah satu input bernilai `1` |
| 5 | `XOR` | `Card_Gate_XOR` | `XOR` | `2` | Output `1` jika kedua input bernilai berbeda |
| 6 | `NAND` | `Card_Gate_NAND` | `NAND` | `2` | Output `0` hanya jika kedua input bernilai `1` (`NOT(A AND B)`) |
| 7 | `NOT` | `Card_Gate_NOT` | `NOT` | `1` | Pembalik fase logika biner (`0 -> 1`, `1 -> 0`) |
| 8 | `Output` | `Card_Output` | `OutputActuator` | `1` | Aktuator 3D akhir penerima hasil evaluasi yang menampilkan diorama kontekstual sesuai misi level |

---

## 3. Kurikulum 7 Level Progresif & Mode Sandbox Bebas (Total 21 Bintang)

Seluruh skenario misi dikelola oleh `LevelAndUIManager` ([LevelAndUIManager.cs](Assets/Scripts/Levels/LevelAndUIManager.cs)) dengan sistem penghargaan **3 bintang per level** (skor maksimal **21 bintang** untuk 7 level):

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

## 4. Arsitektur Fitur Lengkap (v1.0 s.d. v3.0)

### A. Fondasi Rangkaian Spasial & Visualisasi 3D (v1.0–v2.0 | Hari ke-1 s.d. Hari ke-11)
1. **Algoritma Routing Spasial *Greedy Closest-Pair* ([`CircuitManager.cs`](Assets/Scripts/Core/CircuitManager.cs))**
   - Mengurutkan kartu terlacak dari kiri ke kanan di ruang lokal kamera utama tanpa alokasi closure per frame.
   - Memblokir sambungan langsung `InputSource -> OutputActuator` selama masih ada kartu gerbang logika aktif (`activeGateCardsCount > 0`).
   - Memisahkan fase sambungan `Input -> Gate` (jangkauan `maxSnapDistance * inputSourceRangeMultiplier`, default `3.0f * 1.8f`) serta fase *Gate-to-Gate* lalu *Gate-to-Output* (`maxSnapDistance = 3.0f`) dengan guard anti-siklus (`WouldCreateCycle`) untuk mendukung susunan zig-zag hingga 5 kartu.
   - Evaluasi sinyal multi-pass stabil, *tracking loss debounce* (`trackingLossDebounceTime = 0.4f`), dan toggle input via rotasi kartu fisik `~90°` (`rotationAngleThreshold = 45f`) di [`GateNode.cs`](Assets/Scripts/Core/GateNode.cs).
2. ***3D Quadratic Bezier Energy Arc*, Paket Data Mengalir, & Soket Terminal ([`GateNode.cs`](Assets/Scripts/Core/GateNode.cs))**
   - Busur energi 3D bersegmen 20 titik (`LineRenderer`, shader `Sprites/Default`), 3 bola paket data yang bergerak sepanjang kurva `P0 -> P1 -> P2` sesuai kondisi sinyal `HIGH (1)` / `LOW (0)`, dan cakram soket terminal tanpa `Collider`.
3. **Totem Reaktor Hologram 3D & 5 Varian Diorama Aktuator ([`GateNode.cs`](Assets/Scripts/Core/GateNode.cs))**
   - Totem 3D unik per jenis kartu menggunakan mesh bersama berbasis reference count (`sharedTorusMesh`, `sharedDiamondSpireMesh`) agar hemat memori.
   - 5 varian diorama kontekstual pada `Card_Output` (*Emergency Rotary Siren Tower*, *Twin Staircase Lantern*, *Mechanical Vault Door*, *Reactor Containment Core*, dan *Twin Sync Cyber Obelisk*) yang dipetakan hingga Level 7 via `ResolveDioramaVariantForLevel`.
4. **Pemindai Tabel Kebenaran *Real-Time* & Generator Aljabar Boolean ([`CircuitManager.cs`](Assets/Scripts/Core/CircuitManager.cs), [`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs))**
   - [`GetCircuitBooleanExpression()`](Assets/Scripts/Core/CircuitManager.cs) menelusuri pohon koneksi mundur dari `Card_Output` menghasilkan ekspresi real-time (mis. `Y = (A AND (NOT B))`, `Y = ((NOT A) NAND B)`, atau `Y = (Belum Terhubung)`).
   - Panel non-blocking `Panel_TruthTableScanner` menampilkan rumus rakitan, status `Output Y`, sorotan baris aktif manual, dan animasi pindai sekuensial baris `00..11`.
5. **Synthesizer Audio Prosedural PCM, Haptik Android, & Rating Bintang ([`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs))**
   - Menghasilkan 9 klip audio mono `44100 Hz` secara prosedural via `AudioClip.Create` (toggle input, snap koneksi, nada pindai baris `00..11`, pass/fail, dan arpeggio fanfare kemenangan) dengan kontrol `Btn_AudioToggle` (`Audio: ON` / `Audio: OFF`) serta getar haptik (`Handheld.Vibrate()` pada build Android).

### B. Ekspansi GateWeaver AR v3.0 (Hari ke-12 s.d. Hari ke-17)
6. **Telemetri Pin Input-Output 3D, Indikator Mikro Slot Input, & *Rising-Edge Shockwave Ring* (Hari ke-12 | [`GateNode.cs`](Assets/Scripts/Core/GateNode.cs))**
   - `Label_Status3D` menampilkan status pin masuk dan keluaran eksplisit berkarakter ASCII aman untuk `LiberationSans SDF` (contoh: `AND [1,0] -> 0`, `OR [1,?] -> 1`, `NOT [1] -> 0`, dan `NAND [?,?] -> 1`).
   - Indikator mikro fisik 3D (`InputPin_0`, `InputPin_1`) di sisi kiri kartu gerbang dan `OutputActuator` menyala sesuai kondisi terhubung `HIGH (1)`, `LOW (0)`, atau belum terhubung (`?`).
   - Transisi logika keluaran `0 -> 1` (*rising edge*) memicu gelombang kejut torus meluas `ShockwaveRing3D` di atas kartu.
7. ***3-Tier Pedagogical Scaffolding Hint System* & Mesin Diagnostik Miskonsepsi (Hari ke-13 | [`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs))**
   - Tombol interaktif `Btn_ScaffoldingHint` menyediakan petunjuk bertahap per level (`Petunjuk (0/3)` -> **Tier 1 Konsep** -> **Tier 2 Komponen** -> **Tier 3 Struktur**), dengan aturan batas maksimal `2` bintang jika membuka Tier 3 sebelum level diselesaikan pertama kali.
   - `BuildMisconceptionDiagnostic()` menganalisis penyebab spesifik kegagalan verifikasi tabel kebenaran (kartu `NOT` belum terpasang, gerbang terbalik mis. `AND` vs `OR` / `XOR` vs `NAND`, sinyal konstan, atau polaritas terbalik pada hukum De Morgan) langsung pada `Txt_Feedback`.
8. **Ekspansi Kurikulum 7 Level — Kombinasi `NOR` & *De Morgan Boss* (Hari ke-14 | [`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs))**
   - Penambahan **Level 6 (Sensor Hening Ruang Server — NOR)** dan **Level 7 (Kunci Biometrik De Morgan)** beserta dukungan ekuivalensi dua jalur solusi sah (`A OR (NOT B)` dan `(NOT A) NAND B`), petunjuk 3 tingkat, serta pemetaan diorama otomatis.
9. **Osiloskop Gelombang Sinyal Digital *Real-Time* `[A(t) | B(t) | Y(t)]` (Hari ke-15 | [`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs))**
   - Seksi `Panel_WaveformMonitor` di bawah `Btn_ScaffoldingHint` menyampel kondisi sinyal setiap perubahan logika maupun interval `0.25 detik` (`WaveformSampleIntervalSeconds`) ke 3 kanal x 16 kolom bar gelombang kotak (`48` elemen teralokasi statis).
10. **Penyimpanan Progres Permanen (`PlayerPrefs`) & Modal Laporan Kompetensi Interaktif (Hari ke-16 | [`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs))**
    - Progres penyelesaian tiap level, perolehan bintang terbaik (`GW_v3_LevelCleared_`, `GW_v3_LevelStars_`, `GW_v3_LastUnlockedLevel`), dan preferensi suara (`GW_v3_AudioMuted`) tersimpan otomatis dan dipulihkan pada sesi berikutnya.
    - Tombol `Btn_OpenCompetencyReport` membuka modal `Panel_CompetencyReport` berisi tabel capaian Level 1–7, skor kumulatif `/21` bintang, predikat kompetensi Informatika, akses cepat ke **Mode Sandbox (`Btn_ReportSandbox`)**, serta tombol **Reset Progres (`Btn_ReportReset`)**.
11. **Audit Siklus Hidup Memori v3.0, Optimasi *Zero-GC*, & Konfigurasi Rilis Android `3.0` (Hari ke-17)**
    - `GateNode.OnDestroy()` menghancurkan seluruh material prosedural termasuk `shockwaveMaterial` dan `inputPinMaterials[]`, serta membebaskan `sharedTorusMesh` dan `sharedDiamondSpireMesh` saat tidak ada node aktif tersisa.
    - `LevelAndUIManager.OnDestroy()` dan `OnApplicationPause()` menjaga konsistensi `PlayerPrefs.Save()`, melepas seluruh listener tombol (`hintButton`, `reportOpenButton`, `reportCloseButton`, `reportSandboxButton`, `reportResetButton`, `audioToggleButton`), menghentikan coroutine, membersihkan klip audio prosedural (`DestroyProceduralAudioClips()`), serta mengembalikan `Screen.sleepTimeout = SleepTimeout.SystemSetting`.
    - Penghapusan alokasi closure, cache kamera utama (`ResolveMainCameraTransform()`), dan pembaruan UI HUD berbasis perubahan status mencegah alokasi sampah memori berlebihan tiap frame pada perangkat Android `60 FPS`.

---

## 5. Spesifikasi Teknis Build Android

Dikonfigurasi berdasarkan [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt), [manifest.json](Packages/manifest.json), dan [ProjectSettings.asset](ProjectSettings/ProjectSettings.asset):

| Parameter | Nilai Konfigurasi |
| :--- | :--- |
| **Unity Editor Version** | `6000.6.9f1` (`8c45277fb9c5`) |
| **AR SDK** | PTC Vuforia Engine `11.4.4` (`com.ptc.vuforia.engine`) |
| **Application ID (Android)** | `com.GateWeaverStudio.GateWeaverAR` |
| **Version / Version Code** | **`3.0`** / **`3`** (`bundleVersion: 3.0`, `AndroidBundleVersionCode: 3`) |
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

## 7. Pengaturan & Verifikasi di Unity Editor

1. Buka `Assets/Scenes/SampleScene.unity`.
2. Pastikan **`ARCamera`**, **`CircuitManager`**, **`LevelAndUIManager`**, seluruh 8 kartu (`Card_Input_A`, `Card_Input_B`, `Card_Gate_AND`, `Card_Gate_OR`, `Card_Gate_XOR`, `Card_Gate_NAND`, `Card_Gate_NOT`, `Card_Output`), serta Canvas HUD (`1920 x 1080`) aktif di Hierarchy.
3. Pada komponen **`CircuitManager`**, pastikan parameter `maxSnapDistance = 3` dan `inputSourceRangeMultiplier = 1.8`.
4. Uji coba di Unity Editor (*Play Mode*) untuk memverifikasi ke-7 level + Mode Sandbox, telemetri pin `Label_Status3D`, osiloskop `[A(t) | B(t) | Y(t)]`, petunjuk 3 tingkat, diagnostik miskonsepsi, serta tombol modal **Laporan Kompetensi**.
5. Untuk build rilis **`GateWeaverAR_v3.apk`**: buka **File → Build Profiles → Android → Build**, pastikan `Version` terbaca **`3.0`**, `Bundle Version Code` bernilai **`3`**, arsitektur **ARM64**, Scripting Backend **IL2CPP**, dan Minimum API Level **Android 10.0 (API 29)**.
