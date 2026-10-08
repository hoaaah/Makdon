namespace Makdon.Tests;

/// <summary>
/// Tautan dokumen -> path file markdown (<see cref="MarkdownSupport.ResolveLinkTarget"/>): tidak boleh menghasilkan path UNC/perangkat
/// (mencegah kebocoran SMB/NTLM lewat File.Exists), termasuk yang ter-percent-encode.
/// </summary>
public class LinkResolutionTests
{
    const string LocalBase = @"C:\docs\notes";
    const string ShareBase = @"\\host\share\docs";

    static string? Resolve(string? baseDir, string? url) => MarkdownSupport.ResolveLinkTarget(baseDir, url, out _);

    // ---- Vektor UNC / perangkat ----

    [Theory]
    [InlineData(@"%5C%5Chost%5Cs%5Cx.md")]
    [InlineData(@"%5c%5cattacker.example%5cshare%5cx.md")]
    [InlineData(@"%2F%2Fhost%2Fs%2Fx.md")]
    [InlineData(@"%2f%2fhost%2fs%2fx.md")]
    [InlineData(@"\\host\s\x.md")]
    [InlineData(@"//host/s/x.md")]
    [InlineData(@"\\host\s\x.md#bag")]
    [InlineData(@"%5C%5C%3F%5CUNC%5Chost%5Cs%5Cx.md")]
    [InlineData(@"%5C%5C.%5Cpipe%5Cx.md")]
    [InlineData(@"\\?\UNC\host\s\x.md")]
    [InlineData(@"\\?\C:\docs\x.md")]
    [InlineData(@"\\.\pipe\x")]
    [InlineData(@"\\.\pipe\x.md")]
    [InlineData(@"//?/UNC/host/s/x.md")]
    public void UncAndDevicePaths_AreRejected(string url)
    {
        Assert.Null(Resolve(LocalBase, url));
        Assert.Null(Resolve(null, url));
    }

    [Theory]
    [InlineData(@"\\other\share\x.md")]
    [InlineData(@"%5C%5Cother%5Cshare%5Cx.md")]
    [InlineData(@"%5C%5Chost%5Cother%5Cx.md")]
    [InlineData(@"\\?\UNC\host\share\x.md")]
    [InlineData(@"file://host/share/x.md")]
    public void SameShareDocument_StillRejectsOtherShares_AndDevicePaths(string url)
    {
        // file://host/... dan \\?\UNC\... ditolak walau share-nya sama dengan dokumen (URL berhost / path perangkat).
        Assert.Null(Resolve(ShareBase, url));
    }

    [Theory]
    [InlineData(@"sub/x.md", @"\\host\share\docs\sub\x.md")]
    [InlineData(@"../x.md", @"\\host\share\x.md")]
    [InlineData(@"..\..\..\x.md", @"\\host\share\x.md")]
    [InlineData(@"%5C%5Chost%5Cshare%5Cother%5Cx.md", @"\\host\share\other\x.md")]
    [InlineData(@"%5C%5CHOST%5CSHARE%5Cx.md", @"\\HOST\SHARE\x.md")]
    [InlineData(@"\\host\share\x.md", @"\\host\share\x.md")]
    public void DocumentOnShare_AllowsLinksOnTheSameShare(string url, string expected)
    {
        Assert.Equal(expected, Resolve(ShareBase, url), ignoreCase: true);
    }

    // ---- Drive lokal ----

    [Theory]
    [InlineData(@"x.md", @"C:\docs\notes\x.md")]
    [InlineData(@"sub/x.md", @"C:\docs\notes\sub\x.md")]
    [InlineData(@"sub\x.md", @"C:\docs\notes\sub\x.md")]
    [InlineData(@"../x.md", @"C:\docs\x.md")]
    [InlineData(@"./x.md", @"C:\docs\notes\x.md")]
    [InlineData(@"my%20file.md", @"C:\docs\notes\my file.md")]
    [InlineData(@"sub%2Fx.md", @"C:\docs\notes\sub\x.md")]
    [InlineData(@"README.MARKDOWN", @"C:\docs\notes\README.MARKDOWN")]
    [InlineData(@"catatan.txt", @"C:\docs\notes\catatan.txt")]
    [InlineData(@"D:\other\x.md", @"D:\other\x.md")]
    public void LocalRelativeAndDriveLinks_Resolve(string url, string expected)
    {
        Assert.Equal(expected, Resolve(LocalBase, url), ignoreCase: true);
    }

