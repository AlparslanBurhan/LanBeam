using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LanBeam.Core;

/// <summary>
/// Transfer sürerken sistemin ve ekranın boşta kalınca uykuya geçmesini engeller.
/// Kapak kapatma ya da güç düğmesi bununla engellenemez (işletim sistemi kararı).
/// Windows: güç isteği (iş parçacığına bağlı değil). macOS: süreç ölünce kendiliğinden biten caffeinate.
/// </summary>
internal sealed class KeepAwake : IDisposable
{
    private IntPtr _request;
    private Process? _caffeinate;

    /// <summary>Uyku engeli şu an etkin mi?</summary>
    public bool IsActive => _request != IntPtr.Zero || _caffeinate is not null;

    public void Set(bool on)
    {
        try
        {
            if (on) Acquire();
            else Release();
        }
        catch (Exception)
        {
            // Uyku engeli konfor özelliği: alınamazsa transfer yine sürer.
        }
    }

    private void Acquire()
    {
        if (OperatingSystem.IsWindows() && _request == IntPtr.Zero)
        {
            var reason = new ReasonContext { Version = 0, Flags = 1, SimpleReasonString = "LanBeam dosya aktarıyor" };
            IntPtr request = PowerCreateRequest(ref reason);
            if (request == new IntPtr(-1)) return;
            if (!PowerSetRequest(request, PowerRequestType.DisplayRequired) |
                !PowerSetRequest(request, PowerRequestType.SystemRequired))
            {
                CloseHandle(request);
                return;
            }
            _request = request;
        }
        else if (OperatingSystem.IsMacOS() && _caffeinate is null)
        {
            // -d ekran, -i sistem boşta uykusu; -w: uygulama çökse bile caffeinate onunla biter.
            _caffeinate = Process.Start("/usr/bin/caffeinate", $"-di -w {Environment.ProcessId}");
        }
    }

    private void Release()
    {
        if (_request != IntPtr.Zero)
        {
            PowerClearRequest(_request, PowerRequestType.DisplayRequired);
            PowerClearRequest(_request, PowerRequestType.SystemRequired);
            CloseHandle(_request);
            _request = IntPtr.Zero;
        }
        if (_caffeinate is not null)
        {
            try { _caffeinate.Kill(); } catch (Exception) { }
            _caffeinate.Dispose();
            _caffeinate = null;
        }
    }

    public void Dispose() => Set(false);

    private enum PowerRequestType { DisplayRequired = 0, SystemRequired = 1 }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ReasonContext
    {
        public uint Version;
        public uint Flags; // POWER_REQUEST_CONTEXT_SIMPLE_STRING
        [MarshalAs(UnmanagedType.LPWStr)] public string SimpleReasonString;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr PowerCreateRequest(ref ReasonContext context);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PowerSetRequest(IntPtr request, PowerRequestType type);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PowerClearRequest(IntPtr request, PowerRequestType type);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
