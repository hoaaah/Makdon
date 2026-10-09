# Makdon development documentation

**Purpose:** index of the development documentation. **Audience:** developers, reviewers, and AI agents working in this repo.
User documentation (features, shortcuts, short build instructions) is in [../README.md](../README.md); project working rules are in
[../AGENTS.md](../AGENTS.md); release history is in [../CHANGELOG.md](../CHANGELOG.md).

| Document | Contents | Read when |
| --- | --- | --- |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Components and responsibilities, component diagram, sequence diagrams of the main flows (startup, open, render, save, external changes, export, sessions, Print Preview, XPS package lifecycle and preview errors), thread model, state model (including `PreviewBuild`) | You will change code or are looking for "where does X happen" |
| [DESIGN-DECISIONS.md](DESIGN-DECISIONS.md) | Decision records (context, decision, consequences, evidence) | You want to know *why* something was built this way before changing it |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Setup, build/test/publish, conventions, how to write tests, how to add themes/commands/modes/extensions/formats, PR checklist, prohibitions, sub-agents | You will contribute |
| [SECURITY.md](SECURITY.md) | Threat model, controls (code location + tests), residual risks and those already closed | You change export, image preview, single instance, scripts, or assess security |
| [DISTRIBUTION.md](DISTRIBUTION.md) | Distribution decisions and implementation: self-contained publish, Inno Setup installer, portable mode, "Open with" registration (registry table), signing, release pipeline; deviations and findings in section 13 | You work on a public release, the installer, portable mode, or file associations |
| [TESTING.md](TESTING.md) | Test map, how to run them, STA/`WpfHost` model (including the dispatcher error recorder and `TestApp`), what is not tested, manual release test checklist (including print and distribution) | You run/write tests or prepare a release |

## Document conventions

- Code references are written as `path:line`, relative to the repo root; line numbers match the code when the document was written and may shift. Type and method names are the stable reference; search for them with `grep`.
- Anything that cannot be proven from code, tests, or the README is marked "not verified" or "assumption".
- Documentation is in English (`README.md`, `AGENTS.md`, `docs/`); UI text, application messages, and code comments remain in Indonesian. `README.id.md` is the Indonesian version of `README.md`, and both must be updated together.
- Update these documents together with the code changes that affect them (see the PR checklist in [CONTRIBUTING.md](CONTRIBUTING.md#6-pr-checklist)).

## Document/code mismatches: already aligned (2026-10-07)

The mismatches recorded when this documentation was written have been fixed; none remain. Details per item are in
[DESIGN-DECISIONS.md](DESIGN-DECISIONS.md) and [SECURITY.md](SECURITY.md).

- README (Data locations): `crash.log` is now described as "the whole file is deleted if larger than 512 KB, then a new entry is written" (`src/Makdon/CrashLog.cs:37`).
- README (Known limitations): "over 1 MB" was replaced by tiered character thresholds (100k: background parse; 200k: longer render delay; 1 million: longer delay and less frequent statistics) - `src/Makdon/DocumentView.xaml.cs:22-29`.
- AGENTS.md: the test rule is now "must not touch/write `%APPDATA%`, `%LOCALAPPDATA%`, or the registry (reading HKCU Personalize for the
  theme is allowed)". Four tests read that value through `ThemeManager.SystemUsesLightTheme()` (one test directly, three `ThemeManagerApplyTests`
  tests through `Apply`); none writes. See [CONTRIBUTING.md](CONTRIBUTING.md#42-rules).
- `Makdon.csproj` `<Version>` changed from `1.0.0` to `0.1.0` to match the CHANGELOG.
- README states that `.txt` (and `.mdown`, `.mkd`) are also accepted for drag-and-drop and relative links (`MarkdownFiles.IsMarkdown`),
  whereas file associations cover only `.md`/`.markdown`.

## Update 2026-10-08: Print Preview

The documents were updated for the Print Preview feature (not yet committed when written; 1455 passing test cases, 800 `[Fact]`/`[Theory]` attributes in 24 files):
new components and sequence diagrams (ARCHITECTURE 4.9-4.10, 6.5), ADR-19 to ADR-26, print controls and risks (SECURITY 2.12, R8), the test map and new
test infrastructure (TESTING), print test rules (CONTRIBUTING 4.2 rules 8-9). Line-number references to `MainWindow.xaml.cs`,
`DocumentView.xaml.cs`, and `Support/WpfHost.cs` that shifted due to this change have been adjusted. Items deliberately marked "not verified":
that `DocumentViewer` only accepts a fixed document (only from code comments), that changing `dialog.PrintTicket` is used when printing,
and the performance/memory of Print Preview for very large documents.

## Update 2026-10-08: distribution and .NET 10

The distribution features are implemented (not yet committed; 1543 passing test cases): target `net10.0-windows`, `win-x64` publish profile,
portable mode (`Makdon.portable` marker), "Open with" (`FileAssociation`, Explorer Integration menu for portable), About,
Inno Setup installer, `scripts/build-release.ps1`, and `.github/workflows/release.yml`.

- Documents changed: DISTRIBUTION (status implemented; deviations in §13), ARCHITECTURE (components `AppPaths`, `AppInfo`,
  `InstallerMutex`, `RegistryStore`, `FileAssociation`; startup flow and portable scope), DESIGN-DECISIONS (ADR-27 to ADR-33),
  SECURITY (2.13 installation/portable/registry; R14-R17; R12), TESTING (test map for the three new files, `TestApp` and `FakeRegistryStore`,
  distribution and Print Preview checklists on the publish result), CONTRIBUTING (.NET 10 SDK, rules 10-11, prohibitions, section 9 making a release),
  README and AGENTS.md at the root, and CHANGELOG (Unreleased entry).
- `path:line` references that shifted due to changes in `MainWindow.xaml(.cs)`, `App.xaml.cs`, `AppSettings.cs`, `CrashLog.cs`,
  `Makdon.csproj`, and `WpfHost.cs` were recalculated from the diff against HEAD with a script, and references pointing to changed lines
  were checked one by one.
- Marked "not verified": the installer has not been compiled, `release.yml` has not been run, installation on Windows 10/11 has not been tested,
  SmartScreen/Smart App Control behavior, immutable releases, `Indonesian.isl`, licensing per third-party package, leftover DLLs during upgrade,
  AppMutex behavior by the installer, and the benefits of ReadyToRun. Since review: the CI action SHAs were checked against `git ls-remote`; the contents of
  `THIRD-PARTY-NOTICES-WPF.txt` have not yet been compared with its tag.
