# Game Design Document (GDD) — GateWeaver AR

- **Versi Dokumen:** 7.5 (Baseline Rilis Stabil `v7.5`, branch `main`)
- **Genre:** Educational Augmented Reality Puzzle & Spatial Logic Sandbox
- **Platform:** Android (Minimum API 29 / Android 10, ARM64, Landscape Left) & Unity Editor (Pengujian Webcam)
- **Target Pengguna:** Siswa SMA/SMK Fase E (Kurikulum Merdeka — Mata Pelajaran Informatika) & Pendidik

---

## 1. Visi Produk & Tujuan Pedagogis

**GateWeaver AR** mengubah konsep abstrak **Gerbang Logika, Tabel Kebenaran, dan Aljabar Boolean** menjadi pengalaman merakit mesin 3D spasial secara langsung di dunia nyata.

### Pilar Desain Utama
1. **Aksesibilitas Dual-Mode & Onboarding Instan (`v7.5`):** Siswa bisa belajar dengan kartu fisik cetak di kelas (**Mode Kartu v5** — 0% regresi) maupun langsung tanpa peralatan tambahan cukup dengan tangan di depan kamera HP (**Mode Udara / Air-Weaving v7.5** — *100% Zero-Touch*, dilengkapi **Kartu Panduan Gestur 1 Halaman** + **Pil Latihan Cepat 4 Langkah** interaktif).
2. **Representasi Konkret-ke-Simbolik (*Open Cutaway*, *Pulsing Packets*, Pod `2x2`, & *Live Boolean Chip*):** Setiap sinyal biner (`0` / `1`) tampil serentak sebagai mekanisme terbuka di dalam mesin 3D (`GateMechanismVisual`), aliran kabel energi & *Pulsing Signal Packets* Zero-GC, audio spasial PCM prosedural (`AirWeavingSpatialSfx`), diorama Output reaktif, Pod Tabel Kebenaran `2x2` (`00`, `01`, `10`, `11`), serta ***Live Boolean Expression Chip*** formal di kedua mode (`PedagogyMasteryHud`).
3. **Debugging Mandiri, Scaffolding Terukur, & Evaluasi Penguasaan:** Alih-alih sekadar menekan tombol uji otomatis di Mode Udara, siswa diundang menguji keempat kombinasi `00/01/10/11` lewat sakelar `Input A/B` dengan evaluasi rangkaian instan anti-*false-win*, dibimbing lencana pemandu live (`SAMBUNG KE OUTPUT`, `KURANGI GERBANG`, `CEK SEL MERAH`, `UJI SAKELAR (X/4)`, `LULUS 4/4`), *hint* 3 tingkat (`Petunjuk 0/3`), serta **`Rapor Penguasaan`** & **`Rapor CP`** terukur.

---

## 2. Arsitektur Dual-Mode AR

Aplikasi selalu memulai pada **Mode Kartu (v5)** dan menyediakan perpindahan instan melalui **Laci Menu Kompak** pojok kanan atas (`Mode: Udara (v7)` ↔ `Mode: Kartu (v5)`) dengan *Camera Handover* bersih (*Camera2-safe*).

