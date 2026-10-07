using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MdViewer.Tests;

public class ZoomLevelTests
{
    [Theory]
    [InlineData(-100, 50)]
    [InlineData(0, 50)]
    [InlineData(49, 50)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(300, 300)]
    [InlineData(301, 300)]
    [InlineData(int.MaxValue, 300)]
    [InlineData(int.MinValue, 50)]
    public void Clamp_KeepsPercentWithinRange(int input, int expected)
    {
        Assert.Equal(expected, ZoomLevel.Clamp(input));
    }

    [Theory]
    [InlineData(100, 1, 110)]
    [InlineData(100, -1, 90)]
    [InlineData(100, 0, 100)]
    [InlineData(100, 5, 110)]      // hanya arah yang dihitung, bukan besarnya
    [InlineData(100, -99, 90)]
    [InlineData(300, 1, 300)]
    [InlineData(50, -1, 50)]
    [InlineData(295, 1, 300)]
    [InlineData(55, -1, 50)]
    [InlineData(1000, 1, 300)]     // di luar rentang: diklem dulu
    [InlineData(1000, -1, 290)]
    [InlineData(-10, 1, 60)]
    [InlineData(-10, -1, 50)]
    [InlineData(100, int.MaxValue, 110)]
    [InlineData(100, int.MinValue, 90)]
    public void Step_MovesByTenPercentAndStaysInRange(int current, int direction, int expected)
    {
        Assert.Equal(expected, ZoomLevel.Step(current, direction));
    }

    [Fact]
    public void Step_RepeatedZoomIn_ReachesMaxAndStops()
    {
        var z = ZoomLevel.Default;
        for (var i = 0; i < 100; i++) z = ZoomLevel.Step(z, 1);

        Assert.Equal(ZoomLevel.Max, z);
    }

    [Fact]
    public void Step_RepeatedZoomOut_ReachesMinAndStops()
    {
        var z = ZoomLevel.Default;
        for (var i = 0; i < 100; i++) z = ZoomLevel.Step(z, -1);

        Assert.Equal(ZoomLevel.Min, z);
    }

    [Theory]
    [InlineData(100, 14.0)]
    [InlineData(50, 7.0)]
    [InlineData(300, 42.0)]
    [InlineData(150, 21.0)]
    [InlineData(110, 15.4)]
    [InlineData(1, 7.0)]      // diklem ke 50
    [InlineData(9999, 42.0)]  // diklem ke 300
    [InlineData(133, 18.62)]
    public void EditorFontSize_ScalesBaseSizeAndClamps(int percent, double expected)
    {
        Assert.Equal(expected, ZoomLevel.EditorFontSize(percent), 6);
    }

    [Theory]
    [InlineData(100, "100%")]
    [InlineData(10, "50%")]
    [InlineData(500, "300%")]
    public void Format_ShowsClampedPercent(int percent, string expected)
    {
        Assert.Equal(expected, ZoomLevel.Format(percent));
    }
}

public class TextStatsTests
{
    [Theory]
    [InlineData(null, 0, 0)]
    [InlineData("", 0, 0)]
    [InlineData(" ", 0, 1)]
    [InlineData("   \t  ", 0, 6)]
    [InlineData("a", 1, 1)]
    [InlineData("hello world", 2, 11)]
    [InlineData("  hello   world  ", 2, 17)]
    [InlineData("hello\nworld", 2, 10)]
    [InlineData("hello\r\nworld", 2, 10)]
    [InlineData("a\rb", 2, 2)]
    [InlineData("\n\n\n", 0, 0)]
    [InlineData("one\ttwo", 2, 7)]
    [InlineData("kata-kata, bukan dua?", 3, 21)]
    [InlineData("a\u00A0b", 2, 3)]           // spasi tak terputus
    [InlineData("日本語 テキスト", 2, 8)]
    [InlineData("😀", 1, 2)]                  // pasangan surrogat = 2 unit UTF-16
    public void Count_WordsAndCharacters(string? text, int words, int characters)
    {
        Assert.Equal((words, characters), TextStats.Count(text));
    }

    [Fact]
    public void Count_TrailingNewline_DoesNotAddWordsOrCharacters()
    {
        Assert.Equal((2, 4), TextStats.Count("ab\ncd\r\n"));
    }

    [Fact]
    public void Count_LargeText_IsLinear()
    {
        var text = string.Concat(Enumerable.Repeat("kata ", 200_000));

        Assert.Equal((200_000, 1_000_000), TextStats.Count(text));
    }

