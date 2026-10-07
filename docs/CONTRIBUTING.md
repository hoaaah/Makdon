# Panduan Kontribusi

**Tujuan:** cara menyiapkan lingkungan, membangun/menguji/mem-publish MdViewer, konvensi kode, cara menulis test, cara menambah
hal-hal umum (tema, perintah, mode tampilan, ekstensi, format toolbar), checklist PR, dan larangan proyek.
**Pembaca:** kontributor baru dan agen AI yang mengubah kode. Aturan resmi proyek ada di [../CLAUDE.md](../CLAUDE.md); dokumen ini
merangkum dan menambah langkah praktis. Gambaran arsitektur: [ARCHITECTURE.md](ARCHITECTURE.md). Pengujian: [TESTING.md](TESTING.md).

## 1. Setup

- Windows (WPF) dan **.NET 9 SDK** (README: "Butuh .NET 9 SDK"). Tidak ada dependensi alat lain; paket NuGet dipulihkan otomatis oleh
  `dotnet build`. Proyek memakai `net9.0-windows` (`src/MdViewer/MdViewer.csproj`), jadi tidak bisa dibangun di Linux/macOS.
- Tidak ditemukan konfigurasi CI atau analyzer/`.editorconfig` di repo; penjaga kualitas saat ini adalah aturan "0 warning" dan test
  (lihat bagian 6).

```powershell
dotnet build MdViewer.sln                 # harus 0 warning, 0 error
dotnet test src/MdViewer.Tests            # xUnit; semua harus hijau
dotnet run --project src/MdViewer -- file.md
dotnet publish src/MdViewer -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

Hasil publish: `src\MdViewer\bin\Release\net9.0-windows\win-x64\publish\MdViewer.exe` (butuh .NET 9 Desktop Runtime di mesin
tujuan). `RuntimeIdentifier` sengaja tidak dipaksa di csproj agar build/test biasa tetap netral (komentar `MdViewer.csproj:15-19`).
Properti `<Version>` saat ini `0.1.0` (`MdViewer.csproj:12`), sama dengan rilis awal di [../CHANGELOG.md](../CHANGELOG.md). Ubah keduanya bersama saat rilis.

## 2. Struktur folder

```text
MdViewer.sln
CLAUDE.md, README.md, CHANGELOG.md
docs/                       dokumentasi pengembangan (indeks: docs/README.md)
scripts/                    register/unregister-file-association.ps1 (HKCU), generate-icon.ps1
.claude/agents/             sub-agent proyek (kuli, tyas, kurang-kerjaan, pak-bos)
src/MdViewer/               aplikasi WPF
    App.xaml(.cs)           titik masuk, single-instance, penangan galat global
    MainWindow.xaml(.cs)    tab, dialog, sesi, menu, ekspor, cetak
    DocumentTab.cs          model dokumen (teks, path, encoding, watcher, simpan)
    DocumentView.xaml(.cs)  editor + pratinjau + sinkron scroll
    FindReplaceBar.xaml(.cs), SearchEngine.cs, MarkdownEditing.cs
    TextFileIO.cs, FileStamp.cs, AppSettings.cs, SingleInstance.cs, CrashLog.cs
    MarkdownSupport.cs, AnchorHeadingRenderer.cs, HtmlExporter.cs
    Theming.cs, EditorTheme.cs, Themes/{Light,Dark,Controls,Preview}.xaml
    AppCommands.cs, ChoiceDialog.xaml(.cs), Converters.cs, ViewModeConverter.cs, NotNullConverter.cs
    ZoomLevel.cs, TextStats.cs, EncodingNames.cs, MarkdownFiles.cs, Assets/app.ico
src/MdViewer.Tests/         xUnit
    Support/                WpfHost.cs, TempDir.cs, TestLogRedirect.cs
    *Tests.cs               peta berkas -> area ada di TESTING.md
