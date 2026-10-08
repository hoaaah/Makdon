using System.Runtime.InteropServices;

namespace Makdon;

/// <summary>Bagaimana HKCU saat ini menunjuk exe untuk ProgID/Applications Makdon.</summary>
public enum RegistrationKind
{
    /// <summary>Tidak ada perintah terdaftar.</summary>
    None,
    /// <summary>Menunjuk exe yang sudah tidak ada (path basi).</summary>
    Stale,
    /// <summary>Menunjuk exe ini.</summary>
    ThisExe,
    /// <summary>Menunjuk exe Makdon portable lain yang masih ada (ada penanda).</summary>
    OtherPortable,
    /// <summary>Menunjuk exe lain yang masih ada tanpa penanda (mis. hasil skrip pengembangan atau instalasi).</summary>
    OtherExe,
}

/// <param name="InstalledPath">Folder instalasi Makdon hasil installer (null bila tidak ada).</param>
/// <param name="Registration">Keadaan pendaftaran di HKCU.</param>
/// <param name="RegisteredExe">Exe yang menjadi dasar <paramref name="Registration"/> (null bila None).</param>
public sealed record AssociationStatus(string? InstalledPath, RegistrationKind Registration, string? RegisteredExe)
{
    public bool IsInstalled => InstalledPath is not null;
}

public enum RegisterResult
{
    Registered,
    /// <summary>Sudah menunjuk exe ini; tidak ada yang ditulis.</summary>
    AlreadyRegistered,
    /// <summary>Ada versi terpasang (HKCU atau HKLM): ditolak.</summary>
    BlockedByInstallation,
    /// <summary>Exe ini tidak bernama Makdon.exe atau tidak ada di path-nya (mis. diganti nama): ditolak, tidak ada yang ditulis.</summary>
    ExeNotFound,
    /// <summary>Menunjuk Makdon portable lain; perlu konfirmasi, lalu panggil lagi dengan replaceExisting.</summary>
    NeedsConfirmationOtherPortable,
    /// <summary>Menunjuk exe lain tanpa penanda; perlu konfirmasi (dengan peringatan), lalu panggil lagi dengan replaceExisting.</summary>
    NeedsConfirmationOtherExe,
}

public enum UnregisterResult
{
    Removed,
    /// <summary>Tidak ada pendaftaran yang menunjuk exe ini; tidak ada yang dihapus.</summary>
    NotOwned,
}

public enum StartupIssue
{
    None,
    /// <summary>HKCU menunjuk exe portable yang sudah tidak ada: tawarkan memperbarui path.</summary>
    StalePath,
    /// <summary>Ada instalasi dan HKCU menunjuk exe ini: tawarkan mencabut pendaftaran portable.</summary>
    ShadowsInstallation,
}

/// <summary>
/// Pendaftaran "Buka dengan" untuk Makdon portable (docs/DISTRIBUTION.md §4.1 dan §4.3), hanya di HKCU. Seluruh akses registri
/// lewat <see cref="IRegistryStore"/> dan keberadaan berkas lewat <c>fileExists</c>, supaya test memakai palsu. Galat registri
/// (SecurityException, UnauthorizedAccessException, IOException) dibiarkan naik; pemanggil yang menampilkannya.
/// </summary>
public sealed class FileAssociation
{
    public const string ProgId = "Makdon.Markdown";
    public const string AppName = "Makdon";
    public const string AppExeName = "Makdon.exe";
    public const string Description = "Editor dan pratinjau Markdown";
    public static readonly IReadOnlyList<string> Extensions = [".md", ".markdown"];

    const string Classes = @"Software\Classes";
    const string ProgIdKey = Classes + @"\" + ProgId;
    const string ApplicationKey = Classes + @"\Applications\" + AppExeName;
    const string AppRootKey = @"Software\" + AppName;
    const string CapabilitiesKey = AppRootKey + @"\Capabilities";
    const string RegisteredApplicationsKey = @"Software\RegisteredApplications";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{" + AppInfo.InstallerAppId + "}_is1";
    const string UninstallKeyWow64 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{" + AppInfo.InstallerAppId + "}_is1";

    const int ShcneAssocChanged = 0x08000000;

