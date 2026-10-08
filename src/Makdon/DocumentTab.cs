using System.ComponentModel;
using System.Text;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;

namespace Makdon;

public enum ViewMode { Edit, Split, Preview }

/// <summary>Jawaban pengguna saat menyimpan file yang sudah diubah program lain sejak dimuat.</summary>
public enum SaveConflictChoice { Overwrite, Reload, Cancel }

/// <summary>Dilempar <see cref="DocumentTab.SaveTo"/> bila file di disk berubah dan tidak ada penangan <see cref="DocumentTab.SaveConflict"/>.</summary>
public sealed class ExternalChangeException(string message) : IOException(message);

/// <summary>Satu tab = satu dokumen: teks, path, encoding, dan view-nya.</summary>
public sealed class DocumentTab : INotifyPropertyChanged, IDisposable
{
    // Editor sering menulis file bertahap (hapus/ganti nama/tulis); tunggu sebentar sebelum membaca.
    static readonly TimeSpan ChangeDebounce = TimeSpan.FromMilliseconds(400);
    const int MaxChangeRetries = 5;

    static int untitledCount;

    readonly string untitledName;
    readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    readonly DispatcherTimer changeTimer;
    string? filePath;
    ViewMode mode = ViewMode.Split;
    string caretText = "Brs 1, Kol 1";
    string statsText = TextStats.Format(0, 0);

    // Hash byte file terakhir yang kita ketahui (saat dimuat/disimpan/dimuat ulang/dipertahankan pengguna).
    // Perubahan disk dianggap nyata hanya bila hash-nya berbeda, sehingga simpan oleh aplikasi
    // sendiri atau sekadar "touch" tidak memicu prompt palsu. diskStamp (ukuran + waktu tulis) mendampinginya
    // supaya pemeriksaan berulang tidak perlu membaca file besar bila jelas tidak berubah.
    string? diskHash;
    FileStamp? diskStamp;

    // Hash/stempel perubahan eksternal yang sudah dilaporkan lewat ExternalChangeConflict tetapi belum dijawab pengguna.
    // diskHash sengaja tidak diperbarui sebelum ada jawaban, supaya simpan berikutnya tetap mendeteksi konflik.
    string? pendingHash;
    FileStamp? pendingStamp;

    FileSystemWatcher? watcher;
    int changeRetries;
    bool disposed;
    // true selama SaveTo (termasuk dialog konflik simpan yang memompa pesan): pemeriksaan perubahan eksternal ditunda
    // supaya tidak ada dialog konflik kedua untuk file yang sama dan tidak ada keputusan berdasarkan isi disk yang basi.
    bool saving;

