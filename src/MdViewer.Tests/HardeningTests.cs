using System.Text;
using ICSharpCode.AvalonEdit.Document;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdViewer.Tests.Support;

namespace MdViewer.Tests;

/// <summary>Ekspor HTML: HTML mentah di-escape dan URL disaring allowlist (tanpa kebocoran path lokal).</summary>
public class ExportSanitizationTests : IDisposable
{
    readonly TempDir dir = new();

    public void Dispose() => dir.Dispose();

    static string Body(string markdown, string? baseDir = null)
    {
        var html = HtmlExporter.ToHtmlDocument(markdown, "t", baseDir);
        return html[html.IndexOf("<body>", StringComparison.Ordinal)..];
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<div onclick=\"alert(1)\">x</div>")]
    [InlineData("teks <b onmouseover=alert(1)>x</b>")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe>")]
    public void RawHtml_IsEscaped(string markdown)
    {
        var body = Body(markdown);

        Assert.DoesNotContain("<script", body);
        Assert.DoesNotContain("<img", body);
        Assert.DoesNotContain("<div", body);
        Assert.DoesNotContain("<b ", body);
        Assert.DoesNotContain("<iframe", body);
        Assert.Contains("&lt;", body);
    }

    [Fact]
    public void GenericAttributes_AreNotInjectedIntoElements()
    {
        var body = Body("# Judul {onclick=alert(1)}\n\n[x](https://a.b){onmouseover=alert(1)}\n");

        Assert.DoesNotContain("onclick=\"", body);
        Assert.DoesNotContain("onmouseover=\"", body);
    }

    [Fact]
    public void MediaLinks_AreNotTurnedIntoIframes()
    {
        Assert.DoesNotContain("<iframe", Body("![v](https://www.youtube.com/watch?v=abc123)"));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("java&#9;script:alert(1)")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("file://host/share/x")]
    [InlineData("//evil.example/x")]
    public void UnsafeLinkUrls_BecomeInertAnchor(string url)
    {
        Assert.Contains("<a href=\"#\">klik</a>", Body($"[klik]({url})"));
    }

    [Theory]
    [InlineData("https://example.com/a?b=1&c=2", "https://example.com/a?b=1&amp;c=2")]
    [InlineData("http://example.com", "http://example.com")]
    [InlineData("mailto:a@b.co", "mailto:a@b.co")]
    [InlineData("#bagian-1", "#bagian-1")]
    [InlineData("catatan/lain.md", "catatan/lain.md")]
    [InlineData("../x.md#y", "../x.md#y")]
    public void SafeLinkUrls_AreKept(string url, string expectedHref)
    {
        Assert.Contains($"<a href=\"{expectedHref}\">klik</a>", Body($"[klik]({url})"));
    }

    [Theory]
    [InlineData(@"\\host\share\a.png")]
    [InlineData(@"\\?\C:\a.png")]
    [InlineData("//host/share/a.png")]
    [InlineData("file://host/share/a.png")]
    [InlineData("javascript:alert(1)")]
    public void ClassifyUrl_BlocksUncSchemeRelativeAndUnknownSchemes(string url)
    {
        Assert.Equal(UrlKind.Blocked, MarkdownSupport.ClassifyUrl(url));
    }

    [Theory]
    [InlineData("a/b.png", UrlKind.Relative)]
    [InlineData("#x", UrlKind.Relative)]
    [InlineData("", UrlKind.Relative)]
    [InlineData("https://a.b/c", UrlKind.Http)]
    [InlineData("MAILTO:a@b.c", UrlKind.Mailto)]
    [InlineData("data:image/PNG;base64,AA", UrlKind.DataImage)]
    [InlineData("data:image/svg+xml,<svg/>", UrlKind.Blocked)]
    [InlineData("C:/x/a.png", UrlKind.LocalFile)]
    [InlineData("file:///C:/x/a.png", UrlKind.LocalFile)]
    [InlineData("java\tscript:alert(1)", UrlKind.Blocked)]
    public void ClassifyUrl_Categories(string url, UrlKind expected)
    {
        Assert.Equal(expected, MarkdownSupport.ClassifyUrl(url));
    }

    [Fact]
    public void Autolink_WithScriptScheme_IsNotLinked()
    {
        Assert.DoesNotContain("href=\"javascript", Body("<javascript:alert(1)>"));
    }

    [Fact]
    public void Autolink_Http_IsStillLinked()
    {
        Assert.Contains("<a href=\"https://example.com\">", Body("<https://example.com>"));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/svg+xml;base64,AAAA")]
    [InlineData("data:text/html;base64,AAAA")]
    [InlineData("file://host/share/a.png")]
    [InlineData("mailto:a@b.co")]
    public void UnsafeImageUrls_AreReplacedByText(string url)
    {
        var body = Body($"![teks](<{url}>)");

        Assert.DoesNotContain("<img", body);
        Assert.Contains("[gambar diblokir: teks]", body);
    }

    [Theory]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("data:image/jpeg;base64,AAAA")]
    [InlineData("data:image/gif;base64,AAAA")]
    [InlineData("data:image/webp;base64,AAAA")]
    [InlineData("https://example.com/a.png")]
    public void SafeImageUrls_AreKept(string url)
    {
        Assert.Contains($"src=\"{url}\"", Body($"![x]({url})"));
    }

    [Fact]
    public void RelativeImage_Small_IsEmbedded_Large_StaysRelative()
    {
        dir.WriteBytes("kecil.png", [1, 2, 3]);
        dir.WriteBytes("besar.png", new byte[(int)MarkdownSupport.MaxEmbeddedImageBytes + 1]);
        dir.WriteBytes("vektor.svg", [1]);

        var body = Body("![a](kecil.png) ![b](besar.png) ![c](vektor.svg)", dir.Path);

        Assert.Contains("src=\"data:image/png;base64,AQID\"", body);
        Assert.Contains("src=\"besar.png\"", body);
        Assert.Contains("src=\"vektor.svg\"", body);
        Assert.DoesNotContain("file:///", body);
        Assert.DoesNotContain(dir.Path, body);
    }

    [Fact]
    public void AbsoluteLocalImage_IsEmbeddedOrReplaced_NeverLeftAsPath()
    {
        var png = dir.WriteBytes("abs.png", [1]);
        var bmp = dir.WriteBytes("abs.bmp", [1]);

        var body = Body($"![ada]({png.Replace('\\', '/')}) ![tidak]({bmp.Replace('\\', '/')})", dir.Path);

        Assert.Contains("src=\"data:image/png;base64,AQ==\"", body);
        Assert.Contains("[gambar lokal tidak disertakan: tidak]", body);
        Assert.DoesNotContain(dir.Path.Replace('\\', '/'), body);
        Assert.DoesNotContain("file:///", body);
    }

    [Fact]
    public void ExportPipeline_DoesNotAffectThePreviewPipeline()
    {
        // Pratinjau WPF memakai pipeline terpisah; HTML mentah tetap diurai di sana (diabaikan renderer WPF).
        var preview = Markdig.Markdown.Parse("<div>x</div>", MarkdownSupport.Pipeline);
        var export = Markdig.Markdown.Parse("<div>x</div>", MarkdownSupport.ExportPipeline);

        Assert.Contains(preview.Descendants(), b => b is HtmlBlock);
        Assert.DoesNotContain(export.Descendants(), b => b is HtmlBlock);
    }
}

