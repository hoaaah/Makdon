using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Makdon.Tests.Support;

namespace Makdon.Tests;

public class MarkdownEditingInlineTests
{
    static TextDocument Doc(string text) => new(text);

    static SelectionRange Sel(int start, int length = 0) => new(start, length);

    // ---- Seleksi biasa ----

    [Fact]
    public void ToggleInline_WrapsSelection_AndSelectsInnerText()
    {
        var doc = Doc("hello world");

        var r = MarkdownEditing.ToggleInline(doc, Sel(0, 5), "**");

        Assert.Equal("**hello** world", doc.Text);
        Assert.Equal(Sel(2, 5), r);
    }

    [Fact]
    public void ToggleInline_SelectionWithMarkersOutside_Unwraps()
    {
        var doc = Doc("**hello** world");

        var r = MarkdownEditing.ToggleInline(doc, Sel(2, 5), "**");

        Assert.Equal("hello world", doc.Text);
        Assert.Equal(Sel(0, 5), r);
    }

    [Fact]
    public void ToggleInline_SelectionIncludingMarkers_Unwraps()
    {
        var doc = Doc("**hello** world");

        var r = MarkdownEditing.ToggleInline(doc, Sel(0, 9), "**");

        Assert.Equal("hello world", doc.Text);
        Assert.Equal(Sel(0, 5), r);
    }

    [Fact]
    public void ToggleInline_WrapThenToggleAgain_RestoresOriginalText()
    {
        var doc = Doc("alpha beta gamma");

        var first = MarkdownEditing.ToggleInline(doc, Sel(6, 4), "**");
        MarkdownEditing.ToggleInline(doc, first, "**");

        Assert.Equal("alpha beta gamma", doc.Text);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("**")]
    [InlineData("`")]
    public void ToggleInline_SelectionOfOnlyMarkerCharacters_DoesNotCorruptOrThrow(string marker)
    {
        var doc = Doc(new string(marker[0], 2));

        var ex = Record.Exception(() => MarkdownEditing.ToggleInline(doc, Sel(0, 2), marker));

        Assert.Null(ex);
    }

    [Fact]
    public void ToggleInline_InlineCodeMarker()
    {
        var doc = Doc("x = 1");

        MarkdownEditing.ToggleInline(doc, Sel(0, 1), "`");

        Assert.Equal("`x` = 1", doc.Text);
    }

    [Fact]
    public void ToggleInline_MultiLineSelection_WrapsWholeSelection()
    {
        var doc = Doc("satu\ndua");

        var r = MarkdownEditing.ToggleInline(doc, Sel(0, 8), "**");

        Assert.Equal("**satu\ndua**", doc.Text);
        Assert.Equal(Sel(2, 8), r);
    }

    [Fact]
    public void ToggleInline_OnlyTouchesSelectedPartOfLargerText()
    {
        var doc = Doc("a *b* c");

        MarkdownEditing.ToggleInline(doc, Sel(3, 1), "**");

        Assert.Equal("a ***b*** c", doc.Text);
    }

    // ---- * dan ** bertumpuk ----

    [Fact]
    public void ToggleInline_ItalicOnBoldText_AddsItalicInsteadOfRemovingBold()
    {
        var doc = Doc("**bold**");

        var r = MarkdownEditing.ToggleInline(doc, Sel(2, 4), "*");

        Assert.Equal("***bold***", doc.Text);
        Assert.Equal(Sel(3, 4), r);
    }

    [Fact]
    public void ToggleInline_ItalicOnBoldItalicText_RemovesOnlyItalic()
    {
        var doc = Doc("***bold***");

        var r = MarkdownEditing.ToggleInline(doc, Sel(3, 4), "*");

        Assert.Equal("**bold**", doc.Text);
        Assert.Equal(Sel(2, 4), r);
    }

    [Fact]
    public void ToggleInline_BoldOnBoldItalicText_RemovesOnlyBold()
    {
        var doc = Doc("***x***");

        var r = MarkdownEditing.ToggleInline(doc, Sel(3, 1), "**");

        Assert.Equal("*x*", doc.Text);
        Assert.Equal(Sel(1, 1), r);
    }

