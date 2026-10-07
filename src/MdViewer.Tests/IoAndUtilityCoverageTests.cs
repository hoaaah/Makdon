using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.Document;
using MdViewer.Tests.Support;

namespace MdViewer.Tests;

/// <summary>Dekode BOM + byte tidak valid, penulisan atomik, ResolveLinkTarget, dan <see cref="FileStamp"/>.</summary>
public class TextFileIOCoverageTests : IDisposable
{
    readonly TempDir dir = new();

    public void Dispose() => dir.Dispose(); // test ACL memulihkan izin sendiri di blok finally

    static readonly UTF8Encoding Utf8 = new(false);

    // ---- Decode(out lossy) untuk tiap BOM ----

    public static TheoryData<string, byte[], int> LossyInputs => new()
    {
        { "UTF-8: byte lanjutan yatim", [0xEF, 0xBB, 0xBF, 0x61, 0xFF, 0x62], 3 },
        { "UTF-8: overlong C0 80", [0xEF, 0xBB, 0xBF, 0xC0, 0x80], 3 },
        { "UTF-8: urutan terpotong", [0xEF, 0xBB, 0xBF, 0x61, 0xE2, 0x82], 3 },
        { "UTF-16 LE: surrogate tinggi yatim", [0xFF, 0xFE, 0x3D, 0xD8], 2 },
        { "UTF-16 LE: surrogate rendah yatim", [0xFF, 0xFE, 0x00, 0xDC, 0x61, 0x00], 2 },
        { "UTF-16 LE: byte sisa ganjil", [0xFF, 0xFE, 0x61, 0x00, 0x62], 2 },
        { "UTF-16 BE: surrogate tinggi yatim", [0xFE, 0xFF, 0xD8, 0x3D], 2 },
        { "UTF-16 BE: byte sisa ganjil", [0xFE, 0xFF, 0x00, 0x61, 0x00], 2 },
        { "UTF-32 LE: titik kode > 10FFFF", [0xFF, 0xFE, 0x00, 0x00, 0x00, 0x00, 0x11, 0x00], 4 },
        { "UTF-32 LE: titik kode surrogate", [0xFF, 0xFE, 0x00, 0x00, 0x00, 0xD8, 0x00, 0x00], 4 },
        { "UTF-32 LE: sisa 1 byte", [0xFF, 0xFE, 0x00, 0x00, 0x61, 0x00, 0x00, 0x00, 0x62], 4 },
        { "UTF-32 BE: titik kode > 10FFFF", [0x00, 0x00, 0xFE, 0xFF, 0x00, 0x11, 0x00, 0x00], 4 },
        { "UTF-32 BE: sisa 3 byte", [0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x00, 0x61, 0x00, 0x00, 0x00], 4 },
    };

    [Theory]
    [MemberData(nameof(LossyInputs))]
    public void Decode_EachBomWithInvalidBody_IsLossy_KeepsTheBomAndSubstitutesReplacementChar(string name, byte[] bytes, int bomLength)
    {
        var (text, encoding) = TextFileIO.Decode(bytes, out var lossy);

        Assert.True(lossy, name);
        Assert.Contains('\uFFFD', text);
        Assert.Equal(bytes[..bomLength], encoding.GetPreamble());
    }

