using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace LanBeam.App;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DevicesSection is null) return; // ilk yükleme sırasında

        int index = NavList.SelectedIndex;
        DevicesSection.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        TransfersSection.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        SettingsSection.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Transferler sekmesine geçer (gönderim başlatılınca çağrılır).</summary>
    public void ShowTransfers() => NavList.SelectedIndex = 1;

    protected override void OnClosing(CancelEventArgs e)
    {
        // Kapatma = tray'e küçült (ayarlıysa); alıcı olabilmek için arka planda çalışmaya devam.
        if (App.Node.Settings.Current.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            App.Instance.ExitApplication();
        }
        base.OnClosing(e);
    }
}
