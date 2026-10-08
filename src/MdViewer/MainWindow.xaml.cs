using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MdViewer;

public partial class MainWindow : Window
{
    const string AppName = "MdViewer";
    const string OpenFilter = "Markdown (*.md;*.markdown;*.mdown;*.mkd)|*.md;*.markdown;*.mdown;*.mkd|Teks (*.txt)|*.txt|Semua file (*.*)|*.*";
    const string SaveFilter = "Markdown (*.md)|*.md|Teks (*.txt)|*.txt|Semua file (*.*)|*.*";

    // File di atas ConfirmOpenBytes ditanyakan dulu; di atas MaxOpenBytes ditolak (dibaca penuh ke memori, lalu didekode).
    const long ConfirmOpenBytes = 50L * 1024 * 1024;
    const long MaxOpenBytes = 500L * 1024 * 1024;

    readonly ObservableCollection<DocumentTab> tabs = [];
    readonly AppSettings settings = AppSettings.Load();
    // Konflik perubahan eksternal diantrekan dan ditanyakan satu per satu (tidak ada yang hilang diam-diam).
    readonly Queue<DocumentTab> conflictQueue = new();
    bool conflictPromptOpen;
    // Instance yang dibuka lewat argumen hanya berisi file itu; jangan menimpa sesi tersimpan saat keluar. Begitu pengguna
    // membuka tab lagi (kiriman instance lain, dialog Buka, drag-drop, Berkas Terakhir), instance dianggap "diadopsi":
    // ia sudah menjadi ruang kerja biasa, jadi sesinya disimpan seperti instance tanpa argumen.
    bool preserveStoredSession;
    // Jendela sedang/sudah ditutup: kiriman file dari instance lain diabaikan (tab baru tak akan sempat ditanyakan/disimpan).
    bool closing;
    bool closed;
    int zoomPercent;

    public MainWindow(IEnumerable<string> startupFiles)
    {
        // Tema harus terpasang sebelum XAML dimuat supaya DynamicResource langsung menemukan brush-nya.
        ThemeManager.Apply(settings.ParsedTheme);
        zoomPercent = ZoomLevel.Clamp(settings.ZoomPercent);
        DocumentView.BlockRemoteImages = settings.BlockRemoteImages;

        InitializeComponent();
        LoadRemoteImagesItem.IsChecked = !settings.BlockRemoteImages;
        RecentFiles = new ObservableCollection<string>(settings.RecentFiles);
        Tabs.ItemsSource = tabs;
        tabs.CollectionChanged += (_, _) => UpdateEmptyState();
        UpdateEmptyState();
        UpdateZoomText();
        UpdateThemeChecks();
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        ThemeManager.ThemeChanged += OnThemeChanged;
        Activated += (_, _) => Current?.CheckExternalChange();
        Closed += Window_Closed;

        var files = startupFiles.ToList();
        preserveStoredSession = files.Count > 0;
        if (files.Count > 0) foreach (var file in files) OpenFile(file);
        else RestoreSession();
    }

    /// <summary>Dipanggil saat instance lain diluncurkan dengan file: buka di sini lalu bawa jendela ke depan.</summary>
    public void OpenFromOtherInstance(IEnumerable<string> files)
    {
        if (closing || closed) return;

        foreach (var file in files) OpenUserFile(file);

        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        // Aktivasi saja bisa hanya mengedipkan taskbar; menaikkan Topmost sebentar membawa jendela ke depan.
        Topmost = true;
        Topmost = false;
    }

    // Pembukaan atas kehendak pengguna (bukan sesi/startup): menandai instance sebagai diadopsi bila tab benar-benar terbuka.
    DocumentTab? OpenUserFile(string path)
    {
        var tab = OpenFile(path);
        if (tab is not null) preserveStoredSession = false;
        return tab;
    }

    /// <summary>Daftar berkas terakhir dibuka (terbaru di atas, maks 10) untuk menu "Berkas Terakhir".</summary>
    public ObservableCollection<string> RecentFiles { get; }

    // Tabs masih null selama InitializeComponent(): tombol toolbar sudah memanggil CanExecute saat itu.
    DocumentTab? Current => Tabs?.SelectedItem as DocumentTab;

    // ---- Keadaan kosong, tema, zoom ----