    [Theory]
    [MemberData(nameof(LossyInputs))]
    public void Decode_LossyInput_RoundTripsToAValidFileThatIsNoLongerLossy(string name, byte[] bytes, int bomLength)
    {
        _ = bomLength;
        var (text, encoding) = TextFileIO.Decode(bytes, out _);

        var rewritten = TextFileIO.Encode(text, encoding, out var actual);
        var (text2, _) = TextFileIO.Decode(rewritten, out var lossy2);

        Assert.False(lossy2, name);
        Assert.Equal(text, text2);
        Assert.Equal(encoding.CodePage, actual.CodePage);
    }

    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0xF0, 0x9F, 0x98, 0x80 })]             // UTF-8: emoji
    [InlineData(new byte[] { 0xFF, 0xFE, 0x3D, 0xD8, 0x00, 0xDE })]                   // UTF-16 LE: pasangan surrogate sah
    [InlineData(new byte[] { 0xFE, 0xFF, 0xD8, 0x3D, 0xDE, 0x00 })]                   // UTF-16 BE: pasangan surrogate sah
    [InlineData(new byte[] { 0xFF, 0xFE, 0x00, 0x00, 0x00, 0xF6, 0x01, 0x00 })]       // UTF-32 LE: U+1F600
    [InlineData(new byte[] { 0x00, 0x00, 0xFE, 0xFF, 0x00, 0x01, 0xF6, 0x00 })]       // UTF-32 BE: U+1F600
    [InlineData(new byte[] { 0xFF, 0xFE })]                                           // BOM saja
    public void Decode_EachBomWithValidBody_IsNotLossy_AndNeverContainsReplacementChar(byte[] bytes)
    {
        var (text, _) = TextFileIO.Decode(bytes, out var lossy);

        Assert.False(lossy);
        Assert.DoesNotContain('\uFFFD', text);
    }

    [Fact]
    public void Decode_WithAndWithoutLossyOutParameter_ReturnSameTextAndEncoding()
    {
        byte[] bytes = [0xFF, 0xFE, 0x61, 0x00, 0x62];

        var a = TextFileIO.Decode(bytes);
        var b = TextFileIO.Decode(bytes, out _);

        Assert.Equal(a.Text, b.Text);
        Assert.Equal(a.Encoding.CodePage, b.Encoding.CodePage);
    }

    // ---- WriteBytesAtomic ----

    [Fact]
    public void WriteBytesAtomic_UsesShortTempNameInTargetFolder_AndLeavesNoneBehind()
    {
        var target = dir.File(new string('n', 200) + ".md");
        var created = new ConcurrentBag<string>();
        using var watcher = new FileSystemWatcher(dir.Path) { NotifyFilter = NotifyFilters.FileName };
        watcher.Created += (_, e) => created.Add(e.Name!);
        watcher.EnableRaisingEvents = true;

        TextFileIO.WriteBytesAtomic(target, [1, 2, 3]); // file baru
        TextFileIO.WriteBytesAtomic(target, [4, 5, 6]); // menimpa
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (created.Count(n => n.StartsWith("~md", StringComparison.Ordinal)) < 2 && DateTime.UtcNow < deadline) Thread.Sleep(20);

        var temps = created.Where(n => n.StartsWith("~md", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, temps.Count);
        Assert.All(temps, n => Assert.Matches(@"^~md[0-9a-f]{8}\.tmp$", n));
        Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(target));
        Assert.Equal([Path.GetFileName(target)], dir.Entries());
    }

    [Fact]
    public void WriteBytesAtomic_FailureLeavesNoTempFile_OnTargetThatIsADirectory()
    {
        Directory.CreateDirectory(dir.File("target.md"));

        Assert.ThrowsAny<IOException>(() => TextFileIO.WriteBytesAtomic(dir.File("target.md"), [1]));

        Assert.Equal(["target.md"], dir.Entries());
    }

    static FileSystemAccessRule DenyCreateFiles() => new(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateFiles,
        InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);

    [Fact]
    public void WriteBytesAtomic_FolderNotWritable_ButFileIs_FallsBackToInPlaceWrite()
    {
        var path = dir.WriteText("a.md", "lama dan lebih panjang");
        var info = new DirectoryInfo(dir.Path);
        var rule = DenyCreateFiles();
        var security = info.GetAccessControl();
        security.AddAccessRule(rule);
        info.SetAccessControl(security);
        try
        {
            Assert.ThrowsAny<UnauthorizedAccessException>(() => File.WriteAllText(dir.File("coba.tmp"), "x")); // prasyarat

            var (_, hash) = TextFileIO.Write(path, "baru", Utf8);

            Assert.Equal("baru", File.ReadAllText(path)); // dipotong di akhir: tidak ada sisa isi lama
            Assert.Equal(TextFileIO.Hash("baru"u8), hash);
        }
        finally
        {
            security.RemoveAccessRule(rule);
            info.SetAccessControl(security);
        }
        Assert.Equal(["a.md"], dir.Entries()); // tidak ada ~md*.tmp
    }

    [Fact]
    public void WriteBytesAtomic_InPlaceFallback_WithLongerContent_WritesEverything()
    {
        var path = dir.WriteText("a.md", "pendek");
        var info = new DirectoryInfo(dir.Path);
        var rule = DenyCreateFiles();
        var security = info.GetAccessControl();
        security.AddAccessRule(rule);
        info.SetAccessControl(security);
        try
        {
            TextFileIO.Write(path, "isi baru yang jauh lebih panjang daripada sebelumnya", Utf8);

            Assert.Equal("isi baru yang jauh lebih panjang daripada sebelumnya", File.ReadAllText(path));
        }
        finally
        {
            security.RemoveAccessRule(rule);
            info.SetAccessControl(security);
        }
    }

    [Fact]
    public void WriteBytesAtomic_FolderNotWritable_AndFileMissing_Throws()
    {
        var info = new DirectoryInfo(dir.Path);
        var rule = DenyCreateFiles();
        var security = info.GetAccessControl();
        security.AddAccessRule(rule);
        info.SetAccessControl(security);
        try
        {
            Assert.ThrowsAny<UnauthorizedAccessException>(() => TextFileIO.WriteBytesAtomic(dir.File("baru.md"), [1]));
        }
        finally
        {
            security.RemoveAccessRule(rule);
            info.SetAccessControl(security);
        }
        Assert.Empty(dir.Entries());
    }

    // ---- ResolveLinkTarget ----

    [Fact]
    public void ResolveLinkTarget_RegularMissingAndDirectoryPaths_ComeBackUnchanged()
    {
        var file = dir.WriteText("a.md", "x");

        Assert.Equal(file, TextFileIO.ResolveLinkTarget(file));
        Assert.Equal(dir.File("tidak-ada.md"), TextFileIO.ResolveLinkTarget(dir.File("tidak-ada.md")));
        Assert.Equal(dir.Path, TextFileIO.ResolveLinkTarget(dir.Path));
        Assert.Equal(dir.File(@"tidak\ada\sama\sekali.md"), TextFileIO.ResolveLinkTarget(dir.File(@"tidak\ada\sama\sekali.md")));
    }

    [Fact]
    public void ResolveLinkTarget_FollowsAChainOfSymlinksToTheFinalFile()
    {
        var real = dir.WriteText("asli.md", "x");
        var mid = dir.File("tengah.md");
        var top = dir.File("atas.md");
        try
        {
            File.CreateSymbolicLink(mid, real);
            File.CreateSymbolicLink(top, mid);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return; // tanpa hak membuat symlink (Developer Mode mati); jalur ini tak bisa diuji di mesin ini
        }

        Assert.Equal(real, TextFileIO.ResolveLinkTarget(top), ignoreCase: true);
        Assert.Equal(real, TextFileIO.ResolveLinkTarget(mid), ignoreCase: true);
    }

    [Fact]
    public void ResolveLinkTarget_BrokenSymlink_DoesNotThrow()
    {
        var link = dir.File("patah.md");
        try { File.CreateSymbolicLink(link, dir.File("hilang.md")); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return; }

        Assert.Null(Record.Exception(() => TextFileIO.ResolveLinkTarget(link)));
    }

    [Fact]
    public void ResolveLinkTarget_PathWithIllegalCharacters_IsReturnedAsIs_OrThrowsArgument_NeverCrashesOtherwise()
    {
        var weird = dir.File("a<b>.md");

        var result = Record.Exception(() => TextFileIO.ResolveLinkTarget(weird));

        Assert.True(result is null or ArgumentException, result?.ToString());
    }

    // ---- FileStamp ----

    [Fact]
    public void FileStamp_SameFileAs_ComparesLengthAndWriteTime_ButNotWhenTheStampWasTaken()
    {
        var t = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var a = new FileStamp(10, t, t.AddHours(1));

        Assert.True(a.SameFileAs(new FileStamp(10, t, t.AddDays(3))));
        Assert.False(a.SameFileAs(new FileStamp(11, t, t)));
        Assert.False(a.SameFileAs(new FileStamp(10, t.AddTicks(1), t)));
    }

    [Fact]
    public void FileStamp_IsReliable_OnlyWhenTakenMoreThanTwoSecondsAfterTheWrite()
    {
        var t = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        Assert.False(new FileStamp(1, t, t.AddSeconds(2)).IsReliable);          // tepat 2 dtk: belum andal (batas eksklusif)
        Assert.True(new FileStamp(1, t, t.AddSeconds(2).AddTicks(1)).IsReliable);
        Assert.False(new FileStamp(1, t, t).IsReliable);
        Assert.False(new FileStamp(1, t.AddHours(1), t).IsReliable);            // waktu tulis di masa depan (jam bergeser)
    }

    [Fact]
    public void FileStamp_TryRead_ReportsLengthAndWriteTime_AndTakesTheStampNow()
    {
        var path = dir.WriteBytes("a.md", [1, 2, 3, 4, 5]);
        var written = new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);

        var before = DateTime.UtcNow;
        var stamp = FileStamp.TryRead(path)!.Value;

        Assert.Equal(5, stamp.Length);
        Assert.Equal(written, stamp.LastWriteUtc);
        Assert.InRange(stamp.TakenUtc, before, DateTime.UtcNow);
        Assert.True(stamp.IsReliable);
    }

    [Fact]
    public void FileStamp_ChangesWhenContentLengthChanges()
    {
        var path = dir.WriteBytes("a.md", [1]);
        var first = FileStamp.TryRead(path)!.Value;

        File.WriteAllBytes(path, [1, 2]);

        Assert.False(first.SameFileAs(FileStamp.TryRead(path)!.Value));
    }

    [Fact]
    public void FileStamp_Directory_AndMissingFolder_AreNullNotAnError()
    {
        Assert.Null(FileStamp.TryRead(dir.Path));
        Assert.Null(FileStamp.TryRead(dir.File(@"tidak\ada\a.md")));
    }

    [Fact]
    public void FileStamp_TryReadQuietly_TurnsIllegalPathsIntoNull_WhileTryReadThrows()
    {
        var bad = dir.File("a\0b.md");

        Assert.ThrowsAny<ArgumentException>(() => FileStamp.TryRead(bad));
        Assert.Null(FileStamp.TryReadQuietly(bad));
        Assert.Null(FileStamp.TryReadQuietly(""));
        Assert.Null(FileStamp.TryReadQuietly(dir.File("tidak-ada.md")));
    }

    [Fact]
    public void FileStamp_TryReadQuietly_WorksForFileHeldOpenExclusivelyByAnotherProcessHandle()
    {
        var path = dir.WriteBytes("a.md", [1, 2, 3]);
        using var hold = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var stamp = FileStamp.TryReadQuietly(path);

        Assert.Equal(3, stamp!.Value.Length); // metadata dapat dibaca walau isi terkunci
    }
}

