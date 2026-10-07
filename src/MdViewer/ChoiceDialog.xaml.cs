using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MdViewer;

/// <summary>Satu tombol pada <see cref="ChoiceDialog"/>. Tepat satu yang <paramref name="IsDefault"/> (fokus awal/Enter).</summary>
public sealed record DialogChoice<T>(string Label, T Value, bool IsDefault = false);

/// <summary>
/// Dialog kecil bertema dengan tombol berlabel jelas (pengganti MessageBox Ya/Tidak/Batal yang ambigu).
/// Menutup dengan Esc atau tombol X sama dengan memilih <c>cancelValue</c>.
/// </summary>
public partial class ChoiceDialog : Window
{
    object? result;

    ChoiceDialog() => InitializeComponent();

    /// <summary>Menampilkan dialog modal dan mengembalikan nilai tombol yang dipilih (<paramref name="cancelValue"/> bila ditutup tanpa memilih).</summary>
    public static T Show<T>(Window? owner, string title, string message, T cancelValue, params DialogChoice<T>[] choices)
    {
        var dialog = new ChoiceDialog { Title = title };
        if (owner is { IsLoaded: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.MessageText.Text = message;
        dialog.result = cancelValue;

        Button? initial = null;
        foreach (var choice in choices)
        {
            var button = new Button { Content = choice.Label, Margin = new Thickness(dialog.ButtonPanel.Children.Count == 0 ? 0 : 8, 0, 0, 0) };
            if (choice.IsDefault)
            {
                button.Style = (Style)dialog.FindResource("AccentButton");
                button.IsDefault = true;
                initial = button;
            }
            var value = choice.Value;
            button.Click += (_, _) =>
            {
                dialog.result = value;
                dialog.Close();
            };
            dialog.ButtonPanel.Children.Add(button);
        }
        if (initial is not null) dialog.Loaded += (_, _) => initial.Focus();

        dialog.ShowDialog();
        return (T)dialog.result!;
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }
}
