# MdViewer

Aplikasi WPF .NET 9 (Windows) untuk menyunting dan melihat pratinjau Markdown. Lihat `README.md` untuk fitur dan pemakaian, dan `docs/README.md` untuk dokumentasi pengembangan (arsitektur, keputusan desain, keamanan, pengujian).

## Perintah

```powershell
dotnet build MdViewer.sln                 # harus 0 warning, 0 error
dotnet test src/MdViewer.Tests            # xUnit; semua harus hijau
dotnet run --project src/MdViewer -- file.md
dotnet publish src/MdViewer -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

## Struktur

- `src/MdViewer` - aplikasi. Titik masuk `App.xaml.cs` (single-instance, penangan galat global) lalu `MainWindow`.
  - `MainWindow` - tab, dialog (konflik, simpan, ukuran file), sesi/settings, menu. Satu `DocumentTab` per dokumen.
    `ChoiceDialog` - dialog kecil bertema dengan tombol berlabel (dipakai dialog konflik; bukan MessageBox Ya/Tidak/Batal).
  - `DocumentTab` - model dokumen: teks, path, encoding, hash/stempel file di disk, watcher perubahan eksternal, simpan.
  - `DocumentView` - editor AvalonEdit + pratinjau FlowDocument (Markdig.Wpf), sinkron scroll, `Dispose()` menghentikan timer.
  - `TextFileIO` - baca/tulis/deteksi encoding/penyimpanan atomik; `FileStamp` - ukuran + waktu tulis.
  - `MarkdownSupport` - pipeline Markdig (pratinjau `Pipeline`, ekspor `ExportPipeline`), allowlist URL, pemblokiran gambar.
  - `HtmlExporter` - ekspor HTML mandiri; `SingleInstance` - Mutex `Local\` + named pipe (nama memuat id sesi Windows);
    `AppSettings` - settings JSON.
  - `Themes/` - kamus warna Light/Dark dan gaya kontrol.
- `src/MdViewer.Tests` - xUnit. Test yang menyentuh WPF berjalan lewat `Support/WpfHost` (satu thread STA) dengan
  `[Collection("Wpf")]`; `Support/TempDir` untuk file sementara.
- `scripts/` - registrasi asosiasi file (HKCU, jangan dijalankan tanpa `-WhatIf` dulu) dan pembuat ikon.

## Konvensi

- Teks UI, pesan, dan komentar kode berbahasa Indonesia; ikuti gaya yang sudah ada. Nama identifier berbahasa Inggris.
- Nullable aktif, ImplicitUsings aktif; build tidak boleh menghasilkan warning.
- Komentar menjelaskan alasan (mengapa), bukan mengulang kode. Jangan menambah fitur di luar permintaan.
- Penyimpanan file selalu lewat `TextFileIO.Write` (atomik, mempertahankan encoding/BOM); jangan menulis file dokumen langsung.
- Pratinjau memakai `MarkdownSupport.Pipeline`; ekspor HTML wajib `ExportPipeline` + `SanitizeForExport` (HTML mentah di-escape,
  URL disaring). Jangan menyatukan keduanya.
- Gambar di pratinjau/cetak (`ResolveImageUrls`): hanya file lokal dan `http(s)` yang boleh sampai ke WPF; UNC, skema lain (`ftp:`, dll.)
  dan `data:` diganti teks penanda (WPF tak memuat `data:` lewat URI dan akan menggagalkan seluruh pratinjau; `http(s)` mengikuti
  opsi blokir remote). Allowlist ekspor terpisah (`ClassifyUrl`/`SanitizeForExport`): `data:image/*` sah tetap boleh di ekspor.
- Ekspor HTML menyematkan gambar lokal hanya bila di bawah folder dokumen (yang di luar diganti penanda; privasi), maks 2 MB per gambar,
  anggaran total `MaxTotalEmbeddedBytes` (30 MB, dihitung per kemunculan; setelah habis path relatif dibiarkan), cache per path lengkap.
  `ExportHtml_Executed` menangkap OOM dan galat umum dengan pesan ramah.
- Pembukaan dokumen hanya lewat `MainWindow.OpenFile` (cek ukuran, OOM, konflik tab ganda). Semua dialog konflik (perubahan eksternal
  dan konflik simpan) memakai `ChoiceDialog` dan diserialisasi lewat `conflictPromptOpen`/`conflictQueue`; jangan menampilkan
  `MessageBox`/dialog konflik langsung dari event. Muat ulang karena konflik adalah satu langkah Undo (`DocumentTab.Reload`), dan
  `DocumentTab.SaveTo` menunda pemeriksaan eksternal selama berjalan.
- Sesi: instance yang dimulai dengan argumen tidak menimpa sesi tersimpan sampai pengguna membuka tab lagi (kiriman instance lain, dialog
  Buka, seret-lepas, Berkas Terakhir) - lewat `OpenUserFile`; sesudahnya sesi disimpan seperti biasa.
- Galat I/O yang bisa dipulihkan ditangkap dan ditampilkan ke pengguna; galat tak terduga dicatat `CrashLog` (`%LOCALAPPDATA%\MdViewer\crash.log`).
- Test tidak boleh menyentuh/menulis `%APPDATA%`, `%LOCALAPPDATA%`, dan registri (membaca HKCU Personalize untuk tema diperbolehkan); crash.log pengguna tidak boleh tersentuh (log sudah dialihkan di `TestLogRedirect`); `SingleInstance.Create(scope)`
  memakai scope unik di test. Jangan menjalankan skrip registri sungguhan.
- Jangan commit atau push kecuali diminta.
