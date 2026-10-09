# Contribution Guide

**Purpose:** how to set up the environment, build/test/publish Makdon, code conventions, how to write tests, how to add common things (themes, commands, view modes, extensions, toolbar formats), the PR checklist, and the project prohibitions.
**Audience:** new contributors and AI agents that change code. The official project rules are in [../CLAUDE.md](../CLAUDE.md); this document summarizes them and adds practical steps. Architecture overview: [ARCHITECTURE.md](ARCHITECTURE.md). Testing: [TESTING.md](TESTING.md).

## 1. Setup

- Windows (WPF) and the **.NET 10 SDK** (README: "Requires the **.NET 10 SDK**"). No other tool dependencies are needed for build/test; NuGet packages are restored automatically by `dotnet build`. The project targets `net10.0-windows` (`src/Makdon/Makdon.csproj:5`), so it cannot be built on Linux/macOS.
- CI runs only for releases (`.github/workflows/release.yml`, triggered by a push to the `build` branch); there is no per-PR CI. There is no analyzer or `.editorconfig`; the current quality guards are the "0 warnings" rule and the tests (see section 6).

```powershell
dotnet build Makdon.sln                   # must be 0 warnings, 0 errors
dotnet test src/Makdon.Tests              # xUnit; all must pass
dotnet run --project src/Makdon -- file.md
dotnet publish src/Makdon -p:PublishProfile=win-x64
```

Publish output: `src\Makdon\bin\Release\net10.0-windows\win-x64\publish\Makdon.exe` (self-contained: the runtime is bundled, so .NET does not need to be installed on the target machine). The publish profile is at `src/Makdon/Properties/PublishProfiles/win-x64.pubxml`. `RuntimeIdentifier` is not forced in `Makdon.csproj`, so ordinary build/test stays neutral (comment at `Makdon.csproj:15-18`).
The `<Version>` property is currently `0.1.0` (`Makdon.csproj:12`), the same as the initial release in [../CHANGELOG.md](../CHANGELOG.md). Change both together when releasing.

## 2. Folder structure

```text
Makdon.sln
CLAUDE.md, README.md, CHANGELOG.md
docs/                       dokumentasi pengembangan (indeks: docs/README.md)
scripts/                    register/unregister-file-association.ps1 (HKCU), build-release.ps1, generate-icon.ps1
installer/                  Makdon.iss (Inno Setup 6), Languages/Indonesian.isl
.github/workflows/          release.yml (rilis pada push ke branch build)
LICENSE, THIRD-PARTY-NOTICES.txt   lisensi MIT dan pemberitahuan pihak ketiga (ikut dalam setiap rilis)
.claude/agents/             sub-agent proyek (kuli, tyas, kurang-kerjaan, pak-bos)
src/Makdon/                 aplikasi WPF
    App.xaml(.cs)           titik masuk, single-instance, penangan galat global
    MainWindow.xaml(.cs)    tab, dialog, sesi, menu, ekspor, cetak
    DocumentTab.cs          model dokumen (teks, path, encoding, watcher, simpan)
    DocumentView.xaml(.cs)  editor + pratinjau + sinkron scroll
    FindReplaceBar.xaml(.cs), SearchEngine.cs, MarkdownEditing.cs
    TextFileIO.cs, FileStamp.cs, AppSettings.cs, SingleInstance.cs, CrashLog.cs
    AppPaths.cs, AppInfo.cs, InstallerMutex.cs, RegistryStore.cs, FileAssociation.cs   mode/lokasi data, identitas, mutex installer, "Buka dengan"
    MarkdownSupport.cs, AnchorHeadingRenderer.cs, HtmlExporter.cs
    PrintLayout.cs          PageLayout, PrintSnapshot, PrintSource, PrintService, enum kertas/orientasi/margin
    HeaderFooterPaginator.cs, PreviewBuild.cs, PrintPreviewWindow.xaml(.cs)   Pratinjau Cetak
    Theming.cs, EditorTheme.cs, Themes/{Light,Dark,Controls,Preview}.xaml
    AppCommands.cs, ChoiceDialog.xaml(.cs), Converters.cs, ViewModeConverter.cs, NotNullConverter.cs
    ZoomLevel.cs, TextStats.cs, EncodingNames.cs, MarkdownFiles.cs, Assets/app.ico
src/Makdon.Tests/           xUnit
    Support/                WpfHost.cs (+ DispatcherErrors, FailOnUnexpectedDispatcherErrorsAttribute), AssemblyInfo.cs,
                            PrintTestKit.cs, TempDir.cs, TestLogRedirect.cs, FakeRegistryStore.cs
    *Tests.cs               peta berkas -> area ada di TESTING.md
```

