using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MdViewer;

/// <summary>
/// Membungkus paginator FlowDocument: tiap halaman diberi kaki halaman (nama dokumen di kiri, "Halaman X dari N" di kanan)
/// yang digambar di dalam margin bawah, jadi paginasi isi tidak berubah. Dipakai sama persis oleh pratinjau dan cetak.
/// Wadah ini sengaja bukan DynamicDocumentPaginator: N baru diketahui setelah paginasi selesai, jadi pratinjau
/// (PreviewBuild) baru menulis halaman sesudah itu; untuk cetak langsung PageCount dihitung sinkron.
/// </summary>
sealed class HeaderFooterPaginator(DynamicDocumentPaginator inner, Thickness padding, string title, bool headerFooter)
    : DocumentPaginator, IDocumentPaginatorSource
{
    const double FontSize = 10;
    const double MinFooterEdgeDistance = 24; // 0,25 inci
    static readonly Brush TextBrush = CreateBrush();
    static readonly Typeface Face = new("Segoe UI");

    static Brush CreateBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
        brush.Freeze();
        return brush;
    }

    public override bool IsPageCountValid => inner.IsPageCountValid;

    public override int PageCount => inner.PageCount;

    public override Size PageSize
    {
        get => inner.PageSize;
        set => inner.PageSize = value;
    }

    public override IDocumentPaginatorSource Source => this;

    // Penulis XPS/PrintDialog memanggil ini sebelum membaca PageCount; tanpa diteruskan, paginator FlowDocument melaporkan 0 halaman.
    public override void ComputePageCount() => inner.ComputePageCount();

    DocumentPaginator IDocumentPaginatorSource.DocumentPaginator => this;

    public override DocumentPage GetPage(int pageNumber)
    {
        var page = inner.GetPage(pageNumber);
        if (!headerFooter || page == DocumentPage.Missing || page.Visual is null) return page;

        // Paginator dasar bisa mengembalikan Visual yang sama untuk halaman yang sama; lepaskan dulu dari pembungkus sebelumnya
        // (yang masih dipegang pemanggil) supaya tidak melempar "Specified Visual is already a child of another Visual".
        if (VisualTreeHelper.GetParent(page.Visual) is ContainerVisual previous) previous.Children.Remove(page.Visual);

        var container = new ContainerVisual();
        container.Children.Add(page.Visual);
        container.Children.Add(DrawFooter(page.Size, pageNumber + 1));

        var wrapped = new DocumentPage(container, page.Size, page.BleedBox, page.ContentBox);
        wrapped.PageDestroyed += (_, _) => page.Dispose();
        return wrapped;
    }

    DrawingVisual DrawFooter(Size pageSize, int number)
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();

        var left = padding.Left;
        var right = pageSize.Width - padding.Right;
        var width = Math.Max(0, right - left);
        var counter = inner.IsPageCountValid ? $"Halaman {number} dari {inner.PageCount}" : $"Halaman {number}";

        var pageText = Format(counter);
        var nameText = Format(title);
        nameText.MaxTextWidth = Math.Max(1, width - pageText.WidthIncludingTrailingWhitespace - 16);
        nameText.MaxLineCount = 1;
        nameText.Trimming = TextTrimming.CharacterEllipsis;

        // Di tengah margin bawah, tetapi tidak lebih dekat dari 0,25" ke tepi kertas: margin Sempit (48) menaruh teks sekitar 17 DIP
        // dari tepi, di dalam zona yang tak bisa dicetak banyak printer.
        // Tetap di dalam margin bawah (tidak menimpa isi) untuk margin yang sangat kecil.
        var fromBottom = Math.Min(Math.Max(padding.Bottom / 2 + pageText.Height / 2, MinFooterEdgeDistance + pageText.Height), padding.Bottom);
        var y = pageSize.Height - fromBottom;
        dc.DrawText(nameText, new Point(left, y));
        dc.DrawText(pageText, new Point(right - pageText.WidthIncludingTrailingWhitespace, y));
        return visual;
    }

    static FormattedText Format(string text) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, FontSize, TextBrush, 1.0);
}
