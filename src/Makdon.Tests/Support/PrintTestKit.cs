using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Makdon.Tests.Support;

/// <summary>Bahan bersama test pratinjau/cetak: halaman kecil supaya cepat banyak halaman, dokumen contoh, pembaca teks halaman.</summary>
internal static class PrintTestKit
{
    /// <summary>Batas tunggu kondisi (bukan jeda tetap): test selesai begitu kondisi terpenuhi. Sengaja pendek: kelambatan harus tampak sebagai gagal.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    /// <summary>Halaman 360 x 420 dengan margin 24: dokumen pendek pun menjadi banyak halaman.</summary>
    public static readonly PageLayout Small = new(360, 420, 24);

    public const string Sample = "# Judul\n\nParagraf pertama dengan **tebal** dan `kode`.\n\n- satu\n- dua\n\n> kutipan\n\n| a | b |\n|---|---|\n| 1 | 2 |\n";

    public static PrintSnapshot Snapshot(string text, string title = "contoh.md", bool blockRemote = true, string? baseDirectory = null) =>
        new(text, baseDirectory, blockRemote, title);

    /// <summary>n paragraf (+ judul): kira-kira 5 paragraf per halaman <see cref="Small"/>, 1 paragraf = 2 baris pada A4.</summary>
    public static string Paragraphs(int count) =>
        "# Dokumen uji\n\n" + string.Concat(Enumerable.Range(1, count)
            .Select(i => $"Paragraf nomor {i}: " + string.Concat(Enumerable.Repeat("lorem ipsum dolor sit amet ", 5)) + "\n\n"));

    /// <summary>Paragraf panjang untuk jendela pratinjau (ukuran kertas tidak bisa diperkecil): beberapa halaman A4 dari sedikit teks.</summary>
    public static string Pages(int paragraphs) =>
        "# Dokumen uji\n\n" + string.Concat(Enumerable.Range(1, paragraphs)
            .Select(i => $"Paragraf nomor {i}: " + string.Concat(Enumerable.Repeat("lorem ipsum dolor sit amet ", 12)) + "\n\n"));

    public static PreviewBuild StartBuild(string text, PageLayout? layout = null, bool footer = true, string title = "contoh.md")
    {
        var build = new PreviewBuild(Snapshot(text, title), layout ?? Small, footer);
        build.Start();
        return build;
    }

    public static void WaitForEnd(PreviewBuild build) =>
        Assert.True(UiPump.Until(() => build.Stage is PreviewStage.Ready or PreviewStage.Failed, Patience),
            $"penyusunan tidak selesai (tahap {build.Stage})");

    /// <summary>Menunggu sampai paginasi selesai (jumlah halaman final diketahui) tanpa menunggu penulisan XPS.</summary>
    public static void WaitForPaginated(PreviewBuild build) =>
        Assert.True(UiPump.Until(() => build.Stage != PreviewStage.Paginating, Patience), "paginasi tidak selesai");

    /// <summary>Semua string Glyphs pada satu halaman XPS (isi + kaki halaman), urut dari atas.</summary>
    public static List<string> GlyphTexts(FixedDocumentSequence pages, int pageIndex)
    {
        var fixedDocument = pages.References[0].GetDocument(false);
        var root = fixedDocument.Pages[pageIndex].GetPageRoot(false);
        var texts = new List<string>();
        Collect(root, texts);
        return texts;

        static void Collect(DependencyObject node, List<string> into)
        {
            if (node is System.Windows.Documents.Glyphs glyphs) into.Add(glyphs.UnicodeString);
            foreach (var child in LogicalTreeHelper.GetChildren(node))
                if (child is DependencyObject next) Collect(next, into);
        }
    }

    /// <summary>Teks yang digambar kaki halaman (anak ke-2 wadah halaman); kosong bila halaman tanpa kaki.</summary>
    public static List<string> FooterTexts(DocumentPage page)
    {
        var texts = new List<string>();
        if (page.Visual is null || VisualTreeHelper.GetChildrenCount(page.Visual) != 2) return texts;
        if (VisualTreeHelper.GetChild(page.Visual, 1) is DrawingVisual footer) Collect(footer.Drawing, texts);
        return texts;

        static void Collect(Drawing drawing, List<string> into)
        {
            switch (drawing)
            {
                case DrawingGroup group:
                    foreach (var child in group.Children) Collect(child, into);
                    break;
                case GlyphRunDrawing run when run.GlyphRun.Characters is { } characters:
                    into.Add(new string(characters.ToArray()));
                    break;
            }
        }
    }

    /// <summary>Dokumen cetak yang sudah dipaginasi sinkron + paginator kaki halaman.</summary>
    public static DocumentPaginator Paginator(string text, string title = "contoh.md", bool footer = true, PageLayout? layout = null) =>
        PrintService.CreatePaginator(PrintService.CreateDocument(Snapshot(text, title), layout ?? Small), title, footer);
}
