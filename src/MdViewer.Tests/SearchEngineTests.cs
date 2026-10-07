namespace MdViewer.Tests;

public class SearchEngineFindTests
{
    static readonly SearchOptions Plain = new();
    static readonly SearchOptions Case = new(MatchCase: true);
    static readonly SearchOptions Rx = new(UseRegex: true);
    static readonly SearchOptions RxCase = new(MatchCase: true, UseRegex: true);

    static (int Offset, int Length)[] Spans(IReadOnlyList<SearchMatch> m) => m.Select(x => (x.Offset, x.Length)).ToArray();

    [Fact]
    public void FindAll_Literal_ReturnsOrderedOffsets()
    {
        var m = SearchEngine.FindAll("a-b-a-b", "b", Plain);

        Assert.Equal(new[] { (2, 1), (6, 1) }, Spans(m));
    }

    [Theory]
    [InlineData("")]
    public void FindAll_EmptyQuery_ReturnsNothing(string query)
    {
        Assert.Empty(SearchEngine.FindAll("abc", query, Plain));
        Assert.Empty(SearchEngine.FindAll("abc", query, Rx));
    }

    [Fact]
    public void FindAll_NullQuery_ReturnsNothing()
    {
        Assert.Empty(SearchEngine.FindAll("abc", null!, Plain));
    }