    [Theory]
    [InlineData(@"x.md#bag", @"C:\docs\notes\x.md", "bag")]
    [InlineData(@"sub/x.md#a-b-c", @"C:\docs\notes\sub\x.md", "a-b-c")]
    [InlineData(@"x.md#", @"C:\docs\notes\x.md", "")]
    [InlineData(@"x.md", @"C:\docs\notes\x.md", null)]
    [InlineData(@"file:///C:/docs/y.md#bag", @"C:\docs\y.md", "bag")]
    public void Anchor_IsSplitFromPath(string url, string expectedPath, string? expectedAnchor)
    {
        var target = MarkdownSupport.ResolveLinkTarget(LocalBase, url, out var anchor);
        Assert.Equal(expectedPath, target, ignoreCase: true);
        Assert.Equal(expectedAnchor, anchor);
    }

    // ---- file: ----

    [Theory]
    [InlineData(@"file:///C:/docs/y.md", @"C:\docs\y.md")]
    [InlineData(@"file:///C:/my%20docs/y.md", @"C:\my docs\y.md")]
    [InlineData(@"file:///D:/a/b.markdown", @"D:\a\b.markdown")]
    public void FileUriWithoutHost_ResolvesToLocalPath(string url, string expected)
    {
        Assert.Equal(expected, Resolve(LocalBase, url), ignoreCase: true);
        Assert.Equal(expected, Resolve(null, url), ignoreCase: true);
    }

    [Theory]
    [InlineData(@"file://host/s/x.md")]
    [InlineData(@"file://attacker.example/share/x.md")]
    [InlineData(@"file:////host/s/x.md")]
    [InlineData(@"file://localhost/C$/x.md")]
    public void FileUriWithHost_IsRejected(string url)
    {
        Assert.Null(Resolve(LocalBase, url));
        Assert.Null(Resolve(ShareBase, url));
    }

    [Theory]
    [InlineData(@"http://example.com/x.md")]
    [InlineData(@"ftp://example.com/x.md")]
    [InlineData(@"javascript:alert(1)")]
    public void OtherSchemes_AreRejected(string url)
    {
        Assert.Null(Resolve(LocalBase, url));
    }

    // ---- Ekstensi ----

    [Theory]
    [InlineData(@"run.exe")]
    [InlineData(@"script.ps1")]
    [InlineData(@"x.md.exe")]
    [InlineData(@"noext")]
    [InlineData(@"x.md:stream")]
    [InlineData(@"sub/")]
    [InlineData(@"image.png#bag")]
    [InlineData(@"%5C%5Chost%5Cs%5Cx.exe")]
    [InlineData(@"file:///C:/windows/system32/cmd.exe")]
    public void NonMarkdown_IsRejected(string url)
    {
        Assert.Null(Resolve(LocalBase, url));
    }

    // ---- Masukan kosong ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData("#bag")]
    public void NullEmptyAndAnchorOnly_ReturnNull(string? url)
    {
        var target = MarkdownSupport.ResolveLinkTarget(LocalBase, url, out var anchor);
        Assert.Null(target);
        Assert.Null(anchor);
    }

    [Fact]
    public void RelativeLink_WithoutBaseDir_IsRejected()
    {
        Assert.Null(Resolve(null, "x.md"));
    }

    [Fact]
    public void InvalidPath_ReturnsNullWithoutThrowing()
    {
        Assert.Null(Resolve(LocalBase, "a%00b.md"));
        Assert.Null(Resolve(LocalBase, new string('a', 40000) + ".md"));
    }
}