    [Fact]
    public void Format_UsesIndonesianThousandsSeparators()
    {
        Assert.Equal("1.234 kata · 56.789 karakter", TextStats.Format(1234, 56789));
    }

    [Fact]
    public void Format_Zero()
    {
        Assert.Equal("0 kata · 0 karakter", TextStats.Format(0, 0));
    }

    [Fact]
    public void Format_IsIndependentOfCurrentCulture()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("1.234 kata · 5 karakter", TextStats.Format(1234, 5));
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }
    }
}

public class EncodingNamesTests
{
    [Fact]
    public void Describe_Null_IsEmpty()
    {
        Assert.Equal("", EncodingNames.Describe(null));
    }

    [Fact]
    public void Describe_Utf8_WithAndWithoutBom()
    {
        Assert.Equal("UTF-8", EncodingNames.Describe(new UTF8Encoding(false)));
        Assert.Equal("UTF-8 BOM", EncodingNames.Describe(new UTF8Encoding(true)));
    }

    [Fact]
    public void Describe_Utf16_Utf32_Endianness()
    {
        Assert.Equal("UTF-16 LE", EncodingNames.Describe(new UnicodeEncoding(false, true)));
        Assert.Equal("UTF-16 BE", EncodingNames.Describe(new UnicodeEncoding(true, true)));
        Assert.Equal("UTF-32 LE", EncodingNames.Describe(new UTF32Encoding(false, true)));
        Assert.Equal("UTF-32 BE", EncodingNames.Describe(new UTF32Encoding(true, true)));
    }

    [Fact]
    public void Describe_Windows1252_AndOthersByWebName()
    {
        _ = TextFileIO.Decode([]); // mendaftarkan provider code page
        Assert.Equal("Windows-1252", EncodingNames.Describe(Encoding.GetEncoding(1252)));
        Assert.Equal("ISO-8859-1", EncodingNames.Describe(Encoding.Latin1));
        Assert.Equal("US-ASCII", EncodingNames.Describe(Encoding.ASCII));
    }

    [Fact]
    public void Describe_MatchesWhatDecodeReports()
    {
        Assert.Equal("UTF-8 BOM", EncodingNames.Describe(TextFileIO.Decode([0xEF, 0xBB, 0xBF, 0x61]).Encoding));
        Assert.Equal("UTF-8", EncodingNames.Describe(TextFileIO.Decode("a"u8).Encoding));
        Assert.Equal("UTF-16 LE", EncodingNames.Describe(TextFileIO.Decode([0xFF, 0xFE, 0x61, 0x00]).Encoding));
        Assert.Equal("UTF-16 BE", EncodingNames.Describe(TextFileIO.Decode([0xFE, 0xFF, 0x00, 0x61]).Encoding));
        Assert.Equal("Windows-1252", EncodingNames.Describe(TextFileIO.Decode([0x61, 0xE9]).Encoding));
    }
}

public class ThemeManagerPureTests
{
    [Theory]
    [InlineData("System", AppThemeMode.System)]
    [InlineData("Light", AppThemeMode.Light)]
    [InlineData("Dark", AppThemeMode.Dark)]
    [InlineData("dark", AppThemeMode.Dark)]
    [InlineData("LIGHT", AppThemeMode.Light)]
    [InlineData("sYsTeM", AppThemeMode.System)]
    [InlineData(null, AppThemeMode.System)]
    [InlineData("", AppThemeMode.System)]
    [InlineData("   ", AppThemeMode.System)]
    [InlineData("Neon", AppThemeMode.System)]
    [InlineData("99", AppThemeMode.System)]
    [InlineData("-1", AppThemeMode.System)]
    [InlineData("Dark,Light", AppThemeMode.System)]
    [InlineData(" Dark", AppThemeMode.Dark)]
    public void Parse_KnownNamesIgnoringCase_ElseSystem(string? value, AppThemeMode expected)
    {
        Assert.Equal(expected, ThemeManager.Parse(value));
    }

    [Theory]
    [InlineData(AppThemeMode.Dark, true, true)]
    [InlineData(AppThemeMode.Dark, false, true)]
    [InlineData(AppThemeMode.Light, true, false)]
    [InlineData(AppThemeMode.Light, false, false)]
    [InlineData(AppThemeMode.System, true, false)]
    [InlineData(AppThemeMode.System, false, true)]
    public void ResolveIsDark_ExplicitModeWinsOverSystem(AppThemeMode mode, bool systemUsesLight, bool expectedDark)
    {
        Assert.Equal(expectedDark, ThemeManager.ResolveIsDark(mode, systemUsesLight));
    }

