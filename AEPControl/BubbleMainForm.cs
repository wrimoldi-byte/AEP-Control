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
    private int _stage;
    private int _index = -1;
    private CancellationTokenSource? _cts;
    private BubbleForm? _bubble;

    public BubbleMainForm()
    {
        Text = "AEP Control v0.6";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1180, 700);
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

        var reset = new Button { Text = "Reiniciar", AutoSize = true, Padding = new Padding(10, 7, 10, 7) };
        reset.Click += (_, _) => ResetFlow();
        _status.AutoSize = true;
        _status.Padding = new Padding(12, 11, 0, 0);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(12, 8, 12, 4) };
        bar.Controls.AddRange(new Control[] { _action, reset, _status });

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.DataSource = _flights;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
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

        Controls.AddRange(new Control[] { _grid, bar, _help, _title });
        ResetFlow();
    }

    private void AddColumn(string text, string property) => _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = text, DataPropertyName = property });

    private void ResetFlow()
    {
        _cts?.Cancel();
        _bubble?.Close();
        _flights.Clear();
        _batch.Clear();
        _stage = 0;
        _index = -1;
        _action.Visible = true;
        _title.Text = "Paso 1 — Vuelos de llegada";
        _help.Text = "Abrí la lista de llegadas en Sabre. Marcá la tabla una vez y hacé scroll lentamente hasta el final.";
        _action.Text = "Iniciar lectura continua de llegadas";
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
        _cts = new CancellationTokenSource();
        _bubble = new BubbleForm($"Vuelos de {movement.ToLowerInvariant()}", true);
        _bubble.FinishRequested += (_, _) => _cts.Cancel();
        _bubble.Show();

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                using var bmp = new Bitmap(area.Width, area.Height);
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(area.Location, Point.Empty, area.Size);

                var text = await OcrService.ReadContinuousAsync(bmp);
                foreach (var flight in FlightParser.Parse(text))
                {
                    flight.Movimiento = movement;
                    var key = $"{flight.Vuelo}|{flight.Hora}";
                    unique[key] = flight;
                }

                _bubble.UpdateFlightCount(unique.Count);
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
            _status.Text = "No detecté vuelos. Seleccioná una zona más ajustada.";
            return;
        }

        _batch.Clear();
        foreach (var flight in unique.Values.OrderBy(x => x.Hora).ThenBy(x => x.Vuelo))
        {
            _flights.Add(flight);
            _batch.Add(flight);
        }

        _stage = nextStage;
        _index = 0;
        _status.Text = $"Lista terminada: {_batch.Count} vuelos únicos.";
        PrepareFlight();
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "\n[…]";

    private void PrepareFlight()
    {
        var f = _batch[_index];
        _title.Text = $"{f.Movimiento}: {f.Vuelo}";
        _help.Text = $"Entrá al vuelo {f.Vuelo}, escribí SS y abrí SusEdit. Tocá el botón y marcá la lista. La ventana se convertirá en una burbuja flotante.";
        _action.Text = $"Iniciar burbuja OCR de {f.Vuelo}";
        _status.Text = $"Vuelo {_index + 1} de {_batch.Count}.";
    }

    private async Task StartBubbleAsync()
    {
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
                var text = await OcrService.ReadAsync(bmp);
                var c = reader.AddOcrText(text);
                f.WCHR = c.WCHR; f.WCHS = c.WCHS; f.WCHC = c.WCHC; f.AVIH = c.AVIH; f.INF = c.INF;
                _bubble.UpdateCounts(f, reader.UniqueRows);
                await Task.Delay(650, _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "AEP Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
            FinishFlight();
        }
    }

    private void FinishFlight()
    {
        _cts?.Cancel();
        _bubble?.Close();
        _bubble = null;
        _batch[_index].EspecialesLeidos = true;

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
            _help.Text = "Revisá los resultados de llegadas y salidas.";
            _action.Visible = false;
            _status.Text = "Lectura terminada.";
        }
    }

    private async Task<string?> CaptureAsync()
    {
        try
        {
            Hide();
            await Task.Delay(250);
            using var selector = new SelectionForm();
            if (selector.ShowDialog() != DialogResult.OK) return null;
            using var bmp = new Bitmap(selector.SelectedArea.Width, selector.SelectedArea.Height);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(selector.SelectedArea.Location, Point.Empty, selector.SelectedArea.Size);
            Show(); Activate();
            _status.Text = "Leyendo pantalla con OCR y generando diagnóstico…";
            return await OcrService.ReadAsync(bmp);
        }
        catch (Exception ex)
        {
            Show();
            MessageBox.Show(ex.Message, "AEP Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
    }
}
