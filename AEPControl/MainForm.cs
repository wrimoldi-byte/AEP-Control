using System.ComponentModel;

namespace AEPControl;

public sealed class MainForm : Form
{
    private readonly BindingList<FlightData> _flights = new();
    private readonly DataGridView _grid = new();
    private readonly Label _status = new();
    private readonly Button _captureButton = new();

    public MainForm()
    {
        Text = "AEP Control v0.1";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(920, 520);
        Size = new Size(1050, 640);
        TopMost = true;

        var title = new Label
        {
            Text = "AEP Control — Vuelos del siguiente turno",
            Dock = DockStyle.Top,
            Height = 52,
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            Padding = new Padding(14, 12, 0, 0)
        };

        _captureButton.Text = "Capturar tabla de vuelos";
        _captureButton.AutoSize = true;
        _captureButton.Padding = new Padding(10, 6, 10, 6);
        _captureButton.Click += async (_, _) => await CaptureAndReadAsync();

        var clearButton = new Button { Text = "Limpiar", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
        clearButton.Click += (_, _) => _flights.Clear();

        _status.Text = "Listo. Tocá Capturar y marcá con el mouse solo la tabla de Sabre.";
        _status.AutoSize = true;
        _status.Padding = new Padding(12, 10, 0, 0);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            Padding = new Padding(12, 8, 12, 4),
            FlowDirection = FlowDirection.LeftToRight
        };
        toolbar.Controls.Add(_captureButton);
        toolbar.Controls.Add(clearButton);
        toolbar.Controls.Add(_status);

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.DataSource = _flights;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.Columns.Add(Column("Vuelo", nameof(FlightData.Vuelo)));
        _grid.Columns.Add(Column("Destino", nameof(FlightData.Destino)));
        _grid.Columns.Add(Column("Hora", nameof(FlightData.Hora)));
        _grid.Columns.Add(Column("Equipo", nameof(FlightData.Equipo)));
        _grid.Columns.Add(Column("Premium", nameof(FlightData.Premium)));
        _grid.Columns.Add(Column("Economy", nameof(FlightData.Economy)));
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Total",
            DataPropertyName = nameof(FlightData.Total),
            ReadOnly = true,
            FillWeight = 75
        });

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(title);
    }

    private static DataGridViewTextBoxColumn Column(string title, string property) => new()
    {
        HeaderText = title,
        DataPropertyName = property,
        FillWeight = title == "Vuelo" ? 110 : 85
    };

    private async Task CaptureAndReadAsync()
    {
        try
        {
            _captureButton.Enabled = false;
            _status.Text = "Seleccioná la tabla. Esc cancela.";
            Hide();
            await Task.Delay(250);

            using var selector = new SelectionForm();
            if (selector.ShowDialog() != DialogResult.OK) return;

            using var bitmap = new Bitmap(selector.SelectedArea.Width, selector.SelectedArea.Height);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(selector.SelectedArea.Location, Point.Empty, selector.SelectedArea.Size);

            Show();
            Activate();
            _status.Text = "Leyendo pantalla con OCR…";

            var text = await OcrService.ReadAsync(bitmap);
            var parsed = FlightParser.Parse(text);

            _flights.Clear();
            foreach (var flight in parsed) _flights.Add(flight);

            _status.Text = parsed.Count > 0
                ? $"Listo: {parsed.Count} vuelos detectados. Podés corregir las celdas."
                : "No detecté filas. Probá seleccionando solo la tabla y con mayor zoom.";
        }
        catch (Exception ex)
        {
            Show();
            MessageBox.Show($"No se pudo leer la pantalla.\n\n{ex.Message}", "AEP Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = "Error durante la lectura.";
        }
        finally
        {
            Show();
            _captureButton.Enabled = true;
        }
    }
}
