using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MdViewer;

/// <summary>
/// Catatan galat (%LOCALAPPDATA%\MdViewer\crash.log) untuk galat tak tertangani maupun galat yang dipulihkan
/// (mis. gambar rusak di pratinjau). Menulis log tidak pernah melempar.
/// </summary>
static class CrashLog
{
    const long MaxBytes = 512 * 1024;
    static readonly object Gate = new();

    /// <summary>Galat dengan tipe+pesan sama dalam rentang ini tidak membuka dialog baru (lihat <see cref="ShouldShowDialog"/>).</summary>
    public static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(10);

    static readonly Dictionary<string, DateTimeOffset> RecentDialogs = [];

    /// <summary>Lokasi file log; bisa diganti (mis. oleh test) supaya tidak mengotori log pengguna.</summary>
    public static string LogPath { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MdViewer", "crash.log");

    public static void Write(string context, Exception exception, string? logPath = null)
    {
        logPath ??= LogPath;
        Debug.WriteLine($"{context}: {exception}");
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                // Jaga ukuran: log lama yang sudah besar dibuang, bukan dibiarkan tumbuh tanpa batas.
                if (File.Exists(logPath) && new FileInfo(logPath).Length > MaxBytes) File.Delete(logPath);
                // Invariant: kalender/digit budaya pengguna (mis. Buddha, Arab) tidak boleh mengubah format log.
                var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
                File.AppendAllText(logPath,
                    $"[{stamp}] {context}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Debug.WriteLine($"Gagal menulis crash.log: {ex.Message}");
        }
    }

    /// <summary>
    /// True untuk galat operasi (I/O, shell, format, dll.) yang aman dilanjutkan setelah memberi tahu pengguna.
    /// Selain itu (mis. kehabisan memori, NullReference, InvalidOperation) keadaan program tidak bisa dipercaya.
    /// COMException hanya untuk HRESULT yang dikenal (<see cref="IsKnownRecoverableHResult"/>); COM lain bisa berarti
    /// keadaan antarmuka/proses yang rusak.
    /// </summary>
    public static bool IsRecoverable(Exception exception) => exception switch
    {
        OutOfMemoryException or StackOverflowException or AccessViolationException or AppDomainUnloadedException
            or BadImageFormatException or TypeInitializationException => false,
        COMException com => IsKnownRecoverableHResult(com.HResult),
        IOException or UnauthorizedAccessException or NotSupportedException or Win32Exception
            or RegexMatchTimeoutException or FormatException or UriFormatException => true,
        _ => false,
    };

    /// <summary>
    /// HRESULT yang jelas sementara/aman: clipboard dipakai proses lain (CLIPBRD_E_*, 0x800401D0-0x800401D5) dan
    /// galat pengolah gambar (WINCODEC_ERR_*, 0x88982F00-0x88982FFF, mis. COMPONENTNOTFOUND 0x88982F50).
    /// </summary>
    public static bool IsKnownRecoverableHResult(int hresult) => (uint)hresult is >= 0x800401D0 and <= 0x800401D5
        or >= 0x88982F00 and <= 0x88982FFF;

    /// <summary>
    /// False bila galat bertipe+pesan sama sudah ditampilkan dalam <see cref="RepeatWindow"/> terakhir: galat yang berulang
    /// cepat (mis. tiap ketukan/timer) cukup dicatat di log, jangan membuka dialog bertumpuk. True (dan waktunya dicatat) bila baru.
    /// </summary>
    public static bool ShouldShowDialog(Exception exception, DateTimeOffset? now = null)
    {
        var time = now ?? DateTimeOffset.UtcNow;
        var key = exception.GetType().FullName + "|" + exception.Message;
        lock (Gate)
        {
            foreach (var old in RecentDialogs.Where(p => time - p.Value >= RepeatWindow).Select(p => p.Key).ToList())
                RecentDialogs.Remove(old);
            if (RecentDialogs.ContainsKey(key)) return false;
            RecentDialogs[key] = time;
            return true;
        }
    }
}