    readonly IRegistryStore registry;
    readonly string exePath;
    readonly Func<string, bool> fileExists;
    readonly Action notifyShell;

    /// <param name="registry">Penyimpan registri (nyata: <c>new WindowsRegistryStore()</c>).</param>
    /// <param name="exePath">Path exe ini.</param>
    /// <param name="fileExists">Penguji berkas (bawaan <see cref="File.Exists"/>); dipakai untuk exe terdaftar, exe instalasi, dan penanda portable.</param>
    /// <param name="notifyShell">Dipanggil setelah menulis/menghapus (bawaan SHChangeNotify SHCNE_ASSOCCHANGED).</param>
    internal FileAssociation(IRegistryStore registry, string exePath, Func<string, bool>? fileExists = null, Action? notifyShell = null)
    {
        this.registry = registry;
        this.exePath = Path.GetFullPath(exePath);
        this.fileExists = fileExists ?? File.Exists;
        this.notifyShell = notifyShell ?? NotifyShellAssociationChanged;
    }

    /// <summary>Instance produksi: registri nyata, exe proses ini.</summary>
    internal static FileAssociation ForCurrentProcess() => new(new WindowsRegistryStore(), AppPaths.Current.ExePath);

    // ---- Deteksi ----

    /// <summary>Folder instalasi Inno (HKCU lalu HKLM, view 64-bit lalu WOW6432Node) bila kunci ada dan Makdon.exe-nya ada; selain itu null.</summary>
    internal string? FindInstallation()
    {
        foreach (var root in new[] { RegistryRoot.CurrentUser, RegistryRoot.LocalMachine })
        foreach (var key in new[] { UninstallKey, UninstallKeyWow64 })
        {
            if (!registry.KeyExists(root, key)) continue;
            var path = registry.GetString(root, key, "Inno Setup: App Path");
            if (string.IsNullOrWhiteSpace(path)) path = registry.GetString(root, key, "InstallLocation");
            if (string.IsNullOrWhiteSpace(path)) continue;
            path = path.Trim().Trim('"');
            // Relatif akan dinormalkan terhadap CWD; UNC memblokir thread UI saat dicek. Keduanya dianggap bukan instalasi.
            if (!Path.IsPathFullyQualified(path) || IsNetworkPath(path)) continue;
            try
            {
                if (fileExists(Path.Combine(path, AppExeName))) return path;
            }
            catch (ArgumentException) { } // path berisi karakter terlarang: anggap tidak ada
        }
        return null;
    }

    public AssociationStatus GetStatus()
    {
        var installed = FindInstallation();
        var kinds = new[]
        {
            ProgIdKey + @"\shell\open\command",
            ApplicationKey + @"\shell\open\command",
        }
        .Select(key => ClassifyCommand(registry.GetString(RegistryRoot.CurrentUser, key), installed))
        .Where(entry => entry is not null)
        .Select(entry => entry!.Value)
        .ToList();

        if (kinds.Count == 0) return new AssociationStatus(installed, RegistrationKind.None, null);

        // Yang paling "asing" menang: konfirmasi paling keras untuk exe tanpa penanda, lalu portable lain, lalu basi.
        foreach (var kind in new[] { RegistrationKind.OtherExe, RegistrationKind.OtherPortable, RegistrationKind.Stale, RegistrationKind.ThisExe })
        {
            var hit = kinds.FirstOrDefault(k => k.Kind == kind);
            if (hit.Path is not null) return new AssociationStatus(installed, kind, hit.Path);
        }
        return new AssociationStatus(installed, RegistrationKind.None, null);
    }

    /// <summary>
    /// Perintah relatif (exe-nya tak bisa dipastikan) dan path jaringan (tak diperiksa di thread UI) dianggap exe lain, bukan basi:
    /// pengguna diminta konfirmasi sebelum ditimpa dan tidak pernah ditawari pembaruan otomatis.
    /// </summary>
    (string Path, RegistrationKind Kind)? ClassifyCommand(string? command, string? installedFolder)
    {
        var token = ExtractCommandToken(command);
        if (token is null) return null;
        if (!Path.IsPathFullyQualified(token)) return (token, RegistrationKind.OtherExe);
        var path = ExtractExePath(command);
        return path is null ? null : (path, Classify(path, installedFolder));
    }

