using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MdViewer.Tests.Support;

namespace MdViewer.Tests;

/// <summary>Logika murni tata letak cetak (ukuran halaman, margin, orientasi, nilai bawaan tanpa printer).</summary>
[Collection("Wpf")]
public class PageLayoutTests
{
    [Fact]
    public void Default_IsA4PortraitWithNormalMargin()
    {
        var layout = PageLayout.Default;

        Assert.Equal(793.7, layout.Width, 1);
        Assert.Equal(1122.5, layout.Height, 1);
        Assert.Equal(PageLayout.MarginOf(MarginPreset.Normal), layout.Margin);
    }

    [Fact]
    public void Landscape_SwapsWidthAndHeight()
    {
        var portrait = PageLayout.For(PaperKind.Letter, PrintOrientation.Portrait, MarginPreset.Normal);
        var landscape = PageLayout.For(PaperKind.Letter, PrintOrientation.Landscape, MarginPreset.Normal);

        Assert.Equal((816d, 1056d), (portrait.Width, portrait.Height));
        Assert.Equal((1056d, 816d), (landscape.Width, landscape.Height));
    }

    [Fact]
    public void Margins_GrowFromNarrowToWide()
    {
        var narrow = PageLayout.MarginOf(MarginPreset.Narrow);
        var normal = PageLayout.MarginOf(MarginPreset.Normal);
        var wide = PageLayout.MarginOf(MarginPreset.Wide);

        Assert.True(narrow < normal && normal < wide);
        Assert.Equal(48, narrow); // 0,5 inci
        Assert.Equal(96, wide); // 1 inci
        Assert.True(narrow >= 48, "kaki halaman (di dalam margin bawah) butuh ruang");
    }

    [Theory]
    [InlineData(double.NaN, 1000)]
    [InlineData(800, double.NaN)]
    [InlineData(0, 0)]
    [InlineData(-5, 900)]
    [InlineData(double.PositiveInfinity, 900)]
    [InlineData(50, 50)]
    public void FromPrintableArea_InvalidSize_FallsBackToDefaultPage(double width, double height)
    {
        var layout = PageLayout.FromPrintableArea(width, height);

        Assert.Equal(PageLayout.Default.Width, layout.Width);
        Assert.Equal(PageLayout.Default.Height, layout.Height);
    }

    [Fact]
    public void FromPrintableArea_ValidSize_IsUsedAsIs()
    {
        var layout = PageLayout.FromPrintableArea(700, 1000, MarginPreset.Wide);

        Assert.Equal((700d, 1000d, 96d), (layout.Width, layout.Height, layout.Margin));
    }

    [Fact]
    public void EffectiveMargin_NeverExceedsAQuarterOfTheShortSide()
    {
        var layout = new PageLayout(300, 400, 200);

        Assert.Equal(75, layout.EffectiveMargin);
        Assert.Equal(new Thickness(75), layout.Padding);
        Assert.Equal(0, new PageLayout(300, 400, -10).EffectiveMargin);
    }

    [Fact]
    public void Apply_SetsPageSizeSingleColumnAndPadding()
    {
        var layout = PageLayout.For(PaperKind.A4, PrintOrientation.Landscape, MarginPreset.Narrow);
        WpfHost.Instance.Run(() =>
        {
            var document = new FlowDocument();

            layout.Apply(document);

            Assert.Equal(layout.Width, document.PageWidth);
            Assert.Equal(layout.Height, document.PageHeight);
            Assert.Equal(layout.Width, document.ColumnWidth);
            Assert.Equal(new Thickness(48), document.PagePadding);
        });
    }
}

/// <summary>Jendela Pratinjau Cetak dan paginator kaki halaman: dibuat di STA, paginasi async, snapshot.</summary>
[Collection("Wpf")]
public class PrintPreviewWindowTests : IDisposable
{
    static readonly TimeSpan Patience = PrintTestKit.Patience;

    readonly List<PrintPreviewWindow> windows = [];
    readonly TempDir dir = new();

    public void Dispose()
    {
        WpfHost.Instance.Run(() =>
        {
            foreach (var window in windows) window.Close();
        });
        dir.Dispose();
    }

    static PrintSnapshot Snapshot(string text, string title = "contoh.md", bool blockRemote = true) =>
        new(text, null, blockRemote, title);

    const string Sample = "# Judul\n\nParagraf pertama dengan **tebal** dan `kode`.\n\n- satu\n- dua\n\n> kutipan\n\n| a | b |\n|---|---|\n| 1 | 2 |\n";

