using System.Diagnostics;
using System.IO;

namespace LanBeam.App.Services;

/// <summary>
/// Windows Güvenlik Duvarı'na gelen bağlantı kuralı ekler (UAC istemiyle yükseltilmiş netsh).
/// Kural programa bağlıdır: yalnızca LanBeam.exe'nin dinlediği portlara izin verir.
/// </summary>
public static class FirewallHelper
{
    private const string RuleName = "LanBeam";

    /// <summary>Kuralı (yeniden) oluşturur. Kullanıcı UAC istemini reddederse false döner.</summary>
    public static bool EnsureRule(string exePath)
    {
        // Yol doğrulaması: yalnızca gerçek, mevcut bir .exe; tırnak/komut ayracı içeren yolu reddet
        // (komut satırı enjeksiyonuna karşı emniyet — yol saldırgan kontrolünde olmasa da).
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath) ||
            exePath.IndexOfAny(['"', '&', '|', '<', '>', '^', '%']) >= 0)
        {
            return false;
        }

        string script =
            $"netsh advfirewall firewall delete rule name=\"{RuleName}\" >nul & " +
            $"netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow " +
            $"program=\"{exePath}\" protocol=TCP profile=private,domain >nul & " +
            $"netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow " +
            $"program=\"{exePath}\" protocol=UDP profile=private,domain >nul";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + script,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using Process? p = Process.Start(psi);
            p?.WaitForExit(15000);
            return p?.ExitCode == 0;
        }
        catch (Exception)
        {
            return false; // kullanıcı UAC'yi reddetti ya da başlatılamadı
        }
    }
}
