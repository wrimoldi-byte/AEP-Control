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

    private static void TestStitching()
    {
        using var document = new Bitmap(320, 3200);
        using (var g = Graphics.FromImage(document)) g.Clear(Color.White);
        var random = new Random(42);
        for (var y = 0; y < document.Height; y++)
            for (var n = 0; n < 35; n++) document.SetPixel(random.Next(5, 280), y, Color.Black);
        Bitmap Capture(int offset)
        {
            var image = new Bitmap(320, 300);
            using var g = Graphics.FromImage(image);
            g.Clear(Color.LightBlue);
            g.DrawImage(document, new Rectangle(0, 20, 320, 270), new Rectangle(0, offset, 320, 270), GraphicsUnit.Pixel);
            return image;
        }
        using var first = Capture(0);
        using var second = Capture(180);
        using (var joined = VisionPageStitcher.Build(new[] { first, second }))
        {
            Check(joined.Pages.Count == 1 && joined.JoinedPairs == 1 && joined.Pages[0].Height == 480,
                "Overlapping captures become one image without repeated body/header/footer");
            var exact = true;
            for (var y = 0; y < 450; y++)
                for (var x = 0; x < 296; x++)
                    if (joined.Pages[0].GetPixel(x, y + 20) != document.GetPixel(x, y)) exact = false;
            Check(exact, "Every source passenger pixel survives stitching at native resolution in order");
        }
        using var unrelated = Capture(800);
        using (var retained = VisionPageStitcher.Build(new[] { first, unrelated }))
            Check(retained.Pages.Count == 2 && retained.UncertainPairs == 1 && retained.Pages[1].Height == 300,
                "Non-overlapping captures remain intact");
        using var blank = new Bitmap(320, 300);
        using (var g = Graphics.FromImage(blank)) g.Clear(Color.White);
        using (var retained = VisionPageStitcher.Build(new[] { blank, blank }))
            Check(retained.JoinedPairs == 0, "Blank areas cannot prove passenger overlap");
        var many = Enumerable.Range(0, 15).Select(i => Capture(i * 180)).ToArray();
        try
        {
            using var split = VisionPageStitcher.Build(many);
            Check(split.Pages.Count > 1 && split.Pages.All(p => p.Height <= VisionPageStitcher.MaxJoinedHeight) && split.JoinedPairs == 14,
                "Long lists split into readable images without resizing");
            Check(split.Pages.Sum(p => p.Height) == 20 + 270 + 14 * 180 + 10, "Splitting preserves all novel rows");
        }
        finally { foreach (var page in many) page.Dispose(); }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Throws(() => VisionPageStitcher.Build(new[] { first }, cancelled.Token), "Stitching honors cancellation");
    }

    [STAThread]
    private static void Main()
    {
        TestStitching();
        var scrollDelta = typeof(BubbleMainForm).GetMethod("EditsScrollDelta", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var lines in new[] { 1, 2, 3 })
        {
            var delta = (int)scrollDelta.Invoke(null, new object[] { lines })!;
            Check(delta < 0 && (-delta / 120) * lines == 6,
                "Scroll advances six configured lines, retaining margin in a nine-row viewport: " + lines);
        }
        // Regression: a mostly white passenger table is not "unchanged" when
        // only a small text/number region changes after scrolling.
        var compareFrames = typeof(BubbleMainForm).GetMethod("FramesAreSimilar", BindingFlags.Static | BindingFlags.NonPublic)!;
        using (var before = new Bitmap(800, 400))
        using (var after = new Bitmap(800, 400))
        {
            using (var g = Graphics.FromImage(before)) g.Clear(Color.White);
            using (var g = Graphics.FromImage(after)) g.Clear(Color.White);
            Check((bool)compareFrames.Invoke(null, new object[] { before, after })!, "Identical tables end scrolling");
            using (var g = Graphics.FromImage(after)) g.FillRectangle(Brushes.Black, 40, 40, 30, 10);
            Check(!(bool)compareFrames.Invoke(null, new object[] { before, after })!, "Sparse passenger changes must not end scrolling early");
        }
        using (var full = new VisionPageBuffer())
        using (var page = new Bitmap(20, 20))
        {
            full.Observe(page, true);
            full.MarkLimitReached();
            Check(full.LimitReached && full.Pages.Count == 1, "A full batch warns even when the current page is a duplicate");
        }
        using (var limited = new VisionPageBuffer())
        using (var oversized = new Bitmap(5000, 5000))
        {
            Check(!limited.Observe(oversized, true) && limited.LimitReached, "Pixel budget rejects a pending page and signals capture must stop");
        }
        var parsed = VisionResult.Parse(Fixture());
        Check(parsed.Flights.Single().Vuelo == "LA8035" && parsed.Flights.Single().Economy == 156, "IA preserves 156 without 3/5 substitution");
        Check(parsed.Ito.Configuracion == "12/156", "ITO configuration separators preserved");
        foreach (var input in new[] { "7:05", "07:05:00", "0705", "07.05", "07/10/2026 07:05", "2026-10-07T07:05:00Z", "ETA 07:05" })
            Check(VisionResult.NormalizeTime(input) == "07:05", "Time normalized: " + input);
        Check(VisionResult.NormalizeTime("3:30 PM") == "15:30", "AM/PM time normalized to 24 hours");
        foreach (var input in new[] { "", "25:30", "12:78", "8035", "07/10/2026", "7/156" })
            Check(VisionResult.NormalizeTime(input) == "", "Not a clock time: " + input);
        Check(VisionResult.Parse(Fixture().Replace("12:30", "")).Warnings.Any(w => w.Contains("no leyó la hora")), "Missing time is reported per flight");
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

        // Exercise the actual main tables and editing controls on Windows.
        var editorFlight = new FlightData { Vuelo = "LA8035", Movimiento = "Salida", Destino = "GRU", Hora = "12:00" };
        using (var form = new BubbleMainForm())
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var departures = (System.ComponentModel.BindingList<FlightData>)typeof(BubbleMainForm).GetField("_departures", flags)!.GetValue(form)!;
            var arrivals = (System.ComponentModel.BindingList<FlightData>)typeof(BubbleMainForm).GetField("_arrivals", flags)!.GetValue(form)!;
            var grid = (DataGridView)typeof(BubbleMainForm).GetField("_departureGrid", flags)!.GetValue(form)!;
            var arrivalGrid = (DataGridView)typeof(BubbleMainForm).GetField("_arrivalGrid", flags)!.GetValue(form)!;
            departures.Add(editorFlight);
            departures.Add(new FlightData { Vuelo = "LA8033", Movimiento = "Salida" });
            arrivals.Add(new FlightData { Vuelo = "LA8032", Movimiento = "Llegada", Destino = "GRU" });
            form.Show();
            Application.DoEvents();
            void BeginCell(DataGridView target, string property, string value, int row = 0)
            {
                target.Focus();
                target.CurrentCell = target.Rows[row].Cells[property];
                Check(target.BeginEdit(true), "Begin inline " + property);
                ((TextBox)target.EditingControl!).Text = value;
                target.NotifyCurrentCellDirty(true);
            }
            void Edit(string property, string value)
            {
                BeginCell(grid, property, value);
                Check(grid.EndEdit(), "Commit inline " + property);
            }
            Check(grid.Rows[0].Cells[nameof(FlightData.Vuelo)].FormattedValue?.ToString() == "LA8035", "Unbound table displays live flight model");
            Edit(nameof(FlightData.Configuracion), "12/156");
            Check(!editorFlight.EspecialesLeidos && !editorFlight.ManualFields.Contains(nameof(FlightData.Edits)), "Editing ITO does not mark unread EDITS as zero");
            Edit(nameof(FlightData.Booking), "7 / 156");
            Edit(nameof(FlightData.Edits), "WCHS 2; INF 1; ETO 3");
            Check(editorFlight.Configuracion == "12/156" && editorFlight.Booking == "7/156" && editorFlight.WCHS == 2 && editorFlight.INF == 1,
                "Real inline table saves ITO, booking, EDITS and manual protections");
            Check(grid.Rows[0].Cells[nameof(FlightData.Edits)].FormattedValue?.ToString() == editorFlight.Edits, "Computed EDITS display reflects committed model");
            BeginCell(grid, nameof(FlightData.Booking), "900/900");
            typeof(DataGridView).GetMethod("ProcessDialogKey", flags)!.Invoke(grid, new object[] { Keys.Escape });
            Check(editorFlight.Booking == "7/156", "Cancel edit preserves previous booking");
            BeginCell(grid, nameof(FlightData.Hora), "25:70");
            Check(!FlightGridEditing.FinishEdit(grid) && editorFlight.Hora == "12:00", "Invalid time stays in cell without changing model");
            typeof(DataGridView).GetMethod("ProcessDialogKey", flags)!.Invoke(grid, new object[] { Keys.Escape });
            Check(grid.CurrentCell.ErrorText.Length == 0, "Cancel clears validation error");
            BeginCell(grid, nameof(FlightData.Edits), "WCHR 2; WCHR 3");
            Check(FlightGridEditing.FinishEdit(grid) && editorFlight.Edits == "WCHR 2; WCHR 3" && !editorFlight.ManualEditsCountsKnown,
                "Free EDITS accepts repeated codes without inventing numeric totals");
            Edit(nameof(FlightData.Edits), "  silla pendiente / revisar con puerta ✈  ");
            Check(editorFlight.Edits == "  silla pendiente / revisar con puerta ✈  " && editorFlight.WCHS == 0,
                "Free EDITS preserves arbitrary text and removes stale counts");
            FlightCorrections.SetCounts(editorFlight, new Dictionary<string, int> { ["WCHR"] = 99 });
            Check(editorFlight.Edits == "  silla pendiente / revisar con puerta ✈  ", "Automatic reads preserve free text");
            var specialText = typeof(ExcelExporter).GetMethod("SpecialText", BindingFlags.Static | BindingFlags.NonPublic)!;
            Check((string)specialText.Invoke(null, new object[] { editorFlight, true })! == editorFlight.Edits, "Excel preserves free EDITS text");
            Edit(nameof(FlightData.Edits), "WCHS 2; INF 1; ETO 3");
            BeginCell(grid, nameof(FlightData.Hora), "0705");
            typeof(DataGridView).GetMethod("ProcessDialogKey", flags)!.Invoke(grid, new object[] { Keys.Tab });
            Check(editorFlight.Hora == "07:05" && grid.CurrentCell.ColumnIndex != grid.Columns[nameof(FlightData.Hora)].Index, "Tab normalizes time, saves and advances cell");
            BeginCell(grid, nameof(FlightData.Matricula), "CC-TEST");
            typeof(DataGridView).GetMethod("ProcessDialogKey", flags)!.Invoke(grid, new object[] { Keys.Enter });
            Check(editorFlight.Matricula == "CC-TEST", "Enter commits ITO field");
            Edit(nameof(FlightData.Vuelo), "la 8141");
            Check(editorFlight.Vuelo == "LA8141" && editorFlight.SourceFlight == "LA8035", "Renaming preserves source flight identity");
            FlightCorrections.Merge(editorFlight, new FlightData { Hora = "22:00", Configuracion = "12/136", Premium = 1, Economy = 99, BookingKnown = true });
            FlightCorrections.SetCounts(editorFlight, new Dictionary<string, int> { ["WCHS"] = 99 });
            Check(editorFlight.Booking == "7/156" && editorFlight.Hora == "07:05" && editorFlight.WCHS == 2, "Inline corrections protected against subsequent reads");
            BeginCell(grid, nameof(FlightData.Edits), "WCHR 0", 1);
            Check(grid.EndEdit() && departures[1].EspecialesLeidos && departures[1].ManualFields.Contains(nameof(FlightData.Edits)), "Explicit zero confirms unread EDITS in table");
            BeginCell(arrivalGrid, nameof(FlightData.Edits), "WCHR 4; INF 2");
            Check(arrivalGrid.EndEdit() && arrivals[0].WCHR == 4 && arrivals[0].INF == 2, "Arrival EDITS edited in place");
            BeginCell(arrivalGrid, nameof(FlightData.Edits), "");
            Check(arrivalGrid.EndEdit() && arrivals[0].WCHR == 0 && arrivals[0].INF == 0, "Clearing EDITS removes previous counts");
            Check(grid.Columns[nameof(FlightData.Revision)].ReadOnly, "Review marker remains read-only");
            arrivalGrid.CurrentCell = arrivalGrid.Rows[0].Cells[nameof(FlightData.Edits)];
            typeof(DataGridView).GetMethod("OnCellDoubleClick", flags)!.Invoke(arrivalGrid,
                new object[] { new DataGridViewCellEventArgs(arrivalGrid.Columns[nameof(FlightData.Edits)].Index, 0) });
            Check(arrivalGrid.IsCurrentCellInEditMode, "Double-click starts EDITS directly in the table");
            ((TextBox)arrivalGrid.EditingControl!).Text = "lo que quiera / pendiente";
            arrivalGrid.NotifyCurrentCellDirty(true);
            Check(FlightGridEditing.FinishEdit(arrivalGrid) && arrivals[0].Edits == "lo que quiera / pendiente", "Double-click free text saves in arrivals");
            Check(Application.OpenForms.Count == 1, "Inline editing never opens another form");
            form.Close();
        }
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
        using (var pages = new VisionPageBuffer())
        using (var first = new Bitmap(20, 20))
        using (var second = new Bitmap(20, 20))
        {
            using (var graphics = Graphics.FromImage(second)) graphics.Clear(Color.White);
            Check(!pages.Observe(first) && !pages.Observe(first) && pages.Observe(first), "Scroll waits for three stable frames");
            Check(!pages.Observe(first, true) && pages.Pages.Count == 1, "Identical scroll capture is not queued twice");
            Check(!pages.Observe(second) && !pages.Observe(first) && !pages.Observe(second), "Moving screen does not produce unstable captures");
            Check(pages.Observe(second, true) && pages.Pages.Count == 2, "Explicit capture includes final page");
            var batchHandler = new FakeHandler(response, expectedImages: 2);
            using var client = new HttpClient(batchHandler);
            var batch = new GeminiVisionClient(client).ReadAsync(pages.Pages, "specials", "", "LA8035", settings, CancellationToken.None).GetAwaiter().GetResult();
            Check(batchHandler.Requests == 1 && batch.Specials.Count == 2, "Multiple scroll pages sent in one Gemini request");
            for (var i = 0; i < 12; i++)
            {
                using var frame = new Bitmap(20, 20);
                using (var graphics = Graphics.FromImage(frame)) graphics.Clear(Color.FromArgb(i + 1, 80, 120));
                pages.Observe(frame, true);
            }
            Check(pages.Pages.Count == VisionPageBuffer.MaxPages && pages.LimitReached, "Page limit preserves collected pages and reports pending content");
            using var bubble = new VisionScrollCaptureForm("LA8035", new Rectangle(0, 0, 100, 100));
            bubble.Show();
            Application.DoEvents();
            Check(!bubble.Modal, "Scroll capture window is non-modal");
            bubble.Close();
            Check(bubble.Cancelled && !bubble.Finished, "Closing scroll capture cancels without submitting");
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

    private sealed class FakeHandler(string response, HttpStatusCode status = HttpStatusCode.OK, int expectedImages = 1) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            Check(request.Headers.GetValues("x-goog-api-key").Single() == "fake-test-key" && !request.RequestUri!.Query.Contains("key"), "API key stays in header, not URL");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var parts = body.RootElement.GetProperty("contents")[0].GetProperty("parts");
            Check(parts.GetArrayLength() == expectedImages + 1, "Request contains every captured page");
            Check(parts[1].GetProperty("inlineData").GetProperty("mimeType").GetString() == "image/png" &&
                body.RootElement.GetProperty("generationConfig").GetProperty("responseSchema").GetProperty("required").GetArrayLength() == 4,
                "Capture sent directly as PNG with required JSON schema");
            return new HttpResponseMessage(status) { Content = new StringContent(response) };
        }
    }
}