    [Fact]
    public void FindAll_NullText_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SearchEngine.FindAll(null!, "a", Plain));
    }

    [Fact]
    public void FindAll_EmptyText_ReturnsNothing()
    {
        Assert.Empty(SearchEngine.FindAll("", "a", Plain));
        Assert.Empty(SearchEngine.FindAll("", "a", Rx));
    }

    [Fact]
    public void FindAll_QueryLongerThanText_ReturnsNothing()
    {
        Assert.Empty(SearchEngine.FindAll("ab", "abc", Plain));
    }

    [Fact]
    public void FindAll_QueryEqualToText_MatchesOnce()
    {
        Assert.Equal(new[] { (0, 3) }, Spans(SearchEngine.FindAll("abc", "abc", Plain)));
    }

    [Fact]
    public void FindAll_IgnoresCaseByDefault_AndHonoursMatchCase()
    {
        Assert.Equal(2, SearchEngine.FindAll("Foo foo", "FOO", Plain).Count);
        Assert.Empty(SearchEngine.FindAll("Foo foo", "FOO", Case));
        Assert.Single(SearchEngine.FindAll("Foo foo", "Foo", Case));
    }

    [Fact]
    public void FindAll_MatchesDoNotOverlap()
    {
        Assert.Equal(new[] { (0, 2), (2, 2) }, Spans(SearchEngine.FindAll("aaaaa", "aa", Plain)));
    }

    [Fact]
    public void FindAll_Literal_TreatsRegexMetacharactersAsText()
    {
        var m = SearchEngine.FindAll("a.b axb a.b", "a.b", Plain);

        Assert.Equal(new[] { (0, 3), (8, 3) }, Spans(m));
    }

    [Fact]
    public void FindAll_Literal_ReplacementIsStoredVerbatim_WithoutDollarExpansion()
    {
        var m = SearchEngine.FindAll("abc", "b", Plain, "$1");

        Assert.Equal("$1", m.Single().Replacement);
    }

    [Fact]
    public void FindAll_NoReplacement_GivesEmptyReplacementString()
    {
        Assert.Equal("", SearchEngine.FindAll("abc", "b", Plain).Single().Replacement);
        Assert.Equal("", SearchEngine.FindAll("abc", "b", Rx).Single().Replacement);
    }

    [Fact]
    public void FindAll_Literal_NonAsciiCaseInsensitive()
    {
        Assert.Single(SearchEngine.FindAll("ÜBER", "über", Plain));
    }

    [Fact]
    public void FindAll_Regex_UsesGroupsInReplacement()
    {
        var m = SearchEngine.FindAll("alice@example", @"(\w+)@(\w+)", Rx, "$2:$1");

        Assert.Equal("example:alice", m.Single().Replacement);
    }

    [Fact]
    public void FindAll_Regex_NamedGroupReplacement()
    {
        var m = SearchEngine.FindAll("2024-05", @"(?<y>\d+)-(?<m>\d+)", Rx, "${m}/${y}");

        Assert.Equal("05/2024", m.Single().Replacement);
    }

    [Fact]
    public void FindAll_Regex_CaseSensitivityFollowsOption()
    {
        Assert.Equal(2, SearchEngine.FindAll("Ab ab", "ab", Rx).Count);
        Assert.Single(SearchEngine.FindAll("Ab ab", "ab", RxCase));
    }

    [Fact]
    public void FindAll_Regex_AnchorsMatchAtEveryLine()
    {
        var m = SearchEngine.FindAll("a1\nb\na2", "^a", Rx);

        Assert.Equal(new[] { (0, 1), (5, 1) }, Spans(m));
    }

    [Fact]
    public void FindAll_Regex_ZeroLengthMatchesAreSkipped()
    {
        Assert.Empty(SearchEngine.FindAll("abc", "x*", Rx));
        Assert.Empty(SearchEngine.FindAll("abc\ndef", "^", Rx));
        Assert.Empty(SearchEngine.FindAll("abc", @"\b", Rx));
    }

    [Fact]
    public void FindAll_Regex_MixedZeroAndNonZeroLength_ReturnsOnlyNonZero()
    {
        var m = SearchEngine.FindAll("baab", "a*", Rx);

        Assert.Equal(new[] { (1, 2) }, Spans(m));
    }

    [Fact]
    public void FindAll_Regex_Unicode()
    {
        Assert.Equal(new[] { (2, 2) }, Spans(SearchEngine.FindAll("a 日本 b", @"\p{IsCJKUnifiedIdeographs}+", Rx)));
    }

    [Theory]
    [InlineData("(")]
    [InlineData("[a-")]
    [InlineData("*abc")]
    [InlineData(@"\")]
    [InlineData("(?<n>")]
    public void FindAll_InvalidRegex_ThrowsArgumentException(string pattern)
    {
        Assert.ThrowsAny<ArgumentException>(() => SearchEngine.FindAll("abc", pattern, Rx));
    }

    [Fact]
    public void FindAll_InvalidRegexInLiteralMode_IsJustText()
    {
        Assert.Single(SearchEngine.FindAll("a(b", "(", Plain));
    }

    [Fact]
    public void FindAll_Regex_InvalidReplacementGroup_ProducesLiteralText()
    {
        // $9 tidak ada grup-nya: .NET membiarkannya apa adanya, tidak melempar.
        var m = SearchEngine.FindAll("a", "a", Rx, "$9");

        Assert.Equal("$9", m.Single().Replacement);
    }

    // ---- MaxResults ----

    [Fact]
    public void FindAll_Literal_StopsAtMaxResults()
    {
        var text = new string('a', SearchEngine.MaxResults + 500);

        var m = SearchEngine.FindAll(text, "a", Plain);

        Assert.Equal(SearchEngine.MaxResults, m.Count);
        Assert.Equal(SearchEngine.MaxResults - 1, m[^1].Offset);
    }

    [Fact]
    public void FindAll_Regex_StopsAtMaxResults()
    {
        var text = new string('a', SearchEngine.MaxResults + 500);

        Assert.Equal(SearchEngine.MaxResults, SearchEngine.FindAll(text, "a", Rx).Count);
    }

    [Fact]
    public void FindAll_ExactlyMaxResultsMatches_AreAllReturned()
    {
        var text = new string('a', SearchEngine.MaxResults);

        Assert.Equal(SearchEngine.MaxResults, SearchEngine.FindAll(text, "a", Plain).Count);
    }

    // ---- Akhir baris Windows ----

    [Fact]
    public void FindAll_Regex_DollarMatchesBeforeCrLf()
    {
        var m = SearchEngine.FindAll("foo\r\nbar", "foo$", Rx);

        Assert.Single(m);
    }

    [Fact]
    public void FindAll_Regex_DollarMatchesBeforeLfOnly()
    {
        Assert.Single(SearchEngine.FindAll("foo\nbar", "foo$", Rx));
    }

    [Fact]
    public void FindAll_Regex_DollarMatchesAtEndOfCrLfLines_AndAtEndOfText()
    {
        var m = SearchEngine.FindAll("ab\r\ncb\r\nxb", "b$", Rx);

        Assert.Equal(new[] { (1, 1), (5, 1), (9, 1) }, Spans(m));
    }

    [Fact]
    public void FindAll_Regex_DotDoesNotCaptureCarriageReturn()
    {
        var m = SearchEngine.FindAll("ab\r\ncd\r\n", "^.+$", Rx);

        Assert.Equal(new[] { (0, 2), (4, 2) }, Spans(m));
    }

    [Fact]
    public void FindAll_Regex_EscapedDollarIsStillLiteral()
    {
        var m = SearchEngine.FindAll("cost $5\r\nfoo\r\n", @"\$5", Rx);

        Assert.Equal(new[] { (5, 2) }, Spans(m));
        Assert.Empty(SearchEngine.FindAll("foo\r\nbar", @"foo\$", Rx));
    }

    [Fact]
    public void FindAll_Regex_DollarInCharacterClassIsStillLiteral()
    {
        var m = SearchEngine.FindAll("a$b\r\nc", "[$]", Rx);

        Assert.Equal(new[] { (1, 1) }, Spans(m));
    }

    [Fact]
    public void FindAll_Regex_EscapedDotAndDotInClassAreStillLiteral()
    {
        Assert.Equal(new[] { (1, 1) }, Spans(SearchEngine.FindAll("a.b\r\n", @"\.", Rx)));
        Assert.Equal(new[] { (1, 1) }, Spans(SearchEngine.FindAll("a.b\r\n", "[.]", Rx)));
    }

    [Fact]
    public void FindAll_Regex_DollarInsideLookahead_MatchesBeforeCrLf()
    {
        var m = SearchEngine.FindAll("foo\r\nbar\r\n", "o(?=$)", Rx);

        Assert.Equal(new[] { (2, 1) }, Spans(m));
    }

    [Fact]
    public void FindAll_Regex_ExplicitCrLfInPatternStillWorks()
    {
        Assert.Equal(new[] { (3, 2) }, Spans(SearchEngine.FindAll("foo\r\nbar", @"\r\n", Rx)));
    }

    [Fact]
    public void FindAll_Regex_InlineSingleline_KeepsDotMatchingNewlines()
    {
        var m = SearchEngine.FindAll("a\nb", "(?s)a.b", Rx);

        Assert.Equal(new[] { (0, 3) }, Spans(m));
    }

    [Theory]
    [InlineData("foo", "foo")]
    [InlineData("foo$", @"foo(?=\r?$)")]
    [InlineData(@"\$", @"\$")]
    [InlineData("[$]", "[$]")]
    [InlineData("[^$.]", "[^$.]")]
    [InlineData("[]$]", "[]$]")]
    [InlineData("[a-z-[$]]$", @"[a-z-[$]](?=\r?$)")]
    [InlineData("a.b", @"a[^\r\n]b")]
    [InlineData(@"a\\$", @"a\\(?=\r?$)")]
    [InlineData("(?#a.b$)x.", @"(?#a.b$)x[^\r\n]")]
    [InlineData("(?x) a . # c", "(?x) a . # c")]
    public void NormalizeLineEndings_RewritesOnlyBareDollarAndDot(string pattern, string expected)
    {
        Assert.Equal(expected, SearchEngine.NormalizeLineEndings(pattern));
    }

    [Fact]
    public void FindAll_Regex_TotalDeadlineApplies_NotJustPerMatchTimeout()
    {
        var text = new string('a', 2_000_000);

        Assert.Throws<System.Text.RegularExpressions.RegexMatchTimeoutException>(() =>
            SearchEngine.FindAll(text, "a", new SearchOptions(UseRegex: true), null, int.MaxValue, TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void FindAll_MaxResultsParameter_LimitsAndCanBeLifted()
    {
        var text = new string('a', SearchEngine.MaxResults + 5);

        Assert.Equal(SearchEngine.MaxResults, SearchEngine.FindAll(text, "a", new SearchOptions()).Count);
        Assert.Equal(text.Length, SearchEngine.FindAll(text, "a", new SearchOptions(), null, int.MaxValue).Count);
        Assert.Equal(text.Length, SearchEngine.FindAll(text, "a", Rx, null, int.MaxValue).Count);
    }

    [Fact]
    public void FindAll_Regex_CaretMatchesAfterCrLf()
    {
        var m = SearchEngine.FindAll("a\r\nb\r\n", "^b", Rx);

        Assert.Equal(new[] { (3, 1) }, Spans(m));
    }
}

public class SearchEngineTryFindAllTests
{
    [Fact]
    public void TryFindAll_Valid_ReturnsTrueAndMatches()
    {
        var ok = SearchEngine.TryFindAll("abab", "ab", new SearchOptions(), out var matches, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void TryFindAll_EmptyQuery_IsSuccessWithNoMatches()
    {
        var ok = SearchEngine.TryFindAll("abab", "", new SearchOptions(UseRegex: true), out var matches, out var error);

        Assert.True(ok);
        Assert.Empty(matches);
        Assert.Null(error);
    }

    [Fact]
    public void TryFindAll_InvalidRegex_ReturnsFalseWithMessage_AndNoThrow()
    {
        var ok = SearchEngine.TryFindAll("abc", "(", new SearchOptions(UseRegex: true), out var matches, out var error);

        Assert.False(ok);
        Assert.Empty(matches);
        Assert.Equal("Regex tidak valid", error);
    }

    [Fact]
    public void TryFindAll_CatastrophicRegex_TimesOutWithMessage()
    {
        // Backtracking eksponensial; batas waktu 2 detik pada SearchEngine memutusnya.
        var text = new string('a', 64) + "!";

        var ok = SearchEngine.TryFindAll(text, "^(a+)+$", new SearchOptions(UseRegex: true), out var matches, out var error);

        Assert.False(ok);
        Assert.Empty(matches);
        Assert.Equal("Pencarian terlalu lama", error);
    }

    [Fact]
    public void FindAll_CatastrophicRegex_ThrowsRegexMatchTimeout()
    {
        var text = new string('a', 64) + "!";

        Assert.Throws<System.Text.RegularExpressions.RegexMatchTimeoutException>(
            () => SearchEngine.FindAll(text, "^(a+)+$", new SearchOptions(UseRegex: true)));
    }

    [Fact]
    public void TryFindAll_PassesReplacementThrough()
    {
        SearchEngine.TryFindAll("ab", "(a)", new SearchOptions(UseRegex: true), out var matches, out _, "<$1>");

        Assert.Equal("<a>", matches.Single().Replacement);
    }
}

public class SearchEngineIndexTests
{
    static IReadOnlyList<SearchMatch> At(params int[] offsets) =>
        offsets.Select(o => new SearchMatch(o, 2)).ToList();

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(5, 0)]
    [InlineData(6, 1)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    [InlineData(20, 2)]
    [InlineData(21, -1)]
    [InlineData(1000, -1)]
    public void IndexAtOrAfter_FindsFirstMatchAtOrPastOffset(int offset, int expected)
    {
        Assert.Equal(expected, SearchEngine.IndexAtOrAfter(At(5, 10, 20), offset));
    }

    [Fact]
    public void IndexAtOrAfter_EmptyList_ReturnsMinusOne()
    {
        Assert.Equal(-1, SearchEngine.IndexAtOrAfter(At(), 0));
    }

    [Fact]
    public void IndexAtOrAfter_SingleElement()
    {
        Assert.Equal(0, SearchEngine.IndexAtOrAfter(At(3), 3));
        Assert.Equal(-1, SearchEngine.IndexAtOrAfter(At(3), 4));
    }

    [Theory]
    [InlineData(-1, -1)]
    [InlineData(0, -1)]
    [InlineData(5, -1)]
    [InlineData(6, 0)]
    [InlineData(10, 0)]
    [InlineData(11, 1)]
    [InlineData(20, 1)]
    [InlineData(21, 2)]
    [InlineData(1000, 2)]
    public void IndexBefore_FindsLastMatchStrictlyBeforeOffset(int offset, int expected)
    {
        Assert.Equal(expected, SearchEngine.IndexBefore(At(5, 10, 20), offset));
    }

    [Fact]
    public void IndexBefore_EmptyList_ReturnsMinusOne()
    {
        Assert.Equal(-1, SearchEngine.IndexBefore(At(), 10));
    }

    [Fact]
    public void IndexOfExact_RequiresSameOffsetAndLength()
    {
        var m = At(5, 10);

        Assert.Equal(1, SearchEngine.IndexOfExact(m, 10, 2));
        Assert.Equal(-1, SearchEngine.IndexOfExact(m, 10, 3));
        Assert.Equal(-1, SearchEngine.IndexOfExact(m, 11, 2));
        Assert.Equal(-1, SearchEngine.IndexOfExact(m, 99, 2));
        Assert.Equal(-1, SearchEngine.IndexOfExact(At(), 0, 0));
    }

    [Fact]
    public void Navigation_WrapAroundIsPossibleWithTheseHelpers()
    {
        var m = SearchEngine.FindAll("x a x a x", "a", new SearchOptions());

        Assert.Equal(1, SearchEngine.IndexAtOrAfter(m, 4));
        Assert.Equal(0, SearchEngine.IndexBefore(m, 4));
        Assert.Equal(-1, SearchEngine.IndexAtOrAfter(m, 7));
        Assert.Equal(1, SearchEngine.IndexBefore(m, 100));
    }
}

public class SearchEngineReplaceAllTests
{
    static string Replace(string text, string query, string repl, SearchOptions o, out int count) =>
        SearchEngine.ReplaceAll(text, query, repl, o, out count);

    [Fact]
    public void ReplaceAll_Literal_ReplacesEveryOccurrenceAndCounts()
    {
        var r = Replace("a b a b a", "a", "X", new SearchOptions(), out var count);

        Assert.Equal("X b X b X", r);
        Assert.Equal(3, count);
    }

    [Fact]
    public void ReplaceAll_NoMatch_ReturnsOriginalTextAndZero()
    {
        var r = Replace("abc", "z", "X", new SearchOptions(), out var count);

        Assert.Equal("abc", r);
        Assert.Equal(0, count);
    }

    [Fact]
    public void ReplaceAll_EmptyQuery_DoesNothing()
    {
        var r = Replace("abc", "", "X", new SearchOptions(), out var count);

        Assert.Equal("abc", r);
        Assert.Equal(0, count);
    }

    [Fact]
    public void ReplaceAll_EmptyReplacement_DeletesMatches()
    {
        Assert.Equal("ac", Replace("abc", "b", "", new SearchOptions(), out var c));
        Assert.Equal(1, c);
    }

    [Fact]
    public void ReplaceAll_ReplacementContainingQuery_IsNotReprocessed()
    {
        Assert.Equal("aa aa", Replace("a a", "a", "aa", new SearchOptions(), out var c));
        Assert.Equal(2, c);
    }

    [Fact]
    public void ReplaceAll_Literal_DollarSignsStayLiteral()
    {
        Assert.Equal("$1-$&", Replace("x", "x", "$1-$&", new SearchOptions(), out _));
    }

    [Fact]
    public void ReplaceAll_Regex_ExpandsCaptureGroups()
    {
        var r = Replace("john smith, jane doe", @"(\w+) (\w+)", "$2 $1", new SearchOptions(UseRegex: true), out var c);

        Assert.Equal("smith john, doe jane", r);
        Assert.Equal(2, c);
    }

    [Fact]
    public void ReplaceAll_Regex_DollarDollarProducesLiteralDollar()
    {
        Assert.Equal("$5", Replace("5", @"\d", "$$$0", new SearchOptions(UseRegex: true), out _));
    }

    [Fact]
    public void ReplaceAll_Regex_OnlyZeroLengthMatches_ChangesNothing()
    {
        var r = Replace("abc", "x*", "-", new SearchOptions(UseRegex: true), out var c);

        Assert.Equal("abc", r);
        Assert.Equal(0, c);
    }

    [Fact]
    public void ReplaceAll_IgnoreCase_ReplacesAllCaseVariants()
    {
        Assert.Equal("X X X", Replace("Foo FOO foo", "foo", "X", new SearchOptions(), out var c));
        Assert.Equal(3, c);
    }

    [Fact]
    public void ReplaceAll_MatchCase_LeavesOtherCases()
    {
        Assert.Equal("X FOO foo", Replace("Foo FOO foo", "Foo", "X", new SearchOptions(MatchCase: true), out _));
    }

    [Fact]
    public void ReplaceAll_PreservesTextBetweenAndAfterMatches_IncludingNewlines()
    {
        Assert.Equal("#\r\n#\r\n#", Replace("a\r\nb\r\nc", "[abc]", "#", new SearchOptions(UseRegex: true), out var c));
        Assert.Equal(3, c);
    }

    [Fact]
    public void ReplaceAll_Regex_MultilineAnchorsPrefixEveryLine()
    {
        Assert.Equal("> a\n> b", Replace("a\nb", @"^\w", "> $0", new SearchOptions(UseRegex: true), out var c));
        Assert.Equal(2, c);
    }

    [Fact]
    public void ReplaceAll_InvalidRegex_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => Replace("abc", "(", "x", new SearchOptions(UseRegex: true), out _));
    }

    [Fact]
    public void ReplaceAll_SurrogatePairs_AreHandledByUtf16Offsets()
    {
        Assert.Equal("a-b-c", Replace("a😀b😀c", "😀", "-", new SearchOptions(), out var c));
        Assert.Equal(2, c);
    }

    [Fact]
    public void ReplaceAll_Regex_MoreThanMaxResults_ReplacesEveryOccurrence()
    {
        var text = new string('a', SearchEngine.MaxResults + 5);

        var r = Replace(text, "a", "b", new SearchOptions(UseRegex: true), out var count);

        Assert.Equal(new string('b', text.Length), r);
        Assert.Equal(text.Length, count);
    }

    [Fact]
    public void ReplaceAll_MoreThanMaxResults_ReplacesEveryOccurrence()
    {
        var text = new string('a', SearchEngine.MaxResults + 5);

        var r = Replace(text, "a", "b", new SearchOptions(), out var count);

        Assert.Equal(new string('b', text.Length), r);
        Assert.Equal(text.Length, count);
    }
}
