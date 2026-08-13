using System.Drawing.Imaging;
using System.Text.RegularExpressions;

namespace AEPControl;

public static class FlightColumnReader
{
    private static readonly Regex FlightRegex = new(@"\b(?<flight>\d{3,4})\b", RegexOptions.Compiled);
    private static readonly Regex OriginRegex = new(@"\b(?<origin>[A-Z]{3})\b", RegexOptions.Compiled);
    private static readonly Regex RawTimeRegex = new(@"\b(?<time>\d{4})\b", RegexOptions.Compiled);
    private static readonly Regex BookingRegex = new(@"\b(?<premium>\d{1,3})\s*[/\\|]\s*(?<economy>\d{1,3})\*?\b", RegexOptions.Compiled);
    private static bool _diagnosticSaved;
    private static HashSet<string> _previousSignatures = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _lastReadUtc = DateTime.MinValue;

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

        if ((DateTime.UtcNow - _lastReadUtc).TotalSeconds > 8)
            _previousSignatures.Clear();
        _lastReadUtc = DateTime.UtcNow;

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

        var parsed = ParseColumns(flightText, originText, timeText, bookingText);
        return ConfirmStableRows(parsed);
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

        // Para evitar que durante el scroll se mezclen columnas de filas distintas,
        // sólo aceptamos capturas donde las tres columnas operativas estén alineadas.
        if (flights.Count == 0 || times.Count != flights.Count || bookings.Count != flights.Count)
            return new List<FlightData>();

        var originsAligned = origins.Count == flights.Count;
        var result = new List<FlightData>(flights.Count);

        for (var i = 0; i < flights.Count; i++)
        {
            result.Add(new FlightData
            {
                Vuelo = $"LA{flights[i]}",
                Destino = originsAligned ? origins[i] : string.Empty,
                Hora = times[i],
                Equipo = string.Empty,
                Premium = bookings[i].Premium,
                Economy = bookings[i].Economy,
                BookingKnown = true
            });
        }

        return result;
    }

    private static List<FlightData> ConfirmStableRows(List<FlightData> parsed)
    {
        var current = parsed
            .Where(f => f.BookingKnown && !string.IsNullOrWhiteSpace(f.Hora))
            .ToDictionary(Signature, f => f, StringComparer.OrdinalIgnoreCase);

        var confirmed = current
            .Where(kvp => _previousSignatures.Contains(kvp.Key))
            .Select(kvp => kvp.Value)
            .ToList();

        _previousSignatures = current.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return confirmed;
    }

    private static string Signature(FlightData flight) =>
        $"{flight.Vuelo}|{flight.Hora}|{flight.Premium:000}/{flight.Economy:000}";

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

        // Error observado en Sabre/Windows OCR: 0740 puede llegar como 3740.
        var repaired = "0" + raw[1..];
        var repairedHour = int.Parse(repaired[..2]);
        return repairedHour <= 23 ? repaired.Insert(2, ":") : null;
    }
}
