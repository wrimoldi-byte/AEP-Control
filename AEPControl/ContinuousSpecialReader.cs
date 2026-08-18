using System.Text;
using System.Text.RegularExpressions;

namespace AEPControl;

public sealed class ContinuousSpecialReader
{
    private sealed class SeenRow
    {
        public string Code { get; init; } = string.Empty;
        public string Seat { get; init; } = string.Empty;
        public string Canonical { get; set; } = string.Empty;
    }

    private sealed record ScreenRow(string Code, string Seat, string Canonical);
    private sealed record Alignment(int Offset, int Matches, int Mismatches, int Overlap, double Score);

    private readonly Regex _codeRegex;
    private static readonly Regex SeatRegex = new(@"\b(?<seat>\d{1,2}[A-F])\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

        // Primera defensa contra duplicados: si el mismo edit vuelve a aparecer para el
        // mismo asiento, no puede volver a sumarse aunque el OCR del nombre cambie mucho.
        // Usamos ASIENTO + CODIGO para no perder casos reales donde el mismo pasajero
        // tiene más de un edit distinto (por ejemplo WCHR e INF).
        current = current
            .GroupBy(r => SeatCodeKey(r.Seat, r.Code), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(r => r.Canonical.Length).First())
            .ToList();

        if (_sequence.Count == 0)
        {
            foreach (var row in current)
                AddIfNew(row);
            return BuildCounts();
        }

        var alignment = FindBestAlignment(current);
        if (alignment is null)
        {
            foreach (var row in current)
                AddIfNew(row);
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
            var seatMatch = SeatRegex.Match(line);
            var seat = seatMatch.Success ? NormalizeSeat(seatMatch.Groups["seat"].Value) : string.Empty;
            var canonical = Canonicalize(line);
            if (canonical.Length < code.Length)
                canonical = code;

            result.Add(new ScreenRow(code, seat, canonical));
        }

        return result;
    }

    private void AddIfNew(ScreenRow row)
    {
        if (IsAlreadySeenBySeat(row)) return;

        if (string.IsNullOrWhiteSpace(row.Seat))
        {
            var similar = _sequence.Any(seen =>
                seen.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase) &&
                RowSimilarity(row, seen) >= 0.84);
            if (similar) return;
        }

        _sequence.Add(new SeenRow { Code = row.Code, Seat = row.Seat, Canonical = row.Canonical });
    }

    private bool IsAlreadySeenBySeat(ScreenRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Seat)) return false;

        return _sequence.Any(seen =>
            seen.Seat.Equals(row.Seat, StringComparison.OrdinalIgnoreCase) &&
            seen.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase));
    }

    private Alignment? FindBestAlignment(IReadOnlyList<ScreenRow> current)
    {
        Alignment? best = null;

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
        var leadingCount = Math.Min(current.Count, Math.Max(0, -offset));
        if (leadingCount > 0)
        {
            var leading = current.Take(leadingCount)
                .Where(r => !IsAlreadySeenBySeat(r))
                .Where(r => string.IsNullOrWhiteSpace(r.Seat) || !_sequence.Any(s => s.Code.Equals(r.Code, StringComparison.OrdinalIgnoreCase) && RowSimilarity(r, s) >= 0.84))
                .Select(r => new SeenRow { Code = r.Code, Seat = r.Seat, Canonical = r.Canonical })
                .ToList();
            _sequence.InsertRange(0, leading);
            offset += leadingCount;
        }

        for (var i = 0; i < current.Count; i++)
        {
            var row = current[i];

            // Esta condición es independiente del sentido del scroll y de la posición
            // de la fila: un asiento+edit ya leído no vuelve a entrar nunca.
            if (IsAlreadySeenBySeat(row))
                continue;

            var globalIndex = i + offset;
            if (globalIndex >= 0 && globalIndex < _sequence.Count)
            {
                var seen = _sequence[globalIndex];
                if (RowSimilarity(row, seen) >= 0.68)
                {
                    if (row.Canonical.Length > seen.Canonical.Length)
                        seen.Canonical = row.Canonical;
                    continue;
                }
            }

            AddIfNew(row);
        }
    }

    private static string NormalizeSeat(string value)
    {
        value = value.Trim().ToUpperInvariant();
        if (value.Length < 2) return value;

        // Corrige errores OCR frecuentes sólo dentro del número de asiento.
        var numberPart = value[..^1]
            .Replace('O', '0')
            .Replace('Q', '0')
            .Replace('I', '1')
            .Replace('L', '1');
        var letter = value[^1];
        return $"{numberPart}{letter}";
    }

    private static string SeatCodeKey(string seat, string code) =>
        string.IsNullOrWhiteSpace(seat) ? $"NOSEAT|{code}|{Guid.NewGuid()}" : $"{seat}|{code}";

    private static double RowSimilarity(ScreenRow current, SeenRow seen)
    {
        if (!current.Code.Equals(seen.Code, StringComparison.OrdinalIgnoreCase))
            return 0;

        if (!string.IsNullOrWhiteSpace(current.Seat) && !string.IsNullOrWhiteSpace(seen.Seat))
        {
            if (current.Seat.Equals(seen.Seat, StringComparison.OrdinalIgnoreCase))
                return 1;
            return 0;
        }

        if (current.Canonical.Equals(seen.Canonical, StringComparison.OrdinalIgnoreCase))
            return 1;

        var charSimilarity = Similarity(current.Canonical, seen.Canonical);
        var tokenSimilarity = TokenSimilarity(current.Canonical, seen.Canonical);
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
