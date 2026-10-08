using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;

namespace Makdon.Tests;

/// <summary>
/// Server/klien named pipe SingleInstance dengan scope unik per test (nama mutex dan pipe tidak bentrok dengan aplikasi
/// asli maupun test lain). Melengkapi <see cref="SingleInstanceTests"/> yang sudah menguji jalur normal.
/// </summary>
public class SingleInstanceServerTests
{
    static string NewScope() => "test-" + Guid.NewGuid().ToString("N");

    // Dipakai untuk mengirim muatan mentah (bukan lewat TrySendToPrimary).
    static string PipeName(string scope) => SingleInstance.PipeNameFor(scope);

    static void SendRaw(string scope, string payload)
    {
        using var client = new NamedPipeClientStream(".", PipeName(scope), PipeDirection.Out, PipeOptions.CurrentUserOnly);
        client.Connect(5000);
        var bytes = new UTF8Encoding(false).GetBytes(payload);
        try { client.Write(bytes); }
        catch (IOException) { } // server boleh memutus pesan yang terlalu besar
    }

    static (SingleInstance Primary, BlockingCollection<IReadOnlyList<string>> Received) StartPrimary(string scope)
    {
        var primary = SingleInstance.Create(scope);
        Assert.True(primary.IsPrimary);
        var received = new BlockingCollection<IReadOnlyList<string>>();
        primary.StartServer(received.Add);
        return (primary, received);
    }

    static IReadOnlyList<string> Take(BlockingCollection<IReadOnlyList<string>> received, int seconds = 10)
    {
        Assert.True(received.TryTake(out var files, TimeSpan.FromSeconds(seconds)), "Server tidak meneruskan pesan tepat waktu.");
        return files!;
    }

    // ---- ParseMessage ----

    [Fact]
    public void ParseMessage_CapsAtSixtyFourFiles_KeepingTheFirstOnes()
    {
        var files = Enumerable.Range(0, 100).Select(i => $@"C:\f{i}.md");

        var parsed = SingleInstance.ParseMessage(SingleInstance.BuildMessage(files))!;

        Assert.Equal(64, parsed.Count);
        Assert.Equal(@"C:\f0.md", parsed[0]);
        Assert.Equal(@"C:\f63.md", parsed[^1]);
    }

    [Fact]
    public void ParseMessage_RelativeEntriesDoNotCountTowardsTheCap()
    {
        var lines = Enumerable.Range(0, 70).Select(i => $"rel{i}.md").Concat(Enumerable.Range(0, 64).Select(i => $@"C:\a{i}.md"));

        var parsed = SingleInstance.ParseMessage("MAKDON1\n" + string.Join("\n", lines))!;

        Assert.Equal(64, parsed.Count);
        Assert.All(parsed, p => Assert.StartsWith(@"C:\a", p));
    }

    [Theory]
    [InlineData("MAKDON1 ")]
    [InlineData(" MAKDON1")]
    [InlineData("makdon1")]
    [InlineData("MAKDON2")]
    [InlineData("")]
    public void ParseMessage_HeaderMustMatchExactly(string header)
    {
        Assert.Null(SingleInstance.ParseMessage(header + "\nC:\\a.md"));
    }

    [Fact]
    public void ParseMessage_HeaderWithCrLf_IsAccepted_AndCrIsStrippedFromPaths()
    {
        var parsed = SingleInstance.ParseMessage("MAKDON1\r\nC:\\a.md\r\nD:\\b.md\r\n");

        Assert.Equal([@"C:\a.md", @"D:\b.md"], parsed);
    }

    [Fact]
    public void ParseMessage_SkipsBlankLinesAndOverlongPaths()
    {
        var exact = @"C:\" + new string('a', 32767 - 3);   // 32767 karakter: masih diterima
        var tooLong = @"C:\" + new string('b', 32768 - 3);  // 32768 karakter: ditolak

        var parsed = SingleInstance.ParseMessage($"MAKDON1\n\n{exact}\n{tooLong}\n\nC:\\ok.md")!;

        Assert.Equal([exact, @"C:\ok.md"], parsed);
    }

    [Fact]
    public void ParseMessage_AcceptsUncAndFullyQualifiedPaths()
    {
        var parsed = SingleInstance.ParseMessage("MAKDON1\n\\\\server\\share\\a.md\nC:\\x\\..\\b.md");

        Assert.Equal([@"\\server\share\a.md", @"C:\x\..\b.md"], parsed);
    }

    [Fact]
    public void ParseMessage_RejectsRootedButNotFullyQualifiedPaths()
    {
        var parsed = SingleInstance.ParseMessage("MAKDON1\nC:rel.md\n\\rel.md\nC:\\ok.md")!;

        Assert.Equal([@"C:\ok.md"], parsed);
    }

    [Fact]
    public void BuildMessage_DropsEntriesContainingLineBreaks_SoTheyCannotInjectExtraPaths()
    {
        var message = SingleInstance.BuildMessage([@"C:\a.md", "C:\\b.md\nC:\\injeksi.md", "C:\\c.md\rD:\\x.md", @"C:\d.md"]);

        Assert.Equal([@"C:\a.md", @"C:\d.md"], SingleInstance.ParseMessage(message));
    }

