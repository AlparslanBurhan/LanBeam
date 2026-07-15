using System.Windows;
using System.Windows.Controls;
using LanBeam.App.Services;
using LanBeam.App.ViewModels;

namespace LanBeam.App.Views;

public partial class DevicesView : UserControl
{
    public DevicesView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            DataContext = App.MainVm;
            UpdateSelfInfo();
            UpdateEmptyState();
            App.MainVm.Devices.CollectionChanged += (_, _) => UpdateEmptyState();
            LanBeam.App.Localization.Loc.LanguageChanged += UpdateSelfInfo;
        };
        Unloaded += (_, _) => LanBeam.App.Localization.Loc.LanguageChanged -= UpdateSelfInfo;
    }

    private void UpdateSelfInfo()
    {
        SelfInfoText.Text = LanBeam.App.Localization.Loc.Format(
            "Str_ThisDeviceFormat", App.Node.Settings.Current.DeviceName);
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = App.MainVm.Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static DeviceItemViewModel? DeviceOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as DeviceItemViewModel;

    private void SendFiles_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceOf(sender) is { } device)
            AppFlows.PickFilesAndSend(device.Device);
    }

    private void SendFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceOf(sender) is { } device)
            AppFlows.PickFolderAndSend(device.Device);
    }

    private void Card_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Card_Drop(object sender, DragEventArgs e)
    {
        if (DeviceOf(sender) is { } device &&
            e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
        {
            AppFlows.SendPaths(device.Device, paths);
        }
    }
}
