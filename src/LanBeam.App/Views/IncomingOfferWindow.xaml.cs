using System.IO;
using System.Media;
using System.Windows;
using LanBeam.App.Localization;
using LanBeam.App.Services;
using LanBeam.Core.Transfer;

namespace LanBeam.App.Views;

public partial class IncomingOfferWindow
{
    private readonly IncomingOffer _offer;
    private bool _decided;

    public IncomingOfferWindow(IncomingOffer offer)
    {
        InitializeComponent();
        _offer = offer;

        SenderText.Text = Loc.Format("Str_WantsToSendFormat", offer.SenderName);
        NameText.Text = offer.Offer.DisplayName;
        DetailText.Text = Loc.Format("Str_OfferDetailFormat",
            Format.Bytes(offer.Offer.TotalBytes), offer.Offer.FileCount);

        var s = App.Node.Settings.Current;
        DestinationText.Text = s.AlwaysAskDestination
            ? Loc.Get("Str_WillAskFolder")
            : Loc.Format("Str_SaveToFormat", s.DownloadFolder);

        offer.Revoked += () => Dispatcher.BeginInvoke(() => { _decided = true; Close(); });
        Closed += (_, _) => { if (!_decided) _offer.Reject(); };

        SystemSounds.Exclamation.Play();
        Activate();
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Node.Settings.Current;
        string destination;

        if (s.AlwaysAskDestination)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = Loc.Get("Str_ChooseSaveFolder"),
                InitialDirectory = Directory.Exists(s.DownloadFolder) ? s.DownloadFolder : null,
            };
            if (dialog.ShowDialog(this) != true)
                return; // seçim iptal: pencere açık kalsın, kullanıcı tekrar denesin ya da reddetsin
            destination = dialog.FolderName;
        }
        else
        {
            destination = s.DownloadFolder;
        }

        try
        {
            Directory.CreateDirectory(destination);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Klasör oluşturulamadı: {ex.Message}", "LanBeam");
            return;
        }

        _decided = true;
        _offer.Accept(destination);
        App.Instance.ShowMainWindow();
        App.Instance.MainWindowInstance?.ShowTransfers();
        Close();
    }

    private void Reject_Click(object sender, RoutedEventArgs e)
    {
        _decided = true;
        _offer.Reject();
        Close();
    }
}
