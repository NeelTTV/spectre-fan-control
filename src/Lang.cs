namespace SpectreControl
{
    // Translations: each text is written in French and English right where it is used
    static class Lang
    {
        public const string Default = "en";

        public static bool English = true;

        public static string T(string fr, string en)
        {
            return English ? en : fr;
        }
    }
}
