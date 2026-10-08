using System.Security.Cryptography;
using System.Text;

namespace Makdon;

/// <summary>
/// Baca/tulis file teks: deteksi encoding, penyimpanan atomik, dan hash isi.
/// Dipisah dari UI supaya mudah diuji.
/// </summary>
internal static class TextFileIO
{
    static TextFileIO()
    {
        // Windows-1252 dkk. tidak tersedia secara bawaan di .NET Core+; perlu didaftarkan.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Membaca file (boleh sedang dibuka proses lain) lalu mendekode teksnya.</summary>
    public static (string Text, Encoding Encoding) Read(string path) => Decode(ReadBytes(path));

    /// <summary>Membaca seluruh byte file tanpa mengunci; editor lain boleh sedang membukanya.</summary>
    public static byte[] ReadBytes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[stream.Length];
        stream.ReadExactly(buffer);
        return buffer;
    }

    /// <summary>
    /// Urutan deteksi: BOM (UTF-8, UTF-16 LE/BE, UTF-32 LE/BE), UTF-8 tanpa BOM yang valid,
    /// lalu Windows-1252. Encoding yang dikembalikan membawa BOM (preamble) hanya bila file aslinya punya.
    /// </summary>
    public static (string Text, Encoding Encoding) Decode(ReadOnlySpan<byte> bytes) => Decode(bytes, out _);

