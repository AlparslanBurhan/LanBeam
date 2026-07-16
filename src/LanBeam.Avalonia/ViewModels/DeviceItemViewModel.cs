using Avalonia.Media;
using Avalonia.Media.Imaging;
using LanBeam.Ui.Services;
using LanBeam.Core.Models;

namespace LanBeam.Ui.ViewModels;

public sealed class DeviceItemViewModel : ObservableObject
{
    private string _name;
    private string _address;
    private bool _isTrusted;
    private Bitmap? _avatarImage;
    private string _presetGlyph = "";
    private IBrush _presetBrush = Brushes.Gray;

    public DeviceInfo Device { get; private set; }

    public DeviceItemViewModel(DeviceInfo device)
    {
        Device = device;
        _name = device.Name;
        _address = device.Address;
        ApplyAvatar(null);
    }

    public string DeviceId => Device.DeviceId;

    public string Name { get => _name; private set => Set(ref _name, value); }
    public string Address { get => _address; private set => Set(ref _address, value); }
    public bool IsTrusted { get => _isTrusted; set => Set(ref _isTrusted, value); }

    public Bitmap? AvatarImage
    {
        get => _avatarImage;
        private set { Set(ref _avatarImage, value); OnPropertyChanged(nameof(HasImage)); OnPropertyChanged(nameof(ShowGlyph)); }
    }
    public bool HasImage => _avatarImage is not null;
    public bool ShowGlyph => _avatarImage is null;
    public string PresetGlyph { get => _presetGlyph; private set => Set(ref _presetGlyph, value); }
    public IBrush PresetBrush { get => _presetBrush; private set => Set(ref _presetBrush, value); }

    public void Update(DeviceInfo device)
    {
        Device = device;
        Name = device.Name;
        Address = device.Address;
    }

    public void ApplyAvatar(Bitmap? image)
    {
        (string glyph, IBrush brush) = AvatarPalette.ForDevice(Device.AvatarTag, Device.DeviceId);
        PresetGlyph = glyph;
        PresetBrush = brush;
        AvatarImage = image;
    }
}
