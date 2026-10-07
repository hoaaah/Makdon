using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.GenericAttributes;
using Markdig.Extensions.MediaLinks;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Wpf;

namespace MdViewer;

/// <summary>Jenis URL menurut allowlist ekspor/pratinjau (lihat <see cref="MarkdownSupport.ClassifyUrl"/>).</summary>
public enum UrlKind
{
    /// <summary>Tanpa skema: path relatif, "#anchor", "?query", atau kosong.</summary>
    Relative,
    Http,
    Mailto,
    /// <summary>data:image/png|jpeg|gif|webp.</summary>
    DataImage,
    /// <summary>Path lokal: huruf drive ("C:\..."), atau file:/// tanpa host.</summary>
    LocalFile,
    /// <summary>Selain itu: javascript:, vbscript:, data: lain, file://host, UNC, "//host", skema tak dikenal.</summary>
    Blocked,
}

/// <summary>Pipeline Markdig (pratinjau dan ekspor HTML terpisah) dan helper pemrosesan tautan/gambar.</summary>
static class MarkdownSupport
{
    /// <summary>Teks pengganti gambar remote/UNC yang diblokir di pratinjau dan cetak.</summary>
    public const string BlockedRemoteImageText = "[gambar remote diblokir]";

    /// <summary>Teks pengganti gambar <c>data:</c> di pratinjau/cetak (WPF tidak bisa memuatnya lewat URI).</summary>
    public const string UnsupportedPreviewImageText = "[gambar data: tidak ditampilkan di pratinjau]";