    [Fact]
    public void ResolveIsDark_UndefinedEnumValue_FollowsSystem()
    {
        Assert.True(ThemeManager.ResolveIsDark((AppThemeMode)42, systemUsesLight: false));
        Assert.False(ThemeManager.ResolveIsDark((AppThemeMode)42, systemUsesLight: true));
    }

    [Fact]
    public void SystemUsesLightTheme_ReadsRegistryWithoutThrowing()
    {
        var ex = Record.Exception(() => ThemeManager.SystemUsesLightTheme());

        Assert.Null(ex);
    }
}

public class EditorThemeContrastTests
{
    static Color Gray(byte v) => Color.FromRgb(v, v, v);

    [Fact]
    public void RelativeLuminance_BlackIsZero_WhiteIsOne()
    {
        Assert.Equal(0.0, EditorTheme.RelativeLuminance(Colors.Black), 9);
        Assert.Equal(1.0, EditorTheme.RelativeLuminance(Colors.White), 9);
    }

    [Fact]
    public void RelativeLuminance_UsesWcagChannelWeights()
    {
        Assert.Equal(0.2126, EditorTheme.RelativeLuminance(Colors.Red), 4);
        Assert.Equal(0.7152, EditorTheme.RelativeLuminance(Colors.Lime), 4);
        Assert.Equal(0.0722, EditorTheme.RelativeLuminance(Colors.Blue), 4);
    }

    [Fact]
    public void RelativeLuminance_IncreasesWithBrightness()
    {
        var previous = -1.0;
        for (var v = 0; v <= 255; v += 5)
        {
            var l = EditorTheme.RelativeLuminance(Gray((byte)v));
            Assert.True(l > previous);
            previous = l;
        }
    }

    [Fact]
    public void ContrastRatio_BlackOnWhite_Is21()
    {
        Assert.Equal(21.0, EditorTheme.ContrastRatio(Colors.Black, Colors.White), 6);
    }

    [Fact]
    public void ContrastRatio_SameColor_Is1()
    {
        Assert.Equal(1.0, EditorTheme.ContrastRatio(Colors.SteelBlue, Colors.SteelBlue), 9);
    }

    [Fact]
    public void ContrastRatio_IsSymmetric()
    {
        var a = Color.FromRgb(10, 120, 200);
        var b = Color.FromRgb(240, 230, 40);

        Assert.Equal(EditorTheme.ContrastRatio(a, b), EditorTheme.ContrastRatio(b, a), 12);
    }

    [Fact]
    public void ContrastRatio_KnownValue_GrayOnWhite()
    {
        // #767676 di atas putih = 4.54:1 (batas AA yang terkenal).
        Assert.Equal(4.54, EditorTheme.ContrastRatio(Gray(0x76), Colors.White), 2);
    }

    [Fact]
    public void EnsureContrast_AlreadySufficient_ReturnsSameColor()
    {
        var fg = Colors.Black;

        Assert.Equal(fg, EditorTheme.EnsureContrast(fg, Colors.White));
    }

    [Fact]
    public void EnsureContrast_LowContrastOnLightBackground_DarkensUntilReadable()
    {
        var fg = Gray(0xCC);

        var result = EditorTheme.EnsureContrast(fg, Colors.White);

        Assert.True(EditorTheme.ContrastRatio(result, Colors.White) >= 4.5);
        Assert.True(EditorTheme.RelativeLuminance(result) < EditorTheme.RelativeLuminance(fg));
    }

    [Fact]
    public void EnsureContrast_LowContrastOnDarkBackground_LightensUntilReadable()
    {
        var bg = Color.FromRgb(0x1E, 0x1E, 0x1E);
        var fg = Color.FromRgb(0x30, 0x30, 0x60);

        var result = EditorTheme.EnsureContrast(fg, bg);

        Assert.True(EditorTheme.ContrastRatio(result, bg) >= 4.5);
        Assert.True(EditorTheme.RelativeLuminance(result) > EditorTheme.RelativeLuminance(fg));
    }

