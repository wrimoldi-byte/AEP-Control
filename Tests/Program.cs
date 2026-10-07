using AEPControl;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        _checks++;
        Console.WriteLine("OK: " + description);
    }
    private static void Throws(Action action, string description)
    {
        try { action(); }
        catch (Exception) { Check(true, description); return; }
        throw new Exception("Expected rejection: " + description);
    }

    [STAThread]
    private static void Main()
    {
        var parsed = VisionResult.Parse(Fixture());
        Check(parsed.Flights.Single().Vuelo == "LA8035" && parsed.Flights.Single().Economy == 156, "IA preserves 156 without 3/5 substitution");
        Check(parsed.Ito.Configuracion == "12/156", "ITO configuration separators preserved");
        Check(!VisionResult.Parse(Fixture().Replace("\"156\"", "\"unclear\"")).Flights.Single().BookingKnown, "Ambiguous booking remains unknown");
        var accumulator = new VisionSpecialAccumulator();
        accumulator.Add(parsed.Specials);
        accumulator.Add(parsed.Specials);
        accumulator.Add(new[] { new VisionSpecialRow("PAX01", new() { "WCHC" }), new VisionSpecialRow("PAX02", new() { "WCHS" }) });
        var counts = accumulator.Counts();
        Check(counts.GetValueOrDefault("WCHC") == 1 && counts.GetValueOrDefault("WCHR") == 0 && counts.GetValueOrDefault("WCHS") == 1,
            "Wheelchair priority applies only to same passenger across captures");
        Check(counts.GetValueOrDefault("INF") == 1, "Repeat capture does not double-count INF");
        var flight = new FlightData { Movimiento = "Salida", Vuelo = "LA8035", Matricula = "CC-TEST", Configuracion = "12/156" };
        FlightCorrections.SetCounts(flight, FlightCorrections.ParseCounts("WCHR 2\nINF 1\nETO 3\nABCD 4"), manual: true);
        FlightCorrections.SetCounts(flight, new Dictionary<string, int> { ["WCHR"] = 99 });
        Check(flight.WCHR == 2 && flight.INF == 1 && flight.ETO == 3 && flight.ExtraSpecialCounts["ABCD"] == 4, "OCR/IA cannot overwrite manually corrected EDITS");
        flight.ManualFields.Add(nameof(FlightData.Configuracion));
        FlightCorrections.Merge(flight, new FlightData { Configuracion = "12/136", Matricula = "CC-NEW", Premium = 7, Economy = 156, BookingKnown = true });
        Check(flight.Configuracion == "12/156" && flight.Matricula == "CC-NEW" && flight.Booking == "7/156", "Field-level protection preserves corrections and updates other fields");
        Throws(() => FlightCorrections.ParseCounts("WCHR 2\nWCHR 3"), "Duplicate EDIT code rejected");
        Throws(() => FlightCorrections.ParseCounts("WCHR -1"), "Negative EDIT count rejected");
        Throws(() => FlightCorrections.ParseCounts("WCHR 1000"), "Out-of-range EDIT count rejected");
        var response = JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = Fixture() } } } } } });
        Check(GeminiVisionClient.ParseResponse(response).Flights.Count == 1, "Gemini response parsed before applying data");
        Throws(() => GeminiVisionClient.ParseResponse(response.Replace("STOP", "MAX_TOKENS")), "Truncated Gemini response rejected");
        Throws(() => GeminiVisionClient.ParseResponse("{\"candidates\":[]}"), "Blocked/empty Gemini response rejected");

        // Exercise real Windows editor save and DPAPI, without real passenger data or API requests.
        var editorFlight = new FlightData { Vuelo = "LA8035", Movimiento = "Salida", Destino = "GRU", Hora = "12:00" };
        using (var editor = new FlightEditorForm(editorFlight))
        {
            var fields = (Dictionary<string, TextBox>)typeof(FlightEditorForm).GetField("_fields", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
            fields[nameof(FlightData.Configuracion)].Text = "12/156";
            fields[nameof(FlightData.Booking)].Text = "7/156";
            var edits = (TextBox)typeof(FlightEditorForm).GetField("_edits", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
            edits.Text = "WCHS 2\nINF 1\nETO 3";
            typeof(FlightEditorForm).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null);
        }
        Check(editorFlight.Configuracion == "12/156" && editorFlight.Booking == "7/156" && editorFlight.WCHS == 2 && editorFlight.INF == 1,
            "Real editor saves ITO, booking, EDITS and manual protections");
        var onlyIto = new FlightData { Vuelo = "LA8033" };
        using (var editor = new FlightEditorForm(onlyIto))
        {
            var fields = (Dictionary<string, TextBox>)typeof(FlightEditorForm).GetField("_fields", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
            fields[nameof(FlightData.Configuracion)].Text = "12/156";
            typeof(FlightEditorForm).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null);
        }
        Check(!onlyIto.EspecialesLeidos && !onlyIto.ManualFields.Contains(nameof(FlightData.Edits)), "Editing ITO does not claim unread EDITS are zero");
        var settings = new VisionSettings();
        settings.SetApiKey("fake-test-key");
        Check(settings.GetApiKey() == "fake-test-key" && !settings.ProtectedKey.Contains("fake-test-key"), "API key encrypted with Windows user protection");
        Throws(() => settings.ReserveRequest(), "Unconfirmed free project cannot send requests");
        settings.FreeProjectConfirmed = true;
        settings.UsageDate = DateTime.Now.ToString("yyyy-MM-dd");
        settings.RequestsToday = 100;
        Throws(() => settings.ReserveRequest(), "Daily cap stops API requests");

        var handler = new FakeHandler(response);
        using (var client = new HttpClient(handler))
        using (var bitmap = new Bitmap(40, 40))
        {
            var result = new GeminiVisionClient(client).ReadAsync(bitmap, "ito", "Salida", "LA8035", settings, CancellationToken.None).GetAwaiter().GetResult();
            Check(result.Ito.Configuracion == "12/156" && handler.Requests == 1, "Image request returns structured data in one call");
        }
        using (var client = new HttpClient(new FakeHandler("{}", HttpStatusCode.TooManyRequests)))
        using (var bitmap = new Bitmap(40, 40))
            Throws(() => new GeminiVisionClient(client).ReadAsync(bitmap, "ito", "Salida", "LA8035", settings, CancellationToken.None).GetAwaiter().GetResult(), "Quota exhaustion has no automatic retry or fallback model");

        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
        try
        {
            ExcelExporter.Export(path, new[] { editorFlight });
            using var zip = ZipFile.OpenRead(path);
            using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var xml = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            string Value(string address) => xml.Descendants(ns + "c").First(c => (string?)c.Attribute("r") == address).Value;
            Check(Value("J4") == "12/156" && Value("K4") == "7/156" && Value("M4") == "1" && Value("R4") == "3" && Value("N4").Contains("WCHS 2"),
                "Excel exports corrected ITO, booking, INF, ETO and EDITS");
        }
        finally { File.Delete(path); }
        Console.WriteLine($"PASS: {_checks} checks");
    }

    private static string Fixture() => """
        {"flights":[{"flight":"8035","airport":"GRU","time":"12:30","equipment":"A320","premium":"7","economy":"156"}],
        "ito":{"flight":"LA8035","registration":"CC-TEST","configuration":"12/156","services":"SPMLY 5"},
        "specials":[{"identity":"PAX01","codes":["WCHR"]},{"identity":"PAX03","codes":["INF"]}],"warnings":[]}
        """;

    private sealed class FakeHandler(string response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            Check(request.Headers.GetValues("x-goog-api-key").Single() == "fake-test-key" && !request.RequestUri!.Query.Contains("key"), "API key stays in header, not URL");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var parts = body.RootElement.GetProperty("contents")[0].GetProperty("parts");
            Check(parts[1].GetProperty("inlineData").GetProperty("mimeType").GetString() == "image/png" &&
                body.RootElement.GetProperty("generationConfig").GetProperty("responseSchema").GetProperty("required").GetArrayLength() == 4,
                "Capture sent directly as PNG with required JSON schema");
            return new HttpResponseMessage(status) { Content = new StringContent(response) };
        }
    }
}
