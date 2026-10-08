using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Makdon.Tests.Support;

namespace Makdon.Tests;

/// <summary>Dialog pilihan berlabel (pengganti MessageBox Ya/Tidak/Batal) untuk konflik simpan/perubahan eksternal.</summary>
[Collection("Wpf")]
public class ChoiceDialogTests
{
    public enum Pick { Overwrite, Reload, Cancel }

    static Pick ShowAndThen(Action<ChoiceDialog> act, out string message, out string[] labels, out string? defaultLabel)
    {
        string? shownMessage = null;
        string[] shownLabels = [];
        string? shownDefault = null;
        var result = Pick.Cancel;
        WpfHost.Instance.Run(() =>
        {
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                var dialog = Application.Current.Windows.OfType<ChoiceDialog>().Single();
                var buttons = ((Panel)dialog.FindName("ButtonPanel")).Children.OfType<Button>().ToList();
                shownMessage = ((TextBlock)dialog.FindName("MessageText")).Text;
                shownLabels = buttons.Select(b => (string)b.Content).ToArray();
                shownDefault = buttons.SingleOrDefault(b => b.IsDefault)?.Content as string;
                act(dialog);
            });
            result = ChoiceDialog.Show(null, "Makdon", "Pesan konflik", Pick.Cancel,
                new DialogChoice<Pick>("Timpa", Pick.Overwrite),
                new DialogChoice<Pick>("Muat dari Disk", Pick.Reload),
                new DialogChoice<Pick>("Batal", Pick.Cancel, IsDefault: true));
        });
        message = shownMessage!;
        labels = shownLabels;
        defaultLabel = shownDefault;
        return result;
    }

    static void Click(ChoiceDialog dialog, string label) =>
        ((Panel)dialog.FindName("ButtonPanel")).Children.OfType<Button>().Single(b => (string)b.Content == label)
            .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    [Theory]
    [InlineData("Timpa", Pick.Overwrite)]
    [InlineData("Muat dari Disk", Pick.Reload)]
    [InlineData("Batal", Pick.Cancel)]
    public void ClickingAButton_ReturnsItsValue(string label, Pick expected)
    {
        var result = ShowAndThen(d => Click(d, label), out var message, out var labels, out var defaultLabel);

        Assert.Equal(expected, result);
        Assert.Equal("Pesan konflik", message);
        Assert.Equal(["Timpa", "Muat dari Disk", "Batal"], labels);
        Assert.Equal("Batal", defaultLabel); // pilihan paling aman jadi bawaan (Enter)
    }

    [Fact]
    public void ClosingTheWindowWithoutChoosing_ReturnsTheCancelValue()
    {
        var result = ShowAndThen(d => d.Close(), out _, out _, out _);

        Assert.Equal(Pick.Cancel, result);
    }
}
