using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LanBeam.Ui.Localization;

namespace LanBeam.Ui.Views;

public partial class PinEntryWindow : Window
{
    private readonly TaskCompletionSource<string?> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<string?> PinResult => _tcs.Task;

    public PinEntryWindow() => InitializeComponent();

    public PinEntryWindow(string peerDeviceName, int attemptsLeft) : this()
    {
        Icon = App.LoadIcon();
        InfoText.Text = Loc.Format("Str_EnterPinFormat", peerDeviceName);
        AttemptsText.Text = attemptsLeft < 3 ? Loc.Format("Str_AttemptsLeftFormat", attemptsLeft) : "";
        Opened += (_, _) => PinBox.Focus();
        Closed += (_, _) => _tcs.TrySetResult(null);
    }


    private void Pin_TextChanged(object? sender, TextChangedEventArgs e) =>
        OkButton.IsEnabled = (PinBox.Text ?? "").Length == 6 && (PinBox.Text ?? "").All(char.IsDigit);

    private void Pin_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && OkButton.IsEnabled) Ok_Click(sender, e);
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(PinBox.Text);
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(null);
        Close();
    }
}
