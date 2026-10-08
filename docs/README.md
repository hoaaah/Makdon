# Dokumentasi pengembangan Makdon

**Tujuan:** indeks dokumentasi pengembangan. **Pembaca:** pengembang, reviewer, dan agen AI yang bekerja di repo ini.
Dokumentasi pengguna (fitur, pintasan, build singkat) ada di [../README.md](../README.md); aturan kerja proyek di
[../CLAUDE.md](../CLAUDE.md); riwayat rilis di [../CHANGELOG.md](../CHANGELOG.md).

| Dokumen | Isi | Baca bila |
| --- | --- | --- |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Komponen dan tanggung jawab, diagram komponen, diagram urutan alur utama (startup, buka, render, simpan, perubahan eksternal, ekspor, sesi, Pratinjau Cetak, siklus hidup paket XPS dan galat pratinjau), model thread, model state (termasuk `PreviewBuild`) | Akan mengubah kode atau mencari "di mana X terjadi" |
| [DESIGN-DECISIONS.md](DESIGN-DECISIONS.md) | Catatan keputusan (konteks, keputusan, konsekuensi, bukti) | Ingin tahu *mengapa* suatu hal dibuat begitu sebelum mengubahnya |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Setup, build/test/publish, konvensi, cara menulis test, cara menambah tema/perintah/mode/ekstensi/format, checklist PR, larangan, sub-agent | Akan berkontribusi |
| [SECURITY.md](SECURITY.md) | Model ancaman, kontrol (lokasi kode + test), risiko residual dan yang sudah ditutup | Mengubah ekspor, pratinjau gambar, single-instance, skrip, atau menilai keamanan |
| [DISTRIBUTION.md](DISTRIBUTION.md) | Keputusan dan implementasi distribusi: publish self-contained, installer Inno Setup, mode portable, pendaftaran "Buka dengan" (tabel registri), penandatanganan, pipeline rilis; penyimpangan dan temuan di bagian 13 | Mengerjakan rilis publik, installer, mode portable, atau asosiasi file |
| [TESTING.md](TESTING.md) | Peta test, cara menjalankan, model STA/`WpfHost` (termasuk pencatat galat dispatcher dan `TestApp`), yang tidak teruji, checklist uji manual rilis (termasuk cetak dan distribusi) | Menjalankan/menulis test atau menyiapkan rilis |

## Konvensi dokumen

- Rujukan kode ditulis `path:baris` relatif terhadap akar repo; nomor baris sesuai kode saat dokumen ditulis dan bisa bergeser. Nama tipe/method
  adalah sumber yang stabil; cari dengan `grep`.