/// <summary><see cref="TextStats.CountSource"/> pada perbatasan potongan 64 KB.</summary>
public class TextStatsChunkBoundaryTests
{
    const int Chunk = 64 * 1024;

    static void AssertSame(string text)
    {
        Assert.Equal(TextStats.Count(text), TextStats.CountSource(new TextDocument(text)));
        Assert.Equal(TextStats.Count(text), TextStats.CountSource(new StringTextSource(text)));
    }

    [Fact]
    public void WordSplitAcrossTheBoundary_IsOneWord()
    {
        var text = new string(' ', Chunk - 3) + "abcdefghij" + " akhir"; // "abcdefghij" terbelah di indeks 65536

        Assert.Equal((2, Chunk - 3 + 10 + 6), TextStats.CountSource(new TextDocument(text)));
        AssertSame(text);
    }

    [Fact]
    public void CrLfSplitAcrossTheBoundary_CountsNeitherCharacter_AndEndsTheWord()
    {
        var text = new string('x', Chunk - 1) + "\r\n" + "yy"; // '\r' di akhir potongan 1, '\n' di awal potongan 2

        Assert.Equal((2, Chunk - 1 + 2), TextStats.CountSource(new TextDocument(text)));
        AssertSame(text);
    }

    [Fact]
    public void LoneCrOrLfExactlyAtTheBoundary_AreNotCounted()
    {
        AssertSame(new string('x', Chunk - 1) + "\r" + "yy");
        AssertSame(new string('x', Chunk - 1) + "\n" + "yy");
        AssertSame(new string('x', Chunk) + "\n" + "yy");
        AssertSame(new string('x', Chunk) + "\r\n");
    }

    [Fact]
    public void SurrogatePairSplitAcrossTheBoundary_CountsTwoUtf16Units_AndStaysInsideItsWord()
    {
        var text = new string(' ', Chunk - 1) + "😀" + "x" + " y"; // pasangan: tinggi di 65535, rendah di 65536

        var (words, characters) = TextStats.CountSource(new TextDocument(text));

        Assert.Equal(2, words);                     // "😀x" dan "y"
        Assert.Equal(Chunk - 1 + 2 + 1 + 2, characters);
        AssertSame(text);
    }

    [Fact]
    public void WhitespaceExactlyOnTheBoundary_SeparatesWords()
    {
        AssertSame(new string('a', Chunk - 1) + " " + "b");
        AssertSame(new string('a', Chunk) + " " + "b");
        AssertSame(new string('a', Chunk - 1) + "\u00A0" + "b"); // NBSP dihitung spasi
        AssertSame(new string('a', Chunk - 1) + "\t" + new string('b', Chunk));
    }

    [Theory]
    [InlineData(Chunk - 1)]
    [InlineData(Chunk)]
    [InlineData(Chunk + 1)]
    [InlineData(2 * Chunk - 1)]
    [InlineData(2 * Chunk)]
    [InlineData(2 * Chunk + 1)]
    [InlineData(3 * Chunk + 7)]
    public void RepeatedMixedContent_AroundEveryMultipleOfTheChunkSize_MatchesTheStringCount(int length)
    {
        var unit = "kata dua\r\nsatu  😀 tab\tlagi\n";
        var text = string.Concat(Enumerable.Repeat(unit, length / unit.Length + 1))[..length];

        AssertSame(text);
    }

    [Theory]
    [InlineData(Chunk)]
    [InlineData(Chunk + 1)]
    [InlineData(2 * Chunk + 1)]
    public void SingleLongWord_IsOneWord_AtAnyLength(int length)
    {
        Assert.Equal((1, length), TextStats.CountSource(new TextDocument(new string('k', length))));
    }

    [Fact]
    public void EmptySource_IsZero()
    {
        Assert.Equal((0, 0), TextStats.CountSource(new TextDocument()));
        Assert.Equal((0, 0), TextStats.CountSource(new StringTextSource("")));
    }

