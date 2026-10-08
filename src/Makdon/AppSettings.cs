using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Makdon;

/// <summary>Satu tab pada sesi tersimpan. Mode disimpan sebagai teks agar nilai tak dikenal tidak merusak seluruh file.</summary>
public sealed class SessionTab
{
    public string Path { get; set; } = "";
    public string Mode { get; set; } = nameof(ViewMode.Split);
    public int CaretOffset { get; set; }

    [JsonIgnore]
    public ViewMode ParsedMode => Enum.TryParse<ViewMode>(Mode, ignoreCase: true, out var m) && Enum.IsDefined(m) ? m : ViewMode.Split;
}

public sealed class SessionState
{
    public List<SessionTab> Tabs { get; set; } = [];
    public int ActiveIndex { get; set; }
}

/// <summary>
/// Pengaturan pengguna (%APPDATA%\Makdon\settings.json): berkas terakhir, sesi, tema, zoom, dan pemblokiran gambar remote.
/// Memuat/menyimpan tidak pernah melempar: file hilang atau korup -> nilai bawaan.
/// </summary>
public sealed class AppSettings
{
    public const int MaxRecentFiles = 10;

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    // Berkas yang sengaja dihapus dari daftar terakhir oleh instance ini; agar tidak muncul lagi saat digabung dengan isi file.
    readonly HashSet<string> removedRecent = new(StringComparer.OrdinalIgnoreCase);

    public List<string> RecentFiles { get; set; } = [];
    public SessionState? Session { get; set; }

    /// <summary>Tema sebagai teks (System/Light/Dark) agar nilai tak dikenal tidak merusak seluruh file.</summary>
    public string Theme { get; set; } = nameof(AppThemeMode.System);

    /// <summary>Zoom editor/pratinjau dalam persen (<see cref="ZoomLevel.Min"/>..<see cref="ZoomLevel.Max"/>).</summary>
    public int ZoomPercent { get; set; } = ZoomLevel.Default;

    /// <summary>
    /// Pratinjau dan cetak mengganti gambar http(s) dengan teks penanda (mencegah pelacakan lewat gambar remote).
    /// Menu Tampilan &gt; "Muat gambar remote" mematikannya.
    /// </summary>
    public bool BlockRemoteImages { get; set; } = true;

    [JsonIgnore]
    public AppThemeMode ParsedTheme => ThemeManager.Parse(Theme);

    /// <summary>
    /// %APPDATA%\Makdon\settings.json (terpasang) atau &lt;folder exe&gt;\data\settings.json (portable). Bila folder portable
    /// tidak bisa ditulisi, <see cref="Save"/> gagal (false) dan pengaturan hanya di memori; tidak pindah ke %APPDATA%.
    /// </summary>
    public static string DefaultPath => AppPaths.Current.SettingsPath;

    public static AppSettings Load(string? path = null)
    {
        try
        {
            var json = File.ReadAllText(path ?? DefaultPath, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.Sanitize();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or NotSupportedException or ArgumentException)
        {
            // Hilang, tak terbaca, atau korup: mulai dari bawaan.
            return new AppSettings();
        }
    }

    /// <summary>Menyimpan secara atomik. false bila gagal (mis. folder tak bisa ditulis); tidak melempar.</summary>
    public bool Save(string? path = null)
    {
        try
        {
            var target = path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
            TextFileIO.Write(target, JsonSerializer.Serialize(this, JsonOptions), new UTF8Encoding(false));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Seperti <see cref="Save"/> tetapi memuat ulang file lebih dulu dan menggabungkan <see cref="RecentFiles"/>
    /// (milik instance ini di depan, sisanya dari file yang mungkin ditulis instance lain), sehingga salinan usang tidak
    /// menimpa. <paramref name="keepStoredSession"/>: pertahankan Session yang tersimpan (instance dibuka lewat argumen).
    /// </summary>
    public bool SaveMerged(string? path = null, bool keepStoredSession = false)
    {
        var stored = Load(path);
        foreach (var file in stored.RecentFiles)
        {
            if (removedRecent.Contains(file)) continue;
            if (!RecentFiles.Contains(file, StringComparer.OrdinalIgnoreCase)) RecentFiles.Add(file);
        }
        if (RecentFiles.Count > MaxRecentFiles) RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
        if (keepStoredSession) Session = stored.Session;
        return Save(path);
    }

    /// <summary>Menaruh path di urutan teratas (tanpa duplikat, tanpa membedakan huruf besar/kecil), maksimal 10.</summary>
    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        removedRecent.Remove(path);
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecentFiles) RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
    }

    public void RemoveRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        removedRecent.Add(path);
    }

    // JSON bisa diedit tangan atau rusak sebagian: buang entri tak masuk akal.
    void Sanitize()
    {
        Theme = ThemeManager.Parse(Theme).ToString();
        ZoomPercent = ZoomLevel.Clamp(ZoomPercent);

        RecentFiles = (RecentFiles ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxRecentFiles)
            .ToList();

        if (Session is null) return;
        Session.Tabs = (Session.Tabs ?? [])
            .Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Path))
            .ToList();
        foreach (var tab in Session.Tabs) tab.CaretOffset = Math.Max(0, tab.CaretOffset);
        if (Session.ActiveIndex < 0 || Session.ActiveIndex >= Session.Tabs.Count) Session.ActiveIndex = 0;
    }
}
