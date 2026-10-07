using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MdViewer;

/// <summary>
/// Panel cari &amp; ganti inline di atas editor (satu per <see cref="DocumentView"/>). Logika pencarian ada di
/// <see cref="SearchEngine"/>; penanda hasil digambar oleh <see cref="SearchResultsRenderer"/>.
/// </summary>
public partial class FindReplaceBar : UserControl
{
    static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(150);
    // Jeda input kolom cari: regex tidak dijalankan di setiap ketukan, hanya setelah pengguna berhenti mengetik.
    static readonly TimeSpan QueryDebounce = TimeSpan.FromMilliseconds(250);

    readonly DispatcherTimer refreshTimer = new() { Interval = RefreshDelay };
    readonly DispatcherTimer queryTimer = new() { Interval = QueryDebounce };
    readonly SearchResultsRenderer renderer = new();
    TextEditor? editor;
    IReadOnlyList<SearchMatch> matches = [];
    bool invalidPattern;
    // Pola+opsi pencarian terakhir yang kena batas waktu: tidak dijalankan ulang otomatis (tiap perubahan teks editor/Cari
    // Berikutnya akan membekukan UI lagi) sampai pengguna mengubah pola atau opsi.
    string? timedOutKey;

    public FindReplaceBar()
    {
        InitializeComponent();
        refreshTimer.Tick += (_, _) =>
        {
            refreshTimer.Stop();
            Refresh(selectCurrent: false);
        };
        queryTimer.Tick += (_, _) =>
        {
            queryTimer.Stop();
            Refresh(selectCurrent: true);
        };
    }

    public bool IsOpen => Visibility == Visibility.Visible;

    SearchOptions Options => new(MatchCaseToggle.IsChecked == true, RegexToggle.IsChecked == true);

    /// <summary>Menghubungkan panel ke editor: memasang penggambar hasil dan memantau perubahan teks.</summary>
    public void Attach(TextEditor target)
    {
        editor = target;
        target.TextArea.TextView.BackgroundRenderers.Add(renderer);
        target.Document.TextChanged += OnEditorTextChanged;
    }

