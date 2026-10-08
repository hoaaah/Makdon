# Pengujian MdViewer

**Tujuan:** peta test (berkas -> area), cara menjalankan, model thread test WPF, daftar hal yang **tidak** teruji, dan checklist uji
manual sebelum rilis.
**Pembaca:** pengembang yang menjalankan/menulis test dan orang yang menyiapkan rilis. Cara menulis test baru (pola dan aturan):
[CONTRIBUTING.md](CONTRIBUTING.md#4-cara-menulis-test).

Catatan kejujuran: dokumen ini disusun dengan membaca kode. Run terakhir (2026-10-08, `dotnet test src/MdViewer.Tests`): 1455 kasus
lulus, 0 gagal, 0 dilewati (durasi sekitar 55 dtk; sebelum fitur Pratinjau Cetak: 1168 kasus, sekitar 22 dtk). Jumlah test di tabel = jumlah atribut `[Fact]`/`[Theory]` per berkas (hasil `grep`), bukan jumlah
kasus yang dieksekusi; satu `[Theory]` bisa menjadi banyak kasus. Total 800 atribut di 24 berkas.

## Peta test: berkas -> area

Proyek `src/MdViewer.Tests` (xUnit 2.9.2, `net9.0-windows`). `[W]` = kelas bertanda `[Collection("Wpf")]` (berjalan lewat `WpfHost`).

| Berkas | Atribut | Kelas (area) |
| --- | --- | --- |
| `TextFileIOTests.cs` | 44 | `TextFileIODecodeTests` (BOM, UTF-8/16/32, fallback 1252, round-trip byte identik), `TextFileIOEncodeTests` (BOM, naik ke UTF-8, surrogate yatim), `TextFileIOHashTests` (SHA-256), `TextFileIOFileTests` (tulis atomik, tanpa sisa temp, file terkunci/read-only/direktori, symlink) |
| `IoAndUtilityCoverageTests.cs` | 73 | `TextFileIOCoverageTests` (flag lossy per BOM, `WriteBytesAtomic`, fallback `WriteInPlace` lewat ACL, `ResolveLinkTarget`, `FileStamp`), `TextStatsChunkBoundaryTests` (potongan 64 KB), `CrashLogLimitsTests` (klasifikasi galat, batas 512 KB, konkurensi), `AppSettingsMergeEdgeTests` (`SaveMerged`, `BlockRemoteImages`), `SearchEngineEdgeCaseTests` (`NormalizeLineEndings`, batas waktu total) |
| `HardeningTests.cs` | 58 | `ExportSanitizationTests` (escape HTML, allowlist URL, `ClassifyUrl`), `RemoteImageBlockingTests` (UNC/remote di pratinjau), `DocumentTabHardeningTests` `[W]` (simpan vs perubahan eksternal, lossy, dispose), `TextFileIOHardeningTests`, `TextStatsSourceTests`, `AppSettingsMergeTests`, `SingleInstanceTests` (protokol + pipe dasar) |
| `ExportAndImageSecurityTests.cs` | 30 | `ExportXssVectorTests` (vektor XSS, autolink, media link, gambar), `ResolveImageUrlsSecurityTests` (varian UNC/skema di pratinjau, `IsAllowedLocalPath`) |
| `LinkResolutionTests.cs` | 12 | `MarkdownSupport.ResolveLinkTarget`: tautan UNC/perangkat (termasuk ter-percent-encode) ditolak, share yang sama diizinkan, drive lokal/relatif, `#anchor`, `file:` dengan/tanpa host, non-markdown, masukan kosong (66 kasus) |
| `ExportImageBudgetAndPrivacyTests.cs` | 9 | Gambar lokal hanya di bawah folder dokumen, anggaran total 30 MB, cache per path |
| `HtmlAndMarkdownSupportTests.cs` | 34 | `MarkdownFilesTests`, `MarkdownSlugTests` (id heading gaya GitHub), `ResolveImageUrlsTests` (resolusi path), `HtmlExporterTests` (template, judul, tulis file) |
| `DocumentTabTests.cs` | 61 | `DocumentTabTests` `[W]` (buat/muat/simpan, encoding, `IsDirty`/Undo, perubahan eksternal, `Reload`, dispose, ekspor), `ThemeManagerApplyTests` `[W]` (tukar `ResourceDictionary`) |
| `DocumentTabConflictEdgeTests.cs` | 24 | `[W]` Konflik simpan/eksternal: `pendingHash`, `KeepEditorVersion`, stempel "racy", flag lossy, Undo satu langkah |
| `DocumentViewLifecycleTests.cs` | 22 | `[W]` Definisi `UiPump`; `DocumentView.Dispose`, `FindReplaceBar.Detach`, render latar basi dibuang, gambar rusak/`data:`/FTP di pratinjau dan cetak, pola regex timeout tidak diulang |
| `SingleInstanceServerTests.cs` | 24 | `ParseMessage`/`BuildMessage`, server pipe nyata (scope unik): batas 64, pesan besar, klien macet, callback melempar, siklus hidup |
| `SearchEngineTests.cs` | 70 | `SearchEngineFindTests`, `TryFindAllTests`, `IndexTests`, `ReplaceAllTests` (literal/regex, CRLF, batas hasil, katastrofik) |
| `MarkdownEditingTests.cs` | 86 | `MarkdownEditingInlineTests`, `LineTests`, `LinkTests` (tanpa UI, `TextDocument`), `MarkdownEditingApplyTests` `[W]`; juga `CollectionDefinition("Wpf", DisableParallelization = true)` |
| `AppSettingsTests.cs` | 40 | `Load` (korup, tipe salah, sanitasi), `Save`, `AddRecent`/`RemoveRecent`, round-trip |
| `SmallUtilityTests.cs` | 49 | `ZoomLevelTests`, `TextStatsTests`, `EncodingNamesTests`, `ThemeManagerPureTests`, `EditorThemeContrastTests`, `ConverterTests` |
| `CrashLogTests.cs` | 7 | `IsRecoverable`, HRESULT yang dikenal, `ShouldShowDialog`, format stempel waktu, `Write` |
| `ChoiceDialogTests.cs` | 2 | `[W]` `ChoiceDialog`: klik tombol mengembalikan nilai, tutup tanpa memilih = nilai batal |
| `PrintPreviewTests.cs` | 19 | `[W]` `PageLayoutTests` (bawaan A4 potret margin Normal, lanskap menukar sisi, urutan margin, `FromPrintableArea`, margin efektif <= seperempat sisi pendek, `Apply`) dan `PrintPreviewWindowTests` (jendela dibuat di STA, paginasi async, snapshot tak terpengaruh editor, dokumen cetak tetap memblokir gambar remote/UNC, paginator + kaki halaman, halaman XPS sesuai paginator, navigasi dasar) |
| `PreviewBuildTests.cs` | 32 | `[W]` `PreviewBuild`: tahap Paginating -> Rendering -> Ready berurutan, isi halaman XPS (nama dokumen, "Halaman X dari N", ukuran halaman), dokumen kosong, `Dispose` di tiap tahap (termasuk dari dalam callback), siklus kedua, satu penulis XPS per siklus, galat tak lolos ke dispatcher (penerima `Changed` melempar, OOM), paket dilepas dari `PackageStore`, timer cadangan 10 dtk, sumber parse bersama dan parse latar yang ditahan, kata panjang tak melebihi lebar halaman |
| `HeaderFooterPaginatorTests.cs` | 18 | `[W]` Kaki halaman: nama + "Halaman X dari N" di tiap halaman, tanpa kaki = halaman identik, jumlah halaman sama dengan/tanpa kaki, kaki di dalam margin bawah dan >= 24 DIP dari tepi untuk tiga preset, halaman yang sama diminta dua kali, halaman di luar rentang, nama dokumen aneh/sangat panjang |
| `PrintLayoutMatrixTests.cs` | 17 | `[W]` `PageLayoutMatrixTests`: seluruh 12 kombinasi kertas x orientasi x margin, nilai enum di luar rentang, `FromPrintableArea` dengan NaN/tak hingga/terlalu kecil/terlalu besar, `Apply` dua kali dan margin ekstrem, margin lebih lebar tidak pernah memberi halaman lebih sedikit |
| `PrintContentAndCommandTests.cs` | 23 | `[W]` Dokumen cetak (kertas putih, tema Terang walau aplikasi Gelap, dokumen independen), kegagalan render (`DocumentView.RenderFaultForTests`: pesan ramah tanpa path, bukan dokumen galat), gambar rusak/hilang/valid, ftp/UNC/`file://host`/`data:`/`javascript:` diblokir tanpa koneksi (listener TCP lokal), flag blokir remote dari snapshot, `AppCommands.PrintPreview` (Ctrl+Shift+P) dan keunikan pintasan (membaca teks `MainWindow.xaml`), pengkabelan menu/toolbar, Ctrl+P di panel pratinjau utama, `ApplyTicket`/`TicketMatches`/`DescribeTicket` tanpa printer |
| `PrintPreviewWindowBehaviorTests.cs` | 42 | `[W]` Kontrol jendela Pratinjau Cetak: pengaturan halaman membangun siklus baru, navigasi (Pertama/Sebelumnya/Berikutnya/Terakhir, kotak halaman, batas), zoom, Esc/Tutup, tombol dan perintah Cetak (via `ShowPrintDialogForTests`), galat dari callback/pembatalan, parse tertahan dan dokumen besar, snapshot tab yang ditutup atau dipindah |
| `WpfHostErrorTrackingTests.cs` | 4 | `[W]` Pencatat galat dispatcher: galat tak terduga menggagalkan test (sekali), galat dalam `ExpectUnhandled` tidak, galat sesudah scope tetap dilaporkan, galat saat memompa di dalam `Run` tidak menggantung proses |
| `Support/WpfHost.cs` (+ `DispatcherErrors`, `FailOnUnexpectedDispatcherErrorsAttribute`), `Support/AssemblyInfo.cs`, `Support/PrintTestKit.cs`, `TempDir.cs`, `TestLogRedirect.cs` | - | Infrastruktur (lihat berikut). `PrintTestKit`: halaman kecil `Small` 360 x 420, dokumen contoh, `StartBuild`/`WaitForEnd`/`WaitForPaginated`, pembaca teks glyph halaman XPS dan teks kaki |

Peta area -> berkas untuk keamanan dan keputusan ada di [SECURITY.md](SECURITY.md) dan [DESIGN-DECISIONS.md](DESIGN-DECISIONS.md).

## Cara menjalankan

Dari akar repo (PowerShell):

```powershell
dotnet test src/MdViewer.Tests                                          # semua test
dotnet test src/MdViewer.Tests --filter "FullyQualifiedName~DocumentTabTests"   # satu kelas
dotnet test src/MdViewer.Tests --filter "FullyQualifiedName~SingleInstance"     # semua kelas yang namanya memuat teks itu
dotnet test src/MdViewer.Tests --filter "DisplayName~IsRecoverable"             # berdasarkan nama test
dotnet test src/MdViewer.Tests --logger "console;verbosity=detailed"             # keluaran rinci per test
dotnet test src/MdViewer.Tests --blame-hang-timeout 2min                         # batalkan + laporkan test yang menggantung
dotnet test MdViewer.sln                                                         # lewat solution
```

- `--blame-hang-timeout` berguna di sini karena test WPF memompa dispatcher (`ChoiceDialog` modal, `Dispatcher.Invoke`): satu dialog yang
  tidak tertutup atau `Invoke` yang menunggu selamanya akan menggantungkan seluruh proses test. `WpfHost` sendiri menyerah bila thread
  STA tidak siap dalam 60 dtk (`Support/WpfHost.cs:57`). Nilai `2min` hanya contoh.
- Tidak ada `Trait`/kategori; penyaringan lewat nama kelas/test. Kelas dengan banyak test lambat bukan terkelompok khusus.
- Sebelum PR: `dotnet build MdViewer.sln` (harus 0 warning) lalu `dotnet test src/MdViewer.Tests` (semua hijau) - aturan CLAUDE.md.
- Test tertentu memerlukan waktu nyata: klien pipe macet menunggu batas baca 5 dtk (`Server_StalledClient_IsDroppedAfterTheReadTimeout_*`),
  pola katastrofik menunggu batas regex 2-4 dtk (`TimedOutPattern_*`, `TryFindAll_CatastrophicRegex_*`), render dokumen besar
  menunggu sampai 20 dtk bila lambat (`UiPump.Until`). Test pratinjau cetak menunggu dengan batas `PrintTestKit.Patience` (15 dtk),
  bukan jeda tetap: selesai begitu kondisi terpenuhi, dan kelambatan tampak sebagai gagal. Karena itu suite tidak seketika.

## Thread STA dan `WpfHost`

- WPF mensyaratkan thread STA dan `Application` adalah singleton per proses. `WpfHost.Instance` (`Support/WpfHost.cs`) membuat **satu**
  thread STA latar (`IsBackground = true`, nama `MdViewer.Tests STA`), membuat satu `App` (`ShutdownMode.OnExplicitShutdown`, memanggil
  `InitializeComponent()` sehingga resource tema/kontrol dimuat, **tanpa** `OnStartup`: tidak ada single-instance, tidak ada
  `MainWindow`), lalu `Dispatcher.Run()`.
- Semua test yang menyentuh WPF memanggil `WpfHost.Instance.Run(() => ...)` dan kelasnya `[Collection("Wpf")]`. `Run` bukan sekadar
  `Dispatcher.Invoke`: operasi dijalankan dengan `InvokeAsync` dan ditunggu per 100 ms (`WpfHost.cs:74-104`); galat milik test (mis. `Assert`)
  ditangkap di thread STA lalu dilempar ulang di thread test. Alasannya (komentar `WpfHost.cs:67-73`): bila callback tertunda melempar saat test
  memompa dispatcher (`UiPump`) dan galatnya ditelan, WPF meninggalkan frame bersarang itu selamanya sehingga `Invoke` biasa tak pernah
  kembali dan seluruh proses test menggantung tanpa pesan. Kini, bila galat tercatat dan operasi belum selesai dalam 3 dtk (`GraceAfterError`),
  test gagal dengan galat itu sebagai pesan. `Run` yang dipanggil dari thread STA (bersarang) langsung menjalankan fungsinya.
  `DocumentTab` menangkap `Dispatcher.CurrentDispatcher`, jadi harus dibuat **di dalam** `Run`.
- `DispatcherTimer` di tab (render 250 ms, statistik 300 ms, debounce watcher 400 ms, cari 150/250 ms) **hanya berdetak saat dispatcher
  dipompa**: di antara dua `Run`, atau di dalam satu `Run` bila test memompa sendiri lewat `UiPump.For`/`UiPump.Until`
  (`DispatcherFrame`, didefinisikan di `DocumentViewLifecycleTests.cs`). Karena itu banyak test memanggil
  `CheckExternalChange()`/`RefreshPreview()` langsung agar deterministik (komentar `DocumentTabTests.cs:8-11`).
- Koleksi `Wpf` bertanda `DisableParallelization = true` (`MarkdownEditingTests.cs:942`); kelas lain (tanpa WPF) dapat berjalan paralel
  satu sama lain, jadi tidak boleh berbagi state statis. State statis yang disentuh test dan harus dipulihkan: `DocumentView.BlockRemoteImages`,
  `ThemeManager` (mode/kamus), `CrashLog.LogPath`, `CultureInfo.CurrentCulture`.
- `TestLogRedirect` (`[ModuleInitializer]`) mengalihkan `CrashLog.LogPath` ke `%TEMP%\MdViewer.Tests\crash-<pid>.log` saat assembly test dimuat.
- **Pencatat galat dispatcher** (`Support/WpfHost.cs`). `OnStartup` `App` tidak berjalan di test, jadi `WpfHost` memasang sendiri
  `DispatcherUnhandledException` yang memasukkan galat ke `DispatcherErrors.Queue` (`WpfHost.Unhandled`) dan menandainya `Handled` (proses
  test tidak mati, seperti galat yang dipulihkan di aplikasi). Atribut tingkat assembly `[assembly: FailOnUnexpectedDispatcherErrors]`
  (`Support/AssemblyInfo.cs:4`, kelas `FailOnUnexpectedDispatcherErrorsAttribute`, turunan `BeforeAfterTestAttribute`) memeriksa di akhir
  **setiap test di koleksi `Wpf`**: bila ada galat yang belum diakui, test itu gagal (sekali; galat yang datang di antara dua test
  dibebankan ke test berikutnya). Test non-WPF tidak memicu pembuatan `WpfHost` (penanda `DispatcherErrors.Started`).
  - `WpfHost.ExpectUnhandled()` membuka scope untuk test yang memang menguji bahwa callback melempar: galat selama scope dianggap diharapkan
    (`scope.Errors` untuk diperiksa); galat yang datang sesudah scope ditutup tetap dilaporkan.
  - Pola yang dipakai test cetak untuk "tidak ada galat lolos": `var before = WpfHost.Unhandled.Count; ...; Assert.Equal(before, WpfHost.Unhandled.Count);`
    (selain pemeriksa otomatis di atas, supaya pesan kegagalannya langsung menunjuk test itu).
  - Perilaku WPF saat galat pecah di dalam frame `UiPump` tidak tetap (kadang frame ditinggalkan, kadang pompa selesai normal);
    `WpfHostErrorTrackingTests.AnErrorThrownWhilePumpingInsideRun_*` hanya menjamin: kembali (tidak menggantung), galat tercatat, host tetap bisa dipakai.
- `TempDir`: folder unik `%TEMP%\MdViewer.Tests\<guid>`; kelas test yang membuat tab memanggil `GC.Collect()` +
  `WaitForPendingFinalizers()` sebelum menghapusnya karena `BitmapImage` menahan handle file gambar sampai di-GC.
- Test mengakses field privat lewat refleksi (`UiPump.IsTimerEnabled`: `statsTimer`, `renderTimer`, `queryTimer`, `refreshTimer`) dan
  elemen XAML via `FindName`/field internal; mengganti nama keduanya memerlukan pembaruan test.

## Yang tidak teruji

Diperiksa dengan `grep` terhadap `src/MdViewer.Tests`; "tidak teruji" berarti tidak ada test yang menjalankan kodenya.

| Area | Keterangan |
| --- | --- |
| **`MainWindow`** (seluruh isi) | Tidak ada test yang membuat `MainWindow` (satu-satunya sebutan: komentar di `DocumentTabConflictEdgeTests.cs:71`; dua test cetak hanya membaca teks `MainWindow.xaml` dengan regex untuk pengkabelan perintah dan keunikan pintasan, `PrintContentAndCommandTests.cs:333, 369`). Tidak teruji: `OpenFile` (cek ukuran 50/500 MB, OOM, tab ganda), antrean konflik (`conflictQueue`/`conflictPromptOpen`), `OnSaveConflict`, `TrySave` (konfirmasi lossy), `preserveStoredSession`/`RestoreSession`/`SaveSettings`, `OpenFromOtherInstance`, seret-lepas, daftar berkas terakhir, handler perintah, `Window_Closing`, handler `ExportHtml_Executed`/`Print_Executed`/`PrintPreview_Executed` (penangkapan OOM/galat saat membuat jendela pratinjau dan `ShowDialog` modal). Logika di bawahnya (`DocumentTab`, `AppSettings`, `SingleInstance`, model cetak) teruji terpisah. |
| **`App`** | `OnStartup` (urutan single-instance), `OnDispatcherUnhandledException` (keputusan fatal/pulih + dialog), `OnExit`. `CrashLog.IsRecoverable`/`ShouldShowDialog` teruji; pemakaiannya tidak. |
| **Dialog** | `ChoiceDialog` diuji sendiri (2 test). Dialog sistem (`OpenFileDialog`, `SaveFileDialog`, `MessageBox`) tidak teruji. `PrintDialog` tidak pernah dibuka di test: tombol/perintah Cetak di jendela pratinjau diuji lewat hook `PrintPreviewWindow.ShowPrintDialogForTests` yang dipakai dengan hasil "batal" saja (`PrintPreviewWindowBehaviorTests.cs:889, 921`). `ConfirmPaperMatchesPreview` (dan `ChoiceDialog` konfirmasi kertas) tidak dijalankan test mana pun; yang teruji hanya pembantu murninya (`TicketMatches`, `ApplyTicket`, `DescribeTicket`, `PrintContentAndCommandTests.cs:421-528`). |
| **Cetak** | **Teruji:** pembuatan dokumen cetak (`BuildPrintDocument`/`PrintService.CreateDocument`: teks hasil, penanda gambar, kertas putih, tema Terang, ukuran halaman dan margin), paginasi dan ukuran halaman (seluruh kombinasi kertas x orientasi x margin, `FromPrintableArea` dengan nilai tak masuk akal), kaki halaman, `PreviewBuild` (tahap, halaman XPS, `Dispose`, paket di `PackageStore`), jendela pratinjau (pengaturan, navigasi, zoom, tutup, snapshot), pemblokiran gambar remote/UNC/ftp/`data:` pada dokumen cetak. **Tidak teruji:** pencetakan nyata (`dialog.PrintDocument` tidak pernah dijalankan: tidak ada printer fisik, driver, atau "Microsoft Print to PDF" di test), `PrintService.Print` dan `Print_Executed` (Ctrl+P), cabang "dialog diterima" termasuk `ConfirmPaperMatchesPreview`, tampilan piksel halaman (test membaca teks glyph, ukuran, dan kotak kaki, bukan gambar), gambar `http(s)` yang dimuat async di halaman XPS, OOM saat mencetak, dokumen Markdown sangat besar (kinerja tidak diukur otomatis; lihat README, Batasan yang diketahui). |
| **Visual/tampilan** | Tata letak, gaya `Controls.xaml`/`Preview.xaml`, tampilan tema sebenarnya, DPI tinggi, ikon, title bar gelap (`ApplyTitleBar`), keadaan kosong. Tema hanya diuji sebatas penukaran kamus dan matematika kontras; **kesamaan kunci Light/Dark tidak diuji** (hanya komentar). |
| **Sinkron scroll dan lompat anchor** | `ScrollToAnchor`/`FindHeading`/`AnchorHeadingRenderer` dan sinkron scroll editor-pratinjau tidak teruji (satu pemanggilan `ScrollToAnchor` hanya untuk "tidak melempar setelah Dispose"). Slug id heading (Markdig) teruji lewat `MarkdownSlugTests`. |
| **Klik tautan** | Pembungkus `DocumentView.OnHyperlink` (anchor, buka shell untuk `http(s)`/`mailto`, `File.Exists` lalu `RequestOpen`). Resolusi path (`MarkdownSupport.ResolveLinkTarget`) teruji di `LinkResolutionTests.cs`; `RequestOpen` teruji di `DocumentTabTests`. |
| **Zoom** | Pengkabelan Ctrl+roda dan perintah; hanya logika murni `ZoomLevel`. |
| **Watcher end-to-end** | `FileSystemWatcher` -> `dispatcher.BeginInvoke` -> `changeTimer` 400 ms -> `CheckExternalChange` tidak diuji sebagai satu rangkaian; test memanggil `CheckExternalChange()` langsung. Hingga 5 percobaan baca (1 + 4 ulangan) untuk file terkunci hanya diuji sebagai "tidak melempar". |
| **Symlink** | Butuh hak membuat symlink (Developer Mode atau administrator). Test symlink (`WriteBytesAtomic_SymlinkTarget_*`, `ResolveLinkTarget_FollowsAChain*`, `*_BrokenSymlink_*`) **langsung `return` (lulus tanpa menguji apa pun)** bila `CreateSymbolicLink` ditolak; hasil hijau tidak membuktikan jalur itu. Cabang ekspor "symlink di dalam folder menunjuk ke luar" (`MarkdownSupport.cs:333-334`) tidak punya test sama sekali. |
| **Keamanan pipe lintas-pengguna** | `CurrentUserOnly` tidak diuji menolak pengguna lain (butuh akun kedua). |
| **Skrip** | `scripts/*.ps1` (registrasi/hapus asosiasi, pembuat ikon) tidak punya test; CLAUDE.md melarang menjalankan skrip registri sungguhan dari test. |
| **Ketahanan memori** | Jalur `OutOfMemoryException` di `OpenFile`, `ExportHtml_Executed`, `Print_Executed`, `PrintPreview_Executed`, dan render; batas 50/500 MB. Satu-satunya yang teruji: `PreviewBuild.Guard` mengubah OOM yang dilempar penerima `Changed` (buatan test) menjadi tahap `Failed` tanpa lolos ke dispatcher (`SubscriberThatThrows_FailsTheBuild_AndNeverReachesTheDispatcher`, `PreviewBuildTests.cs:468`); kehabisan memori sungguhan pada dokumen sangat besar tidak diuji. |
| **Registri tema** | `ThemeManager.SystemUsesLightTheme` dan `Apply(System)` hanya diuji terhadap nilai registri mesin yang menjalankan test (hanya baca); perubahan tema sistem saat berjalan (`UserPreferenceChanged`) tidak diuji. |
| **Multi-instance sungguhan / multi-sesi** | Test memakai scope unik dalam satu proses; peluncuran proses kedua sungguhan dan sesi Remote Desktop tidak diuji. |
| **Keamanan thread `MarkdownPipeline`** | Pipeline statis dipakai dari thread latar dan UI; tidak ada test konkurensi. |

## Checklist uji manual sebelum rilis

Jalankan pada build **publish** (`dotnet publish src/MdViewer -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true`,
hasil di `src\MdViewer\bin\Release\net9.0-windows\win-x64\publish\MdViewer.exe`), idealnya di mesin/akun uji dengan .NET 9 Desktop Runtime.
Centang tiap butir; catat versi Windows dan DPI.

**Build dan paket**
- [ ] `dotnet build MdViewer.sln` 0 warning, 0 error; `dotnet test src/MdViewer.Tests` hijau (dan periksa test symlink berjalan, bukan lulus kosong, mis. dengan Developer Mode aktif).
- [ ] `MdViewer.exe` hasil publish berjalan tanpa folder lain; ikon dan judul jendela benar; versi sesuai ([../CHANGELOG.md](../CHANGELOG.md)).

**Startup dan single-instance**
- [ ] Tanpa argumen: keadaan kosong tampil; tanpa argumen + sesi tersimpan: tab, mode, dan posisi caret dipulihkan.
- [ ] `MdViewer.exe file.md` membuka tab; path relatif pada argumen terbuka benar.
- [ ] Saat berjalan, peluncuran kedua dengan file membuka tab di jendela yang ada dan membawanya ke depan (juga dari keadaan minimize); tanpa argumen hanya mengaktifkan jendela.
- [ ] Setelah instance utama ditutup, peluncuran berikutnya menjadi instance utama baru. (Opsional) sesi Windows lain punya instance sendiri.

**Buka dan encoding**
- [ ] Buka file UTF-8, UTF-8 BOM, UTF-16 LE/BE, Windows-1252: label encoding di status bar benar; simpan tanpa edit tidak mengubah byte (bandingkan hash).
- [ ] File ber-BOM dengan byte rusak: konfirmasi muncul sebelum simpan.
- [ ] File > 50 MB menanyakan konfirmasi; > 500 MB ditolak; buka file yang sama dua kali memilih tab yang ada; seret-lepas `.md` membuka tab, file non-markdown dilewati dengan pesan.

**Edit dan pratinjau**
- [ ] Mode Ctrl+1/2/3; ketik di mode Terpisah: pratinjau menyusul; scroll editor dan pratinjau tersinkron tanpa "bergetar".
- [ ] Dokumen besar (>= 200 ribu karakter): mengetik tetap lancar; pratinjau menyusul; tidak ada hasil render lama menimpa yang baru.
- [ ] Tautan `#anchor` melompat ke heading; tautan relatif ke `.md` membuka tab (dengan anchor); tautan `http(s)` membuka peramban; tautan ke file non-markdown tidak melakukan apa pun.
- [ ] Toolbar/pintasan format (Ctrl+B/I/E, Ctrl+Shift+L/Q, Ctrl+K, Ctrl+Shift+I, menu Heading): hasil benar dan **satu** Ctrl+Z membatalkan satu operasi; tidak aktif di mode Pratinjau atau saat fokus di panel cari.

**Cari dan ganti**
- [ ] Ctrl+F/Ctrl+H, F3/Shift+F3, Esc; penanda hasil tampak di kedua tema.
- [ ] Regex dengan `$` dan `.` pada dokumen CRLF cocok sebagaimana diharapkan; pola `(a+)+$` pada teks `aaaa...b` menampilkan "Pola terlalu lambat" dan tidak diulang pada tiap ketukan; Ganti Semua = satu Ctrl+Z.

**Simpan dan konflik**
- [ ] Ctrl+S (file ada), Simpan Sebagai, simpan dokumen tanpa judul; encoding/BOM dipertahankan; tidak ada sisa `~md*.tmp` di folder.
- [ ] Simpan ke file read-only / folder tanpa izin tulis menampilkan pesan (atau fallback tulis langsung bila hanya folder yang tak bisa dibuatkan file).
- [ ] Ubah file dari program lain: tab bersih termuat ulang otomatis dan Ctrl+Z mengembalikan; tab kotor menampilkan dialog "Muat dari Disk"/"Pertahankan Editor" (uji kedua pilihan).
- [ ] Ctrl+S pada tab yang file-nya diubah dari luar: dialog Timpa/Muat dari Disk/Batal (uji ketiganya; Muat dari Disk bisa di-Undo).
- [ ] Dua tab terkena perubahan eksternal sekaligus: dialog muncul berurutan, tidak bertumpuk. File dihapus dari luar: isi editor tetap.

**Ekspor dan privasi**
- [ ] Ekspor HTML dibuka di peramban: HTML mentah (`<script>`, `<img onerror>`) tampil sebagai teks; `[x](javascript:alert(1))` tidak bisa diklik; judul benar.
- [ ] Gambar di dalam folder dokumen tersemat; gambar di luar folder (absolut, `../`) menjadi `[gambar di luar folder dokumen tidak disertakan]`; > 2 MB atau SVG tetap relatif. Cari `C:\` dan `file:///` di berkas HTML: tidak ada. (Opsional, bila bisa membuat symlink: symlink menunjuk keluar folder.)
- [ ] Dokumen banyak gambar besar: ekspor selesai atau gagal dengan pesan ramah (tidak menutup aplikasi).

**Gambar di pratinjau**
- [ ] Gambar `http(s)` diblokir bawaan (`[gambar remote diblokir]`); menu Tampilan > "Muat gambar remote" menampilkannya dan berlaku untuk semua tab; pengaturan bertahan setelah restart.
- [ ] Gambar UNC (`file://host/share/a.png`), `ftp://`, dan `data:` diganti penanda; pantau lalu lintas jaringan (mis. `netstat`/Wireshark) saat membuka dokumen berisi UNC/FTP ke host uji: tidak ada koneksi.
- [ ] Gambar lokal rusak (`.png` berisi sampah) menampilkan `[gambar tidak dapat ditampilkan: ...]` dan sisa pratinjau tetap tampil.

**Tema, zoom, cetak**
- [ ] Terang/Gelap/Ikuti Sistem; ubah tema Windows saat mode Ikuti Sistem; title bar ikut; warna sintaks terbaca di kedua tema; tidak ada kontrol dengan warna tema lain.
- [ ] Zoom Ctrl+roda, Ctrl+=/-/0; nilai bertahan setelah restart; status bar menampilkan persen.
- [ ] Ctrl+P ke printer nyata dan ke "Microsoft Print to PDF" dari tema gelap: hasil berlatar putih, margin 0,75", kaki halaman "nama berkas ... Halaman X dari N" terbaca dan tidak terpotong printer, gambar remote mengikuti opsi blokir.
- [ ] Pratinjau Cetak (Ctrl+Shift+P atau Berkas > Pratinjau Cetak..., tombol toolbar): jendela terbuka tanpa membekukan UI ("Menyusun halaman..." lalu "Menyusun pratinjau..."), halaman tampil putih dengan kaki halaman; ubah Orientasi/Kertas/Margin dan kotak "Nama dan nomor halaman" menyusun ulang; navigasi (Pertama/Sebelumnya/Berikutnya/Terakhir, ketik nomor lalu Enter), zoom (Satu halaman/Lebar halaman/100%, +/-, Ctrl+roda), Esc menutup; edit teks di tab sesudah pratinjau dibuka tidak mengubah pratinjau yang terbuka.
- [ ] Cetak dari Pratinjau Cetak ke printer nyata dan ke "Microsoft Print to PDF": hasil sama dengan pratinjau (jumlah halaman, kaki halaman, margin). Ganti kertas atau orientasi di dialog Cetak (mis. Letter di pratinjau A4, atau Legal): muncul pertanyaan "Cetak sesuai pratinjau" atau "Batal"; Batal tidak mencetak, "Cetak sesuai pratinjau" mencetak dengan ukuran pratinjau. Tanpa printer terpasang: dialog Cetak sendiri yang melapor, aplikasi tidak crash.
- [ ] Dokumen sangat besar (mis. 500 KB dan 1,5 MB Markdown): pratinjau utama lambat dan memakai banyak memori (lihat README, Batasan); mode Editor tetap lancar. Buka Pratinjau Cetak untuk dokumen itu dan catat apakah UI tetap responsif, lamanya, dan pesan memori bila gagal (**belum pernah diukur**). Dokumen dengan gambar `http(s)` (blokir remote dimatikan): periksa apakah gambar tampil di halaman pratinjau (bisa kosong, lihat README).
- [ ] Dokumen dengan gambar rusak (`.png` berisi sampah) dan gambar UNC/`ftp:`: Pratinjau Cetak tetap tersusun dan gambar diganti penanda; tidak ada koneksi jaringan.

**Sesi dan pengaturan**
- [ ] Buka beberapa tab, tutup, buka lagi: sesi pulih. Buka lewat argumen file lalu tutup: sesi tersimpan sebelumnya **tidak** tertimpa; buka tab lagi di instance itu (dialog Buka/seret-lepas/Berkas Terakhir) lalu tutup: sesinya tersimpan.
- [ ] Berkas Terakhir (maks 10); entri yang file-nya hilang menampilkan pesan dan dihapus.
- [ ] `settings.json` dirusak manual: aplikasi tetap terbuka dengan bawaan.
- [ ] Setelah galat yang dipulihkan (mis. gambar rusak), `%LOCALAPPDATA%\MdViewer\crash.log` terisi; galat fatal: tidak ada cara memicu dari rilis (belum diverifikasi manual; gunakan build debug bila perlu).

**Skrip asosiasi file (di akun/VM uji)**
- [ ] `scripts\register-file-association.ps1 -WhatIf` tidak mengubah apa pun; tanpa `-WhatIf` mendaftar di HKCU; "Buka dengan" menampilkan MdViewer; `unregister-file-association.ps1` membersihkan (dan memulihkan nilai bawaan bila `-SetDefault` dipakai).

**Aksesibilitas ringan**
- [ ] Navigasi keyboard (Tab/Ctrl+Tab), fokus terlihat, nama kontrol terbaca pembaca layar (properti `AutomationProperties.Name` ada di XAML; kualitasnya belum diverifikasi).
