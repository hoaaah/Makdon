namespace Makdon;

/// <summary>
/// Ukuran + waktu tulis terakhir file, dipakai untuk melewati pembacaan/hash penuh saat file jelas tidak berubah.
/// Stempel yang dibuat tak lama setelah file ditulis dianggap tidak andal (dua tulis berurutan bisa berbagi
/// timestamp dan ukuran yang sama), sehingga pemeriksa harus jatuh ke hash isi.
/// </summary>
internal readonly record struct FileStamp(long Length, DateTime LastWriteUtc, DateTime TakenUtc)
{
    static readonly TimeSpan RacyWindow = TimeSpan.FromSeconds(2);

    /// <summary>Null bila file tidak ada. Mengikuti symlink ke file sebenarnya. Dapat melempar IOException/UnauthorizedAccessException.</summary>
    public static FileStamp? TryRead(string path)
    {
        var info = new FileInfo(TextFileIO.ResolveLinkTarget(path));
        if (!info.Exists) return null;
        return new FileStamp(info.Length, info.LastWriteTimeUtc, DateTime.UtcNow);
    }

    /// <summary>Seperti <see cref="TryRead"/> tetapi galat I/O menjadi null (stempel tak diketahui -> selalu baca isi).</summary>
    public static FileStamp? TryReadQuietly(string path)
    {
        try { return TryRead(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public bool SameFileAs(FileStamp other) => Length == other.Length && LastWriteUtc == other.LastWriteUtc;

    /// <summary>true bila file sudah "tenang" cukup lama saat stempel diambil sehingga kesamaan stempel dapat dipercaya.</summary>
    public bool IsReliable => TakenUtc - LastWriteUtc > RacyWindow;
}
