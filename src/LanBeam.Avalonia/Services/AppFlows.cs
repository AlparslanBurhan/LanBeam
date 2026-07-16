using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LanBeam.Ui.Localization;
using LanBeam.Ui.Views;
using LanBeam.Core.Models;
using LanBeam.Core.Transfer;

namespace LanBeam.Ui.Services;

/// <summary>Gönderim sırasında PIN gerektiğinde PIN girişi penceresini açar.</summary>
public sealed class UiSendInteraction : ISendInteraction
{
    public async Task<string?> RequestPinAsync(string peerDeviceName, int attemptsLeft, CancellationToken ct) =>
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = new PinEntryWindow(peerDeviceName, attemptsLeft);
            window.Show();
            return window.PinResult;
        });
}

public static class AppFlows
{
    public static void SendPaths(DeviceInfo device, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        App.Node.Send(device, paths, new UiSendInteraction());
        (App.MainWindowInstance as MainWindow)?.ShowTransfers();
    }

    public static async Task PickFilesAndSend(Visual owner, DeviceInfo device)
    {
        TopLevel? top = TopLevel.GetTopLevel(owner);
        if (top is null) return;
        IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = Loc.Format("Str_PickFilesTitleFormat", device.Name),
        });
        string[] paths = files.Select(f => f.Path.LocalPath).Where(p => !string.IsNullOrEmpty(p)).ToArray();
        SendPaths(device, paths);
    }

    public static async Task PickFolderAndSend(Visual owner, DeviceInfo device)
    {
        TopLevel? top = TopLevel.GetTopLevel(owner);
        if (top is null) return;
        IReadOnlyList<IStorageFolder> folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Loc.Format("Str_PickFolderTitleFormat", device.Name),
        });
        if (folders.Count > 0 && folders[0].Path.LocalPath is { Length: > 0 } path)
            SendPaths(device, [path]);
    }
}
