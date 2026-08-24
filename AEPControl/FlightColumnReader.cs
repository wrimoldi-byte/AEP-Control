using System.Drawing.Imaging;
using System.Text.RegularExpressions;

namespace AEPControl;

public static class FlightColumnReader
{
    private static readonly Regex FlightRegex = new(@"\b(?<flight>\d{3,4})\b", RegexOptions.Compiled);
    private static readonly Regex OriginTokenRegex = new(@"\b(?<origin>[A-Z0-9]{3})\b", RegexOptions.Compiled);
    private static readonly Regex RawTimeRegex = new(@"\b(?<time>\d{1,2}\s*[:.]?\s*\d{2})\b", RegexOptions.Compiled);
    private static readonly Regex BookingRegex = new(@"\b(?<premium>\d{1,3})\s*[/\\|]\s*(?<economy>\d{1,3})\*?\b", RegexOptions.Compiled);

    private static readonly string[] PriorityOrigins = { "GRU", "SCL", "LIM", "GIG" };

    private sealed record ColumnProfile(
        double FlightLeft, double FlightRight,
        double AirportLeft, double AirportRight,
        double TimeLeft, double TimeRight,
        double BookingLeft, double BookingRight);

    // Perfil histórico de llegadas: no se toca porque ya funciona.
    private static readonly ColumnProfile ArrivalProfile = new(
        0.186, 0.282,
        0.369, 0.451,
        0.527, 0.672,
        0.764, 0.943);

    // Perfil medido sobre la grilla de SALIDAS enviada:
    // No | Aerolínea | Vuelo | Destino | Salida | Puerta | Estado | Equipo | Cantidad Bkd
    private static readonly ColumnProfile DepartureProfile = new(
        0.132, 0.208,
        0.208, 0.313,
        0.313, 0.395,
        0.812, 0.948);

    public static async Task<List<FlightData>> ReadAsync(Bitmap grid, string movement)
    {
        if (grid.Width < 400 || grid.Height < 100)
            return new List<FlightData>();

        var profile = movement.Equals("Salida", StringComparison.OrdinalIgnoreCase)
            ? DepartureProfile
            : ArrivalProfile;

        using var flightColumn = Crop(grid, profile.FlightLeft, profile.FlightRight);
        using var airportColumn = Crop(grid, profile.AirportLeft, profile.AirportRight);
        using var timeColumn = Crop(grid, profile.TimeLeft, profile.TimeRight);
        using var bookingColumn = Crop(grid, profile.BookingLeft, profile.BookingRight);

        var flightText = await OcrService.ReadContinuousAsync(flightColumn);
        var airportText = await OcrService.ReadContinuousAsync(airportColumn);
        var timeText = await OcrService.ReadContinuousAsync(timeColumn);
        var bookingText = await OcrService.ReadContinuousAsync(bookingColumn);

        return movement.Equals("Salida", StringComparison.OrdinalIgnoreCase)
            ? ParseDepartureColumns(flightText, airportText, timeText, bookingText)
            : ParseArrivalColumns(flightText, airportText, timeText, bookingText);
    }

    // Llegadas conserva su criterio histórico (v2.13): Vuelo + Hora forman la
    // fila base. Origen y Booking se completan cuando sus columnas vienen
    // alineadas. Mantener este camino separado evita que los ajustes de la
    // grilla de salidas vuelvan a romper una lectura que ya funcionaba.
    private static List<FlightData> ParseArrivalColumns(string flightText, string airportText, string timeText, string bookingText)
    {
        var flights = ParseFlights(flightText);
        var airports = ParseAirports(airportText);
        var times = ParseTimes(timeText);
        var bookings = ParseBookings(bookingText);

        if (flights.Count == 0 || times.Count == 0)
            return new List<FlightData>();

        var rowCount = Math.Min(flights.Count, times.Count);
        return BuildRows(flights, airports, times, bookings, rowCount);
    }

    // Salidas sólo necesita reconocer el número de vuelo. El resto de los
    // datos puede completarse en capturas posteriores durante el scroll.
    private static List<FlightData> ParseDepartureColumns(string flightText, string airportText, string timeText, string bookingText)
    {
        var flights = ParseFlights(flightText);
        var airports = ParseAirports(airportText);
        var times = ParseTimes(timeText);
        var bookings = ParseBookings(bookingText);

        if (flights.Count == 0)
            return new List<FlightData>();

        return BuildRows(flights, airports, times, bookings, flights.Count);
    }

