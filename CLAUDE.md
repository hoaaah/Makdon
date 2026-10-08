# Makdon

Aplikasi WPF .NET 10 (Windows) untuk menyunting dan melihat pratinjau Markdown. Lihat `README.md` untuk fitur dan pemakaian, dan `docs/README.md` untuk dokumentasi pengembangan (arsitektur, keputusan desain, keamanan, pengujian, distribusi).

## Perintah

```powershell
dotnet build Makdon.sln                   # harus 0 warning, 0 error
dotnet test src/Makdon.Tests              # xUnit; semua harus hijau
dotnet run --project src/Makdon -- file.md
dotnet publish src/Makdon -p:PublishProfile=win-x64   # self-contained, folder (profil di Properties/PublishProfiles)
powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1   # build+test+publish+installer+zip+SHA256SUMS (artifacts\<versi>\)
```

## Struktur

- `src/Makdon` - aplikasi. Titik masuk `App.xaml.cs` (single-instance, penangan galat global) lalu `MainWindow`.
  - `MainWindow` - tab, dialog (konflik, simpan, ukuran file), sesi/settings, menu, menu Integrasi Explorer (portable), Tentang. Satu `DocumentTab` per dokumen.
    `ChoiceDialog` - dialog kecil bertema dengan tombol berlabel (dipakai dialog konflik; bukan MessageBox Ya/Tidak/Batal).
  - `DocumentTab` - model dokumen: teks, path, encoding, hash/stempel file di disk, watcher perubahan eksternal, simpan.
  - `DocumentView` - editor AvalonEdit + pratinjau FlowDocument (Markdig.Wpf), sinkron scroll, `Dispose()` menghentikan timer.
  - `TextFileIO` - baca/tulis/deteksi encoding/penyimpanan atomik; `FileStamp` - ukuran + waktu tulis.
  - `MarkdownSupport` - pipeline Markdig (pratinjau `Pipeline`, ekspor `ExportPipeline`), allowlist URL, pemblokiran gambar.
  - `HtmlExporter` - ekspor HTML mandiri; `SingleInstance` - Mutex `Local\` + named pipe (nama memuat id sesi Windows, dan scope untuk portable);
    `AppSettings` - settings JSON.
  - `AppPaths` - mode terpasang/portable (penanda `Makdon.portable` di samping exe) dan lokasi `settings.json`/`crash.log`;
    `AppInfo` - konstanta identitas (AppId installer, nama mutex, URL rilis, lisensi); `InstallerMutex` - mutex bernama tetap untuk installer (mode terpasang).
  - `RegistryStore` (`IRegistryStore`, `WindowsRegistryStore`) + `FileAssociation` - pendaftaran "Buka dengan" di HKCU untuk mode portable.
  - `PrintLayout.cs` (`PageLayout`, `PrintSnapshot`, `PrintSource`, `PrintService`), `HeaderFooterPaginator`, `PreviewBuild`, `PrintPreviewWindow` -
    Cetak (Ctrl+P) dan Pratinjau Cetak (Ctrl+Shift+P): snapshot teks tab -> parse -> FlowDocument cetak tema Terang -> paginasi + kaki halaman ->
    halaman XPS di memori untuk `DocumentViewer`; `PreviewBuild.Guard` menangkap semua galat, paket XPS dibersihkan setelah dispatcher idle.
  - `Themes/` - kamus warna Light/Dark dan gaya kontrol.
  - `Properties/PublishProfiles/win-x64.pubxml` - profil publish rilis.
- `src/Makdon.Tests` - xUnit. Test yang menyentuh WPF berjalan lewat `Support/WpfHost` (satu thread STA, `TestApp` tanpa `OnStartup`) dengan
  `[Collection("Wpf")]`; `Support/TempDir` untuk file sementara; `Support/FakeRegistryStore` untuk registri palsu.
- `scripts/` - registrasi asosiasi file (HKCU, jangan dijalankan tanpa `-WhatIf` dulu), `build-release.ps1` (pembangun rilis lokal, sama dengan CI), dan pembuat ikon.
- `installer/` - `Makdon.iss` (Inno Setup 6) dan `Languages/Indonesian.isl` (terjemahan tidak resmi).
- `.github/workflows/release.yml` - rilis otomatis pada tag `v*`.
- `LICENSE` (MIT), `THIRD-PARTY-NOTICES.txt` - ikut dalam setiap rilis.

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
- Cetak/Pratinjau Cetak: dokumen cetak tidak boleh memuat path lokal atau dokumen galat (jalur cetak melempar, `throwOnFailure`); test cetak tanpa
  printer fisik atau dialog sungguhan (`ShowPrintDialogForTests`), dan galat dispatcher yang diharapkan dibungkus `WpfHost.ExpectUnhandled()`.
- Galat I/O yang bisa dipulihkan ditangkap dan ditampilkan ke pengguna; galat tak terduga dicatat `CrashLog` (lokasi lewat `AppPaths`, lihat di bawah).
- Lokasi data (`settings.json`, `crash.log`) hanya lewat `AppPaths` (`SettingsPath`, `CrashLogPath`); jangan memanggil `Environment.GetFolderPath`
  langsung. Mode portable menulis ke `<folder exe>\data\` dan tidak pernah pindah diam-diam ke `%APPDATA%`.
- Registri "Buka dengan" hanya lewat `IRegistryStore` (`FileAssociation` tidak menyentuh `Microsoft.Win32.Registry` langsung). Test memakai
  `FakeRegistryStore`.
- Identitas installer: `AppId` GUID (`installer/Makdon.iss`, `AppInfo.InstallerAppId`) **tidak boleh berubah**; tabel registri di
  `docs/DISTRIBUTION.md` bagian 4.1 harus sama persis di `FileAssociation`, `installer/Makdon.iss`, dan `scripts/register-file-association.ps1`.
  Nama mutex installer (`Makdon.AppMutex`) juga harus sama di `AppInfo` dan `Makdon.iss`.
- Jangan menghapus switch `Switch.System.Windows.DisableXpsPackageBoundaryRestriction` (`Makdon.csproj` dan `Makdon.Tests.csproj`): tanpanya
  .NET 10 membuat Pratinjau Cetak selalu gagal. Jangan memasangnya lewat `AppContext.SetSwitch` di kode (WPF men-cache switch itu).
- Test tidak boleh menyentuh/menulis `%APPDATA%`, `%LOCALAPPDATA%`, dan registri (membaca HKCU Personalize untuk tema diperbolehkan); crash.log pengguna tidak boleh tersentuh (log sudah dialihkan di `TestLogRedirect`); `SingleInstance.Create(scope)`
  memakai scope unik di test. Jangan menjalankan skrip registri sungguhan.
- Jangan commit atau push kecuali diminta.
