using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Makdon.Tests.Support;
using static Makdon.Tests.Support.PrintTestKit;

namespace Makdon.Tests;

/// <summary>Kaki halaman (nama dokumen + "Halaman X dari N") dan perilaku pembungkus paginator.</summary>
[Collection("Wpf")]
public class HeaderFooterPaginatorTests
{
    static void Sta(Action action) => WpfHost.Instance.Run(action);

    static int InnerPageCount(string text, PageLayout layout)
    {
        var document = PrintService.CreateDocument(Snapshot(text), layout);
        var inner = (DynamicDocumentPaginator)((IDocumentPaginatorSource)document).DocumentPaginator;
        inner.ComputePageCount();
        return inner.PageCount;
    }

    [Fact]
    public void Footer_HasDocumentNameAndPageXOfN_OnEveryPage()
    {
        Sta(() =>
        {
            var paginator = Paginator(Paragraphs(30), "catatan.md");
            var total = paginator.PageCount;
            Assert.True(total > 3, $"{total} halaman");

            for (var number = 0; number < total; number++)
            {
                using var page = paginator.GetPage(number);
                var footer = FooterTexts(page);

                Assert.Contains("catatan.md", footer);
                Assert.Contains($"Halaman {number + 1} dari {total}", footer);
            }
        });
    }

    [Fact]
    public void FooterOff_AddsNothingToThePage()
    {
        Sta(() =>
        {
            var paginator = Paginator(Paragraphs(30), footer: false);

            using var page = paginator.GetPage(0);

            Assert.NotNull(page.Visual);
            Assert.Empty(FooterTexts(page));
        });
    }

    [Fact]
    public void PageCount_IsTheSameWithAndWithoutFooter_AndAsTheBasePaginator()
    {
        Sta(() =>
        {
            var text = Paragraphs(40);

            var withFooter = Paginator(text, footer: true).PageCount;
            var withoutFooter = Paginator(text, footer: false).PageCount;

            Assert.Equal(InnerPageCount(text, Small), withFooter);
            Assert.Equal(withFooter, withoutFooter);
            Assert.True(withFooter > 1);
        });
    }

    [Fact]
    public void Footer_DoesNotEnterTheContentArea_SoBodyTextIsNotMovedByIt()
    {
        Sta(() =>
        {
            using var with = Paginator(Paragraphs(30), footer: true).GetPage(1);
            using var without = Paginator(Paragraphs(30), footer: false).GetPage(1);

            Assert.Equal(without.Size, with.Size);
            Assert.Equal(without.ContentBox, with.ContentBox);
            Assert.Equal(without.BleedBox, with.BleedBox);
        });
    }

    [Fact]
    public void GetPage_TheSamePageTwice_OneAfterTheOtherDisposed_GivesTheSameFooter()
    {
        Sta(() =>
        {
            var paginator = Paginator(Paragraphs(30));

            List<string> first;
            using (var page = paginator.GetPage(2)) first = FooterTexts(page);
            using var second = paginator.GetPage(2);

            Assert.Equal(first, FooterTexts(second));
            Assert.Contains("Halaman 3 dari " + paginator.PageCount, first);
        });
    }

    [Fact]
    public void GetPage_TheSamePageTwice_WhileBothAreHeld_DoesNotThrow()
    {
        Sta(() =>
        {
            var paginator = Paginator(Paragraphs(30));

            var first = paginator.GetPage(2);
            var second = Record.Exception(() => paginator.GetPage(2));

            Assert.Null(second);
            first.Dispose();
        });
    }

    [Fact]
    public void GetPage_OutOfRange_BeyondLastIsMissing_NegativeIsRejected()
    {
        Sta(() =>
        {
            var paginator = Paginator(Paragraphs(10));

            var beyond = paginator.GetPage(paginator.PageCount);
            var far = paginator.GetPage(paginator.PageCount + 1000);

            Assert.Same(DocumentPage.Missing, beyond);
            Assert.Same(DocumentPage.Missing, far);
            Assert.Empty(FooterTexts(beyond));
            Assert.Throws<ArgumentOutOfRangeException>(() => paginator.GetPage(-1));
        });
    }

    [Fact]
    public void DisposingTheWrappedPage_IsIdempotent_AndLaterPagesStillWork()
    {
        Sta(() =>
        {
            var paginator = Paginator(Paragraphs(20));
            var page = paginator.GetPage(0);

            page.Dispose();

            Assert.Null(Record.Exception(page.Dispose));
            using var again = paginator.GetPage(0);
            Assert.NotEmpty(FooterTexts(again));
        });
    }

