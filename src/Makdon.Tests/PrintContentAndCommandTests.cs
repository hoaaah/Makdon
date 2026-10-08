using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Printing;
using Makdon.Tests.Support;
using static Makdon.Tests.Support.PrintTestKit;

namespace Makdon.Tests;

/// <summary>Isi dokumen cetak yang berbahaya/rusak (gambar), pembuatan dokumen cetak, dan perintah/pintasan Pratinjau Cetak.</summary>
[Collection("Wpf")]
public class PrintContentAndCommandTests : IDisposable
{
    readonly TempDir dir = new();

    public void Dispose() => dir.Dispose();

    static void Sta(Action action) => WpfHost.Instance.Run(action);

    static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    // ---- Dokumen cetak ----

    [Fact]
    public void CreateDocument_IsWhitePaper_WithLayoutSizeAndMargin_NotTheDefaultPadding()
    {
        Sta(() =>
        {
            var layout = PageLayout.For(PaperKind.Letter, PrintOrientation.Landscape, MarginPreset.Wide);

            var document = PrintService.CreateDocument(Snapshot(Sample), layout);

            Assert.Equal(System.Windows.Media.Brushes.White, document.Background);
            Assert.Equal((layout.Width, layout.Height, layout.Width), (document.PageWidth, document.PageHeight, document.ColumnWidth));
            Assert.Equal(new Thickness(96), document.PagePadding);
        });
    }

    [Fact]
    public void CreateDocument_UsesTheLightTheme_EvenWhenTheAppIsDark()
    {
        Sta(() =>
        {
            try
            {
                ThemeManager.Apply(AppThemeMode.Dark);

                var document = PrintService.CreateDocument(Snapshot("# Judul\n\nteks"), Small);

                var brush = document.Foreground as System.Windows.Media.SolidColorBrush;
                Assert.NotNull(brush);
                Assert.True(brush!.Color.R < 0x60 && brush.Color.G < 0x60 && brush.Color.B < 0x60, $"teks cetak {brush.Color} terlalu terang untuk kertas putih");
            }
            finally
            {
                // Keadaan awal suite = kamus Terang (WpfHost); sama seperti test tema lain.
                ThemeManager.Apply(AppThemeMode.Light);
            }
        });
    }

    [Fact]
    public void CreateDocument_TwiceFromTheSameSnapshot_GivesIndependentDocumentsWithTheSameText()
    {
        Sta(() =>
        {
            var snapshot = Snapshot(Paragraphs(5));

            var first = PrintService.CreateDocument(snapshot, Small);
            var second = PrintService.CreateDocument(snapshot, PageLayout.Default);

            Assert.NotSame(first, second);
            Assert.Equal(UiPump.TextOf(first), UiPump.TextOf(second));
            Assert.Equal(Small.Width, first.PageWidth); // mengubah satu tidak mengubah yang lain
            Assert.Equal(PageLayout.Default.Width, second.PageWidth);
        });
    }

    // ---- Galat render: dokumen galat tidak boleh sampai ke kertas ----

    [Fact]
    public void RenderFailure_OnThePrintPath_FailsWithAFriendlyMessage_NotAnErrorDocumentWithTheCrashLogPath()
    {
        var before = WpfHost.Unhandled.Count;
        Sta(() =>
        {
            DocumentView.RenderFaultForTests = _ => throw new InvalidOperationException("render dipaksa gagal");
            try
            {
                var build = new PreviewBuild(Snapshot(Sample), Small, headerFooter: true);
                build.Start(); // dokumen kecil: dibuat sinkron

                Assert.Equal(PreviewStage.Failed, build.Stage);
                Assert.Null(build.Document); // tidak ada dokumen (galat) yang bisa dipaginasi/dicetak
                Assert.Null(build.Pages);
                Assert.NotNull(build.Error);
                Assert.DoesNotContain(CrashLog.LogPath, build.Error!.Message);
                Assert.DoesNotContain(Path.GetDirectoryName(CrashLog.LogPath)!, build.Error.Message);
                Assert.Contains("crash.log", build.Error.Message);
                Assert.IsType<InvalidOperationException>(build.Error.InnerException); // penyebab asli tetap tersimpan untuk log
                build.Dispose();

                // Jalur Cetak langsung (Ctrl+P): melempar ke pemanggil yang menampilkannya sebagai "Gagal mencetak."
                var thrown = Assert.Throws<InvalidOperationException>(() => PrintService.CreateDocument(Snapshot(Sample), Small));
                Assert.DoesNotContain(CrashLog.LogPath, thrown.Message);
            }
            finally
            {
                DocumentView.RenderFaultForTests = null;
            }
        });
        Assert.Equal(before, WpfHost.Unhandled.Count);
    }