    [Fact]
    public void LargeDocument_IsReadInChunksOfAtMost64K_NeverAsOneWholeString()
    {
        var spy = new SpySource(new string('a', 5 * Chunk + 123));

        var result = TextStats.CountSource(spy);

        Assert.Equal((1, 5 * Chunk + 123), result);
        Assert.False(spy.TextRead, "Seluruh teks disalin lewat ITextSource.Text.");
        Assert.NotEmpty(spy.RequestedLengths);
        Assert.All(spy.RequestedLengths, n => Assert.InRange(n, 1, Chunk));
        Assert.Equal(5 * Chunk + 123, spy.RequestedLengths.Sum());
    }

    sealed class SpySource(string text) : ITextSource
    {
        readonly StringTextSource inner = new(text);
        public bool TextRead { get; private set; }
        public List<int> RequestedLengths { get; } = [];

        public ITextSourceVersion Version => inner.Version;
        public int TextLength => inner.TextLength;
        public string Text { get { TextRead = true; return inner.Text; } }
        public ITextSource CreateSnapshot() => inner.CreateSnapshot();
        public ITextSource CreateSnapshot(int offset, int length) => inner.CreateSnapshot(offset, length);
        public TextReader CreateReader() { TextRead = true; return inner.CreateReader(); }
        public TextReader CreateReader(int offset, int length) => inner.CreateReader(offset, length);
        public char GetCharAt(int offset) => inner.GetCharAt(offset);
        public string GetText(int offset, int length) { RequestedLengths.Add(length); return inner.GetText(offset, length); }
        public string GetText(ISegment segment) => GetText(segment.Offset, segment.Length);
        public void WriteTextTo(TextWriter writer) { TextRead = true; inner.WriteTextTo(writer); }
        public void WriteTextTo(TextWriter writer, int offset, int length) => inner.WriteTextTo(writer, offset, length);
        public int IndexOf(char c, int startIndex, int count) => inner.IndexOf(c, startIndex, count);
        public int IndexOfAny(char[] anyOf, int startIndex, int count) => inner.IndexOfAny(anyOf, startIndex, count);
        public int IndexOf(string searchText, int startIndex, int count, StringComparison comparisonType) =>
            inner.IndexOf(searchText, startIndex, count, comparisonType);
        public int LastIndexOf(char c, int startIndex, int count) => inner.LastIndexOf(c, startIndex, count);
        public int LastIndexOf(string searchText, int startIndex, int count, StringComparison comparisonType) =>
            inner.LastIndexOf(searchText, startIndex, count, comparisonType);
    }
}

/// <summary><see cref="CrashLog"/>: klasifikasi galat dan batas ukuran 512 KB.</summary>
public class CrashLogLimitsTests : IDisposable
{
    const long MaxBytes = 512 * 1024;
    readonly TempDir dir = new();

    public void Dispose() => dir.Dispose();

    // ---- IsRecoverable ----

    public static TheoryData<string, bool> Classification => new()
    {
        { nameof(FileNotFoundException), true },
        { nameof(DirectoryNotFoundException), true },
        { nameof(PathTooLongException), true },
        { nameof(EndOfStreamException), true },
        { nameof(ExternalChangeException), true },
        { nameof(PlatformNotSupportedException), true },
        { nameof(UriFormatException), true },
        { nameof(RegexMatchTimeoutException), true },
        { nameof(COMException), false },                   // HRESULT tak dikenal (E_FAIL): keadaan tak bisa dipercaya
        { nameof(InsufficientMemoryException), false },   // turunan OutOfMemoryException
        { nameof(StackOverflowException), false },
        { nameof(BadImageFormatException), false },
        { nameof(TypeInitializationException), false },
        { nameof(AppDomainUnloadedException), false },
        { nameof(ArgumentException), false },
        { nameof(ArgumentNullException), false },
        { nameof(ObjectDisposedException), false },
        { nameof(TimeoutException), false },
        { nameof(OperationCanceledException), false },
        { nameof(InvalidDataException), false },
        { nameof(NotImplementedException), false },
        { nameof(IndexOutOfRangeException), false },
        { nameof(AggregateException), false },
        { nameof(Exception), false },
    };

    static Exception Make(string name) => name switch
    {
        nameof(FileNotFoundException) => new FileNotFoundException("x"),
        nameof(DirectoryNotFoundException) => new DirectoryNotFoundException("x"),
        nameof(PathTooLongException) => new PathTooLongException("x"),
        nameof(EndOfStreamException) => new EndOfStreamException("x"),
        nameof(ExternalChangeException) => new ExternalChangeException("x"),
        nameof(PlatformNotSupportedException) => new PlatformNotSupportedException("x"),
        nameof(UriFormatException) => new UriFormatException("x"),
        nameof(RegexMatchTimeoutException) => new RegexMatchTimeoutException("x"),
        nameof(COMException) => new COMException("x"),
        nameof(InsufficientMemoryException) => new InsufficientMemoryException("x"),
        nameof(StackOverflowException) => new StackOverflowException("x"),
        nameof(BadImageFormatException) => new BadImageFormatException("x"),
        nameof(TypeInitializationException) => new TypeInitializationException("T", new IOException("dalam")),
        nameof(AppDomainUnloadedException) => new AppDomainUnloadedException("x"),
        nameof(ArgumentException) => new ArgumentException("x"),
        nameof(ArgumentNullException) => new ArgumentNullException("x"),
        nameof(ObjectDisposedException) => new ObjectDisposedException("x"),
        nameof(TimeoutException) => new TimeoutException("x"),
        nameof(OperationCanceledException) => new OperationCanceledException("x"),
        nameof(InvalidDataException) => new InvalidDataException("x"),
        nameof(NotImplementedException) => new NotImplementedException("x"),
        nameof(IndexOutOfRangeException) => new IndexOutOfRangeException("x"),
        nameof(AggregateException) => new AggregateException(new IOException("dalam")),
        _ => new Exception("x"),
    };

    [Theory]
    [MemberData(nameof(Classification))]
    public void IsRecoverable_DerivedAndRelatedTypes(string name, bool expected)
    {
        Assert.Equal(expected, CrashLog.IsRecoverable(Make(name)));
    }

