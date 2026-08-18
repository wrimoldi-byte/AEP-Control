using System.Text;
using System.Text.RegularExpressions;

namespace AEPControl;

public sealed class ContinuousSpecialReader
{
    private sealed class SeenRow
    {
        public string Code { get; init; } = string.Empty;
        public string Canonical { get; set; } = string.Empty;
    }

    private sealed record ScreenRow(string Code, string Canonical);
    private sealed record Alignment(int Offset, int Matches, int Mismatches, int Overlap, double Score);

    private readonly Regex _codeRegex;

    // En vez de recordar sólo "filas parecidas", armamos una secuencia global de la
    // lista SusEdit. Cada nueva pantalla se alinea contra esa secuencia. Así, al bajar
    // y luego volver a subir, la pantalla encaja en una zona ya conocida y NO suma.
    private readonly List<SeenRow> _sequence = new();

    public int UniqueRows => _sequence.Count;

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

        if (_sequence.Count == 0)
        {
            foreach (var row in current)
                _sequence.Add(new SeenRow { Code = row.Code, Canonical = row.Canonical });
            return BuildCounts();
        }

        var alignment = FindBestAlignment(current);
        if (alignment is null)
        {
            // Un salto grande sin ninguna fila común puede ser una zona nueva. Sólo la
            // agregamos si realmente no encontramos coincidencias fuertes en toda la
            // lista. Si hay alguna coincidencia, preferimos no sumar antes que duplicar.
            if (!HasAnyStrongGlobalMatch(current))
            {
                foreach (var row in current)
                    _sequence.Add(new SeenRow { Code = row.Code, Canonical = row.Canonical });
            }
            return BuildCounts();
        }

        MergeAlignedScreen(current, alignment.Offset);
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

    private Alignment? FindBestAlignment(IReadOnlyList<ScreenRow> current)
    {
        Alignment? best = null;

        // offset: índice global = índice actual + offset.
        // Un offset negativo significa que la pantalla contiene filas nuevas arriba.
        for (var offset = -current.Count + 1; offset <= _sequence.Count - 1; offset++)
        {
            var matches = 0;
            var mismatches = 0;
            var overlap = 0;
            double similaritySum = 0;

            for (var i = 0; i < current.Count; i++)
            {
                var globalIndex = i + offset;
                if (globalIndex < 0 || globalIndex >= _sequence.Count) continue;

                overlap++;
                var similarity = RowSimilarity(current[i], _sequence[globalIndex]);
                if (similarity >= 0.68)
                {
                    matches++;
                    similaritySum += similarity;
                }
                else
                {
                    mismatches++;
                }
            }

            if (overlap == 0 || matches == 0) continue;

            // Para solapes medianos/grandes pedimos al menos dos anclas. Con un solape
            // de una sola fila permitimos una coincidencia muy fuerte.
            var average = matches == 0 ? 0 : similaritySum / matches;
            var acceptable = overlap switch
            {
                1 => matches == 1 && average >= 0.88,
                2 => matches >= 1 && average >= 0.82,
                _ => matches >= 2 && matches >= mismatches
            };
            if (!acceptable) continue;

            var score = matches * 5.0 - mismatches * 2.5 + average * 2.0 + overlap * 0.05;
            var candidate = new Alignment(offset, matches, mismatches, overlap, score);

            if (best is null || candidate.Score > best.Score ||
                (Math.Abs(candidate.Score - best.Score) < 0.001 && candidate.Matches > best.Matches))
                best = candidate;
        }

        return best;
    }

    private void MergeAlignedScreen(IReadOnlyList<ScreenRow> current, int offset)
    {
        var oldCount = _sequence.Count;

        // Filas nuevas por arriba de lo ya conocido.
        var leadingCount = Math.Min(current.Count, Math.Max(0, -offset));
        if (leadingCount > 0)
        {
            var leading = current.Take(leadingCount)
                .Select(r => new SeenRow { Code = r.Code, Canonical = r.Canonical })
                .ToList();
            _sequence.InsertRange(0, leading);
            offset += leadingCount;
            oldCount += leadingCount;
        }

        // Actualizamos las filas ya alineadas con la lectura más completa, sin sumar.
        for (var i = 0; i < current.Count; i++)
        {
            var globalIndex = i + offset;
            if (globalIndex < 0 || globalIndex >= _sequence.Count) continue;

            var row = current[i];
            var seen = _sequence[globalIndex];
            if (RowSimilarity(row, seen) >= 0.68 && row.Canonical.Length > seen.Canonical.Length)
                seen.Canonical = row.Canonical;
        }

        // Filas que extienden la secuencia por abajo: son las únicas que suman al bajar.
        for (var i = 0; i < current.Count; i++)
        {
            var globalIndex = i + offset;
            if (globalIndex < _sequence.Count) continue;

            _sequence.Add(new SeenRow { Code = current[i].Code, Canonical = current[i].Canonical });
        }
    }

    private bool HasAnyStrongGlobalMatch(IReadOnlyList<ScreenRow> current)
    {
        foreach (var row in current)
        {
            foreach (var seen in _sequence)
            {
                if (!row.Code.Equals(seen.Code, StringComparison.OrdinalIgnoreCase)) continue;
                if (RowSimilarity(row, seen) >= 0.84)
                    return true;
            }
        }
        return false;
    }

    private static double RowSimilarity(ScreenRow current, SeenRow seen)
    {
        if (!current.Code.Equals(seen.Code, StringComparison.OrdinalIgnoreCase))
            return 0;

        if (current.Canonical.Equals(seen.Canonical, StringComparison.OrdinalIgnoreCase))
            return 1;

        var charSimilarity = Similarity(current.Canonical, seen.Canonical);
        var tokenSimilarity = TokenSimilarity(current.Canonical, seen.Canonical);

        // La línea puede variar mucho por OCR durante el scroll. El código del edit ya
        // coincide; usamos la mejor señal entre texto completo y fragmentos estables.
        return Math.Max(charSimilarity, tokenSimilarity);
    }

    private static double TokenSimilarity(string a, string b)
    {
        var aTokens = StableTokens(a);
        var bTokens = StableTokens(b);
        if (aTokens.Count == 0 || bTokens.Count == 0) return 0;

        var intersection = aTokens.Intersect(bTokens, StringComparer.OrdinalIgnoreCase).Count();
        if (intersection == 0) return 0;

        var denominator = Math.Min(aTokens.Count, bTokens.Count);
        return (double)intersection / denominator;
    }

    private static List<string> StableTokens(string value)
    {
        return Regex.Matches(value, @"[A-Z0-9]{4,}")
            .Select(m => m.Value)
            .Where(v => v.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Canonicalize(string line)
    {
        // No cambiamos O/I/L globalmente porque eso deformaba nombres de pasajeros y
        // hacía que la misma fila pareciera distinta al volver a subir.
        var normalized = line;
        normalized = Regex.Replace(normalized, @"(?<=\d)[OQ](?=\d)", "0");
        normalized = Regex.Replace(normalized, @"(?<=\d)[IL](?=\d)", "1");

        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch)) builder.Append(ch);
            else builder.Append(' ');
        }

        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
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
        foreach (var row in _sequence)
            AddCount(result, row.Code);
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
                result.Extra.TryGetValue(code, out var count);
                result.Extra[code] = count + 1;
                break;
        }
    }
}
