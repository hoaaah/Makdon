# Panduan Kontribusi

**Tujuan:** cara menyiapkan lingkungan, membangun/menguji/mem-publish Makdon, konvensi kode, cara menulis test, cara menambah
hal-hal umum (tema, perintah, mode tampilan, ekstensi, format toolbar), checklist PR, dan larangan proyek.
**Pembaca:** kontributor baru dan agen AI yang mengubah kode. Aturan resmi proyek ada di [../CLAUDE.md](../CLAUDE.md); dokumen ini
merangkum dan menambah langkah praktis. Gambaran arsitektur: [ARCHITECTURE.md](ARCHITECTURE.md). Pengujian: [TESTING.md](TESTING.md).

## 1. Setup

- Windows (WPF) dan **.NET 10 SDK** (README: "Butuh .NET 10 SDK"). Tidak ada dependensi alat lain untuk build/test; paket NuGet dipulihkan otomatis
  oleh `dotnet build`. Proyek memakai `net10.0-windows` (`src/Makdon/Makdon.csproj:5`), jadi tidak bisa dibangun di Linux/macOS.
- CI hanya untuk rilis (`.github/workflows/release.yml`, dipicu push ke branch `build`); tidak ada CI per PR. Tidak ada analyzer atau `.editorconfig`;
  penjaga kualitas saat ini adalah aturan "0 warning" dan test (lihat bagian 6).

```powershell
dotnet build Makdon.sln                   # harus 0 warning, 0 error
dotnet test src/Makdon.Tests              # xUnit; semua harus hijau
dotnet run --project src/Makdon -- file.md
dotnet publish src/Makdon -p:PublishProfile=win-x64
```

Hasil publish: `src\Makdon\bin\Release\net10.0-windows\win-x64\publish\Makdon.exe` (self-contained: runtime ikut dikemas, jadi
.NET tidak perlu dipasang di mesin tujuan). Profil publish ada di `src/Makdon/Properties/PublishProfiles/win-x64.pubxml`. `RuntimeIdentifier` tidak dipaksa di `Makdon.csproj` agar build/test biasa tetap netral (komentar `Makdon.csproj:15-18`).
Properti `<Version>` saat ini `0.1.0` (`Makdon.csproj:12`), sama dengan rilis awal di [../CHANGELOG.md](../CHANGELOG.md). Ubah keduanya bersama saat rilis.

## 2. Struktur folder

```text
Makdon.sln
CLAUDE.md, README.md, CHANGELOG.md
docs/                       dokumentasi pengembangan (indeks: docs/README.md)
scripts/                    register/unregister-file-association.ps1 (HKCU), build-release.ps1, generate-icon.ps1
installer/                  Makdon.iss (Inno Setup 6), Languages/Indonesian.isl
.github/workflows/          release.yml (rilis pada push ke branch build)
LICENSE, THIRD-PARTY-NOTICES.txt   lisensi MIT dan pemberitahuan pihak ketiga (ikut dalam setiap rilis)
.claude/agents/             sub-agent proyek (kuli, tyas, kurang-kerjaan, pak-bos)
src/Makdon/                 aplikasi WPF
    App.xaml(.cs)           titik masuk, single-instance, penangan galat global
    MainWindow.xaml(.cs)    tab, dialog, sesi, menu, ekspor, cetak
    DocumentTab.cs          model dokumen (teks, path, encoding, watcher, simpan)
    DocumentView.xaml(.cs)  editor + pratinjau + sinkron scroll
    FindReplaceBar.xaml(.cs), SearchEngine.cs, MarkdownEditing.cs
    TextFileIO.cs, FileStamp.cs, AppSettings.cs, SingleInstance.cs, CrashLog.cs
    AppPaths.cs, AppInfo.cs, InstallerMutex.cs, RegistryStore.cs, FileAssociation.cs   mode/lokasi data, identitas, mutex installer, "Buka dengan"
    MarkdownSupport.cs, AnchorHeadingRenderer.cs, HtmlExporter.cs
    PrintLayout.cs          PageLayout, PrintSnapshot, PrintSource, PrintService, enum kertas/orientasi/margin
    HeaderFooterPaginator.cs, PreviewBuild.cs, PrintPreviewWindow.xaml(.cs)   Pratinjau Cetak
    Theming.cs, EditorTheme.cs, Themes/{Light,Dark,Controls,Preview}.xaml
    AppCommands.cs, ChoiceDialog.xaml(.cs), Converters.cs, ViewModeConverter.cs, NotNullConverter.cs
    ZoomLevel.cs, TextStats.cs, EncodingNames.cs, MarkdownFiles.cs, Assets/app.ico
src/Makdon.Tests/           xUnit
    Support/                WpfHost.cs (+ DispatcherErrors, FailOnUnexpectedDispatcherErrorsAttribute), AssemblyInfo.cs,
                            PrintTestKit.cs, TempDir.cs, TestLogRedirect.cs, FakeRegistryStore.cs
    *Tests.cs               peta berkas -> area ada di TESTING.md
```

