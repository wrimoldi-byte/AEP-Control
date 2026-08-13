using System.Drawing.Imaging;
using System.Text.RegularExpressions;

namespace AEPControl;

public static class FlightColumnReader
{
    private static readonly Regex FlightRegex = new(@"\b(?<flight>\d{3,4})\b", RegexOptions.Compiled);
    private static readonly Regex OriginRegex = new(@"\b(?<origin>[A-Z]{3})\b", RegexOptions.Compiled);
    private static readonly Regex TimeRegex = new(@"\b(?<time>(?:[01]\d|2[0-3])[0-5]\d)\b", RegexOptions.Compiled);
    private static readonly Regex BookingRegex = new(@"\b(?<premium>\d{1,3})\s*[/\\|]\s*(?<economy>\d{1,3})\*?\b", RegexOptions.Compiled);

    // Proporciones medidas directamente sobre la grilla Sabre mostrada por el usuario.
    // Se mantienen márgenes internos para no invadir las columnas vecinas (especialmente ETA).
    private const double FlightLeft = 0.186;
    private const double FlightRight = 0.282;
    private const double OriginLeft = 0.369;
    private const double OriginRight = 0.451;
    private const double TimeLeft = 0.527;
    private const double TimeRight = 0.672;
    private const double BookingLeft = 0.764;
    private const double BookingRight = 0.943;

    public static async Task<List<FlightData>> ReadAsync(Bitmap grid)
    {
        if (grid.Width < 400 || grid.Height < 100)
            return new List<FlightData>();

        using var flightColumn = Crop(grid, FlightLeft, FlightRight);
        using var originColumn = Crop(grid, OriginLeft, OriginRight);
        using var timeColumn = Crop(grid, TimeLeft, TimeRight);
        using var bookingColumn = Crop(grid, BookingLeft, BookingRight);

        var flightText = await OcrService.ReadContinuousAsync(flightColumn);
        var originText = await OcrService.ReadContinuousAsync(originColumn);
        var timeText = await OcrService.ReadContinuousAsync(timeColumn);
        var bookingText = await OcrService.ReadContinuousAsync(bookingColumn);

        return ParseColumns(flightText, originText, timeText, bookingText);
    }

    public static async Task<string> SaveDiagnosticAsync(Bitmap grid)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "AEPControl-Diagnostico-v2");
        Directory.CreateDirectory(folder);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        grid.Save(Path.Combine(folder, $"00-grilla-{stamp}.png"), ImageFormat.Png);

        using var flightColumn = Crop(grid, FlightLeft, FlightRight);
        using var originColumn = Crop(grid, OriginLeft, OriginRight);
        using var timeColumn = Crop(grid, TimeLeft, TimeRight);
        using var bookingColumn = Crop(grid, BookingLeft, BookingRight);

        flightColumn.Save(Path.Combine(folder, $"01-vuelo-{stamp}.png"), ImageFormat.Png);
        originColumn.Save(Path.Combine(folder, $"02-origen-{stamp}.png"), ImageFormat.Png);
        timeColumn.Save(Path.Combine(folder, $"03-hora-{stamp}.png"), ImageFormat.Png);
        bookingColumn.Save(Path.Combine(folder, $"04-booking-{stamp}.png"), ImageFormat.Png);

        var flightText = await OcrService.ReadContinuousAsync(flightColumn);
        var originText = await OcrService.ReadContinuousAsync(originColumn);
        var timeText = await OcrService.ReadContinuousAsync(timeColumn);
        var bookingText = await OcrService.ReadContinuousAsync(bookingColumn);

        File.WriteAllText(
            Path.Combine(folder, $"05-ocr-{stamp}.txt"),
            $"Tamaño grilla: {grid.Width}x{grid.Height}\r\n\r\n" +
            "=== VUELO ===\r\n" + flightText + "\r\n\r\n" +
            "=== ORIGEN ===\r\n" + originText + "\r\n\r\n" +
            "=== HORA ===\r\n" + timeText + "\r\n\r\n" +
            "=== BOOKING ===\r\n" + bookingText + "\r\n");

        return folder;
    }

    private static List<FlightData> ParseColumns(string flightText, string originText, string timeText, string bookingText)
    {
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

        // Vuelo y hora identifican la fila. Si las cantidades difieren, no asociamos
        // posiciones dudosas; esperamos la próxima captura estable.
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
