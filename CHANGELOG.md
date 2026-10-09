# Changelog

Semua perubahan penting pada Makdon dicatat di berkas ini.
Format mengikuti [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), dan penomoran versi mengikuti
[Semantic Versioning](https://semver.org/).

> Catatan: properti `<Version>` di `src/Makdon/Makdon.csproj` disamakan dengan nomor rilis di berkas ini (0.1.0). Ubah keduanya
> bersama saat rilis berikutnya.
> Repo belum punya remote/tag, jadi tidak ada tautan perbandingan versi. Riwayat git baru dua commit (`448e1ad`, `b2a35be`); isi 0.1.0
> disusun dari kode, test, README, dan CLAUDE.md pada commit pertama.

## [Unreleased]

### Added

- **Installer Windows** (`installer/Makdon.iss`, Inno Setup 6): pasang per pengguna (bawaan, tanpa UAC) atau untuk semua pengguna (meminta admin);
  Start Menu; "Buka dengan" didaftarkan otomatis; downgrade ditolak; uninstall menanyakan penghapusan pengaturan dan catatan galat (bawaan Tidak),
  dan tidak menghapus apa pun dengan `/SILENT`. Bahasa Indonesia dan Inggris.
- **Mode portable**: folder dengan penanda `Makdon.portable` di samping exe menyimpan `settings.json` dan `crash.log` di `data\`. Zip portable
  dibuat oleh `scripts/build-release.ps1`.
- **Integrasi Explorer (portable)**: Berkas > Integrasi Explorer > Daftarkan ke "Buka dengan" / Cabut pendaftaran (HKCU). Menolak bila versi
  terpasang ada; meminta konfirmasi sebelum mengganti pendaftaran exe lain; menawarkan memperbarui path yang basi saat startup.
- **Bantuan > Tentang Makdon**: versi, mode, lisensi MIT, dan tautan halaman rilis. Makdon tidak memeriksa pembaruan sendiri.
- **Rilis**: profil publish `win-x64` (self-contained, folder), `scripts/build-release.ps1` (build 0 warning, test, publish, installer,
  zip, `SHA256SUMS.txt`), dan `.github/workflows/release.yml` (dipicu push ke branch `build`, tag `v<versi>` dibuat otomatis, attestasi asal-usul build, draft lalu publikasi).
- **LICENSE** (MIT) dan **THIRD-PARTY-NOTICES.txt**; keduanya ikut dalam setiap rilis.

- **Pratinjau Cetak**- **Pratinjau Cetak** (Ctrl+Shift+P, Berkas > Pratinjau Cetak..., tombol toolbar; `AppCommands.PrintPreview`): jendela modal berisi halaman cetak yang
  disusun async (paginasi FlowDocument, lalu halaman ditulis ke paket XPS di memori untuk `DocumentViewer`) dengan pilihan orientasi (potret/lanskap),
  kertas (A4/Letter), margin (Sempit 0,5"/Normal 0,75"/Lebar 1"), kotak kaki halaman, navigasi halaman, zoom (Satu halaman/Lebar halaman/100%, +/-, Ctrl+roda),
  dan tombol Cetak... yang mencetak halaman yang tampil. Dokumen diambil sebagai snapshot saat dibuka (penyuntingan sesudahnya tidak mengubahnya),
  di-parse sekali dan dipakai ulang tiap ganti pengaturan (di thread latar untuk teks >= 100 rb karakter). Bila kertas/orientasi di dialog Cetak berbeda dari
  pratinjau, pengguna ditanya "Cetak sesuai pratinjau" atau "Batal".
- Kaki halaman pada Cetak dan Pratinjau Cetak: nama berkas di kiri dan "Halaman X dari N" di kanan, digambar di dalam margin bawah (tidak mengubah paginasi isi)
  dan tidak lebih dekat dari 0,25" ke tepi kertas untuk preset margin.
- Infrastruktur test: `Support/PrintTestKit`, pencatat galat dispatcher di `WpfHost` (`WpfHost.Unhandled`, `ExpectUnhandled()`,
  `[assembly: FailOnUnexpectedDispatcherErrors]`), dan `WpfHost.Run` yang tidak lagi menggantung bila galat pecah saat dispatcher dipompa. 155 atribut test baru
  di 7 berkas (total 1455 kasus lulus). `TestResults/` ditambahkan ke `.gitignore`.

### Security

- Pendaftaran "Buka dengan" setelah review: perintah relatif tidak lagi dinormalkan terhadap folder kerja (dianggap exe lain, tidak dihapus "Cabut"); path UNC
  tidak diperiksa dengan `File.Exists` (tidak dianggap basi, instalasi UNC diabaikan); `Register` menolak exe yang bukan `Makdon.exe` atau tidak ada.
- Rilis: action CI disematkan ke SHA commit; `build-release.ps1 -VerifyInstallerContents` hanya di CI (atau `-Force`) dan menolak bila instalasi ada.
- Pendaftaran "Buka dengan" hanya di HKCU (portable) atau lewat installer; satu tabel registri untuk aplikasi, installer, dan skrip; tidak ada
  nilai bawaan ekstensi atau `UserChoice` yang disentuh.
- Penanda `Makdon.portable` tidak ikut installer, dihapus dari bahan zip, dan diperiksa oleh installer (`#if FileExists`) dan skrip rilis.
- Dokumen galat tidak lagi tercetak: bila render gagal, Cetak (Ctrl+P) sebelumnya dapat mencetak dokumen galat yang memuat jalur `crash.log` (path profil pengguna)
  dan pesan galat; kini jalur cetak/pratinjau cetak melempar galat berpesan ramah tanpa path (`CreateFlowDocument(throwOnFailure: true)`) dan pratinjau utama tetap
  menampilkan dokumen galat di layar.
- Kaki halaman hanya memuat nama berkas (bukan path). Gambar remote/UNC/`file://host`/`ftp:`/`data:` tetap diblokir pada dokumen cetak dan Pratinjau Cetak
  (test baru memakai listener TCP lokal untuk membuktikan tidak ada koneksi); flag blokir remote diambil dari snapshot.

- Klik tautan di pratinjau tidak lagi bisa memicu koneksi SMB/NTLM (risiko R1): tautan relatif ter-percent-encode seperti
  `%5C%5Chost%5Cshare%5Cx.md` atau `%2F%2Fhost%2Fshare%2Fx.md` didekode menjadi path UNC dan `File.Exists` dipanggil sebelum cek
  ekstensi. Resolusi path kini lewat `MarkdownSupport.ResolveLinkTarget` (tanpa I/O): ekstensi markdown dicek dulu, lalu
  `IsAllowedLocalPath` (UNC hanya di share yang sama dengan dokumen), path `\\?\`/`\\.\` ditolak, baru `File.Exists`.

### Fixed

- Pratinjau Cetak gagal di .NET 10 karena pembatasan batas paket XPS (font tidak bisa dimuat dari paket XPS di memori). Diperbaiki dengan
  switch `Switch.System.Windows.DisableXpsPackageBoundaryRestriction` di runtimeconfig (ADR-32).
- Test WPF sebelumnya menjalankan `App.OnStartup` sungguhan (mutex/pipe produksi, `MainWindow`, settings `%APPDATA%`); host test kini memakai `TestApp`
  tanpa `OnStartup` (ADR-33).
- Pratinjau Cetak: tombol Sebelumnya dari halaman terakhir melompati satu halaman (atau tak berefek) karena gulir mentok sebelum halaman tujuan mencapai
  puncak; navigasi kini relatif terhadap halaman yang dilaporkan kotak halaman (ditemukan selama pengembangan fitur, belum pernah dirilis).
- Cetak (Ctrl+P): ukuran halaman dari dialog Cetak tidak divalidasi (NaN, tak hingga, sangat kecil/besar langsung dipakai); kini ukuran di luar 200-96.000 DIP
  jatuh ke A4 potret (`PageLayout.FromPrintableArea`).
- Tautan `file:///C:/x.md` absolut di pratinjau kini dibuka (sebelumnya string URL mentah digabung ke `Path.Combine` dan tidak pernah
  berhasil); `file://host/...` tetap ditolak.
