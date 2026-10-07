using System.Reflection;
using System.Text.RegularExpressions;

namespace AEPControl;

public static class FlightCorrections
{
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
