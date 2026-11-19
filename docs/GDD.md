# Game Design Document (GDD) — GateWeaver AR

- **Versi Dokumen:** 6.0 (Baseline Rilis Stabil `v6.0`, branch `main`)
- **Genre:** Educational Augmented Reality Puzzle & Spatial Logic Sandbox
- **Platform:** Android (Minimum API 29 / Android 10, ARM64, Landscape Left) & Unity Editor (Pengujian Webcam)
- **Target Pengguna:** Siswa SMA/SMK Fase E (Kurikulum Merdeka — Mata Pelajaran Informatika) & Pendidik

---

## 1. Visi Produk & Tujuan Pedagogis

**GateWeaver AR** mengubah konsep abstrak **Gerbang Logika, Tabel Kebenaran, dan Aljabar Boolean** menjadi pengalaman merakit mesin 3D spasial secara langsung di dunia nyata.

### Pilar Desain Utama
1. **Aksesibilitas Dual-Mode:** Siswa bisa belajar dengan kartu fisik cetak di kelas (**Mode Kartu v5**) maupun langsung tanpa peralatan tambahan cukup dengan tangan di depan kamera HP (**Mode Udara / Air-Weaving v6**).
2. **Representasi Konkret-ke-Simbolik:** Setiap sinyal biner (`0` / `1`) tampil bersamaan sebagai animasi mekanis 3D (tuas sakelar, saluran energi menyala/padam, diorama output), ekspresi Boolean *real-time*, matriks tabel kebenaran, dan gelombang waktu (osiloskop 3-kanal).
3. **Scaffolding Terukur (Tanpa Langsung Memberi Jawaban):** Sistem *hint* 3 tingkat (`Petunjuk 0/3`), batas kuota gerbang, dan evaluasi otomatis seluruh kombinasi input melatih penalaran komputasional siswa secara bertahap.

---

## 2. Arsitektur Dual-Mode AR

Aplikasi selalu memulai pada **Mode Kartu (v5)** dan menyediakan perpindahan instan melalui tombol HUD `Mode: Udara (v6)` ↔ `Mode: Kartu (v5)` dengan *Camera Handover* bersih (*Camera2-safe*).

| Aspek | Mode Kartu (v5 — Tangible AR) | Mode Udara (v6 — Air-Weaving AR) |
| :--- | :--- | :--- |
| **Mesin Pelacakan** | Vuforia Engine 11.4.4 (Image Targets) | MediaPipeUnityPlugin 0.16.3 (`HandLandmarker`, CPU) + `WebCamTexture` |
| **Kamera** | Kamera belakang | Kamera depan (cermin horizontal) / Webcam laptop |
| **Objek Interaksi** | 8 kartu fisik cetak (`Input A/B`, 5 Gerbang, `Output`) | Meja kerja spasial 3D di udara (Terminal Tetap + Rak Parkir Atas) |
| **Kendali Gerbang** | Menggeser & mendekatkan kartu fisik di meja | *Air-Pinch & Drag*, *Air-Tap*, dan *Air-Draw* 5 simbol neon |
| **Mesin Evaluasi** | `CircuitManager.cs` (identik 100% di kedua mode) | `CircuitManager.cs` (identik 100% di kedua mode) |

---

## 3. Core Gameplay Loop

1. **Pahami Misi:** Siswa membaca target logika pada bilah atas (mis. kondisi kapan `Output` harus `1` atau `0`) beserta batas `Kuota Gerbang`.
2. **Rakit Mesin 3D:**
   - Di **Mode Kartu**, susun kartu dari kiri ke kanan (`Input A/B` → Gerbang Logika → `Output`) hingga saluran energi tersambung otomatis.
   - Di **Mode Udara**, cubit-tarik gerbang dari **Rak Parkir Atas** atau gambar simbol di udara (*Air-Draw*) di dalam **Zona Rakit**.
3. **Eksperimen Sinyal:** Jungkit sakelar `Input A` dan `Input B` (via tombol HUD atau *Air-Tap* 3D) untuk mengamati perubahan rumus rakitan (`Y = ...`), aliran energi, dan osiloskop 3-kanal secara *real-time*.
4. **Verifikasi Otomatis (`Uji Rangkaian`):** Sistem menguji seluruh 4 baris kombinasi input (`00`, `01`, `10`, `11`) secara otomatis terhadap target tabel kebenaran.
5. **Umpan Balik & Progresi:** Jika 4/4 baris `[OK] LULUS` dan tidak melebihi kuota gerbang, siswa meraih 1–3 bintang (berdasarkan efisiensi percobaan & penggunaan petunjuk), membuka level berikutnya, dan mencatat capaian di **Rapor CP**.

---

## 4. Mekanik Interaksi Detail

### 4.1 Sistem Rangkaian Spasial (`CircuitManager.cs` & `GateNode.cs`)
- **8 Node Standar:** `Card_Input_A`, `Card_Input_B`, `Card_Gate_AND`, `Card_Gate_OR`, `Card_Gate_XOR`, `Card_Gate_NAND`, `Card_Gate_NOT`, dan `Card_Output`.
- **Auto-Snap Kiri-ke-Kanan:** Port keluaran otomatis mencari port masukan terdekat dalam radius jangkauan alami (`maxSnapDistance = 3.0f`, jangkauan sumber Input `1.8x`), memicu *Snap Lock VFX*, saluran energi berlapis (kurva Bezier), dan *energy bolt* searah aliran sinyal.
- **Evaluasi Real-Time:** Menghitung ekspresi Boolean majemuk (termasuk gerbang bertingkat seperti `NOT(A OR B)` dan transformasi De Morgan) serta menjaga perhitungan `Kuota Gerbang` hanya untuk gerbang aktif di Zona Rakit.