    [Fact]
    public void RenderFailure_InThePrintWindow_ShowsTheFriendlyMessageInThePanel_WithoutThePathOrAPrintablePage()
    {
        Sta(() =>
        {
            DocumentView.RenderFaultForTests = _ => throw new InvalidOperationException("render dipaksa gagal");
            PrintPreviewWindow? window = null;
            try
            {
                window = new PrintPreviewWindow(Snapshot(Sample));
                window.Show();

                Assert.True(window.HasFailed);
                var shown = string.Join("|", new[] { "BusyText", "BusyDetail" }.Select(n => ((System.Windows.Controls.TextBlock)window.FindName(n)).Text));
                Assert.Contains("Pratinjau tidak dapat disusun.", shown);
                Assert.DoesNotContain(CrashLog.LogPath, shown);
                Assert.False(((System.Windows.Controls.Button)window.FindName("PrintButton")).IsEnabled);
            }
            finally
            {
                DocumentView.RenderFaultForTests = null;
                window?.Close();
            }
        });
    }

    [Fact]
    public void RenderFailure_InTheMainPreview_StillShowsTheErrorDocument_AsBefore()
    {
        Sta(() =>
        {
            var tab = DocumentTab.CreateUntitled();
            try
            {
                DocumentView.RenderFaultForTests = _ => throw new InvalidOperationException("render dipaksa gagal");
                tab.Mode = ViewMode.Preview;
                tab.Document.Text = "# judul";
                tab.View.RefreshPreview();

                var preview = (FlowDocumentScrollViewer)tab.View.FindName("Preview");
                Assert.Contains("Pratinjau tidak dapat ditampilkan", UiPump.TextOf(preview.Document));
            }
            finally
            {
                DocumentView.RenderFaultForTests = null;
                tab.Dispose();
            }
        });
    }

    // ---- Gambar: rusak / remote / UNC / data ----

    [Fact]
    public void UndecodableAndMissingImages_DoNotBreakThePrintDocument_OrLeaveDispatcherErrors()
    {
        dir.WriteBytes("rusak.png", [1, 2, 3]);
        var before = WpfHost.Unhandled.Count;
        Sta(() =>
        {
            var snapshot = Snapshot("a ![alt-rusak](rusak.png) b ![hilang](tidak-ada.png) c ![](sub/../tidak-ada-juga.png) d", baseDirectory: dir.Path);

            var build = new PreviewBuild(snapshot, Small, headerFooter: true);
            build.Start();
            WaitForEnd(build);
            UiPump.For(TimeSpan.FromMilliseconds(300)); // pemuatan gambar/halaman yang tertunda

            var text = UiPump.TextOf(build.Document);
            Assert.Equal(PreviewStage.Ready, build.Stage);
            Assert.Contains("[gambar tidak dapat ditampilkan: alt-rusak]", text);
            Assert.Contains("a ", text);
            Assert.Contains(" d", text);
            Assert.True(build.PageCount >= 1);
            build.Dispose();
        });
        Assert.Equal(before, WpfHost.Unhandled.Count);
    }

