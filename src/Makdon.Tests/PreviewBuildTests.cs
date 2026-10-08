using System.IO.Packaging;
using System.Reflection;
using System.Windows.Documents;
using Makdon.Tests.Support;
using static Makdon.Tests.Support.PrintTestKit;

namespace Makdon.Tests;

/// <summary>Satu siklus penyusunan pratinjau: tahapan, pembatalan, Dispose, siklus kedua, isi halaman XPS.</summary>
[Collection("Wpf")]
public class PreviewBuildTests
{
    static void Sta(Action action) => WpfHost.Instance.Run(action);

    static Uri? PackageUriOf(PreviewBuild build) =>
        (Uri?)typeof(PreviewBuild).GetField("packageUri", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(build);

    // ---- Alur normal ----

    [Fact]
    public void Build_GoesThroughPaginatingRenderingReady_InOrder_WithConsistentCounts()
    {
        Sta(() =>
        {
            var seen = new List<(PreviewStage Stage, int Count, int Rendered)>();
            var build = new PreviewBuild(Snapshot(Paragraphs(40)), Small, headerFooter: true);
            build.Changed += () => seen.Add((build.Stage, build.PageCount, build.RenderedPages));

            build.Start();
            WaitForEnd(build);

            Assert.Equal(PreviewStage.Ready, build.Stage);
            Assert.Equal(seen.OrderBy(s => s.Stage).Select(s => s.Stage), seen.Select(s => s.Stage)); // tahap tak pernah mundur
            Assert.Contains(seen, s => s.Stage == PreviewStage.Rendering);
            Assert.Equal(PreviewStage.Ready, seen[^1].Stage);
            Assert.All(seen, s => Assert.True(s.Rendered <= s.Count || s.Stage == PreviewStage.Paginating, $"{s}"));
            Assert.True(build.PageCount >= 5, $"{build.PageCount} halaman");
            Assert.Equal(build.PageCount, build.Pages!.DocumentPaginator.PageCount);
            Assert.Null(build.Error);
            build.Dispose();
        });
    }

    [Fact]
    public void XpsPages_CarryDocumentNameAndPageXOfN_WhenFooterIsOn()
    {
        Sta(() =>
        {
            var build = StartBuild(Paragraphs(30), footer: true, title: "laporan.md");
            WaitForEnd(build);

            var total = build.PageCount;
            Assert.Equal(PreviewStage.Ready, build.Stage);
            Assert.True(total > 3);
            foreach (var index in new[] { 0, 1, total - 1 })
            {
                var texts = GlyphTexts(build.Pages!, index);
                Assert.Contains("laporan.md", texts);
                Assert.Contains($"Halaman {index + 1} dari {total}", texts);
            }
            build.Dispose();
        });
    }

    [Fact]
    public void XpsPages_HaveNoFooter_WhenFooterIsOff_ButSameBodyAndPageCount()
    {
        Sta(() =>
        {
            var on = StartBuild(Paragraphs(30), footer: true);
            var off = StartBuild(Paragraphs(30), footer: false);
            WaitForEnd(on);
            WaitForEnd(off);

            Assert.Equal(on.PageCount, off.PageCount);
            var offTexts = GlyphTexts(off.Pages!, 0);
            Assert.DoesNotContain(offTexts, t => t.Contains("Halaman "));
            Assert.DoesNotContain("contoh.md", offTexts);
            Assert.Equal(offTexts, GlyphTexts(on.Pages!, 0).Where(t => t != "contoh.md" && !t.StartsWith("Halaman ")).ToList());
            on.Dispose();
            off.Dispose();
        });
    }

    [Fact]
    public void XpsPages_HaveTheLayoutPageSize()
    {
        Sta(() =>
        {
            var layout = PageLayout.For(PaperKind.Letter, PrintOrientation.Landscape, MarginPreset.Narrow);
            var build = StartBuild(Paragraphs(20), layout);
            WaitForEnd(build);

            var page = build.Pages!.References[0].GetDocument(false).Pages[0].GetPageRoot(false);

            Assert.Equal(layout.Width, page.Width, 1);
            Assert.Equal(layout.Height, page.Height, 1);
            build.Dispose();
        });
    }

    [Fact]
    public void Layout_IsTheOneGiven_AndDocumentUsesIt()
    {
        Sta(() =>
        {
            var build = new PreviewBuild(Snapshot(Sample), Small, headerFooter: false);
            Assert.Equal(PreviewStage.Paginating, build.Stage);
            Assert.Equal(0, build.PageCount);
            Assert.Null(build.Pages);

            build.Start(); // dokumen dibuat saat Start (parse sudah selesai untuk dokumen kecil)

            Assert.Equal(Small, build.Layout);
            Assert.Equal(Small.Width, build.Document.PageWidth);
            Assert.Equal(Small.Height, build.Document.PageHeight);
            build.Dispose();
        });
    }

    // ---- Dokumen kosong ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n\n")]
    [InlineData("\t \r\n \t")]
    public void EmptyOrBlankDocument_ReachesReady_WithOneBlankPage(string text)
    {
        Sta(() =>
        {
            var build = StartBuild(text);
            WaitForEnd(build);

            Assert.Equal(PreviewStage.Ready, build.Stage);
            Assert.Equal(1, build.PageCount);
            Assert.Equal(1, build.Pages!.DocumentPaginator.PageCount);
            Assert.Contains("Halaman 1 dari 1", GlyphTexts(build.Pages, 0));
            build.Dispose();
        });
    }

    // ---- Pembatalan / Dispose ----

    [Fact]
    public void Dispose_WhilePaginating_RaisesNoLaterEvents_AndNeverBecomesReady()
    {
        Sta(() =>
        {
            var build = new PreviewBuild(Snapshot(Paragraphs(1500)), Small, headerFooter: true);
            var raised = 0;
            build.Changed += () => raised++;
            build.Start();
            Assert.Equal(PreviewStage.Paginating, build.Stage); // dokumen cukup besar sehingga belum selesai
            var before = raised;

            build.Dispose();
            UiPump.For(TimeSpan.FromMilliseconds(400));

            Assert.Equal(before, raised);
            Assert.Equal(PreviewStage.Paginating, build.Stage);
            Assert.Null(build.Pages);
            Assert.Null(build.Error);
        });
    }

    [Fact]
    public void Dispose_WhilePaginating_StopsTheBackgroundPagination()
    {
        Sta(() =>
        {
            // Di bawah ambang parse latar (BackgroundParseChars) supaya dokumen langsung ada setelah Start.
            var text = Paragraphs(550);
            Assert.True(text.Length < DocumentView.BackgroundParseChars);
            var running = new PreviewBuild(Snapshot(text), Small, headerFooter: false);
            var disposed = new PreviewBuild(Snapshot(text), Small, headerFooter: false);
            running.Start();
            disposed.Start();
            var runningInner = (DynamicDocumentPaginator)((IDocumentPaginatorSource)running.Document).DocumentPaginator;
            var disposedInner = (DynamicDocumentPaginator)((IDocumentPaginatorSource)disposed.Document).DocumentPaginator;
            UiPump.For(TimeSpan.FromMilliseconds(100));
            Assert.False(disposedInner.IsPageCountValid, "dokumen uji terlalu kecil: paginasi sudah selesai sebelum Dispose");

            disposed.Dispose();
            var atDispose = disposedInner.PageCount;
            var runningAtDispose = runningInner.PageCount;
            UiPump.For(TimeSpan.FromMilliseconds(700));

            // Kontrol: tanpa Dispose paginasi terus maju (membuktikan jeda di atas cukup untuk melihat kemajuan).
            Assert.True(runningInner.PageCount > runningAtDispose || runningInner.IsPageCountValid, "kontrol: paginasi yang tidak dibuang tidak maju");
            Assert.False(disposedInner.IsPageCountValid, "paginasi masih berjalan setelah Dispose");
            Assert.Equal(atDispose, disposedInner.PageCount);
            Assert.False(disposedInner.IsBackgroundPaginationEnabled);
            running.Dispose();
        });
    }

    [Fact]
    public void Dispose_InsideTheRenderingStageChangedCallback_StopsBeforeAnyPageIsWritten()
    {
        Sta(() =>
        {
            PreviewBuild? build = null;
            var raised = 0;
            var disposedAtRendering = false;
            build = new PreviewBuild(Snapshot(Paragraphs(40)), Small, headerFooter: true);
            build.Changed += () =>
            {
                raised++;
                if (build!.Stage == PreviewStage.Rendering && !disposedAtRendering)
                {
                    disposedAtRendering = true;
                    build.Dispose();
                }
            };

            build.Start();
            Assert.True(UiPump.Until(() => disposedAtRendering, Patience));
            var before = raised;
            UiPump.For(TimeSpan.FromMilliseconds(400));

            Assert.Equal(before, raised);
            Assert.Equal(PreviewStage.Rendering, build.Stage);
            Assert.Null(build.Pages);
            Assert.Null(build.Error);
        });
    }

    [Fact]
    public void Dispose_WhileXpsPagesAreBeingWritten_IsClean()
    {
        Sta(() =>
        {
            PreviewBuild? build = null;
            var raised = 0;
            var disposedMidWrite = false;
            build = new PreviewBuild(Snapshot(Paragraphs(120)), Small, headerFooter: true);
            build.Changed += () =>
            {
                raised++;
                if (build!.Stage == PreviewStage.Rendering && build.RenderedPages >= 2 && !disposedMidWrite)
                {
                    disposedMidWrite = true;
                    build.Dispose();
                }
            };

            build.Start();
            Assert.True(UiPump.Until(() => disposedMidWrite, Patience), $"tahap {build.Stage}, ditulis {build.RenderedPages}/{build.PageCount}");
            var before = raised;

            // Penulisan yang dibatalkan boleh masih menyisakan callback di antrean dispatcher; tak boleh ada yang melempar/menembak.
            UiPump.For(TimeSpan.FromMilliseconds(600));

            Assert.Equal(before, raised);
            Assert.NotEqual(PreviewStage.Ready, build.Stage);
            Assert.NotEqual(PreviewStage.Failed, build.Stage);
            Assert.Null(build.Pages);
            Assert.Null(build.Error);
            Assert.True(build.RenderedPages < build.PageCount, $"{build.RenderedPages} dari {build.PageCount}: penulisan tidak dibatalkan");
        });
    }

    [Fact]
    public void Dispose_AfterReady_ReleasesPagesAndPackage_AndIsIdempotent()
    {
        Sta(() =>
        {
            var build = StartBuild(Paragraphs(10));
            WaitForEnd(build);
            var uri = PackageUriOf(build);
            Assert.NotNull(uri);
            Assert.NotNull(PackageStore.GetPackage(uri));

            build.Dispose();
            build.Dispose();

            Assert.Null(build.Pages);
            // Paket dilepas setelah dispatcher idle (pemuatan halaman viewer yang sudah antre masih butuh paketnya).
            Assert.True(UiPump.Until(() => PackageStore.GetPackage(uri) is null, TimeSpan.FromSeconds(5)), "paket tertinggal di PackageStore (kebocoran)");
        });
    }

    [Fact]
    public void Dispose_BeforeStart_ThenStart_DoesNothing()
    {
        Sta(() =>
        {
            var build = new PreviewBuild(Snapshot(Paragraphs(10)), Small, headerFooter: true);
            var raised = 0;
            build.Changed += () => raised++;

            build.Dispose();
            build.Start();
            UiPump.For(TimeSpan.FromMilliseconds(200));

            Assert.Equal(0, raised);
            Assert.Equal(PreviewStage.Paginating, build.Stage);
            Assert.Null(build.Pages);
            Assert.Null(PackageUriOf(build));
        });
    }

    [Fact]
    public void Start_Twice_StillProducesASingleReadyResult()
    {
        Sta(() =>
        {
            var build = new PreviewBuild(Snapshot(Paragraphs(20)), Small, headerFooter: true);
            var ready = 0;
            build.Changed += () => { if (build.Stage == PreviewStage.Ready) ready++; };

            build.Start();
            build.Start();
            WaitForEnd(build);
            UiPump.For(TimeSpan.FromMilliseconds(100));

            Assert.Equal(1, ready);
            Assert.Equal(PreviewStage.Ready, build.Stage);
            build.Dispose();
        });
    }

    [Fact]
    public void Dispose_IsSafeEvenIfCalledFromWithinTheReadyCallback()
    {
        var unhandledBefore = WpfHost.Unhandled.Count;
        Sta(() =>
        {
            PreviewBuild? build = null;
            build = new PreviewBuild(Snapshot(Paragraphs(10)), Small, headerFooter: false);
            Exception? failure = null;
            var disposedInsideCallback = false;
            Uri? uri = null;
            var raisedAfterDispose = 0;
            build.Changed += () =>
            {
                if (disposedInsideCallback) raisedAfterDispose++;
                if (build!.Stage != PreviewStage.Ready || disposedInsideCallback) return;
                disposedInsideCallback = true;
                uri = PackageUriOf(build);
                Assert.NotNull(build.Pages); // di dalam callback halaman masih ada
                failure = Record.Exception(build.Dispose);
            };

            build.Start();
            // Callback Ready benar-benar jalan (bukan sekadar Stage berubah) dan Dispose terjadi di dalamnya.
            Assert.True(UiPump.Until(() => disposedInsideCallback, Patience), $"callback Ready tidak pernah jalan (tahap {build.Stage})");
            UiPump.For(TimeSpan.FromMilliseconds(100));

            Assert.Null(failure);
            Assert.Null(build.Pages);
            Assert.Null(build.Error);
            Assert.Equal(PreviewStage.Ready, build.Stage);
            Assert.Equal(0, raisedAfterDispose);
            Assert.NotNull(uri);
            Assert.True(UiPump.Until(() => PackageStore.GetPackage(uri!) is null, TimeSpan.FromSeconds(5)), "paket tertinggal di PackageStore setelah Dispose di callback Ready");
            Assert.True(build.CleanupFallbackTimer is null or { IsEnabled: false });
        });
        Assert.Equal(unhandledBefore, WpfHost.Unhandled.Count);
    }

    // ---- Siklus kedua ----

    [Fact]
    public void SecondCycle_AfterDispose_BuildsIndependently_WithItsOwnPackage()
    {
        Sta(() =>
        {
            var first = StartBuild(Paragraphs(30));
            WaitForEnd(first);
            var firstUri = PackageUriOf(first);
            var firstCount = first.PageCount;
            first.Dispose();

            var second = StartBuild(Paragraphs(30));
            WaitForEnd(second);

            Assert.Equal(PreviewStage.Ready, second.Stage);
            Assert.Equal(firstCount, second.PageCount);
            Assert.NotEqual(firstUri, PackageUriOf(second));
            Assert.Contains($"Halaman 1 dari {firstCount}", GlyphTexts(second.Pages!, 0));
            second.Dispose();
        });
    }

    [Fact]
    public void SecondCycle_StartedWhileTheFirstIsStillRendering_DoesNotDisturbIt()
    {
        Sta(() =>
        {
            var first = StartBuild(Paragraphs(60));
            var second = StartBuild(Paragraphs(15), footer: false);

            WaitForEnd(first);
            WaitForEnd(second);

            Assert.Equal(PreviewStage.Ready, first.Stage);
            Assert.Equal(PreviewStage.Ready, second.Stage);
            Assert.True(first.PageCount > second.PageCount);
            Assert.NotEqual(PackageUriOf(first), PackageUriOf(second));
            Assert.Contains(GlyphTexts(first.Pages!, 0), t => t.StartsWith("Halaman 1 dari "));
            first.Dispose();
            Assert.Equal(second.PageCount, second.Pages!.DocumentPaginator.PageCount); // yang kedua tetap utuh
            second.Dispose();
        });
    }

    [Fact]
    public void ManyCyclesInARow_DoNotAccumulatePackages()
    {
        Sta(() =>
        {
            var uris = new List<Uri>();
            for (var i = 0; i < 6; i++)
            {
                var build = StartBuild(Paragraphs(8));
                WaitForEnd(build);
                uris.Add(PackageUriOf(build)!);
                build.Dispose();
            }

            Assert.Equal(6, uris.Distinct().Count());
            Assert.True(UiPump.Until(() => uris.All(uri => PackageStore.GetPackage(uri) is null), TimeSpan.FromSeconds(5)), "paket tertinggal di PackageStore");
        });
    }

    // ---- Satu penulis per siklus ----

    static int WriterCounter => (int)typeof(PreviewBuild).GetField("counter", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("# satu halaman\n\nisi pendek")]
    public void ShortOrEmptyDocument_StartsExactlyOneXpsWriter_AndEntersRenderingOnce(string text)
    {
        Sta(() =>
        {
            var before = WriterCounter;
            var rendering = 0;
            var last = PreviewStage.Paginating;
            var build = new PreviewBuild(Snapshot(text), Small, headerFooter: true);
            build.Changed += () =>
            {
                if (build.Stage == PreviewStage.Rendering && last != PreviewStage.Rendering) rendering++; // masuk tahap, bukan tiap kabar kemajuan
                last = build.Stage;
            };

            build.Start(); // PaginationCompleted bisa terpicu sinkron saat PageCount dibaca di dalam Start
            WaitForEnd(build);
            UiPump.For(TimeSpan.FromMilliseconds(100));

            Assert.Equal(PreviewStage.Ready, build.Stage);
            Assert.Equal(1, WriterCounter - before);
            Assert.Equal(1, rendering);
            build.Dispose();
        });
    }

    // ---- Galat tidak boleh lolos ke dispatcher ----

    [Theory]
    [InlineData(PreviewStage.Rendering, false)]
    [InlineData(PreviewStage.Ready, false)]
    [InlineData(PreviewStage.Rendering, true)]
    [InlineData(PreviewStage.Ready, true)]
    public void SubscriberThatThrows_FailsTheBuild_AndNeverReachesTheDispatcher(PreviewStage failAt, bool outOfMemory)
    {
        var unhandledBefore = WpfHost.Unhandled.Count;
        Sta(() =>
        {
            var build = new PreviewBuild(Snapshot(Paragraphs(20)), Small, headerFooter: true);
            var thrown = false;
            build.Changed += () =>
            {
                if (build.Stage != failAt || thrown) return;
                thrown = true;
                throw outOfMemory ? new OutOfMemoryException("uji") : new InvalidOperationException("uji");
            };

            Assert.Null(Record.Exception(build.Start));
            Assert.True(UiPump.Until(() => build.Stage == PreviewStage.Failed, Patience), $"tahap {build.Stage}");
            UiPump.For(TimeSpan.FromMilliseconds(100));

            Assert.True(thrown);
            Assert.Equal(outOfMemory ? typeof(OutOfMemoryException) : typeof(InvalidOperationException), build.Error!.GetType());
            Assert.Null(Record.Exception(build.Dispose));
        });
        Assert.Equal(unhandledBefore, WpfHost.Unhandled.Count);
    }

    [Fact]
    public void SubscriberThatAlwaysThrows_DoesNotLoopOrEscape()
    {
        var unhandledBefore = WpfHost.Unhandled.Count;
        Sta(() =>
        {
            var build = new PreviewBuild(Snapshot(Paragraphs(10)), Small, headerFooter: false);
            var calls = 0;
            build.Changed += () => { calls++; throw new InvalidOperationException("selalu gagal"); };

            Assert.Null(Record.Exception(build.Start));
            UiPump.For(TimeSpan.FromMilliseconds(300));

            Assert.Equal(PreviewStage.Failed, build.Stage);
            Assert.InRange(calls, 1, 10);
            build.Dispose();
        });
        Assert.Equal(unhandledBefore, WpfHost.Unhandled.Count);
    }

    // ---- Dispose saat Rendering: paket baru ditutup setelah penulis berhenti ----

    [Fact]
    public void Dispose_WhileRendering_KeepsThePackageUntilTheWriterEnds_ThenReleasesIt_WithoutDispatcherErrors()
    {
        var unhandledBefore = WpfHost.Unhandled.Count;
        Sta(() =>
        {
            PreviewBuild? build = null;
            Uri? uri = null;
            var disposed = false;
            build = new PreviewBuild(Snapshot(Paragraphs(120)), Small, headerFooter: true);
            build.Changed += () =>
            {
                if (disposed || build!.Stage != PreviewStage.Rendering || build.RenderedPages < 2) return;
                disposed = true;
                build.Dispose();
                uri = PackageUriOf(build);
                // Penutupan tidak boleh sinkron: callback serializer yang tersisa masih menulis ke paket.
                Assert.NotNull(PackageStore.GetPackage(uri));
            };

            build.Start();
            Assert.True(UiPump.Until(() => disposed, Patience));
            UiPump.For(TimeSpan.FromMilliseconds(400));

            Assert.True(UiPump.Until(() => PackageStore.GetPackage(uri!) is null, TimeSpan.FromSeconds(5)), "paket tidak dilepas setelah penulisan dibatalkan");
        });
        Assert.Equal(unhandledBefore, WpfHost.Unhandled.Count);
    }

    static object? PrivateField(PreviewBuild build, string name) =>
        typeof(PreviewBuild).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(build);

    [Fact]
    public void Dispose_WhileRendering_ArmsAFallbackTimer_ThatIsGoneOnceTheWriterEndsAndThePackageIsReleased()
    {
        var unhandledBefore = WpfHost.Unhandled.Count;
        Sta(() =>
        {
            PreviewBuild? build = null;
            var armed = false;
            build = new PreviewBuild(Snapshot(Paragraphs(120)), Small, headerFooter: true);
            build.Changed += () =>
            {
                if (armed || build!.Stage != PreviewStage.Rendering || build.RenderedPages < 1) return;
                armed = true;
                build.Dispose();
                Assert.True(build.CleanupFallbackTimer is { IsEnabled: true }, "penulis masih jalan: timer cadangan harus aktif");
            };

            build.Start();
            Assert.True(UiPump.Until(() => armed, Patience));
            var uri = PackageUriOf(build)!;
            Assert.True(UiPump.Until(() => PackageStore.GetPackage(uri) is null, TimeSpan.FromSeconds(5)), "paket tidak dilepas");

            // Timer cadangan tidak boleh tertinggal berdetak setelah pembersihan (menahan FlowDocument, AST, dan teks).
            Assert.True(build.CleanupFallbackTimer is null or { IsEnabled: false }, "timer cadangan masih aktif setelah pembersihan");
        });
        Assert.Equal(unhandledBefore, WpfHost.Unhandled.Count);
    }

    [Fact]
    public void Dispose_WhenCleanupIsAlreadyScheduledDuringCancel_DoesNotArmAFallbackTimerThatNobodyStops()
    {
        var unhandledBefore = WpfHost.Unhandled.Count;
        Sta(() =>
        {
            PreviewBuild? build = null;
            var disposed = false;
            build = new PreviewBuild(Snapshot(Paragraphs(120)), Small, headerFooter: true);
            build.Changed += () =>
            {
                if (disposed || build!.Stage != PreviewStage.Rendering || build.RenderedPages < 1) return;
                disposed = true;
                // Meniru CancelAsync yang memicu WritingCancelled/WritingCompleted sinkron: ScheduleCleanup sudah menandai
                // cleanupScheduled sebelum Dispose sampai ke keputusan membuat timer cadangan.
                typeof(PreviewBuild).GetField("cleanupScheduled", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(build, true);
                build.Dispose();
            };

            build.Start();
            Assert.True(UiPump.Until(() => disposed, Patience));
            UiPump.For(TimeSpan.FromMilliseconds(400)); // sisa callback penulis yang dibatalkan

            Assert.True(build.CleanupFallbackTimer is null or { IsEnabled: false }, "timer cadangan dibuat padahal pembersihan sudah dijadwalkan");

            // Pembersihan yang "sudah dijadwalkan" di atas hanya bendera; jalankan sendiri supaya paket tidak yatim.
            typeof(PreviewBuild).GetMethod("Cleanup", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(build, null);
            Assert.Null(PackageStore.GetPackage(PackageUriOf(build)!));
        });
        Assert.Equal(unhandledBefore, WpfHost.Unhandled.Count);
    }

    [Fact]
    public void RepeatedCycles_DisposedWhileRendering_LeaveNoDispatcherErrorsAndNoOrphanPackages()
    {
        var unhandledBefore = WpfHost.Unhandled.Count;
        Sta(() =>
        {
            var uris = new List<Uri>();
            for (var i = 0; i < 8; i++)
            {
                var build = StartBuild(Paragraphs(80));
                Assert.True(UiPump.Until(() => build.Stage == PreviewStage.Ready || (build.Stage == PreviewStage.Rendering && build.RenderedPages >= 1), Patience));
                uris.Add(PackageUriOf(build)!);
                build.Dispose(); // siklus berikutnya dimulai selagi penulis yang dibatalkan belum selesai
            }

            UiPump.For(TimeSpan.FromMilliseconds(400)); // sisa callback penulis/pembersihan
            Assert.True(UiPump.Until(() => uris.All(uri => PackageStore.GetPackage(uri) is null), TimeSpan.FromSeconds(10)), "ada paket yatim di PackageStore");
        });
        Assert.Equal(unhandledBefore, WpfHost.Unhandled.Count);
    }

    // ---- Sumber parse bersama / dokumen besar ----

    [Fact]
    public void TwoBuildsFromOneSource_ShareTheParse_ButHaveIndependentDocumentsAndLayouts()
    {
        Sta(() =>
        {
            var source = new PrintSource(Snapshot(Paragraphs(20)));
            var first = new PreviewBuild(source, Small, headerFooter: false);
            var second = new PreviewBuild(source, PageLayout.Default, headerFooter: false);

            first.Start();
            second.Start();

            Assert.NotSame(first.Document, second.Document);
            Assert.Equal(Small.Width, first.Document.PageWidth);
            Assert.Equal(PageLayout.Default.Width, second.Document.PageWidth, 1);
            Assert.Equal(UiPump.TextOf(first.Document), UiPump.TextOf(second.Document));
            first.Dispose();
            second.Dispose();
        });
    }

    // Parse yang ditahan: bukti deterministik bahwa Start tidak menunggu parse (bukan balapan dengan thread latar sungguhan).
    static (PrintSource Source, TaskCompletionSource<Markdig.Syntax.MarkdownDocument> Gate, PrintSnapshot Snapshot) GatedSource(string text)
    {
        var snapshot = Snapshot(text);
        var gate = new TaskCompletionSource<Markdig.Syntax.MarkdownDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        return (new PrintSource(snapshot, gate.Task), gate, snapshot);
    }

    // Selesai di thread pool, seperti Task.Run di PrintSource.
    static void CompleteOnAThreadPoolThread(TaskCompletionSource<Markdig.Syntax.MarkdownDocument> gate, PrintSnapshot snapshot) =>
        Task.Run(() => gate.SetResult(DocumentView.ParsePrintSnapshot(snapshot))).Wait();

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // tanpa SynchronizationContext saat Start: lanjutan tidak boleh bergantung padanya
    public void PendingParse_StartDoesNotBlock_NoDocumentYet_AndTheRestRunsOnTheUiThread(bool withoutSynchronizationContext)
    {
        var unhandledBefore = WpfHost.Unhandled.Count;
        Sta(() =>
        {
            var (source, gate, snapshot) = GatedSource(Paragraphs(30));
            var build = new PreviewBuild(source, Small, headerFooter: true);
            var uiThread = Environment.CurrentManagedThreadId;
            var threads = new List<int>();
            build.Changed += () => threads.Add(Environment.CurrentManagedThreadId);

            var previous = SynchronizationContext.Current;
            if (withoutSynchronizationContext) SynchronizationContext.SetSynchronizationContext(null);
            try { build.Start(); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            UiPump.For(TimeSpan.FromMilliseconds(100));

            Assert.False(source.Parsed.IsCompleted);
            Assert.Null(build.Document); // belum ada dokumen: tidak ada yang menunggu parse
            Assert.Equal(PreviewStage.Paginating, build.Stage);
            Assert.Empty(threads);

            CompleteOnAThreadPoolThread(gate, snapshot);
            WaitForEnd(build);

            Assert.Equal(PreviewStage.Ready, build.Stage);
            Assert.NotNull(build.Document);
            Assert.NotEmpty(threads);
            Assert.All(threads, id => Assert.Equal(uiThread, id));
            build.Dispose();
        });
        Assert.Equal(unhandledBefore, WpfHost.Unhandled.Count);
    }

    [Fact]
    public void PendingParse_ThatFails_FailsTheBuildOnTheUiThread()
    {
        var unhandledBefore = WpfHost.Unhandled.Count;
        Sta(() =>
        {
            var (source, gate, _) = GatedSource(Paragraphs(3));
            var build = new PreviewBuild(source, Small, headerFooter: false);
            var uiThread = Environment.CurrentManagedThreadId;
            var failedOn = new List<int>();
            build.Changed += () => failedOn.Add(Environment.CurrentManagedThreadId);
            build.Start();

            Task.Run(() => gate.SetException(new InvalidOperationException("parse gagal"))).Wait();
            Assert.True(UiPump.Until(() => build.Stage == PreviewStage.Failed, Patience));

            Assert.IsType<InvalidOperationException>(build.Error);
            Assert.Equal("parse gagal", build.Error!.Message);
            Assert.Equal([uiThread], failedOn);
            build.Dispose();
        });
        Assert.Equal(unhandledBefore, WpfHost.Unhandled.Count);
    }

    [Fact]
    public void PendingParse_DisposedBeforeItFinishes_NeverCreatesADocumentOrRaisesEvents_EvenAfterItCompletes()
    {
        var unhandledBefore = WpfHost.Unhandled.Count;
        Sta(() =>
        {
            var (source, gate, snapshot) = GatedSource(Paragraphs(30));
            var build = new PreviewBuild(source, Small, headerFooter: true);
            var raised = 0;
            build.Changed += () => raised++;
            build.Start();

            build.Dispose();
            CompleteOnAThreadPoolThread(gate, snapshot);
            UiPump.For(TimeSpan.FromMilliseconds(300));

            Assert.Equal(0, raised);
            Assert.Null(build.Document);
            Assert.Equal(PreviewStage.Paginating, build.Stage);
            Assert.Null(build.Pages);
            Assert.Null(PackageUriOf(build));
        });
        Assert.Equal(unhandledBefore, WpfHost.Unhandled.Count);
    }

    [Fact]
    public void LargeDocument_IsParsedInTheBackground_StartDoesNotBlock_AndRemoteImagesStayBlocked()
    {
        Sta(() =>
        {
            var text = Paragraphs(700) + "\n\n![x](https://example.invalid/a.png)\n";
            Assert.True(text.Length >= DocumentView.BackgroundParseChars);
            var source = new PrintSource(Snapshot(text, blockRemote: true));
            var build = new PreviewBuild(source, PageLayout.Default, headerFooter: false);

            build.Start();

            // Parse berjalan di thread latar: Start kembali sebelum selesai, dan belum ada dokumen (bukan hanya "Stage == Paginating",
            // yang juga benar bila parse sudah selesai dan paginasi baru mulai).
            Assert.False(source.Parsed.IsCompleted, "parse selesai sebelum Start kembali: tidak lagi membuktikan parse latar");
            Assert.Null(build.Document);
            Assert.Equal(PreviewStage.Paginating, build.Stage);
            WaitForPaginated(build);
            Assert.NotEqual(PreviewStage.Failed, build.Stage);
            var body = UiPump.TextOf(build.Document);
            Assert.Contains(MarkdownSupport.BlockedRemoteImageText, body);
            Assert.DoesNotContain("example.invalid", body);
            build.Dispose();
        });
    }

    [Fact]
    public void LargeDocument_DisposedBeforeParseFinishes_NeverCreatesADocumentOrRaisesEvents()
    {
        Sta(() =>
        {
            var source = new PrintSource(Snapshot(Paragraphs(700)));
            var build = new PreviewBuild(source, PageLayout.Default, headerFooter: false);
            var raised = 0;
            build.Changed += () => raised++;

            build.Start();
            Assert.False(source.Parsed.IsCompleted, "parse selesai sebelum Dispose: tidak lagi membuktikan pembuangan sebelum parse selesai");
            build.Dispose();
            UiPump.For(TimeSpan.FromMilliseconds(500));

            Assert.True(source.Parsed.IsCompleted); // parse latar memang selesai, tetapi hasilnya tidak dipakai siklus yang dibuang
            Assert.Null(build.Document);
            Assert.Equal(0, raised);
            Assert.Equal(PreviewStage.Paginating, build.Stage);
            Assert.Null(build.Pages);
        });
    }

    // ---- Isi yang aneh ----

    public static TheoryData<string, string> AwkwardDocuments() => new()
    {
        { "emoji", "# 😀 Judul 🎉\n\nTeks dengan 👨‍👩‍👧‍👦 keluarga, 🏳️‍🌈, dan 𝒳 di luar BMP.\n\n- 🍎 satu\n- 🍊 dua\n" },
        { "rtl", "# مرحبا بالعالم\n\nهذا نص عربي طويل " + "كلمة ".PadRight(300, 'x') + "\n\nשלום עולם abc עברית 123\n\n| ا | ב |\n|---|---|\n| 1 | 2 |\n" },
        { "cjk", "# 日本語の見出し\n\n" + string.Concat(Enumerable.Repeat("これは長い日本語の段落です。折り返しは文字単位で行われます。", 30)) + "\n\n中文段落" + new string('字', 400) },
        { "rtl-override", "a \u202E b \u202D c \u2066 d \u2069 \u200F \u200E e\n" },
        { "wide-table", "|" + string.Join("|", Enumerable.Range(1, 30).Select(i => $" kolom{i} ")) + "|\n|" + string.Join("|", Enumerable.Repeat("---", 30)) + "|\n|" + string.Join("|", Enumerable.Range(1, 30).Select(i => $" nilai-{i}-panjang-sekali ")) + "|\n" },
        { "long-code-line", "```\n" + new string('x', 5000) + "\n```\n\nsesudah\n" },
        { "long-inline-code", "`" + new string('y', 3000) + "`\n" },
        { "long-word", "kata " + new string('z', 4000) + " akhir\n" },
        { "long-url", "<https://example.com/" + new string('a', 2000) + ">\n\n[tautan](https://example.com/" + new string('b', 2000) + ")\n" },
        { "deep-nesting", string.Concat(Enumerable.Range(0, 40).Select(i => new string(' ', i * 2) + "- tingkat " + i + "\n")) + string.Concat(Enumerable.Repeat("> ", 30)) + "kutipan dalam\n" },
        { "control-chars", "a\0b\u0001c\u007Fd\u0085e\u2028f\u2029g\uFEFFh\u00ADi\n" },
        { "lone-surrogates", "x\uD83D y \uDE00 z\n\n# \uD800\n" },
        { "html-inline", "<script>alert(1)</script> <b>tebal</b> &amp; &lt; &#x1F600; <!-- komentar --> <img src=\"x\">\n\n<div>blok</div>\n" },
        { "many-headings", string.Concat(Enumerable.Range(1, 200).Select(i => $"## Judul {i}\n\nisi {i}\n\n")) },
        { "setext-and-rules", "Judul\n=====\n\n---\n\n***\n\nSub\n---\n" },
        { "task-list-and-footnote", "- [x] selesai\n- [ ] belum\n\nteks[^1]\n\n[^1]: catatan kaki\n" },
        { "math-and-escapes", "\\*bukan miring\\* $x^2$ \\\\ \\` ~~coret~~ ==tanda== H~2~O\n" },
    };

    [Theory]
    [MemberData(nameof(AwkwardDocuments))]
    public void AwkwardContent_PaginatesAndWritesPages_WithoutFailing(string name, string text)
    {
        Sta(() =>
        {
            var build = StartBuild(text);
            WaitForEnd(build);

            Assert.True(build.Stage == PreviewStage.Ready, $"{name}: {build.Stage} {build.Error}");
            Assert.True(build.PageCount > 0, name);
            Assert.Equal(build.PageCount, build.Pages!.DocumentPaginator.PageCount);
            Assert.Contains($"Halaman 1 dari {build.PageCount}", GlyphTexts(build.Pages, 0));
            build.Dispose();
        });
    }

    [Fact]
    public void LongUnbreakableWord_DoesNotMakeATextLineWiderThanThePage()
    {
        Sta(() =>
        {
            var build = StartBuild("kata " + new string('z', 4000) + " akhir\n");
            WaitForEnd(build);

            var page = build.Pages!.References[0].GetDocument(false).Pages[0].GetPageRoot(false);
            var offenders = new List<string>();
            void Walk(System.Windows.DependencyObject node)
            {
                if (node is Glyphs g && g.OriginX > Small.Width + 1) offenders.Add(g.UnicodeString);
                foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(node))
                    if (child is System.Windows.DependencyObject next) Walk(next);
            }
            Walk(page);

            Assert.Equal(PreviewStage.Ready, build.Stage);
            Assert.Empty(offenders);
            Assert.True(build.PageCount >= 1);
            build.Dispose();
        });
    }
}