    [Fact]
    public void ToggleInline_BoldOnItalicText_AddsBold()
    {
        var doc = Doc("*x*");

        MarkdownEditing.ToggleInline(doc, Sel(1, 1), "**");

        Assert.Equal("***x***", doc.Text);
    }

    [Fact]
    public void ToggleInline_ItalicOnItalicText_Removes()
    {
        var doc = Doc("*i*");

        var r = MarkdownEditing.ToggleInline(doc, Sel(1, 1), "*");

        Assert.Equal("i", doc.Text);
        Assert.Equal(Sel(0, 1), r);
    }

    [Fact]
    public void ToggleInline_ItalicWithMarkersInsideSelection_Removes()
    {
        var doc = Doc("*i*");

        var r = MarkdownEditing.ToggleInline(doc, Sel(0, 3), "*");

        Assert.Equal("i", doc.Text);
        Assert.Equal(Sel(0, 1), r);
    }

    [Fact]
    public void ToggleInline_ItalicSelectionContainingBold_WrapsInsteadOfStripping()
    {
        var doc = Doc("**bold**");

        MarkdownEditing.ToggleInline(doc, Sel(0, 8), "*");

        Assert.Equal("***bold***", doc.Text);
    }

    [Fact]
    public void ToggleInline_BoldItalicRoundTrip_ThroughAllFourStates()
    {
        var doc = Doc("x");

        var s = MarkdownEditing.ToggleInline(doc, Sel(0, 1), "**");   // **x**
        s = MarkdownEditing.ToggleInline(doc, s, "*");                // ***x***
        Assert.Equal("***x***", doc.Text);
        s = MarkdownEditing.ToggleInline(doc, s, "**");               // *x*
        Assert.Equal("*x*", doc.Text);
        MarkdownEditing.ToggleInline(doc, s, "*");                    // x

        Assert.Equal("x", doc.Text);
    }

    // ---- Tanpa seleksi ----

