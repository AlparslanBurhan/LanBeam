using System.Windows;
using LanBeam.App.Views;
using LanBeam.Core.Transfer;

namespace LanBeam.App.Services;

/// <summary>Gönderim sırasında PIN gerektiğinde UI iş parçacığında pencere açar.</summary>
public sealed class UiSendInteraction : ISendInteraction
{
    public async Task<string?> RequestPinAsync(string peerDeviceName, int attemptsLeft, CancellationToken ct)
    {
        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var window = new PinEntryWindow(peerDeviceName, attemptsLeft);
            Window? owner = Application.Current.Windows.OfType<MainWindow>()
                .FirstOrDefault(w => w.IsVisible);
            if (owner is not null) window.Owner = owner;
            return window.ShowDialog() == true ? window.Pin : null;
        });
    }
}

/// <summary>UI'dan gönderim başlatma yardımcıları.</summary>
public static class AppFlows
{
    public static void SendPaths(Core.Models.DeviceInfo device, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        App.Node.Send(device, paths, new UiSendInteraction());
        App.Instance.MainWindowInstance?.ShowTransfers();
    }

    public static void PickFilesAndSend(Core.Models.DeviceInfo device)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Title = Localization.Loc.Format("Str_PickFilesTitleFormat", device.Name),
        };
        if (dialog.ShowDialog() == true)
            SendPaths(device, dialog.FileNames);
    }

    public static void PickFolderAndSend(Core.Models.DeviceInfo device)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Localization.Loc.Format("Str_PickFolderTitleFormat", device.Name),
        };
        if (dialog.ShowDialog() == true)
            SendPaths(device, [dialog.FolderName]);
    }
}