| Aspek | Mode Kartu (v5 — Tangible AR) | Mode Udara (v7.5 — Air-Weaving Hologram AR) |
| :--- | :--- | :--- |
| **Mesin Pelacakan** | Vuforia Engine 11.4.4 (Image Targets) | MediaPipeUnityPlugin 0.16.3 (`HandLandmarker`, CPU) + `WebCamTexture` |
| **Kamera** | Kamera belakang | Kamera depan (cermin horizontal) / Webcam laptop |
| **Objek Interaksi** | 8 kartu fisik cetak (`Input A/B`, 5 Gerbang, `Output`) | Hologram 3D melayang bebas (*levitation* + *tilt* `±12°`) di ruang rakit (`AssemblyZone` 4 siku berbayangan) + Rak Parkir Atas |
| **Kendali Gerbang & Kabel** | Geser kartu fisik di meja (auto-snap kiri-ke-kanan) | 2-Tahap *Circle Summon* + *Open-5 Bloom*, **Cubit (`Pinch`)** kabel manual port-ke-port + *Pulsing Signal Packets*, **Kepal (`Fist`)** pindah/buang gerbang, **Telunjuk `#8`** sakelar Input |
| **Audio & Onboarding** | Audio PCM prosedural UI & evaluasi level | Synthesizer audio spasial PCM prosedural penuh (`24` cue + `2` hum anti-nyangkut) + Kartu Panduan Gestur 1 Halaman & Latihan Cepat 4 Langkah |
| **HUD, Pedagogi, & Pengujian** | 4 kapsul ramping bawah (`Input A/B`, `Uji`/`Lanjut`), *Live Boolean Chip*, `Rapor Penguasaan`, & Pod `2x2` miring `-48°` | **0 tombol gameplay (*Zero-Touch*)**, tombol `?` panduan di Bilah Atas, *Live Boolean Chip*, `Rapor Penguasaan`, Pod `2x2` menghadap kamera, evaluasi instan anti-*false-win*, *Auto-Win* `4/4`, & tahan kepal di Output untuk *Next Level* |
| **Mesin Evaluasi** | `CircuitManager.cs` (inti *tracking-agnostic* terpadu) | `CircuitManager.cs` (inti *tracking-agnostic* terpadu + dukungan kabel manual & *fan-out*) |

---

## 3. Core Gameplay Loop

1. **Pahami Misi & Onboarding:** Siswa membaca target logika pada Bilah Atas ramping (`72 px`) beserta batas `Kuota Gerbang` dan konteks label 3D tiap level. Saat pertama kali membuka Mode Udara, **Kartu Panduan Gestur 1 Halaman** tampil otomatis 1x dengan opsi **Latihan Cepat 4 Langkah** interaktif.
2. **Rakit Mesin 3D:**
   - Di **Mode Kartu (v5)**, susun kartu fisik dari kiri ke kanan (`Input A/B` → Gerbang Logika → `Output`) hingga saluran energi tersambung otomatis.
   - Di **Mode Udara (v7.5)**, panggil gerbang dengan **2-Tahap Circle Summon + Open-5 Bloom**, atur posisinya dengan **Kepal (`Fist`)** di dalam 4 siku `AssemblyZone`, lalu tarik/sambungkan kabel antar orb port dengan **Cubit (`Pinch` `#4+#8`)** disertai *Pulsing Signal Packets* dan audio spasial prosedural.
3. **Eksperimen Sinyal, Rumus Boolean Live, & Debugging Mandiri:**
   - Di kedua mode, ***Live Boolean Expression Chip*** menampilkan rumus aljabar Boolean aktual yang terhubung ke `Output` beserta nilai biner `[0]`/`[1]` secara real-time.
   - Di **Mode Udara (v7.5)**, sentuh sakelar `Input A` dan `Input B` dengan ujung **Telunjuk `#8`** (`75 ms` + *ripple*) untuk mengamati mekanisme *Open Cutaway*, aliran *Pulsing Signal Packets*, dan status 4 kotak biner `00/01/10/11` pada Pod `OutputTruthPodVisual` sambil dibimbing lencana pemandu live (`AirWeavingOutputTrigger`) dengan sinkronisasi evaluasi instan anti-*false-win*.
   - Di **Mode Kartu (v5)**, ubah sakelar via kapsul bawah/sentuh 3D/rotasi kartu lalu tekan `Uji Rangkaian` untuk pemindaian otomatis `00..11`.
4. **Auto-Win, Progresi, & Refleksi Penguasaan:** Begitu 4/4 sel hijau dan tidak melebihi kuota gerbang, siswa meraih 1–3 bintang (tercatat di **`Rapor Penguasaan`** & **`Rapor CP`**). Di Mode Udara, `LULUS 4/4` langsung memicu ***Auto-Win*** dan siswa cukup **menahan kepal (`Fist`) di diorama `Output`** untuk *Zero-Touch Next Level*.

---

## 4. Mekanik Interaksi Detail