    static string Long(int paragraphs) =>
        "# Dokumen panjang\n\n" + string.Concat(Enumerable.Range(1, paragraphs)
            .Select(i => $"Paragraf nomor {i}: " + string.Concat(Enumerable.Repeat("lorem ipsum dolor sit amet ", 12)) + "\n\n"));

    PrintPreviewWindow Open(string text, bool show = false)
    {
        var window = new PrintPreviewWindow(Snapshot(text));
        windows.Add(window);
        if (show) window.Show();
        return window;
    }

    // Cukup sampai paginasi selesai (jumlah halaman final diketahui); tidak perlu menunggu halaman XPS ditulis.
    static void WaitUntilPaginated(PrintPreviewWindow window) =>
        Assert.True(UiPump.Until(() => window.Stage is not (null or PreviewStage.Paginating), Patience), "paginasi pratinjau tidak selesai");

    static void WaitUntilReady(PrintPreviewWindow window) =>
        Assert.True(UiPump.Until(() => !window.IsBusy, Patience), "penyusunan pratinjau tidak selesai");

    [Fact]
    public void Creating_OnStaThread_DoesNotThrow_AndStartsPaginating()
    {
        WpfHost.Instance.Run(() =>
        {
            var window = Open(Sample);

            Assert.Equal("Pratinjau Cetak — contoh.md", window.Title);
            Assert.Equal(PageLayout.Default, window.Layout);
            WaitUntilPaginated(window);
        });
    }

    [Fact]
    public void SampleDocument_HasAtLeastOnePage()
    {
        WpfHost.Instance.Run(() =>
        {
            var window = Open(Sample);

            WaitUntilPaginated(window);

            Assert.True(window.PageCount > 0);
            Assert.Equal(1, window.PageCount);
        });
    }

    [Fact]
    public void LongDocument_HasMorePagesThanShortOne()
    {
        WpfHost.Instance.Run(() =>
        {
            var shortWindow = Open(Sample);
            var longWindow = Open(Long(120));

            WaitUntilPaginated(shortWindow);
            WaitUntilPaginated(longWindow);

            Assert.True(longWindow.PageCount > shortWindow.PageCount + 1, $"{longWindow.PageCount} vs {shortWindow.PageCount}");
        });
    }

    [Fact]
    public void ChangingOrientation_RepaginatesWithDifferentPageSize()
    {
        WpfHost.Instance.Run(() =>
        {
            var window = Open(Long(60));
            WaitUntilPaginated(window);
            var portraitPages = window.PageCount;

            ((RadioButton)window.FindName("LandscapeButton")).IsChecked = true;

            Assert.Equal(PreviewStage.Paginating, window.Stage); // layout baru langsung memulai paginasi ulang
            WaitUntilPaginated(window);
            Assert.True(window.Layout.Width > window.Layout.Height);
            Assert.Equal(window.Layout.Width, window.PrintedDocument.PageWidth);
            Assert.True(window.PageCount > portraitPages, $"lanskap {window.PageCount} vs potret {portraitPages}");
        });
    }

    [Fact]
    public void ChangingPaperAndMargin_UpdatesLayout()
    {
        WpfHost.Instance.Run(() =>
        {
            var window = Open(Sample);
            WaitUntilPaginated(window);

            ((RadioButton)window.FindName("LetterButton")).IsChecked = true;
            ((RadioButton)window.FindName("WideMarginButton")).IsChecked = true;
            WaitUntilPaginated(window);

            Assert.Equal(PageLayout.For(PaperKind.Letter, PrintOrientation.Portrait, MarginPreset.Wide), window.Layout);
            Assert.Equal(816, window.PrintedDocument.PageWidth);
            Assert.Equal(new Thickness(96), window.PrintedDocument.PagePadding);
        });
    }

    [Fact]
    public void Snapshot_IsNotAffectedByLaterEditorChanges()
    {
        var path = dir.WriteText("snapshot.md", "SEBELUM perubahan");
        WpfHost.Instance.Run(() =>
        {
            var tab = DocumentTab.Load(path);
            try
            {
                var window = new PrintPreviewWindow(tab.View.CapturePrintSnapshot());
                windows.Add(window);

                tab.Document.Text = "SESUDAH perubahan";
                WaitUntilPaginated(window);

                var text = UiPump.TextOf(window.PrintedDocument);
                Assert.Contains("SEBELUM", text);
                Assert.DoesNotContain("SESUDAH", text);
                Assert.Equal(Path.GetFileName(path), window.Title.Split("— ")[1]);
            }
            finally
            {
                tab.Dispose();
            }
        });
    }

