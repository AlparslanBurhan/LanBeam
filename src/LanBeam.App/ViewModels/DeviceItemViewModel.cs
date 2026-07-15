using System.Windows.Media;
using LanBeam.App.Services;
using LanBeam.Core.Models;

namespace LanBeam.App.ViewModels;

public sealed class DeviceItemViewModel : ObservableObject
{
    private string _name;
    private string _address;
    private bool _isTrusted;
    private ImageSource? _avatarImage;
    private string _presetGlyph = "";
    private Brush _presetBrush = Brushes.Gray;

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

    /// <summary>Özel fotoğraf (indirildiyse); yoksa hazır silüet gösterilir.</summary>
    public ImageSource? AvatarImage { get => _avatarImage; private set { Set(ref _avatarImage, value); OnPropertyChanged(nameof(HasImage)); } }
    public bool HasImage => _avatarImage is not null;
    public string PresetGlyph { get => _presetGlyph; private set => Set(ref _presetGlyph, value); }
    public Brush PresetBrush { get => _presetBrush; private set => Set(ref _presetBrush, value); }

    public void Update(DeviceInfo device)
    {
        Device = device;
        Name = device.Name;
        Address = device.Address;
    }

    /// <summary>Avatar görünümünü etikete göre ayarlar (image null ise silüete düşer).</summary>
    public void ApplyAvatar(ImageSource? image)
    {
        (string glyph, Brush brush) = AvatarPresets.ForDevice(Device.AvatarTag, Device.DeviceId);
        PresetGlyph = glyph;
        PresetBrush = brush;
        AvatarImage = image;
    }
}