    [Fact]
    public void IsRecoverable_Win32ExceptionWithErrorCode_AndWrappedRecoverableException_AreHandledByOuterType()
    {
        Assert.True(CrashLog.IsRecoverable(new Win32Exception(5)));
        Assert.False(CrashLog.IsRecoverable(new InvalidOperationException("luar", new IOException("dalam"))));
        Assert.True(CrashLog.IsRecoverable(new IOException("luar", new InvalidOperationException("dalam"))));
    }

    // ---- Write: batas 512 KB ----

    string LogFile(string name = "crash.log") => dir.File(name);

    static void FillLog(string path, long bytes) => File.WriteAllBytes(path, Enumerable.Repeat((byte)'X', (int)bytes).ToArray());

    [Fact]
    public void Write_LogExactlyAtTheLimit_IsAppendedTo_NotDiscarded()
    {
        var log = LogFile();
        FillLog(log, MaxBytes);

        CrashLog.Write("konteks-baru", new IOException("pesan"), log);

        var text = File.ReadAllText(log);
        Assert.StartsWith(new string('X', 100), text);
        Assert.Contains("konteks-baru", text);
        Assert.True(new FileInfo(log).Length > MaxBytes);
    }

    [Fact]
    public void Write_LogOneByteOverTheLimit_IsDiscarded_AndOnlyTheNewEntryRemains()
    {
        var log = LogFile();
        FillLog(log, MaxBytes + 1);

        CrashLog.Write("konteks-baru", new IOException("pesan"), log);

        var text = File.ReadAllText(log);
        Assert.DoesNotContain("XXXX", text);
        Assert.Contains("konteks-baru", text);
        Assert.Contains("pesan", text);
        Assert.True(new FileInfo(log).Length < 16 * 1024);
    }

    [Fact]
    public void Write_RepeatedlyOverTheLimit_NeverGrowsWithoutBound()
    {
        var log = LogFile();
        var bigMessage = new string('m', 100_000);

        for (var i = 0; i < 20; i++) CrashLog.Write("ctx" + i, new IOException(bigMessage), log);

        Assert.True(new FileInfo(log).Length <= MaxBytes + 2 * 100_000 + 4096);
        Assert.Contains("ctx19", File.ReadAllText(log)); // entri terbaru selalu ada
    }

    [Fact]
    public void Write_OversizedLogThatCannotBeDeleted_DoesNotThrow_AndLeavesItIntact()
    {
        var log = LogFile();
        FillLog(log, MaxBytes + 10);
        using (new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var ex = Record.Exception(() => CrashLog.Write("x", new IOException("y"), log));

            Assert.Null(ex);
        }
        Assert.Equal(MaxBytes + 10, new FileInfo(log).Length);
    }

    [Fact]
    public void Write_LogPathIsADirectory_DoesNotThrow()
    {
        Directory.CreateDirectory(LogFile("sebuah-folder"));

        Assert.Null(Record.Exception(() => CrashLog.Write("x", new IOException("y"), LogFile("sebuah-folder"))));
    }

    [Fact]
    public void Write_ConcurrentWriters_LoseNoEntriesAndNeverThrow()
    {
        var log = LogFile();
        var failures = new ConcurrentBag<Exception>();

        Parallel.For(0, 200, i =>
        {
            try { CrashLog.Write($"ctx-{i:D3}-akhir", new IOException($"pesan-{i:D3}"), log); }
            catch (Exception ex) { failures.Add(ex); }
        });

        Assert.Empty(failures);
        var text = File.ReadAllText(log);
        for (var i = 0; i < 200; i++) Assert.Contains($"ctx-{i:D3}-akhir", text);
    }

    [Fact]
    public void Write_RecordsContextExceptionTypeAndTimestamp_InAReadableBlock()
    {
        var log = LogFile();

        CrashLog.Write("saat menyimpan", new UnauthorizedAccessException("ditolak"), log);

        var text = File.ReadAllText(log);
        // Pemisah jam mengikuti budaya pengguna (id-ID memakai "."), jadi terima ":" maupun ".".
        Assert.Matches(@"^\[\d{4}-\d{2}-\d{2} \d{2}[:.]\d{2}[:.]\d{2} [+-]\d{2}[:.]\d{2}\] saat menyimpan", text);
        Assert.Contains("UnauthorizedAccessException", text);
        Assert.Contains("ditolak", text);
    }

    [Fact]
    public void Write_WithoutExplicitPath_UsesTheRedirectedLogPath_NeverTheUsersRealLog()
    {
        var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MdViewer", "crash.log");
        var before = File.Exists(real) ? new FileInfo(real).Length : -1;

        CrashLog.Write("uji-pengalihan", new IOException("x"));

        Assert.NotEqual(real, CrashLog.LogPath);
        Assert.Contains("uji-pengalihan", File.ReadAllText(CrashLog.LogPath));
        Assert.Equal(before, File.Exists(real) ? new FileInfo(real).Length : -1);
    }
}

/// <summary>Gabung (SaveMerged) dan pemulihan pengaturan; melengkapi <see cref="AppSettingsMergeTests"/>.</summary>
public class AppSettingsMergeEdgeTests : IDisposable
{
    readonly TempDir dir = new();

    public void Dispose() => dir.Dispose();

    string SettingsPath => dir.File("settings.json");

    [Fact]
    public void SaveMerged_StoredEntriesDuplicateOwnIgnoringCase_AreNotAddedTwice()
    {
        new AppSettings { RecentFiles = [@"C:\Dok\A.md", @"C:\b.md"] }.Save(SettingsPath);
        var mine = new AppSettings { RecentFiles = [@"c:\dok\a.md"] };

        mine.SaveMerged(SettingsPath);

        Assert.Equal([@"c:\dok\a.md", @"C:\b.md"], AppSettings.Load(SettingsPath).RecentFiles);
    }

    [Fact]
    public void SaveMerged_RemovedEntry_IsMatchedIgnoringCase()
    {
        new AppSettings { RecentFiles = [@"C:\Hapus.md", @"C:\tetap.md"] }.Save(SettingsPath);
        var mine = new AppSettings();
        mine.RemoveRecent(@"c:\hapus.MD");

        mine.SaveMerged(SettingsPath);

        Assert.Equal([@"C:\tetap.md"], AppSettings.Load(SettingsPath).RecentFiles);
    }