    // ---- Lewat pipe sungguhan ----

    [Fact]
    public void Server_ForwardsOnlyAbsolutePaths_AndNoMoreThanSixtyFour()
    {
        var scope = NewScope();
        var (primary, received) = StartPrimary(scope);
        using var _ = primary;
        using var second = SingleInstance.Create(scope);

        var files = new List<string> { "relatif.md", @"..\naik.md" };
        files.AddRange(Enumerable.Range(0, 80).Select(i => $@"C:\banyak\f{i}.md"));
        Assert.True(second.TrySendToPrimary(files));

        var got = Take(received);
        Assert.Equal(64, got.Count);
        Assert.All(got, p => Assert.StartsWith(@"C:\banyak\f", p));
    }

    [Fact]
    public void Server_EmptyList_IsDeliveredAsActivationRequest()
    {
        var scope = NewScope();
        var (primary, received) = StartPrimary(scope);
        using var _ = primary;
        using var second = SingleInstance.Create(scope);

        Assert.True(second.TrySendToPrimary([]));

        Assert.Empty(Take(received));
    }

    [Fact]
    public void Server_UnicodePathsSurviveTheRoundTrip()
    {
        var scope = NewScope();
        var (primary, received) = StartPrimary(scope);
        using var _ = primary;
        using var second = SingleInstance.Create(scope);

        Assert.True(second.TrySendToPrimary([@"C:\catatan\rapat é 日本語 😀.md"]));

        Assert.Equal([@"C:\catatan\rapat é 日本語 😀.md"], Take(received));
    }

    [Fact]
    public void Server_IgnoresMessagesWithWrongHeader_AndKeepsServing()
    {
        var scope = NewScope();
        var (primary, received) = StartPrimary(scope);
        using var _ = primary;
        using var second = SingleInstance.Create(scope);

        SendRaw(scope, "HALO\nC:\\jahat.md");
        SendRaw(scope, "");
        Assert.True(second.TrySendToPrimary([@"C:\sah.md"]));

        Assert.Equal([@"C:\sah.md"], Take(received));
        Assert.True(received.Count == 0); // pesan salah tidak pernah diteruskan
    }

    [Fact]
    public void Server_OversizedMessage_DoesNotKillTheServer_AndYieldsAtMostSixtyFourFiles()
    {
        var scope = NewScope();
        var (primary, received) = StartPrimary(scope);
        using var _ = primary;
        using var second = SingleInstance.Create(scope);

        var huge = new StringBuilder("MAKDON1\n");
        for (var i = 0; i < 6000; i++) huge.Append(@"C:\").Append(new string('x', 100)).Append(i).Append('\n'); // > 256 K karakter
        SendRaw(scope, huge.ToString());

        // Pesan yang dipotong boleh diteruskan sebagian (maks. 64) atau dibuang; yang wajib: server tetap melayani.
        Assert.True(second.TrySendToPrimary([@"C:\sesudah.md"], timeoutMs: 10_000));
        var seen = new List<IReadOnlyList<string>>();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (received.TryTake(out var files, TimeSpan.FromMilliseconds(200))) seen.Add(files);
            if (seen.Any(f => f.SequenceEqual([@"C:\sesudah.md"]))) break;
        }

        Assert.Contains(seen, f => f.SequenceEqual([@"C:\sesudah.md"]));
        Assert.All(seen, f => Assert.True(f.Count <= 64));
    }

    [Fact]
    public void Server_StalledClient_IsDroppedAfterTheReadTimeout_AndLaterClientsAreServed()
    {
        var scope = NewScope();
        var (primary, received) = StartPrimary(scope);
        using var _ = primary;
        using var second = SingleInstance.Create(scope);

        // Klien tersambung tetapi tidak menulis apa pun dan tidak menutup pipe.
        using var stalled = new NamedPipeClientStream(".", PipeName(scope), PipeDirection.Out, PipeOptions.CurrentUserOnly);
        stalled.Connect(5000);

        // Server membuang klien itu setelah batas baca (5 dtk) lalu membuka pipe baru; klien kedua menunggu di Connect.
        Assert.True(second.TrySendToPrimary([@"C:\antre.md"], timeoutMs: 20_000));

        Assert.Equal([@"C:\antre.md"], Take(received, seconds: 20));
    }

