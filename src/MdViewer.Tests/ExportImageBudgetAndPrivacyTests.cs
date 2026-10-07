using System.Text.RegularExpressions;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdViewer.Tests.Support;

namespace MdViewer.Tests;

/// <summary>Ekspor HTML: gambar lokal hanya di bawah folder dokumen, anggaran total penyematan, dan cache per path.</summary>
public class ExportImageBudgetAndPrivacyTests : IDisposable
{
    const string OutsideMarker = "[gambar di luar folder dokumen tidak disertakan";

    readonly TempDir dir = new();
    string DocsDir => Path.Combine(dir.Path, "docs");

    public void Dispose() => dir.Dispose();

    static string Render(string markdown, string? baseDir, long? budget = null)
    {
        var parsed = Markdig.Markdown.Parse(markdown, MarkdownSupport.ExportPipeline);
        if (budget is { } limit) MarkdownSupport.SanitizeForExport(parsed, baseDir, limit);
        else MarkdownSupport.SanitizeForExport(parsed, baseDir);
        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        MarkdownSupport.ExportPipeline.Setup(renderer);
        renderer.Render(parsed);
        return writer.ToString();
    }

    static string Slash(string path) => path.Replace(Path.DirectorySeparatorChar, '/');

    // ---- Privasi: hanya di bawah folder dokumen ----

    [Fact]
    public void RelativeImage_ResolvingOutsideTheDocumentFolder_IsReplacedByMarker_NotEmbedded()
    {
        dir.WriteBytes("rahasia.png", [1, 2, 3]);
        Directory.CreateDirectory(DocsDir);

        var html = Render("![foto](../rahasia.png)", DocsDir);

        Assert.Contains(OutsideMarker + ": foto]", html);
        Assert.DoesNotContain("data:", html);
        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public void AbsoluteImage_OutsideTheDocumentFolder_IsReplacedByMarker_WithoutLeakingThePath()
    {
        var secret = dir.WriteBytes("rahasia.png", [1, 2, 3]);
        Directory.CreateDirectory(DocsDir);

        var html = Render($"![foto]({Slash(secret)}) ![uri]({new Uri(secret).AbsoluteUri})", DocsDir);

        Assert.Equal(2, Regex.Matches(html, Regex.Escape(OutsideMarker)).Count);
        Assert.DoesNotContain("data:", html);
        Assert.DoesNotContain(Slash(dir.Path), html);
    }

    [Fact]
    public void SiblingFolderSharingTheNamePrefix_IsNotInsideTheDocumentFolder()
    {
        dir.WriteBytes(Path.Combine("docs2", "a.png"), [1, 2, 3]);
        Directory.CreateDirectory(DocsDir);

        var html = Render("![a](../docs2/a.png)", DocsDir);

        Assert.Contains(OutsideMarker, html);
        Assert.DoesNotContain("data:", html);
    }

    [Fact]
    public void Images_InsideTheDocumentFolder_AreStillEmbedded_IncludingDotDotThatStaysInside()
    {
        dir.WriteBytes(Path.Combine("docs", "a.png"), [1, 2, 3]);
        dir.WriteBytes(Path.Combine("docs", "sub", "b.png"), [1, 2, 3]);

        var html = Render("![a](a.png) ![b](sub/b.png) ![c](sub/../a.png) ![d](./sub/../sub/b.png)", DocsDir);

        Assert.Equal(4, Regex.Matches(html, "src=\"data:image/png;base64,AQID\"").Count);
        Assert.DoesNotContain(OutsideMarker, html);
    }

    [Fact]
    public void WithoutADocumentFolder_AbsoluteLocalImages_AreNotEmbedded_AndRelativeOnesStayRelative()
    {
        var abs = dir.WriteBytes("a.png", [1, 2, 3]);

        var html = Render($"![abs]({Slash(abs)}) ![rel](a.png)", baseDir: null);

        Assert.Contains(OutsideMarker + ": abs]", html);
        Assert.Contains("src=\"a.png\"", html);
        Assert.DoesNotContain("data:", html);
    }

    // ---- Anggaran total & cache ----

    [Fact]
    public void TotalBudget_OnceSpent_LeavesFurtherImagesRelative_ButStillEmbedsWhatFits()
    {
        dir.WriteBytes("a.png", [1, 2, 3]); // data URI: "data:image/png;base64,AQID" = 26 karakter

        var html = Render("![1](a.png) ![2](a.png) ![3](a.png) ![4](a.png)", dir.Path, budget: 26 * 2 + 5);

        Assert.Equal(2, Regex.Matches(html, "src=\"data:image/png;base64,AQID\"").Count);
        Assert.Equal(2, Regex.Matches(html, "src=\"a.png\"").Count);
    }

    [Fact]
    public void TotalBudget_SkipsAnImageTooBigForTheRemainder_ButLaterSmallOnesStillFit()
    {
        dir.WriteBytes("kecil.png", [1, 2, 3]);
        dir.WriteBytes("besar.png", new byte[300]);

        var html = Render("![a](besar.png) ![b](kecil.png)", dir.Path, budget: 100);

        Assert.Contains("src=\"besar.png\"", html);
        Assert.Contains("src=\"data:image/png;base64,AQID\"", html);
    }

    [Fact]
    public void SameImageReferencedManyTimes_IsEncodedOnce_AndSharesTheSameString()
    {
        dir.WriteBytes("a.png", new byte[50_000]);
        var parsed = Markdig.Markdown.Parse(string.Concat(Enumerable.Repeat("![x](a.png) ", 20)), MarkdownSupport.ExportPipeline);

        MarkdownSupport.SanitizeForExport(parsed, dir.Path);

        var urls = parsed.Descendants<LinkInline>().Where(l => l.IsImage).Select(l => l.Url!).ToList();
        Assert.Equal(20, urls.Count);
        Assert.All(urls, u => Assert.StartsWith("data:image/png;base64,", u));
        Assert.All(urls, u => Assert.Same(urls[0], u)); // satu instance string: tidak dikodekan ulang per referensi
    }

    [Fact]
    public void ManyReferencesToTheSameLargeImage_StayWithinTheTotalBudget()
    {
        // 1,9 MB per gambar (di bawah batas 2 MB per gambar) x 40 referensi = 76 MB tanpa anggaran.
        dir.WriteBytes("besar.png", new byte[1_900_000]);
        var markdown = string.Concat(Enumerable.Repeat("![x](besar.png)\n\n", 40));

        var html = HtmlExporter.ToHtmlDocument(markdown, "t", dir.Path);

        var embedded = Regex.Matches(html, "src=\"data:image/png;base64,").Count;
        Assert.InRange(embedded, 1, 12); // 30 MB / ~2,53 MB per kemunculan
        Assert.Equal(40 - embedded, Regex.Matches(html, "src=\"besar.png\"").Count); // sisanya tetap path relatif
        Assert.True(html.Length < MarkdownSupport.MaxTotalEmbeddedBytes + 100_000, $"HTML {html.Length} melewati anggaran");
    }
}
