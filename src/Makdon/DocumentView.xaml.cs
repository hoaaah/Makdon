using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Editing;
using Markdig.Renderers;
using Markdig.Renderers.Wpf;
using Markdig.Syntax;
using Markdig.Wpf;

namespace Makdon;

public partial class DocumentView : System.Windows.Controls.UserControl, IDisposable
{
    // Jeda render ditingkatkan untuk dokumen besar supaya mengetik tetap lancar.
    const int BaseRenderDelayMs = 250;
    const int LargeDocumentChars = 200_000;
    const int HugeDocumentChars = 1_000_000;
    // Di atas ukuran ini, parsing Markdig dijalankan di thread latar.
    internal const int BackgroundParseChars = 100_000;
    // Di atas ukuran ini statistik kata/karakter dihitung lebih jarang.
    const int SlowStatsChars = 1_000_000;

    readonly DocumentTab tab;
    readonly DispatcherTimer renderTimer = new() { Interval = TimeSpan.FromMilliseconds(BaseRenderDelayMs) };
    readonly DispatcherTimer statsTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    bool previewStale = true;

    // Render ulang dilewati bila dokumen belum berubah (versi sama) atau teksnya sama dengan render terakhir.
    // Teks hanya disimpan untuk dokumen yang tidak raksasa (menghindari menahan salinan penuh).
    string? lastRenderedText;
    ITextSourceVersion? lastRenderedVersion;
    int renderGeneration;
    bool disposed;
    bool renderInFlight;
    string? pendingAnchor;

    // Sinkron scroll. Nilai "expected" menandai scroll yang kita sendiri picu supaya
    // gema event-nya tidak diteruskan balik (menghindari loop umpan balik).
    ScrollViewer? previewScroll;
    double? expectedEditorOffset;
    double? expectedPreviewOffset;
    bool scrollSyncSuspended;

    /// <summary>
    /// Gambar http(s) di pratinjau/cetak diganti teks penanda (privasi: mencegah pelacakan). Global untuk semua tab;
    /// ubah lewat jendela utama lalu panggil <see cref="RefreshPreview"/> pada tiap tab.
    /// </summary>
    public static bool BlockRemoteImages { get; set; } = true;

    public DocumentView(DocumentTab tab)
    {
        InitializeComponent();
        this.tab = tab;

        Editor.Document = tab.Document;
        Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("MarkDown");
        Editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        Editor.TextArea.TextView.ScrollOffsetChanged += OnEditorScroll;
        Editor.PreviewKeyDown += OnEditorPreviewKeyDown;
        ConfigureEditorAppearance();
        FindBar.Attach(Editor);

        Preview.CommandBindings.Add(new CommandBinding(Commands.Hyperlink, OnHyperlink));
        // FlowDocumentScrollViewer punya pengikatan kelas sendiri untuk Cetak (Ctrl+P) yang mencetak dokumen pratinjau apa adanya
        // (tema aktif, tanpa tata letak/kaki halaman PrintService) dan mendahului CommandBinding jendela. Teruskan ke jalur jendela;
        // target = DocumentView (di atas Preview) supaya pengikatan ini tidak dilewati lagi.
        Preview.CommandBindings.Add(new CommandBinding(ApplicationCommands.Print,
            (_, e) => { ApplicationCommands.Print.Execute(null, this); e.Handled = true; },
            (_, e) => { e.CanExecute = ApplicationCommands.Print.CanExecute(null, this); e.Handled = true; }));
        Preview.SizeChanged += OnPreviewSizeChanged;

        statsTimer.Tick += (_, _) => { statsTimer.Stop(); UpdateStats(); };
        UpdateStats();

        tab.Document.TextChanged += OnDocumentTextChanged;
        renderTimer.Tick += (_, _) => { renderTimer.Stop(); RenderPreview(); };
        tab.PropertyChanged += OnTabPropertyChanged;

        ApplyMode(focusEditor: false);
    }

    bool PreviewVisible => tab.Mode != ViewMode.Edit;

    void OnCaretPositionChanged(object? sender, EventArgs e) =>
        tab.SetCaret(Editor.TextArea.Caret.Line, Editor.TextArea.Caret.Column);

    void OnPreviewSizeChanged(object sender, SizeChangedEventArgs e) => UpdateReadingPadding();