    RegistrationKind Classify(string registeredExe, string? installedFolder)
    {
        if (PathsEqual(registeredExe, exePath)) return RegistrationKind.ThisExe;
        if (IsNetworkPath(registeredExe)) return RegistrationKind.OtherExe;
        if (!fileExists(registeredExe)) return RegistrationKind.Stale;
        // Exe di dalam folder instalasi bukan portable (paket installer tidak membawa penanda).
        if (installedFolder is not null && IsUnderFolder(registeredExe, installedFolder)) return RegistrationKind.OtherExe;
        var folder = Path.GetDirectoryName(registeredExe);
        return folder is not null && fileExists(Path.Combine(folder, AppPaths.PortableMarkerFileName))
            ? RegistrationKind.OtherPortable
            : RegistrationKind.OtherExe;
    }

    /// <summary>Masalah yang perlu ditawarkan ke pengguna saat startup mode portable.</summary>
    public StartupIssue CheckStartup()
    {
        var status = GetStatus();
        if (status.IsInstalled)
            return status.Registration == RegistrationKind.ThisExe ? StartupIssue.ShadowsInstallation : StartupIssue.None;
        return status.Registration == RegistrationKind.Stale ? StartupIssue.StalePath : StartupIssue.None;
    }

    // ---- Daftarkan / Cabut ----

    /// <param name="replaceExisting">true setelah pengguna mengonfirmasi penggantian pendaftaran exe lain.</param>
    public RegisterResult Register(bool replaceExisting = false)
    {
        var status = GetStatus();
        if (status.IsInstalled) return RegisterResult.BlockedByInstallation;
        // Windows mencocokkan kunci Applications\Makdon.exe lewat nama berkas, dan perintah tak boleh menunjuk exe yang tidak ada.
        if (!string.Equals(Path.GetFileName(exePath), AppExeName, StringComparison.OrdinalIgnoreCase) || !fileExists(exePath))
            return RegisterResult.ExeNotFound;

        switch (status.Registration)
        {
            case RegistrationKind.ThisExe:
                return RegisterResult.AlreadyRegistered;
            case RegistrationKind.OtherPortable when !replaceExisting:
                return RegisterResult.NeedsConfirmationOtherPortable;
            case RegistrationKind.OtherExe when !replaceExisting:
                return RegisterResult.NeedsConfirmationOtherExe;
        }

        WriteRegistration();
        notifyShell();
        return RegisterResult.Registered;
    }

