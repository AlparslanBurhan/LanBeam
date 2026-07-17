using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using LanBeam.Ui.Localization;
using LanBeam.Ui.Services;
using LanBeam.Core.Models;

namespace LanBeam.Ui.Views;

public partial class SettingsView : UserControl
{
    private bool _loading = true;
    private bool _subscribed;

    private sealed record TrustedItem(string DeviceId, string Name, string FingerprintShort);

    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadFromSettings();
    }


    private void LoadFromSettings()
    {
        _loading = true;
        var s = App.Node.Settings.Current;

        DeviceNameBox.Text = s.DeviceName;
        DownloadFolderBox.Text = s.DownloadFolder;
        AlwaysAskBox.IsChecked = s.AlwaysAskDestination;
        StreamSlider.Value = s.StreamCount;
        StreamCountText.Text = s.StreamCount.ToString();
        PortBox.Text = s.TcpPort.ToString();
        TrayBox.IsChecked = s.MinimizeToTray;
        LanguageBox.SelectedIndex = Loc.CurrentLanguage == "en" ? 1 : 0;

        BuildPresetButtons();
        UpdateAvatarPreview();
        RefreshTrustedList();
        RefreshTexts();

        if (!_subscribed)
        {
            _subscribed = true;
            App.Node.TrustedDevices.Changed += OnTrustedChanged;
            Loc.LanguageChanged += RefreshTexts;
            Unloaded += (_, _) =>
            {
                App.Node.TrustedDevices.Changed -= OnTrustedChanged;
                Loc.LanguageChanged -= RefreshTexts;
            };
        }
        _loading = false;
    }

    private void OnTrustedChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshTrustedList);

    private void RefreshTexts()
    {
        Version? v = Assembly.GetExecutingAssembly().GetName().Version;
        string ver = v is null ? "" : $"{v.Major}.{v.Minor}.{v.Build}";
        // Veri klasörü yolu (ve %APPDATA% gibi token'lar) yalnızca Windows'ta anlamlı; macOS/Linux'ta gizle.
        AboutText.Text = OperatingSystem.IsWindows()
            ? Loc.Format("Str_AboutFormat", ver, FriendlyPath(App.Node.Settings.DataDirectory))
            : Loc.Format("Str_AboutNoPath", ver);
        FingerprintText.Text = Loc.Format("Str_FingerprintFormat", App.Node.CertFingerprint);
    }

    private static string FriendlyPath(string full)
    {
        (Environment.SpecialFolder folder, string token)[] map =
        [
            (Environment.SpecialFolder.ApplicationData, "%APPDATA%"),
            (Environment.SpecialFolder.LocalApplicationData, "%LOCALAPPDATA%"),
            (Environment.SpecialFolder.UserProfile, "~"),
        ];
        foreach ((Environment.SpecialFolder folder, string token) in map)
        {
            string prefix = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(prefix) && full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return token + full[prefix.Length..];
        }
        return full;
    }

    private void Save()
    {
        if (!_loading) App.Node.Settings.Save();
    }

    private void Language_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        string lang = LanguageBox.SelectedIndex == 1 ? "en" : "tr";
        App.Node.Settings.Current.Language = lang;
        Save();
        Loc.Apply(lang);
    }

    // ----- Avatar -----

    private void BuildPresetButtons()
    {
        if (PresetPanel.Children.Count > 0) return;
        for (int i = 0; i < AvatarPalette.Count; i++)
        {
            (string glyph, IBrush brush) = AvatarPalette.Get(i);
            var button = new Button
            {
                Margin = new Avalonia.Thickness(0, 4, 8, 4),
                Padding = new Avalonia.Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Avalonia.Thickness(0),
                Tag = i,
                Content = new Border
                {
                    Width = 40,
                    Height = 40,
                    CornerRadius = new Avalonia.CornerRadius(20),
                    Background = brush,
                    Child = new Viewbox
                    {
                        Width = 22,
                        Height = 22,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = glyph },
                    },
                },
            };
            button.Click += PresetButton_Click;
            PresetPanel.Children.Add(button);
        }
    }

    private void PresetButton_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not int presetId) return;
        App.Node.Settings.Current.AvatarId = $"preset:{presetId}";
        Save();
        App.Node.RefreshAvatar();
        UpdateAvatarPreview();
    }

    private async void PickPhoto_Click(object? sender, RoutedEventArgs e)
    {
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Get("Str_PickAvatar"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Image") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp"] }],
        });
        if (files.Count == 0 || files[0].Path.LocalPath is not { Length: > 0 } path) return;

        try
        {
            byte[] png = AvatarImageHelper.ProcessToSquarePng(path);
            File.WriteAllBytes(App.Node.CustomAvatarPath, png);
            App.Node.Settings.Current.AvatarId = "custom";
            Save();
            App.Node.RefreshAvatar();
            UpdateAvatarPreview();
        }
        catch (Exception) { }
    }

    private void UpdateAvatarPreview()
    {
        string tag = App.Node.AvatarTag;
        if (AvatarTags.IsImage(tag) && File.Exists(App.Node.CustomAvatarPath))
        {
            PreviewImage.Source = new Bitmap(App.Node.CustomAvatarPath);
            PreviewImage.IsVisible = true;
            PreviewGlyph.IsVisible = false;
        }
        else
        {
            (string glyph, IBrush brush) = AvatarPalette.ForDevice(tag, App.Node.Settings.Current.DeviceId);
            PreviewGlyph.Text = glyph;
            PreviewGlyph.IsVisible = true;
            PreviewCircle.Background = brush;
            PreviewImage.IsVisible = false;
        }
    }

    // ----- Diğer -----

    private void DeviceName_LostFocus(object? sender, RoutedEventArgs e)
    {
        string name = (DeviceNameBox.Text ?? "").Trim();
        if (name.Length == 0) { DeviceNameBox.Text = App.Node.Settings.Current.DeviceName; return; }
        App.Node.Settings.Current.DeviceName = name;
        Save();
    }

    private async void ChangeFolder_Click(object? sender, RoutedEventArgs e)
    {
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        IReadOnlyList<IStorageFolder> folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Loc.Get("Str_ChooseDownloadFolder"),
        });
        if (folders.Count > 0 && folders[0].Path.LocalPath is { Length: > 0 } path)
        {
            App.Node.Settings.Current.DownloadFolder = path;
            DownloadFolderBox.Text = path;
            Save();
        }
    }

    private void AlwaysAsk_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Node.Settings.Current.AlwaysAskDestination = AlwaysAskBox.IsChecked == true;
        Save();
    }

    private void StreamSlider_Changed(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Slider.ValueProperty || StreamCountText is null) return;
        int value = (int)StreamSlider.Value;
        StreamCountText.Text = value.ToString();
        if (_loading) return;
        App.Node.Settings.Current.StreamCount = value;
        Save();
    }

    private void Port_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (int.TryParse(PortBox.Text, out int port) && port is > 1024 and < 65536)
        {
            App.Node.Settings.Current.TcpPort = port;
            Save();
        }
        else PortBox.Text = App.Node.Settings.Current.TcpPort.ToString();
    }

    private void Tray_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Node.Settings.Current.MinimizeToTray = TrayBox.IsChecked == true;
        Save();
    }

    private void RefreshTrustedList()
    {
        List<TrustedItem> items = App.Node.TrustedDevices.All()
            .OrderBy(d => d.Name)
            .Select(d => new TrustedItem(d.DeviceId, d.Name, d.CertFingerprint[..16] + "…"))
            .ToList();
        TrustedList.ItemsSource = items;
        NoTrustedText.IsVisible = items.Count == 0;
    }

    private void RemoveTrusted_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is string deviceId)
            App.Node.TrustedDevices.Remove(deviceId);
    }
}