- Properti `<Version>` di `Makdon.csproj` disamakan menjadi `0.1.0` (sebelumnya `1.0.0`).

### Changed

- **Target .NET 10** (`net10.0-windows`, SDK 10 untuk build); .NET 9 berhenti didukung 2026-11-10. Mode terpasang tetap memakai
  `%APPDATA%\Makdon\settings.json` dan `%LOCALAPPDATA%\Makdon\crash.log`; lokasinya kini lewat `AppPaths`.
- Single-instance: portable memakai scope per folder exe (tidak meneruskan berkas ke instance terpasang atau portable lain di folder berbeda).
  Mode terpasang membuat mutex bernama tetap `Makdon.AppMutex` untuk installer.
- README: instalasi (installer, portable), persyaratan Windows, peringatan SmartScreen/Smart App Control dan verifikasi checksum, migrasi dari
  skrip, pembaruan, uninstall, lokasi data per mode, dan lisensi. Dokumen pengembangan diperbarui (lihat `docs/README.md`).
- **Aplikasi dinamai Makdon** (sebelumnya bernama MdViewer). Berubah: folder dan proyek (`src/Makdon`, `src/Makdon.Tests`, `Makdon.sln`,
  `Makdon.csproj`), nama assembly dan namespace (`Makdon`), berkas hasil publish (`Makdon.exe`), judul jendela ("... — Makdon") dan caption dialog,
  serta dokumentasi dan skrip. Perilaku tidak berubah.