    [Fact]
    public void ToggleInline_CaretInsideWord_WrapsTheWord()
    {
        var doc = Doc("hello world");

        var r = MarkdownEditing.ToggleInline(doc, Sel(2), "**");

        Assert.Equal("**hello** world", doc.Text);
        Assert.Equal(Sel(2, 5), r);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void ToggleInline_CaretAtWordEdges_WrapsAdjacentWord(int caret)
    {
        var doc = Doc("hello world");

        MarkdownEditing.ToggleInline(doc, Sel(caret), "**");

        Assert.Equal("**hello** world", doc.Text);
    }

    [Fact]
    public void ToggleInline_CaretAtStartOfSecondWord_WrapsSecondWord()
    {
        var doc = Doc("hello world");

        MarkdownEditing.ToggleInline(doc, Sel(6), "**");

        Assert.Equal("hello **world**", doc.Text);
    }

    [Fact]
    public void ToggleInline_CaretInsideBoldWord_Unwraps()
    {
        var doc = Doc("**hello**");

        MarkdownEditing.ToggleInline(doc, Sel(4), "**");

        Assert.Equal("hello", doc.Text);
    }

    [Fact]
    public void ToggleInline_EmptyDocument_InsertsMarkersWithSelectedPlaceholder()
    {
        var doc = Doc("");

        var r = MarkdownEditing.ToggleInline(doc, Sel(0), "**");

        Assert.Equal("**teks**", doc.Text);
        Assert.Equal(Sel(2, 4), r);
    }

    [Fact]
    public void ToggleInline_CustomPlaceholder_IsUsed()
    {
        var doc = Doc("");

        var r = MarkdownEditing.ToggleInline(doc, Sel(0), "`", "kode");

        Assert.Equal("`kode`", doc.Text);
        Assert.Equal(Sel(1, 4), r);
    }

    [Fact]
    public void ToggleInline_CaretBetweenSpaces_InsertsPlaceholderAtCaret()
    {
        var doc = Doc("a  b");

        var r = MarkdownEditing.ToggleInline(doc, Sel(2), "**");

        Assert.Equal("a **teks** b", doc.Text);
        Assert.Equal(Sel(4, 4), r);
    }

    [Fact]
    public void ToggleInline_CaretBetweenEmptyMarkerPair_RemovesThePair()
    {
        var doc = Doc("a **** b");

        var r = MarkdownEditing.ToggleInline(doc, Sel(4), "**");

        Assert.Equal("a  b", doc.Text);
        Assert.Equal(Sel(2, 0), r);
    }

    [Fact]
    public void ToggleInline_CaretBetweenEmptyItalicPair_RemovesThePair()
    {
        var doc = Doc("**");

        MarkdownEditing.ToggleInline(doc, Sel(1), "*");

        Assert.Equal("", doc.Text);
    }

    [Fact]
    public void ToggleInline_UnicodeLettersCountAsWordCharacters()
    {
        var doc = Doc("naïve café");

        MarkdownEditing.ToggleInline(doc, Sel(7), "*");

        Assert.Equal("naïve *café*", doc.Text);
    }

    // ---- Klem, undo, argumen ----

    [Fact]
    public void ToggleInline_SelectionOutOfRange_IsClampedToDocument()
    {
        var doc = Doc("abc");

        var ex = Record.Exception(() => MarkdownEditing.ToggleInline(doc, Sel(-5, 2), "**"));

        Assert.Null(ex);
        Assert.Equal("**ab**c", doc.Text);
    }

    [Fact]
    public void ToggleInline_StartBeyondEnd_DoesNotThrow()
    {
        var doc = Doc("abc def");

        var ex = Record.Exception(() => MarkdownEditing.ToggleInline(doc, Sel(500, 20), "**"));

        Assert.Null(ex);
        Assert.Equal("abc **def**", doc.Text);
    }

    [Fact]
    public void ToggleInline_NegativeLength_TreatedAsCaret()
    {
        var doc = Doc("abc");

        var ex = Record.Exception(() => MarkdownEditing.ToggleInline(doc, Sel(1, -3), "**"));

        Assert.Null(ex);
        Assert.Equal("**abc**", doc.Text);
    }

    [Fact]
    public void ToggleInline_IsASingleUndoStep()
    {
        var doc = Doc("hello world");

        MarkdownEditing.ToggleInline(doc, Sel(0, 5), "**");
        doc.UndoStack.Undo();

        Assert.Equal("hello world", doc.Text);
    }

    [Fact]
    public void ToggleInline_NullDocument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MarkdownEditing.ToggleInline(null!, Sel(0), "**"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ToggleInline_EmptyMarker_Throws(string? marker)
    {
        Assert.ThrowsAny<ArgumentException>(() => MarkdownEditing.ToggleInline(Doc("a"), Sel(0), marker!));
    }
}

public class MarkdownEditingLineTests
{
    static TextDocument Doc(string text) => new(text);

    static SelectionRange Sel(int start, int length = 0) => new(start, length);

    // ---- SetHeading ----

    [Fact]
    public void SetHeading_AddsPrefixToCurrentLine_AndMovesCaretWithText()
    {
        var doc = Doc("text");

        var r = MarkdownEditing.SetHeading(doc, Sel(2), 2);

        Assert.Equal("## text", doc.Text);
        Assert.Equal(Sel(5), r);
    }

    [Fact]
    public void SetHeading_SameLevelAgain_TogglesOff()
    {
        var doc = Doc("## text");

        MarkdownEditing.SetHeading(doc, Sel(4), 2);

        Assert.Equal("text", doc.Text);
    }

    [Fact]
    public void SetHeading_DifferentLevel_ReplacesExistingMarks()
    {
        var doc = Doc("# a");

        MarkdownEditing.SetHeading(doc, Sel(0), 3);

        Assert.Equal("### a", doc.Text);
    }

    [Fact]
    public void SetHeading_LevelZero_RemovesHeading()
    {
        var doc = Doc("### a");

        MarkdownEditing.SetHeading(doc, Sel(0), 0);

        Assert.Equal("a", doc.Text);
    }

    [Fact]
    public void SetHeading_LevelZeroOnPlainText_IsNoOp()
    {
        var doc = Doc("a");

        MarkdownEditing.SetHeading(doc, Sel(0), 0);

        Assert.Equal("a", doc.Text);
    }

    [Theory]
    [InlineData(9, "###### a")]
    [InlineData(7, "###### a")]
    [InlineData(-1, "a")]
    public void SetHeading_LevelIsClampedTo0Through6(int level, string expected)
    {
        var doc = Doc("a");

        MarkdownEditing.SetHeading(doc, Sel(0), level);

        Assert.Equal(expected, doc.Text);
    }

    [Fact]
    public void SetHeading_MultiLine_SkipsBlankLines()
    {
        var doc = Doc("a\n\nb");

        var r = MarkdownEditing.SetHeading(doc, Sel(0, 4), 1);

        Assert.Equal("# a\n\n# b", doc.Text);
        Assert.Equal(Sel(0, doc.TextLength), r);
    }

    [Fact]
    public void SetHeading_WhitespaceOnlyLineBetween_IsLeftAlone()
    {
        var doc = Doc("a\n   \nb");

        MarkdownEditing.SetHeading(doc, Sel(0, doc.TextLength), 2);

        Assert.Equal("## a\n   \n## b", doc.Text);
    }

    [Fact]
    public void SetHeading_AllLinesAlreadyAtLevel_TogglesAllOff()
    {
        var doc = Doc("# a\n\n# b");

        MarkdownEditing.SetHeading(doc, Sel(0, doc.TextLength), 1);

        Assert.Equal("a\n\nb", doc.Text);
    }

    [Fact]
    public void SetHeading_MixedLevels_NormalizesToRequestedLevel()
    {
        var doc = Doc("# a\n## b");

        MarkdownEditing.SetHeading(doc, Sel(0, doc.TextLength), 1);

        Assert.Equal("# a\n# b", doc.Text);
    }

    [Fact]
    public void SetHeading_SelectionEndingAtStartOfNextLine_ExcludesThatLine()
    {
        var doc = Doc("a\nb\nc");

        MarkdownEditing.SetHeading(doc, Sel(0, 4), 1);

        Assert.Equal("# a\n# b\nc", doc.Text);
    }

    [Fact]
    public void SetHeading_HashWithoutSpace_IsNotAHeading_SoItGetsAPrefix()
    {
        var doc = Doc("#hashtag");

        MarkdownEditing.SetHeading(doc, Sel(0), 1);

        Assert.Equal("# #hashtag", doc.Text);
    }

    [Fact]
    public void SetHeading_SevenHashes_IsNotAHeading()
    {
        var doc = Doc("####### x");

        MarkdownEditing.SetHeading(doc, Sel(0), 2);

        Assert.Equal("## ####### x", doc.Text);
    }

    [Fact]
    public void SetHeading_UpToThreeLeadingSpaces_AreStrippedWithTheOldMarks()
    {
        var doc = Doc("   ## x");

        MarkdownEditing.SetHeading(doc, Sel(0), 1);

        Assert.Equal("# x", doc.Text);
    }

    [Fact]
    public void SetHeading_EmptyDocument_InsertsBareMarks_AndTogglesBack()
    {
        var doc = Doc("");

        MarkdownEditing.SetHeading(doc, Sel(0), 1);
        Assert.Equal("# ", doc.Text);

        MarkdownEditing.SetHeading(doc, Sel(2), 1);
        Assert.Equal("", doc.Text);
    }

    [Fact]
    public void SetHeading_PreservesCrLfLineEndings()
    {
        var doc = Doc("a\r\nb");

        MarkdownEditing.SetHeading(doc, Sel(0, doc.TextLength), 2);

        Assert.Equal("## a\r\n## b", doc.Text);
    }

    [Fact]
    public void SetHeading_OnlyBlankLinesSelected_ChangesNothing()
    {
        var doc = Doc("\n\n");

        MarkdownEditing.SetHeading(doc, Sel(0, 2), 1);

        Assert.Equal("\n\n", doc.Text);
    }

    [Fact]
    public void SetHeading_MultiLine_IsASingleUndoStep()
    {
        var doc = Doc("a\nb\nc");

        MarkdownEditing.SetHeading(doc, Sel(0, doc.TextLength), 1);
        Assert.Equal("# a\n# b\n# c", doc.Text);
        doc.UndoStack.Undo();

        Assert.Equal("a\nb\nc", doc.Text);
    }

    [Fact]
    public void SetHeading_OnlyAffectsLinesTouchedBySelection()
    {
        var doc = Doc("a\nb\nc");

        MarkdownEditing.SetHeading(doc, Sel(2, 1), 1);

        Assert.Equal("a\n# b\nc", doc.Text);
    }

    [Fact]
    public void SetHeading_CaretOnLastLineWithoutTrailingNewline()
    {
        var doc = Doc("a\nb");

        MarkdownEditing.SetHeading(doc, Sel(3), 1);

        Assert.Equal("a\n# b", doc.Text);
    }

    [Fact]
    public void SetHeading_NullDocument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MarkdownEditing.SetHeading(null!, Sel(0), 1));
    }

