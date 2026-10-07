using System.Text;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Renderers.Html;
using MdViewer.Tests.Support;

namespace MdViewer.Tests;

public class MarkdownFilesTests
{
    [Theory]
    [InlineData("a.md", true)]
    [InlineData("A.MD", true)]
    [InlineData("notes.Markdown", true)]
    [InlineData("a.mdown", true)]
    [InlineData("a.mkd", true)]
    [InlineData("readme.txt", true)]
    [InlineData(@"C:\dok\sub.folder\a.md", true)]
    [InlineData("a.md.bak", false)]
    [InlineData("a.docx", false)]
    [InlineData("a.mdx", false)]
    [InlineData("README", false)]
    [InlineData("md", false)]
    [InlineData("", false)]
    [InlineData(@"C:\dir.md\file", false)]
    [InlineData("a.md ", false)]
    public void IsMarkdown_ByExtension(string path, bool expected)
    {
        Assert.Equal(expected, MarkdownFiles.IsMarkdown(path));
    }
}

public class MarkdownSlugTests
{
    static List<string?> Ids(string markdown) =>
        Markdig.Markdown.Parse(markdown, MarkdownSupport.Pipeline)
            .Descendants<HeadingBlock>()
            .Select(h => h.TryGetAttributes()?.Id)
            .ToList();

    [Theory]
    [InlineData("# Hello World", "hello-world")]
    [InlineData("## Hello, World!", "hello-world")]
    [InlineData("### UPPER lower", "upper-lower")]
    [InlineData("# snake_case name", "snake_case-name")]
    [InlineData("# kebab-case", "kebab-case")]
    [InlineData("# Use `code` here", "use-code-here")]
    [InlineData("# Link [teks](http://x.y) akhir", "link-teks-akhir")]
    [InlineData("# Pengantar: Ürün Ünik", "pengantar-ürün-ünik")]
    [InlineData("# 2024 Rencana", "2024-rencana")]
    public void HeadingId_FollowsGitHubStyleSlug(string markdown, string expectedId)
    {
        Assert.Equal(expectedId, Ids(markdown).Single());
    }

    [Fact]
    public void HeadingId_DuplicateHeadings_GetNumericSuffixes()
    {
        var ids = Ids("# Foo\n\n## Foo\n\n### Foo");

        Assert.Equal(new[] { "foo", "foo-1", "foo-2" }, ids);
    }

    [Fact]
    public void HeadingId_SetextHeadings_AlsoGetIds()
    {
        Assert.Equal(new[] { "judul-satu", "judul-dua" }, Ids("Judul Satu\n===\n\nJudul Dua\n---\n"));
    }

    [Fact]
    public void HeadingId_EmptyHeading_DoesNotThrow()
    {
        var ids = Ids("#\n\n# Ok");

        Assert.Equal("ok", ids.Last());
    }
}

public class ResolveImageUrlsTests : IDisposable
{
    readonly TempDir dir = new();

    public void Dispose() => dir.Dispose();

    LinkInline Resolve(string markdown, string? baseDir)
    {
        var doc = Markdig.Markdown.Parse(markdown, MarkdownSupport.Pipeline);
        MarkdownSupport.ResolveImageUrls(doc, baseDir);
        return doc.Descendants<LinkInline>().Single();
    }

    [Fact]
    public void RelativeExistingImage_BecomesFileUri()
    {
        var img = dir.WriteBytes(Path.Combine("img", "a.png"), [1]);

        var link = Resolve("![x](img/a.png)", dir.Path);

        Assert.Equal(new Uri(img).AbsoluteUri, link.Url);
        Assert.StartsWith("file:///", link.Url);
    }

    [Fact]
    public void DotSlashAndParentSegments_AreNormalized()
    {
        dir.WriteBytes("a.png", [1]);
        Directory.CreateDirectory(dir.File("sub"));

        var link = Resolve("![x](./sub/../a.png)", dir.Path);

        Assert.Equal(new Uri(dir.File("a.png")).AbsoluteUri, link.Url);
        Assert.DoesNotContain("..", link.Url);
    }

    [Fact]
    public void PercentEncodedSpaces_AreDecodedForLookup_AndReEncodedInUri()
    {
        var img = dir.WriteBytes("my pic.png", [1]);

        var link = Resolve("![x](my%20pic.png)", dir.Path);

        Assert.Equal(new Uri(img).AbsoluteUri, link.Url);
        Assert.Contains("my%20pic.png", link.Url);
    }

