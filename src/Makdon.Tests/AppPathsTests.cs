using Makdon.Tests.Support;

namespace Makdon.Tests;

/// <summary>
/// Mode terpasang/portable dan lokasi data (docs/DISTRIBUTION.md §3). Folder exe selalu disuntik (TempDir atau path palsu);
/// mode terpasang hanya diperiksa path-nya, tidak pernah ditulisi.
/// </summary>
public class AppPathsTests
{
    static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    // ---- Deteksi mode ----

    [Fact]
    public void MarkerNextToTheExe_MeansPortable_WithDataUnderTheExeFolder()
    {
        using var dir = new TempDir();
        dir.WriteText(AppPaths.PortableMarkerFileName, "");

        var paths = AppPaths.Detect(dir.Path);

        Assert.True(paths.IsPortable);
        Assert.Equal(dir.Path, paths.ExeDirectory);
        Assert.Equal(dir.File("Makdon.exe"), paths.ExePath);
        Assert.Equal(dir.File("data"), paths.DataDirectory);
        Assert.Equal(dir.File(Path.Combine("data", "settings.json")), paths.SettingsPath);
        Assert.Equal(dir.File(Path.Combine("data", "crash.log")), paths.CrashLogPath);
        Assert.False(Directory.Exists(dir.File("data"))); // deteksi tidak membuat apa pun
    }

    [Fact]
    public void NoMarker_MeansInstalled_WithDataInTheUserProfile()
    {
        using var dir = new TempDir();
        dir.WriteText("Makdon.portable.txt", ""); // nama mirip tidak dihitung

        var paths = AppPaths.Detect(dir.Path);

        Assert.False(paths.IsPortable);
        Assert.Null(paths.DataDirectory);
        Assert.Equal(Path.Combine(AppData, "Makdon", "settings.json"), paths.SettingsPath);
        Assert.Equal(Path.Combine(LocalAppData, "Makdon", "crash.log"), paths.CrashLogPath);
    }

