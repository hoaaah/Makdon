# Catatan Keputusan Desain (ADR ringkas)

**Tujuan:** mencatat keputusan desain yang tampak di kode MdViewer beserta konteks, keputusan, dan konsekuensinya, supaya
perubahan di masa depan tidak melanggar alasan aslinya tanpa sadar.
**Pembaca:** pengembang dan reviewer.

**Aturan dokumen ini:** alasan hanya diambil dari komentar kode, nama test, README, dan CLAUDE.md. Bila motivasi tidak tertulis di
mana pun, ditulis "tidak tercatat" atau "dugaan, belum diverifikasi". Riwayat git hanya satu commit (`448e1ad`), jadi tidak ada
sejarah keputusan yang bisa ditelusuri dari sana. Arsitektur: [ARCHITECTURE.md](ARCHITECTURE.md). Keamanan: [SECURITY.md](SECURITY.md).

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
| [08](#adr-08-single-instance-dengan-mutex-dan-named-pipe-per-sesi) | Single-instance Mutex + pipe per sesi |
| [09](#adr-09-aturan-sesi-preservestoredsession) | Aturan sesi `preserveStoredSession` |
| [10](#adr-10-normalizelineendings-dan-batas-waktu-regex) | `NormalizeLineEndings` + batas waktu regex |
| [11](#adr-11-crashlog-dan-isrecoverable) | `CrashLog` dan `IsRecoverable` |
| [12](#adr-12-tema-lewat-resourcedictionary) | Tema lewat `ResourceDictionary` |
| [13](#adr-13-nama-appthememode) | Nama `AppThemeMode` |
| [14](#adr-14-render-pratinjau-debounce-parse-latar-dan-generation-check) | Render pratinjau: debounce, parse latar, generation check |
| [15](#adr-15-pengaturan-tidak-pernah-melempar-dan-digabung-saat-simpan) | Pengaturan tidak pernah melempar, digabung saat simpan |
| [16](#adr-16-pembukaan-dokumen-hanya-lewat-openfile-dengan-batas-ukuran) | Pembukaan dokumen hanya lewat `OpenFile`, batas ukuran |
| [17](#adr-17-klik-tautan-di-pratinjau) | Klik tautan di pratinjau |
| [18](#adr-18-skrip-asosiasi-file-hanya-hkcu) | Skrip asosiasi file hanya HKCU |

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
  ditulis dengan `File.AppendAllText` (`CrashLog.cs:41`), bukan atomik.
- **Bukti.** `src/MdViewer/TextFileIO.cs:134-202`, `HtmlExporter.cs:66`, `AppSettings.cs:82`. Test:
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
- **Bukti.** `TextFileIO.cs:40-127`, `DocumentTab.cs:81, 183`, `MainWindow.xaml.cs:429-432`. Test: `TextFileIODecodeTests`
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
  saat aktivasi hanya untuk tab aktif (`MainWindow.xaml.cs:53`); tab lain bergantung pada watcher.
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
  lain atau konflik kedua untuk file yang sama saat dialog terbuka (komentar `MainWindow.xaml.cs:322-323`, `DocumentTab.cs:48-49`).
- **Keputusan.** Semua dialog konflik memakai `ChoiceDialog` dengan label yang menjelaskan akibat ("Muat dari Disk", "Pertahankan
  Editor", "Timpa", "Batal"); pilihan paling aman jadi bawaan (Enter) dan Esc/X = pilihan batal. Penanda `conflictPromptOpen` +
  `conflictQueue` menyerialisasi dialog; `OnSaveConflict` memakai penanda yang sama. `DocumentTab.SaveTo` menyetel `saving = true`
  sehingga `CheckExternalChange` tab itu ditunda selama dialog konflik simpan terbuka. Konflik yang datang selama `SaveTo`
  ditanyakan setelah `SaveTo` selesai (`TrySave` -> `ProcessConflictQueue`, kecuali sedang `closing`).
- **Konsekuensi.** Tidak boleh menampilkan `MessageBox`/dialog konflik langsung dari event (aturan CLAUDE.md). Konflik tidak pernah
  bertumpuk atau hilang diam-diam.
- **Bukti.** `MainWindow.xaml.cs:316-392, 427-448`, `ChoiceDialog.xaml.cs`. Test: `ChoiceDialogTests` (`ChoiceDialogTests.cs:46-66`),
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
- **Bukti.** `MarkdownSupport.cs:58-78, 86-115, 213-358`, `HtmlExporter.cs`, `MainWindow.xaml.cs:565-580`. Test:
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
  UNC hasil gabungan hanya boleh bila dokumen sendiri ada di share yang sama (`IsAllowedLocalPath`). Cetak memakai `ParseDocument` yang
  sama. Jaring pengaman: `ReplaceUnloadableImages` mengganti gambar lokal yang ada tetapi tak bisa di-decode dengan teks, supaya satu
  gambar rusak tidak menggagalkan seluruh pratinjau.
- **Konsekuensi.** `BlockRemoteImages` adalah flag statis global (`DocumentView.BlockRemoteImages`), bukan per tab; mengubahnya
  memanggil `RefreshPreview` di semua tab. Gambar `data:` sah tampil di ekspor tetapi tidak di pratinjau. Pratinjau tidak
  membatasi gambar lokal ke folder dokumen (hanya ekspor yang membatasi).
- **Bukti.** `MarkdownSupport.cs:122-200`, `DocumentView.xaml.cs:56, 418-511`, `MainWindow.xaml.cs:123-129`. Test:
  `ResolveImageUrlsSecurityTests` (`ExportAndImageSecurityTests.cs:330-491`), `RemoteImageBlockingTests` (`HardeningTests.cs:188-280`),
  `RemoteImageOverFtp_IsNotFetched_WhenRemoteImagesAreBlocked` (membuka `TcpListener` lokal dan memastikan WPF tidak terhubung,
  `DocumentViewLifecycleTests.cs:510`), `UndecodableImage_*` (`:414-459`), `DataImage_ShowsMarker_*` (`:468`).

## ADR-08 Single-instance dengan Mutex dan named pipe per sesi

- **Konteks.** Membuka `.md` saat MdViewer berjalan harus membuka tab di jendela yang ada, tetapi sesi Windows lain (mis. Remote
  Desktop) punya instance sendiri (README). Komentar `SingleInstance.cs:10-14`: `Mutex` `Local\` sudah per sesi, sedangkan nama pipe
  global se-mesin sehingga id sesi dimasukkan ke nama pipe agar cakupannya sama.
- **Keputusan.** Nama mutex `Local\MdViewer.SingleInstance.<SID>[.<scope>]`; nama pipe `MdViewer.<SID>.s<sessionId>[.<scope>]`; pipe
  dibuka `PipeOptions.CurrentUserOnly` di kedua sisi, satu instance server. Protokol teks UTF-8: baris pertama harus persis
  `MDVIEWER1`, lalu satu path per baris. Penerima menerima hanya path **fully-qualified** (`Path.IsPathFullyQualified`; `C:rel.md` dan
  `\rel.md` ditolak karena bergantung folder/drive kerja), panjang < 32768, maksimal 64 path, pesan maksimal 256 K karakter, batas
  baca 5 dtk per klien. `BuildMessage` membuang entri yang mengandung `\n`/`\r` (injeksi path). Klien yang macet atau mengirim sampah
  tidak menambah hitungan kegagalan; hanya kegagalan membuat/menunggu pipe yang dihitung (menyerah setelah 5). Seluruh kelas "tidak pernah
  melempar": bila mutex/pipe tak bisa dipakai, pemanggil membuka instance baru. `scope` memungkinkan test memakai nama unik. `App`
  mengubah argumen relatif menjadi mutlak sebelum dikirim.
- **Konsekuensi.** File yang dikirim ke instance yang sedang menutup diabaikan; peluncuran kedua tanpa argumen hanya mengaktifkan
  jendela yang ada (README). Server hanya berhenti sendiri setelah gagal berulang (dicatat ke `CrashLog`); sesudahnya peluncuran
  berikutnya dikirim ke pipe yang tidak melayani lalu gagal dan membuka instance sendiri (inferensi dari alur `App.OnStartup`, tidak diuji).
- **Bukti.** `SingleInstance.cs`, `App.xaml.cs:19-45`. Test: `SingleInstanceServerTests` (24 test, scope unik per test) dan
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
- **Bukti.** `MainWindow.xaml.cs:27-30, 56-59, 76-82, 215-232`, `AppSettings.cs:96-107`. Test hanya di tingkat `AppSettings`:
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
  rusak berbahaya (komentar `CrashLog.cs:52-57`).
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
  lebih besar dari 512 KB, **seluruh** file dihapus lalu entri baru ditulis (`CrashLog.cs:38`; test `Write_LogOneByteOverTheLimit_IsDiscarded_*`).
- **Bukti.** `CrashLog.cs`, `App.xaml.cs:64-102`. Test: `CrashLogTests` (`CrashLogTests.cs`), `CrashLogLimitsTests`
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
- **Bukti.** `Theming.cs`, `EditorTheme.cs`, `Themes/*.xaml`, `MainWindow.xaml.cs:39`. Test: `ThemeManagerApplyTests`
  (`DocumentTabTests.cs:1051-1148`, menukar kamus pada `Application` bersama), `EditorThemeContrastTests` (`SmallUtilityTests.cs:249`),
  `ThemeManagerPureTests` (`:199`).

## ADR-13 Nama `AppThemeMode`

- **Konteks.** Enum pilihan tema bernama `AppThemeMode { System, Light, Dark }` (`Theming.cs:9`).
- **Keputusan.** Alasan pemilihan nama **tidak tercatat** di kode, komentar, test, README, maupun riwayat git. Dugaan, belum
  diverifikasi: awalan `App` menghindari bentrok/ambigu dengan tipe bawaan WPF .NET 9 bernama `ThemeMode`.
- **Fakta yang terbukti dari kode.** Nilai disimpan sebagai **teks** di `AppSettings.Theme` (komentar `AppSettings.cs:40`: "agar nilai
  tak dikenal tidak merusak seluruh file"); `ThemeManager.Parse` tidak peka huruf besar-kecil dan nilai tak dikenal/kosong/angka menjadi
  `System`; `Sanitize` menormalkan saat `Load`. Hal yang sama berlaku untuk `SessionTab.Mode` (`ViewMode`). Konsekuensinya: mengganti
  nama anggota enum yang tersimpan (mis. `Dark`) akan membuat pengaturan lama jatuh ke default tanpa error.
- **Bukti.** `Theming.cs:9, 33-34`, `AppSettings.cs:40-41, 53, 125-128`. Test: `Parse_KnownNamesIgnoringCase_ElseSystem`
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
  1 juta). Pembuatan `FlowDocument` dokumen sangat besar tetap membekukan UI sesaat.
- **Bukti.** `DocumentView.xaml.cs:22-29, 91-102, 326-446`. Test: `StaleBackgroundRender_NeverReplacesTheNewerRender`
  (`DocumentViewLifecycleTests.cs:333`), `BackgroundRender_AfterDispose_*` (`:359`), `BackgroundRender_LargeDocument*` (`:395`).
  Sinkron scroll dan lompat `#anchor` tidak diuji.

## ADR-15 Pengaturan tidak pernah melempar dan digabung saat simpan

- **Konteks.** Beberapa instance bisa berjalan (mis. beda sesi Windows atau bila pipe gagal) dan `settings.json` bisa diedit tangan
  atau rusak (komentar `AppSettings.cs:124`, README).
- **Keputusan.** `Load` dan `Save` menangkap galat I/O/JSON dan mengembalikan bawaan/`false`; tipe salah pada satu properti
  membuat seluruh file jatuh ke bawaan. `Sanitize` membuang entri tak masuk akal (path kosong, duplikat, zoom di luar 50-300,
  indeks aktif di luar rentang). `SaveMerged` memuat ulang file lebih dulu dan menggabungkan `RecentFiles` (milik instance ini di
  depan, sisanya dari file, maksimal 10, tanpa membedakan huruf besar-kecil, melewatkan yang sengaja dihapus lewat `removedRecent`)
  supaya salinan usang tidak menimpa instance lain. Hanya `RecentFiles` yang digabung; tema/zoom/blokir gambar diambil dari instance
  yang menyimpan terakhir.
- **Konsekuensi.** File yang rusak ditimpa diam-diam oleh `SaveMerged` berikutnya (test `SaveMerged_CorruptStoredFile_IsReplacedByOwnSettings`).
  Tidak ada penguncian antar-proses: dua instance yang menyimpan bersamaan tetap bisa saling menimpa antara `Load` dan `Save`
  (inferensi dari kode, tidak diuji).
- **Bukti.** `AppSettings.cs:58-142`. Test: `AppSettingsTests` (40 test), `AppSettingsMergeTests`, `AppSettingsMergeEdgeTests`.

## ADR-16 Pembukaan dokumen hanya lewat `OpenFile`, dengan batas ukuran

- **Konteks.** Seluruh file dibaca ke memori lalu didekode (komentar `MainWindow.xaml.cs:18, 277`).
- **Keputusan.** `MainWindow.OpenFile` adalah satu-satunya pintu: `GetFullPath`, deteksi tab ganda (tab yang sudah ada dipilih), konfirmasi
  > 50 MB, tolak > 500 MB, tangkap `OutOfMemoryException` dan `IOException`/`UnauthorizedAccessException` dengan pesan ramah.
- **Konsekuensi.** `OpenFile` tidak memeriksa ekstensi; penyaringan ekstensi ada di seret-lepas, klik tautan, dan filter dialog
  (`MarkdownFiles.IsMarkdown` juga menyertakan `.txt`). Batas ukuran dan jalur OOM tidak diuji otomatis.
- **Bukti.** `MainWindow.xaml.cs:19-20, 236-304`.

## ADR-17 Klik tautan di pratinjau

- **Konteks.** Komentar `DocumentView.xaml.cs:670-671`: hanya skema aman yang diserahkan ke shell; tautan ke file lokal non-markdown
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
- **Bukti.** `MarkdownSupport.cs:361-412`, `DocumentView.xaml.cs:644-683`. Test: `LinkResolutionTests.cs` (resolusi path). Pembungkus
  `OnHyperlink` tidak punya test (hanya `RequestOpen` yang diuji, `DocumentTabTests.cs:274`).

## ADR-18 Skrip asosiasi file hanya HKCU

- **Konteks.** README: hanya menulis ke HKCU, tanpa hak administrator; Windows 10/11 melindungi pilihan aplikasi bawaan.
- **Keputusan.** `register-file-association.ps1`/`unregister-file-association.ps1` memakai `SupportsShouldProcess` (`-WhatIf`), menulis
  lewat `Registry.CurrentUser`, memvalidasi `-Extensions` dengan `'^\.[a-z0-9]+$'` (ekstensi dipakai sebagai nama subkey registri),
  dan `-SetDefault` mencadangkan nilai bawaan lama ke `HKCU\Software\MdViewer\PreviousDefault` yang dipulihkan skrip unregister.
  Unregister hanya menyentuh nilai bawaan yang persis menunjuk ke ProgID MdViewer dan tidak menyentuh `UserChoice`.
- **Konsekuensi.** Skrip tidak bisa memaksa MdViewer menjadi aplikasi bawaan. Tidak ada test otomatis untuk skrip (CLAUDE.md melarang
  menjalankan skrip registri sungguhan di test).
- **Bukti.** `scripts/register-file-association.ps1:37, 68-70, 81-140`, `scripts/unregister-file-association.ps1:18, 31-33, 52-102`.
