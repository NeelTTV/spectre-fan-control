using System.Globalization;

namespace SpectreControl
{
    // Traductions : chaque texte est écrit en français et en anglais à l'endroit où il est utilisé
    static class Lang
    {
        public static bool English;

        public static string T(string fr, string en)
        {
            return English ? en : fr;
        }

        public static string SystemDefault()
        {
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? "fr" : "en";
        }
    }
}