### 4.1 Sistem Rangkaian Spasial & Visual Inti (`Assets/Scripts/Core/`)
- **8 Node Standar (`GateNode.cs`):** `Card_Input_A`, `Card_Input_B`, `Card_Gate_AND`, `Card_Gate_OR`, `Card_Gate_XOR`, `Card_Gate_NAND`, `Card_Gate_NOT`, dan `Card_Output`. Saat masuk Mode Udara, cincin port hantu bawaan kartu disembunyikan bersih agar hanya orb port hologram yang tampil.
- **Dual-Mode Routing (`CircuitManager.cs`):**
  - Mode Kartu (v5): auto-snap kiri-ke-kanan berbasis jarak (`maxSnapDistance = 3.0f`, jangkauan sumber Input `1.8x`), *Snap Lock VFX*, saluran energi berlapis, & *energy bolt*.
  - Mode Udara (v7.5): koneksi manual port-ke-port dengan *fan-out* multi-cabang dari `Port_Out` yang dirender seragam orb-ke-orb oleh `AirWeavingCableRouter`.
- **Mekanisme Terbuka (`GateMechanismVisual.cs`):** Konstruksi *Open Cutaway* reaktif pada 5 gerbang logika (termasuk *Power Core* internal pada `NOT` & `NAND`) yang bekerja identik di kedua mode.
- **Pod Tabel Kebenaran `2x2` (`OutputTruthPodVisual.cs`):** 4 kotak biner tegas (`00`, `01`, `10`, `11`) dengan warna status jelas (abu gelap = belum diuji, tepi sian = aktif saat ini, hijau = lolos, merah = salah). Miring `-48°` di Mode Kartu dan sejajar menghadap kamera di Mode Udara.

### 4.2 Mekanik Air-Weaving v7.5 (`Assets/Scripts/AirWeaving/`)
- **Tracking 1:1 Adaptif & Visual 3D Melayang (`AirWeavingController.cs`, `AirWeavingHologramVisual.cs`):**
  - Proyeksi tangan 21 sendi 1:1 dengan *smoothing* adaptif berbasis kecepatan (*One-Euro-style*, *zero-GC*) serta peredaman skeleton halus agar fokus visual tetap pada mesin 3D.
  - Hologram 3D mengambang bebas (*sinusoidal levitation* + *velocity tilt* `±12°`, tanpa cincin lantai & tanpa cincin port hantu), label 3D kontras tinggi, bayangan pada 4 siku sudut `AssemblyZone`, serta *reticle* otomatis tersembunyi saat tangan keluar kamera.
- **2-Tahap Circle Summon + Open-5 Bloom (`AirWeavingCircleSummoner.cs`, `AirWeavingSummonVisual.cs`):**
  - Putar ujung jari di ruang rakit dengan indikator pose live di ujung jari:
    - `1` jari → **`AND`**
    - `2` jari → **`OR`**
    - `3` jari → **`XOR`**
    - `4` jari (jempol lipat) → **`NAND`**
    - `Jempol saja` → **`NOT`**
  - Buka lebar **5 jari (*Open-5 Bloom*)** untuk *spawn* gerbang dari Rak Parkir Atas tepat di pusat lingkaran (atau kepal untuk batal instan; tampilkan *Notice + Pulse* amber non-destruktif bila jenis gerbang sudah ada di papan).
- **Manajemen Kabel Manual — Cubit / `Pinch` & *Pulsing Signal Packets* (`AirWeavingCableRouter.cs`, `AirWeavingHandGestureDetector.cs`):**
  - Cubit **Jempol `#4` + Telunjuk `#8`** (skala-invarian + *pre-pinch guard*) pada orb port (`Port_Out` / `Port_In`) untuk tarik, pasang, *fan-out* (dari `Port_Out`), cabut/ganti (dari `Port_In`), atau lepas di ruang kosong untuk putus.
  - Seluruh kabel Mode Udara dirender seragam orb-ke-orb menggunakan **pool 20 Bezier zero-GC** dengan warna sinyal live, diperkaya ***Pulsing Signal Packets* Zero-GC** (pool `60` paket, `1–3` paket adaptif per panjang kabel, 1 shared material + `MaterialPropertyBlock`) yang mengalir cepat-terang saat sinyal `1` dan pelan-redup saat sinyal `0`.
