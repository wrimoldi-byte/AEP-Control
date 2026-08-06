using System.ComponentModel;

namespace AEPControl;

public sealed class MainForm : Form
{
    private enum WorkflowStage { ArrivalList, ArrivalSpecials, DepartureList, DepartureSpecials, Finished }

    private readonly BindingList<FlightData> _flights = new();
    private readonly List<FlightData> _currentBatch = new();
    private readonly DataGridView _grid = new();
    private readonly Label _stepTitle = new();
    private readonly Label _instruction = new();
    private readonly Label _status = new();
    private readonly Button _primaryButton = new();
    private readonly Button _stopButton = new();
    private WorkflowStage _stage;
    private int _activeFlightIndex = -1;
    private CancellationTokenSource? _continuousCts;
    private Rectangle _continuousArea;
    private ContinuousSpecialReader? _continuousReader;

    public MainForm()
    {
        Text = "AEP Control v0.3";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1080, 610);
        Size = new Size(1220, 720);
        TopMost = true;

        _stepTitle.Dock = DockStyle.Top;
        _stepTitle.Height = 48;
        _stepTitle.Font = new Font("Segoe UI", 16, FontStyle.Bold);
        _stepTitle.Padding = new Padding(14, 10, 0, 0);

        _instruction.Dock = DockStyle.Top;
        _instruction.Height = 68;
        _instruction.Font = new Font("Segoe UI", 10);
        _instruction.Padding = new Padding(14, 6, 14, 0);

        _primaryButton.AutoSize = true;
        _primaryButton.Padding = new Padding(12, 7, 12, 7);
        _primaryButton.Click += async (_, _) => await HandlePrimaryActionAsync();

        _stopButton.Text = "Finalizar lectura continua";
        _stopButton.AutoSize = true;
        _stopButton.Padding = new Padding(12, 7, 12, 7);
        _stopButton.Visible = false;
        _stopButton.Click += (_, _) => StopContinuousReading();

        var resetButton = new Button { Text = "Reiniciar", AutoSize = true, Padding = new Padding(10, 7, 10, 7) };
        resetButton.Click += (_, _) => ResetWorkflow();