`bin/`, `obj/`, `.vs/`, `*.user`, `TestResults/` diabaikan git (`.gitignore`).

## 3. Konvensi kode dan bahasa

Dari CLAUDE.md, dilengkapi pola yang konsisten terlihat di kode:

- **Bahasa:** teks UI, pesan galat, dan komentar kode berbahasa **Indonesia**; nama identifier berbahasa **Inggris**.
- **Nullable dan ImplicitUsings aktif**; build tidak boleh menghasilkan warning (`System.IO` ditambahkan lewat `<Using>` di csproj).
- **Komentar menjelaskan alasan (mengapa)**, bukan mengulang kode. Contoh gaya: komentar di atas keputusan non-obvious seperti
  `DocumentTab.cs:33-36`, `TextFileIO.cs:145`.
- **Jangan menambah fitur di luar permintaan.** Temuan di luar tugas dilaporkan, tidak diperbaiki sekalian.
- **Penangkapan galat spesifik**, bukan `catch (Exception)` polos: `catch (Exception ex) when (ex is IOException or
  UnauthorizedAccessException ...)`. Galat I/O yang bisa dipulihkan ditampilkan ke pengguna; galat tak terduga dicatat `CrashLog`.
  Pengecualian yang disengaja (ekspor HTML dan cetak menangkap semua kecuali OOM; `PreviewBuild.Guard` menangkap semua **termasuk** OOM,
  [ADR-25](DESIGN-DECISIONS.md#adr-25-galat-pratinjau-dibungkus-guard-dan-paket-xps-dibersihkan-setelah-idle)) ada komentarnya.
- **Helper lapisan bawah yang "tidak pernah melempar"** (`AppSettings.Load/Save`, `CrashLog.Write`, `SingleInstance`) menyatakannya di
  ringkasan XML; pertahankan sifat itu saat mengubahnya.
- Gaya yang terlihat: namespace file-scoped (`namespace Makdon;`), indentasi 4 spasi, field privat tanpa awalan `_`, kelas
  `sealed`/`static` bila memungkinkan, `internal` untuk yang tak perlu publik (tersedia untuk test lewat `InternalsVisibleTo`).
  Tidak ada aturan tertulis; ikuti berkas di sekitar perubahan.
- **Penyimpanan file** selalu lewat `TextFileIO.Write` (atomik, mempertahankan encoding/BOM). **Pembukaan dokumen** hanya lewat
  `MainWindow.OpenFile`.
- **Pratinjau vs ekspor** memakai pipeline berbeda (`MarkdownSupport.Pipeline` vs `ExportPipeline` + `SanitizeForExport`).
- Pesan commit: tidak ada konvensi tertulis; riwayat saat ini satu commit berbahasa Indonesia. Jangan commit/push kecuali diminta.

## 4. Cara menulis test

Kerangka: xUnit 2.9.2 + `Microsoft.NET.Test.Sdk` 17.12.0 + coverlet.collector (`Makdon.Tests.csproj`), target
`net10.0-windows`, `UseWPF`. `Xunit` dan `System.IO` sudah `<Using>` global.

### 4.1 Pola yang dipakai

| Kebutuhan | Pakai | Catatan |
| --- | --- | --- |
| Menyentuh WPF (`DocumentTab`, `DocumentView`, `FindReplaceBar`, `ChoiceDialog`, `ThemeManager.Apply`, `TextEditor`; tipe cetak: `PrintPreviewWindow`, `PreviewBuild`, `HeaderFooterPaginator`, `FlowDocument` hasil `PrintService.CreateDocument`, `DocumentViewer`, `FixedDocumentSequence`, `PrintTicket`) | `WpfHost.Instance.Run(...)` + `[Collection("Wpf")]` pada kelas | `WpfHost` = satu thread STA dengan `Dispatcher` dan satu `App` (`ShutdownMode.OnExplicitShutdown`, resource tema dimuat, `OnStartup` tidak dipanggil). Koleksi `Wpf` mematikan paralelisasi (`MarkdownEditingTests.cs:942`). `DocumentTab` menangkap `Dispatcher.CurrentDispatcher`, jadi **buat di dalam `Run`**; `PreviewBuild` juga (`Dispatcher.CurrentDispatcher` di field-nya). Tipe non-WPF murni (`PageLayout.For`/`MarginOf`/`FromPrintableArea`, `PrintPreviewWindow.TicketMatches`/`ApplyTicket` pada `PrintTicket` terpisah) tidak butuh `Run`, tetapi kelas yang memuatnya di repo ini tetap `[Collection("Wpf")]`. |
| Test cetak: dokumen contoh, halaman kecil, menunggu tahap, membaca teks halaman XPS | `Support/PrintTestKit` (`using static Makdon.Tests.Support.PrintTestKit;`) | `Small` (360 x 420, margin 24), `Sample`, `Paragraphs(n)` (banyak halaman `Small`), `Pages(n)` (banyak halaman A4), `StartBuild`, `WaitForEnd`/`WaitForPaginated` (memakai `UiPump.Until` dengan batas `Patience` 15 dtk), `GlyphTexts` (teks halaman XPS), `FooterTexts` (teks kaki halaman), `Paginator`. |
| Menguji bahwa callback dispatcher melempar (galat yang memang diharapkan) | `using (var scope = WpfHost.ExpectUnhandled()) { ... }` lalu periksa `scope.Errors` | Tanpa scope, galat yang lolos ke dispatcher menggagalkan test lewat `[assembly: FailOnUnexpectedDispatcherErrors]` (`Support/AssemblyInfo.cs`). Untuk menegaskan "tidak ada galat lolos": `var before = WpfHost.Unhandled.Count; ...; Assert.Equal(before, WpfHost.Unhandled.Count);`. Lihat [TESTING.md](TESTING.md#thread-sta-dan-wpfhost). |
| File/folder sementara | `TempDir` (`new TempDir()`, `File`, `WriteText`, `WriteBytes`, `Entries`; `Dispose` menghapus) | Lokasi `%TEMP%\Makdon.Tests\<guid>`. Pakai `Entries()` untuk menegaskan tidak ada sisa `~md*.tmp`. |
| Mengalihkan `crash.log` | `TestLogRedirect` (`[ModuleInitializer]`) | Otomatis berlaku untuk seluruh assembly test; tidak perlu dipanggil. Log test: `%TEMP%\Makdon.Tests\crash-<pid>.log`. |
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
   debounce"; test cetak menambah 41 pemanggilan: 17 di `PreviewBuildTests`, 19 di `PrintPreviewWindowBehaviorTests`, 3 di
   `PrintContentAndCommandTests`, 1 di `PrintPreviewTests`, 1 di `WpfHostErrorTrackingTests`, umumnya untuk memberi waktu pada callback sisa setelah
   `Dispose` atau pada pemuat gambar), `Thread.Sleep` pada 4 tempat di `SingleInstanceServerTests.cs` dan 1 di `IoAndUtilityCoverageTests.cs:108` (polling
   dengan batas 5 dtk). Itu pengecualian; jangan menambah yang baru bila ada pilihan deterministik.
4. **Uji perilaku, bukan implementasi** (arahan agen `kurang-kerjaan`): cakup jalur normal, edge case, dan error path (file
   terkunci lewat `FileShare.None`, folder tak ada, direktori sebagai target, input kosong/null).
5. **Tipe yang test butuhkan harus bisa dijangkau:** `internal` terlihat karena `InternalsVisibleTo("Makdon.Tests")`
   (`AssemblyInfo.cs:4`). `x:Name` XAML (mis. `FindBox`, `CountText`, `ReplaceRow`, `ButtonPanel`, `MessageText`) dipakai test; mengubah
   nama/hapus mempengaruhi test. Jendela Pratinjau Cetak sama: test mencari `Viewer`, `PageBox`, `PrintButton`, `BusyPanel`, `BusyText`,
   `BusyDetail`, `FooterCheck`, `*Button` (orientasi/kertas/margin/navigasi/zoom) lewat `FindName`. Beberapa test cetak juga membaca
   anggota **privat** `PreviewBuild` lewat refleksi (`counter`, `packageUri`, `cleanupScheduled`, metode `Cleanup`;
   `PreviewBuildTests.cs:15-16, 430, 544-545, 590, 601`) dan membaca teks `MainWindow.xaml` dengan regex (`PrintContentAndCommandTests.cs:333, 369`).
6. **Test symlink/ACL:** test symlink keluar diam-diam (return) bila mesin tidak boleh membuat symlink; test fallback `WriteInPlace`
   memakai ACL deny `CreateFiles` pada folder temp dan memulihkannya di `finally` (tidak butuh admin).
7. **Test yang belum pernah dijalankan dianggap belum selesai** (arahan `kurang-kerjaan`). Jalankan `dotnet test` sebelum PR.
8. **Test cetak tanpa printer fisik dan tanpa dialog sungguhan.** Jangan membuka `PrintDialog`, `MessageBox`, atau `ChoiceDialog` dari test (dialog
   modal menggantung proses test) dan jangan mencetak ke printer atau ke "Microsoft Print to PDF". Jalur Cetak di `PrintPreviewWindow` diuji lewat
   hook `ShowPrintDialogForTests` (mengembalikan `false` = batal; `PrintPreviewWindowBehaviorTests.cs:889, 921`); logika kertas/orientasi diuji lewat
   pembantu statis `TicketMatches`/`ApplyTicket`/`DescribeTicket` pada `PrintTicket` buatan sendiri. Kegagalan render dipaksa lewat
   `DocumentView.RenderFaultForTests` (**kembalikan ke `null` di `finally`**). Test yang memang mengharapkan callback dispatcher melempar membungkusnya
   dengan `WpfHost.ExpectUnhandled()`. Tunggu kondisi dengan `UiPump.Until(cond, PrintTestKit.Patience)`, bukan jeda tetap. Pola yang dipakai test yang
   ada: jendela ditutup di `Dispose` kelas test dan `PreviewBuild` di-`Dispose`, supaya paket XPS dan timer tidak tertinggal ke test berikutnya.
9. **Dokumen cetak tidak boleh memuat path lokal.** Jalur cetak tidak boleh memakai `CreateErrorDocument` (memuat `CrashLog.LogPath`); kegagalan render harus
   dilempar (`throwOnFailure: true`) dan ditampilkan di layar. Kaki halaman hanya memuat nama berkas. Lihat [SECURITY.md](SECURITY.md#212-pratinjau-cetak-dan-cetak).
10. **Lokasi data dan registri lewat seam.** `settings.json` dan `crash.log` hanya lewat `AppPaths`; test menyuntikkan folder palsu lewat `AppPaths.Detect(folder, markerExists: ...)`. `FileAssociation` hanya menulis lewat `IRegistryStore`; test memakai `FakeRegistryStore`. Jangan memanggil `Environment.GetFolderPath` atau `Microsoft.Win32.Registry` langsung dari kode yang diuji.
11. **Host test WPF tidak menjalankan `App.OnStartup`.** Jangan mengganti `WpfHost.TestApp` dengan `App` biasa: startup sungguhan membuat mutex/pipe produksi, `MainWindow`, dan membaca settings pengguna ([ADR-33](DESIGN-DECISIONS.md#adr-33-wpfhost-memakai-testapp-tanpa-onstartup)).

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
   diff <(grep -o 'x:Key="[^"]*"' src/Makdon/Themes/Light.xaml) <(grep -o 'x:Key="[^"]*"' src/Makdon/Themes/Dark.xaml) && echo SAMA
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
5. Periksa bentrok dengan pintasan bawaan WPF/AvalonEdit dan tabel pintasan di [../README.md](../README.md); perbarui README. Pintasan
   ganda di `AppCommands`, `CommandBinding` standar, dan `KeyBinding` di `MainWindow.xaml` ditangkap otomatis oleh
   `NoTwoAppCommands_ShareTheSameKeyGesture` dan `MainWindowShortcuts_AreUnique_*` (`PrintContentAndCommandTests.cs:319, 333`).
6. Bila memperlihatkan dialog, jangan dari event; pakai `ChoiceDialog` dan, untuk konflik, `conflictQueue` (lihat larangan di bawah).

### Menambah mode tampilan

Mode saat ini: `ViewMode { Edit, Split, Preview }` (`DocumentTab.cs:8`). Titik yang harus disentuh:

- `DocumentView.ApplyMode`, `PreviewVisible`, dan semua cabang `tab.Mode == ViewMode.Preview/Split/Edit` (`ShowFind`, `FindNext`,
  `ApplyFormat`, sinkron scroll `OnEditorScroll`/`OnPreviewScroll`, `ApplyParsed`).
- `MainWindow.xaml`: `RoutedUICommand` + `CommandBinding` + `KeyBinding` (Ctrl+1/2/3 saat ini) + item menu + `RadioButton`
  segmented control (`ViewModeConverter` dengan `ConverterParameter` = nama enum).
- `MainWindow.ViewMode_Executed` memetakan **teks** `RoutedUICommand` ("Editor", "Pratinjau", selain itu Terpisah)
  (`MainWindow.xaml.cs:795-804`): tambahkan cabangnya, atau ubah ke pemetaan yang eksplisit.
- `ViewModeLabelConverter`, `EditorVisibleConverter` (`Converters.cs`), `CanEdit_CanExecute`/`CanFormat_CanExecute`.
- Sesi: `SessionTab.Mode` disimpan sebagai teks nama enum dan `ParsedMode` jatuh ke `Split` untuk nilai tak dikenal
  (`AppSettings.cs:15`). **Mengganti nama anggota enum yang ada membuat sesi lama kembali ke Terpisah.**
- Test: `ConverterTests` (`SmallUtilityTests.cs:393`), `SessionTab_ParsedMode_FallsBackToSplit` (`AppSettingsTests.cs:248`),
  `DocumentViewLifecycleTests`. README (fitur dan pintasan).

### Menambah ekstensi file Markdown

1. `MarkdownFiles.Extensions` (`MarkdownFiles.cs:5`): dipakai seret-lepas dan klik tautan relatif. Saat ini `.md .markdown .mdown
   .mkd .txt`.
2. Filter dialog `OpenFilter`/`SaveFilter` di `MainWindow.xaml.cs:16-17` (string terpisah; `.txt` ada di filter sendiri).
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

- [ ] `dotnet build Makdon.sln` -> **0 warning, 0 error**.
- [ ] `dotnet test src/Makdon.Tests` -> semua hijau (jalankan sungguhan; jangan menyatakan hijau tanpa menjalankan).
- [ ] Test baru untuk perilaku baru: jalur normal, edge case, error path. Tidak menyentuh `%APPDATA%`/registri/`crash.log` asli.
- [ ] Perubahan tema: kunci Light = Dark (periksa dengan perintah di atas); `DynamicResource` bukan `StaticResource`.
- [ ] Alur yang menulis file memakai `TextFileIO.Write`; yang membuka dokumen memakai `MainWindow.OpenFile`.
- [ ] Ekspor/pratinjau: tidak menyatukan pipeline; URL baru lewat `ClassifyUrl`; lihat [SECURITY.md](SECURITY.md).
- [ ] Cetak/Pratinjau Cetak: tidak ada path lokal atau dokumen galat di kertas; test cetak tanpa printer fisik/dialog sungguhan (aturan 8-9 di 4.2).
- [ ] Teks UI/pesan/komentar bahasa Indonesia, identifier bahasa Inggris; komentar menjelaskan *mengapa*.
- [ ] Tidak ada fitur di luar permintaan; temuan lain dilaporkan terpisah.
- [ ] Dokumentasi diperbarui bila perilaku berubah: [../README.md](../README.md) (fitur, pintasan, batasan), [../CLAUDE.md](../CLAUDE.md)
  (bila aturan/struktur berubah), dokumen di `docs/`, dan [../CHANGELOG.md](../CHANGELOG.md).
- [ ] Perubahan identitas (`AppId`, nama mutex `Makdon.AppMutex`, tabel registri DISTRIBUTION 4.1, switch XPS) diubah serentak di semua tempat yang tercantum di CLAUDE.md. Perubahan installer atau skrip registri diuji dengan `-WhatIf` atau di VM.
- [ ] Tidak ada commit/push otomatis; commit hanya bila diminta.

## 7. Larangan (dari CLAUDE.md)

- Jangan menulis file dokumen langsung; semua lewat `TextFileIO.Write` (atomik, mempertahankan encoding/BOM).
- Jangan menyatukan `MarkdownSupport.Pipeline` (pratinjau) dengan `ExportPipeline` + `SanitizeForExport` (ekspor HTML wajib: HTML
  mentah di-escape, URL disaring).
- Jangan melonggarkan pemblokiran gambar di pratinjau/cetak (`ResolveImageUrls`): hanya file lokal dan `http(s)` yang boleh sampai ke
  WPF; UNC, `ftp:`, skema lain, dan `data:` diganti penanda; `http(s)` mengikuti opsi blokir remote.
- Jangan menyematkan gambar lokal di ekspor dari luar folder dokumen; hormati 2 MB per gambar dan anggaran 30 MB; jangan menghapus
  penanganan OOM/galat di `ExportHtml_Executed`.
- Jangan membuat dokumen cetak yang memuat path lokal atau dokumen galat (`CreateErrorDocument`); jalur cetak/pratinjau cetak melempar
  (`throwOnFailure: true`). Jangan mencetak dari tempat lain selain `PrintService`/`PrintPreviewWindow`: ukuran halaman hanya diturunkan oleh
  `PageLayout.Apply`, kaki halaman oleh `HeaderFooterPaginator`.
- Jangan menutup paket XPS pratinjau (`PreviewBuild.Cleanup`) secara sinkron dari `Dispose`: tunggu penulis berhenti dan dispatcher idle.
- Jangan membuka dokumen selain lewat `MainWindow.OpenFile` (cek ukuran, OOM, tab ganda).
- Jangan menampilkan `MessageBox`/dialog konflik langsung dari event; pakai `ChoiceDialog` dan serialisasi `conflictPromptOpen`/
  `conflictQueue`. `DocumentTab.SaveTo` menunda pemeriksaan eksternal selama berjalan; pertahankan.
- Jangan membuat instance dengan argumen menimpa sesi tersimpan sebelum pengguna membuka tab lagi (`OpenUserFile`).
- Jangan menambah fitur di luar permintaan; build tidak boleh menghasilkan warning.
- Test tidak boleh menyentuh `%APPDATA%`/registri/`crash.log` pengguna; pakai scope unik pada `SingleInstance.Create(scope)`.
- **Jangan menjalankan skrip registri sungguhan** (`register/unregister-file-association.ps1`) tanpa `-WhatIf` lebih dulu; skrip ini
  menulis ke HKCU.
- Jangan mengubah `AppId` installer atau nama mutex `Makdon.AppMutex`, dan jangan menghapus switch `DisableXpsPackageBoundaryRestriction` ([ADR-31](DESIGN-DECISIONS.md#adr-31-mutex-installer-bernama-tetap-makdonappmutex-bukan-restart-manager), [ADR-32](DESIGN-DECISIONS.md#adr-32-net-10-dan-switch-xps-di-runtimeconfig)).
- Jangan memindahkan data portable diam-diam ke `%APPDATA%`, dan jangan menaruh penanda `Makdon.portable` di bahan installer atau folder rilis ([ADR-28](DESIGN-DECISIONS.md#adr-28-mode-portable-lewat-penanda-makdonportable-data-di-data)).
- Jangan menulis registri dari kode di luar `FileAssociation`/`IRegistryStore`.
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

## 9. Membuat rilis

Rilis dipicu push ke branch `build`; rancangan lengkapnya di [DISTRIBUTION.md](DISTRIBUTION.md) §8. Versi diambil dari `<Version>`, tag `v<versi>`
dibuat otomatis, dan run gagal bila versi itu sudah pernah dirilis.

1. Naikkan `<Version>` di `src/Makdon/Makdon.csproj` (satu sumber; skrip dan CI membacanya) dan pindahkan entri `[Unreleased]` di
   [../CHANGELOG.md](../CHANGELOG.md) ke versi dan tanggal baru.
2. Bangun lokal dengan `powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1`. Skrip ini build (0 warning), test, publish profil
   `win-x64`, menyusun isi rilis, membangun installer, membuat zip portable, lalu `SHA256SUMS.txt`. Setiap run menghapus `artifacts\<versi>\` lebih dulu.
   - Tanpa Inno Setup 6 terpasang, installer dilewati dengan peringatan. Pakai `-IsccPath` untuk lokasi `ISCC.exe`, `-RequireInstaller` untuk gagal bila installer tidak jadi.
   - `-SkipTests` hanya untuk percobaan cepat, jangan dipakai untuk rilis.
   - `-VerifyInstallerContents` memasang installer ke folder sementara lalu mencopotnya, dan menulis HKCU sementara. Skrip menolaknya di luar CI (`GITHUB_ACTIONS=true`) kecuali dengan `-Force`, dan menolak bila kunci uninstall Makdon sudah ada (`scripts/build-release.ps1:174-188`).
3. Keluaran di `artifacts\<versi>\`: `Makdon-<versi>-setup-x64.exe`, `Makdon-<versi>-portable-x64.zip`, `SHA256SUMS.txt`.
4. Uji installer dan zip dengan checklist "Distribusi" di [TESTING.md](TESTING.md#checklist-uji-manual-sebelum-rilis).
5. Commit perubahan versi dan CHANGELOG (hanya bila diminta), lalu push commit itu ke branch `build` (mis. `git push origin main:build`). Workflow
   `release.yml` membuat tag `v<versi>` pada commit tersebut, membuat draft rilis, mengunggah tiga aset, dan mempublikasikannya. Rilis yang sudah terbit tidak boleh diganti (immutable releases, **belum diverifikasi**
   di repo); bila salah, terbitkan versi baru.
