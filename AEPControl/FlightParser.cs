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
        var result = new List<FlightData>();
        var normalized = Normalize(text);
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

        var rawTime = timeMatch.Groups["time"].Value.Replace('.', ':');
        if (!rawTime.Contains(':'))
            rawTime = rawTime.PadLeft(4, '0').Insert(2, ":");

        return new FlightData
        {
            Vuelo = $"LA{flightMatch.Groups["flight"].Value}",
            Destino = destination,
            Hora = rawTime,
            Equipo = equipmentMatch.Groups["equip"].Value,
            Premium = int.Parse(bookingMatch.Groups["premium"].Value),
            Economy = int.Parse(bookingMatch.Groups["economy"].Value)
        };
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
