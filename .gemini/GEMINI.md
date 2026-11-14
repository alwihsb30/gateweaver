# MASTER PROJECT CONTEXT: GateWeaver AR (v2.0)

## 1. Peran & Instruksi untuk AI Agent Utama
Anda adalah Lead AR Game Designer, Unity C# Architect (Vuforia Specialist), dan Instructional Designer. Tugas Anda adalah mengambil alih perancangan penuh, arsitektur sistem, penyempurnaan mekanik permainan, serta implementasi teknis untuk proyek game edukasi **GateWeaver AR** berdasarkan batasan konteks mutlak di bawah ini. Jangan keluar dari batasan ruang lingkup teknis yang telah ditetapkan.

---

## 2. Identitas & Konteks Pedagogis Proyek
- **Nama Proyek:** GateWeaver AR
- **Domain Materi:** Informatika — Gerbang Logika & Rangkaian Kombinasional (`AND`, `OR`, `NOT`, `XOR`, `NAND`).
- **Target Kurikulum:** Kurikulum Merdeka (SMP Fase D / SMA-SMK Kelas X Fase E), Elemen Sistem Komputer (SK) dan Berpikir Komputasional (BK).
- **Masalah Pembelajaran (Observasi):** Siswa kesulitan memvisualisasikan secara konkret bagaimana perubahan nilai biner (`0` dan `1`) pada input merambat secara simultan melewati berlapis-lapis gerbang logika menuju output.
- **Target Keterampilan Abad 21 (4C):**
  1. *Critical Thinking:* Menganalisis tabel kebenaran dan mendiagnosis letak kesalahan logika pada rangkaian.
  2. *Collaboration:* Berbagi peran spasial di meja kelas (penata kartu fisik vs. pemegang perangkat AR/pengawas skenario).
  3. *Communication:* Mendiskusikan strategi penyusunan dan pertukaran posisi kartu secara real-time.
  4. *Creativity:* Mengoptimasi rangkaian agar mencapai output target dengan jumlah kartu gerbang seminimal mungkin (efisiensi komponen).

---

## 3. Batasan Desain & Teknis Mutlak (Hard Constraints)
1. **Tenggat Waktu Produksi:** Maksimal 2 minggu (14 hari). Arsitektur harus modular, bersih, minim risiko bug, dan realistis diselesaikan oleh mahasiswa secara mandiri/tim kecil.
2. **Tech Stack:** Unity Engine (3D) + Vuforia Engine SDK (Target platform: Android).
3. **Anti-Template:** DILARANG merancang kuis pilihan ganda AR, flashcard 3D statis, labirin biasa, atau platformer generik. Interaksi wajib mengandalkan manipulasi fisik kartu marker di ruang nyata (*Tangible Spatial AR*).
4. **Strategi Pelacakan AR (Zero Ground-Plane Risk):**
   - DILARANG menggunakan *Ground Plane / SLAM / Cloud Anchors / Multi-device Networking* karena rentan gagal akibat pantulan lantai kelas dan memakan waktu debugging.
   - WAJIB menggunakan **Simultaneous Image Targets** (melacak 6 hingga 8 kartu marker fisik secara bersamaan di atas meja).
5. **Spesifikasi Marker Fisik:**
   - Jumlah: 8 kartu cetak ukuran `8 cm x 8 cm` (`0.08m` di Vuforia), kertas matte/doff, kontras tinggi, asimetris 4 sudut (Rating 5 bintang Vuforia).
   - Komposisi Kartu: 2 Kartu `Input Source` (`Input A`, `Input B`), 5 Kartu `Gate Node` (`AND`, `OR`, `XOR`, `NAND`, `NOT`), dan 1 Kartu `Output Actuator`.

---

## 4. Mekanik Inti Spasial (Core Gameplay Loop)
1. **Spatial Proximity Snapping & Port Routing:**
   - Siswa menyusun kartu fisik di atas meja dari kiri ke kanan.
   - Sistem membaca posisi seluruh kartu yang terdeteksi kamera (`isTracked`), mengurutkannya berdasarkan sumbu X lokal kamera (`Camera.main.transform.InverseTransformPoint`), dan otomatis menghubungkan kartu yang berjarak kurang dari batas ambang (`maxSnapDistance`, misal 15–18 cm) menggunakan `LineRenderer` (jalur laser).
   - Setiap gerbang memiliki kuota input ketat (`NOT` dan `OutputActuator` = 1 input; `AND`, `OR`, `XOR`, `NAND` = 2 input).