    [Fact]
    public void SaveMerged_RemoveThenAddAgain_ComesBackAtTheTop_WithoutDuplicates()
    {
        new AppSettings { RecentFiles = [@"C:\x.md", @"C:\y.md"] }.Save(SettingsPath);
        var mine = new AppSettings();
        mine.RemoveRecent(@"C:\x.md");
        mine.AddRecent(@"C:\x.md");

        mine.SaveMerged(SettingsPath);

        Assert.Equal([@"C:\x.md", @"C:\y.md"], AppSettings.Load(SettingsPath).RecentFiles);
    }

    [Fact]
    public void SaveMerged_OwnEntriesWinTheCap_StoredOnesFillTheRemainingSlots()
    {
        new AppSettings { RecentFiles = Enumerable.Range(0, 10).Select(i => $@"C:\disk{i}.md").ToList() }.Save(SettingsPath);
        var mine = new AppSettings { RecentFiles = Enumerable.Range(0, 8).Select(i => $@"C:\saya{i}.md").ToList() };

        mine.SaveMerged(SettingsPath);

        var saved = AppSettings.Load(SettingsPath).RecentFiles;
        Assert.Equal(10, saved.Count);
        Assert.Equal(Enumerable.Range(0, 8).Select(i => $@"C:\saya{i}.md"), saved.Take(8));
        Assert.Equal([@"C:\disk0.md", @"C:\disk1.md"], saved.Skip(8));
    }

    [Fact]
    public void SaveMerged_UpdatesTheInMemoryListToTheMergedResult()
    {
        new AppSettings { RecentFiles = [@"C:\disk.md"] }.Save(SettingsPath);
        var mine = new AppSettings { RecentFiles = [@"C:\saya.md"] };

        mine.SaveMerged(SettingsPath);

        Assert.Equal([@"C:\saya.md", @"C:\disk.md"], mine.RecentFiles);
    }

    [Fact]
    public void SaveMerged_CorruptStoredFile_IsReplacedByOwnSettings()
    {
        File.WriteAllText(SettingsPath, "{ bukan json");
        var mine = new AppSettings { RecentFiles = [@"C:\saya.md"], Theme = "Dark" };

        Assert.True(mine.SaveMerged(SettingsPath));

        var loaded = AppSettings.Load(SettingsPath);
        Assert.Equal([@"C:\saya.md"], loaded.RecentFiles);
        Assert.Equal(nameof(AppThemeMode.Dark), loaded.Theme);
    }

    [Fact]
    public void SaveMerged_StoredFileWithJunkEntries_ContributesOnlySaneOnes()
    {
        File.WriteAllText(SettingsPath, """{ "RecentFiles": ["", "  ", null, "C:\\ok.md", "c:\\OK.md"] }""");
        var mine = new AppSettings();

        mine.SaveMerged(SettingsPath);

        Assert.Equal([@"C:\ok.md"], AppSettings.Load(SettingsPath).RecentFiles);
    }

    [Fact]
    public void SaveMerged_OnlyMergesRecentFiles_OtherFieldsComeFromThisInstance()
    {
        new AppSettings { Theme = "Dark", ZoomPercent = 150, BlockRemoteImages = false }.Save(SettingsPath);
        var mine = new AppSettings { Theme = "Light", ZoomPercent = 80, BlockRemoteImages = true };

        mine.SaveMerged(SettingsPath);

        var loaded = AppSettings.Load(SettingsPath);
        Assert.Equal("Light", loaded.Theme);
        Assert.Equal(80, loaded.ZoomPercent);
        Assert.True(loaded.BlockRemoteImages);
    }

    [Fact]
    public void SaveMerged_KeepStoredSession_WithNoStoredSession_WritesNoSession()
    {
        new AppSettings().Save(SettingsPath);
        var mine = new AppSettings { Session = new SessionState { Tabs = [new SessionTab { Path = @"C:\lain.md" }] } };

        mine.SaveMerged(SettingsPath, keepStoredSession: true);

        Assert.Null(AppSettings.Load(SettingsPath).Session);
    }

    [Fact]
    public void SaveMerged_KeepStoredSession_StillMergesRecentFilesAndTakesOwnTheme()
    {
        new AppSettings { RecentFiles = [@"C:\disk.md"], Session = new SessionState { Tabs = [new SessionTab { Path = @"C:\sesi.md" }] } }
            .Save(SettingsPath);
        var mine = new AppSettings { RecentFiles = [@"C:\arg.md"], Theme = "Dark" };

        mine.SaveMerged(SettingsPath, keepStoredSession: true);

        var loaded = AppSettings.Load(SettingsPath);
        Assert.Equal([@"C:\arg.md", @"C:\disk.md"], loaded.RecentFiles);
        Assert.Equal(@"C:\sesi.md", loaded.Session!.Tabs.Single().Path);
        Assert.Equal("Dark", loaded.Theme);
    }

    [Fact]
    public void SaveMerged_WithoutKeepStoredSession_AndNoOwnSession_RemovesTheStoredSession()
    {
        new AppSettings { Session = new SessionState { Tabs = [new SessionTab { Path = @"C:\sesi.md" }] } }.Save(SettingsPath);

        new AppSettings().SaveMerged(SettingsPath);

        Assert.Null(AppSettings.Load(SettingsPath).Session);
    }

    [Fact]
    public void SaveMerged_TargetIsADirectory_ReturnsFalseWithoutThrowing()
    {
        Directory.CreateDirectory(dir.File("settings.json"));

        Assert.False(new AppSettings { RecentFiles = [@"C:\a.md"] }.SaveMerged(SettingsPath));
    }

    [Fact]
    public void SaveMerged_CreatesMissingDirectory()
    {
        var nested = dir.File(@"a\b\settings.json");

        Assert.True(new AppSettings { RecentFiles = [@"C:\a.md"] }.SaveMerged(nested));
        Assert.Equal([@"C:\a.md"], AppSettings.Load(nested).RecentFiles);
    }

    [Fact]
    public void SaveMerged_TwoInstancesInTurn_BothEntriesSurvive()
    {
        var first = new AppSettings();
        var second = new AppSettings();
        first.AddRecent(@"C:\dari-pertama.md");
        second.AddRecent(@"C:\dari-kedua.md");

        first.SaveMerged(SettingsPath);
        second.SaveMerged(SettingsPath);

        Assert.Equal([@"C:\dari-kedua.md", @"C:\dari-pertama.md"], AppSettings.Load(SettingsPath).RecentFiles);
    }