    [Fact]
    public void EnsureContrast_KeepsHueRoughlyByInterpolating_NotJumpingToBlack()
    {
        var fg = Color.FromRgb(0xFF, 0xB0, 0xB0); // merah muda pucat di atas putih

        var result = EditorTheme.EnsureContrast(fg, Colors.White);

        Assert.True(result.R > result.G, "tetap kemerahan");
        Assert.NotEqual(Colors.Black, result);
    }

    [Fact]
    public void EnsureContrast_ImpossibleMinimum_FallsBackToExtremeColor()
    {
        Assert.Equal(Colors.Black, EditorTheme.EnsureContrast(Gray(0x80), Colors.White, minimum: 50));
        Assert.Equal(Colors.White, EditorTheme.EnsureContrast(Gray(0x80), Colors.Black, minimum: 50));
    }

    [Fact]
    public void EnsureContrast_CustomMinimum_IsRespected()
    {
        var result = EditorTheme.EnsureContrast(Gray(0xAA), Colors.White, minimum: 7);

        Assert.True(EditorTheme.ContrastRatio(result, Colors.White) >= 7);
    }

    [Theory]
    [InlineData(0xFF, 0xFF, 0xFF)]
    [InlineData(0xF5, 0xF5, 0xF5)]
    [InlineData(0x1E, 0x1E, 0x1E)]
    [InlineData(0x00, 0x00, 0x00)]
    public void EnsureContrast_ForRealThemeBackgrounds_AlwaysReachesAA(byte r, byte g, byte b)
    {
        var bg = Color.FromRgb(r, g, b);
        for (var fr = 0; fr <= 255; fr += 51)
            for (var fg = 0; fg <= 255; fg += 51)
                for (var fb = 0; fb <= 255; fb += 51)
                {
                    var result = EditorTheme.EnsureContrast(Color.FromRgb((byte)fr, (byte)fg, (byte)fb), bg);
                    Assert.True(EditorTheme.ContrastRatio(result, bg) >= 4.5 - 1e-9, $"fg=({fr},{fg},{fb}) bg=({r},{g},{b})");
                }
    }

    [Fact]
    public void EnsureContrast_MidToneBackground_ChoosesTheDirectionThatCanReachMinimum()
    {
        var bg = Gray(0xB0);

        var result = EditorTheme.EnsureContrast(Gray(0xA0), bg);

        Assert.True(EditorTheme.ContrastRatio(result, bg) >= 4.5);
    }
}

public class ConverterTests
{
    static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    // ---- ViewModeLabelConverter ----

