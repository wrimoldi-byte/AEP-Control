using System.ComponentModel;

namespace AEPControl;

public sealed class MainForm : Form
{
    private readonly BindingList<FlightData> _flights = new();
    private readonly DataGridView _grid = new();
    private readonly Label _stepTitle = new();
    private readonly Label _instruction = new();
    private readonly Label _status = new();
    private readonly Button _primaryButton = new();
    private int _activeFlightIndex = -1;

    public MainForm()
    {
        Text = "AEP Control v0.2";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1050, 600);
        Size = new Size(1180, 700);
        TopMost = true;

        _stepTitle.Dock = DockStyle.Top;
        _stepTitle.Height = 48;
        _stepTitle.Font = new Font("Segoe UI", 16, FontStyle.Bold);
        _stepTitle.Padding = new Padding(14, 10, 0, 0);

        _instruction.Dock = DockStyle.Top;
        _instruction.Height = 58;
        _instruction.Font = new Font("Segoe UI", 10);
        _instruction.Padding = new Padding(14, 6, 14, 0);

        _primaryButton.AutoSize = true;
        _primaryButton.Padding = new Padding(12, 7, 12, 7);
        _primaryButton.Click += async (_, _) => await HandlePrimaryActionAsync();

        var clearButton = new Button { Text = "Reiniciar", AutoSize = true, Padding = new Padding(10, 7, 10, 7) };
        clearButton.Click += (_, _) => ResetWorkflow();

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
        toolbar.Controls.Add(clearButton);
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

        _grid.Columns.Add(Column("Vuelo", nameof(FlightData.Vuelo), 95));
        _grid.Columns.Add(Column("Destino", nameof(FlightData.Destino), 75));
        _grid.Columns.Add(Column("Hora", nameof(FlightData.Hora), 70));
        _grid.Columns.Add(Column("Equipo", nameof(FlightData.Equipo), 65));
        _grid.Columns.Add(Column("Premium", nameof(FlightData.Premium), 70));
        _grid.Columns.Add(Column("Economy", nameof(FlightData.Economy), 75));
        _grid.Columns.Add(Column("WCHR", nameof(FlightData.WCHR), 55));
        _grid.Columns.Add(Column("WCHS", nameof(FlightData.WCHS), 55));
        _grid.Columns.Add(Column("WCHC", nameof(FlightData.WCHC), 55));
        _grid.Columns.Add(Column("AVIH", nameof(FlightData.AVIH), 55));
        _grid.Columns.Add(Column("INF", nameof(FlightData.INF), 50));
    }

    private static DataGridViewTextBoxColumn Column(string title, string property, float weight) => new()
    {
        HeaderText = title,
        DataPropertyName = property,
        FillWeight = weight
    };

    private void ResetWorkflow()
    {
        _flights.Clear();
        _activeFlightIndex = -1;
        _stepTitle.Text = "Paso 1 — Escanear vuelos de llegada";
        _instruction.Text = "Abrí en Sabre la lista de vuelos de llegada del siguiente turno. Luego tocá el botón y seleccioná solamente la tabla.";
        _primaryButton.Text = "Escanear vuelos de llegada";
        _status.Text = "Esperando la primera lectura.";
    }

    private async Task HandlePrimaryActionAsync()
    {
        if (_activeFlightIndex < 0)
            await ScanArrivalFlightsAsync();
        else
            await ScanSpecialsForActiveFlightAsync();
    }

    private async Task ScanArrivalFlightsAsync()
    {
        var text = await CaptureOcrAsync("Seleccioná la tabla de vuelos de llegada. Esc cancela.");
        if (text is null) return;

        var parsed = FlightParser.Parse(text);
        _flights.Clear();
        foreach (var flight in parsed) _flights.Add(flight);

        if (_flights.Count == 0)
        {
            _status.Text = "No detecté vuelos. Probá seleccionando una zona más ajustada.";
            return;
        }

        _activeFlightIndex = 0;
        PrepareActiveFlight();
    }

    private void PrepareActiveFlight()
    {
        var flight = _flights[_activeFlightIndex];
        _grid.ClearSelection();
        _grid.Rows[_activeFlightIndex].Selected = true;
        _grid.FirstDisplayedScrollingRowIndex = _activeFlightIndex;

        _stepTitle.Text = $"Paso 2 — Especiales del vuelo {flight.Vuelo}";
        _instruction.Text = $"Entrá en el vuelo {flight.Vuelo} en Sabre, escribí SS y abrí SusEdit. Cuando aparezca la lista de pasajeros, escaneá esa zona. Se contarán WCHR, WCHS, WCHC, AVIH e INF.";
        _primaryButton.Text = $"Escanear SS de {flight.Vuelo}";
        _status.Text = $"Vuelo {_activeFlightIndex + 1} de {_flights.Count}.";
    }

    private async Task ScanSpecialsForActiveFlightAsync()
    {
        var flight = _flights[_activeFlightIndex];
        var text = await CaptureOcrAsync($"Seleccioná la lista SS / SusEdit de {flight.Vuelo}.");
        if (text is null) return;

        var counts = SpecialParser.Parse(text);
        flight.WCHR = counts.WCHR;
        flight.WCHS = counts.WCHS;
        flight.WCHC = counts.WCHC;
        flight.AVIH = counts.AVIH;
        flight.INF = counts.INF;
        flight.EspecialesLeidos = true;
        _grid.Refresh();

        _status.Text = $"{flight.Vuelo}: WCHR {flight.WCHR}, WCHS {flight.WCHS}, WCHC {flight.WCHC}, AVIH {flight.AVIH}, INF {flight.INF}.";

        if (_activeFlightIndex + 1 < _flights.Count)
        {
            _activeFlightIndex++;
            PrepareActiveFlight();
        }
        else
        {
            _stepTitle.Text = "Especiales de llegadas completados";
            _instruction.Text = "Se procesaron todos los vuelos de llegada. Revisá y corregí cualquier cantidad antes de continuar con el próximo módulo.";
            _primaryButton.Text = "Volver a escanear último vuelo";
            _activeFlightIndex = _flights.Count - 1;
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
