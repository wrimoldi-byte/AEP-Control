using System.Drawing.Imaging;
using System.Text.RegularExpressions;

namespace AEPControl;

public static class FlightColumnReader
{
    private static readonly Regex FlightRegex = new(@"\b(?<flight>\d{3,4})\b", RegexOptions.Compiled);
    private static readonly Regex OriginTokenRegex = new(@"\b(?<origin>[A-Z0-9]{3})\b", RegexOptions.Compiled);
    private static readonly Regex RawTimeRegex = new(@"\b(?<time>\d{1,2}\s*[:.]?\s*\d{2})\b", RegexOptions.Compiled);
    private static readonly Regex BookingRegex = new(@"\b(?<premium>\d{1,3})\s*[/\\|]\s*(?<economy>\d{1,3})\*?\b", RegexOptions.Compiled);

    private static readonly string[] ExpectedAepAirports =
    {
        "GRU", "SCL", "LIM", "GIG", "POA", "FLN", "BSB", "CWB", "MVD", "ASU"
    };

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

        var isDeparture = movement.Equals("Salida", StringComparison.OrdinalIgnoreCase);
        var primaryTask = ReadProfileAsync(grid, profile, isDeparture, 0);

        // Windows OCR puede ignorar o deformar un código IATA cuando queda
        // pegado al borde del recorte. Se hacen dos pasadas tanto en llegadas
        // como en salidas y se fusionan por número de vuelo.
        var paddedTask = ReadProfileAsync(grid, profile, isDeparture, 22);
        await Task.WhenAll(primaryTask, paddedTask);
        return MergeRows(primaryTask.Result, paddedTask.Result);
    }

    private static async Task<List<FlightData>> ReadProfileAsync(
        Bitmap grid,
        ColumnProfile profile,
        bool departure,
        int padding)
    {
        using var flightColumn = Crop(grid, profile.FlightLeft, profile.FlightRight, padding);
        using var airportColumn = Crop(grid, profile.AirportLeft, profile.AirportRight, padding);
        using var timeColumn = Crop(grid, profile.TimeLeft, profile.TimeRight, padding);
        using var bookingColumn = Crop(grid, profile.BookingLeft, profile.BookingRight, padding);

        var flightTask = OcrService.ReadContinuousAsync(flightColumn);
        var airportTask = OcrService.ReadContinuousAsync(airportColumn);
        var timeTask = OcrService.ReadContinuousAsync(timeColumn);
        var bookingTask = OcrService.ReadContinuousAsync(bookingColumn);
        await Task.WhenAll(flightTask, airportTask, timeTask, bookingTask);

        return departure
            ? ParseDepartureColumns(flightTask.Result, airportTask.Result, timeTask.Result, bookingTask.Result)
            : ParseArrivalColumns(flightTask.Result, airportTask.Result, timeTask.Result, bookingTask.Result);
    }

    private static List<FlightData> MergeRows(params IEnumerable<FlightData>[] readings)
    {
        var result = new Dictionary<string, FlightData>(StringComparer.OrdinalIgnoreCase);
        var airportVotes = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var incoming in readings.SelectMany(x => x))
        {
            if (!result.TryGetValue(incoming.Vuelo, out var existing))
            {
                result[incoming.Vuelo] = incoming;
                existing = incoming;
            }

            if (!string.IsNullOrWhiteSpace(incoming.Destino))
            {
                if (!airportVotes.TryGetValue(incoming.Vuelo, out var votes))
                {
                    votes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    airportVotes[incoming.Vuelo] = votes;
                }
                votes[incoming.Destino] = votes.GetValueOrDefault(incoming.Destino) + 1;
                existing.Destino = votes.OrderByDescending(item => item.Value).Select(item => item.Key).First();
            }
            if (string.IsNullOrWhiteSpace(existing.Hora) && !string.IsNullOrWhiteSpace(incoming.Hora))
                existing.Hora = incoming.Hora;
            if (!existing.BookingKnown && incoming.BookingKnown)
            {
                existing.Premium = incoming.Premium;
                existing.Economy = incoming.Economy;
                existing.BookingKnown = true;
            }
        }

        return result.Values.ToList();
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

        if (ExpectedAepAirports.Contains(raw, StringComparer.OrdinalIgnoreCase))
            return raw;

        var corrected = raw
            .Replace('1', 'I')
            .Replace('0', 'O')
            .Replace('5', 'S')
            .Replace('8', 'B')
            .Replace('6', 'G');

        if (ExpectedAepAirports.Contains(corrected, StringComparer.OrdinalIgnoreCase))
            return corrected;

        var closest = ExpectedAepAirports
            .Select(code => new { Code = code, Distance = CharacterDistance(corrected, code) })
            .Where(item => item.Distance == 1)
            .ToList();
        if (closest.Count == 1)
            return closest[0].Code;

        return null;
    }

    private static int CharacterDistance(string left, string right)
    {
        if (left.Length != right.Length) return int.MaxValue;
        var distance = 0;
        for (var i = 0; i < left.Length; i++)
            if (left[i] != right[i]) distance++;
        return distance;
    }

    private static Bitmap Crop(Bitmap source, double leftRatio, double rightRatio, int padding = 0)
    {
        var left = Math.Clamp((int)Math.Round(source.Width * leftRatio), 0, source.Width - 1);
        var right = Math.Clamp((int)Math.Round(source.Width * rightRatio), left + 1, source.Width);
        var rect = new Rectangle(left, 0, right - left, source.Height);
        using var cropped = source.Clone(rect, source.PixelFormat);
        if (padding <= 0)
            return new Bitmap(cropped);

        var result = new Bitmap(cropped.Width + padding * 2, cropped.Height + padding * 2);
        using var graphics = Graphics.FromImage(result);
        graphics.Clear(Color.White);
        graphics.DrawImageUnscaled(cropped, padding, padding);
        return result;
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
