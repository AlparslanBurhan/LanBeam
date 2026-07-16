using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LanBeam.Ui.Localization;
using LanBeam.Ui.Services;
using LanBeam.Core.Transfer;

namespace LanBeam.Ui.Views;

public partial class IncomingOfferWindow : Window
{
    private readonly IncomingOffer _offer = null!;
    private bool _decided;

    public IncomingOfferWindow() => InitializeComponent();

    public IncomingOfferWindow(IncomingOffer offer) : this()
    {
        Icon = App.LoadIcon();
        _offer = offer;

        SenderText.Text = Loc.Format("Str_WantsToSendFormat", offer.SenderName);
        NameText.Text = offer.Offer.DisplayName;
        DetailText.Text = Loc.Format("Str_OfferDetailFormat", Format.Bytes(offer.Offer.TotalBytes), offer.Offer.FileCount);

        var s = App.Node.Settings.Current;
        DestinationText.Text = s.AlwaysAskDestination
            ? Loc.Get("Str_WillAskFolder")
            : Loc.Format("Str_SaveToFormat", s.DownloadFolder);

        offer.Revoked += () => Dispatcher.UIThread.Post(() => { _decided = true; Close(); });
        Closed += (_, _) => { if (!_decided) _offer.Reject(); };
    }


    private async void Accept_Click(object? sender, RoutedEventArgs e)
    {
        var s = App.Node.Settings.Current;
        string destination;

        if (s.AlwaysAskDestination)
        {
            IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Loc.Get("Str_ChooseSaveFolder"),
            });
            if (folders.Count == 0 || folders[0].Path.LocalPath is not { Length: > 0 } picked) return;
            destination = picked;
        }
        else destination = s.DownloadFolder;

        try { Directory.CreateDirectory(destination); }
        catch (System.Exception) { return; }

        _decided = true;
        _offer.Accept(destination);
        (App.MainWindowInstance as MainWindow)?.ShowTransfers();
        Close();
    }

    private void Reject_Click(object? sender, RoutedEventArgs e)
    {
        _decided = true;
        _offer.Reject();
        Close();
    }
}
