using System.Text.RegularExpressions;

namespace AEPControl;

public static class DepartureOperationParser
{
    private const string OcrNumber = @"[0-9OQDIL|BS]{1,3}";

    private static readonly Regex LabeledFlightRegex = new(
        @"(?:N(?:RO|[°ºO0])?\s*DE\s*VUELO|NUMERO\s+DE\s+VUELO|VUELO)\s*(?:LA\s*)?(?<number>\d{3,4})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FlightRegex = new(
        @"\bLA\s*[-:]?\s*(?<number>\d{3,4})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RegistrationRegex = new(
        @"\b(?<prefix>CC|PR|PS)\s*[-:]?\s*(?<suffix>[A-Z0-9]{3})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex StrictConfigurationRegex = new(
        $@"\bJ\s*(?<premium>{OcrNumber})\s*[-/|]\s*Y\s*(?<economy>{OcrNumber})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ConfigurationLabelRegex = new(
        @"CONF(?:IG(?:URACION)?)?\.?\s*(?:DE\s*)?AERONAVE(?<tail>.{0,90})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LooseConfigurationRegex = new(
        $@"\bJ\s*(?<premium>{OcrNumber})\s*(?:[-/|]\s*)?Y\s*(?<economy>{OcrNumber})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Servicios/catering del ITO. Se exige el código para no confundir los números
    // de PASAJERO (J/Y) ni la configuración de la aeronave con comidas/snacks.
    private static readonly Regex ServiceRegex = new(
        $@"\b(?<code>HL[DO0][LR1I]|CSPY|SPM2|SPML[JYIV])\s*[:\-]?\s*(?<count>{OcrNumber})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static DepartureOperationData ParseMany(IEnumerable<string> readings)
    {
        var parsed = readings
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(Parse)
            .ToList();

        return new DepartureOperationData
        {
            Vuelo = PickMostFrequent(parsed.Select(item => item.Vuelo)),
            Matricula = PickMostFrequent(parsed.Select(item => item.Matricula)),
            Configuracion = PickMostFrequent(parsed.Select(item => item.Configuracion)),
            Servicios = PickRichestServices(parsed.Select(item => item.Servicios))
        };
    }

    public static DepartureOperationData Parse(string text)
    {
        var normalized = Normalize(text);
        var result = new DepartureOperationData();

        var flight = LabeledFlightRegex.Match(normalized);
        if (!flight.Success)
            flight = FlightRegex.Match(normalized);
        if (flight.Success)
            result.Vuelo = $"LA{flight.Groups["number"].Value}";

        var registration = RegistrationRegex.Match(normalized);
        if (registration.Success)
        {
            var suffix = NormalizeRegistrationSuffix(registration.Groups["suffix"].Value);
            result.Matricula = $"{registration.Groups["prefix"].Value}-{suffix}";
        }

        // Primero buscar J/Y dentro del bloque CONF. AERONAVE.
        // Así no tomamos por error el bloque superior PASAJERO J/Y.
        Match configuration = Match.Empty;
        var label = ConfigurationLabelRegex.Match(normalized);
        if (label.Success)
            configuration = LooseConfigurationRegex.Match(label.Groups["tail"].Value);

        // Fallback para versiones de ITO donde el OCR perdió la etiqueta de configuración.
        if (!configuration.Success)
            configuration = StrictConfigurationRegex.Match(normalized);

        if (configuration.Success)
        {
            var premium = NormalizeOcrNumber(configuration.Groups["premium"].Value);
            var economy = NormalizeOcrNumber(configuration.Groups["economy"].Value);
            if (premium.Length > 0 && economy.Length > 0)
                result.Configuracion = $"J {premium} - Y {economy}";
        }

        var services = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in ServiceRegex.Matches(normalized))
        {
            var countText = NormalizeOcrNumber(match.Groups["count"].Value);
            if (!int.TryParse(countText, out var count)) continue;
            services[NormalizeServiceCode(match.Groups["code"].Value)] = count;
        }

        if (services.Count > 0)
        {
            var ordered = services
                .OrderBy(item => ServiceOrder(item.Key))
                .Select(item => $"{item.Key} {item.Value}")
                .ToList();
            result.Servicios = ordered.Count > 0 ? string.Join(" / ", ordered) : "SIN SERVICIOS";
        }

        return result;
    }

    private static string PickMostFrequent(IEnumerable<string> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(group => group.Count())
        .ThenByDescending(group => group.Key.Length)
        .Select(group => group.Key)
        .FirstOrDefault() ?? string.Empty;

    private static string PickRichestServices(IEnumerable<string> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(group => Regex.Matches(group.Key, @"\b(?:HLDL|HLDR|CSPY|SPM2|SPMLJ|SPMLY)\b").Count)
        .ThenByDescending(group => group.Count())
        .ThenByDescending(group => group.Key.Length)
        .Select(group => group.Key)
        .FirstOrDefault() ?? string.Empty;

    private static string NormalizeRegistrationSuffix(string value) => value
        .ToUpperInvariant()
        .Replace('0', 'O')
        .Replace('1', 'I')
        .Replace('8', 'B');

    private static string NormalizeOcrNumber(string value)
    {
        var normalized = value
            .ToUpperInvariant()
            .Replace('O', '0')
            .Replace('Q', '0')
            .Replace('D', '0')
            .Replace('I', '1')
            .Replace('L', '1')
            .Replace('B', '8')
            .Replace('S', '5')
            .Replace('|', '1');
        return new string(normalized.Where(char.IsDigit).ToArray());
    }

    private static string NormalizeServiceCode(string value)
    {
        var code = value.ToUpperInvariant();
        if (code.StartsWith("HL", StringComparison.Ordinal) && code.Length == 4)
            return code[3] == 'R' ? "HLDR" : "HLDL";
        if (code is "SPMLI" or "SPMLV")
            return code == "SPMLI" ? "SPMLJ" : "SPMLY";
        return code;
    }

    private static int ServiceOrder(string code) => code switch
    {
        "HLDL" => 1,
        "HLDR" => 2,
        "CSPY" => 3,
        "SPM2" => 4,
        "SPMLJ" => 5,
        "SPMLY" => 6,
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
