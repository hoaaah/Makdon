using System.ComponentModel;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

namespace Makdon;

/// <summary>
/// Pratinjau Cetak modal: halaman hasil paginasi dokumen cetak (kertas putih, tema Terang) ditampilkan di DocumentViewer.
/// Dokumen dibuat dari <see cref="PrintSnapshot"/> saat jendela dibuka, jadi editor tidak terkunci dan pratinjau tidak berubah
/// bila teks diedit sesudahnya. Penyusunan (<see cref="PreviewBuild"/>) berjalan async dengan indikator "Menyusun halaman...";
/// halaman baru ditampilkan setelah paginasi selesai supaya "Halaman X dari N" di kaki halaman sudah diketahui.
/// Mengubah layout membuang siklus penyusunan lama dan memulai yang baru dari snapshot yang sama.
/// </summary>
public partial class PrintPreviewWindow : Window
{
    static readonly double[] ZoomSteps = [25, 50, 75, 100, 125, 150, 200, 300, 400];

    enum ZoomMode { FitPage, FitWidth, Actual, Custom }

    readonly PrintSnapshot snapshot;
    readonly PrintSource source;
    readonly DependencyPropertyDescriptor? pageDescriptor =
        DependencyPropertyDescriptor.FromProperty(DocumentViewerBase.MasterPageNumberProperty, typeof(DocumentViewer));
    readonly DependencyPropertyDescriptor? zoomDescriptor =
        DependencyPropertyDescriptor.FromProperty(DocumentViewer.ZoomProperty, typeof(DocumentViewer));

    PreviewBuild? build;
    ScrollViewer? scroller;
    PaperKind paper = PaperKind.A4;
    PrintOrientation orientation = PrintOrientation.Portrait;
    MarginPreset margin = MarginPreset.Normal;
    ZoomMode zoomMode = ZoomMode.FitPage;
    bool printing;
    int? goToTarget;
    double? goToOffset;
    double goToZoom;
    bool? lastCanPrint;
    Exception? uiError;

    public PrintPreviewWindow(PrintSnapshot snapshot)
        : this(snapshot, new PrintSource(snapshot)) // parse sekali (dokumen besar di thread latar); dipakai ulang tiap ganti layout
    {
    }

