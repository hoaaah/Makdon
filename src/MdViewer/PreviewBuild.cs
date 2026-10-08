using System.IO.Packaging;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Windows.Xps;
using System.Windows.Xps.Packaging;

namespace MdViewer;

public enum PreviewStage { Paginating, Rendering, Ready, Failed }

/// <summary>
/// Satu siklus penyusunan pratinjau: FlowDocument cetak -> paginasi latar (async, UI tetap responsif) -> halaman ditulis
/// ke paket XPS di memori (async) -> <see cref="FixedDocumentSequence"/> untuk DocumentViewer (yang hanya menerima dokumen
/// tetap, bukan FlowDocument). Halaman XPS dihasilkan dari paginator + kaki halaman yang sama dengan jalur cetak, jadi
/// yang tampil sama dengan yang tercetak. Ganti layout/kaki halaman = buat siklus baru dan buang yang lama (<see cref="Dispose"/>).
/// Harus dipakai di thread UI.
/// <para>
/// Semua penangan peristiwa di kelas ini dibungkus <see cref="Guard"/>: galat apa pun (termasuk kehabisan memori) menjadikan
/// siklus <see cref="PreviewStage.Failed"/> dan dicatat, tidak pernah keluar ke dispatcher. Pratinjau tidak mengubah dokumen,
/// jadi galatnya selalu boleh dipulihkan (jendela menampilkan pesan, pengguna bisa menutupnya) dan tidak boleh memicu jalur fatal aplikasi.
/// </para>
/// <para>
/// Keterbatasan: halaman ditulis ke XPS segera setelah paginasi. Gambar http(s) (bila pemblokiran remote dimatikan) dimuat
/// async oleh WPF, jadi bisa saja belum terunduh saat halamannya dibekukan di XPS dan tampil kosong di pratinjau; gambar lokal tidak terpengaruh.
/// </para>
/// </summary>
sealed class PreviewBuild : IDisposable
{
    static int counter;

    // Cadangan bila penulis XPS yang dibatalkan tidak pernah melapor selesai: paket tetap ditutup setelah ini.
    static readonly TimeSpan CleanupFallback = TimeSpan.FromSeconds(10);

    readonly PrintSource source;
    readonly PrintSnapshot snapshot;
    readonly bool headerFooter;
    readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;

    FlowDocument? document;
    DynamicDocumentPaginator? paginator;
    MemoryStream? stream;
    Package? package;
    Uri? packageUri;
    XpsDocument? xps;
    XpsDocumentWriter? writer;
    DispatcherTimer? cleanupFallback;
    bool started;
    bool disposed;
    bool writing;
    bool cleanupScheduled;
    bool cleanedUp;

    public PreviewBuild(PrintSnapshot snapshot, PageLayout layout, bool headerFooter)
        : this(new PrintSource(snapshot), layout, headerFooter)
    {
    }

    /// <param name="source">Hasil parse yang dipakai bersama antarsiklus (ganti layout tidak mem-parse ulang).</param>
    internal PreviewBuild(PrintSource source, PageLayout layout, bool headerFooter)
    {
        this.source = source;
        snapshot = source.Snapshot;
        this.headerFooter = headerFooter;
        Layout = layout;
    }

    public PageLayout Layout { get; }

    public PreviewStage Stage { get; private set; } = PreviewStage.Paginating;

    /// <summary>Selama paginasi: jumlah halaman sejauh ini; sesudahnya: jumlah halaman akhir.</summary>
    public int PageCount { get; private set; }

    /// <summary>Jumlah halaman yang sudah ditulis ke XPS (tahap <see cref="PreviewStage.Rendering"/>).</summary>
    public int RenderedPages { get; private set; }

    /// <summary>Tersedia pada tahap <see cref="PreviewStage.Ready"/>.</summary>
    public FixedDocumentSequence? Pages { get; private set; }

    public Exception? Error { get; private set; }

    /// <summary>Dokumen cetak yang dipaginasi; hanya untuk pengujian. Baru ada setelah <see cref="Start"/> (dan parse latar selesai).</summary>
    internal FlowDocument Document => document!;

    /// <summary>Timer cadangan pembersihan paket; hanya untuk pengujian (harus null/mati begitu pembersihan dijadwalkan).</summary>
    internal DispatcherTimer? CleanupFallbackTimer => cleanupFallback;

