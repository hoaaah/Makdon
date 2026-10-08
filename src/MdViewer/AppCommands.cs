using System.Windows.Input;

namespace MdViewer;

/// <summary>Perintah khusus aplikasi (yang belum ada di ApplicationCommands). Pintasan tampil otomatis di menu.</summary>
public static class AppCommands
{
    static RoutedUICommand Create(string text, string name, params (Key Key, ModifierKeys Modifiers, string? Display)[] gestures)
    {
        var collection = new InputGestureCollection();
        foreach (var (key, modifiers, display) in gestures)
            collection.Add(display is null ? new KeyGesture(key, modifiers) : new KeyGesture(key, modifiers, display));
        return new RoutedUICommand(text, name, typeof(AppCommands), collection);
    }

    const ModifierKeys Ctrl = ModifierKeys.Control;
    const ModifierKeys CtrlShift = ModifierKeys.Control | ModifierKeys.Shift;

    public static readonly RoutedUICommand ZoomIn = Create("Perbesar", nameof(ZoomIn),
        (Key.OemPlus, Ctrl, "Ctrl+="), (Key.Add, Ctrl, null));
    public static readonly RoutedUICommand ZoomOut = Create("Perkecil", nameof(ZoomOut),
        (Key.OemMinus, Ctrl, "Ctrl+-"), (Key.Subtract, Ctrl, null));
    public static readonly RoutedUICommand ZoomReset = Create("Zoom Normal", nameof(ZoomReset),
        (Key.D0, Ctrl, null), (Key.NumPad0, Ctrl, null));

    public static readonly RoutedUICommand ExportHtml = Create("Ekspor sebagai HTML...", nameof(ExportHtml), (Key.E, CtrlShift, null));
    public static readonly RoutedUICommand PrintPreview = Create("Pratinjau Cetak...", nameof(PrintPreview), (Key.P, CtrlShift, null));
    public static readonly RoutedUICommand FindNext = Create("Cari Berikutnya", nameof(FindNext), (Key.F3, ModifierKeys.None, null));
    public static readonly RoutedUICommand FindPrevious = Create("Cari Sebelumnya", nameof(FindPrevious), (Key.F3, ModifierKeys.Shift, null));
    public static readonly RoutedUICommand SetTheme = Create("Tema", nameof(SetTheme));

    public static readonly RoutedUICommand Bold = Create("Tebal", nameof(Bold), (Key.B, Ctrl, null));
    public static readonly RoutedUICommand Italic = Create("Miring", nameof(Italic), (Key.I, Ctrl, null));
    public static readonly RoutedUICommand InlineCode = Create("Kode Inline", nameof(InlineCode), (Key.E, Ctrl, null));
    public static readonly RoutedUICommand Heading = Create("Heading", nameof(Heading));
    /// <summary>Membuka menu tarik-turun pilihan heading pada toolbar (bukan format itu sendiri).</summary>
    public static readonly RoutedUICommand HeadingMenu = Create("Pilih Heading", nameof(HeadingMenu));
    public static readonly RoutedUICommand BulletList = Create("Daftar", nameof(BulletList), (Key.L, CtrlShift, null));
    public static readonly RoutedUICommand Quote = Create("Kutipan", nameof(Quote), (Key.Q, CtrlShift, null));
    public static readonly RoutedUICommand Link = Create("Tautan", nameof(Link), (Key.K, Ctrl, null));
    public static readonly RoutedUICommand Image = Create("Gambar", nameof(Image), (Key.I, CtrlShift, null));

    /// <summary>Peta perintah format -> jenis format (Heading memakai CommandParameter sebagai level).</summary>
    public static MarkdownFormat? FormatOf(ICommand command) =>
        command == Bold ? MarkdownFormat.Bold
        : command == Italic ? MarkdownFormat.Italic
        : command == InlineCode ? MarkdownFormat.InlineCode
        : command == Heading ? MarkdownFormat.Heading
        : command == BulletList ? MarkdownFormat.BulletList
        : command == Quote ? MarkdownFormat.Quote
        : command == Link ? MarkdownFormat.Link
        : command == Image ? MarkdownFormat.Image
        : null;
}
