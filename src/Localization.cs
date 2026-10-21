using System;
using System.Globalization;

namespace PushToTalk
{
    /// <summary>Application text only; never changes Windows or thread culture.</summary>
    internal static class Localization
    {
        private static string choice = "system";

        public static string Choice { get { return choice; } }

        public static bool IsChinese
        {
            get { return ResolveIsChinese(choice, CultureInfo.CurrentUICulture); }
        }

        public static void SetLanguage(string language)
        {
            choice = NormalizeLanguage(language);
        }

        public static string NormalizeLanguage(string language)
        {
            if (language == null) return "system";
            language = language.Trim();
            if (string.Equals(language, "zh-CN", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
            if (string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase)) return "en-US";
            return "system";
        }

        public static string Text(string chinese, string english)
        {
            return IsChinese ? chinese : english;
        }

        public static bool ResolveIsChinese(string language, CultureInfo systemUiCulture)
        {
            string normalized = NormalizeLanguage(language);
            if (normalized == "zh-CN") return true;
            if (normalized == "en-US") return false;
            if (systemUiCulture == null) return false;
            string name = systemUiCulture.Name;
            return string.Equals(name, "zh", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("zh-", StringComparison.OrdinalIgnoreCase);
        }
    }
}
