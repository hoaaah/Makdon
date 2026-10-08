# Catatan Keputusan Desain (ADR ringkas)

**Tujuan:** mencatat keputusan desain yang tampak di kode Makdon beserta konteks, keputusan, dan konsekuensinya, supaya
perubahan di masa depan tidak melanggar alasan aslinya tanpa sadar.
**Pembaca:** pengembang dan reviewer.

**Aturan dokumen ini:** alasan hanya diambil dari komentar kode, nama test, README, dan CLAUDE.md. Bila motivasi tidak tertulis di
mana pun, ditulis "tidak tercatat" atau "dugaan, belum diverifikasi". Riwayat git pendek (`git log`), jadi sejarah keputusan tidak banyak bisa ditelusuri dari sana (`b2a35be` menambah dokumentasi dan
menutup celah tautan UNC; `6d1d539` menambah Pratinjau Cetak, ADR-19 sampai ADR-26; ADR-27 sampai ADR-33 adalah fitur distribusi yang belum di-commit saat ditulis). Arsitektur: [ARCHITECTURE.md](ARCHITECTURE.md). Keamanan: [SECURITY.md](SECURITY.md).

Format tiap catatan: **Konteks** - **Keputusan** - **Konsekuensi** - **Bukti** (kode `path:baris`, test).
Status semua catatan: berlaku (tercermin di kode saat ini).

