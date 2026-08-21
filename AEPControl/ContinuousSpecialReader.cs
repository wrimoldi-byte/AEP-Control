using System.Text;
using System.Text.RegularExpressions;

namespace AEPControl;

public sealed class ContinuousSpecialReader
{
    private sealed class ConfirmedRow
    {
        public string Code { get; init; } = string.Empty;
        public string Seat { get; init; } = string.Empty;
        public string Passenger { get; init; } = string.Empty;
        public string Canonical { get; set; } = string.Empty;
    }

    private sealed class PendingRow
    {
        public string Code { get; init; } = string.Empty;
        public string Seat { get; init; } = string.Empty;
        public string Passenger { get; init; } = string.Empty;
        public string Canonical { get; set; } = string.Empty;
        public int ConsecutiveHits { get; set; }
        public int LastFrame { get; set; }
    }

    private sealed record ScreenRow(string Code, string Seat, string Passenger, string Canonical);

    private readonly Regex _codeRegex;
    private static readonly Regex SeatRegex = new(@"\b(?<seat>\d{1,2}[A-F])\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SlashNameRegex = new(@"\b(?<last>[A-Z]{2,})\s*/\s*(?<first>[A-Z]{2,})(?:\s+(?<extra>[A-Z]{2,}))?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> IgnoredNameTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "SSR", "EDIT", "EDITS", "SUSEDIT", "SEAT", "ASIENTO", "PAX", "PASSENGER",
        "HK", "HK1", "HN", "HN1", "NN", "NN1", "UC", "NO", "YES", "OSI", "DOCS",
        "MR", "MRS", "MISS", "MS", "CHD", "ADT", "INFANT", "BABY"
    };

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
            var passenger = ExtractPassengerKey(line, codeMatches.Select(m => m.Value));

            foreach (var code in codeMatches
                .Select(m => m.Value.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                result.Add(new ScreenRow(code, seat, passenger, canonical));
            }
        }