    [Fact]
    public void PageSize_IsForwardedToTheBasePaginator()
    {
        Sta(() =>
        {
            var document = PrintService.CreateDocument(Snapshot(Paragraphs(20)), Small);
            var paginator = PrintService.CreatePaginator(document, "x", headerFooter: true);
            var inner = (DynamicDocumentPaginator)((IDocumentPaginatorSource)document).DocumentPaginator;

            Assert.Equal(inner.PageSize, paginator.PageSize);

            paginator.PageSize = new Size(300, 500);

            Assert.Equal(new Size(300, 500), inner.PageSize);
            Assert.Equal(inner.PageSize, paginator.PageSize);
        });
    }

    [Fact]
    public void Source_IsTheWrapperItself_SoWritersPaginateThroughTheFooter()
    {
        Sta(() =>
        {
            var paginator = Paginator(Paragraphs(5));

            Assert.Same(paginator, paginator.Source);
            Assert.Same(paginator, ((IDocumentPaginatorSource)paginator).DocumentPaginator);
        });
    }

    // ---- IsPageCountValid / PageCount / ComputePageCount ----

    [Fact]
    public void CreatePaginator_ComputesThePageCountUpFront()
    {
        Sta(() =>
        {
            var document = PrintService.CreateDocument(Snapshot(Paragraphs(30)), Small);
            var inner = (DynamicDocumentPaginator)((IDocumentPaginatorSource)document).DocumentPaginator;
            Assert.False(inner.IsPageCountValid); // dokumen baru belum dipaginasi

            var paginator = PrintService.CreatePaginator(document, "x", headerFooter: true);

            Assert.True(paginator.IsPageCountValid);
            Assert.True(paginator.PageCount > 1);
        });
    }

    [Fact]
    public void ComputePageCount_IsForwarded_WhenWrapperIsBuiltOnAFreshPaginator()
    {
        Sta(() =>
        {
            var document = PrintService.CreateDocument(Snapshot(Paragraphs(30)), Small);
            var inner = (DynamicDocumentPaginator)((IDocumentPaginatorSource)document).DocumentPaginator;
            var wrapper = new HeaderFooterPaginator(inner, document.PagePadding, "x", true);
            Assert.False(wrapper.IsPageCountValid);
            Assert.Equal(0, wrapper.PageCount); // sebelum dihitung: tidak mengarang jumlah

            wrapper.ComputePageCount(); // yang dipanggil penulis XPS/PrintDialog sebelum membaca PageCount

            Assert.True(wrapper.IsPageCountValid);
            Assert.Equal(inner.PageCount, wrapper.PageCount);
            Assert.True(wrapper.PageCount > 1);
        });
    }

    [Fact]
    public void Footer_WhenCountIsNotYetKnown_ShowsOnlyThePageNumber()
    {
        Sta(() =>
        {
            var document = PrintService.CreateDocument(Snapshot(Paragraphs(30)), Small);
            var inner = (DynamicDocumentPaginator)((IDocumentPaginatorSource)document).DocumentPaginator;
            var wrapper = new HeaderFooterPaginator(inner, document.PagePadding, "x", true);

            using var page = wrapper.GetPage(0);

            var footer = FooterTexts(page);
            Assert.Contains("Halaman 1", footer);
            Assert.DoesNotContain(footer, text => text.Contains("dari"));
        });
    }

    // ---- Nama dokumen yang aneh ----

    public static TheoryData<string> OddTitles() => new()
    {
        "",
        " ",
        new string('x', 5000),
        string.Concat(Enumerable.Repeat("nama-berkas-sangat-panjang ", 100)) + ".md",
        "a\nb\r\nc\td",
        "{0} {1} {{x}} %s %d",
        "<b>&amp;</b> \"kutip\" 'tunggal' \\ /",
        "catatan-العربية-עברית.md",
        "\u202Eexe.dm",
        "😀 emoji 🎉👨‍👩‍👧‍👦.md",
        "日本語のファイル名.md",
        "\0kontrol\u0001\u001F.md",
        "ujung\uD83D",          // pasangan surrogat putus
        "\uDE00awal",
        "é́́́ diakritik bertumpuk",
    };

