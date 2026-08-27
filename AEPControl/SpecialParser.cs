using System.Text.RegularExpressions;

namespace AEPControl;

public static class SpecialParser
{
    private static int CountCode(string text, string code)
    {
        return Regex.Matches(text, $@"\b{Regex.Escape(code)}\b", RegexOptions.IgnoreCase).Count;
    }

    public static SpecialCounts Parse(string text)
    {
        var normalized = text.ToUpperInvariant();
        return new SpecialCounts
        {
            WCHR = CountCode(normalized, "WCHR"),
            WCHS = CountCode(normalized, "WCHS"),
            WCHC = CountCode(normalized, "WCHC"),
            AVIH = CountCode(normalized, "AVIH") + CountCode(normalized, "AVI"),
            INF = CountCode(normalized, "INF"),
            ETO = CountCode(normalized, "ETO")
        };
    }
}