    void UpdateEmptyState()
    {
        var empty = tabs.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        Tabs.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        Status.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        RecentPanel.Visibility = RecentFiles is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnThemeChanged()
    {
        foreach (var tab in tabs) tab.View.RefreshTheme();
        UpdateThemeChecks();
    }

    void UpdateThemeChecks()
    {
        ThemeLightItem.IsChecked = ThemeManager.Mode == AppThemeMode.Light;
        ThemeDarkItem.IsChecked = ThemeManager.Mode == AppThemeMode.Dark;
        ThemeSystemItem.IsChecked = ThemeManager.Mode == AppThemeMode.System;
    }

    void SetTheme_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var mode = ThemeManager.Parse(e.Parameter as string);
        settings.Theme = mode.ToString();
        ThemeManager.Apply(mode);
    }

    void ChangeZoom(int direction) => SetZoom(ZoomLevel.Step(zoomPercent, direction));

    void LoadRemoteImages_Click(object sender, RoutedEventArgs e)
    {
        // IsCheckable: IsChecked sudah berganti sebelum Click.
        settings.BlockRemoteImages = !LoadRemoteImagesItem.IsChecked;
        DocumentView.BlockRemoteImages = settings.BlockRemoteImages;
        foreach (var tab in tabs) tab.View.RefreshPreview();
    }

    void SetZoom(int percent)
    {
        zoomPercent = ZoomLevel.Clamp(percent);
        foreach (var tab in tabs) tab.View.ApplyZoom(zoomPercent);
        UpdateZoomText();
    }

    void UpdateZoomText() => ZoomText.Text = ZoomLevel.Format(zoomPercent);

