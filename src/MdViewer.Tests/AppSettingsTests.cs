using System.Text;
using MdViewer.Tests.Support;

namespace MdViewer.Tests;

/// <summary>Semua test memakai path settings sementara; %APPDATA% asli tidak disentuh (Load/Save menerima path).</summary>
public class AppSettingsTests : IDisposable
{
    readonly TempDir dir = new();

    public void Dispose() => dir.Dispose();

    string Settings(string json) => dir.WriteText("settings.json", json);

    // ---- Load ----

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = AppSettings.Load(dir.File("nope.json"));

        Assert.Empty(s.RecentFiles);
        Assert.Null(s.Session);
        Assert.Equal(AppThemeMode.System, s.ParsedTheme);
        Assert.Equal(ZoomLevel.Default, s.ZoomPercent);
    }

    [Fact]
    public void Load_MissingDirectory_ReturnsDefaults()
    {
        var s = AppSettings.Load(dir.File(Path.Combine("a", "b", "settings.json")));

        Assert.Equal(ZoomLevel.Default, s.ZoomPercent);
    }

    [Fact]
    public void Load_PathIsDirectory_ReturnsDefaults()
    {
        var s = AppSettings.Load(dir.Path);

        Assert.Equal(ZoomLevel.Default, s.ZoomPercent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("{\"Theme\": \"Dark\"")] // terpotong
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"teks\"")]
    [InlineData("\u0000\u0001\u0002")]
    public void Load_CorruptOrWrongShapeJson_ReturnsDefaultsWithoutThrowing(string json)
    {
        var s = AppSettings.Load(Settings(json));

        Assert.NotNull(s);
        Assert.Empty(s.RecentFiles);
        Assert.Equal(nameof(AppThemeMode.System), s.Theme);
        Assert.Equal(ZoomLevel.Default, s.ZoomPercent);
    }

    [Fact]
    public void Load_BinaryGarbage_ReturnsDefaults()
    {
        dir.WriteBytes("settings.json", [0xFF, 0xFE, 0x00, 0x80, 0x81, 0xC3, 0x28]);

        var s = AppSettings.Load(dir.File("settings.json"));

        Assert.Equal(ZoomLevel.Default, s.ZoomPercent);
    }

    [Fact]
    public void Load_WrongValueType_ReturnsDefaultsForWholeFile()
    {
        // Tipe salah pada satu properti membuat deserialisasi gagal; seluruh file dianggap korup.
        var s = AppSettings.Load(Settings("""{"ZoomPercent":"banyak","Theme":"Dark"}"""));

        Assert.Equal(ZoomLevel.Default, s.ZoomPercent);
        Assert.Equal(AppThemeMode.System, s.ParsedTheme);
    }

    [Fact]
    public void Load_EmptyObject_ReturnsDefaults()
    {
        var s = AppSettings.Load(Settings("{}"));

        Assert.Equal(ZoomLevel.Default, s.ZoomPercent);
        Assert.Equal(AppThemeMode.System, s.ParsedTheme);
        Assert.Empty(s.RecentFiles);
    }

    [Fact]
    public void Load_UnknownProperties_AreIgnored()
    {
        var s = AppSettings.Load(Settings("""{"Masa Depan":{"x":1},"ZoomPercent":120}"""));

        Assert.Equal(120, s.ZoomPercent);
    }

    [Fact]
    public void Load_PropertyNamesAreCaseInsensitive()
    {
        var s = AppSettings.Load(Settings("""{"zoompercent":130,"theme":"dark"}"""));

        Assert.Equal(130, s.ZoomPercent);
        Assert.Equal(AppThemeMode.Dark, s.ParsedTheme);
    }

    [Fact]
    public void Load_FileWithUtf8Bom_IsParsed()
    {
        dir.WriteBytes("settings.json", [.. new byte[] { 0xEF, 0xBB, 0xBF }, .. "{\"ZoomPercent\":140}"u8.ToArray()]);

        Assert.Equal(140, AppSettings.Load(dir.File("settings.json")).ZoomPercent);
    }

    [Theory]
    [InlineData("\"Neon\"", "System")]
    [InlineData("\"dark\"", "Dark")]
    [InlineData("\"LIGHT\"", "Light")]
    [InlineData("\"\"", "System")]
    [InlineData("null", "System")]
    [InlineData("\"99\"", "System")]
    [InlineData("\"Dark,Light\"", "System")]
    public void Load_Theme_IsNormalizedToKnownName(string themeJson, string expected)
    {
        var s = AppSettings.Load(Settings($$"""{"Theme":{{themeJson}}}"""));

        Assert.Equal(expected, s.Theme);
        Assert.Equal(Enum.Parse<AppThemeMode>(expected), s.ParsedTheme);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(-100, 50)]
    [InlineData(49, 50)]
    [InlineData(50, 50)]
    [InlineData(175, 175)]
    [InlineData(300, 300)]
    [InlineData(301, 300)]
    [InlineData(int.MaxValue, 300)]
    [InlineData(int.MinValue, 50)]
    public void Load_ZoomPercent_IsClampedToRange(int stored, int expected)
    {
        var s = AppSettings.Load(Settings($$"""{"ZoomPercent":{{stored}}}"""));

        Assert.Equal(expected, s.ZoomPercent);
    }

    [Fact]
    public void Load_ZoomPercentOverflowingInt_ReturnsDefaults()
    {
        var s = AppSettings.Load(Settings("""{"ZoomPercent":99999999999999,"Theme":"Dark"}"""));

        Assert.Equal(ZoomLevel.Default, s.ZoomPercent);
    }

    [Fact]
    public void Load_RecentFiles_DropsBlankNullAndCaseInsensitiveDuplicates_KeepingOrder()
    {
        var s = AppSettings.Load(Settings("""{"RecentFiles":["C:\\a.md",null,"","   ","c:\\A.MD","D:\\b.md","C:\\a.md"]}"""));

        Assert.Equal(new[] { @"C:\a.md", @"D:\b.md" }, s.RecentFiles);
    }

    [Fact]
    public void Load_RecentFilesNull_BecomesEmptyList()
    {
        var s = AppSettings.Load(Settings("""{"RecentFiles":null}"""));

        Assert.NotNull(s.RecentFiles);
        Assert.Empty(s.RecentFiles);
    }

    [Fact]
    public void Load_MoreThanTenRecentFiles_KeepsFirstTen()
    {
        var items = string.Join(",", Enumerable.Range(1, 15).Select(i => $"\"C:\\\\f{i}.md\""));
        var s = AppSettings.Load(Settings($$"""{"RecentFiles":[{{items}}]}"""));

        Assert.Equal(AppSettings.MaxRecentFiles, s.RecentFiles.Count);
        Assert.Equal(@"C:\f1.md", s.RecentFiles[0]);
        Assert.Equal(@"C:\f10.md", s.RecentFiles[^1]);
    }

    [Fact]
    public void Load_SessionAbsent_StaysNull()
    {
        Assert.Null(AppSettings.Load(Settings("""{"Session":null}""")).Session);
        Assert.Null(AppSettings.Load(Settings("{}")).Session);
    }

    [Fact]
    public void Load_Session_DropsBadTabsAndFixesCaretAndActiveIndex()
    {
        var s = AppSettings.Load(Settings("""
            {"Session":{"ActiveIndex":7,"Tabs":[
                {"Path":"C:\\a.md","Mode":"Edit","CaretOffset":-5},
                null,
                {"Path":"","Mode":"Edit"},
                {"Path":"   "},
                {"Path":"C:\\b.md","Mode":"Preview","CaretOffset":42}
            ]}}
            """));

        Assert.NotNull(s.Session);
        Assert.Equal(2, s.Session!.Tabs.Count);
        Assert.Equal(@"C:\a.md", s.Session.Tabs[0].Path);
        Assert.Equal(0, s.Session.Tabs[0].CaretOffset);
        Assert.Equal(42, s.Session.Tabs[1].CaretOffset);
        Assert.Equal(0, s.Session.ActiveIndex);
    }

    [Fact]
    public void Load_SessionNegativeActiveIndex_ResetsToZero()
    {
        var s = AppSettings.Load(Settings("""{"Session":{"ActiveIndex":-3,"Tabs":[{"Path":"C:\\a.md"}]}}"""));

        Assert.Equal(0, s.Session!.ActiveIndex);
    }

    [Fact]
    public void Load_SessionValidActiveIndex_IsKept()
    {
        var s = AppSettings.Load(Settings("""{"Session":{"ActiveIndex":1,"Tabs":[{"Path":"C:\\a.md"},{"Path":"C:\\b.md"}]}}"""));

        Assert.Equal(1, s.Session!.ActiveIndex);
    }

    [Fact]
    public void Load_SessionTabsNull_BecomesEmptyAndActiveIndexZero()
    {
        var s = AppSettings.Load(Settings("""{"Session":{"ActiveIndex":3,"Tabs":null}}"""));

        Assert.Empty(s.Session!.Tabs);
        Assert.Equal(0, s.Session.ActiveIndex);
    }

    [Theory]
    [InlineData("Edit", ViewMode.Edit)]
    [InlineData("preview", ViewMode.Preview)]
    [InlineData("SPLIT", ViewMode.Split)]
    [InlineData("Ngawur", ViewMode.Split)]
    [InlineData("", ViewMode.Split)]
    [InlineData("99", ViewMode.Split)]
    public void SessionTab_ParsedMode_FallsBackToSplit(string mode, ViewMode expected)
    {
        Assert.Equal(expected, new SessionTab { Mode = mode }.ParsedMode);
    }

    [Fact]
    public void Load_UnknownTabMode_DoesNotInvalidateTheFile()
    {
        var s = AppSettings.Load(Settings("""{"Session":{"Tabs":[{"Path":"C:\\a.md","Mode":"Aneh"}]},"ZoomPercent":160}"""));

        Assert.Equal(160, s.ZoomPercent);
        Assert.Equal(ViewMode.Split, s.Session!.Tabs[0].ParsedMode);
    }

    // ---- Save ----

    [Fact]
    public void SaveThenLoad_RoundTripsThemeZoomRecentAndSession()
    {
        var path = dir.File("settings.json");
        var original = new AppSettings
        {
            Theme = nameof(AppThemeMode.Dark),
            ZoomPercent = 150,
            RecentFiles = [@"C:\dok\a.md", @"D:\ü日本.md"],
            Session = new SessionState
            {
                ActiveIndex = 1,
                Tabs =
                [
                    new SessionTab { Path = @"C:\dok\a.md", Mode = "Edit", CaretOffset = 12 },
                    new SessionTab { Path = @"D:\ü日本.md", Mode = "Preview", CaretOffset = 0 },
                ],
            },
        };

        Assert.True(original.Save(path));
        var loaded = AppSettings.Load(path);

        Assert.Equal(AppThemeMode.Dark, loaded.ParsedTheme);
        Assert.Equal(150, loaded.ZoomPercent);
        Assert.Equal(original.RecentFiles, loaded.RecentFiles);
        Assert.Equal(1, loaded.Session!.ActiveIndex);
        Assert.Equal(2, loaded.Session.Tabs.Count);
        Assert.Equal(12, loaded.Session.Tabs[0].CaretOffset);
        Assert.Equal(ViewMode.Edit, loaded.Session.Tabs[0].ParsedMode);
        Assert.Equal(ViewMode.Preview, loaded.Session.Tabs[1].ParsedMode);
        Assert.Equal(@"D:\ü日本.md", loaded.Session.Tabs[1].Path);
    }

    [Theory]
    [InlineData(AppThemeMode.System)]
    [InlineData(AppThemeMode.Light)]
    [InlineData(AppThemeMode.Dark)]
    public void SaveThenLoad_RoundTripsEachTheme(AppThemeMode mode)
    {
        var path = dir.File("t.json");

        new AppSettings { Theme = mode.ToString() }.Save(path);

        Assert.Equal(mode, AppSettings.Load(path).ParsedTheme);
    }

    [Theory]
    [InlineData(ZoomLevel.Min)]
    [InlineData(ZoomLevel.Default)]
    [InlineData(ZoomLevel.Max)]
    [InlineData(110)]
    public void SaveThenLoad_RoundTripsZoom(int zoom)
    {
        var path = dir.File("z.json");

        new AppSettings { ZoomPercent = zoom }.Save(path);

        Assert.Equal(zoom, AppSettings.Load(path).ZoomPercent);
    }

    [Fact]
    public void Save_CreatesMissingDirectories()
    {
        var path = dir.File(Path.Combine("a", "b", "settings.json"));

        Assert.True(new AppSettings().Save(path));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Save_WritesUtf8WithoutBom_AndLeavesNoTempFiles()
    {
        var path = dir.File("settings.json");

        new AppSettings().Save(path);

        var bytes = File.ReadAllBytes(path);
        Assert.NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal(new[] { "settings.json" }, dir.Entries());
    }

    [Fact]
    public void Save_OverwritesPreviousContent()
    {
        var path = dir.File("settings.json");
        new AppSettings { ZoomPercent = 200 }.Save(path);

        new AppSettings { ZoomPercent = 80 }.Save(path);

        Assert.Equal(80, AppSettings.Load(path).ZoomPercent);
    }

    [Fact]
    public void Save_TargetIsDirectory_ReturnsFalseWithoutThrowing()
    {
        Directory.CreateDirectory(dir.File("settings.json"));

        Assert.False(new AppSettings().Save(dir.File("settings.json")));
    }

    [Fact]
    public void Save_ParentIsAFile_ReturnsFalseWithoutThrowing()
    {
        var blocker = dir.WriteText("blocker", "x");

        Assert.False(new AppSettings().Save(Path.Combine(blocker, "settings.json")));
    }

    [Fact]
    public void Save_FileLocked_ReturnsFalseAndKeepsPreviousSettings()
    {
        var path = dir.File("settings.json");
        new AppSettings { ZoomPercent = 120 }.Save(path);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(new AppSettings { ZoomPercent = 250 }.Save(path));
        }

        Assert.Equal(120, AppSettings.Load(path).ZoomPercent);
        Assert.Equal(new[] { "settings.json" }, dir.Entries());
    }

    [Fact]
    public void Save_InvalidPathCharacters_ReturnsFalseWithoutThrowing()
    {
        Assert.False(new AppSettings().Save(dir.File("a<b>|c?.json") + "\0"));
    }

    [Fact]
    public void Save_PersistsOutOfRangeValuesAsIs_ButLoadSanitizesThem()
    {
        var path = dir.File("settings.json");

        new AppSettings { ZoomPercent = 9999, Theme = "Neon" }.Save(path);
        var loaded = AppSettings.Load(path);

        Assert.Equal(300, loaded.ZoomPercent);
        Assert.Equal(AppThemeMode.System, loaded.ParsedTheme);
    }

    // ---- RecentFiles ----

    [Fact]
    public void AddRecent_PutsNewestFirst()
    {
        var s = new AppSettings();

        s.AddRecent(@"C:\a.md");
        s.AddRecent(@"C:\b.md");

        Assert.Equal(new[] { @"C:\b.md", @"C:\a.md" }, s.RecentFiles);
    }

    [Fact]
    public void AddRecent_Duplicate_MovesToTopIgnoringCase_AndUsesNewSpelling()
    {
        var s = new AppSettings();
        s.AddRecent(@"C:\a.md");
        s.AddRecent(@"C:\b.md");

        s.AddRecent(@"c:\A.MD");

        Assert.Equal(new[] { @"c:\A.MD", @"C:\b.md" }, s.RecentFiles);
    }

    [Fact]
    public void AddRecent_CapsAtTen_DroppingOldest()
    {
        var s = new AppSettings();
        for (var i = 1; i <= 12; i++) s.AddRecent($@"C:\f{i}.md");

        Assert.Equal(AppSettings.MaxRecentFiles, s.RecentFiles.Count);
        Assert.Equal(@"C:\f12.md", s.RecentFiles[0]);
        Assert.Equal(@"C:\f3.md", s.RecentFiles[^1]);
        Assert.DoesNotContain(@"C:\f1.md", s.RecentFiles);
        Assert.DoesNotContain(@"C:\f2.md", s.RecentFiles);
    }

    [Fact]
    public void AddRecent_ReaddingExistingAtCapacity_DoesNotEvictAnything()
    {
        var s = new AppSettings();
        for (var i = 1; i <= 10; i++) s.AddRecent($@"C:\f{i}.md");

        s.AddRecent(@"C:\f1.md");

        Assert.Equal(10, s.RecentFiles.Count);
        Assert.Equal(@"C:\f1.md", s.RecentFiles[0]);
        Assert.Contains(@"C:\f2.md", s.RecentFiles);
    }

    [Fact]
    public void RemoveRecent_IgnoresCase_AndMissingEntryIsNoOp()
    {
        var s = new AppSettings();
        s.AddRecent(@"C:\a.md");
        s.AddRecent(@"C:\b.md");

        s.RemoveRecent(@"C:\A.MD");
        s.RemoveRecent(@"C:\tidak-ada.md");

        Assert.Equal(new[] { @"C:\b.md" }, s.RecentFiles);
    }

    [Fact]
    public void DefaultPath_PointsToAppDataMdViewerSettingsJson_WithoutBeingTouched()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MdViewer", "settings.json");

        Assert.Equal(expected, AppSettings.DefaultPath);
    }
}
