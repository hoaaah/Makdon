using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace Makdon;

/// <summary>
/// Satu instance Makdon per pengguna per sesi Windows: Mutex bernama (Local\, per sesi) menentukan instance utama, dan
/// named pipe (hanya pengguna yang sama; nama pipe global se-mesin, jadi memuat id sesi agar cakupannya sama dengan mutex)
/// meneruskan path file dari peluncuran berikutnya ke instance utama. Tidak pernah melempar: bila mutex atau pipe tidak
/// bisa dipakai, pemanggil jatuh ke perilaku biasa (membuka instance baru).
/// </summary>
sealed class SingleInstance : IDisposable
{
    const string Header = "MAKDON1";
    const int MaxFiles = 64;
    const int MaxMessageChars = 256 * 1024;
    const int MaxServerFailures = 5;

    readonly Mutex? mutex;
    readonly string pipeName;
    CancellationTokenSource? cts;
    bool disposed;

    SingleInstance(Mutex? mutex, bool isPrimary, string pipeName)
    {
        this.mutex = mutex;
        IsPrimary = isPrimary;
        this.pipeName = pipeName;
    }

    /// <summary>Batas membaca satu pesan dari klien yang sudah tersambung (bisa dipendekkan test sebelum <see cref="StartServer"/>).</summary>
    internal TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>true bila instance ini yang pertama (memegang mutex). Bila mutex tak bisa dibuat, dianggap true tanpa server.</summary>
    public bool IsPrimary { get; }

    /// <param name="scope">Pembeda nama (dipakai test supaya tidak bentrok dengan aplikasi yang sedang berjalan).</param>
    public static SingleInstance Create(string scope = "")
    {
        var id = UserId() + (scope.Length > 0 ? "." + scope : "");
        var pipe = PipeNameFor(scope);
        try
        {
            var mutex = new Mutex(initiallyOwned: true, @"Local\Makdon.SingleInstance." + id, out var createdNew);
            if (createdNew) return new SingleInstance(mutex, true, pipe);

            mutex.Dispose();
            return new SingleInstance(null, false, pipe);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException or ArgumentException)
        {
            CrashLog.Write("Mutex single-instance tidak bisa dibuat; berjalan tanpa single-instance", ex);
            return new SingleInstance(null, true, pipe);
        }
    }

    /// <summary>Nama pipe: pengguna + id sesi Windows (+ scope). Pipe tidak mengenal "Local\", jadi sesi dimasukkan ke nama.</summary>
    internal static string PipeNameFor(string scope = "") =>
        $"Makdon.{UserId()}.s{CurrentSessionId()}" + (scope.Length > 0 ? "." + scope : "");

    static int CurrentSessionId()
    {
        try { return System.Diagnostics.Process.GetCurrentProcess().SessionId; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            return 0;
        }
    }

    static string UserId()
    {
        try { return WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName; }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Environment.UserName;
        }
    }

    // ---- Sisi instance utama ----

    /// <summary>Mulai mendengarkan di pipe. <paramref name="onFiles"/> dipanggil di thread latar (kosong = hanya minta jendela diaktifkan).</summary>
    public void StartServer(Action<IReadOnlyList<string>> onFiles)
    {
        if (!IsPrimary || mutex is null || cts is not null || disposed) return;
        cts = new CancellationTokenSource();
        var token = cts.Token;
        _ = Task.Run(() => ServerLoopAsync(onFiles, token));
    }

    async Task ServerLoopAsync(Action<IReadOnlyList<string>> onFiles, CancellationToken token)
    {
        // Hanya galat fatal berturut-turut (pipe tak bisa dibuat/ditunggu) yang dihitung. Klien yang macet (timeout baca),
        // memutus di tengah, atau mengirim sampah adalah masalah klien: dibuang tanpa menambah hitungan, kalau tidak
        // beberapa klien macet saja sudah cukup mematikan server permanen.
        var failures = 0;
        while (!token.IsCancellationRequested)
        {
            var connected = false;
            try
            {
                await using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                connected = true;
                failures = 0;

                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                readTimeout.CancelAfter(ReadTimeout);
                var message = await ReadMessageAsync(server, readTimeout.Token).ConfigureAwait(false);

                if (ParseMessage(message) is { } files) Deliver(onFiles, files);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or ObjectDisposedException)
            {
                if (connected) continue; // masalah klien, bukan server
                // Pipe tak bisa dibuat/ditunggu. Jangan berputar cepat; menyerah bila terus gagal.
                if (++failures >= MaxServerFailures)
                {
                    CrashLog.Write("Server single-instance berhenti setelah gagal berulang", ex);
                    return;
                }
                try { await Task.Delay(500, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    // Callback milik pemanggil (UI): galat di dalamnya tidak boleh menghentikan server.
    static void Deliver(Action<IReadOnlyList<string>> onFiles, IReadOnlyList<string> files)
    {
        try { onFiles(files); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            CrashLog.Write("Penerusan file dari instance lain gagal diproses", ex);
        }
    }

    static async Task<string> ReadMessageAsync(Stream stream, CancellationToken token)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        var builder = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
        {
            builder.Append(buffer, 0, read);
            if (builder.Length > MaxMessageChars) break; // pesan sah selalu kecil
        }
        return builder.ToString();
    }

    /// <summary>
    /// Null bila bukan pesan Makdon. Hanya path mutlak penuh yang diterima (drive+folder atau UNC; "C:rel.md" dan "\rel.md"
    /// tergantung folder/drive kerja server, jadi ditolak); maksimal 64.
    /// </summary>
    internal static IReadOnlyList<string>? ParseMessage(string message)
    {
        var lines = message.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count == 0 || lines[0] != Header) return null;

        return lines.Skip(1)
            .Where(l => l.Length is > 0 and < 32768 && Path.IsPathFullyQualified(l))
            .Take(MaxFiles)
            .ToList();
    }

    internal static string BuildMessage(IEnumerable<string> files) =>
        Header + "\n" + string.Join("\n", files.Where(f => !f.Contains('\n') && !f.Contains('\r')));

    // ---- Sisi instance berikutnya ----

    /// <summary>
    /// Mengirim path ke instance utama (daftar kosong = hanya aktifkan jendelanya). false bila gagal (pipe tak ada/timeout),
    /// dan pemanggil sebaiknya tetap membuka instance sendiri.
    /// </summary>
    public bool TrySendToPrimary(IReadOnlyList<string> files, int timeoutMs = 3000)
    {
        if (IsPrimary) return false;
        try
        {
            // Connect menunggu sampai server siap (instance utama mungkin baru saja mulai).
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            writer.Write(BuildMessage(files));
            writer.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            CrashLog.Write("Gagal meneruskan file ke instance Makdon yang sudah berjalan; membuka instance baru", ex);
            return false;
        }
    }

    /// <summary>Mengizinkan proses lain (instance utama) mengambil fokus; dipanggil sebelum instance ini keluar.</summary>
    public static void AllowForeground()
    {
        try { _ = AllowSetForegroundWindow(-1); } // ASFW_ANY
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cts?.Cancel();
        cts?.Dispose();
        cts = null;

        if (mutex is null) return;
        try { mutex.ReleaseMutex(); }
        catch (ApplicationException) { } // bukan thread pemilik
        mutex.Dispose();
    }
}
