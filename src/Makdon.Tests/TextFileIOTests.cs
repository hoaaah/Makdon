using System.Text;
using Makdon.Tests.Support;

namespace Makdon.Tests;

public class TextFileIODecodeTests
{
    static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    static byte[] Concat(byte[] a, byte[] b) => [.. a, .. b];

    [Fact]
    public void Decode_Utf8Bom_StripsBomAndKeepsPreamble()
    {
        var (text, enc) = TextFileIO.Decode(Concat(Utf8Bom, Encoding.UTF8.GetBytes("héllo")));

        Assert.Equal("héllo", text);
        Assert.Equal(65001, enc.CodePage);
        Assert.Equal(Utf8Bom, enc.GetPreamble());
    }

    [Fact]
    public void Decode_Utf16LittleEndianBom()
    {
        var bytes = Concat([0xFF, 0xFE], new UnicodeEncoding(false, false).GetBytes("日本 ok"));

        var (text, enc) = TextFileIO.Decode(bytes);

        Assert.Equal("日本 ok", text);
        Assert.Equal(1200, enc.CodePage);
        Assert.Equal(new byte[] { 0xFF, 0xFE }, enc.GetPreamble());
    }

    [Fact]
    public void Decode_Utf16BigEndianBom()
    {
        var bytes = Concat([0xFE, 0xFF], new UnicodeEncoding(true, false).GetBytes("日本 ok"));

        var (text, enc) = TextFileIO.Decode(bytes);

        Assert.Equal("日本 ok", text);
        Assert.Equal(1201, enc.CodePage);
        Assert.Equal(new byte[] { 0xFE, 0xFF }, enc.GetPreamble());
    }

    [Fact]
    public void Decode_Utf32LittleEndianBom_IsNotMistakenForUtf16()
    {
        var bytes = Concat([0xFF, 0xFE, 0x00, 0x00], new UTF32Encoding(false, false).GetBytes("abc😀"));

        var (text, enc) = TextFileIO.Decode(bytes);

        Assert.Equal("abc😀", text);
        Assert.Equal(12000, enc.CodePage);
    }

    [Fact]
    public void Decode_Utf32BigEndianBom()
    {
        var bytes = Concat([0x00, 0x00, 0xFE, 0xFF], new UTF32Encoding(true, false).GetBytes("abc😀"));

        var (text, enc) = TextFileIO.Decode(bytes);

        Assert.Equal("abc😀", text);
        Assert.Equal(12001, enc.CodePage);
    }

    [Fact]
    public void Decode_Utf8WithoutBom_NonAscii_HasNoPreamble()
    {
        var (text, enc) = TextFileIO.Decode(Encoding.UTF8.GetBytes("naïve – 日本"));

        Assert.Equal("naïve – 日本", text);
        Assert.Equal(65001, enc.CodePage);
        Assert.Empty(enc.GetPreamble());
    }

    [Fact]
    public void Decode_PureAscii_IsUtf8WithoutBom()
    {
        var (text, enc) = TextFileIO.Decode("plain"u8);

        Assert.Equal("plain", text);
        Assert.Equal(65001, enc.CodePage);
        Assert.Empty(enc.GetPreamble());
    }

    [Fact]
    public void Decode_Empty_IsEmptyUtf8()
    {
        var (text, enc) = TextFileIO.Decode([]);

        Assert.Equal("", text);
        Assert.Equal(65001, enc.CodePage);
        Assert.Empty(enc.GetPreamble());
    }

    [Fact]
    public void Decode_BomOnly_IsEmptyTextButKeepsBom()
    {
        var (text, enc) = TextFileIO.Decode(Utf8Bom);

        Assert.Equal("", text);
        Assert.Equal(Utf8Bom, enc.GetPreamble());
    }

    [Fact]
    public void Decode_InvalidUtf8_FallsBackToWindows1252()
    {
        // 0xE9 = é dan 0x80 = € di Windows-1252; keduanya bukan UTF-8 yang valid.
        var (text, enc) = TextFileIO.Decode(new byte[] { 0x63, 0x61, 0x66, 0xE9, 0x20, 0x80 });

        Assert.Equal("café €", text);
        Assert.Equal(1252, enc.CodePage);
    }

    [Fact]
    public void Decode_TruncatedUtf8Sequence_FallsBackInsteadOfThrowing()
    {
        var (_, enc) = TextFileIO.Decode(new byte[] { 0x61, 0xE2, 0x82 }); // awal "€" terpotong

        Assert.NotEqual(65001, enc.CodePage);
    }