    void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (!IsOpen) return;
        refreshTimer.Stop();
        refreshTimer.Start();
    }

    /// <summary>Menghentikan timer dan melepas editor (dipakai saat tab ditutup); panel tidak dipakai lagi sesudahnya.</summary>
    public void Detach()
    {
        refreshTimer.Stop();
        queryTimer.Stop();
        if (editor is null) return;

        editor.Document.TextChanged -= OnEditorTextChanged;
        editor.TextArea.TextView.BackgroundRenderers.Remove(renderer);
        matches = [];
        editor = null;
    }

    /// <summary>Menampilkan panel. Seleksi satu baris di editor dipakai sebagai kata kunci awal.</summary>
    public void Open(bool replace)
    {
        if (editor is null) return;

        Visibility = Visibility.Visible;
        if (replace) ReplaceToggle.IsChecked = true;

        var selected = editor.SelectedText;
        if (selected.Length > 0 && selected.Length <= 200 && !selected.Contains('\n') && !selected.Contains('\r'))
            FindBox.Text = selected;

        queryTimer.Stop(); // teks awal di atas sudah diproses oleh Refresh berikut
        Refresh(selectCurrent: false);

        // Fokus baru bisa berpindah setelah panel dilayout; tunda sampai itu selesai.
        var target = replace && FindBox.Text.Length > 0 ? ReplaceBox : FindBox;
        UpdateLayout();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            Keyboard.Focus(target);
            target.SelectAll();
        });
    }

    public void Close()
    {
        refreshTimer.Stop();
        queryTimer.Stop();
        Visibility = Visibility.Collapsed;
        matches = [];
        renderer.Update(matches, -1);
        editor?.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        editor?.Focus();
    }

    /// <summary>Melompat ke hasil berikutnya/sebelumnya (melingkar). Membuka panel bila belum terbuka.</summary>
    public void FindNext(bool forward)
    {
        if (editor is null) return;
        if (!IsOpen) { Open(replace: false); return; }

        queryTimer.Stop(); // pencarian tertunda digantikan oleh Refresh di bawah
        Refresh(selectCurrent: false);
        if (matches.Count == 0) return;

        var selection = (Start: editor.SelectionStart, Length: editor.SelectionLength);
        var onMatch = SearchEngine.IndexOfExact(matches, selection.Start, selection.Length) >= 0;

        int index;
        if (forward)
        {
            index = SearchEngine.IndexAtOrAfter(matches, onMatch ? selection.Start + selection.Length : selection.Start);
            if (index < 0) index = 0;
        }
        else
        {
            index = SearchEngine.IndexBefore(matches, selection.Start);
            if (index < 0) index = matches.Count - 1;
        }
        Select(index);
    }

    // ---- Inti ----

    void Select(int index)
    {
        if (editor is null || index < 0 || index >= matches.Count) return;

        var match = matches[index];
        editor.Select(match.Offset, match.Length);
        var location = editor.Document.GetLocation(match.Offset);
        editor.ScrollTo(location.Line, location.Column);
        UpdateStatus();
    }

    void Refresh(bool selectCurrent)
    {
        if (editor is null) return;

        invalidPattern = false;
        if (FindBox.Text.Length == 0)
        {
            matches = [];
        }
        else if (timedOutKey is not null && timedOutKey == SearchKey())
        {
            matches = [];
            invalidPattern = true;
            CountText.Text = SlowPatternText;
        }
        else if (SearchEngine.TryFindAll(editor.Document.Text, FindBox.Text, Options, out var found, out var error))
        {
            matches = found;
        }
        else
        {
            matches = [];
            invalidPattern = true;
            if (error == SearchEngine.TimeoutError)
            {
                timedOutKey = SearchKey();
                error = SlowPatternText;
            }
            CountText.Text = error;
        }

        if (selectCurrent && matches.Count > 0)
        {
            var index = SearchEngine.IndexAtOrAfter(matches, editor.SelectionStart);
            Select(index < 0 ? 0 : index);
        }
        else
        {
            UpdateStatus();
        }
    }

    const string SlowPatternText = "Pola terlalu lambat; ubah pola atau opsi";

    string SearchKey() => $"{(Options.UseRegex ? 'r' : 'l')}{(Options.MatchCase ? 'c' : 'i')}:{FindBox.Text}";

    void UpdateStatus()
    {
        if (editor is null) return;

        var current = SearchEngine.IndexOfExact(matches, editor.SelectionStart, editor.SelectionLength);
        renderer.Update(matches, current);
        editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);

        if (invalidPattern) return; // pesan galat sudah ditampilkan oleh Refresh
        var capped = matches.Count >= SearchEngine.MaxResults ? "+" : "";
        CountText.Text = FindBox.Text.Length == 0 ? ""
            : matches.Count == 0 ? "Tidak ada hasil"
            : current >= 0 ? $"{current + 1} dari {matches.Count}{capped}"
            : $"{matches.Count}{capped} hasil";
    }

    void ReplaceCurrent()
    {
        if (editor is null || matches.Count == 0) return;

        // Hitung ulang dengan teks pengganti agar $1 pada regex terisi. Tanpa batas MaxResults (itu hanya untuk penanda):
        // hasil terpilih di luar 20.000 pertama tetap harus bisa diganti.
        if (!SearchEngine.TryFindAll(editor.Document.Text, FindBox.Text, Options, out var withReplacement, out var replaceError,
                ReplaceBox.Text, int.MaxValue))
        {
            CountText.Text = replaceError == SearchEngine.TimeoutError ? SlowPatternText : replaceError;
            return;
        }
        var index = SearchEngine.IndexOfExact(withReplacement, editor.SelectionStart, editor.SelectionLength);
        if (index < 0)
        {
            // Belum ada hasil terpilih: lompat dulu ke hasil berikutnya.
            matches = withReplacement;
            FindNext(forward: true);
            return;
        }

        var match = withReplacement[index];
        editor.Document.Replace(match.Offset, match.Length, match.Replacement);
        editor.Select(match.Offset + match.Replacement.Length, 0);
        Refresh(selectCurrent: false);
        FindNext(forward: true);
    }

    void ReplaceAll()
    {
        if (editor is null || FindBox.Text.Length == 0) return;
        // Tanpa batas MaxResults: batas itu hanya untuk penanda/daftar hasil; Ganti Semua harus menjangkau semuanya.
        if (!SearchEngine.TryFindAll(editor.Document.Text, FindBox.Text, Options, out var all, out var error, ReplaceBox.Text, int.MaxValue))
        {
            CountText.Text = error;
            return;
        }
        if (all.Count == 0) return;

        var document = editor.Document;
        document.BeginUpdate(); // satu langkah Undo
        try
        {
            for (var i = all.Count - 1; i >= 0; i--) document.Replace(all[i].Offset, all[i].Length, all[i].Replacement);
        }
        finally
        {
            document.EndUpdate();
        }

        Refresh(selectCurrent: false);
        CountText.Text = $"{all.Count} diganti";
    }

    // ---- Event ----

    void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (editor is null) return;
        timedOutKey = null; // pola berubah: boleh dicoba lagi
        refreshTimer.Stop();
        queryTimer.Stop();
        if (FindBox.Text.Length == 0)
        {
            Refresh(selectCurrent: true); // mengosongkan penanda: murah, tidak perlu ditunda
            return;
        }
        queryTimer.Start();
    }

    void Options_Changed(object sender, RoutedEventArgs e)
    {
        if (editor is null) return;
        timedOutKey = null; // opsi berubah: boleh dicoba lagi
        queryTimer.Stop();
        Refresh(selectCurrent: true);
    }

    void ReplaceToggle_Changed(object sender, RoutedEventArgs e) =>
        ReplaceRow.Visibility = ReplaceToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    void Next_Click(object sender, RoutedEventArgs e) => FindNext(forward: true);

    void Previous_Click(object sender, RoutedEventArgs e) => FindNext(forward: false);

    void Replace_Click(object sender, RoutedEventArgs e) => ReplaceCurrent();

    void ReplaceAll_Click(object sender, RoutedEventArgs e) => ReplaceAll();

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void ReplaceBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ReplaceCurrent();
        e.Handled = true;
    }

    void Bar_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && ReferenceEquals(e.OriginalSource, FindBox))
        {
            FindNext(forward: (Keyboard.Modifiers & ModifierKeys.Shift) == 0);
            e.Handled = true;
        }
    }
}

