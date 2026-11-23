# Game Design Document (GDD) — GateWeaver AR

- **Versi Dokumen:** `8.5.0` (Rilis `v8.5.0`, branch `main`)
- **Genre:** Educational Spatial Augmented Reality Puzzle & Digital Logic Sandbox
- **Platform:** Android (`Min API 29`, `ARM64`, `OpenGLES3`, `Landscape Left`) & Unity Editor (`6000.6.9f1`)
- **Target Pengguna:** Siswa SMA/SMK Kelas X Fase E (Kurikulum Merdeka — Informatika) & Pendidik

---

## 1. Visi Produk & Pilar Desain Pedagogis

**GateWeaver AR** menjembatani pemahaman siswa dari representasi konkret-spasial menuju abstraksi simbolik pada materi **Gerbang Logika Kombinasional, Tabel Kebenaran, dan Aljabar Boolean**.

1. **Aksesibilitas Dual-Mode Terpadu:** Siswa dapat berpraktikum secara kolaboratif di kelas menggunakan 8 kartu cetak fisik (**Mode Kartu v5.0**) maupun berlatih mandiri kapan saja tanpa kartu cetak hanya dengan gestur tangan di depan kamera HP (**Mode Udara / Air-Weaving v8.5**).
2. **Quiet Minimalism & Keterbacaan Optik (*Smoked Optical Glass + Brushed Titanium*):** Seluruh antarmuka Mode Udara dirancang tenang tanpa *visual clutter*, mengandalkan ikon vektor SDF prosedural (simbol gerbang IEEE, siluet pose jari, indikator status tunggal) sehingga fokus kognitif siswa tetap tertuju pada rangkaian logika 3D.
3. **Debugging Mandiri & Proteksi Pedagogis:** Sistem mendorong siswa menguji keempat kombinasi biner (`00`, `01`, `10`, `11`) secara aktif sambil memantau strip *waveform* Input A/B, status ekspresi Boolean live (`Y = ...`), Pod Tabel Kebenaran `2x2`, serta Peta Sirkuit yang menyembunyikan gerbang target dan rumus solusi hingga level lulus diverifikasi `4/4`.

---

## 2. Arsitektur Dual-Mode & Alur Navigasi

Aplikasi dibuka di **Layar Judul (`AirWeavingTitleOverlay`)** yang menawarkan pemilihan langsung **Mode Udara (Hand Tracking)**, **Mode Kartu (Vuforia AR)**, atau **Sandbox (Laboratorium Bebas)**. Perpindahan antar-mode saat sesi berjalan dilakukan melalui **Laci Menu Kompak (`AirWeavingMenuDrawerOverlay`)** dengan *Camera Handover* bersih (*Android Camera2-safe*).

| Dimensi | Mode Udara (`v8.5` — Air-Weaving) | Mode Kartu (`v5.0` — Tangible AR) |
| :--- | :--- | :--- |
| **Engine Pelacakan** | MediaPipe `HandLandmarker` (`0.16.3`, CPU) + `WebCamTexture` | Vuforia Engine `11.4.4` (8 Image Targets) |
| **Kamera** | Kamera depan (cermin) / Webcam | Kamera belakang |
| **Panggung & Prop 3D** | Meja hologram 3D (*Smoked Tray*), prop industrial *Brushed Titanium* (`GwIndustrial3DKit` + `V8_AirProps`), shader Fresnel OpenGLES3 | 8 kartu fisik cetak + 12 model modular Quaternius (`V5_SciFi`) + *Open Cutaway* |
| **Skema Interaksi** | **Kepal (`Fist`)** sakelar & gerbang, **Cubit (`Pinch`)** kabel PBR, **Dua Tangan** orbit/zoom/pan, **Circle-Charge -> Open-5** summon | Auto-snap kiri-ke-kanan berbasis posisi fisik kartu + 4 kapsul kontrol bawah |
| **Evaluasi & Verifikasi** | Debugging mandiri `00..11`, bar tabel kebenaran live, *Auto-Win* + Modal Verifikasi `4/4` | Tombol `Uji Rangkaian` / pemindaian otomatis `00..11` + Pod `2x2` miring `-48°` |
| **Inti Simulasi** | `CircuitManager.cs` & `GateNode.cs` (*tracking-agnostic*, 0% regresi v5) | `CircuitManager.cs` & `GateNode.cs` (*tracking-agnostic*, 0% regresi v5) |

---

## 3. Core Gameplay Loop

1. **Pemilihan Mode & Misi:** Pengguna memilih mode di Layar Judul atau memilih node misi (`01`–`07`) pada **Peta Sirkuit (`AirWeavingLevelSelectorOverlay`)**. Saat pertama kali masuk Mode Udara, **Panduan Interaksi & Gestur Tangan** tampil dengan opsi **Latihan Cepat 4 Langkah**.
2. **Perakitan Rangkaian 3D:**
   - **Mode Udara:** Ambil gerbang dari Rak Parkir Atas dengan **Kepal (`Fist`)** atau panggil di ruang rakit lewat **2-Tahap Circle-Charge -> Open-5 Bloom**, atur posisi di meja 3D, lalu sambungkan kabel antar-port menggunakan **Cubit (`Pinch`)**.
   - **Mode Kartu:** Susun kartu fisik `Input A/B`, gerbang logika, dan `Output` di atas meja hingga saluran energi tersambung otomatis.
