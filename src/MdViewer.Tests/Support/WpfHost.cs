using System.Windows;
using System.Windows.Threading;

namespace MdViewer.Tests.Support;

/// <summary>
/// Satu thread STA dengan Dispatcher dan satu instance <see cref="App"/> (resource tema dimuat) untuk seluruh proses test.
/// Application bersifat singleton per proses, jadi semua test yang menyentuh WPF dijalankan lewat <see cref="Run(Action)"/>.
/// Timer DispatcherTimer di tab hanya bisa berdetak di antara dua panggilan Run, tidak di tengah satu test.
/// </summary>
public sealed class WpfHost
{
    public static WpfHost Instance { get; } = new();

    readonly Dispatcher dispatcher;

    WpfHost()
    {
        Dispatcher? created = null;
        Exception? failure = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.InitializeComponent();
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
        { IsBackground = true, Name = "MdViewer.Tests STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(60))) throw new TimeoutException("Thread STA tidak siap.");
        if (failure is not null) throw new InvalidOperationException("Gagal menyiapkan Application WPF untuk test.", failure);
        dispatcher = created!;
    }

    public void Run(Action action) => dispatcher.Invoke(action);

    public T Run<T>(Func<T> func) => dispatcher.Invoke(func);
}