- **Manipulasi Objek — Kepal / `Fist` (`AirWeavingHandGestureDetector.cs`, `AirWeavingController.cs`):**
  - Kepal badan gerbang (*sticky hysteresis* + *occlusion grace* `180 ms`) untuk memindah posisi di dalam ruang rakit.
  - Tarik gerbang keluar dari 4 siku `AssemblyZone` (bingkai & aura menyala merah) lalu lepas untuk mengembalikannya ke Rak Parkir Atas sekaligus memutus seluruh kabelnya.
- **Sakelar Input A/B — Sentuh Telunjuk `#8` (`AirWeavingController.cs`):**
  - Sentuh ringan ujung telunjuk `#8` (`75 ms` dwell + *Exit-to-Rearm*) disertai efek *ripple* untuk membalik nilai `0 ↔ 1`.
- **100% Debugging Mandiri, Anti-*False-Win*, & *Zero-Touch Next Level* (`AirWeavingOutputTrigger.cs`, `OutputTruthPodVisual.cs`):**
  - Cincin Output ditempatkan di belakang bodi 3D (anti-clipping), dengan lencana pemandu live tepat di bawah Pod `00/01/10/11`: `SAMBUNG KE OUTPUT`, `KURANGI GERBANG`, `CEK SEL MERAH`, `UJI SAKELAR (X/4)`, `LULUS 4/4` → *Auto-Win* otomatis.
  - Sinkronisasi evaluasi rangkaian instan menjamin status Pod & lencana selalu mencerminkan kebenaran graf live (*anti-false-win*); tahan kepal pada diorama Output setelah lulus untuk pindah level tanpa menyentuh layar.
- **Audio Spasial Prosedural Penuh (`AirWeavingSpatialSfx.cs`):**
  - Synthesizer PCM prosedural 100% interaktif (*zero-GC*, 0 aset audio eksternal): **24 cue *one-shot*** (pool 6 `AudioSource` dengan pan stereo spasial), **2 *looping hum*** (seret kabel & seret gerbang dengan watchdog `LateUpdate` anti-nyangkut), ***soft limiter* `tanh`**, dan **cooldown independen per kategori**.
- **Onboarding Mode Udara (`AirWeavingGestureGuide.cs`):**
  - **Kartu Panduan Gestur 1 Halaman** (tampil otomatis 1x saat pertama masuk Mode Udara via `PlayerPrefs: GW_AirGuideSeen_v75`; dapat dibuka kapan saja lewat tombol `?` di Bilah Atas atau opsi `Panduan Gestur` di laci `≡`).
  - **Pil Latihan Cepat 4 Langkah** interaktif *real-time* (Zero-GC, non-intrusif di tengah-bawah layar): `(1)` sentuh sakelar Input, `(2)` summon gerbang, `(3)` sambungkan kabel, `(4)` buang gerbang ke luar `AssemblyZone`.

### 4.3 Pedagogi Live & Rapor Penguasaan (`Assets/Scripts/Levels/PedagogyMasteryHud.cs`)
- ***Live Boolean Expression Chip* (Zero-GC, Dual-Mode):**
  - Pil hologram ramping di kiri-atas tepat di bawah Bilah Atas `72 px` (bekerja identik di Mode Kartu v5 maupun Mode Udara v7.5, aman Safe Area Android 20:9).
  - Mengevaluasi graf rangkaian aktif menjadi rumus aljabar Boolean formal + hasil `[0]`/`[1]` (mendukung cabang belum lengkap `?` & guard anti-siklus) tanpa alokasi runtime lewat `StringBuilder` + `char[]` + `TMP_Text.SetCharArray`.
- **Modal `Rapor Penguasaan` (Laci `≡`):**
  - Merangkum progres resmi 7 level kurikulum dari `GW_v3_*`: total bintang **`X/21`**, jumlah level lulus **`Y/7`**, dan **predikat penguasaan**.
  - Mencatat statistik pedagogis terisolasi via `GW_v75_*`: counter ***Self-Debugging*** (perbaikan dari salah ke benar) serta pemisahan penyelesaian **Mode Kartu vs Mode Udara**, termasuk deteksi pasif saat progres direset.

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

