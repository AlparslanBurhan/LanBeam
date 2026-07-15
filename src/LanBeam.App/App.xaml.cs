using System.IO;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using LanBeam.App.Localization;
using LanBeam.App.Services;
using LanBeam.App.ViewModels;
using LanBeam.App.Views;
using LanBeam.Core;

namespace LanBeam.App;

public partial class App : Application
{
    private LanBeamNode? _node;
    private SingleInstance? _instance;
    private TaskbarIcon? _tray;
    private MainWindow? _mainWindow;

    public static App Instance => (App)Current;
    public static LanBeamNode Node => Instance._node!;
    public static MainViewModel MainVm { get; private set; } = null!;
    public static AvatarCacheService Avatars { get; private set; } = null!;
    public static string ExePath => Environment.ProcessPath!;
    public MainWindow? MainWindowInstance => _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        (string? dataDir, string[] sendPaths, bool startInTray) = ParseArgs(e.Args);
        string effectiveDataDir = dataDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LanBeam");

        _instance = new SingleInstance(effectiveDataDir);
        if (!_instance.IsFirstInstance)
        {
            // İkinci kopya: yolları (varsa) çalışan örneğe ilet. Yol yoksa bile ilet ki
            // çalışan örnek ana pencereyi öne getirsin (kısayola tekrar tıklama davranışı).
            _instance.ForwardToRunningInstance(sendPaths);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(Loc.Format("Str_UnexpectedErrorFormat", args.Exception.Message), "LanBeam",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        _node = new LanBeamNode(dataDir);
        Avatars = new AvatarCacheService(_node);
        MainVm = new MainViewModel(_node, Avatars);

        // Arayüz dilini ayardan (ya da sistem dilinden) uygula — pencerelerden önce olmalı.
        Loc.Apply(Loc.Resolve(_node.Settings.Current.Language));

        _node.Listener.PairingStarted += prompt => Dispatcher.BeginInvoke(() =>
            new PairingPinWindow(prompt).Show());
        _node.Listener.OfferReceived += offer => Dispatcher.BeginInvoke(() =>
            new IncomingOfferWindow(offer).Show());

        _instance.SendRequested += paths => Dispatcher.BeginInvoke(() =>
        {
            if (paths.Length > 0)
                OpenSendPicker(paths);
            else
                ShowMainWindow(); // yolsuz istek = "beni öne getir"
        });
        _instance.StartServer();

        _node.Start();

        // Sağ tık "gönder" ile ya da --tray (otomatik başlatma) ile açıldıysa ana pencereyi
        // hiç gösterme; tray'de başla.
        bool openHidden = startInTray || sendPaths.Length > 0;

        CreateTrayIcon();
        _mainWindow = new MainWindow();
        if (!openHidden)
            _mainWindow.Show();

        // Sağ tık menüsü kayıtlıysa ve exe taşınmışsa kaydı mevcut konuma göre onar.
        HealContextMenuIfMoved();

        if (sendPaths.Length > 0)
            OpenSendPicker(sendPaths);

        EnsureFirewallOnFirstRun();
    }

    private static (string? DataDir, string[] SendPaths, bool Tray) ParseArgs(string[] args)
    {
        string? dataDir = null;
        var sendPaths = new List<string>();
        bool tray = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--datadir" when i + 1 < args.Length:
                    dataDir = args[++i];
                    break;
                case "--send" when i + 1 < args.Length:
                    sendPaths.Add(args[++i]);
                    break;
                case "--tray":
                    tray = true;
                    break;
            }
        }
        return (dataDir, sendPaths.ToArray(), tray);
    }

    private void CreateTrayIcon()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var openItem = new System.Windows.Controls.MenuItem { Header = Loc.Get("Str_OpenLanBeam") };
        openItem.Click += (_, _) => ShowMainWindow();
        var exitItem = new System.Windows.Controls.MenuItem { Header = Loc.Get("Str_Exit") };
        exitItem.Click += (_, _) => ExitApplication();
        menu.Items.Add(openItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(exitItem);

        _tray = new TaskbarIcon
        {
            ToolTipText = Loc.Get("Str_TrayTooltip"),
            IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Assets/lanbeam.ico")),
            ContextMenu = menu,
        };
        _tray.TrayLeftMouseUp += (_, _) => ShowMainWindow();
    }

    public void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void ExitApplication()
    {
        _tray?.Dispose();
        _node?.Dispose();
        _instance?.Dispose();
        Shutdown();
    }

    public void OpenSendPicker(string[] paths)
    {
        string[] existing = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        if (existing.Length == 0) return;
        new SendPickerWindow(existing).Show();
    }

    /// <summary>Sağ tık menüsü kayıtlıysa ve exe farklı bir konuma taşınmışsa kaydı günceller.</summary>
    private void HealContextMenuIfMoved()
    {
        try
        {
            if (ExplorerIntegration.IsInstalled() &&
                !string.Equals(ExplorerIntegration.GetRegisteredExePath(), ExePath, StringComparison.OrdinalIgnoreCase))
            {
                ExplorerIntegration.Install(ExePath); // aynı anahtarların üzerine yazar
            }
        }
        catch (Exception) { }
    }

    private void EnsureFirewallOnFirstRun()
    {
        if (_node!.Settings.Current.FirewallConfigured || _node.Settings.Current.FirewallPromptShown)
            return;

        _node.Settings.Current.FirewallPromptShown = true;
        _node.Settings.Save();

        MessageBoxResult result = MessageBox.Show(
            Loc.Get("Str_FirewallPrompt"), Loc.Get("Str_FirewallTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes && FirewallHelper.EnsureRule(ExePath))
        {
            _node.Settings.Current.FirewallConfigured = true;
            _node.Settings.Save();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _node?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