| # | Keputusan |
| --- | --- |
| [01](#adr-01-simpan-atomik-dengan-fallback-writeinplace) | Simpan atomik + fallback `WriteInPlace` |
| [02](#adr-02-deteksi-encoding-dan-penanda-lossy) | Deteksi encoding dan penanda lossy |
| [03](#adr-03-hash-isi--filestamp-untuk-perubahan-eksternal) | Hash isi + `FileStamp` untuk perubahan eksternal |
| [04](#adr-04-muat-ulang-adalah-satu-langkah-undo) | Muat ulang = satu langkah Undo |
| [05](#adr-05-dialog-konflik-berlabel-dan-diserialisasi) | Dialog konflik berlabel dan diserialisasi |
| [06](#adr-06-pipeline-ekspor-terpisah-allowlist-url-dan-anggaran-data-uri) | Pipeline ekspor terpisah, allowlist URL, anggaran data URI |
| [07](#adr-07-pemblokiran-gambar-remoteunc-di-pratinjau-dan-cetak) | Pemblokiran gambar remote/UNC di pratinjau |
| [08](#adr-08-single-instance-dengan-mutex-dan-named-pipe-per-sesi) | Single-instance Mutex + pipe per sesi (+ scope portable, ADR-29) |
| [09](#adr-09-aturan-sesi-preservestoredsession) | Aturan sesi `preserveStoredSession` |
| [10](#adr-10-normalizelineendings-dan-batas-waktu-regex) | `NormalizeLineEndings` + batas waktu regex |
| [11](#adr-11-crashlog-dan-isrecoverable) | `CrashLog` dan `IsRecoverable` |
| [12](#adr-12-tema-lewat-resourcedictionary) | Tema lewat `ResourceDictionary` |
| [13](#adr-13-nama-appthememode) | Nama `AppThemeMode` |
| [14](#adr-14-render-pratinjau-debounce-parse-latar-dan-generation-check) | Render pratinjau: debounce, parse latar, generation check |
| [15](#adr-15-pengaturan-tidak-pernah-melempar-dan-digabung-saat-simpan) | Pengaturan tidak pernah melempar, digabung saat simpan |
| [16](#adr-16-pembukaan-dokumen-hanya-lewat-openfile-dengan-batas-ukuran) | Pembukaan dokumen hanya lewat `OpenFile`, batas ukuran |
| [17](#adr-17-klik-tautan-di-pratinjau) | Klik tautan di pratinjau |
| [18](#adr-18-skrip-asosiasi-file-hanya-hkcu) | Skrip asosiasi file hanya HKCU (alat pengembangan; aplikasi memakai ADR-30) |
| [19](#adr-19-pratinjau-cetak-lewat-paket-xps-di-memori) | Pratinjau Cetak lewat paket XPS di memori |
| [20](#adr-20-snapshot-saat-pratinjau-dibuka-dan-parse-yang-dipakai-bersama) | Snapshot saat pratinjau dibuka dan parse yang dipakai bersama |
| [21](#adr-21-kaki-halaman-di-dalam-margin-bawah) | Kaki halaman di dalam margin bawah |
| [22](#adr-22-pratinjau-punya-pengaturan-kertas-sendiri-dan-konfirmasi-bila-dialog-cetak-berbeda) | Pratinjau punya pengaturan kertas sendiri + konfirmasi bila dialog Cetak berbeda |
| [23](#adr-23-dokumen-cetak-selalu-bertema-terang) | Dokumen cetak selalu bertema Terang |
| [24](#adr-24-dokumen-galat-tidak-pernah-dicetak) | Dokumen galat tidak pernah dicetak |
| [25](#adr-25-galat-pratinjau-dibungkus-guard-dan-paket-xps-dibersihkan-setelah-idle) | Galat pratinjau dibungkus `Guard`, paket XPS dibersihkan setelah idle |
| [26](#adr-26-seam-khusus-test-pada-kode-cetak) | Seam khusus test pada kode cetak |
| [27](#adr-27-folder-self-contained-bukan-single-file-dan-installer-per-pengguna) | Folder self-contained (bukan single-file); installer per pengguna |
| [28](#adr-28-mode-portable-lewat-penanda-makdonportable-data-di-data) | Mode portable lewat penanda `Makdon.portable`; data di `data\` |
| [29](#adr-29-scope-single-instance-per-folder-exe-untuk-portable) | Scope single-instance per folder exe untuk portable |
| [30](#adr-30-pendaftaran-buka-dengan-portable-di-hkcu-lewat-fileassociation-dan-iregistrystore-tolak-bila-terpasang) | "Buka dengan" portable lewat `FileAssociation` + `IRegistryStore`; tolak bila terpasang |
| [31](#adr-31-mutex-installer-bernama-tetap-makdonappmutex-bukan-restart-manager) | Mutex installer bernama tetap (`Makdon.AppMutex`), bukan Restart Manager |
| [32](#adr-32-net-10-dan-switch-xps-di-runtimeconfig) | .NET 10 dan switch XPS di runtimeconfig |
| [33](#adr-33-wpfhost-memakai-testapp-tanpa-onstartup) | `WpfHost` memakai `TestApp` tanpa `OnStartup` |

---

## ADR-01 Simpan atomik dengan fallback `WriteInPlace`

- **Konteks.** File dokumen tidak boleh rusak setengah jalan bila penulisan gagal (README: "Penyimpanan atomik (file sementara
  lalu ganti)"). Di sisi lain ada folder yang tidak bisa dibuatkan file baru padahal file-nya bisa ditulis, dan nama file panjang
  bisa membuat nama sementara melewati batas panjang path (komentar `TextFileIO.cs:145, 155-157`).
- **Keputusan.** `TextFileIO.WriteBytesAtomic` menulis ke `~md` + 8 heks + `.tmp` di folder yang sama (`FileMode.CreateNew`,
  `Flush(true)`), lalu `File.Replace` (atau `File.Move(overwrite)` bila `ReplaceFile` tidak didukung, mis. share jaringan
  tertentu). Bila gagal, file sementara dihapus. Hanya `UnauthorizedAccessException` dan `PathTooLongException` saat membuat file
  sementara yang jatuh ke `WriteInPlace`; galat I/O lain (disk penuh, pelanggaran berbagi, folder hilang) tidak dicoba lagi lewat
  jalur non-atomik "yang lebih berisiko" (komentar kode) dan diteruskan ke pemanggil. `WriteInPlace` menulis dari awal lalu `SetLength` (tidak mengosongkan
  dulu) agar kegagalan di tengah tidak menghapus seluruh isi lama. Bila path adalah symlink, penulisan menuju file sebenarnya
  (`ResolveLinkTarget`) sehingga link tidak diganti file biasa. Hash yang dikembalikan adalah hash byte yang benar-benar ditulis.
- **Konsekuensi.** File `~md########.tmp` muncul sebentar di folder dokumen. Jalur `WriteInPlace` tidak atomik (README, bagian
  Batasan). Penulisan dokumen, ekspor HTML, dan `settings.json` memakai jalur yang sama: `TextFileIO.Write`. Pengecualian: `crash.log`
  ditulis dengan `File.AppendAllText` (`CrashLog.cs:40`), bukan atomik.
- **Bukti.** `src/Makdon/TextFileIO.cs:134-202`, `HtmlExporter.cs:66`, `AppSettings.cs:85`. Test:
  `TextFileIOCoverageTests.WriteBytesAtomic_*` (`IoAndUtilityCoverageTests.cs:97-196`, termasuk fallback lewat ACL deny
  CreateFiles), `TextFileIOHardeningTests.Write_VeryLongFileName_StillSavesAtomically` (`HardeningTests.cs:669`),
  `TextFileIOFileTests.Write_*` (`TextFileIOTests.cs:295-445`). Penulisan lewat symlink hanya teruji bila mesin boleh membuat symlink
  (test keluar diam-diam bila tidak, `TextFileIOTests.cs:446-461`).

## ADR-02 Deteksi encoding dan penanda lossy

- **Konteks.** README: encoding dipertahankan saat menyimpan (UTF-8 dengan/tanpa BOM, UTF-16, UTF-32, Windows-1252) dan file
  dengan byte tidak valid "ditandai dan meminta konfirmasi sebelum disimpan". Test `DecodeThenEncode_IsByteIdentical` menjadikan
  "buka lalu simpan tanpa edit = byte identik" sebagai sifat yang dijaga.
- **Keputusan.** Urutan deteksi: BOM (UTF-32 LE dicek sebelum UTF-16 LE) -> UTF-8 tanpa BOM yang valid -> Windows-1252. Bila
  dekode 1252 tidak kembali ke byte yang sama (byte tak terdefinisi di 1252), jatuh ke Latin-1 yang memetakan 256 byte satu-satu.
  File ber-BOM dengan isi tidak valid didekode ketat dulu, lalu longgar (U+FFFD) dan `lossy = true` (`DocumentTab.IsLossyDecoded`);
  `MainWindow.TrySave` menanyakan konfirmasi. File tanpa BOM yang bukan UTF-8 valid jatuh ke 1252 dan **tidak** dianggap lossy.
  `Encode` memakai fallback pengecualian: bila teks tak muat di encoding aslinya (mis. karakter di luar 1252), simpan sebagai UTF-8
  tanpa BOM daripada mengganti karakter dengan `?`; surrogate yatim memakai penggantian bawaan karena tak ada encoding yang bisa
  memuatnya.
- **Konsekuensi.** File 1252 bisa berganti menjadi UTF-8 saat disimpan (label status bar diperbarui lewat `EncodingLabel`).
  Simpan setelah dekode lossy menetapkan penggantian U+FFFD secara permanen (karena itu ada konfirmasi); `SaveCore` menghapus flag.
  Deteksi 1252-vs-lain berbasis heuristik "bukan UTF-8 valid", jadi file encoding lain tanpa BOM (mis. Shift-JIS) dibaca sebagai 1252
  (belum diuji; inferensi dari urutan deteksi).
- **Bukti.** `TextFileIO.cs:40-127`, `DocumentTab.cs:81, 183`, `MainWindow.xaml.cs:431-434`. Test: `TextFileIODecodeTests`
  (`TextFileIOTests.cs:6-162`), `Decode_EachBomWithInvalidBody_IsLossy_*` (`IoAndUtilityCoverageTests.cs:43`), `Lossy_*`
  (`DocumentTabConflictEdgeTests.cs:482-578`).

## ADR-03 Hash isi + `FileStamp` untuk perubahan eksternal

- **Konteks.** Komentar `DocumentTab.cs:33-36`: perubahan disk dianggap nyata hanya bila hash-nya berbeda, "sehingga simpan oleh
  aplikasi sendiri atau sekadar touch tidak memicu prompt palsu"; stempel (ukuran + waktu tulis) mendampingi agar pemeriksaan
  berulang (tiap jendela aktif, file besar) tidak membaca file bila jelas tidak berubah. Komentar `FileStamp.cs:3-7`: dua tulis
  berurutan bisa berbagi timestamp dan ukuran yang sama, jadi stempel yang diambil < 2 dtk setelah tulis tidak andal.
- **Keputusan.** `diskHash` (SHA-256 hex) adalah dasar kebenaran. `FileStamp.IsReliable` (`TakenUtc - LastWriteUtc > 2 dtk`)
  menjadi jalan pintas: stempel andal dan sama dengan yang diketahui (`diskStamp` atau `pendingStamp`) = lewati baca. Selain itu
  baca byte dan bandingkan hash. Perubahan yang sudah dilaporkan disimpan sebagai `pendingHash`/`pendingStamp` dan `diskHash` tidak
  diubah sampai pengguna menjawab, sehingga: (a) perubahan yang sama tidak ditanyakan dua kali, (b) menyimpan sebelum menjawab tetap
  mendeteksi konflik. Stempel diambil **sebelum** membaca (`DocumentTab.Load`, `Reload`) agar perubahan di antaranya tetap
  terdeteksi. Watcher didebounce 400 ms dan percobaan baca maksimal 5 kali (1 baca awal + 4 ulangan; `++changeRetries < MaxChangeRetries`) bila file terkunci penulisnya; saat jendela aktif
  kembali, tab aktif diperiksa sebagai cadangan (share jaringan tanpa dukungan watcher).
- **Konsekuensi.** File yang baru ditulis (< 2 dtk) dibaca penuh tiap pemeriksaan. Semua pembacaan sinkron di UI thread. Pemeriksaan
  saat aktivasi hanya untuk tab aktif (`MainWindow.xaml.cs:55`); tab lain bergantung pada watcher.
- **Bukti.** `DocumentTab.cs:37-43, 227-277, 300-321`, `FileStamp.cs`. Test: `FileStamp_IsReliable_*`
  (`IoAndUtilityCoverageTests.cs:265`), `CheckExternalChange_SameSizeAndTimestampButDifferentContent_IsStillDetected`
  (`DocumentTabConflictEdgeTests.cs:398`), `*_TouchWithIdenticalContent_IsIgnored`, `ExternalConflict_*`
  (`DocumentTabConflictEdgeTests.cs:331-393`). Jalur watcher + timer 400 ms **tidak** diuji end-to-end: test memanggil
  `CheckExternalChange()` langsung (komentar `DocumentTabTests.cs:8-11`).

## ADR-04 Muat ulang adalah satu langkah Undo

- **Konteks.** Pengguna bisa salah memilih "Muat dari Disk" di dialog konflik; edit yang terbuang tidak boleh hilang permanen
  (komentar `DocumentTab.cs:203-205, 323-324`). README: "Muat dari Disk adalah satu langkah Undo".
- **Keputusan.** `ApplyDiskContent` mengganti seluruh teks di dalam satu `UndoStack.StartUndoGroup/EndUndoGroup`, lalu
  `MarkAsOriginalFile`. Berlaku sama untuk muat ulang otomatis tab bersih, pilihan "Muat dari Disk" di konflik eksternal, dan di
  konflik simpan. File dibaca ulang **setelah** dialog ditutup (`Reload()` memanggil `ReadBytes` saat itu), bukan memakai byte saat
  konflik terdeteksi, karena dialog bisa terbuka lama.
- **Konsekuensi.** Setelah muat ulang tab bersih tetapi `Undo` tersedia; Undo membuat tab kotor lagi. Caret dipertahankan dan
  diklem ke panjang teks baru.
- **Bukti.** `DocumentTab.cs:206-211, 325-345`. Test: `Reload_DiscardsEditorChangesAndTakesDiskContent`
  (`DocumentTabTests.cs:846`), `CheckExternalChange_CleanTab_AutoReloadIsOneUndoStep` (`:575`),
  `SaveTo_Conflict_Reload_IsOneUndoStep_*` dan `*_ReadsTheFileAgainAfterTheDialog_*` (`DocumentTabConflictEdgeTests.cs:167, 211`).

## ADR-05 Dialog konflik berlabel dan diserialisasi

- **Konteks.** `ChoiceDialog` dibuat sebagai "pengganti MessageBox Ya/Tidak/Batal yang ambigu" (komentar
  `ChoiceDialog.xaml.cs:10-13`). Dialog modal WPF tetap memompa pesan, jadi event lain (watcher, aktivasi) bisa memicu konflik tab
  lain atau konflik kedua untuk file yang sama saat dialog terbuka (komentar `MainWindow.xaml.cs:324-325`, `DocumentTab.cs:48-49`).
- **Keputusan.** Semua dialog konflik memakai `ChoiceDialog` dengan label yang menjelaskan akibat ("Muat dari Disk", "Pertahankan
  Editor", "Timpa", "Batal"); pilihan paling aman jadi bawaan (Enter) dan Esc/X = pilihan batal. Penanda `conflictPromptOpen` +
  `conflictQueue` menyerialisasi dialog; `OnSaveConflict` memakai penanda yang sama. `DocumentTab.SaveTo` menyetel `saving = true`
  sehingga `CheckExternalChange` tab itu ditunda selama dialog konflik simpan terbuka. Konflik yang datang selama `SaveTo`
  ditanyakan setelah `SaveTo` selesai (`TrySave` -> `ProcessConflictQueue`, kecuali sedang `closing`).
- **Konsekuensi.** Tidak boleh menampilkan `MessageBox`/dialog konflik langsung dari event (aturan CLAUDE.md). Konflik tidak pernah
  bertumpuk atau hilang diam-diam.
- **Bukti.** `MainWindow.xaml.cs:318-394, 429-450`, `ChoiceDialog.xaml.cs`. Test: `ChoiceDialogTests` (`ChoiceDialogTests.cs:46-66`),
  `SaveTo_Conflict_SuppressesExternalChangeChecksWhileTheDialogIsOpen` (`DocumentTabConflictEdgeTests.cs:237`). Logika antrean di
  `MainWindow` **tidak** diuji (tidak ada test yang membuat `MainWindow`).

## ADR-06 Pipeline ekspor terpisah, allowlist URL, dan anggaran data URI

- **Konteks.** Ekspor menghasilkan file yang dibagikan dan dibuka di peramban pihak lain, sedangkan pratinjau dirender WPF
  (`FlowDocument`), bukan HTML. README/CLAUDE.md: "Jangan menyatukan keduanya".
- **Keputusan.**
  - `MarkdownSupport.Pipeline` (pratinjau) dan `ExportPipeline` dipisah. `ExportPipeline` = ekstensi yang sama +
    `DisableHtml()` (HTML mentah di-escape) dan menghapus `GenericAttributesExtension` (`{onclick=...}`) dan `MediaLinkExtension`
    (gambar menjadi `<iframe>`). Pratinjau tetap mengurai HTML mentah (renderer WPF mengabaikannya, README).
  - URL disaring `ClassifyUrl` (allowlist, bukan blocklist): `Relative`, `Http`, `Mailto`, `DataImage`
    (`png|jpeg|gif|webp`), `LocalFile`, selain itu `Blocked` (`javascript:`, `vbscript:`, `data:` lain, `file://host`, UNC, `//host`,
    skema tak dikenal). Spasi/karakter kontrol dibuang sebelum deteksi skema (browser membuang tab/baris baru di `java\tscript:`).
    Tautan tak aman menjadi `href="#"`; autolink tak aman menjadi teks; gambar tak aman menjadi teks penanda dengan alt di-escape.
  - Gambar lokal disematkan sebagai data URI hanya bila: tipe png/jpg/jpeg/gif/webp (berdasarkan ekstensi), <= 2 MB per gambar
    (`MaxEmbeddedImageBytes`), dan berada **di bawah folder dokumen**; yang di luar (absolut ke folder lain, `../`, symlink yang
    menunjuk keluar) diganti teks `[gambar di luar folder dokumen tidak disertakan]` (privasi: file lain di mesin tidak ikut
    terekspor). Dokumen tanpa folder tidak menyematkan gambar lokal apa pun. Anggaran total `MaxTotalEmbeddedBytes` = 30 MB, dihitung
    per kemunculan (tiap kemunculan menambah ukuran HTML); setelah habis path relatif dibiarkan relatif. Gambar yang sama dibaca dan
    dikodekan sekali (cache per path lengkap).
  - Bila tidak disematkan, path relatif tetap relatif (tidak pernah menjadi `file:///C:/...`); path absolut lokal diganti teks
    sehingga path mesin tidak bocor. Judul di-`HtmlEncode`; template diisi satu pass sehingga `{{BODY}}`/`{{TITLE}}` di isi atau
    judul tetap literal.
- **Konsekuensi.** HTML hasil ekspor bisa sampai ~30 MB data gambar. SVG/BMP dan gambar > 2 MB tidak tersemat. Gambar `http(s)`
  tetap dirujuk apa adanya di HTML (penerima yang membukanya bisa dilacak). Tipe gambar ditentukan dari ekstensi, bukan isi.
  Ekspor berjalan sinkron di UI thread; `OutOfMemoryException` dan galat lain ditangkap di `ExportHtml_Executed`.
- **Bukti.** `MarkdownSupport.cs:58-78, 86-115, 213-358`, `HtmlExporter.cs`, `MainWindow.xaml.cs:727-742`. Test:
  `ExportSanitizationTests` (`HardeningTests.cs:10-185`), `ExportXssVectorTests` (`ExportAndImageSecurityTests.cs:11-327`),
  `ExportImageBudgetAndPrivacyTests` (seluruh berkas), `HtmlExporterTests` (`HtmlAndMarkdownSupportTests.cs:212-382`). Cabang
  symlink-menunjuk-keluar (`MarkdownSupport.cs:333-334`) tidak punya test.

## ADR-07 Pemblokiran gambar remote/UNC di pratinjau dan cetak

- **Konteks.** README: gambar remote diganti penanda secara bawaan "agar dokumen tidak bisa melacak Anda"; UNC dan skema lain
  (`ftp:` dll.) selalu diblokir tanpa koneksi jaringan; `data:` tidak ditampilkan karena WPF tidak mendukungnya. Komentar
  `DocumentViewLifecycleTests.cs:465-466`: sebelum perbaikan, `BitmapImage(Uri)` melempar `NotSupportedException` untuk `data:` dan
  seluruh pratinjau berganti dokumen galat.
- **Keputusan.** `ResolveImageUrls` (dijalankan pada dokumen hasil parse, juga di thread latar) memutuskan per `UrlKind`:
  `DataImage` -> teks `[gambar data: tidak ditampilkan di pratinjau]`; `Blocked`/`Mailto` (UNC, `file://host`, `//host`, `ftp:`,
  dst.) -> `[gambar remote diblokir]` selalu; `Http` -> diblokir bila `BlockRemoteImages` (bawaan `true`, disimpan di
  `AppSettings`, menu Tampilan > "Muat gambar remote"). Setelah klasifikasi hanya `file:` tanpa host yang diteruskan ke WPF; path
  UNC hasil gabungan hanya boleh bila dokumen sendiri ada di share yang sama (`IsAllowedLocalPath`). Cetak dan Pratinjau Cetak memakai
  `ParseDocument` yang sama (lewat `DocumentView.ParsePrintSnapshot`, [ADR-20](#adr-20-snapshot-saat-pratinjau-dibuka-dan-parse-yang-dipakai-bersama)). Jaring pengaman: `ReplaceUnloadableImages` mengganti gambar lokal yang ada tetapi tak bisa di-decode dengan teks, supaya satu
  gambar rusak tidak menggagalkan seluruh pratinjau.
- **Konsekuensi.** `BlockRemoteImages` adalah flag statis global (`DocumentView.BlockRemoteImages`), bukan per tab; mengubahnya
  memanggil `RefreshPreview` di semua tab. Gambar `data:` sah tampil di ekspor tetapi tidak di pratinjau. Pratinjau tidak
  membatasi gambar lokal ke folder dokumen (hanya ekspor yang membatasi).
- **Bukti.** `MarkdownSupport.cs:122-200`, `DocumentView.xaml.cs:56, 424-527`, `MainWindow.xaml.cs:125-131`. Test:
  `ResolveImageUrlsSecurityTests` (`ExportAndImageSecurityTests.cs:330-491`), `RemoteImageBlockingTests` (`HardeningTests.cs:188-280`),
  `RemoteImageOverFtp_IsNotFetched_WhenRemoteImagesAreBlocked` (membuka `TcpListener` lokal dan memastikan WPF tidak terhubung,
  `DocumentViewLifecycleTests.cs:510`), `UndecodableImage_*` (`:414-459`), `DataImage_ShowsMarker_*` (`:468`).

## ADR-08 Single-instance dengan Mutex dan named pipe per sesi

- **Konteks.** Membuka `.md` saat Makdon berjalan harus membuka tab di jendela yang ada, tetapi sesi Windows lain (mis. Remote
  Desktop) punya instance sendiri (README). Komentar `SingleInstance.cs:10-14`: `Mutex` `Local\` sudah per sesi, sedangkan nama pipe
  global se-mesin sehingga id sesi dimasukkan ke nama pipe agar cakupannya sama.
- **Keputusan.** Nama mutex `Local\Makdon.SingleInstance.<SID>[.<scope>]`; nama pipe `Makdon.<SID>.s<sessionId>[.<scope>]`; pipe
  dibuka `PipeOptions.CurrentUserOnly` di kedua sisi, satu instance server. Protokol teks UTF-8: baris pertama harus persis
  `MAKDON1`, lalu satu path per baris. Penerima menerima hanya path **fully-qualified** (`Path.IsPathFullyQualified`; `C:rel.md` dan
  `\rel.md` ditolak karena bergantung folder/drive kerja), panjang < 32768, maksimal 64 path, pesan maksimal 256 K karakter, batas
  baca 5 dtk per klien. `BuildMessage` membuang entri yang mengandung `\n`/`\r` (injeksi path). Klien yang macet atau mengirim sampah
  tidak menambah hitungan kegagalan; hanya kegagalan membuat/menunggu pipe yang dihitung (menyerah setelah 5). Seluruh kelas "tidak pernah
  melempar": bila mutex/pipe tak bisa dipakai, pemanggil membuka instance baru. `scope` memungkinkan test memakai nama unik. `App`
  mengubah argumen relatif menjadi mutlak sebelum dikirim.
- **Konsekuensi.** File yang dikirim ke instance yang sedang menutup diabaikan; peluncuran kedua tanpa argumen hanya mengaktifkan
  jendela yang ada (README). Server hanya berhenti sendiri setelah gagal berulang (dicatat ke `CrashLog`); sesudahnya peluncuran
  berikutnya dikirim ke pipe yang tidak melayani lalu gagal dan membuka instance sendiri (inferensi dari alur `App.OnStartup`, tidak diuji).
- **Bukti.** `SingleInstance.cs`, `App.xaml.cs:19-47`. Test: `SingleInstanceServerTests` (24 test, scope unik per test) dan
  `SingleInstanceTests` (`HardeningTests.cs:795-870`). Properti `CurrentUserOnly` sendiri tidak punya test yang membuktikan pengguna
  lain ditolak (butuh akun kedua).

## ADR-09 Aturan sesi `preserveStoredSession`

- **Konteks.** Instance yang dibuka lewat argumen (klik dua kali `.md`) hanya berisi file itu; menyimpan sesinya saat keluar akan
  menimpa ruang kerja multi-tab yang tersimpan (README, bagian Lokasi data; CLAUDE.md).
- **Keputusan.** `preserveStoredSession = files.Count > 0` di konstruktor `MainWindow`. Selama `true`, `SaveSettings` memanggil
  `SaveMerged(keepStoredSession: true)` sehingga `Session` di file tidak disentuh (daftar berkas terakhir, tema, dan zoom tetap
  disimpan). Setiap pembukaan atas kehendak pengguna lewat `OpenUserFile` (kiriman instance lain, dialog Buka, seret-lepas, Berkas
  Terakhir, hanya bila tab benar-benar terbuka) mengubahnya menjadi `false` ("diadopsi"). Argumen startup dan pemulihan sesi memakai
  `OpenFile` langsung sehingga tidak mengadopsi. Galat fatal memaksa simpan sesi (`forceSession`) karena sesi dan daftar tab terbuka
  harus bisa dipulihkan.
- **Konsekuensi.** Ada dua jalur pembukaan yang harus dibedakan dengan sengaja saat menambah fitur pembukaan file baru. Klik tautan
  relatif antar-dokumen memakai `OpenFile` (bukan `OpenUserFile`), jadi tidak mengadopsi (`MainWindow.Attach`, `:310`).
- **Bukti.** `MainWindow.xaml.cs:28-31, 58-61, 78-84, 217-234`, `AppSettings.cs:99-110`. Test hanya di tingkat `AppSettings`:
  `SaveMerged_KeepStoredSession_*` (`HardeningTests.cs:763-783`, `IoAndUtilityCoverageTests.cs:769-802`). Logika `MainWindow` tidak diuji.

## ADR-10 `NormalizeLineEndings` dan batas waktu regex

- **Konteks.** Dokumen Windows memakai CRLF. Dengan `RegexOptions.Multiline`, `$` hanya cocok sebelum `\n` (bukan sebelum `\r\n`)
  dan `.` menangkap `\r` (komentar `SearchEngine.cs:75-77`). Pola regex dari pengguna dijalankan di UI thread; pola lambat membekukan
  aplikasi (komentar `SearchEngine.cs:22-23`).
- **Keputusan.** `NormalizeLineEndings` menulis ulang pola: di luar kelas karakter, escape, dan komentar `(?#...)`, `$` menjadi
  `(?=\r?$)` dan `.` menjadi `[^\r\n]`. Flag inline `s` (titik menangkap baris baru) dihormati sesuai cakupan grup (`(?s)`, `(?s:...)`,
  `(?-s)`); pola dengan flag `x` dibiarkan utuh. Subtraksi kelas `-[` dikenali; `[` lain di dalam kelas literal. Pembatasan:
  `RegexTimeout` 2 dtk per pemanggilan `Match`, `DefaultTotalTimeout` 4 dtk untuk satu pencarian (diperiksa pada **setiap** iterasi,
  karena satu `NextMatch` bisa memakan 2 dtk), `MaxResults` 20.000 untuk penanda (Ganti Semua memakai `int.MaxValue`). Pola yang kena
  batas waktu disimpan sebagai `timedOutKey` di `FindReplaceBar` dan tidak dijalankan ulang otomatis (tiap perubahan teks/Cari
  Berikutnya akan membekukan UI lagi) sampai pola atau opsi berubah. Input kolom cari didebounce 250 ms.
- **Konsekuensi.** Pola mode `x` tidak sadar-CRLF. Pola yang berakhir dengan backslash menjadi "Regex tidak valid" (bukan crash).
  Pembekuan UI sampai ~4 dtk (+ hingga 2 dtk untuk satu match) tetap mungkin karena sinkron.
- **Bukti.** `SearchEngine.cs:14-265`, `FindReplaceBar.xaml.cs:28-30, 155-199`. Test: `SearchEngineEdgeCaseTests`
  (`IoAndUtilityCoverageTests.cs:864-1069`), `SearchEngineFindTests` (`SearchEngineTests.cs`), `TimedOutPattern_IsNotRerun*`
  (`DocumentViewLifecycleTests.cs:213`).

## ADR-11 `CrashLog` dan `IsRecoverable`

- **Konteks.** Galat tak tertangani harus dicatat dan ditampilkan ramah (CLAUDE.md), tetapi melanjutkan setelah keadaan program
  rusak berbahaya (komentar `CrashLog.cs:51-56`).
- **Keputusan.** `IsRecoverable` adalah allowlist tipe: `IOException`, `UnauthorizedAccessException`, `NotSupportedException`,
  `Win32Exception`, `RegexMatchTimeoutException`, `FormatException`, `UriFormatException`; `COMException` hanya untuk HRESULT yang
  dikenal (clipboard `0x800401D0-D5`, WIC `0x88982F00-FF`). Selain itu (OOM, `NullReferenceException`, `InvalidOperationException`,
  `ArgumentException`, dst.) fatal: sesi disimpan lewat `TrySaveSession`, dialog galat tampil, `e.Handled = false` agar runtime
  mengakhiri proses. Galat yang dipulihkan menampilkan dialog sekali per jenis+pesan dalam 10 dtk (`ShouldShowDialog`), tetapi tetap
  dicatat. `ExternalChangeException` adalah turunan `IOException`, jadi `IsRecoverable` bernilai true dan `MainWindow` menampilkannya sebagai galat
  I/O biasa (komentar test `DocumentTabConflictEdgeTests.cs:71`). Log: tidak pernah melempar, kunci `lock`,
  stempel waktu `InvariantCulture`, batas 512 KB.
- **Konsekuensi.** Bug kecil bertipe `InvalidOperationException`/`NullReferenceException` di jalur UI menutup aplikasi (tab yang
  belum disimpan hilang: hanya path/mode/caret yang masuk sesi). Perilaku "dipangkas otomatis" di README sebenarnya: bila log sudah
  lebih besar dari 512 KB, **seluruh** file dihapus lalu entri baru ditulis (`CrashLog.cs:37`; test `Write_LogOneByteOverTheLimit_IsDiscarded_*`).
- **Bukti.** `CrashLog.cs`, `App.xaml.cs:66-104`. Test: `CrashLogTests` (`CrashLogTests.cs`), `CrashLogLimitsTests`
  (`IoAndUtilityCoverageTests.cs:466-658`). Test dialihkan dari log asli oleh `TestLogRedirect` (`[ModuleInitializer]`).
  `App.OnDispatcherUnhandledException` sendiri tidak diuji.

## ADR-12 Tema lewat `ResourceDictionary`

- **Konteks.** Pergantian tema (Terang/Gelap/Ikuti Sistem) harus berlaku seketika tanpa memuat ulang jendela.
- **Keputusan.** `Themes/Light.xaml` dan `Dark.xaml` memuat 43 brush semantik dengan kunci **identik** (komentar di kedua berkas); semua
  gaya di `Controls.xaml`/`Preview.xaml` memakai `DynamicResource`. `ThemeManager.Apply` menukar kamus di `Application.Resources
  .MergedDictionaries` pada indeks yang sama (tidak menumpuk), memanggil `EditorTheme.ApplyMarkdownHighlighting`, menyesuaikan title
  bar (`DwmSetWindowAttribute` dengan konstanta `DwmUseImmersiveDarkMode` = 20, `Theming.cs:19`), lalu memicu `ThemeChanged`. Properti AvalonEdit yang bukan dependency property biasa
  diikat lewat `SetResourceReference`/`RefreshTheme`. Warna highlighting dijaga kontras >= 4,5:1 terhadap `EditorBackgroundBrush`
  (`EnsureContrast`, arah dipilih berdasarkan kontras yang dapat dicapai, bukan luminansi 0,5). Cetak memuat kamus Terang sendiri
  (`ThemeManager.LoadDictionary(dark: false)`) agar kertas putih. Mode `System` membaca HKCU `...\Themes\Personalize\AppsUseLightTheme`
  dan mendengarkan `SystemEvents.UserPreferenceChanged`; `Apply` dipanggil di konstruktor `MainWindow` **sebelum** `InitializeComponent`
  agar `DynamicResource` langsung menemukan brush.
- **Konsekuensi.** Tidak ada test yang memeriksa kesamaan kunci Light/Dark (hanya komentar); kunci yang hilang di salah satu kamus
  baru ketahuan saat dijalankan. Lihat [CONTRIBUTING.md](CONTRIBUTING.md#menambah-temakunci-brush-baru) untuk cara memeriksanya.
- **Bukti.** `Theming.cs`, `EditorTheme.cs`, `Themes/*.xaml`, `MainWindow.xaml.cs:40`. Test: `ThemeManagerApplyTests`
  (`DocumentTabTests.cs:1051-1148`, menukar kamus pada `Application` bersama), `EditorThemeContrastTests` (`SmallUtilityTests.cs:249`),
  `ThemeManagerPureTests` (`:199`).

## ADR-13 Nama `AppThemeMode`

- **Konteks.** Enum pilihan tema bernama `AppThemeMode { System, Light, Dark }` (`Theming.cs:9`).
- **Keputusan.** Alasan pemilihan nama **tidak tercatat** di kode, komentar, test, README, maupun riwayat git. Dugaan, belum
  diverifikasi: awalan `App` menghindari bentrok/ambigu dengan tipe bawaan WPF (sejak .NET 9) bernama `ThemeMode`.
- **Fakta yang terbukti dari kode.** Nilai disimpan sebagai **teks** di `AppSettings.Theme` (komentar `AppSettings.cs:40`: "agar nilai
  tak dikenal tidak merusak seluruh file"); `ThemeManager.Parse` tidak peka huruf besar-kecil dan nilai tak dikenal/kosong/angka menjadi
  `System`; `Sanitize` menormalkan saat `Load`. Hal yang sama berlaku untuk `SessionTab.Mode` (`ViewMode`). Konsekuensinya: mengganti
  nama anggota enum yang tersimpan (mis. `Dark`) akan membuat pengaturan lama jatuh ke default tanpa error.
- **Bukti.** `Theming.cs:9, 33-34`, `AppSettings.cs:40-41, 53, 128-131`. Test: `Parse_KnownNamesIgnoringCase_ElseSystem`
  (`SmallUtilityTests.cs:216`), `Load_Theme_IsNormalizedToKnownName` (`AppSettingsTests.cs:127`).

## ADR-14 Render pratinjau: debounce, parse latar, dan generation check

- **Konteks.** Mengetik harus tetap lancar pada dokumen besar (komentar `DocumentView.xaml.cs:22`).
- **Keputusan.** Debounce `renderTimer` 250 ms, naik ke 800 ms (>= 200 rb karakter) dan 1500 ms (>= 1 juta). Statistik kata
  dihitung lebih jarang (300 ms; 2000 ms dari 1 juta) dan per potongan 64 KB tanpa menyalin string utuh. Render dilewati bila
  `Document.Version` sama dengan render terakhir (tanpa menyalin teks) atau teks sama dengan `lastRenderedText` (mis. setelah Undo). Dokumen
  >= 100 rb karakter di-parse di thread latar; `renderGeneration` membuang hasil basi (render lebih baru atau tab ditutup).
  `FlowDocument` tetap dibuat di UI thread (wajib). Scroll dipertahankan setelah render; mode Terpisah mengikuti posisi editor secara
  proporsional dengan penanda "expected offset" untuk memutus umpan balik. Hanya dilakukan bila pratinjau tampak.
- **Konsekuensi.** README menyederhanakan ambang sebagai "di atas 1 MB"; kode memakai ambang karakter berjenjang (100 rb, 200 rb,
  1 juta). Pembuatan `FlowDocument` dokumen sangat besar tetap membekukan UI; pengukuran kemudian menunjukkan "sesaat" bisa
  berarti sekitar 8 detik (200 KB), 16 detik (500 KB), sampai lebih dari 5 menit (1,5 MB), karena WPF menata satu `FlowDocument` raksasa secara superlinear
  (angka: README, Batasan yang diketahui). Parse latar tidak mengurangi bagian ini; bahwa penataan berjalan di UI thread adalah
  inferensi dari UI yang tak merespons, belum diverifikasi lebih jauh.
- **Bukti.** `DocumentView.xaml.cs:22-29, 97-108, 332-452`. Test: `StaleBackgroundRender_NeverReplacesTheNewerRender`
  (`DocumentViewLifecycleTests.cs:333`), `BackgroundRender_AfterDispose_*` (`:359`), `BackgroundRender_LargeDocument*` (`:395`).
  Sinkron scroll dan lompat `#anchor` tidak diuji.

## ADR-15 Pengaturan tidak pernah melempar dan digabung saat simpan

- **Konteks.** Beberapa instance bisa berjalan (mis. beda sesi Windows atau bila pipe gagal) dan `settings.json` bisa diedit tangan
  atau rusak (komentar `AppSettings.cs:127`, README).
- **Keputusan.** `Load` dan `Save` menangkap galat I/O/JSON dan mengembalikan bawaan/`false`; tipe salah pada satu properti
  membuat seluruh file jatuh ke bawaan. `Sanitize` membuang entri tak masuk akal (path kosong, duplikat, zoom di luar 50-300,
  indeks aktif di luar rentang). `SaveMerged` memuat ulang file lebih dulu dan menggabungkan `RecentFiles` (milik instance ini di
  depan, sisanya dari file, maksimal 10, tanpa membedakan huruf besar-kecil, melewatkan yang sengaja dihapus lewat `removedRecent`)
  supaya salinan usang tidak menimpa instance lain. Hanya `RecentFiles` yang digabung; tema/zoom/blokir gambar diambil dari instance
  yang menyimpan terakhir.
- **Konsekuensi.** File yang rusak ditimpa diam-diam oleh `SaveMerged` berikutnya (test `SaveMerged_CorruptStoredFile_IsReplacedByOwnSettings`).
  Tidak ada penguncian antar-proses: dua instance yang menyimpan bersamaan tetap bisa saling menimpa antara `Load` dan `Save`
  (inferensi dari kode, tidak diuji).
- **Bukti.** `AppSettings.cs:61-145`. Test: `AppSettingsTests` (40 test), `AppSettingsMergeTests`, `AppSettingsMergeEdgeTests`.

## ADR-16 Pembukaan dokumen hanya lewat `OpenFile`, dengan batas ukuran

- **Konteks.** Seluruh file dibaca ke memori lalu didekode (komentar `MainWindow.xaml.cs:19, 279`).
- **Keputusan.** `MainWindow.OpenFile` adalah satu-satunya pintu: `GetFullPath`, deteksi tab ganda (tab yang sudah ada dipilih), konfirmasi
  > 50 MB, tolak > 500 MB, tangkap `OutOfMemoryException` dan `IOException`/`UnauthorizedAccessException` dengan pesan ramah.
- **Konsekuensi.** `OpenFile` tidak memeriksa ekstensi; penyaringan ekstensi ada di seret-lepas, klik tautan, dan filter dialog
  (`MarkdownFiles.IsMarkdown` juga menyertakan `.txt`). Batas ukuran dan jalur OOM tidak diuji otomatis.
- **Bukti.** `MainWindow.xaml.cs:20-21, 238-306`.

## ADR-17 Klik tautan di pratinjau

- **Konteks.** Komentar `DocumentView.xaml.cs:699-700`: hanya skema aman yang diserahkan ke shell; tautan ke file lokal non-markdown
  diabaikan "agar dokumen tidak bisa menjalankan file lain di sebelahnya". Versi awal menggabungkan `Path.Combine(baseDir,
  Uri.UnescapeDataString(url))` lalu memanggil `File.Exists` sebelum cek ekstensi; tautan ter-percent-encode ke UNC
  (`%5C%5Chost%5Cs%5Cx.md`, `%2F%2Fhost%2Fs%2Fx.md`) memicu koneksi SMB/NTLM dengan satu klik (risiko R1, sekarang ditutup).
- **Keputusan.** `OnHyperlink`: `#anchor` -> lompat di dokumen yang sama; skema absolut selain `file` -> hanya `http`/`https`/`mailto`
  ke `Process.Start(UseShellExecute)` (galat shell ditangkap dan dicatat); selain itu path diselesaikan oleh
  `MarkdownSupport.ResolveLinkTarget` (murni, tanpa I/O): `file:` tanpa host dikonversi lewat `Uri.LocalPath` (berhost ditolak), tautan
  relatif didekode dan digabung dengan folder dokumen, path perangkat `\\?\`/`\\.\` ditolak, ekstensi harus markdown
  (`MarkdownFiles.IsMarkdown`), lalu `IsAllowedLocalPath` (UNC hanya di share dokumen sendiri). Hanya bila helper mengembalikan path,
  `OnHyperlink` memanggil `File.Exists` dan `RequestOpen` (dibuka sebagai tab oleh `MainWindow`, dengan anchor).
- **Konsekuensi.** Allowlist ini terpisah dari `ClassifyUrl` (yang dipakai ekspor/gambar) tetapi memakai `IsAllowedLocalPath` yang sama
  dengan gambar pratinjau. Urutan cek (ekstensi dan lokasi sebelum `File.Exists`) adalah bagian dari kontrak keamanan; jangan dibalik.
  Lihat [SECURITY.md](SECURITY.md#4-risiko-yang-sudah-ditutup) (R1, ditutup) dan [bagian 2.4](SECURITY.md#24-unc-smb-ntlm).
- **Bukti.** `MarkdownSupport.cs:361-412`, `DocumentView.xaml.cs:673-712`. Test: `LinkResolutionTests.cs` (resolusi path). Pembungkus
  `OnHyperlink` tidak punya test (hanya `RequestOpen` yang diuji, `DocumentTabTests.cs:274`).

## ADR-18 Skrip asosiasi file hanya HKCU

- **Konteks.** README: hanya menulis ke HKCU, tanpa hak administrator; Windows 10/11 melindungi pilihan aplikasi bawaan.
- **Keputusan.** `register-file-association.ps1`/`unregister-file-association.ps1` memakai `SupportsShouldProcess` (`-WhatIf`), menulis
  lewat `Registry.CurrentUser`, memvalidasi `-Extensions` dengan `'^\.[a-z0-9]+$'` (ekstensi dipakai sebagai nama subkey registri),
  dan `-SetDefault` mencadangkan nilai bawaan lama ke `HKCU\Software\Makdon\PreviousDefault` yang dipulihkan skrip unregister.
  Unregister hanya menyentuh nilai bawaan yang persis menunjuk ke ProgID Makdon dan tidak menyentuh `UserChoice`.
  Unregister juga membersihkan sisa pendaftaran lama dari masa aplikasi ini bernama MdViewer (ProgID `MdViewer.Markdown`,
  `Applications\MdViewer.exe`, `Software\MdViewer`) dengan aturan yang sama; register tidak membuat kunci lama.
- **Konsekuensi.** Skrip tidak bisa memaksa Makdon menjadi aplikasi bawaan. Tidak ada test otomatis untuk skrip (CLAUDE.md melarang
  menjalankan skrip registri sungguhan di test).
- **Bukti.** `scripts/register-file-association.ps1:37, 68-70, 81-140`, `scripts/unregister-file-association.ps1:20, 36-38, 57-114`.

## ADR-19 Pratinjau Cetak lewat paket XPS di memori

- **Konteks.** Pratinjau Cetak harus memperlihatkan halaman yang persis sama dengan yang tercetak, termasuk kaki halaman "Halaman X dari N"
  yang butuh N (jumlah halaman akhir) sudah diketahui. Komentar `PreviewBuild.cs:11-15`: `DocumentViewer` "hanya menerima dokumen
  tetap, bukan `FlowDocument`". Tidak ada test yang membuktikan penolakan itu (klaim perilaku WPF dari komentar; **belum diverifikasi** di
  repo ini). Alternatif lain yang dipertimbangkan tidak tercatat di kode, komentar, maupun test.
- **Keputusan.** `PreviewBuild` memaginasi `FlowDocument` cetak di latar, lalu menulis halaman lewat `XpsDocumentWriter.WriteAsync` ke paket
  XPS di `MemoryStream` (didaftarkan di `PackageStore`, URI `pack://makdon-preview-N.xps`) dan menyerahkan `FixedDocumentSequence` ke
  `DocumentViewer` (`PreviewBuild.cs:152-171, 198`). Yang ditulis adalah `HeaderFooterPaginator` di atas paginator FlowDocument, yaitu
  paginator yang sama dengan jalur Cetak langsung, jadi "yang tampil sama dengan yang tercetak" (komentar `PreviewBuild.cs:13-15`).
  `DocumentViewer` baru diisi pada tahap `Ready` (`PrintPreviewWindow.xaml.cs:166-173`), yaitu sesudah paginasi dan penulisan XPS selesai
  (komentar kelas di `:15-16` hanya menyebut "setelah paginasi selesai"). Tombol Cetak mencetak paket yang sama
  (`build.Pages.DocumentPaginator`), bukan hasil penyusunan ulang.
- **Konsekuensi.** Siklus hidup paket menjadi rumit dan wajib dikelola ([ADR-25](#adr-25-galat-pratinjau-dibungkus-guard-dan-paket-xps-dibersihkan-setelah-idle)).
  Halaman pratinjau berukuran tetap, jadi kertas/orientasi tidak bisa diubah sesudah penulisan ([ADR-22](#adr-22-pratinjau-punya-pengaturan-kertas-sendiri-dan-konfirmasi-bila-dialog-cetak-berbeda)).
  Tiap perubahan layout atau kaki halaman menulis ulang seluruh paket. Keterbatasan yang dicatat penulisnya (`PreviewBuild.cs:23-25`): gambar
  `http(s)` (bila blokir remote dimatikan) dimuat async oleh WPF dan bisa belum terunduh saat halaman dibekukan di XPS, sehingga tampil
  kosong; gambar lokal tidak terpengaruh. Seluruh halaman tinggal di memori selama jendela terbuka (ukurannya untuk dokumen besar tidak diukur).
- **Bukti.** `PreviewBuild.cs`, `PrintPreviewWindow.xaml.cs:103-177, 446-447`. Test: `PreviewBuild_ProducesFixedPagesMatchingThePaginator_*`
  (`PrintPreviewTests.cs:298`), `XpsPages_CarryDocumentNameAndPageXOfN_WhenFooterIsOn`, `XpsPages_HaveTheLayoutPageSize`
  (`PreviewBuildTests.cs:45, 86`). Pencetakan nyata dari paket (`dialog.PrintDocument`) tidak teruji ([TESTING.md](TESTING.md#yang-tidak-teruji)).

## ADR-20 Snapshot saat pratinjau dibuka dan parse yang dipakai bersama

- **Konteks.** Komentar `PrintLayout.cs:14-17`: snapshot "diambil sekali saat pratinjau dibuka sehingga penyuntingan sesudahnya tidak
  mengubah pratinjau, dan editor tidak terkunci". Komentar `MainWindow.xaml.cs:765`: jendela modal, jadi editor tidak bisa berubah selama
  terbuka. Dokumen besar tidak boleh membekukan UI saat pratinjau dibuka (komentar `PrintLayout.cs:82-87`).
- **Keputusan.** `DocumentView.CapturePrintSnapshot` menyalin teks, folder dokumen, `BlockRemoteImages`, dan judul (`PrintSnapshot`).
  `PrintPreviewWindow` membuat satu `PrintSource` yang mem-parse sekali (sinkron di bawah `BackgroundParseChars` = 100 rb karakter, selain itu
  `Task.Run`); ganti kertas/margin/orientasi/kaki halaman hanya membuat `FlowDocument` baru dari AST yang sama. AST dipakai bersama antarsiklus
  karena renderer hanya membacanya; satu-satunya yang mengubahnya adalah `ReplaceUnloadableImages` pada jalur galat, yang hanya jalan di UI
  thread dan idempoten (komentar `PrintLayout.cs:86-88`, `DocumentView.xaml.cs:594-595`). Flag blokir remote diambil dari snapshot, bukan dari
  nilai global saat penyusunan ulang. Parse latar tidak dibatalkan saat jendela ditutup: Markdig tak punya titik pembatalan, token hanya
  mencegah tugas yang belum mulai (komentar `PrintLayout.cs:89-90`).
- **Konsekuensi.** Pratinjau bisa berbeda dari isi tab bila tab berubah lewat jalur lain sesudahnya (mis. dimuat ulang oleh perubahan
  eksternal); judul dan folder gambar tetap yang lama walau tab disimpan ke path lain atau ditutup. Parse latar yang sudah berjalan
  terus memakai CPU sampai selesai.
- **Bukti.** `PrintLayout.cs:14-18, 82-125`, `DocumentView.xaml.cs:585-596`, `PrintPreviewWindow.xaml.cs:45-52`. Test:
  `Snapshot_IsNotAffectedByLaterEditorChanges` (`PrintPreviewTests.cs:218`), `Snapshot_ClosingTheTabAfterOpeningThePreview_*` dan
  `Snapshot_ChangingTheFilePathAfterOpening_*` (`PrintPreviewWindowBehaviorTests.cs:1048, 1075`), `SnapshotBlockRemoteFlag_IsHonoured_*`
  (`PrintContentAndCommandTests.cs:264`), `TwoBuildsFromOneSource_ShareTheParse_*` dan `LargeDocument_*` (`PreviewBuildTests.cs:631, 750, 776`),
  `LargeDocument_OpensWithoutBlocking_AndLayoutChangesReuseTheParse` (`PrintPreviewWindowBehaviorTests.cs:1005`).

## ADR-21 Kaki halaman di dalam margin bawah

- **Konteks.** Cetak dan pratinjau perlu nama dokumen dan nomor halaman, tetapi menambah kaki tidak boleh mengubah paginasi isi (jumlah dan
  isi halaman harus sama dengan atau tanpa kaki). Komentar `HeaderFooterPaginator.cs:80-84`: margin Sempit menaruh teks sekitar 17 DIP dari tepi
  kertas, di dalam zona yang tak bisa dicetak banyak printer.
- **Keputusan.** `HeaderFooterPaginator` membungkus paginator FlowDocument dan menggambar kaki (nama dokumen di kiri, dipotong dengan elipsis
  pada satu baris; "Halaman X dari N" di kanan; Segoe UI 10, abu-abu) di dalam margin bawah. Posisi: di tengah margin bawah, tetapi tidak lebih
  dekat dari 0,25 inci (`MinFooterEdgeDistance` = 24 DIP) ke tepi kertas, dan tidak pernah keluar dari margin bawah. Bila jumlah halaman
  belum diketahui hanya "Halaman X". Hanya kaki yang ada (tidak ada kepala halaman, walau namanya `HeaderFooterPaginator`). Cetak langsung
  (Ctrl+P) selalu memakai kaki (`headerFooter: true`, `MainWindow.xaml.cs:756`); di pratinjau bisa dimatikan lewat kotak "Nama dan nomor halaman".
- **Konsekuensi.** Untuk margin yang sangat kecil (kurang dari 24 DIP ditambah tinggi teks) teks tetap di dalam margin dan bisa lebih dekat dari
  0,25 inci ke tepi (komentar kode: "tetap di dalam margin bawah ... untuk margin yang sangat kecil"). Ketiga preset margin memenuhi batas itu.
  Kaki ikut Ctrl+P, jadi cetak langsung kini berbeda dari versi sebelum fitur ini (lihat [CHANGELOG](../CHANGELOG.md)).
  Warna dan font kaki ditulis tetap di kode (`HeaderFooterPaginator.cs:17-27`), tidak mengikuti tema.
- **Bukti.** `HeaderFooterPaginator.cs:14-92`. Test (`HeaderFooterPaginatorTests.cs`): `Footer_DoesNotEnterTheContentArea_SoBodyTextIsNotMovedByIt` (`:74`),
  `PageCount_IsTheSameWithAndWithoutFooter_AndAsTheBasePaginator` (`:58`), `Footer_IsDrawnInsideTheBottomMargin` (`:307`),
  `Footer_StaysAtLeastAQuarterInchFromThePaperEdge_ForEveryRealMarginPreset` (`:325`), `VeryLongName_IsTrimmedToOneLine_*` (`:275`),
  `OddDocumentNames_*` (`:258`, 15 nama aneh), `Footer_WhenCountIsNotYetKnown_ShowsOnlyThePageNumber` (`:219`).

## ADR-22 Pratinjau punya pengaturan kertas sendiri dan konfirmasi bila dialog Cetak berbeda

- **Konteks.** Komentar `PrintPreviewWindow.xaml.cs:462-465`: halaman pratinjau berukuran tetap (XPS). Bila pengguna mengganti kertas atau
  orientasi di dialog Cetak, pilihannya tidak boleh ditimpa diam-diam dan juga tidak dipaksakan ke printer tanpa tanya; membangun ulang halaman
  dari pilihan dialog tidak bisa dilakukan di situ (dialog sudah tertutup, dan kertas seperti Legal/A5 tidak punya preset di pratinjau).
- **Keputusan.** Pratinjau punya pilihan sendiri: Orientasi (Potret/Lanskap), Kertas (A4/Letter), Margin (0,5"/0,75"/1") dan kaki halaman. Sebelum
  `PrintDialog` tampil, tiketnya disamakan dengan pratinjau (`ApplyTicket(dialog)`, ukuran eksplisit supaya tidak bergantung tabel kertas
  driver, komentar `:499`). Sesudah dialog diterima, `ConfirmPaperMatchesPreview` membandingkan tiket dengan pratinjau (`TicketMatches`: nama
  kertas termasuk varian `Rotated`, atau lebar/tinggi dengan toleransi 4 DIP; nilai yang tidak diisi dianggap cocok). Bila beda, `ChoiceDialog`
  menawarkan "Cetak sesuai pratinjau" atau "Batal" (bawaan: Batal; Esc/X = Batal). "Cetak sesuai pratinjau" menyetel tiket ke kertas dan
  orientasi pratinjau. Tanpa printer terpasang, `dialog.PrintTicket` melempar dan konfirmasi dilewati (dialog Cetak sendiri yang melaporkan).
  Nama kertas yang ditampilkan dipetakan ke nama lazim (`PaperName`), selebihnya nama enum apa adanya.
- **Konsekuensi.** Pengaturan pratinjau tidak disimpan antarpembukaan (bawaan A4, potret, Normal). Cetak dengan kertas selain A4/Letter hanya
  bisa dengan ukuran pratinjau. Cetak langsung (Ctrl+P) berlawanan: ukuran halaman diambil dari dialog (`PageLayout.FromPrintableArea`).
  Bahwa mengubah objek `dialog.PrintTicket` benar-benar dipakai saat `PrintDocument` **belum diverifikasi** (test hanya memeriksa `ApplyTicket`
  pada `PrintTicket` terpisah).
- **Bukti.** `PrintPreviewWindow.xaml.cs:421-569`, `PrintLayout.cs:54-65`. Test: `ApplyTicket_*`, `TicketMatches_*`, `DescribeTicket_*`
  (`PrintContentAndCommandTests.cs:421-528`). `ConfirmPaperMatchesPreview` sendiri (dengan `ChoiceDialog`) dan pencetakan nyata tidak
  dijalankan test mana pun; hook `ShowPrintDialogForTests` hanya dipakai dengan hasil "batal" (`PrintPreviewWindowBehaviorTests.cs:889, 921`).

## ADR-23 Dokumen cetak selalu bertema Terang

- **Konteks.** Aplikasi bisa bertema Gelap, tetapi kertas putih. Pratinjau utama memakai tema aktif.
- **Keputusan.** `DocumentView.BuildPrintDocument(parsed)` merender ulang dengan `ThemeManager.LoadDictionary(dark: false)` dan `Background = White`
  (`DocumentView.xaml.cs:596-602`). Berlaku untuk Cetak langsung dan Pratinjau Cetak. Jendela pratinjau sendiri bertema aplikasi (area abu-abu
  memakai `SurfaceAltBrush`), hanya kertasnya putih. `PageLayout.Apply` menimpa `PagePadding` bawaan 48 dengan margin preset.
- **Konsekuensi.** Kunci brush harus ada di `Light.xaml` (lihat [CONTRIBUTING.md](CONTRIBUTING.md#menambah-temakunci-brush-baru)). Lihat juga
  [ADR-12](#adr-12-tema-lewat-resourcedictionary).
- **Bukti.** `DocumentView.xaml.cs:596-602`, `PrintLayout.cs:73-79`. Test: `CreateDocument_UsesTheLightTheme_EvenWhenTheAppIsDark`,
  `CreateDocument_IsWhitePaper_WithLayoutSizeAndMargin_NotTheDefaultPadding` (`PrintContentAndCommandTests.cs:31, 46`).

## ADR-24 Dokumen galat tidak pernah dicetak

- **Konteks.** Bila render gagal, pratinjau utama menampilkan dokumen galat (`CreateErrorDocument`) yang memuat `ex.Message` dan jalur
  `CrashLog.LogPath` (`DocumentView.xaml.cs:529-535`), yaitu path profil pengguna. Komentar `DocumentView.xaml.cs:455-458`: dokumen galat tidak
  boleh dicetak dan memuat path profil pengguna.
- **Keputusan.** Jalur cetak/pratinjau cetak memanggil `CreateFlowDocument(..., throwOnFailure: true)`: kegagalan akhir dilempar sebagai
  `InvalidOperationException` berpesan ramah (nama tipe galat asal + "Isi file tidak berubah. Rincian ada di crash.log.", tanpa path;
  galat asal tersimpan sebagai `InnerException` dan dicatat `CrashLog`). `PreviewBuild` menjadikannya `Failed` (panel "Pratinjau tidak dapat
  disusun.", tombol Cetak mati); Cetak langsung menampilkannya di `MessageBox` "Gagal mencetak.". Pratinjau utama tetap memakai dokumen galat
  seperti sebelumnya.
- **Konsekuensi.** Pesan di layar memakai `ex.Message` apa adanya untuk galat selain OOM; untuk kegagalan render pesannya sudah bebas path,
  tetapi galat lain dari pustaka (mis. I/O) dapat memuat path di layar (bukan di kertas). Lihat [SECURITY.md](SECURITY.md#212-pratinjau-cetak-dan-cetak).
- **Bukti.** `DocumentView.xaml.cs:455-483, 596-602`. Test: `RenderFailure_OnThePrintPath_FailsWithAFriendlyMessage_*`,
  `RenderFailure_InThePrintWindow_ShowsTheFriendlyMessageInThePanel_*`, `RenderFailure_InTheMainPreview_StillShowsTheErrorDocument_AsBefore`
  (`PrintContentAndCommandTests.cs:88, 122, 148`; memakai `DocumentView.RenderFaultForTests`).

## ADR-25 Galat pratinjau dibungkus `Guard` dan paket XPS dibersihkan setelah idle

- **Konteks.** Komentar `PreviewBuild.cs:17-21`: pratinjau tidak mengubah dokumen, jadi galatnya selalu boleh dipulihkan dan tidak boleh
  memicu jalur fatal aplikasi ([ADR-11](#adr-11-crashlog-dan-isrecoverable) menganggap OOM dan `InvalidOperationException` fatal untuk jalur
  lain). Komentar `PreviewBuild.cs:291-292`: `DocumentViewer` memuat `PageContent` async lewat `pack://`; menutup paket lebih awal membuat
  pemuatan yang sudah antre melempar `UriFormatException` di dispatcher. Komentar `:259-260`: `CancelAsync` hanya menandai batal, penulis masih
  bisa menjalankan callback berikutnya.
- **Keputusan.** Semua penangan peristiwa `PreviewBuild` dibungkus `Guard`: galat (termasuk OOM) menjadikan siklus `Failed` lewat `Fail`
  (dicatat, penulis dibatalkan, penerima `Changed` yang melempar ikut ditangkap); galat sesudah `Failed`/`Dispose` hanya dicatat. Galat di
  kode jendela ditangkap `StartBuild`/`OnBuildChanged` dan ditampilkan `ShowFailure`. `Dispose` membuang peristiwa, mematikan paginasi latar,
  lalu menutup paket hanya setelah penulis XPS melapor batal/selesai, di prioritas `ApplicationIdle`; timer cadangan 10 dtk menutup paket bila
  laporan itu tidak pernah datang, dan dihentikan begitu pembersihan dijadwalkan. Pembuatan jendela di `PrintPreview_Executed` ditangkap, tetapi
  `ShowDialog` sengaja di luar `try` (komentar `MainWindow.xaml.cs:770-772`): galat callback selama dialog tampil menjadi urusan
  `App.OnDispatcherUnhandledException`, bukan ditelan.
- **Konsekuensi.** Timer cadangan berarti paket bisa ditutup 10 dtk setelah `Dispose` walau penulis belum melapor; dampaknya pada penulis yang
  masih berjalan **belum diverifikasi**. Galat pada `PreviewBuild` atau jendela tampil sebagai panel galat, bukan dialog.
- **Bukti.** `PreviewBuild.cs:91-329`, `PrintPreviewWindow.xaml.cs:103-150`. Test: `SubscriberThatThrows_FailsTheBuild_AndNeverReachesTheDispatcher`
  (`PreviewBuildTests.cs:468`), `Dispose_WhileRendering_KeepsThePackageUntilTheWriterEnds_*`, `Dispose_WhileRendering_ArmsAFallbackTimer_*`,
  `Dispose_WhenCleanupIsAlreadyScheduledDuringCancel_*`, `RepeatedCycles_DisposedWhileRendering_*` (`:516, 548, 576, 608`),
  `ClosingRightAfterThePagesAppear_*`, `ChangingLayoutManyTimes_WhileRendering_ThenClosing_*` (`PrintPreviewWindowBehaviorTests.cs:759, 950`).

## ADR-26 Seam khusus test pada kode cetak

- **Konteks.** Dialog sistem (`PrintDialog`), printer, dan kegagalan renderer tidak bisa dipakai di test otomatis (CLAUDE.md: test tidak
  menyentuh data pengguna; membuka dialog sungguhan menggantung proses test). Komentar `PrintPreviewWindow.xaml.cs:432`: membaca tiket printer lambat.
- **Keputusan.** Kode produksi menyediakan titik sambung `internal` kecil: `PrintPreviewWindow.ShowPrintDialogForTests` (pengganti `ShowDialog`;
  false = batal), `DocumentView.RenderFaultForTests` (memaksa kegagalan renderer), konstruktor `PrintSource(snapshot, Task)` (menahan hasil parse),
  properti `PreviewBuild.Document`/`CleanupFallbackTimer` dan `PrintPreviewWindow.Stage`/`PrintedDocument`, serta helper statis murni
  (`TicketMatches`, `ApplyTicket`, `PaperName`, `DescribeTicket`). Sisi test: `WpfHost.Run` tidak lagi sekadar `Dispatcher.Invoke`, dan galat
  yang lolos ke dispatcher dicatat lalu menggagalkan test ([TESTING.md](TESTING.md#thread-sta-dan-wpfhost)).
- **Konsekuensi.** Permukaan `internal` bertambah. Beberapa test membaca anggota privat lewat refleksi (`PreviewBuild.counter`,
  `packageUri`, `cleanupScheduled`, metode `Cleanup`); mengganti namanya memecahkan test. Hook dialog hanya menguji cabang "batal"; cabang diterima
  (`ConfirmPaperMatchesPreview`, `dialog.PrintDocument`) tidak terjangkau test ([ADR-22](#adr-22-pratinjau-punya-pengaturan-kertas-sendiri-dan-konfirmasi-bila-dialog-cetak-berbeda)).
- **Bukti.** `PrintPreviewWindow.xaml.cs:418-419`, `DocumentView.xaml.cs:486-487`, `PrintLayout.cs:107-112`, `PreviewBuild.cs:82-86`,
  `PreviewBuildTests.cs:15-16, 430, 544-545, 590, 601`, `Support/WpfHost.cs`.

---

## ADR-27 Folder self-contained (bukan single-file) dan installer per pengguna

- **Konteks.** Single-file WPF tetap mengekstrak pustaka native ke folder sementara (jejak di `%TEMP%`, bertentangan dengan portable tanpa
  jejak). WPF tidak mendukung trimming. Pengguna umum tidak boleh diminta memasang .NET ([DISTRIBUTION.md](DISTRIBUTION.md) §1 #2, §2.1).
- **Keputusan.** Publish memakai profil `win-x64`: self-contained, `PublishSingleFile=false`, `PublishReadyToRun=false`, `PublishTrimmed=false`.
  Installer Inno Setup 6 dengan `PrivilegesRequired=lowest` dan `PrivilegesRequiredOverridesAllowed=dialog`: bawaan per pengguna ke
  `%LOCALAPPDATA%\Programs\Makdon` tanpa UAC; pilihan semua pengguna ke `Program Files`. Mode 64-bit wajib, `MinVersion=10.0.14393`.
  `IncludeNativeLibrariesForSelfExtract` dihapus dari csproj.
- **Konsekuensi.** Keluaran lebih besar: 155,5 MB terurai (258 berkas) dan 65,0 MB zip (diukur 2026-10-08). Tidak ada pemeriksaan pembaruan; update manual.
  ReadyToRun belum diukur manfaatnya. Installer belum pernah dikompilasi, jadi perilakunya **belum diverifikasi**.
- **Bukti.** `src/Makdon/Properties/PublishProfiles/win-x64.pubxml:11-17`; `src/Makdon/Makdon.csproj:22`; `installer/Makdon.iss:41-49`.

## ADR-28 Mode portable lewat penanda `Makdon.portable`, data di `data\`

- **Konteks.** Satu exe untuk dua mode. Portable harus tanpa jejak di `%APPDATA%`. Pengaturan tidak boleh pindah diam-diam ke tempat lain
  saat folder tak bisa ditulis (DISTRIBUTION §3).
- **Keputusan.** `AppPaths.Detect`: penanda di samping exe menentukan mode. `SettingsPath` dan `CrashLogPath` berada di `<folder exe>\data\`.
  Bila folder data tak bisa ditulis (`IsDataDirectoryWritable`), pengaturan hanya di memori dan pengguna diberi tahu sekali; **tidak** ada fallback
  ke `%APPDATA%`. Penanda tidak ikut installer (`Excludes`), dihapus dari bahan zip sebelum penanda portable dibuat, dan `Makdon.iss` menolak dikompilasi bila
  folder publish memuatnya.
- **Konsekuensi.** Data ikut folder (dan ikut media lepas; lihat [SECURITY.md](SECURITY.md) 2.13). Memindahkan folder portable membuat
  path pendaftaran basi (ditangani ADR-30). Pengguna yang menyalin folder portable juga menyalin pengaturannya.
- **Bukti.** `AppPaths.cs:13, 35-41, 60-65, 81-96`; `installer/Makdon.iss:22-25, 80`; `scripts/build-release.ps1:129-130, 155-158`; `AppPathsTests.cs`
  (`MarkerNextToTheExe_MeansPortable_*`, `IsDataDirectoryWritable_Portable_*`).

## ADR-29 Scope single-instance per folder exe untuk portable

- **Konteks.** Nama mutex dan pipe semula hanya memuat SID pengguna dan id sesi Windows. Portable dan terpasang di sesi yang sama
  akan saling meneruskan berkas: file yang dibuka di portable masuk ke instance terpasang yang sedang berjalan, atau sebaliknya.
- **Keputusan.** `AppPaths.SingleInstanceScope`: mode terpasang kosong (nama bawaan); portable `p` + 8 byte pertama SHA-256 dari path folder exe
  yang dinormalkan (huruf besar, tanpa pemisah di akhir). Nama jadi `Local\Makdon.SingleInstance.<SID>.<scope>` dan pipe
  `Makdon.<SID>.s<sesi>.<scope>`.
- **Konsekuensi.** Dua salinan portable di folder berbeda berjalan sebagai instance masing-masing. Dua portable di folder yang sama berbagi satu
  instance. Memindahkan folder portable menghasilkan scope baru.
- **Bukti.** `AppPaths.cs:47-55`; `SingleInstance.cs:43-44, 61-62`; `App.xaml.cs:24`; `AppPathsTests.cs`
  (`SingleInstanceScope_IsAStableHashOfTheExeFolder_WhenPortable`, `SingleInstanceScope_DiffersBetweenPortableFolders`).

## ADR-30 Pendaftaran "Buka dengan" portable di HKCU lewat `FileAssociation` dan `IRegistryStore`; tolak bila terpasang

- **Konteks.** Portable tidak memasang apa pun, tetapi pengguna ingin "Buka dengan" juga untuk versi portable. Installer tidak bisa
  membersihkan HKCU pengguna secara andal (DISTRIBUTION §4.3). HKCU menutupi HKLM dalam gabungan HKCR. Test tidak boleh menulis registri (CLAUDE.md).
- **Keputusan.** `FileAssociation` (bukan skrip) menulis tabel registri DISTRIBUTION 4.1 ke HKCU lewat `IRegistryStore`. Deteksi instalasi membaca kunci
  `Uninstall\{AppId}_is1` di HKCU dan HKLM (view 64-bit, `WOW6432Node`), lalu memeriksa `Makdon.exe` di folder yang tercatat.
  `Register` menolak (`BlockedByInstallation`) bila ada instalasi, tanpa memandang hive. Exe yang sudah terdaftar diklasifikasi
  (`ThisExe`, `Stale`, `OtherPortable`, `OtherExe`) dan hanya diganti setelah konfirmasi. `Unregister` hanya menghapus kunci yang menunjuk exe ini.
  Pemeriksaan startup portable (`CheckStartup`) menawarkan memperbarui path basi, atau mencabut pendaftaran portable yang menutupi instalasi.
  Perintah relatif diklasifikasi `OtherExe` dan tidak dinormalkan terhadap folder kerja; path UNC tidak diperiksa dengan `File.Exists` (`OtherExe`, tanpa I/O di thread UI); `Register` menolak `ExeNotFound` bila exe bukan `Makdon.exe` atau tidak ada (lihat [DISTRIBUTION.md](DISTRIBUTION.md) 4.3). Test memakai `FakeRegistryStore` (`Support/FakeRegistryStore.cs`).
- **Konsekuensi.** GUID `AppId` menjadi konstanta di tiga tempat (`AppInfo.cs:9`, `FileAssociation.cs:78-79`, `installer/Makdon.iss:34`) yang harus sama.
  Status "paling asing" bisa menyembunyikan tawaran pencabutan bila dua kunci menunjuk exe berbeda (dugaan, lihat DISTRIBUTION 4.3).
- **Bukti.** `FileAssociation.cs:106-125, 127-175, 178-184, 189-210, 213-237, 243-271`; `RegistryStore.cs:11-32`;
  `FileAssociationTests.cs` (`Installation_*`, `Register_*`, `Unregister_*`, `CheckStartup_*`, `Status_*`).

## ADR-31 Mutex installer bernama tetap (`Makdon.AppMutex`), bukan Restart Manager

- **Konteks.** Installer harus tahu Makdon sedang berjalan agar dokumen belum disimpan tidak hilang. Nama mutex single-instance memuat SID pengguna, sehingga
  tidak bisa dipakai installer (DISTRIBUTION §5.2). Restart Manager tidak ditangani aplikasi, dan `Window_Closing` bisa membatalkan penutupan
  (`MainWindow.xaml.cs:876`), jadi Restart Manager tidak bisa dipakai untuk memaksa penutupan dengan aman.
- **Keputusan.** `AppMutex=Makdon.AppMutex,Global\Makdon.AppMutex` di installer. Aplikasi membuat kedua nama itu (`InstallerMutex.Acquire`)
  hanya di mode terpasang, dipegang sepanjang proses, dan mengabaikan galat. Portable tidak membuatnya.
  `CloseApplications` (Restart Manager) tidak dipakai.
- **Konsekuensi.** Installer dan uninstaller meminta pengguna menutup Makdon bila masih berjalan. Installer tidak melihat instance portable yang sedang
  berjalan (sengaja: portable tidak boleh mengganggu instalasi). Penutupan oleh Restart Manager tidak didukung. Perilaku pemeriksaan oleh installer
  **belum diverifikasi**.
- **Bukti.** `installer/Makdon.iss:51-52`; `src/Makdon/InstallerMutex.cs:8-24`; `src/Makdon/AppInfo.cs:11-13`; `src/Makdon/App.xaml.cs:23`.

## ADR-32 .NET 10 dan switch XPS di runtimeconfig

- **Konteks.** .NET 9 berhenti didukung 2026-11-10; aplikasi self-contained membawa runtime ke pengguna (DISTRIBUTION §11).
  .NET 10 membatasi font halaman XPS hanya dari paket yang sama. Paket XPS di memori (Pratinjau Cetak) juga menolak font miliknya sendiri,
  sehingga setiap halaman bertekst gagal dan Pratinjau Cetak selalu gagal. `AppContext.SetSwitch` di kode tidak cukup karena WPF men-cache switch
  saat gambar pertama dimuat.
- **Keputusan.** Target `net10.0-windows`. `RuntimeHostConfigurationOption Switch.System.Windows.DisableXpsPackageBoundaryRestriction=true` di
  `Makdon.csproj` dan `Makdon.Tests.csproj`. Test `XpsBoundarySwitchTests` memeriksa bahwa runtimeconfig Makdon memuat switch itu dan
  proses test memasangnya juga. Alternatif yang tidak dipilih: menulis XPS ke berkas sementara.
- **Konsekuensi.** Pembatasan batas paket XPS dimatikan untuk seluruh proses. Alasan aman: Makdon hanya membaca XPS buatannya sendiri
  (risiko residual R14 di [SECURITY.md](SECURITY.md)). Setiap pindah versi .NET, switch dan perilaku ini harus diperiksa ulang.
- **Bukti.** `src/Makdon/Makdon.csproj:33-42`; `src/Makdon.Tests/Makdon.Tests.csproj:24-26`; `src/Makdon.Tests/WpfHostStartupTests.cs:48-70`
  (`XpsBoundarySwitchTests`).

## ADR-33 `WpfHost` memakai `TestApp` tanpa `OnStartup`

- **Konteks.** Konstruktor `Application` menitipkan pemanggilan `OnStartup` ke dispatcher, jadi `OnStartup` berjalan begitu `Dispatcher.Run` dimulai,
  walau tanpa `Application.Run`. Sebelumnya host test membuat `App` biasa, sehingga startup sungguhan ikut berjalan: mutex/pipe single-instance
  produksi, `MainWindow`, dan settings `%APPDATA%`. Bila Makdon sudah berjalan, test bisa meneruskan berkas ke instance itu lalu memanggil `Shutdown()`,
  dan setiap test WPF sesudahnya gagal dengan "The Application object is being shut down".
- **Keputusan.** `WpfHost.TestApp : App` menimpa `OnStartup` dengan kosong dan memuat resource `app.xaml` lewat `LoadAppXaml()`
  (BAML dibaca langsung ke instance itu, isi resource tetap sama dengan App sungguhan). Pencatat galat dispatcher tetap dipasang.
- **Konsekuensi.** Test tidak menjalankan `App.OnStartup` sama sekali; alur startup hanya bisa diuji secara terpisah (lihat TESTING "Yang tidak teruji").
  `InitializeComponent` tidak dipakai di host test.
- **Bukti.** `src/Makdon.Tests/Support/WpfHost.cs` (kelas `TestApp`); `src/Makdon.Tests/WpfHostStartupTests.cs:14-46`
  (`TestHost_DoesNotRunAppStartup_AndKeepsTheApplicationAlive`, `TestHost_LoadsTheSameThemeResourcesAsApp`).