/// <summary>Menggambar latar penanda hasil cari (semua hasil + hasil terpilih) di lapisan Background editor.</summary>
sealed class SearchResultsRenderer : IBackgroundRenderer
{
    IReadOnlyList<SearchMatch> matches = [];
    int current = -1;

    public KnownLayer Layer => KnownLayer.Background;

    public void Update(IReadOnlyList<SearchMatch> newMatches, int currentIndex)
    {
        matches = newMatches;
        current = currentIndex;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (matches.Count == 0 || !textView.VisualLinesValid || textView.VisualLines.Count == 0) return;

        var visible = textView.VisualLines;
        var viewStart = visible[0].FirstDocumentLine.Offset;
        var viewEnd = visible[^1].LastDocumentLine.EndOffset;

        var all = new BackgroundGeometryBuilder { CornerRadius = 2, AlignToWholePixels = true };
        BackgroundGeometryBuilder? currentBuilder = null;

        var first = SearchEngine.IndexAtOrAfter(matches, viewStart);
        // Hasil sebelum viewStart yang masih menjorok ke area terlihat sangat jarang; cukup mundur satu.
        for (var i = Math.Max(0, (first < 0 ? matches.Count : first) - 1); i < matches.Count && matches[i].Offset <= viewEnd; i++)
        {
            var segment = new TextSegment { StartOffset = matches[i].Offset, Length = matches[i].Length };
            if (i == current)
            {
                currentBuilder = new BackgroundGeometryBuilder { CornerRadius = 2, AlignToWholePixels = true };
                currentBuilder.AddSegment(textView, segment);
            }
            else
            {
                all.AddSegment(textView, segment);
            }
        }

        Draw(drawingContext, all, "SearchMatchBrush");
        if (currentBuilder is not null) Draw(drawingContext, currentBuilder, "SearchCurrentBrush");
    }

    static void Draw(DrawingContext context, BackgroundGeometryBuilder builder, string brushKey)
    {
        if (Application.Current?.TryFindResource(brushKey) is not Brush brush) return;
        if (builder.CreateGeometry() is { } geometry) context.DrawGeometry(brush, null, geometry);
    }
}
