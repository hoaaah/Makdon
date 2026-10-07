using System.Text;
using MdViewer.Tests.Support;

namespace MdViewer.Tests;

/// <summary>
/// Tepi konflik simpan/perubahan eksternal pada <see cref="DocumentTab"/> yang belum dicakup test lain:
/// urutan pertanyaan konflik, hash tertunda (pending), KeepEditorVersion, stempel file "racy", dan penanda lossy.
/// </summary>
[Collection("Wpf")]
public class DocumentTabConflictEdgeTests : IDisposable
{
    readonly TempDir dir = new();
    readonly List<DocumentTab> tabs = [];

    public void Dispose()
    {
        WpfHost.Instance.Run(() => { foreach (var t in tabs) t.Dispose(); });
        tabs.Clear();
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

    static void WriteExternal(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(false));

    // ---- SaveTo + SaveConflict ----

    [Fact]
    public void SaveTo_Conflict_HandlerReceivesTheTabItself()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            DocumentTab? received = null;
            tab.SaveConflict += t => { received = t; return SaveConflictChoice.Cancel; };
            tab.Document.Text = "saya";
            WriteExternal(path, "lain");

            tab.SaveTo(path);

            Assert.Same(tab, received);
        });
    }

    [Fact]
    public void SaveTo_Conflict_WithoutHandler_ThrowsRecoverableIOException_NamingTheFile_AndLeavesNoPendingState()
    {
        var path = dir.WriteText("laporan.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            tab.Document.Text = "saya";
            WriteExternal(path, "lain");

            var ex = Assert.Throws<ExternalChangeException>(() => tab.SaveTo(path));

            Assert.IsAssignableFrom<IOException>(ex);
            Assert.True(CrashLog.IsRecoverable(ex)); // MainWindow menampilkannya sebagai galat I/O biasa
            Assert.Contains("laporan.md", ex.Message);
            Assert.False(tab.HasExternalConflict); // hanya CheckExternalChange yang menandai pending
        });
    }

    [Fact]
    public void SaveTo_Conflict_Cancel_ThenOverwrite_AsksEachTime_AndOverwriteClearsPendingConflict()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            var choice = SaveConflictChoice.Cancel;
            var asked = 0;
            var reported = 0;
            tab.SaveConflict += _ => { asked++; return choice; };
            tab.ExternalChangeConflict += _ => reported++;
            tab.Document.Insert(0, "x");
            WriteExternal(path, "lain");
            tab.CheckExternalChange();
            Assert.Equal(1, reported);
            Assert.True(tab.HasExternalConflict);

            Assert.False(tab.SaveTo(path));
            Assert.False(tab.SaveTo(path));
            Assert.Equal(2, asked);
            Assert.True(tab.HasExternalConflict);

            choice = SaveConflictChoice.Overwrite;
            Assert.True(tab.SaveTo(path));

            Assert.Equal(3, asked);
            Assert.False(tab.HasExternalConflict);
            Assert.Equal("xlama", File.ReadAllText(path));
            tab.CheckExternalChange();
            Assert.Equal(1, reported); // tidak ada konflik baru: isi disk = hasil simpan kita
            Assert.False(tab.IsDirty);
        });
    }

    [Fact]
    public void SaveTo_Conflict_Overwrite_BecomesTheNewBaseline_SoTheNextSaveDoesNotAsk()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            var asked = 0;
            tab.SaveConflict += _ => { asked++; return SaveConflictChoice.Overwrite; };
            tab.Document.Text = "satu";
            WriteExternal(path, "lain");
            tab.SaveTo(path);

            tab.Document.Text = "dua";
            Assert.True(tab.SaveTo(path));

            Assert.Equal(1, asked);
            Assert.Equal("dua", File.ReadAllText(path));
        });
    }

    [Fact]
    public void SaveTo_Conflict_Reload_ClearsPending_UpdatesEncoding_AndLaterSavesDoNotAsk()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            var asked = 0;
            tab.SaveConflict += _ => { asked++; return SaveConflictChoice.Reload; };
            tab.Document.Insert(0, "x");
            File.WriteAllText(path, "orang lain", new UnicodeEncoding(false, true)); // UTF-16 LE + BOM
            tab.CheckExternalChange();
            Assert.True(tab.HasExternalConflict);
            Assert.Equal("UTF-8", tab.EncodingLabel);

            Assert.False(tab.SaveTo(path));

            Assert.False(tab.HasExternalConflict);
            Assert.Equal("UTF-16 LE", tab.EncodingLabel);
            Assert.Equal("orang lain", tab.Document.Text);
            Assert.False(tab.IsDirty);

            tab.Document.Insert(0, "y");
            Assert.True(tab.SaveTo(path));
            Assert.Equal(1, asked);
            Assert.Equal("UTF-16 LE", tab.EncodingLabel); // simpan berikutnya mempertahankan encoding file yang dimuat ulang
        });
    }

    // Muat ulang dari dialog konflik simpan adalah satu langkah Undo: edit pengguna tidak hilang permanen.
    [Fact]
    public void SaveTo_Conflict_Reload_IsOneUndoStep_AndUndoBringsBackTheEditorText()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            tab.SaveConflict += _ => SaveConflictChoice.Reload;
            tab.Document.Text = "hasil kerja saya";
            WriteExternal(path, "orang lain");

            Assert.False(tab.SaveTo(path));
            Assert.Equal("orang lain", tab.Document.Text);
            Assert.False(tab.IsDirty);

            tab.Document.UndoStack.Undo();

            Assert.Equal("hasil kerja saya", tab.Document.Text);
            Assert.True(tab.IsDirty);
        });
    }

    [Fact]
    public void ExternalConflict_Reload_IsOneUndoStep_AndUndoBringsBackTheEditorText()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            tab.Document.Text = "hasil kerja saya";
            WriteExternal(path, "orang lain");
            tab.CheckExternalChange();
            Assert.True(tab.HasExternalConflict);

            tab.Reload();
            tab.Document.UndoStack.Undo();

            Assert.Equal("hasil kerja saya", tab.Document.Text);
        });
    }

    // Dialog konflik bisa terbuka lama: file dibaca ulang setelah dialog ditutup, bukan memakai isi saat konflik terdeteksi.
    [Fact]
    public void SaveTo_Conflict_Reload_ReadsTheFileAgainAfterTheDialog_NotTheStaleBytes()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            tab.SaveConflict += _ =>
            {
                WriteExternal(path, "versi ketiga, ditulis saat dialog terbuka");
                return SaveConflictChoice.Reload;
            };
            tab.Document.Text = "saya";
            WriteExternal(path, "versi kedua");

            Assert.False(tab.SaveTo(path));

            Assert.Equal("versi ketiga, ditulis saat dialog terbuka", tab.Document.Text);
            Assert.False(tab.HasExternalConflict);
            tab.CheckExternalChange();
            Assert.False(tab.HasExternalConflict); // dasar perbandingan sudah isi terbaru: tidak ada konflik palsu
        });
    }

    // Selama SaveTo (dialog konflik simpan memompa pesan), pemeriksaan eksternal tab ini tidak boleh memunculkan konflik kedua.
    [Fact]
    public void SaveTo_Conflict_SuppressesExternalChangeChecksWhileTheDialogIsOpen()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            var raised = 0;
            tab.ExternalChangeConflict += _ => raised++;
            tab.SaveConflict += t =>
            {
                WriteExternal(path, "ditulis lagi saat dialog");
                t.CheckExternalChange(); // mis. Activated/timer saat dialog memompa pesan
                return SaveConflictChoice.Cancel;
            };
            tab.Document.Text = "saya";
            WriteExternal(path, "orang lain");

            Assert.False(tab.SaveTo(path));

            Assert.Equal(0, raised);
            Assert.False(tab.HasExternalConflict);
            Assert.Equal("saya", tab.Document.Text);
        });
    }

    [Fact]
    public void SaveTo_DifferentCasePath_IsStillTheSameFile_SoConflictIsDetected()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            var asked = 0;
            tab.SaveConflict += _ => { asked++; return SaveConflictChoice.Cancel; };
            tab.Document.Text = "saya";
            WriteExternal(path, "lain");

            Assert.False(tab.SaveTo(path.ToUpperInvariant()));

            Assert.Equal(1, asked);
            Assert.Equal("lain", File.ReadAllText(path));
        });
    }

    // ---- pendingHash / KeepEditorVersion ----

    [Fact]
    public void KeepEditorVersion_WithoutPendingConflict_IsNoOp_AndLaterExternalChangesAreStillDetected()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            tab.Document.Insert(0, "x");

            tab.KeepEditorVersion();
            WriteExternal(path, "lain");
            tab.CheckExternalChange();

            Assert.True(tab.HasExternalConflict);
        });
    }

    [Fact]
    public void KeepEditorVersion_ThenAnotherExternalChange_IsReportedAgain()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            var reported = 0;
            var asked = 0;
            tab.ExternalChangeConflict += _ => reported++;
            tab.SaveConflict += _ => { asked++; return SaveConflictChoice.Cancel; };
            tab.Document.Insert(0, "x");
            WriteExternal(path, "versi A");
            tab.CheckExternalChange();
            tab.KeepEditorVersion();

            WriteExternal(path, "versi B yang lebih panjang");
            tab.CheckExternalChange();

            Assert.Equal(2, reported);
            Assert.True(tab.HasExternalConflict);
            Assert.False(tab.SaveTo(path));
            Assert.Equal(1, asked);
        });
    }

    [Fact]
    public void ExternalConflict_RewriteWithSamePendingContent_DoesNotReportAgain_AndStaysPending()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            var reported = 0;
            tab.ExternalChangeConflict += _ => reported++;
            tab.Document.Insert(0, "x");
            WriteExternal(path, "versi A");
            tab.CheckExternalChange();

            WriteExternal(path, "versi A");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(1)); // "touch": isi tetap
            tab.CheckExternalChange();
            tab.CheckExternalChange();

            Assert.Equal(1, reported);
            Assert.True(tab.HasExternalConflict);
        });
    }

    [Fact]
    public void ExternalConflict_Reload_ClearsPendingAndDropsEditorChanges()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            var reported = 0;
            tab.ExternalChangeConflict += _ => reported++;
            tab.Document.Insert(0, "x");
            WriteExternal(path, "lain");
            tab.CheckExternalChange();

            tab.Reload();

            Assert.False(tab.HasExternalConflict);
            Assert.False(tab.IsDirty);
            Assert.Equal("lain", tab.Document.Text);
            tab.CheckExternalChange();
            Assert.Equal(1, reported);
        });
    }

    [Fact]
    public void ExternalConflict_CleanTab_NeverBecomesPending()
    {
        var path = dir.WriteText("a.md", "lama");

        Sta(() =>
        {
            var tab = Load(path);
            WriteExternal(path, "lain");

            tab.CheckExternalChange();

            Assert.False(tab.HasExternalConflict);
            Assert.Equal("lain", tab.Document.Text);
        });
    }

    // ---- FileStamp "racy": ukuran dan waktu tulis sama, isi beda ----

    [Fact]
    public void CheckExternalChange_SameSizeAndTimestampButDifferentContent_IsStillDetected()
    {
        var path = dir.WriteText("a.md", "aaaa");
        var originalWrite = File.GetLastWriteTimeUtc(path);

        Sta(() =>
        {
            var tab = Load(path);
            tab.Document.Insert(0, "x");

            WriteExternal(path, "bbbb");
            File.SetLastWriteTimeUtc(path, originalWrite);
            tab.CheckExternalChange();

            Assert.True(tab.HasExternalConflict);
        });
    }

    [Fact]
    public void CheckExternalChange_SameSizeAndTimestamp_OnCleanTab_ReloadsContent()
    {
        var path = dir.WriteText("a.md", "aaaa");
        var originalWrite = File.GetLastWriteTimeUtc(path);

        Sta(() =>
        {
            var tab = Load(path);

            WriteExternal(path, "bbbb");
            File.SetLastWriteTimeUtc(path, originalWrite);
            tab.CheckExternalChange();

            Assert.Equal("bbbb", tab.Document.Text);
        });
    }

    [Fact]
    public void SaveTo_SameSizeAndTimestampButDifferentContent_RaisesConflict()
    {
        var path = dir.WriteText("a.md", "aaaa");
        var originalWrite = File.GetLastWriteTimeUtc(path);

        Sta(() =>
        {
            var tab = Load(path);
            var asked = 0;
            tab.SaveConflict += _ => { asked++; return SaveConflictChoice.Cancel; };
            tab.Document.Text = "saya";

            WriteExternal(path, "bbbb");
            File.SetLastWriteTimeUtc(path, originalWrite);

            Assert.False(tab.SaveTo(path));
            Assert.Equal(1, asked);
            Assert.Equal("bbbb", File.ReadAllText(path));
        });
    }

    [Fact]
    public void SaveTo_OldFileUnchangedOnDisk_SavesWithoutAsking()
    {
        var path = dir.WriteText("a.md", "lama");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1)); // stempel andal (jauh di masa lalu)

        Sta(() =>
        {
            var tab = Load(path);
            var asked = 0;
            tab.SaveConflict += _ => { asked++; return SaveConflictChoice.Cancel; };
            tab.Document.Text = "baru";

            Assert.True(tab.SaveTo(path));

            Assert.Equal(0, asked);
            Assert.Equal("baru", File.ReadAllText(path));
        });
    }

    // ---- IsLossyDecoded ----

    static readonly byte[] CleanBom = [0xEF, 0xBB, 0xBF, 0x61];
    static readonly byte[] LossyBom = [0xEF, 0xBB, 0xBF, 0x61, 0xFF, 0x62];

    [Fact]
    public void Lossy_ExternalReloadOfCleanTab_SetsFlag_AndSavingWritesValidBytesAndClearsIt()
    {
        var path = dir.WriteBytes("a.md", CleanBom);

        Sta(() =>
        {
            var tab = Load(path);
            Assert.False(tab.IsLossyDecoded);

            File.WriteAllBytes(path, LossyBom);
            tab.CheckExternalChange();

            Assert.True(tab.IsLossyDecoded);
            Assert.Equal("a�b", tab.Document.Text);

            Assert.True(tab.SaveTo(path));

            Assert.False(tab.IsLossyDecoded);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, 0x61, 0xEF, 0xBF, 0xBD, 0x62 }, File.ReadAllBytes(path));
            TextFileIO.Decode(File.ReadAllBytes(path), out var lossyAfter);
            Assert.False(lossyAfter);
        });
    }

    [Fact]
    public void Lossy_Reload_FollowsTheDiskContent_BothWays()
    {
        var path = dir.WriteBytes("a.md", LossyBom);

        Sta(() =>
        {
            var tab = Load(path);
            Assert.True(tab.IsLossyDecoded);

            File.WriteAllBytes(path, CleanBom);
            tab.Reload();
            Assert.False(tab.IsLossyDecoded);

            File.WriteAllBytes(path, LossyBom);
            tab.Reload();
            Assert.True(tab.IsLossyDecoded);
        });
    }

    [Fact]
    public void Lossy_ConflictReload_TakesLossyFlagFromDisk()
    {
        var path = dir.WriteBytes("a.md", CleanBom);

        Sta(() =>
        {
            var tab = Load(path);
            tab.SaveConflict += _ => SaveConflictChoice.Reload;
            tab.Document.Insert(0, "x");
            File.WriteAllBytes(path, LossyBom);

            Assert.False(tab.SaveTo(path));

            Assert.True(tab.IsLossyDecoded);
        });
    }

    [Fact]
    public void Lossy_CancelledSave_KeepsFlag_SaveAsToNewPath_ClearsIt()
    {
        var path = dir.WriteBytes("a.md", LossyBom);
        var other = dir.File("b.md");

        Sta(() =>
        {
            var tab = Load(path);
            tab.SaveConflict += _ => SaveConflictChoice.Cancel;
            tab.Document.Insert(0, "x");
            File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, 0x7A, 0xFF]); // perubahan eksternal -> konflik
            Assert.False(tab.SaveTo(path));
            Assert.True(tab.IsLossyDecoded);

            Assert.True(tab.SaveTo(other));

            Assert.False(tab.IsLossyDecoded);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, 0x78, 0x61, 0xEF, 0xBF, 0xBD, 0x62 }, File.ReadAllBytes(other));
        });
    }

    [Fact]
    public void Lossy_NonBomInvalidBytes_AreNotLossy_TheyBecomeWindows1252()
    {
        var path = dir.WriteBytes("a.md", [0x61, 0xFF, 0x62]);

        Sta(() =>
        {
            var tab = Load(path);

            Assert.False(tab.IsLossyDecoded);
            Assert.Equal("Windows-1252", tab.EncodingLabel);
        });
    }
}