```

`bin/`, `obj/`, `.vs/`, `*.user` diabaikan git (`.gitignore`).

## 3. Konvensi kode dan bahasa

Dari CLAUDE.md, dilengkapi pola yang konsisten terlihat di kode:

- **Bahasa:** teks UI, pesan galat, dan komentar kode berbahasa **Indonesia**; nama identifier berbahasa **Inggris**.
- **Nullable dan ImplicitUsings aktif**; build tidak boleh menghasilkan warning (`System.IO` ditambahkan lewat `<Using>` di csproj).
- **Komentar menjelaskan alasan (mengapa)**, bukan mengulang kode. Contoh gaya: komentar di atas keputusan non-obvious seperti
  `DocumentTab.cs:33-36`, `TextFileIO.cs:145`.
- **Jangan menambah fitur di luar permintaan.** Temuan di luar tugas dilaporkan, tidak diperbaiki sekalian.
- **Penangkapan galat spesifik**, bukan `catch (Exception)` polos: `catch (Exception ex) when (ex is IOException or
  UnauthorizedAccessException ...)`. Galat I/O yang bisa dipulihkan ditampilkan ke pengguna; galat tak terduga dicatat `CrashLog`.
  Pengecualian yang disengaja (ekspor HTML dan cetak menangkap semua kecuali OOM) ada komentarnya.
- **Helper lapisan bawah yang "tidak pernah melempar"** (`AppSettings.Load/Save`, `CrashLog.Write`, `SingleInstance`) menyatakannya di
  ringkasan XML; pertahankan sifat itu saat mengubahnya.
- Gaya yang terlihat: namespace file-scoped (`namespace MdViewer;`), indentasi 4 spasi, field privat tanpa awalan `_`, kelas
  `sealed`/`static` bila memungkinkan, `internal` untuk yang tak perlu publik (tersedia untuk test lewat `InternalsVisibleTo`).
  Tidak ada aturan tertulis; ikuti berkas di sekitar perubahan.
- **Penyimpanan file** selalu lewat `TextFileIO.Write` (atomik, mempertahankan encoding/BOM). **Pembukaan dokumen** hanya lewat
  `MainWindow.OpenFile`.
- **Pratinjau vs ekspor** memakai pipeline berbeda (`MarkdownSupport.Pipeline` vs `ExportPipeline` + `SanitizeForExport`).
- Pesan commit: tidak ada konvensi tertulis; riwayat saat ini satu commit berbahasa Indonesia. Jangan commit/push kecuali diminta.

## 4. Cara menulis test

Kerangka: xUnit 2.9.2 + `Microsoft.NET.Test.Sdk` 17.12.0 + coverlet.collector (`MdViewer.Tests.csproj`), target
`net9.0-windows`, `UseWPF`. `Xunit` dan `System.IO` sudah `<Using>` global.

### 4.1 Pola yang dipakai

| Kebutuhan | Pakai | Catatan |
| --- | --- | --- |
| Menyentuh WPF (`DocumentTab`, `DocumentView`, `FindReplaceBar`, `ChoiceDialog`, `ThemeManager.Apply`, `TextEditor`) | `WpfHost.Instance.Run(...)` + `[Collection("Wpf")]` pada kelas | `WpfHost` = satu thread STA dengan `Dispatcher` dan satu `App` (`ShutdownMode.OnExplicitShutdown`, resource tema dimuat, `OnStartup` tidak dipanggil). Koleksi `Wpf` mematikan paralelisasi (`MarkdownEditingTests.cs:942`). `DocumentTab` menangkap `Dispatcher.CurrentDispatcher`, jadi **buat di dalam `Run`**. |
| File/folder sementara | `TempDir` (`new TempDir()`, `File`, `WriteText`, `WriteBytes`, `Entries`; `Dispose` menghapus) | Lokasi `%TEMP%\MdViewer.Tests\<guid>`. Pakai `Entries()` untuk menegaskan tidak ada sisa `~md*.tmp`. |
| Mengalihkan `crash.log` | `TestLogRedirect` (`[ModuleInitializer]`) | Otomatis berlaku untuk seluruh assembly test; tidak perlu dipanggil. Log test: `%TEMP%\MdViewer.Tests\crash-<pid>.log`. |
| Menunggu event async/timer di dalam satu test STA | `UiPump.For(TimeSpan)` / `UiPump.Until(cond, timeout)` | Didefinisikan di `DocumentViewLifecycleTests.cs:14-41`; memompa dispatcher (`DispatcherFrame`). `UiPump.IsTimerEnabled(owner, "namaField")` membaca **field privat** lewat refleksi (`statsTimer`, `renderTimer`, `queryTimer`, `refreshTimer`): jangan ganti nama field itu tanpa memperbarui test. |
| Single-instance | `SingleInstance.Create("test-" + Guid.NewGuid().ToString("N"))` | Scope unik supaya mutex/pipe tidak bentrok dengan aplikasi asli atau test lain. |
| Pengaturan | `AppSettings.Load(path)`, `Save(path)`, `SaveMerged(path)` dengan path di `TempDir` | **Jangan** memakai `AppSettings.DefaultPath`/`Load()` tanpa argumen. |

Kerangka test yang menyentuh WPF (pola dari `DocumentTabTests.cs:13-47`):

```csharp
[Collection("Wpf")]
public class ContohTests : IDisposable
{
    readonly TempDir dir = new();
    readonly List<DocumentTab> tabs = [];