    /// <summary>Menghapus pendaftaran, hanya bagian yang perintahnya menunjuk exe ini.</summary>
    public UnregisterResult Unregister()
    {
        var ownsProgId = PointsToThisExe(ProgIdKey + @"\shell\open\command");
        var ownsApplication = PointsToThisExe(ApplicationKey + @"\shell\open\command");
        if (!ownsProgId && !ownsApplication) return UnregisterResult.NotOwned;

        if (ownsProgId)
        {
            foreach (var ext in Extensions)
                registry.DeleteValue(RegistryRoot.CurrentUser, Classes + @"\" + ext + @"\OpenWithProgids", ProgId);
            registry.DeleteKeyTree(RegistryRoot.CurrentUser, ProgIdKey);
            registry.DeleteKeyTree(RegistryRoot.CurrentUser, CapabilitiesKey);
            registry.DeleteValue(RegistryRoot.CurrentUser, RegisteredApplicationsKey, AppName);
        }
        if (ownsApplication) registry.DeleteKeyTree(RegistryRoot.CurrentUser, ApplicationKey);

        // Software\Makdon dibiarkan bila masih berisi (mis. PreviousDefault dari skrip pengembangan).
        if (registry.KeyExists(RegistryRoot.CurrentUser, AppRootKey)
            && registry.GetValueNames(RegistryRoot.CurrentUser, AppRootKey).Count == 0
            && registry.GetSubKeyNames(RegistryRoot.CurrentUser, AppRootKey).Count == 0)
            registry.DeleteKeyTree(RegistryRoot.CurrentUser, AppRootKey);

        notifyShell();
        return UnregisterResult.Removed;
    }

    bool PointsToThisExe(string commandKey) =>
        ExtractExePath(registry.GetString(RegistryRoot.CurrentUser, commandKey)) is { } path && PathsEqual(path, exePath);

    // Nilai persis tabel di docs/DISTRIBUTION.md §4.1 dan scripts/register-file-association.ps1 (tanpa UserChoice / nilai bawaan ekstensi).
    void WriteRegistration()
    {
        const RegistryRoot hkcu = RegistryRoot.CurrentUser;
        var command = $"\"{exePath}\" \"%1\"";
        var icon = $"\"{exePath}\",0";

        registry.SetString(hkcu, ProgIdKey, "", "Dokumen Markdown");
        registry.SetString(hkcu, ProgIdKey, "FriendlyTypeName", "Dokumen Markdown");
        registry.SetString(hkcu, ProgIdKey + @"\DefaultIcon", "", icon);
        registry.SetString(hkcu, ProgIdKey + @"\shell", "", "open");
        registry.SetString(hkcu, ProgIdKey + @"\shell\open", "FriendlyAppName", AppName);
        registry.SetString(hkcu, ProgIdKey + @"\shell\open", "MUIVerb", "Buka dengan " + AppName);
        registry.SetString(hkcu, ProgIdKey + @"\shell\open\command", "", command);

        registry.SetString(hkcu, ApplicationKey, "FriendlyAppName", AppName);
        registry.SetString(hkcu, ApplicationKey + @"\DefaultIcon", "", icon);
        registry.SetString(hkcu, ApplicationKey + @"\shell\open\command", "", command);
        foreach (var ext in Extensions)
            registry.SetString(hkcu, ApplicationKey + @"\SupportedTypes", ext, "");

        foreach (var ext in Extensions)
            registry.SetEmptyValue(hkcu, Classes + @"\" + ext + @"\OpenWithProgids", ProgId);

        registry.SetString(hkcu, CapabilitiesKey, "ApplicationName", AppName);
        registry.SetString(hkcu, CapabilitiesKey, "ApplicationDescription", Description);
        foreach (var ext in Extensions)
            registry.SetString(hkcu, CapabilitiesKey + @"\FileAssociations", ext, ProgId);
        registry.SetString(hkcu, RegisteredApplicationsKey, AppName, CapabilitiesKey);
    }

    // ---- Path ----

    /// <summary>
    /// Exe dari baris perintah: token pertama (<see cref="ExtractCommandToken"/>), lalu <see cref="Path.GetFullPath(string)"/>.
    /// Null bila kosong, tidak valid, atau bukan path penuh: path relatif tidak boleh dinormalkan terhadap CWD proses
    /// (bisa kebetulan sama dengan exe ini, sehingga "Cabut" menghapus pendaftaran yang bukan milik kita).
    /// </summary>
    internal static string? ExtractExePath(string? command)
    {
        var token = ExtractCommandToken(command);
        if (token is null || !Path.IsPathFullyQualified(token)) return null;
        try { return Path.GetFullPath(token); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    /// <summary>Token pertama baris perintah (dalam kutip bila diawali kutip, selain itu sampai spasi pertama); null bila kosong.</summary>
    internal static string? ExtractCommandToken(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var text = command.Trim();
        string token;
        if (text[0] == '"')
        {
            var end = text.IndexOf('"', 1);
            token = end < 0 ? text[1..] : text[1..end];
        }
        else
        {
            var space = text.IndexOfAny([' ', '\t']);
            token = space < 0 ? text : text[..space];
        }
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    /// <summary>UNC (\\server\share, \\?\UNC\...) dan path perangkat; \\?\C:\... adalah path lokal. Berkas di sini tidak diperiksa (bisa memblokir thread UI).</summary>
    internal static bool IsNetworkPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal)
        && !(path.StartsWith(@"\\?\", StringComparison.Ordinal) && path.Length >= 6 && path[5] == ':');

    internal static bool PathsEqual(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Prefix folder dengan pemisah di akhir: C:\X\Makdon\Makdon.exe ada di C:\X\Makdon, tetapi C:\X\Makdon2\ bukan.</summary>
    internal static bool IsUnderFolder(string path, string folder)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    static void NotifyShellAssociationChanged()
    {
        try { SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
