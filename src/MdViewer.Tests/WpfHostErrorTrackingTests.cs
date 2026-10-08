using System.Windows.Threading;
using MdViewer.Tests.Support;
using Xunit.Sdk;

namespace MdViewer.Tests;

/// <summary>Pencatat galat dispatcher: galat tak terduga menggagalkan test, galat yang diharapkan (scope) tidak.</summary>
[Collection("Wpf")]
public class WpfHostErrorTrackingTests
{
    static void Sta(Action action) => WpfHost.Instance.Run(action);

    // Callback dilempar tanpa memompa dispatcher di dalam Run: galat diproses loop utama sesudah Run kembali. (Galat yang pecah di
    // frame bersarang membuat WPF meninggalkan frame itu - lihat WpfHost.Run.)
    static void ThrowFromADispatcherCallback(string message)
    {
        var before = WpfHost.Unhandled.Count;
        Sta(() => Dispatcher.CurrentDispatcher.BeginInvoke(() => throw new InvalidOperationException(message)));
        Assert.True(SpinWait.SpinUntil(() => WpfHost.Unhandled.Count > before, TimeSpan.FromSeconds(10)), "galat tidak tercatat");
    }

    [Fact]
    public void AnUnexpectedDispatcherError_IsRecorded_AndTheEndOfTestCheckFailsOnce()
    {
        var before = WpfHost.Unhandled.Count;
        ThrowFromADispatcherCallback("uji-tak-terduga");
        var check = new FailOnUnexpectedDispatcherErrorsAttribute();
        var method = typeof(WpfHostErrorTrackingTests).GetMethod(nameof(AnUnexpectedDispatcherError_IsRecorded_AndTheEndOfTestCheckFailsOnce))!;

        Assert.Equal(before + 1, WpfHost.Unhandled.Count); // dicatat, proses tetap hidup (Handled)
        var failure = Assert.Throws<XunitException>(() => check.After(method));
        Assert.Contains("uji-tak-terduga", failure.Message);
        check.After(method); // sudah dilaporkan: tidak menggagalkan lagi (dan tidak menggagalkan pemeriksa asli di akhir test ini)
    }

    [Fact]
    public void ExpectedErrors_InsideAScope_AreCaptured_AndDoNotFailTheTest()
    {
        var check = new FailOnUnexpectedDispatcherErrorsAttribute();
        var method = typeof(WpfHostErrorTrackingTests).GetMethod(nameof(ExpectedErrors_InsideAScope_AreCaptured_AndDoNotFailTheTest))!;
        Assert.Null(Record.Exception(() => check.After(method))); // bersihkan sisa dari test lain

        using (var scope = WpfHost.ExpectUnhandled())
        {
            ThrowFromADispatcherCallback("uji-diharapkan-1");
            ThrowFromADispatcherCallback("uji-diharapkan-2");

            Assert.Equal(["uji-diharapkan-1", "uji-diharapkan-2"], scope.Errors.Select(e => e.Message));
        }

        Assert.Null(Record.Exception(() => check.After(method)));
    }

    [Fact]
    public void AnErrorAfterTheScopeEnds_IsStillReported()
    {
        var check = new FailOnUnexpectedDispatcherErrorsAttribute();
        var method = typeof(WpfHostErrorTrackingTests).GetMethod(nameof(AnErrorAfterTheScopeEnds_IsStillReported))!;
        Assert.Null(Record.Exception(() => check.After(method)));

        using (WpfHost.ExpectUnhandled()) ThrowFromADispatcherCallback("dalam-scope");
        ThrowFromADispatcherCallback("sesudah-scope");

        var failure = Assert.Throws<XunitException>(() => check.After(method));
        Assert.Contains("sesudah-scope", failure.Message);
        Assert.DoesNotContain("dalam-scope", failure.Message);
    }

    [Fact]
    public void AnErrorThrownWhilePumpingInsideRun_FailsTheTestInsteadOfHangingTheWholeRun()
    {
        var check = new FailOnUnexpectedDispatcherErrorsAttribute();
        var method = typeof(WpfHostErrorTrackingTests).GetMethod(nameof(AnErrorThrownWhilePumpingInsideRun_FailsTheTestInsteadOfHangingTheWholeRun))!;
        Assert.Null(Record.Exception(() => check.After(method)));

        using var scope = WpfHost.ExpectUnhandled();
        var failure = Record.Exception(() => Sta(() =>
        {
            Dispatcher.CurrentDispatcher.BeginInvoke(() => throw new InvalidOperationException("pecah-saat-memompa"));
            UiPump.For(TimeSpan.FromMilliseconds(100));
        }));

        // Perilaku WPF tidak tetap: kadang frame bersarang itu ditinggalkan (Run akan menggantung selamanya tanpa batas tunggu di
        // WpfHost.Run, di sini menjadi InvalidOperationException), kadang pompa selesai normal. Yang dijamin: kembali, tidak menggantung,
        // dan galat tercatat.
        if (failure is not null)
        {
            Assert.IsType<InvalidOperationException>(failure);
            Assert.Contains("pecah-saat-memompa", failure.Message);
        }
        Assert.Single(scope.Errors);
        // Host tetap bisa dipakai untuk test berikutnya.
        Assert.Equal(7, WpfHost.Instance.Run(() => 7));
    }
}
