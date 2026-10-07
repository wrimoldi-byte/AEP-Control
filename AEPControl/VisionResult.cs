using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;

namespace AEPControl;

public sealed class VisionResult
{
    public List<FlightData> Flights { get; } = new();
    public DepartureOperationData Ito { get; } = new();
    public List<VisionSpecialRow> Specials { get; } = new();
    public List<string> Warnings { get; } = new();

    public static VisionResult Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var result = new VisionResult();
        foreach (var warning in root.GetProperty("warnings").EnumerateArray())
            result.Warnings.Add(warning.GetString() ?? "Dato dudoso.");
        foreach (var item in root.GetProperty("flights").EnumerateArray())
        {
            var number = FlightNumber(Text(item, "flight"));
            if (number.Length == 0) { result.Warnings.Add("Una fila no tiene vuelo legible y se omitió."); continue; }
            var airport = Text(item, "airport").ToUpperInvariant();
            if (airport.Length > 0 && !Regex.IsMatch(airport, @"^[A-Z]{3}$")) { airport = ""; result.Warnings.Add($"{number}: aeropuerto inválido."); }
            var rawHour = Text(item, "time");
            var hour = NormalizeTime(rawHour);
            if (hour.Length == 0) result.Warnings.Add(rawHour.Length == 0
                ? $"{number}: la IA no leyó la hora. Incluí la columna y su encabezado en la captura o completala manualmente."
                : $"{number}: hora no reconocida ({rawHour}). Revisá el dato original.");
            var pe = Text(item, "premium");
            var eco = Text(item, "economy");
            var known = int.TryParse(pe, out var p) && p is >= 0 and <= 999;
            known = int.TryParse(eco, out var e) && e is >= 0 and <= 999 && known;
            result.Flights.Add(new FlightData { Vuelo = number, Destino = airport, Hora = hour,
                Equipo = Text(item, "equipment"), Premium = known ? p : 0, Economy = known ? e : 0, BookingKnown = known });
        }
        var ito = root.GetProperty("ito");
        result.Ito.Vuelo = FlightNumber(Text(ito, "flight"));
        result.Ito.Matricula = Text(ito, "registration");
        result.Ito.Configuracion = Text(ito, "configuration");
        result.Ito.Servicios = Text(ito, "services");
        foreach (var row in root.GetProperty("specials").EnumerateArray())
        {
            var identity = Text(row, "identity").ToUpperInvariant();
            if (identity.Length == 0) { result.Warnings.Add("EDIT sin identidad estable: revisá y agregalo manualmente."); continue; }
            var codes = row.GetProperty("codes").EnumerateArray().Select(x => (x.GetString() ?? "").Trim().ToUpperInvariant()).Distinct().ToList();
            if (codes.Any(c => !Regex.IsMatch(c, @"^[A-Z0-9]{3,6}$"))) throw new FormatException("La IA devolvió un código EDIT inválido.");
            result.Specials.Add(new VisionSpecialRow(identity, codes));
        }
        return result;
    }

    private static string Text(JsonElement item, string key) => (item.GetProperty(key).GetString() ?? "").Trim();
    public static string NormalizeTime(string value)
    {
        var text = value.Trim().ToUpperInvariant();
        text = Regex.Replace(text, @"^(?:ETA|ETD|STA|STD|HORA)\s*[:=]?\s*", "");
        var formats = new[] { "H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss", "H.mm", "HH.mm", "HHmm", "HHmmss", "h:mm tt", "hh:mm tt" };
        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var time))
            return time.ToString("HH:mm", CultureInfo.InvariantCulture);
        // Dates are allowed only when followed by a clearly separated clock time.
        var match = Regex.Match(text, @"(?:\s|T)(\d{1,2}:\d{2}(?::\d{2})?(?:\s+[AP]M)?)Z?$");
        if (match.Success && DateTime.TryParseExact(match.Groups[1].Value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out time))
            return time.ToString("HH:mm", CultureInfo.InvariantCulture);
        return "";
    }
    public static string FlightNumber(string value)
    {
        var compact = Regex.Replace(value.ToUpperInvariant(), @"\s+", "");
        if (!Regex.IsMatch(compact, @"^(?:[A-Z]{2})?\d{1,4}$")) return "";
        return char.IsDigit(compact[0]) ? "LA" + compact : compact;
    }
}

public sealed record VisionSpecialRow(string Identity, List<string> Codes);

public sealed class VisionSpecialAccumulator
{
    private readonly Dictionary<string, HashSet<string>> _passengers = new(StringComparer.OrdinalIgnoreCase);
    public int UniquePassengers => _passengers.Count;
    public void Add(IEnumerable<VisionSpecialRow> rows)
    {
        foreach (var row in rows)
        {
            if (!_passengers.TryGetValue(row.Identity, out var codes))
                _passengers[row.Identity] = codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            codes.UnionWith(row.Codes);
        }
    }
    public Dictionary<string, int> Counts()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var codes in _passengers.Values)
        {
            var wheelchair = codes.Contains("WCHC") ? "WCHC" : codes.Contains("WCHS") ? "WCHS" : codes.Contains("WCHR") ? "WCHR" : "";
            foreach (var code in codes.Where(c => c is not ("WCHR" or "WCHS" or "WCHC")).Concat(wheelchair.Length == 0 ? Array.Empty<string>() : new[] { wheelchair }))
                result[code] = result.GetValueOrDefault(code) + 1;
        }
        return result;
    }
}
