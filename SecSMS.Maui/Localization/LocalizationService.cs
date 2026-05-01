using System.Globalization;
using System.Resources;

namespace SecSMS.Maui.Localization;

public static class LocalizationService
{
    static readonly ResourceManager ResourceManager = new("SecSMS.Maui.Localization.AppStrings", typeof(LocalizationService).Assembly);

    public static string GetString(string key)
    {
        return ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }

    public static string TabMessages => GetString("TabMessages");
    public static string TabLinks => GetString("TabLinks");
    public static string NoMessages => GetString("NoMessages");
    public static string LinksIntro => GetString("LinksIntro");
    public static string Link1Title => GetString("Link1Title");
    public static string Link2Title => GetString("Link2Title");
    public static string Link3Title => GetString("Link3Title");
    public static string BackToList => GetString("BackToList");
    public static string CopyOtp => GetString("CopyOtp");
    public static string SendToWindows => GetString("SendToWindows");
    public static string OpenInBrowser => GetString("OpenInBrowser");
    public static string MessageDetailTitle => GetString("MessageDetailTitle");
    public static string WaitingForPqBundle => GetString("WaitingForPqBundle");
    public static string NothingToCopy => GetString("NothingToCopy");
    public static string CopiedToClipboard => GetString("CopiedToClipboard");
    public static string NothingToSend => GetString("NothingToSend");
    public static string EncryptedOtpSent => GetString("EncryptedOtpSent");
    public static string BluetoothCancelled => GetString("BluetoothCancelled");
    public static string NoUrlFound => GetString("NoUrlFound");
    public static string UnableToOpenLink => GetString("UnableToOpenLink");
    public static string PermissionRequired => GetString("PermissionRequired");
    public static string OK => GetString("OK");
}