    [Fact]
    public void Decode_ByteUndefinedIn1252_StillRoundTripsExactly()
    {
        byte[] bytes = [0x41, 0x81, 0x8D, 0x8F, 0x90, 0x9D, 0xE9];

        var (text, enc) = TextFileIO.Decode(bytes);
        var back = TextFileIO.Encode(text, enc, out _);

        Assert.Equal(bytes, back);
    }

    public static IEnumerable<object[]> RoundTripFixtures()
    {
        yield return new object[] { "utf8", Encoding.UTF8.GetBytes("a\r\nb\nc – é 日本") };
        yield return new object[] { "utf8-bom", Concat(Utf8Bom, Encoding.UTF8.GetBytes("héllo\r\n")) };
        yield return new object[] { "utf8-bom-empty", Utf8Bom };
        yield return new object[] { "utf16le", Concat([0xFF, 0xFE], new UnicodeEncoding(false, false).GetBytes("日本\r\nx")) };
        yield return new object[] { "utf16be", Concat([0xFE, 0xFF], new UnicodeEncoding(true, false).GetBytes("日本\r\nx")) };
        yield return new object[] { "utf32le", Concat([0xFF, 0xFE, 0, 0], new UTF32Encoding(false, false).GetBytes("a😀")) };
        yield return new object[] { "utf32be", Concat([0, 0, 0xFE, 0xFF], new UTF32Encoding(true, false).GetBytes("a😀")) };
        yield return new object[] { "cp1252", new byte[] { 0x63, 0x61, 0x66, 0xE9, 0x20, 0x80, 0x93, 0x94, 0x0D, 0x0A } };
        yield return new object[] { "empty", Array.Empty<byte>() };
        yield return new object[] { "ascii-crlf", "a\r\nb\r\n"u8.ToArray() };
    }

    [Theory]
    [MemberData(nameof(RoundTripFixtures))]
    public void DecodeThenEncode_IsByteIdentical(string name, byte[] original)
    {
        _ = name;
        var (text, enc) = TextFileIO.Decode(original);

        var back = TextFileIO.Encode(text, enc, out var actual);

        Assert.Equal(original, back);
        Assert.Equal(enc.CodePage, actual.CodePage);
    }
}

public class TextFileIOEncodeTests
{
    [Fact]
    public void Encode_Utf8Bom_PrependsBom()
    {
        var bytes = TextFileIO.Encode("é", new UTF8Encoding(true), out var actual);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, 0xC3, 0xA9 }, bytes);
        Assert.Equal(65001, actual.CodePage);
    }

    [Fact]
    public void Encode_Utf8NoBom_HasNoBom()
    {
        var bytes = TextFileIO.Encode("é", new UTF8Encoding(false), out _);

        Assert.Equal(new byte[] { 0xC3, 0xA9 }, bytes);
    }

    [Fact]
    public void Encode_Utf16LE_WithBom()
    {
        var bytes = TextFileIO.Encode("a", new UnicodeEncoding(false, true), out _);

        Assert.Equal(new byte[] { 0xFF, 0xFE, 0x61, 0x00 }, bytes);
    }

    [Fact]
    public void Encode_Windows1252_CharsInside1252_StaysAndUsesSingleBytes()
    {
        _ = TextFileIO.Decode([]); // memastikan provider code page terdaftar
        var cp1252 = Encoding.GetEncoding(1252);

        var bytes = TextFileIO.Encode("café €", cp1252, out var actual);

        Assert.Equal(new byte[] { 0x63, 0x61, 0x66, 0xE9, 0x20, 0x80 }, bytes);
        Assert.Equal(1252, actual.CodePage);
    }

    [Fact]
    public void Encode_Windows1252_WithCharOutside1252_UpgradesToUtf8WithoutLoss()
    {
        _ = TextFileIO.Decode([]);
        var cp1252 = Encoding.GetEncoding(1252);

        var bytes = TextFileIO.Encode("café 日本", cp1252, out var actual);

        Assert.Equal(65001, actual.CodePage);
        Assert.Empty(actual.GetPreamble());
        Assert.Equal("café 日本", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain((byte)'?', bytes);
    }

    [Fact]
    public void Encode_DoesNotMutateTheEncodingPassedIn()
    {
        _ = TextFileIO.Decode([]);
        var cp1252 = Encoding.GetEncoding(1252);
        var before = cp1252.EncoderFallback;

        TextFileIO.Encode("日本", cp1252, out _);

        Assert.Same(before, cp1252.EncoderFallback);
    }

    [Fact]
    public void Encode_Emoji_ToUtf16_StaysUtf16()
    {
        var bytes = TextFileIO.Encode("😀", new UnicodeEncoding(false, true), out var actual);

        Assert.Equal(1200, actual.CodePage);
        Assert.Equal(new byte[] { 0xFF, 0xFE, 0x3D, 0xD8, 0x00, 0xDE }, bytes);
    }

    [Fact]
    public void Encode_LoneSurrogate_DoesNotThrowAndKeepsOriginalEncoding()
    {
        var bytes = TextFileIO.Encode("a\uD800b", new UTF8Encoding(true), out var actual);

        Assert.Equal(65001, actual.CodePage);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]); // BOM dipertahankan
        Assert.Equal((byte)'a', bytes[3]);
        Assert.Equal((byte)'b', bytes[^1]);
    }

    [Fact]
    public void Encode_LoneSurrogate_In1252_DoesNotThrow()
    {
        _ = TextFileIO.Decode([]);
        var bytes = TextFileIO.Encode("a\uD800b", Encoding.GetEncoding(1252), out var actual);

        Assert.Equal(1252, actual.CodePage);
        Assert.Equal(3, bytes.Length);
    }

    [Fact]
    public void Encode_EmptyText_OnlyPreamble()
    {
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, TextFileIO.Encode("", new UTF8Encoding(true), out _));
        Assert.Empty(TextFileIO.Encode("", new UTF8Encoding(false), out _));
    }
}

