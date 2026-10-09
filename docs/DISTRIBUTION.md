# Distribution: installer, portable, and "Open with"

**Status:** implemented (2026-10-08, not yet committed). The decisions below remain the reference; deviations and implementation findings are recorded in section 13. What has **not** been proven (the installer has never been compiled, `release.yml` has never been run, installation has not been tested on Windows 10/11) is marked "not verified". **Readers:** developers and reviewers working on the public release.
References: architecture in [ARCHITECTURE.md](ARCHITECTURE.md), security in [SECURITY.md](SECURITY.md), manual tests in [TESTING.md](TESTING.md#pre-release-manual-test-checklist), how to make a release in [CONTRIBUTING.md](CONTRIBUTING.md#9-making-a-release).

## 1. Decisions

| # | Topic | Decision |
| --- | --- | --- |
| 1 | Target users | Public |
| 2 | Runtime | Self-contained (users do not need to install .NET) |
| 3 | Portable version | Yes, alongside the installer |
| 4 | Explorer integration | Only the **Open with** list entry; no top-level item in the Windows 11 menu (so **no MSIX / `IExplorerCommand`**) |
| 5 | Updates | Manual: download the new installer/zip; the application does not check for updates |
| 6 | Hosting | GitHub Releases (`github.com/hoaaah/Makdon`) |
| 7 | Code signing | Release unsigned for now (no budget); mitigations in §6 |
| 8 | Portable vs installed | Portable **refuses** to register "Open with" when an installed version exists (§4.3) |
| 9 | Uninstall | The user is asked before user data is deleted (§5.3) |
| 10 | Minimum Windows | Follows the stack: Windows 10 1607 (build 14393) x64 is enforced; details in §2.2 |
| 11 | ARM64, winget | Not now; on the roadmap (§10) |
| 12 | Target framework | **.NET 10 (LTS, `net10.0-windows`)**; applied. .NET 9 stops being supported on 2026-11-10 (§11) |
| 13 | Project license | **MIT** (§9) |

## 2. Release artifacts and platform

| Artifact | Contents | For |
| --- | --- | --- |
| `Makdon-<versi>-setup-x64.exe` | Inno Setup installer | Main path for general users |
| `Makdon-<versi>-portable-x64.zip` | Publish folder + `Makdon.portable` marker | No installation, no trace in `%APPDATA%` |
| `SHA256SUMS.txt` | Checksums of the two files above (`sha256sum` format) | Verifying downloads |

Each artifact also includes `LICENSE`, `THIRD-PARTY-NOTICES.txt`, `THIRD-PARTY-NOTICES-DOTNET.txt` (copy from the runtime package), and `THIRD-PARTY-NOTICES-WPF.txt` (§9).
Local and CI output goes to `artifacts/<versi>/` (in `.gitignore`), produced by `scripts/build-release.ps1`.

### 2.1 Publish shape

```powershell
dotnet publish src/Makdon -p:PublishProfile=win-x64
```

The profile `src/Makdon/Properties/PublishProfiles/win-x64.pubxml` contains `RuntimeIdentifier=win-x64`, `SelfContained=true`, `PublishSingleFile=false`, `PublishReadyToRun=false`, and `PublishTrimmed=false`. Result: `src\Makdon\bin\Release\net10.0-windows\win-x64\publish\`.

- **Folder, not single-file.** Single-file WPF still extracts native libraries to a temporary folder. With a folder: no trace in `%TEMP%` (important for portable), faster startup, and less suspicion from antivirus software. `IncludeNativeLibrariesForSelfExtract` (only meaningful for single-file) has been removed from `Makdon.csproj`, together with the comment about framework-dependent publish.
- **No trimming** (WPF does not support it).
- **ReadyToRun is off by default.** The framework assemblies in the runtime pack are already R2R; the benefit for Makdon/AvalonEdit/Markdig is presumed small. Not measured (**not verified**): measure cold startup before turning it on.
- **Actual sizes** (measured 2026-10-08 on a local publish, runtime `Microsoft.NETCore.App` 10.0.12): publish folder **155.5 MB** unpacked (258 files), portable zip **65.0 MB** (last `build-release.ps1` run, 1543 tests passed). The initial estimates (150-200 MB / 60-80 MB) turned out to be close. The installer uses `Compression=lzma2/ultra64` + `SolidCompression=yes`; installer size has not been measured.
- Publish settings are kept in `src/Makdon/Properties/PublishProfiles/win-x64.pubxml` so local builds and CI are identical.
- Version display: `IncludeSourceRevisionInInformationalVersion=false` (`src/Makdon/Makdon.csproj:22`), so the About dialog shows `0.1.0`, not `0.1.0+<hash>`.

### 2.2 Windows support

According to the official .NET 10 documentation (same as .NET 9), Windows 11 23H2 and later are supported, while Windows 10 support is limited to the LTSC/Enterprise editions (1607, 1809, 21H2). Windows 10 22H2 consumer editions have already reached end of support (2025-10-14). Sources:
[Install .NET on Windows](https://learn.microsoft.com/en-us/dotnet/core/install/windows),
[.NET 9 supported-os](https://github.com/dotnet/core/blob/main/release-notes/9.0/supported-os.md) (for .NET 10 this has not been checked, **not verified**).

| Level | Windows |
| --- | --- |
| Supported (tested on devices: **not verified**, design) | Windows 11 23H2 and later, x64 (also Windows 11 Arm64 through x64 emulation) |
| Runs, without official .NET support | Windows 10 22H2 x64; Windows 10 LTSC 2019/2021 |
| Refused by the installer | Below Windows 10 1607 (build 14393), Windows 7/8.1, 32-bit Windows |

- Installer: `MinVersion=10.0.14393`, `ArchitecturesAllowed=x64compatible`, `ArchitecturesInstallIn64BitMode=x64compatible` (`installer/Makdon.iss:45-47`). 64-bit mode is required, because without it an all-users installation goes to `Program Files (x86)` and HKLM keys go to `WOW6432Node`.
- The portable zip cannot enforce the version: on an OS that is too old, the runtime fails before the application code runs. The OS requirements are only written in the README and the release notes.

## 3. Installed vs portable mode

One and the same exe; the mode is determined at startup by whether `Makdon.portable` exists next to `Makdon.exe` (`dotnet run` = installed).
Implementation: `AppPaths.Detect` (`src/Makdon/AppPaths.cs:60-65`); `AppPaths.Current` is filled from `Environment.ProcessPath` when the name is `Makdon.exe` (`AppPaths.cs:67-75`).

| Item | Installed | Portable |
| --- | --- | --- |
| `settings.json` | `%APPDATA%\Makdon\` (`AppPaths.cs:35-37`) | `<folder exe>\data\` (created when needed) |
| `crash.log` | `%LOCALAPPDATA%\Makdon\` (`AppPaths.cs:39-41`) | `<folder exe>\data\` |
| Registry | Written by the installer | Only if the user chooses "Register" (§4.3) |
| Single-instance | Empty scope (default mutex/pipe names) | Scope = hash of the exe folder path (`AppPaths.cs:47-55`, prefix `p`) |
| Installer mutex (§5.2) | Created (`InstallerMutex.cs:12-23`) | Not created |
| Registry check at startup (§4.3) | None | Present, after the window is shown (`MainWindow.xaml.cs:486-497`) |

- If the portable folder cannot be written to, the application runs with settings in memory and tells the user once (`AppPaths.IsDataDirectoryWritable`, `AppPaths.cs:81-96`; message in `MainWindow.xaml.cs:499-`). Do not silently move to `%APPDATA%`.
- **Single-instance scope for portable** (applied): previously the mutex/pipe names contained only the SID and session id (`SingleInstance.cs:43-44`, `61-62`). Without a different scope, a file opened in portable mode would be forwarded to the running installed instance, or the other way around.
  The `scope` parameter of `SingleInstance.Create` already existed and is now used by `App.xaml.cs:24`.
- **Code impact** (applied): `AppPaths` is used by `AppSettings.DefaultPath` (`src/Makdon/AppSettings.cs:59`) and `CrashLog.LogPath` (`src/Makdon/CrashLog.cs:25`). Tests inject a fake exe folder (`AppPaths.Detect` with `TempDir`), so they still do not touch `%APPDATA%`/`%LOCALAPPDATA%`.

## 4. "Open with" integration

### 4.1 Registry specification (single source of truth)

The contents are the same as those written by the script. The installer, the application feature, and the script must follow this table and be changed together. Implementation:
`FileAssociation.WriteRegistration` (`src/Makdon/FileAssociation.cs:243-271`), the `[Registry]` section of `installer/Makdon.iss`, and `scripts/register-file-association.ps1`.
`<root>` = `HKCU\Software\Classes` (per user) or `HKLM\Software\Classes` (all users).

| Key | Value |
| --- | --- |
| `<root>\Makdon.Markdown` | `(Default)`/`FriendlyTypeName` = `Dokumen Markdown` (Markdown document) |
| `<root>\Makdon.Markdown\DefaultIcon` | `"<exe>",0` |
| `<root>\Makdon.Markdown\shell\open` | `FriendlyAppName` = `Makdon`, `MUIVerb` = `Buka dengan Makdon` (Open with Makdon) |
| `<root>\Makdon.Markdown\shell\open\command` | `"<exe>" "%1"` |
| `<root>\Applications\Makdon.exe` (+ `DefaultIcon`, `shell\open\command`, `SupportedTypes\.md`, `.markdown`) | "Open with" list |
| `<root>\.md\OpenWithProgids`, `<root>\.markdown\OpenWithProgids` | Value `Makdon.Markdown` (empty) |
| `Software\Makdon\Capabilities` + `Software\RegisteredApplications` | Settings > Apps > Default apps |

It does **not** write the extension's default value and does not touch `UserChoice`; the user chooses themselves through "Choose another app > Always use this app".
`-SetDefault` is only available in the development script.

On Windows 11, Makdon appears in the **Open with** submenu of the compact menu, in the "Choose another app" dialog, and in Default apps. A separate "Open with Makdon" item only appears under "Show more options" (accepted, decision #4). This behavior is **not verified** on Windows 11, because the installer has not been tested.

### 4.2 By the installer

Inno writes the table in 4.1 with root `HKA` (HKCU for per-user, HKLM for all users) and `ChangesAssociations=yes`, and removes it on uninstall (`installer/Makdon.iss:86-113`):
- `.md\OpenWithProgids`: only the `Makdon.Markdown` value is deleted, not the key.
- `Software\Makdon`: `uninsdeletekeyifempty`, because the development script uses `Software\Makdon\PreviousDefault`.

### 4.3 By the application (portable mode)

The menu that only appears in portable mode: **File** (Berkas) > **Explorer Integration** (Integrasi Explorer) > `Daftarkan ke "Buka dengan"` (Register to "Open with") / `Cabut pendaftaran` (Unregister)
(`MainWindow.xaml`, item `ExplorerIntegrationItem`; handling in `MainWindow.xaml.cs:550-620`). The logic is in the `FileAssociation` class through the `IRegistryStore` interface (`src/Makdon/RegistryStore.cs:11-32`; the real implementation is `WindowsRegistryStore`, 64-bit view), so tests use an in-memory fake registry (`Support/FakeRegistryStore.cs`) and the rule "tests do not write the registry" still holds.

The **Help** (Bantuan) > **About Makdon...** (Tentang Makdon...) menu is also available in both modes: it shows the version, mode, license, and a link to the release page (`MainWindow.xaml.cs:621-`). Makdon does not contact the internet itself.

**Installation detection** (`FileAssociation.FindInstallation`, `FileAssociation.cs:106-125`). Reads
`Software\Microsoft\Windows\CurrentVersion\Uninstall\{<AppId>}_is1` in HKCU and HKLM (64-bit view, plus `WOW6432Node` just in case).
Takes the value `Inno Setup: App Path` (fallback: `InstallLocation`). An installation is considered present if the key exists **and** `<path>\Makdon.exe` exists.
`AppId` is a constant in the application code (`AppInfo.InstallerAppId`, `src/Makdon/AppInfo.cs:9`) and in `installer/Makdon.iss`.

**Rules for "Register"** (`FileAssociation.Register`, `FileAssociation.cs:189-210`; classification in `GetStatus`/`Classify`, `FileAssociation.cs:127-175`):
1. An installation exists (HKCU or HKLM) → **refuse** (`BlockedByInstallation`), with a message suggesting that the installed version be used or uninstalled first.
   An all-users installation is also refused, because the portable HKCU key would mask the HKLM key in the merged HKCR view.
2. No installation → read the command in `HKCU\...\Makdon.Markdown\shell\open\command` and `Applications\Makdon.exe\shell\open\command`
   (first quoted token, `Path.GetFullPath`, compared with `OrdinalIgnoreCase`; `ExtractExePath`, `FileAssociation.cs:280-305`):
   - empty, or the referenced exe no longer exists (stale path) → write;
   - same as this exe → already registered (`AlreadyRegistered`); the menu shows "Unregister";
   - another portable exe that still exists (marker present) → ask first before replacing (`NeedsConfirmationOtherPortable`);
   - another exe without a marker (e.g. from a development script) → ask with a warning (`NeedsConfirmationOtherExe`).
3. Folder prefix comparison always uses a trailing separator (`FileAssociation.IsUnderFolder`, `FileAssociation.cs:315-319`): `C:\X\Makdon\` ≠ `C:\X\Makdon2\`.
4. A relative command (e.g. `"Makdon.exe" "%1"` without a full path) is not normalized against the working folder, because the exe cannot be identified with certainty. Such a command is classified as `OtherExe` (needs confirmation, is never offered an update, and "Unregister" does not remove it). An installation with a relative path is ignored (`ClassifyCommand`, `FileAssociation.cs:155-162`).
5. Network paths (UNC `\\server\share\...` and `\\?\UNC\...`) are not checked with `File.Exists`, because that could block the UI thread. Such a registration is classified as `OtherExe` (not stale), so startup does not offer an update, and an installation on UNC is ignored. `\\?\C:\...` is still treated as local (`IsNetworkPath`, `FileAssociation.cs:308-310`).
6. `Register` refuses with `RegisterResult.ExeNotFound` if the exe file is not named `Makdon.exe` or does not exist at its path, because Windows matches the `Applications\Makdon.exe` key by file name. Nothing is written, and MainWindow shows a message (`FileAssociation.cs:193-195`).

**"Unregister"** (`FileAssociation.Unregister`, `FileAssociation.cs:213-237`) only deletes keys whose command points to this exe.
The `Software\Makdon` key is deleted only if it is already empty.

**Startup checks (portable)** (`FileAssociation.CheckStartup`, `FileAssociation.cs:178-184`; run by `MainWindow.RunPortableStartupChecks`, `MainWindow.xaml.cs:499-`, after `ContentRendered` at `ApplicationIdle` priority):
- HKCU points to an exe that no longer exists → offer to update the path. *Small deviation:* the code treats **every** missing exe as stale, not only a missing portable one.
- An installation exists **and** HKCU points to this exe (portable was registered before the all-users installer was installed) → offer "Unregister portable registration". The installer for admins cannot reliably clean up the user's HKCU.
  *Unverified assumption, not verified:* `GetStatus` picks the most "foreign" condition of the two keys (ProgID and Applications). If the ProgID points to this exe but `Applications` points to another exe, the status is not `ThisExe` and the unregister offer does not appear.

### 4.4 Scripts

`scripts/register-file-association.ps1` and `unregister-file-association.ps1` remain as development tools; the default `-ExePath` path is updated to the `net10.0-windows` publish folder. The README explains the migration for those who used the scripts before: run `unregister-file-association.ps1` before installing with the installer.

## 5. Installer (Inno Setup 6)

File `installer/Makdon.iss`, Inno Setup 6.7.x (version pinned in CI). WiX/MSI is only considered if corporate deployment needs arise.
Called from `scripts/build-release.ps1` with `/DAppVersion`, `/DPublishDir`, and `/DOutputDir`. The version must match `<Version>`.
`installer/Makdon.iss` also refuses to compile if `PublishDir` contains `Makdon.portable` (`#if FileExists`).

### 5.1 Basics

- `PrivilegesRequired=lowest` + `PrivilegesRequiredOverridesAllowed=dialog`: default is **per user** to `%LOCALAPPDATA%\Programs\Makdon` without UAC; the option "all users" goes to `Program Files` (requires admin). Note in the `.iss`: the per-user program folder differs from the data folder `%LOCALAPPDATA%\Makdon`.
- `AppId={{2025C09D-1945-431D-BEBF-18C8EC3A9A78}` (the first curly brace is escaped; the uninstall key becomes `{2025C09D-...}_is1`). **Must never change**:
  this GUID is the identity for upgrade/uninstall and is used by the application to detect an installation (§4.3). Its value is kept the same in `installer/Makdon.iss:34`,
  `AppInfo.cs:9`, and `FileAssociation.cs:78-79`.
- Version from `<Version>` in `Makdon.csproj` via `iscc /DAppVersion=...`.
- `UsePreviousAppDir=yes`, `DisableDirPage=auto`. Start Menu shortcut; desktop icon optional (not checked by default).
- Installer languages: Indonesian and English. `Indonesian.isl` is **not bundled** with Inno Setup: a copy from an unofficial translation folder (Unofficial issrc, author "MozaikTM", updated for 6.5+) is stored in `installer/Languages/Indonesian.isl`. **Not verified**:
  completeness of the strings and translation quality; it has never been compiled.
- `LicenseFile=..\LICENSE` (MIT, §9).

### 5.2 Upgrade, downgrade, running application

- **Upgrade:** the new installer overwrites files through `[Files] ... ignoreversion recursesubdirs` (`installer/Makdon.iss:80`). **No `[InstallDelete]` with wildcards is used**, because if the user chooses a shared folder (e.g. `C:\Tools`), other files would be deleted too. Inno's uninstaller merges the file log across versions, so everything is still removed on uninstall. Leftover DLLs from the old version are presumed harmless for a self-contained application
  (what gets loaded follows `deps.json`); **not verified**: to be proven by a manual upgrade test.
- **Downgrade:** refused in `InitializeSetup` (`installer/Makdon.iss:144-156`) by comparing the `DisplayVersion` of the `_is1` key (HKCU and HKLM).
  The version is cleaned of its SemVer suffix (`CleanVersion`), then `StrToVersion`.
- **Changing mode** (per user ↔ all users) is on the manual test list, because it can result in two installations with the same AppId.
- **Running application:** `AppMutex=Makdon.AppMutex,Global\Makdon.AppMutex` (`installer/Makdon.iss:52`) is the main path. Both setup and the uninstaller check it and ask the user to close Makdon, so documents that have not been saved are still handled by the application's save dialog.
  - The application creates these two fixed-name mutexes (only in installed mode, outside `SingleInstance`; `InstallerMutex.cs`) and ignores errors when creating them.
    The mutex is held for the lifetime of the process. **Not verified**: the behavior of the installer's AppMutex check.
  - The old `AppMutex` cannot be used, because the single-instance mutex name contains the user id (`SingleInstance.cs:43`, `Local\Makdon.SingleInstance.<SID>`).
  - Restart Manager (`CloseApplications`) is not the main path: Makdon does not handle closing by Restart Manager, and `Window_Closing` can cancel the close (`src/Makdon/MainWindow.xaml.cs:876`).

### 5.3 Uninstall and user data

`CurUninstallStepChanged` at `usPostUninstall` (`installer/Makdon.iss:158-185`):

| Condition | Behavior |
| --- | --- |
| Per-user installation, interactive | Asks "Hapus juga pengaturan dan catatan galat Makdon?" (Also delete Makdon settings and error log?) (default **No**). Yes → deletes `%APPDATA%\Makdon` and `%LOCALAPPDATA%\Makdon` |
| All-users installation (admin) | **Does not ask and does not delete.** The constants `{userappdata}`/`{localappdata}` refer to the elevated admin account, which may differ from the logged-in user. Only informs that each user's settings remain. Never browse all profiles |
| `/SILENT`, `/VERYSILENT` | Does not ask, does not delete |
| Upgrade | The uninstaller does not run, so there is no question |
| Portable data (`<folder>\data\`) | Never touched by the uninstaller |

Question texts are provided through `[CustomMessages]` (id, en). `AppMutex` prevents a still-running application from rewriting `settings.json` (`Window_Closed`, `src/Makdon/MainWindow.xaml.cs:890`) after the data folder has been deleted.

## 6. Unsigned release: mitigations

Without a signature, users will see the SmartScreen warning ("Windows protected your PC") and the release is more likely to be flagged by antivirus software. Mitigations:

1. **Checksums:** `SHA256SUMS.txt` as an asset (created by `scripts/build-release.ps1`, step 7), and the same hash is written in the body of the release notes (`release.yml`) together with the verification command:
   `(Get-FileHash .\Makdon-x.y.z-setup-x64.exe -Algorithm SHA256).Hash`.
2. **Immutable releases** must be enabled in the repo settings: release assets cannot be changed after publication and automatically get release attestation
   (`gh release verify-asset`). The pipeline uploads to a draft first, then publishes. **Not verified**: the repo setting has not been checked, and no step in the repo ensures it.
3. **Artifact attestations** (`actions/attest-build-provenance`) to prove the artifacts were built by this repo's workflow from a specific commit.
   This is a complement, **not a replacement for a signature**: it has no effect on SmartScreen/antivirus. Free only for public repos.
4. **Instructions in the README / release notes** (already written in the [README](../README.md#smartscreen-and-smart-app-control-warnings)):
   - Installer (main path): click "More info > Run anyway". Files installed by the installer do not carry the Mark of the Web, so the installed `Makdon.exe` no longer triggers the warning. **Not verified** on a real Windows machine.
   - Portable zip: extracting in Explorer spreads the Mark of the Web to all files. Suggested order: verify the checksum first, **then** right-click the zip > Properties > Unblock (or `Unblock-File`), and then extract. Or accept the SmartScreen warning when running.
   - **Smart App Control** (Windows 11, active only on clean installations): an unsigned application can be blocked with no "Run anyway" button. The README states plainly that Makdon cannot be used on such a PC until a signed release exists.
     **Not verified** for unsigned DLLs inside the self-contained folder.
5. **Antivirus false positives:** submit to the Microsoft file submission portal (WDSI) for every release that gets flagged.

Signing has been moved to the roadmap (§10).

## 7. Releases, updates, and runtime support lifetime

- Channel: GitHub Releases containing the installer, portable zip, `SHA256SUMS.txt`, and release notes. The notes in `release.yml` currently contain only checksums, not the contents of `CHANGELOG.md` (**not verified** through an actual release).
- Updates: download and run the new installer (in-place upgrade), or overwrite the portable folder (the `data\` folder and `Makdon.portable` are kept; explained in the README).
- The application does **not** contact the internet. The About dialog shows the version and a link to the release page (`MainWindow.xaml.cs:621-`).
- SemVer numbering; a single source in `Makdon.csproj` (`<Version>`, `Makdon.csproj:12`); the git tag `v<versi>` is created automatically by the release workflow (§8).
- **Runtime policy:** because the .NET runtime is bundled, users run the bundled runtime until they update Makdon.
  A re-release (patch) is made when .NET issues a security patch relevant to WPF/runtime.

## 8. Release pipeline (GitHub Actions)

Workflow `.github/workflows/release.yml`, triggered by a **push to the `build` branch**, runner `windows-latest`, permissions `contents: write`, `id-token: write`,
`attestations: write`. The same steps can be run locally through `scripts/build-release.ps1` (see [CONTRIBUTING.md](CONTRIBUTING.md#9-making-a-release)).

1. The version is read from `<Version>` in `Makdon.csproj` (must be SemVer). Fails before the build if the tag `v<Version>` or a release with that name already exists,
   so every push to `build` that is meant to release must bump `<Version>`. Leftover drafts from failed runs are deleted and recreated. Runs are limited to one at a time (`concurrency: release`, without cancelling the one in progress).
2. `dotnet build` (0 warnings) + `dotnet test` (all green). WPF tests (`WpfHost`, STA) need to be tried on the runner; print tests already work without a printer.
   **Not verified**: WPF tests on the `windows-latest` runner.
3. `dotnet publish` with the `win-x64` profile.
4. Install Inno Setup with the pinned version (`INNO_SETUP_VERSION: '6.7.1'`, via Chocolatey), find `ISCC.exe`, then build the installer.
   The Chocolatey package `innosetup` version 6.7.1 has been verified to exist. The workflow installs it itself, so the runner image does not determine it. The Inno Setup installer itself is not checksum-verified (**not verified**).
5. Portable zip: publish folder + `Makdon.portable` + license. The `data\` folder is not included (the application creates it).
6. Automatic check: `Makdon.portable` **must not** be present in the installer contents (`build-release.ps1 -VerifyInstallerContents`: temporarily install and then uninstall, and write a temporary HKCU key). The script only runs it in CI (`GITHUB_ACTIONS=true`) or with `-Force`, and refuses if the Makdon uninstall key (`_is1`) already exists in HKCU or HKLM.
7. `SHA256SUMS.txt` → attestation → **draft release** (`--target` = the commit that was built) → upload assets → publish (immutable). The tag
   `v<versi>` is created on that commit when the release is published.
8. Anyone who can push to `build` can publish a release: protect the `build` branch (branch protection/ruleset) on GitHub (**not configured**).

Local output goes to `artifacts/<versi>/` (in `.gitignore`).

CI notes: actions are pinned to commit SHAs with a version comment: `actions/checkout` v4.4.0 (`11d5960a…`), `actions/setup-dotnet` v4.3.1 (`67a3573c…`), and `actions/attest-build-provenance` v2.4.0 (`e8998f94…`). The SHA values were matched with `git ls-remote`. Inno Setup is installed through Chocolatey version 6.7.1 (verified to exist), without checksum verification of the installer (**not verified**).

## 9. License

- Project: **MIT** (`LICENSE`, copyright holder Arief Wijaya, 2026). Copied into the repo, the publish folder, the installer (`LicenseFile`), and the zip.
- `THIRD-PARTY-NOTICES.txt` in the repo contains notices for: .NET runtime/WPF (MIT), AvalonEdit 6.3.1.120 (MIT), Markdig 0.22.0 (BSD-2-Clause),
  and Markdig.Wpf 0.5.0.1 (MIT). The Markdig 0.22.0 version matches `obj/project.assets.json` (checked 2026-10-08).
- **Not verified:** the AvalonEdit license text has not been compared with the upstream project's repo (NuGet packages do not include a LICENSE file), and the license texts of Markdig, Markdig.Wpf, and the copyright of each package were taken from the `.nuspec` metadata, not from files in the packages.
- `THIRD-PARTY-NOTICES-DOTNET.txt` is copied from the runtime package (`microsoft.netcore.app.runtime.win-x64`) by `build-release.ps1`; the script fails if that file does not exist.
- `THIRD-PARTY-NOTICES-WPF.txt` (in the repo root; taken from dotnet/wpf tag v10.0.12) is copied into the release folder, because the runtime package does not include WPF notices. The script fails if that file does not exist. **Not verified:** its contents have not been compared with the file at that tag.

## 10. Roadmap (beyond the first release)

| Item | Notes |
| --- | --- |
| Code signing | When there is budget: Azure Artifact Signing (eligibility for individuals in Indonesia **not verified**) or an OV certificate. Sign the exe, installer, and uninstaller (`SignTool` for Inno) |
| Native `win-arm64` build | Add a second artifact; Inno `ArchitecturesAllowed` is extended. Meanwhile Arm64 uses x64 emulation |
| winget | Submit the manifest after a stable release. The installer is already safe for `/VERYSILENT` (§5.3) |
| MSIX / top-level Windows 11 menu item | Only if requirement #4 changes |
| Re-checking action SHAs | When actions are updated: match the new SHA with its tag through `git ls-remote` (§8) |

## 11. Additional decisions (decided 2026-10-08)

1. **Move to .NET 10 (LTS) before the first public release: approved and applied.** .NET 9 stops being supported on **2026-11-10**, while
   .NET 10 is supported until November 2028. In a self-contained application, a runtime without security patches is shipped to users. The OS support
   of .NET 10 is the same as §2.2. Impact: `TargetFramework` → `net10.0-windows` (`src/Makdon/Makdon.csproj:5`, `src/Makdon.Tests/Makdon.Tests.csproj:4`),
   publish/script/document paths updated, and the full test suite re-run (1543 cases passed). Source: [.NET 8 & 9 end of support](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/).
2. **Project license: MIT** (consistent with the dependencies), copyright holder Arief Wijaya.

## 12. Impact summary and work order

| Area | Change (status) |
| --- | --- |
| Application code | `AppPaths`, `AppInfo`, `InstallerMutex`, `RegistryStore` (`IRegistryStore`), `FileAssociation`; `AppSettings`/`CrashLog` use `AppPaths`; portable single-instance scope; Explorer Integration menu and startup checks (portable); About dialog. **Applied.** |
| Project | `net10.0-windows`; `win-x64` publish profile; `IncludeNativeLibrariesForSelfExtract` removed; `IncludeSourceRevisionInInformationalVersion=false`; XPS switch (§13). **Applied.** |
| New | `installer/Makdon.iss`, `installer/Languages/Indonesian.isl`, `scripts/build-release.ps1`, `.github/workflows/release.yml`, `LICENSE`, `THIRD-PARTY-NOTICES.txt`, `.gitignore` for `artifacts/`. **Applied; installer not yet compiled.** |
| Tests | `AppPathsTests.cs` (`AppPaths`, `InstallerMutexTests`), `FileAssociationTests.cs` with `Support/FakeRegistryStore.cs`, `WpfHostStartupTests.cs` (TestApp, XPS switch). **Applied.** |
| Docs | README, ARCHITECTURE, SECURITY, TESTING, ADR, CONTRIBUTING, CHANGELOG. **Applied (2026-10-08).** |

Work order (initial decisions, recorded for the trail): 1) .NET 10 migration and license; 2) publish profile + `AppPaths` + single-instance scope;
3) `FileAssociation`; 4) installer + `AppMutex`; 5) release pipeline; 6) documents and manual tests on Windows 10/11 (manual tests **not yet done**).

## 13. Deviations and implementation findings (2026-10-08)

Recorded so that reviewers do not treat the design above as tested fact.

- **XPS switch (.NET 10).** .NET 10 introduces the XPS package boundary restriction: font resources of an XPS page may only be loaded from the same package. For the in-memory XPS package (Print Preview, `PreviewBuild`) the check also rejects fonts belonging to the package itself, so every page with text fails and
  Print Preview always fails. Fix: `RuntimeHostConfigurationOption Switch.System.Windows.DisableXpsPackageBoundaryRestriction=true` in
  `src/Makdon/Makdon.csproj:41` and `src/Makdon.Tests/Makdon.Tests.csproj:26`. It must be set through runtimeconfig: `AppContext.SetSwitch` in code is
  not enough, because WPF caches the switch when the first image is loaded. Why it is safe: Makdon only reads XPS it created itself. Alternative not chosen: writing the XPS to a temporary file. Residual risk: see [SECURITY.md](SECURITY.md) R14.
- **WPF host for tests (`WpfHost`).** Previously `WpfHost` created a regular `App` and called `InitializeComponent`. The `Application` constructor queues the `OnStartup` call to the dispatcher, so the real startup also ran in tests: the production single-instance mutex/pipe, `MainWindow`, and reading settings from `%APPDATA%`. Fix: `WpfHost.TestApp` with an empty `OnStartup` and `LoadAppXaml()`, which reads the BAML of `app.xaml` directly (`src/Makdon.Tests/Support/WpfHost.cs`, class `TestApp`). Guarded by `WpfHostStartupTests`.
- **Actual sizes** (see §2.1): 155.5 MB publish folder (258 files), 65.0 MB portable zip. Installer size has not been measured. The ReadyToRun effect has not been measured.
- **Installer has never been compiled.** Inno Setup is not installed on the developer machine. All claims about installer behavior (AppMutex,
  uninstall dialog, `InitializeSetup`, per-user/all-users installation) are **design claims that have not been verified**.
- **`release.yml` has never been run.** This includes the WPF tests on the runner, the Inno Setup installation through Chocolatey, and the draft/publish steps.
- **`Stale` classification deviation** (§4.3): every missing exe is treated as stale, not only portable ones.
- **Registration status edge case** (§4.3, unverified assumption): ProgID and Applications may point to different exes; the "most foreign" status hides the unregister offer.
- **Indonesian.isl** from an unofficial copy (see §5.1): its completeness is **not verified**.
- **Markdig version** in `THIRD-PARTY-NOTICES.txt` already matches the installed package (0.22.0); the license text per package is **not verified** (§9).
- **`build-release.ps1 -VerifyInstallerContents`** writes a temporary HKCU key while installing and uninstalling the installer. The script refuses it outside CI without `-Force`, and refuses if a Makdon installation already exists (`scripts/build-release.ps1:174-188`).
