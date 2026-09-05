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

    private static readonly Regex RegistrationLabelRegex = new(
        @"MATR(?:I|1|L)CULA\s*[:\-]?\s*(?<tail>.{0,35})",
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

    private static readonly Regex PassengerLabelRegex = new(
        @"PASAJER[O0]\s*(?<tail>.{0,120})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PassengerInlineRegex = new(
        $@"\bJ\s*(?<premium>{OcrNumber})\s+Y\s*(?<economy>{OcrNumber})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PassengerHeadersRegex = new(
        $@"\bJ\s+Y\s+(?<premium>{OcrNumber})\s+(?<economy>{OcrNumber})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
            Matricula = PickRegistrationConsensus(parsed.Select(item => item.Matricula)),
            Configuracion = PickMostFrequent(parsed.Select(item => item.Configuracion)),
            Servicios = PickMostFrequent(parsed.Select(item => item.Servicios))
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

        Match registration = Match.Empty;
        var registrationLabel = RegistrationLabelRegex.Match(normalized);
        if (registrationLabel.Success)
            registration = RegistrationRegex.Match(registrationLabel.Groups["tail"].Value);
        if (!registration.Success)
            registration = RegistrationRegex.Match(normalized);

        if (registration.Success)
        {
            var suffix = NormalizeRegistrationSuffix(registration.Groups["suffix"].Value);
            result.Matricula = $"{registration.Groups["prefix"].Value.ToUpperInvariant()}-{suffix}";
        }

        Match configuration = Match.Empty;
        var label = ConfigurationLabelRegex.Match(normalized);
        if (label.Success)
            configuration = LooseConfigurationRegex.Match(label.Groups["tail"].Value);
        if (!configuration.Success)
            configuration = StrictConfigurationRegex.Match(normalized);

        if (configuration.Success)
        {
            var premium = NormalizeOcrNumber(configuration.Groups["premium"].Value);
            var economy = NormalizeOcrNumber(configuration.Groups["economy"].Value);
            if (premium.Length > 0 && economy.Length > 0)
                result.Configuracion = $"{premium}/{economy}";
        }

        // Para comidas usamos los dos números del bloque PASAJERO del ITO.
        // Ejemplo visual: J=16, Y=137 -> se guarda como 16/137.
        var passengerLabel = PassengerLabelRegex.Match(normalized);
        if (passengerLabel.Success)
        {
            var tail = passengerLabel.Groups["tail"].Value;
            var passengerMatch = PassengerInlineRegex.Match(tail);
            if (!passengerMatch.Success)
                passengerMatch = PassengerHeadersRegex.Match(tail);

            if (passengerMatch.Success)
            {
                var premium = NormalizeOcrNumber(passengerMatch.Groups["premium"].Value);
                var economy = NormalizeOcrNumber(passengerMatch.Groups["economy"].Value);
                if (IsReasonablePassengerCount(premium) && IsReasonablePassengerCount(economy))
                    result.Servicios = $"{premium}/{economy}";
            }
            else
            {
                var numbers = Regex.Matches(tail, $@"\b{OcrNumber}\b")
                    .Select(match => NormalizeOcrNumber(match.Value))
                    .Where(IsReasonablePassengerCount)
                    .Take(2)
                    .ToList();
                if (numbers.Count == 2)
                    result.Servicios = $"{numbers[0]}/{numbers[1]}";
            }
        }

        // Fallback: si el OCR perdió la palabra PASAJERO, usar CSPY/SPM2.
        if (string.IsNullOrWhiteSpace(result.Servicios))
        {
            int? j = null;
            int? y = null;
            foreach (Match match in ServiceRegex.Matches(normalized))
            {
                var code = NormalizeServiceCode(match.Groups["code"].Value);
                var countText = NormalizeOcrNumber(match.Groups["count"].Value);
                if (!int.TryParse(countText, out var count)) continue;
                if (code == "CSPY") j ??= count;
                if (code == "SPM2") y ??= count;
            }
            if (j.HasValue && y.HasValue)
                result.Servicios = $"{j.Value}/{y.Value}";
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

    private static string PickRegistrationConsensus(IEnumerable<string> values)
    {
        var registrations = values
            .Where(value => Regex.IsMatch(value ?? string.Empty, @"^(CC|PR|PS)-[A-Z]{3}$", RegexOptions.IgnoreCase))
            .Select(value => value.ToUpperInvariant())
            .ToList();
        if (registrations.Count == 0) return string.Empty;

        var prefix = registrations
            .GroupBy(value => value[..2])
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .First();
        var samePrefix = registrations.Where(value => value.StartsWith(prefix + "-", StringComparison.Ordinal)).ToList();
        var suffix = new char[3];
        for (var i = 0; i < 3; i++)
        {
            suffix[i] = samePrefix
                .GroupBy(value => value[3 + i])
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => group.Key)
                .First();
        }
        return $"{prefix}-{new string(suffix)}";
    }

    private static bool IsReasonablePassengerCount(string value) =>
        int.TryParse(value, out var number) && number >= 0 && number <= 399;

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
