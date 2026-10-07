using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;

namespace MdViewer;

public enum MarkdownFormat { Bold, Italic, InlineCode, Heading, BulletList, Quote, Link, Image }

/// <summary>Rentang seleksi (offset awal + panjang) hasil sebuah operasi pengeditan.</summary>
public readonly record struct SelectionRange(int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>
/// Helper pembungkus seleksi untuk toolbar/pintasan format Markdown. Inti bekerja pada <see cref="TextDocument"/>
/// (mudah diuji); setiap operasi dibungkus <see cref="TextDocument.BeginUpdate"/> sehingga menjadi satu langkah Undo.
/// </summary>
public static class MarkdownEditing
{
    const string DefaultUrl = "https://";

    static readonly Regex HeadingPrefix = new(@"^[ ]{0,3}#{1,6}(?:[ \t]+|$)", RegexOptions.Compiled);
    static readonly Regex HeadingLevelPattern = new(@"^[ ]{0,3}(#{1,6})(?:[ \t]|$)", RegexOptions.Compiled);

    // ---- Penerapan ke editor ----

    /// <summary>Menerapkan format ke seleksi/caret editor lalu menyeleksi hasilnya. headingLevel: 0 = teks biasa, 1..6.</summary>
    public static void Apply(TextEditor editor, MarkdownFormat format, int headingLevel = 1)
    {
        var selection = new SelectionRange(editor.SelectionStart, editor.SelectionLength);
        var document = editor.Document;

        var result = format switch
        {
            MarkdownFormat.Bold => ToggleInline(document, selection, "**"),
            MarkdownFormat.Italic => ToggleInline(document, selection, "*"),
            MarkdownFormat.InlineCode => ToggleInline(document, selection, "`"),
            MarkdownFormat.Heading => SetHeading(document, selection, headingLevel),
            MarkdownFormat.BulletList => ToggleLinePrefix(document, selection, "- "),
            MarkdownFormat.Quote => ToggleLinePrefix(document, selection, "> "),
            MarkdownFormat.Link => InsertLink(document, selection, image: false),
            MarkdownFormat.Image => InsertLink(document, selection, image: true),
            _ => selection,
        };

        editor.Select(result.Start, result.Length);
        editor.TextArea.Caret.BringCaretToView();
    }

    // ---- Inline: tebal, miring, kode ----

    /// <summary>
    /// Membungkus seleksi dengan marker, atau melepasnya bila sudah terbungkus (marker di luar atau di dalam seleksi).
    /// Tanpa seleksi: kata di bawah caret dibungkus; bila tak ada kata, disisipkan marker + placeholder (terseleksi).
    /// </summary>
    public static SelectionRange ToggleInline(TextDocument document, SelectionRange selection, string marker, string placeholder = "teks")
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(marker);

        var (start, length) = Clamp(document, selection);
        document.BeginUpdate();
        try
        {
            if (length == 0 && WordBounds(document, start) is { } word)
            {
                start = word.Start;
                length = word.Length;
            }

            return length == 0
                ? ToggleAtCaret(document, start, marker, placeholder)
                : ToggleAroundText(document, start, length, marker);
        }
        finally
        {
            document.EndUpdate();
        }
    }

    static SelectionRange ToggleAroundText(TextDocument document, int start, int length, string marker)
    {
        var m = marker.Length;
        var end = start + length;

        // 1) Marker persis di luar seleksi: **|teks|**
        var before = RunBefore(document, start, marker[0]);
        var after = RunAfter(document, end, marker[0]);
        if (IsWrapped(marker, Math.Min(before, after)))
        {
            document.Remove(end, m);
            document.Remove(start - m, m);
            return new SelectionRange(start - m, length);
        }

        // 2) Marker ikut terseleksi: |**teks**|
        var text = document.GetText(start, length);
        var lead = text.TakeWhile(c => c == marker[0]).Count();
        var trail = text.Reverse().TakeWhile(c => c == marker[0]).Count();
        if (lead < length && length >= 2 * m && IsWrapped(marker, Math.Min(lead, trail)))
        {
            document.Replace(start, length, text[m..^m]);
            return new SelectionRange(start, length - 2 * m);
        }

        // 3) Belum terbungkus: bungkus.
        document.Insert(end, marker);
        document.Insert(start, marker);
        return new SelectionRange(start + m, length);
    }

    static SelectionRange ToggleAtCaret(TextDocument document, int caret, string marker, string placeholder)
    {
        var m = marker.Length;
        var before = RunBefore(document, caret, marker[0]);
        var after = RunAfter(document, caret, marker[0]);

        // Caret berada di antara pasangan marker kosong (**|**): lepas.
        if (IsWrapped(marker, Math.Min(before, after)))
        {
            document.Remove(caret, m);
            document.Remove(caret - m, m);
            return new SelectionRange(caret - m, 0);
        }

        document.Insert(caret, marker + placeholder + marker);
        return new SelectionRange(caret + m, placeholder.Length);
    }

    // Satu '*' = miring, dua = tebal, tiga = keduanya; marker lain cukup sepanjang marker-nya.
    static bool IsWrapped(string marker, int run) =>
        marker == "*" ? run % 2 == 1 : run >= marker.Length;

    static int RunBefore(TextDocument document, int offset, char c)
    {
        var count = 0;
        while (offset - count > 0 && document.GetCharAt(offset - count - 1) == c) count++;
        return count;
    }

    static int RunAfter(TextDocument document, int offset, char c)
    {
        var count = 0;
        while (offset + count < document.TextLength && document.GetCharAt(offset + count) == c) count++;
        return count;
    }

    static (int Start, int Length)? WordBounds(TextDocument document, int caret)
    {
        var start = caret;
        while (start > 0 && IsWordChar(document.GetCharAt(start - 1))) start--;
        var end = caret;
        while (end < document.TextLength && IsWordChar(document.GetCharAt(end))) end++;
        return end > start ? (start, end - start) : null;
    }

    static bool IsWordChar(char c) => char.IsLetterOrDigit(c);

    // ---- Tautan & gambar ----

    /// <summary>
    /// Seleksi menjadi teks tautan ([teks](https://)) dengan URL terseleksi; bila seleksi sudah berupa URL, URL itu dipakai
    /// dan teks tautan yang terseleksi. Tanpa seleksi: disisipkan templat dengan teks terseleksi.
    /// </summary>
    public static SelectionRange InsertLink(TextDocument document, SelectionRange selection, bool image)
    {
        ArgumentNullException.ThrowIfNull(document);

        var (start, length) = Clamp(document, selection);
        var open = image ? "![" : "[";
        var selected = document.GetText(start, length);

        string label, url;
        if (length == 0) (label, url) = (image ? "deskripsi" : "teks tautan", DefaultUrl);
        else if (LooksLikeUrl(selected)) (label, url) = (image ? "deskripsi" : "teks tautan", selected.Trim());
        else (label, url) = (selected, DefaultUrl);

        var replacement = $"{open}{label}]({url})";
        document.BeginUpdate();
        try { document.Replace(start, length, replacement); }
        finally { document.EndUpdate(); }

        return length > 0 && !LooksLikeUrl(selected)
            ? new SelectionRange(start + open.Length + label.Length + 2, url.Length) // URL placeholder
            : new SelectionRange(start + open.Length, label.Length);                 // teks tautan
    }

    static bool LooksLikeUrl(string text)
    {
        text = text.Trim();
        return text.Length > 0 && !text.Any(char.IsWhiteSpace)
               && Uri.TryCreate(text, UriKind.Absolute, out var uri)
               && uri.Scheme is "http" or "https" or "mailto";
    }

    // ---- Per baris: heading, daftar, kutipan ----

    /// <summary>
    /// Mengatur heading pada baris-baris yang tersentuh seleksi. level 0 = teks biasa. Bila semua baris sudah
    /// ber-level sama, heading dilepas (toggle).
    /// </summary>
    public static SelectionRange SetHeading(TextDocument document, SelectionRange selection, int level)
    {
        ArgumentNullException.ThrowIfNull(document);
        level = Math.Clamp(level, 0, 6);

        var lines = SelectedLines(document, selection);
        var contentLines = lines.Where(l => !IsBlank(document, l)).ToList();
        if (contentLines.Count == 0 && lines.Count == 1) contentLines = lines;

        // Toggle: semua baris sudah di level yang diminta -> lepas heading.
        if (level > 0 && contentLines.Count > 0 && contentLines.All(l => HeadingLevel(document.GetText(l)) == level)) level = 0;

        return RewriteLines(document, selection, lines, contentLines, text =>
        {
            var stripped = HeadingPrefix.Replace(text, "");
            return level == 0 ? stripped : new string('#', level) + " " + stripped;
        });
    }

    /// <summary>Menambah awalan (mis. "- " atau "&gt; ") pada baris terpilih, atau melepasnya bila semua sudah berawalan.</summary>
    public static SelectionRange ToggleLinePrefix(TextDocument document, SelectionRange selection, string prefix)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        var lines = SelectedLines(document, selection);
        var contentLines = lines.Where(l => !IsBlank(document, l)).ToList();
        if (contentLines.Count == 0 && lines.Count == 1) contentLines = lines;

        var allPrefixed = contentLines.Count > 0 && contentLines.All(l => StartsWithPrefix(document.GetText(l), prefix));
        return RewriteLines(document, selection, lines, contentLines, text =>
        {
            if (allPrefixed) return RemovePrefix(text, prefix);
            return StartsWithPrefix(text, prefix) ? text : prefix + text;
        });
    }

    static bool StartsWithPrefix(string line, string prefix)
    {
        if (line.StartsWith(prefix, StringComparison.Ordinal)) return true;
        // Penanda daftar setara.
        return prefix == "- " && (line.StartsWith("* ", StringComparison.Ordinal) || line.StartsWith("+ ", StringComparison.Ordinal));
    }

    static string RemovePrefix(string line, string prefix) =>
        line.StartsWith(prefix, StringComparison.Ordinal) ? line[prefix.Length..]
        : StartsWithPrefix(line, prefix) ? line[2..]
        : line;

    static int HeadingLevel(string line) =>
        HeadingLevelPattern.Match(line) is { Success: true } m ? m.Groups[1].Length : 0;

    static List<DocumentLine> SelectedLines(TextDocument document, SelectionRange selection)
    {
        var (start, length) = Clamp(document, selection);
        var first = document.GetLineByOffset(start);
        var last = document.GetLineByOffset(start + length);
        // Seleksi yang berakhir tepat di awal baris berikutnya tidak menyertakan baris itu.
        if (length > 0 && last.Offset == start + length && last.LineNumber > first.LineNumber) last = last.PreviousLine;

        var lines = new List<DocumentLine>();
        for (var line = first; line is not null; line = line.NextLine)
        {
            lines.Add(line);
            if (line == last) break;
        }
        return lines;
    }

    static bool IsBlank(TextDocument document, DocumentLine line) =>
        string.IsNullOrWhiteSpace(document.GetText(line));

    static SelectionRange RewriteLines(TextDocument document, SelectionRange selection, List<DocumentLine> lines,
        List<DocumentLine> targets, Func<string, string> transform)
    {
        var (start, length) = Clamp(document, selection);
        var firstOffset = lines[0].Offset;
        var caretDelta = 0;
        var caretLineOffset = document.GetLineByOffset(start).Offset;

        document.BeginUpdate();
        try
        {
            // Dari bawah ke atas agar offset baris di atasnya tidak bergeser.
            foreach (var line in Enumerable.Reverse(targets))
            {
                var oldText = document.GetText(line);
                var newText = transform(oldText);
                if (newText == oldText) continue;

                document.Replace(line.Offset, line.Length, newText);
                if (line.Offset == caretLineOffset) caretDelta = newText.Length - oldText.Length;
            }
        }
        finally
        {
            document.EndUpdate();
        }

        if (length == 0)
        {
            var line = document.GetLineByOffset(Math.Min(firstOffset, document.TextLength));
            var caret = Math.Clamp(start + caretDelta, line.Offset, line.EndOffset);
            return new SelectionRange(caret, 0);
        }

        var lastLine = document.GetLineByOffset(Math.Min(
            lines[^1].Offset, Math.Max(0, document.TextLength)));
        return new SelectionRange(firstOffset, lastLine.EndOffset - firstOffset);
    }

    static (int Start, int Length) Clamp(TextDocument document, SelectionRange selection)
    {
        var start = Math.Clamp(selection.Start, 0, document.TextLength);
        var length = Math.Clamp(selection.Length, 0, document.TextLength - start);
        return (start, length);
    }
}
