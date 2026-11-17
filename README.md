# GateWeaver AR

**Media Pembelajaran Interaktif *Tangible Spatial Augmented Reality* untuk Rangkaian Gerbang Logika Kombinasional Berbasis Kurikulum Merdeka (Fase E)**

---

## 1. Deskripsi Proyek & Pemetaan Kurikulum Merdeka

**GateWeaver AR** adalah aplikasi pembelajaran interaktif berbasis **Tangible Spatial Augmented Reality** untuk Android yang memungkinkan peserta didik menyusun kartu marker fisik gerbang logika secara langsung di atas meja, mengamati perambatan sinyal biner (`0` / `1`) dalam bentuk visual 3D real-time, serta memverifikasi fungsi rangkaian terhadap tabel kebenaran secara otomatis.

### Pemetaan Capaian Pembelajaran (CP)
- **Mata Pelajaran:** Informatika (Kurikulum Merdeka — SMA/SMK Kelas X Fase E, serta pengayaan SMP Fase D)
- **Elemen:** Sistem Komputer (**SK**) dan Berpikir Komputasional (**BK**)
- **Kompetensi Materi:**
  - Memahami fungsi dan tabel kebenaran gerbang logika dasar & kombinasi (`AND`, `OR`, `NOT`, `XOR`, `NAND`, serta komposisi `XNOR`).
  - Menerjemahkan spesifikasi permasalahan logika nyata menjadi ekspresi aljabar Boolean dan rangkaian gerbang logika kombinasional.
  - Mengoptimasi efisiensi rangkaian agar memenuhi target logika dengan jumlah komponen seminimal mungkin.
  - Mengembangkan kecakapan abad ke-21 (**4C**: *Critical Thinking*, *Collaboration*, *Communication*, dan *Creativity*) melalui manipulasi kartu spasial tanpa kuis pilihan ganda statis.

---

## 2. Daftar 8 Kartu Marker Vuforia

Seluruh marker terdaftar di basis data Vuforia [GateWeaverDB.xml](Assets/StreamingAssets/Vuforia/GateWeaverDB.xml) dengan dimensi fisik virtual **`0.800000 x 0.800000` unit Unity** (representasi fisik `8 cm x 8 cm` di dunia nyata pada skala meja AR):

| No | Nama Target Vuforia | Objek Scene | Peran (`NodeType`) | Kuota Input | Keterangan Interaksi |
| :--- | :--- | :--- | :--- | :---: | :--- |
| 1 | `InputA` | `Card_Input_A` | `InputSource` | `0` | Sumber sinyal `A` (`0`/`1`), dapat diubah dengan memutar kartu fisik `~90°` atau tombol HUD `Btn_ToggleInputA` |
| 2 | `InputB` | `Card_Input_B` | `InputSource` | `0` | Sumber sinyal `B` (`0`/`1`), dapat diubah dengan memutar kartu fisik `~90°` atau tombol HUD `Btn_ToggleInputB` |
| 3 | `AND` | `Card_Gate_AND` | `AND` | `2` | Output `1` hanya jika kedua input bernilai `1` |
| 4 | `OR` | `Card_Gate_OR` | `OR` | `2` | Output `1` jika minimal satu input bernilai `1` |
| 5 | `XOR` | `Card_Gate_XOR` | `XOR` | `2` | Output `1` jika kedua input bernilai berbeda |
| 6 | `NAND` | `Card_Gate_NAND` | `NAND` | `2` | Output `0` hanya jika kedua input bernilai `1` (`NOT(A AND B)`) |
| 7 | `NOT` | `Card_Gate_NOT` | `NOT` | `1` | Pembalik fase logika biner (`0 -> 1`, `1 -> 0`) |
| 8 | `Output` | `Card_Output` | `OutputActuator` | `1` | Aktuator 3D akhir yang menerima hasil evaluasi dan menampilkan diorama kontekstual sesuai level |

---

## 3. Kurikulum 5 Level Progresif & Mode Sandbox Bebas

Didefinisikan dan dikelola oleh `LevelAndUIManager` ([LevelAndUIManager.cs](Assets/Scripts/Levels/LevelAndUIManager.cs)):

| Level | Judul Misi | Deskripsi Misi | Rangkaian Solusi | Kuota Gerbang | Tabel Kebenaran Target (`AB: 00, 01, 10, 11`) |
| :---: | :--- | :--- | :--- | :---: | :---: |
| **1** | Alarm Pintu & Jendela | Output = `1` jika A atau B bernilai `1` | `Y = (A OR B)` | Maks `1` kartu | `0, 1, 1, 1` |
| **2** | Sakelar Tangga Dua Arah | Output = `1` jika posisi A dan B berbeda | `Y = (A XOR B)` | Maks `1` kartu | `0, 1, 1, 0` |
| **3** | Kunci Keamanan Brankas | Output = `1` hanya saat `A = 1` dan `B = 0` | `Y = (A AND (NOT B))` | Maks `2` kartu | `0, 0, 1, 0` |
| **4** | Pemutus Darurat Reaktor | Output = `0` hanya saat `A = 1` dan `B = 1` | `Y = (A NAND B)` | Maks `1` kartu | `1, 1, 1, 0` |
| **5 (Boss)** | Sinkronisasi Inti Kembar | Output = `1` saat A dan B bernilai **sama** | `Y = NOT(A XOR B)` | Maks `2` kartu | `1, 0, 0, 1` |
| **Sandbox** | Laboratorium Rangkaian Bebas | Eksplorasi rangkaian bebas tanpa batas kuota & memindai 4 kombinasi output | Rangkaian bebas | Bebas (`Unlimited`) | Pemindaian terbuka 4 baris (`00, 01, 10, 11`) |