    [Fact]
    public void ValidLocalImage_IsKeptInThePrintDocument()
    {
        dir.WriteBytes("ok.png", ValidPng);
        Sta(() =>
        {
            var document = PrintService.CreateDocument(Snapshot("![x](ok.png)", baseDirectory: dir.Path), Small);

            Assert.Single(document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<InlineUIContainer>());
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FtpUncFileHostAndDataImages_AreBlockedInThePrintDocument_AndNeverTouchTheNetwork(bool blockRemote)
    {
        var ftp = new TcpListener(IPAddress.Loopback, 0);
        var http = new TcpListener(IPAddress.Loopback, 0);
        ftp.Start();
        http.Start();
        try
        {
            var ftpPort = ((IPEndPoint)ftp.LocalEndpoint).Port;
            var httpPort = ((IPEndPoint)http.LocalEndpoint).Port;
            var text = string.Join("\n\n",
                $"![a](ftp://127.0.0.1:{ftpPort}/a.png)",
                $"![b](//127.0.0.1:{ftpPort}/b.png)",
                $"![c](file://127.0.0.1/c$/c.png)",
                // Markdig meruntuhkan garis miring terbalik di URL, jadi UNC hanya bisa lolos lewat bentuk ter-escape (%5C%5C).
                "![d](%5C%5C127.0.0.1%5Cshare%5Cd.png)",
                "![e](data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==)",
                $"![f](gopher://127.0.0.1:{ftpPort}/f.png)",
                $"![g](javascript:alert(1))");
            if (blockRemote) text += $"\n\n![h](http://127.0.0.1:{httpPort}/h.png)\n\n![i](https://127.0.0.1:{httpPort}/i.png)";
            var before = WpfHost.Unhandled.Count;

            string body = "";
            Sta(() =>
            {
                var build = new PreviewBuild(Snapshot(text, blockRemote: blockRemote, baseDirectory: dir.Path), Small, headerFooter: true);
                build.Start();
                WaitForEnd(build);
                UiPump.For(TimeSpan.FromMilliseconds(400)); // beri waktu pemuat gambar WPF bila ia (salah) mencoba koneksi
                body = UiPump.TextOf(build.Document);
                Assert.Equal(PreviewStage.Ready, build.Stage);
                Assert.Empty(build.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<InlineUIContainer>()); // tak satu pun gambar dimuat
                build.Dispose();
            });

            Assert.False(ftp.Pending(), "ada koneksi ke server ftp/UNC-host lokal dari dokumen cetak");
            Assert.False(http.Pending(), "ada koneksi http(s) padahal gambar remote diblokir");
            Assert.True(Regex.Matches(body, Regex.Escape(MarkdownSupport.BlockedRemoteImageText)).Count + Regex.Matches(body, Regex.Escape(MarkdownSupport.UnsupportedPreviewImageText)).Count
                        >= (blockRemote ? 9 : 7), body);
            Assert.DoesNotContain("127.0.0.1", body);
            Assert.DoesNotContain("share", body);
            Assert.Equal(before, WpfHost.Unhandled.Count);
        }
        finally
        {
            ftp.Stop();
            http.Stop();
        }
    }

    [Fact]
    public void SnapshotBlockRemoteFlag_IsHonoured_NotTheCurrentGlobalSetting()
    {
        var old = DocumentView.BlockRemoteImages;
        try
        {
            Sta(() =>
            {
                DocumentView.BlockRemoteImages = false;
                var blocked = UiPump.TextOf(PrintService.CreateDocument(Snapshot("![x](https://example.invalid/a.png)", blockRemote: true), Small));

                DocumentView.BlockRemoteImages = true;
                var allowedFlag = PrintService.CreateDocument(Snapshot("![x](https://example.invalid/a.png)", blockRemote: false), Small);

                Assert.Contains(MarkdownSupport.BlockedRemoteImageText, blocked);
                Assert.DoesNotContain(MarkdownSupport.BlockedRemoteImageText, UiPump.TextOf(allowedFlag));
            });
        }
        finally
        {
            DocumentView.BlockRemoteImages = old;
        }
    }

    // ---- Perintah dan pintasan ----

    static string? FindSource(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    static IEnumerable<(string Command, Key Key, ModifierKeys Modifiers)> GesturesOf(string name, RoutedUICommand command) =>
        command.InputGestures.OfType<KeyGesture>().Select(g => (name, g.Key, g.Modifiers));

    [Fact]
    public void PrintPreviewCommand_HasCtrlShiftP_AndCtrlPStaysWithPrint()
    {
        var command = AppCommands.PrintPreview;

        var gesture = Assert.Single(command.InputGestures.OfType<KeyGesture>());
        Assert.Equal((Key.P, ModifierKeys.Control | ModifierKeys.Shift), (gesture.Key, gesture.Modifiers));
        Assert.Equal("Pratinjau Cetak...", command.Text);
        Assert.Equal("PrintPreview", command.Name);
        Assert.Equal(typeof(AppCommands), command.OwnerType);
        Assert.NotSame(ApplicationCommands.Print, command);
        Assert.Contains(ApplicationCommands.Print.InputGestures.OfType<KeyGesture>(), g => g.Key == Key.P && g.Modifiers == ModifierKeys.Control);
        Assert.DoesNotContain(ApplicationCommands.Print.InputGestures.OfType<KeyGesture>(), g => g.Modifiers.HasFlag(ModifierKeys.Shift));
        Assert.Equal("Ctrl+Shift+P", gesture.GetDisplayStringForCulture(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NoTwoAppCommands_ShareTheSameKeyGesture()
    {
        var all = typeof(AppCommands).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(RoutedUICommand))
            .SelectMany(f => GesturesOf(f.Name, (RoutedUICommand)f.GetValue(null)!))
            .ToList();

        Assert.Contains(all, g => g.Command == "PrintPreview");
        var duplicates = all.GroupBy(g => (g.Key, g.Modifiers)).Where(group => group.Select(g => g.Command).Distinct().Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(g => g.Command))}").ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void MainWindowShortcuts_AreUnique_AcrossAppCommandsStandardCommandsAndKeyBindings()
    {
        var xamlPath = FindSource(Path.Combine("src", "Makdon", "MainWindow.xaml"));
        Assert.True(xamlPath is not null, "MainWindow.xaml tidak ditemukan dari folder test (jalankan test dari checkout repo)");
        var xaml = File.ReadAllText(xamlPath!);

        var gestures = new List<(string Command, Key Key, ModifierKeys Modifiers)>();

        // Perintah AppCommands yang punya CommandBinding di jendela utama.
        var appFields = typeof(AppCommands).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(RoutedUICommand)).ToDictionary(f => f.Name);
        foreach (Match m in Regex.Matches(xaml, @"<CommandBinding Command=""\{x:Static local:AppCommands\.(\w+)\}"""))
            gestures.AddRange(GesturesOf("AppCommands." + m.Groups[1].Value, (RoutedUICommand)appFields[m.Groups[1].Value].GetValue(null)!));

        // Perintah standar yang punya CommandBinding.
        foreach (Match m in Regex.Matches(xaml, @"<CommandBinding Command=""ApplicationCommands\.(\w+)"""))
        {
            var property = typeof(ApplicationCommands).GetProperty(m.Groups[1].Value)!;
            gestures.AddRange(GesturesOf("ApplicationCommands." + m.Groups[1].Value, (RoutedUICommand)property.GetValue(null)!));
        }

        // KeyBinding eksplisit.
        foreach (Match m in Regex.Matches(xaml, @"<KeyBinding Key=""(\w+)"" Modifiers=""([\w+]+)"" Command=""([^""]+)"""))
        {
            var modifiers = m.Groups[2].Value.Split('+').Aggregate(ModifierKeys.None, (acc, part) =>
                acc | (part == "Ctrl" ? ModifierKeys.Control : Enum.Parse<ModifierKeys>(part)));
            gestures.Add((m.Groups[3].Value, Enum.Parse<Key>(m.Groups[1].Value), modifiers));
        }

        Assert.Contains(gestures, g => g.Command == "AppCommands.PrintPreview" && g.Key == Key.P);
        Assert.True(gestures.Count > 20, $"hanya {gestures.Count} pintasan terbaca; pola XAML berubah?");
        var duplicates = gestures.GroupBy(g => (g.Key, g.Modifiers)).Where(group => group.Select(g => g.Command).Distinct().Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(g => g.Command).Distinct())}").ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void MainWindow_WiresPrintPreview_ToMenuToolbarAndCommandBinding_AlongsideOrdinaryPrint()
    {
        var xamlPath = FindSource(Path.Combine("src", "Makdon", "MainWindow.xaml"));
        Assert.True(xamlPath is not null, "MainWindow.xaml tidak ditemukan");
        var xaml = File.ReadAllText(xamlPath!);

        Assert.Matches(@"<CommandBinding Command=""\{x:Static local:AppCommands\.PrintPreview\}"" Executed=""PrintPreview_Executed"" CanExecute=""HasTab_CanExecute""", xaml);
        Assert.Matches(@"<MenuItem Header=""[^""]*"" Command=""\{x:Static local:AppCommands\.PrintPreview\}""", xaml);
        Assert.Matches(@"<Button Command=""\{x:Static local:AppCommands\.PrintPreview\}""", xaml);
        Assert.Matches(@"<MenuItem Header=""_Cetak\.\.\."" Command=""ApplicationCommands\.Print""", xaml);
        Assert.Single(Regex.Matches(xaml, @"Executed=""PrintPreview_Executed"""));
    }

    // ---- Ctrl+P di panel pratinjau utama ----

    [Fact]
    public void CtrlP_InTheMainPreviewPane_GoesToTheWindowPrintHandler_NotTheViewersBuiltInPrinting()
    {
        Sta(() =>
        {
            var tab = DocumentTab.CreateUntitled();
            var host = new Window { Content = tab.View, Width = 600, Height = 400 };
            var windowHandlerCalls = 0;
            var windowExecuted = 0;
            host.CommandBindings.Add(new CommandBinding(ApplicationCommands.Print, (_, _) => windowExecuted++,
                (_, e) => { windowHandlerCalls++; e.CanExecute = true; e.Handled = true; }));
            try
            {
                tab.Document.Text = "# hai";
                host.Show();
                UiPump.For(TimeSpan.FromMilliseconds(300));
                var preview = (FlowDocumentScrollViewer)tab.View.FindName("Preview");

                Assert.True(ApplicationCommands.Print.CanExecute(null, preview));
                Assert.True(windowHandlerCalls > 0, "pengikatan bawaan viewer mencegat Print sebelum jendela");

                // Execute aman: pengikatan jendela di atas tidak membuka dialog. Bila pengikatan bawaan viewer yang menang,
                // handler jendela tidak pernah dijalankan (dan PrintDialog bawaan akan terbuka).
                ApplicationCommands.Print.Execute(null, preview);

                Assert.Equal(1, windowExecuted);
            }
            finally
            {
                host.Close();
                tab.Dispose();
            }
        });
    }

    // ---- Tiket printer (tanpa printer, tanpa PrintDialog) ----

    [Theory]
    [InlineData(PaperKind.A4, PrintOrientation.Portrait)]
    [InlineData(PaperKind.A4, PrintOrientation.Landscape)]
    [InlineData(PaperKind.Letter, PrintOrientation.Portrait)]
    [InlineData(PaperKind.Letter, PrintOrientation.Landscape)]
    public void ApplyTicket_SetsExplicitMediaSizeAndOrientation_ThatMatchThePreview(PaperKind paper, PrintOrientation orientation)
    {
        var ticket = new PrintTicket();

        PrintPreviewWindow.ApplyTicket(ticket, paper, orientation);

        var expected = PageLayout.For(paper, PrintOrientation.Portrait, MarginPreset.Normal);
        Assert.Equal(paper == PaperKind.Letter ? PageMediaSizeName.NorthAmericaLetter : PageMediaSizeName.ISOA4, ticket.PageMediaSize!.PageMediaSizeName);
        Assert.Equal(expected.Width, ticket.PageMediaSize.Width!.Value, 1); // ukuran eksplisit, bukan hanya nama
        Assert.Equal(expected.Height, ticket.PageMediaSize.Height!.Value, 1);
        Assert.Equal(orientation == PrintOrientation.Landscape ? PageOrientation.Landscape : PageOrientation.Portrait, ticket.PageOrientation);
        Assert.True(PrintPreviewWindow.TicketMatches(ticket, paper, orientation));
    }

    [Fact]
    public void TicketMatches_DetectsADifferentOrientation_OrPaper_ChosenInThePrintDialog()
    {
        var a4Portrait = new PrintTicket();
        PrintPreviewWindow.ApplyTicket(a4Portrait, PaperKind.A4, PrintOrientation.Portrait);

        Assert.True(PrintPreviewWindow.TicketMatches(a4Portrait, PaperKind.A4, PrintOrientation.Portrait));
        Assert.False(PrintPreviewWindow.TicketMatches(a4Portrait, PaperKind.A4, PrintOrientation.Landscape));
        Assert.False(PrintPreviewWindow.TicketMatches(a4Portrait, PaperKind.Letter, PrintOrientation.Portrait));
        Assert.False(PrintPreviewWindow.TicketMatches(a4Portrait, PaperKind.Letter, PrintOrientation.Landscape));

        var legal = new PrintTicket { PageMediaSize = new PageMediaSize(PageMediaSizeName.NorthAmericaLegal), PageOrientation = PageOrientation.Portrait };
        Assert.False(PrintPreviewWindow.TicketMatches(legal, PaperKind.A4, PrintOrientation.Portrait));
        Assert.False(PrintPreviewWindow.TicketMatches(legal, PaperKind.Letter, PrintOrientation.Portrait));
    }

    [Fact]
    public void TicketMatches_TreatsReverseOrientationsLikeTheirBase_AndUnspecifiedValuesAsNoConflict()
    {
        var reverseLandscape = new PrintTicket { PageOrientation = PageOrientation.ReverseLandscape };
        var reversePortrait = new PrintTicket { PageOrientation = PageOrientation.ReversePortrait };

        Assert.True(PrintPreviewWindow.TicketMatches(reverseLandscape, PaperKind.A4, PrintOrientation.Landscape));
        Assert.False(PrintPreviewWindow.TicketMatches(reverseLandscape, PaperKind.A4, PrintOrientation.Portrait));
        Assert.True(PrintPreviewWindow.TicketMatches(reversePortrait, PaperKind.Letter, PrintOrientation.Portrait));
        Assert.True(PrintPreviewWindow.TicketMatches(new PrintTicket(), PaperKind.Letter, PrintOrientation.Landscape)); // tiket kosong: tak ada yang bertentangan
    }

    [Fact]
    public void TicketMatches_AcceptsAnUnnamedSizeWithTheSameDimensions_AndRejectsOneWithOtherDimensions()
    {
        var sameAsA4 = new PrintTicket { PageMediaSize = new PageMediaSize(794, 1123) };
        var sameAsA4Rotated = new PrintTicket { PageMediaSize = new PageMediaSize(1123, 794) };
        var other = new PrintTicket { PageMediaSize = new PageMediaSize(600, 900) };

        Assert.True(PrintPreviewWindow.TicketMatches(sameAsA4, PaperKind.A4, PrintOrientation.Portrait));
        Assert.True(PrintPreviewWindow.TicketMatches(sameAsA4Rotated, PaperKind.A4, PrintOrientation.Portrait));
        Assert.False(PrintPreviewWindow.TicketMatches(sameAsA4, PaperKind.Letter, PrintOrientation.Portrait));
        Assert.False(PrintPreviewWindow.TicketMatches(other, PaperKind.A4, PrintOrientation.Portrait));
    }

    [Theory]
    [InlineData(PageMediaSizeName.ISOA4Rotated, PaperKind.A4, PrintOrientation.Landscape, true)]
    [InlineData(PageMediaSizeName.ISOA4Rotated, PaperKind.A4, PrintOrientation.Portrait, true)]
    [InlineData(PageMediaSizeName.NorthAmericaLetterRotated, PaperKind.Letter, PrintOrientation.Landscape, true)]
    [InlineData(PageMediaSizeName.NorthAmericaLetterRotated, PaperKind.Letter, PrintOrientation.Portrait, true)]
    [InlineData(PageMediaSizeName.ISOA4Rotated, PaperKind.Letter, PrintOrientation.Landscape, false)]
    [InlineData(PageMediaSizeName.NorthAmericaLetterRotated, PaperKind.A4, PrintOrientation.Landscape, false)]
    public void TicketMatches_TreatsRotatedNamedSizes_AsTheSamePaper_OnlyWhenTheKindMatches(
        PageMediaSizeName name, PaperKind paper, PrintOrientation orientation, bool expected)
    {
        // Nama saja, tanpa lebar/tinggi eksplisit - bentuk yang dilaporkan sebagian driver untuk kertas terputar.
        var ticket = new PrintTicket { PageMediaSize = new PageMediaSize(name) };

        Assert.Equal(expected, PrintPreviewWindow.TicketMatches(ticket, paper, orientation));
    }

    [Fact]
    public void TicketMatches_RotatedSizeWithTheWrongOrientation_StillConflicts()
    {
        var ticket = new PrintTicket { PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4Rotated), PageOrientation = PageOrientation.Portrait };

        Assert.False(PrintPreviewWindow.TicketMatches(ticket, PaperKind.A4, PrintOrientation.Landscape));
    }

    [Theory]
    [InlineData(PageMediaSizeName.ISOA4, "A4")]
    [InlineData(PageMediaSizeName.ISOA4Rotated, "A4")]
    [InlineData(PageMediaSizeName.ISOA3, "A3")]
    [InlineData(PageMediaSizeName.ISOA5, "A5")]
    [InlineData(PageMediaSizeName.NorthAmericaLetter, "Letter")]
    [InlineData(PageMediaSizeName.NorthAmericaLetterRotated, "Letter")]
    [InlineData(PageMediaSizeName.NorthAmericaLegal, "Legal")]
    [InlineData(PageMediaSizeName.NorthAmericaExecutive, "Executive")]
    [InlineData(PageMediaSizeName.NorthAmericaTabloid, "Tabloid")]
    [InlineData(PageMediaSizeName.JapanHagakiPostcard, "JapanHagakiPostcard")] // tanpa pemetaan: nama enum apa adanya
    public void DescribeTicket_ShowsFamiliarPaperNames_NotEnumNames(PageMediaSizeName name, string expected)
    {
        var ticket = new PrintTicket { PageMediaSize = new PageMediaSize(name), PageOrientation = PageOrientation.Landscape };

        Assert.Equal($"{expected}, lanskap", PrintPreviewWindow.DescribeTicket(ticket));
    }

    [Fact]
    public void DescribeTicket_FallsBackToMillimetres_OrAGenericName_WhenThePaperHasNoName()
    {
        Assert.Equal("200 x 300 mm, potret", PrintPreviewWindow.DescribeTicket(new PrintTicket { PageMediaSize = new PageMediaSize(756, 1134), PageOrientation = PageOrientation.Portrait }));
        Assert.Equal("kertas lain, potret", PrintPreviewWindow.DescribeTicket(new PrintTicket()));
    }
}
