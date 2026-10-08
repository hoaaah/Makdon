using System.Text.Json;
using System.Windows;
using Makdon.Tests.Support;

namespace Makdon.Tests;

/// <summary>
/// Host WPF test tidak boleh menjalankan startup aplikasi. Konstruktor Application menitipkan OnStartup ke dispatcher, jadi
/// tanpa TestApp startup sungguhan berjalan begitu thread STA memompa: membuat mutex/pipe single-instance bernama produksi,
/// MainWindow, membaca settings pengguna, dan bila Makdon/proses test lain sedang berjalan memanggil Shutdown() sehingga semua
/// test WPF berikutnya gagal ("The Application object is being shut down").
/// </summary>
[Collection("Wpf")]
public class WpfHostStartupTests
{
    [Fact]
    public void TestHost_DoesNotRunAppStartup_AndKeepsTheApplicationAlive()
    {
        WpfHost.Instance.Run(() =>
        {
            var app = Application.Current;
            Assert.NotNull(app);
            Assert.IsAssignableFrom<App>(app);
            Assert.Equal(ShutdownMode.OnExplicitShutdown, app.ShutdownMode);
            Assert.False(app.Dispatcher.HasShutdownStarted);
            Assert.Empty(app.Windows.OfType<MainWindow>());
            Assert.Null(app.MainWindow);
        });
    }

    [Fact]
    public void TestHost_LoadsTheSameThemeResourcesAsApp()
    {
        WpfHost.Instance.Run(() =>
        {
            // Kamus dari App.xaml (Light, Controls, Preview) harus termuat walau InitializeComponent App tidak dipakai.
            Assert.Equal(3, Application.Current.Resources.MergedDictionaries.Count);
            Assert.IsType<Style>(Application.Current.FindResource("AppWindow"));
            Assert.IsType<Style>(Application.Current.FindResource("AccentButton"));
        });
    }
}

/// <summary>
/// .NET 10: pembatasan batas paket XPS menolak font dari paket XPS di memori milik Pratinjau Cetak sendiri, sehingga setiap
/// pratinjau gagal. Switch harus ada di runtimeconfig (dibaca sebelum kode apa pun; WPF meng-cache nilainya).
/// </summary>
public class XpsBoundarySwitchTests
{
    const string Switch = "Switch.System.Windows.DisableXpsPackageBoundaryRestriction";

    [Fact]
    public void MakdonRuntimeConfig_DisablesTheXpsPackageBoundaryRestriction()
    {
        // Makdon.runtimeconfig.json ikut tersalin ke folder output test sebagai bagian dari referensi proyek exe.
        var path = Path.Combine(AppContext.BaseDirectory, "Makdon.runtimeconfig.json");
        Assert.True(File.Exists(path), "Makdon.runtimeconfig.json tidak ditemukan: " + path);
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var properties = json.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");
        Assert.True(properties.TryGetProperty(Switch, out var value), "switch tidak ada di runtimeconfig Makdon");
        Assert.Equal(JsonValueKind.True, value.ValueKind);
    }

    [Fact]
    public void TestProcess_HasTheSameSwitch_SoPreviewTestsMatchTheApp()
    {
        Assert.True(AppContext.TryGetSwitch(Switch, out var enabled), "switch tidak terpasang di proses test");
        Assert.True(enabled);
    }
}