    [Fact]
    public void TrySend_AfterPrimaryStopped_FailsWithinTheTimeout_SoCallerOpensItsOwnWindow()
    {
        var scope = NewScope();
        var (primary, _) = StartPrimary(scope);
        using var second = SingleInstance.Create(scope); // dibuat sebelum primary berhenti: tidak menjadi primary
        Assert.False(second.IsPrimary);
        Thread.Sleep(200); // beri server waktu membuat pipe
        primary.Dispose();
        Thread.Sleep(300); // pembatalan WaitForConnection + penutupan pipe

        var started = DateTime.UtcNow;
        var sent = second.TrySendToPrimary([@"C:\a.md"], timeoutMs: 500);

        Assert.False(sent);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5), "TrySendToPrimary tidak boleh menggantung melebihi timeout.");
    }

    [Fact]
    public void PipeName_ContainsTheWindowsSessionId_SoItMatchesTheSessionScopedMutex()
    {
        var sessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId;

        var name = SingleInstance.PipeNameFor("abc");

        Assert.Contains($".s{sessionId}.", name);
        Assert.EndsWith(".abc", name);
        Assert.NotEqual(SingleInstance.PipeNameFor("abc"), SingleInstance.PipeNameFor("abd"));
    }

    // Klien yang macet hanya membuang satu putaran; server tidak boleh menyerah permanen karenanya.
    [Fact]
    public void Server_SurvivesMoreStalledClientsThanTheFailureLimit()
    {
        var scope = NewScope();
        var primary = SingleInstance.Create(scope);
        primary.ReadTimeout = TimeSpan.FromMilliseconds(150);
        var received = new BlockingCollection<IReadOnlyList<string>>();
        primary.StartServer(received.Add);
        using var _ = primary;
        using var second = SingleInstance.Create(scope);

        for (var i = 0; i < 8; i++) // > MaxServerFailures (5)
        {
            using var stalled = new NamedPipeClientStream(".", PipeName(scope), PipeDirection.Out, PipeOptions.CurrentUserOnly);
            stalled.Connect(5000);
            Thread.Sleep(400); // lewati batas baca: server membuang klien ini
        }

        Assert.True(second.TrySendToPrimary([@"C:\masih-hidup.md"], timeoutMs: 10_000));
        Assert.Equal([@"C:\masih-hidup.md"], Take(received));
    }

    [Fact]
    public void Server_CallbackThatThrows_IsLoggedAndTheServerKeepsServing()
    {
        var scope = NewScope();
        var primary = SingleInstance.Create(scope);
        var received = new BlockingCollection<IReadOnlyList<string>>();
        var calls = 0;
        primary.StartServer(files =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("callback rusak");
            received.Add(files);
        });
        using var _ = primary;
        using var second = SingleInstance.Create(scope);

        Assert.True(second.TrySendToPrimary([@"C:\pertama.md"]));
        Assert.True(UiWait(() => Volatile.Read(ref calls) >= 1));
        Assert.True(second.TrySendToPrimary([@"C:\kedua.md"], timeoutMs: 10_000));

        Assert.Equal([@"C:\kedua.md"], Take(received));
    }

    static bool UiWait(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(20);
        }
        return false;
    }

    // ---- Siklus hidup ----

    [Fact]
    public void Dispose_Twice_DoesNotThrow()
    {
        var primary = SingleInstance.Create(NewScope());
        primary.StartServer(_ => { });

        primary.Dispose();

        Assert.Null(Record.Exception(primary.Dispose));
    }

    [Fact]
    public void StartServer_AfterDispose_IsIgnored_AndTwiceDoesNotStartASecondServer()
    {
        var scope = NewScope();
        var first = SingleInstance.Create(scope);
        var calls = new BlockingCollection<IReadOnlyList<string>>();
        first.StartServer(calls.Add);
        first.StartServer(_ => throw new InvalidOperationException("server kedua tidak boleh berjalan"));
        using var second = SingleInstance.Create(scope);
        Assert.True(second.TrySendToPrimary([@"C:\a.md"]));
        Assert.Equal([@"C:\a.md"], Take(calls));

        first.Dispose();

        Assert.Null(Record.Exception(() => first.StartServer(_ => { })));
    }

    [Fact]
    public void StartServer_OnNonPrimary_IsIgnored()
    {
        var scope = NewScope();
        using var primary = SingleInstance.Create(scope);
        using var second = SingleInstance.Create(scope);

        Assert.Null(Record.Exception(() => second.StartServer(_ => throw new InvalidOperationException("tidak boleh dipanggil"))));
        Assert.False(second.TrySendToPrimary([], timeoutMs: 200)); // primary tidak mendengarkan
    }

    [Fact]
    public void AfterPrimaryIsDisposed_TheNextLaunchBecomesPrimaryAndCanServe()
    {
        var scope = NewScope();
        var first = SingleInstance.Create(scope);
        first.StartServer(_ => { });
        first.Dispose();

        var (next, received) = StartPrimary(scope);
        using var _ = next;
        using var launcher = SingleInstance.Create(scope);

        Assert.False(launcher.IsPrimary);
        Assert.True(launcher.TrySendToPrimary([@"C:\baru.md"], timeoutMs: 10_000));
        Assert.Equal([@"C:\baru.md"], Take(received));
    }

    [Fact]
    public void DifferentScopes_AreIndependentPrimaries()
    {
        using var a = SingleInstance.Create(NewScope());
        using var b = SingleInstance.Create(NewScope());

        Assert.True(a.IsPrimary);
        Assert.True(b.IsPrimary);
    }

    [Fact]
    public void Create_DefaultScopeNeverCollidesWithTestScopes()
    {
        // Aplikasi asli (scope kosong) boleh sedang berjalan; Create(scope) uji tidak boleh menjadi non-primary karenanya.
        using var test = SingleInstance.Create(NewScope());

        Assert.True(test.IsPrimary);
    }
}
