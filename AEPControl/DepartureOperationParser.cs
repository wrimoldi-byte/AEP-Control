using System.Text.RegularExpressions;

namespace AEPControl;

public static class DepartureOperationParser
{
    private static readonly Regex FlightRegex = new(
        @"\bLA\s*[-:]?\s*(?<number>\d{3,4})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RegistrationRegex = new(
        @"\b(?<prefix>CC|PR|PS)\s*-?\s*(?<suffix>[A-Z0-9]{3})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex StrictConfigurationRegex = new(
        @"\bJ\s*(?<premium>\d{1,3})\s*[-/|]\s*Y\s*(?<economy>\d{1,3})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ConfigurationLabelRegex = new(
        @"CONF(?:IG(?:URACION)?)?\.?\s*(?:DE\s*)?AERONAVE(?<tail>.{0,60})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LooseConfigurationRegex = new(
        @"\bJ\s*(?<premium>\d{1,3})\s*(?:[-/|]\s*)?Y\s*(?<economy>\d{1,3})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ServiceRegex = new(
        @"\b(?<code>HLDL|HLDR|SPMLJ|SPMLY)\s*(?<count>\d{1,3})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static DepartureOperationData Parse(string text)
    {
        var normalized = Normalize(text);
        var result = new DepartureOperationData();

        var flight = FlightRegex.Match(normalized);
        if (flight.Success)
            result.Vuelo = $"LA{flight.Groups["number"].Value}";

        var registration = RegistrationRegex.Match(normalized);
        if (registration.Success)
            result.Matricula = $"{registration.Groups["prefix"].Value}-{registration.Groups["suffix"].Value}";

        var configuration = StrictConfigurationRegex.Match(normalized);
        if (!configuration.Success)
        {
            var label = ConfigurationLabelRegex.Match(normalized);
            if (label.Success)
                configuration = LooseConfigurationRegex.Match(label.Groups["tail"].Value);
        }

        if (configuration.Success)
            result.Configuracion = $"J {configuration.Groups["premium"].Value} - Y {configuration.Groups["economy"].Value}";

        var services = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in ServiceRegex.Matches(normalized))
            services[match.Groups["code"].Value.ToUpperInvariant()] = int.Parse(match.Groups["count"].Value);

        if (services.Count > 0)
        {
            var positive = services
                .Where(item => item.Value > 0)
                .OrderBy(item => ServiceOrder(item.Key))
                .Select(item => $"{item.Key} {item.Value}")
                .ToList();
            result.Servicios = positive.Count > 0 ? string.Join(" / ", positive) : "SIN SERVICIOS";
        }

        return result;
    }

    private static int ServiceOrder(string code) => code switch
    {
        "HLDL" => 1,
        "HLDR" => 2,
        "SPMLJ" => 3,
        "SPMLY" => 4,
        _ => 10
    };

    private static string Normalize(string text)
    {
        var value = text.ToUpperInvariant()
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('—', '-')
            .Replace('–', '-')
            .Replace('Á', 'A')
            .Replace('É', 'E')
            .Replace('Í', 'I')
            .Replace('Ó', 'O')
            .Replace('Ú', 'U');

        value = Regex.Replace(value, @"(?<=\d)[OQ](?=\d)", "0");
        value = Regex.Replace(value, @"(?<=\d)[IL](?=\d)", "1");
        value = Regex.Replace(value, @"\s+", " ");
        return value.Trim();
    }
}