    void OnDocumentTextChanged(object? sender, EventArgs e)
    {
        // Dokumen besar: statistik dihitung lebih jarang (tiap hitungan membaca seluruh dokumen).
        statsTimer.Interval = TimeSpan.FromMilliseconds(tab.Document.TextLength >= SlowStatsChars ? 2000 : 300);
        statsTimer.Stop();
        statsTimer.Start();
        previewStale = true;
        if (!PreviewVisible) return;
        renderTimer.Interval = TimeSpan.FromMilliseconds(RenderDelayMs(tab.Document.TextLength));
        renderTimer.Stop();
        renderTimer.Start();
    }

    void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentTab.Mode)) ApplyMode(focusEditor: true);
        // Path berubah (Simpan Sebagai) -> gambar/tautan relatif perlu di-resolve ulang.
        else if (e.PropertyName == nameof(DocumentTab.FilePath)) RefreshPreview();
    }

    /// <summary>Membuang hasil render terakhir lalu merender ulang (bila pratinjau tampak); dipakai saat pengaturan render berubah.</summary>
    public void RefreshPreview()
    {
        if (disposed) return;
        previewStale = true;
        InvalidateRendered();
        if (PreviewVisible) RenderPreview();
    }

    void InvalidateRendered()
    {
        lastRenderedText = null;
        lastRenderedVersion = null;
    }

    /// <summary>Menghentikan timer, membatalkan render latar, dan melepas event; dipanggil saat tab ditutup.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        renderTimer.Stop();
        statsTimer.Stop();
        renderGeneration++; // render latar yang masih berjalan dibuang hasilnya
        renderInFlight = false;
        pendingAnchor = null;
        FindBar.Detach();

        tab.Document.TextChanged -= OnDocumentTextChanged;
        tab.PropertyChanged -= OnTabPropertyChanged;
        Editor.TextArea.Caret.PositionChanged -= OnCaretPositionChanged;
        Editor.TextArea.TextView.ScrollOffsetChanged -= OnEditorScroll;
        Editor.PreviewKeyDown -= OnEditorPreviewKeyDown;
        Preview.SizeChanged -= OnPreviewSizeChanged;
        if (previewScroll is not null) previewScroll.ScrollChanged -= OnPreviewScroll;
        ZoomRequested = null;
        Preview.Document = null;
        InvalidateRendered();
    }

    /// <summary>Ctrl+roda mouse: +1 memperbesar, -1 memperkecil (diterapkan oleh jendela utama ke semua tab).</summary>
    public event Action<int>? ZoomRequested;

    // ---- Tampilan editor, tema, zoom ----

    // Properti AvalonEdit yang bukan DependencyProperty biasa diikat ke brush tema lewat resource reference
    // (atau di RefreshTheme untuk yang berupa nilai statis).
    void ConfigureEditorAppearance()
    {
        var textView = Editor.TextArea.TextView;
        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.EnableHyperlinks = true;
        textView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
        textView.SetResourceReference(TextView.CurrentLineBackgroundProperty, "CurrentLineBrush");
        textView.SetResourceReference(TextView.LinkTextForegroundBrushProperty, "SyntaxLinkBrush");
        textView.SetResourceReference(TextView.NonPrintableCharacterBrushProperty, "LineNumberBrush");
        Editor.TextArea.SetResourceReference(TextArea.SelectionBrushProperty, "SelectionBrush");
        Editor.TextArea.SelectionBorder = null;
        Editor.TextArea.SelectionForeground = null;
        Editor.TextArea.SelectionCornerRadius = 2;
        RefreshTheme();
    }

    /// <summary>Menerapkan ulang bagian tema yang tidak otomatis (caret, warna sintaks) setelah tema berganti.</summary>
    public void RefreshTheme()
    {
        if (TryFindResource("EditorForegroundBrush") is Brush caret) Editor.TextArea.Caret.CaretBrush = caret;
        Editor.TextArea.TextView.Redraw();
    }

    /// <summary>Zoom dalam persen: ukuran font editor dan Zoom pratinjau.</summary>
    public void ApplyZoom(int percent)
    {
        percent = ZoomLevel.Clamp(percent);
        Editor.FontSize = ZoomLevel.EditorFontSize(percent);
        Preview.Zoom = percent;
        UpdateReadingPadding();
    }

    void View_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || e.Delta == 0) return;
        ZoomRequested?.Invoke(e.Delta > 0 ? +1 : -1);
        e.Handled = true;
    }

    void UpdateStats()
    {
        if (disposed) return;
        // Dihitung per potongan langsung dari dokumen: tanpa salinan string utuh.
        var (words, characters) = TextStats.CountSource(tab.Document);
        tab.SetStats(words, characters);
    }

    // ---- Cari & ganti, format ----

    /// <summary>Membuka panel cari (atau ganti). Di mode Pratinjau, beralih dulu ke Terpisah supaya editor tampak.</summary>
    public void ShowFind(bool replace)
    {
        if (tab.Mode == ViewMode.Preview) tab.Mode = ViewMode.Split;
        FindBar.Open(replace);
    }

    public void FindNext(bool forward)
    {
        if (tab.Mode == ViewMode.Preview) return;
        FindBar.FindNext(forward);
    }

    public bool IsFindBarFocused => FindBar.IsKeyboardFocusWithin;

    /// <summary>Menerapkan format Markdown ke seleksi editor sebagai satu langkah Undo, lalu mengembalikan fokus ke editor.</summary>
    public void ApplyFormat(MarkdownFormat format, int headingLevel = 1)
    {
        if (tab.Mode == ViewMode.Preview) return;
        MarkdownEditing.Apply(Editor, format, headingLevel);
        Editor.Focus();
    }

    void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && FindBar.IsOpen)
        {
            FindBar.Close();
            e.Handled = true;
        }
    }

    static int RenderDelayMs(int length) =>
        length >= HugeDocumentChars ? 1500 : length >= LargeDocumentChars ? 800 : BaseRenderDelayMs;

    public void FocusEditor() => Editor.Focus();

    /// <summary>Posisi caret (offset karakter) di editor; saat di-set diklem ke panjang teks.</summary>
    public int CaretOffset
    {
        get => Editor.CaretOffset;
        set => Editor.CaretOffset = Math.Clamp(value, 0, tab.Document.TextLength);
    }

    /// <summary>Menaruh caret lalu menggulirkan editor ke sana (ditunda sampai layout siap).</summary>
    public void RestoreCaret(int offset)
    {
        CaretOffset = offset;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var location = tab.Document.GetLocation(Math.Clamp(offset, 0, tab.Document.TextLength));
            Editor.ScrollTo(location.Line, location.Column);
        });
    }

    /// <summary>Melompat ke heading dengan id (slug gaya GitHub) tertentu di pratinjau. Diabaikan di mode Editor.</summary>
    public void ScrollToAnchor(string? anchor)
    {
        anchor = anchor?.TrimStart('#');
        if (string.IsNullOrEmpty(anchor) || !PreviewVisible) return;

        pendingAnchor = anchor;
        if (previewStale) { renderTimer.Stop(); RenderPreview(); }
        // Tunggu layout selesai; bila render latar masih berjalan, anchor diterapkan setelah render selesai.
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, ApplyPendingAnchor);
    }

    void ApplyPendingAnchor()
    {
        if (pendingAnchor is null || renderInFlight || Preview.Document is not { } document) return;

        var id = pendingAnchor;
        pendingAnchor = null;

        var heading = FindHeading(document, id);
        if (heading is null)
        {
            try { heading = FindHeading(document, Uri.UnescapeDataString(id)); }
            catch (UriFormatException) { }
        }
        heading?.BringIntoView();
    }

    static Paragraph? FindHeading(DependencyObject parent, string id)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is Paragraph paragraph)
            {
                if (paragraph.Tag is string tag && string.Equals(tag, id, StringComparison.OrdinalIgnoreCase)) return paragraph;
                continue; // heading tidak bersarang di dalam paragraf
            }
            if (child is Inline) continue;
            if (child is DependencyObject node && FindHeading(node, id) is { } found) return found;
        }
        return null;
    }

    void ApplyMode(bool focusEditor)
    {
        var mode = tab.Mode;
        var showEditor = mode != ViewMode.Preview;
        var showPreview = mode != ViewMode.Edit;
        var split = mode == ViewMode.Split;

        EditorHost.Visibility = showEditor ? Visibility.Visible : Visibility.Collapsed;
        Preview.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
        Splitter.Visibility = split ? Visibility.Visible : Visibility.Collapsed;

        EditorColumn.Width = showEditor ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        SplitterColumn.Width = split ? GridLength.Auto : new GridLength(0);
        PreviewColumn.Width = showPreview ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        if (showPreview && previewStale) RenderPreview();
        if (focusEditor && showEditor) Editor.Focus();
    }

    // ---- Render pratinjau ----

    void RenderPreview()
    {
        if (disposed) return;
        previewStale = false;

        // Cek versi dulu: tanpa menyalin teks dokumen (mahal untuk dokumen besar) bila tidak ada perubahan sejak render terakhir.
        var version = tab.Document.Version;
        if (Preview.Document is not null && IsRenderedVersion(version))
        {
            CancelPendingRender();
            return;
        }

        var text = tab.Document.Text;
        if (Preview.Document is not null && string.Equals(text, lastRenderedText, StringComparison.Ordinal))
        {
            lastRenderedVersion = version; // teks kembali ke yang tampil (mis. Undo)
            CancelPendingRender();
            return;
        }

        var baseDir = tab.FilePath is null ? null : Path.GetDirectoryName(tab.FilePath);
        var blockRemote = BlockRemoteImages;
        var generation = ++renderGeneration;

        if (text.Length < BackgroundParseChars)
        {
            renderInFlight = false;
            try
            {
                ApplyParsed(ParseDocument(text, baseDir, blockRemote), text, version);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Jalur timer/konstruktor: galat render tidak boleh keluar sampai ke dispatcher.
                ShowRenderError("Render pratinjau gagal", ex);
            }
        }
        else
        {
            _ = RenderInBackground(text, version, baseDir, blockRemote, generation);
        }
    }

    bool IsRenderedVersion(ITextSourceVersion? version) =>
        version is not null && lastRenderedVersion is { } rendered
        && version.BelongsToSameDocumentAs(rendered) && version.CompareAge(rendered) == 0;

    // Batalkan render latar yang masih berjalan (dokumen sudah kembali ke yang tampil).
    void CancelPendingRender()
    {
        renderGeneration++;
        renderInFlight = false;
        ApplyPendingAnchorSoon();
    }

    static MarkdownDocument ParseDocument(string text, string? baseDir, bool blockRemote)
    {
        var parsed = Markdig.Markdown.Parse(text, MarkdownSupport.Pipeline);
        MarkdownSupport.ResolveImageUrls(parsed, baseDir, blockRemote);
        return parsed;
    }

    // Parsing dokumen besar di thread latar; pembuatan FlowDocument tetap di UI thread (wajib).
    async Task RenderInBackground(string text, ITextSourceVersion? version, string? baseDir, bool blockRemote, int generation)
    {
        renderInFlight = true;
        try
        {
            var parsed = await Task.Run(() => ParseDocument(text, baseDir, blockRemote));
            if (generation != renderGeneration) return; // sudah ada render yang lebih baru (atau tab ditutup)
            renderInFlight = false;
            ApplyParsed(parsed, text, version);
        }
        catch (Exception ex)
        {
            // Task tanpa penunggu: jangan sampai merobohkan aplikasi.
            ShowRenderError("Render pratinjau (latar) gagal", ex);
        }
        finally
        {
            if (generation == renderGeneration) renderInFlight = false;
        }
    }

    void ShowRenderError(string context, Exception ex)
    {
        CrashLog.Write(context, ex);
        InvalidateRendered(); // render berikutnya mencoba lagi
        Preview.Document = CreateErrorDocument(null, ex);
    }

    void ApplyParsed(MarkdownDocument parsed, string text, ITextSourceVersion? version)
    {
        lastRenderedText = text.Length < HugeDocumentChars ? text : null;
        lastRenderedVersion = version;

        var scroll = PreviewScroll;
        var offset = scroll?.VerticalOffset ?? 0;

        var flowDocument = CreateFlowDocument(parsed, null);

        // Mengganti dokumen mereset scroll pratinjau; jangan biarkan itu menggeser editor.
        scrollSyncSuspended = true;
        Preview.Document = flowDocument;
        UpdateReadingPadding();

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            // Pertahankan posisi scroll saat pratinjau dirender ulang ketika mengetik.
            // Mode Terpisah: ikuti posisi editor (proporsional); selain itu pakai offset sebelumnya.
            if (PreviewScroll is { } ps)
            {
                if (tab.Mode == ViewMode.Split && Editor.IsVisible) SyncPreviewToEditor(ps);
                else if (offset > 0) ps.ScrollToVerticalOffset(offset);
            }
            expectedPreviewOffset = null;
            scrollSyncSuspended = false;
            ApplyPendingAnchorSoon();
        });
    }

    // Resources (opsional) dipasang sebelum render supaya style dokumen langsung memakai kamus itu.
    // Bila renderer gagal, gambar yang tidak bisa dimuat diganti teks pengganti lalu dirender ulang; bila masih gagal,
    // pratinjau berisi pesan galat (dokumen galat) dan tidak melempar. Semua kegagalan dicatat di crash.log.
    // throwOnFailure (jalur cetak/pratinjau cetak): dokumen galat tidak boleh dicetak dan memuat path profil
    // pengguna (lokasi crash.log), jadi kegagalan akhir dilempar sebagai galat berpesan ramah untuk ditampilkan pemanggil.
    static FlowDocument CreateFlowDocument(MarkdownDocument parsed, ResourceDictionary? resources, bool throwOnFailure = false)
    {
        try
        {
            return BuildFlowDocument(parsed, resources);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            CrashLog.Write("Render pratinjau gagal; mencoba ulang dengan gambar rusak diganti teks", ex);
        }

        try
        {
            ReplaceUnloadableImages(parsed);
            return BuildFlowDocument(parsed, resources);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            CrashLog.Write("Render pratinjau gagal setelah gambar rusak diganti", ex);
            if (throwOnFailure)
                throw new InvalidOperationException(
                    $"Dokumen tidak dapat disusun untuk dicetak karena terjadi kesalahan saat merender isinya ({ex.GetType().Name}). " +
                    "Isi file tidak berubah. Rincian ada di crash.log.", ex);
            return CreateErrorDocument(resources, ex);
        }
    }

    /// <summary>Hanya untuk pengujian: dipanggil di awal setiap render dokumen, mis. untuk memaksa kegagalan renderer.</summary>
    internal static Action<MarkdownDocument>? RenderFaultForTests;

    static FlowDocument BuildFlowDocument(MarkdownDocument parsed, ResourceDictionary? resources)
    {
        RenderFaultForTests?.Invoke(parsed);
        var flowDocument = new FlowDocument();
        if (resources is not null) flowDocument.Resources.MergedDictionaries.Add(resources);

        var renderer = new WpfRenderer(flowDocument);
        MarkdownSupport.Pipeline.Setup(renderer);
        renderer.ObjectRenderers.Replace<HeadingRenderer>(new AnchorHeadingRenderer());
        renderer.Render(parsed);
        TightenBlocks(flowDocument);
        return flowDocument;
    }

    // Markdig.Wpf membuat BitmapImage tanpa try/catch, jadi gambar yang ada tetapi tidak bisa di-decode
    // (NotSupportedException) menggagalkan seluruh render. Uji tiap gambar lokal dan ganti yang gagal dengan teks alt.
    static void ReplaceUnloadableImages(MarkdownDocument parsed)
    {
        foreach (var image in parsed.Descendants<Markdig.Syntax.Inlines.LinkInline>().Where(l => l.IsImage).ToList())
        {
            if (!Uri.TryCreate(image.Url, UriKind.Absolute, out var uri) || !uri.IsFile) continue;
            try
            {
                var probe = new System.Windows.Media.Imaging.BitmapImage();
                probe.BeginInit();
                probe.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                probe.UriSource = uri;
                probe.EndInit();
            }
            catch (Exception ex) when (ex is NotSupportedException or IOException or UnauthorizedAccessException
                                           or InvalidOperationException or ArgumentException)
            {
                CrashLog.Write($"Gambar tidak dapat ditampilkan: {image.Url}", ex);
                var alt = string.Concat(image.Descendants<Markdig.Syntax.Inlines.LiteralInline>().Select(l => l.Content.ToString()));
                image.ReplaceBy(new Markdig.Syntax.Inlines.LiteralInline($"[gambar tidak dapat ditampilkan: {(alt.Length > 0 ? alt : Path.GetFileName(uri.LocalPath))}]"),
                    copyChildren: false); // anak LinkInline (teks alt) sudah dipakai di penanda; jangan disalin lagi
            }
        }
    }

    static FlowDocument CreateErrorDocument(ResourceDictionary? resources, Exception ex)
    {
        var document = new FlowDocument(new Paragraph(new Run(
            $"Pratinjau tidak dapat ditampilkan karena terjadi kesalahan saat merender dokumen ({ex.GetType().Name}: {ex.Message}). " +
            $"Isi file tidak berubah dan tetap bisa diedit. Rincian ada di {CrashLog.LogPath}.")));
        if (resources is not null) document.Resources.MergedDictionaries.Add(resources);
        return document;
    }

    // Paragraf di dalam item daftar dan sel tabel memakai margin paragraf biasa (14) sehingga daftar/tabel terlihat
    // renggang; rapatkan agar item daftar tampil sebagai satu kelompok dan sel tabel tidak terlalu tinggi.
    static void TightenBlocks(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is ListItem item) SetParagraphMargins(item.Blocks, new Thickness(0, 0, 0, 4));
            else if (child is TableCell cell) SetParagraphMargins(cell.Blocks, new Thickness(0));

            if (child is Inline or Paragraph) continue; // tidak ada daftar/tabel di dalam paragraf
            if (child is DependencyObject node) TightenBlocks(node);
        }
    }

    static void SetParagraphMargins(IEnumerable<System.Windows.Documents.Block> blocks, Thickness margin)
    {
        foreach (var block in blocks)
            if (block is Paragraph paragraph) paragraph.Margin = margin;
    }

    // ---- Lebar baca ----

    const double ReadingWidth = 860;
    const double MinSidePadding = 32;

    /// <summary>
    /// Membatasi lebar baca ±860 px dan menaruhnya di tengah dengan mengatur PagePadding horizontal
    /// (FlowDocumentScrollViewer tidak memusatkan halaman ber-MaxPageWidth).
    /// </summary>
    void UpdateReadingPadding()
    {
        if (Preview.Document is not { } document) return;

        var scale = Math.Max(0.01, Preview.Zoom / 100.0);
        var width = (PreviewScroll is { ViewportWidth: > 0 } scroll ? scroll.ViewportWidth : Preview.ActualWidth) / scale;
        var side = Math.Max(MinSidePadding, Math.Floor((width - ReadingWidth) / 2));
        var padding = new Thickness(side, 28, side, 56);
        if (document.PagePadding != padding) document.PagePadding = padding;
    }

    /// <summary>
    /// Dokumen untuk dicetak: dirender ulang dengan tema Terang (kertas putih), lebar penuh halaman.
    /// Ukuran halaman diatur pemanggil sesuai area cetak printer.
    /// </summary>
    public FlowDocument BuildPrintDocument() => BuildPrintDocument(CapturePrintSnapshot());

    /// <summary>Salinan teks tab saat ini + pengaturan render, untuk pratinjau/cetak yang tak terpengaruh penyuntingan berikutnya.</summary>
    public PrintSnapshot CapturePrintSnapshot() =>
        new(tab.Document.Text, tab.FilePath is null ? null : Path.GetDirectoryName(tab.FilePath), BlockRemoteImages, tab.Title);

    // Gambar remote/UNC diblokir oleh ResolveImageUrls (lewat ParseDocument) sama seperti pratinjau.
    internal static MarkdownDocument ParsePrintSnapshot(PrintSnapshot snapshot) =>
        ParseDocument(snapshot.Text, snapshot.BaseDirectory, snapshot.BlockRemoteImages);

    internal static FlowDocument BuildPrintDocument(PrintSnapshot snapshot) => BuildPrintDocument(ParsePrintSnapshot(snapshot));

    // Dari AST yang sudah diparse (boleh dipakai berulang di thread UI): renderer hanya membaca AST; satu-satunya perubahan
    // adalah ReplaceUnloadableImages pada jalur galat, dan itu idempoten.
    internal static FlowDocument BuildPrintDocument(MarkdownDocument parsed)
    {
        var document = CreateFlowDocument(parsed, ThemeManager.LoadDictionary(dark: false), throwOnFailure: true);
        document.PagePadding = new Thickness(48);
        document.Background = Brushes.White;
        return document;
    }

    void ApplyPendingAnchorSoon()
    {
        if (pendingAnchor is not null) Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, ApplyPendingAnchor);
    }

    // ---- Sinkron scroll editor <-> pratinjau (mode Terpisah) ----

    ScrollViewer? PreviewScroll
    {
        get
        {
            if (previewScroll is null)
            {
                Preview.ApplyTemplate();
                previewScroll = Preview.Template?.FindName("PART_ContentHost", Preview) as ScrollViewer;
                if (previewScroll is not null) previewScroll.ScrollChanged += OnPreviewScroll;
            }
            return previewScroll;
        }
    }

    double EditorScrollMax => Math.Max(0, Editor.ExtentHeight - Editor.ViewportHeight);

    void SyncPreviewToEditor(ScrollViewer ps)
    {
        var fraction = EditorScrollMax > 0 ? Editor.VerticalOffset / EditorScrollMax : 0;
        var target = Math.Clamp(fraction, 0, 1) * ps.ScrollableHeight;
        if (Math.Abs(target - ps.VerticalOffset) < 1) return;

        expectedPreviewOffset = target;
        ps.ScrollToVerticalOffset(target);
    }

    void OnEditorScroll(object? sender, EventArgs e)
    {
        if (expectedEditorOffset is { } expected)
        {
            expectedEditorOffset = null;
            if (Math.Abs(Editor.VerticalOffset - expected) < 1) return; // gema dari sinkron
        }

        if (scrollSyncSuspended || tab.Mode != ViewMode.Split) return;
        if (PreviewScroll is { } ps) SyncPreviewToEditor(ps);
    }

    void OnPreviewScroll(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportWidthChange != 0) UpdateReadingPadding(); // mis. scrollbar muncul/hilang
        if (e.VerticalChange == 0) return; // hanya perubahan extent/viewport

        if (expectedPreviewOffset is { } expected)
        {
            expectedPreviewOffset = null;
            if (Math.Abs(e.VerticalOffset - expected) < 1) return; // gema dari sinkron
        }

        if (scrollSyncSuspended || tab.Mode != ViewMode.Split || previewScroll is not { } ps) return;

        var fraction = ps.ScrollableHeight > 0 ? ps.VerticalOffset / ps.ScrollableHeight : 0;
        var target = Math.Clamp(fraction, 0, 1) * EditorScrollMax;
        if (Math.Abs(target - Editor.VerticalOffset) < 1) return;

        expectedEditorOffset = target;
        Editor.ScrollToVerticalOffset(target);
    }

    // ---- Tautan ----

    // Shell bisa gagal (tidak ada aplikasi terkait, kebijakan sistem, dll.); beri tahu pengguna, jangan jatuhkan aplikasi.
    static void OpenExternally(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException
                                       or FileNotFoundException or UnauthorizedAccessException)
        {
            CrashLog.Write($"Gagal membuka tautan: {uri.AbsoluteUri}", ex);
            MessageBox.Show($"Tidak bisa membuka tautan:\n{uri.AbsoluteUri}\n\n{ex.Message}", "Makdon",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void OnHyperlink(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is not string url || string.IsNullOrWhiteSpace(url)) return;

        // Tautan anchor di dokumen yang sama.
        if (url.StartsWith('#'))
        {
            ScrollToAnchor(url);
            return;
        }

        // Hanya skema aman yang diserahkan ke shell. Tautan ke file lokal non-markdown diabaikan
        // agar dokumen tidak bisa menjalankan file lain di sebelahnya.
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme != Uri.UriSchemeFile)
        {
            if (uri.Scheme is "http" or "https" or "mailto") OpenExternally(uri);
            return;
        }

        // Resolusi path murni (tanpa I/O): ekstensi dan lokasi (UNC ditolak) dicek sebelum File.Exists, supaya tautan
        // ter-percent-encode ke \\host\share tidak memicu koneksi SMB/NTLM.
        var baseDir = tab.FilePath is null ? null : Path.GetDirectoryName(tab.FilePath);
        var target = MarkdownSupport.ResolveLinkTarget(baseDir, url, out var anchor);
        if (target is not null && File.Exists(target)) tab.RequestOpen(target, anchor);
    }
}