public class TextFileIOHashTests
{
    [Fact]
    public void Hash_Empty_IsKnownSha256()
    {
        Assert.Equal("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", TextFileIO.Hash([]));
    }

    [Fact]
    public void Hash_Abc_IsKnownSha256()
    {
        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", TextFileIO.Hash("abc"u8));
    }

    [Fact]
    public void Hash_DiffersWhenSingleByteDiffers_AndIsStableForSameInput()
    {
        Assert.Equal(TextFileIO.Hash("abc"u8), TextFileIO.Hash("abc"u8));
        Assert.NotEqual(TextFileIO.Hash("abc"u8), TextFileIO.Hash("abd"u8));
    }

    [Fact]
    public void Hash_SameTextDifferentBom_Differs()
    {
        Assert.NotEqual(TextFileIO.Hash("a"u8), TextFileIO.Hash(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a' }));
    }
}

public class TextFileIOFileTests : IDisposable
{
    readonly TempDir dir = new();

    public void Dispose() => dir.Dispose();

    [Fact]
    public void Write_NewFile_CreatesFileWithExpectedBytesAndReturnsHashOfThem()
    {
        var path = dir.File("new.md");

        var (enc, hash) = TextFileIO.Write(path, "héllo", new UTF8Encoding(true));

        var onDisk = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, 0x68, 0xC3, 0xA9, 0x6C, 0x6C, 0x6F }, onDisk);
        Assert.Equal(TextFileIO.Hash(onDisk), hash);
        Assert.Equal(65001, enc.CodePage);
    }

    [Fact]
    public void Write_ExistingFile_ReplacesContent()
    {
        var path = dir.WriteText("a.md", "lama yang panjang sekali");

        TextFileIO.Write(path, "baru", new UTF8Encoding(false));

        Assert.Equal("baru", File.ReadAllText(path));
    }

    [Fact]
    public void Write_LeavesNoTemporaryFilesBehind()
    {
        var path = dir.WriteText("a.md", "x");

        TextFileIO.Write(path, "y", new UTF8Encoding(false));
        TextFileIO.Write(dir.File("b.md"), "z", new UTF8Encoding(false));

        Assert.Equal(new[] { "a.md", "b.md" }, dir.Entries());
    }

    [Fact]
    public void Write_ReportsUpgradedEncodingWhenTextDoesNotFit1252()
    {
        _ = TextFileIO.Decode([]);
        var path = dir.File("a.txt");

        var (enc, hash) = TextFileIO.Write(path, "日本", Encoding.GetEncoding(1252));

        Assert.Equal(65001, enc.CodePage);
        Assert.Equal("日本", File.ReadAllText(path, Encoding.UTF8));
        Assert.Equal(TextFileIO.Hash(File.ReadAllBytes(path)), hash);
    }

    [Fact]
    public void Write_EmptyText_CreatesEmptyFile()
    {
        var path = dir.File("empty.md");

        TextFileIO.Write(path, "", new UTF8Encoding(false));

        Assert.Empty(File.ReadAllBytes(path));
    }

    [Fact]
    public void WriteThenRead_RoundTripsTextAndEncoding()
    {
        _ = TextFileIO.Decode([]);
        foreach (var enc in new Encoding[] { new UTF8Encoding(false), new UTF8Encoding(true), new UnicodeEncoding(false, true), new UnicodeEncoding(true, true), Encoding.GetEncoding(1252) })
        {
            var path = dir.File($"rt-{enc.CodePage}-{enc.GetPreamble().Length}.txt");
            const string text = "baris 1\r\nbaris 2 café\n";

            TextFileIO.Write(path, text, enc);
            var (read, readEnc) = TextFileIO.Read(path);

            Assert.Equal(text, read);
            Assert.Equal(EncodingNames.Describe(enc), EncodingNames.Describe(readEnc));
        }
    }

