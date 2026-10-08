using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using MdViewer.Tests.Support;
using static MdViewer.Tests.Support.PrintTestKit;

namespace MdViewer.Tests;

/// <summary>Kontrol pada jendela Pratinjau Cetak: pengaturan halaman, navigasi, zoom, tutup, snapshot. Tanpa printer/PrintDialog.</summary>
[Collection("Wpf")]
public class PrintPreviewWindowBehaviorTests : IDisposable
{
    readonly List<(PrintPreviewWindow Window, bool[] Closed)> windows = [];
    readonly TempDir dir = new();

    public void Dispose()
    {
        WpfHost.Instance.Run(() =>
        {
            foreach (var (window, closed) in windows)
                if (!closed[0]) window.Close();
        });
        dir.Dispose();
    }

    static void Sta(Action action) => WpfHost.Instance.Run(action);

    PrintPreviewWindow Open(string text, bool show = false, string title = "contoh.md")
    {
        var window = new PrintPreviewWindow(Snapshot(text, title));
        Track(window);
        if (show) window.Show();
        return window;
    }

    bool[] Track(PrintPreviewWindow window)
    {
        var closed = new[] { false };
        window.Closed += (_, _) => closed[0] = true;
        windows.Add((window, closed));
        return closed;
    }

    static T Part<T>(PrintPreviewWindow window, string name) where T : class =>
        (T)window.FindName(name) ?? throw new InvalidOperationException(name);

    static void WaitReady(PrintPreviewWindow window)
    {
        Assert.True(UiPump.Until(() => !window.IsBusy, Patience), "penyusunan pratinjau tidak selesai");
        Assert.False(window.HasFailed);
    }

    // Cukup sampai paginasi selesai (jumlah halaman final diketahui): untuk test yang hanya memeriksa layout/jumlah halaman,
    // tanpa menunggu halaman XPS ditulis dan dimuat viewer.
    static void WaitPaginated(PrintPreviewWindow window)
    {
        Assert.True(UiPump.Until(() => window.Stage is not (null or PreviewStage.Paginating), Patience), "paginasi pratinjau tidak selesai");
        Assert.False(window.HasFailed);
    }

    // Siap = halaman terpasang di viewer dan zoom "muat" (yang baru diterapkan sesudah tata letak) sudah berlaku. Tanpa ini,
    // GoToPage yang dipanggil lebih dulu tergeser oleh perubahan zoom berikutnya.
    static void WaitShown(PrintPreviewWindow window)
    {
        WaitReady(window);
        var viewer = Part<DocumentViewer>(window, "Viewer");
        Assert.True(UiPump.Until(() => viewer.Document is not null && viewer.PageCount == window.PageCount && Math.Abs(viewer.Zoom - 100) > 0.5, Patience),
            $"halaman belum tampil (zoom {viewer.Zoom})");
        UiPump.For(TimeSpan.FromMilliseconds(60));
    }

