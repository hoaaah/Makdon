using System.Text;
using System.Text.RegularExpressions;
using Makdon.Tests.Support;

namespace Makdon.Tests;

/// <summary>
/// DocumentTab membuat DocumentView (UserControl + AvalonEdit), jadi semua kode di sini dijalankan di thread STA
/// milik <see cref="WpfHost"/>. Deteksi perubahan eksternal diuji lewat CheckExternalChange() langsung
/// (bukan lewat FileSystemWatcher + timer 400 ms) supaya deterministik.
/// </summary>
[Collection("Wpf")]
public class DocumentTabTests : IDisposable
{
    static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    readonly TempDir dir = new();
    readonly List<DocumentTab> tabs = [];

    public void Dispose()
    {
        WpfHost.Instance.Run(() => { foreach (var t in tabs) t.Dispose(); });
        tabs.Clear();
        // BitmapImage di pratinjau menahan handle file gambar sampai di-GC; lepas dulu agar folder temp bisa dihapus.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        dir.Dispose();
    }

    static void Sta(Action action) => WpfHost.Instance.Run(action);

    DocumentTab Load(string path)
    {
        var tab = DocumentTab.Load(path);
        tabs.Add(tab);
        return tab;
    }

    DocumentTab Untitled()
    {
        var tab = DocumentTab.CreateUntitled();
        tabs.Add(tab);
        return tab;
    }

    static void Edit(DocumentTab tab, string insert = "X") => tab.Document.Insert(0, insert);

    // ---- CreateUntitled ----

    [Fact]
    public void CreateUntitled_StartsEmptyCleanAndUnnamed()
    {
        Sta(() =>
        {
            var tab = Untitled();

            Assert.Null(tab.FilePath);
            Assert.Equal("", tab.Document.Text);
            Assert.False(tab.IsDirty);
            Assert.Matches(@"^Tanpa Judul-\d+$", tab.Title);
            Assert.Equal(tab.Title, tab.DisplayTitle);
            Assert.Equal($"{tab.Title} — Makdon", tab.WindowTitle);
            Assert.Equal("Belum disimpan", tab.PathText);
            Assert.Equal("UTF-8", tab.EncodingLabel);
            Assert.Equal(ViewMode.Split, tab.Mode);
            Assert.NotNull(tab.View);
        });
    }

    [Fact]
    public void CreateUntitled_EachTabGetsItsOwnIncreasingNumber()
    {
        Sta(() =>
        {
            var a = Untitled();
            var b = Untitled();

            var na = int.Parse(Regex.Match(a.Title, @"\d+$").Value);
            var nb = int.Parse(Regex.Match(b.Title, @"\d+$").Value);
            Assert.Equal(na + 1, nb);
            Assert.NotEqual(a.Title, b.Title);
        });
    }

    [Fact]
    public void Untitled_TypingMakesItDirty_AndShowsBullet()
    {
        Sta(() =>
        {
            var tab = Untitled();

            Edit(tab, "halo");

            Assert.True(tab.IsDirty);
            Assert.StartsWith("● ", tab.DisplayTitle);
            Assert.StartsWith("● ", tab.WindowTitle);
        });
    }

    // ---- Load ----

    [Fact]
    public void Load_ReadsTextTitlePathAndEncoding()
    {
        var path = dir.WriteText("catatan.md", "# Judul\r\nisi");

        Sta(() =>
        {
            var tab = Load(path);

            Assert.Equal("# Judul\r\nisi", tab.Document.Text);
            Assert.Equal(path, tab.FilePath);
            Assert.Equal("catatan.md", tab.Title);
            Assert.Equal(path, tab.PathText);
            Assert.Equal("UTF-8", tab.EncodingLabel);
            Assert.False(tab.IsDirty);
            Assert.Equal("catatan.md — Makdon", tab.WindowTitle);
        });
    }

    [Fact]
    public void Load_Utf8Bom_ShowsBomInLabel_AndStripsItFromText()
    {
        var path = dir.WriteBytes("bom.md", [0xEF, 0xBB, 0xBF, 0x68, 0x69]);

        Sta(() =>
        {
            var tab = Load(path);

            Assert.Equal("hi", tab.Document.Text);
            Assert.Equal("UTF-8 BOM", tab.EncodingLabel);
        });
    }