    private static List<FlightData> BuildRows(
        List<string> flights,
        List<string> airports,
        List<string> times,
        List<(int Premium, int Economy)> bookings,
        int rowCount)
    {
        var airportsAligned = airports.Count == flights.Count;
        var timesAligned = times.Count == flights.Count;
        var bookingsAligned = bookings.Count == flights.Count;

        var result = new List<FlightData>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            var flight = new FlightData
            {
                Vuelo = $"LA{flights[i]}",
                Destino = airportsAligned ? airports[i] : string.Empty,
                Hora = timesAligned ? times[i] : string.Empty,
                Equipo = string.Empty,
                BookingKnown = false
            };

            if (bookingsAligned)
            {
                flight.Premium = bookings[i].Premium;
                flight.Economy = bookings[i].Economy;
                flight.BookingKnown = true;
            }

            result.Add(flight);
        }

        return result;
    }

    private static List<string> ParseFlights(string text) =>
        FlightRegex.Matches(Normalize(text))
            .Select(m => m.Groups["flight"].Value)
            .Where(IsPlausibleFlight)
            .ToList();

    private static List<string> ParseTimes(string text) =>
        RawTimeRegex.Matches(Normalize(text))
            .Select(m => NormalizeTime(m.Groups["time"].Value))
            .Where(value => value is not null)
            .Cast<string>()
            .ToList();

    private static List<(int Premium, int Economy)> ParseBookings(string text) =>
        BookingRegex.Matches(Normalize(text))
            .Select(m => (
                Premium: int.Parse(m.Groups["premium"].Value),
                Economy: int.Parse(m.Groups["economy"].Value)))
            .ToList();

    private static List<string> ParseAirports(string text)
    {
        var normalized = text.ToUpperInvariant();
        var result = new List<string>();

        foreach (Match match in OriginTokenRegex.Matches(normalized))
        {
            var raw = match.Groups["origin"].Value;
            var corrected = CorrectAirport(raw);
            if (corrected is not null && IsPlausibleAirport(corrected))
                result.Add(corrected);
        }

        return result;
    }

    private static string? CorrectAirport(string raw)
    {
        raw = raw.ToUpperInvariant();

        if (PriorityOrigins.Contains(raw, StringComparer.OrdinalIgnoreCase))
            return raw;

        var corrected = raw
            .Replace('1', 'I')
            .Replace('0', 'O')
            .Replace('5', 'S')
            .Replace('8', 'B');

        if (PriorityOrigins.Contains(corrected, StringComparer.OrdinalIgnoreCase))
            return corrected;

        if (raw is "G1G" or "GIG") return "GIG";
        if (raw is "L1M" or "LIM") return "LIM";
        if (raw is "SC1" or "SCL") return "SCL";
        if (raw is "GRU") return "GRU";

        return raw.All(char.IsLetter) ? raw : null;
    }

    private static Bitmap Crop(Bitmap source, double leftRatio, double rightRatio)
    {
        var left = Math.Clamp((int)Math.Round(source.Width * leftRatio), 0, source.Width - 1);
        var right = Math.Clamp((int)Math.Round(source.Width * rightRatio), left + 1, source.Width);
        var rect = new Rectangle(left, 0, right - left, source.Height);
        return source.Clone(rect, source.PixelFormat);
    }

    private static bool IsPlausibleFlight(string value)
    {
        if (!int.TryParse(value, out var number)) return false;
        return number is >= 100 and <= 9999;
    }

    private static bool IsPlausibleAirport(string value) =>
        value is not ("VUE" or "BKG" or "BKD" or "ETA" or "ETD" or "HOR" or "SAL" or "LLE" or "ORI" or "DES");

    private static string Normalize(string text)
    {
        var value = text.ToUpperInvariant();
        value = Regex.Replace(value, @"(?<=\d)[OQ](?=\d)", "0");
        value = Regex.Replace(value, @"(?<=\d)[IL](?=\d)", "1");
        return value;
    }

    private static string? NormalizeTime(string raw)
    {
        var digits = Regex.Replace(raw, @"\D", string.Empty);
        if (digits.Length == 3) digits = "0" + digits;
        if (digits.Length != 4 || !digits.All(char.IsDigit)) return null;

        var hour = int.Parse(digits[..2]);
        var minute = int.Parse(digits[2..]);
        if (minute > 59) return null;

        if (hour <= 23)
            return digits.Insert(2, ":");

        var repaired = "0" + digits[1..];
        var repairedHour = int.Parse(repaired[..2]);
        return repairedHour <= 23 ? repaired.Insert(2, ":") : null;
    }
}