`bin/`, `obj/`, `.vs/`, `*.user`, `TestResults/` are ignored by git (`.gitignore`).

## 3. Code and language conventions

From CLAUDE.md, supplemented by the patterns consistently visible in the code:

- **Language:** UI text, error messages, and code comments are in **Indonesian**; identifier names are in **English**. Documentation (`README.md`, `CLAUDE.md`, `docs/`) is in **English**; `README.id.md` is the Indonesian version of `README.md`, and both must be updated together.
- **Nullable and ImplicitUsings are enabled**; the build must not produce warnings (`System.IO` is added via `<Using>` in the csproj).
- **Comments explain the reason (why)**, not repeat the code. Example style: the comments above non-obvious decisions such as `DocumentTab.cs:33-36` and `TextFileIO.cs:145`.
- **Do not add features beyond the request.** Findings outside the task are reported, not fixed along the way.
- **Catch specific exceptions**, not a bare `catch (Exception)`: `catch (Exception ex) when (ex is IOException or
  UnauthorizedAccessException ...)`. Recoverable I/O errors are shown to the user; unexpected errors are logged to `CrashLog`.
  The deliberate exceptions (HTML export and print catch everything except OOM; `PreviewBuild.Guard` catches everything **including** OOM,
  [ADR-25](DESIGN-DECISIONS.md#adr-25-preview-errors-wrapped-in-guard-and-xps-package-cleaned-up-after-idle)) have comments explaining them.
- **Low-level helpers that "never throw"** (`AppSettings.Load/Save`, `CrashLog.Write`, `SingleInstance`) state this in their XML summary; keep that property when changing them.
- Visible style: file-scoped namespaces (`namespace Makdon;`), 4-space indentation, private fields without a `_` prefix, classes `sealed`/`static` where possible, `internal` for anything that does not need to be public (visible to tests through `InternalsVisibleTo`).
  There is no written rule; follow the files around the change.
- **File saving** always goes through `TextFileIO.Write` (atomic, keeps encoding/BOM). **Document opening** only through `MainWindow.OpenFile`.
- **Preview vs export** use different pipelines (`MarkdownSupport.Pipeline` vs `ExportPipeline` + `SanitizeForExport`).
- Commit messages: there is no written convention; the existing history is written in Indonesian. Do not commit/push unless asked.

## 4. Writing tests

Framework: xUnit 2.9.2 + `Microsoft.NET.Test.Sdk` 17.12.0 + coverlet.collector (`Makdon.Tests.csproj`), target
`net10.0-windows`, `UseWPF`. `Xunit` and `System.IO` are already global `<Using>`s.

### 4.1 Patterns in use

| Need | Use | Notes |
| --- | --- | --- |
| Touching WPF (`DocumentTab`, `DocumentView`, `FindReplaceBar`, `ChoiceDialog`, `ThemeManager.Apply`, `TextEditor`; print types: `PrintPreviewWindow`, `PreviewBuild`, `HeaderFooterPaginator`, the `FlowDocument` returned by `PrintService.CreateDocument`, `DocumentViewer`, `FixedDocumentSequence`, `PrintTicket`) | `WpfHost.Instance.Run(...)` + `[Collection("Wpf")]` on the class | `WpfHost` = one STA thread with a `Dispatcher` and one `App` (`ShutdownMode.OnExplicitShutdown`, theme resources loaded, `OnStartup` not called). The `Wpf` collection disables parallelization (`MarkdownEditingTests.cs:942`). `DocumentTab` captures `Dispatcher.CurrentDispatcher`, so **create it inside `Run`**; `PreviewBuild` as well (`Dispatcher.CurrentDispatcher` in its field). Pure non-WPF types (`PageLayout.For`/`MarginOf`/`FromPrintableArea`, `PrintPreviewWindow.TicketMatches`/`ApplyTicket` on a separate `PrintTicket`) do not need `Run`, but classes that load them in this repo still use `[Collection("Wpf")]`. |
| Print tests: sample documents, small pages, waiting for stages, reading XPS page text | `Support/PrintTestKit` (`using static Makdon.Tests.Support.PrintTestKit;`) | `Small` (360 x 420, margin 24), `Sample`, `Paragraphs(n)` (many `Small` pages), `Pages(n)` (many A4 pages), `StartBuild`, `WaitForEnd`/`WaitForPaginated` (use `UiPump.Until` with a `Patience` limit of 15 s), `GlyphTexts` (XPS page text), `FooterTexts` (page footer text), `Paginator`. |
| Testing that a dispatcher callback throws (an expected error) | `using (var scope = WpfHost.ExpectUnhandled()) { ... }`, then check `scope.Errors` | Without a scope, an error that reaches the dispatcher fails the test through `[assembly: FailOnUnexpectedDispatcherErrors]` (`Support/AssemblyInfo.cs`). To assert that "no error escaped": `var before = WpfHost.Unhandled.Count; ...; Assert.Equal(before, WpfHost.Unhandled.Count);`. See [TESTING.md](TESTING.md#sta-thread-and-wpfhost). |
| Temporary files/folders | `TempDir` (`new TempDir()`, `File`, `WriteText`, `WriteBytes`, `Entries`; `Dispose` deletes) | Location `%TEMP%\Makdon.Tests\<guid>`. Use `Entries()` to assert that no `~md*.tmp` leftovers remain. |
| Redirecting `crash.log` | `TestLogRedirect` (`[ModuleInitializer]`) | Applies automatically to the whole test assembly; no need to call it. Test log: `%TEMP%\Makdon.Tests\crash-<pid>.log`. |
| Waiting for async events/timers inside one STA test | `UiPump.For(TimeSpan)` / `UiPump.Until(cond, timeout)` | Defined in `DocumentViewLifecycleTests.cs:14-41`; pumps the dispatcher (`DispatcherFrame`). `UiPump.IsTimerEnabled(owner, "fieldName")` reads **private fields** through reflection (`statsTimer`, `renderTimer`, `queryTimer`, `refreshTimer`): do not rename those fields without updating the tests. |
| Single instance | `SingleInstance.Create("test-" + Guid.NewGuid().ToString("N"))` | A unique scope so the mutex/pipe does not collide with the real application or other tests. |
| Settings | `AppSettings.Load(path)`, `Save(path)`, `SaveMerged(path)` with a path in `TempDir` | **Do not** use `AppSettings.DefaultPath`/`Load()` without arguments. |

Skeleton of a test that touches WPF (pattern from `DocumentTabTests.cs:13-47`):

```csharp
[Collection("Wpf")]
public class ContohTests : IDisposable
{
    readonly TempDir dir = new();
    readonly List<DocumentTab> tabs = [];

    public void Dispose()
    {
        WpfHost.Instance.Run(() => { foreach (var t in tabs) t.Dispose(); });
        tabs.Clear();
        GC.Collect();                 // BitmapImage holds the image file handle until GC
        GC.WaitForPendingFinalizers();
        dir.Dispose();
    }

    [Fact]
    public void Contoh()
    {
        var path = dir.WriteText("a.md", "lama");

        WpfHost.Instance.Run(() =>
        {
            var tab = DocumentTab.Load(path);
            tabs.Add(tab);
            tab.Document.Insert(0, "x");
            Assert.True(tab.IsDirty);
        });
    }
}
```

### 4.2 Rules

1. **Do not touch the real user data.** Tests must not read/write `%APPDATA%`, `%LOCALAPPDATA%` (except through
   `TestLogRedirect`), or the registry, and must not run real registry scripts. Exception: **reading**
   `HKCU\...\Personalize\AppsUseLightTheme` for the theme is allowed. Four tests do it through
   `ThemeManager.SystemUsesLightTheme()`: `SystemUsesLightTheme_ReadsRegistryWithoutThrowing` (`SmallUtilityTests.cs:241`) and three
   `ThemeManagerApplyTests` tests that call `ThemeManager.Apply` (`DocumentTabTests.cs:1060, 1097, 1117`; `Apply` always reads that value,
   whereas `Shutdown_WithoutApply_OrTwice_DoesNotThrow` does not); none writes. One test deliberately compares the real `crash.log` size before and after to prove it is untouched (`IoAndUtilityCoverageTests.cs:647`).
2. **Global static state must be restored** in `finally`: `DocumentView.BlockRemoteImages`, `ThemeManager` (call `Apply(Light)`
   and `Shutdown()`), `CrashLog.LogPath`, `CultureInfo.CurrentCulture`. Classes in different collections may run in parallel.
3. **Avoid `Thread.Sleep` and fixed delays.** Choose deterministic sequences: call `CheckExternalChange()` directly (not wait for the
   `FileSystemWatcher` + 400 ms timer), call `RefreshPreview()` explicitly, use `UiPump.Until(condition, timeout)` with an upper bound,
   or `BlockingCollection.TryTake(timeout)` / `Task.WaitAsync`. Honest about the current state: there are still fixed delays in
   `UiPump.For(...)` (7 calls in `DocumentViewLifecycleTests.cs`, used to prove "nothing happens after the debounce"; the print tests add 41 calls: 17 in `PreviewBuildTests`, 19 in `PrintPreviewWindowBehaviorTests`, 3 in `PrintContentAndCommandTests`, 1 in `PrintPreviewTests`, 1 in `WpfHostErrorTrackingTests`, mostly to give time to leftover callbacks after `Dispose` or to the image loader), and `Thread.Sleep` in 4 places in `SingleInstanceServerTests.cs` and 1 in `IoAndUtilityCoverageTests.cs:108` (polling with a 5-second limit). Those are exceptions; do not add new ones when a deterministic option exists.
4. **Test behavior, not implementation** (guidance of the `kurang-kerjaan` agent): cover the normal path, edge cases, and error paths (file locked with `FileShare.None`, missing folder, a directory as target, empty/null input).
5. **Types the tests need must be reachable:** `internal` is visible because of `InternalsVisibleTo("Makdon.Tests")` (`AssemblyInfo.cs:4`). XAML `x:Name`s (e.g. `FindBox`, `CountText`, `ReplaceRow`, `ButtonPanel`, `MessageText`) are used by tests; renaming or removing them affects tests. The Print Preview window is the same: tests find `Viewer`, `PageBox`, `PrintButton`, `BusyPanel`, `BusyText`,
   `BusyDetail`, `FooterCheck`, and `*Button` (orientation/paper/margin/navigation/zoom) through `FindName`. Some print tests also read **private** members of `PreviewBuild` through reflection (`counter`, `packageUri`, `cleanupScheduled`, method `Cleanup`;
   `PreviewBuildTests.cs:15-16, 430, 544-545, 590, 601`) and read the text of `MainWindow.xaml` with a regex (`PrintContentAndCommandTests.cs:333, 369`).
6. **Symlink/ACL tests:** a symlink-outside test silently returns if the machine is not allowed to create symlinks; the `WriteInPlace` fallback test uses an ACL deny on `CreateFiles` on the temp folder and restores it in `finally` (no admin rights needed).
7. **A test that has never been run is considered unfinished** (guidance of `kurang-kerjaan`). Run `dotnet test` before a PR.
8. **Print tests use no physical printer and no real dialog.** Do not open `PrintDialog`, `MessageBox`, or `ChoiceDialog` from a test (a modal dialog hangs the test process), and do not print to a printer or to "Microsoft Print to PDF". The print path in `PrintPreviewWindow` is tested through the hook `ShowPrintDialogForTests` (returns `false` = cancel; `PrintPreviewWindowBehaviorTests.cs:889, 921`); paper/orientation logic is tested through the static helpers `TicketMatches`/`ApplyTicket`/`DescribeTicket` on a self-built `PrintTicket`. Render failures are forced through `DocumentView.RenderFaultForTests` (**reset it to `null` in `finally`**). A test that expects the dispatcher callback to throw wraps it with `WpfHost.ExpectUnhandled()`. Wait for conditions with `UiPump.Until(cond, PrintTestKit.Patience)`, not fixed delays. The pattern used by existing print tests: the window is closed in the test class's `Dispose`, and `PreviewBuild` is disposed, so XPS packages and timers do not leak into the next test.
9. **A print document must not contain local paths.** The print path must not use `CreateErrorDocument` (it includes `CrashLog.LogPath`); a render failure must throw (`throwOnFailure: true`) and be shown on screen. The page footer holds only the file name. See [SECURITY.md](SECURITY.md#212-print-preview-and-print).
10. **Data locations and the registry go through seams.** `settings.json` and `crash.log` are accessed only through `AppPaths`; tests inject a fake folder through `AppPaths.Detect(folder, markerExists: ...)`. `FileAssociation` writes only through `IRegistryStore`; tests use `FakeRegistryStore`. Do not call `Environment.GetFolderPath` or `Microsoft.Win32.Registry` directly from code under test.
11. **The WPF test host does not run `App.OnStartup`.** Do not replace `WpfHost.TestApp` with a regular `App`: a real startup creates the production mutex/pipe, `MainWindow`, and reads the user's settings ([ADR-33](DESIGN-DECISIONS.md#adr-33-wpfhost-uses-testapp-without-onstartup)).

## 5. How to add things

### Adding a theme/brush key

Semantic brushes are in `Themes/Light.xaml` and `Themes/Dark.xaml` (43 keys each at present).

1. Add `<SolidColorBrush x:Key="NamaBrush" Color="#RRGGBB" />` to **both** files with the **exact same** key (comment in
   both files: "Kunci harus sama persis" = keys must match exactly). A key that exists in only one dictionary fails at runtime in the other theme, including when printing (printing loads `Light.xaml` through `ThemeManager.LoadDictionary(dark: false)`).
2. Use it in XAML with `{DynamicResource NamaBrush}` (not `StaticResource`: it does not change with the theme).
3. In code, get it through `TryFindResource`/`SetResourceReference` (examples: `DocumentView.ConfigureEditorAppearance`,
   `FindReplaceBar` -> `SearchMatchBrush`/`SearchCurrentBrush`). AvalonEdit properties that are not regular dependency properties must be reapplied in `DocumentView.RefreshTheme` (called by `MainWindow.OnThemeChanged`).
4. New Markdown syntax color: add a `Syntax*Brush`, then one line `Set(definition, "NamaWarnaDiDefinisi", "Syntax*Brush", ...)` in `EditorTheme.ApplyMarkdownHighlighting` (`EditorTheme.cs:33-38`). All highlight colors are automatically kept at contrast >= 4.5:1 (`EnsureContrast`); other text colors in the theme dictionary should preferably meet the same ratio (dictionary comment).
5. **Check that the keys match** (there is no automated test for this; the command below was tried in Git Bash and produced an empty diff at the time):

   ```bash
   diff <(grep -o 'x:Key="[^"]*"' src/Makdon/Themes/Light.xaml) <(grep -o 'x:Key="[^"]*"' src/Makdon/Themes/Dark.xaml) && echo SAME
   ```

6. Preview: the Markdig.Wpf `Styles.*` are overridden in `Themes/Preview.xaml` (colors through the `Preview*` brushes). Control styles are in `Controls.xaml`.

Adding a **third theme** (beyond Light/Dark) is not just adding a file: `ThemeManager.LoadDictionary(bool dark)`,
`ResolveIsDark` (boolean result), `IsDark`, `UpdateThemeChecks`, the Theme menu item in `MainWindow.xaml`, and `AppThemeMode` all assume two dictionaries. It needs a redesign and new tests (`ThemeManagerPureTests`, `ThemeManagerApplyTests`, `AppSettingsTests`).

### Adding a command/shortcut

1. `AppCommands.cs`: add a `public static readonly RoutedUICommand` through `Create(text, name, gestures...)`. Gestures go into the menu automatically as shortcut text. (Built-in `ApplicationCommands.*` do not need to be recreated.)
2. `MainWindow.xaml`: add `<CommandBinding Command="{x:Static local:AppCommands.NamaBaru}" Executed="..." CanExecute="..."/>`
   in `Window.CommandBindings`. `HasTab_CanExecute` (needs a tab), `CanEdit_CanExecute` (editor visible), and `CanFormat_CanExecute`
   (editor visible and focus not in the find panel) already exist.
3. Write the handler in `MainWindow.xaml.cs`. If it touches the document, go through `Current` (`DocumentTab`) / `Current.View`.
4. Add a `MenuItem Command="..."` (and a toolbar button if needed). A shortcut defined through `Window.InputBindings`
   (not `AppCommands`) does not appear automatically in the menu; fill in `InputGestureText` manually (examples: Ctrl+W, Ctrl+Shift+S).
5. Check for conflicts with built-in WPF/AvalonEdit shortcuts and with the shortcut table in [../README.md](../README.md); update the README. Duplicate shortcuts across `AppCommands`, standard `CommandBinding`s, and `KeyBinding`s in `MainWindow.xaml` are caught automatically by `NoTwoAppCommands_ShareTheSameKeyGesture` and `MainWindowShortcuts_AreUnique_*` (`PrintContentAndCommandTests.cs:319, 333`).
6. If showing a dialog, do not do it from an event; use `ChoiceDialog` and, for conflicts, `conflictQueue` (see the prohibitions below).

### Adding a view mode

The current mode is `ViewMode { Edit, Split, Preview }` (`DocumentTab.cs:8`). Places to touch:

- `DocumentView.ApplyMode`, `PreviewVisible`, and all branches `tab.Mode == ViewMode.Preview/Split/Edit` (`ShowFind`, `FindNext`,
  `ApplyFormat`, scroll sync `OnEditorScroll`/`OnPreviewScroll`, `ApplyParsed`).
- `MainWindow.xaml`: `RoutedUICommand` + `CommandBinding` + `KeyBinding` (Ctrl+1/2/3 currently) + menu item + `RadioButton`
  segmented control (`ViewModeConverter` with `ConverterParameter` = the enum name).
- `MainWindow.ViewMode_Executed` maps the **text** of the `RoutedUICommand` ("Editor", "Pratinjau", otherwise Split)
  (`MainWindow.xaml.cs:795-804`): add the new branch, or change it to an explicit mapping.
- `ViewModeLabelConverter`, `EditorVisibleConverter` (`Converters.cs`), `CanEdit_CanExecute`/`CanFormat_CanExecute`.
- Sessions: `SessionTab.Mode` is stored as the enum name text, and `ParsedMode` falls back to `Split` for unknown values
  (`AppSettings.cs:15`). **Renaming an existing enum member makes old sessions fall back to Split.**
- Tests: `ConverterTests` (`SmallUtilityTests.cs:393`), `SessionTab_ParsedMode_FallsBackToSplit` (`AppSettingsTests.cs:248`),
  `DocumentViewLifecycleTests`. README (features and shortcuts).

### Adding a Markdown file extension

1. `MarkdownFiles.Extensions` (`MarkdownFiles.cs:5`): used by drag-and-drop and relative-link clicks. Currently `.md .markdown .mdown
   .mkd .txt`.
2. The `OpenFilter`/`SaveFilter` dialog filters in `MainWindow.xaml.cs:16-17` (separate strings; `.txt` has its own filter).
3. File association: the defaults of `-Extensions` in `scripts/register-file-association.ps1` and `unregister-file-association.ps1` are
   `.md` and `.markdown`; `-Extensions` is normalized to lowercase and validated with `^\.[a-z0-9]+$`.
4. Test: `MarkdownFilesTests.IsMarkdown_ByExtension` (`HtmlAndMarkdownSupportTests.cs:9-31`). README (file association).

### Adding a toolbar format

1. `MarkdownEditing.cs`: add a `MarkdownFormat` member (`:7`), a function on `TextDocument` wrapped in
   `document.BeginUpdate()/EndUpdate()` (so it is one Undo step) that returns the resulting `SelectionRange`, and a branch in
   `MarkdownEditing.Apply` (`:34-45`).
2. `AppCommands.cs`: the new command and a branch in `AppCommands.FormatOf`.
3. `MainWindow.xaml`: a `CommandBinding` (use `Format_Executed` and `CanFormat_CanExecute`), a button in the `ToolBar` (the block visible
   while the editor is shown), and a menu item under Edit > Format. Headings use `CommandParameter` as the level.
4. Tests: `MarkdownEditingInlineTests/LineTests/LinkTests` (no UI, plain `TextDocument`) and `MarkdownEditingApplyTests`
   (`TextEditor` on `WpfHost`). README (features and shortcuts).

## 6. PR checklist

- [ ] `dotnet build Makdon.sln` -> **0 warnings, 0 errors**.
- [ ] `dotnet test src/Makdon.Tests` -> all green (run it for real; do not claim green without running).
- [ ] New tests for new behavior: normal path, edge cases, error paths. Do not touch the real `%APPDATA%`/registry/`crash.log`.
- [ ] Theme changes: Light keys = Dark keys (check with the command above); `DynamicResource`, not `StaticResource`.
- [ ] Flows that write files use `TextFileIO.Write`; flows that open documents use `MainWindow.OpenFile`.
- [ ] Export/preview: pipelines are not merged; new URLs go through `ClassifyUrl`; see [SECURITY.md](SECURITY.md).
- [ ] Print/Print Preview: no local paths or error documents on paper; print tests without a physical printer or real dialog (rules 8-9 in 4.2).
- [ ] UI text/messages/comments in Indonesian, identifiers in English; comments explain *why*.
- [ ] No features beyond the request; other findings are reported separately.
- [ ] Documentation updated when behavior changes: [../README.md](../README.md) (features, shortcuts, limitations) and `README.id.md` (updated together with `README.md`), [../CLAUDE.md](../CLAUDE.md)
  (when rules/structure change), documents in `docs/`, and [../CHANGELOG.md](../CHANGELOG.md).
- [ ] Identity changes (`AppId`, mutex name `Makdon.AppMutex`, registry table DISTRIBUTION 4.1, XPS switch) are changed together everywhere listed in CLAUDE.md. Changes to the installer or registry scripts are tested with `-WhatIf` or in a VM.
- [ ] No automatic commit/push; commit only when asked.

## 7. Prohibitions (from CLAUDE.md)

- Do not write document files directly; everything goes through `TextFileIO.Write` (atomic, keeps encoding/BOM).
- Do not merge `MarkdownSupport.Pipeline` (preview) with `ExportPipeline` + `SanitizeForExport` (HTML export must escape raw HTML and filter URLs).
- Do not loosen the image blocking in preview/print (`ResolveImageUrls`): only local files and `http(s)` may reach WPF; UNC, `ftp:`, other schemes, and `data:` are replaced with markers; `http(s)` follows the remote-blocking option.
- Do not embed local images in the export from outside the document folder; respect the 2 MB per image limit and the 30 MB budget; do not remove the OOM/error handling in `ExportHtml_Executed`.
- Do not create print documents that contain local paths or error documents (`CreateErrorDocument`); the print/Print Preview path throws (`throwOnFailure: true`). Do not print from anywhere other than `PrintService`/`PrintPreviewWindow`: the page size is set only by `PageLayout.Apply`, and the page footer only by `HeaderFooterPaginator`.
- Do not close the preview XPS package (`PreviewBuild.Cleanup`) synchronously from `Dispose`: wait for the writer to stop and the dispatcher to become idle.
- Do not open documents except through `MainWindow.OpenFile` (size check, OOM, duplicate tabs).
- Do not show `MessageBox`/conflict dialogs directly from an event; use `ChoiceDialog` and serialize through `conflictPromptOpen`/`conflictQueue`. `DocumentTab.SaveTo` postpones external checks while it runs; keep that.
- Do not let an instance started with arguments overwrite the saved session before the user opens a tab again (`OpenUserFile`).
- Do not add features beyond the request; the build must not produce warnings.
- Tests must not touch the user's `%APPDATA%`/registry/`crash.log`; use a unique scope with `SingleInstance.Create(scope)`.
- **Do not run real registry scripts** (`register/unregister-file-association.ps1`) without `-WhatIf` first; these scripts write to HKCU.
- Do not change the installer `AppId` or the mutex name `Makdon.AppMutex`, and do not remove the `DisableXpsPackageBoundaryRestriction` switch ([ADR-31](DESIGN-DECISIONS.md#adr-31-fixed-installer-mutex-name-makdonappmutex-not-restart-manager), [ADR-32](DESIGN-DECISIONS.md#adr-32-net-10-and-xps-switch-in-runtimeconfig)).
- Do not silently move portable data to `%APPDATA%`, and do not put the `Makdon.portable` marker in installer materials or the release folder ([ADR-28](DESIGN-DECISIONS.md#adr-28-portable-mode-via-makdonportable-marker-data-in-data)).
- Do not write to the registry from code outside `FileAssociation`/`IRegistryStore`.
- Do not commit or push unless asked.

## 8. Project sub-agents (`.claude/agents/`)

Four sub-agents are registered in the repo (`.md` files with frontmatter `name`, `description`, `tools`, `model`). The source of truth is the contents of that folder, not the list of agents in the client; if an agent does not appear in the list, check the folder.

| Agent | Model | Tools | When to use | Important constraints |
| --- | --- | --- | --- | --- |
| `kuli` | sonnet | Read, Edit, Write, Glob, Grep, Bash | Implementing a feature or fixing a bug whose spec is already clear, after the plan is approved; focused changes | Read the surrounding code first; limit to what was asked (other issues are reported); run build/test before finishing; do not commit/push/delete files |
| `tyas` | sonnet | Read, Edit, Glob, Grep, Bash | Investigating errors, failing tests, or unexpected behavior (a stack trace exists, a test is red, the cause is unknown) | Find the root cause, not the symptom: reproduce -> hypothesis -> minimal fix -> rerun; mark the parts that are still assumptions |
| `kurang-kerjaan` | sonnet | Read, Edit, Write, Glob, Grep, Bash | Writing/fixing tests for existing or newly changed code | Follow the project's test patterns; run the tests you write; **does not change production code** (bugs are reported) |
| `pak-bos` | opus | Read, Glob, Grep, Bash | Proactively reviewing diffs/changes after implementation: correctness, regressions, test gaps, basic security | Read-only (no Edit/Write); findings ordered by severity with `file:line`, failure scenario, and suggestion; no style comments that do not violate conventions |

A reasonable flow (a suggestion, not a repo rule): `kuli` implements -> `kurang-kerjaan` adds tests -> `pak-bos` reviews the
`git diff`; if there is a red test or an error that is not understood, `tyas` investigates. Each agent reports its verification results as they are, including failures.

## 9. Making a release

A release is triggered by a push to the `build` branch; the full design is in [DISTRIBUTION.md](DISTRIBUTION.md) §8. The version is taken from `<Version>`, the `v<versi>` tag
is created automatically, and the run fails if that version has already been released.

1. Raise `<Version>` in `src/Makdon/Makdon.csproj` (single source; the script and CI read it) and move the `[Unreleased]` entry in
   [../CHANGELOG.md](../CHANGELOG.md) to the new version and date.
2. Build locally with `powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1`. The script builds (0 warnings), tests, publishes the
   `win-x64` profile, assembles the release contents, builds the installer, creates the portable zip, and then `SHA256SUMS.txt`. Each run deletes `artifacts\<versi>\` first.
   - Without Inno Setup 6 installed, the installer is skipped with a warning. Use `-IsccPath` for the location of `ISCC.exe`, and `-RequireInstaller` to fail if the installer is not produced.
   - `-SkipTests` is only for quick trials; do not use it for a release.
   - `-VerifyInstallerContents` installs the installer into a temporary folder and uninstalls it, and writes a temporary HKCU entry. Outside CI (`GITHUB_ACTIONS=true`) the script refuses it unless `-Force` is given, and it refuses if the Makdon uninstall key already exists (`scripts/build-release.ps1:174-188`).
3. Output in `artifacts\<versi>\`: `Makdon-<versi>-setup-x64.exe`, `Makdon-<versi>-portable-x64.zip`, `SHA256SUMS.txt`.
4. Test the installer and zip with the "Distribution" checklist in [TESTING.md](TESTING.md#pre-release-manual-test-checklist).
5. Commit the version and CHANGELOG changes (only if asked), then push that commit to the `build` branch (e.g. `git push origin main:build`). The
   `release.yml` workflow creates the `v<versi>` tag on that commit, creates a draft release, uploads the three assets, and publishes them. A published release must not be replaced (immutable releases, **not verified** in the repo); if something is wrong, publish a new version.