    [Fact]
    public void Load_Windows1252File_IsDecodedAndLabelled()
    {
        var path = dir.WriteBytes("lama.txt", [0x63, 0x61, 0x66, 0xE9]);

        Sta(() =>
        {
            var tab = Load(path);

            Assert.Equal("café", tab.Document.Text);
            Assert.Equal("Windows-1252", tab.EncodingLabel);
        });
    }

    [Fact]
    public void Load_EmptyFile_GivesEmptyCleanTab()
    {
        var path = dir.WriteBytes("kosong.md", []);

        Sta(() =>
        {
            var tab = Load(path);

            Assert.Equal("", tab.Document.Text);
            Assert.False(tab.IsDirty);
        });
    }

    [Fact]
    public void Load_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => Sta(() => DocumentTab.Load(dir.File("tidak-ada.md"))));
    }

    [Fact]
    public void Load_Directory_Throws()
    {
        var ex = Record.Exception(() => Sta(() => DocumentTab.Load(dir.Path)));

        Assert.True(ex is UnauthorizedAccessException or IOException, $"tipe: {ex?.GetType()}");
    }

    [Fact]
    public void Load_FileLockedForWritingByEditor_StillOpens()
    {
        var path = dir.WriteText("dipakai.md", "isi");
        using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Sta(() => Assert.Equal("isi", Load(path).Document.Text));
    }

    [Fact]
    public void Load_UnicodeFileName_ShowsFullName()
    {
        var path = dir.WriteText("ünï 日本 (1).md", "x");

        Sta(() => Assert.Equal("ünï 日本 (1).md", Load(path).Title));
    }

    // ---- IsDirty / judul ----

    [Fact]
    public void IsDirty_TracksEditsAndUndo()
    {
        var path = dir.WriteText("a.md", "abc");

        Sta(() =>
        {
            var tab = Load(path);

            Edit(tab);
            Assert.True(tab.IsDirty);
            Assert.Equal("● a.md", tab.DisplayTitle);

            tab.Document.UndoStack.Undo();
            Assert.False(tab.IsDirty);
            Assert.Equal("a.md", tab.DisplayTitle);
        });
    }

    [Fact]
    public void IsDirty_RaisesTitlePropertyChangesWhenFirstEditHappens()
    {
        var path = dir.WriteText("a.md", "abc");

        Sta(() =>
        {
            var tab = Load(path);
            var raised = new List<string?>();
            tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            Edit(tab);

            Assert.Contains(nameof(DocumentTab.IsDirty), raised);
            Assert.Contains(nameof(DocumentTab.DisplayTitle), raised);
            Assert.Contains(nameof(DocumentTab.WindowTitle), raised);
        });
    }

    [Fact]
    public void Mode_RaisesPropertyChangedOnlyWhenValueChanges()
    {
        Sta(() =>
        {
            var tab = Untitled();
            var count = 0;
            tab.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DocumentTab.Mode)) count++; };

            tab.Mode = ViewMode.Split; // sama dengan bawaan
            tab.Mode = ViewMode.Edit;
            tab.Mode = ViewMode.Edit;
            tab.Mode = ViewMode.Preview;

            Assert.Equal(2, count);
            Assert.Equal(ViewMode.Preview, tab.Mode);
        });
    }

    [Fact]
    public void SetCaretAndStats_UpdateStatusTexts()
    {
        Sta(() =>
        {
            var tab = Untitled();
            var raised = new List<string?>();
            tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            tab.SetCaret(3, 12);
            tab.SetStats(1234, 5678);

            Assert.Equal("Brs 3, Kol 12", tab.CaretText);
            Assert.Equal("1.234 kata · 5.678 karakter", tab.StatsText);
            Assert.Contains(nameof(DocumentTab.CaretText), raised);
            Assert.Contains(nameof(DocumentTab.StatsText), raised);
        });
    }

    [Fact]
    public void RequestOpen_RaisesOpenLinkRequestedWithPathAndAnchor()
    {
        Sta(() =>
        {
            var tab = Untitled();
            (string, string?)? got = null;
            tab.OpenLinkRequested += (p, a) => got = (p, a);

            tab.RequestOpen(@"C:\x.md", "bagian-2");

            Assert.Equal((@"C:\x.md", "bagian-2"), got);
        });
    }

    // ---- SaveTo ----

    [Fact]
    public void SaveTo_Untitled_WritesFile_NamesTab_AndClearsDirty()
    {
        var path = dir.File("baru.md");

        Sta(() =>
        {
            var tab = Untitled();
            Edit(tab, "# Halo é");
            var raised = new List<string?>();
            tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            tab.SaveTo(path);

            Assert.Equal("# Halo é", File.ReadAllText(path, new UTF8Encoding(false)));
            Assert.Equal(path, tab.FilePath);
            Assert.Equal("baru.md", tab.Title);
            Assert.Equal(path, tab.PathText);
            Assert.False(tab.IsDirty);
            Assert.Contains(nameof(DocumentTab.FilePath), raised);
            Assert.Contains(nameof(DocumentTab.PathText), raised);
            Assert.Contains(nameof(DocumentTab.Title), raised);
            Assert.Contains(nameof(DocumentTab.IsDirty), raised);
        });
    }

    [Fact]
    public void SaveTo_Untitled_WritesUtf8WithoutBom()
    {
        var path = dir.File("baru.md");

        Sta(() =>
        {
            var tab = Untitled();
            Edit(tab, "a");
            tab.SaveTo(path);
        });

        Assert.Equal(new byte[] { 0x61 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void SaveTo_ExistingFile_PreservesBomAndEncoding()
    {
        var path = dir.WriteBytes("bom.md", [0xEF, 0xBB, 0xBF, 0x61]);

        Sta(() =>
        {
            var tab = Load(path);
            tab.Document.Insert(1, "bc");

            tab.SaveTo(path);

            Assert.Equal("UTF-8 BOM", tab.EncodingLabel);
        });

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, 0x61, 0x62, 0x63 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void SaveTo_Utf16File_StaysUtf16()
    {
        var original = new byte[] { 0xFF, 0xFE, 0x61, 0x00 };
        var path = dir.WriteBytes("u16.txt", original);

        Sta(() =>
        {
            var tab = Load(path);
            tab.Document.Insert(1, "b");
            tab.SaveTo(path);

            Assert.Equal("UTF-16 LE", tab.EncodingLabel);
        });

        Assert.Equal(new byte[] { 0xFF, 0xFE, 0x61, 0x00, 0x62, 0x00 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void SaveTo_Windows1252File_WithNewUnicodeChar_UpgradesToUtf8_AndUpdatesLabel()
    {
        var path = dir.WriteBytes("lama.txt", [0x63, 0x61, 0x66, 0xE9]);

        Sta(() =>
        {
            var tab = Load(path);
            Assert.Equal("Windows-1252", tab.EncodingLabel);
            tab.Document.Insert(tab.Document.TextLength, " 日本");
            var raised = new List<string?>();
            tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            tab.SaveTo(path);

            Assert.Equal("UTF-8", tab.EncodingLabel);
            Assert.Contains(nameof(DocumentTab.EncodingLabel), raised);
        });

        Assert.Equal("café 日本", File.ReadAllText(path, new UTF8Encoding(false, true)));
    }

    [Fact]
    public void SaveTo_Windows1252File_WithOnly1252Chars_StaysSingleByte()
    {
        var path = dir.WriteBytes("lama.txt", [0x63, 0x61, 0x66, 0xE9]);

        Sta(() =>
        {
            var tab = Load(path);
            tab.Document.Insert(tab.Document.TextLength, "€");
            tab.SaveTo(path);

            Assert.Equal("Windows-1252", tab.EncodingLabel);
        });

        Assert.Equal(new byte[] { 0x63, 0x61, 0x66, 0xE9, 0x80 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void SaveTo_UnchangedLoadedFile_IsByteIdentical()
    {
        byte[] original = [0xEF, 0xBB, 0xBF, .. "a\r\nb\r\n日本"u8.ToArray()];
        var path = dir.WriteBytes("same.md", original);

        Sta(() => Load(path).SaveTo(path));

        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void SaveAs_NewPath_SwitchesTabToNewFile_AndLeavesOldFileUntouched()
    {
        var oldPath = dir.WriteText("lama.md", "lama");
        var newPath = dir.File("baru.md");

        Sta(() =>
        {
            var tab = Load(oldPath);
            tab.Document.Text = "diedit";

            tab.SaveTo(newPath);

            Assert.Equal(newPath, tab.FilePath);
            Assert.Equal("baru.md", tab.Title);
            Assert.False(tab.IsDirty);
        });

        Assert.Equal("lama", File.ReadAllText(oldPath));
        Assert.Equal("diedit", File.ReadAllText(newPath));
    }

    [Fact]
    public void SaveTo_LeavesNoTempFiles()
    {
        var path = dir.File("a.md");

        Sta(() =>
        {
            var tab = Untitled();
            Edit(tab, "x");
            tab.SaveTo(path);
            Edit(tab, "y");
            tab.SaveTo(path);
        });

        Assert.Equal(new[] { "a.md" }, dir.Entries());
    }

    [Fact]
    public void SaveTo_MissingDirectory_Throws_AndKeepsTabStateDirty()
    {
        var path = dir.File(Path.Combine("tidak-ada", "a.md"));

        Sta(() =>
        {
            var tab = Untitled();
            Edit(tab, "x");

            Assert.Throws<DirectoryNotFoundException>(() => tab.SaveTo(path));

            Assert.Null(tab.FilePath);
            Assert.True(tab.IsDirty);
            Assert.Equal("Belum disimpan", tab.PathText);
        });
    }

    [Fact]
    public void SaveTo_LockedTarget_Throws_KeepsOriginalFile_AndTabStaysDirtyWithOldPath()
    {
        var path = dir.WriteText("a.md", "ASLI");

        Sta(() =>
        {
            var tab = Load(path);
            tab.Document.Text = "BARU";
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var ex = Record.Exception(() => tab.SaveTo(path));
                Assert.True(ex is IOException or UnauthorizedAccessException, $"tipe: {ex?.GetType()}");
            }

            Assert.True(tab.IsDirty);
            Assert.Equal(path, tab.FilePath);
        });

        Assert.Equal("ASLI", File.ReadAllText(path));
        Assert.Equal(new[] { "a.md" }, dir.Entries());
    }

    [Fact]
    public void SaveTo_FailedSaveAs_KeepsPreviousPath()
    {
        var oldPath = dir.WriteText("lama.md", "lama");

        Sta(() =>
        {
            var tab = Load(oldPath);
            tab.Document.Text = "x";

            Assert.Throws<DirectoryNotFoundException>(() => tab.SaveTo(dir.File(Path.Combine("none", "b.md"))));

            Assert.Equal(oldPath, tab.FilePath);
            Assert.True(tab.IsDirty);
        });
    }

    [Fact]
    public void SaveTo_AfterSave_RetypingMakesDirtyAgain()
    {
        var path = dir.File("a.md");

        Sta(() =>
        {
            var tab = Untitled();
            Edit(tab, "x");
            tab.SaveTo(path);
            Assert.False(tab.IsDirty);

            Edit(tab, "y");

            Assert.True(tab.IsDirty);
        });
    }

    // ---- Perubahan eksternal ----

    [Fact]
    public void CheckExternalChange_NoChange_DoesNothing()
    {
        var path = dir.WriteText("a.md", "abc");

        Sta(() =>
        {
            var tab = Load(path);
            var conflicts = 0;
            tab.ExternalChangeConflict += _ => conflicts++;
            Edit(tab);

            tab.CheckExternalChange();

            Assert.Equal(0, conflicts);
            Assert.Equal("Xabc", tab.Document.Text);
            Assert.True(tab.IsDirty);
        });
    }

    [Fact]
    public void CheckExternalChange_CleanTab_ReloadsAutomaticallyWithoutConflict()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            var conflicts = 0;
            tab.ExternalChangeConflict += _ => conflicts++;
            File.WriteAllText(path, "diubah dari luar", new UTF8Encoding(false));

            tab.CheckExternalChange();

            Assert.Equal("diubah dari luar", tab.Document.Text);
            Assert.False(tab.IsDirty);
            Assert.Equal(0, conflicts);
        });
    }

    [Fact]
    public void CheckExternalChange_CleanTab_AutoReloadIsOneUndoStep()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            File.WriteAllText(path, "baru", new UTF8Encoding(false));
            tab.CheckExternalChange();

            Assert.Equal("baru", tab.Document.Text);
            Assert.False(tab.IsDirty);
            Assert.True(tab.Document.UndoStack.CanUndo);

            tab.Document.UndoStack.Undo();

            Assert.Equal("lama", tab.Document.Text);
            Assert.True(tab.IsDirty);
            Assert.False(tab.Document.UndoStack.CanUndo); // satu langkah saja
        });
    }

    [Fact]
    public void CheckExternalChange_DirtyTab_RaisesConflictOnce_AndKeepsUserEdits()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            var conflicts = new List<DocumentTab>();
            tab.ExternalChangeConflict += conflicts.Add;
            tab.Document.Text = "punya saya";
            File.WriteAllText(path, "punya orang lain", new UTF8Encoding(false));

            tab.CheckExternalChange();
            tab.CheckExternalChange(); // perubahan yang sama tidak boleh memunculkan prompt lagi

            Assert.Single(conflicts);
            Assert.Same(tab, conflicts[0]);
            Assert.Equal("punya saya", tab.Document.Text);
            Assert.True(tab.IsDirty);
        });
    }

    [Fact]
    public void CheckExternalChange_DirtyTab_NewerExternalChangeRaisesConflictAgain()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            var conflicts = 0;
            tab.ExternalChangeConflict += _ => conflicts++;
            Edit(tab);

            File.WriteAllText(path, "v2", new UTF8Encoding(false));
            tab.CheckExternalChange();
            File.WriteAllText(path, "v3", new UTF8Encoding(false));
            tab.CheckExternalChange();

            Assert.Equal(2, conflicts);
        });
    }

    [Fact]
    public void CheckExternalChange_DirtyTab_ThenReload_TakesDiskContentAndClearsDirty()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            tab.ExternalChangeConflict += t => t.Reload();
            tab.Document.Text = "punya saya";
            File.WriteAllText(path, "disk", new UTF8Encoding(false));

            tab.CheckExternalChange();

            Assert.Equal("disk", tab.Document.Text);
            Assert.False(tab.IsDirty);
        });
    }

    [Fact]
    public void CheckExternalChange_TouchWithIdenticalContent_IsIgnored()
    {
        var path = dir.WriteText("a.md", "sama");

        Sta(() =>
        {
            var tab = Load(path);
            var conflicts = 0;
            tab.ExternalChangeConflict += _ => conflicts++;
            Edit(tab);

            File.WriteAllText(path, "sama", new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(1));
            tab.CheckExternalChange();

            Assert.Equal(0, conflicts);
            Assert.Equal("Xsama", tab.Document.Text);
        });
    }

    [Fact]
    public void CheckExternalChange_AfterOwnSave_IsNotTreatedAsExternal()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            var conflicts = 0;
            tab.ExternalChangeConflict += _ => conflicts++;
            tab.Document.Text = "simpan saya";
            tab.SaveTo(path);
            Edit(tab);

            tab.CheckExternalChange();

            Assert.Equal(0, conflicts);
            Assert.Equal("Xsimpan saya", tab.Document.Text);
        });
    }

    [Fact]
    public void CheckExternalChange_AfterOwnSaveToNewPath_WatchesNewFile()
    {
        var oldPath = dir.WriteText("lama.md", "lama");
        var newPath = dir.File("baru.md");

        Sta(() =>
        {
            var tab = Load(oldPath);
            tab.SaveTo(newPath);
            File.WriteAllText(newPath, "dari luar", new UTF8Encoding(false));

            tab.CheckExternalChange();

            Assert.Equal("dari luar", tab.Document.Text);
        });
    }

    [Fact]
    public void CheckExternalChange_ExternalChangeToDifferentEncoding_UpdatesLabel()
    {
        var path = dir.WriteText("a.md", "abc");

        Sta(() =>
        {
            var tab = Load(path);
            File.WriteAllBytes(path, [0xFF, 0xFE, 0x78, 0x00]);

            tab.CheckExternalChange();

            Assert.Equal("x", tab.Document.Text);
            Assert.Equal("UTF-16 LE", tab.EncodingLabel);
        });
    }

    [Fact]
    public void CheckExternalChange_FileDeleted_DoesNotThrowAndKeepsEditorContent()
    {
        var path = dir.WriteText("a.md", "abc");

        Sta(() =>
        {
            var tab = Load(path);
            var conflicts = 0;
            tab.ExternalChangeConflict += _ => conflicts++;
            tab.Dispose(); // lepas watcher agar file bisa dihapus tanpa menunggu
            File.Delete(path);

            var ex = Record.Exception(() => tab.CheckExternalChange());

            Assert.Null(ex);
            Assert.Equal("abc", tab.Document.Text);
            Assert.Equal(0, conflicts);
        });
    }

    [Fact]
    public void CheckExternalChange_FileDeletedWhileTabAlive_KeepsContent()
    {
        var path = dir.WriteText("a.md", "abc");

        Sta(() =>
        {
            var tab = Load(path);
            File.Delete(path);

            var ex = Record.Exception(() => tab.CheckExternalChange());

            Assert.Null(ex);
            Assert.Equal("abc", tab.Document.Text);
        });
    }

    [Fact]
    public void CheckExternalChange_DirectoryDeleted_DoesNotThrow()
    {
        var path = dir.WriteText(Path.Combine("sub", "a.md"), "abc");

        Sta(() =>
        {
            var tab = Load(path);
            tab.Dispose();
            Directory.Delete(dir.File("sub"), recursive: true);

            Assert.Null(Record.Exception(() => tab.CheckExternalChange()));
        });
    }

    [Fact]
    public void CheckExternalChange_FileLockedByWriter_DoesNotThrowOrRaiseEvents()
    {
        var path = dir.WriteText("a.md", "abc");

        Sta(() =>
        {
            var tab = Load(path);
            var conflicts = 0;
            tab.ExternalChangeConflict += _ => conflicts++;
            Edit(tab);

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Null(Record.Exception(() => tab.CheckExternalChange()));
            }

            Assert.Equal(0, conflicts);
            Assert.Equal("Xabc", tab.Document.Text);
            tab.Dispose(); // menghentikan timer percobaan-ulang
        });
    }

    [Fact]
    public void CheckExternalChange_UntitledTab_IsNoOp()
    {
        Sta(() =>
        {
            var tab = Untitled();
            Edit(tab, "x");

            Assert.Null(Record.Exception(() => tab.CheckExternalChange()));
            Assert.Equal("x", tab.Document.Text);
        });
    }

    [Fact]
    public void CheckExternalChange_AfterDispose_IsNoOp()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            tab.Dispose();
            File.WriteAllText(path, "baru", new UTF8Encoding(false));

            tab.CheckExternalChange();

            Assert.Equal("lama", tab.Document.Text);
        });
    }

    // ---- Reload ----

    [Fact]
    public void Reload_DiscardsEditorChangesAndTakesDiskContent()
    {
        var path = dir.WriteText("a.md", "disk");

        Sta(() =>
        {
            var tab = Load(path);
            tab.Document.Text = "edit saya";
            Assert.True(tab.IsDirty);

            tab.Reload();

            Assert.Equal("disk", tab.Document.Text);
            Assert.False(tab.IsDirty);
            // Satu langkah Undo: edit yang terbuang karena salah pilih di dialog konflik masih bisa dikembalikan.
            Assert.True(tab.Document.UndoStack.CanUndo);
            tab.Document.UndoStack.Undo();
            Assert.Equal("edit saya", tab.Document.Text);
            Assert.True(tab.IsDirty);
        });
    }

    [Fact]
    public void Reload_Untitled_IsNoOp()
    {
        Sta(() =>
        {
            var tab = Untitled();
            Edit(tab, "tetap");

            tab.Reload();

            Assert.Equal("tetap", tab.Document.Text);
            Assert.True(tab.IsDirty);
        });
    }

    [Fact]
    public void Reload_FileGone_ThrowsAndKeepsEditorContent()
    {
        var path = dir.WriteText("a.md", "abc");

        Sta(() =>
        {
            var tab = Load(path);
            tab.Dispose();
            File.Delete(path);
            Edit(tab);

            Assert.Throws<FileNotFoundException>(() => tab.Reload());

            Assert.Equal("Xabc", tab.Document.Text);
            Assert.True(tab.IsDirty);
        });
    }

    [Fact]
    public void Reload_ShorterFile_KeepsCaretWithinTextBounds()
    {
        var path = dir.WriteText("a.md", "0123456789");

        Sta(() =>
        {
            var tab = Load(path);
            tab.View.CaretOffset = 10;
            File.WriteAllText(path, "ab", new UTF8Encoding(false));

            tab.Reload();

            Assert.True(tab.View.CaretOffset <= 2);
        });
    }

    // ---- Dispose ----

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var path = dir.WriteText("a.md", "x");

        Sta(() =>
        {
            var tab = Load(path);

            tab.Dispose();
            Assert.Null(Record.Exception(() => tab.Dispose()));
        });
    }

    [Fact]
    public void Dispose_ReleasesDirectoryWatcherSoFolderCanBeDeleted()
    {
        var sub = dir.File("watched");
        var path = dir.WriteText(Path.Combine("watched", "a.md"), "x");

        Sta(() =>
        {
            var tab = Load(path);
            tab.Dispose();

            Directory.Delete(sub, recursive: true);

            Assert.False(Directory.Exists(sub));
        });
    }

    // ---- Pratinjau dengan gambar bermasalah ----

    [Fact]
    public void Load_MarkdownReferencingExistingButUndecodableImage_DoesNotThrow()
    {
        dir.WriteBytes("rusak.png", [1, 2, 3]);
        var path = dir.WriteText("a.md", "![x](rusak.png)");

        Sta(() =>
        {
            var ex = Record.Exception(() => Load(path));

            Assert.Null(ex);
        });
    }

    [Fact]
    public void UndecodableImage_IsReplacedByAltText_AndGoodImagesAreKept()
    {
        dir.WriteBytes("rusak.png", [1, 2, 3]);
        dir.WriteBytes("bagus.png", OnePixelPng);
        var path = dir.WriteText("a.md", "![teks-alt](rusak.png) dan ![ok](bagus.png)");

        Sta(() =>
        {
            var tab = Load(path);

            var document = tab.View.BuildPrintDocument();
            var text = new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd).Text;
            Assert.Contains("[gambar tidak dapat ditampilkan: teks-alt]", text);
            Assert.DoesNotContain("[gambar tidak dapat ditampilkan: ok]", text);
        });
    }

    // ---- Ekspor HTML ----

    [Fact]
    public void ExportHtml_UsesFileNameAsTitle_AndEmbedsImagesRelativeToTheFile()
    {
        dir.WriteBytes(Path.Combine("img", "p.png"), OnePixelPng);
        var path = dir.WriteText("Laporan Akhir.md", "# Bab 1\n\n![gambar](img/p.png)\n");
        var output = dir.File("keluar.html");

        Sta(() => Load(path).ExportHtml(output));

        var html = File.ReadAllText(output);
        Assert.Contains("<title>Laporan Akhir</title>", html);
        Assert.Contains("<h1 id=\"bab-1\">Bab 1</h1>", html);
        Assert.Contains($"src=\"data:image/png;base64,{Convert.ToBase64String(OnePixelPng)}\"", html);
        Assert.DoesNotContain("file:///", html);
    }

    [Fact]
    public void ExportHtml_UntitledTab_UsesUntitledNameAndLeavesImagesAlone()
    {
        var output = dir.File("keluar.html");

        Sta(() =>
        {
            var tab = Untitled();
            tab.Document.Text = "![x](a.png)";
            tab.ExportHtml(output);
        });

        var html = File.ReadAllText(output);
        Assert.Matches(@"<title>Tanpa Judul-\d+</title>", html);
        Assert.Contains("src=\"a.png\"", html);
    }

    [Fact]
    public void ExportHtml_UnsavedEditsAreExported_NotTheDiskVersion()
    {
        var path = dir.WriteText("a.md", "lama");
        var output = dir.File("keluar.html");

        Sta(() =>
        {
            var tab = Load(path);
            tab.Document.Text = "belum disimpan";
            tab.ExportHtml(output);
        });

        Assert.Contains("belum disimpan", File.ReadAllText(output));
    }

    [Fact]
    public void ExportHtml_BadOutputDirectory_Throws()
    {
        Sta(() =>
        {
            var tab = Untitled();

            Assert.Throws<DirectoryNotFoundException>(() => tab.ExportHtml(dir.File(Path.Combine("none", "o.html"))));
        });
    }
}

