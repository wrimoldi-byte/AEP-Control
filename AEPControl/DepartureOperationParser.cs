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
        // Windows OCR suele convertir el guion de la matrícula en "_", dejarlo
        // separado por espacios o leerlo como raya. Aceptamos esas variantes,
        // pero exigimos un prefijo aeronáutico conocido y exactamente tres
        // caracteres para no fabricar matrículas desde otro texto del ITO.
        @"\b(?<prefix>CC|PR|PS)\s*(?:[-:_–—]|\s)\s*(?<suffix>[A-Z0-9]{3})\b",
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
        @"PASAJER[O0]\s*(?<tail>.{0,140})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PassengerInlineRegex = new(
        $@"\bJ\s*(?<premium>{OcrNumber})\s+Y\s*(?<economy>{OcrNumber})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PassengerHeadersRegex = new(
        $@"\bJ\s+Y\s+(?<premium>{OcrNumber})\s+(?<economy>{OcrNumber})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ServiceRegex = new(
        $@"\b(?<code>[CH]L[DO0][LR1I]|CSPY|SPM[2Z]|SPML[JYIV])\s*[:\-]?\s*(?<count>{OcrNumber})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static DepartureOperationData ParseMany(IEnumerable<string> readings)
    {
        var texts = readings
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        var parsed = texts.Select(Parse).ToList();
        var visualPassengerValues = texts
            .Select(TryReadPassengerBlockKeepingLines)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        // Elegimos primero los pasajeros y usamos ese dato para descartar una
        // configuración imposible. Por ejemplo, si el panel indica Y146, una
        // lectura OCR J16/Y136 es necesariamente un 5 confundido con un 3.
        var services = visualPassengerValues.Count > 0
            ? PickMostFrequent(visualPassengerValues)
            : PickMostFrequent(parsed.Select(item => item.Servicios));
        var configurations = parsed
            .Select(item => item.Configuracion)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        var plausibleConfigurations = configurations
            .Where(configuration => ConfigurationCanHoldPassengers(configuration, services))
            .ToList();

        return new DepartureOperationData
        {
            Vuelo = PickMostFrequent(parsed.Select(item => item.Vuelo)),
            Matricula = PickMostFrequent(parsed.Select(item => item.Matricula)),
            Configuracion = PickMostFrequent(
                plausibleConfigurations.Count > 0 ? plausibleConfigurations : configurations),
            // Para comidas/PAX manda el número visual debajo de J e Y.
            // CSPY/SPM2 se usa solamente si no logramos leer ese bloque.
            Servicios = services
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

        // Primero intentar leer el bloque PASAJERO conservando la estructura visual.
        result.Servicios = TryReadPassengerBlockKeepingLines(text);

        // Segundo intento sobre texto normalizado, por si Windows OCR unió las líneas.
        if (string.IsNullOrWhiteSpace(result.Servicios))
        {
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
            }
        }

        // Último fallback: reconstruir los totales desde las líneas de servicio.
        // Algunos OCR no conservan la posición de J/Y, pero sí leen con precisión
        // CLDL/HLDL, HLDR/SPM2 y los adicionales SPMLJ/SPMLY.
        if (string.IsNullOrWhiteSpace(result.Servicios))
        {
            int? baseJ = null;
            int? baseY = null;
            var extraJ = 0;
            var extraY = 0;

            foreach (Match match in ServiceRegex.Matches(normalized))
            {
                var code = NormalizeServiceCode(match.Groups["code"].Value);
                var countText = NormalizeOcrNumber(match.Groups["count"].Value);
                if (!int.TryParse(countText, out var count) || count < 0 || count > 399) continue;

                if (code is "CLDL" or "HLDL" or "CSPY") baseJ ??= count;
                else if (code is "HLDR" or "SPM2") baseY ??= count;
                else if (code == "SPMLJ") extraJ = Math.Max(extraJ, count);
                else if (code == "SPMLY") extraY = Math.Max(extraY, count);
            }

            if (baseJ.HasValue && baseY.HasValue)
            {
                var totalJ = baseJ.Value + extraJ;
                var totalY = baseY.Value + extraY;
                if (totalJ <= 399 && totalY <= 399)
                    result.Servicios = $"{totalJ}/{totalY}";
            }
        }

        return result;
    }

    private static string TryReadPassengerBlockKeepingLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var lines = text
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.ToUpperInvariant())
            .ToList();

        var passengerIndex = lines.FindIndex(line => Regex.IsMatch(line, @"PASAJER[O0]", RegexOptions.IgnoreCase));
        if (passengerIndex < 0) return string.Empty;

        // El ITO puede llegar del OCR de dos maneras:
        // 1) J e Y en la misma línea y 8/124 debajo.
        // 2) Por columnas: J, 8, CSPY 8... y luego Y, 124, SPM2 124...
        // En ambos casos queremos SOLO los números visuales del bloque Pasajero,
        // nunca la línea "Conf. Aeronave J 8 - Y 168".
        var end = Math.Min(lines.Count, passengerIndex + 16);

        static bool IsConfigurationLine(string line) =>
            Regex.IsMatch(line, @"CONF(?:IG(?:URACION)?)?\.?\s*(?:DE\s*)?AERONAVE|CONFIGURACION", RegexOptions.IgnoreCase);

        static string ReadStandaloneNumber(string line)
        {
            var match = Regex.Match(line.Trim(), $@"^(?<n>{OcrNumber})$");
            if (!match.Success) return string.Empty;
            var value = NormalizeOcrNumber(match.Groups["n"].Value);
            return IsReasonablePassengerCount(value) ? value : string.Empty;
        }

        // Caso OCR por columnas: encontrar J y Y como encabezados aislados y tomar
        // el primer número aislado inmediatamente posterior a cada encabezado.
        string jValue = string.Empty;
        string yValue = string.Empty;
        for (var i = passengerIndex; i < end; i++)
        {
            var line = lines[i].Trim();
            if (IsConfigurationLine(line)) continue;

            if (string.IsNullOrWhiteSpace(jValue) && Regex.IsMatch(line, @"^J$", RegexOptions.IgnoreCase))
            {
                for (var next = i + 1; next < Math.Min(end, i + 4); next++)
                {
                    var value = ReadStandaloneNumber(lines[next]);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        jValue = value;
                        break;
                    }
                    if (Regex.IsMatch(lines[next], @"\b(CSPY|SPM[2Z]|SPML[JYIV]|HANDLING)\b", RegexOptions.IgnoreCase))
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(yValue) && Regex.IsMatch(line, @"^Y$", RegexOptions.IgnoreCase))
            {
                for (var next = i + 1; next < Math.Min(end, i + 4); next++)
                {
                    var value = ReadStandaloneNumber(lines[next]);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        yValue = value;
                        break;
                    }
                    if (Regex.IsMatch(lines[next], @"\b(CSPY|SPM[2Z]|SPML[JYIV]|HANDLING)\b", RegexOptions.IgnoreCase))
                        break;
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(jValue) && !string.IsNullOrWhiteSpace(yValue))
            return $"{jValue}/{yValue}";

        for (var i = passengerIndex; i < end; i++)
        {
            var line = lines[i];
            if (IsConfigurationLine(line)) continue;

            // Caso: J 8        Y 124
            var inline = Regex.Match(line,
                $@"\bJ\s*(?<j>{OcrNumber})\b.*?\bY\s*(?<y>{OcrNumber})\b",
                RegexOptions.IgnoreCase);
            if (inline.Success)
            {
                var j = NormalizeOcrNumber(inline.Groups["j"].Value);
                var y = NormalizeOcrNumber(inline.Groups["y"].Value);
                if (IsReasonablePassengerCount(j) && IsReasonablePassengerCount(y))
                    return $"{j}/{y}";
            }

            // Caso típico visual: una línea con J ... Y y la siguiente con 8 ... 124.
            if (Regex.IsMatch(line, @"\bJ\b.*\bY\b", RegexOptions.IgnoreCase))
            {
                for (var next = i + 1; next < Math.Min(end, i + 4); next++)
                {
                    if (IsConfigurationLine(lines[next])) continue;
                    if (Regex.IsMatch(lines[next], @"\b(CSPY|SPM[2Z]|SPML[JYIV])\b", RegexOptions.IgnoreCase))
                        break;

                    var nums = Regex.Matches(lines[next], $@"\b{OcrNumber}\b")
                        .Select(match => NormalizeOcrNumber(match.Value))
                        .Where(IsReasonablePassengerCount)
                        .Take(2)
                        .ToList();
                    if (nums.Count == 2)
                        return $"{nums[0]}/{nums[1]}";
                }
            }
        }

        return string.Empty;
    }

    private static string PickMostFrequent(IEnumerable<string> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(group => group.Count())
        .ThenByDescending(group => group.Key.Length)
        .Select(group => group.Key)
        .FirstOrDefault() ?? string.Empty;

    private static bool IsReasonablePassengerCount(string value) =>
        int.TryParse(value, out var number) && number >= 0 && number <= 399;

    private static bool ConfigurationCanHoldPassengers(string configuration, string passengers)
    {
        var configurationParts = configuration.Split('/');
        var passengerParts = passengers.Split('/');
        if (configurationParts.Length != 2 || passengerParts.Length != 2)
            return true;

        return int.TryParse(configurationParts[0], out var configurationJ) &&
               int.TryParse(configurationParts[1], out var configurationY) &&
               int.TryParse(passengerParts[0], out var passengerJ) &&
               int.TryParse(passengerParts[1], out var passengerY) &&
               configurationJ >= passengerJ &&
               configurationY >= passengerY;
    }

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
        if ((code.StartsWith("HL", StringComparison.Ordinal) ||
             code.StartsWith("CL", StringComparison.Ordinal)) && code.Length == 4)
        {
            var prefix = code.StartsWith("CL", StringComparison.Ordinal) ? "CLD" : "HLD";
            return code[3] == 'R' ? prefix + "R" : prefix + "L";
        }
        if (code is "SPMLI" or "SPMLV")
            return code == "SPMLI" ? "SPMLJ" : "SPMLY";
        if (code == "SPMZ")
            return "SPM2";
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
