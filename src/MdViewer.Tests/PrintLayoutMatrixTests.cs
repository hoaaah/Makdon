using System.Windows;
using System.Windows.Documents;
using MdViewer.Tests.Support;

namespace MdViewer.Tests;

/// <summary>Seluruh kombinasi kertas x orientasi x margin dan nilai area cetak yang tidak masuk akal.</summary>
[Collection("Wpf")]
public class PageLayoutMatrixTests
{
    const double A4WidthDip = 210 / 25.4 * 96;   // 793,70
    const double A4HeightDip = 297 / 25.4 * 96;  // 1122,52

    public static TheoryData<PaperKind, PrintOrientation, MarginPreset> AllCombinations()
    {
        var data = new TheoryData<PaperKind, PrintOrientation, MarginPreset>();
        foreach (var paper in Enum.GetValues<PaperKind>())
            foreach (var orientation in Enum.GetValues<PrintOrientation>())
                foreach (var margin in Enum.GetValues<MarginPreset>())
                    data.Add(paper, orientation, margin);
        return data;
    }

    public static TheoryData<PaperKind, PrintOrientation> PaperAndOrientation()
    {
        var data = new TheoryData<PaperKind, PrintOrientation>();
        foreach (var paper in Enum.GetValues<PaperKind>())
            foreach (var orientation in Enum.GetValues<PrintOrientation>())
                data.Add(paper, orientation);
        return data;
    }

    static (double Width, double Height) PortraitSize(PaperKind paper) =>
        paper == PaperKind.Letter ? (816, 1056) : (A4WidthDip, A4HeightDip);

