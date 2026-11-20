# Game Design Document (GDD) — GateWeaver AR

- **Versi Dokumen:** 7.0 (Baseline Rilis Stabil `v7.0`, branch `main`)
- **Genre:** Educational Augmented Reality Puzzle & Spatial Logic Sandbox
- **Platform:** Android (Minimum API 29 / Android 10, ARM64, Landscape Left) & Unity Editor (Pengujian Webcam)
- **Target Pengguna:** Siswa SMA/SMK Fase E (Kurikulum Merdeka — Mata Pelajaran Informatika) & Pendidik

---

## 1. Visi Produk & Tujuan Pedagogis

**GateWeaver AR** mengubah konsep abstrak **Gerbang Logika, Tabel Kebenaran, dan Aljabar Boolean** menjadi pengalaman merakit mesin 3D spasial secara langsung di dunia nyata.

### Pilar Desain Utama
1. **Aksesibilitas Dual-Mode (`v7.0`):** Siswa bisa belajar dengan kartu fisik cetak di kelas (**Mode Kartu v5** — 0% regresi) maupun langsung tanpa peralatan tambahan cukup dengan tangan di depan kamera HP (**Mode Udara / Air-Weaving v7** — *100% Zero-Touch*).
2. **Representasi Konkret-ke-Simbolik (*Open Cutaway* & Pod `2x2`):** Setiap sinyal biner (`0` / `1`) tampil bersamaan sebagai mekanisme terbuka di dalam mesin 3D (`GateMechanismVisual`), aliran kabel energi berwarna live, diorama Output reaktif, dan Pod Tabel Kebenaran `2x2` (`00`, `01`, `10`, `11`) tepat pada diorama Output.
3. **Debugging Mandiri & Scaffolding Terukur:** Alih-alih sekadar menekan tombol uji otomatis di Mode Udara, siswa diundang menguji keempat kombinasi `00/01/10/11` lewat sakelar `Input A/B`, dibimbing oleh lencana pemandu live (`SAMBUNG KE OUTPUT`, `KURANGI GERBANG!`, `CEK SEL MERAH!`, `UJI SAKELAR (X/4)`, `LULUS! 4/4`) serta sistem *hint* 3 tingkat (`Petunjuk 0/3`).

---

## 2. Arsitektur Dual-Mode AR

Aplikasi selalu memulai pada **Mode Kartu (v5)** dan menyediakan perpindahan instan melalui **Laci Menu Kompak** pojok kanan atas (`Mode: Udara (v7)` ↔ `Mode: Kartu (v5)`) dengan *Camera Handover* bersih (*Camera2-safe*).

| Aspek | Mode Kartu (v5 — Tangible AR) | Mode Udara (v7 — Air-Weaving Hologram AR) |
| :--- | :--- | :--- |
| **Mesin Pelacakan** | Vuforia Engine 11.4.4 (Image Targets) | MediaPipeUnityPlugin 0.16.3 (`HandLandmarker`, CPU) + `WebCamTexture` |
| **Kamera** | Kamera belakang | Kamera depan (cermin horizontal) / Webcam laptop |
| **Objek Interaksi** | 8 kartu fisik cetak (`Input A/B`, 5 Gerbang, `Output`) | Hologram 3D melayang bebas (*levitation* + *tilt* `±12°`) di ruang rakit (`AssemblyZone` 4 siku) + Rak Parkir Atas |
| **Kendali Gerbang & Kabel** | Geser kartu fisik di meja (auto-snap kiri-ke-kanan) | 2-Tahap *Circle Summon* + *Open-5 Bloom*, **Cubit (`Pinch`)** kabel manual port-ke-port (*fan-out*/cabut), **Kepal (`Fist`)** pindah/buang gerbang, **Telunjuk `#8`** sakelar Input |
| **HUD & Pengujian** | 4 kapsul ramping bawah (`Input A/B`, `Uji`/`Lanjut`) + Pod `2x2` miring `-48°` | **0 tombol gameplay (*Zero-Touch*)**, Pod `2x2` menghadap kamera, *Auto-Win* saat `4/4` hijau, & tahan kepal di Output untuk *Next Level* |
| **Mesin Evaluasi** | `CircuitManager.cs` (inti *tracking-agnostic* terpadu) | `CircuitManager.cs` (inti *tracking-agnostic* terpadu + dukungan kabel manual & *fan-out*) |

