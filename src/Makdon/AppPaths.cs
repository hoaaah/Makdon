using System.Security.Cryptography;
using System.Text;

namespace Makdon;

/// <summary>
/// Mode (terpasang/portable) dan lokasi data. Portable = berkas <see cref="PortableMarkerFileName"/> ada di samping exe;
/// datanya di &lt;folder exe&gt;\data\ dan tidak pernah jatuh ke %APPDATA%. Terpasang memakai %APPDATA% dan %LOCALAPPDATA%.
/// Folder exe bisa disuntik (<see cref="Detect"/>) supaya test tidak menyentuh folder data pengguna.
/// </summary>
sealed class AppPaths
{
    public const string PortableMarkerFileName = "Makdon.portable";
    public const string ExeFileName = "Makdon.exe";
    const string AppFolderName = "Makdon";

    AppPaths(string exeDirectory, string exePath, bool isPortable)
    {
        ExeDirectory = exeDirectory;
        ExePath = exePath;
        IsPortable = isPortable;
        DataDirectory = isPortable ? Path.Combine(exeDirectory, "data") : null;
    }

    /// <summary>Instance untuk proses ini; diisi sekali saat startup. Test boleh menggantinya.</summary>
    public static AppPaths Current { get; set; } = DetectForProcess();

    public bool IsPortable { get; }
    public string ExeDirectory { get; }
    public string ExePath { get; }

    /// <summary>&lt;folder exe&gt;\data (hanya mode portable; null bila terpasang).</summary>
    public string? DataDirectory { get; }

    public string SettingsPath => DataDirectory is { } data
        ? Path.Combine(data, "settings.json")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName, "settings.json");

    public string CrashLogPath => DataDirectory is { } data
        ? Path.Combine(data, "crash.log")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName, "crash.log");

    /// <summary>
    /// Pembeda nama mutex/pipe single-instance. Kosong untuk mode terpasang; portable memakai hash path folder exe supaya
    /// berkas tidak diteruskan ke instance terpasang (atau portable lain) yang sedang berjalan.
    /// </summary>
    public string SingleInstanceScope
    {
        get
        {
            if (!IsPortable) return "";
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ExeDirectory)).ToUpperInvariant();
            return "p" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)), 0, 8);
        }
    }

    /// <param name="exeDirectory">Folder tempat Makdon.exe berada.</param>
    /// <param name="exePath">Path exe; bawaan &lt;exeDirectory&gt;\Makdon.exe.</param>
    /// <param name="markerExists">Penguji keberadaan penanda portable (seam test); bawaan <see cref="File.Exists"/>.</param>
    public static AppPaths Detect(string exeDirectory, string? exePath = null, Func<string, bool>? markerExists = null)
    {
        var directory = Path.GetFullPath(exeDirectory);
        var portable = (markerExists ?? File.Exists)(Path.Combine(directory, PortableMarkerFileName));
        return new AppPaths(directory, exePath ?? Path.Combine(directory, ExeFileName), portable);
    }

    static AppPaths DetectForProcess()
    {
        // "dotnet Makdon.dll" membuat ProcessPath = dotnet.exe; hanya percaya bila namanya Makdon.exe.
        var processPath = Environment.ProcessPath;
        var exePath = processPath is not null && string.Equals(Path.GetFileName(processPath), ExeFileName, StringComparison.OrdinalIgnoreCase)
            ? processPath
            : null;
        return Detect(AppContext.BaseDirectory, exePath);
    }

    /// <summary>
    /// Mode portable: apakah folder data bisa ditulisi (dibuat bila perlu, lalu berkas uji dihapus). Selalu true bila
    /// terpasang. Tidak melempar. Bila false, pengaturan hanya hidup di memori (tidak pindah ke %APPDATA%).
    /// </summary>
    public bool IsDataDirectoryWritable()
    {
        if (DataDirectory is not { } data) return true;
        try
        {
            Directory.CreateDirectory(data);
            var probe = Path.Combine(data, ".tulis-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }
}