/// <summary>Pratinjau/cetak: path UNC dan URI file berhost selalu diblokir; gambar remote bisa diblokir lewat opsi.</summary>
public class RemoteImageBlockingTests : IDisposable
{
    readonly TempDir dir = new();

    public void Dispose() => dir.Dispose();

    static MarkdownDocument Resolve(string markdown, string? baseDir, bool blockRemote)
    {
        var doc = Markdig.Markdown.Parse(markdown, MarkdownSupport.Pipeline);
        MarkdownSupport.ResolveImageUrls(doc, baseDir, blockRemote);
        return doc;
    }

    static string Text(MarkdownDocument doc) =>
        string.Concat(doc.Descendants<LiteralInline>().Select(l => l.Content.ToString()));

    // Url diset langsung: escape backslash di sintaks markdown membuat penulisan UNC di sumber membingungkan.
    static MarkdownDocument ResolveUrl(string url, string? baseDir, bool blockRemote)
    {
        var doc = Markdig.Markdown.Parse("![x](a.png)", MarkdownSupport.Pipeline);
        doc.Descendants<LinkInline>().Single().Url = url;
        MarkdownSupport.ResolveImageUrls(doc, baseDir, blockRemote);
        return doc;
    }

    [Theory]
    [InlineData(@"\\host\share\a.png")]
    [InlineData("//host/share/a.png")]
    [InlineData("file://host/share/a.png")]
    [InlineData("file:////host/share/a.png")]
    public void UncAndHostFileUrls_AreAlwaysBlocked(string url)
    {
        var doc = ResolveUrl(url, dir.Path, blockRemote: false);

        Assert.DoesNotContain(doc.Descendants<LinkInline>(), l => l.IsImage);
        Assert.Equal(MarkdownSupport.BlockedRemoteImageText, Text(doc));
    }

