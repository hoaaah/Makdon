# Changelog

Semua perubahan penting pada MdViewer dicatat di berkas ini.
Format mengikuti [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), dan penomoran versi mengikuti
[Semantic Versioning](https://semver.org/).

> Catatan: properti `<Version>` di `src/MdViewer/MdViewer.csproj` disamakan dengan nomor rilis di berkas ini (0.1.0). Ubah keduanya
> bersama saat rilis berikutnya.
> Repo belum punya remote/tag, jadi tidak ada tautan perbandingan versi. Riwayat git baru satu commit (`448e1ad`); isi 0.1.0
> disusun dari kode, test, README, dan CLAUDE.md pada commit itu.

## [Unreleased]

### Security

- Klik tautan di pratinjau tidak lagi bisa memicu koneksi SMB/NTLM (risiko R1): tautan relatif ter-percent-encode seperti
  `%5C%5Chost%5Cshare%5Cx.md` atau `%2F%2Fhost%2Fshare%2Fx.md` didekode menjadi path UNC dan `File.Exists` dipanggil sebelum cek
  ekstensi. Resolusi path kini lewat `MarkdownSupport.ResolveLinkTarget` (tanpa I/O): ekstensi markdown dicek dulu, lalu
  `IsAllowedLocalPath` (UNC hanya di share yang sama dengan dokumen), path `\\?\`/`\\.\` ditolak, baru `File.Exists`.

### Fixed

- Tautan `file:///C:/x.md` absolut di pratinjau kini dibuka (sebelumnya string URL mentah digabung ke `Path.Combine` dan tidak pernah
  berhasil); `file://host/...` tetap ditolak.
- Properti `<Version>` di `MdViewer.csproj` disamakan menjadi `0.1.0` (sebelumnya `1.0.0`).

### Changed

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
- Satu instance per pengguna per sesi Windows (Mutex + named pipe); membuka `.md` saat MdViewer berjalan membukanya sebagai tab.
- Skrip asosiasi file `.md`/`.markdown` yang hanya menulis ke HKCU (`register-file-association.ps1`, `unregister-file-association.ps1`, mendukung `-WhatIf`) dan pembuat ikon.
- Penanganan galat global dengan catatan `%LOCALAPPDATA%\MdViewer\crash.log` (batas 512 KB); galat yang aman dilanjutkan, galat fatal menyimpan sesi lalu menutup aplikasi.
- Pengaturan di `%APPDATA%\MdViewer\settings.json` (tema, zoom, blokir gambar remote, berkas terakhir, sesi); file hilang/rusak kembali ke bawaan.
- Status bar: posisi kursor, jumlah kata/karakter, encoding, mode, zoom.
- Batas file: konfirmasi di atas 50 MB, penolakan di atas 500 MB.
- Proyek test xUnit (`src/MdViewer.Tests`), sub-agent proyek (`.claude/agents`), dan dokumentasi pengembangan di `docs/`.

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
