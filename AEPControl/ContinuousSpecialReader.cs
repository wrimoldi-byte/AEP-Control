using System.Text;
using System.Text.RegularExpressions;

namespace AEPControl;

public sealed class ContinuousSpecialReader
{
    private sealed class ConfirmedRow
    {
        public string Code { get; init; } = string.Empty;
        public string Seat { get; init; } = string.Empty;
        public string Canonical { get; set; } = string.Empty;
    }

    private sealed class PendingRow
    {
        public string Code { get; init; } = string.Empty;
        public string Seat { get; init; } = string.Empty;
        public string Canonical { get; set; } = string.Empty;
        public int ConsecutiveHits { get; set; }
        public int LastFrame { get; set; }
    }

    private sealed record ScreenRow(string Code, string Seat, string Canonical);

    private readonly Regex _codeRegex;
    private static readonly Regex SeatRegex = new(@"\b(?<seat>\d{1,2}[A-F])\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly List<ConfirmedRow> _confirmed = new();
    private readonly List<PendingRow> _pending = new();
    private int _frame;

    public int UniqueRows => _confirmed.Count;

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
        _frame++;
        var current = ParseScreen(text);
        if (current.Count == 0)
            return BuildCounts();

        current = DeduplicateCurrentScreen(current);

        foreach (var row in current)
            AddCandidate(row);

        // Si una fila no reaparece pronto, se descarta. Ninguna lectura aislada
        // puede convertirse por sí sola en un WCHR/INF/etc confirmado.
        _pending.RemoveAll(p => _frame - p.LastFrame > 2);

        return BuildCounts();
    }

    private List<ScreenRow> ParseScreen(string text)
    {
        var result = new List<ScreenRow>();

        foreach (var raw in text.Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = Regex.Replace(raw.ToUpperInvariant(), @"\s+", " ").Trim();
            if (line.Length < 3) continue;

            var codeMatches = _codeRegex.Matches(line);
            if (codeMatches.Count == 0) continue;

            var seatMatch = SeatRegex.Match(line);
            var seat = seatMatch.Success ? NormalizeSeat(seatMatch.Groups["seat"].Value) : string.Empty;
            var canonical = Canonicalize(line);

            foreach (var code in codeMatches
                .Select(m => m.Value.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                result.Add(new ScreenRow(code, seat, canonical));
            }
        }

        return result;
    }

    private static List<ScreenRow> DeduplicateCurrentScreen(List<ScreenRow> rows)
    {
        var result = new List<ScreenRow>();

        foreach (var row in rows)
        {
            var duplicate = result.Any(r =>
                r.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase) &&
                SeatsEquivalent(r.Seat, row.Seat) &&
                CanonicalSimilarity(r.Canonical, row.Canonical) >= 0.88);

            if (!duplicate)
                result.Add(row);
        }

        return result;
    }

    private void AddCandidate(ScreenRow row)
    {
        // 1) Ya confirmado exactamente por código + asiento.
        if (!string.IsNullOrWhiteSpace(row.Seat))
        {
            var exact = _confirmed.FirstOrDefault(c =>
                c.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase) &&
                c.Seat.Equals(row.Seat, StringComparison.OrdinalIgnoreCase));

            if (exact is not null)
            {
                if (row.Canonical.Length > exact.Canonical.Length)
                    exact.Canonical = row.Canonical;
                return;
            }
        }

        // 2) La misma fila puede perder el asiento en una captura por OCR.
        // Si el texto coincide con una fila ya confirmada del mismo EDIT, no se suma otra vez.
        var sameConfirmedRow = _confirmed.Any(c =>
            c.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase) &&
            CanonicalSimilarity(c.Canonical, row.Canonical) >= 0.78);
        if (sameConfirmedRow)
            return;

        // 3) Buscar candidato pendiente. Con asiento exigimos mismo asiento;
        // sin asiento usamos similitud textual fuerte.
        PendingRow? pending;
        if (!string.IsNullOrWhiteSpace(row.Seat))
        {
            pending = _pending.FirstOrDefault(p =>
                p.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase) &&
                p.Seat.Equals(row.Seat, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            pending = _pending
                .Where(p => p.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(p.Seat))
                .OrderByDescending(p => CanonicalSimilarity(p.Canonical, row.Canonical))
                .FirstOrDefault(p => CanonicalSimilarity(p.Canonical, row.Canonical) >= 0.86);
        }

        if (pending is null)
        {
            _pending.Add(new PendingRow
            {
                Code = row.Code,
                Seat = row.Seat,
                Canonical = row.Canonical,
                ConsecutiveHits = 1,
                LastFrame = _frame
            });
            return;
        }

        if (pending.LastFrame == _frame)
            return;

        pending.ConsecutiveHits = pending.LastFrame == _frame - 1
            ? pending.ConsecutiveHits + 1
            : 1;
        pending.LastFrame = _frame;

        if (row.Canonical.Length > pending.Canonical.Length)
            pending.Canonical = row.Canonical;

        // Tanto con asiento como sin asiento debe verse estable al menos dos veces.
        if (pending.ConsecutiveHits < 2)
            return;

        // Antes de confirmar, una última comparación global evita que una lectura
        // 12A -> sin asiento -> 12A genere dos pasajeros durante el scroll.
        var duplicateConfirmed = _confirmed.Any(c =>
            c.Code.Equals(pending.Code, StringComparison.OrdinalIgnoreCase) &&
            ((!string.IsNullOrWhiteSpace(pending.Seat) && c.Seat.Equals(pending.Seat, StringComparison.OrdinalIgnoreCase)) ||
             CanonicalSimilarity(c.Canonical, pending.Canonical) >= 0.78));

        if (!duplicateConfirmed)
        {
            _confirmed.Add(new ConfirmedRow
            {
                Code = pending.Code,
                Seat = pending.Seat,
                Canonical = pending.Canonical
            });
        }

        _pending.Remove(pending);
    }

    private static bool SeatsEquivalent(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b);
        return a.Equals(b, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSeat(string value)
    {
        value = value.Trim().ToUpperInvariant();
        if (value.Length < 2) return value;

        var numberPart = value[..^1]
            .Replace('O', '0')
            .Replace('Q', '0')
            .Replace('I', '1')
            .Replace('L', '1');
        var letter = value[^1];
        return $"{numberPart}{letter}";
    }

    private static double CanonicalSimilarity(string a, string b)
    {
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase))
            return 1;

        var charSimilarity = Similarity(a, b);
        var tokenSimilarity = TokenSimilarity(a, b);
        return Math.Max(charSimilarity, tokenSimilarity);
    }

    private static double TokenSimilarity(string a, string b)
    {
        var aTokens = StableTokens(a);
        var bTokens = StableTokens(b);
        if (aTokens.Count == 0 || bTokens.Count == 0) return 0;

        var intersection = aTokens.Intersect(bTokens, StringComparer.OrdinalIgnoreCase).Count();
        if (intersection == 0) return 0;

        var denominator = Math.Max(aTokens.Count, bTokens.Count);
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
        foreach (var row in _confirmed)
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
