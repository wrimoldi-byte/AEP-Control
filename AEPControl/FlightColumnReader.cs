using System.Text.RegularExpressions;

namespace AEPControl;

public static class FlightColumnReader
{
    private static readonly Regex FlightRegex = new(@"\b(?<flight>\d{3,4})\b", RegexOptions.Compiled);
    private static readonly Regex OriginRegex = new(@"\b(?<origin>[A-Z]{3})\b", RegexOptions.Compiled);
    private static readonly Regex TimeRegex = new(@"\b(?<time>(?:[01]\d|2[0-3])[0-5]\d)\b", RegexOptions.Compiled);
    private static readonly Regex BookingRegex = new(@"\b(?<premium>\d{1,3})\s*[/\\|]\s*(?<economy>\d{1,3})\*?\b", RegexOptions.Compiled);

    public static async Task<List<FlightData>> ReadAsync(Bitmap grid)
    {
        if (grid.Width < 400 || grid.Height < 100)
            return new List<FlightData>();

        // Proporciones medidas sobre la grilla Sabre: No | Aerolínea | Vuelo | Fecha |
        // Origen/Destino | Puerta | Hora | ETA | Cantidad Bkd.
        using var flightColumn = Crop(grid, 0.19, 0.31);
        using var originColumn = Crop(grid, 0.38, 0.49);
        using var timeColumn = Crop(grid, 0.54, 0.72);
        using var bookingColumn = Crop(grid, 0.80, 0.995);

        var flightText = await OcrService.ReadContinuousAsync(flightColumn);
        var originText = await OcrService.ReadContinuousAsync(originColumn);
        var timeText = await OcrService.ReadContinuousAsync(timeColumn);
        var bookingText = await OcrService.ReadContinuousAsync(bookingColumn);

        var flights = FlightRegex.Matches(Normalize(flightText))
            .Select(m => m.Groups["flight"].Value)
            .Where(IsPlausibleFlight)
            .ToList();

        var origins = OriginRegex.Matches(Normalize(originText))
            .Select(m => m.Groups["origin"].Value)
            .Where(IsPlausibleAirport)
            .ToList();

        var times = TimeRegex.Matches(Normalize(timeText))
            .Select(m => FormatTime(m.Groups["time"].Value))
            .ToList();

        var bookings = BookingRegex.Matches(Normalize(bookingText))
            .Select(m => (
                Premium: int.Parse(m.Groups["premium"].Value),
                Economy: int.Parse(m.Groups["economy"].Value)))
            .ToList();

        // Vuelo y hora son los campos mínimos para identificar una fila sin mezclarla.
        // Si no tienen la misma cantidad, descartamos esa captura completa y esperamos
        // la siguiente pantalla estable en vez de inventar asociaciones.
        if (flights.Count == 0 || flights.Count != times.Count)
            return new List<FlightData>();

        var originsAligned = origins.Count == flights.Count;
        var bookingsAligned = bookings.Count == flights.Count;
        var result = new List<FlightData>(flights.Count);

        for (var i = 0; i < flights.Count; i++)
        {
            result.Add(new FlightData
            {
                Vuelo = $"LA{flights[i]}",
                Destino = originsAligned ? origins[i] : string.Empty,
                Hora = times[i],
                Equipo = string.Empty,
                Premium = bookingsAligned ? bookings[i].Premium : 0,
                Economy = bookingsAligned ? bookings[i].Economy : 0,
                BookingKnown = bookingsAligned
            });
        }

        return result;
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

    private static bool IsPlausibleAirport(string value)
    {
        return value is not ("VUE" or "BKG" or "BKD" or "ETA" or "HOR" or "SAL" or "LLE" or "ORI");
    }

    private static string Normalize(string text)
    {
        var value = text.ToUpperInvariant();
        value = Regex.Replace(value, @"(?<=\d)[OQ](?=\d)", "0");
        value = Regex.Replace(value, @"(?<=\d)[IL](?=\d)", "1");
        return value;
    }

    private static string FormatTime(string raw) => raw.Insert(2, ":");
}
