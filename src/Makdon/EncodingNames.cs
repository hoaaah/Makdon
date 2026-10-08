using System.Text;

namespace Makdon;

/// <summary>Nama encoding yang ramah untuk status bar (mis. "UTF-8", "UTF-8 BOM").</summary>
public static class EncodingNames
{
    public static string Describe(Encoding? encoding)
    {
        if (encoding is null) return "";

        return encoding.CodePage switch
        {
            65001 => encoding.GetPreamble().Length > 0 ? "UTF-8 BOM" : "UTF-8",
            1200 => "UTF-16 LE",
            1201 => "UTF-16 BE",
            12000 => "UTF-32 LE",
            12001 => "UTF-32 BE",
            1252 => "Windows-1252",
            _ => encoding.WebName.ToUpperInvariant(),
        };
    }
}
