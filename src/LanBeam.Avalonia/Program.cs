using System;
using Avalonia;

namespace LanBeam.Ui;

internal static class Program
{
    // Avalonia, third-party API'leri ya da SynchronizationContext'e bağlı kodu AppMain
    // çağrılmadan kullanma.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Görsel tasarımcı da bunu kullanır; kaldırma.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
