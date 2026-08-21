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
        public string Canonical { get; set; } = string.Empty;
        public int ConsecutiveHits { get; set; }
        public int LastFrame { get; set; }
    }

    private sealed record ScreenRow(string Code, string Seat, string Canonical);

    private readonly Regex _codeRegex;
    private static readonly Regex SeatRegex = new(@"\b(?<seat>\d{1,2}[A-F])\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly List<ConfirmedRow> _confirmed = new();
    private readonly List<PendingRow> _pendingNoSeat = new();
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

        // Eliminamos repeticiones dentro de la misma captura antes de tocar el acumulado.
        current = DeduplicateCurrentScreen(current);

        foreach (var row in current)
        {
            if (!string.IsNullOrWhiteSpace(row.Seat))
            {
                AddSeatRow(row);
                continue;
            }

            AddNoSeatRow(row);
        }

        // Un candidato sin asiento debe reaparecer en capturas consecutivas.
        // Si desaparece durante varios frames, se descarta para que un error aislado de OCR
        // no se transforme en un pasajero/edit nuevo.
        _pendingNoSeat.RemoveAll(p => _frame - p.LastFrame > 2);

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
            if (!string.IsNullOrWhiteSpace(row.Seat))
            {
                if (result.Any(r =>
                    r.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase) &&
                    r.Seat.Equals(row.Seat, StringComparison.OrdinalIgnoreCase)))
                    continue;

                result.Add(row);
                continue;
            }

            var duplicate = result.Any(r =>
                string.IsNullOrWhiteSpace(r.Seat) &&
                r.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase) &&
                CanonicalSimilarity(r.Canonical, row.Canonical) >= 0.90);

            if (!duplicate)
                result.Add(row);
        }

        return result;
    }

    private void AddSeatRow(ScreenRow row)
    {
        var existing = _confirmed.FirstOrDefault(seen =>
            seen.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase) &&
            seen.Seat.Equals(row.Seat, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            if (row.Canonical.Length > existing.Canonical.Length)
                existing.Canonical = row.Canonical;
            return;
        }

        _confirmed.Add(new ConfirmedRow
        {
            Code = row.Code,
            Seat = row.Seat,
            Canonical = row.Canonical
        });
    }

    private void AddNoSeatRow(ScreenRow row)
    {
        // Primero comparamos contra TODO lo ya confirmado del vuelo, no sólo contra la
        // pantalla previa ni contra la misma posición visual.
        var confirmedMatch = _confirmed.Any(seen =>
            seen.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase) &&
            CanonicalSimilarity(seen.Canonical, row.Canonical) >= 0.84);

        if (confirmedMatch)
            return;

        var pending = _pendingNoSeat
            .Where(p => p.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => CanonicalSimilarity(p.Canonical, row.Canonical))
            .FirstOrDefault(p => CanonicalSimilarity(p.Canonical, row.Canonical) >= 0.82);

        if (pending is null)
        {
            _pendingNoSeat.Add(new PendingRow
            {
                Code = row.Code,
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

        if (pending.ConsecutiveHits < 2)
            return;

        _confirmed.Add(new ConfirmedRow
        {
            Code = pending.Code,
            Seat = string.Empty,
            Canonical = pending.Canonical
        });
        _pendingNoSeat.Remove(pending);
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