    // ---- ToggleLinePrefix ----

    [Fact]
    public void ToggleLinePrefix_AddsThenRemovesBullets()
    {
        var doc = Doc("a\nb");

        MarkdownEditing.ToggleLinePrefix(doc, Sel(0, 3), "- ");
        Assert.Equal("- a\n- b", doc.Text);

        MarkdownEditing.ToggleLinePrefix(doc, Sel(0, doc.TextLength), "- ");
        Assert.Equal("a\nb", doc.Text);
    }

    [Fact]
    public void ToggleLinePrefix_MixedSelection_AddsOnlyWhereMissing()
    {
        var doc = Doc("- a\nb");

        MarkdownEditing.ToggleLinePrefix(doc, Sel(0, doc.TextLength), "- ");

        Assert.Equal("- a\n- b", doc.Text);
    }

    [Theory]
    [InlineData("* a")]
    [InlineData("+ a")]
    public void ToggleLinePrefix_AlternativeBulletMarkers_CountAsBullets_AndAreRemoved(string line)
    {
        var doc = Doc(line);

        MarkdownEditing.ToggleLinePrefix(doc, Sel(0), "- ");

        Assert.Equal("a", doc.Text);
    }

    [Fact]
    public void ToggleLinePrefix_AlternativeBulletAmongPlainLines_IsKeptNotDoubled()
    {
        var doc = Doc("* a\nb");

        MarkdownEditing.ToggleLinePrefix(doc, Sel(0, doc.TextLength), "- ");

        Assert.Equal("* a\n- b", doc.Text);
    }

