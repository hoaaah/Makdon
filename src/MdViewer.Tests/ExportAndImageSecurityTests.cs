using System.Net;
using System.Text.RegularExpressions;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdViewer.Tests.Support;

namespace MdViewer.Tests;

/// <summary>Vektor XSS dan kebocoran path pada ekspor HTML; melengkapi <see cref="ExportSanitizationTests"/>.</summary>
public class ExportXssVectorTests : IDisposable
{
    readonly TempDir dir = new();

    public void Dispose() => dir.Dispose();

    static string Body(string markdown, string? baseDir = null)
    {
        var html = HtmlExporter.ToHtmlDocument(markdown, "t", baseDir);
        return html[html.IndexOf("<body>", StringComparison.Ordinal)..];
    }

    // Seperti HtmlExporter, tetapi Url gambar/tautan pertama diganti langsung (menghindari aturan escape backslash markdown).
    static string BodyWithUrl(string markdown, string url, string? baseDir = null)
    {
        var parsed = Markdig.Markdown.Parse(markdown, MarkdownSupport.ExportPipeline);
        parsed.Descendants<LinkInline>().First().Url = url;
        MarkdownSupport.SanitizeForExport(parsed, baseDir);
        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        MarkdownSupport.ExportPipeline.Setup(renderer);
        renderer.Render(parsed);
        return writer.ToString();
    }

