using System.ComponentModel;

namespace AEPControl;

public sealed class BubbleMainForm : Form
{
    private readonly BindingList<FlightData> _arrivals = new();
    private readonly BindingList<FlightData> _departures = new();
    private readonly DataGridView _arrivalGrid = new();
    private readonly DataGridView _departureGrid = new();
    private readonly Label _title = new();
    private readonly Label _help = new();
    private readonly Label _status = new();
    private readonly Button _action = new();
    private readonly Button _readArrivals = new();
    private readonly Button _readDepartures = new();
    private readonly Button _readDepartureOperation = new();
    private readonly Button _export = new();
    private int _stage;
    private FlightData? _activeFlight;
    private CancellationTokenSource? _cts;
    private BubbleForm? _bubble;

    public BubbleMainForm()
    {
        Text = "AEP Control v2.21";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1280, 720);
        TopMost = true;

        _title.Dock = DockStyle.Top;
        _title.Height = 48;
        _title.Font = new Font("Segoe UI", 16, FontStyle.Bold);
        _title.Padding = new Padding(14, 10, 0, 0);

        _help.Dock = DockStyle.Top;
        _help.Height = 68;
        _help.Padding = new Padding(14, 8, 14, 0);

        _action.AutoSize = true;
        _action.Padding = new Padding(12, 7, 12, 7);
        _action.Click += async (_, _) => await ActAsync();

        _readArrivals.Text = "Leer llegadas";
        _readArrivals.AutoSize = true;
        _readArrivals.Padding = new Padding(12, 7, 12, 7);
        _readArrivals.Click += async (_, _) => await ScanFlightsAsync("Llegada");

        _readDepartures.Text = "Leer salidas";
        _readDepartures.AutoSize = true;
        _readDepartures.Padding = new Padding(12, 7, 12, 7);
        _readDepartures.Click += async (_, _) => await ScanFlightsAsync("Salida");

        _readDepartureOperation.Text = "Leer datos de salida";
        _readDepartureOperation.AutoSize = true;
        _readDepartureOperation.Padding = new Padding(12, 7, 12, 7);
        _readDepartureOperation.Enabled = false;
        _readDepartureOperation.Click += async (_, _) => await ReadDepartureOperationAsync();

        _export.Text = "Exportar Excel";
        _export.AutoSize = true;
        _export.Padding = new Padding(10, 7, 10, 7);
        _export.Enabled = false;
        _export.Click += (_, _) => ExportExcel();

        var reset = new Button { Text = "Reiniciar", AutoSize = true, Padding = new Padding(10, 7, 10, 7) };
        reset.Click += (_, _) => ResetFlow();

        var readDocuments = new Button { Text = "Leer documentación de PAX", AutoSize = true, Padding = new Padding(10, 7, 10, 7) };
        readDocuments.Click += async (_, _) => await ReadPassengerDocumentsAsync();

        var configuration = new Button { Text = "Configuración", AutoSize = true, Padding = new Padding(10, 7, 10, 7) };
        configuration.Click += (_, _) => OpenConfiguration();