---

## 3. Core Gameplay Loop

1. **Pahami Misi:** Siswa membaca target logika pada Bilah Atas ramping (`72 px`) beserta batas `Kuota Gerbang` dan konteks label 3D tiap level.
2. **Rakit Mesin 3D:**
   - Di **Mode Kartu (v5)**, susun kartu fisik dari kiri ke kanan (`Input A/B` → Gerbang Logika → `Output`) hingga saluran energi tersambung otomatis.
   - Di **Mode Udara (v7)**, panggil gerbang dengan **2-Tahap Circle Summon + Open-5 Bloom**, atur posisinya dengan **Kepal (`Fist`)** di dalam 4 siku `AssemblyZone`, lalu tarik/sambungkan kabel antar orb port dengan **Cubit (`Pinch` `#4+#8`)**.
3. **Eksperimen Sinyal & Debugging Mandiri:**
   - Di **Mode Udara (v7)**, sentuh sakelar `Input A` dan `Input B` dengan ujung **Telunjuk `#8`** (`75 ms` + *ripple*) untuk mengamati mekanisme *Open Cutaway*, warna kabel sinyal live, dan status 4 kotak biner `00/01/10/11` pada Pod `OutputTruthPodVisual` sambil dibimbing lencana pemandu live (`AirWeavingOutputTrigger`).
   - Di **Mode Kartu (v5)**, ubah sakelar via kapsul bawah/sentuh 3D/rotasi kartu lalu tekan `Uji Rangkaian` untuk pemindaian otomatis `00..11`.
4. **Auto-Win & Progresi:** Begitu 4/4 sel hijau dan tidak melebihi kuota gerbang, siswa meraih 1–3 bintang (tercatat di **Rapor CP**). Di Mode Udara, `LULUS! 4/4` langsung memicu ***Auto-Win*** dan siswa cukup **menahan kepal (`Fist`) di diorama `Output`** untuk *Zero-Touch Next Level*.

---

## 4. Mekanik Interaksi Detail

### 4.1 Sistem Rangkaian Spasial & Visual Inti (`Assets/Scripts/Core/`)
- **8 Node Standar (`GateNode.cs`):** `Card_Input_A`, `Card_Input_B`, `Card_Gate_AND`, `Card_Gate_OR`, `Card_Gate_XOR`, `Card_Gate_NAND`, `Card_Gate_NOT`, dan `Card_Output`.
- **Dual-Mode Routing (`CircuitManager.cs`):**
  - Mode Kartu (v5): auto-snap kiri-ke-kanan berbasis jarak (`maxSnapDistance = 3.0f`, jangkauan sumber Input `1.8x`), *Snap Lock VFX*, saluran energi berlapis, & *energy bolt*.
  - Mode Udara (v7): koneksi manual port-ke-port dengan *fan-out* multi-cabang dari `Port_Out` yang dirender seragam orb-ke-orb oleh `AirWeavingCableRouter`.
- **Mekanisme Terbuka (`GateMechanismVisual.cs`):** Konstruksi *Open Cutaway* reaktif pada 5 gerbang logika (termasuk *Power Core* internal pada `NOT` & `NAND`) yang bekerja identik di kedua mode.
- **Pod Tabel Kebenaran `2x2` (`OutputTruthPodVisual.cs`):** 4 kotak biner tegas (`00`, `01`, `10`, `11`) dengan warna status jelas (abu gelap = belum diuji, tepi sian = aktif saat ini, hijau = lolos, merah = salah). Miring `-48°` di Mode Kartu dan sejajar menghadap kamera di Mode Udara.

### 4.2 Mekanik Air-Weaving v7.0 (`Assets/Scripts/AirWeaving/`)
- **Tracking 1:1 Adaptif & Visual 3D Melayang (`AirWeavingController.cs`, `AirWeavingHologramVisual.cs`):**
  - Proyeksi tangan 21 sendi 1:1 dengan *smoothing* adaptif berbasis kecepatan (*One-Euro-style*, *zero-GC*).
  - Hologram 3D mengambang bebas (*sinusoidal levitation* + *velocity tilt* `±12°`, tanpa cincin lantai pipih), 4 siku sudut `AssemblyZone`, serta *reticle* otomatis tersembunyi saat tangan keluar kamera.
