using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LanBeam.App.Localization;
using LanBeam.App.Services;
using LanBeam.Core.Models;

namespace LanBeam.App.Views;

public partial class SettingsView : UserControl
{
    private bool _loading = true;
    private bool _trustSubscribed;

    private sealed record TrustedItem(string DeviceId, string Name, string FingerprintShort);

    private void OnTrustedChanged() => Dispatcher.BeginInvoke(RefreshTrustedList);

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
        AutoStartBox.IsChecked = AutoStartHelper.IsEnabled();
        TrayBox.IsChecked = s.MinimizeToTray;
        LanguageBox.SelectedIndex = Loc.CurrentLanguage == "en" ? 1 : 0;
        UpdateContextMenuButton();
        RefreshTrustedList();
        BuildPresetButtons();
        UpdateAvatarPreview();
        RefreshTexts();

        // Tek kez abone ol (Loaded birden çok tetiklenirse handler birikmesin).
        if (!_trustSubscribed)
        {
            _trustSubscribed = true;
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

    /// <summary>Dil değişince kod tarafında kurulan metinleri tazeler (XAML DynamicResource kendiliğinden güncellenir).</summary>
    private void RefreshTexts()
    {
        Version? v = Assembly.GetExecutingAssembly().GetName().Version;
        string ver = v is null ? "" : $"{v.Major}.{v.Minor}.{v.Build}";
        AboutText.Text = Loc.Format("Str_AboutFormat", ver, FriendlyPath(App.Node.Settings.DataDirectory));
        FingerprintText.Text = Loc.Format("Str_FingerprintFormat", App.Node.CertFingerprint);
        UpdateContextMenuButton();
    }

    /// <summary>Kullanıcı klasörü ön eklerini ortam değişkeni adlarıyla değiştirir (kullanıcı adını gizler).</summary>
    private static string FriendlyPath(string full)
    {
        (Environment.SpecialFolder folder, string token)[] map =
        [
            (Environment.SpecialFolder.ApplicationData, "%APPDATA%"),
            (Environment.SpecialFolder.LocalApplicationData, "%LOCALAPPDATA%"),
            (Environment.SpecialFolder.UserProfile, "%USERPROFILE%"),
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
        if (!_loading)
            App.Node.Settings.Save();
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        string lang = (LanguageBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "tr";
        App.Node.Settings.Current.Language = lang;
        Save();
        Loc.Apply(lang); // canlı değişim: tüm DynamicResource bağlamaları güncellenir
    }

    // ----- Avatar -----

    private void BuildPresetButtons()
    {
        if (PresetPanel.Children.Count > 0) return;

        for (int i = 0; i < AvatarPresets.Count; i++)
        {
            (string glyph, Brush brush) = AvatarPresets.Get(i);
            var button = new Button
            {
                Margin = new Thickness(0, 4, 8, 4),
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Tag = i,
                ToolTip = Loc.Get("Str_UseThisAvatar"),
                Content = new Border
                {
                    Width = 40,
                    Height = 40,
                    CornerRadius = new CornerRadius(20),
                    Background = brush,
                    Child = new TextBlock
                    {
                        Text = glyph,
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize = 18,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            };
            button.Click += PresetButton_Click;
            PresetPanel.Children.Add(button);
        }
    }

    private void PresetButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not int presetId) return;
        App.Node.Settings.Current.AvatarId = $"preset:{presetId}";
        Save();
        App.Node.RefreshAvatar();
        UpdateAvatarPreview();
    }

    private void PickPhoto_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.Get("Str_PickAvatar"),
            Filter = Loc.Get("Str_ImageFilter"),
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            byte[] png = AvatarImageHelper.ProcessToSquarePng(dialog.FileName);
            File.WriteAllBytes(App.Node.CustomAvatarPath, png);
            App.Node.Settings.Current.AvatarId = "custom";
            Save();
            App.Node.RefreshAvatar();
            UpdateAvatarPreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Format("Str_PhotoFailedFormat", ex.Message), "LanBeam");
        }
    }