    static IEnumerable<string> Attributes(string html, string name) =>
        Regex.Matches(html, name + "=\"([^\"]*)\"").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value));

    // Sifat umum: setiap href/src di hasil ekspor harus lolos allowlist.
    static void AssertAllUrlsAreSafe(string html)
    {
        foreach (var href in Attributes(html, "href"))
            Assert.True(MarkdownSupport.IsSafeLinkUrl(href), $"href tidak aman: {href}");
        foreach (var src in Attributes(html, "src"))
            Assert.Contains(MarkdownSupport.ClassifyUrl(src), new[] { UrlKind.Relative, UrlKind.Http, UrlKind.DataImage });
    }

    // ---- Tautan ----

    [Theory]
    [InlineData("[x](<javascript:alert(1)>)")]
    [InlineData("[x](<JAVASCRIPT:alert(1)>)")]
    [InlineData("[x](<jAvAsCrIpT:alert(1)>)")]
    [InlineData("[x](<  javascript:alert(1)>)")]
    [InlineData("[x](&#106;avascript:alert(1))")]
    [InlineData("[x](&#x6A;avascript:alert(1))")]
    [InlineData("[x](java&#10;script:alert(1))")]
    [InlineData("[x](java&#13;&#10;script:alert(1))")]
    [InlineData("[x](java&Tab;script:alert(1))")]
    [InlineData("[x](&#1;javascript:alert(1))")]
    [InlineData("[x](<vbscript:msgbox(1)>)")]
    [InlineData("[x](<VBScript:msgbox(1)>)")]
    [InlineData("[x](<data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==>)")]
    [InlineData("[x](<DATA:text/html,x>)")]
    [InlineData("[x](<livescript:x>)")]
    [InlineData("[x](<file:///C:/Windows/win.ini>)")]
    [InlineData("[x](<ftp://host/x>)")]
    [InlineData("[x][r]\n\n[r]: javascript:alert(1)")]
    [InlineData("[![gambar](a.png)](<javascript:alert(1)>)")]
    [InlineData("[x](/\\evil.example/x)")]
    public void LinkVectors_NeverProduceAnUnsafeHref(string markdown)
    {
        var body = Body(markdown);

        AssertAllUrlsAreSafe(body);
        Assert.DoesNotMatch(new Regex(@"(javascript|vbscript):", RegexOptions.IgnoreCase), string.Join(" ", Attributes(body, "href")));
        Assert.Contains("href=\"#\"", body);
    }

    [Fact]
    public void LinkWithAngleBracketsAndTitle_TitleQuotesCannotBreakOutOfTheAttribute()
    {
        var body = Body("[x](https://a.b \"t\\\" onclick=\\\"alert(1)\")");

        Assert.DoesNotContain("\" onclick=", body);
        Assert.DoesNotContain("onclick=\"", body);
    }

    [Fact]
    public void ImageAlt_QuotesCannotBreakOutOfTheAttribute()
    {
        var body = Body("![a\" onerror=\"alert(1)](https://a.b/c.png)");

        Assert.DoesNotContain("\" onerror=", body);
        Assert.Contains("&quot;", body);
    }

    [Fact]
    public void LinkTextOfAnUnsafeLink_IsKeptAsInertText()
    {
        var body = Body("[klik <b>di sini</b>](<javascript:alert(1)>)");

        Assert.Contains("<a href=\"#\">klik &lt;b&gt;di sini&lt;/b&gt;</a>", body);
    }

    // ---- Autolink ----

    [Theory]
    [InlineData("<JAVASCRIPT:alert(1)>")]
    [InlineData("<vbscript:msgbox(1)>")]
    [InlineData("<data:text/html,x>")]
    [InlineData("<file:///C:/Windows/win.ini>")]
    [InlineData("<ftp://host/x>")]
    public void Autolinks_WithUnsafeSchemes_AreNotLinked(string markdown)
    {
        var body = Body(markdown);

        Assert.DoesNotContain("<a ", body);
        AssertAllUrlsAreSafe(body);
    }

    [Fact]
    public void Autolink_Email_AndBareUrls_AreStillLinked_ButBareScriptSchemeIsPlainText()
    {
        Assert.Contains("<a href=\"mailto:a@b.co\">", Body("<a@b.co>"));
        Assert.Contains("<a href=\"https://example.com/x\">", Body("lihat https://example.com/x ya"));
        Assert.DoesNotContain("<a ", Body("javascript:alert(1)"));
    }

    // ---- HTML mentah ----

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<SCRIPT SRC=//evil.example/x.js></SCRIPT>")]
    [InlineData("<svg onload=alert(1)>")]
    [InlineData("<style>body{display:none}</style>")]
    [InlineData("<!-- komentar --><b>x</b>")]
    [InlineData("<![CDATA[x]]>")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<object data=\"x\"></object>")]
    [InlineData("<form action=\"//evil.example\"><input></form>")]
    [InlineData("<base href=\"//evil.example/\">")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0;url=//evil.example\">")]
    [InlineData("<body onload=alert(1)>")]
    [InlineData("kata <img src=x onerror=alert(1)> kata")]
    [InlineData("# <img src=x onerror=alert(1)>")]
    [InlineData("| a |\n|---|\n| <script>x</script> |")]
    [InlineData("> <iframe src=//evil.example></iframe>")]
    [InlineData("- <video src=x onerror=alert(1)>")]
    public void RawHtmlInAnyPosition_IsEscaped_NeverEmittedAsTags(string markdown)
    {
        var body = Body(markdown);

        var afterBodyTag = body["<body>".Length..];
        Assert.DoesNotMatch(new Regex(@"<(script|svg|style|object|form|input|base|meta|body|iframe|video|img|b|a)[\s>/]", RegexOptions.IgnoreCase),
            afterBodyTag.Replace("</body>", ""));
        Assert.Contains("&lt;", body);
    }

    [Fact]
    public void ExportPipeline_ParsesInlineHtmlAsText_NotAsHtmlInline()
    {
        var parsed = Markdig.Markdown.Parse("a <b>x</b> c", MarkdownSupport.ExportPipeline);

        Assert.DoesNotContain(parsed.Descendants(), o => o is HtmlInline);
    }

    [Theory]
    [InlineData("![v](https://www.youtube.com/watch?v=abc123)")]
    [InlineData("![v](https://vimeo.com/123456)")]
    [InlineData("![v](https://example.com/film.mp4)")]
    [InlineData("![v](https://example.com/suara.mp3)")]
    [InlineData("![v](https://youtu.be/abc123)")]
    public void MediaLinks_NeverBecomeIframeVideoOrAudio(string markdown)
    {
        var body = Body(markdown);

        Assert.DoesNotMatch(new Regex("<(iframe|video|audio|source)", RegexOptions.IgnoreCase), body);
        Assert.Contains("<img", body);
    }

    [Fact]
    public void GenericAttributes_AreNotInjected_OnImagesCodeAndParagraphs()
    {
        var body = Body("![a](https://a.b/c.png){onerror=alert(1)}\n\n`kode`{onclick=x}\n\npara {onmouseover=x}\n");

        Assert.DoesNotContain("onerror=\"", body);
        Assert.DoesNotContain("onclick=\"", body);
        Assert.DoesNotContain("onmouseover=\"", body);
    }

    // ---- Gambar ----

    [Fact]
    public void UnsafeImage_AltIsEscapedInReplacementText_AndFormattingIsFlattened()
    {
        var script = Body("![<script>alert(1)</script>](<javascript:x>)");
        var emphasis = Body("![a *b* c](<file://host/share/x.png>)");
        var empty = Body("![](<javascript:x>)");

        Assert.DoesNotContain("<script", script);
        Assert.Contains("[gambar diblokir: &lt;script&gt;alert(1)&lt;/script&gt;]", script);
        Assert.Contains("[gambar diblokir: a b c]", emphasis);
        Assert.Contains("[gambar diblokir]", empty);
        Assert.DoesNotContain("<img", script + emphasis + empty);
    }

    [Theory]
    [InlineData(@"\\host\share\a.png")]
    [InlineData("//host/share/a.png")]
    [InlineData("file://host/share/a.png")]
    [InlineData("FILE://host/share/a.png")]
    [InlineData(@"\/host/share/a.png")]
    public void UncAndSchemeRelativeImageUrls_AreReplacedByText_NeverEmitted(string url)
    {
        var body = BodyWithUrl("![x](a.png)", url, dir.Path);

        Assert.DoesNotContain("<img", body);
        Assert.Contains("[gambar diblokir: x]", body);
    }

    [Theory]
    [InlineData(@"\\host\share\a.png")]
    [InlineData("//host/share/a.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData(@"file:///C:/x")]
    public void UnsafeLinkUrls_SetDirectly_BecomeInertAnchor(string url)
    {
        var body = BodyWithUrl("[x](a.md)", url, dir.Path);

        Assert.Contains("<a href=\"#\">x</a>", body);
    }

    [Fact]
    public void LocalImage_AtExactlyTwoMegabytes_IsEmbedded_OneByteMoreIsNot()
    {
        dir.WriteBytes("pas.png", new byte[(int)MarkdownSupport.MaxEmbeddedImageBytes]);
        dir.WriteBytes("lebih.png", new byte[(int)MarkdownSupport.MaxEmbeddedImageBytes + 1]);

        var body = Body("![a](pas.png) ![b](lebih.png)", dir.Path);

        Assert.Contains("src=\"data:image/png;base64,", body);
        Assert.Contains("src=\"lebih.png\"", body);
        Assert.Single(Regex.Matches(body, "data:image/png;base64,"));
    }

    [Fact]
    public void AbsoluteLocalImage_OverTwoMegabytes_OrSvg_OrMissing_IsReplacedByText_WithoutLeakingThePath()
    {
        var big = dir.WriteBytes("besar.png", new byte[(int)MarkdownSupport.MaxEmbeddedImageBytes + 1]);
        var svg = dir.WriteBytes("vektor.svg", [1]);
        var missing = dir.File("tidak-ada.png");
        var md = $"![besar]({big.Replace('\\', '/')}) ![svg]({svg.Replace('\\', '/')}) ![hilang]({missing.Replace('\\', '/')})";

        var body = Body(md, dir.Path);

        Assert.Contains("[gambar lokal tidak disertakan: besar]", body);
        Assert.Contains("[gambar lokal tidak disertakan: svg]", body);
        Assert.Contains("[gambar lokal tidak disertakan: hilang]", body);
        Assert.DoesNotContain("<img", body);
        Assert.DoesNotContain(dir.Path.Replace('\\', '/'), body);
        Assert.DoesNotContain(dir.Path, body);
    }

    [Fact]
    public void AbsoluteLocalImage_WithWindowsBackslashPath_IsEmbedded()
    {
        var png = dir.WriteBytes("p.png", [1, 2, 3]);

        var body = BodyWithUrl("![x](a.png)", png, dir.Path);

        Assert.Contains("src=\"data:image/png;base64,AQID\"", body);
    }

    [Fact]
    public void FileUriImage_WithoutHost_IsEmbedded_WithHost_IsBlocked()
    {
        var png = dir.WriteBytes("dengan spasi.png", [1, 2, 3]);

        var local = Body($"![x]({new Uri(png).AbsoluteUri})", dir.Path);
        var remote = Body("![x](file://host/share/a.png)", dir.Path);

        Assert.Contains("src=\"data:image/png;base64,AQID\"", local);
        Assert.Contains("[gambar diblokir: x]", remote);
    }

    [Fact]
    public void RelativeImage_QueryFragmentAndPercentEncoding_FindTheRealFile()
    {
        dir.WriteBytes(Path.Combine("sub dir", "p q.png"), [1, 2, 3]);
        dir.WriteBytes("a.png", [1, 2, 3]);

        var body = Body("![a](a.png?v=2) ![b](a.png#bagian) ![c](<sub%20dir/p%20q.png>)", dir.Path);

        Assert.Equal(3, Regex.Matches(body, "src=\"data:image/png;base64,AQID\"").Count);
    }

    [Fact]
    public void RelativeImage_PointingAtNonImageExtension_OrDirectory_IsLeftRelative()
    {
        dir.WriteBytes("catatan.txt", [1]);
        Directory.CreateDirectory(dir.File("folder.png"));

        var body = Body("![a](catatan.txt) ![b](folder.png)", dir.Path);

        Assert.Contains("src=\"catatan.txt\"", body);
        Assert.Contains("src=\"folder.png\"", body);
        Assert.DoesNotContain("data:", body);
    }

    [Fact]
    public void ExportOfMixedHostileDocument_PassesTheGlobalUrlAllowlist()
    {
        var md = """
            # Judul <script>x</script>

            [a](javascript:alert(1)) [b](https://ok.example) ![c](data:text/html;base64,AA) ![d](https://ok.example/x.png)
            <img src=x onerror=alert(1)>

            <javascript:alert(1)> <https://ok.example>

            [ref]: <vbscript:x>
            """;

        AssertAllUrlsAreSafe(Body(md, dir.Path));
    }
}

/// <summary>Pratinjau/cetak: pemblokiran gambar UNC/remote di <see cref="MarkdownSupport.ResolveImageUrls"/> dan <see cref="MarkdownSupport.IsAllowedLocalPath"/>.</summary>
public class ResolveImageUrlsSecurityTests : IDisposable
{
    readonly TempDir dir = new();

    public void Dispose() => dir.Dispose();

    static MarkdownDocument ResolveUrl(string url, string? baseDir, bool blockRemote)
    {
        var doc = Markdig.Markdown.Parse("![x](a.png)", MarkdownSupport.Pipeline);
        doc.Descendants<LinkInline>().Single().Url = url;
        MarkdownSupport.ResolveImageUrls(doc, baseDir, blockRemote);
        return doc;
    }

    static string Text(MarkdownDocument doc) =>
        string.Concat(doc.Descendants<LiteralInline>().Select(l => l.Content.ToString()));

    static bool HasImage(MarkdownDocument doc) => doc.Descendants<LinkInline>().Any(l => l.IsImage);

    [Theory]
    [InlineData(@" \\host\share\a.png")]
    [InlineData("\t//host/share/a.png")]
    [InlineData(@"/\host\share\a.png")]
    [InlineData(@"\/host/share/a.png")]
    [InlineData("FILE://host/share/a.png")]
    [InlineData("File://HOST/share/a.png")]
    [InlineData(@"file:\\host\share\a.png")]
    [InlineData("file://localhost/c$/a.png")]
    [InlineData("file:////host/share/a.png")]
    [InlineData("\\\\host\\share\\a.png\n")]
    public void UncVariants_AreBlocked_RegardlessOfBlockRemote(string url)
    {
        foreach (var blockRemote in new[] { true, false })
        {
            var doc = ResolveUrl(url, dir.Path, blockRemote);

            Assert.False(HasImage(doc), $"gambar masih ada untuk {url} (blockRemote={blockRemote})");
            Assert.Equal(MarkdownSupport.BlockedRemoteImageText, Text(doc));
        }
    }

    [Fact]
    public void FileUrlWithoutHost_ToExistingFile_BecomesFileUri_AndMissingFileIsLeftAsIs()
    {
        var png = dir.WriteBytes("ada.png", [1]);
        var url = new Uri(png).AbsoluteUri;
        var missingUrl = new Uri(dir.File("tidak-ada.png")).AbsoluteUri;

        var existing = ResolveUrl(url, dir.Path, blockRemote: true);
        var missing = ResolveUrl(missingUrl, dir.Path, blockRemote: true);

        Assert.Equal(url, existing.Descendants<LinkInline>().Single().Url);
        Assert.Equal(missingUrl, missing.Descendants<LinkInline>().Single().Url);
    }

    [Theory]
    [InlineData("HTTPS://EXAMPLE.COM/a.png")]
    [InlineData("http://example.com:8080/a.png?x=1")]
    [InlineData("https://user:pw@example.com/a.png")]
    public void HttpVariants_BlockedOnlyWhenRequested(string url)
    {
        var blocked = ResolveUrl(url, dir.Path, blockRemote: true);
        var allowed = ResolveUrl(url, dir.Path, blockRemote: false);

        Assert.False(HasImage(blocked));
        Assert.Equal(MarkdownSupport.BlockedRemoteImageText, Text(blocked));
        Assert.True(HasImage(allowed));
        Assert.Equal(url, allowed.Descendants<LinkInline>().Single().Url);
    }

    [Fact]
    public void ProtocolRelativeUrl_IsBlockedEvenWhenRemoteImagesAreAllowed()
    {
        var doc = ResolveUrl("//example.com/a.png", dir.Path, blockRemote: false);

        Assert.False(HasImage(doc));
    }

    [Fact]
    public void BlockRemote_ReplacesOnlyTheRemoteImages_AndKeepsSurroundingLinksAndLocalImages()
    {
        var png = dir.WriteBytes("lokal.png", [1]);
        var doc = Markdig.Markdown.Parse(
            "[![r](https://example.com/a.png)](https://example.com) ![l](lokal.png) ![d](data:image/png;base64,AAAA) teks", MarkdownSupport.Pipeline);

        MarkdownSupport.ResolveImageUrls(doc, dir.Path, blockRemote: true);

        var links = doc.Descendants<LinkInline>().ToList();
        Assert.Contains(links, l => !l.IsImage && l.Url == "https://example.com"); // tautan pembungkus tetap
        Assert.Equal(new[] { new Uri(png).AbsoluteUri }, links.Where(l => l.IsImage).Select(l => l.Url).ToArray());
        Assert.Contains(MarkdownSupport.BlockedRemoteImageText, Text(doc));
        Assert.Contains(MarkdownSupport.UnsupportedPreviewImageText, Text(doc)); // WPF tak bisa memuat data: lewat URI
    }

    [Fact]
    public void NonHttpNetworkSchemes_AreBlocked_WhenRemoteImagesAreBlocked()
    {
        var doc = ResolveUrl("ftp://example.com/a.png", dir.Path, blockRemote: true);

        Assert.False(HasImage(doc));
    }

    // Pratinjau tidak pernah memicu koneksi selain http(s): skema lain diblokir walau gambar remote diizinkan.
    [Theory]
    [InlineData("ftp://example.com/a.png")]
    [InlineData("FTP://example.com/a.png")]
    [InlineData("f\tp://example.com/a.png")]
    [InlineData("gopher://example.com/a.png")]
    [InlineData("ws://example.com/a.png")]
    [InlineData("pack://application:,,,/a.png")]
    [InlineData("mailto:a@b.c")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,AAAA")]
    public void NonHttpSchemes_AreBlockedInPreview_EvenWhenRemoteImagesAreAllowed(string url)
    {
        var doc = ResolveUrl(url, dir.Path, blockRemote: false);

        Assert.False(HasImage(doc), $"gambar masih ada untuk {url}");
        Assert.Equal(MarkdownSupport.BlockedRemoteImageText, Text(doc));
    }

    // WPF (BitmapImage(Uri)) tidak mengenal skema data: dan menggagalkan seluruh pratinjau; ekspor HTML punya allowlist sendiri.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DataImages_GetPreviewMarker_RegardlessOfBlockRemote(bool blockRemote)
    {
        var doc = ResolveUrl("data:image/png;base64,AAAA", dir.Path, blockRemote);

        Assert.False(HasImage(doc));
        Assert.Equal(MarkdownSupport.UnsupportedPreviewImageText, Text(doc));
    }

    [Fact]
    public void ExportAllowlist_StillKeepsValidDataImages_AndPreviewBlockingDoesNotLeakIntoIt()
    {
        var html = HtmlExporter.ToHtmlDocument("![d](data:image/png;base64,AAAA) ![f](ftp://example.com/a.png)", null, dir.Path);

        Assert.Contains("src=\"data:image/png;base64,AAAA\"", html);
        Assert.DoesNotContain("ftp://", html);
    }

    // ---- IsAllowedLocalPath ----

    [Theory]
    [InlineData(@"C:\a\b.png", null, true)]
    [InlineData(@"D:\x.png", @"C:\docs", true)]
    [InlineData(@"\\host\share\a\b.png", @"\\host\share", true)]
    [InlineData(@"\\host\share\a\b.png", @"\\HOST\SHARE\sub\docs", true)]
    [InlineData(@"\\host\share\a\b.png", @"\\host\share2\docs", false)]
    [InlineData(@"\\host2\share\a\b.png", @"\\host\share\docs", false)]
    [InlineData(@"\\host\share\a\b.png", @"C:\docs", false)]
    [InlineData(@"\\host\share\a\b.png", null, false)]
    [InlineData(@"\\?\C:\a.png", @"C:\docs", false)]
    [InlineData(@"\\.\pipe\x", @"C:\docs", false)]
    [InlineData("", @"C:\docs", false)]
    [InlineData("a.png", @"C:\docs", false)]
    public void IsAllowedLocalPath_Matrix(string fullPath, string? baseDir, bool expected)
    {
        Assert.Equal(expected, MarkdownSupport.IsAllowedLocalPath(fullPath, baseDir));
    }
}