        _status.AutoSize = true;
        _status.Padding = new Padding(12, 11, 0, 0);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 62,
            Padding = new Padding(12, 8, 12, 4),
            FlowDirection = FlowDirection.LeftToRight
        };
        toolbar.Controls.Add(_primaryButton);
        toolbar.Controls.Add(_stopButton);
        toolbar.Controls.Add(resetButton);
        toolbar.Controls.Add(_status);

        ConfigureGrid();
        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(_instruction);
        Controls.Add(_stepTitle);
        ResetWorkflow();
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.DataSource = _flights;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.Columns.Add(Column("Tipo", nameof(FlightData.Movimiento), 65));
        _grid.Columns.Add(Column("Vuelo", nameof(FlightData.Vuelo), 90));
        _grid.Columns.Add(Column("Destino", nameof(FlightData.Destino), 70));
        _grid.Columns.Add(Column("Hora", nameof(FlightData.Hora), 65));
        _grid.Columns.Add(Column("Equipo", nameof(FlightData.Equipo), 60));
        _grid.Columns.Add(Column("Premium", nameof(FlightData.Premium), 65));
        _grid.Columns.Add(Column("Economy", nameof(FlightData.Economy), 70));
        _grid.Columns.Add(Column("WCHR", nameof(FlightData.WCHR), 50));
        _grid.Columns.Add(Column("WCHS", nameof(FlightData.WCHS), 50));
        _grid.Columns.Add(Column("WCHC", nameof(FlightData.WCHC), 50));
        _grid.Columns.Add(Column("AVIH", nameof(FlightData.AVIH), 50));
        _grid.Columns.Add(Column("INF", nameof(FlightData.INF), 45));
    }

    private static DataGridViewTextBoxColumn Column(string title, string property, float weight) => new()
    {
        HeaderText = title,
        DataPropertyName = property,
        FillWeight = weight
    };

    private void ResetWorkflow()
    {
        _continuousCts?.Cancel();
        _flights.Clear();
        _currentBatch.Clear();
        _activeFlightIndex = -1;
        _stage = WorkflowStage.ArrivalList;
        _stopButton.Visible = false;
        _primaryButton.Visible = true;
        _stepTitle.Text = "Paso 1 — Escanear vuelos de llegada";
        _instruction.Text = "Abrí en Sabre la lista de vuelos de llegada. Tocá el botón y seleccioná solamente la tabla visible.";
        _primaryButton.Text = "Escanear vuelos de llegada";
        _status.Text = "Esperando la primera lectura.";
    }

    private async Task HandlePrimaryActionAsync()
    {
        switch (_stage)
        {
            case WorkflowStage.ArrivalList:
                await ScanFlightListAsync("Llegada", WorkflowStage.ArrivalSpecials);
                break;
            case WorkflowStage.ArrivalSpecials:
            case WorkflowStage.DepartureSpecials:
                await StartContinuousSpecialReadingAsync();
                break;
            case WorkflowStage.DepartureList:
                await ScanFlightListAsync("Salida", WorkflowStage.DepartureSpecials);
                break;
        }
    }

    private async Task ScanFlightListAsync(string movement, WorkflowStage nextStage)
    {
        var text = await CaptureOcrAsync($"Seleccioná la tabla de vuelos de {movement.ToLowerInvariant()}. Esc cancela.");
        if (text is null) return;

        var parsed = FlightParser.Parse(text);
        if (parsed.Count == 0)
        {
            _status.Text = "No detecté vuelos. Probá seleccionando una zona más ajustada.";
            return;
        }

        _currentBatch.Clear();
        foreach (var flight in parsed)
        {
            flight.Movimiento = movement;
            _flights.Add(flight);
            _currentBatch.Add(flight);
        }

        _stage = nextStage;
        _activeFlightIndex = 0;
        PrepareActiveFlight();
    }

    private void PrepareActiveFlight()
    {
        var flight = _currentBatch[_activeFlightIndex];
        var rowIndex = _flights.IndexOf(flight);
        _grid.ClearSelection();
        if (rowIndex >= 0)
        {
            _grid.Rows[rowIndex].Selected = true;
            _grid.FirstDisplayedScrollingRowIndex = rowIndex;
        }

        var movement = flight.Movimiento.ToLowerInvariant();
        _stepTitle.Text = $"Especiales de {movement} — {flight.Vuelo}";
        _instruction.Text = $"Entrá al vuelo {flight.Vuelo} en Sabre, escribí SS y abrí SusEdit. Tocá Iniciar, marcá una sola vez la zona de la lista y después desplazate lentamente hacia abajo. La app leerá WCHR, WCHS, WCHC, AVIH e INF sin repetir filas.";
        _primaryButton.Text = $"Iniciar lectura continua de {flight.Vuelo}";
        _status.Text = $"Vuelo {_activeFlightIndex + 1} de {_currentBatch.Count}.";
    }

    private async Task StartContinuousSpecialReadingAsync()
    {
        var flight = _currentBatch[_activeFlightIndex];
        _status.Text = "Marcá la zona fija donde aparece la lista SS / SusEdit.";
        Hide();
        await Task.Delay(250);

        using var selector = new SelectionForm();
        if (selector.ShowDialog() != DialogResult.OK)
        {
            Show();
            return;
        }

        _continuousArea = selector.SelectedArea;
        _continuousReader = new ContinuousSpecialReader();
        _continuousCts = new CancellationTokenSource();

        Show();
        Activate();
        _primaryButton.Visible = false;
        _stopButton.Visible = true;
        _status.Text = $"Leyendo {flight.Vuelo} en tiempo real. Hacé scroll lentamente.";

        try
        {
            while (!_continuousCts.IsCancellationRequested)
            {
                using var bitmap = new Bitmap(_continuousArea.Width, _continuousArea.Height);
                using (var graphics = Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(_continuousArea.Location, Point.Empty, _continuousArea.Size);

                var text = await OcrService.ReadAsync(bitmap);
                var counts = _continuousReader.AddOcrText(text);
                flight.WCHR = counts.WCHR;
                flight.WCHS = counts.WCHS;
                flight.WCHC = counts.WCHC;
                flight.AVIH = counts.AVIH;
                flight.INF = counts.INF;
                _grid.Refresh();
                _status.Text = $"{flight.Vuelo} — filas únicas {_continuousReader.UniqueRows} | WCHR {flight.WCHR} · WCHS {flight.WCHS} · WCHC {flight.WCHC} · AVIH {flight.AVIH} · INF {flight.INF}";

                await Task.Delay(700, _continuousCts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show($"Se detuvo la lectura continua.\n\n{ex.Message}", "AEP Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StopContinuousReading()
    {
        _continuousCts?.Cancel();
        var flight = _currentBatch[_activeFlightIndex];
        flight.EspecialesLeidos = true;
        _stopButton.Visible = false;
        _primaryButton.Visible = true;

        if (_activeFlightIndex + 1 < _currentBatch.Count)
        {
            _activeFlightIndex++;
            PrepareActiveFlight();
            return;
        }

        if (_stage == WorkflowStage.ArrivalSpecials)
        {
            _stage = WorkflowStage.DepartureList;
            _currentBatch.Clear();
            _activeFlightIndex = -1;
            _stepTitle.Text = "Llegadas completadas — Paso siguiente: vuelos de salida";
            _instruction.Text = "Abrí ahora en Sabre la lista de vuelos de salida. Después escanearemos la tabla y entraremos uno por uno para leer SS / SusEdit del mismo modo.";
            _primaryButton.Text = "Escanear vuelos de salida";
            _status.Text = "Todos los vuelos de llegada quedaron guardados.";
        }
        else
        {
            _stage = WorkflowStage.Finished;
            _stepTitle.Text = "Llegadas y salidas completadas";
            _instruction.Text = "Se leyeron los vuelos de llegada y salida, con sus cantidades de WCHR, WCHS, WCHC, AVIH e INF. Revisá la tabla antes de continuar con nuevos edits.";
            _primaryButton.Visible = false;
            _status.Text = "Proceso inicial terminado.";
        }
    }

    private async Task<string?> CaptureOcrAsync(string message)
    {
        try
        {
            _primaryButton.Enabled = false;
            _status.Text = message;
            Hide();
            await Task.Delay(250);

            using var selector = new SelectionForm();
            if (selector.ShowDialog() != DialogResult.OK) return null;

            using var bitmap = new Bitmap(selector.SelectedArea.Width, selector.SelectedArea.Height);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(selector.SelectedArea.Location, Point.Empty, selector.SelectedArea.Size);

            Show();
            Activate();
            _status.Text = "Leyendo pantalla con OCR…";
            return await OcrService.ReadAsync(bitmap);
        }
        catch (Exception ex)
        {
            Show();
            MessageBox.Show($"No se pudo leer la pantalla.\n\n{ex.Message}", "AEP Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = "Error durante la lectura.";
            return null;
        }
        finally
        {
            Show();
            _primaryButton.Enabled = true;
        }
    }
}
