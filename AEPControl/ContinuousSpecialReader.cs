using System.Text;
using System.Text.RegularExpressions;

namespace AEPControl;

public sealed class ContinuousSpecialReader
{
    private sealed class SeenGroup
    {
        public string Code { get; init; } = string.Empty;
        public string Canonical { get; set; } = string.Empty;
        public int MaxOccurrences { get; set; }
    }

    private readonly Regex _codeRegex;
    private readonly List<SeenGroup> _seenGroups = new();

    public int UniqueRows => _seenGroups.Sum(g => g.MaxOccurrences);

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

        // Agrupamos las filas que aparecen en ESTA captura. Luego las comparamos contra
        // todo lo visto durante el vuelo. De esta manera da igual si el usuario baja,
        // llega al final y vuelve a subir: una fila vieja nunca se vuelve a sumar.
        var currentGroups = GroupCurrentScreen(current);

        foreach (var group in currentGroups)
        {
            var seen = FindSeenGroup(group.Code, group.Canonical);
            if (seen is null)
            {
                _seenGroups.Add(new SeenGroup
                {
                    Code = group.Code,
                    Canonical = group.Canonical,
                    MaxOccurrences = group.Count
                });
                continue;
            }

            // Si realmente hay dos filas iguales al mismo tiempo, conservamos ambas.
            // Al volver a pasar por ellas con el scroll no aumenta el contador.
            if (group.Count > seen.MaxOccurrences)
                seen.MaxOccurrences = group.Count;

            // Guardamos la lectura más completa para comparar mejor futuras capturas OCR.
            if (group.Canonical.Length > seen.Canonical.Length)
                seen.Canonical = group.Canonical;
        }

        return BuildCounts();
    }

    private List<(string Code, string Canonical)> ParseScreen(string text)
    {
        var result = new List<(string Code, string Canonical)>();

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

            result.Add((code, canonical));
        }

        return result;
    }

    private static List<(string Code, string Canonical, int Count)> GroupCurrentScreen(
        IReadOnlyList<(string Code, string Canonical)> rows)
    {
        var groups = new List<(string Code, string Canonical, int Count)>();

        foreach (var row in rows)
        {
            var index = -1;
            var best = 0.0;

            for (var i = 0; i < groups.Count; i++)
            {
                if (!groups[i].Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase)) continue;
                var similarity = Similarity(groups[i].Canonical, row.Canonical);
                if (similarity >= 0.94 && similarity > best)
                {
                    best = similarity;
                    index = i;
                }
            }

            if (index < 0)
            {
                groups.Add((row.Code, row.Canonical, 1));
            }
            else
            {
                var existing = groups[index];
                groups[index] = (existing.Code,
                    row.Canonical.Length > existing.Canonical.Length ? row.Canonical : existing.Canonical,
                    existing.Count + 1);
            }
        }

        return groups;
    }

    private SeenGroup? FindSeenGroup(string code, string canonical)
    {
        SeenGroup? bestGroup = null;
        var bestSimilarity = 0.0;

        foreach (var seen in _seenGroups)
        {
            if (!seen.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) continue;

            if (seen.Canonical.Equals(canonical, StringComparison.OrdinalIgnoreCase))
                return seen;

            var similarity = Similarity(seen.Canonical, canonical);
            var threshold = SimilarityThreshold(seen.Canonical, canonical);
            if (similarity >= threshold && similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestGroup = seen;
            }
        }

        return bestGroup;
    }

    private static double SimilarityThreshold(string a, string b)
    {
        var minLength = Math.Min(a.Length, b.Length);
        if (minLength <= 8) return 0.94;
        if (minLength <= 16) return 0.88;
        if (minLength <= 28) return 0.84;
        return 0.80;
    }

    private static string Canonicalize(string line)
    {
        var normalized = line
            .Replace('Q', '0')
            .Replace('O', '0')
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
        foreach (var group in _seenGroups)
        {
            for (var i = 0; i < group.MaxOccurrences; i++)
                AddCount(result, group.Code);
        }
        return result;
    }

    private static void AddCount(SpecialCounts result, string code)
    {
        switch (code)
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
                result.Extra.TryGetValue(code, out var current);
                result.Extra[code] = current + 1;
                break;
        }
    }
}