    [Fact]
    public void Write_TargetIsDirectory_ThrowsAndLeavesDirectoryAndNoTempFiles()
    {
        var target = dir.File("folder.md");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "inside.txt"), "keep");

        var ex = Record.Exception(() => TextFileIO.Write(target, "x", new UTF8Encoding(false)));

        Assert.True(ex is IOException or UnauthorizedAccessException, $"tipe: {ex?.GetType()}");
        Assert.True(Directory.Exists(target));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "inside.txt")));
        Assert.Equal(new[] { "folder.md" }, dir.Entries());
    }

    [Fact]
    public void Write_TargetLockedByAnotherProcess_ThrowsKeepsOriginalIntactAndCleansTemp()
    {
        var path = dir.WriteText("locked.md", "ASLI");
        using (var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var ex = Record.Exception(() => TextFileIO.Write(path, "BARU", new UTF8Encoding(false)));

            Assert.True(ex is IOException or UnauthorizedAccessException, $"tipe: {ex?.GetType()}");
        }

        Assert.Equal("ASLI", File.ReadAllText(path));
        Assert.Equal(new[] { "locked.md" }, dir.Entries());
    }

    [Fact]
    public void Write_TargetOpenedForReadWithShareReadWrite_StillSucceeds()
    {
        var path = dir.WriteText("shared.md", "ASLI");
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        TextFileIO.Write(path, "BARU", new UTF8Encoding(false));

        Assert.Equal("BARU", File.ReadAllText(path));
        Assert.Equal(new[] { "shared.md" }, dir.Entries());
    }

    [Fact]
    public void Write_DirectoryDoesNotExist_ThrowsDirectoryNotFoundAndCreatesNothing()
    {
        var path = dir.File(Path.Combine("tidak-ada", "a.md"));

        Assert.Throws<DirectoryNotFoundException>(() => TextFileIO.Write(path, "x", new UTF8Encoding(false)));

        Assert.Empty(dir.Entries());
    }

    [Fact]
    public void Write_ReadOnlyTarget_ThrowsAndKeepsOriginal()
    {
        var path = dir.WriteText("ro.md", "ASLI");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var ex = Record.Exception(() => TextFileIO.Write(path, "BARU", new UTF8Encoding(false)));

            Assert.True(ex is IOException or UnauthorizedAccessException, $"tipe: {ex?.GetType()}");
            Assert.Equal("ASLI", File.ReadAllText(path));
            Assert.Equal(new[] { "ro.md" }, dir.Entries());
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public void WriteBytesAtomic_SymlinkTarget_WritesThroughAndKeepsLink()
    {
        var real = dir.WriteText("real.md", "ASLI");
        var link = dir.File("link.md");
        try { File.CreateSymbolicLink(link, real); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return; // tanpa hak membuat symlink (Developer Mode mati); jalur ini tak bisa diuji di mesin ini
        }

        TextFileIO.WriteBytesAtomic(link, "BARU"u8.ToArray());

        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Equal("BARU", File.ReadAllText(real));
    }

    [Fact]
    public void ReadBytes_FileOpenForWritingByAnotherHandle_StillReadable()
    {
        var path = dir.WriteText("busy.md", "isi");
        using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Equal("isi"u8.ToArray(), TextFileIO.ReadBytes(path));
    }

    [Fact]
    public void ReadBytes_EmptyFile_ReturnsEmptyArray()
    {
        Assert.Empty(TextFileIO.ReadBytes(dir.WriteBytes("e.md", [])));
    }

    [Fact]
    public void ReadBytes_MissingFile_ThrowsFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() => TextFileIO.ReadBytes(dir.File("none.md")));
    }

    [Fact]
    public void ReadBytes_MissingDirectory_ThrowsDirectoryNotFound()
    {
        Assert.Throws<DirectoryNotFoundException>(() => TextFileIO.ReadBytes(dir.File(Path.Combine("x", "none.md"))));
    }

    [Fact]
    public void Read_DetectsEncodingFromDisk()
    {
        var path = dir.WriteBytes("bom.md", [0xEF, 0xBB, 0xBF, 0x68, 0x69]);

        var (text, enc) = TextFileIO.Read(path);

        Assert.Equal("hi", text);
        Assert.Equal("UTF-8 BOM", EncodingNames.Describe(enc));
    }
}
