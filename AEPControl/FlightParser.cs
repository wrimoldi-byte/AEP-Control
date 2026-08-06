using System.Text.RegularExpressions;

namespace AEPControl;

public static partial class FlightParser
{
    [GeneratedRegex(@"\b(?:LA\s*)?(?<flight>\d{3,4})\b.*?\b(?<dest>[A-Z]{3})\b.*?\b(?<time>[0-2]?\d[:.]\d{2})\b.*?\b(?<equip>3?20|319|321|789|788|787|767)\b.*?\b(?<premium>\d{1,3})\s*[/\\]\s*(?<economy>\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RowRegex();

    public static List<FlightData> Parse(string text)
    {
        var result = new List<FlightData>();
        var normalized = text.Replace('\r', '\n');

        foreach (var raw in normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = Regex.Replace(raw.ToUpperInvariant(), @"\s+", " ");
            var match = RowRegex().Match(line);
            if (!match.Success) continue;

            var flight = match.Groups["flight"].Value;
            var data = new FlightData
            {
                Vuelo = $"LA{flight}",
                Destino = match.Groups["dest"].Value,
                Hora = match.Groups["time"].Value.Replace('.', ':'),
                Equipo = match.Groups["equip"].Value,
                Premium = int.Parse(match.Groups["premium"].Value),
                Economy = int.Parse(match.Groups["economy"].Value)
            };

            if (!result.Any(x => x.Vuelo == data.Vuelo && x.Hora == data.Hora))
                result.Add(data);
        }

        return result.OrderBy(x => x.Hora).ToList();
    }
}
