using System.Collections.Generic;
using System.Globalization;

namespace SecSMS.Desktop;

internal static class DesktopStrings
{
    private static readonly Dictionary<string, Dictionary<string, string>> _strings = new()
    {
        ["en"] = new()
        {
            ["Tab_Sms"] = "SMS",
            ["Tab_Links"] = "Links",
            ["Links_Intro"] = "External links (open in your browser):",
            ["Link1_Title"] = "Website exposing corruption",
            ["Link2_Title"] = "Free Password Manager PWA",
            ["Link3_Title"] = "Get a Generative AI based internal search engine for your own website",
        },
        ["zh"] = new()
        {
            ["Tab_Sms"] = "短信",
            ["Tab_Links"] = "链接",
            ["Links_Intro"] = "外部链接（在浏览器中打开）：",
            ["Link1_Title"] = "揭露腐败的网站",
            ["Link2_Title"] = "Free Password Manager PWA",
            ["Link3_Title"] = "为你的网站提供生成式 AI 内部搜索引擎",
        },
        ["ru"] = new()
        {
            ["Tab_Sms"] = "Сообщения",
            ["Tab_Links"] = "Ссылки",
            ["Links_Intro"] = "Внешние ссылки (откроются в браузере):",
            ["Link1_Title"] = "Сайт, раскрывающий коррупцию",
            ["Link2_Title"] = "Free Password Manager PWA",
            ["Link3_Title"] = "Генеративный ИИ-поиск для вашего сайта",
        },
        ["fr"] = new()
        {
            ["Tab_Sms"] = "Messages",
            ["Tab_Links"] = "Liens",
            ["Links_Intro"] = "Liens externes (ouverts dans votre navigateur) :",
            ["Link1_Title"] = "Site dénonçant la corruption",
            ["Link2_Title"] = "Free Password Manager PWA",
            ["Link3_Title"] = "Moteur de recherche interne IA générative pour votre site web",
        },
    };

    public static string Get(string key)
    {
        var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        if (!_strings.TryGetValue(lang, out var map))
        {
            map = _strings["en"];
        }

        if (map.TryGetValue(key, out var value))
        {
            return value;
        }

        return _strings["en"].TryGetValue(key, out var fallback) ? fallback : key;
    }
}
