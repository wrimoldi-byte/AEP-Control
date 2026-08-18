using System.ComponentModel;

namespace AEPControl;

public sealed class BubbleMainForm : Form
{
    private readonly BindingList<FlightData> _flights = new();
    private readonly List<FlightData> _batch = new();
    private readonly DataGridView _grid = new();
    private readonly Label _title = new();
    private readonly Label _help = new();
    private readonly Label _status = new();
    private readonly Button _action = new();
    private readonly Button _export = new();
    private int _stage;
    private int _index = -1;
    private bool _chooseStartFromSelection;
    private CancellationTokenSource? _cts;
    private BubbleForm? _bubble;

    public BubbleMainForm()
    {
        Text = "AEP Control v2.5";
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

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(12, 8, 12, 4) };
        bar.Controls.AddRange(new Control[] { _action, readDocuments, _export, configuration, reset, _status });

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.DataSource = _flights;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        AddColumn("Tipo", nameof(FlightData.Movimiento));
        AddColumn("Vuelo", nameof(FlightData.Vuelo));
        AddColumn("Origen / Destino", nameof(FlightData.Destino));
        AddColumn("Hora", nameof(FlightData.Hora));
        AddColumn("Booking", nameof(FlightData.Booking));
        AddColumn("Equipo", nameof(FlightData.Equipo));
        AddColumn("WCHR", nameof(FlightData.WCHR));
        AddColumn("WCHS", nameof(FlightData.WCHS));
        AddColumn("WCHC", nameof(FlightData.WCHC));
        AddColumn("AVIH", nameof(FlightData.AVIH));
        AddColumn("INF", nameof(FlightData.INF));
        AddColumn("Otros especiales", nameof(FlightData.OtrosEspeciales));