    [Fact]
    public void UncUrls_AreBlockedEvenWithoutBaseDirectory()
    {
        var doc = ResolveUrl(@"\\host\share\a.png", null, blockRemote: false);

        Assert.Equal(MarkdownSupport.BlockedRemoteImageText, Text(doc));
    }

    [Fact]
    public void LocalPathResultingInUncShare_IsBlocked_UnlessMarkdownLivesOnThatShare()
    {
        Assert.True(MarkdownSupport.IsAllowedLocalPath(@"C:.png", @"C:"));
        Assert.False(MarkdownSupport.IsAllowedLocalPath(@"\\host\share\b.png", @"C:\a"));
        Assert.False(MarkdownSupport.IsAllowedLocalPath(@"\\host\share\b.png", null));
        Assert.False(MarkdownSupport.IsAllowedLocalPath(@"\\host\other\b.png", @"\\host\share\docs"));
        Assert.True(MarkdownSupport.IsAllowedLocalPath(@"\\HOST\share\img\b.png", @"\\host\share\docs"));
    }

    [Fact]
    public void HttpImages_AreBlockedOnlyWhenRequested()
    {
        var blocked = Resolve("![x](https://example.com/a.png)", dir.Path, blockRemote: true);
        var allowed = Resolve("![x](https://example.com/a.png)", dir.Path, blockRemote: false);

        Assert.Equal("[gambar remote diblokir]", Text(blocked));
        Assert.DoesNotContain(blocked.Descendants<LinkInline>(), l => l.IsImage);
        Assert.Equal("https://example.com/a.png", allowed.Descendants<LinkInline>().Single().Url);
    }

    [Fact]
    public void HttpImages_AreBlockedEvenWithoutBaseDirectory()
    {
        Assert.Equal("[gambar remote diblokir]", Text(Resolve("![x](http://example.com/a.png)", null, blockRemote: true)));
    }

    [Fact]
    public void LocalImages_AreNotAffectedByBlockRemote_ButDataImagesGetPreviewMarker()
    {
        var img = dir.WriteBytes("a.png", [1]);

        var doc = Resolve("![d](data:image/png;base64,AAAA) ![l](a.png)", dir.Path, blockRemote: true);

        var urls = doc.Descendants<LinkInline>().Where(l => l.IsImage).Select(l => l.Url).ToList();
        Assert.Equal(new[] { new Uri(img).AbsoluteUri }, urls);
        Assert.Contains(MarkdownSupport.UnsupportedPreviewImageText, Text(doc));
    }

    [Fact]
    public void LinksAreNeverBlocked()
    {
        var doc = Resolve("[x](https://example.com)", dir.Path, blockRemote: true);

        Assert.Equal("https://example.com", doc.Descendants<LinkInline>().Single().Url);
    }
}

[Collection("Wpf")]
public class DocumentTabHardeningTests : IDisposable
{
    readonly TempDir dir = new();
    readonly List<DocumentTab> tabs = [];

    public void Dispose()
    {
        WpfHost.Instance.Run(() => { foreach (var t in tabs) t.Dispose(); });
        tabs.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        dir.Dispose();
    }

    static void Sta(Action action) => WpfHost.Instance.Run(action);

    DocumentTab Load(string path)
    {
        var tab = DocumentTab.Load(path);
        tabs.Add(tab);
        return tab;
    }

    static void WriteExternal(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(false));

    // ---- Simpan vs perubahan dari luar ----

    [Fact]
    public void SaveTo_ChangedOnDisk_WithoutHandler_Throws_AndDoesNotOverwrite()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            tab.Document.Text = "punya saya";
            WriteExternal(path, "orang lain");

            Assert.Throws<ExternalChangeException>(() => tab.SaveTo(path));

