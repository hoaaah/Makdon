namespace MdViewer.Tests.Support;

/// <summary>Folder sementara unik per instance; dihapus saat Dispose.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MdViewer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public string WriteBytes(string name, byte[] bytes)
    {
        var full = File(name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllBytes(full, bytes);
        return full;
    }

    public string WriteText(string name, string text) => WriteBytes(name, new System.Text.UTF8Encoding(false).GetBytes(text));

    /// <summary>Semua entri (file/folder) langsung di folder ini.</summary>
    public string[] Entries(string? sub = null) =>
        Directory.GetFileSystemEntries(sub is null ? Path : File(sub))
            .Select(System.IO.Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
