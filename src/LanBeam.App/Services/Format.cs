using LanBeam.App.Localization;

namespace LanBeam.App.Services;

public static class Format
{
    public static string Bytes(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
        };
    }

    public static string Speed(double bytesPerSecond) =>
        Loc.Format("Str_SpeedFormat", (bytesPerSecond / (1024.0 * 1024)).ToString("0.#"));

    public static string Eta(long remainingBytes, double bytesPerSecond)
    {
        if (bytesPerSecond < 1) return "—";
        string sec = Loc.Get("Str_Sec"), min = Loc.Get("Str_Min"), hour = Loc.Get("Str_Hour");
        double seconds = remainingBytes / bytesPerSecond;
        if (seconds < 60) return $"{seconds:0} {sec}";
        if (seconds < 3600) return $"{seconds / 60:0} {min} {seconds % 60:0} {sec}";
        return $"{seconds / 3600:0} {hour} {(seconds % 3600) / 60:0} {min}";
    }
}
