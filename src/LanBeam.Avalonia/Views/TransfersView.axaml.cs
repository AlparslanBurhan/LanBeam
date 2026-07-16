using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LanBeam.Ui.ViewModels;

namespace LanBeam.Ui.Views;

public partial class TransfersView : UserControl
{
    private DispatcherTimer? _timer;

    public TransfersView()
    {
        InitializeComponent();
        DataContext = App.MainVm;
        Loaded += (_, _) =>
        {
            _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) =>
            {
                foreach (var item in App.MainVm.Transfers) item.Tick();
            });
            _timer.Start();
        };
        Unloaded += (_, _) => _timer?.Stop();
    }


    private void ClearFinished_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var item in App.MainVm.Transfers.Where(t => t.Handle.IsFinished).ToList())
            App.MainVm.Transfers.Remove(item);
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TransferItemViewModel item && item.Handle.IsFinished)
            App.MainVm.Transfers.Remove(item);
    }
}
