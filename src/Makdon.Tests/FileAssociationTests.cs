using Makdon.Tests.Support;

namespace Makdon.Tests;

/// <summary>
/// Pendaftaran "Buka dengan" portable (docs/DISTRIBUTION.md §4.1, §4.3) di atas registri palsu dan penguji berkas palsu:
/// registri dan sistem berkas sungguhan tidak disentuh, SHChangeNotify diganti penghitung.
/// </summary>
public class FileAssociationTests
{
    const RegistryRoot Hkcu = RegistryRoot.CurrentUser;
    const RegistryRoot Hklm = RegistryRoot.LocalMachine;

    const string ThisExe = @"D:\Portable\Makdon\Makdon.exe";
    const string OtherExe = @"E:\Lain\Makdon\Makdon.exe";
    const string InstalledFolder = @"C:\Program Files\Makdon";
    const string ProgIdCommandKey = @"Software\Classes\Makdon.Markdown\shell\open\command";
    const string ApplicationCommandKey = @"Software\Classes\Applications\Makdon.exe\shell\open\command";
    const string Uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{2025C09D-1945-431D-BEBF-18C8EC3A9A78}_is1";
    const string UninstallWow = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{2025C09D-1945-431D-BEBF-18C8EC3A9A78}_is1";

    readonly FakeRegistryStore registry = new();
    readonly HashSet<string> files = new(StringComparer.OrdinalIgnoreCase) { ThisExe };
    int shellNotifications;

    FileAssociation Create(string exe = ThisExe) => new(registry, exe, files.Contains, () => shellNotifications++);

    static string Command(string exe) => $"\"{exe}\" \"%1\"";

    void RegisterCommands(string exe)
    {
        registry.With(Hkcu, ProgIdCommandKey, "", Command(exe));
        registry.With(Hkcu, ApplicationCommandKey, "", Command(exe));
    }

    void Install(RegistryRoot root = Hklm, string key = Uninstall, string valueName = "Inno Setup: App Path", string folder = InstalledFolder)
    {
        registry.With(root, key, valueName, folder);
        files.Add(Path.Combine(folder.Trim().Trim('"'), "Makdon.exe"));
    }

    /// <summary>Isi HKCU persis seperti tabel §4.1 / scripts/register-file-association.ps1 (tanpa -SetDefault).</summary>
    static SortedDictionary<string, string> ExpectedTable(string exe) => new(StringComparer.OrdinalIgnoreCase)
    {
        [@"Software\Classes\Makdon.Markdown|"] = "Dokumen Markdown",
        [@"Software\Classes\Makdon.Markdown|FriendlyTypeName"] = "Dokumen Markdown",
        [@"Software\Classes\Makdon.Markdown\DefaultIcon|"] = $"\"{exe}\",0",
        [@"Software\Classes\Makdon.Markdown\shell|"] = "open",
        [@"Software\Classes\Makdon.Markdown\shell\open|FriendlyAppName"] = "Makdon",
        [@"Software\Classes\Makdon.Markdown\shell\open|MUIVerb"] = "Buka dengan Makdon",
        [@"Software\Classes\Makdon.Markdown\shell\open\command|"] = $"\"{exe}\" \"%1\"",
        [@"Software\Classes\Applications\Makdon.exe|FriendlyAppName"] = "Makdon",
        [@"Software\Classes\Applications\Makdon.exe\DefaultIcon|"] = $"\"{exe}\",0",
        [@"Software\Classes\Applications\Makdon.exe\shell\open\command|"] = $"\"{exe}\" \"%1\"",
        [@"Software\Classes\Applications\Makdon.exe\SupportedTypes|.md"] = "",
        [@"Software\Classes\Applications\Makdon.exe\SupportedTypes|.markdown"] = "",
        [@"Software\Classes\.md\OpenWithProgids|Makdon.Markdown"] = "(none)",
        [@"Software\Classes\.markdown\OpenWithProgids|Makdon.Markdown"] = "(none)",
        [@"Software\Makdon\Capabilities|ApplicationName"] = "Makdon",
        [@"Software\Makdon\Capabilities|ApplicationDescription"] = "Editor dan pratinjau Markdown",
        [@"Software\Makdon\Capabilities\FileAssociations|.md"] = "Makdon.Markdown",
        [@"Software\Makdon\Capabilities\FileAssociations|.markdown"] = "Makdon.Markdown",
        [@"Software\RegisteredApplications|Makdon"] = @"Software\Makdon\Capabilities",
    };