    public void Dispose()
    {
        WpfHost.Instance.Run(() => { foreach (var t in tabs) t.Dispose(); });
        tabs.Clear();
        GC.Collect();                 // BitmapImage menahan handle file gambar sampai di-GC
        GC.WaitForPendingFinalizers();
        dir.Dispose();
    }

    [Fact]
    public void Contoh()
    {
        var path = dir.WriteText("a.md", "lama");

        WpfHost.Instance.Run(() =>
        {
            var tab = DocumentTab.Load(path);
            tabs.Add(tab);
            tab.Document.Insert(0, "x");
            Assert.True(tab.IsDirty);
        });
    }
}
```

### 4.2 Aturan

1. **Jangan menyentuh data pengguna asli.** Test tidak boleh membaca/menulis `%APPDATA%`, `%LOCALAPPDATA%` (kecuali lewat
   `TestLogRedirect`), maupun registri, dan tidak boleh menjalankan skrip registri sungguhan. Pengecualian: **membaca**
   `HKCU\...\Personalize\AppsUseLightTheme` untuk tema diperbolehkan. Empat test melakukannya lewat
   `ThemeManager.SystemUsesLightTheme()`: `SystemUsesLightTheme_ReadsRegistryWithoutThrowing` (`SmallUtilityTests.cs:241`) dan tiga test
   `ThemeManagerApplyTests` yang memanggil `ThemeManager.Apply` (`DocumentTabTests.cs:1060, 1097, 1117`; `Apply` selalu membaca nilai itu,
   sedangkan `Shutdown_WithoutApply_OrTwice_DoesNotThrow` tidak); tidak ada yang menulis. Satu test sengaja membandingkan ukuran `crash.log`
   asli sebelum/sesudah untuk membuktikan tidak tersentuh (`IoAndUtilityCoverageTests.cs:647`).
2. **State global statis harus dipulihkan** di `finally`: `DocumentView.BlockRemoteImages`, `ThemeManager` (panggil `Apply(Light)`
   dan `Shutdown()`), `CrashLog.LogPath`, `CultureInfo.CurrentCulture`. Kelas di koleksi berbeda dapat berjalan paralel.
3. **Hindari `Thread.Sleep` dan jeda tetap.** Pilih urutan deterministik: panggil `CheckExternalChange()` langsung (bukan menunggu
   `FileSystemWatcher` + timer 400 ms), `RefreshPreview()` eksplisit, `UiPump.Until(kondisi, timeout)` dengan batas atas,
   `BlockingCollection.TryTake(timeout)` / `Task.WaitAsync`. Jujur soal kondisi sekarang: masih ada jeda tetap pada
   `UiPump.For(...)` (7 pemanggilan di `DocumentViewLifecycleTests.cs`, dipakai untuk membuktikan "tidak ada yang terjadi setelah
   debounce"), `Thread.Sleep` pada 4 tempat di `SingleInstanceServerTests.cs` dan 1 di `IoAndUtilityCoverageTests.cs:108` (polling
   dengan batas 5 dtk). Itu pengecualian; jangan menambah yang baru bila ada pilihan deterministik.
4. **Uji perilaku, bukan implementasi** (arahan agen `kurang-kerjaan`): cakup jalur normal, edge case, dan error path (file
   terkunci lewat `FileShare.None`, folder tak ada, direktori sebagai target, input kosong/null).
5. **Tipe yang test butuhkan harus bisa dijangkau:** `internal` terlihat karena `InternalsVisibleTo("MdViewer.Tests")`
   (`AssemblyInfo.cs:4`). `x:Name` XAML (mis. `FindBox`, `CountText`, `ReplaceRow`, `ButtonPanel`, `MessageText`) dipakai test; mengubah
   nama/hapus mempengaruhi test.
6. **Test symlink/ACL:** test symlink keluar diam-diam (return) bila mesin tidak boleh membuat symlink; test fallback `WriteInPlace`
   memakai ACL deny `CreateFiles` pada folder temp dan memulihkannya di `finally` (tidak butuh admin).
7. **Test yang belum pernah dijalankan dianggap belum selesai** (arahan `kurang-kerjaan`). Jalankan `dotnet test` sebelum PR.

## 5. Cara menambah

### Menambah tema/kunci brush baru

Brush semantik berada di `Themes/Light.xaml` dan `Themes/Dark.xaml` (masing-masing 43 kunci saat ini).

1. Tambahkan `<SolidColorBrush x:Key="NamaBrush" Color="#RRGGBB" />` ke **kedua** berkas dengan kunci **persis sama** (komentar di
   kedua berkas: "Kunci harus sama persis"). Kunci yang hanya ada di satu kamus gagal saat dijalankan di tema lainnya, termasuk saat
   mencetak (cetak memuat `Light.xaml` lewat `ThemeManager.LoadDictionary(dark: false)`).
2. Pakai di XAML dengan `{DynamicResource NamaBrush}` (jangan `StaticResource`: tidak ikut berganti tema).
3. Di kode, ambil lewat `TryFindResource`/`SetResourceReference` (contoh `DocumentView.ConfigureEditorAppearance`,
   `FindReplaceBar` -> `SearchMatchBrush`/`SearchCurrentBrush`). Properti AvalonEdit yang bukan dependency property biasa perlu
   diterapkan ulang di `DocumentView.RefreshTheme` (dipanggil `MainWindow.OnThemeChanged`).
4. Warna sintaks Markdown baru: tambahkan `Syntax*Brush` lalu satu baris `Set(definition, "NamaWarnaDiDefinisi", "Syntax*Brush", ...)`
   di `EditorTheme.ApplyMarkdownHighlighting` (`EditorTheme.cs:33-38`). Semua warna highlight otomatis dijaga kontrasnya >= 4,5:1
   (`EnsureContrast`); warna teks lain di kamus tema disarankan memenuhi rasio yang sama (komentar kamus).
5. **Periksa kesamaan kunci** (tidak ada test otomatis untuk ini; perintah ini sudah dicoba di Git Bash dan menghasilkan diff kosong
   saat ini):

   ```bash
   diff <(grep -o 'x:Key="[^"]*"' src/MdViewer/Themes/Light.xaml) <(grep -o 'x:Key="[^"]*"' src/MdViewer/Themes/Dark.xaml) && echo SAMA
   ```

6. Pratinjau: gaya `Styles.*` Markdig.Wpf ditimpa di `Themes/Preview.xaml` (warna lewat brush `Preview*`). Gaya kontrol di `Controls.xaml`.

Menambah **tema ketiga** (selain Terang/Gelap) bukan sekadar menambah berkas: `ThemeManager.LoadDictionary(bool dark)`,
`ResolveIsDark` (hasil boolean), `IsDark`, `UpdateThemeChecks`, item menu Tema di `MainWindow.xaml`, dan `AppThemeMode` semuanya
mengasumsikan dua kamus. Perlu perancangan ulang dan test baru (`ThemeManagerPureTests`, `ThemeManagerApplyTests`, `AppSettingsTests`).

### Menambah perintah/pintasan baru

1. `AppCommands.cs`: tambahkan `public static readonly RoutedUICommand` lewat `Create(teks, nama, gestur...)`. Gestur masuk ke menu
   otomatis sebagai teks pintasan. (Perintah bawaan `ApplicationCommands.*` tidak perlu dibuat ulang.)
2. `MainWindow.xaml`: tambahkan `<CommandBinding Command="{x:Static local:AppCommands.NamaBaru}" Executed="..." CanExecute="..."/>`
   di `Window.CommandBindings`. `HasTab_CanExecute` (butuh tab), `CanEdit_CanExecute` (editor tampak), `CanFormat_CanExecute`
   (editor tampak dan fokus bukan di panel cari) sudah ada.
3. Tulis handler di `MainWindow.xaml.cs`. Bila menyentuh dokumen, lewat `Current` (`DocumentTab`) / `Current.View`.
4. Tambahkan `MenuItem Command="..."` (dan tombol toolbar bila perlu). Pintasan yang didefinisikan lewat `Window.InputBindings`
   (bukan `AppCommands`) tidak muncul otomatis di menu; isi `InputGestureText` manual (contoh: Ctrl+W, Ctrl+Shift+S).
5. Periksa bentrok dengan pintasan bawaan WPF/AvalonEdit dan tabel pintasan di [../README.md](../README.md); perbarui README.
6. Bila memperlihatkan dialog, jangan dari event; pakai `ChoiceDialog` dan, untuk konflik, `conflictQueue` (lihat larangan di bawah).

### Menambah mode tampilan

Mode saat ini: `ViewMode { Edit, Split, Preview }` (`DocumentTab.cs:8`). Titik yang harus disentuh:

- `DocumentView.ApplyMode`, `PreviewVisible`, dan semua cabang `tab.Mode == ViewMode.Preview/Split/Edit` (`ShowFind`, `FindNext`,
  `ApplyFormat`, sinkron scroll `OnEditorScroll`/`OnPreviewScroll`, `ApplyParsed`).
- `MainWindow.xaml`: `RoutedUICommand` + `CommandBinding` + `KeyBinding` (Ctrl+1/2/3 saat ini) + item menu + `RadioButton`
  segmented control (`ViewModeConverter` dengan `ConverterParameter` = nama enum).
- `MainWindow.ViewMode_Executed` memetakan **teks** `RoutedUICommand` ("Editor", "Pratinjau", selain itu Terpisah)
  (`MainWindow.xaml.cs:605-614`): tambahkan cabangnya, atau ubah ke pemetaan yang eksplisit.
- `ViewModeLabelConverter`, `EditorVisibleConverter` (`Converters.cs`), `CanEdit_CanExecute`/`CanFormat_CanExecute`.
- Sesi: `SessionTab.Mode` disimpan sebagai teks nama enum dan `ParsedMode` jatuh ke `Split` untuk nilai tak dikenal
  (`AppSettings.cs:15`). **Mengganti nama anggota enum yang ada membuat sesi lama kembali ke Terpisah.**
- Test: `ConverterTests` (`SmallUtilityTests.cs:393`), `SessionTab_ParsedMode_FallsBackToSplit` (`AppSettingsTests.cs:248`),
  `DocumentViewLifecycleTests`. README (fitur dan pintasan).

### Menambah ekstensi file Markdown

1. `MarkdownFiles.Extensions` (`MarkdownFiles.cs:5`): dipakai seret-lepas dan klik tautan relatif. Saat ini `.md .markdown .mdown
   .mkd .txt`.
2. Filter dialog `OpenFilter`/`SaveFilter` di `MainWindow.xaml.cs:15-16` (string terpisah; `.txt` ada di filter sendiri).
3. Asosiasi file: default `-Extensions` di `scripts/register-file-association.ps1` dan `unregister-file-association.ps1` adalah
   `.md` dan `.markdown`; `-Extensions` dinormalkan ke huruf kecil lalu divalidasi `^\.[a-z0-9]+$`.
4. Test: `MarkdownFilesTests.IsMarkdown_ByExtension` (`HtmlAndMarkdownSupportTests.cs:9-31`). README (Asosiasi file).

### Menambah format toolbar

1. `MarkdownEditing.cs`: tambah anggota `MarkdownFormat` (`:7`), fungsi pada `TextDocument` yang dibungkus
   `document.BeginUpdate()/EndUpdate()` (supaya satu langkah Undo) dan mengembalikan `SelectionRange` hasil, lalu cabang di
   `MarkdownEditing.Apply` (`:34-45`).
2. `AppCommands.cs`: perintah baru + cabang di `AppCommands.FormatOf`.
3. `MainWindow.xaml`: `CommandBinding` (pakai `Format_Executed` dan `CanFormat_CanExecute`), tombol di `ToolBar` (blok yang visible
   saat editor tampak), dan item menu Edit > Format. Heading memakai `CommandParameter` sebagai level.
4. Test: `MarkdownEditingInlineTests/LineTests/LinkTests` (tanpa UI, `TextDocument` biasa) dan `MarkdownEditingApplyTests`
   (`TextEditor` di `WpfHost`). README (fitur dan pintasan).

## 6. Checklist PR

- [ ] `dotnet build MdViewer.sln` -> **0 warning, 0 error**.
- [ ] `dotnet test src/MdViewer.Tests` -> semua hijau (jalankan sungguhan; jangan menyatakan hijau tanpa menjalankan).
- [ ] Test baru untuk perilaku baru: jalur normal, edge case, error path. Tidak menyentuh `%APPDATA%`/registri/`crash.log` asli.
- [ ] Perubahan tema: kunci Light = Dark (periksa dengan perintah di atas); `DynamicResource` bukan `StaticResource`.
- [ ] Alur yang menulis file memakai `TextFileIO.Write`; yang membuka dokumen memakai `MainWindow.OpenFile`.
- [ ] Ekspor/pratinjau: tidak menyatukan pipeline; URL baru lewat `ClassifyUrl`; lihat [SECURITY.md](SECURITY.md).
- [ ] Teks UI/pesan/komentar bahasa Indonesia, identifier bahasa Inggris; komentar menjelaskan *mengapa*.
- [ ] Tidak ada fitur di luar permintaan; temuan lain dilaporkan terpisah.
- [ ] Dokumentasi diperbarui bila perilaku berubah: [../README.md](../README.md) (fitur, pintasan, batasan), [../CLAUDE.md](../CLAUDE.md)
  (bila aturan/struktur berubah), dokumen di `docs/`, dan [../CHANGELOG.md](../CHANGELOG.md).
- [ ] Tidak ada commit/push otomatis; commit hanya bila diminta.

## 7. Larangan (dari CLAUDE.md)

- Jangan menulis file dokumen langsung; semua lewat `TextFileIO.Write` (atomik, mempertahankan encoding/BOM).
- Jangan menyatukan `MarkdownSupport.Pipeline` (pratinjau) dengan `ExportPipeline` + `SanitizeForExport` (ekspor HTML wajib: HTML
  mentah di-escape, URL disaring).
- Jangan melonggarkan pemblokiran gambar di pratinjau/cetak (`ResolveImageUrls`): hanya file lokal dan `http(s)` yang boleh sampai ke
  WPF; UNC, `ftp:`, skema lain, dan `data:` diganti penanda; `http(s)` mengikuti opsi blokir remote.
- Jangan menyematkan gambar lokal di ekspor dari luar folder dokumen; hormati 2 MB per gambar dan anggaran 30 MB; jangan menghapus
  penanganan OOM/galat di `ExportHtml_Executed`.
- Jangan membuka dokumen selain lewat `MainWindow.OpenFile` (cek ukuran, OOM, tab ganda).
- Jangan menampilkan `MessageBox`/dialog konflik langsung dari event; pakai `ChoiceDialog` dan serialisasi `conflictPromptOpen`/
  `conflictQueue`. `DocumentTab.SaveTo` menunda pemeriksaan eksternal selama berjalan; pertahankan.
- Jangan membuat instance dengan argumen menimpa sesi tersimpan sebelum pengguna membuka tab lagi (`OpenUserFile`).
- Jangan menambah fitur di luar permintaan; build tidak boleh menghasilkan warning.
- Test tidak boleh menyentuh `%APPDATA%`/registri/`crash.log` pengguna; pakai scope unik pada `SingleInstance.Create(scope)`.
- **Jangan menjalankan skrip registri sungguhan** (`register/unregister-file-association.ps1`) tanpa `-WhatIf` lebih dulu; skrip ini
  menulis ke HKCU.
- Jangan commit atau push kecuali diminta.

## 8. Sub-agent proyek (`.claude/agents/`)

Empat sub-agent terdaftar di repo (berkas `.md` dengan frontmatter `name`, `description`, `tools`, `model`). Sumber kebenarannya adalah
isi folder itu, bukan daftar agen di klien; jika agen tidak muncul di daftar, periksa foldernya.

| Agen | Model | Alat | Kapan dipakai | Batasan penting |
| --- | --- | --- | --- | --- |
| `kuli` | sonnet | Read, Edit, Write, Glob, Grep, Bash | Implementasi fitur atau perbaikan bug yang spesifikasinya sudah jelas, setelah rencana disetujui; perubahan terfokus | Baca kode sekitar dulu; batasi pada yang diminta (masalah lain dilaporkan); jalankan build/test sebelum selesai; jangan commit/push/hapus file |
| `tyas` | sonnet | Read, Edit, Glob, Grep, Bash | Menyelidiki error, test gagal, atau perilaku tak terduga (ada stack trace, test merah, penyebab belum diketahui) | Cari akar masalah, bukan gejala: reproduksi -> hipotesis -> perbaikan minimal -> jalankan ulang; tandai bagian yang masih dugaan |
| `kurang-kerjaan` | sonnet | Read, Edit, Write, Glob, Grep, Bash | Menulis/memperbaiki test untuk kode yang ada atau baru berubah | Ikuti pola test proyek; jalankan test yang ditulis; **tidak mengubah kode produksi** (bug dilaporkan) |
| `pak-bos` | opus | Read, Glob, Grep, Bash | Review diff/perubahan secara proaktif setelah implementasi: korektness, regresi, celah test, keamanan dasar | Hanya baca (tidak punya Edit/Write); temuan berurut keparahan dengan `file:baris`, skenario gagal, dan saran; tanpa komentar gaya yang tidak melanggar konvensi |

Alur yang masuk akal (saran, bukan aturan repo): `kuli` mengimplementasikan -> `kurang-kerjaan` menambah test -> `pak-bos` me-review
`git diff`; bila ada test merah atau galat yang belum dipahami, `tyas` menyelidiki. Setiap agen melaporkan hasil verifikasi apa adanya,
termasuk kegagalan.
