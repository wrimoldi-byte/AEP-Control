using System.Text.RegularExpressions;

namespace AEPControl;

public static class FlightParser
{
    private static readonly Regex FlightRegex = new(@"\b(?:LA\s*)?(?<flight>\d{3,4})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DestinationRegex = new(@"\b(?<dest>[A-Z]{3})\b", RegexOptions.Compiled);
    private static readonly Regex TimeRegex = new(@"\b(?<time>(?:[01]?\d|2[0-3])[:.]?[0-5]\d)\b", RegexOptions.Compiled);
    private static readonly Regex EquipmentRegex = new(@"\b(?<equip>319|320|321|32[0-9]|767|787|788|789)\b", RegexOptions.Compiled);
    private static readonly Regex BookingRegex = new(@"\b(?<premium>\d{1,3})\s*[/\\|]\s*(?<economy>\d{1,3})\b", RegexOptions.Compiled);

    private static readonly HashSet<string> IgnoredThreeLetterWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "OPEN", "FOR", "THE", "DEL", "VUE", "EQU", "BKG", "BKD", "PUE", "MAP", "EST", "SAL"
    };

    public static List<FlightData> Parse(string text)
    {
        var normalized = Normalize(text);
        var columnResult = ParseColumnTable(normalized);
        if (columnResult.Count > 0) return columnResult;
        return ParseRowWindows(normalized);
    }

    private static List<FlightData> ParseColumnTable(string text)
    {
        var flightSection = ExtractSection(text, @"\bVUELO\b", @"\bFECHA\b");
        var destinationSection = ExtractSection(text, @"\b(?:ORIGEN|DESTINO)\b", @"\bPUERTA\b");
        var timeSection = ExtractSection(text, @"\bHORA\s+(?:LLEGADA|SALIDA)\b", @"\bETA\b");
        var bookingSection = ExtractSection(text, @"\bCANTIDAD\s+B\w*\b", @"\z");

        if (flightSection is null || destinationSection is null || timeSection is null || bookingSection is null)
            return new List<FlightData>();

        var flights = Regex.Matches(flightSection, @"\b\d{3,4}\b")
            .Select(m => m.Value).ToList();
        var destinations = Regex.Matches(destinationSection, @"\b[A-Z]{3}\b")
            .Select(m => m.Value)
            .Where(value => !IgnoredThreeLetterWords.Contains(value))
            .ToList();
        var times = Regex.Matches(timeSection, @"\b(?:[01]\d|2[0-3])[0-5]\d\b")
            .Select(m => FormatTime(m.Value)).ToList();
        var bookings = BookingRegex.Matches(bookingSection)
            .Select(m => (
                Premium: int.Parse(m.Groups["premium"].Value),
                Economy: int.Parse(m.Groups["economy"].Value)))
            .ToList();

        if (flights.Count == 0 || destinations.Count != flights.Count ||
            times.Count != flights.Count || bookings.Count != flights.Count)
            return new List<FlightData>();

        var result = new List<FlightData>(flights.Count);
        for (var i = 0; i < flights.Count; i++)
        {
            result.Add(new FlightData
            {
                Vuelo = $"LA{flights[i]}",
                Destino = destinations[i],
                Hora = times[i],
                Equipo = string.Empty,
                Premium = bookings[i].Premium,
                Economy = bookings[i].Economy,
                BookingKnown = true
            });
        }

        return result.OrderBy(x => x.Hora).ThenBy(x => x.Vuelo).ToList();
    }

    private static string? ExtractSection(string text, string startPattern, string endPattern)
    {
        var match = Regex.Match(
            text,
            $@"(?:{startPattern})(?<value>.*?)(?={endPattern})",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static List<FlightData> ParseRowWindows(string normalized)
    {
        var result = new List<FlightData>();
        var lines = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var i = 0; i < lines.Length; i++)
        {
            var window = lines[i];
            if (i + 1 < lines.Length) window += " " + lines[i + 1];
            if (i + 2 < lines.Length) window += " " + lines[i + 2];

            var data = TryParseWindow(window);
            if (data is null) continue;

            if (!result.Any(x => x.Vuelo == data.Vuelo && x.Hora == data.Hora))
                result.Add(data);
        }

        return result.OrderBy(x => x.Hora).ThenBy(x => x.Vuelo).ToList();
    }

    private static FlightData? TryParseWindow(string window)
    {
        var flightMatch = FlightRegex.Match(window);
        var bookingMatch = BookingRegex.Match(window);
        if (!flightMatch.Success || !bookingMatch.Success) return null;

        var afterFlight = window[flightMatch.Index..];
        var timeMatch = TimeRegex.Match(afterFlight);
        var equipmentMatch = EquipmentRegex.Match(afterFlight);
        if (!timeMatch.Success || !equipmentMatch.Success) return null;

        var destination = FindDestination(afterFlight, flightMatch.Length, timeMatch.Index);
        if (destination is null) return null;

        return new FlightData
        {
            Vuelo = $"LA{flightMatch.Groups["flight"].Value}",
            Destino = destination,
            Hora = FormatTime(timeMatch.Groups["time"].Value),
            Equipo = equipmentMatch.Groups["equip"].Value,
            Premium = int.Parse(bookingMatch.Groups["premium"].Value),
            Economy = int.Parse(bookingMatch.Groups["economy"].Value),
            BookingKnown = true
        };
    }

    private static string FormatTime(string rawTime)
    {
        var value = rawTime.Replace('.', ':');
        return value.Contains(':') ? value : value.PadLeft(4, '0').Insert(2, ":");
    }

    private static string? FindDestination(string text, int searchStart, int searchEnd)
    {
        var length = Math.Max(0, searchEnd - searchStart);
        if (length == 0) return null;

        var segment = text.Substring(searchStart, length);
        foreach (Match match in DestinationRegex.Matches(segment))
        {
            var value = match.Groups["dest"].Value;
            if (!IgnoredThreeLetterWords.Contains(value)) return value;
        }

        return null;
    }

    private static string Normalize(string text)
    {
        var value = text.ToUpperInvariant()
            .Replace('\r', '\n')
            .Replace('—', '-')
            .Replace('–', '-');

        value = Regex.Replace(value, @"(?<=\d)[OQ](?=\d)", "0");
        value = Regex.Replace(value, @"(?<=\d)[IL](?=\d)", "1");
        value = Regex.Replace(value, @"[ \t]+", " ");
        value = Regex.Replace(value, @"\n+", "\n");
        return value;
    }
}
