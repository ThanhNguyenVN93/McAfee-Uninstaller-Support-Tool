using System.Globalization;

namespace frm_mcafee_unin
{
    internal static class S
    {
        public enum Lang { VI, EN }
        public static Lang Current;

        static S()
        {
            Current = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "vi"
                ? Lang.VI : Lang.EN;
        }

        public static void Toggle() =>
            Current = Current == Lang.VI ? Lang.EN : Lang.VI;

        public static string T(string vi, string en) =>
            Current == Lang.VI ? vi : en;
    }
}