    [Fact]
    public void MissingImage_IsLeftUntouched()
    {
        var link = Resolve("![x](tidak-ada.png)", dir.Path);

        Assert.Equal("tidak-ada.png", link.Url);
    }

    [Theory]
    [InlineData("https://example.com/a.png")]
    [InlineData("http://example.com/a.png")]
    public void HttpImages_AreLeftUntouched_WhenRemoteImagesAreNotBlocked(string url)
    {
        dir.WriteBytes("a.png", [1]);

        Assert.Equal(url, Resolve($"![x]({url})", dir.Path).Url);
    }

    [Fact]
    public void NullBaseDirectory_LeavesEverythingAlone()
    {
        Assert.Equal("img/a.png", Resolve("![x](img/a.png)", null).Url);
    }

    [Fact]
    public void NonImageLinks_AreNeverRewritten()
    {
        dir.WriteBytes("b.md", [1]);

        Assert.Equal("b.md", Resolve("[x](b.md)", dir.Path).Url);
    }

    [Fact]
    public void AbsoluteLocalPath_ToExistingFile_BecomesFileUri()
    {
        var img = dir.WriteBytes("abs.png", [1]);
        var forwardSlashes = img.Replace('\\', '/');

        var link = Resolve($"![x]({forwardSlashes})", Path.GetTempPath());

        Assert.Equal(new Uri(img).AbsoluteUri, link.Url);
    }

    [Fact]
    public void ExistingFileUri_DoesNotThrow_AndStaysUsable()
    {
        var img = dir.WriteBytes("f.png", [1]);
        var uri = new Uri(img).AbsoluteUri;

        var link = Resolve($"![x]({uri})", dir.Path);

        Assert.Equal(uri, link.Url);
    }

    [Fact]
    public void InvalidPathCharacters_DoNotThrow_AndAreLeftUntouched()
    {
        var link = Resolve("![x](a|b\"c.png)", dir.Path);

        Assert.Contains("a", link.Url);
    }

    [Fact]
    public void EmptyImageUrl_IsSkipped()
    {
        var doc = Markdig.Markdown.Parse("![x]()", MarkdownSupport.Pipeline);

        var ex = Record.Exception(() => MarkdownSupport.ResolveImageUrls(doc, dir.Path));

        Assert.Null(ex);
    }

    [Fact]
    public void ImageInsideLinkAndMultipleImages_AreAllResolved()
    {
        var a = dir.WriteBytes("a.png", [1]);
        var b = dir.WriteBytes("b.png", [1]);
        var doc = Markdig.Markdown.Parse("[![a](a.png)](http://x)\n\n![b](b.png)", MarkdownSupport.Pipeline);

        MarkdownSupport.ResolveImageUrls(doc, dir.Path);

        var urls = doc.Descendants<LinkInline>().Where(l => l.IsImage).Select(l => l.Url).ToList();
        Assert.Equal(new[] { new Uri(a).AbsoluteUri, new Uri(b).AbsoluteUri }, urls);
    }
}

public class HtmlExporterTests : IDisposable
{
    readonly TempDir dir = new();

    public void Dispose() => dir.Dispose();

    [Fact]
    public void Headings_GetGitHubStyleIds()
    {
        var html = HtmlExporter.ToHtmlDocument("# Hello World\n\n## Hello World\n");

        Assert.Contains("<h1 id=\"hello-world\">Hello World</h1>", html);
        Assert.Contains("<h2 id=\"hello-world-1\">Hello World</h2>", html);
    }