    private void UpdateAvatarPreview()
    {
        string tag = App.Node.AvatarTag;

        if (AvatarTags.IsImage(tag) && File.Exists(App.Node.CustomAvatarPath))
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(App.Node.CustomAvatarPath);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache; // dosya değişince tazelensin
            image.EndInit();

            PreviewImage.Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            PreviewImage.Visibility = Visibility.Visible;
            PreviewPreset.Visibility = Visibility.Collapsed;
        }
        else
        {
            (string glyph, Brush brush) = AvatarPresets.ForDevice(tag, App.Node.Settings.Current.DeviceId);
            PreviewGlyph.Text = glyph;
            PreviewPreset.Background = brush;
            PreviewPreset.Visibility = Visibility.Visible;
            PreviewImage.Visibility = Visibility.Collapsed;
        }
    }

    // ----- Diğer ayarlar -----

    private void DeviceName_LostFocus(object sender, RoutedEventArgs e)
    {
        string name = DeviceNameBox.Text.Trim();
        if (name.Length == 0) { DeviceNameBox.Text = App.Node.Settings.Current.DeviceName; return; }
        App.Node.Settings.Current.DeviceName = name;
        Save();
    }

    private void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Loc.Get("Str_ChooseDownloadFolder"),
            InitialDirectory = App.Node.Settings.Current.DownloadFolder,
        };
        if (dialog.ShowDialog() == true)
        {
            App.Node.Settings.Current.DownloadFolder = dialog.FolderName;
            DownloadFolderBox.Text = dialog.FolderName;
            Save();
        }
    }

    private void AlwaysAsk_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Node.Settings.Current.AlwaysAskDestination = AlwaysAskBox.IsChecked == true;
        Save();
    }

    private void StreamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (StreamCountText is null) return;
        int value = (int)e.NewValue;
        StreamCountText.Text = value.ToString();
        if (_loading) return;
        App.Node.Settings.Current.StreamCount = value;
        Save();
    }

    private void Port_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(PortBox.Text, out int port) && port is > 1024 and < 65536)
        {
            App.Node.Settings.Current.TcpPort = port;
            Save();
        }
        else
        {
            PortBox.Text = App.Node.Settings.Current.TcpPort.ToString();
        }
    }

    private void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool enabled = AutoStartBox.IsChecked == true;
        try
        {
            AutoStartHelper.SetEnabled(enabled, App.ExePath);
            App.Node.Settings.Current.AutoStart = enabled;
            Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Format("Str_AutoStartFailedFormat", ex.Message), "LanBeam");
        }
    }

    private void Tray_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Node.Settings.Current.MinimizeToTray = TrayBox.IsChecked == true;
        Save();
    }

    private void UpdateContextMenuButton()
    {
        ContextMenuButton.Content = ExplorerIntegration.IsInstalled()
            ? Loc.Get("Str_RemoveContextMenu")
            : Loc.Get("Str_AddContextMenu");
    }

    private void ContextMenu_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ExplorerIntegration.IsInstalled())
                ExplorerIntegration.Uninstall();
            else
                ExplorerIntegration.Install(App.ExePath);
            UpdateContextMenuButton();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Format("Str_ContextMenuFailedFormat", ex.Message), "LanBeam");
        }
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool menu = ExplorerIntegration.IsInstalled();
            bool auto = AutoStartHelper.IsEnabled();
            string installed = InstallHelper.Install(contextMenuEnabled: menu, autoStartEnabled: auto);

            UpdateContextMenuButton();

            string body = Loc.Format("Str_InstalledFormat", installed);
            if (menu) body += "\n" + Loc.Get("Str_ContextMenuRedirected");
            if (!InstallHelper.IsRunningFromInstallDir()) body += "\n\n" + Loc.Get("Str_DeleteTempCopy");

            MessageBox.Show(body, Loc.Get("Str_InstallTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Format("Str_InstallFailedFormat", ex.Message), "LanBeam",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Firewall_Click(object sender, RoutedEventArgs e)
    {
        bool ok = FirewallHelper.EnsureRule(App.ExePath);
        if (ok)
        {
            App.Node.Settings.Current.FirewallConfigured = true;
            Save();
        }
        MessageBox.Show(ok ? Loc.Get("Str_FirewallAdded") : Loc.Get("Str_FirewallFailed"), "LanBeam");
    }

    private void RefreshTrustedList()
    {
        List<TrustedItem> items = App.Node.TrustedDevices.All()
            .OrderBy(d => d.Name)
            .Select(d => new TrustedItem(d.DeviceId, d.Name, d.CertFingerprint[..16] + "…"))
            .ToList();
        TrustedList.ItemsSource = items;
        NoTrustedText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RemoveTrusted_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string deviceId)
            App.Node.TrustedDevices.Remove(deviceId);
    }
}
