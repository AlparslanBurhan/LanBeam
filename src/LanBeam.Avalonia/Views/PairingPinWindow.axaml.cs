using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using LanBeam.Ui.Localization;
using LanBeam.Core.Transfer;

namespace LanBeam.Ui.Views;

public partial class PairingPinWindow : Window
{
    private readonly PairingPrompt _prompt;
    private bool _finished;

    public PairingPinWindow() : this(new PairingPrompt { Pin = "000000", PeerName = "?" }) { }

    public PairingPinWindow(PairingPrompt prompt)
    {
        InitializeComponent();
        Icon = App.LoadIcon();
        _prompt = prompt;

        InfoText.Text = Loc.Format("Str_PairingInfoFormat", prompt.PeerName);
        PinText.Text = prompt.Pin;
        if (prompt.FingerprintChanged)
        {
            InfoText.Text = Loc.Get("Str_FingerprintChangedWarning") + "\n\n" + InfoText.Text;
            InfoText.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x9A, 0x3A));
        }

        _ = WatchAsync();
        Closed += (_, _) => { if (!_finished) _prompt.Cancel(); };
    }


    private async Task WatchAsync()
    {
        bool success;
        try { success = await _prompt.Completion; }
        catch (Exception) { success = false; }
        _finished = true;

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            WaitPanel.IsVisible = false;
            ResultText.IsVisible = true;
            ResultText.Text = success ? Loc.Get("Str_PairingSuccess") : Loc.Get("Str_PairingFailure");
            ResultText.Foreground = new SolidColorBrush(success
                ? Color.FromRgb(0x4C, 0xC3, 0x66) : Color.FromRgb(0xE8, 0x53, 0x4A));
            await Task.Delay(1600);
            Close();
        });
    }
}