## 6. Desain Visual, Audio, & UI/UX (`v7.5`)

- **Aset 3D & Material:** 12 model sci-fi *low-poly* **Quaternius (CC0)**, mekanisme terbuka *Open Cutaway* (`GateMechanismVisual`), Pod Tabel Kebenaran `2x2` (`OutputTruthPodVisual`), hologram melayang Mode Udara tanpa cincin port hantu & dengan cincin Output anti-clipping (`AirWeavingHologramVisual`, `AirWeavingOutputTrigger`), bayangan 4 siku `AssemblyZone`, peredaman skeleton halus, serta label kontekstual 3D kontras tinggi.
- **Audio & VFX:** Umpan balik audio PCM prosedural umum (`LevelAndUIManager`) ditambah synthesizer spasial 100% interaktif khusus Mode Udara (`AirWeavingSpatialSfx.cs`: 24 cue *one-shot* + 2 *looping hum* anti-nyangkut + *soft limiter* `tanh` + cooldown per kategori, dapat dimatikan via Laci Menu Kompak `Audio: ON/OFF`), *Pulsing Signal Packets* Zero-GC di seluruh kabel Bezier, serta *zero-GC* `AR_SparkBurstPool`.
- **Filosofi UI Diet — HUD Minimalis, Pedagogi Live, & Onboarding (`CompactHudMenuController.cs`, `PedagogyMasteryHud.cs`, `AirWeavingGestureGuide.cs`):**
  - **Atas:** 1 Bilah Atas ramping (`72 px`) berisi Judul Level, Misi 1 baris, Kuota Gerbang (berubah merah berkontras tinggi jika terlampaui), tombol `?` panduan gestur (khusus Mode Udara), dan tombol ikon 3 garis `≡` pojok kanan atas. Tepat di kiri-atas bawah Bilah Atas tampil ***Live Boolean Expression Chip*** ramping (aktif di Mode Kartu & Mode Udara).
  - **Laci Menu Kompak (`≡`):** Memuat `Mode: Udara (v7)` / `Mode: Kartu (v5)`, `Pilih Level / Sandbox`, `Reset Level`, `Rapor Penguasaan`, `Rapor CP`, `Petunjuk (0/3)`, `Audio: ON/OFF`, dan `Panduan Gestur` (khusus Mode Udara) + *scrim* klik-luar untuk menutup.
  - **Bawah:**
    - **Mode Udara (v7.5):** **0 tombol gameplay** (*100% Zero-Touch*), dengan **Pil Latihan Cepat 4 Langkah** non-intrusif opsional di tengah-bawah saat mode latihan aktif.
    - **Mode Kartu (v5):** 4 kapsul ramping semi-transparan (`Input A`, `Input B`, `Uji Rangkaian` / `Level Berikutnya`).
  - **Notifikasi (`Txt_Feedback`):** Mode *Toast-Only* berkontras tinggi (tersembunyi saat idle; gaya warna khusus untuk Gagal, Berhasil, & Petunjuk dengan prioritas aman serta pergeseran vertikal otomatis saat Pil Latihan aktif).

---

## 7. Backlog & Arah Pengembangan (`v7.x` / `v8.0` — Belum Diimplementasikan)

*(Bagian ini hanya daftar kandidat fitur masa depan; jangan diimplementasikan kecuali diminta eksplisit oleh pengguna.)*
- **v7.x (QA & Presentasi):**
  - *In-Game Debug Console* tersembunyi (pemicu ketuk 3 jari) untuk inspeksi FPS, log error, dan status kamera di HP Android.
  - Ikon aplikasi kustom (`Default Icon`) & *splash screen* GateWeaver AR.
  - *Editor-only Mouse Fallback* ringan di Mode Udara untuk pengujian cepat tanpa webcam.
- **v8.0 (Ekspansi Pedagogi & Interaksi):**
  - Tantangan rangkaian 3-Input (`A`, `B`, `C`) atau *Half-Adder / Full-Adder* (gerbang majemuk + 2 output `Sum` & `Carry`).
  - Ekspor ringkasan **Rapor Penguasaan / Rapor CP** siswa (CSV/PNG/kode sesi) untuk penilaian guru di kelas.