    DocumentTab(string? path, string text, Encoding encoding, string? diskHash, FileStamp? diskStamp, bool lossy)
    {
        filePath = path;
        this.diskHash = diskHash;
        this.diskStamp = diskStamp;
        IsLossyDecoded = lossy;
        untitledName = path is null ? $"Tanpa Judul-{++untitledCount}" : "";
        Encoding = encoding;
        Document = new TextDocument(text);
        Document.UndoStack.ClearAll();
        Document.UndoStack.MarkAsOriginalFile();
        Document.UndoStack.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Document.UndoStack.IsOriginalFile)) RaiseTitleChanged();
        };
        View = new DocumentView(this);

        changeTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = ChangeDebounce };
        changeTimer.Tick += (_, _) => { changeTimer.Stop(); CheckExternalChange(); };
        StartWatching();
    }

    public TextDocument Document { get; }
    public Encoding Encoding { get; private set; }
    public DocumentView View { get; }

    public string? FilePath => filePath;

    /// <summary>true bila file ber-BOM berisi byte tak valid yang sudah diganti U+FFFD saat dimuat (menyimpan akan menetapkannya).</summary>
    public bool IsLossyDecoded { get; private set; }

    /// <summary>Ada perubahan eksternal yang sudah dilaporkan tetapi belum dijawab pengguna.</summary>
    public bool HasExternalConflict => pendingHash is not null;

    public bool IsDirty => !Document.UndoStack.IsOriginalFile;
    public string Title => filePath is null ? untitledName : Path.GetFileName(filePath);
    public string DisplayTitle => (IsDirty ? "● " : "") + Title;
    public string WindowTitle => $"{DisplayTitle} — Makdon";
    public string PathText => filePath ?? "Belum disimpan";

    public string CaretText
    {
        get => caretText;
        private set { caretText = value; Raise(nameof(CaretText)); }
    }

    /// <summary>Jumlah kata dan karakter untuk status bar; diperbarui oleh view (didebounce).</summary>
    public string StatsText
    {
        get => statsText;
        private set { statsText = value; Raise(nameof(StatsText)); }
    }

    /// <summary>Nama encoding untuk status bar, mis. "UTF-8".</summary>
    public string EncodingLabel => EncodingNames.Describe(Encoding);

    public ViewMode Mode
    {
        get => mode;
        set { if (mode == value) return; mode = value; Raise(nameof(Mode)); }
    }

    /// <summary>Dipicu preview saat pengguna mengklik tautan relatif ke file markdown lain (path, anchor atau null).</summary>
    public event Action<string, string?>? OpenLinkRequested;

    /// <summary>
    /// File berubah di disk padahal tab punya perubahan belum disimpan. Penanganan: panggil
    /// <see cref="Reload"/> bila pengguna memilih muat ulang, atau <see cref="KeepEditorVersion"/> bila memilih
    /// mempertahankan versi editor. Selama belum dijawab, perubahan itu tetap dianggap konflik (mis. saat menyimpan).
    /// Tab yang tidak dirty dimuat ulang otomatis tanpa event ini.
    /// </summary>
    public event Action<DocumentTab>? ExternalChangeConflict;

    /// <summary>
    /// <see cref="SaveTo"/> menemukan file di disk berbeda dari yang terakhir dimuat/disimpan. Penangan memilih
    /// menimpa, memuat ulang dari disk, atau membatalkan. Tanpa penangan, SaveTo melempar <see cref="ExternalChangeException"/>.
    /// </summary>
    public event Func<DocumentTab, SaveConflictChoice>? SaveConflict;

    public event PropertyChangedEventHandler? PropertyChanged;

    public static DocumentTab CreateUntitled() => new(null, "", new UTF8Encoding(false), null, null, false);

    public static DocumentTab Load(string path)
    {
        var stamp = FileStamp.TryReadQuietly(path); // diambil sebelum membaca: perubahan di antaranya tetap terdeteksi
        var bytes = TextFileIO.ReadBytes(path);
        var (text, encoding) = TextFileIO.Decode(bytes, out var lossy);
        return new DocumentTab(path, text, encoding, TextFileIO.Hash(bytes), stamp, lossy);
    }

    /// <summary>
    /// Menyimpan secara atomik (file sementara lalu ganti). Encoding &amp; BOM dipertahankan.
    /// Bila menyimpan ke file yang sama dan isinya di disk sudah diubah program lain sejak dimuat/disimpan, tidak menimpa
    /// diam-diam: <see cref="SaveConflict"/> ditanyakan (tanpa penangan: <see cref="ExternalChangeException"/>).
    /// Mengembalikan false bila tidak ada yang ditulis (pengguna membatalkan atau memilih muat ulang dari disk).
    /// </summary>
    public bool SaveTo(string path)
    {
        saving = true;
        changeTimer.Stop();
        try { return SaveCore(path); }
        finally { saving = false; }
    }

    bool SaveCore(string path)
    {
        var samePath = filePath is not null && string.Equals(filePath, path, StringComparison.OrdinalIgnoreCase);
        if (samePath && diskHash is not null && IsChangedOnDisk(path))
        {
            var choice = SaveConflict?.Invoke(this)
                ?? throw new ExternalChangeException(
                    $"File \"{Path.GetFileName(path)}\" diubah program lain sejak dimuat; penyimpanan dibatalkan agar perubahan itu tidak tertimpa.");
            switch (choice)
            {
                case SaveConflictChoice.Reload:
                    // Baca ulang sekarang: dialog bisa terbuka lama dan file bisa berubah lagi sejak konflik terdeteksi.
                    Reload();
                    return false;
                case SaveConflictChoice.Cancel:
                    return false;
            }
        }

        var oldLabel = EncodingLabel;
        var (actual, hash) = TextFileIO.Write(path, Document.Text, Encoding);
        Encoding = actual;
        diskHash = hash;
        diskStamp = FileStamp.TryReadQuietly(path);
        pendingHash = null;
        pendingStamp = null;
        IsLossyDecoded = false;

        var pathChanged = !string.Equals(filePath, path, StringComparison.Ordinal);
        var watchChanged = !samePath || watcher is null;
        filePath = path;
        Document.UndoStack.MarkAsOriginalFile();
        if (watchChanged) StartWatching();

        // Hanya bila benar-benar berubah: FilePath memicu render ulang pratinjau, jangan terjadi di tiap Ctrl+S.
        if (pathChanged)
        {
            Raise(nameof(FilePath));
            Raise(nameof(PathText));
        }
        if (EncodingLabel != oldLabel) Raise(nameof(EncodingLabel));
        RaiseTitleChanged();
        return true;
    }

    /// <summary>
    /// Memuat ulang isi dari disk menggantikan isi editor. Penggantian adalah satu langkah Undo, jadi perubahan editor
    /// tidak hilang permanen bila pengguna salah memilih. Melempar IOException bila gagal dibaca.
    /// </summary>
    public void Reload()
    {
        if (filePath is null) return;
        var stamp = FileStamp.TryReadQuietly(filePath);
        ApplyDiskContent(TextFileIO.ReadBytes(filePath), stamp);
    }

    /// <summary>Pengguna memilih mempertahankan versi editor: isi disk saat ini diterima sebagai dasar perbandingan berikutnya.</summary>
    public void KeepEditorVersion()
    {
        if (pendingHash is null) return;
        diskHash = pendingHash;
        diskStamp = pendingStamp;
        pendingHash = null;
        pendingStamp = null;
    }

    /// <summary>
    /// Memeriksa apakah file berubah dari luar. Dipanggil oleh watcher (didebounce) dan boleh
    /// dipanggil saat jendela aktif kembali sebagai cadangan.
    /// </summary>
    public void CheckExternalChange()
    {
        if (disposed || saving || filePath is null || diskHash is null) return;

        FileStamp? stamp;
        byte[] bytes;
        try
        {
            stamp = FileStamp.TryRead(filePath);
            if (stamp is null) return; // Dihapus/dipindah: biarkan isi editor apa adanya.
            // Ukuran + waktu tulis sama dengan keadaan yang sudah kita ketahui: lewati baca & hash (file besar, tiap Activated).
            if (IsKnownDiskState(stamp.Value)) return;
            bytes = TextFileIO.ReadBytes(filePath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return; // Dihapus/dipindah: biarkan isi editor apa adanya.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Mungkin masih dikunci penulisnya; coba lagi sebentar lagi.
            if (++changeRetries < MaxChangeRetries) { changeTimer.Stop(); changeTimer.Start(); }
            return;
        }

        var hash = TextFileIO.Hash(bytes);
        if (hash == diskHash)
        {
            // Isi sama dengan yang diketahui (mis. hanya "touch", atau perubahan eksternal dikembalikan).
            diskStamp = stamp;
            pendingHash = null;
            pendingStamp = null;
            return;
        }
        if (hash == pendingHash)
        {
            pendingStamp = stamp; // sudah dilaporkan; jangan tanya lagi
            return;
        }

        if (!IsDirty)
        {
            ApplyDiskContent(bytes, stamp);
            return;
        }

        // diskHash tidak diubah: baru diperbarui setelah pengguna menjawab (Reload/KeepEditorVersion) atau menyimpan.
        pendingHash = hash;
        pendingStamp = stamp;
        ExternalChangeConflict?.Invoke(this);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        changeTimer.Stop();
        StopWatching();
        View.Dispose();
    }

    /// <summary>Ekspor isi dokumen ke HTML mandiri. Melempar IOException/UnauthorizedAccessException bila gagal.</summary>
    public void ExportHtml(string outputPath) =>
        HtmlExporter.ExportToFile(Document.Text, outputPath,
            Path.GetFileNameWithoutExtension(Title),
            filePath is null ? null : Path.GetDirectoryName(filePath));

    internal void SetCaret(int line, int column) => CaretText = $"Brs {line}, Kol {column}";

    internal void SetStats(int words, int characters) => StatsText = TextStats.Format(words, characters);

    internal void RequestOpen(string path, string? anchor) => OpenLinkRequested?.Invoke(path, anchor);

    bool IsKnownDiskState(FileStamp stamp) =>
        (diskStamp is { IsReliable: true } known && known.SameFileAs(stamp))
        || (pendingStamp is { IsReliable: true } pending && pending.SameFileAs(stamp));

    // true bila isi file di disk berbeda dari diskHash; false bila sama, tidak ada, atau sudah dihapus.
    bool IsChangedOnDisk(string path)
    {
        var stamp = FileStamp.TryRead(path);
        if (stamp is null) return false;
        if (diskStamp is { IsReliable: true } known && known.SameFileAs(stamp.Value)) return false;

        byte[] bytes;
        try { bytes = TextFileIO.ReadBytes(path); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }

        if (TextFileIO.Hash(bytes) == diskHash)
        {
            diskStamp = stamp;
            return false;
        }
        return true;
    }

    // Teks diganti sebagai satu langkah Undo (muat ulang otomatis tab bersih maupun pilihan pengguna di dialog konflik),
    // supaya edit yang terbuang karena salah pilih masih bisa dikembalikan.
    void ApplyDiskContent(byte[] bytes, FileStamp? stamp)
    {
        var (text, encoding) = TextFileIO.Decode(bytes, out var lossy);
        diskHash = TextFileIO.Hash(bytes);
        diskStamp = stamp;
        pendingHash = null;
        pendingStamp = null;
        var oldLabel = EncodingLabel;
        Encoding = encoding;
        IsLossyDecoded = lossy;

        var caret = View.CaretOffset;
        Document.UndoStack.StartUndoGroup();
        try { Document.Replace(0, Document.TextLength, text); }
        finally { Document.UndoStack.EndUndoGroup(); }
        Document.UndoStack.MarkAsOriginalFile();
        View.CaretOffset = caret; // diklem ke panjang teks baru

        if (EncodingLabel != oldLabel) Raise(nameof(EncodingLabel));
        RaiseTitleChanged();
    }

    void StartWatching()
    {
        StopWatching();
        if (disposed || filePath is null) return;

        try
        {
            // Bila file adalah symlink, perubahan terjadi pada file sebenarnya (di folder lain): awasi itu.
            var watched = TextFileIO.ResolveLinkTarget(Path.GetFullPath(filePath));
            var directory = Path.GetDirectoryName(watched)!;
            var w = new FileSystemWatcher(directory, Path.GetFileName(watched))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            };
            w.Changed += OnDiskEvent;
            w.Created += OnDiskEvent;
            w.Renamed += OnDiskEvent;
            w.EnableRaisingEvents = true;
            watcher = w;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Folder tak bisa diawasi (mis. share jaringan); deteksi tetap jalan lewat
            // CheckExternalChange() saat jendela aktif kembali.
            watcher = null;
        }
    }

    void StopWatching()
    {
        if (watcher is null) return;
        watcher.EnableRaisingEvents = false;
        watcher.Dispose();
        watcher = null;
    }

    // Berjalan di thread pool: pindahkan ke UI thread dan debounce.
    void OnDiskEvent(object sender, FileSystemEventArgs e) =>
        dispatcher.BeginInvoke(() =>
        {
            if (disposed) return;
            changeRetries = 0;
            changeTimer.Stop();
            changeTimer.Start();
        });

    void RaiseTitleChanged()
    {
        Raise(nameof(IsDirty));
        Raise(nameof(Title));
        Raise(nameof(DisplayTitle));
        Raise(nameof(WindowTitle));
    }

    void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