        return result;
    }

    private static string ExtractPassengerKey(string line, IEnumerable<string> codes)
    {
        var slash = SlashNameRegex.Match(line);
        if (slash.Success)
        {
            var last = slash.Groups["last"].Value;
            var first = slash.Groups["first"].Value;
            return NormalizePassenger($"{last}/{first}");
        }

        var codeSet = codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tokens = Regex.Matches(line, @"\b[A-Z]{3,}\b")
            .Select(m => m.Value.ToUpperInvariant())
            .Where(t => !codeSet.Contains(t))
            .Where(t => !IgnoredNameTokens.Contains(t))
            .Where(t => !Regex.IsMatch(t, @"^[A-Z]{1,2}\d+$"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tokens.Count >= 2)
            return NormalizePassenger(tokens[0] + "/" + tokens[1]);

        return string.Empty;
    }

    private static string NormalizePassenger(string value)
    {
        var upper = value.ToUpperInvariant();
        var builder = new StringBuilder(upper.Length);
        foreach (var ch in upper)
        {
            if (char.IsLetter(ch) || ch == '/')
                builder.Append(ch);
        }
        return builder.ToString();
    }

    private static List<ScreenRow> DeduplicateCurrentScreen(List<ScreenRow> rows)
    {
        var result = new List<ScreenRow>();

        foreach (var row in rows)
        {
            var duplicate = result.Any(r => SameIdentity(r, row) ||
                (r.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase) &&
                 string.IsNullOrWhiteSpace(r.Seat) && string.IsNullOrWhiteSpace(row.Seat) &&
                 string.IsNullOrWhiteSpace(r.Passenger) && string.IsNullOrWhiteSpace(row.Passenger) &&
                 CanonicalSimilarity(r.Canonical, row.Canonical) >= 0.90));

            if (!duplicate)
                result.Add(row);
        }

        return result;
    }

    private static bool SameIdentity(ScreenRow a, ScreenRow b)
    {
        if (!a.Code.Equals(b.Code, StringComparison.OrdinalIgnoreCase)) return false;

        if (!string.IsNullOrWhiteSpace(a.Seat) && !string.IsNullOrWhiteSpace(b.Seat))
            return a.Seat.Equals(b.Seat, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(a.Passenger) && !string.IsNullOrWhiteSpace(b.Passenger))
            return PassengerEquivalent(a.Passenger, b.Passenger);

        return false;
    }

    private void AddCandidate(ScreenRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.Seat))
        {
            var exactSeat = _confirmed.FirstOrDefault(c =>
                c.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase) &&
                c.Seat.Equals(row.Seat, StringComparison.OrdinalIgnoreCase));
            if (exactSeat is not null)
            {
                UpdateCanonical(exactSeat, row.Canonical);
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(row.Passenger))
        {
            var exactPassenger = _confirmed.FirstOrDefault(c =>
                c.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(c.Passenger) &&
                PassengerEquivalent(c.Passenger, row.Passenger));
            if (exactPassenger is not null)
            {
                UpdateCanonical(exactPassenger, row.Canonical);
                return;
            }
        }

        var sameConfirmedRow = _confirmed.Any(c =>
            c.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase) &&
            CanonicalSimilarity(c.Canonical, row.Canonical) >= 0.82);
        if (sameConfirmedRow)
            return;

        var pending = FindPending(row);
        if (pending is null)
        {
            _pending.Add(new PendingRow
            {
                Code = row.Code,
                Seat = row.Seat,
                Passenger = row.Passenger,
                Canonical = row.Canonical,
                ConsecutiveHits = 1,
                LastFrame = _frame
            });
            return;
        }

        if (pending.LastFrame == _frame)
            return;

        pending.ConsecutiveHits = pending.LastFrame == _frame - 1 ? pending.ConsecutiveHits + 1 : 1;
        pending.LastFrame = _frame;
        if (row.Canonical.Length > pending.Canonical.Length)
            pending.Canonical = row.Canonical;

        if (pending.ConsecutiveHits < 2)
            return;

        var duplicateConfirmed = _confirmed.Any(c =>
            c.Code.Equals(pending.Code, StringComparison.OrdinalIgnoreCase) &&
            ((!string.IsNullOrWhiteSpace(pending.Seat) && c.Seat.Equals(pending.Seat, StringComparison.OrdinalIgnoreCase)) ||
             (!string.IsNullOrWhiteSpace(pending.Passenger) && !string.IsNullOrWhiteSpace(c.Passenger) && PassengerEquivalent(c.Passenger, pending.Passenger)) ||
             CanonicalSimilarity(c.Canonical, pending.Canonical) >= 0.82));

        if (!duplicateConfirmed)
        {
            _confirmed.Add(new ConfirmedRow
            {
                Code = pending.Code,
                Seat = pending.Seat,
                Passenger = pending.Passenger,
                Canonical = pending.Canonical
            });
        }

        _pending.Remove(pending);
    }

    private PendingRow? FindPending(ScreenRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.Seat))
        {
            var bySeat = _pending.FirstOrDefault(p =>
                p.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase) &&
                p.Seat.Equals(row.Seat, StringComparison.OrdinalIgnoreCase));
            if (bySeat is not null) return bySeat;
        }

        if (!string.IsNullOrWhiteSpace(row.Passenger))
        {
            var byPassenger = _pending.FirstOrDefault(p =>
                p.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(p.Passenger) &&
                PassengerEquivalent(p.Passenger, row.Passenger));
            if (byPassenger is not null) return byPassenger;
        }

        return _pending
            .Where(p => p.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => CanonicalSimilarity(p.Canonical, row.Canonical))
            .FirstOrDefault(p => CanonicalSimilarity(p.Canonical, row.Canonical) >= 0.88);
    }

    private static void UpdateCanonical(ConfirmedRow row, string canonical)
    {
        if (canonical.Length > row.Canonical.Length)
            row.Canonical = canonical;
    }

    private static bool PassengerEquivalent(string a, string b)
    {
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
        if (a.Length < 6 || b.Length < 6) return false;
        return Similarity(a, b) >= 0.88;
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
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return 1;
        return Math.Max(Similarity(a, b), TokenSimilarity(a, b));
    }

    private static double TokenSimilarity(string a, string b)
    {
        var aTokens = StableTokens(a);
        var bTokens = StableTokens(b);
        if (aTokens.Count == 0 || bTokens.Count == 0) return 0;

        var intersection = aTokens.Intersect(bTokens, StringComparer.OrdinalIgnoreCase).Count();
        if (intersection == 0) return 0;
        return (double)intersection / Math.Max(aTokens.Count, bTokens.Count);
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
        var normalized = Regex.Replace(line, @"(?<=\d)[OQ](?=\d)", "0");
        normalized = Regex.Replace(normalized, @"(?<=\d)[IL](?=\d)", "1");

        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
            builder.Append(char.IsLetterOrDigit(ch) ? ch : ' ');

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
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private SpecialCounts BuildCounts()
    {
        var result = new SpecialCounts();

        // Para asistencia de silla de ruedas, un mismo pasajero puede venir con más de
        // un EDIT (por ejemplo WCHR + WCHS). En ese caso no son dos pasajeros.
        // Contamos una sola vez según la asistencia más restrictiva:
        // WCHC > WCHS > WCHR.
        var wheelchairRows = _confirmed
            .Where(r => IsWheelchairCode(r.Code))
            .ToList();

        var consumed = new HashSet<ConfirmedRow>();
        foreach (var row in wheelchairRows)
        {
            if (consumed.Contains(row)) continue;

            var samePassenger = wheelchairRows
                .Where(other => !consumed.Contains(other) && SamePassengerForWheelchair(row, other))
                .ToList();

            if (samePassenger.Count == 0)
                samePassenger.Add(row);

            var selectedCode = samePassenger
                .Select(r => r.Code)
                .OrderByDescending(WheelchairPriority)
                .First();

            AddCount(result, selectedCode);
            foreach (var item in samePassenger)
                consumed.Add(item);
        }

        foreach (var row in _confirmed.Where(r => !IsWheelchairCode(r.Code)))
            AddCount(result, row.Code);

        return result;
    }

    private static bool IsWheelchairCode(string code) =>
        code is "WCHR" or "WCHS" or "WCHC";

    private static int WheelchairPriority(string code) => code switch
    {
        "WCHC" => 3,
        "WCHS" => 2,
        "WCHR" => 1,
        _ => 0
    };

    private static bool SamePassengerForWheelchair(ConfirmedRow a, ConfirmedRow b)
    {
        if (!string.IsNullOrWhiteSpace(a.Passenger) && !string.IsNullOrWhiteSpace(b.Passenger) &&
            PassengerEquivalent(a.Passenger, b.Passenger))
            return true;

        if (!string.IsNullOrWhiteSpace(a.Seat) && !string.IsNullOrWhiteSpace(b.Seat) &&
            a.Seat.Equals(b.Seat, StringComparison.OrdinalIgnoreCase))
            return true;

        // Si no hay nombre ni asiento, sólo unimos cuando la misma fila OCR contiene
        // ambos códigos. Así evitamos fusionar dos pasajeros distintos por error.
        if (string.IsNullOrWhiteSpace(a.Passenger) && string.IsNullOrWhiteSpace(b.Passenger) &&
            string.IsNullOrWhiteSpace(a.Seat) && string.IsNullOrWhiteSpace(b.Seat) &&
            CanonicalSimilarity(a.Canonical, b.Canonical) >= 0.94)
            return true;

        return ReferenceEquals(a, b);
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