3. **Eksperimen Sinyal & Debugging Mandiri:**
   - Ubah nilai `Input A` dan `Input B` (`0 <-> 1`) menggunakan **Kepal (`Fist`)** di Mode Udara (atau kapsul/rotasi kartu di Mode Kartu).
   - Amati perubahan pada *waveform* A/B, *Pulsing Signal Packets* di sepanjang kabel PBR, mekanisme *Open Cutaway*, lencana pemandu Output (`SAMBUNG KE OUTPUT`, `KURANGI GERBANG`, `CEK SEL MERAH`, `UJI SAKELAR (X/4)`), dan 4 sel tabel kebenaran `[0,0]..[1,1]`.
4. **Verifikasi Lulus `4/4` & Progresi:** Ketika seluruh 4 kombinasi input cocok dengan target dalam batas kuota gerbang, **Modal Verifikasi Lulus `4/4`** (`AirWeavingLevelCompleteModal`) menampilkan ekspresi Boolean terverifikasi, tabel kebenaran penuh, metrik kuota, serta opsi **Lanjut Level** atau **Ulangi Level**.

---

## 4. Mekanik Interaksi & Pipeline Teknis Mode Udara (`v8.5`)

### 4.1 Pipeline *Hand Tracking* Anti-Wajah (`AirWeavingController.cs`)
- **Konfigurasi MediaPipe:** `minHandDetectionConfidence = 0.62f`, `minHandPresenceConfidence = 0.58f`, `minTrackingConfidence = 0.55f`, resolusi inferensi `MaxInferenceLongEdge = 256`, `MinCandidateRawPalmScale = 0.042f`.
- **Validasi Anatomi Worker Zero-GC:** Memeriksa integritas 21 landmark, proporsi dan luas segitiga telapak `(0, 5, 17)`, proporsi tulang proksimal, skala 3D `worldLandmarks` (`0.042m – 0.145m`), skor *handedness* adaptif (`>= 0.65f` entry/tangan-2, `>= 0.50f` saat tangan utama terkunci), serta penolakan duplikat/skala timpang pada tangan ke-2.
- **Temporal Entry & Anti-Teleport:** Slot tangan dan visual skeleton/reticle baru aktif setelah **3 frame berturut-turut** dalam radius `0.16f`, menolak lonjakan 1–2 frame *glitch* ke area wajah.

### 4.2 Detektor Gestur Dual-Kanal (`AirWeavingHandGestureDetector.cs`)
- **Kepal (`Fist`) *Dorsal-Safe*:** Menggabungkan sudut tekukan 3D dengan metrik lipatan radial 2D agar kepalan dari sudut punggung tangan tetap responsif (debounce `25 ms`, *Pre-Fist Latch* `160 ms`), dilindungi `isFistAnatomyDominant` agar kepalan tidak dibajak menjadi `Pinch` tanpa mengganggu postur cubit kabel (`isTipToTipPinchPosture`).
- **Sakelar & Drag Stabil:** Toggle sakelar `Input A/B` terpicu instan pada *rising-edge* `FistJustStarted`; perpindahan gerbang (`GateGrab`) menggunakan perpotongan *ray-plane* (`TryIntersectHandWithHoverPlane`) terhadap bidang hover meja agar bebas pergeseran kedalaman palsu.
- **5 Pose Summon (`AirWeavingCircleSummoner.cs`):**
  - `1 Telunjuk` -> **`AND`** (`A . B`)
  - `2 Jari V` -> **`OR`** (`A + B`)
  - `Jempol Atas` -> **`NOT`** (`!A`)
  - `3 Jari` -> **`XOR`** (`A (+) B`)
  - `4 Jari` -> **`NAND`** (`!(A . B)`)
  - Dilengkapi guard pencegah salah-picu (`PoseConfirmHoldSeconds = 0.240f`, `StartVisualAngleDegrees = 105°`, `MinTotalArcLengthForStartVisual = 0.18f`).

### 4.3 Stabilitas Panggung 3D & Kabel PBR (`AirWeavingStageController.cs`, `AirWeavingCableRouter.cs`, `AirWeavingHologramVisual.cs`)
- **Kendali 2-Tangan Terbatas Aman:** Rentang kedalaman `2.10m – 5.40m`, *depth gain* & kecepatan terkendali (`1.80`), batas pan OBB dalam frustum (`55%` horizontal, `50%` vertikal), clamp sudut Orbit Pitch `[-28°, +42°]` dan Roll `[-25°, +25°]`, serta penonaktifan *Auto Edge-Pan* vertikal ke atas saat `GateGrab`.
- **Kabel PBR & Proyeksi Kursor:** Pool 20 kabel tabung 3D PBR + 60 *Pulsing Signal Packets* Zero-GC. Garis proyeksi jatuh kursor (`Holo_CursorDropLine`) dibatasi maksimal `0.38` unit dan disembunyikan saat kursor melampaui `localPos.y > 0.55f` atau keluar `AssemblyZone`.