        Controls.AddRange(new Control[] { _grid, bar, _help, _title });
        ResetFlow();
    }

    private void AddColumn(string text, string property) => _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = text, DataPropertyName = property });

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
        _flights.Clear();
        _batch.Clear();
        _stage = 0;
        _index = -1;
        _chooseStartFromSelection = false;
        _action.Visible = true;
        _export.Enabled = false;
        _title.Text = "Paso 1 — Vuelos de llegada";
        _help.Text = "Marcá SOLO la grilla de Sabre: desde los encabezados No/Aerolínea/Vuelo hasta la última fila visible. La app leerá Vuelo, Origen, Hora y Booking por columnas mientras hacés scroll.";
        _action.Text = "Iniciar lectura de llegadas";
        _status.Text = "Esperando lectura.";
        Show();
    }

    private async Task ActAsync()
    {
        if (_stage == 0) await ScanFlightsAsync("Llegada", 1);
        else if (_stage == 1 || _stage == 3) await StartBubbleAsync();
        else if (_stage == 2) await ScanFlightsAsync("Salida", 3);
    }

    private async Task ScanFlightsAsync(string movement, int nextStage)
    {
        Hide();
        await Task.Delay(250);
        using var selector = new SelectionForm();
        if (selector.ShowDialog() != DialogResult.OK) { Show(); return; }

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
            foreach (var flight in await FlightColumnReader.ReadAsync(bmp))
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

        if (unique.Count == 0)
        {
            _status.Text = "No detecté vuelos. Marcá sólo la grilla, desde los encabezados hasta la última fila visible.";
            return;
        }

        _batch.Clear();
        foreach (var flight in unique.Values.OrderBy(x => x.Hora).ThenBy(x => x.Vuelo))
        {
            _flights.Add(flight);
            _batch.Add(flight);
        }

        _export.Enabled = _flights.Count > 0;
        _stage = nextStage;
        _index = 0;
        _chooseStartFromSelection = true;
        _title.Text = $"{movement}s — elegí desde qué vuelo comenzar";
        _help.Text = "Seleccioná en la tabla el vuelo desde el que querés empezar a leer especiales. Después tocá Iniciar. Desde ahí continuará automáticamente hacia abajo.";
        _action.Text = "Iniciar especiales desde vuelo seleccionado";
        _status.Text = $"Lista terminada: {_batch.Count} vuelos únicos. Elegí una fila para comenzar.";
    }

    private void PrepareFlight()
    {
        var f = _batch[_index];
        _title.Text = $"{f.Movimiento}: {f.Vuelo}";
        _help.Text = $"Entrá al vuelo {f.Vuelo}, escribí SS y abrí SusEdit. Tocá el botón y marcá la lista. La ventana se convertirá en una burbuja flotante.";
        _action.Text = $"Iniciar burbuja OCR de {f.Vuelo}";
        _status.Text = $"Vuelo {_index + 1} de {_batch.Count}.";

        var rowIndex = _flights.IndexOf(f);
        if (rowIndex >= 0)
        {
            _grid.ClearSelection();
            _grid.Rows[rowIndex].Selected = true;
            _grid.CurrentCell = _grid.Rows[rowIndex].Cells[0];
            if (rowIndex < _grid.RowCount)
                _grid.FirstDisplayedScrollingRowIndex = rowIndex;
        }
    }

    private void ApplySelectedStartFlight()
    {
        if (!_chooseStartFromSelection) return;

        var selected = _grid.SelectedRows.Count > 0
            ? _grid.SelectedRows[0].DataBoundItem as FlightData
            : _grid.CurrentRow?.DataBoundItem as FlightData;

        if (selected is not null)
        {
            var selectedIndex = _batch.IndexOf(selected);
            if (selectedIndex >= 0)
                _index = selectedIndex;
        }

        _chooseStartFromSelection = false;
        PrepareFlight();
    }

    private async Task StartBubbleAsync()
    {
        ApplySelectedStartFlight();
        if (_index < 0 || _index >= _batch.Count) return;

        var f = _batch[_index];
        Hide();
        await Task.Delay(250);
        using var selector = new SelectionForm();
        if (selector.ShowDialog() != DialogResult.OK) { Show(); return; }

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
                    _grid.Refresh();
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
        f.WCHR = c.WCHR; f.WCHS = c.WCHS; f.WCHC = c.WCHC; f.AVIH = c.AVIH; f.INF = c.INF;
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
        _batch[_index].EspecialesLeidos = true;
        _export.Enabled = _flights.Count > 0;

        if (++_index < _batch.Count)
        {
            Show(); Activate(); PrepareFlight(); return;
        }

        Show(); Activate();
        if (_stage == 1)
        {
            _stage = 2;
            _title.Text = "Llegadas completas — vuelos de salida";
            _help.Text = "Abrí ahora la lista de vuelos de salida.";
            _action.Text = "Escanear vuelos de salida";
            _status.Text = "Llegadas guardadas.";
        }
        else
        {
            _stage = 4;
            _title.Text = "Proceso completado";
            _help.Text = "Revisá los resultados de llegadas y salidas. Podés exportarlos al Excel operativo.";
            _action.Visible = false;
            _status.Text = "Lectura terminada.";
        }
    }

    private void ExportExcel()
    {
        if (_flights.Count == 0)
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
            ExcelExporter.Export(dialog.FileName, _flights.ToList());
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
            if (string.IsNullOrWhiteSpace(text)) return;
            foreach (var document in PassengerDocumentParser.Parse(text))
            {
                var key = $"{document.DocumentType}|{document.DocumentNumber}";
                unique[key] = document;
            }
        }

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                using var bmp = new Bitmap(area.Width, area.Height);
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);

                ProcessScreen(await OcrService.ReadContinuousAsync(bmp));
                passes++;
                _bubble.UpdateDocumentCount(unique.Count, passes);

                if (finishRequested)
                {
                    for (var i = 0; i < 3; i++)
                    {
                        await Task.Delay(250);
                        using var finalBmp = new Bitmap(area.Width, area.Height);
                        using (var g = Graphics.FromImage(finalBmp)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                        ProcessScreen(await OcrService.ReadContinuousAsync(finalBmp));
                        passes++;
                        _bubble.UpdateDocumentCount(unique.Count, passes);
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
            _bubble?.Close(); _bubble = null; Show(); Activate();
        }

        var documents = unique.Values.OrderBy(d => d.Surname).ThenBy(d => d.GivenNames).ToList();
        if (documents.Count == 0)
        {
            _status.Text = $"No detecté documentación después de {passes} pasadas OCR.";
            MessageBox.Show("No se encontró documentación con el formato esperado. Probá seleccionando las líneas completas.", "Documentación de PAX", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _status.Text = $"Documentación detectada: {documents.Count} PAX en {passes} pasadas OCR.";
        using var results = new PassengerDocumentsForm(documents);
        results.ShowDialog(this);
    }
}
