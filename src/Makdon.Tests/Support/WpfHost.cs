using System.Windows;
using System.Windows.Threading;
using Xunit.Sdk;

namespace Makdon.Tests.Support;

/// <summary>
/// Satu thread STA dengan Dispatcher dan satu instance <see cref="App"/> (resource tema dimuat) untuk seluruh proses test.
/// Application bersifat singleton per proses, jadi semua test yang menyentuh WPF dijalankan lewat <see cref="Run(Action)"/>.
/// Timer DispatcherTimer di tab hanya bisa berdetak di antara dua panggilan Run, tidak di tengah satu test.
/// </summary>
public sealed class WpfHost
{
    /// <summary>Galat dispatcher selama proses test; lihat <see cref="DispatcherErrors"/>.</summary>
    public static System.Collections.Concurrent.ConcurrentQueue<Exception> Unhandled => DispatcherErrors.Queue;

    public static WpfHost Instance { get; } = new();

    /// <summary>Lihat <see cref="DispatcherErrors.Expect"/>.</summary>
    public static DispatcherErrors.ExpectedScope ExpectUnhandled() => DispatcherErrors.Expect();

    readonly Dispatcher dispatcher;

    WpfHost()
    {
        DispatcherErrors.Started = true;
        Dispatcher? created = null;
        Exception? failure = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.InitializeComponent();
                // OnStartup App tidak berjalan di sini (tanpa Run), jadi penangan galat aplikasi tidak terpasang. Pasang pencatat sendiri:
                // seperti App sungguhan, galat dispatcher tidak mematikan proses, tetapi dicatat dan diperiksa di akhir tiap test.
                app.DispatcherUnhandledException += (_, e) =>
                {
                    DispatcherErrors.Queue.Enqueue(e.Exception);
                    e.Handled = true;
                };
                created = Dispatcher.CurrentDispatcher;
            }
            catch (Exception ex)
            {
                failure = ex;
                ready.Set();
                return;
            }
            ready.Set();
            Dispatcher.Run();
        })
        { IsBackground = true, Name = "Makdon.Tests STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(60))) throw new TimeoutException("Thread STA tidak siap.");
        if (failure is not null) throw new InvalidOperationException("Gagal menyiapkan Application WPF untuk test.", failure);
        dispatcher = created!;
    }

    public void Run(Action action) => Run<object?>(() =>
    {
        action();
        return null;
    });

    /// <summary>
    /// Menjalankan <paramref name="func"/> di thread STA dan menunggu. Bukan <c>Dispatcher.Invoke</c> biasa: bila sebuah callback
    /// tertunda melempar selagi test memompa dispatcher (UiPump) dan galatnya ditelan (Handled), WPF meninggalkan frame bersarang itu
    /// selamanya, sehingga Invoke tidak pernah kembali dan seluruh proses test menggantung tanpa pesan. Di sini menunggu dibatasi:
    /// bila galat tercatat dan operasi tidak selesai dalam <see cref="GraceAfterError"/>, test gagal dengan galat itu sebagai pesan.
    /// </summary>
    public T Run<T>(Func<T> func)
    {
        if (dispatcher.CheckAccess()) return func(); // sudah di thread STA (Run bersarang): jalankan langsung, jangan menunggu diri sendiri

        T result = default!;
        Exception? thrown = null;
        var operation = dispatcher.InvokeAsync(() =>
        {
            // Galat milik test (mis. Assert) harus sampai ke thread test, bukan ke penangan galat dispatcher.
            try { result = func(); }
            catch (Exception ex) { thrown = ex; }
        });

        var errorsAtStart = DispatcherErrors.Queue.Count;
        System.Diagnostics.Stopwatch? sinceError = null;
        while (!operation.Task.Wait(100))
        {
            if (DispatcherErrors.Queue.Count <= errorsAtStart) continue;
            sinceError ??= System.Diagnostics.Stopwatch.StartNew();
            if (sinceError.Elapsed < GraceAfterError) continue;

            throw new InvalidOperationException(
                "Test berhenti menunggu: galat lolos ke dispatcher UI dan frame yang sedang dipompa ditinggalkan WPF. Galat:" + Environment.NewLine +
                string.Join(Environment.NewLine, DispatcherErrors.Queue.Skip(errorsAtStart).Select(e => e.ToString())));
        }

        if (thrown is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(thrown).Throw();
        return result;
    }

    static readonly TimeSpan GraceAfterError = TimeSpan.FromSeconds(3);
}

