using System.Globalization;
using ICSharpCode.AvalonEdit.Document;

namespace Makdon;

/// <summary>Hitung kata dan karakter untuk status bar. Murni, tanpa UI.</summary>
public static class TextStats
{
    // Ukuran potongan saat menghitung langsung dari dokumen: menghindari salinan string utuh (LOH) untuk dokumen besar.
    const int ChunkChars = 64 * 1024;

    static readonly CultureInfo Indonesian = CultureInfo.GetCultureInfo("id-ID");

    /// <summary>Kata = urutan karakter bukan-spasi; karakter = semua karakter selain pemisah baris (spasi dihitung).</summary>
    public static (int Words, int Characters) Count(string? text)
    {
        if (string.IsNullOrEmpty(text)) return (0, 0);

        int words = 0, characters = 0;
        var inWord = false;
        Accumulate(text, ref inWord, ref words, ref characters);
        return (words, characters);
    }

    /// <summary>Seperti <see cref="Count(string?)"/> tetapi membaca sumber teks per potongan (tanpa menyalin seluruh isi sekaligus).</summary>
    public static (int Words, int Characters) CountSource(ITextSource source)
    {
        int words = 0, characters = 0;
        var inWord = false;
        for (var offset = 0; offset < source.TextLength; offset += ChunkChars)
        {
            var chunk = source.GetText(offset, Math.Min(ChunkChars, source.TextLength - offset));
            Accumulate(chunk, ref inWord, ref words, ref characters);
        }
        return (words, characters);
    }

    static void Accumulate(ReadOnlySpan<char> text, ref bool inWord, ref int words, ref int characters)
    {
        foreach (var c in text)
        {
            if (c is '\r' or '\n')
            {
                inWord = false;
                continue;
            }

            characters++;
            if (char.IsWhiteSpace(c)) inWord = false;
            else if (!inWord)
            {
                inWord = true;
                words++;
            }
        }
    }

    public static string Format(int words, int characters) =>
        $"{words.ToString("N0", Indonesian)} kata · {characters.ToString("N0", Indonesian)} karakter";
}