    [Fact]
    public void Document_IsSelfContainedHtmlWithStyleAndUtf8Meta()
    {
        var html = HtmlExporter.ToHtmlDocument("teks");

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<meta charset=\"utf-8\">", html);
        Assert.Contains("<style>", html);
        Assert.Contains("<p>teks</p>", html);
        Assert.EndsWith("</html>", html);
        Assert.DoesNotContain("{{BODY}}", html);
        Assert.DoesNotContain("{{TITLE}}", html);
    }

    [Fact]
    public void Title_IsHtmlEscaped()
    {
        var html = HtmlExporter.ToHtmlDocument("x", "<script>alert(1)</script> & \"q\"");

        Assert.Contains("<title>&lt;script&gt;alert(1)&lt;/script&gt; &amp; &quot;q&quot;</title>", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Title_BlankFallsBackToDokumen(string? title)
    {
        Assert.Contains("<title>Dokumen</title>", HtmlExporter.ToHtmlDocument("x", title));
    }

    [Fact]
    public void Title_PlainTextKeptAsIs()
    {
        Assert.Contains("<title>Catatan Rapat</title>", HtmlExporter.ToHtmlDocument("x", "Catatan Rapat"));
    }

    [Fact]
    public void Body_ContainingPlaceholderTokens_IsNotReinterpreted()
    {
        var html = HtmlExporter.ToHtmlDocument("tulis {{TITLE}} dan {{BODY}} apa adanya", "Judul");

        Assert.Contains("<p>tulis {{TITLE}} dan {{BODY}} apa adanya</p>", html);
        Assert.Contains("<title>Judul</title>", html);
    }

    [Fact]
    public void Title_ContainingBodyPlaceholder_IsKeptLiteral()
    {
        var html = HtmlExporter.ToHtmlDocument("# Isi", "{{BODY}}");

        Assert.Contains("<title>{{BODY}}</title>", html);
    }

    [Fact]
    public void RelativeImage_IsEmbeddedAsDataUri_AndNeverLeaksLocalPath()
    {
        dir.WriteBytes(Path.Combine("img", "a b.png"), [1]);

        var html = HtmlExporter.ToHtmlDocument("![alt](img/a%20b.png)", "t", dir.Path);

        Assert.Contains("src=\"data:image/png;base64,AQ==\"", html);
        Assert.DoesNotContain("file:///", html);
        Assert.DoesNotContain(dir.Path, html);
    }

    [Fact]
    public void RelativeImage_StaysRelative_WithoutBaseDirectory()
    {
        dir.WriteBytes("a.png", [1]);

        var html = HtmlExporter.ToHtmlDocument("![alt](a.png)");

        Assert.Contains("src=\"a.png\"", html);
    }

    [Fact]
    public void RelativeImage_StaysRelative_WhenFileMissing()
    {
        var html = HtmlExporter.ToHtmlDocument("![alt](hilang.png)", null, dir.Path);

        Assert.Contains("src=\"hilang.png\"", html);
    }

    [Fact]
    public void RemoteImage_IsNotRewritten()
    {
        var html = HtmlExporter.ToHtmlDocument("![alt](https://example.com/a.png)", null, dir.Path);

        Assert.Contains("src=\"https://example.com/a.png\"", html);
    }

    [Fact]
    public void EmptyMarkdown_ProducesValidEmptyBody()
    {
        var html = HtmlExporter.ToHtmlDocument("");

        Assert.Contains("<body>", html);
        Assert.Contains("</body>", html);
    }

    [Fact]
    public void Tables_AndCodeBlocks_AreRenderedByTheSharedPipeline()
    {
        var html = HtmlExporter.ToHtmlDocument("| a | b |\n|---|---|\n| 1 | 2 |\n\n```cs\nvar x = 1 < 2;\n```\n");

        Assert.Contains("<table>", html);
        Assert.Contains("<th>a</th>", html);
        Assert.Contains("var x = 1 &lt; 2;", html);
    }

    [Fact]
    public void ExportToFile_WritesUtf8WithoutBom_AndMatchesToHtmlDocument()
    {
        var path = dir.File("out.html");

        HtmlExporter.ExportToFile("# Halo ünï 日本", path, "Judul");

        var bytes = File.ReadAllBytes(path);
        Assert.NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal(HtmlExporter.ToHtmlDocument("# Halo ünï 日本", "Judul"), new UTF8Encoding(false, true).GetString(bytes));
        Assert.Equal(new[] { "out.html" }, dir.Entries());
    }

    [Fact]
    public void ExportToFile_OverwritesExistingFile()
    {
        var path = dir.WriteText("out.html", "lama");

        HtmlExporter.ExportToFile("baru", path);

        Assert.Contains("<p>baru</p>", File.ReadAllText(path));
    }

    [Fact]
    public void ExportToFile_MissingDirectory_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            HtmlExporter.ExportToFile("x", dir.File(Path.Combine("tidak-ada", "out.html"))));
    }

    [Fact]
    public void ExportToFile_LockedTarget_ThrowsAndKeepsOldFile()
    {
        var path = dir.WriteText("out.html", "lama");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var ex = Record.Exception(() => HtmlExporter.ExportToFile("baru", path));
            Assert.True(ex is IOException or UnauthorizedAccessException);
        }

        Assert.Equal("lama", File.ReadAllText(path));
        Assert.Equal(new[] { "out.html" }, dir.Entries());
    }
}
