# Makdon

**English** | [Bahasa Indonesia](README.id.md)

Markdown editor and preview for Windows (WPF, .NET 10). Open `.md` files, edit on the left, and see the result live on the right.

## Features

- Three view modes: Editor, `Terpisah` (Split: editor + preview with synchronized scrolling), and `Pratinjau` (Preview).
- Multiple tabs, sessions restored when reopened, a recent files list, and drag-and-drop of files into the window.
- Markdown syntax highlighting, theme `Terang` (Light) / `Gelap` (Dark) / `Ikuti Sistem` (Follow System), and zoom (Ctrl+mouse wheel).
- Find and replace (plain text or regex) with result markers; **Ganti Semua** (Replace All) is a single Undo step.
- Format toolbar and shortcuts: bold, italic, inline code, heading, list, quote, link, image.
- Relative links to other Markdown files and `#anchor` links to headings (GitHub-style slugs) work in the preview. Files accepted by drag-and-drop and relative links: `.md`, `.markdown`, `.mdown`, `.mkd`, and `.txt` (file association only registers `.md` and `.markdown`). Links to UNC paths (`\\host\...`) and non-Markdown files are not opened.
- Encoding is preserved on save: UTF-8 (with/without BOM), UTF-16, UTF-32, Windows-1252.
  Files containing invalid bytes are flagged and require confirmation before saving.
