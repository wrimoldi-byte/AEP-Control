using System.Text;
using System.Text.RegularExpressions;

namespace AEPControl;

public sealed class ContinuousSpecialReader
{
    private sealed record SeenRow(string Code, string Canonical, int Occurrence);
    private sealed record ScreenRow(string Code, string Canonical, int Occurrence);

    private readonly Regex _codeRegex;
    private readonly List<SeenRow> _uniqueRows = new();
    private List<ScreenRow> _previousScreen = new();

    public int UniqueRows => _uniqueRows.Count;

    public ContinuousSpecialReader()
    {
        var codes = SpecialCodeSettings.Load().Codes;
        var pattern = string.Join("|", codes
            .OrderByDescending(c => c.Length)
            .Select(Regex.Escape));

        _codeRegex = new Regex($@"\b({pattern})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    public SpecialCounts AddOcrText(string text)
    {
        var screenRows = ParseScreen(text);
        if (screenRows.Count == 0)
            return BuildCounts();

        var overlap = FindScrollOverlap(_previousScreen, screenRows);
        var startIndex = overlap;

        for (var i = startIndex; i < screenRows.Count; i++)
        {
            var row = screenRows[i];
            if (!AlreadySeen(row.Code, row.Canonical, row.Occurrence))
                _uniqueRows.Add(new SeenRow(row.Code, row.Canonical, row.Occurrence));
        }

        _previousScreen = screenRows;
        return BuildCounts();
    }

    private List<ScreenRow> ParseScreen(string text)
    {
        var parsed = new List<(string Code, string Canonical)>();

        foreach (var raw in text.Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = Regex.Replace(raw.ToUpperInvariant(), @"\s+", " ").Trim();
            if (line.Length < 3) continue;

            var codeMatch = _codeRegex.Match(line);
            if (!codeMatch.Success) continue;

            var code = codeMatch.Value.ToUpperInvariant();
            var canonical = Canonicalize(line);
            if (canonical.Length < code.Length)
                canonical = code;

            parsed.Add((code, canonical));
        }

        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ScreenRow>(parsed.Count);
        foreach (var row in parsed)
        {
            var key = $"{row.Code}|{row.Canonical}";
            occurrences.TryGetValue(key, out var occurrence);
            occurrence++;
            occurrences[key] = occurrence;
            result.Add(new ScreenRow(row.Code, row.Canonical, occurrence));
        }

        return result;
    }

    private static int FindScrollOverlap(IReadOnlyList<ScreenRow> previous, IReadOnlyList<ScreenRow> current)
    {
        if (previous.Count == 0 || current.Count == 0)
            return 0;

        var max = Math.Min(previous.Count, current.Count);
        for (var length = max; length >= 1; length--)
        {
            var previousStart = previous.Count - length;
            var matches = 0;

            for (var i = 0; i < length; i++)
            {
                if (RowsMatch(previous[previousStart + i], current[i]))
                    matches++;
            }

            var required = length < 3 ? length : length - 1;
            if (matches >= required)
                return length;
        }

        return 0;
    }

    private bool AlreadySeen(string code, string canonical, int occurrence)
    {
        foreach (var seen in _uniqueRows)
        {
            if (!seen.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) continue;
            if (seen.Occurrence != occurrence) continue;

            if (seen.Canonical.Equals(canonical, StringComparison.OrdinalIgnoreCase))
                return true;

            if (Similarity(seen.Canonical, canonical) >= 0.76)
                return true;
        }

        return false;
    }

    private static bool RowsMatch(ScreenRow a, ScreenRow b)
    {
        if (!a.Code.Equals(b.Code, StringComparison.OrdinalIgnoreCase))
            return false;

        if (a.Occurrence != b.Occurrence)
            return false;

        return a.Canonical.Equals(b.Canonical, StringComparison.OrdinalIgnoreCase)
            || Similarity(a.Canonical, b.Canonical) >= 0.72;
    }

    private static string Canonicalize(string line)
    {
        var normalized = line
            .Replace('O', '0')
            .Replace('Q', '0')
            .Replace('I', '1')
            .Replace('L', '1');

        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch)) builder.Append(ch);
        }
        return builder.ToString();
    }

    private static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 1;
        var max = Math.Max(a.Length, b.Length);
        if (max == 0) return 1;
        return 1.0 - (double)Levenshtein(a, b) / max;
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private SpecialCounts BuildCounts()
    {
        var result = new SpecialCounts();
        foreach (var row in _uniqueRows)
        {
            switch (row.Code)
            {
                case "WCHR": result.WCHR++; break;
                case "WCHS": result.WCHS++; break;
                case "WCHC": result.WCHC++; break;
                case "AVIH": result.AVIH++; break;
                case "INF": result.INF++; break;
                case "UMNR": result.UMNR++; break;
                case "PETC": result.PETC++; break;
                case "DEAF": result.DEAF++; break;
                case "BLND": result.BLND++; break;
                case "MAAS": result.MAAS++; break;
                case "STCR": result.STCR++; break;
                case "MEDA": result.MEDA++; break;
                case "WCLB": result.WCLB++; break;
                case "WCMP": result.WCMP++; break;
                case "SVAN": result.SVAN++; break;
                case "ESAN": result.ESAN++; break;
                case "INAD": result.INAD++; break;
                case "DEPA": result.DEPA++; break;
                case "DEPU": result.DEPU++; break;
                default:
                    result.Extra.TryGetValue(row.Code, out var current);
                    result.Extra[row.Code] = current + 1;
                    break;
            }
        }
        return result;
    }
}
