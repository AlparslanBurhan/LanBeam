using System.Media;
using System.Windows;
using System.Windows.Media;
using LanBeam.App.Localization;
using LanBeam.Core.Transfer;

namespace LanBeam.App.Views;

public partial class PairingPinWindow
{
    private readonly PairingPrompt _prompt;
    private bool _finished;

    public PairingPinWindow(PairingPrompt prompt)
    {
        InitializeComponent();
        _prompt = prompt;

        InfoText.Text = Loc.Format("Str_PairingInfoFormat", prompt.PeerName);
        PinText.Text = prompt.Pin;

        if (prompt.FingerprintChanged)
        {
            InfoText.Text = Loc.Get("Str_FingerprintChangedWarning") + "\n\n" + InfoText.Text;
            InfoText.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x9A, 0x3A));
        }

        _ = WatchCompletionAsync();
        Closed += (_, _) => { if (!_finished) _prompt.Cancel(); };

        SystemSounds.Exclamation.Play();
        Activate();
    }

    private async Task WatchCompletionAsync()
    {
        bool success;
        try { success = await _prompt.Completion; }
        catch (Exception) { success = false; }

        _finished = true;
        await Dispatcher.BeginInvoke(async () =>
        {
            WaitPanel.Visibility = Visibility.Collapsed;
            ResultText.Visibility = Visibility.Visible;
            ResultText.Text = success ? Loc.Get("Str_PairingSuccess") : Loc.Get("Str_PairingFailure");
            ResultText.Foreground = new SolidColorBrush(success
                ? Color.FromRgb(0x4C, 0xC3, 0x66) : Color.FromRgb(0xE8, 0x53, 0x4A));

            await Task.Delay(1800);
            Close();
        });
    }
}
