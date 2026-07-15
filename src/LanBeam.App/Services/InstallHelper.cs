using System.IO;
using System.Runtime.InteropServices;

namespace LanBeam.App.Services;

/// <summary>
/// "Kur" adımı: uygulamayı %LOCALAPPDATA%\LanBeam altına sabit bir konuma kopyalar,
/// masaüstü kısayolu oluşturur ve sağ tık menüsü + otomatik başlatmayı bu sabit konuma
/// yönlendirir. Böylece exe elle taşınsa bile kısayol/menü bozulmaz.
/// </summary>
public static class InstallHelper
{
    public static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanBeam");

    public static string InstalledExePath => Path.Combine(InstallDir, "LanBeam.App.exe");

    public static string DesktopShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "LanBeam.lnk");

    /// <summary>Uygulama sabit kurulum konumundan mı çalışıyor?</summary>
    public static bool IsRunningFromInstallDir() =>
        string.Equals(Environment.ProcessPath, InstalledExePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Tam kurulum: kopyala + kısayol + sağ tık menüsü + (etkinse) otomatik başlatma → sabit konum.
    /// Kurulan exe yolunu döndürür.
    /// </summary>
    public static string Install(bool contextMenuEnabled, bool autoStartEnabled)
    {
        Directory.CreateDirectory(InstallDir);

        string current = Environment.ProcessPath
            ?? throw new InvalidOperationException("Çalışan exe yolu belirlenemedi.");

        // Zaten kurulum konumundan çalışmıyorsak kopyala (çalışan exe okunabilir → kopyalanabilir).
        if (!IsRunningFromInstallDir())
            File.Copy(current, InstalledExePath, overwrite: true);

        CreateDesktopShortcut(InstalledExePath);

        // Menü/otostart kayıtlıysa sabit konuma yeniden yönlendir.
        if (contextMenuEnabled)
            ExplorerIntegration.Install(InstalledExePath);
        if (autoStartEnabled)
            AutoStartHelper.SetEnabled(true, InstalledExePath);

        return InstalledExePath;
    }

    public static void CreateDesktopShortcut(string targetExe)
    {
        CreateShortcut(DesktopShortcutPath, targetExe, InstallDir, "LanBeam — LAN dosya transferi");
    }

    /// <summary>WScript.Shell COM üzerinden .lnk kısayolu oluşturur (ek bağımlılık gerektirmez).</summary>
    private static void CreateShortcut(string shortcutPath, string target, string workingDir, string description)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
            throw new InvalidOperationException("Kısayol oluşturma bileşeni bulunamadı.");

        dynamic? shell = Activator.CreateInstance(shellType);
        if (shell is null)
            throw new InvalidOperationException("Kısayol oluşturulamadı.");

        try
        {
            dynamic link = shell.CreateShortcut(shortcutPath);
            link.TargetPath = target;
            link.WorkingDirectory = workingDir;
            link.Description = description;
            link.IconLocation = target + ",0";
            link.Save();
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