    void Zoom_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command == AppCommands.ZoomIn) ChangeZoom(+1);
        else if (e.Command == AppCommands.ZoomOut) ChangeZoom(-1);
        else SetZoom(ZoomLevel.Default);
    }

    // ---- Berkas terakhir & sesi ----

    /// <summary>Membuka berkas dari daftar terakhir; bila sudah tidak ada, dihapus dari daftar.</summary>
    public void OpenRecent(string path)
    {
        if (!File.Exists(path))
        {
            MessageBox.Show(this, $"File tidak ditemukan:\n{path}", AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            settings.RemoveRecent(path);
            SyncRecentFiles();
            return;
        }
        OpenUserFile(path);
    }

    void TrackRecent(string path)
    {
        settings.AddRecent(path);
        SyncRecentFiles();
    }

    void SyncRecentFiles()
    {
        if (RecentFiles.SequenceEqual(settings.RecentFiles)) return;
        RecentFiles.Clear();
        foreach (var file in settings.RecentFiles) RecentFiles.Add(file);
        UpdateEmptyState();
    }

    void RecentMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is string path) OpenRecent(path);
        e.Handled = true;
    }

    void RecentItem_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is string path) OpenRecent(path);
    }

    void RestoreSession()
    {
        if (settings.Session is not { Tabs.Count: > 0 } session) return;

        DocumentTab? active = null;
        for (var i = 0; i < session.Tabs.Count; i++)
        {
            var saved = session.Tabs[i];
            if (!File.Exists(saved.Path)) continue;

            var tab = OpenFile(saved.Path, trackRecent: false);
            if (tab is null) continue;
            tab.Mode = saved.ParsedMode;
            tab.View.RestoreCaret(saved.CaretOffset);
            if (i == session.ActiveIndex) active = tab;
        }

        if (active is not null) Tabs.SelectedItem = active;
    }

    /// <summary>Menyimpan sesi (dipakai saat galat fatal). Tidak melempar; dilewati bila jendela belum selesai dimuat.</summary>
    public void TrySaveSession()
    {
        if (!IsLoaded) return;
        try { SaveSettings(forceSession: true); } // pemulihan galat fatal: sesi selalu disimpan
        catch (Exception ex) { CrashLog.Write("Gagal menyimpan sesi", ex); }
    }

    void SaveSettings(bool forceSession = false)
    {
        settings.Theme = ThemeManager.Mode.ToString();
        settings.ZoomPercent = zoomPercent;

        var keepStoredSession = preserveStoredSession && !forceSession;
        if (!keepStoredSession)
        {
            var saved = tabs.Where(t => t.FilePath is not null).ToList();
            settings.Session = new SessionState
            {
                Tabs = saved.Select(t => new SessionTab { Path = t.FilePath!, Mode = t.Mode.ToString(), CaretOffset = t.View.CaretOffset }).ToList(),
                ActiveIndex = Math.Max(0, saved.IndexOf(Current!)),
            };
        }
        // Muat ulang & gabungkan: instance lain mungkin sudah menulis file ini sejak kita memuatnya.
        settings.SaveMerged(keepStoredSession: keepStoredSession);
    }

    // ---- Membuka / membuat tab ----

    DocumentTab? OpenFile(string path, string? anchor = null, bool trackRecent = true)
    {
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ShowError($"Path tidak valid:\n{path}", ex);
            return null;
        }

        var existing = tabs.FirstOrDefault(t => string.Equals(t.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Tabs.SelectedItem = existing;
            if (trackRecent) TrackRecent(fullPath);
            existing.View.ScrollToAnchor(anchor);
            return existing;
        }

        if (!ConfirmFileSize(fullPath)) return null;

        DocumentTab tab;
        try { tab = DocumentTab.Load(fullPath); }
        catch (OutOfMemoryException ex)
        {
            ShowError($"Memori tidak cukup untuk membuka file ini:\n{fullPath}", ex);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Tidak bisa membuka file:\n{fullPath}", ex);
            return null;
        }

        Attach(tab);
        AddTab(tab);
        if (trackRecent) TrackRecent(fullPath);
        tab.View.ScrollToAnchor(anchor);
        return tab;
    }

    // File sangat besar dibaca penuh ke memori dan didekode: tanyakan dulu (> 50 MB) atau tolak (> 500 MB).
    bool ConfirmFileSize(string fullPath)
    {
        long length;
        try
        {
            var info = new FileInfo(fullPath);
            if (!info.Exists) return true; // galatnya dilaporkan oleh Load
            length = info.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }

        var megabytes = length / (1024.0 * 1024.0);
        if (length > MaxOpenBytes)
        {
            MessageBox.Show(this, $"File terlalu besar ({megabytes:N0} MB) untuk dibuka. Batasnya {MaxOpenBytes / (1024 * 1024)} MB.\n\n{fullPath}",
                AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (length <= ConfirmOpenBytes) return true;

        return MessageBox.Show(this,
            $"File ini besar ({megabytes:N0} MB). Membukanya bisa lambat dan memakan banyak memori.\n\n{fullPath}\n\nTetap buka?",
            AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    void Attach(DocumentTab tab)
    {
        tab.View.ApplyZoom(zoomPercent);
        tab.View.ZoomRequested += ChangeZoom;
        tab.OpenLinkRequested += (path, anchor) => OpenFile(path, anchor);
        tab.ExternalChangeConflict += OnExternalChangeConflict;
        tab.SaveConflict += OnSaveConflict;
    }

    // File berubah di disk sementara tab punya perubahan yang belum disimpan: antrekan lalu tanya pengguna satu per satu.
    void OnExternalChangeConflict(DocumentTab tab)
    {
        if (!conflictQueue.Contains(tab)) conflictQueue.Enqueue(tab);
        ProcessConflictQueue();
    }

    // Dialog bersifat modal tetapi tetap memompa pesan, jadi konflik tab lain dapat datang saat dialog terbuka:
    // itu hanya masuk antrean, dan diproses oleh putaran ini setelah dialog sebelumnya tertutup.
    void ProcessConflictQueue()
    {
        if (conflictPromptOpen) return;
        conflictPromptOpen = true;
        try
        {
            while (conflictQueue.Count > 0)
            {
                var tab = conflictQueue.Dequeue();
                // Tab sudah ditutup, atau konflik sudah selesai (disimpan/dimuat ulang/isi disk kembali sama).
                if (!tabs.Contains(tab) || !tab.HasExternalConflict) continue;
                AskAboutExternalChange(tab);
            }
        }
        finally
        {
            conflictPromptOpen = false;
        }
    }

    void AskAboutExternalChange(DocumentTab tab)
    {
        Tabs.SelectedItem = tab;
        var reload = ChoiceDialog.Show(this, AppName,
            $"File \"{tab.Title}\" diubah oleh program lain, sedangkan tab ini punya perubahan yang belum disimpan.\n\n" +
            "Muat dari Disk: isi editor diganti dengan versi di disk (bisa dikembalikan dengan Ctrl+Z).\n" +
            "Pertahankan Editor: tetap memakai versi di editor; menyimpan nanti akan menimpa versi di disk.",
            cancelValue: false,
            new DialogChoice<bool>("Muat dari Disk", true),
            new DialogChoice<bool>("Pertahankan Editor", false, IsDefault: true));

        if (!reload)
        {
            tab.KeepEditorVersion();
            return;
        }

        try { tab.Reload(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Gagal memuat ulang:\n{tab.FilePath}", ex);
        }
    }

    // Menyimpan ke file yang sudah diubah program lain sejak dibuka: jangan menimpa diam-diam.
    // Memakai penanda conflictPromptOpen yang sama dengan konflik eksternal: selama dialog terbuka, konflik tab lain hanya
    // masuk antrean (diproses TrySave setelah SaveTo selesai), jadi tidak ada dialog yang bertumpuk.
    SaveConflictChoice OnSaveConflict(DocumentTab tab)
    {
        Tabs.SelectedItem = tab;
        var wasOpen = conflictPromptOpen;
        conflictPromptOpen = true;
        try
        {
            return ChoiceDialog.Show(this, AppName,
                $"File \"{tab.Title}\" sudah diubah oleh program lain sejak dibuka di MdViewer.\n\n" +
                "Timpa: simpan versi di editor dan buang perubahan program lain.\n" +
                "Muat dari Disk: ganti isi editor dengan versi di disk (bisa dikembalikan dengan Ctrl+Z).\n" +
                "Batal: jangan simpan.",
                cancelValue: SaveConflictChoice.Cancel,
                new DialogChoice<SaveConflictChoice>("Timpa", SaveConflictChoice.Overwrite),
                new DialogChoice<SaveConflictChoice>("Muat dari Disk", SaveConflictChoice.Reload),
                new DialogChoice<SaveConflictChoice>("Batal", SaveConflictChoice.Cancel, IsDefault: true));
        }
        finally
        {
            conflictPromptOpen = wasOpen;
        }
    }

    void AddTab(DocumentTab tab)
    {
        tabs.Add(tab);
        Tabs.SelectedItem = tab;
    }

    // ---- Menyimpan / menutup ----

    /// <summary>Mengembalikan true bila dokumen tersimpan (false: dibatalkan atau gagal).</summary>
    bool Save(DocumentTab tab) => tab.FilePath is null ? SaveAs(tab) : TrySave(tab, tab.FilePath);

    bool SaveAs(DocumentTab tab)
    {
        var dialog = new SaveFileDialog
        {
            Filter = SaveFilter,
            DefaultExt = ".md",
            AddExtension = true,
            FileName = tab.Title,
            InitialDirectory = tab.FilePath is null ? "" : Path.GetDirectoryName(tab.FilePath),
        };
        if (dialog.ShowDialog(this) != true) return false;

        var target = Path.GetFullPath(dialog.FileName);
        var clash = tabs.FirstOrDefault(t => t != tab && string.Equals(t.FilePath, target, StringComparison.OrdinalIgnoreCase));
        if (clash is not null)
        {
            MessageBox.Show(this, $"File ini sedang terbuka di tab lain:\n{target}", AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return TrySave(tab, target);
    }

    bool TrySave(DocumentTab tab, string path)
    {
        if (tab.IsLossyDecoded && MessageBox.Show(this,
                "File berisi byte tidak valid yang akan diganti U+FFFD. Simpan juga?",
                AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return false;

        try
        {
            return tab.SaveTo(path); // false: dibatalkan, atau dimuat ulang dari disk karena konflik
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Gagal menyimpan:\n{path}", ex);
            return false;
        }
        finally
        {
            // Konflik tab lain yang datang selama SaveTo (dan dialognya) hanya diantrekan; tanyakan sekarang.
            if (!closing) ProcessConflictQueue();
        }
    }

    /// <summary>Menanyakan penyimpanan bila ada perubahan. false = pengguna membatalkan.</summary>
    bool ConfirmDiscardOrSave(DocumentTab tab)
    {
        if (!tab.IsDirty) return true;

        Tabs.SelectedItem = tab;
        var answer = MessageBox.Show(this, $"Simpan perubahan pada \"{tab.Title}\"?", AppName,
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => Save(tab),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    void CloseTab(DocumentTab tab)
    {
        if (!ConfirmDiscardOrSave(tab)) return;
        tabs.Remove(tab);
        tab.Dispose();
    }

    void ShowError(string message, Exception ex) =>
        MessageBox.Show(this, $"{message}\n\n{ex.Message}", AppName, MessageBoxButton.OK, MessageBoxImage.Error);

    // ---- Command handlers ----

    void New_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var tab = DocumentTab.CreateUntitled();
        Attach(tab);
        AddTab(tab);
        tab.View.FocusEditor();
    }

    void Open_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = OpenFilter, Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var file in dialog.FileNames) OpenUserFile(file);
    }

    void Save_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Current is { } tab) Save(tab);
    }

    void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Current is { } tab) SaveAs(tab);
    }

    void Close_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Current is { } tab) CloseTab(tab);
    }

    void HasTab_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Current is not null;

    // Ada tab dan editor tampak (bukan mode Pratinjau).
    void CanEdit_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = Current is { Mode: not ViewMode.Preview };

    // Format hanya berlaku saat editor tampak, dan tidak saat fokus ada di panel cari (Ctrl+B di kolom cari tak boleh mengubah dokumen).
    void CanFormat_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = Current is { Mode: not ViewMode.Preview } tab && !tab.View.IsFindBarFocused;

    void Format_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Current is not { } tab || AppCommands.FormatOf(e.Command) is not { } format) return;
        var level = e.Parameter is string text && int.TryParse(text, out var parsed) ? parsed : 1;
        tab.View.ApplyFormat(format, level);
    }

    void HeadingMenu_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Current is null) return;
        var menu = (ContextMenu)FindResource("HeadingMenu");
        menu.PlacementTarget = HeadingButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    void HeadingItem_Click(object sender, RoutedEventArgs e)
    {
        if (Current is { } tab && ((FrameworkElement)sender).Tag is string text && int.TryParse(text, out var level))
            tab.View.ApplyFormat(MarkdownFormat.Heading, level);
    }

    // ---- Cari & ganti ----

    void Find_Executed(object sender, ExecutedRoutedEventArgs e) => Current?.View.ShowFind(replace: false);

    void Replace_Executed(object sender, ExecutedRoutedEventArgs e) => Current?.View.ShowFind(replace: true);

    void FindNext_Executed(object sender, ExecutedRoutedEventArgs e) =>
        Current?.View.FindNext(forward: e.Command == AppCommands.FindNext);

    // ---- Ekspor & cetak ----

    void ExportHtml_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Current is not { } tab) return;

        var dialog = new SaveFileDialog
        {
            Filter = "Halaman web (*.html)|*.html|Semua file (*.*)|*.*",
            DefaultExt = ".html",
            AddExtension = true,
            FileName = Path.GetFileNameWithoutExtension(tab.Title) + ".html",
            InitialDirectory = tab.FilePath is null ? "" : Path.GetDirectoryName(tab.FilePath),
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            tab.ExportHtml(dialog.FileName);
        }
        catch (OutOfMemoryException ex)
        {
            // Dokumen dengan sangat banyak/besar gambar: ekspor berdiri sendiri (tidak mengubah dokumen), jadi cukup dilaporkan.
            CrashLog.Write("Ekspor HTML kehabisan memori", ex);
            ShowError($"Memori tidak cukup untuk mengekspor dokumen ini (mungkin berisi terlalu banyak gambar besar).\n{dialog.FileName}", ex);
        }
        catch (Exception ex)
        {
            // Tidak hanya galat I/O: isi dokumen yang tak terduga (path/URL aneh, dll.) tidak boleh menjatuhkan aplikasi.
            CrashLog.Write("Ekspor HTML gagal", ex);
            ShowError($"Gagal mengekspor HTML:\n{dialog.FileName}", ex);
        }
    }

    void Print_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Current is not { } tab) return;

        try
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return;

            // Ukuran halaman = ukuran media terorientasi dari dialog Cetak; tata letak dan kaki halaman sama dengan Pratinjau Cetak.
            var layout = PageLayout.FromPrintableArea(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
            PrintService.Print(dialog, tab.View.CapturePrintSnapshot(), layout, headerFooter: true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Driver/antrean printer dapat melempar berbagai jenis pengecualian (PrintQueueException, COMException, ...).
            ShowError("Gagal mencetak.", ex);
        }
    }

    // Modal: editor tidak bisa diubah selama pratinjau terbuka, dan pratinjau memakai salinan teks saat dibuka (snapshot).
    void PrintPreview_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Current is not { } tab) return;

        // Hanya pembuatan jendela yang dibungkus catch-all. ShowDialog sengaja di luarnya: galat dari callback dispatcher
        // selama dialog tampil ditangani App.OnDispatcherUnhandledException (dan galat kita sendiri di dalam jendela sudah
        // ditangkap PreviewBuild/PrintPreviewWindow); menelannya di sini hanya meninggalkan aplikasi yang sudah dinyatakan akan ditutup.
        PrintPreviewWindow window;
        try
        {
            window = new PrintPreviewWindow(tab.View.CapturePrintSnapshot()) { Owner = this };
        }
        catch (OutOfMemoryException ex)
        {
            CrashLog.Write("Pratinjau cetak kehabisan memori", ex);
            ShowError("Memori tidak cukup untuk menyusun pratinjau cetak dokumen ini.", ex);
            return;
        }
        catch (Exception ex)
        {
            // Dokumen/driver yang tak terduga tidak boleh menjatuhkan aplikasi; pratinjau tidak mengubah dokumen.
            CrashLog.Write("Pratinjau cetak gagal", ex);
            ShowError("Gagal membuka pratinjau cetak.", ex);
            return;
        }

        window.ShowDialog();
    }

    void ViewMode_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Current is not { } tab) return;
        tab.Mode = ((RoutedUICommand)e.Command).Text switch
        {
            "Editor" => ViewMode.Edit,
            "Pratinjau" => ViewMode.Preview,
            _ => ViewMode.Split,
        };
    }

    void NextTab_Executed(object sender, ExecutedRoutedEventArgs e) => MoveSelection(+1);

    void PreviousTab_Executed(object sender, ExecutedRoutedEventArgs e) => MoveSelection(-1);

    void MoveSelection(int step)
    {
        if (tabs.Count == 0) return;
        Tabs.SelectedIndex = (Tabs.SelectedIndex + step + tabs.Count) % tabs.Count;
    }

    void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // ---- Tab header ----

    void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is DocumentTab tab) CloseTab(tab);
    }

    void TabHeader_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && ((FrameworkElement)sender).DataContext is DocumentTab tab)
        {
            CloseTab(tab);
            e.Handled = true;
        }
    }

    void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged juga naik dari kontrol di dalam tab (mis. daftar di editor); abaikan.
        if (!ReferenceEquals(e.OriginalSource, Tabs)) return;
        CommandManager.InvalidateRequerySuggested();

        // Fokus ke editor tab yang baru aktif supaya langsung bisa mengetik.
        if (Current is { } tab)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, tab.View.FocusEditor);
            (Tabs.ItemContainerGenerator.ContainerFromItem(tab) as FrameworkElement)?.BringIntoView();
        }
    }

    // ---- Drag and drop ----

    void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        e.Handled = true;

        var skipped = new List<string>();
        foreach (var file in files)
        {
            if (File.Exists(file) && MarkdownFiles.IsMarkdown(file)) OpenUserFile(file);
            else skipped.Add(Path.GetFileName(file));
        }

        if (skipped.Count > 0)
            MessageBox.Show(this, "Dilewati (bukan file markdown):\n" + string.Join("\n", skipped), AppName,
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---- Menutup jendela ----

    void Window_Closing(object? sender, CancelEventArgs e)
    {
        closing = true;
        foreach (var tab in tabs.Where(t => t.IsDirty).ToList())
        {
            if (!ConfirmDiscardOrSave(tab))
            {
                e.Cancel = true;
                closing = false; // penutupan dibatalkan: jendela kembali dipakai seperti biasa
                return;
            }
        }
    }

    void Window_Closed(object? sender, EventArgs e)
    {
        closed = true;
        ThemeManager.ThemeChanged -= OnThemeChanged;
        SaveSettings();
        foreach (var tab in tabs) tab.Dispose();
    }
}