- Nama yang dipakai di mesin pengguna ikut berubah, sehingga data lama **tidak dimigrasikan** (aplikasi belum pernah dirilis; versi 0.1.0 belum ditag):
  pengaturan kini di `%APPDATA%\Makdon\settings.json` (sebelumnya `%APPDATA%\MdViewer`), catatan galat di `%LOCALAPPDATA%\Makdon\crash.log`
  (sebelumnya `%LOCALAPPDATA%\MdViewer`), Mutex `Local\Makdon.SingleInstance.<SID>` dan pipe `Makdon.<SID>.s<sesi>`, header pesan pipe `MAKDON1`,
  serta pendaftaran file di HKCU (ProgID `Makdon.Markdown`, `Applications\Makdon.exe`, `HKCU\Software\Makdon`). Pengaturan, sesi, dan log lama di
  folder `MdViewer` tidak dibaca lagi (hapus manual bila tidak diperlukan). Instance versi lama dan baru tidak saling mengenali, jadi tutup yang lama
  sebelum membuka yang baru. `unregister-file-association.ps1` juga membersihkan sisa pendaftaran `MdViewer` bila ada.
- Cetak (Ctrl+P): margin halaman 0,75" (sebelumnya 0,5"), kaki halaman, ukuran halaman dari dialog Cetak lewat `PageLayout`, pembuatan dokumen dari snapshot lewat
  `PrintService`; tooltip tombol toolbar "Cetak pratinjau (Ctrl+P)" menjadi "Cetak (Ctrl+P)". Menu Berkas dan toolbar mendapat Pratinjau Cetak.
- Ctrl+P saat fokus di panel pratinjau utama kini ikut jalur cetak aplikasi (tema Terang, margin, kaki halaman); sebelumnya pengikatan bawaan
  `FlowDocumentScrollViewer` mendahului pengikatan jendela.
