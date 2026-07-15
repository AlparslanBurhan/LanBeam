using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LanBeam.App.Localization;

namespace LanBeam.App.Views;

public partial class PinEntryWindow
{
    public string? Pin { get; private set; }

    public PinEntryWindow(string peerDeviceName, int attemptsLeft)
    {
        InitializeComponent();
        InfoText.Text = Loc.Format("Str_EnterPinFormat", peerDeviceName);
        AttemptsText.Text = attemptsLeft < 3 ? Loc.Format("Str_AttemptsLeftFormat", attemptsLeft) : "";
        Loaded += (_, _) => PinBox.Focus();
    }

    private void PinBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        OkButton.IsEnabled = PinBox.Text.Length == 6 && PinBox.Text.All(char.IsDigit);
    }

    private void PinBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && OkButton.IsEnabled)
            Ok_Click(sender, e);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Pin = PinBox.Text;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