/// <summary>
/// Pencatat galat dispatcher UI. Dipisah dari <see cref="WpfHost"/> supaya pemeriksa akhir test bisa membacanya tanpa
/// memicu inisialisasi statis WpfHost (yang membuat thread STA) pada test yang tidak memakai WPF.
/// </summary>
public static class DispatcherErrors
{
    /// <summary>
    /// Galat yang lolos ke dispatcher UI (callback tertunda, timer, event). Semuanya dicatat di sini dan ditelan
    /// (<c>Handled = true</c>) supaya satu galat tidak mematikan seluruh proses test; test tetap gagal lewat
    /// <see cref="FailOnUnexpectedDispatcherErrorsAttribute"/> kecuali test itu memang mengharapkannya (<see cref="Expect"/>).
    /// </summary>
    public static System.Collections.Concurrent.ConcurrentQueue<Exception> Queue { get; } = new();

    /// <summary>true bila host WPF sudah dibuat.</summary>
    internal static volatile bool Started;

    // Jumlah galat di Queue yang sudah dilaporkan/diakui. Galat yang datang di antara dua test dibebankan ke test berikutnya.
    static int acknowledged;

    /// <summary>Galat yang belum diakui sejak laporan terakhir; setelah dipanggil dianggap sudah dilaporkan.</summary>
    internal static IReadOnlyList<Exception> TakeUnexpected()
    {
        var all = Queue.ToArray();
        var from = Math.Min(Interlocked.Exchange(ref acknowledged, all.Length), all.Length);
        return all.Skip(from).ToList();
    }

    /// <summary>
    /// Test yang memang menguji bahwa sebuah callback melempar ke dispatcher membungkus bagian itu dengan scope ini:
    /// galat yang tercatat selama scope dianggap diharapkan dan tidak menggagalkan test. <see cref="ExpectedScope.Errors"/>
    /// berisi galat tersebut untuk diperiksa test.
    /// </summary>
    public static ExpectedScope Expect() => new();

    public sealed class ExpectedScope : IDisposable
    {
        readonly int start = Queue.Count;
        List<Exception>? captured;

        /// <summary>Galat yang tercatat sejak scope dibuka (setelah Dispose: sampai saat Dispose).</summary>
        public IReadOnlyList<Exception> Errors => captured ?? Queue.Skip(start).ToList();

        public void Dispose()
        {
            if (captured is not null) return;
            captured = Queue.Skip(start).ToList();
            // Hanya sampai galat terakhir yang ikut scope ini; galat yang datang sesudahnya tetap dilaporkan.
            var value = start + captured.Count;
            int current;
            do
            {
                current = Volatile.Read(ref acknowledged);
                if (current >= value) return;
            }
            while (Interlocked.CompareExchange(ref acknowledged, value, current) != current);
        }
    }
}

/// <summary>
/// Dipasang di tingkat assembly: setiap test di koleksi "Wpf" gagal bila ada galat tak terduga di dispatcher UI sejak test sebelumnya
/// (callback tertunda yang melempar tidak otomatis menggagalkan test karena ditelan oleh WpfHost). Test yang mengharapkan galat
/// membungkusnya dengan <see cref="WpfHost.ExpectUnhandled"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class FailOnUnexpectedDispatcherErrorsAttribute : BeforeAfterTestAttribute
{
    public override void After(System.Reflection.MethodInfo methodUnderTest)
    {
        if (!DispatcherErrors.Started) return;
        var type = methodUnderTest.DeclaringType;
        var isWpfTest = type?.GetCustomAttributesData().Any(d =>
            d.AttributeType == typeof(CollectionAttribute) && d.ConstructorArguments.Count > 0 && d.ConstructorArguments[0].Value as string == "Wpf") == true;
        if (!isWpfTest) return;

        var errors = DispatcherErrors.TakeUnexpected();
        if (errors.Count == 0) return;
        throw new Xunit.Sdk.XunitException(
            $"{errors.Count} galat tak terduga lolos ke dispatcher UI selama/sejak test ini (ditelan WpfHost):{Environment.NewLine}" +
            string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
    }
}
