using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig.Renderers;

namespace Makdon;

/// <summary>
/// Ekspor markdown ke satu file HTML mandiri (CSS ditanam). HTML mentah di-escape dan URL disaring allowlist
/// (lihat <see cref="MarkdownSupport.SanitizeForExport"/>); gambar lokal disematkan sebagai data URI bila kecil.
/// </summary>
public static class HtmlExporter
{
    static readonly Regex Placeholder = new(@"\{\{(?:TITLE|BODY)\}\}", RegexOptions.CultureInvariant);

    const string Template = """
        <!DOCTYPE html>
        <html lang="id">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{TITLE}}</title>
        <style>
        body { max-width: 860px; margin: 2em auto; padding: 0 1em; font-family: "Segoe UI", system-ui, sans-serif; line-height: 1.6; color: #24292f; }
        h1, h2 { border-bottom: 1px solid #d0d7de; padding-bottom: .3em; }
        h1, h2, h3, h4, h5, h6 { margin-top: 1.5em; line-height: 1.25; }
        a { color: #0969da; }
        img { max-width: 100%; }
        code { font-family: "Cascadia Mono", Consolas, monospace; background: #f6f8fa; padding: .2em .4em; border-radius: 4px; font-size: 90%; }
        pre { background: #f6f8fa; padding: 1em; overflow: auto; border-radius: 6px; }
        pre code { background: none; padding: 0; }
        blockquote { margin: 0; padding: 0 1em; color: #57606a; border-left: .25em solid #d0d7de; }
        table { border-collapse: collapse; }
        th, td { border: 1px solid #d0d7de; padding: 6px 13px; }
        th { background: #f6f8fa; }
        hr { border: 0; border-top: 1px solid #d0d7de; }
        </style>
        </head>
        <body>
        {{BODY}}
        </body>
        </html>
        """;

    /// <param name="baseDirectory">Folder file markdown; dipakai menemukan gambar relatif untuk disematkan. Null = biarkan apa adanya.</param>
    public static string ToHtmlDocument(string markdown, string? title = null, string? baseDirectory = null)
    {
        var parsed = Markdig.Markdown.Parse(markdown, MarkdownSupport.ExportPipeline);
        MarkdownSupport.SanitizeForExport(parsed, baseDirectory);
        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        MarkdownSupport.ExportPipeline.Setup(renderer);
        renderer.Render(parsed);
        var body = writer.ToString();

        // Satu pass: isi placeholder tidak dipindai lagi, jadi "{{BODY}}" di judul atau "{{TITLE}}" di isi tetap literal.
        var safeTitle = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(title) ? "Dokumen" : title);
        var trimmedBody = body.TrimEnd();
        return Placeholder.Replace(Template, m => m.Value == "{{TITLE}}" ? safeTitle : trimmedBody);
    }

    /// <summary>Menulis HTML (UTF-8 tanpa BOM) secara atomik. Melempar IOException/UnauthorizedAccessException bila gagal.</summary>
    public static void ExportToFile(string markdown, string outputPath, string? title = null, string? baseDirectory = null)
    {
        var html = ToHtmlDocument(markdown, title, baseDirectory);
        TextFileIO.Write(outputPath, html, new UTF8Encoding(false));
    }
}
