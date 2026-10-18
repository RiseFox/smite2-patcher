using System;
using System.Globalization;
using Microsoft.Win32;

namespace Smite2Patcher
{
    /// <summary>
    /// Ukrainian or English. Defaults to the Windows display language; the choice
    /// made with the EN / UA switch is remembered under HKCU.
    /// </summary>
    internal static class Lang
    {
        private const string Key = @"Software\SMITE2-Patcher";

        public static bool Ukrainian = Detect();

        public static string T(string ukrainian, string english) => Ukrainian ? ukrainian : english;

        public static void Choose(bool ukrainian)
        {
            Ukrainian = ukrainian;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(Key))
                {
                    key?.SetValue("Language", ukrainian ? "uk" : "en");
                }
            }
            catch (Exception error) when (error is UnauthorizedAccessException || error is System.Security.SecurityException)
            {
            }
        }

        private static bool Detect()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(Key))
                {
                    var saved = key?.GetValue("Language") as string;
                    if (saved == "uk" || saved == "en")
                    {
                        return saved == "uk";
                    }
                }
            }
            catch (Exception error) when (error is UnauthorizedAccessException || error is System.Security.SecurityException)
            {
            }
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "uk";
        }
    }
}