    [Fact]
    public void ToggleLinePrefix_Quote_AddsAndRemoves()
    {
        var doc = Doc("kutipan");

        MarkdownEditing.ToggleLinePrefix(doc, Sel(0), "> ");
        Assert.Equal("> kutipan", doc.Text);

        MarkdownEditing.ToggleLinePrefix(doc, Sel(0), "> ");
        Assert.Equal("kutipan", doc.Text);
    }

    [Fact]
    public void ToggleLinePrefix_NestedQuote_RemovesOnlyOneLevel()
    {
        var doc = Doc("> > dalam");

        MarkdownEditing.ToggleLinePrefix(doc, Sel(0), "> ");

        Assert.Equal("> dalam", doc.Text);
    }

    [Fact]
    public void ToggleLinePrefix_BlankLinesInSelection_AreSkipped()
    {
        var doc = Doc("a\n\nb");

        MarkdownEditing.ToggleLinePrefix(doc, Sel(0, doc.TextLength), "- ");

        Assert.Equal("- a\n\n- b", doc.Text);
    }

    [Fact]
    public void ToggleLinePrefix_SingleBlankLine_GetsPrefix()
    {
        var doc = Doc("");

        MarkdownEditing.ToggleLinePrefix(doc, Sel(0), "- ");

        Assert.Equal("- ", doc.Text);
    }

    [Fact]
    public void ToggleLinePrefix_CaretMovesWithInsertedPrefix()
    {
        var doc = Doc("a");

        var r = MarkdownEditing.ToggleLinePrefix(doc, Sel(1), "- ");

        Assert.Equal("- a", doc.Text);
        Assert.Equal(Sel(3), r);
    }

    [Fact]
    public void ToggleLinePrefix_CaretMovesBackWhenPrefixRemoved()
    {
        var doc = Doc("- a");

        var r = MarkdownEditing.ToggleLinePrefix(doc, Sel(3), "- ");

        Assert.Equal("a", doc.Text);
        Assert.Equal(Sel(1), r);
    }