    [Fact]
    public void AllCombinations_AreCoveredByTheTheory() =>
        Assert.Equal(12, AllCombinations().Count);

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void For_GivesPaperSizeInDips_SwappedForLandscape(PaperKind paper, PrintOrientation orientation, MarginPreset margin)
    {
        var layout = PageLayout.For(paper, orientation, margin);

        var (width, height) = PortraitSize(paper);
        if (orientation == PrintOrientation.Landscape) (width, height) = (height, width);
        Assert.Equal(width, layout.Width, 2);
        Assert.Equal(height, layout.Height, 2);
        Assert.Equal(orientation == PrintOrientation.Landscape, layout.Width > layout.Height);
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void For_UsesTheMarginPreset_AndLeavesPositiveContentArea(PaperKind paper, PrintOrientation orientation, MarginPreset margin)
    {
        var layout = PageLayout.For(paper, orientation, margin);

        Assert.Equal(PageLayout.MarginOf(margin), layout.Margin);
        Assert.Equal(layout.Margin, layout.EffectiveMargin); // preset tidak pernah terpotong di kertas sungguhan
        Assert.Equal(new Thickness(layout.Margin), layout.Padding);
        Assert.True(layout.Width - 2 * layout.EffectiveMargin > 400, "lebar isi");
        Assert.True(layout.Height - 2 * layout.EffectiveMargin > 400, "tinggi isi");
    }

    [Theory]
    [MemberData(nameof(PaperAndOrientation))]
    public void ContentArea_ShrinksAsTheMarginGrows(PaperKind paper, PrintOrientation orientation)
    {
        double Area(MarginPreset preset)
        {
            var layout = PageLayout.For(paper, orientation, preset);
            return (layout.Width - 2 * layout.EffectiveMargin) * (layout.Height - 2 * layout.EffectiveMargin);
        }

        Assert.True(Area(MarginPreset.Narrow) > Area(MarginPreset.Normal));
        Assert.True(Area(MarginPreset.Normal) > Area(MarginPreset.Wide));
        Assert.True(Area(MarginPreset.Wide) > 0);
    }

    [Fact]
    public void MarginOf_UndefinedValue_FallsBackToNormal() =>
        Assert.Equal(PageLayout.MarginOf(MarginPreset.Normal), PageLayout.MarginOf((MarginPreset)99));

    [Fact]
    public void For_UndefinedPaperAndOrientation_FallBackToA4Portrait()
    {
        var layout = PageLayout.For((PaperKind)42, (PrintOrientation)42, MarginPreset.Normal);

        Assert.Equal(PageLayout.Default, layout);
    }

    [Fact]
    public void For_IsAValueType_EqualInputsGiveEqualLayouts()
    {
        Assert.Equal(PageLayout.For(PaperKind.Letter, PrintOrientation.Landscape, MarginPreset.Wide),
            PageLayout.For(PaperKind.Letter, PrintOrientation.Landscape, MarginPreset.Wide));
        Assert.NotEqual(PageLayout.For(PaperKind.Letter, PrintOrientation.Portrait, MarginPreset.Wide),
            PageLayout.For(PaperKind.A4, PrintOrientation.Portrait, MarginPreset.Wide));
    }

    // ---- FromPrintableArea ----

    [Theory]
    [InlineData(double.NaN, double.NaN)]
    [InlineData(double.NaN, 900)]
    [InlineData(700, double.NaN)]
    [InlineData(0, 0)]
    [InlineData(0, 900)]
    [InlineData(700, 0)]
    [InlineData(-1, -1)]
    [InlineData(-700, 900)]
    [InlineData(700, -900)]
    [InlineData(double.NegativeInfinity, 900)]
    [InlineData(700, double.PositiveInfinity)]
    [InlineData(double.Epsilon, double.Epsilon)]
    [InlineData(199.99, 900)]
    [InlineData(700, 199.99)]
    [InlineData(double.MinValue, double.MinValue)]
    public void FromPrintableArea_InvalidOrTinySize_FallsBackToDefaultPageSize(double width, double height)
    {
        var layout = PageLayout.FromPrintableArea(width, height);

        Assert.Equal(PageLayout.Default.Width, layout.Width);
        Assert.Equal(PageLayout.Default.Height, layout.Height);
        Assert.True(layout.Width > 0 && layout.Height > 0);
    }

    [Theory]
    [InlineData(MarginPreset.Narrow, 48)]
    [InlineData(MarginPreset.Normal, 72)]
    [InlineData(MarginPreset.Wide, 96)]
    public void FromPrintableArea_FallbackStillHonoursTheRequestedMargin(MarginPreset preset, double expected)
    {
        Assert.Equal(expected, PageLayout.FromPrintableArea(double.NaN, 0, preset).Margin);
        Assert.Equal(expected, PageLayout.FromPrintableArea(700, 1000, preset).Margin);
    }

    [Fact]
    public void FromPrintableArea_ExactlyAtTheMinimum_IsAccepted_AndLeavesRoomForContent()
    {
        var layout = PageLayout.FromPrintableArea(200, 200, MarginPreset.Wide);

        Assert.Equal((200d, 200d), (layout.Width, layout.Height));
        Assert.Equal(50, layout.EffectiveMargin); // dibatasi seperempat sisi, bukan 96
        Assert.True(layout.Width - 2 * layout.EffectiveMargin > 0);
    }

    [Fact]
    public void FromPrintableArea_LandscapeAreaIsKeptAsReported()
    {
        var layout = PageLayout.FromPrintableArea(1000, 700);

        Assert.True(layout.Width > layout.Height);
        Assert.Equal((1000d, 700d), (layout.Width, layout.Height));
    }

    [Fact]
    public void FromPrintableArea_AbsurdlyLargeSize_FallsBackToDefault_InsteadOfFailingInApply()
    {
        WpfHost.Instance.Run(() =>
        {
            var layout = PageLayout.FromPrintableArea(1e9, 1e9);

            Assert.Null(Record.Exception(() => layout.Apply(new FlowDocument())));
        });
    }

    // ---- Apply ----

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void Apply_OnEmptyDocument_SetsSizeColumnAndPadding_WithoutThrowing(PaperKind paper, PrintOrientation orientation, MarginPreset margin)
    {
        var layout = PageLayout.For(paper, orientation, margin);
        WpfHost.Instance.Run(() =>
        {
            var document = new FlowDocument();

            Assert.Null(Record.Exception(() => layout.Apply(document)));

            Assert.Equal(layout.Width, document.PageWidth);
            Assert.Equal(layout.Height, document.PageHeight);
            Assert.Equal(layout.Width, document.ColumnWidth);
            Assert.Equal(new Thickness(layout.EffectiveMargin), document.PagePadding);
        });
    }

    [Fact]
    public void Apply_TwiceWithDifferentLayouts_LastOneWins()
    {
        WpfHost.Instance.Run(() =>
        {
            var document = new FlowDocument(new Paragraph(new Run("isi")));
            PageLayout.For(PaperKind.Letter, PrintOrientation.Landscape, MarginPreset.Wide).Apply(document);
            var second = PageLayout.For(PaperKind.A4, PrintOrientation.Portrait, MarginPreset.Narrow);

            second.Apply(document);

            Assert.Equal((second.Width, second.Height, second.Width), (document.PageWidth, document.PageHeight, document.ColumnWidth));
            Assert.Equal(new Thickness(48), document.PagePadding);
        });
    }

    [Fact]
    public void Apply_NegativeOrOversizedMargin_IsClampedAndDoesNotThrow()
    {
        WpfHost.Instance.Run(() =>
        {
            var document = new FlowDocument();

            new PageLayout(300, 400, -10).Apply(document);
            Assert.Equal(new Thickness(0), document.PagePadding);

            new PageLayout(300, 400, 10_000).Apply(document);
            Assert.Equal(new Thickness(75), document.PagePadding);
        });
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void CreatedDocument_PaginatesToExactlyTheLayoutPageSize_WithMarginAsContentBox(PaperKind paper, PrintOrientation orientation, MarginPreset margin)
    {
        var layout = PageLayout.For(paper, orientation, margin);
        WpfHost.Instance.Run(() =>
        {
            var document = PrintService.CreateDocument(PrintTestKit.Snapshot(PrintTestKit.Sample), layout);
            var inner = (DynamicDocumentPaginator)((IDocumentPaginatorSource)document).DocumentPaginator;
            inner.ComputePageCount();

            using var page = inner.GetPage(0);

            Assert.True(inner.PageCount >= 1);
            Assert.Equal(layout.Width, page.Size.Width, 1);
            Assert.Equal(layout.Height, page.Size.Height, 1);
            Assert.Equal(layout.EffectiveMargin, page.ContentBox.Left, 1);
            Assert.Equal(layout.EffectiveMargin, page.ContentBox.Top, 1);
            Assert.Equal(layout.Width - 2 * layout.EffectiveMargin, page.ContentBox.Width, 1);
        });
    }

    [Fact]
    public void WiderMargin_NeverGivesFewerPagesThanNarrower_ForTheSameText()
    {
        WpfHost.Instance.Run(() =>
        {
            int Pages(MarginPreset margin)
            {
                var layout = new PageLayout(PrintTestKit.Small.Width, PrintTestKit.Small.Height, PageLayout.MarginOf(margin) / 3);
                return PrintTestKit.Paginator(PrintTestKit.Paragraphs(30), layout: layout).PageCount;
            }

            var narrow = Pages(MarginPreset.Narrow);
            var normal = Pages(MarginPreset.Normal);
            var wide = Pages(MarginPreset.Wide);

            Assert.True(narrow <= normal && normal <= wide, $"{narrow} {normal} {wide}");
            Assert.True(wide > narrow, $"{narrow} vs {wide}");
        });
    }
}