            Assert.Equal("orang lain", File.ReadAllText(path));
            Assert.True(tab.IsDirty);
        });
    }

    [Fact]
    public void SaveTo_Conflict_Overwrite_WritesEditorVersion()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            tab.SaveConflict += _ => SaveConflictChoice.Overwrite;
            tab.Document.Text = "punya saya";
            WriteExternal(path, "orang lain");

            Assert.True(tab.SaveTo(path));

            Assert.Equal("punya saya", File.ReadAllText(path));
            Assert.False(tab.IsDirty);
        });
    }

    [Fact]
    public void SaveTo_Conflict_Reload_TakesDiskContent_AndWritesNothing()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            tab.SaveConflict += _ => SaveConflictChoice.Reload;
            tab.Document.Text = "punya saya";
            WriteExternal(path, "orang lain");

            Assert.False(tab.SaveTo(path));

            Assert.Equal("orang lain", File.ReadAllText(path));
            Assert.Equal("orang lain", tab.Document.Text);
            Assert.False(tab.IsDirty);
        });
    }

    [Fact]
    public void SaveTo_Conflict_Cancel_WritesNothing_AndKeepsEditorDirty()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            tab.SaveConflict += _ => SaveConflictChoice.Cancel;
            tab.Document.Text = "punya saya";
            WriteExternal(path, "orang lain");

            Assert.False(tab.SaveTo(path));

            Assert.Equal("orang lain", File.ReadAllText(path));
            Assert.Equal("punya saya", tab.Document.Text);
            Assert.True(tab.IsDirty);
        });
    }

    [Fact]
    public void SaveTo_NormalRepeatedSaves_NeverRaiseConflict()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            var asked = 0;
            tab.SaveConflict += _ => { asked++; return SaveConflictChoice.Cancel; };

            for (var i = 0; i < 3; i++)
            {
                tab.Document.Insert(0, "x");
                Assert.True(tab.SaveTo(path));
            }

            Assert.Equal(0, asked);
            Assert.Equal("xxxlama", File.ReadAllText(path));
        });
    }

    [Fact]
    public void SaveTo_TouchWithIdenticalContent_IsNotAConflict()
    {
        var path = dir.WriteText("a.md", "sama");

        Sta(() =>
        {
            var tab = Load(path);
            var asked = 0;
            tab.SaveConflict += _ => { asked++; return SaveConflictChoice.Cancel; };
            tab.Document.Insert(0, "x");
            WriteExternal(path, "sama");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(1));

            Assert.True(tab.SaveTo(path));

            Assert.Equal(0, asked);
        });
    }

    [Fact]
    public void SaveTo_FileDeletedMeanwhile_JustRecreatesIt()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            tab.Dispose();
            File.Delete(path);
            tab.Document.Text = "baru";

            Assert.True(tab.SaveTo(path));

            Assert.Equal("baru", File.ReadAllText(path));
        });
    }

    [Fact]
    public void SaveTo_SaveAsOtherPath_DoesNotCheckTheOldFile()
    {
        var oldPath = dir.WriteText("lama.md", "lama");
        var newPath = dir.File("baru.md");

        Sta(() =>
        {
            var tab = Load(oldPath);
            var asked = 0;
            tab.SaveConflict += _ => { asked++; return SaveConflictChoice.Cancel; };
            WriteExternal(oldPath, "diubah");
            tab.Document.Text = "isi";

            Assert.True(tab.SaveTo(newPath));

            Assert.Equal(0, asked);
        });
    }

    [Fact]
    public void ExternalConflict_UnansweredStaysPending_UntilReloadKeepOrSave()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            tab.Document.Insert(0, "x");
            WriteExternal(path, "orang lain");

            tab.CheckExternalChange();
            Assert.True(tab.HasExternalConflict);

            // Belum dijawab: simpan tetap mendeteksi konflik.
            var asked = 0;
            tab.SaveConflict += _ => { asked++; return SaveConflictChoice.Cancel; };
            Assert.False(tab.SaveTo(path));
            Assert.Equal(1, asked);
            Assert.True(tab.HasExternalConflict);
        });
    }

    [Fact]
    public void ExternalConflict_KeepEditorVersion_AcceptsDiskAsBaseline_SoSaveDoesNotPromptAgain()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            var asked = 0;
            tab.SaveConflict += _ => { asked++; return SaveConflictChoice.Cancel; };
            tab.Document.Insert(0, "x");
            WriteExternal(path, "orang lain");
            tab.CheckExternalChange();

            tab.KeepEditorVersion();

            Assert.False(tab.HasExternalConflict);
            tab.CheckExternalChange(); // perubahan yang sama tidak dilaporkan lagi
            Assert.True(tab.SaveTo(path));
            Assert.Equal(0, asked);
            Assert.Equal("xlama", File.ReadAllText(path));
        });
    }

    [Fact]
    public void ExternalConflict_DiskRevertedToKnownContent_ClearsPending()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            tab.Document.Insert(0, "x");
            WriteExternal(path, "orang lain");
            tab.CheckExternalChange();
            Assert.True(tab.HasExternalConflict);

            WriteExternal(path, "lama");
            tab.CheckExternalChange();

            Assert.False(tab.HasExternalConflict);
        });
    }

    // ---- Notifikasi ----

    [Fact]
    public void SaveTo_SamePath_DoesNotRaiseFilePathOrPathText()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            var raised = new List<string?>();
            tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            tab.Document.Insert(0, "x");

            tab.SaveTo(path);

            Assert.DoesNotContain(nameof(DocumentTab.FilePath), raised);
            Assert.DoesNotContain(nameof(DocumentTab.PathText), raised);
            Assert.DoesNotContain(nameof(DocumentTab.EncodingLabel), raised);
            Assert.Contains(nameof(DocumentTab.IsDirty), raised);
        });
    }

    // ---- BOM tidak valid ----

    [Fact]
    public void Load_BomFileWithInvalidBytes_IsMarkedLossy_AndSavingClearsTheFlag()
    {
        var path = dir.WriteBytes("rusak.md", [0xEF, 0xBB, 0xBF, 0x61, 0xFF, 0x62]);

        Sta(() =>
        {
            var tab = Load(path);

            Assert.True(tab.IsLossyDecoded);
            Assert.Equal("a\uFFFDb", tab.Document.Text);
            Assert.Equal("UTF-8 BOM", tab.EncodingLabel);

            tab.Document.Insert(0, "x");
            tab.SaveTo(path);

            Assert.False(tab.IsLossyDecoded);
        });
    }

    [Fact]
    public void Load_CleanBomFile_IsNotLossy()
    {
        var path = dir.WriteBytes("bagus.md", [0xEF, 0xBB, 0xBF, 0x61]);

        Sta(() => Assert.False(Load(path).IsLossyDecoded));
    }

    // ---- Dispose view ----

    [Fact]
    public void Dispose_DetachesView_EditingAfterwardsDoesNotThrowOrRender()
    {
        var path = dir.WriteText("a.md", "# judul");

        Sta(() =>
        {
            var tab = Load(path);
            tab.Dispose();

            Assert.Null(Record.Exception(() => tab.Document.Insert(0, "x")));
            Assert.Null(Record.Exception(() => tab.View.Dispose())); // idempoten
            Assert.Null(Record.Exception(() => tab.View.RefreshPreview()));
        });
    }

    [Fact]
    public void RefreshPreview_AfterBlockRemoteImagesToggle_RendersWithoutError()
    {
        var path = dir.WriteText("a.md", "![x](https://example.com/a.png)");
        var old = DocumentView.BlockRemoteImages;

        Sta(() =>
        {
            try
            {
                DocumentView.BlockRemoteImages = true;
                var tab = Load(path);
                var document = tab.View.BuildPrintDocument();
                var text = new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd).Text;
                Assert.Contains("[gambar remote diblokir]", text);

                DocumentView.BlockRemoteImages = false;
                Assert.Null(Record.Exception(() => tab.View.RefreshPreview()));
            }
            finally
            {
                DocumentView.BlockRemoteImages = old;
            }
        });
    }
}

