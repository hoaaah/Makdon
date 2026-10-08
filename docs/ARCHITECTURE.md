# Arsitektur MdViewer

**Tujuan:** menjelaskan komponen, tanggung jawabnya, alur utama, model thread, dan model state MdViewer.
**Pembaca:** pengembang yang akan mengubah kode `src/MdViewer`. Untuk alasan di balik keputusan lihat
[DESIGN-DECISIONS.md](DESIGN-DECISIONS.md); untuk batas keamanan lihat [SECURITY.md](SECURITY.md).

Konvensi rujukan: `berkas:baris` relatif terhadap akar repo; nomor baris sesuai kode saat dokumen ini ditulis dan bisa
bergeser. Pernyataan yang tidak bisa dibuktikan dari kode ditandai "belum diverifikasi".

## 1. Gambaran umum

- Aplikasi WPF .NET 9 (`net9.0-windows`, `WinExe`, Nullable + ImplicitUsings) - `src/MdViewer/MdViewer.csproj:4-12`.
- Pustaka pihak ketiga hanya dua: AvalonEdit 6.3.1.120 (editor) dan Markdig.Wpf 0.5.0.1 (parser Markdown + render
  `FlowDocument`) - `MdViewer.csproj:35-36`.
- Satu jendela (`MainWindow`) berisi banyak tab; satu tab = satu `DocumentTab` (model) dengan satu `DocumentView`
  (tampilan: editor + pratinjau + panel cari).
- Tidak ada `StartupUri`; `App.OnStartup` membuat `MainWindow` sendiri setelah urusan single-instance selesai
  (`src/MdViewer/App.xaml`, `src/MdViewer/App.xaml.cs:40`).
- Proyek test melihat tipe `internal` lewat `InternalsVisibleTo("MdViewer.Tests")` (`src/MdViewer/AssemblyInfo.cs:4`).
- Cetak punya dua jalur yang berbagi `PageLayout` dan `HeaderFooterPaginator`: Cetak langsung (Ctrl+P, sinkron) dan Pratinjau Cetak
  (Ctrl+Shift+P, modal; halaman disusun async ke paket XPS di memori lalu dicetak dari paket itu). Lihat 4.9 dan 4.10.

## 2. Komponen dan tanggung jawab

