using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace MdViewer;

public enum PaperKind { A4, Letter }

public enum PrintOrientation { Portrait, Landscape }

/// <summary>Preset margin halaman (dari tepi fisik kertas): 0,5", 0,75", 1".</summary>
public enum MarginPreset { Narrow, Normal, Wide }

/// <summary>
/// Salinan (snapshot) isi tab yang dipakai untuk pratinjau/cetak. Diambil sekali saat pratinjau dibuka sehingga
/// penyuntingan sesudahnya tidak mengubah pratinjau, dan editor tidak terkunci.
/// </summary>
public sealed record PrintSnapshot(string Text, string? BaseDirectory, bool BlockRemoteImages, string Title);

/// <summary>
/// Ukuran halaman + margin untuk dokumen cetak (satuan WPF, 1/96 inci). Satu-satunya tempat yang menurunkan
/// PageWidth/PageHeight/ColumnWidth/PagePadding sehingga pratinjau dan cetak langsung menghasilkan paginasi yang sama.
/// </summary>
public sealed record PageLayout(double Width, double Height, double Margin)
{
    // A4 = 210 x 297 mm, Letter = 8,5 x 11 inci.
    const double A4Width = 793.7007874;
    const double A4Height = 1122.519685;
    const double LetterWidth = 816;
    const double LetterHeight = 1056;

    // Ukuran media dari PrintDialog di luar rentang ini dianggap tidak masuk akal (driver bermasalah / tanpa printer): pakai bawaan.
    // Batas atas ~1 m (96.000 DIP) jauh di atas kertas nyata, tetapi di bawah batas nilai yang diterima FlowDocument.PageWidth/Height.
    const double MinPageSize = 200;
    const double MaxPageSize = 96_000;

    /// <summary>A4 potret, margin normal. Dipakai bila tidak ada printer atau ukuran dari printer tidak valid.</summary>
    public static PageLayout Default { get; } = For(PaperKind.A4, PrintOrientation.Portrait, MarginPreset.Normal);

    public static double MarginOf(MarginPreset preset) => preset switch
    {
        MarginPreset.Narrow => 48,
        MarginPreset.Wide => 96,
        _ => 72,
    };

    public static PageLayout For(PaperKind paper, PrintOrientation orientation, MarginPreset margin)
    {
        var (width, height) = paper == PaperKind.Letter ? (LetterWidth, LetterHeight) : (A4Width, A4Height);
        if (orientation == PrintOrientation.Landscape) (width, height) = (height, width);
        return new PageLayout(width, height, MarginOf(margin));
    }

    /// <summary>
    /// Layout dari <c>PrintDialog.PrintableAreaWidth/Height</c>. Nilai itu sebenarnya ukuran media terorientasi (kertas penuh),
    /// bukan area cetak fisik tanpa margin printer; margin diatur oleh <paramref name="margin"/>.
    /// Ukuran tak valid (NaN, tak hingga, terlalu kecil/besar) jatuh ke <see cref="Default"/>.
    /// </summary>
    public static PageLayout FromPrintableArea(double width, double height, MarginPreset margin = MarginPreset.Normal)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height)
            || width < MinPageSize || height < MinPageSize || width > MaxPageSize || height > MaxPageSize)
            return Default with { Margin = MarginOf(margin) };
        return new PageLayout(width, height, MarginOf(margin));
    }

    /// <summary>Margin efektif: tidak pernah lebih dari seperempat sisi terpendek supaya area isi tetap ada.</summary>
    public double EffectiveMargin => Math.Max(0, Math.Min(Margin, Math.Min(Width, Height) / 4));

    public Thickness Padding => new(EffectiveMargin);

    /// <summary>Menyiapkan dokumen untuk dipaginasi pada ukuran halaman ini (satu kolom selebar halaman).</summary>
    public void Apply(FlowDocument document)
    {
        document.PageWidth = Width;
        document.PageHeight = Height;
        document.ColumnWidth = Width;
        document.PagePadding = Padding;
    }
}