- Hal yang tidak bisa dibuktikan dari kode, test, atau README ditandai "belum diverifikasi" atau "dugaan".
- Dokumentasi berbahasa Indonesia, sama seperti teks UI dan komentar kode.
- Ubah dokumen ini bersama perubahan kode yang memengaruhinya (lihat checklist PR di [CONTRIBUTING.md](CONTRIBUTING.md#6-checklist-pr)).

## Ketidaksesuaian dokumen lama dan kode: sudah diselaraskan (2026-10-07)

Ketidaksesuaian yang dicatat saat dokumentasi ini disusun telah diperbaiki; tidak ada yang tersisa. Perincian per butir ada di
[DESIGN-DECISIONS.md](DESIGN-DECISIONS.md) dan [SECURITY.md](SECURITY.md).

- README (Lokasi data): `crash.log` kini ditulis "seluruh file dihapus bila > 512 KB, lalu entri baru ditulis" (`src/Makdon/CrashLog.cs:37`).
- README (Batasan): "di atas 1 MB" diganti ambang karakter bertingkat (100 rb: parse latar; 200 rb: jeda render naik; 1 juta: jeda dan
  statistik lebih jarang) - `src/Makdon/DocumentView.xaml.cs:22-29`.
- CLAUDE.md: aturan test kini "tidak boleh menyentuh/menulis `%APPDATA%`, `%LOCALAPPDATA%`, dan registri (membaca HKCU Personalize untuk
  tema diperbolehkan)". Empat test membaca nilai itu lewat `ThemeManager.SystemUsesLightTheme()` (satu test langsung, tiga test
  `ThemeManagerApplyTests` lewat `Apply`); tidak ada yang menulis. Lihat [CONTRIBUTING.md](CONTRIBUTING.md#42-aturan).
- `Makdon.csproj` `<Version>` diubah dari `1.0.0` menjadi `0.1.0` agar cocok dengan CHANGELOG.
- README menyatakan bahwa `.txt` (dan `.mdown`, `.mkd`) juga diterima seret-lepas dan tautan relatif (`MarkdownFiles.IsMarkdown`),
  sedangkan asosiasi file hanya `.md`/`.markdown`.

## Pembaruan 2026-10-08: Pratinjau Cetak

Dokumen diperbarui untuk fitur Pratinjau Cetak (belum di-commit saat ditulis; 1455 kasus test hijau, 800 atribut `[Fact]`/`[Theory]` di 24 berkas):
komponen baru dan diagram urutan (ARCHITECTURE 4.9-4.10, 6.5), ADR-19 sampai ADR-26, kontrol dan risiko cetak (SECURITY 2.12, R8), peta test dan
infrastruktur test baru (TESTING), aturan test cetak (CONTRIBUTING 4.2 aturan 8-9). Nomor baris rujukan ke `MainWindow.xaml.cs`,
`DocumentView.xaml.cs`, dan `Support/WpfHost.cs` yang bergeser oleh perubahan ini sudah disesuaikan. Hal yang sengaja ditandai "belum diverifikasi":
bahwa `DocumentViewer` hanya menerima dokumen tetap (hanya dari komentar kode), bahwa mengubah `dialog.PrintTicket` dipakai saat mencetak,
dan kinerja/memori Pratinjau Cetak untuk dokumen sangat besar.

## Pembaruan 2026-10-08: distribusi dan .NET 10

Fitur distribusi diimplementasikan (belum di-commit; 1543 kasus test lulus): target `net10.0-windows`, profil publish `win-x64`,
mode portable (penanda `Makdon.portable`), "Buka dengan" (`FileAssociation`, menu Integrasi Explorer untuk portable), Tentang,
installer Inno Setup, `scripts/build-release.ps1`, dan `.github/workflows/release.yml`.

- Dokumen yang berubah: DISTRIBUTION (status diimplementasikan; penyimpangan di §13), ARCHITECTURE (komponen `AppPaths`, `AppInfo`,
  `InstallerMutex`, `RegistryStore`, `FileAssociation`; alur startup dan scope portable), DESIGN-DECISIONS (ADR-27 sampai ADR-33),
  SECURITY (2.13 instalasi/portable/registri; R14-R17; R12), TESTING (peta test untuk tiga berkas baru, `TestApp` dan `FakeRegistryStore`,
  checklist distribusi dan Pratinjau Cetak pada hasil publish), CONTRIBUTING (.NET 10 SDK, aturan 10-11, larangan, bagian 9 membuat rilis),
  README dan CLAUDE.md di akar, serta CHANGELOG (entri Unreleased).
- Rujukan `path:baris` yang bergeser karena perubahan di `MainWindow.xaml(.cs)`, `App.xaml.cs`, `AppSettings.cs`, `CrashLog.cs`,
  `Makdon.csproj`, dan `WpfHost.cs` dihitung ulang dari diff terhadap HEAD dengan skrip, lalu rujukan yang menunjuk baris yang berubah
  diperiksa satu per satu.
- Ditandai "belum diverifikasi": installer belum dikompilasi, `release.yml` belum dijalankan, pemasangan di Windows 10/11 belum diuji,
  perilaku SmartScreen/Smart App Control, immutable releases, `Indonesian.isl`, lisensi per paket pihak ketiga, sisa DLL saat upgrade,
  perilaku AppMutex oleh installer, dan manfaat ReadyToRun. Sejak review: SHA action CI sudah dicocokkan dengan `git ls-remote`; isi `THIRD-PARTY-NOTICES-WPF.txt` belum dibandingkan dengan tag-nya.
