# Makdon

WPF .NET 10 app (Windows) for editing and previewing Markdown. See `README.md` for features and usage, and `docs/README.md` for development documentation (architecture, design decisions, security, testing, distribution).

## Commands

```powershell
dotnet build Makdon.sln                   # must be 0 warnings, 0 errors
dotnet test src/Makdon.Tests              # xUnit; all must pass
dotnet run --project src/Makdon -- file.md
dotnet publish src/Makdon -p:PublishProfile=win-x64   # self-contained, folder (profile in Properties/PublishProfiles)
powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1   # build+test+publish+installer+zip+SHA256SUMS (artifacts\<versi>\)
```

## Structure

- `src/Makdon` - the app. Entry point `App.xaml.cs` (single instance, global error handler), then `MainWindow`.
  - `MainWindow` - tabs, dialogs (conflict, save, file size), session/settings, menu, Explorer Integration menu (portable), About. One `DocumentTab` per document.
    `ChoiceDialog` - small themed dialog with labeled buttons (used by conflict dialogs; not a Yes/No/Cancel MessageBox).
  - `DocumentTab` - document model: text, path, encoding, file hash/stamp on disk, external change watcher, save.
  - `DocumentView` - AvalonEdit editor + FlowDocument preview (Markdig.Wpf), scroll sync, `Dispose()` stops the timers.
  - `TextFileIO` - read/write/encoding detection/atomic save; `FileStamp` - size + write time.
  - `MarkdownSupport` - Markdig pipelines (preview `Pipeline`, export `ExportPipeline`), URL allowlist, image blocking.
  - `HtmlExporter` - standalone HTML export; `SingleInstance` - `Local\` Mutex + named pipe (the name includes the Windows session id, and a scope for portable mode);
    `AppSettings` - JSON settings.
  - `AppPaths` - installed/portable mode (`Makdon.portable` marker next to the exe) and the locations of `settings.json`/`crash.log`;
    `AppInfo` - identity constants (installer AppId, mutex name, release URL, license); `InstallerMutex` - fixed named mutex for the installer (installed mode).
  - `RegistryStore` (`IRegistryStore`, `WindowsRegistryStore`) + `FileAssociation` - "Open with" registration in HKCU for portable mode.
  - `PrintLayout.cs` (`PageLayout`, `PrintSnapshot`, `PrintSource`, `PrintService`), `HeaderFooterPaginator`, `PreviewBuild`, `PrintPreviewWindow` -
    Print (Ctrl+P) and Print Preview (Ctrl+Shift+P): tab text snapshot -> parse -> Light-theme print FlowDocument -> pagination + page footer ->
    in-memory XPS pages for `DocumentViewer`; `PreviewBuild.Guard` catches all errors, and the XPS package is cleaned up after the dispatcher is idle.
  - `Themes/` - Light/Dark color dictionaries and control styles.
  - `Properties/PublishProfiles/win-x64.pubxml` - release publish profile.
- `src/Makdon.Tests` - xUnit. Tests that touch WPF run through `Support/WpfHost` (one STA thread, `TestApp` without `OnStartup`) with
  `[Collection("Wpf")]`; `Support/TempDir` for temporary files; `Support/FakeRegistryStore` for a fake registry.
- `scripts/` - file association registration (HKCU; do not run without `-WhatIf` first), `build-release.ps1` (local release builder, same as CI), and the icon generator.
- `installer/` - `Makdon.iss` (Inno Setup 6) and `Languages/Indonesian.isl` (unofficial translation).
- `.github/workflows/release.yml` - automatic release on push to the `build` branch (version from `<Version>`; the workflow creates the `v<versi>` tag).
- `LICENSE` (MIT), `THIRD-PARTY-NOTICES.txt` - included in every release.

## Conventions

- UI text, messages, and code comments are in Indonesian; follow the existing style. Identifier names are in English.
- Documentation (`README.md`, `AGENTS.md`, `docs/`) is in English. `README.id.md` is the Indonesian version of `README.md`. **`README.md` and `README.id.md` must be updated together**, in the same change.
- Nullable and ImplicitUsings are enabled; the build must not produce warnings.
- Comments explain the reason (why), not repeat the code. Do not add features beyond what was requested.
- Saving files always goes through `TextFileIO.Write` (atomic, keeps encoding/BOM); do not write document files directly.
- Preview uses `MarkdownSupport.Pipeline`; HTML export must use `ExportPipeline` + `SanitizeForExport` (raw HTML is escaped, URLs are filtered). Do not merge the two.
- Images in preview/print (`ResolveImageUrls`): only local files and `http(s)` may reach WPF; UNC, other schemes (`ftp:`, etc.), and `data:` are replaced with a marker text (WPF does not load `data:` through a URI and would fail the whole preview; `http(s)` follows the remote-blocking option). The export allowlist is separate (`ClassifyUrl`/`SanitizeForExport`): valid `data:image/*` remains allowed in export.
- HTML export embeds local images only if they are under the document folder (those outside are replaced with a marker; privacy), max 2 MB per image, total budget `MaxTotalEmbeddedBytes` (30 MB, counted per occurrence; once exhausted, relative paths are left as they are), cache per full path.
  `ExportHtml_Executed` catches OOM and general errors with a friendly message.
- Documents are opened only through `MainWindow.OpenFile` (size check, OOM, duplicate-tab conflict). All conflict dialogs (external changes and save conflicts) use `ChoiceDialog` and are serialized through `conflictPromptOpen`/`conflictQueue`; do not show `MessageBox`/conflict dialogs directly from an event. Reloading after a conflict is one Undo step (`DocumentTab.Reload`), and `DocumentTab.SaveTo` postpones external checks while it runs.
- Sessions: an instance started with arguments does not overwrite the saved session until the user opens a tab again (messages from other instances, Open dialog, drag-and-drop, Recent Files), via `OpenUserFile`; after that, the session is saved as usual.
- Print/Print Preview: a print document must not contain local paths or an error document (the print path throws, `throwOnFailure`); print tests do not use a physical printer or a real dialog (`ShowPrintDialogForTests`), and expected dispatcher errors are wrapped in `WpfHost.ExpectUnhandled()`.
- Recoverable I/O errors are caught and shown to the user; unexpected errors are logged to `CrashLog` (location via `AppPaths`, see below).
- Data locations (`settings.json`, `crash.log`) only through `AppPaths` (`SettingsPath`, `CrashLogPath`); do not call `Environment.GetFolderPath` directly. Portable mode writes to `<exe folder>\data\` and never silently moves to `%APPDATA%`.
- "Open with" registry access only through `IRegistryStore` (`FileAssociation` does not touch `Microsoft.Win32.Registry` directly). Tests use `FakeRegistryStore`.
- Installer identity: the `AppId` GUID (`installer/Makdon.iss`, `AppInfo.InstallerAppId`) **must not change**; the registry table in `docs/DISTRIBUTION.md` section 4.1 must match exactly in `FileAssociation`, `installer/Makdon.iss`, and `scripts/register-file-association.ps1`.
  The installer mutex name (`Makdon.AppMutex`) must also match in `AppInfo` and `Makdon.iss`.
- Do not remove the `Switch.System.Windows.DisableXpsPackageBoundaryRestriction` switch (`Makdon.csproj` and `Makdon.Tests.csproj`): without it, .NET 10 makes Print Preview always fail. Do not set it via `AppContext.SetSwitch` in code (WPF caches that switch).
- Tests must not touch/write `%APPDATA%`, `%LOCALAPPDATA%`, or the registry (reading HKCU Personalize for the theme is allowed); the user's real crash.log must not be touched (logging is already redirected in `TestLogRedirect`); `SingleInstance.Create(scope)` uses a unique scope in tests.
  Do not run real registry scripts.
- Do not commit or push unless asked.
