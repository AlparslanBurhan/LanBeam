using Microsoft.Win32;

namespace LanBeam.App.Services;

/// <summary>
/// Explorer sağ tık menüsü: HKCU altında kayıt (yönetici gerektirmez).
/// Windows 11'de "Diğer seçenekleri göster" altında görünür.
/// </summary>
public static class ExplorerIntegration
{
    private const string MenuText = "LanBeam ile gönder";
    private static readonly string[] BaseKeys =
    [
        @"Software\Classes\*\shell\LanBeam",
        @"Software\Classes\Directory\shell\LanBeam",
    ];

    public static bool IsInstalled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(BaseKeys[0]);
        return key is not null;
    }

    /// <summary>Kayıtlı komuttaki exe yolunu döndürür (yoksa null).</summary>
    public static string? GetRegisteredExePath()
    {
        using RegistryKey? cmd = Registry.CurrentUser.OpenSubKey(BaseKeys[0] + @"\command");
        if (cmd?.GetValue(null) is not string command)
            return null;

        // Komut: "C:\...\LanBeam.App.exe" --send "%1"  → ilk tırnaklı bölümü çıkar.
        int first = command.IndexOf('"');
        if (first < 0) return null;
        int second = command.IndexOf('"', first + 1);
        return second > first ? command.Substring(first + 1, second - first - 1) : null;
    }

    public static void Install(string exePath)
    {
        foreach (string baseKey in BaseKeys)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(baseKey);
            key.SetValue(null, MenuText);
            key.SetValue("Icon", $"\"{exePath}\",0");
            using RegistryKey cmd = key.CreateSubKey("command");
            cmd.SetValue(null, $"\"{exePath}\" --send \"%1\"");
        }
    }

    public static void Uninstall()
    {
        foreach (string baseKey in BaseKeys)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(baseKey, throwOnMissingSubKey: false); }
            catch (Exception) { }
        }
    }
}
