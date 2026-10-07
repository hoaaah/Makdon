# MdViewer

Editor dan pratinjau Markdown untuk Windows (WPF, .NET 9). Buka file `.md`, edit di kiri, lihat hasilnya langsung di kanan.

## Fitur

- Tiga mode tampilan: Editor, Terpisah (editor + pratinjau, scroll tersinkron), dan Pratinjau.
- Banyak tab, sesi dipulihkan saat dibuka lagi, daftar berkas terakhir, seret-lepas berkas ke jendela.
- Sorotan sintaks Markdown, tema Terang/Gelap/Ikuti Sistem, zoom (Ctrl+roda mouse).
- Cari dan ganti (teks biasa atau regex) dengan penanda hasil, Ganti Semua sebagai satu langkah Undo.
- Toolbar/pintasan format: tebal, miring, kode inline, heading, daftar, kutipan, tautan, gambar.
- Tautan relatif ke file Markdown lain dan `#anchor` heading (slug gaya GitHub) bekerja di pratinjau. Berkas yang diterima
  seret-lepas dan tautan relatif: `.md`, `.markdown`, `.mdown`, `.mkd`, dan `.txt` (asosiasi file hanya mendaftarkan
  `.md` dan `.markdown`). Tautan ke UNC (`\\host\...`) dan berkas non-Markdown tidak dibuka.
- Encoding dipertahankan saat menyimpan: UTF-8 (dengan/tanpa BOM), UTF-16, UTF-32, Windows-1252.
  File berisi byte tidak valid ditandai dan meminta konfirmasi sebelum disimpan.
- Penyimpanan atomik (file sementara lalu ganti). Bila file sudah diubah program lain sejak dibuka, simpan tidak
  menimpa diam-diam: dialog menawarkan **Timpa** (simpan versi editor), **Muat dari Disk** (ganti isi editor dengan
  versi di disk), atau **Batal**. Muat dari Disk adalah satu langkah Undo (Ctrl+Z mengembalikan isi editor), dan file
  dibaca ulang setelah dialog ditutup, bukan memakai isi saat konflik terdeteksi.
- Deteksi perubahan dari luar: tab bersih dimuat ulang otomatis (bisa di-Undo); tab yang punya perubahan menanyakan
  Anda satu per satu (**Muat dari Disk** atau **Pertahankan Editor**). Dialog konflik tidak pernah bertumpuk: konflik
  lain (termasuk dari simpan) menunggu giliran.
- Ekspor ke HTML mandiri dan cetak pratinjau.
- Satu instance per pengguna per sesi Windows: membuka file `.md` saat MdViewer sudah berjalan (di sesi yang sama)
  membukanya sebagai tab di jendela yang ada. Sesi Windows lain (mis. Remote Desktop) punya instance sendiri.

### Keamanan

- Ekspor HTML: HTML mentah di dalam Markdown di-escape; tautan hanya `http`, `https`, `mailto`, `#anchor`, atau path
  relatif; gambar hanya `http(s)`, `data:image/(png|jpeg|gif|webp)`, atau gambar lokal.
  - Gambar lokal png/jpg/gif/webp sampai 2 MB per gambar disematkan sebagai data URI (path mesin tidak pernah ikut ke
    HTML), **hanya bila berada di bawah folder dokumen**. Gambar di luarnya (path absolut ke folder lain, `../`,
    symlink yang menunjuk keluar) diganti teks `[gambar di luar folder dokumen tidak disertakan]` supaya file lain di
    mesin Anda tidak ikut terekspor. Dokumen yang belum disimpan (tanpa folder) tidak menyematkan gambar lokal apa pun.
  - Ada anggaran total penyematan 30 MB per ekspor (tiap kemunculan gambar dihitung, karena menambah ukuran HTML).
    Setelah habis, gambar berikutnya dibiarkan sebagai path relatif. Gambar yang sama dibaca dan dikodekan sekali.
    Bila memori tetap tidak cukup, ekspor dibatalkan dengan pesan, bukan menutup aplikasi.
- Pratinjau dan cetak hanya memuat file lokal dan `http(s)`. Gambar remote (`http`/`https`) diganti teks
  `[gambar remote diblokir]` secara bawaan agar dokumen tidak bisa melacak Anda; aktifkan lewat Tampilan > Muat gambar
  remote. Gambar ke share UNC (`\\host\...`, `file://host/...`) dan skema lain (`ftp:`, dll.) selalu diblokir tanpa
  koneksi jaringan apa pun. Gambar `data:` tidak ditampilkan di pratinjau (diganti penanda) karena WPF tidak
  mendukungnya, tetapi tetap disertakan di ekspor HTML bila tipenya sah (png/jpeg/gif/webp).

## Dokumentasi

Dokumentasi pengembangan (arsitektur, keputusan desain, kontribusi, keamanan, pengujian) ada di [docs/](docs/README.md).
Riwayat perubahan: [CHANGELOG.md](CHANGELOG.md).

## Pintasan keyboard