    // ---- BlockRemoteImages ----

    [Theory]
    [InlineData("{}", true)]
    [InlineData("""{ "BlockRemoteImages": false }""", false)]
    [InlineData("""{ "blockremoteimages": false }""", false)]
    [InlineData("""{ "BlockRemoteImages": true }""", true)]
    [InlineData("""{ "BlockRemoteImages": "ya" }""", true)]   // tipe salah: seluruh file jatuh ke bawaan (true)
    [InlineData("""{ "BlockRemoteImages": null }""", true)]   // null untuk bool non-nullable: bawaan
    public void BlockRemoteImages_LoadFromJson(string json, bool expected)
    {
        File.WriteAllText(SettingsPath, json);

        Assert.Equal(expected, AppSettings.Load(SettingsPath).BlockRemoteImages);
    }

    [Fact]
    public void BlockRemoteImages_FalseIsWrittenExplicitly_AndSurvivesSaveMerged()
    {
        new AppSettings { BlockRemoteImages = false }.SaveMerged(SettingsPath);

        Assert.Contains("\"BlockRemoteImages\": false", File.ReadAllText(SettingsPath));
        var again = AppSettings.Load(SettingsPath);
        again.SaveMerged(SettingsPath);
        Assert.False(AppSettings.Load(SettingsPath).BlockRemoteImages);
    }
}

/// <summary><see cref="SearchEngine.NormalizeLineEndings"/> kasus tepi dan batas waktu total.</summary>
public class SearchEngineEdgeCaseTests
{
    static readonly SearchOptions Rx = new(UseRegex: true);

