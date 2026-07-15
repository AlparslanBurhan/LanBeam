using System.Collections.ObjectModel;
using System.Windows;
using LanBeam.App.Services;
using LanBeam.Core;
using LanBeam.Core.Models;
using LanBeam.Core.Transfer;

namespace LanBeam.App.ViewModels;

/// <summary>Cihaz ve transfer listelerini çekirdek olaylarından besleyen ana model.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly LanBeamNode _node;
    private readonly AvatarCacheService _avatars;

    public ObservableCollection<DeviceItemViewModel> Devices { get; } = [];
    public ObservableCollection<TransferItemViewModel> Transfers { get; } = [];

    public bool HasDevices => Devices.Count > 0;
    public bool HasTransfers => Transfers.Count > 0;

    public MainViewModel(LanBeamNode node, AvatarCacheService avatars)
    {
        _node = node;
        _avatars = avatars;

        node.Discovery.DeviceUpdated += device => OnUi(() => UpsertDevice(device));
        node.Discovery.DeviceLost += deviceId => OnUi(() =>
        {
            DeviceItemViewModel? item = Devices.FirstOrDefault(d => d.DeviceId == deviceId);
            if (item is not null) Devices.Remove(item);
            OnPropertyChanged(nameof(HasDevices));
        });
        node.TransferAdded += handle => OnUi(() => AddTransfer(handle));
        node.TrustedDevices.Changed += () => OnUi(RefreshTrust);
        avatars.AvatarReady += deviceId => OnUi(() => RefreshAvatar(deviceId));

        Devices.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDevices));
        Transfers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTransfers));
    }

    private static void OnUi(Action action)
    {
        if (Application.Current?.Dispatcher is { } d && !d.CheckAccess())
            d.BeginInvoke(action);
        else
            action();
    }

    private void UpsertDevice(DeviceInfo device)
    {
        DeviceItemViewModel? item = Devices.FirstOrDefault(d => d.DeviceId == device.DeviceId);
        if (item is null)
        {
            item = new DeviceItemViewModel(device)
            {
                IsTrusted = _node.TrustedDevices.IsTrusted(device.CertFingerprint),
            };
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

    private void AddTransfer(TransferHandle handle)
    {
        Transfers.Insert(0, new TransferItemViewModel(handle));
    }

    private void RefreshTrust()
    {
        foreach (DeviceItemViewModel device in Devices)
            device.IsTrusted = _node.TrustedDevices.IsTrusted(device.Device.CertFingerprint);
    }
}