    [Fact]
    public void MarkerThatIsAFolder_DoesNotCount()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File(AppPaths.PortableMarkerFileName));

        Assert.False(AppPaths.Detect(dir.Path).IsPortable);
    }

    [Fact]
    public void Detect_AsksForTheMarkerInTheNormalizedExeFolder()
    {
        var asked = new List<string>();

        var paths = AppPaths.Detect(@"D:\Alat\Makdon\..\Makdon\", markerExists: p => { asked.Add(p); return true; });

        Assert.Equal([@"D:\Alat\Makdon\Makdon.portable"], asked);
        Assert.Equal(@"D:\Alat\Makdon\", paths.ExeDirectory);
        Assert.Equal(@"D:\Alat\Makdon\data", paths.DataDirectory);
    }

    [Fact]
    public void Detect_KeepsAnExplicitExePath()
    {
        var paths = AppPaths.Detect(@"D:\Alat\Makdon", @"D:\Alat\Makdon\Makdon-baru.exe", _ => false);

        Assert.Equal(@"D:\Alat\Makdon\Makdon-baru.exe", paths.ExePath);
    }

    [Fact]
    public void Current_InTheTestProcess_IsNotPortable_AndIsNotUsedToWriteHere()
    {
        // Folder output test tidak berisi penanda; test lain tidak bergantung pada AppPaths.Current (crash.log dialihkan).
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, AppPaths.PortableMarkerFileName)));
        Assert.False(AppPaths.Current.IsPortable);
        Assert.NotEqual(AppPaths.Current.CrashLogPath, CrashLog.LogPath);
    }

    // ---- Scope single-instance ----

    [Fact]
    public void SingleInstanceScope_IsEmptyWhenInstalled()
    {
        Assert.Equal("", AppPaths.Detect(@"C:\Program Files\Makdon", markerExists: _ => false).SingleInstanceScope);
    }

    [Fact]
    public void SingleInstanceScope_IsAStableHashOfTheExeFolder_WhenPortable()
    {
        var scope = AppPaths.Detect(@"D:\Alat\Makdon", markerExists: _ => true).SingleInstanceScope;

        Assert.Matches("^p[0-9A-F]{16}$", scope);
        Assert.Equal(scope, AppPaths.Detect(@"D:\Alat\Makdon", markerExists: _ => true).SingleInstanceScope);
        // Ejaan lain untuk folder yang sama memberi scope yang sama.
        Assert.Equal(scope, AppPaths.Detect(@"d:\alat\MAKDON\", markerExists: _ => true).SingleInstanceScope);
        Assert.Equal(scope, AppPaths.Detect(@"D:\Alat\x\..\Makdon", markerExists: _ => true).SingleInstanceScope);
    }

    [Fact]
    public void SingleInstanceScope_DiffersBetweenPortableFolders()
    {
        var a = AppPaths.Detect(@"D:\Alat\Makdon", markerExists: _ => true).SingleInstanceScope;
        var b = AppPaths.Detect(@"D:\Alat\Makdon2", markerExists: _ => true).SingleInstanceScope;
        var c = AppPaths.Detect(@"E:\Alat\Makdon", markerExists: _ => true).SingleInstanceScope;

        Assert.Equal(3, new[] { a, b, c }.Distinct().Count());
    }

    // ---- Folder data bisa ditulisi ----

    [Fact]
    public void IsDataDirectoryWritable_Portable_CreatesTheDataFolder_AndLeavesNoProbeBehind()
    {
        using var dir = new TempDir();
        dir.WriteText(AppPaths.PortableMarkerFileName, "");
        var paths = AppPaths.Detect(dir.Path);

        Assert.True(paths.IsDataDirectoryWritable());
        Assert.True(Directory.Exists(dir.File("data")));
        Assert.Empty(dir.Entries("data"));

        Assert.True(paths.IsDataDirectoryWritable()); // folder sudah ada
        Assert.Empty(dir.Entries("data"));
    }

    [Fact]
    public void IsDataDirectoryWritable_Portable_FalseWhenDataCannotBeAFolder()
    {
        // Folder tak-bisa-ditulisi lewat ACL tidak andal di semua mesin/akun; berkas bernama "data" memblokir
        // pembuatan folder secara pasti (IOException) dengan efek yang sama bagi pemanggil.
        using var dir = new TempDir();
        dir.WriteText(AppPaths.PortableMarkerFileName, "");
        dir.WriteText("data", "bukan folder");

        Assert.False(AppPaths.Detect(dir.Path).IsDataDirectoryWritable());
        Assert.Equal("bukan folder", File.ReadAllText(dir.File("data")));
    }

    [Fact]
    public void IsDataDirectoryWritable_Portable_FalseForAnInvalidPath_WithoutThrowing()
    {
        // Path tak valid (karakter terlarang di nama folder) sebagai pengganti folder read-only.
        var paths = AppPaths.Detect(Path.Combine(Path.GetTempPath(), "Makdon.Tests", "tak|valid?" + Guid.NewGuid().ToString("N")), markerExists: _ => true);

        Assert.False(paths.IsDataDirectoryWritable());
    }

    [Fact]
    public void IsDataDirectoryWritable_Installed_IsTrue_AndTouchesNothing()
    {
        using var dir = new TempDir();
        var paths = AppPaths.Detect(dir.Path);

        Assert.True(paths.IsDataDirectoryWritable());
        Assert.Empty(dir.Entries());
    }
}

/// <summary>
/// Mutex installer hanya dibuat di mode terpasang. Mode terpasang sengaja tidak diuji di sini: membuat "Makdon.AppMutex"
/// (nama produksi) di proses test akan membuat installer/uninstaller sungguhan mengira Makdon sedang berjalan.
/// </summary>
public class InstallerMutexTests
{
    static int HeldCount() =>
        ((System.Collections.ICollection)typeof(InstallerMutex)
            .GetField("Held", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(null)!).Count;

    [Fact]
    public void Portable_DoesNotCreateTheInstallerMutex()
    {
        var before = HeldCount();

        InstallerMutex.Acquire(AppPaths.Detect(@"D:\Alat\Makdon", markerExists: _ => true));

        Assert.Equal(before, HeldCount());
        Assert.Equal(0, before); // host test tidak menjalankan startup aplikasi (lihat WpfHostStartupTests)
    }
}
