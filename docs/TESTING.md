# Makdon Testing

**Purpose:** test map (file -> area), how to run the tests, the thread model of the WPF tests, the list of things that are **not** tested, and the manual test checklist before a release.
**Audience:** developers who run/write tests and people preparing a release. How to write new tests (patterns and rules):
[CONTRIBUTING.md](CONTRIBUTING.md#4-writing-tests).

Honesty note: this document was written by reading the code. Last run (2026-10-08, `dotnet test src/Makdon.Tests`): 1543 cases passed, 0 failed, 0 skipped (before the Print Preview feature: 1168 cases, about 22 s; before the distribution feature: 1455 cases). The test count in the tables = the number of `[Fact]`/`[Theory]` attributes per file (result of `grep`), not the number of cases executed; one `[Theory]` can be many cases. Total 861 attributes in 27 files.

## Test map: file -> area

The `src/Makdon.Tests` project (xUnit 2.9.2, `net10.0-windows`). `[W]` = class marked `[Collection("Wpf")]` (runs through `WpfHost`).

| File | Attributes | Classes (area) |
| --- | --- | --- |
| `TextFileIOTests.cs` | 44 | `TextFileIODecodeTests` (BOM, UTF-8/16/32, 1252 fallback, byte-identical round-trip), `TextFileIOEncodeTests` (BOM, upgrade to UTF-8, orphan surrogates), `TextFileIOHashTests` (SHA-256), `TextFileIOFileTests` (atomic writes, no leftover temp files, locked/read-only/directory files, symlinks) |
| `IoAndUtilityCoverageTests.cs` | 73 | `TextFileIOCoverageTests` (lossy flag per BOM, `WriteBytesAtomic`, `WriteInPlace` fallback via ACL, `ResolveLinkTarget`, `FileStamp`), `TextStatsChunkBoundaryTests` (64 KB chunks), `CrashLogLimitsTests` (error classification, 512 KB limit, concurrency), `AppSettingsMergeEdgeTests` (`SaveMerged`, `BlockRemoteImages`), `SearchEngineEdgeCaseTests` (`NormalizeLineEndings`, total time limit) |
| `HardeningTests.cs` | 58 | `ExportSanitizationTests` (HTML escaping, URL allowlist, `ClassifyUrl`), `RemoteImageBlockingTests` (UNC/remote in the preview), `DocumentTabHardeningTests` `[W]` (save vs external change, lossy, dispose), `TextFileIOHardeningTests`, `TextStatsSourceTests`, `AppSettingsMergeTests`, `SingleInstanceTests` (protocol + basic pipe) |
| `ExportAndImageSecurityTests.cs` | 30 | `ExportXssVectorTests` (XSS vectors, autolink, media link, images), `ResolveImageUrlsSecurityTests` (UNC/scheme variants in the preview, `IsAllowedLocalPath`) |
| `LinkResolutionTests.cs` | 12 | `MarkdownSupport.ResolveLinkTarget`: UNC/device links (including percent-encoded) are rejected, the same share is allowed, local/relative drives, `#anchor`, `file:` with/without host, non-markdown, empty input (66 cases) |
| `ExportImageBudgetAndPrivacyTests.cs` | 9 | Local images only under the document folder, 30 MB total budget, per-path cache |
| `HtmlAndMarkdownSupportTests.cs` | 34 | `MarkdownFilesTests`, `MarkdownSlugTests` (GitHub-style heading ids), `ResolveImageUrlsTests` (path resolution), `HtmlExporterTests` (template, title, file writing) |
| `DocumentTabTests.cs` | 61 | `DocumentTabTests` `[W]` (create/load/save, encoding, `IsDirty`/Undo, external changes, `Reload`, dispose, export), `ThemeManagerApplyTests` `[W]` (swapping `ResourceDictionary`) |
| `DocumentTabConflictEdgeTests.cs` | 24 | `[W]` Save/external conflicts: `pendingHash`, `KeepEditorVersion`, "racy" stamps, lossy flag, single-step Undo |
| `DocumentViewLifecycleTests.cs` | 22 | `[W]` `UiPump` definition; `DocumentView.Dispose`, `FindReplaceBar.Detach`, stale background render discarded, broken/`data:`/FTP images in preview and print, regex pattern timeout is not re-run |
| `SingleInstanceServerTests.cs` | 24 | `ParseMessage`/`BuildMessage`, real pipe server (unique scope): 64 limit, large messages, stalled client, throwing callback, lifecycle |
| `SearchEngineTests.cs` | 70 | `SearchEngineFindTests`, `TryFindAllTests`, `IndexTests`, `ReplaceAllTests` (literal/regex, CRLF, result limit, catastrophic patterns) |
| `MarkdownEditingTests.cs` | 86 | `MarkdownEditingInlineTests`, `LineTests`, `LinkTests` (no UI, `TextDocument`), `MarkdownEditingApplyTests` `[W]`; also `CollectionDefinition("Wpf", DisableParallelization = true)` |
| `AppSettingsTests.cs` | 40 | `Load` (corrupt, wrong types, sanitizing), `Save`, `AddRecent`/`RemoveRecent`, round-trip |
| `SmallUtilityTests.cs` | 49 | `ZoomLevelTests`, `TextStatsTests`, `EncodingNamesTests`, `ThemeManagerPureTests`, `EditorThemeContrastTests`, `ConverterTests` |
| `CrashLogTests.cs` | 7 | `IsRecoverable`, known HRESULTs, `ShouldShowDialog`, timestamp format, `Write` |
| `ChoiceDialogTests.cs` | 2 | `[W]` `ChoiceDialog`: clicking a button returns its value, closing without choosing = cancel value |
| `PrintPreviewTests.cs` | 19 | `[W]` `PageLayoutTests` (default A4 portrait with Normal margins, landscape swaps sides, margin order, `FromPrintableArea`, effective margin <= a quarter of the short side, `Apply`) and `PrintPreviewWindowTests` (window created on STA, async pagination, snapshot unaffected by the editor, print document still blocks remote/UNC images, paginator + footer, XPS pages match the paginator, basic navigation) |
| `PreviewBuildTests.cs` | 32 | `[W]` `PreviewBuild`: Paginating -> Rendering -> Ready stages in order, XPS page contents (document name, "Halaman X dari N" (Page X of N), page size), empty document, `Dispose` at each stage (including from inside a callback), second cycle, one XPS writer per cycle, errors do not escape to the dispatcher (`Changed` subscriber throws, OOM), package removed from `PackageStore`, 10 s fallback timer, shared parse source and held background parse, long word does not exceed page width |
| `HeaderFooterPaginatorTests.cs` | 18 | `[W]` Footer: name + "Halaman X dari N" on each page, no footer = identical pages, page count the same with/without footer, footer inside the bottom margin and >= 24 DIP from the edge for the three presets, same page requested twice, pages out of range, odd and very long document names |
| `PrintLayoutMatrixTests.cs` | 17 | `[W]` `PageLayoutMatrixTests`: all 12 combinations of paper x orientation x margin, out-of-range enum values, `FromPrintableArea` with NaN/infinity/too small/too large, `Apply` twice and extreme margins, wider margins never give fewer pages |
| `PrintContentAndCommandTests.cs` | 23 | `[W]` Print document (white paper, Light theme even when the app is Dark, independent documents), render failure (`DocumentView.RenderFaultForTests`: friendly message without path, not an error document), broken/missing/valid images, ftp/UNC/`file://host`/`data:`/`javascript:` blocked without connection (local TCP listener), remote-block flag from snapshot, `AppCommands.PrintPreview` (Ctrl+Shift+P) and shortcut uniqueness (reads the text of `MainWindow.xaml`), menu/toolbar wiring, Ctrl+P in the main preview panel, `ApplyTicket`/`TicketMatches`/`DescribeTicket` without a printer |
| `PrintPreviewWindowBehaviorTests.cs` | 42 | `[W]` Print Preview window controls: page setting builds a new cycle, navigation (First/Previous/Next/Last, page box, limits), zoom, Esc/Close, Print button and command (via `ShowPrintDialogForTests`), errors from callbacks/cancellation, pending parse and large documents, snapshot of a closed or moved tab |
| `WpfHostErrorTrackingTests.cs` | 4 | `[W]` Dispatcher error recorder: an unexpected error fails the test (once), an error inside `ExpectUnhandled` does not, an error after the scope is still reported, an error while pumping inside `Run` does not hang the process |
| `FileAssociationTests.cs` | 43 | `FileAssociation` with `FakeRegistryStore` (no real registry): `Register` writes exactly the DISTRIBUTION 4.1 table to HKCU, no default extension value and no `UserChoice`, and notifies the shell once; installation detection (Inno `_is1` key in HKCU/HKLM, `WOW6432Node`, quoted paths, invalid paths); refusal when installed (including for all users); classification of the registered exe (stale, other portable, no marker, inside the install folder); `Unregister` only for this exe; `CheckStartup` (no writing); `ExtractExePath`; `IsUnderFolder` (trailing separator). Added after review: relative commands are not normalized against the working folder (`RelativeCommand_*`), UNC paths without file checks (`IsNetworkPath_*`, `UncRegistration_*`, `UncInstallationPath_*`), and `Register` rejects an exe that is not `Makdon.exe` or does not exist (`Register_WhenTheExeWasRenamed_*`). |
| `AppPathsTests.cs` | 14 | `AppPaths` (`Detect` with marker and data folder, `SingleInstanceScope` stable and different per folder, `IsDataDirectoryWritable`: portable creates `data\` without leaving a test file, a folder that cannot be written = false without throwing; installed touches nothing); `InstallerMutexTests` (portable does not create the installer mutex) |
| `WpfHostStartupTests.cs` | 4 | `[W]` `TestHost_*`: the test host does not run `App.OnStartup` and stays alive; theme resources are the same as in the App. `XpsBoundarySwitchTests`: Makdon's runtimeconfig and the test process load `Switch.System.Windows.DisableXpsPackageBoundaryRestriction` (see ADR-32) |
| `Support/WpfHost.cs` (+ `DispatcherErrors`, `FailOnUnexpectedDispatcherErrorsAttribute`), `Support/AssemblyInfo.cs`, `Support/PrintTestKit.cs`, `TempDir.cs`, `TestLogRedirect.cs` | - | Infrastructure (see below). `PrintTestKit`: small `Small` page 360 x 420, sample documents, `StartBuild`/`WaitForEnd`/`WaitForPaginated`, reader for the glyph text of XPS pages and footer text. `Support/FakeRegistryStore.cs`: in-memory `IRegistryStore` (case-insensitive names, parent keys created on write, `Mutations` counts write/delete operations); `TestApp` in `WpfHost` (see below). |

The area -> file map for security and decisions is in [SECURITY.md](SECURITY.md) and [DESIGN-DECISIONS.md](DESIGN-DECISIONS.md).

## How to run

From the repo root (PowerShell):

```powershell
dotnet test src/Makdon.Tests                                            # all tests
dotnet test src/Makdon.Tests --filter "FullyQualifiedName~DocumentTabTests"     # satu kelas
dotnet test src/Makdon.Tests --filter "FullyQualifiedName~SingleInstance"       # all classes whose name contains that text
dotnet test src/Makdon.Tests --filter "DisplayName~IsRecoverable"               # berdasarkan nama test
dotnet test src/Makdon.Tests --logger "console;verbosity=detailed"               # keluaran rinci per test
dotnet test src/Makdon.Tests --blame-hang-timeout 2min                           # abort + report hanging tests
dotnet test Makdon.sln                                                           # lewat solution
```

- `--blame-hang-timeout` is useful here because WPF tests pump the dispatcher (modal `ChoiceDialog`, `Dispatcher.Invoke`): one dialog
  that is never closed or an `Invoke` that waits forever will hang the whole test process. `WpfHost` itself gives up if the STA thread is not
  ready within 60 s (`Support/WpfHost.cs:57`). The value `2min` is only an example.
- There are no `Trait`s/categories; filtering is by class or test name. Classes with many slow tests are not grouped separately.
- Before a PR: `dotnet build Makdon.sln` (must have 0 warnings) and then `dotnet test src/Makdon.Tests` (all green) - the AGENTS.md rule.
- Some tests take real time: the stalled pipe client waits for the 5 s read limit (`Server_StalledClient_IsDroppedAfterTheReadTimeout_*`),
  catastrophic patterns wait for the 2-4 s regex limit (`TimedOutPattern_*`, `TryFindAll_CatastrophicRegex_*`), and rendering a large document
  waits up to 20 s if it is slow (`UiPump.Until`). Print preview tests wait with the limit `PrintTestKit.Patience` (15 s),
  not a fixed delay: they finish as soon as the condition is met, and slowness shows up as a failure. That is why the suite is not instant.

## STA thread and `WpfHost`

- WPF requires an STA thread, and `Application` is a singleton per process. `WpfHost.Instance` (`Support/WpfHost.cs`) creates **one**
  background STA thread (`IsBackground = true`, named `Makdon.Tests STA`), creates one `TestApp` (a subclass of `App`, `ShutdownMode.OnExplicitShutdown`, loads the `app.xaml` resources through `LoadAppXaml()`, **without** `OnStartup`: no single-instance, no `MainWindow`, no installer mutex, and no settings in `%APPDATA%`; see [ADR-33](DESIGN-DECISIONS.md#adr-33-wpfhost-uses-testapp-without-onstartup)), and then runs `Dispatcher.Run()`.
- All tests that touch WPF call `WpfHost.Instance.Run(() => ...)`, and their class is `[Collection("Wpf")]`. `Run` is not just
  `Dispatcher.Invoke`: the operation is run with `InvokeAsync` and waited on in 100 ms steps (`WpfHost.cs:74-104`); a failure belonging to the test
  (e.g. `Assert`) is caught on the STA thread and rethrown on the test thread. The reason (comment `WpfHost.cs:67-73`): if a deferred callback throws while
  a test pumps the dispatcher (`UiPump`) and the error is swallowed, WPF leaves that nested frame forever, so a plain `Invoke` never
  returns and the whole test process hangs without any message. Now, if an error has been recorded and the operation has not completed within 3 s (`GraceAfterError`),
  the test fails with that error as the message. A `Run` called from the STA thread (nested) runs its function directly.
  `DocumentTab` captures `Dispatcher.CurrentDispatcher`, so it must be created **inside** `Run`.
- The `DispatcherTimer`s in the tabs (render 250 ms, statistics 300 ms, watcher debounce 400 ms, find 150/250 ms) **only tick while the dispatcher
  is pumped**: between two `Run` calls, or inside one `Run` when a test pumps by itself through `UiPump.For`/`UiPump.Until`
  (`DispatcherFrame`, defined in `DocumentViewLifecycleTests.cs`). That is why many tests call
  `CheckExternalChange()`/`RefreshPreview()` directly to be deterministic (comment `DocumentTabTests.cs:8-11`).
- The `Wpf` collection is marked `DisableParallelization = true` (`MarkdownEditingTests.cs:942`); other classes (non-WPF) can run in parallel
  with each other, so they must not share static state. Static state touched by tests that must be restored: `DocumentView.BlockRemoteImages`,
  `ThemeManager` (mode/dictionary), `CrashLog.LogPath`, `CultureInfo.CurrentCulture`.
- `TestLogRedirect` (`[ModuleInitializer]`) redirects `CrashLog.LogPath` to `%TEMP%\Makdon.Tests\crash-<pid>.log` when the test assembly is loaded.
- **Dispatcher error recorder** (`Support/WpfHost.cs`). The `TestApp` `OnStartup` is empty, so `WpfHost` installs its own
  `DispatcherUnhandledException` handler, which puts the error into `DispatcherErrors.Queue` (`WpfHost.Unhandled`) and marks it `Handled` (the test process
  does not die, like a recovered error in the application). The assembly-level attribute `[assembly: FailOnUnexpectedDispatcherErrors]`
  (`Support/AssemblyInfo.cs:4`, class `FailOnUnexpectedDispatcherErrorsAttribute`, a subclass of `BeforeAfterTestAttribute`) checks at the end of
  **every test in the `Wpf` collection**: if there is an unacknowledged error, that test fails (once; an error that arrives between two tests
  is charged to the next test).
  - `WpfHost.ExpectUnhandled()` opens a scope for tests that deliberately check that a callback throws: errors during the scope are treated as
    expected (check `scope.Errors`); errors that arrive after the scope closes are still reported.
  - The pattern used by print tests to make sure no error escapes: `var before = WpfHost.Unhandled.Count; ...; Assert.Equal(before, WpfHost.Unhandled.Count);`
    (in addition to the automatic checker above, so that the failure message points directly at that test).
  - WPF behavior when an error breaks out inside a `UiPump` frame is not fixed (sometimes the frame is abandoned, sometimes the pump finishes normally);
    `WpfHostErrorTrackingTests.AnErrorThrownWhilePumpingInsideRun_*` only guarantees: it returns (no hang), the error is recorded, and the host can still be used.
- `TempDir`: a unique folder `%TEMP%\Makdon.Tests\<guid>`; test classes that create tabs call `GC.Collect()` +
  `WaitForPendingFinalizers()` before deleting it, because `BitmapImage` holds the image file handle until it is garbage collected.
- Tests access private fields through reflection (`UiPump.IsTimerEnabled`: `statsTimer`, `renderTimer`, `queryTimer`, `refreshTimer`) and
  XAML elements via `FindName`/internal fields; renaming either requires updating the tests.

## Not tested

Checked with `grep` against `src/Makdon.Tests`; "not tested" means no test runs its code.

| Area | Description |
| --- | --- |
| **`MainWindow`** (entire class) | No test creates `MainWindow` (the only mention is a comment at `DocumentTabConflictEdgeTests.cs:71`; two print tests only read the text of `MainWindow.xaml` with a regex to check command wiring and shortcut uniqueness, `PrintContentAndCommandTests.cs:333, 369`). Not tested: `OpenFile` (size checks 50/500 MB, OOM, duplicate tabs), the conflict queue (`conflictQueue`/`conflictPromptOpen`), `OnSaveConflict`, `TrySave` (lossy confirmation), `preserveStoredSession`/`RestoreSession`/`SaveSettings`, `OpenFromOtherInstance`, drag-and-drop, the recent files list, command handlers, `Window_Closing`, the `ExportHtml_Executed`/`Print_Executed`/`PrintPreview_Executed` handlers (OOM/error catching when creating the preview window and the modal `ShowDialog`). The logic underneath (`DocumentTab`, `AppSettings`, `SingleInstance`, print model) is tested separately. Since the distribution feature, these are also not tested: `RunPortableStartupChecks`, `RegisterAssociation_Click`, `UnregisterAssociation_Click`, `ExplorerIntegration_SubmenuOpened`, and `About_Click`. |
| **`App`** | `OnStartup` (single-instance order), `OnDispatcherUnhandledException` (fatal/recovered decision + dialog), `OnExit`. `CrashLog.IsRecoverable`/`ShouldShowDialog` are tested; their use is not. Since the distribution feature, also not tested: `InstallerMutex.Acquire` and the choice of portable scope. |
| **Dialogs** | `ChoiceDialog` is tested on its own (2 tests). System dialogs (`OpenFileDialog`, `SaveFileDialog`, `MessageBox`) are not tested. `PrintDialog` is never opened in tests: the Print button/command in the preview window is tested through the hook `PrintPreviewWindow.ShowPrintDialogForTests`, used only with the "cancel" result (`PrintPreviewWindowBehaviorTests.cs:889, 921`). `ConfirmPaperMatchesPreview` (and the paper confirmation `ChoiceDialog`) is not run by any test; only its pure helpers are tested (`TicketMatches`, `ApplyTicket`, `DescribeTicket`, `PrintContentAndCommandTests.cs:421-528`). |
| **Printing** | **Tested:** building the print document (`BuildPrintDocument`/`PrintService.CreateDocument`: resulting text, image markers, white paper, Light theme, page size and margins), pagination and page size (all combinations of paper x orientation x margin, `FromPrintableArea` with unreasonable values), footer, `PreviewBuild` (stages, XPS pages, `Dispose`, package in `PackageStore`), the preview window (settings, navigation, zoom, close, snapshot), blocking remote/UNC/ftp/`data:` images in the print document. **Not tested:** actual printing (`dialog.PrintDocument` is never run: no physical printer, driver, or "Microsoft Print to PDF" in tests), `PrintService.Print` and `Print_Executed` (Ctrl+P), the "dialog accepted" branch including `ConfirmPaperMatchesPreview`, the pixel appearance of pages (tests read glyph text, sizes, and footer boxes, not images), `http(s)` images loaded asynchronously on XPS pages, OOM while printing, and very large Markdown documents (performance is not measured automatically; see README, Known limitations). |
| **Visual/appearance** | Layout, `Controls.xaml`/`Preview.xaml` styles, the real theme appearance, high DPI, icons, dark title bar (`ApplyTitleBar`), empty state. Themes are tested only to the extent of dictionary swapping and contrast math; **whether the Light/Dark keys match is not tested** (only a comment). |
| **Scroll sync and anchor jumps** | `ScrollToAnchor`/`FindHeading`/`AnchorHeadingRenderer` and editor-preview scroll sync are not tested (a single `ScrollToAnchor` call only checks "does not throw after Dispose"). Heading id slugs (Markdig) are tested through `MarkdownSlugTests`. |
| **Link clicks** | The wrapper `DocumentView.OnHyperlink` (anchor, shell open for `http(s)`/`mailto`, `File.Exists` then `RequestOpen`). Path resolution (`MarkdownSupport.ResolveLinkTarget`) is tested in `LinkResolutionTests.cs`; `RequestOpen` is tested in `DocumentTabTests`. |
| **Zoom** | Ctrl+wheel and command wiring; only the pure `ZoomLevel` logic. |
| **End-to-end watcher** | `FileSystemWatcher` -> `dispatcher.BeginInvoke` -> `changeTimer` 400 ms -> `CheckExternalChange` is not tested as one chain; tests call `CheckExternalChange()` directly. Up to 5 read attempts (1 + 4 retries) for locked files are tested only as "does not throw". |
| **Symlinks** | Requires the right to create symlinks (Developer Mode or administrator). The symlink tests (`WriteBytesAtomic_SymlinkTarget_*`, `ResolveLinkTarget_FollowsAChain*`, `*_BrokenSymlink_*`) **return immediately (pass without testing anything)** when `CreateSymbolicLink` is refused; a green result does not prove that path. The export branch "symlink inside the folder points outside" (`MarkdownSupport.cs:333-334`) has no test at all. |
| **Cross-user pipe security** | `CurrentUserOnly` is not tested to refuse other users (requires a second account). |
| **Scripts** | `scripts/*.ps1` (registering/removing associations, icon builder) have no tests; AGENTS.md forbids running real registry scripts from tests. Also not tested: `scripts/build-release.ps1` and `.github/workflows/release.yml` (can only be run on a release machine or in CI). |
| **Memory resilience** | The `OutOfMemoryException` paths in `OpenFile`, `ExportHtml_Executed`, `Print_Executed`, `PrintPreview_Executed`, and render; the 50/500 MB limits. The only one tested: `PreviewBuild.Guard` turns an OOM thrown by a `Changed` subscriber (made by the test) into the `Failed` stage without escaping to the dispatcher (`SubscriberThatThrows_FailsTheBuild_AndNeverReachesTheDispatcher`, `PreviewBuildTests.cs:468`); real memory exhaustion on very large documents is not tested. |
| **Theme registry** | `ThemeManager.SystemUsesLightTheme` and `Apply(System)` are tested only against the registry value of the machine running the tests (read only); a change of the system theme while running (`UserPreferenceChanged`) is not tested. |
| **Real multi-instance / multi-session** | Tests use unique scopes inside one process; launching a real second process and Remote Desktop sessions are not tested. Portable mode (scope per exe folder) is also not tested with two real processes. |
| **`MarkdownPipeline` thread safety** | The static pipeline is used from background and UI threads; there is no concurrency test. |
| **Real registry and installer** | Writes to real HKCU/HKLM are not tested: `WindowsRegistryStore` has no tests, and all `FileAssociation` tests use `FakeRegistryStore`. The Inno Setup installer (`installer/Makdon.iss`, `Indonesian.isl`) has never been compiled; `AppMutex`, the uninstall dialog, and `InitializeSetup` have no automated tests. See the distribution checklist below. |

## Pre-release manual test checklist

Run on the **publish** build (`dotnet publish src/Makdon -p:PublishProfile=win-x64`, output in `src\Makdon\bin\Release\net10.0-windows\win-x64\publish\Makdon.exe`; self-contained, so no .NET needs to be installed on the test machine). For a release, also test the installer and the zip produced by `scripts\build-release.ps1`. Tick each item; record the Windows version (build), edition, and DPI.

**Build and packaging**
- [ ] `dotnet build Makdon.sln` 0 warnings, 0 errors; `dotnet test src/Makdon.Tests` green (and check that the symlink tests actually run, not pass empty, e.g. with Developer Mode on).
- [ ] `Makdon.exe` from the publish output runs from its own folder (DLLs included) without .NET installed; icon and window title are correct; `Bantuan > Tentang Makdon` (Help > About Makdon) shows the matching version ([../CHANGELOG.md](../CHANGELOG.md)).
- [ ] `scripts\build-release.ps1 -VerifyInstallerContents` is run only in CI, or with `-Force` on a machine that certainly has no Makdon installation.
- [ ] `scripts\build-release.ps1` (without `-SkipTests`) finishes with 0 warnings; `artifacts\<version>\` contains the installer, the portable zip, and a `SHA256SUMS.txt` that matches `Get-FileHash`.

**Startup and single-instance**
- [ ] Without arguments: the empty state is shown; without arguments + a stored session: tabs, mode, and caret position are restored.
- [ ] `Makdon.exe file.md` opens a tab; relative paths in the arguments open correctly.
- [ ] While running, a second launch with a file opens a tab in the existing window and brings it to the front (also from the minimized state); without arguments it only activates the window.
- [ ] After the primary instance is closed, the next launch becomes the new primary instance. (Optional) another Windows session has its own instance.

**Open and encoding**
- [ ] Open UTF-8, UTF-8 BOM, UTF-16 LE/BE, Windows-1252 files: the encoding label in the status bar is correct; saving without edits does not change the bytes (compare hashes).
- [ ] BOM file with invalid bytes: confirmation appears before saving.
- [ ] File > 50 MB asks for confirmation; > 500 MB is refused; opening the same file twice selects the existing tab; drag-and-drop of `.md` opens a tab, non-markdown files are skipped with a message.

**Edit and preview**
- [ ] Mode Ctrl+1/2/3; typing in Split mode: the preview follows; editor and preview scroll stay in sync without "jitter".
- [ ] Large document (>= 200,000 characters): typing stays smooth; the preview catches up; no stale render result overwrites a newer one.
- [ ] `#anchor` links jump to the heading; relative links to `.md` open a tab (with the anchor); `http(s)` links open the browser; links to non-markdown files do nothing.
- [ ] Format toolbar/shortcuts (Ctrl+B/I/E, Ctrl+Shift+L/Q, Ctrl+K, Ctrl+Shift+I, Heading menu): the results are correct and **one** Ctrl+Z undoes one operation; disabled in Preview mode or when focus is in the find panel.

**Find and replace**
- [ ] Ctrl+F/Ctrl+H, F3/Shift+F3, Esc; result markers are visible in both themes.
- [ ] Regex with `$` and `.` on a CRLF document matches as expected; the pattern `(a+)+$` on text `aaaa...b` shows "Pola terlalu lambat" (Pattern too slow) and is not re-run on every keystroke; "Ganti Semua" (Replace All) = one Ctrl+Z.

**Save and conflicts**
- [ ] Ctrl+S (file exists), `Simpan Sebagai` (Save As), saving an untitled document; encoding/BOM is kept; no leftover `~md*.tmp` in the folder.
- [ ] Saving to a read-only file / a folder without write permission shows a message (or the direct-write fallback if only the temp file cannot be created in the folder).
- [ ] Change the file from another program: a clean tab reloads automatically and Ctrl+Z restores it; a dirty tab shows the "Muat dari Disk" (Reload from Disk) / "Pertahankan Editor" (Keep Editor) dialog (test both choices).
- [ ] Ctrl+S on a tab whose file was changed externally: the Timpa (Overwrite) / Muat dari Disk (Reload from Disk) / Batal (Cancel) dialog (test all three; Reload from Disk can be undone).
- [ ] Two tabs hit by external changes at the same time: the dialogs appear one after another, not stacked. File deleted externally: the editor contents stay.

**Export and privacy**
- [ ] HTML export opens in a browser: raw HTML (`<script>`, `<img onerror>`) is shown as text; `[x](javascript:alert(1))` cannot be clicked; the title is correct.
- [ ] Images inside the document folder are embedded; images outside the folder (absolute, `../`) become `[gambar di luar folder dokumen tidak disertakan]` (image outside the document folder not included); > 2 MB or SVG stay relative. Search the HTML file for `C:\` and `file:///`: none found. (Optional, if you can create symlinks: a symlink pointing outside the folder.)
- [ ] Document with many large images: the export finishes or fails with a friendly message (the app does not close).

**Images in preview**
- [ ] `http(s)` images are blocked by default (`[gambar remote diblokir]` (remote image blocked)); the `Tampilan > Muat gambar remote` (View > Load remote images) menu shows them and applies to all tabs; the setting survives a restart.
- [ ] UNC images (`file://host/share/a.png`), `ftp://`, and `data:` are replaced by markers; monitor network traffic (e.g. `netstat`/Wireshark) while opening a document with UNC/FTP links to a test host: no connection is made.
- [ ] A broken local image (`.png` containing garbage) shows `[gambar tidak dapat ditampilkan: ...]` (image cannot be displayed) and the rest of the preview still shows.

**Theme, zoom, print**
- [ ] `Terang`/`Gelap`/`Ikuti Sistem` (Light/Dark/Follow System); change the Windows theme while in Follow System mode; the title bar follows; syntax colors are readable in both themes; no control has the colors of the other theme.
- [ ] Zoom with Ctrl+wheel, Ctrl+=/-/0; the value survives a restart; the status bar shows the percentage.
- [ ] Ctrl+P to a real printer and to "Microsoft Print to PDF" from the dark theme: the output has a white background, 0.75" margins, the footer "nama berkas ... Halaman X dari N" (file name ... Page X of N) is readable and not cut off by the printer, and remote images follow the blocking option.
- [ ] Print Preview (Ctrl+Shift+P or `Berkas > Pratinjau Cetak...` (File > Print Preview...), toolbar button): the window opens without freezing the UI ("Menyusun halaman..." (Laying out pages...) then "Menyusun pratinjau..." (Building preview...)); pages appear white with a footer; changing Orientation/Paper/Margin and the "Nama dan nomor halaman" (Document name and page number) box rebuilds the preview; navigation (First/Previous/Next/Last, type a number then Enter), zoom ("Satu halaman"/"Lebar halaman"/100% (Fit page/Fit width/100%), +/-, Ctrl+wheel), Esc closes; editing the text of the tab after the preview is opened does not change the open preview.
- [ ] Print from Print Preview to a real printer and to "Microsoft Print to PDF": the result matches the preview (page count, footer, margins). Change the paper or orientation in the Print dialog (e.g. Letter for an A4 preview, or Legal): the question "Cetak sesuai pratinjau" (Print as previewed) or "Batal" (Cancel) appears; Cancel does not print; "Cetak sesuai pratinjau" prints at the preview size. Without an installed printer: the Print dialog itself reports it, and the application does not crash.
- [ ] Very large documents (e.g. 500 KB and 1.5 MB Markdown): the main preview is slow and uses a lot of memory (see README, Known limitations); Editor mode stays smooth. Open Print Preview for that document and note whether the UI stays responsive, how long it takes, and the memory message if it fails (**never measured yet**). Document with `http(s)` images (remote blocking turned off): check whether the images appear on the preview pages (they may be blank, see README).
- [ ] Document with broken images (`.png` containing garbage) and UNC/`ftp:` images: Print Preview still builds and the images are replaced by markers; no network connection is made.

**Session and settings**
- [ ] Open several tabs, close, reopen: the session is restored. Open via a file argument then close: the previous stored session is **not** overwritten; open a tab again in that instance (Open dialog / drag-and-drop / Recent Files) then close: the session is saved.
- [ ] Recent Files (max 10); an entry whose file is missing shows a message and is removed.
- [ ] `settings.json` corrupted by hand: the application still opens with the defaults.
- [ ] After a recovered error (e.g. a broken image), `%LOCALAPPDATA%\Makdon\crash.log` (installed) or `data\crash.log` (portable) is filled; fatal error: there is no way to trigger one from a release (not verified manually; use a debug build if needed).

**File association scripts (on test account/VM)**
- [ ] `scripts\register-file-association.ps1 -WhatIf` changes nothing; without `-WhatIf` it registers in HKCU; "Open with" shows Makdon; `unregister-file-association.ps1` cleans up (and restores the default value if `-SetDefault` is used).

**Distribution: installer (clean account/VM, Windows 10 and 11)**
- [ ] Install per user (default, no UAC) to `%LOCALAPPDATA%\Programs\Makdon`: the Start Menu contains Makdon, `Makdon.exe` runs, `HKCU\Software\Classes\Makdon.Markdown` and `Applications\Makdon.exe` exist, and "Open with" shows Makdon for `.md`.
- [ ] Install for all users (asks for admin) to `Program Files`: the keys are in HKLM and other users see Makdon in "Open with". Switching modes (per user then all users, or the reverse): record whether two installations with the same AppId appear (**not verified**).
- [ ] Upgrade: install a newer version over an older one. Files are replaced, `%APPDATA%\Makdon\settings.json` stays, and the application runs. Check that leftover DLLs from the old version do not cause problems (**not verified**).
- [ ] Downgrade is refused: installing an older version shows the message "sudah terpasang, lebih baru" (already installed, newer) and the installation is cancelled.
- [ ] Makdon is running during install or uninstall: the installer and uninstaller ask for Makdon to be closed (AppMutex). Unsaved documents are still asked about through the save dialog (**not verified**).
- [ ] Interactive uninstall: the question "Hapus juga pengaturan dan catatan galat Makdon?" (Also delete Makdon settings and error logs?) appears with the default No. `Tidak` (No) → `%APPDATA%\Makdon` and `%LOCALAPPDATA%\Makdon` stay. `Ya` (Yes) → both are deleted.
- [ ] Uninstall of an all-users installation: deletes nothing and shows information that each user's data stays.
- [ ] Uninstall with `/VERYSILENT`: no dialog, data stays.
- [ ] After uninstall, the Makdon "Open with" keys are gone (`Makdon.Markdown`, `Applications\Makdon.exe`, the Makdon value in `OpenWithProgids`, `Software\Makdon` if empty). The default value of `.md` is unchanged.

**Distribution: portable and "Open with"**
- [ ] Extract the zip to a new folder and run it: `Makdon.portable` exists, `data\settings.json` is created when settings change, and no new files appear in `%APPDATA%\Makdon` or `%LOCALAPPDATA%\Makdon`.
- [ ] Folder not writable (e.g. in `Program Files` as a normal user): the message "Folder data portable tidak bisa ditulisi" (Portable data folder is not writable) appears once; settings are not saved and do not move to `%APPDATA%`.
- [ ] `Berkas > Integrasi Explorer` (File > Explorer Integration) appears only in portable mode. `Daftarkan` (Register) → success message and "Open with" shows Makdon. `Cabut Pendaftaran` (Unregister) → success message; repeat → "tidak ada yang dihapus" (nothing was removed).
- [ ] Portable while an installed version exists: `Daftarkan` (Register) is refused with a message, and no new `Makdon.Markdown` key appears in HKCU.
- [ ] An installation exists and HKCU still points to this portable copy: at startup, an offer "Cabut Pendaftaran" (Unregister) appears. "Biarkan" (Leave as is) changes nothing.
- [ ] Stale path: register from folder A, move to folder B, run from B. The offer "Perbarui" (Update) appears; "Perbarui" makes the path point to B; "Biarkan" changes nothing.
- [ ] Another exe is already registered (e.g. from a development script, or another portable copy that still exists): `Daftarkan` (Register) asks for confirmation; "Batal" (Cancel) changes nothing; "Ganti" (Replace) overwrites (an exe without a marker gets a warning).
- [ ] Single-instance: portable and installed copies can run at the same time. Opening `.md` through the portable copy is not forwarded to the installed instance, and the reverse. Two portable copies in different folders do not forward to each other either.
- [ ] Help > About Makdon shows the version, the mode (Installed/Portable), and the MIT license; "Buka Halaman Rilis" (Open Release Page) opens the browser.

**Print Preview on publish output**
- [ ] Print Preview (Ctrl+Shift+P) on the publish output shows text pages with fonts. `Makdon.runtimeconfig.json` contains `Switch.System.Windows.DisableXpsPackageBoundaryRestriction: true` (ADR-32). Without this switch the preview fails; this test is the main check after a .NET upgrade.

**Light accessibility**
- [ ] Keyboard navigation (Tab/Ctrl+Tab), visible focus, control names read by a screen reader (the `AutomationProperties.Name` property exists in XAML; its quality is not verified).