- Atomic save (temporary file, then replace). If the file was changed by another program since it was opened, saving does not overwrite it silently. The dialog offers **Timpa** (Overwrite: save the editor's version), **Muat dari Disk** (Load from Disk: replace the editor content with the version on disk), or **Batal** (Cancel). Load from Disk is a single Undo step (Ctrl+Z restores the editor content), and the file is re-read after the dialog closes, not using the content from when the conflict was detected.
- External change detection: clean tabs are reloaded automatically (this can be undone); tabs with changes ask you one by one: **Muat dari Disk** (Load from Disk) or **Pertahankan Editor** (Keep Editor). Conflict dialogs never stack: other conflicts (including those from saving) wait their turn.
- Export to a standalone HTML file.
- Print (Ctrl+P) and **Print Preview** (`Pratinjau Cetak`; Ctrl+Shift+P, menu `Berkas` (File) > `Pratinjau Cetak...`, or the toolbar button). Print Preview opens a modal window with the pages as they will print: A4 or Letter paper, portrait or landscape, margins `Sempit` (Narrow, 0.5"), `Normal` (Normal, 0.75", default), or `Lebar` (Wide, 1"), and a footer with the file name on the left and "Halaman X dari N" (Page X of N) on the right (can be turned off; the footer sits inside the bottom margin so it does not shift the content). There is page navigation (first/previous/next/last, type a number and press Enter) and zoom (`Satu halaman` (One page), `Lebar halaman` (Page width), 100%, +/- buttons, Ctrl+wheel). Pages are built gradually (pagination and page writing do not block the UI for ordinary documents), with an indicator "Menyusun halaman..." (Building pages...). The content is a copy of the tab's text taken when the preview opens: editing the tab afterwards does not change an open preview. Print output always has a white background with the Light theme, even when the app is in Dark theme. The **Cetak...** (Print...) button in the preview window prints the same pages that are shown. If the paper or orientation chosen in the Print dialog differs from the preview, you are asked: "Cetak sesuai pratinjau" (Print as previewed) or "Batal" (Cancel), because the preview pages have a fixed size.
- One instance per user per Windows session: opening a `.md` file while Makdon is already running (in the same session) opens it as a tab in the existing window. Other Windows sessions (e.g., Remote Desktop) have their own instance.

### Security

- HTML export: raw HTML inside Markdown is escaped; links are allowed only for `http`, `https`, `mailto`, `#anchor`, or relative paths; images only for `http(s)`, `data:image/(png|jpeg|gif|webp)`, or local images.
  - Local png/jpg/gif/webp images up to 2 MB each are embedded as data URIs (machine paths never appear in the HTML), **only if they are under the document's folder**. Images outside it (absolute paths to other folders, `../`, symlinks pointing outside) are replaced with the text `[gambar di luar folder dokumen tidak disertakan]` ("[image outside the document folder not included]"), so other files on your machine are not exported. Unsaved documents (with no folder) embed no local images.
  - There is a total embedding budget of 30 MB per export (each occurrence of an image counts, because it adds to the HTML size). Once it is used up, later images are left as relative paths. The same image is read and encoded only once. If memory still runs out, the export is cancelled with a message instead of closing the app.
- Preview, print, and Print Preview load only local files and `http(s)`. Remote images (`http`/`https`) are replaced with the text `[gambar remote diblokir]` ("[remote image blocked]") by default, so a document cannot track you; enable them via `Tampilan` (View) > `Muat gambar remote` (Load remote images). Images from UNC shares (`\\host\...`, `file://host/...`) and other schemes (`ftp:`, etc.) are always blocked, without any network connection. `data:` images are not shown in the preview (they are replaced with a marker) because WPF does not support them, but they are still included in the HTML export if their type is valid (png/jpeg/gif/webp).
- Print documents contain no local paths: the footer holds only the file name, and if a document fails to render, the failure is shown on screen (a friendly message, with details in `crash.log`), not printed as an error page.

## Installation

There are two ways. Both use the same program and are built with the bundled .NET runtime, so .NET does not need to be installed separately.

| Method | File | Suited for |
| --- | --- | --- |
| **Installer** | `Makdon-<versi>-setup-x64.exe` | Everyday use. Installed in the Start Menu, registered for "Open with", and can be uninstalled from `Pengaturan` (Settings). |
| **Portable** | `Makdon-<versi>-portable-x64.zip` | No installation. All data (settings, error log) is stored in the `data` folder next to `Makdon.exe`. |

The installer installs **per user** by default (to `%LOCALAPPDATA%\Programs\Makdon`, without administrator rights). The "semua pengguna" (all users) option installs to `Program Files` and asks for administrator rights. A desktop icon is not created unless checked.

In the installer, Makdon cannot be installed if a newer version is already installed (downgrades are rejected). An older version must be uninstalled first.

### Verifying the download

Each release includes `SHA256SUMS.txt`. Compare the hash of the downloaded file with the matching line in it:

```powershell
(Get-FileHash .\Makdon-0.1.0-setup-x64.exe -Algorithm SHA256).Hash
```

The result must match the corresponding line in `SHA256SUMS.txt` (case differences can be ignored).

### Windows requirements

- **Windows 11 23H2 and later, x64**: supported.
- **Windows 10 x64**: according to .NET documentation, official support is only for the LTSC/Enterprise editions (1607, 1809, 21H2). Consumer Windows 10 (22H2) runs but without official support. Not verified on a test device.
- Windows 7/8.1, 32-bit Windows, and Windows 10 below 1607 (build 14393) are not supported. The installer refuses to install there; the portable zip cannot refuse by itself, so the runtime will fail before Makdon starts.

### SmartScreen and Smart App Control warnings

Releases are not yet digitally signed, so Windows may show a warning the first time you run them:

- **Installer:** on the "Windows melindungi PC Anda" (Windows protected your PC) screen, choose **Info selengkapnya** (More info) > **Tetap jalankan** (Run anyway). Files installed by the installer do not carry the "from the internet" mark, so `Makdon.exe` is no longer warned about.
- **Portable zip:** Explorer marks every file extracted from a downloaded zip. Suggested order: verify the checksum, then right-click the zip > **Properties** > check **Unblock** > OK, and only then extract. PowerShell alternative: `Unblock-File .\Makdon-0.1.0-portable-x64.zip` before extracting.
- **Smart App Control** (Windows 11, active only on clean Windows installations): unsigned applications can be blocked without a "Run anyway" button. Makdon cannot run on such a PC until a signed release exists. There is no official way around this here.

## "Open with" (file association)

- **Installer:** registration is written automatically. For per-user installs it is in HKCU; for all-users installs, in HKLM. Uninstalling removes the registration.
- **Portable:** choose **Berkas > Integrasi Explorer > Daftarkan ke "Buka dengan"** (File > Explorer Integration > Register for "Open with"). The same menu provides **Cabut pendaftaran** (Unregister). The portable Makdon does not register when an installed version is detected (use the installed version or uninstall it first). If the portable folder is moved, Makdon at the new location offers to update a registration that still points to the old location.

Makdon does not force itself to be the default application. After registering, right-click a `.md` file > **Open with** > **Choose another app** > select Makdon (check "Always use this app"), or set it in **Settings > Apps > Default apps** (`Pengaturan > Aplikasi > Aplikasi bawaan`).

### Migrating from the old association script

If you previously registered with `scripts\register-file-association.ps1`, first run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\unregister-file-association.ps1
```

then install with the installer. The old registration points to the publish folder and can make "Open with" point to an exe that no longer exists.

## Updates

Makdon does not check for updates itself. Download a new version from the [releases page](https://github.com/hoaaah/Makdon/releases):

- **Installer:** run the new installer. Installation overwrites the program in place, and settings are kept.
- **Portable:** extract the new zip over the old folder. Keep the `data` folder and the `Makdon.portable` file.

Makdon must be closed before installing or uninstalling. The installer and uninstaller ask you to close it if it is still running.

## Uninstall

- **Installer (`Pengaturan > Aplikasi`, or Control Panel):** in interactive mode, the uninstaller asks whether settings and error logs should also be deleted (default: **Tidak** (No)). Yes deletes `%APPDATA%\Makdon` and `%LOCALAPPDATA%\Makdon` for the profile used for uninstalling. In an all-users installation, the uninstaller does not delete each user's data; it remains in each user's own profile. Uninstalling with `/SILENT` or `/VERYSILENT` does not ask and does not delete data.
- **Portable:** unregister first (**Berkas > Integrasi Explorer > Cabut pendaftaran**), then delete the folder. The `data` folder is deleted along with it.

## Data locations

| Data | Installer (installed) | Portable |
| --- | --- | --- |
| Settings (theme, zoom, recent files, session, remote image blocking) | `%APPDATA%\Makdon\settings.json` | `<folder Makdon>\data\settings.json` |
| Error log | `%LOCALAPPDATA%\Makdon\crash.log` (when larger than 512 KB, the whole file is deleted and a new entry is written); Print Preview failures are also logged here | `<folder Makdon>\data\crash.log` |

In portable mode, data never moves to `%APPDATA%`. If the portable folder is not writable (e.g., inside `Program Files`), settings apply only while Makdon is running, and Makdon tells you once.

Broken or missing settings are ignored (reset to defaults). On exit, the recent files list is merged with the content of the file on disk so that several instances do not overwrite each other.

Session rule: an instance opened with a file argument (e.g., double-clicking a `.md`) contains only that file, so at first it does not overwrite the saved session. Once you open a tab again in that instance (a file sent from a later launch, the Open dialog, drag-and-drop, or Recent Files), the instance is "adopted" as a normal workspace, and on exit it saves its session like an instance started without arguments.

## Keyboard shortcuts

| Shortcut | Function |
| --- | --- |
| Ctrl+N / Ctrl+O | New / open document |
| Ctrl+S / Ctrl+Shift+S | Save / Save As |
| Ctrl+W | Close tab |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab |
| Ctrl+1 / Ctrl+2 / Ctrl+3 | Editor / Split / Preview mode |
| Ctrl+F / Ctrl+H | Find / Replace |
| F3 / Shift+F3 | Find next / previous |
| Ctrl+B / Ctrl+I / Ctrl+E | Bold / Italic / Inline code |
| Ctrl+Shift+L / Ctrl+Shift+Q | List / Quote |
| Ctrl+K / Ctrl+Shift+I | Link / Image |
| Ctrl+= / Ctrl+- / Ctrl+0 | Zoom in / Zoom out / Normal zoom |
| Ctrl+Shift+E | Export as HTML |
| Ctrl+P | Print |
| Ctrl+Shift+P | Print Preview (Ctrl+P in the preview window = Print...) |
| Esc | Close the find panel |

## Documentation

Development documentation (architecture, design decisions, contributing, security, testing, distribution) is in [docs/](docs/README.md).
Change history: [CHANGELOG.md](CHANGELOG.md).

## Build, test, publish

Requires the **.NET 10 SDK** (target `net10.0-windows`).

```powershell
dotnet build Makdon.sln
dotnet test src/Makdon.Tests
dotnet run --project src/Makdon -- contoh.md
```

Publish a release (self-contained, as a folder; the .NET runtime is bundled, so it does not need to be installed on the target machine):

```powershell
dotnet publish src/Makdon -p:PublishProfile=win-x64
```

Output: `src\Makdon\bin\Release\net10.0-windows\win-x64\publish\Makdon.exe`. To build the installer and the portable zip together (requires Inno Setup 6), run `scripts\build-release.ps1`; the output goes to `artifacts\<versi>\`. See [docs/CONTRIBUTING.md](docs/CONTRIBUTING.md#9-making-a-release).

## File association for development (.md)

This script writes only to HKCU, without administrator rights, and is intended for development. Regular users should just use the installer or the Explorer Integration menu of the portable version.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\register-file-association.ps1 -WhatIf   # preview first
powershell -ExecutionPolicy Bypass -File scripts\register-file-association.ps1
powershell -ExecutionPolicy Bypass -File scripts\unregister-file-association.ps1          # undo
```

Windows 10/11 protects the default-application choice, so after registering, choose Makdon via right-click on the file > Open with > Choose another app (check "Always use this app"), or in Settings > Apps > Default apps. `-SetDefault` writes the extension's default value in HKCU and backs up the old value to `HKCU\Software\Makdon\PreviousDefault`; the unregister script restores it. `-Extensions` accepts only lowercase letters and digits (e.g., `.md`). The script follows the same registry table as the installer (see [docs/DISTRIBUTION.md](docs/DISTRIBUTION.md#41-registry-specification-single-source-of-truth)).

## License

Makdon is released under the **MIT** license ([LICENSE](LICENSE)). Third-party notices (AvalonEdit, Markdig, Markdig.Wpf, the .NET runtime, WPF) are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) and [THIRD-PARTY-NOTICES-WPF.txt](THIRD-PARTY-NOTICES-WPF.txt), and are included in every release.

## Known limitations

- Windows only (WPF). The preview uses Markdig.Wpf: raw HTML in documents is ignored in the preview, and some Markdown extensions may look simpler than in the HTML export.
- Files over 50 MB are confirmed before opening; files over 500 MB are rejected. The whole file is read into memory. Thresholds are based on character count, in tiers: from 100k characters, the preview is parsed on a background thread; from 200k, the preview update delay increases; from 1 million, the delay grows longer and word/character statistics update less often.
- Detection of external file changes uses FileSystemWatcher and a check when the window is reactivated. A file whose size and write time have not changed (and is older than 2 seconds) is not re-read. Network shares that do not support watchers are detected only when the window is activated.
- Saving uses a temporary file `~md########.tmp` in the same folder. If the folder is not writable (access denied) or the temporary name is too long, the app writes directly to the file (not atomic; it is written from the start and then truncated). Other I/O errors (e.g., disk full) are not retried through that path and are reported.
- Single-instance uses a Mutex + named pipe per user per Windows session. If the pipe fails, the file is opened in a new instance. Files sent to an instance that is closing are ignored.
- A second launch without arguments only activates the existing window (it does not open a new window).
- Local images in HTML export that are not png/jpg/gif/webp, are larger than 2 MB, are outside the document folder, or exceed the 30 MB total budget are not embedded.
- Regex searches that are too slow (2-second limit per match, 4 seconds total) are not automatically retried with the same pattern; change the pattern or options to retry.
- There is no spell checker, autosave, or draft recovery for untitled, unsaved documents.
- The app does not check for updates and does not contact the internet (except when you open the releases page or a link on request).
- Releases are not yet digitally signed; see the SmartScreen warnings above.
- **Very large Markdown opens slowly in the main preview** (Split and Preview modes). Measured results: WPF lays out one huge `FlowDocument` superlinearly, so opening a file of about 200 KB takes ±8 seconds and ±370 MB of memory, 500 KB takes ±16 seconds and ±490 MB, and 1.5 MB takes more than 5 minutes with memory up to ±1 GB, with the UI at times not responding. The versions before and after the Print Preview feature are the same. Parsing the Markdown and the editor are not a problem (Editor mode opens a 1.5 MB file in under 2 seconds). Suggestion: for very large files, use **Editor** mode (Ctrl+1). Print Preview performance for documents of that size has not been measured.
- Print and Print Preview: `http(s)` images (when "Muat gambar remote" (Load remote images) is enabled) are loaded by WPF asynchronously, so in the preview pages (XPS) they may not be shown yet or may be empty; local images are not affected. Paper, orientation, margin, and footer settings in the preview window are not saved: each time it opens, they reset to A4, portrait, Normal, and footer on. Only A4 and Letter have presets; other paper sizes can only be printed at the preview size. The page range is not enabled in the Print dialog (the code does not set `UserPageRangeEnabled`), so the whole document is printed. Printing is synchronous: the UI is frozen while the document is sent to the printer. Not yet tested with a physical printer (only page generation and the window flow are tested automatically; actual printing, including "Microsoft Print to PDF", has not been tested).