public class TextFileIOHardeningTests : IDisposable
{
    readonly TempDir dir = new();

    public void Dispose() => dir.Dispose();

    [Fact]
    public void Decode_BomWithInvalidBytes_FallsBackLossyAndReports()
    {
        var (text, enc) = TextFileIO.Decode([0xEF, 0xBB, 0xBF, 0x61, 0xFF, 0x62], out var lossy);

        Assert.True(lossy);
        Assert.Equal("a\uFFFDb", text);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, enc.GetPreamble());
    }

    [Fact]
    public void Decode_Utf16WithOddTrailingByte_IsLossy()
    {
        var (_, _) = TextFileIO.Decode([0xFF, 0xFE, 0x61, 0x00, 0x62], out var lossy);

        Assert.True(lossy);
    }

    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x61 })]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x61, 0x00 })]
    [InlineData(new byte[] { 0x61, 0xE9 })] // bukan BOM: jatuh ke 1252, tidak dianggap lossy
    [InlineData(new byte[] { })]
    public void Decode_ValidOrNonBomInput_IsNotLossy(byte[] bytes)
    {
        _ = TextFileIO.Decode(bytes, out var lossy);

        Assert.False(lossy);
    }

    [Fact]
    public void Write_VeryLongFileName_StillSavesAtomically()
    {
        var path = dir.File(new string('a', 240) + ".md");

        TextFileIO.Write(path, "isi", new UTF8Encoding(false));
        TextFileIO.Write(path, "isi baru", new UTF8Encoding(false));

        Assert.Equal("isi baru", File.ReadAllText(path));
        Assert.Single(dir.Entries());
    }

    [Fact]
    public void FileStamp_RecentWrite_IsNotReliable_OldFileIs()
    {
        var path = dir.WriteText("a.md", "x");
        Assert.False(FileStamp.TryRead(path)!.Value.IsReliable);

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-1));
        Assert.True(FileStamp.TryRead(path)!.Value.IsReliable);
    }

    [Fact]
    public void FileStamp_MissingFile_IsNull()
    {
        Assert.Null(FileStamp.TryRead(dir.File("tidak-ada.md")));
    }
}

