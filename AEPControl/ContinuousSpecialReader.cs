using System.Text;
using System.Text.RegularExpressions;

namespace AEPControl;

public sealed class ContinuousSpecialReader
{
    private sealed record SeenRow(string Code, string Canonical);
    private sealed record ScreenRow(string Code, string Canonical);

    private readonly Regex _codeRegex;
    private readonly List<SeenRow> _uniqueRows = new();
    private readonly LinkedList<List<ScreenRow>> _screenHistory = new();

    private const int HistoryScreens = 6;

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
        var current = ParseScreen(text);
        if (current.Count == 0)
            return BuildCounts();

        if (_screenHistory.Count == 0)
        {
            foreach (var row in current)
                _uniqueRows.Add(new SeenRow(row.Code, row.Canonical));
        }
        else
        {
            var alreadyVisible = MatchAgainstRecentScreens(current);
            for (var i = 0; i < current.Count; i++)
            {
                if (!alreadyVisible[i])
                    _uniqueRows.Add(new SeenRow(current[i].Code, current[i].Canonical));
            }
        }

        RememberScreen(current);
        return BuildCounts();
    }

    private List<ScreenRow> ParseScreen(string text)
    {
        var result = new List<ScreenRow>();

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

            result.Add(new ScreenRow(code, canonical));
        }

        return result;
    }

    private bool[] MatchAgainstRecentScreens(IReadOnlyList<ScreenRow> current)
    {
        var matchedCurrent = new bool[current.Count];

        // Primero compara contra la captura inmediatamente anterior y después contra
        // algunas capturas recientes. Así una fila que sigue visible por el scroll no
        // vuelve a sumarse aunque el OCR cambie letras, espacios o parte del nombre.
        foreach (var previous in _screenHistory.Reverse())
        {
            var usedPrevious = new bool[previous.Count];

            for (var i = 0; i < current.Count; i++)
            {
                if (matchedCurrent[i]) continue;

                var bestIndex = -1;
                var bestSimilarity = 0.0;

                for (var j = 0; j < previous.Count; j++)
                {
                    if (usedPrevious[j]) continue;
                    if (!current[i].Code.Equals(previous[j].Code, StringComparison.OrdinalIgnoreCase)) continue;

                    var similarity = Similarity(current[i].Canonical, previous[j].Canonical);
                    var threshold = SimilarityThreshold(current[i].Canonical, previous[j].Canonical);
                    if (similarity < threshold || similarity <= bestSimilarity) continue;

                    bestSimilarity = similarity;
                    bestIndex = j;
                }

                if (bestIndex >= 0)
                {
                    matchedCurrent[i] = true;
                    usedPrevious[bestIndex] = true;
                }
            }

            if (matchedCurrent.All(x => x)) break;
        }

        return matchedCurrent;
    }

    private static double SimilarityThreshold(string a, string b)
    {
        var minLength = Math.Min(a.Length, b.Length);
        if (minLength <= 8) return 0.88;
        if (minLength <= 16) return 0.72;
        return 0.60;
    }

    private void RememberScreen(List<ScreenRow> screen)
    {
        _screenHistory.AddLast(screen);
        while (_screenHistory.Count > HistoryScreens)
            _screenHistory.RemoveFirst();
    }

    private static string Canonicalize(string line)
    {
        var builder = new StringBuilder(line.Length);
        foreach (var ch in line)
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