- **2-Tahap Circle Summon + Open-5 Bloom (`AirWeavingCircleSummoner.cs`, `AirWeavingSummonVisual.cs`):**
  - Putar ujung jari di ruang rakit dengan indikator pose live di ujung jari:
    - `1` jari → **`AND`**
    - `2` jari → **`OR`**
    - `3` jari → **`XOR`**
    - `4` jari (jempol lipat) → **`NAND`**
    - `Jempol saja` → **`NOT`**
  - Buka lebar **5 jari (*Open-5 Bloom*)** untuk *spawn* gerbang dari Rak Parkir Atas tepat di pusat lingkaran (atau kepal untuk batal instan; tampilkan *Notice + Pulse* amber non-destruktif bila jenis gerbang sudah ada di papan).
- **Manajemen Kabel Manual — Cubit / `Pinch` (`AirWeavingCableRouter.cs`, `AirWeavingHandGestureDetector.cs`):**
  - Cubit **Jempol `#4` + Telunjuk `#8`** (skala-invarian + *pre-pinch guard*) pada orb port (`Port_Out` / `Port_In`) untuk tarik, pasang, *fan-out* (dari `Port_Out`), cabut/ganti (dari `Port_In`), atau lepas di ruang kosong untuk putus.
  - Seluruh kabel Mode Udara dirender seragam orb-ke-orb menggunakan **pool 20 Bezier zero-GC** dengan warna sinyal live.
- **Manipulasi Objek — Kepal / `Fist` (`AirWeavingHandGestureDetector.cs`, `AirWeavingController.cs`):**
  - Kepal badan gerbang (*sticky hysteresis* + *occlusion grace* `180 ms`) untuk memindah posisi di dalam ruang rakit.
  - Tarik gerbang keluar dari 4 siku `AssemblyZone` (bingkai & aura menyala merah) lalu lepas untuk mengembalikannya ke Rak Parkir Atas sekaligus memutus seluruh kabelnya.
- **Sakelar Input A/B — Sentuh Telunjuk `#8` (`AirWeavingController.cs`):**
  - Sentuh ringan ujung telunjuk `#8` (`75 ms` dwell + *Exit-to-Rearm*) disertai efek *ripple* untuk membalik nilai `0 ↔ 1`.
- **100% Debugging Mandiri & *Zero-Touch Next Level* (`AirWeavingOutputTrigger.cs`, `OutputTruthPodVisual.cs`):**
  - Lencana pemandu live tepat di bawah Pod `00/01/10/11`: `SAMBUNG KE OUTPUT`, `KURANGI GERBANG!`, `CEK SEL MERAH!`, `UJI SAKELAR (X/4)`, `LULUS! 4/4` → *Auto-Win* otomatis; tahan kepal pada diorama Output setelah lulus untuk pindah level tanpa menyentuh layar.

---

## 5. Struktur Kurikulum & Progresi Level

Progresi disusun dari gerbang tunggal dasar menuju rangkaian komposit dan hukum aljabar Boolean (**7 Level Kurikulum = maks. 21 Bintang** + **Mode Sandbox** bebas). Sumber kebenaran implementasi berada di `Assets/Scripts/Levels/LevelAndUIManager.cs`.