public class TextStatsSourceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("satu dua  tiga\r\nempat\nlima")]
    [InlineData("  \n  ")]
    public void CountSource_MatchesStringCount(string text)
    {
        Assert.Equal(TextStats.Count(text), TextStats.CountSource(new TextDocument(text)));
    }

    [Fact]
    public void CountSource_WordsAcrossChunkBoundaries_AreCountedOnce()
    {
        var text = string.Concat(Enumerable.Repeat("kata-panjang ", 20_000)) + "\r\n" + new string('x', 70_000);

        Assert.Equal(TextStats.Count(text), TextStats.CountSource(new TextDocument(text)));
    }
}

public class AppSettingsMergeTests : IDisposable
{
    readonly TempDir dir = new();

    public void Dispose() => dir.Dispose();

    string SettingsPath => dir.File("settings.json");

    [Fact]
    public void BlockRemoteImages_DefaultsToTrue_AndRoundTrips()
    {
        Assert.True(new AppSettings().BlockRemoteImages);

        new AppSettings { BlockRemoteImages = false }.Save(SettingsPath);

        Assert.False(AppSettings.Load(SettingsPath).BlockRemoteImages);
    }

    [Fact]
    public void SaveMerged_CombinesRecentFilesFromDisk_OwnFirst()
    {
        var other = new AppSettings { RecentFiles = [@"C:\a.md", @"C:\b.md"] };
        other.Save(SettingsPath);

        var mine = new AppSettings { RecentFiles = [@"C:\c.md", @"c:\A.md"] };
        Assert.True(mine.SaveMerged(SettingsPath));

        Assert.Equal([@"C:\c.md", @"c:\A.md", @"C:\b.md"], AppSettings.Load(SettingsPath).RecentFiles);
    }

    [Fact]
    public void SaveMerged_DoesNotResurrectRemovedRecentFiles_AndCapsAtTen()
    {
        new AppSettings { RecentFiles = [@"C:\hapus.md", @"C:\tetap.md"] }.Save(SettingsPath);

        var mine = new AppSettings { RecentFiles = [@"C:\hapus.md"] };
        mine.RemoveRecent(@"C:\hapus.md");
        mine.SaveMerged(SettingsPath);
        Assert.Equal([@"C:\tetap.md"], AppSettings.Load(SettingsPath).RecentFiles);

        var many = new AppSettings { RecentFiles = Enumerable.Range(0, 10).Select(i => $@"C:\{i}.md").ToList() };
        many.SaveMerged(SettingsPath);
        Assert.Equal(10, AppSettings.Load(SettingsPath).RecentFiles.Count);
    }