| Pintasan | Fungsi |
| --- | --- |
| Ctrl+N / Ctrl+O | Dokumen baru / buka |
| Ctrl+S / Ctrl+Shift+S | Simpan / Simpan Sebagai |
| Ctrl+W | Tutup tab |
| Ctrl+Tab / Ctrl+Shift+Tab | Tab berikutnya / sebelumnya |
| Ctrl+1 / Ctrl+2 / Ctrl+3 | Mode Editor / Terpisah / Pratinjau |
| Ctrl+F / Ctrl+H | Cari / Ganti |
| F3 / Shift+F3 | Cari berikutnya / sebelumnya |
| Ctrl+B / Ctrl+I / Ctrl+E | Tebal / Miring / Kode inline |
| Ctrl+Shift+L / Ctrl+Shift+Q | Daftar / Kutipan |
| Ctrl+K / Ctrl+Shift+I | Tautan / Gambar |
| Ctrl+= / Ctrl+- / Ctrl+0 | Perbesar / Perkecil / Zoom normal |
| Ctrl+Shift+E | Ekspor sebagai HTML |
| Ctrl+P | Cetak |
| Esc | Tutup panel cari |

## Build, test, publish

Butuh .NET 9 SDK.

```powershell
dotnet build MdViewer.sln
dotnet test src/MdViewer.Tests
dotnet run --project src/MdViewer -- contoh.md
```

Publish satu file (butuh .NET 9 Desktop Runtime di mesin tujuan):

```powershell
dotnet publish src/MdViewer -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

Hasil: `src\MdViewer\bin\Release\net9.0-windows\win-x64\publish\MdViewer.exe`.

## Asosiasi file (.md)

Hanya menulis ke HKCU, tanpa hak administrator:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\register-file-association.ps1 -WhatIf   # lihat dulu
powershell -ExecutionPolicy Bypass -File scripts\register-file-association.ps1
powershell -ExecutionPolicy Bypass -File scripts\unregister-file-association.ps1          # batalkan
```

Windows 10/11 melindungi pilihan aplikasi bawaan, jadi setelah mendaftar pilih MdViewer lewat klik kanan berkas >
Buka dengan > Pilih aplikasi lain (centang "Selalu gunakan"), atau di Pengaturan > Aplikasi > Aplikasi bawaan.
`-SetDefault` menulis nilai bawaan ekstensi di HKCU dan mencadangkan nilai lama ke
`HKCU\Software\MdViewer\PreviousDefault`; skrip unregister memulihkannya. `-Extensions` hanya menerima huruf kecil
dan angka (mis. `.md`).

## Lokasi data

| Data | Lokasi |
| --- | --- |
| Pengaturan (tema, zoom, berkas terakhir, sesi, blokir gambar remote) | `%APPDATA%\MdViewer\settings.json` |
| Catatan galat | `%LOCALAPPDATA%\MdViewer\crash.log` (bila lebih dari 512 KB, seluruh file dihapus lalu entri baru ditulis) |

Settings yang rusak atau hilang diabaikan (kembali ke bawaan). Saat keluar, daftar berkas terakhir digabung dengan isi
file di disk supaya beberapa instance tidak saling menimpa.

Aturan sesi: instance yang dibuka dengan argumen file (mis. klik dua kali `.md`) hanya berisi file itu, jadi pada
awalnya tidak menimpa sesi tersimpan. Begitu Anda membuka tab lagi di instance itu (file dikirim dari peluncuran
berikutnya, dialog Buka, seret-lepas, atau Berkas Terakhir), instance dianggap "diadopsi" menjadi ruang kerja biasa dan
saat keluar menyimpan sesinya seperti instance tanpa argumen.

## Batasan yang diketahui

- Hanya Windows (WPF). Pratinjau memakai Markdig.Wpf: HTML mentah di dokumen diabaikan di pratinjau, dan sebagian
  ekstensi Markdown mungkin tampil lebih sederhana daripada di ekspor HTML.
- File di atas 50 MB ditanyakan dulu sebelum dibuka; di atas 500 MB ditolak. Seluruh file dibaca ke memori.
  Ambang berdasarkan jumlah karakter, bertingkat: mulai 100 rb karakter pratinjau diurai di thread latar; mulai 200 rb
  jeda pembaruan pratinjau bertambah; mulai 1 juta jeda makin panjang dan statistik kata/karakter diperbarui lebih jarang.
- Deteksi perubahan file dari luar memakai FileSystemWatcher dan pemeriksaan saat jendela aktif kembali. File yang tidak
  berubah ukuran dan waktu tulisnya (dan sudah lebih dari 2 detik) tidak dibaca ulang. Share jaringan yang tidak
  mendukung watcher hanya terdeteksi saat jendela diaktifkan.
- Simpan menggunakan file sementara `~md########.tmp` di folder yang sama; bila folder tidak bisa ditulisi (akses
  ditolak) atau nama sementara terlalu panjang, menulis langsung ke file (tidak atomik; ditulis dari awal lalu
  dipotong). Galat I/O lain (mis. disk penuh) tidak dicoba lewat jalur itu dan dilaporkan.
- Single-instance memakai Mutex + named pipe per pengguna per sesi Windows. Bila pipe gagal, file dibuka di instance
  baru. File yang dikirim ke instance yang sedang menutup diabaikan.
- Peluncuran kedua tanpa argumen hanya mengaktifkan jendela yang sudah ada (tidak membuka jendela baru).
- Gambar lokal pada ekspor HTML yang bukan png/jpg/gif/webp, lebih dari 2 MB, di luar folder dokumen, atau melewati
  anggaran total 30 MB tidak disematkan.
- Pencarian regex yang terlalu lambat (batas 2 detik per kecocokan, 4 detik total) tidak diulang otomatis dengan pola
  yang sama; ubah pola atau opsi untuk mencoba lagi.
- Belum ada pemeriksa ejaan, penyimpanan otomatis, atau pemulihan draf untuk dokumen tanpa judul yang belum disimpan.