- Dokumentasi diperbarui untuk Pratinjau Cetak: ARCHITECTURE (komponen, diagram urutan, siklus hidup paket XPS, model state `PreviewBuild`, koreksi klaim "satu-satunya
  `Task.Run`" dan "cetak sinkron"), ADR-19 sampai ADR-26, SECURITY (2.12, R8), TESTING, CONTRIBUTING, README (fitur, pintasan, Batasan termasuk kinerja Markdown sangat besar).
- Dokumentasi dikoreksi terhadap kode: penanganan `OutOfMemoryException` pada render sinkron vs latar, percobaan baca file terkunci
  (hingga 5), jalur penulisan `crash.log`, README (`crash.log` dihapus seluruhnya bila > 512 KB, ambang karakter bertingkat, ekstensi
  `.txt`), dan CLAUDE.md (pembacaan registri tema oleh test diperbolehkan).

## [0.1.0] - 2026-10-07

Rilis awal.

### Added

- Editor dan pratinjau Markdown untuk Windows (WPF, .NET 9; editor AvalonEdit, pratinjau Markdig.Wpf).
- Tiga mode tampilan per tab: Editor, Terpisah (scroll tersinkron), dan Pratinjau (Ctrl+1/2/3).
- Banyak tab, pemulihan sesi (path, mode, posisi caret, tab aktif), daftar berkas terakhir (maks 10), seret-lepas berkas.
- Sorotan sintaks Markdown; tema Terang, Gelap, dan Ikuti Sistem (kunci warna identik, kontras sintaks dijaga >= 4,5:1); zoom 50-300% (Ctrl+roda mouse).
- Cari dan ganti (teks biasa atau regex, sadar CRLF) dengan penanda hasil; Ganti Semua sebagai satu langkah Undo.
- Toolbar dan pintasan format: tebal, miring, kode inline, heading, daftar, kutipan, tautan, gambar (tiap operasi satu langkah Undo).
- Tautan relatif ke file Markdown lain dan `#anchor` heading (slug gaya GitHub) di pratinjau.
- Encoding dipertahankan saat menyimpan: UTF-8 (dengan/tanpa BOM), UTF-16, UTF-32, Windows-1252; file ber-BOM dengan byte tidak valid
  ditandai dan meminta konfirmasi sebelum disimpan.
- Penyimpanan atomik (file sementara lalu ganti) dengan cadangan tulis langsung bila folder tak bisa ditulisi.
- Deteksi perubahan file dari luar (watcher + pemeriksaan saat jendela aktif): tab bersih dimuat ulang otomatis; tab kotor menanyakan
  satu per satu lewat dialog berlabel; konflik saat menyimpan menawarkan Timpa, Muat dari Disk, atau Batal. Muat ulang adalah satu langkah Undo.
- Ekspor ke HTML mandiri dan cetak pratinjau.
- Satu instance per pengguna per sesi Windows (Mutex + named pipe); membuka `.md` saat Makdon berjalan membukanya sebagai tab.
- Skrip asosiasi file `.md`/`.markdown` yang hanya menulis ke HKCU (`register-file-association.ps1`, `unregister-file-association.ps1`, mendukung `-WhatIf`) dan pembuat ikon.
- Penanganan galat global dengan catatan `%LOCALAPPDATA%\Makdon\crash.log` (batas 512 KB); galat yang aman dilanjutkan, galat fatal menyimpan sesi lalu menutup aplikasi.
- Pengaturan di `%APPDATA%\Makdon\settings.json` (tema, zoom, blokir gambar remote, berkas terakhir, sesi); file hilang/rusak kembali ke bawaan.
- Status bar: posisi kursor, jumlah kata/karakter, encoding, mode, zoom.
- Batas file: konfirmasi di atas 50 MB, penolakan di atas 500 MB.
- Proyek test xUnit (`src/Makdon.Tests`), sub-agent proyek (`.claude/agents`), dan dokumentasi pengembangan di `docs/`.

### Security

- Ekspor HTML: HTML mentah di-escape (pipeline ekspor terpisah dari pratinjau; `GenericAttributes` dan `MediaLink` dimatikan); tautan hanya
  `http`, `https`, `mailto`, `#anchor`, atau path relatif; gambar hanya `http(s)`, `data:image/(png|jpeg|gif|webp)`, atau gambar lokal; judul di-escape.
- Ekspor HTML: gambar lokal png/jpg/gif/webp sampai 2 MB disematkan sebagai data URI **hanya bila di bawah folder dokumen**; yang di luar
  (absolut, `../`, symlink keluar) diganti teks penanda sehingga path dan file lain di mesin tidak ikut terekspor; anggaran total 30 MB per ekspor.
- Pratinjau dan cetak: gambar remote (`http(s)`) diblokir secara bawaan (opsi Tampilan > Muat gambar remote); UNC/SMB (`\\host\...`,
  `file://host/...`) dan skema lain (`ftp:`, dll.) selalu diblokir tanpa koneksi jaringan; gambar `data:` diganti penanda di pratinjau.
- Single-instance: pipe `CurrentUserOnly`, nama memuat SID pengguna dan id sesi Windows; hanya path absolut (maks 64, batas ukuran pesan dan waktu baca) yang diterima.
- Pencarian regex dibatasi waktu (2 dtk per kecocokan, 4 dtk total) dan jumlah hasil; pola yang timeout tidak diulang otomatis.
- Klik tautan di pratinjau hanya menyerahkan `http`/`https`/`mailto` ke shell; file lokal hanya dibuka bila berupa file Markdown.
- Skrip registri hanya menulis ke HKCU, memvalidasi ekstensi, dan mencadangkan nilai bawaan lama.

### Fixed

Perbaikan yang tercatat sebagai komentar/test regresi di kode rilis awal:

- Gambar `data:` di pratinjau tidak lagi menggagalkan seluruh pratinjau (WPF `BitmapImage(Uri)` tidak mengenal skema itu); diganti penanda.
- Gambar lokal yang ada tetapi tidak bisa di-decode tidak lagi menjatuhkan render pratinjau; diganti `[gambar tidak dapat ditampilkan: ...]`
  dengan teks alt tepat satu kali.
- Batas waktu total pencarian regex kini diperiksa pada setiap iterasi (sebelumnya baru terdeteksi pada iterasi ke-64).
