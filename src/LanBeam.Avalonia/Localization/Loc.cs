using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;

namespace LanBeam.Ui.Localization;

/// <summary>
/// Arayüz dili. Çeviriler C# tablosunda tutulur; Apply() bunları Application kaynaklarına yazar,
/// XAML {DynamicResource Str_...} kullanır. Dil değişince tüm DynamicResource bağlamaları
/// canlı güncellenir.
/// </summary>
public static class Loc
{
    public static string CurrentLanguage { get; private set; } = "tr";
    public static event Action? LanguageChanged;

    public static string Resolve(string? setting)
    {
        if (setting is "tr" or "en") return setting;
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";
    }

    public static void Apply(string language)
    {
        CurrentLanguage = language is "tr" or "en" ? language : "en";
        var res = Application.Current!.Resources;
        foreach ((string key, (string tr, string en) v) in Table)
            res[key] = CurrentLanguage == "en" ? v.en : v.tr;
        LanguageChanged?.Invoke();
    }

    public static string Get(string key) =>
        Application.Current!.Resources.TryGetResource(key, null, out object? v) && v is string s ? s : key;

    public static string Format(string key, params object[] args) => string.Format(Get(key), args);

    // (tr, en)
    private static readonly Dictionary<string, (string tr, string en)> Table = new()
    {
        ["Str_NavDevices"] = ("Cihazlar", "Devices"),
        ["Str_NavTransfers"] = ("Transferler", "Transfers"),
        ["Str_NavSettings"] = ("Ayarlar", "Settings"),

        ["Str_DevicesTitle"] = ("Cihazlar", "Devices"),
        ["Str_ThisDeviceFormat"] = ("Bu cihaz: «{0}» — ağda görünür durumda", "This device: «{0}» — visible on the network"),
        ["Str_SearchingDevices"] = ("Ağda LanBeam açık başka cihaz aranıyor…", "Looking for other devices running LanBeam…"),
        ["Str_SearchingDevicesHint"] = ("Diğer bilgisayarda da LanBeam'i açın. İki cihaz aynı ağda olmalı.", "Open LanBeam on the other computer too. Both must be on the same network."),
        ["Str_Paired"] = ("Eşleştirilmiş", "Paired"),
        ["Str_DragHint"] = ("Dosyaları bu karta sürükleyip bırakabilirsiniz", "You can drag and drop files onto this card"),
        ["Str_SendFiles"] = ("Dosya Gönder", "Send Files"),
        ["Str_Folder"] = ("Klasör", "Folder"),

        ["Str_TransfersTitle"] = ("Transferler", "Transfers"),
        ["Str_ClearFinished"] = ("Tamamlananları temizle", "Clear finished"),
        ["Str_NoTransfers"] = ("Henüz transfer yok. Cihazlar sekmesinden dosya gönderin.", "No transfers yet. Send files from the Devices tab."),
        ["Str_Cancel"] = ("İptal", "Cancel"),
        ["Str_RemoveFromList"] = ("Listeden kaldır", "Remove from list"),
        ["Str_ReceiverFormat"] = ("Alıcı: {0}", "To: {0}"),
        ["Str_SenderFormat"] = ("Gönderen: {0}", "From: {0}"),
        ["Str_RemainingFormat"] = ("Kalan: {0}", "Remaining: {0}"),

        ["Str_StateConnecting"] = ("Bağlanıyor…", "Connecting…"),
        ["Str_StatePairing"] = ("Eşleştirme bekleniyor…", "Waiting for pairing…"),
        ["Str_StateWaitingApproval"] = ("Karşı tarafın onayı bekleniyor…", "Waiting for the other side to approve…"),
        ["Str_StateTransferring"] = ("Aktarılıyor", "Transferring"),
        ["Str_StateVerifying"] = ("Doğrulanıyor…", "Verifying…"),
        ["Str_StateCompleted"] = ("Tamamlandı ✓", "Completed ✓"),
        ["Str_StateRejected"] = ("Reddedildi", "Rejected"),
        ["Str_StateCancelled"] = ("İptal edildi", "Cancelled"),
        ["Str_StateFailedFormat"] = ("Hata: {0}", "Error: {0}"),
        ["Str_Unknown"] = ("bilinmiyor", "unknown"),

        ["Str_SettingsTitle"] = ("Ayarlar", "Settings"),
        ["Str_Device"] = ("Cihaz", "Device"),
        ["Str_DeviceNameLabel"] = ("Cihaz adı (diğer cihazlarda böyle görünür)", "Device name (shown to other devices)"),
        ["Str_Language"] = ("Dil / Language", "Dil / Language"),
        ["Str_Avatar"] = ("Avatar", "Avatar"),
        ["Str_PickPhoto"] = ("Fotoğraf seç…", "Choose photo…"),
        ["Str_AvatarHint"] = ("Diğer cihazlarda böyle görünürsünüz.", "This is how you appear to other devices."),
        ["Str_Presets"] = ("Hazır avatarlar:", "Built-in avatars:"),
        ["Str_FileReceiving"] = ("Dosya Alımı", "Receiving Files"),
        ["Str_DownloadFolder"] = ("Varsayılan indirme klasörü", "Default download folder"),
        ["Str_Change"] = ("Değiştir", "Change"),
        ["Str_AlwaysAsk"] = ("Her transferde kaydedilecek yeri sor", "Ask where to save on every transfer"),
        ["Str_Performance"] = ("Performans", "Performance"),
        ["Str_StreamCountLabel"] = ("Paralel veri akışı sayısı:", "Parallel data streams:"),
        ["Str_StreamCountHint"] = ("Kablolu gigabit ağda 4 idealdir. Wi-Fi ya da 2.5G+ ağlarda artırmayı deneyin.", "4 is ideal on wired gigabit. Try increasing on Wi-Fi or 2.5G+ networks."),
        ["Str_PortLabel"] = ("Dinleme portu (TCP):", "Listening port (TCP):"),
        ["Str_PortHint"] = ("Port değişikliği yeniden başlatınca etkinleşir. İki cihazda da aynı olmalı.", "Port changes take effect after restart. Must match on both devices."),
        ["Str_System"] = ("Sistem", "System"),
        ["Str_MinimizeToTray"] = ("Pencere kapatılınca menü çubuğunda çalışmaya devam et", "Keep running in the menu bar when the window is closed"),
        ["Str_PairedDevices"] = ("Eşleştirilmiş Cihazlar", "Paired Devices"),
        ["Str_NoPaired"] = ("Henüz eşleştirilmiş cihaz yok. İlk transferde PIN ile eşleştirme yapılır.", "No paired devices yet. The first transfer pairs via a PIN."),
        ["Str_Remove"] = ("Kaldır", "Remove"),
        ["Str_About"] = ("Hakkında", "About"),
        ["Str_AboutFormat"] = ("LanBeam {0} — LAN üzerinde hızlı ve şifreli dosya transferi.\nVeri klasörü: {1}", "LanBeam {0} — fast, encrypted file transfer over the LAN.\nData folder: {1}"),
        ["Str_AboutNoPath"] = ("LanBeam {0} — LAN üzerinde hızlı ve şifreli dosya transferi.", "LanBeam {0} — fast, encrypted file transfer over the LAN."),
        ["Str_FingerprintFormat"] = ("Cihaz kimliği (SHA-256): {0}", "Device identity (SHA-256): {0}"),

        ["Str_IncomingTransfer"] = ("Gelen Transfer", "Incoming Transfer"),
        ["Str_IncomingTitle"] = ("Gelen dosya isteği", "Incoming file request"),
        ["Str_WantsToSendFormat"] = ("«{0}» size dosya göndermek istiyor:", "«{0}» wants to send you files:"),
        ["Str_OfferDetailFormat"] = ("{0} • {1} dosya", "{0} • {1} files"),
        ["Str_WillAskFolder"] = ("Kabul ederseniz kaydedilecek klasör sorulacak.", "If you accept, you'll be asked where to save."),
        ["Str_SaveToFormat"] = ("Kaydedilecek yer: {0}", "Will be saved to: {0}"),
        ["Str_Reject"] = ("Reddet", "Reject"),
        ["Str_Accept"] = ("Kabul Et", "Accept"),
        ["Str_ChooseSaveFolder"] = ("Dosyalar nereye kaydedilsin?", "Where should the files be saved?"),

        ["Str_PairingTitle"] = ("Cihaz eşleştirme", "Device pairing"),
        ["Str_PairingInfoFormat"] = ("«{0}» bu cihazla eşleşmek istiyor. Aşağıdaki kodu yalnızca o cihazda girin.", "«{0}» wants to pair with this device. Enter the code below only on that device."),
        ["Str_FingerprintChangedWarning"] = ("⚠ DİKKAT: Bu adı taşıyan cihaz daha önce farklı bir kimlikle eşleşmişti. Emin değilseniz iptal edin.", "⚠ WARNING: A device with this name previously paired with a different identity. If unsure, cancel."),
        ["Str_WaitingForCode"] = ("Karşı tarafın kodu girmesi bekleniyor…", "Waiting for the other side to enter the code…"),
        ["Str_PairingSuccess"] = ("Eşleştirme başarılı ✓", "Pairing successful ✓"),
        ["Str_PairingFailure"] = ("Eşleştirme başarısız ✗", "Pairing failed ✗"),

        ["Str_PinEntryTitle"] = ("Eşleştirme kodu", "Pairing code"),
        ["Str_EnterPinFormat"] = ("«{0}» ekranında görünen 6 haneli kodu girin.", "Enter the 6-digit code shown on «{0}»."),
        ["Str_AttemptsLeftFormat"] = ("Kalan deneme hakkı: {0}", "Attempts left: {0}"),
        ["Str_Dismiss"] = ("Vazgeç", "Cancel"),
        ["Str_Pair"] = ("Eşleştir", "Pair"),

        ["Str_SendToWho"] = ("Kime gönderilsin?", "Send to whom?"),
        ["Str_ToSendFormat"] = ("Gönderilecek: {0}", "To send: {0}"),
        ["Str_SendPickerHint"] = ("Diğer bilgisayarda da LanBeam açık olmalı ve ikisi aynı ağda bulunmalı.", "LanBeam must be open on the other computer and both on the same network."),
        ["Str_Send"] = ("Gönder", "Send"),

        ["Str_TrayTooltip"] = ("LanBeam — LAN dosya transferi", "LanBeam — LAN file transfer"),
        ["Str_OpenLanBeam"] = ("LanBeam'i Aç", "Open LanBeam"),
        ["Str_Exit"] = ("Çıkış", "Exit"),

        ["Str_ChooseDownloadFolder"] = ("Varsayılan indirme klasörünü seçin", "Choose the default download folder"),
        ["Str_PickFilesTitleFormat"] = ("{0} cihazına gönderilecek dosyaları seçin", "Choose files to send to {0}"),
        ["Str_PickFolderTitleFormat"] = ("{0} cihazına gönderilecek klasörü seçin", "Choose a folder to send to {0}"),
        ["Str_PickAvatar"] = ("Avatar fotoğrafı seçin", "Choose an avatar photo"),

        ["Str_SpeedFormat"] = ("{0} MB/sn", "{0} MB/s"),
        ["Str_Sec"] = ("sn", "s"),
        ["Str_Min"] = ("dk", "m"),
        ["Str_Hour"] = ("sa", "h"),
    };
}
