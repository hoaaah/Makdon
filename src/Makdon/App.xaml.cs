using System.Windows;
using System.Windows.Threading;

namespace Makdon;

public partial class App : Application
{
    bool shuttingDownAfterCrash;
    SingleInstance? singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            CrashLog.Write("AppDomain.UnhandledException (proses berakhir)", args.ExceptionObject as Exception
                ?? new InvalidOperationException(args.ExceptionObject?.ToString() ?? "galat tidak dikenal"));

        // Path relatif pada argumen dihitung dari folder kerja proses ini; ubah ke mutlak sebelum diteruskan ke instance lain.
        var files = e.Args.Select(ToFullPath).ToList();

        singleInstance = SingleInstance.Create();
        if (!singleInstance.IsPrimary)
        {
            // Sudah ada Makdon yang berjalan: teruskan file (atau sekadar minta jendelanya aktif) lalu keluar.
            SingleInstance.AllowForeground();
            if (singleInstance.TrySendToPrimary(files))
            {
                Shutdown();
                return;
            }
            // Gagal meneruskan (pipe tak tersedia): tetap buka instance baru supaya file tidak hilang.
        }
        else
        {
            singleInstance.StartServer(OnFilesFromOtherInstance);
        }

        // File yang diberikan lewat argumen ("Open with" / drag ke ikon exe) dibuka sebagai tab.
        var window = new MainWindow(files);
        // Berhenti menerima kiriman file begitu jendela utama tertutup (setelah MainWindow menyimpan sesi): peluncuran
        // berikutnya menjadi instance utama sendiri, bukan menitipkan file ke jendela yang sudah mati.
        window.Closed += (_, _) => singleInstance?.Dispose();
        window.Show();
    }

    static string ToFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path; // MainWindow melaporkan path yang tidak valid
        }
    }

    // Dipanggil di thread latar oleh server pipe.
    void OnFilesFromOtherInstance(IReadOnlyList<string> files) =>
        Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.OpenFromOtherInstance(files));

    /// <summary>
    /// Galat tak tertangani di thread UI: selalu dicatat ke crash.log dan ditampilkan ramah. Hanya galat operasi yang
    /// jelas aman (<see cref="CrashLog.IsRecoverable"/>) yang dilanjutkan; selain itu sesi disimpan dan aplikasi ditutup.
    /// </summary>
    void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var exception = e.Exception;
        var recoverable = CrashLog.IsRecoverable(exception) && !shuttingDownAfterCrash;
        // Galat yang sama berulang cepat: tetap dicatat, tetapi dialog tidak dibuka lagi.
        var showDialog = !recoverable || CrashLog.ShouldShowDialog(exception);
        CrashLog.Write(recoverable
            ? "Galat dipulihkan (DispatcherUnhandledException)" + (showDialog ? "" : "; berulang, dialog tidak ditampilkan")
            : "Galat fatal (DispatcherUnhandledException)", exception);

        // Galat saat proses penutupan akibat galat sebelumnya: jangan membuat putaran dialog, biarkan proses keluar.
        if (shuttingDownAfterCrash) return;

        if (recoverable)
        {
            if (showDialog)
            {
                ShowCrashDialog(
                    "Terjadi kesalahan, tetapi aplikasi dapat dilanjutkan.\n\n" +
                    $"{exception.Message}\n\nRincian dicatat di:\n{CrashLog.LogPath}",
                    MessageBoxImage.Warning);
            }
            e.Handled = true;
            return;
        }

        shuttingDownAfterCrash = true;
        try { (MainWindow as MainWindow)?.TrySaveSession(); }
        catch (Exception ex) { CrashLog.Write("Gagal menyimpan sesi saat galat fatal", ex); }

        ShowCrashDialog(
            "Makdon mengalami kesalahan yang tidak dapat dipulihkan dan akan ditutup. " +
            "Daftar tab yang terbuka sudah disimpan dan akan dibuka lagi saat aplikasi dijalankan ulang.\n\n" +
            $"{exception.GetType().Name}: {exception.Message}\n\nRincian dicatat di:\n{CrashLog.LogPath}",
            MessageBoxImage.Error);

        // Handled = false: biarkan runtime mengakhiri proses (keadaan program tidak lagi dipercaya).
        e.Handled = false;
    }

    static void ShowCrashDialog(string message, MessageBoxImage icon)
    {
        try { MessageBox.Show(message, "Makdon", MessageBoxButton.OK, icon); }
        catch (Exception ex) { CrashLog.Write("Gagal menampilkan dialog galat", ex); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeManager.Shutdown();
        singleInstance?.Dispose();
        base.OnExit(e);
    }
}