    [Theory]
    [MemberData(nameof(OddTitles))]
    public void OddDocumentNames_DoNotThrow_AndStillProduceAFooterWithPageNumber(string title)
    {
        Sta(() =>
        {
            var paginator = Paginator(Paragraphs(8), title);
            var total = paginator.PageCount;

            for (var number = 0; number < total; number++)
            {
                using var page = paginator.GetPage(number);
                Assert.Contains($"Halaman {number + 1} dari {total}", FooterTexts(page));
            }
            Assert.True(total >= 1);
        });
    }

    [Fact]
    public void VeryLongName_IsTrimmedToOneLine_SoItDoesNotCollideWithThePageNumber()
    {
        Sta(() =>
        {
            var title = new string('W', 2000) + ".md";
            var paginator = Paginator(Paragraphs(8), title);
            using var page = paginator.GetPage(0);

            var runs = new List<(string Text, Rect Box)>();
            void Collect(Drawing d)
            {
                if (d is DrawingGroup g) foreach (var c in g.Children) Collect(c);
                else if (d is GlyphRunDrawing r)
                {
                    var box = r.GlyphRun.ComputeAlignmentBox();
                    var origin = r.GlyphRun.BaselineOrigin;
                    runs.Add((new string(r.GlyphRun.Characters.ToArray()), new Rect(origin.X + box.X, origin.Y + box.Y, box.Width, box.Height)));
                }
            }
            Collect(((DrawingVisual)VisualTreeHelper.GetChild(page.Visual, 1)).Drawing);

            var counter = runs.Single(r => r.Text.StartsWith("Halaman")).Box;
            var name = runs.Where(r => !r.Text.StartsWith("Halaman")).Select(r => r.Box).ToList();
            Assert.NotEmpty(name);
            Assert.True(name.Max(b => b.Right) <= counter.Left, $"nama berakhir di {name.Max(b => b.Right)}, nomor halaman mulai di {counter.Left}");
            Assert.True(counter.Right <= Small.Width - Small.EffectiveMargin + 1, "nomor halaman melewati margin kanan");
            Assert.True(name.Min(b => b.Left) >= Small.EffectiveMargin - 1, "nama melewati margin kiri");
            Assert.True(name.All(b => Math.Abs(b.Top - counter.Top) < 5), "nama harus satu baris sejajar nomor halaman");
        });
    }

    [Fact]
    public void Footer_IsDrawnInsideTheBottomMargin()
    {
        Sta(() =>
        {
            var paginator = Paginator(Paragraphs(8));
            using var page = paginator.GetPage(0);

            var bounds = ((DrawingVisual)VisualTreeHelper.GetChild(page.Visual, 1)).Drawing.Bounds;

            Assert.True(bounds.Top >= page.ContentBox.Bottom - 0.5, $"kaki ({bounds.Top}) menimpa isi (berakhir {page.ContentBox.Bottom})");
            Assert.True(bounds.Bottom <= page.Size.Height + 0.5, "kaki keluar dari halaman");
        });
    }

    [Theory]
    [InlineData(MarginPreset.Narrow)]
    [InlineData(MarginPreset.Normal)]
    [InlineData(MarginPreset.Wide)]
    public void Footer_StaysAtLeastAQuarterInchFromThePaperEdge_ForEveryRealMarginPreset(MarginPreset margin)
    {
        Sta(() =>
        {
            var layout = PageLayout.For(PaperKind.A4, PrintOrientation.Portrait, margin);
            var paginator = Paginator(Paragraphs(8), layout: layout);
            using var page = paginator.GetPage(0);

            var bounds = ((DrawingVisual)VisualTreeHelper.GetChild(page.Visual, 1)).Drawing.Bounds;

            Assert.True(page.Size.Height - bounds.Bottom >= 24 - 1, $"kaki {page.Size.Height - bounds.Bottom:0.#} DIP dari tepi bawah (margin {margin})");
            Assert.True(bounds.Top >= page.ContentBox.Bottom - 0.5, $"kaki ({bounds.Top}) menimpa isi (berakhir {page.ContentBox.Bottom})");
        });
    }

    [Fact]
    public void EmptyDocument_HasOnePage_WithFooter()
    {
        Sta(() =>
        {
            var paginator = Paginator("");

            Assert.Equal(1, paginator.PageCount);
            using var page = paginator.GetPage(0);
            Assert.Contains("Halaman 1 dari 1", FooterTexts(page));
        });
    }
}