    [Theory]
    [InlineData("(?s).$", @"(?s).(?=\r?$)")]
    [InlineData("(?is)a.b$", @"(?is)a.b(?=\r?$)")]
    [InlineData("(?i).$", @"(?i)[^\r\n](?=\r?$)")]
    [InlineData("(?i:a.)$", @"(?i:a[^\r\n])(?=\r?$)")]
    [InlineData("(?<n>.)$", @"(?<n>[^\r\n])(?=\r?$)")]
    [InlineData("(?<=a$).", @"(?<=a(?=\r?$))[^\r\n]")]
    [InlineData("(?'n'.)", @"(?'n'[^\r\n])")]
    [InlineData("(?x)a . $ # c", "(?x)a . $ # c")]
    [InlineData("(?xi)a . $", "(?xi)a . $")]
    [InlineData("(?i-x)a.$", @"(?i-x)a[^\r\n](?=\r?$)")]
    [InlineData("[^$]", "[^$]")]
    [InlineData("a[^$]b", "a[^$]b")]
    [InlineData("[^$.]x", "[^$.]x")]
    [InlineData(@"[\]$]", @"[\]$]")]
    [InlineData(@"[\]$].", @"[\]$][^\r\n]")]
    [InlineData(@"\\$", @"\\(?=\r?$)")]
    [InlineData(@"\$$", @"\$(?=\r?$)")]
    [InlineData(@"\.", @"\.")]
    [InlineData(@"\p{L}.", @"\p{L}[^\r\n]")]
    [InlineData("a{2}.", @"a{2}[^\r\n]")]
    [InlineData(".*?$", @"[^\r\n]*?(?=\r?$)")]
    [InlineData("^$", @"^(?=\r?$)")]
    [InlineData("$", @"(?=\r?$)")]
    [InlineData(".", @"[^\r\n]")]
    [InlineData("", "")]
    [InlineData(@"abc\", @"abc\")]          // escape berakhir di akhir pola: tidak melempar
    [InlineData(@"$\", @"(?=\r?$)\")]
    [InlineData(@".\", @"[^\r\n]\")]
    [InlineData("[", "[")]                  // kelas tidak tertutup: tidak melempar
    [InlineData("[$", "[$")]
    [InlineData("(?#tak.tertutup$", "(?#tak.tertutup$")]
    [InlineData("(?", "(?")]
    [InlineData("(?-", "(?-")]
    public void NormalizeLineEndings_EdgeCases(string pattern, string expected)
    {
        Assert.Equal(expected, SearchEngine.NormalizeLineEndings(pattern));
    }

    [Fact]
    public void FindAll_PatternEndingWithBackslash_IsAnInvalidPattern_NotACrash()
    {
        Assert.ThrowsAny<ArgumentException>(() => SearchEngine.FindAll("abc", @"abc\", Rx));
        Assert.False(SearchEngine.TryFindAll("abc", @"abc\", Rx, out var matches, out var error));
        Assert.Empty(matches);
        Assert.Equal("Regex tidak valid", error);
    }

    [Fact]
    public void FindAll_EscapedBackslashBeforeDollar_StillAnchorsAtLineEnd_OnCrLf()
    {
        var text = "a\\\r\nb\\\r\n"; // a\ CRLF b\ CRLF

        var found = SearchEngine.FindAll(text, @"\w\\$", Rx);

        Assert.Equal([0, 4], found.Select(m => m.Offset));
        Assert.All(found, m => Assert.Equal(2, m.Length));
    }

    [Fact]
    public void FindAll_EscapedDollarThenAnchor_MatchesTheDollarSign()
    {
        var found = SearchEngine.FindAll("harga $5\r\nlain\r\n", @"\$\d$", Rx);

        Assert.Equal([(6, 2)], found.Select(m => (m.Offset, m.Length)));
    }

    [Fact]
    public void FindAll_DotAllFlag_MatchesAcrossCrLf_AndDollarStillAnchorsBeforeCr()
    {
        var found = SearchEngine.FindAll("a\r\nb\r\nc", "(?s)a.+b$", Rx);

        Assert.Equal([(0, 4)], found.Select(m => (m.Offset, m.Length)));
    }

    [Fact]
    public void FindAll_CharacterClassWithDollar_DoesNotAnchor()
    {
        var found = SearchEngine.FindAll("a$b\r\n", "a[$]b", Rx);

        Assert.Equal([(0, 3)], found.Select(m => (m.Offset, m.Length)));
    }

    [Fact]
    public void FindAll_VerbosePattern_IsPassedThroughUnchanged_AndStillMatches()
    {
        var found = SearchEngine.FindAll("ab\r\ncd", "(?x) a b  # komentar", Rx);

        Assert.Equal([(0, 2)], found.Select(m => (m.Offset, m.Length)));
    }

    [Fact]
    public void NormalizeLineEndings_SingleLineFlag_ScopedOrTurnedOff_DoesNotLeakIntoLaterDots()
    {
        Assert.Equal(@"(?s:a.)b[^\r\n]", SearchEngine.NormalizeLineEndings("(?s:a.)b."));
        Assert.Equal(@"(?s)a.(?-s)b[^\r\n]", SearchEngine.NormalizeLineEndings("(?s)a.(?-s)b."));
        Assert.Empty(SearchEngine.FindAll("xa\nb\r\n", "(?s:a.)b.", Rx)); // 'b' diikuti '\r': '.' tidak boleh menangkapnya
    }

    [Fact]
    public void NormalizeLineEndings_LiteralOpenBracketInsideClass_DoesNotSwallowTheFollowingAnchor()
    {
        Assert.Equal(@"[[](?=\r?$)", SearchEngine.NormalizeLineEndings("[[]$"));
        Assert.Single(SearchEngine.FindAll("a[\r\nb", "[[]$", Rx));
    }

    // Hanya "-[" (subtraksi) yang membuka kelas bersarang; "[" lain di dalam kelas adalah literal.
    [Theory]
    [InlineData("[a-z-[aeiou]]$", @"[a-z-[aeiou]](?=\r?$)")]
    [InlineData("[a-z-[aeiou-[e]]].", @"[a-z-[aeiou-[e]]][^\r\n]")]
    [InlineData("[a-z-[$]]$", @"[a-z-[$]](?=\r?$)")]
    [InlineData(@"[\w-[x]]$", @"[\w-[x]](?=\r?$)")]
    [InlineData(@"[a\-[]$", @"[a\-[](?=\r?$)")]   // "-" di-escape: bukan subtraksi
    [InlineData("[-[]$", "[-[](?=\\r?$)")]          // "-" adalah karakter pertama: bukan subtraksi
    [InlineData("[^-[]$", "[^-[](?=\\r?$)")]
    [InlineData("[[][[]$", @"[[][[](?=\r?$)")]
    [InlineData("[[.]$", @"[[.](?=\r?$)")]
    public void NormalizeLineEndings_CharacterClassNesting_OnlyDashBracketOpensANestedClass(string pattern, string expected)
    {
        Assert.Equal(expected, SearchEngine.NormalizeLineEndings(pattern));
    }

    // Flag s dibatasi grupnya: (?s) di dalam grup berhenti di ")", (?-s) mematikannya, dan grup bersarang mewarisinya.
    [Theory]
    [InlineData("(a(?s).)b.", @"(a(?s).)b[^\r\n]")]
    [InlineData("(?s)(?-s:.)", @"(?s)(?-s:[^\r\n])")]
    [InlineData("(?s)(.).", "(?s)(.).")]
    [InlineData("(?s:(.).)(.)", @"(?s:(.).)([^\r\n])")]
    [InlineData("(?s)a(?-s).(?s).", @"(?s)a(?-s)[^\r\n](?s).")]
    [InlineData("(?-s).", @"(?-s)[^\r\n]")]
    public void NormalizeLineEndings_SingleLineFlag_FollowsGroupScope(string pattern, string expected)
    {
        Assert.Equal(expected, SearchEngine.NormalizeLineEndings(pattern));
    }

    [Fact]
    public void FindAll_LiteralOpenBracketInsideClass_AnchorsStillMatchBeforeCrLf()
    {
        Assert.Equal(2, SearchEngine.FindAll("a[\r\nb]\r\n", "[[]$|[]]$", Rx).Count);
        Assert.Equal([(5, 1)], SearchEngine.FindAll("ab\r\ncd", "[a-z-[a-c]]$", Rx).Select(m => (m.Offset, m.Length))); // "b" dikecualikan: hanya "d"
    }

    // ---- Batas waktu total ----

    [Fact]
    public void FindAll_TotalDeadline_AppliesEvenWhenEveryMatchIsEmpty()
    {
        var text = new string('a', 100_000);

        Assert.Throws<RegexMatchTimeoutException>(() =>
            SearchEngine.FindAll(text, "(?=a)", Rx, null, SearchEngine.MaxResults, TimeSpan.Zero));
    }

    [Fact]
    public void FindAll_TotalDeadline_DoesNotApplyToLiteralSearch()
    {
        var text = new string('a', 100_000);

        var found = SearchEngine.FindAll(text, "a", new SearchOptions(), null, int.MaxValue, TimeSpan.Zero);

        Assert.Equal(100_000, found.Count);
    }

    [Fact]
    public void FindAll_TotalDeadline_IsCheckedOnEveryIteration_NotEverySixtyFourth()
    {
        var text = new string('a', 100_000);

        // Batas nol sudah lewat sebelum hasil pertama; dulu baru terdeteksi di iterasi ke-64 sehingga maxResults 10 lolos.
        Assert.Throws<RegexMatchTimeoutException>(() =>
            SearchEngine.FindAll(text, "a", Rx, null, maxResults: 10, totalTimeout: TimeSpan.Zero));
    }

    [Fact]
    public void FindAll_ReachingMaxResultsWithinTheDeadline_ReturnsNormally()
    {
        var text = new string('a', 100_000);

        var found = SearchEngine.FindAll(text, "a", Rx, null, maxResults: 10, totalTimeout: TimeSpan.FromSeconds(30));

        Assert.Equal(10, found.Count);
    }

    [Fact]
    public void FindAll_GenerousTotalDeadline_DoesNotInterfereWithNormalSearches()
    {
        var text = string.Concat(Enumerable.Repeat("kata\r\n", 5_000));

        var found = SearchEngine.FindAll(text, "kata$", Rx, null, int.MaxValue, TimeSpan.FromSeconds(30));

        Assert.Equal(5_000, found.Count);
    }

    [Fact]
    public void FindAll_TotalDeadline_AlsoCoversReplacementExpansion()
    {
        var text = string.Concat(Enumerable.Repeat("ab ", 100_000));

        Assert.Throws<RegexMatchTimeoutException>(() =>
            SearchEngine.FindAll(text, "(a)(b)", Rx, "$2$1", int.MaxValue, TimeSpan.Zero));
    }
}