    // ---- Register: isi registri ----

    [Fact]
    public void Register_OnACleanRegistry_WritesExactlyTheSpecTable_InHkcuOnly_AndNotifiesTheShellOnce()
    {
        var result = Create().Register();

        Assert.Equal(RegisterResult.Registered, result);
        Assert.Equal(ExpectedTable(ThisExe), registry.Values(Hkcu));
        Assert.Empty(registry.AllKeys(Hklm));
        Assert.Equal(1, shellNotifications);
    }

    [Fact]
    public void Register_NeverWritesTheExtensionDefault_OrUserChoice()
    {
        Create().Register();

        foreach (var ext in new[] { ".md", ".markdown" })
            Assert.Null(registry.GetString(Hkcu, @"Software\Classes\" + ext));
        Assert.DoesNotContain(registry.AllKeys(Hkcu), k => k.Contains("UserChoice", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(registry.AllKeys(Hkcu), k => k.Contains("FileExts", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(registry.AllKeys(Hkcu), k => k.Contains("PreviousDefault", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Register_KeepsOtherProgramsValuesInSharedKeys()
    {
        registry.With(Hkcu, @"Software\Classes\.md\OpenWithProgids", "Lain.Markdown", "");
        registry.With(Hkcu, @"Software\Classes\.md", "", "Lain.Markdown");
        registry.With(Hkcu, @"Software\RegisteredApplications", "Lain", @"Software\Lain\Capabilities");

        Create().Register();

        Assert.Equal("", registry.GetString(Hkcu, @"Software\Classes\.md\OpenWithProgids", "Lain.Markdown"));
        Assert.Equal("Lain.Markdown", registry.GetString(Hkcu, @"Software\Classes\.md"));
        Assert.Equal(@"Software\Lain\Capabilities", registry.GetString(Hkcu, @"Software\RegisteredApplications", "Lain"));
    }

    [Fact]
    public void Register_ExePathIsNormalized_BeforeItIsWritten()
    {
        files.Add(ThisExe);
        Create(@"D:\Portable\Makdon\..\Makdon\Makdon.exe").Register();

        Assert.Equal(Command(ThisExe), registry.GetString(Hkcu, ProgIdCommandKey));
    }

    // ---- Deteksi instalasi ----

    public static TheoryData<RegistryRoot, string, string> InstallationLocations => new()
    {
        { Hkcu, Uninstall, "Inno Setup: App Path" },
        { Hkcu, UninstallWow, "Inno Setup: App Path" },
        { Hklm, Uninstall, "Inno Setup: App Path" },
        { Hklm, UninstallWow, "Inno Setup: App Path" },
        { Hkcu, Uninstall, "InstallLocation" },
        { Hklm, UninstallWow, "InstallLocation" },
    };

    [Theory]
    [MemberData(nameof(InstallationLocations))]
    public void Installation_InAnyHiveOrView_IsDetected_AndBlocksRegistering(RegistryRoot root, string key, string valueName)
    {
        Install(root, key, valueName);
        var association = Create();

        Assert.Equal(InstalledFolder, association.GetStatus().InstalledPath);
        var before = registry.Mutations;
        Assert.Equal(RegisterResult.BlockedByInstallation, association.Register());
        Assert.Equal(before, registry.Mutations);
        Assert.Equal(0, shellNotifications);
    }

    [Fact]
    public void Installation_AllUsersBlocksEvenWhenHkcuAlreadyPointsToThisExe()
    {
        Install(Hklm);
        RegisterCommands(ThisExe);

        Assert.Equal(RegisterResult.BlockedByInstallation, Create().Register());
    }

    [Fact]
    public void Installation_AppPath_IsPreferredOverInstallLocation()
    {
        registry.With(Hklm, Uninstall, "Inno Setup: App Path", InstalledFolder);
        registry.With(Hklm, Uninstall, "InstallLocation", @"C:\Lain\");
        files.Add(Path.Combine(InstalledFolder, "Makdon.exe"));
        files.Add(@"C:\Lain\Makdon.exe");

        Assert.Equal(InstalledFolder, Create().GetStatus().InstalledPath);
    }

    [Fact]
    public void Installation_EmptyAppPath_FallsBackToInstallLocation()
    {
        registry.With(Hklm, Uninstall, "Inno Setup: App Path", "  ");
        Install(Hklm, Uninstall, "InstallLocation");

        Assert.Equal(InstalledFolder, Create().GetStatus().InstalledPath);
    }

    [Fact]
    public void Installation_QuotedPathWithSpaces_IsTrimmed()
    {
        Install(Hklm, Uninstall, "Inno Setup: App Path", $" \"{InstalledFolder}\" ");

        Assert.Equal(InstalledFolder, Create().GetStatus().InstalledPath);
    }

    [Fact]
    public void Installation_KeyWithoutTheExe_IsNotAnInstallation_SoRegisteringWorks()
    {
        registry.With(Hklm, Uninstall, "Inno Setup: App Path", InstalledFolder); // Makdon.exe tidak ada (sisa uninstall)
        var association = Create();

        Assert.False(association.GetStatus().IsInstalled);
        Assert.Equal(RegisterResult.Registered, association.Register());
    }

    [Fact]
    public void Installation_KeyWithoutAnyPath_IsNotAnInstallation()
    {
        registry.WithKey(Hkcu, Uninstall);

        Assert.Null(Create().GetStatus().InstalledPath);
    }

    [Fact]
    public void Installation_PathWithInvalidCharacters_IsIgnored_NotThrown()
    {
        registry.With(Hklm, Uninstall, "Inno Setup: App Path", "C:\\Pro\0gram");

        Assert.Null(Create().GetStatus().InstalledPath);
    }

    // ---- Register: keadaan pendaftaran yang ada ----

    [Fact]
    public void Register_WhenAlreadyPointingToThisExe_IsAlreadyRegistered_AndWritesNothing()
    {
        Create().Register();
        var before = registry.Mutations;

        Assert.Equal(RegisterResult.AlreadyRegistered, Create().Register());
        Assert.Equal(before, registry.Mutations);
        Assert.Equal(1, shellNotifications);
    }

    [Fact]
    public void Register_ComparesTheRegisteredExe_CaseInsensitively()
    {
        RegisterCommands(ThisExe.ToUpperInvariant());
        var association = Create();

        Assert.Equal(RegistrationKind.ThisExe, association.GetStatus().Registration);
        Assert.Equal(RegisterResult.AlreadyRegistered, association.Register());
    }

    [Fact]
    public void Register_OverAStalePath_WritesWithoutAsking()
    {
        RegisterCommands(@"F:\Usang\Makdon.exe"); // tidak ada di "disk"
        var association = Create();

        Assert.Equal(RegistrationKind.Stale, association.GetStatus().Registration);
        Assert.Equal(RegisterResult.Registered, association.Register());
        Assert.Equal(ExpectedTable(ThisExe), registry.Values(Hkcu));
        Assert.Equal(1, shellNotifications);
    }

    [Fact]
    public void Register_OverAnotherPortableCopy_NeedsConfirmation_ThenReplacesWhenConfirmed()
    {
        RegisterCommands(OtherExe);
        files.Add(OtherExe);
        files.Add(Path.Combine(Path.GetDirectoryName(OtherExe)!, AppPaths.PortableMarkerFileName));
        var association = Create();
        var before = registry.Mutations;

        Assert.Equal(RegistrationKind.OtherPortable, association.GetStatus().Registration);
        Assert.Equal(OtherExe, association.GetStatus().RegisteredExe);
        Assert.Equal(RegisterResult.NeedsConfirmationOtherPortable, association.Register());
        Assert.Equal(before, registry.Mutations);
        Assert.Equal(0, shellNotifications);

        Assert.Equal(RegisterResult.Registered, association.Register(replaceExisting: true));
        Assert.Equal(ExpectedTable(ThisExe), registry.Values(Hkcu));
        Assert.Equal(1, shellNotifications);
    }

    [Fact]
    public void Register_OverAnExeWithoutMarker_NeedsConfirmation_ThenReplacesWhenConfirmed()
    {
        RegisterCommands(OtherExe);
        files.Add(OtherExe); // tanpa penanda: mis. hasil skrip pengembangan
        var association = Create();
        var before = registry.Mutations;

        Assert.Equal(RegistrationKind.OtherExe, association.GetStatus().Registration);
        Assert.Equal(RegisterResult.NeedsConfirmationOtherExe, association.Register());
        Assert.Equal(before, registry.Mutations);

        Assert.Equal(RegisterResult.Registered, association.Register(replaceExisting: true));
        Assert.Equal(Command(ThisExe), registry.GetString(Hkcu, ApplicationCommandKey));
    }

    [Fact]
    public void Status_TheMostForeignRegistrationWins()
    {
        registry.With(Hkcu, ProgIdCommandKey, "", Command(@"F:\Usang\Makdon.exe")); // basi
        registry.With(Hkcu, ApplicationCommandKey, "", Command(OtherExe));          // exe lain tanpa penanda
        files.Add(OtherExe);

        var status = Create().GetStatus();

        Assert.Equal(RegistrationKind.OtherExe, status.Registration);
        Assert.Equal(OtherExe, status.RegisteredExe);
    }

    [Fact]
    public void Status_ExeInsideTheInstallationFolder_IsNeverTreatedAsPortable()
    {
        Install(Hklm);
        var installedExe = Path.Combine(InstalledFolder, "Makdon.exe");
        files.Add(Path.Combine(InstalledFolder, AppPaths.PortableMarkerFileName)); // penanda nyasar
        RegisterCommands(installedExe);

        Assert.Equal(RegistrationKind.OtherExe, Create().GetStatus().Registration);
    }

    [Fact]
    public void Status_NothingRegistered_IsNone()
    {
        var status = Create().GetStatus();

        Assert.Equal(RegistrationKind.None, status.Registration);
        Assert.Null(status.RegisteredExe);
        Assert.False(status.IsInstalled);
    }

    // ---- Unregister ----

    [Fact]
    public void Unregister_WhenRegisteredByAnotherExe_IsNotOwned_AndDeletesNothing()
    {
        RegisterCommands(OtherExe);
        files.Add(OtherExe);
        var before = registry.Values(Hkcu);
        var mutations = registry.Mutations;

        Assert.Equal(UnregisterResult.NotOwned, Create().Unregister());
        Assert.Equal(before, registry.Values(Hkcu));
        Assert.Equal(mutations, registry.Mutations);
        Assert.Equal(0, shellNotifications);
    }

    [Fact]
    public void Unregister_WhenNothingIsRegistered_IsNotOwned()
    {
        Assert.Equal(UnregisterResult.NotOwned, Create().Unregister());
        Assert.Equal(0, registry.Mutations);
    }

    [Fact]
    public void Unregister_AfterRegister_RemovesEverything_ButLeavesOtherProgramsValues()
    {
        registry.With(Hkcu, @"Software\Classes\.md\OpenWithProgids", "Lain.Markdown", "");
        registry.With(Hkcu, @"Software\RegisteredApplications", "Lain", @"Software\Lain\Capabilities");
        Create().Register();

        Assert.Equal(UnregisterResult.Removed, Create().Unregister());

        var left = registry.Values(Hkcu);
        Assert.Equal(2, left.Count);
        Assert.Equal("", left[@"Software\Classes\.md\OpenWithProgids|Lain.Markdown"]);
        Assert.Equal(@"Software\Lain\Capabilities", left[@"Software\RegisteredApplications|Lain"]);
        Assert.False(registry.KeyExists(Hkcu, @"Software\Classes\Makdon.Markdown"));
        Assert.False(registry.KeyExists(Hkcu, @"Software\Classes\Applications\Makdon.exe"));
        Assert.False(registry.KeyExists(Hkcu, @"Software\Makdon")); // kosong -> dihapus
        Assert.Equal(2, shellNotifications);
    }

    [Fact]
    public void Unregister_KeepsSoftwareMakdon_WhenItStillHoldsPreviousDefault()
    {
        Create().Register();
        registry.With(Hkcu, @"Software\Makdon\PreviousDefault", ".md", "txtfile"); // dari skrip -SetDefault

        Create().Unregister();

        Assert.True(registry.KeyExists(Hkcu, @"Software\Makdon"));
        Assert.False(registry.KeyExists(Hkcu, @"Software\Makdon\Capabilities"));
        Assert.Equal("txtfile", registry.GetString(Hkcu, @"Software\Makdon\PreviousDefault", ".md"));
    }

    [Fact]
    public void Unregister_KeepsSoftwareMakdon_WhenItHasAValueOfItsOwn()
    {
        Create().Register();
        registry.With(Hkcu, @"Software\Makdon", "Catatan", "x");

        Create().Unregister();

        Assert.Equal("x", registry.GetString(Hkcu, @"Software\Makdon", "Catatan"));
    }

    [Fact]
    public void Unregister_OnlyRemovesThePartThatPointsToThisExe()
    {
        Create().Register();
        registry.With(Hkcu, ApplicationCommandKey, "", Command(OtherExe)); // Applications diambil alih exe lain

        Assert.Equal(UnregisterResult.Removed, Create().Unregister());

        Assert.False(registry.KeyExists(Hkcu, @"Software\Classes\Makdon.Markdown"));
        Assert.Null(registry.GetString(Hkcu, @"Software\Classes\.md\OpenWithProgids", "Makdon.Markdown"));
        Assert.Equal(Command(OtherExe), registry.GetString(Hkcu, ApplicationCommandKey));
    }

    [Fact]
    public void Unregister_ApplicationsOnly_LeavesTheProgIdOfAnotherExe()
    {
        Create().Register();
        registry.With(Hkcu, ProgIdCommandKey, "", Command(OtherExe));

        Create().Unregister();

        Assert.False(registry.KeyExists(Hkcu, @"Software\Classes\Applications\Makdon.exe"));
        Assert.Equal(Command(OtherExe), registry.GetString(Hkcu, ProgIdCommandKey));
        Assert.True(registry.KeyExists(Hkcu, @"Software\Makdon\Capabilities")); // milik ProgID, tidak disentuh
        Assert.NotNull(registry.GetString(Hkcu, @"Software\RegisteredApplications", "Makdon"));
    }

    // ---- Pemeriksaan startup ----

    [Fact]
    public void CheckStartup_StalePortablePath_OffersToUpdate()
    {
        RegisterCommands(@"F:\Usang\Makdon.exe");

        Assert.Equal(StartupIssue.StalePath, Create().CheckStartup());
    }

    [Fact]
    public void CheckStartup_InstallationPlusHkcuPointingHere_OffersToUnregister()
    {
        RegisterCommands(ThisExe);
        Install(Hklm);

        Assert.Equal(StartupIssue.ShadowsInstallation, Create().CheckStartup());
    }

    [Fact]
    public void CheckStartup_NothingToOffer_InTheOrdinaryCases()
    {
        Assert.Equal(StartupIssue.None, Create().CheckStartup()); // tidak terdaftar

        RegisterCommands(ThisExe);
        Assert.Equal(StartupIssue.None, Create().CheckStartup()); // terdaftar ke exe ini, tanpa instalasi

        RegisterCommands(@"F:\Usang\Makdon.exe");
        Install(Hklm);
        Assert.Equal(StartupIssue.None, Create().CheckStartup()); // ada instalasi: path basi bukan urusan portable
    }

    [Fact]
    public void CheckStartup_DoesNotWriteAnything()
    {
        RegisterCommands(@"F:\Usang\Makdon.exe");

        Create().CheckStartup();

        Assert.Equal(0, registry.Mutations);
        Assert.Equal(0, shellNotifications);
    }

    // ---- Path ----

    [Theory]
    [InlineData("\"C:\\Program Files\\Makdon\\Makdon.exe\" \"%1\"", @"C:\Program Files\Makdon\Makdon.exe")]
    [InlineData("  \"C:\\A B\\Makdon.exe\"", @"C:\A B\Makdon.exe")]
    [InlineData("\"C:\\A\\Makdon.exe", @"C:\A\Makdon.exe")] // kutip tak tertutup: sisa teks
    [InlineData("C:\\A\\Makdon.exe %1", @"C:\A\Makdon.exe")]
    [InlineData("C:\\A\\Makdon.exe\t\"%1\"", @"C:\A\Makdon.exe")]
    [InlineData("C:\\A\\Makdon.exe", @"C:\A\Makdon.exe")]
    [InlineData("C:\\A\\..\\B\\Makdon.exe \"%1\"", @"C:\B\Makdon.exe")]
    public void ExtractExePath_TakesTheFirstTokenAndNormalizesIt(string command, string expected)
    {
        Assert.Equal(expected, FileAssociation.ExtractExePath(command));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\" \"%1\"")]
    [InlineData("\"C:\\A\\Mak\0don.exe\" \"%1\"")]
    public void ExtractExePath_EmptyOrInvalid_IsNull(string? command)
    {
        Assert.Null(FileAssociation.ExtractExePath(command));
    }

    [Theory]
    [InlineData(@"C:\X\Makdon\Makdon.exe", @"C:\X\Makdon", true)]
    [InlineData(@"C:\X\Makdon\Makdon.exe", @"C:\X\Makdon\", true)]
    [InlineData(@"c:\x\makdon\sub\Makdon.exe", @"C:\X\Makdon", true)]
    [InlineData(@"C:\X\Makdon2\Makdon.exe", @"C:\X\Makdon", false)]
    [InlineData(@"C:\X\Makdon", @"C:\X\Makdon", false)]
    [InlineData(@"C:\X\Makdon\..\Makdon2\Makdon.exe", @"C:\X\Makdon", false)]
    public void IsUnderFolder_ComparesWithATrailingSeparator(string path, string folder, bool expected)
    {
        Assert.Equal(expected, FileAssociation.IsUnderFolder(path, folder));
    }

    [Fact]
    public void PathsEqual_IgnoresCase_ButNothingElse()
    {
        Assert.True(FileAssociation.PathsEqual(@"C:\X\Makdon.exe", @"c:\x\MAKDON.EXE"));
        Assert.False(FileAssociation.PathsEqual(@"C:\X\Makdon.exe", @"C:\X\Makdon2.exe"));
    }

    // ---- Perintah relatif, path jaringan, exe hilang ----

    [Theory]
    [InlineData("Makdon.exe \"%1\"")]
    [InlineData("\"Makdon.exe\" \"%1\"")]
    [InlineData(@"""..\Makdon.exe"" ""%1""")]
    [InlineData(@"""\Makdon.exe"" ""%1""")]
    public void RelativeCommand_IsNeverNormalizedAgainstTheCwd(string command)
    {
        Assert.Null(FileAssociation.ExtractExePath(command));
    }

    [Fact]
    public void RelativeCommand_IsAnotherExe_NotThisExe_NotStale_AndUnregisterDoesNotRemoveIt()
    {
        // exe ini kebetulan = Makdon.exe di CWD: perintah relatif "Makdon.exe" tidak boleh dianggap milik exe ini.
        var inCwd = Path.GetFullPath("Makdon.exe");
        files.Add(inCwd);
        registry.With(Hkcu, ProgIdCommandKey, "", "\"Makdon.exe\" \"%1\"");
        registry.With(Hkcu, ApplicationCommandKey, "", "Makdon.exe \"%1\"");
        var assoc = Create(inCwd);

        Assert.Equal(RegistrationKind.OtherExe, assoc.GetStatus().Registration);
        Assert.Equal(StartupIssue.None, assoc.CheckStartup());
        Assert.Equal(UnregisterResult.NotOwned, assoc.Unregister());
        Assert.Equal("\"Makdon.exe\" \"%1\"", registry.GetString(Hkcu, ProgIdCommandKey));
        Assert.Equal(RegisterResult.NeedsConfirmationOtherExe, assoc.Register());
        Assert.Equal(0, shellNotifications);
    }

    [Fact]
    public void Installation_RelativeAppPath_IsNotAnInstallation()
    {
        Install(folder: "Makdon");
        files.Add(Path.GetFullPath(Path.Combine("Makdon", "Makdon.exe")));

        Assert.Null(Create().FindInstallation());
    }

    [Theory]
    [InlineData(@"\\server\share\Makdon\Makdon.exe", true)]
    [InlineData(@"\\?\UNC\server\share\Makdon.exe", true)]
    [InlineData(@"\\?\C:\Makdon\Makdon.exe", false)]
    [InlineData(@"C:\Makdon\Makdon.exe", false)]
    public void IsNetworkPath_RecognizesUncAndDevicePaths_ButNotExtendedLocalPaths(string path, bool expected)
    {
        Assert.Equal(expected, FileAssociation.IsNetworkPath(path));
    }

    [Theory]
    [InlineData(@"\\server\share\Makdon\Makdon.exe")]
    [InlineData(@"\\?\UNC\server\share\Makdon\Makdon.exe")]
    public void UncRegistration_IsOtherExe_WithoutTouchingTheFileSystem_AndStartupDoesNotOfferAnUpdate(string unc)
    {
        RegisterCommands(unc);
        var assoc = new FileAssociation(registry, ThisExe,
            p => p.StartsWith(@"\\", StringComparison.Ordinal) ? throw new InvalidOperationException("UNC diperiksa: " + p) : files.Contains(p),
            () => shellNotifications++);

        var status = assoc.GetStatus();

        Assert.Equal(RegistrationKind.OtherExe, status.Registration);
        Assert.Equal(StartupIssue.None, assoc.CheckStartup());
        Assert.Equal(RegisterResult.NeedsConfirmationOtherExe, assoc.Register());
        Assert.Equal(RegisterResult.Registered, assoc.Register(replaceExisting: true));
    }

    [Fact]
    public void UncInstallationPath_IsIgnored_WithoutTouchingTheFileSystem()
    {
        registry.With(Hklm, Uninstall, "Inno Setup: App Path", @"\\server\share\Makdon");
        var assoc = new FileAssociation(registry, ThisExe,
            p => p.StartsWith(@"\\", StringComparison.Ordinal) ? throw new InvalidOperationException("UNC diperiksa: " + p) : files.Contains(p),
            () => shellNotifications++);

        Assert.Null(assoc.FindInstallation());
    }

    [Fact]
    public void Register_WhenTheExeDoesNotExist_IsRejected_AndWritesNothing()
    {
        files.Remove(ThisExe);

        Assert.Equal(RegisterResult.ExeNotFound, Create().Register());
        Assert.Empty(registry.AllKeys(Hkcu));
        Assert.Equal(0, shellNotifications);
    }

    [Fact]
    public void Register_WhenTheExeWasRenamed_IsRejected_AndWritesNothing()
    {
        const string renamed = @"D:\Portable\Makdon\Lain.exe";
        files.Add(renamed);

        Assert.Equal(RegisterResult.ExeNotFound, Create(renamed).Register());
        Assert.Empty(registry.AllKeys(Hkcu));
        Assert.Equal(0, shellNotifications);
    }
}
