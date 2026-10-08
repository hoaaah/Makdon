namespace Makdon;

/// <summary>Aturan zoom (persen) yang dipakai editor dan pratinjau. Murni, tanpa UI.</summary>
public static class ZoomLevel
{
    public const int Min = 50;
    public const int Max = 300;
    public const int Default = 100;
    public const int StepPercent = 10;

    /// <summary>Ukuran font editor pada zoom 100%.</summary>
    public const double BaseEditorFontSize = 14;

    public static int Clamp(int percent) => Math.Clamp(percent, Min, Max);

    /// <summary>Zoom berikutnya: direction &gt; 0 memperbesar, &lt; 0 memperkecil, 0 tetap. Selalu dalam rentang Min..Max.</summary>
    public static int Step(int current, int direction) =>
        Clamp(Clamp(current) + Math.Sign(direction) * StepPercent);

    public static double EditorFontSize(int percent) =>
        Math.Round(BaseEditorFontSize * Clamp(percent) / 100.0, 2);

    public static string Format(int percent) => $"{Clamp(percent)}%";
}