    /// <summary>
    /// Seperti <see cref="Decode(ReadOnlySpan{byte})"/>; <paramref name="lossy"/> true bila file ber-BOM berisi byte yang
    /// tidak valid untuk encoding-nya sehingga sebagian diganti U+FFFD (menyimpan kembali akan menetapkan penggantian itu).
    /// </summary>
    public static (string Text, Encoding Encoding) Decode(ReadOnlySpan<byte> bytes, out bool lossy)
    {
        lossy = false;
        if (bytes.StartsWith<byte>([0xEF, 0xBB, 0xBF]))
            return (DecodeBom(new UTF8Encoding(false, true), new UTF8Encoding(false), bytes[3..], out lossy), new UTF8Encoding(true));

        // UTF-32 LE (FF FE 00 00) harus dicek sebelum UTF-16 LE (FF FE).
        if (bytes.StartsWith<byte>([0xFF, 0xFE, 0x00, 0x00]))
            return (DecodeBom(new UTF32Encoding(false, false, true), new UTF32Encoding(false, false), bytes[4..], out lossy), new UTF32Encoding(false, true));
        if (bytes.StartsWith<byte>([0x00, 0x00, 0xFE, 0xFF]))
            return (DecodeBom(new UTF32Encoding(true, false, true), new UTF32Encoding(true, false), bytes[4..], out lossy), new UTF32Encoding(true, true));
        if (bytes.StartsWith<byte>([0xFF, 0xFE]))
            return (DecodeBom(new UnicodeEncoding(false, false, true), new UnicodeEncoding(false, false), bytes[2..], out lossy), new UnicodeEncoding(false, true));
        if (bytes.StartsWith<byte>([0xFE, 0xFF]))
            return (DecodeBom(new UnicodeEncoding(true, false, true), new UnicodeEncoding(true, false), bytes[2..], out lossy), new UnicodeEncoding(true, true));

        try
        {
            return (new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes), new UTF8Encoding(false));
        }
        catch (DecoderFallbackException)
        {
            return Decode1252(bytes);
        }
    }

    // Dekode ketat dulu; bila ada byte tak valid, jatuh ke dekode longgar (U+FFFD) dan tandai lossy.
    static string DecodeBom(Encoding strict, Encoding lenient, ReadOnlySpan<byte> body, out bool lossy)
    {
        try
        {
            lossy = false;
            return strict.GetString(body);
        }
        catch (DecoderFallbackException)
        {
            lossy = true;
            return lenient.GetString(body);
        }
    }

    static (string Text, Encoding Encoding) Decode1252(ReadOnlySpan<byte> bytes)
    {
        var cp1252 = Encoding.GetEncoding(1252);
        var text = cp1252.GetString(bytes);

        // Pastikan simpan-ulang menghasilkan byte yang sama; kalau tidak (byte yang tak terdefinisi
        // di 1252), jatuh ke Latin-1 yang memetakan semua 256 byte satu-satu.
        if (cp1252.GetBytes(text).AsSpan().SequenceEqual(bytes)) return (text, cp1252);
        return (Encoding.Latin1.GetString(bytes), Encoding.Latin1);
    }

    /// <summary>Encoding + BOM -> byte yang ditulis ke disk. Bila encoding tak bisa memuat teks, naik ke UTF-8.</summary>
    public static byte[] Encode(string text, Encoding encoding, out Encoding actual)
    {
        actual = encoding;
        byte[] body;
        try
        {
            var strict = (Encoding)encoding.Clone();
            strict.EncoderFallback = EncoderFallback.ExceptionFallback;
            body = strict.GetBytes(text);
        }
        catch (EncoderFallbackException)
        {
            // Mis. file Windows-1252 yang kini berisi karakter di luar 1252. Daripada diam-diam
            // menggantinya dengan '?', simpan sebagai UTF-8 (tanpa BOM).
            try
            {
                var utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
                body = utf8.GetBytes(text);
                actual = new UTF8Encoding(false);
            }
            catch (EncoderFallbackException)
            {
                // Surrogate yatim: tak ada encoding yang bisa memuatnya; pakai penggantian bawaan.
                body = encoding.GetBytes(text);
            }
        }

        var preamble = actual.GetPreamble();
        if (preamble.Length == 0) return body;

        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }

    /// <summary>
    /// Menulis secara atomik: ke file sementara di folder yang sama, lalu menggantikan file tujuan.
    /// Bila gagal, file tujuan tetap utuh dan file sementara dibersihkan.
    /// Mengembalikan encoding yang benar-benar dipakai dan hash byte yang ditulis.
    /// </summary>
    public static (Encoding Encoding, string Hash) Write(string path, string text, Encoding encoding)
    {
        var bytes = Encode(text, encoding, out var actual);
        WriteBytesAtomic(path, bytes);
        return (actual, Hash(bytes));
    }

    public static void WriteBytesAtomic(string path, byte[] bytes)
    {
        var target = ResolveLinkTarget(Path.GetFullPath(path));
        var directory = Path.GetDirectoryName(target)!;
        // Nama pendek (~md + 8 hex) supaya tidak melewati batas panjang path pada nama file yang panjang.
        var temp = Path.Combine(directory, $"~md{Guid.NewGuid().ToString("N")[..8]}.tmp");

        FileStream stream;
        try
        {
            stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PathTooLongException)
        {
            // Folder tak bisa ditulisi (tetapi file-nya mungkin bisa) atau nama sementara terlalu panjang: tulis langsung.
            // Galat I/O lain (disk penuh, pelanggaran berbagi, folder hilang) tidak dicoba lagi lewat jalur non-atomik
            // yang lebih berisiko; galatnya diteruskan ke pemanggil.
            WriteInPlace(target, bytes);
            return;
        }

        try
        {
            using (stream)
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(target))
            {
                try
                {
                    File.Replace(temp, target, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                catch (IOException) when (File.Exists(temp))
                {
                    // Beberapa filesystem (mis. share jaringan tertentu) tak mendukung ReplaceFile.
                    File.Move(temp, target, overwrite: true);
                }
            }
            else
            {
                File.Move(temp, target);
            }
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    // Cadangan non-atomik. Dipakai hanya bila file sementara tidak bisa dibuat. Menulis dari awal lalu memotong sisa
    // di akhir (bukan mengosongkan dulu): bila penulisan gagal di tengah, isi lama tidak sudah lebih dulu hilang seluruhnya.
    static void WriteInPlace(string target, byte[] bytes)
    {
        using var stream = new FileStream(target, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        stream.Write(bytes);
        stream.SetLength(bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>Hash isi file (hex) untuk mendeteksi perubahan tanpa membandingkan teks.</summary>
    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>Bila path adalah symlink, mengembalikan file sebenarnya (dipakai untuk menulis dan mengawasi); selain itu path apa adanya.</summary>
    public static string ResolveLinkTarget(string path)
    {
        try
        {
            if (new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true) is { } final) return final.FullName;
        }
        catch (IOException)
        {
        }
        return path;
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
