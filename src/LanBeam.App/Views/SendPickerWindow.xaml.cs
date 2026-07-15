using System.IO;
using System.Windows;
using LanBeam.App.Services;
using LanBeam.App.ViewModels;

namespace LanBeam.App.Views;

public partial class SendPickerWindow
{
    private readonly string[] _paths;

    public SendPickerWindow(string[] paths)
    {
        InitializeComponent();
        _paths = paths;
        DataContext = App.MainVm;

        PathsText.Text = LanBeam.App.Localization.Loc.Format(
            "Str_ToSendFormat", string.Join(", ", paths.Select(Path.GetFileName)));
        UpdateEmptyState();
        App.MainVm.Devices.CollectionChanged += (_, _) => UpdateEmptyState();
        Activate();
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = App.MainVm.Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
            Close();
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DeviceItemViewModel device)
        {
            AppFlows.SendPaths(device.Device, _paths);
            Close();
        }
    }
}