### 4.2 Mekanik Air-Weaving v6.0 (`Assets/Scripts/AirWeaving/`)
- **Pelacakan 21 Sendi + One-Euro Filter:** Rangka tangan neon sian dihaluskan secara adaptif (*zero-GC*): stabil saat diam membidik, namun responsif tanpa *lag* saat menggores cepat.
- **Tata Letak Breadboard Virtual:**
  - `Input A` (kiri atas), `Input B` (kiri bawah), dan `Output` (kanan) terkunci stabil di dalam area bebas HUD.
  - 5 gerbang (`AND`, `OR`, `XOR`, `NAND`, `NOT`) berjejer di **Rak Parkir Atas** dalam mode pratinjau 3D (`0.56x`, `isTracked = false`) lengkap dengan lencana nama singkat agar tidak memakan kuota atau menyambung kabel prematur.
- **Air-Pinch & Drag (Prioritas Tertinggi):**
  - Memakai **Scale-Invariant Pinch Ratio** (`dist(jempol, telunjuk) / panjang_telapak`: aktif `< 0.34`, lepas `> 0.46`) agar konsisten di jarak dekat maupun jauh.
  - Dilengkapi **Occlusion Grace (`0.16 dtk`)** agar *self-occlusion* singkat tidak menjatuhkan mesin.
  - Melepas gerbang di atas garis rak otomatis memarkirkannya kembali dan memutus seluruh kabel dengan bersih.
- **Air-Tap Sakelar 3D:** Menyentuh hologram `Input A` atau `Input B` dengan ujung telunjuk (`#8`) atau cubitan singkat (`cooldown 0.55 dtk`) langsung menjungkit tuas 3D `-38° <-> +38°` disertai percikan partikel.
- **`$1 Unistroke` Air-Draw Spawner:**
  - Dipicu oleh **Pose Tongkat Telunjuk** (hanya telunjuk terentang, minimal 2 jari lain terlipat, tidak sedang *Pinch*, di dalam Zona Rakit).
  - Menggambar lintasan neon sian-violet dan memanggil mesin 3D tepat di pusat goresan saat tangan dibuka atau diam `~0.38 dtk`:
    - `^` → **`AND`**
    - `O` → **`OR`**
    - `Z` → **`XOR`**
    - `V` → **`NAND`**
    - `|` → **`NOT`**

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

## 6. Desain Visual, Audio, & UI/UX

- **Aset 3D & Material (v5.0):** 12 model sci-fi *low-poly* **Quaternius (CC0)** dengan pelat dasar `AR_BaseTile3D`, port fisik berkode warna sinyal, mekanisme gerbang unik per tipe, diorama `Output` reaktif sesuai tema level, *rim/fill lighting* studio AR, serta label 3D kompak (`Label_Status3D` + `AR_LabelBackdrop`).
- **Audio & VFX:** Umpan balik audio untuk jungkit sakelar, sambungan kabel, *Air-Draw*, dan kelulusan level (dapat dimatikan via tombol `Audio: ON/OFF`), ditambah *zero-GC* `AR_SparkBurstPool`.
- **Filosofi UI Diet (HUD Minimalis):**
  - **Atas:** Judul Level (kiri), Pita Misi (tengah — melebar satu baris otomatis saat Mode Udara agar bebas Rak Parkir), Kuota Gerbang (kanan).
  - **Kiri:** Panel lipat **Matriks Tabel Kebenaran & Rumus** (tab `Tabel`, `Osiloskop`, tombol `Lipat`, `Petunjuk 0/3`, `Rapor CP`).
  - **Kanan:** Tumpukan kompak `Reset Level`, `Pilih Level`, dan `Mode: Kartu/Udara`.
  - **Bawah:** Tombol `Input A`, `Input B`, kapsul status 1 baris `Txt_Feedback`, serta `Uji Rangkaian` / `Level Berikutnya`.

---

## 7. Backlog & Arah Pengembangan (v6.x / v7.0 — Belum Diimplementasikan)

*(Bagian ini hanya daftar kandidat fitur masa depan; jangan diimplementasikan kecuali diminta eksplisit oleh pengguna.)*
- **v6.x (QA & Presentasi):**
  - *In-Game Debug Console* tersembunyi (pemicu ketuk 3 jari) untuk inspeksi FPS, log error, dan status kamera di HP Android.
  - Ikon aplikasi kustom (`Default Icon`) & *splash screen* GateWeaver AR.
  - *Editor-only Mouse Fallback* ringan di Mode Udara untuk pengujian cepat tanpa webcam.
- **v7.0 (Ekspansi Pedagogi & Interaksi):**
  - Tantangan rangkaian 3-Input (`A`, `B`, `C`) atau *Half-Adder / Full-Adder* (gerbang majemuk + 2 output `Sum` & `Carry`).
  - *Onboarding* interaktif singkat (tutorial gestur 3 langkah saat pertama kali membuka Mode Udara).
  - Ekspor ringkasan **Rapor CP** siswa (CSV/PNG/kode sesi) untuk penilaian guru di kelas.