/// <summary>
/// Hasil parse Markdown dari satu <see cref="PrintSnapshot"/>, dipakai bersama oleh semua siklus pratinjau jendela yang sama:
/// ganti kertas/margin/orientasi hanya membuat FlowDocument baru dari AST yang sudah ada, tanpa parse ulang. Dokumen besar
/// (>= <see cref="DocumentView.BackgroundParseChars"/>) diparse di thread latar seperti pratinjau utama, sehingga UI tidak membeku;
/// FlowDocument tetap dibuat di thread UI. AST baru dipakai setelah selesai diparse (gambar remote/UNC sudah diblokir di dalamnya).
/// Satu-satunya yang mengubahnya sesudah itu adalah cadangan <c>DocumentView.ReplaceUnloadableImages</c> (gambar yang tak bisa
/// di-decode diganti teks), yang hanya jalan di thread UI dan idempoten: pembuatan dokumen berikutnya melihat hasil yang sama.
/// Parse latar tidak dibatalkan saat jendela ditutup: Markdig tak punya titik pembatalan, jadi token hanya bisa mencegah tugas yang
/// belum mulai; yang berjalan selesai sendiri (hanya CPU, hasilnya dibuang bersama PrintSource oleh GC).
/// </summary>
sealed class PrintSource
{
    public PrintSource(PrintSnapshot snapshot)
    {
        Snapshot = snapshot;
        if (snapshot.Text.Length >= DocumentView.BackgroundParseChars)
        {
            Parsed = Task.Run(() => DocumentView.ParsePrintSnapshot(snapshot));
            return;
        }

        try { Parsed = Task.FromResult(DocumentView.ParsePrintSnapshot(snapshot)); }
        catch (Exception ex) { Parsed = Task.FromException<Markdig.Syntax.MarkdownDocument>(ex); }
    }

    /// <summary>Hanya untuk pengujian: hasil parse disediakan pemanggil (mis. <see cref="TaskCompletionSource{TResult}"/> yang ditahan).</summary>
    internal PrintSource(PrintSnapshot snapshot, Task<Markdig.Syntax.MarkdownDocument> parsed)
    {
        Snapshot = snapshot;
        Parsed = parsed;
    }

    public PrintSnapshot Snapshot { get; }

    public Task<Markdig.Syntax.MarkdownDocument> Parsed { get; }

    /// <summary>FlowDocument baru (tema Terang, kertas putih) berukuran <paramref name="layout"/>. Hanya di thread UI, setelah <see cref="Parsed"/> selesai.</summary>
    public FlowDocument CreateDocument(PageLayout layout)
    {
        var document = DocumentView.BuildPrintDocument(Parsed.GetAwaiter().GetResult());
        layout.Apply(document);
        return document;
    }
}

/// <summary>Pembuatan dokumen cetak dan pencetakan yang dipakai bersama oleh Cetak (Ctrl+P) dan Pratinjau Cetak.</summary>
public static class PrintService
{
    /// <summary>Dokumen cetak (tema Terang, kertas putih) dari snapshot, sudah diberi ukuran halaman sesuai layout.</summary>
    public static FlowDocument CreateDocument(PrintSnapshot snapshot, PageLayout layout)
    {
        var document = DocumentView.BuildPrintDocument(snapshot);
        layout.Apply(document);
        return document;
    }

    /// <summary>
    /// Paginator untuk mencetak/menulis dokumen ini; <paramref name="headerFooter"/> menambah nama dokumen + nomor halaman di kaki
    /// halaman. Jumlah halaman dihitung sinkron bila belum diketahui (dibutuhkan "Halaman X dari N").
    /// </summary>
    public static DocumentPaginator CreatePaginator(FlowDocument document, string title, bool headerFooter)
    {
        var inner = (DynamicDocumentPaginator)((IDocumentPaginatorSource)document).DocumentPaginator;
        if (!inner.IsPageCountValid) inner.ComputePageCount();
        return new HeaderFooterPaginator(inner, document.PagePadding, title, headerFooter);
    }

    /// <summary>Mencetak snapshot lewat <paramref name="dialog"/> yang sudah dikonfirmasi pengguna. Melempar bila driver/antrean gagal.</summary>
    public static void Print(PrintDialog dialog, PrintSnapshot snapshot, PageLayout layout, bool headerFooter)
    {
        // Dokumen baru khusus cetak: dokumen yang sedang tampil di pratinjau tidak boleh ikut dipaginasi ulang oleh PrintDialog.
        var document = CreateDocument(snapshot, layout);
        dialog.PrintDocument(CreatePaginator(document, snapshot.Title, headerFooter), snapshot.Title);
    }
}