    [Fact]
    public void SaveMerged_KeepStoredSession_PreservesSessionOnDisk()
    {
        var stored = new SessionState { Tabs = [new SessionTab { Path = @"C:\sesi.md" }] };
        new AppSettings { Session = stored }.Save(SettingsPath);

        var mine = new AppSettings { Session = new SessionState { Tabs = [new SessionTab { Path = @"C:\lain.md" }] } };
        mine.SaveMerged(SettingsPath, keepStoredSession: true);

        Assert.Equal(@"C:\sesi.md", AppSettings.Load(SettingsPath).Session!.Tabs.Single().Path);
    }

    [Fact]
    public void SaveMerged_WithoutKeepStoredSession_WritesOwnSession()
    {
        new AppSettings { Session = new SessionState { Tabs = [new SessionTab { Path = @"C:\sesi.md" }] } }.Save(SettingsPath);

        var mine = new AppSettings { Session = new SessionState { Tabs = [new SessionTab { Path = @"C:\lain.md" }] } };
        mine.SaveMerged(SettingsPath);

        Assert.Equal(@"C:\lain.md", AppSettings.Load(SettingsPath).Session!.Tabs.Single().Path);
    }

    [Fact]
    public void SaveMerged_MissingFile_JustSaves()
    {
        var mine = new AppSettings { RecentFiles = [@"C:\x.md"] };

        Assert.True(mine.SaveMerged(SettingsPath));
        Assert.Equal([@"C:\x.md"], AppSettings.Load(SettingsPath).RecentFiles);
    }
}

public class SingleInstanceTests
{
    [Fact]
    public void Message_RoundTrips_AndOnlyAbsolutePathsAreAccepted()
    {
        var message = SingleInstance.BuildMessage([@"C:\a.md", "relatif.md", @"D:\b c\d.md"]);

        Assert.Equal([@"C:\a.md", @"D:\b c\d.md"], SingleInstance.ParseMessage(message));
    }

    [Fact]
    public void Message_WithWrongHeader_IsRejected()
    {
        Assert.Null(SingleInstance.ParseMessage("HALO\nC:\\a.md"));
        Assert.Null(SingleInstance.ParseMessage(""));
    }

    [Fact]
    public void Message_WithoutFiles_IsAnActivationRequest()
    {
        Assert.Empty(SingleInstance.ParseMessage(SingleInstance.BuildMessage([]))!);
    }

    [Fact]
    public async Task SecondInstance_ForwardsFilesToPrimary()
    {
        var scope = "test-" + Guid.NewGuid().ToString("N");
        using var primary = SingleInstance.Create(scope);
        Assert.True(primary.IsPrimary);
        var received = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.StartServer(files => received.TrySetResult(files));

        using var second = SingleInstance.Create(scope);
        Assert.False(second.IsPrimary);
        Assert.True(second.TrySendToPrimary([@"C:\x\a.md", @"C:\x\b.md"]));

        var files = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal([@"C:\x\a.md", @"C:\x\b.md"], files);
    }

    [Fact]
    public void PrimaryKeepsServingMultipleLaunches()
    {
        var scope = "test-" + Guid.NewGuid().ToString("N");
        using var primary = SingleInstance.Create(scope);
        var received = new System.Collections.Concurrent.BlockingCollection<IReadOnlyList<string>>();
        primary.StartServer(received.Add);

        for (var i = 0; i < 3; i++)
        {
            using var next = SingleInstance.Create(scope);
            Assert.True(next.TrySendToPrimary([$@"C:\f{i}.md"]));
            Assert.True(received.TryTake(out var files, TimeSpan.FromSeconds(10)));
            Assert.Equal([$@"C:\f{i}.md"], files);
        }
    }

    [Fact]
    public void TrySend_WithoutRunningServer_FailsSoCallerCanOpenItsOwnInstance()
    {
        var scope = "test-" + Guid.NewGuid().ToString("N");
        using var primary = SingleInstance.Create(scope); // memegang mutex tetapi tidak mendengarkan
        using var second = SingleInstance.Create(scope);

        Assert.False(second.IsPrimary);
        Assert.False(second.TrySendToPrimary([@"C:\a.md"], timeoutMs: 300));
    }

    [Fact]
    public void PrimaryCannotSendToItself()
    {
        using var primary = SingleInstance.Create("test-" + Guid.NewGuid().ToString("N"));

        Assert.False(primary.TrySendToPrimary([]));
    }
}
