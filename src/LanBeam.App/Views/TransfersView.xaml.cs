using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LanBeam.App.ViewModels;

namespace LanBeam.App.Views;

public partial class TransfersView : UserControl
{
    private DispatcherTimer? _timer;

    public TransfersView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            DataContext = App.MainVm;
            UpdateEmptyState();
            App.MainVm.Transfers.CollectionChanged += (_, _) => UpdateEmptyState();

            _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
                (_, _) =>
                {
                    foreach (var item in App.MainVm.Transfers)
                        item.Tick();
                }, Dispatcher);
            _timer.Start();
        };
        Unloaded += (_, _) => _timer?.Stop();
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = App.MainVm.Transfers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearFinished_Click(object sender, RoutedEventArgs e)
    {
        // Aktif transferlere dokunma; yalnızca bitenleri kaldır.
        var finished = App.MainVm.Transfers.Where(t => t.Handle.IsFinished).ToList();
        foreach (var item in finished)
            App.MainVm.Transfers.Remove(item);
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TransferItemViewModel item &&
            item.Handle.IsFinished)
        {
            App.MainVm.Transfers.Remove(item);
        }
    }
}