    [Fact]
    public void ToggleLinePrefix_SelectionResult_CoversAllRewrittenLines()
    {
        var doc = Doc("a\nb");

        var r = MarkdownEditing.ToggleLinePrefix(doc, Sel(0, 3), "- ");

        Assert.Equal(Sel(0, doc.TextLength), r);
    }

    [Fact]
    public void ToggleLinePrefix_PreservesCrLf()
    {
        var doc = Doc("a\r\nb\r\n");

        MarkdownEditing.ToggleLinePrefix(doc, Sel(0, 4), "> ");

        Assert.Equal("> a\r\n> b\r\n", doc.Text);
    }

    [Fact]
    public void ToggleLinePrefix_IsASingleUndoStep()
    {
        var doc = Doc("a\nb");

        MarkdownEditing.ToggleLinePrefix(doc, Sel(0, 3), "- ");
        doc.UndoStack.Undo();

        Assert.Equal("a\nb", doc.Text);
    }

    [Fact]
    public void ToggleLinePrefix_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => MarkdownEditing.ToggleLinePrefix(null!, Sel(0), "- "));
        Assert.ThrowsAny<ArgumentException>(() => MarkdownEditing.ToggleLinePrefix(Doc("a"), Sel(0), ""));
    }

    [Fact]
    public void ToggleLinePrefix_SelectionBeyondDocument_IsClamped()
    {
        var doc = Doc("a");

        var ex = Record.Exception(() => MarkdownEditing.ToggleLinePrefix(doc, Sel(40, 10), "- "));

        Assert.Null(ex);
        Assert.Equal("- a", doc.Text);
    }
}

public class MarkdownEditingLinkTests
{
    static TextDocument Doc(string text) => new(text);

    static SelectionRange Sel(int start, int length = 0) => new(start, length);

    [Fact]
    public void InsertLink_NoSelection_InsertsTemplateWithLabelSelected()
    {
        var doc = Doc("");

        var r = MarkdownEditing.InsertLink(doc, Sel(0), image: false);

        Assert.Equal("[teks tautan](https://)", doc.Text);
        Assert.Equal(Sel(1, "teks tautan".Length), r);
    }

    [Fact]
    public void InsertLink_PlainTextSelection_BecomesLabel_AndUrlPlaceholderIsSelected()
    {
        var doc = Doc("see Google now");

        var r = MarkdownEditing.InsertLink(doc, Sel(4, 6), image: false);

        Assert.Equal("see [Google](https://) now", doc.Text);
        Assert.Equal("https://", doc.GetText(r.Start, r.Length));
    }

    [Theory]
    [InlineData("https://a.com/x?y=1#z")]
    [InlineData("http://a.com")]
    [InlineData("mailto:a@b.com")]
    public void InsertLink_UrlSelection_IsUsedAsUrl_AndLabelIsSelected(string url)
    {
        var doc = Doc(url);

        var r = MarkdownEditing.InsertLink(doc, Sel(0, url.Length), image: false);

        Assert.Equal($"[teks tautan]({url})", doc.Text);
        Assert.Equal("teks tautan", doc.GetText(r.Start, r.Length));
    }

    [Fact]
    public void InsertLink_UrlWithSurroundingWhitespace_IsTrimmed()
    {
        var doc = Doc(" https://a.com ");

        MarkdownEditing.InsertLink(doc, Sel(0, doc.TextLength), image: false);

        Assert.Equal("[teks tautan](https://a.com)", doc.Text);
    }

    [Theory]
    [InlineData("ftp://host/file")]
    [InlineData("www.example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://a.com dan lainnya")]
    public void InsertLink_NonLinkableSelection_IsTreatedAsLabel(string selected)
    {
        var doc = Doc(selected);

        var r = MarkdownEditing.InsertLink(doc, Sel(0, selected.Length), image: false);

        Assert.Equal($"[{selected}](https://)", doc.Text);
        Assert.Equal("https://", doc.GetText(r.Start, r.Length));
    }

    [Fact]
    public void InsertLink_Image_NoSelection_UsesImageTemplate()
    {
        var doc = Doc("");

        var r = MarkdownEditing.InsertLink(doc, Sel(0), image: true);

        Assert.Equal("![deskripsi](https://)", doc.Text);
        Assert.Equal("deskripsi", doc.GetText(r.Start, r.Length));
    }