---

## 4. Arsitektur Fitur Unggulan (Implementasi Hari ke-1 s.d. Hari ke-17)

1. **Algoritma Routing Spasial *Greedy Closest-Pair* ([`CircuitManager.cs`](Assets/Scripts/Core/CircuitManager.cs))**
   - Mengurutkan kartu terlacak dari kiri ke kanan di ruang lokal kamera utama (`Camera.main.transform.InverseTransformPoint`).
   - Memblokir sambungan langsung `InputSource -> OutputActuator` selama masih ada kartu gerbang logika aktif (`activeGateCardsCount > 0`).
   - Memisahkan fase sambungan `Input -> Gate` (jangkauan `maxSnapDistance * inputSourceRangeMultiplier`, default `3.0f * 1.8f`) dan fase bertahap *Gate-to-Gate* lalu *Gate-to-Output* (`maxSnapDistance = 3.0f`) untuk mendukung susunan kartu zig-zag 5 kartu pada layar smartphone tanpa salah sambung maupun siklus (`WouldCreateCycle`).
   - Evaluasi sinyal multi-pass hingga stabil dalam satu frame serta mekanisme *tracking loss debounce* (`trackingLossDebounceTime = 0.4f`) dan input rotasi fisik `~90°` (`rotationAngleThreshold = 45f`) pada [`GateNode.cs`](Assets/Scripts/Core/GateNode.cs).

2. ***3D Quadratic Bezier Energy Arc*, *Flowing Data Packets*, & *Terminal Socket Rings* ([`GateNode.cs`](Assets/Scripts/Core/GateNode.cs))**
   - Menggambar busur energi 3D bersegmen 20 titik (`LineRenderer`, shader aman `Sprites/Default`) dengan titik kontrol `P1` terangkat sepanjang normal permukaan rata-rata kedua kartu.
   - Mengalirkan 3 bola paket data di sepanjang kurva dari `P0 -> P1 -> P2` dengan kecepatan, ukuran, dan warna berbeda untuk sinyal `HIGH (1)` dan `LOW (0)`, lengkap dengan cakram soket terminal di titik pangkal dan ujung tanpa komponen `Collider` (nol beban fisika).

3. **Totem Reaktor Hologram 3D Prosedural ([`GateNode.cs`](Assets/Scripts/Core/GateNode.cs))**
   - Setiap kartu `InputSource` dan `LogicGate` memiliki model totem 3D unik bebas `Collider` dengan mesh bersama (`sharedTorusMesh`, `sharedDiamondSpireMesh`):
     - `InputSource`: inti kubus berlian miring `45°` dan satelit orbit horizontal.
     - `AND`: menara kembar konvergensi dan bola inti pusat.
     - `OR`: *wide halo collector* bersayap dengan dua sabuk cincin miring.
     - `XOR`: *counter-rotating dual gyro* mengelilingi kristal inti tengah.
     - `NAND`: dua pilar energi dengan kubah *inverter cap* di bagian puncak.
     - `NOT`: *phase-inverter diamond spire* dengan cincin pembalik fase harmonik.
   - Dilengkapi teks 3D *world-space* `Label_Status3D` berorientasi *billboard* menghadap kamera.

4. **5 Varian Diorama Aktuator 3D Kontekstual pada `Card_Output` ([`GateNode.cs`](Assets/Scripts/Core/GateNode.cs))**
   - **Diorama 0 (Level 1 — *Emergency Rotary Siren Tower*):** menara sirine dengan bilah reflektor berputar `360°/detik` dan cincin suar berdenyut saat `Y = 1`.
   - **Diorama 1 (Level 2 — *Twin Staircase Lantern*):** miniatur tangga 3 undakan dengan lentera ujung bawah & atas plus cincin halo berputar selaras.
   - **Diorama 2 (Level 3 — *Mechanical Vault Door*):** brankas dengan daun pintu yang berayun terbuka hingga `75°`, roda kombinasi berputar, dan kristal berlian di dalamnya.
   - **Diorama 3 (Level 4 — *Reactor Containment Core*):** dua cincin pengungkung bersilang dan inti reaktor energi yang melambat ke mode aman saat pemutus darurat aktif (`Y = 0`).
   - **Diorama 4 (Level 5 & Sandbox — *Twin Sync Cyber Obelisk*):** menara kristal kembar kiri-kanan dan cincin halo sinkronisasi.