    [Fact]
    public void PrintDocument_StillBlocksRemoteAndUncImages()
    {
        WpfHost.Instance.Run(() =>
        {
            var snapshot = Snapshot("a ![x](https://example.com/a.png) b ![y](\\\\host\\share\\a.png) c");

            var text = UiPump.TextOf(PrintService.CreateDocument(snapshot, PageLayout.Default));

            Assert.Contains("[gambar remote diblokir]", text);
            Assert.DoesNotContain("example.com", text);
            Assert.DoesNotContain("host", text);
        });
    }

    [Fact]
    public void Paginator_ReturnsPagesWithFooter_AndTheSamePageTwice()
    {
        WpfHost.Instance.Run(() =>
        {
            var document = PrintService.CreateDocument(Snapshot(Long(60)), PageLayout.Default);
            var inner = (DynamicDocumentPaginator)((IDocumentPaginatorSource)document).DocumentPaginator;
            _ = inner.PageCount; // paginasi sinkron
            var paginator = PrintService.CreatePaginator(document, "contoh.md", headerFooter: true);

            Assert.True(paginator.PageCount > 1);
            foreach (var number in new[] { 0, 1, 0, paginator.PageCount - 1 })
            {
                using var page = paginator.GetPage(number);
                Assert.NotNull(page.Visual);
                Assert.Equal(PageLayout.Default.Width, page.Size.Width, 1);
                Assert.Equal(PageLayout.Default.Height, page.Size.Height, 1);
            }

            using var plain = PrintService.CreatePaginator(document, "contoh.md", headerFooter: false).GetPage(0);
            Assert.NotNull(plain.Visual);
        });
    }

    [Fact]
    public void EmptyDocument_FinishesWithoutHanging()
    {
        WpfHost.Instance.Run(() =>
        {
            var window = Open("");

            WaitUntilReady(window);

            Assert.False(window.HasFailed);
            Assert.Equal(1, window.PageCount);
        });
    }

    [Fact]
    public void PreviewBuild_ProducesFixedPagesMatchingThePaginator_AndDisposeIsIdempotent()
    {
        WpfHost.Instance.Run(() =>
        {
            var build = new PreviewBuild(Snapshot(Long(40)), PageLayout.Default, headerFooter: true);
            build.Start();

            Assert.True(UiPump.Until(() => build.Stage is PreviewStage.Ready or PreviewStage.Failed, Patience));

            Assert.Equal(PreviewStage.Ready, build.Stage);
            Assert.NotNull(build.Pages);
            Assert.True(build.PageCount > 1);
            Assert.Equal(build.PageCount, build.Pages!.DocumentPaginator.PageCount);
            Assert.Null(Record.Exception(build.Dispose));
            Assert.Null(Record.Exception(build.Dispose));
            Assert.Null(build.Pages);
        });
    }

    [Fact]
    public void PreviewBuild_DisposedWhilePaginating_DoesNotRaiseLaterEvents()
    {
        WpfHost.Instance.Run(() =>
        {
            var build = new PreviewBuild(Snapshot(Long(200)), PageLayout.Default, headerFooter: false);
            var raised = 0;
            build.Changed += () => raised++;
            build.Start();
            var before = raised;

            build.Dispose();
            UiPump.For(TimeSpan.FromMilliseconds(500));

            Assert.Equal(before, raised);
            Assert.NotEqual(PreviewStage.Ready, build.Stage);
        });
    }

    [Fact]
    public void ShownWindow_DisplaysPages_AndNavigationWorks()
    {
        WpfHost.Instance.Run(() =>
        {
            var window = Open(Long(80), show: true);
            WaitUntilReady(window);
            var viewer = (DocumentViewer)window.FindName("Viewer");
            Assert.True(UiPump.Until(() => viewer.Document is not null, Patience));

            Assert.True(window.PageCount > 2);
            Assert.True(UiPump.Until(() => viewer.PageCount == window.PageCount, Patience));

            // Zoom "satu halaman" diterapkan setelah tata letak (bukan lagi 100%).
            Assert.True(UiPump.Until(() => Math.Abs(viewer.Zoom - 100) > 0.5, Patience), $"zoom {viewer.Zoom}");
            Assert.True(UiPump.Until(() => window.PageStatusText == $"Halaman 1 dari {window.PageCount}", Patience), window.PageStatusText);

            var expected = $"Halaman {window.PageCount} dari {window.PageCount}";
            // Diulang: perubahan zoom/tata letak yang tersisa dari pemasangan halaman bisa menggeser posisi sesudah panggilan pertama.
            Assert.True(UiPump.Until(() => { viewer.LastPage(); return window.PageStatusText == expected; }, Patience), window.PageStatusText);
        });
    }
}
