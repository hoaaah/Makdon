using System.Windows;
using System.Windows.Documents;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Wpf;
using Markdig.Syntax;
using Markdig.Wpf;

namespace MdViewer;

/// <summary>
/// Pengganti HeadingRenderer bawaan Markdig.Wpf: sama persis, tetapi menyimpan id heading
/// (slug dari AutoIdentifiers) di <see cref="FrameworkContentElement.Tag"/> agar pratinjau bisa melompat ke "#anchor".
/// </summary>
sealed class AnchorHeadingRenderer : WpfObjectRenderer<HeadingBlock>
{
    static readonly ComponentResourceKey[] StyleKeys =
    [
        Styles.Heading1StyleKey, Styles.Heading2StyleKey, Styles.Heading3StyleKey,
        Styles.Heading4StyleKey, Styles.Heading5StyleKey, Styles.Heading6StyleKey,
    ];

    protected override void Write(WpfRenderer renderer, HeadingBlock obj)
    {
        var paragraph = new Paragraph { Tag = obj.TryGetAttributes()?.Id };
        paragraph.SetResourceReference(FrameworkContentElement.StyleProperty, StyleKeys[Math.Clamp(obj.Level, 1, 6) - 1]);

        renderer.Push(paragraph);
        renderer.WriteLeafInline(obj);
        renderer.Pop();
    }
}
