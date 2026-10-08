# Distribusi: installer, portable, dan "Buka dengan"

**Status:** diimplementasikan (2026-10-08, belum di-commit). Keputusan di bawah tetap berlaku sebagai acuan; penyimpangan dan temuan
implementasi dicatat di bagian 13. Yang **belum** dibuktikan (installer belum pernah dikompilasi, `release.yml` belum pernah dijalankan,
pemasangan belum diuji di Windows 10/11) ditandai "belum diverifikasi". **Pembaca:** pengembang dan reviewer yang mengerjakan rilis publik.
Rujukan: arsitektur di [ARCHITECTURE.md](ARCHITECTURE.md), keamanan di [SECURITY.md](SECURITY.md), uji manual di [TESTING.md](TESTING.md#checklist-uji-manual-sebelum-rilis),
cara membuat rilis di [CONTRIBUTING.md](CONTRIBUTING.md#9-membuat-rilis).

## 1. Keputusan

| # | Hal | Keputusan |
| --- | --- | --- |
| 1 | Target pengguna | Publik |
| 2 | Runtime | Self-contained (pengguna tidak perlu memasang .NET) |
| 3 | Versi portable | Ya, di samping installer |
| 4 | Integrasi Explorer | Cukup masuk daftar **Buka dengan**; tanpa item level atas menu Windows 11 (jadi **tanpa MSIX / `IExplorerCommand`**) |
| 5 | Update | Manual: unduh installer/zip baru; aplikasi tidak memeriksa update |
| 6 | Hosting | GitHub Releases (`github.com/hoaaah/Makdon`) |
| 7 | Penandatanganan kode | Rilis tanpa tanda tangan dulu (tidak ada anggaran); mitigasi di §6 |
| 8 | Portable vs terpasang | Portable **menolak** mendaftar "Buka dengan" bila ada versi terpasang (§4.3) |
| 9 | Uninstall | Pengguna ditanya sebelum data pengguna dihapus (§5.3) |
| 10 | Windows minimum | Mengikuti stack: ditegakkan Windows 10 1607 (build 14393) x64; detail §2.2 |
| 11 | ARM64, winget | Tidak sekarang; masuk roadmap (§10) |
| 12 | Target framework | **.NET 10 (LTS, `net10.0-windows`)**; diterapkan. .NET 9 berhenti didukung 2026-11-10 (§11) |
| 13 | Lisensi proyek | **MIT** (§9) |

## 2. Artefak rilis dan platform

| Artefak | Isi | Untuk |
| --- | --- | --- |
| `Makdon-<versi>-setup-x64.exe` | Installer Inno Setup | Jalur utama pengguna umum |
| `Makdon-<versi>-portable-x64.zip` | Folder publish + penanda `Makdon.portable` | Tanpa instalasi, tanpa jejak di `%APPDATA%` |
| `SHA256SUMS.txt` | Checksum dua berkas di atas (format `sha256sum`) | Verifikasi unduhan |

Setiap artefak juga menyertakan `LICENSE`, `THIRD-PARTY-NOTICES.txt`, `THIRD-PARTY-NOTICES-DOTNET.txt` (salinan dari paket runtime), dan `THIRD-PARTY-NOTICES-WPF.txt` (§9).
Keluaran lokal dan CI ada di `artifacts/<versi>/` (di-`.gitignore`), disusun oleh `scripts/build-release.ps1`.

### 2.1 Bentuk publish

```powershell
dotnet publish src/Makdon -p:PublishProfile=win-x64
```

Profil `src/Makdon/Properties/PublishProfiles/win-x64.pubxml` berisi `RuntimeIdentifier=win-x64`, `SelfContained=true`,
`PublishSingleFile=false`, `PublishReadyToRun=false`, dan `PublishTrimmed=false`. Hasil: `src\Makdon\bin\Release\net10.0-windows\win-x64\publish\`.

- **Folder, bukan single-file.** Single-file WPF tetap mengekstrak pustaka native ke folder sementara. Dengan folder: tanpa jejak di `%TEMP%`
  (penting untuk portable), startup lebih cepat, lebih jarang dicurigai antivirus. `IncludeNativeLibrariesForSelfExtract` (hanya bermakna untuk
  single-file) sudah dihapus dari `Makdon.csproj`, bersama komentar publish framework-dependent.
- **Tanpa trimming** (WPF tidak mendukungnya).
- **ReadyToRun mati secara bawaan.** Assembly framework di runtime pack sudah R2R; manfaat untuk Makdon/AvalonEdit/Markdig diduga kecil.
  Belum diukur (**belum diverifikasi**): ukur startup dingin dulu sebelum menyalakan.
- **Ukuran nyata** (diukur 2026-10-08 pada publish lokal, runtime `Microsoft.NETCore.App` 10.0.12): folder publish **155,5 MB** terurai (258 berkas), zip portable **65,0 MB** (run `build-release.ps1` terakhir, 1543 test lulus). Perkiraan awal
  (150-200 MB / 60-80 MB) terbukti mendekati. Installer memakai `Compression=lzma2/ultra64` + `SolidCompression=yes`; ukuran installer belum diukur.
- Pengaturan publish disimpan di `src/Makdon/Properties/PublishProfiles/win-x64.pubxml` agar build lokal dan CI identik.
- Versi tampilan: `IncludeSourceRevisionInInformationalVersion=false` (`src/Makdon/Makdon.csproj:22`), supaya dialog Tentang menampilkan `0.1.0`, bukan `0.1.0+<hash>`.

### 2.2 Dukungan Windows

Menurut dokumentasi resmi .NET 10 (sama dengan .NET 9), Windows 11 23H2 ke atas didukung, sedangkan dukungan Windows 10 terbatas pada edisi
LTSC/Enterprise (1607, 1809, 21H2). Windows 10 22H2 konsumen sudah habis masa dukungannya (2025-10-14). Sumber:
[Install .NET on Windows](https://learn.microsoft.com/en-us/dotnet/core/install/windows),
[.NET 9 supported-os](https://github.com/dotnet/core/blob/main/release-notes/9.0/supported-os.md) (untuk .NET 10 belum diperiksa, **belum diverifikasi**).

| Tingkat | Windows |
| --- | --- |
| Didukung (diuji di perangkat: **belum diverifikasi**, rancangan) | Windows 11 23H2 ke atas, x64 (juga Windows 11 Arm64 lewat emulasi x64) |
| Berjalan, tanpa dukungan resmi .NET | Windows 10 22H2 x64; Windows 10 LTSC 2019/2021 |
| Ditolak installer | Di bawah Windows 10 1607 (build 14393), Windows 7/8.1, Windows 32-bit |

- Installer: `MinVersion=10.0.14393`, `ArchitecturesAllowed=x64compatible`, `ArchitecturesInstallIn64BitMode=x64compatible` (`installer/Makdon.iss:45-47`). Mode 64-bit wajib,
  karena tanpanya instalasi semua pengguna masuk `Program Files (x86)` dan kunci HKLM masuk `WOW6432Node`.
- Zip portable tidak bisa menegakkan versi: pada OS yang terlalu lama, runtime gagal sebelum kode aplikasi berjalan. Persyaratan OS cukup
  ditulis di README dan catatan rilis.

## 3. Mode terpasang vs portable

Satu exe yang sama; mode ditentukan saat startup oleh ada/tidaknya `Makdon.portable` di samping `Makdon.exe` (`dotnet run` = terpasang).
Implementasi: `AppPaths.Detect` (`src/Makdon/AppPaths.cs:60-65`); `AppPaths.Current` diisi dari `Environment.ProcessPath` bila namanya `Makdon.exe`
(`AppPaths.cs:67-75`).

| Hal | Terpasang | Portable |
| --- | --- | --- |
| `settings.json` | `%APPDATA%\Makdon\` (`AppPaths.cs:35-37`) | `<folder exe>\data\` (dibuat saat dibutuhkan) |
| `crash.log` | `%LOCALAPPDATA%\Makdon\` (`AppPaths.cs:39-41`) | `<folder exe>\data\` |
| Registri | Ditulis installer | Hanya bila pengguna memilih "Daftarkan" (§4.3) |
| Single-instance | Scope kosong (nama mutex/pipe bawaan) | Scope = hash path folder exe (`AppPaths.cs:47-55`, awalan `p`) |
| Mutex installer (§5.2) | Dibuat (`InstallerMutex.cs:12-23`) | Tidak dibuat |
| Pemeriksaan startup registri (§4.3) | Tidak ada | Ada, setelah jendela tampil (`MainWindow.xaml.cs:486-497`) |

- Bila folder portable tidak bisa ditulisi, aplikasi berjalan dengan pengaturan di memori dan memberi tahu pengguna sekali
  (`AppPaths.IsDataDirectoryWritable`, `AppPaths.cs:81-96`; pesan di `MainWindow.xaml.cs:499-`). Jangan pindah
  diam-diam ke `%APPDATA%`.
- **Scope single-instance portable** (diterapkan): sebelumnya nama mutex/pipe hanya memuat SID dan id sesi (`SingleInstance.cs:43-44`, `61-62`).
  Tanpa scope berbeda, berkas yang dibuka di portable akan diteruskan ke instance terpasang yang sedang berjalan, atau sebaliknya.
  Parameter `scope` di `SingleInstance.Create` sudah ada dan kini dipakai `App.xaml.cs:24`.
- **Dampak kode** (diterapkan): `AppPaths` dipakai `AppSettings.DefaultPath` (`src/Makdon/AppSettings.cs:59`) dan `CrashLog.LogPath`
  (`src/Makdon/CrashLog.cs:25`). Test menyuntikkan folder exe palsu (`AppPaths.Detect` dengan `TempDir`), jadi tetap tidak menyentuh
  `%APPDATA%`/`%LOCALAPPDATA%`.

## 4. Integrasi "Buka dengan"

### 4.1 Spesifikasi registri (satu sumber kebenaran)

Isinya sama dengan yang ditulis skrip. Installer, fitur aplikasi, dan skrip wajib mengikuti tabel ini dan diubah bersama. Implementasi:
`FileAssociation.WriteRegistration` (`src/Makdon/FileAssociation.cs:243-271`), `installer/Makdon.iss` bagian `[Registry]`, dan
`scripts/register-file-association.ps1`.
`<root>` = `HKCU\Software\Classes` (per pengguna) atau `HKLM\Software\Classes` (semua pengguna).

| Kunci | Nilai |
| --- | --- |
| `<root>\Makdon.Markdown` | `(Default)`/`FriendlyTypeName` = `Dokumen Markdown` |
| `<root>\Makdon.Markdown\DefaultIcon` | `"<exe>",0` |
| `<root>\Makdon.Markdown\shell\open` | `FriendlyAppName` = `Makdon`, `MUIVerb` = `Buka dengan Makdon` |
| `<root>\Makdon.Markdown\shell\open\command` | `"<exe>" "%1"` |
| `<root>\Applications\Makdon.exe` (+ `DefaultIcon`, `shell\open\command`, `SupportedTypes\.md`, `.markdown`) | Daftar "Buka dengan" |
| `<root>\.md\OpenWithProgids`, `<root>\.markdown\OpenWithProgids` | Nilai `Makdon.Markdown` (kosong) |
| `Software\Makdon\Capabilities` + `Software\RegisteredApplications` | Pengaturan > Aplikasi > Aplikasi bawaan |

**Tidak** menulis nilai bawaan ekstensi dan tidak menyentuh `UserChoice`; pengguna memilih sendiri lewat "Pilih aplikasi lain > Selalu gunakan".
`-SetDefault` hanya tersedia di skrip pengembangan.

Di Windows 11, Makdon muncul di submenu **Buka dengan** pada menu ringkas, di dialog "Pilih aplikasi lain", dan di Aplikasi bawaan. Item
tersendiri "Buka dengan Makdon" hanya ada di "Tampilkan opsi lainnya" (sudah diterima, keputusan #4). Perilaku ini **belum diverifikasi** di
Windows 11 karena installer belum diuji.

### 4.2 Oleh installer

Inno menulis tabel 4.1 dengan root `HKA` (HKCU untuk per pengguna, HKLM untuk semua pengguna) dan `ChangesAssociations=yes`, lalu
menghapusnya saat uninstall (`installer/Makdon.iss:86-113`):
- `.md\OpenWithProgids`: hanya nilai `Makdon.Markdown` yang dihapus, bukan kuncinya.
- `Software\Makdon`: `uninsdeletekeyifempty`, karena skrip pengembangan memakai `Software\Makdon\PreviousDefault`.

### 4.3 Oleh aplikasi (mode portable)

Menu yang hanya tampil di mode portable: **Berkas > Integrasi Explorer > Daftarkan ke "Buka dengan" / Cabut pendaftaran**
(`MainWindow.xaml`, item `ExplorerIntegrationItem`; penanganan di `MainWindow.xaml.cs:550-620`). Logika di kelas `FileAssociation` lewat antarmuka
`IRegistryStore` (`src/Makdon/RegistryStore.cs:11-32`; implementasi nyata `WindowsRegistryStore`, view 64-bit), supaya test memakai
registri palsu di memori (`Support/FakeRegistryStore.cs`) dan aturan "test tidak menulis registri" tetap berlaku.

Menu **Bantuan > Tentang Makdon...** juga ada di kedua mode: menampilkan versi, mode, lisensi, dan tautan halaman rilis
(`MainWindow.xaml.cs:621-`). Makdon tidak menghubungi internet sendiri.

**Deteksi instalasi** (`FileAssociation.FindInstallation`, `FileAssociation.cs:106-125`). Baca
`Software\Microsoft\Windows\CurrentVersion\Uninstall\{<AppId>}_is1` di HKCU dan HKLM (view 64-bit, plus `WOW6432Node` untuk berjaga-jaga).
Ambil nilai `Inno Setup: App Path` (cadangan: `InstallLocation`). Instalasi dianggap ada bila kunci ada **dan** `<path>\Makdon.exe` ada.
`AppId` menjadi konstanta di kode aplikasi (`AppInfo.InstallerAppId`, `src/Makdon/AppInfo.cs:9`) dan di `installer/Makdon.iss`.

**Aturan saat "Daftarkan"** (`FileAssociation.Register`, `FileAssociation.cs:189-210`; klasifikasi di `GetStatus`/`Classify`, `FileAssociation.cs:127-175`):
1. Ada instalasi (HKCU maupun HKLM) → **tolak** (`BlockedByInstallation`), dengan pesan yang menyarankan memakai versi terpasang atau meng-uninstall-nya dulu.
   Instalasi semua pengguna juga ditolak, karena kunci HKCU portable akan menutupi kunci HKLM dalam gabungan HKCR.
2. Tidak ada instalasi → baca perintah di `HKCU\...\Makdon.Markdown\shell\open\command` dan `Applications\Makdon.exe\shell\open\command`
   (token pertama yang dikutip, `Path.GetFullPath`, banding `OrdinalIgnoreCase`; `ExtractExePath`, `FileAssociation.cs:280-305`):
   - kosong, atau exe yang dirujuk sudah tidak ada (path basi) → tulis;
   - sama dengan exe ini → sudah terdaftar (`AlreadyRegistered`), menu menampilkan "Cabut";
   - exe portable lain yang masih ada (ada penanda) → tanya dulu sebelum mengganti (`NeedsConfirmationOtherPortable`);
   - exe lain tanpa penanda (mis. hasil skrip pengembangan) → tanya dengan peringatan (`NeedsConfirmationOtherExe`).
3. Perbandingan prefix folder selalu dengan pemisah di akhir (`FileAssociation.IsUnderFolder`, `FileAssociation.cs:315-319`): `C:\X\Makdon\` ≠ `C:\X\Makdon2\`.
4. Perintah relatif (mis. `"Makdon.exe" "%1"` tanpa path penuh) tidak dinormalkan terhadap folder kerja, karena exe-nya tidak bisa dipastikan. Perintah itu diklasifikasi `OtherExe` (perlu konfirmasi, tidak pernah ditawari pembaruan, dan "Cabut" tidak menghapusnya). Instalasi dengan path relatif diabaikan (`ClassifyCommand`, `FileAssociation.cs:155-162`).
5. Path jaringan (UNC `\\server\share\...` dan `\\?\UNC\...`) tidak diperiksa dengan `File.Exists`, karena bisa memblokir thread UI. Pendaftaran seperti itu diklasifikasi `OtherExe` (bukan basi), sehingga startup tidak menawarkan pembaruan, dan instalasi di UNC diabaikan. `\\?\C:\...` tetap dianggap lokal (`IsNetworkPath`, `FileAssociation.cs:308-310`).
6. `Register` menolak dengan `RegisterResult.ExeNotFound` bila berkas exe bukan bernama `Makdon.exe` atau tidak ada di path-nya, karena Windows mencocokkan kunci `Applications\Makdon.exe` lewat nama berkas. Tidak ada yang ditulis, dan MainWindow menampilkan pesan (`FileAssociation.cs:193-195`).

**"Cabut"** (`FileAssociation.Unregister`, `FileAssociation.cs:213-237`) hanya menghapus kunci yang perintahnya menunjuk exe ini.
Kunci `Software\Makdon` dibuang hanya bila sudah kosong.

**Pemeriksaan saat startup (portable)** (`FileAssociation.CheckStartup`, `FileAssociation.cs:178-184`; dijalankan `MainWindow.RunPortableStartupChecks`, `MainWindow.xaml.cs:499-`, sesudah `ContentRendered` di prioritas `ApplicationIdle`):
- HKCU menunjuk exe yang sudah tidak ada → tawarkan memperbarui path. *Penyimpangan kecil:* kode menganggap **setiap** exe yang tidak ada sebagai basi, tidak hanya portable yang hilang.
- Ada instalasi **dan** HKCU menunjuk exe ini (portable didaftarkan sebelum installer semua pengguna dipasang) → tawarkan "Cabut pendaftaran
  portable". Installer admin tidak bisa membersihkan HKCU pengguna secara andal.
  *Dugaan, belum diverifikasi:* `GetStatus` memilih kondisi yang paling "asing" di antara dua kunci (ProgID dan Applications). Bila ProgID menunjuk
  exe ini tetapi `Applications` menunjuk exe lain, status bukan `ThisExe` dan tawaran pencabutan tidak muncul.

### 4.4 Skrip

`scripts/register-file-association.ps1` dan `unregister-file-association.ps1` tetap ada sebagai alat pengembangan; jalur bawaan `-ExePath`
diperbarui ke folder publish `net10.0-windows`. README menjelaskan migrasi untuk yang pernah memakai skrip: jalankan `unregister-file-association.ps1`
sebelum memasang dengan installer.

## 5. Installer (Inno Setup 6)

Berkas `installer/Makdon.iss`, Inno Setup 6.7.x (versi disematkan di CI). WiX/MSI baru dipertimbangkan bila ada kebutuhan deployment korporat.
Dipanggil dari `scripts/build-release.ps1` dengan `/DAppVersion`, `/DPublishDir`, dan `/DOutputDir`. Versi wajib sama dengan `<Version>`.
`installer/Makdon.iss` juga menolak dikompilasi bila `PublishDir` memuat `Makdon.portable` (`#if FileExists`).

### 5.1 Dasar

- `PrivilegesRequired=lowest` + `PrivilegesRequiredOverridesAllowed=dialog`: bawaan **per pengguna** ke `%LOCALAPPDATA%\Programs\Makdon`
  tanpa UAC; opsi "semua pengguna" ke `Program Files` (meminta admin). Catatan di `.iss`: folder program per pengguna berbeda dari folder data
  `%LOCALAPPDATA%\Makdon`.
- `AppId={{2025C09D-1945-431D-BEBF-18C8EC3A9A78}` (kurung kurawal pertama di-escape; kunci uninstall menjadi `{2025C09D-...}_is1`). **Tidak boleh berubah selamanya**:
  GUID ini identitas upgrade/uninstall dan dipakai aplikasi untuk deteksi instalasi (§4.3). Nilainya dijaga sama di `installer/Makdon.iss:34`,
  `AppInfo.cs:9`, dan `FileAssociation.cs:78-79`.
- Versi dari `<Version>` di `Makdon.csproj` lewat `iscc /DAppVersion=...`.
- `UsePreviousAppDir=yes`, `DisableDirPage=auto`. Pintasan Start Menu; ikon desktop opsional (tidak dicentang).
- Bahasa installer: Indonesia dan Inggris. `Indonesian.isl` **bukan bawaan** Inno Setup: salinan dari folder terjemahan tidak resmi
  (Unofficial issrc, penulis "MozaikTM", diperbarui untuk 6.5+) disimpan di `installer/Languages/Indonesian.isl`. **Belum diverifikasi**:
  kelengkapan string dan kualitas terjemahannya; belum pernah dikompilasi.
- `LicenseFile=..\LICENSE` (MIT, §9).

### 5.2 Upgrade, downgrade, aplikasi yang sedang berjalan

- **Upgrade:** installer baru menimpa berkas lewat `[Files] ... ignoreversion recursesubdirs` (`installer/Makdon.iss:80`). **Tidak memakai `[InstallDelete]` dengan
  wildcard**, karena bila pengguna memilih folder bersama (mis. `C:\Tools`), berkas lain ikut terhapus. Uninstaller Inno menggabungkan log
  berkas lintas versi, jadi semuanya tetap terhapus saat uninstall. DLL sisa versi lama diduga tidak berbahaya untuk aplikasi self-contained
  (yang dimuat mengikuti `deps.json`); **belum diverifikasi**: dibuktikan lewat uji manual upgrade.
- **Downgrade:** ditolak di `InitializeSetup` (`installer/Makdon.iss:144-156`) dengan membandingkan `DisplayVersion` kunci `_is1` (HKCU dan HKLM).
  Versi dibersihkan dari akhiran SemVer (`CleanVersion`), lalu `StrToVersion`.
- **Pindah mode** (per pengguna ↔ semua pengguna) masuk daftar uji manual, karena bisa menghasilkan dua instalasi dengan AppId sama.
- **Aplikasi sedang berjalan:** `AppMutex=Makdon.AppMutex,Global\Makdon.AppMutex` (`installer/Makdon.iss:52`) menjadi jalur utama. Setup maupun uninstaller memeriksanya
  lalu meminta pengguna menutup Makdon, sehingga dokumen yang belum disimpan tetap ditangani dialog simpan aplikasi.
  - Aplikasi membuat kedua mutex bernama tetap ini (hanya dalam mode terpasang, di luar `SingleInstance`; `InstallerMutex.cs`) dan mengabaikan galat saat membuatnya.
    Mutex dipegang sepanjang proses. **Belum diverifikasi**: perilaku pemeriksaan AppMutex oleh installer.
  - `AppMutex` lama tidak bisa dipakai karena nama mutex single-instance memuat id pengguna (`SingleInstance.cs:43`, `Local\Makdon.SingleInstance.<SID>`).
  - Restart Manager (`CloseApplications`) bukan jalur utama: Makdon tidak menangani penutupan oleh Restart Manager, dan `Window_Closing`
    bisa membatalkan penutupan (`src/Makdon/MainWindow.xaml.cs:876`).

### 5.3 Uninstall dan data pengguna

`CurUninstallStepChanged` pada `usPostUninstall` (`installer/Makdon.iss:158-185`):

| Kondisi | Perilaku |
| --- | --- |
| Instalasi per pengguna, interaktif | Tanya "Hapus juga pengaturan dan catatan galat Makdon?" (bawaan **Tidak**). Ya → hapus `%APPDATA%\Makdon` dan `%LOCALAPPDATA%\Makdon` |
| Instalasi semua pengguna (admin) | **Tidak bertanya dan tidak menghapus.** Konstanta `{userappdata}`/`{localappdata}` mengacu ke akun admin yang elevated, yang bisa berbeda dari pengguna yang login. Cukup informasikan bahwa pengaturan tiap pengguna tetap ada. Jangan pernah menelusuri semua profil |
| `/SILENT`, `/VERYSILENT` | Tidak bertanya, tidak menghapus |
| Upgrade | Uninstaller tidak berjalan, jadi tidak ada pertanyaan |
| Data portable (`<folder>\data\`) | Tidak pernah disentuh uninstaller |

Teks pertanyaan lewat `[CustomMessages]` (id, en). `AppMutex` mencegah aplikasi yang masih berjalan menulis ulang `settings.json`
(`Window_Closed`, `src/Makdon/MainWindow.xaml.cs:890`) setelah folder data dihapus.

## 6. Rilis tanpa tanda tangan: mitigasi

Tanpa tanda tangan, pengguna akan melihat peringatan SmartScreen ("Windows melindungi PC Anda") dan rilis lebih mungkin ditandai antivirus.
Mitigasi:

1. **Checksum:** `SHA256SUMS.txt` sebagai aset (dibuat `scripts/build-release.ps1`, langkah 7), dan hash yang sama ditulis di badan catatan
   rilis (`release.yml`) beserta perintah verifikasi:
   `(Get-FileHash .\Makdon-x.y.z-setup-x64.exe -Algorithm SHA256).Hash`.
2. **Immutable releases** perlu diaktifkan di pengaturan repo: aset rilis tidak bisa diganti setelah dipublikasikan dan otomatis mendapat release attestation
   (`gh release verify-asset`). Pipeline mengunggah ke draft dulu, baru mempublikasikan. **Belum diverifikasi**: pengaturan repo belum diperiksa, dan tidak ada langkah di repo yang memastikannya.
3. **Artifact attestations** (`actions/attest-build-provenance`) untuk membuktikan artefak dibangun workflow repo ini dari commit tertentu.
   Ini pelengkap, **bukan pengganti tanda tangan**: tidak berpengaruh pada SmartScreen/antivirus. Gratis hanya untuk repo publik.
4. **Petunjuk di README / catatan rilis** (sudah ditulis di [README](../README.md#peringatan-smartscreen-dan-smart-app-control)):
   - Installer (jalur utama): klik "Info selengkapnya > Tetap jalankan". Berkas yang dipasang installer tidak membawa Mark of the Web, jadi
     `Makdon.exe` terpasang tidak memicu peringatan lagi. **Belum diverifikasi** pada Windows nyata.
   - Zip portable: ekstraksi Explorer menyebarkan Mark of the Web ke semua berkas. Urutan yang disarankan: verifikasi checksum dulu, **baru**
     klik kanan zip > Properti > Buka blokir (atau `Unblock-File`), lalu ekstrak. Atau terima peringatan SmartScreen saat menjalankan.
   - **Smart App Control** (Windows 11, aktif hanya pada instalasi bersih): aplikasi tak bertanda tangan bisa diblokir tanpa tombol
     "Tetap jalankan". Dijelaskan terus terang di README bahwa Makdon belum bisa dipakai di PC tersebut sampai ada rilis bertanda tangan.
     **Belum diverifikasi** untuk DLL tak bertanda tangan di dalam folder self-contained.
5. **False positive antivirus:** ajukan ke portal pengiriman berkas Microsoft (WDSI) untuk setiap rilis yang ditandai.

Penandatanganan dipindahkan ke roadmap (§10).

## 7. Rilis, update, dan masa dukungan runtime

- Saluran: GitHub Releases berisi installer, zip portable, `SHA256SUMS.txt`, dan catatan rilis. Catatan di `release.yml` saat ini hanya memuat
  checksum, belum isi `CHANGELOG.md` (**belum diverifikasi** lewat rilis sungguhan).
- Update: unduh lalu jalankan installer baru (upgrade di tempat), atau timpa folder portable (folder `data\` dan `Makdon.portable` dipertahankan;
  dijelaskan di README).
- Aplikasi **tidak** menghubungi internet. Dialog Tentang menampilkan versi dan tautan halaman rilis (`MainWindow.xaml.cs:621-`).
- Penomoran SemVer; satu sumber di `Makdon.csproj` (`<Version>`, `Makdon.csproj:12`); tag git `v<versi>`.
- **Kebijakan runtime:** karena runtime .NET ikut dikemas, pengguna menjalankan runtime yang dibundel sampai mereka memperbarui Makdon.
  Rilis ulang (patch) dibuat bila .NET mengeluarkan patch keamanan yang relevan untuk WPF/runtime.

## 8. Pipeline rilis (GitHub Actions)

Workflow `.github/workflows/release.yml`, dipicu tag `v*`, runner `windows-latest`, izin `contents: write`, `id-token: write`,
`attestations: write`. Langkah yang sama bisa dijalankan lokal lewat `scripts/build-release.ps1` (lihat [CONTRIBUTING.md](CONTRIBUTING.md#9-membuat-rilis)).

1. Gagal bila tag ≠ `v<Version>` di `Makdon.csproj` (langkah "Tag harus sama dengan Version").
2. `dotnet build` (0 warning) + `dotnet test` (semua hijau). Test WPF (`WpfHost`, STA) perlu dicoba di runner; test cetak sudah tanpa printer.
   **Belum diverifikasi**: test WPF di runner `windows-latest`.
3. `dotnet publish` dengan profil `win-x64`.
4. Pasang Inno Setup dengan versi disematkan (`INNO_SETUP_VERSION: '6.7.1'`, lewat Chocolatey), cari `ISCC.exe` lalu bangun installer.
   Paket Chocolatey `innosetup` versi 6.7.1 terverifikasi ada. Workflow memasangnya sendiri, jadi image runner tidak menentukan. Installer Inno Setup itu sendiri tidak diverifikasi checksum-nya (**belum diverifikasi**).
5. Zip portable: folder publish + `Makdon.portable` + lisensi. Folder `data\` tidak disertakan (dibuat aplikasi).
6. Pemeriksaan otomatis: `Makdon.portable` **tidak** boleh ada di isi installer (`build-release.ps1 -VerifyInstallerContents`: pasang sementara lalu copot, dan menulis HKCU sementara). Skrip hanya menjalankannya di CI (`GITHUB_ACTIONS=true`) atau dengan `-Force`, dan menolak bila kunci uninstall Makdon (`_is1`) sudah ada di HKCU atau HKLM.
7. `SHA256SUMS.txt` → attestation → **draft release** → unggah aset → publikasikan (immutable).

Keluaran lokal ke `artifacts/<versi>/` (di-`.gitignore`).

Catatan CI: action disematkan ke SHA commit dengan komentar versi: `actions/checkout` v4.4.0 (`11d5960a…`), `actions/setup-dotnet` v4.3.1 (`67a3573c…`), dan `actions/attest-build-provenance` v2.4.0 (`e8998f94…`). Nilai SHA sudah dicocokkan dengan `git ls-remote`. Inno Setup dipasang lewat Chocolatey versi 6.7.1 (terverifikasi ada), tanpa verifikasi checksum installer (**belum diverifikasi**).

## 9. Lisensi

- Proyek: **MIT** (`LICENSE`, pemegang hak cipta Arief Wijaya, 2026). Disalin ke repo, folder publish, installer (`LicenseFile`), dan zip.
- `THIRD-PARTY-NOTICES.txt` di repo memuat pemberitahuan untuk: .NET runtime/WPF (MIT), AvalonEdit 6.3.1.120 (MIT), Markdig 0.22.0 (BSD-2-Clause),
  dan Markdig.Wpf 0.5.0.1 (MIT). Versi Markdig 0.22.0 cocok dengan `obj/project.assets.json` (diperiksa 2026-10-08).
- **Belum diverifikasi:** teks lisensi AvalonEdit tidak dibandingkan dengan repo proyek (paket NuGet tidak membawa berkas LICENSE), dan
  isi lisensi Markdig, Markdig.Wpf, serta hak cipta tiap paket dari metadata `.nuspec`, bukan dari berkas di paket.
- `THIRD-PARTY-NOTICES-DOTNET.txt` disalin dari paket runtime (`microsoft.netcore.app.runtime.win-x64`) oleh `build-release.ps1`; skrip gagal bila berkas itu tidak ada.
- `THIRD-PARTY-NOTICES-WPF.txt` (di root repo; diambil dari dotnet/wpf tag v10.0.12) disalin ke folder rilis, karena paket runtime tidak memuat pemberitahuan WPF. Skrip gagal bila berkas itu tidak ada. **Belum diverifikasi:** isinya belum dibandingkan dengan berkas di tag itu.

## 10. Roadmap (di luar rilis pertama)

| Butir | Catatan |
| --- | --- |
| Penandatanganan kode | Saat ada anggaran: Azure Artifact Signing (kelayakan untuk individu di Indonesia belum diverifikasi) atau sertifikat OV. Tanda tangani exe, installer, uninstaller (`SignTool` Inno) |
| Build `win-arm64` native | Tambah artefak kedua; Inno `ArchitecturesAllowed` diperluas. Sementara itu Arm64 memakai emulasi x64 |
| winget | Ajukan manifest setelah rilis stabil. Installer sudah aman untuk `/VERYSILENT` (§5.3) |
| MSIX / item level atas menu Windows 11 | Hanya bila kebutuhan #4 berubah |
| Pemeriksaan ulang SHA action | Saat action diperbarui: cocokkan SHA baru dengan tag-nya lewat `git ls-remote` (§8) |

## 11. Keputusan tambahan (diputuskan 2026-10-08)

1. **Pindah ke .NET 10 (LTS) sebelum rilis publik pertama: disetujui dan diterapkan.** .NET 9 berhenti didukung pada **2026-11-10**, sedangkan
   .NET 10 didukung sampai November 2028. Pada aplikasi self-contained, runtime tanpa patch keamanan ikut terkirim ke pengguna. Dukungan OS
   .NET 10 sama dengan §2.2. Dampak: `TargetFramework` → `net10.0-windows` (`src/Makdon/Makdon.csproj:5`, `src/Makdon.Tests/Makdon.Tests.csproj:4`),
   jalur publish/skrip/dokumen diperbarui, dan seluruh test dijalankan ulang (1543 kasus lulus). Sumber: [.NET 8 & 9 end of support](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/).
2. **Lisensi proyek: MIT** (sejalan dengan dependensi), pemegang hak cipta Arief Wijaya.

## 12. Ringkasan dampak dan urutan kerja

| Area | Perubahan (status) |
| --- | --- |
| Kode aplikasi | `AppPaths`, `AppInfo`, `InstallerMutex`, `RegistryStore` (`IRegistryStore`), `FileAssociation`; `AppSettings`/`CrashLog` memakai `AppPaths`; scope single-instance portable; menu Integrasi Explorer dan pemeriksaan startup (portable); dialog Tentang. **Diterapkan.** |
| Proyek | `net10.0-windows`; profil publish `win-x64`; `IncludeNativeLibrariesForSelfExtract` dihapus; `IncludeSourceRevisionInInformationalVersion=false`; switch XPS (§13). **Diterapkan.** |
| Baru | `installer/Makdon.iss`, `installer/Languages/Indonesian.isl`, `scripts/build-release.ps1`, `.github/workflows/release.yml`, `LICENSE`, `THIRD-PARTY-NOTICES.txt`, `.gitignore` untuk `artifacts/`. **Diterapkan; installer belum dikompilasi.** |
| Test | `AppPathsTests.cs` (`AppPaths`, `InstallerMutexTests`), `FileAssociationTests.cs` dengan `Support/FakeRegistryStore.cs`, `WpfHostStartupTests.cs` (TestApp, switch XPS). **Diterapkan.** |
| Dokumen | README, ARCHITECTURE, SECURITY, TESTING, ADR, CONTRIBUTING, CHANGELOG. **Diterapkan (2026-10-08).** |

Urutan kerja (keputusan awal, dicatat untuk jejak): 1) migrasi .NET 10 dan lisensi; 2) profil publish + `AppPaths` + scope single-instance;
3) `FileAssociation`; 4) installer + `AppMutex`; 5) pipeline rilis; 6) dokumen dan uji manual di Windows 10/11 (uji manual **belum dilakukan**).

## 13. Penyimpangan dan temuan implementasi (2026-10-08)

Dicatat agar reviewer tidak menganggap rancangan di atas sebagai kenyataan yang sudah teruji.

- **Switch XPS (.NET 10).** .NET 10 memperkenalkan pembatasan batas paket XPS: font halaman XPS hanya boleh dimuat dari paket yang sama. Pada paket
  XPS di memori (Pratinjau Cetak, `PreviewBuild`) pemeriksaan itu juga menolak font milik paket sendiri, sehingga setiap halaman bertekst gagal dan
  Pratinjau Cetak selalu gagal. Perbaikan: `RuntimeHostConfigurationOption Switch.System.Windows.DisableXpsPackageBoundaryRestriction=true` di
  `src/Makdon/Makdon.csproj:41` dan `src/Makdon.Tests/Makdon.Tests.csproj:26`. Harus lewat runtimeconfig: `AppContext.SetSwitch` di kode tidak
  cukup karena WPF men-cache switch saat gambar pertama dimuat. Alasan aman: Makdon hanya membaca XPS buatannya sendiri. Alternatif yang
  tidak dipilih: menulis XPS ke berkas sementara. Risiko residual: lihat [SECURITY.md](SECURITY.md) R14.
- **Test host WPF (`WpfHost`).** Sebelumnya `WpfHost` membuat `App` biasa dan memanggil `InitializeComponent`. Konstruktor `Application` menitipkan
  pemanggilan `OnStartup` ke dispatcher, sehingga startup sungguhan ikut berjalan di test: mutex/pipe single-instance produksi, `MainWindow`, dan
  pembacaan settings `%APPDATA%`. Perbaikan: `WpfHost.TestApp` dengan `OnStartup` kosong dan `LoadAppXaml()` yang membaca BAML `app.xaml`
  langsung (`src/Makdon.Tests/Support/WpfHost.cs`, kelas `TestApp`). Dijaga oleh `WpfHostStartupTests`.
- **Ukuran nyata** (lihat §2.1): 155,5 MB folder publish (258 berkas), 65,0 MB zip portable. Ukuran installer belum diukur. Efek ReadyToRun belum diukur.
- **Installer belum pernah dikompilasi.** Inno Setup tidak terpasang di mesin pengembang. Semua klaim tentang perilaku installer (AppMutex,
  dialog uninstall, `InitializeSetup`, pemasangan per pengguna/semua pengguna) adalah **rancangan yang belum diverifikasi**.
- **`release.yml` belum pernah dijalankan.** Termasuk test WPF di runner, pemasangan Inno Setup via Chocolatey, dan langkah draft/publish.
- **Penyimpangan klasifikasi `Stale`** (§4.3): setiap exe yang tidak ada dianggap basi, bukan hanya portable.
- **Edge case status pendaftaran** (§4.3, dugaan): ProgID dan Applications bisa menunjuk exe berbeda; status "paling asing" menutupi
  tawaran pencabutan.
- **Indonesian.isl** dari salinan tidak resmi (lihat §5.1): belum diverifikasi kelengkapannya.
- **Versi Markdig** di `THIRD-PARTY-NOTICES.txt` sudah cocok dengan paket terpasang (0.22.0); isi lisensi per paket belum diverifikasi (§9).
- **`build-release.ps1 -VerifyInstallerContents`** menulis HKCU sementara saat memasang dan mencopot installer. Skrip menolaknya di luar CI tanpa `-Force`, dan menolak bila instalasi Makdon sudah ada (`scripts/build-release.ps1:174-188`).
