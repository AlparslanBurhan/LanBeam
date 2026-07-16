using Avalonia.Controls;

namespace LanBeam.Ui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Icon = App.LoadIcon();
        NavList.SelectedIndex = 0;
    }

    private void Nav_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DevicesSection is null) return;
        int i = NavList.SelectedIndex;
        DevicesSection.IsVisible = i == 0;
        TransfersSection.IsVisible = i == 1;
        SettingsSection.IsVisible = i == 2;
    }

    public void ShowTransfers()
    {
        NavList.SelectedIndex = 1;
        App.Instance.ShowMainWindow();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Pencere kapatılınca (ayarlıysa) menü çubuğunda çalışmaya devam et.
        if (App.Node.Settings.Current.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            App.Instance.ExitApp();
        }
        base.OnClosing(e);
    }
}
