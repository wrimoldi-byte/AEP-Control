using System.Reflection;
using System.Text.RegularExpressions;

namespace AEPControl;

public static class FlightCorrections
{
    public static string NormalizeManualValue(string property, string text)
    {
        var value = text.Trim();
        switch (property)
        {
            case nameof(FlightData.Vuelo):
                value = Regex.Replace(value.ToUpperInvariant(), @"\s+", "");
                if (!Regex.IsMatch(value, @"^(?:[A-Z]{2})?\d{1,4}$"))
                    throw new FormatException("Ingresá un vuelo válido, por ejemplo LA8035.");
                break;
            case nameof(FlightData.Destino):
                value = value.ToUpperInvariant();
                if (value.Length > 0 && !Regex.IsMatch(value, @"^[A-Z]{3}$"))
                    throw new FormatException("Usá un aeropuerto de 3 letras.");
                break;
            case nameof(FlightData.Hora):
                if (value.Length > 0)
                {
                    value = VisionResult.NormalizeTime(value);
                    if (value.Length == 0) throw new FormatException("Ingresá una hora válida, por ejemplo 07:05 o 0705.");
                }
                break;
            case nameof(FlightData.Booking):
                if (value.Length > 0)
                {
                    if (!Regex.IsMatch(value, @"^\d{1,3}\s*/\s*\d{1,3}$"))
                        throw new FormatException("Booking debe ser PE/ECO, por ejemplo 7/156.");
                    value = string.Join("/", value.Split('/').Select(n => int.Parse(n)));
                }
                break;
            case nameof(FlightData.Edits):
                var counts = ParseCounts(value);
                value = string.Join(" · ", counts.Where(p => p.Value > 0).Select(p => $"{p.Key} {p.Value}"));
                break;
            case nameof(FlightData.Equipo):
            case nameof(FlightData.Matricula):
            case nameof(FlightData.Configuracion):
            case nameof(FlightData.Servicios):
                break;
            default: throw new FormatException("Este campo no se puede editar.");
        }
        return value;
    }

    public static void SetManualValue(FlightData flight, string property, string text)
    {
        var value = NormalizeManualValue(property, text);
        var current = (string)typeof(FlightData).GetProperty(property)!.GetValue(flight)!;
        // An explicit zero such as WCHR 0 confirms an unread, empty EDITS cell.
        if (current == value && !(property == nameof(FlightData.Edits) && text.Trim().Length > 0 && !flight.EspecialesLeidos)) return;
        if (property == nameof(FlightData.Edits))
            SetCounts(flight, ParseCounts(value), manual: true);
        else if (property == nameof(FlightData.Booking))
        {
            var parts = value.Split('/');
            flight.Premium = value.Length == 0 ? 0 : int.Parse(parts[0]);
            flight.Economy = value.Length == 0 ? 0 : int.Parse(parts[1]);
            flight.BookingKnown = value.Length > 0;
        }
        else
        {
            if (property == nameof(FlightData.Vuelo) && flight.SourceFlight.Length == 0) flight.SourceFlight = flight.Vuelo;
            typeof(FlightData).GetProperty(property)!.SetValue(flight, value);
        }
        flight.ManualFields.Add(property);
    }

    private static readonly Dictionary<string, PropertyInfo> CountProperties = typeof(SpecialCounts)
        .GetProperties().Where(p => p.PropertyType == typeof(int) && p.CanWrite)
        .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, int> GetCounts(FlightData flight)
    {
        var result = CountProperties.Keys.ToDictionary(code => code,
            code => (int)typeof(FlightData).GetProperty(code)!.GetValue(flight)!, StringComparer.OrdinalIgnoreCase);
        foreach (var item in flight.ExtraSpecialCounts) result[item.Key] = item.Value;
        return result;
    }

    public static Dictionary<string, int> ParseCounts(string text)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in text.Split(new[] { '\r', '\n', ';', '·', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(part.Trim().ToUpperInvariant(), @"^([A-Z0-9]{3,6})\s*[:=]?\s+(\d{1,3})$");
            if (!match.Success) throw new FormatException($"EDIT inválido: {part.Trim()}. Usá un código y cantidad, por ejemplo WCHR 2.");
            var code = match.Groups[1].Value;
            var count = int.Parse(match.Groups[2].Value);
            if (!result.TryAdd(code, count)) throw new FormatException($"El código {code} está repetido.");
        }
        return result;
    }

    public static void SetCounts(FlightData flight, IReadOnlyDictionary<string, int> counts, bool manual = false)
    {
        if (!manual && flight.ManualFields.Contains(nameof(FlightData.Edits))) return;
        if (counts.Any(p => !Regex.IsMatch(p.Key, @"^[A-Z0-9]{3,6}$") || p.Value is < 0 or > 999))
            throw new FormatException("Código o cantidad de EDIT fuera de rango.");
        foreach (var code in CountProperties.Keys)
            typeof(FlightData).GetProperty(code)!.SetValue(flight, counts.GetValueOrDefault(code));
        flight.ExtraSpecialCounts.Clear();
        foreach (var pair in counts.Where(p => !CountProperties.ContainsKey(p.Key) && p.Value > 0))
            flight.ExtraSpecialCounts[pair.Key] = pair.Value;
        flight.EspecialesLeidos = true;
        if (manual) flight.ManualFields.Add(nameof(FlightData.Edits));
    }

    public static void ApplyText(FlightData flight, string property, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !flight.ManualFields.Contains(property))
            typeof(FlightData).GetProperty(property)!.SetValue(flight, value.Trim());
    }

    public static void Merge(FlightData existing, FlightData incoming)
    {
        foreach (var name in new[] { nameof(FlightData.Destino), nameof(FlightData.Hora), nameof(FlightData.Equipo),
                     nameof(FlightData.Matricula), nameof(FlightData.Configuracion), nameof(FlightData.Servicios) })
            ApplyText(existing, name, (string)typeof(FlightData).GetProperty(name)!.GetValue(incoming)!);
        if (incoming.BookingKnown && !existing.ManualFields.Contains(nameof(FlightData.Booking)))
        {
            existing.Premium = incoming.Premium;
            existing.Economy = incoming.Economy;
            existing.BookingKnown = true;
        }
    }
}