/// <summary>ThemeManager.Apply menukar ResourceDictionary pada Application yang disiapkan <see cref="WpfHost"/>.</summary>
[Collection("Wpf")]
public class ThemeManagerApplyTests
{
    static string MergedThemeSource() =>
        System.Windows.Application.Current.Resources.MergedDictionaries
            .Select(d => d.Source?.OriginalString ?? "")
            .Single(s => s.Contains("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase)
                         || s.Contains("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Apply_DarkThenLight_SwapsTheThemeDictionaryInPlace()
    {
        WpfHost.Instance.Run(() =>
        {
            var app = System.Windows.Application.Current;
            var before = app.Resources.MergedDictionaries.Count;
            var changed = 0;
            void Handler() => changed++;
            ThemeManager.ThemeChanged += Handler;
            try
            {
                ThemeManager.Apply(AppThemeMode.Dark);
                Assert.True(ThemeManager.IsDark);
                Assert.Equal(AppThemeMode.Dark, ThemeManager.Mode);
                Assert.Contains("Dark.xaml", MergedThemeSource());
                Assert.Equal(before, app.Resources.MergedDictionaries.Count);
                var darkBg = ((System.Windows.Media.SolidColorBrush)app.FindResource("EditorBackgroundBrush")).Color;

                ThemeManager.Apply(AppThemeMode.Light);
                Assert.False(ThemeManager.IsDark);
                Assert.Contains("Light.xaml", MergedThemeSource());
                Assert.Equal(before, app.Resources.MergedDictionaries.Count);
                var lightBg = ((System.Windows.Media.SolidColorBrush)app.FindResource("EditorBackgroundBrush")).Color;

                Assert.True(EditorTheme.RelativeLuminance(darkBg) < EditorTheme.RelativeLuminance(lightBg));
                Assert.Equal(2, changed);
            }
            finally
            {
                ThemeManager.ThemeChanged -= Handler;
                ThemeManager.Apply(AppThemeMode.Light);
                ThemeManager.Shutdown();
            }
        });
    }

    [Fact]
    public void Apply_System_FollowsRegistryPreference()
    {
        WpfHost.Instance.Run(() =>
        {
            try
            {
                ThemeManager.Apply(AppThemeMode.System);

                Assert.Equal(!ThemeManager.SystemUsesLightTheme(), ThemeManager.IsDark);
                Assert.Equal(AppThemeMode.System, ThemeManager.Mode);
            }
            finally
            {
                ThemeManager.Apply(AppThemeMode.Light);
                ThemeManager.Shutdown();
            }
        });
    }

    [Fact]
    public void Apply_SameModeTwice_DoesNotAccumulateDictionaries()
    {
        WpfHost.Instance.Run(() =>
        {
            var app = System.Windows.Application.Current;
            try
            {
                ThemeManager.Apply(AppThemeMode.Dark);
                var count = app.Resources.MergedDictionaries.Count;

                ThemeManager.Apply(AppThemeMode.Dark);
                ThemeManager.Apply(AppThemeMode.Dark);

                Assert.Equal(count, app.Resources.MergedDictionaries.Count);
            }
            finally
            {
                ThemeManager.Apply(AppThemeMode.Light);
                ThemeManager.Shutdown();
            }
        });
    }

    [Fact]
    public void Shutdown_WithoutApply_OrTwice_DoesNotThrow()
    {
        WpfHost.Instance.Run(() =>
        {
            ThemeManager.Shutdown();
            ThemeManager.Shutdown();
        });
    }
}