5. ***Live Truth Table Scanner Matrix UI* & *Symbolic Boolean Expression Generator* ([`CircuitManager.cs`](Assets/Scripts/Core/CircuitManager.cs), [`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs))**
   - [`GetCircuitBooleanExpression()`](Assets/Scripts/Core/CircuitManager.cs) menelusuri pohon koneksi mundur dari `Card_Output` hingga sumber input dengan guard anti-siklus, menghasilkan rumus real-time (mis. `Y = (A AND (NOT B))`, `Y = (A AND ?)`, atau `Y = (Belum Terhubung)`).
   - Panel prosedural non-blocking `Panel_TruthTableScanner` menampilkan rumus aktif, readout `Output Y`, sorotan baris aktif manual (`AB`), animasi pindai beruntun baris `00..11` saat menekan **`Uji Rangkaian`**, serta karakter ASCII aman untuk atlas statis `LiberationSans SDF`.

6. **Synthesizer Audio Sci-Fi Prosedural PCM, Android Haptics, & Sistem Rating 3 Bintang ([`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs))**
   - Membentuk 9 klip audio mono `44100 Hz` saat runtime menggunakan `AudioClip.Create` dan *raised-cosine fade envelope* tanpa aset eksternal: klik dual-tone toggle input (`680 -> 920 Hz`), sweep snap koneksi (`420 -> 840 Hz`), nada pindai baris bertingkat (`520 / 620 / 720 / 820 Hz`), nada lolos (`1046 Hz`) vs dengung gagal (`190 Hz`), serta arpeggio fanfare kemenangan (`C5-E5-G5-C6`), disertai tombol **`Audio: ON` / `Audio: OFF`**.
   - Umpan balik getar Android (`Handheld.Vibrate()` pada `#if UNITY_ANDROID && !UNITY_EDITOR`) dengan jeda *cooldown* `0.85s` ketika rangkaian baru terbentuk utuh atau saat lolos verifikasi.
   - Evaluasi efisiensi **3 bintang** per level (`[***]` lolos sesuai kuota pada uji ke-`1`/`2`; `[**.]` pada uji ke-`3`/`4`; `[*..]` pada uji ke-`5+`) dengan total skor `/15` pada footer `Row_StarMastery`.

7. **Optimasi Mobile Android & Manajemen Siklus Hidup Memori ([`LevelAndUIManager.cs`](Assets/Scripts/Levels/LevelAndUIManager.cs), [`GateNode.cs`](Assets/Scripts/Core/GateNode.cs))**
   - Penguncian target `Application.targetFrameRate = 60` dan `Screen.sleepTimeout = SleepTimeout.NeverSleep` di `LevelAndUIManager.Awake()`, serta pengembalian `Screen.sleepTimeout = SleepTimeout.SystemSetting` di `OnDestroy()`.
   - Penghancuran eksplisit seluruh `Material` instansiasi, `Mesh` prosedural statis berbasis penghitung instans (`sharedTorusMesh`, `sharedDiamondSpireMesh`), listener UI, coroutine aktif, dan `AudioClip` prosedural di `OnDestroy()` untuk mencegah kebocoran memori lintas level maupun saat aplikasi ditutup.

---

## 5. Spesifikasi Teknis Build Android

Dikonfigurasi berdasarkan [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt), [manifest.json](Packages/manifest.json), dan [ProjectSettings.asset](ProjectSettings/ProjectSettings.asset):

| Parameter | Nilai Konfigurasi |
| :--- | :--- |
| **Unity Editor Version** | `6000.6.9f1` (`8c45277fb9c5`) |
| **AR SDK** | PTC Vuforia Engine `11.4.4` (`com.ptc.vuforia.engine`) |
| **Application ID (Android)** | `com.GateWeaverStudio.GateWeaverAR` |
| **Minimum API Level** | Android `10.0` (`API Level 29`) |
| **Scripting Backend** | `IL2CPP` |
| **Target Architecture** | `ARM64` (`AndroidTargetArchitectures: 2`) |
| **Graphics API** | `OpenGLES3` (Built-in Render Pipeline) |
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
2. Pastikan **`ARCamera`**, **`CircuitManager`**, **`LevelAndUIManager`**, seluruh 8 kartu (`Card_Input_A`, `Card_Input_B`, `Card_Gate_AND`, `Card_Gate_OR`, `Card_Gate_XOR`, `Card_Gate_NAND`, `Card_Gate_NOT`, `Card_Output`), serta Canvas HUD (`1920x1080`) aktif di Hierarchy.
3. Pada komponen **`CircuitManager`**, pastikan parameter `maxSnapDistance = 3` dan `inputSourceRangeMultiplier = 1.8`.
4. Pada komponen **`LevelAndUIManager`**, pastikan `isAudioMuted` tidak tercentang (`false`) bila ingin efek suara aktif sejak awal.
5. Untuk build APK Android: **File → Build Profiles → Android → Build**, pastikan target device menggunakan arsitektur ARM64 dengan Android OS minimal versi 10 (API 29).