    [Theory]
    [InlineData(ViewMode.Edit, "Editor")]
    [InlineData(ViewMode.Split, "Terpisah")]
    [InlineData(ViewMode.Preview, "Pratinjau")]
    public void ViewModeLabel_MapsEachMode(ViewMode mode, string label)
    {
        Assert.Equal(label, new ViewModeLabelConverter().Convert(mode, typeof(string), null!, Culture));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Edit")]
    [InlineData(1)]
    public void ViewModeLabel_NonViewModeValue_IsEmpty(object? value)
    {
        Assert.Equal("", new ViewModeLabelConverter().Convert(value, typeof(string), null!, Culture));
    }

    [Fact]
    public void ViewModeLabel_UndefinedEnumValue_IsEmpty()
    {
        Assert.Equal("", new ViewModeLabelConverter().Convert((ViewMode)77, typeof(string), null!, Culture));
    }

    // ---- EditorVisibleConverter ----

    [Theory]
    [InlineData(ViewMode.Edit, Visibility.Visible)]
    [InlineData(ViewMode.Split, Visibility.Visible)]
    [InlineData(ViewMode.Preview, Visibility.Collapsed)]
    public void EditorVisible_HidesOnlyInPreview(ViewMode mode, Visibility expected)
    {
        Assert.Equal(expected, new EditorVisibleConverter().Convert(mode, typeof(Visibility), null!, Culture));
    }

    [Fact]
    public void EditorVisible_NullOrWrongType_IsCollapsed()
    {
        var c = new EditorVisibleConverter();

        Assert.Equal(Visibility.Collapsed, c.Convert(null, typeof(Visibility), null!, Culture));
        Assert.Equal(Visibility.Collapsed, c.Convert("Edit", typeof(Visibility), null!, Culture));
    }

    // ---- PathPartConverter ----

    [Theory]
    [InlineData(@"C:\dok\catatan.md", "Name", "catatan.md")]
    [InlineData(@"C:\dok\catatan.md", null, "catatan.md")]
    [InlineData(@"C:\dok\catatan.md", "Apa saja", "catatan.md")]
    [InlineData(@"C:\dok\catatan.md", "Directory", @"C:\dok")]
    [InlineData(@"C:\dok\my_file_name.md", "MenuName", "my__file__name.md")]
    [InlineData(@"C:\dok\polos.md", "MenuName", "polos.md")]
    [InlineData("catatan.md", "Directory", "")]
    [InlineData("catatan.md", "Name", "catatan.md")]
    [InlineData(@"C:\", "Directory", "")]
    [InlineData(@"C:\dok\", "Name", "")]
    public void PathPart_SplitsPath(string path, string? parameter, string expected)
    {
        Assert.Equal(expected, new PathPartConverter().Convert(path, typeof(string), parameter!, Culture));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(42)]
    public void PathPart_EmptyOrNonStringValue_IsEmpty(object? value)
    {
        Assert.Equal("", new PathPartConverter().Convert(value, typeof(string), "Name", Culture));
    }

    // ---- NotNullConverter ----

    [Fact]
    public void NotNull_TrueForAnyObject_FalseForNull()
    {
        var c = NotNullConverter.Instance;

        Assert.Equal(false, c.Convert(null, typeof(bool), null!, Culture));
        Assert.Equal(true, c.Convert(new object(), typeof(bool), null!, Culture));
        Assert.Equal(true, c.Convert("", typeof(bool), null!, Culture));
        Assert.Equal(true, c.Convert(0, typeof(bool), null!, Culture));
    }

    // ---- ViewModeConverter ----

    [Theory]
    [InlineData(ViewMode.Edit, "Edit", true)]
    [InlineData(ViewMode.Edit, "Split", false)]
    [InlineData(ViewMode.Preview, "Preview", true)]
    [InlineData(ViewMode.Split, "Preview", false)]
    [InlineData(ViewMode.Split, "split", false)] // nama harus persis (ConvertBack/Convert memakai TryParse peka huruf besar-kecil)
    public void ViewMode_Convert_ComparesWithParameterName(ViewMode mode, string parameter, bool expected)
    {
        Assert.Equal(expected, new ViewModeConverter().Convert(mode, typeof(bool), parameter, Culture));
    }

    [Theory]
    [InlineData(null, "Edit")]
    [InlineData("Edit", "Edit")]
    [InlineData(ViewMode.Edit, null)]
    [InlineData(ViewMode.Edit, 5)]
    [InlineData(ViewMode.Edit, "Bukan Mode")]
    [InlineData(ViewMode.Edit, "")]
    public void ViewMode_Convert_InvalidInput_IsFalse(object? value, object? parameter)
    {
        Assert.Equal(false, new ViewModeConverter().Convert(value!, typeof(bool), parameter!, Culture));
    }

    [Theory]
    [InlineData("Edit", ViewMode.Edit)]
    [InlineData("Split", ViewMode.Split)]
    [InlineData("Preview", ViewMode.Preview)]
    public void ViewMode_ConvertBack_CheckedRadioYieldsMode(string parameter, ViewMode expected)
    {
        Assert.Equal(expected, new ViewModeConverter().ConvertBack(true, typeof(ViewMode), parameter, Culture));
    }

    [Theory]
    [InlineData(false, "Edit")]
    [InlineData(null, "Edit")]
    [InlineData("true", "Edit")]
    [InlineData(true, "Nonsense")]
    [InlineData(true, null)]
    public void ViewMode_ConvertBack_UncheckedOrInvalid_DoesNothing(object? value, object? parameter)
    {
        Assert.Same(Binding.DoNothing, new ViewModeConverter().ConvertBack(value!, typeof(ViewMode), parameter!, Culture));
    }

    // ---- ConvertBack tidak didukung ----

    [Fact]
    public void ConvertBack_NotSupported_ForOneWayConverters()
    {
        Assert.Throws<NotSupportedException>(() => new ViewModeLabelConverter().ConvertBack("x", typeof(ViewMode), null!, Culture));
        Assert.Throws<NotSupportedException>(() => new EditorVisibleConverter().ConvertBack("x", typeof(ViewMode), null!, Culture));
        Assert.Throws<NotSupportedException>(() => new PathPartConverter().ConvertBack("x", typeof(string), null!, Culture));
        Assert.Throws<NotSupportedException>(() => NotNullConverter.Instance.ConvertBack(true, typeof(object), null!, Culture));
    }
}