    /// <summary>Dipicu pada setiap kemajuan atau pergantian tahap.</summary>
    public event Action? Changed;

    public void Start()
    {
        if (started || disposed) return;
        started = true;
        Guard(() =>
        {
            if (source.Parsed.IsCompleted) BeginPagination();
            else _ = BeginPaginationWhenParsedAsync();
        });
    }

    // Dokumen besar: AST masih diparse di thread latar; lanjut di thread UI begitu selesai. Kembali ke thread UI dilakukan
    // eksplisit lewat dispatcher (bukan bergantung pada SynchronizationContext pemanggil), karena BeginPagination menyentuh
    // FlowDocument dan peristiwa yang harus berjalan di thread UI.
    async Task BeginPaginationWhenParsedAsync()
    {
        Exception? parseError = null;
        try
        {
            await source.Parsed.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            parseError = ex;
        }

        // Tanpa penunggu: bila dispatcher sudah berhenti, BeginInvoke hanya mengembalikan operasi yang dibatalkan (tidak melempar).
        _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            if (disposed || Stage != PreviewStage.Paginating) return;
            if (parseError is not null) Fail(parseError);
            else Guard(BeginPagination);
        });
    }

    void BeginPagination()
    {
        document = source.CreateDocument(Layout);
        paginator = (DynamicDocumentPaginator)((IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.IsBackgroundPaginationEnabled = true;
        paginator.PaginationProgress += OnPaginationProgress;
        paginator.PaginationCompleted += OnPaginationCompleted;
        // Tanpa viewer yang menampung FlowDocument, akses PageCount-lah yang menjadwalkan paginasi latar.
        // Untuk dokumen pendek/kosong PaginationCompleted bisa terpicu sinkron di sini; BeginRender menjaga dirinya dari panggilan ganda.
        PageCount = paginator.PageCount;
        if (paginator.IsPageCountValid) BeginRender();
        else Changed?.Invoke();
    }

    void OnPaginationProgress(object? sender, PaginationProgressEventArgs e) => Guard(() =>
    {
        if (disposed || Stage != PreviewStage.Paginating) return;
        PageCount = paginator!.PageCount;
        Changed?.Invoke();
    });

    void OnPaginationCompleted(object? sender, EventArgs e) => Guard(() =>
    {
        if (paginator!.IsPageCountValid) BeginRender();
    });

    void BeginRender()
    {
        if (disposed || Stage != PreviewStage.Paginating) return;
        PageCount = paginator!.PageCount;
        Stage = PreviewStage.Rendering;
        Changed?.Invoke();
        if (disposed) return;

        stream = new MemoryStream();
        package = Package.Open(stream, FileMode.Create, FileAccess.ReadWrite);
        packageUri = new Uri($"pack://mdviewer-preview-{Interlocked.Increment(ref counter)}.xps");
        PackageStore.AddPackage(packageUri, package);
        xps = new XpsDocument(package, CompressionOption.SuperFast, packageUri.AbsoluteUri);
        writer = XpsDocument.CreateXpsDocumentWriter(xps);
        writer.WritingProgressChanged += OnWritingProgress;
        writer.WritingCompleted += OnWritingCompleted;
        writer.WritingCancelled += OnWritingCancelled;
        writing = true;
        writer.WriteAsync(PrintService.CreatePaginator(document!, snapshot.Title, headerFooter));
    }

    void OnWritingProgress(object? sender, System.Windows.Documents.Serialization.WritingProgressChangedEventArgs e) => Guard(() =>
    {
        if (disposed || Stage != PreviewStage.Rendering) return;
        RenderedPages = e.Number;
        Changed?.Invoke();
    });

    void OnWritingCompleted(object? sender, System.Windows.Documents.Serialization.WritingCompletedEventArgs e)
    {
        writing = false;
        if (disposed)
        {
            ScheduleCleanup(); // penulisan yang dibatalkan sudah berhenti; sekarang aman menutup paket
            return;
        }

        Guard(() =>
        {
            if (Stage != PreviewStage.Rendering || e.Cancelled) return;
            if (e.Error is not null)
            {
                Fail(e.Error);
                return;
            }

            Pages = xps!.GetFixedDocumentSequence();
            Stage = PreviewStage.Ready;
            Changed?.Invoke();
        });
    }

    void OnWritingCancelled(object? sender, System.Windows.Documents.Serialization.WritingCancelledEventArgs e)
    {
        writing = false;
        if (disposed) ScheduleCleanup();
    }

    // Membungkus setiap jalur masuk dari dispatcher/peristiwa. Tidak ada galat yang boleh lolos dari sini.
    void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            if (disposed || Stage == PreviewStage.Failed) CrashLog.Write("Galat susulan pada penyusunan pratinjau cetak", ex);
            else Fail(ex);
        }
    }

    // Tidak pernah melempar: dipanggil dari dalam penanganan galat.
    void Fail(Exception ex)
    {
        CrashLog.Write("Penyusunan pratinjau cetak gagal", ex);
        Error = ex;
        Stage = PreviewStage.Failed;
        if (writing && writer is not null)
        {
            try { writer.CancelAsync(); }
            catch (Exception cancelEx) { CrashLog.Write("Gagal membatalkan penulisan XPS", cancelEx); }
        }

        try { Changed?.Invoke(); }
        catch (Exception subscriberEx) { CrashLog.Write("Penerima kabar pratinjau cetak gagal", subscriberEx); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Changed = null;

        if (paginator is not null)
        {
            paginator.PaginationProgress -= OnPaginationProgress;
            paginator.PaginationCompleted -= OnPaginationCompleted;
            // Tanpa ini paginasi latar dokumen besar terus berjalan di thread UI walau siklusnya sudah dibuang.
            try { paginator.IsBackgroundPaginationEnabled = false; }
            catch (Exception ex) { CrashLog.Write("Gagal menghentikan paginasi latar", ex); }
        }

        Pages = null;

        if (writing && writer is not null)
        {
            // CancelAsync hanya menandai batal; penulis masih bisa menjalankan callback serializer berikutnya. Paket tidak boleh
            // ditutup sebelum WritingCancelled/WritingCompleted tiba (OnWritingCancelled/OnWritingCompleted -> ScheduleCleanup).
            try
            {
                writer.CancelAsync();
                // CancelAsync bisa saja sudah memicu WritingCancelled/WritingCompleted secara sinkron (ScheduleCleanup sudah jalan);
                // timer yang dibuat sesudahnya tak akan pernah dihentikan siapa pun dan menahan seluruh PreviewBuild di memori.
                if (!cleanupScheduled)
                {
                    var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = CleanupFallback };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop(); // satu kali saja, apa pun keadaan ScheduleCleanup
                        ScheduleCleanup();
                    };
                    cleanupFallback = timer;
                    timer.Start();
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write("Gagal membatalkan penulisan XPS", ex);
                writing = false;
                ScheduleCleanup();
            }
        }
        else
        {
            ScheduleCleanup();
        }
    }

    // Penutupan paket ditunda sampai dispatcher idle: DocumentViewer memuat halaman XPS (PageContent) secara async lewat
    // pack:// URI; menutup paket lebih awal membuat pemuatan yang sudah antre melempar UriFormatException di dispatcher.
    void ScheduleCleanup()
    {
        if (cleanupScheduled) return;
        cleanupScheduled = true;
        cleanupFallback?.Stop();
        cleanupFallback = null;
        if (dispatcher.HasShutdownStarted) Cleanup();
        else dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, Cleanup);
    }

    // Idempoten; tiap langkah terpisah supaya satu kegagalan tidak membiarkan sisanya tertinggal.
    void Cleanup()
    {
        if (cleanedUp) return;
        cleanedUp = true;

        cleanupFallback?.Stop();
        cleanupFallback = null;

        if (writer is not null)
        {
            writer.WritingProgressChanged -= OnWritingProgress;
            writer.WritingCompleted -= OnWritingCompleted;
            writer.WritingCancelled -= OnWritingCancelled;
        }

        Step(() => xps?.Close());
        Step(() => { if (packageUri is not null) PackageStore.RemovePackage(packageUri); });
        Step(() => package?.Close());
        Step(() => stream?.Dispose());

        static void Step(Action action)
        {
            try { action(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { CrashLog.Write("Pembersihan pratinjau cetak", ex); }
        }
    }
}