| Level | Konsep Inti & Judul Misi | Fokus Capaian Belajar & Solusi | Kuota |
| :--- | :--- | :--- | :--- |
| **Level 1** | **OR** *(Alarm Pintu & Jendela)* | Output = `1` jika A atau B bernilai `1` (`Y = A OR B`) | Maks 1 |
| **Level 2** | **XOR** *(Sakelar Tangga Dua Arah)* | Output = `1` jika posisi A dan B berbeda (`Y = A XOR B`) | Maks 1 |
| **Level 3** | **AND + NOT** *(Kunci Keamanan Brankas)* | Output = `1` hanya saat A = `1` dan B = `0` (`Y = A AND (NOT B)`) | Maks 2 |
| **Level 4** | **NAND** *(Pemutus Darurat Reaktor)* | Output = `0` hanya saat A = `1` dan B = `1` (`Y = A NAND B`) | Maks 1 |
| **Level 5** | **XNOR** *(Sinkronisasi Inti Kembar)* | Output = `1` saat A dan B bernilai sama (`Y = NOT(A XOR B)`) | Maks 2 |
| **Level 6** | **NOR** *(Sensor Hening Ruang Server)* | Output = `1` hanya saat A = `0` dan B = `0` (`Y = NOT(A OR B)`) | Maks 2 |
| **Level 7** | **De Morgan** *(Kunci Biometrik)* | Output = `0` hanya saat A = `0` dan B = `1` (`A OR (NOT B)` / `(NOT A) NAND B`) | Maks 2 |
| **Sandbox** | **Eksplorasi Bebas** | Merangkai kombinasi bebas hingga 5 gerbang tanpa batas kuota | Bebas (∞) |

---

## 6. Desain Visual, Audio, & UI/UX (`v7.0`)

- **Aset 3D & Material:** 12 model sci-fi *low-poly* **Quaternius (CC0)**, mekanisme terbuka *Open Cutaway* (`GateMechanismVisual`), Pod Tabel Kebenaran `2x2` (`OutputTruthPodVisual`), hologram melayang Mode Udara (`AirWeavingHologramVisual`), serta label kontekstual 3D per level.
- **Audio & VFX:** Umpan balik audio PCM prosedural untuk jungkit sakelar, sambungan/cabut kabel, *Circle Summon + Bloom*, dan kelulusan level (dapat dimatikan via Laci Menu Kompak `Audio: ON/OFF`), ditambah *zero-GC* `AR_SparkBurstPool`.
- **Filosofi UI Diet — HUD Minimalis & Menu Kompak (`CompactHudMenuController.cs`):**
  - **Atas:** 1 Bilah Atas ramping (`72 px`) berisi Judul Level, Misi 1 baris, Kuota Gerbang (berubah merah berkontras tinggi jika terlampaui), dan tombol ikon 3 garis pojok kanan atas.
  - **Laci Menu Kompak:** Memuat `Mode: Udara (v7)` / `Mode: Kartu (v5)`, `Pilih Level`, `Reset Level`, `Rapor CP`, `Petunjuk (0/3)`, dan `Audio: ON/OFF` + *scrim* klik-luar untuk menutup.
  - **Bawah:**
    - **Mode Udara (v7):** **0 tombol gameplay** (*100% Zero-Touch*).
    - **Mode Kartu (v5):** 4 kapsul ramping semi-transparan (`Input A`, `Input B`, `Uji Rangkaian` / `Level Berikutnya`).
  - **Notifikasi (`Txt_Feedback`):** Mode *Toast-Only* berkontras tinggi (tersembunyi saat idle; gaya warna khusus untuk Gagal, Berhasil, & Petunjuk dengan prioritas aman agar status *no-hand* tidak menimpa hasil evaluasi).

---

## 7. Backlog & Arah Pengembangan (`v7.x` / `v8.0` — Belum Diimplementasikan)

*(Bagian ini hanya daftar kandidat fitur masa depan; jangan diimplementasikan kecuali diminta eksplisit oleh pengguna.)*
- **v7.x (QA & Presentasi):**
  - *In-Game Debug Console* tersembunyi (pemicu ketuk 3 jari) untuk inspeksi FPS, log error, dan status kamera di HP Android.
  - Ikon aplikasi kustom (`Default Icon`) & *splash screen* GateWeaver AR.
  - *Editor-only Mouse Fallback* ringan di Mode Udara untuk pengujian cepat tanpa webcam.
- **v8.0 (Ekspansi Pedagogi & Interaksi):**
  - Tantangan rangkaian 3-Input (`A`, `B`, `C`) atau *Half-Adder / Full-Adder* (gerbang majemuk + 2 output `Sum` & `Carry`).
  - *Onboarding* interaktif singkat (tutorial gestur 3 langkah saat pertama kali membuka Mode Udara).
  - Ekspor ringkasan **Rapor CP** siswa (CSV/PNG/kode sesi) untuk penilaian guru di kelas.