    /// <param name="source">Sumber parse yang sudah dibuat pemanggil (pengujian memakai sumber yang parse-nya bisa ditahan).</param>
    internal PrintPreviewWindow(PrintSnapshot snapshot, PrintSource source)
    {
        InitializeComponent();
        this.snapshot = snapshot;
        this.source = source;
        Title = $"Pratinjau Cetak — {snapshot.Title}";

        // Cetak dari viewer (Ctrl+P saat fokus di halaman) harus lewat jalur kita, bukan pencetakan bawaan DocumentViewer.
        Viewer.CommandBindings.Add(new CommandBinding(ApplicationCommands.Print, (_, e) => { PrintDocument(); e.Handled = true; },
            (_, e) => { e.CanExecute = CanPrint; e.Handled = true; }));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Print, (_, _) => PrintDocument(), (_, e) => e.CanExecute = CanPrint));

        pageDescriptor?.AddValueChanged(Viewer, OnViewerChanged);
        zoomDescriptor?.AddValueChanged(Viewer, OnViewerChanged);
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Viewer.ApplyTemplate();
        scroller = Viewer.Template?.FindName("PART_ContentHost", Viewer) as ScrollViewer;
        if (scroller is not null) scroller.ScrollChanged += OnViewerChanged;
        Loaded += (_, _) => Viewer.Focus();
        Closed += OnClosed;

        StartBuild();
    }

    /// <summary>Layout halaman yang sedang dipilih (kertas + orientasi + margin).</summary>
    public PageLayout Layout => PageLayout.For(paper, orientation, margin);

    /// <summary>true selama halaman masih disusun (dipaginasi/ditulis); halaman belum ditampilkan.</summary>
    public bool IsBusy => build is { Stage: PreviewStage.Paginating or PreviewStage.Rendering };

    /// <summary>true bila penyusunan gagal (pesan ada di jendela; rincian di crash.log).</summary>
    public bool HasFailed => uiError is not null || build is { Stage: PreviewStage.Failed };

    /// <summary>Tahap siklus penyusunan yang sedang berjalan; hanya untuk pengujian (mis. menunggu paginasi tanpa menunggu penulisan XPS).</summary>
    internal PreviewStage? Stage => build?.Stage;

    /// <summary>Jumlah halaman hasil paginasi (0 selama paginasi belum selesai).</summary>
    public int PageCount => build is { Stage: PreviewStage.Rendering or PreviewStage.Ready } ? build.PageCount : 0;

    /// <summary>Dokumen cetak yang dipaginasi; hanya untuk pengujian.</summary>
    internal FlowDocument PrintedDocument => build!.Document;

    /// <summary>Teks "Halaman X dari N" seperti pada bilah navigasi.</summary>
    public string PageStatusText => PageCount == 0 ? "" : $"Halaman {PageBox.Text} dari {PageCount}";

    bool CanPrint => build is { Stage: PreviewStage.Ready, Pages: not null } && !printing && uiError is null;

    // ---- Penyusunan ----

    // Membuang siklus lama (membatalkan paginasi/penulisan yang berjalan) lalu memulai yang baru dari snapshot yang sama.
    // Dipanggil dari penangan Checked/Click: galat apa pun di sini tidak boleh keluar ke dispatcher (jalur galat fatal aplikasi);
    // pratinjau tidak mengubah dokumen, jadi cukup ditampilkan di panel dan dicatat.
    void StartBuild()
    {
        try
        {
            uiError = null;
            ClearGoToTarget();
            Viewer.Document = null;
            build?.Dispose();
            build = new PreviewBuild(source, Layout, FooterCheck.IsChecked == true);
            build.Changed += OnBuildChanged;
            OnBuildChanged();
            build.Start();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    void ShowFailure(Exception ex)
    {
        CrashLog.Write("Pratinjau cetak gagal", ex);
        uiError = ex;
        try
        {
            ShowBusy("Pratinjau tidak dapat disusun.", Describe(ex));
            UpdateNavigation();
        }
        catch (Exception inner)
        {
            CrashLog.Write("Pratinjau cetak gagal menampilkan galat", inner);
        }
    }

    static string Describe(Exception ex) =>
        ex is OutOfMemoryException ? "Memori tidak cukup untuk menyusun pratinjau dokumen ini." : ex.Message;

    void OnBuildChanged()
    {
        try
        {
            ApplyBuildState();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    void ApplyBuildState()
    {
        if (build is null) return;
        switch (build.Stage)
        {
            case PreviewStage.Paginating:
                ShowBusy("Menyusun halaman...", build.PageCount > 0 ? $"{build.PageCount} halaman sejauh ini" : "");
                break;
            case PreviewStage.Rendering:
                ShowBusy("Menyusun pratinjau...", $"Halaman {build.RenderedPages} dari {build.PageCount}");
                break;
            case PreviewStage.Failed:
                ShowBusy("Pratinjau tidak dapat disusun.", build.Error is { } error ? Describe(error) : "");
                break;
            case PreviewStage.Ready:
                if (!printing) BusyPanel.Visibility = Visibility.Collapsed;
                if (!ReferenceEquals(Viewer.Document, build.Pages))
                {
                    Viewer.Document = build.Pages;
                    // Zoom "muat" butuh ukuran viewport; tunggu tata letak.
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ApplyZoomMode);
                }
                break;
        }
        UpdateNavigation();
    }

    void ShowBusy(string text, string detail)
    {
        BusyText.Text = text;
        BusyDetail.Text = detail;
        BusyPanel.Visibility = Visibility.Visible;
    }

    // ---- Pengaturan halaman ----

    void Orientation_Checked(object sender, RoutedEventArgs e)
    {
        if (build is null) return; // dipicu IsChecked awal dari XAML saat InitializeComponent
        var value = LandscapeButton.IsChecked == true ? PrintOrientation.Landscape : PrintOrientation.Portrait;
        if (value == orientation) return;
        orientation = value;
        StartBuild();
    }

    void Paper_Checked(object sender, RoutedEventArgs e)
    {
        if (build is null) return; // dipicu IsChecked awal dari XAML saat InitializeComponent
        var value = LetterButton.IsChecked == true ? PaperKind.Letter : PaperKind.A4;
        if (value == paper) return;
        paper = value;
        StartBuild();
    }

    void Margin_Checked(object sender, RoutedEventArgs e)
    {
        if (build is null) return; // dipicu IsChecked awal dari XAML saat InitializeComponent
        var value = NarrowMarginButton.IsChecked == true ? MarginPreset.Narrow
            : WideMarginButton.IsChecked == true ? MarginPreset.Wide
            : MarginPreset.Normal;
        if (value == margin) return;
        margin = value;
        StartBuild();
    }

    // Kaki halaman ikut tertulis di halaman XPS, jadi penyusunan diulang.
    void Footer_Changed(object sender, RoutedEventArgs e)
    {
        if (build is not null) StartBuild();
    }

    // ---- Navigasi halaman ----

    // MasterPageNumber = halaman pertama yang tampak. Di ujung gulir halaman terakhir tak bisa mencapai puncak viewport
    // (halaman sebelumnya masih tampak di atasnya), jadi di sana yang dilaporkan adalah halaman terakhir.
    // Pengecualian: tujuan navigasi kita sendiri (Berikutnya/Sebelumnya/kotak halaman). Pada zoom kecil atau dokumen pendek
    // beberapa halaman tampak sekaligus dan gulir mentok sebelum halaman tujuan mencapai puncak (MasterPageNumber tetap halaman
    // lama), jadi tanpa ini halaman tujuan yang bukan terakhir dilaporkan sebagai halaman terakhir: Berikutnya melompat ke akhir
    // dan Sebelumnya tak berefek. Tujuan berlaku sampai posisi gulir/zoom berubah dari posisi hasil navigasi itu (pengguna
    // menggulir sendiri), lalu aturan di atas berlaku lagi.
    int CurrentPage
    {
        get
        {
            if (PageCount == 0) return 1;
            if (GoToTargetStillApplies(out var target)) return target;
            if (scroller is { ScrollableHeight: > 0 } s && s.VerticalOffset >= s.ScrollableHeight - 1) return PageCount;
            return Viewer.MasterPageNumber is > 0 and var page ? Math.Min(page, PageCount) : 1;
        }
    }

    void OnViewerChanged(object? sender, EventArgs e) => UpdateNavigation();

    void UpdateNavigation()
    {
        var hasPages = PageCount > 0 && Viewer.Document is not null;
        var current = CurrentPage;
        if (!PageBox.IsKeyboardFocused) PageBox.Text = hasPages ? current.ToString() : "";
        PageBox.IsEnabled = hasPages;
        PageCountText.Text = hasPages ? $"dari {PageCount}" : "";
        FirstPageButton.IsEnabled = PreviousPageButton.IsEnabled = hasPages && current > 1;
        NextPageButton.IsEnabled = LastPageButton.IsEnabled = hasPages && current < PageCount;
        ZoomText.Text = $"{Math.Round(Viewer.Zoom):0}%";
        ZoomInButton.IsEnabled = ZoomOutButton.IsEnabled = hasPages;
        PrintButton.IsEnabled = CanPrint;
        // CanPrint hanya berubah di titik tertentu; jangan memaksa evaluasi ulang seluruh perintah pada setiap kabar kemajuan.
        if (lastCanPrint != CanPrint)
        {
            lastCanPrint = CanPrint;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    void FirstPage_Click(object sender, RoutedEventArgs e)
    {
        ClearGoToTarget();
        Viewer.FirstPage();
    }

    void GoTo(int page)
    {
        goToTarget = page;
        goToOffset = null; // posisi hasil navigasi baru diketahui setelah tata letak selesai
        Viewer.GoToPage(page);
        // Bila gulir sudah mentok tidak ada ScrollChanged yang memperbarui kotak halaman; lakukan sendiri.
        UpdateNavigation();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            if (goToTarget != page || goToOffset is not null) return;
            goToOffset = scroller?.VerticalOffset ?? 0;
            goToZoom = Viewer.Zoom;
            UpdateNavigation();
        });
    }

    void ClearGoToTarget()
    {
        goToTarget = null;
        goToOffset = null;
    }

    bool GoToTargetStillApplies(out int target)
    {
        target = goToTarget ?? 0;
        if (goToTarget is null || target > PageCount) return false;
        if (goToOffset is not { } offset) return true; // belum selesai bergulir
        return scroller is not null && Math.Abs(scroller.VerticalOffset - offset) < 1 && Viewer.Zoom == goToZoom;
    }

    // Relatif terhadap CurrentPage (yang dilaporkan kotak halaman), bukan MasterPageNumber: di ujung gulir keduanya bisa berbeda
    // dan Viewer.PreviousPage() akan melompati satu halaman.
    void PreviousPage_Click(object sender, RoutedEventArgs e)
    {
        if (PageCount > 0) GoTo(Math.Max(1, CurrentPage - 1));
    }

    void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if (PageCount > 0) GoTo(Math.Min(PageCount, CurrentPage + 1));
    }

    void LastPage_Click(object sender, RoutedEventArgs e)
    {
        ClearGoToTarget();
        Viewer.LastPage();
    }

    void PageBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        // Kotak berfokus sehingga UpdateNavigation tidak menulisnya, dan GoToPage baru berlaku setelah tata letak: isi dengan
        // tujuan (sudah diklem) sekarang, bukan CurrentPage yang masih nomor lama.
        if (PageCount > 0 && int.TryParse(PageBox.Text.Trim(), out var page))
        {
            var target = Math.Clamp(page, 1, PageCount);
            GoTo(target);
            PageBox.Text = target.ToString();
        }
        else
        {
            PageBox.Text = CurrentPage.ToString();
        }
        PageBox.SelectAll();
    }

    void PageBox_LostFocus(object sender, RoutedEventArgs e) => UpdateNavigation();

    // ---- Zoom ----

    void Zoom_Checked(object sender, RoutedEventArgs e)
    {
        if (build is null) return;
        zoomMode = ((FrameworkElement)sender).Tag switch
        {
            "FitWidth" => ZoomMode.FitWidth,
            "Actual" => ZoomMode.Actual,
            _ => ZoomMode.FitPage,
        };
        ApplyZoomMode();
    }

    void ApplyZoomMode()
    {
        if (Viewer.Document is null) return;
        ClearGoToTarget(); // zoom berubah = posisi gulir berubah; tujuan navigasi lama tak lagi menggambarkan posisi
        switch (zoomMode)
        {
            case ZoomMode.FitPage: SetZoomFit(wholePage: true); break;
            case ZoomMode.FitWidth: SetZoomFit(wholePage: false); break;
            case ZoomMode.Actual: Viewer.Zoom = 100; break;
        }
        UpdateNavigation();
    }

    // Zoom "muat" dihitung sendiri dari ukuran viewer dan halaman (FitToHeight bawaan viewer meleset ~4%, halaman terpotong).
    void SetZoomFit(bool wholePage)
    {
        const double gap = 28;       // spasi halaman + bayangan di sekitar kertas
        const double scrollBar = 18; // lebar bilah gulir vertikal (muncul saat halaman lebih tinggi dari viewport)
        var layout = build?.Layout ?? Layout;
        var width = Viewer.ActualWidth - gap - scrollBar;
        var height = Viewer.ActualHeight - gap;
        if (width <= 0 || height <= 0) return;

        var scale = wholePage ? Math.Min(width / layout.Width, height / layout.Height) : width / layout.Width;
        Viewer.Zoom = Math.Clamp(Math.Floor(scale * 100), 10, 400);
    }

    // Mode "muat" mengikuti ukuran jendela (viewer menghitung zoom sekali saat dipanggil).
    void Viewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (zoomMode is ZoomMode.FitPage or ZoomMode.FitWidth) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ApplyZoomMode);
    }

    void ZoomIn_Click(object sender, RoutedEventArgs e) => StepZoom(+1);

    void ZoomOut_Click(object sender, RoutedEventArgs e) => StepZoom(-1);

    void StepZoom(int direction)
    {
        var zoom = Viewer.Zoom;
        Viewer.Zoom = direction > 0
            ? ZoomSteps.FirstOrDefault(s => s > zoom + 0.5, ZoomSteps[^1])
            : ZoomSteps.LastOrDefault(s => s < zoom - 0.5, ZoomSteps[0]);
        SetCustomZoom();
    }

    // Ctrl+roda mouse mengubah zoom lewat viewer; tidak ada lagi mode "muat" yang berlaku.
    void Viewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control) SetCustomZoom();
    }

    void SetCustomZoom()
    {
        ClearGoToTarget();
        zoomMode = ZoomMode.Custom;
        FitPageButton.IsChecked = FitWidthButton.IsChecked = ActualSizeButton.IsChecked = false;
        UpdateNavigation();
    }

    // ---- Cetak & tutup ----

    void Print_Click(object sender, RoutedEventArgs e) => PrintDocument();

    /// <summary>Hanya untuk pengujian: pengganti <c>PrintDialog.ShowDialog</c> (false = batal), supaya jalur Cetak teramati tanpa dialog/printer.</summary>
    internal Func<bool?>? ShowPrintDialogForTests { get; set; }

    void PrintDocument()
    {
        if (!CanPrint) return;
        printing = true;
        PrintButton.IsEnabled = false;
        try
        {
            var dialog = new PrintDialog();
            bool? accepted;
            if (ShowPrintDialogForTests is { } fake)
            {
                accepted = fake(); // tanpa menyentuh printer (membaca tiket printer lambat)
            }
            else
            {
                ApplyTicket(dialog);
                accepted = dialog.ShowDialog();
            }
            if (accepted != true) return;
            if (!ConfirmPaperMatchesPreview(dialog)) return;

            ShowBusy("Mencetak...", "");
            // Beri kesempatan indikator tergambar sebelum pencetakan (sinkron) memblokir thread UI.
            Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

            // Yang dicetak adalah halaman yang tampil (paket XPS yang sama), bukan hasil penyusunan ulang.
            dialog.PrintDocument(build!.Pages!.DocumentPaginator, snapshot.Title);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Driver/antrean printer dapat melempar berbagai jenis pengecualian (PrintQueueException, COMException, ...).
            MessageBox.Show(this, $"Gagal mencetak.\n\n{ex.Message}", "Makdon", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            printing = false;
            BusyPanel.Visibility = Visibility.Collapsed;
            OnBuildChanged();
        }
    }

    // Halaman pratinjau berukuran tetap (XPS). Bila pengguna mengganti kertas/orientasi di dialog Cetak, pilihannya tidak ditimpa
    // diam-diam dan juga tidak dipaksakan ke printer tanpa tanya: pengguna memilih mencetak sesuai pratinjau atau kembali mengubah
    // pratinjau (membangun ulang halaman dari pilihan dialog tidak bisa dilakukan di sini - dialog sudah ditutup dan kertas lain
    // seperti Legal/A5 tak punya preset di pratinjau).
    bool ConfirmPaperMatchesPreview(PrintDialog dialog)
    {
        System.Printing.PrintTicket? ticket;
        try { ticket = dialog.PrintTicket; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return true; } // tanpa printer: dialog Cetak sendiri yang melaporkan
        if (ticket is null || TicketMatches(ticket, paper, orientation)) return true;

        var chosen = DescribeTicket(ticket);
        var shown = $"{(paper == PaperKind.Letter ? "Letter" : "A4")}, {(orientation == PrintOrientation.Landscape ? "lanskap" : "potret")}";
        var answer = ChoiceDialog.Show(this, "Cetak",
            $"Kertas atau orientasi di dialog Cetak ({chosen}) berbeda dari pratinjau ({shown})."+"\n\n" +
            "Halaman pratinjau berukuran tetap, jadi akan dicetak apa adanya dengan ukuran pratinjau. " +
            "Untuk mencetak dengan kertas atau orientasi lain, batalkan lalu ubah di jendela pratinjau.",
            false,
            new DialogChoice<bool>("Cetak sesuai pratinjau", true),
            new DialogChoice<bool>("Batal", false, IsDefault: true));
        if (!answer) return false;

        ApplyTicket(ticket, paper, orientation);
        return true;
    }

    // Menyamakan kertas/orientasi tiket printer dengan pratinjau. Tanpa printer terpasang tiket tidak tersedia: abaikan,
    // dialog Cetak sendiri yang akan melaporkannya.
    void ApplyTicket(PrintDialog dialog)
    {
        try
        {
            if (dialog.PrintTicket is { } ticket) ApplyTicket(ticket, paper, orientation);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }

    // Ukuran eksplisit (1/96 inci) supaya tidak bergantung pada tabel ukuran kertas driver.
    internal static void ApplyTicket(System.Printing.PrintTicket ticket, PaperKind paper, PrintOrientation orientation)
    {
        var (width, height) = MediaSize(paper);
        ticket.PageMediaSize = new System.Printing.PageMediaSize(
            paper == PaperKind.Letter ? System.Printing.PageMediaSizeName.NorthAmericaLetter : System.Printing.PageMediaSizeName.ISOA4, width, height);
        ticket.PageOrientation = orientation == PrintOrientation.Landscape
            ? System.Printing.PageOrientation.Landscape
            : System.Printing.PageOrientation.Portrait;
    }

    static (double Width, double Height) MediaSize(PaperKind paper) =>
        paper == PaperKind.Letter ? (816d, 1056d) : (793.7, 1122.5);

    /// <summary>
    /// true bila tiket (pilihan pengguna di dialog Cetak) sama dengan kertas+orientasi pratinjau. Nilai yang tidak diisi tiket
    /// dianggap cocok (tak ada yang bertentangan). Ukuran dibandingkan lewat nama atau lebar/tinggi (toleransi 4 DIP, ukuran tiket
    /// selalu potret).
    /// </summary>
    internal static bool TicketMatches(System.Printing.PrintTicket ticket, PaperKind paper, PrintOrientation orientation)
    {
        if (ticket.PageOrientation is { } actual && actual != System.Printing.PageOrientation.Unknown)
        {
            var landscape = actual is System.Printing.PageOrientation.Landscape or System.Printing.PageOrientation.ReverseLandscape;
            if (landscape != (orientation == PrintOrientation.Landscape)) return false;
        }

        if (ticket.PageMediaSize is { } size)
        {
            // Varian "Rotated" (nama yang dipakai driver untuk kertas yang sama dalam orientasi terputar) tanpa lebar/tinggi eksplisit
            // tetap kertas yang sama; orientasinya sudah diperiksa di atas. Tanpa ini dialog konfirmasi muncul di setiap Cetak.
            var (expectedName, rotatedName) = paper == PaperKind.Letter
                ? (System.Printing.PageMediaSizeName.NorthAmericaLetter, System.Printing.PageMediaSizeName.NorthAmericaLetterRotated)
                : (System.Printing.PageMediaSizeName.ISOA4, System.Printing.PageMediaSizeName.ISOA4Rotated);
            if (size.PageMediaSizeName == expectedName || size.PageMediaSizeName == rotatedName) return true;

            var (width, height) = MediaSize(paper);
            if (size.Width is { } w && size.Height is { } h)
                return Math.Abs(Math.Min(w, h) - width) <= 4 && Math.Abs(Math.Max(w, h) - height) <= 4;
            return size.PageMediaSizeName is null or System.Printing.PageMediaSizeName.Unknown;
        }

        return true;
    }

    // Nama enum bawaan WPF (ISOA4, NorthAmericaLetter, ...) tidak layak tampil di UI; yang umum dipetakan ke nama lazim,
    // sisanya jatuh ke nama enum apa adanya.
    internal static string PaperName(System.Printing.PageMediaSizeName name) => name switch
    {
        System.Printing.PageMediaSizeName.ISOA3 or System.Printing.PageMediaSizeName.ISOA3Rotated => "A3",
        System.Printing.PageMediaSizeName.ISOA4 or System.Printing.PageMediaSizeName.ISOA4Rotated => "A4",
        System.Printing.PageMediaSizeName.ISOA5 or System.Printing.PageMediaSizeName.ISOA5Rotated => "A5",
        System.Printing.PageMediaSizeName.ISOA6 or System.Printing.PageMediaSizeName.ISOA6Rotated => "A6",
        System.Printing.PageMediaSizeName.ISOB4 => "B4",
        System.Printing.PageMediaSizeName.ISOB5Extra => "B5",
        System.Printing.PageMediaSizeName.NorthAmericaLetter or System.Printing.PageMediaSizeName.NorthAmericaLetterRotated => "Letter",
        System.Printing.PageMediaSizeName.NorthAmericaLegal => "Legal",
        System.Printing.PageMediaSizeName.NorthAmericaExecutive => "Executive",
        System.Printing.PageMediaSizeName.NorthAmericaTabloid => "Tabloid",
        _ => name.ToString(),
    };

    internal static string DescribeTicket(System.Printing.PrintTicket ticket)
    {
        var size = ticket.PageMediaSize;
        var paper = size?.PageMediaSizeName is { } name and not System.Printing.PageMediaSizeName.Unknown
            ? PaperName(name)
            : size is { Width: { } w, Height: { } h } ? $"{w / 96 * 25.4:0} x {h / 96 * 25.4:0} mm" : "kertas lain";
        var landscape = ticket.PageOrientation is System.Printing.PageOrientation.Landscape or System.Printing.PageOrientation.ReverseLandscape;
        return $"{paper}, {(landscape ? "lanskap" : "potret")}";
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }

    void OnClosed(object? sender, EventArgs e)
    {
        pageDescriptor?.RemoveValueChanged(Viewer, OnViewerChanged);
        zoomDescriptor?.RemoveValueChanged(Viewer, OnViewerChanged);
        if (scroller is not null) scroller.ScrollChanged -= OnViewerChanged;
        Viewer.Document = null;
        build?.Dispose();
        build = null;
    }
}
