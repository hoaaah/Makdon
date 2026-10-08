using System.ComponentModel;
using Makdon.Tests.Support;

namespace Makdon.Tests;

public class CrashLogTests
{
    [Theory]
    [InlineData(typeof(IOException), true)]
    [InlineData(typeof(UnauthorizedAccessException), true)]
    [InlineData(typeof(NotSupportedException), true)]
    [InlineData(typeof(Win32Exception), true)]
    [InlineData(typeof(FormatException), true)]
    [InlineData(typeof(OutOfMemoryException), false)]
    [InlineData(typeof(NullReferenceException), false)]
    [InlineData(typeof(InvalidOperationException), false)]
    [InlineData(typeof(AccessViolationException), false)]
    public void IsRecoverable_OnlyForClearlySafeOperationErrors(Type type, bool expected)
    {
        var ex = (Exception)Activator.CreateInstance(type)!;

        Assert.Equal(expected, CrashLog.IsRecoverable(ex));
    }

    // CLIPBRD_E_CANT_OPEN dan galat imaging WIC (mis. WINCODEC_ERR_COMPONENTNOTFOUND) aman dilanjutkan; COM lain tidak.
    [Theory]
    [InlineData(unchecked((int)0x800401D0), true)]  // CLIPBRD_E_CANT_OPEN
    [InlineData(unchecked((int)0x800401D3), true)]  // CLIPBRD_E_BAD_DATA
    [InlineData(unchecked((int)0x800401D5), true)]
    [InlineData(unchecked((int)0x88982F50), true)]  // WINCODEC_ERR_COMPONENTNOTFOUND
    [InlineData(unchecked((int)0x88982F60), true)]  // WINCODEC_ERR_BADIMAGE
    [InlineData(unchecked((int)0x800401CF), false)]
    [InlineData(unchecked((int)0x800401D6), false)]
    [InlineData(unchecked((int)0x88982EFF), false)]
    [InlineData(unchecked((int)0x88983000), false)]
    [InlineData(unchecked((int)0x80004005), false)] // E_FAIL
    [InlineData(unchecked((int)0x8000FFFF), false)] // E_UNEXPECTED
    [InlineData(unchecked((int)0x80004002), false)] // E_NOINTERFACE
    [InlineData(0, false)]
    public void IsRecoverable_ComException_OnlyForKnownHResults(int hresult, bool expected)
    {
        Assert.Equal(expected, CrashLog.IsRecoverable(new System.Runtime.InteropServices.COMException("x", hresult)));
    }

    [Fact]
    public void ShouldShowDialog_SameTypeAndMessageWithinTenSeconds_IsSuppressed_ThenAllowedAgain()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var a = new IOException("disk penuh-uji");

        Assert.True(CrashLog.ShouldShowDialog(a, t0));
        Assert.False(CrashLog.ShouldShowDialog(new IOException("disk penuh-uji"), t0.AddSeconds(3)));
        Assert.False(CrashLog.ShouldShowDialog(a, t0.AddSeconds(9.9)));
        Assert.True(CrashLog.ShouldShowDialog(a, t0.AddSeconds(10.1)));   // jendela 10 dtk sudah lewat
        Assert.False(CrashLog.ShouldShowDialog(a, t0.AddSeconds(12)));    // dihitung dari tampilan terakhir
    }

    [Fact]
    public void ShouldShowDialog_DifferentMessageOrType_IsNotSuppressed()
    {
        var t0 = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.True(CrashLog.ShouldShowDialog(new IOException("pesan A"), t0));
        Assert.True(CrashLog.ShouldShowDialog(new IOException("pesan B"), t0.AddSeconds(1)));
        Assert.True(CrashLog.ShouldShowDialog(new FormatException("pesan A"), t0.AddSeconds(1)));
    }

    [Fact]
    public void Write_Timestamp_IgnoresTheUsersCultureAndCalendar()
    {
        using var dir = new TempDir();
        var log = Path.Combine(dir.Path, "crash.log");
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("th-TH"); // kalender Buddha (2569)
            CrashLog.Write("konteks", new IOException("x"), log);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }

        var first = File.ReadLines(log).First();
        Assert.StartsWith($"[{DateTime.Now.Year:0000}-", first);
        Assert.Matches(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} [+-]\d{2}:\d{2}\] konteks$", first);
    }

    [Fact]
    public void Write_AppendsContextAndExceptionToLogFile_AndCreatesTheFolder()
    {
        using var dir = new TempDir();
        var log = Path.Combine(dir.Path, "sub", "crash.log");

        CrashLog.Write("konteks-uji", new InvalidOperationException("pesan-uji"), log);
        CrashLog.Write("konteks-kedua", new IOException("pesan-dua"), log);

        var text = File.ReadAllText(log);
        Assert.Contains("konteks-uji", text);
        Assert.Contains("pesan-uji", text);
        Assert.Contains("konteks-kedua", text);
    }

    [Fact]
    public void Write_WhenLogPathIsUnwritable_DoesNotThrow()
    {
        using var dir = new TempDir();
        // Induk path adalah FILE biasa -> CreateDirectory gagal dengan IOException; tidak boleh keluar dari Write.
        var blocker = dir.WriteText("blocker", "x");

        var ex = Record.Exception(() => CrashLog.Write("x", new Exception("y"), Path.Combine(blocker, "crash.log")));

        Assert.Null(ex);
    }
}