        _status.AutoSize = true;
        _status.Padding = new Padding(12, 11, 0, 0);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(12, 8, 12, 4), AutoScroll = true, WrapContents = true };
        bar.Controls.AddRange(new Control[] { _readArrivals, _readDepartures, _readDepartureOperation, _action, readDocuments, _export, configuration, reset, _status });

        ConfigureFlightGrid(_arrivalGrid, _arrivals, "Origen", "Hora llegada", includeDepartureOperation: false);
        ConfigureFlightGrid(_departureGrid, _departures, "Destino", "Hora salida", includeDepartureOperation: true);

        _arrivalGrid.Enter += (_, _) => _departureGrid.ClearSelection();
        _departureGrid.Enter += (_, _) => _arrivalGrid.ClearSelection();
        _arrivalGrid.SelectionChanged += (_, _) => UpdateSelectedFlightStatus();
        _departureGrid.SelectionChanged += (_, _) => UpdateSelectedFlightStatus();
        _arrivalGrid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await StartBubbleAsync(); };
        _departureGrid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await StartBubbleAsync(); };

        var arrivalBox = new GroupBox { Text = "LLEGADAS", Dock = DockStyle.Fill, Padding = new Padding(8) };
        arrivalBox.Controls.Add(_arrivalGrid);
        var departureBox = new GroupBox { Text = "SALIDAS", Dock = DockStyle.Fill, Padding = new Padding(8) };
        departureBox.Controls.Add(_departureGrid);
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        split.Panel1.Controls.Add(arrivalBox);
        split.Panel2.Controls.Add(departureBox);
        Shown += (_, _) => split.SplitterDistance = Math.Max(300, split.ClientSize.Width / 2);

        Controls.AddRange(new Control[] { split, bar, _help, _title });
        ResetFlow();
    }

    private static void ConfigureFlightGrid(DataGridView grid, object source, string airportTitle, string timeTitle, bool includeDepartureOperation)
    {
        grid.Dock = DockStyle.Fill;
        grid.AutoGenerateColumns = false;
        grid.DataSource = source;
        grid.AllowUserToAddRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Vuelo", DataPropertyName = nameof(FlightData.Vuelo) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = airportTitle, DataPropertyName = nameof(FlightData.Destino) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = timeTitle, DataPropertyName = nameof(FlightData.Hora) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Booking", DataPropertyName = nameof(FlightData.Booking) });
        if (includeDepartureOperation)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Matrícula", DataPropertyName = nameof(FlightData.Matricula) });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Config.", DataPropertyName = nameof(FlightData.Configuracion) });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "SVCS", DataPropertyName = nameof(FlightData.Servicios) });
        }
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "EDITS", DataPropertyName = nameof(FlightData.Edits) });
    }

    private IEnumerable<FlightData> AllFlights() => _arrivals.Concat(_departures);

    private void OpenConfiguration()
    {
        using var form = new ConfigurationForm();
        if (form.ShowDialog(this) == DialogResult.OK)
            _status.Text = "Configuración guardada. Los nuevos códigos se usarán en la próxima lectura de especiales.";
    }

    private void ResetFlow()
    {
        _cts?.Cancel();
        _bubble?.Close();
        _arrivals.Clear();
        _departures.Clear();
        _stage = 0;
        _activeFlight = null;
        _readArrivals.Visible = true;
        _readDepartures.Visible = true;
        _readDepartureOperation.Enabled = false;
        _action.Visible = false;
        _export.Enabled = false;
        _title.Text = "Paso 1 — Vuelos de llegada";
        _help.Text = "Marcá SOLO la grilla de Sabre: desde los encabezados No/Aerolínea/Vuelo hasta la última fila visible. La app leerá Vuelo, Origen, Hora y Booking por columnas mientras hacés scroll.";
        _status.Text = "Esperando lectura.";
        Show();
    }

    private async Task ActAsync()
    {
        await StartBubbleAsync();
    }

    private async Task ScanFlightsAsync(string movement)
    {
        Hide();
        await Task.Delay(250);
        using var selector = new SelectionForm();
        if (selector.ShowDialog() != DialogResult.OK) { _activeFlight = null; Show(); return; }

        var area = selector.SelectedArea;
        var unique = new Dictionary<string, FlightData>(StringComparer.OrdinalIgnoreCase);
        var finishRequested = false;
        _cts = new CancellationTokenSource();
        _bubble = new BubbleForm($"Vuelos de {movement.ToLowerInvariant()}", BubbleMode.FlightList);
        _bubble.FinishRequested += (_, _) => finishRequested = true;
        _bubble.Show();

        void MergeFlight(FlightData incoming)
        {
            incoming.Movimiento = movement;
            if (!unique.TryGetValue(incoming.Vuelo, out var existing))
            {
                unique[incoming.Vuelo] = incoming;
                return;
            }

            if (string.IsNullOrWhiteSpace(existing.Destino) && !string.IsNullOrWhiteSpace(incoming.Destino)) existing.Destino = incoming.Destino;
            if (string.IsNullOrWhiteSpace(existing.Hora) && !string.IsNullOrWhiteSpace(incoming.Hora)) existing.Hora = incoming.Hora;
            if (string.IsNullOrWhiteSpace(existing.Equipo) && !string.IsNullOrWhiteSpace(incoming.Equipo)) existing.Equipo = incoming.Equipo;
            if (!existing.BookingKnown && incoming.BookingKnown)
            {
                existing.Premium = incoming.Premium;
                existing.Economy = incoming.Economy;
                existing.BookingKnown = true;
            }
        }

        async Task ProcessScreenAsync(Bitmap bmp)
        {
            foreach (var flight in await FlightColumnReader.ReadAsync(bmp, movement))
                MergeFlight(flight);
        }

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                using var bmp = new Bitmap(area.Width, area.Height);
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);

                await ProcessScreenAsync(bmp);
                _bubble.UpdateFlightCount(unique.Count);

                if (finishRequested)
                {
                    for (var i = 0; i < 3; i++)
                    {
                        await Task.Delay(250);
                        using var finalBmp = new Bitmap(area.Width, area.Height);
                        using (var g = Graphics.FromImage(finalBmp)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                        await ProcessScreenAsync(finalBmp);
                        _bubble.UpdateFlightCount(unique.Count);
                    }
                    _cts.Cancel();
                    break;
                }

                await Task.Delay(900, _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "AEP Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _bubble?.Close();
            _bubble = null;
            Show();
            Activate();
        }

        var usableFlights = movement.Equals("Llegada", StringComparison.OrdinalIgnoreCase)
            ? unique.Values.Where(f => !string.IsNullOrWhiteSpace(f.Hora) && f.BookingKnown).ToList()
            : unique.Values.ToList();

        if (usableFlights.Count == 0)
        {
            _status.Text = movement.Equals("Llegada", StringComparison.OrdinalIgnoreCase)
                ? "No detecté llegadas completas con hora y booking. Marcá la grilla incluyendo ambas columnas."
                : "No detecté vuelos. Marcá sólo la grilla, desde los encabezados hasta la última fila visible.";
            return;
        }

        var target = movement.Equals("Llegada", StringComparison.OrdinalIgnoreCase)
            ? _arrivals
            : _departures;
        target.Clear();

        foreach (var flight in usableFlights.OrderBy(x => x.Hora).ThenBy(x => x.Vuelo))
            target.Add(flight);

        _export.Enabled = AllFlights().Any();
        _action.Visible = true;
        _action.Text = "Leer EDITS del vuelo seleccionado";

        if (movement.Equals("Llegada", StringComparison.OrdinalIgnoreCase))
        {
            _stage = 2;
            _title.Text = "Paso 2 — Vuelos de salida";
            _help.Text = "Llegadas guardadas. Abrí la grilla de salidas y presioná el botón fijo «Leer salidas».";
            _status.Text = $"Llegadas guardadas con hora y booking: {usableFlights.Count}. Botón «Leer salidas» disponible arriba.";
            return;
        }

        _readDepartureOperation.Enabled = true;

        PrepareSpecialSelection();
    }

    private async Task ReadDepartureOperationAsync()
    {
        if (_departures.Count == 0)
        {
            MessageBox.Show("Primero leé la lista de vuelos de salida.", "AEP Control", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _status.Text = "Seleccioná el cuadro completo del vuelo de salida: vuelo, matrícula, configuración y servicios.";
        Hide();
        await Task.Delay(250);

        using var selector = new SelectionForm();
        if (selector.ShowDialog() != DialogResult.OK)
        {
            Show(); Activate(); _status.Text = "Lectura de datos de salida cancelada."; return;
        }

        try
        {
            using var bitmap = new Bitmap(selector.SelectedArea.Width, selector.SelectedArea.Height);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(selector.SelectedArea.Location, Point.Empty, selector.SelectedArea.Size);

            var text = await OcrService.ReadAsync(bitmap);
            var data = DepartureOperationParser.Parse(text);
            var selectedDeparture = _departureGrid.CurrentRow?.DataBoundItem as FlightData;
            var target = FindDeparture(data.Vuelo);
            if (target is null && string.IsNullOrWhiteSpace(data.Vuelo))
                target = selectedDeparture;

            if (target is null)
            {
                MessageBox.Show(
                    "No pude relacionar la pantalla con un vuelo de salida. Seleccioná primero el vuelo en la grilla y repetí la captura.",
                    "AEP Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (!data.HasOperationalData)
            {
                MessageBox.Show(
                    "El OCR detectó la pantalla, pero no encontró matrícula, configuración ni servicios. Probá seleccionando el cuadro completo con un pequeño margen.",
                    "AEP Control",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (!string.IsNullOrWhiteSpace(data.Matricula)) target.Matricula = data.Matricula;
            if (!string.IsNullOrWhiteSpace(data.Configuracion)) target.Configuracion = data.Configuracion;
            if (!string.IsNullOrWhiteSpace(data.Servicios)) target.Servicios = data.Servicios;

            _departureGrid.Refresh();
            _export.Enabled = true;
            _status.Text = $"{target.Vuelo}: matrícula {Display(target.Matricula)} · configuración {Display(target.Configuracion)} · SVCS {Display(target.Servicios)}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No pude leer los datos del vuelo de salida:\n\n{ex.Message}", "AEP Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Show();
            Activate();
        }
    }

    private FlightData? FindDeparture(string flight)
    {
        if (string.IsNullOrWhiteSpace(flight)) return null;
        var digits = new string(flight.Where(char.IsDigit).ToArray());
        return _departures.FirstOrDefault(item =>
            new string(item.Vuelo.Where(char.IsDigit).ToArray()).Equals(digits, StringComparison.OrdinalIgnoreCase));
    }

    private static string Display(string value) => string.IsNullOrWhiteSpace(value) ? "sin dato" : value;

    private void PrepareSpecialSelection()
    {
        if (!AllFlights().Any())
        {
            _status.Text = "No hay vuelos para leer especiales.";
            return;
        }

        _stage = 2;
        _action.Visible = true;
        _title.Text = "Paso 3 — Especiales de llegadas y salidas";
        _help.Text = "En Salidas, usá Leer datos de salida para completar matrícula, configuración y SVCS. Para EDITS, elegí cualquier vuelo: se leerá exactamente el seleccionado, sin importar el orden.";
        _action.Text = "Leer EDITS del vuelo seleccionado";
        _status.Text = $"Listas completas: {_arrivals.Count} llegadas + {_departures.Count} salidas.";
    }

    private FlightData? GetSelectedFlight()
    {
        if (_arrivalGrid.Focused && _arrivalGrid.CurrentRow?.DataBoundItem is FlightData arrival)
            return arrival;
        if (_departureGrid.Focused && _departureGrid.CurrentRow?.DataBoundItem is FlightData departure)
            return departure;
        if (_arrivalGrid.SelectedRows.Count > 0)
            return _arrivalGrid.SelectedRows[0].DataBoundItem as FlightData;
        if (_departureGrid.SelectedRows.Count > 0)
            return _departureGrid.SelectedRows[0].DataBoundItem as FlightData;
        return null;
    }

    private void UpdateSelectedFlightStatus()
    {
        var selected = GetSelectedFlight();
        if (selected is not null)
            _status.Text = $"Seleccionado: {selected.Movimiento} {selected.Vuelo}.";
    }

    private async Task StartBubbleAsync()
    {
        var f = GetSelectedFlight();
        if (f is null)
        {
            MessageBox.Show("Seleccioná primero un vuelo de Llegadas o Salidas.", "AEP Control", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _activeFlight = f;
        Hide();
        await Task.Delay(250);
        using var selector = new SelectionForm();
        if (selector.ShowDialog() != DialogResult.OK) { _activeFlight = null; Show(); return; }

        var area = selector.SelectedArea;
        var reader = new ContinuousSpecialReader();
        _cts = new CancellationTokenSource();
        _bubble = new BubbleForm(f.Vuelo);
        _bubble.FinishRequested += (_, _) => FinishFlight();
        _bubble.Show();

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                using var bmp = new Bitmap(area.Width, area.Height);
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);

                var text = await OcrService.ReadContinuousAsync(bmp);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var c = reader.AddOcrText(text);
                    CopyCounts(f, c);
                    _arrivalGrid.Refresh();
                    _departureGrid.Refresh();
                }

                _bubble.UpdateCounts(f, reader.UniqueRows);
                await Task.Delay(900, _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "AEP Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
            FinishFlight();
        }
    }

    private static void CopyCounts(FlightData f, SpecialCounts c)
    {
        f.WCHR = c.WCHR; f.WCHS = c.WCHS; f.WCHC = c.WCHC; f.AVIH = c.AVIH; f.INF = c.INF; f.ETO = c.ETO;
        f.UMNR = c.UMNR; f.PETC = c.PETC; f.DEAF = c.DEAF; f.BLND = c.BLND; f.MAAS = c.MAAS;
        f.STCR = c.STCR; f.MEDA = c.MEDA; f.WCLB = c.WCLB; f.WCMP = c.WCMP; f.SVAN = c.SVAN;
        f.ESAN = c.ESAN; f.INAD = c.INAD; f.DEPA = c.DEPA; f.DEPU = c.DEPU;

        f.ExtraSpecialCounts.Clear();
        foreach (var item in c.Extra)
            f.ExtraSpecialCounts[item.Key] = item.Value;
    }

    private void FinishFlight()
    {
        _cts?.Cancel();
        _bubble?.Close();
        _bubble = null;
        if (_activeFlight is not null)
            _activeFlight.EspecialesLeidos = true;
        _export.Enabled = AllFlights().Any();
        Show(); Activate();
        _stage = 2;
        _title.Text = "Especiales — elegí el próximo vuelo";
        _help.Text = "Podés seleccionar ahora cualquier otro vuelo de cualquiera de las dos listas, sin seguir un orden obligatorio.";
        _action.Visible = true;
        _action.Text = "Leer EDITS del vuelo seleccionado";
        _status.Text = _activeFlight is null ? "Lectura terminada." : $"{_activeFlight.Vuelo} terminado.";
        _activeFlight = null;
    }

    private void ExportExcel()
    {
        if (!AllFlights().Any())
        {
            MessageBox.Show("Todavía no hay vuelos para exportar.", "AEP Control", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Guardar Excel de AEP Control",
            Filter = "Excel (*.xlsx)|*.xlsx",
            DefaultExt = "xlsx",
            AddExtension = true,
            FileName = $"AEP-Control-{DateTime.Now:yyyy-MM-dd}.xlsx"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            ExcelExporter.Export(dialog.FileName, AllFlights().ToList());
            _status.Text = $"Excel guardado: {Path.GetFileName(dialog.FileName)}";
            MessageBox.Show("Excel generado correctamente. Los datos no disponibles quedaron vacíos.", "AEP Control", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No pude generar el Excel:\n\n{ex.Message}", "AEP Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ReadPassengerDocumentsAsync()
    {
        _status.Text = "Seleccioná el área de documentación DOCS/OB. Cada pantalla se leerá por separado mientras hacés scroll.";
        Hide();
        await Task.Delay(250);
        using var selector = new SelectionForm();
        if (selector.ShowDialog() != DialogResult.OK)
        {
            Show(); Activate(); _status.Text = "Lectura de documentación cancelada."; return;
        }

        var area = selector.SelectedArea;
        var unique = new Dictionary<string, PassengerDocument>(StringComparer.OrdinalIgnoreCase);
        var passes = 0;
        var finishRequested = false;
        _cts = new CancellationTokenSource();
        _bubble = new BubbleForm("Documentación PAX", BubbleMode.PassengerDocuments);
        _bubble.FinishRequested += (_, _) => finishRequested = true;
        _bubble.Show();

        void ProcessScreen(string text)
        {
            foreach (var doc in PassengerDocumentParser.Parse(text))
                unique[doc.Key] = doc;
        }

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                using var bmp = new Bitmap(area.Width, area.Height);
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                var text = await OcrService.ReadContinuousAsync(bmp);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    ProcessScreen(text);
                    passes++;
                    _bubble.UpdatePassengerDocumentCount(unique.Count, passes);
                }

                if (finishRequested)
                {
                    _cts.Cancel();
                    break;
                }

                await Task.Delay(900, _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _bubble?.Close();
            _bubble = null;
            Show();
            Activate();
        }

        _status.Text = $"Documentación terminada: {unique.Count} registros únicos.";
    }
}
