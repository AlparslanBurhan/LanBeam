using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LanBeam.Ui.Localization;
using LanBeam.Ui.Services;
using LanBeam.Ui.ViewModels;

namespace LanBeam.Ui.Views;

public partial class DevicesView : UserControl
{
    public DevicesView()
    {
        InitializeComponent();
        DataContext = App.MainVm;
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        Loaded += (_, _) =>
        {
            UpdateSelfInfo();
            Loc.LanguageChanged += UpdateSelfInfo;
        };
        Unloaded += (_, _) => Loc.LanguageChanged -= UpdateSelfInfo;
    }


    private void UpdateSelfInfo() =>
        SelfInfoText.Text = Loc.Format("Str_ThisDeviceFormat", App.Node.Settings.Current.DeviceName);

    private static DeviceItemViewModel? DeviceOf(object? sender) =>
        (sender as Control)?.DataContext as DeviceItemViewModel;

    private async void SendFiles_Click(object? sender, RoutedEventArgs e)
    {
        if (DeviceOf(sender) is { } d) await AppFlows.PickFilesAndSend(this, d.Device);
    }

    private async void SendFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (DeviceOf(sender) is { } d) await AppFlows.PickFolderAndSend(this, d.Device);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DeviceOf(e.Source) is not { } device) return;
        var files = e.Data.GetFiles();
        if (files is null) return;
        var paths = new System.Collections.Generic.List<string>();
        foreach (var f in files)
            if (f.Path.LocalPath is { Length: > 0 } p) paths.Add(p);
        if (paths.Count > 0) AppFlows.SendPaths(device.Device, paths);
    }
}
