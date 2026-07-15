using System.Globalization;
using System.Windows;

namespace LanBeam.App.Localization;

/// <summary>
/// Arayüz dili yönetimi. Dil dizinleri DynamicResource olarak App kaynaklarına yüklenir;
/// XAML'de {DynamicResource Str_...} kullanılır, kod tarafında Loc.Get(...) / Loc.Format(...).
/// Dil değişince tüm DynamicResource bağlamaları canlı güncellenir.
/// </summary>
public static class Loc
{
    private static ResourceDictionary? _current;

    public static string CurrentLanguage { get; private set; } = "tr";

    /// <summary>Dil değişince tetiklenir (kod tarafı metinlerini yeniden kurmak için).</summary>
    public static event Action? LanguageChanged;

    /// <summary>Ayardan ya da sistem dilinden geçerli dili belirler.</summary>
    public static string Resolve(string? setting)
    {
        if (setting is "tr" or "en") return setting;
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";
    }

    public static void Apply(string language)
    {
        language = language is "tr" or "en" ? language : "en";
        CurrentLanguage = language;

        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Localization/Strings.{language}.xaml", UriKind.Absolute),
        };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null)
            merged.Remove(_current);
        merged.Add(dict);
        _current = dict;

        LanguageChanged?.Invoke();
    }

    public static string Get(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object[] args) =>
        string.Format(Get(key), args);
}