    /// <summary>Gambar lokal yang boleh disematkan ke ekspor (data URI) dan batas ukurannya.</summary>
    public const long MaxEmbeddedImageBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Anggaran total ukuran data URI yang disematkan dalam satu ekspor (tiap kemunculan dihitung, karena tiap kemunculan
    /// menambah ukuran HTML). Setelah habis, gambar berikutnya dibiarkan sebagai path relatif.
    /// </summary>
    public const long MaxTotalEmbeddedBytes = 30L * 1024 * 1024;

    static readonly Regex SchemeRegex = new(@"^([A-Za-z][A-Za-z0-9+.\-]*):", RegexOptions.CultureInvariant);
    static readonly Regex DataImageRegex = new(@"^data:image/(png|jpeg|gif|webp)[;,]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    static readonly Dictionary<string, string> EmbeddableImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
    };

    /// <summary>AutoIdentifiers gaya GitHub: heading mendapat id (slug) yang dipakai tautan "#anchor". Untuk pratinjau WPF.</summary>
    public static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseSupportedExtensions()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .Build();

    /// <summary>
    /// Pipeline khusus ekspor HTML: HTML mentah di dalam markdown di-escape (tidak diteruskan), dan ekstensi yang bisa
    /// menyuntikkan atribut/iframe sembarang dimatikan. URL disaring terpisah oleh <see cref="SanitizeForExport"/>.
    /// </summary>
    public static readonly MarkdownPipeline ExportPipeline = BuildExportPipeline();

    static MarkdownPipeline BuildExportPipeline()
    {
        var builder = new MarkdownPipelineBuilder()
            .UseSupportedExtensions()
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
            .DisableHtml();
        builder.Extensions.TryRemove<GenericAttributesExtension>(); // "{onclick=...}" pada elemen
        builder.Extensions.TryRemove<MediaLinkExtension>();         // gambar yang diubah menjadi <iframe>
        return builder.Build();
    }

    // ---- Klasifikasi URL (allowlist) ----

    /// <summary>
    /// Mengklasifikasikan URL tautan/gambar. Spasi dan karakter kontrol diabaikan saat mendeteksi skema
    /// (browser membuang tab/baris baru di dalam "java\tscript:").
    /// </summary>
    public static UrlKind ClassifyUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return UrlKind.Relative;

        var compact = string.Concat(url.Where(c => !char.IsControl(c) && !char.IsWhiteSpace(c)));
        if (compact.StartsWith(@"\\") || compact.StartsWith("//") || compact.StartsWith(@"/\") || compact.StartsWith(@"\/"))
            return UrlKind.Blocked; // UNC atau "//host" (di file:// berarti share SMB)

        var match = SchemeRegex.Match(compact);
        if (!match.Success) return UrlKind.Relative;

        var scheme = match.Groups[1].Value.ToLowerInvariant();
        if (scheme.Length == 1) return UrlKind.LocalFile; // "C:\..." / "C:/..."
        switch (scheme)
        {
            case "http":
            case "https":
                return UrlKind.Http;
            case "mailto":
                return UrlKind.Mailto;
            case "data":
                return DataImageRegex.IsMatch(compact) ? UrlKind.DataImage : UrlKind.Blocked;
            case "file":
                // file:///C:/x (tanpa host) lokal; file://host/share adalah SMB.
                var rest = compact[(match.Length)..];
                return !rest.StartsWith("//") || rest.StartsWith("///") ? UrlKind.LocalFile : UrlKind.Blocked;
            default:
                return UrlKind.Blocked;
        }
    }

    /// <summary>Boleh dijadikan href di HTML ekspor: http, https, mailto, "#anchor", atau path relatif.</summary>
    public static bool IsSafeLinkUrl(string? url) => ClassifyUrl(url) is UrlKind.Relative or UrlKind.Http or UrlKind.Mailto;

    // ---- Pratinjau & cetak ----

    /// <summary>
    /// Gambar relatif ("./img/a.png") diubah ke URI absolut berdasarkan lokasi file markdown. Pratinjau hanya boleh memuat
    /// file lokal dan http(s): gambar ke path UNC (<c>\\host\...</c>, <c>file://host/...</c>) dan skema lain (ftp:, dll.)
    /// selalu diganti teks penanda agar membuka dokumen tidak memicu koneksi jaringan; gambar http(s) ikut diganti bila
    /// <paramref name="blockRemote"/> true (mencegah pelacakan lewat piksel remote). Gambar <c>data:</c> juga diganti
    /// penanda: WPF (<c>BitmapImage(Uri)</c>) tidak mengenal skema itu dan akan menggagalkan seluruh pratinjau.
    /// Ekspor HTML memakai allowlist sendiri (<see cref="SanitizeForExport"/>), bukan metode ini.
    /// </summary>
    public static void ResolveImageUrls(MarkdownDocument parsed, string? baseDir, bool blockRemote = false)
    {
        foreach (var link in parsed.Descendants<LinkInline>().Where(l => l.IsImage).ToList())
        {
            var url = link.Url;
            if (string.IsNullOrWhiteSpace(url)) continue;

            switch (ClassifyUrl(url))
            {
                case UrlKind.DataImage:
                    BlockImage(link, UnsupportedPreviewImageText);
                    continue;
                case UrlKind.Blocked or UrlKind.Mailto:
                    BlockImage(link); // UNC, "//host", file://host, ftp:, skema tak dikenal, dll.
                    continue;
                case UrlKind.Http:
                    if (blockRemote) BlockImage(link);
                    continue;
            }

            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
                {
                    // Setelah klasifikasi hanya file: yang sampai ke sini; skema lain tetap dijaga (jangan pernah dimuat WPF).
                    if (absolute.Scheme != Uri.UriSchemeFile || absolute.IsUnc || !string.IsNullOrEmpty(absolute.Host))
                    {
                        BlockImage(link);
                        continue;
                    }
                    ResolveLocalImage(link, absolute.LocalPath, baseDir);
                    continue;
                }

                if (baseDir is null) continue;
                ResolveLocalImage(link, Path.Combine(baseDir, Uri.UnescapeDataString(url)), baseDir);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or UriFormatException)
            {
                // URL bukan path yang valid; biarkan apa adanya.
            }
        }
    }

    static void ResolveLocalImage(LinkInline link, string path, string? baseDir)
    {
        var full = Path.GetFullPath(path);
        if (!IsAllowedLocalPath(full, baseDir))
        {
            BlockImage(link);
            return;
        }
        if (File.Exists(full)) link.Url = new Uri(full).AbsoluteUri;
    }

    /// <summary>
    /// Path lokal boleh dipakai bila berada di drive (huruf) mana pun; path UNC hanya bila file markdown sendiri ada di share yang sama.
    /// </summary>
    public static bool IsAllowedLocalPath(string fullPath, string? baseDir)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root)) return false;
        if (!root.StartsWith(@"\\", StringComparison.Ordinal)) return true;

        if (baseDir is null) return false;
        var baseRoot = Path.GetPathRoot(Path.GetFullPath(baseDir));
        return baseRoot is not null && string.Equals(root, baseRoot, StringComparison.OrdinalIgnoreCase);
    }

    static void BlockImage(LinkInline image, string text = BlockedRemoteImageText) =>
        image.ReplaceBy(new LiteralInline(text), copyChildren: false);

    // ---- Ekspor HTML ----

    /// <summary>
    /// Menyaring URL dokumen untuk ekspor: tautan hanya http/https/mailto/#anchor/path relatif (selain itu menjadi "#");
    /// gambar hanya http/https, data:image/(png|jpeg|gif|webp), atau gambar lokal. Gambar lokal (relatif maupun absolut)
    /// disematkan sebagai data URI bila bertipe png/jpg/gif/webp, &lt;= 2 MB per gambar, dan berada di bawah folder dokumen
    /// (<paramref name="baseDir"/>; gambar di luarnya diganti teks penanda agar file lain di mesin tidak ikut terekspor),
    /// selama anggaran total <paramref name="maxTotalEmbeddedBytes"/> belum habis. Gambar yang sama dibaca/dikodekan sekali
    /// (cache per path lengkap). Bila tidak disematkan, path relatif dibiarkan relatif (tidak pernah menjadi file:///C:/...),
    /// sedangkan path absolut lokal diganti teks penanda sehingga path mesin tidak bocor.
    /// </summary>
    public static void SanitizeForExport(MarkdownDocument parsed, string? baseDir, long maxTotalEmbeddedBytes = MaxTotalEmbeddedBytes)
    {
        var embedder = new ImageEmbedder(baseDir, maxTotalEmbeddedBytes);
        foreach (var link in parsed.Descendants<LinkInline>().ToList())
        {
            var url = link.GetDynamicUrl?.Invoke() ?? link.Url;
            link.GetDynamicUrl = null;

            if (link.IsImage) SanitizeImage(link, url, embedder);
            else link.Url = IsSafeLinkUrl(url) ? url : "#";
        }

        foreach (var auto in parsed.Descendants<AutolinkInline>().ToList())
        {
            if (!auto.IsEmail && !IsSafeLinkUrl(auto.Url)) auto.ReplaceBy(new LiteralInline(auto.Url), copyChildren: false);
        }
    }

    const string OutsideDocumentFolderReason = "gambar di luar folder dokumen tidak disertakan";

    static void SanitizeImage(LinkInline image, string? url, ImageEmbedder embedder)
    {
        switch (ClassifyUrl(url))
        {
            case UrlKind.Http:
            case UrlKind.DataImage:
                image.Url = url;
                return;

            case UrlKind.Relative:
                image.Url = url;
                if (embedder.HasBaseDirectory && !string.IsNullOrWhiteSpace(url))
                {
                    var result = embedder.TryEmbed(url, relative: true, out var embedded);
                    if (result == EmbedResult.Embedded) image.Url = embedded;
                    else if (result == EmbedResult.OutsideDocumentFolder) ReplaceWithText(image, OutsideDocumentFolderReason);
                }
                return;

            case UrlKind.LocalFile:
                var local = embedder.TryEmbed(url!, relative: false, out var data);
                if (local == EmbedResult.Embedded) image.Url = data;
                else ReplaceWithText(image, local == EmbedResult.OutsideDocumentFolder ? OutsideDocumentFolderReason : "gambar lokal tidak disertakan");
                return;

            default:
                ReplaceWithText(image, "gambar diblokir");
                return;
        }
    }

    static void ReplaceWithText(LinkInline image, string reason)
    {
        var alt = string.Concat(image.Descendants<LiteralInline>().Select(l => l.Content.ToString()));
        image.ReplaceBy(new LiteralInline(alt.Length > 0 ? $"[{reason}: {alt}]" : $"[{reason}]"), copyChildren: false);
    }

    enum EmbedResult { Embedded, NotEmbedded, OutsideDocumentFolder }

    /// <summary>Status satu ekspor: anggaran total yang tersisa dan cache data URI per path lengkap.</summary>
    sealed class ImageEmbedder
    {
        readonly string? baseDir;
        readonly string? baseWithSeparator;
        readonly Dictionary<string, CachedImage> cache = new(StringComparer.OrdinalIgnoreCase);
        long remaining;

        // DataUri null = tak bisa disematkan (hilang, terlalu besar); Outside = file nyata (symlink) yang menunjuk ke luar folder dokumen.
        sealed record CachedImage(string? DataUri, bool Outside);

        public ImageEmbedder(string? baseDir, long budget)
        {
            this.baseDir = baseDir;
            remaining = budget;
            if (baseDir is null) return;
            try
            {
                var full = Path.GetFullPath(baseDir);
                baseWithSeparator = Path.EndsInDirectorySeparator(full) ? full : full + Path.DirectorySeparatorChar;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Folder dokumen tak valid: tidak ada yang dianggap "di bawah folder dokumen".
            }
        }

        public bool HasBaseDirectory => baseDir is not null;

        bool IsInsideDocumentFolder(string fullPath) =>
            baseWithSeparator is not null && fullPath.StartsWith(baseWithSeparator, StringComparison.OrdinalIgnoreCase);

        public EmbedResult TryEmbed(string url, bool relative, out string? dataUri)
        {
            dataUri = null;
            try
            {
                string full;
                if (relative)
                {
                    // Query/fragment bukan bagian dari nama file.
                    var pathPart = url.Split('#', 2)[0].Split('?', 2)[0];
                    if (pathPart.Length == 0) return EmbedResult.NotEmbedded;
                    full = Path.GetFullPath(Path.Combine(baseDir!, Uri.UnescapeDataString(pathPart)));
                }
                else
                {
                    var compact = string.Concat(url.Where(c => !char.IsControl(c) && !char.IsWhiteSpace(c)));
                    full = Path.GetFullPath(Uri.TryCreate(compact, UriKind.Absolute, out var uri) && uri.IsFile
                        ? uri.LocalPath
                        : compact);
                }

                // Privasi: hanya gambar di bawah folder dokumen yang boleh ikut terekspor (dokumen yang dibagikan tidak boleh
                // membawa file lain milik pengguna, mis. "![](C:\Users\...\foto.png)" atau "../../rahasia.png").
                if (!IsAllowedLocalPath(full, baseDir) || !IsInsideDocumentFolder(full)) return EmbedResult.OutsideDocumentFolder;
                if (!EmbeddableImageTypes.TryGetValue(Path.GetExtension(full), out var mime)) return EmbedResult.NotEmbedded;

                if (!cache.TryGetValue(full, out var entry))
                {
                    var info = new FileInfo(full);
                    if (info.Exists && !IsInsideDocumentFolder(TextFileIO.ResolveLinkTarget(full)))
                        entry = new CachedImage(null, Outside: true); // symlink di dalam folder yang menunjuk ke luar
                    else if (!info.Exists || info.Length > MaxEmbeddedImageBytes)
                        entry = new CachedImage(null, Outside: false);
                    else if (EncodedLength(info.Length, mime) > remaining)
                        return EmbedResult.NotEmbedded; // anggaran habis: tidak perlu membaca file
                    else
                        entry = new CachedImage($"data:{mime};base64,{Convert.ToBase64String(TextFileIO.ReadBytes(full))}", Outside: false);
                    cache[full] = entry;
                }

                if (entry.Outside) return EmbedResult.OutsideDocumentFolder;
                if (entry.DataUri is not { } data || data.Length > remaining) return EmbedResult.NotEmbedded;

                remaining -= data.Length; // tiap kemunculan menambah ukuran HTML, jadi dihitung per kemunculan
                dataUri = data;
                return EmbedResult.Embedded;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException
                                           or IOException or UnauthorizedAccessException or UriFormatException)
            {
                return EmbedResult.NotEmbedded;
            }
        }

        static long EncodedLength(long bytes, string mime) => $"data:{mime};base64,".Length + (bytes + 2) / 3 * 4;
    }

    /// <summary>
    /// Menyelesaikan tautan dokumen ke path file markdown lokal, atau null bila tidak boleh dibuka. Tidak melakukan I/O apa pun
    /// (File.Exists dipanggil pemanggil hanya setelah path lolos di sini), sehingga tautan UNC tidak memicu koneksi SMB/NTLM.
    /// Urutan: skema <c>file:</c> tanpa host dikonversi lewat <c>LocalPath</c> (berhost ditolak), tautan relatif didekode lalu digabung
    /// dengan <paramref name="baseDir"/>; path perangkat (<c>\\?\</c>, <c>\\.\</c>) ditolak; ekstensi harus markdown; terakhir
    /// <see cref="IsAllowedLocalPath"/> (UNC hanya di share yang sama dengan dokumen). <paramref name="anchor"/> berisi bagian setelah '#'.
    /// </summary>
    public static string? ResolveLinkTarget(string? baseDir, string? url, out string? anchor)
    {
        anchor = null;
        if (string.IsNullOrWhiteSpace(url) || url.StartsWith('#')) return null;

        var parts = url.Split('#', 2);
        var pathPart = parts[0];
        var fragment = parts.Length > 1 ? parts[1] : null;

        try
        {
            string combined;
            if (Uri.TryCreate(pathPart, UriKind.Absolute, out var absolute) && absolute.Scheme != Uri.UriSchemeFile) return null;

            if (absolute is not null && pathPart.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                if (absolute.IsUnc || !string.IsNullOrEmpty(absolute.Host)) return null;
                combined = absolute.LocalPath;
            }
            else
            {
                // Path relatif, ter-percent-encode, atau path mentah ("C:\x.md", "\\host\share\x.md"; yang rooted menimpa baseDir).
                if (baseDir is null && !Path.IsPathRooted(pathPart)) return null;
                combined = Path.Combine(baseDir ?? "", Uri.UnescapeDataString(pathPart));
            }

            if (IsDevicePath(combined)) return null;
            var full = Path.GetFullPath(combined);
            if (IsDevicePath(full) || !MarkdownFiles.IsMarkdown(full) || !IsAllowedLocalPath(full, baseDir)) return null;

            anchor = fragment;
            return full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or UriFormatException)
        {
            return null;
        }
    }

    /// <summary>Awalan path perangkat/extended-length Win32 (<c>\\?\</c>, <c>\\.\</c>, juga dengan '/'), yang melewati normalisasi path.</summary>
    static bool IsDevicePath(string path) =>
        path.Length >= 4
        && path[0] is '\\' or '/' && path[1] is '\\' or '/'
        && path[2] is '?' or '.'
        && path[3] is '\\' or '/';
}

