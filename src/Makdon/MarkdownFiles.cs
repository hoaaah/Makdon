namespace Makdon;

static class MarkdownFiles
{
    static readonly string[] Extensions = [".md", ".markdown", ".mdown", ".mkd", ".txt"];

    public static bool IsMarkdown(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
