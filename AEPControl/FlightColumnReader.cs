using System.Drawing.Imaging;
using System.Text.RegularExpressions;

namespace AEPControl;

public static class FlightColumnReader
{
    private static readonly Regex FlightRegex = new(@"\b(?<flight>\d{3,4})\b", RegexOptions.Compiled);
    private static readonly Regex OriginTokenRegex = new(@"\b(?<origin>[A-Z0-9]{3})\b", RegexOptions.Compiled);
    private static readonly Regex RawTimeRegex = new(@"\b(?<time>\d{4})\b", RegexOptions.Compiled);
    private static readonly Regex BookingRegex = new(@"\b(?<premium>\d{1,3})\s*[/\\|]\s*(?<economy>\d{1,3})\*?\b", RegexOptions.Compiled);
    private static bool _diagnosticSaved;

    private static readonly string[] PriorityOrigins = { "GRU", "SCL", "LIM", "GIG" };

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

        if (!_diagnosticSaved)
        {
            _diagnosticSaved = true;
            try { await SaveDiagnosticAsync(grid); } catch { }
        }

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

        var origins = ParseOrigins(originText);

        var times = RawTimeRegex.Matches(Normalize(timeText))
            .Select(m => NormalizeTime(m.Groups["time"].Value))
            .Where(value => value is not null)
            .Cast<string>()
            .ToList();

        var bookings = BookingRegex.Matches(Normalize(bookingText))
            .Select(m => (
                Premium: int.Parse(m.Groups["premium"].Value),
                Economy: int.Parse(m.Groups["economy"].Value)))
            .ToList();

        // La versión anterior descartaba TODA la pantalla si Booking u Hora
        // no tenían exactamente la misma cantidad de filas que Vuelo.
        // Eso hacía que llegadas quedara en cero ante un solo fallo del OCR.
        // Ahora Vuelo + Hora son la base; Origen y Booking se completan sólo
        // cuando esa columna viene alineada. El diccionario de BubbleMainForm
        // fusiona lecturas posteriores del mismo vuelo y completa lo faltante.
        if (flights.Count == 0 || times.Count == 0)
            return new List<FlightData>();

        var rowCount = Math.Min(flights.Count, times.Count);
        var originsAligned = origins.Count == flights.Count;
        var bookingsAligned = bookings.Count == flights.Count;

        var result = new List<FlightData>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            var flight = new FlightData
            {
                Vuelo = $"LA{flights[i]}",
                Destino = originsAligned ? origins[i] : string.Empty,
                Hora = times[i],
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

    private static List<string> ParseOrigins(string text)
    {
        var normalized = text.ToUpperInvariant();
        var result = new List<string>();

        foreach (Match match in OriginTokenRegex.Matches(normalized))
        {
            var raw = match.Groups["origin"].Value;
            var corrected = CorrectOrigin(raw);
            if (corrected is not null && IsPlausibleAirport(corrected))
                result.Add(corrected);
        }

        return result;
    }

    private static string? CorrectOrigin(string raw)
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

    private static string? NormalizeTime(string raw)
    {
        if (raw.Length != 4 || !raw.All(char.IsDigit)) return null;

        var hour = int.Parse(raw[..2]);
        var minute = int.Parse(raw[2..]);
        if (minute > 59) return null;

        if (hour <= 23)
            return raw.Insert(2, ":");

        var repaired = "0" + raw[1..];
        var repairedHour = int.Parse(repaired[..2]);
        return repairedHour <= 23 ? repaired.Insert(2, ":") : null;
    }
}