    static void Click(PrintPreviewWindow window, string buttonName) =>
        Part<ButtonBase>(window, buttonName).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    static void Press(UIElement target, Key key, bool preview = true)
    {
        var source = PresentationSource.FromVisual(target) ?? throw new InvalidOperationException("elemen belum tampil");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = preview ? Keyboard.PreviewKeyDownEvent : Keyboard.KeyDownEvent,
        };
        target.RaiseEvent(args);
    }

    // Seperti pengguna: kotak berfokus saat mengetik (UpdateNavigation tidak menulis kotak yang sedang berfokus).
    // Jendela yang belum tampil (Open tanpa show) tidak bisa memberi fokus keyboard; pemanggil yang mengandalkannya memakai jendela tampil.
    static void TypeInPageBox(PrintPreviewWindow window, string text)
    {
        var box = Part<TextBox>(window, "PageBox");
        if (box.IsEnabled && window.IsVisible)
        {
            box.Focus();
            Assert.True(box.IsKeyboardFocused, "kotak halaman tidak mendapat fokus keyboard");
        }
        box.Text = text;
        Press(box, Key.Enter);
    }

    static bool Enabled(PrintPreviewWindow window, string name) => Part<UIElement>(window, name).IsEnabled;

    // ---- Pengaturan halaman membangun ulang siklus ----

    [Fact]
    public void ChangingPaper_StartsANewCycle_WithThePaperDimensions_AndBack()
    {
        Sta(() =>
        {
            var window = Open(Pages(20));
            WaitPaginated(window);
            var a4Document = window.PrintedDocument;
            var a4Pages = window.PageCount;
            Assert.Equal(PageLayout.Default.Width, a4Document.PageWidth, 1);

            Part<RadioButton>(window, "LetterButton").IsChecked = true;

            Assert.True(window.IsBusy);
            Assert.Equal(0, window.PageCount); // jumlah halaman lama tidak dipakai lagi selama menyusun
            WaitPaginated(window);
            Assert.NotSame(a4Document, window.PrintedDocument);
            Assert.Equal((816d, 1056d), (window.PrintedDocument.PageWidth, window.PrintedDocument.PageHeight));
            Assert.Equal(PaperKind.Letter, PaperOf(window));
            Assert.True(window.PageCount > 0);

            Part<RadioButton>(window, "A4Button").IsChecked = true;
            WaitPaginated(window);

            Assert.Equal(PageLayout.Default.Width, window.PrintedDocument.PageWidth, 1);
            Assert.Equal(a4Pages, window.PageCount);
        });
    }

    static PaperKind PaperOf(PrintPreviewWindow window) =>
        Math.Abs(window.Layout.Width - 816) < 1 || Math.Abs(window.Layout.Height - 816) < 1 ? PaperKind.Letter : PaperKind.A4;

    [Fact]
    public void ChangingMargin_RebuildsWithThePresetPadding_AndWiderMarginGivesMorePages()
    {
        Sta(() =>
        {
            var window = Open(Pages(90));
            WaitPaginated(window);
            var normal = window.PageCount;

            Part<RadioButton>(window, "NarrowMarginButton").IsChecked = true;
            WaitPaginated(window);
            var narrow = window.PageCount;
            Assert.Equal(new Thickness(48), window.PrintedDocument.PagePadding);

            Part<RadioButton>(window, "WideMarginButton").IsChecked = true;
            WaitPaginated(window);
            var wide = window.PageCount;
            Assert.Equal(new Thickness(96), window.PrintedDocument.PagePadding);

            Assert.True(narrow <= normal && normal <= wide && narrow < wide, $"sempit {narrow}, normal {normal}, lebar {wide}");
            Assert.Equal(MarginPreset.Wide, MarginOf(window));
        });
    }

    static MarginPreset MarginOf(PrintPreviewWindow window) =>
        window.Layout.Margin switch { 48 => MarginPreset.Narrow, 96 => MarginPreset.Wide, _ => MarginPreset.Normal };

    [Fact]
    public void ChangingOrientation_BackToPortrait_RestoresTheOriginalPageCount()
    {
        Sta(() =>
        {
            var window = Open(Pages(16));
            WaitPaginated(window);
            var portrait = window.PageCount;

            Part<RadioButton>(window, "LandscapeButton").IsChecked = true;
            WaitPaginated(window);
            Assert.True(window.Layout.Width > window.Layout.Height);
            Assert.True(window.PageCount > portrait);

            Part<RadioButton>(window, "PortraitButton").IsChecked = true;
            WaitPaginated(window);

            Assert.True(window.Layout.Height > window.Layout.Width);
            Assert.Equal(portrait, window.PageCount);
        });
    }

    [Fact]
    public void FooterToggle_StartsANewCycle_ButDoesNotChangePagination()
    {
        Sta(() =>
        {
            var window = Open(Pages(20));
            WaitPaginated(window);
            var before = window.PrintedDocument;
            var pages = window.PageCount;
            var layout = window.Layout;

            Part<CheckBox>(window, "FooterCheck").IsChecked = false;

            Assert.True(window.IsBusy);
            WaitPaginated(window);
            Assert.NotSame(before, window.PrintedDocument);
            Assert.Equal(pages, window.PageCount);
            Assert.Equal(layout, window.Layout);

            Part<CheckBox>(window, "FooterCheck").IsChecked = true;
            WaitPaginated(window);
            Assert.Equal(pages, window.PageCount);
        });
    }

    [Fact]
    public void ChoosingTheAlreadySelectedOption_DoesNotRebuild()
    {
        Sta(() =>
        {
            var window = Open(Pages(10));
            WaitReady(window);
            var document = window.PrintedDocument;

            // Dipicu ulang event Checked dengan nilai yang sama (mis. klik pada yang sudah terpilih).
            Part<RadioButton>(window, "PortraitButton").RaiseEvent(new RoutedEventArgs(ToggleButton.CheckedEvent));
            Part<RadioButton>(window, "A4Button").RaiseEvent(new RoutedEventArgs(ToggleButton.CheckedEvent));
            Part<RadioButton>(window, "NormalMarginButton").RaiseEvent(new RoutedEventArgs(ToggleButton.CheckedEvent));

            Assert.False(window.IsBusy);
            Assert.Same(document, window.PrintedDocument);
        });
    }

    [Fact]
    public void RapidChanges_WithoutWaiting_EndWithOnlyTheLastLayout_AndNoFailure()
    {
        Sta(() =>
        {
            var window = Open(Pages(40), show: true);

            Part<RadioButton>(window, "LandscapeButton").IsChecked = true;
            Part<RadioButton>(window, "LetterButton").IsChecked = true;
            Part<RadioButton>(window, "WideMarginButton").IsChecked = true;
            Part<CheckBox>(window, "FooterCheck").IsChecked = false;
            Part<RadioButton>(window, "NarrowMarginButton").IsChecked = true;
            Part<RadioButton>(window, "PortraitButton").IsChecked = true;

            WaitShown(window);

            Assert.Equal(PageLayout.For(PaperKind.Letter, PrintOrientation.Portrait, MarginPreset.Narrow), window.Layout);
            Assert.Equal(window.Layout.Width, window.PrintedDocument.PageWidth);
            Assert.Equal(new Thickness(48), window.PrintedDocument.PagePadding);
            Assert.False(window.HasFailed);
            UiPump.For(TimeSpan.FromMilliseconds(200)); // sisa callback siklus lama (sudah dibuang) tidak boleh melempar
            Assert.False(window.IsBusy);
        });
    }

    [Fact]
    public void ChangingLayout_ShowsAnEmptyViewerWhileBusy_ThenTheNewPages()
    {
        Sta(() =>
        {
            var window = Open(Pages(20), show: true);
            WaitShown(window);
            var viewer = Part<DocumentViewer>(window, "Viewer");
            var firstPages = viewer.Document;
            Assert.NotNull(firstPages);

            Part<RadioButton>(window, "LandscapeButton").IsChecked = true;

            Assert.Null(viewer.Document); // halaman lama (paket yang sudah dibuang) tidak boleh tetap terpasang
            Assert.Equal(Visibility.Visible, Part<Border>(window, "BusyPanel").Visibility);
            Assert.Equal("Menyusun halaman...", Part<TextBlock>(window, "BusyText").Text);
            WaitShown(window);
            Assert.NotSame(firstPages, viewer.Document);
            Assert.Equal(Visibility.Collapsed, Part<Border>(window, "BusyPanel").Visibility);
        });
    }

    // ---- Navigasi ----

    [Fact]
    public void Navigation_StartsAtPageOne_WithBackwardButtonsDisabled()
    {
        Sta(() =>
        {
            var window = Open(Pages(30), show: true);
            WaitShown(window);

            Assert.True(window.PageCount >= 3);
            Assert.Equal("1", Part<TextBox>(window, "PageBox").Text);
            Assert.Equal($"dari {window.PageCount}", Part<TextBlock>(window, "PageCountText").Text);
            Assert.False(Enabled(window, "FirstPageButton"));
            Assert.False(Enabled(window, "PreviousPageButton"));
            Assert.True(Enabled(window, "NextPageButton"));
            Assert.True(Enabled(window, "LastPageButton"));
        });
    }

    [Fact]
    public void Navigation_FirstPreviousNextLast_StayWithinBounds()
    {
        Sta(() =>
        {
            var window = Open(Pages(30), show: true);
            WaitShown(window);
            var viewer = Part<DocumentViewer>(window, "Viewer");
            var last = window.PageCount;
            var pageBox = Part<TextBox>(window, "PageBox");

            // "Sebelumnya"/"Awal" di halaman 1: tetap di 1 dan tidak melempar.
            Click(window, "PreviousPageButton");
            Click(window, "FirstPageButton");
            UiPump.For(TimeSpan.FromMilliseconds(50));
            Assert.Equal(1, viewer.MasterPageNumber);

            Click(window, "NextPageButton");
            Assert.True(UiPump.Until(() => pageBox.Text == "2", Patience), pageBox.Text);
            Assert.True(Enabled(window, "FirstPageButton") && Enabled(window, "PreviousPageButton"));

            // Diulang: perubahan zoom/tata letak yang tersisa dari pemasangan halaman bisa menggeser posisi sesudah klik pertama.
            Assert.True(UiPump.Until(() => { Click(window, "LastPageButton"); return pageBox.Text == last.ToString(); }, Patience), pageBox.Text);
            Assert.False(Enabled(window, "NextPageButton"));
            Assert.False(Enabled(window, "LastPageButton"));
            Assert.True(Enabled(window, "PreviousPageButton"));
            Assert.Equal($"Halaman {last} dari {last}", window.PageStatusText);

            // "Berikutnya"/"Akhir" di halaman terakhir: tetap di sana.
            Click(window, "NextPageButton");
            Click(window, "LastPageButton");
            UiPump.For(TimeSpan.FromMilliseconds(50));
            Assert.Equal(last.ToString(), pageBox.Text);

            Click(window, "FirstPageButton");
            Assert.True(UiPump.Until(() => pageBox.Text == "1", Patience), pageBox.Text);
            Assert.False(Enabled(window, "FirstPageButton"));
        });
    }

    [Fact]
    public void Navigation_PreviousFromTheLastPage_GoesToTheSecondToLastPage()
    {
        Sta(() =>
        {
            var window = Open(Pages(30), show: true);
            WaitShown(window);
            var last = window.PageCount;
            var pageBox = Part<TextBox>(window, "PageBox");
            Assert.True(UiPump.Until(() => { Click(window, "LastPageButton"); return pageBox.Text == last.ToString(); }, Patience), pageBox.Text);

            Click(window, "PreviousPageButton");

            Assert.True(UiPump.Until(() => pageBox.Text == (last - 1).ToString(), TimeSpan.FromSeconds(5)), $"kotak halaman: {pageBox.Text}, seharusnya {last - 1}");
        });
    }

    // Zoom 25%: beberapa halaman sebaris. Halaman tujuan sudah tampak tanpa menggulir, jadi tombol harus tetap memajukan nomor halaman.
    static void ZoomOutTo25(PrintPreviewWindow window)
    {
        var viewer = Part<DocumentViewer>(window, "Viewer");
        for (var i = 0; i < 10 && viewer.Zoom > 25; i++) Click(window, "ZoomOutButton");
        Assert.Equal(25, viewer.Zoom);
        UiPump.For(TimeSpan.FromMilliseconds(80));
    }

    // 22 paragraf = 3 halaman: seluruhnya hampir muat di viewport sehingga gulir mentok sebelum halaman tujuan sampai ke puncak.
    // 60 paragraf = 8 halaman: sebagian halaman tampak sekaligus, tetapi gulir tetap panjang.
    [Theory]
    [InlineData(22)]
    [InlineData(60)]
    public void Navigation_NextAndPrevious_MoveExactlyOnePage_WhenSeveralPagesAreVisible_AtSmallZoom(int paragraphs)
    {
        Sta(() =>
        {
            var window = Open(Pages(paragraphs), show: true);
            WaitShown(window);
            var pageBox = Part<TextBox>(window, "PageBox");
            var viewer = Part<DocumentViewer>(window, "Viewer");
            ZoomOutTo25(window);
            var last = window.PageCount;
            Assert.True(last >= 3, $"{last} halaman");

            for (var expected = 2; expected <= last; expected++)
            {
                Click(window, "NextPageButton");
                Assert.True(UiPump.Until(() => pageBox.Text == expected.ToString(), TimeSpan.FromSeconds(3)),
                    $"Berikutnya: kotak '{pageBox.Text}', diharapkan {expected} (MasterPageNumber {viewer.MasterPageNumber}, dari {last})");
                UiPump.For(TimeSpan.FromMilliseconds(40)); // posisi gulir hasil navigasi sudah mapan; nomor tidak boleh berubah sendiri
                Assert.Equal(expected.ToString(), pageBox.Text);
            }
            Assert.False(Enabled(window, "NextPageButton"));

            for (var expected = last - 1; expected >= 1; expected--)
            {
                Click(window, "PreviousPageButton");
                Assert.True(UiPump.Until(() => pageBox.Text == expected.ToString(), TimeSpan.FromSeconds(3)),
                    $"Sebelumnya: kotak '{pageBox.Text}', diharapkan {expected} (MasterPageNumber {viewer.MasterPageNumber}, dari {last})");
                UiPump.For(TimeSpan.FromMilliseconds(40));
                Assert.Equal(expected.ToString(), pageBox.Text);
            }
            Assert.False(Enabled(window, "PreviousPageButton"));
        });
    }

    [Fact]
    public void PageBox_NumberInRange_GoesThere()
    {
        Sta(() =>
        {
            var window = Open(Pages(30), show: true);
            WaitShown(window);
            var pageBox = Part<TextBox>(window, "PageBox");

            TypeInPageBox(window, "2");

            Assert.True(UiPump.Until(() => pageBox.Text == "2" && Part<DocumentViewer>(window, "Viewer").MasterPageNumber == 2, Patience), pageBox.Text);
            Assert.Equal($"Halaman 2 dari {window.PageCount}", window.PageStatusText);
        });
    }

    [Theory]
    [InlineData("3", "3")]
    [InlineData("999", "999")] // diklem ke halaman terakhir (nilai sebenarnya diisi test)
    [InlineData("0", "1")]
    public void PageBox_AfterEnter_ShowsTheClampedTargetAtOnce_EvenThoughTheBoxStaysFocused(string typed, string expectedOrClamp)
    {
        Sta(() =>
        {
            var window = Open(Pages(30), show: true);
            WaitShown(window);
            var pageBox = Part<TextBox>(window, "PageBox");
            var expected = typed == "999" ? window.PageCount.ToString() : expectedOrClamp;

            TypeInPageBox(window, typed);

            Assert.True(pageBox.IsKeyboardFocused);
            Assert.Equal(expected, pageBox.Text); // tanpa memompa dispatcher: bukan nomor lama sebelum tata letak
            UiPump.For(TimeSpan.FromMilliseconds(80));
            Assert.Equal(expected, pageBox.Text);
            Assert.Equal($"Halaman {expected} dari {window.PageCount}", window.PageStatusText);
        });
    }

    // Satu jendela untuk semua masukan (membuka jendela + memuat halaman per masukan membuat suite lambat); pesan gagal memuat masukannya.
    [Fact]
    public void PageBox_OutOfRangeNumber_IsClampedToTheFirstOrLastPage()
    {
        Sta(() =>
        {
            var window = Open(Pages(30), show: true);
            WaitShown(window);
            var pageBox = Part<TextBox>(window, "PageBox");
            var last = window.PageCount.ToString();

            foreach (var (input, expected) in new[]
                     {
                         ("999999", last), ("2147483647", last), ("0", "1"), ("-5", "1"), ("-2147483648", "1"), ("  3  ", "3"), ("+2", "2"),
                     })
            {
                TypeInPageBox(window, input);

                Assert.True(UiPump.Until(() => pageBox.Text == expected, Patience), $"'{input}' -> '{pageBox.Text}', diharapkan {expected}");
            }
        });
    }

    [Fact]
    public void PageBox_NonNumericOrEmpty_KeepsTheCurrentPage_AndRestoresTheBox()
    {
        Sta(() =>
        {
            var window = Open(Pages(30), show: true);
            WaitShown(window);
            var viewer = Part<DocumentViewer>(window, "Viewer");
            var pageBox = Part<TextBox>(window, "PageBox");
            TypeInPageBox(window, "2");
            Assert.True(UiPump.Until(() => viewer.MasterPageNumber == 2, Patience));

            foreach (var input in new[] { "", "   ", "abc", "2abc", "1.5", "1,5", "99999999999999999999", "١٢", "∞", "NaN" })
            {
                TypeInPageBox(window, input);
                UiPump.For(TimeSpan.FromMilliseconds(20));

                Assert.True(pageBox.Text == "2", $"'{input}': kotak berisi '{pageBox.Text}'"); // teks liar dikembalikan ke nomor halaman sebenarnya
                Assert.True(viewer.MasterPageNumber == 2, $"'{input}': pindah ke halaman {viewer.MasterPageNumber}");
            }
        });
    }

    [Fact]
    public void PageBox_OtherKeys_AreNotConsumed_AndDoNotNavigate()
    {
        Sta(() =>
        {
            var window = Open(Pages(30), show: true);
            WaitShown(window);
            var pageBox = Part<TextBox>(window, "PageBox");
            pageBox.Text = "3";

            Press(pageBox, Key.D5);
            Press(pageBox, Key.Tab);
            UiPump.For(TimeSpan.FromMilliseconds(30));

            Assert.Equal(1, Part<DocumentViewer>(window, "Viewer").MasterPageNumber);
        });
    }

    [Fact]
    public void Navigation_WhileBusy_IsDisabledAndHarmless()
    {
        Sta(() =>
        {
            var window = Open(Pages(30), show: true);

            Assert.True(window.IsBusy);
            Assert.False(Enabled(window, "PageBox"));
            Assert.False(Enabled(window, "NextPageButton"));
            Assert.False(Enabled(window, "ZoomInButton"));
            Assert.Equal("", window.PageStatusText);

            Assert.Null(Record.Exception(() =>
            {
                Click(window, "NextPageButton");
                Click(window, "LastPageButton");
                Click(window, "ZoomInButton");
                TypeInPageBox(window, "3");
            }));
            WaitShown(window);
        });
    }

    [Fact]
    public void SinglePageDocument_DisablesAllPageButtons_AndPageBoxClampsToOne()
    {
        Sta(() =>
        {
            var window = Open(Sample, show: true);
            WaitShown(window);

            Assert.Equal(1, window.PageCount);
            Assert.False(Enabled(window, "FirstPageButton") || Enabled(window, "PreviousPageButton"));
            Assert.False(Enabled(window, "NextPageButton") || Enabled(window, "LastPageButton"));
            TypeInPageBox(window, "7");
            Assert.Equal("1", Part<TextBox>(window, "PageBox").Text);
            Assert.Equal("Halaman 1 dari 1", window.PageStatusText);
        });
    }

    // ---- Zoom ----

    [Fact]
    public void Zoom_InAndOut_StopAtTheLimits_AndSwitchToCustomMode()
    {
        Sta(() =>
        {
            var window = Open(Pages(20), show: true);
            WaitShown(window);
            var viewer = Part<DocumentViewer>(window, "Viewer");
            var text = Part<TextBlock>(window, "ZoomText");
            Assert.True(Part<RadioButton>(window, "FitPageButton").IsChecked);

            var previous = -1d;
            for (var i = 0; i < 15 && viewer.Zoom != previous; i++)
            {
                previous = viewer.Zoom;
                Click(window, "ZoomInButton");
            }
            Assert.Equal(400, viewer.Zoom);
            Assert.Equal("400%", text.Text);
            Assert.False(Part<RadioButton>(window, "FitPageButton").IsChecked); // zoom manual melepas mode "muat"
            Click(window, "ZoomInButton");
            Assert.Equal(400, viewer.Zoom);

            previous = -1;
            for (var i = 0; i < 15 && viewer.Zoom != previous; i++)
            {
                previous = viewer.Zoom;
                Click(window, "ZoomOutButton");
            }
            Assert.Equal(25, viewer.Zoom);
            Assert.Equal("25%", text.Text);
            Click(window, "ZoomOutButton");
            Assert.Equal(25, viewer.Zoom);
        });
    }

    [Fact]
    public void Zoom_StepsFollowTheFixedLadder()
    {
        Sta(() =>
        {
            var window = Open(Pages(10), show: true);
            WaitShown(window);
            var viewer = Part<DocumentViewer>(window, "Viewer");
            Part<RadioButton>(window, "ActualSizeButton").IsChecked = true;
            Assert.Equal(100, viewer.Zoom);

            var up = new List<double>();
            for (var i = 0; i < 4; i++) { Click(window, "ZoomInButton"); up.Add(viewer.Zoom); }
            var down = new List<double>();
            for (var i = 0; i < 4; i++) { Click(window, "ZoomOutButton"); down.Add(viewer.Zoom); }

            Assert.Equal([125d, 150d, 200d, 300d], up);
            Assert.Equal([200d, 150d, 125d, 100d], down);
        });
    }

    [Fact]
    public void ZoomModes_ActualIs100_FitPageIsNoLargerThanFitWidth_BothWithinLimits()
    {
        Sta(() =>
        {
            var window = Open(Pages(20), show: true);
            WaitShown(window);
            var viewer = Part<DocumentViewer>(window, "Viewer");

            Part<RadioButton>(window, "ActualSizeButton").IsChecked = true;
            Assert.Equal(100, viewer.Zoom);

            Part<RadioButton>(window, "FitWidthButton").IsChecked = true;
            var fitWidth = viewer.Zoom;
            Part<RadioButton>(window, "FitPageButton").IsChecked = true;
            var fitPage = viewer.Zoom;

            Assert.InRange(fitPage, 10, 400);
            Assert.InRange(fitWidth, 10, 400);
            Assert.True(fitPage <= fitWidth, $"satu halaman {fitPage}% vs lebar halaman {fitWidth}%");
            Assert.Equal($"{fitPage:0}%", Part<TextBlock>(window, "ZoomText").Text);
        });
    }

    [Fact]
    public void FitModes_ReapplyWhenTheWindowIsResized()
    {
        Sta(() =>
        {
            var window = Open(Pages(20), show: true);
            WaitShown(window);
            var viewer = Part<DocumentViewer>(window, "Viewer");
            Part<RadioButton>(window, "FitPageButton").IsChecked = true;
            var large = viewer.Zoom;

            window.Width = 760;
            window.Height = 560;

            Assert.True(UiPump.Until(() => viewer.Zoom < large, Patience), $"zoom tetap {viewer.Zoom}% (awal {large}%)");
        });
    }

    [Fact]
    public void Zoom_OnSmallViewer_NeverDropsBelowTheFloor()
    {
        Sta(() =>
        {
            var window = Open(Pages(10), show: true);
            WaitShown(window);
            var viewer = Part<DocumentViewer>(window, "Viewer");

            window.MinWidth = window.MinHeight = 0;
            window.Width = 300;
            window.Height = 250;
            UiPump.For(TimeSpan.FromMilliseconds(100));
            Part<RadioButton>(window, "FitWidthButton").IsChecked = false;
            Part<RadioButton>(window, "FitPageButton").IsChecked = false;
            Part<RadioButton>(window, "FitPageButton").IsChecked = true;
            UiPump.For(TimeSpan.FromMilliseconds(50));

            Assert.InRange(viewer.Zoom, 10, 400);
        });
    }

    // ---- Tutup / Esc ----

    [Fact]
    public void Escape_ClosesTheWindow_FromAnyFocusedControl()
    {
        Sta(() =>
        {
            var window = Open(Pages(10), show: true);
            WaitShown(window);
            var closed = windows.Single(w => w.Window == window).Closed;

            Press(Part<TextBox>(window, "PageBox"), Key.Escape);

            Assert.True(closed[0]);
        });
    }

    [Fact]
    public void Escape_WhileStillBusy_ClosesWithoutThrowing()
    {
        Sta(() =>
        {
            var window = Open(Pages(200), show: true);
            Assert.True(window.IsBusy);
            var closed = windows.Single(w => w.Window == window).Closed;

            Press(Part<TextBox>(window, "PageBox"), Key.Escape);
            UiPump.For(TimeSpan.FromMilliseconds(400));

            Assert.True(closed[0]);
            Assert.False(window.IsBusy);
            Assert.Equal(0, window.PageCount);
        });
    }

    [Fact]
    public void CloseButton_ClosesTheWindow()
    {
        Sta(() =>
        {
            var window = Open(Pages(10), show: true);
            var closed = windows.Single(w => w.Window == window).Closed;

            Click(window, "CloseButton");

            Assert.True(closed[0]);
        });
    }

    [Fact]
    public void OtherKeys_DoNotCloseTheWindow()
    {
        Sta(() =>
        {
            var window = Open(Pages(10), show: true);
            WaitShown(window);
            var closed = windows.Single(w => w.Window == window).Closed;

            Press(Part<TextBox>(window, "PageBox"), Key.Enter);
            Press(Part<TextBox>(window, "PageBox"), Key.Back);
            Press(Part<TextBox>(window, "PageBox"), Key.P);

            Assert.False(closed[0]);
        });
    }

    [Theory]
    [InlineData(0)]    // segera setelah dibuat: masih memaginasi
    [InlineData(1)]    // sedang menulis halaman XPS
    [InlineData(2)]    // sudah siap
    public void ClosingAtAnyStage_DoesNotThrow_AndStopsBuilding(int stage)
    {
        Sta(() =>
        {
            var window = Open(Pages(stage == 0 ? 400 : 80), show: true);
            var closed = windows.Single(w => w.Window == window).Closed;
            if (stage == 1) Assert.True(UiPump.Until(() => window.PageCount > 0 || !window.IsBusy, Patience));
            if (stage == 2) WaitShown(window);

            Assert.Null(Record.Exception(window.Close));
            UiPump.For(TimeSpan.FromMilliseconds(300)); // callback tertunda tak boleh melempar ke dispatcher

            Assert.True(closed[0]);
            Assert.False(window.IsBusy);
            Assert.False(window.HasFailed);
            Assert.Equal(0, window.PageCount);
        });
    }

    [Fact]
    public void ClosingRightAfterThePagesAppear_LeavesNoUnhandledDispatcherException()
    {
        var before = WpfHost.Unhandled.Count;
        Sta(() =>
        {
            for (var i = 0; i < 4; i++)
            {
                var window = new PrintPreviewWindow(Snapshot(Pages(30)));
                window.Show();
                Assert.True(UiPump.Until(() => !window.IsBusy, Patience));
                UiPump.For(TimeSpan.FromMilliseconds(15 * i));
                window.Close();
                UiPump.For(TimeSpan.FromMilliseconds(120));
            }
        });

        Assert.Equal(before, WpfHost.Unhandled.Count);
    }

    [Fact]
    public void ClosedWindow_IsGarbageCollected_NoHandlersKeepItAlive()
    {
        var reference = Sta(() =>
        {
            var window = new PrintPreviewWindow(Snapshot(Pages(20)));
            window.Show();
            WaitShown(window);
            Click(window, "NextPageButton");
            Click(window, "ZoomInButton");
            window.Close();
            UiPump.For(TimeSpan.FromMilliseconds(100));
            return new WeakReference(window);
        });

        // UiPump memakai dispatcher thread pemanggilnya: harus di thread STA host, bukan thread MTA xunit (yang akan membuat dispatcher baru).
        Sta(() => Assert.True(UiPump.Until(() =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return !reference.IsAlive;
        }, TimeSpan.FromSeconds(10)), "jendela pratinjau yang sudah ditutup masih direferensikan (kebocoran penangan DependencyPropertyDescriptor/event?)"));
    }

    static T Sta<T>(Func<T> func) => WpfHost.Instance.Run(func);

    // ---- Dibuka berulang ----

    [Fact]
    public void OpeningTwiceInARow_GivesTheSameResultBothTimes()
    {
        Sta(() =>
        {
            var counts = new List<int>();
            for (var i = 0; i < 2; i++)
            {
                var window = new PrintPreviewWindow(Snapshot(Pages(20)));
                var closed = Track(window);
                window.Show();
                WaitShown(window);
                counts.Add(window.PageCount);
                window.Close();
                Assert.True(closed[0]);
            }

            Assert.Equal(counts[0], counts[1]);
            Assert.True(counts[0] > 1);
        });
    }

    [Fact]
    public void TwoWindowsOpenAtTheSameTime_BuildIndependently()
    {
        Sta(() =>
        {
            var small = Open(Sample, show: true);
            var big = Open(Pages(30), show: true);
            var bigDocumentEarly = big.IsBusy;

            WaitShown(small);
            WaitShown(big);

            Assert.True(bigDocumentEarly);
            Assert.Equal(1, small.PageCount);
            Assert.True(big.PageCount > 2);
            Assert.Contains("Paragraf nomor 30", UiPump.TextOf(big.PrintedDocument));
            Assert.DoesNotContain("Paragraf nomor", UiPump.TextOf(small.PrintedDocument));

            Click(big, "NextPageButton");
            Part<RadioButton>(small, "LandscapeButton").IsChecked = true;
            WaitShown(small);
            Assert.True(small.Layout.Width > small.Layout.Height);
            Assert.True(big.Layout.Height > big.Layout.Width); // pengaturan satu jendela tidak merembes ke yang lain
        });
    }

    // ---- Tombol Cetak / Ctrl+P (tanpa membuka PrintDialog) ----

    [Fact]
    public void PrintButtonAndCommand_AreDisabledWhileBusy_EnabledWhenReady()
    {
        Sta(() =>
        {
            var window = Open(Pages(20), show: true);
            var viewer = Part<DocumentViewer>(window, "Viewer");

            Assert.True(window.IsBusy);
            Assert.False(Enabled(window, "PrintButton"));
            Assert.False(ApplicationCommands.Print.CanExecute(null, viewer));

            WaitShown(window);

            Assert.True(Enabled(window, "PrintButton"));
            Assert.True(ApplicationCommands.Print.CanExecute(null, viewer));

            Part<RadioButton>(window, "LetterButton").IsChecked = true;
            Assert.False(Enabled(window, "PrintButton")); // layout berubah: tidak boleh mencetak paket yang lama
            Assert.False(ApplicationCommands.Print.CanExecute(null, viewer));
            WaitShown(window);
            Assert.True(Enabled(window, "PrintButton"));
        });
    }

    [Fact]
    public void PrintCommand_ExecutedOnTheViewer_GoesThroughTheWindowsPrintPath_NotTheBuiltInPrinting()
    {
        Sta(() =>
        {
            var window = Open(Pages(10), show: true);
            var viewer = Part<DocumentViewer>(window, "Viewer");
            var dialogs = 0;
            window.ShowPrintDialogForTests = () => { dialogs++; return false; }; // "Batal": tidak ada yang dicetak
            WaitShown(window);

            // Pengikatan tingkat-instans mendahului pengikatan kelas DocumentViewer (yang akan membuka PrintDialog bawaan).
            // Diperiksa lebih dulu: bila hilang, Execute di bawah akan membuka dialog sungguhan dan menggantung test.
            Assert.Contains(viewer.CommandBindings.Cast<CommandBinding>(), b => b.Command == ApplicationCommands.Print);
            Assert.Contains(window.CommandBindings.Cast<CommandBinding>(), b => b.Command == ApplicationCommands.Print);

            ApplicationCommands.Print.Execute(null, viewer); // Ctrl+P saat fokus di halaman
            Assert.Equal(1, dialogs);

            ApplicationCommands.Print.Execute(null, Part<TextBox>(window, "PageBox")); // Ctrl+P dari kontrol lain di jendela
            Assert.Equal(2, dialogs);

            Click(window, "PrintButton");
            Assert.Equal(3, dialogs);

            // Dibatalkan: jendela kembali normal dan masih bisa mencetak.
            Assert.Equal(Visibility.Collapsed, Part<Border>(window, "BusyPanel").Visibility);
            Assert.True(Enabled(window, "PrintButton"));
            Assert.True(ApplicationCommands.Print.CanExecute(null, viewer));
        });
    }

    [Fact]
    public void PrintCommand_ExecutedWhileBusy_DoesNotOpenTheDialog()
    {
        Sta(() =>
        {
            var window = Open(Pages(200), show: true);
            var viewer = Part<DocumentViewer>(window, "Viewer");
            var dialogs = 0;
            window.ShowPrintDialogForTests = () => { dialogs++; return false; };
            Assert.True(window.IsBusy);
            Assert.Contains(viewer.CommandBindings.Cast<CommandBinding>(), b => b.Command == ApplicationCommands.Print);

            Assert.False(ApplicationCommands.Print.CanExecute(null, viewer));
            ApplicationCommands.Print.Execute(null, viewer);
            Click(window, "PrintButton");

            Assert.Equal(0, dialogs);
        });
    }

    [Fact]
    public void PrintCommand_WhenNotReady_ExecutesAsNoOp_WithoutOpeningAnyDialog()
    {
        Sta(() =>
        {
            var window = Open(Pages(200), show: true);
            Assert.True(window.IsBusy);

            // CanPrint salah: Execute langsung (mis. dari pintasan yang tak ter-disable) tidak boleh membuka PrintDialog.
            Assert.Null(Record.Exception(() => Click(window, "PrintButton")));
            Assert.True(window.IsBusy || window.PageCount > 0);
        });
    }

    // ---- Galat dari callback dispatcher / pembatalan ----

    [Fact]
    public void ChangingLayoutManyTimes_WhileRendering_ThenClosing_LeavesNoDispatcherError()
    {
        var before = WpfHost.Unhandled.Count;
        Sta(() =>
        {
            var window = Open(Pages(60), show: true);
            var closed = windows.Single(w => w.Window == window).Closed;

            // Tiap pergantian dilakukan begitu siklus berjalan mulai menulis halaman XPS (atau sudah siap): penulis yang
            // dibatalkan dan viewer yang baru memuat halaman harus aman dibuang.
            foreach (var button in new[] { "LandscapeButton", "LetterButton", "WideMarginButton", "PortraitButton", "A4Button", "NarrowMarginButton" })
            {
                Assert.True(UiPump.Until(() => window.Stage is PreviewStage.Rendering or PreviewStage.Ready, Patience));
                Part<RadioButton>(window, button).IsChecked = true;
            }

            Assert.True(UiPump.Until(() => window.Stage is PreviewStage.Rendering or PreviewStage.Ready, Patience));
            window.Close();
            UiPump.For(TimeSpan.FromMilliseconds(500)); // sisa callback penulis yang dibatalkan / pembersihan paket

            Assert.True(closed[0]);
            Assert.False(window.HasFailed);
        });
        Assert.Equal(before, WpfHost.Unhandled.Count);
    }

    [Fact]
    public void PendingParse_WindowShowsBusyAndNoDocument_UntilTheBackgroundParseFinishes()
    {
        Sta(() =>
        {
            var snapshot = Snapshot(Pages(20));
            var gate = new TaskCompletionSource<Markdig.Syntax.MarkdownDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
            var source = new PrintSource(snapshot, gate.Task);
            var window = new PrintPreviewWindow(snapshot, source);
            Track(window);
            window.Show();
            UiPump.For(TimeSpan.FromMilliseconds(150));

            Assert.False(source.Parsed.IsCompleted);
            Assert.True(window.IsBusy);
            Assert.Equal(PreviewStage.Paginating, window.Stage);
            Assert.Null(window.PrintedDocument); // dokumen belum dibuat selama parse berjalan
            Assert.Equal(0, window.PageCount);
            Assert.False(Enabled(window, "NextPageButton"));

            Task.Run(() => gate.SetResult(DocumentView.ParsePrintSnapshot(snapshot))).Wait();
            WaitShown(window);

            Assert.NotNull(window.PrintedDocument);
            Assert.True(window.PageCount > 1);
        });
    }

    [Fact]
    public void LargeDocument_OpensWithoutBlocking_AndLayoutChangesReuseTheParse()
    {
        Sta(() =>
        {
            var text = Pages(330);
            Assert.True(text.Length >= DocumentView.BackgroundParseChars, text.Length.ToString());

            var window = Open(text, show: true);

            Assert.True(window.IsBusy); // konstruktor kembali sebelum parse/paginasi selesai
            WaitPaginated(window);
            Part<RadioButton>(window, "LandscapeButton").IsChecked = true;

            // AST sudah ada: dokumen siklus baru dibuat sinkron tanpa menunggu parse ulang.
            Assert.NotNull(window.PrintedDocument);
            Assert.Equal(window.Layout.Width, window.PrintedDocument.PageWidth);
            WaitPaginated(window);
            Assert.Contains("Paragraf nomor 330", UiPump.TextOf(window.PrintedDocument));
        });
    }

    [Fact]
    public void FooterOffAndOn_ReachReadyEachTime_WithoutDispatcherErrors()
    {
        var before = WpfHost.Unhandled.Count;
        Sta(() =>
        {
            var window = Open(Pages(20), show: true);
            WaitShown(window);

            for (var i = 0; i < 3; i++)
            {
                Part<CheckBox>(window, "FooterCheck").IsChecked = i % 2 == 1;
                WaitShown(window);
                Assert.True(window.PageCount > 1);
            }
        });
        Assert.Equal(before, WpfHost.Unhandled.Count);
    }

    // ---- Snapshot ----

    [Fact]
    public void Snapshot_ClosingTheTabAfterOpeningThePreview_DoesNotAffectItOrLaterRebuilds()
    {
        var path = dir.WriteText("asli.md", "SEBELUM perubahan\n\n" + Pages(12));
        Sta(() =>
        {
            var tab = DocumentTab.Load(path);
            var window = new PrintPreviewWindow(tab.View.CapturePrintSnapshot());
            Track(window);
            window.Show();

            tab.Document.Text = "SESUDAH perubahan";
            tab.Dispose(); // tab ditutup selagi pratinjau masih menyusun
            WaitShown(window);

            Assert.Contains("SEBELUM", UiPump.TextOf(window.PrintedDocument));
            Assert.Equal("Pratinjau Cetak — asli.md", window.Title);

            Part<RadioButton>(window, "LandscapeButton").IsChecked = true; // siklus baru dari snapshot yang sama
            WaitShown(window);

            Assert.Contains("SEBELUM", UiPump.TextOf(window.PrintedDocument));
            Assert.DoesNotContain("SESUDAH", UiPump.TextOf(window.PrintedDocument));
            Assert.Contains("Paragraf nomor 12", UiPump.TextOf(window.PrintedDocument));
        });
    }

    [Fact]
    public void Snapshot_ChangingTheFilePathAfterOpening_KeepsTheOriginalNameAndImageFolder()
    {
        dir.WriteBytes("gambar/kecil.png", OnePixelPng);
        var path = dir.WriteText("asli.md", "teks asli ![x](gambar/kecil.png)\n\n" + Pages(6));
        var other = dir.File("lain/dipindah.md");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        Sta(() =>
        {
            var tab = DocumentTab.Load(path);
            try
            {
                var window = new PrintPreviewWindow(tab.View.CapturePrintSnapshot());
                Track(window);
                WaitReady(window);
                var imagesBefore = ImageCount(window.PrintedDocument);
                Assert.Equal(1, imagesBefore);

                tab.Document.Text = "isi baru";
                Assert.True(tab.SaveTo(other)); // FilePath tab berpindah ke folder lain yang tak punya gambar tsb

                Part<RadioButton>(window, "WideMarginButton").IsChecked = true;
                WaitReady(window);

                Assert.Equal("Pratinjau Cetak — asli.md", window.Title);
                Assert.Equal(1, ImageCount(window.PrintedDocument)); // folder gambar tetap yang lama
                Assert.Contains("teks asli", UiPump.TextOf(window.PrintedDocument));
                Assert.DoesNotContain("isi baru", UiPump.TextOf(window.PrintedDocument));
            }
            finally
            {
                tab.Dispose();
            }
        });
    }

    static int ImageCount(FlowDocument document)
    {
        var count = 0;
        void Walk(object node)
        {
            // Markdig.Wpf membungkus gambar dalam InlineUIContainer (Button > Image); gambar yang diblokir/rusak menjadi Run teks.
            if (node is InlineUIContainer or BlockUIContainer) { count++; return; }
            if (node is DependencyObject d)
                foreach (var child in LogicalTreeHelper.GetChildren(d)) Walk(child);
        }
        Walk(document);
        return count;
    }

    static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
}
