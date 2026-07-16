using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using LanBeam.Ui.Services;
using LanBeam.Core;
using LanBeam.Core.Models;
using LanBeam.Core.Transfer;

namespace LanBeam.Ui.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly LanBeamNode _node;
    private readonly AvatarCacheService _avatars;

    public ObservableCollection<DeviceItemViewModel> Devices { get; } = [];
    public ObservableCollection<TransferItemViewModel> Transfers { get; } = [];

    public bool HasDevices => Devices.Count > 0;
    public bool NoDevices => Devices.Count == 0;
    public bool HasTransfers => Transfers.Count > 0;
    public bool NoTransfers => Transfers.Count == 0;

    public MainViewModel(LanBeamNode node, AvatarCacheService avatars)
    {
        _node = node;
        _avatars = avatars;

        node.Discovery.DeviceUpdated += d => OnUi(() => UpsertDevice(d));
        node.Discovery.DeviceLost += id => OnUi(() =>
        {
            DeviceItemViewModel? item = Devices.FirstOrDefault(d => d.DeviceId == id);
            if (item is not null) Devices.Remove(item);
            RaiseDeviceCounts();
        });
        node.TransferAdded += h => OnUi(() => AddTransfer(h));
        node.TrustedDevices.Changed += () => OnUi(RefreshTrust);
        avatars.AvatarReady += id => OnUi(() => RefreshAvatar(id));

        Devices.CollectionChanged += (_, _) => RaiseDeviceCounts();
        Transfers.CollectionChanged += (_, _) => RaiseTransferCounts();
    }

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private void RaiseDeviceCounts()
    {
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(NoDevices));
    }

    private void RaiseTransferCounts()
    {
        OnPropertyChanged(nameof(HasTransfers));
        OnPropertyChanged(nameof(NoTransfers));
    }

    private void UpsertDevice(DeviceInfo device)
    {
        DeviceItemViewModel? item = Devices.FirstOrDefault(d => d.DeviceId == device.DeviceId);
        if (item is null)
        {
            item = new DeviceItemViewModel(device) { IsTrusted = _node.TrustedDevices.IsTrusted(device.CertFingerprint) };
            Devices.Add(item);
        }
        else
        {
            item.Update(device);
            item.IsTrusted = _node.TrustedDevices.IsTrusted(device.CertFingerprint);
        }
        item.ApplyAvatar(_avatars.TryGet(device.AvatarTag));
        _avatars.EnsureFetched(device);
    }

    private void RefreshAvatar(string deviceId)
    {
        DeviceItemViewModel? item = Devices.FirstOrDefault(d => d.DeviceId == deviceId);
        item?.ApplyAvatar(_avatars.TryGet(item.Device.AvatarTag));
    }

    private void AddTransfer(TransferHandle handle) => Transfers.Insert(0, new TransferItemViewModel(handle));

    private void RefreshTrust()
    {
        foreach (DeviceItemViewModel d in Devices)
            d.IsTrusted = _node.TrustedDevices.IsTrusted(d.Device.CertFingerprint);
    }
}
