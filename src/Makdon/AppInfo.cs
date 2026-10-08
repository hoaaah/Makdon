using System.Reflection;

namespace Makdon;

/// <summary>Konstanta identitas aplikasi yang harus sama dengan installer (installer/Makdon.iss) dan docs/DISTRIBUTION.md.</summary>
static class AppInfo
{
    /// <summary>AppId Inno Setup (tanpa kurung kurawal). Tidak boleh berubah: identitas upgrade/uninstall dan deteksi instalasi.</summary>
    public const string InstallerAppId = "2025C09D-1945-431D-BEBF-18C8EC3A9A78";

    /// <summary>Nama mutex yang diperiksa installer/uninstaller untuk mendeteksi Makdon yang sedang berjalan (mode terpasang).</summary>
    public const string InstallerMutexName = "Makdon.AppMutex";
    public const string InstallerGlobalMutexName = @"Global\Makdon.AppMutex";

    public const string ReleasesUrl = "https://github.com/hoaaah/Makdon/releases";
    public const string LicenseName = "MIT";

    /// <summary>InformationalVersion tanpa akhiran "+hash"; cadangan: versi assembly.</summary>
    public static string Version { get; } = ReadVersion();

    static string ReadVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational)) return informational.Split('+')[0];
        return assembly.GetName().Version?.ToString(3) ?? "?";
    }
}
