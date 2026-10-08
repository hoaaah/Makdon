# Dokumentasi pengembangan MdViewer

**Tujuan:** indeks dokumentasi pengembangan. **Pembaca:** pengembang, reviewer, dan agen AI yang bekerja di repo ini.
Dokumentasi pengguna (fitur, pintasan, build singkat) ada di [../README.md](../README.md); aturan kerja proyek di
[../CLAUDE.md](../CLAUDE.md); riwayat rilis di [../CHANGELOG.md](../CHANGELOG.md).

| Dokumen | Isi | Baca bila |
| --- | --- | --- |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Komponen dan tanggung jawab, diagram komponen, diagram urutan alur utama (startup, buka, render, simpan, perubahan eksternal, ekspor, sesi, Pratinjau Cetak, siklus hidup paket XPS dan galat pratinjau), model thread, model state (termasuk `PreviewBuild`) | Akan mengubah kode atau mencari "di mana X terjadi" |
| [DESIGN-DECISIONS.md](DESIGN-DECISIONS.md) | Catatan keputusan (konteks, keputusan, konsekuensi, bukti) | Ingin tahu *mengapa* suatu hal dibuat begitu sebelum mengubahnya |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Setup, build/test/publish, konvensi, cara menulis test, cara menambah tema/perintah/mode/ekstensi/format, checklist PR, larangan, sub-agent | Akan berkontribusi |
| [SECURITY.md](SECURITY.md) | Model ancaman, kontrol (lokasi kode + test), risiko residual dan yang sudah ditutup | Mengubah ekspor, pratinjau gambar, single-instance, skrip, atau menilai keamanan |
| [TESTING.md](TESTING.md) | Peta test, cara menjalankan, model STA/`WpfHost` (termasuk pencatat galat dispatcher), yang tidak teruji, checklist uji manual rilis (termasuk cetak) | Menjalankan/menulis test atau menyiapkan rilis |

## Konvensi dokumen

- Rujukan kode ditulis `path:baris` relatif terhadap akar repo; nomor baris sesuai kode saat dokumen ditulis dan bisa bergeser. Nama tipe/method
  adalah sumber yang stabil; cari dengan `grep`.
- Hal yang tidak bisa dibuktikan dari kode, test, atau README ditandai "belum diverifikasi" atau "dugaan".
- Dokumentasi berbahasa Indonesia, sama seperti teks UI dan komentar kode.
- Ubah dokumen ini bersama perubahan kode yang memengaruhinya (lihat checklist PR di [CONTRIBUTING.md](CONTRIBUTING.md#6-checklist-pr)).

## Ketidaksesuaian dokumen lama dan kode: sudah diselaraskan (2026-10-07)

Ketidaksesuaian yang dicatat saat dokumentasi ini disusun telah diperbaiki; tidak ada yang tersisa. Perincian per butir ada di
[DESIGN-DECISIONS.md](DESIGN-DECISIONS.md) dan [SECURITY.md](SECURITY.md).

- README (Lokasi data): `crash.log` kini ditulis "seluruh file dihapus bila > 512 KB, lalu entri baru ditulis" (`src/MdViewer/CrashLog.cs:38`).
- README (Batasan): "di atas 1 MB" diganti ambang karakter bertingkat (100 rb: parse latar; 200 rb: jeda render naik; 1 juta: jeda dan
  statistik lebih jarang) - `src/MdViewer/DocumentView.xaml.cs:22-29`.
- CLAUDE.md: aturan test kini "tidak boleh menyentuh/menulis `%APPDATA%`, `%LOCALAPPDATA%`, dan registri (membaca HKCU Personalize untuk
  tema diperbolehkan)". Empat test membaca nilai itu lewat `ThemeManager.SystemUsesLightTheme()` (satu test langsung, tiga test
  `ThemeManagerApplyTests` lewat `Apply`); tidak ada yang menulis. Lihat [CONTRIBUTING.md](CONTRIBUTING.md#42-aturan).
- `MdViewer.csproj` `<Version>` diubah dari `1.0.0` menjadi `0.1.0` agar cocok dengan CHANGELOG.
- README menyatakan bahwa `.txt` (dan `.mdown`, `.mkd`) juga diterima seret-lepas dan tautan relatif (`MarkdownFiles.IsMarkdown`),
  sedangkan asosiasi file hanya `.md`/`.markdown`.

## Pembaruan 2026-10-08: Pratinjau Cetak

Dokumen diperbarui untuk fitur Pratinjau Cetak (belum di-commit saat ditulis; 1455 kasus test hijau, 800 atribut `[Fact]`/`[Theory]` di 24 berkas):
komponen baru dan diagram urutan (ARCHITECTURE 4.9-4.10, 6.5), ADR-19 sampai ADR-26, kontrol dan risiko cetak (SECURITY 2.12, R8), peta test dan
infrastruktur test baru (TESTING), aturan test cetak (CONTRIBUTING 4.2 aturan 8-9). Nomor baris rujukan ke `MainWindow.xaml.cs`,
`DocumentView.xaml.cs`, dan `Support/WpfHost.cs` yang bergeser oleh perubahan ini sudah disesuaikan. Hal yang sengaja ditandai "belum diverifikasi":
bahwa `DocumentViewer` hanya menerima dokumen tetap (hanya dari komentar kode), bahwa mengubah `dialog.PrintTicket` dipakai saat mencetak,
dan kinerja/memori Pratinjau Cetak untuk dokumen sangat besar.