---

## 5. Kurikulum 7 Level & Mode Sandbox (Total 21 Bintang)

| Level | Konsep & Judul Misi | Target Logika & Solusi Referensi | Kuota |
| :---: | :--- | :--- | :---: |
| **01** | **OR** — Alarm Pintu & Jendela | Output `1` jika A atau B bernilai `1` (`Y = A OR B`) | Maks `1` |
| **02** | **XOR** — Sakelar Tangga Dua Arah | Output `1` jika A dan B berbeda (`Y = A XOR B`) | Maks `1` |
| **03** | **AND + NOT** — Kunci Keamanan Brankas | Output `1` hanya saat A = `1` dan B = `0` (`Y = A AND (NOT B)`) | Maks `2` |
| **04** | **NAND** — Pemutus Darurat Reaktor | Output `0` hanya saat A = `1` dan B = `1` (`Y = A NAND B`) | Maks `1` |
| **05** | **XNOR** — Sinkronisasi Inti Kembar | Output `1` saat A dan B bernilai sama (`Y = NOT(A XOR B)`) | Maks `2` |
| **06** | **NOR** — Sensor Hening Ruang Server | Output `1` hanya saat A = `0` dan B = `0` (`Y = NOT(A OR B)`) | Maks `2` |
| **07** | **De Morgan** — Kunci Biometrik | Output `0` hanya saat A = `0` dan B = `1` (`A OR (NOT B)` / `(NOT A) NAND B`) | Maks `2` |
| **Sandbox** | **Laboratorium Bebas** | Eksplorasi kombinasi gerbang bebas tanpa batas kuota | Bebas (`∞`) |

---

## 6. Sistem Visual, Audio, & Antarmuka (`v8.5.0`)

- **Desain Visual (`GwThemeTokens.cs` & `GwIndustrial3DKit.cs`):** Bahasa visual *Smoked Optical Glass + Brushed Titanium* pada kanvas referensi `1920×1080`, seluruh ikon (simbol IEEE, pose jari, gestur, centang, cincin radio) dihasilkan secara prosedural via SDF Zero-GC tanpa membebani ukuran paket atau alokasi GC.
- **Shader & Pencahayaan (`AirWeavingLightingAndShaderRig.cs`):** Built-in Render Pipeline (Forward) ramah OpenGLES3 dengan `ARBackdropDimming.shader`, `HologramFresnelHardware.shader`, dan rig pencahayaan 3-Point + Hemisphere analitik.
- **Audio Spasial Prosedural (`AirWeavingSpatialSfx.cs`):** 24 cue *one-shot* ber-panning stereo spasial + 2 *looping hum* (seret kabel & gerbang dengan watchdog anti-nyangkut) + *soft limiter* `tanh`, 100% PCM prosedural Zero-GC.
- **5 Overlay Inti v8.5:**
  1. `AirWeavingTitleOverlay` — Layar Judul & Pemilih Mode (kartu 2 baris + cincin radio).
  2. `AirWeavingMenuDrawerOverlay` — Laci Menu (`01 // MODE SIMULASI`, `02 // KEMAJUAN LEVEL (X/7)`, `03 // KONTROL & KALIBRASI`).
  3. `AirWeavingLevelSelectorOverlay` — Peta Sirkuit 7 node kompak + panel detail misi berproteksi pedagogis.
  4. `AirWeavingGestureGuide` — Modal 2 kolom (`01 // 3 GESTUR UTAMA` + `02 // 5 POSE SUMMON GERBANG` grid `3x2` + Latihan Cepat 4 Langkah).
  5. `AirWeavingLevelCompleteModal` — Modal Verifikasi Lulus `4/4` dengan tabel kebenaran 1 kontainer bergaris grid `1px`.

---

## 7. Batasan Teknis & Rencana Pengembangan Selanjutnya

- **Batasan Arsitektur Terkunci:** Unity `6000.6.9f1` Built-in RP (tanpa URP/AR Foundation/XR Plugin agar kompatibel penuh dengan Vuforia `11.4.4` & MediaPipe `0.16.3`), isolasi total kode Mode Udara di `Assets/Scripts/AirWeaving/`, serta Zero-GC pada loop `Update()` dan worker inferensi.
- **Kandidat Pengembangan Pasca-`v8.5.0`:**
  - *In-Game Telemetry Toggle* ringan (FPS & status kamera) untuk pengujian lapangan di perangkat Android beragam rasio layar.
  - Tantangan lanjutan 3-Input (`A`, `B`, `C`) serta studi kasus *Half-Adder / Full-Adder* (2 output `Sum` & `Carry`).
  - Ikon aplikasi kustom & *splash screen* rilis produksi.
