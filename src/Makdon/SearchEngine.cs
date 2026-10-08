using System.Text.RegularExpressions;

namespace Makdon;

/// <summary>Satu hasil pencarian. Replacement sudah dihitung (mendukung $1/${nama} pada mode regex) bila diminta.</summary>
public readonly record struct SearchMatch(int Offset, int Length, string Replacement = "")
{
    public int End => Offset + Length;
}

public readonly record struct SearchOptions(bool MatchCase = false, bool UseRegex = false);

/// <summary>Logika cari &amp; ganti murni (tanpa UI) untuk panel Cari/Ganti.</summary>
public static class SearchEngine
{
    /// <summary>Pesan galat <see cref="TryFindAll"/> bila pencarian regex kena batas waktu (per match atau total).</summary>
    public const string TimeoutError = "Pencarian terlalu lama";

    /// <summary>Batas jumlah hasil agar dokumen besar + pola longgar tidak membekukan UI.</summary>
    public const int MaxResults = 20_000;

    // Batas per pemanggilan Match (diberlakukan Regex) dan batas total satu pencarian (diperiksa di loop FindAll),
    // supaya pola yang lambat di banyak titik tidak membekukan UI sebesar (jumlah match x batas per match).
    static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);
    static readonly TimeSpan DefaultTotalTimeout = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Mencari semua kemunculan (tidak tumpang tindih, urut). Query kosong = tanpa hasil.
    /// Mode regex: pola tidak valid melempar <see cref="ArgumentException"/>; terlalu lama (per match atau total)
    /// melempar <see cref="RegexMatchTimeoutException"/>. Hasil dengan panjang nol dilewati.
    /// </summary>
    /// <param name="maxResults">Batas jumlah hasil (<see cref="MaxResults"/> untuk penanda/daftar; int.MaxValue untuk Ganti Semua).</param>
    /// <param name="totalTimeout">Batas waktu total mode regex; null = bawaan (4 detik).</param>
    public static IReadOnlyList<SearchMatch> FindAll(string text, string query, SearchOptions options, string? replacement = null,
        int maxResults = MaxResults, TimeSpan? totalTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrEmpty(query)) return [];

        var results = new List<SearchMatch>();
        if (options.UseRegex)
        {
            var regexOptions = RegexOptions.Multiline | RegexOptions.CultureInvariant
                               | (options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
            var regex = new Regex(NormalizeLineEndings(query), regexOptions, RegexTimeout);
            var limit = totalTimeout ?? DefaultTotalTimeout;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (var match = regex.Match(text); match.Success; match = match.NextMatch())
            {
                // Tiap iterasi: satu NextMatch bisa memakan sampai RegexTimeout, jadi memeriksa tiap 64 iterasi
                // membiarkan total mencapai puluhan kali batas.
                if (clock.Elapsed > limit)
                    throw new RegexMatchTimeoutException(text, query, limit);
                if (match.Length == 0) continue;
                results.Add(new SearchMatch(match.Index, match.Length, replacement is null ? "" : match.Result(replacement)));
                if (results.Count >= maxResults) break;
            }
            return results;
        }

        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var position = 0;
        while (position <= text.Length - query.Length)
        {
            var index = text.IndexOf(query, position, comparison);
            if (index < 0) break;
            results.Add(new SearchMatch(index, query.Length, replacement ?? ""));
            if (results.Count >= maxResults) break;
            position = index + query.Length;
        }
        return results;
    }

    /// <summary>
    /// Membuat pola regex sadar akhir baris Windows: di luar kelas karakter dan escape, "$" menjadi <c>(?=\r?$)</c>
    /// (di mode Multiline "$" saja hanya cocok sebelum \n, bukan sebelum \r\n) dan "." menjadi <c>[^\r\n]</c>
    /// (tidak menangkap \r). Escape (<c>\$</c>, <c>\.</c>), kelas karakter (<c>[$]</c>, <c>[.]</c>), komentar
    /// <c>(?#...)</c>, dan sisanya dibiarkan apa adanya. Bila pola memakai flag inline x (komentar bebas) pola tidak diubah;
    /// setelah flag inline s ("." menangkap baris baru) "." tidak diubah lagi, sampai grup pembungkusnya ditutup
    /// atau flag dimatikan ((?-s)); (?s:...) hanya berlaku di dalam grupnya.
    /// </summary>
    internal static string NormalizeLineEndings(string pattern)
    {
        if (pattern.IndexOfAny(['$', '.']) < 0) return pattern;

        var result = new System.Text.StringBuilder(pattern.Length + 16);
        var classDepth = 0;
        var classStart = -1; // indeks karakter pertama kelas terdalam ("-[" di posisi ini bukan subtraksi)
        var dotAll = false;
        // Flag s berlaku sampai grup tempat ia dinyalakan ditutup: nilai sebelum tiap grup disimpan di sini dan dipulihkan oleh ")".
        var dotAllScopes = new Stack<bool>();
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\')
            {
                result.Append(c);
                if (i + 1 < pattern.Length) result.Append(pattern[++i]);
                continue;
            }

            if (classDepth > 0)
            {
                // Di .NET hanya "-[" (subtraksi: [a-z-[aeiou]]) yang membuka kelas bersarang; "[" lain di dalam kelas
                // adalah literal (idiom "[[]" untuk "["). "]" menutup kelas terdalam.
                if (c == '-' && i != classStart && i + 1 < pattern.Length && pattern[i + 1] == '[')
                {
                    result.Append("-[");
                    i = OpenClass(pattern, i + 1, result, out classStart);
                    classDepth++;
                }
                else
                {
                    if (c == ']') classDepth--;
                    result.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '[':
                    classDepth = 1;
                    result.Append(c);
                    i = OpenClass(pattern, i, result, out classStart);
                    break;
                case '(' when pattern.AsSpan(i).StartsWith("(?#"):
                    var close = pattern.IndexOf(')', i);
                    var end = close < 0 ? pattern.Length : close + 1;
                    result.Append(pattern, i, end - i);
                    i = end - 1;
                    break;
                case '(':
                    var scoped = dotAll;
                    if (i + 1 < pattern.Length && pattern[i + 1] == '?')
                    {
                        // Flag inline seperti (?i) / (?s:...) / (?-s).
                        var j = i + 2;
                        var enabled = true;
                        var newDotAll = dotAll;
                        while (j < pattern.Length && (char.IsAsciiLetter(pattern[j]) || pattern[j] == '-'))
                        {
                            if (pattern[j] == '-') enabled = false;
                            else if (enabled && pattern[j] == 'x') return pattern;
                            else if (pattern[j] == 's') newDotAll = enabled;
                            j++;
                        }

                        if (j > i + 2 && j < pattern.Length && pattern[j] == ')')
                        {
                            // "(?s)" bukan grup: berlaku untuk sisa grup yang membungkusnya.
                            dotAll = newDotAll;
                            result.Append(pattern, i, j - i + 1);
                            i = j;
                            break;
                        }
                        if (j > i + 2 && j < pattern.Length && pattern[j] == ':') dotAll = newDotAll; // "(?s:...)": hanya di dalam grup
                        result.Append(pattern, i, 2);
                        i++;
                    }
                    else
                    {
                        result.Append(c);
                    }
                    dotAllScopes.Push(scoped);
                    break;
                case ')':
                    if (dotAllScopes.Count > 0) dotAll = dotAllScopes.Pop();
                    result.Append(c);
                    break;
                case '$':
                    result.Append(@"(?=\r?$)");
                    break;
                case '.' when !dotAll:
                    result.Append(@"[^\r\n]");
                    break;
                default:
                    result.Append(c);
                    break;
            }
        }
        return result.ToString();
    }

    // Setelah "[" di indeks open: "^" dan "]" pertama adalah bagian isi kelas ("]" tepat di awal adalah literal, bukan penutup).
    // Mengembalikan indeks karakter terakhir yang dikonsumsi; contentStart = indeks karakter pertama isi kelas.
    static int OpenClass(string pattern, int open, System.Text.StringBuilder result, out int contentStart)
    {
        var i = open;
        if (i + 1 < pattern.Length && pattern[i + 1] == '^') result.Append(pattern[++i]);
        contentStart = i + 1;
        if (i + 1 < pattern.Length && pattern[i + 1] == ']') result.Append(pattern[++i]);
        return i;
    }

    /// <summary>Seperti <see cref="FindAll"/> tetapi galat pola dikembalikan sebagai pesan, bukan pengecualian.</summary>
    public static bool TryFindAll(string text, string query, SearchOptions options, out IReadOnlyList<SearchMatch> matches,
        out string? error, string? replacement = null, int maxResults = MaxResults)
    {
        try
        {
            matches = FindAll(text, query, options, replacement, maxResults);
            error = null;
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            matches = [];
            error = TimeoutError;
            return false;
        }
        catch (ArgumentException)
        {
            matches = [];
            error = "Regex tidak valid";
            return false;
        }
    }

    /// <summary>Indeks hasil pertama yang Offset-nya &gt;= offset, atau -1 bila tidak ada. matches harus terurut.</summary>
    public static int IndexAtOrAfter(IReadOnlyList<SearchMatch> matches, int offset)
    {
        int low = 0, high = matches.Count;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (matches[mid].Offset < offset) low = mid + 1;
            else high = mid;
        }
        return low < matches.Count ? low : -1;
    }

    /// <summary>Indeks hasil terakhir yang Offset-nya &lt; offset, atau -1 bila tidak ada.</summary>
    public static int IndexBefore(IReadOnlyList<SearchMatch> matches, int offset)
    {
        var next = IndexAtOrAfter(matches, offset);
        var index = next < 0 ? matches.Count - 1 : next - 1;
        return index;
    }

    /// <summary>Indeks hasil yang persis sama dengan rentang (offset, length), atau -1.</summary>
    public static int IndexOfExact(IReadOnlyList<SearchMatch> matches, int offset, int length)
    {
        var index = IndexAtOrAfter(matches, offset);
        return index >= 0 && matches[index].Offset == offset && matches[index].Length == length ? index : -1;
    }

    /// <summary>Mengganti semua hasil dan mengembalikan teks baru (jumlah ganti lewat <paramref name="count"/>).</summary>
    public static string ReplaceAll(string text, string query, string replacement, SearchOptions options, out int count)
    {
        // Tanpa batas MaxResults: batas itu hanya untuk penanda/daftar hasil, bukan untuk penggantian.
        var matches = FindAll(text, query, options, replacement, int.MaxValue);
        count = matches.Count;
        if (matches.Count == 0) return text;

        var builder = new System.Text.StringBuilder(text.Length);
        var position = 0;
        foreach (var match in matches)
        {
            builder.Append(text, position, match.Offset - position).Append(match.Replacement);
            position = match.End;
        }
        return builder.Append(text, position, text.Length - position).ToString();
    }
}
