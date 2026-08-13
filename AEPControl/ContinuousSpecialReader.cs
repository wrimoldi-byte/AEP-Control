using System.Text;
using System.Text.RegularExpressions;

namespace AEPControl;

public sealed class ContinuousSpecialReader
{
    private sealed record SeenRow(string Code, string Canonical, int Occurrence);

    private readonly List<SeenRow> _uniqueRows = new();

    public int UniqueRows => _uniqueRows.Count;

    public SpecialCounts AddOcrText(string text)
    {
        var screenRows = new List<(string Code, string Canonical)>();

        foreach (var raw in text.Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = Regex.Replace(raw.ToUpperInvariant(), @"\s+", " ").Trim();
            if (line.Length < 4) continue;

            var codeMatch = Regex.Match(line, @"\b(WCHR|WCHS|WCHC|AVIH|INF)\b");
            if (!codeMatch.Success) continue;

            var code = codeMatch.Value;
            var canonical = Canonicalize(line);
            if (canonical.Length < code.Length)
                canonical = code;

            screenRows.Add((code, canonical));
        }

        // La misma fila puede aparecer varias veces en una pantalla. Conservamos la
        // ocurrencia 1, 2, 3... para no fusionar dos PAX reales con el mismo texto.
        foreach (var group in screenRows.GroupBy(r => $"{r.Code}|{r.Canonical}", StringComparer.OrdinalIgnoreCase))
        {
            var sample = group.First();
            var count = group.Count();
            for (var occurrence = 1; occurrence <= count; occurrence++)
            {
                if (!AlreadySeen(sample.Code, sample.Canonical, occurrence))
                    _uniqueRows.Add(new SeenRow(sample.Code, sample.Canonical, occurrence));
            }
        }

        return BuildCounts();
    }

    private bool AlreadySeen(string code, string canonical, int occurrence)
    {
        foreach (var seen in _uniqueRows)
        {
            if (!seen.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) continue;
            if (seen.Occurrence != occurrence) continue;

            if (seen.Canonical.Equals(canonical, StringComparison.OrdinalIgnoreCase))
                return true;

            // Windows OCR suele variar una o dos letras/espacios de la misma fila
            // entre capturas. Consideramos la misma fila cuando la similitud es alta.
            if (Similarity(seen.Canonical, canonical) >= 0.82)
                return true;
        }

        return false;
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
            }
        }
        return result;
    }
}