2. **Touchless Physical Rotation Input & Real-Time Propagation:**
   - Nilai `Input A` dan `Input B` diubah secara fisik tanpa menyentuh layar dengan memutar orientasi kartu di atas meja (orientasi tegak/0° = `0/False`, diputar miring/90° terhadap kamera = `1/True`), disertai opsi tombol fallback di UI untuk pengujian di Unity Editor.
   - Sinyal biner dievaluasi secara berurutan dari kiri ke kanan: Sinyal `1` (`True`) memancarkan laser hijau menyala; sinyal `0` (`False`) memancarkan warna merah redup.
3. **Automated Truth-Table Verification (Kondisi Menang Anti-Tebakan):**
   - Pemain tidak bisa menang hanya dengan menyalakan output `1` secara kebetulan pada satu keadaan input.
   - Ketika rangkaian terhubung penuh dari Input ke Output dan memenuhi batas kuota kartu level tersebut, sistem menjalankan *Auto-Sweep Verification* (menguji kombinasi input `00`, `01`, `10`, `11` secara otomatis atau memvalidasi fungsi boolean rangkaian) sebelum membuka kunci aktuator 3D di kartu `Output`.

---

## 5. Kontrak Arsitektur Kode & Penanganan Edge-Case (WAJIB DIPATUHI AI)
Seluruh skrip baru yang Anda tulis WAJIB kompatibel dengan struktur dasar yang sudah ada:
1. **Kontrak `GateNode.cs`:**
   - Enum: `public enum NodeType { InputSource, AND, OR, NOT, XOR, NAND, OutputActuator }`
   - Properti Utama: `public NodeType nodeType`, `public bool isTracked`, `public bool currentOutputValue`, `public List<GateNode> connectedInputs`, `public GateNode connectedTarget`.
   - Metode Utama: `SetTrackingState(bool tracked)`, `EvaluateGateLogic()`, `UpdateVisuals()`.
2. **Kontrak `CircuitManager.cs`:**
   - Mengelola `public List<GateNode> allNodes`, `public float maxSnapDistance = 0.18f`, pengurutan spasial kiri-ke-kanan, dan propagasi sinyal.
3. **Aturan Anti-Flicker (Tracking Loss Debounce):**
   - Saat tangan siswa memutar atau menggeser kartu, kamera dapat kehilangan jejak marker selama sepersekian detik. Terapkan *grace period / debounce timer* (`~0.4f - 0.5f` detik) sebelum benar-benar memutus status `isTracked = false` agar garis laser tidak berkedip liar.
4. **Standar Output Teknis:**
   - Setiap memberikan kode C#, sertakan langkah konkret pemasangan komponen di Hierarchy/Inspector Unity Editor, hindari dependensi pihak ketiga berbayar, dan gunakan `TextMeshPro` untuk seluruh teks UI/3D.

---

## 6. Fokus Tugas AI Agent Selanjutnya (Backlog Eksekusi)
1. **Sistem Validasi Tabel Kebenaran Otomatis & Level Data (`LevelManager.cs`):** Merancang struktur data level (target tabel kebenaran 4-baris: `00`, `01`, `10`, `11` + batas maksimal kartu gerbang) beserta *Coroutine* simulasi pengujian beruntun yang menampilkan umpan balik baris demi baris di layar.
2. **Penyempurnaan `GateNode.cs` (Rotasi 90° & Debounce):** Menambahkan deteksi sudut rotasi lokal kartu terhadap kamera untuk mengubah nilai `InputSource` secara otomatis, lengkap dengan *tracking loss debounce*.
3. **State Machine, UI/UX & Audio/Visual Feedback:** Merancang alur status permainan (`Scanning/Building` -> `Validating Sweep` -> `Level Complete / Over-Quota Warning`) beserta tata letak HUD tabel kebenaran di layar.