| Komponen | Berkas | Tanggung jawab |
| --- | --- | --- |
| `App` | `App.xaml(.cs)` | Titik masuk. Memasang penangan galat global (`DispatcherUnhandledException`, `AppDomain.UnhandledException`), mengubah argumen jadi path mutlak, memanggil `SingleInstance`, membuat `MainWindow`, menerima kiriman file dari instance lain (`OnFilesFromOtherInstance`), memutuskan galat dipulihkan atau fatal (`OnDispatcherUnhandledException`). |
| `MainWindow` | `MainWindow.xaml(.cs)` | Orkestrator UI: koleksi tab, menu/toolbar/pintasan (`CommandBinding`), dialog (buka/simpan, konfirmasi ukuran file, konflik), sesi dan berkas terakhir, zoom, tema, seret-lepas, ekspor HTML, Cetak langsung (`Print_Executed`, Ctrl+P) dan membuka Pratinjau Cetak modal (`PrintPreview_Executed`, Ctrl+Shift+P). Satu-satunya jalur membuka dokumen: `OpenFile`. Memegang antrean konflik (`conflictQueue`, `conflictPromptOpen`) dan `preserveStoredSession`. |
| `DocumentTab` | `DocumentTab.cs` | Model satu dokumen: `TextDocument` (+ `UndoStack`), path, `Encoding`, `IsLossyDecoded`, hash/stempel file di disk (`diskHash`, `diskStamp`, `pendingHash`, `pendingStamp`), `FileSystemWatcher`, `SaveTo`, `Reload`, `CheckExternalChange`, `ExportHtml`. Memuat enum `ViewMode`, `SaveConflictChoice`, dan `ExternalChangeException`. |
| `DocumentView` | `DocumentView.xaml(.cs)` | Tampilan satu tab: editor AvalonEdit, pratinjau `FlowDocumentScrollViewer`, `FindReplaceBar`; debounce + parse latar + pengecekan generasi untuk render pratinjau; sinkron scroll; statistik kata; zoom; tema editor; penanganan klik tautan; `Dispose()` menghentikan timer. Sisi cetak: `CapturePrintSnapshot` (salinan teks + folder dokumen + flag blokir remote + judul), `BuildPrintDocument` (dokumen tema Terang; varian statis `internal` dari snapshot atau dari AST), `ParsePrintSnapshot`, dan `CreateFlowDocument(throwOnFailure)`; juga meneruskan perintah `Print` dari panel `Preview` ke jalur jendela (lihat 4.9). Memegang flag statis `BlockRemoteImages`. |
| `FindReplaceBar` + `SearchResultsRenderer` | `FindReplaceBar.xaml(.cs)` | Panel cari/ganti inline (satu per `DocumentView`), debounce input 250 ms dan refresh 150 ms, penanda hasil digambar `SearchResultsRenderer` (`IBackgroundRenderer`), Ganti Semua sebagai satu Undo. Mencegah pola regex yang sudah kena batas waktu dijalankan ulang (`timedOutKey`). |
| `SearchEngine` | `SearchEngine.cs` | Logika cari/ganti murni (tanpa UI): `FindAll`, `TryFindAll`, `ReplaceAll`, indeks navigasi, `NormalizeLineEndings` (regex sadar CRLF), batas waktu regex, `MaxResults`. |
| `MarkdownEditing` | `MarkdownEditing.cs` | Operasi format Markdown pada `TextDocument` (tebal, miring, kode, heading, daftar, kutipan, tautan, gambar); tiap operasi dibungkus `BeginUpdate` = satu langkah Undo. `Apply(TextEditor, ...)` adalah adaptor ke editor. |
| `TextFileIO` | `TextFileIO.cs` | Baca byte tanpa mengunci, deteksi/encode encoding, penulisan atomik (`Write`, `WriteBytesAtomic`, fallback `WriteInPlace`), SHA-256 (`Hash`), `ResolveLinkTarget` (symlink). |
| `FileStamp` | `FileStamp.cs` | `record struct` ukuran + waktu tulis + waktu pengambilan; `IsReliable` (jendela "racy" 2 dtk) dan `SameFileAs`; jalan pintas agar file yang jelas tak berubah tidak dibaca/di-hash ulang. |
| `AppSettings` (+ `SessionState`, `SessionTab`) | `AppSettings.cs` | Pengaturan JSON di `%APPDATA%\MdViewer\settings.json`: tema, zoom, blokir gambar remote, berkas terakhir, sesi. `Load`/`Save` tidak melempar; `SaveMerged` menggabungkan `RecentFiles` dengan isi file; `Sanitize` merapikan data rusak. |
| `SingleInstance` | `SingleInstance.cs` | `Mutex` `Local\` per sesi Windows menentukan instance utama; named pipe (`CurrentUserOnly`) meneruskan path absolut dari peluncuran berikutnya. Tidak pernah melempar; gagal berarti jatuh ke instance baru. |
| `MarkdownSupport` | `MarkdownSupport.cs` | Dua pipeline Markdig terpisah (`Pipeline` pratinjau, `ExportPipeline` ekspor), `ClassifyUrl`/`UrlKind` (allowlist), `ResolveImageUrls` (pratinjau/cetak), `SanitizeForExport` + `ImageEmbedder` (ekspor), `IsAllowedLocalPath`, `ResolveLinkTarget` (path tautan dokumen, tanpa I/O). |
| `AnchorHeadingRenderer` | `AnchorHeadingRenderer.cs` | Pengganti `HeadingRenderer` Markdig.Wpf yang menyimpan id heading (slug GitHub) di `Paragraph.Tag` agar pratinjau bisa melompat ke `#anchor` (`DocumentView.FindHeading`). Dipasang di `DocumentView.BuildFlowDocument`. |
| `HtmlExporter` | `HtmlExporter.cs` | Ekspor ke satu file HTML mandiri: parse dengan `ExportPipeline`, `SanitizeForExport`, render, template CSS tertanam, tulis atomik UTF-8 tanpa BOM. |
| Model cetak: `PageLayout`, `PrintSnapshot`, `PrintSource`, `PrintService` (+ enum `PaperKind`, `PrintOrientation`, `MarginPreset`) | `PrintLayout.cs` | `PrintSnapshot` = salinan isi tab saat pratinjau dibuka. `PageLayout` = satu-satunya penurun `PageWidth`/`PageHeight`/`ColumnWidth`/`PagePadding` (A4/Letter x potret/lanskap x margin 0,5"/0,75"/1"; `FromPrintableArea` menolak ukuran tak masuk akal). `PrintSource` (`internal`) = hasil parse Markdown satu snapshot yang dipakai ulang antarsiklus (di thread latar bila teks >= `DocumentView.BackgroundParseChars`). `PrintService` = `CreateDocument`, `CreatePaginator` (+ kaki halaman), dan `Print` (Cetak langsung). |
| `HeaderFooterPaginator` | `HeaderFooterPaginator.cs` | `DocumentPaginator` pembungkus (`internal`): menggambar nama dokumen (kiri, dipotong satu baris) dan "Halaman X dari N" (kanan) di dalam margin bawah tiap halaman tanpa mengubah paginasi isi. Dipakai sama oleh pratinjau dan Cetak langsung. |
| `PreviewBuild` + `PreviewStage` | `PreviewBuild.cs` | Satu siklus penyusunan pratinjau (`internal`, UI thread): `Paginating`, `Rendering`, lalu `Ready` atau `Failed`. Paginasi FlowDocument latar, lalu halaman ditulis async ke paket XPS di memori (`PackageStore`) dan diekspos sebagai `FixedDocumentSequence`. Semua penangan peristiwa dibungkus `Guard`; `Dispose()` membatalkan dan menjadwalkan pembersihan paket. |
| `PrintPreviewWindow` | `PrintPreviewWindow.xaml(.cs)` | Jendela modal: `DocumentViewer` bertema, pilihan orientasi/kertas/margin/kaki halaman (tiap perubahan = `PreviewBuild` baru dari `PrintSource` yang sama), navigasi halaman, zoom, tombol Cetak (`PrintDialog` + `ConfirmPaperMatchesPreview`), dan panel galat (`ShowFailure`). |
| `ThemeManager` + `AppThemeMode` | `Theming.cs` | Menukar `ResourceDictionary` `Themes/Light.xaml`/`Dark.xaml` di `Application.Resources`, mengikuti pengaturan sistem (HKCU `AppsUseLightTheme` + `SystemEvents.UserPreferenceChanged`), title bar gelap (`DwmSetWindowAttribute`), `LoadDictionary` (dipakai cetak). |
| `EditorTheme` | `EditorTheme.cs` | Mewarnai definisi highlighting "MarkDown" AvalonEdit dari brush `Syntax*Brush` dan menjaga kontras >= 4,5:1 terhadap latar editor (`EnsureContrast`, rumus WCAG). |
| `CrashLog` | `CrashLog.cs` | Catatan galat `%LOCALAPPDATA%\MdViewer\crash.log` (batas 512 KB), `IsRecoverable` (galat yang aman dilanjutkan), `ShouldShowDialog` (redam dialog berulang 10 dtk). Menulis log tidak pernah melempar. |
| `ChoiceDialog` + `DialogChoice<T>` | `ChoiceDialog.xaml(.cs)` | Dialog modal bertema dengan tombol berlabel; Esc/X = `cancelValue`. Dipakai untuk semua dialog konflik. |
| `AppCommands` | `AppCommands.cs` | `RoutedUICommand` khusus aplikasi (zoom, ekspor, pratinjau cetak, cari berikutnya/sebelumnya, tema, format) + `FormatOf`. |
| Utilitas kecil | `ZoomLevel.cs`, `TextStats.cs`, `EncodingNames.cs`, `MarkdownFiles.cs`, `Converters.cs`, `ViewModeConverter.cs`, `NotNullConverter.cs` | Aturan zoom 50-300%, hitung kata/karakter per potongan 64 KB, label encoding status bar, daftar ekstensi Markdown (`.md .markdown .mdown .mkd .txt`), konverter binding. |
| Tema XAML | `Themes/Light.xaml`, `Dark.xaml`, `Controls.xaml`, `Preview.xaml` | `Light`/`Dark`: 43 brush semantik dengan kunci identik. `Controls`: gaya kontrol (semua warna `DynamicResource`). `Preview`: menimpa gaya `Styles.*` Markdig.Wpf untuk `FlowDocument`. Digabung di `App.xaml`. |

## 3. Diagram komponen

Panah = "memakai". Hanya dependensi yang terlihat di kode; AvalonEdit, Markdig.Wpf, dan API sistem digambar sebagai
kotak luar.

```mermaid
flowchart LR
    subgraph UI["Lapisan UI (UI thread)"]
        App["App"]
        MW["MainWindow"]
        CD["ChoiceDialog"]
        DV["DocumentView"]
        FB["FindReplaceBar"]
        Cmd["AppCommands"]
        PPW["PrintPreviewWindow"]
    end

    subgraph Model["Model dokumen"]
        DT["DocumentTab"]
        FS["FileStamp"]
    end

    subgraph Logic["Logika murni / layanan"]
        SE["SearchEngine"]
        ME["MarkdownEditing"]
        MS["MarkdownSupport"]
        AH["AnchorHeadingRenderer"]
        HE["HtmlExporter"]
        IO["TextFileIO"]
        AS["AppSettings"]
        TM["ThemeManager"]
        ET["EditorTheme"]
        CL["CrashLog"]
        SI["SingleInstance"]
        PL["PrintLayout.cs: PageLayout, PrintSnapshot, PrintSource, PrintService"]
        HF["HeaderFooterPaginator"]
        PB["PreviewBuild"]
    end

    subgraph Ext["Pihak luar"]
        AE[("AvalonEdit")]
        MD[("Markdig.Wpf")]
        FSW[("FileSystemWatcher")]
        PIPE[("Named pipe + Mutex")]
        REG[("Registry HKCU (baca tema)")]
        DISK[("Disk")]
        XPS[("XPS di memori: XpsDocumentWriter, PackageStore")]
        PD[("PrintDialog + driver printer")]
    end

    App --> SI
    App --> MW
    App --> CL
    App --> TM
    SI --> PIPE
    SI --> CL
    MW --> DT
    MW --> CD
    MW --> AS
    MW --> TM
    MW --> Cmd
    MW --> CL
    MW --> DV
    DT --> DV
    DT --> IO
    DT --> FS
    DT --> HE
    DT --> FSW
    FS --> IO
    DV --> FB
    DV --> ME
    DV --> MS
    DV --> AH
    DV --> TM
    DV --> CL
    DV --> AE
    DV --> MD
    FB --> SE
    ME --> AE
    HE --> MS
    HE --> IO
    MS --> MD
    MS --> IO
    AH --> MD
    AS --> IO
    AS --> TM
    TM --> ET
    TM --> REG
    IO --> DISK
    MW --> PPW
    MW --> PL
    MW --> PD
    PPW --> PB
    PPW --> PL
    PPW --> CD
    PPW --> TM
    PPW --> CL
    PPW --> PD
    PB --> PL
    PB --> XPS
    PB --> CL
    PL --> HF
    PL --> DV
    DV --> PL
```

Catatan arah dependensi:

- `DocumentTab` membuat `DocumentView` di konstruktornya (`DocumentTab.cs:67`) dan `DocumentView` memegang referensi balik
  ke `DocumentTab` (untuk `Document`, `Mode`, `FilePath`, `SetCaret`, `SetStats`, `RequestOpen`). Keduanya sepasang.
- `MainWindow` tidak pernah menyentuh `TextFileIO`/`FileStamp` langsung; semua I/O dokumen lewat `DocumentTab`.
- `AppSettings.ParsedTheme` memakai `ThemeManager.Parse` (`AppSettings.cs:53`), jadi `AppSettings` bergantung pada `Theming.cs`.
- `MarkdownSupport` memakai `TextFileIO.ReadBytes`/`ResolveLinkTarget` untuk menyematkan gambar (`MarkdownSupport.cs:333,340`).
- `PrintLayout.cs` dan `DocumentView` saling bergantung: `DocumentView.CapturePrintSnapshot` mengembalikan `PrintSnapshot`, sedangkan
  `PrintSource`/`PrintService` memanggil statis `internal` `DocumentView.ParsePrintSnapshot`/`BuildPrintDocument`
  (`PrintLayout.cs:99,121,133`). `PrintPreviewWindow` dan `PreviewBuild` tidak menyentuh `DocumentTab`/`DocumentView`: yang mereka pegang
  hanya `PrintSnapshot`, sehingga menutup atau mengedit tab tidak mengubah pratinjau yang sedang terbuka.
- Pratinjau (`PreviewBuild`) dan Cetak langsung (`PrintService.Print`) memakai `PageLayout.Apply` dan `PrintService.CreatePaginator` yang
  sama, jadi paginasi dan kaki halamannya sama (`PrintLayout.cs:73,142`; `MainWindow.xaml.cs:593-594`).

## 4. Alur utama

### 4.1 Startup dan single-instance

Kode: `App.xaml.cs:11-45`, `SingleInstance.cs:41-58, 85-133, 163-201`, `MainWindow.xaml.cs:36-60`.

```mermaid
sequenceDiagram
    autonumber
    participant A as App.OnStartup
    participant S as SingleInstance
    participant P as Server pipe instance utama
    participant M as MainWindow

    A->>A: pasang handler galat global, ubah argumen jadi path mutlak
    A->>S: Create() lalu Mutex Local MdViewer.SingleInstance dengan SID pengguna
    alt mutex baru dibuat (IsPrimary)
        A->>S: StartServer(OnFilesFromOtherInstance)
        S-->>P: Task.Run ServerLoopAsync
        A->>M: new MainWindow(files) lalu Show
        M->>M: ada argumen: OpenFile tiap file. Tanpa argumen: RestoreSession
    else mutex sudah ada (bukan primary)
        A->>S: AllowForeground lalu TrySendToPrimary(files, 3 dtk)
        S->>P: pipe dengan nama SID + id sesi Windows, pesan MDVIEWER1 + path
        alt terkirim
            A->>A: Shutdown dan return
        else gagal (timeout atau pipe tidak ada)
            A->>M: tetap buka instance baru (tanpa server)
        end
    end
    P->>P: ParseMessage: header harus pas, hanya path absolut penuh, maks 64
    P-->>A: onFiles (thread latar)
    A->>M: Dispatcher.BeginInvoke ke OpenFromOtherInstance
    M->>M: OpenUserFile tiap path, Activate, Topmost sesaat
```

Hal yang perlu diketahui:

- Bila mutex tidak bisa dibuat, `Create` mengembalikan `IsPrimary = true` tanpa mutex; `StartServer` lalu tidak berbuat apa-apa
  karena `mutex is null` (`SingleInstance.cs:56, 87`). Aplikasi tetap jalan, hanya tanpa single-instance.
- `window.Closed += singleInstance.Dispose` didaftarkan setelah `MainWindow` mendaftarkan `Window_Closed` sendiri, sehingga sesi
  disimpan dulu baru server berhenti (`App.xaml.cs:41-43`, `MainWindow.xaml.cs:54`).
- Instance utama yang sedang menutup mengabaikan kiriman file (`closing`/`closed`, `MainWindow.xaml.cs:65`).

### 4.2 Buka file

Kode: `MainWindow.OpenFile` (`MainWindow.xaml.cs:236-275`), `DocumentTab.Load` (`DocumentTab.cs:135-141`), `TextFileIO.ReadBytes/Decode`.

```mermaid
sequenceDiagram
    autonumber
    actor U as Pengguna
    participant M as MainWindow
    participant T as DocumentTab
    participant IO as TextFileIO
    participant V as DocumentView

    U->>M: dialog Buka, seret-lepas, Berkas Terakhir, tautan relatif, atau argumen
    M->>M: Path.GetFullPath (path tidak valid: pesan galat)
    alt path sudah terbuka di tab lain
        M->>M: pilih tab itu, TrackRecent (bila trackRecent), ScrollToAnchor bila ada
    else file baru
        M->>M: ConfirmFileSize: tanya di atas 50 MB, tolak di atas 500 MB
        M->>T: DocumentTab.Load(path)
        T->>T: FileStamp.TryReadQuietly (diambil sebelum membaca)
        T->>IO: ReadBytes (FileShare ReadWrite dan Delete)
        T->>IO: Decode(bytes, out lossy)
        T->>T: diskHash = SHA-256, buat TextDocument, UndoStack ClearAll + MarkAsOriginalFile
        T->>V: new DocumentView(this)
        T->>T: StartWatching (FileSystemWatcher pada file sebenarnya bila symlink)
        M->>M: Attach (zoom, event), AddTab, TrackRecent (bila trackRecent), ScrollToAnchor
    end
    Note over M: OutOfMemoryException dan IOException atau UnauthorizedAccessException ditangkap, tampil ke pengguna, tab tidak dibuat
```

`OpenFile` tidak memeriksa ekstensi; penyaringan ekstensi hanya di seret-lepas (`Window_PreviewDrop`), klik tautan
relatif (`DocumentView.OnHyperlink` memakai `MarkdownSupport.ResolveLinkTarget`), dan filter dialog. Ekstensi yang diterima adalah `MarkdownFiles.IsMarkdown`
(`.md .markdown .mdown .mkd .txt`); asosiasi file (skrip) hanya `.md`/`.markdown`.

### 4.3 Edit, lalu render pratinjau (debounce, parse latar, generation check)

Kode: `DocumentView.xaml.cs:97-108, 332-452`.

```mermaid
sequenceDiagram
    autonumber
    participant E as Editor AvalonEdit
    participant V as DocumentView di UI thread
    participant BG as Thread pool
    participant P as Preview FlowDocumentScrollViewer

    E->>V: Document.TextChanged
    V->>V: previewStale = true, restart statsTimer (300 ms, 2000 ms bila 1 juta karakter atau lebih)
    V->>V: bila pratinjau tampak: restart renderTimer (250 ms, 800 ms dari 200 rb, 1500 ms dari 1 juta karakter)
    V->>V: renderTimer.Tick memanggil RenderPreview
    alt Document.Version sama dengan render terakhir
        V->>V: lewati, CancelPendingRender (renderGeneration++)
    else teks sama dengan lastRenderedText (mis. setelah Undo)
        V->>V: hanya perbarui lastRenderedVersion
    else teks di bawah 100 rb karakter
        V->>V: ParseDocument sinkron (Markdig Parse + ResolveImageUrls)
        V->>P: ApplyParsed lalu CreateFlowDocument lalu Preview.Document
    else teks 100 rb karakter atau lebih
        V->>V: generation = ++renderGeneration, renderInFlight = true
        V->>BG: Task.Run(ParseDocument)
        BG-->>V: MarkdownDocument (lanjutan kembali di UI thread)
        alt generation tidak sama dengan renderGeneration
            V->>V: hasil dibuang (ada render lebih baru atau tab ditutup)
        else generasi masih terbaru
            V->>P: ApplyParsed lalu CreateFlowDocument lalu Preview.Document
        end
    end
    V->>V: BeginInvoke(Loaded): pulihkan scroll (Terpisah: ikut editor), terapkan pendingAnchor
```

Poin penting:

- `CreateFlowDocument` wajib di UI thread; hanya parse + `ResolveImageUrls` yang pindah ke thread latar (komentar di
  `DocumentView.xaml.cs:395`).
- Teks dokumen dan nilai `BlockRemoteImages` diambil di UI thread sebelum `Task.Run` (`:345, 354`), sehingga thread latar tidak
  membaca state UI.
- `Dispose()` menaikkan `renderGeneration`, jadi hasil latar yang tiba sesudah tab ditutup dibuang (`:140`; test
  `BackgroundRender_AfterDispose_IsDiscarded_AndNothingIsShown`).
- Galat render dicatat ke `CrashLog` dan pratinjau diganti dokumen pesan galat; isi file tidak tersentuh (`ShowRenderError`,
  `CreateFlowDocument`, `ReplaceUnloadableImages`). `OutOfMemoryException` diperlakukan berbeda per jalur: pada render sinkron
  (< 100 rb karakter, `DocumentView.xaml.cs:364`) OOM sengaja lolos dan menjadi galat fatal; di `RenderInBackground` (`:406`)
  semua galat termasuk OOM ditangkap dan ditampilkan sebagai dokumen galat. Jalur cetak/pratinjau cetak berbeda: kegagalan akhir
  render dilempar (`CreateFlowDocument(throwOnFailure: true)`, `:459-483`), bukan diganti dokumen galat, karena dokumen galat memuat
  path `crash.log` dan tidak boleh sampai ke kertas (lihat 4.9 dan [ADR-24](DESIGN-DECISIONS.md#adr-24-dokumen-galat-tidak-pernah-dicetak)).

### 4.4 Simpan (atomik, deteksi konflik)

Kode: `MainWindow.Save/SaveAs/TrySave` (`:403-448`), `DocumentTab.SaveTo/SaveCore` (`:149-200`),
`TextFileIO.Write/WriteBytesAtomic` (`:134-202`).

```mermaid
sequenceDiagram
    autonumber
    actor U as Pengguna
    participant M as MainWindow
    participant T as DocumentTab
    participant IO as TextFileIO
    participant D as ChoiceDialog

    U->>M: Ctrl+S
    M->>M: FilePath kosong: SaveAs (SaveFileDialog, tolak bila path dipakai tab lain). Selain itu TrySave
    M->>M: IsLossyDecoded: konfirmasi MessageBox sebelum lanjut
    M->>T: SaveTo(path): saving = true, changeTimer.Stop
    T->>T: path sama dan diskHash ada: IsChangedOnDisk(path)
    Note right of T: stempel andal dan sama berarti tidak berubah. Selain itu baca byte lalu bandingkan SHA-256 dengan diskHash
    alt isi di disk berbeda
        T->>M: event SaveConflict (OnSaveConflict). Tanpa penangan: ExternalChangeException
        M->>D: Timpa, Muat dari Disk, Batal (bawaan Batal)
        D-->>M: pilihan
        alt Batal
            T-->>M: false, tidak menulis
        else Muat dari Disk
            T->>T: Reload() dibaca ulang saat ini, satu langkah Undo, lalu false
        else Timpa
            T->>T: lanjut menulis
        end
    end
    T->>IO: Write(path, Document.Text, Encoding)
    IO->>IO: Encode (BOM dipertahankan, di luar jangkauan encoding: naik ke UTF-8 tanpa BOM)
    IO->>IO: WriteBytesAtomic: file sementara ~md plus 8 hex .tmp (CreateNew) lalu Flush(true) lalu File.Replace atau File.Move
    alt file sementara tak bisa dibuat (UnauthorizedAccess atau PathTooLong)
        IO->>IO: WriteInPlace (tulis dari awal lalu potong, tidak atomik)
    end
    IO-->>T: (encoding sebenarnya, hash byte yang ditulis)
    T->>T: diskHash, diskStamp diperbarui, pending dihapus, IsLossyDecoded = false, MarkAsOriginalFile
    T->>T: StartWatching bila path berubah, Raise FilePath/PathText/EncodingLabel hanya bila berubah
    M->>M: finally: ProcessConflictQueue (kecuali sedang closing)
```

Galat `IOException`/`UnauthorizedAccessException` dari `SaveTo` ditangkap `TrySave` dan ditampilkan; tab tetap kotor
dengan path lama.

### 4.5 Perubahan file dari luar (watcher, hash/FileStamp, antrean konflik, dialog)

Kode: `DocumentTab.cs:227-277, 347-391`, `MainWindow.xaml.cs:53, 316-366`.

```mermaid
sequenceDiagram
    autonumber
    participant W as FileSystemWatcher di thread pool
    participant T as DocumentTab di UI thread
    participant M as MainWindow
    participant D as ChoiceDialog

    W->>T: Changed, Created, atau Renamed lalu dispatcher.BeginInvoke
    T->>T: changeRetries = 0, restart changeTimer 400 ms (debounce)
    Note over M,T: Jendela aktif kembali: Current.CheckExternalChange() (hanya tab aktif)
    T->>T: CheckExternalChange. Berhenti bila disposed, saving, tanpa path, atau diskHash null
    T->>T: FileStamp.TryRead: null (dihapus atau dipindah) berarti biarkan editor
    T->>T: stempel andal dan sama dengan diskStamp atau pendingStamp berarti selesai
    T->>T: ReadBytes. Terkunci: ulangi lewat timer, hingga 5 percobaan baca (1 + 4 ulangan)
    T->>T: hash = SHA-256(bytes)
    alt hash sama dengan diskHash
        T->>T: perbarui diskStamp, hapus pending (touch atau dikembalikan)
    else hash sama dengan pendingHash
        T->>T: perbarui pendingStamp, sudah dilaporkan
    else tab bersih (bukan IsDirty)
        T->>T: ApplyDiskContent: ganti teks sebagai satu langkah Undo
    else tab kotor
        T->>T: pendingHash dan pendingStamp diisi (diskHash tidak diubah)
        T->>M: event ExternalChangeConflict
        M->>M: OnExternalChangeConflict: antre tanpa duplikat, ProcessConflictQueue
        loop selama antrean tidak kosong dan tidak ada dialog lain (conflictPromptOpen)
            M->>D: Muat dari Disk atau Pertahankan Editor (bawaan)
            alt Muat dari Disk
                M->>T: Reload()
            else Pertahankan Editor
                M->>T: KeepEditorVersion(): diskHash = pendingHash
            end
        end
    end
```

Tab yang ditutup atau yang konfliknya sudah selesai (`HasExternalConflict == false`) dilewati saat antrean diproses
(`MainWindow.xaml.cs:334`).

### 4.6 Ekspor HTML

Kode: `MainWindow.ExportHtml_Executed` (`:551-581`), `DocumentTab.ExportHtml`, `HtmlExporter.ToHtmlDocument/ExportToFile`,
`MarkdownSupport.SanitizeForExport`.

```mermaid
sequenceDiagram
    autonumber
    actor U as Pengguna
    participant M as MainWindow
    participant T as DocumentTab
    participant H as HtmlExporter
    participant S as MarkdownSupport
    participant IO as TextFileIO

    U->>M: Ctrl+Shift+E lalu SaveFileDialog
    M->>T: ExportHtml(outputPath)
    T->>H: ExportToFile(Document.Text, path, judul = nama file, baseDir = folder dokumen atau null)
    H->>H: Markdig.Markdown.Parse(teks, MarkdownSupport.ExportPipeline) (pipeline dari MarkdownSupport: HTML mentah dimatikan, GenericAttributes dan MediaLink dibuang)
    H->>S: SanitizeForExport(parsed, baseDir)
    S->>S: tautan: hanya http, https, mailto, anchor, relatif. selain itu href diganti anchor kosong
    S->>S: gambar: ClassifyUrl lalu ImageEmbedder (di bawah folder dokumen, maks 2 MB, anggaran 30 MB, cache per path)
    H->>H: HtmlRenderer, lalu isi template satu pass (judul di-HtmlEncode)
    H->>IO: Write(outputPath, html, UTF-8 tanpa BOM) atomik
    alt OutOfMemoryException atau galat lain
        M->>M: CrashLog.Write lalu pesan ramah, aplikasi tetap hidup
    end
```

Ekspor memakai teks editor saat ini (termasuk yang belum disimpan), bukan versi di disk.

### 4.7 Pemulihan dan penyimpanan sesi

Kode: `MainWindow.xaml.cs:36-60, 187-232, 714-734`, `AppSettings.SaveMerged` (`AppSettings.cs:96-107`).

```mermaid
sequenceDiagram
    autonumber
    participant M as MainWindow
    participant S as AppSettings
    participant F as settings.json

    Note over M: field settings = AppSettings.Load() (file hilang atau rusak berarti nilai bawaan)
    alt ada argumen file
        M->>M: preserveStoredSession = true, OpenFile tiap argumen
    else tanpa argumen
        M->>M: RestoreSession: tiap SessionTab yang file-nya ada, OpenFile(trackRecent false), Mode, RestoreCaret, tab aktif
    end
    Note over M: pengguna membuka tab lagi (OpenUserFile) lalu preserveStoredSession = false
    M->>M: Window_Closing: tanyakan tiap tab kotor (batal menahan penutupan)
    M->>M: Window_Closed lalu SaveSettings
    alt preserveStoredSession dan bukan forceSession
        M->>S: SaveMerged(keepStoredSession true): Session di file dipertahankan
    else
        M->>S: Session = tab ber-FilePath (path, Mode, CaretOffset, ActiveIndex)
        M->>S: SaveMerged
    end
    S->>F: Load ulang, gabung RecentFiles (milik sendiri di depan, maks 10), TextFileIO.Write atomik
```

Hanya tab yang punya path yang masuk sesi; dokumen "Tanpa Judul" dan isi yang belum disimpan tidak dipulihkan
(README, bagian Batasan). Pada galat fatal `TrySaveSession` memaksa penyimpanan sesi (`forceSession: true`).

### 4.8 Penanganan galat

Kode: `App.xaml.cs:64-102` (cabang `shuttingDownAfterCrash`: `:67, 75`), `CrashLog.cs:58-91`.

```mermaid
flowchart TD
    X["Exception di UI thread"] --> R{"CrashLog.IsRecoverable dan belum shuttingDownAfterCrash?"}
    R -- ya --> L1["CrashLog.Write 'Galat dipulihkan'"]
    L1 --> Q{"ShouldShowDialog? (tipe + pesan yang sama dalam 10 dtk ditekan)"}
    Q -- ya --> D1["MessageBox peringatan"]
    Q -- tidak --> H
    D1 --> H["e.Handled = true, aplikasi lanjut"]
    R -- tidak --> L2["CrashLog.Write 'Galat fatal'"]
    L2 --> SD{"shuttingDownAfterCrash sudah true? (galat saat penutupan akibat galat sebelumnya)"}
    SD -- ya --> RT["return tanpa dialog dan tanpa menyimpan sesi lagi (e.Handled tetap false), proses keluar"]
    SD -- tidak --> S["shuttingDownAfterCrash = true, MainWindow.TrySaveSession"]
    S --> D2["MessageBox galat"]
    D2 --> E["e.Handled = false, runtime mengakhiri proses"]
```

### 4.9 Pratinjau Cetak (Ctrl+Shift+P) dan Cetak

Kode: `MainWindow.PrintPreview_Executed` (`MainWindow.xaml.cs:604-631`) dan `Print_Executed` (`:583-601`), `PrintPreviewWindow`
(`PrintPreviewWindow.xaml.cs:45-120, 421-486`), `PrintSource`/`PrintService` (`PrintLayout.cs:92-156`), `PreviewBuild`
(`PreviewBuild.cs:91-202`), `HeaderFooterPaginator.cs`.

```mermaid
sequenceDiagram
    autonumber
    actor U as Pengguna
    participant M as MainWindow
    participant V as DocumentView
    participant W as PrintPreviewWindow
    participant S as PrintSource
    participant BG as Thread pool
    participant B as PreviewBuild
    participant X as XpsDocumentWriter dengan paket di memori
    participant DV as DocumentViewer
    participant D as PrintDialog

    U->>M: Ctrl+Shift+P (AppCommands.PrintPreview)
    M->>V: CapturePrintSnapshot
    V-->>M: PrintSnapshot (teks, folder dokumen, flag blokir remote, judul)
    M->>W: new PrintPreviewWindow(snapshot) lalu ShowDialog (modal)
    W->>S: new PrintSource(snapshot)
    alt teks di bawah 100 rb karakter
        S->>S: ParsePrintSnapshot sinkron (galat disimpan di Task, tidak dilempar)
    else 100 rb karakter atau lebih
        S->>BG: Task.Run ParsePrintSnapshot
    end
    W->>B: StartBuild: new PreviewBuild(source, layout, kaki halaman) lalu Start
    B->>S: tunggu Parsed (dilanjutkan di UI thread lewat dispatcher)
    B->>B: BeginPagination: CreateDocument (tema Terang, ukuran halaman layout), paginasi latar dinyalakan
    loop selama paginasi
        B-->>W: Changed (tahap Paginating, halaman sejauh ini)
    end
    B->>B: paginasi selesai, BeginRender: tahap Rendering, paket XPS di MemoryStream dan PackageStore
    B->>X: WriteAsync(HeaderFooterPaginator di atas paginator FlowDocument)
    loop tiap halaman ditulis
        X-->>B: WritingProgressChanged
        B-->>W: Changed (tahap Rendering, halaman X dari N)
    end
    X-->>B: WritingCompleted
    B->>B: Pages = GetFixedDocumentSequence, tahap Ready
    B-->>W: Changed
    W->>DV: Viewer.Document = Pages, zoom mode muat diterapkan setelah tata letak
    U->>W: ganti orientasi, kertas, margin, atau kaki halaman
    W->>B: Dispose siklus lama, lalu PreviewBuild baru dari PrintSource yang sama (tanpa parse ulang)
    U->>W: Cetak (tombol atau Ctrl+P), hanya bila Ready
    W->>D: ApplyTicket (kertas dan orientasi pratinjau), ShowDialog
    D-->>W: diterima
    W->>W: ConfirmPaperMatchesPreview(dialog)
    alt kertas atau orientasi dialog berbeda dari pratinjau
        W->>U: ChoiceDialog: Cetak sesuai pratinjau atau Batal (bawaan)
    end
    W->>D: PrintDocument(Pages.DocumentPaginator, judul) sinkron di UI thread
    U->>W: Esc atau Tutup
    W->>B: Dispose
```

Poin penting:

- **Snapshot.** `CapturePrintSnapshot` (`DocumentView.xaml.cs:585-586`) menyalin teks, folder dokumen, nilai `BlockRemoteImages`, dan judul
  saat pratinjau dibuka. Penyuntingan, penutupan tab, atau `Simpan Sebagai` sesudahnya tidak mengubah pratinjau maupun penyusunan
  ulangnya (tes `Snapshot_*` di `PrintPreviewWindowBehaviorTests.cs`). Pratinjau modal, jadi editor juga tidak bisa berubah selama terbuka.
- **Parse.** Teks di bawah `DocumentView.BackgroundParseChars` (100 rb karakter) diparse sinkron di konstruktor `PrintSource`
  (`PrintLayout.cs:103-104`); sisanya lewat `Task.Run` (`:99`). AST dipakai ulang oleh semua siklus jendela yang sama, jadi ganti kertas
  atau margin hanya membuat `FlowDocument` baru (`PrintSource.CreateDocument`), bukan parse ulang. Gambar remote/UNC/`ftp:`/`data:`
  sudah diblokir di AST oleh `ResolveImageUrls` (lewat `ParseDocument`), sama seperti pratinjau utama.
- **Paginasi latar** adalah `IsBackgroundPaginationEnabled` pada paginator FlowDocument (`PreviewBuild.cs:130`). Ia berjalan di UI thread
  dalam potongan lewat dispatcher, bukan di thread terpisah (komentar `PreviewBuild.cs:250`), sehingga UI tetap responsif tetapi
  `Dispose()` harus mematikannya. Penulisan XPS (`WriteAsync`) juga mengirim kabar lewat dispatcher; bagian dalam WPF-nya belum diverifikasi.
- **Kenapa XPS.** `DocumentViewer` hanya menampilkan dokumen tetap, bukan `FlowDocument` ([ADR-19](DESIGN-DECISIONS.md#adr-19-pratinjau-cetak-lewat-paket-xps-di-memori)).
  `HeaderFooterPaginator` ikut tertulis di tiap halaman XPS, sehingga mengganti kaki halaman = siklus baru.
- **Cetak dari pratinjau** mencetak halaman yang tampil (`build.Pages.DocumentPaginator`, paket XPS yang sama), bukan hasil paginasi
  ulang (`PrintPreviewWindow.xaml.cs:446-447`), sinkron di UI thread; indikator "Mencetak..." digambar dulu lewat
  `Dispatcher.Invoke(..., Render)` (`:442-444`). Tombol/perintah hanya aktif bila tahap `Ready`, `Pages` ada, tidak sedang mencetak, dan
  tidak ada galat jendela (`CanPrint`, `:96`). Galat selain `OutOfMemoryException` (driver/antrean) ditampilkan sebagai `MessageBox`
  "Gagal mencetak." (`:449-453`); OOM sengaja tidak ditangkap di situ dan jatuh ke penangan global ([R8](SECURITY.md#3-risiko-residual-dan-batasan-yang-diketahui)).
- **`ConfirmPaperMatchesPreview`** (`:466-486`): halaman XPS berukuran tetap, jadi kertas/orientasi yang diubah pengguna di dialog Cetak
  tidak ditimpa diam-diam dan tidak dipaksakan tanpa tanya ([ADR-22](DESIGN-DECISIONS.md#adr-22-pratinjau-punya-pengaturan-kertas-sendiri-dan-konfirmasi-bila-dialog-cetak-berbeda)).
  `TicketMatches` (`:518-542`) membandingkan lewat nama kertas (termasuk varian `Rotated`) atau lebar/tinggi (toleransi 4 DIP).
- **Ctrl+P dari panel pratinjau utama.** `FlowDocumentScrollViewer` punya pengikatan kelas untuk `Print` yang mendahului pengikatan
  jendela; `DocumentView` memasang `CommandBinding` sendiri di `Preview` yang meneruskan ke jalur jendela (`DocumentView.xaml.cs:71-76`),
  jadi Ctrl+P selalu lewat `Print_Executed` (tema Terang, margin, kaki halaman).
- **Cetak langsung (Ctrl+P, `Print_Executed`)** tidak melewati `PreviewBuild`: `PrintDialog` -> `PageLayout.FromPrintableArea` (ukuran media
  terorientasi dari dialog, margin Normal) -> `PrintService.Print`, yang membuat `FlowDocument` baru, membungkusnya dengan
  `HeaderFooterPaginator` (`PageCount` dihitung sinkron), dan memanggil `dialog.PrintDocument`. Seluruhnya sinkron di UI thread
  (`MainWindow.xaml.cs:583-601`, `PrintLayout.cs:150-155`); galat selain OOM ditampilkan lewat `ShowError("Gagal mencetak.")`.
  Cetak langsung tidak menyediakan rentang halaman (belum ada di kode).

### 4.10 Siklus hidup paket XPS dan penanganan galat pratinjau

Kode: `PreviewBuild.Dispose/ScheduleCleanup/Cleanup` (`PreviewBuild.cs:240-329`), `Guard/Fail` (`:211-238`),
`PrintPreviewWindow.StartBuild/ShowFailure/ApplyBuildState` (`PrintPreviewWindow.xaml.cs:103-177`).

Paket XPS (`MemoryStream` -> `Package` -> `XpsDocument`, didaftarkan di `PackageStore` dengan URI `pack://mdviewer-preview-N.xps`,
`PreviewBuild.cs:160-165`) dipakai `DocumentViewer` lewat URI itu. `DocumentViewer` memuat `PageContent` secara async; menutup paket lebih
awal membuat pemuatan yang sudah antre melempar `UriFormatException` di dispatcher (komentar `PreviewBuild.cs:291-292`). Karena itu paket
ditutup hanya setelah dispatcher idle dan hanya setelah penulis XPS benar-benar berhenti:

```mermaid
flowchart TD
    D["Dispose dipanggil: disposed = true, Changed dikosongkan, paginasi latar dimatikan, Pages = null"] --> Q{"writing dan writer ada?"}
    Q -- tidak --> SC["ScheduleCleanup"]
    Q -- ya --> CA["writer.CancelAsync, lalu timer cadangan 10 dtk (DispatcherTimer, prioritas Background) bila cleanup belum dijadwalkan"]
    CA --> WE["WritingCancelled atau WritingCompleted tiba (OnWritingCancelled, OnWritingCompleted)"]
    CA --> TM["Timer cadangan berdetak: Stop lalu ScheduleCleanup"]
    WE --> SC
    TM --> SC
    SC --> IDLE["dispatcher.BeginInvoke prioritas ApplicationIdle (langsung bila dispatcher sedang shutdown)"]
    IDLE --> CU["Cleanup (idempoten): lepas handler writer, xps.Close, PackageStore.RemovePackage, package.Close, stream.Dispose. Tiap langkah dibungkus try sendiri"]
```

`ScheduleCleanup` menghentikan dan menghapus timer cadangan (`:297-298`), jadi timer tidak tertinggal menahan `PreviewBuild`
(dokumen, AST, teks) di memori; test `Dispose_WhileRendering_ArmsAFallbackTimer_*` dan
`Dispose_WhenCleanupIsAlreadyScheduledDuringCancel_*` menjaganya.

Galat: pratinjau tidak mengubah dokumen, jadi galatnya selalu dianggap bisa dipulihkan dan tidak boleh keluar ke dispatcher (jalur galat
fatal aplikasi, 4.8).

```mermaid
flowchart TD
    E["Galat di penangan PreviewBuild: parse, paginasi, kemajuan, penulisan XPS, atau penerima Changed melempar"] --> G["Guard menangkap semua Exception, termasuk OutOfMemoryException"]
    G --> C{"disposed atau Stage sudah Failed?"}
    C -- ya --> L1["CrashLog.Write: Galat susulan pada penyusunan pratinjau cetak"]
    C -- tidak --> F["Fail: CrashLog.Write, Error = ex, Stage = Failed, CancelAsync bila sedang menulis, Changed (galat penerima ikut ditangkap)"]
    F --> A["PrintPreviewWindow.OnBuildChanged lalu ApplyBuildState: panel Pratinjau tidak dapat disusun + Describe(ex)"]
    W["Galat di kode jendela sendiri: StartBuild atau OnBuildChanged"] --> SF["ShowFailure: CrashLog.Write, uiError = ex, panel galat, UpdateNavigation"]
    A --> P["Tombol Cetak dan Ctrl+P nonaktif (CanPrint butuh Ready)"]
    SF --> P
```

`Describe` mengganti `OutOfMemoryException` dengan "Memori tidak cukup untuk menyusun pratinjau dokumen ini." dan memakai `ex.Message`
untuk yang lain (`PrintPreviewWindow.xaml.cs:137-138`). Kegagalan render FlowDocument di jalur cetak datang sebagai
`InvalidOperationException` berpesan ramah tanpa path ([ADR-24](DESIGN-DECISIONS.md#adr-24-dokumen-galat-tidak-pernah-dicetak)).
Pembuatan jendela di `PrintPreview_Executed` ditangkap terpisah (OOM: pesan memori; lainnya: "Gagal membuka pratinjau cetak."),
sedangkan `ShowDialog` sengaja di luar `try` (`MainWindow.xaml.cs:608-630`).

## 5. Model thread

| Pekerjaan | Thread | Bukti |
| --- | --- | --- |
| Semua UI, `DocumentTab`, `DocumentView`, `FindReplaceBar`, `MainWindow` | UI thread (Dispatcher WPF) | `DocumentTab` menangkap `Dispatcher.CurrentDispatcher` saat dibuat (`DocumentTab.cs:26`) |
| Timer: `renderTimer`, `statsTimer` (`DocumentView`), `changeTimer` (`DocumentTab`), `queryTimer`, `refreshTimer` (`FindReplaceBar`) | UI thread (`DispatcherTimer`) | `DocumentView.xaml.cs:32-33`, `DocumentTab.cs:27,69`, `FindReplaceBar.xaml.cs:22-23` |
| Baca/hash/tulis file dokumen, ekspor, `CheckExternalChange` | UI thread, sinkron | tidak ada `Task.Run` di `DocumentTab`/`HtmlExporter`/`MainWindow` |
| Cetak langsung (Ctrl+P): buat dokumen, `ComputePageCount`, `PrintDialog.PrintDocument` | UI thread, sinkron (membekukan UI selama berjalan) | `MainWindow.xaml.cs:583-601`, `PrintLayout.cs:131-155` |
| Parse Markdig + `ResolveImageUrls` untuk pratinjau utama, dokumen 100 rb karakter atau lebih | Thread pool; hasil dilanjutkan di UI thread (`await` tanpa `ConfigureAwait(false)`) | `DocumentView.xaml.cs:27, 401` |
| Parse snapshot Pratinjau Cetak (`PrintSource`), 100 rb karakter atau lebih | Thread pool (`Task.Run`); `PreviewBuild` menunggunya dengan `ConfigureAwait(false)` lalu kembali ke UI thread lewat `dispatcher.BeginInvoke`, tidak bergantung pada `SynchronizationContext`. Di bawah ambang: sinkron di UI thread. Parse yang berjalan tidak dibatalkan saat jendela ditutup (Markdig tak punya titik pembatalan); hasilnya dibuang bersama `PrintSource` | `PrintLayout.cs:92-105`, `PreviewBuild.cs:102-124` |
| Pembuatan `FlowDocument` (`CreateFlowDocument`, `PrintSource.CreateDocument`) | UI thread (wajib) | komentar `DocumentView.xaml.cs:395`; `PrintLayout.cs:118` |
| Paginasi FlowDocument latar dan penulisan XPS async (`PreviewBuild`) | UI thread, dalam potongan lewat dispatcher; bukan thread terpisah. Kabar kemajuan/selesai (`Changed`) dipicu dari callback dispatcher, dan `Dispose()` harus mematikan paginasinya. Detail internal WPF untuk penulis XPS belum diverifikasi | komentar `PreviewBuild.cs:250`; `PreviewBuildTests.cs:168-196` (paginasi berhenti setelah `Dispose`) |
| Pembersihan paket XPS | UI thread, `DispatcherPriority.ApplicationIdle`; timer cadangan 10 dtk (`DispatcherTimer`, prioritas `Background`) | `PreviewBuild.cs:32, 268, 293-301` |
| Event `FileSystemWatcher` | Thread pool, dipindah ke UI lewat `dispatcher.BeginInvoke` | `DocumentTab.cs:383-391` |
| Loop server pipe `SingleInstance` | Thread pool (`Task.Run`, `ConfigureAwait(false)`); callback `onFiles` di thread latar, lalu `Dispatcher.BeginInvoke` | `SingleInstance.cs:90-151`, `App.xaml.cs:57-58` |
| `SystemEvents.UserPreferenceChanged` (tema Ikuti Sistem) | Dipindah ke UI lewat `BeginInvoke` | `Theming.cs:103-110` |
| `CrashLog.Write` | Thread mana pun; diserialisasi `lock (Gate)` | `CrashLog.cs:17,34` |

Konsekuensi: operasi I/O besar (muat file ratusan MB, ekspor dengan banyak gambar, hash saat stempel "racy") membekukan UI selama
berjalan; batas ukuran dan anggaran ekspor ada untuk membatasi ini. Pencetakan (Cetak langsung dan tombol Cetak di pratinjau) juga
sinkron; menyusun Pratinjau Cetak tidak (async, indikator "Menyusun halaman..."). Pembuatan `FlowDocument` dokumen sangat besar
tetap di UI thread dan dapat membekukan UI: sudah diukur untuk pratinjau utama (README, Batasan yang diketahui), belum diukur untuk
Pratinjau Cetak. Regex cari juga sinkron di UI thread, dibatasi 2 dtk per
kecocokan dan 4 dtk total (`SearchEngine.cs:24-25`). Keamanan-thread `MarkdownSupport.Pipeline` (statis, dipakai thread latar dan
UI) belum diverifikasi.

## 6. Model state

### 6.1 `DocumentTab`

| State | Arti | Diubah oleh |
| --- | --- | --- |
| `IsDirty` | Turunan `!Document.UndoStack.IsOriginalFile`. Bukan flag sendiri: Undo sampai versi asli membuat tab bersih lagi. | Edit, Undo, `MarkAsOriginalFile` di `SaveCore` dan `ApplyDiskContent` |
| Undo | Reload (otomatis maupun pilihan pengguna) = `StartUndoGroup` + `Replace` + `EndUndoGroup` + `MarkAsOriginalFile`: tab bersih, tetapi Undo masih mengembalikan teks editor lama (lalu tab kotor) | `ApplyDiskContent` (`DocumentTab.cs:325-345`) |
| `diskHash`, `diskStamp` | Isi/stempel file yang terakhir diketahui sama dengan di disk | Hanya di `Load`, `SaveCore`, `ApplyDiskContent`, `KeepEditorVersion`, dan penyegaran stempel di `CheckExternalChange`/`IsChangedOnDisk` saat hash tidak berubah. `null` untuk tab tanpa judul: tidak ada pemeriksaan eksternal dan tidak ada konflik simpan |
| `pendingHash`, `pendingStamp` | Perubahan eksternal yang sudah dilaporkan (`ExternalChangeConflict`) tetapi belum dijawab; `HasExternalConflict = pendingHash is not null`. `diskHash` sengaja tidak diubah sampai ada jawaban, supaya simpan berikutnya tetap mendeteksi konflik | `CheckExternalChange` mengisi; `SaveCore`, `ApplyDiskContent`, `KeepEditorVersion`, atau hash kembali sama menghapus |
| `saving` | `true` selama `SaveTo` (termasuk dialog konflik simpan yang memompa pesan): `CheckExternalChange` ditunda supaya tidak ada dialog konflik kedua | `SaveTo` (`try/finally`) |
| `IsLossyDecoded` | File ber-BOM berisi byte tak valid yang diganti U+FFFD; simpan meminta konfirmasi | `Load`, `ApplyDiskContent` mengisi; `SaveCore` menghapus |
| `Encoding` | Encoding yang dipakai saat menyimpan; bisa naik ke UTF-8 saat simpan | `Load`, `SaveCore`, `ApplyDiskContent` |
| `disposed`, `changeRetries` | Penjaga siklus hidup; batas percobaan baca file terkunci (5, termasuk baca awal) | `Dispose`; `OnDiskEvent` mereset hitungan |

```mermaid
stateDiagram-v2
    [*] --> Bersih: Load atau SaveTo sukses
    Bersih --> Bersih: perubahan eksternal lalu ApplyDiskContent
    Bersih --> Kotor: edit
    Kotor --> Bersih: SaveTo sukses atau Undo sampai versi asli
    Kotor --> KonflikTertunda: hash baru dari disk, pendingHash terisi
    KonflikTertunda --> Bersih: Reload atau SaveTo Timpa
    KonflikTertunda --> Kotor: KeepEditorVersion, diskHash menjadi pendingHash
    KonflikTertunda --> KonflikTertunda: perubahan eksternal lain atau simpan dibatalkan
```

### 6.2 `MainWindow`

| State | Arti |
| --- | --- |
| `conflictQueue` + `conflictPromptOpen` | Satu dialog konflik pada satu waktu. Dialog bersifat modal tetapi memompa pesan, sehingga konflik tab lain datang saat dialog terbuka; itu hanya masuk antrean dan diproses setelah dialog tertutup (`MainWindow.xaml.cs:316-342`). `OnSaveConflict` memakai penanda yang sama dan memulihkan nilai lamanya (`:371-392`). |
| `preserveStoredSession` | `true` bila instance dibuka dengan argumen file: saat keluar `Session` di `settings.json` tidak ditimpa. Menjadi `false` begitu `OpenUserFile` benar-benar mengembalikan tab (kiriman instance lain, dialog Buka, seret-lepas, Berkas Terakhir). Pemulihan sesi dan argumen startup memakai `OpenFile` langsung sehingga tidak "mengadopsi". `forceSession` (galat fatal) mengabaikannya. |
| `closing`, `closed` | `closing`: penutupan sedang berjalan; direset bila pengguna membatalkan. Efeknya hanya dua: `ProcessConflictQueue` di `finally` milik `TrySave` dilewati (`MainWindow.xaml.cs:446`) dan kiriman file dari instance lain diabaikan (`:65`, bersama `closed`). `closed`: setelah `Window_Closed`. |
| `zoomPercent`, tema | Disalin ke `AppSettings` saat `SaveSettings`. |

### 6.3 `DocumentView`

`previewStale`, `lastRenderedText` (tidak disimpan bila 1 juta karakter atau lebih), `lastRenderedVersion`, `renderGeneration`,
`renderInFlight`, `pendingAnchor` (anchor yang menunggu render selesai), `expectedEditorOffset`/`expectedPreviewOffset`/
`scrollSyncSuspended` (mencegah umpan balik sinkron scroll), `disposed`. Flag statis `BlockRemoteImages` berlaku untuk semua tab
dan diubah dari `MainWindow` (`LoadRemoteImages_Click`) diikuti `RefreshPreview` pada tiap tab.

### 6.4 `AppSettings`

`removedRecent` (berkas yang sengaja dihapus instance ini dari daftar terakhir, supaya tidak "hidup lagi" saat digabung dengan
isi file) bersifat per-instance dan tidak disimpan. Data yang disimpan: `Theme` dan `SessionTab.Mode` sebagai teks agar nilai
tak dikenal tidak merusak seluruh file (`Sanitize` + `ParsedMode` jatuh ke default).

### 6.5 `PreviewBuild` dan `PrintPreviewWindow`

`PreviewBuild.Stage` (`PreviewStage`) hanya bergerak maju; siklus yang sudah `Ready`/`Failed` tidak dipakai ulang (mengganti layout membuat
siklus baru, `PrintPreviewWindow.StartBuild`).

```mermaid
stateDiagram-v2
    [*] --> Paginating: konstruktor, Start memulai pembuatan dokumen
    Paginating --> Rendering: paginasi selesai (BeginRender)
    Paginating --> Failed: parse gagal atau galat tertangkap Guard
    Rendering --> Ready: WritingCompleted tanpa galat, Pages terisi
    Rendering --> Failed: e.Error, galat Guard, atau penerima Changed melempar
    Ready --> Failed: penerima Changed melempar saat tahap Ready
```

`Dispose()` tidak mengubah `Stage`: sesudahnya tidak ada lagi peristiwa `Changed`, `Pages` menjadi `null`, dan `Start()` tidak berbuat apa-apa
(`PreviewBuildTests.Dispose_*`, `Dispose_BeforeStart_ThenStart_DoesNothing`). Siklus yang dibuang saat `Paginating`/`Rendering` tetap
berstatus itu.

| State | Arti | Diubah oleh |
| --- | --- | --- |
| `Stage`, `PageCount`, `RenderedPages` | Tahap; selama `Paginating` jumlah halaman sejauh ini, sesudahnya jumlah akhir; halaman XPS yang sudah ditulis | `BeginPagination`, `OnPaginationProgress`, `BeginRender`, `OnWritingProgress`, `OnWritingCompleted`, `Fail` |
| `Pages`, `Error` | `FixedDocumentSequence` (hanya `Ready`); galat penyebab `Failed` | `OnWritingCompleted`, `Fail`; `Dispose` mengosongkan `Pages` |
| `started`, `disposed`, `writing` | `Start` hanya sekali; setelah `disposed` semua penangan keluar tanpa kerja; `writing` benar selama `WriteAsync` belum melapor selesai/batal | `Start`, `Dispose`, `BeginRender`, `OnWritingCompleted/Cancelled` |
| `cleanupScheduled`, `cleanedUp`, `cleanupFallback` | Pembersihan paket dijadwalkan sekali, dijalankan sekali; timer cadangan 10 dtk hanya ada selama penulis dibatalkan tetapi belum melapor | `Dispose`, `ScheduleCleanup`, `Cleanup` |

Jendela (`PrintPreviewWindow`): `paper`/`orientation`/`margin` (pilihan segmented control; bawaan A4, potret, Normal, tidak disimpan ke
`settings.json` dan tidak dibawa ke pembukaan berikutnya), `zoomMode` (`FitPage`, `FitWidth`, `Actual`, `Custom`), `printing` (selama
`PrintDocument`), `uiError` (galat di kode jendela, mematikan Cetak), `goToTarget`/`goToOffset`/`goToZoom` (tujuan navigasi Berikutnya/Sebelumnya/
kotak halaman yang berlaku sampai gulir atau zoom berubah; `CurrentPage`, `PrintPreviewWindow.xaml.cs:232-241`), `lastCanPrint` (agar
`CommandManager.InvalidateRequerySuggested` tidak dipanggil pada tiap kabar kemajuan).
