using System.Text.RegularExpressions;

namespace AEPControl;

public sealed class FlightEditorForm : Form
{
    private readonly FlightData _flight;
    private readonly Dictionary<string, TextBox> _fields = new();
    private readonly TextBox _edits = new();
    private readonly CheckBox _confirmEmptyEdits = new() { Text = "Confirmar EDITS vacíos como cero", AutoSize = true };

    public FlightEditorForm(FlightData flight)
    {
        _flight = flight;
        Text = $"Editar {flight.Movimiento} {flight.Vuelo}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(560, 700);
        MinimumSize = Size;
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(16), AutoScroll = true };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(table, "Vuelo", nameof(FlightData.Vuelo), flight.Vuelo);
        AddField(table, "Origen / destino", nameof(FlightData.Destino), flight.Destino);
        AddField(table, "Hora (HH:mm)", nameof(FlightData.Hora), flight.Hora);
        AddField(table, "Equipo", nameof(FlightData.Equipo), flight.Equipo);
        AddField(table, "PAX (PE/ECO)", nameof(FlightData.Booking), flight.Booking);
        AddField(table, "Matrícula", nameof(FlightData.Matricula), flight.Matricula);
        AddField(table, "Configuración", nameof(FlightData.Configuracion), flight.Configuracion);
        AddField(table, "Servicios ITO", nameof(FlightData.Servicios), flight.Servicios);
        var hint = new Label { Text = "EDITS: un código y cantidad por línea. Agregá, corregí o quitá líneas. Ejemplo: WCHR 2. INF y ETO se exportan en sus columnas.", AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(0, 12, 0, 6) };
        table.Controls.Add(hint, 0, table.RowCount++);
        table.SetColumnSpan(hint, 2);
        _edits.Multiline = true;
        _edits.ScrollBars = ScrollBars.Vertical;
        _edits.AcceptsReturn = true;
        _edits.Height = 150;
        _edits.Dock = DockStyle.Fill;
        _edits.Text = CountsText(flight);
        table.Controls.Add(_edits, 0, table.RowCount++);
        table.SetColumnSpan(_edits, 2);
        table.Controls.Add(_confirmEmptyEdits, 0, table.RowCount++);
        table.SetColumnSpan(_confirmEmptyEdits, 2);
        var note = new Label { Text = "Los campos corregidos quedan protegidos frente a nuevas lecturas. Para reemplazarlos, usá «Permitir releer» en la pantalla principal.", AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(0, 8, 0, 8) };
        table.Controls.Add(note, 0, table.RowCount++);
        table.SetColumnSpan(note, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var save = new Button { Text = "Guardar", AutoSize = true };
        save.Click += (_, _) => Save();
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.AddRange(new Control[] { save, cancel });
        Controls.Add(table);
        Controls.Add(buttons);
        CancelButton = cancel;
    }

    private void AddField(TableLayoutPanel table, string label, string name, string value)
    {
        var row = table.RowCount++;
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        var box = new TextBox { Text = value, Dock = DockStyle.Fill, Margin = new Padding(3, 5, 3, 5) };
        _fields.Add(name, box);
        table.Controls.Add(box, 1, row);
    }

    private static string CountsText(FlightData flight) => string.Join(Environment.NewLine,
        FlightCorrections.GetCounts(flight).Where(p => p.Value > 0).Select(p => $"{p.Key} {p.Value}"));

    private void Save()
    {
        try
        {
            var counts = FlightCorrections.ParseCounts(_edits.Text);
            var flight = _fields[nameof(FlightData.Vuelo)].Text.Trim().ToUpperInvariant();
            if (!Regex.IsMatch(flight, @"^(?:[A-Z]{2}\s*)?\d{1,4}$")) throw new FormatException("Ingresá un vuelo válido, por ejemplo LA8035.");
            var airport = _fields[nameof(FlightData.Destino)].Text.Trim().ToUpperInvariant();
            if (airport.Length > 0 && !Regex.IsMatch(airport, @"^[A-Z]{3}$")) throw new FormatException("Usá un aeropuerto de 3 letras.");
            var hour = _fields[nameof(FlightData.Hora)].Text.Trim();
            if (hour.Length > 0 && !Regex.IsMatch(hour, @"^(?:[01]\d|2[0-3]):[0-5]\d$")) throw new FormatException("La hora debe tener formato HH:mm.");
            var booking = _fields[nameof(FlightData.Booking)].Text.Trim();
            if (booking.Length > 0 && !Regex.IsMatch(booking, @"^\d{1,3}\s*/\s*\d{1,3}$")) throw new FormatException("PAX debe ser PE/ECO, por ejemplo 7/156.");
            foreach (var pair in _fields)
            {
                var value = pair.Value.Text.Trim();
                if (pair.Key == nameof(FlightData.Vuelo)) value = flight;
                if (pair.Key == nameof(FlightData.Destino)) value = airport;
                if (pair.Key == nameof(FlightData.Booking))
                {
                    if (value == _flight.Booking) continue;
                    var numbers = value.Split('/');
                    _flight.Premium = value.Length == 0 ? 0 : int.Parse(numbers[0]);
                    _flight.Economy = value.Length == 0 ? 0 : int.Parse(numbers[1]);
                    _flight.BookingKnown = value.Length > 0;
                }
                else
                {
                    var property = typeof(FlightData).GetProperty(pair.Key)!;
                    if (Equals(property.GetValue(_flight), value)) continue;
                    property.SetValue(_flight, value);
                }
                _flight.ManualFields.Add(pair.Key);
            }
            if (_edits.Text.Replace("\r", "") != CountsText(_flight).Replace("\r", "") || _confirmEmptyEdits.Checked)
                FlightCorrections.SetCounts(_flight, counts, manual: true);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (FormatException ex) { MessageBox.Show(this, ex.Message, "Revisá los datos", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
