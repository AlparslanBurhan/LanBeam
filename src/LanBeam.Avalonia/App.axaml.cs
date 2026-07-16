using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using LanBeam.Ui.Localization;
using LanBeam.Ui.Services;
using LanBeam.Ui.ViewModels;
using LanBeam.Ui.Views;
using LanBeam.Core;

namespace LanBeam.Ui;

public partial class App : Application
{
    private LanBeamNode? _node;
    private TrayIcon? _tray;
    private MainWindow? _mainWindow;

    public static App Instance => (App)Current!;
    public static LanBeamNode Node => Instance._node!;
    public static MainViewModel MainVm { get; private set; } = null!;
    public static AvatarCacheService Avatars { get; private set; } = null!;
    public static Window? MainWindowInstance => Instance._mainWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        string? dataDir = ParseDataDir(desktop.Args ?? []);

        _node = new LanBeamNode(dataDir);
        Avatars = new AvatarCacheService(_node);
        MainVm = new MainViewModel(_node, Avatars);

        Loc.Apply(Loc.Resolve(_node.Settings.Current.Language));

        _node.Listener.PairingStarted += prompt => Dispatcher.UIThread.Post(() =>
            new PairingPinWindow(prompt).Show());
        _node.Listener.OfferReceived += offer => Dispatcher.UIThread.Post(() =>
            new IncomingOfferWindow(offer).Show());

        _node.Start();

        // Pencere kapatılınca uygulama kapanmasın; menü çubuğunda (tray) dinlemeye devam.
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        CreateTray();
        _mainWindow = new MainWindow();
        _mainWindow.Show();

        desktop.Exit += (_, _) => _node?.Dispose();
        base.OnFrameworkInitializationCompleted();
    }

    private static string? ParseDataDir(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count - 1; i++)
            if (args[i] == "--datadir")
                return args[i + 1];
        return null;
    }

    private void CreateTray()
    {
        WindowIcon icon = LoadIcon();
        var open = new NativeMenuItem(Loc.Get("Str_OpenLanBeam"));
        open.Click += (_, _) => ShowMainWindow();
        var exit = new NativeMenuItem(Loc.Get("Str_Exit"));
        exit.Click += (_, _) => ExitApp();

        _tray = new TrayIcon
        {
            Icon = icon,
            ToolTipText = Loc.Get("Str_TrayTooltip"),
            Menu = new NativeMenu { Items = { open, new NativeMenuItemSeparator(), exit } },
        };
        _tray.Clicked += (_, _) => ShowMainWindow();
        TrayIcon.SetIcons(this, new TrayIcons { _tray });
    }

    public static WindowIcon LoadIcon()
    {
        using Stream s = AssetLoader.Open(new Uri("avares://LanBeam/Assets/lanbeam.png"));
        return new WindowIcon(s);
    }

    public void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        if (!_mainWindow.IsVisible) _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void ExitApp()
    {
        _tray?.Dispose();
        _node?.Dispose();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