    [Fact]
    public void InsertLink_Image_TextSelection_BecomesAltText_AndUrlSelected()
    {
        var doc = Doc("Logo");

        var r = MarkdownEditing.InsertLink(doc, Sel(0, 4), image: true);

        Assert.Equal("![Logo](https://)", doc.Text);
        Assert.Equal("https://", doc.GetText(r.Start, r.Length));
    }

    [Fact]
    public void InsertLink_Image_UrlSelection_KeepsUrl()
    {
        var doc = Doc("https://a.com/p.png");

        var r = MarkdownEditing.InsertLink(doc, Sel(0, doc.TextLength), image: true);

        Assert.Equal("![deskripsi](https://a.com/p.png)", doc.Text);
        Assert.Equal("deskripsi", doc.GetText(r.Start, r.Length));
    }

    [Fact]
    public void InsertLink_CaretInMiddleOfText_InsertsAtCaret()
    {
        var doc = Doc("ab");

        MarkdownEditing.InsertLink(doc, Sel(1), image: false);

        Assert.Equal("a[teks tautan](https://)b", doc.Text);
    }

    [Fact]
    public void InsertLink_SelectionOutOfRange_IsClamped()
    {
        var doc = Doc("ab");

        var ex = Record.Exception(() => MarkdownEditing.InsertLink(doc, Sel(50, 5), image: false));

        Assert.Null(ex);
        Assert.Equal("ab[teks tautan](https://)", doc.Text);
    }

    [Fact]
    public void InsertLink_IsASingleUndoStep()
    {
        var doc = Doc("Google");

        MarkdownEditing.InsertLink(doc, Sel(0, 6), image: false);
        doc.UndoStack.Undo();

        Assert.Equal("Google", doc.Text);
    }

    [Fact]
    public void InsertLink_NullDocument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MarkdownEditing.InsertLink(null!, Sel(0), false));
    }
}

/// <summary>Apply() bekerja pada kontrol TextEditor sungguhan, jadi dijalankan di thread STA bersama.</summary>
[Collection("Wpf")]
public class MarkdownEditingApplyTests
{
    static (string Text, int Start, int Length) Apply(string text, int start, int length, MarkdownFormat format, int level = 1)
    {
        return WpfHost.Instance.Run(() =>
        {
            var editor = new TextEditor { Text = text };
            editor.Select(start, length);
            MarkdownEditing.Apply(editor, format, level);
            return (editor.Text, editor.SelectionStart, editor.SelectionLength);
        });
    }

    [Fact]
    public void Apply_Bold_WrapsSelectionAndKeepsInnerTextSelected()
    {
        Assert.Equal(("**ab** c", 2, 2), Apply("ab c", 0, 2, MarkdownFormat.Bold));
    }

    [Fact]
    public void Apply_Italic_AndInlineCode()
    {
        Assert.Equal(("*ab* c", 1, 2), Apply("ab c", 0, 2, MarkdownFormat.Italic));
        Assert.Equal(("`ab` c", 1, 2), Apply("ab c", 0, 2, MarkdownFormat.InlineCode));
    }

    [Fact]
    public void Apply_Heading_UsesRequestedLevel()
    {
        Assert.Equal(("### ab", 0, 6), Apply("ab", 0, 2, MarkdownFormat.Heading, 3));
    }

    [Fact]
    public void Apply_BulletAndQuote()
    {
        Assert.Equal("- ab", Apply("ab", 0, 2, MarkdownFormat.BulletList).Text);
        Assert.Equal("> ab", Apply("ab", 0, 2, MarkdownFormat.Quote).Text);
    }

    [Fact]
    public void Apply_LinkAndImage()
    {
        Assert.Equal("[ab](https://)", Apply("ab", 0, 2, MarkdownFormat.Link).Text);
        Assert.Equal("![ab](https://)", Apply("ab", 0, 2, MarkdownFormat.Image).Text);
    }

    [Fact]
    public void Apply_UnknownFormat_LeavesTextAndSelectionUntouched()
    {
        Assert.Equal(("ab c", 0, 2), Apply("ab c", 0, 2, (MarkdownFormat)99));
    }
}

[CollectionDefinition("Wpf", DisableParallelization = true)]
public class WpfCollection;
