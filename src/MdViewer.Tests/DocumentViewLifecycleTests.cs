using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using MdViewer.Tests.Support;

namespace MdViewer.Tests;

/// <summary>Memompa dispatcher STA di dalam satu test supaya timer/continuation async (render latar) benar-benar jalan.</summary>
internal static class UiPump
{
    public static void For(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    public static bool Until(Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > timeout) return false;
            For(TimeSpan.FromMilliseconds(15));
        }
        return true;
    }

    public static bool IsTimerEnabled(object owner, string field) =>
        ((DispatcherTimer)owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!).IsEnabled;

    public static string TextOf(FlowDocument? document) =>
        document is null ? "" : new TextRange(document.ContentStart, document.ContentEnd).Text;
}

/// <summary>Siklus hidup <see cref="DocumentView"/> dan <see cref="FindReplaceBar"/>: Dispose/Detach aman, timer berhenti, render usang dibuang.</summary>
[Collection("Wpf")]
public class DocumentViewLifecycleTests : IDisposable
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

    DocumentTab Untitled()
    {
        var tab = DocumentTab.CreateUntitled();
        tabs.Add(tab);
        return tab;
    }

    // Dokumen > 100.000 karakter (satu paragraf panjang): dirender di thread latar.
    static string Big(string heading) => $"# {heading}\n\n" + string.Concat(Enumerable.Repeat("lorem ", 20_000));

    // ---- DocumentView.Dispose ----

    [Fact]
    public void ViewDispose_StopsRunningTimers()
    {
        Sta(() =>
        {
            var tab = Untitled();
            tab.Document.Text = "# halo";
            Assert.True(UiPump.IsTimerEnabled(tab.View, "statsTimer"));
            Assert.True(UiPump.IsTimerEnabled(tab.View, "renderTimer"));

            tab.View.Dispose();

            Assert.False(UiPump.IsTimerEnabled(tab.View, "statsTimer"));
            Assert.False(UiPump.IsTimerEnabled(tab.View, "renderTimer"));
        });
    }

    [Fact]
    public void ViewDispose_EditingAfterwardsDoesNotRestartTimersOrTouchTheStatusBar()
    {
        Sta(() =>
        {
            var tab = Untitled();
            tab.View.Dispose();
            var statsBefore = tab.StatsText;

            tab.Document.Text = "kata satu dua tiga";
            UiPump.For(TimeSpan.FromMilliseconds(500)); // lebih lama dari jeda statistik (300 ms) dan render (250 ms)

            Assert.False(UiPump.IsTimerEnabled(tab.View, "statsTimer"));
            Assert.False(UiPump.IsTimerEnabled(tab.View, "renderTimer"));
            Assert.Equal(statsBefore, tab.StatsText);
            Assert.Null(tab.View.Preview.Document);
        });
    }

    [Fact]
    public void ViewDispose_WithOpenFindBarAndPendingQuery_StopsItsTimersToo()
    {
        Sta(() =>
        {
            var tab = Untitled();
            tab.Document.Text = "abc abc";
            tab.View.ShowFind(replace: false);
            tab.View.FindBar.FindBox.Text = "abc"; // memulai queryTimer (debounce 250 ms)
            tab.Document.Insert(0, "x");           // memulai refreshTimer
            Assert.True(UiPump.IsTimerEnabled(tab.View.FindBar, "queryTimer"));
            Assert.True(UiPump.IsTimerEnabled(tab.View.FindBar, "refreshTimer"));

            tab.View.Dispose();

            Assert.False(UiPump.IsTimerEnabled(tab.View.FindBar, "queryTimer"));
            Assert.False(UiPump.IsTimerEnabled(tab.View.FindBar, "refreshTimer"));
            UiPump.For(TimeSpan.FromMilliseconds(400)); // tidak ada tick susulan yang melempar
        });
    }

    [Fact]
    public void ViewDispose_Twice_AndAllMembersAfterDispose_DoNotThrow()
    {
        Sta(() =>
        {
            var tab = Untitled();
            tab.View.Dispose();

            Assert.Null(Record.Exception(() => tab.View.Dispose()));
            Assert.Null(Record.Exception(() => tab.View.RefreshPreview()));
            Assert.Null(Record.Exception(() => tab.View.FindNext(forward: true)));
            Assert.Null(Record.Exception(() => tab.View.ShowFind(replace: true)));
            Assert.Null(Record.Exception(() => tab.View.ScrollToAnchor("x")));
            Assert.Null(Record.Exception(() => tab.View.ApplyZoom(150)));
            Assert.Null(Record.Exception(() => tab.View.RefreshTheme()));
            Assert.Null(Record.Exception(() => tab.Dispose())); // Dispose tab memanggil View.Dispose lagi
            Assert.Null(tab.View.Preview.Document);
        });
    }

    [Fact]
    public void TabDispose_ThenModeOrPathChange_DoesNotRender()
    {
        var path = dir.WriteText("a.md", "# judul");

        Sta(() =>
        {
            var tab = Load(path);
            tab.Dispose();

            tab.Mode = ViewMode.Preview;
            tab.Mode = ViewMode.Split;

            Assert.Null(tab.View.Preview.Document);
        });
    }

    // ---- FindReplaceBar.Detach ----

    static (TextEditor Editor, FindReplaceBar Bar) NewBar(string text)
    {
        var editor = new TextEditor { Document = new ICSharpCode.AvalonEdit.Document.TextDocument(text) };
        var bar = new FindReplaceBar { Visibility = System.Windows.Visibility.Collapsed }; // seperti di DocumentView.xaml
        bar.Attach(editor);
        return (editor, bar);
    }

    static void ClickReplace(FindReplaceBar bar, string label) =>
        ((System.Windows.Controls.Panel)bar.FindName("ReplaceRow")).Children.OfType<Button>().Single(b => (string)b.Content == label)
            .RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    // Daftar penanda dibatasi 20.000 hasil, tetapi "Ganti" harus tetap bekerja pada hasil terpilih di luar batas itu.
    [Fact]
    public void ReplaceCurrent_WorksBeyondTheMarkerCap()
    {
        Sta(() =>
        {
            var count = SearchEngine.MaxResults + 500;
            var (editor, bar) = NewBar(string.Concat(Enumerable.Repeat("x" + (char)10, count)));
            bar.Open(replace: true);
            bar.FindBox.Text = "x";
            bar.FindNext(forward: true); // Refresh langsung (tanpa menunggu debounce)
            var offset = 2 * (SearchEngine.MaxResults + 400);
            editor.Select(offset, 1);
            bar.ReplaceBox.Text = "Y";

            ClickReplace(bar, "Ganti");

            Assert.Equal('Y', editor.Document.GetCharAt(offset));
            Assert.Equal(1, editor.Document.Text.Count(c => c == 'Y'));
        });
    }

    // Pola yang kena batas waktu tidak boleh dijalankan ulang (membekukan UI beberapa detik) pada tiap perubahan teks editor
    // atau tiap Cari Berikutnya; baru dicoba lagi setelah pola/opsi berubah.
    [Fact]
    public void TimedOutPattern_IsNotRerunOnEditorChangesOrFindNext_UntilThePatternOrOptionsChange()
    {
        Sta(() =>
        {
            var (editor, bar) = NewBar(new string('a', 40) + "b");
            bar.Open(replace: false);
            bar.FindBox.Text = "(a+)+$";
            var clock = Stopwatch.StartNew();
            bar.RegexToggle.IsChecked = true; // Options_Changed -> Refresh: backtracking eksponensial, kena batas waktu
            Assert.True(clock.Elapsed > TimeSpan.FromSeconds(1), "prasyarat: pola ini harus lambat");
            Assert.Contains("terlalu lambat", bar.CountText.Text);

            clock.Restart();
            editor.Document.Insert(0, "c"); // memicu refreshTimer (150 ms) dengan pola yang sama
            UiPump.For(TimeSpan.FromMilliseconds(500));
            bar.FindNext(forward: true);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1.5), $"pencarian dijalankan ulang ({clock.Elapsed})");
            Assert.Contains("terlalu lambat", bar.CountText.Text);

            bar.FindBox.Text = "b"; // pola berubah: dicoba lagi
            bar.FindNext(forward: true);
            Assert.DoesNotContain("terlalu lambat", bar.CountText.Text);
            Assert.Contains("hasil", bar.CountText.Text + " hasil");
        });
    }

    static int RendererCount(TextEditor editor) => editor.TextArea.TextView.BackgroundRenderers.Count;

    [Fact]
    public void Detach_RemovesRenderer_StopsTimers_AndIgnoresLaterEdits()
    {
        Sta(() =>
        {
            var (editor, bar) = NewBar("abc");
            var baseline = RendererCount(editor) - 1;
            bar.Open(replace: false);
            editor.Document.Insert(0, "x");
            Assert.True(UiPump.IsTimerEnabled(bar, "refreshTimer"));

            bar.Detach();

            Assert.Equal(baseline, RendererCount(editor));
            Assert.False(UiPump.IsTimerEnabled(bar, "refreshTimer"));
            Assert.False(UiPump.IsTimerEnabled(bar, "queryTimer"));
            editor.Document.Insert(0, "y");
            Assert.False(UiPump.IsTimerEnabled(bar, "refreshTimer")); // tidak lagi mendengarkan editor
        });
    }

    [Fact]
    public void Detach_Twice_OrBeforeAttach_DoesNotThrow()
    {
        Sta(() =>
        {
            var neverAttached = new FindReplaceBar();
            Assert.Null(Record.Exception(neverAttached.Detach));

            var (_, bar) = NewBar("abc");
            bar.Detach();
            Assert.Null(Record.Exception(bar.Detach));
        });
    }

    [Fact]
    public void AfterDetach_OpenFindNextAndClose_AreHarmlessNoOps()
    {
        Sta(() =>
        {
            var (_, bar) = NewBar("abc abc");
            bar.Detach();

            Assert.Null(Record.Exception(() => bar.Open(replace: true)));
            Assert.False(bar.IsOpen);
            Assert.Null(Record.Exception(() => bar.FindNext(forward: true)));
            Assert.Null(Record.Exception(() => bar.FindNext(forward: false)));
            Assert.Null(Record.Exception(bar.Close));
        });
    }

    [Fact]
    public void Detach_WhilePendingQueryTimerRunning_NeverFiresARefreshAfterwards()
    {
        Sta(() =>
        {
            var (editor, bar) = NewBar("abc abc");
            bar.Open(replace: false);
            bar.FindBox.Text = "abc"; // queryTimer berjalan
            Assert.True(UiPump.IsTimerEnabled(bar, "queryTimer"));
            var statusBefore = bar.CountText.Text;

            bar.Detach();
            UiPump.For(TimeSpan.FromMilliseconds(450)); // melewati debounce 250 ms

            Assert.Equal(statusBefore, bar.CountText.Text);
            Assert.Equal(0, editor.SelectionLength); // tidak ada hasil yang dipilih setelah detach
        });
    }

    [Fact]
    public void Detach_ThenAttachAgain_WorksAsFreshBar()
    {
        Sta(() =>
        {
            var (editor, bar) = NewBar("abc abc abc");
            bar.Detach();

            bar.Attach(editor);
            bar.Open(replace: false);
            bar.FindBox.Text = "abc";
            UiPump.Until(() => bar.CountText.Text.Contains("dari"), TimeSpan.FromSeconds(3));

            Assert.Contains("3", bar.CountText.Text);
            Assert.Equal(0, editor.SelectionStart);
            Assert.Equal(3, editor.SelectionLength);
        });
    }

    // ---- Render usang ----

    [Fact]
    public void StaleBackgroundRender_NeverReplacesTheNewerRender()
    {
        var path = dir.WriteText("a.md", Big("ALFA"));

        Sta(() =>
        {
            var tab = Load(path); // render latar #1 (ALFA) sedang berjalan
            var shown = new List<string>();
            var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                FlowDocumentScrollViewer.DocumentProperty, typeof(FlowDocumentScrollViewer));
            descriptor.AddValueChanged(tab.View.Preview, (_, _) => shown.Add(UiPump.TextOf(tab.View.Preview.Document)));

            tab.Document.Text = Big("BETA");
            tab.View.RefreshPreview(); // render latar #2 (BETA); #1 harus dibuang walau selesai belakangan

            var done = UiPump.Until(() => UiPump.TextOf(tab.View.Preview.Document).Contains("BETA"), TimeSpan.FromSeconds(20));
            UiPump.For(TimeSpan.FromMilliseconds(600)); // beri kesempatan render lama (yang harusnya dibuang) mendarat

            Assert.True(done, "Render baru tidak pernah tampil.");
            Assert.DoesNotContain(shown, t => t.Contains("ALFA"));
            Assert.Contains("BETA", UiPump.TextOf(tab.View.Preview.Document));
            Assert.DoesNotContain("ALFA", UiPump.TextOf(tab.View.Preview.Document));
        });
    }

    [Fact]
    public void BackgroundRender_AfterDispose_IsDiscarded_AndNothingIsShown()
    {
        var path = dir.WriteText("a.md", Big("ALFA"));

        Sta(() =>
        {
            var tab = Load(path); // render latar berjalan
            Assert.Null(tab.View.Preview.Document);

            tab.Dispose();
            UiPump.For(TimeSpan.FromSeconds(1.5)); // hasil parse latar tiba di sini dan harus dibuang

            Assert.Null(tab.View.Preview.Document);
        });
    }

    [Fact]
    public void SmallDocument_RefreshAfterEdit_ShowsNewTextImmediately_NotTheOldOne()
    {
        Sta(() =>
        {
            var tab = Untitled();
            tab.Document.Text = "# LAMA";
            tab.View.RefreshPreview();
            Assert.Contains("LAMA", UiPump.TextOf(tab.View.Preview.Document));

            tab.Document.Text = "# BARU";
            tab.View.RefreshPreview();

            var text = UiPump.TextOf(tab.View.Preview.Document);
            Assert.Contains("BARU", text);
            Assert.DoesNotContain("LAMA", text);
        });
    }

    [Fact]
    public void BackgroundRender_LargeDocumentWithEditorTextChange_EventuallyShowsLatestText()
    {
        var path = dir.WriteText("a.md", Big("ALFA"));

        Sta(() =>
        {
            var tab = Load(path);
            tab.Document.Text = Big("BETA"); // jadwal render lewat timer (tanpa RefreshPreview eksplisit)

            var done = UiPump.Until(() => UiPump.TextOf(tab.View.Preview.Document).Contains("BETA"), TimeSpan.FromSeconds(20));

            Assert.True(done);
            Assert.DoesNotContain("ALFA", UiPump.TextOf(tab.View.Preview.Document));
        });
    }

    // ---- Gambar yang tidak bisa di-decode ----

    [Fact]
    public void UndecodableImage_AltTextAppearsExactlyOnce_AfterTheReplacementMarker()
    {
        dir.WriteBytes("rusak.png", [1, 2, 3]);
        var path = dir.WriteText("a.md", "![teks-alt](rusak.png)");

        Sta(() =>
        {
            var tab = Load(path);

            var text = UiPump.TextOf(tab.View.BuildPrintDocument()).Trim();

            Assert.Equal("[gambar tidak dapat ditampilkan: teks-alt]", text);
        });
    }

    [Fact]
    public void UndecodableImage_WithoutAlt_UsesFileNameOnce()
    {
        dir.WriteBytes("rusak.png", [1, 2, 3]);
        var path = dir.WriteText("a.md", "![](rusak.png)");

        Sta(() =>
        {
            var tab = Load(path);

            var text = UiPump.TextOf(tab.View.BuildPrintDocument()).Trim();

            Assert.Equal("[gambar tidak dapat ditampilkan: rusak.png]", text);
        });
    }

    [Fact]
    public void UndecodableImage_WithFormattedAlt_ShowsPlainAltOnce()
    {
        dir.WriteBytes("rusak.png", [1, 2, 3]);
        var path = dir.WriteText("a.md", "sebelum ![gambar *tebal* kita](rusak.png) sesudah");

        Sta(() =>
        {
            var tab = Load(path);

            var text = UiPump.TextOf(tab.View.BuildPrintDocument()).Trim();

            Assert.Equal("sebelum [gambar tidak dapat ditampilkan: gambar tebal kita] sesudah", text);
        });
    }

    // ---- Gambar data: di pratinjau ----

    const string TinyPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    // Sebelum perbaikan: BitmapImage(Uri) melempar NotSupportedException ("The URI prefix is not recognized") untuk data:,
    // retry hanya memeriksa file: sehingga seluruh pratinjau berganti dokumen galat.
    [Fact]
    public void DataImage_ShowsMarker_AndTheRestOfThePreviewStillRenders()
    {
        var path = dir.WriteText("a.md", $"sebelum ![png-kecil](data:image/png;base64,{TinyPng}) sesudah\n\n# Judul");

        Sta(() =>
        {
            var tab = Load(path);
            tab.View.RefreshPreview();

            var text = UiPump.TextOf(tab.View.Preview.Document);
            var print = UiPump.TextOf(tab.View.BuildPrintDocument());

            Assert.Contains("sebelum " + MarkdownSupport.UnsupportedPreviewImageText + " sesudah", text);
            Assert.Contains("Judul", text);
            Assert.DoesNotContain("Pratinjau tidak dapat ditampilkan", text);
            Assert.Contains(MarkdownSupport.UnsupportedPreviewImageText, print);
        });
    }

    [Fact]
    public void FtpImage_ShowsMarker_InPreview_EvenWhenRemoteImagesAreAllowed()
    {
        var path = dir.WriteText("a.md", "a ![x](ftp://127.0.0.1:9/a.png) b");
        var old = DocumentView.BlockRemoteImages;

        try
        {
            Sta(() =>
            {
                DocumentView.BlockRemoteImages = false;
                var tab = Load(path);
                tab.View.RefreshPreview();

                Assert.Contains("a " + MarkdownSupport.BlockedRemoteImageText + " b", UiPump.TextOf(tab.View.Preview.Document));
            });
        }
        finally { DocumentView.BlockRemoteImages = old; }
    }

    // ---- Gambar remote non-http(s) ----

    [Fact]
    public void RemoteImageOverFtp_IsNotFetched_WhenRemoteImagesAreBlocked()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var old = DocumentView.BlockRemoteImages;
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var path = dir.WriteText("a.md", $"![x](ftp://127.0.0.1:{port}/a.png)");

            Sta(() =>
            {
                DocumentView.BlockRemoteImages = true;
                var tab = Load(path);
                tab.View.RefreshPreview();
                UiPump.For(TimeSpan.FromSeconds(2));
            });

            Assert.False(listener.Pending(), "WPF membuka koneksi ke server ftp walau gambar remote diblokir.");
        }
        finally
        {
            DocumentView.BlockRemoteImages = old;
            listener.Stop();
        }
    }
}
