using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;

namespace MdViewer;

/// <summary>
/// Warna syntax highlighting Markdown AvalonEdit mengikuti tema. Warna Markdown utama berasal dari brush SyntaxXxxBrush;
/// warna bawaan lain (mis. kode ber-indentasi yang memakai aturan C#) dijaga kontrasnya terhadap latar editor.
/// </summary>
static class EditorTheme
{
    const double MinimumContrast = 4.5;

    // Warna asli definisi bawaan, disimpan sekali supaya perhitungan kontras selalu berangkat dari aslinya
    // (bukan dari hasil penyesuaian tema sebelumnya).
    static readonly Dictionary<HighlightingColor, Color?> OriginalForegrounds = new(ReferenceEqualityComparer.Instance as IEqualityComparer<HighlightingColor>);

    public static void ApplyMarkdownHighlighting()
    {
        var definition = HighlightingManager.Instance.GetDefinition("MarkDown");
        if (definition is null) return;

        var background = (Application.Current?.TryFindResource("EditorBackgroundBrush") as SolidColorBrush)?.Color ?? Colors.White;
        foreach (var color in AllColors(definition))
        {
            if (!OriginalForegrounds.TryGetValue(color, out var original))
                OriginalForegrounds[color] = original = ColorOf(color.Foreground);
            if (original is { } fg)
                color.Foreground = new SimpleHighlightingBrush(EnsureContrast(fg, background, MinimumContrast));
        }

        Set(definition, "Heading", "SyntaxHeadingBrush", foreground: true, bold: true);
        Set(definition, "BlockQuote", "SyntaxQuoteBrush", foreground: true);
        Set(definition, "Link", "SyntaxLinkBrush", foreground: true);
        Set(definition, "Image", "SyntaxImageBrush", foreground: true);
        Set(definition, "Code", "SyntaxCodeBrush", foreground: true);
        Set(definition, "LineBreak", "SyntaxLineBreakBrush", foreground: false);
    }

    static void Set(IHighlightingDefinition definition, string name, string brushKey, bool foreground, bool bold = false)
    {
        var color = definition.GetNamedColor(name);
        if (color is null || Application.Current?.TryFindResource(brushKey) is not SolidColorBrush brush) return;

        var highlight = new SimpleHighlightingBrush(brush.Color);
        if (foreground) color.Foreground = highlight;
        else color.Background = highlight;
        if (bold) color.FontWeight = FontWeights.Bold;
    }

    // Semua HighlightingColor yang dipakai definisi (bernama maupun anonim di dalam aturan/span).
    static IEnumerable<HighlightingColor> AllColors(IHighlightingDefinition definition)
    {
        var seen = new HashSet<HighlightingColor>(ReferenceEqualityComparer.Instance as IEqualityComparer<HighlightingColor>);
        var visitedRuleSets = new HashSet<HighlightingRuleSet>(ReferenceEqualityComparer.Instance as IEqualityComparer<HighlightingRuleSet>);

        foreach (var named in definition.NamedHighlightingColors)
            if (seen.Add(named)) yield return named;

        var pending = new Stack<HighlightingRuleSet>();
        if (definition.MainRuleSet is { } main) pending.Push(main);
        while (pending.Count > 0)
        {
            var ruleSet = pending.Pop();
            if (!visitedRuleSets.Add(ruleSet)) continue;

            foreach (var rule in ruleSet.Rules)
                if (rule.Color is { } c && seen.Add(c)) yield return c;

            foreach (var span in ruleSet.Spans)
            {
                foreach (var c in new[] { span.SpanColor, span.StartColor, span.EndColor })
                    if (c is not null && seen.Add(c)) yield return c;
                if (span.RuleSet is { } inner) pending.Push(inner);
            }
        }
    }

    static Color? ColorOf(HighlightingBrush? brush) =>
        (brush?.GetBrush(null) as SolidColorBrush)?.Color;

    // ---- Kontras WCAG (murni; dipakai juga untuk pengujian) ----

    public static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    public static double ContrastRatio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Menggeser warna ke putih atau hitam (mana yang kontrasnya lebih tinggi terhadap latar) sampai kontrasnya cukup.</summary>
    public static Color EnsureContrast(Color foreground, Color background, double minimum = MinimumContrast)
    {
        if (ContrastRatio(foreground, background) >= minimum) return foreground;

        // Arah dipilih berdasarkan kontras yang bisa dicapai, bukan luminansi 0,5: titik silang putih/hitam terhadap
        // latar ada di luminansi ~0,179 ((1,05)/(L+0,05) = (L+0,05)/0,05), jadi latar tengah (mis. #B0B0B0) lebih
        // kontras dengan hitam walau tampak "terang".
        var target = ContrastRatio(Colors.White, background) > ContrastRatio(Colors.Black, background) ? Colors.White : Colors.Black;
        for (var t = 0.05; t < 1.0; t += 0.05)
        {
            var candidate = Color.FromRgb(
                (byte)Math.Round(foreground.R + (target.R - foreground.R) * t),
                (byte)Math.Round(foreground.G + (target.G - foreground.G) * t),
                (byte)Math.Round(foreground.B + (target.B - foreground.B) * t));
            if (ContrastRatio(candidate, background) >= minimum) return candidate;
        }
        return target;
    }
}
